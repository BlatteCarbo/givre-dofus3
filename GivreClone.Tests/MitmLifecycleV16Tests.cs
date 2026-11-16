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
/// Only local loopback sockets and independent synthetic owner authority.
/// No DOFUS process, hook, public upstream or server/gameplay ACK.
/// </summary>
public sealed class MitmLifecycleV16Tests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(7);
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    private const uint TestPid = 34126;

    private sealed class OwnerAuthority : ITestProxyRouteAuthority
    {
        private readonly ManualResetEventSlim _releaseOwner = new(true);
        private int _stallNextOwner;
        private int _replayAllowed = 1;
        private int _replayChecks;

        public TaskCompletionSource<bool> OwnerCheckEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> FirstReplayCheck { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action? AfterFirstReplayDecisionLatched { get; set; }

        public bool AdmitClient(TcpClient _, out uint pid)
        {
            pid = TestPid;
            return true;
        }

        public bool IsStillOwner(TcpClient _, uint pid)
        {
            if (Interlocked.Exchange(ref _stallNextOwner, 0) != 0)
            {
                OwnerCheckEntered.TrySetResult(true);
                if (!_releaseOwner.Wait(TimeSpan.FromSeconds(15))) return false;
            }
            return pid == TestPid;
        }

        public bool MayReplay(TcpClient _, uint pid, Direction __)
        {
            int check = Interlocked.Increment(ref _replayChecks);
            bool allowed = pid == TestPid && Volatile.Read(ref _replayAllowed) != 0;
            if (check == 1)
            {
                // The first decision is already latched. Permit the fixture
                // to mutate/revoke while its real TCP write gate is held,
                // without racing the 1.5s production gate timeout against
                // thread-pool scheduling under a full test-suite load.
                AfterFirstReplayDecisionLatched?.Invoke();
                FirstReplayCheck.TrySetResult(true);
            }
            return allowed;
        }

        public void BlockNextOwnerCheck()
        {
            _releaseOwner.Reset();
            Volatile.Write(ref _stallNextOwner, 1);
        }

        public void ReleaseOwner() => _releaseOwner.Set();
        public void RevokeReplay() => Volatile.Write(ref _replayAllowed, 0);
        public int ReplayChecks => Volatile.Read(ref _replayChecks);
    }

    private sealed class DeniedSink : IFrameSink
    {
        public bool Send(Direction _, byte[] __) => false;
    }

    private static SessionBinder Binder() => new((CapturedMessage captured) =>
        new BotSession(captured.ConnectionId,
            ProtocolCatalog.LoadFromStrings(
                "message GameMessage { int64 uid = 1; }",
                "{\"TestType\":\"MessageType.GameMessage\"}",
                null, "{\"GameMessage\":{\"uid\":1}}"),
            new DeniedSink(), new NoPacer()));

    private static TcpListener Upstream()
    {
        while (true)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            if (port >= 1024 && port is not (5657 or 5658 or 26666))
                return listener;
            listener.Stop();
        }
    }

    private static int ProxyPort(int upstreamPort)
    {
        while (true)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            if (port >= 1024 && port != upstreamPort &&
                port is not (5657 or 5658 or 26666)) return port;
        }
    }

    private static byte[] Framed(byte[] body) =>
        RawProtobuf.EncodeVarint((ulong)body.Length).Concat(body).ToArray();

    private static byte[] TypedBody(byte value) => RawProtobuf.Encode(
    [
        new ProtoField { Field = 1, Wire = WireType.Len,
            String = "type.ankama.com/TestType" },
        new ProtoField { Field = 2, Wire = WireType.Varint, Varint = value }
    ]);

    [Fact]
    public async Task Non_protobuf_but_well_framed_body_does_not_drop_later_fifo_frames_or_session()
    {
        var upstream = Upstream();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        int proxyPort = ProxyPort(upstreamPort);
        using var proxy = new TcpProxyServer(proxyPort);
        var owner = new OwnerAuthority();
        var binder = Binder();
        var raw = new ConcurrentQueue<TransportFrameObservation>();
        var typed = new ConcurrentQueue<CapturedMessage>();
        var receivedThree = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedFour = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedTwoTyped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = new TaskCompletionSource<BotSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var eof = new TaskCompletionSource<TransportLinkProvenance>(TaskCreationOptions.RunContinuationsAsynchronously);

        proxy.TransportFrameObserved += frame =>
        {
            raw.Enqueue(frame);
            if (raw.Count == 3) receivedThree.TrySetResult(true);
            if (raw.Count == 4) receivedFour.TrySetResult(true);
        };
        proxy.MessageCaptured += capture =>
        {
            typed.Enqueue(capture);
            if (typed.Count == 2) receivedTwoTyped.TrySetResult(true);
        };
        proxy.TransportLinkClosed += identity => eof.TrySetResult(identity);
        binder.SessionOpened += session => opened.TrySetResult(session);
        binder.Attach(proxy);

        try
        {
            proxy.ConfigureOwnedLoopbackTest(
                new IPEndPoint(IPAddress.Loopback, upstreamPort), owner);
            proxy.Start();
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var destination = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);

            // The first payload has valid varint framing but an invalid
            // protobuf tag. It has no type_url or gameplay meaning.
            byte[] untyped = Framed([0x07]);
            byte[] first = Framed(TypedBody(1));
            byte[] second = Framed(TypedBody(2));
            byte[] batch = untyped.Concat(first).Concat(second).ToArray();
            await client.GetStream().WriteAsync(batch);
            var forwarded = new byte[batch.Length];
            await destination.GetStream().ReadExactlyAsync(forwarded)
                .AsTask().WaitAsync(Deadline);
            Assert.Equal(batch, forwarded); // exact byte-for-byte FIFO
            await receivedThree.Task.WaitAsync(Deadline);
            await receivedTwoTyped.Task.WaitAsync(Deadline);
            var observations = raw.ToArray();
            Assert.Equal(3, observations.Length);
            Assert.All(observations, item =>
                Assert.Equal(observations[0].Provenance, item.Provenance));
            Assert.True(observations[0].FrameSequence < observations[1].FrameSequence);
            Assert.True(observations[1].FrameSequence < observations[2].FrameSequence);
            Assert.Equal(new byte[] { 0x07 }, observations[0].RawFrame);
            Assert.Equal(new[] { "TestType", "TestType" },
                typed.Select(capture => capture.Code).ToArray());
            var session = await opened.Task.WaitAsync(Deadline);
            Assert.True(binder.TryGetCurrentTransportLink(session, out var proof));
            Assert.Equal(observations[0].Provenance, proof);

            // The malformed diagnostic frame must not have killed the pump.
            byte[] third = Framed(TypedBody(3));
            await client.GetStream().WriteAsync(third);
            var thirdForwarded = new byte[third.Length];
            await destination.GetStream().ReadExactlyAsync(thirdForwarded)
                .AsTask().WaitAsync(Deadline);
            Assert.Equal(third, thirdForwarded);
            await receivedFour.Task.WaitAsync(Deadline);
            Assert.Equal(proof, raw.ToArray()[3].Provenance);
            Assert.True(proxy.IsCurrentTransportLink(proof));

            client.Client.Shutdown(SocketShutdown.Send);
            destination.Client.Shutdown(SocketShutdown.Send);
            Assert.Equal(proof, await eof.Task.WaitAsync(Deadline));
            Assert.False(proxy.IsCurrentTransportLink(proof));
            Assert.Null(binder.Find(proof.ConnectionId));
        }
        finally
        {
            binder.Detach(proxy);
            proxy.Stop();
            upstream.Stop();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Service_stop_revokes_epoch_while_external_link_lookup_is_blocked(
        bool lookupFromSession)
    {
        var upstream = Upstream();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        int proxyPort = ProxyPort(upstreamPort);
        var permit = new MZP1OwnedClientPermit(int.MaxValue, 1, "3.7.10.10",
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".exe"),
            new string('A', 64));
        Assert.True(permit.IsWellFormed);
        var host = new BotHost(Path.Combine(Path.GetTempPath(),
            "v16-isolated-" + Guid.NewGuid().ToString("N")),
            proxyPort: proxyPort,
            hookPath: Path.Combine(Path.GetTempPath(), "missing-v16-hook.dll"),
            ownedClient: permit);
        var owner = new OwnerAuthority();
        host.Proxy.ConfigureOwnedLoopbackTest(
            new IPEndPoint(IPAddress.Loopback, upstreamPort), owner);
        var service = new BotHostService();
        var opened = new TaskCompletionSource<BotSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var captureFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Orchestrator.Binder.SessionOpened += session => opened.TrySetResult(session);
        // The fallback guarded host deliberately rejects gameplay ingress:
        // its Bus may not publish. Capture callback order still gives an exact
        // post-Binder observation without guessing authentication.
        host.Proxy.MessageCaptured += _ => captureFinished.TrySetResult(true);
        try
        {
            // The native Service.StartBound lacks an authorized-loopback
            // configuration port. This test binds only its actual read-only
            // ownership/liveness methods to a separately owned local host.
            typeof(BotHostService).GetField("_host", Hidden)!.SetValue(service, host);
            typeof(BotHostService).GetField("_hostEpoch", Hidden)!.SetValue(service, 16L);
            host.Start();
            Assert.True(host.Capture.IsRunning);
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var destination = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
            byte[] framed = Framed(TypedBody(5));
            await client.GetStream().WriteAsync(framed);
            var forwarded = new byte[framed.Length];
            await destination.GetStream().ReadExactlyAsync(forwarded)
                .AsTask().WaitAsync(Deadline);
            Assert.Equal(framed, forwarded);
            await captureFinished.Task.WaitAsync(Deadline);
            var session = await opened.Task.WaitAsync(Deadline);
            Assert.True(service.TryGetCurrentTransportLink(session, out var original));
            Assert.True(service.IsCurrentTransportLink(original));

            owner.BlockNextOwnerCheck();
            Task<bool> pending = Task.Run(() =>
                lookupFromSession
                    ? service.TryGetCurrentTransportLink(session, out _)
                    : service.IsCurrentTransportLink(original));
            await owner.OwnerCheckEntered.Task.WaitAsync(Deadline);

            // Stop must revoke the service epoch and close its own proxy even
            // though the external process owner has not released its lookup.
            await Task.Run(service.Stop).WaitAsync(Deadline);
            Assert.False(service.IsCurrentTransportLink(original));
            owner.ReleaseOwner();
            Assert.False(await pending.WaitAsync(Deadline));
            Assert.False(service.TryGetCurrentTransportLink(session, out _));
        }
        finally
        {
            owner.ReleaseOwner();
            service.Stop();
            host.Stop();
            host.Proxy.Dispose();
            upstream.Stop();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replay_sends_original_validated_snapshot_and_rechecks_permission(
        bool revokeDuringWriteGate)
    {
        var upstream = Upstream();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        int proxyPort = ProxyPort(upstreamPort);
        using var proxy = new TcpProxyServer(proxyPort);
        var owner = new OwnerAuthority();
        var frameObserved = new TaskCompletionSource<TransportLinkProvenance>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        proxy.TransportFrameObserved += frame => frameObserved.TrySetResult(frame.Provenance);
        try
        {
            proxy.ConfigureOwnedLoopbackTest(
                new IPEndPoint(IPAddress.Loopback, upstreamPort), owner);
            proxy.Start();
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var destination = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
            byte[] harmless = Framed(TypedBody(1));
            await client.GetStream().WriteAsync(harmless);
            var prior = new byte[harmless.Length];
            await destination.GetStream().ReadExactlyAsync(prior)
                .AsTask().WaitAsync(Deadline);
            Assert.Equal(harmless, prior);
            var identity = await frameObserved.Task.WaitAsync(Deadline);
            Assert.True(proxy.IsCurrentTransportLink(identity));

            // Test a real in-flight replay waiting behind the actual
            // per-direction TCP write semaphore, not a mocked sender.
            var links = typeof(TcpProxyServer).GetField("_links", Hidden)!.GetValue(proxy)!;
            var link = links.GetType().GetProperty("Item")!
                .GetValue(links, [identity.ConnectionId])!;
            var gate = Assert.IsType<SemaphoreSlim>(
                link.GetType().GetProperty("UpstreamWrite")!.GetValue(link));
            Assert.True(gate.Wait(0));
            int gateHeldByTest = 1;
            void ReleaseHeldGate()
            {
                if (Interlocked.Exchange(ref gateHeldByTest, 0) == 1)
                    gate.Release();
            }
            try
            {
                byte[] request = [0x08, 0x01]; // valid neutral protobuf, no game action
                owner.AfterFirstReplayDecisionLatched = () =>
                {
                    // This callback runs on the actual Replay path AFTER
                    // the first permission result is captured, but BEFORE
                    // acquisition of the held real TCP write gate.
                    request[1] = 0x7F;
                    if (revokeDuringWriteGate) owner.RevokeReplay();
                    ReleaseHeldGate();
                };
                Task<bool> replay = Task.Run(() =>
                    proxy.Replay(identity, Direction.ClientToServer, request));
                await owner.FirstReplayCheck.Task.WaitAsync(Deadline);
                bool wroteLocally = await replay.WaitAsync(Deadline);
                Assert.Equal(!revokeDuringWriteGate, wroteLocally);
                Assert.True(owner.ReplayChecks >= 2);
                if (!revokeDuringWriteGate)
                {
                    var bytes = new byte[3];
                    await destination.GetStream().ReadExactlyAsync(bytes)
                        .AsTask().WaitAsync(Deadline);
                    Assert.Equal(new byte[] { 0x02, 0x08, 0x01 }, bytes);
                }
                else
                {
                    Assert.False(destination.GetStream().DataAvailable);
                }
                Assert.True(proxy.IsCurrentTransportLink(identity));
            }
            finally
            {
                owner.AfterFirstReplayDecisionLatched = null;
                ReleaseHeldGate();
            }
        }
        finally
        {
            proxy.Stop();
            upstream.Stop();
        }
    }
}
