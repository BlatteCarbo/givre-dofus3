extern alias F06App;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;
using Xunit;
using Xunit.Abstractions;
using Goal = F06App::GivreClone.App.Services.Production.GuidedActivityProductionGoal;
using Workflow = F06App::GivreClone.App.Services.Production.GuidedProductionWorkflowLedger;
using WorkflowSnapshot = F06App::GivreClone.App.Services.Production.GuidedProductionWorkflowSnapshot;
using WorkflowPhase = F06App::GivreClone.App.Services.Production.GuidedProductionWorkflowPhase;
using Planner = F06App::GivreClone.App.Services.Production.GuidedSupplyPlannerV13;
using Continuation = F06App::GivreClone.App.Services.Production.GuidedSupplyContinuationPlannerV15;
using Step = F06App::GivreClone.App.Services.Production.GuidedSupplyNextStepV15;
using SupplySource = F06App::GivreClone.App.Services.Production.GuidedSupplySourceObservation;
using SupplyActions = F06App::GivreClone.App.Services.Production.GuidedProductionReadOnlyActions;
using RecoveryStore = F06App::GivreClone.App.Services.ActivityRecoveryDurableStore;
using RecoveryStatus = F06App::GivreClone.App.Services.ActivityRecoveryDurableStatus;
using RecoveryScope = F06App::GivreClone.App.Services.ActivityRecoveryScope;
using RecoverySegment = F06App::GivreClone.App.Services.ActivityRecoverySegment;
using RecoveryKind = F06App::GivreClone.App.Services.ActivityRecoveryKind;
using RecoverySnapshot = F06App::GivreClone.App.Services.ActivityRecoverySnapshot;
using RecoveryState = F06App::GivreClone.App.Services.ActivityRecoveryDurableState;
using Attempt = F06App::GivreClone.App.Services.Production.ItemCraftAttemptSnapshot;
using AttemptPhase = F06App::GivreClone.App.Services.Production.ItemCraftAttemptPhase;
using Recipe = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionRecipe;
using Ingredient = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionInput;
using Request = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionRequest;
using Receipt = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftWorkshopReceipt;
using Actions = F06App::GivreClone.App.Services.ItemCraftPlanner.IItemCraftProductionActions;
using ObservedStock = F06App::GivreClone.App.Services.ItemCraftPlanner.IItemCraftProductionStockObservation;
using Atelier = F06App::GivreClone.App.ViewModels.ItemCraftPlannerViewModel;

namespace GivreClone.Tests;

/// <summary>
/// V15 real V13 checkpoint -> post-activity observation -> existing runner
/// handoff; never a transport acknowledgement or automatic gameplay action.
/// </summary>
public sealed class GuidedSupplyContinuationUiV15Tests
{
    private readonly ITestOutputHelper _output;
    public GuidedSupplyContinuationUiV15Tests(ITestOutputHelper output) => _output = output;

    private static readonly Goal Target = new(
        292, 991001, 191105026, 7, 320, 1934, 3, 95, true);
    private static readonly RecoveryScope Identity = new(292, 991001);
    private static readonly Request CraftOrder = new(
        new Recipe(1934, 1, 12, 0,
            [new Ingredient(10, 2, false), new Ingredient(11, 1, true)]),
        3, 10000, true, null);
    private static readonly Attempt NoAttempt = new(AttemptPhase.None, "no real attempt");
    private static readonly RecoveryStatus Clear = new(
        Identity, RecoveryState.Clear, null, 0, 0, "no marker");

    private sealed class Temp : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "givre-v15-continuation-" + Guid.NewGuid().ToString("N"));
        public Workflow Ledger => new(Path.Combine(Root, "workflow"));
        public RecoveryStore Recovery => new(Path.Combine(Root, "recovery"));
        public void Dispose()
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class Local : Actions, ObservedStock
    {
        public readonly Dictionary<long, int> Bag = new() { [10] = 0, [11] = 0 };
        public readonly Dictionary<long, int> Chest = new() { [10] = 0, [11] = 2 };
        public int Sends;
        public bool SupportsStockObservation => true;
        public bool TryReadBagCount(long gid, out int count) => Bag.TryGetValue(gid, out count);
        public int BagCount(long gid) => Bag.GetValueOrDefault(gid);
        public int ChestCount(long gid) => Chest.GetValueOrDefault(gid);
        public IReadOnlyList<MapInteractive> Interactives() => Array.Empty<MapInteractive>();
        public Task<bool> OpenChestAsync(CancellationToken ct) { Sends++; throw new Exception("NO SEND"); }
        public Task<int> WithdrawAsync(long gid, int qty, CancellationToken ct) { Sends++; throw new Exception("NO SEND"); }
        public Task<bool> CloseChestAsync(CancellationToken ct) { Sends++; throw new Exception("NO SEND"); }
        public Task<Receipt> CraftAsync(int elementId, int skillUid,
            IReadOnlyList<(long Gid, int Qty)> ingredients, int count, CancellationToken ct)
        { Sends++; throw new Exception("NO SEND"); }
        public Task<long> LotPriceAsync(long gid, int qty, CancellationToken ct)
        { Sends++; throw new Exception("NO SEND"); }
        public Task<bool> BuyAsync(long gid, int qty, long maxPrice, CancellationToken ct)
        { Sends++; throw new Exception("NO SEND"); }
        public Task<bool> ListAsync(long gid, int qty, long price, CancellationToken ct)
        { Sends++; throw new Exception("NO SEND"); }
    }

    private static SupplySource Source(long revision, string link = "verified-host-link-offline") =>
        new(292, 991001, 191105026, 7, link, revision, 10, 100);

    [Fact]
    public void Actual_V13_checkpoint_transitions_gather_then_bank_then_existing_V9_runner_without_send()
    {
        using var temp = new Temp();
        var data = new Local();
        var ledger = temp.Ledger;
        long revision = 10;
        SupplyActions Safe() => new(data, () => true, () => Source(revision));
        var first = Planner.PrepareAndCheckpoint(Target, CraftOrder,
            Safe(), Clear, NoAttempt, ledger, explicitPreviewCheckpoint: true);
        Assert.True(first.CheckpointWritten);
        Assert.False(first.MayOfferExistingRunner);
        var gather = Continuation.Inspect(Target, ledger.Read(292, 991001),
            Clear, NoAttempt, first, sameObservedOwner: true);
        Assert.Equal(Step.PrepareExactGatherRoute, gather.NextStep);
        Assert.Equal(10, gather.Gid);
        Assert.Equal(6, gather.Quantity);
        Assert.False(gather.MayAuthorizeGameSend);

        // After a separately proven activity reconciliation, source revision
        // changes. The new inventory count is NOT proof of that reconciliation.
        data.Bag[10] = 6;
        revision = 11;
        var second = Planner.PrepareAndCheckpoint(Target, CraftOrder,
            Safe(), Clear, NoAttempt, ledger, explicitPreviewCheckpoint: true);
        Assert.True(second.CheckpointWritten);
        var bank = Continuation.Inspect(Target, ledger.Read(292, 991001),
            Clear, NoAttempt, second, sameObservedOwner: true);
        Assert.Equal(Step.InspectBankAndPods, bank.NextStep);
        Assert.False(bank.MayAuthorizeGameSend);

        // Manual bank observation after a new revision; never run Withdraw on
        // this read-only port. The existing V9 Lancer is the only runner.
        data.Bag[11] = 3;
        revision = 12;
        var third = Planner.PrepareAndCheckpoint(Target, CraftOrder,
            Safe(), Clear, NoAttempt, ledger, explicitPreviewCheckpoint: true);
        Assert.True(third.CheckpointWritten);
        Assert.True(third.MayOfferExistingRunner);
        var ready = Continuation.Inspect(Target, ledger.Read(292, 991001),
            Clear, NoAttempt, third, sameObservedOwner: true);
        Assert.Equal(Step.UseExistingProductionRunner, ready.NextStep);
        Assert.Equal(Target.ResultGid, ready.Gid);
        Assert.False(ready.MayAuthorizeGameSend);
        Assert.False(ready.HasIndividuallyAttributedServerAck);
        Assert.Equal(0, data.Sends);
    }

    [Fact]
    public void Actual_durable_gather_marker_survives_restart_and_local_stock_growth_never_releases_it()
    {
        using var temp = new Temp();
        var store = temp.Recovery;
        var segment = new RecoverySegment(RecoveryKind.Gather, 2416,
            Cell: 241, ElementId: 2416);
        var snapshot = new RecoverySnapshot(
            new ResumableRouteMap(191105026, 7), 191105026,
            191105026, 240, PositionConfirmed: true, Interrupted: false,
            Suspended: false, ResourcePresent: true,
            ResourceAvailable: true, AlreadyInReach: true,
            StockGid: 10, StockCount: 0);
        Assert.True(store.TryReserve(Identity, segment, snapshot).MayDispatch);
        var restarted = temp.Recovery;
        var status = restarted.ReadStatus(Identity);
        Assert.Equal(RecoveryState.Pending, status.State);
        var workflow = new WorkflowSnapshot(WorkflowPhase.Blocked,
            Workflow.Fingerprint(Target), new string('A', 64), Guid.Empty, "await");
        var result = Continuation.Inspect(Target, workflow, status,
            NoAttempt, null, sameObservedOwner: true);
        Assert.Equal(Step.InspectActivityRecovery, result.NextStep);
        Assert.False(result.MayClearPendingRecovery);
        Assert.False(result.MayAuthorizeGameSend);
        Assert.False(restarted.TryReserve(Identity, segment, snapshot).MayDispatch);
        Assert.Equal(RecoveryState.Pending, restarted.ReadStatus(Identity).State);
    }

    [Fact]
    public void Stale_checkpoint_or_changed_link_requires_new_stock_observation_no_replay()
    {
        using var temp = new Temp();
        var data = new Local();
        long revision = 10;
        string token = "source-A";
        SupplyActions Safe() => new(data, () => true, () => Source(revision, token));
        var first = Planner.PrepareAndCheckpoint(Target, CraftOrder,
            Safe(), Clear, NoAttempt, temp.Ledger, explicitPreviewCheckpoint: true);
        Assert.True(first.CheckpointWritten);
        token = "source-B";
        var reconnected = Planner.Evaluate(Target, CraftOrder, Safe(),
            Clear, NoAttempt, temp.Ledger.Read(292, 991001));
        Assert.False(reconnected.EvidenceValid);
        var decision = Continuation.Inspect(Target, temp.Ledger.Read(292, 991001),
            Clear, NoAttempt, reconnected, sameObservedOwner: true);
        Assert.Equal(Step.RefreshObservedStock, decision.NextStep);
        Assert.False(decision.MayAuthorizeGameSend);
        Assert.Equal(Step.Blocked, Continuation.Inspect(Target,
            temp.Ledger.Read(292, 991001), Clear, NoAttempt, first,
            sameObservedOwner: false).NextStep);
        Assert.Equal(0, data.Sends);
    }

    [Fact]
    public void Started_uncertain_and_exact_completed_V9_runs_keep_distinct_next_steps()
    {
        var run = Guid.NewGuid();
        const string hash = "A123";
        var workflow = new WorkflowSnapshot(WorkflowPhase.InProgress,
            Workflow.Fingerprint(Target), hash, run, "started");
        var started = Continuation.Inspect(Target, workflow, Clear,
            new Attempt(AttemptPhase.Started, "sent", hash, run), null, true);
        Assert.Equal(Step.InspectProductionAttempt, started.NextStep);
        Assert.False(started.MayAuthorizeGameSend);
        var reconciled = workflow with { Phase = WorkflowPhase.Reconciled };
        Assert.Equal(Step.InspectProductionAttempt,
            Continuation.Inspect(Target, reconciled, Clear,
                new Attempt(AttemptPhase.Completed, "different", hash, Guid.NewGuid()),
                null, true).NextStep);
        var exact = Continuation.Inspect(Target, reconciled, Clear,
            new Attempt(AttemptPhase.Completed, "V9 verified offline", hash, run),
            null, true);
        Assert.Equal(Step.InspectCompletedHdvListing, exact.NextStep);
        Assert.False(exact.HasIndividuallyAttributedServerAck);
    }

    [Fact]
    public void Classic_atelier_commands_reach_real_reinspection_guard_without_selected_owner()
    {
        var vm = new Atelier();
        vm.InspectGuidedSupplyContinuationCommand.Execute(null);
        Assert.Contains("Blocked", vm.GuidedSupplyContinuationStatus);
        vm.PrepareGuidedSupplyNextStepCommand.Execute(null);
        Assert.Contains("aucun", vm.GuidedSupplyContinuationStatus,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Desktop_pane_binds_readonly_inspection_and_next_step_to_existing_runner()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
            !Directory.Exists(Path.Combine(directory.FullName, "GivreClone.App")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        string panel = File.ReadAllText(Path.Combine(directory!.FullName,
            "GivreClone.App", "Views", "ItemCraftPlannerPanel.axaml"));
        string vm = File.ReadAllText(Path.Combine(directory.FullName,
            "GivreClone.App", "ViewModels", "ItemCraftPlannerViewModel.GuidedActivity.cs"));
        Assert.Contains("InspectGuidedSupplyContinuationCommand", panel);
        Assert.Contains("PrepareGuidedSupplyNextStepCommand", panel);
        Assert.Contains("RunProductionCommand", panel);
        Assert.Contains("PrepareGuidedSupplyRoute();", vm);
        Assert.Contains("InspectGuidedBankSupply();", vm);
        Assert.Contains("InspectGuidedHdvListing();", vm);
        Assert.Contains("InspectGuidedStockPreflight();", vm);
        string main = File.ReadAllText(Path.Combine(directory.FullName,
            "GivreClone.App", "Views", "MainWindow.axaml.cs"));
        Assert.Contains("GuidedSupplyRecoveryInspectionRequested +=", main);
        Assert.Contains("InspectActivityRecoveryCommand.Execute(null)", main);
        Assert.Contains("ExecuteQuickNavigation(\"lua\")", main);
    }

    [Fact]
    public void Readonly_next_step_latency_and_allocations_measured_without_live_claim()
    {
        var state = new WorkflowSnapshot(WorkflowPhase.Uncertain, "", "",
            Guid.Empty, "damaged anchor");
        long before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 128; ++i)
        {
            var result = Continuation.Inspect(Target, state, Clear,
                NoAttempt, null, true);
            Assert.Equal(Step.InspectProductionAttempt, result.NextStep);
        }
        watch.Stop();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        _output.WriteLine($"V15 128 readonly next-step evaluations: " +
            $"{watch.Elapsed.TotalMilliseconds:F3} ms; {bytes} caller-thread bytes; " +
            "OFFLINE, no gameplay or TCP latency inference");
        Assert.InRange(bytes, 0L, 5_000_000L);
    }
}
