using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services.UiNavigation;

namespace GivreClone.App.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] public partial bool QuickNavVisible { get; set; }
    [ObservableProperty] public partial string QuickNavQuery { get; set; } = string.Empty;
    [ObservableProperty] public partial bool SidebarToolsVisible { get; set; }
    [ObservableProperty] public partial bool AdvancedToolsVisible { get; set; }
    public string SidebarToolsLabel => SidebarToolsVisible ? "Outils · réduire" : "Outils";
    partial void OnSidebarToolsVisibleChanged(bool value) => OnPropertyChanged(nameof(SidebarToolsLabel));

    [RelayCommand] private void ToggleSidebarTools() => SidebarToolsVisible = !SidebarToolsVisible;
    [RelayCommand] private void ToggleAdvancedTools()
    {
        QuickNavVisible = false;
        AdvancedToolsVisible = !AdvancedToolsVisible;
    }
    [RelayCommand] private void CloseAdvancedTools() => AdvancedToolsVisible = false;

    public IReadOnlyList<QuickNavigationAction> QuickNavResults { get; private set; } = QuickNavigationCatalog.All;
    public bool QuickNavNoResults => QuickNavResults.Count == 0;

    partial void OnQuickNavQueryChanged(string value)
    {
        QuickNavResults = QuickNavigationCatalog.Search(value);
        OnPropertyChanged(nameof(QuickNavResults));
        OnPropertyChanged(nameof(QuickNavNoResults));
    }

    public void OpenQuickNavigation()
    {
        AdvancedToolsVisible = false;
        ShortcutsHelpVisible = false;
        QuickNavQuery = string.Empty;
        QuickNavVisible = true;
    }

    [RelayCommand]
    private void CloseQuickNavigation() => QuickNavVisible = false;

    /// <summary>Route uniquement vers les pages existantes ou le carnet local.</summary>
    public bool ExecuteQuickNavigation(string? key)
    {
        if (!QuickNavigationCatalog.IsKnown(key)) return false;
        CloseQuickNavigation();
        AdvancedToolsVisible = false;
        if (key is "proxys" or "adapters" or "visualmods" or "worldatlas" or "professions" or "itemcraft")
            SidebarToolsVisible = true;
        WorldAtlasVisible = false;
        ProfessionPlannerVisible = false;
        ItemCraftVisible = false;
        MarketInsightsVisible = false;
        EcoToolsVisible = false;
        VisualModsVisible = key == "visualmods";
        if (QuickNavigationCatalog.BotTabIndex(key) is int tab)
        {
            BotsVM.RequestedTab = tab;   // la vue Bots applique l'onglet (elle peut être recréée par la navigation)
            ShowBots();
            return true;
        }
        switch (key)
        {
            case "professions": OpenProfessionPlanner(); break;
            case "itemcraft": OpenItemCraft(); break;
            case "ecotools": OpenEcoTools(); break;
            case "market": OpenMarketInsights(); break;
            case "worldatlas": OpenWorldAtlas(); break;
            case "dashboard": ShowDashboard(); break;
            case "bots": ShowBots(); break;
            case "accounts": ShowAccounts(); break;
            case "teams": ShowTeams(); break;
            case "proxys": ShowProxys(); break;
            case "adapters": ShowAdapters(); break;
            case "settings": ShowSettings(); break;
            case "myaccount": ShowMyAccount(); break;
        }
        return true;
    }

    // The compact "Outils" menu delegates navigation through the same catalog, so
    // every shortcut closes the menu and respects the existing page/overlay routing.
    [RelayCommand] private void OpenToolVisualMods() => ExecuteQuickNavigation("visualmods");
    [RelayCommand] private void OpenToolWorldAtlas() => ExecuteQuickNavigation("worldatlas");
    [RelayCommand] private void OpenToolProfessions() => ExecuteQuickNavigation("professions");
    [RelayCommand] private void OpenToolItemCraft() => ExecuteQuickNavigation("itemcraft");
    [RelayCommand] private void OpenToolProxys() => ExecuteQuickNavigation("proxys");
    [RelayCommand] private void OpenToolAdapters() => ExecuteQuickNavigation("adapters");
}
