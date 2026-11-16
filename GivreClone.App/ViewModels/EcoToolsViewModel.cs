using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Eco;
using GivreClone.App.Services.LotM;

namespace GivreClone.App.ViewModels;

public sealed record EcoItemHit(long Gid, string Name, int Level, bool HasRecipe)
{
    public Avalonia.Media.Imaging.Bitmap? Icon { get; } = EcoService.IconOf(Gid);
    public bool HasIcon => Icon is not null;
    public string Caption => $"{Name}  ·  niv. {Level}" + (HasRecipe ? "" : "  (pas de recette)");
}

public sealed record EcoBoardRow(long Gid, string Name, string CraftText, string BuyText, string SavingText, string Verdict, bool Good)
{
    public Avalonia.Media.Imaging.Bitmap? Icon { get; } = EcoService.IconOf(Gid);
    public bool HasIcon => Icon is not null;
}

public sealed record EcoCraftRow(long Gid, string Indent, string Name, string QtyText, string DecisionText, string UnitText, string TotalText,
                                 string SourceText, bool Stale, bool CanForce)
{
    public Avalonia.Media.Imaging.Bitmap? Icon { get; } = EcoService.IconOf(Gid);
    public bool HasIcon => Icon is not null;
}

/// <summary>Atelier économique : « Craft ou acheter » (comme Dofus-Craft), forgemagie (poids, runes, séance) et carnet personnel.
/// Lecture seule côté jeu : lit l'HDV déjà observé du bot sélectionné et les données du jeu, n'envoie jamais rien.</summary>
public sealed partial class EcoToolsViewModel : ObservableObject
{
    private static string Dir => Path.Combine(MZP19LocalClone.DataRoot, "eco");
    private readonly string _nbPath = Path.Combine(Dir, "notebook.json"), _fmPath = Path.Combine(Dir, "fm.json");
    private bool _loaded;

    private static uint CurrentPid => MainViewModel.Instance?.BotsVM.SelectedBot is { Pid: > 0 } b ? (uint)b.Pid : 0;
    private static int CurrentServer => CurrentPid > 0 ? Stores.Launch.ServerIdOf(CurrentPid) : 0;

    public EcoToolsViewModel() { }

    /// <summary>Charge les fichiers locaux une fois (au premier affichage).</summary>
    public void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _notebook = EcoJson.Load<Notebook>(_nbPath);
        _fm = EcoJson.Load<FmLedgerStore>(_fmPath);
        ReloadNotebook(); ReloadTracked(); ReloadFmSessions(); RefreshRunes(); RefreshSubs(); RefreshBoard(); RefreshAlerts(); LoadJobs(); RefreshResources();
        // Miniatures des objets : le bundle d'icônes du client se lit une fois en tâche de fond, puis les lignes affichées sont reconstruites.
        if (!Stores.InteractiveIcons.IsLoaded)
            _ = System.Threading.Tasks.Task.Run(() => { Stores.InteractiveIcons.EnsureLoaded(); Avalonia.Threading.Dispatcher.UIThread.Post(RefreshIcons); });
        BotLine = CurrentPid > 0 ? $"Prix HDV du bot sélectionné (serveur {CurrentServer})" : "Aucun bot sélectionné : seuls les prix saisis à la main sont utilisés.";
    }

    [ObservableProperty] public partial string BotLine { get; set; } = "";

    private void RefreshIcons()
    {
        var keep = SelectedHit;
        if (keep is not null) { var hit = new EcoItemHit(keep.Gid, keep.Name, keep.Level, keep.HasRecipe); SelectedHit = hit; }
        if (CraftResults.Count > 0) { var q = CraftSearch; CraftSearch = ""; CraftSearch = q; }
        RefreshBoard();
        if (Deals.Count > 0) RefreshDeals();
        if (BagTop.Count > 0 || BankTop.Count > 0) RefreshWealth();
    }

    private PriceQuote? PriceFor(long gid) => EcoService.PriceFor(CurrentPid, gid);

    // ============================== Craft ou acheter ==============================
    public ObservableCollection<EcoItemHit> CraftResults { get; } = new();
    public ObservableCollection<EcoCraftRow> CraftRows { get; } = new();
    public ObservableCollection<EcoItemHit> Tracked { get; } = new();
    private readonly Dictionary<long, CraftForce> _force = new();

    [ObservableProperty] public partial string CraftSearch { get; set; } = "";
    [ObservableProperty] public partial EcoItemHit? SelectedHit { get; set; }
    [ObservableProperty] public partial decimal? CraftQty { get; set; } = 1;
    [ObservableProperty] public partial EcoCraftRow? SelectedRow { get; set; }
    [ObservableProperty] public partial string ManualPriceText { get; set; } = "";
    [ObservableProperty] public partial string CraftCostText { get; set; } = "—";
    [ObservableProperty] public partial string BuyCostText { get; set; } = "—";
    [ObservableProperty] public partial string SavingText { get; set; } = "—";
    [ObservableProperty] public partial string ResaleText { get; set; } = "";
    [ObservableProperty] public partial string Recommendation { get; set; } = "Cherche un objet à fabriquer.";
    [ObservableProperty] public partial bool RecommendCraft { get; set; }
    [ObservableProperty] public partial string ShoppingText { get; set; } = "";
    [ObservableProperty] public partial string StaleText { get; set; } = "";
    [ObservableProperty] public partial string CraftStatus { get; set; } = "";
    public bool HasRows => CraftRows.Count > 0;

    partial void OnCraftSearchChanged(string value)
    {
        EnsureLoaded();
        CraftResults.Clear();
        if (value.Trim().Length < 2) return;
        if (Stores.Launch.GameData is not { } db) { CraftStatus = "Données du jeu (dofus.sqlite) introuvables."; return; }
        foreach (var (gid, name, level) in db.FindItems(value.Trim(), 30))
            CraftResults.Add(new EcoItemHit(gid, name, level, HasRecipe(db, gid)));
    }

    private static string NameOf(long gid) => EcoService.NameOf(gid);

    private static bool HasRecipe(GameDb? db, long gid) { try { return db?.Recipe(gid) is not null; } catch { return false; } }

    partial void OnSelectedHitChanged(EcoItemHit? value) { _force.Clear(); SelectedRow = null; Analyze(); }
    partial void OnCraftQtyChanged(decimal? value) => Analyze();
    partial void OnSelectedRowChanged(EcoCraftRow? value) => ManualPriceText = value is null ? "" : (PriceFor(value.Gid) is { } q ? q.Unit.ToString("0.##", CultureInfo.InvariantCulture) : "");

    [RelayCommand] private void Refresh() { EnsureLoaded(); Analyze(); RefreshRunes(); RefreshFmItems(); }

    private void Analyze()
    {
        CraftRows.Clear();
        OnPropertyChanged(nameof(HasRows));
        if (SelectedHit is not { } hit || Stores.Launch.GameData is not { } db)
        {
            Recommendation = "Cherche un objet à fabriquer."; CraftCostText = BuyCostText = SavingText = "—"; ResaleText = ShoppingText = StaleText = ""; RecommendCraft = false;
            return;
        }
        long qty = (long)Math.Clamp(CraftQty ?? 1, 1, 10000);
        uint pid = CurrentPid;
        decimal tax = pid > 0 ? (decimal)Math.Clamp(Stores.Launch.HdvTaxPercent(pid), 0f, 50f) : 0;
        var advice = BuildAdvice(db, hit.Gid, qty, _force, tax);
        foreach (var (depth, n) in CraftAdvisor.Flatten(advice.Root))
        {
            string decision = n.Decision switch { CraftDecision.Craft => "Fabriquer", CraftDecision.Buy => "Acheter", _ => "Prix ?" };
            string src = n.Decision == CraftDecision.Buy ? (n.PriceSource + (n.Stale ? " · périmé" : "")).Trim() : n.Craftable ? "recette" : "";
            CraftRows.Add(new EcoCraftRow(n.Gid, new string(' ', depth * 3) + (depth > 0 ? "└ " : ""), n.Name, "×" + n.Quantity.ToString("N0"), decision,
                n.UnitCost is { } u ? u.ToString("N0") : "—", n.TotalCost is { } t ? t.ToString("N0") : "—", src, n.Stale, depth > 0));
        }
        OnPropertyChanged(nameof(HasRows));
        CraftCostText = advice.CraftCost is { } cc ? cc.ToString("N0") + " k" : "—";
        BuyCostText = advice.BuyCost is { } bc ? bc.ToString("N0") + " k" : "—";
        SavingText = advice.Saving is { } sv ? $"{(sv >= 0 ? "+" : "")}{sv:N0} k" + (advice.SavingPercent is { } p ? $" ({p:0.#} %)" : "") : "—";
        ResaleText = ResaleFees.Describe(advice.BuyCost, advice.ResaleProfit, tax);
        Recommendation = advice.Recommendation;
        RecommendCraft = advice.Saving is > 0;
        StaleText = advice.AnyStale ? "Certains prix sont anciens (plus de 48 h) : rouvre l'HDV pour les rafraîchir." : "";
        var shop = CraftAdvisor.ShoppingList(advice.Root);
        ShoppingText = shop.Count == 0 ? "" : string.Join("\n", shop.Select(x => $"{x.Quantity:N0} × {x.Name}" + (x.Cost is { } c ? $"  ({c:N0} k)" : "  (prix ?)")));
        CraftStatus = advice.MissingPrices.Count > 0 ? $"{advice.MissingPrices.Count} prix manquant(s) : saisis-les à la main ci-dessous." : "";
    }

    private CraftAdvice BuildAdvice(GameDb db, long gid, long qty, IReadOnlyDictionary<long, CraftForce>? force, decimal tax)
        => EcoService.Advice(CurrentPid, gid, qty, force)!;

    // ---- tableau des objets suivis : rentabilité de chacun d'un coup d'oeil ----
    public ObservableCollection<EcoBoardRow> Board { get; } = new();

    [RelayCommand]
    private void RefreshBoard()
    {
        Board.Clear();
        if (Stores.Launch.GameData is not { } db) return;
        uint pid = CurrentPid;
        decimal tax = pid > 0 ? (decimal)Math.Clamp(Stores.Launch.HdvTaxPercent(pid), 0f, 50f) : 0;
        var rows = new List<(EcoBoardRow Row, decimal Sort)>();
        foreach (var gid in _notebook.TrackedItems.Distinct())
        {
            var a = BuildAdvice(db, gid, 1, null, tax);
            string name = NameOf(gid);
            rows.Add((new EcoBoardRow(gid, name, a.CraftCost is { } c ? c.ToString("N0") : "—", a.BuyCost is { } b ? b.ToString("N0") : "—",
                a.Saving is { } s ? $"{(s >= 0 ? "+" : "")}{s:N0}" + (a.SavingPercent is { } p ? $" ({p:0} %)" : "") : "prix ?",
                a.Saving is > 0 ? "Fabriquer" : a.Saving is < 0 ? "Acheter" : "—", a.Saving is > 0), a.Saving ?? decimal.MinValue));
        }
        foreach (var (row, _) in rows.OrderByDescending(r => r.Sort)) Board.Add(row);
    }

    [RelayCommand] private void OpenBoardItem(EcoBoardRow? r) { if (r is not null) SelectedHit = new EcoItemHit(r.Gid, r.Name, db_level(r.Gid), true); }
    private static int db_level(long gid) => Stores.Launch.GameData?.ItemLevelOf(gid) ?? 0;

    [RelayCommand] private void ForceBuy(EcoCraftRow? r) { if (r is null) return; _force[r.Gid] = CraftForce.Buy; Analyze(); }
    [RelayCommand] private void ForceCraft(EcoCraftRow? r) { if (r is null) return; _force[r.Gid] = CraftForce.Craft; Analyze(); }
    [RelayCommand] private void ForceAuto(EcoCraftRow? r) { if (r is null) return; _force.Remove(r.Gid); Analyze(); }

    [RelayCommand]
    private void ApplyManualPrice()
    {
        if (SelectedRow is not { } r) { CraftStatus = "Sélectionne d'abord une ligne."; return; }
        if (!decimal.TryParse(ManualPriceText.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var price) || price < 0 || price > 10_000_000_000)
        { CraftStatus = "Prix invalide (kamas, 0 = effacer)."; return; }
        var median = FmLedger.Median(new[] { PriceFor(r.Gid) }.Where(q => q is not null).Select(q => q!.Unit));
        bool outlier = price > 0 && FmLedger.IsOutlier(price, median);
        EcoService.Book.Set(CurrentServer, r.Gid, price);
        try { EcoService.SavePrices(); } catch (Exception e) { CraftStatus = "Prix gardé en mémoire seulement : " + e.Message; }
        var keep = r.Gid;
        Analyze();
        SelectedRow = CraftRows.FirstOrDefault(x => x.Gid == keep);
        CraftStatus = price == 0 ? "Prix manuel effacé." : outlier ? "Prix enregistré — attention : il s'écarte de plus de 25 % du prix connu." : "Prix enregistré (serveur " + CurrentServer + ").";
    }

    // ---- import d'une liste de prix collée ----
    [ObservableProperty] public partial string ImportText { get; set; } = "";
    [ObservableProperty] public partial string ImportStatus { get; set; } = "";

    [RelayCommand]
    private void ImportPrices()
    {
        if (Stores.Launch.GameData is not { } db) { ImportStatus = "Données du jeu (dofus.sqlite) introuvables."; return; }
        var lines = PriceImport.Parse(ImportText);
        if (lines.Count == 0) { ImportStatus = "Aucune ligne « nom  prix » reconnue."; return; }
        int ok = 0; var missing = new List<string>();
        int server = CurrentServer;
        foreach (var l in lines.Take(2000))
        {
            var hits = db.FindItems(l.Name, 6);
            var hit = hits.FirstOrDefault(h => string.Equals(h.Name, l.Name, StringComparison.OrdinalIgnoreCase));
            if (hit.Gid == 0 && hits.Count == 1) hit = hits[0];
            if (hit.Gid == 0) { if (missing.Count < 8) missing.Add(l.Name); continue; }
            EcoService.Book.Set(server, hit.Gid, l.Price); ok++;
        }
        try { EcoService.SavePrices(); } catch (Exception e) { ImportStatus = "Prix gardés en mémoire seulement : " + e.Message; return; }
        ImportStatus = $"{ok} prix importé(s)" + (lines.Count - ok > 0 ? $", {lines.Count - ok} nom(s) non reconnu(s)" + (missing.Count > 0 ? " (" + string.Join(", ", missing) + (lines.Count - ok > missing.Count ? "…" : "") + ")" : "") : "") + ".";
        if (ok > 0) { Analyze(); RefreshBoard(); }
    }

    [RelayCommand]
    private void CopyShopping()
    {
        if (ShoppingText.Length == 0) return;
        CopyRequested?.Invoke(ShoppingText);
        CraftStatus = "Liste de courses copiée.";
    }

    // ---- objets suivis ----
    private void ReloadTracked()
    {
        Tracked.Clear();
        var db = Stores.Launch.GameData;
        foreach (var gid in _notebook.TrackedItems.Distinct())
            Tracked.Add(new EcoItemHit(gid, NameOf(gid), db?.ItemLevelOf(gid) ?? 0, HasRecipe(db, gid)));
    }

    [RelayCommand]
    private void ToggleTrack()
    {
        if (SelectedHit is not { } h) return;
        if (!_notebook.TrackedItems.Remove(h.Gid)) _notebook.TrackedItems.Add(h.Gid);
        SaveNotebook(); ReloadTracked(); RefreshBoard();
    }

    [RelayCommand] private void OpenTracked(EcoItemHit? h) { if (h is not null) SelectedHit = h; }
}
