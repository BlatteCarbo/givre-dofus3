extern alias F06App;

using System.Reflection;
using GivreClone.Protocol;
using Xunit;
using CrushRunReport = F06App::GivreClone.App.Services.LaunchService.CrushRunReport;
using SmithMagicViewModel = F06App::GivreClone.App.ViewModels.SmithMagicViewModel;
using SmithQueueRow = F06App::GivreClone.App.ViewModels.SmithQueueRow;
using BagPickRow = F06App::GivreClone.App.ViewModels.BagPickRow;
using SmithCrushOwner = F06App::GivreClone.App.Services.SmithCrushOwner;
using SmithCrushProvenanceStore = F06App::GivreClone.App.Services.SmithCrushProvenanceStore;

namespace GivreClone.Tests;

public sealed class SmithCrushingQuantityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "givre-crush-tests-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    private sealed class Bag
    {
        public int Quantity = 10;
        public long Gid = 30;
        public List<int> Consumed { get; } = [];
    }

    private (SmithMagicViewModel Vm, Bag Bag) Model(int selected = 3)
    {
        var bag = new Bag();
        var vm = new SmithMagicViewModel
        {
            CrushSkill = () => 181,
            CrushProvenance = new SmithCrushProvenanceStore(Path.Combine(_directory, "provenance.json")),
            CrushOwnerOf = _ => new SmithCrushOwner("test-crushing", 1, 2),
            CrushHistoryHeadOf = (_, _) => 0,
            BindingOf = (_, _) => GivreClone.Engine.Recovery.ItemBind.None,
            CurrentMap = _ => 100,
            InteractivesForPid = _ => [new MapInteractive(100, 501, 0, 0, [new(701, 181, 0, true)])],
            CrushQuantityOf = (_, uid) => uid == 7 ? bag.Quantity : 0,
            CrushGidOf = (_, uid) => uid == 7 && bag.Quantity > 0 ? bag.Gid : null,
            CrushItems = (pid, element, skill, items, _) =>
            {
                bag.Consumed.AddRange(items.Select(item => item.Qty));
                Assert.Equal(101u, pid);
                Assert.Equal(501, element);
                Assert.Equal(701, skill);
                var item = Assert.Single(items);
                Assert.Equal(7, item.Uid);
                Assert.InRange(item.Qty, 1, bag.Quantity);
                bag.Quantity -= item.Qty;
                return Task.FromResult(new CrushRunReport(true, 1, [], 0, "termine"));
            }
        };
        SetPid(vm, 101);
        vm.IncludeOtherCrushItems = true;
        vm.Picks.Add(new BagPickRow { ObjectUid = 7, Gid = 30, Name = "Anneau", Quantity = 10,
            InBag = true, HasSlot = true, IsChecked = true, CrushQuantity = Math.Clamp(selected, 1, 10) });
        vm.PickCrushCommand.Execute(null);
        vm.AddCheckedToCrushCommand.Execute(null);
        if (selected is < 1 or > 10)
            vm.CrushQueue[0] = vm.CrushQueue[0] with { Qty = selected };
        return (vm, bag);
    }

    private static void SetPid(SmithMagicViewModel vm, int pid)
        => typeof(SmithMagicViewModel).GetField("_pid", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, pid);

    [Fact]
    public async Task Mixed_stack_consumes_only_selected_quantity_after_separate_approval()
    {
        var (vm, bag) = Model();
        await vm.StartCrushCommand.ExecuteAsync(null);
        Assert.Equal(new[] { 1 }, bag.Consumed);
        Assert.Equal(9, bag.Quantity);
        Assert.Equal(2, Assert.Single(vm.CrushQueue).Qty);
        Assert.True(vm.CanContinueCrushTrial);

        await vm.ContinueCrushTrialCommand.ExecuteAsync(null);
        Assert.Equal(new[] { 1, 2 }, bag.Consumed);
        Assert.Equal(7, bag.Quantity);
        Assert.Empty(vm.CrushQueue);
        Assert.False(vm.CanContinueCrushTrial);
        await vm.ContinueCrushTrialCommand.ExecuteAsync(null);
        Assert.Equal(new[] { 1, 2 }, bag.Consumed);
    }

    [Fact]
    public async Task Single_selected_item_does_not_queue_the_unrelated_stack_remainder()
    {
        var (vm, bag) = Model(1);
        await vm.StartCrushCommand.ExecuteAsync(null);
        Assert.Equal(new[] { 1 }, bag.Consumed);
        Assert.Equal(9, bag.Quantity);
        Assert.Empty(vm.CrushQueue);
        Assert.False(vm.CanContinueCrushTrial);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public async Task Invalid_or_unavailable_selected_quantity_cannot_start_a_trial(int selected)
    {
        var (vm, bag) = Model(selected);
        bool approached = false;
        vm.ApproachWorkshop = (_, _, _, _) =>
        {
            approached = true;
            return Task.FromResult(true);
        };
        await vm.StartCrushCommand.ExecuteAsync(null);
        Assert.False(approached);
        Assert.Empty(bag.Consumed);
        Assert.Equal(selected, Assert.Single(vm.CrushQueue).Qty);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    public async Task Changed_mixed_stack_cannot_release_the_selected_remainder(int observed)
    {
        var (vm, bag) = Model();
        await vm.StartCrushCommand.ExecuteAsync(null);
        bag.Quantity = observed;
        await vm.ContinueCrushTrialCommand.ExecuteAsync(null);
        Assert.Equal(new[] { 1 }, bag.Consumed);
        Assert.Equal(observed, bag.Quantity);
        Assert.Equal(2, Assert.Single(vm.CrushQueue).Qty);
    }

    [Theory]
    [InlineData(false, "less")]
    [InlineData(false, "more")]
    [InlineData(false, "gid")]
    [InlineData(false, "queue")]
    [InlineData(false, "pid")]
    [InlineData(true, "less")]
    [InlineData(true, "more")]
    [InlineData(true, "gid")]
    [InlineData(true, "queue")]
    [InlineData(true, "pid")]
    public async Task Final_revalidation_after_approach_rejects_inventory_selection_or_character_changes(
        bool remaining, string change)
    {
        var (vm, bag) = Model();
        if (remaining) await vm.StartCrushCommand.ExecuteAsync(null);
        vm.ApproachWorkshop = (_, _, _, _) =>
        {
            switch (change)
            {
                case "less": bag.Quantity--; break;
                case "more": bag.Quantity++; break;
                case "gid": bag.Gid++; break;
                case "queue": vm.CrushQueue.Clear(); break;
                case "pid": SetPid(vm, 102); break;
            }
            return Task.FromResult(true);
        };
        if (remaining) await vm.ContinueCrushTrialCommand.ExecuteAsync(null);
        else await vm.StartCrushCommand.ExecuteAsync(null);
        Assert.Equal(remaining ? new[] { 1 } : Array.Empty<int>(), bag.Consumed);
    }

    [Fact]
    public async Task Changed_selected_quantity_cannot_reuse_trial_approval()
    {
        var (vm, bag) = Model();
        await vm.StartCrushCommand.ExecuteAsync(null);
        vm.CrushQueue[0] = vm.CrushQueue[0] with { Qty = 1 };
        await vm.ContinueCrushTrialCommand.ExecuteAsync(null);
        Assert.Equal(new[] { 1 }, bag.Consumed);
        Assert.Equal(9, bag.Quantity);
    }

    [Fact]
    public async Task Mismatched_inventory_delta_does_not_authorize_remaining_quantity()
    {
        var (vm, bag) = Model();
        vm.CrushItems = (_, _, _, _, _) =>
        {
            bag.Quantity -= 2;
            return Task.FromResult(new CrushRunReport(true, 1, [], 0, "termine"));
        };
        await vm.StartCrushCommand.ExecuteAsync(null);
        Assert.False(vm.CanContinueCrushTrial);
        Assert.Equal(3, Assert.Single(vm.CrushQueue).Qty);
    }

    [Fact]
    public async Task Changed_inventory_identity_cannot_start_a_trial()
    {
        var (vm, bag) = Model();
        bag.Gid = 31;
        await vm.StartCrushCommand.ExecuteAsync(null);
        Assert.Empty(bag.Consumed);
    }

    [Fact]
    public async Task Exception_after_consumption_cannot_reuse_the_same_explicit_selection()
    {
        var (vm, bag) = Model();
        vm.CrushItems = (_, _, _, items, _) =>
        {
            var item = Assert.Single(items);
            bag.Consumed.Add(item.Qty);
            bag.Quantity -= item.Qty;
            throw new IOException("response lost");
        };
        await vm.StartCrushCommand.ExecuteAsync(null);
        await vm.StartCrushCommand.ExecuteAsync(null);
        Assert.Equal(new[] { 1 }, bag.Consumed);
        Assert.Equal(9, bag.Quantity);
    }

    [Fact]
    public async Task Character_identity_change_on_same_pid_cannot_dispatch()
    {
        var (vm, bag) = Model();
        vm.ApproachWorkshop = (_, _, _, _) =>
        {
            vm.CrushOwnerOf = _ => new SmithCrushOwner("test-crushing", 1, 3);
            return Task.FromResult(true);
        };
        await vm.StartCrushCommand.ExecuteAsync(null);
        Assert.Empty(bag.Consumed);
    }
}
