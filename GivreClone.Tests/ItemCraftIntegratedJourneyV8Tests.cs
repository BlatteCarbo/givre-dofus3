extern alias F06App;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;
using Journal = F06App::GivreClone.App.Services.Production.ItemCraftProductionAttemptJournal;
using Phase = F06App::GivreClone.App.Services.Production.ItemCraftAttemptPhase;
using Decision = F06App::GivreClone.App.Services.Production.ItemCraftAttemptDecision;
using BoundPort = F06App::GivreClone.App.Services.Production.ItemCraftSessionBoundActions;
using Runner = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionRun;
using Request = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionRequest;
using Recipe = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionRecipe;
using Input = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionInput;
using Receipt = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftWorkshopReceipt;
using Result = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionResult;
using Stop = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionStop;
using ActionPort = F06App::GivreClone.App.Services.ItemCraftPlanner.IItemCraftProductionActions;
using StockPort = F06App::GivreClone.App.Services.ItemCraftPlanner.IItemCraftProductionStockObservation;
using Vm = F06App::GivreClone.App.ViewModels.ItemCraftPlannerViewModel;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Offline integration of the same journal, session wrapper, real runner and
/// VM completion/status policies used by the production button. No game sends.
/// </summary>
public sealed class ItemCraftIntegratedJourneyV8Tests
{
    private const string Character = "server:292:TestV8";

    private sealed class Files : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(),
            "givre-v8-itemcraft-" + Guid.NewGuid().ToString("N"));
        public Journal Open() => new(Path.Combine(_directory, "production", "attempts.journal"));
        public void Dispose()
        {
            try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static Request Order(bool bank = false, long? saleFloor = null) =>
        new(new Recipe(900, 1, 7, 0, new[] { new Input(10, 2, false) }),
            1, 0, bank, saleFloor);

    private static bool Finish(Journal journal,
        F06App::GivreClone.App.Services.Production.ItemCraftAttemptLease lease,
        Request request, Result outcome, bool current, bool revoked) =>
        journal.TryFinish(lease, Vm.CanCloseProductionAsCompleted(
            outcome, request, current, revoked));

    [Fact]
    public async Task Normal_warehouse_craft_and_sale_complete_from_the_real_runner_and_allow_next_opt_in()
    {
        using var files = new Files();
        var journal = files.Open();
        var order = Order(bank: true, saleFloor: 8);
        Assert.Equal(Decision.Allowed, journal.TryBegin(Character, 4, order, out var lease));
        var source = new FakeActions { BankStock = 2 };
        var bound = new BoundPort(source, () => true);
        Result result = await new Runner().RunAsync(order, bound, null, CancellationToken.None);

        Assert.Equal(Stop.QuantityReached, result.Stop);
        Assert.Equal(1, result.Produced);
        Assert.Equal(1, result.Listed);
        Assert.Equal(1, source.Withdrawals);
        Assert.Equal(1, source.Crafts);
        Assert.Equal(1, source.Listings);
        Assert.True(Finish(journal, lease, order, result, true, bound.IsRevoked));
        var status = files.Open().ReadStatus(Character);
        Assert.Equal(Phase.Completed, status.Phase);
        Assert.Contains("TERMINÉ", Vm.FormatProductionRecovery(status));
        Assert.Equal(Decision.Allowed, files.Open().TryBegin(Character, 5,
            Order(), out _));
    }

    [Fact]
    public async Task Craft_result_without_corresponding_bag_delta_is_uncertain_after_restart()
    {
        using var files = new Files();
        var journal = files.Open();
        var order = Order();
        Assert.Equal(Decision.Allowed, journal.TryBegin(Character, 11, order, out var lease));
        var source = new FakeActions { DoNotUpdateOnCraft = true, BagStock = 2 };
        var guarded = new BoundPort(source, () => true);
        Result result = await new Runner().RunAsync(order, guarded, null, CancellationToken.None);

        Assert.Equal(Stop.StockUnverified, result.Stop);
        Assert.Equal(0, result.Produced);
        Assert.Equal(1, source.Crafts);
        Assert.True(Finish(journal, lease, order, result, true, guarded.IsRevoked));
        Assert.Equal(Phase.Uncertain, files.Open().ReadStatus(Character).Phase);
        Assert.Equal(Decision.NeedsReconciliation,
            files.Open().TryBegin(Character, 12, order, out _));
        Assert.Contains("BLOQUÉ", Vm.FormatProductionRecovery(files.Open().ReadStatus(Character)));
    }

    [Fact]
    public async Task Bank_receipt_without_a_matching_inventory_change_stops_before_craft()
    {
        using var files = new Files();
        var journal = files.Open();
        var order = Order(bank: true);
        Assert.Equal(Decision.Allowed, journal.TryBegin(Character, 11, order, out var lease));
        var source = new FakeActions { BankStock = 2, DoNotUpdateOnWithdraw = true };
        var guarded = new BoundPort(source, () => true);
        Result result = await new Runner().RunAsync(order, guarded, null, CancellationToken.None);

        Assert.Equal(Stop.StockUnverified, result.Stop);
        Assert.Equal(0, source.Crafts);
        Assert.True(Finish(journal, lease, order, result, true, guarded.IsRevoked));
        Assert.Equal(Phase.Uncertain, files.Open().ReadStatus(Character).Phase);
    }

    [Fact]
    public async Task Listing_receipt_without_a_matching_stock_decrement_cannot_finish_durably()
    {
        using var files = new Files();
        var journal = files.Open();
        var order = Order(saleFloor: 8);
        Assert.Equal(Decision.Allowed, journal.TryBegin(Character, 11, order, out var lease));
        var source = new FakeActions { BagStock = 2, DoNotUpdateOnList = true };
        var guarded = new BoundPort(source, () => true);
        Result result = await new Runner().RunAsync(order, guarded, null, CancellationToken.None);

        Assert.Equal(Stop.StockUnverified, result.Stop);
        Assert.Equal(1, result.Produced);
        Assert.Equal(0, result.Listed);
        Assert.True(Finish(journal, lease, order, result, true, guarded.IsRevoked));
        Assert.Equal(Phase.Uncertain, files.Open().ReadStatus(Character).Phase);
        Assert.Equal(Decision.NeedsReconciliation,
            files.Open().TryBegin(Character, 12, order, out _));
    }

    [Fact]
    public async Task If_observation_port_disappears_during_craft_completed_is_impossible()
    {
        using var files = new Files();
        var journal = files.Open();
        var order = Order();
        Assert.Equal(Decision.Allowed, journal.TryBegin(Character, 11, order, out var lease));
        var source = new FakeActions { BagStock = 2, LoseObservationOnCraft = true };
        var guarded = new BoundPort(source, () => true);
        Result result = await new Runner().RunAsync(order, guarded, null, CancellationToken.None);

        Assert.Equal(Stop.StockUnverified, result.Stop);
        Assert.Equal(0, result.Produced);
        Assert.True(Finish(journal, lease, order, result, true, guarded.IsRevoked));
        Assert.Equal(Phase.Uncertain, files.Open().ReadStatus(Character).Phase);
    }

    [Fact]
    public async Task Identity_lost_during_craft_blocks_completion_and_remains_revoked_even_if_identity_returns()
    {
        using var files = new Files();
        var journal = files.Open();
        var order = Order();
        Assert.Equal(Decision.Allowed, journal.TryBegin(Character, 11, order, out var lease));
        bool current = true;
        var source = new FakeActions { BagStock = 2, AfterCraft = () => current = false };
        var guarded = new BoundPort(source, () => current);
        Result result = await new Runner().RunAsync(order, guarded, null, CancellationToken.None);
        Assert.NotEqual(Stop.QuantityReached, result.Stop);
        Assert.True(guarded.IsRevoked);
        current = true;
        Assert.False(Vm.CanCloseProductionAsCompleted(result, order, current, guarded.IsRevoked));
        Assert.True(Finish(journal, lease, order, result, current, guarded.IsRevoked));
        Assert.Equal(Phase.Uncertain, files.Open().ReadStatus(Character).Phase);
        Assert.Equal(Decision.NeedsReconciliation,
            files.Open().TryBegin(Character, 11, order, out _));
    }

    [Fact]
    public void Crash_after_durable_admission_preserves_blocked_status_and_does_not_clear_on_manual_refresh()
    {
        using var files = new Files();
        var journal = files.Open();
        Assert.Equal(Decision.Allowed, journal.TryBegin(Character, 11, Order(), out _));
        var restarted = files.Open();
        var snapshot = restarted.ReadStatus(Character);
        Assert.Equal(Phase.Started, snapshot.Phase);
        Assert.Contains("BLOQUÉ", Vm.FormatProductionRecovery(snapshot));
        Assert.Contains("rapprochement", Vm.FormatProductionRecovery(snapshot),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Decision.NeedsReconciliation,
            restarted.TryBegin(Character, 12, Order(), out _));
        Assert.Equal(Phase.Started, files.Open().ReadStatus(Character).Phase);
    }

    [Fact]
    public void Completion_policy_rejects_unverified_listing_and_revoked_session()
    {
        var order = Order(saleFloor: 3);
        var complete = new Result(1, 0, 1, Stop.QuantityReached);
        Assert.True(Vm.CanCloseProductionAsCompleted(complete, order, true, false));
        Assert.False(Vm.CanCloseProductionAsCompleted(complete, order, true, true));
        Assert.False(Vm.CanCloseProductionAsCompleted(complete, order, false, false));
        Assert.False(Vm.CanCloseProductionAsCompleted(
            complete with { Listed = 0 }, order, true, false));
        Assert.False(Vm.CanCloseProductionAsCompleted(
            complete with { Stop = Stop.StockUnverified }, order, true, false));
    }

    private sealed class FakeActions : ActionPort, StockPort
    {
        public bool SupportsStockObservation => true;
        public readonly Dictionary<long, int> Bag = new();
        public int BagStock { set => Bag[10] = value; }
        public int BankStock { get; set; }
        public bool DoNotUpdateOnCraft { get; set; }
        public bool DoNotUpdateOnWithdraw { get; set; }
        public bool DoNotUpdateOnList { get; set; }
        public bool LoseObservationOnCraft { get; set; }
        public Action? AfterCraft { get; set; }
        public int Withdrawals { get; private set; }
        public int Crafts { get; private set; }
        public int Listings { get; private set; }

        public int BagCount(long gid) => Bag.GetValueOrDefault(gid);
        public bool TryReadBagCount(long gid, out int count)
        {
            count = 0;
            if (LoseObservationOnCraft && Crafts > 0) return false;
            count = Bag.GetValueOrDefault(gid);
            return true;
        }
        public int ChestCount(long gid) => gid == 10 ? BankStock : 0;
        public IReadOnlyList<MapInteractive> Interactives() => new[]
        {
            new MapInteractive(1, 17, 42, 0,
                new[] { new InteractiveSkill(19, 7, 0, true) }, "Atelier fictif")
        };
        public Task<bool> OpenChestAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<int> WithdrawAsync(long gid, int quantity, CancellationToken ct)
        {
            Withdrawals++;
            if (!DoNotUpdateOnWithdraw)
                Bag[gid] = Bag.GetValueOrDefault(gid) + quantity;
            BankStock -= quantity;
            return Task.FromResult(quantity);
        }
        public Task<bool> CloseChestAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<Receipt> CraftAsync(int elementId, int skillInstanceUid,
            IReadOnlyList<(long Gid, int Qty)> ingredients, int count, CancellationToken ct)
        {
            Crafts++;
            Assert.Equal(17, elementId);
            Assert.Equal(19, skillInstanceUid);
            Assert.Equal(1, count);
            if (!DoNotUpdateOnCraft)
            {
                Bag[10] = Bag.GetValueOrDefault(10) - 2;
                Bag[900] = Bag.GetValueOrDefault(900) + 1;
            }
            AfterCraft?.Invoke();
            return Task.FromResult(new Receipt(true, 1, 900));
        }
        public Task<long> LotPriceAsync(long gid, int quantity, CancellationToken ct) =>
            Task.FromResult(0L);
        public Task<bool> BuyAsync(long gid, int quantity, long maxPrice, CancellationToken ct) =>
            Task.FromResult(false);
        public Task<bool> ListAsync(long gid, int quantity, long price, CancellationToken ct)
        {
            Listings++;
            if (!DoNotUpdateOnList)
                Bag[gid] = Bag.GetValueOrDefault(gid) - quantity;
            return Task.FromResult(true);
        }
    }
}
