using GivreClone.Engine.Recovery;
using Xunit;

namespace GivreClone.Tests;

public sealed class ItemBindingTests
{
    [Theory]
    [InlineData(new[] { 125, 981 }, ItemBind.Character)]
    [InlineData(new[] { 982, 125 }, ItemBind.Account)]
    [InlineData(new[] { 983 }, ItemBind.TimedLock)]
    [InlineData(new[] { 983, 981 }, ItemBind.Character)]
    [InlineData(new[] { 125, 174 }, ItemBind.None)]
    public void Binding_is_read_from_the_item_effects(int[] effects, ItemBind expected)
        => Assert.Equal(expected, ItemBindingRules.Classify(effects));

    [Theory]
    [InlineData(ItemBind.None, true, true)]
    [InlineData(ItemBind.Character, false, false)]
    [InlineData(ItemBind.Account, false, false)]
    [InlineData(ItemBind.TimedLock, true, false)]
    public void Bound_items_cannot_be_forged_or_traded(ItemBind bind, bool forge, bool trade)
    {
        Assert.Equal(forge, ItemBindingRules.CanForge(bind));
        Assert.Equal(trade, ItemBindingRules.CanTrade(bind));
    }

    [Theory]
    [InlineData(ItemBind.None, true, true)]
    [InlineData(ItemBind.Character, false, false)]
    [InlineData(ItemBind.Account, false, true)]
    [InlineData(ItemBind.TimedLock, true, true)]
    public void Bound_items_are_not_crushed_and_character_bound_stay_out_of_the_bank(ItemBind bind, bool crush, bool bank)
    {
        Assert.Equal(crush, ItemBindingRules.CanCrush(bind));
        Assert.Equal(bank, ItemBindingRules.CanBank(bind));
    }

    [Fact]
    public void Crush_picker_blocks_a_bound_item()
        => Assert.Equal("objet lié au personnage ou au compte", SmithJournal.CrushBlock(true, false, false, true, false, bound: true));

    [Fact]
    public void Leveling_skips_a_bound_item_and_picks_the_next_forgeable_one()
    {
        var items = new[] { new LevelingItem(1, true, SmithAdmission.Bound), new LevelingItem(2, true, SmithAdmission.Forge) };
        Assert.Equal(2, SmithLeveling.NextUid(items, stayOn: 1));
    }

    [Fact]
    public void Bound_admission_blocks_the_picker_row()
        => Assert.Equal("objet lié au personnage ou au compte", SmithJournal.ForgeBlock(true, false, SmithAdmission.Bound, 50, 1));
}
