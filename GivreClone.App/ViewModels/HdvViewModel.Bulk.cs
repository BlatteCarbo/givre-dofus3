using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

public enum HdvSellRowState { Idle, Pricing, Selling, Done, Skipped, Failed }

/// <summary>Un OBJET vendable (sac + banque, piles dédoublonnées) dans la fenêtre « Mettre en vente ». La valeur estimée vient du
/// prix moyen SERVEUR (estimation seulement : le prix de vente est toujours calculé sur le marché de l'HDV au moment de vendre).</summary>
public sealed partial class HdvSellRow : ObservableObject
{
    public int TooltipPid { get; init; }
    public long Gid { get; init; }
    public string Name { get; init; } = "";
    public Bitmap? Icon { get; init; }
    public bool HasIcon => Icon is not null;
    public int Qty { get; init; }            // total sac + banque
    public int MaxStack { get; init; }       // plus grande pile : un lot sort d'UNE seule pile
    public bool InBag { get; init; }
    public bool InBank { get; init; }
    public bool IsResource { get; init; }
    public long AvgUnit { get; init; }       // prix moyen serveur, à l'unité (0 = inconnu)
    public long EstValue => HdvSellPreviewMath.EstimatedValue(AvgUnit, Qty);
    public string QtyText => Qty.ToString("N0", CultureInfo.CurrentUICulture);
    public string AvgText => AvgUnit > 0 ? AvgUnit.ToString("N0", CultureInfo.CurrentUICulture) : "—";
    public string EstValueText => EstValue > 0 ? EstValue.ToString("N0", CultureInfo.CurrentUICulture) : "—";
    public string SourceText => InBag && InBank ? Loc.T("L.Bots.Hdv.Sell.SrcBoth") : InBank ? Loc.T("L.Bots.Hdv.FromBank") : Loc.T("L.Bots.Hdv.FromBag");
    public Action<HdvSellRow>? CheckToggled { get; init; }

    [ObservableProperty] public partial bool Checked { get; set; }
    [ObservableProperty] public partial string PlanText { get; set; } = "";
    [ObservableProperty] public partial HdvSellRowState State { get; set; }
    [ObservableProperty] public partial int LotsDone { get; set; }
    [ObservableProperty] public partial int LotsPlanned { get; set; }
    [ObservableProperty] public partial long ListedValue { get; set; }
    [ObservableProperty] public partial string StateText { get; set; } = "";
    /// <summary>Motif du dernier refus de mise en vente pendant ce passage (texte du jeu si reçu), ou null.</summary>
    public string? LastError { get; set; }

    public bool IsBusy => State is HdvSellRowState.Pricing or HdvSellRowState.Selling;
    public bool IsDone => State == HdvSellRowState.Done;
    public bool IsSkipped => State == HdvSellRowState.Skipped;
    public bool IsFailed => State == HdvSellRowState.Failed;
    public bool HasState => State != HdvSellRowState.Idle;
    public string ProgressText => LotsPlanned > 0 ? $"{LotsDone}/{LotsPlanned}" : "";

    partial void OnCheckedChanged(bool value) => CheckToggled?.Invoke(this);
    partial void OnStateChanged(HdvSellRowState value)
    {
        OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(IsDone)); OnPropertyChanged(nameof(IsSkipped));
        OnPropertyChanged(nameof(IsFailed)); OnPropertyChanged(nameof(HasState));
    }
    partial void OnLotsDoneChanged(int value) => OnPropertyChanged(nameof(ProgressText));
    partial void OnLotsPlannedChanged(int value) => OnPropertyChanged(nameof(ProgressText));
}

// Mise en vente dans une fenêtre dédiée (niveau fenêtre) : objets cochés ou toutes les ressources, en FLUX : pour chaque objet, du
// plus précieux au moins précieux (prix moyen serveur × quantité), on récupère le prix du marché PUIS on met en vente tout de suite
// (lots du plus grand au plus petit), jusqu'à remplir les emplacements libres. Plus d'aller-retour « tous les prix d'abord ».
// Exclusif avec la surveillance (reprix auto) et les actions manuelles : un seul moteur d'actions HDV à la fois.
public partial class HdvViewModel
{
    private readonly HashSet<long> _checkedGids = new();
    private readonly Dictionary<long, Bitmap?> _iconCache = new();
    private CancellationTokenSource? _bulkCts;

    // ---- fenêtre ----
    public ObservableCollection<HdvSellRow> SellRows { get; } = new();
    public ObservableCollection<HdvSellRow> SellRowsView { get; } = new();
    [ObservableProperty] public partial bool SellModalVisible { get; set; }
    [ObservableProperty] public partial int SellStage { get; set; }             // 0 choix · 1 en cours · 2 terminé
    [ObservableProperty] public partial string SellSearch { get; set; } = "";
    [ObservableProperty] public partial int SellFilterIndex { get; set; }       // 0 tout · 1 ressources · 2 cochés
    [ObservableProperty] public partial int SellSortIndex { get; set; }         // 0 valeur estimée · 1 nom · 2 quantité
    [ObservableProperty] public partial HdvSellRow? SelectedSellRow { get; set; }
    public bool IsStageSelect => SellStage == 0;
    public bool IsStageRunning => SellStage == 1;
    public bool IsStageDone => SellStage == 2;
    public string[] SellSortModes => new[] { Loc.T("L.Bots.Hdv.Sell.SortValue"), Loc.T("L.Bots.Hdv.SortName"), Loc.T("L.Bots.Hdv.Sell.SortQty") };
    public string[] SellFilterModes => new[] { Loc.T("L.Bots.Hdv.Sell.FilterAll"), Loc.T("L.Bots.Hdv.Sell.FilterRes"), Loc.T("L.Bots.Hdv.Sell.FilterChecked") };
    public string SellCharName => _charName ?? "";
    public int NotAcceptedCount { get; private set; }
    public int BoundHiddenCount { get; private set; }
    public string NotAcceptedText => string.Join(" ", new[]
    {
        NotAcceptedCount > 0 ? Loc.Fmt("L.Bots.Hdv.Sell.NotAccepted", NotAcceptedCount) : "",
        BoundHiddenCount > 0 ? Loc.Fmt("L.Bots.Hdv.Sell.BoundHidden", BoundHiddenCount) : "",
    }.Where(text => text.Length > 0));
    public bool HasNotAccepted => NotAcceptedCount > 0 || BoundHiddenCount > 0;

    // ---- réglages ----
    [ObservableProperty] public partial bool BulkRunning { get; set; }
    [ObservableProperty] public partial int BulkDone { get; set; }
    [ObservableProperty] public partial int BulkTotal { get; set; }
    /// <summary>0 = auto (x1000 → x100 → x10 → x1), 1..4 = x1, x10, x100, x1000.</summary>
    [ObservableProperty] public partial int BulkLotIndex { get; set; }
    /// <summary>Lots mis en vente au plus, par objet (0 = sans limite).</summary>
    [ObservableProperty] public partial int BulkMaxLots { get; set; } = 5;
    public string[] BulkLotModes => new[] { Loc.T("L.Bots.Hdv.Bulk.LotAuto"), "x1", "x10", "x100", "x1000" };

    // ---- résumé / progression ----
    public int CheckedCount => SellRows.Count(r => r.Checked);
    public bool HasChecked => CheckedCount > 0;
    public string CheckedCountText => Loc.Fmt("L.Bots.Hdv.Bulk.CheckedFmt", CheckedCount);
    [ObservableProperty] public partial int PreviewLots { get; set; }
    [ObservableProperty] public partial long PreviewValue { get; set; }
    [ObservableProperty] public partial string PreviewText { get; set; } = "";
    [ObservableProperty] public partial string TaxHintText { get; set; } = "";
    [ObservableProperty] public partial string CurrentItemText { get; set; } = "";
    [ObservableProperty] public partial long RunListedValue { get; set; }
    [ObservableProperty] public partial string RunSummaryText { get; set; } = "";
    public bool HasTaxHint => TaxHintText.Length > 0;
    public string StartSellText => PreviewLots > 0 ? Loc.Fmt("L.Bots.Hdv.Sell.StartFmt", PreviewLots) : Loc.T("L.Bots.Hdv.Sell.Start");
    partial void OnPreviewLotsChanged(int v) => OnPropertyChanged(nameof(StartSellText));
    public string FreeSlotsText
    {
        get
        {
            int free = _launch.HdvFreeSlots(_pid), max = _launch.HdvMaxSlots(_pid);
            return free >= 0 ? Loc.Fmt("L.Bots.Hdv.Bulk.SlotsFmt", free, max) : Loc.T("L.Bots.Hdv.Bulk.SlotsUnknown");
        }
    }
    public string RunListedValueText => N(RunListedValue);
    public string BulkProgressText => Loc.Fmt("L.Bots.Hdv.Sell.ProgressFmt", BulkDone, Math.Max(BulkDone, BulkTotal));
    public double BulkProgressValue => BulkTotal > 0 ? Math.Min(1.0, (double)BulkDone / BulkTotal) : 0;
    public bool CanBulk => !BulkRunning && !WatcherRunning && !Busy && !Traveling && !BankStockLoading;
    public bool CanStartSell => CanBulk && CanHdv && HasChecked;

    partial void OnBulkRunningChanged(bool v) { OnPropertyChanged(nameof(CanBulk)); OnPropertyChanged(nameof(CanManual)); OnPropertyChanged(nameof(CanStartSell)); }
    partial void OnBulkDoneChanged(int v) { OnPropertyChanged(nameof(BulkProgressText)); OnPropertyChanged(nameof(BulkProgressValue)); }
    partial void OnBulkTotalChanged(int v) { OnPropertyChanged(nameof(BulkProgressText)); OnPropertyChanged(nameof(BulkProgressValue)); }
    partial void OnBusyChanged(bool v) { OnPropertyChanged(nameof(CanBulk)); OnPropertyChanged(nameof(CanStartSell)); }
    partial void OnTravelingChanged(bool v) { OnPropertyChanged(nameof(CanBulk)); OnPropertyChanged(nameof(CanStartSell)); }
    partial void OnRunListedValueChanged(long v) => OnPropertyChanged(nameof(RunListedValueText));
    partial void OnTaxHintTextChanged(string v) => OnPropertyChanged(nameof(HasTaxHint));
    partial void OnSellStageChanged(int v) { OnPropertyChanged(nameof(IsStageSelect)); OnPropertyChanged(nameof(IsStageRunning)); OnPropertyChanged(nameof(IsStageDone)); }
    partial void OnBulkLotIndexChanged(int v) { if (!_loading) { _cfg.BulkLotIndex = Math.Clamp(v, 0, 4); Save(); } RecomputePreview(); }
    partial void OnBulkMaxLotsChanged(int v) { if (!_loading) { _cfg.BulkMaxLotsPerItem = Math.Clamp(v, 0, 100); Save(); } RecomputePreview(); }
    partial void OnSellSearchChanged(string v) => RebuildSellView();
    partial void OnSellFilterIndexChanged(int v) => RebuildSellView();
    partial void OnSellSortIndexChanged(int v) => RebuildSellView();

    // ---------------- ouverture / fermeture ----------------
    [RelayCommand]
    private void OpenSellModal()
    {
        if (_pid == 0 || !CanHdv) return;
        if (!BulkRunning) SellStage = 0;
        RefreshSellRows(force: !BulkRunning);
        OnPropertyChanged(nameof(SellCharName)); OnPropertyChanged(nameof(FreeSlotsText));
        SellModalVisible = true;
        AudioService.PlayUi(AudioService.ModalIn);
        if (IsHdvOpen && !BulkRunning && !WatcherRunning) _ = RefreshBankStockAsync();
    }

    [ObservableProperty] public partial bool BankStockLoading { get; set; }
    partial void OnBankStockLoadingChanged(bool value) { OnPropertyChanged(nameof(CanBulk)); OnPropertyChanged(nameof(CanStartSell)); }
    [ObservableProperty] public partial string BankStockStatus { get; set; } = "";
    private CancellationTokenSource? _bankStockCts;
    private bool _bankStockAttempted;

    // Lecture seulement : aucune vente ni voyage en ouvrant cette fenêtre. En acheteur,
    // le bouton explicite peut basculer le HDV déjà ouvert vers le vendeur.
    [RelayCommand]
    private async Task RefreshBankStockAsync()
    {
        if (_pid == 0 || !CanHdv || BankStockLoading || BulkRunning || WatcherRunning || Busy || Traveling) return;
        uint pid = _pid;
        var cts = new CancellationTokenSource();
        _bankStockCts = cts;
        BankStockLoading = true;
        _bankStockAttempted = true;
        BankStockStatus = Loc.T("L.Bots.Hdv.BankLoading");
        try
        {
            bool ok = await Gated(async () =>
            {
                if (cts.IsCancellationRequested || _pid != pid) return false;
                if (!_launch.HdvSellerOpen(pid) && _launch.HdvAnyOpen(pid))
                {
                    long map = _launch.CurrentMap(pid) ?? 0;
                    if (map == 0 || !await _launch.HdvOpenSellerAsync(pid, map, cts.Token)) return false;
                }
                if (cts.IsCancellationRequested || _pid != pid) return false;
                return await _launch.SetMergedInventoryAsync(pid, bank: true, havenBag: false, cts.Token);
            }, cts.Token);
            if (_pid != pid || !ReferenceEquals(_bankStockCts, cts)) return;
            BankStockStatus = Loc.T(ok ? "L.Bots.Hdv.BankReady" : "L.Bots.Hdv.BankUnavailable");
            RefreshSellRows(force: true);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (_pid == pid && ReferenceEquals(_bankStockCts, cts)) BankStockStatus = Loc.T("L.Bots.Hdv.BankUnavailable"); }
        finally
        {
            if (ReferenceEquals(_bankStockCts, cts)) { _bankStockCts = null; BankStockLoading = false; }
            cts.Dispose();
        }
    }

    /// <summary>Ferme la fenêtre. Pendant une mise en vente = « Réduire » : elle continue, la carte de l'onglet montre la progression.</summary>
    [RelayCommand]
    private void CloseSellModal()
    {
        SellModalVisible = false;
        SellArmed = false;
        if (!BulkRunning && SellStage == 2) SellStage = 0;
    }

    // Une ligne par OBJET éligible (vendable, accepté par l'HDV vendeur ouvert), piles sac + banque dédoublonnées.
    private string _sellSig = "";
    private void RefreshSellRows(bool force = false)
    {
        if (_pid == 0) { if (SellRows.Count > 0) { SellRows.Clear(); SellRowsView.Clear(); } _sellSig = ""; RaiseChecked(); return; }
        if (BulkRunning) return;   // pendant un passage : les lignes portent l'état de progression -> jamais reconstruites
        var byGid = new Dictionary<long, (int Qty, int MaxStack, bool Bag, bool Bank)>();
        var refused = new HashSet<long>();
        var bound = new HashSet<long>();
        foreach (var (it, inBank) in _launch.HdvMergedItems(_pid))
        {
            if (!_launch.IsSellableItem(it.Gid)) continue;
            if (!inBank && it.Position != 63) continue;   // équipé : jamais proposé
            if (!_launch.ItemCanTrade(_pid, it.Uid)) { bound.Add(it.Gid); continue; }   // lié au personnage/compte : invendable
            if (IsHdvOpen && !_launch.HdvAcceptsGid(_pid, it.Gid)) { refused.Add(it.Gid); continue; }   // conditions de CET HDV
            if (it.Qty <= 0) continue; // impossible stack: do not create a sale preview from it
            byGid.TryGetValue(it.Gid, out var a);
            byGid[it.Gid] = (HdvSellPreviewMath.AddQuantity(a.Qty, it.Qty),
                Math.Max(a.MaxStack, it.Qty), a.Bag || !inBank, a.Bank || inBank);
        }
        NotAcceptedCount = refused.Count;
        BoundHiddenCount = bound.Count;
        OnPropertyChanged(nameof(NotAcceptedText)); OnPropertyChanged(nameof(HasNotAccepted));
        string sig = string.Join("|", byGid.OrderBy(k => k.Key).Select(k => k.Key + ":" + k.Value.Qty + ":" + k.Value.MaxStack + ":" + k.Value.Bag + ":" + k.Value.Bank));
        if (!force && sig == _sellSig) { RecomputePreview(); return; }
        _sellSig = sig;
        long? selGid = SelectedSellRow?.Gid;
        _reselecting = true;
        try
        {
            SellRows.Clear();
            foreach (var (gid, a) in byGid)
            {
                if (!_iconCache.TryGetValue(gid, out var icon)) _iconCache[gid] = icon = IconFor(gid);
                SellRows.Add(new HdvSellRow
                {
                    TooltipPid = (int)_pid, Gid = gid, Name = _launch.ItemDisplayName(gid), Icon = icon, Qty = a.Qty, MaxStack = a.MaxStack,
                    InBag = a.Bag, InBank = a.Bank, IsResource = _launch.IsResourceItem(gid), AvgUnit = AvgUnit(gid),
                    Checked = _checkedGids.Contains(gid), CheckToggled = OnSellRowToggled,
                });
            }
            RebuildSellView();
            SelectedSellRow = selGid is long g ? SellRows.FirstOrDefault(r => r.Gid == g) : null;
        }
        finally { _reselecting = false; }
        RaiseChecked();
    }

    private void RebuildSellView()
    {
        IEnumerable<HdvSellRow> q = SellRows;
        var f = (SellSearch ?? "").Trim();
        if (f.Length > 0) q = q.Where(r => r.Name.Contains(f, StringComparison.OrdinalIgnoreCase));
        q = SellFilterIndex switch { 1 => q.Where(r => r.IsResource), 2 => q.Where(r => r.Checked), _ => q };
        q = SellSortIndex switch
        {
            1 => q.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
            2 => q.OrderByDescending(r => r.Qty),
            _ => q.OrderByDescending(r => r.EstValue).ThenByDescending(r => r.Qty),
        };
        var list = q.ToList();
        SellRowsView.Clear();
        foreach (var r in list) SellRowsView.Add(r);
        RecomputePreview();
    }

    // ---------------- sélection (cases cochées, mémorisées par perso et par objet) ----------------
    private void OnSellRowToggled(HdvSellRow row)
    {
        if (_reselecting) return;
        if (row.Checked) _checkedGids.Add(row.Gid); else _checkedGids.Remove(row.Gid);
        PersistChecked();
        RaiseChecked();
    }

    private void SetChecked(IEnumerable<HdvSellRow> rows, bool on)
    {
        _reselecting = true;
        try
        {
            foreach (var r in rows)
            {
                r.Checked = on;
                if (on) _checkedGids.Add(r.Gid); else _checkedGids.Remove(r.Gid);
            }
        }
        finally { _reselecting = false; }
        PersistChecked();
        RaiseChecked();
        if (SellFilterIndex == 2) RebuildSellView();
    }

    private void PersistChecked()
    {
        if (_loading) return;
        _cfg.SellGids = string.Join(",", _checkedGids);
        Save();
    }

    private void RaiseChecked()
    {
        OnPropertyChanged(nameof(CheckedCount)); OnPropertyChanged(nameof(HasChecked)); OnPropertyChanged(nameof(CheckedCountText));
        OnPropertyChanged(nameof(CanStartSell));
        RecomputePreview();
    }

    private void LoadChecked(string csv)
    {
        _checkedGids.Clear();
        foreach (var s in (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (long.TryParse(s, out var g) && g > 0) _checkedGids.Add(g);
        RaiseChecked();
    }

    [RelayCommand] private void CheckVisible() => SetChecked(SellRowsView.ToList(), true);
    [RelayCommand] private void CheckResources() => SetChecked(SellRows.Where(r => r.IsResource).ToList(), true);
    [RelayCommand]
    private void UncheckAll()
    {
        _checkedGids.Clear();   // aussi les objets cochés qui ne sont plus dans l'inventaire
        SetChecked(SellRows.ToList(), false);
    }

    // ---------------- estimation locale (aucune requête) ----------------
    // Lots envisagés par objet selon la quantité et les réglages, dans l'ordre de passage (valeur estimée), bornés par les
    // emplacements libres. Le prix réel est calculé au moment de vendre, sur le marché de l'HDV.
    private void RecomputePreview()
    {
        if (BulkRunning) return;
        int free = _launch.HdvFreeSlots(_pid);
        int budget = free >= 0 ? free : int.MaxValue;
        int lots = 0; long value = 0;
        foreach (var r in SellRows) r.PlanText = "";
        foreach (var r in OrderForRun(SellRows.Where(r => r.Checked)))
        {
            if (lots >= budget) { r.PlanText = Loc.T("L.Bots.Hdv.Sell.NoSlot"); continue; }
            var sizes = PlanSizes(r.MaxStack, r.Qty, (int)Math.Min(MaxLotsFor(), (long)budget - lots));
            r.PlanText = PlanLabel(sizes);
            lots = HdvSellPreviewMath.AddQuantity(lots, sizes.Count);
            foreach (int size in sizes)
                value = HdvSellPreviewMath.AddValue(value,
                    HdvSellPreviewMath.EstimatedValue(r.AvgUnit, size));
        }
        PreviewLots = lots; PreviewValue = value;
        PreviewText = lots == 0 ? "" : Loc.Fmt("L.Bots.Hdv.Sell.PreviewFmt", lots, N(value));
        // Indicative hint only. A missing/invalid server tax rate must remain unknown;
        // the actual capped sale independently fails closed via HdvTaxBudget.
        var taxSnapshot = MaxTaxSpend > 0
            ? _taxReservations.ReadProfileSnapshot(_hdvProfileKey)
            : new HdvTaxProfileSnapshot(0, HdvTaxProfileRecoveryStatus.Ready);
        long reserved = taxSnapshot.ReservedFees;
        var capHint = HdvSellPreviewMath.TaxCapHint(MaxTaxSpend,
            HdvSellPreviewMath.AddValue(TotalTax, reserved), value, _launch.HdvTaxPercent(_pid));
        TaxHintText = MaxTaxSpend > 0 && taxSnapshot.RecoveryStatus == HdvTaxProfileRecoveryStatus.Unavailable
            ? Loc.T("L.Bots.Hdv.Sell.TaxJournalUnavailable")
            : MaxTaxSpend > 0 && taxSnapshot.RecoveryStatus == HdvTaxProfileRecoveryStatus.NeedsReconciliation
                ? TaxReservationReason(TaxReservationDecision.NeedsReconciliation)
            : !capHint.Warn ? "" : capHint.UnknownRate
                ? Loc.T("L.Bots.Hdv.Sell.TaxRateUnknown")
                : Loc.Fmt("L.Bots.Hdv.Sell.TaxCapHint", N(MaxTaxSpend), N(capHint.RemainingListingValue));
    }

    private int MaxLotsFor() => BulkMaxLots <= 0 ? int.MaxValue : BulkMaxLots;

    // Ordre de passage : valeur estimée décroissante (prix moyen serveur × quantité), puis quantité.
    private static IEnumerable<HdvSellRow> OrderForRun(IEnumerable<HdvSellRow> rows) =>
        rows.OrderByDescending(r => r.EstValue).ThenByDescending(r => r.Qty);

    private int[] SizesDesc()
    {
        int[] allowed = _launch.HdvLotSizes(_pid);
        int fixedLot = BulkLotIndex switch { 1 => 1, 2 => 10, 3 => 100, 4 => 1000, _ => 0 };
        return (fixedLot > 0 ? allowed.Where(s => s == fixedLot) : allowed).OrderByDescending(s => s).ToArray();
    }

    // Tailles de lot envisagées pour une quantité (sans prix) : glouton du plus GRAND au plus petit, la plus grande pile d'abord.
    private List<int> PlanSizes(int maxStack, int qty, int maxLots)
    {
        var res = new List<int>();
        if (maxLots <= 0) return res;
        var sizes = SizesDesc();
        foreach (int stackQty in new[] { maxStack, qty - maxStack })
        {
            int left = Math.Max(0, stackQty);
            while (left > 0 && res.Count < maxLots)
            {
                int s = sizes.FirstOrDefault(z => z <= left);
                if (s <= 0) break;
                res.Add(s); left -= s;
            }
        }
        return res;
    }

    private static string PlanLabel(List<int> sizes) =>
        sizes.Count == 0 ? "—" : string.Join(" + ", sizes.GroupBy(s => s).OrderByDescending(g => g.Key).Select(g => $"{g.Count()}×x{g.Key}"));

    // ---------------- prix ----------------
    // Prix moyen à l'UNITÉ : table du serveur (à la connexion), sinon celui renvoyé avec le dernier prix HDV. 0 = inconnu.
    private long AvgUnit(long gid)
    {
        long a = _launch.AveragePrice(_pid, (int)gid);
        if (a > 0) return a;
        long h = _launch.Hdv.AverageFor(_pid, gid);
        return h > 0 ? h : 0;
    }

    /// <summary>Plancher ABSOLU d'un lot : jamais sous la moitié du prix moyen serveur (un lot « appât » à 1 kamas ne doit pas
    /// entraîner nos prix). 1 si le prix moyen est inconnu.</summary>
    private long SanityFloor(long gid, int lotSize)
    {
        long avg = AvgUnit(gid);
        // -1 is an explicit invalid-price sentinel, NEVER a real listing price.
        return HdvSalePriceMath.HalfAverageFloor(avg, lotSize) ?? -1;
    }

    /// <summary>Prix d'un NOUVEAU lot de <paramref name="lotSize"/>. Concurrent sur cette taille : même règle que le reprix
    /// (aligné sur mon lot s'il est déjà le moins cher, sinon marché − sous-cote, jamais sous le plancher %) + plancher absolu.
    /// Aucun concurrent sur cette taille : prix DÉRIVÉ de la taille connue la plus proche (à l'unité), borné autour du prix moyen
    /// serveur (80–130 %) ; sans prix moyen, seulement depuis la taille voisine (x100 → x1000, jamais x1 → x1000). -1 = aucune
    /// référence fiable -> pas de vente de cette taille.</summary>
    private (long Price, bool Derived) LotPrice(uint pid, long gid, int lotSize, IReadOnlyList<BidHouseStore.Listing> mine)
    {
        int slot = BidHouseStore.SlotOf(lotSize);
        long market = _launch.Hdv.MarketFor(pid, gid, slot);
        long sanity = SanityFloor(gid, lotSize);
        if (sanity <= 0) return (-1, false);
        if (market > 0 && _launch.HdvSalePriceRefusal(pid, gid, lotSize, Math.Max(market, sanity)) is not null)
            return (-1, false);
        if (market > 0)
        {
            if (mine.Any(l => l.Gid == gid && BidHouseStore.SlotOf(l.Qty) == slot && l.Price == market)) return (Math.Max(market, sanity), false);
            long? floorPrice = HdvSalePriceMath.MarketPercentageFloor(market, FloorPercent);
            if (floorPrice is not long floor) return (-1, false);
            long p = Math.Max(Math.Max(1, floor), market - Math.Max(1, PriceStep));
            return (Math.Max(p, sanity), false);
        }
        // Aucun concurrent : unité de la taille connue la plus proche.
        var prices = _launch.Hdv.MarketPricesFor(pid, gid);
        int[] sizes = { 1, 10, 100, 1000 };
        decimal? unit = null; double bestDist = double.MaxValue; int bestSize = 0;
        for (int i = 0; i < prices.Length && i < sizes.Length; i++)
        {
            if (prices[i] <= 0 || sizes[i] == lotSize) continue;
            double d = Math.Abs(Math.Log10((double)sizes[i] / lotSize));
            if (d < bestDist - 1e-9 || (Math.Abs(d - bestDist) < 1e-9 && sizes[i] > bestSize)) { bestDist = d; bestSize = sizes[i]; unit = (decimal)prices[i] / sizes[i]; }
        }
        long avg = AvgUnit(gid);
        decimal u;
        if (avg > 0) u = Math.Clamp(unit ?? avg, 0.8m * avg, 1.3m * avg);
        else if (unit is decimal uu && bestDist <= 1.0001) u = uu;
        else return (-1, false);
        if (HdvSalePriceMath.DerivedPrice(u, lotSize) is not long derived)
            return (-1, false); // never send a wrapped/saturated calculated price
        long candidate = Math.Max(derived, sanity);
        return _launch.HdvSalePriceRefusal(pid, gid, lotSize, candidate) is null
            ? (candidate, true) : (-1, false);
    }

    // Lots RÉELS d'un objet : piles fraîches (sac + banque), du plus grand lot au plus petit, prix calculés sur le marché.
    private List<(int Uid, int Size, long Price, bool Derived)> LotsFor(uint pid, long gid, int maxLots, IReadOnlyList<BidHouseStore.Listing> mine)
    {
        var lots = new List<(int, int, long, bool)>();
        if (maxLots <= 0) return lots;
        var sizes = SizesDesc();
        var priceBySize = new Dictionary<int, (long, bool)>();
        (long, bool) PriceOf(int s) => priceBySize.TryGetValue(s, out var v) ? v : priceBySize[s] = LotPrice(pid, gid, s, mine);
        var stacks = _launch.HdvMergedItems(pid)
            .Where(x => x.Item.Gid == gid && (x.InBank || x.Item.Position == 63) && _launch.ItemCanTrade(pid, x.Item.Uid))
            .Select(x => x.Item).GroupBy(i => i.Uid).Select(g => g.First()).OrderByDescending(i => i.Qty).ToList();
        foreach (var st in stacks)
        {
            int left = st.Qty;
            while (lots.Count < maxLots)
            {
                int pick = 0; long price = -1; bool derived = false;
                foreach (var s in sizes)
                {
                    if (s > left) continue;
                    long quote = _launch.Hdv.MarketFor(pid, gid, BidHouseStore.SlotOf(s));
                    long sanity = SanityFloor(gid, s);
                    if (sanity <= 0) return new(); // cannot quote a safe REAL floor
                    if (quote > 0 && _launch.HdvSalePriceRefusal(pid, gid, s,
                        Math.Max(quote, sanity)) is not null)
                        return new(); // Skip the item, not merely this lot size.
                    var (p, d) = PriceOf(s);
                    if (p > 0) { pick = s; price = p; derived = d; break; }
                }
                if (pick == 0) break;
                lots.Add((st.Uid, pick, price, derived));
                left -= pick;
            }
            if (lots.Count >= maxLots) break;
        }
        return lots;
    }

    // ---------------- lancement ----------------
    [RelayCommand]
    private void StartSell() => _ = RunSellAsync();

    [RelayCommand]
    private void CancelBulk()
    {
        try { _bulkCts?.Cancel(); } catch (ObjectDisposedException) { /* passage déjà terminé */ }
    }

    private async Task RunSellAsync()
    {
        uint pid = _pid;   // figé AVANT tout await : un changement de bot ne vend jamais la sélection chez l'autre
        string? charName = _charName;
        if (pid == 0 || !CanStartSell) return;
        if (!LicenseService.CanBot) { Warn("L.LicLink.Paused"); return; }
        var rows = OrderForRun(SellRows.Where(r => r.Checked)).ToList();
        if (rows.Count == 0) { Warn("L.Bots.Hdv.Bulk.NothingChecked"); return; }

        var cts = new CancellationTokenSource();
        _bulkCts = cts;
        int total = Math.Max(1, PreviewLots);
        foreach (var r in SellRows) { r.State = HdvSellRowState.Idle; r.LotsDone = 0; r.LotsPlanned = 0; r.ListedValue = 0; r.StateText = ""; r.LastError = null; }
        BulkRunning = true; SellStage = 1;   // BulkRunning AVANT tout await : un 2e clic pendant l'ouverture ne relance rien
        BulkDone = 0; BulkTotal = total; RunListedValue = 0; RunSummaryText = ""; CurrentItemText = "";
        _launch.SetHdvAuto(pid, true);
        int ok = 0, fails = 0, consecutive = 0, noPrice = 0, notReached = 0;
        int pauseEveryMin = Math.Clamp(BulkPauseEveryMin, 1, 10000);
        int pauseEveryMax = Math.Clamp(BulkPauseEveryMax, pauseEveryMin, 10000);
        int nextPause = BulkPauses ? RandDelay(pauseEveryMin, pauseEveryMax) : int.MaxValue;
        string stop = "";
        try
        {
            if (!IsHdvOpen && !await EnsureSellerModeAsync(cts.Token))
            {
                stop = cts.IsCancellationRequested ? Loc.T("L.Bots.Hdv.Bulk.Canceled") : Loc.T("L.Bots.Hdv.TravelOpenFail");
                notReached = rows.Count;
            }
            else for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                string? why = StopReason(pid, cts);
                if (why is not null) { stop = why; notReached = rows.Count - i; break; }
                int free = _launch.HdvFreeSlots(pid);
                int maxLots = Math.Min(MaxLotsFor(), free >= 0 ? free : int.MaxValue);
                if (maxLots <= 0) { stop = Loc.T("L.Bots.Hdv.Bulk.SlotsFull"); notReached = rows.Count - i; break; }
                if (!_launch.HdvAcceptsGid(pid, row.Gid)) { row.State = HdvSellRowState.Skipped; row.StateText = Loc.T("L.Bots.Hdv.Sell.Refused"); continue; }

                // 1) prix du marché de CET objet, juste avant de le vendre (jamais un prix vieux de plusieurs minutes)
                row.State = HdvSellRowState.Pricing;
                CurrentItemText = Loc.Fmt("L.Bots.Hdv.Sell.PricingFmt", row.Name, i + 1, rows.Count);
                if (_launch.Hdv.PriceAgeMs(pid, row.Gid) > 60_000)
                {
                    await Gated(async () => { await _launch.HdvGetMarketPriceAsync(pid, row.Gid, cts.Token); return true; }, cts.Token);
                    MarkFetch(row.Gid);
                    await Task.Delay(RandDelay(150, 450), cts.Token);
                }
                // Prix pas reçu (délai dépassé) : aucune vente sur un prix en cache périmé.
                if (_launch.Hdv.PriceAgeMs(pid, row.Gid) > 90_000) { row.State = HdvSellRowState.Skipped; row.StateText = Loc.T("L.Bots.Hdv.Sell.NoPrice"); noPrice++; continue; }

                // 2) lots réels (piles fraîches, du plus grand au plus petit) et vente immédiate
                var lots = LotsFor(pid, row.Gid, maxLots, _launch.Hdv.MyListings(pid));
                if (lots.Count == 0)
                {
                    string? refusal = SizesDesc().Where(size => size <= row.MaxStack
                        && _launch.Hdv.MarketFor(pid, row.Gid, BidHouseStore.SlotOf(size)) > 0).Select(size =>
                        _launch.HdvSalePriceRefusal(pid, row.Gid, size,
                            _launch.Hdv.MarketFor(pid, row.Gid, BidHouseStore.SlotOf(size))))
                        .FirstOrDefault(reason => reason is not null);
                    row.State = HdvSellRowState.Skipped;
                    row.StateText = refusal ?? Loc.T("L.Bots.Hdv.Sell.NoPrice");
                    row.LastError = refusal;
                    if (refusal is not null) Log(row.Name + " — " + refusal, LogWarn);
                    noPrice++; continue;
                }
                row.State = HdvSellRowState.Selling; row.LotsPlanned = lots.Count;
                int rowFails = 0;
                foreach (var lot in lots)
                {
                    why = StopReason(pid, cts);
                    if (why is not null) { stop = why; break; }
                    // The shared reservation ledger covers manual, bulk AND
                    // watcher operations, not just this bulk iteration.
                    // Its booking must occur under Gated immediately before
                    // a possible send; no global kamas delta can release it.
                    if (_launch.HdvFreeSlots(pid) == 0) { stop = Loc.T("L.Bots.Hdv.Bulk.SlotsFull"); break; }
                    CurrentItemText = Loc.Fmt("L.Bots.Hdv.Bulk.SellingFmt", row.Name, "x" + lot.Size, row.LotsDone + 1, lots.Count);
                    long before = _launch.Kamas(pid);
                    bool missing = false;
                    var taxDecision = TaxReservationDecision.Allowed;
                    _launch.HdvResetSellError(pid);
                    bool sold = await Gated(async () =>
                    {
                        // La pile a pu bouger (lot précédent, script Lua) : vérifié SOUS le verrou moteur, jamais une quantité absente.
                        if (_pid != pid || cts.IsCancellationRequested) return false;
                        int have = _launch.HdvSellableQuantity(pid, lot.Uid);
                        if (have < lot.Size) { missing = true; return false; }
                        if (!_launch.HdvMergedItems(pid).Any(x =>
                            x.Item.Uid == lot.Uid && x.Item.Gid == row.Gid &&
                            x.Item.Position == 63 && x.Item.Qty >= lot.Size &&
                            _launch.ItemCanTrade(pid, lot.Uid)))
                        { missing = true; return false; }
                        if (_launch.HdvSalePriceRefusal(pid, row.Gid, lot.Size, lot.Price) is not null)
                            return false;
                        var outcome = await ExecuteTaxedMutationAsync(pid, lot.Price,
                            SellTaxOrder(lot.Uid, lot.Size, lot.Price, have),
                            () => _launch.HdvSellAsync(pid, lot.Uid, lot.Size, lot.Price, cts.Token));
                        taxDecision = outcome.Decision;
                        return outcome.Ok;
                    }, cts.Token);
                    if (taxDecision != TaxReservationDecision.Allowed)
                    {
                        stop = TaxReservationReason(taxDecision);
                        break;
                    }
                    if (missing) continue;
                    if (sold)
                    {
                        ok++; consecutive = 0; BulkDone++;
                        if (BulkDone > BulkTotal) BulkTotal = BulkDone;
                        row.LotsDone++;
                        row.ListedValue = HdvSellPreviewMath.AddValue(row.ListedValue, lot.Price);
                        RunListedValue = HdvSellPreviewMath.AddValue(RunListedValue, lot.Price);
                        Log(Loc.Fmt("L.Bots.Hdv.LogListed", row.Name, "x" + lot.Size, N(lot.Price)) + (lot.Derived ? " " + Loc.T("L.Bots.Hdv.Sell.DerivedTag") : ""), LogOk);
                        _ = RecordTaxAsync(pid, charName, before, row.Gid, row.Name, lot.Size, Loc.T("L.Bots.Hdv.ActSell"));
                    }
                    else
                    {
                        fails++; consecutive++; rowFails++;
                        string reason = _launch.HdvLastSellError(pid) ?? Loc.T("L.Bots.Hdv.Why.Timeout");
                        Log(Loc.Fmt("L.Bots.Hdv.Bulk.LotFailFmt", row.Name, "x" + lot.Size) + " — " + reason, LogErr);
                        row.LastError = reason;
                        if (consecutive >= 3) { stop = Loc.Fmt("L.Bots.Hdv.Bulk.TooManyFailsFmt", reason); break; }
                        if (rowFails >= 2) break;   // objet refusé deux fois : on passe au suivant
                    }
                    // Cadence : si 0 ms est configuré (ou lots du même objet en rafale), aucun délai artificiel (départ en rafale immédiate)
                    if (BulkDelayMaxMs <= 0)
                    {
                        // 0 ms demandé : aucun délai entre la vente des lots
                    }
                    else if (BulkPauses && BulkDone >= nextPause)
                    {
                        nextPause = nextPause > int.MaxValue - pauseEveryMax ? int.MaxValue : nextPause + RandDelay(pauseEveryMin, pauseEveryMax);
                        int minSec = Math.Clamp(BulkPauseMinSec, 0, 3600);
                        int maxSec = Math.Clamp(BulkPauseMaxSec, minSec, 3600);
                        await Task.Delay(RandDelay(minSec * 1000, maxSec * 1000), cts.Token);
                    }
                    else
                    {
                        var (minMs, maxMs) = HdvPacing.Resolve(BulkDelayMinMs, BulkDelayMaxMs);
                        if (maxMs > 0) await Task.Delay(RandDelay(minMs, maxMs), cts.Token);
                    }
                }
                row.State = row.LotsDone > 0 ? HdvSellRowState.Done : HdvSellRowState.Failed;
                row.StateText = row.LotsDone > 0 ? N(row.ListedValue) : row.LastError ?? Loc.T("L.Bots.Hdv.Sell.Refused");
                if (stop.Length > 0) { notReached = rows.Count - i - 1; break; }
            }
        }
        catch (OperationCanceledException) { stop = Loc.T("L.Bots.Hdv.Bulk.Canceled"); }
        finally
        {
            foreach (var r in SellRows) if (r.IsBusy) { r.State = HdvSellRowState.Idle; r.StateText = ""; }
            BulkRunning = false; SellStage = 2;
            CurrentItemText = "";
            if (!WatcherRunning) _launch.SetHdvAuto(pid, false);
            if (ReferenceEquals(_bulkCts, cts)) _bulkCts = null;
            cts.Dispose();
            OnPropertyChanged(nameof(FreeSlotsText));
        }
        string summary = Loc.Fmt("L.Bots.Hdv.Sell.DoneFmt", ok, N(RunListedValue), fails, noPrice, notReached);
        RunSummaryText = stop.Length > 0 ? summary + " — " + stop : summary;
        Log(RunSummaryText, fails > 0 || stop.Length > 0 ? LogWarn : LogOk);
        Toasts.Show(Loc.Fmt("L.Bots.Hdv.Sell.ToastFmt", ok, N(RunListedValue)), success: ok > 0);
    }

    // Raison d'arrêt avant une nouvelle action (null = on continue).
    private string? StopReason(uint pid, CancellationTokenSource cts)
    {
        if (cts.IsCancellationRequested || pid != _pid) return Loc.T("L.Bots.Hdv.Bulk.Canceled");
        if (!IsHdvOpen) return Loc.T("L.Bots.Hdv.ClosedStop");
        if (!LicenseService.CanBot || !CanHdv) return Loc.T("L.LicLink.Paused");
        if (MaxTaxSpend > 0 && TotalTax >= MaxTaxSpend) return Loc.T("L.Bots.Hdv.TaxCapHit");
        return null;
    }

    /// <summary>Nouveau passage depuis l'écran de fin : retour au choix, lignes remises à jour (quantités restantes).</summary>
    [RelayCommand]
    private void SellAgain()
    {
        if (BulkRunning) return;
        SellStage = 0;
        RefreshSellRows(force: true);
    }
}
