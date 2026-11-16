using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using GivreControl;

namespace GivreClone.App.Services.Mcp;

/// <summary>Contrôle à distance (téléphone) : petit serveur HTTP/1.1 sur la machine, DÉSACTIVÉ par défaut. Sert l'interface web mobile (pages publiques,
/// sans donnée) et relaie <c>POST /api/tool/&lt;outil&gt;</c> vers les mêmes outils que le MCP, avec les mêmes garde-fous (comptes protégés, actions
/// autorisées, confirmation des suppressions). Jeton obligatoire sur /api ; adresses autorisées : boucle locale, Tailscale et, si demandé, réseau local.</summary>
public sealed class RemoteControlServer : IDisposable
{
    private const int MaxHeaderBytes = 16 * 1024, MaxBodyBytes = 256 * 1024, MaxConnections = 16;
    private readonly LocalControlBridge _bridge;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _slots = new(MaxConnections);
    private readonly ConcurrentDictionary<string, (int Fails, DateTime Until)> _bans = new();
    private TcpListener? _listener;

    public int Port { get; private set; }
    public RemoteControlServer(LocalControlBridge bridge) => _bridge = bridge;

    public bool Start()
    {
        var cfg = AppConfig.Current;
        if (string.IsNullOrEmpty(cfg.RemoteToken)) { cfg.RemoteToken = RemoteAccessPolicy.NewToken(); AppConfig.Save(); }
        try
        {
            Port = Math.Clamp(cfg.RemotePort, 1024, 65535);
            _listener = new TcpListener(IPAddress.IPv6Any, Port) { ExclusiveAddressUse = true };
            _listener.Server.DualMode = true;
            _listener.Start(32);
        }
        catch (SocketException) { _listener = null; return false; }
        _ = AcceptLoop();
        return true;
    }

    public void Dispose() { _stop.Cancel(); try { _listener?.Stop(); } catch { } }

    private async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(_stop.Token); }
            catch { break; }
            _ = Task.Run(() => Serve(client));
        }
    }

    private async Task Serve(TcpClient client)
    {
        using var _c = client;
        var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
        if (remote is null || !RemoteAccessPolicy.IsAllowed(remote, AppConfig.Current.RemoteAllowLan)) return;   // adresse publique : fermeture silencieuse
        if (!await _slots.WaitAsync(0)) return;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            cts.CancelAfter(TimeSpan.FromSeconds(70));
            var stream = client.GetStream();
            var (method, path, headers, body) = await ReadRequest(stream, cts.Token);
            if (method is null) { await Reply(stream, 400, "text/plain", "bad request", cts.Token); return; }
            string ipKey = RemoteAccessPolicy.Normalize(remote).ToString();
            if (method == "GET" && (path == "/" || path == "/index.html")) { await Reply(stream, 200, "text/html; charset=utf-8", RemoteWebUi.Html, cts.Token); return; }
            if (method == "GET" && path == "/manifest.webmanifest") { await Reply(stream, 200, "application/manifest+json", RemoteWebUi.Manifest, cts.Token); return; }
            if (method == "GET" && path == "/icon.svg") { await Reply(stream, 200, "image/svg+xml", RemoteWebUi.Icon, cts.Token); return; }
            const string prefix = "/api/tool/";
            if (method != "POST" || !path.StartsWith(prefix, StringComparison.Ordinal)) { await Reply(stream, 404, "text/plain", "not found", cts.Token); return; }

            if (_bans.TryGetValue(ipKey, out var ban) && ban.Until > DateTime.UtcNow) { await Reply(stream, 429, "application/json", Err("trop d'essais, réessaie dans une minute"), cts.Token); return; }
            headers.TryGetValue("authorization", out var auth);
            string? given = auth is not null && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth[7..].Trim() : null;
            if (!AppConfig.Current.RemoteEnabled || !RemoteAccessPolicy.TokenMatches(AppConfig.Current.RemoteToken, given))
            {
                _bans.AddOrUpdate(ipKey, _ => (1, DateTime.MinValue), (_, v) => v.Fails + 1 >= 5 ? (0, DateTime.UtcNow.AddMinutes(1)) : (v.Fails + 1, DateTime.MinValue));
                await Reply(stream, 401, "application/json", Err("jeton refusé"), cts.Token);
                return;
            }
            _bans.TryRemove(ipKey, out _);
            string tool = path[prefix.Length..];
            if (!RemoteAccessPolicy.IsToolName(tool)) { await Reply(stream, 400, "application/json", Err("nom d'outil invalide"), cts.Token); return; }
            JsonElement args;
            try { using var doc = JsonDocument.Parse(body.Length == 0 ? Encoding.UTF8.GetBytes("{}") : body); args = doc.RootElement.Clone(); }
            catch (JsonException) { await Reply(stream, 400, "application/json", Err("JSON invalide"), cts.Token); return; }
            if (args.ValueKind != JsonValueKind.Object) { await Reply(stream, 400, "application/json", Err("objet JSON attendu"), cts.Token); return; }
            try
            {
                var result = await _bridge.HandleAsync(new ControlRequest(tool, args), cts.Token, remote: true);
                await Reply(stream, 200, "application/json", result.ToJsonString(), cts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or KeyNotFoundException or JsonException or TimeoutException)
            {
                await Reply(stream, 400, "application/json", Err(e.Message), cts.Token);
            }
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or OperationCanceledException) { }
        finally { _slots.Release(); }
    }

    private static string Err(string m) => new JsonObject { ["isError"] = true, ["error"] = m }.ToJsonString();

    private static async Task Reply(Stream s, int code, string type, string body, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        string status = code switch { 200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 404 => "Not Found", 429 => "Too Many Requests", _ => "Error" };
        var head = $"HTTP/1.1 {code} {status}\r\nContent-Type: {type}\r\nContent-Length: {bytes.Length}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\n\r\n";
        await s.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
        await s.WriteAsync(bytes, ct);
        await s.FlushAsync(ct);
    }

    private static async Task<(string? Method, string Path, Dictionary<string, string> Headers, byte[] Body)> ReadRequest(Stream s, CancellationToken ct)
    {
        var buf = new byte[MaxHeaderBytes];
        int len = 0, end = -1;
        while (end < 0)
        {
            if (len == buf.Length) return (null, "", new(), Array.Empty<byte>());
            int n = await s.ReadAsync(buf.AsMemory(len, buf.Length - len), ct);
            if (n == 0) return (null, "", new(), Array.Empty<byte>());
            len += n;
            end = Encoding.ASCII.GetString(buf, 0, len).IndexOf("\r\n\r\n", StringComparison.Ordinal);
        }
        var lines = Encoding.ASCII.GetString(buf, 0, end).Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts.Length < 2) return (null, "", new(), Array.Empty<byte>());
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int k = 1; k < lines.Length; k++) { var l = lines[k]; int i = l.IndexOf(':'); if (i > 0) headers[l[..i].Trim()] = l[(i + 1)..].Trim(); }
        int want = headers.TryGetValue("content-length", out var cl) && int.TryParse(cl, out var v) ? v : 0;
        if (want < 0 || want > MaxBodyBytes) return (null, "", new(), Array.Empty<byte>());
        var body = new byte[want];
        int have = Math.Min(want, len - (end + 4));
        Array.Copy(buf, end + 4, body, 0, have);
        while (have < want) { int n = await s.ReadAsync(body.AsMemory(have, want - have), ct); if (n == 0) break; have += n; }
        string path = parts[1]; int q = path.IndexOf('?'); if (q >= 0) path = path[..q];
        return (parts[0].ToUpperInvariant(), path, headers, body);
    }
}
