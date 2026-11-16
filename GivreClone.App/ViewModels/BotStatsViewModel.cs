using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

/// <summary>Une ligne du feed "Dernieres actions" (recolte / combat) : pastille coloree + libelle + heure.</summary>
public sealed class StatsActionRow
{
    private static readonly IBrush HarvestDot = new SolidColorBrush(Color.Parse("#63D3A6"));
    private static readonly IBrush FightDot = new SolidColorBrush(Color.Parse("#6EA8FE"));

    public string Text { get; }
    public string Time { get; }
    public IBrush DotBrush { get; }
    public Bitmap? Icon { get; }
    public bool HasIcon => Icon is not null;
    /// <summary>Ligne de combat -> affiche l'icône Combat (asset combat.png) au lieu du point bleu.</summary>
    public bool IsFight { get; }
    /// <summary>Point coloré de repli : seulement si ni icône d'objet ni combat (ne devrait pas arriver).</summary>
    public bool ShowDot => !IsFight && !HasIcon;

    public StatsActionRow(StatsAction a)
    {
        IsFight = a.IsFight;
        Text = a.IsFight ? a.Label : (a.Qty > 0 ? $"{a.Label} ×{a.Qty}" : a.Label);
        Time = DateTimeOffset.FromUnixTimeMilliseconds(a.Ts).LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        DotBrush = a.IsFight ? FightDot : HarvestDot;
        Icon = a.IsFight ? null : Stores.InteractiveIcons.GetByIcon(a.IconId);   // asset du jeu (ex icone de Blé), null -> pastille
    }
}

/// <summary>Un jour proposé dans le sélecteur "jour précis" de l'onglet Stats (seulement les jours avec des données).</summary>
public sealed class DayItem
{
    public DateTime Date { get; }
    public string Label { get; }
    // Libellé dans la langue de l'app (« mer. 23 sept. 2026 ») : un format culturel sortirait en anglais (build AOT invariant).
    public DayItem(DateTime date) { Date = date; Label = LocDate.DayDate(date); }
    public override string ToString() => Label;   // affichage direct dans le ComboBox
}

/// <summary>Total recolte d'une ressource sur la periode (chip "icone Blé ×187").</summary>
public sealed class ResourceTotalRow
{
    public string Name { get; }
    public string QtyText { get; }
    public Bitmap? Icon { get; }
    public bool HasIcon => Icon is not null;

    public ResourceTotalRow(ResourceTotal t)
    {
        Name = t.Name;
        QtyText = $"×{t.Qty}";
        Icon = Stores.InteractiveIcons.GetByIcon(t.IconId);
    }
}

/// <summary>Ligne de la heatmap de trajet (onglet Stats) : carte, passages, récoltes, temps moyen par passage.</summary>
public sealed class HeatmapRowVm
{
    public string MapKey { get; }
    public string Passages { get; }
    public string Harvests { get; }
    public string AvgTime { get; }
    public HeatmapRowVm(HeatmapRow r)
    {
        MapKey = r.MapKey;
        Passages = r.Passages.ToString(CultureInfo.InvariantCulture);
        Harvests = r.Harvests.ToString(CultureInfo.InvariantCulture);
        AvgTime = r.AvgSec >= 60 ? $"{(int)(r.AvgSec / 60)}min{(int)(r.AvgSec % 60):00}" : $"{Math.Round(r.AvgSec)}s";
    }
}

/// <summary>
/// VM de l'onglet Stats (par perso selectionne) : cartes KPI (recoltes, combats, temps moyen/combat, temps de botting),
/// feed des dernieres actions, et courbe cumulee du temps de botting. Rafraichi periodiquement par <c>BotsViewModel</c>
/// depuis <see cref="BotStatsStore"/> pour le perso courant + la periode choisie (24h/7j/30j).
/// </summary>
public partial class BotStatsViewModel : ObservableObject
{
    // Repere logique de la courbe (mis a l'echelle par un Viewbox dans la vue).
    private const double ChartW = 1000, ChartH = 280, ChartTop = 16;

    [ObservableProperty] public partial StatsPeriod Period { get; set; } = StatsPeriod.Day;

    [ObservableProperty] public partial string HarvestsText { get; set; } = "0";
    [ObservableProperty] public partial string FightsText { get; set; } = "0";
    [ObservableProperty] public partial string AvgFightText { get; set; } = "—";
    [ObservableProperty] public partial string BottingText { get; set; } = "0h";
    [ObservableProperty] public partial string BottingTitle { get; set; } = Loc.T("L.Bots.Stats.BottingTitle");

    // KPI avancés (rentabilité / stabilité) : kamas/XP nets + taux/heure + combats gagnés/perdus + passages banque.
    [ObservableProperty] public partial string KamasGainedText { get; set; } = "0";
    [ObservableProperty] public partial string KamasPerHourText { get; set; } = "—";
    [ObservableProperty] public partial string XpGainedText { get; set; } = "0";
    [ObservableProperty] public partial string XpPerHourText { get; set; } = "—";
    [ObservableProperty] public partial string ResourcesText { get; set; } = "0";       // quantité totale récoltée
    [ObservableProperty] public partial string ResourcesPerHourText { get; set; } = "—";
    [ObservableProperty] public partial string EstimatedValuePerHourText { get; set; } = "—";   // valeur estimée HDV / heure
    [ObservableProperty] public partial string HarvestsPerHourText { get; set; } = "—";
    [ObservableProperty] public partial string FightsWonText { get; set; } = "0";    // combats gagnés (icône check verte)
    [ObservableProperty] public partial string FightsLostText { get; set; } = "0";   // combats perdus (croix rouge)
    [ObservableProperty] public partial string BankVisitsText { get; set; } = "0";
    [ObservableProperty] public partial string DeathsText { get; set; } = "0";       // nombre de morts du perso
    [ObservableProperty] public partial string PodsPerHourText { get; set; } = "—";  // pods récoltés / heure
    [ObservableProperty] public partial string FragmentsText { get; set; } = "";       // fragments d'anomalie (vide si aucun)

    /// <summary>Heatmap de trajet (par carte) : passages, récoltes, temps moyen — pour repérer les cartes rentables.</summary>
    public ObservableCollection<HeatmapRowVm> Heatmap { get; } = new();
    public bool HasHeatmap => Heatmap.Count > 0;
    private long _heatmapSig = -1;

    [ObservableProperty] public partial List<Point> LinePoints { get; set; } = new();
    [ObservableProperty] public partial List<Point> AreaPoints { get; set; } = new();
    [ObservableProperty] public partial bool HasChart { get; set; }
    [ObservableProperty] public partial string ChartMaxLabel { get; set; } = "0h";
    [ObservableProperty] public partial string ChartMidLabel { get; set; } = "";
    [ObservableProperty] public partial string ChartStartLabel { get; set; } = "";
    [ObservableProperty] public partial string ChartEndLabel { get; set; } = "";

    public ObservableCollection<StatsActionRow> Recent { get; } = new();
    public ObservableCollection<ResourceTotalRow> ResourceTotals { get; } = new();
    public bool HasResourceTotals => ResourceTotals.Count > 0;

    /// <summary>Jour précis sélectionné (mode "jour"). Null = période glissante (24h/7j/30j/1an).</summary>
    [ObservableProperty] public partial DayItem? SelectedDayItem { get; set; }
    public bool DayActive => SelectedDayItem is not null;
    /// <summary>Jours AVEC données proposés dans le sélecteur "jour précis" (reconstruit au changement de perso).</summary>
    public ObservableCollection<DayItem> AvailableDays { get; } = new();
    public bool HasDays => AvailableDays.Count > 0;

    // Pour surligner le bouton de periode actif (aucun surligné en mode "jour précis").
    public bool PeriodIsDay => !DayActive && Period == StatsPeriod.Day;
    public bool PeriodIsWeek => !DayActive && Period == StatsPeriod.Week;
    public bool PeriodIsMonth => !DayActive && Period == StatsPeriod.Month;
    public bool PeriodIsYear => !DayActive && Period == StatsPeriod.Year;

    private string? _charName;
    private bool _applyingPeriod;

    [RelayCommand]
    private void SetPeriod(string p)
    {
        _applyingPeriod = true;                 // on efface le jour précis SANS déclencher un double refresh
        Period = p switch { "Week" => StatsPeriod.Week, "Month" => StatsPeriod.Month, "Year" => StatsPeriod.Year, _ => StatsPeriod.Day };
        SelectedDayItem = null;
        _applyingPeriod = false;
        RaisePeriodFlags();
        Refresh(_charName, force: true);
    }

    partial void OnSelectedDayItemChanged(DayItem? value)
    {
        if (_applyingPeriod) return;
        RaisePeriodFlags();
        Refresh(_charName, force: true);
    }

    private void RaisePeriodFlags()
    {
        OnPropertyChanged(nameof(DayActive));
        OnPropertyChanged(nameof(PeriodIsDay));
        OnPropertyChanged(nameof(PeriodIsWeek));
        OnPropertyChanged(nameof(PeriodIsMonth));
        OnPropertyChanged(nameof(PeriodIsYear));
    }

    // (Re)construit la liste des jours AVEC données pour le sélecteur "jour précis". _applyingPeriod évite le refresh
    // récursif (on est déjà dans Refresh). Réinitialise la sélection (retour période) au changement de perso.
    private void RebuildAvailableDays(string? charName)
    {
        _applyingPeriod = true;
        AvailableDays.Clear();
        foreach (var d in Stores.Stats.DaysWithData(charName)) AvailableDays.Add(new DayItem(d));
        SelectedDayItem = null;
        _applyingPeriod = false;
        OnPropertyChanged(nameof(HasDays));
    }

    private long _lastRecentTs = -1;
    private bool _iconsWereLoaded;

    /// <summary>Recalcule l'affichage pour <paramref name="charName"/> et la periode courante. Appele periodiquement.</summary>
    public void Refresh(string? charName, bool force = false)
    {
        // Le bundle d'icones charge en tache de fond : quand il devient dispo, on FORCE un rebuild du feed pour resoudre les
        // images (sinon les StatsActionRow construites AVANT le chargement gardent leur icone a null -> pastille au lieu du Blé).
        if (Stores.InteractiveIcons.IsLoaded && !_iconsWereLoaded) { _iconsWereLoaded = true; _lastRecentTs = -1; }
        bool charChanged = !string.Equals(_charName, charName, StringComparison.Ordinal);
        if (force || charChanged) _lastRecentTs = -1;   // change de perso/periode -> reconstruit le feed
        // Sélecteur "jour précis" : (re)construit la liste des jours AVEC données au changement de perso (pas à chaque tick).
        if (charChanged || (force && AvailableDays.Count == 0)) RebuildAvailableDays(charName);
        _charName = charName;
        var snap = Stores.Stats.Compute(charName, Period, SelectedDayItem?.Date);

        HarvestsText = snap.Harvests.ToString(CultureInfo.InvariantCulture);
        FightsText = snap.Fights.ToString(CultureInfo.InvariantCulture);
        AvgFightText = snap.Fights > 0 ? $"{Math.Round(snap.AvgFightSec)}s" : "—";
        BottingText = FormatDuration(snap.BottingSec);
        BottingTitle = Loc.Fmt("L.Bots.Stats.BottingTitleFmt", FormatLong(snap.BottingSec));

        // KPI avancés. Les taux/heure ne sont significatifs qu'avec du temps de botting -> "—" sinon.
        bool hasTime = snap.BottingSec > 1;
        KamasGainedText = FormatKamasSigned(snap.KamasGained);
        KamasPerHourText = hasTime ? FormatRate(snap.KamasPerHour) + " k/h" : "—";
        XpGainedText = FormatKamasSigned(snap.XpGained);
        XpPerHourText = hasTime ? FormatRate(snap.XpPerHour) + " xp/h" : "—";
        ResourcesText = snap.ResourcesQty.ToString(CultureInfo.InvariantCulture);
        ResourcesPerHourText = hasTime ? FormatRate(snap.ResourcesPerHour) + "/h" : "—";
        EstimatedValuePerHourText = (hasTime && snap.EstimatedValue > 0) ? "≈ " + FormatRate(snap.EstimatedValuePerHour) + " k/h" : "—";
        HarvestsPerHourText = hasTime ? FormatRate(snap.HarvestsPerHour) + "/h" : "—";
        FightsWonText = snap.FightsWon.ToString(CultureInfo.InvariantCulture);
        FightsLostText = snap.FightsLost.ToString(CultureInfo.InvariantCulture);
        BankVisitsText = snap.BankVisits.ToString(CultureInfo.InvariantCulture);
        DeathsText = snap.Deaths.ToString(CultureInfo.InvariantCulture);
        PodsPerHourText = (hasTime && snap.PodsGenerated > 0) ? FormatRate(snap.PodsPerHour) + "/h" : "—";
        FragmentsText = snap.AnomalyFragments > 0
            ? Loc.Fmt("L.Bots.Stats.Fragments", snap.AnomalyFragments, hasTime ? FormatRate(snap.AnomalyFragmentsPerHour) : "—") : "";

        BuildChart(snap.BottingSeriesHours);
        BuildFeed(snap);
        BuildHeatmap(snap);
    }

    // Reconstruit la table heatmap seulement si les données ont changé (signature = count + somme passages+temps) -> pas de flicker.
    private void BuildHeatmap(StatsSnapshot snap)
    {
        long sig = snap.Heatmap.Count;
        foreach (var r in snap.Heatmap) sig = sig * 31 + r.Passages + (long)(r.AvgSec) + r.Harvests;
        if (sig == _heatmapSig) return;
        _heatmapSig = sig;
        Heatmap.Clear();
        foreach (var r in snap.Heatmap) Heatmap.Add(new HeatmapRowVm(r));
        OnPropertyChanged(nameof(HasHeatmap));
    }

    private void BuildChart(IReadOnlyList<double> series)
    {
        double max = series.Count > 0 ? Math.Max(series.Max(), 0.001) : 0;
        HasChart = max > 0.0005;   // au moins ~2 s de botting -> on affiche la courbe
        var line = new List<Point>(series.Count);
        for (int i = 0; i < series.Count; i++)
        {
            double x = series.Count == 1 ? 0 : (double)i / (series.Count - 1) * ChartW;
            double y = HasChart ? ChartH - series[i] / max * (ChartH - ChartTop) : ChartH;
            line.Add(new Point(x, y));
        }
        LinePoints = line;
        var area = new List<Point>(line.Count + 2) { new(0, ChartH) };
        area.AddRange(line);
        area.Add(new Point(ChartW, ChartH));
        AreaPoints = area;

        ChartMaxLabel = FormatAxis(max);
        ChartMidLabel = FormatAxis(max / 2);

        var (fromMs, toMs) = BotStatsStore.Window(Period, SelectedDayItem?.Date);
        var start = DateTimeOffset.FromUnixTimeMilliseconds(fromMs).LocalDateTime;
        var end = DateTimeOffset.FromUnixTimeMilliseconds(toMs).LocalDateTime;
        bool hours = DayActive || Period == StatsPeriod.Day;   // axe en heures pour 24h / un jour précis, en dates sinon
        ChartStartLabel = start.ToString(hours ? "HH:mm" : "dd/MM", CultureInfo.InvariantCulture);
        ChartEndLabel = end.ToString(hours ? "HH:mm" : "dd/MM", CultureInfo.InvariantCulture);
    }

    /// <summary>Construit le CSV (séparateur ';', pour Excel FR) de TOUTES les actions de la fenêtre courante.</summary>
    public string BuildCsv()
    {
        var (from, to) = BotStatsStore.Window(Period, SelectedDayItem?.Date);
        var actions = Stores.Stats.ActionsInWindow(_charName, from, to);
        var sb = new StringBuilder();
        sb.Append(Loc.T("L.Bots.Stats.CsvHeader")).Append('\n');
        foreach (var a in actions)
        {
            var ts = Services.Eco.CsvTime.Local(a.Ts, TimeZoneInfo.Local);
            string type = a.IsFight ? Loc.T("L.Stats.Fight") : Loc.T("L.Stats.Resource");
            sb.Append(ts).Append(';').Append(Csv(type)).Append(';').Append(Csv(a.Label)).Append(';')
              .Append(a.Qty.ToString(CultureInfo.InvariantCulture)).Append(';')
              .Append(Services.Eco.CsvTime.Utc(a.Ts)).Append('\n');
        }
        return sb.ToString();

        static string Csv(string s) => s.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    /// <summary>Nom de fichier suggéré pour l'export CSV : &lt;perso&gt;_&lt;portée&gt;.csv.</summary>
    public string CsvSuggestedName()
    {
        string scope = DayActive
            ? SelectedDayItem!.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : Period switch { StatsPeriod.Day => "24h", StatsPeriod.Week => "7j", StatsPeriod.Month => "30j", _ => "1an" };
        string who = string.IsNullOrWhiteSpace(_charName) ? "stats" : _charName!;
        var safe = string.Concat(who.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        return $"{safe}_{scope}.csv";
    }

    private void BuildFeed(StatsSnapshot snap)
    {
        long top = snap.Recent.Count > 0 ? snap.Recent[0].Ts : 0;
        if (top == _lastRecentTs && Recent.Count == snap.Recent.Count) return;   // rien de neuf -> pas de rebuild (evite le flicker + le saut de scroll)
        _lastRecentTs = top;
        Recent.Clear();
        foreach (var a in snap.Recent) Recent.Add(new StatsActionRow(a));
        ResourceTotals.Clear();
        foreach (var t in snap.ResourceTotals) ResourceTotals.Add(new ResourceTotalRow(t));
        OnPropertyChanged(nameof(HasResourceTotals));
    }

    // Kamas nets avec signe + séparateur de milliers (espace) : "+284 500", "-12 000", "0".
    private static string FormatKamasSigned(long k)
        => (k > 0 ? "+" : "") + k.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " ");

    // Taux : séparateur de milliers si >= 1000, 1 décimale si petit, entier sinon.
    internal static string FormatRate(double r)
    {
        if (Math.Abs(r) >= 1000) return Math.Round(r).ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " ");
        return (r >= 100 ? Math.Round(r) : Math.Round(r, 1)).ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatAxis(double hours) => hours >= 1 ? $"{hours:0.#}h" : $"{Math.Round(hours * 60)}min";

    private static string FormatDuration(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h{t.Minutes:00}";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes}min";
        return $"{(int)t.TotalSeconds}s";
    }

    private static string FormatLong(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        if (t.TotalHours >= 1) return Loc.Fmt("L.Bots.Stats.DurationHm", (int)t.TotalHours, t.Minutes);
        return Loc.Fmt("L.Bots.Stats.DurationM", (int)t.TotalMinutes);
    }
}
