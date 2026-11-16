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
/// V18 real V9 journal, production runner, CAS and recovery entrypoints.
/// Host-verified server receipts are a deliberately separate test ledger:
/// these tests do not assert any production server receipt implementation.
/// No actual game/network calls can occur in this fixture.
/// </summary>
public sealed class ProfessionFunctionalV18Tests
{
    private const string GameVersion = "3.7.13.17";
    private const string Profile = "server:292:profession-functional-v18";
    private const string Subject = "v18-character";
    private const string Session = "v18-session";
    private static readonly string Manifest = new('A', 64);

    private static ProfessionMcpSnapshot Catalog(int requested = 1) => new(
        new ProfessionPlanRequest(new ProfessionGoal(7, "V18 profession", 20, 20),
            TrainingRecipeId: 120, RequestedCrafts: requested,
            Recipes: new[] { new ProfessionRecipe(120, 200, 1,
                new[] { new RecipeIngredient(10, 2) }, 20, 7,
                SourceVersion: GameVersion, Verified: true,
                SecondsPerCraft: 1, KamasPerCraft: 0) },
            Inventory: Array.Empty<ProfessionInventoryStack>(),
            Sources: Array.Empty<MaterialSource>()),
        new ProfessionMcpProvenance("local-owned", "v18-exact", GameVersion,
            Manifest, true, "reviewed-test-source"));

    private static ItemCraftProductionRequest Order(int count = 1) => new(
        new ItemCraftProductionRecipe(200, 1, 10, 0,
            new[] { new ItemCraftProductionInput(10, 2, false) }),
        count, MaxKamas: 0, BankAllowed: false, SaleFloor: null);

    private static ProfessionMcpRecoveryEnvelope Snapshot(long revision,
        long ingredientCount, long outputCount, long sourceCapacity = 0)
    {
        var inventory = new List<ProfessionInventoryStack>();
        if (ingredientCount > 0) inventory.Add(new("ingredient", 10, ingredientCount, "bag"));
        if (outputCount > 0) inventory.Add(new("output", 200, outputCount, "bag"));
        var sources = new List<MaterialSource>
        {
            new(10, AcquisitionMethod.Gather, 1, 0, GameVersion, true,
                MaxUnits: sourceCapacity)
        };
        var observation = new ProfessionRecoveryObservation(Subject, revision,
            GameVersion, true, true, inventory, sources);
        var receipt = new ProfessionMcpRecoveryReceipt(Subject, Session, revision,
            GameVersion, Manifest,
            ProfessionMcpRecoveryIntegration.InventoryDigest(inventory),
            ProfessionMcpRecoveryIntegration.SourceCapsDigest(sources),
            "v18-observed-" + revision);
        return new(observation, receipt);
    }

    private sealed class RealV9Host : IDisposable, IProfessionProductionV9Evidence,
        IItemCraftProductionStockObservation
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(),
            "givre-v18-profession-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, ProfessionMcpRecoveryReceipt> _signedObservations = new();
        private readonly Dictionary<string, ProfessionProductionCraftWitness> _crafts = new();
        private readonly Dictionary<string, ProfessionProductionV9CraftReceipt> _v9Crafts = new();
        private readonly Dictionary<string, ProfessionProductionAcquisitionReceipt> _acquisitions = new();
        private readonly Dictionary<long, int> _bag = new();

        public RealV9Host()
        {
            Directory.CreateDirectory(_folder);
            ProfessionJournalPath = Path.Combine(_folder, "profession.journal");
            V9JournalPath = Path.Combine(_folder, "v9.journal");
            Journal = new ProfessionProductionFileJournal(ProfessionJournalPath);
            V9Journal = new ItemCraftProductionAttemptJournal(V9JournalPath);
        }

        public string ProfessionJournalPath { get; }
        public string V9JournalPath { get; }
        public ProfessionProductionFileJournal Journal { get; }
        public ItemCraftProductionAttemptJournal V9Journal { get; }
        public string V9Profile => Profile;
        public bool SupportsStockObservation => true;
        public bool Enabled = true;
        public bool VerifierAvailable = true;
        public Action? OnRead;
        public Action? OnBagRead;
        public ProfessionMcpRecoveryEnvelope? Current { get; private set; }
        public int CraftCalls { get; private set; }
        public int OtherGameplayCalls { get; private set; }

        public void Observe(ProfessionMcpRecoveryEnvelope evidence)
        {
            Current = evidence;
            _signedObservations[evidence.Receipt.ReceiptId] = evidence.Receipt;
            _bag.Clear();
            foreach (var item in evidence.Observation.Inventory)
                _bag[item.ItemGid] = checked(_bag.GetValueOrDefault(item.ItemGid) +
                    (int)item.Quantity);
        }

        public ProfessionProductionGuidance Prepare(int quantity = 1, bool activate = true) =>
            new ItemCraftProductionRun().PrepareVerifiedNextAction(Catalog(quantity),
                Order(quantity), this, Journal, activate);

        public ProfessionProductionJourneyStatus Inspect(int quantity = 1) =>
            ProfessionProductionWorkflow.InspectJourney(Catalog(quantity),
                Order(quantity), this, Journal);

        public void RegisterGather(ProfessionProductionPendingAction pending,
            long revision, long quantity, string receiptId)
        {
            _acquisitions[pending.ActionKey] = new ProfessionProductionAcquisitionReceipt(
                pending.ActionKey, Subject, Session, revision, pending.Kind,
                pending.ItemGid, quantity, receiptId);
        }

        public void RegisterCraft(ProfessionProductionPendingAction pending,
            ItemCraftAttemptLease lease, long revision, int quantity, string receiptId,
            int? attemptQuantity = null)
        {
            _crafts[pending.ActionKey] = new ProfessionProductionCraftWitness(
                pending.ActionKey, Session, revision, pending.RecipeId!.Value,
                pending.ItemGid, quantity, receiptId);
            _v9Crafts[pending.ActionKey] = new ProfessionProductionV9CraftReceipt(
                pending.ActionKey, Subject, Session, revision, pending.RecipeId.Value,
                receiptId, lease.Profile, lease.RunId, lease.Pid, lease.RequestHash,
                new ItemCraftWorkshopReceipt(true, quantity, pending.ItemGid),
                attemptQuantity);
        }

        public ProfessionMcpRecoveryContext ReadRecoveryContext()
        {
            OnRead?.Invoke();
            return new(Enabled, Current, VerifierAvailable
                ? receipt => _signedObservations.TryGetValue(receipt.ReceiptId,
                    out var saved) && saved == receipt : null);
        }
        public ProfessionProductionCraftWitness? ReadCraftWitness(string actionKey) =>
            VerifierAvailable ? _crafts.GetValueOrDefault(actionKey) : null;
        public bool VerifyCraftWitness(ProfessionProductionCraftWitness witness) =>
            VerifierAvailable && _crafts.TryGetValue(witness.ActionKey, out var saved) &&
            saved == witness;
        public ProfessionProductionV9CraftReceipt? ReadV9CraftReceipt(string actionKey) =>
            VerifierAvailable ? _v9Crafts.GetValueOrDefault(actionKey) : null;
        public bool VerifyV9CraftReceipt(ProfessionProductionV9CraftReceipt receipt) =>
            VerifierAvailable && _v9Crafts.TryGetValue(receipt.ActionKey, out var saved) &&
            saved == receipt;
        public ProfessionProductionAcquisitionReceipt? ReadAcquisitionReceipt(string actionKey) =>
            VerifierAvailable ? _acquisitions.GetValueOrDefault(actionKey) : null;
        public bool VerifyAcquisitionReceipt(ProfessionProductionAcquisitionReceipt receipt) =>
            VerifierAvailable && _acquisitions.TryGetValue(receipt.ActionKey, out var saved) &&
            saved == receipt;

        public int BagCount(long gid)
        {
            OnBagRead?.Invoke();
            return _bag.GetValueOrDefault(gid);
        }
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
        public Task<int> WithdrawAsync(long gid, int qty, CancellationToken ct)
        { OtherGameplayCalls++; return Task.FromResult(0); }
        public Task<bool> CloseChestAsync(CancellationToken ct)
        { OtherGameplayCalls++; return Task.FromResult(false); }
        public Task<long> LotPriceAsync(long gid, int qty, CancellationToken ct) =>
            Task.FromResult(0L);
        public Task<bool> BuyAsync(long gid, int qty, long maxPrice, CancellationToken ct)
        { OtherGameplayCalls++; return Task.FromResult(false); }
        public Task<ItemCraftWorkshopReceipt> CraftAsync(int elementId, int uid,
            IReadOnlyList<(long Gid, int Qty)> ingredients, int count, CancellationToken ct)
        {
            CraftCalls++;
            foreach (var input in ingredients)
                _bag[input.Gid] -= input.Qty;
            _bag[200] = _bag.GetValueOrDefault(200) + 1;
            return Task.FromResult(new ItemCraftWorkshopReceipt(true, 1, 200));
        }
        public Task<bool> ListAsync(long gid, int qty, long price, CancellationToken ct)
        { OtherGameplayCalls++; return Task.FromResult(false); }

        public void Dispose() => Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Preexisting_started_v9_lease_blocks_fresh_profession_guidance()
    {
        using var host = new RealV9Host();
        host.Observe(Snapshot(1, 2, 0));
        Assert.Equal(ItemCraftAttemptDecision.Allowed,
            host.V9Journal.TryBegin(Profile, 801, Order(), out var lease));
        var proposed = host.Prepare();
        Assert.Equal("V9_PREEXISTING_ATTEMPT_STARTED", proposed.Diagnostic);
        Assert.Null(proposed.NextAction);
        Assert.False(proposed.Written);
        Assert.Equal(ProfessionProductionJournalStatus.Empty, host.Journal.Read().Status);
        var gaps = ProfessionProductionWorkflow.InspectHostProofGaps("CRAFT", host,
            host.Journal);
        Assert.Equal(ItemCraftAttemptPhase.Started, gaps.V9AttemptPhase);
        Assert.Contains(gaps.MissingOrUnverifiableProofs,
            gap => gap.Code == "V9_UNRECONCILED_ATTEMPT");

        // A new process can read Started but cannot finish a lease it did not
        // open; a crash must stay pending until independent reconciliation.
        var restarted = new ItemCraftProductionAttemptJournal(host.V9JournalPath);
        Assert.Equal(ItemCraftAttemptPhase.Started, restarted.ReadStatus(Profile).Phase);
        Assert.False(restarted.TryFinish(lease, fullyConfirmed: true));
        Assert.Equal(ItemCraftAttemptDecision.NeedsReconciliation,
            restarted.TryBegin(Profile, 802, Order(), out _));
        Assert.Equal(0, host.CraftCalls);
    }

    [Fact]
    public void Uncertain_v9_lease_blocks_even_when_bag_is_full_and_after_restart()
    {
        using var host = new RealV9Host();
        host.Observe(Snapshot(1, 2, 0));
        Assert.Equal(ItemCraftAttemptDecision.Allowed,
            host.V9Journal.TryBegin(Profile, 803, Order(), out var lease));
        Assert.True(host.V9Journal.TryFinish(lease, fullyConfirmed: false));
        Assert.Equal("V9_PREEXISTING_ATTEMPT_UNCERTAIN", host.Prepare().Diagnostic);
        Assert.Equal("V9_PREEXISTING_ATTEMPT_UNCERTAIN", host.Inspect().Diagnostic);
        Assert.Equal(ItemCraftAttemptPhase.Uncertain, host.Inspect().V9AttemptPhase);
        Assert.Equal(ProfessionProductionJournalStatus.Empty, host.Journal.Read().Status);
        Assert.Equal(ItemCraftAttemptPhase.Uncertain,
            new ItemCraftProductionAttemptJournal(host.V9JournalPath).ReadStatus(Profile).Phase);
        Assert.Equal(0, host.OtherGameplayCalls);
    }

    [Fact]
    public void V9_attempt_started_between_initial_observation_and_cas_revokes_checkpoint()
    {
        using var host = new RealV9Host();
        host.Observe(Snapshot(1, 2, 0));
        int reads = 0;
        host.OnRead = () =>
        {
            if (++reads == 2)
                Assert.Equal(ItemCraftAttemptDecision.Allowed,
                    host.V9Journal.TryBegin(Profile, 804, Order(), out _));
        };
        var proposed = host.Prepare();
        Assert.Equal("V9_PREEXISTING_ATTEMPT_STARTED", proposed.Diagnostic);
        Assert.False(proposed.Written);
        Assert.Equal(ProfessionProductionJournalStatus.Empty, host.Journal.Read().Status);
    }

    [Fact]
    public void Revoked_host_opt_in_at_second_observation_does_not_commit_checkpoint()
    {
        using var host = new RealV9Host();
        host.Observe(Snapshot(1, 2, 0));
        int reads = 0;
        host.OnRead = () =>
        {
            if (++reads == 2) host.Enabled = false;
        };
        var denied = host.Prepare();
        Assert.Equal("CERTIFIED_EVIDENCE_CHANGED_BEFORE_COMMIT", denied.Diagnostic);
        Assert.False(denied.Written);
        Assert.Equal(ProfessionProductionJournalStatus.Empty, host.Journal.Read().Status);
        Assert.False(host.Inspect().CanExecute);
        Assert.Equal(0, host.CraftCalls);
    }

    [Fact]
    public void Existing_gather_partial_receipt_cannot_unlock_next_action_while_v9_lease_started()
    {
        using var host = new RealV9Host();
        host.Observe(Snapshot(1, 0, 0, sourceCapacity: 2));
        var first = host.Prepare();
        Assert.Equal("GATHER", first.NextAction!.Kind);
        Assert.Equal(ItemCraftAttemptDecision.Allowed,
            host.V9Journal.TryBegin(Profile, 805, Order(), out _));
        host.Observe(Snapshot(2, 1, 0, sourceCapacity: 1));
        host.RegisterGather(first.NextAction, 2, 1, "v18-gather-1");

        Assert.Equal("V9_PREEXISTING_ATTEMPT_STARTED", host.Prepare().Diagnostic);
        Assert.Equal(1, host.Journal.Read().Sequence);
        Assert.Null(host.Journal.Read().Entry!.ConsumedReceiptIds);
        Assert.Equal(0, host.CraftCalls);
    }

    [Fact]
    public async Task Real_v9_runner_and_independent_receipts_close_gather_then_craft()
    {
        using var host = new RealV9Host();
        host.Observe(Snapshot(1, 0, 0, sourceCapacity: 2));
        var gather = host.Prepare();
        Assert.Equal("GUIDED_ACTION_CHECKPOINTED", gather.Diagnostic);
        Assert.Equal("GATHER", gather.NextAction!.Kind);
        Assert.Equal(0, host.CraftCalls);

        host.Observe(Snapshot(2, 2, 0, sourceCapacity: 0));
        Assert.Equal("V9_ACQUISITION_RECEIPT_UNVERIFIED", host.Prepare().Diagnostic);
        host.RegisterGather(gather.NextAction, 2, 2, "gather-v18-receipt");
        var craft = host.Prepare();
        Assert.Equal("GUIDED_ACTION_CHECKPOINTED", craft.Diagnostic);
        Assert.Equal("CRAFT", craft.NextAction!.Kind);

        Assert.Equal(ItemCraftAttemptDecision.Allowed,
            host.V9Journal.TryBegin(Profile, 806, Order(), out var lease));
        var run = await new ItemCraftProductionRun().RunAsync(Order(), host, null,
            CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.QuantityReached, run.Stop);
        Assert.True(host.V9Journal.TryFinish(lease, fullyConfirmed: true));
        host.Observe(Snapshot(3, 0, 1));
        host.RegisterCraft(craft.NextAction, lease, 3, 1, "craft-v18-receipt");
        var done = host.Prepare();
        Assert.Equal("TARGET_OBSERVED_CHECKPOINTED", done.Diagnostic);
        Assert.Equal(new[] { "gather-v18-receipt", "craft-v18-receipt" },
            host.Journal.Read().Entry!.ConsumedReceiptIds);
        Assert.Single(host.Journal.Read().Entry!.ConsumedV9RunIds!);
        Assert.Equal(1, host.CraftCalls); // Only explicit runner issued CraftAsync
        Assert.Equal(0, host.OtherGameplayCalls);

        var reopened = new ProfessionProductionFileJournal(host.ProfessionJournalPath);
        Assert.Equal(ProfessionProductionJournalStatus.Current, reopened.Read().Status);
        Assert.Null(reopened.Read().Entry!.Pending);
        Assert.Equal("CHECKPOINT_NO_PENDING_ACTION", host.Prepare().Diagnostic);
    }

    [Fact]
    public void Partial_craft_and_old_run_receipt_cannot_replay_a_completed_attempt()
    {
        using var host = new RealV9Host();
        host.Observe(Snapshot(1, 4, 0));
        var first = host.Prepare(quantity: 2).NextAction!;
        Assert.Equal(ItemCraftAttemptDecision.Allowed,
            host.V9Journal.TryBegin(Profile, 807, Order(), out var lease));
        Assert.True(host.V9Journal.TryFinish(lease, fullyConfirmed: true));
        host.Observe(Snapshot(2, 2, 1));
        host.RegisterCraft(first, lease, 2, 1, "partial-v18", attemptQuantity: 1);
        var second = host.Prepare(quantity: 2);
        Assert.Equal("GUIDED_ACTION_CHECKPOINTED", second.Diagnostic);
        Assert.Equal(1, second.NextAction!.Quantity);

        host.Observe(Snapshot(3, 0, 2));
        host.RegisterCraft(second.NextAction, lease, 3, 1, "renamed-old-v18", attemptQuantity: 1);
        Assert.Equal("V9_ATTEMPT_REPLAY", host.Prepare(quantity: 2).Diagnostic);
        Assert.Equal(2, host.Journal.Read().Sequence);
        Assert.Single(host.Journal.Read().Entry!.ConsumedV9RunIds!);
        Assert.Equal(0, host.CraftCalls); // No implicit send on reconciling
    }

    [Fact]
    public void Double_click_stale_journal_cas_cannot_duplicate_guidance()
    {
        using var host = new RealV9Host();
        host.Observe(Snapshot(1, 2, 0));
        Assert.Equal("GUIDED_ACTION_CHECKPOINTED", host.Prepare().Diagnostic);
        Assert.Equal("SESSION_OR_REVISION_STALE", host.Prepare().Diagnostic);
        var current = host.Journal.Read();
        var fabricated = current.Entry! with { Sequence = current.Sequence + 1 };
        Assert.Equal(ProfessionProductionJournalStatus.Corrupt,
            host.Journal.CompareExchange(current.Sequence, fabricated));
        Assert.Equal(current.Sequence, host.Journal.Read().Sequence);
        Assert.Equal(0, host.CraftCalls);
    }

    [Fact]
    public void Mutable_host_stock_callback_cannot_rebind_pending_action_after_digest_verified()
    {
        using var host = new RealV9Host();
        var snapshot = Snapshot(1, 2, 0);
        host.Observe(snapshot);
        var mutable = Assert.IsType<List<ProfessionInventoryStack>>(
            snapshot.Observation.Inventory);
        int bagReads = 0;
        host.OnBagRead = () =>
        {
            if (++bagReads == 1)
                mutable[0] = mutable[0] with { ItemGid = 999, Quantity = 999 };
        };

        var result = host.Prepare();
        Assert.Equal("CERTIFIED_EVIDENCE_CHANGED_BEFORE_COMMIT", result.Diagnostic);
        Assert.False(result.Written);
        Assert.Equal(ProfessionProductionJournalStatus.Empty, host.Journal.Read().Status);
        Assert.Equal(0, host.CraftCalls);
    }

    [Fact]
    public void Host_proof_report_enumerates_exact_missing_v9_acquisition_and_forge_contracts()
    {
        using var host = new RealV9Host();
        var craft = ProfessionProductionWorkflow.InspectHostProofGaps("CRAFT", host,
            host.Journal);
        Assert.Equal("HOST_PROOFS_MISSING_OR_UNVERIFIABLE", craft.Diagnostic);
        Assert.False(craft.CanAdmitMutation);
        Assert.Contains(craft.MissingOrUnverifiableProofs,
            gap => gap.Code == "V9_PID_RUN_IDENTITY" &&
                gap.ExactHostEvidenceRequired.Contains("PID", StringComparison.Ordinal));
        Assert.Contains(craft.MissingOrUnverifiableProofs,
            gap => gap.Code == "SERVER_CRAFT_RECEIPT");
        Assert.Contains(craft.MissingOrUnverifiableProofs,
            gap => gap.Code == "SINGLE_HOST_MUTATION_LEASE");
        Assert.DoesNotContain(craft.MissingOrUnverifiableProofs,
            gap => gap.Code == "V9_EXACT_HOST_BRIDGE"); // test port has the shape only

        var gather = ProfessionProductionWorkflow.InspectHostProofGaps("GATHER", host,
            host.Journal);
        Assert.Contains(gather.MissingOrUnverifiableProofs,
            gap => gap.Code == "SERVER_ACQUISITION_RECEIPT");
        Assert.DoesNotContain(gather.MissingOrUnverifiableProofs,
            gap => gap.Code == "SERVER_CRAFT_RECEIPT");

        var forge = ProfessionProductionWorkflow.InspectHostProofGaps("FORGE", host,
            host.Journal);
        Assert.Contains(forge.MissingOrUnverifiableProofs,
            gap => gap.Code == "FORGE_SERVER_OUTCOME");
        Assert.Contains(forge.MissingOrUnverifiableProofs,
            gap => gap.Code == "FORGE_ATTEMPT_OWNER");
        Assert.False(forge.CanAdmitMutation);
        Assert.Equal(0, host.CraftCalls);
        Assert.Equal(0, host.OtherGameplayCalls);
    }

    [Fact]
    public void Missing_port_is_explicit_and_forge_advice_is_not_execution()
    {
        var report = ProfessionProductionWorkflow.InspectHostProofGaps("CRAFT",
            null, null);
        Assert.Contains(report.MissingOrUnverifiableProofs,
            gap => gap.Code == "CERTIFIED_STOCK_PORT");
        Assert.Contains(report.MissingOrUnverifiableProofs,
            gap => gap.Code == "V9_EXACT_HOST_BRIDGE");
        Assert.Contains(report.MissingOrUnverifiableProofs,
            gap => gap.Code == "DURABLE_GUIDANCE_JOURNAL");
        Assert.False(report.CanAdmitMutation);

        var advice = ProfessionProductionWorkflow.PreviewForge(
            new[] { new ForgeLine("Force", 10, 0, 40, 15) },
            new Dictionary<(string Stat, string Tier), int> { [("Force", "Ba")] = 1 });
        Assert.Equal("FORGE_ADVISORY_ONLY", advice.Diagnostic);
        Assert.Equal(AdviceKind.Rune, advice.Advice!.Kind);
        Assert.False(advice.CanExecute);
    }
}
