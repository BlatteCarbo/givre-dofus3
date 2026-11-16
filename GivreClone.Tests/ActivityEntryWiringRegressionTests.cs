using System;
using System.IO;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Source-level regression guards complement the executed journal/port tests:
/// these check that the compiled application entry still calls the protected
/// path. They are not substitutes for server receipts or live game validation.
/// </summary>
public sealed class ActivityEntryWiringRegressionTests
{
    private static string Root => MappingScan.WalkUp(d =>
        File.Exists(Path.Combine(d.FullName, "GivreClone.App", "Views", "MainWindow.axaml.cs")))?.FullName
        ?? throw new InvalidOperationException("Project root missing");

    private static string Source(string file) => File.ReadAllText(Path.Combine(Root,
        "GivreClone.App", file.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void Actual_desktop_item_craft_button_has_selected_character_and_server_checked_action_adapter()
    {
        string shell = Source("Views/MainWindow.axaml.cs");
        string vm = Source("ViewModels/ItemCraftPlannerViewModel.cs");
        Assert.Contains("ItemCraftPlannerHost.ViewModel.ProductionCharacter =", shell);
        Assert.Contains("ItemCraftPlannerHost.ViewModel.ProductionActions =", shell);
        Assert.Contains("ItemCraftPlannerHost.ViewModel.ProductionRecipe =", shell);
        Assert.Contains("_productionAttempts.TryBegin(scope, pid, request", vm);
        Assert.Contains("new ItemCraftSessionBoundActions(actions, CurrentSession)", vm);
        Assert.Contains("_production.RunAsync(request, actions, progress", vm);
        Assert.Contains("_productionAttempts.TryFinish(lease, fullyConfirmed)", vm);
        Assert.Contains("!Stores.Launch.IsInFight(pid)", vm);
    }

    [Fact]
    public void All_known_non_Lua_listing_callers_enter_common_tax_journal()
    {
        Assert.Contains("HdvScriptSellByGidAsync", Source("ViewModels/HdvViewModel.Trade.cs"));
        Assert.Contains("HdvScriptSellByGidAsync", Source("ViewModels/ItemCraftPlannerViewModel.cs"));
        Assert.Contains("HdvScriptSellByGidAsync", Source("Services/LotMGame.cs"));
        string lua = Source("Services/GivreLuaHost.Bindings.cs");
        Assert.Contains("HdvScriptSellByGidAsync", lua);
        Assert.Contains("HdvScriptRepriceAsync", lua);
        Assert.Contains("HdvScriptRepriceAllAsync", lua);
    }

    [Fact]
    public void Tactical_advice_opt_in_survives_profile_clone_and_serialization_but_never_activates_by_default()
    {
        var old = new CombatProfile();
        Assert.False(old.TacticalAdvisorDiagnostic);
        old.TacticalAdvisorDiagnostic = true;
        Assert.True(old.Clone().TacticalAdvisorDiagnostic);
        string json = System.Text.Json.JsonSerializer.Serialize(old);
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<CombatProfile>(json)!.TacticalAdvisorDiagnostic);
        Assert.Contains("TacticalAdvisorDiagnostic", Source("ViewModels/BotsViewModel.CombatTacticalAdvisor.cs"));
        Assert.Contains("RestoreCombatTacticalAdvisorForLoadedProfile", Source("ViewModels/BotsViewModel.Combat.cs"));
        Assert.Contains("BtnCombatTacticalAdvisor", Source("Views/BotsView.axaml"));
    }
}
