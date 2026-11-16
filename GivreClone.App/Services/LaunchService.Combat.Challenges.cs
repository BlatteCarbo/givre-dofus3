using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

// ===================== Combat — DÉFIS : choix du défi à valider + règles des défis actifs tenues par l'IA =====================
// Choix : parmi les propositions du serveur, le défi accepté (liste de l'onglet Combat) au meilleur gain moyen (chance de
// réussite × bonus, cf ChallengeCatalog). Pendant le combat, chaque défi ACTIF non encore tranché (pas de ChallengeResultEvent)
// devient une contrainte du tour (ChallengePlan) : déplacement (Statue, Zombie, Nomade), sorts (Versatile, Économe,
// Élémentaire), cibles (Premier, Élitiste, Focus, Cruel…) et case de fin de tour (Hardi, Prudent, Collant…). Un défi raté ou
// réussi ne contraint plus rien. Les cibles désignées viennent du serveur (Challenge.targets) quand il en donne.
public sealed partial class LaunchService
{
    // État de défi du COMBAT EN COURS (vidé au FightStarting) : sorts déjà lancés (Économe), élément verrouillé (Élémentaire),
    // 1er ennemi attaqué (Focus), cible « à vue » (Tueur à vue), niveaux des monstres (Cruel / Ordonné), morts (Conquérant).
    // Écrit par le thread réseau (niveaux, morts) ET le thread du tour (sorts, cibles) -> toujours sous lock(state).
    private sealed class ChallengeFightState
    {
        public readonly HashSet<int> SpellsUsed = new();
        public int? Element;
        public long Focus;
        public readonly GivreClone.Engine.SightTargetPolicy Sight = new();
        public readonly Dictionary<long, int> Levels = new();
        public readonly Dictionary<long, HashSet<long>> Attackers = new();
        public readonly List<(long Id, int Cell, long At)> Deaths = new();
        public bool Abandoned;   // PV passés sous le seuil du profil : défis ignorés jusqu'à la fin du combat
    }
    private readonly ConcurrentDictionary<uint, ChallengeFightState> _challengeFight = new();
    private ChallengeFightState ChallengeState(uint pid) => _challengeFight.GetOrAdd(pid, static _ => new ChallengeFightState());
    private readonly ConcurrentDictionary<uint, int> _challengeBonusKind = new();   // bonus confirmé par le serveur : 0 XP, 1 butin

    /// <summary>Bonus des défis confirmé par le serveur pour ce perso (ChallengeBonusChoiceSelectedEvent) : 0 XP, 1 butin,
    /// -1 inconnu. Lecture pure.</summary>
    public int ChallengeBonusKind(uint pid) => _challengeBonusKind.TryGetValue(pid, out var k) ? k : -1;

    /// <summary>Bonus préféré du profil (mode Socket : c'est GivreClone qui l'annonce au serveur à l'entrée en combat).</summary>
    public bool PrefersChallengeLoot(uint pid) => _combat.TryGetValue(pid, out var p) && p.ChallengeLoot;

    // MITM : ré-annonces du bonus faites CE combat (borne anti-emballement ; vidé au FightStarting).
    private readonly ConcurrentDictionary<uint, int> _bonusReannounced = new();

    /// <summary>MITM : ré-annonce le bonus du profil juste APRÈS celui du client (le serveur garde la dernière annonce et la
    /// confirme par ChallengeBonusChoiceSelectedEvent, que le client reçoit aussi -> son propre choix s'aligne). Le petit délai
    /// garantit que la trame du client est déjà partie vers le serveur avant la nôtre. Revient sur la décision d'août
    /// (« ne pas forcer ») : en socket le 05/10, le serveur a confirmé « butin » sur notre seule annonce.</summary>
    private async System.Threading.Tasks.Task ReannounceChallengeBonusAsync(uint pid, bool loot)
    {
        try
        {
            await System.Threading.Tasks.Task.Delay(80).ConfigureAwait(false);
            if (!LicenseService.HasPermission(LicenseService.Perm.Fight) || !IsInFight(pid)) return;
            var relay = RelayForInject(); if (relay is null) return;
            byte[] pkt = MoveForge.BuildChallengeBonusFramed(loot);
            if (pkt.Length == 0) return;
            bool ok = relay.Inject(pid, pkt); ShowInjected(pid, pkt);
            TransportLog.Line($"[combat] pid {pid} : bonus des challenges ré-annoncé après le client -> {(loot ? "butin" : "XP")} inject={(ok ? "OK" : "ECHEC")}");
        }
        catch { }
    }

    // nameId -> règle des défis génériques : une variante de donjon homonyme (« Hardi » d'un boss) suit la règle générique.
    private Dictionary<int, ChallengeRule>? _challengeRuleByName;
    private readonly object _challengeRuleLock = new();

    /// <summary>Règle d'un défi (générique, ou variante de même nom) ; Unknown si inconnue.</summary>
    public ChallengeRule ChallengeRuleOf(int id)
    {
        var r = ChallengeCatalog.RuleOfGeneric(id);
        if (r != ChallengeRule.Unknown) return r;
        var byName = _challengeRuleByName;
        if (byName is null)
            lock (_challengeRuleLock)
            {
                byName = _challengeRuleByName;
                if (byName is null)
                {
                    byName = new Dictionary<int, ChallengeRule>();
                    foreach (int g in ChallengeCatalog.GenericIds)
                        if (_gameNames.ChallengeNameId(g) is int n && n != 0) byName.TryAdd(n, ChallengeCatalog.RuleOfGeneric(g));
                    if (byName.Count > 0) _challengeRuleByName = byName;   // données du jeu pas encore chargées : on retentera
                }
            }
        return _gameNames.ChallengeNameId(id) is int nid && byName.TryGetValue(nid, out var vr) ? vr : ChallengeRule.Unknown;
    }

    // ---------------------------------------------------------------------------------------------------------- choix du défi

    /// <summary>Défi accepté par le profil ? (surcharges de la liste « Défis acceptés », cf ChallengeCatalog.Accepted)</summary>
    internal static bool ChallengeAccepted(CombatProfile profile, ChallengeRule rule) =>
        ChallengeCatalog.Accepted(rule, profile.ChallengeOn, profile.ChallengeOff);

    private ChallengeContext ChallengeContextFor(uint pid, CombatProfile profile)
    {
        long me = SelfActorId(pid);
        bool team = TeamMembers(pid).Count > 1
                    || (_fighters.TryGetValue(pid, out var fs) && fs.Any(f => f.ActorId > 0 && f.ActorId != me && !IsEnemy(pid, f.ActorId)));
        // Corps-à-corps = aucun sort offensif de la rotation ne porte au-delà de 2 cases (portée de base).
        int reach = profile.Spells
            .Where(s => s.Enabled && s.SpellId > 0 && !s.OnMe && !s.IsSummon && s.TargetKind != SpellTargetKind.Ally)
            .Select(s => _gameNames.SpellCombat(s.SpellId, LearnedSpellLevel(pid, s.SpellId))?.Range ?? 0)
            .DefaultIfEmpty(0).Max();
        bool levels;
        var st = ChallengeState(pid);
        lock (st)
            levels = _fighters.TryGetValue(pid, out var f2) && f2.Where(f => IsEnemy(pid, f.ActorId)).All(f => st.Levels.ContainsKey(f.ActorId))
                     && st.Levels.Count > 0;
        return new ChallengeContext(team, reach <= 2, profile.ChallengeRules, levels);
    }

    /// <summary>Défi à valider parmi les propositions (null = aucune n'est acceptée). Journalisé côté technique.</summary>
    private int? PickChallenge(uint pid, CombatProfile profile, IReadOnlyList<FightPrepReader.ChallengeProposal> props)
    {
        var ctx = ChallengeContextFor(pid, profile);
        var offers = props.Select(p => new ChallengeRewardOffer(p.Id, ChallengeRuleOf(p.Id), p.XpBonus, p.DropBonus)).ToList();
        long who = CharacterId(pid);
        int? pick = ChallengeCatalog.PickPreferred(offers, o => ChallengeAccepted(profile, o.Rule), ctx, profile.ChallengeLoot,
            (rule, prior) => ChallengeOutcomeStore.Adjusted(who, rule, prior));
        string Odds(ChallengeRule r)
        {
            int prior = ChallengeCatalog.Likelihood(r, ctx);
            var (ok, ko) = ChallengeOutcomeStore.Of(who, r);
            return ok + ko == 0 ? $"p{prior}" : $"p{ChallengeOutcomeStore.Adjusted(who, r, prior)} (a priori {prior}, vécu {ok}/{ok + ko})";
        }
        TransportLog.Line($"[combat] pid {pid} : défis proposés [{string.Join(", ", offers.Select(o =>
            $"{o.Id} {o.Rule} XP+{o.XpBonus}% butin+{o.DropBonus}% {Odds(o.Rule)}{(ChallengeAccepted(profile, o.Rule) ? "" : " refusé")}"))}] "
            + $"(équipe={ctx.Team} cac={ctx.Melee} règles={ctx.Rules} niveaux={ctx.LevelsKnown}) -> {(pick is int id ? $"choix {id}" : "aucun accepté")}");
        return pick;
    }

    // ------------------------------------------------------------------------------------------- suivi pendant le combat

    // Niveau d'un monstre (GameFightShowFighter) -> Cruel / Ordonné.
    private void NoteFighterLevel(uint pid, long fighterId, int level)
    {
        var st = ChallengeState(pid);
        lock (st) st.Levels[fighterId] = level;
    }

    // Mort d'un ENNEMI (case connue juste avant son retrait) -> Conquérant (finir sur la case d'un ennemi tué ce tour).
    private void NoteChallengeDeath(uint pid, long deadId, int cell)
    {
        if (cell < 0 || !IsEnemy(pid, deadId)) return;
        var st = ChallengeState(pid);
        lock (st) st.Deaths.Add((deadId, cell, Environment.TickCount64));
    }

    // Sort lancé (moteur OU script) -> Économe (une fois par combat) et Élémentaire (1er élément de dégâts verrouillé).
    private void NoteChallengeSpell(uint pid, int spellId)
    {
        var st = ChallengeState(pid);
        int? el = SpellElement(pid, spellId);
        lock (st) { st.SpellsUsed.Add(spellId); if (st.Element is null && el is int e) st.Element = e; }
    }

    // 1er ennemi attaqué du combat -> Focus (l'achever avant d'en attaquer un autre).
    private void NoteChallengeTarget(uint pid, long actorId)
    {
        if (actorId == 0) return;
        var st = ChallengeState(pid);
        lock (st) if (st.Focus == 0 || !FighterAlive(pid, st.Focus)) st.Focus = actorId;
    }

    private bool FighterAlive(uint pid, long id) => _fighters.TryGetValue(pid, out var fs) && fs.Any(f => f.ActorId == id);

    private void NoteChallengeDamage(uint pid, long source, long target)
    {
        if (source == target || !FighterAlive(pid, source) || IsEnemy(pid, source) || !IsEnemy(pid, target)) return;
        var st = ChallengeState(pid);
        lock (st)
        {
            if (!st.Attackers.TryGetValue(target, out var attackers)) st.Attackers[target] = attackers = new();
            attackers.Add(source);
            if (st.Focus == 0 || !FighterAlive(pid, st.Focus)) st.Focus = target;
        }
    }

    // Élément des dégâts d'un sort (null = pas de dégâts : buff, soin, invocation…).
    private int? SpellElement(uint pid, int spellId)
    {
        int grade = LearnedSpellLevel(pid, spellId);
        if (_gameNames.GetSpellDescriptor(spellId, grade)?.Effects
            .FirstOrDefault(e => e.ActionId is 2822 or 2828 or 2832 or 2890) is { } dynamicEffect)
            return (int)Combat.DamageEstimator.ElementOf(dynamicEffect, SelfCharacteristics(pid));
        return _gameNames.GetSpellDamage(spellId, grade) is { Max: > 0 } d ? d.Element : null;
    }

    private void ForgetChallengeFight(uint pid)
    {
        _challengeFight.TryRemove(pid, out _);
        ForgetStatueFight(pid);
        ForgetEnemyHits(pid);
    }

    // --------------------------------------------------------------------------------------------------- plan du tour

    /// <summary>Contraintes des défis actifs pour UN tour. Construit en début de tour ; les prédicats de case relisent les
    /// positions et les cibles À CHAQUE appel (les combattants bougent ou meurent pendant le tour).</summary>
    internal sealed class ChallengePlan
    {
        public readonly List<ChallengeRule> Rules = new();
        public bool NoMove, OneMove, AllMove, OncePerTurn, OncePerFight, ElementLock;
        public HashSet<long>? Only;    // n'attaquer QUE ces ennemis (null = libre)
        public HashSet<long>? Avoid;   // ne jamais toucher ces ennemis
        public Action? RefreshTargets;
        public readonly List<Func<int, bool>> EndCell = new();
        public bool HasEndCell => EndCell.Count > 0;
        public bool Restricts { get { RefreshTargets?.Invoke(); return Only is not null || Avoid is not null; } }
        public bool Moves => NoMove || OneMove || AllMove || HasEndCell;
        public bool EndCellOk(int c) { foreach (var f in EndCell) if (!f(c)) return false; return true; }
        public bool TargetOk(long id)
        {
            RefreshTargets?.Invoke();
            return (Only is null || Only.Contains(id)) && (Avoid is null || !Avoid.Contains(id));
        }
    }

    internal static ChallengePlan? ApplyChallengeHpThreshold(ChallengePlan? plan, int threshold, int? hp,
        ref bool abandoned, out bool logAbandonment)
    {
        logAbandonment = !abandoned && plan is not null && threshold > 0 && hp is int observed && observed < threshold;
        abandoned |= logAbandonment;
        return abandoned ? null : plan;
    }

    // Le même plan alimente toutes les contraintes du moteur et du snapshot ; null les retire ensemble.
    private ChallengePlan? ApplyChallengeHpThreshold(uint pid, CombatProfile profile, ChallengePlan? plan)
    {
        var st = ChallengeState(pid);
        int? hp = SelfHpPercent(pid);
        bool log;
        lock (st) plan = ApplyChallengeHpThreshold(plan, profile.ChallengeAbandonHp, hp, ref st.Abandoned, out log);
        if (log) TransportLog.Line($"[combat] pid {pid} : défi abandonné : PV {hp}% < seuil {profile.ChallengeAbandonHp}%");
        return plan;
    }

    /// <summary>Plan des défis actifs (null = aucun défi à tenir, ou option « adapter l'IA » coupée).</summary>
    private ChallengePlan? BuildChallengePlan(uint pid, CombatProfile profile, int myCell, Func<int, bool> isWalkStatic,
                                              Func<int, bool> blocksLos, long turnStart)
    {
        if (!profile.ChallengeRules) return null;
        var active = FightChallenges(pid).Where(c => c.Result < 0).Select(c => c.Challenge).ToList();
        if (active.Count == 0) return null;
        if (ChallengeState(pid).Abandoned) return null;
        long me = SelfActorId(pid);
        var plan = new ChallengePlan();
        var st = ChallengeState(pid);

        IReadOnlyList<(int Cell, long ActorId)> Fs() => _fighters.TryGetValue(pid, out var f) ? f : Array.Empty<(int, long)>();
        IEnumerable<(int Cell, long ActorId)> Enemies() => Fs().Where(f => IsEnemy(pid, f.ActorId));
        // Alliés = tout combattant non ennemi sauf moi (coéquipiers ET invocations : « un autre combattant allié »).
        IEnumerable<(int Cell, long ActorId)> Allies() => Fs().Where(f => f.ActorId != me && !IsEnemy(pid, f.ActorId));
        var alive = new HashSet<long>();

        void Only(IEnumerable<long> ids)
        {
            var set = new HashSet<long>(ids.Where(alive.Contains));
            if (set.Count == 0) return;                                        // cible morte / inconnue : pas de contrainte
            if (plan.Only is null) { plan.Only = set; return; }
            var both = new HashSet<long>(plan.Only.Where(set.Contains));
            if (both.Count > 0) plan.Only = both;                              // deux défis incompatibles : le 1er prime
        }
        void Avoid(IEnumerable<long> ids)
        {
            foreach (var id in ids.Where(alive.Contains)) (plan.Avoid ??= new HashSet<long>()).Add(id);
        }
        // Re-read deaths, ownership and server-designated replacements before every target decision.
        void RefreshTargets()
        {
            alive = new HashSet<long>(Enemies().Select(e => e.ActorId));
            plan.Only = null;
            plan.Avoid = null;
            foreach (var ch in FightChallenges(pid).Where(c => c.Result < 0).Select(c => c.Challenge))
            {
                var rule = ChallengeRuleOf(ch.Id);
                if (ChallengeCatalog.Support(rule) != ChallengeSupport.Enforced) continue;
                var designated = ch.Targets.Select(t => t.TargetId).Where(alive.Contains).ToList();
                switch (rule)
                {
                    case ChallengeRule.Premier or ChallengeRule.Elitiste or ChallengeRule.Imprevisible or ChallengeRule.TueurAGages:
                        Only(designated); break;
                    case ChallengeRule.Focus:
                    {
                        long focus; lock (st) focus = st.Focus;
                        Only(designated.Count > 0 ? designated : focus != 0 ? new[] { focus } : Array.Empty<long>());
                        break;
                    }
                    case ChallengeRule.TueurAVue:
                    {
                        long sight;
                        lock (st) sight = st.Sight.Select(_turnEpoch.GetValueOrDefault(pid), SelfFighterCell(pid), Enemies().ToList(), designated);
                        // Preserve all live server-designated targets if a variant supplies several.
                        Only(designated.Count > 0 ? designated : new[] { sight });
                        break;
                    }
                    case ChallengeRule.Cruel or ChallengeRule.Ordonne:
                    {
                        if (designated.Count > 0) { Only(designated); break; }
                        Dictionary<long, int> lv; lock (st) lv = new Dictionary<long, int>(st.Levels);
                        if (alive.Count == 0 || !alive.All(lv.ContainsKey)) break;   // niveaux incomplets : pas de contrainte
                        int pickLv = rule == ChallengeRule.Cruel ? alive.Min(id => lv[id]) : alive.Max(id => lv[id]);
                        Only(alive.Where(id => lv[id] == pickLv));
                        break;
                    }
                    case ChallengeRule.Dernier:
                        if (alive.Any(id => !designated.Contains(id))) Avoid(designated);
                        break;
                    case ChallengeRule.Duel or ChallengeRule.ChacunSonMonstre:
                        // Ennemi déjà attaqué par un AUTRE allié (cible du défi avec ses attaquants) : il lui appartient.
                        Avoid(ch.Targets.Where(t => t.Attackers?.Any(a => a != me) == true).Select(t => t.TargetId));
                        lock (st) Avoid(st.Attackers.Where(t => t.Value.Any(a => a != me)).Select(t => t.Key));
                        break;
                }
            }
        }
        plan.RefreshTargets = RefreshTargets;
        RefreshTargets();
        bool Los(int from, int to)
        {
            var occ = new HashSet<int>(Fs().Where(f => f.Cell != from && f.Cell != to && f.Cell != myCell).Select(f => f.Cell));
            return MapPathfinder.HasLineOfSight(from, to, blocksLos, occ.Contains);
        }
        bool NextToObstacle(int c)
        {
            var (x, y) = MapPathfinder.MapPointOf(c);
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int n = MapPathfinder.CellFromMapPoint(x + dx, y + dy);
                if (n < 0 || !isWalkStatic(n)) return true;                    // bord de carte, trou, mur
            }
            return false;
        }

        foreach (var ch in active)
        {
            var rule = ChallengeRuleOf(ch.Id);
            if (rule == ChallengeRule.Unknown || ChallengeCatalog.Support(rule) != ChallengeSupport.Enforced) continue;
            plan.Rules.Add(rule);
            switch (rule)
            {
                case ChallengeRule.Statue: plan.NoMove = true; break;
                case ChallengeRule.Zombie: plan.OneMove = true; break;
                case ChallengeRule.Nomade: plan.AllMove = true; break;
                case ChallengeRule.Versatile: plan.OncePerTurn = true; break;
                case ChallengeRule.Econome: plan.OncePerFight = true; break;
                case ChallengeRule.Elementaire: plan.ElementLock = true; break;
                case ChallengeRule.Hardi: plan.EndCell.Add(c => Enemies().Any(e => MapPathfinder.CellDistance(c, e.Cell) == 1)); break;
                case ChallengeRule.Prudent: plan.EndCell.Add(c => !Enemies().Any(e => MapPathfinder.CellDistance(c, e.Cell) == 1)); break;
                case ChallengeRule.Collant: plan.EndCell.Add(c => Allies().Any(a => MapPathfinder.CellDistance(c, a.Cell) == 1)); break;
                case ChallengeRule.Misanthrope: plan.EndCell.Add(c => !Allies().Any(a => MapPathfinder.CellDistance(c, a.Cell) == 1)); break;
                case ChallengeRule.MemeLignee: plan.EndCell.Add(c => Allies().Any(a => MapPathfinder.Aligned(c, a.Cell))); break;
                case ChallengeRule.Diagonale: plan.EndCell.Add(c => Allies().Any(a => MapPathfinder.Diagonal(c, a.Cell))); break;
                case ChallengeRule.LigneDeMire: plan.EndCell.Add(c => Enemies().Any(e => Los(c, e.Cell))); break;
                case ChallengeRule.PasVuPasPris: plan.EndCell.Add(c => !Enemies().Any(e => Los(c, e.Cell))); break;
                case ChallengeRule.RaserLesMurs: plan.EndCell.Add(NextToObstacle); break;
                case ChallengeRule.Conquerant:
                    plan.EndCell.Add(c =>
                    {
                        HashSet<int> cells;
                        lock (st) cells = new HashSet<int>(st.Deaths.Where(d => d.At >= turnStart).Select(d => d.Cell));
                        return cells.Count == 0 || cells.Contains(c);   // personne tué ce tour : rien à respecter
                    });
                    break;
            }
        }
        if (plan.Rules.Count == 0) return null;
        TransportLog.Line($"[combat] défis actifs tenus : {string.Join(", ", plan.Rules)}"
            + $"{(plan.NoMove ? " | sans bouger" : "")}{(plan.OneMove ? " | 1 PM exactement" : "")}{(plan.AllMove ? " | tous les PM" : "")}"
            + $"{(plan.OncePerTurn ? " | 1 lancer/sort/tour" : "")}{(plan.OncePerFight ? " | 1 lancer/sort/combat" : "")}{(plan.ElementLock ? " | élément unique" : "")}"
            + $"{(plan.Only is { } o ? $" | cibles {string.Join("/", o)}" : "")}{(plan.Avoid is { } a2 ? $" | intouchables {string.Join("/", a2)}" : "")}"
            + $"{(plan.HasEndCell ? $" | case de fin contrainte ({plan.EndCell.Count})" : "")}");
        return plan;
    }

    /// <summary>Ce sort respecte-t-il les défis du tour ? (Versatile : pas déjà lancé ce tour ; Économe : jamais lancé ce
    /// combat ; Élémentaire : même élément que le 1er sort de dégâts). Les sorts sans dégâts n'ont pas d'élément.</summary>
    private bool ChallengeSpellOk(uint pid, ChallengePlan? plan, int spellId, IReadOnlyDictionary<int, int> castsThisTurn)
    {
        if (plan is null) return true;
        if (plan.OncePerTurn && castsThisTurn.GetValueOrDefault(spellId) > 0) return false;
        var st = ChallengeState(pid);
        if (plan.OncePerFight) lock (st) if (st.SpellsUsed.Contains(spellId)) return false;
        if (plan.ElementLock)
        {
            int? locked; lock (st) locked = st.Element;
            if (locked is int l && SpellElement(pid, spellId) is int e && e != l) return false;
        }
        return true;
    }

    /// <summary>La zone du sort (centrée sur `center`, orientée du lanceur `from` vers `toward`) ne touche-t-elle aucun ennemi
    /// interdit par les défis (cible imposée / ennemi à épargner) ? Sort sans zone = la seule case visée.</summary>
    private bool ChallengeZoneOk(uint pid, ChallengePlan? plan, int spellId, int center, int from, int toward)
    {
        if (plan is null || !plan.Restricts) return true;
        var cells = _gameNames.SpellZone(spellId, LearnedSpellLevel(pid, spellId)) is { Size: > 0 } z
            ? new HashSet<int>(CombatZones.GetCells(z.Shape, center, CombatZones.OrientationTo(from, toward), z.MinSize, z.Size))
            : new HashSet<int> { center };
        if (!_fighters.TryGetValue(pid, out var fs)) return true;
        foreach (var f in fs)
            if (cells.Contains(f.Cell) && IsEnemy(pid, f.ActorId) && !plan.TargetOk(f.ActorId)) return false;
        return true;
    }

    // Case de fin de tour préférée par le STYLE parmi des candidates (cellule -> pas) : Agressif = au plus près d'une cible
    // autorisée, Fuyard = au plus loin des ennemis, Passif = le moins de pas. À égalité, le moins de pas.
    private int PreferEndCell(uint pid, ChallengePlan plan, CombatProfile profile, IReadOnlyList<KeyValuePair<int, int>> cands, Func<int, int>? walkDist = null)
    {
        var enemies = _fighters.TryGetValue(pid, out var fs) ? fs.Where(f => IsEnemy(pid, f.ActorId)).ToList() : new List<(int Cell, long ActorId)>();
        var all = enemies.Select(e => e.Cell).ToList();
        var targets = enemies.Where(e => plan.TargetOk(e.ActorId)).Select(e => e.Cell).ToList();
        if (targets.Count == 0) targets = all;
        int Near(int c, List<int> set) => set.Count == 0 ? 0 : set.Min(e => MapPathfinder.CellDistance(c, e));
        // « Ne jamais approcher » : l'agressif ne choisit plus la case la plus proche, il dépense le moins de PM
        // (comme le passif). Le fuyard continue de s'éloigner.
        IOrderedEnumerable<KeyValuePair<int, int>> ordered = !profile.AllowsApproach && profile.Style == CombatStyle.Aggressive
            ? cands.OrderBy(kv => kv.Value)
            : (profile.Style switch
        {
            // Agressif : d'abord la distance de MARCHE (obstacles contournés), puis le vol d'oiseau pour départager.
            CombatStyle.Aggressive => walkDist is not null ? cands.OrderBy(kv => walkDist(kv.Key)).ThenBy(kv => Near(kv.Key, targets)) : cands.OrderBy(kv => Near(kv.Key, targets)),
            CombatStyle.Fleeing => cands.OrderByDescending(kv => Near(kv.Key, all)),
            _ => cands.OrderBy(kv => kv.Value),
        });
        return ordered.ThenBy(kv => kv.Value).First().Key;
    }
}
