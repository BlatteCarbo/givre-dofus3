using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GivreClone.App.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] public partial bool WorldAtlasVisible { get; set; }

    [RelayCommand]
    private void ToggleWorldAtlas()
    {
        if (!WorldAtlasVisible)
        {
            MarketInsightsVisible = false;
            EcoToolsVisible = false;
            VisualModsVisible = false;
            ProfessionPlannerVisible = false;
            ItemCraftVisible = false;
        }
        WorldAtlasVisible = !WorldAtlasVisible;
    }

    [RelayCommand]
    private void CloseWorldAtlas() => WorldAtlasVisible = false;

    public void OpenWorldAtlas()
    {
        MarketInsightsVisible = false;
        EcoToolsVisible = false;
        VisualModsVisible = false;
        ProfessionPlannerVisible = false;
        ItemCraftVisible = false;
        WorldAtlasVisible = true;
    }
}
