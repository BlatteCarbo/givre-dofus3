extern alias F06App;

using System;
using System.IO;
using Xunit;
using Vm = F06App::GivreClone.App.ViewModels.ItemCraftPlannerViewModel;

namespace GivreClone.Tests;

/// <summary>
/// The actual classic operator command, not a second supply sender. Positive
/// GID/stock journal admission is covered by the durable supply journey tests.
/// </summary>
public sealed class GuidedSupplyOperatorReturnV18Tests
{
    [Fact]
    public void No_current_goal_never_constructs_a_gameplay_port_or_persists_an_intent()
    {
        var vm = new Vm();
        int senderFactories = 0, buyRequests = 0, routes = 0;
        vm.ProductionCharacter = () => null;
        vm.ProductionActions = _ =>
        {
            senderFactories++;
            throw new InvalidOperationException("no sender should be constructed");
        };
        vm.GuidedSupplyHdvBuyStageRequested += _ => buyRequests++;
        vm.GuidedSupplyRoutePreparationRequested += _ => routes++;

        vm.ReinspectGuidedSupplyAfterExternalActionCommand.Execute(null);

        Assert.Empty(vm.GuidedV16ExactChoices);
        Assert.Null(vm.SelectedGuidedV16ExactChoice);
        Assert.Contains("refusée", vm.GuidedSupplyContinuationStatus);
        Assert.Equal(0, senderFactories);
        Assert.Equal(0, buyRequests);
        Assert.Equal(0, routes);
    }

    [Fact]
    public void Classic_operator_button_reuses_real_preflight_and_durable_choice_commands()
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
        Assert.Contains("ReinspectGuidedSupplyAfterExternalActionCommand", panel);
        Assert.Contains("InspectGuidedStockPreflight(); // actual existing", vm);
        Assert.Contains("InspectGuidedV16ExactChoices(); // durable current revision", vm);
        Assert.Contains("InspectGuidedHdvListing();", vm);
        Assert.Contains("GuidedSupplyNextStepV15.InspectActivityRecovery", vm);
        Assert.Contains("GuidedSupplyNextStepV15.InspectProductionAttempt", vm);
    }
}
