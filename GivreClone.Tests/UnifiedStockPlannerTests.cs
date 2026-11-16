using GivreClone.App.Services.UnifiedStock;
using Xunit;

namespace GivreClone.Tests;

public class UnifiedStockPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 16, 0, 0, TimeSpan.Zero);
    private static StockObservation Obs(StockLocation location, string store, long gid, long quantity,
        StockConfidence confidence = StockConfidence.Fresh, bool complete = true, long characterId = 10,
        string version = "3.7.12.12")
        => new("account-A", characterId, location, store, $"event-{store}", Now, version,
            confidence, complete, [new StockStack(gid, $"uid-{store}", quantity)]);

    [Fact]
    public void Allocates_bag_before_bank_and_havenbag_for_all_targets_once()
    {
        var result = UnifiedStockPlanner.Allocate("account-A", 10, "3.7.12.12", Now,
            [Obs(StockLocation.Inventory, "bag:10", 20, 3),
             Obs(StockLocation.Bank, "bank:A", 20, 4),
             Obs(StockLocation.HavenBag, "haven:10", 20, 6)],
            new Dictionary<long, long> { [20] = 15 });
        var item = Assert.Single(result.Items);
        Assert.Equal(3, item.FromInventory);
        Assert.Equal(4, item.FromBank);
        Assert.Equal(6, item.FromHavenBag);
        Assert.Equal(2, item.Missing);
        Assert.True(result.AllSourcesFresh);
    }

    [Fact]
    public void Bank_alias_havenbag_does_not_double_count()
    {
        var result = UnifiedStockPlanner.Allocate("account-A", 10, "3.7.12.12", Now,
            [Obs(StockLocation.Bank, "shared-bank", 20, 5),
             Obs(StockLocation.HavenBag, "shared-bank", 20, 5)],
            new Dictionary<long, long> { [20] = 9 });
        var item = Assert.Single(result.Items);
        Assert.Equal(5, item.FromBank + item.FromHavenBag);
        Assert.Equal(4, item.Missing);
    }

    [Fact]
    public void Stale_partial_wrong_owner_and_version_are_not_consumed()
    {
        var stale = Obs(StockLocation.Bank, "bank:A", 20, 50, StockConfidence.Stale);
        var partial = Obs(StockLocation.HavenBag, "haven:10", 20, 50, complete: false);
        var wrong = Obs(StockLocation.Inventory, "bag:11", 20, 50, characterId: 11);
        var wrongVersion = Obs(StockLocation.Inventory, "bag:10", 20, 50, version: "3.7.11.11");
        var result = UnifiedStockPlanner.Allocate("account-A", 10, "3.7.12.12", Now,
            [stale, partial, wrong, wrongVersion], new Dictionary<long, long> { [20] = 12 });
        var item = Assert.Single(result.Items);
        Assert.Equal(12, item.Missing);
        Assert.Equal(12, item.UnknownStorage);
        Assert.False(result.AllSourcesFresh);
    }

    [Fact]
    public void Contradictory_shared_alias_is_rejected_instead_of_chosen_arbitrarily()
    {
        var result = UnifiedStockPlanner.Allocate("account-A", 10, "3.7.12.12", Now,
            [Obs(StockLocation.Bank, "shared", 20, 5),
             Obs(StockLocation.HavenBag, "shared", 20, 7)],
            new Dictionary<long, long> { [20] = 8 });
        Assert.Equal(8, Assert.Single(result.Items).Missing);
        Assert.Contains(result.Warnings, x => x.Contains("contradictoire"));
    }

    [Fact]
    public void Reserved_stacks_are_not_available_and_missing_store_is_unknown()
    {
        var reserved = Obs(StockLocation.Inventory, "bag:10", 20, 99) with
        {
            Stacks = [new StockStack(20, "uid-1", 99, Reserved: true)]
        };
        var result = UnifiedStockPlanner.Allocate("account-A", 10, "3.7.12.12", Now,
            [reserved], new Dictionary<long, long> { [20] = 10 });
        Assert.Equal(0, Assert.Single(result.Items).FromInventory);
        Assert.Equal(10, Assert.Single(result.Items).UnknownStorage);
    }

    [Fact]
    public void Same_game_item_uid_in_bag_and_bank_counts_once()
    {
        var bag = Obs(StockLocation.Inventory, "bag:10", 20, 7) with
            { Stacks = [new StockStack(20, "uid-123", 7)] };
        var bank = Obs(StockLocation.Bank, "bank:A", 20, 7) with
            { Stacks = [new StockStack(20, "uid-123", 7)] };
        var result = UnifiedStockPlanner.Allocate("account-A", 10, "3.7.12.12", Now,
            [bag, bank], new Dictionary<long, long> { [20] = 10 });
        var item = Assert.Single(result.Items);
        Assert.Equal(7, item.FromInventory);
        Assert.Equal(0, item.FromBank);
        Assert.Equal(3, item.Missing);
    }
}
