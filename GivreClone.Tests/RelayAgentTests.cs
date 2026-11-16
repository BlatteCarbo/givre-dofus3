using System;
using System.Buffers.Binary;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GivreRelayAgent;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Agent GivreRelay (l'exe de l'ami) : règles du partage (handoff §3.4) et dialogue complet avec un faux gateway
/// WebSocket local — accueil, flux autorisé relayé dans les deux sens, refus hors partage, refus du réseau local de l'ami,
/// fermeture côté destination, code révoqué (4001).</summary>
public sealed class RelayAgentTests
{
    // ------------------------------------------------------------------ règles

    private static HostPolicy Ankama() => new(false, new[] { "ankama.com", "ankama-games.com" },
                                              new[] { "dofus2-co-production.ankama-games.com" }, new[] { 443, 5555 });

    [Theory]
    [InlineData("haapi.ankama.com", 443, true)]
    [InlineData("dofus2-ga-kourial.ankama-games.com", 5555, true)]
    [InlineData("HAAPI.Ankama.com.", 443, true)]          // casse et point final normalisés
    [InlineData("ankama.com", 443, true)]
    [InlineData("evilankama.com", 443, false)]            // suffixe sans point : refusé
    [InlineData("ankama.com.evil.net", 443, false)]
    [InlineData("haapi.ankama.com", 80, false)]           // port hors liste
    [InlineData("api.ipify.org", 443, false)]
    [InlineData("1.2.3.4", 5555, false)]                  // IP jamais résolue depuis un nom de « resolve »
    public void Dofus_only_scope_filters_by_name_and_port(string host, int port, bool allowed)
        => Assert.Equal(allowed, Ankama().Allows(host, port));

    [Fact]
    public void Ip_is_allowed_only_while_a_recent_resolution_backs_it()
    {
        var p = Ankama();
        var ip = IPAddress.Parse("34.247.205.111");
        Assert.False(p.Allows("34.247.205.111", 5555));
        p.Remember(ip, DateTime.UtcNow.AddMinutes(10));
        Assert.True(p.Allows("34.247.205.111", 5555));
        Assert.False(p.Allows("34.247.205.111", 22));
        p.Remember(ip, DateTime.UtcNow.AddSeconds(-1));     // résolution périmée
        Assert.False(p.Allows("34.247.205.111", 5555));
    }

    [Fact]
    public void Everything_scope_is_web_ports_plus_the_dofus_rule()
    {
        using var doc = JsonDocument.Parse("""{"type":"welcome","scope":"all","allowed_hosts":null}""");
        var p = HostPolicy.FromWelcome(doc.RootElement);
        Assert.True(p.ScopeAll);
        Assert.True(p.Allows("example.com", 443));
        Assert.True(p.Allows("example.com", 80));
        Assert.False(p.Allows("example.com", 5555));
        Assert.False(p.Allows("example.com", 22));
        Assert.True(p.Allows("dofus2-co-production.ankama-games.com", 5555));   // le jeu passe aussi en « Tout »
    }

    [Fact]
    public void Welcome_allowed_hosts_are_read()
    {
        using var doc = JsonDocument.Parse("""
            {"type":"welcome","scope":"ankama","allowed_hosts":{"suffixes":["ankama.com"],"resolve":["a.ankama-games.com","b.ankama-games.com"],"ports":[443]}}
            """);
        var p = HostPolicy.FromWelcome(doc.RootElement);
        Assert.False(p.ScopeAll);
        Assert.Equal(new[] { "ankama.com" }, p.Suffixes);
        Assert.Equal(2, p.Resolve.Count);
        Assert.False(p.Allows("x.ankama-games.com", 443));   // suffixe non listé par ce gateway
        Assert.False(p.Allows("haapi.ankama.com", 5555));    // port non listé
        Assert.True(p.Allows("haapi.ankama.com", 443));
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("52.49.75.221", true)]
    [InlineData("172.32.0.1", true)]
    [InlineData("2a00:1450:4007::1", true)]
    [InlineData("10.0.0.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.1.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:192.168.1.1", false)]
    [InlineData("240.0.0.1", false)]
    [InlineData("::", false)]
    [InlineData("ff02::1", false)]
    public void Friend_local_network_is_never_reachable(string ip, bool isPublic)
        => Assert.Equal(isPublic, HostPolicy.IsPublic(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("[2a00:1450:4007::1]", "2a00:1450:4007::1")]
    [InlineData("fe80::1%eth0", "fe80::1")]
    [InlineData("52.49.75.221", "52.49.75.221")]
    public void Ip_literals_lose_brackets_and_zone_before_parsing(string host, string expected)
    {
        Assert.True(HostPolicy.TryParseIp(host, out var ip));
        Assert.Equal(IPAddress.Parse(expected), ip);
        Assert.False(HostPolicy.TryParseIp("haapi.ankama.com", out _));
    }

    // ------------------------------------------------------------------ dialogue avec le gateway

    [Fact]
    public async Task Agent_relays_an_allowed_stream_and_refuses_the_rest()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var gw = new FakeGateway();
        using var echo = new EchoServer();
        var link = new AgentLink(gw.Uri, "TESTTOKEN123", reachable: _ => true);   // écho local : réseau local permis ICI seulement
        var run = link.RunAsync(cts.Token);

        var ws = await gw.AcceptAsync(cts.Token);
        Assert.Equal("Bearer TESTTOKEN123", gw.Authorization);
        await gw.SendAsync($$$"""
            {"type":"welcome","exit_id":"e1","label":"Chez Paul","scope":"ankama","quota_bytes":1000000,"used_bytes":10,
             "public_ip":"92.184.112.79","max_streams":30,"allowed_hosts":{"suffixes":["localhost"],"resolve":[],"ports":[{{{echo.Port}}}]}}
            """, cts.Token);

        // flux autorisé : connected, puis écho dans les deux sens
        await gw.SendAsync($$"""{"type":"connect","stream_id":7,"dest_host":"localhost","dest_port":{{echo.Port}}}""", cts.Token);
        var connected = await gw.ReceiveJsonAsync(cts.Token);
        Assert.Equal("connected", connected.GetProperty("type").GetString());
        Assert.Equal(7u, connected.GetProperty("stream_id").GetUInt32());
        Assert.Equal("Chez Paul", link.Label);
        Assert.Equal(LinkState.Connected, link.State);

        var frame = new byte[4 + 5];
        BinaryPrimitives.WriteUInt32BigEndian(frame, 7);
        Encoding.ASCII.GetBytes("hello").CopyTo(frame, 4);
        await ws.SendAsync(frame, WebSocketMessageType.Binary, true, cts.Token);
        var back = await gw.ReceiveBinaryAsync(cts.Token);
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32BigEndian(back));
        Assert.Equal("hello", Encoding.ASCII.GetString(back, 4, back.Length - 4));

        // hors partage : nom non couvert, puis port non couvert
        await gw.SendAsync($$"""{"type":"connect","stream_id":8,"dest_host":"evil.example.com","dest_port":{{echo.Port}}}""", cts.Token);
        var r8 = await gw.ReceiveJsonAsync(cts.Token);
        Assert.Equal("stream_refused", r8.GetProperty("type").GetString());
        Assert.Equal("scope_denied", r8.GetProperty("reason").GetString());
        await gw.SendAsync("""{"type":"connect","stream_id":9,"dest_host":"localhost","dest_port":6666}""", cts.Token);
        var r9 = await gw.ReceiveJsonAsync(cts.Token);
        Assert.Equal(9u, r9.GetProperty("stream_id").GetUInt32());
        Assert.Equal("scope_denied", r9.GetProperty("reason").GetString());

        // la destination ferme : l'agent prévient le gateway
        echo.CloseAll();
        var closed = await gw.ReceiveJsonAsync(cts.Token);
        Assert.Equal("stream_close", closed.GetProperty("type").GetString());
        Assert.Equal(7u, closed.GetProperty("stream_id").GetUInt32());

        // code révoqué sur le portail : l'agent s'arrête et redemandera un code
        await ws.CloseAsync((WebSocketCloseStatus)4001, "revoked", cts.Token);
        await run.WaitAsync(cts.Token);
        Assert.Equal(LinkState.TokenRejected, link.State);
    }

    [Fact]
    public async Task Agent_never_dials_the_friend_local_network()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var gw = new FakeGateway();
        using var echo = new EchoServer();
        var link = new AgentLink(gw.Uri, "TESTTOKEN123");   // règle de production : Internet seulement
        var run = link.RunAsync(cts.Token);
        await gw.AcceptAsync(cts.Token);
        await gw.SendAsync($$$"""
            {"type":"welcome","exit_id":"e1","label":"x","scope":"ankama","allowed_hosts":{"suffixes":["localhost"],"resolve":[],"ports":[{{{echo.Port}}}]}}
            """, cts.Token);
        await gw.SendAsync($$"""{"type":"connect","stream_id":1,"dest_host":"localhost","dest_port":{{echo.Port}}}""", cts.Token);
        var r = await gw.ReceiveJsonAsync(cts.Token);
        Assert.Equal("stream_refused", r.GetProperty("type").GetString());
        Assert.Equal("scope_denied", r.GetProperty("reason").GetString());
        Assert.Equal(0, echo.Accepted);
        cts.Cancel();
        try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task Failed_connections_report_one_of_the_three_stable_reasons()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var gw = new FakeGateway();
        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        int closedPort = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();   // port libre, personne n'écoute : connexion refusée
        var link = new AgentLink(gw.Uri, "TESTTOKEN123", reachable: _ => true);
        var run = link.RunAsync(cts.Token);
        await gw.AcceptAsync(cts.Token);
        await gw.SendAsync($$$"""
            {"type":"welcome","exit_id":"e1","label":"x","scope":"ankama","allowed_hosts":{"suffixes":["localhost","invalid"],"resolve":[],"ports":[{{{closedPort}}}]}}
            """, cts.Token);

        await gw.SendAsync($$"""{"type":"connect","stream_id":4,"dest_host":"localhost","dest_port":{{closedPort}}}""", cts.Token);
        var r4 = await gw.ReceiveJsonAsync(cts.Token);
        Assert.Equal("connect_failed", r4.GetProperty("type").GetString());
        Assert.Equal("unreachable", r4.GetProperty("reason").GetString());

        await gw.SendAsync($$"""{"type":"connect","stream_id":5,"dest_host":"nope.invalid","dest_port":{{closedPort}}}""", cts.Token);
        var r5 = await gw.ReceiveJsonAsync(cts.Token);
        Assert.Equal("connect_failed", r5.GetProperty("type").GetString());
        Assert.Equal("dns_failed", r5.GetProperty("reason").GetString());
        cts.Cancel();
        try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task Rules_change_reconnects_at_once_and_usage_follows_the_gateway()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var gw = new FakeGateway();
        var link = new AgentLink(gw.Uri, "TESTTOKEN123");
        var run = link.RunAsync(cts.Token);
        var ws = await gw.AcceptAsync(cts.Token);
        await gw.SendAsync("""{"type":"welcome","exit_id":"e1","label":"x","scope":"ankama","quota_bytes":1000,"used_bytes":10}""", cts.Token);
        await gw.SendAsync("""{"type":"exit_update","exit_id":"e1","online":true,"used_bytes":5000,"quota_bytes":9000}""", cts.Token);
        while (link.UsedBytes != 5000 || link.QuotaBytes != 9000) await Task.Delay(20, cts.Token);   // remplace, n'ajoute pas

        // 1012 : scope changé sur le portail -> nouvelle connexion immédiate (nouveau welcome, nouvelles règles)
        await ws.CloseAsync((WebSocketCloseStatus)1012, "scope changed", cts.Token);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await gw.AcceptAsync(cts.Token);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"reconnexion en {sw.Elapsed}");
        await gw.SendAsync("""{"type":"welcome","exit_id":"e1","label":"x","scope":"all","allowed_hosts":{"suffixes":["ankama.com"],"resolve":[],"ports":[443,5555]}}""", cts.Token);
        while (!link.ScopeAll) await Task.Delay(20, cts.Token);
        cts.Cancel();
        try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task Same_code_elsewhere_stops_this_agent()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var gw = new FakeGateway();
        var link = new AgentLink(gw.Uri, "TESTTOKEN123");
        var run = link.RunAsync(cts.Token);
        var ws = await gw.AcceptAsync(cts.Token);
        await gw.SendAsync("""{"type":"error","message":"replaced"}""", cts.Token);   // le gateway prévient avant de fermer
        await ws.CloseAsync((WebSocketCloseStatus)4009, "replaced", cts.Token);
        await run.WaitAsync(cts.Token);   // s'arrête au lieu de se reconnecter (sinon éjection mutuelle en boucle)
        Assert.Equal(LinkState.Replaced, link.State);
    }

    /// <summary>L'exe PUBLIÉ (NativeAOT) parle au gateway comme le code testé ci-dessus. Lancé seulement si GIVRE_AGENT_EXE
    /// pointe sur prod-builds\agent\GivreRelay.exe (build-agent.bat) : le test enregistre un code factice, que la fermeture
    /// 4001 efface aussitôt (l'agent oublie un code refusé).</summary>
    [Fact]
    public async Task Published_agent_exe_speaks_the_protocol()
    {
        string? exe = Environment.GetEnvironmentVariable("GIVRE_AGENT_EXE");
        if (string.IsNullOrEmpty(exe) || !System.IO.File.Exists(exe)) return;
        // Un agent déjà lancé sur ce poste (une seule instance permise) : on ne le dérange pas.
        if (System.Diagnostics.Process.GetProcessesByName("GivreRelay").Length > 0) return;
        // Un vrai code déjà enregistré sur ce poste : le test l'écraserait (puis l'effacerait sur 4001). On s'abstient.
        if (System.IO.File.Exists(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GivreRelay", "agent.dat"))) return;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var gw = new FakeGateway();
        var psi = new System.Diagnostics.ProcessStartInfo(exe)
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in new[] { "--code", "TESTCODE1234", "--server", gw.Uri.ToString() }) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEndAsync();
        try
        {
            var ws = await gw.AcceptAsync(cts.Token);
            Assert.Equal("Bearer TESTCODE1234", gw.Authorization);
            await gw.SendAsync("""
                {"type":"welcome","exit_id":"e1","label":"Chez Paul","scope":"ankama","quota_bytes":1000,"used_bytes":0,"max_streams":30,
                 "allowed_hosts":{"suffixes":["ankama.com","localhost"],"resolve":[],"ports":[443,5555]}}
                """, cts.Token);
            await gw.SendAsync("""{"type":"connect","stream_id":3,"dest_host":"localhost","dest_port":5555}""", cts.Token);
            var r = await gw.ReceiveJsonAsync(cts.Token);
            Assert.Equal("stream_refused", r.GetProperty("type").GetString());   // boucle locale : jamais
            Assert.Equal(3u, r.GetProperty("stream_id").GetUInt32());
            await ws.CloseAsync((WebSocketCloseStatus)4001, "revoked", cts.Token);
            await p.WaitForExitAsync(cts.Token);
            Assert.Equal(0, p.ExitCode);
            string log = await output;
            Assert.Contains("connecté : « Chez Paul »", log);
            Assert.Contains("Ce code n'est plus valable", log);
        }
        finally { try { if (!p.HasExited) p.Kill(); } catch { } }
    }

    // ------------------------------------------------------------------ outillage

    /// <summary>Gateway minimal : poignée HTTP Upgrade à la main puis WebSocket serveur .NET.</summary>
    private sealed class FakeGateway : IDisposable
    {
        private readonly TcpListener _l = new(IPAddress.Loopback, 0);
        private TcpClient? _c;
        public WebSocket? Ws { get; private set; }
        public string? Authorization { get; private set; }
        public Uri Uri { get; }

        public FakeGateway()
        {
            _l.Start();
            Uri = new Uri($"ws://127.0.0.1:{((IPEndPoint)_l.LocalEndpoint).Port}/relay/agent");
        }

        public async Task<WebSocket> AcceptAsync(CancellationToken ct)
        {
            _c = await _l.AcceptTcpClientAsync(ct);
            var s = _c.GetStream();
            var head = new StringBuilder();
            var one = new byte[1];
            while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                await s.ReadExactlyAsync(one, ct);
                head.Append((char)one[0]);
            }
            var lines = head.ToString().Split("\r\n");
            string Header(string n) => lines.First(l => l.StartsWith(n + ":", StringComparison.OrdinalIgnoreCase)).Split(':', 2)[1].Trim();
            Authorization = Header("Authorization");
            string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(Header("Sec-WebSocket-Key") + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            await s.WriteAsync(Encoding.ASCII.GetBytes(
                $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"), ct);
            Ws = WebSocket.CreateFromStream(s, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.Zero });
            return Ws;
        }

        public Task SendAsync(string json, CancellationToken ct)
            => Ws!.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, ct);

        private async Task<(WebSocketMessageType Type, byte[] Data)> ReceiveAsync(CancellationToken ct)
        {
            var buf = new byte[80 * 1024];
            using var acc = new System.IO.MemoryStream();
            while (true)
            {
                var r = await Ws!.ReceiveAsync(buf, ct);
                acc.Write(buf, 0, r.Count);
                if (r.EndOfMessage) return (r.MessageType, acc.ToArray());
            }
        }

        public async Task<JsonElement> ReceiveJsonAsync(CancellationToken ct)
        {
            var (t, d) = await ReceiveAsync(ct);
            Assert.Equal(WebSocketMessageType.Text, t);
            return JsonDocument.Parse(d).RootElement.Clone();
        }

        public async Task<byte[]> ReceiveBinaryAsync(CancellationToken ct)
        {
            var (t, d) = await ReceiveAsync(ct);
            Assert.Equal(WebSocketMessageType.Binary, t);
            return d;
        }

        public void Dispose()
        {
            try { Ws?.Dispose(); } catch { }
            try { _c?.Dispose(); } catch { }
            _l.Stop();
        }
    }

    /// <summary>Destination factice : renvoie ce qu'elle reçoit.</summary>
    private sealed class EchoServer : IDisposable
    {
        private readonly TcpListener _l = new(IPAddress.Loopback, 0);
        private readonly System.Collections.Concurrent.ConcurrentBag<TcpClient> _clients = new();
        private int _accepted;
        public int Port { get; }
        public int Accepted => Volatile.Read(ref _accepted);

        public EchoServer()
        {
            _l.Start();
            Port = ((IPEndPoint)_l.LocalEndpoint).Port;
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    TcpClient c;
                    try { c = await _l.AcceptTcpClientAsync(); } catch { return; }
                    Interlocked.Increment(ref _accepted);
                    _clients.Add(c);
                    _ = Task.Run(async () => { try { await c.GetStream().CopyToAsync(c.GetStream()); } catch { } });
                }
            });
        }

        public void CloseAll() { foreach (var c in _clients) try { c.Client.Shutdown(SocketShutdown.Both); c.Dispose(); } catch { } }

        public void Dispose() { CloseAll(); _l.Stop(); }
    }
}
