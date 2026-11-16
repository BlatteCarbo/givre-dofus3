using GivreCore.Mitm;
using GivreClone.Engine.Recovery;

namespace GivreClone.Engine;

/// <summary>
/// Relie les captures GivreCore (qui n'exposent que connectionId + le pid seulement LOGGE) aux BotSession :
/// enregistre connectionId -&gt; session a la 1re frame et route chaque capture vers la bonne session.
/// Encapsule le petit manque de GivreCore (pas de map pid-&gt;connectionId publique). En Phase 1 on liera le
/// compte via le pid du handshake ; ici la cle est le connectionId.
/// </summary>
public sealed class SessionBinder
{
    private readonly Func<CapturedMessage, BotSession> _factory;
    private readonly Dictionary<int, BotSession> _sessions = new();
    private readonly Dictionary<uint, int> _connectionByProcess = new();
    // A real TCP session is bound to the actual accepted link/run identity,
    // never just the connection number and its reported process ID.
    private readonly Dictionary<int, TransportLinkProvenance> _sessionTransport = new();
    private readonly HashSet<TransportLinkProvenance> _retiredTransport = new();
    // An explicitly closed or superseded connection must never be resurrected
    // by a delayed pump callback, even if no session was created before close.
    // Keep tombstones across stop/start of the SAME proxy, whose IDs increase
    // monotonically; a different proxy has its own connection-ID namespace.
    private readonly HashSet<int> _retiredConnections = new();
    private readonly HashSet<int> _announcedAuthenticated = new();
    private readonly object _gate = new();
    // Factories may perform independently authorized I/O. Keep one factory
    // per connection at a time without holding the global session/EOF lock.
    // Fixed stripes avoid retaining an unbounded lock object per retired link.
    // Every attachment gets its OWN stripes: an old factory blocked for ID=1
    // must not stall the unrelated ID=1 of a new proxy after Detach/Attach.
    private object[] _ingressGates = NewIngressGates();
    private static object[] NewIngressGates() => Enumerable.Range(0, 256)
        .Select(_ => new object()).ToArray();
    private TcpProxyServer? _attachedProxy;
    private TcpProxyServer? _lastDetachedProxy;
    private Action<CapturedMessage>? _captureHandler;
    private Action<int>? _closeHandler;
    private Action<TransportLinkProvenance>? _transportCloseHandler;
    private long _attachmentEpoch;
    // Allow legacy unstarted in-memory test captures. Once the real source has
    // ever listened, only a link in its own live transport registry is trusted.

    /// <summary>Emis quand une nouvelle session (connexion) apparait.</summary>
    public event Action<BotSession>? SessionOpened;
    /// <summary>Raised only for a verified guarded session, never for a first packet.</summary>
    public event Action<BotSession>? SessionAuthenticated;

    public SessionBinder(Func<int, BotSession> sessionFactory)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        _factory = capture => sessionFactory(capture.ConnectionId);
    }

    /// <summary>
    /// Capture-aware factory for protected sessions. The capture's PID alone is
    /// untrusted; the factory must use independent process/session authorities.
    /// </summary>
    public SessionBinder(Func<CapturedMessage, BotSession> sessionFactory)
        => _factory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));

    /// <summary>Bind one proxy at a time. A fresh attachment cannot inherit an old PID lease.</summary>
    public void Attach(TcpProxyServer proxy)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        lock (_gate)
        {
            if (ReferenceEquals(_attachedProxy, proxy)) return;
            if (_attachedProxy is not null)
                throw new InvalidOperationException("Detach the previous proxy before attaching another.");
            _sessions.Clear();
            _connectionByProcess.Clear();
            _sessionTransport.Clear();
            if (!ReferenceEquals(_lastDetachedProxy, proxy))
            {
                _retiredConnections.Clear();
                _retiredTransport.Clear();
            }
            _lastDetachedProxy = null;
            _announcedAuthenticated.Clear();
            var epoch = ++_attachmentEpoch;
            var ingressGates = _ingressGates = NewIngressGates();
            _captureHandler = capture => OnCaptured(capture, epoch, ingressGates);
            _closeHandler = id => ForgetCore(id, proxy, epoch);
            _transportCloseHandler = identity => ForgetTransportCore(proxy, identity, epoch);
            _attachedProxy = proxy;
            try
            {
                proxy.MessageCaptured += _captureHandler;
                proxy.ConnectionClosed += _closeHandler;
                proxy.TransportLinkClosed += _transportCloseHandler;
            }
            catch
            {
                proxy.MessageCaptured -= _captureHandler;
                proxy.TransportLinkClosed -= _transportCloseHandler;
                proxy.ConnectionClosed -= _closeHandler;
                _attachedProxy = null;
                _captureHandler = null;
                _closeHandler = null;
                _transportCloseHandler = null;
                ++_attachmentEpoch;
                throw;
            }
        }
    }

    /// <summary>
    /// Stop routing and revoke active sessions. Sessions remains a historical
    /// snapshot until the next Attach, but no PID or authentication lookup uses it.
    /// </summary>
    public void Detach(TcpProxyServer proxy)
    {
        BotSession[] retired;
        lock (_gate)
        {
            if (!ReferenceEquals(_attachedProxy, proxy)) return;
            proxy.MessageCaptured -= _captureHandler;
            proxy.ConnectionClosed -= _closeHandler;
            proxy.TransportLinkClosed -= _transportCloseHandler;
            _closeHandler = null;
            _transportCloseHandler = null;
            _captureHandler = null;
            _attachedProxy = null;
            _lastDetachedProxy = proxy;
            ++_attachmentEpoch;
            _connectionByProcess.Clear();
            retired = _sessions.Values.ToArray();
            foreach (var session in retired)
            {
                _retiredConnections.Add(session.ConnectionId);
                RevokeActions(session);
            }
            foreach (var identity in _sessionTransport.Values)
                _retiredTransport.Add(identity);
            _sessionTransport.Clear();
        }
        foreach (var session in retired) Retire(session);
    }

    /// <summary>
    /// The legacy explicit close port. Callers with a real transport source
    /// must use ForgetFrom so an old proxy's recycled ID cannot retire a
    /// session from a different connection-ID namespace.
    /// </summary>
    public bool Forget(int connectionId) => ForgetCore(connectionId, null, null);

    /// <summary>
    /// Retire an ID only from its actual source proxy. Delayed ConnectionClosed
    /// callbacks from a detached/replaced proxy may not affect the next proxy.
    /// </summary>
    public bool ForgetFrom(TcpProxyServer source, int connectionId)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ForgetCore(connectionId, source, null);
    }

    /// <summary>
    /// Close only the actual accepted link/run identity, even if an old callback
    /// is delivered after a new link with the same numeric ID is registered.
    /// No server response or ACK is inferred from transport closure.
    /// </summary>
    public bool ForgetFrom(TcpProxyServer source, TransportLinkProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ForgetTransportCore(source, provenance, null);
    }

    private bool ForgetTransportCore(TcpProxyServer source,
        TransportLinkProvenance provenance, long? epoch)
    {
        if (provenance.ConnectionId <= 0 || provenance.ProcessId == 0 ||
            provenance.LinkToken <= 0 || provenance.RunEpoch <= 0 ||
            provenance.ProxyInstanceId != source.ProxyInstanceId) return false;
        BotSession? retired;
        lock (_gate)
        {
            if (epoch is long boundEpoch && (boundEpoch != _attachmentEpoch ||
                !ReferenceEquals(source, _attachedProxy))) return false;
            if (!ReferenceEquals(source, _attachedProxy) &&
                !ReferenceEquals(source, _lastDetachedProxy)) return false;
            _retiredTransport.Add(provenance);
            if (!_sessions.TryGetValue(provenance.ConnectionId, out retired) ||
                !_sessionTransport.TryGetValue(provenance.ConnectionId, out var bound) ||
                bound != provenance) return false;
            _sessions.Remove(provenance.ConnectionId);
            _sessionTransport.Remove(provenance.ConnectionId);
            // Retire the exact link token, not a naked numeric ID: a separate
            // admitted link can legitimately reuse the number in another run.
            RevokeActions(retired);
            if (retired.ProcessId is uint pid &&
                _connectionByProcess.TryGetValue(pid, out var current) &&
                current == provenance.ConnectionId)
                _connectionByProcess.Remove(pid);
            _announcedAuthenticated.Remove(provenance.ConnectionId);
        }
        Retire(retired);
        return true;
    }

    private bool ForgetCore(int connectionId, TcpProxyServer? source, long? epoch)
    {
        if (connectionId <= 0) return false;
        BotSession? retired;
        lock (_gate)
        {
            // ID-only legacy closure has no source/epoch. It is allowed only
            // for unstarted synthetic fixtures; never retire a real session
            // with a naked numeric ID after the transport has started.
            if (source is null && _attachedProxy is { } active
                && active.HasEverStarted) return false;
            // A running real proxy publishes TransportLinkClosed with a full
            // link token. A numeric close alone must not retire a successor
            // registered under the same number. Pre-start fixtures retain the
            // historical explicit-ID callback contract.
            if (source is not null && source.RequiresTransportProvenance) return false;
            // A delayed callback retained by the old event invocation list
            // cannot retire a session after Detach or a newer Attach, even
            // when the same TcpProxyServer instance is reused.
            if (epoch is long boundEpoch && (boundEpoch != _attachmentEpoch ||
                !ReferenceEquals(source, _attachedProxy))) return false;
            if (source is not null
                && !ReferenceEquals(source, _attachedProxy)
                && !ReferenceEquals(source, _lastDetachedProxy))
                return false;
            // A close can beat the first MessageCaptured callback. Remember the
            // exact closed link ID even without a session or after Detach, so
            // its delayed pump cannot manufacture a new BotSession on restart.
            _retiredConnections.Add(connectionId);
            if (!_sessions.Remove(connectionId, out retired)) return false;
            _sessionTransport.Remove(connectionId);
            RevokeActions(retired);
            if (retired.ProcessId is uint pid &&
                _connectionByProcess.TryGetValue(pid, out var current) && current == connectionId)
                _connectionByProcess.Remove(pid);
            _announcedAuthenticated.Remove(connectionId);
        }
        Retire(retired);
        return true;
    }

    public BotSession? Find(int connectionId)
    {
        BotSession session;
        TcpProxyServer proxy;
        TransportLinkProvenance link = default;
        bool requiresProof;
        lock (_gate)
        {
            if (_attachedProxy is not { } source ||
                !_sessions.TryGetValue(connectionId, out session!)) return null;
            proxy = source;
            requiresProof = proxy.RequiresTransportProvenance ||
                _sessionTransport.ContainsKey(connectionId);
            if (requiresProof && !_sessionTransport.TryGetValue(connectionId, out link))
                return null;
        }
        // A real ownership authority may block or reenter session callbacks.
        // Never invoke it while holding _gate: EOF/Detach must remain runnable.
        if (requiresProof && !proxy.IsCurrentTransportLink(link)) return null;
        lock (_gate)
            return ReferenceEquals(proxy, _attachedProxy) &&
                _sessions.TryGetValue(connectionId, out var current) &&
                ReferenceEquals(current, session) &&
                (!requiresProof || (_sessionTransport.TryGetValue(connectionId, out var currentLink) &&
                    currentLink == link && !_retiredTransport.Contains(link)))
                ? session : null;
    }

    /// <summary>Session liee a un pid de client Dofus (pour associer un compte a sa fenetre), ou null.</summary>
    public BotSession? FindByProcess(uint processId)
    {
        TcpProxyServer proxy;
        BotSession session;
        int id;
        TransportLinkProvenance link = default;
        bool requiresProof;
        lock (_gate)
        {
            if (_attachedProxy is not { } source ||
                !_connectionByProcess.TryGetValue(processId, out id) ||
                !_sessions.TryGetValue(id, out session!)) return null;
            proxy = source;
            requiresProof = proxy.RequiresTransportProvenance ||
                _sessionTransport.ContainsKey(id);
            if (requiresProof && !_sessionTransport.TryGetValue(id, out link))
                return null;
        }
        if (requiresProof && !proxy.IsCurrentTransportLink(link)) return null;
        lock (_gate)
            return ReferenceEquals(proxy, _attachedProxy) &&
                _connectionByProcess.TryGetValue(processId, out var currentId) &&
                currentId == id && _sessions.TryGetValue(id, out var current) &&
                ReferenceEquals(current, session) &&
                (!requiresProof || (_sessionTransport.TryGetValue(id, out var currentLink) &&
                    currentLink == link && !_retiredTransport.Contains(link)))
                ? session : null;
    }

    /// <summary>
    /// Obtain link/run provenance only for the identical currently bound
    /// BotSession. The result is transient: consumers must recheck through
    /// TcpProxyServer.IsCurrentTransportLink before trusting transport liveness.
    /// </summary>
    public bool TryGetCurrentTransportLink(BotSession session,
        out TransportLinkProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(session);
        TcpProxyServer proxy;
        TransportLinkProvenance link;
        lock (_gate)
        {
            if (_attachedProxy is not { } source ||
                !_sessions.TryGetValue(session.ConnectionId, out var current) ||
                !ReferenceEquals(current, session) ||
                !_sessionTransport.TryGetValue(session.ConnectionId, out link) ||
                _retiredTransport.Contains(link))
            {
                provenance = default;
                return false;
            }
            proxy = source;
        }
        if (proxy.IsCurrentTransportLink(link))
        {
            lock (_gate)
            {
                if (ReferenceEquals(proxy, _attachedProxy) &&
                    _sessions.TryGetValue(session.ConnectionId, out var current) &&
                    ReferenceEquals(current, session) &&
                    _sessionTransport.TryGetValue(session.ConnectionId, out var currentLink) &&
                    currentLink == link && !_retiredTransport.Contains(link))
                {
                    provenance = link;
                    return true;
                }
            }
        }
        provenance = default;
        return false;
    }

    public IReadOnlyList<BotSession> Sessions
    {
        get { lock (_gate) return _sessions.Values.ToArray(); }
    }

    /// <summary>
    /// Never infer "connected" from the presence of a connection ID.
    /// Evaluate ownership/expiry on each query, so revocation disappears.
    /// </summary>
    public IReadOnlyList<BotSession> AuthenticatedSessions
    {
        get
        {
            BotSession[] snapshot;
            lock (_gate)
            {
                snapshot = _attachedProxy is null ? [] : _sessions.Values.ToArray();
            }
            // Authentication checks can invoke revocation and behavior callbacks:
            // never run them under the binder collection lock.
            return snapshot.Where(s => s.IsAuthenticated &&
                ReferenceEquals(Find(s.ConnectionId), s)).ToArray();
        }
    }

    private void OnCaptured(CapturedMessage captured, long epoch, object[] ingressGates)
    {
        // Keep one link's construction/dispatch serial, but an old callback
        // retains its OLD stripe array. A new attachment never inherits its
        // blocked factory/owner verification even with a recycled numeric ID.
        lock (ingressGates[captured.ConnectionId & 255])
            OnCapturedSerial(captured, epoch);
    }

    private bool CaptureAdmittedUnsafe(CapturedMessage captured, long epoch) =>
        _attachedProxy is not null && epoch == _attachmentEpoch &&
        (captured.Provenance is not null ||
         !_retiredConnections.Contains(captured.ConnectionId)) &&
        (captured.Provenance is not { } identity ||
         !_retiredTransport.Contains(identity));

    private bool CaptureAdmitted(CapturedMessage captured, long epoch)
    {
        TcpProxyServer source;
        lock (_gate)
        {
            if (!CaptureAdmittedUnsafe(captured, epoch)) return false;
            source = _attachedProxy!;
        }
        // May invoke a blocking OS/process authority. NEVER hold _gate here;
        // an independent EOF, Stop or a different link must remain progressable.
        if (!CaptureFromCurrentLink(source, captured)) return false;
        lock (_gate)
            return ReferenceEquals(source, _attachedProxy) &&
                CaptureAdmittedUnsafe(captured, epoch);
    }

    private bool MatchesExistingSessionUnsafe(BotSession session, CapturedMessage captured) =>
        session.ProcessId == (captured.ProcessId == 0 ? (uint?)null : captured.ProcessId) &&
        (_sessionTransport.TryGetValue(captured.ConnectionId, out var bound)
            ? captured.Provenance == bound : captured.Provenance is null);

    private readonly record struct AuthenticatedOwnerSnapshot(int ConnectionId,
        TcpProxyServer? Source, TransportLinkProvenance? Link);

    private AuthenticatedOwnerSnapshot? AuthenticatedOwnerUnsafe(uint pid)
    {
        if (!_connectionByProcess.TryGetValue(pid, out var id) ||
            !_announcedAuthenticated.Contains(id)) return null;
        // A revoked owner must not block a genuinely admitted successor just
        // because its EOF observer was delayed behind a consumer callback.
        // The external authority check happens OUTSIDE _gate, then we verify
        // this precise owner snapshot again before committing a replacement.
        return new AuthenticatedOwnerSnapshot(id, _attachedProxy,
            _sessionTransport.TryGetValue(id, out var link) ? link : null);
    }

    private static bool OwnerStillLive(AuthenticatedOwnerSnapshot? snapshot) =>
        snapshot is { } current &&
        (current.Source is not { HasEverStarted: true } source ||
         current.Link is not { } link || source.IsCurrentTransportLink(link));

    private AuthenticatedOwnerSnapshot? SnapshotAuthenticatedOwner(uint pid)
    {
        lock (_gate) return AuthenticatedOwnerUnsafe(pid);
    }

    private void OnCapturedSerial(CapturedMessage captured, long epoch)
    {
        BotSession session = null!;
        BotSession? retired = null;
        BotSession? discard = null;
        bool opened = false;
        bool accepted = false;
        bool needsFactory;
        if (!CaptureAdmitted(captured, epoch)) return;
        AuthenticatedOwnerSnapshot? priorOwner = null;
        lock (_gate)
        {
            if (!CaptureAdmittedUnsafe(captured, epoch)) return;
            needsFactory = !_sessions.TryGetValue(captured.ConnectionId, out session!);
            if (!needsFactory && !MatchesExistingSessionUnsafe(session, captured)) return;
            // Reject an already authenticated, still-live owner BEFORE
            // constructing an alternative guarded journal or action lease.
            if (needsFactory && captured.ProcessId != 0)
                priorOwner = AuthenticatedOwnerUnsafe(captured.ProcessId);
        }
        if (needsFactory && OwnerStillLive(priorOwner)) return;

        BotSession? candidate = null;
        if (needsFactory)
        {
            // No global lock across external factory: unrelated sessions,
            // exact-link EOF and Detach remain free to make progress.
            candidate = _factory(captured);
            if (candidate.ConnectionId != captured.ConnectionId)
            {
                Retire(candidate);
                throw new InvalidOperationException("Session factory returned another connection ID.");
            }
        }

        // Verify transport and old authenticated owner outside the global
        // lock. If an owner changes during this check, fail closed rather than
        // accidentally replacing a NEW authenticated link for the same PID.
        bool admissionNow = CaptureAdmitted(captured, epoch);
        var ownerAtCommit = captured.ProcessId == 0 ? null :
            SnapshotAuthenticatedOwner(captured.ProcessId);
        bool ownerLiveAtCommit = OwnerStillLive(ownerAtCommit);
        lock (_gate)
        {
            // Stop, close, owner revocation, or another link for the same PID
            // can race while a factory runs. No stale candidate becomes live.
            if (!admissionNow || !CaptureAdmittedUnsafe(captured, epoch))
                discard = candidate;
            else if (_sessions.TryGetValue(captured.ConnectionId, out session!))
            {
                if (MatchesExistingSessionUnsafe(session, captured))
                    accepted = true;
                discard = candidate;
            }
            else if (candidate is null ||
                     (captured.ProcessId != 0 &&
                      (AuthenticatedOwnerUnsafe(captured.ProcessId) != ownerAtCommit ||
                       ownerLiveAtCommit)))
                discard = candidate;
            else
            {
                if (captured.ProcessId != 0 &&
                    _connectionByProcess.TryGetValue(captured.ProcessId, out var oldId))
                {
                    if (_sessions.Remove(oldId, out retired))
                    {
                        // Fail closed before making the replacement visible to
                        // any concurrent observer; cancellation follows outside.
                        RevokeActions(retired);
                        _announcedAuthenticated.Remove(oldId);
                        _retiredConnections.Add(oldId);
                        if (_sessionTransport.Remove(oldId, out var oldTransport))
                            _retiredTransport.Add(oldTransport);
                    }
                }
                session = candidate;
                session.ProcessId = captured.ProcessId == 0 ? null : captured.ProcessId;
                _sessions[captured.ConnectionId] = session;
                if (captured.Provenance is { } admittedTransport)
                    _sessionTransport[captured.ConnectionId] = admittedTransport;
                if (captured.ProcessId != 0)
                    _connectionByProcess[captured.ProcessId] = captured.ConnectionId;
                opened = true;
                accepted = true;
            }
        }
        if (retired is not null) Retire(retired);
        if (discard is not null && !ReferenceEquals(discard, session)) Retire(discard);
        if (!accepted) return;
        if (!IsCurrent(captured.ConnectionId, session, epoch)) return;
        if (opened)
            NotifyWhileCurrent(SessionOpened, captured, session, epoch);
        if (!IsCurrent(captured.ConnectionId, session, epoch)) return;
        // The source link may have been retired during SessionOpened. A link
        // from the previous transport run must never reach ACK/state folding.
        if (!CaptureStillLive(captured, epoch)) return;
        session.Dispatch(captured);
        var verified = session.IsAuthenticated;
        bool firstVerified;
        lock (_gate)
            firstVerified = verified && IsCurrentUnsafe(captured.ConnectionId, session, epoch) &&
                _announcedAuthenticated.Add(captured.ConnectionId);
        if (firstVerified)
            NotifyWhileCurrent(SessionAuthenticated, captured, session, epoch);
    }

    private void NotifyWhileCurrent(Action<BotSession>? listeners,
        CapturedMessage captured, BotSession session, long epoch)
    {
        if (listeners is null) return;
        // User callbacks can call Detach(), ForgetFrom() or attach a successor.
        // The event invocation list is a snapshot, so revalidate before EACH
        // subscriber; do not notify remaining listeners for a retired session.
        foreach (Action<BotSession> listener in listeners.GetInvocationList())
        {
            if (!IsCurrent(captured.ConnectionId, session, epoch) ||
                !CaptureStillLive(captured, epoch)) return;
            listener(session);
        }
    }

    private bool IsCurrent(int connectionId, BotSession session, long epoch)
    {
        lock (_gate) return IsCurrentUnsafe(connectionId, session, epoch);
    }

    private bool CaptureStillLive(CapturedMessage captured, long epoch)
        => CaptureAdmitted(captured, epoch);

    private static bool CaptureFromCurrentLink(TcpProxyServer source, CapturedMessage captured)
    {
        // In-memory unit tests invoke MessageCaptured without starting a TCP
        // listener; preserve that test-only path. The actual accepted transport
        // must prove the exact live registry entry (including its link token).
        return !source.HasEverStarted || source.IsCurrentCapturedLink(captured);
    }

    private bool IsCurrentUnsafe(int connectionId, BotSession session, long epoch) =>
        _attachedProxy is not null && epoch == _attachmentEpoch &&
        _sessions.TryGetValue(connectionId, out var current) && ReferenceEquals(current, session);

    private static void Retire(BotSession session)
    {
        RevokeActions(session);
        session.Behaviors.Stop();
    }

    private static void RevokeActions(BotSession session)
    {
        // This guard's Revoke takes only its own in-memory lock and never sends.
        if (session.Actions is MZP0GuardedActionSender protectedActions)
            protectedActions.Revoke();
    }
}
