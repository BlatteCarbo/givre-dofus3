using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using GivreClone.App.Services.ProfessionData;
using GivreClone.App.Services.ProfessionPlanner;
using GivreClone.App.Services.ProfessionMcp;
using GivreClone.App.ViewModels;
using DataRecipe = GivreClone.App.Services.ProfessionData.ProfessionRecipe;
using EngineRecipe = GivreClone.App.Services.ProfessionPlanner.ProfessionRecipe;

namespace GivreClone.App.Services;

/// <summary>
/// Bounded local, read-only bridge between a user-selected, pinned profession
/// catalogue, the pure planner and Avalonia. No game requests or third-party API.
/// The user-supplied file hash proves byte integrity, not publisher authenticity,
/// server acceptance, prices, recipes of an uninstalled patch or current XP.
/// </summary>
public static class ProfessionAccessService
{
    private static readonly object Sync = new();
    private static string? _folder;
    private static ProfessionCatalog? _catalog;
    private const int MaxPreviewSteps = 120;

    public static void SelectCatalogDirectory(string? directory)
    {
        lock (Sync)
        {
            _folder = string.IsNullOrWhiteSpace(directory) ? null : directory;
            _catalog = null;
        }
    }

    public static ProfessionCatalog? GetCatalog()
    {
        lock (Sync)
        {
            var folder = _folder ?? Environment.GetEnvironmentVariable("GIVRE_PROFESSION_DATA_DIR");
            if (string.IsNullOrWhiteSpace(folder)) return null;
            if (_catalog is null)
                _catalog = ProfessionCatalogReader.LoadDirectory(folder,
                    Environment.GetEnvironmentVariable("GIVRE_PROFESSION_EXPECTED_VERSION"));
            return _catalog;
        }
    }

    public static IReadOnlyList<ProfessionCatalogRecipe> GetRecipeCards(string? jobName)
    {
        var catalog = GetCatalog();
        if (catalog is not { Admitted: true }) return Array.Empty<ProfessionCatalogRecipe>();
        var jobs = catalog.Jobs.Where(j => j.Name is not null &&
            string.Equals(j.Name, jobName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (jobs.Length != 1) return Array.Empty<ProfessionCatalogRecipe>();
        var names = catalog.Items.ToDictionary(i => i.Id, i => i.Name);
        return catalog.RecipesForJob(jobs[0].Id).Take(200).Select(r =>
            new ProfessionCatalogRecipe(r.Id, jobs[0].Name ?? "Métier",
                names.TryGetValue(r.ResultItemId, out var itemName) && !string.IsNullOrWhiteSpace(itemName)
                    ? itemName : $"Objet GID {r.ResultItemId}",
                $"Extraction locale · {catalog.Source.GameVersion} · SHA {ShortHash(catalog.Source.FileSha256)}",
                // Recipe provenance is a pinned local dataset claim, not a
                // gameplay-tested or publisher-signed attestation.
                IsVerified: false,
                ProductionLabel: r.ResultQuantity is { } qty ? $"×{qty}/craft · source locale" : "Rendement inconnu",
                ExperienceLabel: "XP non certifiée",
                PriceLabel: "Prix HDV non relevé")).ToArray();
    }

    public static ProfessionPlannerSnapshot? BuildVisualPlan(ProfessionPlannerGoal ui)
    {
        var catalog = GetCatalog();
        if (catalog is not { Admitted: true }) return null;
        var job = catalog.Jobs.Where(j => j.Name is not null &&
            string.Equals(j.Name, ui.JobName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (job.Length != 1) return null;
        var model = CreatePlanRequest(catalog, ui, job[0].Id);
        ProfessionPlan plan;
        try { plan = ProfessionPlannerEngine.Plan(model); }
        catch (Exception e) when (e is ArgumentException or ArithmeticException or OverflowException)
        {
            return new ProfessionPlannerSnapshot($"Plan indisponible · {ui.JobName}",
                "Données, quantité ou nomenclature non valides pour un calcul borné.",
                catalog.Source.SourceLabel, false, "Inconnue", "Inconnu", "Inconnu", [],
                e.GetType().Name, "BLOQUÉ");
        }
        var lookup = catalog.Items.ToDictionary(i => i.Id, i => i.Name);
        var byId = plan.Steps.ToDictionary(s => s.Id);
        var referenced = plan.Steps.SelectMany(s => s.Dependencies).ToHashSet();
        var rendered = new List<ProfessionPlannerStep>();
        var visited = new HashSet<int>();
        void Walk(ProfessionPlanStep step, int depth)
        {
            if (rendered.Count >= MaxPreviewSteps || !visited.Add(step.Id)) return;
            string name = lookup.TryGetValue(step.ItemGid, out var label) && !string.IsNullOrWhiteSpace(label)
                ? label : (step.ItemGid > 0 ? $"Objet GID {step.ItemGid}" : "Recette inconnue");
            string method = step.Kind switch
            {
                PlanStepKind.UseInventory => "Stock",
                PlanStepKind.Craft => "Fabrication",
                PlanStepKind.Gather => "Récolte",
                PlanStepKind.Drop => "Drop",
                PlanStepKind.Buy => "HDV",
                _ => "À résoudre"
            };
            var options = step.Alternatives.Select(x => x.Method switch
                {
                    AcquisitionMethod.Craft => "Fabriquer",
                    AcquisitionMethod.Gather => "Récolter",
                    AcquisitionMethod.Drop => "Drop",
                    AcquisitionMethod.Buy => "Acheter",
                    _ => "À vérifier"
                }).Distinct(StringComparer.Ordinal).ToArray();
            rendered.Add(new ProfessionPlannerStep(
                // Acquisition preference belongs to the item GID, not the
                // presentation position in a post-order expansion.
                step.ItemGid.ToString(CultureInfo.InvariantCulture), name, step.Detail,
                depth, method, $"× {step.Quantity:N0}",
                step.Kind == PlanStepKind.UseInventory ? $"× {step.Quantity:N0}" : "—",
                step.Kind == PlanStepKind.Unresolved ? $"× {step.Quantity:N0} · inconnu" :
                    step.Kind == PlanStepKind.UseInventory ? "0" : $"× {step.Quantity:N0} à prévoir",
                step.Verified ? "Calcul local" : "UNVERIFIED",
                $"Catalogue {catalog.Source.GameVersion} · SHA {ShortHash(catalog.Source.FileSha256)}",
                options));
            foreach (var childId in step.Dependencies)
                if (byId.TryGetValue(childId, out var child)) Walk(child, depth + 1);
        }
        foreach (var root in plan.Steps.Where(s => !referenced.Contains(s.Id)).OrderByDescending(s => s.Id))
            Walk(root, 0);
        string itemName = catalog.Recipes.FirstOrDefault(r => r.Id == ui.RecipeId) is { } recipe
            && lookup.TryGetValue(recipe.ResultItemId, out var named) && !string.IsNullOrWhiteSpace(named)
                ? named : "Recette à choisir";
        var xp = plan.Progress.NeededXp is { } xpNeeded
            ? $"{xpNeeded:N0} XP à obtenir · source locale" : "XP cible inconnue";
        var kamas = plan.FullKamasEstimate is { } full
            ? $"{full:N0} kamas (hypothèse locale)" : "Coût total inconnu";
        var seconds = plan.FullSecondsEstimate is { } sec
            ? $"{sec:N0} s (hypothèse locale)" : "Durée inconnue";
        var warnings = new List<string>(plan.Warnings)
        {
            "Un SHA local ne certifie pas l'exactitude serveur des recettes ni des ingrédients.",
            "Les prix HDV, stocks du personnage, disponibilités de ressources et routes de farm ne sont pas observés ici."
        };
        if (ui.Inventory.Count > 0)
            warnings.Add("Les quantités du stock sont déclarées manuellement, non vérifiées dans le client.");
        if (rendered.Count < plan.Steps.Count)
            warnings.Add($"Seulement {MaxPreviewSteps} étapes affichées ; affiner le projet.");
        return new ProfessionPlannerSnapshot(
            $"{ui.JobName} · niv. {ui.StartLevel} → {ui.TargetLevel} · {itemName}",
            plan.Progress.PlannedCrafts is { } crafts
                ? $"{crafts:N0} fabrication(s) prévues, {plan.Steps.Count} étape(s) simulées. Aucune exécution."
                : $"{plan.Steps.Count} étape(s) simulées ; le nombre nécessaire pour atteindre le niveau cible reste inconnu.",
            $"Source locale · {catalog.Source.SourceLabel} · {catalog.Source.GameVersion} · {ShortHash(catalog.Source.FileSha256)}",
            IsFictional: false, xp, kamas, seconds, rendered,
            string.Join("  •  ", warnings.Take(12)),
            plan.IsComplete && plan.Progress.TargetReachableFromKnownXp
                ? "CALCUL SOURCE LOCALE" : "PLAN PARTIEL · XP/ACQUISITIONS À CONFIRMER");
    }

    internal static ProfessionPlanRequest CreatePlanRequest(
        ProfessionCatalog catalog, ProfessionPlannerGoal goal, int jobId)
    {
        // Only fully specified recipe quantities and levels can be fed to the
        // planner. A present resultLevel is NOT proof of required craft XP.
        var usable = catalog.Recipes
            .Where(r => r.RequiredLevel is >= 1 and <= 200 && r.ResultQuantity is > 0
                        && long.TryParse(r.Id, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                        && r.Ingredients.Count > 0)
            .Take(2000)
            .Select(r => new EngineRecipe(
                long.Parse(r.Id, CultureInfo.InvariantCulture), r.ResultItemId,
                r.ResultQuantity!.Value,
                r.Ingredients.Select(i => new RecipeIngredient(i.ItemId, i.Quantity)).ToArray(),
                r.RequiredLevel!.Value, r.JobId, r.ExperiencePerCraft,
                XpVerified: false, SourceVersion: catalog.Source.GameVersion,
                Verified: catalog.Source.LocalHashVerified, SecondsPerCraft: null,
                KamasPerCraft: null)).ToArray();
        long? selectedRecipe = long.TryParse(goal.RecipeId, NumberStyles.None,
            CultureInfo.InvariantCulture, out var id) ? id : null;
        var byName = catalog.Items.Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Name!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().Id, StringComparer.OrdinalIgnoreCase);
        var stock = new List<ProfessionInventoryStack>();
        int ordinal = 0;
        foreach (var kv in goal.Inventory.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            long itemId;
            if (!byName.TryGetValue(kv.Key, out itemId) &&
                !long.TryParse(kv.Key, NumberStyles.None, CultureInfo.InvariantCulture, out itemId))
                continue; // Missing/ambiguous name cannot be silently matched to a GID.
            if (itemId <= 0 || kv.Value < 0) continue;
            stock.Add(new ProfessionInventoryStack($"manual-{ordinal++}", itemId, kv.Value, "manual-unverified"));
        }
        var weights = goal.Strategy switch
        {
            "Économiser des kamas" => new ProfessionPreferences(1, 0),
            "Réduire le temps" => new ProfessionPreferences(0, 1),
            _ => new ProfessionPreferences(1, 1)
        };
        var chosenMethods = new Dictionary<long, AcquisitionMethod>();
        if (goal.ItemMethodChoices is not null)
            foreach (var selection in goal.ItemMethodChoices)
            {
                if (!long.TryParse(selection.Key, NumberStyles.None,
                        CultureInfo.InvariantCulture, out var itemId) || itemId <= 0)
                    continue;
                AcquisitionMethod? method = selection.Value switch
                {
                    "Fabriquer" => AcquisitionMethod.Craft,
                    "Récolter" => AcquisitionMethod.Gather,
                    "Drop" => AcquisitionMethod.Drop,
                    "Acheter" => AcquisitionMethod.Buy,
                    _ => null
                };
                if (method is not null) chosenMethods[itemId] = method.Value;
            }
        return new ProfessionPlanRequest(
            new ProfessionGoal(jobId, goal.JobName, goal.StartLevel, goal.TargetLevel,
                goal.CurrentXp, CurrentXpVerified: false),
            selectedRecipe, goal.RequestedCrafts, usable, stock,
            Array.Empty<MaterialSource>(), XpTable: null, Preferences: weights,
            MethodChoices: chosenMethods,
            KnownJobLevels: new Dictionary<int, int>()); // Secondary profession levels unknown.
    }

    public static ProfessionMcpSnapshot? ApprovedMcpSnapshot(ProfessionPlannerGoal goal)
    {
        // Explicit, independent AI disclosure authorization is required EVEN
        // for a locally hashed dataset; public DofusDB remains always excluded.
        if (!AppConfig.Current.McpEnabled) return null;
        var catalog = GetCatalog();
        if (catalog is not { Admitted: true } || catalog.Source.SourceLabel != "local-extraction")
            return null;
        string? pinned = Environment.GetEnvironmentVariable("GIVRE_PROFESSION_AI_APPROVED_SHA256");
        string? approval = Environment.GetEnvironmentVariable("GIVRE_PROFESSION_AI_AUTH_ID");
        if (!string.Equals(pinned, catalog.Source.FileSha256, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(approval)) return null;
        var job = catalog.Jobs.Where(j => string.Equals(j.Name, goal.JobName,
            StringComparison.OrdinalIgnoreCase)).ToArray();
        if (job.Length != 1) return null;
        try
        {
            return new ProfessionMcpSnapshot(CreatePlanRequest(catalog, goal, job[0].Id),
                new ProfessionMcpProvenance("local-extraction", "owner-approved-catalog",
                    catalog.Source.GameVersion, catalog.Source.FileSha256,
                    AiDisclosureAuthorized: true, AuthorizationEvidenceId: approval));
        }
        catch (Exception e) when (e is ArgumentException or OverflowException) { return null; }
    }

    /// <summary>MCP callers may SELECT a read-only goal, but cannot assert their
    /// own XP/stock/permission. The source and AI disclosure still need a
    /// separately pinned user-managed local extraction.</summary>
    public static ProfessionMcpSnapshot? ApprovedMcpSnapshotFromQuery(string? json)
    {
        if (json is not { Length: > 0 and <= 4096 }) return null;
        try
        {
            using var doc = JsonDocument.Parse(json,
                new JsonDocumentOptions { MaxDepth = 5, CommentHandling = JsonCommentHandling.Disallow });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("startLevel", out var startNode)
                || !root.TryGetProperty("targetLevel", out var targetNode)
                || !startNode.TryGetInt32(out int start)
                || !targetNode.TryGetInt32(out int target)
                || start is < 1 or > 200 || target is < 1 or > 200 || target < start)
                return null;
            var catalog = GetCatalog();
            if (catalog is not { Admitted: true }) return null;
            string? jobName = null;
            if (root.TryGetProperty("jobId", out var jobId))
            {
                if (!jobId.TryGetInt32(out var id)) return null;
                jobName = catalog.Jobs.SingleOrDefault(j => j.Id == id)?.Name;
            }
            else if (root.TryGetProperty("jobName", out var nameNode) &&
                     nameNode.ValueKind == JsonValueKind.String)
                jobName = nameNode.GetString();
            if (string.IsNullOrWhiteSpace(jobName)) return null;
            long? recipeId = null, crafts = null;
            if (root.TryGetProperty("trainingRecipeId", out var recipeNode))
            {
                if (!recipeNode.TryGetInt64(out long id) || id <= 0) return null;
                recipeId = id;
            }
            if (root.TryGetProperty("requestedCrafts", out var countNode))
            {
                if (!countNode.TryGetInt64(out long qty) || qty < 0 || qty > 1_000_000) return null;
                crafts = qty;
            }
            return ApprovedMcpSnapshot(new ProfessionPlannerGoal(jobName, start, target,
                new Dictionary<string, long>(), recipeId?.ToString(CultureInfo.InvariantCulture),
                "Équilibré", crafts));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static string ShortHash(string hash) =>
        hash.Length >= 12 ? hash[..12] + "…" : "UNKNOWN";
}
