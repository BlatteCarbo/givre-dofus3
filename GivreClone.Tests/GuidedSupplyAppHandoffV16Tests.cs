extern alias F06App;

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Vm = F06App::GivreClone.App.ViewModels.ItemCraftPlannerViewModel;

namespace GivreClone.Tests;

/// <summary>
/// V16 tests the REAL classic VM commands and desktop wiring, with an
/// unselected owner to prove that no production/bank/buy sender is invoked.
/// Other V9/V13/V16 service tests exercise admitted production and its journal.
/// </summary>
public sealed class GuidedSupplyAppHandoffV16Tests
{
    [Fact]
    public async Task Real_V16_VM_runner_requires_one_shot_opt_in_and_consumes_it_even_when_denied()
    {
        var vm = new Vm();
        int portsConstructed = 0;
        vm.ProductionCharacter = () => null;
        vm.ProductionActions = _ =>
        {
            portsConstructed++;
            throw new InvalidOperationException("must never create a gameplay sender");
        };
        Assert.False(vm.GuidedSupplyV16RunnerOptIn);
        await vm.ExecuteGuidedSupplyV16RunnerCommand.ExecuteAsync(null);
        Assert.Contains("opt-in", vm.GuidedSupplyV16RunnerStatus,
            StringComparison.OrdinalIgnoreCase);
        vm.GuidedSupplyV16RunnerOptIn = true;
        await vm.ExecuteGuidedSupplyV16RunnerCommand.ExecuteAsync(null);
        Assert.False(vm.GuidedSupplyV16RunnerOptIn);
        Assert.Contains("non admise", vm.GuidedSupplyV16RunnerStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, portsConstructed);
    }

    [Fact]
    public async Task Double_click_guard_denies_before_touching_any_port_or_durable_ledger()
    {
        var vm = new Vm();
        vm.GuidedSupplyV16RunnerOptIn = true;
        var gate = typeof(Vm).GetField("_guidedV16RunnerHandoffInFlight",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(gate);
        gate!.SetValue(vm, 1);
        await vm.ExecuteGuidedSupplyV16RunnerCommand.ExecuteAsync(null);
        Assert.True(vm.GuidedSupplyV16RunnerOptIn); // another click cannot consume the owner's token
        Assert.Contains("Double-clic", vm.GuidedSupplyV16RunnerStatus,
            StringComparison.OrdinalIgnoreCase);
        gate.SetValue(vm, 0);
        await vm.ExecuteGuidedSupplyV16RunnerCommand.ExecuteAsync(null);
        Assert.False(vm.GuidedSupplyV16RunnerOptIn);
        Assert.Equal(0, (int)gate.GetValue(vm)!);
    }

    [Fact]
    public void Stale_Lua_recovery_callback_cannot_overwrite_a_detached_or_foreign_actor()
    {
        var vm = new Vm();
        var before = vm.GuidedSupplyV16RecoveryStatus;
        var goal = new F06App::GivreClone.App.Services.Production.GuidedActivityProductionGoal(
            292, 991001, 191105026, 7, 320, 1934, 3, 95, true);
        vm.ObserveGuidedSupplyV16Recovery(811, goal,
            "fake late successful result after actor switch");
        Assert.Equal(before, vm.GuidedSupplyV16RecoveryStatus);
    }

    [Fact]
    public void Classic_pane_exposes_real_V9_runner_and_host_recovery_under_explicit_opt_in()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null &&
            !Directory.Exists(Path.Combine(root.FullName, "GivreClone.App")))
            root = root.Parent;
        Assert.NotNull(root);
        string panel = File.ReadAllText(Path.Combine(root!.FullName,
            "GivreClone.App", "Views", "ItemCraftPlannerPanel.axaml"));
        string vm = File.ReadAllText(Path.Combine(root.FullName,
            "GivreClone.App", "ViewModels",
            "ItemCraftPlannerViewModel.GuidedActivity.cs"));
        string host = File.ReadAllText(Path.Combine(root.FullName,
            "GivreClone.App", "ViewModels", "BotsViewModel.ActivityRecovery.cs"));
        string bridge = File.ReadAllText(Path.Combine(root.FullName,
            "GivreClone.App", "Views", "MainWindow.axaml.cs"));
        Assert.Contains("ExecuteGuidedSupplyV16RunnerCommand", panel);
        Assert.Contains("ChooseGuidedV16ExactStepCommand", panel);
        Assert.Contains("ResumeGuidedV16RecordedChoiceCommand", panel);
        Assert.Contains("GuidedSupplyV16RunnerOptIn", panel);
        Assert.Contains("RunProductionCommand", panel);
        Assert.Contains("await RunProduction();", vm);
        Assert.Contains("MatchesExistingReadOnlyCheckpoint", vm);
        Assert.Contains("ReadGuidedSupplyContinuation()", vm);
        Assert.Contains("GuidedSupplyResumePlannerV16.InspectDurable", vm);
        Assert.Contains("GuidedSupplyResumePlannerV16.TryChooseOnce", vm);
        Assert.Contains("RefreshActivitySupplyContinuityStatus", host);
        Assert.Contains("InspectGuidedSupplyV16Async", bridge);
        Assert.Contains("CurrentGuidedProductionAuthority", bridge);
    }
}
