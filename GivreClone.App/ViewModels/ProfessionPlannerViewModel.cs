using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GivreClone.App.ViewModels;

/// <summary>Presentation boundary; data adapters can map the pure ProfessionPlanner engine into this shape.</summary>
public sealed record ProfessionPlannerGoal(
    string JobName, int StartLevel, int TargetLevel,
    IReadOnlyDictionary<string, long> Inventory, string? RecipeId, string Strategy,
    long? RequestedCrafts = null, long? CurrentXp = null, bool CurrentXpVerified = false,
    IReadOnlyDictionary<string, string>? ItemMethodChoices = null);

/// <summary>No recipe, quantities or experience are silently inferred by the UI.</summary>
public sealed record ProfessionCatalogRecipe(
    string Id, string JobName, string Name, string SourceLabel, bool IsVerified,
    string ProductionLabel = "Rendement inconnu", string ExperienceLabel = "XP inconnue",
    string PriceLabel = "Prix inconnu")
{
    public string EvidenceLabel => IsVerified ? "SOURCE VÉRIFIÉE" : "NON VÉRIFIÉ";
}

public sealed record ProfessionPlannerStep(
    string Id, string Title, string Description, int Depth, string Kind,
    string Required, string InInventory, string Missing, string Evidence,
    string Provenance, IReadOnlyList<string>? AllowedActions = null);

public sealed record ProfessionPlannerSnapshot(
    string Title, string Explanation, string Source, bool IsFictional,
    string Xp, string Cost, string Time, IReadOnlyList<ProfessionPlannerStep> Steps,
    string Warnings = "", string Coverage = "Partielle");

/// <summary>Only a verified atlas reader can provide this route; UI never infers map edges.</summary>
public sealed record ProfessionVerifiedAtlasRoute(
    IReadOnlyList<long> MapIds, string Source, string GameVersion,
    string Sha256, string EvidenceReference, bool IsVerified);

public sealed partial class ProfessionInventoryRow : ObservableObject
{
    public ProfessionInventoryRow(string name, long quantity)
    {
        Name = name;
        Quantity = quantity;
    }

    public string Name { get; }
    public long Quantity { get; }
    public string QuantityLabel => $"× {Quantity:N0}";
}

public sealed partial class ProfessionPlanStepRow : ObservableObject
{
    public ProfessionPlanStepRow(ProfessionPlannerStep step)
    {
        Id = step.Id;
        Title = step.Title;
        Description = step.Description;
        Depth = Math.Clamp(step.Depth, 0, 10);
        Kind = step.Kind;
        Required = step.Required;
        InInventory = step.InInventory;
        Missing = step.Missing;
        Evidence = step.Evidence;
        Provenance = step.Provenance;
        Indent = new string(' ', Depth * 3) + (Depth > 0 ? "↳ " : "◆ ");
        ActionOptions.Add("À déterminer");
        foreach (var option in (step.AllowedActions ?? Array.Empty<string>())
                     .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8))
            if (!ActionOptions.Contains(option)) ActionOptions.Add(option);
        SelectedAction = "À déterminer";
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public int Depth { get; }
    public Thickness DepthMargin => new(Depth * 17, 0, 0, 0);
    public string Indent { get; }
    public string Kind { get; }
    public string Required { get; }
    public string InInventory { get; }
    public string Missing { get; }
    public string Evidence { get; }
    public string Provenance { get; }
    public ObservableCollection<string> ActionOptions { get; } = new();
    [ObservableProperty] public partial string SelectedAction { get; set; } = "À déterminer";
}

/// <summary>Offline no-code profession assistant: explicit input, bound UI, provenance and no speculative values.</summary>
public partial class ProfessionPlannerViewModel : ViewModelBase
{
    private const int MaxInventoryRows = 80;
    private const int MaxCatalogRows = 200;
    private const int MaxPlanSteps = 120;
    private const long MaxInventoryQuantity = 1_000_000_000;
    private static readonly string[] KnownJobs =
    [
        "Bijoutier", "Mineur", "Bricoleur", "Alchimiste", "Bûcheron", "Paysan",
        "Pêcheur", "Chasseur", "Boucher", "Boulanger", "Poissonnier", "Cordonnier",
        "Tailleur", "Forgeur", "Sculpteur", "Façonneur"
    ];

    private readonly Dictionary<string, long> _stock = new(StringComparer.OrdinalIgnoreCase);
    private bool _isLoadingDemo;

    public Func<ProfessionPlannerGoal, ProfessionPlannerSnapshot?>? PlanProvider { get; set; }
    public Func<IReadOnlyList<ProfessionCatalogRecipe>>? CatalogProvider { get; set; }
    public Func<string, IReadOnlyList<ProfessionCatalogRecipe>>? FolderCatalogProvider { get; set; }
    public Func<ProfessionPlanStepRow, ProfessionVerifiedAtlasRoute?>? VerifiedAtlasRouteProvider { get; set; }

    public ObservableCollection<string> FilteredJobs { get; } = new();
    public ObservableCollection<ProfessionInventoryRow> InventoryRows { get; } = new();
    public ObservableCollection<ProfessionCatalogRecipe> Recipes { get; } = new();
    public ObservableCollection<ProfessionCatalogRecipe> VisibleRecipes { get; } = new();
    public ObservableCollection<ProfessionPlanStepRow> Steps { get; } = new();
    public ObservableCollection<ProfessionPlanStepRow> VisibleSteps { get; } = new();
    public IReadOnlyList<string> Strategies { get; } = ["Équilibré", "Économiser des kamas", "Réduire le temps"];
    public IReadOnlyList<string> PlanViewModes { get; } = ["Toutes les étapes", "Farm / Drop", "Acheter HDV", "Craft"];

    [ObservableProperty] public partial string JobSearch { get; set; } = "Bijoutier";
    [ObservableProperty] public partial string SelectedJob { get; set; } = "Bijoutier";
    [ObservableProperty] public partial string StartLevel { get; set; } = "20";
    [ObservableProperty] public partial string TargetLevel { get; set; } = "30";
    [ObservableProperty] public partial string RequestedCraftsText { get; set; } = "";
    [ObservableProperty] public partial string CurrentXpText { get; set; } = "";
    [ObservableProperty] public partial string CatalogFolder { get; set; } = "Aucun dossier sélectionné";
    [ObservableProperty] public partial string NewInventoryItem { get; set; } = "";
    [ObservableProperty] public partial string NewInventoryQuantity { get; set; } = "";
    [ObservableProperty] public partial ProfessionInventoryRow? SelectedInventoryRow { get; set; }
    [ObservableProperty] public partial ProfessionCatalogRecipe? SelectedRecipe { get; set; }
    [ObservableProperty] public partial ProfessionPlanStepRow? SelectedStep { get; set; }
    [ObservableProperty] public partial string Strategy { get; set; } = "Équilibré";
    [ObservableProperty] public partial string RecipeSearch { get; set; } = "";
    [ObservableProperty] public partial bool VerifiedRecipesOnly { get; set; }
    [ObservableProperty] public partial string PlanViewMode { get; set; } = "Toutes les étapes";
    [ObservableProperty] public partial string AtlasRouteStatus { get; private set; } =
        "Itinéraire UNKNOWN · aucune preuve Atlas reliée à la ressource.";
    [ObservableProperty] public partial string StatusMessage { get; private set; } =
        "Saisis ton objectif, charge un catalogue vérifié ou ouvre la démonstration fictive.";
    [ObservableProperty] public partial string HostProofV18Status { get; private set; } =
        "Preuves hôte V18 non inspectées : aucune exécution métier autorisée.";

    [RelayCommand]
    private void InspectProfessionHostProofsV18()
    {
        // This offline planner has no certified V9 sender, journal or host
        // authority. Never substitute its manually-entered inventory for one.
        var report = GivreClone.App.Services.ProfessionMcp.ProfessionProductionWorkflow
            .InspectHostProofGaps("CRAFT", null, null);
        HostProofV18Status = "V18 · " + report.Diagnostic + " · mutation interdite. " +
            string.Join(" ; ", report.MissingOrUnverifiableProofs.Select(gap =>
                gap.Code + " : " + gap.ExactHostEvidenceRequired));
    }
    [ObservableProperty] public partial string SourceMessage { get; private set; } =
        "Aucune donnée de recette vérifiée chargée";
    [ObservableProperty] public partial string PlanHeadline { get; private set; } = "Plan en attente de données";
    [ObservableProperty] public partial string PlanExplanation { get; private set; } =
        "Connecte un catalogue local avec provenance pour calculer les quantités et afficher l'arbre des sous-crafts.";
    [ObservableProperty] public partial string XpLabel { get; private set; } = "Inconnue";
    [ObservableProperty] public partial string CostLabel { get; private set; } = "Inconnu";
    [ObservableProperty] public partial string TimeLabel { get; private set; } = "Inconnu";
    [ObservableProperty] public partial string CoverageLabel { get; private set; } = "NON VÉRIFIÉ";
    [ObservableProperty] public partial string WarningLabel { get; private set; } =
        "Quantités, gains XP, prix et taux de drop absents tant que leurs sources ne sont pas contrôlées.";
    [ObservableProperty] public partial string SimulationResult { get; private set; } =
        "Simulation prête dès qu'un plan documenté est chargé.";
    [ObservableProperty] public partial bool DemoMode { get; private set; }
    [ObservableProperty] public partial bool HasPlan { get; private set; }

    public bool HasRecipes => Recipes.Count > 0;
    public bool HasInventory => InventoryRows.Count > 0;
    public bool HasSelectedStep => SelectedStep is not null;
    public string DemoLabel => DemoMode ? "DÉMO · FICTIF" : "SOURCES · À VÉRIFIER";
    public string RecipeCountLabel => $"{Recipes.Count} recette(s) disponibles";
    public string StepCountLabel => $"{Steps.Count} étape(s) affichée(s)";
    public string InventoryCountLabel => $"{InventoryRows.Count} référence(s) saisie(s)";
    public string VisibleRecipeCountLabel => $"{VisibleRecipes.Count} recette(s) visibles";
    public string VisibleStepCountLabel => $"{VisibleSteps.Count} étape(s) visibles";
    // Proportion visuelle du NIVEAU MANUEL vers l'objectif, jamais un taux d'XP vérifié.
    public double LevelProgressValue => int.TryParse(StartLevel, out int start)
        && int.TryParse(TargetLevel, out int target) && target > 0
        ? Math.Clamp(100d * start / target, 0d, 100d) : 0d;
    public string LevelProgressCaption => $"Niveau déclaré {StartLevel} / objectif {TargetLevel} · XP restante UNKNOWN";
    public string GoalLabel => $"{SelectedJob}   ·   {StartLevel} → {TargetLevel}"
        + (string.IsNullOrWhiteSpace(RequestedCraftsText) ? "" : $"   ·   {RequestedCraftsText} crafts saisis");
    public string DataDisclaimer => DemoMode
        ? "SCÉNARIO ENTIÈREMENT FICTIF · toutes les quantités, dépendances et sources affichées dans cette démonstration sont inventées."
        : "Consultation locale · aucune action en jeu · aucune recette ou transition inventée.";

    public ProfessionPlannerViewModel() => RefreshFilteredJobs();

    partial void OnJobSearchChanged(string value) => RefreshFilteredJobs();

    partial void OnSelectedJobChanged(string value)
    {
        ExitDemoWhenGoalChanges();
        OnPropertyChanged(nameof(GoalLabel));
        if (!_isLoadingDemo)
        {
            Recipes.Clear();
            RefreshVisibleRecipes();
            SelectedRecipe = null;
            SourceMessage = "Métier modifié : recharge les recettes correspondantes.";
            OnPropertyChanged(nameof(HasRecipes));
            OnPropertyChanged(nameof(RecipeCountLabel));
        }
        InvalidatePlan();
    }

    partial void OnStartLevelChanged(string value)
    {
        ExitDemoWhenGoalChanges();
        OnPropertyChanged(nameof(GoalLabel));
        OnPropertyChanged(nameof(LevelProgressValue));
        OnPropertyChanged(nameof(LevelProgressCaption));
        InvalidatePlan();
    }

    partial void OnTargetLevelChanged(string value)
    {
        ExitDemoWhenGoalChanges();
        OnPropertyChanged(nameof(GoalLabel));
        OnPropertyChanged(nameof(LevelProgressValue));
        OnPropertyChanged(nameof(LevelProgressCaption));
        InvalidatePlan();
    }

    partial void OnRequestedCraftsTextChanged(string value)
    {
        ExitDemoWhenGoalChanges();
        OnPropertyChanged(nameof(GoalLabel));
        InvalidatePlan();
    }

    partial void OnCurrentXpTextChanged(string value)
    {
        ExitDemoWhenGoalChanges();
        InvalidatePlan();
    }

    partial void OnStrategyChanged(string value)
    {
        ExitDemoWhenGoalChanges();
        InvalidatePlan();
    }

    partial void OnSelectedRecipeChanged(ProfessionCatalogRecipe? value)
    {
        ExitDemoWhenGoalChanges();
        InvalidatePlan();
    }
    partial void OnSelectedStepChanged(ProfessionPlanStepRow? value)
    {
        OnPropertyChanged(nameof(HasSelectedStep));
        AtlasRouteStatus = "Itinéraire UNKNOWN · aucune preuve de route fournie pour cette ressource.";
    }
    partial void OnRecipeSearchChanged(string value) => RefreshVisibleRecipes();
    partial void OnVerifiedRecipesOnlyChanged(bool value) => RefreshVisibleRecipes();
    partial void OnPlanViewModeChanged(string value) => RefreshVisibleSteps();

    partial void OnDemoModeChanged(bool value)
    {
        OnPropertyChanged(nameof(DemoLabel));
        OnPropertyChanged(nameof(DataDisclaimer));
    }

    private void ExitDemoWhenGoalChanges()
    {
        if (DemoMode && !_isLoadingDemo) LeaveDemo();
    }

    private void RefreshFilteredJobs()
    {
        FilteredJobs.Clear();
        foreach (var job in KnownJobs.Where(j => j.Contains(JobSearch ?? "", StringComparison.OrdinalIgnoreCase)).Take(16))
            FilteredJobs.Add(job);
        if (!string.IsNullOrWhiteSpace(JobSearch) && !KnownJobs.Contains(JobSearch.Trim(), StringComparer.OrdinalIgnoreCase))
            FilteredJobs.Insert(0, JobSearch.Trim()[..Math.Min(JobSearch.Trim().Length, 64)]);
    }

    private void RefreshVisibleRecipes()
    {
        var query = (RecipeSearch ?? "").Trim();
        VisibleRecipes.Clear();
        foreach (var recipe in Recipes.Where(r => (!VerifiedRecipesOnly || r.IsVerified)
            && (query.Length == 0
                || r.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || r.Id.Contains(query, StringComparison.OrdinalIgnoreCase))).Take(MaxCatalogRows))
            VisibleRecipes.Add(recipe);
        OnPropertyChanged(nameof(VisibleRecipeCountLabel));
    }

    private void RefreshVisibleSteps()
    {
        VisibleSteps.Clear();
        foreach (var step in Steps.Where(s => PlanViewMode switch
        {
            "Farm / Drop" => s.Kind.Contains("DROP", StringComparison.OrdinalIgnoreCase)
                || s.Kind.Contains("RÉCOLTE", StringComparison.OrdinalIgnoreCase)
                || s.Kind.Contains("GATHER", StringComparison.OrdinalIgnoreCase)
                || s.ActionOptions.Any(a => a.Contains("farm", StringComparison.OrdinalIgnoreCase)
                    || a.Contains("drop", StringComparison.OrdinalIgnoreCase)),
            "Acheter HDV" => s.Kind.Contains("BUY", StringComparison.OrdinalIgnoreCase)
                || s.ActionOptions.Any(a => a.Contains("achet", StringComparison.OrdinalIgnoreCase)),
            "Craft" => s.Kind.Contains("CRAFT", StringComparison.OrdinalIgnoreCase)
                || s.ActionOptions.Any(a => a.Contains("craft", StringComparison.OrdinalIgnoreCase)),
            _ => true
        }))
            VisibleSteps.Add(step);
        OnPropertyChanged(nameof(VisibleStepCountLabel));
        if (SelectedStep is null || !VisibleSteps.Contains(SelectedStep))
            SelectedStep = VisibleSteps.FirstOrDefault();
    }

    [RelayCommand]
    private void ChooseJob(string? job)
    {
        if (string.IsNullOrWhiteSpace(job)) return;
        SelectedJob = job.Trim();
        JobSearch = SelectedJob;
        StatusMessage = $"Métier sélectionné : {SelectedJob}. Actualise le plan pour l'objectif choisi.";
    }

    [RelayCommand]
    private void AddInventory()
    {
        var name = (NewInventoryItem ?? "").Trim();
        if (name.Length is 0 or > 96 || name.Any(char.IsControl))
        {
            StatusMessage = "Indique un nom d'ingrédient valide (1 à 96 caractères).";
            return;
        }
        if (!long.TryParse(NewInventoryQuantity?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long quantity)
            || quantity < 0 || quantity > MaxInventoryQuantity)
        {
            StatusMessage = "Quantité de stock attendue : entier entre 0 et 1 000 000 000.";
            return;
        }
        if (_stock.Count >= MaxInventoryRows && !_stock.ContainsKey(name))
        {
            StatusMessage = $"Stock limité à {MaxInventoryRows} références.";
            return;
        }
        _stock[name] = quantity; // replace rather than accumulating the same stack twice
        RefreshInventory();
        NewInventoryItem = "";
        NewInventoryQuantity = "";
        StatusMessage = $"Stock manuel mis à jour : {name} × {quantity:N0}.";
        if (DemoMode) BuildDemoSnapshot(); else InvalidatePlan();
    }

    [RelayCommand]
    private void RemoveInventory(ProfessionInventoryRow? row)
    {
        if (row is null || !_stock.Remove(row.Name)) return;
        RefreshInventory();
        StatusMessage = $"{row.Name} retiré du stock saisi.";
        if (DemoMode) BuildDemoSnapshot(); else InvalidatePlan();
    }

    private void RefreshInventory()
    {
        InventoryRows.Clear();
        foreach (var item in _stock.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            InventoryRows.Add(new ProfessionInventoryRow(item.Key, item.Value));
        OnPropertyChanged(nameof(InventoryCountLabel));
        OnPropertyChanged(nameof(HasInventory));
    }

    [RelayCommand]
    private void RefreshPlan()
    {
        var selectedMethods = Steps.Where(s => s.SelectedAction != "À déterminer")
            .GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().SelectedAction, StringComparer.OrdinalIgnoreCase);
        if (DemoMode)
        {
            BuildDemoSnapshot();
            foreach (var step in Steps)
                if (selectedMethods.TryGetValue(step.Id, out string? chosen) && step.ActionOptions.Contains(chosen))
                    step.SelectedAction = chosen;
            StatusMessage = "Démo fictive recalculée avec le stock manuel.";
            return;
        }
        if (FolderCatalogProvider is not null && CatalogFolder != "Aucun dossier sélectionné")
        {
            try { LoadCatalog(FolderCatalogProvider(CatalogFolder) ?? Array.Empty<ProfessionCatalogRecipe>()); }
            catch (Exception ex) { StatusMessage = $"Dossier catalogue indisponible : {ex.Message}"; return; }
        }
        else if (CatalogProvider is not null)
        {
            try { LoadCatalog(CatalogProvider() ?? Array.Empty<ProfessionCatalogRecipe>()); }
            catch (Exception ex) { StatusMessage = $"Catalogue indisponible : {ex.Message}"; return; }
        }
        if (!TryGetGoal(out var goal, selectedMethods)) return;
        if (PlanProvider is null)
        {
            InvalidatePlan();
            StatusMessage = "Le moteur de calcul n'est pas encore relié. Sélectionne un catalogue local vérifié.";
            return;
        }
        try
        {
            LoadPlan(PlanProvider(goal));
            foreach (var step in Steps)
                if (selectedMethods.TryGetValue(step.Id, out string? chosen) && step.ActionOptions.Contains(chosen))
                    step.SelectedAction = chosen;
            StatusMessage = HasPlan ? "Plan consultatif actualisé à partir du fournisseur local." : "Aucun plan vérifiable pour cet objectif.";
        }
        catch (Exception ex)
        {
            InvalidatePlan();
            StatusMessage = $"Calcul interrompu : {ex.Message}";
        }
    }

    private bool TryGetGoal(out ProfessionPlannerGoal goal, IReadOnlyDictionary<string, string>? selectedMethods = null)
    {
        goal = default!;
        if (string.IsNullOrWhiteSpace(SelectedJob) || SelectedJob.Length > 64
            || !int.TryParse(StartLevel, NumberStyles.None, CultureInfo.InvariantCulture, out int start)
            || !int.TryParse(TargetLevel, NumberStyles.None, CultureInfo.InvariantCulture, out int target)
            || start is < 1 or > 200 || target is < 1 or > 200 || target <= start)
        {
            StatusMessage = "Choisis un métier et des niveaux valides : 1 à 200, cible supérieure au départ.";
            return false;
        }
        long? crafts = null;
        if (!string.IsNullOrWhiteSpace(RequestedCraftsText))
        {
            if (!long.TryParse(RequestedCraftsText, NumberStyles.None, CultureInfo.InvariantCulture, out long parsedCrafts)
                || parsedCrafts is < 1 or > 1_000_000)
            {
                StatusMessage = "Nombre de crafts attendu : entier de 1 à 1 000 000.";
                return false;
            }
            crafts = parsedCrafts;
        }
        long? xp = null;
        if (!string.IsNullOrWhiteSpace(CurrentXpText))
        {
            if (!long.TryParse(CurrentXpText, NumberStyles.None, CultureInfo.InvariantCulture, out long parsedXp)
                || parsedXp is < 0 or > 1_000_000_000_000)
            {
                StatusMessage = "XP actuelle attendue : entier positif valide.";
                return false;
            }
            xp = parsedXp;
        }
        goal = new ProfessionPlannerGoal(SelectedJob.Trim(), start, target,
            new Dictionary<string, long>(_stock, StringComparer.OrdinalIgnoreCase), SelectedRecipe?.Id, Strategy,
            crafts, xp, CurrentXpVerified: false, ItemMethodChoices: selectedMethods);
        return true;
    }

    public void SetCatalogFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        CatalogFolder = folder;
        SourceMessage = $"Dossier local choisi : {folder} · actualise pour importer le catalogue";
        StatusMessage = "Dossier sélectionné. L'import nécessite un fournisseur de données local autorisé.";
        if (!DemoMode) InvalidatePlan();
    }

    public void NotifyPickerUnavailable(string message) => StatusMessage = message;

    public void LoadCatalog(IEnumerable<ProfessionCatalogRecipe> recipes)
    {
        ArgumentNullException.ThrowIfNull(recipes);
        DemoMode = false;
        var chosen = SelectedRecipe?.Id;
        Recipes.Clear();
        foreach (var item in recipes.Where(x => x is not null &&
                     !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.Name)
                     && string.Equals(x.JobName, SelectedJob, StringComparison.OrdinalIgnoreCase))
                     .Take(MaxCatalogRows))
            Recipes.Add(item);
        RefreshVisibleRecipes();
        SelectedRecipe = Recipes.FirstOrDefault(x => x.Id == chosen) ?? Recipes.FirstOrDefault();
        SourceMessage = Recipes.Count > 0
            ? $"Catalogue importé · {Recipes.Count} recettes · consulter la provenance par recette"
            : "Aucune recette vérifiée importée";
        OnPropertyChanged(nameof(HasRecipes));
        OnPropertyChanged(nameof(RecipeCountLabel));
        InvalidatePlan();
    }

    public void LoadPlan(ProfessionPlannerSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            InvalidatePlan();
            return;
        }
        Steps.Clear();
        foreach (var step in (snapshot.Steps ?? Array.Empty<ProfessionPlannerStep>())
                     .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Title))
                     .Take(MaxPlanSteps))
            Steps.Add(new ProfessionPlanStepRow(step));
        RefreshVisibleSteps();
        DemoMode = snapshot.IsFictional;
        PlanHeadline = snapshot.Title;
        PlanExplanation = snapshot.Explanation;
        SourceMessage = snapshot.Source;
        XpLabel = string.IsNullOrWhiteSpace(snapshot.Xp) ? "Inconnue" : snapshot.Xp;
        CostLabel = string.IsNullOrWhiteSpace(snapshot.Cost) ? "Inconnu" : snapshot.Cost;
        TimeLabel = string.IsNullOrWhiteSpace(snapshot.Time) ? "Inconnu" : snapshot.Time;
        CoverageLabel = snapshot.IsFictional ? "FICTIF · EXEMPLE" : snapshot.Coverage;
        WarningLabel = string.IsNullOrWhiteSpace(snapshot.Warnings)
            ? "Les méthodes non documentées restent à vérifier avant toute décision."
            : snapshot.Warnings;
        HasPlan = Steps.Count > 0;
        SelectedStep = VisibleSteps.FirstOrDefault();
        OnPropertyChanged(nameof(StepCountLabel));
        SimulationResult = "Lance une simulation locale pour examiner les choix de collecte, craft et achat.";
    }

    private void InvalidatePlan()
    {
        if (_isLoadingDemo) return;
        Steps.Clear();
        RefreshVisibleSteps();
        HasPlan = false;
        SelectedStep = null;
        PlanHeadline = "Plan à recalculer";
        PlanExplanation = "Actualise après avoir choisi un métier, une recette et ton stock disponible.";
        XpLabel = "Inconnue";
        CostLabel = "Inconnu";
        TimeLabel = "Inconnu";
        CoverageLabel = "NON VÉRIFIÉ";
        SimulationResult = "Aucune simulation tant qu'un plan n'est pas chargé.";
        OnPropertyChanged(nameof(StepCountLabel));
    }

    [RelayCommand]
    private void Simulate()
    {
        if (!HasPlan)
        {
            SimulationResult = "Impossible de simuler : aucun graphe de recettes documenté.";
            return;
        }
        int undecided = Steps.Count(s => s.SelectedAction == "À déterminer");
        SimulationResult = DemoMode
            ? $"DÉMONSTRATION FICTIVE · {Steps.Count} nœuds consultés · {undecided} choix non renseignés. Aucun XP, coût ou déplacement réel simulé."
            : $"Projection hors jeu : {Steps.Count} étapes visibles, {undecided} méthodes indéterminées. Vérifie les sources, quantités et restrictions avant utilisation.";
    }

    [RelayCommand]
    private void InspectVerifiedAtlasRoute()
    {
        if (SelectedStep is null || VerifiedAtlasRouteProvider is null)
        {
            AtlasRouteStatus = "Itinéraire UNKNOWN · aucune source Atlas vérifiée raccordée.";
            return;
        }
        try
        {
            var route = VerifiedAtlasRouteProvider(SelectedStep);
            if (route is null || !route.IsVerified || route.MapIds is null
                || route.MapIds.Count is < 2 or > 120 || route.MapIds.Any(id => id <= 0)
                || string.IsNullOrWhiteSpace(route.Source) || route.Source.Length > 180
                || string.IsNullOrWhiteSpace(route.GameVersion) || route.GameVersion.Length > 64
                || route.Sha256 is not { Length: 64 } || !route.Sha256.All(Uri.IsHexDigit)
                || string.IsNullOrWhiteSpace(route.EvidenceReference) || route.EvidenceReference.Length > 260)
            {
                AtlasRouteStatus = "Itinéraire UNKNOWN · transitions ou provenance insuffisantes.";
                return;
            }
            AtlasRouteStatus = $"ATLAS VÉRIFIÉ · {route.MapIds.Count - 1} transitions · "
                + $"{string.Join(" → ", route.MapIds)} · {route.Source} ({route.GameVersion})"
                + $" · preuve {route.EvidenceReference}. Consultation uniquement.";
        }
        catch (Exception)
        {
            AtlasRouteStatus = "Itinéraire UNKNOWN · lecture de la preuve Atlas impossible.";
        }
    }

    [RelayCommand]
    private void ShowDemo()
    {
        _isLoadingDemo = true;
        try
        {
            SelectedJob = "Bijoutier";
            JobSearch = "Bijoutier";
            StartLevel = "20";
            TargetLevel = "30";
            RequestedCraftsText = "";
            CurrentXpText = "";
            RecipeSearch = "";
            VerifiedRecipesOnly = false; // la fiche de démonstration est FICTIVE, et doit rester visible
            PlanViewMode = "Toutes les étapes";
            Strategy = "Équilibré";
            Recipes.Clear();
            Recipes.Add(new ProfessionCatalogRecipe("demo-bijou", "Bijoutier", "Anneau pédagogique (FICTIF)",
                "DÉMO FICTIVE · recette inventée", false, "1 objet (fictif)", "XP inconnue", "Prix inconnu"));
            RefreshVisibleRecipes();
            SelectedRecipe = Recipes[0];
            OnPropertyChanged(nameof(HasRecipes));
            OnPropertyChanged(nameof(RecipeCountLabel));
        }
        finally { _isLoadingDemo = false; }
        DemoMode = true;
        BuildDemoSnapshot();
        StatusMessage = "Démonstration activée. Les objets, besoins et méthodes affichés sont fictifs.";
    }

    private void BuildDemoSnapshot()
    {
        static string Label(long count) => count.ToString("N0", CultureInfo.GetCultureInfo("fr-FR"));
        long plumes = Math.Min(20, StockOf("Plume de Piou"));
        long huile = Math.Min(20, StockOf("Huile de sésame"));
        long seedsNeeded = (20 - huile) * 3; // FICTIONAL 3:1 processing ratio
        long seeds = Math.Min(seedsNeeded, StockOf("Graines de sésame"));
        var steps = new ProfessionPlannerStep[]
        {
            new("demo-root", "Anneau pédagogique", "Objet racine · exemple de recette fictive pour l'interface", 0,
                "CRAFT", "1 (fictif)", "0", "1", "FICTIF", "Exemple pédagogique inventé", ["Sous-craft", "Acheter HDV"]),
            new("demo-pi", "Plume de Piou", "Ressource enfant · scénario de combat/drop ou achat", 1,
                "DROP", "20 (fictif)", Label(plumes), Label(20 - plumes), "FICTIF", "20 = hypothèse de démonstration",
                ["Drop / combat", "Acheter HDV"]),
            new("demo-oil", "Huile de sésame", "Sous-craft enfant · dépendance de graines inventée", 1,
                "SOUS-CRAFT", "20 (fictif)", Label(huile), Label(20 - huile), "FICTIF", "Quantité et transformation inventées",
                ["Sous-craft", "Acheter HDV"]),
            new("demo-seeds", "Graines de sésame", "Enfant de l'huile · ratio 3:1 fictif, récolte ou achat", 2,
                "RÉCOLTE", $"{Label(seedsNeeded)} (fictif)", Label(seeds), Label(seedsNeeded - seeds), "FICTIF",
                "3 graines par huile = hypothèse", ["Farm / récolte", "Acheter HDV"])
        };
        LoadPlan(new ProfessionPlannerSnapshot(
            "Bijoutier 20 → 30 · arbre pédagogique",
            "1 recette fictive avec une ressource à drop et un sous-craft à récolter. Les stocks manuels changent les manquants de cet exemple.",
            "DÉMO FICTIVE · aucune recette officielle ni rendement vérifié", true,
            "Inconnue", "Inconnu", "Inconnu", steps,
            "Toutes les quantités, dépendances et méthodes proposées sont inventées pour illustrer le parcours.",
            "FICTIF"));
    }

    private long StockOf(string name) => _stock.TryGetValue(name, out var quantity) ? quantity : 0;

    [RelayCommand]
    private void LeaveDemo()
    {
        DemoMode = false;
        Recipes.Clear();
        RefreshVisibleRecipes();
        SelectedRecipe = null;
        OnPropertyChanged(nameof(HasRecipes));
        OnPropertyChanged(nameof(RecipeCountLabel));
        SourceMessage = "Aucune donnée de recette vérifiée chargée";
        InvalidatePlan();
        StatusMessage = "Démonstration fermée. Tu peux charger un catalogue de recettes vérifié.";
    }
}
