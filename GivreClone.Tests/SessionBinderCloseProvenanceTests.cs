using System.Reflection;
using GivreClone.Engine;
using GivreCore.Mitm;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Closed-source callback simulation; no socket Start or client.</summary>
public sealed class SessionBinderCloseProvenanceTests
{
    private static BotHost Host() => new(
        Path.Combine(Path.GetTempPath(), "givre-close-provenance-" + Guid.NewGuid().ToString("N")),
        proxyPort: 15150);

    private static CapturedMessage Frame(int id) =>
        new(1, Direction.ServerToClient, id, 202u, "type.ankama.com/sample",
            Array.Empty<ProtoField>(), Array.Empty<byte>());

    private static Action<CapturedMessage> Capture(TcpProxyServer proxy) =>
        Assert.IsType<Action<CapturedMessage>>(typeof(TcpProxyServer)
            .GetField("MessageCaptured", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(proxy));

    private static Action<int> Closed(TcpProxyServer proxy) =>
        Assert.IsType<Action<int>>(typeof(TcpProxyServer)
            .GetField("ConnectionClosed", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(proxy));

    [Fact]
    public void Delayed_close_from_old_proxy_cannot_retire_same_number_in_new_proxy()
    {
        var host = Host();
        var oldProxy = host.Proxy;
        var binder = host.Orchestrator.Binder;
        var staleClose = Closed(oldProxy); // real orchestrator callback captures oldProxy
        binder.Detach(oldProxy);

        using var nextProxy = new TcpProxyServer(15151);
        binder.Attach(nextProxy);
        var capture = Capture(nextProxy);
        capture(Frame(1)); // new proxy reuses its own ID=1
        Assert.Equal(1, binder.FindByProcess(202u)?.ConnectionId);
        staleClose(1);
        Assert.Equal(1, binder.FindByProcess(202u)?.ConnectionId);

        staleClose(2); // old closure must not poison the next ID before first capture
        capture(Frame(2));
        Assert.Equal(2, binder.FindByProcess(202u)?.ConnectionId);

        Assert.True(binder.ForgetFrom(nextProxy, 2)); // actual new source is valid
        Assert.Null(binder.Find(2));
        capture(Frame(2));
        Assert.Empty(binder.Sessions);
        binder.Detach(nextProxy);
        host.Stop();
    }

    [Fact]
    public void Explicit_source_scoped_close_after_detach_is_retained_until_same_proxy_reattaches()
    {
        var host = Host();
        var proxy = host.Proxy;
        var binder = host.Orchestrator.Binder;
        binder.Detach(proxy);
        // Detach correctly unsubscribes the old event handler. The explicit
        // source-scoped port is the only supported close after detachment.
        Assert.False(binder.ForgetFrom(proxy, 5));
        binder.Attach(proxy);
        Capture(proxy)(Frame(5));
        Assert.Empty(binder.Sessions);
        Capture(proxy)(Frame(6)); // fresh ID still accepted
        Assert.Equal(6, binder.FindByProcess(202u)?.ConnectionId);
        host.Stop();
    }
}
