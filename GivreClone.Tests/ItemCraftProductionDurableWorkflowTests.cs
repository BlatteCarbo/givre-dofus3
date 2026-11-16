extern alias F06App;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;
using Decision = F06App::GivreClone.App.Services.Production.ItemCraftAttemptDecision;
using Phase = F06App::GivreClone.App.Services.Production.ItemCraftAttemptPhase;
using Journal = F06App::GivreClone.App.Services.Production.ItemCraftProductionAttemptJournal;
using BoundActions = F06App::GivreClone.App.Services.Production.ItemCraftSessionBoundActions;
using Recipe = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionRecipe;
using Input = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionInput;
using Request = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionRequest;
using Receipt = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftWorkshopReceipt;
using ActionPort = F06App::GivreClone.App.Services.ItemCraftPlanner.IItemCraftProductionActions;
using Xunit;

namespace GivreClone.Tests;

public sealed class ItemCraftProductionDurableWorkflowTests
{
    private const string Character = "server:292:Cra-Test";

    private sealed class Sandbox : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(),
            "givre-itemcraft-run-" + Guid.NewGuid().ToString("N"));

        public string PathName => Path.Combine(_dir, "production", "itemcraft-attempts-v1.journal");
        public Journal Open() => new(PathName);
        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static Request Order(bool bank = true) => new(
        new Recipe(2034, 1, 162, 0, new[] { new Input(510, 2, false),
            new Input(511, 1, true) }),
        2, 1000, bank, 120);

    [Fact]
    public void Durable_intent_is_visible_after_crash_and_blocks_new_action_or_pid()
    {
        using var files = new Sandbox();
        var first = files.Open();
        Assert.Equal(Decision.Allowed, first.TryBegin(Character, 55, Order(), out var lease));
        Assert.NotEqual(Guid.Empty, lease.RunId);
        Assert.Equal(Phase.Started, files.Open().ReadStatus(Character).Phase);
        Assert.Equal(Decision.NeedsReconciliation,
            files.Open().TryBegin(Character, 55, Order(), out _));
        Assert.Equal(Decision.NeedsReconciliation,
            files.Open().TryBegin(Character, 77, Order(bank: false), out _));
        // An unrelated process cannot use a copied journal UUID to certify
        // the old bank+craft operation as completed.
        Assert.False(files.Open().TryFinish(lease, fullyConfirmed: true));
        Assert.Equal(Phase.Started, files.Open().ReadStatus(Character).Phase);
    }

    [Fact]
    public void Confirmed_complete_run_allows_next_explicit_production_without_replay()
    {
        using var files = new Sandbox();
        var journal = files.Open();
        Assert.Equal(Decision.Allowed, journal.TryBegin(Character, 11, Order(), out var lease));
        Assert.True(journal.TryFinish(lease, fullyConfirmed: true));
        Assert.Equal(Phase.Completed, files.Open().ReadStatus(Character).Phase);
        Assert.False(journal.TryFinish(lease, fullyConfirmed: true));
        Assert.Equal(Decision.Allowed, files.Open().TryBegin(Character, 12,
            Order(bank: false), out _));
        Assert.Equal(Phase.Started, files.Open().ReadStatus(Character).Phase);
    }

    [Fact]
    public void Partial_craft_or_unconfirmed_listing_keeps_journal_quarantined()
    {
        using var files = new Sandbox();
        var journal = files.Open();
        Assert.Equal(Decision.Allowed, journal.TryBegin(Character, 11, Order(), out var lease));
        Assert.True(journal.TryFinish(lease, fullyConfirmed: false));
        Assert.Equal(Phase.Uncertain, files.Open().ReadStatus(Character).Phase);
        Assert.Equal(Decision.NeedsReconciliation, files.Open().TryBegin(Character,
            11, Order(), out _));
    }

    [Fact]
    public async Task Concurrent_instances_can_start_only_one_production_for_character()
    {
        using var files = new Sandbox();
        var a = files.Open();
        var b = files.Open();
        var t1 = Task.Run(() => a.TryBegin(Character, 1, Order(), out _));
        var t2 = Task.Run(() => b.TryBegin(Character, 2, Order(), out _));
        var both = await Task.WhenAll(t1, t2);
        Assert.Single(both, d => d == Decision.Allowed);
        Assert.Single(both, d => d == Decision.NeedsReconciliation);
    }

    [Fact]
    public void Corrupt_tail_and_incomplete_commit_never_turn_into_empty_production_history()
    {
        using var files = new Sandbox();
        Assert.Equal(Decision.Allowed, files.Open().TryBegin(Character, 13, Order(), out _));
        string first = File.ReadAllText(files.PathName);
        File.AppendAllText(files.PathName, "2|incomplete-unknown-production\n");
        Assert.Equal(Phase.Uncertain, files.Open().ReadStatus(Character).Phase);
        Assert.Equal(Decision.Unavailable, files.Open().TryBegin(Character, 22, Order(), out _));
        Assert.StartsWith(first, File.ReadAllText(files.PathName));
    }

    [Fact]
    public void Missing_anchor_preserves_source_and_blocks_rerun()
    {
        using var files = new Sandbox();
        Assert.Equal(Decision.Allowed, files.Open().TryBegin(Character, 13, Order(), out _));
        string before = File.ReadAllText(files.PathName);
        File.WriteAllText(files.PathName + ".anchor", "old-version");
        Assert.Equal(Phase.Uncertain, files.Open().ReadStatus(Character).Phase);
        Assert.Equal(Decision.Unavailable, files.Open().TryBegin(Character, 22, Order(), out _));
        Assert.Equal(before, File.ReadAllText(files.PathName));
    }

    [Fact]
    public void Canonical_fingerprint_is_independent_of_ingredient_iteration_order()
    {
        var first = Order();
        var recipe = first.Recipe with { Inputs = new[] { new Input(511, 1, true), new Input(510, 2, false) } };
        Assert.Equal(Journal.RequestFingerprint(first),
            Journal.RequestFingerprint(first with { Recipe = recipe }));
        Assert.NotEqual(Journal.RequestFingerprint(first),
            Journal.RequestFingerprint(Order(bank: false)));
    }

    [Fact]
    public async Task Actual_action_port_blocks_next_bank_step_when_session_changes_and_preserves_uncertainty()
    {
        using var files = new Sandbox();
        var journal = files.Open();
        Assert.Equal(Decision.Allowed, journal.TryBegin(Character, 91, Order(), out var lease));
        bool current = true;
        var fake = new FakeActions();
        var bound = new BoundActions(fake, () => current);
        Assert.True(await bound.OpenChestAsync(CancellationToken.None));
        current = false; // disconnect/reused PID before attempting withdrawal
        Assert.Equal(0, await bound.WithdrawAsync(510, 2, CancellationToken.None));
        Assert.Equal(0, fake.WithdrawCalls);
        Assert.False(await bound.ListAsync(2034, 1, 120, CancellationToken.None));
        Assert.True(journal.TryFinish(lease, fullyConfirmed: false));
        Assert.Equal(Phase.Uncertain, files.Open().ReadStatus(Character).Phase);
        Assert.Equal(Decision.NeedsReconciliation,
            files.Open().TryBegin(Character, 91, Order(), out _));
    }

    [Fact]
    public async Task Late_craft_success_after_character_replacement_is_not_reported_as_confirmed()
    {
        bool current = true;
        var fake = new FakeActions { OnCraft = () => { current = false; return new Receipt(true, 1, 2034); } };
        var bound = new BoundActions(fake, () => current);
        var result = await bound.CraftAsync(1, 1, new[] { (510L, 2) }, 1, CancellationToken.None);
        Assert.False(result.Confirmed);
        Assert.Equal(0, result.Quantity);
        Assert.Equal(1, fake.CraftCalls);
    }

    private sealed class FakeActions : ActionPort
    {
        public int WithdrawCalls { get; private set; }
        public int CraftCalls { get; private set; }
        public Func<Receipt>? OnCraft { get; init; }
        public int BagCount(long gid) => 2;
        public int ChestCount(long gid) => 2;
        public IReadOnlyList<MapInteractive> Interactives() => Array.Empty<MapInteractive>();
        public Task<bool> OpenChestAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<int> WithdrawAsync(long gid, int quantity, CancellationToken ct)
        { WithdrawCalls++; return Task.FromResult(quantity); }
        public Task<bool> CloseChestAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<Receipt> CraftAsync(int elementId, int skillInstanceUid,
            IReadOnlyList<(long Gid, int Qty)> ingredients, int count, CancellationToken ct)
        { CraftCalls++; return Task.FromResult(OnCraft?.Invoke() ?? new Receipt(true, count, 2034)); }
        public Task<long> LotPriceAsync(long gid, int quantity, CancellationToken ct) => Task.FromResult(100L);
        public Task<bool> BuyAsync(long gid, int quantity, long maxPrice, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> ListAsync(long gid, int quantity, long price, CancellationToken ct) => Task.FromResult(true);
    }
}
