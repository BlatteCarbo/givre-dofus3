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
using SafeActions = F06App::GivreClone.App.Services.Production.GuidedProductionReadOnlyActions;
using GoalStore = F06App::GivreClone.App.Services.Production.GuidedActivityProductionStore;
using Ledger = F06App::GivreClone.App.Services.Production.GuidedProductionWorkflowLedger;
using WorkflowPhase = F06App::GivreClone.App.Services.Production.GuidedProductionWorkflowPhase;
using Planner = F06App::GivreClone.App.Services.Production.GuidedSupplyPlannerV13;
using SupplyPhase = F06App::GivreClone.App.Services.Production.GuidedSupplyPlanPhaseV13;
using Continuation = F06App::GivreClone.App.Services.Production.GuidedSupplyResumePlannerV16;
using Step = F06App::GivreClone.App.Services.Production.GuidedSupplyResumeStepV16;
using Choice = F06App::GivreClone.App.Services.Production.GuidedSupplyExactChoiceV16;
using Journal = F06App::GivreClone.App.Services.Production.ItemCraftProductionAttemptJournal;
using AttemptPhase = F06App::GivreClone.App.Services.Production.ItemCraftAttemptPhase;
using AttemptDecision = F06App::GivreClone.App.Services.Production.ItemCraftAttemptDecision;
using RecoveryStore = F06App::GivreClone.App.Services.ActivityRecoveryDurableStore;
using RecoveryScope = F06App::GivreClone.App.Services.ActivityRecoveryScope;
using RecoverySegment = F06App::GivreClone.App.Services.ActivityRecoverySegment;
using RecoveryKind = F06App::GivreClone.App.Services.ActivityRecoveryKind;
using RecoverySnapshot = F06App::GivreClone.App.Services.ActivityRecoverySnapshot;
using Recipe = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionRecipe;
using Ingredient = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionInput;
using Request = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionRequest;
using Receipt = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftWorkshopReceipt;
using StockPort = F06App::GivreClone.App.Services.ItemCraftPlanner.IItemCraftProductionStockObservation;
using ActionPort = F06App::GivreClone.App.Services.ItemCraftPlanner.IItemCraftProductionActions;

namespace GivreClone.Tests;

/// <summary>
/// V18 real durable V11/V12/V13/V16/V17 + ActivityRecovery and V9 JOURNAL
/// integrations. Only local observer fixtures are fake. Every mutation
/// port throws, so no assertion treats stock or a staged HDV row as an ACK.
/// </summary>
public sealed class GuidedSupplyContinuationV18Tests
{
    private const string Profile = "server:292:V18Observer";
    private static readonly Goal Target = new(
        292, 991001, 191105026, 7, 320, 1934, 3, 95, true);
    private static readonly RecoveryScope Scope = new(292, 991001);

    private static Request Order(bool bank = true, bool buy10 = true,
        bool buy11 = true) =>
        new(new Recipe(1934, 1, 12, 0,
            [new Ingredient(10, 2, buy10), new Ingredient(11, 1, buy11)]),
            3, 10000, bank, null);

    private sealed class Disk : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(),
            "givre-guided-supply-v18-" + Guid.NewGuid().ToString("N"));
        public GoalStore Goals => new(Path.Combine(_root, "goals"));
        public Ledger Flow => new(Path.Combine(_root, "workflow"));
        public RecoveryStore Activity => new(Path.Combine(_root, "activity"));
        public Journal Attempt => new(Path.Combine(_root, "attempt", "v9.journal"));
        public string WorkflowPath => Path.Combine(_root, "workflow",
            "server-292", "character-991001.workflow");

        public Disk() => Assert.True(Goals.TrySave(Target));
        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class Host
    {
        public long Revision = 10;
        public string Session = "host-link-epoch-v18";
        public long Map = 191105026;
        public long Generation = 7;
        public int? Used = 20;
        public int? Max = 100;
        public bool Current = true;
        public int SourceReads;
        public Action<int>? BeforeSourceRead;

        public Source Read()
        {
            BeforeSourceRead?.Invoke(++SourceReads);
            return new Source(292, 991001, Map, Generation, Session,
                Revision, Used, Max);
        }
    }

    private sealed class Stock : ActionPort, StockPort
    {
        public readonly Dictionary<long, int> Bag = new()
        {
            [10] = 1, [11] = 1
        };
        public readonly Dictionary<long, int> Chest = new()
        {
            [10] = 2, [11] = 1
        };
        public Action? DuringBagRead;
        public Action? DuringChestRead;
        public bool ThrowDuringBag;
        public int Reads;
        public int Sends;
        public bool SupportsStockObservation => true;
        public bool TryReadBagCount(long gid, out int count)
        {
            Reads++;
            DuringBagRead?.Invoke();
            if (ThrowDuringBag) throw new IOException("observer disconnected");
            return Bag.TryGetValue(gid, out count);
        }
        public int BagCount(long gid) => Bag.GetValueOrDefault(gid);
        public int ChestCount(long gid)
        {
            DuringChestRead?.Invoke();
            return Chest.GetValueOrDefault(gid);
        }
        public IReadOnlyList<MapInteractive> Interactives() =>
            Array.Empty<MapInteractive>();
        private Task<T> CannotSend<T>()
        {
            Sends++;
            throw new InvalidOperationException("Unexpected V18 gameplay send");
        }
        public Task<bool> OpenChestAsync(CancellationToken ct) => CannotSend<bool>();
        public Task<int> WithdrawAsync(long gid, int qty, CancellationToken ct) =>
            CannotSend<int>();
        public Task<bool> CloseChestAsync(CancellationToken ct) => CannotSend<bool>();
        public Task<Receipt> CraftAsync(int elementId, int skill,
            IReadOnlyList<(long Gid, int Qty)> ingredients, int count,
            CancellationToken ct) => CannotSend<Receipt>();
        public Task<long> LotPriceAsync(long gid, int qty, CancellationToken ct) =>
            CannotSend<long>();
        public Task<bool> BuyAsync(long gid, int qty, long limit,
            CancellationToken ct) => CannotSend<bool>();
        public Task<bool> ListAsync(long gid, int qty, long price,
            CancellationToken ct) => CannotSend<bool>();
    }

    private static SafeActions Safe(Stock stock, Host host) =>
        new(stock, () => host.Current, host.Read);

    private static F06App::GivreClone.App.Services.Production.GuidedSupplyResumeV16
        Next(Disk disk, Request order, SafeActions actions) =>
        Continuation.InspectNextAdmissionV17(Target, order, actions,
            disk.Goals, disk.Flow, disk.Activity, disk.Attempt, Profile);

    private static F06App::GivreClone.App.Services.Production.GuidedSupplyNextStepEvidenceV18
        Verify(Disk disk, Request order, SafeActions actions, Choice choice) =>
        Continuation.RevalidateRecordedNextStepV18(Target, order, actions,
            disk.Goals, disk.Flow, disk.Activity, disk.Attempt, Profile, choice);

    private static bool Choose(Disk disk, Request order, SafeActions actions,
        Choice choice) =>
        Continuation.TryChooseNextOnceV17(Target, order, actions,
            disk.Goals, disk.Flow, disk.Activity, disk.Attempt, Profile, choice);

    private static void Checkpoint(Disk disk, Request order, SafeActions actions)
    {
        var stored = Planner.PrepareAndCheckpoint(Target, order, actions,
            disk.Activity.ReadStatus(Scope), disk.Attempt.ReadStatus(Profile),
            disk.Flow, explicitPreviewCheckpoint: true);
        Assert.True(stored.EvidenceValid);
        Assert.True(stored.CheckpointWritten);
    }

    [Fact]
    public void Exact_pods_stop_threshold_is_exclusive_and_matches_desktop_guard()
    {
        using var disk = new Disk();
        var host = new Host { Used = 95, Max = 100 };
        var stock = new Stock();
        var plan = Planner.Evaluate(Target, Order(), Safe(stock, host),
            disk.Activity.ReadStatus(Scope), disk.Attempt.ReadStatus(Profile),
            disk.Flow.Read(292, 991001));
        Assert.False(plan.EvidenceValid);
        Assert.Equal(SupplyPhase.Blocked, plan.Phase);
        Assert.Contains("pods", plan.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Step.InspectPods, Next(disk, Order(), Safe(stock, host)).Step);
        Assert.Equal(0, stock.Sends);
    }

    [Fact]
    public void Pods_changing_between_plan_and_checkpoint_revokes_local_write()
    {
        using var disk = new Disk();
        var host = new Host();
        var stock = new Stock();
        // Evaluate checks source before and after stock IO; PrepareAndCheckpoint
        // performs an additional source read immediately before persistence.
        host.BeforeSourceRead = reads =>
        {
            if (reads == 3) host.Used = 95;
        };
        var rejected = Planner.PrepareAndCheckpoint(Target, Order(),
            Safe(stock, host), disk.Activity.ReadStatus(Scope),
            disk.Attempt.ReadStatus(Profile), disk.Flow,
            explicitPreviewCheckpoint: true);
        Assert.True(rejected.EvidenceValid); // earlier V13 arithmetic is valid
        Assert.False(rejected.CheckpointWritten);
        Assert.Equal(SupplyPhase.Blocked, rejected.Phase);
        Assert.Equal(WorkflowPhase.Absent, disk.Flow.Read(292, 991001).Phase);
        Assert.Equal(0, stock.Sends);
    }

    [Fact]
    public void Pods_changing_after_durable_checkpoint_write_are_reported_as_stale()
    {
        using var disk = new Disk();
        var host = new Host();
        var stock = new Stock();
        host.BeforeSourceRead = reads =>
        {
            // V13 source start/end, pre-write, then post-write identity read.
            if (reads == 4) host.Used = 45;
        };
        var status = Planner.PrepareAndCheckpoint(Target, Order(),
            Safe(stock, host), disk.Activity.ReadStatus(Scope),
            disk.Attempt.ReadStatus(Profile), disk.Flow,
            explicitPreviewCheckpoint: true);
        Assert.False(status.CheckpointWritten);
        Assert.Equal(SupplyPhase.Blocked, status.Phase);
        Assert.Contains("écrit", status.Reason);
        Assert.Equal(10, disk.Flow.Read(292, 991001).SupplyObservationRevision);
        Assert.Equal(Step.StageFreshCheckpoint,
            Next(disk, Order(), Safe(stock, host)).Step);
        Assert.Equal(0, stock.Sends);
    }

    [Fact]
    public void Exact_net_buy_after_hypothetical_bank_is_only_a_staged_HDV_inspection()
    {
        using var disk = new Disk();
        var host = new Host();
        var stock = new Stock();
        var order = Order();
        Checkpoint(disk, order, Safe(stock, host));
        var buy = Next(disk, order, Safe(stock, host)).Candidates
            .Single(x => x.Kind == Step.InspectBuy && x.Gid == 10);
        Assert.Equal(3, buy.Quantity); // required 6 - bag 1 - bank 2
        Assert.Equal(20, buy.ObservedUsedPodsV18);
        Assert.Equal(100, buy.ObservedCapacityPodsV18);
        Assert.Equal(20, disk.Flow.Read(292, 991001).SupplyUsedPodsV18);
        Assert.Equal(100, disk.Flow.Read(292, 991001).SupplyCapacityPodsV18);
        Assert.True(Choose(disk, order, Safe(stock, host), buy));
        var checkedAgain = Verify(disk, order, Safe(stock, host), buy);
        Assert.True(checkedAgain.MayPrepareConsultativeHandoff);
        Assert.Equal(Step.InspectBuy, checkedAgain.NextStep);
        Assert.Equal(buy, checkedAgain.RecordedChoice);
        Assert.False(checkedAgain.MayAuthorizeGameSend);
        Assert.False(checkedAgain.HasIndividuallyAttributedServerAck);
        Assert.Contains("hypothétique", checkedAgain.Reason);
        Assert.Equal(0, stock.Sends);
    }

    [Fact]
    public void Same_inventory_revision_with_new_pods_revokes_staged_buy_after_restart()
    {
        using var disk = new Disk();
        var host = new Host();
        var stock = new Stock();
        var order = Order();
        Checkpoint(disk, order, Safe(stock, host));
        var buy = Next(disk, order, Safe(stock, host)).Candidates
            .Single(x => x.Kind == Step.InspectBuy && x.Gid == 10);
        Assert.True(Choose(disk, order, Safe(stock, host), buy));
        Assert.True(Verify(disk, order, Safe(stock, host), buy)
            .MayPrepareConsultativeHandoff);

        // The inventory revision did not advance. The pods witness belongs
        // to the same session, but a changed load must revoke this handoff.
        host.Used = 37;
        var stale = Verify(disk, order, Safe(stock, host), buy);
        Assert.False(stale.MayPrepareConsultativeHandoff);
        Assert.Equal(buy, disk.Flow.Read(292, 991001).SupplyChoiceV16);
        Assert.False(Choose(disk, order, Safe(stock, host), buy));
        Assert.Equal(0, stock.Sends);
    }

    [Fact]
    public void Pods_changed_before_exact_choice_journal_write_cannot_be_selected()
    {
        using var disk = new Disk();
        var host = new Host();
        var stock = new Stock();
        var order = Order();
        Checkpoint(disk, order, Safe(stock, host));
        var buy = Next(disk, order, Safe(stock, host)).Candidates
            .Single(x => x.Kind == Step.InspectBuy && x.Gid == 10);
        host.SourceReads = 0;
        host.BeforeSourceRead = reads =>
        {
            // Four InspectNext source reads precede the final pre-write read.
            if (reads == 5) host.Used = 33;
        };
        Assert.False(Choose(disk, order, Safe(stock, host), buy));
        Assert.Null(disk.Flow.Read(292, 991001).SupplyChoiceV16);
        Assert.Equal(0, stock.Sends);
    }

    [Fact]
    public void Pods_changed_after_fsynced_choice_consume_the_choice_without_false_success()
    {
        using var disk = new Disk();
        var host = new Host();
        var stock = new Stock();
        var order = Order();
        Checkpoint(disk, order, Safe(stock, host));
        var buy = Next(disk, order, Safe(stock, host)).Candidates
            .Single(x => x.Kind == Step.InspectBuy && x.Gid == 10);
        host.BeforeSourceRead = _ =>
        {
            // The V12 ledger is persisted by TryNoteExactSupplyChoice before
            // the post-write host source read. This injects a real race at
            // that boundary without replacing or faking journal contents.
            if (disk.Flow.Read(292, 991001).SupplyChoiceV16 == buy)
                host.Used = 33;
        };
        Assert.False(Choose(disk, order, Safe(stock, host), buy));
        Assert.Equal(buy, disk.Flow.Read(292, 991001).SupplyChoiceV16);
        Assert.False(Verify(disk, order, Safe(stock, host), buy)
            .MayPrepareConsultativeHandoff);
        Assert.False(Choose(disk, order, Safe(stock, host), buy));
        Assert.Equal(0, stock.Sends);
    }

    [Fact]
    public void Other_choice_or_new_inventory_revision_revokes_old_HDV_handoff()
    {
        using var disk = new Disk();
        var host = new Host();
        var stock = new Stock();
        var order = Order();
        Checkpoint(disk, order, Safe(stock, host));
        var choices = Next(disk, order, Safe(stock, host)).Candidates;
        var buy10 = choices.Single(x => x.Kind == Step.InspectBuy && x.Gid == 10);
        var buy11 = choices.Single(x => x.Kind == Step.InspectBuy && x.Gid == 11);
        Assert.True(Choose(disk, order, Safe(stock, host), buy10));
        Assert.True(Choose(disk, order, Safe(stock, host), buy11));
        Assert.False(Verify(disk, order, Safe(stock, host), buy10)
            .MayPrepareConsultativeHandoff);
        Assert.True(Verify(disk, order, Safe(stock, host), buy11)
            .MayPrepareConsultativeHandoff);

        host.Revision = 11;
        stock.Bag[11] = 2;
        var revoked = Verify(disk, order, Safe(stock, host), buy11);
        Assert.False(revoked.MayPrepareConsultativeHandoff);
        Assert.False(revoked.MayAuthorizeGameSend);
        Assert.Equal(10, disk.Flow.Read(292, 991001).SupplyObservationRevision);
        Assert.Equal(0, stock.Sends);
    }

    [Fact]
    public void Exact_bank_inspection_requires_chest_opt_in_and_current_pods_and_source()
    {
        using var disk = new Disk();
        var host = new Host();
        var stock = new Stock();
        var order = Order();
        Checkpoint(disk, order, Safe(stock, host));
        var bank = Next(disk, order, Safe(stock, host)).Candidates
            .Single(x => x.Kind == Step.InspectBank && x.Gid == 10);
        Assert.Equal(2, bank.Quantity);
        Assert.True(Choose(disk, order, Safe(stock, host), bank));
        Assert.True(Verify(disk, order, Safe(stock, host), bank)
            .MayPrepareConsultativeHandoff);
        host.Session = "new-decoder-epoch";
        Assert.False(Verify(disk, order, Safe(stock, host), bank)
            .MayPrepareConsultativeHandoff);
        host.Session = "host-link-epoch-v18";
        host.Generation++;
        Assert.False(Verify(disk, order, Safe(stock, host), bank)
            .MayPrepareConsultativeHandoff);
        Assert.Equal(0, stock.Sends);
    }

    [Fact]
    public void Concurrent_stock_source_change_inside_chest_read_revokes_all_candidates()
    {
        using var disk = new Disk();
        var host = new Host();
        var stock = new Stock();
        stock.DuringChestRead = () => host.Used++;
        var safe = Safe(stock, host);
        var plan = Planner.Evaluate(Target, Order(), safe,
            disk.Activity.ReadStatus(Scope), disk.Attempt.ReadStatus(Profile),
            disk.Flow.Read(292, 991001));
        Assert.False(plan.EvidenceValid);
        Assert.Empty(plan.Rows);
        Assert.Equal(Step.RefreshObservedStock,
            Next(disk, Order(), Safe(stock, host)).Step);
        Assert.Equal(0, stock.Sends);
    }

    [Fact]
    public void Real_recovery_pending_preempts_selected_buy_without_auto_clear()
    {
        using var disk = new Disk();
        var host = new Host();
        var stock = new Stock();
        var order = Order();
        Checkpoint(disk, order, Safe(stock, host));
        var buy = Next(disk, order, Safe(stock, host)).Candidates
            .Single(x => x.Kind == Step.InspectBuy && x.Gid == 10);
        Assert.True(Choose(disk, order, Safe(stock, host), buy));
        var segment = new RecoverySegment(RecoveryKind.Gather, 2416,
            Cell: 241, ElementId: 2416);
        var observation = new RecoverySnapshot(
            new ResumableRouteMap(191105026, 7), 191105026, 191105026,
            240, PositionConfirmed: true, Interrupted: false,
            Suspended: false, ResourcePresent: true,
            ResourceAvailable: true, AlreadyInReach: true,
            StockGid: 10, StockCount: 1);
        Assert.True(disk.Activity.TryReserve(Scope, segment, observation).MayDispatch);
        int reads = stock.Reads;
        var pending = Verify(disk, order, Safe(stock, host), buy);
        Assert.False(pending.MayPrepareConsultativeHandoff);
        Assert.Equal(Step.InspectActivityRecovery, pending.NextStep);
        Assert.Equal(reads, stock.Reads);
        Assert.Equal(buy, disk.Flow.Read(292, 991001).SupplyChoiceV16);
        Assert.Equal(0, stock.Sends);
    }

    [Fact]
    public void Real_V9_started_and_uncertain_journal_revokes_buy_staging_after_restart()
    {
        using var disk = new Disk();
        var host = new Host();
        var stock = new Stock();
        var order = Order();
        Checkpoint(disk, order, Safe(stock, host));
        var buy = Next(disk, order, Safe(stock, host)).Candidates
            .Single(x => x.Kind == Step.InspectBuy && x.Gid == 10);
        Assert.True(Choose(disk, order, Safe(stock, host), buy));
        var liveJournal = disk.Attempt;
        Assert.Equal(AttemptDecision.Allowed,
            liveJournal.TryBegin(Profile, 123, order, out var lease));
        Assert.True(disk.Flow.TryBegin(Target,
            Journal.RequestFingerprint(order), lease.RunId));
        var started = Verify(disk, order, Safe(stock, host), buy);
        Assert.False(started.MayPrepareConsultativeHandoff);
        Assert.Equal(Step.InspectProductionAttempt, started.NextStep);
        Assert.True(liveJournal.TryFinish(lease, fullyConfirmed: false));
        Assert.Equal(AttemptPhase.Uncertain, disk.Attempt.ReadStatus(Profile).Phase);
        Assert.Equal(Step.InspectProductionAttempt,
            Verify(disk, order, Safe(stock, host), buy).NextStep);
        Assert.False(disk.Flow.TryNotePreflight(Target,
            Journal.RequestFingerprint(order), true, "stale after V9"));
        Assert.Equal(0, stock.Sends);
    }

    [Fact]
    public void Route_choice_requires_its_existing_admission_even_with_growing_local_stock()
    {
        using var disk = new Disk();
        var host = new Host();
        var stock = new Stock();
        stock.Bag[10] = 0;
        stock.Bag[11] = 0;
        stock.Chest[10] = 0;
        stock.Chest[11] = 0;
        var order = Order(bank: false, buy10: false, buy11: false);
        Checkpoint(disk, order, Safe(stock, host));
        var route = Next(disk, order, Safe(stock, host)).Candidates
            .First(x => x.Kind == Step.PrepareExactGatherRoute);
        Assert.True(Choose(disk, order, Safe(stock, host), route));
        var result = Verify(disk, order, Safe(stock, host), route);
        Assert.False(result.MayPrepareConsultativeHandoff);
        Assert.False(result.MayAuthorizeGameSend);
        host.Revision = 11;
        stock.Bag[10] = 6;
        Assert.Equal(Step.SelectionRecorded,
            Next(disk, order, Safe(stock, host)).Step);
        Assert.Equal(route, disk.Flow.Read(292, 991001).SupplyChoiceV16);
        Assert.Equal(0, stock.Sends);
    }

    [Fact]
    public void Torn_workflow_anchor_refuses_consultative_handoff_after_restart()
    {
        using var disk = new Disk();
        var host = new Host();
        var stock = new Stock();
        var order = Order();
        Checkpoint(disk, order, Safe(stock, host));
        var buy = Next(disk, order, Safe(stock, host)).Candidates
            .Single(x => x.Kind == Step.InspectBuy && x.Gid == 10);
        Assert.True(Choose(disk, order, Safe(stock, host), buy));
        File.AppendAllText(disk.WorkflowPath + ".anchor", "damaged");
        Assert.Equal(WorkflowPhase.Uncertain, disk.Flow.Read(292, 991001).Phase);
        var result = Verify(disk, order, Safe(stock, host), buy);
        Assert.False(result.MayPrepareConsultativeHandoff);
        Assert.Equal(Step.InspectProductionAttempt, result.NextStep);
        Assert.Equal(0, stock.Sends);
    }
}
