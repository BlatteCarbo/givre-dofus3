using System.Collections.Generic;
using System.Linq;

namespace GivreClone.Protocol;

public sealed record CombatAoeVerdict(bool Allowed, string Reason, int EnemiesHit, int AlliesHit);

public static class CombatAoeSafety
{
    /// <summary>Score d'une action de zone : ennemis touchés ×10 000, alliés touchés −100 000 chacun (jamais rentable), coût en PA retranché.</summary>
    public static int Score(int enemiesHit, int alliesHit, int actionPointCost) =>
        Math.Max(0, enemiesHit) * 10000 - Math.Max(0, alliesHit) * 100000 - Math.Max(0, actionPointCost);

    public static CombatAoeVerdict Evaluate(IEnumerable<int> zoneCells, IEnumerable<int> enemyCells,
        IEnumerable<int> allyCells, int minimumHits)
    {
        var zone = zoneCells.ToHashSet();
        int enemies = enemyCells.Distinct().Count(zone.Contains);
        int allies = allyCells.Distinct().Count(zone.Contains);
        if (allies > 0) return new(false, "ally_in_zone", enemies, allies);
        if (enemies < minimumHits) return new(false, "too_few_enemies", enemies, allies);
        return new(true, "allowed", enemies, allies);
    }
}
