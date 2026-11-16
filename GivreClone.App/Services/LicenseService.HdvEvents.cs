using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.License;

namespace GivreClone.App.Services;

/// <summary>Un événement HDV objet par objet (canal signé "hdv", docs/web-hdv-tracker.md). Kind : list | reprice | remove |
/// sold | buy | price. Prix = TOTAL du lot (le serveur calcule l'unitaire) ; avg / Min = relevé de marché (kind price).
/// SoldAt &gt; 0 = vente faite HORS LIGNE (date réelle de la vente, ms epoch), annoncée à la connexion (docs/web-hdv-ventes-hors-ligne.md).
/// Tax &gt;= 0 = kamas réellement débités par la mise en vente / le reprix (list, reprice ; -1 = inconnue, non envoyée ;
/// docs/web-hdv-taxe.md).</summary>
public sealed record HdvTrackEvent(string Id, long T, string Kind, int Pid, string Server, string Character, long Gid, string Name,
    int TypeId, string? TypeName, bool? Auto, int Qty = 0, long Price = -1, long OldPrice = -1, string? Lot = null,
    long Avg = 0, long[]? Min = null, long SoldAt = 0, long Tax = -1);

// ===================== Canal "hdv" : événements HDV objet par objet (HDV Tracker du portail / de l'admin) =====================
public static partial class LicenseService
{
    private static readonly object _hdvLock = new();
    private static readonly List<HdvTrackEvent> _hdvPending = new();                 // pas encore acceptés par un envoi
    private static readonly List<(HdvTrackEvent Ev, long SentTick)> _hdvRecent = new();   // partis < 60 s : rejoués à la reconnexion
    private static readonly Dictionary<(string Server, long Gid), long> _hdvPriceAt = new();   // relevé de prix : 1 / (serveur, gid) / 15 min
    private static readonly SemaphoreSlim _hdvPulse = new(0, 1);

    private const int HdvBatchMax = 100, HdvBatchEarly = 20, HdvPendingCap = 2000;
    private static readonly TimeSpan HdvFlushEvery = TimeSpan.FromSeconds(10);
    private const long HdvPriceEveryMs = 15 * 60_000, HdvResendWindowMs = 60_000, HdvMaxAgeMs = 23 * 3_600_000;

    /// <summary>Met un événement HDV en file (thread-safe, depuis n'importe quel thread). Ignoré sans permission hdv. Un relevé
    /// de prix n'est gardé qu'une fois par (serveur, gid) toutes les 15 min pour toute l'appli ; un reprix sans changement de prix
    /// est ignoré. L'envoi part dès 20 événements en attente, sinon toutes les 10 s, et à l'arrêt d'un bot.</summary>
    public static void ReportHdvEvent(string kind, int pid, long gid, string name, int typeId, string? typeName, bool? auto,
                                      int qty = 0, long price = -1, long oldPrice = -1, string? lot = null, long avg = 0, long[]? min = null,
                                      long tax = -1)
    {
        if (_client is null || !HasPermission(Perm.Hdv) || gid <= 0) return;
        if (kind == "reprice" && price == oldPrice) return;
        var (server, character) = BotIdentity(pid);
        if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(character)) return;   // champs requis côté serveur
        long now = ServerClock.NowMs();
        lock (_hdvLock)
        {
            if (kind == "price")
            {
                var key = (server, gid);
                if (_hdvPriceAt.TryGetValue(key, out long last) && now - last < HdvPriceEveryMs) return;
                _hdvPriceAt[key] = now;
            }
            var id = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(10)).ToLowerInvariant();
            _hdvPending.Add(new HdvTrackEvent(id, now, kind, pid, Clip(server, 64), Clip(character, 64), gid, Clip(name, 100),
                                              Math.Max(0, typeId), typeName is null ? null : Clip(typeName, 48), auto,
                                              Math.Clamp(qty, 0, 10_000), price, oldPrice, lot is null ? null : Clip(lot, 32), avg, min,
                                              Tax: kind is "list" or "reprice" ? tax : -1));
            if (_hdvPending.Count > HdvPendingCap) _hdvPending.RemoveRange(0, _hdvPending.Count - HdvPendingCap);
            if (_hdvPending.Count >= HdvBatchEarly) PulseHdvEvents();
        }
    }

    /// <summary>Ventes faites HORS LIGNE (liste reçue à la connexion) : un <c>sold</c> par lot, <c>offline</c> + <c>sold_at</c> = date
    /// réelle de la vente. Id DÉTERMINISTE (serveur, perso, objet, quantité, prix, date, rang parmi les doublons exacts) : si le
    /// jeu renvoie la même liste à une connexion suivante, le serveur la déduplique par id au lieu de la compter deux fois.
    /// Renvoie false si l'identité du bot (serveur, perso) n'est pas encore connue : l'appelant réessaie plus tard.</summary>
    public static bool ReportHdvOfflineSales(int pid, IReadOnlyList<(long Gid, string Name, int TypeId, string? TypeName, int Qty, long Price, long SoldAt)> sales)
    {
        if (_client is null || !HasPermission(Perm.Hdv)) return true;   // rien à envoyer : ne pas réessayer
        var (server, character) = BotIdentity(pid);
        if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(character)) return false;
        long now = ServerClock.NowMs();
        var seen = new Dictionary<string, int>();
        lock (_hdvLock)
        {
            foreach (var s in sales)
            {
                if (s.Gid <= 0 || s.Qty <= 0 || s.Price <= 0 || s.SoldAt <= 0) continue;
                string key = $"offline|{server}|{character}|{s.Gid}|{s.Qty}|{s.Price}|{s.SoldAt}";
                int rank = seen.TryGetValue(key, out int r) ? r + 1 : 0;
                seen[key] = rank;
                var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key + "|" + rank));
                var id = Convert.ToHexString(hash, 0, 10).ToLowerInvariant();
                if (_hdvPending.Any(e => e.Id == id)) continue;
                _hdvPending.Add(new HdvTrackEvent(id, now, "sold", pid, Clip(server, 64), Clip(character, 64), s.Gid, Clip(s.Name, 100),
                                                  Math.Max(0, s.TypeId), s.TypeName is null ? null : Clip(s.TypeName, 48), null,
                                                  Math.Clamp(s.Qty, 0, 10_000), s.Price, SoldAt: s.SoldAt));
            }
            if (_hdvPending.Count > HdvPendingCap) _hdvPending.RemoveRange(0, _hdvPending.Count - HdvPendingCap);
        }
        PulseHdvEvents();
        return true;
    }

    /// <summary>Nom du personnage d'un bot tel que publié sur le canal "bots", ou null s'il n'est pas encore connu.</summary>
    public static string? BotCharacterName(int pid) => BotIdentity(pid).Character is { Length: > 0 } c ? c : null;

    /// <summary>Envoi immédiat des événements HDV en attente (arrêt d'un bot, lot de 20 atteint).</summary>
    public static void PulseHdvEvents()
    {
        try { _hdvPulse.Release(); } catch (SemaphoreFullException) { /* déjà réveillé */ }
    }

    // (serveur, personnage) d'un bot, mêmes libellés que le canal "bots" (instantané fourni par l'app).
    private static (string Server, string Character) BotIdentity(int pid)
    {
        var b = _botProvider?.Invoke()?.FirstOrDefault(x => x.Pid == pid);
        return b is null ? ("", "") : (b.Server ?? "", b.Character ?? "");
    }

    private static string Clip(string s, int max) => s.Length > max ? s[..max] : s;

    // Reconnexion WS : les événements partis il y a moins d'une minute sont rejoués (la trame a pu se perdre avec l'ancien
    // socket ; le serveur déduplique par id). Rien n'est écrit sur disque.
    private static void RequeueRecentHdvEvents()
    {
        lock (_hdvLock)
        {
            long now = Environment.TickCount64;
            var again = _hdvRecent.Where(r => now - r.SentTick < HdvResendWindowMs).Select(r => r.Ev).ToList();
            _hdvRecent.Clear();
            var known = new HashSet<string>(_hdvPending.Select(e => e.Id));
            _hdvPending.InsertRange(0, again.Where(e => known.Add(e.Id)));
        }
        PulseHdvEvents();
    }

    // Boucle d'émission du canal "hdv" (même enveloppe signée que "bots"/"pm"/"arch"). Best-effort : un envoi refusé (WS down)
    // laisse les événements en file pour le prochain passage.
    private static void StartHdvEvents(LicenseClient client, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var (sk, pk) = ClientKey();
                var pkHex = Convert.ToHexString(pk).ToLowerInvariant();
                while (!ct.IsCancellationRequested)
                {
                    try { await _hdvPulse.WaitAsync(HdvFlushEvery, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    while (!ct.IsCancellationRequested)
                    {
                        List<HdvTrackEvent> batch;
                        lock (_hdvLock)
                        {
                            if (!HasPermission(Perm.Hdv)) { _hdvPending.Clear(); break; }   // licence sans hdv : aucune trame
                            long limit = ServerClock.NowMs() - HdvMaxAgeMs;                 // le serveur refuse au-delà de 24 h
                            _hdvPending.RemoveAll(e => e.T < limit);
                            if (_hdvPending.Count == 0) break;
                            batch = _hdvPending.Take(HdvBatchMax).ToList();
                        }
                        if (!client.CanUpload) break;
                        var bytes = BuildHdvPayloadBytes(batch, pkHex);
                        var signature = Cryptographic.Ed25519.Signature(bytes, sk, pk);
                        if (!await client.PushUploadAsync("hdv", bytes, signature).ConfigureAwait(false)) break;
                        lock (_hdvLock)
                        {
                            var sent = new HashSet<string>(batch.Select(e => e.Id));
                            _hdvPending.RemoveAll(e => sent.Contains(e.Id));
                            long tick = Environment.TickCount64;
                            _hdvRecent.RemoveAll(r => tick - r.SentTick >= HdvResendWindowMs);
                            _hdvRecent.AddRange(batch.Select(e => (e, tick)));
                            if (_hdvPending.Count < HdvBatchEarly) break;   // reste < 20 : on attend le prochain tick de 10 s
                        }
                    }
                }
            }
            catch (Exception e) { Logger.Warn($"Événements HDV arrêtés : {e.Message}"); }
        });
    }

    // JSON signé : { events:[{id,t,kind,pid,server,character,gid,name[,type_id,type_name,auto][,offline,sold_at],…}], pubkey, nonce, server_time }.
    private static byte[] BuildHdvPayloadBytes(IReadOnlyList<HdvTrackEvent> events, string pubKeyHex)
    {
        using var ms = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteStartArray("events");
            foreach (var e in events)
            {
                w.WriteStartObject();
                w.WriteString("id", e.Id);
                w.WriteNumber("t", e.T);
                w.WriteString("kind", e.Kind);
                if (e.Pid > 0) w.WriteNumber("pid", e.Pid);
                w.WriteString("server", e.Server);
                w.WriteString("character", e.Character);
                w.WriteNumber("gid", e.Gid);
                w.WriteString("name", e.Name);
                if (e.TypeId > 0) w.WriteNumber("type_id", e.TypeId);
                if (e.TypeName is { Length: > 0 } tn) w.WriteString("type_name", tn);
                if (e.Auto is bool a) w.WriteBoolean("auto", a);
                if (e.Kind == "price")
                {
                    if (e.Avg > 0) w.WriteNumber("avg", e.Avg);
                    if (e.Min is { Length: > 0 } mins)
                    {
                        w.WriteStartArray("min");
                        for (int i = 0; i < 3; i++) w.WriteNumberValue(i < mins.Length ? Math.Max(0, mins[i]) : 0);
                        w.WriteEndArray();
                    }
                }
                else
                {
                    if (e.Qty > 0) w.WriteNumber("qty", e.Qty);
                    if (e.Price >= 0) w.WriteNumber("price", e.Price);
                    if (e.Kind == "reprice" && e.OldPrice >= 0) w.WriteNumber("old_price", e.OldPrice);
                    if (e.Kind is "list" or "reprice" && e.Tax >= 0) w.WriteNumber("tax", e.Tax);
                    if (e.Lot is { Length: > 0 } lot) w.WriteString("lot", lot);
                    if (e.SoldAt > 0) { w.WriteBoolean("offline", true); w.WriteNumber("sold_at", e.SoldAt); }
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteString("pubkey", pubKeyHex);
            w.WriteString("nonce", Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
            w.WriteNumber("server_time", ServerClock.NowMs());
            w.WriteEndObject();
        }
        return ms.ToArray();
    }
}
