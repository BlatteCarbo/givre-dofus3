using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services.Relay;

public enum RelayLinkState { Off, Connecting, Connected, Reconnecting, Refused }

/// <summary>
/// Client GivreRelay (docs/web-reponse-givrerelay.md, handoff serveur du 04/10) : UN WebSocket vers wss://…/relay/client,
/// authentifié par le jeton de la session de licence. Contrôle en JSON texte, trafic des flux en binaire
/// [stream_id uint32 BE][données]. Ping toutes les 25 s (Cloudflare coupe à 100 s), reconnexion exponentielle 1 → 30 s.
/// Les flux tombent avec le WebSocket : c'est aux bots de se reconnecter (même comportement qu'un proxy qui coupe).
/// </summary>
public sealed class GivreRelayClient
{
    private const int MaxPayload = 64 * 1024 - 4;                      // message binaire ≤ 64 Ko, en-tête compris
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(15);   // gateway : agent_timeout à 10 s

    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, RelayExit> _exits = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<uint, RelayStream> _streams = new();
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<string?>> _pendingOpen = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);              // ClientWebSocket : un seul SendAsync à la fois
    private ClientWebSocket? _ws;
    private Task? _loop;
    private uint _nextId;

    /// <summary>État du lien avec le gateway (affichage).</summary>
    public RelayLinkState State { get; private set; } = RelayLinkState.Off;
    /// <summary>Dernière raison d'un refus global (code de fermeture ou erreur), lisible.</summary>
    public string? LastError { get; private set; }

    public event Action? ExitsChanged;
    /// <summary>Liste complète des partages de la licence reçue (<c>exits</c>) : un partage absent a été supprimé.</summary>
    public event Action? ExitsListed;
    public event Action? StateChanged;

    public IReadOnlyList<RelayExit> Exits => _exits.Values.OrderBy(e => e.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
    public RelayExit? GetExit(string id) => _exits.TryGetValue(id, out var e) ? e : null;
    public int ActiveStreams => _streams.Count;

    /// <summary>Démarre la boucle (idempotent). Elle attend une session de licence, s'y connecte, et suit ses changements.</summary>
    public void EnsureStarted()
    {
        lock (_gate) _loop ??= Task.Run(RunAsync);
    }

    /// <summary>Fin de session de licence : coupe le WebSocket (la boucle attend la session suivante).</summary>
    public void OnSessionEnded()
    {
        try { _ws?.Abort(); } catch { }
    }

    // ------------------------------------------------------------------ connexion

    private async Task RunAsync()
    {
        int delay = 1;
        while (true)
        {
            var (token, uri) = LicenseService.RelayEndpoint();
            if (token is null || uri is null) { SetState(RelayLinkState.Off); await Task.Delay(2000).ConfigureAwait(false); continue; }

            bool wasConnected = false;
            try
            {
                SetState(State == RelayLinkState.Connected ? RelayLinkState.Reconnecting : RelayLinkState.Connecting);
                using var ws = new ClientWebSocket();
                ws.Options.SetRequestHeader("Authorization", "Bearer " + token);
                ws.Options.SetRequestHeader("User-Agent", "GivreClient/" + LicenseService.AppVersion);
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(25);   // ping WebSocket toutes les 25 s
                ws.Options.KeepAliveTimeout = TimeSpan.FromSeconds(20);    // sans pong -> lien déclaré mort
                using (var cto = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                    await ws.ConnectAsync(uri, cto.Token).ConfigureAwait(false);
                _ws = ws;
                wasConnected = true;
                delay = 1;
                LastError = null;
                SetState(RelayLinkState.Connected);
                TransportLog.Line("[relais] connecté au relais Givre");
                await RequestExitsAsync().ConfigureAwait(false);
                // Le gateway ne pousse rien quand un agent est créé, renommé ou supprimé sur le portail : liste redemandée
                // toutes les 60 s (suppression/modification visibles sans relancer l'app).
                using (new Timer(_ => _ = RequestExitsAsync(), null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60)))
                    await ReceiveLoopAsync(ws).ConfigureAwait(false);
                OnClosed(ws.CloseStatus, ws.CloseStatusDescription);
            }
            catch (Exception ex)
            {
                if (!wasConnected) LastError = Loc.Fmt("L.Relay.ConnectFailed", ex.GetType().Name);
            }
            finally
            {
                _ws = null;
                FailAllStreams();
            }

            // Refus explicite du serveur (handoff §4.1) : pas de nouvelle tentative en boucle. On attend le délai propre au code,
            // écourté dès que la session de licence change (reprise de session, nouvelle connexion : nouveau jeton).
            // Sinon (coupure réseau, maintenance Cloudflare, 4500) : reconnexion exponentielle 1 → 30 s.
            if (wasConnected) delay = 1;
            if (State == RelayLinkState.Refused && _refusedWait is int wait)
            {
                await WaitForNewSessionAsync(token, wait).ConfigureAwait(false);
                delay = 1;
            }
            else
            {
                SetState(RelayLinkState.Reconnecting);
                await Task.Delay(TimeSpan.FromSeconds(delay)).ConfigureAwait(false);
                delay = Math.Min(delay * 2, 30);
            }
            _refusedWait = null;
        }
    }

    // Délai avant nouvel essai imposé par le dernier code de fermeture (secondes ; -1 = jusqu'à une nouvelle session).
    private int? _refusedWait;

    private void OnClosed(WebSocketCloseStatus? status, string? description)
    {
        int code = status is { } s ? (int)s : 0;
        (string? why, int? wait) = code switch
        {
            4001 => (Loc.T("L.Relay.Close4001"), 30),     // session invalide : la licence la rouvre, on suit son nouveau jeton
            4002 => (Loc.T("L.Relay.Close4002"), -1),     // licence invalide : rien avant une nouvelle session
            4003 => (Loc.T("L.Relay.Close4003"), 600),    // relais/partage désactivé par l'admin
            4004 => (Loc.T("L.Relay.Close4004"), 30),
            4010 => (Loc.T("L.Relay.Close4010"), 600),
            4404 => (Loc.T("L.Relay.Close4404"), 600),    // chemin inconnu : bug d'intégration, inutile d'insister
            4500 => (Loc.T("L.Relay.Close4500"), (int?)null),   // serveur Givre momentanément injoignable : reconnexion normale
            _ => ((string?)null, (int?)null),
        };
        if (why is not null) LastError = why;
        if (wait is not null) { _refusedWait = wait; SetState(RelayLinkState.Refused); }
        TransportLog.Line($"[relais] déconnecté du relais Givre{(code != 0 ? $" (code {code})" : "")}{(why is not null ? " : " + why : "")}");
    }

    private static async Task WaitForNewSessionAsync(string token, int seconds)
    {
        var until = seconds < 0 ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(1000).ConfigureAwait(false);
            if (LicenseService.RelayEndpoint().Token is { } now && now != token) return;
        }
    }

    private void SetState(RelayLinkState s)
    {
        if (State == s) return;
        State = s;
        try { StateChanged?.Invoke(); } catch { }
    }

    // ------------------------------------------------------------------ réception

    private async Task ReceiveLoopAsync(ClientWebSocket ws)
    {
        var buf = new byte[80 * 1024];
        using var acc = new MemoryStream();
        while (ws.State == WebSocketState.Open)
        {
            var r = await ws.ReceiveAsync(buf, CancellationToken.None).ConfigureAwait(false);
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
            else OnText(msg);
        }
    }

    private void OnBinary(byte[] msg)
    {
        if (msg.Length < 4) return;
        uint id = BinaryPrimitives.ReadUInt32BigEndian(msg);
        // Données d'un flux pas (encore) ouvert : ignorées (course au démarrage, cf. handoff §4.4).
        if (_streams.TryGetValue(id, out var s)) s.Push(msg.AsSpan(4).ToArray());
    }

    private void OnText(byte[] msg)
    {
        try
        {
            using var doc = JsonDocument.Parse(msg);
            var root = doc.RootElement;
            string type = Str(root, "type") ?? "";
            switch (type)
            {
                case "exits":
                    var fresh = new Dictionary<string, RelayExit>(StringComparer.Ordinal);
                    if (root.TryGetProperty("exits", out var arr) && arr.ValueKind == JsonValueKind.Array)
                        foreach (var e in arr.EnumerateArray())
                            if (ParseExit(e, null) is { } ex) fresh[ex.Id] = ex;
                    foreach (var k in _exits.Keys.Except(fresh.Keys).ToList()) _exits.TryRemove(k, out _);
                    foreach (var kv in fresh) _exits[kv.Key] = kv.Value;
                    try { ExitsListed?.Invoke(); } catch { }   // liste COMPLÈTE de la licence : fait foi pour les suppressions
                    RaiseExits();
                    break;
                case "exit_update":
                    if (Str(root, "exit_id") is not { } uid) break;
                    // Partage pas encore connu (créé sur le portail depuis) : l'update n'a pas son libellé -> liste complète.
                    if (GetExit(uid) is null) { _ = RequestExitsAsync(); break; }
                    if (ParseExit(root, GetExit(uid)) is { } upd)
                    {
                        bool wentOffline = GetExit(uid) is { Online: true } && !upd.Online;
                        _exits[uid] = upd;
                        RaiseExits();
                        // Agent débranché : peut-être supprimé ou modifié sur le portail (le gateway le ferme alors) -> liste à jour.
                        if (wentOffline) _ = RequestExitsAsync();
                    }
                    break;
                case "stream_opened":
                    if (U32(root, "stream_id") is uint oid && _pendingOpen.TryRemove(oid, out var tok)) tok.TrySetResult(null);
                    break;
                case "stream_refused":
                    if (U32(root, "stream_id") is uint rid && _pendingOpen.TryRemove(rid, out var tko))
                        tko.TrySetResult(Str(root, "reason") ?? "refused");
                    break;
                case "stream_closed":
                    if (U32(root, "stream_id") is uint cid)
                    {
                        if (_pendingOpen.TryRemove(cid, out var tkc)) tkc.TrySetResult(Str(root, "reason") ?? "closed");
                        if (_streams.TryRemove(cid, out var st)) st.RemoteClosed(Str(root, "reason"));
                    }
                    break;
                case "error":
                    // Avec un code (SESSION_EXPIRED…), ou juste un message juste avant une fermeture (le code arrive alors dans
                    // la trame de fermeture, cf. OnClosed).
                    string code = Str(root, "code") ?? "";
                    LastError = code switch
                    {
                        "SESSION_EXPIRED" => Loc.T("L.Relay.Close4001"),
                        "RELAY_DISABLED" => Loc.T("L.Relay.Close4003"),
                        "RATE_LIMITED" => Loc.T("L.Relay.RateLimited"),
                        _ => Str(root, "message") is { Length: > 0 } m ? m : code,
                    };
                    TransportLog.Line($"[relais] erreur du relais : {LastError}");
                    if (code == "SESSION_EXPIRED") OnSessionEnded();   // reconnexion avec le jeton courant
                    break;
            }
        }
        catch (Exception ex) { TransportLog.Line($"[relais] message illisible ignoré ({ex.GetType().Name})"); }
    }

    private static RelayExit? ParseExit(JsonElement e, RelayExit? prev)
    {
        string? id = Str(e, "exit_id") ?? prev?.Id;
        if (string.IsNullOrEmpty(id)) return null;
        return new RelayExit(
            id,
            Str(e, "label") ?? prev?.Label ?? id,
            Bool(e, "online") ?? prev?.Online ?? false,
            Str(e, "country") ?? prev?.Country,
            Str(e, "scope") ?? prev?.Scope ?? "ankama",
            Str(e, "public_ip") ?? prev?.PublicIp,
            I64(e, "used_bytes") ?? prev?.UsedBytes ?? 0,
            I64(e, "quota_bytes") ?? prev?.QuotaBytes ?? 0,
            e.TryGetProperty("quota_reset", out var qr) ? (qr.ValueKind == JsonValueKind.String ? qr.GetString() : null) : prev?.QuotaReset);
    }

    private void RaiseExits() { try { ExitsChanged?.Invoke(); } catch { } }

    /// <summary>Redemande la liste des partages de la licence (réponse : <c>exits</c>).</summary>
    public async Task RequestExitsAsync()
    {
        try { await SendJsonAsync(w => w.WriteString("type", "list_exits")).ConfigureAwait(false); }
        catch { /* relais déconnecté : la liste sera redemandée à la reconnexion */ }
    }

    // ------------------------------------------------------------------ flux

    /// <summary>Ouvre un flux TCP vers host:port à travers le partage. Renvoie (flux, null) ou (null, raison du refus).</summary>
    public async Task<(RelayStream? Stream, string? Reason)> OpenStreamAsync(string exitId, string host, int port, CancellationToken ct)
    {
        if (_ws is not { State: WebSocketState.Open }) return (null, "relay_offline");
        // Bornes du gateway (sinon invalid_request) : nom de 253 caractères max, port 1-65535.
        if (host.Length is 0 or > 253 || port is < 1 or > 65535) return (null, "invalid_request");
        if (GetExit(exitId) is { } ex)
        {
            if (!ex.Online) return (null, "exit_offline");
            if (ex.QuotaReached) return (null, "quota_exceeded");
        }
        uint id = AllocateId();
        var stream = new RelayStream(this, id);
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingOpen[id] = tcs;
        _streams[id] = stream;   // enregistré AVANT l'ouverture : aucune donnée perdue entre stream_opened et la 1re lecture
        try
        {
            await SendJsonAsync(w =>
            {
                w.WriteString("type", "stream_open");
                w.WriteNumber("stream_id", id);
                w.WriteString("exit_id", exitId);
                w.WriteString("dest_host", host);
                w.WriteNumber("dest_port", port);
            }).ConfigureAwait(false);
            var done = await Task.WhenAny(tcs.Task, Task.Delay(OpenTimeout, ct)).ConfigureAwait(false);
            string? reason = done == tcs.Task ? tcs.Task.Result : "agent_timeout";
            if (reason is null) return (stream, null);
            _streams.TryRemove(id, out _);
            if (done != tcs.Task) await CloseStreamAsync(id).ConfigureAwait(false);
            return (null, reason);
        }
        catch (Exception)
        {
            _streams.TryRemove(id, out _);
            return (null, "relay_offline");
        }
        finally { _pendingOpen.TryRemove(id, out _); }
    }

    private uint AllocateId()
    {
        lock (_gate)
        {
            for (int i = 0; i < 1_000_000; i++)
            {
                _nextId = _nextId >= 0x7FFF_FFFF ? 1 : _nextId + 1;
                if (!_streams.ContainsKey(_nextId) && !_pendingOpen.ContainsKey(_nextId)) return _nextId;
            }
            throw new InvalidOperationException("plus d'identifiant de flux libre");
        }
    }

    internal async Task SendDataAsync(uint id, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var ws = _ws ?? throw new IOException("relais Givre déconnecté");
        for (int off = 0; off < data.Length; off += MaxPayload)
        {
            int n = Math.Min(MaxPayload, data.Length - off);
            var frame = new byte[4 + n];
            BinaryPrimitives.WriteUInt32BigEndian(frame, id);
            data.Slice(off, n).CopyTo(frame.AsMemory(4));
            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
            try { await ws.SendAsync(frame, WebSocketMessageType.Binary, true, ct).ConfigureAwait(false); }
            finally { _sendLock.Release(); }
        }
    }

    internal async Task CloseStreamAsync(uint id)
    {
        _streams.TryRemove(id, out _);
        try { await SendJsonAsync(w => { w.WriteString("type", "stream_close"); w.WriteNumber("stream_id", id); }).ConfigureAwait(false); }
        catch { /* WebSocket déjà tombé : le gateway a fermé le flux */ }
    }

    private void FailAllStreams()
    {
        foreach (var kv in _pendingOpen) kv.Value.TrySetResult("relay_offline");
        _pendingOpen.Clear();
        foreach (var kv in _streams) kv.Value.RemoteClosed("relay_offline");
        _streams.Clear();
    }

    private async Task SendJsonAsync(Action<Utf8JsonWriter> body)
    {
        var ws = _ws ?? throw new IOException("relais Givre déconnecté");
        var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms)) { w.WriteStartObject(); body(w); w.WriteEndObject(); }
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try { await ws.SendAsync(ms.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false); }
        finally { _sendLock.Release(); }
    }

    // ------------------------------------------------------------------ lecture JSON tolérante

    private static string? Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool? Bool(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
    private static long? I64(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;
    private static uint? U32(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetUInt32(out var n) ? n : null;

    /// <summary>Raison de refus (code du gateway) -> texte lisible pour l'utilisateur.</summary>
    public static string DescribeReason(string? reason) => reason switch
    {
        "quota_exceeded" => Loc.T("L.Relay.R.Quota"),
        "exit_offline" => Loc.T("L.Relay.R.Offline"),
        "exit_not_found" => Loc.T("L.Relay.R.NotFound"),
        "scope_denied" => Loc.T("L.Relay.R.Scope"),
        "too_many_streams" => Loc.T("L.Relay.R.TooMany"),
        "global_budget_exceeded" => Loc.T("L.Relay.R.Budget"),
        "licence_invalid" => Loc.T("L.Relay.Close4002"),
        // connect_failed de l'agent : le gateway relaie sa raison telle quelle (timeout, dns_failed, unreachable).
        "connect_failed" or "unreachable" or "timeout" or "dns_failed" => Loc.T("L.Relay.R.ConnectFailed"),
        "exit_paused" => Loc.T("L.Relay.R.Paused"),
        "invalid_request" or "stream_id_in_use" => Loc.T("L.Relay.R.Invalid"),
        "agent_timeout" => Loc.T("L.Relay.R.AgentTimeout"),
        "relay_offline" => Loc.T("L.Relay.R.RelayOffline"),
        null or "" => "",
        _ => reason,
    };
}
