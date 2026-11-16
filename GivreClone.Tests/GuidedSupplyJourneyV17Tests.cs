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
using Guarded = F06App::GivreClone.App.Services.Production.GuidedProductionReadOnlyActions;
using Goals = F06App::GivreClone.App.Services.Production.GuidedActivityProductionStore;
using Ledger = F06App::GivreClone.App.Services.Production.GuidedProductionWorkflowLedger;
using WorkflowPhase = F06App::GivreClone.App.Services.Production.GuidedProductionWorkflowPhase;
using V13 = F06App::GivreClone.App.Services.Production.GuidedSupplyPlannerV13;
using V16 = F06App::GivreClone.App.Services.Production.GuidedSupplyResumePlannerV16;
using Step = F06App::GivreClone.App.Services.Production.GuidedSupplyResumeStepV16;
using Choice = F06App::GivreClone.App.Services.Production.GuidedSupplyExactChoiceV16;
using Journal = F06App::GivreClone.App.Services.Production.ItemCraftProductionAttemptJournal;
using AttemptPhase = F06App::GivreClone.App.Services.Production.ItemCraftAttemptPhase;
using ProductionDecision = F06App::GivreClone.App.Services.Production.ItemCraftAttemptDecision;
using Recovery = F06App::GivreClone.App.Services.ActivityRecoveryDurableStore;
using Scope = F06App::GivreClone.App.Services.ActivityRecoveryScope;
using Segment = F06App::GivreClone.App.Services.ActivityRecoverySegment;
using RecoveryKind = F06App::GivreClone.App.Services.ActivityRecoveryKind;
using RecoverySnapshot = F06App::GivreClone.App.Services.ActivityRecoverySnapshot;
using Recipe = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionRecipe;
using Input = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionInput;
using Request = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionRequest;
using Receipt = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftWorkshopReceipt;
using StockPort = F06App::GivreClone.App.Services.ItemCraftPlanner.IItemCraftProductionStockObservation;
using ActionPort = F06App::GivreClone.App.Services.ItemCraftPlanner.IItemCraftProductionActions;

namespace GivreClone.Tests;

/// <summary>
/// Full offline journey through the V11 objective, V12 durable ledger, V13
/// observed inventory plan, V16 operator choices, genuine V9 Started journal
/// and ActivityRecovery pending marker. A local delta is never a server ACK.
/// </summary>
public sealed class GuidedSupplyJourneyV17Tests
{
    private const string Profile = "server:292:V17Character";
    private static readonly Goal Target = new(
        292, 991001, 191105026, 7, 320, 1934, 3, 95, true);
    private static readonly Scope Owner = new(292, 991001);

    private static Request Order(bool bank = true, bool buy10 = true,
        bool buy11 = true) => new(
        new Recipe(1934, 1, 12, 0, [
            new Input(10, 2, buy10),
            new Input(11, 1, buy11)
        ]), 3, 10000, bank, null);

    private sealed class Disk : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(),
            "givre-guided-journey-v17-" + Guid.NewGuid().ToString("N"));
        public Goals Goals => new(Path.Combine(Root, "objective"));
        public Ledger Flow => new(Path.Combine(Root, "flow"));
        public Recovery Activity => new(Path.Combine(Root, "activity"));
        public Journal Attempt => new(Path.Combine(Root, "attempt", "v9.journal"));
        public string WorkflowFile => Path.Combine(Root, "flow",
            "server-292", "character-991001.workflow");
        public Disk() => Assert.True(Goals.TrySave(Target));
        public void Dispose()
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class Host
    {
        public long Revision = 10;
        public string Token = "real-test-host-session-7";
        public int? Used = 20;
        public int? Max = 100;
        public bool Current = true;
        public Source Read() => new(292, 991001, 191105026, 7,
            Token, Revision, Used, Max);
    }

    private sealed class Stock : ActionPort, StockPort
    {
        public readonly Dictionary<long, int> Bag = new() { [10] = 1, [11] = 1 };
        public readonly Dictionary<long, int> Chest = new() { [10] = 2, [11] = 1 };
        public Action? DuringBagRead;
        public Action? DuringChestRead;
        public bool ThrowOnBag;
        public bool SupportsStockObservation => true;
        public int Reads;
        public int Sends;
        public bool TryReadBagCount(long gid, out int count)
        {
            Reads++;
            DuringBagRead?.Invoke();
            if (ThrowOnBag) throw new IOException("offline witness loss");
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
        private Task<T> Deny<T>()
        {
            Sends++;
            throw new InvalidOperationException("V17 attempted a gameplay send");
        }
        public Task<bool> OpenChestAsync(CancellationToken ct) => Deny<bool>();
        public Task<int> WithdrawAsync(long gid, int qty, CancellationToken ct) => Deny<int>();
        public Task<bool> CloseChestAsync(CancellationToken ct) => Deny<bool>();
        public Task<Receipt> CraftAsync(int elementId, int skill,
            IReadOnlyList<(long Gid, int Qty)> ingredients, int count,
            CancellationToken ct) => Deny<Receipt>();
        public Task<long> LotPriceAsync(long gid, int qty, CancellationToken ct) => Deny<long>();
        public Task<bool> BuyAsync(long gid, int qty, long max,
            CancellationToken ct) => Deny<bool>();
        public Task<bool> ListAsync(long gid, int qty, long price,
            CancellationToken ct) => Deny<bool>();
    }

    private static Guarded Guard(Stock data, Host host) =>
        new(data, () => host.Current, host.Read);

    private static F06App::GivreClone.App.Services.Production.GuidedSupplyResumeV16
        Next(Disk disk, Request order, Guarded guarded) =>
        V16.InspectNextAdmissionV17(Target, order, guarded,
            disk.Goals, disk.Flow, disk.Activity, disk.Attempt, Profile);

    private static F06App::GivreClone.App.Services.Production.GuidedSupplyResumeV16
        Legacy(Disk disk, Request order, Guarded guarded) =>
        V16.InspectDurable(Target, order, guarded,
            disk.Goals, disk.Flow, disk.Activity, disk.Attempt, Profile);

    private static bool Select(Disk disk, Request order, Guarded guarded, Choice choice) =>
        V16.TryChooseNextOnceV17(Target, order, guarded,
            disk.Goals, disk.Flow, disk.Activity, disk.Attempt, Profile, choice);

    private static void Stage(Disk disk, Request order, Guarded guarded)
    {
        var staged = V13.PrepareAndCheckpoint(Target, order, guarded,
            disk.Activity.ReadStatus(Owner), disk.Attempt.ReadStatus(Profile),
            disk.Flow, explicitPreviewCheckpoint: true);
        Assert.True(staged.EvidenceValid);
        Assert.True(staged.CheckpointWritten);
    }

    [Fact]
    public void Real_journals_allow_distinct_bank_and_buy_consultations_then_exact_V9_choice()
    {
        using var disk = new Disk();
        var host = new Host();
        var stock = new Stock();
        var request = Order();
        Stage(disk, request, Guard(stock, host));

        var first = Next(disk, request, Guard(stock, host));
        Assert.Equal(5, first.Candidates.Count);
        Assert.Equal((10L, 2L), (first.Candidates[0].Gid, first.Candidates[0].Quantity));
        Assert.Equal(Step.InspectBank, first.Candidates[0].Kind);
        Assert.Equal((11L, 1L), (first.Candidates[1].Gid, first.Candidates[1].Quantity));
        Assert.Equal(Step.InspectBank, first.Candidates[1].Kind);
        Assert.Equal(Step.InspectBuy, first.Candidates[2].Kind);
        Assert.Equal((10L, 3L), (first.Candidates[2].Gid, first.Candidates[2].Quantity));
        Assert.Equal((11L, 1L), (first.Candidates[3].Gid, first.Candidates[3].Quantity));
        Assert.Equal(Step.InspectBuy, first.Candidates[3].Kind);
        // Two bank rows, two buyer rows, then the existing V9 runner.
        Assert.Equal(Step.UseExistingV9Runner, first.Candidates[4].Kind);

        Assert.True(Select(disk, request, Guard(stock, host), first.Candidates[0]));
        // Original V16 contract remains read-only and terminal on this exact
        // revision; the new V17 entry point exposes only remaining choices.
        Assert.Equal(Step.SelectionRecorded, Legacy(disk, request,
            Guard(stock, host)).Step);
        var restarted = Next(disk, request, Guard(stock, host));
        Assert.Equal(4, restarted.Candidates.Count);
        Assert.DoesNotContain(first.Candidates[0], restarted.Candidates);
        Assert.Equal(first.Candidates[0], restarted.RecordedChoice);
        Assert.False(Select(disk, request, Guard(stock, host), first.Candidates[0]));

        Assert.True(Select(disk, request, Guard(stock, host), first.Candidates[1]));
        Assert.True(Select(disk, request, Guard(stock, host), first.Candidates[2]));
        Assert.True(Select(disk, request, Guard(stock, host), first.Candidates[3]));
        var final = Next(disk, request, Guard(stock, host));
        Assert.Single(final.Candidates);
        Assert.Equal(Step.UseExistingV9Runner, final.Candidates[0].Kind);
        Assert.True(Select(disk, request, Guard(stock, host), final.Candidates[0]));
        var terminal = Next(disk, request, Guard(stock, host));
        Assert.Equal(Step.SelectionRecorded, terminal.Step);
        Assert.Empty(terminal.Candidates);
        Assert.Equal(4, disk.Flow.Read(292, 991001)
            .SupplyConsultationsV17.Split('|').Length);
        Assert.Equal(0, stock.Sends);
    }

    [Fact]
    public void Consultation_can_replan_only_after_new_host_revision_and_clears_old_choice()
    {
        using var disk = new Disk();
        var host = new Host();
        var stock = new Stock();
        var request = Order();
        Stage(disk, request, Guard(stock, host));
        var chosen = Next(disk, request, Guard(stock, host)).Candidates[0];
        Assert.True(Select(disk, request, Guard(stock, host), chosen));

        var prior = disk.Flow.Read(292, 991001);
        Assert.False(disk.Flow.TryNotePreflight(Target,
            Journal.RequestFingerprint(request), false,
            "legacy tried to preserve choice after changing Phase"));
        Assert.Equal(prior, disk.Flow.Read(292, 991001));

        // A *local* observation revision allows a new operator preview after
        // an inspection. It does not attest any bank withdrawal or buy.
        host.Revision++;
        stock.Bag[10] = 3;
        Stage(disk, request, Guard(stock, host));
        var next = Next(disk, request, Guard(stock, host));
        Assert.NotEqual(chosen.PlanHash, next.PlanHash);
        Assert.Null(next.RecordedChoice);
        Assert.Empty(disk.Flow.Read(292, 991001).SupplyConsultationsV17);
        Assert.False(Select(disk, request, Guard(stock, host), chosen));
        Assert.Equal(0, stock.Sends);
    }

    [Fact]
    public void Existing_V16_first_choice_can_continue_through_V17_without_replaying_it()
    {
        using var disk = new Disk();
        var host = new Host();
        var stock = new Stock();
        var request = Order();
        Stage(disk, request, Guard(stock, host));
        var legacy = Legacy(disk, request, Guard(stock, host)).Candidates[0];
        Assert.True(V16.TryChooseOnce(Target, request, Guard(stock, host),
            disk.Goals, disk.Flow, disk.Activity, disk.Attempt, Profile, legacy));
        Assert.Equal(Step.SelectionRecorded,
            Legacy(disk, request, Guard(stock, host)).Step);
        var next = Next(disk, request, Guard(stock, host));
        Assert.DoesNotContain(legacy, next.Candidates);
        Assert.Equal(4, next.Candidates.Count);
        Assert.True(Select(disk, request, Guard(stock, host), next.Candidates[0]));
        Assert.False(Select(disk, request, Guard(stock, host), legacy));
        Assert.Equal(0, stock.Sends);
    }

    [Fact]
    public void Same_revision_preflight_never_preserves_a_choice_on_an_unrelated_phase()
    {
        using var disk = new Disk();
        var host = new Host();
        var stock = new Stock();
        var request = Order();
        Stage(disk, request, Guard(stock, host));
        var step = Next(disk, request, Guard(stock, host)).Candidates[0];
        Assert.True(Select(disk, request, Guard(stock, host), step));
        Assert.False(disk.Flow.TryNotePreflight(Target,
            Journal.RequestFingerprint(request), true, "ready after consultation"));
        Assert.False(disk.Flow.TryNotePreflight(Target,
            Journal.RequestFingerprint(request), false, "blocked after consultation"));
        Assert.Equal(step, disk.Flow.Read(292, 991001).SupplyChoiceV16);
        Assert.Equal(WorkflowPhase.Ready, disk.Flow.Read(292, 991001).Phase);
        Assert.Equal(0, stock.Sends);
    }

    [Fact]
    public void Gather_choice_is_terminal_even_if_stock_and_revision_grow_without_receipt()
    {
        using var disk = new Disk();
        var host = new Host();
        var stock = new Stock();
        stock.Bag[10] = 0;
        stock.Bag[11] = 0;
        var request = Order(bank: false, buy10: false, buy11: false);
        Stage(disk, request, Guard(stock, host));
        var choices = Next(disk, request, Guard(stock, host)).Candidates;
        Assert.Equal(2, choices.Count);
        Assert.All(choices, c => Assert.Equal(Step.PrepareExactGatherRoute, c.Kind));
        Assert.True(Select(disk, request, Guard(stock, host), choices[0]));
        host.Revision++;
        stock.Bag[10] = 6;
        var replan = V13.PrepareAndCheckpoint(Target, request,
            Guard(stock, host), disk.Activity.ReadStatus(Owner),
            disk.Attempt.ReadStatus(Profile), disk.Flow, true);
        Assert.False(replan.CheckpointWritten);
        Assert.Equal(Step.SelectionRecorded, Next(disk, request,
            Guard(stock, host)).Step);
        Assert.False(Select(disk, request, Guard(stock, host), choices[1]));
        Assert.Equal(choices[0], disk.Flow.Read(292, 991001).SupplyChoiceV16);
        Assert.Equal(0, stock.Sends);
    }

    [Fact]
    public void Genuine_pending_ActivityRecovery_marker_preempts_new_stock_and_any_selection()
    {
        using var disk = new Disk();
        var host = new Host();
        var stock = new Stock();
        var request = Order();
        Stage(disk, request, Guard(stock, host));
        var choice = Next(disk, request, Guard(stock, host)).Candidates[0];
        var segment = new Segment(RecoveryKind.Gather, 2416,
            Cell: 241, ElementId: 2416);
        var snapshot = new RecoverySnapshot(
            new ResumableRouteMap(191105026, 7), 191105026, 191105026,
            240, PositionConfirmed: true, Interrupted: false,
            Suspended: false, ResourcePresent: true,
            ResourceAvailable: true, AlreadyInReach: true,
            StockGid: 10, StockCount: 1);
        Assert.True(disk.Activity.TryReserve(Owner, segment, snapshot).MayDispatch);
        host.Revision++;
        stock.Bag[10] = 6;
        int before = stock.Reads;
        Assert.Equal(Step.InspectActivityRecovery,
            Next(disk, request, Guard(stock, host)).Step);
        Assert.Equal(before, stock.Reads);
        Assert.False(Select(disk, request, Guard(stock, host), choice));
        Assert.Null(disk.Flow.Read(292, 991001).SupplyChoiceV16);
        Assert.Equal(0, stock.Sends);
    }

    [Fact]
    public void Real_V9_started_then_uncertain_blocks_next_step_even_after_restart()
    {
        using var disk = new Disk();
        var host = new Host();
        var stock = new Stock();
        var request = Order();
        Stage(disk, request, Guard(stock, host));
        var choice = Next(disk, request, Guard(stock, host)).Candidates
            .Single(x => x.Kind == Step.UseExistingV9Runner);
        Assert.True(Select(disk, request, Guard(stock, host), choice));
        var journal = disk.Attempt; // Actual V9 leases are instance-owned.
        Assert.Equal(ProductionDecision.Allowed,
            journal.TryBegin(Profile, 123, request, out var lease));
        Assert.True(disk.Flow.TryBegin(Target,
            Journal.RequestFingerprint(request), lease.RunId));
        Assert.Equal(Step.InspectProductionAttempt,
            Next(disk, request, Guard(stock, host)).Step);
        Assert.True(journal.TryFinish(lease, fullyConfirmed: false));
        Assert.Equal(AttemptPhase.Uncertain, disk.Attempt.ReadStatus(Profile).Phase);
        Assert.Equal(Step.InspectProductionAttempt,
            Next(disk, request, Guard(stock, host)).Step);
        Assert.Equal(choice, disk.Flow.Read(292, 991001).SupplyChoiceV16);
        Assert.False(Select(disk, request, Guard(stock, host), choice));
        Assert.Equal(0, stock.Sends);
    }

    [Fact]
    public void Invalid_source_during_bag_or_chest_IO_revokes_preview_and_rejects_checkpoint()
    {
        using var disk = new Disk();
        var host = new Host();
        var stock = new Stock();
        var request = Order();
        stock.DuringBagRead = () => host.Current = false;
        var guarded = Guard(stock, host);
        var failed = V13.PrepareAndCheckpoint(Target, request, guarded,
            disk.Activity.ReadStatus(Owner), disk.Attempt.ReadStatus(Profile),
            disk.Flow, true);
        Assert.False(failed.EvidenceValid);
        Assert.True(guarded.IsRevoked);
        Assert.Equal(WorkflowPhase.Absent, disk.Flow.Read(292, 991001).Phase);

        host.Current = true;
        stock.DuringBagRead = null;
        stock.DuringChestRead = () => host.Revision++;
        guarded = Guard(stock, host);
        failed = V13.PrepareAndCheckpoint(Target, request, guarded,
            disk.Activity.ReadStatus(Owner), disk.Attempt.ReadStatus(Profile),
            disk.Flow, true);
        Assert.False(failed.EvidenceValid); // source changed during chest read
        Assert.False(failed.CheckpointWritten);
        Assert.Equal(WorkflowPhase.Absent, disk.Flow.Read(292, 991001).Phase);

        stock.DuringChestRead = null;
        stock.ThrowOnBag = true;
        guarded = Guard(stock, host);
        failed = V13.PrepareAndCheckpoint(Target, request, guarded,
            disk.Activity.ReadStatus(Owner), disk.Attempt.ReadStatus(Profile),
            disk.Flow, true);
        Assert.False(failed.EvidenceValid);
        Assert.True(guarded.IsRevoked);
        Assert.Equal(0, stock.Sends);
    }

    [Fact]
    public void Over_max_pods_and_unknown_pods_require_fresh_host_observation()
    {
        using var disk = new Disk();
        var host = new Host { Used = null, Max = null };
        var stock = new Stock();
        var request = Order();
        Stage(disk, request, Guard(stock, host));
        Assert.Equal(Step.InspectPods, Next(disk, request, Guard(stock, host)).Step);
        host.Revision++;
        host.Used = 99;
        host.Max = 100;
        Assert.Equal(Step.InspectPods, Next(disk, request, Guard(stock, host)).Step);
        Assert.Empty(Next(disk, request, Guard(stock, host)).Candidates);
        Assert.Equal(0, stock.Sends);
    }

    [Fact]
    public void Corrupt_anchor_or_changed_durable_goal_never_reuses_a_previous_consultation()
    {
        using var disk = new Disk();
        var host = new Host();
        var stock = new Stock();
        var request = Order();
        Stage(disk, request, Guard(stock, host));
        var inspected = Next(disk, request, Guard(stock, host)).Candidates[0];
        Assert.True(Select(disk, request, Guard(stock, host), inspected));
        Assert.True(disk.Goals.TrySave(Target with { MaxPods = 90 }));
        Assert.Equal(Step.Blocked, Next(disk, request, Guard(stock, host)).Step);
        Assert.False(Select(disk, request, Guard(stock, host), inspected));
        Assert.True(disk.Goals.TrySave(Target));
        File.AppendAllText(disk.WorkflowFile + ".anchor", "corrupt");
        Assert.Equal(Step.InspectProductionAttempt,
            Next(disk, request, Guard(stock, host)).Step);
        Assert.False(Select(disk, request, Guard(stock, host), inspected));
        Assert.Equal(0, stock.Sends);
    }
}
