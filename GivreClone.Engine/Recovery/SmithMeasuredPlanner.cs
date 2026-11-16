namespace GivreClone.Engine.Recovery;

public sealed record SmithLevelingChoice(long ItemUid, long RuneGid, int Runes, long? NewCashCost,
    bool CurrentSupport = false);

public sealed record SmithXpObservation(long ItemUid, long RuneGid, int Runes, long? Experience);

/// <summary>Local batch estimates for one profession level. Stock valuation is not a cash debit.</summary>
public static class SmithMeasuredPlanner
{
    public static SmithLevelingChoice? Choose(IEnumerable<SmithLevelingChoice> choices,
        IReadOnlyList<SmithXpObservation> observations, long cashHeadroom = long.MaxValue)
    {
        var affordable = choices.Where(c => c.Runes > 0 && c.NewCashCost is >= 0
            && c.NewCashCost <= Math.Max(0, cashHeadroom)).ToArray();
        // One bounded sample per new choice, using stock before spending additional cash.
        var unexplored = affordable.Where(c => !observations.Any(o => o.ItemUid == c.ItemUid && o.RuneGid == c.RuneGid))
            .OrderBy(c => c.NewCashCost).ThenByDescending(c => c.CurrentSupport)
            .ThenBy(c => c.ItemUid).ThenBy(c => c.RuneGid).FirstOrDefault();
        if (unexplored is { NewCashCost: 0 }) return unexplored;

        var measured = affordable.Select(c =>
        {
            var samples = observations.Where(o => o.ItemUid == c.ItemUid && o.RuneGid == c.RuneGid).ToArray();
            // An unknown or fresh zero result is not evidence for repeating the same action.
            if (samples.Length == 0 || samples[^1].Experience is null or <= 0)
                return (Choice: c, CostPerXp: (decimal?)null, XpPerRune: 0m);
            var known = samples.Where(o => o.Runes > 0 && o.Experience > 0).ToArray();
            decimal runes = known.Sum(o => (decimal)o.Runes);
            decimal xp = known.Sum(o => (decimal)o.Experience!.Value);
            return runes == 0 ? (Choice: c, CostPerXp: (decimal?)null, XpPerRune: 0m)
                : (Choice: c, CostPerXp: (decimal?)(c.NewCashCost!.Value * runes / (xp * c.Runes)), XpPerRune: xp / runes);
        }).Where(c => c.CostPerXp.HasValue)
            .OrderBy(c => c.CostPerXp).ThenByDescending(c => c.XpPerRune)
            .ThenByDescending(c => c.Choice.CurrentSupport)
            .ThenBy(c => c.Choice.ItemUid).ThenBy(c => c.Choice.RuneGid).FirstOrDefault();
        return measured.Choice ?? unexplored;
    }
}
