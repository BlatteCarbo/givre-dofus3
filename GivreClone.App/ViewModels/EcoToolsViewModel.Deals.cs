using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Eco;

namespace GivreClone.App.ViewModels;

/// <summary>Ligne d'affaire. Prix max, quantité et budget sont des limites : le prix payé est relu sur l'HDV au moment de l'achat.</summary>
public sealed partial class EcoDealRow : ObservableObject
{
    public EcoDealRow(string name, string lotText, string unitText, string averageText, string discountText, string gainText, string ageText, long gid,
        long maxUnitPrice, int quantity, long budget)
    {
        Name = name;
        LotText = lotText;
        UnitText = unitText;
        AverageText = averageText;
        DiscountText = discountText;
        GainText = gainText;
        AgeText = ageText;
        Gid = gid;
        Icon = EcoService.IconOf(gid);
        if (maxUnitPrice > 0) MaxUnitPrice = maxUnitPrice;
        if (quantity > 0) BuyQuantity = quantity;
        if (budget > 0) BuyBudget = budget;
    }

    public string Name { get; }
    public string LotText { get; }
    public string UnitText { get; }
    public string AverageText { get; }
    public string DiscountText { get; }
    public string GainText { get; }
    public string AgeText { get; }
    public long Gid { get; }
    public Avalonia.Media.Imaging.Bitmap? Icon { get; }
    public bool HasIcon => Icon is not null;

    [ObservableProperty] public partial decimal? MaxUnitPrice { get; set; }
    [ObservableProperty] public partial decimal? BuyQuantity { get; set; }
    [ObservableProperty] public partial decimal? BuyBudget { get; set; }
    [ObservableProperty] public partial string BuyProgress { get; set; } = "";
    [ObservableProperty] public partial bool Buying { get; set; }
    public string BuyButtonText => Buying ? "Stop" : "Acheter";
    partial void OnBuyingChanged(bool value) => OnPropertyChanged(nameof(BuyButtonText));
}

/// <summary>Ligne d'arbitrage de découpe (lecture seule, aucun achat) : « Acheter lot X → Revendre N × lot Y ».</summary>
public sealed class EcoBatchSplitRow
{
    public EcoBatchSplitRow(BatchSplitDeal d, string ageText)
    {
        Name = d.Name;
        Gid = d.Gid;
        Icon = EcoService.IconOf(d.Gid);
        RouteText = $"Acheter lot ×{d.BuyLotSize} ({d.BuyLotCost:N0}) → Revendre {d.SplitCount} × lot ×{d.SellLotSize} ({d.SellLotPrice:N0})";
        ProfitText = "+" + d.NetProfit.ToString("N0");
        MarginText = $"+{d.NetMarginPercent:0.#} %";
        DetailText = $"Brut {d.GrossRevenue:N0} − taxe {d.TaxCost:N0} − achat {d.BuyLotCost:N0}";
        AgeText = ageText;
    }

    public string Name { get; }
    public long Gid { get; }
    public Avalonia.Media.Imaging.Bitmap? Icon { get; }
    public bool HasIcon => Icon is not null;
    public string RouteText { get; }
    public string ProfitText { get; }
    public string MarginText { get; }
    public string DetailText { get; }
    public string AgeText { get; }
}

public sealed partial class EcoToolsViewModel
{
    public ObservableCollection<EcoDealRow> Deals { get; } = new();
    public ObservableCollection<EcoBatchSplitRow> BatchSplitDeals { get; } = new();
    [ObservableProperty] public partial decimal? BatchSplitMinMargin { get; set; } = 5;
    [ObservableProperty] public partial decimal? BatchSplitMinProfit { get; set; } = 500;
    [ObservableProperty] public partial string BatchSplitStatus { get; set; } = "";
    [ObservableProperty] public partial decimal? DealMinDiscount { get; set; } = 25;
    [ObservableProperty] public partial decimal? DealMinGain { get; set; } = 1000;
    [ObservableProperty] public partial decimal? DealMaxAgeMin { get; set; } = 180;
    [ObservableProperty] public partial string DealsStatus { get; set; } = "Ouvre l'HDV avec un bot (Bots → HDV) pour relever des prix, puis actualise.";

    private CancellationTokenSource? _dealBuyCts;
    private EcoDealRow? _dealRow;

    [RelayCommand]
    private void RefreshDeals()
    {
        if (_dealBuyCts is not null)
        {
            DealsStatus = "Achat en cours. Stoppe-le avant d'actualiser la liste.";
            return;
        }
        Deals.Clear();
        BatchSplitDeals.Clear();
        BatchSplitStatus = "";
        uint pid = CurrentPid;
        if (pid == 0) { DealsStatus = "Sélectionne un bot connecté : les prix viennent de l'HDV qu'il a déjà observé."; return; }
        var l = Stores.Launch;
        var quotes = l.Hdv.KnownGids(pid).Select(g =>
        {
            long age = l.Hdv.PriceAgeMs(pid, g);
            long avg = l.Hdv.AverageFor(pid, g);
            if (avg <= 0 && g > 0 && g <= int.MaxValue) avg = l.AveragePrice(pid, (int)g);
            return new MarketQuote(g, NameOf(g), l.Hdv.MarketPricesFor(pid, g), avg, age == long.MaxValue ? null : age);
        }).ToList();
        long? maxAge = DealMaxAgeMin is > 0 ? (long)(DealMaxAgeMin.Value * 60_000) : null;
        var found = MarketDeals.Find(quotes, (double)(DealMinDiscount ?? 25), (long)(DealMinGain ?? 0), maxAge);
        foreach (var d in found.Take(200))
            Deals.Add(new EcoDealRow(d.Name, d.LotSize == 1 ? "×1" : "×" + d.LotSize, d.UnitPrice.ToString("N3"), d.Average.ToString("N0"),
                $"\u2212{d.DiscountPercent:0} %", "+" + d.LotGain.ToString("N0"), d.AgeMs is { } a ? FormatAge(a) : "?", d.Gid,
                CeilUnit(d.UnitPrice), d.LotSize, d.LotCost));
        DealsStatus = $"{quotes.Count} objet(s) avec prix connu, {found.Count} bonne(s) affaire(s). Prix affichés indicatifs. Acheter relit l'HDV avant chaque lot.";

        var splits = MarketDeals.FindBatchSplits(quotes, (double)(BatchSplitMinMargin ?? 0), (long)(BatchSplitMinProfit ?? 0), 0.02m, maxAge);
        foreach (var s in splits.Take(100)) BatchSplitDeals.Add(new EcoBatchSplitRow(s, s.AgeMs is { } sa ? FormatAge(sa) : "?"));
        BatchSplitStatus = $"{splits.Count} découpe(s) rentable(s) après taxe de 2 %. Consultatif : rien n'est envoyé à l'HDV, vérifie les prix avant d'agir.";
    }

    [RelayCommand]
    private void CopyBatchSplitName(EcoBatchSplitRow? d) { if (d is not null) { CopyRequested?.Invoke(d.Name); NotebookStatus = "Nom copié : " + d.Name; } }

    private static long CeilUnit(decimal unit)
    {
        if (unit <= 0 || unit >= long.MaxValue) return 0;
        return (long)Math.Ceiling(unit);
    }

    private static string FormatAge(long ms) => ms < 60_000 ? "< 1 min" : ms < 3_600_000 ? $"{ms / 60_000} min" : $"{ms / 3_600_000} h";

    [RelayCommand]
    private void CopyDealName(EcoDealRow? d) { if (d is not null) { CopyRequested?.Invoke(d.Name); NotebookStatus = "Nom copié : " + d.Name; } }

    [RelayCommand]
    private void OpenDealInCraft(EcoDealRow? d)
    {
        if (d is null) return;
        CraftSearch = d.Name;
        SelectedHit = CraftResults.FirstOrDefault(r => r.Gid == d.Gid) ?? SelectedHit;
    }

    /// <summary>Un bouton : Acheter lance la série, le même bouton devient Stop et annule le lot suivant.</summary>
    [RelayCommand]
    private async Task ToggleDealBuy(EcoDealRow? row)
    {
        if (row is null) return;
        if (_dealBuyCts is not null)
        {
            if (ReferenceEquals(_dealRow, row)) _dealBuyCts.Cancel();
            else row.BuyProgress = "Un achat est déjà en cours.";
            return;
        }
        if (!TryKamas(row.BuyQuantity, 100_000, out long quantity))
        { row.BuyProgress = "Fixe une quantité entre 1 et 100000."; return; }
        if (!TryKamas(row.MaxUnitPrice, long.MaxValue, out long maxUnit))
        { row.BuyProgress = "Fixe un prix unitaire maximum."; return; }
        if (!TryKamas(row.BuyBudget, long.MaxValue, out long budget))
        { row.BuyProgress = "Fixe un budget en kamas."; return; }
        uint pid = CurrentPid;
        if (pid == 0) { row.BuyProgress = "Sélectionne un bot connecté."; return; }

        var cts = new CancellationTokenSource();
        _dealBuyCts = cts;
        _dealRow = row;
        row.Buying = true;
        var port = new DealMarketPort(pid);
        try
        {
            var launch = Stores.Launch;
            if (!launch.HdvAnyOpen(pid))
            {
                row.BuyProgress = "Ouvre l'HDV en mode achat pour acheter.";
                return;
            }
            if (!launch.HdvBuyerOpen(pid))
            {
                row.BuyProgress = "Bascule de l'HDV en mode achat…";
                long map = launch.CurrentMap(pid) ?? 0;
                if (map == 0 || !await launch.HdvOpenBuyerAsync(pid, map, cts.Token).ConfigureAwait(true))
                {
                    row.BuyProgress = "Ouvre l'HDV en mode achat pour acheter.";
                    return;
                }
            }
            if (CurrentPid != pid) { row.BuyProgress = "Achat arrêté : le bot sélectionné a changé."; return; }

            row.BuyProgress = "Lecture du marché…";
            var result = await ObservedLotBuy.RunAsync(port, new ObservedLotOrder(row.Gid, (int)quantity, maxUnit, budget),
                p => OnUi(() => { if (ReferenceEquals(_dealRow, row)) row.BuyProgress = Live(p); }), cts.Token).ConfigureAwait(true);
            row.BuyProgress = port.BotChanged && result.Stop == "arrêté" ? StoppedForBot(result) : Describe(result);
        }
        catch (OperationCanceledException) { row.BuyProgress = "Arrêté."; }
        catch (Exception e) { row.BuyProgress = "Achat interrompu : " + e.Message; }
        finally
        {
            if (ReferenceEquals(_dealBuyCts, cts)) { _dealBuyCts = null; _dealRow = null; }
            row.Buying = false;
            cts.Dispose();
        }
    }

    private static bool TryKamas(decimal? value, long max, out long n)
    {
        n = 0;
        if (value is not decimal v || v != decimal.Truncate(v) || v < 1 || v > max) return false;
        n = (long)v;
        return true;
    }

    private static void OnUi(Action apply)
    {
        var ui = Dispatcher.UIThread;
        if (ui.CheckAccess()) apply();
        else ui.Invoke(apply);
    }

    private static string Live(ObservedLotProgress p)
        => $"{p.Bought:N0} acheté(s), {p.Spent:N0} kamas. Reste {p.Remaining:N0}, budget {p.BudgetLeft:N0}.";

    private static string Describe(ObservedLotBuyResult r) => r.Stop switch
    {
        "quantité atteinte" => $"{r.Bought:N0} acheté(s), {r.Spent:N0} kamas. Quantité atteinte.",
        "budget atteint" when r.Bought == 0 => "Budget insuffisant pour un lot observé.",
        "budget atteint" => $"{r.Bought:N0} acheté(s), {r.Spent:N0} kamas. Budget atteint.",
        "achat refusé" when r.Bought == 0 => "Achat refusé. Aucun kama dépensé.",
        "achat refusé" => $"{r.Bought:N0} acheté(s), {r.Spent:N0} kamas. Achat refusé.",
        "arrêté" when r.Bought == 0 => "Arrêté.",
        "arrêté" => $"{r.Bought:N0} acheté(s), {r.Spent:N0} kamas. Arrêté.",
        "prix non observé" when r.Bought == 0 => "Aucun prix de marché frais.",
        "prix non observé" => $"{r.Bought:N0} acheté(s), {r.Spent:N0} kamas. Plus de prix de marché frais.",
        "aucun lot achetable" when r.Bought == 0 => "Aucun lot observé sous le prix unitaire maximum.",
        "aucun lot achetable" => $"{r.Bought:N0} acheté(s), {r.Spent:N0} kamas. Plus de lot achetable.",
        "budget non défini" => "Fixe un budget en kamas.",
        "prix unitaire max absent" => "Fixe un prix unitaire maximum.",
        "rien à acheter" => "Fixe une quantité entre 1 et 100000.",
        _ => $"{r.Bought:N0} acheté(s), {r.Spent:N0} kamas.",
    };

    private static string StoppedForBot(ObservedLotBuyResult r)
        => r.Bought == 0
            ? "Achat arrêté : le bot sélectionné a changé."
            : $"{r.Bought:N0} acheté(s), {r.Spent:N0} kamas. Achat arrêté : le bot sélectionné a changé.";

    /// <summary>
    /// Prix d'achat : recherche acheteur fraîche (<see cref="LaunchService.HdvGetBuyerLotPricesAsync"/>), qui efface l'offre précédente
    /// et ne renvoie rien si la réponse n'arrive pas. L'âge est le temps de cet appel, pas une cote gardée en mémoire.
    /// L'achat ne compte que lorsque <see cref="LaunchService.HdvBuyByGidAsync"/> confirme le reçu serveur.
    /// </summary>
    private sealed class DealMarketPort : IObservedLotMarket
    {
        private readonly uint _pid;
        public bool BotChanged { get; private set; }
        public DealMarketPort(uint pid) => _pid = pid;

        public async Task<MarketObservation?> ReadAsync(long gid, CancellationToken ct)
        {
            if (CurrentPid != _pid) { BotChanged = true; throw new OperationCanceledException(ct); }
            long started = Environment.TickCount64;
            var prices = await Stores.Launch.HdvGetBuyerLotPricesAsync(_pid, gid, ct).ConfigureAwait(false);
            long age = Math.Max(0, Environment.TickCount64 - started);
            if (prices is not { Length: > 0 } || !prices.Any(p => p > 0)) return null;
            return new MarketObservation("marché", age, prices.ToArray());
        }

        public async Task<LotPurchaseEvidence> PurchaseAsync(long gid, int quantity, long maxPrice, CancellationToken ct)
        {
            if (CurrentPid != _pid) { BotChanged = true; throw new OperationCanceledException(ct); }
            if (gid <= 0 || quantity <= 0 || maxPrice <= 0) return LotPurchaseEvidence.None;
            bool confirmed = await Stores.Launch.HdvBuyByGidAsync(_pid, gid, quantity, maxPrice, ct).ConfigureAwait(false);
            return confirmed ? LotPurchaseEvidence.Receipt : LotPurchaseEvidence.None;
        }
    }
}
