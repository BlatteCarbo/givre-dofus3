using GivreClone.Engine;
using GivreClone.Engine.Recovery;
using GivreCore.Mitm;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

/// <summary>
/// Adaptateur App -&gt; moteur headless (BotHost). Resout les chemins (data a cote de l'exe, comptes dans
/// %APPDATA%\GivreClone) et relaie logs + nombre de sessions vers l'UI. L'UX riche (dashboards, comportements)
/// se branchera ici en Phase 5.
/// </summary>
public sealed class BotHostService
{
    private readonly string _dataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private readonly object _gate = new();
    private BotHost? _host;
    private Action<string>? _hostLogHandler;
    private Action<BotSession>? _hostAuthenticatedHandler;
    private Action<TransportFrameObservation>? _hostFrameHandler;
    private Action<TransportLinkProvenance>? _hostLinkClosedHandler;
    private long _hostEpoch;
    private bool _starting;
    private bool _stopping;
    private bool _stopCompleted;

    // GivreClone's original relay/account store must NEVER be re-used by the clone.
    public int Port { get; private set; }
    // A retained host object is not proof its capture loop is still alive.
    // This service is the protected headless relay, not a Socket Lua session.
    public bool IsRunning
    {
        get { lock (_gate) return !_stopping && _host?.Capture.IsRunning == true; }
    }

    public event Action<string>? Log;
    public event Action<int>? SessionsChanged;
    /// <summary>
    /// Exact source-bound raw transport frame from the currently owned BotHost.
    /// This is a local TCP observation, not a verified gameplay ACK/response.
    /// </summary>
    public event Action<TransportFrameObservation>? TransportFrameObserved;
    /// <summary>
    /// Exact EOF/closure of one source-issued relay link, forwarded only from
    /// the current host/run. Consumers must retire the full provenance token,
    /// never a PID or numeric connection ID. Stop invalidates all links even
    /// when an individual close event is suppressed during teardown.
    /// </summary>
    public event Action<TransportLinkProvenance>? TransportLinkClosed;

    /// <summary>
    /// Revalidate the proxy instance, accepted link and run generation at the
    /// consumer boundary; an earlier callback is not a lasting lease.
    /// </summary>
    public bool IsCurrentTransportLink(TransportLinkProvenance provenance)
    {
        BotHost host;
        long epoch;
        lock (_gate)
        {
            if (_stopping || _host is not { } current) return false;
            host = current;
            epoch = _hostEpoch;
        }
        return IsCurrentHostLink(host, epoch, provenance);
    }

    /// <summary>
    /// Read-only session lookup bound to this exact service generation. False
    /// after Stop/Start, EOF, session replacement or transport revocation.
    /// </summary>
    public bool TryGetCurrentTransportLink(BotSession session,
        out TransportLinkProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(session);
        BotHost host;
        long epoch;
        lock (_gate)
        {
            if (_stopping || _host is not { } current)
            {
                provenance = default;
                return false;
            }
            host = current;
            epoch = _hostEpoch;
        }
        // These lookups consult an independent (possibly slow) process owner.
        // Stop must be able to revoke this service's host epoch without waiting
        // for that authority while holding _gate.
        if (host.Orchestrator.TryGetCurrentTransportLink(session, out var link) &&
            IsCurrentHostLink(host, epoch, link))
        {
            provenance = link;
            return true;
        }
        provenance = default;
        return false;
    }

    public void Start() => Start(null, null);

    /// <summary>
    /// Fail-closed production entry: a missing trusted session and protected
    /// factory cannot read account storage, start the proxy or inject anyone.
    /// </summary>
    public void Start(MZP1OwnedClientPermit? ownedClient,
        Func<CapturedMessage, MZP1GuardedEngineWiring>? guardedSessionFactory,
        int testRelayPort = 0, string? testAccountStorePath = null)
        => StartCore(ownedClient, guardedSessionFactory, null,
            testRelayPort, testAccountStorePath);

    /// <summary>
    /// Stronger entry: the guarded session factory receives the actual proxy
    /// sink and protocol catalogue, allocated inside THIS BotHost. Unlike a
    /// supplied arbitrary sink, it is bound to the captured connection.
    /// Independent live identity and server-response authorities remain required.
    /// </summary>
    public void StartBound(MZP1OwnedClientPermit? ownedClient,
        Func<CapturedMessage, ProtocolCatalog, IFrameSink, IPacer, MZP1GuardedEngineWiring>? guardedFactory,
        int testRelayPort, string? testAccountStorePath)
        => StartCore(ownedClient, null, guardedFactory,
            testRelayPort, testAccountStorePath);

    private void StartCore(MZP1OwnedClientPermit? ownedClient,
        Func<CapturedMessage, MZP1GuardedEngineWiring>? guardedFactory,
        Func<CapturedMessage, ProtocolCatalog, IFrameSink, IPacer, MZP1GuardedEngineWiring>? boundFactory,
        int testRelayPort, string? testAccountStorePath)
    {
        BotHost? previous;
        long intentEpoch;
        lock (_gate)
        {
            if (_starting || _stopping || _host?.Capture.IsRunning == true) return;
            previous = _host;
            intentEpoch = _hostEpoch;
        }
        // Capture may have exited by itself. Its callbacks must be revoked
        // before an explicit replacement can be installed.
        if (previous is not null)
        {
            if (!IsCurrentHost(previous, intentEpoch)) return;
            Stop();
            lock (_gate)
            {
                // Only the Stop initiated by this replacement may advance the
                // intent epoch. A concurrent external Stop cancels this Start.
                if (_host is not null || _starting || _stopping ||
                    _hostEpoch != intentEpoch + 1) return;
                intentEpoch = _hostEpoch;
            }
        }
        if (ownedClient?.IsWellFormed != true ||
            (guardedFactory is null) == (boundFactory is null) ||
            testRelayPort is < 1024 or > 65535 or 5657 or 5658 ||
            !IsIsolatedStore(testAccountStorePath))
        {
            Log?.Invoke("[capture] Client, moteur protégé, port dédié et comptes de TEST isolés requis ; démarrage refusé.");
            return;
        }
        var host = new BotHost(_dataDir, testRelayPort, testAccountStorePath,
            guardedSessionFactory: guardedFactory, ownedClient: ownedClient,
            hostBoundGuardedFactory: boundFactory);
        long epoch;
        bool candidateRejected;
        lock (_gate)
        {
            // Another caller may have won a simultaneous Start. This candidate
            // has no listener and must never replace a newer owned host.
            candidateRejected = _host is not null || _starting || _stopping ||
                _hostEpoch != intentEpoch;
            if (!candidateRejected)
            {
                _starting = true;
                epoch = ++_hostEpoch;
                _host = host;
                var log = new Action<string>(message => OnHostLog(host, epoch, message));
                var authenticated = new Action<BotSession>(
                    session => OnHostAuthenticated(host, epoch, session));
                var frame = new Action<TransportFrameObservation>(
                    observation => OnHostFrame(host, epoch, observation));
                var linkClosed = new Action<TransportLinkProvenance>(
                    provenance => OnHostLinkClosed(host, epoch, provenance));
                _hostLogHandler = log;
                _hostAuthenticatedHandler = authenticated;
                _hostFrameHandler = frame;
                _hostLinkClosedHandler = linkClosed;
                host.Log += log;
                host.Orchestrator.Binder.SessionAuthenticated += authenticated;
                host.TransportFrameObserved += frame;
                host.Orchestrator.TransportLinkClosed += linkClosed;
            }
            else epoch = 0;
        }
        if (candidateRejected)
        {
            host.Stop(); // close candidate's constructor-time binder subscription
            return;
        }
        bool started = false;
        bool stale;
        try
        {
            if (IsCurrentHost(host, epoch))
            {
                host.Start();
                started = host.Capture.IsRunning;
            }
        }
        finally
        {
            lock (_gate)
            {
                stale = !IsCurrentHostUnsafe(host, epoch);
                if (!stale && started)
                {
                    _starting = false;
                    Port = testRelayPort;
                }
                else if (!stale)
                {
                    DetachHostCallbacks(host);
                    _host = null;
                    Port = 0;
                }
            }
            if (stale || !started)
            {
                // The Start/Stop callback race may have completed a transport
                // start after Stop was requested. Its old host must own the
                // cleanup; a successor's callbacks are never touched.
                try { host.Stop(); }
                finally
                {
                    lock (_gate)
                    {
                        _starting = false;
                        if (_stopping && _stopCompleted) _stopping = false;
                    }
                }
            }
        }
        if (!started && !stale)
            Log?.Invoke("[capture] Aucun client utilisateur explicitement autorisé : hôte inactif.");
    }

    public void Stop()
    {
        BotHost? oldHost;
        long epoch;
        lock (_gate)
        {
            if (_stopping) return;
            _stopping = true;
            _stopCompleted = false;
            epoch = ++_hostEpoch;
            oldHost = _host;
            if (oldHost is not null) DetachHostCallbacks(oldHost);
            _host = null;
            Port = 0;
        }
        try { oldHost?.Stop(); }
        finally
        {
            lock (_gate)
            {
                _stopCompleted = true;
                if (!_starting) _stopping = false;
            }
        }
        // A second Stop/Start from a subscriber may advance the epoch.
        // Only this exact Stop may publish the zero-session transition.
        if (IsCurrentStop(epoch))
            SessionsChanged?.Invoke(0);
    }

    private void DetachHostCallbacks(BotHost host)
    {
        // Called under _gate after the local epoch was already revoked.
        if (_hostLogHandler is { } log) host.Log -= log;
        if (_hostAuthenticatedHandler is { } authenticated)
            host.Orchestrator.Binder.SessionAuthenticated -= authenticated;
        if (_hostFrameHandler is { } frame) host.TransportFrameObserved -= frame;
        if (_hostLinkClosedHandler is { } linkClosed)
            host.Orchestrator.TransportLinkClosed -= linkClosed;
        _hostLogHandler = null;
        _hostAuthenticatedHandler = null;
        _hostFrameHandler = null;
        _hostLinkClosedHandler = null;
    }

    private bool IsCurrentHost(BotHost host, long epoch)
    {
        lock (_gate) return IsCurrentHostUnsafe(host, epoch);
    }

    private bool IsCurrentHostLink(BotHost host, long epoch,
        TransportLinkProvenance provenance)
    {
        if (!IsCurrentHost(host, epoch) || !host.Capture.IsRunning) return false;
        // Exact source/run/link ownership is verified outside _gate, to avoid
        // an external IsStillOwner stall blocking Stop/Start and EOF delivery.
        // This host is private to the service; its transport is the only
        // source of the proof. Local forwarding is NOT a gameplay ACK.
        if (!host.Proxy.IsCurrentTransportLink(provenance)) return false;
        return IsCurrentHost(host, epoch) && host.Capture.IsRunning;
    }

    private bool IsCurrentHostUnsafe(BotHost host, long epoch) =>
        !_stopping && ReferenceEquals(_host, host) && _hostEpoch == epoch;

    private bool IsCurrentStop(long epoch)
    {
        lock (_gate) return _hostEpoch == epoch && _host is null;
    }

    private void OnHostLog(BotHost host, long epoch, string message)
    {
        var listeners = Log;
        if (listeners is null) return;
        foreach (Action<string> listener in listeners.GetInvocationList())
        {
            if (!IsCurrentHost(host, epoch)) return;
            try { listener(message); }
            catch { /* Diagnostics must not interrupt lifecycle teardown. */ }
        }
    }

    private void OnHostAuthenticated(BotHost host, long epoch, BotSession session)
    {
        if (!IsCurrentHost(host, epoch) || !host.Capture.IsRunning ||
            !ReferenceEquals(host.Orchestrator.Binder.Find(session.ConnectionId), session))
            return;
        int count = host.Orchestrator.AuthenticatedSessions.Count;
        var listeners = SessionsChanged;
        if (listeners is null) return;
        foreach (Action<int> listener in listeners.GetInvocationList())
        {
            if (!IsCurrentHost(host, epoch) || !host.Capture.IsRunning ||
                !ReferenceEquals(host.Orchestrator.Binder.Find(session.ConnectionId), session))
                return;
            try { listener(count); }
            catch { /* A stale/throwing subscriber cannot disrupt capture. */ }
        }
    }

    private void OnHostFrame(BotHost host, long epoch, TransportFrameObservation frame)
    {
        var listeners = TransportFrameObserved;
        if (listeners is null) return;
        foreach (Action<TransportFrameObservation> listener in listeners.GetInvocationList())
        {
            // Revalidate at EACH consumer: a prior callback may synchronously
            // Stop or switch host while another callback is queued.
            if (!IsCurrentHostLink(host, epoch, frame.Provenance)) return;
            try { listener(frame); }
            catch { /* Consumer failures may not break MZP1 FIFO forwarding. */ }
        }
    }

    private void OnHostLinkClosed(BotHost host, long epoch,
        TransportLinkProvenance provenance)
    {
        var listeners = TransportLinkClosed;
        if (listeners is null) return;
        foreach (Action<TransportLinkProvenance> listener in listeners.GetInvocationList())
        {
            // The link is already removed, so IsCurrentTransportLink must be
            // false. Validate the real source and still-current listener run
            // instead, rechecking after each potentially reentrant consumer.
            if (!IsCurrentHost(host, epoch) || !host.Capture.IsRunning ||
                provenance.ProxyInstanceId != host.Proxy.ProxyInstanceId ||
                provenance.RunEpoch <= 0 ||
                provenance.RunEpoch != host.Proxy.CurrentRunEpoch ||
                provenance.ConnectionId <= 0 || provenance.LinkToken <= 0 ||
                provenance.ProcessId == 0) return;
            try { listener(provenance); }
            catch { /* Consumer cannot interrupt source transport cleanup. */ }
        }
    }

    private static bool IsIsolatedStore(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        try
        {
            string root = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GivreReplica", "TestRuns"));
            string file = Path.GetFullPath(candidate);
            if (!file.StartsWith(root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) ||
                !file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return false;
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(file)!);
                 dir is not null; dir = dir.Parent)
                if (dir.Exists && dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    return false;
            if (File.Exists(file) &&
                File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint))
                return false;
            return true;
        }
        catch { return false; }
    }
}
