using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Eco;

namespace GivreClone.App.ViewModels;

public sealed record EcoAlertRow(long Gid, string Name, string Condition, string CurrentText)
{
    public Avalonia.Media.Imaging.Bitmap? Icon { get; } = EcoService.IconOf(Gid);
    public bool HasIcon => Icon is not null;
}

public sealed partial class EcoToolsViewModel
{
    public ObservableCollection<EcoAlertRow> AlertRows { get; } = new();
    public ObservableCollection<EcoItemHit> AlertResults { get; } = new();
    public string[] AlertDirections { get; } = { "passe sous", "passe au-dessus de" };
    [ObservableProperty] public partial string AlertSearch { get; set; } = "";
    [ObservableProperty] public partial EcoItemHit? SelectedAlertHit { get; set; }
    [ObservableProperty] public partial int AlertDirectionIndex { get; set; }
    [ObservableProperty] public partial decimal? AlertTarget { get; set; }
    [ObservableProperty] public partial string AlertStatus { get; set; } = "Choisis un objet, une condition et un prix cible. L'alerte part sur Discord (webhook du personnage) quand un bot observe ce prix à l'HDV.";

    partial void OnAlertSearchChanged(string value)
    {
        AlertResults.Clear();
        if (value.Trim().Length < 2 || Stores.Launch.GameData is not { } db) return;
        foreach (var (gid, name, level) in db.FindItems(value.Trim(), 20)) AlertResults.Add(new EcoItemHit(gid, name, level, false));
    }

    [RelayCommand]
    private void RefreshAlerts()
    {
        AlertRows.Clear();
        uint pid = CurrentPid;
        foreach (var a in PriceAlertStore.Locked(b => b.Alerts.ToList()))
        {
            var q = pid > 0 ? EcoService.PriceFor(pid, a.Gid) : null;
            string cur = q is null ? "prix inconnu" : $"actuel {q.Unit:N0} ({q.Source})";
            AlertRows.Add(new EcoAlertRow(a.Gid, a.Name, (a.Below ? "sous " : "au-dessus de ") + a.Target.ToString("N0") + " kamas", cur));
        }
    }

    [RelayCommand]
    private void AddAlert()
    {
        if (SelectedAlertHit is not { } hit) { AlertStatus = "Choisis d'abord un objet dans la liste."; return; }
        if (AlertTarget is not { } t || t <= 0) { AlertStatus = "Indique un prix cible (kamas)."; return; }
        PriceAlertStore.Locked(b =>
        {
            b.Alerts.RemoveAll(x => x.Gid == hit.Gid && x.Below == (AlertDirectionIndex == 0));
            b.Alerts.Add(new PriceAlert { Gid = hit.Gid, Name = hit.Name, Below = AlertDirectionIndex == 0, Target = t });
            return 0;
        });
        PriceAlertStore.Save();
        AlertStatus = $"Alerte enregistrée : {hit.Name} {(AlertDirectionIndex == 0 ? "sous" : "au-dessus de")} {t:N0}.";
        AlertSearch = ""; AlertTarget = null;
        RefreshAlerts();
    }

    [RelayCommand]
    private void RemoveAlert(EcoAlertRow? r)
    {
        if (r is null) return;
        PriceAlertStore.Locked(b => b.Alerts.RemoveAll(x => x.Gid == r.Gid));
        PriceAlertStore.Save();
        RefreshAlerts();
    }
}
