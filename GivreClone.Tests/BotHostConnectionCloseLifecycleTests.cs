using System.Reflection;
using GivreClone.Engine;
using GivreCore.Mitm;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Pure in-memory capture events. No TcpProxyServer.Start, listening socket or
/// client process is used; an explicit close notification is not mistaken for
/// a real TCP event supplied by the current dependency.
/// </summary>
public sealed class BotHostConnectionCloseLifecycleTests
{
    private static BotHost Host() => new(
        Path.Combine(Path.GetTempPath(), "givre-host-close-fixture-" + Guid.NewGuid().ToString("N")),
        proxyPort: 15020);

    private static CapturedMessage Capture(uint pid, int connectionId) =>
        new(1, Direction.ServerToClient, connectionId, pid,
            "type.ankama.com/neutral-fixture", Array.Empty<ProtoField>(),
            Array.Empty<byte>());

    private static Action<CapturedMessage> Handler(TcpProxyServer proxy) =>
        Assert.IsType<Action<CapturedMessage>>(typeof(TcpProxyServer)
            .GetField("MessageCaptured", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(proxy));

    private static Action<int> Closed(TcpProxyServer proxy) =>
        Assert.IsType<Action<int>>(typeof(TcpProxyServer)
            .GetField("ConnectionClosed", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(proxy));

    [Fact]
    public void Exact_connection_close_notification_forgets_only_that_connection()
    {
        var host = Host();
        var binder = host.Orchestrator.Binder;
        var emit = Handler(host.Proxy);
        emit(Capture(801u, 71));
        emit(Capture(802u, 72));

        Assert.Equal(71, binder.FindByProcess(801u)?.ConnectionId);
        Assert.Equal(72, binder.FindByProcess(802u)?.ConnectionId);
        // Prefer the actual typed source event. A standalone copy may compile
        // against the older bundled proxy, in which case only the explicit
        // closure port exists and is tested, not an imaginary TCP EOF.
        if (typeof(TcpProxyServer).GetEvent("ConnectionClosed") is not null)
            Closed(host.Proxy)(71);
        else Assert.True(host.Orchestrator.NotifyConnectionClosed(71));
        Assert.False(host.Orchestrator.NotifyConnectionClosed(71)); // idempotent
        Assert.False(host.Orchestrator.NotifyConnectionClosed(99)); // unrelated
        Assert.Null(binder.Find(71));
        Assert.Null(binder.FindByProcess(801u));
        Assert.Equal(72, binder.FindByProcess(802u)?.ConnectionId);

        // A queued message for the closed link must not recreate the session.
        emit(Capture(801u, 71));
        Assert.Single(binder.Sessions);
        Assert.Equal(72, binder.Sessions[0].ConnectionId);

        // A genuinely new connection for the same PID is allowed only after
        // the old connection ID was retired by the structured close notice.
        emit(Capture(801u, 73));
        Assert.Equal(73, binder.FindByProcess(801u)?.ConnectionId);
        Assert.Equal(72, binder.FindByProcess(802u)?.ConnectionId);
        Assert.Equal(2, binder.Sessions.Count);
        host.Stop();
    }

    [Fact]
    public void Host_shutdown_revokes_pid_lookup_even_without_a_running_listener()
    {
        var host = Host();
        Assert.False(host.Capture.IsRunning);
        var emit = Handler(host.Proxy);
        emit(Capture(811u, 81));
        var original = Assert.IsType<BotSession>(host.Orchestrator.Binder.Find(81));

        host.Stop(); // Detach MUST also work when Capture.Stop returns early.

        Assert.Null(host.Orchestrator.Binder.Find(81));
        Assert.Null(host.Orchestrator.Binder.FindByProcess(811u));
        Assert.Empty(host.Orchestrator.AuthenticatedSessions);
        Assert.Same(original, Assert.Single(host.Orchestrator.Sessions)); // diagnostics only
        emit(Capture(811u, 82)); // stale delegate retained before Detach
        Assert.Null(host.Orchestrator.Binder.Find(82));
        host.Stop(); // idempotent
    }

    [Fact]
    public void Restart_reinstalls_only_a_fresh_capture_epoch()
    {
        var host = Host(); // no owned client permit; Capture.Start is fail-closed
        var firstEpoch = Handler(host.Proxy);
        firstEpoch(Capture(821u, 91));
        host.Stop();

        host.Start(); // no permit: no listener, but clean Binder attachment
        Assert.False(host.Capture.IsRunning);
        firstEpoch(Capture(821u, 92)); // old event delegate cannot regain ownership
        Assert.Empty(host.Orchestrator.Sessions);
        Handler(host.Proxy)(Capture(821u, 93));
        Assert.Equal(93, host.Orchestrator.Binder.FindByProcess(821u)?.ConnectionId);
        host.Stop();
    }
}
