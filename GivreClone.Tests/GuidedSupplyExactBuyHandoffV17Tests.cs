extern alias F06App;

using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using HdvVm = F06App::GivreClone.App.ViewModels.HdvViewModel;
using HdvRow = F06App::GivreClone.App.ViewModels.HdvBuyRow;
using ItemPick = F06App::GivreClone.App.ViewModels.HdvItemPick;
using Choice = F06App::GivreClone.App.Services.Production.GuidedSupplyExactChoiceV16;
using Step = F06App::GivreClone.App.Services.Production.GuidedSupplyResumeStepV16;
using AtelierVm = F06App::GivreClone.App.ViewModels.ItemCraftPlannerViewModel;

namespace GivreClone.Tests;

/// <summary>Actual HDV VM buy-list admission, not a duplicate buy executor.
/// The caller-supplied source delegate models the MainWindow durable read.
/// No test opens the game, creates an HDV order or asserts a server receipt.</summary>
[Collection("Transport journal and timing")]
public sealed class GuidedSupplyExactBuyHandoffV17Tests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly uint Pid = 9100391;
    private const int Server = 292;
    private const long Character = 99100039;

    private static void Put(object target, string field, object value) =>
        target.GetType().GetField(field, Hidden)!.SetValue(target, value);

    private static (Launch Launch, HdvVm Vm) Model()
    {
        var launch = new Launch();
        ((ConcurrentDictionary<uint, (int Id, string Name)>)typeof(Launch)
            .GetField("_serverInfo", Hidden)!.GetValue(launch)!)[Pid] =
            (Server, "v17-offline-buyer");
        ((ConcurrentDictionary<uint, long>)typeof(Launch)
            .GetField("_charId", Hidden)!.GetValue(launch)!)[Pid] = Character;
        var vm = (HdvVm)RuntimeHelpers.GetUninitializedObject(typeof(HdvVm));
        Put(vm, "_launch", launch);
        Put(vm, "_pid", Pid);
        Put(vm, "_loading", true);
        Put(vm, "<BuyRows>k__BackingField", new ObservableCollection<HdvRow>());
        Put(vm, "<BuySuggestions>k__BackingField", new ObservableCollection<ItemPick>());
        Put(vm, "<RelistSuggestions>k__BackingField", new ObservableCollection<ItemPick>());
        return (launch, vm);
    }

    private static Choice Buy(long gid = 1519, long quantity = 17) =>
        new(Step.InspectBuy, gid, quantity,
            new string('a', 64), new string('b', 64), new string('c', 64), 9)
        { ObservedUsedPodsV18 = 12, ObservedCapacityPodsV18 = 100 };

    private static bool Stage(HdvVm vm, Choice choice, Func<bool> source,
        out string explanation) =>
        vm.TryStageGuidedSupplyBuy(Pid, Server, Character,
            choice, source, out explanation);

    [Fact]
    public void Real_buy_row_stages_net_deficit_without_second_inventory_deduction_or_send()
    {
        var (launch, vm) = Model();
        launch.Inventory.Seed(Pid, [new(10, 1519, 70)]);
        vm.BuyDeductStock = true; // default UI behavior must not subtract stock again
        Assert.True(Stage(vm, Buy(), () => true, out string explanation), explanation);
        var row = Assert.Single(vm.BuyRows);
        Assert.Equal(1519, row.Gid);
        Assert.Equal(17, row.Needed);
        Assert.Equal(0, row.Owned);
        Assert.Equal(17, row.ToBuy);
        Assert.True(vm.ShowBuy);
        typeof(HdvVm).GetMethod("RefreshBuyLive", Hidden)!.Invoke(vm, null);
        Assert.Equal(0, row.Owned);
        Assert.Equal(17, row.ToBuy);
        Assert.Contains("non persistée", explanation);
        Assert.False(vm.BuyRunning);
        Assert.Equal(0, vm.BuyBudget);
    }

    [Fact]
    public void Double_click_is_idempotent_but_changed_quantity_or_manual_rule_is_refused()
    {
        var (_, vm) = Model();
        Assert.True(Stage(vm, Buy(), () => true, out _));
        Assert.True(Stage(vm, Buy(), () => true, out _));
        Assert.Single(vm.BuyRows);
        Assert.False(Stage(vm, Buy(quantity: 18), () => true, out _));
        Assert.Single(vm.BuyRows);
        vm.BuyRows.Clear();
        vm.BuyRows.Add(new HdvRow { Gid = 1519, Needed = 1 });
        Assert.False(Stage(vm, Buy(), () => true, out _));
        Assert.Equal(1, Assert.Single(vm.BuyRows).Needed);
    }

    [Fact]
    public void Replaced_source_and_modified_rule_revoke_original_intent()
    {
        var (_, vm) = Model();
        bool current = true;
        Assert.True(Stage(vm, Buy(), () => current, out _));
        var row = Assert.Single(vm.BuyRows);
        current = false;
        Assert.False((bool)typeof(HdvVm).GetMethod(
            "GuidedSupplyBuySourceCurrent", Hidden)!.Invoke(vm, null)!);
        Assert.False(Stage(vm, Buy(), () => current, out _));
        current = true;
        row.Needed = 99;
        Assert.False(Stage(vm, Buy(), () => current, out _));
    }

    [Theory]
    [InlineData(0L, 1L)]
    [InlineData(1519L, 0L)]
    [InlineData(1519L, 2147483648L)]
    [InlineData(2147483648L, 1L)]
    public void Invalid_gid_or_quantity_never_adds_a_buy_row(long gid, long quantity)
    {
        var (_, vm) = Model();
        Assert.False(Stage(vm, Buy(gid, quantity), () => true, out _));
        Assert.Empty(vm.BuyRows);
    }

    [Fact]
    public void Legacy_choice_without_pods_evidence_cannot_enter_real_buyer_even_with_positive_source_callback()
    {
        var (_, vm) = Model();
        var old = new Choice(Step.InspectBuy, 1519, 17,
            new string('a', 64), new string('b', 64), new string('c', 64), 9);
        Assert.True(old.Valid); // readable historic durable entry
        Assert.False(old.HasExactPodsEvidenceV18);
        Assert.False(Stage(vm, old, () => true, out _));
        Assert.Empty(vm.BuyRows);
    }

    [Fact]
    public void Running_or_ambiguous_buyer_refuses_staging_and_atelier_requires_durable_goal()
    {
        var (_, vm) = Model();
        vm.BuyRunning = true;
        Assert.False(Stage(vm, Buy(), () => true, out _));
        vm.BuyRunning = false;
        Assert.False(Stage(vm, Buy(), () => false, out _));
        Assert.Empty(vm.BuyRows);

        var atelier = new AtelierVm();
        int emitted = 0;
        atelier.GuidedSupplyHdvBuyStageRequested += _ => emitted++;
        var goal = new F06App::GivreClone.App.Services.Production.GuidedActivityProductionGoal(
            Server, Character, 191105026, 7, 320, 1934, 3, 95, true);
        Assert.False(atelier.IsCurrentGuidedV17BuyStage(Pid, goal, Buy()));
        Assert.Equal(0, emitted);
    }
}
