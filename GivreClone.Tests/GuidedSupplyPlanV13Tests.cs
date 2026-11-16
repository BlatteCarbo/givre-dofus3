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
using Planner = F06App::GivreClone.App.Services.Production.GuidedSupplyPlannerV13;
using Phase = F06App::GivreClone.App.Services.Production.GuidedSupplyPlanPhaseV13;
using Workflow = F06App::GivreClone.App.Services.Production.GuidedProductionWorkflowLedger;
using WorkflowPhase = F06App::GivreClone.App.Services.Production.GuidedProductionWorkflowPhase;
using SafePort = F06App::GivreClone.App.Services.Production.GuidedProductionReadOnlyActions;
using Journal = F06App::GivreClone.App.Services.Production.ItemCraftProductionAttemptJournal;
using JournalPhase = F06App::GivreClone.App.Services.Production.ItemCraftAttemptPhase;
using Snapshot = F06App::GivreClone.App.Services.Production.ItemCraftAttemptSnapshot;
using Recipe = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionRecipe;
using Ingredient = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionInput;
using Request = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionRequest;
using Receipt = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftWorkshopReceipt;
using ActionPort = F06App::GivreClone.App.Services.ItemCraftPlanner.IItemCraftProductionActions;
using StockPort = F06App::GivreClone.App.Services.ItemCraftPlanner.IItemCraftProductionStockObservation;
using Activity = F06App::GivreClone.App.Services.ActivityRecoveryDurableStatus;
using ActivityScope = F06App::GivreClone.App.Services.ActivityRecoveryScope;
using ActivityState = F06App::GivreClone.App.Services.ActivityRecoveryDurableState;
using ActivityAdmission = F06App::GivreClone.App.Services.ActivityRecoveryDurableAdmission;
using ActivitySegment = F06App::GivreClone.App.Services.ActivityRecoverySegment;
using ActivityKind = F06App::GivreClone.App.Services.ActivityRecoveryKind;

namespace GivreClone.Tests;

/// <summary>
/// V13 snapshots are fake LOCAL inventory observations. Positive assertions
/// prove supply arithmetic and durable preview ordering, never a server ACK.
/// </summary>
public sealed class GuidedSupplyPlanV13Tests
{
    private sealed class Sandbox : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(),
            "givre-supply-v13-" + Guid.NewGuid().ToString("N"));
        public string Root => _root;
        public Workflow Open() => new(Path.Combine(_root, "workflow"));
        public string WorkflowFile => Path.Combine(_root, "workflow",
            "server-292", "character-991001.workflow");
        public Journal Attempt() => new(Path.Combine(_root, "attempt",
            "itemcraft.journal"));
        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private const string Profile = "server:292:SupplyV13";
    private static Goal Goal(bool gather = true) =>
        new(292, 991001, 191105026, 7, 320, 1934, 3, 95, gather);
    private static Request Order(bool bank = true, bool buy = true) =>
        new(new Recipe(1934, 1, 12, 0,
            new[] { new Ingredient(10, 2, buy), new Ingredient(11, 1, false) }),
            3, 10_000, bank, null);
    private static Source Observation(long revision = 10, string token = "decoder-7",
        int? usedPods = 40, int? capacityPods = 100) =>
        new(292, 991001, 191105026, 7, token, revision, usedPods, capacityPods);
    private static Activity ActivityStatus(ActivityState phase = ActivityState.Clear) =>
        new(new ActivityScope(292, 991001), phase, null, 0, 0,
            "offline fixture, not a remote receipt");
    private static Snapshot Actual(JournalPhase phase = JournalPhase.None) =>
        new(phase, "offline fixture");

    private static SafePort Safe(FakeSource source,
        Func<Source?>? capture = null, Func<bool>? current = null) =>
        new(source, current ?? (() => true), capture ?? (() => Observation()));

    [Fact]
    public void Exact_bank_and_buy_quantities_come_from_goal_recipe_and_real_readonly_ports()
    {
        using var files = new Sandbox();
        var data = new FakeSource();
        var readOnly = Safe(data);
        var plan = Planner.Evaluate(Goal(), Order(), readOnly,
            ActivityStatus(), Actual(), files.Open().Read(292, 991001));

        Assert.True(plan.EvidenceValid);
        Assert.Equal(Phase.ReadyForExistingRunner, plan.Phase);
        Assert.True(plan.MayOfferExistingRunner);
        Assert.True(plan.BankOptIn);
        Assert.True(plan.BuyerNeeded);
        Assert.False(plan.GatherNeeded);
        Assert.True(plan.PodsObserved);
        Assert.False(plan.CheckpointWritten);
        Assert.Equal(10, plan.ObservationRevision);
        Assert.Equal(64, plan.PlanHash.Length);
        Assert.Equal(2, plan.Rows.Count);
        var ten = Assert.Single(plan.Rows.Where(row => row.Gid == 10));
        Assert.Equal(2, ten.PerCraft);
        Assert.Equal(6, ten.Required);
        Assert.Equal(2, ten.BagObserved);
        Assert.Equal(3, ten.ChestObserved);
        Assert.Equal(3, ten.TakeFromBank);
        Assert.Equal(1, ten.BuyCandidate);
        Assert.Equal(0, ten.GatherDeficit);
        var eleven = Assert.Single(plan.Rows.Where(row => row.Gid == 11));
        Assert.Equal(3, eleven.Required);
        Assert.Equal(3, eleven.BagObserved);
        Assert.Equal(0, eleven.BuyCandidate);
        Assert.Equal(0, data.Mutations);
    }

    [Fact]
    public void Missing_unbuyable_material_never_becomes_a_gather_send_without_admission()
    {
        using var files = new Sandbox();
        var data = new FakeSource();
        data.Bag[11] = 0;
        var plan = Planner.Evaluate(Goal(), Order(), Safe(data),
            ActivityStatus(), Actual(), files.Open().Read(292, 991001));
        Assert.True(plan.EvidenceValid);
        Assert.Equal(Phase.Blocked, plan.Phase);
        Assert.True(plan.GatherNeeded);
        Assert.False(plan.GatherAdmissionPending);
        Assert.False(plan.MayOfferExistingRunner);
        Assert.Equal(3, Assert.Single(plan.Rows.Where(x => x.Gid == 11)).GatherDeficit);
        Assert.Equal(0, data.Mutations);
    }

    [Fact]
    public void No_bank_optin_never_counts_chest_as_authorized_supply()
    {
        using var files = new Sandbox();
        var data = new FakeSource { ChestReadsMustBeZero = true };
        var plan = Planner.Evaluate(Goal(gather: false), Order(bank: false, buy: false),
            Safe(data), ActivityStatus(), Actual(), files.Open().Read(292, 991001));
        Assert.Equal(Phase.Blocked, plan.Phase);
        Assert.True(plan.GatherNeeded);
        var ten = Assert.Single(plan.Rows.Where(x => x.Gid == 10));
        Assert.Equal(0, ten.ChestObserved);
        Assert.Equal(0, ten.TakeFromBank);
        Assert.Equal(4, ten.GatherDeficit);
        Assert.Equal(0, data.ChestReads);
    }

    [Fact]
    public void Buyer_optin_with_no_budget_does_not_mark_the_existing_runner_ready()
    {
        using var files = new Sandbox();
        var request = Order() with { MaxKamas = 0 };
        var data = new FakeSource();
        var plan = Planner.Evaluate(Goal(), request, Safe(data),
            ActivityStatus(), Actual(), files.Open().Read(292, 991001));
        Assert.True(plan.EvidenceValid);
        Assert.True(plan.BuyerNeeded);
        Assert.Equal(Phase.Blocked, plan.Phase);
        Assert.False(plan.MayOfferExistingRunner);
        Assert.Equal(0, data.Mutations);
    }

    [Fact]
    public void Mixed_buy_permissions_for_the_same_ingredient_do_not_widen_optin()
    {
        using var files = new Sandbox();
        var request = Order() with
        {
            Recipe = Order().Recipe with
            {
                Inputs = new[]
                {
                    new Ingredient(10, 1, false),
                    new Ingredient(10, 1, true),
                    new Ingredient(11, 1, false)
                }
            }
        };
        var plan = Planner.Evaluate(Goal(), request, Safe(new FakeSource()),
            ActivityStatus(), Actual(), files.Open().Read(292, 991001));
        Assert.Equal(Phase.Blocked, plan.Phase);
        Assert.False(plan.EvidenceValid);
        Assert.Empty(plan.Rows);
    }

    [Theory]
    [InlineData(96, 100)] // 96% exceeds the operator's 95% cap
    [InlineData(100, 100)]
    public void Observed_pods_over_threshold_block_the_plan(int used, int capacity)
    {
        using var files = new Sandbox();
        var data = new FakeSource();
        var plan = Planner.Evaluate(Goal(), Order(), Safe(data,
                () => Observation(usedPods: used, capacityPods: capacity)),
            ActivityStatus(), Actual(), files.Open().Read(292, 991001));
        Assert.Equal(Phase.Blocked, plan.Phase);
        Assert.False(plan.EvidenceValid);
        Assert.Contains("pods", plan.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, data.Mutations);
    }

    [Fact]
    public void No_pods_observation_cannot_certify_a_gather_candidate()
    {
        using var files = new Sandbox();
        var data = new FakeSource();
        data.Bag[11] = 0;
        var plan = Planner.Evaluate(Goal(), Order(), Safe(data,
                () => Observation(usedPods: null, capacityPods: null)),
            ActivityStatus(), Actual(), files.Open().Read(292, 991001));
        Assert.Equal(Phase.Blocked, plan.Phase);
        Assert.True(plan.GatherNeeded);
        Assert.False(plan.PodsObserved);
        Assert.False(plan.GatherAdmissionPending);
    }

    [Fact]
    public void Existing_durable_gather_admission_is_only_pending_observation_never_a_second_send()
    {
        using var files = new Sandbox();
        var data = new FakeSource();
        data.Bag[11] = 0;
        var segment = new ActivitySegment(ActivityKind.Gather, Target: 44,
            Cell: 320, ElementId: 118);
        var pending = new Activity(new ActivityScope(292, 991001),
            ActivityState.Pending, segment, Goal().SourceMap,
            Goal().SourceMapGeneration, "durable local gather intent");
        var admitted = new ActivityAdmission(true, pending);
        var plan = Planner.Evaluate(Goal(), Order(), Safe(data),
            pending, Actual(), files.Open().Read(292, 991001), admitted);
        Assert.Equal(Phase.AwaitingObservation, plan.Phase);
        Assert.True(plan.GatherAdmissionPending);
        Assert.False(plan.MayOfferExistingRunner);
        Assert.Equal(0, data.Mutations);
        var wrong = admitted with
        {
            Status = pending with { MapGeneration = pending.MapGeneration + 1 }
        };
        var denied = Planner.Evaluate(Goal(), Order(), Safe(data),
            pending, Actual(), files.Open().Read(292, 991001), wrong);
        Assert.Equal(Phase.Blocked, denied.Phase);
        Assert.False(denied.EvidenceValid);
        Assert.Equal(0, data.Mutations);
    }

    [Theory]
    [InlineData(293, 991001)]
    [InlineData(292, 991002)]
    public void Source_identity_mismatch_fails_closed(
        int server, long character)
    {
        using var files = new Sandbox();
        var plan = Planner.Evaluate(Goal(), Order(), Safe(new FakeSource(),
                () => Observation() with
                { ServerId = server, CharacterId = character }),
            ActivityStatus(), Actual(), files.Open().Read(292, 991001));
        Assert.Equal(Phase.Blocked, plan.Phase);
        Assert.False(plan.EvidenceValid);
    }

    [Fact]
    public void New_map_after_route_preserves_origin_goal_and_replans_only_on_newer_revision()
    {
        using var files = new Sandbox();
        var data = new FakeSource();
        var goal = Goal(); // immutable route origin 191105026 / generation 7
        var first = Planner.PrepareAndCheckpoint(goal, Order(), Safe(data),
            ActivityStatus(), Actual(), files.Open(), true);
        Assert.True(first.CheckpointWritten);
        string oldSourceKey = first.SourceKey;
        var currentMap = Observation(11) with
        {
            SourceMap = 191105027,
            SourceMapGeneration = 9
        };
        var second = Planner.PrepareAndCheckpoint(goal, Order(),
            Safe(data, () => currentMap), ActivityStatus(),
            Actual(), files.Open(), true);
        Assert.True(second.CheckpointWritten);
        Assert.NotEqual(oldSourceKey, second.SourceKey);
        Assert.Equal(first.SessionKey, second.SessionKey);
        Assert.Equal(Workflow.Fingerprint(goal), second.GoalHash);
        Assert.Equal(11, files.Open().Read(292, 991001).SupplyObservationRevision);
        var wrongRevision = Planner.Evaluate(goal, Order(),
            Safe(data, () => currentMap with { InventoryRevision = 10 }),
            ActivityStatus(), Actual(), files.Open().Read(292, 991001));
        Assert.Equal(Phase.Blocked, wrongRevision.Phase);
        Assert.False(wrongRevision.EvidenceValid);
        var stale = Planner.PrepareAndCheckpoint(goal, Order(),
            Safe(data, () => currentMap), ActivityStatus(),
            Actual(), files.Open(), true);
        Assert.False(stale.CheckpointWritten);
        Assert.Equal(0, data.Mutations);
    }

    [Fact]
    public void Cache_revision_change_during_ingredient_read_is_rejected_without_sends()
    {
        using var files = new Sandbox();
        var data = new FakeSource();
        long revision = 10;
        data.OnBagRead = () => revision = 11;
        var safe = Safe(data, () => Observation(revision));
        var plan = Planner.Evaluate(Goal(), Order(), safe,
            ActivityStatus(), Actual(), files.Open().Read(292, 991001));
        Assert.Equal(Phase.Blocked, plan.Phase);
        Assert.False(plan.EvidenceValid);
        Assert.Contains("cache", plan.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, data.Mutations);
    }

    [Fact]
    public void Unavailable_inventory_observer_fails_closed_without_raising_an_action()
    {
        using var files = new Sandbox();
        var data = new FakeSource();
        data.OnBagRead = () => throw new InvalidOperationException(
            "synthetic retired inventory");
        var plan = Planner.Evaluate(Goal(), Order(), Safe(data),
            ActivityStatus(), Actual(), files.Open().Read(292, 991001));
        Assert.Equal(Phase.Blocked, plan.Phase);
        Assert.False(plan.EvidenceValid);
        Assert.Equal(0, data.Mutations);
    }

    [Fact]
    public void Durable_preview_is_explicit_and_single_revision_replay_is_forbidden_after_restart()
    {
        using var files = new Sandbox();
        var data = new FakeSource();
        var safe = Safe(data);
        var draft = Planner.PrepareAndCheckpoint(Goal(), Order(), safe,
            ActivityStatus(), Actual(), files.Open(), false);
        Assert.False(draft.CheckpointWritten);
        Assert.Equal(WorkflowPhase.Absent, files.Open().Read(292, 991001).Phase);
        var persisted = Planner.PrepareAndCheckpoint(Goal(), Order(), safe,
            ActivityStatus(), Actual(), files.Open(), true);
        Assert.True(persisted.CheckpointWritten);
        var stored = files.Open().Read(292, 991001);
        Assert.Equal(WorkflowPhase.Ready, stored.Phase);
        Assert.Equal(10, stored.SupplyObservationRevision);
        Assert.Equal(persisted.PlanHash, stored.SupplyPlanHash);
        Assert.Equal(persisted.SourceKey, stored.SupplySourceKey);
        Assert.Equal(Phase.ReadyForExistingRunner,
            stored.SupplyPhase.GetValueOrDefault());
        var rechecked = Planner.Evaluate(Goal(), Order(), safe,
            ActivityStatus(), Actual(), files.Open().Read(292, 991001));
        Assert.True(rechecked.MayOfferExistingRunner);
        Assert.Equal(stored.SupplyPlanHash, rechecked.PlanHash);
        Assert.False(Planner.PrepareAndCheckpoint(Goal(), Order(), safe,
            ActivityStatus(), Actual(), files.Open(), true).CheckpointWritten);
        var newer = Planner.PrepareAndCheckpoint(Goal(), Order(),
            Safe(data, () => Observation(11)), ActivityStatus(),
            Actual(), files.Open(), true);
        Assert.True(newer.CheckpointWritten);
        Assert.Equal(11, files.Open().Read(292, 991001).SupplyObservationRevision);
        Assert.Equal(0, data.Mutations);
    }

    [Fact]
    public void Foreign_source_token_and_old_goal_deny_replay_even_with_new_revision()
    {
        using var files = new Sandbox();
        var data = new FakeSource();
        Assert.True(Planner.PrepareAndCheckpoint(Goal(), Order(), Safe(data),
            ActivityStatus(), Actual(), files.Open(), true).CheckpointWritten);
        var changedToken = Planner.PrepareAndCheckpoint(Goal(), Order(),
            Safe(data, () => Observation(11, "different-epoch")),
            ActivityStatus(), Actual(), files.Open(), true);
        Assert.False(changedToken.CheckpointWritten);
        Assert.Equal(Phase.Blocked, changedToken.Phase);
        Assert.Equal(10, files.Open().Read(292, 991001).SupplyObservationRevision);
        Assert.True(files.Open().TryNotePreflight(Goal(), Journal.RequestFingerprint(Order()),
            false, "re-evaluate without removing v13 witness"));
        Assert.Equal(10, files.Open().Read(292, 991001).SupplyObservationRevision);
    }

    [Fact]
    public void Started_actual_attempt_and_inprogress_workflow_both_block_preview_reemission()
    {
        using var files = new Sandbox();
        var goal = Goal();
        var request = Order();
        var data = new FakeSource();
        var safe = Safe(data);
        Assert.True(Planner.PrepareAndCheckpoint(goal, request, safe,
            ActivityStatus(), Actual(), files.Open(), true).CheckpointWritten);
        var actualJournal = files.Attempt();
        Assert.Equal(F06App::GivreClone.App.Services.Production.ItemCraftAttemptDecision.Allowed,
            actualJournal.TryBegin(Profile, 15, request, out var lease));
        Assert.True(files.Open().TryBegin(goal,
            Journal.RequestFingerprint(request), lease.RunId));
        var later = Planner.PrepareAndCheckpoint(goal, request,
            Safe(data, () => Observation(11)), ActivityStatus(),
            actualJournal.ReadStatus(Profile), files.Open(), true);
        Assert.False(later.CheckpointWritten);
        Assert.Equal(Phase.Blocked, later.Phase);
        Assert.Equal(WorkflowPhase.InProgress, files.Open().Read(292, 991001).Phase);
        Assert.Equal(0, data.Mutations);
    }

    [Fact]
    public void Corrupt_anchor_never_becomes_a_new_ready_stage()
    {
        using var files = new Sandbox();
        var data = new FakeSource();
        Assert.True(Planner.PrepareAndCheckpoint(Goal(), Order(), Safe(data),
            ActivityStatus(), Actual(), files.Open(), true).CheckpointWritten);
        File.AppendAllText(files.WorkflowFile + ".anchor", "corrupt");
        Assert.Equal(WorkflowPhase.Uncertain, files.Open().Read(292, 991001).Phase);
        var result = Planner.PrepareAndCheckpoint(Goal(), Order(),
            Safe(data, () => Observation(11)), ActivityStatus(), Actual(),
            files.Open(), true);
        Assert.False(result.CheckpointWritten);
        Assert.Equal(WorkflowPhase.Uncertain, files.Open().Read(292, 991001).Phase);
    }

    [Fact]
    public async Task Existing_preview_wrapper_still_never_sends_bank_buy_gather_or_craft()
    {
        var source = new FakeSource();
        var guarded = Safe(source);
        Assert.True(guarded.TryReadSupplySource(out _));
        Assert.False(await guarded.OpenChestAsync(CancellationToken.None));
        Assert.Equal(0, await guarded.WithdrawAsync(10, 2, CancellationToken.None));
        Assert.False(await guarded.BuyAsync(10, 1, 100, CancellationToken.None));
        Assert.False((await guarded.CraftAsync(1, 2, Array.Empty<(long, int)>(), 1,
            CancellationToken.None)).Confirmed);
        Assert.False(await guarded.ListAsync(1934, 1, 100, CancellationToken.None));
        Assert.Equal(0, source.Mutations);
    }

    private sealed class FakeSource : ActionPort, StockPort
    {
        public readonly Dictionary<long, int> Bag = new() { [10] = 2, [11] = 3 };
        public readonly Dictionary<long, int> Chest = new() { [10] = 3, [11] = 0 };
        public Action? OnBagRead;
        public bool ChestReadsMustBeZero;
        public int ChestReads;
        public int Mutations;
        public bool SupportsStockObservation => true;
        public bool TryReadBagCount(long gid, out int count)
        {
            OnBagRead?.Invoke();
            return Bag.TryGetValue(gid, out count);
        }
        public int BagCount(long gid) => Bag.GetValueOrDefault(gid);
        public int ChestCount(long gid)
        {
            ChestReads++;
            if (ChestReadsMustBeZero) throw new InvalidOperationException(
                "No chest read when bank opt-in disabled");
            return Chest.GetValueOrDefault(gid);
        }
        public IReadOnlyList<MapInteractive> Interactives() => Array.Empty<MapInteractive>();
        public Task<bool> OpenChestAsync(CancellationToken ct)
        { Mutations++; return Task.FromResult(false); }
        public Task<int> WithdrawAsync(long gid, int quantity, CancellationToken ct)
        { Mutations++; return Task.FromResult(0); }
        public Task<bool> CloseChestAsync(CancellationToken ct)
        { Mutations++; return Task.FromResult(false); }
        public Task<Receipt> CraftAsync(int elementId, int skillInstanceUid,
            IReadOnlyList<(long Gid, int Qty)> ingredients, int count, CancellationToken ct)
        { Mutations++; return Task.FromResult(new Receipt(false, 0, 0)); }
        public Task<long> LotPriceAsync(long gid, int quantity, CancellationToken ct)
        { Mutations++; return Task.FromResult(0L); }
        public Task<bool> BuyAsync(long gid, int quantity, long maxPrice, CancellationToken ct)
        { Mutations++; return Task.FromResult(false); }
        public Task<bool> ListAsync(long gid, int quantity, long price, CancellationToken ct)
        { Mutations++; return Task.FromResult(false); }
    }
}
