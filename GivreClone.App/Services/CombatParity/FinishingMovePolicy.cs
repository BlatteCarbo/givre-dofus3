using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.CombatParity;

public static class FinishingMovePolicy
{
    public static bool HasCastBudget(int totalCasts, int totalLimit, int spellCasts, int configuredLimit,
        bool serverLimitOk)
        => totalCasts < totalLimit && configuredLimit > 0 && spellCasts < configuredLimit && serverLimitOk;

    public static bool ShouldPrepare(bool initial, int enemies, bool lowHp, Func<bool> areaCanFinish)
        => enemies > 0 && (enemies == 1 || initial && lowHp || areaCanFinish());

    public static int RemainingMp(int localMp, int startMp, int stepsUsed, int? serverMp, int gainedMp = 0)
        => Math.Max(0, Math.Min(Math.Min(localMp, startMp + gainedMp - stepsUsed), serverMp ?? int.MaxValue));
}

/// <summary>One challenge movement search per observed situation during a turn.
/// HP/AP changes alone do not make a refused movement possible.</summary>
public sealed class ChallengeMoveAttempts
{
    private readonly HashSet<(int Steps, int Cell, int Mp, string Positions)> _seen = new();

    public bool TryBegin(int steps, int cell, int mp, IEnumerable<(long Actor, int Cell)> positions)
        => _seen.Add((steps, cell, mp, string.Join(";", positions.OrderBy(f => f.Actor)
            .ThenBy(f => f.Cell).Select(f => $"{f.Actor}:{f.Cell}"))));
}
