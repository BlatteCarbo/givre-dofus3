using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GivreClone.App.ViewModels;

/// <summary>Navigation locale pour la planification de métiers; aucune action en jeu.</summary>
public partial class MainViewModel
{
    [ObservableProperty] public partial bool ProfessionPlannerVisible { get; set; }

    [RelayCommand]
    private void ToggleProfessionPlanner()
    {
        if (ProfessionPlannerVisible) { ProfessionPlannerVisible = false; return; }
        OpenProfessionPlanner();
    }

    [RelayCommand]
    private void CloseProfessionPlanner() => ProfessionPlannerVisible = false;

    public void OpenProfessionPlanner()
    {
        MarketInsightsVisible = false;
        EcoToolsVisible = false;
        VisualModsVisible = false;
        WorldAtlasVisible = false;
        ItemCraftVisible = false;
        ProfessionPlannerVisible = true;
    }
}
