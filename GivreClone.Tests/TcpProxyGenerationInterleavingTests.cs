using System.Net;
using System.Net.Sockets;
using System.Reflection;
using GivreClone.Engine;
using GivreClone.Protocol;
using GivreCore.Mitm;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Deterministic offline transport-generation interleavings. No socket is
/// started, no upstream connection is created, and no client is launched.
/// </summary>
public sealed class TcpProxyGenerationInterleavingTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private sealed class DeniedAuthority : ITestProxyRouteAuthority
    {
        public bool AdmitClient(TcpClient _, out uint pid) { pid = 0; return false; }
        public bool IsStillOwner(TcpClient _, uint __) => false;
        public bool MayReplay(TcpClient _, uint __, Direction ___) => false;
    }

    private sealed class SilentSink : IFrameSink
    {
        public bool Send(Direction _, byte[] __) => true;
    }

    private static SessionBinder NewBinder() => new(id =>
        new BotSession(id, ProtocolCatalog.LoadFromStrings(
            "message GameMessage { int64 uid = 1; }",
            "{\"type.ankama.com/sample\":\"MessageType.GameMessage\"}",
            null, "{\"GameMessage\":{\"uid\":1}}"),
            new SilentSink(), new NoPacer()));

    private static CapturedMessage Frame(int id, uint pid = 901) =>
        new(1, Direction.ServerToClient, id, pid, "type.ankama.com/sample",
            Array.Empty<ProtoField>(), Array.Empty<byte>());

    private static Action<CapturedMessage> Captured(TcpProxyServer proxy) =>
        Assert.IsType<Action<CapturedMessage>>(typeof(TcpProxyServer)
            .GetField("MessageCaptured", Hidden)!.GetValue(proxy));

    private static Action<int> Closed(TcpProxyServer proxy) =>
        Assert.IsType<Action<int>>(typeof(TcpProxyServer)
            .GetField("ConnectionClosed", Hidden)!.GetValue(proxy));

    [Fact]
    public void Late_close_from_proxy_A_cannot_retire_or_poison_proxy_B_same_numeric_id()
    {
        var a = new TcpProxyServer(15301);
        var b = new TcpProxyServer(15302);
        var binder = NewBinder();
        binder.Attach(a);
        var staleClose = Closed(a); // saved event invocation from prior namespace
        binder.Detach(a);

        binder.Attach(b);
        Captured(b)(Frame(17));
        var current = Assert.IsType<BotSession>(binder.Find(17));
        staleClose(17); // A's closure after B has opened the same numeric ID
        staleClose(18); // A's closure before B's first frame on another ID
        Assert.Same(current, binder.Find(17));
        Captured(b)(Frame(18, 902));
        Assert.NotNull(binder.Find(18));
        Assert.Equal(2, binder.Sessions.Count);
        binder.Detach(b);
    }

    [Fact]
    public void Delayed_close_from_previous_attachment_of_same_proxy_is_ignored()
    {
        var proxy = new TcpProxyServer(15303);
        var binder = NewBinder();
        binder.Attach(proxy);
        var oldClose = Closed(proxy);
        binder.Detach(proxy);

        binder.Attach(proxy);
        oldClose(23); // old subscription generation cannot tombstone new one
        Captured(proxy)(Frame(23));
        Assert.NotNull(binder.Find(23));
        Closed(proxy)(23); // current subscription must retire it
        Assert.Null(binder.Find(23));
        binder.Detach(proxy);
    }

    private static object NewLink(int id, uint pid, long epoch)
    {
        var type = typeof(TcpProxyServer).GetNestedType("Link", BindingFlags.NonPublic)!;
        var ctor = Assert.Single(type.GetConstructors(Hidden | BindingFlags.Public));
        var link = ctor.Invoke(new object[]
        {
            id, pid, new TcpClient(), new TcpClient(), new DeniedAuthority()
        });
        type.GetProperty("RunEpoch")!.SetValue(link, epoch);
        return link;
    }

    private static void Set(TcpProxyServer proxy, string name, object value) =>
        typeof(TcpProxyServer).GetField(name, Hidden)!.SetValue(proxy, value);

    private static bool TryRegister(TcpProxyServer proxy, object link, TcpListener listener,
        long epoch, CancellationToken ct) =>
        (bool)typeof(TcpProxyServer).GetMethod("TryRegisterLink", Hidden)!
            .Invoke(proxy, [link, listener, epoch, ct])!;

    private static bool PumpMayPublish(TcpProxyServer proxy, object link, CancellationToken ct) =>
        (bool)typeof(TcpProxyServer).GetMethod("IsActiveLink", Hidden)!
            .Invoke(proxy, [link, ct])!;

    [Fact]
    public void Stop_invalidates_link_generation_and_forbids_delayed_accept_commit()
    {
        using var proxy = new TcpProxyServer(15304);
        var listener = new TcpListener(IPAddress.Loopback, 15304); // never Start()
        Set(proxy, "_listener", listener);
        Set(proxy, "_runEpoch", 7L);
        Set(proxy, "_hasEverStarted", true);
        var link = NewLink(71, 901, 7);
        Assert.False(TryRegister(proxy, link, listener, 6, CancellationToken.None));
        Assert.True(TryRegister(proxy, link, listener, 7, CancellationToken.None));
        Assert.True(PumpMayPublish(proxy, link, CancellationToken.None));
        Assert.True(proxy.IsCurrentCapturedLink(Frame(71)));
        Assert.False(proxy.IsCurrentCapturedLink(Frame(71, 902))); // wrong owner PID

        proxy.Stop();
        Assert.False(PumpMayPublish(proxy, link, CancellationToken.None));
        Assert.False(proxy.IsCurrentCapturedLink(Frame(71)));
        Assert.False(TryRegister(proxy, NewLink(72, 901, 7), listener, 7, CancellationToken.None));
        Assert.False(TryRegister(proxy, NewLink(73, 901, 8), listener, 8,
            new CancellationToken(canceled: true)));

        // Model the next run's listener/generation without opening a socket.
        // An accept that began on the old listener must not join the next run.
        var nextListener = new TcpListener(IPAddress.Loopback, 15304);
        Set(proxy, "_listener", nextListener);
        Set(proxy, "_runEpoch", 9L);
        Assert.False(TryRegister(proxy, NewLink(74, 901, 7), listener, 7, CancellationToken.None));
        Assert.False(TryRegister(proxy, NewLink(75, 901, 9), listener, 9, CancellationToken.None));
        Assert.True(TryRegister(proxy, NewLink(76, 901, 9), nextListener, 9, CancellationToken.None));
        Assert.True(proxy.IsCurrentCapturedLink(Frame(76)));
        Assert.False(proxy.IsCurrentCapturedLink(Frame(71)));
    }

    [Fact]
    public void Old_pump_capture_after_stop_and_rebind_cannot_open_a_session()
    {
        using var proxy = new TcpProxyServer(15305);
        var binder = NewBinder();
        binder.Attach(proxy);
        var listener = new TcpListener(IPAddress.Loopback, 15305);
        Set(proxy, "_listener", listener);
        Set(proxy, "_runEpoch", 40L);
        Set(proxy, "_hasEverStarted", true);
        Assert.True(TryRegister(proxy, NewLink(90, 901, 40), listener, 40, CancellationToken.None));
        Captured(proxy)(Frame(90));
        Assert.NotNull(binder.Find(90));

        binder.Detach(proxy);
        proxy.Stop(); // genuine link removal and close callback, never a listener
        binder.Attach(proxy);
        // A queued old PumpAsync callback picks up the *new* event invocation.
        Captured(proxy)(Frame(90));
        Captured(proxy)(Frame(91)); // absent link must be rejected too
        Assert.Empty(binder.Sessions);
        binder.Detach(proxy);
    }

    [Fact]
    public void Naked_numeric_close_cannot_retire_a_started_generation_but_its_bound_event_can()
    {
        using var proxy = new TcpProxyServer(15306);
        var binder = NewBinder();
        binder.Attach(proxy);
        var listener = new TcpListener(IPAddress.Loopback, 15306); // never Start()
        Set(proxy, "_listener", listener);
        Set(proxy, "_runEpoch", 88L);
        Set(proxy, "_hasEverStarted", true);
        Assert.True(TryRegister(proxy, NewLink(123, 901, 88),
            listener, 88, CancellationToken.None));
        Captured(proxy)(Frame(123));
        Assert.NotNull(binder.Find(123));

        Assert.False(binder.Forget(123)); // no source/epoch is inadequate
        Assert.NotNull(binder.Find(123));
        Closed(proxy)(123); // transport event was bound to this attachment
        Assert.Null(binder.Find(123));
        binder.Detach(proxy);
    }
}
