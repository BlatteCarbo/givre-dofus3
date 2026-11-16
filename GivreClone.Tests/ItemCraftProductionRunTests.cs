using System.Globalization;
using GivreClone.App.Services.ItemCraftPlanner;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public class ItemCraftProductionRunTests
{
    [Fact]
    public void Locator_reads_skillId_and_matches_only_an_enabled_interactive()
    {
        Assert.Null(WorkshopLocator.SkillId(null));
        Assert.Null(WorkshopLocator.SkillId(new Dictionary<string, object?> { ["elementId"] = 12 }));
        Assert.Null(WorkshopLocator.SkillId(new Dictionary<string, object?> { ["skillId"] = 0 }));
        Assert.Null(WorkshopLocator.SkillId(new Dictionary<string, object?> { ["skillId"] = 12.5d }));
        Assert.Equal(12, WorkshopLocator.SkillId(new Dictionary<string, object?> { ["skillId"] = 12L }));

        var disabled = Machine(4, 12, 5, false);
        var wrong = Machine(7, 9, 8, true);
        var usable = Machine(8, 12, 6, true);
        var zeroElement = new MapInteractive(1, 0, 12, 0, new[] { new InteractiveSkill(3, 12, 0, true) });

        Assert.Null(WorkshopLocator.Match(null, new[] { usable }));
        Assert.Null(WorkshopLocator.Match(12, new[] { disabled, wrong, zeroElement }));
        var found = WorkshopLocator.Match(12, new[] { disabled, wrong, usable });
        Assert.Equal(new ItemCraftWorkshop(8, 6, 12), found);
    }

    [Fact]
    public void Locator_requires_matching_quantities_and_does_not_treat_resultLevel_as_yield()
    {
        Assert.Null(WorkshopLocator.IngredientQuantities("[1,0]"));
        Assert.Equal(new[] { 2, 5 }, WorkshopLocator.IngredientQuantities("[2, 5]"));

        var row = new Dictionary<string, object?>
        {
            ["skillId"] = 12,
            ["quantities"] = "[2,1]",
            ["resultLevel"] = 40
        };
        var recipe = WorkshopLocator.Build(50, row, new[] { (5L, 2), (6L, 1) }, 0);
        Assert.NotNull(recipe);
        Assert.Equal(12, recipe!.SkillId);
        Assert.Null(recipe.ResultPerCraft);
        Assert.Equal(0, recipe.KamasPerCraft);
        Assert.Equal(new[] { 5L, 6L }, recipe.Inputs.Select(i => i.Gid));

        Assert.Null(WorkshopLocator.Build(50, row, new[] { (5L, 1), (6L, 1) }, 0));
        var yielded = new Dictionary<string, object?> { ["skillId"] = "12", ["quantities"] = "[2]", ["resultQuantity"] = 3 };
        Assert.Equal(3, WorkshopLocator.Build(9, yielded, new[] { (4L, 2) }, null)!.ResultPerCraft);
        Assert.Null(WorkshopLocator.Build(9, new Dictionary<string, object?> { ["skillId"] = 12, ["resultLevel"] = 3 }, new[] { (4L, 2) }, 0));
    }

    [Fact]
    public async Task Run_withdraws_then_buys_only_allowed_inputs_then_crafts_to_quantity()
    {
        var fake = Stocked();
        fake.Chest[5] = 10;
        fake.Prices[(6, 1)] = 4;
        var result = await new ItemCraftProductionRun().RunAsync(Order(quantity: 2), fake, null, CancellationToken.None);

        Assert.Equal(ItemCraftProductionStop.QuantityReached, result.Stop);
        Assert.Equal(2, result.Produced);
        Assert.Equal(8, result.Spent);
        Assert.Equal(0, result.Listed);
        Assert.Equal(4, fake.Withdraws.Where(w => w.Gid == 5).Sum(w => w.Qty));
        Assert.Equal(2, fake.Buys.Where(b => b.Gid == 6).Sum(b => b.Qty));
        Assert.DoesNotContain(fake.Buys, b => b.Gid == 5);
        Assert.Equal(2, fake.Crafts);
        Assert.All(fake.CraftCalls, call => Assert.Equal((3, 9, 1), call));
        Assert.Empty(fake.Lists);
        Assert.Equal(0, fake.Bag.GetValueOrDefault(6));
        int withdraw = fake.Log.FindIndex(line => line.StartsWith("withdraw:", StringComparison.Ordinal));
        int buy = fake.Log.FindIndex(line => line.StartsWith("buy:", StringComparison.Ordinal));
        int close = fake.Log.FindIndex(line => line == "close");
        int craft = fake.Log.FindIndex(line => line.StartsWith("craft:", StringComparison.Ordinal));
        Assert.True(withdraw >= 0 && withdraw < buy && buy < craft);
        Assert.True(close >= 0 && close < craft);
    }

    [Fact]
    public async Task Receipts_count_and_a_refused_buy_or_craft_does_not_spend()
    {
        var withdrawn = Stocked();
        withdrawn.UpdateBagOnWithdraw = false;
        withdrawn.Chest[5] = 4;
        var withdrawRun = await new ItemCraftProductionRun().RunAsync(
            Order(quantity: 1, yield: 1, inputs: new[] { new ItemCraftProductionInput(5, 4, false) }), withdrawn, null, CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.QuantityReached, withdrawRun.Stop);
        Assert.Equal(1, withdrawRun.Produced);
        Assert.Equal(0, withdrawn.Bag.GetValueOrDefault(5));
        Assert.Equal(1, withdrawn.BagReads);

        var refused = Stocked();
        refused.BuyOk = false;
        refused.Prices[(6, 1)] = 10;
        var buyRun = await new ItemCraftProductionRun().RunAsync(
            Order(quantity: 1, yield: 1, bank: false, inputs: new[] { new ItemCraftProductionInput(6, 1, true) }), refused, null, CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.BuyRefused, buyRun.Stop);
        Assert.Equal(0, buyRun.Spent);
        Assert.Equal(0, buyRun.Produced);
        Assert.Equal(0, refused.Crafts);

        var rejected = Stocked();
        rejected.Bag[5] = 1;
        rejected.CraftResult = new ItemCraftWorkshopReceipt(false, 1, 50);
        var craftRun = await new ItemCraftProductionRun().RunAsync(
            Order(quantity: 2, fee: 8, bank: false, inputs: new[] { new ItemCraftProductionInput(5, 1, false) }), rejected, null, CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.CraftUnconfirmed, craftRun.Stop);
        Assert.Equal(0, craftRun.Spent);
        Assert.Equal(0, craftRun.Produced);
        Assert.Equal(1, rejected.Crafts);

        var other = Stocked();
        other.Bag[5] = 1;
        other.CraftResult = new ItemCraftWorkshopReceipt(true, 1, 999);
        var wrong = await new ItemCraftProductionRun().RunAsync(
            Order(quantity: 1, fee: 8, bank: false, inputs: new[] { new ItemCraftProductionInput(5, 1, false) }), other, null, CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.CraftUnconfirmed, wrong.Stop);
        Assert.Equal(0, wrong.Spent);

        var batch = Stocked();
        batch.Bag[5] = 2;
        batch.CraftResult = new ItemCraftWorkshopReceipt(true, 3, 50);
        var once = await new ItemCraftProductionRun().RunAsync(
            Order(quantity: 3, fee: 7, bank: false, inputs: new[] { new ItemCraftProductionInput(5, 2, false) }), batch, null, CancellationToken.None);
        Assert.Equal(3, once.Produced);
        Assert.Equal(7, once.Spent);
        Assert.Equal(1, batch.Crafts);
    }

    [Fact]
    public async Task Budget_stops_further_crafts_and_limits_the_withdrawal()
    {
        var inBag = Stocked();
        inBag.Bag[5] = 10;
        var crafted = await new ItemCraftProductionRun().RunAsync(
            Order(quantity: 4, yield: 1, fee: 10, budget: 25, bank: false, inputs: new[] { new ItemCraftProductionInput(5, 1, false) }),
            inBag, null, CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.Budget, crafted.Stop);
        Assert.Equal(2, crafted.Produced);
        Assert.Equal(20, crafted.Spent);
        Assert.Equal(2, inBag.Crafts);
        Assert.DoesNotContain(inBag.Log, line => line == "open");

        var fromChest = Stocked();
        fromChest.Chest[8] = 100;
        var withdrawn = await new ItemCraftProductionRun().RunAsync(
            Order(quantity: 4, yield: 1, fee: 10, budget: 25, inputs: new[] { new ItemCraftProductionInput(8, 3, false) }),
            fromChest, null, CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.Budget, withdrawn.Stop);
        Assert.Equal(new[] { (8L, 6) }, fromChest.Withdraws);
        Assert.Equal(2, fromChest.Crafts);
        Assert.Equal(20, withdrawn.Spent);
    }

    [Fact]
    public async Task Partial_buy_under_the_budget_does_not_craft()
    {
        var fake = Stocked();
        fake.Prices[(6, 1)] = 30;
        var result = await new ItemCraftProductionRun().RunAsync(
            Order(quantity: 3, yield: 1, budget: 50, bank: false, inputs: new[] { new ItemCraftProductionInput(6, 1, true) }),
            fake, null, CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.Budget, result.Stop);
        Assert.Equal(30, result.Spent);
        Assert.Equal(0, result.Produced);
        Assert.Equal(0, fake.Crafts);
        Assert.Equal(new[] { (6L, 1) }, fake.Buys.Select(b => (b.Gid, b.Qty)));
    }

    [Fact]
    public async Task Sale_floor_lists_only_a_finished_run_and_stop_blocks_the_next_craft()
    {
        var listed = Stocked();
        listed.Bag[5] = 2;
        listed.CraftResult = new ItemCraftWorkshopReceipt(true, 10, 50);
        var done = await new ItemCraftProductionRun().RunAsync(
            Order(quantity: 10, fee: 0, budget: 0, bank: false, floor: 5, inputs: new[] { new ItemCraftProductionInput(5, 2, false) }),
            listed, null, CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.QuantityReached, done.Stop);
        Assert.Equal(10, done.Listed);
        Assert.Equal((10, 50L), Assert.Single(listed.Lists));
        Assert.True(listed.Lists[0].Price / listed.Lists[0].Qty >= 5);

        var failed = Stocked();
        failed.Bag[5] = 1;
        failed.ListOk = false;
        var unsold = await new ItemCraftProductionRun().RunAsync(
            Order(quantity: 1, yield: 1, bank: false, floor: 12, inputs: new[] { new ItemCraftProductionInput(5, 1, false) }),
            failed, null, CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.ListUnconfirmed, unsold.Stop);
        Assert.Equal(1, unsold.Produced);
        Assert.Equal(0, unsold.Listed);

        var runner = new ItemCraftProductionRun();
        var stopping = Stocked();
        stopping.Bag[5] = 2;
        var progress = new InlineProgress(p =>
        {
            if (p.Produced == 1) runner.Stop();
        });
        var halted = await runner.RunAsync(
            Order(quantity: 2, yield: 1, bank: false, inputs: new[] { new ItemCraftProductionInput(5, 1, false) }),
            stopping, progress, CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.Stopped, halted.Stop);
        Assert.Equal(1, halted.Produced);
        Assert.Equal(1, stopping.Crafts);
    }

    [Fact]
    public async Task Missing_cost_skill_or_workshop_and_a_closed_chest_do_not_spend()
    {
        var fake = Stocked();
        var noCost = await new ItemCraftProductionRun().RunAsync(Order(fee: null), fake, null, CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.UnknownCost, noCost.Stop);
        Assert.Equal(0, fake.MapReads);
        Assert.Equal(0, fake.BagReads);
        Assert.Equal(0, fake.Crafts);

        var noSkill = await new ItemCraftProductionRun().RunAsync(Order(skill: null), fake, null, CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.UnknownSkill, noSkill.Stop);
        Assert.Equal(0, fake.MapReads);

        fake.Map = new[] { Machine(4, 12, 5, false) };
        var missing = await new ItemCraftProductionRun().RunAsync(Order(), fake, null, CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.WorkshopMissing, missing.Stop);
        Assert.Equal(0, fake.BagReads);
        Assert.Equal(0, fake.Crafts);
        Assert.Equal(0, missing.Spent);

        var closed = Stocked();
        closed.Open = false;
        closed.Chest[5] = 5;
        var blocked = await new ItemCraftProductionRun().RunAsync(
            Order(quantity: 1, yield: 1, inputs: new[] { new ItemCraftProductionInput(5, 1, false) }), closed, null, CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.MissingInputs, blocked.Stop);
        Assert.Empty(closed.Withdraws);
        Assert.Equal(0, closed.Crafts);
        Assert.Contains(closed.Log, line => line == "open");
    }

    [Fact]
    public async Task A_second_run_does_not_start_while_the_first_is_active()
    {
        var release = new TaskCompletionSource<ItemCraftWorkshopReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = Stocked();
        fake.Bag[5] = 1;
        fake.CraftOverride = ct =>
        {
            entered.TrySetResult();
            return release.Task.WaitAsync(ct);
        };
        var runner = new ItemCraftProductionRun();
        var first = runner.RunAsync(
            Order(quantity: 1, yield: 1, bank: false, inputs: new[] { new ItemCraftProductionInput(5, 1, false) }),
            fake, null, CancellationToken.None);
        await entered.Task;
        var second = await runner.RunAsync(Order(), fake, null, CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.AlreadyRunning, second.Stop);
        Assert.Equal(0, second.Produced);
        Assert.Equal(0, second.Spent);
        release.TrySetResult(new ItemCraftWorkshopReceipt(true, 1, 50));
        var done = await first;
        Assert.Equal(1, done.Produced);
        Assert.Equal(1, fake.Crafts);
    }

    [Fact]
    public void Production_request_has_no_energy_gate()
    {
        Assert.DoesNotContain(typeof(ItemCraftProductionRequest).GetProperties(),
            property => property.Name.Contains("Energy", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(ItemCraftProductionRun).GetMethods(),
            method => method.Name.Contains("Energy", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(IItemCraftProductionActions).GetMethods(),
            method => method.Name.Contains("Energy", StringComparison.OrdinalIgnoreCase));
    }

    private static ItemCraftProductionRequest Order(int quantity = 2, int? yield = null, int? skill = 12, long? fee = 0,
        long budget = 1000, bool bank = true, long? floor = null, ItemCraftProductionInput[]? inputs = null)
        => new(new ItemCraftProductionRecipe(50, yield, skill, fee,
            inputs ?? new[] { new ItemCraftProductionInput(5, 2, false), new ItemCraftProductionInput(6, 1, true) }),
            quantity, budget, bank, floor);

    private static FakeActions Stocked() => new() { Map = new[] { Machine(3, 12, 9, true) } };

    private static MapInteractive Machine(int element, int skill, int instance, bool enabled)
        => new(1, element, 99, 0, new[] { new InteractiveSkill(instance, skill, 0, enabled) }, "Atelier");

    private sealed class InlineProgress : IProgress<ItemCraftProductionProgress>
    {
        private readonly Action<ItemCraftProductionProgress> _report;
        public InlineProgress(Action<ItemCraftProductionProgress> report) => _report = report;
        public void Report(ItemCraftProductionProgress value) => _report(value);
    }

    private sealed class FakeActions : IItemCraftProductionActions
    {
        public readonly List<string> Log = new();
        public readonly Dictionary<long, int> Bag = new();
        public readonly Dictionary<long, int> Chest = new();
        public readonly Dictionary<(long Gid, int Qty), long> Prices = new();
        public readonly List<(long Gid, int Qty)> Withdraws = new();
        public readonly List<(long Gid, int Qty, long Max)> Buys = new();
        public readonly List<(int Element, int Skill, int Count)> CraftCalls = new();
        public readonly List<(int Qty, long Price)> Lists = new();
        public IReadOnlyList<MapInteractive> Map = Array.Empty<MapInteractive>();
        public bool Open = true;
        public bool BuyOk = true;
        public bool ListOk = true;
        public bool UpdateBagOnWithdraw = true;
        public int BagReads;
        public int MapReads;
        public int Crafts;
        public ItemCraftWorkshopReceipt CraftResult = new(true, 1, 50);
        public Func<CancellationToken, Task<ItemCraftWorkshopReceipt>>? CraftOverride;

        public int BagCount(long gid) { BagReads++; return Bag.GetValueOrDefault(gid); }
        public int ChestCount(long gid) => Chest.GetValueOrDefault(gid);
        public IReadOnlyList<MapInteractive> Interactives() { MapReads++; Log.Add("map"); return Map; }

        public Task<bool> OpenChestAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Log.Add("open");
            return Task.FromResult(Open);
        }

        public Task<int> WithdrawAsync(long gid, int quantity, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Log.Add("withdraw:" + gid.ToString(CultureInfo.InvariantCulture) + ":" + quantity.ToString(CultureInfo.InvariantCulture));
            Withdraws.Add((gid, quantity));
            int got = Math.Min(quantity, Math.Max(0, Chest.GetValueOrDefault(gid)));
            if (UpdateBagOnWithdraw)
            {
                Chest[gid] = Chest.GetValueOrDefault(gid) - got;
                Bag[gid] = Bag.GetValueOrDefault(gid) + got;
            }
            return Task.FromResult(got);
        }

        public Task<bool> CloseChestAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Log.Add("close");
            return Task.FromResult(true);
        }

        public Task<long> LotPriceAsync(long gid, int qty, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Log.Add("price:" + gid.ToString(CultureInfo.InvariantCulture) + ":" + qty.ToString(CultureInfo.InvariantCulture));
            return Task.FromResult(Prices.GetValueOrDefault((gid, qty)));
        }

        public Task<bool> BuyAsync(long gid, int qty, long maxPrice, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Log.Add("buy:" + gid.ToString(CultureInfo.InvariantCulture) + ":" + qty.ToString(CultureInfo.InvariantCulture));
            Buys.Add((gid, qty, maxPrice));
            return Task.FromResult(BuyOk);
        }

        public Task<ItemCraftWorkshopReceipt> CraftAsync(int elementId, int skillInstanceUid,
            IReadOnlyList<(long Gid, int Qty)> ingredients, int count, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Crafts++;
            CraftCalls.Add((elementId, skillInstanceUid, count));
            Log.Add("craft:" + elementId.ToString(CultureInfo.InvariantCulture) + ":"
                + skillInstanceUid.ToString(CultureInfo.InvariantCulture) + ":" + count.ToString(CultureInfo.InvariantCulture));
            return CraftOverride?.Invoke(ct) ?? Task.FromResult(CraftResult);
        }

        public Task<bool> ListAsync(long gid, int quantity, long price, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Log.Add("list:" + quantity.ToString(CultureInfo.InvariantCulture) + ":" + price.ToString(CultureInfo.InvariantCulture));
            Lists.Add((quantity, price));
            return Task.FromResult(ListOk);
        }
    }
}
