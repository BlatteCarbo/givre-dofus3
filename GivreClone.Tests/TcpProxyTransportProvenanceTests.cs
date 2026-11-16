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
/// Offline provenance tests. Listener instances used for transport registry
/// tests are never started; the only real sockets are isolated loopback pairs.
/// No game client, production relay, or server ACK is involved.
/// </summary>
public sealed class TcpProxyTransportProvenanceTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const uint Pid = 916;

    private sealed class TestAuthority : ITestProxyRouteAuthority
    {
        public bool AdmitClient(TcpClient _, out uint pid)
        {
            pid = 0;
            return false;
        }
        public bool IsStillOwner(TcpClient _, uint pid) => pid == Pid;
        public bool MayReplay(TcpClient _, uint pid, Direction direction) =>
            pid == Pid && direction == Direction.ClientToServer;
    }

    private sealed class SilentSink : IFrameSink
    {
        public bool Send(Direction _, byte[] __) => false;
    }

    private static SessionBinder Binder() => new(id => new BotSession(id,
        ProtocolCatalog.LoadFromStrings(
            "message GameMessage { int64 uid = 1; }",
            "{\"TestType\":\"MessageType.GameMessage\"}",
            null, "{\"GameMessage\":{\"uid\":1}}"),
        new SilentSink(), new NoPacer()));

    private static void Set(TcpProxyServer proxy, string name, object value) =>
        typeof(TcpProxyServer).GetField(name, Hidden)!.SetValue(proxy, value);

    private static (TcpListener Listener, CancellationTokenSource Stop) ArrangeRun(
        TcpProxyServer proxy, int port, long epoch)
    {
        // The active source marker models Start's token, without opening a
        // TCP proxy port. Stop() owns disposal of the token.
        var listener = new TcpListener(IPAddress.Loopback, port);
        var stop = new CancellationTokenSource();
        Set(proxy, "_listener", listener);
        Set(proxy, "_stop", stop);
        Set(proxy, "_runEpoch", epoch);
        Set(proxy, "_hasEverStarted", true);
        return (listener, stop);
    }

    private static object NewLink(int connectionId, long epoch,
        TcpClient client, TcpClient upstream)
    {
        var type = typeof(TcpProxyServer).GetNestedType("Link", BindingFlags.NonPublic)!;
        var ctor = Assert.Single(type.GetConstructors(Hidden | BindingFlags.Public));
        object link = ctor.Invoke([connectionId, Pid, client, upstream, new TestAuthority()]);
        type.GetProperty("RunEpoch")!.SetValue(link, epoch);
        return link;
    }

    private static TransportLinkProvenance Admit(TcpProxyServer proxy,
        TcpListener listener, object link, int id, long epoch)
    {
        Assert.True((bool)typeof(TcpProxyServer).GetMethod("TryRegisterLink", Hidden)!
            .Invoke(proxy, [link, listener, epoch, CancellationToken.None])!);
        var token = (long)link.GetType().GetProperty("LinkToken")!.GetValue(link)!;
        Assert.True(token > 0);
        var evidence = new TransportLinkProvenance(proxy.ProxyInstanceId, epoch,
            token, id, Pid);
        Assert.True(proxy.IsCurrentTransportLink(evidence));
        return evidence;
    }

    private static Action<CapturedMessage> Captured(TcpProxyServer proxy) =>
        Assert.IsType<Action<CapturedMessage>>(typeof(TcpProxyServer)
            .GetField("MessageCaptured", Hidden)!.GetValue(proxy));

    private static void Close(TcpProxyServer proxy, object link) =>
        typeof(TcpProxyServer).GetMethod("CloseLink", Hidden)!.Invoke(proxy, [link]);

    private static CapturedMessage Capture(int id, TransportLinkProvenance? evidence,
        uint pid = Pid) => new(1, Direction.ServerToClient, id, pid,
        "TestType", [], []) { Provenance = evidence };

    private static Task Pump(TcpProxyServer proxy, object link,
        TcpClient from, TcpClient to, SemaphoreSlim gate) =>
        (Task)typeof(TcpProxyServer).GetMethod("PumpAsync", Hidden)!
            .Invoke(proxy, [link, from, to, gate, Direction.ClientToServer,
                CancellationToken.None])!;

    private static SemaphoreSlim Gate(object link) =>
        (SemaphoreSlim)link.GetType().GetProperty("UpstreamWrite")!.GetValue(link)!;

    private static async Task<(TcpClient Local, TcpClient Peer)> Pair()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var peer = new TcpClient(AddressFamily.InterNetwork);
            try
            {
                var connected = peer.ConnectAsync(endpoint.Address, endpoint.Port);
                var local = await listener.AcceptTcpClientAsync().WaitAsync(Deadline);
                await connected.WaitAsync(Deadline);
                return (local, peer);
            }
            catch { peer.Dispose(); throw; }
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void Real_link_requires_exact_proxy_epoch_and_token_even_with_same_id_and_pid()
    {
        using var proxy = new TcpProxyServer(15501);
        var (listener, _) = ArrangeRun(proxy, 15501, 19);
        var link = NewLink(7, 19, new TcpClient(), new TcpClient());
        var current = Admit(proxy, listener, link, 7, 19);

        Assert.True(proxy.IsCurrentCapturedLink(Capture(7, current)));
        Assert.False(proxy.IsCurrentCapturedLink(Capture(7, null)));
        Assert.False(proxy.IsCurrentCapturedLink(Capture(7, current with { LinkToken = current.LinkToken + 1 })));
        Assert.False(proxy.IsCurrentCapturedLink(Capture(7, current with { RunEpoch = 18 })));
        Assert.False(proxy.IsCurrentCapturedLink(Capture(7,
            current with { ProxyInstanceId = Guid.NewGuid() })));
        Assert.False(proxy.IsCurrentCapturedLink(Capture(7, current, pid: Pid + 1)));
        Assert.False(proxy.IsCurrentTransportLink(current with { LinkToken = current.LinkToken + 1 }));

        var binder = Binder();
        binder.Attach(proxy);
        Captured(proxy)(Capture(7, null));
        Assert.Empty(binder.Sessions); // legacy callback cannot create a real lease
        Captured(proxy)(Capture(7, current));
        var session = Assert.IsType<BotSession>(binder.Find(7));
        Assert.True(binder.TryGetCurrentTransportLink(session, out var observed));
        Assert.Equal(current, observed);
        binder.Detach(proxy);
        Assert.False(binder.TryGetCurrentTransportLink(session, out _));
    }

    [Fact]
    public void Old_close_provenance_and_late_numeric_close_cannot_retire_successor_same_id()
    {
        using var proxy = new TcpProxyServer(15502);
        var (listener, _) = ArrangeRun(proxy, 15502, 22);
        var first = NewLink(8, 22, new TcpClient(), new TcpClient());
        var oldIdentity = Admit(proxy, listener, first, 8, 22);
        var binder = Binder();
        binder.Attach(proxy);
        Captured(proxy)(Capture(8, oldIdentity));
        var previous = Assert.IsType<BotSession>(binder.Find(8));

        TransportLinkProvenance? replacement = null;
        var order = new List<string>();
        proxy.TransportLinkClosed += proof =>
        {
            Assert.Equal(oldIdentity, proof);
            order.Add("transport");
            // Binder's earlier subscriber already retired the old lease.
            var next = NewLink(8, 22, new TcpClient(), new TcpClient());
            replacement = Admit(proxy, listener, next, 8, 22);
            Captured(proxy)(Capture(8, replacement));
        };
        proxy.ConnectionClosed += id =>
        {
            Assert.Equal(8, id);
            order.Add("legacy");
        };

        Close(proxy, first);
        Assert.Equal(new[] { "transport", "legacy" }, order);
        Assert.NotNull(replacement);
        var successor = Assert.IsType<BotSession>(binder.Find(8));
        Assert.NotSame(previous, successor);
        Assert.True(binder.TryGetCurrentTransportLink(successor, out var current));
        Assert.Equal(replacement.Value, current);
        Assert.NotEqual(oldIdentity.LinkToken, current.LinkToken);

        // Old capture and old close evidence may arrive after the replacement.
        Captured(proxy)(Capture(8, oldIdentity));
        Assert.False(binder.ForgetFrom(proxy, oldIdentity));
        Assert.False(binder.ForgetFrom(proxy, 8)); // ID-only source cannot close a live run
        Assert.Same(successor, binder.Find(8));
        Assert.False(proxy.IsCurrentTransportLink(oldIdentity));
        Assert.True(proxy.IsCurrentTransportLink(current));
        binder.Detach(proxy);
    }

    [Fact]
    public void Old_proxy_cannot_close_different_proxys_same_numeric_connection()
    {
        using var oldProxy = new TcpProxyServer(15503);
        using var newProxy = new TcpProxyServer(15504);
        var (oldListener, _) = ArrangeRun(oldProxy, 15503, 33);
        var (newListener, _) = ArrangeRun(newProxy, 15504, 33);
        var oldIdentity = Admit(oldProxy, oldListener,
            NewLink(9, 33, new TcpClient(), new TcpClient()), 9, 33);
        var newIdentity = Admit(newProxy, newListener,
            NewLink(9, 33, new TcpClient(), new TcpClient()), 9, 33);
        var binder = Binder();
        binder.Attach(newProxy);
        Captured(newProxy)(Capture(9, newIdentity));
        var current = Assert.IsType<BotSession>(binder.Find(9));

        Assert.False(binder.ForgetFrom(oldProxy, oldIdentity));
        Assert.False(newProxy.IsCurrentTransportLink(oldIdentity));
        Assert.Same(current, binder.Find(9));
        binder.Detach(newProxy);
    }

    [Fact]
    public async Task Real_pump_issues_provenance_and_preserves_single_direction_fifo()
    {
        using var proxy = new TcpProxyServer(15505);
        var (listener, _) = ArrangeRun(proxy, 15505, 44);
        var incoming = await Pair();
        using var from = incoming.Local;
        using var sending = incoming.Peer;
        var outgoing = await Pair();
        using var to = outgoing.Local;
        using var receiving = outgoing.Peer;
        var link = NewLink(10, 44, from, to);
        var identity = Admit(proxy, listener, link, 10, 44);
        var receivedCaptures = new List<CapturedMessage>();
        var receivedFrames = new List<TransportFrameObservation>();
        var callbackOrder = new List<string>();
        proxy.TransportFrameObserved += observation =>
        {
            receivedFrames.Add(observation);
            callbackOrder.Add("raw");
        };
        proxy.MessageCaptured += capture =>
        {
            receivedCaptures.Add(capture);
            callbackOrder.Add("decoded");
        };

        var body = RawProtobuf.Encode([new ProtoField
        {
            Field = 1, Wire = WireType.Len, String = "type.ankama.com/TestType"
        }]);
        var framed = RawProtobuf.EncodeVarint((ulong)body.Length)
            .Concat(body).ToArray();
        var pump = Pump(proxy, link, from, to, Gate(link));
        await sending.GetStream().WriteAsync(framed);
        await sending.GetStream().WriteAsync(framed);
        var forwarded = new byte[framed.Length * 2];
        await receiving.GetStream().ReadExactlyAsync(forwarded).AsTask().WaitAsync(Deadline);
        Assert.Equal(framed.Concat(framed).ToArray(), forwarded);

        sending.Client.Shutdown(SocketShutdown.Send);
        await pump.WaitAsync(Deadline);
        Assert.Equal(new[] { "raw", "decoded", "raw", "decoded" }, callbackOrder);
        Assert.Equal(2, receivedFrames.Count);
        Assert.All(receivedFrames, f =>
        {
            Assert.Equal(identity, f.Provenance);
            Assert.Equal(Direction.ClientToServer, f.Direction);
            Assert.Equal(body, f.RawFrame);
        });
        Assert.True(receivedFrames[0].FrameSequence < receivedFrames[1].FrameSequence);
        Assert.Equal(2, receivedCaptures.Count);
        Assert.All(receivedCaptures, c =>
        {
            Assert.True(c.Provenance.HasValue);
            Assert.Equal(identity, c.Provenance.Value);
            Assert.True(proxy.IsCurrentCapturedLink(c));
        });
        Assert.True(receivedCaptures[0].Seq < receivedCaptures[1].Seq);
    }

    [Fact]
    public async Task Pinned_replay_rejects_retired_link_after_replacement_but_allows_current_local_write()
    {
        using var proxy = new TcpProxyServer(15506);
        var (listener, _) = ArrangeRun(proxy, 15506, 55);
        var old = NewLink(11, 55, new TcpClient(), new TcpClient());
        var oldIdentity = Admit(proxy, listener, old, 11, 55);
        Close(proxy, old);

        var incoming = await Pair();
        using var client = incoming.Local;
        using var clientPeer = incoming.Peer;
        var outgoing = await Pair();
        using var upstream = outgoing.Local;
        using var upstreamPeer = outgoing.Peer;
        var next = NewLink(11, 55, client, upstream);
        var newIdentity = Admit(proxy, listener, next, 11, 55);

        byte[] bytes = [0x08, 0x01];
        Assert.False(proxy.Replay(11, Direction.ClientToServer, bytes));
        Assert.False(proxy.Replay(oldIdentity, Direction.ClientToServer, bytes));
        Assert.False(proxy.Replay(newIdentity with { LinkToken = 0 },
            Direction.ClientToServer, bytes));
        Assert.True(proxy.Replay(newIdentity, Direction.ClientToServer, bytes));
        var received = new byte[3]; // length prefix then 2 protobuf bytes
        await upstreamPeer.GetStream().ReadExactlyAsync(received).AsTask().WaitAsync(Deadline);
        Assert.Equal(new byte[] { 2, 0x08, 0x01 }, received);

        proxy.Stop();
        Assert.False(proxy.Replay(newIdentity, Direction.ClientToServer, bytes));
    }

    [Fact]
    public async Task Stop_inside_raw_source_callback_suppresses_later_decode_without_waiting_for_pump()
    {
        using var proxy = new TcpProxyServer(15507);
        var (listener, _) = ArrangeRun(proxy, 15507, 66);
        var incoming = await Pair();
        using var from = incoming.Local;
        using var sending = incoming.Peer;
        var outgoing = await Pair();
        using var to = outgoing.Local;
        using var receiving = outgoing.Peer;
        var link = NewLink(12, 66, from, to);
        var identity = Admit(proxy, listener, link, 12, 66);
        int raw = 0, decoded = 0;
        proxy.TransportFrameObserved += observed =>
        {
            Assert.Equal(identity, observed.Provenance);
            Interlocked.Increment(ref raw);
            proxy.Stop();
        };
        proxy.MessageCaptured += _ => Interlocked.Increment(ref decoded);

        var body = RawProtobuf.Encode([new ProtoField
        {
            Field = 1, Wire = WireType.Len, String = "type.ankama.com/TestType"
        }]);
        var framed = RawProtobuf.EncodeVarint((ulong)body.Length)
            .Concat(body).ToArray();
        var pump = Pump(proxy, link, from, to, Gate(link));
        await sending.GetStream().WriteAsync(framed);
        await pump.WaitAsync(Deadline);
        Assert.Equal(1, Volatile.Read(ref raw));
        Assert.Equal(0, Volatile.Read(ref decoded));
        Assert.False(proxy.IsCurrentTransportLink(identity));
    }
}
