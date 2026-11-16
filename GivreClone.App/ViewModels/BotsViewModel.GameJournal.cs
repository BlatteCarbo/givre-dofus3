using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using GivreClone.App.Services;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

// Journal des ÉVÉNEMENTS DE JEU, affiché même sans script (onglet Console > Journal, et console Lua): niveau,
// métier, mort/résurrection, récoltes, pods, combats (début, défis choisis/réussis/ratés, bilan XP/kamas/butin), ventes HDV,
// MP reçus, popup modérateur, invitations, archimonstres. Quand un script tourne, ses propres lignes (récolte, refus auto, modérateur…) ne sont pas doublées.
public partial class BotsViewModel
{
    private readonly Dictionary<int, int> _journalFightNo = new();     // n° de combat de la session (par pid)
    private readonly Dictionary<int, int> _journalHarvestNo = new();   // n° de récolte de la session (par pid)
    private readonly Dictionary<int, long> _journalFightSeq = new();   // dernier bilan de combat affiché (Seq)
    private readonly Dictionary<int, int> _journalPodsBand = new();    // 0 = normal, 1 = presque pleins (≥ 90 %), 2 = pleins

    private void WireGameJournal()
    {
        var l = Stores.Launch;
        void Ui(Action a) => Dispatcher.UIThread.Post(a);
        l.CharacterLeveledUp += (pid, lvl) => Ui(() => Game((int)pid, "perso", Loc.Fmt("L.GameLog.LevelUp", lvl), LuaLogLevel.Success));
        l.JobLeveledUp += (pid, job, lvl) => Ui(() => Game((int)pid, "metier",
            Loc.Fmt("L.GameLog.JobUp", l.GameNames.JobName(job) ?? Loc.Fmt("L.GameLog.JobN", job), lvl), LuaLogLevel.Success));
        l.LifeStatusChanged += (pid, prev, state) => Ui(() => OnLifeJournal((int)pid, prev, state));
        l.ResourceHarvested += (pid, gid, qty, _, name) => Ui(() => OnHarvestJournal((int)pid, qty, name, gid));
        l.PodsUpdated += (pid, w, max) => Ui(() => OnPodsJournal((int)pid, w, max));
        l.FightStarted += pid => Ui(() => OnFightStartJournal((int)pid));
        l.FightEnded += pid => Ui(() => DispatcherTimer.RunOnce(() => OnFightEndJournal((int)pid, retry: true), TimeSpan.FromMilliseconds(400)));
        l.HdvSold += (pid, amount, gid, qty) => Ui(() => Game((int)pid, "hdv",
            Loc.Fmt("L.GameLog.HdvSold", qty, ItemLabel((int)gid), Kamas(amount)), LuaLogLevel.Success));
        l.HdvOfflineSold += (pid, count, total) => Ui(() => Game((int)pid, "hdv", Loc.Fmt("L.GameLog.HdvOffline", count, Kamas(total)), LuaLogLevel.Success));
        l.PrivateMessageReceived += (pid, sender, text, _) => Ui(() => Game((int)pid, "mp", Loc.Fmt("L.GameLog.PmIn", sender, text), LuaLogLevel.Info));
        l.ModeratorPopupReceived += (pid, pop) => Ui(() =>
        {
            if (!ScriptRunning((int)pid))   // script en cours : sa propre alerte modérateur est déjà écrite
                Game((int)pid, "moderation", Loc.Fmt("L.GameLog.Moderator", pop.Author, pop.LockSeconds, pop.Content), LuaLogLevel.Danger);
        });
        l.RequestAutoRefused += (pid, kind, _) => Ui(() =>
        {
            if (!ScriptRunning((int)pid))
                Game((int)pid, "social", Loc.Fmt("L.GameLog.Refused", Loc.T(kind switch { "duel" => "L.GameLog.Kind.Duel", "guild" => "L.GameLog.Kind.Guild", _ => "L.GameLog.Kind.Exchange" })), LuaLogLevel.Info);
        });
        l.PartyInvited += (pid, _, name, _) => Ui(() => Game((int)pid, "groupe", Loc.Fmt("L.GameLog.PartyInvite", name), LuaLogLevel.Info));
        l.ArchMonsterSighted += s => Ui(() => Game((int)s.Pid, "archimonstre", Loc.Fmt("L.GameLog.Arch", s.Name, s.MapId, s.Cell), LuaLogLevel.Warning));
        // Défis : choix au placement, aucun accepté, puis réussite / échec (souvent avant la fin du combat).
        l.ChallengeChosen += (pid, id, bonus) => Ui(() =>
        {
            int kind = l.ChallengeBonusKind(pid);   // bonus confirmé par le serveur : 0 XP, 1 butin, -1 inconnu
            string msg = bonus <= 0 ? Loc.Fmt("L.GameLog.ChallengeChosenNoBonus", ChallengeLabel(id))
                : kind < 0 ? Loc.Fmt("L.GameLog.ChallengeChosenPct", ChallengeLabel(id), bonus)
                : Loc.Fmt("L.GameLog.ChallengeChosen", ChallengeLabel(id), bonus, Loc.T(kind == 1 ? "L.GameLog.BonusLoot" : "L.GameLog.BonusXp"));
            Game((int)pid, "combat", msg, LuaLogLevel.Info);
        });
        l.ChallengeDeclined += (pid, ids) => Ui(() => Game((int)pid, "combat", Loc.Fmt("L.GameLog.ChallengeNone", string.Join(", ", ids.Select(ChallengeLabel))), LuaLogLevel.Info));
        l.ChallengeResolved += (pid, id, ok) => Ui(() => Game((int)pid, "combat", Loc.Fmt(ok ? "L.GameLog.ChallengeOk" : "L.GameLog.ChallengeKo", ChallengeLabel(id)),
            ok ? LuaLogLevel.Success : LuaLogLevel.Warning));
    }

    private static string ChallengeLabel(int id) => Stores.Launch.GameNames.ChallengeName(id) is { Length: > 0 } n ? n : Loc.Fmt("L.Bots.Combat.ChallengeFallback", id);

    private void Game(int pid, string category, string message, LuaLogLevel level)
    {
        if (pid <= 0 || !_byPid.ContainsKey(pid)) return;   // bot pas (encore / plus) affiché
        AppendLuaLine(category, message, level, pid);
    }

    private bool ScriptRunning(int pid) => _luaHosts.TryGetValue(pid, out var h) && h.Running;

    private string ItemLabel(int gid) => Stores.Launch.GameNames.ItemName(gid) ?? Loc.Fmt("L.GameLog.ItemN", gid);

    // 1234567 -> « 1 234 567 » (espaces fines, indépendant de la culture : l'app tourne en culture invariante).
    private static string Kamas(long v) => v.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ');

    private void OnLifeJournal(int pid, int prev, int state)
    {
        if (state == 1) Game(pid, "perso", Loc.T("L.GameLog.Dead"), LuaLogLevel.Danger);
        else if (state == 2) Game(pid, "perso", Loc.T("L.GameLog.Ghost"), LuaLogLevel.Danger);
        else if (prev != 0) Game(pid, "perso", Loc.T("L.GameLog.Revived"), LuaLogLevel.Success);
    }

    private void OnHarvestJournal(int pid, int qty, string name, int gid)
    {
        int n = _journalHarvestNo[pid] = _journalHarvestNo.GetValueOrDefault(pid) + 1;
        if (ScriptRunning(pid)) return;   // la récolte d'un script est déjà écrite par le script (« Récolté ×N … »)
        Game(pid, "recolte", Loc.Fmt("L.GameLog.Harvest", n, qty, string.IsNullOrEmpty(name) ? ItemLabel(gid) : name), LuaLogLevel.Success);
    }

    private void OnPodsJournal(int pid, int weight, int max)
    {
        if (max <= 0) return;
        int pct = (int)(weight * 100L / max);
        int band = weight >= max ? 2 : pct >= 90 ? 1 : 0;
        int was = _journalPodsBand.GetValueOrDefault(pid);
        if (band == 0 && pct < 85) _journalPodsBand[pid] = 0;   // hystérésis : ré-armé une fois nettement redescendu
        if (band <= was) return;
        _journalPodsBand[pid] = band;
        Game(pid, "inventaire", band == 2 ? Loc.Fmt("L.GameLog.PodsFull", Kamas(weight), Kamas(max))
                                          : Loc.Fmt("L.GameLog.PodsHigh", pct, Kamas(weight), Kamas(max)),
             band == 2 ? LuaLogLevel.Danger : LuaLogLevel.Warning);
    }

    private void OnFightStartJournal(int pid)
    {
        int n = _journalFightNo[pid] = _journalFightNo.GetValueOrDefault(pid) + 1;
        Game(pid, "combat", Loc.Fmt("L.GameLog.FightStart", n), LuaLogLevel.Info);
    }

    // Bilan de fin de combat (issue, durée, XP, kamas, butin, défis). Le bilan est publié au décodage du FightEndEvent : s'il
    // n'est pas encore là, un second essai 1 s plus tard.
    private void OnFightEndJournal(int pid, bool retry)
    {
        var r = Stores.Launch.LastFightResult((uint)pid);
        if (r is null || r.Seq == _journalFightSeq.GetValueOrDefault(pid))
        {
            if (retry) DispatcherTimer.RunOnce(() => OnFightEndJournal(pid, retry: false), TimeSpan.FromSeconds(1));
            else Game(pid, "combat", Loc.T("L.GameLog.FightEnd"), LuaLogLevel.Info);
            return;
        }
        _journalFightSeq[pid] = r.Seq;
        long secs = (r.ServerDuration > 0 ? r.ServerDuration : r.DurationMs) / 1000;
        string head = FightSummaryText.Format(r.Outcome, r.Decoded, secs, r.Turns, Loc.T);
        var gains = new List<string>();
        if (r.Xp is long xp && xp > 0) gains.Add(Loc.Fmt("L.GameLog.Xp", Kamas(xp)));
        if (r.Kamas is long k && k > 0) gains.Add(Loc.Fmt("L.GameLog.KamasGain", Kamas(k)));
        Game(pid, "combat", gains.Count > 0 ? head + " : " + string.Join(", ", gains) : head,
             r.Outcome == 2 ? LuaLogLevel.Success : r.Outcome == 0 && r.Decoded ? LuaLogLevel.Warning : LuaLogLevel.Info);
        if (r.Loot.Count > 0)
            Game(pid, "combat", Loc.Fmt("L.GameLog.Loot", string.Join(", ", r.Loot.Select(x => $"{x.Qty}× {ItemLabel(x.Gid)}"))), LuaLogLevel.Success);
        // (défis : déjà annoncés un par un à leur résultat — « Défi réussi / échoué »)
    }

    // Sessions terminées : compteurs oubliés (appelé par le ménage du journal de connexion).
    private void ForgetGameJournal()
    {
        foreach (var d in new[] { _journalFightNo, _journalHarvestNo, _journalPodsBand })
            foreach (var k in d.Keys.Where(p => !_byPid.ContainsKey(p)).ToList()) d.Remove(k);
        foreach (var k in _journalFightSeq.Keys.Where(p => !_byPid.ContainsKey(p)).ToList()) _journalFightSeq.Remove(k);
    }
}
