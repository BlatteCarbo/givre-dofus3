using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Eco;

namespace GivreClone.App.ViewModels;

public sealed record EcoBagItem(int Uid, long Gid, string Name, int Qty)
{
    public string Caption => Qty > 1 ? $"{Name} ×{Qty}" : Name;
}
public sealed record EcoStatRow(string Text, string ValueText, string RateText, string WeightText, bool Negative);
public sealed record EcoRuneRow(string Name, string Tier, string AmountText, string WeightText, string PriceText);
public sealed record EcoFmSessionRow(FmSession Session)
{
    public string Title => (string.IsNullOrWhiteSpace(Session.Item) ? "Séance" : Session.Item) + "  ·  " + Session.StartedUtc.ToLocalTime().ToString("dd/MM HH:mm") +
                           (Session.Archived ? "  ·  archivée" : Session.Paused ? "  ·  en pause" : "");
}

public sealed partial class EcoToolsViewModel
{
    private FmLedgerStore _fm = new();
    private readonly Dictionary<string, long> _runeGid = new(StringComparer.OrdinalIgnoreCase);

    // ---- poids d'un objet du sac ----
    public ObservableCollection<EcoBagItem> FmItems { get; } = new();
    public ObservableCollection<EcoStatRow> FmStats { get; } = new();
    public ObservableCollection<EcoRuneRow> FmRunes { get; } = new();
    [ObservableProperty] public partial EcoBagItem? SelectedFmItem { get; set; }
    [ObservableProperty] public partial string FmTotalText { get; set; } = "—";
    [ObservableProperty] public partial string FmMaxWeightText { get; set; } = "";
    [ObservableProperty] public partial string FmPercentText { get; set; } = "";
    [ObservableProperty] public partial string FmHint { get; set; } = "Choisis un objet du sac de ton bot pour calculer son poids de forgemagie.";

    private IReadOnlyDictionary<int, decimal> Rates => Stores.Launch.GameData?.EffectPowerRates() ?? new Dictionary<int, decimal>();

    partial void OnSelectedFmItemChanged(EcoBagItem? value) { ComputeWeights(); RebuildFmTargets(); }
    partial void OnFmMaxWeightTextChanged(string value) => ComputeWeights();

    [RelayCommand]
    private void RefreshFmItems()
    {
        FmItems.Clear();
        uint pid = CurrentPid;
        if (pid == 0) { FmHint = "Sélectionne un bot connecté (onglet Bots) pour lire son sac."; return; }
        var l = Stores.Launch;
        foreach (var it in l.Inventory.Items(pid).Where(i => i.Qty > 0 && l.ItemEffects(pid, i.Uid).Count > 0).OrderBy(i => NameOf(i.Gid), StringComparer.OrdinalIgnoreCase).Take(300))
            FmItems.Add(new EcoBagItem(it.Uid, it.Gid, NameOf(it.Gid), it.Qty));
        FmHint = FmItems.Count == 0 ? "Aucun objet avec des caractéristiques dans le sac (ouvre-le ou reconnecte-toi pour les recevoir)." : $"{FmItems.Count} objet(s) avec caractéristiques.";
    }

    private void ComputeWeights()
    {
        FmStats.Clear(); FmPercentText = ""; FmTotalText = "—";
        if (SelectedFmItem is not { } item || CurrentPid == 0) return;
        var l = Stores.Launch; var rates = Rates;
        var stats = new List<FmStat>();
        foreach (var e in l.ItemEffects(CurrentPid, item.Uid))
        {
            int value = e.Value != 0 ? e.Value : e.Max;
            if (value == 0 || l.DescribeEffect(e) is not { } d || d.Hidden) continue;
            decimal rate = rates.TryGetValue(e.ActionId, out var r) ? r : 0;
            stats.Add(new FmStat(e.ActionId, d.Text, value, rate));
        }
        foreach (var s in stats)
            FmStats.Add(new EcoStatRow(s.Name, s.Value.ToString("N0"), s.RatePerPoint == 0 ? "—" : s.RatePerPoint.ToString("0.###", CultureInfo.InvariantCulture),
                s.RatePerPoint == 0 ? "—" : s.Weight.ToString("0.##", CultureInfo.InvariantCulture), s.Value < 0));
        decimal total = Forgemagie.TotalWeight(stats.Where(s => s.RatePerPoint != 0));
        FmTotalText = total.ToString("0.##", CultureInfo.InvariantCulture) + " poids";
        if (decimal.TryParse(FmMaxWeightText.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var max) && Forgemagie.PercentOfMax(total, max) is { } pct)
            FmPercentText = $"{pct:0.#} % du poids maximum ({max:0.##})";
        FmHint = Services.Eco.Forgemagie.WeightProvenance(rates.Count, stats.Count(s => s.RatePerPoint == 0));
    }

    // ---- runes ----
    private long RuneGid(string name)
    {
        if (_runeGid.TryGetValue(name, out var g)) return g;
        long found = 0;
        if (Stores.Launch.GameData is { } db)
            foreach (var (gid, n, _) in db.FindItems(name, 8))
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) { found = gid; break; }
        return _runeGid[name] = found;
    }

    private decimal? RunePrice(string name) => RuneGid(name) is > 0 and var g ? PriceFor(g)?.Unit : null;

    private void RefreshRunes()
    {
        FmRunes.Clear();
        var rates = Rates;
        foreach (var r in Forgemagie.Runes)
        {
            decimal rate = rates.TryGetValue(r.EffectId, out var x) ? x : 0;
            FmRunes.Add(new EcoRuneRow(r.Name, r.Tier, "+" + r.Amount.ToString("N0") + " " + r.Stat, rate == 0 ? "—" : Forgemagie.RuneWeight(r, rate).ToString("0.##", CultureInfo.InvariantCulture),
                RunePrice(r.Name) is { } p ? p.ToString("N0") + " k" : "—"));
        }
        MixStats.Clear();
        foreach (var s in Forgemagie.Runes.Select(r => r.Stat).Distinct()) MixStats.Add(s);
        if (SelectedMixStat is null && MixStats.Count > 0) SelectedMixStat = MixStats[0];
        FmRuneNames.Clear();
        foreach (var r in Forgemagie.Runes) FmRuneNames.Add(r.Name);
    }

    // ---- prix manuel d'une rune (l'HDV ne les affiche que si le bot a ouvert l'onglet Runes) ----
    [ObservableProperty] public partial string? RunePriceRune { get; set; }
    [ObservableProperty] public partial decimal? RunePriceValue { get; set; }

    [RelayCommand]
    private void SaveRunePrice()
    {
        FmWarning = "";
        if (RunePriceRune is not { Length: > 0 } name || RunePriceValue is not { } price || price < 0) { FmWarning = "Choisis une rune et un prix unitaire (0 = effacer)."; return; }
        long gid = RuneGid(name);
        if (gid <= 0) { FmWarning = "Rune introuvable dans les données du jeu."; return; }
        EcoService.Book.Set(CurrentServer, gid, price);
        try { EcoService.SavePrices(); } catch (Exception e) { FmWarning = "Prix gardé en mémoire seulement : " + e.Message; }
        RefreshRunes(); ComputeMix();
    }

    // ---- calculateur de mélange ----
    public ObservableCollection<string> MixStats { get; } = new();
    public ObservableCollection<string> FmRuneNames { get; } = new();
    [ObservableProperty] public partial string? SelectedMixStat { get; set; }
    [ObservableProperty] public partial decimal? MixPoints { get; set; } = 40;
    [ObservableProperty] public partial string MixText { get; set; } = "";

    partial void OnSelectedMixStatChanged(string? value) => ComputeMix();
    partial void OnMixPointsChanged(decimal? value) => ComputeMix();

    private void ComputeMix()
    {
        MixText = "";
        if (SelectedMixStat is not { } stat || MixPoints is not { } pts || pts < 1) return;
        var rune = Forgemagie.Runes.First(r => r.Stat == stat);
        var d = Forgemagie.Decompose(rune.EffectId, (int)Math.Min(pts, 100_000));
        if (d is null) return;
        var (mix, rest) = d.Value;
        var parts = new List<string>();
        if (mix.Ra > 0) parts.Add($"{mix.Ra} Ra"); if (mix.Pa > 0) parts.Add($"{mix.Pa} Pa"); if (mix.Ba > 0) parts.Add($"{mix.Ba} Ba");
        string text = string.Join(" + ", parts) + $"  ({mix.Count} rune(s))";
        if (Rates.TryGetValue(rune.EffectId, out var rate)) text += $"  ·  poids +{(pts - rest) * rate:0.##}";
        var cost = Forgemagie.MixCost(rune.EffectId, mix, RunePrice);
        text += cost is { } c ? $"  ·  coût ≈ {c:N0} kamas" : "  ·  coût : prix de rune inconnu";
        if (rest > 0) text += $"  ·  {rest} point(s) non couverts (la rune de base en donne {Forgemagie.Runes.First(r => r.EffectId == rune.EffectId && r.Tier == "Ba").Amount})";
        MixText = text;
    }

    // ---- séances ----
    public ObservableCollection<EcoFmSessionRow> FmSessions { get; } = new();
    [ObservableProperty] public partial EcoFmSessionRow? SelectedFmSession { get; set; }
    [ObservableProperty] public partial string NewSessionItem { get; set; } = "";
    [ObservableProperty] public partial string? PurchaseRune { get; set; }
    [ObservableProperty] public partial decimal? PurchaseQty { get; set; } = 1;
    [ObservableProperty] public partial decimal? PurchasePrice { get; set; }
    [ObservableProperty] public partial string? UseRune { get; set; }
    [ObservableProperty] public partial decimal? UseCount { get; set; } = 1;
    [ObservableProperty] public partial string SessionSummary { get; set; } = "Démarre une séance pour suivre tes achats de runes.";
    [ObservableProperty] public partial string SessionLines { get; set; } = "";
    [ObservableProperty] public partial string FmWarning { get; set; } = "";
    public bool HasSession => SelectedFmSession is not null;

    private void ReloadFmSessions()
    {
        var keep = SelectedFmSession?.Session.Id;
        FmSessions.Clear();
        foreach (var s in _fm.Sessions.OrderBy(s => s.Archived).ThenByDescending(s => s.StartedUtc)) FmSessions.Add(new EcoFmSessionRow(s));
        SelectedFmSession = FmSessions.FirstOrDefault(r => r.Session.Id == keep) ?? FmSessions.FirstOrDefault();
    }

    partial void OnSelectedFmSessionChanged(EcoFmSessionRow? value) { OnPropertyChanged(nameof(HasSession)); RenderSession(); }

    private void SaveFm() { try { EcoJson.Save(_fmPath, _fm); } catch (Exception e) { FmWarning = "Séance gardée en mémoire seulement : " + e.Message; } }

    private void RenderSession()
    {
        if (SelectedFmSession is not { } row) { SessionSummary = "Démarre une séance pour suivre tes achats de runes."; SessionLines = ""; return; }
        var sum = FmLedger.Summarize(row.Session);
        SessionSummary = $"Dépensé : {sum.TotalSpent:N0} kamas  ·  {sum.RunesBought:N0} rune(s) achetée(s)  ·  {sum.RunesUsed:N0} utilisée(s)" +
                         (sum.CostPerUse is { } c ? $"  ·  ≈ {c:N0} kamas par rune utilisée" : "");
        SessionLines = string.Join("\n", sum.Runes.Select(r => $"{r.Rune} : {r.Bought:N0} achetée(s) (moy. {r.AveragePrice:N0}), {r.Used:N0} utilisée(s), stock {r.Stock:N0}"));
    }

    [RelayCommand]
    private void StartSession()
    {
        var s = new FmSession { Item = NewSessionItem.Trim() };
        _fm.Sessions.Add(s); SaveFm(); NewSessionItem = "";
        ReloadFmSessions();
        SelectedFmSession = FmSessions.FirstOrDefault(r => r.Session.Id == s.Id);
    }

    [RelayCommand]
    private void AddPurchase()
    {
        FmWarning = "";
        if (SelectedFmSession is not { } row || PurchaseRune is not { Length: > 0 } rune || PurchaseQty is not { } q || q < 1 || PurchasePrice is not { } p || p < 0) { FmWarning = "Rune, quantité et prix unitaire requis."; return; }
        var median = RunePrice(rune);
        if (FmLedger.IsOutlier(p, median)) FmWarning = $"Prix éloigné de plus de 25 % du prix connu ({median:N0}) : vérifie la saisie.";
        row.Session.Purchases.Add(new FmPurchase { Rune = rune, Quantity = (long)q, UnitPrice = p });
        SaveFm(); RenderSession();
    }

    [RelayCommand]
    private void AddUse()
    {
        FmWarning = "";
        if (SelectedFmSession is not { } row || UseRune is not { Length: > 0 } rune || UseCount is not { } n || n < 1) { FmWarning = "Rune et nombre utilisé requis."; return; }
        row.Session.Uses.Add(new FmUse { Rune = rune, Count = (int)n });
        SaveFm(); RenderSession();
    }

    [RelayCommand] private void TogglePause() { if (SelectedFmSession is { } r) { r.Session.Paused = !r.Session.Paused; SaveFm(); ReloadFmSessions(); } }
    [RelayCommand] private void ArchiveSession() { if (SelectedFmSession is { } r) { r.Session.Archived = !r.Session.Archived; SaveFm(); ReloadFmSessions(); } }
    [RelayCommand] private void DeleteSession() { if (SelectedFmSession is { } r) { _fm.Sessions.Remove(r.Session); SaveFm(); ReloadFmSessions(); } }
}
