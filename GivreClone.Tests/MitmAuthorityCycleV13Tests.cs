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
/// Real, isolated loopback forwarding with source-issued transport proofs.
/// No game client, injection, foreign service, or server ACK is involved.
/// </summary>
public sealed class MitmAuthorityCycleV13Tests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const uint TestPid = 93217;

    private sealed class SwitchableAuthority : ITestProxyRouteAuthority
    {
        private int _owner = 1;
        private int _throw;

        public bool AdmitClient(TcpClient _, out uint independentlyVerifiedProcessId)
        {
            independentlyVerifiedProcessId = TestPid;
            return Volatile.Read(ref _owner) != 0;
        }

        public bool IsStillOwner(TcpClient _, uint processId)
        {
            if (Volatile.Read(ref _throw) != 0)
                throw new InvalidOperationException("Owner source unavailable.");
            return processId == TestPid && Volatile.Read(ref _owner) != 0;
        }

        public bool MayReplay(TcpClient _, uint __, Direction ___) => false;
        public void Revoke() => Volatile.Write(ref _owner, 0);
        public void Restore() => Volatile.Write(ref _owner, 1);
        public void Throw() => Volatile.Write(ref _throw, 1);
    }

    private sealed class DeniedSink : IFrameSink
    {
        public bool Send(Direction _, byte[] __) => false;
    }

    private static SessionBinder CreateBinder() => new(id => new BotSession(id,
        ProtocolCatalog.LoadFromStrings(
            "message GameMessage { int64 uid = 1; }",
            "{\"TestType\":\"MessageType.GameMessage\"}",
            null, "{\"GameMessage\":{\"uid\":1}}"),
        new DeniedSink(), new NoPacer()));

    private static int AvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private static int ProxyPort(int upstream)
    {
        int port;
        do { port = AvailablePort(); }
        while (port == upstream || port is 5657 or 5658 or 26666);
        return port;
    }

    private static byte[] Framed(byte[] body) =>
        RawProtobuf.EncodeVarint((ulong)body.Length).Concat(body).ToArray();

    private static byte[] Body(int uid = 42) => RawProtobuf.Encode(
    [
        new ProtoField { Field = 1, Wire = WireType.Len,
            String = "type.ankama.com/TestType" },
        new ProtoField { Field = 2, Wire = WireType.Len, Bytes = RawProtobuf.Encode(
        [
            new ProtoField { Field = 1, Wire = WireType.Varint, Varint = (ulong)uid },
            new ProtoField { Field = 2, Wire = WireType.Len, Bytes = [0xAA, 0xBB] }
        ]) }
    ]);

    [Fact]
    public async Task Owner_loss_is_terminal_for_original_link_even_when_pid_is_reaccepted()
    {
        var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        var upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        var authority = new SwitchableAuthority();
        using var proxy = new TcpProxyServer(ProxyPort(upstreamPort));
        var proxyPort = (int)typeof(TcpProxyServer).GetField("_localPort", Hidden)!
            .GetValue(proxy)!;
        var binder = CreateBinder();
        binder.Attach(proxy);
        var observed = new ConcurrentQueue<TransportFrameObservation>();
        var closed = new ConcurrentQueue<TransportLinkProvenance>();
        var two = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var three = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var eof = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = new TaskCompletionSource<BotSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        binder.SessionOpened += session => opened.TrySetResult(session);
        proxy.TransportFrameObserved += frame =>
        {
            observed.Enqueue(frame);
            if (observed.Count == 2) two.TrySetResult(true);
            if (observed.Count == 3) three.TrySetResult(true);
        };
        proxy.TransportLinkClosed += proof =>
        {
            closed.Enqueue(proof);
            eof.TrySetResult(true);
        };

        try
        {
            proxy.ConfigureOwnedLoopbackTest(
                new IPEndPoint(IPAddress.Loopback, upstreamPort), authority);
            proxy.Start();
            byte[] framed = Framed(Body());
            using (var first = new TcpClient(AddressFamily.InterNetwork))
            {
                await first.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
                using var destination = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
                await first.GetStream().WriteAsync(framed.Concat(framed).ToArray());
                var forwarded = new byte[framed.Length * 2];
                await destination.GetStream().ReadExactlyAsync(forwarded)
                    .AsTask().WaitAsync(Deadline);
                Assert.Equal(framed.Concat(framed).ToArray(), forwarded);
                await two.Task.WaitAsync(Deadline);
                var events = observed.ToArray();
                Assert.Equal(2, events.Length);
                Assert.True(events[0].FrameSequence < events[1].FrameSequence);
                var oldLink = events[0].Provenance;
                Assert.Equal(oldLink, events[1].Provenance);
                var oldSession = await opened.Task.WaitAsync(Deadline);
                Assert.Same(oldSession, binder.Find(oldLink.ConnectionId));
                Assert.True(binder.TryGetCurrentTransportLink(oldSession, out var proof));
                Assert.Equal(oldLink, proof);

                authority.Revoke();
                Assert.False(proxy.IsCurrentTransportLink(oldLink));
                // Revocation is permanent for the old socket, even if this
                // numeric PID becomes acceptable again for a fresh process.
                authority.Restore();
                Assert.False(proxy.IsCurrentTransportLink(oldLink));
                Assert.False(proxy.IsCurrentCapturedLink(new CapturedMessage(
                    1, Direction.ClientToServer, oldLink.ConnectionId, TestPid,
                    "TestType", [], Body()) { Provenance = oldLink }));
                Assert.False(binder.TryGetCurrentTransportLink(oldSession, out _));
                await eof.Task.WaitAsync(Deadline);
                Assert.Equal(oldLink, Assert.Single(closed));
            }

            using var next = new TcpClient(AddressFamily.InterNetwork);
            await next.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var nextDestination = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
            await next.GetStream().WriteAsync(framed);
            var nextForwarded = new byte[framed.Length];
            await nextDestination.GetStream().ReadExactlyAsync(nextForwarded)
                .AsTask().WaitAsync(Deadline);
            await three.Task.WaitAsync(Deadline);
            var currentLink = observed.ToArray()[2].Provenance;
            Assert.Equal(TestPid, currentLink.ProcessId);
            Assert.NotEqual(observed.ToArray()[0].Provenance, currentLink);
            Assert.True(currentLink.LinkToken > observed.ToArray()[0].Provenance.LinkToken);
            Assert.True(proxy.IsCurrentTransportLink(currentLink));
            Assert.False(binder.ForgetFrom(proxy, observed.ToArray()[0].Provenance));
            Assert.True(proxy.IsCurrentTransportLink(currentLink));
        }
        finally
        {
            binder.Detach(proxy);
            proxy.Stop();
            upstream.Stop();
        }
    }

    [Fact]
    public async Task Stop_start_retires_same_proxy_old_run_and_delayed_callback_before_new_link()
    {
        var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        var authority = new SwitchableAuthority();
        using var proxy = new TcpProxyServer(ProxyPort(upstreamPort));
        int proxyPort = (int)typeof(TcpProxyServer).GetField("_localPort", Hidden)!
            .GetValue(proxy)!;
        var binder = CreateBinder();
        binder.Attach(proxy);
        var observed = new ConcurrentQueue<TransportLinkProvenance>();
        var first = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        proxy.TransportFrameObserved += frame =>
        {
            observed.Enqueue(frame.Provenance);
            if (observed.Count == 1) first.TrySetResult(true);
            if (observed.Count == 2) second.TrySetResult(true);
        };
        var oldCaptured = new TaskCompletionSource<Action<CapturedMessage>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var eventField = typeof(TcpProxyServer).GetField("MessageCaptured", Hidden)!;

        try
        {
            proxy.ConfigureOwnedLoopbackTest(
                new IPEndPoint(IPAddress.Loopback, upstreamPort), authority);
            proxy.Start();
            byte[] bytes = Framed(Body());
            using (var oldClient = new TcpClient(AddressFamily.InterNetwork))
            {
                await oldClient.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
                using var oldDestination = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
                await oldClient.GetStream().WriteAsync(bytes);
                var forwarded = new byte[bytes.Length];
                await oldDestination.GetStream().ReadExactlyAsync(forwarded)
                    .AsTask().WaitAsync(Deadline);
                await first.Task.WaitAsync(Deadline);
                oldCaptured.SetResult(Assert.IsType<Action<CapturedMessage>>(
                    eventField.GetValue(proxy)));
                var oldProof = Assert.Single(observed);
                proxy.Stop();
                Assert.False(proxy.IsCurrentTransportLink(oldProof));
                Assert.Null(binder.FindByProcess(TestPid));
            }

            // Keep the same binder attached and resume the same proxy object.
            // The new run cannot accept old captured callbacks, even when the
            // independent owner returns the same numeric PID.
            proxy.Start();
            using var currentClient = new TcpClient(AddressFamily.InterNetwork);
            await currentClient.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var currentDestination = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
            await currentClient.GetStream().WriteAsync(bytes);
            var nextForwarded = new byte[bytes.Length];
            await currentDestination.GetStream().ReadExactlyAsync(nextForwarded)
                .AsTask().WaitAsync(Deadline);
            await second.Task.WaitAsync(Deadline);
            var snapshots = observed.ToArray();
            var old = snapshots[0];
            var current = snapshots[1];
            Assert.Equal(old.ProxyInstanceId, current.ProxyInstanceId);
            Assert.NotEqual(old.RunEpoch, current.RunEpoch);
            Assert.NotEqual(old.LinkToken, current.LinkToken);
            var delayed = new CapturedMessage(1, Direction.ClientToServer,
                old.ConnectionId, TestPid, "TestType", [], Body()) { Provenance = old };
            (await oldCaptured.Task.WaitAsync(Deadline))(delayed);
            Assert.False(proxy.IsCurrentCapturedLink(delayed));
            Assert.True(proxy.IsCurrentTransportLink(current));
            Assert.True(binder.Sessions.All(session => session.ConnectionId != old.ConnectionId));
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
    public async Task Owner_failure_during_first_raw_callback_suppresses_later_consumers(
        bool throwInsteadOfRevoke)
    {
        var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        var authority = new SwitchableAuthority();
        using var proxy = new TcpProxyServer(ProxyPort(upstreamPort));
        int proxyPort = (int)typeof(TcpProxyServer).GetField("_localPort", Hidden)!
            .GetValue(proxy)!;
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        TransportLinkProvenance observed = default;
        int laterRaw = 0, laterDecode = 0, closes = 0;

        proxy.TransportFrameObserved += frame =>
        {
            observed = frame.Provenance;
            if (throwInsteadOfRevoke) authority.Throw();
            else authority.Revoke();
            completed.TrySetResult(true);
        };
        proxy.TransportFrameObserved += _ => Interlocked.Increment(ref laterRaw);
        proxy.MessageCaptured += _ => Interlocked.Increment(ref laterDecode);
        proxy.TransportLinkClosed += _ => Interlocked.Increment(ref closes);
        try
        {
            proxy.ConfigureOwnedLoopbackTest(
                new IPEndPoint(IPAddress.Loopback, upstreamPort), authority);
            proxy.Start();
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var destination = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
            byte[] data = Framed(Body());
            await client.GetStream().WriteAsync(data);
            await completed.Task.WaitAsync(Deadline);
            Assert.False(proxy.IsCurrentTransportLink(observed));
            authority.Restore();
            Assert.False(proxy.IsCurrentTransportLink(observed));
            Assert.Equal(0, Volatile.Read(ref laterRaw));
            Assert.Equal(0, Volatile.Read(ref laterDecode));
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref closes) == 1,
                Deadline));
        }
        finally
        {
            proxy.Stop();
            upstream.Stop();
        }
    }

    [Fact]
    public async Task Mutable_observer_cannot_rewrite_framed_bytes_or_engine_payload_for_next_subscriber()
    {
        var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        using var proxy = new TcpProxyServer(ProxyPort(upstreamPort));
        int proxyPort = (int)typeof(TcpProxyServer).GetField("_localPort", Hidden)!
            .GetValue(proxy)!;
        var authority = new SwitchableAuthority();
        byte[] body = Body();
        var rawObserved = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var engineObserved = new TaskCompletionSource<BotMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        proxy.TransportFrameObserved += frame => frame.RawFrame[0] ^= 0xFF;
        proxy.TransportFrameObserved += frame => rawObserved.TrySetResult(frame.RawFrame);
        proxy.MessageCaptured += capture =>
        {
            capture.RawFrame[0] ^= 0xFF;
            var first = capture.Payload[0];
            first.Varint = 999;
            var nested = capture.Payload[1].Bytes;
            if (nested is not null) nested[0] = 0;
        };
        var binder = CreateBinder();
        binder.Attach(proxy); // intentionally after the mutating subscriber
        binder.SessionOpened += session =>
            session.Bus.Subscribe(message => engineObserved.TrySetResult(message));

        try
        {
            proxy.ConfigureOwnedLoopbackTest(
                new IPEndPoint(IPAddress.Loopback, upstreamPort), authority);
            proxy.Start();
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var destination = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
            await client.GetStream().WriteAsync(Framed(body));
            var forwarded = new byte[Framed(body).Length];
            await destination.GetStream().ReadExactlyAsync(forwarded)
                .AsTask().WaitAsync(Deadline);
            Assert.Equal(Framed(body), forwarded);
            Assert.Equal(body, await rawObserved.Task.WaitAsync(Deadline));
            var message = await engineObserved.Task.WaitAsync(Deadline);
            Assert.Equal(body, message.RawFrame);
            Assert.Equal(42UL, message.Payload[0].Varint);
            Assert.Equal(new byte[] { 0xAA, 0xBB }, message.Payload[1].Bytes);
            var snapshot = new TransportFrameObservation(1, Direction.ClientToServer,
                new TransportLinkProvenance(proxy.ProxyInstanceId, 1, 1, 1, TestPid), body);
            snapshot.RawFrame[0] = 0;
            Assert.Equal(body, snapshot.RawFrame);
            var changed = snapshot with { RawFrame = [0x08, 0x02] };
            Assert.Equal(body, snapshot.RawFrame);
            Assert.Equal(new byte[] { 0x08, 0x02 }, changed.RawFrame);
        }
        finally
        {
            binder.Detach(proxy);
            proxy.Stop();
            upstream.Stop();
        }
    }

    [Fact]
    public async Task Host_and_service_subscriber_mutations_do_not_forge_downstream_source_bytes()
    {
        var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        int proxyPort = ProxyPort(upstreamPort);
        var permit = new MZP1OwnedClientPermit(int.MaxValue, 1, "3.7.10.10",
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".exe"),
            new string('A', 64));
        Assert.True(permit.IsWellFormed);
        var host = new BotHost(Path.Combine(Path.GetTempPath(),
            "v13-host-" + Guid.NewGuid().ToString("N")), proxyPort: proxyPort,
            hookPath: Path.Combine(Path.GetTempPath(), "never-created-v13-hook.dll"),
            ownedClient: permit);
        var authority = new SwitchableAuthority();
        host.Proxy.ConfigureOwnedLoopbackTest(
            new IPEndPoint(IPAddress.Loopback, upstreamPort), authority);
        var service = new BotHostService();
        var observed = new ConcurrentQueue<(long Sequence, TransportLinkProvenance Link, byte[] Bytes)>();
        var two = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var eof = new TaskCompletionSource<TransportLinkProvenance>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.TransportFrameObserved += frame => frame.RawFrame[0] = 0;
        service.TransportFrameObserved += frame => frame.RawFrame[0] = 0;
        service.TransportFrameObserved += frame =>
        {
            observed.Enqueue((frame.FrameSequence, frame.Provenance, frame.RawFrame));
            if (observed.Count == 2) two.TrySetResult(true);
        };
        service.TransportLinkClosed += identity => eof.TrySetResult(identity);

        try
        {
            host.Start();
            Assert.True(host.Capture.IsRunning);
            // Real BotHostService callback attached to this exact source/epoch;
            // StartBound cannot configure a test upstream, so this offline
            // fixture mirrors its private ownership-specific subscription.
            var type = typeof(BotHostService);
            type.GetField("_host", Hidden)!.SetValue(service, host);
            type.GetField("_hostEpoch", Hidden)!.SetValue(service, 73L);
            var frameMethod = type.GetMethod("OnHostFrame", Hidden)!;
            var closeMethod = type.GetMethod("OnHostLinkClosed", Hidden)!;
            host.TransportFrameObserved += frame =>
                frameMethod.Invoke(service, [host, 73L, frame]);
            host.Orchestrator.TransportLinkClosed += identity =>
                closeMethod.Invoke(service, [host, 73L, identity]);

            byte[] body = Body();
            var framed = Framed(body);
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var destination = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
            await client.GetStream().WriteAsync(framed.Concat(framed).ToArray());
            var forwarded = new byte[2 * framed.Length];
            await destination.GetStream().ReadExactlyAsync(forwarded)
                .AsTask().WaitAsync(Deadline);
            Assert.Equal(framed.Concat(framed).ToArray(), forwarded);
            await two.Task.WaitAsync(Deadline);
            var actual = observed.ToArray();
            Assert.Equal(2, actual.Length);
            Assert.True(actual[0].Sequence < actual[1].Sequence);
            Assert.Equal(actual[0].Link, actual[1].Link);
            Assert.All(actual, observation => Assert.Equal(body, observation.Bytes));
            Assert.True(service.IsCurrentTransportLink(actual[0].Link));
            client.Client.Shutdown(SocketShutdown.Send);
            // V17 preserves the reverse TCP direction after client FIN.
            // End the independent upstream sending direction before EOF.
            destination.Client.Shutdown(SocketShutdown.Send);
            Assert.Equal(actual[0].Link, await eof.Task.WaitAsync(Deadline));
            Assert.False(service.IsCurrentTransportLink(actual[0].Link));
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
