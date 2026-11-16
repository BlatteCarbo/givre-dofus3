using System.Diagnostics;
using GivreCore.Injection;
using GivreCore.Mitm;

namespace GivreClone.Engine;

/// <summary>
/// La chaine de capture MITM de GivreClone, headless (analogue de CaptureService de GivreProxy.App, sans UI).
/// GivreClone est autonome : il EST le proxy. Demarre TcpProxyServer et surveille les process Dofus.exe pour
/// injecter SON hook natif (GivreKama.dll) UNIQUEMENT dans un client
/// utilisateur épinglé par PID, naissance, chemin et SHA. Sans autorisation,
/// le démarrage est refusé et aucun autre processus n'est visité.
/// </summary>
public sealed class DofusCaptureHost
{
    private readonly TcpProxyServer _proxy;
    private readonly string _hookPath;
    private readonly MZP1OwnedClientPermit? _ownedClient;
    private readonly HashSet<int> _injected = new();
    private readonly object _gate = new();
    private readonly object _lifecycleGate = new();
    private enum CapturePhase { Stopped, Starting, Running, Stopping }
    private CapturePhase _phase;
    private bool _stopRequested;
    private int _startingThreadId;
    private int _stoppingThreadId;
    private TaskCompletionSource<bool>? _startupFinished;
    private TaskCompletionSource<bool>? _shutdownFinished;
    private Timer? _watch;
    private volatile bool _running;
    private int _polling;

    public event Action<string>? Log;

    public DofusCaptureHost(TcpProxyServer proxy, string hookPath,
        MZP1OwnedClientPermit? ownedClient = null)
    {
        _proxy = proxy;
        _hookPath = hookPath;
        _ownedClient = ownedClient;
    }

    public bool IsRunning => _running;

    public void Start()
    {
        // Fail closed: a generic all-Dofus-PID injector can steal or modify an
        // unrelated, already-running GivreClone session. Without a single pinned
        // owner process there is NO proxy start and NO polling or injection.
        if (_ownedClient?.IsWellFormed != true)
        {
            SafeLog("[capture] Bloqué : client propre non épinglé et authentifié; aucune injection.");
            return;
        }
        TaskCompletionSource<bool> startup;
        lock (_lifecycleGate)
        {
            if (_phase != CapturePhase.Stopped) return;
            _phase = CapturePhase.Starting;
            _stopRequested = false;
            _startingThreadId = Environment.CurrentManagedThreadId;
            startup = _startupFinished =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        Timer? watch = null;
        bool committed = false;
        try
        {
            // No lifecycle lock is held across Start: the proxy can invoke
            // arbitrary Log/ConnectionClosed subscribers synchronously.
            // Start may throw AFTER opening a listener; finally must roll back.
            _proxy.Start();
            watch = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
            lock (_lifecycleGate)
            {
                if (!_stopRequested && _phase == CapturePhase.Starting)
                {
                    // Poll may run after Change, but sees _running=false
                    // until this commit. Never create a timer after Stop.
                    watch.Change(0, 2000);
                    _watch = watch;
                    _running = true;
                    _phase = CapturePhase.Running;
                    committed = true;
                }
            }
            if (committed)
            {
                if (!File.Exists(_hookPath))
                    SafeLog($"[hook] GivreKama.dll absente ({_hookPath}) -> observe-seul : aucune injection, mais un client deja proxifie sera capture.");
                SafeLog("[givrebot] capture demarree.");
            }
        }
        catch
        {
            // Do not trust a throwing proxy Start as meaning no listener exists.
            SafeLog("[proxy] demarrage impossible; capture arretee.");
        }
        finally
        {
            if (!committed)
            {
                watch?.Dispose();
                // Also handles Stop() reentered on the starting thread from a
                // proxy callback. No lock or callback is held by this host.
                try { _proxy.Stop(); } catch { /* best-effort rollback */ }
            }
            lock (_lifecycleGate)
            {
                if (_phase == CapturePhase.Starting) _phase = CapturePhase.Stopped;
                _startingThreadId = 0;
                _startupFinished = null;
            }
            startup.TrySetResult(true);
        }
    }

    public void Stop()
    {
        Task? waitFor = null;
        Timer? watch = null;
        TaskCompletionSource<bool>? shutdown = null;
        lock (_lifecycleGate)
        {
            switch (_phase)
            {
                case CapturePhase.Stopped:
                    return;
                case CapturePhase.Starting:
                    _stopRequested = true;
                    // A synchronous callback from TcpProxyServer.Start may call
                    // Stop on the STARTING thread. Waiting there would deadlock;
                    // Start's finally owns and performs the rollback instead.
                    if (_startingThreadId != Environment.CurrentManagedThreadId)
                        waitFor = _startupFinished?.Task;
                    break;
                case CapturePhase.Stopping:
                    if (_stoppingThreadId != Environment.CurrentManagedThreadId)
                        waitFor = _shutdownFinished?.Task;
                    break;
                case CapturePhase.Running:
                    _phase = CapturePhase.Stopping;
                    _stoppingThreadId = Environment.CurrentManagedThreadId;
                    shutdown = _shutdownFinished =
                        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _running = false;
                    watch = _watch;
                    _watch = null;
                    break;
            }
        }
        if (waitFor is not null)
        {
            waitFor.GetAwaiter().GetResult(); // never under _lifecycleGate
            return;
        }
        if (shutdown is null) return;
        try
        {
            watch?.Dispose();
            // Complete already admitted injection before stopping the proxy.
            lock (_gate) { }
            _proxy.Stop(); // invokes external subscribers outside host locks
        }
        finally
        {
            lock (_lifecycleGate)
            {
                _phase = CapturePhase.Stopped;
                _stoppingThreadId = 0;
                _shutdownFinished = null;
            }
            shutdown.TrySetResult(true);
        }
        SafeLog("[givrebot] capture arretee.");
    }

    private void SafeLog(string message)
    {
        // Neither observer faults nor reentrant lifecycle calls may strand a
        // listening proxy. Invoke subscribers outside all host locks.
        var handlers = Log;
        if (handlers is null) return;
        foreach (Action<string> handler in handlers.GetInvocationList())
            try { handler(message); }
            catch { /* diagnostic subscriber cannot alter capture lifecycle */ }
    }

    private void Poll()
    {
        if (!_running) return;
        if (!File.Exists(_hookPath)) return; // pas de hook -> on n'injecte pas (degrade proprement)
        if (_ownedClient?.IsWellFormed != true) return;
        lock (_gate)
            if (_injected.Contains(_ownedClient.Pid)) return; // no repeated hash/I/O for an admitted PID
        if (Interlocked.Exchange(ref _polling, 1) != 0) return;
        Process? p = null;
        string? injectionLog = null;
        try
        {
            // Never enumerate and inject all Dofus processes: inspect only the
            // explicit test-client PID and verify its birth/path/hash again.
            p = Process.GetProcessById(_ownedClient.Pid);
            if (p.HasExited || !string.Equals(p.ProcessName, "Dofus", StringComparison.OrdinalIgnoreCase) ||
                p.StartTime.ToUniversalTime().Ticks != _ownedClient.ProcessStartUtcTicks ||
                !_ownedClient.MatchesExecutableHash(p.MainModule?.FileName ?? ""))
                return;
            lock (_gate)
            {
                if (!_running || !_injected.Add(_ownedClient.Pid)) return;
                try
                {
                    bool ok = DllInjector.Inject(_ownedClient.Pid, _hookPath);
                    if (ok) injectionLog = "[hook] injection acceptée pour le client propre épinglé.";
                    else
                    {
                        injectionLog = "[hook] injection refusée pour le client propre.";
                        _injected.Remove(_ownedClient.Pid);
                    }
                }
                catch
                {
                    injectionLog = "[hook] erreur d'injection du client propre.";
                    _injected.Remove(_ownedClient.Pid);
                }
            }
            if (injectionLog is not null) SafeLog(injectionLog);
        }
        catch { /* process absent, identité indisponible ou refus de lecture => no-op */ }
        finally { p?.Dispose(); Volatile.Write(ref _polling, 0); }
    }
}
