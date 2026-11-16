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
/// Real, isolated local TCP admissions, FIFO and source-issued link lifecycles.
/// No game, injection, native process, network service or gameplay ACK.
/// </summary>
public sealed class MitmLifecycleV15Tests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(7);
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    private const uint TestPid = 31817;

    private sealed class Owner : ITestProxyRouteAuthority
    {
        private readonly ManualResetEventSlim _release = new(true);
        private int _stallNext;
        private int _revoke;
        public TaskCompletionSource<bool> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool AdmitClient(TcpClient _, out uint pid)
        {
            pid = TestPid;
            return Volatile.Read(ref _revoke) == 0;
        }

        public bool IsStillOwner(TcpClient _, uint pid)
        {
            if (Interlocked.Exchange(ref _stallNext, 0) != 0)
            {
                Entered.TrySetResult(true);
                if (!_release.Wait(TimeSpan.FromSeconds(15))) return false;
            }
            return pid == TestPid && Volatile.Read(ref _revoke) == 0;
        }

        public bool MayReplay(TcpClient _, uint __, Direction ___) => false;
        public void StallOnce() { _release.Reset(); Volatile.Write(ref _stallNext, 1); }
        public void Release() => _release.Set();
        public void Revoke() => Volatile.Write(ref _revoke, 1);
    }

    private sealed class DeniedSink : IFrameSink
    {
        public bool Send(Direction _, byte[] __) => false;
    }

    private static ProtocolCatalog Catalog() => ProtocolCatalog.LoadFromStrings(
        "message GameMessage { int64 uid = 1; }",
        "{\"TestType\":\"MessageType.GameMessage\"}",
        null, "{\"GameMessage\":{\"uid\":1}}");

    private static SessionBinder Binder() => new((CapturedMessage capture) =>
        new BotSession(capture.ConnectionId, Catalog(),
            new DeniedSink(), new NoPacer()));

    private static int Port()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        try { return ((IPEndPoint)l.LocalEndpoint).Port; }
        finally { l.Stop(); }
    }

    private static int ProxyPort(int upstream)
    {
        int port;
        do { port = Port(); }
        while (port == upstream || port is 5657 or 5658 or 26666);
        return port;
    }

    private static byte[] Frame(byte id)
    {
        byte[] body = RawProtobuf.Encode([new ProtoField
        {
            Field = 1, Wire = WireType.Len, String = "type.ankama.com/TestType"
        }, new ProtoField { Field = 2, Wire = WireType.Varint, Varint = id }]);
        return RawProtobuf.EncodeVarint((ulong)body.Length).Concat(body).ToArray();
    }

    [Fact]
    public async Task Active_link_capacity_is_independent_of_pending_admission_capacity()
    {
        var upstreamListener = new TcpListener(IPAddress.Loopback, 0);
        upstreamListener.Start();
        int upstreamPort = ((IPEndPoint)upstreamListener.LocalEndpoint).Port;
        int proxyPort = ProxyPort(upstreamPort);
        var owner = new Owner();
        using var proxy = new TcpProxyServer(proxyPort);
        var clients = new List<TcpClient>();
        var upstreams = new List<TcpClient>();
        var frames = new ConcurrentQueue<TransportFrameObservation>();
        var closed = new TaskCompletionSource<TransportLinkProvenance>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        proxy.TransportFrameObserved += frames.Enqueue;
        TransportLinkProvenance first = default;
        proxy.TransportLinkClosed += link =>
        {
            if (link == first) closed.TrySetResult(link);
        };
        try
        {
            proxy.ConfigureOwnedLoopbackTest(
                new IPEndPoint(IPAddress.Loopback, upstreamPort), owner);
            proxy.Start();
            // All pending-admission permits are released after registration.
            // V14 used to admit unlimited long-lived links after this loop.
            for (int i = 0; i < 16; i++)
            {
                var client = new TcpClient(AddressFamily.InterNetwork);
                clients.Add(client);
                await client.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
                var target = await upstreamListener.AcceptTcpClientAsync().WaitAsync(Deadline);
                upstreams.Add(target);
                byte[] frame = Frame((byte)(i + 1));
                await client.GetStream().WriteAsync(frame);
                var delivered = new byte[frame.Length];
                await target.GetStream().ReadExactlyAsync(delivered)
                    .AsTask().WaitAsync(Deadline);
                Assert.Equal(frame, delivered);
                Assert.True(SpinWait.SpinUntil(() => frames.Count == i + 1, Deadline));
                var observation = frames.ToArray()[i];
                Assert.Equal(TestPid, observation.Provenance.ProcessId);
                Assert.True(proxy.IsCurrentTransportLink(observation.Provenance));
                if (i == 0) first = observation.Provenance;
            }

            using var excess = new TcpClient(AddressFamily.InterNetwork);
            await excess.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var excessTarget = await upstreamListener.AcceptTcpClientAsync().WaitAsync(Deadline);
            // Upstream is connected before TryRegisterLink; the 17th link must
            // be closed rather than retained with live pumps or provenance.
            int read = 0;
            try
            {
                read = await excessTarget.GetStream().ReadAsync(new byte[1])
                    .AsTask().WaitAsync(Deadline);
            }
            catch (IOException) { /* Closed by source on refused registration. */ }
            catch (SocketException) { /* TCP reset is also a refused link. */ }
            Assert.Equal(0, read);
            Assert.Equal(16, frames.Count);

            clients[0].Close();
            // Releasing an active-link slot requires both directional FINs;
            // the still-open upstream half may otherwise answer legitimately.
            upstreams[0].Client.Shutdown(SocketShutdown.Send);
            Assert.Equal(first, await closed.Task.WaitAsync(Deadline));
            using var replacement = new TcpClient(AddressFamily.InterNetwork);
            await replacement.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var replacementTarget = await upstreamListener.AcceptTcpClientAsync().WaitAsync(Deadline);
            byte[] next = Frame(99);
            await replacement.GetStream().WriteAsync(next);
            var nextReceived = new byte[next.Length];
            await replacementTarget.GetStream().ReadExactlyAsync(nextReceived)
                .AsTask().WaitAsync(Deadline);
            Assert.Equal(next, nextReceived);
            Assert.True(SpinWait.SpinUntil(() => frames.Count == 17, Deadline));
            var successor = frames.ToArray()[16].Provenance;
            Assert.NotEqual(first, successor);
            Assert.True(successor.LinkToken > first.LinkToken);
            Assert.False(proxy.IsCurrentTransportLink(first));
            Assert.True(proxy.IsCurrentTransportLink(successor));
        }
        finally
        {
            proxy.Stop();
            foreach (var client in clients) client.Dispose();
            foreach (var target in upstreams) target.Dispose();
            upstreamListener.Stop();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Blocking_external_owner_lookup_does_not_hold_binder_global_lock(int lookup)
    {
        var upstreamListener = new TcpListener(IPAddress.Loopback, 0);
        upstreamListener.Start();
        int upstreamPort = ((IPEndPoint)upstreamListener.LocalEndpoint).Port;
        int proxyPort = ProxyPort(upstreamPort);
        var authority = new Owner();
        using var proxy = new TcpProxyServer(proxyPort);
        var binder = Binder();
        var observed = new TaskCompletionSource<BotSession>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        binder.SessionOpened += session => session.Bus.Subscribe(_ => observed.TrySetResult(session));
        binder.Attach(proxy);
        try
        {
            proxy.ConfigureOwnedLoopbackTest(
                new IPEndPoint(IPAddress.Loopback, upstreamPort), authority);
            proxy.Start();
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var target = await upstreamListener.AcceptTcpClientAsync().WaitAsync(Deadline);
            byte[] first = Frame(5);
            await client.GetStream().WriteAsync(first);
            var forwarded = new byte[first.Length];
            await target.GetStream().ReadExactlyAsync(forwarded)
                .AsTask().WaitAsync(Deadline);
            var session = await observed.Task.WaitAsync(Deadline);
            Assert.Same(session, binder.Find(session.ConnectionId));

            authority.StallOnce();
            Task lookupTask = Task.Run(() =>
            {
                if (lookup == 0) Assert.Null(binder.Find(session.ConnectionId));
                else if (lookup == 1) Assert.Null(binder.FindByProcess(TestPid));
                else Assert.False(binder.TryGetCurrentTransportLink(session, out _));
            });
            await authority.Entered.Task.WaitAsync(Deadline);

            // Stop publishes exact-link closure into the SAME binder while
            // its ownership authority is still stalled in the lookup. This
            // deadlocked V14 because the external call retained binder._gate.
            await Task.Run(proxy.Stop).WaitAsync(Deadline);
            Assert.Null(binder.Find(session.ConnectionId));
            authority.Release();
            await lookupTask.WaitAsync(Deadline);
            Assert.Empty(binder.Sessions);
        }
        finally
        {
            authority.Release();
            binder.Detach(proxy);
            proxy.Stop();
            upstreamListener.Stop();
        }
    }

    [Fact]
    public async Task Detached_blocked_factory_cannot_hold_successor_proxys_same_connection_number()
    {
        var oldUpstream = new TcpListener(IPAddress.Loopback, 0);
        var newUpstream = new TcpListener(IPAddress.Loopback, 0);
        oldUpstream.Start();
        newUpstream.Start();
        int oldUpstreamPort = ((IPEndPoint)oldUpstream.LocalEndpoint).Port;
        int newUpstreamPort = ((IPEndPoint)newUpstream.LocalEndpoint).Port;
        int oldProxyPort = ProxyPort(oldUpstreamPort);
        int newProxyPort;
        do { newProxyPort = ProxyPort(newUpstreamPort); }
        while (newProxyPort == oldProxyPort || newProxyPort == oldUpstreamPort);
        using var oldProxy = new TcpProxyServer(oldProxyPort);
        using var newProxy = new TcpProxyServer(newProxyPort);
        var authority = new Owner();
        var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstReleased = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newOpened = new TaskCompletionSource<BotSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new ManualResetEventSlim();
        int factories = 0;
        var binder = new SessionBinder((CapturedMessage capture) =>
        {
            if (Interlocked.Increment(ref factories) == 1)
            {
                firstEntered.TrySetResult(true);
                if (!gate.Wait(TimeSpan.FromSeconds(15)))
                    throw new TimeoutException("Old test factory not released.");
                firstReleased.TrySetResult(true);
            }
            return new BotSession(capture.ConnectionId, Catalog(),
                new DeniedSink(), new NoPacer());
        });
        binder.SessionOpened += session => newOpened.TrySetResult(session);
        var oldFrame = new TaskCompletionSource<TransportLinkProvenance>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        oldProxy.TransportFrameObserved += frame => oldFrame.TrySetResult(frame.Provenance);

        try
        {
            oldProxy.ConfigureOwnedLoopbackTest(
                new IPEndPoint(IPAddress.Loopback, oldUpstreamPort), authority);
            newProxy.ConfigureOwnedLoopbackTest(
                new IPEndPoint(IPAddress.Loopback, newUpstreamPort), authority);
            binder.Attach(oldProxy);
            oldProxy.Start();
            using var oldClient = new TcpClient(AddressFamily.InterNetwork);
            await oldClient.ConnectAsync(IPAddress.Loopback, oldProxyPort).WaitAsync(Deadline);
            using var oldTarget = await oldUpstream.AcceptTcpClientAsync().WaitAsync(Deadline);
            byte[] oldBytes = Frame(11);
            await oldClient.GetStream().WriteAsync(oldBytes);
            var delivered = new byte[oldBytes.Length];
            await oldTarget.GetStream().ReadExactlyAsync(delivered)
                .AsTask().WaitAsync(Deadline);
            await firstEntered.Task.WaitAsync(Deadline);
            var oldProof = await oldFrame.Task.WaitAsync(Deadline);
            var staleCapture = Assert.IsType<Action<CapturedMessage>>(
                typeof(TcpProxyServer).GetField("MessageCaptured", Hidden)!
                    .GetValue(oldProxy));

            // Neither Detach nor the old exact EOF may wait for the blocked
            // factory. The new source legitimately begins at ConnectionId=1.
            binder.Detach(oldProxy);
            oldProxy.Stop();
            binder.Attach(newProxy);
            newProxy.Start();
            using var newClient = new TcpClient(AddressFamily.InterNetwork);
            await newClient.ConnectAsync(IPAddress.Loopback, newProxyPort).WaitAsync(Deadline);
            using var newTarget = await newUpstream.AcceptTcpClientAsync().WaitAsync(Deadline);
            byte[] newBytes = Frame(12);
            await newClient.GetStream().WriteAsync(newBytes);
            var secondDelivered = new byte[newBytes.Length];
            await newTarget.GetStream().ReadExactlyAsync(secondDelivered)
                .AsTask().WaitAsync(Deadline);
            Assert.Equal(newBytes, secondDelivered);
            var session = await newOpened.Task.WaitAsync(Deadline);
            Assert.Equal(oldProof.ConnectionId, session.ConnectionId);
            Assert.True(binder.TryGetCurrentTransportLink(session, out var newProof));
            Assert.NotEqual(oldProof.ProxyInstanceId, newProof.ProxyInstanceId);
            Assert.True(newProxy.IsCurrentTransportLink(newProof));
            Assert.False(oldProxy.IsCurrentTransportLink(oldProof));

            gate.Set();
            await firstReleased.Task.WaitAsync(Deadline);
            staleCapture(new CapturedMessage(1, Direction.ClientToServer,
                oldProof.ConnectionId, TestPid, "TestType", [], Frame(11))
                { Provenance = oldProof });
            Assert.Same(session, binder.FindByProcess(TestPid));
            Assert.Equal(2, Volatile.Read(ref factories));
        }
        finally
        {
            gate.Set();
            binder.Detach(newProxy);
            binder.Detach(oldProxy);
            oldProxy.Stop();
            newProxy.Stop();
            oldUpstream.Stop();
            newUpstream.Stop();
        }
    }

    [Fact]
    public async Task Host_service_preserves_exact_eof_reconnect_fifo_and_immutable_frame_bytes()
    {
        var upstreamListener = new TcpListener(IPAddress.Loopback, 0);
        upstreamListener.Start();
        int upstreamPort = ((IPEndPoint)upstreamListener.LocalEndpoint).Port;
        int proxyPort = ProxyPort(upstreamPort);
        var permit = new MZP1OwnedClientPermit(int.MaxValue, 1, "3.7.10.10",
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".exe"),
            new string('A', 64));
        Assert.True(permit.IsWellFormed);
        var host = new BotHost(Path.Combine(Path.GetTempPath(),
            "v15-isolated-host-" + Guid.NewGuid().ToString("N")),
            proxyPort: proxyPort,
            hookPath: Path.Combine(Path.GetTempPath(), "nonexistent-v15-hook.dll"),
            ownedClient: permit);
        var authority = new Owner();
        host.Proxy.ConfigureOwnedLoopbackTest(
            new IPEndPoint(IPAddress.Loopback, upstreamPort), authority);
        var service = new BotHostService();
        var observed = new ConcurrentQueue<(long Sequence, TransportLinkProvenance Link, byte[] Raw)>();
        var two = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var three = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var eof = new TaskCompletionSource<TransportLinkProvenance>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // First consumers may intentionally mutate the byte[] returned from
        // RawFrame. The next consumer must still observe original wire bytes.
        host.TransportFrameObserved += frame => frame.RawFrame[0] ^= 0xFF;
        service.TransportFrameObserved += frame => frame.RawFrame[0] ^= 0xFF;
        service.TransportFrameObserved += frame =>
        {
            observed.Enqueue((frame.FrameSequence, frame.Provenance, frame.RawFrame));
            if (observed.Count == 2) two.TrySetResult(true);
            if (observed.Count == 3) three.TrySetResult(true);
        };
        service.TransportLinkClosed += link => eof.TrySetResult(link);

        try
        {
            // Service.StartBound cannot configure an independently authorized
            // upstream; this isolated fixture attaches its ACTUAL callback
            // ports, preserving source+host epoch and normal Stop cleanup.
            var type = typeof(BotHostService);
            type.GetField("_host", Hidden)!.SetValue(service, host);
            type.GetField("_hostEpoch", Hidden)!.SetValue(service, 615L);
            var onFrame = type.GetMethod("OnHostFrame", Hidden)!;
            var onClosed = type.GetMethod("OnHostLinkClosed", Hidden)!;
            Action<TransportFrameObservation> bridge = frame =>
                onFrame.Invoke(service, [host, 615L, frame]);
            Action<TransportLinkProvenance> closeBridge = link =>
                onClosed.Invoke(service, [host, 615L, link]);
            type.GetField("_hostFrameHandler", Hidden)!.SetValue(service, bridge);
            type.GetField("_hostLinkClosedHandler", Hidden)!.SetValue(service, closeBridge);
            host.TransportFrameObserved += bridge;
            host.Orchestrator.TransportLinkClosed += closeBridge;
            host.Start();
            Assert.True(host.Capture.IsRunning);

            byte[] first = Frame(1);
            byte[] second = Frame(2);
            using (var firstClient = new TcpClient(AddressFamily.InterNetwork))
            {
                await firstClient.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
                using var firstTarget = await upstreamListener.AcceptTcpClientAsync().WaitAsync(Deadline);
                await firstClient.GetStream().WriteAsync(first.Concat(second).ToArray());
                var forwarded = new byte[first.Length + second.Length];
                await firstTarget.GetStream().ReadExactlyAsync(forwarded)
                    .AsTask().WaitAsync(Deadline);
                Assert.Equal(first.Concat(second).ToArray(), forwarded);
                await two.Task.WaitAsync(Deadline);
                var snapshots = observed.ToArray();
                Assert.True(snapshots[0].Sequence < snapshots[1].Sequence);
                Assert.Equal(snapshots[0].Link, snapshots[1].Link);
                Assert.Equal(RawProtobuf.ConsumeFrames(first, out _).Single(), snapshots[0].Raw);
                Assert.True(host.Proxy.IsCurrentTransportLink(snapshots[0].Link));
                firstClient.Client.Shutdown(SocketShutdown.Send);
                firstTarget.Client.Shutdown(SocketShutdown.Send);
                Assert.Equal(snapshots[0].Link, await eof.Task.WaitAsync(Deadline));
                Assert.False(service.IsCurrentTransportLink(snapshots[0].Link));
                // TcpProxyServer.CloseLink publishes TransportLinkClosed BEFORE
                // ConnectionClosed. The observed EOF is not a completion fence
                // for SessionBinder's separate close callback; under full-suite
                // load the binder can still own the retired connection here.
                // Wait for the REAL binder eviction, without synthesizing an
                // ACK, changing the deadline or weakening the final assertion.
                Assert.True(SpinWait.SpinUntil(
                    () => host.Orchestrator.Binder.Find(snapshots[0].Link.ConnectionId)
                        is null, Deadline),
                    "Exact EOF link did not retire from the real SessionBinder");
                Assert.False(host.Orchestrator.NotifyConnectionClosed(snapshots[0].Link));
            }

            using var reconnected = new TcpClient(AddressFamily.InterNetwork);
            await reconnected.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var reconnectedTarget = await upstreamListener.AcceptTcpClientAsync().WaitAsync(Deadline);
            byte[] next = Frame(3);
            await reconnected.GetStream().WriteAsync(next);
            var delivered = new byte[next.Length];
            await reconnectedTarget.GetStream().ReadExactlyAsync(delivered)
                .AsTask().WaitAsync(Deadline);
            Assert.Equal(next, delivered);
            await three.Task.WaitAsync(Deadline);
            var all = observed.ToArray();
            var old = all[0].Link;
            var current = all[2].Link;
            Assert.Equal(old.ProcessId, current.ProcessId); // PID reuse, NOT identity reuse
            Assert.NotEqual(old, current);
            Assert.True(current.LinkToken > old.LinkToken);
            Assert.True(service.IsCurrentTransportLink(current));
            Assert.False(service.IsCurrentTransportLink(old));
            // The proxy intentionally emits raw TransportFrameObserved BEFORE
            // decoding type URLs and publishing MessageCaptured to SessionBinder.
            // three.Task proves wire capture, not that Binder's later callback
            // has committed its new source-bound BotSession already.
            Assert.True(SpinWait.SpinUntil(
                () => host.Orchestrator.Binder.Find(current.ConnectionId) is not null,
                Deadline),
                "Real MessageCaptured callback did not admit the new exact-link session");
            var newSession = Assert.IsType<BotSession>(host.Orchestrator.Binder.Find(current.ConnectionId));
            Assert.True(service.TryGetCurrentTransportLink(newSession, out var engineProof));
            Assert.Equal(current, engineProof);

            service.Stop();
            Assert.False(service.IsCurrentTransportLink(current));
        }
        finally
        {
            service.Stop();
            host.Stop();
            host.Proxy.Dispose();
            upstreamListener.Stop();
        }
    }
}
