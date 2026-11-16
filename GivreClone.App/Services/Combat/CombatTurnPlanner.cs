using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GivreClone.Protocol;
using GivreClone.App.Services.LotM;

namespace GivreClone.App.Services.Combat;

public sealed record PlannerCell(int Id, bool Walkable, bool BlocksLos, ImmutableArray<int> Neighbours);
public sealed record PlannerFighter(long Id, int Cell, bool Enemy, int Life, int MaxLife, int Shield = 0,
    int MovePoints = 3, int AttackRange = 6, int ExpectedDamage = 30, bool ThreatKnown = false,
    ImmutableDictionary<int, int>? Characteristics = null,
    FighterVisibility Visibility = FighterVisibility.Visible,
    FighterPositionCertainty PositionCertainty = FighterPositionCertainty.Confirmed,
    int MonsterTemplate = 0, long? SummonerId = null,
    ImmutableHashSet<int>? States = null, int? Breed = null, int? TurnAp = null,
    bool? StatesKnown = null, ImmutableHashSet<int>? AbsentStates = null, int MonsterGrade = 0,
    ImmutableArray<SpellCastDescriptor> OffensiveSpells = default)
{
    public ImmutableArray<SpellCastDescriptor> OffensiveSpells { get; init; } =
        OffensiveSpells.IsDefault ? ImmutableArray<SpellCastDescriptor>.Empty : OffensiveSpells;

    public bool CanTarget => Visibility == FighterVisibility.Visible
        && PositionCertainty == FighterPositionCertainty.Confirmed;

    public bool HasSameStateKnowledge(PlannerFighter other) =>
        (StatesKnown ?? States is not null) == (other.StatesKnown ?? other.States is not null)
        && (States ?? ImmutableHashSet<int>.Empty).SetEquals(other.States ?? ImmutableHashSet<int>.Empty)
        && (AbsentStates ?? ImmutableHashSet<int>.Empty).SetEquals(other.AbsentStates ?? ImmutableHashSet<int>.Empty);
}
public sealed record PlannerEscapeCost(int ApLost, int MpLost);
public sealed record PlannerZonePolicy(int Shape, int Size, int MinSize, int MinEnemyHits,
    bool RequireEnemySafety, ImmutableHashSet<long> ForbiddenEnemies);

/// <summary>All profile and live limits are copied, never read through mutable callbacks during search.</summary>
public sealed record PlannerSpell(SpellCastDescriptor Cast, int CastsLeft, int TargetCap,
    ImmutableDictionary<long, int> TargetCasts, ImmutableHashSet<long> AllowedTargets,
    bool AllowApproach = true, int MinAp = 0, int SelfHpMin = 0, int SelfHpMax = 100,
    int TargetHpMin = 0, int TargetHpMax = 100, int SupportHpThreshold = 60, int ConfigurationIndex = 0,
    ImmutableHashSet<int>? AllowedCells = null, int? ChallengeElement = null, int? MaxSummons = null,
    PlannerZonePolicy? ZonePolicy = null, int? RangeCap = null, int? CastsLimit = null,
    bool AllowNewAlliedSummons = false);

public sealed record CombatTurnSnapshot(
    ImmutableArray<PlannerCell> Cells, ImmutableArray<PlannerFighter> Fighters, long SelfId,
    int Ap, int Mp, ImmutableArray<PlannerSpell> Spells, ImmutableDictionary<int, int> Characteristics,
    CombatMovementPolicy MovementPolicy = CombatMovementPolicy.Standard, CombatStyle Style = CombatStyle.Aggressive,
    int MaxCasts = 3, ImmutableHashSet<int>? EndCells = null,
    ImmutableDictionary<int, PlannerEscapeCost>? EscapeCosts = null, bool TackleImmune = false,
    bool MobilityAllowed = true,
    // Caller must establish no movement-blocking states, carry, portals, marks or movement triggers.
    // Missing observations are not evidence that these interactions are absent.
    bool ForcedMovementSafe = false,
    // Below this life percentage (0 = off), expected incoming damage outweighs any attack reward.
    int FleeBelowHpPercent = 0,
    Func<int, int, SpellCastDescriptor?>? SpellLookup = null, int CasterLevel = 1,
    TeamIntentView? TeamView = null, bool SingleElement = false, int? LockedElement = null,
    bool FocusTargets = false, long FocusTarget = 0,
    ImmutableDictionary<int, SpellStateDamageRule>? StateDamageRules = null,
    // Server-observed mark cells (traps, glyphs, runes, portals) for the whole fight. Null = mark coverage unknown.
    ImmutableHashSet<int>? UnsafeForcedMovementCells = null,
    // Active portal network for Eliotrope spell redirection and damage amplification.
    ImmutableArray<ClassMechanics.Portal> Portals = default,
    bool EmptyStatBuffInventory = false, ImmutableHashSet<int>? AbsentStatBuffSources = null,
    ImmutableArray<DeferredSpellEffect> ObservedDeferredEffects = default,
    Func<int, int, int, SummonGradeCharacteristics?>? SummonLookup = null);

public enum PlannerActionKind { Move, Cast }
public sealed record PlannerAction(PlannerActionKind Kind, int Cell, int SpellId = 0, long Target = 0, int Ap = 0, int Mp = 0,
    int ConfigurationIndex = 0, bool ViaPortal = false)
{
    public override string ToString() => Kind == PlannerActionKind.Move ? $"move:{Cell}({Mp}MP)"
        : $"cast:{SpellId}@{Cell}({Ap}AP)";
}
public sealed record PlannerScore(double Damage, double Finishes, double Healing, double Shielding,
    double Incoming, double Position, double Uncertainty, double Control = 0, double FriendlyFire = 0)
{
    public double Total => Damage + Finishes + Healing + Shielding + Control - FriendlyFire - Incoming + Position - Uncertainty;
    public override string ToString() => $"total={Total:0.0} damage={Damage:0.0} finish={Finishes:0.0} heal={Healing:0.0}"
        + $" shield={Shielding:0.0} incoming={Incoming:0.0} position={Position:0.00} uncertainty={Uncertainty:0.0}"
        + $" control={Control:0.0} friendly-fire={FriendlyFire:0.0}";
}
public sealed record CombatTurnPlan(ImmutableArray<PlannerAction> Actions, PlannerScore Score, int Work, bool BudgetExhausted,
    TeamIntent? Intent = null, int Unmodeled = 0);

/// <summary>
/// Deterministic beam search: pre-cast walk, up to three casts, then a safety walk. Teleport is a real board transition.
/// One shared work budget covers reachability, casts AND enemy replies; no stopwatch-dependent pruning.
/// Unknown tackle losses forbid departure rather than inventing an AP budget. Enemy spell knowledge can be conservative.
/// </summary>
public static class CombatTurnPlanner
{
    public const int BeamWidth = 8, MaxDepth = 3, MaxWork = 180_000;
    private const double InvisibilityUtilityCap = 60;

    /// <summary>
    /// Classifies the simulated board without guessing a bomb from legacy template
    /// IDs 2831..2836. Client 3.7.13.17 establishes Explobombe template 3112;
    /// ownership, ally side, life and a confirmed observed cell are mandatory via
    /// ClassMechanics.IsRogueBomb. An unobserved or moved simulated summon does
    /// NOT gain position certainty merely by being present in SpellResolver.
    /// All consistently observed enemy-side fighters and new simulated enemies
    /// remain enemies, including legacy-template monsters and template 3112
    /// owned by a different combatant; a simulated ally->enemy flip is refused.
    /// This partition is NOT evidence that a bomb wall is active.
    /// </summary>
    public static (ImmutableArray<PlannerFighter> OwnedBombs, ImmutableArray<PlannerFighter> LiveEnemies)
        ClassifyRogueCombatants(CombatTurnSnapshot snapshot, SpellBoard board)
    {
        var uniqueObserved = snapshot.Fighters.GroupBy(f => f.Id)
            .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        var ambiguousBoardIds = board.Fighters.GroupBy(f => f.Id)
            .Where(g => g.Count() != 1).Select(g => g.Key).ToHashSet();
        var bombs = ImmutableArray.CreateBuilder<PlannerFighter>();
        var enemies = ImmutableArray.CreateBuilder<PlannerFighter>();
        foreach (var f in board.Fighters)
        {
            if (!f.Alive || f.Life <= 0) continue;
            // Resolver-only actors and actors whose simulated cell differs from
            // the observed location cannot assert a server-confirmed position.
            bool confirmedCell = uniqueObserved.TryGetValue(f.Id, out var observed)
                && observed.Cell == f.Cell && observed.Enemy == f.Enemy
                && observed.MonsterTemplate == f.MonsterTemplate
                && observed.SummonerId == f.SummonerId;
            var candidate = new PlannerFighter(f.Id, f.Cell, f.Enemy,
                (int)f.Life, (int)f.MaxLife, (int)f.Shield,
                Visibility: f.Visibility,
                PositionCertainty: confirmedCell ? observed!.PositionCertainty
                    : FighterPositionCertainty.LastKnown,
                MonsterTemplate: f.MonsterTemplate, SummonerId: f.SummonerId);
            if (!ambiguousBoardIds.Contains(f.Id)
                && ClassMechanics.IsRogueBomb(candidate, snapshot.SelfId))
                bombs.Add(candidate);
            // A resolver-side flag must not turn a previously observed ally
            // (including an owned bomb) into an enemy. Resolver-only enemy
            // actors are kept as enemies, as were non-bomb legacy monsters.
            if (f.Enemy && !ambiguousBoardIds.Contains(f.Id)
                && (observed is null || observed.Enemy))
                enemies.Add(candidate);
        }
        return (bombs.ToImmutable(), enemies.ToImmutable());
    }

    public static int SummonCapacity(IReadOnlyDictionary<int, int> characteristics, int? configuredMax = null)
    {
        int cap = configuredMax is int configured ? Math.Max(1, configured) : int.MaxValue;
        return characteristics.TryGetValue(CharIds.MaxSummonedCreatures, out int observed)
            ? Math.Min(cap, Math.Max(0, observed)) : cap;
    }

    public static bool IsAdmitted(SpellCastDescriptor spell) => !spell.Effects.IsDefaultOrEmpty
        && !SpellResolver.ResolverCoverage(spell).Any(e => e.ActionId == 406)
        && (string.IsNullOrWhiteSpace(spell.StatesCriterion)
            || System.Text.RegularExpressions.Regex.IsMatch(spell.StatesCriterion, @"\AHS[!=][0-9]+([&|]HS[!=][0-9]+)*\z"))
        && (spell.Effects.Any(SpellResolver.IsImmediateModeled)
            || spell.SpellId == 14577 && spell.Effects.Any(e => e.ActionId == 1160
                && e.Triggers == "D|XD" && e.DiceNum == 14578))
        && !spell.Effects.Any(e => SpellResolver.IsDamageAction(e.ActionId) && SpellResolver.RowProblem(e) is not null)
        && (spell.Effects.Any(e => SpellResolver.IsDamageAction(e.ActionId) && SpellResolver.IsImmediateModeled(e)
                && !e.TargetMask.Split(',').All(t => t.Trim() is "C" or "c" or "a" or "g"))
            || !spell.Effects.Any(e => e.Delay <= 0
                && (string.IsNullOrWhiteSpace(e.Triggers) || e.Triggers.Trim() == "I")
                && SpellResolver.RowProblem(e) is not null));

    /// <summary>AP/MP the caster should regain from an executed immediate self gain (111/120 AP, 128 MP).
    /// Execution still takes the minimum with server observations, so a refused gain is never invented.</summary>
    public static (int Ap, int Mp) ExpectedSelfPointGain(SpellCastDescriptor spell, bool targetedSelf,
        (int Caster, int Target)? cells = null)
    {
        if (cells is { } positions)
        {
            var points = spell with { Effects = spell.Effects.Where(e => e.ActionId is 111 or 120 or 128).ToImmutableArray() };
            var board = new SpellBoard(new[] { new SpellFighter(1, positions.Caster, false, 1, 1,
                StatesKnown: false, BreedKnown: false) });
            var result = SpellResolver.Resolve(board, 0, positions.Target, points, (_, _) => null, 1);
            return ((int)result.Events.OfType<PointsResolved>().Where(e => e.IsAp).Sum(e => e.Delta),
                (int)result.Events.OfType<PointsResolved>().Where(e => !e.IsAp).Sum(e => e.Delta));
        }
        int ap = 0, mp = 0;
        foreach (var e in spell.Effects.IsDefault ? ImmutableArray<SpellEffectDescriptor>.Empty : spell.Effects)
        {
            if (e.ActionId is not (111 or 120 or 128) || !SpellResolver.IsImmediateModeled(e)) continue;
            bool self = e.TargetMask.Split(',').Any(t => t.Trim() is "C") || targetedSelf && (e.Targets & SpellTargets.Self) != 0;
            if (!self) continue;
            int amount = (int)Math.Max(0, (e.Min + (double)Math.Max(e.Min, e.Max)) / 2);
            if (e.ActionId == 128) mp += amount; else ap += amount;
        }
        return (ap, mp);
    }

    public static bool HasModeledEnemyDamage(SpellCastDescriptor spell) => IsAdmitted(spell)
        && spell.Effects.Any(e => SpellResolver.IsDamageAction(e.ActionId) && SpellResolver.IsImmediateModeled(e)
            && !e.TargetMask.Split(',').All(t => t.Trim() is "C" or "c" or "a" or "g"));

    public static bool CanHealAlliesWithoutFriendlyDamage(SpellCastDescriptor spell) =>
        spell.Effects.Any(e => e.Kind == SpellEffectKind.Heal && SpellResolver.IsImmediateModeled(e)
            && e.TargetMask.Split(',', StringSplitOptions.TrimEntries).Any(t => t is "a" or "g"))
        && spell.Effects.Where(e => SpellResolver.IsDamageAction(e.ActionId))
            .All(e => e.TargetMask.Split(',', StringSplitOptions.TrimEntries).All(t => t == "A"));

    public static (int Ap, int Mp) ExpectedSelfPointGain(CombatTurnSnapshot snapshot, SpellCastDescriptor spell, int targetCell,
        bool viaPortal = false)
    {
        int self = snapshot.Fighters.FindIndex(f => f.Id == snapshot.SelfId);
        var before = ObservedBoard(snapshot);
        int? areaOrigin = null;
        if (viaPortal)
        {
            var portals = snapshot.Portals.IsDefaultOrEmpty ? Array.Empty<int>() : snapshot.Portals
                .Where(p => p.Active).Select(p => p.Cell).ToArray();
            if (ClassMechanics.ProjectThroughPortals(snapshot.Fighters[self].Cell, targetCell, portals) is not { } shot)
                return (0, 0);
            targetCell = shot.Impact;
            areaOrigin = shot.Exit;
            before = before with { ThroughPortal = true };
        }
        before = before with { Fighters = before.Fighters.Select((f, i) => i == self
            ? f with { Ap = f.Ap - spell.Ap } : f).ToImmutableArray() };
        var resolved = SpellResolver.Resolve(before, self, targetCell, spell,
            snapshot.SpellLookup ?? ((_, _) => null), snapshot.CasterLevel,
            c => snapshot.Cells.Any(tile => tile.Id == c && tile.Walkable), areaOrigin);
        var gains = resolved.Events.OfType<PointsResolved>().Where(e => e.TargetId == snapshot.SelfId);
        return ((int)Math.Max(0, gains.Where(e => e.IsAp).Sum(e => e.Delta)),
            (int)Math.Max(0, gains.Where(e => !e.IsAp).Sum(e => e.Delta)));
    }

    private static SpellBoard ObservedBoard(CombatTurnSnapshot snapshot)
    {
        return new SpellBoard(snapshot.Fighters.Select(f =>
        {
            int Stat(int id) => f.Characteristics?.GetValueOrDefault(id) ?? 0;
            var resistances = new Dictionary<DamageElement, int>
            {
                [DamageElement.Neutral] = Stat(CharIds.ResistNeutral), [DamageElement.Earth] = Stat(CharIds.ResistEarth),
                [DamageElement.Fire] = Stat(CharIds.ResistFire), [DamageElement.Water] = Stat(CharIds.ResistWater),
                [DamageElement.Air] = Stat(CharIds.ResistAir)
            }.ToImmutableDictionary();
            var reductions = new Dictionary<DamageElement, int>
            {
                [DamageElement.Neutral] = Stat(CharIds.ReductionNeutral), [DamageElement.Earth] = Stat(CharIds.ReductionEarth),
                [DamageElement.Fire] = Stat(CharIds.ReductionFire), [DamageElement.Water] = Stat(CharIds.ReductionWater),
                [DamageElement.Air] = Stat(CharIds.ReductionAir)
            }.ToImmutableDictionary();
            bool self = f.Id == snapshot.SelfId;
            return new SpellFighter(f.Id, f.Cell, f.Enemy, f.Life, f.MaxLife, f.Shield,
                self ? snapshot.Characteristics : f.Characteristics, resistances, reductions,
                Ap: self ? snapshot.Ap : f.TurnAp ?? 12, Mp: self ? snapshot.Mp : f.MovePoints,
                MonsterTemplate: f.MonsterTemplate, SummonerId: f.SummonerId, MonsterGrade: f.MonsterGrade,
                States: f.States, StatesKnown: f.StatesKnown ?? f.States is not null, AbsentStates: f.AbsentStates,
                Breed: f.Breed ?? 0, BreedKnown: f.Breed.HasValue, Visibility: f.Visibility);
        }).ToImmutableArray(), Marks: snapshot.Portals.IsDefaultOrEmpty ? null : snapshot.Portals
            .Where(p => p.Active || snapshot.Portals.Length == 1)
            .Select(p => new SpellMark(1181, 2, p.OwnerId, new[] { p.Cell },
                AwaitingPortalPair: !p.Active)).ToImmutableArray(),
            StateDamageRules: snapshot.StateDamageRules,
            KnownStatBuffSources: snapshot.AbsentStatBuffSources,
            AllStatBuffSourcesKnown: snapshot.EmptyStatBuffInventory,
            DeferredEffects: snapshot.ObservedDeferredEffects.IsDefault
                ? ImmutableArray<DeferredSpellEffect>.Empty : snapshot.ObservedDeferredEffects,
            SummonLookup: snapshot.SummonLookup);
    }

    private sealed record Node(int Cell, int Ap, int Mp, ImmutableArray<int> Positions, ImmutableArray<int> Life, ImmutableArray<int> Shield,
        ImmutableDictionary<int, int> Casts, ImmutableDictionary<(int, long), int> Targets,
        ImmutableArray<PlannerAction> Actions, double Damage = 0, double Finishes = 0,
        double Healing = 0, double Shielding = 0, int Depth = 0, bool Repositioned = false,
        SpellBoard? Board = null, double Control = 0, double FriendlyFire = 0, double Uncertainty = 0,
        int Unmodeled = 0, int? Element = null, long FocusTarget = 0, double InvisibilityUtility = 0,
        ImmutableDictionary<long, double>? DodgeableMpLoss = null);
    private sealed record Walk(int Cell, int Ap, int Mp, int Steps, ImmutableArray<int> Path,
        SpellBoard? Board = null, double Healing = 0);
    private sealed class Budget
    {
        public int Used;
        public int Limit = MaxWork;
        public bool Take(int limit = MaxWork) { if (Used >= Math.Min(limit, Limit)) return false; Used++; return true; }
    }

    public static CombatTurnPlan Choose(CombatTurnSnapshot snapshot, int maxWork = MaxWork)
    {
        var plan = ChooseConstrained(snapshot, maxWork);
        // Live 04/11 23:30 (Collant): no compliant end cell was reachable, so the plan stayed []
        // with 13 AP beside enemies and the turn ended without a cast. When no reachable cell
        // satisfies the challenge it is lost whatever we do: plan the turn without it. A reachable
        // compliant cell keeps the challenge binding, even at the cost of damage.
        if (snapshot.EndCells is { } required && plan.Intent is { } intent && !required.Contains(intent.EndCell))
            return ChooseConstrained(snapshot with { EndCells = null }, maxWork);
        // A reachable walking endpoint must not consume the entire turn when
        // every productive action makes that endpoint unaffordable (tackle AP).
        // Keep a productive compliant plan; otherwise prefer an available hit.
        if (snapshot.EndCells is not null
            && snapshot.EscapeCosts?.GetValueOrDefault(snapshot.Fighters.First(f => f.Id == snapshot.SelfId).Cell)?.ApLost > 0
            && plan.Score.Damage == 0 && plan.Score.Finishes == 0
            && plan.Score.Healing == 0 && plan.Score.Shielding == 0 && plan.Score.Control == 0)
        {
            var attackPlan = ChooseConstrained(snapshot with { EndCells = null }, maxWork);
            if (attackPlan.Score.Damage > 0) return attackPlan;
        }
        return plan;
    }

    private static CombatTurnPlan ChooseConstrained(CombatTurnSnapshot snapshot, int maxWork)
    {
        // Bound input sizes as well as expansions; the board is the game's fixed 560-cell grid.
        if (snapshot.Cells.Length > MapPathfinder.CellCount || snapshot.Fighters.Length > 64 || snapshot.Spells.Length > 128)
            throw new ArgumentOutOfRangeException(nameof(snapshot));
        var cells = snapshot.Cells.ToDictionary(c => c.Id);
        int self = snapshot.Fighters.FindIndex(f => f.Id == snapshot.SelfId);
        if (self < 0 || !cells.ContainsKey(snapshot.Fighters[self].Cell))
            throw new ArgumentException("Snapshot must contain the caster and its cell.", nameof(snapshot));
        var budget = new Budget { Limit = Math.Clamp(maxWork, 1, MaxWork) };
        var root = new Node(snapshot.Fighters[self].Cell, Math.Max(0, snapshot.Ap), Math.Max(0, snapshot.Mp),
            snapshot.Fighters.Select(f => f.Cell).ToImmutableArray(),
            snapshot.Fighters.Select(f => f.Life).ToImmutableArray(), snapshot.Fighters.Select(f => f.Shield).ToImmutableArray(),
            ImmutableDictionary<int, int>.Empty, ImmutableDictionary<(int, long), int>.Empty, ImmutableArray<PlannerAction>.Empty);
        root = root with { Element = snapshot.LockedElement, FocusTarget = snapshot.FocusTarget,
            Board = ObservedBoard(snapshot) };
        var spells = snapshot.Spells.Where(s => IsAdmitted(s.Cast) && s.Cast.Ap > 0
                && (s.CastsLeft > 0 || s.CastsLimit > 0))
            .OrderBy(s => s.Cast.SpellId).ToArray();
        var untargetable = snapshot.Fighters.Where(f => !f.CanTarget).Select(f => f.Id).ToHashSet();
        int Position(Node n, int i) => i == self ? n.Cell : n.Positions[i];
        bool Occupied(Node n, int c) => snapshot.Fighters.Where((f, i) => i != self && n.Life[i] > 0 && Position(n, i) == c).Any()
            || n.Board!.Fighters.Skip(snapshot.Fighters.Length).Any(f => f.Alive && f.Life > 0 && f.Cell == c);
        bool Los(int a, int b, Func<int, bool> occupied) => MapPathfinder.CellDistance(a, b) <= 1
            || MapPathfinder.HasLineOfSight(a, b, c => !cells.TryGetValue(c, out var v) || v.BlocksLos, occupied);
        int Nearest(Node n, int c) => snapshot.Fighters.Select((f, i) => (f, i)).Where(t => t.f.Enemy && n.Life[t.i] > 0)
            .Select(t => MapPathfinder.CellDistance(c, Position(n, t.i))).DefaultIfEmpty(0).Min();

        // A conservative reply map: enemy movement ignores fighters, so killing a blocker never exposes unscored danger.
        // Walls, sector edges and firing LoS still matter. Unknown enemy spell ranges/damage are labelled, never zero.
        var threat = new Dictionary<(long Id, int Cell, int Mp), Dictionary<int, int>>();
        bool threatTruncated = false;
        Dictionary<int, int> ReplyMap(PlannerFighter enemy, int originCell, int mp)
        {
            mp = Math.Clamp(mp, 0, 12);
            if (threat.TryGetValue((enemy.Id, originCell, mp), out var cached)) return cached;
            var reach = new Dictionary<int, int> { [originCell] = 0 };
            var queue = new Queue<int>(); queue.Enqueue(originCell);
            while (queue.Count > 0 && budget.Take(budget.Limit / 2))
            {
                int c = queue.Dequeue();
                if (reach[c] >= mp || !cells.TryGetValue(c, out var tile)) continue;
                foreach (int next in tile.Neighbours)
                    if (cells.TryGetValue(next, out var nc) && nc.Walkable && !reach.ContainsKey(next))
                    { reach[next] = reach[c] + 1; queue.Enqueue(next); }
            }
            bool reachComplete = queue.Count == 0;
            threatTruncated |= !reachComplete;
            var danger = new Dictionary<int, int>();
            foreach (var tile in snapshot.Cells)
            {
                bool hit = !reachComplete || !enemy.CanTarget;
                if (!hit) foreach (int origin in reach.Keys)
                {
                    if (!budget.Take(budget.Limit / 2)) { hit = true; threatTruncated = true; break; }
                    if (MapPathfinder.CellDistance(origin, tile.Id) <= enemy.AttackRange && Los(origin, tile.Id, _ => false))
                    { hit = true; break; }
                }
                if (hit) danger[tile.Id] = Math.Max(0, enemy.ExpectedDamage);
            }
            threat[(enemy.Id, originCell, mp)] = danger;
            return danger;
        }
        foreach (var enemy in snapshot.Fighters.Where(f => f.Enemy)) ReplyMap(enemy, enemy.Cell, enemy.MovePoints);
        // Recompute displaced replies from their actual origin under the same work budget.
        // An incomplete map still charges full damage rather than inventing a safe landing.
        double EnemyReply(Node n, int e, int c)
        {
            var f = snapshot.Fighters[e];
            if (f.TurnAp == 0) return 0;
            // A simulated dodgeable loss is potential control, not guaranteed safety.
            // Observed losses are already in the snapshot and are not added back.
            double replyMp = n.Board!.Fighters[e].Mp + (n.DodgeableMpLoss?.GetValueOrDefault(f.Id) ?? 0);
            double direct = ReplyMap(f, Position(n, e), (int)Math.Ceiling(replyMp)).GetValueOrDefault(c);
            var portals = PortalCells(n);
            if (portals.Count >= 2 && f.ExpectedDamage > direct && !f.OffensiveSpells.IsDefaultOrEmpty)
            {
                foreach (int entry in portals)
                {
                    if (!budget.Take()) { direct = f.ExpectedDamage; break; }
                    int origin = Position(n, e);
                    if (Occupied(n, entry)
                        || ClassMechanics.ProjectThroughPortals(origin, entry, portals) is not { } shot
                        || shot.Impact != c) continue;
                    if (f.OffensiveSpells.Any(sp => HasModeledEnemyDamage(sp) && sp.Ap <= n.Board.Fighters[e].Ap
                        && !sp.NeedTakenCell && string.IsNullOrWhiteSpace(sp.StatesCriterion)
                        && (!sp.CastInLine || MapPathfinder.Aligned(origin, entry))
                        && (!sp.CastInDiagonal || MapPathfinder.Diagonal(origin, entry))
                        && MapPathfinder.CellDistance(origin, entry) >= sp.MinRange
                        && MapPathfinder.CellDistance(origin, entry) <= sp.Range
                        && (!sp.TestLos || Los(origin, entry, cell => cell != origin && cell != entry && Occupied(n, cell))
                            && Los(shot.Exit, c, cell => cell != shot.Exit && cell != c && Occupied(n, cell)))))
                    { direct = f.ExpectedDamage; break; }
                }
            }
            return direct
                * Math.Clamp(n.Board.Fighters[e].Ap / (double)(f.TurnAp ?? 12), 0, 1);
        }
        double Exposure(Node n, int c) => snapshot.Fighters.Select((f, i) => (f, i))
            .Where(t => t.f.Enemy && n.Life[t.i] > 0).Sum(t => EnemyReply(n, t.i, c));
        double Survival(int life, int max) => max > 0 && life * 100L < max * 35L ? 2.5 : 1;
        var partnerPotentials = new Dictionary<Node, double>();
        double PartnerPotential(Node n)
        {
            if (partnerPotentials.TryGetValue(n, out double cached)) return cached;
            double total = 0;
            for (int ally = 0; ally < n.Board!.Fighters.Count; ally++)
            {
                var fighter = n.Board.Fighters[ally];
                if (ally == self || fighter.Enemy || !fighter.Alive || fighter.Life <= 0) continue;
                var observed = ally < snapshot.Fighters.Length ? snapshot.Fighters[ally] : null;
                if (observed is not null && !observed.CanTarget) continue;
                var attacks = observed?.OffensiveSpells ?? ImmutableArray<SpellCastDescriptor>.Empty;
                if (attacks.IsDefaultOrEmpty && fighter.SummonerId == snapshot.SelfId
                    && snapshot.SummonLookup?.Invoke(fighter.MonsterTemplate, fighter.MonsterGrade, snapshot.CasterLevel)?.Spells is { } summonSpells)
                    attacks = summonSpells.Select(s => snapshot.SpellLookup?.Invoke(s.SpellId, s.Grade))
                        .Where(s => s is not null && (HasModeledEnemyDamage(s) || CanHealAlliesWithoutFriendlyDamage(s)))
                        .Select(s => s!).ToImmutableArray();
                if (attacks.IsDefaultOrEmpty) continue;
                double best = 0;
                foreach (var spell in attacks)
                {
                    if (spell.Ap > (observed?.TurnAp ?? fighter.Ap)) continue;
                    for (int enemy = 0; enemy < n.Board.Fighters.Count; enemy++)
                    {
                        if (!budget.Take()) return total;
                        var target = n.Board.Fighters[enemy];
                        var observedTarget = enemy < snapshot.Fighters.Length ? snapshot.Fighters[enemy] : null;
                        if (observedTarget is not null && !observedTarget.CanTarget || target.Life <= 0
                            || target.Enemy && observedTarget is not null && Reserved(enemy)) continue;
                        if (target.Enemy ? !HasModeledEnemyDamage(spell) : !CanHealAlliesWithoutFriendlyDamage(spell)) continue;
                        int from = observed is null ? fighter.Cell : Position(n, ally);
                        int to = observedTarget is null ? target.Cell : Position(n, enemy);
                        int distance = MapPathfinder.CellDistance(from, to);
                        if (distance < spell.MinRange || distance > spell.Range
                            || spell.CastInLine && !MapPathfinder.Aligned(from, to)
                            || spell.CastInDiagonal && !MapPathfinder.Diagonal(from, to)
                            || spell.NeedFreeCell
                            || spell.TestLos && !Los(from, to, c => c != from && c != to && (c == n.Cell || Occupied(n, c)))) continue;
                        var before = n.Board! with { Fighters = n.Board!.Fighters.Select((fighter, i) =>
                            i == self ? fighter with { Cell = n.Cell } : fighter).ToImmutableArray() };
                        var resolved = SpellResolver.Resolve(before, ally, to, spell,
                            snapshot.SpellLookup ?? ((_, _) => null), snapshot.CasterLevel);
                        // One discounted legal action per ally: future healing is not immediate healing.
                        if (resolved.FriendlyFire == 0 && resolved.Unmodeled.Count == 0)
                            best = Math.Max(best, (resolved.EnemyDamage + resolved.Board.Fighters
                                .Take(before.Fighters.Count).Select((f, i) => f.Enemy ? 0
                                    : Math.Max(0, f.Life - before.Fighters[i].Life)).Sum()) * .5);
                    }
                }
                total += best;
            }
            partnerPotentials[n] = total;
            return total;
        }
        double initialPartnerPotential = PartnerPotential(root);
        long? exposed = snapshot.TeamView?.MostExposedAlly(snapshot.Fighters.Where(f => !f.Enemy)
            .Select(f => new TeamAllyExposure(f.Id, f.Life, f.MaxLife, (int)Exposure(root, f.Cell), f.Shield)));
        // Average damage of the best single modeled attack, and how far it reaches next turn (own full MP + spell range).
        // Discounted: the enemy moves before that turn, so this is a tie-breaker between equally safe ends, not a plan.
        const double NextTurnWeight = .5;
        var firingDistances = new Dictionary<int, int>();
        var attackSpells = snapshot.Spells.Where(s => s.AllowApproach && s.CastsLeft > 0
            && HasModeledEnemyDamage(s.Cast)).Select(s => (Spell: s,
                Reach: s.Cast.Range + Math.Clamp(snapshot.Fighters[self].MovePoints, 0, 12),
                Effects: s.Cast.Effects.Where(e => SpellResolver.IsDamageAction(e.ActionId)
                    && SpellResolver.IsImmediateModeled(e)
                    && !e.TargetMask.Split(',', StringSplitOptions.TrimEntries).All(t => t is "C" or "c" or "a" or "g"))
                    .ToArray())).ToArray();
        // A teleport is never spent on this speculative bonus alone: its cooldown is worth more than a guessed setup.
        var teleportIds = snapshot.Spells.Where(s => s.Cast.IsTeleport).Select(s => s.Cast.SpellId).ToHashSet();
        // Graded, not a flat in-reach bonus: live 02/11 21:48 a flat bonus let both bots wander at 6-8 cells for 4 turns,
        // every end "in reach" scoring the same while the enemy stepped back. The closer the nearest target, the surer the hit.
        double NextTurnPotential(Node n)
        {
            if (n.Actions.Any(a => a.Kind == PlannerActionKind.Cast && teleportIds.Contains(a.SpellId))) return 0;
            // A cast with no useful result must not unlock the flat hit-and-run
            // reward merely by spending AP. Setup still competes through follow-ups.
            bool productive = n.Damage > 0 || n.Finishes > 0 || n.Healing > 0 || n.Shielding > 0 || n.Control > 0;
            var nextBoard = SpellResolver.AdvanceStatBuffs(n.Board!);
            double best = 0;
            foreach (var attack in attackSpells)
            {
                if (!HpGate(n.Life[self], snapshot.Fighters[self].MaxLife,
                    attack.Spell.SelfHpMin, attack.Spell.SelfHpMax)) continue;
                if (snapshot.SingleElement && n.Element is int locked
                    && ChallengeElement(n, attack.Spell) is int element && element != locked) continue;
                for (int i = 0; i < snapshot.Fighters.Length; i++)
                {
                    var f = snapshot.Fighters[i];
                    if (f.Enemy && f.CanTarget && n.Board!.Fighters[i].Visibility == FighterVisibility.Visible
                        && n.Life[i] > 0 && attack.Spell.AllowedTargets.Contains(f.Id)
                        && HpGate(n.Life[i], f.MaxLife, attack.Spell.TargetHpMin, attack.Spell.TargetHpMax))
                    {
                        int distance = MapPathfinder.CellDistance(n.Cell, Position(n, i));
                        if (distance > attack.Reach) continue;
                        var caster = nextBoard.Fighters[self];
                        var target = nextBoard.Fighters[i];
                        if (snapshot.StateDamageRules is { } rules
                            && ((target.States?.Any(state => rules.TryGetValue(state, out var rule) && rule.Invulnerable) ?? false)
                                || (caster.States?.Any(state => rules.TryGetValue(state, out var rule) && rule.CantDealDamage) ?? false))) continue;
                        var stats = caster.Characteristics ?? snapshot.Characteristics;
                        double damage = attack.Effects.Sum(e =>
                        {
                            foreach (var maskToken in e.TargetMask.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            {
                                var fighter = maskToken.StartsWith('*') ? caster : target;
                                string token = maskToken.TrimStart('*');
                                if (token.Length > 1 && token[0] is 'E' or 'e'
                                    && int.TryParse(token.AsSpan(1), out int state))
                                {
                                    bool present = fighter.States?.Contains(state) == true;
                                    bool known = fighter.StatesKnown || present || fighter.AbsentStates?.Contains(state) == true;
                                    if (!known || (token[0] == 'E' ? !present : present)) return 0;
                                }
                            }
                            var element = e.ActionId switch
                            {
                                91 or 96 => DamageElement.Water, 92 or 97 => DamageElement.Earth,
                                93 or 98 => DamageElement.Air, 94 or 99 => DamageElement.Fire,
                                2822 or 2828 or 2832 or 2890 => DamageEstimator.ElementOf(e, stats),
                                _ => DamageElement.Neutral
                            };
                            double amount = (e.Min + e.Max) / 2.0;
                            double raw = e.ActionId switch
                            {
                                82 or 144 => amount,
                                89 => caster.Life * amount / 100,
                                279 => (caster.MaxLife - caster.Life) * amount / 100,
                                _ => 0
                            };
                            return e.ActionId is 82 or 144 or 89 or 279
                                ? Math.Max(0, raw * (1 - Math.Clamp(target.Resistances?.GetValueOrDefault(element) ?? 0, -100, 100) / 100.0)
                                    - (target.Reductions?.GetValueOrDefault(element) ?? 0))
                                : DamageEstimator.Estimate(e.Min, e.Max, element, stats,
                                    new DamageTarget(target.Resistances, target.Reductions)).Average;
                        });
                        // Speculative overkill must not outscore finishing the last living target now.
                        best = Math.Max(best, Math.Min(damage, n.Life[i] + n.Shield[i]) * NextTurnWeight
                            * (productive ? 1 : (attack.Reach + 1 - distance) / (attack.Reach + 1.0)));
                    }
                }
            }
            if (!productive && firingDistances.TryGetValue(n.Cell, out int steps))
                best = 20.0 / (steps + 1);
            return best;
        }
        PlannerScore Score(Node n)
        {
            double startTurnDamage = 0, startTurnHealing = 0;
            if (snapshot.SpellLookup is { } lookup && n.Board!.DeferredEffects is { Count: > 0 } pending)
            {
                foreach (long actor in pending.Where(p => p.TargetId.HasValue
                    && (p.SpellId == 29098 && p.Triggers == "TB" && p.Effect.ActionId is 96 or 2998
                        || p.SpellId == 14594 && p.Triggers == "TE" && p.Effect.ActionId == 99))
                    .Select(p => p.TargetId!.Value).Distinct())
                {
                    if (!budget.Take()) break;
                    var future = SpellResolver.ResolveStartTurn(n.Board, actor, lookup, snapshot.CasterLevel);
                    var end = SpellResolver.ResolveEndTurn(future.Board, actor, lookup, snapshot.CasterLevel);
                    // Delayed damage is discounted; it neither earns an immediate finish nor removes a reply.
                    startTurnDamage += (future.EnemyDamage + end.EnemyDamage) * .5;
                    startTurnHealing += future.Events.OfType<HealingResolved>()
                        .Where(e => n.Board.Fighters.Any(f => f.Id == e.TargetId && !f.Enemy))
                        .Sum(e => e.Amount) * .5;
                }
            }
            // Each enemy's reply turn is charged ONCE, on the ally it hurts most: one monster does not deal its full turn to
            // every ally at once. Live 02/11 20:33: charging it per ally doubled incoming in a duo and turned attacks into flight.
            var allyLoss = new Dictionary<int, double>();
            for (int e = 0; e < snapshot.Fighters.Length; e++)
            {
                if (!snapshot.Fighters[e].Enemy || n.Life[e] <= 0) continue;
                int target = -1; double worst = 0;
                for (int i = 0; i < snapshot.Fighters.Length; i++)
                {
                    var f = snapshot.Fighters[i];
                    if (f.Enemy || n.Life[i] <= 0) continue;
                    double hit = EnemyReply(n, e, Position(n, i)) * Survival(n.Life[i], f.MaxLife);
                    if (hit > worst) { worst = hit; target = i; }
                }
                if (target >= 0) allyLoss[target] = allyLoss.GetValueOrDefault(target) + EnemyReply(n, e, Position(n, target));
            }
            double incoming = 0;
            foreach (var (i, raw) in allyLoss)
            {
                var f = snapshot.Fighters[i];
                double loss = Math.Max(0, raw - n.Shield[i]);
                incoming += loss * Survival(n.Life[i], f.MaxLife);
                if (loss >= n.Life[i]) incoming += 100;
            }
            double uncertain = snapshot.Fighters.Count(f => f.Enemy && !f.ThreatKnown) * 2;
            var me = snapshot.Fighters[self];
            bool fleeing = snapshot.FleeBelowHpPercent > 0 && me.MaxLife > 0
                && n.Life[self] * 100L < (long)me.MaxLife * snapshot.FleeBelowHpPercent;
            if (fleeing) incoming *= 4;
            return new(n.Damage + startTurnDamage, n.Finishes, n.Healing + startTurnHealing, n.Shielding + n.InvisibilityUtility, incoming,
                // Reward hit-and-run spacing after damage, not a retreat that
                // neither reduces the modeled reply nor enables an attack.
                Math.Min(20, Nearest(n, n.Cell)) * (fleeing ? .5
                    : snapshot.Style == CombatStyle.Fleeing || n.Damage > 0
                        && snapshot.Fighters.Where((f, i) =>
                            f.Enemy && n.Life[i] > 0 && f.ExpectedDamage > 0 && f.TurnAp != 0).Any() ? .02 : 0)
                    + (!fleeing && snapshot.Style == CombatStyle.Aggressive ? NextTurnPotential(n) : 0)
                    - n.Actions.Sum(a => a.Mp) * .001
                    - TeamBlocking(n) + PartnerPotential(n) - initialPartnerPotential,
                uncertain + n.Uncertainty, n.Control, n.FriendlyFire);
        }
        bool MoveAllowed(Node n, int from, int to) => snapshot.Style != CombatStyle.Passive
            && CombatMovementDecision.CanMove(snapshot.MovementPolicy, Nearest(n, from), Nearest(n, to));
        IEnumerable<Walk> Reach(Node n)
        {
            var first = new Walk(n.Cell, n.Ap, n.Mp, 0, ImmutableArray<int>.Empty);
            var queue = new Queue<Walk>(); queue.Enqueue(first);
            var labels = new Dictionary<int, List<(int Ap, int Mp)>> { [n.Cell] = new() { (n.Ap, n.Mp) } };
            while (queue.Count > 0 && budget.Take())
            {
                var at = queue.Dequeue();
                yield return at;
                if (at.Mp <= 0 || !cells.TryGetValue(at.Cell, out var tile)) continue;
                int lostAp = 0, lostMp = 0;
                var adjacent = snapshot.TackleImmune ? Array.Empty<int>() : Enumerable.Range(0, snapshot.Fighters.Length)
                    .Where(i => snapshot.Fighters[i].Enemy && n.Life[i] > 0
                        && MapPathfinder.CellDistance(at.Cell, Position(n, i)) == 1).ToArray();
                // A last-known (unconfirmed) cell alone is not a guaranteed tackle block: charge an
                // observed escape cost when one exists, otherwise allow the departure. Confirmed
                // adjacency without observed costs still forbids leaving.
                bool confirmedAdjacent = adjacent.Any(i => snapshot.Fighters[i].PositionCertainty == FighterPositionCertainty.Confirmed);
                if (adjacent.Length > 0 && !confirmedAdjacent)
                {
                    if (!snapshot.Fighters.Where((f, i) => f.Enemy && Position(n, i) != f.Cell).Any()
                        && snapshot.EscapeCosts?.TryGetValue(at.Cell, out var observed) == true)
                    { lostAp = Math.Max(0, observed.ApLost); lostMp = Math.Max(0, observed.MpLost); }
                }
                else if (confirmedAdjacent)
                {
                    // Observed escape costs belong to the original enemy layout only.
                    if (snapshot.Fighters.Where((f, i) => f.Enemy && Position(n, i) != f.Cell).Any()) continue;
                    if (snapshot.EscapeCosts is null || !snapshot.EscapeCosts.TryGetValue(at.Cell, out var cost)) continue;
                    lostAp = Math.Max(0, cost.ApLost); lostMp = Math.Max(0, cost.MpLost);
                }
                int ap = at.Ap - lostAp, mp = at.Mp - lostMp - 1;
                if (ap < 0 || mp < 0) continue;
                foreach (int next in tile.Neighbours.Order())
                {
                    if (!budget.Take()) yield break;
                    var portals = PortalCells(n);
                    if (portals.Contains(next))
                    {
                        // Stop this walking prefix at the entry: execution must
                        // confirm the teleport before sending another path.
                        if (!MoveAllowed(n, at.Cell, next)
                            || ClassMechanics.ProjectThroughPortals(next, next, portals) is not { } route
                            || !cells.TryGetValue(route.Exit, out var exit) || !exit.Walkable
                            || Occupied(n, next) || Occupied(n, route.Exit)
                            || !MoveAllowed(n, at.Cell, route.Exit)
                            || n.Board!.Marks?.Any(m => m.ActionId is not (1181 or 1183)
                                && (m.Cells.Contains(next) || m.Cells.Contains(route.Exit))) == true) continue;
                        var owners = n.Board!.Marks?.Where(m => m.ActionId == 1181)
                            .Select(m => m.OwnerId).Distinct().ToArray();
                        if (owners is { Length: > 1 }) continue;
                        var board = n.Board with
                        {
                            Fighters = n.Board.Fighters.Select((f, i) => i == self
                                ? f with { Cell = route.Exit, Ap = ap, Mp = mp } : f).ToImmutableArray(),
                            Marks = n.Board.Marks?.Select(m => m.ActionId == 1181
                                && (m.Cells.Contains(next) || m.Cells.Contains(route.Exit))
                                    ? m with { ActionId = 1183 } : m).ToImmutableArray()
                        };
                        var resolved = SpellResolver.ResolvePortalTraversal(board, self,
                            snapshot.SpellLookup ?? ((_, _) => null), snapshot.CasterLevel);
                        if (resolved.Unmodeled.Count > 0) continue;
                        yield return new(route.Exit, (int)resolved.Board.Fighters[self].Ap,
                            (int)resolved.Board.Fighters[self].Mp, at.Steps + 1, at.Path.Add(next),
                            resolved.Board, resolved.Healing);
                        continue;
                    }
                    if (!cells.TryGetValue(next, out var target) || !target.Walkable || Occupied(n, next)
                        || snapshot.UnsafeForcedMovementCells?.Contains(next) == true
                        || n.Board!.Marks?.Any(mark => mark.Cells.Contains(next)) == true
                        || !MoveAllowed(n, at.Cell, next)) continue;
                    if (!labels.TryGetValue(next, out var list)) labels[next] = list = new();
                    if (list.Any(l => l.Ap >= ap && l.Mp >= mp)) continue;
                    list.RemoveAll(l => l.Ap <= ap && l.Mp <= mp);
                    list.Add((ap, mp));
                    queue.Enqueue(new(next, ap, mp, at.Steps + 1, at.Path.Add(next)));
                }
            }
        }
        Node WalkTo(Node n, Walk w)
        {
            if (w.Cell == n.Cell) return n;
            // Keep individual steps: execution cannot choose a different, more heavily tackled shortest path.
            var actions = n.Actions;
            foreach (int c in w.Path) actions = actions.Add(new(PlannerActionKind.Move, c, Mp: 1));
            return n with { Cell = w.Cell, Ap = w.Ap, Mp = w.Mp, Actions = actions, Repositioned = true,
                Board = w.Board ?? n.Board,
                Positions = w.Board is null ? n.Positions : w.Board.Fighters.Take(snapshot.Fighters.Length)
                    .Select(f => f.Cell).ToImmutableArray(),
                Life = w.Board is null ? n.Life : w.Board.Fighters.Take(snapshot.Fighters.Length)
                    .Select(f => (int)Math.Ceiling(f.Life)).ToImmutableArray(),
                Healing = n.Healing + w.Healing };
        }
        bool CanCast(Node n, PlannerSpell sp, int cell)
        {
            var d = sp.Cast;
            // Descriptor range already includes the observed bonus. Only apply
            // the simulated change since this snapshot, and only to boostable spells.
            int rangeDelta = d.Rangeable
                ? (n.Board!.Fighters[self].Characteristics?.GetValueOrDefault(CharIds.Range) ?? 0)
                    - snapshot.Characteristics.GetValueOrDefault(CharIds.Range) : 0;
            int spellRangeDelta = n.Board!.SpellModifiers?.Where(m => m.TargetId == snapshot.SelfId
                && m.SpellId == d.SpellId && m.ActionId == 281).Sum(m => m.Value) ?? 0;
            int minRange = Math.Max(0, d.MinRange + (n.Board!.SpellModifiers?.Where(m => m.TargetId == snapshot.SelfId
                && m.SpellId == d.SpellId && m.ActionId is 280 or 295)
                .Sum(m => m.ActionId == 295 ? -m.Value : m.Value) ?? 0));
            int range = Math.Max(0, d.Range + rangeDelta + spellRangeDelta);
            if (sp.RangeCap is int cap) range = Math.Min(range, cap);
            bool direct = CastLegality.CanCastGeometry(n.Cell, cell, minRange, range,
                d.CastInLine, d.CastInDiagonal, d.TestLos,
                c => !cells.TryGetValue(c, out var v) || v.BlocksLos,
                c => c != cell && c != n.Cell && Occupied(n, c));
            bool cellOk = CastLegality.CastCellConstraintsOk(d.NeedFreeCell, d.NeedTakenCell, cell == n.Cell || Occupied(n, cell));
            // Toujours un tir DIRECT sur la case visee : le serveur controle la case visee comme telle (09/11 : 21 tirs vises
            // sur l'ennemi « via portails » refuses). Le tir par portail vise le portail d'entree, voir PortalShots.
            return cells.TryGetValue(cell, out var tile) && tile.Walkable
                && (sp.AllowedCells is null || sp.AllowedCells.Contains(cell))
                && direct
                && (d.SpellId is not (14574 or 14604)
                    || (snapshot.Portals.IsDefaultOrEmpty || !snapshot.Portals.Any(p => p.Cell == cell))
                        && !PortalCells(n).Contains(cell))
                && cellOk;
        }
        static bool HpGate(int life, int max, int lo, int hi) => max <= 0
            ? lo <= 0 && hi >= 100
            : life * 100L >= (long)max * lo && life * 100L <= (long)max * hi;
        int? ChallengeElement(Node n, PlannerSpell sp) => sp.Cast.Effects
            .FirstOrDefault(e => e.ActionId is 2822 or 2828 or 2832 or 2890) is { } dynamicEffect
                ? (int)DamageEstimator.ElementOf(dynamicEffect,
                    n.Board!.Fighters[self].Characteristics ?? snapshot.Characteristics)
                : sp.ChallengeElement;
        bool Affordable(Node n, PlannerSpell sp) => n.Ap >= Math.Max(sp.Cast.Ap, sp.MinAp)
            && StateCriterion.Parse(sp.Cast.StatesCriterion).All(a => n.Board!.Fighters[self].StatesKnown
                || n.Board.Fighters[self].States?.Contains(a.Atom.Value) == true
                || n.Board.Fighters[self].AbsentStates?.Contains(a.Atom.Value) == true)
            && StateCriterion.CanCast(sp.Cast.StatesCriterion, state => n.Board!.Fighters[self].States?.Contains(state) == true)
            && (!sp.Cast.Effects.Any(e => (e.Kind == SpellEffectKind.Summon || e.ActionId == 405) && SpellResolver.IsImmediateModeled(e)
                    && (snapshot.SummonLookup?.Invoke(e.DiceNum != 0 ? e.DiceNum : e.Min,
                        e.DiceSide, snapshot.CasterLevel)?.SummonCost ?? 1) > 0)
                || n.Board!.Fighters.Where(f => f.Alive && f.Life > 0 && f.SummonerId == snapshot.SelfId)
                    .Sum(f => snapshot.SummonLookup?.Invoke(f.MonsterTemplate, f.MonsterGrade, snapshot.CasterLevel)?.SummonCost ?? 1)
                    < SummonCapacity(n.Board.Fighters[self].Characteristics ?? snapshot.Characteristics, sp.MaxSummons))
            && (!snapshot.SingleElement || n.Element is null || ChallengeElement(n, sp) is not int element || n.Element == element)
            && n.Casts.GetValueOrDefault(sp.Cast.SpellId) < Math.Min(sp.CastsLimit ?? sp.CastsLeft,
                sp.CastsLeft + (n.Board!.SpellModifiers?.Where(m => m.TargetId == snapshot.SelfId
                    && m.SpellId == sp.Cast.SpellId && m.ActionId == 290).Sum(m => m.Value) ?? 0))
            && HpGate(n.Life[self], snapshot.Fighters[self].MaxLife, sp.SelfHpMin, sp.SelfHpMax)
            && (!n.Repositioned || sp.AllowApproach);
        double TeamBlocking(Node n)
        {
            if (snapshot.TeamView is not { } team) return 0;
            double penalty = 0;
            foreach (var intent in team.Intents)
            {
                int owner = snapshot.Fighters.FindIndex(f => f.Id == intent.SelfActorId);
                if (owner < 0 || owner == self || snapshot.Fighters[owner].Enemy || n.Life[owner] <= 0) continue;
                if (intent.EndCell == n.Cell) penalty += 30;
                foreach (var target in intent.Targets)
                {
                    int i = snapshot.Fighters.FindIndex(f => f.Id == target.ActorId);
                    if (i < 0 || n.Cell == intent.EndCell || n.Cell == Position(n, i)) continue;
                    if (MapPathfinder.Aligned(intent.EndCell, Position(n, i))
                        && MapPathfinder.Aligned(intent.EndCell, n.Cell)
                        && MapPathfinder.CellDistance(intent.EndCell, n.Cell) + MapPathfinder.CellDistance(n.Cell, Position(n, i))
                            == MapPathfinder.CellDistance(intent.EndCell, Position(n, i))) penalty += 10;
                }
            }
            return penalty;
        }
        // Portails du NOEUD : ceux observes au sol (du plus ancien au plus recent) + ceux poses dans ce plan (marques 1181
        // du resolveur). 4 au plus : au-dela, le plus ancien disparait (verifie en jeu le 09/11).
        List<int> PortalCells(Node n)
        {
            var list = (snapshot.Portals.IsDefaultOrEmpty ? Enumerable.Empty<ClassMechanics.Portal>() : snapshot.Portals)
                .Where(p => p.Active).OrderBy(p => p.Id).Select(p => p.Cell).ToList();
            // The server leaves the first isolated portal inactive, then activates
            // it when its owner places the second. A closed network stays closed.
            if (!snapshot.Portals.IsDefaultOrEmpty && snapshot.Portals.Length == 1 && !snapshot.Portals[0].Active
                && snapshot.Portals[0].OwnerId == snapshot.SelfId
                && n.Board?.Marks?.Any(m => m.ActionId == 1181 && m.OwnerId == snapshot.SelfId
                    && !m.Cells.Contains(snapshot.Portals[0].Cell)) == true)
                list.Add(snapshot.Portals[0].Cell);
            if (n.Board?.Marks is { } marks)
            {
                foreach (var m in marks)
                {
                    if (m.ActionId == 1181 && !m.AwaitingPortalPair)
                    {
                        foreach (int c in m.Cells)
                        {
                            list.Remove(c);
                            list.Add(c);
                        }
                    }
                    else if (m.ActionId == 1183)
                        list.RemoveAll(m.Cells.Contains);
                }
            }
            while (list.Count > 4) list.RemoveAt(0);
            return list;
        }
        // Un tir via portail ne reoriente QUE la geometrie de zone (CastOn/areaOrigin). Les effets qui
        // deplacent le lanceur, posent une marque ou invoquent gardent leur origine reelle : jamais de tir.
        static bool PortalShotEffect(SpellEffectDescriptor e) => SpellResolver.IsImmediateModeled(e)
            && !e.PlacesGroundMark
            && e.Kind is not (SpellEffectKind.Summon or SpellEffectKind.Teleport or SpellEffectKind.Dash
                or SpellEffectKind.Push or SpellEffectKind.Pull or SpellEffectKind.Swap);
        static bool PortalShotSpell(SpellCastDescriptor d) => d.SpellId is not (369 or 14574 or 14604)
            && !d.NeedTakenCell
            && (HasModeledEnemyDamage(d) || d.Effects.All(PortalShotEffect)
                && d.Effects.Any(e => e.Kind is SpellEffectKind.Heal or SpellEffectKind.Shield or SpellEffectKind.Buff))
            && (!d.Effects.Any(e => e.Size > 0 && e.Shape != 'P')
                || !d.Uncertain && d.Effects.All(PortalShotEffect));
        // Tirs a travers le reseau : le lanceur vise un portail d'ENTREE a portee directe ; la case d'impact suit les regles
        // du jeu (ClassMechanics.ProjectThroughPortals). Les degats et la zone sortent de la SORTIE ; jamais l'arme (369).
        IEnumerable<Node> PortalShots(Node n)
        {
            var portals = PortalCells(n);
            if (portals.Count < 2) yield break;
            foreach (var sp in spells)
            {
                if (!Affordable(n, sp) || !PortalShotSpell(sp.Cast)) continue;
                foreach (int entry in portals)
                {
                    if (!budget.Take()) yield break;
                    if (Occupied(n, entry) || !CanCast(n, sp, entry)) continue;
                    if (ClassMechanics.ProjectThroughPortals(n.Cell, entry, portals) is not { } shot) continue;
                    int target = Enumerable.Range(0, snapshot.Fighters.Length)
                        .FirstOrDefault(i => Position(n, i) == shot.Impact && n.Life[i] > 0, -1);
                    if (target >= 0 ? !Eligible(n, sp, target)
                        : !sp.Cast.Effects.Any(e => e.Size > 0 && e.Shape != 'P')) continue;
                    // Le serveur teste aussi la ligne de vue de la SORTIE vers l'impact (10/11 : 7 refus 174 sur des tirs
                    // via portail dont seule l'entree avait ete verifiee).
                    if (sp.Cast.TestLos && !Los(shot.Exit, shot.Impact, c => c != shot.Impact && c != shot.Exit && Occupied(n, c))) continue;
                    if (CastOn(n, sp, shot.Impact, target, entry, shot.NetworkDistance, shot.Exit) is { } cast) yield return cast;
                }
            }
        }
        // Portal, portal, shot as ONE candidate. A lone first portal scores nothing (no network yet)
        // and was pruned from the 8-wide beam before its pair was ever tried, so the bot laid pairs
        // it never fired through. Only real resolved damage is credited to the combo.
        IEnumerable<Node> PortalCombos(Node n)
        {
            var layer = spells.FirstOrDefault(s => s.Cast.SpellId == 14574 && Affordable(n, s));
            if (layer is null) yield break;
            var own = PortalCells(n);
            if (!snapshot.Portals.IsDefaultOrEmpty && snapshot.Portals.Length == 1 && !snapshot.Portals[0].Active
                && snapshot.Portals[0].OwnerId == snapshot.SelfId && !own.Contains(snapshot.Portals[0].Cell))
                own.Add(snapshot.Portals[0].Cell);
            if (own.Count >= 2) yield break;
            int needed = 2 - own.Count;
            var shooters = spells.Where(s => PortalShotSpell(s.Cast) && HasModeledEnemyDamage(s.Cast)).ToList();
            if (shooters.Count == 0 || n.Depth + needed + 1 > snapshot.MaxCasts
                || n.Ap < layer.Cast.Ap * needed + shooters.Min(s => Math.Max(s.Cast.Ap, s.MinAp))) yield break;
            // Cell reached from origin by the vector from -> to (-1 off the grid).
            static int Shift(int origin, int from, int to)
            {
                var (ox, oy) = MapPathfinder.MapPointOf(origin);
                var (fx, fy) = MapPathfinder.MapPointOf(from);
                var (tx, ty) = MapPathfinder.MapPointOf(to);
                return MapPathfinder.CellFromMapPoint(ox + (tx - fx), oy + (ty - fy));
            }
            // Two-portal network: the shot enters at E and leaves at X with impact = X + (E - caster).
            var pairs = new List<(int First, int Second)>();
            for (int i = 0; i < snapshot.Fighters.Length; i++)
            {
                if (!snapshot.Fighters[i].Enemy || n.Life[i] <= 0 || !snapshot.Fighters[i].CanTarget) continue;
                int target = Position(n, i);
                if (own.Count == 1)
                {
                    int q = own[0];
                    pairs.Add((-1, Shift(target, q, n.Cell)));   // q is the entry: new exit = target - (q - caster)
                    pairs.Add((-1, Shift(n.Cell, q, target)));   // q is the exit: new entry = caster + (target - q)
                    continue;
                }
                foreach (var tile in snapshot.Cells)
                {
                    if (!budget.Take()) yield break;
                    int entry = tile.Id;
                    if (entry == n.Cell || !CanCast(n, layer, entry) || !shooters.Any(s => CanCast(n, s, entry))) continue;
                    int exit = Shift(target, entry, n.Cell);
                    if (exit >= 0 && exit != entry) pairs.Add((entry, exit));
                }
            }
            // A wider entry-exit spacing earns a larger +2 %/cell bonus: try those first, bounded.
            // The exit mirrors the entry around the target, so wide pairs fall out of portal range:
            // keep only exits the caster can actually lay before ranking by spacing.
            foreach (var (first, second) in pairs.Where(p => p.Second >= 0 && CanCast(n, layer, p.Second)).Distinct()
                .OrderByDescending(p => p.First < 0 ? 0 : MapPathfinder.CellDistance(p.First, p.Second)).Take(24))
            {
                if (!budget.Take()) yield break;
                var laid = n;
                if (first >= 0)
                {
                    if (!CanCast(laid, layer, first) || CastOn(laid, layer, first, -1) is not { } one) continue;
                    laid = one;
                }
                if (!Affordable(laid, layer) || !CanCast(laid, layer, second)
                    || CastOn(laid, layer, second, -1) is not { } pair) continue;
                // One search step: the 3-deep beam must still have room for the shots that follow.
                foreach (var shot in PortalShots(pair))
                    if (shot.Damage > pair.Damage) yield return shot with { Depth = n.Depth + 1 };
            }
        }
        // A portal the caster can walk into next turn to land out of the modeled reply is
        // real safety, not a flat placement bonus: value only the exposure actually avoided,
        // discounted like a future shot and capped the same way. An unreachable exit, an
        // occupied one, or a landing no safer than standing still is worth nothing.
        double PortalEscapeValue(Node n, int portalCell)
        {
            double here = Exposure(n, n.Cell);
            if (here <= 0) return 0;
            // Same network the cast would complete: a lone own inactive portal pairs with it.
            var network = PortalCells(n);
            if (!snapshot.Portals.IsDefaultOrEmpty && snapshot.Portals.Length == 1 && !snapshot.Portals[0].Active
                && snapshot.Portals[0].OwnerId == snapshot.SelfId)
                network.Add(snapshot.Portals[0].Cell);
            network.RemoveAll(c => c == portalCell);
            if (network.Count == 0) return 0;
            double best = 0;
            foreach (int paired in network)
            {
                if (!budget.Take()) return best;
                // Entering either side exits at the other: both landings are candidates.
                foreach (int exit in new[] { portalCell, paired })
                {
                    if (!cells.TryGetValue(exit, out var tile) || !tile.Walkable
                        || Occupied(n, exit) || exit == n.Cell
                        || snapshot.UnsafeForcedMovementCells?.Contains(exit) == true) continue;
                    best = Math.Max(best, Math.Min(12, (here - Exposure(n, exit)) * NextTurnWeight));
                }
            }
            return best;
        }
        // A future shot is discounted and capped: setup must not outrank a confirmed kill now.
        // Validate the same geometry, masks and resources as an immediate portal cast.
        double PortalPlacementValue(Node n, int portalCell, int apCost)
        {
            var portals = PortalCells(n);
            if (!snapshot.Portals.IsDefaultOrEmpty && snapshot.Portals.Length == 1 && !snapshot.Portals[0].Active
                && snapshot.Portals[0].OwnerId == snapshot.SelfId
                && snapshot.Portals[0].Cell != portalCell)
                portals.Add(snapshot.Portals[0].Cell);
            portals.Remove(portalCell); portals.Add(portalCell);
            while (portals.Count > 4) portals.RemoveAt(0);
            int futureAp = Math.Max(n.Ap - apCost, snapshot.Fighters[self].TurnAp ?? snapshot.Ap);
            var shooters = spells.Where(s => s.Cast.Ap <= futureAp && PortalShotSpell(s.Cast)).ToList();
            if (portals.Count >= 2)
            {
                var futureBoard = SpellResolver.AdvanceStatBuffs(n.Board!);
                futureBoard = futureBoard with { Fighters = futureBoard.Fighters.Select((fighter, i) =>
                    i == self ? fighter with { Ap = futureAp } : fighter).ToImmutableArray() };
                var future = n with
                {
                    Ap = futureAp,
                    Board = futureBoard with { Marks = (n.Board.Marks ?? Array.Empty<SpellMark>())
                        .Append(new SpellMark(1181, 2, snapshot.SelfId, new[] { portalCell })).ToArray() }
                };
                double setupValue = 0;
                foreach (var sp in shooters)
                {
                    double existingGain = 0;
                    var existing = future with { Board = futureBoard };
                    for (int i = 0; i < snapshot.Fighters.Length; i++)
                    {
                        if (!budget.Take()) return setupValue;
                        int cell = Position(existing, i);
                        if (existing.Life[i] <= 0 || !Eligible(existing, sp, i)
                            || !Affordable(existing, sp) || !CanCast(existing, sp, cell)) continue;
                        if (CastOn(existing, sp, cell, i) is { } directFollow)
                            existingGain = Math.Max(existingGain, directFollow.Damage - n.Damage
                                + directFollow.Healing - n.Healing + directFollow.Shielding - n.Shielding);
                    }
                    var existingPortals = portals.Where(cell => cell != portalCell).ToArray();
                    foreach (int entry in existingPortals)
                    {
                        if (!budget.Take()) return setupValue;
                        if (!Affordable(existing, sp) || Occupied(existing, entry) || !CanCast(existing, sp, entry)
                            || ClassMechanics.ProjectThroughPortals(n.Cell, entry, existingPortals) is not { } shot) continue;
                        int target = Enumerable.Range(0, snapshot.Fighters.Length)
                            .FirstOrDefault(i => n.Life[i] > 0 && Position(n, i) == shot.Impact, -1);
                        if ((target >= 0 ? !Eligible(existing, sp, target)
                                : !sp.Cast.Effects.Any(e => e.Size > 0 && e.Shape != 'P'))
                            || sp.Cast.TestLos && !Los(shot.Exit, shot.Impact,
                                c => c != shot.Impact && c != shot.Exit && Occupied(existing, c))) continue;
                        if (CastOn(existing, sp, shot.Impact, target, entry, shot.NetworkDistance, shot.Exit) is { } follow)
                            existingGain = Math.Max(existingGain, follow.Damage - n.Damage
                                + follow.Healing - n.Healing + follow.Shielding - n.Shielding);
                    }
                    foreach (int entry in portals)
                    {
                        if (!budget.Take()) return setupValue;
                        if (!Affordable(future, sp) || Occupied(future, entry) || !CanCast(future, sp, entry)
                            || ClassMechanics.ProjectThroughPortals(n.Cell, entry, portals) is not { } shot) continue;
                        int target = Enumerable.Range(0, snapshot.Fighters.Length)
                            .FirstOrDefault(i => n.Life[i] > 0 && Position(n, i) == shot.Impact, -1);
                        if ((target >= 0 ? !Eligible(future, sp, target)
                                : !sp.Cast.Effects.Any(e => e.Size > 0 && e.Shape != 'P'))
                            || sp.Cast.TestLos && !Los(shot.Exit, shot.Impact,
                                c => c != shot.Impact && c != shot.Exit && Occupied(future, c))) continue;
                        if (CastOn(future, sp, shot.Impact, target, entry, shot.NetworkDistance, shot.Exit) is { } follow)
                        {
                            var direct = CanCast(future, sp, shot.Impact)
                                ? CastOn(future, sp, shot.Impact, target) : null;
                            double directGain = direct is null ? 0 : Math.Max(0,
                                direct.Damage - n.Damage + direct.Healing - n.Healing + direct.Shielding - n.Shielding);
                            setupValue = Math.Max(setupValue, Math.Min(10,
                                Math.Max(0, follow.Damage - n.Damage + follow.Healing - n.Healing
                                    + follow.Shielding - n.Shielding - Math.Max(directGain, existingGain)) * .15));
                        }
                    }
                }
                return setupValue;
            }
            // An observed inactive network is not evidence that a newly laid portal
            // will reactivate it. Wait for the server's activation before valuing a shot.
            if (!snapshot.Portals.IsDefaultOrEmpty && snapshot.Portals.Any(p => !p.Active)) return 0;
            int reach = shooters.Select(s => s.Cast.Range).DefaultIfEmpty(0).Max();
            double best = 0;
            for (int i = 0; i < snapshot.Fighters.Length; i++)
            {
                if (!snapshot.Fighters[i].Enemy || n.Life[i] <= 0) continue;
                if (!shooters.Any(sp => Eligible(n, sp, i))) continue;
                int enemy = Position(n, i);
                if (MapPathfinder.CellDistance(n.Cell, enemy) <= reach) continue;
                int d = MapPathfinder.CellDistance(portalCell, enemy);
                if (d <= 3) best = Math.Max(best, 6.0 - d);
            }
            return best;
        }
        bool Eligible(Node n, PlannerSpell sp, int i) => n.Life[i] > 0
            && (i == self || snapshot.Fighters[i].CanTarget
                && n.Board!.Fighters[i].Visibility == FighterVisibility.Visible)
            && sp.AllowedTargets.Contains(snapshot.Fighters[i].Id)
            && HpGate(n.Life[i], snapshot.Fighters[i].MaxLife, sp.TargetHpMin, sp.TargetHpMax)
            && (sp.TargetCap <= 0 || sp.TargetCasts.GetValueOrDefault(snapshot.Fighters[i].Id)
                + n.Targets.GetValueOrDefault((sp.Cast.SpellId, snapshot.Fighters[i].Id)) < sp.TargetCap);
        bool Reserved(int i) => snapshot.TeamView is { } team && snapshot.Fighters[i].Enemy
            && (team.ReservedKills.Contains(snapshot.Fighters[i].Id)
                || team.EffectiveRemainingLife(snapshot.Fighters[i].Id, snapshot.Fighters[i].Life) == 0);
        // aim >= 0 : tir a travers les portails ; aim = portail d'entree vise (case envoyee au serveur), cell = case d'impact.
        // areaOrigin >= 0 : la zone est orientee depuis la SORTIE du portail ; sinon depuis la position reelle du lanceur.
        // Le lanceur, ses effets personnels et l'attribution des degats restent sur n.Cell / self.
        Node? CastOn(Node n, PlannerSpell sp, int cell, int target, int aim = -1, int aimNetworkDistance = 0, int areaOrigin = -1)
        {
            var d = sp.Cast;
            int zoneOrigin = areaOrigin >= 0 ? areaOrigin : n.Cell;
            if (sp.ZonePolicy is { } policy)
            {
                if (!budget.Take()) return null;
                var area = policy.Size > 0
                    ? CombatZones.GetCells(policy.Shape, cell, CombatZones.OrientationTo(zoneOrigin, cell), policy.MinSize, policy.Size)
                    : new[] { cell };
                for (int work = 0; work < area.Count + n.Board!.Fighters.Count; work++)
                    if (!budget.Take()) return null;
                var zone = area.ToHashSet();
                var alive = n.Board.Fighters.Where(f => f.Alive && f.Life > 0).ToArray();
                int CurrentCell(SpellFighter f) => f.Id == snapshot.SelfId ? n.Cell : f.Cell;
                // Challenge and ally safety are geometric, even when effect masks exclude a fighter.
                if (alive.Any(f => f.Enemy && policy.ForbiddenEnemies.Contains(f.Id) && zone.Contains(CurrentCell(f)))) return null;
                bool healingAim = target >= 0 && !snapshot.Fighters[target].Enemy
                    && CanHealAlliesWithoutFriendlyDamage(d);
                if (policy.Size > 0 && !healingAim && (policy.RequireEnemySafety || target >= 0 && snapshot.Fighters[target].Enemy)
                    && !CombatAoeSafety.Evaluate(zone,
                        alive.Where(f => f.Enemy && f.Visibility == FighterVisibility.Visible
                            && !policy.ForbiddenEnemies.Contains(f.Id)
                            && !untargetable.Contains(f.Id))
                            .Select(CurrentCell),
                        alive.Where(f => !f.Enemy).Select(CurrentCell), policy.MinEnemyHits).Allowed) return null;
            }
            // Resolver calls and every affected fighter share the search budget.
            for (int work = 0; work < n.Board!.Fighters.Count * Math.Max(1, d.Effects.Length); work++)
                if (!budget.Take()) return null;
            bool throughPortal = aim >= 0;
            int portalNetDist = aimNetworkDistance;
            var before = n.Board with
            {
                ThroughPortal = throughPortal,
                Fighters = n.Board.Fighters.Select((f, i) => i == self
                    ? f with { Cell = n.Cell, Ap = n.Ap - d.Ap, Mp = n.Mp } : f).ToImmutableArray()
            };
            var resolved = SpellResolver.Resolve(before, self, cell, d, snapshot.SpellLookup ?? ((_, _) => null),
                snapshot.CasterLevel, c => cells.TryGetValue(c, out var tile) && tile.Walkable,
                areaOrigin >= 0 ? areaOrigin : null,
                throughPortal ? ClassMechanics.PortalDamageMultiplier(portalNetDist) : 1);
            if (resolved.Unmodeled.Count > 0 && resolved.EnemyDamage <= 0) return null;
            if (resolved.Summons.Any(s => s.CasterId == snapshot.SelfId)
                && resolved.Board.Fighters.Where(f => f.Alive && f.Life > 0 && f.SummonerId == snapshot.SelfId)
                    .Sum(f => snapshot.SummonLookup?.Invoke(f.MonsterTemplate, f.MonsterGrade, snapshot.CasterLevel)?.SummonCost ?? 1)
                    > SummonCapacity(resolved.Board.Fighters[self].Characteristics ?? snapshot.Characteristics, sp.MaxSummons)) return null;
            if (resolved.Events.OfType<DamageResolved>().Any(e => !e.Enemy && e.Killed)) return null;
            long focus = n.FocusTarget;
            if (snapshot.FocusTargets)
            {
                var alive = before.Fighters.Where(f => f.Enemy && f.Alive && f.Life > 0).Select(f => f.Id).ToHashSet();
                if (!alive.Contains(focus)) focus = 0;
                foreach (var hit in resolved.Events.OfType<DamageResolved>().Where(e => e.Enemy))
                {
                    if (focus != 0 && focus != hit.TargetId) return null;
                    focus = hit.TargetId;
                    if (hit.Killed) { alive.Remove(hit.TargetId); focus = 0; }
                }
            }
            // Pulling into the caster stops at melee distance. Other obstructions remain
            // outside the admitted displacement contract.
            if (resolved.Movements.Any(m => m.Blocked
                && !(m.ActionId == 6 && m.To != m.From
                    && MapPathfinder.CellDistance(m.To, before.Fighters.First(f => f.Id == m.CasterId).Cell) == 1)
                && !(m.ActionId == 5 && resolved.Unmodeled.Count == 0
                    && before.Fighters.Any(f => f.Id == m.TargetId && f.Enemy)))) return null;
            var movementPositions = before.Fighters.ToDictionary(f => f.Id, f => f.Cell);
            foreach (var movement in resolved.Movements)
            {
                if (movement.ActionId != 4 && !snapshot.ForcedMovementSafe
                    && (snapshot.UnsafeForcedMovementCells is not { } unsafeCells
                        || MapPathfinder.StraightLine(movement.From, movement.To).Any(unsafeCells.Contains))) return null;
                if (movement.TargetId == snapshot.SelfId && (!snapshot.MobilityAllowed
                    || !CombatMovementDecision.CanReposition(snapshot.MovementPolicy, snapshot.Style, sp.AllowApproach))) return null;
                if (movement.ActionId is 5 or 6 or 783
                    && !MapPathfinder.Aligned(movementPositions[movement.CasterId], movement.From)) return null;
                movementPositions[movement.TargetId] = movement.To;
            }
            var affected = resolved.Events.Select(e => snapshot.Fighters.FindIndex(f => f.Id == e.TargetId))
                .Concat(resolved.Deferred.Where(e => e.TargetId.HasValue)
                    .Select(e => snapshot.Fighters.FindIndex(f => f.Id == e.TargetId)))
                // Establishing one absent state is useful setup even without a
                // positively observed state to remove. It earns no control points.
                .Concat(Enumerable.Range(0, snapshot.Fighters.Length).Where(i =>
                    resolved.Board.Fighters[i].AbsentStates?.Any(state =>
                        before.Fighters[i].AbsentStates?.Contains(state) != true) == true))
                .Where(i => i >= 0).Distinct().ToArray();
            // Side effects on self (life steal, self buffs) are not a second configured target.
            // Une ZONE qui effleure un combattant non eligible n'est pas refusee ici : la securite allies
            // (CombatAoeSafety) et la penalite FriendlyFire tranchent. Refuser la branche entiere jetait la
            // cellule qui touche tous les ennemis et faisait tirer a cote.
            bool zoneCast = sp.ZonePolicy is { Size: > 0 };
            bool isPortalSpell = d.SpellId is 14574 or 14604;
            bool closesPortal = resolved.Marks.Any(m => m.ActionId == 1183);
            if (!zoneCast && affected.Any(i => i != self && !Eligible(n, sp, i)
                && !resolved.Events.OfType<DamageResolved>().Any(e => e.TargetId == snapshot.Fighters[i].Id && !e.Enemy))) return null;
            if (!d.IsTeleport && !isPortalSpell && !closesPortal && resolved.Summons.Count == 0 && !affected.Any(i => Eligible(n, sp, i))
                && !(target >= 0 && Eligible(n, sp, target) && affected.Contains(self))) return null;
            if (d.Effects.All(e => e.Kind is SpellEffectKind.Heal or SpellEffectKind.Shield)
                && !affected.Any(i => !snapshot.Fighters[i].Enemy && snapshot.Fighters[i].MaxLife > 0
                    && (n.Life[i] * 100L < (long)snapshot.Fighters[i].MaxLife * sp.SupportHpThreshold
                        || Exposure(n, Position(n, i)) > n.Shield[i])
                    && (resolved.Board.Fighters[i].Life > before.Fighters[i].Life
                        || resolved.Board.Fighters[i].Shield > before.Fighters[i].Shield && Exposure(n, Position(n, i)) > n.Shield[i]))) return null;
            if (affected.Length == 0 && resolved.Events.Count == 0 && !isPortalSpell) return null;
            var history = n.Targets;
            foreach (int i in affected)
                history = history.SetItem((d.SpellId, snapshot.Fighters[i].Id), history.GetValueOrDefault((d.SpellId, snapshot.Fighters[i].Id)) + 1);
            var result = n with
            {
                Board = resolved.Board, Cell = resolved.Board.Fighters[self].Cell,
                DodgeableMpLoss = resolved.Events.OfType<PointsResolved>()
                    .Where(e => e.ActionId == 1080 && e.Enemy && e.Delta < 0)
                    .Aggregate(n.DodgeableMpLoss ?? ImmutableDictionary<long, double>.Empty,
                        (losses, e) => losses.SetItem(e.TargetId, losses.GetValueOrDefault(e.TargetId) - e.Delta)),
                Element = n.Element ?? ChallengeElement(n, sp),
                FocusTarget = focus,
                Ap = Math.Max(0, (int)resolved.Board.Fighters[self].Ap), Mp = Math.Max(0, (int)resolved.Board.Fighters[self].Mp),
                Positions = resolved.Board.Fighters.Take(snapshot.Fighters.Length).Select(f => f.Cell).ToImmutableArray(),
                Life = resolved.Board.Fighters.Take(snapshot.Fighters.Length).Select(f => (int)Math.Ceiling(f.Life)).ToImmutableArray(),
                Shield = resolved.Board.Fighters.Take(snapshot.Fighters.Length).Select(f => (int)Math.Ceiling(f.Shield)).ToImmutableArray(),
                Depth = n.Depth + 1, Repositioned = n.Repositioned || resolved.Board.Fighters[self].Cell != n.Cell,
                Casts = n.Casts.SetItem(d.SpellId, n.Casts.GetValueOrDefault(d.SpellId) + 1), Targets = history,
                Actions = n.Actions.Add(new(PlannerActionKind.Cast, aim >= 0 ? aim : cell, d.SpellId,
                    target >= 0 ? snapshot.Fighters[target].Id : 0, d.Ap, ConfigurationIndex: sp.ConfigurationIndex,
                    ViaPortal: throughPortal)),
                FriendlyFire = n.FriendlyFire + resolved.FriendlyFire * 4,
                // Pose de portail : valeur de placement seulement (voir PortalPlacementValue), plus de bonus fixe.
                Control = n.Control + (isPortalSpell
                        ? PortalPlacementValue(n, cell, d.Ap) + PortalEscapeValue(n, cell) : 0)
                    + resolved.Events.OfType<PointsResolved>()
                    .Where(e => e.Enemy || e.TargetId != snapshot.SelfId)
                    .GroupBy(e => (e.TargetId, e.IsAp, e.Enemy))
                    .Sum(g => Math.Max(0, g.Sum(e => e.Delta) * (g.Key.Enemy ? -1 : 1))
                        * (g.Key.Enemy ? (g.Key.IsAp ? 8 : 5) : (g.Key.IsAp ? 6 : 3))),
                // Genuine self visible->invisible transition: bounded defensive utility against the
                // modeled reply. Residual incoming is deliberately left unchanged (enemies can still hit
                // area/last-known cells); an already-invisible caster emits no event and earns nothing.
                InvisibilityUtility = resolved.Board.Fighters[self].Visibility is not (FighterVisibility.Invisible or FighterVisibility.Detected)
                    ? 0 : n.InvisibilityUtility + (resolved.Events.OfType<VisibilityResolved>().Any(e => e.TargetId == snapshot.SelfId
                        && e.Visibility == FighterVisibility.Invisible)
                    ? Math.Min(InvisibilityUtilityCap, Exposure(n, resolved.Board.Fighters[self].Cell) * .25) : 0),
                Uncertainty = n.Uncertainty + resolved.Unmodeled.Count * 4 + resolved.Deferred.Count * 2,
                Unmodeled = n.Unmodeled + resolved.Unmodeled.Count,
            };
            foreach (int i in affected)
            {
                var f = snapshot.Fighters[i];
                if (f.Enemy)
                {
                    double damage = resolved.Events.OfType<DamageResolved>().Where(e => e.TargetId == f.Id).Sum(e => e.LifeLost + e.ShieldLost);
                    int effective = snapshot.TeamView?.EffectiveRemainingLife(f.Id, f.Life) ?? f.Life;
                    double remaining = Math.Max(0, effective - (f.Life - before.Fighters[i].Life));
                    bool reserved = Reserved(i);
                    result = result with { Damage = result.Damage + (reserved ? damage * .01 : Math.Min(damage, remaining + before.Fighters[i].Shield)),
                        Finishes = result.Finishes + (!reserved && remaining > 0 && before.Fighters[i].Life - resolved.Board.Fighters[i].Life >= remaining
                            ? 40 + f.ExpectedDamage + (snapshot.TeamView?.ExpectedDamageOn(f.Id) > 0 ? 10 : 0) : 0) };
                }
                else
                {
                    double incoming = Exposure(n, Position(n, i));
                    bool useful = f.MaxLife > 0 && (n.Life[i] * 100L < (long)f.MaxLife * sp.SupportHpThreshold
                        || incoming > n.Shield[i]);
                    if (!useful) continue;
                    double healed = resolved.Events.OfType<HealingResolved>().Where(e => e.TargetId == f.Id).Sum(e => e.Amount);
                    // Reservations reduce the value of another heal, not observed survival or resolver life.
                    // Local healing already explored in this branch consumes the remaining need too.
                    int reservedLife = snapshot.TeamView?.EffectiveLife(f.Id, f.Life, f.MaxLife) ?? f.Life;
                    healed = Math.Min(healed, Math.Max(0, f.MaxLife - reservedLife - (before.Fighters[i].Life - f.Life)));
                    double shield = resolved.Events.OfType<ShieldResolved>().Where(e => e.TargetId == f.Id).Sum(e => e.Amount);
                    int reservedShield = snapshot.TeamView?.EffectiveShield(f.Id, f.Shield) ?? f.Shield;
                    double protection = Math.Max(0, reservedShield + before.Fighters[i].Shield - f.Shield);
                    double priority = f.Id == exposed && Survival(n.Life[i], f.MaxLife) > 1 ? 2 : 1;
                    result = result with { Healing = result.Healing + healed * Survival(n.Life[i], f.MaxLife) * priority,
                        Shielding = result.Shielding + Math.Min(shield, Math.Max(0, incoming - protection)) * .5 * priority };
                }
            }
            return result;
        }
        IEnumerable<Node> Casts(Node n)
        {
            if (budget.Used >= budget.Limit) yield break;
            // The observed network has at most four entry cells. Evaluate these bounded
            // shots before scanning every ground center of the direct area spells.
            var portalCandidates = PortalShots(n).Concat(PortalCombos(n)).ToList();
            var candidates = new List<Node>();
            foreach (var sp in spells)
            {
                if (budget.Used >= budget.Limit) break;
                if (!Affordable(n, sp)) continue;
                var d = sp.Cast;
                bool free = d.IsTeleport || d.SpellId is 14574 or 14604
                    || d.Effects.Any(e => e.ActionId == 1183)
                    || d.Effects.Any(e => e.Kind == SpellEffectKind.Summon);
                bool zone = d.Effects.Any(e => e.Size > 0 && e.Shape != 'P');
                bool globalCaster = d.Effects.Any(e => e.TargetMask.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains("C"));
                var centers = snapshot.Fighters.Select((f, i) => i).Where(i => Eligible(n, sp, i)).Select(i => Position(n, i))
                    .Concat(sp.AllowNewAlliedSummons && d.Effects.Any(e => e.ActionId == 405)
                        ? n.Board!.Fighters.Skip(snapshot.Fighters.Length)
                            .Where(f => f.Alive && f.Life > 0 && !f.Enemy && f.SummonerId == snapshot.SelfId
                                && f.Visibility == FighterVisibility.Visible
                                && HpGate((int)f.Life, (int)f.MaxLife, sp.TargetHpMin, sp.TargetHpMax)
                                && (sp.TargetCap <= 0 || sp.TargetCasts.GetValueOrDefault(f.Id)
                                    + n.Targets.GetValueOrDefault((d.SpellId, f.Id)) < sp.TargetCap))
                            .Select(f => f.Cell) : Enumerable.Empty<int>())
                    .Concat(zone || free || d.NeedFreeCell || globalCaster && d.MinRange > 0 && !d.NeedTakenCell
                        ? snapshot.Cells.Select(c => c.Id) : Enumerable.Empty<int>()).Distinct().Order();
                foreach (int cell in centers)
                {
                    if (!budget.Take()) break;
                    if (!CanCast(n, sp, cell)) continue;
                    if (free && (!cells[cell].Walkable || cell == n.Cell || Occupied(n, cell))) continue;
                    int target = Enumerable.Range(0, snapshot.Fighters.Length).FirstOrDefault(i => Position(n, i) == cell && n.Life[i] > 0, -1);
                    if (target >= 0 && !Eligible(n, sp, target) && !zone && !free) continue;
                    if (CastOn(n, sp, cell, target) is { } cast) candidates.Add(cast);
                }
            }
            // Preserve direct casts on equal scores; a portal route must add value.
            candidates.AddRange(portalCandidates);
            bool alternative = candidates.Any(c => snapshot.Fighters.Select((f, i) => (f, i))
                .Any(t => t.f.Enemy && !Reserved(t.i) && c.Life[t.i] < n.Life[t.i]));
            foreach (var cast in candidates)
            {
                bool onlyReserved = snapshot.Fighters.Select((f, i) => (f, i)).Any(t => t.f.Enemy && Reserved(t.i) && cast.Life[t.i] < n.Life[t.i])
                    && !snapshot.Fighters.Select((f, i) => (f, i)).Any(t => t.f.Enemy && !Reserved(t.i) && cast.Life[t.i] < n.Life[t.i]);
                if (!alternative || !onlyReserved) yield return cast;
            }
        }
        // Placement, buffs and state setup can have zero immediate score; rank by a legal,
        // affordable follow-up before pruning them behind weak direct attacks.
        // Chains (e.g. Transposition -> Attirance -> Melee) explore nested follow-ups recursively.
        double Rank(Node n, int depth = 0)
        {
            double rank = Score(n).Total;
            if (n.Depth >= Math.Min(MaxDepth, snapshot.MaxCasts) || depth >= 2
                || n.Actions.LastOrDefault() is not { Kind: PlannerActionKind.Cast } last
                || !spells.Any(s => s.Cast.SpellId == last.SpellId && (s.Cast.IsTeleport || s.Cast.IsForcedMovement
                    || s.Cast.SpellId is 14574 or 14604
                    || n.Board!.DeferredEffects?.Any(e => e.SpellId == last.SpellId && e.CasterId == snapshot.SelfId) == true
                    || s.Cast.Effects.Any(e => e.Kind is SpellEffectKind.Buff or SpellEffectKind.Summon || e.ActionId is 950 or 951 or 952)))) return rank;

            // Simulated actors are not proof of an observed wall. Keep every
            // living enemy; only recognize observed allied 3112 bombs belonging
            // to this caster, not the unverified old 2831..2836 templates.
            if (depth == 0)
            {
                var (bombs, liveEnemies) = n.Board is { } board
                    ? ClassifyRogueCombatants(snapshot, board)
                    : (ImmutableArray<PlannerFighter>.Empty, ImmutableArray<PlannerFighter>.Empty);
                // Legacy overload intentionally scores zero: no wall-activation
                // event is available here, so geometry grants no damage bonus.
                if (!bombs.IsDefaultOrEmpty)
                    rank += ClassMechanics.EvaluateRogueBombTactics(n.Cell, bombs, liveEnemies,
                        c => cells.TryGetValue(c, out var t) && t.Walkable);

                // Synergies et combos tactiques par classe (Sacrieur CaC, Zobal bouclier essaim, Forgelance cluster)
                var selfFighter = snapshot.Fighters.FirstOrDefault(f => f.Id == snapshot.SelfId);
                int selfBreed = selfFighter?.Breed ?? 0;
                if (selfBreed == ClassMechanics.SacrierBreedId && liveEnemies.Length > 0)
                {
                    var nearestE = liveEnemies.OrderBy(e => MapPathfinder.CellDistance(n.Cell, e.Cell)).First();
                    bool aligned = MapPathfinder.Aligned(n.Cell, nearestE.Cell);
                    int dist = MapPathfinder.CellDistance(n.Cell, nearestE.Cell);
                    rank += ClassMechanics.EvaluateSacrieurCombo(n.Cell, nearestE.Cell, aligned, dist, n.Ap);
                }
                else if (selfBreed == ClassMechanics.MasqueraiderBreedId)
                {
                    var summons = snapshot.Fighters.Where(f => !f.Enemy && f.SummonerId != 0);
                    rank += ClassMechanics.EvaluateZobalShieldOnSummons(n.Cell, summons.Select(f => new PlannerFighter(f.Id, f.Cell, false, (int)f.Life, (int)f.MaxLife, (int)f.Shield)));
                }
                else if (selfBreed == ClassMechanics.ForgelanceBreedId && liveEnemies.Length >= 2)
                {
                    rank += ClassMechanics.EvaluatePandaForgelanceCluster(liveEnemies.Select(e => e.Cell));
                }
                else if (selfBreed == ClassMechanics.XelorBreedId && n.Board is { } after)
                {
                    var target = snapshot.Fighters.FirstOrDefault(f => f.Id == last.Target);
                    var movedTarget = target is null ? null : after.Fighters.FirstOrDefault(f => f.Id == target.Id);
                    var movedSelf = after.Fighters.FirstOrDefault(f => f.Id == snapshot.SelfId);
                    bool exchanged = target is not null && movedTarget is not null && movedSelf is not null
                        && target.Cell != snapshot.Fighters[self].Cell
                        && movedTarget.Cell == snapshot.Fighters[self].Cell && movedSelf.Cell == target.Cell;
                    if (exchanged)
                        rank += ClassMechanics.EvaluateXelorTelefrag(true,
                            target!.States?.Contains(target.Enemy ? ClassMechanics.StateTelefrag
                                : ClassMechanics.StateAllyTelefrag) == true);
                }
            }
            // Eliotrope : apres une pose de portail, les tirs a travers le reseau sont des suites legitimes (degats reels),
            // plus de bonus fixe par portail manquant (il faisait poser des portails sans jamais tirer).
            if (spells.Any(s => s.Cast.SpellId is 14574 or 14604))
                foreach (var follow in PortalShots(n))
                    rank = Math.Max(rank, Rank(follow, depth + 1));

            foreach (var sp in spells.Where(s => Affordable(n, s)))
                foreach (var f in snapshot.Fighters.Where((f, i) => f.Enemy && f.CanTarget
                    && n.Board!.Fighters[i].Visibility == FighterVisibility.Visible && n.Life[i] > 0))
                {
                    if (!budget.Take()) return rank;
                    int target = snapshot.Fighters.FindIndex(t => t.Id == f.Id);
                    if (Eligible(n, sp, target) && CanCast(n, sp, Position(n, target))
                        && CastOn(n, sp, Position(n, target), target) is { } follow)
                        rank = Math.Max(rank, Rank(follow, depth + 1));
                }
            return rank;
        }
        // A geometric range bonus cannot guide a detour around a wall. Seed actual legal
        // firing cells, then measure walking distance through the observed free board.
        // This is setup only; normal Reach still enforces MP, tackle and every policy step.
        var firingQueue = new Queue<int>();
        int firingBudget = Math.Min(budget.Limit - 1, budget.Used + budget.Limit / 8);
        bool needsFiringRoute = !spells.Any(sp => HasModeledEnemyDamage(sp.Cast) && Affordable(root, sp)
            && snapshot.Fighters.Select((f, i) => (f, i)).Any(t => t.f.Enemy && Eligible(root, sp, t.i)
                && CanCast(root, sp, t.f.Cell)));
        foreach (var tile in snapshot.Cells.Where(_ => needsFiringRoute).OrderBy(c => c.Id))
        {
            if (!budget.Take(firingBudget)) break;
            if (!tile.Walkable || Occupied(root, tile.Id)
                || snapshot.UnsafeForcedMovementCells?.Contains(tile.Id) == true) continue;
            var origin = root with { Cell = tile.Id, Repositioned = tile.Id != root.Cell };
            bool legal = false;
            foreach (var sp in spells.Where(s => s.AllowApproach && HasModeledEnemyDamage(s.Cast) && Affordable(origin, s)))
            {
                for (int i = 0; i < snapshot.Fighters.Length; i++)
                {
                    if (!budget.Take(firingBudget)) break;
                    if (snapshot.Fighters[i].Enemy && Eligible(origin, sp, i) && CanCast(origin, sp, Position(origin, i))
                        && CastOn(origin, sp, Position(origin, i), i) is { Damage: > 0 })
                    { legal = true; break; }
                }
                if (legal) break;
            }
            if (legal) { firingDistances[tile.Id] = 0; firingQueue.Enqueue(tile.Id); }
        }
        var incoming = new Dictionary<int, List<int>>();
        foreach (var c in snapshot.Cells)
        {
            if (!c.Walkable) continue;
            foreach (int nbr in c.Neighbours)
            {
                if (!incoming.TryGetValue(nbr, out var list)) incoming[nbr] = list = new();
                list.Add(c.Id);
            }
        }
        while (firingQueue.Count > 0 && budget.Take(firingBudget))
        {
            int at = firingQueue.Dequeue();
            if (!incoming.TryGetValue(at, out var sources)) continue;
            foreach (int next in sources.Order())
            {
                if (!budget.Take(firingBudget)) break;
                if (firingDistances.ContainsKey(next) || !cells.TryGetValue(next, out var tile) || !tile.Walkable
                    || Occupied(root, next) || snapshot.UnsafeForcedMovementCells?.Contains(next) == true
                    || !MoveAllowed(root, next, at)) continue;
                firingDistances[next] = firingDistances[at] + 1;
                firingQueue.Enqueue(next);
            }
        }
        Node best = root;
        PlannerScore bestScore = Score(root);
        bool bestLegal = snapshot.EndCells is null || snapshot.EndCells.Contains(root.Cell);
        bool aggressive = snapshot.Style == CombatStyle.Aggressive
            && !(snapshot.FleeBelowHpPercent > 0 && snapshot.Fighters[self].MaxLife > 0
                && snapshot.Fighters[self].Life * 100L < (long)snapshot.Fighters[self].MaxLife * snapshot.FleeBelowHpPercent);
        // Aggressive, above the flee threshold: a productive plan whose modeled post-shield reply
        // costs under half the caster's current life outranks any zero-productivity plan; totals
        // decide within a class. Heavier trades (e.g. 80 of 100 HP for a 40 hit) stay score-ranked.
        int Class(Node n, PlannerScore s) => aggressive
            && (s.Damage > 0 || s.Finishes > 0 || s.Healing > 0 || s.Shielding > 0 || s.Control > 0)
            && Math.Max(0, Exposure(n, n.Cell) - n.Shield[self]) * 2 < n.Life[self] ? 1 : 0;
        int bestClass = Class(best, bestScore);
        void Consider(Node n)
        {
            if (snapshot.EndCells is not null && !snapshot.EndCells.Contains(n.Cell)) return;
            var score = Score(n);
            int cls = Class(n, score);
            if (!bestLegal || cls > bestClass || cls == bestClass && (score.Total > bestScore.Total + .00001
                || Math.Abs(score.Total - bestScore.Total) < .00001 && (Rank(n) > Rank(best) + .00001 || Math.Abs(Rank(n) - Rank(best)) <= .00001 && n.Actions.Length < best.Actions.Length)))
            { best = n; bestScore = score; bestClass = cls; bestLegal = true; }
        }
        void Finish(Node n)
        {
            Consider(n);
            if (budget.Used >= budget.Limit) return;
            foreach (var w in Reach(n))
            {
                Consider(WalkTo(n, w));
                if (budget.Used >= budget.Limit) break;
            }
        }
        // Establish a legal walking end before casts can consume the budget when
        // the starting cell is forbidden. Otherwise keep in-range casts first
        // (live 02/11 regression: movement search starved the first attack).
        Consider(root);
        if (!bestLegal)
            foreach (var walk in Reach(root))
            {
                Consider(WalkTo(root, walk));
                if (bestLegal) break;
            }
        if (snapshot.MaxCasts > 0)
            foreach (var cast in Casts(root)) Consider(cast);
        Finish(root);
        var beam = new List<Node> { root };
        for (int depth = 0; depth < Math.Min(MaxDepth, snapshot.MaxCasts) && beam.Count > 0 && budget.Used < budget.Limit; depth++)
        {
            var next = new List<(Node Node, double Rank)>();
            foreach (var n in beam)
            {
                if (budget.Used >= budget.Limit) break;
                // Movement is grouped with its cast so firing cells cannot be pruned just for costing MP.
                // Reach applies the policy to every step. NeverApproach still permits
                // retreating to a firing line; it must not disable all pre-cast walking.
                var origins = Reach(n).Select(w => WalkTo(n, w))
                    .OrderBy(origin => Nearest(origin, origin.Cell)).ThenBy(origin => origin.Actions.Length).ThenBy(origin => origin.Cell);
                foreach (var origin in origins)
                {
                    if (budget.Used >= budget.Limit) break;
                    foreach (var cast in Casts(origin))
                    {
                        Consider(cast);
                        next.Add((cast, Rank(cast)));
                        if (budget.Used >= budget.Limit) break;
                    }
                }
            }
            // No lossy board signature: histories, shields and resources remain part of each branch.
            beam = next.OrderByDescending(n => n.Rank).ThenBy(n => n.Node.Actions.Length)
                .ThenBy(n => n.Node.Cell).Take(BeamWidth).Select(n => n.Node).ToList();
            foreach (var n in beam) Finish(n);
        }
        var targets = snapshot.Fighters.Select((f, i) => (f, i)).Where(t => t.f.Enemy && best.Life[t.i] < t.f.Life)
            .Select(t => new TeamTargetIntent(t.f.Id, t.f.Life - best.Life[t.i], best.Life[t.i] == 0, t.f.Life)).ToArray();
        var support = snapshot.Fighters.Select((f, i) => (f, i)).Where(t => !t.f.Enemy)
            .Select(t => new TeamSupportIntent(t.f.Id, Math.Max(0, best.Life[t.i] - t.f.Life), Math.Max(0, best.Shield[t.i] - t.f.Shield), t.f.Life, t.f.Shield))
            .Where(s => s.Heal > 0 || s.Shield > 0).ToArray();
        return new(best.Actions, bestScore, budget.Used, threatTruncated || budget.Used >= budget.Limit,
            new(snapshot.SelfId, 0, targets, best.Cell, support, 0), best.Unmodeled);
    }

    private static int FindIndex<T>(this ImmutableArray<T> items, Func<T, bool> predicate)
    {
        for (int i = 0; i < items.Length; i++) if (predicate(items[i])) return i;
        return -1;
    }
}
