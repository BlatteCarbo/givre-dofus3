using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using GivreClone.App.Services.LocalLaunch;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Real loopback hook-handshake -> relay -> fake upstream game tunnel.
/// OnGameData is a source-stamped observation of bytes the relay wrote to
/// its upstream socket; it is never a server receipt or gameplay ACK.
/// </summary>
public sealed class LocalMitmForwardedBytesV21Tests
{
    private const uint OwnerPid = 42121;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(8);
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed record Observed(
        LocalHandshakeRelay.GameRelayLink Link,
        long Sequence,
        bool ClientToServer,
        byte[] Bytes);

    private static TcpListener StartUpstream()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return listener;
    }

    private static byte[] Handshake(int upstreamPort, uint pid)
    {
        var bytes = new byte[LocalHandshakeRelay.HandshakeLength];
        IPAddress.Loopback.MapToIPv6().GetAddressBytes().CopyTo(bytes, 0);
        BitConverter.GetBytes(28).CopyTo(bytes, 16);
        BitConverter.GetBytes((ushort)upstreamPort).CopyTo(bytes, 20);
        BitConverter.GetBytes(pid).CopyTo(bytes, 22);
        return bytes;
    }

    private static byte[] Frame(byte marker)
    {
        byte[] body = [0x08, marker];
        return RawProtobuf.EncodeVarint((ulong)body.Length).Concat(body).ToArray();
    }

    private static async Task<(TcpClient Client, TcpClient Server)> OpenTunnel(
        LocalHandshakeRelay relay, TcpListener upstream, int upstreamPort,
        bool expectConnectionServer)
    {
        var opened = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        relay.OnGameTunnelOpened = (_, _, connectionServer, _) =>
            opened.TrySetResult(connectionServer);
        var client = new TcpClient();
        TcpClient? server = null;
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, relay.Port)
                .WaitAsync(Deadline);
            await client.GetStream().WriteAsync(Handshake(upstreamPort, OwnerPid))
                .AsTask().WaitAsync(Deadline);
            server = await upstream.AcceptTcpClientAsync().WaitAsync(Deadline);
            Assert.Equal(expectConnectionServer,
                await opened.Task.WaitAsync(Deadline));
            return (client, server);
        }
        catch
        {
            client.Dispose();
            server?.Dispose();
            throw;
        }
        finally
        {
            relay.OnGameTunnelOpened = null;
        }
    }

    private static async Task<(TcpClient LoginClient, TcpClient LoginServer,
        TcpClient GameClient, TcpClient GameServer)> OpenLoginAndGame(
        LocalHandshakeRelay relay, TcpListener upstream, int upstreamPort)
    {
        var login = await OpenTunnel(relay, upstream, upstreamPort,
            expectConnectionServer: true);
        try
        {
            var game = await OpenTunnel(relay, upstream, upstreamPort,
                expectConnectionServer: false);
            return (login.Client, login.Server, game.Client, game.Server);
        }
        catch
        {
            login.Client.Dispose();
            login.Server.Dispose();
            throw;
        }
    }

    // Synchronize a split-frame test on the actual tunnel's UpstreamFrameGate
    // pending bytes. This proves the first TCP read was consumed before the
    // second write, without relying on sleeps or TCP packet-boundary guesses.
    private static int PendingBytes(LocalHandshakeRelay relay)
    {
        var tunnels = typeof(LocalHandshakeRelay)
            .GetField("_gameTunnel", InstanceMembers)!.GetValue(relay)!;
        var tryGet = tunnels.GetType().GetMethods()
            .Single(method => method.Name == "TryGetValue" &&
                              method.GetParameters().Length == 2);
        object?[] arguments = [OwnerPid, null];
        if (!(bool)tryGet.Invoke(tunnels, arguments)!) return -1;
        var tunnel = arguments[1]!;
        var gate = tunnel.GetType().GetProperty("Gate", InstanceMembers)!
            .GetValue(tunnel)!;
        var pending = gate.GetType().GetField("_pending", InstanceMembers)!
            .GetValue(gate)!;
        return ((ICollection)pending).Count;
    }

    private static long DroppedFrames(LocalHandshakeRelay relay)
    {
        var tunnels = typeof(LocalHandshakeRelay)
            .GetField("_gameTunnel", InstanceMembers)!.GetValue(relay)!;
        var tryGet = tunnels.GetType().GetMethods()
            .Single(method => method.Name == "TryGetValue" &&
                              method.GetParameters().Length == 2);
        object?[] arguments = [OwnerPid, null];
        if (!(bool)tryGet.Invoke(tunnels, arguments)!) return -1;
        var gate = arguments[1]!.GetType()
            .GetProperty("Gate", InstanceMembers)!.GetValue(arguments[1])!;
        return (long)gate.GetType()
            .GetProperty("Dropped", InstanceMembers)!.GetValue(gate)!;
    }

    private static async Task WaitUntil(Func<bool> predicate, string failure)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < Deadline)
        {
            if (predicate()) return;
            await Task.Delay(2);
        }
        throw new TimeoutException(failure);
    }

    private static void AssertExactSource(
        LocalHandshakeRelay relay, IReadOnlyCollection<Observed> observations)
    {
        Assert.NotEmpty(observations);
        var link = observations.First().Link;
        Assert.NotEqual(Guid.Empty, link.RelayInstanceId);
        Assert.Equal(OwnerPid, link.OwnerPid);
        Assert.True(link.RunEpoch > 0);
        Assert.True(link.TunnelId > 0);
        Assert.True(relay.IsCurrentGameLink(link));
        Assert.All(observations, observation =>
        {
            Assert.Equal(link, observation.Link);
            Assert.True(observation.ClientToServer);
            Assert.True(observation.Sequence > 0);
            Assert.True(relay.IsCurrentGameLink(observation.Link));
        });
        Assert.Equal(Enumerable.Range(1, observations.Count)
            .Select(value => (long)value),
            observations.Select(observation => observation.Sequence));
    }

    [Fact]
    public async Task Split_frame_is_observed_once_only_after_its_complete_forwarded_bytes()
    {
        using var upstream = StartUpstream();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        using var relay = new LocalHandshakeRelay(0, _ => true, _ => true,
            _ => null, allowLoopbackUpstream: true);
        relay.Start();
        var tunnels = await OpenLoginAndGame(relay, upstream, upstreamPort);
        using var loginClient = tunnels.LoginClient;
        using var loginServer = tunnels.LoginServer;
        using var gameClient = tunnels.GameClient;
        using var gameServer = tunnels.GameServer;

        var observations = new ConcurrentQueue<Observed>();
        var published = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        relay.DropClientFrame = (_, _) => false;
        relay.OnGameData = (link, sequence, clientToServer, bytes) =>
        {
            observations.Enqueue(new(link, sequence, clientToServer, bytes.ToArray()));
            published.TrySetResult();
        };

        byte[] completeFrame = Frame(0x31);
        await gameClient.GetStream().WriteAsync(completeFrame.AsMemory(0, 2));
        await WaitUntil(() => PendingBytes(relay) == 2,
            "The relay did not retain the first partial frame.");

        // The prefix/body fragment is not sent upstream and is not decoder
        // input. Wait for the gate's actual pending state before sending tail.
        Assert.Equal(0, gameServer.Available);
        Assert.Empty(observations);

        await gameClient.GetStream().WriteAsync(completeFrame.AsMemory(2));
        byte[] forwarded = new byte[completeFrame.Length];
        await gameServer.GetStream().ReadExactlyAsync(forwarded)
            .AsTask().WaitAsync(Deadline);
        await published.Task.WaitAsync(Deadline);

        Assert.Equal(completeFrame, forwarded);
        var captured = observations.ToArray();
        var only = Assert.Single(captured);
        Assert.Equal(completeFrame, only.Bytes);
        Assert.Equal(1, only.Sequence);
        Assert.True(only.ClientToServer);
        AssertExactSource(relay, captured);
    }

    [Fact]
    public async Task Dropped_frame_produces_no_observation_and_next_forwarded_frame_keeps_exact_bytes()
    {
        using var upstream = StartUpstream();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        using var relay = new LocalHandshakeRelay(0, _ => true, _ => true,
            _ => null, allowLoopbackUpstream: true);
        relay.Start();
        var tunnels = await OpenLoginAndGame(relay, upstream, upstreamPort);
        using var loginClient = tunnels.LoginClient;
        using var loginServer = tunnels.LoginServer;
        using var gameClient = tunnels.GameClient;
        using var gameServer = tunnels.GameServer;

        byte[] dropped = Frame(0x42);
        byte[] kept = Frame(0x43);
        var observations = new ConcurrentQueue<Observed>();
        var keptObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        relay.DropClientFrame = (_, frame) => frame.Length == 2 && frame[1] == 0x42;
        relay.OnGameData = (link, sequence, clientToServer, bytes) =>
        {
            observations.Enqueue(new(link, sequence, clientToServer, bytes.ToArray()));
            if (bytes.AsSpan().IndexOf(kept) >= 0) keptObserved.TrySetResult();
        };

        await gameClient.GetStream().WriteAsync(dropped);
        await WaitUntil(() => DroppedFrames(relay) == 1 &&
                              PendingBytes(relay) == 0,
            "The complete dropped frame was not processed by the real gate.");
        Assert.Equal(0, gameServer.Available);

        // This later kept frame is a deterministic barrier: the same pump
        // cannot read it until it has returned from the dropped-frame path.
        await gameClient.GetStream().WriteAsync(kept);
        byte[] forwarded = new byte[kept.Length];
        await gameServer.GetStream().ReadExactlyAsync(forwarded)
            .AsTask().WaitAsync(Deadline);
        await keptObserved.Task.WaitAsync(Deadline);

        Assert.Equal(kept, forwarded);
        var captured = observations.ToArray();
        Assert.Equal(kept, captured.SelectMany(item => item.Bytes).ToArray());
        Assert.DoesNotContain(captured, item =>
            item.Bytes.AsSpan().IndexOf(dropped) >= 0);
        AssertExactSource(relay, captured);
    }

    [Fact]
    public async Task Mixed_kept_dropped_kept_batch_matches_upstream_and_decoder_byte_for_byte()
    {
        using var upstream = StartUpstream();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
        using var relay = new LocalHandshakeRelay(0, _ => true, _ => true,
            _ => null, allowLoopbackUpstream: true);
        relay.Start();
        var tunnels = await OpenLoginAndGame(relay, upstream, upstreamPort);
        using var loginClient = tunnels.LoginClient;
        using var loginServer = tunnels.LoginServer;
        using var gameClient = tunnels.GameClient;
        using var gameServer = tunnels.GameServer;

        byte[] first = Frame(0x51);
        byte[] dropped = Frame(0x52);
        byte[] last = Frame(0x53);
        byte[] input = first.Concat(dropped).Concat(last).ToArray();
        byte[] expected = first.Concat(last).ToArray();
        var observations = new ConcurrentQueue<Observed>();
        var finalObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int legacyGameCallbacks = 0;
        relay.DropClientFrame = (_, frame) => frame.Length == 2 && frame[1] == 0x52;
        relay.OnData = (_, _, _) => Interlocked.Increment(ref legacyGameCallbacks);
        relay.OnGameData = (link, sequence, clientToServer, bytes) =>
        {
            observations.Enqueue(new(link, sequence, clientToServer, bytes.ToArray()));
            if (bytes.AsSpan().IndexOf(last) >= 0) finalObserved.TrySetResult();
        };

        await gameClient.GetStream().WriteAsync(input);
        byte[] forwarded = new byte[expected.Length];
        await gameServer.GetStream().ReadExactlyAsync(forwarded)
            .AsTask().WaitAsync(Deadline);
        await finalObserved.Task.WaitAsync(Deadline);

        Assert.Equal(expected, forwarded);
        var captured = observations.ToArray();
        Assert.Equal(expected, captured.SelectMany(item => item.Bytes).ToArray());
        Assert.DoesNotContain(captured, item =>
            item.Bytes.AsSpan().IndexOf(dropped) >= 0);
        Assert.Equal(0, Volatile.Read(ref legacyGameCallbacks));
        AssertExactSource(relay, captured);
    }
}
