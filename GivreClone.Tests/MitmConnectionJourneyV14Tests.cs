extern alias F06App;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using BotHostService = F06App::GivreClone.App.Services.BotHostService;
using GivreClone.Engine;
using GivreClone.Protocol;
using GivreCore.Mitm;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Offline TCP loopback tests for actual source -> binder -> host callbacks.
/// No DOFUS/game process, hook, external server or gameplay ACK is involved.
/// </summary>
public sealed class MitmConnectionJourneyV14Tests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(7);
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const uint TestPid = 14916;

    private sealed class BlockingFirstOwner : ITestProxyRouteAuthority
    {
        private int _calls;
        private readonly ManualResetEventSlim _release = new();
        public TaskCompletionSource<bool> FirstEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> FirstExited { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool AdmitClient(TcpClient _, out uint independentlyVerifiedProcessId)
        {
            independentlyVerifiedProcessId = TestPid;
            if (Interlocked.Increment(ref _calls) == 1)
            {
                FirstEntered.TrySetResult(true);
                bool released = _release.Wait(TimeSpan.FromSeconds(12));
                FirstExited.TrySetResult(true);
                return released;
            }
            return true;
        }

        public bool IsStillOwner(TcpClient _, uint pid) => pid == TestPid;
        public bool MayReplay(TcpClient _, uint __, Direction ___) => false;
        public void ReleaseFirst() => _release.Set();
    }

    private sealed class OrdinaryOwner : ITestProxyRouteAuthority
    {
        public bool AdmitClient(TcpClient _, out uint pid)
        { pid = TestPid; return true; }
        public bool IsStillOwner(TcpClient _, uint pid) => pid == TestPid;
        public bool MayReplay(TcpClient _, uint __, Direction ___) => false;
    }

    private sealed class DeniedSink : IFrameSink
    {
        public bool Send(Direction _, byte[] __) => false;
    }

    private static int Port()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private static int RelayPort(int upstreamPort)
    {
        int port;
        do { port = Port(); }
        while (port == upstreamPort || port is 5657 or 5658 or 26666);
        return port;
    }

    private static byte[] Frame(byte value)
    {
        var body = RawProtobuf.Encode([new ProtoField
        {
            Field = 1, Wire = WireType.Len, String = "type.ankama.com/TestType"
        }, new ProtoField { Field = 2, Wire = WireType.Varint, Varint = value }]);
        return RawProtobuf.EncodeVarint((ulong)body.Length).Concat(body).ToArray();
    }

    private static SessionBinder Binder()
    {
        var catalog = ProtocolCatalog.LoadFromStrings(
            "message GameMessage { int64 uid = 1; }",
            "{\"TestType\":\"MessageType.GameMessage\"}",
            null, "{\"GameMessage\":{\"uid\":1}}");
        return new SessionBinder(id => new BotSession(id, catalog,
            new DeniedSink(), new NoPacer()));
    }

    [Fact]
    public async Task Slow_first_owner_does_not_block_second_link_fifo_or_binder()
    {
        var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        int proxyPort = RelayPort(upstreamPort);
        var authority = new BlockingFirstOwner();
        using var proxy = new TcpProxyServer(proxyPort);
        var binder = Binder();
        var opened = new TaskCompletionSource<BotSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observations = new ConcurrentQueue<TransportFrameObservation>();
        var twoFrames = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        binder.SessionOpened += session => opened.TrySetResult(session);
        binder.Attach(proxy);
        proxy.TransportFrameObserved += frame =>
        {
            observations.Enqueue(frame);
            if (observations.Count == 2) twoFrames.TrySetResult(true);
        };
        try
        {
            proxy.ConfigureOwnedLoopbackTest(
                new IPEndPoint(IPAddress.Loopback, upstreamPort), authority);
            proxy.Start();
            using var slowClient = new TcpClient(AddressFamily.InterNetwork);
            await slowClient.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            await authority.FirstEntered.Task.WaitAsync(Deadline);

            using var fastClient = new TcpClient(AddressFamily.InterNetwork);
            await fastClient.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var upstreamFast = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
            var first = Frame(1);
            var second = Frame(2);
            await fastClient.GetStream().WriteAsync(first.Concat(second).ToArray());
            var forwarded = new byte[first.Length + second.Length];
            await upstreamFast.GetStream().ReadExactlyAsync(forwarded)
                .AsTask().WaitAsync(Deadline);
            Assert.Equal(first.Concat(second).ToArray(), forwarded);
            await twoFrames.Task.WaitAsync(Deadline);
            var actual = observations.ToArray();
            Assert.Equal(2, actual.Length);
            Assert.True(actual[0].FrameSequence < actual[1].FrameSequence);
            Assert.Equal(actual[0].Provenance, actual[1].Provenance);
            Assert.Equal(TestPid, actual[0].Provenance.ProcessId);
            Assert.Equal(Direction.ClientToServer, actual[0].Direction);
            Assert.True(proxy.IsCurrentTransportLink(actual[0].Provenance));
            var session = await opened.Task.WaitAsync(Deadline);
            Assert.True(binder.TryGetCurrentTransportLink(session, out var bound));
            Assert.Equal(actual[0].Provenance, bound);
            Assert.Equal(2, observations.Count);
        }
        finally
        {
            proxy.Stop();
            authority.ReleaseFirst();
            binder.Detach(proxy);
            upstream.Stop();
        }
    }

    [Fact]
    public async Task Slow_session_factory_cannot_block_other_link_or_exact_eof()
    {
        var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        int proxyPort = RelayPort(upstreamPort);
        using var proxy = new TcpProxyServer(proxyPort);
        var entered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new ManualResetEventSlim();
        var nextOpened = new TaskCompletionSource<BotSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = ProtocolCatalog.LoadFromStrings(
            "message GameMessage { int64 uid = 1; }",
            "{\"TestType\":\"MessageType.GameMessage\"}",
            null, "{\"GameMessage\":{\"uid\":1}}");
        int created = 0;
        var binder = new SessionBinder((CapturedMessage capture) =>
        {
            if (Interlocked.Increment(ref created) == 1)
            {
                entered.TrySetResult(capture.ConnectionId);
                if (!release.Wait(TimeSpan.FromSeconds(12)))
                    throw new TimeoutException("Factory fixture not released.");
                finished.TrySetResult(true);
            }
            return new BotSession(capture.ConnectionId, catalog,
                new DeniedSink(), new NoPacer());
        });
        binder.SessionOpened += session => nextOpened.TrySetResult(session);
        binder.Attach(proxy);
        try
        {
            proxy.ConfigureOwnedLoopbackTest(
                new IPEndPoint(IPAddress.Loopback, upstreamPort), new OrdinaryOwner());
            proxy.Start();

            using var firstClient = new TcpClient(AddressFamily.InterNetwork);
            await firstClient.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var firstUpstream = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
            var firstFrame = Frame(6);
            await firstClient.GetStream().WriteAsync(firstFrame);
            var firstForwarded = new byte[firstFrame.Length];
            await firstUpstream.GetStream().ReadExactlyAsync(firstForwarded)
                .AsTask().WaitAsync(Deadline);
            int blockedId = await entered.Task.WaitAsync(Deadline);

            // While A's SessionBinder factory is blocked, B must still reach
            // its own SessionOpened via the same real proxy subscription.
            using var secondClient = new TcpClient(AddressFamily.InterNetwork);
            await secondClient.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var secondUpstream = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
            var secondFrame = Frame(7);
            await secondClient.GetStream().WriteAsync(secondFrame);
            var secondForwarded = new byte[secondFrame.Length];
            await secondUpstream.GetStream().ReadExactlyAsync(secondForwarded)
                .AsTask().WaitAsync(Deadline);
            Assert.Equal(secondFrame, secondForwarded);
            var live = await nextOpened.Task.WaitAsync(Deadline);
            Assert.NotEqual(blockedId, live.ConnectionId);
            Assert.True(binder.TryGetCurrentTransportLink(live, out var proof));
            Assert.True(proxy.IsCurrentTransportLink(proof));

            // Stop publishes the exact-link EOF on both links without waiting
            // for an unrelated factory under the binder's collection lock.
            await Task.Run(proxy.Stop).WaitAsync(Deadline);
            Assert.Null(binder.Find(live.ConnectionId));
            Assert.False(binder.TryGetCurrentTransportLink(live, out _));
            release.Set();
            await finished.Task.WaitAsync(Deadline);
        }
        finally
        {
            release.Set();
            binder.Detach(proxy);
            proxy.Stop();
            upstream.Stop();
        }
    }

    [Fact]
    public async Task Late_blocked_admission_cannot_enter_restarted_host_or_service()
    {
        var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        int proxyPort = RelayPort(upstreamPort);
        var authority = new BlockingFirstOwner();
        var permit = new MZP1OwnedClientPermit(int.MaxValue, 1, "3.7.10.10",
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".exe"),
            new string('A', 64));
        Assert.True(permit.IsWellFormed);
        var host = new BotHost(Path.Combine(Path.GetTempPath(),
            "v14-host-" + Guid.NewGuid().ToString("N")), proxyPort: proxyPort,
            hookPath: Path.Combine(Path.GetTempPath(), "missing-v14-hook.dll"),
            ownedClient: permit);
        host.Proxy.ConfigureOwnedLoopbackTest(
            new IPEndPoint(IPAddress.Loopback, upstreamPort), authority);
        var service = new BotHostService();
        var observed = new ConcurrentQueue<TransportFrameObservation>();
        var oneFrame = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.TransportFrameObserved += frame =>
        {
            observed.Enqueue(frame);
            oneFrame.TrySetResult(true);
        };

        try
        {
            // This read-only test bridge exercises the service's real callback
            // without StartBound's unrelated account-store/launcher gate.
            var serviceType = typeof(BotHostService);
            serviceType.GetField("_host", Hidden)!.SetValue(service, host);
            serviceType.GetField("_hostEpoch", Hidden)!.SetValue(service, 51L);
            var onFrame = serviceType.GetMethod("OnHostFrame", Hidden)!;
            host.TransportFrameObserved += frame =>
                onFrame.Invoke(service, [host, 51L, frame]);

            host.Start();
            Assert.True(host.Capture.IsRunning);
            long priorRun = host.Proxy.CurrentRunEpoch;
            var oldProxyCallback = typeof(TcpProxyServer)
                .GetField("TransportFrameObserved", Hidden)!.GetValue(host.Proxy) as
                Action<TransportFrameObservation>;
            Assert.NotNull(oldProxyCallback);

            using var delayedClient = new TcpClient(AddressFamily.InterNetwork);
            await delayedClient.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            await authority.FirstEntered.Task.WaitAsync(Deadline);
            host.Stop();
            Assert.False(host.Capture.IsRunning);
            host.Start();
            Assert.True(host.Capture.IsRunning);
            Assert.True(host.Proxy.CurrentRunEpoch > priorRun);

            using var currentClient = new TcpClient(AddressFamily.InterNetwork);
            await currentClient.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var accepted = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
            var frame = Frame(3);
            await currentClient.GetStream().WriteAsync(frame);
            var forwarded = new byte[frame.Length];
            await accepted.GetStream().ReadExactlyAsync(forwarded)
                .AsTask().WaitAsync(Deadline);
            Assert.Equal(frame, forwarded);
            await oneFrame.Task.WaitAsync(Deadline);
            var current = Assert.Single(observed).Provenance;
            Assert.Equal(host.Proxy.CurrentRunEpoch, current.RunEpoch);
            Assert.True(service.IsCurrentTransportLink(current));

            // Retained old transport callback, supplied with otherwise-current
            // bytes and source, must not pass the previous host consumer epoch.
            oldProxyCallback!(new TransportFrameObservation(100,
                Direction.ClientToServer, current, [0x08, 0x01]));
            Assert.Single(observed);
            authority.ReleaseFirst();
            await authority.FirstExited.Task.WaitAsync(Deadline);
            Assert.True(service.IsCurrentTransportLink(current));
            Assert.Single(observed);
        }
        finally
        {
            authority.ReleaseFirst();
            service.Stop();
            host.Stop();
            host.Proxy.Dispose();
            upstream.Stop();
        }
    }

    [Fact]
    public async Task Admission_capacity_blocks_accept_until_a_permit_is_released()
    {
        var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        int proxyPort = RelayPort(upstreamPort);
        using var proxy = new TcpProxyServer(proxyPort);
        var admissionGate = Assert.IsType<SemaphoreSlim>(typeof(TcpProxyServer)
            .GetField("_pendingAdmissions", Hidden)!.GetValue(proxy));
        int held = 0;
        while (admissionGate.Wait(0)) held++;
        Assert.InRange(held, 1, 64); // bounded capacity, no global queue
        try
        {
            proxy.ConfigureOwnedLoopbackTest(
                new IPEndPoint(IPAddress.Loopback, upstreamPort),
                new OrdinaryOwner());
            proxy.Start();
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            var inbound = upstream.AcceptTcpClientAsync();
            Assert.False(inbound.IsCompleted); // nothing accepted by the relay
            admissionGate.Release();
            held--;
            using var accepted = await inbound.WaitAsync(Deadline);
            var frame = Frame(4);
            await client.GetStream().WriteAsync(frame);
            var forwarded = new byte[frame.Length];
            await accepted.GetStream().ReadExactlyAsync(forwarded)
                .AsTask().WaitAsync(Deadline);
            Assert.Equal(frame, forwarded);
        }
        finally
        {
            if (held > 0) admissionGate.Release(held);
            proxy.Stop();
            upstream.Stop();
        }
    }
}
