using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.ItemCraftPlanner;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Exercise the REAL production runner and its mutation ports, not a standalone receipt helper.</summary>
public sealed class ItemCraftProductionReceiptHardeningPrimeTests
{
    private static readonly MapInteractive Workshop = new(1, 3, 99, 0,
        new[] { new InteractiveSkill(9, 12, 0, true) }, "Atelier");

    private static ItemCraftProductionRequest Order(int? yield = 1, bool bank = false,
        long? listingFloor = 2) => new(
            new ItemCraftProductionRecipe(50, yield, 12, 0,
                new[] { new ItemCraftProductionInput(5, 1, false) }),
            Quantity: 1, MaxKamas: 0, BankAllowed: bank, SaleFloor: listingFloor);

    [Theory]
    [InlineData(true, 1, 0L)]
    [InlineData(true, 1, 999L)]
    [InlineData(false, 1, 50L)]
    [InlineData(true, 3, 50L)]
    [InlineData(true, 0, 50L)]
    public async Task Runner_never_certifies_unknown_wrong_or_inconsistent_target_receipt(
        bool confirmed, int quantity, long gid)
    {
        var port = new Actions { CraftReceipt = new(confirmed, quantity, gid) };
        port.Bag[5] = 1;
        var result = await new ItemCraftProductionRun().RunAsync(Order(), port, null, CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.CraftUnconfirmed, result.Stop);
        Assert.Equal(0, result.Produced);
        Assert.Equal(0, result.Listed);
        Assert.Empty(port.Listings);
        Assert.Equal(1, port.CraftCalls);
    }

    [Fact]
    public async Task Valid_exact_craft_receipt_still_reaches_target_and_lists()
    {
        var port = new Actions();
        port.Bag[5] = 1;
        var result = await new ItemCraftProductionRun().RunAsync(Order(), port, null, CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.QuantityReached, result.Stop);
        Assert.Equal(1, result.Produced);
        Assert.Equal(1, result.Listed);
        Assert.Single(port.Listings);
    }

    [Fact]
    public async Task Withdraw_overreport_prevents_any_craft_or_listing()
    {
        var port = new Actions { WithdrawResult = 2 };
        port.Chest[5] = 1;
        var result = await new ItemCraftProductionRun().RunAsync(Order(bank: true), port,
            null, CancellationToken.None);
        Assert.NotEqual(ItemCraftProductionStop.QuantityReached, result.Stop);
        Assert.Equal(0, result.Produced);
        Assert.Equal(0, port.CraftCalls);
        Assert.Empty(port.Listings);
    }

    [Fact]
    public async Task Stopped_runner_rejects_late_server_receipt_from_unfinished_await()
    {
        var port = new Actions();
        port.Bag[5] = 1;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<ItemCraftWorkshopReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        port.CraftOverride = _ => { entered.TrySetResult(); return release.Task; };
        var runner = new ItemCraftProductionRun();
        Task<ItemCraftProductionResult> pending = runner.RunAsync(Order(), port,
            null, CancellationToken.None);
        await entered.Task;
        runner.Stop();
        release.TrySetResult(new ItemCraftWorkshopReceipt(true, 1, 50));
        var result = await pending;
        Assert.Equal(ItemCraftProductionStop.Stopped, result.Stop);
        Assert.Equal(0, result.Produced);
        Assert.Empty(port.Listings);
    }

    private sealed class Actions : IItemCraftProductionActions
    {
        public readonly Dictionary<long, int> Bag = new();
        public readonly Dictionary<long, int> Chest = new();
        public readonly List<(long Gid, int Quantity, long Price)> Listings = new();
        public ItemCraftWorkshopReceipt CraftReceipt = new(true, 1, 50);
        public Func<CancellationToken, Task<ItemCraftWorkshopReceipt>>? CraftOverride;
        public int WithdrawResult = -1;
        public int CraftCalls;
        public int BagCount(long gid) => Bag.GetValueOrDefault(gid);
        public int ChestCount(long gid) => Chest.GetValueOrDefault(gid);
        public IReadOnlyList<MapInteractive> Interactives() => new[] { Workshop };
        public Task<bool> OpenChestAsync(CancellationToken _) => Task.FromResult(true);
        public Task<int> WithdrawAsync(long gid, int count, CancellationToken _)
            => Task.FromResult(WithdrawResult >= 0 ? WithdrawResult : Math.Min(count, ChestCount(gid)));
        public Task<bool> CloseChestAsync(CancellationToken _) => Task.FromResult(true);
        public Task<ItemCraftWorkshopReceipt> CraftAsync(int elementId, int uid,
            IReadOnlyList<(long Gid, int Qty)> ingredients, int count, CancellationToken ct)
        {
            CraftCalls++;
            return CraftOverride?.Invoke(ct) ?? Task.FromResult(CraftReceipt);
        }
        public Task<bool> ListAsync(long gid, int qty, long price, CancellationToken _)
        {
            Listings.Add((gid, qty, price));
            return Task.FromResult(true);
        }
        public Task<long> LotPriceAsync(long gid, int qty, CancellationToken _)
            => Task.FromResult(100L);
        public Task<bool> BuyAsync(long gid, int qty, long maxPrice, CancellationToken _)
            => Task.FromResult(false);
    }
}
