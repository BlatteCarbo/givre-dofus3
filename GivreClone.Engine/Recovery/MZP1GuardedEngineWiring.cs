using GivreClone.Protocol;
using GivreCore.Mitm;
using GivreCore.Protobuf;

namespace GivreClone.Engine.Recovery;

/// <summary>Fail-closed admission outcome; no identifier, frame or account data.</summary>
public enum MZP1Admission
{
    Ready,
    MissingJournal,
    MissingIdentityAuthority,
    MissingResponseAuthority,
    SameAuthority,
    IdentityUnverified,
    IdentityMismatch,
    JournalRejected,
    Revoked
}

public enum MZP1CaptureResult
{
    Accepted,
    Blocked,
    MissingProcessProof,
    MismatchedSession
}

/// <summary>The existing concrete BotContext and BehaviorHost, wired to a protected IActionSender.</summary>
public sealed class MZP1GuardedEngineWiring
{
    private readonly int _connectionId;
    private readonly MZP0SessionIdentity? _admittedIdentity;
    private readonly MZP0GuardedActionSender? _guard;
    private readonly ProtocolCatalog _catalog;
    private readonly object _gate = new();
    private MZP1Admission _admission;
    // State folding is serialized by _gate. Fanout MUST follow that same
    // order, but user-supplied callbacks cannot execute while _gate is held.
    // Exactly one drainer owns this bounded queue; reentrant callbacks enqueue
    // behind the current message instead of recursively publishing it.
    private const int MaxPendingPublications = 1024;
    private readonly Queue<BotMessage> _pendingPublications = new();
    private bool _publicationDraining;

    internal MZP1GuardedEngineWiring(int connectionId, ProtocolCatalog catalog,
        GameState state, PacketBus bus, RequestCorrelator correlator,
        IActionSender actions, MZP0GuardedActionSender? guard,
        MZP0SessionIdentity? admittedIdentity, MZP1Admission admission)
    {
        _connectionId = connectionId;
        _catalog = catalog;
        _guard = guard;
        _admittedIdentity = admittedIdentity;
        _admission = admission;
        State = state;
        Bus = bus;
        Correlator = correlator;
        Actions = actions;
        Context = new BotContext(state, actions, bus);
        Behaviors = new BehaviorHost(Context);
    }

    public GameState State { get; }
    public int ConnectionId => _connectionId;
    public PacketBus Bus { get; }
    public RequestCorrelator Correlator { get; }
    public IActionSender Actions { get; }
    public BotContext Context { get; }
    public BehaviorHost Behaviors { get; }

    /// <summary>Recheck the live authority on every status query, fail closed on drift.</summary>
    public MZP1Admission Admission
    {
        get
        {
            bool stopBehaviors = false;
            MZP1Admission current;
            lock (_gate)
            {
                if (_admission == MZP1Admission.Ready && _guard?.IsAuthenticated != true)
                    stopBehaviors = RevokeUnsafe();
                current = _admission;
            }
            if (stopBehaviors) Behaviors.Stop();
            return current;
        }
    }

    /// <summary>
    /// Explicit ingress seam corresponding to BotSession.Dispatch(CapturedMessage).
    /// Caller must use actual capture events with independently proven origin. This
    /// method rejects unknown process ID; connection ID alone cannot attest identity.
    /// No automatic subscription to a live proxy occurs here.
    /// </summary>
    public MZP1CaptureResult TryDispatchCaptured(CapturedMessage capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        // Refresh admission before taking the ingress lock, so cancellation of a
        // running behavior never occurs under the same lock as message delivery.
        if (Admission != MZP1Admission.Ready) return MZP1CaptureResult.Blocked;
        bool stopBehaviors = false;
        bool startDraining = false;
        MZP1CaptureResult result;
        lock (_gate)
        {
            if (_admission != MZP1Admission.Ready || _guard is null || _admittedIdentity is null)
                return MZP1CaptureResult.Blocked;
            if (capture.ProcessId == 0)
                return MZP1CaptureResult.MissingProcessProof;
            if (capture.ConnectionId != _connectionId ||
                capture.ProcessId != (uint)_admittedIdentity.Value.ProcessId)
                return MZP1CaptureResult.MismatchedSession;

            // Preserve BotSession's real state folding, mapping and packet fanout.
            // The independent verifier can acknowledge only a matching server response.
            var message = new BotMessage(capture.Seq, capture.Direction,
                capture.ConnectionId, capture.Code, _catalog.NameForCode(capture.Code),
                capture.Payload, capture.RawFrame);
            try
            {
                _guard.TryAcknowledgeVerifiedInbound(message);
                if (!_guard.IsAuthenticated)
                {
                    stopBehaviors = RevokeUnsafe();
                    result = MZP1CaptureResult.Blocked;
                }
                else
                {
                    State.ApplyGuardedInbound(message, _catalog);
                    // The real capture carries raw protocol bytes and potentially
                    // account fields; behaviors receive a categorical projection.
                    var projected = message with
                    {
                        Payload = Array.Empty<ProtoField>(), RawFrame = Array.Empty<byte>()
                    };
                    // A blocked observer must not accumulate unbounded frames
                    // or cause later ACKs to overtake earlier state updates.
                    // Ambiguous overload revokes this generation rather than
                    // silently dropping a potentially authoritative packet.
                    if (_pendingPublications.Count >= MaxPendingPublications)
                    {
                        stopBehaviors = RevokeUnsafe();
                        result = MZP1CaptureResult.Blocked;
                    }
                    else
                    {
                        _pendingPublications.Enqueue(projected);
                        if (!_publicationDraining)
                        {
                            _publicationDraining = true;
                            startDraining = true;
                        }
                        result = MZP1CaptureResult.Accepted;
                    }
                }
            }
            catch
            {
                stopBehaviors = RevokeUnsafe();
                // Never expose the untrusted decoder/transport exception message.
                result = MZP1CaptureResult.Blocked;
            }
        }
        if (stopBehaviors) Behaviors.Stop();
        // No nested publication and no callbacks under the admission lock.
        // A concurrent accepted capture may return while the first subscriber
        // is still working; its packet remains queued for the sole drainer.
        if (startDraining) DrainPublications();
        return result;
    }

    private void DrainPublications()
    {
        while (true)
        {
            BotMessage? next;
            bool stopBehaviors = false;
            lock (_gate)
            {
                if (_admission == MZP1Admission.Ready && _guard?.IsAuthenticated != true)
                    stopBehaviors = RevokeUnsafe();

                if (_admission != MZP1Admission.Ready || _pendingPublications.Count == 0)
                {
                    _pendingPublications.Clear();
                    _publicationDraining = false;
                    next = null;
                }
                else next = _pendingPublications.Dequeue();
            }
            if (stopBehaviors) Behaviors.Stop();
            if (next is null) return;
            // A message that passed this final dequeue/admission gate may
            // still enter or finish subscriber callbacks if Revoke races
            // immediately afterwards. Queued successors are discarded.
            // PacketBus isolates subscriber exceptions; do not hold _gate here.
            try { Bus.Publish(next); }
            catch
            {
                // Unexpected bus failure cannot leave _publicationDraining
                // permanently armed with undelivered authoritative events.
                Revoke();
                return;
            }
        }
    }

    public void Revoke()
    {
        bool stopBehaviors;
        lock (_gate) stopBehaviors = RevokeUnsafe();
        if (stopBehaviors) Behaviors.Stop();
    }

    private bool RevokeUnsafe()
    {
        if (_admission == MZP1Admission.Revoked) return false;
        _guard?.Revoke();
        State.ClearVerifiedSession();
        _pendingPublications.Clear();
        _admission = MZP1Admission.Revoked;
        return true;
    }
}

/// <summary>
/// Creates the real engine object graph currently assembled privately in BotSession.
/// For absent or non-independent authorities, actions reject immediately; no fallback
/// to ActionSender is permitted. The host owns the journal and must close it on shutdown.
/// </summary>
public static class MZP1GuardedEngineWiringFactory
{
    public static MZP1GuardedEngineWiring Create(
        int expectedConnectionId,
        string requiredProtocolVersion,
        ProtocolCatalog catalog,
        IFrameSink sink,
        IPacer pacer,
        MZP0RecoveryJournal? journal,
        IMZP0AuthenticatedSessionSource? identityAuthority,
        IMZP0PositiveResponseVerifier? responseAuthority)
    {
        if (expectedConnectionId <= 0) throw new ArgumentOutOfRangeException(nameof(expectedConnectionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredProtocolVersion);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(pacer);

        var state = new GameState();
        var bus = new PacketBus();
        var correlator = new RequestCorrelator();
        var admission = Preflight(expectedConnectionId, requiredProtocolVersion,
            journal, identityAuthority, responseAuthority, out var identity);

        MZP0GuardedActionSender? guarded = null;
        IActionSender actions;
        if (admission == MZP1Admission.Ready)
        {
            try
            {
                var boundResponses = new MZP1BoundResponseAuthority(
                    identityAuthority!, responseAuthority!, expectedConnectionId, requiredProtocolVersion);
                guarded = new MZP0GuardedActionSender(catalog, sink, bus, pacer,
                    correlator, journal!, identityAuthority!, boundResponses);
                var activated = guarded.ActivateAuthenticatedSession();
                if (activated.Identity != identity || !guarded.IsAuthenticated)
                {
                    guarded.Revoke();
                    admission = MZP1Admission.IdentityMismatch;
                    guarded = null;
                }
            }
            catch
            {
                guarded?.Revoke();
                guarded = null;
                admission = MZP1Admission.JournalRejected;
            }
        }

        actions = guarded is not null ? guarded : new MZP1DeniedActionSender(admission);
        return new MZP1GuardedEngineWiring(expectedConnectionId, catalog,
            state, bus, correlator, actions, guarded,
            guarded is null ? null : identity, admission);
    }

    private static MZP1Admission Preflight(int connectionId, string protocol,
        MZP0RecoveryJournal? journal,
        IMZP0AuthenticatedSessionSource? authority,
        IMZP0PositiveResponseVerifier? verifier,
        out MZP0SessionIdentity identity)
    {
        identity = default;
        if (journal is null) return MZP1Admission.MissingJournal;
        if (authority is null) return MZP1Admission.MissingIdentityAuthority;
        if (verifier is null) return MZP1Admission.MissingResponseAuthority;
        if (ReferenceEquals(authority, verifier)) return MZP1Admission.SameAuthority;
        bool authenticated;
        try { authenticated = authority.TryGetAuthenticatedIdentity(out identity); }
        catch { return MZP1Admission.IdentityUnverified; }
        if (!authenticated) return MZP1Admission.IdentityUnverified;
        if (identity.ProcessId <= 0 || identity.ProcessStartUtcTicks <= 0 ||
            identity.ConnectionId != connectionId ||
            !string.Equals(identity.ProtocolVersion, protocol, StringComparison.Ordinal))
            return MZP1Admission.IdentityMismatch;
        return MZP1Admission.Ready;
    }

    private sealed class MZP1DeniedActionSender(MZP1Admission reason) : IActionSender
    {
        public Task<ActionResult> SendRequestAsync(string clearName,
            Action<PayloadBuilder> build, CancellationToken ct = default)
            => Task.FromException<ActionResult>(new MZP1AdmissionException(reason));

        public Task<BotMessage> WaitForEventAsync(string clearEventName,
            TimeSpan timeout, CancellationToken ct = default)
            => Task.FromException<BotMessage>(new MZP1AdmissionException(reason));
    }

    /// <summary>Preserves independent server-response evidence and checks source again
    /// after the verifier returns, closing the obvious identity-loss-during-verification
    /// gap. Full atomic provenance requires a future trusted producer/session lease.</summary>
    private sealed class MZP1BoundResponseAuthority(
        IMZP0AuthenticatedSessionSource source,
        IMZP0PositiveResponseVerifier verifier,
        int connectionId,
        string protocolVersion) : IMZP0PositiveResponseVerifier
    {
        public bool IsVerifiedPositiveResponse(BotMessage inbound,
            MZP0SessionGeneration expectedGeneration, int expectedUid)
        {
            if (!verifier.IsVerifiedPositiveResponse(inbound, expectedGeneration, expectedUid))
                return false;
            return source.TryGetAuthenticatedIdentity(out var current) &&
                   current == expectedGeneration.Identity &&
                   current.ConnectionId == connectionId &&
                   string.Equals(current.ProtocolVersion, protocolVersion, StringComparison.Ordinal);
        }
    }
}

public sealed class MZP1AdmissionException(MZP1Admission reason)
    : InvalidOperationException($"Protected engine actions blocked ({reason}).")
{
    public MZP1Admission Reason { get; } = reason;
}
