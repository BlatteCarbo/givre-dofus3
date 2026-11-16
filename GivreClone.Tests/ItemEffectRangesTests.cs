using GivreClone.App.Services.Eco;
using Xunit;

namespace GivreClone.Tests;

public class ItemEffectRangesTests
{
    [Fact]
    public void ReadsTheTemplateDiceOfEachEffect()
    {
        const string json = """
        {"id":8463,"effects":[
          {"from":151,"to":200,"characteristic":11,"effectId":125},
          {"from":26,"to":40,"characteristic":10,"effectId":118}]}
        """;
        var ranges = ItemEffectRanges.Parse(json);
        Assert.NotNull(ranges);
        Assert.Equal((151, 200), ranges![125]);
        Assert.Equal((26, 40), ranges[118]);
    }

    [Fact]
    public void AFixedValueHasNoMaximumBelowItsMinimum()
    {
        var ranges = ItemEffectRanges.Parse("""{"effects":[{"from":4,"to":0,"effectId":210}]}""");
        Assert.Equal((4, 4), ranges![210]);
    }

    [Fact]
    public void AnItemWithoutEffectsIsUnknown()
    {
        Assert.Null(ItemEffectRanges.Parse("""{"id":1,"effects":[]}"""));
        Assert.Null(ItemEffectRanges.Parse("""{"id":1}"""));
    }
}
