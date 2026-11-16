// TEST ONLY. Never starts sockets, watches processes, opens accounts or injects anything.
// The real BotHostService.cs compiles and calls these compatible Host type surfaces.
using GivreClone.Engine.Recovery;
using GivreClone.Protocol;
using GivreCore.Mitm;

namespace GivreClone.Engine;

public sealed class BotHost
{
    public event Action<string>? Log;
    public OfflineOrchestrator Orchestrator { get; } = new();
    public OfflineCapture Capture { get; } = new();

    public BotHost(string dataDir, int proxyPort, string? accountStorePath,
        string? hookPath = null,
        Func<CapturedMessage, MZP1GuardedEngineWiring>? guardedSessionFactory = null,
        MZP1OwnedClientPermit? ownedClient = null,
        Func<CapturedMessage, ProtocolCatalog, IFrameSink, IPacer, MZP1GuardedEngineWiring>? hostBoundGuardedFactory = null)
        => throw new InvalidOperationException("Offline BotHost construction is prohibited.");

    public void Start() => throw new InvalidOperationException("Offline BotHost.Start prohibited.");
    public void Stop() => throw new InvalidOperationException("Offline BotHost.Stop prohibited.");
}

public sealed class OfflineOrchestrator
{
    public OfflineBinder Binder { get; } = new();
    public IReadOnlyList<int> Sessions => Array.Empty<int>();
    public IReadOnlyList<int> AuthenticatedSessions => Array.Empty<int>();
}

public sealed class OfflineBinder
{
    public event Action<int>? SessionOpened;
    public event Action<int>? SessionAuthenticated;
}

public sealed class OfflineCapture
{
    public bool IsRunning => false;
}
