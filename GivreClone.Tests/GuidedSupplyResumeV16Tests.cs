extern alias F06App;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;
using Xunit;
using Goal = F06App::GivreClone.App.Services.Production.GuidedActivityProductionGoal;
using Source = F06App::GivreClone.App.Services.Production.GuidedSupplySourceObservation;
using SafePort = F06App::GivreClone.App.Services.Production.GuidedProductionReadOnlyActions;
using GoalStore = F06App::GivreClone.App.Services.Production.GuidedActivityProductionStore;
using Workflow = F06App::GivreClone.App.Services.Production.GuidedProductionWorkflowLedger;
using Planner = F06App::GivreClone.App.Services.Production.GuidedSupplyPlannerV13;
using Resume = F06App::GivreClone.App.Services.Production.GuidedSupplyResumePlannerV16;
using ResumeStep = F06App::GivreClone.App.Services.Production.GuidedSupplyResumeStepV16;
using Journal = F06App::GivreClone.App.Services.Production.ItemCraftProductionAttemptJournal;
using JournalPhase = F06App::GivreClone.App.Services.Production.ItemCraftAttemptPhase;
using Receipt = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftWorkshopReceipt;
using Request = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionRequest;
using Recipe = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionRecipe;
using Ingredient = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionInput;
using ActionPort = F06App::GivreClone.App.Services.ItemCraftPlanner.IItemCraftProductionActions;
using StockPort = F06App::GivreClone.App.Services.ItemCraftPlanner.IItemCraftProductionStockObservation;
using Recovery = F06App::GivreClone.App.Services.ActivityRecoveryDurableStore;
using RecoveryScope = F06App::GivreClone.App.Services.ActivityRecoveryScope;
using RecoverySegment = F06App::GivreClone.App.Services.ActivityRecoverySegment;
using RecoveryKind = F06App::GivreClone.App.Services.ActivityRecoveryKind;
using RecoverySnapshot = F06App::GivreClone.App.Services.ActivityRecoverySnapshot;

namespace GivreClone.Tests;

/// <summary>
/// Real V11/V12/V13/V9/ActivityRecovery local-journal entry points with
/// host-like test observations. No sockets and no gameplay action call.
/// </summary>
public sealed class GuidedSupplyResumeV16Tests
{
    private const string Profile = "server:292:991001";
    private static readonly Goal Target = new(
        292, 991001, 191105026, 7, 320, 1934, 3, 95, true);

    private static Request RecipeFor(bool buy10 = true, bool bank = true,
        bool buy11 = false, long maxKamas = 10000) =>
        new(new Recipe(1934, 1, 12, 0,
            [new Ingredient(10, 2, buy10), new Ingredient(11, 1, buy11)]),
            3, maxKamas, bank, null);

    private sealed class Sandbox : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(),
            "givre-supply-resume-v16-" + Guid.NewGuid().ToString("N"));
        private Journal? _originalSenderJournal;

        public GoalStore Goals => new(Path.Combine(Root, "goals"));
        public Workflow Ledger => new(Path.Combine(Root, "workflow"));
        public Recovery Recovery => new(Path.Combine(Root, "activity"));
        // A real V9 TryBegin/TryFinish belongs to the SAME journal instance:
        // _localLeases deliberately rejects finishing a copied lease from a
        // restarted, foreign or otherwise unrelated sender.
        public Journal Attempt => _originalSenderJournal ??=
            new(Path.Combine(Root, "attempt", "v9.journal"));
        public Journal ReopenedAttempt() =>
            new(Path.Combine(Root, "attempt", "v9.journal"));
        public string WorkflowFile => Path.Combine(Root, "workflow",
            "server-292", "character-991001.workflow");

        public Sandbox() => Assert.True(Goals.TrySave(Target));

        public void Dispose()
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class FakePort : ActionPort, StockPort
    {
        public readonly Dictionary<long, int> Bag = new()
        {
            [10] = 2, [11] = 3
        };
        public readonly Dictionary<long, int> Chest = new()
        {
            [10] = 3, [11] = 0
        };
        public int Sends;
        public int BagReads;
        public bool SupportsStockObservation => true;
        public bool TryReadBagCount(long gid, out int count)
        {
            BagReads++;
            return Bag.TryGetValue(gid, out count);
        }
        public int BagCount(long gid) => Bag.GetValueOrDefault(gid);
        public int ChestCount(long gid) => Chest.GetValueOrDefault(gid);
        public IReadOnlyList<MapInteractive> Interactives() => Array.Empty<MapInteractive>();
        public Task<bool> OpenChestAsync(CancellationToken ct) => Sent<bool>();
        public Task<int> WithdrawAsync(long gid, int quantity, CancellationToken ct) => Sent<int>();
        public Task<bool> CloseChestAsync(CancellationToken ct) => Sent<bool>();
        public Task<Receipt> CraftAsync(int elementId, int skillInstanceUid,
            IReadOnlyList<(long Gid, int Qty)> ingredients, int count,
            CancellationToken ct) => Sent<Receipt>();
        public Task<long> LotPriceAsync(long gid, int quantity,
            CancellationToken ct) => Sent<long>();
        public Task<bool> BuyAsync(long gid, int quantity, long maxPrice,
            CancellationToken ct) => Sent<bool>();
        public Task<bool> ListAsync(long gid, int quantity, long price,
            CancellationToken ct) => Sent<bool>();
        private Task<T> Sent<T>()
        {
            Sends++;
            throw new InvalidOperationException("Gameplay send attempted by V16");
        }
    }

    private sealed class Observed
    {
        public long Revision = 10;
        public string Token = "host-decoder-7";
        public int? Used = 10;
        public int? Capacity = 100;
        public bool Current = true;
        public Source Snapshot() => new(292, 991001, 191105026, 7,
            Token, Revision, Used, Capacity);
    }

    private static SafePort Guard(FakePort fake, Observed source) =>
        new(fake, () => source.Current, source.Snapshot);

    private static F06App::GivreClone.App.Services.Production.GuidedSupplyResumeV16
        Inspect(Sandbox files, Request order, SafePort safe) =>
        Resume.InspectDurable(Target, order, safe, files.Goals, files.Ledger,
            files.Recovery, files.Attempt, Profile);

    private static bool Choose(Sandbox files, Request order, SafePort safe,
        F06App::GivreClone.App.Services.Production.GuidedSupplyExactChoiceV16 selection) =>
        Resume.TryChooseOnce(Target, order, safe, files.Goals, files.Ledger,
            files.Recovery, files.Attempt, Profile, selection);

    private static void Checkpoint(Sandbox files, Request order, SafePort safe)
    {
        var result = Planner.PrepareAndCheckpoint(Target, order, safe,
            files.Recovery.ReadStatus(new RecoveryScope(292, 991001)),
            files.Attempt.ReadStatus(Profile), files.Ledger, true);
        Assert.True(result.EvidenceValid);
        Assert.True(result.CheckpointWritten);
    }

    [Fact]
    public void Partial_bag_chest_and_buy_yield_exact_V9_choices_and_one_shot_restart()
    {
        using var files = new Sandbox();
        var data = new FakePort();
        var source = new Observed();
        var order = RecipeFor();
        Checkpoint(files, order, Guard(data, source));
        var preview = Inspect(files, order, Guard(data, source));
        Assert.Equal(ResumeStep.InspectBank, preview.Step);
        Assert.Equal(3, preview.Candidates.Count);
        Assert.Equal((10L, 3L), (preview.Candidates[0].Gid, preview.Candidates[0].Quantity));
        Assert.Equal(ResumeStep.InspectBank, preview.Candidates[0].Kind);
        Assert.Equal((10L, 1L), (preview.Candidates[1].Gid, preview.Candidates[1].Quantity));
        Assert.Equal(ResumeStep.InspectBuy, preview.Candidates[1].Kind);
        Assert.Equal((1934L, 3L), (preview.Candidates[2].Gid, preview.Candidates[2].Quantity));
        Assert.Equal(ResumeStep.UseExistingV9Runner, preview.Candidates[2].Kind);
        Assert.False(preview.MayAuthorizeGameSend);

        var selected = preview.Candidates[1];
        Assert.True(Choose(files, order, Guard(data, source), selected));
        // A freshly constructed ledger must read the same fsynced winner.
        var restarted = Inspect(files, order, Guard(data, source));
        Assert.Equal(ResumeStep.SelectionRecorded, restarted.Step);
        Assert.Equal(selected, restarted.RecordedChoice);
        Assert.Empty(restarted.Candidates);
        Assert.Equal(selected, files.Ledger.Read(292, 991001).SupplyChoiceV16);
        Assert.False(Choose(files, order, Guard(data, source), selected));
        Assert.Equal(0, data.Sends);
    }

    [Fact]
    public void Multiple_missing_unbuyable_GIDs_require_exact_choice_and_preserve_pods()
    {
        using var files = new Sandbox();
        var data = new FakePort();
        data.Bag[10] = 0;
        data.Bag[11] = 0;
        data.Chest[10] = 0;
        var source = new Observed { Used = 55, Capacity = 100 };
        var order = RecipeFor(buy10: false, bank: false);
        Checkpoint(files, order, Guard(data, source));
        var preview = Inspect(files, order, Guard(data, source));
        Assert.Equal(ResumeStep.PrepareExactGatherRoute, preview.Step);
        Assert.Equal(2, preview.Candidates.Count);
        Assert.All(preview.Candidates, c =>
            Assert.Equal(ResumeStep.PrepareExactGatherRoute, c.Kind));
        Assert.Equal((10L, 6L), (preview.Candidates[0].Gid, preview.Candidates[0].Quantity));
        Assert.Equal((11L, 3L), (preview.Candidates[1].Gid, preview.Candidates[1].Quantity));
        Assert.Equal(55, preview.UsedPods);
        Assert.Equal(100, preview.CapacityPods);
        Assert.False(Choose(files, order, Guard(data, source),
            preview.Candidates[0] with { Gid = 999 }));
        Assert.True(Choose(files, order, Guard(data, source), preview.Candidates[1]));
        Assert.Equal(0, data.Sends);
    }

    [Fact]
    public void Gather_choice_cannot_be_rearmed_just_because_local_stock_or_revision_changes()
    {
        using var files = new Sandbox();
        var data = new FakePort();
        data.Bag[10] = 0;
        data.Bag[11] = 0;
        var source = new Observed();
        var order = RecipeFor(buy10: false, bank: false);
        Checkpoint(files, order, Guard(data, source));
        var selected = Inspect(files, order, Guard(data, source)).Candidates[0];
        Assert.Equal(ResumeStep.PrepareExactGatherRoute, selected.Kind);
        Assert.True(Choose(files, order, Guard(data, source), selected));

        // A local delta is not a correlated gather receipt. A new preview
        // cannot overwrite this durable handoff.
        data.Bag[10] = 6;
        source.Revision++;
        var stale = Planner.PrepareAndCheckpoint(Target, order,
            Guard(data, source),
            files.Recovery.ReadStatus(new RecoveryScope(292, 991001)),
            files.Attempt.ReadStatus(Profile), files.Ledger, true);
        Assert.False(stale.CheckpointWritten);
        Assert.Equal(selected, files.Ledger.Read(292, 991001).SupplyChoiceV16);
        Assert.False(Choose(files, order, Guard(data, source), selected));
        Assert.Equal(0, data.Sends);
    }

    [Fact]
    public void Exact_V9_runner_handoff_is_durable_and_remains_readable_after_Started()
    {
        using var files = new Sandbox();
        var data = new FakePort();
        var source = new Observed();
        var order = RecipeFor();
        Checkpoint(files, order, Guard(data, source));
        var selected = Inspect(files, order, Guard(data, source)).Candidates
            .Single(x => x.Kind == ResumeStep.UseExistingV9Runner);
        Assert.True(Choose(files, order, Guard(data, source), selected));
        Assert.Equal(ResumeStep.SelectionRecorded,
            Inspect(files, order, Guard(data, source)).Step);

        // Real V9 local Start marker; no test code manufactures a completion.
        Assert.Equal(F06App::GivreClone.App.Services.Production.ItemCraftAttemptDecision.Allowed,
            files.Attempt.TryBegin(Profile, 123, order, out var lease));
        Assert.True(files.Ledger.TryBegin(Target,
            Journal.RequestFingerprint(order), lease.RunId));
        var restarted = files.Ledger.Read(292, 991001);
        Assert.Equal(F06App::GivreClone.App.Services.Production.GuidedProductionWorkflowPhase.InProgress,
            restarted.Phase);
        Assert.Equal(selected, restarted.SupplyChoiceV16);
        Assert.Equal(ResumeStep.InspectProductionAttempt,
            Inspect(files, order, Guard(data, source)).Step);
        Assert.False(files.ReopenedAttempt().TryFinish(lease,
            fullyConfirmed: false)); // foreign instance cannot close sender
        Assert.True(files.Attempt.TryFinish(lease, fullyConfirmed: false));
        Assert.Equal(JournalPhase.Uncertain, files.Attempt.ReadStatus(Profile).Phase);
        Assert.Equal(ResumeStep.InspectProductionAttempt,
            Inspect(files, order, Guard(data, source)).Step);
        Assert.Equal(0, data.Sends);
    }

    [Fact]
    public void Changed_inventory_revision_session_or_identity_never_consumes_old_choice()
    {
        using var files = new Sandbox();
        var data = new FakePort();
        var source = new Observed();
        var order = RecipeFor();
        Checkpoint(files, order, Guard(data, source));
        var original = Inspect(files, order, Guard(data, source)).Candidates[0];

        // An unchanged bag count still cannot authorize a DIFFERENT source.
        source.Token = "host-new-link";
        Assert.False(Choose(files, order, Guard(data, source), original));
        Assert.Empty(Inspect(files, order, Guard(data, source)).Candidates);
        source.Token = "host-decoder-7";
        source.Revision++;
        data.Bag[10]++;
        Assert.False(Choose(files, order, Guard(data, source), original));
        Assert.Null(files.Ledger.Read(292, 991001).SupplyChoiceV16);
        source.Revision--;
        source.Current = false;
        Assert.Equal(ResumeStep.RefreshObservedStock,
            Inspect(files, order, Guard(data, source)).Step);
        Assert.Equal(0, data.Sends);
    }

    [Fact]
    public void Unknown_pods_and_exceeded_maxPods_block_any_next_action()
    {
        using var files = new Sandbox();
        var data = new FakePort();
        var source = new Observed { Used = null, Capacity = null };
        var order = RecipeFor();
        Checkpoint(files, order, Guard(data, source));
        Assert.Equal(ResumeStep.InspectPods,
            Inspect(files, order, Guard(data, source)).Step);
        source.Used = 99;
        source.Capacity = 100;
        source.Revision++;
        var blocked = Inspect(files, order, Guard(data, source));
        Assert.Equal(ResumeStep.InspectPods, blocked.Step);
        Assert.Empty(blocked.Candidates);
        Assert.Equal(0, data.Sends);
    }

    [Fact]
    public void Real_pending_gather_marker_preempts_stock_growth_and_all_selection()
    {
        using var files = new Sandbox();
        var data = new FakePort();
        var source = new Observed();
        var order = RecipeFor();
        Checkpoint(files, order, Guard(data, source));
        var chosen = Inspect(files, order, Guard(data, source)).Candidates[0];
        var segment = new RecoverySegment(RecoveryKind.Gather, 2416,
            Cell: 241, ElementId: 2416);
        var snapshot = new RecoverySnapshot(
            new ResumableRouteMap(191105026, 7), 191105026, 191105026,
            240, PositionConfirmed: true, Interrupted: false,
            Suspended: false, ResourcePresent: true,
            ResourceAvailable: true, AlreadyInReach: true,
            StockGid: 10, StockCount: 2);
        Assert.True(files.Recovery.TryReserve(
            new RecoveryScope(292, 991001), segment, snapshot).MayDispatch);
        source.Revision++;
        data.Bag[10] += 2;
        int reads = data.BagReads;
        Assert.Equal(ResumeStep.InspectActivityRecovery,
            Inspect(files, order, Guard(data, source)).Step);
        Assert.Equal(reads, data.BagReads); // pending branch never reads inventory
        Assert.False(Choose(files, order, Guard(data, source), chosen));
        Assert.Null(files.Ledger.Read(292, 991001).SupplyChoiceV16);
        Assert.Equal(0, data.Sends);
    }

    [Fact]
    public void Real_V9_started_and_corrupt_V12_anchor_block_after_restart()
    {
        using var files = new Sandbox();
        var data = new FakePort();
        var source = new Observed();
        var order = RecipeFor();
        Checkpoint(files, order, Guard(data, source));
        var first = Inspect(files, order, Guard(data, source)).Candidates[0];
        Assert.Equal(F06App::GivreClone.App.Services.Production.ItemCraftAttemptDecision.Allowed,
            files.Attempt.TryBegin(Profile, 123, order, out var lease));
        Assert.NotEqual(Guid.Empty, lease.RunId);
        Assert.Equal(JournalPhase.Started, files.Attempt.ReadStatus(Profile).Phase);
        Assert.Equal(ResumeStep.InspectProductionAttempt,
            Inspect(files, order, Guard(data, source)).Step);
        Assert.False(Choose(files, order, Guard(data, source), first));

        // The V9 start remains pending, irrespective of a later local count.
        data.Bag[10] += 4;
        source.Revision++;
        Assert.Equal(ResumeStep.InspectProductionAttempt,
            Inspect(files, order, Guard(data, source)).Step);
        Assert.Equal(0, data.Sends);
    }

    [Fact]
    public void Corrupt_workflow_anchor_and_changed_durable_goal_cannot_rearm()
    {
        using var files = new Sandbox();
        var data = new FakePort();
        var source = new Observed();
        var order = RecipeFor();
        Checkpoint(files, order, Guard(data, source));
        var original = Inspect(files, order, Guard(data, source)).Candidates[0];
        Assert.True(files.Goals.TrySave(Target with { MaxPods = 90 }));
        Assert.Equal(ResumeStep.Blocked,
            Inspect(files, order, Guard(data, source)).Step);
        Assert.False(Choose(files, order, Guard(data, source), original));
        Assert.True(files.Goals.TrySave(Target));
        File.AppendAllText(files.WorkflowFile + ".anchor", "damaged");
        Assert.Equal(ResumeStep.InspectProductionAttempt,
            Inspect(files, order, Guard(data, source)).Step);
        Assert.False(Choose(files, order, Guard(data, source), original));
        Assert.Equal(0, data.Sends);
    }
}
