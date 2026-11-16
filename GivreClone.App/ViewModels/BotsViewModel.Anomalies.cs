using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

/// <summary>Une anomalie ouverte (sous-zone à bonus de récompense) affichée dans l'onglet Expédition.</summary>
public sealed record AnomalyRow(int SubareaId, int AreaId, string Name, string Area, int Level, int Bonus, double Remaining)
{
    public string Caption => Level > 0 ? $"{Name}  ·  niv. {Level}" : Name;
    public string BonusText => Bonus > 0 ? $"+{Bonus} %" : "";
    public bool HasBonus => Bonus > 0;
    public string RemainingText => Remaining <= 0 ? "" : Remaining >= 3600
        ? $"{(int)(Remaining / 3600)} h {(int)(Remaining % 3600 / 60):00}"
        : $"{(int)(Remaining / 60)} min";
}

// ===================== Onglet Expédition : anomalies ouvertes =====================
// Liste des anomalies du serveur du bot sélectionné (bonus, niveau, temps restant). Un clic prépare l'expédition sur la sous-zone :
// monstres cliquables, limites, banque… puis « Lancer ». Le script s'arrête tout seul quand l'anomalie se ferme (bonus perdu).
public partial class BotsViewModel
{
    public ObservableCollection<AnomalyRow> ExpAnomalies { get; } = new();
    public bool ExpHasAnomalies => ExpAnomalies.Count > 0;
    /// <summary>Sous-zone d'anomalie choisie (0 = expédition classique) : ajoute l'arrêt à la fermeture de l'anomalie.</summary>
    [ObservableProperty] public partial int ExpAnomalySubarea { get; set; }
    public bool ExpIsAnomaly => ExpAnomalySubarea > 0;
    partial void OnExpAnomalySubareaChanged(int value) { OnPropertyChanged(nameof(ExpIsAnomaly)); UpdateExpSummary(); }

    private string _anomalySig = "";

    // ---- Élixir uchronique (fragments d'anomalie) : gardé actif pendant l'expédition, racheté à l'HDV consommables ----
    /// <summary>0 aucun, 1 mineur, 2 amélioré, 3 majeur (index de la liste déroulante).</summary>
    [ObservableProperty] public partial int ExpElixirRank { get; set; } = AppConfig.Current.ExpElixirRank;
    [ObservableProperty] public partial decimal? ExpElixirMaxPrice { get; set; } = AppConfig.Current.ExpElixirMaxPrice;
    [ObservableProperty] public partial decimal? ExpElixirStock { get; set; } = AppConfig.Current.ExpElixirStock;
    /// <summary>Altérations actives du perso (élixir en cours et combats restants), lues des paquets.</summary>
    [ObservableProperty] public partial string ExpElixirState { get; set; } = "";
    public bool ExpUsesElixir => ExpElixirRank > 0;
    public string[] ExpElixirChoices => new[]
    {
        Loc.T("L.Exp.ElixirNone"),
        ElixirLabel(1), ElixirLabel(2), ElixirLabel(3),
    };
    private static string ElixirLabel(int rank)
        => UchronicElixirs.ByRank(rank) is { } t ? Loc.Fmt("L.Exp.ElixirChoice", Stores.Launch.ItemDisplayName(t.Gid), t.Level, t.Fights) : "";

    partial void OnExpElixirRankChanged(int value) { OnPropertyChanged(nameof(ExpUsesElixir)); SaveElixir(); UpdateExpSummary(); }
    partial void OnExpElixirMaxPriceChanged(decimal? value) => SaveElixir();
    partial void OnExpElixirStockChanged(decimal? value) => SaveElixir();
    private void SaveElixir()
    {
        var c = AppConfig.Current;
        c.ExpElixirRank = Math.Clamp(ExpElixirRank, 0, 3);
        c.ExpElixirMaxPrice = (long)Math.Max(0, ExpElixirMaxPrice ?? 0);
        c.ExpElixirStock = (int)Math.Clamp(ExpElixirStock ?? 2, 1, 50);
        AppConfig.Save();
    }

    /// <summary>Plan de l'élixir choisi pour l'expédition : HDV consommables le plus proche de la zone (prix max requis pour acheter).</summary>
    private ElixirPlan? ElixirPlanFor(long nearMap)
        => ElixirPlanFor(nearMap, ExpElixirRank, (long)Math.Max(0, ExpElixirMaxPrice ?? 0), (int)Math.Clamp(ExpElixirStock ?? 2, 1, 50));

    private static ElixirPlan? ElixirPlanFor(long nearMap, int rank, long maxPrice, int stock)
    {
        if (UchronicElixirs.ByRank(rank) is not { } t) return null;
        var l = Stores.Launch;
        var here = l.MapCoordOf(nearMap);
        long hdv = HdvLocations.All.Where(h => h.Type == "consumables")
            .OrderBy(h => here is (int x, int y) && l.MapCoordOf(h.MapId) is (int hx, int hy) ? Math.Abs(hx - x) + Math.Abs(hy - y) : int.MaxValue)
            .Select(h => h.MapId).FirstOrDefault();
        return new ElixirPlan(t.Gid, t.AlterationId, hdv, maxPrice, stock);
    }

    /// <summary>« Élixir uchronique majeur actif · 4 combat(s) » ; vide sans altération connue.</summary>
    private void RefreshElixirState()
    {
        if (SelectedBot is not { Pid: > 0 } b) { ExpElixirState = ""; return; }   // tick lent, onglet visible
        var l = Stores.Launch;
        var parts = l.Alterations((uint)b.Pid).Select(a => UchronicElixirs.ByAlteration(a.Id) is { } t
            ? UchronicElixirs.IsFightCount(t, a.ExpirationValue)   // combats restants ; une date ou un type inconnu : juste « actif »
                ? Loc.Fmt("L.Exp.ElixirActive", l.ItemDisplayName(t.Gid), a.ExpirationValue)
                : Loc.Fmt("L.Exp.ElixirActiveShort", l.ItemDisplayName(t.Gid))
            : null).Where(x => x is not null).ToList();
        string state = parts.Count > 0 ? string.Join(" · ", parts)
                     : l.HasAlteration((uint)b.Pid, 50) == false ? Loc.T("L.Exp.ElixirNoneActive") : "";
        int frags = l.Inventory.CountByGid((uint)b.Pid, UchronicElixirs.FragmentGid);
        if (frags > 0) state = (state.Length > 0 ? state + "\n" : "") + Loc.Fmt("L.Exp.Fragments", frags);
        ExpElixirState = state;
    }

    /// <summary>Relit les anomalies ouvertes du bot sélectionné (onglet visible, tick lent) ; ne touche la liste que si elle change.</summary>
    private void RefreshAnomalies()
    {
        RefreshElixirState();
        RefreshZoneValue();
        var l = Stores.Launch;
        var rows = SelectedBot is { Pid: > 0 } b
            ? l.OpenAnomalies((uint)b.Pid).Select(a =>
              {
                  GivreClone.App.Services.LotM.SubAreaInfo? sub = null;
                  try { sub = l.GameData?.SubArea(a.State.SubareaId); } catch { sub = null; }
                  return new AnomalyRow(a.State.SubareaId, sub?.AreaId ?? 0, sub?.Name is { Length: > 0 } n ? n : $"#{a.State.SubareaId}",
                                        sub?.Area ?? "", sub?.Level ?? 0, a.State.RewardRate, a.Remaining);
              })
              .OrderByDescending(r => r.Bonus).ThenByDescending(r => r.Remaining).ToList()
            : new();
        // Signature à la minute : le temps restant affiché suit sans reconstruire la liste à chaque tick.
        string sig = string.Join(";", rows.Select(r => $"{r.SubareaId}:{r.Bonus}:{(int)(r.Remaining / 60)}"));
        if (sig == _anomalySig) return;
        _anomalySig = sig;
        ExpAnomalies.Clear();
        foreach (var r in rows) ExpAnomalies.Add(r);
        OnPropertyChanged(nameof(ExpHasAnomalies));
        // Anomalie choisie refermée : l'expédition redevient classique (le script en cours s'arrête de lui-même).
        if (ExpAnomalySubarea > 0 && rows.All(r => r.SubareaId != ExpAnomalySubarea)) ExpAnomalySubarea = 0;
    }

    /// <summary>Clic sur une anomalie : mode zone, zone + sous-zone sélectionnées (monstres et cartes chargés), arrêt à la fermeture.</summary>
    [RelayCommand]
    private void ExpPickAnomaly(AnomalyRow? row)
    {
        if (row is null) return;
        ExpeditionOpen();
        ExpDungeonMode = false;
        if (ExpAreas.FirstOrDefault(a => a.Id == row.AreaId) is not { } area) { ExpStatus = Loc.Fmt("L.Exp.AnomalyUnknownZone", row.Name); return; }
        ExpArea = area;
        if (ExpSubAreas.FirstOrDefault(s => s.Id == row.SubareaId) is not { } sub) { ExpStatus = Loc.Fmt("L.Exp.AnomalyNoMonsters", row.Name); return; }
        ExpSubArea = sub;
        ExpAnomalySubarea = row.SubareaId;
        ExpStatus = Loc.Fmt("L.Exp.AnomalyPicked", row.Name, row.RemainingText);
    }
}
