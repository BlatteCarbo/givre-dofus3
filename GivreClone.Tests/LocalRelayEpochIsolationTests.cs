using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Xunit.Abstractions;
using GivreClone.App.Services.LocalLaunch;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Real loopback TCP, fake upstream only. No Dofus/client/game process.
/// These cases exercise the real relay admission and byte-observer boundary.
/// </summary>
public sealed class LocalRelayEpochIsolationTests
{
    private readonly ITestOutputHelper _output;
    public LocalRelayEpochIsolationTests(ITestOutputHelper output) => _output = output;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(8);

    private static byte[] Handshake(int upstreamPort, uint pid)
    {
        var header = new byte[LocalHandshakeRelay.HandshakeLength];
        IPAddress.Loopback.MapToIPv6().GetAddressBytes().CopyTo(header, 0);
        BitConverter.GetBytes(28).CopyTo(header, 16);
        BitConverter.GetBytes((ushort)upstreamPort).CopyTo(header, 20);
        BitConverter.GetBytes(pid).CopyTo(header, 22);
        return header;
    }

    private static async Task<(TcpClient Client, TcpClient Server)> Open(
        LocalHandshakeRelay relay, TcpListener upstream, int upstreamPort, uint pid, bool login)
    {
        var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        relay.OnGameTunnelOpened = (_, _, connection, _) => opened.TrySetResult(connection);
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, relay.Port).WaitAsync(Deadline);
            await client.GetStream().WriteAsync(Handshake(upstreamPort, pid)).AsTask().WaitAsync(Deadline);
            var server = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
            Assert.Equal(login, await opened.Task.WaitAsync(Deadline));
            relay.OnGameTunnelOpened = null;
            return (client, server);
        }
        catch
        {
            client.Dispose();
            relay.OnGameTunnelOpened = null;
            throw;
        }
    }

    [Fact]
    public async Task Replaced_game_tunnel_cannot_feed_old_bytes_or_become_injection_target()
    {
        using var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        int port = ((IPEndPoint)upstream.LocalEndpoint).Port;
        using var relay = new LocalHandshakeRelay(0, _ => true, _ => true, _ => null,
            allowLoopbackUpstream: true);
        relay.Start();
        var first = await Open(relay, upstream, port, 77, login: true);
        using var c1 = first.Client; using var s1 = first.Server;
        var old = await Open(relay, upstream, port, 77, login: false);
        using var oldClient = old.Client; using var oldServer = old.Server;
        var current = await Open(relay, upstream, port, 77, login: false);
        using var newClient = current.Client; using var newServer = current.Server;

        var observed = new ConcurrentQueue<byte>();
        var newSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        relay.OnData = (_, outbound, bytes) =>
        {
            if (outbound) return;
            foreach (byte b in bytes) observed.Enqueue(b);
            if (bytes.Contains((byte)42)) newSeen.TrySetResult();
        };
        try { await oldServer.GetStream().WriteAsync(new byte[] { 99 }); }
        catch (IOException) { } catch (ObjectDisposedException) { } // A replaced tunnel may already be closed.
        await newServer.GetStream().WriteAsync(new byte[] { 42 });
        await newSeen.Task.WaitAsync(Deadline);

        Assert.DoesNotContain((byte)99, observed);
        Assert.True(relay.Inject(77, new byte[] { 41 }));
        var injected = new byte[1];
        await newServer.GetStream().ReadExactlyAsync(injected).AsTask().WaitAsync(Deadline);
        Assert.Equal(41, injected[0]);
    }

    [Fact]
    public async Task Stop_revokes_stale_eof_callbacks_and_resets_admission_for_new_listener_epoch()
    {
        using var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        int port = ((IPEndPoint)upstream.LocalEndpoint).Port;
        using var relay = new LocalHandshakeRelay(0, _ => true, _ => true, _ => null,
            allowLoopbackUpstream: true);
        relay.Start();
        var first = await Open(relay, upstream, port, 77, login: true);
        using var c1 = first.Client; using var s1 = first.Server;
        var old = await Open(relay, upstream, port, 77, login: false);
        using var oldClient = old.Client; using var oldServer = old.Server;
        int oldCloses = 0;
        relay.OnClosed = (_, _, _) => Interlocked.Increment(ref oldCloses);
        relay.Stop();
        Assert.False(relay.Inject(77, new byte[] { 21 }));

        // An old EOF must not be delivered under the new PID's connection epoch.
        c1.Dispose(); oldClient.Dispose(); s1.Dispose(); oldServer.Dispose();
        relay.Start();
        var freshLogin = await Open(relay, upstream, port, 77, login: true);
        using var c2 = freshLogin.Client; using var s2 = freshLogin.Server;
        Assert.Equal(0, Volatile.Read(ref oldCloses));
        var freshGame = await Open(relay, upstream, port, 77, login: false);
        using var c3 = freshGame.Client; using var s3 = freshGame.Server;
        Assert.True(relay.Inject(77, new byte[] { 22 }));
        var forwarded = new byte[1];
        await s3.GetStream().ReadExactlyAsync(forwarded).AsTask().WaitAsync(Deadline);
        Assert.Equal(22, forwarded[0]);
    }

    [Fact]
    public async Task Typed_game_ingress_carries_real_source_link_and_monotone_chunk_order()
    {
        using var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        int port = ((IPEndPoint)upstream.LocalEndpoint).Port;
        using var relay = new LocalHandshakeRelay(0, _ => true, _ => true, _ => null,
            allowLoopbackUpstream: true);
        relay.Start();
        var login = await Open(relay, upstream, port, 77, login: true);
        using var loginClient = login.Client; using var loginServer = login.Server;
        var game = await Open(relay, upstream, port, 77, login: false);
        using var gameClient = game.Client; using var gameServer = game.Server;

        var frames = new ConcurrentQueue<(LocalHandshakeRelay.GameRelayLink Link, long Sequence, bool Outbound)>();
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int legacyGameFeed = 0;
        relay.OnData = (_, _, _) => Interlocked.Increment(ref legacyGameFeed);
        relay.OnGameData = (link, seq, outbound, _) =>
        {
            Assert.True(relay.IsCurrentGameLink(link));
            frames.Enqueue((link, seq, outbound));
            if (frames.Count >= 2) signal.TrySetResult();
        };

        await gameServer.GetStream().WriteAsync(new byte[] { 15 });
        var target = new byte[1];
        await gameClient.GetStream().ReadExactlyAsync(target).AsTask().WaitAsync(Deadline);
        await gameServer.GetStream().WriteAsync(new byte[] { 16 });
        await gameClient.GetStream().ReadExactlyAsync(target).AsTask().WaitAsync(Deadline);
        await signal.Task.WaitAsync(Deadline);

        var first = frames.ToArray();
        Assert.Equal(2, first.Length);
        Assert.Equal(first[0].Link, first[1].Link);
        Assert.Equal(1, first[0].Sequence);
        Assert.Equal(2, first[1].Sequence);
        Assert.All(first, x => Assert.False(x.Outbound));
        Assert.Equal(0, Volatile.Read(ref legacyGameFeed));
        var oldLink = first[0].Link;

        var replacement = await Open(relay, upstream, port, 77, login: false);
        using var newerClient = replacement.Client; using var newerServer = replacement.Server;
        Assert.False(relay.IsCurrentGameLink(oldLink));
        relay.Stop();
        Assert.False(relay.IsCurrentGameLink(oldLink));
    }

    [Fact]
    public async Task Actual_local_link_validation_reports_offline_latency_and_allocations()
    {
        using var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        int port = ((IPEndPoint)upstream.LocalEndpoint).Port;
        using var relay = new LocalHandshakeRelay(0, _ => true, _ => true, _ => null,
            allowLoopbackUpstream: true);
        relay.Start();
        var login = await Open(relay, upstream, port, 77, login: true);
        using var loginClient = login.Client; using var loginServer = login.Server;
        var game = await Open(relay, upstream, port, 77, login: false);
        using var gameClient = game.Client; using var gameServer = game.Server;
        var observed = new TaskCompletionSource<LocalHandshakeRelay.GameRelayLink>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        relay.OnGameData = (link, _, outbound, _) =>
        {
            if (!outbound) observed.TrySetResult(link);
        };
        await gameServer.GetStream().WriteAsync(new byte[] { 123 });
        var receiver = new byte[1];
        await gameClient.GetStream().ReadExactlyAsync(receiver).AsTask().WaitAsync(Deadline);
        var link = await observed.Task.WaitAsync(Deadline);

        for (int i = 0; i < 1024; i++) Assert.True(relay.IsCurrentGameLink(link));
        const int checks = 50_000;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        bool allCurrent = true;
        for (int i = 0; i < checks; i++) allCurrent &= relay.IsCurrentGameLink(link);
        stopwatch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        _output.WriteLine($"V5 offline relay link validation cost: {checks} current-source checks " +
            $"in {stopwatch.Elapsed.TotalMilliseconds:F3} ms, {allocated} B allocated on caller thread; " +
            "loopback fake upstream, not a gameplay or live latency benchmark.");
        Assert.True(allCurrent);
        relay.Stop();
        Assert.False(relay.IsCurrentGameLink(link));
    }
}
