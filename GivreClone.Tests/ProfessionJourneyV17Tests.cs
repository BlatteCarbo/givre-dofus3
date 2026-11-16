using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.Forgemagie;
using GivreClone.App.Services.ItemCraftPlanner;
using GivreClone.App.Services.Production;
using GivreClone.App.Services.ProfessionMcp;
using GivreClone.App.Services.ProfessionPlanner;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Real profession Prepare / V9 runner / fsynced journal entrypoint tests.
/// Receipt authorities are host fixtures, never a claim of live server ACK.
/// </summary>
public sealed class ProfessionJourneyV17Tests
{
    private const string Version = "3.7.13.17";
    private const string Profile = "server:292:profession-v17";
    private const string Subject = "character-profession-v17";
    private const string Session = "session-v17";
    private static readonly string Manifest = new('A', 64);

    private static ProfessionMcpSnapshot Catalog(int quantity = 1,
        bool nested = false) => new(
        new ProfessionPlanRequest(new ProfessionGoal(7, "Profession", 20, 20),
            TrainingRecipeId: 120, RequestedCrafts: quantity,
            Recipes: nested
                ? new ProfessionRecipe[]
                {
                    new(120, 200, 1, new[] { new RecipeIngredient(10, 2) },
                        20, 7, SourceVersion: Version, Verified: true,
                        SecondsPerCraft: 1, KamasPerCraft: 0),
                    new(121, 10, 1, new[] { new RecipeIngredient(5, 1) },
                        20, 7, SourceVersion: Version, Verified: true,
                        SecondsPerCraft: 1, KamasPerCraft: 0)
                }
                : new[]
                {
                    new ProfessionRecipe(120, 200, 1,
                        new[] { new RecipeIngredient(10, 2) }, 20, 7,
                        SourceVersion: Version, Verified: true,
                        SecondsPerCraft: 1, KamasPerCraft: 0)
                },
            Inventory: Array.Empty<ProfessionInventoryStack>(),
            Sources: Array.Empty<MaterialSource>()),
        new ProfessionMcpProvenance("local-owned", "v17-fixture", Version,
            Manifest, true, "independent-review"));

    private static ItemCraftProductionRequest Order(int quantity = 1) => new(
        new ItemCraftProductionRecipe(200, 1, 10, 0,
            new[] { new ItemCraftProductionInput(10, 2, false) }),
        quantity, MaxKamas: 0, BankAllowed: false, SaleFloor: null);

    private static ProfessionMcpRecoveryEnvelope Evidence(long revision,
        long input, long output, long capacity = 0, long precursor = 0,
        bool includeGatherSource = true)
    {
        var bag = new List<ProfessionInventoryStack>();
        if (input > 0) bag.Add(new("input-stack", 10, input, "bag"));
        if (output > 0) bag.Add(new("output-stack", 200, output, "bag"));
        if (precursor > 0) bag.Add(new("precursor-stack", 5, precursor, "bag"));
        IReadOnlyList<MaterialSource> sources = includeGatherSource
            ? new[] { new MaterialSource(10, AcquisitionMethod.Gather,
                1, 0, Version, true, MaxUnits: capacity) }
            : Array.Empty<MaterialSource>();
        var observed = new ProfessionRecoveryObservation(Subject, revision,
            Version, true, true, bag, sources);
        var receipt = new ProfessionMcpRecoveryReceipt(Subject, Session,
            revision, Version, Manifest,
            ProfessionMcpRecoveryIntegration.InventoryDigest(bag),
            ProfessionMcpRecoveryIntegration.SourceCapsDigest(sources),
            "observation-v17-" + revision);
        return new(observed, receipt);
    }

    private sealed class Host : IDisposable, IProfessionProductionV9Evidence,
        IItemCraftProductionStockObservation
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(),
            "givre-profession-v17-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, ProfessionMcpRecoveryReceipt> _observations = new();
        private readonly Dictionary<string, ProfessionProductionAcquisitionReceipt> _acquisitions = new();
        private readonly Dictionary<string, ProfessionProductionCraftWitness> _crafts = new();
        private readonly Dictionary<string, ProfessionProductionV9CraftReceipt> _craftLinks = new();
        private readonly Dictionary<long, int> _bag = new();

        public Host()
        {
            Directory.CreateDirectory(_root);
            Journal = new ProfessionProductionFileJournal(Path.Combine(_root, "guidance.json"));
            V9Journal = new ItemCraftProductionAttemptJournal(Path.Combine(_root, "v9.journal"));
        }

        public ProfessionProductionFileJournal Journal { get; }
        public ItemCraftProductionAttemptJournal V9Journal { get; }
        public string V9Profile => Profile;
        public bool SupportsStockObservation => true;
        public bool Enabled = true;
        public bool ReceiptReaderReady = true;
        public Action? OnRecoveryRead;
        public int CraftCalls;
        public int OtherGameplayCalls;
        public ProfessionMcpRecoveryEnvelope? Current;

        public void Observe(ProfessionMcpRecoveryEnvelope envelope)
        {
            Current = envelope;
            _observations[envelope.Receipt.ReceiptId] = envelope.Receipt;
            _bag.Clear();
            foreach (var stack in envelope.Observation.Inventory)
                _bag[stack.ItemGid] = checked(_bag.GetValueOrDefault(stack.ItemGid) +
                    (int)stack.Quantity);
        }

        public ProfessionProductionGuidance Prepare(int quantity = 1, bool activate = true,
            ProfessionMcpSnapshot? catalog = null) =>
            new ItemCraftProductionRun().PrepareVerifiedNextAction(catalog ?? Catalog(quantity),
                Order(quantity), this, Journal, activate);

        public ProfessionProductionJourneyStatus Inspect(int quantity = 1,
            ProfessionMcpSnapshot? catalog = null) =>
            ProfessionProductionWorkflow.InspectJourney(catalog ?? Catalog(quantity),
                Order(quantity), this, Journal);

        public void Acquisition(ProfessionProductionPendingAction action,
            long revision, long count, string receiptId)
        {
            _acquisitions[action.ActionKey] = new ProfessionProductionAcquisitionReceipt(
                action.ActionKey, Subject, Session, revision, action.Kind,
                action.ItemGid, count, receiptId);
        }

        public void Craft(ProfessionProductionPendingAction action, ItemCraftAttemptLease lease,
            long revision, int count, string receiptId, int? attempted = null)
        {
            _crafts[action.ActionKey] = new ProfessionProductionCraftWitness(
                action.ActionKey, Session, revision, action.RecipeId!.Value,
                action.ItemGid, count, receiptId);
            _craftLinks[action.ActionKey] = new ProfessionProductionV9CraftReceipt(
                action.ActionKey, Subject, Session, revision, action.RecipeId.Value,
                receiptId, lease.Profile, lease.RunId, lease.Pid, lease.RequestHash,
                new ItemCraftWorkshopReceipt(true, count, action.ItemGid), attempted);
        }

        public ProfessionMcpRecoveryContext ReadRecoveryContext()
        {
            OnRecoveryRead?.Invoke();
            return new(Enabled, Current, ReceiptReaderReady
                ? receipt => _observations.TryGetValue(receipt.ReceiptId, out var prior) && prior == receipt
                : null);
        }
        public ProfessionProductionAcquisitionReceipt? ReadAcquisitionReceipt(string actionKey) =>
            ReceiptReaderReady ? _acquisitions.GetValueOrDefault(actionKey) : null;
        public bool VerifyAcquisitionReceipt(ProfessionProductionAcquisitionReceipt receipt) =>
            ReceiptReaderReady && _acquisitions.TryGetValue(receipt.ActionKey, out var prior) &&
            prior == receipt;
        public ProfessionProductionCraftWitness? ReadCraftWitness(string actionKey) =>
            ReceiptReaderReady ? _crafts.GetValueOrDefault(actionKey) : null;
        public bool VerifyCraftWitness(ProfessionProductionCraftWitness witness) =>
            ReceiptReaderReady && _crafts.TryGetValue(witness.ActionKey, out var prior) &&
            prior == witness;
        public ProfessionProductionV9CraftReceipt? ReadV9CraftReceipt(string actionKey) =>
            ReceiptReaderReady ? _craftLinks.GetValueOrDefault(actionKey) : null;
        public bool VerifyV9CraftReceipt(ProfessionProductionV9CraftReceipt receipt) =>
            ReceiptReaderReady && _craftLinks.TryGetValue(receipt.ActionKey, out var prior) &&
            prior == receipt;

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
        { OtherGameplayCalls++; return Task.FromResult(false); }
        public Task<int> WithdrawAsync(long gid, int quantity, CancellationToken ct)
        { OtherGameplayCalls++; return Task.FromResult(0); }
        public Task<bool> CloseChestAsync(CancellationToken ct)
        { OtherGameplayCalls++; return Task.FromResult(false); }
        public Task<long> LotPriceAsync(long gid, int quantity, CancellationToken ct) =>
            Task.FromResult(0L);
        public Task<bool> BuyAsync(long gid, int quantity, long maxPrice, CancellationToken ct)
        { OtherGameplayCalls++; return Task.FromResult(false); }
        public Task<ItemCraftWorkshopReceipt> CraftAsync(int elementId, int uid,
            IReadOnlyList<(long Gid, int Qty)> ingredients, int count, CancellationToken ct)
        {
            CraftCalls++;
            foreach (var ingredient in ingredients)
                _bag[ingredient.Gid] -= ingredient.Qty;
            _bag[200] = _bag.GetValueOrDefault(200) + 1;
            return Task.FromResult(new ItemCraftWorkshopReceipt(true, 1, 200));
        }
        public Task<bool> ListAsync(long gid, int quantity, long price, CancellationToken ct)
        { OtherGameplayCalls++; return Task.FromResult(false); }
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Goal_gather_craft_journey_uses_real_v9_and_never_executes_from_preview()
    {
        using var host = new Host();
        host.Observe(Evidence(1, 0, 0, capacity: 2));
        var gather = host.Prepare();
        Assert.Equal("GUIDED_ACTION_CHECKPOINTED", gather.Diagnostic);
        Assert.Equal("GATHER", gather.NextAction!.Kind);
        Assert.Equal(2, gather.NextAction.Quantity);
        Assert.False(host.Inspect().CanExecute);
        Assert.Equal("GATHER", host.Inspect().PendingKind);
        Assert.Equal(0, host.CraftCalls);

        host.Observe(Evidence(2, 2, 0, capacity: 0));
        Assert.Equal("V9_ACQUISITION_RECEIPT_UNVERIFIED", host.Inspect().Diagnostic);
        host.Acquisition(gather.NextAction, 2, 2, "gather-receipt-v17");
        Assert.Equal("NEXT_ACTION_REQUIRES_EXPLICIT_OPT_IN", host.Inspect().Diagnostic);
        Assert.False(host.Prepare(activate: false).Written);
        var craft = host.Prepare();
        Assert.Equal("GUIDED_ACTION_CHECKPOINTED", craft.Diagnostic);
        Assert.Equal("CRAFT", craft.NextAction!.Kind);
        Assert.Equal(2, host.Journal.Read().Sequence);

        Assert.Equal(ItemCraftAttemptDecision.Allowed,
            host.V9Journal.TryBegin(Profile, 701, Order(), out var lease));
        var produced = await new ItemCraftProductionRun().RunAsync(Order(), host,
            null, CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.QuantityReached, produced.Stop);
        Assert.True(host.V9Journal.TryFinish(lease, fullyConfirmed: true));
        host.Observe(Evidence(3, 0, 1));
        host.Craft(craft.NextAction, lease, 3, 1, "craft-receipt-v17");

        Assert.Equal("TARGET_ALREADY_IN_OBSERVED_STOCK", host.Inspect().Diagnostic);
        var complete = host.Prepare();
        Assert.Equal("TARGET_OBSERVED_CHECKPOINTED", complete.Diagnostic);
        Assert.Null(host.Journal.Read().Entry!.Pending);
        Assert.Equal(new[] { "gather-receipt-v17", "craft-receipt-v17" },
            host.Journal.Read().Entry!.ConsumedReceiptIds);
        Assert.Single(host.Journal.Read().Entry!.ConsumedV9RunIds!);
        Assert.Equal(1, host.CraftCalls); // Only explicit real V9 runner invoked it
        Assert.Equal(0, host.OtherGameplayCalls);

        var terminal = host.Journal.Read();
        Assert.Equal(ProfessionProductionJournalStatus.Corrupt,
            host.Journal.CompareExchange(terminal.Sequence,
                terminal.Entry! with { Sequence = terminal.Sequence + 1 }));
        Assert.Equal(terminal.Sequence, host.Journal.Read().Sequence);
    }

    [Fact]
    public async Task Partial_v9_craft_replans_exact_remaining_units_without_replay()
    {
        using var host = new Host();
        host.Observe(Evidence(1, 4, 0));
        var first = host.Prepare(quantity: 2);
        Assert.Equal("CRAFT", first.NextAction!.Kind);
        Assert.Equal(2, first.NextAction.Quantity);
        Assert.Equal(ItemCraftAttemptDecision.Allowed,
            host.V9Journal.TryBegin(Profile, 702, Order(1), out var lease));
        var produced = await new ItemCraftProductionRun().RunAsync(Order(1), host,
            null, CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.QuantityReached, produced.Stop);
        Assert.True(host.V9Journal.TryFinish(lease, fullyConfirmed: true));
        host.Observe(Evidence(2, 2, 1));
        host.Craft(first.NextAction, lease, 2, 1, "partial-v17", attempted: 1);

        var second = host.Prepare(quantity: 2);
        Assert.Equal("GUIDED_ACTION_CHECKPOINTED", second.Diagnostic);
        Assert.Equal("CRAFT", second.NextAction!.Kind);
        Assert.Equal(1, second.NextAction.Quantity);
        Assert.Single(host.Journal.Read().Entry!.ConsumedV9RunIds!);
        Assert.Equal("SESSION_OR_REVISION_STALE", host.Inspect(2).Diagnostic);
        Assert.Equal(1, host.CraftCalls);
    }

    [Fact]
    public void Double_click_and_same_revision_cas_cannot_reopen_a_strict_action()
    {
        using var host = new Host();
        host.Observe(Evidence(1, 2, 0));
        var first = host.Prepare();
        Assert.Equal("GUIDED_ACTION_CHECKPOINTED", first.Diagnostic);
        Assert.Equal("SESSION_OR_REVISION_STALE", host.Prepare().Diagnostic);
        Assert.Equal(1, host.Journal.Read().Sequence);

        var current = host.Journal.Read();
        var duplicated = current.Entry! with
        {
            Sequence = current.Sequence + 1,
            ConsumedReceiptIds = new[] { "improper-receipt" },
            ConsumedV9RunIds = new[] { Guid.NewGuid() }
        };
        Assert.Equal(ProfessionProductionJournalStatus.Corrupt,
            host.Journal.CompareExchange(current.Sequence, duplicated));
        Assert.Equal(current.Sequence, host.Journal.Read().Sequence);
        Assert.False(host.Inspect().CanExecute);
        Assert.Equal(0, host.CraftCalls);
    }

    [Fact]
    public void Started_crash_and_uncertain_v9_result_report_reconciliation_without_auto_retry()
    {
        using var host = new Host();
        host.Observe(Evidence(1, 2, 0));
        var craft = host.Prepare().NextAction!;
        Assert.Equal(ItemCraftAttemptDecision.Allowed,
            host.V9Journal.TryBegin(Profile, 703, Order(), out var lease));
        host.Observe(Evidence(2, 0, 1));
        host.Craft(craft, lease, 2, 1, "late-after-crash");
        var started = host.Inspect();
        Assert.Equal("V9_CRAFT_STARTED_RECONCILIATION_REQUIRED", started.Diagnostic);
        Assert.Equal(ItemCraftAttemptPhase.Started, started.V9AttemptPhase);
        Assert.False(started.CanExecute);
        Assert.Equal(1, host.Journal.Read().Sequence);

        Assert.True(host.V9Journal.TryFinish(lease, fullyConfirmed: false));
        var uncertain = host.Inspect();
        Assert.Equal("V9_CRAFT_UNCERTAIN_RECONCILIATION_REQUIRED", uncertain.Diagnostic);
        Assert.Equal(ItemCraftAttemptPhase.Uncertain, uncertain.V9AttemptPhase);
        Assert.Equal("V9_CRAFT_RECEIPT_OR_ATTEMPT_UNVERIFIED", host.Prepare().Diagnostic);
        Assert.Equal(0, host.CraftCalls);
    }

    [Fact]
    public async Task Old_completed_v9_run_cannot_masquerade_as_the_next_craft()
    {
        using var host = new Host();
        host.Observe(Evidence(1, 4, 0));
        var first = host.Prepare(quantity: 2).NextAction!;
        Assert.Equal(ItemCraftAttemptDecision.Allowed,
            host.V9Journal.TryBegin(Profile, 704, Order(1), out var lease));
        Assert.Equal(ItemCraftProductionStop.QuantityReached,
            (await new ItemCraftProductionRun().RunAsync(Order(1), host,
                null, CancellationToken.None)).Stop);
        Assert.True(host.V9Journal.TryFinish(lease, fullyConfirmed: true));
        host.Observe(Evidence(2, 2, 1));
        host.Craft(first, lease, 2, 1, "old-run-receipt", attempted: 1);
        var second = host.Prepare(quantity: 2).NextAction!;

        // New callback identity from the *same* V9 RunId after first was
        // consumed: the durable run history, not receipt label, must block it.
        host.Observe(Evidence(3, 0, 2));
        host.Craft(second, lease, 3, 1, "renamed-late-callback", attempted: 1);
        Assert.Equal("V9_ATTEMPT_REPLAY", host.Prepare(quantity: 2).Diagnostic);
        Assert.Equal("V9_ATTEMPT_REPLAY", host.Inspect(2).Diagnostic);
        Assert.Equal(2, host.Journal.Read().Sequence);
        Assert.Equal(1, host.CraftCalls);
    }

    [Fact]
    public void Host_revocation_and_missing_independent_receipt_block_runtime_admission()
    {
        using var host = new Host();
        host.Observe(Evidence(1, 2, 0));
        host.Prepare();
        host.ReceiptReaderReady = false;
        Assert.Equal("CERTIFIED_OBSERVATION_UNAVAILABLE", host.Inspect().Diagnostic);
        Assert.False(host.Inspect().CanExecute);

        host.ReceiptReaderReady = true;
        host.Enabled = false;
        Assert.Equal("CERTIFIED_OBSERVATION_UNAVAILABLE", host.Inspect().Diagnostic);
        Assert.False(ProfessionProductionWorkflow.InspectExecutionAdmission(
            Catalog(), Order(), host, host.Journal).CanExecute);
        Assert.Equal(1, host.Journal.Read().Sequence);
        Assert.Equal(0, host.CraftCalls);
    }

    [Fact]
    public void Revocation_between_valid_preview_and_cas_leaves_no_durable_checkpoint()
    {
        using var host = new Host();
        host.Observe(Evidence(1, 2, 0));
        int observations = 0;
        host.OnRecoveryRead = () =>
        {
            if (++observations == 2) host.Enabled = false;
        };
        var result = host.Prepare();
        Assert.Equal("CERTIFIED_EVIDENCE_CHANGED_BEFORE_COMMIT", result.Diagnostic);
        Assert.False(result.Written);
        Assert.Equal(ProfessionProductionJournalStatus.Empty, host.Journal.Read().Status);
        Assert.Equal(0, host.CraftCalls);
        Assert.Equal(0, host.OtherGameplayCalls);
    }

    private sealed class RevisionChangingReader : IProfessionProductionJournal
    {
        private readonly IProfessionProductionJournal _inner;
        private int _reads;
        public RevisionChangingReader(IProfessionProductionJournal inner) => _inner = inner;
        public ProfessionProductionJournalRead Read()
        {
            var observed = _inner.Read();
            return ++_reads == 3
                ? observed with { Sequence = observed.Sequence + 1 }
                : observed;
        }
        public ProfessionProductionJournalStatus CompareExchange(long expected,
            ProfessionProductionJournalEntry next) =>
            throw new InvalidOperationException("Read-only inspection must never commit.");
    }

    [Fact]
    public void Runtime_inspection_rejects_a_journal_changed_between_its_two_reads()
    {
        using var host = new Host();
        host.Observe(Evidence(1, 2, 0));
        Assert.Equal("GUIDED_ACTION_CHECKPOINTED", host.Prepare().Diagnostic);
        var reader = new RevisionChangingReader(host.Journal);
        var inspection = ProfessionProductionWorkflow.InspectJourney(
            Catalog(), Order(), host, reader);
        Assert.Equal("JOURNAL_CHANGED_DURING_INSPECTION", inspection.Diagnostic);
        Assert.False(inspection.CanExecute);
        Assert.False(inspection.CanPreviewNextAction);
        Assert.Equal(1, host.Journal.Read().Sequence);
        Assert.Equal(0, host.CraftCalls);
    }

    [Fact]
    public void Mutable_host_inventory_is_frozen_before_independent_verification()
    {
        var catalog = Catalog();
        var envelope = Evidence(1, 2, 0);
        var input = Assert.IsType<List<ProfessionInventoryStack>>(envelope.Observation.Inventory);
        var sources = Assert.IsType<MaterialSource[]>(envelope.Observation.Sources);
        bool verified = false;
        var context = new ProfessionMcpRecoveryContext(true, envelope, receipt =>
        {
            verified = true;
            input[0] = input[0] with { ItemGid = 999, Quantity = 999 };
            sources[0] = sources[0] with { MaxUnits = 10 };
            return receipt == envelope.Receipt;
        }, TargetItemGid: 200, TargetQuantity: 1);

        var result = ProfessionMcpRecoveryIntegration.Evaluate(catalog, context, true);
        Assert.True(verified);
        Assert.True(result.HasCurrentPlan);
        Assert.Equal(10, result.NextState!.LastEvidence.Observation.Inventory[0].ItemGid);
        Assert.Equal(2, result.NextState.LastEvidence.Observation.Inventory[0].Quantity);
        Assert.Equal(0, result.NextState.LastEvidence.Observation.Sources[0].MaxUnits);
        Assert.Equal(envelope.Receipt.InventorySha256,
            ProfessionMcpRecoveryIntegration.InventoryDigest(
                result.NextState.LastEvidence.Observation.Inventory));
        Assert.False(ProfessionMcpRecoveryIntegration.Evaluate(catalog, context, true)
            .HasCurrentPlan); // same old receipt cannot attest mutated raw lists
    }

    [Fact]
    public void Nested_craft_does_not_checkpoint_without_an_exact_secondary_v9_order()
    {
        using var host = new Host();
        host.Observe(Evidence(1, 0, 0, precursor: 2, includeGatherSource: false));
        var nested = Catalog(nested: true);
        var result = host.Prepare(catalog: nested);
        Assert.Equal("NESTED_CRAFT_EXACT_V9_ORDER_MISSING", result.Diagnostic);
        Assert.False(result.Written);
        Assert.Null(result.NextAction);
        Assert.Equal(ProfessionProductionJournalStatus.Empty, host.Journal.Read().Status);
        Assert.Equal(0, host.CraftCalls);
    }

    [Fact]
    public void Forgemagie_remains_pure_advice_and_never_creates_a_send_admission()
    {
        using var host = new Host();
        var preview = ProfessionProductionWorkflow.PreviewForge(
            new[] { new ForgeLine("Force", 10, 0, 40, 15) },
            new Dictionary<(string Stat, string Tier), int> { [("Force", "Ba")] = 1 });
        Assert.Equal("FORGE_ADVISORY_ONLY", preview.Diagnostic);
        Assert.Equal(AdviceKind.Rune, preview.Advice!.Kind);
        Assert.False(preview.CanExecute);
        Assert.False(ProfessionProductionWorkflow.InspectExecutionAdmission(
            Catalog(), Order(), host, host.Journal).CanExecute);
        Assert.Equal(0, host.CraftCalls);
        Assert.Equal(0, host.OtherGameplayCalls);
    }
}
