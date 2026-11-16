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
/// Isolated loopback socket pairs and private transport interleavings.
/// No game, injection, real proxy listener, or foreign process is used.
/// The fake authority is only suitable for these locally constructed pairs.
/// </summary>
public sealed class TcpProxyEofStopDrainInterleavingTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const uint TestPid = 908;

    private sealed class TestAuthority : ITestProxyRouteAuthority
    {
        private int _ownerChecks;
        public TaskCompletionSource<bool> TwoOwnerChecks { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReplayAdmitted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool AdmitClient(TcpClient _, out uint processId)
        {
            processId = 0;
            return false;
        }

        public bool IsStillOwner(TcpClient _, uint processId)
        {
            if (Interlocked.Increment(ref _ownerChecks) >= 2)
                TwoOwnerChecks.TrySetResult(true);
            return processId == TestPid;
        }

        public bool MayReplay(TcpClient _, uint processId, Direction direction)
        {
            ReplayAdmitted.TrySetResult(true);
            return processId == TestPid && direction == Direction.ClientToServer;
        }
    }

    private sealed class SilentSink : IFrameSink
    {
        public bool Send(Direction _, byte[] __) => false;
    }

    private static SessionBinder Binder() => new(id =>
        new BotSession(id, ProtocolCatalog.LoadFromStrings(
            "message GameMessage { int64 uid = 1; }",
            "{\"TestType\":\"MessageType.GameMessage\"}",
            null, "{\"GameMessage\":{\"uid\":1}}"),
            new SilentSink(), new NoPacer()));

    private static CapturedMessage Capture(int id) =>
        new(1, Direction.ServerToClient, id, TestPid, "TestType", [], []);

    private static Action<CapturedMessage> Captured(TcpProxyServer proxy) =>
        Assert.IsType<Action<CapturedMessage>>(typeof(TcpProxyServer)
            .GetField("MessageCaptured", Hidden)!.GetValue(proxy));

    private static void Set(TcpProxyServer proxy, string field, object value) =>
        typeof(TcpProxyServer).GetField(field, Hidden)!.SetValue(proxy, value);

    private static object Link(int id, TcpClient client, TcpClient upstream,
        TestAuthority authority, long epoch)
    {
        var type = typeof(TcpProxyServer).GetNestedType("Link", BindingFlags.NonPublic)!;
        var ctor = Assert.Single(type.GetConstructors(Hidden | BindingFlags.Public));
        object link = ctor.Invoke([id, TestPid, client, upstream, authority]);
        type.GetProperty("RunEpoch")!.SetValue(link, epoch);
        return link;
    }

    private static bool Register(TcpProxyServer proxy, object link,
        TcpListener listener, long epoch) =>
        (bool)typeof(TcpProxyServer).GetMethod("TryRegisterLink", Hidden)!
            .Invoke(proxy, [link, listener, epoch, CancellationToken.None])!;

    private static bool IsIndexed(TcpProxyServer proxy, int id)
    {
        var index = typeof(TcpProxyServer).GetField("_links", Hidden)!.GetValue(proxy)!;
        return (bool)index.GetType().GetMethod("ContainsKey")!.Invoke(index, [id])!;
    }

    private static TcpListener FakeListener(int port) =>
        new(IPAddress.Loopback, port); // deliberately not started

    private static void Run(TcpProxyServer proxy, TcpListener listener, long epoch)
    {
        Set(proxy, "_listener", listener);
        Set(proxy, "_runEpoch", epoch);
        Set(proxy, "_hasEverStarted", true);
    }

    private static Task Pump(TcpProxyServer proxy, object link,
        TcpClient from, TcpClient to, SemaphoreSlim gate, CancellationToken ct) =>
        (Task)typeof(TcpProxyServer).GetMethod("PumpAsync", Hidden)!
            .Invoke(proxy, [link, from, to, gate, Direction.ClientToServer, ct])!;

    private static SemaphoreSlim Gate(object link, string name) =>
        (SemaphoreSlim)link.GetType().GetProperty(name)!.GetValue(link)!;

    private static async Task<(TcpClient Accepted, TcpClient Peer)> SocketPair()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var peer = new TcpClient(AddressFamily.InterNetwork);
            try
            {
                var connecting = peer.ConnectAsync(endpoint.Address, endpoint.Port);
                var accepted = await listener.AcceptTcpClientAsync().WaitAsync(Deadline);
                await connecting.WaitAsync(Deadline);
                return (accepted, peer);
            }
            catch { peer.Dispose(); throw; }
        }
        finally { listener.Stop(); }
    }

    private static byte[] Framed(byte[] body) =>
        RawProtobuf.EncodeVarint((ulong)body.Length).Concat(body).ToArray();

    [Fact]
    public void Detach_from_first_session_opened_callback_skips_remaining_stale_subscribers()
    {
        using var proxy = new TcpProxyServer(15410);
        var binder = Binder();
        binder.Attach(proxy); // in-memory event only; no socket or listener
        int later = 0;
        binder.SessionOpened += _ => binder.Detach(proxy);
        binder.SessionOpened += _ => Interlocked.Increment(ref later);

        Captured(proxy)(Capture(9));
        Assert.Equal(0, Volatile.Read(ref later));
        Assert.Null(binder.Find(9));
        Assert.Single(binder.Sessions); // diagnostic history, no active lease
    }

    [Fact]
    public void Stop_closes_only_its_snapshot_even_if_close_callback_restarts_proxy_generation()
    {
        using var proxy = new TcpProxyServer(15411);
        var originalListener = FakeListener(15411);
        Run(proxy, originalListener, 10);
        var authority = new TestAuthority();
        var old = Link(10, new TcpClient(), new TcpClient(), authority, 10);
        Assert.True(Register(proxy, old, originalListener, 10));

        var closed = new List<int>();
        object? successor = null;
        proxy.ConnectionClosed += id =>
        {
            closed.Add(id);
            if (id != 10) return;
            // A subscriber can reenter Start or another lifecycle owner. Model
            // the new run under the same registry/registration guard, no socket.
            var freshListener = FakeListener(15411);
            Run(proxy, freshListener, 12);
            successor = Link(11, new TcpClient(), new TcpClient(), authority, 12);
            Assert.True(Register(proxy, successor, freshListener, 12));
        };

        proxy.Stop();
        Assert.Equal(new[] { 10 }, closed);
        Assert.False(IsIndexed(proxy, 10));
        Assert.True(IsIndexed(proxy, 11)); // old Stop must not close new run
        Assert.NotNull(successor);
        proxy.Stop();
        Assert.Equal(new[] { 10, 11 }, closed);
    }

    [Fact]
    public async Task Generation_revoked_while_pump_waits_for_write_gate_never_forwards_stale_frame()
    {
        using var proxy = new TcpProxyServer(15412);
        var incoming = await SocketPair();
        using var source = incoming.Accepted;
        using var sendingPeer = incoming.Peer;
        var outgoing = await SocketPair();
        using var destination = outgoing.Accepted;
        using var receivingPeer = outgoing.Peer;
        var authority = new TestAuthority();
        var listener = FakeListener(15412);
        Run(proxy, listener, 20);
        var link = Link(20, source, destination, authority, 20);
        Assert.True(Register(proxy, link, listener, 20));

        var writeGate = Gate(link, "UpstreamWrite");
        await writeGate.WaitAsync();
        var held = true;
        Task? pump = null;
        try
        {
            pump = Pump(proxy, link, source, destination, writeGate, CancellationToken.None);
            await sendingPeer.GetStream().WriteAsync(Framed([0x08, 0x01]));
            // Two owner checks prove that this frame reached the send gate:
            // one after read, one after framing and before WaitAsync.
            await authority.TwoOwnerChecks.Task.WaitAsync(Deadline);
            Set(proxy, "_runEpoch", 21L); // Stop's revocation before socket close
            writeGate.Release();
            held = false;
            await pump.WaitAsync(Deadline);
            Assert.False(receivingPeer.GetStream().DataAvailable);
            Assert.False(proxy.IsCurrentCapturedLink(Capture(20)));
        }
        finally
        {
            if (held) writeGate.Release();
            proxy.Stop();
            if (pump is not null) await pump.WaitAsync(Deadline);
        }
    }

    [Fact]
    public async Task Stop_while_replay_waits_on_gate_denies_write_without_a_server_ack()
    {
        using var proxy = new TcpProxyServer(15413);
        var incoming = await SocketPair();
        using var client = incoming.Accepted;
        using var clientPeer = incoming.Peer;
        var outgoing = await SocketPair();
        using var upstream = outgoing.Accepted;
        using var upstreamPeer = outgoing.Peer;
        var authority = new TestAuthority();
        var listener = FakeListener(15413);
        Run(proxy, listener, 30);
        var link = Link(30, client, upstream, authority, 30);
        Assert.True(Register(proxy, link, listener, 30));

        var writeGate = Gate(link, "UpstreamWrite");
        await writeGate.WaitAsync();
        bool held = true;
        Task<bool>? replay = null;
        try
        {
            replay = Task.Run(() => proxy.Replay(30, Direction.ClientToServer, [0x08, 0x01]));
            await authority.ReplayAdmitted.Task.WaitAsync(Deadline);
            proxy.Stop();
            writeGate.Release();
            held = false;
            Assert.False(await replay.WaitAsync(Deadline));
            Assert.False(proxy.Replay(30, Direction.ClientToServer, [0x08, 0x01]));
        }
        finally
        {
            if (held) writeGate.Release();
            proxy.Stop();
            if (replay is not null) await replay.WaitAsync(Deadline);
        }
    }

    [Fact]
    public async Task EOF_retires_exact_transport_link_and_binder_refuses_delayed_capture()
    {
        using var proxy = new TcpProxyServer(15414);
        var incoming = await SocketPair();
        using var client = incoming.Accepted;
        using var clientPeer = incoming.Peer;
        var outgoing = await SocketPair();
        using var upstream = outgoing.Accepted;
        using var upstreamPeer = outgoing.Peer;
        var authority = new TestAuthority();
        var listener = FakeListener(15414);
        Run(proxy, listener, 40);
        var link = Link(40, client, upstream, authority, 40);
        Assert.True(Register(proxy, link, listener, 40));

        var binder = Binder();
        binder.Attach(proxy);
        var captured = Captured(proxy);
        captured(Capture(40));
        Assert.NotNull(binder.Find(40));
        var closed = new List<int>();
        proxy.ConnectionClosed += id => closed.Add(id);

        var handling = (Task)typeof(TcpProxyServer)
            .GetMethod("HandleLinkAsync", Hidden)!
            .Invoke(proxy, [link, CancellationToken.None])!;
        clientPeer.Client.Shutdown(SocketShutdown.Send);
        // A single directional FIN now keeps the reply pump alive. Retire
        // the exact source only after the reverse direction also closes.
        upstreamPeer.Client.Shutdown(SocketShutdown.Send);
        await handling.WaitAsync(Deadline);
        Assert.Equal(new[] { 40 }, closed);
        Assert.Null(binder.Find(40));
        Assert.False(proxy.IsCurrentCapturedLink(Capture(40)));
        captured(Capture(40)); // captured invocation list retained before EOF
        Assert.Empty(binder.Sessions);
        Assert.False(proxy.Replay(40, Direction.ClientToServer, [0x08, 0x01]));
        binder.Detach(proxy);
    }

    [Fact]
    public async Task Stop_from_first_capture_callback_skips_later_subscriber_without_deadlock()
    {
        using var proxy = new TcpProxyServer(15415);
        var incoming = await SocketPair();
        using var client = incoming.Accepted;
        using var clientPeer = incoming.Peer;
        var outgoing = await SocketPair();
        using var upstream = outgoing.Accepted;
        using var upstreamPeer = outgoing.Peer;
        var authority = new TestAuthority();
        var listener = FakeListener(15415);
        Run(proxy, listener, 50);
        var link = Link(50, client, upstream, authority, 50);
        Assert.True(Register(proxy, link, listener, 50));

        int first = 0, afterStop = 0;
        proxy.MessageCaptured += _ =>
        {
            Interlocked.Increment(ref first);
            proxy.Stop(); // call from the pump itself; must never await its drain
        };
        proxy.MessageCaptured += _ => Interlocked.Increment(ref afterStop);

        var body = RawProtobuf.Encode([new ProtoField
        {
            Field = 1, Wire = WireType.Len, String = "type.ankama.com/TestType"
        }]);
        var pump = Pump(proxy, link, client, upstream,
            Gate(link, "UpstreamWrite"), CancellationToken.None);
        await clientPeer.GetStream().WriteAsync(Framed(body));
        await pump.WaitAsync(Deadline);

        Assert.Equal(1, Volatile.Read(ref first));
        Assert.Equal(0, Volatile.Read(ref afterStop));
        Assert.False(IsIndexed(proxy, 50));
    }
}
