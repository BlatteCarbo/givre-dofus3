using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.ItemCraftPlanner;
using GivreClone.App.Services.Production;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

/// <summary>Catalog boundary. Category must be "Objet" or "Panoplie". Sets reference stable item ids.</summary>
public sealed record ItemCraftCatalogEntry(
    string Id, string Name, string Category, string Provenance, bool IsVerified,
    IReadOnlyList<string>? MemberItemIds = null)
{
    // IsVerified is the adapter's LOCAL FILE HASH check, not publisher/game authenticity.
    public string EvidenceLabel => IsVerified ? "EMPREINTE LOCALE VÉRIFIÉE" : "SOURCE NON VÉRIFIÉE";
    public string CategoryLabel => Category == "Panoplie" ? "PANOPLIE" : "OBJET";
    public string PiecesLabel => Category == "Panoplie"
        ? $"{MemberItemIds?.Count ?? 0} pièce(s) documentée(s)" : "Article individuel";
}

/// <summary>Single target, repeated across sets and free lists. All quantities are deliberate.</summary>
public sealed record ItemCraftTarget(string ItemId, string Name, long Quantity, string GroupName = "")
{
    public string QuantityLabel => $"× {Quantity:N0}";
    public string GroupLabel => string.IsNullOrWhiteSpace(GroupName) ? "Objet sélectionné" : GroupName;
}

public sealed record ItemCraftPlannerRequest(
    string Mode, IReadOnlyList<ItemCraftTarget> Targets,
    IReadOnlyDictionary<string, long> Inventory, string Strategy,
    IReadOnlyDictionary<string, string>? ItemMethodChoices = null);

/// <summary>One graph row. Id identifies a node; Source/Evidence distinguish verified and unknown data.</summary>
public sealed record ItemCraftPlanNode(
    string Id, string ItemName, int Depth, string Kind, string Required,
    string Available, string Missing, string Description, string Provenance,
    IReadOnlyList<string>? AllowedMethods = null);

public sealed record ItemCraftPlanSnapshot(
    string Title, string Explanation, string Provenance, bool IsVerified, bool IsFictional,
    string CostLabel, string DurationLabel, string ItemsLabel,
    IReadOnlyList<ItemCraftPlanNode> Nodes, string Warnings = "", string CoverageLabel = "PARTIEL",
    bool? IsComplete = null);

public sealed record ItemCraftStockRow(string ItemName, long Quantity)
{
    public string QuantityLabel => $"× {Quantity:N0}";
}

public sealed partial class ItemCraftNodeRow : ObservableObject
{
    public ItemCraftNodeRow(ItemCraftPlanNode node)
    {
        Id = node.Id;
        ItemName = node.ItemName;
        Kind = node.Kind;
        Required = node.Required;
        Available = node.Available;
        Missing = node.Missing;
        Description = node.Description;
        Provenance = node.Provenance;
        Depth = Math.Clamp(node.Depth, 0, 12);
        Prefix = new string(' ', Depth * 3) + (Depth == 0 ? "◆ " : "↳ ");
        Methods.Add("À déterminer");
        foreach (var method in (node.AllowedMethods ?? Array.Empty<string>())
                     .Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8))
        {
            if (!Methods.Contains(method)) Methods.Add(method);
        }
    }

    public string Id { get; }
    public string ItemName { get; }
    public string Kind { get; }
    public string Required { get; }
    public string Available { get; }
    public string Missing { get; }
    public string Description { get; }
    public string Provenance { get; }
    public int Depth { get; }
    public string Prefix { get; }
    public ObservableCollection<string> Methods { get; } = new();
    [ObservableProperty] public partial string SelectedMethod { get; set; } = "À déterminer";
}

/// <summary>Offline item / complete-set / free-list composer. No external reads unless a provider is wired.</summary>
public sealed partial class ItemCraftPlannerViewModel : ViewModelBase
{
    public const string SingleMode = "Un objet";
    public const string SetMode = "Panoplie";
    public const string FreeMode = "Liste libre";
    private const int MaxCatalog = 300;
    private const int MaxTargets = 60;
    private const int MaxStock = 100;
    private const int MaxNodesPerPage = 160;
    private const int MaxGraphNodes = 4096;
    private const long MaxQuantity = 100_000;

    private readonly Dictionary<string, long> _stock = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _targetSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _methodChoices = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ItemCraftPlanNode> _graphNodes = new();
    private int _graphPage;
    private bool _graphCapped;
    private bool _graphCappedByProvider;
    private bool _graphExhaustivenessUnknown;
    private bool _loadingDemo;
    private readonly ItemCraftProductionRun _production = new();
    private long _productionUiRevision;
    private readonly ItemCraftProductionAttemptJournal _productionAttempts = new(
        Path.Combine(MZP19LocalClone.DataRoot, "production", "itemcraft-attempts-v1.journal"));

    public Func<IReadOnlyList<ItemCraftCatalogEntry>>? CatalogProvider { get; set; }
    public Func<string, IReadOnlyList<ItemCraftCatalogEntry>>? FolderCatalogProvider { get; set; }
    public Func<ItemCraftPlannerRequest, ItemCraftPlanSnapshot?>? PlanProvider { get; set; }
    /// <summary>Selected character at the moment Lancer is pressed. The shell wires this; the plan itself never sends.</summary>
    public Func<uint?>? ProductionCharacter { get; set; }
    public Func<uint, IItemCraftProductionActions>? ProductionActions { get; set; }
    /// <summary>V12: read-only stock/recipe preview, NEVER a mutation port.</summary>
    public Func<uint, IItemCraftProductionActions>? ProductionPreviewActions { get; set; }
    public Func<long, ItemCraftProductionRecipe?>? ProductionRecipe { get; set; }

    public IReadOnlyList<string> Modes { get; } = [SingleMode, SetMode, FreeMode];
    public IReadOnlyList<string> Strategies { get; } = ["Équilibré", "Privilégier le farm", "Privilégier l'achat", "Privilégier les sous-crafts"];
    public ObservableCollection<ItemCraftCatalogEntry> Catalog { get; } = new();
    public ObservableCollection<ItemCraftCatalogEntry> SearchResults { get; } = new();
    public ObservableCollection<ItemCraftTarget> Targets { get; } = new();
    public ObservableCollection<ItemCraftStockRow> Inventory { get; } = new();
    public ObservableCollection<ItemCraftNodeRow> Nodes { get; } = new();

    [ObservableProperty] public partial string Mode { get; set; } = SingleMode;
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial ItemCraftCatalogEntry? SelectedCatalogEntry { get; set; }
    [ObservableProperty] public partial string QuantityText { get; set; } = "1";
    [ObservableProperty] public partial ItemCraftTarget? SelectedTarget { get; set; }
    [ObservableProperty] public partial string StockItemText { get; set; } = "";
    [ObservableProperty] public partial string StockQuantityText { get; set; } = "";
    [ObservableProperty] public partial ItemCraftStockRow? SelectedStockRow { get; set; }
    [ObservableProperty] public partial string Strategy { get; set; } = "Équilibré";
    [ObservableProperty] public partial ItemCraftNodeRow? SelectedNode { get; set; }
    [ObservableProperty] public partial bool DemoMode { get; private set; }
    [ObservableProperty] public partial bool HasPlan { get; private set; }
    [ObservableProperty] public partial string CatalogFolder { get; private set; } = "Aucun dossier sélectionné";
    [ObservableProperty] public partial string CatalogStatus { get; private set; } =
        "Catalogue non chargé · importer une source locale vérifiée.";
    [ObservableProperty] public partial string Status { get; private set; } =
        "Choisis un objet ou une panoplie documentés, ou ouvre la démonstration fictive.";
    [ObservableProperty] public partial string PlanTitle { get; private set; } = "Ton atelier attend ses objectifs";
    [ObservableProperty] public partial string PlanDescription { get; private set; } =
        "L'arbre des sous-crafts se construira à partir de recettes vérifiées et du stock saisi.";
    [ObservableProperty] public partial string PlanSource { get; private set; } = "Aucune recette vérifiée";
    [ObservableProperty] public partial string Cost { get; private set; } = "Inconnu";
    [ObservableProperty] public partial string Duration { get; private set; } = "Inconnue";
    [ObservableProperty] public partial string PlannedItems { get; private set; } = "Inconnus";
    [ObservableProperty] public partial string Coverage { get; private set; } = "NON VÉRIFIÉ";
    [ObservableProperty] public partial string Warnings { get; private set; } =
        "Quantités, drops, prix et recettes inconnus tant que les preuves locales sont absentes.";
    [ObservableProperty] public partial string Simulation { get; private set; } = "Aucune simulation effectuée.";
    [ObservableProperty] public partial string Completeness { get; private set; } = "COMPLÉTUDE INCONNUE";
    [ObservableProperty] public partial string TargetEvidenceStatus { get; private set; } = "Aucune cible à vérifier.";
    [ObservableProperty] public partial string BudgetText { get; set; } = "";
    [ObservableProperty] public partial string CraftCostText { get; set; } = "";
    [ObservableProperty] public partial string SaleFloorText { get; set; } = "";
    [ObservableProperty] public partial bool BankAllowed { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunProductionCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopProductionCommand))]
    public partial bool IsProductionRunning { get; private set; }
    [ObservableProperty] public partial string ProductionProgress { get; private set; } = "Aucune fabrication lancée.";
    [ObservableProperty] public partial string ProductionStop { get; private set; } = "";
    [ObservableProperty] public partial int ProductionProduced { get; private set; }
    [ObservableProperty] public partial long ProductionSpent { get; private set; }
    [ObservableProperty] public partial int ProductionListed { get; private set; }
    [ObservableProperty] public partial string ProductionRecoveryStatus { get; private set; } =
        "Journal de production : aucun personnage vérifié sélectionné.";

    public bool HasCatalog => Catalog.Count != 0;
    public bool HasResults => SearchResults.Count != 0;
    public bool HasTargets => Targets.Count != 0;
    public bool HasInventory => Inventory.Count != 0;
    public bool HasSelectedNode => SelectedNode is not null;
    public string TargetCount => $"{Targets.Count} pièce(s) / objectif(s)";
    public string StockCount => $"{Inventory.Count} ressource(s) saisie(s)";
    public string GraphCount => $"{Nodes.Count} affiché(s) / {_graphNodes.Count} fourni(s)";
    public string GraphPageLabel => _graphNodes.Count == 0 ? "Aucune page"
        : $"Page {_graphPage + 1} / {(_graphNodes.Count - 1) / MaxNodesPerPage + 1}";
    public bool HasPreviousGraphPage => _graphPage > 0;
    public bool HasNextGraphPage => (_graphPage + 1) * MaxNodesPerPage < _graphNodes.Count;
    public bool IsGraphLimited => _graphCapped || _graphCappedByProvider || _graphExhaustivenessUnknown;
    public string GraphLimitLabel => _graphCapped
        ? $"Plus de {MaxGraphNodes:N0} nœuds reçus : aperçu borné, dépendances non affichées = UNKNOWN."
        : _graphCappedByProvider
            ? "Le fournisseur a limité son arbre à 160 nœuds : le reste est UNKNOWN et inaccessible ici."
            : _graphExhaustivenessUnknown
                ? "160 nœuds reçus : l'adaptateur possède un plafond à 160, l'exhaustivité reste UNKNOWN."
            : "";
    public string SearchCount => $"{SearchResults.Count} résultat(s) affiché(s) / {Catalog.Count} fiche(s) reçue(s)";
    public string ModeSubtitle => Mode switch
    {
        SetMode => "Sélectionne un ensemble avec une liste de pièces documentée",
        FreeMode => "Compose librement plusieurs objets et leurs quantités",
        _ => "Prépare un article et sa quantité"
    };
    public string DemoBanner => DemoMode ? "DÉMONSTRATION FICTIVE · DONNÉES INVENTÉES" : "DONNÉES SOURCÉES UNIQUEMENT";
    public string FooterDisclaimer => DemoMode
        ? "DÉMO FICTIVE : pièces, matériaux, quantités et méthodes inventés. Aucun drop, prix ou recette de Dofus n'est attesté."
        : "Hors jeu · toute route de farm, recette, disponibilité ou comparaison de coûts doit être vérifiée par l'adaptateur.";

    public ItemCraftPlannerViewModel() { }

    partial void OnModeChanged(string value)
    {
        if (_loadingDemo) return;
        if (DemoMode) LeaveDemo();
        Targets.Clear();
        _targetSources.Clear();
        SelectedTarget = null;
        OnPropertyChanged(nameof(HasTargets));
        OnPropertyChanged(nameof(TargetCount));
        OnPropertyChanged(nameof(ModeSubtitle));
        FilterCatalog();
        InvalidatePlan();
        Status = "Mode changé : sélectionne les articles correspondant à ton objectif.";
    }

    partial void OnSearchTextChanged(string value)
    {
        FilterCatalog();
        if (!DemoMode)
            CatalogStatus = "Recherche sur les fiches déjà reçues. Clique « Rechercher tout le catalogue » pour interroger les données locales.";
    }
    partial void OnQuantityTextChanged(string value)
    {
        if (!DemoMode || _loadingDemo || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var q)
            || q is < 1 or > MaxQuantity) return;
        for (int i = 0; i < Targets.Count; i++) Targets[i] = Targets[i] with { Quantity = q };
        BuildDemo();
    }
    partial void OnStrategyChanged(string value) { if (!_loadingDemo) InvalidatePlan(); }
    partial void OnSelectedNodeChanged(ItemCraftNodeRow? value) => OnPropertyChanged(nameof(HasSelectedNode));
    partial void OnDemoModeChanged(bool value)
    {
        OnPropertyChanged(nameof(DemoBanner));
        OnPropertyChanged(nameof(FooterDisclaimer));
    }

    private void FilterCatalog()
    {
        var selectedId = SelectedCatalogEntry?.Id;
        SearchResults.Clear();
        var desiredCategory = Mode == SetMode ? SetMode : "Objet";
        foreach (var entry in Catalog.Where(e => e.Category == desiredCategory &&
                     (e.Name.Contains(SearchText ?? "", StringComparison.OrdinalIgnoreCase)
                      || e.Id.Contains(SearchText ?? "", StringComparison.OrdinalIgnoreCase))).Take(50))
            SearchResults.Add(entry);
        SelectedCatalogEntry = SearchResults.FirstOrDefault(e => e.Id == selectedId) ?? SearchResults.FirstOrDefault();
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(SearchCount));
    }

    private static bool IsTrusted(ItemCraftCatalogEntry? entry) =>
        entry is { IsVerified: true } && !string.IsNullOrWhiteSpace(entry.Id)
        && !string.IsNullOrWhiteSpace(entry.Name) && !string.IsNullOrWhiteSpace(entry.Provenance)
        && !entry.Provenance.Contains("FICTIF", StringComparison.OrdinalIgnoreCase);

    /// <summary>Only verified entries enter the real catalog; fictional fixtures use ShowDemo explicitly.</summary>
    public void LoadCatalog(IEnumerable<ItemCraftCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (DemoMode) ExitDemo(); // A real import never carries fictional demonstration targets forward.
        var previousId = SelectedCatalogEntry?.Id;
        Catalog.Clear();
        var seen = new HashSet<(string Category, string Id)>();
        bool capped = false;
        foreach (var entry in entries)
        {
            if (!IsTrusted(entry) || entry.Category is not ("Objet" or SetMode)) continue;
            // Numeric set ids and item GIDs may overlap. Never deduplicate
            // across distinct categories: this hid verified equipment sets.
            if (!seen.Add((entry.Category, entry.Id.ToUpperInvariant()))) continue;
            if (Catalog.Count >= MaxCatalog) { capped = true; break; }
            Catalog.Add(entry);
        }
        FilterCatalog();
        SelectedCatalogEntry = SearchResults.FirstOrDefault(e => e.Id == previousId) ?? SelectedCatalogEntry;
        // The adapter hands over a filtered/limited page (<= 300), not the whole
        // game's item catalog. Never silently remove selected targets on refresh.
        // Verify the original evidence again before requesting a new plan.
        UpdateTargetEvidenceStatus();
        NotifyTargetsChanged();
        CatalogStatus = Catalog.Count == 0
            ? "Aucune fiche locale vérifiée pour cette recherche · anciennes cibles conservées mais à revérifier."
            : $"{Catalog.Count} fiche(s) à empreinte locale vérifiée · périmètre reçu, non catalogue exhaustif"
              + (capped ? " · résultat plafonné à 300 : affine la recherche" : "");
        OnPropertyChanged(nameof(HasCatalog));
        InvalidatePlan();
    }

    private bool UpdateTargetEvidenceStatus()
    {
        if (Targets.Count == 0)
        {
            TargetEvidenceStatus = "Aucune cible à vérifier.";
            return true;
        }
        int missing = 0, changed = 0;
        foreach (var target in Targets)
        {
            var now = Catalog.FirstOrDefault(e => e.Category == "Objet"
                && e.Id.Equals(target.ItemId, StringComparison.OrdinalIgnoreCase) && IsTrusted(e));
            if (now is null) missing++;
            else if (!_targetSources.TryGetValue(target.ItemId, out var before)
                     || !string.Equals(now.Provenance, before, StringComparison.Ordinal)) changed++;
        }
        TargetEvidenceStatus = missing == 0 && changed == 0
            ? $"{Targets.Count} cible(s) retrouvée(s) dans la même provenance locale."
            : $"{Targets.Count} cible(s) conservée(s), dont {missing} absente(s) du lot et {changed} à provenance différente/inconnue. Revérifie ces pièces dans le catalogue avant de calculer.";
        return missing == 0 && changed == 0;
    }

    /// <summary>Ask the injected full-catalog adapter about SearchText without running the craft engine.</summary>
    [RelayCommand]
    private void SearchCatalog()
    {
        if (DemoMode)
        {
            Status = "La recherche réelle est séparée de la démo : ferme-la d'abord.";
            return;
        }
        try
        {
            if (FolderCatalogProvider is not null && CatalogFolder != "Aucun dossier sélectionné")
                LoadCatalog(FolderCatalogProvider(CatalogFolder) ?? Array.Empty<ItemCraftCatalogEntry>());
            else if (CatalogProvider is not null)
                LoadCatalog(CatalogProvider() ?? Array.Empty<ItemCraftCatalogEntry>());
            else
            {
                Status = "Aucun lecteur de catalogue raccordé.";
                return;
            }
            Status = $"Recherche terminée : {SearchCount}. Les objectifs précédents restent conservés.";
        }
        catch (Exception ex)
        {
            Status = $"Recherche locale indisponible : {ex.Message}. Cibles conservées.";
        }
    }

    public void SetCatalogFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        CatalogFolder = folder;
        CatalogStatus = "Dossier choisi. Actualise pour lire les sources avec le fournisseur configuré.";
        Status = "Catalogue local sélectionné ; lecture et vérification assurées par l'adaptateur.";
        if (!DemoMode) InvalidatePlan();
    }

    public void NotifyPickerError(string message) => Status = message;

    [RelayCommand]
    private void AddSelection()
    {
        if (DemoMode) { Status = "La démo est isolée : quitte-la pour choisir un vrai article."; return; }
        if (!TryQuantity(out var quantity)) return;
        var entry = SelectedCatalogEntry;
        if (!IsTrusted(entry) || entry is null || !Catalog.Contains(entry))
        {
            Status = "Sélectionne un objet ou une panoplie vérifiés dans le catalogue.";
            return;
        }
        if (Mode == SetMode)
        {
            if (entry.Category != SetMode || entry.MemberItemIds is not { Count: > 0 and <= MaxTargets })
            {
                Status = "Cette panoplie ne possède pas de liste de pièces vérifiable.";
                return;
            }
            if (entry.MemberItemIds.Any(id => string.IsNullOrWhiteSpace(id)) ||
                entry.MemberItemIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != entry.MemberItemIds.Count)
            {
                Status = "Panoplie refusée : un identifiant de pièce est vide ou dupliqué dans la source.";
                return;
            }
            var ids = entry.MemberItemIds.ToArray();
            var items = ids.Select(id => Catalog.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase)
                     && e.Category == "Objet" && IsTrusted(e)
                     && string.Equals(e.Provenance, entry.Provenance, StringComparison.Ordinal))).ToArray();
            if (items.Any(i => i is null))
            {
                Status = "Liste de pièces incomplète : membre absent ou provenance différente de la panoplie.";
                return;
            }
            Targets.Clear();
            _targetSources.Clear();
            foreach (var item in items)
            {
                Targets.Add(new ItemCraftTarget(item!.Id, item.Name, quantity, entry.Name));
                _targetSources[item.Id] = item.Provenance;
            }
        }
        else
        {
            if (entry.Category != "Objet") { Status = "Sélectionne une fiche d'objet vérifiée."; return; }
            if (Mode == SingleMode) Targets.Clear();
            var previous = Targets.FirstOrDefault(t => t.ItemId.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));
            if (previous is null && Targets.Count >= MaxTargets)
            {
                Status = $"Maximum {MaxTargets} objets par plan.";
                return;
            }
            if (previous is not null) Targets.Remove(previous); // explicit update, not duplicated stock
            Targets.Add(new ItemCraftTarget(entry.Id, entry.Name, quantity));
            _targetSources[entry.Id] = entry.Provenance;
        }
        NotifyTargetsChanged();
        UpdateTargetEvidenceStatus();
        InvalidatePlan();
        Status = $"Cible actualisée : {Targets.Count} pièce(s), besoins à recalculer.";
    }

    [RelayCommand]
    private void RemoveTarget(ItemCraftTarget? row)
    {
        if (row is null || !Targets.Remove(row)) return;
        if (DemoMode) { ExitDemo(); return; }
        _targetSources.Remove(row.ItemId);
        NotifyTargetsChanged();
        UpdateTargetEvidenceStatus();
        InvalidatePlan();
        Status = "Objet retiré de la liste.";
    }

    [RelayCommand]
    private void ClearTargets()
    {
        if (DemoMode) { ExitDemo(); return; }
        Targets.Clear();
        _targetSources.Clear();
        NotifyTargetsChanged();
        UpdateTargetEvidenceStatus();
        InvalidatePlan();
    }

    private void NotifyTargetsChanged()
    {
        OnPropertyChanged(nameof(HasTargets));
        OnPropertyChanged(nameof(TargetCount));
    }

    [RelayCommand]
    private void AddStock()
    {
        var item = (StockItemText ?? "").Trim();
        if (item.Length is 0 or > 96 || item.Any(char.IsControl))
        {
            Status = "Nom d'ingrédient attendu, entre 1 et 96 caractères.";
            return;
        }
        if (!long.TryParse(StockQuantityText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var amount)
            || amount < 0 || amount > 1_000_000_000)
        {
            Status = "Quantité de stock : entier de 0 à 1 000 000 000.";
            return;
        }
        if (_stock.Count >= MaxStock && !_stock.ContainsKey(item))
        {
            Status = $"Stock limité à {MaxStock} ressources distinctes.";
            return;
        }
        _stock[item] = amount;
        StockItemText = "";
        StockQuantityText = "";
        RefreshStock();
        if (DemoMode) BuildDemo(); else InvalidatePlan();
        Status = $"Stock local actualisé : {item} × {amount:N0}.";
    }

    [RelayCommand]
    private void RemoveStock(ItemCraftStockRow? row)
    {
        if (row is null || !_stock.Remove(row.ItemName)) return;
        RefreshStock();
        if (DemoMode) BuildDemo(); else InvalidatePlan();
    }

    private void RefreshStock()
    {
        Inventory.Clear();
        foreach (var item in _stock.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            Inventory.Add(new ItemCraftStockRow(item.Key, item.Value));
        OnPropertyChanged(nameof(HasInventory));
        OnPropertyChanged(nameof(StockCount));
    }

    private bool TryQuantity(out long quantity)
    {
        if (!long.TryParse(QuantityText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out quantity)
            || quantity is < 1 or > MaxQuantity)
        {
            Status = $"Quantité demandée : entier de 1 à {MaxQuantity:N0}.";
            return false;
        }
        return true;
    }

    [RelayCommand]
    private void RefreshPlan()
    {
        RememberGraphMethods();
        var methods = new Dictionary<string, string>(_methodChoices, StringComparer.OrdinalIgnoreCase);
        if (DemoMode)
        {
            BuildDemo();
            RestoreMethods(methods);
            Status = "Démo fictive recalculée après prise en compte du stock.";
            return;
        }
        if (FolderCatalogProvider is not null && CatalogFolder != "Aucun dossier sélectionné")
        {
            try { LoadCatalog(FolderCatalogProvider(CatalogFolder) ?? Array.Empty<ItemCraftCatalogEntry>()); }
            catch (Exception ex) { InvalidatePlan(); Status = $"Catalogue indisponible : {ex.Message}. Cibles conservées."; return; }
        }
        else if (CatalogProvider is not null)
        {
            try { LoadCatalog(CatalogProvider() ?? Array.Empty<ItemCraftCatalogEntry>()); }
            catch (Exception ex) { InvalidatePlan(); Status = $"Catalogue indisponible : {ex.Message}. Cibles conservées."; return; }
        }
        if (Targets.Count == 0)
        {
            InvalidatePlan(); Status = "Ajoute un objet ou les pièces d'une panoplie avant de calculer."; return;
        }
        if (!UpdateTargetEvidenceStatus())
        {
            InvalidatePlan();
            Status = "Cibles conservées mais preuves absentes/modifiées dans la page chargée ; re-sélectionne une source vérifiée.";
            return;
        }
        if (PlanProvider is null)
        {
            InvalidatePlan(); Status = "Le moteur de nomenclature multi-objets n'est pas encore raccordé."; return;
        }
        try
        {
            var request = new ItemCraftPlannerRequest(Mode, Targets.ToArray(),
                new Dictionary<string, long>(_stock, StringComparer.OrdinalIgnoreCase), Strategy, methods);
            var snapshot = PlanProvider(request);
            if (snapshot is null || !snapshot.IsVerified || snapshot.IsFictional)
            {
                InvalidatePlan();
                Status = snapshot is { IsVerified: false, IsFictional: false }
                    ? $"Plan indisponible (UNKNOWN) : {snapshot.Explanation}"
                    : "Aucun graphe avec preuve suffisante ; ne pas afficher de recettes supposées.";
                return;
            }
            LoadPlan(snapshot);
            RestoreMethods(methods);
            Status = $"Plan hors jeu calculé · {Completeness} · provenance locale non certifiée en jeu.";
        }
        catch (Exception ex)
        {
            InvalidatePlan(); Status = $"Planification interrompue : {ex.Message}";
        }
    }

    private void RestoreMethods(IReadOnlyDictionary<string, string> methods)
    {
        _methodChoices.Clear();
        // An old plan must not lend unsupported/unknown actions to the new graph.
        foreach (var choice in methods)
            if (_graphNodes.Any(n => string.Equals(n.Id, choice.Key, StringComparison.OrdinalIgnoreCase)
                    && n.AllowedMethods?.Contains(choice.Value) == true))
                _methodChoices[choice.Key] = choice.Value;
        foreach (var node in Nodes)
            if (_methodChoices.TryGetValue(node.Id, out var previous) && node.Methods.Contains(previous))
                node.SelectedMethod = previous;
    }

    private void RememberGraphMethods()
    {
        foreach (var node in Nodes)
        {
            if (node.SelectedMethod == "À déterminer") _methodChoices.Remove(node.Id);
            else _methodChoices[node.Id] = node.SelectedMethod;
        }
    }

    private void NotifyGraphPageChanged()
    {
        OnPropertyChanged(nameof(GraphPageLabel));
        OnPropertyChanged(nameof(GraphCount));
        OnPropertyChanged(nameof(HasPreviousGraphPage));
        OnPropertyChanged(nameof(HasNextGraphPage));
        OnPropertyChanged(nameof(IsGraphLimited));
        OnPropertyChanged(nameof(GraphLimitLabel));
    }

    private void RenderGraphPage(string? preservedSelectedId = null)
    {
        Nodes.Clear();
        foreach (var node in _graphNodes.Skip(_graphPage * MaxNodesPerPage).Take(MaxNodesPerPage))
        {
            var row = new ItemCraftNodeRow(node);
            if (_methodChoices.TryGetValue(row.Id, out var method) && row.Methods.Contains(method))
                row.SelectedMethod = method;
            Nodes.Add(row);
        }
        SelectedNode = Nodes.FirstOrDefault(n => n.Id == preservedSelectedId) ?? Nodes.FirstOrDefault();
        NotifyGraphPageChanged();
    }

    [RelayCommand]
    private void NextGraphPage()
    {
        if (!HasNextGraphPage) return;
        RememberGraphMethods();
        _graphPage++;
        RenderGraphPage();
    }

    [RelayCommand]
    private void PreviousGraphPage()
    {
        if (!HasPreviousGraphPage) return;
        RememberGraphMethods();
        _graphPage--;
        RenderGraphPage();
    }

    /// <summary>Providers can push a snapshot; a verified local hash does not imply complete crafting evidence.</summary>
    public void LoadPlan(ItemCraftPlanSnapshot? snapshot)
    {
        if (snapshot is null || !snapshot.IsVerified && !snapshot.IsFictional || snapshot.IsFictional && !DemoMode)
        {
            InvalidatePlan(); return;
        }
        _methodChoices.Clear();
        _graphNodes.Clear();
        var all = snapshot.Nodes ?? Array.Empty<ItemCraftPlanNode>();
        foreach (var node in all.Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Id)
                     && !string.IsNullOrWhiteSpace(e.ItemName)).Take(MaxGraphNodes)) _graphNodes.Add(node);
        _graphPage = 0;
        _graphCapped = all.Count > MaxGraphNodes;
        _graphCappedByProvider = snapshot.Warnings?.Contains("Arborescence limitée à 160", StringComparison.OrdinalIgnoreCase) == true;
        // The current Access adapter caps at 160 before the UI sees its snapshot.
        // A full 160-row page is therefore of unknown exhaustiveness even when
        // upstream warnings are shortened to 15 messages.
        _graphExhaustivenessUnknown = !snapshot.IsFictional && all.Count == MaxNodesPerPage && !_graphCappedByProvider;
        RenderGraphPage();
        PlanTitle = snapshot.Title;
        PlanDescription = snapshot.Explanation;
        PlanSource = snapshot.Provenance;
        Cost = string.IsNullOrWhiteSpace(snapshot.CostLabel) ? "Inconnu" : snapshot.CostLabel;
        Duration = string.IsNullOrWhiteSpace(snapshot.DurationLabel) ? "Inconnue" : snapshot.DurationLabel;
        PlannedItems = string.IsNullOrWhiteSpace(snapshot.ItemsLabel) ? "Inconnus" : snapshot.ItemsLabel;
        Coverage = snapshot.IsFictional ? "FICTIF · EXEMPLE" :
            _graphCapped ? "PARTIEL · GRAPHE BORNÉ" :
            string.IsNullOrWhiteSpace(snapshot.CoverageLabel) ? "COMPLÉTUDE NON TRANSMISE" : snapshot.CoverageLabel;
        Completeness = snapshot.IsFictional ? "EXEMPLE FICTIF" : _graphCapped || _graphCappedByProvider
            ? "PARTIEL · DEPENDANCES UNKNOWN"
            : _graphExhaustivenessUnknown
                ? "EXHAUSTIVITÉ UNKNOWN · LIMITE FOURNISSEUR POSSIBLE"
            : snapshot.IsComplete switch
            {
                true => "COMPLET SELON MOTEUR · À VÉRIFIER EN JEU",
                false => "INCOMPLET · UNKNOWN",
                _ => Coverage.Contains("PARTIEL", StringComparison.OrdinalIgnoreCase)
                    ? "PARTIEL · UNKNOWN" : "ISCOMPLETE NON TRANSMIS · UNKNOWN"
            };
        Warnings = string.IsNullOrWhiteSpace(snapshot.Warnings) ? "Certaines méthodes peuvent rester indéterminées." : snapshot.Warnings;
        if (_graphCapped) Warnings += " • Arbre UI plafonné à 4 096 nœuds ; les nœuds non affichés restent UNKNOWN.";
        HasPlan = _graphNodes.Count > 0;
        Simulation = "Choisis les méthodes disponibles puis lance une simulation consultative.";
        NotifyGraphPageChanged();
    }

    private void InvalidatePlan()
    {
        if (_loadingDemo) return;
        _graphNodes.Clear();
        _methodChoices.Clear();
        _graphPage = 0;
        _graphCapped = false;
        _graphCappedByProvider = false;
        _graphExhaustivenessUnknown = false;
        Nodes.Clear();
        SelectedNode = null;
        HasPlan = false;
        PlanTitle = "Plan à construire";
        PlanDescription = "Choisis des objets et actualise pour calculer l'ensemble des besoins.";
        PlanSource = "Aucun calcul vérifié";
        Cost = "Inconnu";
        Duration = "Inconnue";
        PlannedItems = "Inconnus";
        Coverage = "NON VÉRIFIÉ";
        Completeness = "COMPLÉTUDE INCONNUE";
        Warnings = "Quantités, drops, prix et recettes inconnus tant que les preuves locales sont absentes.";
        Simulation = "Aucun plan à simuler.";
        NotifyGraphPageChanged();
    }

    [RelayCommand]
    private void Simulate()
    {
        if (!HasPlan)
        {
            Simulation = "Impossible de simuler : aucun arbre de fabrication disponible."; return;
        }
        RememberGraphMethods();
        int chosen = _graphNodes.Count(node => _methodChoices.ContainsKey(node.Id));
        var unknown = _graphNodes.Count - chosen;
        Simulation = DemoMode
            ? $"DÉMO FICTIVE · {Targets.Count} pièces · {_graphNodes.Count} nœuds · {unknown} méthodes indéterminées. Aucun résultat de jeu réel."
            : $"Simulation locale : {Targets.Count} cibles, {_graphNodes.Count} nœuds reçus, {unknown} méthodes non choisies. {Completeness}. Les branches non transmises restent UNKNOWN.";
    }

    [RelayCommand]
    private void ShowDemo()
    {
        _loadingDemo = true;
        try
        {
            Mode = SetMode;
            SearchText = "";
            QuantityText = "1";
            Catalog.Clear();
            _targetSources.Clear();
            // All names are invented teaching placeholders; the real Bouftou piece list is not implied.
            Catalog.Add(new ItemCraftCatalogEntry("fictive-set", "Panoplie Bouftou · illustration fictive",
                SetMode, "DÉMO FICTIVE", false, ["fictive-a", "fictive-b", "fictive-c"]));
            Catalog.Add(new ItemCraftCatalogEntry("fictive-a", "Pièce A d'exercice (FICTIVE)", "Objet", "DÉMO FICTIVE", false));
            Catalog.Add(new ItemCraftCatalogEntry("fictive-b", "Pièce B d'exercice (FICTIVE)", "Objet", "DÉMO FICTIVE", false));
            Catalog.Add(new ItemCraftCatalogEntry("fictive-c", "Pièce C d'exercice (FICTIVE)", "Objet", "DÉMO FICTIVE", false));
            Targets.Clear();
            foreach (var item in Catalog.Where(i => i.Category == "Objet"))
                Targets.Add(new ItemCraftTarget(item.Id, item.Name, 1, "Panoplie Bouftou · FICTIVE"));
            Strategy = "Équilibré";
        }
        finally { _loadingDemo = false; }
        DemoMode = true;
        FilterCatalog();
        NotifyTargetsChanged();
        TargetEvidenceStatus = "DÉMO FICTIVE · aucune validation de pièces réelles.";
        OnPropertyChanged(nameof(HasCatalog));
        CatalogStatus = "DÉMO FICTIVE · les trois pièces et tous leurs ingrédients sont inventés";
        BuildDemo();
        Status = "Démonstration activée : trois pièces imaginaires, ressources et sous-craft fictifs.";
    }

    private void BuildDemo()
    {
        if (!DemoMode) return;
        // Deliberately invented ratios. Allocate inventory ONCE across the whole graph, in stable order.
        long q = 1;
        if (Targets.Count > 0) q = Math.Clamp(Targets[0].Quantity, 1, MaxQuantity);
        var remaining = new Dictionary<string, long>(_stock, StringComparer.OrdinalIgnoreCase);
        long Consume(string key, long demanded)
        {
            long available = remaining.TryGetValue(key, out var stock) ? stock : 0;
            long used = Math.Min(demanded, available);
            remaining[key] = available - used;
            return used;
        }
        string Num(long value) => value.ToString("N0", CultureInfo.GetCultureInfo("fr-FR"));
        var nodes = new List<ItemCraftPlanNode>();
        void Root(string id, string title)
        {
            nodes.Add(new ItemCraftPlanNode(id, title, 0, "OBJET FICTIF", $"{Num(q)} (fictif)", "0",
                $"{Num(q)} (fictif)", "Pièce pédagogique imaginaire", "DÉMO FICTIVE", ["Craft fictif", "Acheter fictivement"]));
        }
        long Material(string id, string name, long required, int depth, bool subCraft)
        {
            var used = Consume(name, required);
            nodes.Add(new ItemCraftPlanNode(id, name, depth, subCraft ? "SOUS-CRAFT FICTIF" : "RESSOURCE FICTIVE",
                $"{Num(required)} (fictif)", Num(used), Num(required - used),
                subCraft ? "Transformable, recette inventée" : "Obtenable, provenance imaginaire",
                "DÉMO FICTIVE · ratio hypothétique", subCraft
                    ? ["Sous-craft fictif", "Achat fictif"] : ["Farm fictif", "Achat fictif"]));
            return required - used;
        }
        Root("demo-a", "Pièce A d'exercice (FICTIVE)");
        long missingA = Material("demo-a-thread", "Fil pédagogique fictif", 2*q, 1, true);
        Material("demo-a-fiber", "Fibre pédagogique fictive", missingA*2, 2, false);
        Root("demo-b", "Pièce B d'exercice (FICTIVE)");
        long missingB = Material("demo-b-thread", "Fil pédagogique fictif", q, 1, true);
        Material("demo-b-fiber", "Fibre pédagogique fictive", missingB*2, 2, false);
        Root("demo-c", "Pièce C d'exercice (FICTIVE)");
        Material("demo-c-fiber", "Fibre pédagogique fictive", q, 1, false);

        var fiberDemand = (missingA + missingB)*2 + q;
        var fiberStock = Math.Min(_stock.TryGetValue("Fibre pédagogique fictive", out var f) ? f : 0, fiberDemand);
        var threadStock = Math.Min(_stock.TryGetValue("Fil pédagogique fictif", out var t) ? t : 0, 3*q);
        LoadPlan(new ItemCraftPlanSnapshot("Panoplie Bouftou · démonstration fictive",
            "Trois pièces inventées : les fibres et sous-crafts communs utilisent le même stock global, sans double déduction. Les ratios sont purement pédagogiques.",
            "DÉMO FICTIVE · aucune donnée réelle de la panoplie Bouftou", false, true,
            "Inconnu", "Inconnue", $"3 pièces × {Num(q)} (fictif)", nodes,
            $"Inventé : fibres à traiter {Num(fiberDemand)}, stock employé {Num(fiberStock)} ; fils {Num(3*q)}, stock employé {Num(threadStock)}. Aucune information de drop, recette, HDV ou XP.",
            "FICTIF"));
    }

    private void ExitDemo()
    {
        DemoMode = false;
        Catalog.Clear();
        SearchResults.Clear();
        Targets.Clear();
        _targetSources.Clear();
        NotifyTargetsChanged();
        UpdateTargetEvidenceStatus();
        OnPropertyChanged(nameof(HasCatalog));
        OnPropertyChanged(nameof(HasResults));
        SelectedCatalogEntry = null;
        CatalogStatus = "Catalogue non chargé · importer une source locale vérifiée.";
        InvalidatePlan();
    }

    [RelayCommand]
    private void LeaveDemo()
    {
        ExitDemo();
        Status = "Démonstration fermée : aucun objet ou recette réel ne sera affiché sans source vérifiée.";
    }

    private bool CanRunProduction() => !IsProductionRunning;
    private bool CanStopProduction() => IsProductionRunning;

    [RelayCommand(CanExecute = nameof(CanStopProduction))]
    private void StopProduction() => _production.Stop();

    [RelayCommand]
    private void RefreshProductionRecovery()
    {
        if (ProductionCharacter?.Invoke() is not uint pid ||
            !TryResolveProductionIdentity(pid, out string scope, out _, out _, out _))
        {
            ProductionRecoveryStatus = "BLOQUÉ · identité personnage/serveur non observée. Aucune production ni reprise autorisée.";
            return;
        }
        var state = _productionAttempts.ReadStatus(scope);
        ProductionRecoveryStatus = FormatProductionRecovery(state);
    }

    /// <summary>
    /// The production journal has no authenticated reconciliation/clear API.
    /// The existing panel's status and refresh button show a persistent,
    /// actionable blockade, never a fake opt-in to reset an ambiguous attempt.
    /// </summary>
    public static string FormatProductionRecovery(ItemCraftAttemptSnapshot state) =>
        state.Phase switch
        {
            ItemCraftAttemptPhase.None =>
                "PRÊT · aucune tentative durable en attente. Lancer démarre une nouvelle fabrication.",
            ItemCraftAttemptPhase.Completed =>
                "TERMINÉ · dernier cycle confirmé et clôturé dans le journal. Nouvelle fabrication uniquement par Lancer.",
            ItemCraftAttemptPhase.Started =>
                "BLOQUÉ · tentative persistée interrompue ou toujours en cours : rapprochement manuel des reçus et du stock requis. Aucune relance automatique.",
            ItemCraftAttemptPhase.Uncertain =>
                "BLOQUÉ · production indéterminée ou journal inaccessible : " + state.Reason +
                ". Conserver les données et rapprocher les opérations ; aucune reprise automatique ni purge.",
            _ => "BLOQUÉ · état du journal inconnu, intervention nécessaire."
        };

    private bool TryResolveProductionIdentity(uint pid, out string scope,
        out string characterName, out int server, out long characterId)
    {
        scope = "";
        characterName = Stores.Launch.SelfCharacterInfo(pid)?.Name ?? "";
        server = Stores.Launch.ServerIdOf(pid);
        characterId = Stores.Launch.CharacterId(pid);
        if (pid == 0 || server <= 0 || characterId <= 0 || string.IsNullOrWhiteSpace(characterName))
            return false;
        scope = HdvProfileKey.For(characterName, server);
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanRunProduction))]
    private async Task RunProduction()
    {
        // V12 guided mode is opt-in: a real preflight must have persisted
        // Ready for THIS objective, not merely an old recipe with the same
        // result GID. Normal manual production retains its V9 path.
        var guidedGoal = GuidedActivityGoal;
        if (guidedGoal is not null &&
            (!GuidedActivityTargetStillMatches(guidedGoal) ||
             _guidedWorkflow.Read(guidedGoal.ServerId, guidedGoal.CharacterId)
                 .Phase != GuidedProductionWorkflowPhase.Ready))
        {
            Refuse("Objectif guidé V12 non prêt, remplacé ou encore incertain. " +
                "Utilise « Vérifier ressources » avant Lancer ; aucune action envoyée.");
            return;
        }
        // Read the persistent boundary BEFORE creating a new production request
        // or adapter. This is user-visible and deliberately not a clear/retry.
        RefreshProductionRecovery();
        if (ProductionCharacter?.Invoke() is uint selectedPid &&
            TryResolveProductionIdentity(selectedPid, out string selectedScope,
                out _, out _, out _))
        {
            var persisted = _productionAttempts.ReadStatus(selectedScope);
            if (persisted.Phase is ItemCraftAttemptPhase.Started or ItemCraftAttemptPhase.Uncertain)
            {
                Refuse(FormatProductionRecovery(persisted));
                return;
            }
        }
        if (!BeginProduction(out var request, out var actions)) return;
        string? guidedFingerprint = null;
        if (guidedGoal is not null)
        {
            guidedFingerprint = ItemCraftProductionAttemptJournal.RequestFingerprint(request);
            var flow = _guidedWorkflow.Read(guidedGoal.ServerId, guidedGoal.CharacterId);
            if (!GuidedActivityTargetStillMatches(guidedGoal) ||
                flow.Phase != GuidedProductionWorkflowPhase.Ready ||
                flow.GoalHash != GuidedProductionWorkflowLedger.Fingerprint(guidedGoal) ||
                flow.RequestHash != guidedFingerprint ||
                !GuidedSupplyCheckpointStillCurrent(guidedGoal, request))
            {
                Refuse("Objectif guidé changé depuis la pré-vérification : " +
                    "source, stock, révision, budget, recette ou quantité modifiés. " +
                    "Relance « Vérifier ressources », sans émission.");
                return;
            }
        }
        if (ProductionCharacter?.Invoke() is not uint pid ||
            !TryResolveProductionIdentity(pid, out string scope, out string name,
                out int server, out long characterId))
        {
            Refuse("Fabrication refusée : identité serveur/personnage non observée.");
            RefreshProductionRecovery();
            return;
        }
        long localEpoch = Stores.Launch.CurrentLocalProductionDecoderEpoch(pid);
        long initialMap = Stores.Launch.CurrentMap(pid) ?? 0;
        var world = Stores.World.Get(pid);
        if (localEpoch <= 0 || initialMap <= 0 || !world.SelfReadyFor(initialMap) ||
            !world.CellsReadyFor(initialMap) || Stores.Launch.IsInFight(pid))
        {
            Refuse("Fabrication refusée : carte, cellule serveur ou décodage de session non confirmés.");
            RefreshProductionRecovery();
            return;
        }
        bool CurrentSession() => ProductionCharacter?.Invoke() == pid &&
            Stores.Launch.ServerIdOf(pid) == server &&
            Stores.Launch.CharacterId(pid) == characterId &&
            string.Equals(Stores.Launch.SelfCharacterInfo(pid)?.Name, name, StringComparison.Ordinal) &&
            !Stores.Launch.IsInFight(pid) &&
            Stores.Launch.CurrentLocalProductionDecoderEpoch(pid) == localEpoch &&
            Stores.Launch.CurrentMap(pid) == initialMap &&
            Stores.World.Get(pid).SelfReadyFor(initialMap) &&
            Stores.World.Get(pid).CellsReadyFor(initialMap);
        // Read-only preflight BEFORE the irreversible durable Started entry.
        // The exact same validation is repeated by RunAsync after admission;
        // preflight is not a receipt or a bypass of the persisted lease.
        var boundActions = new ItemCraftSessionBoundActions(actions, CurrentSession);
        string? preflightFailure;
        try { preflightFailure = _production.Preflight(request, boundActions); }
        catch (Exception ex)
        {
            Refuse("Pré-vérification de production indisponible : " + ex.GetType().Name +
                ". Aucune action envoyée ; journal intact.");
            return;
        }
        if (!CurrentSession() || boundActions.IsRevoked)
        {
            Refuse("Pré-vérification refusée : identité ou observation de session perdue. Aucune action envoyée ; journal intact.");
            return;
        }
        if (preflightFailure is not null)
        {
            Refuse("Pré-vérification refusée : " + preflightFailure +
                ". Corriger la recette, l'atelier ou le stock avant Lancer ; aucune tentative persistée.");
            return;
        }
        ItemCraftAttemptDecision admission = _productionAttempts.TryBegin(scope, pid, request,
            out ItemCraftAttemptLease lease);
        if (admission != ItemCraftAttemptDecision.Allowed)
        {
            var blocked = _productionAttempts.ReadStatus(scope);
            Refuse($"Fabrication bloquée ({admission}) : {blocked.Reason}. Pas de relance automatique après crash.");
            RefreshProductionRecovery();
            return;
        }
        if (guidedGoal is not null &&
            (guidedFingerprint is null ||
             !AdmitGuidedProductionActualRun(guidedGoal, guidedFingerprint, lease)))
        {
            // V9 Started may already have been fsynced. Even if no request
            // actually reached the transport, we cannot safely replay this
            // old lease after crash/race. Mark uncertain, do not run.
            _productionAttempts.TryFinish(lease, fullyConfirmed: false);
            Refuse("INCERTAIN · admission V12 impossible après réservation V9 ; " +
                "aucune action de jeu envoyée par ce passage, rapprochement requis.");
            RefreshProductionRecovery();
            return;
        }
        // Every real action crosses the same character/server/characterId
        // boundary. A sender result arriving after character replacement is
        // ignored; the persisted attempt remains unresolved for inspection.
        actions = boundActions;
        long uiRevision = Interlocked.Increment(ref _productionUiRevision);
        IsProductionRunning = true;
        RefreshProductionRecovery();
        try
        {
            var progress = new Progress<ItemCraftProductionProgress>(p =>
            {
                // Progress<T> posts through the UI SynchronizationContext.
                // A callback queued by the old production must never replace
                // the status of the next character or a completed newer run.
                if (Volatile.Read(ref _productionUiRevision) == uiRevision &&
                    CurrentSession() && !boundActions.IsRevoked)
                    ShowProgress(p);
            });
            var result = await _production.RunAsync(request, actions, progress, CancellationToken.None);
            // A result can arrive after the selected character/session was
            // replaced. Preserve the durable uncertain outcome, but never
            // publish the previous character's completion in the new UI.
            bool uiCurrent = Volatile.Read(ref _productionUiRevision) == uiRevision &&
                CurrentSession() && !boundActions.IsRevoked;
            if (uiCurrent)
            {
                ShowProgress(new ItemCraftProductionProgress("Terminé", result.Produced, result.Spent, result.Listed, result.Stop));
                Status = ProductionProgress;
            }
            bool fullyConfirmed = CanCloseProductionAsCompleted(result, request,
                CurrentSession(), boundActions.IsRevoked);
            if (!_productionAttempts.TryFinish(lease, fullyConfirmed))
            {
                if (uiCurrent)
                {
                    ProductionStop = "Journal impossible à finaliser : rapprochement des opérations nécessaire.";
                    Status = ProductionStop;
                }
            }
            if (guidedGoal is not null)
                FinishGuidedProductionActualRun(guidedGoal, scope);
            else if (!fullyConfirmed)
            {
                if (uiCurrent)
                {
                    ProductionStop = "BLOQUÉ · " + result.Stop +
                        ". Production partielle ou indéterminée : rapprocher les reçus et le stock avant toute nouvelle exécution.";
                    Status = ProductionStop;
                }
            }
        }
        catch (Exception ex)
        {
            // A thrown sender may already have transmitted a request. Never
            // turn the attempt into a clean, retryable failure.
            _productionAttempts.TryFinish(lease, fullyConfirmed: false);
            if (guidedGoal is not null)
                FinishGuidedProductionActualRun(guidedGoal, scope);
            if (Volatile.Read(ref _productionUiRevision) == uiRevision &&
                CurrentSession() && !boundActions.IsRevoked)
            {
                ProductionProgress = "Fabrication interrompue : " + ex.GetType().Name;
                ProductionStop = ProductionProgress;
                Status = ProductionProgress;
            }
        }
        finally
        {
            Interlocked.Increment(ref _productionUiRevision);
            IsProductionRunning = false;
            RefreshProductionRecovery();
        }
    }

    /// <summary>Only the fully observed runner outcome may close Started to Completed.</summary>
    public static bool CanCloseProductionAsCompleted(
        ItemCraftProductionResult result, ItemCraftProductionRequest request,
        bool sessionCurrent, bool adapterRevoked) =>
        sessionCurrent && !adapterRevoked &&
        request is { Recipe: { ResultGid: > 0 }, Quantity: > 0, MaxKamas: >= 0 } &&
        result is not null && result.Produced >= request.Quantity &&
        result.Stop == ItemCraftProductionStop.QuantityReached &&
        result.Spent >= 0 && result.Spent <= request.MaxKamas &&
        (request.SaleFloor is null ? result.Listed == 0 :
            result.Listed == result.Produced);

    private bool BeginProduction(out ItemCraftProductionRequest request,
        out IItemCraftProductionActions actions, bool previewOnly = false)
    {
        request = null!;
        actions = null!;
        if (DemoMode) return Refuse("La démonstration fictive ne fabrique rien.");
        if (Targets.Count != 1)
            return Refuse(Targets.Count == 0
                ? "Ajoute un objet avant de lancer."
                : "La fabrication lance un seul objet. Retire les autres pièces.");
        var target = Targets[0];
        if (!long.TryParse(target.ItemId, NumberStyles.None, CultureInfo.InvariantCulture, out long gid) || gid <= 0
            || target.Quantity is < 1 or > MaxQuantity)
            return Refuse("Objet ou quantité invalide.");
        if (!TryKamas(BudgetText, true, out long? budget, out string? budgetError) || budget is not long maxKamas)
            return Refuse(budgetError ?? "Indique le budget maximum en kamas.");
        if (!TryKamas(CraftCostText, true, out long? typedCost, out string? costError) || typedCost is not long fee)
            return Refuse(costError ?? "Indique le coût d'atelier par fabrication, ou 0.");
        if (!TryFloor(SaleFloorText, out long? floor, out string? floorError))
            return Refuse(floorError ?? "Plancher de vente invalide.");
        if (ProductionCharacter?.Invoke() is not uint pid || pid == 0)
            return Refuse("Aucun personnage branché. Sélectionne un personnage dans les bots.");
        RememberGraphMethods();
        ItemCraftProductionRecipe? recipe;
        try
        {
            recipe = ProductionRecipe is not null
                ? ProductionRecipe(gid)
                : WorkshopLocator.ReadRecipe(Stores.Launch.GameData, gid);
        }
        catch (Exception ex) { return Refuse("Recette illisible : " + ex.GetType().Name); }
        if (recipe is null) return Refuse("Recette locale introuvable, ambiguë ou incomplète.");
        var inputs = recipe.Inputs.Select(input => input with
        {
            BuyAllowed = _methodChoices.TryGetValue(input.Gid.ToString(CultureInfo.InvariantCulture), out var method)
                && string.Equals(method, "Acheter", StringComparison.Ordinal)
        }).ToArray();
        recipe = recipe with { Inputs = inputs, KamasPerCraft = fee };
        if (previewOnly && ProductionPreviewActions is null)
            return Refuse("Pré-vérification refusée : port de stock sans émission indisponible.");
        try
        {
            actions = previewOnly
                ? ProductionPreviewActions!(pid)
                : ProductionActions?.Invoke(pid) ?? new ItemCraftProductionLaunch(Stores.Launch, pid);
        }
        catch (Exception ex) { return Refuse("Exécution indisponible : " + ex.GetType().Name); }
        if (actions is not IItemCraftProductionStockObservation { SupportsStockObservation: true })
            return Refuse("Fabrication refusée : lecture de stock associée à la session indisponible. Aucune mutation autorisée.");
        request = new ItemCraftProductionRequest(recipe, (int)target.Quantity, maxKamas, BankAllowed, floor);
        return true;
    }

    private bool Refuse(string message)
    {
        ProductionProgress = message;
        ProductionStop = message;
        Status = message;
        return false;
    }

    private void ShowProgress(ItemCraftProductionProgress progress)
    {
        ProductionProgress = progress.Text;
        ProductionProduced = progress.Produced;
        ProductionSpent = progress.Spent;
        ProductionListed = progress.Listed;
        if (progress.Phase == "Terminé") ProductionStop = progress.Detail;
    }

    private static bool TryKamas(string? text, bool required, out long? value, out string? error)
    {
        value = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            if (required) error = "Indique un nombre de kamas (0 est accepté).";
            return !required;
        }
        string cleaned = text.Trim().Replace(" ", "", StringComparison.Ordinal)
            .Replace("\u00A0", "", StringComparison.Ordinal).Replace("\u202F", "", StringComparison.Ordinal);
        if (!long.TryParse(cleaned, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed) || parsed < 0)
        {
            error = "Les kamas doivent être un entier positif ou nul.";
            return false;
        }
        value = parsed;
        return true;
    }

    private static bool TryFloor(string? text, out long? value, out string? error)
    {
        value = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!TryKamas(text, true, out value, out error) || value is not long floor || floor <= 0)
        {
            error = "Le plancher de vente doit être un entier supérieur à zéro, ou rester vide.";
            value = null;
            return false;
        }
        value = floor;
        return true;
    }
}

/// <summary>Adapter for one character. Craft, chest withdrawal and the buyer port succeed only when LaunchService
/// already observed a server receipt. This type is not in the planner folder because that folder is compiled
/// into the offline test slice without the HDV partial.</summary>
public sealed class ItemCraftProductionLaunch :
    IItemCraftProductionActions, IItemCraftProductionStockObservation
{
    private readonly LaunchService _launch;
    private readonly uint _pid;
    private readonly Action<HdvProductionListingAssessmentV14>? _listingObservation;
    private readonly bool _requireGuidedListingEvidence;

    public ItemCraftProductionLaunch(LaunchService launch, uint pid,
        Action<HdvProductionListingAssessmentV14>? listingObservation = null,
        bool requireGuidedListingEvidence = false)
    {
        _launch = launch ?? throw new ArgumentNullException(nameof(launch));
        if (pid == 0) throw new ArgumentOutOfRangeException(nameof(pid));
        _pid = pid;
        _listingObservation = listingObservation;
        _requireGuidedListingEvidence = requireGuidedListingEvidence;
    }

    public int BagCount(long gid) => _launch.Inventory.CountByGid(_pid, gid);
    public int ChestCount(long gid) => Math.Max(0, _launch.Bank.CountByGid(_pid, gid));
    public bool SupportsStockObservation => true;

    public bool TryReadBagCount(long gid, out int count)
    {
        count = 0;
        if (gid <= 0 || _launch.CurrentLocalProductionDecoderEpoch(_pid) <= 0)
            return false;
        int observed = _launch.Inventory.CountByGid(_pid, gid);
        if (observed < 0) return false;
        count = observed;
        return true;
    }

    public IReadOnlyList<MapInteractive> Interactives()
    {
        long map = _launch.CurrentMap(_pid) ?? 0;
        if (map <= 0 || _launch.InteractivesDecodedMap(_pid) != map) return Array.Empty<MapInteractive>();
        IReadOnlyList<MapInteractive> placed = Stores.World.Get(_pid).Interactives ?? Array.Empty<MapInteractive>();
        IReadOnlyList<MapInteractive> placeless = _launch.PlacelessInteractives(_pid, map) ?? Array.Empty<MapInteractive>();
        return placeless.Count == 0 ? placed : placed.Concat(placeless).ToArray();
    }

    public Task<bool> OpenChestAsync(CancellationToken ct) => _launch.BankOpenAsync(_pid, 0, true, ct);
    public Task<int> WithdrawAsync(long gid, int quantity, CancellationToken ct) => _launch.BankWithdrawAsync(_pid, gid, quantity, ct);
    public Task<bool> CloseChestAsync(CancellationToken ct) => _launch.BankCloseAsync(_pid, ct);

    public async Task<ItemCraftWorkshopReceipt> CraftAsync(int elementId, int skillInstanceUid,
        IReadOnlyList<(long Gid, int Qty)> ingredients, int count, CancellationToken ct)
    {
        var outcome = await _launch.CraftAsync(_pid, elementId, skillInstanceUid, ingredients, count, ct).ConfigureAwait(false);
        bool confirmed = outcome.Ok && outcome.Qty > 0;
        return new ItemCraftWorkshopReceipt(confirmed, confirmed ? outcome.Qty : 0, confirmed ? outcome.Gid : 0);
    }

    public Task<long> LotPriceAsync(long gid, int qty, CancellationToken ct) => _launch.HdvGetLotPriceAsync(_pid, gid, qty, ct);
    public Task<bool> BuyAsync(long gid, int qty, long maxPrice, CancellationToken ct) => _launch.HdvBuyByGidAsync(_pid, gid, qty, maxPrice, ct);
    // Produced items enter the same durable tax/UID reconciliation boundary
    // as manual sales, repricing and script selling. No direct raw listing.
    public async Task<bool> ListAsync(long gid, int quantity, long price, CancellationToken ct)
    {
        // V14: the SAME awaited call still uses the V9/V13 tax/UID journal,
        // not a new listing sender. Snapshot the exact eligible bag UID just
        // before dispatch, and reconcile only after this original invocation
        // has returned. Neither the snapshot nor the returned bool is a TCP
        // ACK. A guided cycle with missing evidence remains uncertain.
        ct.ThrowIfCancellationRequested();
        HdvProductionListingCaptureV14? capture;
        try { capture = _launch.HdvCaptureProductionListingV14(_pid, gid, quantity, price); }
        catch (Exception) { capture = null; }
        // A guided listing is not allowed to SEND when the exact source,
        // bag UID, price or durable journal cannot even be inspected. Manual
        // legacy usage keeps its existing emission path and separate review.
        if (_requireGuidedListingEvidence && capture is null)
            return false;
        HdvProductionListingAssessmentV14 Observe(bool originalReturned)
        {
            HdvProductionListingAssessmentV14 result;
            try { result = _launch.HdvAssessProductionListingV14(capture, originalReturned); }
            catch (Exception e)
            {
                result = new(HdvProductionListingOutcomeV14.Denied,
                    null, null, "listing observation unavailable (" +
                    e.GetType().Name + "); no correlated receipt");
            }
            try { _listingObservation?.Invoke(result); }
            catch (Exception) { /* an advisory UI callback cannot alter gameplay */ }
            return result;
        }
        bool accepted;
        try
        {
            accepted = await _launch.HdvScriptSellByGidAsync(_pid, gid, quantity,
                price, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            Observe(originalReturned: false); // consumes any one-shot capture
            throw; // the V9 real production journal retains uncertainty
        }
        var assessment = Observe(accepted);
        return accepted && (!_requireGuidedListingEvidence ||
            assessment.HasLocalSaleCorrelation);
    }
}
