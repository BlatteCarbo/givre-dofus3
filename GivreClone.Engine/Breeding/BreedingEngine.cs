using System.Collections.ObjectModel;

namespace GivreClone.Engine.Breeding;

public enum BreedingValueSource { Unknown, Observed, Manual, Predicted }
public enum MountFamily { Dragoturkey, Muldo, Volkorne }
public enum BreedingGauge { SerenityMinus, SerenityPlus, Endurance, Maturity, Love, Xp }
public enum BreedingStageKind { Unification, MoveSerenity, Progression, Xp }

/// <summary>A missing reading is never zero. Predictions cannot be used as execution evidence.</summary>
public readonly record struct BreedingValue<T>(T? Value, BreedingValueSource Source, DateTimeOffset? At = null)
    where T : struct
{
    public static BreedingValue<T> Unknown => new(null, BreedingValueSource.Unknown);
    public bool IsKnown => Value.HasValue && Source != BreedingValueSource.Unknown;
    public bool IsConfirmed => IsKnown && Source is BreedingValueSource.Observed or BreedingValueSource.Manual;
}

/// <summary>
/// One homogeneous lot, not individual mounts. For a non-unified lot Serenity is its highest reading.
/// Xp is the gained XP since the lot's chosen starting level, not total lifetime XP.
/// </summary>
public sealed record BreedingLot(
    string PaddockId, MountFamily Family, BreedingValue<int> MountCount,
    BreedingValue<int> Serenity, BreedingValue<bool> Unified,
    BreedingValue<int> Endurance, BreedingValue<int> Maturity,
    BreedingValue<int> Love, BreedingValue<int> Xp,
    IReadOnlyDictionary<BreedingGauge, BreedingValue<int>> Fuel);

/// <summary>RequiredXp must come from a verified XP table or explicit manual target; no guessed level conversion.</summary>
public sealed record BreedingTarget(int RequiredXp, bool Prudent = false);
public sealed record FuelConsumption(int Cycles, int Given, int Remaining, bool EmptyBeforeTarget);
public sealed record BreedingStage(
    BreedingStageKind Kind, int SerenityTarget, IReadOnlyList<BreedingGauge> Gauges,
    IReadOnlyDictionary<BreedingGauge, int> Needs,
    IReadOnlyDictionary<BreedingGauge, int> StartingFuel,
    int Cycles, TimeSpan EstimatedDuration, IReadOnlyList<BreedingGauge> EmptyGauges);
public sealed record BreedingPlan(BreedingStage? Stage, string? BlockedReason, bool Complete)
{
    public bool CanStart => Stage is { Cycles: > 0 } && BlockedReason is null;
}

/// <summary>
/// Adapted from kihw/monture-minute at 1def87be5cbb46a8ff4fe7c399d7dd7ef464ebd5,
/// src/engine/parcours.ts and carburant.ts. See THIRD-PARTY-NOTICES.md beside this file.
/// Dofus 3.7 assumptions, not protocol facts: two active gauges, ten-second cycles,
/// same rules for all three families. Does not assume refills or fabricate missing fuel.
/// </summary>
public static class BreedingEngine
{
    public const int StatMaximum = 20_000;
    public const int FuelMaximum = 200_000;
    public const int CycleSeconds = 10;

    public static int FuelTier(int fuel)
    {
        if (fuel is < 0 or > FuelMaximum) throw new ArgumentOutOfRangeException(nameof(fuel));
        return fuel > 180_000 ? 40 : fuel > 140_000 ? 30 : fuel > 80_000 ? 20 : 10;
    }

    public static FuelConsumption Consume(int fuel, int need, int maxCycles = int.MaxValue)
    {
        _ = FuelTier(fuel);
        if (need < 0) throw new ArgumentOutOfRangeException(nameof(need));
        if (maxCycles < 0) throw new ArgumentOutOfRangeException(nameof(maxCycles));
        int remaining = need, cycles = 0, initial = fuel;
        while (remaining > 0 && fuel > 0 && cycles < maxCycles)
        {
            int tier = FuelTier(fuel);
            int threshold = tier switch { 40 => 180_000, 30 => 140_000, 20 => 80_000, _ => 0 };
            int beforeTier = Math.Max(1, (fuel - threshold + tier - 1) / tier);
            int n = Math.Min(beforeTier, Math.Min((int)(((long)remaining + tier - 1) / tier), maxCycles - cycles));
            int given = (int)Math.Min((long)n * tier, Math.Min(remaining, fuel));
            fuel -= given;
            remaining -= given;
            cycles += n;
        }
        return new(cycles, initial - fuel, fuel, remaining > 0 && fuel == 0);
    }

    public static BreedingPlan NextStage(BreedingLot lot, BreedingTarget target)
    {
        if (target.RequiredXp < 0) throw new ArgumentOutOfRangeException(nameof(target));
        if (string.IsNullOrWhiteSpace(lot.PaddockId)) return Block("Paddock identity unknown.");
        if (!Enum.IsDefined(lot.Family)) return Block("Mount family unknown.");
        if (!lot.MountCount.IsConfirmed) return Block("Mount count unknown or predicted.");
        if (lot.MountCount.Value < 0) return Block("Invalid mount count.");
        if (lot.MountCount.Value == 0) return Block("Paddock is empty.");
        if (!lot.Serenity.IsConfirmed || !lot.Unified.IsConfirmed ||
            !lot.Endurance.IsConfirmed || !lot.Maturity.IsConfirmed ||
            !lot.Love.IsConfirmed || !lot.Xp.IsConfirmed)
            return Block("Confirm the lot's serenity, unification, characteristics and XP.");
        int serenity = lot.Serenity.Value!.Value;
        int xp = lot.Xp.Value!.Value;
        if (serenity is < -5000 or > 5000 || xp < 0 ||
            new[] { lot.Endurance, lot.Maturity, lot.Love }.Any(v => v.Value is < 0 or > StatMaximum))
            return Block("Lot readings outside Dofus 3.7 bounds.");
        var stats = new[] {
            (Gauge: BreedingGauge.Endurance, Current: lot.Endurance.Value!.Value, Min: -5000, Max: -1),
            (Gauge: BreedingGauge.Maturity, Current: lot.Maturity.Value!.Value, Min: -2000, Max: 2000),
            (Gauge: BreedingGauge.Love, Current: lot.Love.Value!.Value, Min: 1, Max: 5000)
        }.Where(s => s.Current < StatMaximum).ToArray();
        int xpNeed = Math.Max(0, target.RequiredXp - xp);
        if (stats.Length == 0 && xpNeed == 0) return new(null, null, true);

        var needs = new Dictionary<BreedingGauge, int>();
        var kind = BreedingStageKind.Progression;
        int serenityTarget = serenity;
        if (lot.Unified.Value != true && serenity > -5000)
        {
            kind = BreedingStageKind.Unification;
            serenityTarget = -5000;
            needs.Add(BreedingGauge.SerenityMinus, serenity + 5000);
        }
        else
        {
            var available = stats.Where(s => serenity >= s.Min && serenity <= s.Max).Take(2).ToArray();
            foreach (var s in available) needs.Add(s.Gauge, StatMaximum - s.Current);
            if (available.Length == 0 && stats.Length > 0)
            {
                // Rank intersecting zones by number of unfinished stats, then distance, then stable stat order.
                var candidates = new List<(int Count, int Target)>();
                for (int i = 0; i < stats.Length; i++)
                {
                    candidates.Add((1, ZoneTarget(serenity, stats[i].Min, stats[i].Max)));
                    for (int j = i + 1; j < stats.Length; j++)
                    {
                        int min = Math.Max(stats[i].Min, stats[j].Min);
                        int max = Math.Min(stats[i].Max, stats[j].Max);
                        if (min <= max) candidates.Add((2, ZoneTarget(serenity, min, max)));
                    }
                }
                serenityTarget = candidates.OrderByDescending(c => c.Count)
                    .ThenBy(c => Math.Abs(c.Target - serenity)).First().Target;
                kind = BreedingStageKind.MoveSerenity;
                needs.Add(serenityTarget < serenity ? BreedingGauge.SerenityMinus : BreedingGauge.SerenityPlus,
                    Math.Abs(serenityTarget - serenity));
            }
            else if (stats.Length == 0) kind = BreedingStageKind.Xp;
        }
        if (needs.Count < 2 && xpNeed > 0) needs.Add(BreedingGauge.Xp, xpNeed);
        var fuel = new Dictionary<BreedingGauge, int>();
        foreach (var gauge in needs.Keys)
        {
            if (!lot.Fuel.TryGetValue(gauge, out var value) || !value.IsConfirmed)
                return Block($"Fuel for {gauge} unknown or predicted; confirm it before starting.");
            int level = value.Value!.Value;
            if (level is < 0 or > FuelMaximum) return Block($"Invalid fuel for {gauge}.");
            fuel.Add(gauge, level);
        }
        var primary = needs.Keys.Where(g => g != BreedingGauge.Xp).ToArray();
        if (primary.Length == 0) primary = [BreedingGauge.Xp];
        // Reference stages last as long as the longest primary gauge. A shorter one stops consuming.
        int cycles = primary.Max(g => Consume(fuel[g], needs[g]).Cycles);
        var empty = primary.Where(g => Consume(fuel[g], needs[g], cycles).EmptyBeforeTarget).ToArray();
        var stage = new BreedingStage(kind, serenityTarget, Array.AsReadOnly(needs.Keys.ToArray()),
            new ReadOnlyDictionary<BreedingGauge, int>(needs), new ReadOnlyDictionary<BreedingGauge, int>(fuel),
            cycles, TimeSpan.FromSeconds(Math.Round(cycles * (double)CycleSeconds * (target.Prudent ? 1.1 : 1))),
            Array.AsReadOnly(empty));
        return new(stage, cycles == 0 ? "Active primary gauges are empty; refill and confirm fuel." : null, false);
    }

    /// <summary>Predicts only changed values. Never promotes a simulation into an observed reading.</summary>
    public static BreedingLot Predict(BreedingLot lot, BreedingStage stage, TimeSpan elapsed, DateTimeOffset at)
    {
        int cycles = (int)Math.Clamp(Math.Floor(elapsed.TotalSeconds / CycleSeconds), 0, stage.Cycles);
        var fuel = lot.Fuel.ToDictionary(p => p.Key, p => p.Value);
        var result = lot;
        foreach (var gauge in stage.Gauges)
        {
            var used = Consume(stage.StartingFuel[gauge], stage.Needs[gauge], cycles);
            fuel[gauge] = new(used.Remaining, BreedingValueSource.Predicted, at);
            if (used.Given == 0) continue;
            BreedingValue<int> Pred(int v) => new(v, BreedingValueSource.Predicted, at);
            result = gauge switch {
                BreedingGauge.SerenityMinus => result with { Serenity = Pred(Math.Max(-5000, result.Serenity.Value!.Value - used.Given)) },
                BreedingGauge.SerenityPlus => result with { Serenity = Pred(Math.Min(5000, result.Serenity.Value!.Value + used.Given)) },
                BreedingGauge.Endurance => result with { Endurance = Pred(result.Endurance.Value!.Value + used.Given) },
                BreedingGauge.Maturity => result with { Maturity = Pred(result.Maturity.Value!.Value + used.Given) },
                BreedingGauge.Love => result with { Love = Pred(result.Love.Value!.Value + used.Given) },
                BreedingGauge.Xp => result with { Xp = Pred(result.Xp.Value!.Value + used.Given) },
                _ => result
            };
        }
        if (stage.Kind == BreedingStageKind.Unification && result.Serenity.Value <= -5000)
            result = result with { Unified = new(true, BreedingValueSource.Predicted, at) };
        return result with { Fuel = new ReadOnlyDictionary<BreedingGauge, BreedingValue<int>>(fuel) };
    }

    private static int ZoneTarget(int from, int min, int max)
    {
        int lo = min == 1 ? 0 : min, hi = max == -1 ? 0 : max;
        int middle = (int)Math.Round((lo + hi) / 2d, MidpointRounding.AwayFromZero);
        return from < min ? Math.Min(lo + 500, middle) : from > max ? Math.Max(hi - 500, middle) : from;
    }

    private static BreedingPlan Block(string reason) => new(null, reason, false);
}
