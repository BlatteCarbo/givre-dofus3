using System.Net;
using System.Net.Sockets;
using System.Reflection;
using GivreClone.Engine;
using GivreCore.Mitm;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Isolated loopback test relay only: no upstream connection, no game process,
/// no injection, no game packets. TcpProxyServer.Start can synchronously invoke
/// Log AFTER its listener opens, which is the critical lifecycle boundary.
/// </summary>
public sealed class DofusCaptureHostStartStopRaceTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    private static TaskCompletionSource<bool> Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class RefusingAuthority : ITestProxyRouteAuthority
    {
        public bool AdmitClient(TcpClient client, out uint independentlyVerifiedProcessId)
        {
            independentlyVerifiedProcessId = 0;
            return false;
        }
        public bool IsStillOwner(TcpClient client, uint processId) => false;
        public bool MayReplay(TcpClient client, uint processId, Direction direction) => false;
    }

    private static int LoopbackPort()
    {
        // TcpProxyServer forbids 0 and requires >=1024. Reserve an ephemeral
        // loopback port to minimize collisions with any unrelated application.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally { listener.Stop(); }
    }

    private static (DofusCaptureHost Host, TcpProxyServer Proxy) Fixture()
    {
        var port = LoopbackPort();
        var proxy = new TcpProxyServer(port);
        var upstreamPort = port == 15151 ? 15152 : 15151;
        proxy.ConfigureOwnedLoopbackTest(
            new IPEndPoint(IPAddress.Loopback, upstreamPort), new RefusingAuthority());
        // IsWellFormed is needed to enter the start path. Poll never sees a
        // hook, so it never enumerates or opens a process.
        var permit = new MZP1OwnedClientPermit(
            1, 1, "3.7.10.10", Path.Combine(Path.GetTempPath(), "never-dofus.exe"),
            new string('A', 64));
        Assert.True(permit.IsWellFormed);
        var host = new DofusCaptureHost(proxy,
            Path.Combine(Path.GetTempPath(), "not-a-hook-" + Guid.NewGuid().ToString("N") + ".dll"),
            permit);
        return (host, proxy);
    }

    [Fact]
    public void Proxy_log_throw_after_listener_open_always_rolls_back()
    {
        var (host, proxy) = Fixture();
        using (proxy)
        {
            var entered = 0;
            proxy.Log += _ =>
            {
                Assert.True(proxy.IsRunning); // listener is already open
                Interlocked.Increment(ref entered);
                throw new InvalidOperationException("synthetic observer failure");
            };
            host.Start();
            Assert.Equal(1, entered);
            Assert.False(host.IsRunning);
            Assert.False(proxy.IsRunning);
            host.Stop(); // no orphan listener when the start never committed
            Assert.False(proxy.IsRunning);
        }
    }

    [Fact]
    public async Task Stop_during_proxy_start_observer_waits_for_rollback_without_deadlock()
    {
        var (host, proxy) = Fixture();
        using (proxy)
        {
            var entered = Signal();
            var release = Signal();
            proxy.Log += _ =>
            {
                entered.TrySetResult(true);
                release.Task.GetAwaiter().GetResult(); // deterministic starting barrier
            };

            var starting = Task.Run(host.Start);
            Task? stopping = null;
            try
            {
                await entered.Task.WaitAsync(Deadline);
                Assert.True(proxy.IsRunning);
                stopping = Task.Run(host.Stop);

                // Observe Stop's committed cancellation request, not just its
                // scheduling; Start must see this when the callback resumes.
                var field = typeof(DofusCaptureHost).GetField(
                    "_stopRequested", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var lifecycleGate = typeof(DofusCaptureHost).GetField(
                    "_lifecycleGate", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(host)!;
                Assert.True(SpinWait.SpinUntil(
                    () => { lock (lifecycleGate) return (bool)field.GetValue(host)!; }, Deadline));
                Assert.False(stopping.IsCompleted);
            }
            finally { release.TrySetResult(true); }

            await starting.WaitAsync(Deadline);
            await stopping!.WaitAsync(Deadline);
            Assert.False(host.IsRunning);
            Assert.False(proxy.IsRunning);
        }
    }

    [Fact]
    public void Reentrant_stop_from_proxy_start_callback_does_not_deadlock()
    {
        var (host, proxy) = Fixture();
        using (proxy)
        {
            var callbacks = 0;
            proxy.Log += _ =>
            {
                callbacks++;
                Assert.True(proxy.IsRunning);
                host.Stop(); // synchronous callback on the Start thread
            };
            host.Start();
            Assert.Equal(1, callbacks);
            Assert.False(host.IsRunning);
            Assert.False(proxy.IsRunning);
        }
    }

    [Fact]
    public void Throwing_host_diagnostic_subscriber_cannot_prevent_stop()
    {
        var (host, proxy) = Fixture();
        using (proxy)
        {
            host.Log += _ => throw new InvalidOperationException("host log failed");
            host.Start();
            Assert.True(host.IsRunning);
            Assert.True(proxy.IsRunning);
            host.Stop();
            Assert.False(host.IsRunning);
            Assert.False(proxy.IsRunning);
        }
    }
}
