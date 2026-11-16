using System.Collections.Generic;

namespace GivreClone.Engine;

public static class ChallengeFinishPolicy
{
    /// <summary>Absolute life matters: several full-health enemies may die to the same area attack.</summary>
    public static bool CanFinishArea(IReadOnlyList<(int Cell, int? Life)> enemies,
                                     ISet<int> area, double conservativeDamage)
    {
        if (enemies.Count == 0 || conservativeDamage <= 0) return false;
        foreach (var enemy in enemies)
            if (!area.Contains(enemy.Cell) || enemy.Life is not int hp || hp <= 0 || hp > conservativeDamage)
                return false;
        return true;
    }
}
