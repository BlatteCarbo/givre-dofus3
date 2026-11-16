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

/// <summary>Une ligne d'agrégat par objet dans le sous-onglet Statistiques HDV : quantité vendue, ventes, taxes, profit net.</summary>
public sealed class HdvStatItemRow
{
    public long Gid { get; init; }
    public string Name { get; init; } = "";
    public int Units { get; init; }
    public long Sales { get; init; }
    public long Taxes { get; init; }
    public long Net { get; init; }
    public double BarWidth { get; init; }   // largeur px de la barre (|profit net| relatif au max) pour le graphe par objet
    public Bitmap? Icon { get; init; }
    public bool HasIcon => Icon is not null;
    public string UnitsText => "x" + Units.ToString("N0", CultureInfo.CurrentUICulture);
    public string SalesText => Sales.ToString("N0", CultureInfo.CurrentUICulture);
    public string TaxesText => Taxes.ToString("N0", CultureInfo.CurrentUICulture);
    public string NetText => Net.ToString("N0", CultureInfo.CurrentUICulture);
    public bool IsProfit => Net >= 0;
    // Couleur par SIGNE : profit = vert, perte = rouge (le montant porte déjà le « - »). Sert au texte ET à la barre.
    private static readonly IBrush ProfitBrush = new SolidColorBrush(Color.Parse("#63D3A6"));
    private static readonly IBrush LossBrush = new SolidColorBrush(Color.Parse("#F0616D"));
    public IBrush NetBrush => Net >= 0 ? ProfitBrush : LossBrush;
    public IBrush BarBrush => Net >= 0 ? ProfitBrush : LossBrush;
}

/// <summary>Une transaction BRUTE horodatée (vente ou taxe) du journal HDV : pour la traçabilité fine dans le sous-onglet
/// Statistiques (et exportée telle quelle en CSV). La plus récente en tête.</summary>
public sealed class HdvStatTxRow
{
    public long Ts { get; init; }
    public bool IsTax { get; init; }
    public string Name { get; init; } = "";
    public int Qty { get; init; }
    public long Amount { get; init; }
    public string WhenText => DateTimeOffset.FromUnixTimeMilliseconds(Ts).LocalDateTime.ToString("dd/MM/yy HH:mm:ss", CultureInfo.CurrentUICulture);
    public string TypeText => IsTax ? Loc.T("L.Bots.Hdv.Taxes") : Loc.T("L.Bots.Hdv.Sales");
    public string QtyText => "x" + Qty.ToString("N0", CultureInfo.CurrentUICulture);
    public string AmountText => (IsTax ? "-" : "+") + Amount.ToString("N0", CultureInfo.CurrentUICulture);
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#63D3A6"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#F0616D"));
    public IBrush AmountBrush => IsTax ? Red : Green;
}

/// <summary>
/// VM du sous-onglet « Statistiques » du HDV (par personnage), calqué sur <see cref="BotStatsViewModel"/> : sélecteur de
/// période (24h/7j/30j/1an), KPIs (ventes, taxes, profit net, lots vendus), tableau par objet, courbe de profit net cumulé,
/// export CSV. Lit le journal persistant <see cref="HdvStatsStore"/> et agrège via <see cref="BotStatsStore.Window"/>.
/// </summary>
public partial class HdvStatsViewModel : ObservableObject
{
    private string? _charName;

    [ObservableProperty] public partial StatsPeriod Period { get; set; } = StatsPeriod.Week;
    public ObservableCollection<HdvStatItemRow> Items { get; } = new();

    [ObservableProperty] public partial long TotalSales { get; set; }
    [ObservableProperty] public partial long TotalTaxes { get; set; }
    [ObservableProperty] public partial long NetProfit { get; set; }
    [ObservableProperty] public partial int LotsSold { get; set; }

    [ObservableProperty] public partial List<Point> LinePoints { get; set; } = new();
    [ObservableProperty] public partial List<Point> AreaPoints { get; set; } = new();
    [ObservableProperty] public partial bool HasChart { get; set; }

    /// <summary>Journal BRUT horodaté (ventes + taxes) de la période, le plus récent en tête — traçabilité fine.</summary>
    public ObservableCollection<HdvStatTxRow> Transactions { get; } = new();
    public bool HasTransactions => Transactions.Count > 0;

    public bool PeriodIsDay => Period == StatsPeriod.Day;
    public bool PeriodIsWeek => Period == StatsPeriod.Week;
    public bool PeriodIsMonth => Period == StatsPeriod.Month;
    public bool PeriodIsYear => Period == StatsPeriod.Year;
    public bool HasItems => Items.Count > 0;

    public string TotalSalesText => TotalSales.ToString("N0", CultureInfo.CurrentUICulture);
    public string TotalTaxesText => TotalTaxes.ToString("N0", CultureInfo.CurrentUICulture);
    public string NetProfitText => NetProfit.ToString("N0", CultureInfo.CurrentUICulture);
    public string LotsSoldText => LotsSold.ToString("N0", CultureInfo.CurrentUICulture);

    partial void OnTotalSalesChanged(long v) => OnPropertyChanged(nameof(TotalSalesText));
    partial void OnTotalTaxesChanged(long v) => OnPropertyChanged(nameof(TotalTaxesText));
    partial void OnNetProfitChanged(long v) => OnPropertyChanged(nameof(NetProfitText));
    partial void OnLotsSoldChanged(int v) => OnPropertyChanged(nameof(LotsSoldText));

    [RelayCommand]
    private void SetPeriod(string p)
    {
        Period = p switch { "day" => StatsPeriod.Day, "week" => StatsPeriod.Week, "month" => StatsPeriod.Month, "year" => StatsPeriod.Year, _ => Period };
        OnPropertyChanged(nameof(PeriodIsDay)); OnPropertyChanged(nameof(PeriodIsWeek));
        OnPropertyChanged(nameof(PeriodIsMonth)); OnPropertyChanged(nameof(PeriodIsYear));
        Refresh();
    }

    /// <summary>Fixe le personnage sans recalculer (pas d'I/O disque tant que l'onglet Stats n'est pas ouvert).</summary>
    public void SetChar(string? charName) => _charName = charName;

    /// <summary>Recalcule les agrégats depuis le journal persistant pour la période courante. La LECTURE DISQUE
    /// (EntriesInWindow, potentiellement volumineuse) est déportée hors du thread UI ; le remplissage des collections
    /// revient sur le thread UI (pas de micro-freeze à l'ouverture de l'onglet Stats / au changement de période).</summary>
    public void Refresh()
    {
        string? charName = _charName; var period = Period;
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            var (f, t) = BotStatsStore.Window(period, null);
            var e = HdvStatsStore.EntriesInWindow(charName, f, t);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyRefresh(e, f, t));
        });
    }

    private void ApplyRefresh(System.Collections.Generic.IReadOnlyList<HdvStatEntry> entries, long from, long to)
    {
        long sales = 0, taxes = 0; int lots = 0;
        var byGid = new Dictionary<long, (string Name, int Units, long Sales, long Taxes)>();
        foreach (var e in entries)
        {
            byGid.TryGetValue(e.Gid, out var cur);
            if (string.IsNullOrEmpty(cur.Name)) cur.Name = e.Name;
            if (e.IsTax) { taxes += e.Amount; cur.Taxes += e.Amount; }
            else { sales += e.Amount; lots++; cur.Units += e.Qty; cur.Sales += e.Amount; }
            byGid[e.Gid] = cur;
        }
        TotalSales = sales; TotalTaxes = taxes; NetProfit = sales - taxes; LotsSold = lots;
        Items.Clear();
        var aggs = byGid.OrderByDescending(k => k.Value.Sales - k.Value.Taxes).ToList();
        // Barre = MAGNITUDE du profit net (|net|) relative au max -> une grosse perte a une grande barre (rouge), un gros
        // gain une grande barre (verte). Sinon toutes les pertes auraient une barre nulle.
        long maxAbs = aggs.Count > 0 ? Math.Max(1, aggs.Max(k => Math.Abs(k.Value.Sales - k.Value.Taxes))) : 1;
        const double BarMaxPx = 220.0;
        foreach (var kv in aggs)
        {
            long net = kv.Value.Sales - kv.Value.Taxes;
            Items.Add(new HdvStatItemRow
            {
                Gid = kv.Key, Name = kv.Value.Name, Units = kv.Value.Units, Sales = kv.Value.Sales,
                Taxes = kv.Value.Taxes, Net = net, Icon = IconFor(kv.Key),
                BarWidth = Math.Max(2.0, (double)Math.Abs(net) / maxAbs * BarMaxPx),
            });
        }
        OnPropertyChanged(nameof(HasItems));
        // Journal brut horodaté (le plus récent en tête), plafonné pour l'affichage — l'export CSV, lui, reste exhaustif.
        Transactions.Clear();
        foreach (var e in entries.OrderByDescending(x => x.Ts).Take(1000))
            Transactions.Add(new HdvStatTxRow { Ts = e.Ts, IsTax = e.IsTax, Name = e.Name, Qty = e.Qty, Amount = e.Amount });
        OnPropertyChanged(nameof(HasTransactions));
        BuildChart(entries, from, to);
    }

    private static Bitmap? IconFor(long gid) => Stores.InteractiveIcons.GetByIcon(Stores.Launch.ItemIconId(gid));

    // Courbe de profit NET cumulé (ventes - taxes) sur la période, en N buckets temporels -> Polyline/Polygon (façon BotStats).
    private const double ChartW = 1000, ChartH = 280, ChartTop = 16;
    private void BuildChart(IReadOnlyList<HdvStatEntry> entries, long from, long to)
    {
        const int N = 48;
        var buckets = new double[N];
        double span = Math.Max(1, to - from);
        foreach (var e in entries)
        {
            int i = (int)((e.Ts - from) / span * N);
            if (i < 0) i = 0; else if (i >= N) i = N - 1;
            buckets[i] += e.IsTax ? -e.Amount : e.Amount;
        }
        var series = new double[N];
        double run = 0, min = 0, max = 0;
        for (int i = 0; i < N; i++) { run += buckets[i]; series[i] = run; if (run < min) min = run; if (run > max) max = run; }
        HasChart = entries.Count > 0 && (max - min) > 0.5;
        double range = Math.Max(1, max - min);
        var line = new List<Point>(N);
        for (int i = 0; i < N; i++)
        {
            double x = (double)i / (N - 1) * ChartW;
            double y = HasChart ? ChartH - (series[i] - min) / range * (ChartH - ChartTop) : ChartH;
            line.Add(new Point(x, y));
        }
        LinePoints = line;
        var area = new List<Point>(line.Count + 2) { new(0, ChartH) };
        area.AddRange(line);
        area.Add(new Point(ChartW, ChartH));
        AreaPoints = area;
    }

    /// <summary>CSV du journal (ventes+taxes) sur la période — BOM UTF-8 côté écriture (Excel FR).</summary>
    public string BuildCsv()
    {
        var (from, to) = BotStatsStore.Window(Period, null);
        var entries = HdvStatsStore.EntriesInWindow(_charName, from, to);
        var sb = new StringBuilder();
        sb.Append(Loc.T("L.Bots.Hdv.CsvHeader")).Append('\n');
        foreach (var e in entries.OrderBy(x => x.Ts))
        {
            var ts = Services.Eco.CsvTime.Local(e.Ts, TimeZoneInfo.Local);
            string type = e.IsTax ? Loc.T("L.Bots.Hdv.Taxes") : Loc.T("L.Bots.Hdv.Sales");
            sb.Append(ts).Append(';').Append(Csv(type)).Append(';').Append(Csv(e.Name)).Append(';')
              .Append(e.Qty.ToString(CultureInfo.InvariantCulture)).Append(';')
              .Append(e.Amount.ToString(CultureInfo.InvariantCulture)).Append(';')
              .Append(Services.Eco.CsvTime.Utc(e.Ts)).Append('\n');
        }
        return sb.ToString();
        static string Csv(string s) => s.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    public string CsvSuggestedName()
    {
        var safe = new string((_charName ?? "hdv").Where(char.IsLetterOrDigit).ToArray());
        return $"hdv_{(string.IsNullOrEmpty(safe) ? "stats" : safe)}_{Period}".ToLowerInvariant() + ".csv";
    }
}
