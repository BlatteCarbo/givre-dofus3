using GivreClone.App.Services.Combat;
using Xunit;

namespace GivreClone.Tests;

public class SpellReachTests
{
    [Theory]
    [InlineData(6, 1, true, 3, 9)]
    [InlineData(6, 1, false, 3, 6)]
    [InlineData(4, 2, true, -4, 2)]
    [InlineData(5, 1, true, -2, 3)]
    public void Range_bonus_applies_only_to_boostable_spells_and_a_penalty_stops_at_the_minimum(int baseRange, int min, bool boostable, int stat, int expected)
        => Assert.Equal(expected, SpellReach.Effective(baseRange, min, boostable, stat));

    [Fact]
    public void Description_names_the_base_the_modifier_and_the_floor()
    {
        Assert.Equal("101:1-9 (base 6, PO +3)", SpellReach.Describe(101, 6, 1, true, 3));
        Assert.Equal("102:1-6 (base 6, non modifiable)", SpellReach.Describe(102, 6, 1, false, 3));
        Assert.Equal("103:2-2 (base 4, PO -4, plancher 2)", SpellReach.Describe(103, 4, 2, true, -4));
    }
}
