using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Transport;
using GivreClone.App.Services.Transport.Socket;
using GivreClone.Engine.Accounts;
using GivreClone.Engine.Proxies;
using GivreClone.Protocol;
using GivreLauncher.Core.Ankama;

namespace GivreClone.App.ViewModels;

// ===================== Télémétrie : archimonstres relayés, instantané des bots pour le portail, vocabulaire d'activité =====================
public partial class AccountsViewModel
{
    // Archimonstre repéré (thread du relais) : on enrichit avec le contexte du compte (perso/serveur/coord), on alerte
    // (toast -> centre de notifications + son), on journalise (panneau Archimonstres), et on relaie au backend (Discord +
    // portail). Best-effort : le relais ne bloque pas l'UI et n'échoue jamais l'alerte locale.
    private void OnArchMonsterSighted(ArchSighting s)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var row = Accounts.FirstOrDefault(a => a.Pid == (int)s.Pid);
            string character = row?.CharacterName ?? "";
            string server = row?.Server ?? "";
            string coords = row?.MapCoords ?? "";
            int breed = row?.BreedId ?? 0;

            ArchMonsterLog.Add(new ArchSightingRow
            {
                Name = s.Name, Gid = s.GenericId, Character = character, Server = server,
                MapId = s.MapId, Coords = coords, Cell = s.Cell, BreedId = breed, Time = DateTime.Now,
            });

            AudioService.PlayUi(AudioService.Notify);
            string ctx = string.IsNullOrEmpty(coords) ? character : $"{character} ({coords})";
            Toasts.Show(Loc.Fmt("L.Arch.Found", s.Name, ctx), success: true, sound: false);   // -> centre de notifications

            // Coord monde "x, y" -> (x, y) pour le backend (portail/Discord). Best-effort, fire-and-forget.
            int? x = null, y = null;
            if (coords.Split(',') is { Length: 2 } pp && int.TryParse(pp[0].Trim(), out var px) && int.TryParse(pp[1].Trim(), out var py)) { x = px; y = py; }
            var pending = new PendingArch(s, character, breed, x, y, !AppConfig.Current.ArchDiscordDm, Environment.TickCount64);
            int sid = _launch.ServerIdOf(s.Pid);
            if (sid > 0) RelayArch(pending, sid);
            else
            {
                // Serveur pas encore identifié (MITM : WhoIs quelques secondes après la 1re carte) : JAMAIS envoyé avec un
                // serveur vide/« - » (rejet silencieux côté serveur, perdu pour la visite) -> en attente de l'identification.
                if (!_archPending.TryGetValue((int)s.Pid, out var list)) _archPending[(int)s.Pid] = list = new();
                list.RemoveAll(p => Environment.TickCount64 - p.Tick > ArchPendingTtlMs);
                if (list.Count < 32) list.Add(pending);
            }
        });
    }

    // Signalements archimonstre en attente du SERVEUR du bot (thread UI uniquement) ; envoyés dès son identification.
    private sealed record PendingArch(ArchSighting S, string Character, int Breed, int? X, int? Y, bool NoDm, long Tick);
    private readonly Dictionary<int, List<PendingArch>> _archPending = new();
    private const long ArchPendingTtlMs = 10 * 60_000;
    // Anti-rafale : une même apparition (serveur, carte, archimonstre) relayée au plus une fois par minute pour TOUTE l'app.
    // Une équipe de 8 bots qui entre sur la carte = 1 trame, pas 8 (le serveur compte des licences, pas des bots, et le
    // seau d'envois du socket est partagé : une rafale le fait fermer).
    private readonly Dictionary<(int Server, long Map, int Gid), long> _archSent = new();

    private void RelayArch(PendingArch p, int serverId)
    {
        // Nom FRANÇAIS canonique (liste de serveurs connus du relais), jamais vide ni « - ».
        string server = _launch.GameNames.CanonicalServerName(serverId) ?? _launch.ServerNameOf(p.S.Pid);
        if (string.IsNullOrWhiteSpace(server) || server == "-") return;
        long now = Environment.TickCount64;
        var key = (serverId, p.S.MapId, p.S.GenericId);
        if (_archSent.TryGetValue(key, out var last) && now - last < 60_000) return;
        _archSent[key] = now;
        if (_archSent.Count > 512)
            foreach (var k in _archSent.Where(kv => now - kv.Value > 60_000).Select(kv => kv.Key).ToList()) _archSent.Remove(k);
        _ = LicenseService.ReportArchMonsterAsync((int)p.S.Pid, server, p.Character, p.Breed, p.S.GenericId, p.S.Name, p.S.MapId,
                                                  p.X, p.Y, p.S.Cell, p.S.GfxId, noDm: p.NoDm);
    }

    // Serveur du bot identifié : envoie ses signalements archimonstre restés en attente (encore dans la fenêtre de 10 min).
    private void FlushArchPending(uint pid)
    {
        int sid = _launch.ServerIdOf(pid);
        if (sid <= 0 || !_archPending.Remove((int)pid, out var list)) return;
        long now = Environment.TickCount64;
        foreach (var p in list)
            if (now - p.Tick <= ArchPendingTtlMs) RelayArch(p, sid);
    }

    // Instantané THREAD-SAFE des bots en jeu (swap de référence atomique) : construit sur le thread UI (poll 2,5 s),
    // lu depuis le thread de fond de la télémetrie via CollectBotTelemetry -> pas d'itération concurrente d'Accounts.
    private volatile IReadOnlyList<BotTelemetry> _botSnapshot = Array.Empty<BotTelemetry>();

    // Appelé par la boucle télémétrie (thread de fond, ~1,5 s + pulses) : on rafraîchit l'instantané À LA DEMANDE
    // sur le thread UI juste avant l'envoi -> la position émise est FRAÎCHE à chaque frame (pas bornée par le poll 2,5 s).
    private IReadOnlyList<BotTelemetry> CollectBotTelemetry()
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess()) RefreshBotSnapshot();
            else Dispatcher.UIThread.Invoke(RefreshBotSnapshot);
        }
        catch { /* dispatcher indisponible (arrêt de l'app) -> on renvoie le dernier instantané connu */ }
        return _botSnapshot;
    }

    private void RefreshBotSnapshot()
    {
        var list = new List<BotTelemetry>();
        foreach (var row in Accounts)
        {
            // Les bots SOCKET sont de vrais persos en jeu : télémétrie + pilotage web (START/STOP_SCRIPT, DISCONNECT_BOT par pid)
            // comme le MITM. Leur pid est synthétique (≥ 0x40000000) : jamais traité comme un process (cf TransportPerf).
            if (!row.Running || row.Pid is not int pid || pid <= 0) continue;
            var w = Stores.World.Get((uint)pid);
            var sc = BotScripts.Get(pid);   // null si aucun script jamais lancé sur ce pid
            string? scriptName = sc?.Name;
            bool? scriptRunning = sc?.Running;
            list.Add(new BotTelemetry(
                pid,
                row.Server is null or "-" ? "" : row.Server,
                row.CharacterName ?? w.CharacterName ?? "",
                row.Level ?? w.Level ?? 0,
                w.MapId != 0 ? w.MapId : (row.MapId ?? 0),
                w.MapX ?? 0, w.MapY ?? 0,
                w.Pods.Cur, w.Pods.Max,
                _launch.Kamas((uint)pid),
                row.BreedId,   // classe du bot (1-20) -> icône de classe côté portail/admin
                ActivityFor((uint)pid),   // état courant (vocabulaire serveur : idle/moving/harvesting/fighting/hdv/banking/…)
                scriptName, scriptRunning,
                row.IsSocketSession ? "socket" : "mitm",       // mode de la SESSION (pas IsSocketMode, la préférence mémorisée) -> pastille « Mes bots »
                row.RunningSince is { } since ? (long)Math.Max(0, (DateTimeOffset.UtcNow - since).TotalSeconds) : -1,   // uptime_s (hors signature)
                LicenseService.HasPermission(LicenseService.Perm.Hdv) ? _launch.HdvStatusOf((uint)pid) : null));       // résumé HDV (panel)
        }
        _botSnapshot = list;
    }

    // Activité envoyée au panel : « hdv » (HDV ouvert ou vente/reprix auto) et « banking » (banque ouverte) quand le bot est en
    // jeu à l'arrêt ; le portail et l'admin affichent déjà ces valeurs (« À l'HDV », « Market »). Combat, trajet, récolte priment.
    private string ActivityFor(uint pid)
    {
        var a = _launch.ActivityOf(pid);
        if (a == BotActivity.InGame)
        {
            if (_launch.HdvAnyOpen(pid) || _launch.HdvAutoActive(pid)) return "hdv";
            if (_launch.StorageOpen(pid)) return "banking";
        }
        return ActivityVocab(a);
    }

    // Mappe l'activité MITM interne vers le vocabulaire du canal « bots » du serveur (valeurs minuscules, mappées côté web).
    private static string ActivityVocab(BotActivity a) => a switch
    {
        BotActivity.Combat => "fighting",
        BotActivity.Gathering => "harvesting",
        BotActivity.Moving => "moving",
        BotActivity.Connecting => "connecting",
        BotActivity.Offline or BotActivity.Disconnected => "idle",   // session perdue : même jeton que hors ligne (pas de valeur nouvelle côté portail)
        _ => "idle",   // InGame (à l'arrêt)
    };
}
