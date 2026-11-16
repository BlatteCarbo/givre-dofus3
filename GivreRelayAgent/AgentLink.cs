using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;

namespace GivreRelayAgent;

public enum LinkState { Connecting, Connected, Reconnecting, Paused, TokenRejected, Replaced, Stopped }

/// <summary>
/// Lien de l'agent avec le gateway GivreRelay (handoff §3) : WebSocket sortant authentifié par le code du partage, contrôle en
/// JSON, données en binaire [stream_id uint32 BE][octets]. Chaque <c>connect</c> devient une connexion TCP de l'ami vers la
/// destination, si le partage l'autorise (<see cref="HostPolicy"/>). Ping 25 s, reconnexion 1 → 30 s.
/// </summary>
public sealed class AgentLink
{
    private const int MaxPayload = 64 * 1024 - 4;
    private static readonly TimeSpan DialTimeout = TimeSpan.FromSeconds(8);    // le gateway répond agent_timeout à 10 s

    private readonly Uri _server;
    private readonly string _token;
    private readonly ConcurrentDictionary<uint, Flow> _flows = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _sessionCts;
    private volatile bool _paused;
    private long _bytesSinceWelcome;

    public LinkState State { get; private set; } = LinkState.Connecting;
    public string? Label { get; private set; }
    public bool ScopeAll { get; private set; }
    public string? PublicIp { get; private set; }
    public long QuotaBytes { get; private set; }
    public long UsedAtWelcome { get; private set; }
    public int MaxStreams { get; private set; } = 30;
    public int ActiveStreams => _flows.Count;
    /// <summary>Consommation estimée : valeur du gateway à l'accueil + octets passés depuis (le gateway fait foi).</summary>
    public long UsedBytes => UsedAtWelcome + Interlocked.Read(ref _bytesSinceWelcome);
    public string? LastError { get; private set; }

    public event Action<string>? Log;
    public event Action? Changed;

    private HostPolicy _policy = new(false, null, null, null);
    private readonly Func<IPAddress, bool> _reachable;

    /// <param name="reachable">Adresses que l'agent peut joindre : Internet seulement (<see cref="HostPolicy.IsPublic"/>).
    /// Remplaçable uniquement par les tests (serveur d'écho local).</param>
    public AgentLink(Uri server, string token, Func<IPAddress, bool>? reachable = null)
    {
        _server = server;
        _token = token;
        _reachable = reachable ?? HostPolicy.IsPublic;
    }

    public bool Paused => _paused;

    /// <summary>Pause : le partage passe hors ligne pour GivreClone (WebSocket fermé, connexions coupées). Reprise : reconnexion.</summary>
    public void SetPaused(bool paused)
    {
        _paused = paused;
        if (paused) { try { _sessionCts?.Cancel(); } catch { } SetState(LinkState.Paused); }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        int delay = 1;
        using var refresh = new PeriodicTimer(TimeSpan.FromMinutes(5));
        _ = Task.Run(async () =>
        {
            try { while (await refresh.WaitForNextTickAsync(ct).ConfigureAwait(false)) await _policy.RefreshAsync(ct).ConfigureAwait(false); }
            catch { /* arrêt */ }
        }, ct);

        while (!ct.IsCancellationRequested)
        {
            if (_paused) { SetState(LinkState.Paused); await Task.Delay(500, ct).ConfigureAwait(false); continue; }
            bool wasConnected = false;
            int closeCode = 0;
            using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _sessionCts = session;
            try
            {
                SetState(State == LinkState.Connected ? LinkState.Reconnecting : LinkState.Connecting);
                using var ws = new ClientWebSocket();
                ws.Options.SetRequestHeader("Authorization", "Bearer " + _token);
                ws.Options.SetRequestHeader("User-Agent", "GivreRelayAgent/" + Program.Version);
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(25);
                ws.Options.KeepAliveTimeout = TimeSpan.FromSeconds(20);
                using (var cto = CancellationTokenSource.CreateLinkedTokenSource(session.Token))
                {
                    cto.CancelAfter(TimeSpan.FromSeconds(15));
                    await ws.ConnectAsync(_server, cto.Token).ConfigureAwait(false);
                }
                _ws = ws;
                wasConnected = true;
                delay = 1;
                LastError = null;
                await ReceiveLoopAsync(ws, session.Token).ConfigureAwait(false);
                closeCode = ws.CloseStatus is { } cs ? (int)cs : 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { /* pause */ }
            catch (Exception ex) { if (!wasConnected) LastError = "connexion impossible (" + ex.GetType().Name + ")"; }
            finally
            {
                _ws = null;
                _sessionCts = null;
                foreach (var f in _flows.Values) f.Close();
                _flows.Clear();
            }

            // Codes de fermeture du gateway (handoff §4.1). wait = délai imposé (s), null = reconnexion normale 1 → 30 s.
            int? wait = null;
            switch (closeCode)
            {
                case 4001:
                    LastError = "code de partage refusé (régénéré ou supprimé sur le portail)";
                    Log?.Invoke(LastError);
                    SetState(LinkState.TokenRejected);
                    return;   // inutile d'insister : il faut un nouveau code
                case 4009:
                    LastError = "ce code vient de se connecter depuis un autre agent GivreRelay";
                    Log?.Invoke(LastError + " : celui-ci s'arrête");
                    SetState(LinkState.Replaced);
                    return;   // sinon les deux agents s'éjectent l'un l'autre en boucle
                case 1012:
                    LastError = null;
                    Log?.Invoke("règles du partage modifiées sur le portail, rechargement…");
                    wait = 0;
                    break;
                case 4002: LastError = "licence du propriétaire du partage invalide"; wait = 60; break;
                case 4003: LastError = "partage ou relais désactivé par l'administrateur"; wait = 60; break;
                case 4004: LastError = "trop de connexions simultanées"; wait = 30; break;
                case 4010: LastError = "budget global du relais épuisé"; wait = 60; break;
                case 4404: LastError = "adresse du relais inconnue (mise à jour de GivreRelay nécessaire ?)"; wait = 300; break;
                case 4500: LastError = "serveur Givre momentanément injoignable"; break;
            }
            if (_paused || ct.IsCancellationRequested) continue;
            if (wasConnected && closeCode != 1012) Log?.Invoke("déconnecté du relais" + (LastError is { } e ? " : " + e : "") + ", reconnexion…");
            if (wasConnected) delay = 1;
            SetState(LinkState.Reconnecting);
            try { await Task.Delay(TimeSpan.FromSeconds(wait ?? delay), ct).ConfigureAwait(false); } catch { break; }
            if (wait is null) delay = Math.Min(delay * 2, 30);
        }
        SetState(LinkState.Stopped);
    }

    private void SetState(LinkState s)
    {
        if (State == s) return;
        State = s;
        try { Changed?.Invoke(); } catch { }
    }

    // ------------------------------------------------------------------ réception

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buf = new byte[80 * 1024];
        using var acc = new MemoryStream();
        while (ws.State == WebSocketState.Open)
        {
            var r = await ws.ReceiveAsync(buf, ct).ConfigureAwait(false);
            if (r.MessageType == WebSocketMessageType.Close)
            {
                // Accuse la fermeture (poignée de fermeture complète) ; le code reçu reste lisible dans CloseStatus.
                try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).ConfigureAwait(false); } catch { }
                return;
            }
            acc.Write(buf, 0, r.Count);
            if (!r.EndOfMessage) continue;
            var msg = acc.ToArray();
            acc.SetLength(0);
            if (r.MessageType == WebSocketMessageType.Binary) OnBinary(msg);
            else OnText(msg, ct);
        }
    }

    private void OnBinary(byte[] msg)
    {
        if (msg.Length < 4) return;
        Interlocked.Add(ref _bytesSinceWelcome, msg.Length);
        uint id = BinaryPrimitives.ReadUInt32BigEndian(msg);
        if (_flows.TryGetValue(id, out var f)) f.Enqueue(msg.AsSpan(4).ToArray());
    }

    private void OnText(byte[] msg, CancellationToken ct)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(msg); } catch { return; }
        using (doc)
        {
            var root = doc.RootElement;
            switch (Str(root, "type"))
            {
                case "welcome":
                    Label = Str(root, "label") ?? Label;
                    ScopeAll = Str(root, "scope") == "all";
                    PublicIp = Str(root, "public_ip") ?? PublicIp;
                    QuotaBytes = I64(root, "quota_bytes") ?? QuotaBytes;
                    UsedAtWelcome = I64(root, "used_bytes") ?? UsedAtWelcome;
                    Interlocked.Exchange(ref _bytesSinceWelcome, 0);
                    MaxStreams = (int)(I64(root, "max_streams") ?? MaxStreams);
                    _policy = HostPolicy.FromWelcome(root);
                    _ = _policy.RefreshAsync(ct);
                    SetState(LinkState.Connected);
                    Log?.Invoke($"connecté : « {Label} », {(ScopeAll ? "tout (web 80/443 + Dofus)" : "Dofus uniquement")}");
                    try { Changed?.Invoke(); } catch { }
                    break;
                case "connect":
                    if (U32(root, "stream_id") is uint id)
                    {
                        // Champs lus ICI : le document JSON est libéré au retour de OnText, avant que la tâche ne démarre.
                        string host = Str(root, "dest_host") ?? "";
                        int port = (int)Math.Clamp(I64(root, "dest_port") ?? 0, 0, 65535);
                        _ = Task.Run(() => OpenAsync(id, host, port, ct), ct);
                    }
                    break;
                case "stream_close":
                    if (U32(root, "stream_id") is uint cid && _flows.TryRemove(cid, out var f)) f.Close();
                    break;
                case "exit_update":   // toutes les ~30 s s'il y a eu du trafic : ces valeurs font foi (le gateway compte les deux sens)
                    PublicIp = Str(root, "public_ip") ?? PublicIp;
                    QuotaBytes = I64(root, "quota_bytes") ?? QuotaBytes;
                    if (I64(root, "used_bytes") is long used) { UsedAtWelcome = used; Interlocked.Exchange(ref _bytesSinceWelcome, 0); }
                    try { Changed?.Invoke(); } catch { }
                    break;
                case "error":
                    LastError = Str(root, "message") ?? Str(root, "code");
                    if (LastError is not null) Log?.Invoke("relais : " + LastError);
                    break;
            }
        }
    }

    // ------------------------------------------------------------------ connexions

    private async Task OpenAsync(uint id, string host, int port, CancellationToken ct)
    {
        string dest = $"{host}:{port}";
        if (!_policy.Allows(host, port)) { await RefuseAsync(id, dest, "scope_denied").ConfigureAwait(false); return; }
        if (_flows.Count >= MaxStreams) { await RefuseAsync(id, dest, "too_many_streams").ConfigureAwait(false); return; }

        // Adresse vérifiée APRÈS résolution : un nom autorisé peut résoudre vers le réseau local de l'ami (localhost…).
        IPAddress[] addrs;
        if (HostPolicy.TryParseIp(host, out var lit)) addrs = new[] { lit };
        else
        {
            try { addrs = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch { await FailAsync(id, dest, "dns_failed").ConfigureAwait(false); return; }
        }
        var targets = addrs.Where(_reachable).OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToArray();
        if (targets.Length == 0) { await RefuseAsync(id, dest, "scope_denied").ConfigureAwait(false); return; }   // réseau local de l'ami : jamais

        TcpClient? tcp = null;
        Exception? last = null;
        foreach (var a in targets)
        {
            var c = new TcpClient(a.AddressFamily) { NoDelay = true };
            try
            {
                using var to = CancellationTokenSource.CreateLinkedTokenSource(ct);
                to.CancelAfter(DialTimeout);
                await c.ConnectAsync(a, port, to.Token).ConfigureAwait(false);
                tcp = c;
                break;
            }
            catch (Exception ex) { last = ex; c.Dispose(); }
        }
        if (tcp is null)
        {
            if (ct.IsCancellationRequested) return;
            // Trois raisons stables seulement (handoff §3.2) : timeout, dns_failed, unreachable.
            await FailAsync(id, dest, last is OperationCanceledException ? "timeout" : "unreachable").ConfigureAwait(false);
            return;
        }

        var flow = new Flow(tcp);
        _flows[id] = flow;
        try { await SendJsonAsync(w => { w.WriteString("type", "connected"); w.WriteNumber("stream_id", id); }).ConfigureAwait(false); }
        catch { _flows.TryRemove(id, out _); flow.Close(); return; }
        try { Changed?.Invoke(); } catch { }

        _ = flow.RunWriterAsync();
        var buf = new byte[MaxPayload];
        try
        {
            var s = tcp.GetStream();
            while (true)
            {
                int n = await s.ReadAsync(buf.AsMemory(0, MaxPayload), ct).ConfigureAwait(false);
                if (n <= 0) break;
                var frame = new byte[4 + n];
                BinaryPrimitives.WriteUInt32BigEndian(frame, id);
                Buffer.BlockCopy(buf, 0, frame, 4, n);
                await SendBinaryAsync(frame).ConfigureAwait(false);
            }
        }
        catch { /* destination ou relais coupé */ }
        finally
        {
            // Fermée côté destination (serveur Dofus) : on prévient le gateway, qui le relaie à GivreClone.
            if (_flows.TryRemove(id, out var f))
            {
                f.Close();
                try { await SendJsonAsync(w => { w.WriteString("type", "stream_close"); w.WriteNumber("stream_id", id); }).ConfigureAwait(false); } catch { }
            }
            try { Changed?.Invoke(); } catch { }
        }
    }

    private async Task FailAsync(uint id, string dest, string reason)
    {
        Log?.Invoke($"{dest} injoignable ({reason})");
        try { await SendJsonAsync(w => { w.WriteString("type", "connect_failed"); w.WriteNumber("stream_id", id); w.WriteString("reason", reason); }).ConfigureAwait(false); }
        catch { }
    }

    private async Task RefuseAsync(uint id, string dest, string reason)
    {
        Log?.Invoke($"refusé : {dest} ({(reason == "scope_denied" ? "hors du partage" : "trop de connexions")})");
        try { await SendJsonAsync(w => { w.WriteString("type", "stream_refused"); w.WriteNumber("stream_id", id); w.WriteString("reason", reason); }).ConfigureAwait(false); }
        catch { }
    }

    // ------------------------------------------------------------------ envoi

    private async Task SendBinaryAsync(byte[] frame)
    {
        var ws = _ws ?? throw new IOException("relais déconnecté");
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try { await ws.SendAsync(frame, WebSocketMessageType.Binary, true, CancellationToken.None).ConfigureAwait(false); }
        finally { _sendLock.Release(); }
        Interlocked.Add(ref _bytesSinceWelcome, frame.Length);
    }

    private async Task SendJsonAsync(Action<Utf8JsonWriter> body)
    {
        var ws = _ws ?? throw new IOException("relais déconnecté");
        var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms)) { w.WriteStartObject(); body(w); w.WriteEndObject(); }
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try { await ws.SendAsync(ms.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false); }
        finally { _sendLock.Release(); }
    }

    private static string? Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static long? I64(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;
    private static uint? U32(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetUInt32(out var n) ? n : null;

    /// <summary>Une connexion TCP de l'ami : file d'écriture propre (un flux lent ne bloque pas les autres).</summary>
    private sealed class Flow
    {
        private readonly TcpClient _tcp;
        private readonly Channel<byte[]> _out = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
        public Flow(TcpClient tcp) => _tcp = tcp;
        public void Enqueue(byte[] data) => _out.Writer.TryWrite(data);

        public async Task RunWriterAsync()
        {
            try
            {
                var s = _tcp.GetStream();
                await foreach (var chunk in _out.Reader.ReadAllAsync().ConfigureAwait(false))
                    await s.WriteAsync(chunk).ConfigureAwait(false);
            }
            catch { Close(); }
        }

        public void Close()
        {
            _out.Writer.TryComplete();
            try { _tcp.Client.Shutdown(SocketShutdown.Both); } catch { }
            try { _tcp.Dispose(); } catch { }
        }
    }
}
