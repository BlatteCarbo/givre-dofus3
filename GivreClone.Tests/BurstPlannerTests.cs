using GivreClone.App.Services.Combat;
using Xunit;

namespace GivreClone.Tests;

public class BurstPlannerTests
{
    [Fact]
    public void Prefers_the_combination_over_the_single_biggest_spell()
    {
        // 7 PA : gros 5 PA/300 (+ 1 petit = 460) vs 3× petit 2 PA/160 (= 480) : la combinaison de petits gagne.
        var p = BurstPlanner.Best(new[] { new BurstSpell("Gros", 5, 300, 1), new BurstSpell("Petit", 2, 160, 3) }, 7);
        Assert.Equal(480, p.Damage);
        Assert.Equal(6, p.ApUsed);
        Assert.Equal("3× Petit = 480 dégâts pour 6 PA", p.Describe());
    }

    [Fact]
    public void Honours_the_per_turn_cast_limit()
    {
        var p = BurstPlanner.Best(new[] { new BurstSpell("A", 3, 200, 1), new BurstSpell("B", 3, 150, 0) }, 9);
        Assert.Equal(200 + 150 + 150, p.Damage);
        Assert.Contains(("A", 1), p.Casts);
        Assert.Contains(("B", 2), p.Casts);
    }

    [Fact]
    public void Nothing_affordable_gives_an_empty_plan()
    {
        var p = BurstPlanner.Best(new[] { new BurstSpell("A", 5, 100, 0), new BurstSpell("Buff", 2, 0, 0) }, 4);
        Assert.Equal(0, p.Damage);
        Assert.Empty(p.Casts);
        Assert.Equal("aucun lancer", p.Describe());
    }
}
