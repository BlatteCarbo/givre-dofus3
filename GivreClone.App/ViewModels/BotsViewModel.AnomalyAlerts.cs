using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using GivreClone.App.Services;
using GivreClone.App.Services.Eco;
using GivreClone.App.Services.LotM;

namespace GivreClone.App.ViewModels;

// ===================== Notifications Discord des anomalies (webhook du perso, onglet Paramètres) =====================
// « Anomalie ouverte » : événement du serveur (pas l'état reçu à la connexion). « Anomalie farmée fermée » : celle choisie dans
// l'Expédition pour ce bot, fermée par le serveur ou arrivée au bout de son temps. Plusieurs bots d'un même serveur voient la même
// anomalie : un seul message par webhook, serveur, sous-zone et sens pendant 15 min après confirmation HTTP.
public partial class BotsViewModel
{
    private readonly Dictionary<int, int> _farmedAnomaly = new();          // pid -> sous-zone de l'anomalie farmée
    private readonly AnomalyAlertDeduplicator _anomalyAlertSent = new();   // borné, token d'URL haché
    private long _lastAnomalyAlertErrorToast = -60_000;

    private void WireAnomalyAlerts()
        => Stores.Launch.AnomalyChanged += (pid, st, opened) => Dispatcher.UIThread.Post(() => OnAnomalyChanged((int)pid, st, opened));

    private void OnAnomalyChanged(int pid, AnomalyState st, bool opened)
    {
        if (!_byPid.TryGetValue(pid, out var bot)) return;
        var cfg = BotSettingsStore.Get(bot.Row.CharacterName);
        if (opened)
        {
            if (cfg.NotifyAnomalyOpened) SendAnomalyAlert(bot, cfg, st.SubareaId, true, st.RewardRate);
            return;
        }
        if (_farmedAnomaly.TryGetValue(pid, out var sub) && sub == st.SubareaId)
        {
            _farmedAnomaly.Remove(pid);
            if (cfg.NotifyAnomalyClosed) SendAnomalyAlert(bot, cfg, sub, false, 0);
        }
    }

    /// <summary>Tick lent : anomalie farmée qui n'est plus ouverte (ou dont le temps est écoulé) sans message de fermeture reçu.</summary>
    private void CheckFarmedAnomalies()
    {
        foreach (var (pid, sub) in _farmedAnomaly.ToList())
        {
            if (!_byPid.TryGetValue(pid, out var bot) || bot.Pid <= 0) { _farmedAnomaly.Remove(pid); continue; }
            // OpenAnomalies ne contient que l'état connu OUVERT. Une liste vide pendant
            // connexion/reconnexion n'est PAS une preuve de fermeture du serveur.
            // Seule l'expiration observée d'un timer connu confirme une clôture locale ;
            // une fermeture explicite reste traitée par OnAnomalyChanged.
            var observed = Stores.Launch.OpenAnomalies((uint)pid)
                .FirstOrDefault(a => a.State.SubareaId == sub);
            if (observed.State is null || observed.State.ClosingRaw <= 0 || observed.Remaining > 0) continue;
            _farmedAnomaly.Remove(pid);
            var cfg = BotSettingsStore.Get(bot.Row.CharacterName);
            if (cfg.NotifyAnomalyClosed) SendAnomalyAlert(bot, cfg, sub, false, 0);
        }
    }

    /// <summary>Expédition lancée sur une anomalie (ou arrêtée) : mémorise la sous-zone suivie pour ce bot.</summary>
    private void TrackFarmedAnomaly(int pid, int subarea)
    {
        if (subarea > 0) _farmedAnomaly[pid] = subarea; else _farmedAnomaly.Remove(pid);
    }

    private void SendAnomalyAlert(BotRowViewModel bot, BotSettings cfg, int subarea, bool opened, int bonus)
        => _ = SendAnomalyAlertAsync(bot, cfg, subarea, opened, bonus);

    private async Task SendAnomalyAlertAsync(BotRowViewModel bot, BotSettings cfg, int subarea, bool opened, int bonus)
    {
        string url = (cfg.DiscordWebhookUrl ?? "").Trim();
        if (url.Length == 0 || !LicenseService.HasPermission(LicenseService.Perm.PmRelay)) return;
        int serverId = Stores.Launch.ServerIdOf((uint)bot.Pid);
        string? serverIdentity = serverId > 0 ? $"id:{serverId}" : bot.Row.Server;
        var key = AnomalyAlertKey.Create(url, serverIdentity, bot.Pid, subarea, opened);
        var reserved = _anomalyAlertSent.TryBegin(key, Environment.TickCount64);
        if (reserved == AnomalyAlertBegin.Duplicate) return;
        if (reserved == AnomalyAlertBegin.Saturated)
        {
            Logger.Warn("[anomalie] file des alertes Discord saturée (512 entrées maximum).");
            ShowAnomalyAlertError();
            return;
        }

        bool delivered = false;
        try
        {
            string who = string.IsNullOrWhiteSpace(bot.Row.Server) || bot.Row.Server == "-" ? bot.Row.CharacterName ?? "?" : $"{bot.Row.CharacterName} · {bot.Row.Server}";
            string zone = AnomalyZoneName(subarea);
            string msg = opened
                ? Loc.Fmt("L.Notif.AnomalyOpened", who, zone, bonus > 0 ? $" (+{bonus} %)" : "")
                : Loc.Fmt("L.Notif.AnomalyClosed", who, zone);
            delivered = await WebhookService.PostAsync(url, msg);
        }
        catch (Exception ex)
        {
            // Ne jamais logguer l'exception complète : elle peut contenir l'URL privée.
            Logger.Warn($"[anomalie] notification échouée ({ex.GetType().Name}; destination masquée).");
        }
        finally
        {
            _anomalyAlertSent.Complete(key, delivered, Environment.TickCount64);
        }
        if (!delivered) ShowAnomalyAlertError();
    }

    // ---- Alerte de revente : objet du sac dont le prix HDV observé dépasse nettement la moyenne ----
    private readonly Dictionary<(int Pid, long Gid), (long Unit, long At)> _resaleAlerted = new();
    private long _lastResaleCheck;

    private void CheckResaleOpportunities()
    {
        long now = Environment.TickCount64;
        if (now - _lastResaleCheck < 60_000) return;   // prix en cache : une passe par minute suffit
        _lastResaleCheck = now;
        foreach (var bot in _byPid.Values.ToList())
        {
            if (bot.Pid <= 0) continue;
            var cfg = BotSettingsStore.Get(bot.Row.CharacterName);
            if (!cfg.NotifyResale || string.IsNullOrWhiteSpace(cfg.DiscordWebhookUrl)) continue;
            uint pid = (uint)bot.Pid;
            var l = Stores.Launch;
            var cands = l.Inventory.Items(pid).Where(i => i.Position == 63 && i.Qty > 0)
                .Select(i => new ResaleCandidate(i.Gid, l.ItemDisplayName(i.Gid), i.Qty, l.Hdv.MarketFor(pid, i.Gid, 0), l.Hdv.PriceAgeMs(pid, i.Gid), l.Hdv.AverageFor(pid, i.Gid)));
            foreach (var hit in ResaleOpportunity.Find(cands, Math.Max(1, cfg.ResaleMinGainPercent), Math.Max(0, cfg.ResaleMinTotal), l.HdvTaxPercent(pid)).Take(3))
            {
                var key = (bot.Pid, hit.Gid);
                var last = _resaleAlerted.TryGetValue(key, out var v) ? v : (Unit: 0L, At: 0L);
                if (!ResaleOpportunity.ShouldAlert(last.Unit, last.At, hit.MarketUnit, now)) continue;
                _resaleAlerted[key] = (hit.MarketUnit, now);
                _ = SendResaleAlertAsync(bot, cfg, hit);
            }
        }
    }

    private async Task SendResaleAlertAsync(BotRowViewModel bot, BotSettings cfg, ResaleHit hit)
    {
        string url = (cfg.DiscordWebhookUrl ?? "").Trim();
        if (url.Length == 0 || !LicenseService.HasPermission(LicenseService.Perm.PmRelay)) return;
        try
        {
            string who = string.IsNullOrWhiteSpace(bot.Row.Server) || bot.Row.Server == "-" ? bot.Row.CharacterName ?? "?" : $"{bot.Row.CharacterName} · {bot.Row.Server}";
            await WebhookService.PostAsync(url, Loc.Fmt("L.Notif.Resale", who, hit.Name, hit.Quantity, hit.MarketUnit.ToString("N0"), hit.GainPercent.ToString("0"), hit.NetTotal.ToString("N0")));
        }
        catch (Exception ex) { Logger.Warn($"[revente] notification échouée ({ex.GetType().Name}; destination masquée)."); }
    }

    // ---- Suivi de l'abonnement : fin d'abonnement lue des paquets, alertes Discord à 7 / 3 / 1 jour(s) et à l'expiration ----
    private long _lastSubCheck;

    private void CheckSubscriptions()
    {
        long now = Environment.TickCount64;
        if (now - _lastSubCheck < 120_000) return;   // la fin d'abonnement ne change qu'à l'achat : toutes les 2 min suffisent
        _lastSubCheck = now;
        var utc = DateTime.UtcNow;
        foreach (var bot in _byPid.Values.ToList())
        {
            if (bot.Pid <= 0 || string.IsNullOrWhiteSpace(bot.Row.Login)) continue;
            if (Stores.Launch.SubscriptionEndUtc((uint)bot.Pid) is not { } end || end.Year < 2000) continue;
            var cfg = BotSettingsStore.Get(bot.Row.CharacterName);
            (int Threshold, DateTime End)? due = SubscriptionStore.Locked(book =>
            {
                var e = SubscriptionTracker.Observe(book, bot.Row.Login, bot.Row.CharacterName ?? "", end, utc);
                // Sans webhook configuré on ne « consomme » pas le seuil : une alerte ne se perd pas.
                if (string.IsNullOrWhiteSpace(cfg.DiscordWebhookUrl)) return ((int, DateTime)?)null;
                return SubscriptionTracker.AlertDue(e, utc) is { } t ? (t, e.EndUtc) : null;
            });
            SubscriptionStore.Save();
            if (due is { } d) _ = SendSubscriptionAlertAsync(bot, cfg, d.Threshold, d.End);
        }
        // Comptes non connectés (date saisie à la main ou dernière date vue) : alerte via le webhook du personnage mémorisé.
        var live = new HashSet<string>(_byPid.Values.Select(b => b.Row.Login), StringComparer.OrdinalIgnoreCase);
        var offline = SubscriptionStore.Locked(b => b.Entries.Where(e => !live.Contains(e.Login) && !string.IsNullOrWhiteSpace(e.Character)).ToList());
        foreach (var e in offline)
        {
            var cfg = BotSettingsStore.Get(e.Character);
            if (string.IsNullOrWhiteSpace(cfg.DiscordWebhookUrl)) continue;
            int? t = SubscriptionStore.Locked(_ => SubscriptionTracker.AlertDue(e, utc));
            SubscriptionStore.Save();
            if (t is { } th)
            {
                string offText = SubscriptionTracker.AlertText($"{e.Login} · {e.Character}", th, e.EndUtc, utc);
                Services.PhoneAlerts.PhoneAlertHub.Raise("subscription", e.Character ?? e.Login, offText, key: $"sub|{e.Login}|{th}", discord: false);
                _ = SendSubscriptionAlertTextAsync(cfg, offText);
            }
        }
    }

    private async Task SendSubscriptionAlertTextAsync(BotSettings cfg, string text)
    {
        string url = (cfg.DiscordWebhookUrl ?? "").Trim();
        if (url.Length == 0 || !LicenseService.HasPermission(LicenseService.Perm.PmRelay)) return;
        try { await WebhookService.PostAsync(url, text); }
        catch (Exception ex) { Logger.Warn($"[abonnement] notification échouée ({ex.GetType().Name}; destination masquée)."); }
    }

    private async Task SendSubscriptionAlertAsync(BotRowViewModel bot, BotSettings cfg, int threshold, DateTime endUtc)
    {
        string url = (cfg.DiscordWebhookUrl ?? "").Trim();
        if (url.Length == 0 || !LicenseService.HasPermission(LicenseService.Perm.PmRelay)) return;
        try
        {
            string who = string.IsNullOrWhiteSpace(bot.Row.CharacterName) ? bot.Row.Login : $"{bot.Row.Login} · {bot.Row.CharacterName}";
            string subText = SubscriptionTracker.AlertText(who, threshold, endUtc, DateTime.UtcNow);
            Services.PhoneAlerts.PhoneAlertHub.Raise("subscription", who, subText, key: $"sub|{who}|{threshold}", discord: false);
            await WebhookService.PostAsync(url, subText);
        }
        catch (Exception ex) { Logger.Warn($"[abonnement] notification échouée ({ex.GetType().Name}; destination masquée)."); }
    }

    // ---- Alertes de prix : prix observé à l'HDV par un bot sous / au-dessus de la cible -> Discord ----
    private long _lastPriceAlertCheck;

    private void CheckPriceAlerts()
    {
        long now = Environment.TickCount64;
        if (now - _lastPriceAlertCheck < 60_000) return;
        _lastPriceAlertCheck = now;
        var alerts = PriceAlertStore.Locked(b => b.Alerts.ToList());
        if (alerts.Count == 0) return;
        var utc = DateTime.UtcNow;
        foreach (var bot in _byPid.Values.ToList())
        {
            if (bot.Pid <= 0) continue;
            var cfg = BotSettingsStore.Get(bot.Row.CharacterName);
            if (string.IsNullOrWhiteSpace(cfg.DiscordWebhookUrl)) continue;
            foreach (var a in alerts)
            {
                var q = EcoService.PriceFor((uint)bot.Pid, a.Gid);
                if (q is null || !PriceAlerts.IsFreshObservation(q.Source, q.AgeMs)) continue;
                bool notify = PriceAlertStore.Locked(_ =>
                {
                    if (!PriceAlerts.ShouldNotify(a, q.Unit, utc)) return false;
                    PriceAlerts.MarkNotified(a, q.Unit, utc);
                    return true;
                });
                if (!notify) continue;
                PriceAlertStore.Save();
                string who = string.IsNullOrWhiteSpace(bot.Row.CharacterName) ? bot.Row.Login : bot.Row.CharacterName!;
                _ = SendSubscriptionAlertTextAsync(cfg, PriceAlerts.Text(who, a, q.Unit));
            }
        }
    }

    private void ShowAnomalyAlertError()
    {
        long now = Environment.TickCount64;
        if (now - _lastAnomalyAlertErrorToast < 60_000) return;
        _lastAnomalyAlertErrorToast = now;
        Toasts.Show("Notification Discord non envoyée. Vérifiez le webhook et la connexion.", success: false, sound: false);
    }

    private static string AnomalyZoneName(int subarea)
    {
        var l = Stores.Launch;
        try { if (l.GameData?.SubArea(subarea) is { Name.Length: > 0 } s) return string.IsNullOrEmpty(s.Area) ? s.Name : $"{s.Name} ({s.Area})"; } catch { }
        return l.GameNames.SubAreaName(subarea) ?? $"#{subarea}";
    }
}
