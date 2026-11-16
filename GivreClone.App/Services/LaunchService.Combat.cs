using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.Transport;
using GivreClone.Engine;
using GivreClone.Engine.Accounts;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using GivreLauncher.Core.Ankama;
using GivreLauncher.Core.Launch;
using GivreLauncher.Core.Proxy;
using GivreLauncher.Core.Transport;
using GivreLauncher.Core.Zaap;
using CoreAccount = GivreLauncher.Core.Accounts.Account;

namespace GivreClone.App.Services;

// ===================== Combat — état partagé : géométrie de tir / d'approche, fin de combat, barrières de séquence, stats, camps, profil =====================
public sealed partial class LaunchService
{
    // Surcharges de PLACEMENT (onglet Placement) appliquées sur la fenêtre de portée effective d'un sort.
    // CastRangeMin>0 => distance de tir MINI imposée (kite par sort) ; CastRangeMax>0 => PLAFOND de portée (se rapprocher).
    // 0 = auto (portée native, boosts de PO inclus). Retourne un couple cohérent (lo <= hi).
    internal static (int lo, int hi) PlacementRange(SpellConfig cfg, int minRange, int effRange)
    {
        int lo = minRange, hi = effRange;
        if (cfg.CastRangeMax > 0) hi = System.Math.Min(hi, cfg.CastRangeMax);
        if (cfg.CastRangeMin > 0) lo = System.Math.Max(lo, cfg.CastRangeMin);
        if (lo > hi) lo = hi;   // fenêtre incohérente (min > max décodé) -> on colle au max, jamais de plage vide négative
        return (lo, hi);
    }

    // Le sort est-il lançable de `from` sur `to` : portee [min,max] + (castInLine -> alignes) + (castInDiagonal -> diagonale)
    // + (castTestLos -> ligne de vue). Les 3 flags sont des CONTRAINTES géométriques posées par-dessus la portée diamant.
    internal static bool CanCast(int from, int to, (int Ap, int MinRange, int Range, bool CastInLine, bool CastInDiagonal, bool CastTestLos) s,
        Func<int, bool> blocksLos, Func<int, bool> occupied)
        => Combat.CastLegality.CanCastGeometry(from, to, s.MinRange, s.Range, s.CastInLine, s.CastInDiagonal, s.CastTestLos, blocksLos, occupied);

    // Case marchable atteignable en <= mp cases d'ou la cible est castable (la plus proche du perso). -1 si aucune.
    internal static int FindCastCell(int from, int target, (int Ap, int MinRange, int Range, bool CastInLine, bool CastInDiagonal, bool CastTestLos) s,
        int mp, Func<int, bool> isWalk, Func<int, bool> blocksLos, Func<int, bool> occupied, out int steps, Func<int, int, bool>? stepAllowed = null,
        Func<int, bool>? positionAllowed = null, Func<int, int>? tieBreak = null)
        => CombatPositionPlanner.FindCastCell(from, mp, isWalk,
            c => CanCast(c, target, s, blocksLos, occupied) && (positionAllowed?.Invoke(c) ?? true), out steps, stepAllowed, tieBreak);

    // Case marchable atteignable ce tour qui me RAPPROCHE le plus de `target`. On mesure la
    // proximite via le CHEMIN MARCHABLE reel (OrthoDistanceField), pas a vol d'oiseau : ainsi un mur/obstacle qui impose un
    // DETOUR (avancer en s'ecartant d'abord) n'empeche plus l'approche -> le bot longe l'ouverture au lieu de se figer et de
    // passer son tour. Parmi mes cases atteignables (BFS orthogonal <= mp), on prend celle de plus petite distance-chemin a
    // la cible (a egalite, le moins de PM). -1 si AUCUNE ne me rapproche (vraiment encercle / ennemi injoignable).
    internal static int FindApproachCell(int from, int target, int mp, Func<int, bool> isWalk, out int steps, Func<int, int, bool>? stepAllowed = null)
    {
        steps = 0;
        var reach = MapPathfinder.OrthoReachable(from, mp, isWalk, stepAllowed);      // mes cases atteignables ce tour (pas exacts)
        if (reach.Count <= 1) return -1;                                 // rien d'atteignable (juste ma case)
        var field = MapPathfinder.OrthoDistanceField(target, isWalk, stepAllowed);    // distance-chemin marchable jusqu'a l'ennemi
        // ma distance-chemin actuelle a l'ennemi ; repli a vol d'oiseau si je suis hors de son secteur marchable (ex : moi
        // sur une case que le champ n'atteint pas) -> on ne bougera alors que si une case atteignable est dans le champ.
        int myFieldDist = field.TryGetValue(from, out var fd0) ? fd0 : MapPathfinder.CellDistance(from, target);
        int best = -1, bestField = myFieldDist, bestSteps = int.MaxValue;
        foreach (var kv in reach)
        {
            int c = kv.Key, st = kv.Value;
            if (c == from || st <= 0) continue;
            int fdist = field.TryGetValue(c, out var v) ? v : int.MaxValue;         // injoignable depuis c -> ecarte
            if (fdist >= myFieldDist) continue;                                     // on ne RETIENT que ce qui rapproche STRICTEMENT (sinon on pietine/oscille lateralement)
            if (fdist < bestField || (fdist == bestField && st < bestSteps)) { bestField = fdist; bestSteps = st; best = c; }
        }
        if (best < 0) return -1;
        steps = bestSteps;
        return best;
    }

    // Ennemis TRIES par preference du mode de ciblage (le 1er = cible ideale). LowestHp : trie par PV CROISSANTS via le
    // lookup hpOf (actorId -> PV%, ex FighterHpPercent) ; PV inconnu -> cible placee en DERNIER ; a PV egal, la plus proche
    // (focus-kill). Sans hpOf (ex simulateur offline), LowestHp retombe sur le plus proche.
    internal static System.Collections.Generic.List<(int Cell, long ActorId)> OrderTargets(TargetMode mode, System.Collections.Generic.List<(int Cell, long ActorId)> enemies, int fromCell, System.Func<long, int?>? hpOf = null) => mode switch
    {
        TargetMode.Farthest => enemies.OrderByDescending(e => MapPathfinder.CellDistance(fromCell, e.Cell)).ToList(),
        TargetMode.LowestHp when hpOf is not null => enemies
            .OrderBy(e => hpOf(e.ActorId) ?? int.MaxValue)                        // PV croissants ; PV inconnu en dernier
            .ThenBy(e => MapPathfinder.CellDistance(fromCell, e.Cell)).ToList(),  // egalite PV -> le plus proche
        _ => enemies.OrderBy(e => MapPathfinder.CellDistance(fromCell, e.Cell)).ToList(),   // Closest (defaut) + LowestHp sans lookup (repli)
    };

    // Cible unique (la plus prioritaire) - conserve pour les appels simples.
    private static (int Cell, long ActorId) SelectTarget(TargetMode mode, System.Collections.Generic.List<(int Cell, long ActorId)> enemies, int fromCell, System.Func<long, int?>? hpOf = null)
        => OrderTargets(mode, enemies, fromCell, hpOf).First();

    // Case marchable atteignable en <= mp cases qui MAXIMISE la distance a l'ennemi le plus proche (kiting, style Fleeing).
    // Ne retient que des cases STRICTEMENT plus eloignees qu'a present ; a ecart egal, le chemin le plus court. -1 si aucune.
    internal static int FindKiteCell(int from, IReadOnlyList<int> enemyCells, int mp, Func<int, bool> isWalk, out int steps, Func<int, int, bool>? stepAllowed = null)
    {
        steps = 0;
        if (enemyCells.Count == 0) return -1;
        int MinEnemyDist(int c) { int d = int.MaxValue; foreach (var e in enemyCells) { int cd = MapPathfinder.CellDistance(c, e); if (cd < d) d = cd; } return d; }
        int mySep = MinEnemyDist(from);
        int best = -1, bestSep = mySep, bestLen = int.MaxValue;
        for (int c = 0; c < MapPathfinder.CellCount; c++)
        {
            if (c == from || !isWalk(c)) continue;
            if (MapPathfinder.CellDistance(from, c) > mp) continue;
            int sep = MinEnemyDist(c);
            if (sep <= mySep) continue;                                             // ne s'eloigne pas -> inutile
            var key = MapPathfinder.KeyMovements(from, c, isWalk, allowDiag: false, stepAllowed);
            if (key.Count == 0) continue;
            int len = MapPathfinder.ExpandPath(key.Select(k => k & 0xFFF).ToList()).Count - 1;
            if (len <= 0 || len > mp) continue;
            if (sep > bestSep || (sep == bestSep && len < bestLen)) { bestSep = sep; bestLen = len; best = c; }
        }
        if (best >= 0) steps = bestLen;
        return best;
    }

    // KITE OPTIMAL : case marchable atteignable en <= mp qui rapproche la distance à l'ennemi le plus proche de la BANDE
    // [min..max] (ni collé ni hors de portée). Score = 0 dans la bande, sinon écart au MILIEU de la bande. On ne retient
    // qu'une case qui AMÉLIORE strictement le score (sinon on ne bouge pas), à égalité le chemin le plus court. Gère les DEUX
    // bornes d'un coup (trop près -> s'écarte ; trop loin -> se rapproche). -1 si déjà dans la bande ou aucune amélioration.
    internal static int FindBandCell(int from, IReadOnlyList<int> enemyCells, int mp, int min, int max, Func<int, bool> isWalk, out int steps, Func<int, int, bool>? stepAllowed = null)
    {
        steps = 0;
        if (enemyCells.Count == 0) return -1;
        if (max < min) max = min;
        int mid = (min + max) / 2;
        int MinEnemyDist(int c) { int d = int.MaxValue; foreach (var e in enemyCells) { int cd = MapPathfinder.CellDistance(c, e); if (cd < d) d = cd; } return d; }
        int Score(int d) => d >= min && d <= max ? 0 : System.Math.Abs(d - mid);   // 0 = dans la bande
        int myScore = Score(MinEnemyDist(from));
        if (myScore == 0) return -1;                                               // déjà dans la bande -> rester
        int best = -1, bestScore = myScore, bestLen = int.MaxValue;
        for (int c = 0; c < MapPathfinder.CellCount; c++)
        {
            if (c == from || !isWalk(c)) continue;
            if (MapPathfinder.CellDistance(from, c) > mp) continue;
            int sc = Score(MinEnemyDist(c));
            if (sc >= myScore) continue;                                           // n'améliore pas -> inutile
            var key = MapPathfinder.KeyMovements(from, c, isWalk, allowDiag: false, stepAllowed);
            if (key.Count == 0) continue;
            int len = MapPathfinder.ExpandPath(key.Select(k => k & 0xFFF).ToList()).Count - 1;
            if (len <= 0 || len > mp) continue;
            if (sc < bestScore || (sc == bestScore && len < bestLen)) { bestScore = sc; bestLen = len; best = c; }
        }
        if (best >= 0) steps = bestLen;
        return best;
    }

    /// <summary>fight_id du combat EN COURS de ce bot (0 = aucun). Capté au FightStartingEvent ; consommé par l'auto-join
    /// d'équipe (le follower rejoint le fight_id du chef).</summary>
    public long CurrentFightId(uint pid) => _fightId.TryGetValue(pid, out var f) ? f : 0;

    private readonly Combat.TeamIntentBoard _teamIntents = new();
    private readonly ConcurrentDictionary<uint, int> _teamIntentRound = new();
    private readonly ConcurrentDictionary<uint, string> _teamIntentFightKeys = new();

    private (string Key, int Allies)? TeamIntentFight(uint pid)
    {
        long fightId = CurrentFightId(pid), self = SelfActorId(pid);
        if (fightId == 0 || self == 0 || !IsInFight(pid)) return null;
        if (!_fighters.TryGetValue(pid, out var roster)) return null;
        int allies = _fightId.Where(p => p.Value == fightId && IsInFight(p.Key))
            .Select(p => CharacterId(p.Key))
            .Concat(_controlledHistory.Where(p => _fightId.GetValueOrDefault(p.Key.Pid) == fightId
                && IsInFight(p.Key.Pid) && p.Value.Fighter.MasterId == CharacterId(p.Key.Pid))
                .Select(p => p.Key.Actor))
            .Where(actor => actor != 0 && (actor == self || !IsEnemy(pid, actor))
                && roster.Any(f => f.ActorId == actor)).Distinct().Count();
        if (allies < 2) return null;
        string key = $"fight:{fightId}";
        _teamIntentFightKeys[pid] = key;
        return (key, allies);
    }

    /// <summary>Other controlled allies' current-round decisions; empty outside a shared fight.</summary>
    public Combat.TeamIntentView ReadTeamView(uint pid)
        => TeamIntentFight(pid) is { } fight
            ? _teamIntents.Read(fight.Key, Math.Max(1, _teamIntentRound.GetValueOrDefault(pid, _fightTurnNo.GetValueOrDefault(pid))), SelfActorId(pid))
            : Combat.TeamIntentView.Empty;

    /// <summary>Planner hook for this account or its currently server-controlled actor.</summary>
    public void PublishTeamIntent(uint pid, Combat.TeamIntent intent)
    {
        if (intent.SelfActorId != SelfActorId(pid) || TeamIntentFight(pid) is not { } fight) return;
        if (EngineControlled(pid) is { } controlled && (!HasControlledTurn(pid)
            || !_controlledCombat.TryGetValue(pid, out var current) || !ReferenceEquals(current, controlled))) return;
        _teamIntents.Publish(fight.Key, intent);
    }

    private sealed class TeamIntentPublication(Action publish) : IDisposable
    {
        public void Dispose() => publish();
    }

    /// <summary>Nettoyage de FIN DE COMBAT, IDEMPOTENT : reset _inFight, purge combattants/stats/cases bloquees, notifie
    /// l'UI et leve FightEnded. Declenche par FightEndEvent MAIS AUSSI par le 1er jss (carte de jeu de role) qui suit le
    /// combat : selon la MAJ Dofus le FightEndEvent peut ne pas etre emis/mappe (codes shuffles) et le bot resterait
    /// bloque « en combat » (boucle de recolte en pause a l'infini). Retourne true si on ETAIT en combat (nettoyage fait).</summary>
    private bool EndFight(uint pid, bool preserveTacticalTransition = false)
    {
        NativeTurboRuntime.SetCombatActive((int)pid, false);
        if (!preserveTacticalTransition) InvalidateCombatMap(pid);
        else lock (_combatMapGate) _combatMapTickets.TryRemove(pid, out _);
        if (!(_inFight.TryGetValue(pid, out var was) && was)) return false;
        SeedLifeAfterFight(pid);   // Lot F : graine des PV hors combat AVANT la purge de _fighters (SelfActorId en dépend)
        // Lot C2 : fin de combat SANS FightEndEvent décodé (repli jss) -> bilan « non décodé » publié quand même (issue -1),
        // AVANT que IsInFight ne retombe (fight() côté script rend la main sur cette condition et relit lastFightResult).
        if (!_fightResultStored.TryRemove(pid, out _))
            PublishFightResult(pid, new FightEndSummary(false, -1, 0, null, null, Array.Empty<(int, int)>()), decoded: false);
        _postFightMapPending[pid] = true;
        lock (_combatMapGate)
        {
            _inFight[pid] = false;
            _combatMapTickets.TryRemove(pid, out _);
        }
        _fightEndAt[pid] = Environment.TickCount64;   // horodate la fin -> fenêtre de grâce anti-spam "hors du world-graph" (instance de combat)
        _fightId.TryRemove(pid, out _);   // fin de combat -> plus de fight_id à rejoindre
        FightEnded?.Invoke(pid);
        PurgeFightState(pid);
        FighterStatsUpdated?.Invoke(pid);
        MapActorsDecoded?.Invoke(pid, Array.Empty<MapActor>());   // vide les combattants (le jss du jeu de role repeuple)
        return true;
    }

    private readonly ConcurrentDictionary<uint, bool> _postFightMapPending = new();
    private readonly ConcurrentDictionary<uint, FightVisibilityReducer> _fighterVisibility = new();
    private readonly ConcurrentDictionary<uint, FightMarks> _fightMarks = new();

    /// <summary>Cases des marques au sol observées CE combat (registre remis à zéro à chaque début de combat). Null = couverture
    /// inconnue (pas de registre, ou une marque dont les cases n'ont pas été décodées) : le planificateur interdit alors tout
    /// déplacement forcé (poussée, attirance, échange) plutôt que de supposer le sol libre.</summary>
    private System.Collections.Immutable.ImmutableHashSet<int>? ForcedMovementUnsafeCells(uint pid)
    {
        if (!_fightMarks.TryGetValue(pid, out var registry)) return null;
        var marks = registry.Snapshot.Values;
        if (marks.Any(m => m.Cells.IsDefaultOrEmpty)) return null;
        return System.Collections.Immutable.ImmutableHashSet.CreateRange(marks.SelectMany(m => m.Cells));
    }

    /// <summary>Cases portant une marque au sol (piège, glyphe) observée CE combat. Contrairement à
    /// <see cref="ForcedMovementUnsafeCells"/>, une couverture inconnue rend ici un ensemble VIDE : interdire toutes les
    /// cases de lancer sur une marque mal décodée empêcherait le tour, alors qu'ici le pire cas est un refus serveur
    /// déjà géré. Sert à ne plus viser une case que le serveur refuse (message 229 « Il y a un piège sur cette case »).</summary>
    private System.Collections.Generic.HashSet<int> MarkedCells(uint pid)
        => _fightMarks.TryGetValue(pid, out var registry)
            ? registry.Snapshot.Values.SelectMany(m => m.Cells.IsDefaultOrEmpty
                ? System.Array.Empty<int>() : (System.Collections.Generic.IEnumerable<int>)m.Cells).ToHashSet()
            : new System.Collections.Generic.HashSet<int>();

    public bool IsPostFightMapPending(uint pid) => _postFightMapPending.GetValueOrDefault(pid);

    /// <summary>Portails actifs au sol observés ce combat (FightMarks). Sert au calcul de portée et de redirection
    /// de sorts Éliotrope (CanHitThroughPortals) et aux synergies multi-classes (Iop+Élio).</summary>
    public System.Collections.Generic.IReadOnlyList<Combat.ClassMechanics.Portal> Portals(uint pid, bool includeInactive = false)
    {
        if (!_fightMarks.TryGetValue(pid, out var registry)) return System.Array.Empty<Combat.ClassMechanics.Portal>();
        var list = new System.Collections.Generic.List<Combat.ClassMechanics.Portal>();
        _fighterBreeds.TryGetValue(pid, out var breeds);
        foreach (var m in registry.Snapshot.Values)
        {
            if (m.Cells.IsDefaultOrEmpty || !includeInactive && !m.Active) continue;
            // La classe du perso joue n'arrive pas toujours par FightFighterShowEvent : sans ce repli, les portails
            // de Eliobot (marques type 3, 1 case) n'etaient jamais reconnus et aucun tir ne passait par eux.
            bool isElioAuthor = breeds is not null && breeds.TryGetValue(m.Author, out int br) && br == Combat.ClassMechanics.EliotropeBreedId
                || m.Author == CharacterId(pid) && SelfCharacterInfo(pid)?.BreedIndex is int bi
                    && ClassIconProvider.BreedId(bi) == Combat.ClassMechanics.EliotropeBreedId;
            // Type 4 is also emitted for Huppermage runes in observed client captures.
            // A mark's kind alone is not evidence of a usable Eliotrope portal.
            // Restrict portal routing to one-cell marks owned by a verified Eliotrope;
            // older captures may encode these marks as type 3 or 4.
            if (isElioAuthor && m.Cells.Length == 1 && m.Type is 3 or 4)
            {
                foreach (int c in m.Cells)
                    list.Add(new Combat.ClassMechanics.Portal(m.Id, c, m.Author, m.Active));
            }
        }
        return list;
    }

    // Purge de l'état du combat TERMINÉ (roster, cases, alliés/ennemis, tour, hooks Lua, stats des combattants), SANS événement.
    // Partagée par EndFight (après FightEnded : les abonnés lisent encore le roster) et ForgetSession (session de jeu perdue en
    // plein combat : jamais de FightEndEvent -> sans ça le combat suivant hériterait des alliés/ennemis de celui-ci).
    private void PurgeFightState(uint pid)
    {
        ClearFighterStates(pid);
        if (_teamIntentFightKeys.TryRemove(pid, out var intentKey)) _teamIntents.Clear(intentKey);
        _teamIntentRound.TryRemove(pid, out _);
        _fighterVisibility.TryRemove(pid, out _);
        _fightMarks.TryRemove(pid, out _);
        _fighterBreeds.TryRemove(pid, out _);
        ResetTurboMetrics(pid);
        ResetControlledCombat(pid);
        ForgetSpellModifiers(pid);   // fin de combat : les buffs de portée du combat suivant seront reposés par ses propres lancers
        _fighters.TryRemove(pid, out _);
        _prefightRan.TryRemove(pid, out _);
        _challengeGate.TryRemove(pid, out _);
        // Lot C2 : état défi du combat terminé (les RÉSULTATS de défis restent lisibles jusqu'au prochain FightStarting).
        _challengeProps.TryRemove(pid, out _); _challengeRequested.TryRemove(pid, out _); _challengeChosen.TryRemove(pid, out _); _placementOver.TryRemove(pid, out _);
        _blockedCells.TryRemove(pid, out _);   // fin de combat -> oublie les cases refusées
        _fightObstacles.TryRemove(pid, out _); // fin de combat -> oublie les obstacles appris (le prochain combat en a d'autres)
        _allies.TryRemove(pid, out _);         // fin de combat -> oublie les alliés (le prochain combat les redéclare)
        _summonOwners.TryRemove(pid, out _);   // fin de combat -> oublie les propriétaires des invocations
        _confirmedEnemies.TryRemove(pid, out _); // fin de combat -> oublie les ennemis confirmés (le prochain combat en a d'autres)
        _fightMonsters.TryRemove(pid, out _);
        _treasureEncounters.TryRemove(pid, out _);
        _treasureAttacked.TryRemove(pid, out _);
        _fighterTeam.TryRemove(pid, out _);    // fin de combat -> oublie les équipes (le prochain combat les redéclare)
        _kickedThisFight.TryRemove(pid, out _);  // fin de combat -> oublie les intrus déjà expulsés (anti-spam par combat)
        _castLedgers.TryRemove(pid, out _); _fightTurnNo.TryRemove(pid, out _);   // fin de combat -> réinitialise les temps de relance
        _emptyDumpedTagsThisFight.TryRemove(pid, out _);  // fin de combat -> autorise à nouveau le dump de diagnostic du prochain combat
        ResetFightOptions(pid);                // Lot E : options de combat du combat terminé
        _turnRunning.TryRemove(pid, out _); _turnEpoch.TryRemove(pid, out _);   // fin de combat -> libère anti-réentrance + époque
        ResetScriptFightState(pid);            // fin de combat -> oublie l'état des hooks de combat Lua
        // Stats : on retire les combattants du combat termine mais on GARDE la cle 0 (stats de base hors combat).
        if (_fighterStats.TryGetValue(pid, out var fst))
            foreach (var k in fst.Keys.Where(k => k != 0).ToList()) fst.TryRemove(k, out _);
    }

    /// <summary>Combat commence (FightStartingEvent) / termine (FightEndEvent) — pour attendre la fin d'un combat cote script.</summary>
    public event Action<uint>? FightStarted;
    public event Action<uint>? FightEnded;
    /// <summary>Issue d'un combat qui vient de se terminer : (pid, gagné, perdu). Levé JUSTE AVANT FightEnded (état équipe encore présent).</summary>
    public event Action<uint, bool, bool>? FightOutcome;
    /// <summary>Prix moyen HDV reçu pour un objet (gid, prix unitaire moyen) quand le bot consulte l'HDV — pour estimer la valeur des ressources récoltées.</summary>
    public event Action<long, long>? MarketPriceUpdated;

    // Décode l'issue du combat depuis FightEndEvent.results (repeated FightResultListEntry) : on cherche MON entrée
    // (fighter_list_entry dont fighter_id == mon id perso) et on lit son outcome. Énum FightOutcome (stable) :
    // 2 = VICTOIRE ; 0 = défaite (proto3 omet le 0 côté perdants) ; 1 = nul. Vérifié capture 2026-09-14.
    // ⚠ named_party_teams_outcomes (#1) est ABSENT des combats PvM -> on n'en dépend pas. TOUT est routé par le mapping,
    // y compris fighter_id (FighterListEntry.fighter_id = #6).
    private void DecodeFightOutcome(uint pid, IReadOnlyList<ProtoField> payload)
    {
        // Identité par _charId (id AUTHENTIFIÉ kvi) EN PRIORITÉ, PAS par le roster combat vivant (SelfActorId) : en cas
        // d'ABANDON ou de MORT, le serveur retire MON combattant du plateau (jue action 103) AVANT le FightEndEvent, donc
        // SelfActorId — qui relit _fighters où je ne suis plus — renverrait 0 et la défaite ne serait JAMAIS comptée.
        // Mon fighter_id dans results reste mon _charId. Repli SelfActorId si _charId inconnu (pid sans sélection perso).
        long me = _charId.TryGetValue(pid, out var cid) && cid > 0 ? cid : SelfActorId(pid);
        if (me == 0) return;
        int resultsF = FNum("FightEndEvent", "results");
        int fighterF = FNum("FightResultListEntry", "fighter_list_entry");
        int outcomeF = FNum("FightResultListEntry", "outcome");
        int fidF = FNum("FighterListEntry", "fighter_id");
        foreach (var f in payload)
            if (f.Field == resultsF && f.Message is { } entry)
            {
                var fighter = entry.FirstOrDefault(g => g.Field == fighterF)?.Message;
                long fid = fighter is null ? 0 : PayloadReader.Int(fighter, fidF, 0);
                if (fid != me) continue;
                int outcome = (int)PayloadReader.Int(entry, outcomeF, 0);
                FightOutcome?.Invoke(pid, outcome == 2, outcome == 0);   // victoire=2 ; défaite=0 (nul=1 -> ni l'un ni l'autre)
                return;
            }
    }
    // Cases REFUSÉES par le serveur pendant CE combat (jqt sur un déplacement de combat) -> exclues du pathfinding/ciblage
    // pour ne pas re-tenter en boucle un move bloqué (souvent = position d'un ennemi mal suivie). Vidé au re-sync (kmk) + fin de combat.
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<int, byte>> _blockedCells = new();
    // OBSTACLES PERSISTANTS appris pendant CE combat (case refusée par le serveur alors qu'AUCUN combattant suivi ne
    // l'occupe = mur invoqué / bombe / idole / portail / occupant non décodé). Contrairement à _blockedCells (vidé chaque
    // tour + au kmk), ceux-ci PERSISTENT tout le combat (un obstacle ne bouge pas) -> vidés seulement à la FIN du combat.
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<int, byte>> _fightObstacles = new();

    // BARRIÈRES DE SÉQUENCE de combat (RACINE des moves refusés — vérifiée : à vitesse Lente tout passe, en Instant/Fast le
    // serveur refuse car on injecte pendant une séquence OUVERTE). (await SequenceEndEvent) / un bot tiers (compteur
    // de séquences -> 0) : (A) 1re action gatée sur FightTurnStartPlayingEvent `jyj` (« tu peux jouer », la mise en place du
    // tour est finie) ; (B) après CHAQUE action on attend le `SequenceEndEvent` `isq` avant la suivante. Un cast est validé
    // sur les PA (déjà acquis) -> accepté même en pleine séquence ; un DÉPLACEMENT doit être réconcilié -> rejeté (jqt) s'il
    // arrive pendant une séquence -> ce sont les délais qui masquaient/révélaient le bug selon la vitesse.
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<bool>> _seqEndTcs = new();
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<bool>> _turnPlayTcs = new();
    // DIAG : compte les isq (SequenceEndEvent) / jyj (FightTurnStartPlayingEvent) REÇUS -> si 0 en combat, le mapping streamé
    // ne les nomme/mappe pas comme attendu (code serveur ≠ isq/jyj) -> les barrières tombent sur le timeout (lent). À voir dans le log TOUR.
    private readonly ConcurrentDictionary<uint, int> _isqCount = new();
    private readonly ConcurrentDictionary<uint, int> _jyjCount = new();
    // Télémétrie Turbo, sans effet sur l'ordonnancement : permet de comparer Givre aux timings Givre sans enlever
    // aucune barrière. Horodatages monotones locaux (TickCount64), activés uniquement quand le profil courant = Turbo.
    private readonly ConcurrentDictionary<uint, (long ActionId, long At, long StartAt)> _turboLastSelfSeqEnd = new();
    // Séquences d'action du serveur : profondeur d'imbrication + début de la séquence EXTERNE en cours. Un sort ouvre une séquence
    // externe qui contient des sous-séquences (mort, déclencheurs) ; seule la FIN de l'externe, dont le DÉBUT suit notre requête,
    // est la réponse à CE sort. Une fin « self » plus ancienne ou imbriquée fermait la barrière trop tôt -> 2e sort sur la même case.
    private readonly ConcurrentDictionary<uint, int> _turboSeqDepth = new();
    // Confirmations déjà envoyées PAR LE BOT (accusés + « tour prêt », Turbo MITM) : la copie du client visible est retirée du flux montant,
    // pour que le serveur reçoive chaque confirmation UNE seule fois (comme en Socket).
    private readonly TurboUpstreamDedupe _turboDedupe = new();
    /// <summary>Turbo-ack actif pour ce pid (MITM comme Socket) : accusés de nos séquences envoyés dès leur fin.</summary>
    public bool IsTurboAck(uint pid) => AppConfig.Current.TurboAckOwned && TurboMetricsEnabled(pid);
    /// <summary>Profil Turbo + « Accuser les actions de tous les combattants » (Socket comme MITM).</summary>
    public bool IsTurboAckAll(uint pid) => _combat?.TryGetValue(pid, out var tp) == true && tp?.Speed == CombatSpeed.Turbo;   // Turbo = agressif d office (réglage retiré : il restait à false)
    /// <summary>Profil Turbo + « Passage de tour agressif » (« tour prêt » anticipé, réponse immédiate au marqueur, relances).</summary>
    public bool IsTurboEagerReady(uint pid) => _combat?.TryGetValue(pid, out var tp) == true && tp?.Speed == CombatSpeed.Turbo;
    private bool TurboAckAllOwned(uint pid) => !IsSocketPid(pid) && IsTurboAckAll(pid);
    private bool TurboEagerReadyOwned(uint pid) => !IsSocketPid(pid) && IsTurboEagerReady(pid);

    // MITM : « tour prêt » du bot (Turbo agressif), UNE fois par marqueur : le « tour prêt » du client visible est retiré (DropClientAck).
    private readonly ConcurrentDictionary<uint, bool> _turboEarlyReady = new();
    private void InjectTurboAckFor(uint pid, long actionId, string who)
    {
        if (actionId == 0) return;
        _turboDedupe.NoteBotAck(pid, actionId, TurboNow(), foreign: true);   // séquence d'un autre combattant : le client visible ne l'accuse pas
        if (InjectForge(pid, MoveForge.BuildActionAckFramed(actionId)))
            TransportLog.Line($"[turbo-ack] accusé envoyé par le bot ({who}) action={actionId}");
        else _turboDedupe.UndoBotAck(pid, actionId);
    }
    private void InjectTurboReady(uint pid, string why, bool early = false)
    {
        if (!_turboDedupe.TryBeginBotReady(pid, TurboNow(), early)) return;   // le client visible a déjà répondu à ce marqueur : un seul « tour prêt » en amont
        if (InjectForge(pid, MoveForge.BuildTurnReadyFramed()))
            TransportLog.Line($"[turbo-ready] tour prêt envoyé par le bot : {why}");
        else _turboDedupe.UndoBotReady(pid, early);
    }
    private bool TurboAckOwned(uint pid) => !IsSocketPid(pid) && TurboMetricsEnabled(pid);
    /// <summary>Turbo MITM en combat : le relais décode les paquets du serveur AVANT de les donner au client visible, pour que l'accusé
    /// du bot parte toujours le premier (comme en Socket, où le bot est le client).</summary>
    private bool TurboDecodeFirst(uint pid) => TurboAckOwned(pid) && IsInFight(pid);
    // Actions déjà accusées (par le bot ou par le client visible) : la barrière Turbo MITM se ferme dès que NOTRE séquence est finie
    // ET accusée, quel que soit l'ordre d'arrivée (avant : un accusé du client décodé avant la fin de séquence bloquait 5 s).
    private readonly ConcurrentDictionary<(uint Pid, long ActionId), long> _turboAcked = new();
    private void NoteTurboAcked(uint pid, long actionId) { if (actionId != 0) _turboAcked[(pid, actionId)] = TurboNow(); }
    /// <summary>Délai max d'attente d'une barrière Turbo : court quand le bot accuse lui-même (MITM comme Socket), long sinon
    /// (l'accusé dépend alors du rythme d'animation du client visible).</summary>
    private int TurboBarrierTimeoutMs(uint pid) => IsSocketPid(pid) || TurboAckOwned(pid) ? 1500 : 5000;
    private bool DropClientAck(uint pid, byte[] frame)
    {
        bool ownsTurn = !IsSocketPid(pid) && IsInFight(pid) && _myTurnOpen.GetValueOrDefault(pid)
            && LicenseService.HasPermission(LicenseService.Perm.Fight)
            && (EngineAutoPlays(pid) || ScriptDrivesTurns(pid));
        bool readyOwned = TurboEagerReadyOwned(pid) && IsInFight(pid);
        if ((!_turboDedupe.HasPendingAcks && !ownsTurn && !readyOwned) || _catalog is null) return false;
        var tree = GivreCore.Protobuf.RawProtobuf.Parse(frame);
        foreach (var hit in GivreCore.Protobuf.RawProtobuf.FindTypeUrls(tree))
        {
            string? name = _catalog.NameForCode(hit.Code);
            if (name == "FightTurnFinishRequest" && ownsTurn
                && FieldOf("FightTurnFinishRequest", "is_afk") is int afkField)
            {
                long value = PayloadReader.Int(hit.Payload ?? (IReadOnlyList<ProtoField>)Array.Empty<ProtoField>(), afkField, -1);
                bool? afk = value == 1 ? true : value == 0 ? false : null;
                if (GivreClone.Engine.ClientAutoPassPolicy.Suppress(IsSocketPid(pid), IsInFight(pid),
                    _myTurnOpen.GetValueOrDefault(pid), EngineAutoPlays(pid) || ScriptDrivesTurns(pid),
                    LicenseService.HasPermission(LicenseService.Perm.Fight), afk))
                {
                    TransportLog.Line($"[combat] pid {pid} : fin de tour AFK du client retirée (tour piloté par le bot, is_afk=1)");
                    return true;
                }
            }
            // « Tour prêt » du client visible : un seul par marqueur FightIsTurnReady (le bot a déjà répondu, ou c'est le client qui répond).
            if (name == "FightTurnReadyRequest" && readyOwned)
            {
                long isReady = FieldOf("FightTurnReadyRequest", "is_ready") is int readyField
                    ? PayloadReader.Int(hit.Payload ?? (IReadOnlyList<ProtoField>)Array.Empty<ProtoField>(), readyField, 1) : 1;
                if (isReady != 0 && _turboDedupe.ShouldDropClientReady(pid, TurboNow()))
                { TransportLog.Line("[turbo-ready] tour prêt du client retiré (déjà envoyé pour ce marqueur)"); return true; }
                continue;
            }
            if (name != "GameActionAcknowledgementRequest") continue;
            if (FieldOf("GameActionAcknowledgementRequest", "action_id") is not int f) return false;
            long id = PayloadReader.Int(hit.Payload ?? (IReadOnlyList<GivreCore.Protobuf.ProtoField>)Array.Empty<GivreCore.Protobuf.ProtoField>(), f, 0);
            if (_turboDedupe.ShouldDropClientAck(pid, id, TurboNow())) { TransportLog.Line($"[turbo-ack] accusé du client retiré (déjà envoyé par le bot) action={id}"); return true; }
        }
        return false;
    }
    private readonly ConcurrentDictionary<uint, long> _turboSeqOuterStartAt = new();
    // Type de la dernière séquence externe fermée (Spell0 Weapon1 Glyph2 Triggered3 Move4 Death5 TurnStart6 TurnEnd7 FightStart8) et nature de la
    // dernière requête (c = sort, m = déplacement) : une séquence « déclenchée » (type 3) ouverte juste avant notre sort fermait la barrière -> double tir.
    private readonly ConcurrentDictionary<uint, long> _turboLastSelfSeqType = new();
    private readonly ConcurrentDictionary<uint, char> _turboRequestKind = new();
    private readonly ConcurrentDictionary<uint, (long ActionId, long At)> _turboLastAck = new();
    private readonly ConcurrentDictionary<uint, long> _turboLastRequestAt = new();
    private readonly ConcurrentDictionary<uint, (long RequestAt, int AvailableAp)> _turboCastRejected = new();
    private readonly ConcurrentDictionary<uint, int> _turboCastRejectMessageId = new();
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<bool>> _turboBarrierSignal = new();
    // Horloge Turbo ms haute résolution : TickCount64 (~15 ms) confondait la fin de la séquence précédente avec notre requête -> 2e sort inutile.
    private static long TurboNow() => System.Diagnostics.Stopwatch.GetTimestamp() * 1000 / System.Diagnostics.Stopwatch.Frequency;
    private bool TurboMetricsEnabled(uint pid) => _combat.TryGetValue(pid, out var p) && p.Speed == CombatSpeed.Turbo;
    /// <summary>Pas de sondage des attentes de combat : 1 ms en Turbo (minuteur 1 ms, cf Program.EnableHighResolutionTimer), sinon la valeur historique.</summary>
    private int PollMs(uint pid, int normalMs) => TurboMetricsEnabled(pid) ? 1 : normalMs;
    private bool TurboTrackingEnabled(uint pid) => TurboMetricsEnabled(pid) || _turboLastRequestAt.ContainsKey(pid);
    private long NoteTurboRequestTx(uint pid, string kind)
    {
        if (!TurboMetricsEnabled(pid)) return 0;
        long lastEvent = Math.Max(_turboSeqOuterStartAt.GetValueOrDefault(pid),
            _turboLastAck.TryGetValue(pid, out var prevAck) ? prevAck.At : 0);
        if (_turboLastSelfSeqEnd.TryGetValue(pid, out var prevSeq)) lastEvent = Math.Max(lastEvent, Math.Max(prevSeq.At, prevSeq.StartAt));
        long now = TurboFightPolicy.RequestStamp(TurboNow(), lastEvent);
        long ackToReq = _turboLastAck.TryGetValue(pid, out var ack) ? now - ack.At : -1;
        _turboSeqOuterStartAt.TryRemove(pid, out _);
        _turboCastRejected.TryRemove(pid, out _);
        _turboCastRejectMessageId.TryRemove(pid, out _);
        _turboLastRequestAt[pid] = now;
        _turboRequestKind[pid] = kind.StartsWith("cast") ? 'c' : kind.Contains("move") ? 'm' : '?';
        TransportLog.Line($"[turbo-metric] request_tx kind={kind} t={now} ack_to_request_ms={(ackToReq >= 0 ? ackToReq : -1)}");
        return now;
    }
    private void NoteTurboCastRejection(uint pid, int messageId, IReadOnlyList<string> parameters)
    {
        // Player or companion challenge failure is a consequence of an accepted cast,
        // not a refusal: its damage/sequence confirmation can follow this text.
        if (messageId is 188 or 597 or 1376) return;
        if (!IsInFight(pid) || !_myTurnOpen.GetValueOrDefault(pid)
            || !_turboRequestKind.TryGetValue(pid, out var kind) || kind != 'c'
            || !_turboLastRequestAt.TryGetValue(pid, out var requestAt) || requestAt <= 0) return;
        bool isCastRefusalText = messageId is 171 or 172 or 173 or 174 or 175 or 176 or 177;
        bool started = !isCastRefusalText && TurboSequenceStartedSince(pid, requestAt);
        if (!started && !TurboBarrierClosed(pid, requestAt, out _, out _))
        {
            // Réveil d'abord : la résolution du texte peut charger les données du jeu (secondes) et ne doit jamais retarder le tour.
            int availableAp = CombatParity.CastRejectionPolicy.TryRead(messageId, parameters, out int parsedAp) ? parsedAp : -1;
            _turboCastRejected[pid] = (requestAt, availableAp);
            _turboCastRejectMessageId[pid] = messageId;
            PulseTurboBarrier(pid);
            TransportLog.Line($"[turbo-metric] cast_refused message={messageId} PA={availableAp} request_to_refusal_ms={Math.Max(0, TurboNow() - requestAt)} ; arrêt de l'attente sans accusé fictif");
        }
        var info = LastTextInfo(pid);
        string text = _gameNames.IsReady ? _gameNames.InfoMessageText(info?.MessageType ?? 0, messageId, parameters) ?? "<texte non résolu>" : "<données non chargées>";
        TransportLog.Line($"[combat-cast] text message={messageId} params=[{string.Join(" | ", parameters)}] text={text} sequence_started={started}");
    }
    private bool TryTurboCastRejection(uint pid, long requestAt, out int availableAp)
    {
        availableAp = 0;
        if (requestAt <= 0 || !_turboCastRejected.TryGetValue(pid, out var refusal)
            || refusal.RequestAt != requestAt || _turboLastRequestAt.GetValueOrDefault(pid) != requestAt) return false;
        availableAp = refusal.AvailableAp;
        return true;
    }

    private bool TryTurboCastRejectionWithMessage(uint pid, long requestAt, out int availableAp, out int messageId)
    {
        availableAp = 0;
        messageId = 0;
        if (requestAt <= 0 || !_turboCastRejected.TryGetValue(pid, out var refusal)
            || refusal.RequestAt != requestAt || _turboLastRequestAt.GetValueOrDefault(pid) != requestAt) return false;
        availableAp = refusal.AvailableAp;
        messageId = _turboCastRejectMessageId.TryGetValue(pid, out int mid) ? mid : 0;
        return true;
    }
    private static TaskCompletionSource<bool> NewTurboBarrierSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private void PulseTurboBarrier(uint pid)
    {
        while (true)
        {
            var current = _turboBarrierSignal.GetOrAdd(pid, static _ => NewTurboBarrierSignal());
            var next = NewTurboBarrierSignal();
            if (!_turboBarrierSignal.TryUpdate(pid, next, current)) continue;
            current.TrySetResult(true);
            return;
        }
    }
    private bool TurboBarrierClosed(uint pid, long requestAt, out long actionId, out long seqToAckMs)
    {
        actionId = 0; seqToAckMs = -1;
        if (!_turboLastSelfSeqEnd.TryGetValue(pid, out var seq)) return false;
        long seqType = _turboLastSelfSeqType.TryGetValue(pid, out var st) ? st : -1;
        char kind = _turboRequestKind.TryGetValue(pid, out var rk) ? rk : '?';
        bool socket = IsSocketPid(pid), botAcks = !socket && TurboAckOwned(pid);
        long? ackedAt = _turboAcked.TryGetValue((pid, seq.ActionId), out var at) ? at : null;
        (long, long)? clientAck = _turboLastAck.TryGetValue(pid, out var ack) ? ack : null;
        if (!TurboFightPolicy.BarrierClosed(requestAt, seq, seqType, kind, socket, botAcks, ackedAt, clientAck)) return false;
        actionId = seq.ActionId;
        seqToAckMs = socket ? 0 : botAcks ? Math.Max(0, (ackedAt ?? seq.At) - seq.At) : clientAck!.Value.Item2 - seq.At;
        return true;
    }
    /// <summary>Barrière MITM avec SECONDE CHANCE : si la séquence ne se ferme pas dans le délai court (animations réelles quand le
    /// Turbo natif n'est pas injecté dans ce client), on attend encore <see cref="TurboBarrierGraceMs"/> avant d'abandonner le tour.
    /// Capture 23/10 : fin de séquence à 1 548 ms pour un délai de 1 500 -> le tour était terminé après UN seul sort.</summary>
    private async Task<bool> WaitTurboActionBarrierWithGrace(uint pid, long requestAt)
    {
        // Sonde courte : une action acceptée OUVRE sa séquence presque tout de suite ; seule sa FIN dépend des animations du client.
        // Sans séquence ouverte après NoSequenceProbeMs, l'action a été ignorée par le serveur : inutile d'attendre 1,5 s (tour 1
        // contre le Poutch 24/10 16:53:15, 1,5 s perdue). Séquence ouverte -> attente complète puis seconde chance, comme avant.
        int fullMs = TurboBarrierTimeoutMs(pid);
        int probeMs = Math.Min(NoSequenceProbeMs, fullMs);
        if (await WaitTurboActionBarrier(pid, requestAt, probeMs).ConfigureAwait(false)) return true;
        if (!IsInFight(pid) || !_myTurnOpen.GetValueOrDefault(pid)) return false;
        if (!TurboSequenceStartedSince(pid, requestAt)) return false;   // aucune séquence : requête ignorée, pas une animation lente
        if (fullMs > probeMs && await WaitTurboActionBarrier(pid, requestAt, fullMs - probeMs).ConfigureAwait(false)) return true;
        if (!IsInFight(pid) || !_myTurnOpen.GetValueOrDefault(pid)) return false;
        TransportLog.Line($"[turbo-metric] barrière lente (animations du client ?) -> seconde chance {TurboBarrierGraceMs} ms");
        return await WaitTurboActionBarrier(pid, requestAt, TurboBarrierGraceMs).ConfigureAwait(false);
    }
    private const int TurboBarrierGraceMs = 1500;
    private const int NoSequenceProbeMs = 250;

    /// <summary>Une séquence (externe) a-t-elle démarré depuis la requête ? Non = le serveur n'a rien exécuté (requête ignorée).</summary>
    private bool TurboSequenceStartedSince(uint pid, long requestAt) =>
        requestAt > 0 && _turboSeqOuterStartAt.TryGetValue(pid, out var start) && start >= requestAt;

    private async Task<bool> WaitTurboActionBarrier(uint pid, long requestAt, int timeoutMs = 5000)
    {
        if (requestAt <= 0) return true;
        long until = TurboNow() + timeoutMs;
        while (IsInFight(pid) && _myTurnOpen.GetValueOrDefault(pid))
        {
            // A confirmed rejection resolves the request without a sequence or ACK.
            if (TryTurboCastRejection(pid, requestAt, out _)) return true;
            if (TurboBarrierClosed(pid, requestAt, out var actionId, out var seqToAck))
            {
                TransportLog.Line($"[turbo-metric] barrier_closed action={actionId} request_to_ack_ms={TurboNow() - requestAt} sequence_end_to_ack_ms={seqToAck}");
                return true;
            }
            long remaining = until - TurboNow();
            if (remaining <= 0) break;
            var signal = _turboBarrierSignal.GetOrAdd(pid, static _ => NewTurboBarrierSignal());
            // Relecture après capture du signal : évite de rater un ACK arrivé entre le premier test et l'attente.
            if (TryTurboCastRejection(pid, requestAt, out _) || TurboBarrierClosed(pid, requestAt, out _, out _)) continue;
            await Task.WhenAny(signal.Task, Task.Delay((int)Math.Min(remaining, int.MaxValue))).ConfigureAwait(false);
        }
        TransportLog.Line($"[turbo-metric] BARRIERE NON FERMEE après {Math.Max(0, TurboNow() - requestAt)} ms -> aucune action suivante injectée");
        return false;
    }
    private void ResetTurboMetrics(uint pid)
    {
        _turboLastSelfSeqEnd.TryRemove(pid, out _);
        _turboLastSelfSeqType.TryRemove(pid, out _);
        _turboRequestKind.TryRemove(pid, out _);
        _turboSeqDepth.TryRemove(pid, out _);
        _turnStartedInSequence.TryRemove(pid, out _);
        _turboSeqOuterStartAt.TryRemove(pid, out _);
        _turboDedupe.Reset(pid);
        _turboLastAck.TryRemove(pid, out _);
        foreach (var k in _turboAcked.Keys.Where(k => k.Pid == pid)) _turboAcked.TryRemove(k, out _);
        _turboLastRequestAt.TryRemove(pid, out _);
        _turboCastRejected.TryRemove(pid, out _);
        _turboCastRejectMessageId.TryRemove(pid, out _);
        _turboEarlyReady.TryRemove(pid, out _);
        if (_turboBarrierSignal.TryRemove(pid, out var pulse)) pulse.TrySetResult(true);
    }
    // Nb de fins de séquence (isq/jxh) AU MOMENT du dernier "ouvreur" de séquence : début de tour (baseline du 1er move) OU
    // injection de notre dernière action (cast/move, via ArmSeqEnd). La barrière WaitMoveClear n'autorise la sortie « flux
    // calmé » que lorsque isq a dépassé ce seuil (+1) = l'ouvreur s'est refermé, même si cette fermeture précède le démarrage
    // de la barrière (elle peut tomber pendant l'attente de confirmation du move précédent).
    private readonly ConcurrentDictionary<uint, int> _seqArmIsq = new();
    private void ArmSeqEnd(uint pid)
    {
        _seqEndTcs[pid] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _seqArmIsq[pid] = _isqCount.GetValueOrDefault(pid);
    }
    private async Task WaitSeqEnd(uint pid, int timeoutMs)
    {
        if (_seqEndTcs.TryGetValue(pid, out var tcs)) await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);
    }
    private void ArmTurnPlay(uint pid) => _turnPlayTcs[pid] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    // Horodatage du DERNIER jyj (FightTurnStartPlayingEvent) reçu par pid : sur le serveur du user, jyj arrive JUSTE AVANT jwi
    // (qui déclenche RunFightTurn) -> quand on arme la TCS sur jwi, le jyj de MON tour est déjà passé, donc l'attente épuisait
    // le timeout COMPLET à chaque tour (= le délai de ~2,5 s constaté). Cf audit 2026-09-01 : jyj EST reconnu (jyj=18,30,41…
    // dans les logs), il n'est simplement pas synchronisable via une TCS armée trop tard.
    private readonly ConcurrentDictionary<uint, long> _lastTurnPlay = new();
    // ── PROPRIÉTÉ DU TOUR (fix racine « TOUR FANTÔME », 2026-09-07) ────────────────────────────────────────────────────
    // Le déclencheur de tour est `jwi` + IsFightTurnOf, qui matche N'IMPORTE QUEL varint == mon actorId : il ne distingue donc
    // PAS « ton tour COMMENCE » de « ton tour est FINI ». Le serveur renvoyant un écho porteur de mon id juste après NOTRE jxy,
    // RunFightTurn repartait ~33 ms plus tard pour un TOUR ENTIER joué PENDANT LE TOUR DES ENNEMIS (preuve logs : deux TOUR
    // consécutifs avec le MÊME jyj — 13.863 [jyj=17] et 17.015 [jyj=17] ; 50.579 [jyj=3] puis 51.246/51.966 [jyj=4] deux fois).
    // Sur ce tour fantôme le serveur DROPPE tout : les 4 casts n'ont fait aucun dégât (alors qu'UN seul cast avait tué -1 en
    // 34 ms au vrai tour) et les moves sont « refusés » — d'où le « il ne fait rien / il passe son tour ».
    // Le compteur jyj (FightTurnStartPlayingEvent) est mappé et fiable : exactement UN par vrai tour. On passe donc d'un gate
    // par RÉCENCE (< 2 s, qui acceptait le jyj du tour PRÉCÉDENT) + fail-open à un gate par FRONT : un tour n'est joué que si un
    // jyj NON ENCORE CONSOMMÉ est disponible. Coût 0 ms sur un vrai tour.
    private readonly ConcurrentDictionary<uint, int> _jyjConsumed = new();   // valeur de _jyjCount du dernier tour RÉELLEMENT joué
    private readonly ConcurrentDictionary<uint, long> _turnEndedAt = new();  // TickCount64 de NOTRE dernier jxy injecté
    private readonly ConcurrentDictionary<uint, bool> _myTurnOpen = new();   // vrai UNIQUEMENT pendant MA fenêtre de tour (jzc), jusqu'à notre jxy
    private readonly ConcurrentDictionary<uint, bool> _jzcSeen = new();      // `jzc` (autorité de tour) vu au moins une fois CE combat

    /// <summary>Gate de tour par FRONT jyj. true = c'est bien MON tour (front consommé) ; false = écho/fantôme, ne rien injecter.</summary>
    private async Task<bool> WaitTurnPlayEdge(uint pid, int timeoutMs)
    {
        // `jzc` fait autorité quand il existe : il a DÉJÀ nommé le combattant dont c'est le tour -> 0 ms d'attente, et si la
        // fenêtre s'est refermée entre-temps (jzc d'un autre combattant) on refuse net.
        if (_jzcSeen.GetValueOrDefault(pid)) return _myTurnOpen.GetValueOrDefault(pid);
        int consumed = _jyjConsumed.GetValueOrDefault(pid);
        int now = _jyjCount.GetValueOrDefault(pid);
        if (now > consumed) { _jyjConsumed[pid] = now; _myTurnOpen[pid] = true; return true; }   // 0 ms : le tour EST ouvert
        // ATTENTE ACTIVE (fix 2026-09-07) — NE PAS bloquer sur la TCS. À L'OUVERTURE d'un combat, `jwi` précède `jzc` de 60 à
        // 310 ms et arme un tour FANTÔME via le repli legacy (`_jzcSeen` encore faux). La TCS qu'on attendrait ici est ensuite
        // RÉARMÉE par les jwi suivants (5 à 8 en quelques ms) puis par le jzc lui-même : celle qu'on attend devient ORPHELINE
        // et plus rien ne la réveille -> timeout PLEIN brûlé au 1er tour de CHAQUE combat (mesuré 1200-1216 ms sur 5 combats ;
        // dès le round 2, jzc et TOUR tombent sur la MÊME milliseconde). On relit donc l'autorité périodiquement.
        long t0 = System.Environment.TickCount64;
        while (System.Environment.TickCount64 - t0 < timeoutMs)
        {
            if (_jzcSeen.GetValueOrDefault(pid)) return _myTurnOpen.GetValueOrDefault(pid);   // l'autorité jzc est arrivée entre-temps
            if (_jyjCount.GetValueOrDefault(pid) > consumed) break;
            await Task.Delay(PollMs(pid, 20)).ConfigureAwait(false);
        }
        int after = _jyjCount.GetValueOrDefault(pid);
        if (after > consumed) { _jyjConsumed[pid] = after; _myTurnOpen[pid] = true; return true; }
        // REPLI PATCH-PROOF : aucun jyj vu de TOUT ce combat -> le mapping ne le nomme pas sur ce serveur ; on joue quand même
        // plutôt que de rester AFK (aucune régression si jyj disparaît après une MAJ Dofus).
        if (_jyjCount.GetValueOrDefault(pid) == 0) { _myTurnOpen[pid] = true; return true; }
        return false;   // aucun front jyj -> ce n'est PAS mon tour
    }
    // Barrière ROBUSTE, INDÉPENDANTE DU MAPPING : avant d'injecter un déplacement on attend que le flux de paquets de
    // combat se CALME (les effets de la séquence précédente ont fini d'arriver) OU la fin de séquence (isq/jxh si reconnu),
    // OU un timeout. Pas besoin d'identifier l'event exact -> aucune régression même si le code diffère selon le serveur.
    private readonly ConcurrentDictionary<uint, long> _lastFightPkt = new();
    private readonly ConcurrentDictionary<uint, bool> _turnStartedInSequence = new();
    private async Task WaitMoveClear(uint pid, int quietMs, int timeoutMs)
    {
        long start = System.Environment.TickCount64;
        int isq0 = _isqCount.GetValueOrDefault(pid);   // nb de fins de séquence AU DÉBUT de l'attente
        // Séquence qu'on DOIT laisser se refermer avant d'injecter : celle ouverte par le dernier "ouvreur" (début de tour OU
        // notre dernier cast/move, cf ArmSeqEnd) -> son isq attendu = _seqArmIsq[pid] + 1. TANT QUE ce seuil n'est pas atteint,
        // une séquence est ENCORE OUVERTE côté serveur : on INTERDIT la sortie « flux calmé » (le serveur fait des PAUSES au
        // milieu d'une séquence -> le silence réseau ne prouve PAS qu'elle est fermée). C'ÉTAIT LA FUITE : le 1er move de chaque
        // tour partait après ~350 ms de silence, dans la séquence d'ouverture encore ouverte -> DROP silencieux -> « refusé »
        // -> tour passé (logs turn 5 : 1er move refusé, essai 2 OK une fois la séquence enfin fermée ~2 s plus tard).
        int need = _seqArmIsq.GetValueOrDefault(pid) + 1;
        while (System.Environment.TickCount64 - start < timeoutMs)
        {
            int cur = _isqCount.GetValueOrDefault(pid);
            // (A) une NOUVELLE fin de séquence est arrivée depuis le début de l'attente -> l'ouvreur s'est refermé, on injecte.
            //     (On compare le COMPTEUR isq, jamais `tcs.IsCompleted` : au début d'un tour le TCS est un RESTE déjà complété
            //      du tour précédent -> il ferait repartir la barrière INSTANTANÉMENT dans la séquence d'ouverture encore ouverte.)
            if (cur > isq0) return;
            // (B) l'ouvreur précédent est DÉJÀ refermé (isq a atteint le seuil armé) -> la sortie « flux calmé » redevient sûre :
            //     plus aucune séquence ouverte, le calme réseau signifie bien « rien en cours ». (Chemin des moves suivants : cast/
            //     move précédent réussi -> son isq est arrivé pendant l'attente de confirmation -> seuil atteint -> injection rapide.)
            // (B) REPLI PATCH-PROOF (fix 2026-09-07), même logique que WaitTurnPlayEdge sur jyj : `cur == 0` = AUCUN isq de
            //     TOUT ce combat -> le mapping ne nomme PAS SequenceEndEvent sur ce serveur (capture : 0 isq / 0
            //     SequenceEndEvent sur un combat entier, `isq=0` dans les 16 lignes TOUR) -> `need` est INATTEIGNABLE et,
            //     la sortie « flux calmé » étant IMBRIQUÉE dans ce test, la barrière dégénérait en SLEEP de timeoutMs :
            //     1,49 s mesurées entre la décision et l'injection pour 34 ms de RTT réel. Quand isq n'existe pas, le flux
            //     calmé redevient la seule preuve disponible. (`isq` est de toute façon un pin CONTESTÉ.)
            // A turn boundary does not necessarily open an action sequence. When Turbo's
            // tracked depth is zero, waiting for baseline+1 invents a missing sequence end
            // (300 ms before the first move). Keep the quiet window and all open-sequence waits.
            int? openSequences = _turboSeqDepth.TryGetValue(pid, out var depth) ? depth : null;
            if (CombatParity.MoveSequenceWaitPolicy.CanCheckQuiet(cur, need, TurboMetricsEnabled(pid),
                    _jzcSeen.GetValueOrDefault(pid) && !_turnStartedInSequence.GetValueOrDefault(pid),
                    _myTurnOpen.GetValueOrDefault(pid), openSequences))
            {
                long last = _lastFightPkt.TryGetValue(pid, out var t) ? t : 0;
                if (System.Environment.TickCount64 - last >= quietMs) return;
            }
            await Task.Delay(PollMs(pid, 15)).ConfigureAwait(false);
        }
    }

    private readonly ConcurrentDictionary<uint, IReadOnlyList<int>> _charSpells = new(); // pid -> sorts APPRIS (SpellsEvent hms)
    private readonly ConcurrentDictionary<uint, IReadOnlyDictionary<int, int>> _charSpellLevels = new(); // pid -> (spell_id -> grade)

    /// <summary>Grade (niveau) APPRIS d'un sort pour ce perso (hms). 1 par défaut. Sert à lire les dégâts du bon grade.</summary>
    public int LearnedSpellLevel(uint pid, int spellId) =>
        EngineControlled(pid) is { } controlled ? controlled.Fighter.Spells.GetValueOrDefault(spellId, 1) :
        _charSpellLevels.TryGetValue(pid, out var m) && m.TryGetValue(spellId, out var g) ? g : 1;
    private readonly ConcurrentDictionary<uint, bool> _turnRunning = new(); // pid -> tour d'auto-combat en cours (anti-reentrance)
    // ÉPOQUE de tour : incrémentée à CHAQUE jzc qui me donne la main. Sert à invalider un run PÉRIMÉ — sans elle, un run
    // encore vivant quand le tour suivant s'ouvre (a) faisait ABANDONNER le vrai tour en SILENCE (TryAdd échoue, aucun log,
    // le tour n'est jamais joué) et (b) injectait son `jxy` dans le tour SUIVANT, le coupant net.
    private readonly ConcurrentDictionary<uint, int> _turnEpoch = new();
    // Nb de refus de déplacement EXPLICITES (jqt / MapMovementRefusedEvent) reçus. Permet à l'attente de confirmation de
    // sortir dès le refus (~33 ms) au lieu de sonder SelfCell() jusqu'au plafond de 800 ms — elle ne changera jamais.
    private readonly ConcurrentDictionary<uint, int> _moveRefused = new();

    // Registre des lancers (Ledger) par combattant joué.
    private readonly ConcurrentDictionary<uint, Combat.CastLedger> _castLedgers = new();
    private Combat.CastLedger ActiveLedger(uint pid) => EngineControlled(pid)?.Ledger ?? _castLedgers.GetOrAdd(pid, static _ => new Combat.CastLedger());

    // Cooldown des sorts: compteur de MES tours par combat.
    private readonly ConcurrentDictionary<uint, int> _fightTurnNo = new();                                   // pid -> n° de MON tour dans ce combat

    private void RecordCast(uint pid, int spellId, int turnNo)
    {
        ActiveLedger(pid).RecordCast(spellId, 0, turnNo);
        NoteChallengeSpell(pid, spellId);   // défis Économe / Élémentaire (moteur ET script)
    }

    // Stats de combattant (PA/PM/PV) par pid puis par fighterId (cle 0 = stats de base hors combat, via kub). Alimente
    // par jxg (sync de combat) + kub, mis a jour par les pertes de PV (jwe #40). Sert au budget PA/PM REEL du moteur
    // (fini le 6/3 code en dur) et a l'onglet Infos. Voir GivreClone.Protocol.FightStatsReader.
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<long, FighterStats>> _fighterStats = new();
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<long, int>> _fighterBreeds = new();

    // Portée TOTALE du perso (caractéristique #19 = base + équipement/parchos), lue de kub. Ajoutée à la portée des
    // sorts "boostables" (rangeCanBeBoosted) dans l'auto-combat -> le bot tire à sa PO RÉELLE, pas à la PO de base.
    private readonly ConcurrentDictionary<uint, int> _selfRange = new();

    // Alliés CONFIRMÉS par PROPRIÉTAIRE : les invocations alliées (cf source jwe #3). Une invocation a un id
    // NÉGATIF comme un monstre -> sans ce filtre le bot l'attaque. Vidé à la fin du combat. (Le "team" du jxg = `alive`,
    // inexploitable; l'équipe proto3 est OMISE quand =0 -> on classe par propriétaire.)
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<long, byte>> _allies = new();
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<long, long>> _summonOwners = new();

    /// <summary>
    /// Invocation apparue en combat (toute forme : créature, double, bombe, invocation statique comme l'épée du Sacrieur).
    /// Classement allié/ennemi, du plus sûr au moins sûr : (1) son ÉQUIPE de spawn, autoritaire comme celle de
    /// FightFighterShowEvent ; (2) sinon l'équipe de son INVOCATEUR ; (3) invocateur = moi -> mes invocations (plafond) ;
    /// invocateur non ennemi sans équipe connue -> alliée. Avant : seules MES invocations étaient protégées, celles d'un
    /// coéquipier tombaient dans le repli « id négatif = monstre » et le bot les frappait.
    /// </summary>
    private void OnSummoned(uint pid, FightStatsReader.SummonedFighter sm, long? actionSource)
    {
        if (sm.InvisibilityState is int visibility)
            _fighterVisibility.GetOrAdd(pid, static _ => new FightVisibilityReducer())
                .Observe(new FighterVisibilityObservation(sm.Id, visibility), sm.Cell >= 0);
        if (sm.Cell >= 0)
        {
            IReadOnlyList<(int Cell, long ActorId)>? added = null;
            lock (_fightersLock)
            {
                var current = _fighters.TryGetValue(pid, out var roster)
                    ? roster : (IReadOnlyList<(int Cell, long ActorId)>)Array.Empty<(int Cell, long ActorId)>();
                if (!current.Any(f => f.ActorId == sm.Id))
                {
                    added = current.Append((sm.Cell, sm.Id)).ToList();
                    _fighters[pid] = added;
                }
            }
            if (added is not null)
            {
                FightersUpdated?.Invoke(pid, added);
                TransportLog.Line($"[combat] INVOCATION {sm.Id} apparue -> case {sm.Cell} (ajoutée au suivi)");
            }
        }
        _summonIds.GetOrAdd(pid, static _ => new ConcurrentDictionary<long, byte>())[sm.Id] = 0;   // toute invocation (lecture script isSummon)

        long owner = sm.Summoner ?? (actionSource is long s && s != 0 ? s : 0);   // invocateur, sinon lanceur de l'action
        int? team = sm.Team ?? (owner != 0 ? TeamOf(pid, owner) : null);
        if (team is int t) _fighterTeam.GetOrAdd(pid, static _ => new ConcurrentDictionary<long, int>())[sm.Id] = t;
        bool mine = owner != 0 && owner == SelfActorId(pid);
        if (mine || (owner != 0 && !IsEnemy(pid, owner)))
            _allies.GetOrAdd(pid, static _ => new ConcurrentDictionary<long, byte>())[sm.Id] = 0;
        if (owner != 0) _summonOwners.GetOrAdd(pid, static _ => new ConcurrentDictionary<long, long>())[sm.Id] = owner;
        TransportLog.Line($"[combat] invocation {sm.Id} (invocateur {(owner != 0 ? owner.ToString() : "?")}{(mine ? " = MOI" : "")}, " +
                          $"équipe {team?.ToString() ?? "?"}) -> {(IsEnemy(pid, sm.Id) ? "ENNEMIE" : "alliée, jamais ciblée")}");
    }

    /// <summary>Nombre de MES invocations encore VIVANTES (présentes dans _fighters). L'intersection avec _fighters élague
    /// automatiquement les invocations mortes (retirées de _fighters au jwe Death). Le propriétaire est comparé au lanceur courant.</summary>
    private int CountMySummons(uint pid)
    {
        if (!_summonOwners.TryGetValue(pid, out var owners) || owners.IsEmpty) return 0;
        if (!_fighters.TryGetValue(pid, out var fs)) return 0;
        long self = SelfActorId(pid);
        int n = 0;
        foreach (var f in fs)
            if (owners.TryGetValue(f.ActorId, out long owner) && owner == self)
                n += _fightMonsters.TryGetValue(pid, out var monsters) && monsters.TryGetValue(f.ActorId, out var monster)
                    ? GameData?.SummonGradeStats(monster.GenericId, monster.Grade, CharacterLevel(pid))?.SummonCost ?? 1
                    : 1;
        return n;
    }

    // BUG "a attaqué ~10 tours puis stoppé" : latch INVERSE de _allies. Un combattant que le bot a RÉELLEMENT attaqué
    // (cast offensif) est CONFIRMÉ ennemi -> un kae (FightTeamUpdateEvent) tardif ne peut plus le reclasser en allié et
    // aveugler le bot pour le reste du combat. _allies étant un latch définitif, un kae du roster ENNEMI mal lu comme
    // "le mien" latchait sinon les monstres (-id) en alliés à vie. Vidé à la fin du combat.
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<long, byte>> _confirmedEnemies = new();

    // Équipe (spawn_information.team du jxg, cf FightStatsReader.ReadFighterTeam) par combattant -> ENNEMI = équipe ≠ la
    // mienne (lue DYNAMIQUEMENT). Classe correctement monstres, invocations/compagnons ALLIÉS (team = la mienne) et un
    // "Archiduk" ENNEMI (famille de monstre, team adverse). Vidé à la fin du combat. Repli sur le signe si équipe inconnue.
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<long, int>> _fighterTeam = new();

    /// <summary>Evenement : les stats de MON combattant ont change (sync, perte de PV, hors combat) -> l'UI rafraichit.</summary>
    public event Action<uint>? FighterStatsUpdated;

    /// <summary>Stats de MON combattant (le plus grand actorId positif = le perso pilote), sinon les stats de base
    /// (cle 0, hors combat). null tant qu'aucune donnee n'est recue.</summary>
    public FighterStats? SelfStats(uint pid)
    {
        if (_fighterStats.TryGetValue(pid, out var byId))
        {
            long self = SelfActorId(pid);
            if (self != 0 && byId.TryGetValue(self, out var s)) return s;
            if (EngineControlled(pid) is null && byId.TryGetValue(0, out var baseStats)) return baseStats;
        }
        return null;
    }

    /// <summary>Portée TOTALE du perso (caractéristique #19, lue de kub). 0 si inconnue. Onglet Infos + allonge auto-combat.</summary>
    public int SelfRange(uint pid) => EngineControlled(pid) is not null
        ? SelfStats(pid)?.Characteristics.GetValueOrDefault(FightStatsReader.IdRange) ?? 0
        : _selfRange.TryGetValue(pid, out var r) ? r : 0;

    /// <summary>Contrainte de CELLULE-CIBLE d'un sort (needFreeCell/needTakenCell, bits m_flags via SpellCastConstraints) :
    /// refuse le cast sur une cellule OCCUPÉE si le sort exige une case LIBRE, ou sur une case LIBRE s'il exige une case
    /// OCCUPÉE. Fail-open si la donnée du sort est absente (aucune régression). La diagonale (géométrie) est gérée dans CanCast.</summary>
    private bool CastCellOk(uint pid, int spellId, int cell, bool occupied)
    {
        var fc = _gameNames.SpellCastConstraints(spellId, LearnedSpellLevel(pid, spellId));
        return Combat.CastLegality.CastCellConstraintsOk(fc?.NeedFreeCell ?? false, fc?.NeedTakenCell ?? false, occupied);
    }

    /// <summary>Données de combat d'UN sort pour le SIMULATEUR (mêmes tables + grade appris que <c>RunFightTurn</c>). null si
    /// inconnu (bundle sorts pas chargé). IsBuff = sort-sur-soi sans dégâts OU case Buff cochée -> lancé même sans ennemi.</summary>
    public (int Ap, int MinRange, int Range, bool CastInLine, bool CastInDiagonal, bool CastTestLos, bool Rangeable, bool IsBuff,
        int AvgDamage, int Element, int MaxPerTarget, int MaxPerTurn, int ZoneShape, int ZoneSize, int ZoneMin)? ResolveSimSpell(uint pid, int spellId, bool selfBuff)
    {
        int g = LearnedSpellLevel(pid, spellId);
        if (_gameNames.SpellCombat(spellId, g) is not { } s) return null;
        var dmg = _gameNames.GetSpellDamage(spellId, g);   // déjà résolu pour IsBuff : on capte Avg/Element/caps au lieu de le jeter
        bool isBuff = selfBuff || dmg is not { Max: > 0 };
        var z = _gameNames.SpellZone(spellId, g);          // forme d'AoE -> le simulateur touche TOUS les combattants de la zone
        return (s.Ap, s.MinRange, s.Range, s.CastInLine, s.CastInDiagonal, s.CastTestLos, s.Rangeable, isBuff,
            dmg?.Avg ?? 0, dmg?.Element ?? -1, dmg?.MaxPerTarget ?? 0, dmg?.MaxPerTurn ?? 0,
            z?.Shape ?? 0, z?.Size ?? 0, z?.MinSize ?? 0);
    }

    // --- Conditions de lancement par sort (onglet Combat). 0 (ou 0..100 pour les fenêtres PV) = désactivé -> aucune
    //     restriction. PV inconnu -> condition PV ignorée (fail-open : on ne bloque JAMAIS un cast faute d'info). ---
    // PV% (0-100) d'un combattant suivi, ou null si inconnu.
    private int? FighterHpPercent(uint pid, long actorId) =>
        _fighterStats.TryGetValue(pid, out var byId) && byId.TryGetValue(actorId, out var st) && st.MaxLife > 0
            ? (int)(100L * st.Life / st.MaxLife) : (int?)null;
    // PV ABSOLUS courants d'un combattant suivi (>0), ou null si inconnu. Alimente le ciblage « finish/execute » (KillFirst).
    private int? FighterAbsHp(uint pid, long actorId) =>
        _fighterStats.TryGetValue(pid, out var byId) && byId.TryGetValue(actorId, out var st) && st.MaxLife > 0 && st.Life > 0
            ? st.Life : (int?)null;
    private int? SelfHpPercent(uint pid) => SelfStats(pid) is { MaxLife: > 0 } s ? (int)(100L * s.Life / s.MaxLife) : (int?)null;

    // Géométrie de combat pour les verbes de déplacement Lua (MIROIR de RunFightTurn, dupliqué pour ne PAS toucher au
    // moteur) : marchabilité de combat (Mov && !nonWalkableDuringFight, cases apprises/obstacles exclues, occupation live),
    // StepAllowed (connexion de secteurs), ma cellule, et la map de FORGE (instance de combat _serverMapId).
    private (System.Func<int, bool> Walk, System.Func<int, int, bool> Step, int SelfCell, long ForgeMap)? BuildFightGeom(uint pid)
    {
        var (mapId, cells) = LoadFightMap(pid);
        if (mapId == 0) return null;
        var walk = new bool[MapPathfinder.CellCount];
        var floorF = new int[MapPathfinder.CellCount];
        var zoneF = new int[MapPathfinder.CellCount];
        var canMoveZoneF = new bool[MapPathfinder.CellCount];
        for (int i = 0; i < floorF.Length; i++) { floorF[i] = -1; zoneF[i] = -1; }
        if (cells is not null) foreach (var c in cells) if ((uint)c.CellId < walk.Length)
        {
            walk[c.CellId] = c.Mov && !c.NonWalkableDuringFight;
            canMoveZoneF[c.CellId] = c.MoveZone == 1;
            if (walk[c.CellId]) { zoneF[c.CellId] = (c.LinkedZone & 0xF8) >> 4; floorF[c.CellId] = c.Floor; }
        }
        long me = SelfActorId(pid);
        int selfCell = me != 0 && _fighters.TryGetValue(pid, out var f)
            ? f.Where(x => x.ActorId == me).Select(x => x.Cell).DefaultIfEmpty(-1).First() : -1;
        bool Blocked(int c) => (_blockedCells.TryGetValue(pid, out var b) && b.ContainsKey(c))
                               || (_fightObstacles.TryGetValue(pid, out var o) && o.ContainsKey(c));
        bool WalkFight(int c)
        {
            if ((uint)c >= walk.Length || !walk[c] || Blocked(c)) return false;
            if (_fighters.TryGetValue(pid, out var fl)) foreach (var x in fl) if (x.Cell == c && x.Cell != selfCell) return false;
            return true;
        }
        bool Step(int a, int b) => MapPathfinder.Communicates(a, b, floorF, zoneF, canMoveZoneF);
        long forge = _serverMapId.TryGetValue(pid, out var smid) && smid != 0 ? smid : mapId;
        return (WalkFight, Step, selfCell, forge);
    }

    /// <summary>Cellules atteignables ce tour (combat:reachableCells) : BFS ortho borné aux PM sur la marchabilité de combat.</summary>
    public System.Collections.Generic.IReadOnlyList<int> CombatReachable(uint pid)
    {
        if (BuildFightGeom(pid) is not { } g || g.SelfCell < 0) return System.Array.Empty<int>();
        int mp = SelfStats(pid)?.Pm ?? 0;
        return MapPathfinder.OrthoReachable(g.SelfCell, mp, g.Walk, CombatPolicyStep(pid, g.Step)).Keys.ToList();
    }

    /// <summary>Conditions SELF/GLOBAL d'un sort remplies CE tour ? (PA courants, n° de tour, nb d'ennemis vivants, PV% du lanceur).</summary>
    private bool SelfSpellGateOk(uint pid, SpellConfig cfg, int ap, int turnNo, int enemyCount)
        => Combat.CastLegality.SelfSpellGateOk(cfg.MinAp, cfg.MinTurn, cfg.MinEnemies, cfg.SelfHpMin, cfg.SelfHpMax, ap, turnNo, enemyCount, SelfHpPercent(pid));

    /// <summary>Condition PV de la CIBLE remplie pour ce sort ? (PV cible% dans [TgtHpMin, TgtHpMax]). PV inconnu -> true (fail-open).</summary>
    private bool TargetSpellGateOk(uint pid, SpellConfig cfg, long targetActorId)
        => Combat.CastLegality.TargetSpellGateOk(cfg.TgtHpMin, cfg.TgtHpMax, FighterHpPercent(pid, targetActorId));

    // Équipe d'un combattant suivi (jxg spawn.#2). null si inconnue.
    private int? TeamOf(uint pid, long actorId) =>
        _fighterTeam.TryGetValue(pid, out var t) && t.TryGetValue(actorId, out var team) ? (int?)team : null;

    // ENNEMI ? Priorité : (1) allié CONFIRMÉ par propriétaire (mon invocation) -> jamais ciblé ; (2) par ÉQUIPE quand
    // MON équipe ET la sienne sont connues;
    // (3) repli sur le signe (négatif = monstre/ennemi). Corrige l'attaque des invocations/compagnons ALLIÉS tout en
    // gardant les monstres — et un "Archiduk" ENNEMI (team adverse) reste bien une cible.
    private bool IsEnemy(uint pid, long actorId)
    {
        if (_controlledHistory?.ContainsKey((pid, actorId)) == true) return false;
        // Un ennemi DÉJÀ attaqué par le bot reste ennemi quoi qu'il arrive (prime sur tout) : empêche qu'un kae
        // tardif mal lu le fasse passer allié et aveugle le bot en plein combat.
        if (_confirmedEnemies.TryGetValue(pid, out var ce) && ce.ContainsKey(actorId)) return true;
        // ÉQUIPE d'ABORD (avant _allies) quand les DEUX équipes sont connues (jxg autoritaire) : ennemi = équipe ≠ la
        // mienne. C'est le signal FIABLE et ça neutralise un _allies EMPOISONNÉ — cas réel : en approchant le combat
        // d'un AUTRE joueur, son roster kae (avec un -1 parasite) était classé « mien » et latché le -1 en allié ;
        // le monstre (actorId -1, équipe adverse) restait alors « allié » à vie -> 0 ennemi -> tours passés.
        // Mes invocations n'ont PAS d'équipe lue (elles arrivent par jwe, pas jxg) -> TeamOf=null -> on tombe sur
        // _allies (classement par propriétaire) qui les protège. Aucune régression sur les alliés donc.
        if (TeamOf(pid, SelfActorId(pid)) is int mine && TeamOf(pid, actorId) is int his) return his != mine;
        if (_allies.TryGetValue(pid, out var a) && a.ContainsKey(actorId)) return false;
        return actorId < 0;
    }

    // Le bot a lancé un sort OFFENSIF sur ce combattant -> le confirme ennemi. Source FIABLE (le bot ne cible que des
    // IsEnemy=true) : sert de garde contre un reclassement allié erroné par un kae ultérieur.
    private void MarkEnemyConfirmed(uint pid, long actorId)
    {
        if (actorId == 0) return;
        _confirmedEnemies.GetOrAdd(pid, static _ => new ConcurrentDictionary<long, byte>())[actorId] = 0;
        NoteChallengeTarget(pid, actorId);   // défi Focus : 1er ennemi attaqué
    }

    private bool IsConfirmedEnemy(uint pid, long actorId) =>
        _confirmedEnemies.TryGetValue(pid, out var ce) && ce.ContainsKey(actorId);

    // MON actorId de combat. Un joueur est keyé dans `_fighters` par son characterId (jxg innerId #1 / kmk #3) et seuls les
    // MONSTRES sont négatifs -> l'id AUTHENTIFIÉ du login (_charId, capté sur kvi/kvw) est la source fiable.
    // Le max() des positifs n'est qu'un REPLI : dès qu'un 2e JOUEUR est dans le combat (multi-compte, groupe, joueur qui
    // rejoint), il désigne l'ALLIÉ s'il porte l'id le plus grand -> le jzc de l'ALLIÉ ouvrirait ma fenêtre (tout est droppé)
    // et MON jzc la fermerait (mine=false) => AFK total. En solo, les deux donnent le MÊME résultat (aucune régression).
    // ⚠ On n'IMPOSE pas _charId : s'il n'est pas PARMI les combattants suivis (jamais capté, attaché après le login,
    // re-sélection de perso), aucun jzc ne nommerait cet id, `_myTurnOpen` resterait faux à vie et le bot ne jouerait NI
    // n'enverrait jxy. On ne l'utilise donc QUE s'il est réellement présent dans le combat.
    private long SelfActorId(uint pid)
    {
        if (EngineControlled(pid) is { } controlled) return controlled.Fighter.ActorId;
        if (!_fighters.TryGetValue(pid, out var f)) return 0;   // hors combat : inchangé
        long me = _charId.TryGetValue(pid, out var c) ? c : 0;
        if (me > 0 && f.Any(x => x.ActorId == me)) return me;   // identité CONFIRMÉE parmi les combattants
        return f.Where(x => x.ActorId > 0).Select(x => x.ActorId).DefaultIfEmpty(0).Max();
    }

    private void UpsertFighterStats(uint pid, long id, FighterStats stats)
    {
        _fighterStats.GetOrAdd(pid, static _ => new ConcurrentDictionary<long, FighterStats>())[id] = stats;
        if (id == 0 || id == SelfActorId(pid)) FighterStatsUpdated?.Invoke(pid);
    }

    // Applique une perte de PV a un combattant (jwe #40). PV plancher a 0. Notifie l'UI si c'est moi.
    private void ApplyLifeLoss(uint pid, long target, int amount)
    {
        if (!_fighterStats.TryGetValue(pid, out var byId) || !byId.TryGetValue(target, out var s)) return;
        byId[target] = s with { Life = System.Math.Max(0, s.Life - amount) };
        if (target == SelfActorId(pid)) FighterStatsUpdated?.Invoke(pid);
    }

    // MAJ des PA/PM COURANTS d'un combattant (jxw). value = valeur absolue restante (le jeu renvoie base+modif ; il
    // remet la valeur pleine au debut du tour -> auto-reset). Notifie l'UI si c'est moi.
    private void ApplyPointUpdate(uint pid, long actor, bool isPa, int value)
    {
        if (!_fighterStats.TryGetValue(pid, out var byId) || !byId.TryGetValue(actor, out var s)) return;
        byId[actor] = isPa ? s with { Pa = value } : s with { Pm = value };
        if (actor == SelfActorId(pid))
        {
            if (ActiveScriptTurn(pid) is { } turn)
                lock (turn)
                {
                    if (isPa) { turn.ApLeft = System.Math.Max(0, value); turn.ApObservation++; }
                    else { turn.MpLeft = System.Math.Max(0, value); turn.MpObservation++; }
                }
            FighterStatsUpdated?.Invoke(pid);
        }
    }

    /// <summary>Sorts APPRIS du personnage (spell_id), decodes du SpellsEvent (hms). Vide tant que le paquet n'est pas recu.
    /// Repli : si SpellsEvent n'a pas encore été reçu pour ce PID, on prend les sorts de la classe depuis les données client.</summary>
    public IReadOnlyList<int> LearnedSpells(uint pid)
    {
        if (EngineControlled(pid) is { } controlled) return controlled.Fighter.Spells.Keys.ToArray();
        if (_charSpells.TryGetValue(pid, out var s) && s.Count > 0) return s;
        if (SelfCharacterInfo(pid)?.BreedIndex is int bi)
        {
            int breedId = ClassIconProvider.BreedId(bi);
            var breedSpells = _gameNames.SpellsForBreed(breedId);
            if (breedSpells.Count > 0) return breedSpells.Select(x => x.Id).ToArray();
        }
        return System.Array.Empty<int>();
    }

    // Profil de combat ACTIF par bot (pid). Pousse par l'UI (BotsViewModel) ; le moteur (RunFightTurn) en fait un
    // snapshot au debut de chaque tour. Absent ou AutoFight=false -> le bot ne joue pas automatiquement.
    private readonly ConcurrentDictionary<uint, CombatProfile> _combat = new();

    /// <summary>Definit/remplace le profil de combat d'un bot (edite dans l'onglet Combat). Copie defensive. Repercute
    /// aussi les mods natifs (no-anim / tactique) dans le fichier de controle lu par GivreMods.dll.</summary>
    public void SetCombatProfile(uint pid, CombatProfile profile)
    {
        // Droits serveur (audit P1-2 : gate au POINT D'ACTION, pas juste dans l'UI). Le profil STOCKÉ (lu par le moteur
        // RunFightTurn à chaque tour) est CLAMPÉ : sans le droit "fight", AutoFight est forcé à false -> le bot ne joue
        // AUCUN tour automatiquement, même si combat.json contient AutoFight=true ou si un combat démarre par aggro.
        var clone = profile.Clone();
        bool canFight = LicenseService.HasPermission(LicenseService.Perm.Fight);
        clone.AutoFight     = clone.AutoFight     && canFight;   // jouer les tours
        clone.AutoPreFight  = clone.AutoPreFight  && canFight;   // pré-combat (placement + prêt) = sous-fonction de "fight"
        clone.AutoChallenge = clone.AutoChallenge && canFight;   // challenge auto = sous-fonction de "fight"
        // Lot C2 : le profil UI devient la BASE ; le profil effectif = base + surcharges script (s'il y en a). Sans
        // surcharge, RecomputeCombat stocke `clone` lui-même -> strictement identique à avant.
        lock (_combatLock)
        {
            _combatBase[pid] = clone;
            RecomputeCombat(pid);
            PublishCombatProfileNative(pid, _combat[pid]);
        }
    }

    private void PublishCombatProfileNative(uint pid, CombatProfile profile)
    {
        // Mods natifs : ne pousser que si le droit correspondant est ACTIF -> un client patché forçant NoAnim/Tactical est stoppé ici, avant la DLL.
        // Turbo MITM combine la barrière réseau événementielle (SequenceEnd+self ACK, côté moteur) et les accélérations
        // de présentation skipper + collapse + instant-move pour le vrai client Dofus. Un pid Socket n'a pas de renderer natif.
        bool canNoAnim = LicenseService.HasPermission(LicenseService.Perm.NoAnim);
        bool turboMitm   = profile.Speed == CombatSpeed.Turbo && !IsSocketPid(pid);
        var presentation = CombatVisualModPolicy.Evaluate(
            MZP19LocalClone.Enabled, AppConfig.Current.ModsEnabled, canNoAnim,
            IsSocketPid(pid), pinnedSkipperInstalled:
                CombatVisualModPolicy.HasPinnedStandaloneSkipper(
                    System.IO.Path.Combine(AppContext.BaseDirectory, "NativeTurboRuntime")),
            independentlyVerifiedLegacyRenderer: false);
        var requested = CombatVisualModPolicy.Effective(presentation,
            profile.NoAnim || turboMitm, profile.Collapse || turboMitm,
            profile.InstantMove || turboMitm);
        bool noAnim      = requested.NoAnim;
        bool tactical    = CombatVisualOptions.TacticalRequested(profile.Tactical,
            AppConfig.Current.ModsEnabled, LicenseService.HasPermission(LicenseService.Perm.Tactical), IsSocketPid(pid));
        bool collapse    = requested.Collapse;
        bool instantMove = requested.InstantMove;
        ModControl.WriteForPid((int)pid, noAnim, tactical, collapse, instantMove);
        if ((profile.Collapse || profile.InstantMove || turboMitm) && !presentation.CanRequestInstantMove)
            TransportLog.Line($"[mod] pid {pid} : raccourcis d'attente/déplacement non admis : fournisseur natif indisponible ; aucun TP serveur généré");

        // GivreMods historique reste prioritaire s'il est réellement livré. Sinon, Turbo/NoAnim dispose d'un repli autonome
        // Animator2D: host natif injecté explicitement + LOAD/UNLOAD via le canal de contrôle propre à CE pid.
        bool nativeFallback = presentation.CanRequestNoAnim && (profile.NoAnim || turboMitm);
        bool itemIds = AppConfig.Current.ModsEnabled && AppConfig.Current.ItemIdOverlay && !IsSocketPid(pid);
        bool tacticalNative = AppConfig.Current.ModsEnabled && tactical && !IsSocketPid(pid);
        TransportLog.Line($"[mod] pid {pid} : profil noAnim={noAnim} tactique={tactical} collapse={collapse} deplInstant={instantMove} turbo={turboMitm} socket={IsSocketPid(pid)} modsOn={AppConfig.Current.ModsEnabled} canNoAnim={canNoAnim} -> hote natif={nativeFallback}");
        lock (_nativeDeferGate)
        {
            if (CharacterLoadGuard.HoldNativeUntilMap(CurrentMap(pid) is > 0 || _serverMapId.GetValueOrDefault(pid) > 0 || IsInFight(pid)))
            {
                _deferredNative[pid] = new DeferredNative(nativeFallback, turboMitm, itemIds, tacticalNative);
                TransportLog.Line($"[mod] pid {pid} : hôte natif et speedhack en attente de la carte");
                NativeTurboRuntime.RequestState((int)pid, enabled: false, combatSpeedHack: false, itemIds: itemIds, tactical: false,
                    caller: nameof(SetCombatProfile));
                return;
            }
            _deferredNative.TryRemove(pid, out _);
            NativeTurboRuntime.RequestState((int)pid, nativeFallback, turboMitm, itemIds: itemIds,
                tactical: tacticalNative, profileVisuals: true);
        }
    }

    internal void RecoverNativeAfterEnhanced(uint pid, long start, long generation)
    {
        lock (_combatLock)
        lock (_nativeDeferGate)
        lock (NativeTurboRuntime.StateGate)
        {
            if (!NativeTurboRuntime.CanRecoverEnhanced((int)pid, start, generation)
                || !_combat.TryGetValue(pid, out var profile)) return;
            PublishCombatProfileNative(pid, profile);
        }
    }
    /// <summary>Retire le profil de combat d'un bot (bot ferme) + nettoie son fichier de mods.</summary>
    public void ClearCombatProfile(uint pid)
    {
        lock (_combatLock)
        lock (_nativeDeferGate)
        {
            _deferredNative.TryRemove(pid, out _);
            NativeTurboRuntime.SetCombatActive((int)pid, false);
            NativeTurboRuntime.RequestState((int)pid, enabled: false);
            _combat.TryRemove(pid, out _); _combatBase.TryRemove(pid, out _); _combatOverrides.TryRemove(pid, out _);
            _teamFollower.TryRemove(pid, out _); ModControl.RemoveForPid((int)pid);
        }
    }

    // ── Lot C2 : réglages de combat posés par SCRIPT (combat:setStyle/setTargetMode/setChallengeMode…) ─────────────────
    // Couche EN MÉMOIRE au-dessus du profil UI : _combatBase = dernier profil poussé par l'UI (déjà clampé), _combatOverrides
    // = surcharges du script, _combat = base + surcharges (re-clampé). Une poussée UI ne les écrase donc pas. JAMAIS
    // sauvegardé dans combat.json. On ne MODIFIE JAMAIS en place l'instance stockée dans _combat (le moteur la clone en
    // concurrence) : on construit une nouvelle instance et on l'échange.
    private readonly object _combatLock = new();
    private readonly ConcurrentDictionary<uint, CombatProfile> _combatBase = new();
    private readonly ConcurrentDictionary<uint, ScriptCombatOverrides> _combatOverrides = new();

    // Appelant : détient _combatLock.
    private void RecomputeCombat(uint pid)
    {
        if (!_combatBase.TryGetValue(pid, out var b)) return;
        _combat[pid] = _combatOverrides.TryGetValue(pid, out var o) && !o.IsEmpty
            ? o.ApplyTo(b.Clone(), LicenseService.HasPermission(LicenseService.Perm.Fight)) : b;
    }

    /// <summary>Applique une modification de surcharge script (thread du script) puis recalcule le profil effectif.</summary>
    public void ScriptSetCombat(uint pid, Action<ScriptCombatOverrides> edit)
    { lock (_combatLock) { edit(_combatOverrides.GetOrAdd(pid, _ => new())); RecomputeCombat(pid); } }

    /// <summary>Retire toutes les surcharges script de ce bot (profil UI rétabli). false s'il n'y en avait pas.</summary>
    public bool ClearScriptCombatOverrides(uint pid)
    { lock (_combatLock) { if (!_combatOverrides.TryRemove(pid, out _)) return false; RecomputeCombat(pid); return true; } }

    /// <summary>Profil de combat EFFECTIF (copie), ou null si l'UI n'en a pas encore poussé.</summary>
    public CombatProfile? EffectiveCombatProfile(uint pid) => _combat.TryGetValue(pid, out var p) ? p.Clone() : null;
    public bool HasScriptCombatOverrides(uint pid) => _combatOverrides.TryGetValue(pid, out var o) && !o.IsEmpty;
    /// <summary>Le script a-t-il posé combat:setChallengeMode("script") ?</summary>
    public bool ScriptChallengeModeIsScript(uint pid) => _combatOverrides.TryGetValue(pid, out var o) && o.ChallengeByScript == true;
    /// <summary>Le sort est-il dans la rotation de BASE (profil UI) ? Pour combat:setSpellEnabled.</summary>
    public bool BaseHasSpell(uint pid, int spellId) => _combatBase.TryGetValue(pid, out var b) && b.Spells.Exists(s => s.SpellId == spellId);
    /// <summary>Override d'auto-combat posé par combat:setAutoFight (C1), null sans override.</summary>
    public bool? AutoFightOverride(uint pid) => _autoFightOverride.TryGetValue(pid, out var ov) ? ov.Value : null;
}
