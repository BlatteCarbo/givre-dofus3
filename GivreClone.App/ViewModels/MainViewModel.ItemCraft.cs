using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GivreClone.App.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] public partial bool ItemCraftVisible { get; set; }

    [RelayCommand]
    private void ToggleItemCraft()
    {
        if (ItemCraftVisible) ItemCraftVisible = false;
        else OpenItemCraft();
    }

    [RelayCommand]
    private void CloseItemCraft() => ItemCraftVisible = false;

    public void OpenItemCraft()
    {
        MarketInsightsVisible = false;
        EcoToolsVisible = false;
        VisualModsVisible = false;
        WorldAtlasVisible = false;
        ProfessionPlannerVisible = false;
        ItemCraftVisible = true;
    }
}
