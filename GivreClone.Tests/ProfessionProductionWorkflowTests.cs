using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.ItemCraftPlanner;
using GivreClone.App.Services.ProfessionMcp;
using GivreClone.App.Services.ProfessionPlanner;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Offline production-entry tests with synthetic recipe, stock and receipts.</summary>
public sealed class ProfessionProductionWorkflowTests
{
    private const string Version = "3.7.13.17";
    private static readonly string Manifest = new('A', 64);

    private static ProfessionMcpSnapshot Catalog() => new(
        new ProfessionPlanRequest(new ProfessionGoal(7, "Test métier", 20, 20),
            TrainingRecipeId: 120, RequestedCrafts: 1,
            Recipes: new[] { new ProfessionRecipe(120, 200, 1,
                new[] { new RecipeIngredient(10, 2) }, 20, 7,
                SourceVersion: Version, Verified: true,
                SecondsPerCraft: 1, KamasPerCraft: 0) },
            Inventory: Array.Empty<ProfessionInventoryStack>(),
            Sources: Array.Empty<MaterialSource>()),
        new ProfessionMcpProvenance("local-owned", "synthetic-prod", Version,
            Manifest, true, "independent-review"));

    private static ItemCraftProductionRequest Order(int quantity = 1) =>
        new(new ItemCraftProductionRecipe(200, 1, 10, 0,
            new[] { new ItemCraftProductionInput(10, 2, false) }),
            quantity, 0, BankAllowed: false, SaleFloor: null);

    private static ProfessionMcpRecoveryEnvelope Evidence(long revision, long inputCount,
        long capacity, long resultCount = 0, string session = "session-a")
    {
        var inventory = new List<ProfessionInventoryStack>();
        if (inputCount > 0) inventory.Add(new("bag-input", 10, inputCount, "bag"));
        if (resultCount > 0) inventory.Add(new("bag-output", 200, resultCount, "bag"));
        var source = new[] { new MaterialSource(10, AcquisitionMethod.Gather,
            2, 0, Version, true, MaxUnits: capacity) };
        var observation = new ProfessionRecoveryObservation("character-fixture",
            revision, Version, true, true, inventory, source);
        var receipt = new ProfessionMcpRecoveryReceipt(observation.SubjectKey, session,
            revision, Version, Manifest,
            ProfessionMcpRecoveryIntegration.InventoryDigest(inventory),
            ProfessionMcpRecoveryIntegration.SourceCapsDigest(source),
            "receipt-" + session + "-" + revision);
        return new(observation, receipt);
    }

    private sealed class Storage : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(),
            "givre-profession-production-" + Guid.NewGuid().ToString("N"));
        public string PathName => Path.Combine(DirectoryPath, "journal.json");
        public Storage() => Directory.CreateDirectory(DirectoryPath);
        public ProfessionProductionFileJournal Open() => new(PathName);
        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }

    private sealed class CertifiedActions : IProfessionProductionCertifiedActions
    {
        private readonly Dictionary<string, ProfessionMcpRecoveryReceipt> _receiptLedger =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, ProfessionProductionCraftWitness> _craftLedger =
            new(StringComparer.Ordinal);
        public ProfessionMcpRecoveryEnvelope? Current;
        public readonly Dictionary<long, int> Bag = new();
        public bool HostEnabled = true;
        public bool ReceiptVerifierReady = true;
        public bool ThrowOnRead;
        public int MutatingCalls;
        public int BagReads;

        public void Load(ProfessionMcpRecoveryEnvelope evidence)
        {
            Current = evidence;
            _receiptLedger.Add(evidence.Receipt.ReceiptId, evidence.Receipt);
            Bag.Clear();
            foreach (var stack in evidence.Observation.Inventory)
                Bag[stack.ItemGid] = checked(Bag.GetValueOrDefault(stack.ItemGid) + (int)stack.Quantity);
        }

        public void AddCraftReceipt(ProfessionProductionCraftWitness witness) =>
            _craftLedger.Add(witness.ActionKey, witness);

        public ProfessionMcpRecoveryContext ReadRecoveryContext()
        {
            if (ThrowOnRead) throw new IOException("Disconnected observation service");
            return new ProfessionMcpRecoveryContext(HostEnabled, Current,
                ReceiptVerifierReady ? VerifyReceipt : null);
        }

        public bool VerifyReceipt(ProfessionMcpRecoveryReceipt receipt) =>
            _receiptLedger.TryGetValue(receipt.ReceiptId, out var prior) && prior == receipt;
        public ProfessionProductionCraftWitness? ReadCraftWitness(string actionKey) =>
            _craftLedger.GetValueOrDefault(actionKey);
        public bool VerifyCraftWitness(ProfessionProductionCraftWitness witness) =>
            _craftLedger.TryGetValue(witness.ActionKey, out var prior) && prior == witness;

        public int BagCount(long gid) { BagReads++; return Bag.GetValueOrDefault(gid); }
        public int ChestCount(long gid) => 0;
        public IReadOnlyList<MapInteractive> Interactives() => Array.Empty<MapInteractive>();
        public Task<bool> OpenChestAsync(CancellationToken ct) { MutatingCalls++; return Task.FromResult(false); }
        public Task<int> WithdrawAsync(long gid, int quantity, CancellationToken ct)
        { MutatingCalls++; return Task.FromResult(0); }
        public Task<bool> CloseChestAsync(CancellationToken ct) { MutatingCalls++; return Task.FromResult(false); }
        public Task<long> LotPriceAsync(long gid, int qty, CancellationToken ct) => Task.FromResult(0L);
        public Task<bool> BuyAsync(long gid, int qty, long maxPrice, CancellationToken ct)
        { MutatingCalls++; return Task.FromResult(false); }
        public Task<ItemCraftWorkshopReceipt> CraftAsync(int elementId, int skillInstanceUid,
            IReadOnlyList<(long Gid, int Qty)> ingredients, int count, CancellationToken ct)
        { MutatingCalls++; return Task.FromResult(new ItemCraftWorkshopReceipt(false, 0, 0)); }
        public Task<bool> ListAsync(long gid, int quantity, long price, CancellationToken ct)
        { MutatingCalls++; return Task.FromResult(false); }
    }

    private static ProfessionProductionGuidance Prepare(
        CertifiedActions actions, IProfessionProductionJournal journal,
        bool activate, ItemCraftProductionRequest? order = null) =>
        new ItemCraftProductionRun().PrepareVerifiedNextAction(
            Catalog(), order ?? Order(), actions, journal, activate);

    [Fact]
    public void Production_entry_requires_independent_port_before_any_preview_or_gameplay()
    {
        using var storage = new Storage();
        var legacy = new LegacyActions();
        var result = new ItemCraftProductionRun().PrepareVerifiedNextAction(
            Catalog(), Order(), legacy, storage.Open(), true);
        Assert.Equal("CERTIFIED_OBSERVATION_PORT_MISSING", result.Diagnostic);
        Assert.False(result.Written);
        Assert.Equal(ProfessionProductionJournalStatus.Empty, storage.Open().Read().Status);
    }

    [Fact]
    public void Explicit_activation_controls_the_first_guided_gather_checkpoint()
    {
        using var storage = new Storage();
        var actions = new CertifiedActions();
        actions.Load(Evidence(1, 0, 2));
        var ready = Prepare(actions, storage.Open(), false);
        Assert.Equal("NEXT_ACTION_REQUIRES_EXPLICIT_OPT_IN", ready.Diagnostic);
        Assert.Equal("GATHER", ready.NextAction!.Kind);
        Assert.Equal(2, ready.NextAction.Quantity);
        Assert.False(ready.Written);
        Assert.Equal(ProfessionProductionJournalStatus.Empty, storage.Open().Read().Status);

        var committed = Prepare(actions, storage.Open(), true);
        Assert.Equal("GUIDED_ACTION_CHECKPOINTED", committed.Diagnostic);
        Assert.True(committed.Written);
        Assert.Equal(1, committed.JournalSequence);
        Assert.Equal("GATHER", storage.Open().Read().Entry!.Pending!.Kind);
        Assert.Equal(0, actions.MutatingCalls);
    }

    [Fact]
    public void After_crash_a_new_snapshot_reduces_pending_gather_without_double_consumption()
    {
        using var storage = new Storage();
        var actions = new CertifiedActions();
        actions.Load(Evidence(41, 0, 2));
        var first = Prepare(actions, storage.Open(), true);
        Assert.Equal(2, first.NextAction!.Quantity);

        // Crash/restart: same durable file, new journal instance and runner.
        actions.Load(Evidence(42, 1, 1));
        var resumed = Prepare(actions, storage.Open(), true);
        Assert.Equal("GUIDED_ACTION_CHECKPOINTED", resumed.Diagnostic);
        Assert.True(resumed.PreviousProposalInvalidated);
        Assert.Equal(2, resumed.JournalSequence);
        Assert.Equal("GATHER", resumed.NextAction!.Kind);
        Assert.Equal(1, resumed.NextAction.Quantity);
        Assert.Equal(1, resumed.Plan!.UsedStock[10]);
        Assert.Equal(0, actions.MutatingCalls);
    }

    [Fact]
    public void Without_a_fresh_observation_no_pending_action_can_be_duplicated()
    {
        using var storage = new Storage();
        var actions = new CertifiedActions();
        actions.Load(Evidence(1, 0, 2));
        var first = Prepare(actions, storage.Open(), true);
        var second = Prepare(actions, storage.Open(), true);
        Assert.Equal("SESSION_OR_REVISION_STALE", second.Diagnostic);
        Assert.Null(second.NextAction);
        Assert.Equal(first.JournalSequence, storage.Open().Read().Sequence);
    }

    [Fact]
    public void Craft_checkpoint_requires_both_receipt_and_observed_output_before_close()
    {
        using var storage = new Storage();
        var actions = new CertifiedActions();
        actions.Load(Evidence(1, 0, 2));
        var gather = Prepare(actions, storage.Open(), true);
        Assert.Equal("GATHER", gather.NextAction!.Kind);

        actions.Load(Evidence(2, 2, 0));
        var craft = Prepare(actions, storage.Open(), true);
        Assert.Equal("CRAFT", craft.NextAction!.Kind);
        Assert.Equal(120, craft.NextAction.RecipeId);
        Assert.Equal(2, craft.JournalSequence);

        actions.Load(Evidence(3, 0, 0, resultCount: 1));
        var noReceipt = Prepare(actions, storage.Open(), true);
        Assert.Equal("AWAITING_INDEPENDENT_OBSERVATION_OR_CRAFT_RECEIPT", noReceipt.Diagnostic);
        Assert.Equal(2, storage.Open().Read().Sequence);

        actions.AddCraftReceipt(new ProfessionProductionCraftWitness(
            craft.NextAction.ActionKey, craft.NextAction.SessionKey, 3,
            craft.NextAction.RecipeId!.Value, craft.NextAction.ItemGid, 1, "craft-receipt-test"));
        var completed = Prepare(actions, storage.Open(), true);
        Assert.Equal("TARGET_OBSERVED_CHECKPOINTED", completed.Diagnostic);
        Assert.True(completed.Written);
        Assert.Null(storage.Open().Read().Entry!.Pending);
        Assert.Equal(3, completed.JournalSequence);
        Assert.Equal(0, actions.MutatingCalls);
    }

    [Fact]
    public void Bag_drift_or_unauthorized_caps_stops_without_journal_advance()
    {
        using var storage = new Storage();
        var actions = new CertifiedActions();
        actions.Load(Evidence(1, 1, 1));
        actions.Bag[10] = 2;
        var mismatch = Prepare(actions, storage.Open(), true);
        Assert.Equal("BAG_SNAPSHOT_MISMATCH", mismatch.Diagnostic);
        Assert.Equal(ProfessionProductionJournalStatus.Empty, storage.Open().Read().Status);

        var unbounded = Evidence(2, 0, 1);
        actions.Load(unbounded with { Observation = unbounded.Observation with
        { Sources = new[] { unbounded.Observation.Sources[0] with { MaxUnits = null } } } });
        var denied = Prepare(actions, storage.Open(), true);
        Assert.Equal("CURRENT_OBSERVATION_UNVERIFIED", denied.Diagnostic);
        Assert.Null(denied.NextAction);
        Assert.Equal(0, actions.MutatingCalls);
    }

    [Fact]
    public void Session_change_and_missing_verifier_stop_with_previous_state_preserved()
    {
        using var storage = new Storage();
        var actions = new CertifiedActions();
        actions.Load(Evidence(1, 0, 2));
        Prepare(actions, storage.Open(), true);
        actions.Load(Evidence(2, 1, 1, session: "session-b"));
        var switched = Prepare(actions, storage.Open(), true);
        Assert.Equal("SESSION_OR_REVISION_STALE", switched.Diagnostic);
        Assert.Equal(1, storage.Open().Read().Sequence);

        actions.Load(Evidence(2, 1, 1));
        actions.ReceiptVerifierReady = false;
        var noVerifier = Prepare(actions, storage.Open(), true);
        Assert.Equal("CERTIFIED_OBSERVATION_UNAVAILABLE", noVerifier.Diagnostic);
        Assert.Equal(1, storage.Open().Read().Sequence);
    }

    [Fact]
    public void Durable_cas_refuses_stale_writes_from_parallel_readers()
    {
        using var storage = new Storage();
        var actions = new CertifiedActions();
        actions.Load(Evidence(1, 0, 2));
        var result = Prepare(actions, storage.Open(), true);
        var original = storage.Open().Read();
        Assert.Equal(1, original.Sequence);
        var candidate = original.Entry! with { Sequence = 2 };

        Assert.Equal(ProfessionProductionJournalStatus.Current,
            storage.Open().CompareExchange(1, candidate));
        Assert.Equal(ProfessionProductionJournalStatus.Conflict,
            storage.Open().CompareExchange(1, candidate));
        Assert.Equal(2, storage.Open().Read().Sequence);
    }

    [Fact]
    public void Corrupted_journal_is_preserved_and_prevents_new_guidance()
    {
        using var storage = new Storage();
        var actions = new CertifiedActions();
        actions.Load(Evidence(1, 0, 2));
        Prepare(actions, storage.Open(), true);
        byte[] corruption = { 0x7B, 0x30, 0x0A, 0x00, 0xFF };
        File.WriteAllBytes(storage.PathName, corruption);

        var fail = Prepare(actions, storage.Open(), true);
        Assert.Equal("JOURNAL_CORRUPT", fail.Diagnostic);
        Assert.Equal(corruption, File.ReadAllBytes(storage.PathName));
        Assert.False(fail.Written);
    }

    [Fact]
    public void Deleting_a_checkpoint_but_leaving_its_durable_anchor_blocks_restart()
    {
        using var storage = new Storage();
        var actions = new CertifiedActions();
        actions.Load(Evidence(1, 0, 2));
        Prepare(actions, storage.Open(), true);
        Assert.True(File.Exists(storage.PathName + ".anchor"));
        File.Delete(storage.PathName);

        var denied = Prepare(actions, storage.Open(), true);
        Assert.Equal("JOURNAL_CORRUPT", denied.Diagnostic);
        Assert.False(denied.Written);
        Assert.False(File.Exists(storage.PathName));
    }

    [Fact]
    public void Order_recipe_mismatch_or_loss_of_observation_refuses_guidance()
    {
        using var storage = new Storage();
        var actions = new CertifiedActions();
        actions.Load(Evidence(1, 0, 2));
        var differentOrder = Order() with
        { Recipe = Order().Recipe with { Inputs = new[] { new ItemCraftProductionInput(10, 3, false) } } };
        Assert.Equal("RECIPE_OR_PROVENANCE_UNAVAILABLE",
            Prepare(actions, storage.Open(), true, differentOrder).Diagnostic);
        actions.ThrowOnRead = true;
        Assert.Equal("CERTIFIED_OBSERVATION_UNAVAILABLE",
            Prepare(actions, storage.Open(), true).Diagnostic);
        Assert.Equal(ProfessionProductionJournalStatus.Empty, storage.Open().Read().Status);
    }

    [Fact]
    public void Existing_profession_mcp_entry_exposes_manual_next_action_only_with_host_context()
    {
        using var storage = new Storage();
        var actions = new CertifiedActions();
        actions.Load(Evidence(1, 0, 2));
        var port = new ProfessionMcpProductionContext(
            new ItemCraftProductionRun(), Order(), actions, storage.Open());
        var snapshot = Catalog();

        using var withoutHost = JsonDocument.Parse(ProfessionMcpFacade.QueryJson(
            "{\"op\":\"production_next_action\",\"activateProduction\":true}", snapshot));
        Assert.Equal("UNKNOWN", withoutHost.RootElement.GetProperty("status").GetString());
        using var preview = JsonDocument.Parse(ProfessionMcpFacade.QueryJson(
            "{\"op\":\"production_next_action\"}", snapshot, null, port));
        Assert.Equal("KNOWN", preview.RootElement.GetProperty("status").GetString());
        Assert.Equal("NEXT_ACTION_REQUIRES_EXPLICIT_OPT_IN",
            preview.RootElement.GetProperty("diagnostic").GetString());
        Assert.Equal("GATHER", preview.RootElement.GetProperty("nextAction")
            .GetProperty("kind").GetString());
        Assert.False(preview.RootElement.GetProperty("checkpointWritten").GetBoolean());
        Assert.Equal(ProfessionProductionJournalStatus.Empty, storage.Open().Read().Status);

        using var activated = JsonDocument.Parse(ProfessionMcpFacade.QueryJson(
            "{\"op\":\"production_next_action\",\"activateProduction\":true}",
            snapshot, null, port));
        Assert.Equal("GUIDED_ACTION_CHECKPOINTED",
            activated.RootElement.GetProperty("diagnostic").GetString());
        Assert.True(activated.RootElement.GetProperty("checkpointWritten").GetBoolean());
        Assert.False(activated.RootElement.GetProperty("executed").GetBoolean());
        Assert.False(activated.RootElement.GetProperty("acknowledged").GetBoolean());
        Assert.Equal(0, actions.MutatingCalls);

        foreach (var forged in new[]
        {
            "{\"op\":\"production_next_action\",\"activateProduction\":true,\"inventory\":[]}",
            "{\"op\":\"production_next_action\",\"activateProduction\":true,\"receipt\":\"fake\"}",
            "{\"op\":\"production_status\",\"activateProduction\":true}",
            "{\"op\":\"plan_job\",\"activateProduction\":true,\"jobId\":7,\"startLevel\":20,\"targetLevel\":20}"
        })
        {
            using var invalid = JsonDocument.Parse(ProfessionMcpFacade.QueryJson(
                forged, snapshot, null, port));
            Assert.Equal("INVALID_REQUEST", invalid.RootElement.GetProperty("status").GetString());
        }
    }

    // Original production port intentionally lacks revisions, certified source
    // capacities and independent receipt verification.
    private sealed class LegacyActions : IItemCraftProductionActions
    {
        public int BagCount(long gid) => 0;
        public int ChestCount(long gid) => 0;
        public IReadOnlyList<MapInteractive> Interactives() => Array.Empty<MapInteractive>();
        public Task<bool> OpenChestAsync(CancellationToken ct) => Task.FromResult(false);
        public Task<int> WithdrawAsync(long gid, int quantity, CancellationToken ct) => Task.FromResult(0);
        public Task<bool> CloseChestAsync(CancellationToken ct) => Task.FromResult(false);
        public Task<long> LotPriceAsync(long gid, int qty, CancellationToken ct) => Task.FromResult(0L);
        public Task<bool> BuyAsync(long gid, int qty, long maxPrice, CancellationToken ct) => Task.FromResult(false);
        public Task<ItemCraftWorkshopReceipt> CraftAsync(int elementId, int skillInstanceUid,
            IReadOnlyList<(long Gid, int Qty)> ingredients, int count, CancellationToken ct) =>
            Task.FromResult(new ItemCraftWorkshopReceipt(false, 0, 0));
        public Task<bool> ListAsync(long gid, int quantity, long price, CancellationToken ct) =>
            Task.FromResult(false);
    }
}
