extern alias F06App;

using System;
using System.Collections.Concurrent;
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
using WorkflowPhase = F06App::GivreClone.App.Services.Production.GuidedProductionWorkflowPhase;
using StockPlanner = F06App::GivreClone.App.Services.Production.GuidedProductionStockPlanner;
using ReadOnlyActions = F06App::GivreClone.App.Services.Production.GuidedProductionReadOnlyActions;
using Journal = F06App::GivreClone.App.Services.Production.ItemCraftProductionAttemptJournal;
using JournalPhase = F06App::GivreClone.App.Services.Production.ItemCraftAttemptPhase;
using JournalDecision = F06App::GivreClone.App.Services.Production.ItemCraftAttemptDecision;
using Recipe = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionRecipe;
using Ingredient = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionInput;
using Request = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionRequest;
using Receipt = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftWorkshopReceipt;
using Actions = F06App::GivreClone.App.Services.ItemCraftPlanner.IItemCraftProductionActions;
using StockObservation = F06App::GivreClone.App.Services.ItemCraftPlanner.IItemCraftProductionStockObservation;
using Activity = F06App::GivreClone.App.Services.ActivityRecoveryDurableStatus;
using ActivityScope = F06App::GivreClone.App.Services.ActivityRecoveryScope;
using ActivityPhase = F06App::GivreClone.App.Services.ActivityRecoveryDurableState;
using ViewModel = F06App::GivreClone.App.ViewModels.ItemCraftPlannerViewModel;

namespace GivreClone.Tests;

/// <summary>
/// Real V12 durable transitions + existing V9 attempt journal and real
/// production action ports with offline, non-transmitting injected stock.
/// A fixture-supplied completed receipt is NOT a Live server ACK.
/// </summary>
public sealed class GuidedProductionOperationalV12Tests
{
    private const string Profile = "server:292:CharacterV12";
    private readonly ITestOutputHelper _output;
    public GuidedProductionOperationalV12Tests(ITestOutputHelper output) => _output = output;

    private static Goal Goal(long character = 991001) =>
        new(292, character, 191105026, 7, 320, 1934, 3, 95, true);

    private static Request Order(bool bank = true, bool buy = true,
        int? resultPerCraft = 1) =>
        new(new Recipe(1934, resultPerCraft, 12, 0,
            [new Ingredient(10, 2, buy), new Ingredient(11, 1, false)]),
            3, 10_000, bank, null);

    private static Activity Status(ActivityPhase phase) =>
        new(new ActivityScope(292, 991001), phase, null, 0, 0, "simulated checkpoint");

    private sealed class Fixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(),
            "givre-guided-v12-" + Guid.NewGuid().ToString("N"));
        public Workflow Workflow => new(Path.Combine(Root, "flow"));
        public Journal Journal => new(Path.Combine(Root, "attempts", "itemcraft.journal"));
        public string StatePath => Path.Combine(Root, "flow", "server-292",
            "character-991001.workflow");
        public void Dispose()
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class LocalStockActions : Actions, StockObservation
    {
        public readonly Dictionary<long, int> Bag = new() { [10] = 2, [11] = 3 };
        public readonly Dictionary<long, int> Chest = new() { [10] = 3, [11] = 0 };
        public int ActualSends;
        public bool SupportsStockObservation { get; set; } = true;
        public bool TryReadBagCount(long gid, out int count) => Bag.TryGetValue(gid, out count);
        public int BagCount(long gid) => Bag.GetValueOrDefault(gid);
        public int ChestCount(long gid) => Chest.GetValueOrDefault(gid);
        public IReadOnlyList<MapInteractive> Interactives() => Array.Empty<MapInteractive>();
        public Task<bool> OpenChestAsync(CancellationToken ct)
        { ActualSends++; return Task.FromResult(false); }
        public Task<int> WithdrawAsync(long gid, int quantity, CancellationToken ct)
        { ActualSends++; return Task.FromResult(0); }
        public Task<bool> CloseChestAsync(CancellationToken ct)
        { ActualSends++; return Task.FromResult(false); }
        public Task<Receipt> CraftAsync(int elementId, int skillInstanceUid,
            IReadOnlyList<(long Gid, int Qty)> ingredients, int count,
            CancellationToken ct)
        { ActualSends++; return Task.FromResult(new Receipt(false, 0, 0)); }
        public Task<long> LotPriceAsync(long gid, int quantity, CancellationToken ct)
        { ActualSends++; return Task.FromResult(0L); }
        public Task<bool> BuyAsync(long gid, int quantity, long maxPrice, CancellationToken ct)
        { ActualSends++; return Task.FromResult(false); }
        public Task<bool> ListAsync(long gid, int quantity, long price, CancellationToken ct)
        { ActualSends++; return Task.FromResult(false); }
    }

    [Fact]
    public void Preflight_observes_ingredients_in_the_real_runner_ports_without_a_game_send()
    {
        var actions = new LocalStockActions();
        var result = StockPlanner.Evaluate(Order(), actions);
        Assert.True(result.Valid);
        Assert.True(result.NeedsChest);
        Assert.True(result.NeedsBuyer);
        Assert.False(result.NeedsGather);
        Assert.True(result.MayRequestExistingRunner);
        var ten = Assert.Single(result.Ingredients.Where(x => x.Gid == 10));
        Assert.Equal(6, ten.Required);
        Assert.Equal(2, ten.BagObserved);
        Assert.Equal(3, ten.ChestObserved);
        Assert.Equal(4, ten.MissingFromBag);
        Assert.Equal(1, ten.MissingAfterAuthorizedBank);
        Assert.Equal(0, actions.ActualSends);
    }

    [Fact]
    public void Unbuyable_missing_resource_demands_route_or_manual_stock_not_auto_craft()
    {
        var actions = new LocalStockActions();
        var pre = StockPlanner.Evaluate(Order(bank: false, buy: false), actions);
        Assert.True(pre.Valid);
        Assert.True(pre.NeedsGather);
        Assert.False(pre.MayRequestExistingRunner);
        Assert.Equal(4, pre.MissingCount);
        Assert.Equal(0, actions.ActualSends);
    }

    [Fact]
    public void Stale_unknown_or_incomplete_stock_is_denied_without_any_send()
    {
        var actions = new LocalStockActions { SupportsStockObservation = false };
        Assert.False(StockPlanner.Evaluate(Order(), actions).Valid);
        actions.SupportsStockObservation = true;
        actions.Bag.Remove(10);
        Assert.False(StockPlanner.Evaluate(Order(), actions).Valid);
        Assert.False(StockPlanner.Evaluate(Order(resultPerCraft: null), actions).Valid);
        Assert.Equal(0, actions.ActualSends);
    }

    [Fact]
    public async Task Real_preflight_action_port_reads_necessary_counts_but_never_emits_even_if_abused()
    {
        var source = new LocalStockActions();
        bool current = true;
        var safe = new ReadOnlyActions(source, () => current);
        Assert.Equal(2, safe.BagCount(10));
        Assert.True(safe.TryReadBagCount(10, out int seen));
        Assert.Equal(2, seen);
        Assert.Equal(3, safe.ChestCount(10));
        Assert.True(StockPlanner.Evaluate(Order(), safe).Valid);
        Assert.False(await safe.OpenChestAsync(CancellationToken.None));
        Assert.Equal(0, await safe.WithdrawAsync(10, 2, CancellationToken.None));
        Assert.False(await safe.CloseChestAsync(CancellationToken.None));
        Assert.False((await safe.CraftAsync(12, 13, [], 1,
            CancellationToken.None)).Confirmed);
        Assert.Equal(0, await safe.LotPriceAsync(10, 1, CancellationToken.None));
        Assert.False(await safe.BuyAsync(10, 1, 100, CancellationToken.None));
        Assert.False(await safe.ListAsync(1934, 1, 1000, CancellationToken.None));
        Assert.Equal(0, source.ActualSends);
        current = false;
        Assert.False(safe.TryReadBagCount(10, out _));
        current = true; // ABA never restores a revoked source.
        Assert.True(safe.IsRevoked);
        Assert.False(safe.TryReadBagCount(10, out _));
    }

    [Fact]
    public void Real_v9_attempt_run_reconciles_only_matching_v12_workflow_after_receipts()
    {
        using var scratch = new Fixture();
        var goal = Goal();
        var request = Order();
        string hash = Journal.RequestFingerprint(request);
        var flow = scratch.Workflow;
        Assert.True(flow.TryNotePreflight(goal, hash, true, "stock observed; runner still validates"));
        Assert.Equal(WorkflowPhase.Ready, new Workflow(Path.Combine(scratch.Root, "flow"))
            .Read(292, 991001).Phase);
        var v9 = scratch.Journal;
        Assert.Equal(JournalDecision.Allowed, v9.TryBegin(Profile, 91, request, out var lease));
        Assert.True(flow.TryBegin(goal, hash, lease.RunId));
        Assert.Equal(WorkflowPhase.InProgress, flow.Read(292, 991001).Phase);
        Assert.False(flow.TryBegin(goal, hash, lease.RunId));
        Assert.False(flow.TryNotePreflight(goal, hash, true, "attempt already emitted"));
        // One prior run cannot be replaced by another local UUID or a
        // Completed status from an unrelated profile/request.
        var fake = new F06App::GivreClone.App.Services.Production.ItemCraftAttemptSnapshot(
            JournalPhase.Completed, "foreign offline witness", hash, Guid.NewGuid());
        Assert.False(flow.TryCompleteFromActualProductionJournal(goal, fake));
        Assert.True(v9.TryFinish(lease, fullyConfirmed: true)); // fixture simulates EXACT V9 receipt finish
        var observed = new Journal(Path.Combine(scratch.Root, "attempts",
            "itemcraft.journal")).ReadStatus(Profile);
        Assert.Equal(JournalPhase.Completed, observed.Phase);
        Assert.Equal(lease.RunId, observed.LocalRunId);
        Assert.Equal(hash, observed.RequestHash);
        Assert.Equal(WorkflowPhase.AwaitingObservation,
            flow.Inspect(goal, Status(ActivityPhase.Clear), observed).Phase);
        Assert.True(flow.TryCompleteFromActualProductionJournal(goal, observed));
        Assert.Equal(WorkflowPhase.Reconciled, flow.Read(292, 991001).Phase);
        Assert.False(flow.TryCompleteFromActualProductionJournal(goal, observed));
    }

    [Fact]
    public void Crash_after_v9_admission_before_verified_receipts_never_restarts()
    {
        using var scratch = new Fixture();
        var goal = Goal();
        var request = Order();
        string hash = Journal.RequestFingerprint(request);
        Assert.True(scratch.Workflow.TryNotePreflight(goal, hash, true, "offline validated"));
        var first = scratch.Journal;
        Assert.Equal(JournalDecision.Allowed, first.TryBegin(Profile, 91, request, out var lease));
        Assert.True(scratch.Workflow.TryBegin(goal, hash, lease.RunId));
        // Crash: no receipts, no cleanup. Reopen both journals in new object.
        var restarted = scratch.Workflow;
        var pending = scratch.Journal.ReadStatus(Profile);
        Assert.Equal(JournalPhase.Started, pending.Phase);
        Assert.Equal(WorkflowPhase.InProgress, restarted.Read(292, 991001).Phase);
        Assert.False(restarted.TryNotePreflight(goal, hash, true, "attempt replay forbidden"));
        Assert.False(restarted.TryBegin(goal, hash, Guid.NewGuid()));
        Assert.False(restarted.TryCompleteFromActualProductionJournal(goal, pending));
        Assert.True(first.TryFinish(lease, fullyConfirmed: false));
        var uncertain = scratch.Journal.ReadStatus(Profile);
        Assert.True(restarted.TryCompleteFromActualProductionJournal(goal, uncertain));
        Assert.Equal(WorkflowPhase.Uncertain, restarted.Read(292, 991001).Phase);
        Assert.False(restarted.TryNotePreflight(goal, hash, true, "no auto-clear"));
    }

    [Theory]
    [InlineData(ActivityPhase.Pending, WorkflowPhase.AwaitingObservation)]
    [InlineData(ActivityPhase.Corrupt, WorkflowPhase.Blocked)]
    [InlineData(ActivityPhase.StorageUnavailable, WorkflowPhase.Blocked)]
    public void An_unsettled_activity_recovery_marker_blocks_prepared_trajectory(
        ActivityPhase state, WorkflowPhase expected)
    {
        using var scratch = new Fixture();
        var goal = Goal();
        var request = Order();
        var hash = Journal.RequestFingerprint(request);
        Assert.True(scratch.Workflow.TryNotePreflight(goal, hash, true, "local observations"));
        var observed = scratch.Workflow.Inspect(goal, Status(state),
            new F06App::GivreClone.App.Services.Production.ItemCraftAttemptSnapshot(
                JournalPhase.None, "no craft"));
        Assert.Equal(expected, observed.Phase);
        Assert.False(observed.Phase == WorkflowPhase.Reconciled);
    }

    [Fact]
    public void Torn_anchor_preserves_corruption_and_fences_all_new_work()
    {
        using var scratch = new Fixture();
        var goal = Goal();
        string hash = Journal.RequestFingerprint(Order());
        Assert.True(scratch.Workflow.TryNotePreflight(goal, hash, true, "ready"));
        string original = File.ReadAllText(scratch.StatePath);
        File.AppendAllText(scratch.StatePath + ".anchor", "not-a-receipt");
        Assert.Equal(WorkflowPhase.Uncertain, scratch.Workflow.Read(292, 991001).Phase);
        Assert.False(scratch.Workflow.TryNotePreflight(goal, hash, true, "retry blocked"));
        Assert.Equal(original, File.ReadAllText(scratch.StatePath));
    }

    [Fact]
    public void Manual_workshop_after_detach_or_restart_cannot_bypass_unresolved_v12_run()
    {
        using var scratch = new Fixture();
        var goal = Goal();
        var request = Order();
        var hash = Journal.RequestFingerprint(request);
        Assert.True(Workflow.MayBeginIndependentManualProduction(
            scratch.Workflow.Read(292, 991001))); // previous manual path preserved
        Assert.True(scratch.Workflow.TryNotePreflight(goal, hash, true, "ready"));
        Assert.True(Workflow.MayBeginIndependentManualProduction(
            scratch.Workflow.Read(292, 991001))); // not emitted yet
        var journal = scratch.Journal;
        Assert.Equal(JournalDecision.Allowed, journal.TryBegin(
            Profile, 91, request, out var lease));
        Assert.True(scratch.Workflow.TryBegin(goal, hash, lease.RunId));
        var afterRestart = new Workflow(Path.Combine(scratch.Root, "flow"));
        Assert.False(Workflow.MayBeginIndependentManualProduction(
            afterRestart.Read(292, 991001))); // no replay
        Assert.True(journal.TryFinish(lease, fullyConfirmed: false));
        Assert.True(scratch.Workflow.TryCompleteFromActualProductionJournal(
            goal, scratch.Journal.ReadStatus(Profile)));
        Assert.False(Workflow.MayBeginIndependentManualProduction(
            afterRestart.Read(292, 991001))); // still uncertain
    }

    [Fact]
    public async Task Simultaneous_double_clicks_have_at_most_one_actual_v12_start()
    {
        using var scratch = new Fixture();
        var goal = Goal();
        string hash = Journal.RequestFingerprint(Order());
        Assert.True(scratch.Workflow.TryNotePreflight(goal, hash, true, "ready"));
        var accepted = new ConcurrentBag<Guid>();
        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            var run = Guid.NewGuid();
            if (scratch.Workflow.TryBegin(goal, hash, run)) accepted.Add(run);
        })));
        var unique = Assert.Single(accepted);
        Assert.Equal(unique, scratch.Workflow.Read(292, 991001).LocalRunId);
        Assert.False(scratch.Workflow.TryNotePreflight(goal, hash, true, "no replay"));
    }

    [Fact]
    public void Foreign_character_or_changed_goal_can_never_reconcile_existing_cycle()
    {
        using var scratch = new Fixture();
        var goal = Goal();
        string hash = Journal.RequestFingerprint(Order());
        var run = Guid.NewGuid();
        Assert.True(scratch.Workflow.TryNotePreflight(goal, hash, true, "ready"));
        Assert.True(scratch.Workflow.TryBegin(goal, hash, run));
        var fixtureOutcome = new F06App::GivreClone.App.Services.Production.ItemCraftAttemptSnapshot(
            JournalPhase.Completed, "synthetic exact fixture", hash, run);
        Assert.False(scratch.Workflow.TryCompleteFromActualProductionJournal(
            goal with { Quantity = 2 }, fixtureOutcome));
        Assert.False(scratch.Workflow.TryCompleteFromActualProductionJournal(
            Goal(character: 991002), fixtureOutcome));
        Assert.Equal(WorkflowPhase.InProgress, scratch.Workflow.Read(292, 991001).Phase);
        Assert.Equal(WorkflowPhase.Absent, scratch.Workflow.Read(292, 991002).Phase);
    }

    [Fact]
    public void The_real_atelier_entry_and_classic_panel_use_same_preflight_and_run_services()
    {
        Assert.NotNull(typeof(ViewModel).GetProperty("InspectGuidedStockPreflightCommand"));
        Assert.NotNull(typeof(ViewModel).GetProperty("InspectGuidedWorkflowCommand"));
        Assert.NotNull(typeof(ViewModel).GetMethod("GuidedWorkflowMayContinue"));
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName,
                   "GivreClone.App"))) dir = dir.Parent;
        Assert.NotNull(dir);
        string app = Path.Combine(dir!.FullName, "GivreClone.App");
        string vm = File.ReadAllText(Path.Combine(app, "ViewModels",
            "ItemCraftPlannerViewModel.cs"));
        string main = File.ReadAllText(Path.Combine(app, "Views", "MainWindow.axaml.cs"));
        string view = File.ReadAllText(Path.Combine(app, "Views", "ItemCraftPlannerPanel.axaml"));
        string routes = File.ReadAllText(Path.Combine(app, "ViewModels",
            "BotsViewModel.GuidedProduction.cs"));
        Assert.Contains("AdmitGuidedProductionActualRun(guidedGoal,", vm);
        Assert.Contains("FinishGuidedProductionActualRun(guidedGoal,", vm);
        Assert.Contains("GuidedWorkflowMayContinue(goal)", main);
        Assert.Contains("MayBeginIndependentManualProduction(", main);
        Assert.Contains("ProductionPreviewActions = pid =>", main);
        Assert.Contains("new GuidedProductionReadOnlyActions(", main);
        Assert.Contains("previewOnly: true", File.ReadAllText(Path.Combine(app,
            "ViewModels", "ItemCraftPlannerViewModel.GuidedActivity.cs")));
        Assert.Contains("InspectGuidedStockPreflightCommand", view);
        Assert.Contains("GuidedObservedIngredients", view);
        Assert.Contains("_guidedWorkflowLedger.Inspect(goal, recovery,", routes);
    }

    [Fact]
    public void Local_checkpoint_read_cost_is_reported_without_any_live_extrapolation()
    {
        using var scratch = new Fixture();
        var goal = Goal();
        Assert.True(scratch.Workflow.TryNotePreflight(goal,
            Journal.RequestFingerprint(Order()), true, "small persisted checkpoint"));
        long start = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        for (int n = 0; n < 32; n++)
            Assert.Equal(WorkflowPhase.Ready, scratch.Workflow.Read(292, 991001).Phase);
        clock.Stop();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        _output.WriteLine($"V12 32 real fsynced-checkpoint reads: {clock.Elapsed.TotalMilliseconds:F3} ms, " +
            $"{bytes} bytes allocated on caller thread, OFFLINE only");
        Assert.InRange(bytes, 0L, 48_000_000L);
    }
}
