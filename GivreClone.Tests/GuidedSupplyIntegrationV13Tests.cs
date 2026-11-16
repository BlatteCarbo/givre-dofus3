extern alias F06App;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;
using Xunit;
using Actions = F06App::GivreClone.App.Services.ItemCraftPlanner.IItemCraftProductionActions;
using Receipt = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftWorkshopReceipt;
using BoundActions = F06App::GivreClone.App.Services.Production.ItemCraftSessionBoundActions;
using Inventory = F06App::GivreClone.App.Services.InventoryStore;
using ViewModel = F06App::GivreClone.App.ViewModels.ItemCraftPlannerViewModel;

namespace GivreClone.Tests;

/// <summary>
/// The actual VM -> session-bound production port and inventory store fences.
/// All backend receipts in these fixtures are synthetic: no game is launched.
/// </summary>
public sealed class GuidedSupplyIntegrationV13Tests
{
    private sealed class MutableFixture : Actions
    {
        public int Sent;
        public TaskCompletionSource<bool>? WaitingBank;
        public int BagCount(long gid) => gid == 10 ? 3 : 0;
        public int ChestCount(long gid) => gid == 10 ? 2 : 0;
        public IReadOnlyList<MapInteractive> Interactives() => Array.Empty<MapInteractive>();
        public async Task<bool> OpenChestAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref Sent);
            return WaitingBank is { } waiting ? await waiting.Task : true;
        }
        public Task<int> WithdrawAsync(long gid, int quantity, CancellationToken ct)
        {
            Interlocked.Increment(ref Sent);
            return Task.FromResult(quantity);
        }
        public Task<bool> CloseChestAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref Sent);
            return Task.FromResult(true);
        }
        public Task<Receipt> CraftAsync(int elementId, int skillInstanceUid,
            IReadOnlyList<(long Gid, int Qty)> ingredients, int count, CancellationToken ct)
        {
            Interlocked.Increment(ref Sent);
            return Task.FromResult(new Receipt(true, count, 1934));
        }
        public Task<long> LotPriceAsync(long gid, int quantity, CancellationToken ct)
        {
            Interlocked.Increment(ref Sent);
            return Task.FromResult(250L);
        }
        public Task<bool> BuyAsync(long gid, int quantity, long maxPrice, CancellationToken ct)
        {
            Interlocked.Increment(ref Sent);
            return Task.FromResult(true);
        }
        public Task<bool> ListAsync(long gid, int quantity, long price, CancellationToken ct)
        {
            Interlocked.Increment(ref Sent);
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task Ready_preview_can_read_stock_but_cannot_mutate_before_actual_started_lease()
    {
        var backend = new MutableFixture();
        bool started = false;
        var actions = new BoundActions(backend, () => true, () => started);
        Assert.Equal(3, actions.BagCount(10));
        Assert.Equal(2, actions.ChestCount(10));
        Assert.False(await actions.OpenChestAsync(CancellationToken.None));
        Assert.Equal(0, backend.Sent);
        Assert.True(actions.IsRevoked);
        started = true; // cannot re-arm a previously denied source/lease
        Assert.Equal(0, await actions.WithdrawAsync(10, 1, CancellationToken.None));
        Assert.Equal(0, backend.Sent);
    }

    [Fact]
    public async Task Mutations_after_started_are_checked_before_each_send_and_after_late_result()
    {
        var backend = new MutableFixture
        {
            WaitingBank = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        bool started = true;
        var actions = new BoundActions(backend, () => true, () => started);
        Task<bool> send = actions.OpenChestAsync(CancellationToken.None);
        Assert.Equal(1, backend.Sent);
        started = false; // V9 lease revoked during an actual outstanding await
        backend.WaitingBank.SetResult(true); // positive local fixture result is discarded
        Assert.False(await send);
        Assert.True(actions.IsRevoked);
        started = true;
        Assert.False(await actions.ListAsync(1934, 1, 250, CancellationToken.None));
        Assert.Equal(1, backend.Sent);
    }

    [Fact]
    public async Task Every_mutable_port_checks_started_lease_not_just_bank_open()
    {
        var backend = new MutableFixture();
        Func<bool> current = () => true;
        var denied = new Func<BoundActions>(() => new(backend, current, () => false));
        Assert.Equal(0, await denied().WithdrawAsync(10, 2, CancellationToken.None));
        Assert.False(await denied().CloseChestAsync(CancellationToken.None));
        Assert.False((await denied().CraftAsync(1, 2, [(10, 2)], 1,
            CancellationToken.None)).Confirmed);
        Assert.Equal(0, await denied().LotPriceAsync(10, 1, CancellationToken.None));
        Assert.False(await denied().BuyAsync(10, 1, 250, CancellationToken.None));
        Assert.False(await denied().ListAsync(1934, 1, 250, CancellationToken.None));
        Assert.Equal(0, backend.Sent);
    }

    [Fact]
    public async Task Throwing_source_or_mutation_lease_is_sticky_and_never_dispatches()
    {
        var backend = new MutableFixture();
        var sourceThrows = new BoundActions(backend,
            () => throw new IOException("durable source missing"), () => true);
        Assert.Equal(0, sourceThrows.BagCount(10));
        Assert.True(sourceThrows.IsRevoked);
        Assert.False(await sourceThrows.OpenChestAsync(CancellationToken.None));
        var leaseThrows = new BoundActions(backend, () => true,
            () => throw new IOException("attempt journal inaccessible"));
        Assert.False(await leaseThrows.BuyAsync(10, 1, 250, CancellationToken.None));
        Assert.True(leaseThrows.IsRevoked);
        Assert.Equal(0, backend.Sent);
    }

    [Fact]
    public void Actual_inventory_seed_and_mutations_advance_source_scoped_observation_revision()
    {
        const uint pid = 58013;
        var store = new Inventory();
        Assert.Equal(0, store.ObservationRevision(pid));
        store.Seed(pid, [new Inventory.Item(1, 10, 3)]);
        long first = store.ObservationRevision(pid);
        Assert.True(first > 0);
        store.Upsert(pid, new Inventory.Item(2, 11, 2));
        Assert.True(store.ObservationRevision(pid) > first);
        long second = store.ObservationRevision(pid);
        store.SetQuantity(pid, 1, 4);
        Assert.True(store.ObservationRevision(pid) > second);
        long third = store.ObservationRevision(pid);
        store.SetPosition(pid, 2, 7);
        Assert.True(store.ObservationRevision(pid) > third);
        long fourth = store.ObservationRevision(pid);
        Assert.True(store.TryUpdateQuantity(pid, 1, 4, 2));
        Assert.True(store.ObservationRevision(pid) > fourth);
        long fifth = store.ObservationRevision(pid);
        store.Remove(pid, 2);
        Assert.True(store.ObservationRevision(pid) > fifth);
        store.Clear(pid);
        Assert.Equal(0, store.ObservationRevision(pid));
        store.Seed(pid, [new Inventory.Item(3, 10, 9)]);
        Assert.True(store.ObservationRevision(pid) > fifth);
        Assert.Equal(9, store.CountByGid(pid, 10));
    }

    [Fact]
    public void Session_bound_ledger_preview_and_classic_buttons_use_real_V13_admission()
    {
        Assert.NotNull(typeof(ViewModel).GetProperty("PrepareGuidedSupplyRouteCommand"));
        Assert.NotNull(typeof(ViewModel).GetProperty("GuidedSupplyStages"));
        Assert.NotNull(typeof(ViewModel).GetProperty("GuidedSupplyActionStatus"));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName,
                   "GivreClone.App"))) root = root.Parent;
        Assert.NotNull(root);
        string app = Path.Combine(root!.FullName, "GivreClone.App");
        string vm = File.ReadAllText(Path.Combine(app, "ViewModels",
            "ItemCraftPlannerViewModel.GuidedActivity.cs"));
        string runner = File.ReadAllText(Path.Combine(app, "ViewModels",
            "ItemCraftPlannerViewModel.cs"));
        string panel = File.ReadAllText(Path.Combine(app, "Views",
            "ItemCraftPlannerPanel.axaml"));
        string main = File.ReadAllText(Path.Combine(app, "Views",
            "MainWindow.axaml.cs"));
        string lua = File.ReadAllText(Path.Combine(app, "Services",
            "GivreLuaHost.cs"));
        string supply = File.ReadAllText(Path.Combine(app, "Services",
            "LaunchService.GuidedSupplyV13.cs"));
        Assert.Contains("GuidedSupplyPlannerV13.PrepareAndCheckpoint(goal, request,", vm);
        Assert.Contains("GuidedSupplyCheckpointStillCurrent(guidedGoal, request)", runner);
        Assert.Contains("GuidedSupplyRoutePreparationRequested +=", main);
        Assert.Contains("PrepareStrictActivityRouteCommand.Execute(null)", main);
        Assert.Contains("new ItemCraftSessionBoundActions(original,", main);
        Assert.Contains("GuidedWorkflowMayContinue(goal)", main);
        Assert.Contains("ReadGuidedSupplySource(pid)", main);
        Assert.Contains("ObservationRevision(pid)", supply);
        Assert.Contains("PrepareGuidedSupplyRouteCommand", panel);
        Assert.Contains("GuidedSupplyStages", panel);
        Assert.True(lua.IndexOf("var beforeImperative = ReadDurableRecoveryStatus()",
            StringComparison.Ordinal) < lua.IndexOf("DispatchFightEnd(script, ct)",
                StringComparison.Ordinal));
    }
}
