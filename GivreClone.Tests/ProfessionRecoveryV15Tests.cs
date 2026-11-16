using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.ItemCraftPlanner;
using GivreClone.App.Services.Production;
using GivreClone.App.Services.ProfessionMcp;
using GivreClone.App.Services.ProfessionPlanner;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Tests of the real production runner, V9 attempt journal and profession
/// production-entry API. The independent receipt ledger is a test fixture;
/// there is still no real host adapter supplying this evidence in production.
/// </summary>
public sealed class ProfessionRecoveryV15Tests
{
    private const string Version = "3.7.13.17";
    private const string Profile = "server:292:profession-v15-test";
    private const string Subject = "character-profession-v15-test";
    private const string Session = "session-v15";
    private static readonly string Manifest = new('A', 64);

    private static ProfessionMcpSnapshot Catalog() => new(
        new ProfessionPlanRequest(new ProfessionGoal(7, "Test profession", 20, 20),
            TrainingRecipeId: 120, RequestedCrafts: 1,
            Recipes: new[] { new ProfessionRecipe(120, 200, 1,
                new[] { new RecipeIngredient(10, 2) }, 20, 7,
                SourceVersion: Version, Verified: true,
                SecondsPerCraft: 1, KamasPerCraft: 0) },
            Inventory: Array.Empty<ProfessionInventoryStack>(),
            Sources: Array.Empty<MaterialSource>()),
        new ProfessionMcpProvenance("local-owned", "strict-v15", Version,
            Manifest, true, "independent-review"));

    private static ItemCraftProductionRequest Order() => new(
        new ItemCraftProductionRecipe(200, 1, 10, 0,
            new[] { new ItemCraftProductionInput(10, 2, false) }),
        1, 0, BankAllowed: false, SaleFloor: null);

    private static ProfessionMcpRecoveryEnvelope Evidence(long revision, long ingredients,
        long remainingSource, long output = 0)
    {
        var inventory = new List<ProfessionInventoryStack>();
        if (ingredients > 0) inventory.Add(new("ingredient-stack", 10, ingredients, "bag"));
        if (output > 0) inventory.Add(new("crafted-stack", 200, output, "bag"));
        var sources = new[] { new MaterialSource(10, AcquisitionMethod.Gather, 1,
            0, Version, true, MaxUnits: remainingSource) };
        var observation = new ProfessionRecoveryObservation(Subject, revision, Version,
            true, true, inventory, sources);
        var receipt = new ProfessionMcpRecoveryReceipt(Subject, Session, revision, Version,
            Manifest, ProfessionMcpRecoveryIntegration.InventoryDigest(inventory),
            ProfessionMcpRecoveryIntegration.SourceCapsDigest(sources),
            "observed-" + revision);
        return new(observation, receipt);
    }

    private sealed class Files : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(),
            "givre-profession-v15-" + Guid.NewGuid().ToString("N"));
        public string ProfessionPath => Path.Combine(DirectoryPath, "profession.json");
        public string V9Path => Path.Combine(DirectoryPath, "itemcraft.journal");
        public Files() => Directory.CreateDirectory(DirectoryPath);
        public ProfessionProductionFileJournal ProfessionJournal() => new(ProfessionPath);
        public ItemCraftProductionAttemptJournal V9Journal() => new(V9Path);
        public void Dispose() => Directory.Delete(DirectoryPath, true);
    }

    private sealed class Port : IProfessionProductionV9Evidence,
        IItemCraftProductionStockObservation
    {
        private readonly Dictionary<string, ProfessionMcpRecoveryReceipt> _observations =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, ProfessionProductionCraftWitness> _crafts =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, ProfessionProductionV9CraftReceipt> _links =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, ProfessionProductionAcquisitionReceipt> _acquisitions =
            new(StringComparer.Ordinal);
        private readonly Dictionary<long, int> _bag = new();

        public Port(Files files)
        {
            V9Journal = files.V9Journal();
            ProfessionJournal = files.ProfessionJournal();
        }

        public ProfessionMcpRecoveryEnvelope? Current { get; private set; }
        public string V9Profile => Profile;
        public ItemCraftProductionAttemptJournal V9Journal { get; }
        public ProfessionProductionFileJournal ProfessionJournal { get; }
        public bool SupportsStockObservation => true;
        public int CraftCalls { get; private set; }
        public int OtherMutations { get; private set; }

        public void Observe(ProfessionMcpRecoveryEnvelope envelope)
        {
            Current = envelope;
            _observations[envelope.Receipt.ReceiptId] = envelope.Receipt;
            _bag.Clear();
            foreach (var stack in envelope.Observation.Inventory)
                _bag[stack.ItemGid] = checked(_bag.GetValueOrDefault(stack.ItemGid) +
                    (int)stack.Quantity);
        }

        public void RegisterCraft(ProfessionProductionPendingAction action,
            ItemCraftAttemptLease lease, long revision, int quantity = 1)
        {
            var witness = new ProfessionProductionCraftWitness(action.ActionKey, Session,
                revision, action.RecipeId!.Value, action.ItemGid, quantity,
                "server-craft-" + lease.RunId.ToString("N"));
            _crafts[action.ActionKey] = witness;
            _links[action.ActionKey] = new ProfessionProductionV9CraftReceipt(
                action.ActionKey, Subject, Session, revision, action.RecipeId.Value,
                witness.ReceiptId, lease.Profile, lease.RunId, lease.Pid, lease.RequestHash,
                new ItemCraftWorkshopReceipt(true, quantity, action.ItemGid));
        }

        public void RegisterAcquisition(ProfessionProductionPendingAction action,
            long revision, long quantity)
        {
            _acquisitions[action.ActionKey] = new ProfessionProductionAcquisitionReceipt(
                action.ActionKey, Subject, Session, revision, action.Kind,
                action.ItemGid, quantity, "acquisition-" + action.ActionKey);
        }

        public void OverrideV9Link(ProfessionProductionPendingAction action,
            Func<ProfessionProductionV9CraftReceipt, ProfessionProductionV9CraftReceipt> change)
            => _links[action.ActionKey] = change(_links[action.ActionKey]);

        public ProfessionMcpRecoveryContext ReadRecoveryContext() =>
            new(true, Current, receipt => _observations.TryGetValue(receipt.ReceiptId, out var saved) &&
                saved == receipt);
        public ProfessionProductionCraftWitness? ReadCraftWitness(string key) =>
            _crafts.GetValueOrDefault(key);
        public bool VerifyCraftWitness(ProfessionProductionCraftWitness witness) =>
            _crafts.TryGetValue(witness.ActionKey, out var saved) && saved == witness;
        public ProfessionProductionV9CraftReceipt? ReadV9CraftReceipt(string key) =>
            _links.GetValueOrDefault(key);
        public bool VerifyV9CraftReceipt(ProfessionProductionV9CraftReceipt receipt) =>
            _links.TryGetValue(receipt.ActionKey, out var saved) && saved == receipt;
        public ProfessionProductionAcquisitionReceipt? ReadAcquisitionReceipt(string key) =>
            _acquisitions.GetValueOrDefault(key);
        public bool VerifyAcquisitionReceipt(ProfessionProductionAcquisitionReceipt receipt) =>
            _acquisitions.TryGetValue(receipt.ActionKey, out var saved) && saved == receipt;

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
        { OtherMutations++; return Task.FromResult(false); }
        public Task<int> WithdrawAsync(long gid, int qty, CancellationToken ct)
        { OtherMutations++; return Task.FromResult(0); }
        public Task<bool> CloseChestAsync(CancellationToken ct)
        { OtherMutations++; return Task.FromResult(false); }
        public Task<long> LotPriceAsync(long gid, int qty, CancellationToken ct) =>
            Task.FromResult(0L);
        public Task<bool> BuyAsync(long gid, int qty, long maxPrice, CancellationToken ct)
        { OtherMutations++; return Task.FromResult(false); }
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
        { OtherMutations++; return Task.FromResult(false); }
    }

    private static ProfessionProductionGuidance Prepare(Port port, bool activate) =>
        new ItemCraftProductionRun().PrepareVerifiedNextAction(Catalog(), Order(),
            port, port.ProfessionJournal, activate);

    [Fact]
    public async Task Real_v9_completed_attempt_and_independent_exact_receipt_close_one_craft()
    {
        using var files = new Files();
        var port = new Port(files);
        port.Observe(Evidence(1, 2, 0));
        var first = Prepare(port, true);
        Assert.Equal("GUIDED_ACTION_CHECKPOINTED", first.Diagnostic);
        Assert.Equal("CRAFT", first.NextAction!.Kind);
        Assert.Equal("V9_EXACT", port.ProfessionJournal.Read().Entry!.Pending!.EvidenceContract);

        var duplicate = Prepare(port, true);
        Assert.Equal("SESSION_OR_REVISION_STALE", duplicate.Diagnostic);
        Assert.Equal(1, port.ProfessionJournal.Read().Sequence);

        var order = Order();
        Assert.Equal(ItemCraftAttemptDecision.Allowed,
            port.V9Journal.TryBegin(Profile, 377, order, out var lease));
        var produced = await new ItemCraftProductionRun().RunAsync(order, port, null,
            CancellationToken.None);
        Assert.Equal(ItemCraftProductionStop.QuantityReached, produced.Stop);
        Assert.Equal(1, produced.Produced);
        Assert.True(port.V9Journal.TryFinish(lease, fullyConfirmed: true));
        port.Observe(Evidence(2, 0, 0, output: 1));
        port.RegisterCraft(first.NextAction, lease, 2);

        var final = Prepare(port, true);
        Assert.Equal("TARGET_OBSERVED_CHECKPOINTED", final.Diagnostic);
        Assert.Equal(2, final.JournalSequence);
        Assert.Null(port.ProfessionJournal.Read().Entry!.Pending);
        Assert.Equal(1, port.CraftCalls);
        Assert.Equal(0, port.OtherMutations);
        Assert.False(Prepare(port, true).Written);
    }

    [Fact]
    public void Started_or_uncertain_v9_attempt_cannot_be_recovered_as_confirmed_craft()
    {
        using var files = new Files();
        var port = new Port(files);
        port.Observe(Evidence(1, 2, 0));
        var pending = Prepare(port, true).NextAction!;
        Assert.Equal(ItemCraftAttemptDecision.Allowed,
            port.V9Journal.TryBegin(Profile, 378, Order(), out var lease));
        port.Observe(Evidence(2, 0, 0, output: 1));
        port.RegisterCraft(pending, lease, 2);

        Assert.Equal("V9_CRAFT_RECEIPT_OR_ATTEMPT_UNVERIFIED",
            Prepare(port, true).Diagnostic);
        Assert.True(port.V9Journal.TryFinish(lease, fullyConfirmed: false));
        Assert.Equal("V9_CRAFT_RECEIPT_OR_ATTEMPT_UNVERIFIED",
            Prepare(port, true).Diagnostic);
        Assert.Equal(1, port.ProfessionJournal.Read().Sequence);
        Assert.Equal(0, port.CraftCalls);
    }

    [Fact]
    public void Manual_stock_or_wrong_run_cannot_authorize_replay_or_a_new_craft()
    {
        using var files = new Files();
        var port = new Port(files);
        port.Observe(Evidence(1, 2, 0));
        var pending = Prepare(port, true).NextAction!;
        Assert.Equal(ItemCraftAttemptDecision.Allowed,
            port.V9Journal.TryBegin(Profile, 379, Order(), out var lease));
        Assert.True(port.V9Journal.TryFinish(lease, fullyConfirmed: true));
        port.Observe(Evidence(2, 0, 0, output: 1));

        // An unrelated completed V9 attempt and observed output are insufficient.
        Assert.Equal("AWAITING_INDEPENDENT_OBSERVATION_OR_CRAFT_RECEIPT",
            Prepare(port, true).Diagnostic);
        port.RegisterCraft(pending, lease, 2);
        port.OverrideV9Link(pending, link => link with { RunId = Guid.NewGuid() });
        Assert.Equal("V9_CRAFT_RECEIPT_OR_ATTEMPT_UNVERIFIED",
            Prepare(port, true).Diagnostic);
        Assert.Equal(1, port.ProfessionJournal.Read().Sequence);

        // Repeating the inventory snapshot with a later revision cannot turn
        // the previously mismatched run into an admitted V9 craft.
        port.Observe(Evidence(3, 0, 0, output: 1));
        Assert.False(Prepare(port, true).Written);
        Assert.Equal(0, port.CraftCalls);
    }

    [Fact]
    public void Gather_repetition_needs_independent_receipts_and_exact_cap_consumption()
    {
        using var files = new Files();
        var port = new Port(files);
        port.Observe(Evidence(10, 0, 2));
        var first = Prepare(port, true);
        Assert.Equal("GATHER", first.NextAction!.Kind);
        port.Observe(Evidence(11, 1, 1));
        Assert.Equal("V9_ACQUISITION_RECEIPT_UNVERIFIED",
            Prepare(port, true).Diagnostic);
        port.RegisterAcquisition(first.NextAction, 11, 1);
        var second = Prepare(port, true);
        Assert.Equal("GUIDED_ACTION_CHECKPOINTED", second.Diagnostic);
        Assert.Equal("GATHER", second.NextAction!.Kind);
        Assert.Equal(1, second.NextAction.Quantity);

        // Reusing the old receipt or presenting a fresh bag with unchanged
        // remaining supply cannot authorize the second gather.
        port.Observe(Evidence(12, 2, 1));
        port.RegisterAcquisition(second.NextAction, 12, 1);
        Assert.Equal("V9_ACQUISITION_RECEIPT_UNVERIFIED",
            Prepare(port, true).Diagnostic);
        port.Observe(Evidence(13, 2, 0));
        Assert.Equal("V9_ACQUISITION_RECEIPT_UNVERIFIED",
            Prepare(port, true).Diagnostic); // receipt still binds revision 12
        port.RegisterAcquisition(second.NextAction, 13, 1);
        var ready = Prepare(port, true);
        Assert.Equal("GUIDED_ACTION_CHECKPOINTED", ready.Diagnostic);
        Assert.Equal("CRAFT", ready.NextAction!.Kind);
        Assert.Equal(3, ready.JournalSequence);
        Assert.Equal(0, port.CraftCalls);
        Assert.Equal(0, port.OtherMutations);
    }

    [Fact]
    public void V9_contract_survives_restart_and_blocks_adapter_downgrade()
    {
        using var files = new Files();
        var port = new Port(files);
        port.Observe(Evidence(1, 2, 0));
        Prepare(port, true);
        var old = files.ProfessionJournal().Read();
        Assert.Equal("V9_EXACT", old.Entry!.Pending!.EvidenceContract);
        var restarted = files.ProfessionJournal();
        Assert.Equal(ProfessionProductionJournalStatus.Current, restarted.Read().Status);
        var downgraded = new CertifiedDowngrade(port);
        port.Observe(Evidence(2, 0, 0, output: 1));
        var denied = new ItemCraftProductionRun().PrepareVerifiedNextAction(Catalog(),
            Order(), downgraded, restarted, true);
        Assert.Equal("V9_CERTIFIED_PORT_MISSING", denied.Diagnostic);
        Assert.Null(denied.NextAction);
        Assert.False(denied.Written);
        Assert.Equal(old.Sequence, restarted.Read().Sequence);
    }

    [Fact]
    public void Oversized_or_damaged_anchor_remains_quarantined_and_blocks_recovery()
    {
        using var files = new Files();
        var port = new Port(files);
        port.Observe(Evidence(1, 2, 0));
        Prepare(port, true);
        byte[] damagedAnchor = new byte[8192];
        Array.Fill(damagedAnchor, (byte)0x5A);
        File.WriteAllBytes(files.ProfessionPath + ".anchor", damagedAnchor);

        Assert.Equal(ProfessionProductionJournalStatus.Corrupt,
            files.ProfessionJournal().Read().Status);
        Assert.Equal("JOURNAL_CORRUPT", Prepare(port, true).Diagnostic);
        Assert.Equal(damagedAnchor, File.ReadAllBytes(files.ProfessionPath + ".anchor"));
        Assert.Equal(0, port.CraftCalls);
    }

    // The same observation API is still exposed, but no V9 receipt/attempt
    // verifier survives this adapter change. The durable evidence-contract bit
    // prevents falling back to the less demanding advisory reconciliation.
    private sealed class CertifiedDowngrade : IProfessionProductionCertifiedActions
    {
        private readonly Port _port;
        public CertifiedDowngrade(Port port) => _port = port;
        public ProfessionMcpRecoveryContext ReadRecoveryContext() => _port.ReadRecoveryContext();
        public ProfessionProductionCraftWitness? ReadCraftWitness(string key) =>
            _port.ReadCraftWitness(key);
        public bool VerifyCraftWitness(ProfessionProductionCraftWitness witness) =>
            _port.VerifyCraftWitness(witness);
        public int BagCount(long gid) => _port.BagCount(gid);
        public int ChestCount(long gid) => _port.ChestCount(gid);
        public IReadOnlyList<MapInteractive> Interactives() => _port.Interactives();
        public Task<bool> OpenChestAsync(CancellationToken ct) => _port.OpenChestAsync(ct);
        public Task<int> WithdrawAsync(long gid, int qty, CancellationToken ct) =>
            _port.WithdrawAsync(gid, qty, ct);
        public Task<bool> CloseChestAsync(CancellationToken ct) => _port.CloseChestAsync(ct);
        public Task<long> LotPriceAsync(long gid, int qty, CancellationToken ct) =>
            _port.LotPriceAsync(gid, qty, ct);
        public Task<bool> BuyAsync(long gid, int qty, long maxPrice, CancellationToken ct) =>
            _port.BuyAsync(gid, qty, maxPrice, ct);
        public Task<ItemCraftWorkshopReceipt> CraftAsync(int elementId, int uid,
            IReadOnlyList<(long Gid, int Qty)> ingredients, int count, CancellationToken ct) =>
            _port.CraftAsync(elementId, uid, ingredients, count, ct);
        public Task<bool> ListAsync(long gid, int qty, long price, CancellationToken ct) =>
            _port.ListAsync(gid, qty, price, ct);
    }
}
