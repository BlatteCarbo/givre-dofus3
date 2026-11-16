using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.ItemCraftPlanner;
using GivreClone.App.Services.Production;
using GivreClone.App.Services.Forgemagie;
using GivreClone.App.Services.ProfessionMcp;
using GivreClone.App.Services.ProfessionPlanner;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Offline V16 real entrypoint tests. The host receipt ledger is deliberately
/// synthetic; nothing here proves production has a server receipt authority.
/// </summary>
public sealed class ProfessionRecoveryV16Tests
{
    private const string Version = "3.7.13.17";
    private const string Profile = "server:292:profession-v16";
    private const string Subject = "character-v16";
    private const string Session = "session-v16";
    private static readonly string Manifest = new('A', 64);

    private static ProfessionMcpSnapshot Catalog() => new(
        new ProfessionPlanRequest(new ProfessionGoal(7, "profession", 20, 20),
            TrainingRecipeId: 120, RequestedCrafts: 2,
            Recipes: new[] { new ProfessionRecipe(120, 200, 1,
                new[] { new RecipeIngredient(10, 2) }, 20, 7,
                SourceVersion: Version, Verified: true, SecondsPerCraft: 1,
                KamasPerCraft: 0) },
            Inventory: Array.Empty<ProfessionInventoryStack>(),
            Sources: Array.Empty<MaterialSource>()),
        new ProfessionMcpProvenance("local-owned", "v16-fixture", Version,
            Manifest, true, "independent-review"));

    private static ItemCraftProductionRequest Order(int quantity = 2) => new(
        new ItemCraftProductionRecipe(200, 1, 10, 0,
            new[] { new ItemCraftProductionInput(10, 2, false) }),
        quantity, 0, BankAllowed: false, SaleFloor: null);

    private static ProfessionMcpRecoveryEnvelope Evidence(long revision,
        long input, long output, long remainingSource = 0, bool complete = true)
    {
        var stacks = new List<ProfessionInventoryStack>();
        if (input > 0) stacks.Add(new("input", 10, input, "bag"));
        if (output > 0) stacks.Add(new("result", 200, output, "bag"));
        var sources = new[] { new MaterialSource(10, AcquisitionMethod.Gather,
            1, 0, Version, true, MaxUnits: remainingSource) };
        var observed = new ProfessionRecoveryObservation(Subject, revision, Version,
            complete, complete, stacks, sources);
        return new(observed, new ProfessionMcpRecoveryReceipt(Subject, Session,
            revision, Version, Manifest,
            ProfessionMcpRecoveryIntegration.InventoryDigest(stacks),
            ProfessionMcpRecoveryIntegration.SourceCapsDigest(sources),
            "observation-v16-" + revision));
    }

    private sealed class Fixture : IDisposable, IProfessionProductionV9Evidence,
        IItemCraftProductionStockObservation
    {
        private readonly Dictionary<string, ProfessionMcpRecoveryReceipt> _observationLedger = new();
        private readonly Dictionary<string, ProfessionProductionCraftWitness> _craftLedger = new();
        private readonly Dictionary<string, ProfessionProductionV9CraftReceipt> _v9Ledger = new();
        private readonly Dictionary<string, ProfessionProductionAcquisitionReceipt> _acquisitionLedger = new();
        private readonly Dictionary<long, int> _bag = new();
        private readonly string _root = Path.Combine(Path.GetTempPath(),
            "profession-recovery-v16-" + Guid.NewGuid().ToString("N"));

        public Fixture()
        {
            Directory.CreateDirectory(_root);
            V9Journal = new ItemCraftProductionAttemptJournal(Path.Combine(_root, "v9.journal"));
            Journal = new ProfessionProductionFileJournal(Path.Combine(_root, "profession.json"));
        }
        public void Dispose() => Directory.Delete(_root, recursive: true);

        public ProfessionProductionFileJournal Journal { get; }
        public ItemCraftProductionAttemptJournal V9Journal { get; }
        public string V9Profile => Profile;
        public bool SupportsStockObservation => true;
        public int CraftCalls { get; private set; }
        public int OtherCalls { get; private set; }
        public bool ReceiptReaderConnected = true;
        public Action? OnReadContext;
        public ProfessionMcpRecoveryEnvelope? Current;

        public void Observe(ProfessionMcpRecoveryEnvelope evidence)
        {
            Current = evidence;
            _observationLedger[evidence.Receipt.ReceiptId] = evidence.Receipt;
            _bag.Clear();
            foreach (var stack in evidence.Observation.Inventory)
                _bag[stack.ItemGid] = checked(_bag.GetValueOrDefault(stack.ItemGid) +
                    (int)stack.Quantity);
        }

        public ProfessionProductionGuidance Prepare(bool activate) =>
            new ItemCraftProductionRun().PrepareVerifiedNextAction(Catalog(), Order(),
                this, Journal, activate);

        public void AddCraft(ProfessionProductionPendingAction pending,
            ItemCraftAttemptLease lease, long observationRevision, int actualQuantity,
            string receiptId, int? attemptQuantity = null)
        {
            _craftLedger[pending.ActionKey] = new ProfessionProductionCraftWitness(
                pending.ActionKey, Session, observationRevision, pending.RecipeId!.Value,
                pending.ItemGid, actualQuantity, receiptId);
            _v9Ledger[pending.ActionKey] = new ProfessionProductionV9CraftReceipt(
                pending.ActionKey, Subject, Session, observationRevision,
                pending.RecipeId.Value, receiptId, lease.Profile, lease.RunId, lease.Pid,
                lease.RequestHash, new ItemCraftWorkshopReceipt(true, actualQuantity,
                    pending.ItemGid), attemptQuantity);
        }

        public void AddAcquisition(ProfessionProductionPendingAction pending,
            long revision, long confirmedQuantity, string receiptId)
        {
            _acquisitionLedger[pending.ActionKey] = new ProfessionProductionAcquisitionReceipt(
                pending.ActionKey, Subject, Session, revision, pending.Kind,
                pending.ItemGid, confirmedQuantity, receiptId);
        }

        public ProfessionMcpRecoveryContext ReadRecoveryContext()
        {
            OnReadContext?.Invoke();
            return new(true, Current, ReceiptReaderConnected
                ? receipt => _observationLedger.TryGetValue(receipt.ReceiptId, out var old) && old == receipt
                : null);
        }
        public ProfessionProductionCraftWitness? ReadCraftWitness(string actionKey) =>
            ReceiptReaderConnected ? _craftLedger.GetValueOrDefault(actionKey) : null;
        public bool VerifyCraftWitness(ProfessionProductionCraftWitness witness) =>
            ReceiptReaderConnected && _craftLedger.TryGetValue(witness.ActionKey, out var old) &&
            old == witness;
        public ProfessionProductionV9CraftReceipt? ReadV9CraftReceipt(string actionKey) =>
            ReceiptReaderConnected ? _v9Ledger.GetValueOrDefault(actionKey) : null;
        public bool VerifyV9CraftReceipt(ProfessionProductionV9CraftReceipt receipt) =>
            ReceiptReaderConnected && _v9Ledger.TryGetValue(receipt.ActionKey, out var old) &&
            old == receipt;
        public ProfessionProductionAcquisitionReceipt? ReadAcquisitionReceipt(string actionKey) =>
            ReceiptReaderConnected ? _acquisitionLedger.GetValueOrDefault(actionKey) : null;
        public bool VerifyAcquisitionReceipt(ProfessionProductionAcquisitionReceipt receipt) =>
            ReceiptReaderConnected && _acquisitionLedger.TryGetValue(receipt.ActionKey, out var old) &&
            old == receipt;

        public int BagCount(long gid) => _bag.GetValueOrDefault(gid);
        public bool TryReadBagCount(long gid, out int count)
        {
            count = BagCount(gid);
            return true;
        }
        public int ChestCount(long gid) => 0;
        public IReadOnlyList<MapInteractive> Interactives() => new[]
        {
            new MapInteractive(1, 3, 99, 0,
                new[] { new InteractiveSkill(9, 10, 0, true) }, "Atelier")
        };
        public Task<bool> OpenChestAsync(CancellationToken ct)
        { OtherCalls++; return Task.FromResult(false); }
        public Task<int> WithdrawAsync(long gid, int qty, CancellationToken ct)
        { OtherCalls++; return Task.FromResult(0); }
        public Task<bool> CloseChestAsync(CancellationToken ct)
        { OtherCalls++; return Task.FromResult(false); }
        public Task<long> LotPriceAsync(long gid, int qty, CancellationToken ct) =>
            Task.FromResult(0L);
        public Task<bool> BuyAsync(long gid, int qty, long maxPrice, CancellationToken ct)
        { OtherCalls++; return Task.FromResult(false); }
        public Task<ItemCraftWorkshopReceipt> CraftAsync(int elementId, int uid,
            IReadOnlyList<(long Gid, int Qty)> ingredients, int count, CancellationToken ct)
        {
            CraftCalls++;
            foreach (var ingredient in ingredients)
                _bag[ingredient.Gid] -= ingredient.Qty;
            _bag[200] = _bag.GetValueOrDefault(200) + 1;
            return Task.FromResult(new ItemCraftWorkshopReceipt(true, 1, 200));
        }
        public Task<bool> ListAsync(long gid, int qty, long price, CancellationToken ct)
        { OtherCalls++; return Task.FromResult(false); }
    }

    [Fact]
    public async Task Partial_real_v9_craft_replans_then_second_confirmed_run_completes_target()
    {
        using var f = new Fixture();
        f.Observe(Evidence(1, input: 4, output: 0));
        var first = f.Prepare(activate: true);
        Assert.Equal("GUIDED_ACTION_CHECKPOINTED", first.Diagnostic);
        Assert.Equal(2, first.NextAction!.Quantity);
        Assert.Equal("CRAFT", first.NextAction.Kind);

        Assert.Equal(ItemCraftAttemptDecision.Allowed,
            f.V9Journal.TryBegin(Profile, 370, Order(1), out var lease1));
        var result1 = await new ItemCraftProductionRun().RunAsync(Order(1), f, null,
            CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.QuantityReached, result1.Stop);
        Assert.True(f.V9Journal.TryFinish(lease1, fullyConfirmed: true));
        f.Observe(Evidence(2, input: 2, output: 1));
        f.AddCraft(first.NextAction, lease1, 2, 1, "unique-v16-receipt-1", 1);

        var second = f.Prepare(activate: true);
        Assert.Equal("GUIDED_ACTION_CHECKPOINTED", second.Diagnostic);
        Assert.Equal(1, second.NextAction!.Quantity);
        Assert.Equal("CRAFT", second.NextAction.Kind);
        Assert.Equal(new[] { "unique-v16-receipt-1" },
            f.Journal.Read().Entry!.ConsumedReceiptIds);
        Assert.Equal(new[] { lease1.RunId }, f.Journal.Read().Entry!.ConsumedV9RunIds);

        Assert.Equal(ItemCraftAttemptDecision.Allowed,
            f.V9Journal.TryBegin(Profile, 371, Order(1), out var lease2));
        var result2 = await new ItemCraftProductionRun().RunAsync(Order(1), f, null,
            CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.QuantityReached, result2.Stop);
        Assert.True(f.V9Journal.TryFinish(lease2, fullyConfirmed: true));
        f.Observe(Evidence(3, input: 0, output: 2));
        f.AddCraft(second.NextAction, lease2, 3, 1, "unique-v16-receipt-2", 1);

        var finished = f.Prepare(activate: true);
        Assert.Equal("TARGET_OBSERVED_CHECKPOINTED", finished.Diagnostic);
        Assert.Null(f.Journal.Read().Entry!.Pending);
        Assert.Equal(2, f.Journal.Read().Entry!.ConsumedReceiptIds!.Count);
        Assert.Equal(2, f.Journal.Read().Entry!.ConsumedV9RunIds!.Count);
        Assert.Equal(2, f.CraftCalls);
        Assert.Equal(0, f.OtherCalls);
    }

    [Fact]
    public void Completed_run_cannot_be_reused_by_later_action_with_new_receipt_id()
    {
        using var f = new Fixture();
        f.Observe(Evidence(1, 4, 0));
        var first = f.Prepare(true).NextAction!;
        Assert.Equal(ItemCraftAttemptDecision.Allowed,
            f.V9Journal.TryBegin(Profile, 372, Order(1), out var lease));
        Assert.True(f.V9Journal.TryFinish(lease, fullyConfirmed: true));
        f.Observe(Evidence(2, 2, 1));
        f.AddCraft(first, lease, 2, 1, "receipt-A", 1);
        var second = f.Prepare(true).NextAction!;
        Assert.Equal("CRAFT", second.Kind);

        // A late callback from the old completed V9 run is given a new ID by a
        // faulty adapter. Independent verification alone must not permit reuse.
        f.Observe(Evidence(3, 0, 2));
        f.AddCraft(second, lease, 3, 1, "receipt-B", 1);
        Assert.Equal("V9_ATTEMPT_REPLAY", f.Prepare(true).Diagnostic);
        Assert.Equal(2, f.Journal.Read().Sequence);
        Assert.Equal(new[] { "receipt-A" }, f.Journal.Read().Entry!.ConsumedReceiptIds);
    }

    [Fact]
    public void Old_receipt_id_replayed_on_a_distinct_v9_run_fails_closed()
    {
        using var f = new Fixture();
        f.Observe(Evidence(1, 4, 0));
        var first = f.Prepare(true).NextAction!;
        Assert.Equal(ItemCraftAttemptDecision.Allowed,
            f.V9Journal.TryBegin(Profile, 373, Order(1), out var lease1));
        Assert.True(f.V9Journal.TryFinish(lease1, fullyConfirmed: true));
        f.Observe(Evidence(2, 2, 1));
        f.AddCraft(first, lease1, 2, 1, "receipt-used", 1);
        var second = f.Prepare(true).NextAction!;

        Assert.Equal(ItemCraftAttemptDecision.Allowed,
            f.V9Journal.TryBegin(Profile, 374, Order(1), out var lease2));
        Assert.True(f.V9Journal.TryFinish(lease2, fullyConfirmed: true));
        f.Observe(Evidence(3, 0, 2));
        f.AddCraft(second, lease2, 3, 1, "receipt-used", 1);
        Assert.Equal("CERTIFIED_RECEIPT_REPLAY", f.Prepare(true).Diagnostic);
        Assert.Equal(2, f.Journal.Read().Sequence);
    }

    [Fact]
    public void Crash_started_attempt_late_receipt_and_disconnected_reader_cannot_advance()
    {
        using var f = new Fixture();
        f.Observe(Evidence(1, 4, 0));
        var first = f.Prepare(true).NextAction!;
        Assert.Equal(ItemCraftAttemptDecision.Allowed,
            f.V9Journal.TryBegin(Profile, 375, Order(1), out var crashedLease));
        f.Observe(Evidence(2, 2, 1));
        f.AddCraft(first, crashedLease, 2, 1, "late-callback", 1);
        Assert.Equal("V9_CRAFT_RECEIPT_OR_ATTEMPT_UNVERIFIED", f.Prepare(true).Diagnostic);
        Assert.Equal(ItemCraftAttemptPhase.Started,
            f.V9Journal.ReadStatus(Profile).Phase);

        f.ReceiptReaderConnected = false;
        Assert.Equal("CERTIFIED_OBSERVATION_UNAVAILABLE", f.Prepare(true).Diagnostic);
        Assert.Equal(1, f.Journal.Read().Sequence);
        Assert.Equal(0, f.CraftCalls);
        Assert.Equal(0, f.OtherCalls);
    }

    [Fact]
    public void Incomplete_stock_revision_never_commits_or_consumes_receipt()
    {
        using var f = new Fixture();
        f.Observe(Evidence(1, 4, 0));
        var first = f.Prepare(true).NextAction!;
        Assert.Equal(ItemCraftAttemptDecision.Allowed,
            f.V9Journal.TryBegin(Profile, 376, Order(1), out var lease));
        Assert.True(f.V9Journal.TryFinish(lease, fullyConfirmed: true));
        f.AddCraft(first, lease, 2, 1, "partial-observation", 1);
        f.Observe(Evidence(2, 2, 1, complete: false));
        Assert.Equal("BAG_SNAPSHOT_MISMATCH", f.Prepare(true).Diagnostic);
        Assert.Equal(1, f.Journal.Read().Sequence);

        f.Observe(Evidence(2, 2, 1));
        Assert.Equal("GUIDED_ACTION_CHECKPOINTED", f.Prepare(true).Diagnostic);
        Assert.Equal("partial-observation", f.Journal.Read().Entry!.ConsumedReceiptIds!.Single());
    }

    [Fact]
    public void Partial_gather_needs_attributed_receipt_and_replayed_acquisition_is_denied()
    {
        using var f = new Fixture();
        f.Observe(Evidence(1, input: 0, output: 0, remainingSource: 4));
        var first = f.Prepare(true).NextAction!;
        Assert.Equal("GATHER", first.Kind);
        Assert.Equal(4, first.Quantity);
        f.Observe(Evidence(2, input: 2, output: 0, remainingSource: 2));
        Assert.Equal("V9_ACQUISITION_RECEIPT_UNVERIFIED", f.Prepare(true).Diagnostic);
        f.AddAcquisition(first, 2, 2, "one-gather-receipt");
        var second = f.Prepare(true).NextAction!;
        Assert.Equal("GATHER", second.Kind);
        Assert.Equal(2, second.Quantity);
        Assert.Equal("one-gather-receipt", f.Journal.Read().Entry!.ConsumedReceiptIds!.Single());

        f.Observe(Evidence(3, input: 4, output: 0, remainingSource: 0));
        f.AddAcquisition(second, 3, 2, "one-gather-receipt");
        Assert.Equal("CERTIFIED_RECEIPT_REPLAY", f.Prepare(true).Diagnostic);
        Assert.Equal(2, f.Journal.Read().Sequence);

        f.AddAcquisition(second, 3, 2, "second-gather-receipt");
        var next = f.Prepare(true);
        Assert.Equal("GUIDED_ACTION_CHECKPOINTED", next.Diagnostic);
        Assert.Equal("CRAFT", next.NextAction!.Kind);
        Assert.Equal(new[] { "one-gather-receipt", "second-gather-receipt" },
            f.Journal.Read().Entry!.ConsumedReceiptIds);
        Assert.Equal(0, f.CraftCalls);
        Assert.Equal(0, f.OtherCalls);
    }

    [Fact]
    public void Host_revision_changed_during_prepare_blocks_checkpoint_before_cas()
    {
        using var f = new Fixture();
        f.Observe(Evidence(1, 4, 0));
        int reads = 0;
        f.OnReadContext = () =>
        {
            if (++reads == 2)
                f.Observe(Evidence(2, 2, 1));
        };
        var result = f.Prepare(true);
        Assert.Equal("CERTIFIED_EVIDENCE_CHANGED_BEFORE_COMMIT", result.Diagnostic);
        Assert.False(result.Written);
        Assert.Equal(ProfessionProductionJournalStatus.Empty, f.Journal.Read().Status);
        Assert.Equal(0, f.CraftCalls);
    }

    [Fact]
    public void Durable_cas_refuses_truncated_receipt_or_run_history_after_restart()
    {
        using var f = new Fixture();
        f.Observe(Evidence(1, 4, 0));
        var first = f.Prepare(true).NextAction!;
        Assert.Equal(ItemCraftAttemptDecision.Allowed,
            f.V9Journal.TryBegin(Profile, 388, Order(1), out var lease));
        Assert.True(f.V9Journal.TryFinish(lease, fullyConfirmed: true));
        f.Observe(Evidence(2, 2, 1));
        f.AddCraft(first, lease, 2, 1, "history-bound", 1);
        Assert.Equal("GUIDED_ACTION_CHECKPOINTED", f.Prepare(true).Diagnostic);

        var saved = f.Journal.Read();
        Assert.Equal(2, saved.Sequence);
        var candidate = saved.Entry! with
        {
            Sequence = saved.Sequence + 1,
            ConsumedReceiptIds = Array.Empty<string>()
        };
        Assert.Equal(ProfessionProductionJournalStatus.Corrupt,
            f.Journal.CompareExchange(saved.Sequence, candidate));
        Assert.Equal(saved.Sequence, f.Journal.Read().Sequence);

        candidate = saved.Entry! with
        {
            Sequence = saved.Sequence + 1,
            ConsumedV9RunIds = Array.Empty<Guid>()
        };
        Assert.Equal(ProfessionProductionJournalStatus.Corrupt,
            f.Journal.CompareExchange(saved.Sequence, candidate));
        Assert.Equal(saved.Sequence, f.Journal.Read().Sequence);
        Assert.Equal("history-bound", f.Journal.Read().Entry!.ConsumedReceiptIds!.Single());
    }

    [Fact]
    public void Preview_does_not_commit_and_execution_admission_remains_false()
    {
        using var f = new Fixture();
        f.Observe(Evidence(1, 4, 0));
        var preview = f.Prepare(false);
        Assert.Equal("NEXT_ACTION_REQUIRES_EXPLICIT_OPT_IN", preview.Diagnostic);
        Assert.False(preview.Written);
        Assert.Equal(ProfessionProductionJournalStatus.Empty, f.Journal.Read().Status);
        Assert.False(ProfessionProductionWorkflow.InspectExecutionAdmission(f, f.Journal).CanExecute);

        var optedIn = f.Prepare(true);
        Assert.Equal("GUIDED_ACTION_CHECKPOINTED", optedIn.Diagnostic);
        var status = ProfessionProductionWorkflow.InspectExecutionAdmission(f, f.Journal);
        Assert.Equal("V9_INDEPENDENT_MUTATION_LEASE_UNAVAILABLE", status.Diagnostic);
        Assert.False(status.CanExecute);
        Assert.Equal(optedIn.NextAction!.ActionKey, status.PendingActionKey);
        Assert.False(ProfessionProductionWorkflow.InspectExecutionAdmission(null, f.Journal).CanExecute);
        Assert.Equal(0, f.CraftCalls);
        Assert.Equal(0, f.OtherCalls);
    }

    [Fact]
    public void Forge_preview_uses_existing_pure_advisor_and_never_exposes_a_send_lease()
    {
        var lines = new[] { new ForgeLine("Force", 10, 0, 40, 15) };
        var stock = new Dictionary<(string Stat, string Tier), int>
        {
            [("Force", "Ba")] = 1
        };
        var preview = ProfessionProductionWorkflow.PreviewForge(lines, stock);
        Assert.Equal("FORGE_ADVISORY_ONLY", preview.Diagnostic);
        Assert.Equal(AdviceKind.Rune, preview.Advice!.Kind);
        Assert.False(preview.CanExecute);

        var malformed = ProfessionProductionWorkflow.PreviewForge(lines,
            new Dictionary<(string Stat, string Tier), int> { [("Force", "Ba")] = -1 });
        Assert.Equal("FORGE_INPUTS_UNVERIFIED", malformed.Diagnostic);
        Assert.Null(malformed.Advice);
        Assert.False(malformed.CanExecute);
    }
}
