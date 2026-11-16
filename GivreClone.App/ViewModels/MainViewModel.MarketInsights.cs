using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GivreClone.App.ViewModels;

/// <summary>Read-only market screen; active bot HDV remains the source of truth.</summary>
public partial class MainViewModel
{
    [ObservableProperty] public partial bool MarketInsightsVisible { get; set; }

    [RelayCommand]
    private void ToggleMarketInsights()
    {
        if (MarketInsightsVisible) MarketInsightsVisible = false;
        else OpenMarketInsights();
    }

    [RelayCommand]
    private void CloseMarketInsights() => MarketInsightsVisible = false;

    [RelayCommand]
    private void OpenMarketInsights()
    {
        VisualModsVisible = false;
        WorldAtlasVisible = false;
        ProfessionPlannerVisible = false;
        ItemCraftVisible = false;
        EcoToolsVisible = false;
        MarketInsightsVisible = true;
    }
}
