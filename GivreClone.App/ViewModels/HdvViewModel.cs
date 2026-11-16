using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

/// <summary>Un de MES lots en vente : prix marché MIN pour ce lot vs mon prix (éditable), état (OK/à repricer/erreur), ignorer.</summary>
public partial class HdvListingRow : ObservableObject
{
    public int TooltipPid { get; init; }
    public int Uid { get; init; }
    public long Gid { get; init; }
    public int Qty { get; init; }
    public int Slot { get; init; }
    public string Name { get; init; } = "";
    public Bitmap? Icon { get; init; }
    public bool HasIcon => Icon is not null;
    public Action<HdvListingRow>? IgnoreToggled { get; init; }

    [ObservableProperty] public partial long Price { get; set; }    // MON prix
    [ObservableProperty] public partial long Market { get; set; }   // prix marché MIN pour ce lot (le moins cher en vente)
    [ObservableProperty] public partial bool Ignored { get; set; }
    [ObservableProperty] public partial bool FetchError { get; set; }
    [ObservableProperty] public partial bool IsFetching { get; set; }   // prix de ce gid en cours de récupération -> spinner dans la colonne État
    [ObservableProperty] public partial long MinimumPrice { get; set; } = 1;
    [ObservableProperty] public partial long SuggestedPrice { get; set; } = -1;
    public bool IsFloorLimited => !Ignored && !FetchError && Market > 0 && Price > Market && MinimumPrice >= Price;
    public bool HasSuggestedPrice => SuggestedPrice > 0;
    public string DecisionText => Ignored ? Loc.T("L.Bots.Hdv.DecisionIgnored") :
        IsFloorLimited ? Loc.Fmt("L.Bots.Hdv.DecisionFloor", MinimumPrice.ToString("N0", CultureInfo.CurrentUICulture)) :
        HasSuggestedPrice ? Loc.Fmt("L.Bots.Hdv.DecisionTarget", SuggestedPrice.ToString("N0", CultureInfo.CurrentUICulture)) : "";
    partial void OnMinimumPriceChanged(long value) => RaiseStatus();
    partial void OnSuggestedPriceChanged(long value) => RaiseStatus();

    public string LotText => "x" + Qty;
    public string PriceText => Price.ToString("N0", CultureInfo.CurrentUICulture);
    public string MarketText => Market > 0 ? Market.ToString("N0", CultureInfo.CurrentUICulture) : "—";
    public bool HasMarket => Market > 0;   // affiche le logo Kamas seulement quand un prix marché est connu

    // État : erreur (rouge) > inconnu (gris, prix pas encore reçu) > à repricer (ambre, je suis plus cher) > OK (vert, moins cher).
    public bool IsStatusError => FetchError;
    public bool IsStatusUnknown => !FetchError && Market <= 0;
    public bool IsStatusWarning => !Ignored && !FetchError && SuggestedPrice > 0;
    public bool IsStatusOk => !Ignored && !FetchError && Market > 0 && Price <= Market;

    partial void OnMarketChanged(long v) => RaiseStatus();
    partial void OnPriceChanged(long v) { OnPropertyChanged(nameof(PriceText)); RaiseStatus(); }
    partial void OnFetchErrorChanged(bool v) => RaiseStatus();
    public double RowOpacity => Ignored ? 0.5 : 1.0;   // lot ignoré (exclu du reprice auto) = grisé pour le distinguer d'un coup d'oeil
    partial void OnIgnoredChanged(bool v) { OnPropertyChanged(nameof(RowOpacity)); RaiseStatus(); IgnoreToggled?.Invoke(this); }
    private void RaiseStatus()
    {
        OnPropertyChanged(nameof(MarketText)); OnPropertyChanged(nameof(HasMarket));
        OnPropertyChanged(nameof(IsStatusError)); OnPropertyChanged(nameof(IsStatusUnknown));
        OnPropertyChanged(nameof(IsStatusWarning)); OnPropertyChanged(nameof(IsStatusOk));
        OnPropertyChanged(nameof(IsFloorLimited)); OnPropertyChanged(nameof(HasSuggestedPrice)); OnPropertyChanged(nameof(DecisionText));
    }
}

/// <summary>Une ligne du journal des taxes (mise en vente / reprice) : quel objet, quel lot, combien de kamas de taxe.</summary>
public sealed class HdvTaxEntry
{
    public DateTime Ts { get; init; } = DateTime.Now;   // horodatage local pour la traçabilité (affiché + exporté)
    public string Name { get; init; } = "";
    public int Lot { get; init; }
    public long Tax { get; init; }
    public string Action { get; init; } = "";
    public string LotText => "x" + Lot;
    public string TaxText => "-" + Tax.ToString("N0", CultureInfo.CurrentUICulture);
    public string WhenText => Ts.ToString("dd/MM HH:mm:ss", CultureInfo.CurrentUICulture);
}

/// <summary>Une ligne du journal des ventes abouties (event lqn) : quel objet, quel lot, combien encaissé.</summary>
public sealed class HdvSaleEntry
{
    public DateTime Ts { get; init; } = DateTime.Now;
    public string Name { get; init; } = "";
    public int Lot { get; init; }
    public long Amount { get; init; }
    public string LotText => "x" + Lot;
    public string AmountText => "+" + Amount.ToString("N0", CultureInfo.CurrentUICulture);
    public string WhenText => Ts.ToString("dd/MM HH:mm:ss", CultureInfo.CurrentUICulture);
}

/// <summary>Une ligne du JOURNAL D'ACTIVITÉ HDV (verbeux, horodaté) : reprix/vente/retrait/refresh + couleur par niveau.</summary>
public sealed class HdvLogEntry
{
    public DateTime Ts { get; init; } = DateTime.Now;
    public string Text { get; init; } = "";
    public IBrush Color { get; init; } = Brushes.Gray;
    public string WhenText => Ts.ToString("HH:mm:ss", CultureInfo.CurrentUICulture);
}

/// <summary>
/// VM du module HDV par personnage: lots en vente (prix marché MIN vs mon prix, align-or-undercut sans se
/// sous-coter soi-même), mise en vente depuis le sac+banque (objets éligibles), watcher de repricing périodique, suivi des
/// TAXES payées + plafond. Config persistée via <see cref="HdvProfileStore"/>.
/// </summary>
public partial class HdvViewModel : ObservableObject
{
    private readonly LaunchService _launch = Stores.Launch;
    private HdvProfile _cfg = new();
    private bool _loading;
    private bool _reselecting;
    private uint _pid;
    private string? _charName;   // perso courant -> clé du journal HDV persistant (HdvStatsStore)
    private string? _hdvProfileKey;
    private int _mutationEpoch;
    private bool _checkedLoaded;

    // A modal confirmation or a previously armed sell button can survive an
    // asynchronous PID reuse or profile switch. These tickets pin the actual
    // server/character and the VM mutation generation at the first user
    // interaction. They are UI preconditions, NOT gameplay ACKs; LaunchService
    // still performs its independent durable pre-send authorization.
    private readonly record struct HdvUiMutationTicket(
        uint Pid, int ServerId, long CharacterId, int Epoch);

    private HdvUiMutationTicket CaptureHdvUiMutationTicket()
    {
        uint pid = _pid;
        if (pid == 0) return default;
        int server = _launch.ServerIdOf(pid);
        long actor = _launch.CharacterId(pid);
        if (server <= 0 || actor <= 0) return default;
        return new(pid, server, actor, Volatile.Read(ref _mutationEpoch));
    }

    private bool IsCurrentHdvUiMutationTicket(HdvUiMutationTicket ticket)
        => ticket.Pid > 0 && ticket.ServerId > 0 && ticket.CharacterId > 0 &&
           ticket.Pid == _pid && ticket.Epoch == Volatile.Read(ref _mutationEpoch) &&
           _launch.ServerIdOf(ticket.Pid) == ticket.ServerId &&
           _launch.CharacterId(ticket.Pid) == ticket.CharacterId;

    public IReadOnlyList<HdvLocation> Locations { get; } = HdvLocations.All;

    /// <summary>Sous-onglet Statistiques (agrégats persistés par perso).</summary>
    public HdvStatsViewModel Stats { get; } = new();
    // 5 sous-onglets (HDV/Achat/Rentabilité/Statistiques/Réglages) : un seul vrai à la fois. ShowMain = aucun des 4 autres
    // (pas un bool stocké) pour ne jamais pouvoir désynchroniser "HDV" des 4 autres, comme c'était le cas quand ShowMain
    // valait juste "!ShowStats" (ShowBuy/ShowScan restaient vrais en passant sur HDV ou Statistiques).
    [ObservableProperty] public partial bool ShowStats { get; set; }
    [ObservableProperty] public partial bool ShowSettings { get; set; }
    public bool ShowMain => !ShowStats && !ShowBuy && !ShowScan && !ShowSettings;
    partial void OnShowStatsChanged(bool v) { OnPropertyChanged(nameof(ShowMain)); if (v) Stats.Refresh(); }
    partial void OnShowSettingsChanged(bool v) => OnPropertyChanged(nameof(ShowMain));
    [RelayCommand] private void ShowMainView() { ShowStats = false; ShowBuy = false; ShowScan = false; ShowSettings = false; OnPropertyChanged(nameof(ShowMain)); }
    [RelayCommand] private void ShowStatsView() { ShowBuy = false; ShowScan = false; ShowSettings = false; ShowStats = true; }

    // ---- Config persistée ----
    [ObservableProperty] public partial HdvLocation? SelectedLocation { get; set; }
    [ObservableProperty] public partial int PriceStep { get; set; } = 1;
    [ObservableProperty] public partial int FloorPercent { get; set; } = 10;
    // Délais RANDOMISÉS entre deux bornes (min..max) -> humanisation (jamais deux cycles identiques). Max<=Min => valeur fixe = Min.
    [ObservableProperty] public partial int RefreshRateMs { get; set; } = 5000;          // borne MIN du rafraîchissement
    [ObservableProperty] public partial int RefreshRateMaxMs { get; set; } = 8000;       // borne MAX
    [ObservableProperty] public partial int DelayBetweenSalesMs { get; set; } = 1000;    // borne MIN du délai entre reprices
    [ObservableProperty] public partial int DelayBetweenSalesMaxMs { get; set; } = 2000; // borne MAX
    [ObservableProperty] public partial long MaxTaxSpend { get; set; }   // plafond de taxe (0 = illimité)
    [ObservableProperty] public partial int RepriceCooldownSec { get; set; } = 300;
    [ObservableProperty] public partial decimal PriceCeilingMultiplier { get; set; } = HdvSalePriceGuard.DefaultCeilingMultiplier;
    [ObservableProperty] public partial long MaxTaxPerOperation { get; set; }
    private readonly Dictionary<(long Gid, int Qty), long> _lastRepriceAt = new();

    // ---- Mise en vente unitaire (objet sélectionné dans la fenêtre « Mettre en vente ») ----
    [ObservableProperty] public partial long SellUnitPrice { get; set; }
    [ObservableProperty] public partial int SellLotQty { get; set; } = 1;

    // ---- Mes ventes (reprice) ----
    public ObservableCollection<HdvListingRow> Listings { get; } = new();            // SOURCE (le moteur reprice l'utilise directement)
    public ObservableCollection<HdvListingRow> ListingsView { get; } = new();        // vue filtrée/triée liée à l'UI (mêmes instances de lignes)
    [ObservableProperty] public partial string ListingsFilter { get; set; } = "";
    [ObservableProperty] public partial int ListingsSortIndex { get; set; }
    public string[] ListingSortModes => new[] { Loc.T("L.Bots.Hdv.SortDefault"), Loc.T("L.Bots.Hdv.SortToReprice"), Loc.T("L.Bots.Hdv.SortName"), Loc.T("L.Bots.Hdv.SortGap") };
    partial void OnListingsFilterChanged(string v) => RebuildListingsView();
    partial void OnListingsSortIndexChanged(int v) => RebuildListingsView();
    public ObservableCollection<HdvTaxEntry> TaxLog { get; } = new();
    public ObservableCollection<HdvSaleEntry> SalesLog { get; } = new();
    public ObservableCollection<HdvLogEntry> Activity { get; } = new();   // journal d'activité verbeux + horodaté
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial bool WatcherRunning { get; set; }
    [ObservableProperty] public partial bool Busy { get; set; }          // refresh/undercut-all en cours -> spinner
    [ObservableProperty] public partial bool Traveling { get; set; }     // voyage/ouverture HDV auto en cours -> spinner + bouton grisé
    [ObservableProperty] public partial long TotalTax { get; set; }
    [ObservableProperty] public partial long TotalSales { get; set; }    // kamas encaissés en vente (event lqn)

    private readonly HashSet<long> _ignored = new();
    private readonly HashSet<long> _fetchErrors = new();
    private CancellationTokenSource? _watchCts;
    private CancellationTokenSource? _travelCts;

    public bool HasListings => Listings.Count > 0;
    /// <summary>HDV ouvert mais aucun lot en vente (message « rien en vente »). Fermé -> l'UI montre l'attente à la place.</summary>
    public bool ShowEmptyListings => IsHdvOpen && Listings.Count == 0;
    /// <summary>Résumé de l'en-tête « Mes ventes » : nombre de lots, combien sont à repricer (plus chers que le marché), exposition
    /// totale en kamas. Vide s'il n'y a aucun lot. Recalculé à chaque RaiseLive.</summary>
    public string ListingsSummaryText => Listings.Count == 0 ? "" :
        Loc.Fmt("L.Bots.Hdv.ListingsSummary", Listings.Count, Listings.Count(l => l.IsStatusWarning), N(Listings.Sum(l => l.Price))) +
        Loc.Fmt("L.Bots.Hdv.FloorSummary", Listings.Count(l => l.IsFloorLimited));
    public bool HasSelectedItem => SelectedSellRow is not null;
    public bool HasTaxLog => TaxLog.Count > 0;
    public bool HasSalesLog => SalesLog.Count > 0;
    public bool HasActivity => Activity.Count > 0;
    public string TotalSalesText => TotalSales.ToString("N0", CultureInfo.CurrentUICulture);
    public long SelectedMarket => SelectedSellRow is { } r ? _launch.Hdv.MarketFor(_pid, r.Gid, BidHouseStore.SlotOf(SellLotQty)) : -1;
    public string SelectedMarketText => SelectedMarket >= 0 ? SelectedMarket.ToString("N0", CultureInfo.CurrentUICulture) : Loc.T("L.Bots.Hdv.Unknown");
    public string TotalTaxText => TotalTax.ToString("N0", CultureInfo.CurrentUICulture);
    public bool CanHdv => LicenseService.HasPermission(LicenseService.Perm.Hdv);
    public string WatcherText => WatcherRunning ? Loc.T("L.Bots.Hdv.Stop") : Loc.T("L.Bots.Hdv.Start");

    /// <summary>Tailles de lot possibles (Dofus n'autorise que 1/10/100/1000).</summary>
    private static readonly int[] AllLots = { 1, 10, 100, 1000 };
    private int[] _lotOptions = { 1 };
    /// <summary>Tailles de lot proposées pour la mise en vente : UNIQUEMENT celles &lt;= à la quantité de l'item sélectionné
    /// (ex. 82 en stock -&gt; {1, 10}, pas 100/1000). Évite de proposer un lot qu'on ne peut pas constituer. CACHÉ (référence
    /// STABLE) : on ne notifie que quand l'ENSEMBLE change réellement — sinon un refresh d'inventaire après une vente
    /// remplaçait le tableau à l'identique, ce qui faisait perdre sa sélection au ComboBox (quantité « déselectionnée »).</summary>
    public int[] LotOptions => _lotOptions;

    /// <summary>Recalcule les lots proposés pour l'item courant ; ne notifie/ne re-clamp que si l'ensemble a VRAIMENT changé
    /// (préserve la sélection de quantité après une vente quand la quantité reste dans le même palier).</summary>
    private void RecomputeLotOptions()
    {
        var next = SelectedSellRow is { } r ? _launch.HdvLotSizes(_pid).Where(l => l <= r.MaxStack).DefaultIfEmpty(1).ToArray() : new[] { 1 };
        if (!_lotOptions.SequenceEqual(next))
        {
            _lotOptions = next;
            OnPropertyChanged(nameof(LotOptions));
        }
        if (!_lotOptions.Contains(SellLotQty)) SellLotQty = _lotOptions.Max();
    }
    /// <summary>Vrai si le HDV vendeur est ouvert en jeu (sinon prix/reprice/vente inactifs).</summary>
    public bool IsHdvOpen => _launch.HdvSellerOpen(_pid);

    public HdvViewModel()
    {
        LicenseService.PermissionsChanged += () => Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(CanHdv)));
        _launch.HdvUpdated += pid =>
        {
            // A queued UI callback may cross a PID reuse. Never credit the
            // newer actor with the previous actor's market observations.
            var owner = CaptureHdvUiMutationTicket();
            if (pid == owner.Pid && IsCurrentHdvUiMutationTicket(owner))
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsCurrentHdvUiMutationTicket(owner)) OnHdvUpdated();
                });
        };
        // La banque affichée en jeu arrive via ivx/iwb (HdvUpdated/BankUpdated) -> rafraîchit la liste « mettre en vente ».
        _launch.BankUpdated += pid =>
        {
            var owner = CaptureHdvUiMutationTicket();
            if (pid == owner.Pid && IsCurrentHdvUiMutationTicket(owner))
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsCurrentHdvUiMutationTicket(owner)) QueueRefresh();
                });
        };
        // Vente aboutie (lqn) : crédite le total des gains + journalise + toast.
        _launch.HdvSold += (pid, amount, gid, qty) =>
        {
            var owner = CaptureHdvUiMutationTicket();
            if (pid == owner.Pid && IsCurrentHdvUiMutationTicket(owner))
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsCurrentHdvUiMutationTicket(owner)) OnHdvSold(amount, gid, qty);
                });
        };
        // Ventes faites hors ligne (liste reçue à la connexion, déjà au journal HDV à leur vraie date) : une ligne de journal.
        _launch.HdvOfflineSold += (pid, count, total) =>
        {
            var owner = CaptureHdvUiMutationTicket();
            if (pid == owner.Pid && IsCurrentHdvUiMutationTicket(owner))
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsCurrentHdvUiMutationTicket(owner))
                        Log(Loc.Fmt("L.Bots.Hdv.LogOfflineSoldFmt", count, N(total)), LogOk);
                });
        };
    }

    private void OnHdvUpdated()
    {
        OnPropertyChanged(nameof(IsHdvOpen));
        if (!IsHdvOpen) { _bankStockAttempted = false; BankStockStatus = ""; }
        else if (SellModalVisible && !_bankStockAttempted && !BulkRunning && !WatcherRunning) _ = RefreshBankStockAsync();
        OnPropertyChanged(nameof(RepriceRequiresReview));
        OnPropertyChanged(nameof(SellRequiresReview));
        OnPropertyChanged(nameof(RemoveRequiresReview));
        RaiseBuy();
        if (WatcherRunning && !IsHdvOpen) { StopWatcher(); Log(Loc.T("L.Bots.Hdv.ClosedStop"), LogWarn); Toasts.Show(Loc.T("L.Bots.Hdv.ClosedStop"), success: false); }
        QueueRefresh();
    }

    // Un de mes lots s'est vendu (event lqn) : cumule les gains, journalise, notifie.
    private void OnHdvSold(long amount, long gid, int qty)
    {
        TotalSales += amount;
        string name = _launch.ItemDisplayName(gid);
        string cn = _charName ?? "";
        _ = Task.Run(() => HdvStatsStore.RecordSale(cn, gid, qty, amount, name));   // journal HDV persistant (I/O hors UI)
        SalesLog.Insert(0, new HdvSaleEntry { Name = name, Lot = qty, Amount = amount });
        while (SalesLog.Count > 80) SalesLog.RemoveAt(SalesLog.Count - 1);
        OnPropertyChanged(nameof(HasSalesLog));
        Log(Loc.Fmt("L.Bots.Hdv.LogSold", name, "x" + qty, N(amount)), LogOk);
        _ = RelistAfterSaleAsync(gid, qty);
        Toasts.Show(Loc.Fmt("L.Bots.Hdv.SoldToast", name, qty, amount.ToString("N0", CultureInfo.CurrentUICulture)), success: true);
    }

    partial void OnTotalSalesChanged(long value) => OnPropertyChanged(nameof(TotalSalesText));

    public void AttachPid(uint pid)
    {
        if (pid != _pid)
        {
            Interlocked.Increment(ref _mutationEpoch);
            ResetHdvJournalInspection();
            _bankStockCts?.Cancel(); _bankStockCts = null;
            BankStockLoading = false; BankStockStatus = ""; _bankStockAttempted = false;
        }
        if (pid != _pid) ClearUidDiagnostics();
        if (pid != _pid) { _marketScanCts?.Cancel(); ScanRows.Clear(); ScanStatusText = ""; }
        if (pid != _pid) { _buyCts?.Cancel(); StopWatcher(); CancelBulk(); CancelTravel(); SellModalVisible = false; SellArmed = false; SelectedSellRow = null; _sellSig = ""; }
        bool sessionChanged = _pid != pid;
        _pid = pid;
        if (sessionChanged) ResetPriceHistorySession();
        OnPropertyChanged(nameof(SellRequiresReview));
        OnPropertyChanged(nameof(RemoveRequiresReview));
        OnPropertyChanged(nameof(RepriceRequiresReview));
        RaiseBuy();
        RefreshLive();
    }

    private static Bitmap? IconFor(long gid) => Stores.InteractiveIcons.GetByIcon(Stores.Launch.ItemIconId(gid));

    // ---- Scanner de rentabilité local : prix par lot réellement reçus pour ce PID uniquement ----
    private CancellationTokenSource? _marketScanCts;
    private const int LocalScanMaxTargets = 12;
    private const int LocalScanMaxPriceRequests = 32;

    [RelayCommand]
    private Task RunLocalScanAsync() => ScanLocalMarketAsync(null, false);

    [RelayCommand]
    private Task VerifyLocalScanRowAsync(HdvScanRow? row)
        => row is { Gid: > 0 } ? ScanLocalMarketAsync(row.Gid, true) : Task.CompletedTask;

    private static long? ScanPrice(long[] prices, int lot)
    {
        int slot = BidHouseStore.SlotOf(lot);
        return slot < prices.Length && prices[slot] > 0 ? prices[slot] : null;
    }

    // Une estimation du coût d'achat de quantités réelles : les lots sont indivisibles,
    // donc tout surplus est payé, jamais évalué au prix unitaire fractionné.
    private static long? ScanAcquisitionCost(long[] prices, long quantity)
    {
        if (quantity <= 0) return null;
        long? best = null;
        foreach (int size in AllLots)
        {
            if (ScanPrice(prices, size) is not long price) continue;
            try
            {
                long lots = checked((quantity + size - 1) / size);
                long total = checked(lots * price);
                if (best is null || total < best) best = total;
            }
            catch (OverflowException) { /* Ce devis dépasse long.MaxValue : pas de marge extrapolée. */ }
        }
        return best;
    }

    private static long ScanNet(long gross, decimal taxPercent)
        => checked(gross - (long)Math.Ceiling(gross * taxPercent / 100m));

    // Option « Décomposer » : au plus trois niveaux locaux, sans boucle ni estimation
    // d'une recette dont les ingrédients ne sont pas entièrement connus.
    private static bool ExpandScanIngredients(Services.LotM.GameDb db, long gid, long quantity,
        int remainingDepth, Dictionary<long, long> needed, HashSet<long> path)
    {
        if (gid <= 0 || quantity <= 0 || !path.Add(gid)) return false;
        try
        {
            if (remainingDepth > 0 && db.Recipe(gid) is { Ingredients.Count: > 0 } recipe)
            {
                foreach (var ing in recipe.Ingredients)
                {
                    if (ing.Quantity <= 0 ||
                        !ExpandScanIngredients(db, ing.Gid, checked(quantity * ing.Quantity),
                            remainingDepth - 1, needed, path)) return false;
                }
                return true;
            }
            needed[gid] = checked(needed.GetValueOrDefault(gid) + quantity);
            return needed.Count <= LocalScanMaxPriceRequests;
        }
        catch (OverflowException) { return false; }
        finally { path.Remove(gid); }
    }

    private async Task ScanLocalMarketAsync(long? singleGid, bool verify)
    {
        if (ScanBusy) return;
        uint pid = _pid;
        if (pid == 0 || !CanHdv || !_launch.HdvAnyOpen(pid))
        {
            ScanStatusText = Loc.T("L.Bots.Hdv.LocalScanOpen");
            return;
        }

        ScanBusy = true;
        ScanRows.Clear();
        ScanStatusText = Loc.T("L.Bots.Hdv.LocalScanRunning");
        _marketScanCts?.Dispose();
        using var cts = new CancellationTokenSource();
        _marketScanCts = cts;
        var ct = cts.Token;
        try
        {
            // Un seul serveur : celui de la session connectée. Aucune fausse cote multi-serveurs.
            int serverId = _launch.ServerIdOf(pid);
            if (ScanServers.Count != 1 || ScanServers[0].Id != serverId)
            {
                ScanServers.Clear();
                ScanServers.Add(new HdvScanServer
                {
                    Id = serverId,
                    Label = _launch.ServerNameOf(pid) is { Length: > 0 } n ? n : $"#{serverId}"
                });
            }
            ScanServer = ScanServers[0];

            var tracked = new Dictionary<long, string>();
            void Follow(long gid, string name)
            {
                if (gid > 0 && gid <= int.MaxValue && !tracked.ContainsKey(gid))
                    tracked[gid] = string.IsNullOrWhiteSpace(name) ? _launch.ItemDisplayName(gid) : name;
            }
            foreach (var r in BuyRows) Follow(r.Gid, r.Name);
            foreach (var r in RelistRows) Follow(r.Gid, r.Name);
            foreach (var r in Listings) Follow(r.Gid, r.Name);
            foreach (long gid in _checkedGids) Follow(gid, "");

            string query = ScanQuery.Trim();
            bool numericQuery = long.TryParse(query, out long directGid);
            if (query.Length > 0 && singleGid is null)
            {
                if (numericQuery) Follow(directGid, "");
                else foreach (var (gid, name, _) in _launch.SearchItems(query, LocalScanMaxTargets))
                    Follow(gid, name);
            }
            if (singleGid is long only)
            {
                tracked.Clear();
                Follow(only, "");
            }

            var db = _launch.GameData;  // SQLite local en lecture seule
            if (ScanTypes.Count == 0)
                ScanTypes.Add(new HdvScanFilter { Id = 0, Label = Loc.T("L.Bots.Hdv.LocalScanAllTypes") });
            foreach (int type in tracked.Keys.Select(_launch.ItemTypeId).Where(t => t > 0).Distinct().Order())
                if (ScanTypes.All(t => t.Id != type))
                    ScanTypes.Add(new HdvScanFilter { Id = type, Label = $"Type #{type}" });
            ScanType ??= ScanTypes[0];
            if (ScanJobs.Count == 0)
            {
                ScanJobs.Add(new HdvScanFilter { Id = 0, Label = Loc.T("L.Bots.Hdv.LocalScanAllJobs") });
                if (db is not null)
                    foreach (var job in db.Jobs().Where(j => j.Id > 0 && j.Name.Length > 0))
                        ScanJobs.Add(new HdvScanFilter { Id = job.Id, Label = job.Name });
            }
            ScanJob ??= ScanJobs[0];
            if (ScanModeIndex == 1 && db is null)
            {
                ScanStatusText = Loc.T("L.Bots.Hdv.LocalScanNoDb");
                return;
            }
            if (ScanModeIndex == 0 && ScanJob?.Id > 0)
            {
                ScanStatusText = Loc.T("L.Bots.Hdv.LocalScanJobCraftOnly");
                return;
            }

            var candidates = tracked
                .Where(t => (string.IsNullOrWhiteSpace(query) || singleGid is not null ||
                            (numericQuery && t.Key == directGid) ||
                            t.Value.Contains(query, StringComparison.CurrentCultureIgnoreCase)) &&
                            (ScanType is null || ScanType.Id == 0 || _launch.ItemTypeId(t.Key) == ScanType.Id) &&
                            (ScanModeIndex == 1 ||
                             (_launch.ItemLevel((int)t.Key) >= Math.Min(ScanLevelMin, ScanLevelMax) &&
                              _launch.ItemLevel((int)t.Key) <= Math.Max(ScanLevelMin, ScanLevelMax))))
                .OrderBy(t => t.Key).ToArray();
            if (ScanModeIndex == 1)
                candidates = candidates.Where(t =>
                {
                    var recipe = db!.Recipe(t.Key);
                    return recipe is { Ingredients.Count: > 0 } &&
                           recipe.Level >= Math.Min(ScanLevelMin, ScanLevelMax) &&
                           recipe.Level <= Math.Max(ScanLevelMin, ScanLevelMax) &&
                           (ScanJob is null || ScanJob.Id == 0 || recipe.JobId == ScanJob.Id);
                }).ToArray();

            bool truncated = candidates.Length > LocalScanMaxTargets;
            candidates = candidates.Take(LocalScanMaxTargets).ToArray();
            if (candidates.Length == 0)
            {
                ScanStatusText = Loc.T("L.Bots.Hdv.LocalScanEmpty");
                return;
            }

            int ttlMs = Math.Clamp(PriceTtlMaxSec, 10, 120) * 1000;
            int minDelay = Math.Clamp(PriceProbeMinMs, 800, 10000);
            int maxDelay = Math.Clamp(Math.Max(minDelay, PriceProbeMaxMs), minDelay, 10000);
            decimal tax = Math.Clamp((decimal)_launch.HdvTaxPercent(pid), 0m, 100m);
            int multiplier = Math.Clamp(ScanQty, 1, 1000);
            var quotes = new Dictionary<long, LaunchService.HdvObservedLots>();
            var attempted = new HashSet<long>();
            int unknown = 0;

            async Task<LaunchService.HdvObservedLots?> Observe(long gid)
            {
                if (quotes.TryGetValue(gid, out var cached)) return cached;
                if (!attempted.Add(gid) || attempted.Count > LocalScanMaxPriceRequests) return null;
                if (attempted.Count > 1)
                    await Task.Delay(Random.Shared.Next(minDelay, maxDelay + 1), ct);
                ct.ThrowIfCancellationRequested();
                if (_pid != pid || !_launch.HdvAnyOpen(pid)) throw new OperationCanceledException(ct);
                // Chaque analyse doit relever au moins une cotation fraîche : un cache PID
                // seul ne distingue pas deux ouvertures successives du même HDV.
                var q = await _launch.HdvObserveLotsAsync(pid, gid, ttlMs, ct, forceRefresh: true);
                if (q is { } observed) quotes[gid] = observed;
                else unknown++;
                return q;
            }

            var results = new List<HdvScanRow>();
            foreach (var target in candidates)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                var output = await Observe(target.Key);
                if (output is null) continue;
                if (ScanModeIndex == 0)
                {
                    // Un petit lot observé est regroupé en un grand lot. Ni achat ni vente
                    // n'est réalisé ; l'existence d'une offre ne garantit pas sa vente.
                    foreach (int sellSize in AllLots.Where(s => s > 1))
                    foreach (int buySize in AllLots.Where(s => s < sellSize))
                    {
                        if (ScanPrice(output.Value.Prices, buySize) is not long buy ||
                            ScanPrice(output.Value.Prices, sellSize) is not long sell) continue;
                        long cost = checked(checked(buy * (sellSize / buySize)) * multiplier);
                        long gross = checked(sell * multiplier);
                        long net = ScanNet(gross, tax);
                        if (cost <= 0 || net - cost < ScanMinGain || (net - cost) * 100m / cost < ScanMinPct) continue;
                        results.Add(new HdvScanRow
                        {
                            Gid = target.Key, Name = target.Value, Icon = IconFor(target.Key),
                            Cost = cost, Value = net, Seen = 1, OldestPriceAgeMs = output.Value.AgeMs,
                            Detail = $"Revente {multiplier} × {sellSize} (achat {(sellSize / buySize) * multiplier} × {buySize}, taxe {tax:0.#} %)",
                            Suspect = true // une seule observation des offres, aucune vente garantie
                        });
                    }
                }
                else if (db!.Recipe(target.Key) is { Ingredients.Count: > 0 } recipe)
                {
                    var needed = new Dictionary<long, long>();
                    var stack = new HashSet<long> { target.Key };
                    if (recipe.Ingredients.Any(i => i.Quantity <= 0 ||
                        !ExpandScanIngredients(db, i.Gid, checked((long)i.Quantity * multiplier),
                            ScanDecompose ? 3 : 0, needed, stack))) continue;
                    long cost = 0;
                    long maxAge = output.Value.AgeMs;
                    bool complete = needed.Count > 0;
                    foreach (var ingredient in needed)
                    {
                        var observed = await Observe(ingredient.Key);
                        var ingredientCost = observed is { } o ? ScanAcquisitionCost(o.Prices, ingredient.Value) : null;
                        if (ingredientCost is null) { complete = false; break; }
                        cost = checked(cost + ingredientCost.Value);
                        maxAge = Math.Max(maxAge, observed!.Value.AgeMs);
                    }
                    if (!complete || cost <= 0) continue;
                    // Les unités issues d'un craft ne se fractionnent pas en lots de vente.
                    long net = -1; int sellLot = 0;
                    foreach (int size in AllLots.Where(s => multiplier % s == 0))
                    {
                        if (ScanPrice(output.Value.Prices, size) is not long lotPrice) continue;
                        long value = ScanNet(checked(lotPrice * (multiplier / size)), tax);
                        if (value > net) { net = value; sellLot = size; }
                    }
                    if (net < 0 || net - cost < ScanMinGain || (net - cost) * 100m / cost < ScanMinPct) continue;
                    results.Add(new HdvScanRow
                    {
                        Gid = target.Key, Name = target.Value, Icon = IconFor(target.Key),
                        Cost = cost, Value = net, Seen = needed.Count + 1, OldestPriceAgeMs = maxAge,
                        Detail = $"Craft ×{multiplier} ({recipe.Job}, {needed.Count} ingrédients, vente par {sellLot}, taxe {tax:0.#} %)",
                        Suspect = true // estimation sur prix affichés, sans transactions exécutées
                    });
                }
                }
                catch (OverflowException) { /* Montant/quantité excessive : ignorer ce résultat, continuer les autres. */ }
            }

            IEnumerable<HdvScanRow> sorted = ScanSortIndex switch
            {
                1 => results.OrderByDescending(r => (decimal)r.Gain / Math.Max(1, r.Cost)),
                2 => results.OrderBy(r => r.Cost),
                3 => results.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
                _ => results.OrderByDescending(r => r.Gain)
            };
            foreach (var row in sorted.Take(40)) ScanRows.Add(row);
            ScanStatusText = Loc.Fmt("L.Bots.Hdv.LocalScanDone", ScanRows.Count, attempted.Count, unknown) +
                             (truncated ? " " + Loc.T("L.Bots.Hdv.LocalScanLimit") : "");
        }
        catch (OperationCanceledException) { ScanStatusText = Loc.T("L.Bots.Hdv.LocalScanCancelled"); }
        catch (Exception ex) { ScanStatusText = Loc.Fmt("L.Bots.Hdv.LocalScanError", ex.Message); }
        finally
        {
            if (ReferenceEquals(_marketScanCts, cts)) _marketScanCts = null;
            ScanBusy = false;
        }
    }

    // ---------------- Rafraîchissement de l'affichage (coalescé + maj en place = pas de saccade) ----------------
    private bool _refreshQueued;
    private string _listSig = "";

    // Coalesce les rafraîchissements : une rafale d'events prix (kcq) -> UN seul refresh (évite les saccades).
    private void QueueRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Avalonia.Threading.DispatcherTimer.RunOnce(() => { _refreshQueued = false; RefreshLive(); }, TimeSpan.FromMilliseconds(160));
    }

    private void RefreshLive()
    {
        RefreshBuyLive();
        if (_pid == 0)
        {
            if (Listings.Count > 0) Listings.Clear();
            RefreshSellRows();
            _listSig = "";
            RaiseLive();
            return;
        }
        RefreshListings();
        RefreshListingDecisions();
        if (SellModalVisible) RefreshSellRows();   // fenêtre fermée : rien à reconstruire (le résumé de l'onglet n'en dépend pas)
        RaiseLive();
    }

    // Lots en vente : maj EN PLACE (prix marché + erreur) si la structure (uid/gid/qty) est identique -> AUCUN rebuild ni
    // rechargement d'icônes -> pas de saccade au rafraîchissement des prix. Rebuild complet seulement si un lot change.
    private void RefreshListings()
    {
        // HDV vendeur FERMÉ -> aucune « vente en cours » valide à montrer : on VIDE la liste (l'UI bascule sur l'attente,
        // façon onglet Banque). Sans ça les anciens lots restaient affichés, périmés, après la fermeture du HDV.
        if (!IsHdvOpen)
        {
            if (Listings.Count > 0) { Listings.Clear(); ListingsView.Clear(); }
            _listSig = "";
            return;
        }
        var myList = _launch.Hdv.MyListings(_pid);
        string sig = string.Join("|", myList.Select(l => l.Uid + ":" + l.Gid + ":" + l.Qty));
        if (sig == _listSig && Listings.Count == myList.Count)
        {
            for (int i = 0; i < myList.Count; i++)
            {
                var l = myList[i]; var row = Listings[i];
                row.Price = l.Price;
                row.Market = _launch.Hdv.MarketFor(_pid, l.Gid, row.Slot);
                row.FetchError = HasFetchError(l.Gid);
                row.IsFetching = _fetchingGids.Contains(l.Gid);   // ne pas perdre le spinner si un refresh est en cours
            }
            return;
        }
        _listSig = sig;
        Listings.Clear();
        foreach (var l in myList)
        {
            int slot = BidHouseStore.SlotOf(l.Qty);
            Listings.Add(new HdvListingRow
            {
                TooltipPid = (int)_pid, Uid = l.Uid, Gid = l.Gid, Qty = l.Qty, Slot = slot, Name = _launch.ItemDisplayName(l.Gid), Icon = IconFor(l.Gid),
                Price = l.Price, Market = _launch.Hdv.MarketFor(_pid, l.Gid, slot),
                Ignored = _ignored.Contains(l.Gid), FetchError = HasFetchError(l.Gid),
                IsFetching = _fetchingGids.Contains(l.Gid), IgnoreToggled = OnRowIgnoreToggled,
            });
        }
        RebuildListingsView();
    }

    // Reconstruit la vue filtrée/triée depuis Listings (mêmes instances -> les prix continuent de se mettre à jour en place).
    // Appelée sur rebuild structurel + changement de recherche/tri (PAS à chaque tick -> pas de reset de scroll pendant le reprice).
    private void RebuildListingsView()
    {
        System.Collections.Generic.IEnumerable<HdvListingRow> q = Listings;
        var f = (ListingsFilter ?? "").Trim();
        if (f.Length > 0) q = q.Where(l => l.Name.Contains(f, StringComparison.OrdinalIgnoreCase));
        q = ListingsSortIndex switch
        {
            1 => q.OrderByDescending(l => l.IsStatusWarning).ThenBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase),   // à repricer d'abord
            2 => q.OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase),                                            // nom A→Z
            3 => q.OrderByDescending(l => l.Market > 0 ? l.Price - l.Market : long.MinValue),                               // plus cher que le marché d'abord
            _ => q,                                                                                                          // défaut (ordre source)
        };
        var list = q.ToList();
        ListingsView.Clear();
        foreach (var l in list) ListingsView.Add(l);
    }

    private void OnRowIgnoreToggled(HdvListingRow row)
    {
        if (row.Ignored) _ignored.Add(row.Gid); else _ignored.Remove(row.Gid);
        if (_loading) return;
        _cfg.IgnoreGids = _ignored.ToList();
        Save();
        RefreshListingDecisions();
        RaiseLive();
    }

    private void RefreshListingDecisions()
    {
        foreach (var row in Listings)
        {
            row.MinimumPrice = Math.Max(0, SanityFloor(row.Gid, row.Qty));
            row.SuggestedPrice = ComputeTarget(row);
        }
    }

    private void RaiseLive()
    {
        OnPropertyChanged(nameof(HasListings)); OnPropertyChanged(nameof(HasTaxLog));
        OnPropertyChanged(nameof(ShowEmptyListings)); OnPropertyChanged(nameof(ListingsSummaryText));
        OnPropertyChanged(nameof(IsHdvOpen)); OnPropertyChanged(nameof(FreeSlotsText));
        RaiseSelectedMarket();
    }
    private void RaiseSelectedMarket() { OnPropertyChanged(nameof(SelectedMarket)); OnPropertyChanged(nameof(SelectedMarketText)); }

    // ---------------- Journal d'activité (verbeux + horodaté) ----------------
    private static readonly IBrush LogOk = new SolidColorBrush(Color.Parse("#63D3A6"));
    private static readonly IBrush LogWarn = new SolidColorBrush(Color.Parse("#F2C14E"));
    private static readonly IBrush LogErr = new SolidColorBrush(Color.Parse("#F0616D"));
    private static readonly IBrush LogInfo = new SolidColorBrush(Color.Parse("#9AA6C0"));

    /// <summary>Ajoute une ligne HORODATÉE au journal d'activité ET la reflète dans le statut (une seule source pour « ce que fait
    /// le bot »). Réservé aux ACTIONS discrètes (reprix/vente/retrait/refresh terminé) ; la progression transitoire reste dans
    /// StatusText direct pour ne pas noyer le journal. Thread-safe (marshalle sur le thread UI).</summary>
    private void Log(string text, IBrush? color = null)
    {
        void Add()
        {
            StatusText = text;
            Activity.Insert(0, new HdvLogEntry { Text = text, Color = color ?? LogInfo });
            while (Activity.Count > 200) Activity.RemoveAt(Activity.Count - 1);
            OnPropertyChanged(nameof(HasActivity));
        }
        if (Dispatcher.UIThread.CheckAccess()) Add(); else Dispatcher.UIThread.Post(Add);
    }

    /// <summary>Délai RANDOMISÉ entre deux bornes (tirage uniforme, humanisation). Max &lt;= Min -> valeur fixe = Min.</summary>
    private static int RandDelay(int min, int max) => max > min ? System.Random.Shared.Next(min, max + 1) : min;

    private static string N(long v) => v.ToString("N0", CultureInfo.CurrentUICulture);

    /// <summary>Message d'ÉCHEC unifié : statut + toast rouge (dédoublonne ~11 sites identiques). Distinct de Log() (journal d'activité).</summary>
    private void Warn(string key) { StatusText = Loc.T(key); Toasts.Show(Loc.T(key), success: false); }

    // ---------------- Sérialisation & humanisation ----------------
    // Sérialise les MUTATIONS HDV (reprice/vente/retrait) : jamais deux en vol -> pas de 2e requête sur un uid périmé
    // (l'uid change à chaque reprice via ken+kes) ni de taxe fantôme (l'attente par CODE validait la mauvaise réponse).
    private readonly SemaphoreSlim _engineGate = new(1, 1);
    // One conservative budget for manual, bulk and watcher mutations of this
    // view-model. No fee is ever released on an ambiguous kamas delta/UID ACK.
    private readonly HdvTaxReservations _taxReservations = new(
        System.IO.Path.Combine(MZP19LocalClone.DataRoot, "hdv", "tax-reservations-v1.journal"));

    private enum TaxReservationDecision { Allowed, CapExceeded, UnknownRate, AlreadyReserved, Unavailable, NeedsReconciliation }

    /// <summary>
    /// Called INSIDE Gated(), for all capped sale/reprice sends. The journal
    /// persists intent and attempt BEFORE the network callback and the
    /// attributable listing result afterwards; no tax is ever released.
    /// </summary>
    private async Task<(TaxReservationDecision Decision, bool Ok)> ExecuteTaxedMutationAsync(
        uint pid, long price, string orderKey, Func<Task<bool>> send)
    {
        if (MaxTaxSpend > 0 && HdvTaxBudget.EstimateUpperBound(price, _launch.HdvTaxPercent(pid)) is null)
            return (TaxReservationDecision.UnknownRate, false);
        var result = await HdvTaxedMutationFlow.ExecuteAsync(_taxReservations, pid,
            _hdvProfileKey, orderKey, MaxTaxSpend, TotalTax, price,
            _launch.HdvTaxPercent(pid), _ => send());
        return (result.Decision switch
        {
            HdvTaxOrderReservation.Reserved => TaxReservationDecision.Allowed,
            HdvTaxOrderReservation.AlreadyReserved => TaxReservationDecision.AlreadyReserved,
            HdvTaxOrderReservation.CapExceeded => TaxReservationDecision.CapExceeded,
            HdvTaxOrderReservation.NeedsReconciliation => TaxReservationDecision.NeedsReconciliation,
            _ => TaxReservationDecision.Unavailable
        }, result.ServerResultObserved);
    }

    private static string TaxReservationReason(TaxReservationDecision decision)
        => decision == TaxReservationDecision.UnknownRate
            ? "Taxe HDV non vérifiable : opération plafonnée refusée avant envoi."
            : decision == TaxReservationDecision.AlreadyReserved
                ? "Ordre HDV déjà réservé : vérifier son résultat et l'UID avant toute nouvelle tentative."
                : decision == TaxReservationDecision.NeedsReconciliation
                    ? "Opération HDV précédente non résolue : nouvelles mutations plafonnées bloquées jusqu'au rapprochement."
                : decision == TaxReservationDecision.Unavailable
                    ? "Journal de réservations HDV indisponible ou invalide : émission bloquée, rapprochement requis."
                    : Loc.T("L.Bots.Hdv.TaxCapHit");

    // The current stock count is part of a sale attempt's stable identity.
    // A retry with the same UID/quantity/price/stock after crash is blocked;
    // a truly new lot requires independently observed stock change.
    private static string SellTaxOrder(int uid, int qty, long price, int stock)
        => HdvTaxOrderKeys.Sell(uid, qty, price, stock);
    private static string RepriceTaxOrder(int uid, int qty, long price)
        => HdvTaxOrderKeys.Reprice(uid, qty, price);

    /// <summary>Délai (ms) laissé au KamasUpdateEvent avant de relever la taxe. Borné à 60 ms : la mesure du
    /// 07/11 donne une confirmation serveur à 22 ms de médiane (84 ms p95), et les 300 ms précédents coûtaient
    /// 60 s sur 200 lots sans qu'aucun réglage ne puisse les atteindre.</summary>
    internal const int TaxSettleDelayMs = 60;
    private async Task<bool> Gated(Func<Task<bool>> op, CancellationToken ct = default)
    {
        uint pid = _pid;
        string? profile = _hdvProfileKey;
        int epoch = Volatile.Read(ref _mutationEpoch);
        await _engineGate.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            if (pid == 0 || _pid != pid || epoch != Volatile.Read(ref _mutationEpoch)
                || !string.Equals(profile, _hdvProfileKey, StringComparison.Ordinal)) return false;
            return await op();
        }
        finally { _engineGate.Release(); }
    }
    private static List<T> Shuffle<T>(List<T> list)   // Fisher-Yates : casse l'empreinte « ordre déterministe » de l'automate
    {
        for (int i = list.Count - 1; i > 0; i--) { int j = System.Random.Shared.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
        return list;
    }

    /// <summary>Actions manuelles (reprice/vente/retrait par lot, refresh) autorisées seulement HORS watcher : sinon on agirait
    /// sur un uid que le watcher vient de changer (ken+kes) -> rejet serveur. Le watcher fait déjà le reprice.</summary>
    public bool CanManual => !WatcherRunning && !BulkRunning;
    partial void OnWatcherRunningChanged(bool v) { OnPropertyChanged(nameof(CanManual)); OnPropertyChanged(nameof(CanBulk)); OnPropertyChanged(nameof(RelistNeedsWatcher)); }

    // Changer la taille de lot RE-CALCULE le prix sur le marché de CE stack (force) : sinon on laissait le prix du lot
    // précédent (typiquement x1) sur un lot x100 -> mise en vente très sous-évaluée. Le marché est déjà par-lot.
    partial void OnSellLotQtyChanged(int value) { SellArmed = false; RaiseSelectedMarket(); ApplyAutoPrice(force: true); }
    partial void OnSellUnitPriceChanged(long value) => SellArmed = false;
    partial void OnSelectedSellRowChanged(HdvSellRow? value)
    {
        SellArmed = false;
        SellUnitPrice = 0;   // jamais le prix de l'objet précédent : re-rempli à l'arrivée du prix de CET objet
        OnPropertyChanged(nameof(HasSelectedItem));
        RecomputeLotOptions();   // ne re-notifie/re-clamp que si l'ensemble change -> garde la quantité sélectionnée après une vente
        RaiseSelectedMarket();
        // Pas pendant une mise en vente en masse : une seule requête de prix à la fois par bot (un seul « attendu » côté moteur).
        if (!_reselecting && !BulkRunning && value is not null && _pid != 0 && IsHdvOpen) _ = FetchPriceAsync(value.Gid);
    }
    partial void OnTotalTaxChanged(long value) => OnPropertyChanged(nameof(TotalTaxText));

    private async Task FetchPriceAsync(long gid)
    {
        await _launch.HdvGetMarketPriceAsync(_pid, gid);
        MarkFetch(gid);
        Dispatcher.UIThread.Post(() => { RefreshLive(); PrefillSellPrice(); });
    }

    /// <summary>Cale « Prix du lot » sur le prix marché du STACK CHOISI (x1/x10/x100/x1000) − sous-cote (PriceStep), en kamas
    /// ENTIERS. <paramref name="force"/> = true (changement de lot ou d'item) écrase la valeur courante pour ne JAMAIS laisser
    /// le prix d'un AUTRE lot (ex prix x1 conservé sur un lot x100) ; false (pré-remplissage sur récup. de prix) respecte une
    /// saisie déjà présente. Sans marché connu pour ce stack (event pas encore reçu) on ne touche à rien. Le PriceStep
    /// (= UndercutKamas, « Sous-coter de », défaut 1) fournit le « −1 » : jamais codé en dur.</summary>
    private void ApplyAutoPrice(bool force)
    {
        long m = SelectedMarket;                    // = MarketFor(gid, SlotOf(SellLotQty)) : marché MIN du LOT choisi
        if (m <= 0) return;                         // stack pas encore price -> laisser le champ tel quel
        if (!force && SellUnitPrice > 0) return;    // pré-remplissage : ne pas écraser une saisie manuelle
        long p = Math.Max(1, m - Math.Max(1, PriceStep));
        if (SelectedSellRow is { } row)
        {
            long sanity = SanityFloor(row.Gid, SellLotQty);
            if (sanity <= 0) { SellUnitPrice = 0; SellArmed = false; return; }
            p = Math.Max(p, sanity);
        }
        SellUnitPrice = p;
    }

    /// <summary>Pré-remplit « Prix du lot » sur récupération du marché (sélection d'un item) sans écraser une saisie.</summary>
    private void PrefillSellPrice() => ApplyAutoPrice(force: false);

    // Un échec de récupération n'est plus affiché dès que le marché de ce gid est connu (event prix arrivé après le délai
    // d'attente, ou récupéré par un autre chemin : reprice auto, Lua) : sinon le badge rouge restait collé à côté du prix.
    private bool HasFetchError(long gid) => _fetchErrors.Contains(gid) && _launch.Hdv.MarketPricesFor(_pid, gid).Length == 0;

    private void MarkFetch(long gid)
    {
        if (_launch.Hdv.MarketPricesFor(_pid, gid).Length == 0) _fetchErrors.Add(gid); else _fetchErrors.Remove(gid);
    }

    // Ensemble AUTORITAIRE des gids dont le prix est en cours de récupération -> le spinner de la colonne État. Tenu à part
    // des lignes pour SURVIVRE à un rebuild de Listings (RefreshListings réapplique cet état à chaque ligne reconstruite).
    private readonly HashSet<long> _fetchingGids = new();

    // Spinner par-item pendant le refresh : marque TOUTES les lignes de ce gid (plusieurs lots peuvent partager un gid).
    private void SetFetching(long gid, bool value)
    {
        if (value) _fetchingGids.Add(gid); else _fetchingGids.Remove(gid);
        foreach (var row in Listings) if (row.Gid == gid) row.IsFetching = value;
    }

    // Éteint tous les spinners (fin/annulation d'un refresh) -> pas de loader coincé si la boucle s'arrête en cours d'item.
    private void ClearAllFetching()
    {
        if (_fetchingGids.Count == 0) return;
        _fetchingGids.Clear();
        foreach (var row in Listings) if (row.IsFetching) row.IsFetching = false;
    }

    // ---------------- Prix marché (lecture seule, throttlé) ----------------
    [RelayCommand] private void Refresh() => _ = RefreshAllPricesAsync();

    private async Task RefreshAllPricesAsync()
    {
        if (_pid == 0 || Busy) return;
        if (!_launch.HdvAnyOpen(_pid)) { Warn("L.Bots.Hdv.OpenFirst"); return; }
        Busy = true;
        try
        {
            // Ne rafraîchir QUE les gids de MES lots en vente (les « stacks »), pas tout le sac/banque : concaténer Items
            // faisait exploser le nombre de requêtes prix (ex. 35 lots + ~1000 objets vendables = ~1040 paquets). Le prix
            // d'un objet à vendre est déjà récupéré à la sélection (OnSelectedItemRowChanged -> FetchPriceAsync).
            var gids = Listings.Where(l => !l.Ignored).Select(l => l.Gid).Distinct().ToList();   // ne pas sonder les lots ignores (exclus du reprice auto)
            for (int i = 0; i < gids.Count; i++)
            {
                StatusText = Loc.Fmt("L.Bots.Hdv.FetchingProgress", i + 1, gids.Count);
                SetFetching(gids[i], true);
                await _launch.HdvGetMarketPriceAsync(_pid, gids[i]);
                MarkFetch(gids[i]);
                await Task.Delay(RandDelay(150, 450));            // garde le spinner visible + jitter (cadence non régulière)
                SetFetching(gids[i], false);
            }
            Dispatcher.UIThread.Post(RefreshLive);
            Log(Loc.Fmt("L.Bots.Hdv.LogPricesRefreshed", gids.Count), LogInfo);
        }
        finally { ClearAllFetching(); Busy = false; }
    }

    // ---------------- Actions manuelles ----------------
    // Vente unitaire DANS la fenêtre « Mettre en vente » : 1er clic = arme (le bouton affiche « objet x lot à prix — confirmer »),
    // 2e clic dans les 6 s = vend. Pas de boîte de confirmation : elle s'ouvrirait SOUS la fenêtre.
    [ObservableProperty] public partial bool SellArmed { get; set; }
    private long _armTick;
    private HdvUiMutationTicket _sellArmOwner;
    private HdvSellRow? _sellArmRow;
    private long _sellArmGid;
    private int _sellArmQuantity;
    private long _sellArmPrice;
    public string SellArmText => SelectedSellRow is { } r
        ? Loc.Fmt("L.Bots.Hdv.Sell.ConfirmOneFmt", r.Name, SellLotQty, SellUnitPrice.ToString("N0", CultureInfo.CurrentUICulture)) : "";
    partial void OnSellArmedChanged(bool value) => OnPropertyChanged(nameof(SellArmText));

    [RelayCommand]
    private void SellSelected()
    {
        if (BulkRunning) return;
        if (_pid == 0 || SelectedSellRow is not { } r) { Warn("L.Bots.Hdv.PickItem"); return; }
        var ticket = CaptureHdvUiMutationTicket();
        if (!IsCurrentHdvUiMutationTicket(ticket))
        {
            SellArmed = false;
            StatusText = "Vente refusée : serveur/personnage non attesté.";
            return;
        }
        if (SellUnitPrice <= 0 || SellLotQty <= 0) { Warn("L.Bots.Hdv.BadPrice"); return; }
        if (r.MaxStack < SellLotQty) { Warn("L.Bots.Hdv.NotEnough"); return; }
        if (SanityFloor(r.Gid, SellLotQty) <= 0) { Warn("L.Bots.Hdv.BadPrice"); return; }
        if (MaxTaxSpend > 0 && TotalTax >= MaxTaxSpend) { Warn("L.Bots.Hdv.TaxCapHit"); return; }
        if (!SellArmed || Environment.TickCount64 - _armTick > 6000 ||
            _sellArmOwner != ticket || !ReferenceEquals(_sellArmRow, r) ||
            _sellArmGid != r.Gid || _sellArmQuantity != SellLotQty ||
            _sellArmPrice != SellUnitPrice)
        {
            _armTick = Environment.TickCount64;
            _sellArmOwner = ticket;
            _sellArmRow = r;
            _sellArmGid = r.Gid;
            _sellArmQuantity = SellLotQty;
            _sellArmPrice = SellUnitPrice;
            SellArmed = true;
            OnPropertyChanged(nameof(SellArmText));
            return;
        }
        // 2e clic d'un DOUBLE-clic (le bouton « Confirmer » apparaît sous le curseur) : on reste armé, on ne vend pas.
        if (Environment.TickCount64 - _armTick < 700) return;
        SellArmed = false;
        _ = SellSelectedCore(r, SellLotQty, SellUnitPrice, ticket);
    }

    private async Task SellSelectedCore(
        HdvSellRow r, int qty, long price, HdvUiMutationTicket ticket)
    {
        if (BulkRunning || !IsCurrentHdvUiMutationTicket(ticket)) return;
        uint pid = _pid;
        string? charName = _charName;
        int epoch = Volatile.Read(ref _mutationEpoch);
        if (!IsHdvOpen && !await EnsureSellerModeAsync()) return;
        if (!IsCurrentHdvUiMutationTicket(ticket) ||
            _pid != pid || epoch != Volatile.Read(ref _mutationEpoch)) return;
        if (SanityFloor(r.Gid, qty) <= 0) { Warn("L.Bots.Hdv.BadPrice"); return; }
        // Pile la plus grande qui contient le lot (sac ou banque), relue au moment de vendre.
        var stack = _launch.HdvMergedItems(pid).Where(x => x.Item.Gid == r.Gid && x.Item.Qty >= qty && (x.InBank || x.Item.Position == 63) && _launch.ItemCanTrade(pid, x.Item.Uid))
                           .OrderByDescending(x => x.Item.Qty).Select(x => (int?)x.Item.Uid).FirstOrDefault();
        if (stack is not int uid) { Warn("L.Bots.Hdv.NotEnough"); return; }
        StatusText = Loc.T("L.Bots.Hdv.Selling");
        long before = _launch.Kamas(pid);
        _launch.MarkHdvManual(pid);   // HDV Tracker : clic unitaire = action manuelle
        _launch.HdvResetSellError(pid);
        var taxDecision = TaxReservationDecision.Allowed;
        bool ok = await Gated(async () =>
        {
            if (!IsCurrentHdvUiMutationTicket(ticket) ||
                _pid != pid || epoch != Volatile.Read(ref _mutationEpoch)) return false;
            int stock = _launch.HdvSellableQuantity(pid, uid);
            if (stock < qty || !_launch.HdvMergedItems(pid).Any(x =>
                    x.Item.Uid == uid && x.Item.Gid == r.Gid &&
                    x.Item.Position == 63 && x.Item.Qty >= qty &&
                    _launch.ItemCanTrade(pid, uid)) ||
                _launch.HdvSalePriceRefusal(pid, r.Gid, qty, price) is not null)
                return false;
            var outcome = await ExecuteTaxedMutationAsync(pid, price,
                SellTaxOrder(uid, qty, price, stock),
                () => _launch.HdvSellAsync(pid, uid, qty, price));
            taxDecision = outcome.Decision;
            return outcome.Ok;
        });
        if (taxDecision != TaxReservationDecision.Allowed)
        {
            StatusText = TaxReservationReason(taxDecision);
            Log(StatusText, LogWarn);
            return;
        }
        if (_pid != pid || epoch != Volatile.Read(ref _mutationEpoch)) return;
        string fail = _launch.HdvLastSellError(pid) is { } why ? Loc.Fmt("L.Bots.Hdv.SellFailFmt", why) : Loc.T("L.Bots.Hdv.SellFail");
        OnPropertyChanged(nameof(SellRequiresReview));
        StatusText = ok ? Loc.T("L.Bots.Hdv.SellOk") : fail;
        if (ok) Log(Loc.Fmt("L.Bots.Hdv.LogListed", r.Name, "x" + qty, N(price)), LogOk); else Log(fail, LogErr);
        Toasts.Show(ok ? Loc.Fmt("L.Bots.Hdv.SellToast", r.Name, qty) : fail, success: ok);
        if (ok) await RecordTaxAsync(pid, charName, before, r.Gid, r.Name, qty, Loc.T("L.Bots.Hdv.ActSell"));
        OnPropertyChanged(nameof(FreeSlotsText));
    }

    public bool SellRequiresReview => _pid != 0 &&
        _launch.HdvSellLastDisposition(_pid) == HdvSellDisposition.NeedsReview;

    /// <summary>
    /// A confirmation dialog can outlive the character that opened it, even
    /// when Windows reuses the PID and the next character has the same name.
    /// Never reconcile a different server/character than the one inspected.
    /// This is a UI identity fence, not a source-authenticated game ACK.
    /// </summary>
    private bool SameHdvReviewIdentity(uint pid, int server, long character)
        => pid > 0 && server > 0 && character > 0 &&
           _pid == pid && _launch.ServerIdOf(pid) == server &&
           _launch.CharacterId(pid) == character;

    [RelayCommand]
    private void ReconcileSell()
    {
        uint pid = _pid;
        if (pid == 0 || !SellRequiresReview) return;
        int server = _launch.ServerIdOf(pid);
        long character = _launch.CharacterId(pid);
        if (!SameHdvReviewIdentity(pid, server, character)) return;
        Dialogs.Confirm(Loc.T("L.Bots.Hdv.ReconcileTitle"), Loc.T("L.Bots.Hdv.ReconcileBody"),
            onConfirm: () =>
            {
                if (!SameHdvReviewIdentity(pid, server, character) ||
                    !_launch.HdvSellManuallyReconciled(pid)) return;
                StatusText = Loc.T("L.Bots.Hdv.ReconcileDone");
                OnPropertyChanged(nameof(SellRequiresReview));
                NoteHdvManualReview("vente");
            }, danger: true);
    }

    [RelayCommand]
    private void RemoveListing(HdvListingRow? row)
    {
        if (_pid == 0 || row is null) return;
        if (!IsHdvOpen) { Warn("L.Bots.Hdv.OpenFirst"); return; }
        var ticket = CaptureHdvUiMutationTicket();
        if (!IsCurrentHdvUiMutationTicket(ticket)) return;
        // Immutable offer/listing snapshot from the FIRST user click. A row
        // can be rebound, edited or refreshed before the dialog closes.
        var intent = new HdvListingIntent(row.Uid, row.Gid, row.Qty, row.Price);
        if (!ValidHdvListingIntent(ticket, intent)) return;
        Dialogs.Confirm(Loc.T("L.Bots.Hdv.RemoveTitle"), Loc.Fmt("L.Bots.Hdv.RemoveBody", row.Name),
            onConfirm: () =>
            {
                // Capturing PID only when the modal closes is unsafe: another
                // character can now occupy the same PID. Require the original
                // selected lot to still belong to the original profile.
                if (IsCurrentHdvUiMutationTicket(ticket) &&
                    row.Uid == intent.Uid && row.Gid == intent.Gid &&
                    row.Qty == intent.Quantity && row.Price == intent.Price)
                    _ = RemoveListingCore(row, intent, ticket);
            }, danger: true);
    }

    private readonly record struct HdvListingIntent(
        int Uid, long Gid, int Quantity, long Price);

    private bool ValidHdvListingIntent(
        HdvUiMutationTicket ticket, HdvListingIntent intent)
        => IsCurrentHdvUiMutationTicket(ticket) &&
           intent.Uid > 0 && intent.Gid > 0 && intent.Quantity > 0 &&
           intent.Price > 0 && _launch.Hdv.MyListings(ticket.Pid).Any(x =>
               x.Uid == intent.Uid && x.Gid == intent.Gid &&
               x.Qty == intent.Quantity && x.Price == intent.Price);

    private async Task RemoveListingCore(
        HdvListingRow row, HdvListingIntent intent, HdvUiMutationTicket ticket)
    {
        if (!ValidHdvListingIntent(ticket, intent)) return;
        uint pid = ticket.Pid;
        StatusText = Loc.T("L.Bots.Hdv.Removing");
        _launch.MarkHdvManual(pid);   // HDV Tracker : clic unitaire = action manuelle
        bool ok = await Gated(() =>
            ValidHdvListingIntent(ticket, intent)
                ? _launch.HdvRemoveAsync(pid, intent.Uid, intent.Quantity)
                : Task.FromResult(false));
        if (!IsCurrentHdvUiMutationTicket(ticket)) return;
        OnPropertyChanged(nameof(RemoveRequiresReview));
        if (ok) Log(Loc.Fmt("L.Bots.Hdv.LogRemoved", row.Name, "x" + row.Qty), LogWarn); else Log(Loc.T("L.Bots.Hdv.RemoveFail"), LogErr);
        Toasts.Show(ok ? Loc.T("L.Bots.Hdv.RemoveOk") : Loc.T("L.Bots.Hdv.RemoveFail"), success: ok);
    }

    public bool RemoveRequiresReview => _pid != 0 &&
        _launch.HdvRemoveLastDisposition(_pid) == HdvSellDisposition.NeedsReview;

    public bool RepriceRequiresReview => _pid != 0 && _launch.HdvRepriceReviewPending(_pid);

    [RelayCommand]
    private void ReconcileReprice()
    {
        uint pid = _pid;
        if (pid == 0 || !RepriceRequiresReview) return;
        int server = _launch.ServerIdOf(pid);
        long character = _launch.CharacterId(pid);
        if (!SameHdvReviewIdentity(pid, server, character)) return;
        Dialogs.Confirm("Repricing HDV à vérifier",
            "La réponse du serveur ne permet pas de confirmer ce changement de prix. Vérifie l'annonce et son nouveau prix dans le jeu, puis actualise la liste avant d'autoriser un autre repricing.",
            onConfirm: () =>
            {
                if (!SameHdvReviewIdentity(pid, server, character) ||
                    !_launch.HdvAcknowledgeRepriceReview(pid)) return;
                StatusText = "Repricing rapproché manuellement. Aucune confirmation réseau n'a été déduite.";
                OnPropertyChanged(nameof(RepriceRequiresReview));
                NoteHdvManualReview("repricing");
            }, danger: true);
    }

    [RelayCommand]
    private void ReconcileRemove()
    {
        uint pid = _pid;
        if (pid == 0 || !RemoveRequiresReview) return;
        int server = _launch.ServerIdOf(pid);
        long character = _launch.CharacterId(pid);
        if (!SameHdvReviewIdentity(pid, server, character)) return;
        Dialogs.Confirm("Retrait HDV à vérifier",
            "Vérifie dans le jeu si le lot est toujours en vente, puis actualise les annonces. Confirmer que le retrait incertain a été rapproché manuellement ?",
            onConfirm: () =>
            {
                if (!SameHdvReviewIdentity(pid, server, character) ||
                    !_launch.HdvAcknowledgeRemoveReview(pid)) return;
                StatusText = "Retrait vérifié par l'opérateur ; les nouvelles opérations sont autorisées.";
                OnPropertyChanged(nameof(RemoveRequiresReview));
                NoteHdvManualReview("retrait");
            }, danger: true);
    }

    [RelayCommand]
    private async Task RepriceListing(HdvListingRow? row)
    {
        if (_pid == 0 || row is null || row.Price <= 0) return;
        if (!IsHdvOpen) { Warn("L.Bots.Hdv.OpenFirst"); return; }
        uint pid = _pid;
        var ticket = CaptureHdvUiMutationTicket();
        if (!IsCurrentHdvUiMutationTicket(ticket)) return;
        long targetPrice = row.Price;
        string? charName = _charName;
        StatusText = Loc.T("L.Bots.Hdv.Repricing");
        if (_launch.HdvSalePriceRefusal(pid, row.Gid, row.Qty, targetPrice) is { } refusal)
        { StatusText = refusal; Log(refusal, LogErr); return; }
        long before = _launch.Kamas(pid);
        _launch.MarkHdvManual(pid);   // HDV Tracker : clic unitaire = action manuelle
        var taxDecision = TaxReservationDecision.Allowed;
        int epoch = Volatile.Read(ref _mutationEpoch);
        bool ok = await Gated(async () =>
        {
            if (!IsCurrentHdvUiMutationTicket(ticket) ||
                _pid != pid || epoch != Volatile.Read(ref _mutationEpoch) ||
                row.Price != targetPrice ||
                !_launch.Hdv.MyListings(pid).Any(l => l.Uid == row.Uid && l.Gid == row.Gid && l.Qty == row.Qty) ||
                _launch.HdvSalePriceRefusal(pid, row.Gid, row.Qty, targetPrice) is not null)
                return false;
            var outcome = await ExecuteTaxedMutationAsync(pid, targetPrice,
                RepriceTaxOrder(row.Uid, row.Qty, targetPrice),
                () => _launch.HdvRepriceAsync(pid, row.Uid, row.Qty, targetPrice));
            taxDecision = outcome.Decision;
            return outcome.Ok;
        });
        if (taxDecision != TaxReservationDecision.Allowed)
        {
            StatusText = TaxReservationReason(taxDecision);
            Log(StatusText, LogWarn);
            return;
        }
        if (!IsCurrentHdvUiMutationTicket(ticket) ||
            _pid != pid || epoch != Volatile.Read(ref _mutationEpoch)) return;
        OnPropertyChanged(nameof(RepriceRequiresReview));
        if (ok) { Log(Loc.Fmt("L.Bots.Hdv.LogReprice", row.Name, "x" + row.Qty, N(targetPrice)), LogOk); await RecordTaxAsync(pid, charName, before, row.Gid, row.Name, row.Qty, Loc.T("L.Bots.Hdv.ActReprice")); }
        else Log(RepriceRequiresReview ? "Repricing incertain : vérifie prix et UID du lot avant tout nouvel essai." : Loc.T("L.Bots.Hdv.RepriceFail"), LogErr);
    }

    [RelayCommand]
    private async Task UndercutListing(HdvListingRow? row)
    {
        if (_pid == 0 || row is null) return;
        if (!IsHdvOpen) { Warn("L.Bots.Hdv.OpenFirst"); return; }
        long target = ComputeTarget(row);
        if (target <= 0 || target == row.Price) { StatusText = Loc.T("L.Bots.Hdv.NoChange"); return; }
        uint pid = _pid;
        var ticket = CaptureHdvUiMutationTicket();
        if (!IsCurrentHdvUiMutationTicket(ticket)) return;
        string? charName = _charName;
        StatusText = Loc.T("L.Bots.Hdv.Repricing");
        if (_launch.HdvSalePriceRefusal(pid, row.Gid, row.Qty, target) is { } refusal)
        { StatusText = refusal; Log(refusal, LogErr); return; }
        long before = _launch.Kamas(pid);
        _launch.MarkHdvManual(pid);   // HDV Tracker : clic unitaire = action manuelle
        var taxDecision = TaxReservationDecision.Allowed;
        int epoch = Volatile.Read(ref _mutationEpoch);
        bool ok = await Gated(async () =>
        {
            if (!IsCurrentHdvUiMutationTicket(ticket) ||
                _pid != pid || epoch != Volatile.Read(ref _mutationEpoch) ||
                !_launch.Hdv.MyListings(pid).Any(l => l.Uid == row.Uid && l.Gid == row.Gid && l.Qty == row.Qty) ||
                _launch.HdvSalePriceRefusal(pid, row.Gid, row.Qty, target) is not null)
                return false;
            var outcome = await ExecuteTaxedMutationAsync(pid, target,
                RepriceTaxOrder(row.Uid, row.Qty, target),
                () => _launch.HdvRepriceAsync(pid, row.Uid, row.Qty, target));
            taxDecision = outcome.Decision;
            return outcome.Ok;
        });
        if (taxDecision != TaxReservationDecision.Allowed)
        {
            StatusText = TaxReservationReason(taxDecision);
            Log(StatusText, LogWarn);
            return;
        }
        if (!IsCurrentHdvUiMutationTicket(ticket) ||
            _pid != pid || epoch != Volatile.Read(ref _mutationEpoch)) return;
        OnPropertyChanged(nameof(RepriceRequiresReview));
        if (ok) { Log(Loc.Fmt("L.Bots.Hdv.LogReprice", row.Name, "x" + row.Qty, N(target)), LogOk); await RecordTaxAsync(pid, charName, before, row.Gid, row.Name, row.Qty, Loc.T("L.Bots.Hdv.ActReprice")); }
        else Log(RepriceRequiresReview ? "Repricing incertain : vérifie prix et UID du lot avant tout nouvel essai." : Loc.T("L.Bots.Hdv.RepriceFail"), LogErr);
    }

    /// <summary>Sous-cote TOUS les lots en un passage (throttlé DelayBetweenSales pour rester legit). Confirmation d'abord.</summary>
    [RelayCommand]
    private void UndercutAll()
    {
        if (_pid == 0 || Busy || Listings.Count == 0) return;
        Dialogs.Confirm(Loc.T("L.Bots.Hdv.UndercutAllTitle"), Loc.Fmt("L.Bots.Hdv.UndercutAllBody", Listings.Count),
            onConfirm: () => _ = UndercutAllCore());
    }

    private async Task UndercutAllCore()
    {
        if (_pid == 0 || Busy) return;
        if (!IsHdvOpen && !await EnsureSellerModeAsync()) return;
        Busy = true;
        try { await RunRepriceCycle(CancellationToken.None, respectCooldown: false); }
        finally { Busy = false; }
    }

    // Un passage de reprice sur tous les lots éligibles (utilisé par le bouton « tout » ET le watcher).
    private async Task RunRepriceCycle(CancellationToken ct, bool respectCooldown = true)
    {
        uint pid = _pid;
        string? charName = _charName;
        int epoch = Volatile.Read(ref _mutationEpoch);
        // Calculer les cibles sur le meme instantane : les lots identiques s'alignent
        // au meme prix en un passage, sans sous-coter ses propres lots en cascade.
        var actions = Dispatcher.UIThread.Invoke(() =>
            Shuffle(Listings.Select(l => (l.Uid, l.Gid, l.Qty, l.Name, Target: ComputeTarget(l)))
                .Where(x => x.Target > 0).ToList()));
        // Le délai anti-reprix vaut par (objet, taille) ET par passage : les lots identiques d'un même passage partagent
        // la décision prise au début du passage. Avant, le 1er lot repricé réarmait le délai et les autres lots
        // identiques attendaient 5 min chacun (200 lots échelonnés sur des heures).
        long now = Environment.TickCount64;
        var eligible = new HashSet<(long Gid, int Qty)>(actions.Select(a => (a.Gid, a.Qty)).Distinct().Where(k =>
            !respectCooldown || HdvRepricePolicy.CooldownElapsed(now,
                _lastRepriceAt.TryGetValue(k, out long last) ? last : null, RepriceCooldownSec)));
        foreach (var (uid, gid, qty, name, target) in actions)
        {
            if (ct.IsCancellationRequested || _pid != pid || epoch != Volatile.Read(ref _mutationEpoch)) return;
            if (MaxTaxSpend > 0 && TotalTax >= MaxTaxSpend) { Log(Loc.T("L.Bots.Hdv.TaxCapHit"), LogWarn); StopWatcher(); return; }
            if (!eligible.Contains((gid, qty))) continue;
            if (_launch.HdvSalePriceRefusal(pid, gid, qty, target) is { } refusal)
            { Log(refusal, LogWarn); continue; }
            long before = _launch.Kamas(pid);
            var taxDecision = TaxReservationDecision.Allowed;
            bool ok = await Gated(async () =>
            {
                if (_pid != pid || epoch != Volatile.Read(ref _mutationEpoch)
                    || !_launch.Hdv.MyListings(pid).Any(l => l.Uid == uid && l.Gid == gid && l.Qty == qty)
                    || _launch.HdvSalePriceRefusal(pid, gid, qty, target) is not null)
                    return false;
                var outcome = await ExecuteTaxedMutationAsync(pid, target,
                    RepriceTaxOrder(uid, qty, target),
                    () => _launch.HdvRepriceAsync(pid, uid, qty, target, ct));
                taxDecision = outcome.Decision;
                return outcome.Ok;
            }, ct);
            if (taxDecision != TaxReservationDecision.Allowed)
            {
                Log(TaxReservationReason(taxDecision), LogWarn);
                StopWatcher();
                return;
            }
            if (ok) { _lastRepriceAt[(gid, qty)] = now; _ = RecordTaxAsync(pid, charName, before, gid, name, qty, Loc.T("L.Bots.Hdv.ActReprice")); Log(Loc.Fmt("L.Bots.Hdv.LogAutoReprice", name, "x" + qty, N(target)), LogOk); }
            if (_pid != pid || epoch != Volatile.Read(ref _mutationEpoch)) return;
            if (DelayBetweenSalesMaxMs > 0)
                await Task.Delay(Math.Max(0, RandDelay(DelayBetweenSalesMs, DelayBetweenSalesMaxMs)), ct);
        }
    }

    // ---------------- Moteur align-or-undercut ----------------
    private long ComputeTarget(HdvListingRow row)
    {
        bool ownAtMarket = Listings.Any(l => l.Gid == row.Gid && l.Slot == row.Slot && l.Price == row.Market);
        long sanity = SanityFloor(row.Gid, row.Qty);
        if (sanity <= 0) return -1;
        return HdvRepricePolicy.Target(row.Price, row.Market, sanity,
            PriceStep, FloorPercent, ownAtMarket, row.Ignored, row.FetchError);
    }

    // ---------------- Suivi des taxes ----------------
    private Task RecordTaxAsync(long before, long gid, string name, int qty, string action)
        => RecordTaxAsync(_pid, _charName, before, gid, name, qty, action);

    // Bot et perso FIGÉS à l'appel : si l'utilisateur change de bot pendant l'attente, la taxe (et surtout pas l'écart de kamas
    // entre les deux bots) n'est jamais comptée sur l'autre.
    private async Task RecordTaxAsync(uint pid, string? charName, long before, long gid, string name, int qty, string action)
    {
        if (before < 0 || pid == 0) return;
        // Attente du KamasUpdateEvent (ivf) : ce délai ne doit JAMAIS cadencer la boucle de vente.
        // Avant le 07/11 il était de 300 ms appelés en séquence — 60 s sur 200 lots, invisibles au réglage.
        await Task.Delay(TaxSettleDelayMs).ConfigureAwait(false);
        long after = _launch.Kamas(pid);
        long tax = before - after;
        if (after < 0 || tax <= 0) return;
        string cn = charName ?? "";
        _ = Task.Run(() => HdvStatsStore.RecordTax(cn, gid, qty, tax, name, action));   // journal HDV persistant (I/O hors UI)
        Dispatcher.UIThread.Post(() =>
        {
            if (_pid != pid) return;                                    // bot changé : journal disque seulement
            TotalTax = HdvSellPreviewMath.AddValue(TotalTax, tax);
            TaxLog.Insert(0, new HdvTaxEntry { Name = name, Lot = qty, Tax = tax, Action = action });
            while (TaxLog.Count > 80) TaxLog.RemoveAt(TaxLog.Count - 1);
            OnPropertyChanged(nameof(HasTaxLog));
        });
    }

    // ---------------- Watcher périodique ----------------
    // Démarrage = confirmation (il DÉPLACE le perso + reprice en boucle) ; arrêt = immédiat, sans modale.
    [RelayCommand]
    private void ToggleWatcher()
    {
        if (WatcherRunning) { StopWatcher(); return; }
        if (_pid == 0 || !CanHdv || BulkRunning) return;
        Dialogs.Confirm(Loc.T("L.Bots.Hdv.WatchTitle"), Loc.T("L.Bots.Hdv.WatchBody"), onConfirm: () => _ = StartWatcher());
    }

    private async Task StartWatcher()
    {
        if (_pid == 0 || !CanHdv || WatcherRunning) return;
        if (!IsHdvOpen && !await EnsureSellerModeAsync()) return;
        _watchCts = new CancellationTokenSource();
        WatcherRunning = true; OnPropertyChanged(nameof(WatcherText));
        _launch.SetHdvAuto(_pid, true);   // panel web : reprix automatique en cours
        _ = WatchLoop(_watchCts.Token);
    }

    // S'assure que le HDV VENDEUR est prêt, en faisant tout le chemin automatiquement selon l'état :
    //  - déjà en vente -> rien ;
    //  - HDV ouvert acheteur (kdw) -> simple bascule vendeur (iov action=5) ;
    //  - pas au HDV -> voyage vers la cible + ouverture de l'hôtelier (iwo) + bascule vendeur (le flow « je me déplace,
    //    je parle au PNJ, je swap en vente »). Renvoie vrai si vendeur prêt. `ok` (event kby reçu) fait foi (IsHdvOpen peut courser).
    private async Task<bool> EnsureSellerModeAsync(CancellationToken outer = default)
    {
        if (IsHdvOpen) return true;

        // Cas rapide : déjà devant l'hôtelier (ouvert acheteur) -> juste basculer en vente.
        if (_launch.HdvAnyOpen(_pid))
        {
            StatusText = Loc.T("L.Bots.Hdv.SwitchingSeller");
            long map = _launch.CurrentMap(_pid) ?? 0;
            bool sw = map != 0 && await _launch.HdvOpenSellerAsync(_pid, map);
            OnPropertyChanged(nameof(IsHdvOpen));
            if (!sw) { Warn("L.Bots.Hdv.SellerSwitchFail"); }
            return sw;
        }

        // Cas complet : pas au HDV -> voyage + ouverture hôtelier + bascule vente, via un hôte transitoire (moteur de voyage).
        if (SelectedLocation is not { } loc) { Warn("L.Bots.Hdv.PickTarget"); return false; }
        if (Traveling) return false;
        Traveling = true;
        StatusText = Loc.Fmt("L.Bots.Hdv.Traveling", loc.Name);
        var host = new GivreLuaHost(_pid, _launch, Stores.World);
        // TRAÇABILITÉ du voyage : sans ça les logs « trajet » de l'hôte transitoire étaient PERDUS (aucun abonné). On les
        // pousse dans le journal transport (fichier %APPDATA%\GivreClone\logs, diagnostic) + on affiche le dernier en statut.
        void OnHostLog(LuaLogEntry e)
        {
            GivreLauncher.Core.Transport.TransportLog.Line($"[hdv-trajet] {e.Category} : {e.Message}");
            Dispatcher.UIThread.Post(() => StatusText = e.Message);
        }
        host.Log += OnHostLog;
        _travelCts = outer.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(outer) : new CancellationTokenSource();
        try
        {
            bool ok = await host.GoToHdvAndOpenSellerAsync(loc.MapId, _travelCts.Token);
            OnPropertyChanged(nameof(IsHdvOpen));
            if (!ok)
            {
                // Motif du trajet (refus serveur, chemin coupé...) plutôt que le message générique, un seul toast.
                string generic = Loc.T("L.Bots.Hdv.TravelOpenFail");
                string why = host.LastTravelResult is { Ok: false, Reason: { Length: > 0 } r } ? generic + " " + r : generic;
                StatusText = why; Toasts.Show(why, success: false);
                return false;
            }
            StatusText = Loc.T("L.Bots.Hdv.Ready"); Toasts.Show(Loc.T("L.Bots.Hdv.Ready"), success: true);
            return true;
        }
        finally { host.Log -= OnHostLog; host.Dispose(); _travelCts?.Dispose(); _travelCts = null; Traveling = false; }
    }

    /// <summary>Annule le voyage/ouverture HDV en cours (bouton « Annuler »). Le CancellationToken se propage au moteur de
    /// voyage (WaitUntil/InterruptibleDelay via ThrowIfStopping) -> GoToHdvAndOpenSellerAsync rend false et le bloc finally
    /// remet Traveling=false + libère l'hôte. No-op si aucun voyage n'est en cours.</summary>
    [RelayCommand]
    private void CancelTravel()
    {
        if (!Traveling) return;
        StatusText = Loc.T("L.Bots.Hdv.TravelCanceling");
        try { _travelCts?.Cancel(); } catch { /* déjà disposé -> le finally a déjà nettoyé */ }
    }

    private void StopWatcher()
    {
        var cts = _watchCts; _watchCts = null;   // capturer avant de nuller (evite une course avec un Cancel concurrent) + Dispose (pas de fuite de CTS)
        try { cts?.Cancel(); } catch { } finally { cts?.Dispose(); }
        if (WatcherRunning) { WatcherRunning = false; OnPropertyChanged(nameof(WatcherText)); }
        if (_pid != 0 && !BulkRunning) _launch.SetHdvAuto(_pid, false);
    }

    private async Task WatchLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (MaxTaxSpend > 0 && TotalTax >= MaxTaxSpend) { Dispatcher.UIThread.Post(() => StatusText = Loc.T("L.Bots.Hdv.TaxCapHit")); return; }
                var allGids = Dispatcher.UIThread.Invoke(() => Listings.Where(l => !l.Ignored).Select(l => l.Gid).Distinct().ToList());
                // TTL jitteré : on ne RE-sonde un prix que s'il est périmé (au-delà de ~45-90 s). Sans ça le watcher re-query
                // chaque objet à chaque cycle (5-8 s) H24 = l'empreinte la plus détectable (aucun joueur ne fait ça). + ordre aléatoire.
                long ttl = RandDelay(45_000, 90_000);
                var gids = Shuffle(allGids.Where(g => _launch.Hdv.PriceAgeMs(_pid, g) > ttl).ToList());
                foreach (var gid in gids)
                {
                    if (ct.IsCancellationRequested) return;
                    Dispatcher.UIThread.Post(() => SetFetching(gid, true));
                    await _launch.HdvGetMarketPriceAsync(_pid, gid, ct);
                    MarkFetch(gid);
                    await Task.Delay(RandDelay(150, 450), ct);   // jitter humain (au lieu d'un 220 ms fixe régulier)
                    Dispatcher.UIThread.Post(() => SetFetching(gid, false));
                }
                Dispatcher.UIThread.Post(RefreshLive);
                await Task.Delay(300, ct);
                await RunRepriceCycle(ct);
                await TopUpAllRelistAsync(ct);   // remise en vente : chaque objet de la liste complété jusqu'à son nombre de lots
                await Task.Delay(Math.Max(1000, RandDelay(RefreshRateMs, RefreshRateMaxMs)), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Dispatcher.UIThread.Post(() => StatusText = "Watcher: " + ex.Message); }
        finally { Dispatcher.UIThread.Post(() => { ClearAllFetching(); StopWatcher(); }); }
    }

    // ---------------- Config ----------------
    public void Load(string? charName)
    {
        _loading = true;
        // Load() est rappelé à CHAQUE tick tant que le nom du perso sélectionné se résout (BotsViewModel.Tick) : on ne
        // réinitialise l'état financier de SESSION que si le perso CHANGE réellement (sinon on effacerait les compteurs
        // à chaque tick). L'historique complet reste dans HdvStatsStore (journal persistant) ; ces compteurs = session courante.
        int serverId = _pid == 0 ? 0 : _launch.ServerIdOf(_pid);
        string scopedKey = HdvProfileKey.For(charName, serverId);
        bool charChanged = !string.Equals(_hdvProfileKey, scopedKey, StringComparison.Ordinal);
        if (charChanged) Interlocked.Increment(ref _mutationEpoch);
        _hdvProfileKey = scopedKey;
        _charName = charName;
        Stats.SetChar(charName);
        if (charChanged)
        {
            ResetPriceHistorySession();
            _buyCts?.Cancel();
            BuyRows.Clear(); RelistRows.Clear(); ScanRows.Clear(); CraftSteps.Clear();
            TotalTax = 0; TotalSales = 0;
            TaxLog.Clear(); SalesLog.Clear();
            OnPropertyChanged(nameof(HasTaxLog)); OnPropertyChanged(nameof(HasSalesLog));
            if (WatcherRunning) StopWatcher();   // le watcher de l'ancien perso ne doit pas continuer sur le nouveau
            CancelBulk();                        // ... ni une mise en vente en masse
            CancelTravel();                      // ... ni un voyage vers l'HDV
            SellModalVisible = false;
        }
        _cfg = HdvProfileStore.GetScoped(charName, serverId);
        SelectedLocation = _cfg.HdvMapId != 0 ? Locations.FirstOrDefault(l => l.MapId == _cfg.HdvMapId) : null;
        PriceStep = Math.Max(1, _cfg.UndercutKamas);
        FloorPercent = Math.Clamp(_cfg.FloorPercent, 0, 100);
        RefreshRateMs = Math.Max(1000, _cfg.RefreshRateMs);
        RefreshRateMaxMs = Math.Max(RefreshRateMs, _cfg.RefreshRateMaxMs);
        DelayBetweenSalesMs = Math.Max(0, _cfg.DelayBetweenSalesMs);
        DelayBetweenSalesMaxMs = Math.Max(DelayBetweenSalesMs, _cfg.DelayBetweenSalesMaxMs);
        MaxTaxSpend = Math.Max(0, _cfg.MaxTaxSpend);
        RepriceCooldownSec = Math.Max(0, _cfg.RepriceCooldownSec);
        PriceCeilingMultiplier = Math.Max(0, _cfg.PriceCeilingMultiplier);
        MaxTaxPerOperation = Math.Max(0, _cfg.MaxTaxPerOperation);
        if (charChanged) _lastRepriceAt.Clear();
        if (_pid != 0) _launch.SetHdvSaleLimits(_pid, PriceCeilingMultiplier, MaxTaxPerOperation);
        _ignored.Clear();
        foreach (var g in _cfg.IgnoreGids) _ignored.Add(g);
        // Cases cochées : relues seulement au CHANGEMENT de perso (Load() tourne à chaque tick ; ensuite _checkedGids fait foi et
        // est persisté à chaque clic) — sinon on recalculait l'estimation sur ~1000 lignes à chaque tick.
        if (charChanged || !_checkedLoaded) { LoadChecked(_cfg.SellGids); _checkedLoaded = true; }
        BulkLotIndex = Math.Clamp(_cfg.BulkLotIndex, 0, 4);
        BulkMaxLots = Math.Clamp(_cfg.BulkMaxLotsPerItem, 0, 100);
        LoadTrade();
        if (charChanged) RestoreTradeRules();
        OnPropertyChanged(nameof(CanHdv));
        _loading = false;
    }

    partial void OnSelectedLocationChanged(HdvLocation? v) { if (_loading) return; _cfg.HdvMapId = v?.MapId ?? 0; _cfg.HdvName = v?.Name ?? ""; Save(); }
    partial void OnPriceStepChanged(int v) { if (_loading) return; _cfg.UndercutKamas = Math.Max(1, v); Save(); }
    partial void OnFloorPercentChanged(int v) { if (_loading) return; _cfg.FloorPercent = Math.Clamp(v, 0, 100); Save(); }
    partial void OnRefreshRateMsChanged(int v) { if (_loading) return; _cfg.RefreshRateMs = Math.Max(1000, v); Save(); }
    partial void OnRefreshRateMaxMsChanged(int v) { if (_loading) return; _cfg.RefreshRateMaxMs = Math.Max(RefreshRateMs, v); Save(); }   // max >= min (aligne sur Load)
    partial void OnDelayBetweenSalesMsChanged(int v) { if (_loading) return; _cfg.DelayBetweenSalesMs = Math.Max(0, v); Save(); }
    partial void OnDelayBetweenSalesMaxMsChanged(int v) { if (_loading) return; _cfg.DelayBetweenSalesMaxMs = Math.Max(DelayBetweenSalesMs, v); Save(); }
    partial void OnMaxTaxSpendChanged(long v) { if (_loading) return; _cfg.MaxTaxSpend = Math.Max(0, v); Save(); }
    partial void OnRepriceCooldownSecChanged(int value) { if (_loading) return; _cfg.RepriceCooldownSec = Math.Max(0, value); Save(); }
    partial void OnPriceCeilingMultiplierChanged(decimal value) { if (_loading) return; _cfg.PriceCeilingMultiplier = Math.Max(0, value); PushSaleLimits(); Save(); }
    partial void OnMaxTaxPerOperationChanged(long value) { if (_loading) return; _cfg.MaxTaxPerOperation = Math.Max(0, value); PushSaleLimits(); Save(); }
    private void PushSaleLimits() { if (_pid != 0) _launch.SetHdvSaleLimits(_pid, PriceCeilingMultiplier, MaxTaxPerOperation); }

    private void Save() { if (_loading) return; HdvProfileStore.Save(); }
}
