extern alias F06App;

using System.Reflection;
using Xunit;
using F06App::GivreClone.App.Services;
using F06App::GivreClone.App.ViewModels;
using GivreClone.Engine.Recovery;

namespace GivreClone.Tests;

public sealed class SmithCrushProvenanceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "givre-crush-provenance-" + Guid.NewGuid());
    private readonly SmithCrushOwner _owner = new("test-crushing", 354, 123);
    private string Ledger => Path.Combine(_directory, "provenance.json");
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private ForgeHistoryEntry Support(long cursor = 1) => new(cursor, DateTimeOffset.UtcNow,
        Guid.NewGuid(), 101, _owner.AccountId, "support", 7, 30, null, null, null, null, null, null,
        new Dictionary<int, JobProgressSnapshot>());

    [Fact]
    public void Placement_proves_one_not_the_mixed_stack_and_repeated_placements_do_not_add_units()
    {
        var store = new SmithCrushProvenanceStore(Ledger);
        var proof = Support();
        store.Observe(_owner, proof, 30, 10);
        store.Observe(_owner, proof, 30, 10);
        store.Observe(_owner, Support(2), 30, 10);
        Assert.Equal(1, store.Available(_owner, 7, 30, 10));
        Assert.Equal(0, store.Available(_owner with { CharacterId = 124 }, 7, 30, 10));
        Assert.Equal(0, store.Available(_owner with { ServerId = 355 }, 7, 30, 10));
    }

    [Fact]
    public void Retirement_survives_restart_and_both_identical_and_new_history_replay()
    {
        var store = new SmithCrushProvenanceStore(Ledger);
        var proof = Support();
        store.Observe(_owner, proof, 30, 10);
        Assert.True(store.Retire(_owner, 7, 30));
        store.Observe(_owner, proof, 30, 9);
        Assert.Equal(0, store.Available(_owner, 7, 30, 9));
        var restarted = new SmithCrushProvenanceStore(Ledger);
        restarted.Observe(_owner, proof, 30, 9);
        restarted.Observe(_owner, Support(3), 30, 9);
        Assert.Equal(0, restarted.Available(_owner, 7, 30, 9));
    }

    [Fact]
    public void Unobserved_restart_and_changed_stack_fail_closed_without_reacquiring_on_quantity_restore()
    {
        var store = new SmithCrushProvenanceStore(Ledger);
        store.Observe(_owner, Support(), 30, 10);
        Assert.Equal(0, new SmithCrushProvenanceStore(Ledger).Available(_owner, 7, 30, 10));
        store.Reconcile(_owner, _ => (30, 9));
        store.Reconcile(_owner, _ => (30, 10));
        store.Observe(_owner, Support(2), 30, 10);
        Assert.Equal(0, store.Available(_owner, 7, 30, 10));
    }

    [Fact]
    public void Explicit_unknown_item_is_tombstoned_before_dispatch_too()
    {
        var store = new SmithCrushProvenanceStore(Ledger);
        Assert.True(store.Retire(_owner, 7, 30));
        store.Observe(_owner, Support(), 30, 10);
        Assert.Equal(0, store.Available(_owner, 7, 30, 10));
    }

    [Fact]
    public void Corruption_and_write_failure_refuse_authorization()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Ledger, "{");
        var corrupt = new SmithCrushProvenanceStore(Ledger);
        Assert.NotNull(corrupt.StorageError);
        Assert.False(corrupt.Retire(_owner, 7, 30));
        var blocked = new SmithCrushProvenanceStore(_directory);
        Assert.False(blocked.Retire(_owner, 7, 30));
        Assert.NotNull(blocked.StorageError);
        Assert.Equal(0, blocked.Available(_owner, 7, 30, 10));
    }

    private (SmithMagicViewModel Vm, Action<ForgeHistoryEntry> Add) Picker()
    {
        var entries = new List<ForgeHistoryEntry> { Support() };
        var vm = new SmithMagicViewModel
        {
            CrushProvenance = new(Ledger),
            CrushOwnerOf = _ => _owner,
            CrushHistoryHeadOf = (_, _) => entries[^1].Cursor,
            CrushHistorySince = (_, _, cursor) => new(entries.Where(e => e.Cursor > cursor).ToArray(),
                [], entries[^1].Cursor, false),
            CrushQuantityOf = (_, uid) => uid == 7 ? 10 : 6,
            CrushGidOf = (_, uid) => uid == 7 ? 30 : 31,
            BindingOf = (_, _) => ItemBind.None,
        };
        typeof(SmithMagicViewModel).GetField("_pid", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, 101);
        vm.Picks.Add(new() { ObjectUid = 7, Gid = 30, Name = "Session", Quantity = 10, InBag = true, HasSlot = true });
        vm.Picks.Add(new() { ObjectUid = 8, Gid = 31, Name = "Autre", Quantity = 6, InBag = true, HasSlot = true });
        vm.PickCrushCommand.Execute(null);
        return (vm, entries.Add);
    }

    [Fact]
    public void Default_picker_ignores_old_history_then_offers_only_one_observed_session_unit()
    {
        var (vm, add) = Picker();
        Assert.False(vm.IncludeOtherCrushItems);
        Assert.Empty(vm.FilteredPicks);
        add(Support(2));
        vm.BagSearch = "Session";
        var row = Assert.Single(vm.FilteredPicks);
        Assert.Equal(1, row.CrushMaximum);
        row.IsChecked = true;
        row.CrushQuantity = 2;
        vm.AddCheckedToCrushCommand.Execute(null);
        Assert.Empty(vm.CrushQueue);
        row.IsChecked = true;
        row.CrushQuantity = 1;
        vm.AddCheckedToCrushCommand.Execute(null);
        Assert.Equal(1, Assert.Single(vm.CrushQueue).Qty);
    }

    [Fact]
    public void Explicit_picker_previews_individual_quantities_and_return_to_default_clears_consent()
    {
        var (vm, _) = Picker();
        vm.IncludeOtherCrushItems = true;
        Assert.Equal(2, vm.FilteredPicks.Count);
        var first = vm.FilteredPicks[0];
        var second = vm.FilteredPicks[1];
        first.CrushQuantity = 3;
        second.CrushQuantity = 2;
        first.IsChecked = second.IsChecked = true;
        vm.AddCheckedToCrushCommand.Execute(null);
        Assert.Equal(new[] { 3, 2 }, vm.CrushQueue.Select(r => r.Qty));
        vm.IncludeOtherCrushItems = false;
        Assert.Empty(vm.CrushQueue);
        Assert.Empty(vm.FilteredPicks);
    }

    [Fact]
    public void Losing_character_identity_retires_session_proof_even_if_the_same_bag_returns()
    {
        var (vm, add) = Picker();
        add(Support(2));
        vm.BagSearch = "Session";
        Assert.Single(vm.FilteredPicks);
        vm.CrushOwnerOf = _ => null;
        vm.BagSearch = "";
        Assert.Empty(vm.FilteredPicks);
        vm.CrushOwnerOf = _ => _owner;
        vm.BagSearch = "Session";
        Assert.Empty(vm.FilteredPicks);
    }
}
