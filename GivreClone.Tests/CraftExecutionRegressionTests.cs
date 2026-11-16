using GivreClone.App.Services;
using GivreCore.Protobuf;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

[Collection("F08InventoryMapping")]
public sealed class CraftExecutionRegressionTests
{
    private static readonly ProtocolCatalog Mapping = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
    private static int F(string m, string f) => Mapping.FieldNumber(m, f)!.Value;
    private static ProtoField V(int f, long v) => new() { Field = f, Wire = WireType.Varint, Varint = (ulong)v };
    private static ProtoField M(int f, params ProtoField[] v) => new() { Field = f, Wire = WireType.Len, Message = v.ToList() };
    private static DecodedPacket Placed(int uid, int qty, bool remote = false) => new(9, false, "fixture", "ExchangeObjectsAddedEvent",
        [V(F("ExchangeObjectsAddedEvent", "remote"), remote ? 1 : 0),
         M(F("ExchangeObjectsAddedEvent", "objects"), M(F("ObjectItemInventory", "item"),
            V(F("ObjectItem", "uid"), uid), V(F("ObjectItem", "gid"), 100), V(F("ObjectItem", "quantity"), qty)))], []);
    private static LaunchService Workshop()
    {
        var service = new LaunchService();
        service.Inventory.Seed(9, [new InventoryStore.Item(10, 100, 20)]);
        foreach (var name in new[] { "ExchangeCraftStartedEvent", "ExchangeLeaveEvent" })
            service.Responses.Add(new DecodedPacket(9, false, "fixture", name, Array.Empty<ProtoField>(), []));
        service.Responses.Add(Placed(10, 2));
        return service;
    }
    [Fact]
    public async Task Cancellation_after_placement_before_resync_delay_never_sends_ready_or_retries()
    {
        var service = Workshop();
        using var cancellation = new CancellationTokenSource();
        int returns = 0;
        service.BeforeWaitReturn = () => { if (++returns == 2) cancellation.Cancel(); };
        var result = await service.CraftAsync(9, 20, 30, [(100L, 2)], 1, cancellation.Token);
        Assert.False(result.Ok);
        Assert.DoesNotContain(service.RequestedNames, n => n.Contains("ExchangeCraftResultEvent"));
        Assert.Single(service.RequestedNames.Where(n => n.Contains("ExchangeCraftStartedEvent")));
        Assert.Contains(service.RequestedNames, n => n.Contains("ExchangeLeaveEvent"));
    }

    [Fact]
    public async Task Missing_result_after_validation_does_not_open_or_execute_a_second_craft()
    {
        var service = Workshop();
        service.Responses.RemoveAll(p => p.Name == "ExchangeLeaveEvent");
        var result = await service.CraftAsync(9, 20, 30, [(100L, 2)], 1);
        Assert.False(result.Ok);
        Assert.Single(service.RequestedNames.Where(n => n.Contains("ExchangeCraftStartedEvent")));
        Assert.Single(service.RequestedNames.Where(n => n.Contains("ExchangeCraftResultEvent")));
    }
    [Fact]
    public async Task Unconfirmed_count_closes_workshop_without_ready_or_retry()
    {
        var service = Workshop();
        // No response to count; a leave response must not be mistaken for count confirmation.
        service.Responses.RemoveAll(p => p.Name == "ExchangeLeaveEvent");
        var result = await service.CraftAsync(9, 20, 30, [(100L, 2)], 5);
        Assert.False(result.Ok);
        Assert.Single(service.RequestedNames.Where(n => n.Contains("ExchangeCraftCountModifiedEvent")));
        Assert.DoesNotContain(service.RequestedNames, n => n.Contains("ExchangeCraftResultEvent"));
        Assert.Single(service.RequestedNames.Where(n => n.Contains("ExchangeCraftStartedEvent")));
    }
    [Theory]
    [InlineData(0, 100, 1)]
    [InlineData(1, -1, 1)]
    [InlineData(1, 100, 0)]
    public async Task Invalid_recipe_does_not_send_anything(int count, long gid, int qty)
    {
        var service = Workshop();
        Assert.False((await service.CraftAsync(9, 20, 30, [(gid, qty)], count)).Ok);
        Assert.Empty(service.RequestedNames);
    }
    [Fact]
    public async Task Ingredients_are_split_over_bag_stacks_without_using_equipment()
    {
        var service = Workshop();
        service.Responses.RemoveAll(p => p.Name == "ExchangeLeaveEvent");
        service.Inventory.Seed(9, [new InventoryStore.Item(10, 100, 1), new(11, 100, 1), new(12, 100, 10, 5)]);
        service.Responses.Add(Placed(10, 1));
        service.Responses.Add(Placed(11, 1));
        await service.CraftAsync(9, 20, 30, [(100L, 2)], 1);
        Assert.Equal(2, service.RequestedNames.Count(n => n.Contains("ExchangeObjectsAddedEvent")));
        Assert.Single(service.RequestedNames.Where(n => n.Contains("ExchangeCraftResultEvent")));
    }
    [Fact]
    public async Task Duplicate_ingredients_are_combined_before_checking_available_stock()
    {
        var service = Workshop();
        service.Inventory.Seed(9, [new InventoryStore.Item(10, 100, 1)]);
        await service.CraftAsync(9, 20, 30, [(100L, 1), (100L, 1)], 1);
        Assert.DoesNotContain(service.RequestedNames, n => n.Contains("ExchangeObjectsAddedEvent"));
        Assert.DoesNotContain(service.RequestedNames, n => n.Contains("ExchangeCraftResultEvent"));
    }

    [Theory]
    [InlineData(99, 2, false)]
    [InlineData(10, 1, false)]
    [InlineData(10, 2, true)]
    public async Task Wrong_object_quantity_or_side_cannot_confirm_ingredient_placement(int uid, int qty, bool remote)
    {
        var service = Workshop();
        service.Responses.RemoveAll(p => p.Name is "ExchangeObjectsAddedEvent" or "ExchangeLeaveEvent");
        service.Responses.Add(Placed(uid, qty, remote));
        await service.CraftAsync(9, 20, 30, [(100L, 2)], 1);
        Assert.DoesNotContain(service.RequestedNames, n => n.Contains("ExchangeCraftResultEvent"));
    }
    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public async Task Only_the_requested_craft_count_can_allow_validation(int confirmed, bool ready)
    {
        var service = Workshop();
        service.Responses.RemoveAll(p => p.Name == "ExchangeLeaveEvent");
        service.Responses.Add(new DecodedPacket(9, false, "fixture", "ExchangeCraftCountModifiedEvent",
            [V(F("ExchangeCraftCountModifiedEvent", "count"), confirmed)], []));
        await service.CraftAsync(9, 20, 30, [(100L, 2)], 5);
        Assert.Equal(ready, service.RequestedNames.Any(n => n.Contains("ExchangeCraftResultEvent")));
    }

    [Fact]
    public async Task Direct_object_item_receipt_confirms_the_matching_placement()
    {
        var service = Workshop();
        service.Responses.RemoveAll(p => p.Name is "ExchangeObjectsAddedEvent" or "ExchangeLeaveEvent");
        service.Responses.Add(new DecodedPacket(9, false, "fixture", "ExchangeObjectsAddedEvent",
            [M(F("ExchangeObjectsAddedEvent", "objects"), V(F("ObjectItem", "uid"), 10),
                V(F("ObjectItem", "gid"), 100), V(F("ObjectItem", "quantity"), 2))], []));
        await service.CraftAsync(9, 20, 30, [(100L, 2)], 1);
        Assert.Contains(service.RequestedNames, n => n.Contains("ExchangeCraftResultEvent"));
    }

}
