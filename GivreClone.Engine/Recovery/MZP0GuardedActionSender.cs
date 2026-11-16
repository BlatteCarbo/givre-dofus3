using GivreClone.Protocol;
using GivreCore.Mitm;
using GivreCore.Protobuf;

namespace GivreClone.Engine.Recovery;

/// <summary>
/// Implemented by a trusted session-origin adapter, NOT by a self-reported process ID.
/// It must verify PID birth, connection binding and exact protocol version against its
/// own observation on every call. Null, stale or unverifiable source blocks all sends.
/// No GivreClone production adapter is assumed to exist yet.
/// </summary>
public interface IMZP0AuthenticatedSessionSource
{
    bool TryGetAuthenticatedIdentity(out MZP0SessionIdentity identity);
}

/// <summary>
/// Independent inbound authority. It must verify response envelope (not event), inbound
/// direction, trusted capture origin, correlation UID and explicit positive server result.
/// A matching UID or generic event alone is insufficient. No built-in live verifier exists.
/// </summary>
public interface IMZP0PositiveResponseVerifier
{
    bool IsVerifiedPositiveResponse(BotMessage inbound, MZP0SessionGeneration expectedGeneration, int expectedUid);
}

public enum MZP0GuardFailure
{
    SessionNotAuthenticated,
    SessionChanged,
    InvalidEnvelope,
    JournalRejected,
    TransportUncertain,
    SenderFailure
}

/// <summary>No payload, raw frames, identifiers of accounts or underlying exception data.</summary>
public sealed class MZP0GuardedActionException(MZP0GuardFailure failure)
    : InvalidOperationException($"Protected action rejected ({failure}).")
{
    public MZP0GuardFailure Failure { get; } = failure;
}

/// <summary>
/// Real IActionSender implementation using the unmodified ActionSender and a guarded
/// IFrameSink. The sink learns the REAL UID after ActionSender has encoded its envelope,
/// persists intent + dispatch window BEFORE delegating exactly once to the real sink.
/// An ordinary IActionSender decorator would see UID only after transmission, too late
/// for safe write-ahead journaling. No automatic replay or synthesized ACK is provided.
/// </summary>
public sealed class MZP0GuardedActionSender : IActionSender
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _sendSequence = new(1, 1);
    private readonly MZP0RecoveryJournal _journal;
    private readonly ProtocolCatalog _catalog;
    private readonly IMZP0AuthenticatedSessionSource _source;
    private readonly IMZP0PositiveResponseVerifier _responses;
    private readonly ActionSender _sender;
    private MZP0SessionGeneration? _generation;
    private string? _sendingClearName;
    private CancellationToken _sendingToken;
    private bool _revoked;

    public MZP0GuardedActionSender(
        ProtocolCatalog catalog,
        IFrameSink transportSink,
        IPacketBus bus,
        IPacer pacer,
        RequestCorrelator correlator,
        MZP0RecoveryJournal journal,
        IMZP0AuthenticatedSessionSource authenticatedSource,
        IMZP0PositiveResponseVerifier verifiedResponses)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(transportSink);
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(pacer);
        ArgumentNullException.ThrowIfNull(correlator);
        _catalog = catalog;
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _source = authenticatedSource ?? throw new ArgumentNullException(nameof(authenticatedSource));
        _responses = verifiedResponses ?? throw new ArgumentNullException(nameof(verifiedResponses));
        if (ReferenceEquals(_source, _responses))
            throw new ArgumentException("Origin and response verification must be independent authorities.");

        _sender = new ActionSender(catalog, new MZP0WriteAheadSink(this, transportSink),
            bus, pacer, correlator);
    }

    /// <summary>
    /// Called once after an external session-origin adapter has independently authenticated
    /// the session. Never auto-activates after an identity change; create a new sender,
    /// matching fresh correlator and journal activation for the next session.
    /// </summary>
    public MZP0SessionGeneration ActivateAuthenticatedSession()
    {
        lock (_gate)
        {
            if (_revoked || _generation is not null)
                throw new MZP0GuardedActionException(MZP0GuardFailure.SessionChanged);
            if (!TryAuthenticatedIdentity(out var identity))
                throw new MZP0GuardedActionException(MZP0GuardFailure.SessionNotAuthenticated);
            if (_journal.ActiveGeneration is not null)
                throw new MZP0GuardedActionException(MZP0GuardFailure.SessionChanged);

            try { return (_generation = _journal.Activate(identity)).Value; }
            catch
            {
                _revoked = true;
                throw new MZP0GuardedActionException(MZP0GuardFailure.JournalRejected);
            }
        }
    }

    public bool IsAuthenticated
    {
        get
        {
            lock (_gate)
                return !_revoked && _generation is { } generation && IsStillAuthenticated(generation);
        }
    }

    /// <summary>Intentionally exposes only categorical metadata, never raw requests/frames.</summary>
    public IReadOnlyList<MZP0JournalRecord> JournalStatus => _journal.Records;

    public async Task<ActionResult> SendRequestAsync(
        string clearName, Action<PayloadBuilder> build, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clearName);
        ArgumentNullException.ThrowIfNull(build);
        // Serialize throughout pacer+encoding+sink to keep ActionSender's monotonic UIDs
        // aligned with journal insertion order and the current request classification.
        await _sendSequence.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                var generation = RequireAuthenticated();
                _sendingClearName = clearName;
                _sendingToken = ct;
                // Keep the local generation pinned for the entire single request.
                if (_journal.ActiveGeneration != generation)
                {
                    _revoked = true;
                    throw new MZP0GuardedActionException(MZP0GuardFailure.SessionChanged);
                }
            }

            try
            {
                var result = await _sender.SendRequestAsync(clearName, build, ct).ConfigureAwait(false);
                // ActionResult.Frame contains the *complete* encoded request; never
                // return it (including when ActionSender reports ConnectionClosed).
                return new ActionResult(result.Outcome, result.ClearName, result.Uid, null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (MZP0GuardedActionException)
            {
                throw;
            }
            catch
            {
                // No exception from a codec or sink may leak an encoded frame or input.
                throw new MZP0GuardedActionException(MZP0GuardFailure.SenderFailure);
            }
        }
        finally
        {
            lock (_gate)
            {
                _sendingClearName = null;
                _sendingToken = default;
            }
            _sendSequence.Release();
        }
    }

    /// <summary>
    /// Ordinary event waiting is NOT verification of an ACK. Return only event labels,
    /// clearing raw frames and payloads so no account fields can leave this boundary.
    /// Call TryAcknowledgeVerifiedInbound separately with an independent verifier.
    /// </summary>
    public async Task<BotMessage> WaitForEventAsync(string clearEventName, TimeSpan timeout,
        CancellationToken ct = default)
    {
        MZP0SessionGeneration generation;
        lock (_gate) generation = RequireAuthenticated();
        BotMessage inbound = await _sender.WaitForEventAsync(clearEventName, timeout, ct).ConfigureAwait(false);
        lock (_gate)
        {
            if (_revoked || _generation != generation || !IsStillAuthenticated(generation) ||
                !inbound.IsFromServer || inbound.ConnectionId != generation.Identity.ConnectionId)
            {
                _revoked = true;
                throw new MZP0GuardedActionException(MZP0GuardFailure.SessionChanged);
            }
        }
        return inbound with { Payload = Array.Empty<ProtoField>(), RawFrame = Array.Empty<byte>() };
    }

    /// <summary>
    /// ACK can be set only from a real independently verified positive response.
    /// This never treats a returned ActionResult.Sent, generic event or packet arrival as ACK.
    /// </summary>
    public bool TryAcknowledgeVerifiedInbound(BotMessage inbound)
    {
        ArgumentNullException.ThrowIfNull(inbound);
        lock (_gate)
        {
            if (_revoked || _generation is not { } generation)
                return false;
            if (!IsStillAuthenticated(generation))
            {
                _revoked = true;
                return false;
            }
            if (!inbound.IsFromServer || inbound.ConnectionId != generation.Identity.ConnectionId)
                return false;

            // Reject a server event/request masquerading as a positive response even
            // if a buggy external verifier unconditionally returns true.
            int? uid = ReadUidForBranch(inbound.RawFrame, "response", "Response");
            if (uid is null or <= 0) return false;
            var record = _journal.Records.SingleOrDefault(r => r.Generation == generation.Number &&
                r.GenerationNonce == generation.Nonce && r.CorrelationUid == uid.Value);
            if (record is null || record.State != MZP0DeliveryState.DispatchWindowOpened)
                return false;

            bool verified;
            try { verified = _responses.IsVerifiedPositiveResponse(inbound, generation, uid.Value); }
            catch { return false; }
            if (!verified) return false;
            // This is the final in-process correlation gate, not a live verifier.
            return _journal.TryAcknowledge(generation, record.OperationId,
                new MZP0AcknowledgmentEvidence(generation, uid.Value, ExplicitPositiveResponse: true));
        }
    }

    /// <summary>Disable this instance on disconnection/ownership changes; no replay or reactivation.</summary>
    public void Revoke()
    {
        lock (_gate) _revoked = true;
    }

    private bool TryAuthenticatedIdentity(out MZP0SessionIdentity identity)
    {
        try { return _source.TryGetAuthenticatedIdentity(out identity); }
        catch { identity = default; return false; }
    }

    private bool IsStillAuthenticated(MZP0SessionGeneration generation)
        => TryAuthenticatedIdentity(out var verified) && verified == generation.Identity &&
           _journal.ActiveGeneration == generation;

    private MZP0SessionGeneration RequireAuthenticated()
    {
        if (_revoked || _generation is not { } generation)
            throw new MZP0GuardedActionException(MZP0GuardFailure.SessionNotAuthenticated);
        if (!IsStillAuthenticated(generation))
        {
            _revoked = true;
            throw new MZP0GuardedActionException(MZP0GuardFailure.SessionChanged);
        }
        return generation;
    }

    private bool Dispatch(Direction direction, byte[] frame, IFrameSink actualSink)
    {
        lock (_gate)
        {
            var generation = RequireAuthenticated();
            _sendingToken.ThrowIfCancellationRequested();
            if (_sendingClearName is null || direction != Direction.ClientToServer || frame.Length == 0)
                throw new MZP0GuardedActionException(MZP0GuardFailure.InvalidEnvelope);

            int? uid = ReadUidForBranch(frame, "request", "Request");
            if (uid is null or <= 0)
                throw new MZP0GuardedActionException(MZP0GuardFailure.InvalidEnvelope);

            // All actions default to NON-idempotent: a crash never permits replay.
            var operation = Guid.NewGuid();
            try
            {
                _journal.BeginIntent(generation, operation, uid.Value,
                    MZP0OperationCategory.Other, MZP0DeliverySemantics.NonIdempotent);
                _journal.OpenDispatchWindow(generation, operation);
            }
            catch
            {
                throw new MZP0GuardedActionException(MZP0GuardFailure.JournalRejected);
            }

            // A connection may be withdrawn between journaling and forwarding.
            // Its opened dispatch window stays unresolved/uncertain even if no bytes left.
            try
            {
                if (!IsStillAuthenticated(generation))
                {
                    _revoked = true;
                    _journal.MarkUncertain(generation, operation);
                    throw new MZP0GuardedActionException(MZP0GuardFailure.SessionChanged);
                }
                if (_sendingToken.IsCancellationRequested)
                {
                    _journal.MarkUncertain(generation, operation);
                    throw new MZP0GuardedActionException(MZP0GuardFailure.TransportUncertain);
                }
                bool sent = actualSink.Send(direction, frame);
                if (!sent) _journal.MarkUncertain(generation, operation);
                return sent;
            }
            catch (MZP0GuardedActionException) { throw; }
            catch
            {
                // The transport may have put bytes on the wire before throwing.
                try { _journal.MarkUncertain(generation, operation); } catch { _revoked = true; }
                throw new MZP0GuardedActionException(MZP0GuardFailure.TransportUncertain);
            }
        }
    }

    private int? ReadUidForBranch(byte[] bytes, string branch, string container)
    {
        if (bytes is not { Length: > 0 }) return null;
        int? branchSlot = _catalog.FieldNumber("GameMessage", branch);
        int? uidSlot = _catalog.FieldNumber(container, "uid");
        int? requestSlot = _catalog.FieldNumber("GameMessage", "request");
        int? eventSlot = _catalog.FieldNumber("GameMessage", "event");
        int? responseSlot = _catalog.FieldNumber("GameMessage", "response");
        if (branchSlot is null or <= 0 || uidSlot is null or <= 0 ||
            requestSlot is null or <= 0 || eventSlot is null or <= 0 || responseSlot is null or <= 0 ||
            requestSlot == eventSlot || requestSlot == responseSlot || eventSlot == responseSlot)
            return null;
        try
        {
            var root = RawProtobuf.Parse(bytes);
            // A malformed frame with multiple oneof branches is ambiguous; never
            // authorize an outbound request or inbound ACK from it.
            if (root.Count(f => f.Field == requestSlot.Value || f.Field == eventSlot.Value ||
                                f.Field == responseSlot.Value) != 1) return null;
            var branches = root.Where(f => f.Field == branchSlot.Value && f.Wire == WireType.Len &&
                                           f.Message is not null).ToArray();
            if (branches.Length != 1) return null;
            var uidFields = branches[0].Message!.Where(f => f.Field == uidSlot.Value &&
                f.Wire == WireType.Varint).ToArray();
            if (uidFields.Length != 1 || uidFields[0].Varint is 0 or > int.MaxValue) return null;
            return (int)uidFields[0].Varint;
        }
        catch { return null; }
    }

    private sealed class MZP0WriteAheadSink(MZP0GuardedActionSender owner, IFrameSink transport) : IFrameSink
    {
        public bool Send(Direction direction, byte[] gameMessageBytes)
            => owner.Dispatch(direction, gameMessageBytes, transport);
    }
}
