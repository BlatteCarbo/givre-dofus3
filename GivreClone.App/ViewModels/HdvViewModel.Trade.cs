using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Eco;
using GivreClone.App.Services.LotM;
using GivreClone.App.Services.Production;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

// Onglet HDV : achat, relist, scan (MZP23). Les NOMS des membres sont ceux de l'original v1.3.1 ; la logique de calcul est dans
// HdvTradeLogic (testée), l'exécution d'achat dans HdvBuyRunner (testée avec un port factice). Ce qui n'a PAS de source de données dans
// le clone (recettes, relevé de marché multi-objets) reste explicitement indisponible : aucune donnée n'est inventée.
public partial class HdvViewModel
{
    private static readonly int[] TradeLots = { 1, 10, 100, 1000 };
    private const string Unavailable = "Indisponible dans le clone : source de recettes / relevé de marché non branchée.";

    // ---- Listes ----
    public ObservableCollection<HdvBuyRow> BuyRows { get; } = new();
    [ObservableProperty] public partial HdvBuyRow? SelectedBuyRow { get; set; }
    public ObservableCollection<HdvChosenOfferRow> BuyOffers { get; } = new();
    public bool HasBuyOffers => BuyOffers.Count > 0;
    public ObservableCollection<HdvRelistRow> RelistRows { get; } = new();
    public ObservableCollection<HdvScanRow> ScanRows { get; } = new();
    public ObservableCollection<HdvCraftStep> CraftSteps { get; } = new();
    public ObservableCollection<HdvItemPick> BuySuggestions { get; } = new();
    public ObservableCollection<HdvItemPick> RelistSuggestions { get; } = new();
    public ObservableCollection<HdvCatalogPick> ScanSuggestions { get; } = new();
    public ObservableCollection<HdvScanServer> ScanServers { get; } = new();
    public ObservableCollection<HdvScanFilter> ScanTypes { get; } = new();
    public ObservableCollection<HdvScanFilter> ScanJobs { get; } = new();
    [ObservableProperty] public partial HdvScanServer? ScanServer { get; set; }
    [ObservableProperty] public partial HdvScanFilter? ScanType { get; set; }
    [ObservableProperty] public partial HdvScanFilter? ScanJob { get; set; }

    // ---- Modes (libellés choisis ici ; l'ordre = valeur des *Index persistés) ----
    public string[] BuyModes { get; } = { "Ma liste", "Recette" };
    public string[] LotStrategyModes { get; } = { "Plus gros lot possible", "Lot unique", "Tous les lots" };
    public string[] NoCompetitorModes { get; } = { "Garder mon prix", "Prix moyen serveur", "Ne rien faire" };
    public string[] RepriceScopeModes { get; } = { "Tous mes lots", "Les N premiers", "Seulement ceux à repricer" };
    public string[] ScanModes { get; } = { "Revente", "Craft" };
    public string[] ScanSorts { get; } = { "Gain", "Gain %", "Coût", "Nom" };

    // ---- Dérivés ----
    public bool HasBuyRows => BuyRows.Count > 0;
    public bool HasBuySuggestions => BuySuggestions.Count > 0;
    public bool HasBuyNoMatch => !string.IsNullOrEmpty(BuyNoMatchText);
    public bool HasBuyRecipe => !string.IsNullOrEmpty(BuyRecipeName);
    public bool HasBuyTolerance => BuyStrategyIndex == 1;
    public bool ShowNothingToBuy => HasBuyRows && BuyRows.All(r => r.IsCovered);
    public bool CanBuy => CanHdv && _launch.HdvBuyerOpen(_pid);
    public bool CanStartBuy => CanBuy && !BuyRequiresReview && GuidedSupplyBuySourceCurrent() &&
        HasBuyRows && BuyBudget > 0 && !ShowNothingToBuy;
    // Keep the Stop action accessible if a pending mutation is discovered
    // while an existing auto-buy or sniper cycle is still winding down.
    public bool BuyControlEnabled => BuyRunning || !BuyRequiresReview;
    public bool SniperControlEnabled => SniperRunning || !BuyRequiresReview;
    /// <summary>
    /// The status is sourced from the durable, server/character-scoped
    /// pre-send BUY journal, not from a local timer or observed wallet delta.
    /// It intentionally remains visible when the original PID is replaced.
    /// </summary>
    public bool BuyRequiresReview => _pid != 0 && _launch.HdvBuyReviewPending(_pid);
    public string BuyReviewExplanation =>
        "Achat HDV non attribuable : vérifier offres, inventaire ET kamas dans le client officiel. " +
        "Après rapprochement opérateur explicite seulement, autoriser de nouveaux achats. " +
        "L'absence de réponse n'est ni un échec confirmé ni une preuve d'achat.";

    [RelayCommand]
    private void ReconcileBuy()
    {
        uint pid = _pid;
        if (pid == 0 || !BuyRequiresReview || BuyRunning || SniperRunning) return;
        int server = _launch.ServerIdOf(pid);
        long character = _launch.CharacterId(pid);
        if (!SameHdvReviewIdentity(pid, server, character)) return;
        Dialogs.Confirm("Achat HDV à rapprocher",
            BuyReviewExplanation + " Confirmer que le contrôle manuel a été effectué ?",
            onConfirm: () =>
            {
                if (!SameHdvReviewIdentity(pid, server, character) ||
                    BuyRunning || SniperRunning) return;
                if (!_launch.HdvAcknowledgeBuyReview(pid))
                {
                    BuyStatusText = "Rapprochement refusé : ordre encore actif, identité changée ou journal indisponible.";
                    return;
                }
                BuyStatusText = "Rapprochement déclaré. Aucune preuve serveur n'a été fabriquée.";
                RaiseBuy();
                NoteHdvManualReview("achat");
            }, danger: true);
    }
    public string BuyStartText => BuyRunning ? "Arrêter l'achat" : "Lancer l'achat";
    public string BuySpentSessionText => N(BuySpentSession);
    public int BuyRowsToBuy => BuyRows.Count(r => r.ToBuy > 0);
    public long BuyRemainingBudget =>
        BuyBudget <= 0 || BuySpentSession < 0 || BuySpentSession >= BuyBudget
            ? 0 : BuyBudget - BuySpentSession;
    public string BuyRemainingBudgetText => N(BuyRemainingBudget);
    public long BuyEstimatedCost
    {
        get
        {
            long left = BuyRemainingBudget;
            long cost = 0;
            foreach (var row in BuyRows)
            {
                if (left <= 0) break; // Plan treats zero as unlimited; buying does not.
                var quotes = row.Lots.Select(l => new LotQuote(l.Qty, l.Price, l.Allowed, l.Max, MinMissing(l.Qty)));
                var plan = HdvTradeLogic.Plan(row.ToBuy, quotes, left, BuyCheaperMarginPct);
                // If a planner ever returns a malformed/over-budget amount,
                // refuse to produce a plausible-but-wrapped preview.
                if (plan.Cost < 0 || plan.Cost > left ||
                    plan.Cost > long.MaxValue - cost) return long.MaxValue;
                cost += plan.Cost;
                left -= plan.Cost;
            }
            return cost;
        }
    }
    public string BuySummaryText => Loc.Fmt("L.Bots.Hdv.PlanSummary",
        BuyRows.Count, BuyRowsToBuy, N(BuyEstimatedCost), N(BuyRemainingBudget));

    private void AccountConfirmedBuySpend(long spent)
    {
        if (spent < 0)
        {
            // No negative spend may give a larger future buying allowance.
            BuySpentSession = long.MaxValue;
            return;
        }
        long previous = Math.Max(0, BuySpentSession);
        BuySpentSession = spent > long.MaxValue - previous
            ? long.MaxValue : previous + spent;
    }
    public string BuyStartHintText => BuyBudget > 0 ? "" : "Fixe un budget pour pouvoir acheter.";
    public bool HasRelistRules => RelistRows.Count > 0;
    public bool HasRelistSuggestions => RelistSuggestions.Count > 0;
    public bool ShowRelistIdleHint => RelistEnabled && !HasRelistRules;
    /// <summary>Remise en vente active avec des règles mais repricing arrêté : rien ne remplacera les lots vendus.</summary>
    public bool RelistNeedsWatcher => RelistEnabled && HasRelistRules && !WatcherRunning;
    public bool HasScanRows => ScanRows.Count > 0;
    public bool HasScanNoMatch => !string.IsNullOrEmpty(ScanNoMatchText);
    public bool HasScanServers => ScanServers.Count > 0;
    public bool IsScanResell => ScanModeIndex == 0;
    public bool IsScanCraft => ScanModeIndex == 1;
    public bool HasCraftSteps => CraftSteps.Count > 0;
    public bool HasLotTolerance => LotStrategyIndex > 0;
    public bool HasScopeCount => RepriceScopeIndex == 1;
    public bool CanHdvNotify => CanHdv;
    public string ListedValueText => N(Listings.Sum(l => l.Price));
    public string RepriceLossText => LossPerUnit ? "perte max. par unité" : "perte max. par lot";
    public string WatchModeText => WatcherRunning ? "Surveillance active" : "Surveillance arrêtée";

    private bool _tradeWired;
    private CancellationTokenSource? _buyCts;
    private bool _buyStarting;
    private CancellationTokenSource? _sniperCts;
    [ObservableProperty] public partial int BuyCheaperMarginPct { get; set; }
    [ObservableProperty] public partial bool SniperRunning { get; set; }
    [ObservableProperty] public partial string SniperStatusText { get; set; } = "";
    public string SniperStartText => SniperRunning ? "Arrêter le sniper" : "Lancer le sniper";
    partial void OnSniperRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(SniperStartText));
        OnPropertyChanged(nameof(SniperControlEnabled));
    }
    partial void OnBuyCheaperMarginPctChanged(int value)
    {
        int clamped = Math.Clamp(value, 0, 100);
        if (clamped != value) { BuyCheaperMarginPct = clamped; return; }
        if (_loading) return;
        _cfg.Trade.BuyCheaperMarginPct = clamped;
        Save();
        ReplanAllBuyRows();
    }
    private readonly HashSet<HdvBuyRow> _watchedBuyRules = new();
    private readonly HashSet<HdvBuyLot> _watchedBuyLots = new();
    private readonly HashSet<HdvRelistRow> _watchedRelistRules = new();
    // Only one transient deficit rule may be imported from a durable V16 choice.
    // Its quantity is ALREADY net of bag and bank; no second stock deduction.
    // Never persist it as an ordinary rule that could outlive source/revision.
    private HdvBuyRow? _guidedSupplyBuyRow;
    private GuidedSupplyExactChoiceV16? _guidedSupplyBuyChoice;
    private Func<bool>? _guidedSupplyBuySource;

    private bool GuidedSupplyBuySourceCurrent()
    {
        var row = _guidedSupplyBuyRow;
        if (row is null) return true;
        // A UI removal during an already active HdvBuyRunner must revoke
        // subsequent lots, not turn the removed row into unguarded traffic.
        if (!BuyRows.Contains(row)) return !BuyRunning && !_buyStarting;
        if (_guidedSupplyBuyChoice is not { } choice ||
            row.Gid != choice.Gid || row.Needed != choice.Quantity ||
            row.Owned != 0 || _guidedSupplyBuySource is null)
            return false;
        try { return _guidedSupplyBuySource(); }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Stage an exact fsynced V16 InspectBuy deficit in the EXISTING buyer.
    /// No buy request, budget adjustment, lots permission or server evidence.
    /// The callback re-reads the same durable source at every operator send.
    /// </summary>
    public bool TryStageGuidedSupplyBuy(uint pid, int server, long character,
        GuidedSupplyExactChoiceV16 choice, Func<bool> sourceCurrent,
        out string explanation)
    {
        explanation = "source ou journal non admissible";
        var owner = CaptureHdvUiMutationTicket();
        if (pid == 0 || server <= 0 || character <= 0 ||
            choice is not { Valid: true, Kind: GuidedSupplyResumeStepV16.InspectBuy } ||
            !choice.HasExactPodsEvidenceV18 ||
            choice.Quantity > int.MaxValue || choice.Gid > int.MaxValue ||
            sourceCurrent is null ||
            owner.Pid != pid || owner.ServerId != server ||
            owner.CharacterId != character || !IsCurrentHdvUiMutationTicket(owner) ||
            BuyRunning || _buyStarting || SniperRunning || BuyRequiresReview ||
            !SafeGuidedBuySource(sourceCurrent))
            return false;
        if (_guidedSupplyBuyRow is { } prior && BuyRows.Contains(prior))
        {
            if (_guidedSupplyBuyChoice != choice || prior.Needed != choice.Quantity ||
                prior.Owned != 0 || !GuidedSupplyBuySourceCurrent())
            {
                explanation = "une instruction guidée différente ou modifiée existe déjà";
                return false;
            }
            SelectedBuyRow = prior;
            ShowBuyView();
            explanation = "instruction GID " + choice.Gid + " ×" + choice.Quantity +
                " déjà présente, sans deuxième ligne";
            return true;
        }
        if (BuyRows.Any(row => row.Gid == choice.Gid))
        {
            explanation = "une règle manuelle sur le même GID existe : rapprochement requis";
            return false;
        }

        // The V13 deficit is (Required - observed bag - planned bank), not
        // gross required stock. Even with BuyDeductStock enabled, keep Owned=0
        // for this one transient row until the operator explicitly removes it.
        var staged = NewBuyRow(choice.Gid, _launch.ItemDisplayName(choice.Gid),
            checked((int)choice.Quantity));
        staged.Owned = 0;
        staged.StateText = "V17 · déficit net guidé ; achat manuel, revalidation source";
        _guidedSupplyBuyRow = staged;
        _guidedSupplyBuyChoice = choice;
        _guidedSupplyBuySource = sourceCurrent;
        BuyRows.Insert(0, staged);
        if (!IsCurrentHdvUiMutationTicket(owner) || !GuidedSupplyBuySourceCurrent())
        {
            BuyRows.Remove(staged);
            _guidedSupplyBuyRow = null;
            _guidedSupplyBuyChoice = null;
            _guidedSupplyBuySource = null;
            explanation = "identité ou révision remplacée durant la préparation";
            return false;
        }
        SelectedBuyRow = staged;
        Replan(staged);
        ShowBuyView();
        RaiseBuy();
        explanation = "ligne d'achat GID " + choice.Gid + " ×" + choice.Quantity +
            " préparée depuis le déficit net (non persistée)";
        return true;
    }

    private static bool SafeGuidedBuySource(Func<bool> source)
    {
        try { return source(); }
        catch (Exception) { return false; }
    }

    private void WireTrade()
    {
        if (_tradeWired) return;
        _tradeWired = true;
        BuyRows.CollectionChanged += (_, _) => { SyncRuleSubscriptions(); RaiseBuy(); SaveTradeRules(); };
        BuyOffers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasBuyOffers));
        RelistRows.CollectionChanged += (_, _) => { SyncRuleSubscriptions(); OnPropertyChanged(nameof(HasRelistRules)); OnPropertyChanged(nameof(ShowRelistIdleHint)); OnPropertyChanged(nameof(RelistNeedsWatcher)); SaveTradeRules(); };
        ScanRows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasScanRows));
        CraftSteps.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasCraftSteps));
        BuySuggestions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasBuySuggestions));
        RelistSuggestions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRelistSuggestions));
        ScanServers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasScanServers));
    }

    private void SyncRuleSubscriptions()
    {
        foreach (var row in _watchedBuyRules.Where(r => !BuyRows.Contains(r)).ToArray())
        { row.PropertyChanged -= OnBuyRuleChanged; _watchedBuyRules.Remove(row); }
        foreach (var row in BuyRows.Where(r => _watchedBuyRules.Add(r))) row.PropertyChanged += OnBuyRuleChanged;
        var allLots = BuyRows.SelectMany(r => r.Lots).ToHashSet();
        foreach (var lot in _watchedBuyLots.Where(l => !allLots.Contains(l)).ToArray())
        { lot.PropertyChanged -= OnBuyLotRuleChanged; _watchedBuyLots.Remove(lot); }
        foreach (var lot in allLots.Where(l => _watchedBuyLots.Add(l))) lot.PropertyChanged += OnBuyLotRuleChanged;
        foreach (var row in _watchedRelistRules.Where(r => !RelistRows.Contains(r)).ToArray())
        { row.PropertyChanged -= OnRelistRuleChanged; _watchedRelistRules.Remove(row); }
        foreach (var row in RelistRows.Where(r => _watchedRelistRules.Add(r))) row.PropertyChanged += OnRelistRuleChanged;
    }

    private void OnBuyRuleChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaiseBuy();
        if (e.PropertyName == nameof(HdvBuyRow.Needed))
        {
            if (sender is HdvBuyRow row) Replan(row);
            SaveTradeRules();
        }
    }

    private void OnBuyLotRuleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HdvBuyLot.Allowed) or nameof(HdvBuyLot.Max))
        {
            foreach (var row in BuyRows) Replan(row);
            RaiseBuy();
            SaveTradeRules();
        }
    }

    private void OnRelistRuleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HdvRelistRow.MinPrice) or nameof(HdvRelistRow.Keep) or nameof(HdvRelistRow.Target) or nameof(HdvRelistRow.Qty)) SaveTradeRules();
    }

    private void SaveTradeRules()
    {
        if (_loading || string.IsNullOrWhiteSpace(_charName)) return;
        _cfg.Trade.BuyRules = BuyRows.Where(r => r.Gid > 0 && r.Needed > 0 &&
            !ReferenceEquals(r, _guidedSupplyBuyRow)).Select(r => new HdvBuyRule
        {
            Gid = r.Gid, Needed = r.Needed,
            Lots = r.Lots.Select(l => new HdvBuyLotRule { Qty = l.Qty, Allowed = l.Allowed, Max = Math.Max(0, l.Max) }).ToList()
        }).ToList();
        _cfg.Trade.RelistRules = RelistRows.Where(r => r.Gid > 0 && r.Qty > 0).Select(r => new HdvRelistRule
        {
            Gid = r.Gid, Qty = r.Qty, MinPrice = Math.Max(0, r.MinPrice), Keep = r.Keep, Target = Math.Clamp(r.Target, 1, 100)
        }).ToList();
        Save();
    }

    private void RestoreTradeRules()
    {
        _sniperCts?.Cancel();
        BuyCheaperMarginPct = Math.Clamp(_cfg.Trade.BuyCheaperMarginPct, 0, 100);
        foreach (var rule in _cfg.Trade.BuyRules ?? new())
        {
            if (rule.Gid <= 0 || rule.Needed <= 0 || BuyRows.Any(r => r.Gid == rule.Gid)) continue;
            var row = NewBuyRow(rule.Gid, _launch.ItemDisplayName(rule.Gid), rule.Needed);
            foreach (var lot in row.Lots)
                if (rule.Lots?.FirstOrDefault(x => x.Qty == lot.Qty) is { } saved)
                { lot.Allowed = saved.Allowed; lot.Max = Math.Max(0, saved.Max); }
            Replan(row);
            BuyRows.Add(row);
        }
        foreach (var rule in _cfg.Trade.RelistRules ?? new())
        {
            if (rule.Gid <= 0 || rule.Qty is not (1 or 10 or 100 or 1000) ||
                RelistRows.Any(r => r.Gid == rule.Gid)) continue;
            RelistRows.Add(new HdvRelistRow
            {
                Gid = rule.Gid, Name = _launch.ItemDisplayName(rule.Gid), Icon = IconFor(rule.Gid),
                Qty = rule.Qty, MinPrice = Math.Max(0, rule.MinPrice), Keep = rule.Keep, Target = Math.Clamp(rule.Target, 1, 100)
            });
        }
    }

    private void RaiseBuy()
    {
        foreach (var n in new[] { nameof(BuyRequiresReview), nameof(BuyControlEnabled),
                                 nameof(SniperControlEnabled), nameof(HasBuyRows), nameof(ShowNothingToBuy), nameof(CanStartBuy), nameof(BuySummaryText),
                                 nameof(BuyRowsToBuy), nameof(BuyEstimatedCost), nameof(BuyRemainingBudget), nameof(BuyRemainingBudgetText), nameof(CanBuy) })
            OnPropertyChanged(n);
    }

    partial void OnBuyBudgetChanged(long value) { RaiseBuy(); OnPropertyChanged(nameof(BuyStartHintText)); }
    partial void OnBuyRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(BuyStartText));
        OnPropertyChanged(nameof(BuyControlEnabled));
    }
    partial void OnBuySpentSessionChanged(long value) { OnPropertyChanged(nameof(BuySpentSessionText)); RaiseBuy(); }
    partial void OnBuyNoMatchTextChanged(string value) => OnPropertyChanged(nameof(HasBuyNoMatch));
    partial void OnBuyRecipeNameChanged(string value) => OnPropertyChanged(nameof(HasBuyRecipe));
    partial void OnBuyStrategyIndexChanged(int value) => OnPropertyChanged(nameof(HasBuyTolerance));
    partial void OnScanNoMatchTextChanged(string value) => OnPropertyChanged(nameof(HasScanNoMatch));
    partial void OnScanModeIndexChanged(int value) { OnPropertyChanged(nameof(IsScanResell)); OnPropertyChanged(nameof(IsScanCraft)); }
    partial void OnLotStrategyIndexChanged(int value) => OnPropertyChanged(nameof(HasLotTolerance));
    partial void OnRepriceScopeIndexChanged(int value) => OnPropertyChanged(nameof(HasScopeCount));
    partial void OnLossPerUnitChanged(bool value) => OnPropertyChanged(nameof(RepriceLossText));
    partial void OnRelistEnabledChanged(bool value) { OnPropertyChanged(nameof(ShowRelistIdleHint)); OnPropertyChanged(nameof(RelistNeedsWatcher)); }

    // ---- Suggestions par nom (catalogue d'objets) ----
    partial void OnBuyQueryChanged(string value) => SuggestAsync(value, () => BuyQuery, BuySuggestions, hit => BuyNoMatchText = hit ? "" : "Aucun objet ne correspond.");
    partial void OnRelistQueryChanged(string value) => SuggestAsync(value, () => RelistQuery, RelistSuggestions, null);

    private void SuggestAsync(string query, Func<string> currentQuery, ObservableCollection<HdvItemPick> target, Action<bool>? setMatch)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2) { target.Clear(); setMatch?.Invoke(true); return; }
        _ = Task.Run(() =>
        {
            var hits = _launch.SearchItems(query, 8);
            Dispatcher.UIThread.Post(() =>
            {
                if (currentQuery() != query) return;   // frappe plus récente : cette réponse est périmée
                target.Clear();
                foreach (var (gid, name, _) in hits)
                    target.Add(new HdvItemPick
                    {
                        Gid = gid, Name = name, Icon = IconFor(gid), Owned = _launch.Inventory.CountByGid(_pid, gid),
                        Sizes = TradeLots.Select(s => new HdvSizePick { Size = s, Active = SizeEnabled(s) }).ToList(),
                    });
                setMatch?.Invoke(hits.Count > 0);
            });
        });
    }

    private bool SizeEnabled(int size) => size switch { 1 => BuyT1, 10 => BuyT10, 100 => BuyT100, _ => BuyT1000 };
    /// <summary>Seuil d'achat d'une taille de lot (réglages « Lots de N s'il en manque au moins »).</summary>
    private int MinMissing(int size) => size switch { 1 => BuyMin1, 10 => BuyMin10, 100 => BuyMin100, _ => BuyMin1000 };

    // ---- Liste d'achat ----
    private HdvBuyRow NewBuyRow(long gid, string name, int needed)
    {
        int owned = BuyDeductStock ? _launch.Inventory.CountByGid(_pid, gid) : 0;
        var row = new HdvBuyRow
        {
            TooltipPid = (int)_pid,
            Gid = gid, Name = name, Icon = IconFor(gid), Level = Math.Max(0, _launch.ItemLevel((int)gid)),
            Lots = TradeLots.Select(s => new HdvBuyLot { Qty = s, Allowed = SizeEnabled(s) }).ToList(),
            Needed = Math.Max(1, needed), Owned = owned,
        };
        return row;
    }

    /// <summary>Seuils d'achat modifiés : le plan (lots, coût) de chaque ligne suit.</summary>
    private void ReplanAllBuyRows() { foreach (var row in BuyRows) Replan(row); RaiseBuy(); }

    // InventoryContentEvent and buyer offers already raise HdvUpdated. Keep the
    // existing rows/suggestions in sync instead of freezing their creation snapshot.
    private void RefreshBuyLive()
    {
        foreach (var pick in BuySuggestions.Concat(RelistSuggestions))
            pick.Owned = _launch.Inventory.CountByGid(_pid, pick.Gid);
        foreach (var row in BuyRows)
        {
            // Bought is already included in the server's bag count; coverage adds
            // it separately, so Owned represents the stock outside those purchases.
            row.Owned = ReferenceEquals(row, _guidedSupplyBuyRow) ? 0 :
                BuyDeductStock ? Math.Max(0, _launch.Inventory.CountByGid(_pid, row.Gid) - row.Bought) : 0;
            foreach (var lot in row.Lots)
            {
                long price = _launch.Hdv.OfferPriceFor(_pid, row.Gid, BidHouseStore.SlotOf(lot.Qty));
                // Searches invalidate the transport offer before requesting a fresh
                // one. Retain the last observed quote for the bulk price action.
                if (price > 0) lot.Price = price;
            }
            Replan(row);
        }
        RaiseBuy();
    }

    private void Replan(HdvBuyRow r)
    {
        var quotes = r.Lots.Select(l => new LotQuote(l.Qty, l.Price, l.Allowed, l.Max, MinMissing(l.Qty)));
        var plan = BuyCheaperMarginPct > 0
            ? HdvTradeLogic.Plan(r.ToBuy, quotes, BuyBudget, BuyCheaperMarginPct)
            : HdvTradeLogic.Plan(r.ToBuy, quotes, BuyBudget);
        r.PlanText = plan.Text;
        r.PlanCost = plan.Cost;
    }

    [RelayCommand]
    private void AddBuyItem(HdvItemPick? pick)
    {
        if (pick is null || BuyRows.Any(r => r.Gid == pick.Gid)) return;
        var row = NewBuyRow(pick.Gid, pick.Name, 1);
        BuyRows.Insert(0, row);
        SelectedBuyRow = row;
        BuyQuery = ""; BuySuggestions.Clear();
    }

    [RelayCommand] private void RemoveBuyRow(HdvBuyRow? row) { if (row is not null) BuyRows.Remove(row); }
    [RelayCommand] private void ClearBuyList() => BuyRows.Clear();
    [RelayCommand] private void ToggleBuyLot(HdvBuyLot? lot) { if (lot is not null) { lot.Allowed = !lot.Allowed; foreach (var r in BuyRows) Replan(r); } }
    [RelayCommand] private void ToggleLossUnit() => LossPerUnit = !LossPerUnit;

    private static int ParsePct(string? s, int fallback = 100) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    [RelayCommand]
    private void ScaleBuyMax()
    {
        foreach (var r in BuyRows) foreach (var l in r.Lots) l.Max = HdvTradeLogic.MaxFromPrice(l.Max, BuyTolerancePct);
        foreach (var r in BuyRows) Replan(r);
    }

    [RelayCommand]
    private void FillBuyMaxFromAvg()
    {
        foreach (var r in BuyRows)
        {
            long avg = _launch.AveragePrice(_pid, (int)r.Gid);
            foreach (var l in r.Lots) l.Max = HdvTradeLogic.MaxFromAverage(avg, l.Qty, BuyTolerancePct);
            Replan(r);
        }
    }

    [RelayCommand]
    private void FillBuyMaxFromPrices()
    {
        RefreshBuyLive();
        foreach (var r in BuyRows)
        {
            foreach (var l in r.Lots)
                if (l.Price > 0) l.Max = HdvTradeLogic.MaxFromPrice(l.Price, BuyTolerancePct);
            Replan(r);
        }
    }

    // ---- Prix (lecture seule) ----
    private async Task FetchRowPricesAsync(HdvBuyRow r, CancellationToken ct)
    {
        foreach (var l in r.Lots.Where(l => l.Allowed))
        {
            long p = await _launch.HdvGetLotPriceAsync(_pid, r.Gid, l.Qty, ct).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => { l.Price = p > 0 ? p : 0; });
        }
        await Dispatcher.UIThread.InvokeAsync(() => Replan(r));
    }

    [RelayCommand]
    private async Task FetchBuyRowPrice(HdvBuyRow? row)
    {
        if (row is null) return;
        if (!CanBuy) { BuyStatusText = "Ouvre l'HDV en mode achat pour lire les prix."; return; }
        row.IsBusy = true;
        try { await FetchRowPricesAsync(row, CancellationToken.None); } finally { row.IsBusy = false; }
    }

    [RelayCommand]
    private async Task FetchBuyPrices()
    {
        if (!CanBuy) { BuyStatusText = "Ouvre l'HDV en mode achat pour lire les prix."; return; }
        await FetchBuyPricesAsync(r => FetchBuyRowPrice(r));
    }

    [ObservableProperty] public partial int BuyPricesCompleted { get; set; }
    [ObservableProperty] public partial int BuyPricesTotal { get; set; }
    [ObservableProperty] public partial int BuyPricesFound { get; set; }

    private async Task FetchBuyPricesAsync(Func<HdvBuyRow, Task> fetchRow)
    {
        var rows = BuyRows.ToArray();
        BuyPricesCompleted = 0;
        BuyPricesTotal = rows.Length;
        BuyPricesFound = 0;
        BuySearching = true;
        try
        {
            BuyStatusText = Loc.Fmt("L.Bots.Hdv.PriceProgress", 0, BuyPricesTotal);
            foreach (var row in rows)
            {
                await fetchRow(row);
                RaiseBuy();
                BuyPricesCompleted++;
                if (row.Lots.Any(l => l.Allowed && l.Price > 0)) BuyPricesFound++;
                BuyStatusText = Loc.Fmt("L.Bots.Hdv.PriceProgress", BuyPricesCompleted, BuyPricesTotal);
            }
            BuyStatusText = Loc.Fmt("L.Bots.Hdv.PriceSummary", BuyPricesFound, BuyPricesTotal - BuyPricesFound);
        }
        finally { BuySearching = false; }
    }

    [RelayCommand]
    private async Task RequestPrices()
    {
        if (!IsHdvOpen) { StatusText = "Ouvre l'HDV pour lire les prix du marché."; return; }
        Busy = true;
        try { foreach (var g in Listings.Select(l => l.Gid).Distinct().ToList()) await _launch.HdvGetMarketPriceAsync(_pid, g); }
        finally { Busy = false; RefreshLive(); }
    }

    // ---- Exécution de l'achat ----
    private sealed class LaunchBuyPort(LaunchService launch, uint pid,
        Func<bool> sourceCurrent) : IHdvBuyPort
    {
        public Task<long> LotPriceAsync(long gid, int qty, CancellationToken ct) =>
            sourceCurrent() ? launch.HdvGetLotPriceAsync(pid, gid, qty, ct) : Task.FromResult(0L);
        public Task<bool> BuyAsync(long gid, int qty, long maxPrice, CancellationToken ct) =>
            sourceCurrent() ? launch.HdvBuyByGidAsync(pid, gid, qty, maxPrice, ct) : Task.FromResult(false);
    }

    /// <summary>Comme la vente : rejoint l'HDV cible et l'ouvre en mode achat. Le bouton « Annuler » arrête le trajet.</summary>
    private async Task<bool> EnsureBuyerModeAsync()
    {
        if (CanBuy) return true;
        uint pid = _pid;
        if (_launch.HdvAnyOpen(pid))
        {
            long map = _launch.CurrentMap(pid) ?? 0;
            bool switched = map != 0 && await _launch.HdvOpenBuyerAsync(pid, map);
            if (!switched) BuyStatusText = "Bascule de l'HDV en mode achat échouée.";
            OnPropertyChanged(nameof(CanBuy));
            return switched;
        }
        if (SelectedLocation is not { } location) { BuyStatusText = "Choisis l'HDV cible."; return false; }
        if (Traveling) return false;
        Traveling = true;
        BuyStatusText = $"Trajet vers {location.Name}…";
        var host = new GivreLuaHost(pid, _launch, Stores.World);
        void OnHostLog(LuaLogEntry entry)
        {
            GivreLauncher.Core.Transport.TransportLog.Line($"[hdv-trajet] {entry.Category} : {entry.Message}");
            Dispatcher.UIThread.Post(() => BuyStatusText = entry.Message);
        }
        host.Log += OnHostLog;
        _travelCts = new CancellationTokenSource();
        try
        {
            bool opened = await host.GoToHdvAndOpenBuyerAsync(location.MapId, _travelCts.Token);
            BuyStatusText = opened ? "HDV ouvert en mode achat." : $"Trajet ou ouverture de {location.Name} échoué.";
            OnPropertyChanged(nameof(CanBuy));
            return opened;
        }
        finally { host.Log -= OnHostLog; host.Dispose(); _travelCts.Dispose(); _travelCts = null; Traveling = false; }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ToggleBuy()
    {
        if (BuyRunning) { _buyCts?.Cancel(); return; }
        if (_buyStarting) return;
        if (BuyRequiresReview) { BuyStatusText = BuyReviewExplanation; return; }
        if (!GuidedSupplyBuySourceCurrent())
        { BuyStatusText = "Achat refusé : déficit guidé périmé, retirer la ligne et réinspecter V17."; return; }
        var ticket = CaptureHdvUiMutationTicket();
        if (!IsCurrentHdvUiMutationTicket(ticket))
        {
            BuyStatusText = "Achat refusé : identité serveur/personnage indisponible.";
            return;
        }
        _buyStarting = true;
        try
        {
            if (SniperRunning) { BuyStatusText = "Le sniper est en cours."; return; }
            if (BuyBudget <= 0) { BuyStatusText = "Fixe un budget d'abord."; return; }
            if (!CanHdv || !HasBuyRows || ShowNothingToBuy) { BuyStatusText = "Rien à acheter."; return; }
            if (!CanBuy)
            {
                if (!await EnsureBuyerModeAsync()) return;
                if (!IsCurrentHdvUiMutationTicket(ticket) || !GuidedSupplyBuySourceCurrent()) return;
                await FetchBuyPrices();
                if (!IsCurrentHdvUiMutationTicket(ticket) || !GuidedSupplyBuySourceCurrent()) return;
                if (!CanStartBuy) { BuyStatusText = "Rien à acheter."; return; }
            }
        }
        finally { _buyStarting = false; }
        var cts = new CancellationTokenSource();
        _buyCts = cts;
        var ct = cts.Token;
        uint pid = _pid;
        string? character = _charName;
        bool Current() => !ct.IsCancellationRequested &&
                          IsCurrentHdvUiMutationTicket(ticket) &&
                          pid == _pid && string.Equals(character, _charName, StringComparison.Ordinal)
                          && ReferenceEquals(_buyCts, cts) && !BuyRequiresReview &&
                          GuidedSupplyBuySourceCurrent();
        BuyRunning = true;
        BuySpentSession = 0;
        var port = new LaunchBuyPort(_launch, pid, GuidedSupplyBuySourceCurrent);
        try
        {
            while (Current())
            {
                foreach (var row in BuyRows.Where(r => r.ToBuy > 0).ToList())
                {
                    if (!Current()) return;
                    // L'inventaire peut évoluer (récolte, banque, autre script) après la création de la ligne.
                    if (ReferenceEquals(row, _guidedSupplyBuyRow)) row.Owned = 0;
                    else if (BuyDeductStock) row.Owned = Math.Max(0, _launch.Inventory.CountByGid(pid, row.Gid) - row.Bought);
                    long left = BuyRemainingBudget;
                    if (left <= 0) { BuyStatusText = "Budget atteint."; return; }
                    var req = new BuyRequest(row.Gid, row.ToBuy, row.Lots.Select(l => new LotQuote(l.Qty, l.Price, l.Allowed, l.Max, MinMissing(l.Qty))).ToList());
                    var res = await HdvBuyRunner.RunAsync(port, req, left, BuyIgnorePrice, ct, BuyCheaperMarginPct);
                    // A stop between two lots still returns the lots the server confirmed: count them before leaving,
                    // otherwise the next run would buy them a second time.
                    if (!IsCurrentHdvUiMutationTicket(ticket) ||
                        pid != _pid || !string.Equals(character, _charName, StringComparison.Ordinal)) return;
                    row.Bought += res.Bought;
                    AccountConfirmedBuySpend(res.Spent);
                    if (BuyRequiresReview)
                    {
                        BuyStatusText = BuyReviewExplanation;
                        RaiseBuy();
                        return;
                    }
                    if (!Current()) { BuyStatusText = "Achat arrêté."; return; }
                    RefreshBuyLive();
                    row.StateText = res.Stop;
                    if (res.Bought > 0 && NotifyBuy) Toasts.Show($"Acheté {res.Bought} × {row.Name} ({N(res.Spent)} k)", success: true);
                    Log($"Achat {row.Name} : {res.Bought} acheté(s), {N(res.Spent)} k, {res.Stop}", res.Bought > 0 ? LogOk : LogWarn);
                    await Task.Delay(Random.Shared.Next(BuyDelayMinMs, Math.Max(BuyDelayMinMs, BuyDelayMaxMs) + 1), ct);
                }
                if (!Current()) return;
                if (BuyRows.All(r => r.IsCovered)) { BuyStatusText = "Tout est acheté."; return; }
                BuyStatusText = $"Reste à acheter ; nouvel essai dans {BuyRetrySec}s.";
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, BuyRetrySec)), ct);
            }
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_buyCts, cts) &&
                IsCurrentHdvUiMutationTicket(ticket) &&
                pid == _pid && string.Equals(character, _charName, StringComparison.Ordinal))
                BuyStatusText = "Achat arrêté.";
        }
        finally
        {
            if (ReferenceEquals(_buyCts, cts)) { BuyRunning = false; _buyCts = null; }
            cts.Dispose();
        }
    }

    // ---- Relist ----
    [RelayCommand]
    private void AddRelistPick(HdvItemPick? pick)
    {
        if (pick is null || RelistRows.Any(r => r.Gid == pick.Gid)) return;
        // Taille proposée : le plus grand lot qu'on peut former avec le stock du sac (modifiable ensuite dans la carte).
        int owned = _launch.Inventory.CountByGid(_pid, pick.Gid);
        int qty = HdvRelistRow.LotSizes.Where(s => s <= Math.Max(1, owned)).DefaultIfEmpty(1).Max();
        RelistRows.Add(new HdvRelistRow { Gid = pick.Gid, Name = pick.Name, Icon = pick.Icon, Qty = qty, Keep = RelistBulkKeep });
        RelistQuery = ""; RelistSuggestions.Clear();
    }

    [RelayCommand] private void RemoveRelist(HdvRelistRow? row) { if (row is not null) RelistRows.Remove(row); }
    [RelayCommand] private void ClearRelist() => RelistRows.Clear();

    /// <summary>Lots de chaque règle en vente (mes ventes chargées) et en stock (lots complets du sac) ; « stock épuisé » quand
    /// plus aucun lot complet n'est disponible. Appelé au tick lent de l'UI (lectures seules).</summary>
    public void RefreshRelistStats()
    {
        if (_pid == 0 || RelistRows.Count == 0) return;
        foreach (var r in RelistRows)
        {
            int lots = r.Qty > 0 ? _launch.Inventory.CountByGid(_pid, r.Gid) / r.Qty : 0;
            int onSale = Listings.Count(l => l.Gid == r.Gid && l.Qty == r.Qty);
            r.StatsText = Loc.Fmt("L.Bots.Hdv.RelistStats", onSale, lots, r.Target);
            r.StockOut = lots == 0;
            if (lots > 0 && r.IsDry) r.IsDry = false;   // réapprovisionné : la règle repart
        }
    }
    [RelayCommand] private void ToggleRelist() => RelistEnabled = !RelistEnabled;
    [RelayCommand] private void ApplyRelistKeep() { foreach (var r in RelistRows) r.Keep = RelistBulkKeep; }

    [RelayCommand]
    private void ScaleRelistMin(string? percent)
    {
        int p = ParsePct(percent);
        foreach (var r in RelistRows) r.MinPrice = HdvTradeLogic.Scale(r.MinPrice, p);
    }

    [RelayCommand]
    private void FillRelistMinFromMarket()
    {
        foreach (var r in RelistRows)
        {
            long m = _launch.Hdv.MarketFor(_pid, r.Gid, BidHouseStore.SlotOf(r.Qty));
            long v = HdvTradeLogic.MinFromMarket(m, RelistBulkPct);
            if (v > 0) r.MinPrice = v;
        }
    }

    [RelayCommand]
    private void ImportListingsToRelist()
    {
        // Une règle par objet : la taille la plus présente dans mes ventes, et autant de lots à garder en vente qu'il y en a.
        foreach (var g in Listings.Where(l => RelistRows.All(r => r.Gid != l.Gid)).GroupBy(l => l.Gid))
        {
            var bySize = g.GroupBy(l => l.Qty).OrderByDescending(x => x.Count()).ThenByDescending(x => x.Key).First();
            var l = bySize.First();
            RelistRows.Add(new HdvRelistRow { Gid = l.Gid, Name = l.Name, Icon = l.Icon, Qty = l.Qty, Target = Math.Clamp(bySize.Count(), 1, 100), Keep = RelistBulkKeep });
        }
    }

    [RelayCommand]
    private void ImportRelistToBuy()
    {
        foreach (var r in RelistRows.Where(r => BuyRows.All(b => b.Gid != r.Gid)).ToList()) BuyRows.Add(NewBuyRow(r.Gid, r.Name, r.Qty));
    }

    [RelayCommand]
    private void ExportBuyToRelist()
    {
        foreach (var b in BuyRows.Where(b => RelistRows.All(r => r.Gid != b.Gid)).ToList())
        {
            int qty = b.Lots.Where(l => l.Allowed).Select(l => l.Qty).DefaultIfEmpty(1).Max();
            RelistRows.Add(new HdvRelistRow { Gid = b.Gid, Name = b.Name, Icon = b.Icon, Qty = qty, Keep = RelistBulkKeep });
        }
    }

    /// <summary>Règle de remise en vente figée pour un traitement hors du thread UI.</summary>
    private readonly record struct RelistSnap(long Gid, int Qty, int Target, long MinPrice);
    private static RelistSnap Snap(HdvRelistRow r) => new(r.Gid, r.Qty, Math.Clamp(r.Target, 1, 100), Math.Max(0, r.MinPrice));

    /// <summary>Après une vente d'un objet de la liste : complète ses lots en vente jusqu'au nombre voulu (prix marché − sous-cotation,
    /// jamais sous le minimum). Inactif si la remise en vente est coupée, si l'objet n'est pas listé ou « à sec ».</summary>
    private async Task RelistAfterSaleAsync(long gid, int qty)
    {
        if (!RelistEnabled || !RelistOnSale) return;
        uint pid = _pid;
        string? character = _charName;
        bool Current() => pid != 0 && pid == _pid && string.Equals(character, _charName, StringComparison.Ordinal);
        var rule = RelistRows.FirstOrDefault(r => r.Gid == gid && r.Qty == qty) ?? RelistRows.FirstOrDefault(r => r.Gid == gid);
        if (rule is null || rule.IsDry) return;
        var snap = Snap(rule);
        await Task.Delay(Random.Shared.Next(RelistWakeMinMs, Math.Max(RelistWakeMinMs, RelistWakeMaxMs) + 1));   // le serveur retire le lot vendu
        if (!Current()) return;
        await TopUpRelistAsync(pid, snap, Current, CancellationToken.None);
    }

    /// <summary>Repricing auto : chaque règle est complétée jusqu'à son nombre de lots en vente (stock du sac).</summary>
    private async Task TopUpAllRelistAsync(CancellationToken ct)
    {
        uint pid = _pid;
        string? character = _charName;
        bool Current() => pid != 0 && pid == _pid && string.Equals(character, _charName, StringComparison.Ordinal);
        var rules = Dispatcher.UIThread.Invoke(() => RelistEnabled ? RelistRows.Where(r => !r.IsDry).Select(Snap).ToList() : new List<RelistSnap>());
        foreach (var r in rules)
        {
            if (ct.IsCancellationRequested || !Current()) return;
            await TopUpRelistAsync(pid, r, Current, ct);
        }
    }

    /// <summary>Met en vente les lots manquants d'une règle (au plus le stock du sac). Rend le nombre de lots posés.</summary>
    private async Task<int> TopUpRelistAsync(uint pid, RelistSnap r, Func<bool> current, CancellationToken ct)
    {
        if (r.Qty <= 0) return 0;
        int onSale = _launch.Hdv.MyListings(pid).Count(l => l.Gid == r.Gid && l.Qty == r.Qty);
        int stock = _launch.Inventory.CountByGid(pid, r.Gid) / r.Qty;
        int missing = Math.Min(Math.Max(0, r.Target - onSale), stock);
        void State(string text, bool dry) => Dispatcher.UIThread.Post(() =>
        {
            if (!current()) return;
            foreach (var row in RelistRows.Where(x => x.Gid == r.Gid && x.Qty == r.Qty)) { row.StateText = text; row.IsDry = dry; }
        });
        if (r.Target - onSale > 0 && stock == 0) { State(Loc.T("L.Bots.Hdv.RelistStockOut"), true); return 0; }
        if (missing == 0) return 0;
        await _launch.HdvGetMarketPriceAsync(pid, r.Gid, ct);
        if (!current()) return 0;
        long price = HdvTradeLogic.RelistPrice(_launch.Hdv.MarketFor(pid, r.Gid, BidHouseStore.SlotOf(r.Qty)), r.MinPrice, ListUndercut);
        if (price <= 0) { State("Marché inconnu : pas de remise en vente", false); return 0; }
        int done = 0;
        for (int i = 0; i < missing && !ct.IsCancellationRequested && current(); i++)
        {
            // Auto-relist is another genuine sale path. It must reserve the
            // SAME durable order/cap as manual HDV and Lua, not bypass the
            // desktop journal through LaunchService.HdvSellByGidAsync.
            if (!await _launch.HdvScriptSellByGidAsync(pid, r.Gid, r.Qty, price, ct)) break;
            done++;
            if (i + 1 < missing) await Task.Delay(RandDelay(DelayBetweenSalesMs, DelayBetweenSalesMaxMs), ct);
        }
        bool dry = done == 0 && _launch.Inventory.CountByGid(pid, r.Gid) < r.Qty;
        State(done > 0 ? $"Remis en vente {done} × x{r.Qty} à {N(price)}" : "Remise en vente refusée", dry);
        return done;
    }

    // ---- Recettes locales : base extraite du jeu, déjà utilisée par l'API Lua LotM ----
    // Les listes d'achat et les quantités restent contrôlées par l'utilisateur : aucune action réseau ici.
    private bool AddRecipeIngredients(RecipeInfo recipe, int multiplier, bool accumulate = false)
    {
        if (recipe.Ingredients.Count == 0) return false;
        foreach (var group in recipe.Ingredients.Where(i => i.Gid > 0 && i.Quantity > 0).GroupBy(i => i.Gid))
        {
            int amount = (int)Math.Min(int.MaxValue, group.Sum(i => (long)i.Quantity) * multiplier);
            if (amount <= 0) continue;
            var existing = BuyRows.FirstOrDefault(r => r.Gid == group.Key);
            if (existing is null)
            {
                var ingredient = group.First();
                BuyRows.Add(NewBuyRow(ingredient.Gid, ingredient.Name, amount));
            }
            else
            {
                // Recharger la même recette ne multiplie pas les quantités à chaque clic.
                existing.Needed = accumulate
                    ? (int)Math.Min(int.MaxValue, (long)existing.Needed + amount)
                    : Math.Max(existing.Needed, amount);
            }
        }
        foreach (var row in BuyRows) Replan(row);
        return true;
    }

    [RelayCommand]
    private void LoadBuyRecipe()
    {
        if (BuyRunning) return;
        var db = _launch.GameData;
        if (db is null) { BuyStatusText = "Base de recettes locale introuvable (dofus.sqlite)."; return; }
        if (string.IsNullOrWhiteSpace(BuyRecipeName)) { BuyStatusText = "Indique le nom de l'objet à fabriquer."; return; }
        try
        {
            var gid = db.ItemGidByName(BuyRecipeName);
            var recipe = gid is long id ? db.Recipe(id) : null;
            if (recipe is null || !AddRecipeIngredients(recipe, Math.Clamp(BuyRecipeMult, 1, 1000)))
            { BuyStatusText = "Aucune recette avec ingrédients pour cet objet."; return; }
            BuyStatusText = $"Recette « {recipe.Name} » : {recipe.Ingredients.Count} ingrédient(s) chargé(s).";
        }
        catch (Exception ex) { BuyStatusText = $"Lecture de la recette impossible : {ex.Message}"; }
    }

    [RelayCommand] private void ReloadBuyRecipe() => LoadBuyRecipe();

    [RelayCommand]
    private void DecomposeBuyRow(HdvBuyRow? row)
    {
        if (BuyRunning || SniperRunning || row is null || row.ToBuy <= 0) return;
        var db = _launch.GameData;
        if (db is null) { BuyStatusText = "Base de recettes locale introuvable (dofus.sqlite)."; return; }
        try
        {
            var recipe = db.Recipe(row.Gid);
            if (recipe is null || recipe.Ingredients.Count == 0)
            { BuyStatusText = $"Aucune recette à décomposer pour « {row.Name} »."; return; }
            int amount = row.ToBuy;
            BuyRows.Remove(row);
            AddRecipeIngredients(recipe, amount, accumulate: true);
            BuyStatusText = $"« {row.Name} » décomposé en ingrédients pour {amount} fabrication(s).";
        }
        catch (Exception ex) { BuyStatusText = $"Décomposition impossible : {ex.Message}"; }
    }

    [RelayCommand]
    private void DecomposeAllBuy()
    {
        if (BuyRunning || SniperRunning) return;
        var db = _launch.GameData;
        if (db is null) { BuyStatusText = "Base de recettes locale introuvable (dofus.sqlite)."; return; }
        var sources = BuyRows.Where(r => r.Gid > 0 && r.ToBuy > 0).ToList();
        if (sources.Count == 0) { BuyStatusText = "Rien à décomposer."; return; }
        HdvDecomposeResult result;
        try
        {
            result = HdvRecipeBreakdown.DecomposeToRaw(
                sources.Select(r => new HdvComponent(r.Gid, r.ToBuy, r.Name)).ToList(),
                gid =>
                {
                    var recipe = db.Recipe(gid);
                    if (recipe is null || recipe.Ingredients.Count == 0) return null;
                    return recipe.Ingredients.Where(i => i.Gid > 0 && i.Quantity > 0)
                        .GroupBy(i => i.Gid)
                        .Select(g =>
                        {
                            long qty = g.Sum(i => (long)i.Quantity);
                            if (qty <= 0 || qty > int.MaxValue) throw new OverflowException("quantité d'ingrédient trop grande");
                            return new HdvComponent(g.Key, (int)qty, g.First().Name ?? "");
                        }).ToList();
                });
        }
        catch (Exception ex) { BuyStatusText = $"Décomposition interrompue : {ex.Message}"; return; }
        if (result.Overflow) { BuyStatusText = "Décomposition interrompue : quantité trop grande."; return; }
        if (result.Expanded == 0) { BuyStatusText = "Aucune recette décomposable dans la liste."; return; }
        if (result.Rows.Count == 0) { BuyStatusText = "Décomposition interrompue : aucun ingrédient."; return; }

        var inputGids = sources.Select(r => r.Gid).ToHashSet();
        var finalGids = result.Rows.Select(r => r.Gid).ToHashSet();
        foreach (var row in sources.Where(r => !finalGids.Contains(r.Gid)).ToList()) BuyRows.Remove(row);
        foreach (var item in result.Rows)
        {
            var existing = BuyRows.FirstOrDefault(r => r.Gid == item.Gid);
            if (existing is null)
            {
                string name = item.Name.Length > 0 ? item.Name : _launch.ItemDisplayName(item.Gid);
                BuyRows.Add(NewBuyRow(item.Gid, name, item.Quantity));
                continue;
            }
            long needed = inputGids.Contains(item.Gid)
                ? (long)existing.Owned + existing.Bought + item.Quantity
                : (long)existing.Needed + item.Quantity;
            existing.Needed = needed >= int.MaxValue ? int.MaxValue : (int)Math.Max(0, needed);
        }
        ReplanAllBuyRows();
        BuyStatusText = result.CycleStopped
            ? $"{result.Expanded} recette(s) décomposée(s). Une boucle de recette a été arrêtée."
            : $"{result.Expanded} recette(s) décomposée(s) jusqu'aux matières premières.";
    }

    [RelayCommand] private void RunScan() => _ = RunLocalCraftScanAsync();
    [RelayCommand] private void VerifyScanRow(HdvScanRow? row)
    {
        if (row is not null) ScanStatusText = "Vérification fraîche indisponible hors ligne : relance le scan avec les prix locaux; aucune requête envoyée.";
    }

    /// <summary>Résultat de scan → ligne d'achat (quantité = ScanQty).</summary>
    [RelayCommand]
    private void ScanToBuy(HdvScanRow? row)
    {
        if (row is null || BuyRows.Any(r => r.Gid == row.Gid)) return;
        BuyRows.Add(NewBuyRow(row.Gid, row.Name, ScanQty));
    }

    [RelayCommand(AllowConcurrentExecutions = true)]   // le même bouton arrête le sniper pendant qu'il tourne
    private async Task ToggleSniper()
    {
        if (SniperRunning) { _sniperCts?.Cancel(); return; }
        if (BuyRequiresReview) { SniperStatusText = BuyReviewExplanation; return; }
        if (BuyRunning) { SniperStatusText = "Un achat est déjà en cours."; return; }
        if (!CanHdv) { SniperStatusText = "Permission HDV manquante."; return; }
        if (_pid == 0 || _launch.CurrentMap(_pid) is null) { SniperStatusText = "Personnage hors jeu."; return; }
        if (!CanBuy) { SniperStatusText = "Ouvre l'HDV en mode achat."; return; }
        if (SniperPct <= 0) { SniperStatusText = "Indique un pourcentage sniper supérieur à zéro."; return; }
        if (BuyBudget <= 0) { SniperStatusText = "Fixe un budget d'abord."; return; }
        long left = BuyRemainingBudget;
        if (left <= 0) { SniperStatusText = "Budget atteint."; return; }
        if (_launch.Kamas(_pid) == 0) { SniperStatusText = "Pas assez de kamas."; return; }
        if (!BuyRows.Any(r => r.Lots.Any(l => l.Allowed && l.Max > 0))) { SniperStatusText = "Aucune ligne avec un prix maximum."; return; }
        if (!HdvSniper.HasDemand(SniperSnapshot())) { SniperStatusText = "Rien à acheter."; return; }

        var ticket = CaptureHdvUiMutationTicket();
        if (!IsCurrentHdvUiMutationTicket(ticket))
        {
            SniperStatusText = "Sniper refusé : identité serveur/personnage indisponible.";
            return;
        }

        var cts = new CancellationTokenSource();
        _sniperCts = cts;
        uint pid = _pid;
        string? character = _charName;
        bool SameCharacter() => IsCurrentHdvUiMutationTicket(ticket) &&
                                pid == _pid && string.Equals(character, _charName, StringComparison.Ordinal);
        bool Live() => !cts.IsCancellationRequested && SameCharacter() && !BuyRequiresReview;
        SniperRunning = true;
        SniperStatusText = "Sniper en cours.";
        var config = new HdvSniperConfig(SniperPct, SniperWaitMinSec, SniperWaitMaxSec, left);
        try
        {
            var result = await HdvSniper.WatchAsync(
                new SniperBuyPort(this, pid, Live), new LiveSniperClock(), new LiveSniperRandom(),
                config, SniperSnapshot, purchase => OnSniperPurchase(purchase, SameCharacter), cts.Token);
            if (!SameCharacter()) return;
            if (BuyRequiresReview)
            {
                SniperStatusText = BuyReviewExplanation;
                RaiseBuy();
                return;
            }
            SniperStatusText = result.Stop switch
            {
                "arrêté" => "Sniper arrêté.",
                "budget atteint" => "Sniper : budget atteint.",
                "quantité atteinte" => "Sniper : quantité atteinte.",
                "achat non confirmé" => "Sniper arrêté : achat non confirmé par le serveur.",
                "pas assez de kamas" => "Sniper arrêté : pas assez de kamas.",
                "rien à surveiller" => "Aucune ligne avec un prix maximum.",
                _ => "Sniper : " + result.Stop,
            };
        }
        catch (Exception ex)
        {
            if (SameCharacter()) SniperStatusText = "Sniper interrompu : " + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_sniperCts, cts)) { SniperRunning = false; _sniperCts = null; }
            cts.Dispose();
        }
    }

    private IReadOnlyList<HdvSniperItem> SniperSnapshot()
        => Dispatcher.UIThread.Invoke(() =>
        {
            var list = new List<HdvSniperItem>();
            foreach (var row in BuyRows)
            {
                int? cap = row.Needed <= 0 ? null : row.ToBuy;
                if (cap == 0) continue;
                list.Add(new HdvSniperItem(row.Gid, cap, row.Lots.Select(l => new HdvSniperLot(l.Qty, l.Max, l.Allowed)).ToList()));
            }
            return (IReadOnlyList<HdvSniperItem>)list;
        });

    private void OnSniperPurchase(HdvSniperPurchase purchase, Func<bool> live)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            if (!live()) return;
            AccountConfirmedBuySpend(purchase.Price);
            var row = BuyRows.FirstOrDefault(r => r.Gid == purchase.Gid);
            if (row is not null)
            {
                row.Bought += purchase.Quantity;
                row.StateText = $"Sniper x{purchase.Quantity}";
            }
            Log($"Sniper : {purchase.Quantity} acheté(s), {N(purchase.Price)} k", LogOk);
            if (NotifyBuy) Toasts.Show($"Sniper {purchase.Quantity} ({N(purchase.Price)} k)", success: true);
        });
    }

    [RelayCommand]
    private async Task LoadRowOffers(HdvBuyRow? row)
    {
        if (row is null) return;
        if (BuyRunning || SniperRunning) { BuyStatusText = "Attends la fin de l'achat en cours."; return; }
        if (!CanHdv) { BuyStatusText = "Permission HDV manquante."; return; }
        if (_pid == 0 || _launch.CurrentMap(_pid) is null) { BuyStatusText = "Personnage hors jeu."; return; }
        if (!CanBuy) { BuyStatusText = "Ouvre l'HDV en mode achat pour lire les offres."; return; }
        uint pid = _pid;
        long gid = row.Gid;
        try
        {
            var offers = await _launch.HdvGetBuyerOffersAsync(pid, gid, CancellationToken.None);
            if (pid != _pid) return;
            BuyOffers.Clear();
            foreach (var offer in offers)
            {
                if (offer.Uid <= 0 || offer.Gid != gid) continue;
                var effects = CopyEffects(offer.Effects);
                string effectsText = effects.Count == 0
                    ? "Sans effet"
                    : string.Join(", ", effects.Select(e => ItemEffectText.Format(e.ActionId, null, ItemEffectText.Params(e.Value, e.Min, e.Max, e.Const, e.Text))));
                foreach (int qty in TradeLots)
                {
                    int slot = BidHouseStore.SlotOf(qty);
                    if ((uint)slot >= (uint)offer.Prices.Length || offer.Prices[slot] <= 0) continue;
                    BuyOffers.Add(new HdvChosenOfferRow
                    {
                        Gid = gid, Uid = offer.Uid, Quantity = qty, ShownPrice = offer.Prices[slot],
                        Effects = effects, EffectsText = effectsText,
                    });
                }
            }
            BuyStatusText = BuyOffers.Count == 0 ? "Aucune offre pour cet objet." : $"{BuyOffers.Count} offre(s) chargée(s). Choisis-en une.";
        }
        catch (Exception ex) { BuyStatusText = "Lecture des offres impossible : " + ex.Message; }
    }

    [RelayCommand]
    private async Task BuyChosenOffer(HdvChosenOfferRow? offer)
    {
        if (offer is null) return;
        if (BuyRequiresReview) { BuyStatusText = BuyReviewExplanation; return; }
        if (BuyRunning || SniperRunning) { BuyStatusText = "Attends la fin de l'achat en cours."; return; }
        if (!CanHdv) { BuyStatusText = "Permission HDV manquante."; return; }
        if (_pid == 0 || _launch.CurrentMap(_pid) is null) { BuyStatusText = "Personnage hors jeu."; return; }
        if (!CanBuy) { BuyStatusText = "Ouvre l'HDV en mode achat."; return; }
        if (BuyBudget <= 0) { BuyStatusText = "Fixe un budget d'abord."; return; }
        var ticket = CaptureHdvUiMutationTicket();
        if (!IsCurrentHdvUiMutationTicket(ticket))
        {
            BuyStatusText = "Achat refusé : identité serveur/personnage indisponible.";
            return;
        }
        long left = BuyRemainingBudget;
        uint pid = _pid;
        string? character = _charName;
        HdvChosenBuyResult result;
        try
        {
            result = await HdvChosenListing.BuyAsync(new ChosenListingPort(_launch, pid),
                new HdvChosenBuyRequest(offer.Gid, offer.Uid, offer.Quantity, offer.ShownPrice, offer.Effects, left),
                CancellationToken.None);
        }
        catch (Exception ex) { BuyStatusText = "Achat interrompu : " + ex.Message; RaiseBuy(); return; }
        if (!IsCurrentHdvUiMutationTicket(ticket) ||
            pid != _pid || !string.Equals(character, _charName, StringComparison.Ordinal)) return;
        if (!result.Confirmed)
        {
            BuyStatusText = BuyRequiresReview ? BuyReviewExplanation : result.Reason;
            RaiseBuy();
            return;
        }
        AccountConfirmedBuySpend(result.Spent);
        var row = BuyRows.FirstOrDefault(r => r.Gid == offer.Gid);
        if (row is not null) row.Bought += offer.Quantity;
        BuyStatusText = $"Offre {offer.Uid} achetée ({N(result.Spent)} k).";
        Log($"Achat offre {offer.Uid} x{offer.Quantity} : {N(result.Spent)} k", LogOk);
        if (NotifyBuy) Toasts.Show($"Acheté x{offer.Quantity} ({N(result.Spent)} k)", success: true);
    }

    private static List<HdvListedEffect> CopyEffects(IReadOnlyList<InventoryReader.ItemEffect>? effects)
        => (effects ?? Array.Empty<InventoryReader.ItemEffect>())
            .Select(e => new HdvListedEffect(e.ActionId, e.Value, e.Min, e.Max, e.Const, e.Text, e.ObjectGid)).ToList();

    private sealed class SniperBuyPort(HdvViewModel vm, uint pid, Func<bool> live) : IHdvSniperPort
    {
        private void Ensure() { if (!live()) throw new OperationCanceledException(); }
        public Task<long> FreshLotPriceAsync(long gid, int quantity, CancellationToken ct)
        {
            Ensure();
            return vm._launch.HdvGetLotPriceAsync(pid, gid, quantity, ct);
        }
        public long ReferenceUnit(long gid)
            => gid > 0 && gid <= int.MaxValue ? vm._launch.AveragePrice(pid, (int)gid) : 0;
        public long Kamas() => vm._launch.Kamas(pid);
        public Task<bool> ConfirmedBuyAsync(long gid, int quantity, long price, CancellationToken ct)
        {
            Ensure();
            return vm._launch.HdvBuyByGidAsync(pid, gid, quantity, price, ct);
        }
    }

    private sealed class LiveSniperClock : IHdvSniperClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);
    }

    private sealed class LiveSniperRandom : IHdvSniperRandom
    {
        public int NextInclusive(int minInclusive, int maxInclusive)
        {
            if (maxInclusive < minInclusive) (minInclusive, maxInclusive) = (maxInclusive, minInclusive);
            if (maxInclusive == int.MaxValue) maxInclusive--;
            return Random.Shared.Next(minInclusive, maxInclusive + 1);
        }
    }

    private sealed class ChosenListingPort(LaunchService launch, uint pid) : IHdvChosenListingPort
    {
        public async Task<HdvFreshListing?> FreshAsync(long gid, int uid, int quantity, CancellationToken ct)
        {
            var rows = await launch.HdvGetBuyerOffersAsync(pid, gid, ct).ConfigureAwait(false);
            var offer = rows.FirstOrDefault(o => o.Uid == uid && o.Gid == gid);
            if (offer is null) return null;
            int slot = BidHouseStore.SlotOf(quantity);
            if ((uint)slot >= (uint)offer.Prices.Length) return null;
            return new HdvFreshListing(offer.Uid, offer.Gid, offer.Prices[slot], CopyEffects(offer.Effects));
        }
        public long Kamas() => launch.Kamas(pid);
        public Task<bool> ConfirmedBuyAsync(long gid, int uid, int quantity, long price, CancellationToken ct)
            => launch.HdvBuySelectedOfferAsync(pid, gid, uid, quantity, price, ct);
    }

    // ---- Vues ----
    [RelayCommand] private void ShowBuyView() { ShowStats = false; ShowSettings = false; ShowScan = false; ShowBuy = true; }
    [RelayCommand] private void ShowScanView() { ShowStats = false; ShowSettings = false; ShowBuy = false; ShowScan = true; }
    [RelayCommand] private void ShowSettingsView() { ShowStats = false; ShowBuy = false; ShowScan = false; ShowSettings = true; }
}

/// <summary>Une offre achetable choisie sur l'écran : uid, taille, prix affiché et effets figés au chargement.</summary>
public sealed class HdvChosenOfferRow
{
    public long Gid { get; init; }
    public int Uid { get; init; }
    public int Quantity { get; init; }
    public long ShownPrice { get; init; }
    public string EffectsText { get; init; } = "";
    public IReadOnlyList<HdvListedEffect> Effects { get; init; } = Array.Empty<HdvListedEffect>();
    public string Label => string.IsNullOrEmpty(EffectsText)
        ? $"uid {Uid} · x{Quantity} · {ShownPrice:N0} k"
        : $"{EffectsText} · uid {Uid} · x{Quantity} · {ShownPrice:N0} k";
}
