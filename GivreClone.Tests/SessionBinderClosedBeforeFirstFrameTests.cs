using System.Reflection;
using GivreClone.Engine;
using GivreClone.Protocol;
using GivreCore.Mitm;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Deterministic synthetic event replay, with no listening socket or game.</summary>
public sealed class SessionBinderClosedBeforeFirstFrameTests
{
    private static ProtocolCatalog Catalog() => ProtocolCatalog.LoadFromStrings(
        "message GameMessage { int64 uid = 1; }",
        "{\"type.ankama.com/sample\":\"MessageType.GameMessage\"}",
        null, "{\"GameMessage\":{\"uid\":1}}");

    private sealed class SilentSink : IFrameSink
    {
        public bool Send(Direction _, byte[] __) => true;
    }

    private static SessionBinder Binder() => new(id =>
        new BotSession(id, Catalog(), new SilentSink(), new NoPacer()));

    private static CapturedMessage Frame(int id) =>
        new(1, Direction.ServerToClient, id, 404u, "type.ankama.com/sample",
            Array.Empty<ProtoField>(), Array.Empty<byte>());

    private static Action<CapturedMessage> Handler(TcpProxyServer proxy) =>
        Assert.IsType<Action<CapturedMessage>>(typeof(TcpProxyServer)
            .GetField("MessageCaptured", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(proxy));

    [Fact]
    public void Close_before_the_first_capture_tombstones_even_without_a_session()
    {
        var binder = Binder();
        var proxy = new TcpProxyServer(15111);
        binder.Attach(proxy);
        Assert.False(binder.Forget(10)); // no previously opened session
        Handler(proxy)(Frame(10)); // delayed pump callback for exact closed ID
        Assert.Empty(binder.Sessions);
        Handler(proxy)(Frame(11)); // independently new connection
        Assert.Equal(11, binder.FindByProcess(404)?.ConnectionId);
        Assert.False(binder.Forget(10)); // duplicate close remains idempotent
        binder.Detach(proxy);
    }

    [Fact]
    public void Same_proxy_restart_preserves_explicit_and_open_session_tombstones()
    {
        var binder = Binder();
        var proxy = new TcpProxyServer(15112);
        binder.Attach(proxy);
        var first = Handler(proxy);
        first(Frame(20));
        Assert.NotNull(binder.Find(20));
        Assert.False(binder.Forget(21)); // link closed before first captured frame
        binder.Detach(proxy);

        binder.Attach(proxy);
        var second = Handler(proxy);
        second(Frame(20)); // previously open, then revoked by Detach
        second(Frame(21)); // previously closed before first capture
        Assert.Empty(binder.Sessions);
        second(Frame(22)); // fresh monotone proxy ID may start a new session
        Assert.Equal(22, binder.FindByProcess(404)?.ConnectionId);
        binder.Detach(proxy);
    }

    [Fact]
    public void Different_proxy_can_reuse_an_id_in_its_distinct_namespace()
    {
        var binder = Binder();
        var former = new TcpProxyServer(15113);
        binder.Attach(former);
        Assert.False(binder.Forget(31));
        binder.Detach(former);

        var replacement = new TcpProxyServer(15114);
        binder.Attach(replacement);
        Handler(replacement)(Frame(31));
        Assert.Equal(31, binder.FindByProcess(404)?.ConnectionId);
        binder.Detach(replacement);
    }

    [Fact]
    public void Invalid_close_id_does_not_tombstone_future_valid_connection()
    {
        var binder = Binder();
        var proxy = new TcpProxyServer(15115);
        binder.Attach(proxy);
        Assert.False(binder.Forget(0));
        Assert.False(binder.Forget(-1));
        Handler(proxy)(Frame(1));
        Assert.Equal(1, binder.FindByProcess(404)?.ConnectionId);
        binder.Detach(proxy);
    }
}
