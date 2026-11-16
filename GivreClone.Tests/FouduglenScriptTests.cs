extern alias F06App;

using MoonSharp.Interpreter;
using FouduglenScript = F06App::GivreClone.App.Services.Treasure.FouduglenScript;
using Xunit;

namespace GivreClone.Tests;

public sealed class FouduglenScriptTests
{
    private static Script Host(int count = 2, int budget = 100, int maxUnitPrice = 10)
    {
        var host = new Script(CoreModules.Preset_SoftSandbox);
        host.DoString("""
            stock = {[15902]=0, [15903]=0, [15904]=0}
            offers = {[15902]=10, [15903]=10}
            purchases = {[15902]=0, [15903]=0}
            balance, paid, buyAttempts = 100, 0, 0
            starts, digs, crafts, crafted = 0, 0, 0, 0
            stopped, hunting, fighting = false, false, false
            craftAccepted, craftConfirmed = true, true
            current = 128452097
            function print(...) end
            function stopScript() stopped = true end
            function getInventoryItemCount(gid) return stock[gid] or 0 end
            function getMapId() return current end
            function travelTo(id) current = id; return true end
            function goToHdvBuy(id) return id == 191104004 end
            character = {}
            function character:kamas() return balance end
            sale = {}
            function sale:GetPriceItem(gid, quantity)
                assert(quantity == 1)
                return offers[gid] or 0
            end
            function sale:buyItem(gid, quantity, price)
                buyAttempts = buyAttempts + 1
                assert(quantity == 1)
                if not offers[gid] or offers[gid] < 1
                    or offers[gid] ~= price or balance < price then return false end
                balance = balance - price
                paid = paid + price
                purchases[gid] = purchases[gid] + quantity
                stock[gid] = getInventoryItemCount(gid) + quantity
                return true
            end
            global = {}
            function global:leaveDialog() end
            map = {}
            function map:getInteractives()
                return {
                    {elementId=1, skills={{id=275, uid=11, enabled=true}}},
                    {elementId=2, skills={{id=277, uid=22, enabled=true}}}
                }
            end
            function map:useById(elementId, uid)
                return current == 128452097
                    and ((elementId == 1 and uid == 11) or (elementId == 2 and uid == 22))
            end
            function craftItem(elementId, uid, ingredients, quantity)
                crafts = crafts + 1
                assert(elementId == 1 and uid == 11)
                assert(#ingredients == 2)
                assert(ingredients[1].gid == 15902 and ingredients[1].quantity == 1)
                assert(ingredients[2].gid == 15903 and ingredients[2].quantity == 1)
                assert(quantity >= 1 and quantity == math.floor(quantity))
                if not craftAccepted then return false end
                for _, ingredient in ipairs(ingredients) do
                    assert(getInventoryItemCount(ingredient.gid) >= ingredient.quantity * quantity)
                end
                if craftConfirmed then
                    for _, ingredient in ipairs(ingredients) do
                        stock[ingredient.gid] = stock[ingredient.gid] - ingredient.quantity * quantity
                    end
                    stock[15904] = stock[15904] + quantity
                    crafted = crafted + quantity
                end
                return true
            end
            treasure = {}
            function treasure:isHunting() return hunting end
            function treasure:startLegendary(id)
                assert(id == 29 and current == 128452097)
                assert(not hunting and getInventoryItemCount(15904) > 0)
                stock[15904] = stock[15904] - 1
                starts = starts + 1
                hunting = true
                return true
            end
            function treasure:isFightStep() return hunting end
            function treasure:getStartMapId() return 128452097 end
            function treasure:huntDig()
                assert(hunting and not fighting)
                digs = digs + 1
                fighting = true
                return true
            end
            function finishFight(result)
                assert(fighting)
                hunting, fighting = false, false
                onFightEnd(result)
            end
            """);
        host.DoString(FouduglenScript.Build(count, budget, maxUnitPrice));
        return host;
    }

    private static bool Move(Script host) => host.Call(host.Globals.Get("move")).Boolean;

    private static double Number(Script host, string name) => host.Globals.Get(name).Number;

    [Theory]
    [InlineData(0, 100, 10, 100, 0)]
    [InlineData(-1, 100, 10, 100, 0)]
    [InlineData(11, 100, 10, 100, 0)]
    [InlineData(10, 9, 10, 100, 0)]
    [InlineData(10, 15, 10, 100, 1)]
    [InlineData(10, 100, 10, 9, 0)]
    [InlineData(10, 100, 10, 15, 1)]
    [InlineData(10, 0, 10, 100, 0)]
    [InlineData(10, 100, 0, 100, 0)]
    public void Unaffordable_or_absent_offers_stop_before_an_invalid_purchase(
        int price, int budget, int unitCap, int balance, int expectedPurchases)
    {
        var host = Host(count: 1, budget: budget, maxUnitPrice: unitCap);
        host.Globals["balance"] = balance;
        host.Globals.Get("offers").Table.Set(15902, DynValue.NewNumber(price));
        host.Globals.Get("offers").Table.Set(15903, DynValue.NewNumber(price));

        Assert.False(Move(host));

        Assert.True(host.Globals.Get("stopped").Boolean);
        Assert.Equal(expectedPurchases, Number(host, "buyAttempts"));
        Assert.Equal(expectedPurchases * price, Number(host, "paid"));
        Assert.Equal(balance - expectedPurchases * price, Number(host, "balance"));
        Assert.Equal(0, Number(host, "crafts"));
        Assert.Equal(0, Number(host, "starts"));
        Assert.False(Move(host));
        Assert.Equal(expectedPurchases, Number(host, "buyAttempts"));
    }

    [Fact]
    public void Missing_second_offer_stops_after_only_the_affordable_first_purchase()
    {
        var host = Host(count: 1);
        host.DoString("offers[15903] = nil");

        Assert.False(Move(host));

        Assert.True(host.Globals.Get("stopped").Boolean);
        Assert.Equal(1, Number(host, "buyAttempts"));
        Assert.Equal(10, Number(host, "paid"));
        Assert.Equal(1, host.Globals.Get("stock").Table.Get(15902).Number);
        Assert.Equal(0, Number(host, "crafts"));
        Assert.Equal(0, Number(host, "starts"));
    }

    [Fact]
    public void Existing_fragment_is_reused_to_craft_two_cards()
    {
        var host = Host(count: 2, budget: 30);
        host.Globals.Get("stock").Table.Set(15902, DynValue.NewNumber(1));

        Assert.True(Move(host));

        Assert.Equal(1, host.Globals.Get("purchases").Table.Get(15902).Number);
        Assert.Equal(2, host.Globals.Get("purchases").Table.Get(15903).Number);
        Assert.Equal(3, Number(host, "buyAttempts"));
        Assert.Equal(30, Number(host, "paid"));
        Assert.Equal(70, Number(host, "balance"));
        Assert.Equal(1, Number(host, "crafts"));
        Assert.Equal(2, Number(host, "crafted"));
        Assert.Equal(0, host.Globals.Get("stock").Table.Get(15902).Number);
        Assert.Equal(0, host.Globals.Get("stock").Table.Get(15903).Number);
        Assert.Equal(1, host.Globals.Get("stock").Table.Get(15904).Number);
        Assert.Equal(1, Number(host, "starts"));
        Assert.True(host.Globals.Get("hunting").Boolean);
        Assert.False(host.Globals.Get("stopped").Boolean);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Unconfirmed_craft_stops_before_starting_a_legendary_hunt(bool accepted, bool confirmed)
    {
        var host = Host();
        host.Globals["craftAccepted"] = accepted;
        host.Globals["craftConfirmed"] = confirmed;

        Assert.False(Move(host));

        Assert.True(host.Globals.Get("stopped").Boolean);
        Assert.Equal(4, Number(host, "buyAttempts"));
        Assert.Equal(1, Number(host, "crafts"));
        Assert.Equal(0, Number(host, "crafted"));
        Assert.Equal(0, Number(host, "starts"));
        Assert.False(host.Globals.Get("hunting").Boolean);
        Assert.False(Move(host));
        Assert.Equal(4, Number(host, "buyAttempts"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Requested_count_finishes_only_after_that_many_confirmed_wins(int count)
    {
        var host = Host(count);
        // Unsolicited and duplicate events must not count as completed cycles.
        host.DoString("onFightEnd({won=true})");
        for (var cycle = 1; cycle <= count; cycle++)
        {
            Assert.True(Move(host));
            Assert.Equal(cycle, Number(host, "starts"));
            Assert.True(host.Globals.Get("hunting").Boolean);
            Assert.True(Move(host));
            Assert.Equal(cycle, Number(host, "digs"));
            Assert.True(Move(host));
            Assert.Equal(cycle, Number(host, "starts"));
            Assert.Equal(cycle, Number(host, "digs"));
            host.DoString("finishFight({won=true}); onFightEnd({won=true})");
        }

        Assert.False(Move(host));
        Assert.False(host.Globals.Get("stopped").Boolean);
        Assert.Equal(count, Number(host, "starts"));
        Assert.Equal(count, Number(host, "digs"));
        Assert.Equal(count, Number(host, "crafted"));
        Assert.Equal(count * 2, Number(host, "buyAttempts"));
        Assert.Equal(count * 20, Number(host, "paid"));
        Assert.Equal(0, host.Globals.Get("stock").Table.Get(15904).Number);
    }

    [Fact]
    public void Existing_cards_need_no_purchase_or_craft_even_with_zero_budget()
    {
        var host = Host(count: 2, budget: 0, maxUnitPrice: 0);
        host.Globals.Get("stock").Table.Set(15904, DynValue.NewNumber(2));

        Assert.True(Move(host));
        Assert.True(Move(host));
        host.DoString("finishFight({won=true})");
        Assert.True(Move(host));
        Assert.True(Move(host));
        host.DoString("finishFight({won=true})");

        Assert.False(Move(host));
        Assert.Equal(2, Number(host, "starts"));
        Assert.Equal(0, Number(host, "buyAttempts"));
        Assert.Equal(0, Number(host, "crafts"));
        Assert.False(host.Globals.Get("stopped").Boolean);
    }

    [Theory]
    [InlineData("{won=false}")]
    [InlineData("{}")]
    [InlineData("nil")]
    public void Non_winning_result_stops_without_another_purchase_or_hunt(string result)
    {
        var host = Host(count: 3);
        Assert.True(Move(host));
        Assert.True(Move(host));
        host.DoString("finishFight({won=true})");
        Assert.True(Move(host));
        Assert.True(Move(host));

        host.DoString("finishFight(" + result + ")");

        Assert.True(host.Globals.Get("stopped").Boolean);
        Assert.False(Move(host));
        Assert.False(Move(host));
        Assert.Equal(2, Number(host, "starts"));
        Assert.Equal(2, Number(host, "digs"));
        Assert.Equal(6, Number(host, "buyAttempts"));
        Assert.Equal(60, Number(host, "paid"));
        Assert.Equal(1, host.Globals.Get("stock").Table.Get(15904).Number);
    }
}
