using GivreClone.Engine.Accounts;
using GivreClone.Protocol;
using GivreCore.Mitm;
using GivreClone.Engine.Recovery;

namespace GivreClone.Engine;

/// <summary>
/// Cablage HEADLESS de GivreClone (l'analogue de CaptureService de GivreProxy.App, sans Avalonia/Dispatcher) :
/// proxy GivreCore + catalogue protocole + orchestrateur. Point d'entree pour l'app comme pour un runtime sans UI.
/// L'auto-injection du hook par pid (comme CaptureService) est deleguee a la couche App/launcher.
/// </summary>
public sealed class BotHost
{
    private readonly object _lifecycleGate = new();
    private Action<TransportFrameObservation>? _transportHandler;
    private long _consumerEpoch;
    private bool _starting;
    private bool _stopping;
    private bool _stopCompleted;
    private bool _active;

    public TcpProxyServer Proxy { get; }
    public ProtocolCatalog Catalog { get; }
    public BotOrchestrator Orchestrator { get; }
    public DofusCaptureHost Capture { get; }

    public event Action<string>? Log;
    /// <summary>
    /// Real neutral protobuf bodies emitted by this host's current transport.
    /// Provenance identifies the proxy, link and run; delivery is not a
    /// server response, authenticated game event, or ACK.
    /// </summary>
    public event Action<TransportFrameObservation>? TransportFrameObserved;

    /// <summary>
    /// Check the exact current owned relay link at the point of consumption.
    /// Stop/restart invalidates previously captured callbacks and sessions.
    /// </summary>
    public bool IsCurrentTransportLink(TransportLinkProvenance provenance)
    {
        lock (_lifecycleGate)
            return !_stopping && (_active || (_starting && Capture.IsRunning)) &&
                Proxy.IsCurrentTransportLink(provenance);
    }

    // Port 5658 = propre a GivreClone (GivreProxy = 5657) -> les deux peuvent coexister. Le hook GivreKama.dll
    // redirige vers ce port ; garder les deux synchronises.
    public BotHost(string dataDir, int proxyPort = 5658, string? accountStorePath = null,
        string? hookPath = null,
        Func<CapturedMessage, MZP1GuardedEngineWiring>? guardedSessionFactory = null,
        MZP1OwnedClientPermit? ownedClient = null,
        Func<CapturedMessage, ProtocolCatalog, IFrameSink, IPacer, MZP1GuardedEngineWiring>? hostBoundGuardedFactory = null)
    {
        Catalog = ProtocolCatalog.LoadFromDirectory(dataDir);
        Proxy = new TcpProxyServer(proxyPort);
        Proxy.Log += m => Log?.Invoke(m);
        var accounts = new AccountManager(accountStorePath ?? Path.Combine(dataDir, "accounts.json"));
        Orchestrator = new BotOrchestrator(Catalog, Proxy, accounts,
            guardedSessionFactory: guardedSessionFactory,
            hostBoundGuardedFactory: hostBoundGuardedFactory);

        // GivreClone est autonome : il injecte SON hook (GivreKama.dll) et fait tourner le proxy lui-meme.
        Capture = new DofusCaptureHost(Proxy,
            hookPath ?? Path.Combine(AppContext.BaseDirectory, "GivreKama.dll"), ownedClient);
        Capture.Log += m => Log?.Invoke(m);
    }

    /// <summary>Démarre la capture uniquement si un client de test unique a été autorisé.</summary>
    public void Start()
    {
        long epoch;
        lock (_lifecycleGate)
        {
            if (_starting || _stopping || _active) return;
            _starting = true;
            epoch = ++_consumerEpoch;
            var handler = new Action<TransportFrameObservation>(
                frame => OnTransportFrame(frame, epoch));
            _transportHandler = handler;
            Orchestrator.TransportFrameObserved += handler;
        }

        bool cancelled;
        try
        {
            // Stop() revokes the old binder and subscriptions first. Never
            // hold the lifecycle lock across a logging or capture callback.
            Orchestrator.Binder.Attach(Proxy);
            if (IsStarting(epoch))
                Log?.Invoke($"[givrebot] catalogue: {Catalog.MappingCount} codes mappes, {Catalog.MessageDefCount} messages .proto.");
            // Log handlers may synchronously call Stop(). Do not start the
            // transport after that cancellation has already been recorded.
            if (IsStarting(epoch)) Capture.Start();
        }
        finally
        {
            lock (_lifecycleGate)
            {
                cancelled = _consumerEpoch != epoch || _stopping;
                // A cancelled Start still owns Binder.Detach/Capture.Stop.
                // Keep _starting until those complete so a concurrent Stop
                // cannot unblock a successor while old cleanup is in flight.
                if (!cancelled) _starting = false;
                _active = !cancelled && Capture.IsRunning;
                if (!_active)
                {
                    if (_transportHandler is { } handler)
                        Orchestrator.TransportFrameObserved -= handler;
                    _transportHandler = null;
                }
            }
            if (cancelled)
            {
                // Stop can race the final call into Capture.Start(). Finish
                // revocation after that call returns, without holding a lock
                // around callbacks that could reenter this host.
                try { Orchestrator.Binder.Detach(Proxy); }
                finally
                {
                    try { Capture.Stop(); }
                    finally
                    {
                        lock (_lifecycleGate)
                        {
                            _starting = false;
                            if (_stopCompleted) _stopping = false;
                        }
                    }
                }
            }
        }
    }

    public void Stop()
    {
        lock (_lifecycleGate)
        {
            if (_stopping) return;
            _stopping = true;
            _stopCompleted = false;
            ++_consumerEpoch;
            _active = false;
            if (_transportHandler is { } handler)
                Orchestrator.TransportFrameObserved -= handler;
            _transportHandler = null;
        }
        // Revoke sessions and event delivery before stopping the transport.
        // Capture.Stop() handles Stop reentered inside its own Start callback.
        try
        {
            try { Orchestrator.Binder.Detach(Proxy); }
            finally { Capture.Stop(); }
        }
        finally
        {
            // Both Stop and a concurrently cancelled Start must finish before
            // a replacement run may attach a new subscription.
            lock (_lifecycleGate)
            {
                _stopCompleted = true;
                if (!_starting) _stopping = false;
            }
        }
    }

    private bool IsStarting(long epoch)
    {
        lock (_lifecycleGate)
            return _starting && !_stopping && _consumerEpoch == epoch;
    }

    private void OnTransportFrame(TransportFrameObservation frame, long epoch)
    {
        if (!IsCurrentFrame(frame, epoch)) return;
        var listeners = TransportFrameObserved;
        if (listeners is null) return;
        foreach (Action<TransportFrameObservation> listener in listeners.GetInvocationList())
        {
            // The first callback can call Stop/Start/EOF, invalidating the
            // remaining subscribers from this same invocation-list snapshot.
            if (!IsCurrentFrame(frame, epoch)) return;
            try { listener(frame); }
            catch { /* One consumer cannot break the TCP forwarding path. */ }
        }
    }

    private bool IsCurrentFrame(TransportFrameObservation frame, long epoch)
    {
        lock (_lifecycleGate)
            return _consumerEpoch == epoch && !_stopping &&
                (_active || (_starting && Capture.IsRunning)) &&
                Orchestrator.IsCurrentTransportLink(frame.Provenance);
    }
}
