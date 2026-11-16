using System.Linq;
using GivreClone.App.Services.Eco;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public class ForgeStateTests
{
    [Theory]
    [InlineData(0, 100, false)]
    [InlineData(4, 100, false)]
    [InlineData(99, 100, false)]
    [InlineData(2, 0, false)]
    [InlineData(1, 100, true)]
    [InlineData(2, 100, true)]
    [InlineData(3, 100, true)]
    public void Forge_requires_an_object_snapshot_and_a_known_outcome(int code, long gid, bool usable)
    {
        var result = new InventoryReader.MagicResult(code, gid, System.Array.Empty<InventoryReader.ItemEffect>(), null, null);
        Assert.Equal(usable, ForgeState.HasUsableResult(result));
        if (usable) Assert.Empty(ForgeState.Stats(result.Effects)); // all effects lost is a valid new state
    }

    private static InventoryReader.ItemEffect E(int id, int value = 0, int max = 0) => new(id, value, 0, max, 0, null, 0);

    [Fact]
    public void Stats_map_effect_ids_to_rune_stat_codes_and_prefer_the_rolled_value()
    {
        var s = ForgeState.Stats(new[] { E(118, value: 42), E(125, max: 60), E(999, value: 5) });
        Assert.Equal(42, s["Fo"]);
        Assert.Equal(60, s["vi"]);          // clés insensibles à la casse ; repli sur le maximum de la fourchette
        Assert.False(s.ContainsKey("999"));
    }

    [Fact]
    public void Engine_decision_runs_on_stats_read_from_effects()
    {
        var stats = ForgeState.Stats(new[] { E(118, value: 10) });
        var action = ForgeEngine.DecideTargets(stats, new[] { new ForgeTarget("Fo", 20, 1, "Ra Fo") });
        Assert.Equal("Ra Fo", action.Rune);
        Assert.Equal(new[] { "Rune Ra Fo", "Ra Fo" }, ForgeState.RuneItemNames("Ra Fo").ToArray());
        Assert.Equal(new[] { "Rune Ra Fo" }, ForgeState.RuneItemNames("Rune Ra Fo").ToArray());
    }
}
