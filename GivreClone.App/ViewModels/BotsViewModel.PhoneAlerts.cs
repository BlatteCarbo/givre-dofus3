using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using GivreClone.App.Services;
using GivreClone.App.Services.Combat;
using GivreClone.App.Services.PhoneAlerts;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

// Alertes téléphone : mort, pods pleins, déconnexion, combat perdu, bot figé, archimonstre, fin d'abonnement. Chaque alerte part vers la page du téléphone (long-poll) ET vers le webhook Discord du perso
// (= notification push sur le téléphone, même app fermée). Les anti-répétitions sont dans PhoneAlertHub.
public partial class BotsViewModel
{
    private readonly ArchAlertTracker _archAlerts = new(id => ArchMonsters.IsArch(id) || CapturableMonsters.Is(id));
    private long _lastArchCheck;

    private string WhoIs(uint pid) => RowOfPid(pid) is { } r ? (string.IsNullOrWhiteSpace(r.CharacterName) ? r.Login : r.CharacterName!) : $"bot {pid}";

    private void InitPhoneAlerts()
    {
        PhoneAlertHub.Raised += ev => { if (ev.Discord) Dispatcher.UIThread.Post(() => DiscordForPhoneEvent(ev)); };
        Stores.Launch.PodsUpdated += (pid, current, maximum) =>
        {
            if (maximum > 0 && current * 100L >= maximum * 98L) Dispatcher.UIThread.Post(() => PhoneAlertHub.Raise("pods", WhoIs(pid), $"🎒 {WhoIs(pid)} : pods pleins ({current}/{maximum})"));
        };
        Stores.Launch.FighterKilled += (pid, dead) =>
        {
            if (dead == 0 || dead != Stores.Launch.CharacterId(pid)) return;
            Dispatcher.UIThread.Post(() => PhoneAlertHub.Raise("death", WhoIs(pid), $"💀 {WhoIs(pid)} est mort"));
        };
        Stores.Launch.FightOutcome += (pid, won, lost) =>
        {
            if (lost && !won) Dispatcher.UIThread.Post(() => PhoneAlertHub.Raise("fightlost", WhoIs(pid), $"⚔️ {WhoIs(pid)} a perdu un combat"));
        };
        Stores.Launch.GameLinkLost += (pid, _, _) => Dispatcher.UIThread.Post(() => { _archAlerts.Forget(pid); PhoneAlertHub.Raise("disconnected", WhoIs(pid), $"🔌 {WhoIs(pid)} est déconnecté"); });
    }

    private void DiscordForPhoneEvent(PhoneEvent ev)
    {
        var cfg = BotSettingsStore.Get(ev.Who);
        if (string.IsNullOrWhiteSpace(cfg.DiscordWebhookUrl)) return;
        _ = SendSubscriptionAlertTextAsync(cfg, ev.Text);
    }

    /// <summary>Cherche un archimonstre / monstre à âme capturable sur la carte de chaque bot (toutes les 4 s, hors combat).</summary>
    private void CheckArchMonsters()
    {
        long now = Environment.TickCount64;
        if (now - _lastArchCheck < 4000) return;
        _lastArchCheck = now;
        foreach (var bot in _byPid.Values.ToList())
        {
            if (bot.Pid <= 0 || Stores.Launch.IsInFight((uint)bot.Pid)) continue;
            uint pid = (uint)bot.Pid;
            var world = Stores.World.Get(pid);
            long map = Stores.Launch.CurrentMap(pid) ?? 0;
            if (map == 0) continue;
            var groups = world.Monsters.Where(g => g.Monsters is { Count: > 0 })
                .Select(g => new SeenGroup(g.ActorId, g.Monsters!.Select(m => (m.GenericId, m.Name ?? ("monstre " + m.GenericId))).ToList())).ToList();
            foreach (var a in _archAlerts.Observe(pid, map, groups, now))
            {
                string where = Stores.Launch.MapCoordOf(a.MapId) is { } c ? $"[{c.X},{c.Y}]" : $"carte {a.MapId}";
                string who = WhoIs(pid);
                PhoneAlertHub.Raise("arch", who, $"👑 {a.Names} sur {where} ({who})", key: $"arch|{pid}|{a.MapId}|{a.ActorId}", cooldown: TimeSpan.FromSeconds(30));
            }
        }
    }
}
