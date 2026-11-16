using System.Linq;
using GivreClone.App.Services.Eco;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public class RuneCatalogTests
{
    [Fact]
    public void Families_are_unique_by_effect_and_name_runes_like_the_game()
    {
        Assert.Equal(RuneCatalog.Families.Count, RuneCatalog.Families.Select(f => f.EffectId).Distinct().Count());
        var vi = RuneCatalog.Of(125)!;
        Assert.Equal("Rune Vi", RuneCatalog.ItemName(vi, "Ba"));
        Assert.Equal("Rune Pa Vi", RuneCatalog.ItemName(vi, "Pa"));
        Assert.Equal(50, RuneCatalog.Amount(vi, "Ra"));
        Assert.Equal("Rune Ga Pa", RuneCatalog.ItemName(RuneCatalog.Of(111)!, "Ba"));
    }

    [Theory]
    [InlineData(10, 40, "Ra")]   // écart 30 >= 10 : Ra
    [InlineData(10, 15, "Pa")]   // écart 5 : Pa (3) mais pas Ra (10)
    [InlineData(10, 12, "Ba")]   // écart 2 : Ba
    public void Tier_is_the_biggest_rune_that_fits_the_gap(int current, int target, string tier)
        => Assert.Equal(tier, RuneCatalog.TierFor(RuneCatalog.Of(118)!, current, target, current, current));

    [Fact]
    public void Workshop_is_the_first_enabled_smithmagic_skill_of_the_map()
    {
        var map = new[]
        {
            new MapInteractive(10, 1, 0, 0, new[] { new InteractiveSkill(500, 113, 0, false) }),   // Forgemager désactivé
            new MapInteractive(11, 2, 0, 0, new[] { new InteractiveSkill(501, 360, 0, true) }),   // « Utiliser »
            new MapInteractive(12, 3, 0, 0, new[] { new InteractiveSkill(502, 168, 0, true) }),   // Joaillomager
        };
        Assert.Equal((3, 502), RuneCatalog.Workshop(map));
        Assert.Null(RuneCatalog.Workshop(map.Take(2)));
        Assert.Equal((1, 3, 10), RuneCatalog.Gains["Force"]);
    }

    [Fact]
    public void Stats_cover_every_catalogued_family()
    {
        var fx = new[] { new InventoryReader.ItemEffect(111, 1, 0, 0, 0, null, 0), new InventoryReader.ItemEffect(243, 7, 0, 0, 0, null, 0) };
        var s = ForgeState.Stats(fx);
        Assert.Equal(1, s["Ga Pa"]);
        Assert.Equal(7, s["Ré Feu"]);
    }
}
