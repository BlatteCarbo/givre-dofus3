using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GivreClone.App.ViewModels;

/// <summary>Atelier éco (craft ou acheter, forgemagie, carnet) : un panneau de plus dans la colonne centrale, comme le marché.</summary>
public partial class MainViewModel
{
    [ObservableProperty] public partial bool EcoToolsVisible { get; set; }

    [RelayCommand]
    private void ToggleEcoTools()
    {
        if (EcoToolsVisible) EcoToolsVisible = false;
        else OpenEcoTools();
    }

    /// <summary>Replay de combat : fenêtre séparée (liste des combats, constats, chronologie des décisions de l'IA).</summary>
    [RelayCommand]
    public void OpenCombatReplay() => new Views.CombatReplayWindow().Show();

    /// <summary>Vraie carte du monde (images DofusDB) avec ressources et position des bots.</summary>
    [RelayCommand]
    public void OpenWorldMap()
    {
        Services.WorldMap.DofusDbWorldMap.CacheFolder = System.IO.Path.Combine(Services.MZP19LocalClone.DataRoot, "worldmap");
        var db = Services.Stores.Launch.GameData;
        if (db is null) return;
        var map = new Services.WorldMap.DofusDbWorldMap(db.AllMapPositions());
        new Views.WorldMapWindow(map, () => BotsVM.Bots.Where(b => b.Pid > 0 && Services.Stores.Launch.CurrentMap((uint)b.Pid) is > 0)
            .Select(b => new Views.WorldMapBot(string.IsNullOrWhiteSpace(b.Row.CharacterName) ? b.Row.Login : b.Row.CharacterName!, Services.Stores.Launch.CurrentMap((uint)b.Pid)!.Value, (int)b.Pid)),
            (pid, mapId) => { var r = BotsVM.OrderStartScript(pid, TravelScript(mapId)); return r.Ok ? "trajet lancé" : "refusé : " + r.Reason; }, db.ZaapMaps()).Show();
    }

    private static string TravelScript(long mapId) =>
        $"function move()\n  if not travelTo({mapId.ToString(System.Globalization.CultureInfo.InvariantCulture)}) then print(\"Trajet impossible\") end\n  return false\nend\n";

    [RelayCommand]
    private void CloseEcoTools() => EcoToolsVisible = false;

    [RelayCommand]
    public void OpenEcoTools()
    {
        VisualModsVisible = false;
        WorldAtlasVisible = false;
        ProfessionPlannerVisible = false;
        ItemCraftVisible = false;
        MarketInsightsVisible = false;
        AdvancedToolsVisible = false;
        EcoToolsVisible = true;
    }
}
