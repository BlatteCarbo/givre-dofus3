using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Eco;

namespace GivreClone.App.ViewModels;

public sealed record EcoSetHit(int Id, string Name, int Pieces) { public string Caption => $"{Name}  ·  {Pieces} pièce(s)"; }

public sealed record EcoSetRow(long Gid, string Name, string CraftText, string BuyText, string VerdictText, string ChosenText, bool Good)
{
    public Avalonia.Media.Imaging.Bitmap? Icon { get; } = EcoService.IconOf(Gid);
    public bool HasIcon => Icon is not null;
}

public sealed partial class EcoToolsViewModel
{
    public ObservableCollection<EcoSetHit> SetResults { get; } = new();
    public ObservableCollection<EcoSetRow> SetRows { get; } = new();
    [ObservableProperty] public partial string SetSearch { get; set; } = "";
    [ObservableProperty] public partial EcoSetHit? SelectedSet { get; set; }
    [ObservableProperty] public partial string SetTotalText { get; set; } = "—";
    [ObservableProperty] public partial string SetBuyAllText { get; set; } = "—";
    [ObservableProperty] public partial string SetCraftAllText { get; set; } = "—";
    [ObservableProperty] public partial string SetNote { get; set; } = "Cherche une panoplie (2 lettres minimum).";
    [ObservableProperty] public partial string SetShopping { get; set; } = "";

    partial void OnSetSearchChanged(string value)
    {
        SetResults.Clear();
        if (value.Trim().Length < 2 || Stores.Launch.GameData is not { } db) return;
        foreach (var (id, name, pieces) in db.FindSets(value.Trim(), 30)) SetResults.Add(new EcoSetHit(id, name, pieces));
    }

    partial void OnSelectedSetChanged(EcoSetHit? value) => AnalyzeSet();

    private void AnalyzeSet()
    {
        SetRows.Clear(); SetTotalText = SetBuyAllText = SetCraftAllText = "—"; SetShopping = "";
        if (SelectedSet is not { } set || Stores.Launch.GameData is not { } db) return;
        uint pid = CurrentPid;
        var pieces = db.SetItems(set.Id);
        var empty = new CraftAdvice(new CraftNode(0, "", 1, null, null, CraftDecision.Unknown, null, false, "", Array.Empty<CraftNode>()), null, null, null, null, null, false, Array.Empty<long>(), "");
        var res = SetAdvisor.Analyze(pieces, g => EcoService.Advice(pid, g, 1) ?? empty, NameOf);
        foreach (var r in res.Items)
            SetRows.Add(new EcoSetRow(r.Gid, r.Name, r.CraftCost is { } c ? c.ToString("N0") : "—", r.BuyCost is { } b ? b.ToString("N0") : "—",
                r.Verdict switch { SetVerdict.Craft => "Fabriquer", SetVerdict.Buy => "Acheter", _ => "Prix ?" }, r.Chosen is { } x ? x.ToString("N0") : "—", r.Verdict == SetVerdict.Craft));
        SetTotalText = res.TotalChosen is { } t ? t.ToString("N0") + " k" : "—";
        SetBuyAllText = res.TotalBuy is { } tb ? tb.ToString("N0") + " k" : "—";
        SetCraftAllText = res.TotalCraft is { } tc ? tc.ToString("N0") + " k" : "—";
        SetNote = res.Unknown > 0 ? $"{res.Unknown} pièce(s) sans prix exploitable : saisis les prix manquants dans « Craft ou acheter » (ou ouvre l'HDV avec un bot)." : "Coût de la panoplie en prenant, pièce par pièce, le moins cher entre fabriquer et acheter.";
        SetShopping = string.Join("\n", res.Shopping.Select(x => $"{x.Quantity:N0} × {x.Name}" + (x.Cost is { } cc ? $"  ({cc:N0} k)" : "  (prix ?)")));
    }

    [RelayCommand] private void CopySetShopping() { if (SetShopping.Length > 0) { CopyRequested?.Invoke(SetShopping); SetNote = "Liste de courses copiée."; } }
    [RelayCommand] private void OpenSetPiece(EcoSetRow? r) { if (r is not null) { CraftSearch = r.Name; SelectedHit = CraftResults.FirstOrDefault(h => h.Gid == r.Gid) ?? SelectedHit; } }
}
