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
using ActionPort = F06App::GivreClone.App.Services.ItemCraftPlanner.IItemCraftProductionActions;
using Runner = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionRun;
using Stop = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionStop;
using Input = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionInput;
using Recipe = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionRecipe;
using Request = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionRequest;
using Receipt = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftWorkshopReceipt;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// V7: once the production session guard has observed a lost identity, no
/// later return to a matching snapshot can revive the same action adapter.
/// The fake ports and item IDs are local fixtures, not invented server ACKs.
/// </summary>
public sealed class ItemCraftProductionDurabilityV7Tests
{
    private const string Profile = "server:292:TestV7";
    private static Request Order() => new(
        new Recipe(900, 1, 7, 0, new[] { new Input(10, 2, false) }),
        Quantity: 1, MaxKamas: 0, BankAllowed: true, SaleFloor: null);

    private sealed class JournalFiles : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(),
            "givre-craft-v7-" + Guid.NewGuid().ToString("N"));
        public string FilePath => Path.Combine(_folder, "attempts.journal");
        public Journal Open() => new(FilePath);
        public void Dispose()
        {
            try { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task Withdrawal_detects_loss_then_revocation_survives_return_to_matching_session()
    {
        using var files = new JournalFiles();
        var journal = files.Open();
        Assert.Equal(Decision.Allowed, journal.TryBegin(Profile, 101, Order(), out var lease));

        bool isCurrent = true;
        var port = new FakePort { Withdraw = (_, _) =>
        {
            isCurrent = false; // session lost while an old bank operation was in flight
            return Task.FromResult(2);
        } };
        var guarded = new BoundActions(port, () => isCurrent);
        Assert.Equal(0, await guarded.WithdrawAsync(10, 2, CancellationToken.None));
        Assert.Equal(1, port.WithdrawCalls);

        isCurrent = true; // a transient matching snapshot cannot revive this lease
        Assert.Equal(0, guarded.BagCount(10));
        Assert.False(await guarded.OpenChestAsync(CancellationToken.None));
        Assert.False(await guarded.BuyAsync(10, 2, 50, CancellationToken.None));
        Assert.False(await guarded.ListAsync(900, 1, 50, CancellationToken.None));
        Assert.Equal(new Receipt(false, 0, 0),
            await guarded.CraftAsync(1, 2, new[] { (10L, 2) }, 1, CancellationToken.None));
        Assert.Equal(0, port.BuyCalls);
        Assert.Equal(0, port.ListCalls);
        Assert.Equal(0, port.CraftCalls);
        Assert.Equal(0, port.OpenCalls);

        // Production was not certified: durable state must remain quarantined
        // through process restart. Neither stock count nor the late withdrawal
        // receipt is evidence to clear the old attempt.
        Assert.True(journal.TryFinish(lease, fullyConfirmed: false));
        Assert.Equal(Phase.Uncertain, files.Open().ReadStatus(Profile).Phase);
        Assert.Equal(Decision.NeedsReconciliation,
            files.Open().TryBegin(Profile, 101, Order(), out _));
    }

    [Fact]
    public async Task Late_positive_craft_is_denied_then_future_operations_stay_denied()
    {
        bool isCurrent = true;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<Receipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        var port = new FakePort { Craft = (_, _) =>
        {
            started.TrySetResult();
            return finish.Task; // intentionally ignores cancellation/session changes
        } };
        var guarded = new BoundActions(port, () => isCurrent);
        Task<Receipt> pending = guarded.CraftAsync(1, 2, new[] { (10L, 2) }, 1, CancellationToken.None);
        await started.Task;
        isCurrent = false;
        finish.SetResult(new Receipt(true, 1, 900));
        Receipt result = await pending;
        Assert.Equal(new Receipt(false, 0, 0), result);
        Assert.Equal(1, port.CraftCalls);

        isCurrent = true;
        Assert.Equal(0, await guarded.LotPriceAsync(10, 1, CancellationToken.None));
        Assert.False(await guarded.ListAsync(900, 1, 50, CancellationToken.None));
        Assert.Equal(0, port.PriceCalls);
        Assert.Equal(0, port.ListCalls);
    }

    [Fact]
    public async Task Read_that_observes_lost_identity_cannot_seed_stock_for_a_later_action()
    {
        bool isCurrent = true;
        var port = new FakePort { ReadBag = _ =>
        {
            isCurrent = false;
            return 5;
        } };
        var guarded = new BoundActions(port, () => isCurrent);
        Assert.Equal(0, guarded.BagCount(10));
        Assert.Equal(1, port.BagReads);

        isCurrent = true;
        Assert.Equal(0, guarded.ChestCount(10));
        Assert.Empty(guarded.Interactives());
        Assert.False(await guarded.BuyAsync(10, 1, 100, CancellationToken.None));
        Assert.Equal(0, port.BuyCalls);
        Assert.Equal(0, port.ChestReads);
        Assert.Equal(0, port.InteractiveReads);
    }

    [Fact]
    public async Task Real_runner_stops_after_an_ambiguous_bank_receipt_without_dispatching_craft()
    {
        bool isCurrent = true;
        var port = new FakePort
        {
            WorkshopAvailable = true,
            ReadBag = _ => 0,
            Withdraw = (_, _) =>
            {
                isCurrent = false;
                return Task.FromResult(2);
            }
        };
        var guarded = new BoundActions(port, () => isCurrent);
        var result = await new Runner().RunAsync(Order(), guarded, null, CancellationToken.None);
        Assert.Equal(Stop.MissingInputs, result.Stop);
        Assert.Equal(0, result.Produced);
        Assert.Equal(0, port.CraftCalls);
        Assert.Equal(0, port.BuyCalls);

        isCurrent = true; // cannot revive this real runner's retired adapter
        Assert.Equal(new Receipt(false, 0, 0),
            await guarded.CraftAsync(17, 19, new[] { (10L, 2) }, 1, CancellationToken.None));
        Assert.Equal(0, port.CraftCalls);
    }

    [Fact]
    public async Task Stable_identity_keeps_existing_positive_receipt_contract()
    {
        var port = new FakePort();
        var guarded = new BoundActions(port, () => true);
        Assert.Equal(3, guarded.BagCount(10));
        Assert.Equal(4, guarded.ChestCount(10));
        Assert.True(await guarded.OpenChestAsync(CancellationToken.None));
        Assert.Equal(2, await guarded.WithdrawAsync(10, 2, CancellationToken.None));
        Assert.True(await guarded.CloseChestAsync(CancellationToken.None));
        Assert.Equal(new Receipt(true, 1, 900),
            await guarded.CraftAsync(1, 2, new[] { (10L, 2) }, 1, CancellationToken.None));
        Assert.True(await guarded.BuyAsync(10, 1, 50, CancellationToken.None));
        Assert.True(await guarded.ListAsync(900, 1, 50, CancellationToken.None));
        Assert.Equal(1, port.CraftCalls);
        Assert.Equal(1, port.BuyCalls);
        Assert.Equal(1, port.ListCalls);
    }

    [Fact]
    public async Task Cancelled_inflight_craft_cannot_publish_late_positive_receipt()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<Receipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        var port = new FakePort { Craft = (_, _) =>
        {
            started.TrySetResult();
            return finish.Task;
        } };
        var guarded = new BoundActions(port, () => true);
        using var cancellation = new CancellationTokenSource();
        Task<Receipt> pending = guarded.CraftAsync(1, 2, new[] { (10L, 2) }, 1, cancellation.Token);
        await started.Task;
        cancellation.Cancel();
        finish.SetResult(new Receipt(true, 1, 900));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, port.CraftCalls);
    }

    [Fact]
    public void Crash_restart_with_started_lease_is_not_a_success_receipt()
    {
        using var files = new JournalFiles();
        var live = files.Open();
        Assert.Equal(Decision.Allowed, live.TryBegin(Profile, 101, Order(), out var lease));
        // Simulated process exit: the in-memory lease does not survive, but its
        // flushed Started record and anchor do.
        var restarted = files.Open();
        Assert.Equal(Phase.Started, restarted.ReadStatus(Profile).Phase);
        Assert.False(restarted.TryFinish(lease, fullyConfirmed: true));
        Assert.Equal(Decision.NeedsReconciliation,
            restarted.TryBegin(Profile, 101, Order(), out _));
    }

    private sealed class FakePort : ActionPort
    {
        public Func<long, int>? ReadBag;
        public Func<long, int, Task<int>>? Withdraw;
        public Func<IReadOnlyList<(long Gid, int Qty)>, CancellationToken, Task<Receipt>>? Craft;
        public int BagReads;
        public int ChestReads;
        public int InteractiveReads;
        public int WithdrawCalls;
        public int CraftCalls;
        public int BuyCalls;
        public int ListCalls;
        public int PriceCalls;
        public int OpenCalls;
        public bool WorkshopAvailable;

        public int BagCount(long gid) { BagReads++; return ReadBag?.Invoke(gid) ?? 3; }
        public int ChestCount(long gid) { ChestReads++; return 4; }
        public IReadOnlyList<MapInteractive> Interactives()
        {
            InteractiveReads++;
            return WorkshopAvailable
                ? new[] { new MapInteractive(1, 17, 42, 0,
                    new[] { new InteractiveSkill(19, 7, 0, true) }, "Atelier fictif") }
                : Array.Empty<MapInteractive>();
        }
        public Task<bool> OpenChestAsync(CancellationToken ct)
        { OpenCalls++; return Task.FromResult(true); }
        public Task<int> WithdrawAsync(long gid, int quantity, CancellationToken ct)
        { WithdrawCalls++; return Withdraw?.Invoke(gid, quantity) ?? Task.FromResult(quantity); }
        public Task<bool> CloseChestAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<Receipt> CraftAsync(int elementId, int skillInstanceUid,
            IReadOnlyList<(long Gid, int Qty)> ingredients, int count, CancellationToken ct)
        {
            CraftCalls++;
            return Craft?.Invoke(ingredients, ct) ?? Task.FromResult(new Receipt(true, 1, 900));
        }
        public Task<long> LotPriceAsync(long gid, int quantity, CancellationToken ct)
        { PriceCalls++; return Task.FromResult(50L); }
        public Task<bool> BuyAsync(long gid, int quantity, long maxPrice, CancellationToken ct)
        { BuyCalls++; return Task.FromResult(true); }
        public Task<bool> ListAsync(long gid, int quantity, long price, CancellationToken ct)
        { ListCalls++; return Task.FromResult(true); }
    }
}
