using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GivreClone.App.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] public partial bool VisualModsVisible { get; set; }

    [RelayCommand]
    private void ToggleVisualMods()
    {
        if (!VisualModsVisible)
        {
            MarketInsightsVisible = false;
            EcoToolsVisible = false;
            WorldAtlasVisible = false;
            ProfessionPlannerVisible = false;
            ItemCraftVisible = false;
        }
        VisualModsVisible = !VisualModsVisible;
    }

    [RelayCommand]
    private void CloseVisualMods() => VisualModsVisible = false;
}
