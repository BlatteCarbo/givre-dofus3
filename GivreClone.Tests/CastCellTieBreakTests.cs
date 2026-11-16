using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public class CastCellTieBreakTests
{
    private static bool Walk(int c) => c is >= 0 and < MapPathfinder.CellCount;

    [Fact]
    public void Equal_cost_cast_cells_are_ranked_by_the_tie_break_then_by_cell_id()
    {
        const int from = 280;
        var reach = MapPathfinder.OrthoReachable(from, 2, Walk);
        var twoSteps = reach.Where(kv => kv.Value == 2).Select(kv => kv.Key).OrderBy(c => c).ToList();
        Assert.True(twoSteps.Count >= 3);
        var allowed = new HashSet<int>(twoSteps);
        // sans départage : la plus petite cellule (comportement historique)
        int plain = CombatPositionPlanner.FindCastCell(from, 2, Walk, c => allowed.Contains(c), out int s0);
        Assert.Equal(twoSteps[0], plain); Assert.Equal(2, s0);
        // avec départage : la meilleure, même si sa cellule est plus grande
        int favoured = twoSteps[^1];
        int chosen = CombatPositionPlanner.FindCastCell(from, 2, Walk, c => allowed.Contains(c), out int s1, null, c => c == favoured ? 0 : 5);
        Assert.Equal(favoured, chosen); Assert.Equal(2, s1);
        // à départage égal : retour à la plus petite cellule (déterministe)
        Assert.Equal(twoSteps[0], CombatPositionPlanner.FindCastCell(from, 2, Walk, c => allowed.Contains(c), out _, null, _ => 1));
    }

    [Fact]
    public void The_tie_break_never_beats_a_cheaper_cast_cell()
    {
        const int from = 280;
        var reach = MapPathfinder.OrthoReachable(from, 3, Walk);
        int one = reach.First(kv => kv.Value == 1).Key, three = reach.First(kv => kv.Value == 3).Key;
        var allowed = new HashSet<int> { one, three };
        int chosen = CombatPositionPlanner.FindCastCell(from, 3, Walk, c => allowed.Contains(c), out int steps, null, c => c == three ? -1000 : 1000);
        Assert.Equal(one, chosen); Assert.Equal(1, steps);   // moins de PM d'abord, quel que soit le départage
    }

    [Fact]
    public void Area_score_uses_jitsuri_weights_and_never_rewards_hitting_allies()
    {
        Assert.Equal(3 * 10000 - 2, CombatAoeSafety.Score(3, 0, 2));
        Assert.True(CombatAoeSafety.Score(2, 0, 4) > CombatAoeSafety.Score(5, 1, 1));   // un allié touché annule 10 ennemis
        Assert.True(CombatAoeSafety.Score(3, 0, 5) > CombatAoeSafety.Score(2, 0, 1));   // plus d'ennemis d'abord
        Assert.True(CombatAoeSafety.Score(2, 0, 2) > CombatAoeSafety.Score(2, 0, 6));   // à ennemis égaux, moins de PA
        Assert.Equal(0, CombatAoeSafety.Score(-3, -1, -9));                              // bornes : jamais de valeur négative saisie
    }
}
