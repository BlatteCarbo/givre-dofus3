using System.Collections.Generic;
using System.Linq;

namespace GivreClone.Engine;

/// <summary>Fail-closed selection among the four guards of a treasure-hunt chest.</summary>
public static class TreasureFightPolicy
{
    public readonly record struct Guard(long Actor, int Cell, string? Appearance);

    public static Guard? Select(IReadOnlyList<Guard> guards)
    {
        if (guards.Count != 4 || guards.Any(g => g.Actor == 0 || g.Cell is < 0 or >= 560 || string.IsNullOrEmpty(g.Appearance))
            || guards.Select(g => g.Actor).Distinct().Count() != 4
            || guards.Select(g => g.Cell).Distinct().Count() != 4) return null;
        var groups = guards.GroupBy(g => g.Appearance).ToList();
        // No verified special generic ID/grade is shipped. The owner's rule is exactly three identical guards
        // and one different (four-legged) appearance. Never infer the answer from HP, cell, GID order or grade.
        return groups.Count == 2 && groups.Any(g => g.Count() == 3)
            ? groups.Single(g => g.Count() == 1).Single() : null;
    }

    public static bool SingleTargetEffects(IReadOnlyList<int> normalShapes, IReadOnlyList<int> criticalShapes)
        => normalShapes.Count > 0 && normalShapes.All(s => s == 'P') && criticalShapes.All(s => s == 'P');
}
