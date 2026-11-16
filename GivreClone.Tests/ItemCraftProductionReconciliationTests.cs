using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.ItemCraftPlanner;
using GivreClone.App.ViewModels;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Runner-level integrity scenarios. All IDs below belong to an in-memory
/// fixture; these tests do not contact the DOFUS client or claim a server ACK.
/// </summary>
public sealed class ItemCraftProductionReconciliationTests
{
    private static ItemCraftProductionRequest Order(
        int quantity = 1, int? yield = 1, long? saleFloor = null,
        bool bank = false, long fee = 4) =>
        new(new ItemCraftProductionRecipe(
            ResultGid: 900, ResultPerCraft: yield, SkillId: 7,
            KamasPerCraft: fee,
            Inputs: new[] { new ItemCraftProductionInput(10, 2, false) }),
            Quantity: quantity, MaxKamas: 100, BankAllowed: bank, SaleFloor: saleFloor);

    private static FakePort Stocked(int bag = 2) =>
        new() { BagCountValue = bag };

    [Theory]
    [InlineData(true, 1, 0)]
    [InlineData(true, 1, 901)]
    [InlineData(true, 0, 900)]
    [InlineData(false, 1, 900)]
    [InlineData(true, -1, 900)]
    public async Task Unknown_wrong_or_unsuccessful_craft_receipt_never_completes_or_lists(
        bool confirmed, int receiptQty, long receiptGid)
    {
        var fake = Stocked();
        fake.CraftReceipt = new(confirmed, receiptQty, receiptGid);

        var result = await new ItemCraftProductionRun().RunAsync(
            Order(saleFloor: 20), fake, null, CancellationToken.None);

        Assert.Equal(ItemCraftProductionStop.CraftUnconfirmed, result.Stop);
        Assert.Equal(0, result.Produced);
        Assert.Equal(0, result.Spent);
        Assert.Equal(0, result.Listed);
        Assert.Equal(1, fake.CraftCalls);
        Assert.Equal(0, fake.ListCalls);
    }

    [Theory]
    [InlineData(1, 2, 1)] // promised yield 1, suspicious result x2
    [InlineData(2, 1, 1)] // promised yield 2, suspicious result x1
    [InlineData(null, 2, 1)] // no known yield: cannot exceed remaining target
    public async Task Conflicting_result_quantity_never_reaches_durable_success(
        int? documentedYield, int actual, int requestedQuantity)
    {
        var fake = Stocked();
        fake.CraftReceipt = new(true, actual, 900);

        var result = await new ItemCraftProductionRun().RunAsync(
            Order(quantity: requestedQuantity, yield: documentedYield), fake, null,
            CancellationToken.None);

        Assert.Equal(ItemCraftProductionStop.CraftUnconfirmed, result.Stop);
        Assert.Equal(0, result.Produced);
        Assert.Equal(0, result.Spent);
        Assert.Equal(1, fake.CraftCalls);
    }

    [Fact]
    public async Task Exact_gid_and_documented_yield_are_credited_on_real_runner()
    {
        var fake = Stocked();
        fake.CraftReceipt = new(true, 2, 900);

        var result = await new ItemCraftProductionRun().RunAsync(
            Order(quantity: 2, yield: 2, fee: 6), fake, null, CancellationToken.None);

        Assert.Equal(ItemCraftProductionStop.QuantityReached, result.Stop);
        Assert.Equal(2, result.Produced);
        Assert.Equal(6, result.Spent);
        Assert.Equal(1, fake.CraftCalls);
    }

    [Theory]
    [InlineData(3)] // the requested withdrawal is exactly x2
    [InlineData(-1)]
    public async Task Bank_withdrawal_receipt_outside_requested_bounds_blocks_craft(int returned)
    {
        var fake = Stocked(bag: 0);
        fake.ChestCountValue = 4;
        fake.WithdrawReceipt = returned;

        var result = await new ItemCraftProductionRun().RunAsync(
            Order(bank: true), fake, null, CancellationToken.None);

        Assert.Equal(ItemCraftProductionStop.MissingInputs, result.Stop);
        Assert.Equal(0, result.Produced);
        Assert.Equal(0, fake.CraftCalls);
        Assert.Equal(1, fake.WithdrawCalls);
        Assert.Equal(1, fake.CloseCalls);
    }

    [Fact]
    public async Task Unconfirmed_bank_close_blocks_fabrication_even_if_withdrawal_succeeded()
    {
        var fake = Stocked(bag: 0);
        fake.ChestCountValue = 2;
        fake.WithdrawReceipt = 2;
        fake.CloseReceipt = false;

        var result = await new ItemCraftProductionRun().RunAsync(
            Order(bank: true), fake, null, CancellationToken.None);

        Assert.Equal(ItemCraftProductionStop.MissingInputs, result.Stop);
        Assert.Equal(0, result.Produced);
        Assert.Equal(0, fake.CraftCalls);
        Assert.Equal(1, fake.WithdrawCalls);
        Assert.Equal(1, fake.CloseCalls);
    }

    [Fact]
    public async Task Craft_reply_arriving_after_Stop_must_not_count_as_a_confirmed_result()
    {
        var fake = Stocked();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<ItemCraftWorkshopReceipt>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.CraftOverride = (_, _) =>
        {
            entered.TrySetResult();
            return release.Task; // deliberately ignores cancellation
        };

        var runner = new ItemCraftProductionRun();
        Task<ItemCraftProductionResult> running = runner.RunAsync(
            Order(saleFloor: 30), fake, null, CancellationToken.None);
        await entered.Task;
        runner.Stop();
        release.TrySetResult(new(true, 1, 900));
        ItemCraftProductionResult result = await running;

        Assert.Equal(ItemCraftProductionStop.Stopped, result.Stop);
        Assert.Equal(0, result.Produced);
        Assert.Equal(0, result.Listed);
        Assert.Equal(0, fake.ListCalls);
    }

    [Fact]
    public async Task Listing_reply_arriving_after_Stop_cannot_certify_Completed()
    {
        var fake = Stocked();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.ListOverride = (_, _, _) =>
        {
            entered.TrySetResult();
            return release.Task; // late success is not authorization
        };

        var runner = new ItemCraftProductionRun();
        Task<ItemCraftProductionResult> running = runner.RunAsync(
            Order(saleFloor: 5), fake, null, CancellationToken.None);
        await entered.Task;
        runner.Stop();
        release.TrySetResult(true);
        ItemCraftProductionResult result = await running;

        Assert.Equal(ItemCraftProductionStop.Stopped, result.Stop);
        Assert.Equal(1, result.Produced);
        Assert.Equal(0, result.Listed);
        Assert.Equal(1, fake.ListCalls);
    }

    [Fact]
    public async Task Withdrawal_reply_arriving_after_Stop_never_continues_to_craft()
    {
        var fake = Stocked(bag: 0);
        fake.ChestCountValue = 2;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.WithdrawOverride = (_, _, _) =>
        {
            entered.TrySetResult();
            return release.Task;
        };
        var runner = new ItemCraftProductionRun();
        Task<ItemCraftProductionResult> running = runner.RunAsync(
            Order(bank: true), fake, null, CancellationToken.None);
        await entered.Task;
        runner.Stop();
        release.TrySetResult(2);
        ItemCraftProductionResult result = await running;

        Assert.Equal(ItemCraftProductionStop.Stopped, result.Stop);
        Assert.Equal(0, result.Produced);
        Assert.Equal(0, fake.CraftCalls);
    }

    private sealed class FakePort : IItemCraftProductionActions
    {
        public int BagCountValue { get; set; }
        public int ChestCountValue { get; set; }
        public int? WithdrawReceipt { get; set; }
        public bool CloseReceipt { get; set; } = true;
        public ItemCraftWorkshopReceipt CraftReceipt { get; set; } = new(true, 1, 900);
        public Func<IReadOnlyList<(long Gid, int Qty)>, CancellationToken,
            Task<ItemCraftWorkshopReceipt>>? CraftOverride { get; set; }
        public Func<long, int, CancellationToken, Task<int>>? WithdrawOverride { get; set; }
        public Func<long, int, long, Task<bool>>? ListOverride { get; set; }
        public int CraftCalls { get; private set; }
        public int WithdrawCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public int ListCalls { get; private set; }

        public int BagCount(long gid) => gid == 10 ? BagCountValue : 0;
        public int ChestCount(long gid) => gid == 10 ? ChestCountValue : 0;
        public IReadOnlyList<MapInteractive> Interactives() => new[]
        {
            new MapInteractive(1, 17, 42, 0,
                new[] { new InteractiveSkill(19, 7, 0, true) }, "Synthetic workbench")
        };
        public Task<bool> OpenChestAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<int> WithdrawAsync(long gid, int quantity, CancellationToken ct)
        {
            WithdrawCalls++;
            return WithdrawOverride?.Invoke(gid, quantity, ct) ??
                Task.FromResult(WithdrawReceipt ?? quantity);
        }
        public Task<bool> CloseChestAsync(CancellationToken ct)
        {
            CloseCalls++;
            return Task.FromResult(CloseReceipt);
        }
        public Task<ItemCraftWorkshopReceipt> CraftAsync(int elementId, int skillInstanceUid,
            IReadOnlyList<(long Gid, int Qty)> ingredients, int count, CancellationToken ct)
        {
            CraftCalls++;
            Assert.Equal(17, elementId);
            Assert.Equal(19, skillInstanceUid);
            Assert.Equal(1, count);
            Assert.Equal((10L, 2), Assert.Single(ingredients));
            return CraftOverride?.Invoke(ingredients, ct) ?? Task.FromResult(CraftReceipt);
        }
        public Task<long> LotPriceAsync(long gid, int qty, CancellationToken ct) =>
            Task.FromResult(0L);
        public Task<bool> BuyAsync(long gid, int qty, long maxPrice, CancellationToken ct) =>
            Task.FromResult(false);
        public Task<bool> ListAsync(long gid, int quantity, long price, CancellationToken ct)
        {
            ListCalls++;
            return ListOverride?.Invoke(gid, quantity, price) ?? Task.FromResult(true);
        }
    }
}
