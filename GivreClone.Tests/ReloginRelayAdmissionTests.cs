extern alias F06App;

using System.Net;
using System.Net.Sockets;
using System.Text;
using F06App::GivreClone.App.Services.LocalLaunch;
using Xunit;

namespace GivreClone.Tests;

public sealed class ReloginRelayAdmissionTests : IDisposable
{
    private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(10));
    private CancellationToken Ct => _deadline.Token;
    public void Dispose() => _deadline.Dispose();

    private static byte[] Handshake(IPAddress ip, int port, uint pid)
    {
        var b = new byte[26];
        var v6 = ip.MapToIPv6().GetAddressBytes();
        Array.Copy(v6, 0, b, 0, 16);
        BitConverter.GetBytes(28).CopyTo(b, 16);
        BitConverter.GetBytes((ushort)port).CopyTo(b, 20);
        BitConverter.GetBytes(pid).CopyTo(b, 22);
        return b;
    }

    private static TcpListener FakeServer(out int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return listener;
    }

    private static LocalHandshakeRelay Relay(Func<uint, bool>? known = null, Func<uint, bool>? owned = null)
    {
        var relay = new LocalHandshakeRelay(0, known ?? (_ => true), owned ?? (_ => true), _ => null, allowLoopbackUpstream: true);
        relay.Start();
        return relay;
    }

    private async Task<(TcpClient Client, TcpClient Server, uint Pid)> Open(
        TcpListener server, LocalHandshakeRelay relay, int upstreamPort, uint pid, bool connectionServer)
    {
        var opened = new TaskCompletionSource<(uint Pid, bool First)>(TaskCreationOptions.RunContinuationsAsynchronously);
        relay.OnGameTunnelOpened = (seen, _, first, _) => opened.TrySetResult((seen, first));
        var client = new TcpClient();
        TcpClient? upstream = null;
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, relay.Port, Ct);
            await client.GetStream().WriteAsync(Handshake(IPAddress.Loopback, upstreamPort, pid), Ct);
            upstream = await server.AcceptTcpClientAsync(Ct);
            var result = await opened.Task.WaitAsync(Ct);
            Assert.Equal(connectionServer, result.First);
            return (client, upstream, result.Pid);
        }
        catch
        {
            client.Dispose();
            upstream?.Dispose();
            throw;
        }
        finally { relay.OnGameTunnelOpened = null; }
    }

    private async Task AssertHits(NetworkStream stream, string text)
    {
        var buf = new byte[text.Length];
        await stream.ReadExactlyAsync(buf, Ct);
        Assert.Equal(text, Encoding.ASCII.GetString(buf));
    }

    [Fact]
    public async Task Relogin_reconnect_keeps_the_account_and_does_not_steal_the_inject_target()
    {
        using var server = FakeServer(out int upstreamPort);
        using var relay = Relay();
        var first = await Open(server, relay, upstreamPort, 7, connectionServer: true);
        using var c1 = first.Client; using var s1 = first.Server;
        var game = await Open(server, relay, upstreamPort, 7, connectionServer: false);
        using var c2 = game.Client; using var s2 = game.Server;
        Assert.Equal(7u, game.Pid);

        var third = await Open(server, relay, upstreamPort, 7, connectionServer: false);
        using var c3 = third.Client; using var s3 = third.Server;
        Assert.True(relay.Inject(7, Encoding.ASCII.GetBytes("UNARMED")));
        await AssertHits(s3.GetStream(), "UNARMED");

        relay.ArmRelogin(7);
        var login = await Open(server, relay, upstreamPort, 7, connectionServer: true);
        using var c4 = login.Client; using var s4 = login.Server;
        Assert.Equal(7u, login.Pid);
        Assert.False(relay.GameSocketReplaced(7));
        Assert.True(relay.Inject(7, Encoding.ASCII.GetBytes("STAY")));
        await AssertHits(s3.GetStream(), "STAY");

        var replaced = await Open(server, relay, upstreamPort, 7, connectionServer: false);
        using var c5 = replaced.Client; using var s5 = replaced.Server;
        Assert.Equal(7u, replaced.Pid);
        Assert.True(relay.GameSocketReplaced(7));
        Assert.False(relay.CloseGameClient(7));
        Assert.True(relay.Inject(7, Encoding.ASCII.GetBytes("NEW")));
        await AssertHits(s5.GetStream(), "NEW");
    }

    [Fact]
    public async Task Relogin_adopts_one_unknown_socket_onto_the_armed_account()
    {
        using var server = FakeServer(out int upstreamPort);
        using var relay = Relay(known: pid => pid == 7, owned: pid => pid == 7);
        using (var refused = new TcpClient())
        {
            await refused.ConnectAsync(IPAddress.Loopback, relay.Port, Ct);
            await refused.GetStream().WriteAsync(Handshake(IPAddress.Loopback, upstreamPort, 99), Ct);
            Assert.Equal(0, await refused.GetStream().ReadAsync(new byte[1], Ct));
            Assert.False(server.Pending());
        }

        relay.ArmRelogin(7);
        var login = await Open(server, relay, upstreamPort, 99, connectionServer: true);
        using var c1 = login.Client; using var s1 = login.Server;
        Assert.Equal(7u, login.Pid);

        var seen = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
        relay.OnData = (pid, clientToServer, _) => { if (clientToServer) seen.TrySetResult(pid); };
        await c1.GetStream().WriteAsync(new byte[] { 1 }, Ct);
        Assert.Equal(7u, await seen.Task.WaitAsync(Ct));

        var game = await Open(server, relay, upstreamPort, 99, connectionServer: false);
        using var c2 = game.Client; using var s2 = game.Server;
        Assert.Equal(7u, game.Pid);
        Assert.True(relay.Inject(7, Encoding.ASCII.GetBytes("PICK")));
        await AssertHits(s2.GetStream(), "PICK");
    }

    [Fact]
    public async Task Relogin_close_drops_the_original_game_socket_only()
    {
        using var server = FakeServer(out int upstreamPort);
        using var relay = Relay();
        var first = await Open(server, relay, upstreamPort, 7, connectionServer: true);
        using var c1 = first.Client; using var s1 = first.Server;
        var game = await Open(server, relay, upstreamPort, 7, connectionServer: false);
        using var c2 = game.Client; using var s2 = game.Server;
        relay.ArmRelogin(7);
        var login = await Open(server, relay, upstreamPort, 7, connectionServer: true);
        using var c3 = login.Client; using var s3 = login.Server;

        Assert.True(relay.CloseGameClient(7));
        Assert.Equal(0, await c2.GetStream().ReadAsync(new byte[1], Ct));

        var replaced = await Open(server, relay, upstreamPort, 7, connectionServer: false);
        using var c4 = replaced.Client; using var s4 = replaced.Server;
        Assert.False(relay.CloseGameClient(7));
        Assert.True(relay.Inject(7, Encoding.ASCII.GetBytes("LIVE")));
        await AssertHits(s4.GetStream(), "LIVE");
    }
}
