extern alias F06App;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;
using BoundActions = F06App::GivreClone.App.Services.Production.ItemCraftSessionBoundActions;
using Journal = F06App::GivreClone.App.Services.Production.ItemCraftProductionAttemptJournal;
using Decision = F06App::GivreClone.App.Services.Production.ItemCraftAttemptDecision;
using Phase = F06App::GivreClone.App.Services.Production.ItemCraftAttemptPhase;
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
/// V9: tests of the actual production admission and runner pipeline, without
/// contacting a client. A local inventory model corroborates a receipt, but
/// is not interpreted as an invented server ACK.
/// </summary>
public sealed class ItemCraftProductionJourneyV9Tests
{
    private const string Profile = "server:292:JourneyV9";

    private sealed class Sandbox : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(),
            "givre-itemcraft-v9-" + Guid.NewGuid().ToString("N"));
        public string JournalPath => Path.Combine(_folder, "production", "itemcraft.journal");
        public Journal Open() => new(JournalPath);
        public void Dispose()
        {
            try { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static Request Order(bool bank = false, long? saleFloor = null, long? cost = 0) =>
        new(new Recipe(900, 1, 7, cost,
            new[] { new Input(10, 2, false) }),
            Quantity: 1, MaxKamas: 0, BankAllowed: bank, SaleFloor: saleFloor);

    private static bool Finish(Journal journal,
        F06App::GivreClone.App.Services.Production.ItemCraftAttemptLease lease,
        Request order, Result result, BoundActions bound, bool sessionCurrent = true) =>
        journal.TryFinish(lease,
            Vm.CanCloseProductionAsCompleted(result, order, sessionCurrent, bound.IsRevoked));

    [Fact]
    public void Preflight_rejects_missing_workshop_without_starting_an_unrecoverable_attempt()
    {
        using var files = new Sandbox();
        var source = new FakeActions { WorkshopPresent = false, BagStock = 2 };
        var bound = new BoundActions(source, () => true);
        var runner = new Runner();

        Assert.Equal(Stop.WorkshopMissing, runner.Preflight(Order(), bound));
        Assert.Equal(Phase.None, files.Open().ReadStatus(Profile).Phase);
        Assert.False(File.Exists(files.JournalPath));
        Assert.Equal(0, source.Crafts);
        Assert.Equal(0, source.Withdrawals);
        Assert.Equal(0, source.Listings);

        source.WorkshopPresent = true;
        Assert.Null(runner.Preflight(Order(), bound));
        Assert.Equal(Phase.None, files.Open().ReadStatus(Profile).Phase);
    }

    [Fact]
    public void Invalid_recipe_or_unavailable_stock_is_rejected_before_durable_admission()
    {
        using var files = new Sandbox();
        var source = new FakeActions { BagStock = 2 };
        var bound = new BoundActions(source, () => true);
        var runner = new Runner();
        Assert.Equal(Stop.UnknownCost, runner.Preflight(Order(cost: null), bound));
        source.ObservationEnabled = false;
        Assert.Equal(Stop.StockUnverified, runner.Preflight(Order(), bound));
        Assert.Equal(Phase.None, files.Open().ReadStatus(Profile).Phase);
        Assert.False(File.Exists(files.JournalPath));
        Assert.Equal(0, source.Crafts);
    }

    [Fact]
    public void Missing_unpurchasable_first_craft_inputs_do_not_poison_durable_history()
    {
        using var files = new Sandbox();
        var source = new FakeActions { BagStock = 0 };
        var bound = new BoundActions(source, () => true);
        Assert.Equal(Stop.MissingInputs, new Runner().Preflight(Order(), bound));
        Assert.Equal(Phase.None, files.Open().ReadStatus(Profile).Phase);
        Assert.False(File.Exists(files.JournalPath));
        Assert.Equal(0, source.Withdrawals);
        Assert.Equal(0, source.Crafts);
    }

    [Fact]
    public async Task Real_bank_craft_and_listing_allow_delayed_model_updates_but_never_resend()
    {
        using var files = new Sandbox();
        var order = Order(bank: true, saleFloor: 9);
        var source = new FakeActions
        {
            BankStock = 2,
            WithdrawObservationPolls = 3,
            ResultObservationPolls = 4
        };
        var bound = new BoundActions(source, () => true);
        var runner = new Runner();
        Assert.Null(runner.Preflight(order, bound));
        var journal = files.Open();
        Assert.Equal(Decision.Allowed, journal.TryBegin(Profile, 41, order, out var lease));

        Result result = await runner.RunAsync(order, bound, null, CancellationToken.None);
        Assert.Equal(Stop.QuantityReached, result.Stop);
        Assert.Equal(1, result.Produced);
        Assert.Equal(1, result.Listed);
        Assert.Equal(0, result.Spent);
        Assert.Equal(1, source.Withdrawals);
        Assert.Equal(1, source.Crafts);
        Assert.Equal(1, source.Listings);
        Assert.True(source.ReadsWaitingOnWithdraw >= 3);
        Assert.True(source.ReadsWaitingOnResult >= 4);
        Assert.True(Finish(journal, lease, order, result, bound));
        Assert.Equal(Phase.Completed, files.Open().ReadStatus(Profile).Phase);
        Assert.Equal(Decision.Allowed, files.Open().TryBegin(Profile, 42, order, out _));
    }

    [Fact]
    public async Task Target_produced_but_ingredients_not_consumed_requires_manual_reconciliation()
    {
        using var files = new Sandbox();
        var order = Order(saleFloor: 8);
        var source = new FakeActions { BagStock = 2, SkipIngredientDebit = true };
        var bound = new BoundActions(source, () => true);
        var journal = files.Open();
        Assert.Equal(Decision.Allowed, journal.TryBegin(Profile, 43, order, out var lease));

        Result result = await new Runner().RunAsync(order, bound, null, CancellationToken.None);
        Assert.Equal(Stop.StockUnverified, result.Stop);
        Assert.Equal(0, result.Produced);
        Assert.Equal(1, source.Crafts);
        Assert.Equal(0, source.Listings);
        Assert.True(Finish(journal, lease, order, result, bound));
        Assert.Equal(Phase.Uncertain, files.Open().ReadStatus(Profile).Phase);
        Assert.Equal(Decision.NeedsReconciliation,
            files.Open().TryBegin(Profile, 44, order, out _));
        Assert.Contains("BLOQUÉ", Vm.FormatProductionRecovery(files.Open().ReadStatus(Profile)));
    }

    [Fact]
    public async Task More_items_than_a_receipt_claims_must_not_be_normalized_to_success()
    {
        using var files = new Sandbox();
        var order = Order();
        var source = new FakeActions { BagStock = 2, ExtraOutput = true };
        var bound = new BoundActions(source, () => true);
        var journal = files.Open();
        Assert.Equal(Decision.Allowed, journal.TryBegin(Profile, 43, order, out var lease));
        Result result = await new Runner().RunAsync(order, bound, null, CancellationToken.None);
        Assert.Equal(Stop.StockUnverified, result.Stop);
        Assert.Equal(0, result.Produced);
        Assert.True(Finish(journal, lease, order, result, bound));
        Assert.Equal(Phase.Uncertain, files.Open().ReadStatus(Profile).Phase);
    }

    [Fact]
    public async Task A_preflight_that_was_true_must_be_revalidated_after_durable_admission()
    {
        using var files = new Sandbox();
        var source = new FakeActions { BagStock = 2 };
        var order = Order();
        var bound = new BoundActions(source, () => true);
        var runner = new Runner();
        Assert.Null(runner.Preflight(order, bound));
        var journal = files.Open();
        Assert.Equal(Decision.Allowed, journal.TryBegin(Profile, 43, order, out var lease));

        // The workshop disappeared after the journal was written, before any
        // workshop request. The runner must check again, not trust preflight.
        source.WorkshopPresent = false;
        Result result = await runner.RunAsync(order, bound, null, CancellationToken.None);
        Assert.Equal(Stop.WorkshopMissing, result.Stop);
        Assert.Equal(0, source.Crafts);
        Assert.True(Finish(journal, lease, order, result, bound));
        Assert.Equal(Phase.Uncertain, files.Open().ReadStatus(Profile).Phase);
    }

    [Fact]
    public async Task Late_success_after_Stop_never_completes_or_retries_craft()
    {
        using var files = new Sandbox();
        var order = Order();
        var journal = files.Open();
        var source = new FakeActions { BagStock = 2 };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource<Receipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.CustomCraft = () =>
        {
            entered.TrySetResult();
            return released.Task; // source deliberately ignores Stop/cancellation
        };
        var bound = new BoundActions(source, () => true);
        var runner = new Runner();
        Assert.Null(runner.Preflight(order, bound));
        Assert.Equal(Decision.Allowed, journal.TryBegin(Profile, 43, order, out var lease));
        Task<Result> pending = runner.RunAsync(order, bound, null, CancellationToken.None);
        await entered.Task;
        runner.Stop();
        released.SetResult(new Receipt(true, 1, 900));
        Result result = await pending;

        Assert.Equal(Stop.Stopped, result.Stop);
        Assert.Equal(0, result.Produced);
        Assert.Equal(1, source.Crafts);
        Assert.Equal(0, source.Listings);
        Assert.True(Finish(journal, lease, order, result, bound));
        Assert.Equal(Phase.Uncertain, files.Open().ReadStatus(Profile).Phase);
        Assert.Equal(Decision.NeedsReconciliation,
            files.Open().TryBegin(Profile, 45, order, out _));
    }

    [Fact]
    public async Task Session_revocation_during_craft_cannot_be_undone_by_matching_identity_later()
    {
        using var files = new Sandbox();
        var order = Order();
        var journal = files.Open();
        bool current = true;
        var source = new FakeActions
        {
            BagStock = 2,
            AfterCraft = () => current = false
        };
        var bound = new BoundActions(source, () => current);
        var runner = new Runner();
        Assert.Null(runner.Preflight(order, bound));
        Assert.Equal(Decision.Allowed, journal.TryBegin(Profile, 43, order, out var lease));

        Result result = await runner.RunAsync(order, bound, null, CancellationToken.None);
        Assert.NotEqual(Stop.QuantityReached, result.Stop);
        Assert.True(bound.IsRevoked);
        current = true;
        Assert.False(Vm.CanCloseProductionAsCompleted(result, order, true, bound.IsRevoked));
        Assert.True(Finish(journal, lease, order, result, bound));
        Assert.Equal(Phase.Uncertain, files.Open().ReadStatus(Profile).Phase);
        Assert.Equal(Decision.NeedsReconciliation,
            files.Open().TryBegin(Profile, 46, order, out _));
    }

    private sealed class FakeActions : ActionPort, StockPort
    {
        private readonly Dictionary<long, int> _bag = new();
        private int _withdrawPending;
        private int _resultPending;
        public bool SupportsStockObservation => true;
        public bool ObservationEnabled = true;
        public bool WorkshopPresent = true;
        public int BankStock;
        public int WithdrawObservationPolls;
        public int ResultObservationPolls;
        public int ReadsWaitingOnWithdraw;
        public int ReadsWaitingOnResult;
        public int Withdrawals;
        public int Crafts;
        public int Listings;
        public bool SkipIngredientDebit;
        public bool ExtraOutput;
        public Action? AfterCraft;
        public Func<Task<Receipt>>? CustomCraft;
        public int BagStock { set => _bag[10] = value; }

        public int BagCount(long gid) => _bag.GetValueOrDefault(gid);
        public bool TryReadBagCount(long gid, out int count)
        {
            count = 0;
            if (!ObservationEnabled) return false;
            if (gid == 10 && _withdrawPending > 0)
            {
                if (++ReadsWaitingOnWithdraw >= WithdrawObservationPolls)
                {
                    _bag[10] = _bag.GetValueOrDefault(10) + _withdrawPending;
                    _withdrawPending = 0;
                }
            }
            if (gid == 900 && _resultPending > 0)
            {
                if (++ReadsWaitingOnResult >= ResultObservationPolls)
                {
                    _bag[900] = _bag.GetValueOrDefault(900) + _resultPending;
                    _resultPending = 0;
                }
            }
            count = BagCount(gid);
            return true;
        }
        public int ChestCount(long gid) => gid == 10 ? BankStock : 0;
        public IReadOnlyList<MapInteractive> Interactives() =>
            WorkshopPresent
                ? new[] { new MapInteractive(1, 17, 42, 0,
                    new[] { new InteractiveSkill(19, 7, 0, true) }, "Atelier fictif") }
                : Array.Empty<MapInteractive>();
        public Task<bool> OpenChestAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<int> WithdrawAsync(long gid, int quantity, CancellationToken ct)
        {
            Withdrawals++;
            BankStock -= quantity;
            if (WithdrawObservationPolls > 0) _withdrawPending = quantity;
            else _bag[gid] = BagCount(gid) + quantity;
            return Task.FromResult(quantity);
        }
        public Task<bool> CloseChestAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<Receipt> CraftAsync(int elementId, int skillInstanceUid,
            IReadOnlyList<(long Gid, int Qty)> ingredients, int count, CancellationToken ct)
        {
            Crafts++;
            Assert.Equal(17, elementId);
            Assert.Equal(19, skillInstanceUid);
            Assert.Equal((10L, 2), Assert.Single(ingredients));
            Assert.Equal(1, count);
            if (CustomCraft is not null) return CustomCraft();
            if (!SkipIngredientDebit) _bag[10] = BagCount(10) - 2;
            int produced = ExtraOutput ? 2 : 1;
            if (ResultObservationPolls > 0) _resultPending = produced;
            else _bag[900] = BagCount(900) + produced;
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
            _bag[gid] = BagCount(gid) - quantity;
            return Task.FromResult(true);
        }
    }
}
