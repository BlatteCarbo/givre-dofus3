using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

// ===================== Résurrection autonome (Phénix) + watchdog anti-stuck =====================
public partial class BotsViewModel
{
    // ======================= RÉSURRECTION AUTONOME (Phénix) =======================
    // Hôtes Lua TRANSITOIRES dédiés à la résurrection auto, pilotée MÊME SANS script en cours. Un script qui tourne gère
    // déjà sa propre résurrection (RunLoop) -> le watcher n'agit QUE si aucun script ne pilote le bot (pas de double).
    private readonly Dictionary<int, GivreLuaHost> _reviveHosts = new();

    private void DriveAutoRevive(int pid)
    {
        bool scriptRunning = _luaHosts.TryGetValue(pid, out var sh) && sh.Running;   // script actif -> il gère la résurrection lui-même
        bool needsRevive = !scriptRunning && LicenseService.CanBot && !Stores.Launch.LevelStartBlocked((uint)pid)   // offre gratuite : cap
                           && Stores.Launch.LifeStatusOf((uint)pid) != 0 && Stores.Launch.AutoReviveEnabled((uint)pid);
        bool hasHost = _reviveHosts.TryGetValue(pid, out var host);
        if (needsRevive)
        {
            if (!hasHost || !host!.Running)   // pas d'hôte, ou l'hôte a fini alors qu'on est encore mort -> (re)lance
            {
                if (hasHost) { try { host!.Dispose(); } catch { /* best-effort */ } }
                var h = new GivreLuaHost((uint)pid, Stores.Launch, Stores.World);
                WireHostConsole(h, pid);
                ApplyMovementSpeed(h, _byPid.TryGetValue(pid, out var vm) ? vm.Row.CharacterName : null);
                _reviveHosts[pid] = h;
                h.StartRevive();
            }
        }
        else if (hasHost) { try { host!.Dispose(); } catch { /* best-effort */ } _reviveHosts.Remove(pid); }
    }

    // Watchdog ANTI-STUCK (V1 sûr) : détecte un bot VIVANT mais qui n'avance plus (script qui boucle sans agir, coincé dans
    // un dialogue, bloqué après une carte). Signal = dernière ACTION forgée (LaunchService, hors DialogLeave). Jugé UNIQUEMENT
    // si un script tourne ET l'activité est « InGame » — Combat/Récolte/Déplacement/Mort ne sont JAMAIS flaggés (états
    // productifs ou légitimement silencieux). Remédiation graduée DOUCE et bornée (1 fois par épisode), JAMAIS de reconnexion.
    private readonly Dictionary<int, int> _stuckStage = new();   // pid -> dernier cran de récupération joué (0 = sain)

    private readonly Dictionary<string, List<long>> _stuckReconnects = new(StringComparer.Ordinal);   // Account.Id -> horodatages (ms) des reconnexions de secours

    /// <summary>Dernier cran du watchdog : reconnexion de secours du compte (script repris par le flux « Reconnecter »), sous garde-fous.</summary>
    private void TryStuckReconnect(int pid, string? name, BotSettings cfg)
    {
        if (!_byPid.TryGetValue(pid, out var bot)) return;
        var row = bot.Row;
        string id = row.Model.Id;
        long now = Environment.TickCount64;
        var history = _stuckReconnects.TryGetValue(id, out var h) ? h : (_stuckReconnects[id] = new());
        history.RemoveAll(t => now - t >= 3_600_000);
        TimeSpan? sinceMod = Stores.Launch.LastModeratorPopup((uint)pid) is { } popup ? DateTime.UtcNow - popup.AtUtc : null;
        var decision = StuckReconnectPolicy.Decide(cfg.AntiStuckReconnect, Stores.Launch.ActivityOf((uint)pid) == BotActivity.Combat,
            sinceMod, bot.PodsPercent, history, now);
        if (decision != StuckReconnectDecision.Allow)
        {
            if (decision != StuckReconnectDecision.Disabled)
                Stores.Launch.TeamLog($"[anti-stuck] {name} : reconnexion de secours non lancée ({decision}).");
            return;
        }
        history.Add(now);
        Stores.Launch.TeamLog($"[anti-stuck] {name} : reconnexion de secours ({history.Count}/{StuckReconnectPolicy.MaxPerHour} cette heure).");
        Toasts.Show(Loc.Fmt("L.AntiStuck.Reconnecting", name ?? pid.ToString()), success: false, sound: false);
        // Même flux que « Reconnecter » (script repris au retour en jeu), sans la confirmation : la session socket confirmée est épinglée.
        int expectSeq = row.IsSocketSession ? System.Threading.Volatile.Read(ref row.SocketSessionSeq) : -1;
        _ = ReconnectCoreAsync(row, expectSeq);
    }

    private void DriveAntiStuck(int pid, string? name)
    {
        var cfg = BotSettingsStore.Get(name);
        if (!cfg.AntiStuckEnabled) { _stuckStage.Remove(pid); return; }
        bool running = _luaHosts.TryGetValue(pid, out var host) && host.Running;
        // sain (compteur remis à zéro) si : pas de script, ou activité ≠ InGame (Combat/Récolte/Déplacement/Mort/connexion)
        // Lot D : une MULE d'équipe (trade:autoAcceptTeam) est inactive PAR CONCEPTION ; le cran 1 (LeaveDialog) couperait
        // un échange en cours -> jamais jugée bloquée tant que le mode mule est actif.
        // Lot G : idem pour un script « écouteur » (registerMessage / onPrivateMessage sans move()) : inactif par conception.
        if (!running || Stores.Launch.ActivityOf((uint)pid) != BotActivity.InGame || Stores.Launch.TradeAutoAcceptTeamOn((uint)pid)
            || host!.IsListenerOnly) { _stuckStage.Remove(pid); return; }
        // Compat un bot tiers: GIVRE_TIMEOUT du script prime sur le réglage du bot le temps du run. ⚠ Unité un bot tiers = TOURS de 6 s,
        // PAS des secondes (MITM.vb timeOutDofusTouch: count += 1 par Sleep(6000), déclenche à count > GIVRE_TIMEOUT; défaut
        // 120 ≈ 12 min) -> ×6. Plancher 60 s : un script ne peut pas rendre le watchdog hyper-agressif. Sans le global : réglage
        // du bot (plancher 30 s, comme l'UI). En long : une valeur géante ne déborde pas.
        long timeoutMs = (host!.ScriptWatchdogSeconds is int snowTicks
            ? Math.Max(60L, snowTicks * 6L)
            : Math.Max(30L, cfg.AntiStuckTimeoutSeconds)) * 1000L;
        long ageMs = Stores.Launch.LastActionAgeMs((uint)pid);
        if (ageMs < timeoutMs) { _stuckStage.Remove(pid); return; }   // a agi récemment -> sain
        int target = (int)Math.Min(3, ageMs / timeoutMs);             // cran 1 à ~timeout, 2 à 2×, 3 (abandon) à 3×
        if (target <= _stuckStage.GetValueOrDefault(pid)) return;     // ce cran est déjà joué pour cet épisode
        _stuckStage[pid] = target;
        switch (target)
        {
            case 1:   // cran DOUX : fermer un dialogue resté ouvert (inoffensif s'il n'y en a pas ; ne compte pas comme progrès)
                Stores.Launch.LeaveDialog((uint)pid);
                Stores.Launch.TeamLog($"[anti-stuck] {name} figé ~{ageMs / 1000}s -> fermeture d'un éventuel dialogue bloquant.");
                AppendLuaLine("watchdog", Loc.Fmt("L.GameLog.Stuck1", ageMs / 1000), LuaLogLevel.Warning, pid);
                break;
            case 2:   // 2e cran OPT-IN : redémarrer le script (Stop+Start de la même source). Sinon on attend l'abandon.
                if (cfg.AntiStuckRestartScript && host!.Restart())
                {
                    Stores.Launch.TeamLog($"[anti-stuck] {name} toujours figé -> redémarrage du script.");
                    AppendLuaLine("watchdog", Loc.T("L.GameLog.Stuck2"), LuaLogLevel.Warning, pid);
                }
                break;
            case 3:   // abandon : on notifie l'utilisateur et on NE touche plus (JAMAIS de reconnexion auto)
                Stores.Launch.TeamLog($"[anti-stuck] {name} figé > {3 * timeoutMs / 1000}s -> abandon, intervention manuelle conseillée.");
                Toasts.Show(Loc.Fmt("L.AntiStuck.Stuck", name ?? pid.ToString()), success: false, sound: true);
                Services.PhoneAlerts.PhoneAlertHub.Raise("stuck", name ?? pid.ToString(), $"⛔ {name ?? pid.ToString()} est figé depuis plus de {3 * timeoutMs / 1000} s : intervention conseillée");
                AppendLuaLine("watchdog", Loc.Fmt("L.GameLog.Stuck3", 3 * timeoutMs / 1000), LuaLogLevel.Danger, pid);
                TryStuckReconnect(pid, name, cfg);
                break;
        }
    }
}
