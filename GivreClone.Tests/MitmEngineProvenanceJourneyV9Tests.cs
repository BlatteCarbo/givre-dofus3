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
/// Offline-only source-to-consumer tests. TCP traffic goes exclusively between
/// test-owned loopback listeners; no DOFUS process, hook, remote server or ACK.
/// </summary>
public sealed class MitmEngineProvenanceJourneyV9Tests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    private const uint TestPid = 9917;

    private sealed class RevocableAuthority : ITestProxyRouteAuthority
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
                throw new InvalidOperationException("Independent identity provider unavailable.");
            return processId == TestPid && Volatile.Read(ref _owner) != 0;
        }

        public bool MayReplay(TcpClient _, uint __, Direction ___) => false;

        public void Revoke() => Volatile.Write(ref _owner, 0);
        public void Restore() => Volatile.Write(ref _owner, 1);
        public void ThrowOnCheck() => Volatile.Write(ref _throw, 1);
    }

    private sealed class DeniedSink : IFrameSink
    {
        public bool Send(Direction _, byte[] __) => false;
    }

    private static int AvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private static BotHost Host(int localPort, int upstreamPort,
        RevocableAuthority authority)
    {
        var folder = Path.Combine(Path.GetTempPath(),
            "givre-v9-offline-" + Guid.NewGuid().ToString("N"));
        // A deliberately nonexistent PID keeps the capture-host timer inert.
        var owned = new MZP1OwnedClientPermit(int.MaxValue, 1, "3.7.10.10",
            Path.Combine(folder, "never-started.exe"), new string('A', 64));
        Assert.True(owned.IsWellFormed);
        var host = new BotHost(folder, proxyPort: localPort,
            hookPath: Path.Combine(folder, "missing-hook.dll"), ownedClient: owned);
        host.Proxy.ConfigureOwnedLoopbackTest(
            new IPEndPoint(IPAddress.Loopback, upstreamPort), authority);
        return host;
    }

    private static void BindService(BotHostService service, BotHost host, long epoch)
    {
        // Production service only constructs an approved host through StartBound.
        // The offline test attaches the real service callbacks to a separately
        // configured owned-loopback host; no production authorization is bypassed.
        var type = typeof(BotHostService);
        type.GetField("_host", Hidden)!.SetValue(service, host);
        type.GetField("_hostEpoch", Hidden)!.SetValue(service, epoch);
        var frameMethod = type.GetMethod("OnHostFrame", Hidden)!;
        var closeMethod = type.GetMethod("OnHostLinkClosed", Hidden)!;
        Action<TransportFrameObservation> frames = frame =>
            frameMethod.Invoke(service, [host, epoch, frame]);
        Action<TransportLinkProvenance> closes = proof =>
            closeMethod.Invoke(service, [host, epoch, proof]);
        type.GetField("_hostFrameHandler", Hidden)!.SetValue(service, frames);
        type.GetField("_hostLinkClosedHandler", Hidden)!.SetValue(service, closes);
        host.TransportFrameObserved += frames;
        host.Orchestrator.TransportLinkClosed += closes;
    }

    private static byte[] NeutralFrame()
    {
        var body = RawProtobuf.Encode([new ProtoField
        {
            Field = 1, Wire = WireType.Len, String = "type.ankama.com/TestType"
        }]);
        return RawProtobuf.EncodeVarint((ulong)body.Length).Concat(body).ToArray();
    }

    [Fact]
    public async Task Actual_loopback_pump_propagates_fifo_proof_to_service_and_exact_eof()
    {
        var upstreamListener = new TcpListener(IPAddress.Loopback, 0);
        upstreamListener.Start();
        int upstreamPort = ((IPEndPoint)upstreamListener.LocalEndpoint).Port;
        int proxyPort;
        do { proxyPort = AvailablePort(); }
        while (proxyPort == upstreamPort || proxyPort is 5657 or 5658 or 26666);
        var authority = new RevocableAuthority();
        var host = Host(proxyPort, upstreamPort, authority);
        var service = new BotHostService();
        var observed = new ConcurrentQueue<TransportFrameObservation>();
        var closed = new ConcurrentQueue<TransportLinkProvenance>();
        var twoFrames = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var threeFrames = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstClose = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = new TaskCompletionSource<BotSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Orchestrator.Binder.SessionOpened += session => opened.TrySetResult(session);
        service.TransportFrameObserved += frame =>
        {
            observed.Enqueue(frame);
            if (observed.Count == 2) twoFrames.TrySetResult(true);
            if (observed.Count == 3) threeFrames.TrySetResult(true);
        };
        service.TransportLinkClosed += identity =>
        {
            closed.Enqueue(identity);
            firstClose.TrySetResult(true);
        };

        try
        {
            host.Start();
            Assert.True(host.Capture.IsRunning);
            BindService(service, host, 37);
            byte[] frame = NeutralFrame();
            using (var firstClient = new TcpClient(AddressFamily.InterNetwork))
            {
                await firstClient.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
                using var firstUpstream = await upstreamListener.AcceptTcpClientAsync().WaitAsync(Deadline);
                await firstClient.GetStream().WriteAsync(frame.Concat(frame).ToArray());
                var forwarded = new byte[frame.Length * 2];
                await firstUpstream.GetStream().ReadExactlyAsync(forwarded)
                    .AsTask().WaitAsync(Deadline);
                Assert.Equal(frame.Concat(frame).ToArray(), forwarded);
                await twoFrames.Task.WaitAsync(Deadline);
                var snapshots = observed.ToArray();
                Assert.Equal(2, snapshots.Length);
                Assert.Equal(snapshots[0].Provenance, snapshots[1].Provenance);
                Assert.True(snapshots[0].FrameSequence < snapshots[1].FrameSequence);
                Assert.All(snapshots, f =>
                {
                    Assert.Equal(TestPid, f.Provenance.ProcessId);
                    Assert.True(f.Provenance.RunEpoch > 0 && f.Provenance.LinkToken > 0);
                    Assert.Equal(Direction.ClientToServer, f.Direction);
                });
                var old = snapshots[0].Provenance;
                var admitted = await opened.Task.WaitAsync(Deadline);
                Assert.Equal(old.ConnectionId, admitted.ConnectionId);
                Assert.Equal<uint?>(TestPid, admitted.ProcessId);
                Assert.True(host.Orchestrator.TryGetCurrentTransportLink(admitted,
                    out var admittedLink));
                Assert.Equal(old, admittedLink);
                Assert.True(service.IsCurrentTransportLink(old));
                Assert.False(service.IsCurrentTransportLink(old with { LinkToken = old.LinkToken + 1 }));

                // Ownership revocation is observable immediately even before
                // either socket reaches EOF or a close callback is delivered.
                authority.Revoke();
                Assert.False(host.Proxy.IsCurrentTransportLink(old));
                Assert.False(service.IsCurrentTransportLink(old));
                Assert.False(host.Orchestrator.TryGetCurrentTransportLink(admitted, out _));
                Assert.Null(host.Orchestrator.Binder.FindByProcess(TestPid));
                Assert.Null(host.Orchestrator.Binder.Find(old.ConnectionId));
                firstClient.Client.Shutdown(SocketShutdown.Send);
                await firstClose.Task.WaitAsync(Deadline);
                Assert.Equal(old, Assert.Single(closed));
                Assert.False(host.Proxy.IsCurrentTransportLink(old));
            }

            // A PID may be reused for a fresh independently owned link. Its
            // new source token must never revive observations from the old one.
            authority.Restore();
            using var successor = new TcpClient(AddressFamily.InterNetwork);
            await successor.ConnectAsync(IPAddress.Loopback, proxyPort).WaitAsync(Deadline);
            using var secondUpstream = await upstreamListener.AcceptTcpClientAsync().WaitAsync(Deadline);
            await successor.GetStream().WriteAsync(frame);
            var successorForwarded = new byte[frame.Length];
            await secondUpstream.GetStream().ReadExactlyAsync(successorForwarded)
                .AsTask().WaitAsync(Deadline);
            Assert.Equal(frame, successorForwarded);
            await threeFrames.Task.WaitAsync(Deadline);
            var next = observed.ToArray()[2].Provenance;
            var previous = observed.ToArray()[0].Provenance;
            Assert.Equal(previous.ProcessId, next.ProcessId);
            Assert.NotEqual(previous, next);
            Assert.True(next.LinkToken > previous.LinkToken);
            Assert.False(service.IsCurrentTransportLink(previous));
            Assert.True(service.IsCurrentTransportLink(next));
            Assert.False(host.Orchestrator.NotifyConnectionClosed(previous));
            Assert.True(service.IsCurrentTransportLink(next));
        }
        finally
        {
            service.Stop();
            host.Stop();
            host.Proxy.Dispose();
            upstreamListener.Stop();
        }
    }

    [Fact]
    public async Task Factory_paused_during_stop_cannot_publish_retired_session()
    {
        using var proxy = new TcpProxyServer(16571);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        var stop = new CancellationTokenSource();
        typeof(TcpProxyServer).GetField("_listener", Hidden)!.SetValue(proxy, listener);
        typeof(TcpProxyServer).GetField("_stop", Hidden)!.SetValue(proxy, stop);
        typeof(TcpProxyServer).GetField("_runEpoch", Hidden)!.SetValue(proxy, 8L);
        typeof(TcpProxyServer).GetField("_hasEverStarted", Hidden)!.SetValue(proxy, true);

        var authority = new RevocableAuthority();
        var linkType = typeof(TcpProxyServer).GetNestedType("Link", BindingFlags.NonPublic)!;
        object link = Assert.Single(linkType.GetConstructors(Hidden | BindingFlags.Public))
            .Invoke([7, TestPid, new TcpClient(), new TcpClient(), authority]);
        linkType.GetProperty("RunEpoch")!.SetValue(link, 8L);
        Assert.True((bool)typeof(TcpProxyServer).GetMethod("TryRegisterLink", Hidden)!
            .Invoke(proxy, [link, listener, 8L, CancellationToken.None])!);
        long token = (long)linkType.GetProperty("LinkToken")!.GetValue(link)!;
        var identity = new TransportLinkProvenance(proxy.ProxyInstanceId, 8, token, 7, TestPid);
        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var catalog = ProtocolCatalog.LoadFromStrings(
            "message GameMessage { int64 uid = 1; }",
            "{\"TestType\":\"MessageType.GameMessage\"}",
            null, "{\"GameMessage\":{\"uid\":1}}");
        var binder = new SessionBinder(capture =>
        {
            entered.Set();
            if (!release.Wait(Deadline))
                throw new TimeoutException("Test factory was not released.");
            return new BotSession(capture.ConnectionId, catalog, new DeniedSink(), new NoPacer());
        });
        binder.Attach(proxy);
        int opened = 0;
        binder.SessionOpened += _ => Interlocked.Increment(ref opened);
        var captured = new CapturedMessage(1, Direction.ServerToClient, 7,
            TestPid, "TestType", [], [0x08, 0x01]) { Provenance = identity };
        var callback = Assert.IsType<Action<CapturedMessage>>(typeof(TcpProxyServer)
            .GetField("MessageCaptured", Hidden)!.GetValue(proxy));

        try
        {
            var captureTask = Task.Run(() => callback(captured));
            Assert.True(entered.Wait(Deadline));
            var stopTask = Task.Run(proxy.Stop);
            Assert.True(SpinWait.SpinUntil(() => proxy.CurrentRunEpoch != 8, Deadline));
            release.Set();
            await Task.WhenAll(captureTask, stopTask).WaitAsync(Deadline);
            Assert.Equal(0, Volatile.Read(ref opened));
            Assert.Null(binder.Find(7));
            Assert.Empty(binder.Sessions);
        }
        finally
        {
            release.Set();
            proxy.Stop();
            binder.Detach(proxy);
        }
    }

    [Fact]
    public void Owner_authority_failure_invalidates_both_typed_lookup_paths()
    {
        using var proxy = new TcpProxyServer(16572);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        typeof(TcpProxyServer).GetField("_listener", Hidden)!.SetValue(proxy, listener);
        typeof(TcpProxyServer).GetField("_stop", Hidden)!
            .SetValue(proxy, new CancellationTokenSource());
        typeof(TcpProxyServer).GetField("_runEpoch", Hidden)!.SetValue(proxy, 9L);
        typeof(TcpProxyServer).GetField("_hasEverStarted", Hidden)!.SetValue(proxy, true);
        var authority = new RevocableAuthority();
        var linkType = typeof(TcpProxyServer).GetNestedType("Link", BindingFlags.NonPublic)!;
        object link = Assert.Single(linkType.GetConstructors(Hidden | BindingFlags.Public))
            .Invoke([8, TestPid, new TcpClient(), new TcpClient(), authority]);
        linkType.GetProperty("RunEpoch")!.SetValue(link, 9L);
        Assert.True((bool)typeof(TcpProxyServer).GetMethod("TryRegisterLink", Hidden)!
            .Invoke(proxy, [link, listener, 9L, CancellationToken.None])!);
        long token = (long)linkType.GetProperty("LinkToken")!.GetValue(link)!;
        var identity = new TransportLinkProvenance(proxy.ProxyInstanceId, 9, token, 8, TestPid);
        var captured = new CapturedMessage(1, Direction.ClientToServer, 8,
            TestPid, "TestType", [], [0x08, 0x01]) { Provenance = identity };
        Assert.True(proxy.IsCurrentTransportLink(identity));
        Assert.True(proxy.IsCurrentCapturedLink(captured));
        authority.ThrowOnCheck();
        Assert.False(proxy.IsCurrentTransportLink(identity));
        Assert.False(proxy.IsCurrentCapturedLink(captured));
    }
}
