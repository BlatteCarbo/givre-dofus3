using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.CombatParity;

public static class AoeAimPolicy
{
    // The reference enemy must be hit, but need not occupy the cast cell.
    public static int Find(int referenceCell, IEnumerable<int> candidates,
        Func<int, bool> legal, Func<int, IReadOnlyCollection<int>> area,
        IReadOnlyCollection<int> enemies)
    {
        int best = -1, hits = -1;
        foreach (int center in new[] { referenceCell }.Concat(candidates).Distinct())
        {
            if (!legal(center)) continue;
            var cells = area(center);
            if (!cells.Contains(referenceCell)) continue;
            int count = enemies.Count(cells.Contains);
            if (count > hits) { best = center; hits = count; }
        }
        return best;
    }
}
