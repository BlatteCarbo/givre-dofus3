using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Resources;
using System.Globalization;
using Avalonia.Threading;
using GivreClone.App.Services.QuantityFarm;

namespace GivreClone.App.ViewModels;

public sealed record EcoResourceHit(ResourceInfo Info)
{
    public string Caption => $"{Info.Name}  ·  {ResourceMap.JobName(Info.Job)} niv. {Info.Level}";
    public Avalonia.Media.Imaging.Bitmap? Icon { get; } = EcoService.IconOf(Info.ItemId);
    public bool HasIcon => Icon is not null;
}

public sealed record EcoResourceMapRow(long MapId, string Coords, string Count, string Distance, string Where);

// « Où récolter » : 86 ressources de récolte et leurs cartes (données DofusDB embarquées, hors ligne), classées depuis la position du bot sélectionné.
public sealed partial class EcoToolsViewModel
{
    private static readonly int?[] ResourceJobIds = { null, 2, 24, 26, 28, 36, 1 };
    public string[] ResourceJobs { get; } = { "Tous les métiers", "Bûcheron", "Mineur", "Alchimiste", "Paysan", "Pêcheur", "Eau" };
    public ObservableCollection<EcoResourceHit> ResourceResults { get; } = new();
    public ObservableCollection<EcoResourceMapRow> ResourceMapRows { get; } = new();
    [ObservableProperty] public partial string ResourceSearch { get; set; } = "";
    [ObservableProperty] public partial int ResourceJobIndex { get; set; }
    [ObservableProperty] public partial EcoResourceHit? SelectedResource { get; set; }
    [ObservableProperty] public partial bool ResourceIndoor { get; set; } = true;
    [ObservableProperty] public partial string ResourceNote { get; set; } = "Choisis une ressource : les cartes les plus proches de ton bot s'affichent en premier.";
    [ObservableProperty] public partial decimal? ResourceQuantity { get; set; } = 10;
    [ObservableProperty] public partial string ResourceQuantityStatus { get; set; } = "";
    [ObservableProperty] public partial string ResourceQuantityProgress { get; set; } = "";
    [ObservableProperty] public partial bool ResourceQuantityBagOnlyOffer { get; set; }
    private bool _quantityBoardHooked;


    partial void OnResourceSearchChanged(string value) => RefreshResources();
    partial void OnResourceJobIndexChanged(int value) => RefreshResources();
    partial void OnResourceIndoorChanged(bool value) => ShowResourceMaps();
    partial void OnSelectedResourceChanged(EcoResourceHit? value)
    {
        EnsureQuantityHook();
        ResourceQuantityBagOnlyOffer = false;
        if (value is null) ResourceQuantityProgress = "";
        ShowResourceMaps();
    }


    private void RefreshResources()
    {
        var keep = SelectedResource?.Info.ItemId;
        ResourceResults.Clear();
        foreach (var r in ResourceMap.Default.Find(ResourceSearch, ResourceJobIds[Math.Clamp(ResourceJobIndex, 0, ResourceJobIds.Length - 1)])) ResourceResults.Add(new EcoResourceHit(r));
        SelectedResource = keep is { } k ? ResourceResults.FirstOrDefault(h => h.Info.ItemId == k) : null;
    }

    private void ShowResourceMaps()
    {
        ResourceMapRows.Clear();
        if (SelectedResource is not { } hit) return;
        var origin = EcoService.OriginOf(CurrentPid);
        var hits = ResourceMap.Default.Best(hit.Info.ItemId, origin?.X, origin?.Y, origin?.World, ResourceIndoor, 25);
        foreach (var h in hits)
            ResourceMapRows.Add(new EcoResourceMapRow(h.Node.MapId, $"[{h.Node.X}, {h.Node.Y}]", $"{h.Node.Count} point(s)", origin is null ? "—" : h.Distance == 0 ? "ici" : $"{h.Distance} carte(s)", h.Node.Indoor ? "intérieur" : "extérieur"));
        ResourceNote = hits.Count == 0 ? "Aucune carte recensée pour cette ressource (dans ce monde)."
            : origin is null ? "Aucun bot en ligne : les cartes qui en ont le plus d'abord. Ce sont des points de récolte recensés, pas la disponibilité en direct."
            : "Classé depuis la carte de ton bot. Ce sont des points de récolte recensés, pas la disponibilité en direct.";
    }

    /// <summary>Crée (ou remplace) le script « Récolte &lt;ressource&gt; » : circuit de 6 cartes riches et proches du bot sélectionné.</summary>
    [RelayCommand]
    private void MakeGatherScript()
    {
        if (SelectedResource is not { } hit) { ResourceNote = "Choisis d'abord une ressource."; return; }
        var origin = EcoService.OriginOf(CurrentPid);
        var route = GatherScript.Route(ResourceMap.Default, hit.Info.ItemId, origin?.X, origin?.Y, origin?.World, 6, ResourceIndoor);
        if (route.Count == 0) { ResourceNote = "Aucune carte recensée pour cette ressource : pas de script."; return; }
        string code = GatherScript.Build(hit.Info, route);
        string name = "Récolte " + hit.Info.Name;
        try
        {
            new GivreClone.App.Services.Mcp.SavedLuaScripts(System.IO.Path.Combine(MZP19LocalClone.DataRoot, "scripts")).Save(name, code, true);
            ResourceNote = $"Script « {name} » enregistré ({route.Count} cartes). Ouvre-le dans l'éditeur Lua ou lance-le sur un bot ; vérifie le circuit avant de le laisser tourner seul.";
        }
        catch (Exception e) when (e is System.IO.IOException or ArgumentException or UnauthorizedAccessException) { ResourceNote = "Enregistrement impossible : " + e.Message; }
    }

    /// <summary>Crée (ou remplace) le script « Chasse au trésor » : démarre une chasse au coffre, résout les indices avec DofusDB, pose les drapeaux, creuse et recommence.</summary>
    [RelayCommand]
    private void MakeTreasureScript()
    {
        try
        {
            new GivreClone.App.Services.Mcp.SavedLuaScripts(System.IO.Path.Combine(MZP19LocalClone.DataRoot, "scripts")).Save("Chasse au trésor", GivreClone.App.Services.Treasure.TreasureScript.Build(), true);
            ResourceNote = "Script « Chasse au trésor » enregistré. Lance-le sur un bot placé sur la carte d'un coffre de chasse ; surveille les premières chasses (les phorreurs avancent carte par carte).";
        }
        catch (Exception e) when (e is System.IO.IOException or ArgumentException or UnauthorizedAccessException) { ResourceNote = "Enregistrement impossible : " + e.Message; }
    }

    [RelayCommand] private void CopyResourceCoords(EcoResourceMapRow? r) { if (r is not null) { CopyRequested?.Invoke(r.Coords.Trim('[', ']').Replace(" ", "")); ResourceNote = "Coordonnées copiées : " + r.Coords; } }

    [RelayCommand] private void StartResourceQuantity() => RunResourceQuantity(false);
    [RelayCommand] private void AcceptResourceQuantityBagOnly() => RunResourceQuantity(true);

    [RelayCommand]
    private void StopResourceQuantity()
    {
        EnsureQuantityHook();
        var bots = MainViewModel.Instance?.BotsVM;
        if (bots is null) { ResourceQuantityStatus = "Sélectionne un bot connecté."; ResourceQuantityBagOnlyOffer = false; return; }
        bool running = bots.StopQuantityFarm();
        ResourceQuantityBagOnlyOffer = false;
        ResourceQuantityStatus = running ? "Script arrêté." : "Aucun script en cours.";
    }

    private void RunResourceQuantity(bool acceptBagOnly)
    {
        EnsureQuantityHook();
        if (SelectedResource is not { } hit)
        {
            ResourceQuantityStatus = "Choisis d'abord une ressource.";
            ResourceQuantityBagOnlyOffer = false;
            return;
        }
        var pick = QuantityFarmPlanner.ResolveResource(ResourceMap.Default, hit.Info.ItemId.ToString(CultureInfo.InvariantCulture));
        if (!pick.Accepted)
        {
            ResourceQuantityStatus = pick.Message;
            ResourceQuantityBagOnlyOffer = false;
            return;
        }
        if (!QuantityFarmPlanner.TryQuantity(ResourceQuantity, out long quantity))
        {
            ResourceQuantityStatus = "Quantité entre 1 et 1 000 000 000.";
            ResourceQuantityBagOnlyOffer = false;
            return;
        }
        var origin = EcoService.OriginOf(CurrentPid);
        var route = GatherScript.Route(ResourceMap.Default, hit.Info.ItemId, origin?.X, origin?.Y, origin?.World, 6, ResourceIndoor);
        var bots = MainViewModel.Instance?.BotsVM;
        if (bots is null)
        {
            ResourceQuantityStatus = "Sélectionne un bot connecté.";
            ResourceQuantityBagOnlyOffer = false;
            return;
        }
        var decision = bots.StartResourceQuantity(hit.Info.ItemId, hit.Info.Name, quantity, acceptBagOnly, route.Select(node => node.MapId).ToArray());
        ResourceQuantityStatus = decision.Message;
        ResourceQuantityBagOnlyOffer = decision.Status == QuantityFarmStatus.NeedsBagOnly;
        if (decision.Status is QuantityFarmStatus.Completed or QuantityFarmStatus.NeedsBagOnly or QuantityFarmStatus.Ready)
            ResourceQuantityProgress = QuantityFarmBoard.Format(new QuantityFarmProgress(decision.ItemGid, decision.Owned, decision.Target, decision.Remaining));
    }

    private void EnsureQuantityHook()
    {
        if (_quantityBoardHooked) return;
        _quantityBoardHooked = true;
        QuantityFarmBoard.Changed += OnQuantityBoard;
        OnQuantityBoard((int)CurrentPid);
    }

    private void OnQuantityBoard(int pid)
    {
        if (pid != (int)CurrentPid) return;
        void apply() => ResourceQuantityProgress = QuantityFarmBoard.TryGet(pid, out var progress) ? QuantityFarmBoard.Format(progress) : "";
        if (Dispatcher.UIThread.CheckAccess()) apply();
        else Dispatcher.UIThread.Post(apply);
    }
}
