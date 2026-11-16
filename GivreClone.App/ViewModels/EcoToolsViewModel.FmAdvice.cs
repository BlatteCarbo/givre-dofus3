using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Eco;
using GivreClone.App.Services.Forgemagie;

namespace GivreClone.App.ViewModels;

/// <summary>Une ligne de l'atelier : jet actuel lu sur l'objet, objectif saisi par l'utilisateur (le jet maximal du modèle n'est pas connu hors ligne).</summary>
public sealed partial class FmTargetRow : ObservableObject
{
    public FmTargetRow(string stat, string label, int current, Action changed)
    {
        Stat = stat; Label = label; Current = current; _changed = changed; Target = current;
    }
    private readonly Action _changed;
    public string Stat { get; }
    public string Label { get; }
    public int Current { get; }
    [ObservableProperty] public partial decimal? Target { get; set; }
    [ObservableProperty] public partial decimal? Max { get; set; }
    partial void OnTargetChanged(decimal? value) => _changed();
    partial void OnMaxChanged(decimal? value) => _changed();
}

/// <summary>Ligne du brisage : une caractéristique, son poids et la valeur nette si on la vise en Focus (« — » = prix de rune inconnu).</summary>
public sealed record EcoBrisageRow(string StatText, string ValueText, string WeightText, string FocusText, bool Best);

public sealed partial class EcoToolsViewModel
{
    // ---- brisage : Focus ou sans Focus (consultatif, prix déjà observés ou saisis) ----
    public ObservableCollection<EcoBrisageRow> BrisageRows { get; } = new();
    [ObservableProperty] public partial decimal? BrisageCost { get; set; }
    [ObservableProperty] public partial decimal? BrisageCoeff { get; set; } = 100;
    [ObservableProperty] public partial string BrisageHint { get; set; } = "Choisis un objet du sac : l'atelier compare le brisage avec et sans Focus.";
    [ObservableProperty] public partial string BrisageVerdict { get; set; } = "";
    [ObservableProperty] public partial string BrisageBreakEven { get; set; } = "";
    [ObservableProperty] public partial string BrisageClassText { get; set; } = "";
    [ObservableProperty] public partial string BrisageProfitText { get; set; } = "";
    [ObservableProperty] public partial bool BrisageComplete { get; set; }
    public bool HasBrisage => BrisageRows.Count > 0 || BrisageVerdict.Length > 0;

    private int _brisageLevel;
    private IReadOnlyList<BrisageStatLine> _brisageLines = Array.Empty<BrisageStatLine>();
    private string _brisageItem = "";

    partial void OnBrisageCostChanged(decimal? value) => ShowBrisage();
    partial void OnBrisageCoeffChanged(decimal? value) => ShowBrisage();

    /// <summary>Relit l'objet choisi : jets observés, poids du jeu et prix de la rune de base (HDV observé ou prix saisi). Le coût est prérempli
    /// avec le prix HDV observé de l'objet (achat du lot), modifiable ; vide si inconnu.</summary>
    [RelayCommand]
    private void RecomputeBrisage() => LoadBrisage(resetCost: false);

    private void LoadBrisage(bool resetCost)
    {
        _brisageLines = Array.Empty<BrisageStatLine>(); _brisageLevel = 0; _brisageItem = "";
        decimal? cost = BrisageCost;
        if (SelectedFmItem is { } item && CurrentPid != 0)
        {
            var l = Stores.Launch;
            _brisageItem = item.Name;
            _brisageLevel = l.ItemLevel((int)Math.Clamp(item.Gid, 0, int.MaxValue));
            _brisageLines = BrisageFocusAdvisor.BuildLines(l.ItemEffects(CurrentPid, item.Uid).Select(e => (e.ActionId, e.Value != 0 ? e.Value : e.Max)), Rates, RunePrice);
            if (resetCost) cost = PriceFor(item.Gid) is { } q && q.Unit > 0 ? q.Unit : null;
        }
        else if (resetCost) cost = null;
        BrisageCost = cost;   // déclenche l'affichage si la valeur change
        ShowBrisage();
    }

    /// <summary>Affiche l'évaluation d'un objet de niveau `level` : point d'entrée unique de l'UI (objet réel ou lignes d'essai).</summary>
    public void ShowBrisage(int level, IReadOnlyList<BrisageStatLine> lines, string itemName)
    {
        _brisageLevel = level; _brisageLines = lines; _brisageItem = itemName;
        ShowBrisage();
    }

    private void ShowBrisage()
    {
        BrisageRows.Clear();
        BrisageVerdict = BrisageBreakEven = BrisageClassText = BrisageProfitText = "";
        BrisageComplete = false;
        try
        {
            if (_brisageItem.Length == 0) { BrisageHint = "Choisis un objet du sac : l'atelier compare le brisage avec et sans Focus."; return; }
            if (_brisageLevel is < 1 or > 200) { BrisageHint = $"{_brisageItem} : niveau de l'objet inconnu ou hors 1–200, brisage non évalué."; return; }
            double coeff = (double)Math.Clamp(BrisageCoeff ?? 100m, 0m, 10000m);
            var ev = BrisageFocusAdvisor.Evaluate(_brisageLevel, _brisageLines, Math.Max(0m, BrisageCost ?? 0m), coeff);
            bool costKnown = BrisageCost is > 0;
            var s = BrisageFocusAdvisor.Summarize(ev, costKnown);
            foreach (var line in _brisageLines.Where(x => x.Value > 0 && x.UnitWeight > 0))
            {
                var c = ev.Candidates.FirstOrDefault(x => x.StatKey == line.StatKey);
                BrisageRows.Add(new EcoBrisageRow(line.StatKey, line.Value.ToString("N0"), c is null ? "—" : c.TotalWeight.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                    c?.NetKama is { } k ? k.ToString("N0") + " k" : "—", ev.Choice == BrisageChoice.Focus && ev.FocusStatKey == line.StatKey));
            }
            BrisageVerdict = s.Verdict; BrisageBreakEven = s.BreakEven; BrisageClassText = s.ClassLabel; BrisageProfitText = s.Profit; BrisageComplete = s.Complete;
            BrisageHint = $"{_brisageItem} · niv. {_brisageLevel} · coeff. {coeff:0.#} % · sans focus {(ev.NoFocusNetKama is { } n ? n.ToString("N0") + " k" : "—")} net (taxe 2 %). Modèle communautaire, η = 0,5 : indicatif.";
        }
        finally { OnPropertyChanged(nameof(HasBrisage)); }
    }

    public ObservableCollection<FmTargetRow> FmTargets { get; } = new();
    [ObservableProperty] public partial bool FmAllowOver { get; set; }
    [ObservableProperty] public partial string FmAdviceText { get; set; } = "Choisis un objet, puis fixe un objectif sur une ou plusieurs lignes.";
    [ObservableProperty] public partial string FmStockText { get; set; } = "";
    public bool HasFmTargets => FmTargets.Count > 0;
    [ObservableProperty] public partial string FmUnmodeledText { get; set; } = "";

    partial void OnFmAllowOverChanged(bool value) => AdviseRune();

    /// <summary>Gains par rune tirés de la table du jeu déjà utilisée pour les poids (Ba, Pa = ×3, Ra = ×10).</summary>
    private static readonly IReadOnlyDictionary<string, (int Ba, int Pa, int Ra)> Gains =
        Forgemagie.Runes.GroupBy(r => r.Stat).ToDictionary(g => g.Key,
            g => (g.First(r => r.Tier == "Ba").Amount, g.First(r => r.Tier == "Pa").Amount, g.First(r => r.Tier == "Ra").Amount), StringComparer.OrdinalIgnoreCase);

    private void RebuildFmTargets()
    {
        FmTargets.Clear();
        var unmodeled = new List<string>();
        if (SelectedFmItem is { } item && CurrentPid != 0)
        {
            var l = Stores.Launch;
            foreach (var e in l.ItemEffects(CurrentPid, item.Uid))
            {
                var rune = Forgemagie.Runes.FirstOrDefault(r => r.EffectId == e.ActionId);
                if (rune is null) { unmodeled.Add(l.DescribeEffect(e) is { } u ? u.Text : "effet " + e.ActionId); continue; }
                if (FmTargets.Any(t => t.Stat == rune.Stat)) continue;
                int value = e.Value != 0 ? e.Value : e.Max;
                string label = l.DescribeEffect(e) is { } d ? d.Text : rune.Stat;
                FmTargets.Add(new FmTargetRow(rune.Stat, label, value, AdviseRune));
            }
        }
        FmUnmodeledText = FmSupportLines.Unmodeled(unmodeled);
        OnPropertyChanged(nameof(HasFmTargets));
        LoadBrisage(resetCost: true);
        AdviseRune();
    }

    private Dictionary<(string Stat, string Tier), int> BagRunes()
    {
        var stock = new Dictionary<(string, string), int>();
        if (CurrentPid == 0) return stock;
        var byName = Forgemagie.Runes.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var it in Stores.Launch.Inventory.Items(CurrentPid))
            if (it.Qty > 0 && byName.TryGetValue(NameOf(it.Gid), out var r))
                stock[(r.Stat, r.Tier)] = stock.GetValueOrDefault((r.Stat, r.Tier)) + it.Qty;
        return stock;
    }

    [RelayCommand]
    private void AdviseRune()
    {
        if (FmTargets.Count == 0)
        {
            FmAdviceText = SelectedFmItem is null ? "Choisis un objet, puis fixe un objectif sur une ou plusieurs lignes."
                : "Aucune ligne de cet objet n'a de rune connue (Force, Intelligence, Chance, Agilité, Sagesse, Vitalité, Pods, Initiative).";
            FmStockText = "";
            return;
        }
        var stock = BagRunes();
        FmStockText = stock.Count == 0 ? "Aucune rune connue dans le sac du bot."
            : "Runes du sac : " + string.Join(", ", stock.OrderBy(k => k.Key.Stat).ThenBy(k => k.Key.Tier).Select(k => $"{(k.Key.Tier == "Ba" ? "" : k.Key.Tier + " ")}{k.Key.Stat} ×{k.Value}"));
        var lines = FmTargets.Select(t => new ForgeLine(t.Stat, t.Current, 0, (int)(t.Max ?? 0), (int)(t.Target ?? t.Current))).ToArray();
        var advice = RuneAdvisor.Advise(lines, stock, new AdvisorSettings(AllowOver: FmAllowOver), Gains);
        FmAdviceText = advice.Kind switch
        {
            AdviceKind.Rune => $"Prochaine rune : {Forgemagie.Runes.First(r => r.Stat == advice.Rune!.Stat && r.Tier == advice.Rune.Tier).Name}  ·  {advice.Reason}",
            AdviceKind.Done => "Objectifs atteints : rien à poser.",
            _ => "Pas de conseil : " + advice.Reason,
        };
    }
}
