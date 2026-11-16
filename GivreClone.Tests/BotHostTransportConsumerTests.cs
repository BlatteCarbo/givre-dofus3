extern alias F06App;

using System.Net;
using System.Net.Sockets;
using System.Reflection;
using BotHostService = F06App::GivreClone.App.Services.BotHostService;
using GivreClone.Engine;
using GivreCore.Mitm;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Offline consumer interleavings. Only an independently bound local listener
/// is started; no client, injection, game process, server ACK or remote upstream
/// is contacted. The private test link represents an admitted relay link.
/// </summary>
public sealed class BotHostTransportConsumerTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const uint TestPid = 917;

    private sealed class TestAuthority : ITestProxyRouteAuthority
    {
        public bool AdmitClient(TcpClient _, out uint processId)
        {
            processId = 0; // no actual incoming test client may be admitted
            return false;
        }
        public bool IsStillOwner(TcpClient _, uint processId) => processId == TestPid;
        public bool MayReplay(TcpClient _, uint __, Direction ___) => false;
    }

    private sealed class SilentSink : IFrameSink
    {
        public bool Send(Direction _, byte[] __) => false;
    }

    private static int LocalPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private static BotHost Host(int excludedPort = 0)
    {
        int port;
        do { port = LocalPort(); } while (port == excludedPort);
        int upstreamPort;
        do { upstreamPort = LocalPort(); }
        while (upstreamPort == port || upstreamPort is 5657 or 5658 or 26666);
        var fakePath = Path.Combine(Path.GetTempPath(),
            "givre-transport-consumer-" + Guid.NewGuid().ToString("N"));
        var owned = new MZP1OwnedClientPermit(
            1, 1, "3.7.10.10", fakePath + ".exe", new string('A', 64));
        Assert.True(owned.IsWellFormed);
        var host = new BotHost(fakePath, proxyPort: port,
            hookPath: fakePath + ".dll", ownedClient: owned);
        host.Proxy.ConfigureOwnedLoopbackTest(
            new IPEndPoint(IPAddress.Loopback, upstreamPort), new TestAuthority());
        return host;
    }

    private static TransportLinkProvenance Link(BotHost host, int id)
    {
        var proxy = host.Proxy;
        var listener = Assert.IsType<TcpListener>(typeof(TcpProxyServer)
            .GetField("_listener", Hidden)!.GetValue(proxy));
        var runEpoch = (long)typeof(TcpProxyServer).GetField("_runEpoch", Hidden)!
            .GetValue(proxy)!;
        var linkType = typeof(TcpProxyServer).GetNestedType("Link", BindingFlags.NonPublic)!;
        var ctor = Assert.Single(linkType.GetConstructors(BindingFlags.Public | Hidden));
        var link = ctor.Invoke([id, TestPid, new TcpClient(), new TcpClient(),
            new TestAuthority()]);
        linkType.GetProperty("RunEpoch")!.SetValue(link, runEpoch);
        Assert.True((bool)typeof(TcpProxyServer).GetMethod("TryRegisterLink", Hidden)!
            .Invoke(proxy, [link, listener, runEpoch, CancellationToken.None])!);
        var token = (long)linkType.GetProperty("LinkToken")!.GetValue(link)!;
        var identity = new TransportLinkProvenance(proxy.ProxyInstanceId,
            runEpoch, token, id, TestPid);
        Assert.True(proxy.IsCurrentTransportLink(identity));
        return identity;
    }

    private static TransportFrameObservation Frame(
        TransportLinkProvenance identity, long sequence) =>
        new(sequence, Direction.ServerToClient, identity, [0x08, (byte)sequence]);

    private static Action<TransportFrameObservation> RawCallback(TcpProxyServer proxy) =>
        Assert.IsType<Action<TransportFrameObservation>>(typeof(TcpProxyServer)
            .GetField("TransportFrameObserved", Hidden)!.GetValue(proxy));

    // BotHostService has no public fixture injection because production only
    // builds a guarded host with real owner authorization. This offline harness
    // installs its private event bridge around an already-started local host.
    private static Action<TransportFrameObservation> BindService(
        BotHostService service, BotHost host, long epoch)
    {
        var type = typeof(BotHostService);
        type.GetField("_host", Hidden)!.SetValue(service, host);
        type.GetField("_hostEpoch", Hidden)!.SetValue(service, epoch);
        var frameMethod = type.GetMethod("OnHostFrame", Hidden)!;
        var logMethod = type.GetMethod("OnHostLog", Hidden)!;
        var authMethod = type.GetMethod("OnHostAuthenticated", Hidden)!;
        Action<string> log = message => logMethod.Invoke(service, [host, epoch, message]);
        Action<BotSession> authenticated =
            session => authMethod.Invoke(service, [host, epoch, session]);
        Action<TransportFrameObservation> callback =
            frame => frameMethod.Invoke(service, [host, epoch, frame]);
        type.GetField("_hostLogHandler", Hidden)!.SetValue(service, log);
        type.GetField("_hostAuthenticatedHandler", Hidden)!.SetValue(service, authenticated);
        type.GetField("_hostFrameHandler", Hidden)!.SetValue(service, callback);
        host.Log += log;
        host.Orchestrator.Binder.SessionAuthenticated += authenticated;
        host.TransportFrameObserved += callback;
        return callback;
    }

    [Fact]
    public void Host_forwards_only_exact_source_link_and_generation_in_original_order()
    {
        var host = Host();
        try
        {
            host.Start();
            Assert.True(host.Capture.IsRunning);
            var identity = Link(host, 41);
            var observed = new List<TransportFrameObservation>();
            host.TransportFrameObserved += observed.Add;
            var raw = RawCallback(host.Proxy);
            raw(Frame(identity, 1));
            raw(Frame(identity with { RunEpoch = identity.RunEpoch - 1 }, 2));
            raw(Frame(identity with { LinkToken = identity.LinkToken + 1 }, 3));
            raw(Frame(identity with { ProxyInstanceId = Guid.NewGuid() }, 4));
            raw(Frame(identity, 5));

            Assert.Equal(new long[] { 1, 5 }, observed.Select(f => f.FrameSequence));
            Assert.All(observed, f => Assert.Equal(identity, f.Provenance));
            Assert.True(host.IsCurrentTransportLink(identity));
            Assert.False(host.IsCurrentTransportLink(identity with { LinkToken = 0 }));
        }
        finally { host.Stop(); host.Proxy.Dispose(); }
    }

    [Fact]
    public void Host_stop_during_start_log_callback_cannot_open_late_listener()
    {
        var host = Host();
        try
        {
            int calls = 0;
            host.Log += _ =>
            {
                Interlocked.Increment(ref calls);
                host.Stop(); // reentrant while Host.Start is before Capture.Start
            };
            host.Start();
            Assert.Equal(1, calls);
            Assert.False(host.Capture.IsRunning);
            Assert.False(host.Proxy.IsRunning);
        }
        finally { host.Stop(); host.Proxy.Dispose(); }
    }

    [Fact]
    public void Host_restart_revokes_old_snapshot_callback_even_with_reused_numeric_id()
    {
        var host = Host();
        try
        {
            host.Start();
            var old = Link(host, 51);
            var oldInvocation = RawCallback(host.Proxy);
            int received = 0;
            host.TransportFrameObserved += _ => Interlocked.Increment(ref received);
            oldInvocation(Frame(old, 1));
            Assert.Equal(1, received);

            host.Stop();
            oldInvocation(Frame(old, 2));
            Assert.Equal(1, received);
            host.Start();
            Assert.True(host.Capture.IsRunning);
            var next = Link(host, 51);
            Assert.NotEqual(old.RunEpoch, next.RunEpoch);
            Assert.NotEqual(old.LinkToken, next.LinkToken);
            oldInvocation(Frame(next, 3)); // old host epoch callback remains stale
            Assert.Equal(1, received);
            RawCallback(host.Proxy)(Frame(old, 4));
            Assert.Equal(1, received);
            RawCallback(host.Proxy)(Frame(next, 5));
            Assert.Equal(2, received);
        }
        finally { host.Stop(); host.Proxy.Dispose(); }
    }

    [Fact]
    public void Service_consumer_stop_revokes_later_subscribers_and_retained_callback()
    {
        var host = Host();
        try
        {
            host.Start();
            var identity = Link(host, 61);
            var service = new BotHostService();
            var oldServiceCallback = BindService(service, host, 70);
            int first = 0, later = 0;
            service.TransportFrameObserved += _ =>
            {
                Interlocked.Increment(ref first);
                service.Stop();
            };
            service.TransportFrameObserved += _ => Interlocked.Increment(ref later);
            Assert.True(service.IsCurrentTransportLink(identity));

            RawCallback(host.Proxy)(Frame(identity, 1));
            Assert.Equal(1, first);
            Assert.Equal(0, later);
            Assert.False(service.IsRunning);
            Assert.False(service.IsCurrentTransportLink(identity));
            oldServiceCallback(Frame(identity, 2));
            Assert.Equal(1, first);
            Assert.Equal(0, later);
            Assert.False(host.Capture.IsRunning);
        }
        finally { host.Stop(); host.Proxy.Dispose(); }
    }

    [Fact]
    public void Service_refuses_foreign_source_and_stale_host_frame_after_host_replacement()
    {
        var firstHost = Host();
        var firstPort = (int)typeof(TcpProxyServer).GetField("_localPort", Hidden)!
            .GetValue(firstHost.Proxy)!;
        var replacementHost = Host(excludedPort: firstPort);
        try
        {
            firstHost.Start();
            replacementHost.Start();
            var old = Link(firstHost, 71);
            var current = Link(replacementHost, 71);
            var service = new BotHostService();
            var oldDelegate = BindService(service, firstHost, 80);
            int received = 0;
            service.TransportFrameObserved += _ => Interlocked.Increment(ref received);
            Assert.True(service.IsCurrentTransportLink(old));
            Assert.False(service.IsCurrentTransportLink(current));

            // Simulate stop/replacement after revoking the first service epoch.
            service.Stop();
            BindService(service, replacementHost, 82);
            Assert.True(service.IsCurrentTransportLink(current));
            Assert.False(service.IsCurrentTransportLink(old));
            oldDelegate(Frame(old, 1));
            Assert.Equal(0, received);
            RawCallback(replacementHost.Proxy)(Frame(old, 2));
            Assert.Equal(0, received);
            RawCallback(replacementHost.Proxy)(Frame(current, 3));
            Assert.Equal(1, received);
            service.Stop();
        }
        finally
        {
            firstHost.Stop();
            replacementHost.Stop();
            firstHost.Proxy.Dispose();
            replacementHost.Proxy.Dispose();
        }
    }

    [Fact]
    public void Service_stop_unsubscribes_old_log_and_authentication_callbacks()
    {
        var host = Host();
        try
        {
            host.Start();
            var service = new BotHostService();
            BindService(service, host, 90);
            int logs = 0, sessionChanges = 0;
            service.Log += _ => Interlocked.Increment(ref logs);
            service.SessionsChanged += _ => Interlocked.Increment(ref sessionChanges);

            var oldLog = Assert.IsType<Action<string>>(typeof(BotHost)
                .GetField("Log", Hidden)!.GetValue(host));
            var oldAuthentication = Assert.IsType<Action<BotSession>>(
                typeof(SessionBinder).GetField("SessionAuthenticated", Hidden)!
                    .GetValue(host.Orchestrator.Binder));
            oldLog("current");
            Assert.Equal(1, logs);

            service.Stop();
            int countAtStop = sessionChanges; // zero-session notice is expected
            oldLog("old host after stop");
            oldAuthentication(new BotSession(91, host.Catalog,
                new SilentSink(), new NoPacer()));
            Assert.Equal(1, logs);
            Assert.Equal(countAtStop, sessionChanges);
            Assert.False(service.IsRunning);
        }
        finally { host.Stop(); host.Proxy.Dispose(); }
    }
}
