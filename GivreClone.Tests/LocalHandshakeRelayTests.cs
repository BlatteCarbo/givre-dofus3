using System.Net;
using System.Net.Sockets;
using GivreClone.App.Services.LocalLaunch;
using GivreLauncher.Core.Proxy;
using Xunit;

namespace GivreClone.Tests;

public class LocalHandshakeRelayTests : IDisposable
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

    private static TcpListener FakeServer(out int port) { var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; return l; }

    private static LocalHandshakeRelay Relay(Func<uint, bool>? known = null, Func<uint, bool>? owned = null, Func<uint, ProxySettings?>? proxy = null, bool loopback = true)
    {
        var r = new LocalHandshakeRelay(0, known ?? (_ => true), owned ?? (_ => true), proxy ?? (_ => null), allowLoopbackUpstream: loopback);
        r.Start(); return r;
    }

    private static string Text(byte[] b, int n) => System.Text.Encoding.ASCII.GetString(b, 0, n);
    private static byte[] Ascii(string s) => System.Text.Encoding.ASCII.GetBytes(s);

    // Accept completes before the relay continuation registers its tunnel. Opening
    // the next connection at that point can reverse connection/game classification.
    private async Task<(TcpClient Client, TcpClient Server)> OpenTunnel(
        TcpListener server, LocalHandshakeRelay relay, int upstreamPort, uint pid, bool connectionServer)
    {
        var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        relay.OnGameTunnelOpened = (_, _, first, _) => opened.TrySetResult(first);
        var client = new TcpClient();
        TcpClient? upstream = null;
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, relay.Port, Ct);
            await client.GetStream().WriteAsync(Handshake(IPAddress.Loopback, upstreamPort, pid), Ct);
            upstream = await server.AcceptTcpClientAsync(Ct);
            Assert.Equal(connectionServer, await opened.Task.WaitAsync(Ct));
            return (client, upstream);
        }
        catch { client.Dispose(); upstream?.Dispose(); throw; }
        finally { relay.OnGameTunnelOpened = null; }
    }

    [Fact]
    public void Handshake_layout_matches_the_hook_26_bytes_and_rejects_garbage()
    {
        var h = LocalHandshakeRelay.ParseHandshake(Handshake(IPAddress.Parse("1.2.3.4"), 5555, 77));
        Assert.NotNull(h);
        Assert.Equal(IPAddress.Parse("1.2.3.4"), h!.Address);
        Assert.Equal(5555, h.Port); Assert.Equal(77u, h.ProcessId); Assert.Equal(28, h.AddrLen);
        Assert.Null(LocalHandshakeRelay.ParseHandshake(new byte[25]));
        Assert.Null(LocalHandshakeRelay.ParseHandshake(new byte[26]));
        Assert.Null(LocalHandshakeRelay.ParseHandshake(Handshake(IPAddress.Parse("1.2.3.4"), 5555, 0)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Relays_both_directions_feeds_the_decoder_tap_and_injects_toward_the_server(bool decodeFirst)
    {
        using var server = FakeServer(out int sp);
        using var relay = Relay();
        relay.DecodeBeforeForward = _ => decodeFirst;
        var tap = new List<(bool C2S, string Text)>(); var gate = new object();
        var pingTapped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pongTapped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        relay.OnData = (pid, c2s, b) =>
        {
            lock (gate)
            {
                tap.Add((c2s, Text(b, b.Length)));
                // TCP may split a payload into several chunks.
                string text = string.Concat(tap.Where(t => t.C2S == c2s).Select(t => t.Text));
                if (c2s && text == "PING") pingTapped.TrySetResult();
                if (!c2s && text == "PONG") pongTapped.TrySetResult();
            }
        };
        // 1re connexion = serveur de connexion ; la 2e = serveur de jeu (cible de l'injection)
        var first = await OpenTunnel(server, relay, sp, 4242, connectionServer: true);
        using var c1 = first.Client; using var s1 = first.Server;
        var second = await OpenTunnel(server, relay, sp, 4242, connectionServer: false);
        using var c2 = second.Client; using var s2 = second.Server;

        var buf = new byte[4];
        await c2.GetStream().WriteAsync(Ascii("PING"), Ct);
        await s2.GetStream().ReadExactlyAsync(buf, Ct); Assert.Equal("PING", Text(buf, 4));
        await s2.GetStream().WriteAsync(Ascii("PONG"), Ct);
        await c2.GetStream().ReadExactlyAsync(buf, Ct); Assert.Equal("PONG", Text(buf, 4));

        Assert.True(relay.Inject(4242, Ascii("INJ")));   // vers le serveur de JEU
        await s2.GetStream().ReadExactlyAsync(buf.AsMemory(0, 3), Ct); Assert.Equal("INJ", Text(buf, 3));
        await Task.WhenAll(pingTapped.Task, pongTapped.Task).WaitAsync(Ct);
        lock (gate)
        {
            Assert.Equal("PING", string.Concat(tap.Where(t => t.C2S).Select(t => t.Text)));
            Assert.Equal("PONG", string.Concat(tap.Where(t => !t.C2S).Select(t => t.Text)));
        }
    }

    private async Task AssertRefused(TcpListener server, LocalHandshakeRelay r, byte[] handshake)
    {
        using var c = new TcpClient(); await c.ConnectAsync(IPAddress.Loopback, r.Port, Ct);
        await c.GetStream().WriteAsync(handshake, Ct);
        // EOF proves that rejection has finished; no delay and no orphaned accept
        // which could consume the valid control connection later in this test.
        Assert.Equal(0, await c.GetStream().ReadAsync(new byte[1], Ct));
        Assert.False(server.Pending());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Game_observer_completes_before_forwarding_even_without_turbo_and_exceptions_still_forward(bool throws)
    {
        using var server = FakeServer(out int sp);
        using var relay = Relay();
        relay.DecodeBeforeForward = _ => false;
        var first = await OpenTunnel(server, relay, sp, 4242, connectionServer: true);
        using var c1 = first.Client; using var s1 = first.Server;
        var second = await OpenTunnel(server, relay, sp, 4242, connectionServer: false);
        using var c2 = second.Client; using var s2 = second.Server;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        relay.OnData = (_, c2s, _) =>
        {
            if (c2s) return;
            entered.TrySetResult();
            release.Wait(Ct);
            if (throws) throw new InvalidOperationException("observer failed");
        };
        byte[] received = new byte[1];
        var read = c2.GetStream().ReadExactlyAsync(received, Ct).AsTask();
        try
        {
            await s2.GetStream().WriteAsync(new byte[] { 42 }, Ct);
            await entered.Task.WaitAsync(Ct);
            Assert.False(read.IsCompleted);
        }
        finally { release.Set(); }
        await read.WaitAsync(Ct);
        Assert.Equal(42, received[0]);
    }

    [Fact]
    public async Task Refuses_unknown_or_foreign_pids_loopback_destinations_proxies_and_bad_headers()
    {
        using var server = FakeServer(out int sp);
        using (var r = Relay(known: _ => false)) await AssertRefused(server, r, Handshake(IPAddress.Loopback, sp, 1));
        using (var r = Relay(owned: _ => false)) await AssertRefused(server, r, Handshake(IPAddress.Loopback, sp, 1));
        using (var r = Relay(loopback: false)) await AssertRefused(server, r, Handshake(IPAddress.Loopback, sp, 1));
        using (var r = Relay(proxy: _ => new ProxySettings(ProxyKind.Socks5, "127.0.0.1", 1080))) await AssertRefused(server, r, Handshake(IPAddress.Loopback, sp, 1));
        using (var r = Relay()) await AssertRefused(server, r, new byte[26]);
        using var validRelay = Relay();
        var valid = await OpenTunnel(server, validRelay, sp, 1, connectionServer: true);
        using var client = valid.Client; using var upstream = valid.Server;
    }

    [Fact]
    public void Protected_historical_ports_are_never_bound()
    {
        var r = new LocalHandshakeRelay(5658, _ => true, _ => true, _ => null);
        Assert.Throws<InvalidOperationException>(() => r.Start());
    }

    [Fact]
    public async Task Closing_reports_whether_the_server_closed_first()
    {
        using var server = FakeServer(out int sp);
        using var relay = Relay();
        var closed = new TaskCompletionSource<(bool Current, bool ServerFirst)>(TaskCreationOptions.RunContinuationsAsynchronously);
        relay.OnClosed = (pid, current, serverFirst) => closed.TrySetResult((current, serverFirst));
        var first = await OpenTunnel(server, relay, sp, 9, connectionServer: true);
        using var c1 = first.Client; using var s1 = first.Server;
        var second = await OpenTunnel(server, relay, sp, 9, connectionServer: false);
        using var c2 = second.Client; using var s2 = second.Server;
        s1.Close();   // ferme la connexion 1 côté serveur : ce n'est pas la connexion de jeu courante
        var result = await closed.Task.WaitAsync(Ct);
        Assert.False(result.Current);
        Assert.True(result.ServerFirst);
    }
}
