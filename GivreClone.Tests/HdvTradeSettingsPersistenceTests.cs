using System.Collections.Generic;
using System.Text.Json;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public sealed class HdvTradeSettingsPersistenceTests
{
    [Fact]
    public void JsonRoundTripRestoresOnlyUserRules()
    {
        var settings = new HdvTradeSettings
        {
            RelistOnSale = false,
            BuyRules = new List<HdvBuyRule>
            {
                new() { Gid = 100, Needed = 45, Lots = new List<HdvBuyLotRule>
                {
                    new() { Qty = 1, Allowed = false, Max = 25 },
                    new() { Qty = 10, Allowed = true, Max = 200 }
                }}
            },
            RelistRules = new List<HdvRelistRule>
            {
                new() { Gid = 200, Qty = 10, MinPrice = 1000, Keep = true }
            }
        };
        var json = JsonSerializer.Serialize(settings);
        var again = JsonSerializer.Deserialize<HdvTradeSettings>(json)!;
        Assert.Equal(45, again.BuyRules[0].Needed);
        Assert.False(again.BuyRules[0].Lots[0].Allowed);
        Assert.Equal(200, again.BuyRules[0].Lots[1].Max);
        Assert.True(again.RelistRules[0].Keep);
        Assert.Equal(1000, again.RelistRules[0].MinPrice);
        Assert.False(again.RelistOnSale);
        Assert.DoesNotContain("Owned", json);
        Assert.DoesNotContain("Bought", json);
        Assert.DoesNotContain("IsDry", json);
    }

    [Fact]
    public void DeepCloneDoesNotShareNestedLotOrRelistRules()
    {
        var a = new HdvTradeSettings
        {
            BuyRules = new List<HdvBuyRule> { new() { Gid = 1, Needed = 3, Lots = new() { new() { Qty = 10, Max = 99 } } } },
            RelistRules = new List<HdvRelistRule> { new() { Gid = 1, Qty = 10, Keep = true } }
        };
        var b = a.Clone();
        b.BuyRules[0].Lots[0].Max = 1;
        b.RelistRules[0].Keep = false;
        Assert.Equal(99, a.BuyRules[0].Lots[0].Max);
        Assert.True(a.RelistRules[0].Keep);
    }

    [Fact]
    public void SameCharacterOnTwoServersGetsDifferentKeys()
    {
        Assert.NotEqual(HdvProfileKey.For("Test", 1), HdvProfileKey.For("Test", 2));
        Assert.Equal(HdvProfileKey.For("Test", 1), HdvProfileKey.For("Test", 1));
        Assert.Equal("?", HdvProfileKey.For(null, 1));
    }

    [Fact]
    public void OldSettingsWithoutListsLoadAsEmptyRules()
    {
        var loaded = JsonSerializer.Deserialize<HdvTradeSettings>("{\"RelistOnSale\":true,\"BuyBudget\":3000}")!;
        Assert.Empty(loaded.BuyRules);
        Assert.Empty(loaded.RelistRules);
        Assert.True(loaded.RelistOnSale);
    }
}
