using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GivreClone.Protocol;
using GivreClone.App.Services.LotM;

namespace GivreClone.App.Services.Combat;

/// <summary>Side is relative to the original caster, not a recursively casting summon.</summary>
public sealed record SpellFighter(long Id, int Cell, bool Enemy, double Life, double MaxLife,
    double Shield = 0, IReadOnlyDictionary<int, int>? Characteristics = null,
    IReadOnlyDictionary<DamageElement, int>? Resistances = null,
    IReadOnlyDictionary<DamageElement, int>? Reductions = null,
    IReadOnlySet<int>? States = null, bool Alive = true, double Ap = 12, double Mp = 6,
    int Breed = 0, int MonsterTemplate = 0, long? SummonerId = null, long? CarrierId = null,
    bool StatesKnown = true, bool BreedKnown = true, IReadOnlySet<int>? AbsentStates = null,
    FighterVisibility Visibility = FighterVisibility.Visible, int MonsterGrade = 0);

public sealed record SpellMark(int ActionId, int SpellId, long OwnerId, IReadOnlyList<int> Cells,
    int SourceSpellId = 0, int SourceGrade = 0, int Duration = 0, int SpellGrade = 0,
    bool AwaitingPortalPair = false);
public sealed record SpellStatBuff(int SpellId, long TargetId, int Characteristic, double Delta, int Duration,
    bool Points = false);
public sealed record SimulatedSpellModifier(int SpellId, long TargetId, int ActionId, int Value, int Duration);
public sealed record SimulatedState(long TargetId, int State, int Duration);
public sealed record SpellBoard(IReadOnlyList<SpellFighter> Fighters, IReadOnlyList<SpellMark>? Marks = null,
    bool ThroughPortal = false, IReadOnlyDictionary<int, SpellStateDamageRule>? StateDamageRules = null,
    IReadOnlyList<SpellStatBuff>? StatBuffs = null,
    IReadOnlySet<int>? KnownStatBuffSources = null,
    bool AllStatBuffSourcesKnown = false, IReadOnlyList<SimulatedSpellModifier>? SpellModifiers = null,
    IReadOnlyList<DeferredSpellEffect>? DeferredEffects = null,
    IReadOnlyList<SimulatedState>? TimedStates = null,
    Func<int, int, int, SummonGradeCharacteristics?>? SummonLookup = null);

public abstract record SpellResolutionEvent(int ActionId, long CasterId, long TargetId);
public sealed record DamageResolved(int Action, long Caster, long Target, double LifeLost,
    double ShieldLost, bool Enemy, bool Killed) : SpellResolutionEvent(Action, Caster, Target);
public sealed record HealingResolved(int Action, long Caster, long Target, double Amount) : SpellResolutionEvent(Action, Caster, Target);
public sealed record ShieldResolved(int Action, long Caster, long Target, double Amount) : SpellResolutionEvent(Action, Caster, Target);
public sealed record PointsResolved(int Action, long Caster, long Target, bool IsAp, double Delta, bool Enemy) : SpellResolutionEvent(Action, Caster, Target);
public sealed record StateResolved(int Action, long Caster, long Target, int State, bool Applied) : SpellResolutionEvent(Action, Caster, Target);
public sealed record VisibilityResolved(int Action, long Caster, long Target, FighterVisibility Visibility) : SpellResolutionEvent(Action, Caster, Target);
public sealed record StatResolved(int Action, long Caster, long Target, int Characteristic, int Delta) : SpellResolutionEvent(Action, Caster, Target);
public sealed record SpellModifierResolved(int Action, long Caster, long Target, int SpellId, int Value) : SpellResolutionEvent(Action, Caster, Target);
public sealed record MovementResolved(int Action, long Caster, long Target, int From, int To, bool Blocked) : SpellResolutionEvent(Action, Caster, Target);
public sealed record SummonResolved(int Action, long Caster, long Target, int Template, int Cell, int Grade = 0) : SpellResolutionEvent(Action, Caster, Target);
public sealed record MarkResolved(int Action, long Caster, SpellMark Mark) : SpellResolutionEvent(Action, Caster, Caster);
public sealed record SubCastResolved(int Action, long Caster, long Target, int SpellId, int Grade, int Cell) : SpellResolutionEvent(Action, Caster, Target);
public sealed record DeferredSpellEffect(int SpellId, long CasterId, int Cell, SpellEffectDescriptor Effect,
    long? TargetId = null)
{
    public string Triggers => Effect.Triggers;
}
public sealed record UnmodeledSpellEffect(int SpellId, int ActionId, string Reason, long? TargetId = null);

public sealed record ResolvedCast(SpellBoard Board, IReadOnlyList<SpellResolutionEvent> Events,
    IReadOnlyList<DeferredSpellEffect> Deferred, IReadOnlyList<UnmodeledSpellEffect> Unmodeled)
{
    public double EnemyDamage => Events.OfType<DamageResolved>().Where(e => e.Enemy).Sum(e => e.LifeLost + e.ShieldLost);
    public double FriendlyFire => Events.OfType<DamageResolved>().Where(e => !e.Enemy).Sum(e => e.LifeLost + e.ShieldLost);
    public int Kills => Events.OfType<DamageResolved>().Count(e => e.Killed && e.Enemy);
    public double Healing => Events.OfType<HealingResolved>().Sum(e => e.Amount);
    public double ShieldGained => Events.OfType<ShieldResolved>().Sum(e => e.Amount);
    public double EnemyApRemoved => Removed(true);
    public double EnemyMpRemoved => Removed(false);
    public double AllyApGiven => Given(true);
    public double AllyMpGiven => Given(false);
    public int StatesApplied => Events.OfType<StateResolved>().Count(e => e.Applied);
    public int StatesRemoved => Events.OfType<StateResolved>().Count(e => !e.Applied);
    public IReadOnlyList<MovementResolved> Movements => Events.OfType<MovementResolved>().ToArray();
    public IReadOnlyList<SummonResolved> Summons => Events.OfType<SummonResolved>().ToArray();
    public IReadOnlyList<MarkResolved> Marks => Events.OfType<MarkResolved>().ToArray();
    private double Removed(bool ap) => -Events.OfType<PointsResolved>().Where(e => e.Enemy && e.IsAp == ap && e.Delta < 0).Sum(e => e.Delta);
    private double Given(bool ap) => Events.OfType<PointsResolved>().Where(e => !e.Enemy && e.IsAp == ap && e.Delta > 0).Sum(e => e.Delta);
}

/// <summary>
/// Pure immediate-cast simulation. Rolls use their arithmetic mean, without critical hits; AP/MP
/// losses ignore dodge. Random-choice rows are explicitly unmodeled: applying their mean to
/// discrete states, movement and recursive casts would invent a board that cannot occur.
/// Deferred rows are retained, not executed. Buff duration/expiry and hooks need a turn simulator.
/// </summary>
public static class SpellResolver
{
    private static readonly IReadOnlyDictionary<int, int> EmptyStats = ImmutableDictionary<int, int>.Empty;
    /// <summary>Affliction's bound target-start damage, healing and trigger lifetime. Critical context and script rows remain unmodeled.</summary>
    public static ResolvedCast ResolveStartTurn(SpellBoard board, long actorId,
        Func<int, int, SpellCastDescriptor?> lookup, int casterLevel)
        => ResolveTurnTrigger(board, actorId, lookup, casterLevel, 29098, "TB");

    /// <summary>Extinction's observed target-end damage; critical and portal amplification context remain unmodeled.</summary>
    public static ResolvedCast ResolveEndTurn(SpellBoard board, long actorId,
        Func<int, int, SpellCastDescriptor?> lookup, int casterLevel)
        => ResolveTurnTrigger(board, actorId, lookup, casterLevel, 14594, "TE");

    private static ResolvedCast ResolveTurnTrigger(SpellBoard board, long actorId,
        Func<int, int, SpellCastDescriptor?> lookup, int casterLevel, int spellId, string trigger)
    {
        var events = new List<SpellResolutionEvent>();
        var unknown = new List<UnmodeledSpellEffect>();
        foreach (var pending in board.DeferredEffects ?? Array.Empty<DeferredSpellEffect>())
        {
            if (pending.SpellId != spellId || pending.TargetId != actorId || pending.Triggers != trigger) continue;
            int owner = -1, target = -1;
            for (int i = 0; i < board.Fighters.Count; i++)
            {
                if (board.Fighters[i].Id == pending.CasterId) owner = i;
                if (board.Fighters[i].Id == actorId) target = i;
            }
            if (owner < 0 || target < 0 || !board.Fighters[target].Alive) continue;
            if (!(spellId == 29098 && pending.Effect.ActionId is 96 or 2998
                || spellId == 14594 && pending.Effect.ActionId == 99))
            {
                unknown.Add(new(spellId, pending.Effect.ActionId, "Turn-trigger script is not simulated", actorId));
                continue;
            }
            var immediate = pending.Effect with { Triggers = "", Delay = 0, Conditional = false,
                TargetMask = string.Join(",", Tokens(pending.Effect.TargetMask)
                    .Where(token => token.TrimStart('*') is not ("R" or "r"))),
                Shape = 'P', Size = 0, MinSize = 0 };
            var spell = new SpellCastDescriptor(spellId, 0, 0, 0, 0, 0, false, 0, 0, 0, 0, "",
                ImmutableArray.Create(immediate));
            var resolved = Resolve(board, owner, board.Fighters[target].Cell, spell, lookup, casterLevel,
                triggeredDamage: true);
            board = resolved.Board;
            events.AddRange(resolved.Events);
            unknown.AddRange(resolved.Unmodeled);
        }
        board = board with
        {
            DeferredEffects = board.DeferredEffects?.Where(p => p.SpellId != spellId || p.TargetId != actorId
                || p.Triggers != trigger || p.Effect.EffectTriggerDuration > 1)
                .Select(p => p.SpellId == spellId && p.TargetId == actorId && p.Triggers == trigger
                    ? p with { Effect = p.Effect with { EffectTriggerDuration = p.Effect.EffectTriggerDuration - 1 } }
                    : p).ToImmutableArray()
        };
        return new(board, events.ToImmutableArray(), ImmutableArray<DeferredSpellEffect>.Empty, unknown.ToImmutableArray());
    }

    /// <summary>Consumes the observed Sram critical reward after a confirmed critical, including a revealing cast.</summary>
    public static SpellBoard ConsumeCriticalTriggers(SpellBoard board, long casterId)
    {
        if (board.DeferredEffects?.Any(p => p.SpellId == 21979 && p.CasterId == casterId
            && p.Triggers == "CC" && p.Effect.ActionId == 792 && p.Effect.DiceNum == 21979
            && p.Effect.DiceSide == 2) != true) return board;
        var removed = (board.StatBuffs ?? Array.Empty<SpellStatBuff>())
            .Where(b => b.SpellId == 21979 && b.TargetId == casterId).ToArray();
        return board with
        {
            Fighters = board.Fighters.Select(f =>
            {
                if (f.Id != casterId) return f;
                var stats = (f.Characteristics ?? EmptyStats).ToDictionary(p => p.Key, p => p.Value);
                foreach (var buff in removed)
                    stats[buff.Characteristic] = stats.GetValueOrDefault(buff.Characteristic) - (int)buff.Delta;
                return f with { Characteristics = stats.ToImmutableDictionary() };
            }).ToImmutableArray(),
            StatBuffs = (board.StatBuffs ?? Array.Empty<SpellStatBuff>())
                .Where(b => b.SpellId != 21979 || b.TargetId != casterId).ToImmutableArray(),
            DeferredEffects = board.DeferredEffects
                .Where(p => p.SpellId != 21979 || p.CasterId != casterId).ToImmutableArray()
        };
    }

    public static SpellBoard AdvanceStatBuffs(SpellBoard board)
    {
        if (board.StatBuffs is not { Count: > 0 } && board.SpellModifiers is not { Count: > 0 }
            && board.TimedStates is not { Count: > 0 }) return board;
        var expired = (board.StatBuffs ?? Array.Empty<SpellStatBuff>()).Where(b => b.Duration == 1).ToArray();
        var fighters = board.Fighters.Select(f =>
        {
            var stats = (f.Characteristics ?? EmptyStats).ToDictionary(x => x.Key, x => x.Value);
            double ap = f.Ap, mp = f.Mp;
            foreach (var buff in expired.Where(b => b.TargetId == f.Id))
            {
                if (buff.Points)
                {
                    if (buff.Characteristic == 1) ap = Math.Max(0, ap - buff.Delta);
                    else mp = Math.Max(0, mp - buff.Delta);
                }
                else stats[buff.Characteristic] = stats.GetValueOrDefault(buff.Characteristic) - (int)buff.Delta;
            }
            var expiredStates = (board.TimedStates ?? Array.Empty<SimulatedState>())
                .Where(s => s.TargetId == f.Id && s.Duration == 1).Select(s => s.State).ToHashSet();
            return f with { Characteristics = stats.ToImmutableDictionary(), Ap = ap, Mp = mp,
                States = f.States?.Except(expiredStates).ToImmutableHashSet(),
                AbsentStates = (f.AbsentStates ?? ImmutableHashSet<int>.Empty).Union(expiredStates).ToImmutableHashSet() };
        }).ToImmutableArray();
        return board with
        {
            Fighters = fighters,
            TimedStates = (board.TimedStates ?? Array.Empty<SimulatedState>()).Where(s => s.Duration > 1)
                .Select(s => s with { Duration = s.Duration - 1 }).ToImmutableArray(),
            StatBuffs = (board.StatBuffs ?? Array.Empty<SpellStatBuff>()).Where(b => b.Duration != 1)
                .Select(b => b.Duration > 1 ? b with { Duration = b.Duration - 1 } : b).ToImmutableArray(),
            SpellModifiers = (board.SpellModifiers ?? Array.Empty<SimulatedSpellModifier>()).Where(m => m.Duration != 1)
                .Select(m => m.Duration > 1 ? m with { Duration = m.Duration - 1 } : m).ToImmutableArray(),
            DeferredEffects = board.DeferredEffects?.Where(p => p.SpellId != 21979 || p.Triggers != "CC"
                || !expired.Any(b => b.SpellId == 21979 && b.TargetId == p.CasterId))
                .ToImmutableArray()
        };
    }

    public static bool IsDamageAction(int action) => action is >= 91 and <= 100 or 82 or 144 or 89 or 279 or 2822 or 2828 or 2832 or 2890 or 1223;
    private static bool SubCast(int a) => a is 1160 or 2960 or 2794 or 792 or 1017 or 1019 or 2160;
    private static bool Deferred(SpellEffectDescriptor e) => e.Delay > 0 || !string.IsNullOrWhiteSpace(e.Triggers) && e.Triggers.Trim() != "I";
    private static bool KnownShape(int s) => s == 0 || s is > 0 and <= char.MaxValue && "P#*+-/LACDGIOQTUVWXSa".Contains((char)s);
    private static bool KnownAction(int a) => IsDamageAction(a) || SubCast(a) || StatDelta(a).Id != 0 || a is
        81 or 108 or 143 or 407 or 1109 or >= 2998 and <= 3002 or 2973 or 786 or 90 or
        1020 or 1039 or 1040 or 111 or 128 or 120 or 1079 or 1080 or 168 or 169 or
        150 or 950 or 951 or 952 or 5 or 783 or 1103 or 6 or 1042 or 1041 or 4 or 8 or 1104 or 1105 or 1106 or
        181 or 1011 or 1008 or 405 or 400 or 401 or 402 or 1091 or 1165 or 2022 or 1181 or
        406 or 1183 or 280 or 281 or 290 or 295 or 320 or 666;

    /// <summary>Static row coverage, including masks/conditions, but not runtime lookup or collision failures.</summary>
    public static IReadOnlyList<UnmodeledSpellEffect> ResolverCoverage(SpellCastDescriptor spell)
    {
        var errors = new List<UnmodeledSpellEffect>();
        foreach (var e in spell.Effects.IsDefault ? ImmutableArray<SpellEffectDescriptor>.Empty : spell.Effects)
        {
            if (!Deferred(e) && RowProblem(e) is { } reason) errors.Add(new(spell.SpellId, e.ActionId, reason));
            else if (!Deferred(e) && e.ActionId == 406
                && ((e.Value != 0 ? e.Value : e.DiceNum) != spell.SpellId
                    || spell.Effects.Any(row => row.ActionId is not (406 or 111 or 128)
                        && StatDelta(row.ActionId).Id == 0)))
                errors.Add(new(spell.SpellId, e.ActionId, "Source-spell refresh requires a complete stat-only self source"));
        }
        return errors;
    }

    public static bool IsImmediateModeled(SpellEffectDescriptor e) => !Deferred(e) && RowProblem(e) is null;

    /// <summary>Turn-trigger damage rows that ResolveStartTurn/ResolveEndTurn actually resolve and the planner scores.</summary>
    public static bool IsModeledTurnTrigger(int spellId, SpellEffectDescriptor e) => e.Delay == 0 && RowProblem(e) is null
        && (spellId == 14594 && e.Triggers == "TE" && e.ActionId is 99 or 3793
            || spellId == 29098 && e.Triggers == "TB" && e.ActionId is 96 or 2998 or 3793);

    public static string? RowProblem(SpellEffectDescriptor e)
    {
        if (e.Random != 0) return "Random-choice row: mutually exclusive outcome probabilities are unavailable";
        if (!string.IsNullOrWhiteSpace(e.Condition)) return "Effect condition expression: " + e.Condition;
        if (e.Conditional && e.Delay == 0 && e.Random == 0 && string.IsNullOrWhiteSpace(e.Triggers) && string.IsNullOrWhiteSpace(e.Condition))
            return "Legacy conditional row without raw condition data";
        if (!KnownAction(e.ActionId)) return "Unsupported action";
        if (e.ActionId == 1223) return "Requires triggering final damage, unavailable for an immediate cast";
        if (!KnownShape(e.Shape)) return "Unsupported zone shape: " + e.Shape;
        foreach (string token in Tokens(e.TargetMask))
            if (!KnownToken(token.TrimStart('*'))) return "Unsupported target mask: " + token;
        return null;
    }

    private static string[] Tokens(string? mask) => (mask ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static bool KnownToken(string t) => t is "a" or "A" or "g" or "c" or "C" or "i" or "I" or "j" or "J" or
        "h" or "H" or "l" or "L" or "m" or "M" or "d" or "D" or "P" or "p" or "PB" or "pb" or "R" or "r" or "K" or "x" or "T"
        || t.Length > 1 && "EeFfVvBb".Contains(t[0]) && int.TryParse(t.AsSpan(1), out _);

    private static (int Id, int Sign) StatDelta(int a) => a switch
    {
        117 => (CharIds.Range, 1), 116 => (CharIds.Range, -1),
        118 => (CharIds.Strength, 1), 157 => (CharIds.Strength, -1),
        119 => (CharIds.Agility, 1), 154 => (CharIds.Agility, -1),
        123 => (CharIds.Chance, 1), 152 => (CharIds.Chance, -1),
        126 => (CharIds.Intelligence, 1), 155 => (CharIds.Intelligence, -1),
        138 => (CharIds.DamagePercent, 1), 186 => (CharIds.DamagePercent, -1),
        112 => (CharIds.AllDamageBonus, 1), 145 => (CharIds.AllDamageBonus, -1),
        115 => (CharIds.CriticalHit, 1), 171 => (CharIds.CriticalHit, -1),
        414 => (CharIds.PushDamageBonus, 1), 415 => (CharIds.PushDamageBonus, -1),
        418 => (CharIds.CriticalDamageBonus, 1), 419 => (CharIds.CriticalDamageBonus, -1),
        1171 => (CharIds.DealtDamageMultiplier, 1), 1172 => (CharIds.DealtDamageMultiplier, -1),
        2971 => (CharIds.DealtHealMultiplier, 1), 2972 => (CharIds.DealtHealMultiplier, -1),
        _ => (0, 0)
    };

    /// <summary>Called after an entity's confirmed portal teleport, never for a projected spell shot.</summary>
    public static ResolvedCast ResolvePortalTraversal(SpellBoard board, int traverserIndex,
        Func<int, int, SpellCastDescriptor?> lookup, int casterLevel)
    {
        if (traverserIndex < 0 || traverserIndex >= board.Fighters.Count
            || !board.Fighters[traverserIndex].Alive || board.Fighters[traverserIndex].Life <= 0)
            throw new ArgumentOutOfRangeException(nameof(traverserIndex));
        var events = new List<SpellResolutionEvent>();
        var deferred = new List<DeferredSpellEffect>();
        var unknown = new List<UnmodeledSpellEffect>();
        foreach (var pending in board.DeferredEffects ?? Array.Empty<DeferredSpellEffect>())
        {
            // CPT is only established here for Entraide's installed subcast.
            // The spell description and live capture establish entity traversal;
            // unrelated triggers keep their existing deferred contract.
            var effect = pending.Effect;
            if (pending.SpellId != 14596 || pending.Triggers != "CPT" || effect.ActionId != 1160
                || effect.DiceNum != 14598 || effect.DiceSide != 1) continue;
            int owner = -1;
            for (int i = 0; i < board.Fighters.Count; i++)
                if (board.Fighters[i].Id == pending.CasterId) { owner = i; break; }
            if (owner < 0 || !board.Fighters[owner].Alive || board.Fighters[owner].Life <= 0
                || board.Fighters[owner].States?.Contains(6252) != true) continue;
            if (lookup(effect.DiceNum, effect.DiceSide) is not { } reward)
            {
                unknown.Add(new(pending.SpellId, effect.ActionId, "Missing Entraide traversal subcast", pending.CasterId));
                continue;
            }
            var resolved = Resolve(board, owner, board.Fighters[owner].Cell, reward, lookup, casterLevel);
            board = resolved.Board;
            events.AddRange(resolved.Events);
            deferred.AddRange(resolved.Deferred);
            unknown.AddRange(resolved.Unmodeled);
        }
        return new(board, events.ToImmutableArray(), deferred.ToImmutableArray(), unknown.ToImmutableArray());
    }

    public static ResolvedCast Resolve(SpellBoard board, int casterIndex, int castCell, SpellCastDescriptor spell,
        Func<int, int, SpellCastDescriptor?> lookup, int casterLevel, Func<int, bool>? passable = null,
        int? areaOrigin = null, double damageMultiplier = 1, bool critical = false, bool triggeredDamage = false)
    {
        // Copy every collection so neither this cast nor subsequent caller mutations alter the input/output snapshots.
        var fighters = board.Fighters.Select(f => f with
        {
            Characteristics = (f.Characteristics ?? EmptyStats).ToImmutableDictionary(),
            Resistances = f.Resistances?.ToImmutableDictionary(), Reductions = f.Reductions?.ToImmutableDictionary(),
            States = f.States?.ToImmutableHashSet() ?? ImmutableHashSet<int>.Empty,
            AbsentStates = f.AbsentStates?.ToImmutableHashSet() ?? ImmutableHashSet<int>.Empty
        }).ToList();
        var marks = (board.Marks ?? Array.Empty<SpellMark>()).Select(m => m with { Cells = m.Cells.ToImmutableArray() }).ToList();
        var statBuffs = (board.StatBuffs ?? Array.Empty<SpellStatBuff>()).ToList();
        var spellModifiers = (board.SpellModifiers ?? Array.Empty<SimulatedSpellModifier>()).ToList();
        var timedStates = (board.TimedStates ?? Array.Empty<SimulatedState>()).ToList();
        var events = new List<SpellResolutionEvent>();
        var deferred = new List<DeferredSpellEffect>();
        var unknown = new List<UnmodeledSpellEffect>();
        var active = new HashSet<(int Spell, int Grade, int Cell)>();
        var telefragged = new HashSet<long>();
        var consumedResonance = new HashSet<(long Caster, long Target)>();
        bool Pass(int cell) => cell is >= 0 and < 560 && (passable?.Invoke(cell) ?? true);
        bool Occupied(int cell, int except = -1) => fighters.Where((f, i) => i != except).Any(f => f.Alive && f.Life > 0 && f.Cell == cell);
        void Problem(SpellCastDescriptor s, SpellEffectDescriptor e, string why, int? target = null) => unknown.Add(new(s.SpellId, e.ActionId, why, target is int t ? fighters[t].Id : null));
        void Heal(int c, int t, SpellCastDescriptor s, SpellEffectDescriptor e, double amount)
        {
            var f = fighters[t];
            // SpellStateData 76 is incurable; 5920 shares its icon but is not.
            if (!f.Alive || f.Life <= 0 || f.States!.Contains(76)) return;
            double effective = Math.Min(Math.Max(0, amount), Math.Max(0, f.MaxLife - f.Life));
            if (effective > 0 && !f.StatesKnown && !f.AbsentStates!.Contains(76))
            {
                Problem(s, e, "Incurable state absence is unobserved", t);
                return;
            }
            fighters[t] = f with { Life = f.Life + effective };
            events.Add(new HealingResolved(e.ActionId, fighters[c].Id, f.Id, effective));
        }
        double Damage(int c, int t, int action, double amount)
        {
            var f = fighters[t];
            if (board.StateDamageRules is { } rules
                && (f.States!.Any(state => rules.TryGetValue(state, out var rule) && rule.Invulnerable)
                    || fighters[c].States!.Any(state => rules.TryGetValue(state, out var rule) && rule.CantDealDamage)))
                amount = 0;
            double shield = Math.Min(Math.Max(0, f.Shield), Math.Max(0, amount));
            double life = Math.Min(Math.Max(0, f.Life), Math.Max(0, amount - shield));
            bool killed = f.Alive && f.Life > 0 && life >= f.Life;
            fighters[t] = f with { Life = f.Life - life, Shield = f.Shield - shield, Alive = f.Alive && !killed };
            events.Add(new DamageResolved(action, fighters[c].Id, f.Id, life, shield, f.Enemy, killed));
            foreach (var pending in (board.DeferredEffects ?? Array.Empty<DeferredSpellEffect>()).Concat(deferred)
                .Where(p => p.SpellId == 14611 && p.TargetId == f.Id && p.Triggers == "D|XD"
                    && p.Effect.ActionId == 1160 && p.Effect.DiceNum == 31020).ToArray())
            {
                if (consumedResonance.Contains((pending.CasterId, f.Id)) || life <= 0 && shield <= 0) continue;
                if (shield > 0 || killed || fighters[c].Id != pending.CasterId || f.States!.Contains(678))
                {
                    unknown.Add(new(14611, 1182, "Resonance shield, lethal, third-party or forbidden traversal is unobserved", f.Id));
                    continue;
                }
                var network = new List<int>();
                foreach (var mark in marks.Where(m => m.OwnerId == pending.CasterId))
                    if (mark.ActionId == 1181)
                        foreach (int tile in mark.Cells) { network.Remove(tile); network.Add(tile); }
                    else if (mark.ActionId == 1183) network.RemoveAll(mark.Cells.Contains);
                network.Remove(f.Cell);
                network.Add(f.Cell);
                while (network.Count > 4) network.RemoveAt(0);
                var route = ClassMechanics.ProjectThroughPortals(f.Cell, f.Cell, network);
                if (network.Count > 1 && route is null)
                {
                    unknown.Add(new(14611, 1182, "Resonance portal route is ambiguous", f.Id));
                    continue;
                }
                if (route is { } traversal && (!Pass(traversal.Exit) || Occupied(traversal.Exit, t)))
                {
                    unknown.Add(new(14611, 1182, "Resonance occupied or impassable exit is unobserved", f.Id));
                    continue;
                }
                consumedResonance.Add((pending.CasterId, f.Id));
                fighters[t] = fighters[t] with { States = fighters[t].States!.Where(state => state != 6251).ToImmutableHashSet() };
                events.Add(new StateResolved(951, pending.CasterId, f.Id, 6251, false));
                var placed = new SpellMark(1181, 14611, pending.CasterId, ImmutableArray.Create(f.Cell));
                marks.Add(placed);
                events.Add(new MarkResolved(1181, pending.CasterId, placed));
                if (route is { } teleport)
                {
                    Move(c, t, 1182, teleport.Exit);
                    for (int i = 0; i < marks.Count; i++)
                        if (marks[i].OwnerId == pending.CasterId && marks[i].ActionId == 1181
                            && marks[i].Cells.Any(tile => tile == f.Cell || tile == teleport.Exit))
                            marks[i] = marks[i] with { ActionId = 1183 };
                    foreach (var support in (board.DeferredEffects ?? Array.Empty<DeferredSpellEffect>()).Concat(deferred)
                        .Where(p => p.SpellId == 14596 && p.Triggers == "CPT" && p.Effect.ActionId == 1160
                            && p.Effect.DiceNum == 14598 && p.Effect.DiceSide == 1).ToArray())
                    {
                        int owner = fighters.FindIndex(actor => actor.Id == support.CasterId
                            && actor.Alive && actor.Life > 0 && actor.States!.Contains(6252));
                        if (owner < 0) continue;
                        if (lookup(14598, 1) is not { } reward)
                        {
                            unknown.Add(new(14596, 1160, "Missing Entraide traversal subcast", support.CasterId));
                            continue;
                        }
                        Cast(owner, fighters[owner].Cell, reward, 1, fighters[owner].Cell);
                    }
                }
            }
            foreach (var pending in (board.DeferredEffects ?? Array.Empty<DeferredSpellEffect>()).Concat(deferred)
                .Where(p => p.SpellId == 14577 && p.TargetId == f.Id && p.Triggers == "D|XD"
                    && p.Effect.ActionId == 1160 && p.Effect.DiceNum == 14578).ToArray())
            {
                int owner = fighters.FindIndex(actor => actor.Id == pending.CasterId && actor.Alive && actor.Life > 0);
                if (owner < 0 || life <= 0 && shield <= 0) continue;
                // The live capture establishes unshielded, nonlethal damage from the owner.
                // Keep the other event boundaries explicit until they have equivalent evidence.
                if (shield > 0 || killed || fighters[c].Id != pending.CasterId)
                {
                    unknown.Add(new(pending.SpellId, pending.Effect.ActionId,
                        "Distribution shield, lethal or third-party damage trigger is unobserved", f.Id));
                    continue;
                }
                var reward = lookup(pending.Effect.DiceNum, pending.Effect.DiceSide);
                if (reward is null)
                {
                    unknown.Add(new(pending.SpellId, pending.Effect.ActionId, "Missing Distribution damage subcast", f.Id));
                    continue;
                }
                foreach (var heal in reward.Effects.Where(row => row.ActionId == 2973))
                {
                    if (RowProblem(heal) is { } reason) { Problem(reward, heal, reason, t); continue; }
                    var zone = Zone(heal, fighters[t].Cell, fighters[owner].Cell);
                    foreach (int beneficiary in Enumerable.Range(0, fighters.Count)
                        .Where(i => fighters[i].Alive && fighters[i].Life > 0 && Mask(reward, heal, owner, i, zone)))
                        Heal(owner, beneficiary, reward, heal, Math.Floor(life * (heal.Min + (double)heal.Max) / 200));
                }
            }
            return life;
        }
        void Move(int c, int t, int action, int destination, bool swap = false)
        {
            var f = fighters[t];
            int other = fighters.FindIndex(x => x.Alive && x.Life > 0 && x.Cell == destination && x.Id != f.Id);
            bool blocked = !Pass(destination) || other >= 0 && !swap;
            if (!blocked)
            {
                fighters[t] = f with { Cell = destination };
                if (other >= 0)
                {
                    var o = fighters[other];
                    fighters[other] = o with { Cell = f.Cell };
                    telefragged.Add(f.Id);
                    telefragged.Add(o.Id);
                    events.Add(new MovementResolved(action, fighters[c].Id, o.Id, o.Cell, f.Cell, false));
                }
            }
            events.Add(new MovementResolved(action, fighters[c].Id, f.Id, f.Cell, blocked ? f.Cell : destination, blocked));
        }
        void Walk(int c, int t, int action, int toward, int steps, bool away, SpellCastDescriptor s, SpellEffectDescriptor e)
        {
            int start = fighters[t].Cell, cell = start;
            var (sx, sy) = MapPathfinder.MapPointOf(start);
            var (tx, ty) = MapPathfinder.MapPointOf(toward);
            int dx = Math.Sign(tx - sx) * (away ? -1 : 1), dy = Math.Sign(ty - sy) * (away ? -1 : 1);
            bool blocked = false;
            int remainingSteps = 0;
            for (int n = 0; n < Math.Clamp(steps, 0, 560); n++)
            {
                var (x, y) = MapPathfinder.MapPointOf(cell);
                int next = MapPathfinder.CellFromMapPoint(x + dx, y + dy);
                if (next == cell || !Pass(next) || Occupied(next, t))
                {
                    blocked = true;
                    remainingSteps = steps - n;
                    break;
                }
                cell = next;
            }
            fighters[t] = fighters[t] with { Cell = cell };
            events.Add(new MovementResolved(action, fighters[c].Id, fighters[t].Id, start, cell, blocked));
            if (blocked && action == 5 && remainingSteps > 0 && fighters[t].Enemy && !Occupied(MapPathfinder.CellFromMapPoint(MapPathfinder.MapPointOf(cell).X + dx, MapPathfinder.MapPointOf(cell).Y + dy), t))
            {
                double dopouDmg = Math.Max(8.0, ((casterLevel / 2.0) + 32.0) * remainingSteps / 4.0);
                Damage(c, t, action, dopouDmg);
            }
            else if (blocked && action is 5 or 783) Problem(s, e, "Push collision damage is not modeled", t);
        }
        IReadOnlyList<int> Zone(SpellEffectDescriptor e, int cell, int origin)
        {
            int shape = e.Shape == 'a' ? 'A' : e.Shape;
            int radius = Math.Clamp(e.Size, 0, 63), min = Math.Clamp(e.MinSize, 0, 63);
            var cells = shape == 'S' ? CombatZones.SimpleShape('S', cell, min, radius)
                : CombatZones.GetCells(shape, cell, CombatZones.OrientationTo(origin, cell), min, radius, Pass);
            // GetCells does not apply minSize to all shapes. Remove the inner copy of the same geometry.
            if (min > 0 && shape is 'C' or 'X' or 'D' or 'G' or 'T' or 'U' or 'V' or 'W')
            {
                var inner = CombatZones.GetCells(shape, cell, CombatZones.OrientationTo(origin, cell), 0, min - 1, Pass).ToHashSet();
                return cells.Where(x => !inner.Contains(x) && Pass(x)).ToArray();
            }
            return cells.Where(Pass).ToArray();
        }
        bool Mask(SpellCastDescriptor s, SpellEffectDescriptor e, int c, int t, IReadOnlyList<int> zone, int? selectionCell = null)
        {
            var caster = fighters[c]; var target = fighters[t];
            var tokens = Tokens(e.TargetMask);
            bool category = false, hasCategory = false, globalCaster = tokens.Contains("C");
            bool telefragTarget = tokens.Contains("T") && telefragged.Contains(target.Id);
            if (!zone.Contains(selectionCell ?? target.Cell) && !(globalCaster && target.Id == caster.Id) && !telefragTarget) return false;
            foreach (var raw in tokens)
            {
                bool star = raw.StartsWith('*'); string token = raw.TrimStart('*');
                var f = star ? caster : target;
                bool ally = f.Enemy == caster.Enemy, self = f.Id == caster.Id, summon = f.SummonerId.HasValue;
                bool? select = token switch
                {
                    "a" => ally, "A" => !ally, "g" => ally && !self, "c" or "C" => self,
                    "i" or "j" => ally && summon, "I" or "J" => !ally && summon,
                    "h" or "l" => ally && !summon && f.MonsterTemplate == 0,
                    "H" or "L" => !ally && !summon && f.MonsterTemplate == 0,
                    "m" => ally && !summon && f.MonsterTemplate != 0,
                    "M" => !ally && !summon && f.MonsterTemplate != 0,
                    "d" or "D" or "x" => false,
                    _ => null
                };
                if (select is bool selected)
                {
                    if (star) { if (!selected) return false; }
                    else { hasCategory = true; category |= selected; }
                    continue;
                }
                bool condition = token switch
                {
                    "P" => !summon || f.SummonerId == caster.Id,
                    "p" => !summon || f.SummonerId != caster.Id,
                    "PB" => f.Shield > 0, "pb" => f.Shield <= 0,
                    "R" => board.ThroughPortal, "r" => !board.ThroughPortal,
                    "K" => f.CarrierId == caster.Id,
                    "T" => telefragged.Contains(f.Id),
                    _ => true
                };
                if (token.Length > 1 && int.TryParse(token.AsSpan(1), out int value))
                {
                    if (token[0] is 'B' or 'b' && !f.BreedKnown)
                    {
                        Problem(s, e, "Required breed is unobserved", star ? c : t);
                        return false;
                    }
                    if (token[0] is 'V' or 'v' && f.MaxLife <= 0)
                    {
                        Problem(s, e, "Required life percentage is unobserved", star ? c : t);
                        return false;
                    }
                    if (token[0] is 'E' or 'e' && !f.StatesKnown && !f.States!.Contains(value)
                        && !f.AbsentStates!.Contains(value))
                    {
                        Problem(s, e, "Required state presence or absence is unobserved", star ? c : t);
                        return false;
                    }
                    condition = token[0] switch
                    {
                        'E' => f.States!.Contains(value), 'e' => !f.States!.Contains(value),
                        // Positive template/breed alternatives are OR'd; negative filters are all required.
                        'F' => tokens.Where(x => x.StartsWith(star ? "*F" : "F")).Any(x => int.TryParse(x.AsSpan(star ? 2 : 1), out int v) && f.MonsterTemplate == v),
                        'f' => f.MonsterTemplate != value,
                        'B' => tokens.Where(x => x.StartsWith(star ? "*B" : "B")).Any(x => int.TryParse(x.AsSpan(star ? 2 : 1), out int v) && f.Breed == v),
                        'b' => f.Breed != value,
                        'V' => f.Life * 100 < f.MaxLife * value, 'v' => f.Life * 100 >= f.MaxLife * value,
                        _ => false
                    };
                }
                if (!condition) return false;
            }
            return (!hasCategory || category) && (zone.Contains(selectionCell ?? target.Cell) || globalCaster && target.Id == caster.Id || telefragTarget);
        }
        void Cast(int c, int cell, SpellCastDescriptor s, int depth, int origin)
        {
            var key = (s.SpellId, s.Grade, cell);
            if (depth > 4 || !active.Add(key))
            {
                unknown.Add(new(s.SpellId, 0, depth > 4 ? "Sub-cast depth exceeds four" : "Recursive spell/grade/cell cycle"));
                return;
            }
            double dealt = 0;
            var outerTelefragged = telefragged;
            telefragged = new HashSet<long>();
            var previousBuffs = statBuffs.ToArray();
            var initialCells = fighters.Select(f => f.Cell).ToArray();
            foreach (var e in s.Effects.IsDefault ? ImmutableArray<SpellEffectDescriptor>.Empty : s.Effects)
            {
                if (Deferred(e))
                {
                    // Projection is a property of this cast, not of the later trigger.
                    var projectionMasks = Tokens(e.TargetMask).Select(token => token.TrimStart('*'));
                    if (projectionMasks.Any(token => token == "R" && !board.ThroughPortal
                        || token == "r" && board.ThroughPortal)) continue;
                    if (e.Triggers == "D|XD" && (s.SpellId == 14611
                        || s.SpellId == 14577 && e.ActionId == 1160 && e.DiceNum == 14578)
                        || s.SpellId == 14594 && e.Triggers == "TE" && e.ActionId is 99 or 3793
                        || s.SpellId == 29098 && e.Triggers == "TB" && e.ActionId is 96 or 2998 or 3793)
                    {
                        var triggerZone = Zone(e, cell, origin);
                        foreach (var target in Enumerable.Range(0, fighters.Count)
                            .Where(t => fighters[t].Alive && fighters[t].Life > 0 && Mask(s, e, c, t, triggerZone)))
                            deferred.Add(new(s.SpellId, fighters[c].Id, cell, e, fighters[target].Id));
                    }
                    else deferred.Add(new(s.SpellId, fighters[c].Id, cell, e));
                    continue;
                }
                if (RowProblem(e) is { } reason) { Problem(s, e, reason); continue; }
                // Installed DataCenter.ActionId (UInt16): Noop = 666.
                if (e.ActionId == 666) continue;
                var zone = Zone(e, cell, origin);
                var targets = Enumerable.Range(0, fighters.Count).Where(t => fighters[t].Alive && fighters[t].Life > 0
                    && Mask(s, e, c, t, zone, e.ActionId is 1041 or 1042 && t < initialCells.Length ? initialCells[t] : null)).ToList();
                int a = e.ActionId;
                double amount = Math.Max(0, ((double)e.Min + e.Max) / 2);
                if (a == 1183)
                {
                    var disabled = marks.Where(m => m.ActionId == 1181 && m.Cells.Any(zone.Contains)).ToArray();
                    foreach (var portal in disabled)
                    {
                        marks.Remove(portal);
                        var remaining = portal.Cells.Where(tile => !zone.Contains(tile)).ToImmutableArray();
                        if (remaining.Length > 0) marks.Add(portal with { Cells = remaining });
                        var inactive = new SpellMark(1183, s.SpellId, fighters[c].Id,
                            portal.Cells.Where(zone.Contains).ToImmutableArray(), s.SpellId, s.Grade, e.Duration);
                        marks.Add(inactive);
                        events.Add(new MarkResolved(a, fighters[c].Id, inactive));
                    }
                    continue;
                }
                if (a == 406)
                {
                    int source = e.Value != 0 ? e.Value : e.DiceNum;
                    var sourceSpell = source == s.SpellId ? s : lookup(source, s.Grade);
                    if ((!board.AllStatBuffSourcesKnown && board.KnownStatBuffSources?.Contains(source) != true) || sourceSpell is null
                        || sourceSpell.Effects.Any(row => row.ActionId is not (406 or 111 or 128)
                            && StatDelta(row.ActionId).Id == 0))
                    {
                        Problem(s, e, "Complete stat-only source-spell buff inventory is unavailable");
                        continue;
                    }
                    foreach (int t in targets)
                    {
                        var target = fighters[t];
                        var stats = (target.Characteristics ?? EmptyStats).ToDictionary(x => x.Key, x => x.Value);
                        foreach (var buff in statBuffs.Where(b => b.SpellId == source && b.TargetId == target.Id
                            && (source != s.SpellId || previousBuffs.Any(old => ReferenceEquals(old, b)))))
                        {
                            if (buff.Points)
                                target = buff.Characteristic == 1
                                    ? target with { Ap = Math.Max(0, target.Ap - buff.Delta) }
                                    : target with { Mp = Math.Max(0, target.Mp - buff.Delta) };
                            else stats[buff.Characteristic] = stats.GetValueOrDefault(buff.Characteristic) - (int)buff.Delta;
                        }
                        statBuffs.RemoveAll(b => b.SpellId == source && b.TargetId == target.Id
                            && (source != s.SpellId || previousBuffs.Any(old => ReferenceEquals(old, b))));
                        fighters[t] = target with { Characteristics = stats.ToImmutableDictionary() };
                    }
                    continue;
                }
                // Ground-targeted teleports (Bond uses a,A) move the caster even though
                // the destination zone contains no fighter. Preserve its mask conditions.
                if (a == 4 && targets.Count == 0 && !Occupied(cell)
                    && Mask(s, e, c, c, new[] { fighters[c].Cell })) targets.Add(c);
                if (a == 783)
                {
                    var (cx, cy) = MapPathfinder.MapPointOf(fighters[c].Cell);
                    var (tx, ty) = MapPathfinder.MapPointOf(cell);
                    int dx = Math.Sign(tx - cx), dy = Math.Sign(ty - cy);
                    int distance = Math.Abs(tx - cx) + Math.Abs(ty - cy);
                    if (!((dx == 0) ^ (dy == 0)) || distance < 2 || !Pass(cell) || Occupied(cell)) continue;
                    int adjacent = MapPathfinder.CellFromMapPoint(cx + dx, cy + dy);
                    int pushed = fighters.FindIndex(f => f.Alive && f.Life > 0 && f.Cell == adjacent);
                    if (pushed >= 0 && Mask(s, e, c, pushed, new[] { adjacent }))
                        Walk(c, pushed, a, fighters[c].Cell, distance - 1, true, s, e);
                    continue;
                }
                if (a == 405)
                {
                    foreach (int t in targets)
                    {
                        var replaced = fighters[t];
                        // Official Sadida capture: the tree dies, then a new actor appears on its cell.
                        // Never remove the actor if the replacement's catalogue rank is unavailable.
                        var stats = board.SummonLookup?.Invoke(e.DiceNum, e.DiceSide, casterLevel);
                        if (stats is null) { Problem(s, e, "Replacement summon statistics are unavailable", t); continue; }
                        long id = Math.Min(0, fighters.Min(f => f.Id)) - 1;
                        fighters[t] = replaced with { Alive = false, Life = 0 };
                        var resistances = new Dictionary<DamageElement, int>
                        {
                            [DamageElement.Earth] = stats.Characteristics.GetValueOrDefault(CharIds.ResistEarth),
                            [DamageElement.Fire] = stats.Characteristics.GetValueOrDefault(CharIds.ResistFire),
                            [DamageElement.Water] = stats.Characteristics.GetValueOrDefault(CharIds.ResistWater),
                            [DamageElement.Air] = stats.Characteristics.GetValueOrDefault(CharIds.ResistAir),
                            [DamageElement.Neutral] = stats.Characteristics.GetValueOrDefault(CharIds.ResistNeutral)
                        };
                        fighters.Add(new(id, replaced.Cell, fighters[c].Enemy, stats.Life, stats.Life,
                            Characteristics: stats.Characteristics.ToImmutableDictionary(), Resistances: resistances.ToImmutableDictionary(),
                            Ap: stats.Ap, Mp: stats.Mp, States: ImmutableHashSet<int>.Empty,
                            SummonerId: fighters[c].Id, MonsterTemplate: e.DiceNum, MonsterGrade: e.DiceSide,
                            AbsentStates: ImmutableHashSet<int>.Empty));
                        events.Add(new SummonResolved(a, fighters[c].Id, id, e.DiceNum, replaced.Cell, e.DiceSide));
                    }
                    continue;
                }
                if (a is 181 or 1011 or 1008)
                {
                    if (!Pass(cell) || Occupied(cell)) continue;
                    // Without a catalogue provider, retain the existing occupancy-only approximation.
                    if (Tokens(e.TargetMask).Any(t => t is not ("x" or "a" or "A" or "C" or "c")))
                    { Problem(s, e, "Summon placement has fighter-dependent mask conditions"); continue; }
                    long id = Math.Min(0, fighters.Min(f => f.Id)) - 1;
                    int template = e.DiceNum != 0 ? e.DiceNum : e.Min;
                    int grade = a is 181 or 1008 ? e.DiceSide : 0;
                    var stats = board.SummonLookup?.Invoke(template, grade, casterLevel);
                    var summoned = new SpellFighter(id, cell, fighters[c].Enemy, stats?.Life ?? 1, stats?.Life ?? 1,
                        Characteristics: stats?.Characteristics.ToImmutableDictionary(),
                        Ap: stats?.Ap ?? 12, Mp: stats?.Mp ?? 6, States: ImmutableHashSet<int>.Empty,
                        SummonerId: fighters[c].Id, MonsterTemplate: template, MonsterGrade: grade,
                        AbsentStates: ImmutableHashSet<int>.Empty);
                    if (stats is not null)
                        summoned = summoned with { Resistances = new Dictionary<DamageElement, int>
                        {
                            [DamageElement.Earth] = stats.Characteristics.GetValueOrDefault(CharIds.ResistEarth),
                            [DamageElement.Fire] = stats.Characteristics.GetValueOrDefault(CharIds.ResistFire),
                            [DamageElement.Water] = stats.Characteristics.GetValueOrDefault(CharIds.ResistWater),
                            [DamageElement.Air] = stats.Characteristics.GetValueOrDefault(CharIds.ResistAir),
                            [DamageElement.Neutral] = stats.Characteristics.GetValueOrDefault(CharIds.ResistNeutral)
                        }.ToImmutableDictionary() };
                    fighters.Add(summoned);
                    events.Add(new SummonResolved(a, fighters[c].Id, id, template, cell, grade));
                    continue;
                }
                if (a is 400 or 401 or 402 or 1091 or 1165 or 2022 or 1181)
                {
                    // Ground marks do not need an occupied target, but their
                    // caster conditions still select the applicable effect row.
                    var casterConditions = Tokens(e.TargetMask).Where(token => token.StartsWith('*')).ToArray();
                    if (casterConditions.Length > 0 && !Mask(s,
                        e with { TargetMask = "C," + string.Join(",", casterConditions) }, c, c, new[] { fighters[c].Cell }))
                        continue;
                    var mark = new SpellMark(a, e.DiceNum, fighters[c].Id, zone.ToImmutableArray(),
                        s.SpellId, s.Grade, e.Duration, e.DiceSide);
                    marks.Add(mark); events.Add(new MarkResolved(a, fighters[c].Id, mark)); continue;
                }
                if (SubCast(a))
                {
                    if (a == 2160) targets = targets.OrderBy(t => Distance(fighters[c].Cell, fighters[t].Cell)).ThenBy(t => fighters[t].Id).Take(1).ToList();
                    // 2960 is allowed to execute on empty ground; it does not require a fighter.
                    if (a == 2960 && targets.Count == 0 && Tokens(e.TargetMask).All(t => t is "a" or "A")) targets.Add(c);
                    foreach (int t in targets)
                    {
                        int childId = e.DiceNum != 0 ? e.DiceNum : e.Min;
                        int grade = Math.Max(1, e.DiceSide);
                        var child = lookup(childId, grade);
                        if (child is null) { Problem(s, e, $"Missing sub-spell {childId} grade {grade}", t); continue; }
                        int childCaster = a is 792 or 1017 or 2794 ? t : c;
                        int childCell = a switch { 2960 or 2794 => cell, 1017 or 1019 => fighters[c].Cell, _ => fighters[t].Cell };
                        events.Add(new SubCastResolved(a, fighters[childCaster].Id, fighters[t].Id, childId, grade, childCell));
                        int before = events.Count;
                        // A sub-cast keeps its own caster origin: only the top-level area is re-oriented
                        // from a portal exit.
                        Cast(childCaster, childCell, child, depth + 1, fighters[childCaster].Cell);
                        dealt += events.Skip(before).OfType<DamageResolved>().Sum(d => d.LifeLost);
                    }
                    continue;
                }
                foreach (int t in targets)
                {
                    if (!fighters[t].Alive) continue;
                    var caster = fighters[c]; var target = fighters[t];
                    if (a is 280 or 281 or 290 or 295)
                    {
                        spellModifiers.Add(new(e.DiceNum, target.Id, a, e.Value, e.Duration));
                        events.Add(new SpellModifierResolved(a, caster.Id, target.Id, e.DiceNum, e.Value));
                        continue;
                    }
                    if (a == 279 && caster.MaxLife <= 0 || a is 1109 or 1039 && target.MaxLife <= 0)
                    {
                        Problem(s, e, "Required maximum life is unobserved", a == 279 ? c : t);
                        continue;
                    }
                    if (IsDamageAction(a))
                    {
                        DamageElement element = a switch
                        {
                            91 or 96 => DamageElement.Water, 92 or 97 => DamageElement.Earth,
                            93 or 98 => DamageElement.Air, 94 or 99 => DamageElement.Fire,
                            _ => DamageElement.Neutral
                        };
                        var stats = caster.Characteristics ?? EmptyStats;
                        if (a is 2822 or 2828 or 2832 or 2890)
                        {
                            element = DamageEstimator.ElementOf(e, stats);
                        }
                        double damage = a switch
                        {
                            82 or 144 => amount,
                            89 => caster.Life * amount / 100,
                            279 => (caster.MaxLife - caster.Life) * amount / 100,
                            _ => DamageEstimator.Estimate(e.Min, e.Max, element, stats, new(target.Resistances, target.Reductions),
                                depth == 0 ? damageMultiplier : 1).Average
                        };
                        if (a is 82 or 144 or 89 or 279)
                            damage = Math.Max(0, damage * (depth == 0 ? damageMultiplier : 1)
                                * (1 - Math.Clamp(target.Resistances?.GetValueOrDefault(element) ?? 0, -100, 100) / 100.0)
                                - (target.Reductions?.GetValueOrDefault(element) ?? 0));
                        double lost = Damage(c, t, a, damage); dealt += lost;
                        // Official Sram class text 1084674: direct damage reveals;
                        // deferred poison and ground-mark damage are separate paths.
                        if (!triggeredDamage && (lost > 0 || fighters[t].Shield < target.Shield)
                            && fighters[c].Visibility is FighterVisibility.Invisible or FighterVisibility.Detected)
                        {
                            fighters[c] = fighters[c] with { Visibility = FighterVisibility.Visible };
                            events.Add(new VisibilityResolved(150, fighters[c].Id, fighters[c].Id, FighterVisibility.Visible));
                        }
                        if (a is >= 91 and <= 95 or 82 or 2828 or 2890) Heal(c, c, s, e, Math.Floor(lost / 2));
                        continue;
                    }
                    if (a is 81 or 108 or 143 or 407 or 1109 or >= 2998 and <= 3002 or 2973 or 786)
                    {
                        var stats = caster.Characteristics ?? EmptyStats;
                        int stat = a switch { 2998 => CharIds.Chance, 2999 => CharIds.Agility, 3000 => CharIds.Strength, _ => CharIds.Intelligence };
                        double heal = a switch
                        {
                            1109 => target.MaxLife * amount / 100,
                            2973 or 786 => dealt * amount / 100,
                            81 or 143 or 407 or 3001 => amount,
                            3002 => amount * (100 + new[] { CharIds.Strength, CharIds.Intelligence, CharIds.Chance, CharIds.Agility }.Max(x => stats.GetValueOrDefault(x))) / 100,
                            _ => amount * (100 + stats.GetValueOrDefault(stat)) / 100
                        };
                        heal *= Math.Max(0, stats.GetValueOrDefault(CharIds.DealtHealMultiplier, 100)) / 100.0;
                        if (depth == 0) heal *= damageMultiplier;
                        Heal(c, t, s, e, heal); continue;
                    }
                    if (a == 90)
                    {
                        double transfer = Math.Min(caster.Life, caster.Life * amount / 100);
                        Damage(c, c, a, transfer); Heal(c, t, s, e, transfer); continue;
                    }
                    if (a is 1020 or 1039 or 1040)
                    {
                        if (a == 1020 && (c != casterIndex || casterLevel <= 0))
                        {
                            Problem(s, e, "Caster level is unavailable for this actor", t); continue;
                        }
                        double shield = a == 1020 ? casterLevel * amount / 100 : a == 1039 ? target.MaxLife * amount / 100 : amount;
                        fighters[t] = target with { Shield = target.Shield + shield };
                        events.Add(new ShieldResolved(a, caster.Id, target.Id, shield)); continue;
                    }
                    if (a is 111 or 128 or 120 or 1079 or 1080 or 168 or 169)
                    {
                        bool ap = a is 111 or 120 or 1079 or 168;
                        bool gain = a is 111 or 128 or 120;
                        double delta = gain ? amount : -Math.Min(amount, Math.Max(0, ap ? target.Ap : target.Mp));
                        fighters[t] = ap ? target with { Ap = target.Ap + delta } : target with { Mp = target.Mp + delta };
                        if (e.Duration != 0 && a is 111 or 128)
                            statBuffs.Add(new(s.SpellId, target.Id, ap ? 1 : 23, delta, e.Duration, Points: true));
                        events.Add(new PointsResolved(a, caster.Id, target.Id, ap, delta, target.Enemy)); continue;
                    }
                    if (a == 150)
                    {
                        fighters[t] = target with { Visibility = FighterVisibility.Invisible };
                        if (target.Visibility != FighterVisibility.Invisible)
                            events.Add(new VisibilityResolved(a, caster.Id, target.Id, FighterVisibility.Invisible));
                        continue;
                    }
                    if (a is 950 or 951 or 952)
                    {
                        int state = e.Value != 0 ? e.Value : e.Min;
                        var states = target.States!.ToHashSet();
                        var absent = target.AbsentStates!.ToHashSet();
                        bool changed = a == 950 ? states.Add(state) : states.Remove(state);
                        timedStates.RemoveAll(s => s.TargetId == target.Id && s.State == state);
                        if (a == 950 && e.Duration > 0)
                            timedStates.Add(new(target.Id, state, e.Duration));
                        if (a == 950) absent.Remove(state); else absent.Add(state);
                        fighters[t] = target with { States = states.ToImmutableHashSet(), AbsentStates = absent.ToImmutableHashSet() };
                        if (changed) events.Add(new StateResolved(a, caster.Id, target.Id, state, a == 950));
                        continue;
                    }
                    if (StatDelta(a) is var statDelta && statDelta.Id != 0)
                    {
                        var stats = (target.Characteristics ?? EmptyStats).ToDictionary(x => x.Key, x => x.Value);
                        int delta = (int)Math.Round(amount, MidpointRounding.AwayFromZero) * statDelta.Sign;
                        int baseline = statDelta.Id is CharIds.DealtDamageMultiplier or CharIds.DealtHealMultiplier ? 100 : 0;
                        stats[statDelta.Id] = stats.GetValueOrDefault(statDelta.Id, baseline) + delta;
                        fighters[t] = target with { Characteristics = stats.ToImmutableDictionary() };
                        if (e.Duration != 0)
                            statBuffs.Add(new(s.SpellId, target.Id, statDelta.Id, delta, e.Duration));
                        events.Add(new StatResolved(a, caster.Id, target.Id, statDelta.Id, delta)); continue;
                    }
                    if (a == 320)
                    {
                        int stolen = (int)Math.Round(amount, MidpointRounding.AwayFromZero);
                        foreach (var (recipient, delta) in new[] { (t, -stolen), (c, stolen) })
                        {
                            var fighter = fighters[recipient];
                            var stats = (fighter.Characteristics ?? EmptyStats).ToDictionary(x => x.Key, x => x.Value);
                            stats[19] = stats.GetValueOrDefault(19) + delta;
                            fighters[recipient] = fighter with { Characteristics = stats.ToImmutableDictionary() };
                            if (e.Duration != 0) statBuffs.Add(new(s.SpellId, fighter.Id, 19, delta, e.Duration));
                            events.Add(new StatResolved(a, caster.Id, fighter.Id, 19, delta));
                        }
                        continue;
                    }
                    if (a is 5 or 1103 or 6) Walk(c, t, a, caster.Cell, (int)amount, a != 6, s, e);
                    else if (a is 1041 or 1042) { Walk(c, c, a, cell, (int)amount, a == 1041, s, e); break; }
                    else if (a == 4) Move(c, t, a, cell);
                    else if (a == 8) Move(c, c, a, target.Cell, true);
                    else if (a is 1104 or 1105 or 1106)
                    {
                        int moving = a == 1104 ? c : t;
                        int pivot = a == 1105 ? caster.Cell : a == 1104 && c != t ? target.Cell : cell;
                        var (px, py) = MapPathfinder.MapPointOf(pivot);
                        var (mx, my) = MapPathfinder.MapPointOf(fighters[moving].Cell);
                        Move(c, moving, a, MapPathfinder.CellFromMapPoint(2 * px - mx, 2 * py - my), true);
                    }
                }
            }
            telefragged = outerTelefragged;
            active.Remove(key);
        }
        if (casterIndex < 0 || casterIndex >= fighters.Count || castCell is < 0 or >= 560)
            unknown.Add(new(spell.SpellId, 0, "Invalid caster index or cast cell"));
        else Cast(casterIndex, castCell, spell, 0, areaOrigin ?? fighters[casterIndex].Cell);
        foreach (var revealed in events.OfType<VisibilityResolved>()
            .Where(e => e.Visibility == FighterVisibility.Visible).Select(e => e.TargetId).Distinct().ToArray())
        {
            int owner = fighters.FindIndex(f => f.Id == revealed);
            if (owner < 0 || !fighters[owner].Alive) continue;
            var trigger = (board.DeferredEffects ?? Array.Empty<DeferredSpellEffect>()).FirstOrDefault(p =>
                p.SpellId == 21978 && p.CasterId == revealed && p.TargetId == revealed
                && p.Triggers == "IOFF" && p.Effect.ActionId == 792 && p.Effect.DiceNum == 21979 && p.Effect.DiceSide == 1);
            if (trigger is null) continue;
            if (lookup(21979, 1) is not { } reward)
            {
                unknown.Add(new(21978, 792, "Missing Sram reveal subcast", revealed));
                continue;
            }
            Cast(owner, fighters[owner].Cell, reward, 0, fighters[owner].Cell);
        }
        var resolved = new ResolvedCast(new(fighters.ToImmutableArray(), marks.ToImmutableArray(), board.ThroughPortal, board.StateDamageRules,
                statBuffs.ToImmutableArray(), board.KnownStatBuffSources?.ToImmutableHashSet(), board.AllStatBuffSourcesKnown,
                 spellModifiers.ToImmutableArray(),
                 (board.DeferredEffects ?? Array.Empty<DeferredSpellEffect>()).Concat(deferred)
                    .Where(p => p.SpellId != 14611 || p.TargetId is not long target
                 || !consumedResonance.Contains((p.CasterId, target))).ToImmutableArray(), timedStates.ToImmutableArray(), board.SummonLookup),
            events.ToImmutableArray(), deferred.Where(p => p.SpellId != 14611 || p.TargetId is not long target
                || !consumedResonance.Contains((p.CasterId, target))).ToImmutableArray(), unknown.ToImmutableArray());
        // This context consumes hooks only; damage still uses the selected descriptor's normal mean.
        // A revealing critical installs IOFF before consuming CC in the same cast (live capture).
        if (critical && casterIndex >= 0 && casterIndex < fighters.Count)
        {
            var consumed = ConsumeCriticalTriggers(resolved.Board, fighters[casterIndex].Id);
            resolved = resolved with
            {
                Board = consumed,
                Deferred = resolved.Deferred.Where(p => consumed.DeferredEffects!.Contains(p)).ToImmutableArray()
            };
        }
        return resolved;
    }

    private static int Distance(int a, int b)
    {
        var (ax, ay) = MapPathfinder.MapPointOf(a); var (bx, by) = MapPathfinder.MapPointOf(b);
        return Math.Abs(ax - bx) + Math.Abs(ay - by);
    }
}
