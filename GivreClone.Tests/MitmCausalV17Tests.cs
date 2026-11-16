extern alias F06App;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using BotHostService = F06App::GivreClone.App.Services.BotHostService;
using GivreClone.Engine;
using GivreCore.Mitm;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Actual bounded loopback TCP lifecycle, including half-close, exact source
/// token, local FIFO and host callbacks. No DOFUS/game/foreign process or ACK.
/// </summary>
public sealed class MitmCausalV17Tests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(7);
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const uint TestPid = 41717;

    private sealed class TestAuthority : ITestProxyRouteAuthority
    {
        private int _replayChecks;
        public bool AdmitClient(TcpClient _, out uint pid)
        {
            pid = TestPid;
            return true;
        }

        public bool IsStillOwner(TcpClient _, uint pid) => pid == TestPid;
        public bool MayReplay(TcpClient _, uint pid, Direction __)
        {
            Interlocked.Increment(ref _replayChecks);
            return pid == TestPid;
        }

        public int ReplayChecks => Volatile.Read(ref _replayChecks);
    }

    private static TcpListener Upstream()
    {
        while (true)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            if (port is >= 1024 and not (5657 or 5658 or 26666))
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

    private static byte[] Body(byte value) => RawProtobuf.Encode(
    [
        new ProtoField
        {
            Field = 1, Wire = WireType.Len,
            String = "type.ankama.com/TestType"
        },
        new ProtoField { Field = 2, Wire = WireType.Varint, Varint = value }
    ]);

    private static byte[] Frame(byte[] body) =>
        RawProtobuf.EncodeVarint((ulong)body.Length).Concat(body).ToArray();

    [Fact]
    public async Task Client_half_close_allows_remaining_server_response_then_exact_EOF_and_reconnect()
    {
        var upstream = Upstream();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        int proxyPort = ProxyPort(upstreamPort);
        var owner = new TestAuthority();
        using var proxy = new TcpProxyServer(proxyPort);
        var frames = new ConcurrentQueue<TransportFrameObservation>();
        var closed = new ConcurrentQueue<TransportLinkProvenance>();
        var sawClient = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sawServer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var eof = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        proxy.TransportFrameObserved += observation =>
        {
            frames.Enqueue(observation);
            if (observation.Direction == Direction.ClientToServer)
                sawClient.TrySetResult(true);
            else sawServer.TrySetResult(true);
        };
        proxy.TransportLinkClosed += link =>
        {
            closed.Enqueue(link);
            eof.TrySetResult(true);
        };
        try
        {
            proxy.ConfigureOwnedLoopbackTest(
                new IPEndPoint(IPAddress.Loopback, upstreamPort), owner);
            proxy.Start();
            using (var client = new TcpClient(AddressFamily.InterNetwork))
            {
                await client.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
                using var server = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
                // .NET TcpClient.GetStream() rejects a socket whose Connected
                // flag was cleared by a receive-side FIN, even though TCP
                // permits the reverse direction to remain operational. Keep
                // the real streams acquired before either directional FIN.
                var clientStream = client.GetStream();
                var serverStream = server.GetStream();
                byte[] request = Frame(Body(1));
                await clientStream.WriteAsync(request);
                var forwarded = new byte[request.Length];
                await serverStream.ReadExactlyAsync(forwarded)
                    .AsTask().WaitAsync(Deadline);
                Assert.Equal(request, forwarded);
                await sawClient.Task.WaitAsync(Deadline);
                var original = Assert.Single(frames).Provenance;
                Assert.Equal(TestPid, original.ProcessId);
                Assert.True(proxy.IsCurrentTransportLink(original));

                client.Client.Shutdown(SocketShutdown.Send);
                var consumedFin = new byte[1];
                Assert.Equal(0, await serverStream.ReadAsync(consumedFin)
                    .AsTask().WaitAsync(Deadline));
                // FIN is directional: the link must remain current for the
                // outstanding server response but cannot replay client writes.
                Assert.True(proxy.IsCurrentTransportLink(original));
                Assert.False(proxy.Replay(original, Direction.ClientToServer, [0x08, 0x01]));
                Assert.Empty(closed);

                // The reverse direction is still independently authorized:
                // this is a neutral local replay, NOT an upstream response or
                // gameplay ACK, and is never routed back into capture evidence.
                Assert.True(proxy.Replay(original, Direction.ServerToClient, [0x08, 0x01]));
                var replayed = new byte[3];
                await clientStream.ReadExactlyAsync(replayed)
                    .AsTask().WaitAsync(Deadline);
                Assert.Equal(new byte[] { 0x02, 0x08, 0x01 }, replayed);
                Assert.Single(frames);

                byte[] response = Frame(Body(2));
                await serverStream.WriteAsync(response);
                var received = new byte[response.Length];
                await clientStream.ReadExactlyAsync(received)
                    .AsTask().WaitAsync(Deadline);
                Assert.Equal(response, received);
                await sawServer.Task.WaitAsync(Deadline);
                var observed = frames.ToArray();
                Assert.Equal(2, observed.Length);
                Assert.Equal(Direction.ServerToClient, observed[1].Direction);
                Assert.Equal(original, observed[1].Provenance);
                Assert.Equal(Body(2), observed[1].RawFrame);

                server.Client.Shutdown(SocketShutdown.Send);
                Assert.Equal(0, await clientStream.ReadAsync(new byte[1])
                    .AsTask().WaitAsync(Deadline));
                await eof.Task.WaitAsync(Deadline);
                Assert.Equal(original, Assert.Single(closed));
                Assert.False(proxy.IsCurrentTransportLink(original));
            }

            // A fresh independently accepted socket may have the same PID,
            // but it must have a distinct source-issued link token.
            using var nextClient = new TcpClient(AddressFamily.InterNetwork);
            await nextClient.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var nextServer = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
            byte[] next = Frame(Body(3));
            await nextClient.GetStream().WriteAsync(next);
            var nextForwarded = new byte[next.Length];
            await nextServer.GetStream().ReadExactlyAsync(nextForwarded)
                .AsTask().WaitAsync(Deadline);
            Assert.Equal(next, nextForwarded);
            Assert.True(SpinWait.SpinUntil(() => frames.Count == 3, Deadline));
            var old = frames.ToArray()[0].Provenance;
            var successor = frames.ToArray()[2].Provenance;
            Assert.Equal(old.ProcessId, successor.ProcessId);
            Assert.NotEqual(old, successor);
            Assert.True(successor.LinkToken > old.LinkToken);
            Assert.False(proxy.IsCurrentTransportLink(old));
            Assert.True(proxy.IsCurrentTransportLink(successor));
        }
        finally
        {
            proxy.Stop();
            upstream.Stop();
        }
    }

    [Fact]
    public async Task Server_half_close_allows_remaining_client_frames_then_symmetric_EOF()
    {
        var upstream = Upstream();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        int proxyPort = ProxyPort(upstreamPort);
        using var proxy = new TcpProxyServer(proxyPort);
        var owner = new TestAuthority();
        var seen = new TaskCompletionSource<TransportLinkProvenance>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource<TransportLinkProvenance>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        proxy.TransportFrameObserved += frame => seen.TrySetResult(frame.Provenance);
        proxy.TransportLinkClosed += link => closed.TrySetResult(link);
        try
        {
            proxy.ConfigureOwnedLoopbackTest(
                new IPEndPoint(IPAddress.Loopback, upstreamPort), owner);
            proxy.Start();
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var server = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
            var clientStream = client.GetStream();
            var serverStream = server.GetStream();
            byte[] greeting = Frame(Body(4));
            await serverStream.WriteAsync(greeting);
            var received = new byte[greeting.Length];
            await clientStream.ReadExactlyAsync(received)
                .AsTask().WaitAsync(Deadline);
            Assert.Equal(greeting, received);
            var link = await seen.Task.WaitAsync(Deadline);
            server.Client.Shutdown(SocketShutdown.Send);
            Assert.Equal(0, await clientStream.ReadAsync(new byte[1])
                .AsTask().WaitAsync(Deadline));
            Assert.True(proxy.IsCurrentTransportLink(link));
            Assert.False(proxy.Replay(link, Direction.ServerToClient, [0x08, 0x01]));

            // The opposite sending direction remains legal after server FIN.
            byte[] request = Frame(Body(5));
            await clientStream.WriteAsync(request);
            var forwarded = new byte[request.Length];
            await serverStream.ReadExactlyAsync(forwarded)
                .AsTask().WaitAsync(Deadline);
            Assert.Equal(request, forwarded);
            client.Client.Shutdown(SocketShutdown.Send);
            Assert.Equal(0, await serverStream.ReadAsync(new byte[1])
                .AsTask().WaitAsync(Deadline));
            Assert.Equal(link, await closed.Task.WaitAsync(Deadline));
            Assert.False(proxy.IsCurrentTransportLink(link));
        }
        finally
        {
            proxy.Stop();
            upstream.Stop();
        }
    }

    [Fact]
    public async Task Stop_during_half_close_revokes_old_link_before_new_Start_and_late_callback()
    {
        var upstream = Upstream();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        int proxyPort = ProxyPort(upstreamPort);
        using var proxy = new TcpProxyServer(proxyPort);
        var owner = new TestAuthority();
        var frames = new ConcurrentQueue<TransportFrameObservation>();
        var closed = new ConcurrentQueue<TransportLinkProvenance>();
        proxy.TransportFrameObserved += frames.Enqueue;
        proxy.TransportLinkClosed += closed.Enqueue;
        try
        {
            proxy.ConfigureOwnedLoopbackTest(
                new IPEndPoint(IPAddress.Loopback, upstreamPort), owner);
            proxy.Start();
            using (var client = new TcpClient(AddressFamily.InterNetwork))
            {
                await client.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
                using var server = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
                byte[] request = Frame(Body(6));
                await client.GetStream().WriteAsync(request);
                var forwarded = new byte[request.Length];
                await server.GetStream().ReadExactlyAsync(forwarded)
                    .AsTask().WaitAsync(Deadline);
                Assert.Equal(request, forwarded);
                Assert.True(SpinWait.SpinUntil(() => frames.Count == 1, Deadline));
                var old = frames.ToArray()[0].Provenance;
                client.Client.Shutdown(SocketShutdown.Send);
                Assert.Equal(0, await server.GetStream().ReadAsync(new byte[1])
                    .AsTask().WaitAsync(Deadline));
                Assert.True(proxy.IsCurrentTransportLink(old));
                long oldRun = old.RunEpoch;
                proxy.Stop();
                Assert.False(proxy.IsCurrentTransportLink(old));
                Assert.Equal(old, Assert.Single(closed));
                Assert.False(proxy.Replay(old, Direction.ServerToClient, [0x08, 0x01]));
                proxy.Start();
                Assert.True(proxy.CurrentRunEpoch > oldRun);
            }

            using var successor = new TcpClient(AddressFamily.InterNetwork);
            await successor.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var nextUpstream = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
            byte[] next = Frame(Body(7));
            await successor.GetStream().WriteAsync(next);
            var received = new byte[next.Length];
            await nextUpstream.GetStream().ReadExactlyAsync(received)
                .AsTask().WaitAsync(Deadline);
            Assert.Equal(next, received);
            Assert.True(SpinWait.SpinUntil(() => frames.Count == 2, Deadline));
            var oldProof = frames.ToArray()[0].Provenance;
            var newProof = frames.ToArray()[1].Provenance;
            Assert.Equal(oldProof.ProcessId, newProof.ProcessId);
            Assert.NotEqual(oldProof.RunEpoch, newProof.RunEpoch);
            Assert.NotEqual(oldProof.LinkToken, newProof.LinkToken);
            Assert.False(proxy.IsCurrentTransportLink(oldProof));
            Assert.True(proxy.IsCurrentTransportLink(newProof));
        }
        finally
        {
            proxy.Stop();
            upstream.Stop();
        }
    }

    [Fact]
    public async Task Truncated_frame_at_FIN_fails_closed_without_publishing_type_evidence()
    {
        var upstream = Upstream();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        int proxyPort = ProxyPort(upstreamPort);
        var owner = new TestAuthority();
        using var proxy = new TcpProxyServer(proxyPort);
        int typed = 0;
        var eof = new TaskCompletionSource<TransportLinkProvenance>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        proxy.MessageCaptured += _ => Interlocked.Increment(ref typed);
        proxy.TransportLinkClosed += proof => eof.TrySetResult(proof);
        try
        {
            proxy.ConfigureOwnedLoopbackTest(
                new IPEndPoint(IPAddress.Loopback, upstreamPort), owner);
            proxy.Start();
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var server = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
            // Frame declares length=5, but only two body bytes arrive before
            // FIN; no complete frame, no captured type or source ACK.
            await client.GetStream().WriteAsync(new byte[] { 0x05, 0x08, 0x01 });
            client.Client.Shutdown(SocketShutdown.Send);
            var proof = await eof.Task.WaitAsync(Deadline);
            Assert.False(proxy.IsCurrentTransportLink(proof));
            Assert.Equal(0, Volatile.Read(ref typed));
            int read = 0;
            try
            {
                read = await server.GetStream().ReadAsync(new byte[1])
                    .AsTask().WaitAsync(Deadline);
            }
            catch (IOException) { /* Exact link terminated after truncated frame. */ }
            Assert.Equal(0, read);
        }
        finally
        {
            proxy.Stop();
            upstream.Stop();
        }
    }

    [Fact]
    public async Task Replay_and_pump_share_one_direction_gate_without_interleaved_frame_bodies()
    {
        var upstream = Upstream();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        int proxyPort = ProxyPort(upstreamPort);
        using var proxy = new TcpProxyServer(proxyPort);
        var authority = new TestAuthority();
        var firstFrame = new TaskCompletionSource<TransportLinkProvenance>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        proxy.TransportFrameObserved += frame => firstFrame.TrySetResult(frame.Provenance);
        try
        {
            proxy.ConfigureOwnedLoopbackTest(
                new IPEndPoint(IPAddress.Loopback, upstreamPort), authority);
            proxy.Start();
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var server = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
            byte[] initial = Frame(Body(8));
            await client.GetStream().WriteAsync(initial);
            var first = new byte[initial.Length];
            await server.GetStream().ReadExactlyAsync(first)
                .AsTask().WaitAsync(Deadline);
            Assert.Equal(initial, first);
            var proof = await firstFrame.Task.WaitAsync(Deadline);

            // This is the ACTUAL per-link semaphore shared by both the TCP
            // pump and the provenance-bound replay path.
            var links = typeof(TcpProxyServer).GetField("_links", Hidden)!.GetValue(proxy)!;
            var link = links.GetType().GetProperty("Item")!
                .GetValue(links, [proof.ConnectionId])!;
            var gate = Assert.IsType<SemaphoreSlim>(
                link.GetType().GetProperty("UpstreamWrite")!.GetValue(link));
            Assert.True(gate.Wait(0));
            bool heldByTest = true;
            try
            {
                byte[] pumpBody = Body(9);
                byte[] pumpFrame = Frame(pumpBody);
                byte[] replayBody = [0x08, 0x01]; // neutral protobuf, NOT gameplay
                await client.GetStream().WriteAsync(pumpFrame);
                Task<bool> replay = Task.Run(() =>
                    proxy.Replay(proof, Direction.ClientToServer, replayBody));
                Assert.True(SpinWait.SpinUntil(() => authority.ReplayChecks == 1, Deadline));
                // Source-owned private replay snapshot must not change while
                // the call awaits the same gate as the forward pump.
                replayBody[1] = 0x7F;
                gate.Release();
                heldByTest = false;
                Assert.True(await replay.WaitAsync(Deadline));
                int total = pumpFrame.Length + Frame([0x08, 0x01]).Length;
                var bytes = new byte[total];
                await server.GetStream().ReadExactlyAsync(bytes)
                    .AsTask().WaitAsync(Deadline);
                var decoded = RawProtobuf.ConsumeFrames(bytes, out var remainder);
                Assert.Empty(remainder);
                Assert.Equal(2, decoded.Count);
                Assert.Contains(decoded, body => body.SequenceEqual(pumpBody));
                Assert.Contains(decoded, body => body.SequenceEqual(new byte[] { 0x08, 0x01 }));
                Assert.True(proxy.IsCurrentTransportLink(proof));
            }
            finally
            {
                if (heldByTest) gate.Release();
            }
        }
        finally
        {
            proxy.Stop();
            upstream.Stop();
        }
    }

    [Fact]
    public async Task Host_service_reentrant_Stop_suppresses_later_observers_and_stale_link_identity()
    {
        var upstream = Upstream();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        int proxyPort = ProxyPort(upstreamPort);
        var permit = new MZP1OwnedClientPermit(int.MaxValue, 1, "3.7.10.10",
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".exe"),
            new string('A', 64));
        Assert.True(permit.IsWellFormed);
        var host = new BotHost(Path.Combine(Path.GetTempPath(),
            "v17-host-" + Guid.NewGuid().ToString("N")),
            proxyPort: proxyPort,
            hookPath: Path.Combine(Path.GetTempPath(), "never-created-v17-hook.dll"),
            ownedClient: permit);
        host.Proxy.ConfigureOwnedLoopbackTest(
            new IPEndPoint(IPAddress.Loopback, upstreamPort), new TestAuthority());
        var service = new BotHostService();
        var stopped = new TaskCompletionSource<TransportLinkProvenance>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int late = 0;
        var type = typeof(BotHostService);
        const long serviceEpoch = 517;
        try
        {
            // The public StartBound path intentionally has no authority/test
            // upstream injection port. Mirror its exact private callback
            // wiring to THIS real host, not to a mocked source or game client.
            type.GetField("_host", Hidden)!.SetValue(service, host);
            type.GetField("_hostEpoch", Hidden)!.SetValue(service, serviceEpoch);
            var onFrame = type.GetMethod("OnHostFrame", Hidden)!;
            Action<TransportFrameObservation> relay = frame =>
                onFrame.Invoke(service, [host, serviceEpoch, frame]);
            type.GetField("_hostFrameHandler", Hidden)!.SetValue(service, relay);
            host.TransportFrameObserved += relay;
            service.TransportFrameObserved += frame =>
            {
                // Stop synchronously from the actual TCP pump's observer,
                // invalidating service epoch while a copied invocation list
                // still contains the later subscriber.
                service.Stop();
                stopped.TrySetResult(frame.Provenance);
            };
            service.TransportFrameObserved += _ => Interlocked.Increment(ref late);

            host.Start();
            Assert.True(host.Capture.IsRunning);
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var server = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
            await client.GetStream().WriteAsync(Frame(Body(10)));
            var proof = await stopped.Task.WaitAsync(Deadline);
            Assert.Equal(TestPid, proof.ProcessId);
            Assert.False(service.IsRunning);
            Assert.False(service.IsCurrentTransportLink(proof));
            Assert.False(host.Proxy.IsCurrentTransportLink(proof));
            Assert.Equal(0, Volatile.Read(ref late));

            // Deliver a retained callback directly to the same real service
            // entry point. Previous host/run must remain inert after Stop.
            onFrame.Invoke(service, [host, serviceEpoch,
                new TransportFrameObservation(999, Direction.ClientToServer,
                    proof, Body(10))]);
            Assert.Equal(0, Volatile.Read(ref late));
        }
        finally
        {
            service.Stop();
            host.Stop();
            host.Proxy.Dispose();
            upstream.Stop();
        }
    }
}
