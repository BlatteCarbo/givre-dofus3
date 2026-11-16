using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GivreClone.App.Services.EquipmentSetData;
using GivreClone.App.Services.ItemCraftMcp;
using GivreClone.App.Services.ProfessionData;
using GivreClone.App.Services.UnifiedStock;
using GivreClone.App.ViewModels;
using CraftEngine = GivreClone.App.Services.ItemCraftPlanner.ItemCraftPlanner;
using CraftTarget = GivreClone.App.Services.ItemCraftPlanner.ItemCraftTarget;
using CraftContext = GivreClone.App.Services.ItemCraftPlanner.ItemCraftContext;
using CraftRecipe = GivreClone.App.Services.ItemCraftPlanner.CraftRecipe;
using CraftSource = GivreClone.App.Services.ItemCraftPlanner.AcquisitionSource;
using CraftStack = GivreClone.App.Services.ItemCraftPlanner.ItemStockStack;
using CraftSet = GivreClone.App.Services.ItemCraftPlanner.EquipmentSetDefinition;
using CraftSetTarget = GivreClone.App.Services.ItemCraftPlanner.EquipmentSetTarget;
using ItemAcquisitionKind = GivreClone.App.Services.ItemCraftPlanner.ItemAcquisitionKind;

namespace GivreClone.App.Services;

/// <summary>
/// Explicit local review only. A user-selected SHA256-manifested dataset is
/// distinct from legal authorization, publisher authenticity and game receipts.
/// No DofusDB public API source, no gameplay, no terminal/browser requests.
/// </summary>
public static class ItemCraftAccessService
{
    private static readonly object Gate = new();
    private static string? _folder;
    private static EquipmentSetCatalog? _sets;
    private static ProfessionCatalog? _recipes;
    private static readonly LiveStockEvidence Live = new();

    /// <summary>Subscribe passively before a character's snapshot arrives.</summary>
    public static void AttachLiveStock(LaunchService launch) => Live.Attach(launch);

    public static void SelectFolder(string? folder)
    {
        lock (Gate)
        {
            _folder = string.IsNullOrWhiteSpace(folder) ? null : folder;
            _sets = null;
            _recipes = null;
        }
    }

    private static (EquipmentSetCatalog? Sets, ProfessionCatalog? Recipes) Catalogs()
    {
        lock (Gate)
        {
            string? folder = _folder ?? Environment.GetEnvironmentVariable("GIVRE_ITEM_CATALOG_DIR");
            if (string.IsNullOrWhiteSpace(folder)) return (null, null);
            string setsDir = File.Exists(Path.Combine(folder, "equipment-sets.json"))
                ? folder : Path.Combine(folder, "sets");
            string recipesDir = File.Exists(Path.Combine(folder, "recipes.json")) ||
                                File.Exists(Path.Combine(folder, "dofus.sqlite"))
                ? folder : Path.Combine(folder, "recipes");
            var version = Environment.GetEnvironmentVariable("GIVRE_ITEM_EXPECTED_VERSION");
            _sets ??= EquipmentSetCatalogReader.LoadDirectory(setsDir, version);
            _recipes ??= ProfessionCatalogReader.LoadDirectory(recipesDir, version);
            return (_sets, _recipes);
        }
    }

    public static IReadOnlyList<ItemCraftCatalogEntry> ListCatalog(string? query = null, string? mode = null,
        IReadOnlyList<GivreClone.App.ViewModels.ItemCraftTarget>? existingTargets = null)
    {
        var (sets, recipes) = Catalogs();
        if (sets is not { Admitted: true }) return Array.Empty<ItemCraftCatalogEntry>();
        string proof = $"catalogue local hashé {sets.Source.GameVersion} SHA {Hash(sets.Source.FileSha256)}" +
            " · origine déclarée, éditeur/jeu NON certifiés";
        string search = query?.Trim() ?? "";
        if (search.Length > 96) return Array.Empty<ItemCraftCatalogEntry>();
        var previousIds = (existingTargets ?? Array.Empty<GivreClone.App.ViewModels.ItemCraftTarget>())
            .Select(t => t.ItemId).ToHashSet(StringComparer.Ordinal);
        var previousSets = (existingTargets ?? Array.Empty<GivreClone.App.ViewModels.ItemCraftTarget>())
            .Select(t => t.GroupName).Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previousMemberIds = sets.Items.Where(i =>
            previousIds.Contains(i.Id.ToString(CultureInfo.InvariantCulture)))
            .Select(i => i.Id).ToHashSet();
        var candidates = sets.Sets.Where(s => previousSets.Contains(s.Name ?? "") ||
            (mode == "Panoplie" && (string.IsNullOrEmpty(search) ||
            s.Name?.Contains(search, StringComparison.OrdinalIgnoreCase) == true ||
            s.Id.ToString(CultureInfo.InvariantCulture).Contains(search, StringComparison.Ordinal))))
            .OrderByDescending(s => previousSets.Contains(s.Name ?? ""))
            .ThenByDescending(s => string.Equals(s.Name, search, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(s => s.Name?.StartsWith(search, StringComparison.OrdinalIgnoreCase) == true)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var chosenSets = new List<EquipmentSetData.EquipmentSet>();
        var members = new HashSet<long>(previousMemberIds);
        foreach (var set in candidates)
        {
            if (chosenSets.Count >= 30) break;
            // The UI accepts at most 300 rows. A set is shown ONLY if all its
            // member item cards can be included; never show an incomplete set.
            var more = set.MemberItemIds.Where(id => !members.Contains(id)).ToArray();
            if (members.Count + more.Length + chosenSets.Count + 1 > 290)
                continue;
            chosenSets.Add(set);
            members.UnionWith(more);
        }
        var matches = chosenSets.ToArray();
        // Important: the panel loads only 300 cards. Prioritize the matching
        // set and ALL its members so a full set never loses pieces when
        // a large game's item catalog is paginated. Search is applied at the
        // source instead of searching just a truncated prefix.
        var selectedItems = sets.Items.Where(i =>
            members.Contains(i.Id) ||
            (mode != "Panoplie" && (string.IsNullOrEmpty(search) ||
            i.Name?.Contains(search, StringComparison.OrdinalIgnoreCase) == true ||
            i.Id.ToString(CultureInfo.InvariantCulture).Contains(search, StringComparison.Ordinal))))
            .OrderByDescending(i => previousMemberIds.Contains(i.Id))
            .ThenByDescending(i => members.Contains(i.Id))
            .ThenBy(i => i.Id).Take(300 - matches.Length).ToArray();
        var entries = selectedItems.Select(i => new ItemCraftCatalogEntry(
            i.Id.ToString(CultureInfo.InvariantCulture),
            i.Name ?? $"Objet GID {i.Id}", "Objet", proof, IsVerified: sets.Source.LocalHashVerified))
            .Concat(matches.Select(s => new ItemCraftCatalogEntry(
                s.Id.ToString(CultureInfo.InvariantCulture),
                s.Name ?? $"Panoplie ID {s.Id}", "Panoplie", proof,
                IsVerified: sets.Source.LocalHashVerified,
                MemberItemIds: s.MemberItemIds.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray())))
            .ToArray();
        if (recipes is { Admitted: true } && recipes.Source.GameVersion != sets.Source.GameVersion)
            return entries.Select(x => x with { Provenance = x.Provenance + " · recettes VERSION DIFFÉRENTE : non chargées" }).ToArray();
        return entries;
    }

    public static ItemCraftPlanSnapshot? BuildPlan(ItemCraftPlannerRequest request,
        uint? selectedPid = null, string? accountKey = null)
    {
        var (sets, recipes) = Catalogs();
        if (sets is not { Admitted: true } || request.Targets.Count == 0) return null;
        if (request.Targets.Count > 60 || request.Inventory.Count > 100)
            return Block("Objectif trop volumineux.");
        var names = sets.Items.ToDictionary(i => i.Id,
            i => i.Name ?? $"Objet GID {i.Id}");
        var byName = sets.Items.Where(i => i.Name is not null)
            .GroupBy(i => i.Name!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().Id, StringComparer.OrdinalIgnoreCase);
        var allItems = new List<CraftTarget>();
        var warnings = new List<string>();
        foreach (var t in request.Targets)
        {
            if (!long.TryParse(t.ItemId, NumberStyles.None, CultureInfo.InvariantCulture, out long gid)
                || sets.FindItem(gid) is null || t.Quantity is < 1 or > 100_000)
                return Block("La cible n'appartient pas au catalogue local haché.");
            allItems.Add(new CraftTarget(gid, t.Quantity));
        }
        if (recipes is not { Admitted: true } ||
            recipes.Source.GameVersion != sets.Source.GameVersion)
            warnings.Add("Recettes absentes ou version différente : les ingrédients restent inconnus.");
        // Pull a bounded REACHABLE dependency closure instead of an arbitrary
        // first-4000 prefix of all recipes: an item at the end of the game's
        // catalogue must not become invisible merely because of file order.
        var selectedRecipeRows = new List<GivreClone.App.Services.ProfessionData.ProfessionRecipe>();
        if (recipes is { Admitted: true } &&
            recipes.Source.GameVersion == sets.Source.GameVersion)
        {
            // Local SQLite recipes use UUID identities and may omit output
            // yield. Preserve those real ingredient links as partial evidence;
            // the engine renders UNKNOWN totals and never assumes yield=1.
            var recipeIndex = recipes.Recipes.Where(r => r.Ingredients.Count > 0)
                .GroupBy(r => r.ResultItemId).ToDictionary(g => g.Key, g => g.ToArray());
            var pending = new Stack<long>(allItems.Select(t => t.ItemId).Distinct());
            var seen = new HashSet<long>();
            while (pending.Count > 0 && selectedRecipeRows.Count < 4000)
            {
                long target = pending.Pop();
                if (!seen.Add(target) || !recipeIndex.TryGetValue(target, out var options)) continue;
                foreach (var candidate in options)
                {
                    if (selectedRecipeRows.Count >= 4000) break;
                    selectedRecipeRows.Add(candidate);
                    foreach (var ingredient in candidate.Ingredients)
                        if (!seen.Contains(ingredient.ItemId)) pending.Push(ingredient.ItemId);
                }
            }
            if (pending.Count > 0)
                warnings.Add("Limite de 4 000 recettes atteinte ; dépendances restantes UNKNOWN.");
        }
        // CraftRecipe's numeric ID is an EPHEMERAL LOCAL GRAPH KEY, not an
        // official game recipe ID. Stable ordered indices prevent UUID parsing
        // failures and cannot collide within this bounded plan.
        var engineRecipes = selectedRecipeRows.OrderBy(r => r.Id, StringComparer.Ordinal)
            .Select((r, index) => new CraftRecipe(
                    index + 1L, r.ResultItemId, r.ResultQuantity,
                    r.Ingredients.Select(i => new GivreClone.App.Services.ItemCraftPlanner.CraftIngredient(
                        i.ItemId, i.Quantity)).ToArray(), recipes.Source.GameVersion,
                    Verified: recipes.Source.LocalHashVerified,
                    JobId: r.JobId, RequiredLevel: r.RequiredLevel)).ToArray();
        if (selectedRecipeRows.Any(r => r.ResultQuantity is null))
            warnings.Add("Recettes avec rendement inconnu : ingrédients par fabrication consultables, " +
                "mais nombre de fabrications et besoins totaux UNKNOWN.");
        var inventory = new List<CraftStack>();
        int stackNo = 0;
        foreach (var entry in request.Inventory.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!byName.TryGetValue(entry.Key, out long gid) &&
                !long.TryParse(entry.Key, NumberStyles.None, CultureInfo.InvariantCulture, out gid))
            {
                warnings.Add($"Stock « {entry.Key} » : nom ambigu / GID inconnu, non déduit.");
                continue;
            }
            if (entry.Value < 0 || gid <= 0 || !names.ContainsKey(gid)) continue;
            // Explicitly unverified manual counts are displayed, but NEVER consumed
            // by the engine as observed authoritative stock.
            inventory.Add(new CraftStack($"manu-{stackNo++}", gid, entry.Value,
                "déclaré-manuellement", Verified: false));
        }
        // A runtime stock can be counted only after actual decoded full
        // inventory/bank snapshots of THIS character, and only when the
        // installed local GameData version matches the admitted recipe source.
        // No bank interaction and no Havre-Sac storage operation is invoked.
        if (selectedPid is { } pid && pid > 0 && !string.IsNullOrWhiteSpace(accountKey))
        {
            var launch = Stores.Launch;
            if (!string.Equals(launch.LotMGameVersion, sets.Source.GameVersion,
                    StringComparison.Ordinal))
                warnings.Add("Version runtime/catalogue non certifiée identique : stocks du bot ignorés.");
            else if (launch.CharacterId(pid) is not > 0)
                warnings.Add("Identité du personnage non confirmée : stocks du bot ignorés.");
            else
            {
                var now = DateTimeOffset.UtcNow;
                var observations = Live.Read(pid, accountKey, sets.Source.GameVersion, now);
                var allKnownGids = observations.SelectMany(x => x.Stacks)
                    .Where(x => x.ItemGid > 0)
                    .Select(x => x.ItemGid).Distinct().Take(10000).ToDictionary(
                        x => x, _ => 1_000_000_000L);
                if (allKnownGids.Count > 0)
                {
                    var allocations = UnifiedStockPlanner.Allocate(accountKey, launch.CharacterId(pid),
                        sets.Source.GameVersion, now, observations, allKnownGids);
                    foreach (var row in allocations.Items)
                    {
                        if (row.FromInventory > 0) inventory.Add(new CraftStack(
                            $"obs-bag-{row.ItemGid}", row.ItemGid, row.FromInventory, "sac-observé", true));
                        if (row.FromBank > 0) inventory.Add(new CraftStack(
                            $"obs-bank-{row.ItemGid}", row.ItemGid, row.FromBank, "banque-observée", true));
                        if (row.FromHavenBag > 0) inventory.Add(new CraftStack(
                            $"obs-haven-{row.ItemGid}", row.ItemGid, row.FromHavenBag, "havre-sac-observé", true));
                    }
                    warnings.AddRange(allocations.Warnings);
                    warnings.Add($"Stocks observés du personnage : {allocations.Items.Count} GID, " +
                        "sac et banque uniquement lorsque snapshots récents et complets.");
                }
                else warnings.Add("Aucun stock serveur récent et complet observable ; quantité inconnue.");
            }
        }
        var methodChoices = new Dictionary<long, GivreClone.App.Services.ItemCraftPlanner.ItemAcquisitionKind>();
        foreach (var choice in request.ItemMethodChoices ??
                 new Dictionary<string, string>())
        {
            if (!long.TryParse(choice.Key, out var gid)) continue;
            GivreClone.App.Services.ItemCraftPlanner.ItemAcquisitionKind? kind = choice.Value switch
            {
                "Fabriquer" => GivreClone.App.Services.ItemCraftPlanner.ItemAcquisitionKind.Craft,
                "Récolter" => GivreClone.App.Services.ItemCraftPlanner.ItemAcquisitionKind.Gather,
                "Drop" => GivreClone.App.Services.ItemCraftPlanner.ItemAcquisitionKind.Drop,
                "Acheter" => GivreClone.App.Services.ItemCraftPlanner.ItemAcquisitionKind.Buy,
                _ => null
            };
            if (kind is not null) methodChoices[gid] = kind.Value;
        }
        var context = new CraftContext(
            engineRecipes, inventory, Array.Empty<CraftSource>(),
            // This UI already expanded and checked set members against the
            // admitted source; PlanList does not require the full sets table.
            Array.Empty<CraftSet>(),
            KnownJobLevels: new Dictionary<int, int>(),
            MethodChoices: methodChoices);
        GivreClone.App.Services.ItemCraftPlanner.ItemCraftPlan result;
        try { result = CraftEngine.PlanList(allItems, context); }
        catch (Exception e) when (e is ArgumentException or ArithmeticException or OverflowException)
        {
            return Block("Calcul non fiable : " + e.GetType().Name);
        }
        var nodeById = result.Steps.ToDictionary(s => s.Id);
        var included = new HashSet<int>();
        var nodes = new List<ItemCraftPlanNode>();
        void Visit(int id, int depth)
        {
            if (nodes.Count >= CraftEngine.MaxSteps || !nodeById.TryGetValue(id, out var step)
                || !included.Add(id)) return;
            var methods = step.Alternatives.Select(x => x.Kind switch
            {
                GivreClone.App.Services.ItemCraftPlanner.ItemAcquisitionKind.Craft => "Fabriquer",
                GivreClone.App.Services.ItemCraftPlanner.ItemAcquisitionKind.Gather => "Récolter",
                GivreClone.App.Services.ItemCraftPlanner.ItemAcquisitionKind.Drop => "Drop",
                _ => "Acheter"
            }).Distinct().ToArray();
            nodes.Add(new ItemCraftPlanNode(step.ItemId.ToString(CultureInfo.InvariantCulture),
                names.GetValueOrDefault(step.ItemId, $"Objet GID {step.ItemId}"),
                depth, step.Kind.ToString(),
                step.Quantity is { } q ? $"× {q:N0}" : "Quantité inconnue",
                step.Kind == GivreClone.App.Services.ItemCraftPlanner.ItemCraftStepKind.UseInventory
                    ? $"× {step.Quantity:N0}" : "Non établi",
                step.Kind == GivreClone.App.Services.ItemCraftPlanner.ItemCraftStepKind.Unresolved
                    ? "INCONNU" : step.Kind.ToString(),
                step.Explanation,
                $"Source {sets.Source.GameVersion} SHA {Hash(sets.Source.FileSha256)} · " +
                (step.Verified ? "preuve locale" : "UNVERIFIED"),
                methods));
            foreach (int child in step.Dependencies) Visit(child, depth + 1);
        }
        foreach (var t in result.TargetSummaries)
            foreach (var rootId in t.RootStepIds) Visit(rootId, 0);
        warnings.AddRange(result.Warnings);
        if (result.Steps.Count > nodes.Count)
            warnings.Add($"Graphe incomplet à l'affichage : {nodes.Count}/{result.Steps.Count} nœuds accessibles.");
        warnings.Add("Havre-Sac : seul le voyage/zaap est observé ; aucun coffre indépendant attesté." +
                     " Son contenu n'est PAS assimilé à zéro.");
        if (request.Inventory.Count > 0)
            warnings.Add("Stock manuel non attesté : saisie visible mais non déduite automatiquement.");
        return new ItemCraftPlanSnapshot(
            $"{request.Mode} · {request.Targets.Count} pièce(s) à fabriquer",
            $"{result.Targets.Count} cible(s) réunies, {result.Steps.Count} étapes calculées " +
            "sur un stock et des surplus partagés. Simulation, jamais exécution.",
            $"{sets.Source.SourceLabel} · {sets.Source.GameVersion} SHA {Hash(sets.Source.FileSha256)}",
            // Presentation contract: this flag means the local catalogue was
            // checked and the graph may be rendered (including UNKNOWN nodes).
            // It does NOT mean publisher/game/ACK verification; those states
            // remain explicitly marked in Coverage and each node.
            IsVerified: sets.Source.LocalHashVerified, IsFictional: false,
            result.FullKamasEstimate is { } k ? $"{k:N0} kamas (source locale)" : "Kamas inconnus",
            result.FullSecondsEstimate is { } secs ? $"{secs:N0} secondes (source locale)" : "Durée inconnue",
            $"{result.Targets.Count} cible(s) · {result.Missing.Count} ressource(s) à éclaircir",
            nodes, string.Join(" • ", warnings.Take(15)),
            result.IsComplete ? "SOURCES LOCALES · À CERTIFIER EN JEU" : "PARTIEL · ACQUISITIONS INCONNUES",
            IsComplete: result.IsComplete);
    }

    private static ItemCraftPlanSnapshot Block(string message) =>
        new("Plan bloqué", message, "Aucune source admise", false, false,
            "Inconnu", "Inconnue", "Non certifié", [], message, "INCONNU");
    private static string Hash(string value) => value.Length >= 12 ? value[..12] + "…" : "UNKNOWN";

    /// <summary>
    /// The ordinary UI is not an AI-disclosure grant. For MCP we require
    /// separately reviewed local extraction, exact SHA of both assets,
    /// matching game versions, and an explicit non-DofusDB approval ID.
    /// No live stock or game/account data is exposed here.
    /// </summary>
    public static ItemCraftMcpSnapshot? ApprovedMcpSnapshot()
    {
        if (!AppConfig.Current.McpEnabled) return null;
        var (sets, recipes) = Catalogs();
        if (sets is not { Admitted: true } || recipes is not { Admitted: true } ||
            sets.Source.SourceLabel != "local-extraction" ||
            recipes.Source.SourceLabel != "local-extraction" ||
            sets.Source.GameVersion != recipes.Source.GameVersion)
            return null;
        string? expectedSets = Environment.GetEnvironmentVariable("GIVRE_ITEM_AI_APPROVED_SETS_SHA256");
        string? expectedRecipes = Environment.GetEnvironmentVariable("GIVRE_ITEM_AI_APPROVED_RECIPES_SHA256");
        string? authorization = Environment.GetEnvironmentVariable("GIVRE_ITEM_AI_AUTH_ID");
        if (!string.Equals(expectedSets, sets.Source.FileSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(expectedRecipes, recipes.Source.FileSha256, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(authorization))
            return null;
        var proof = new ItemCraftMcpEvidence("local-extraction", "owner-approved-set-and-recipes",
            sets.Source.GameVersion, sets.Source.FileSha256, AiDisclosureAuthorized: true,
            AuthorizationEvidenceId: authorization);
        if (!proof.CanDisclose()) return null;
        var setTargets = sets.Sets.Where(s => s.MemberItemIds.Count is > 0 and <=
            ItemCraftMcpSnapshot.MaxPiecesPerSet)
            .Take(1000).Select(s => new ItemCraftMcpTarget(
                ItemCraftMcpTargetKind.Set, s.Id, SafeName(s.Name, $"Set {s.Id}"),
                s.MemberItemIds, proof, Verified: true)).ToArray();
        // Do not arbitrarily drop the last thousands of game items when the
        // authorized catalog fits the MCP's explicit bound.
        var items = sets.Items.Take(Math.Max(0, ItemCraftMcpSnapshot.MaxTargets - setTargets.Length))
            .Select(i => new ItemCraftMcpTarget(
            ItemCraftMcpTargetKind.Item, i.Id, SafeName(i.Name, $"Item {i.Id}"),
            Array.Empty<long>(), proof, Verified: true));
        var targets = items.Concat(setTargets).ToArray();
        // All authorized UUID/non-UUID records stay indexed. For each MCP
        // query, the façade derives a bounded dependency closure instead of
        // silently excluding the >4,000th recipe. Local graph IDs are never
        // represented as game recipe IDs. Null yield stays UNKNOWN.
        var catalogRows = recipes.Recipes.Where(r => r.Ingredients.Count > 0)
            .Select(r => new ItemCraftMcpRecipeRow(r.Id, r.ResultItemId, r.ResultQuantity,
                r.Ingredients.Select(i =>
                    new GivreClone.App.Services.ItemCraftPlanner.CraftIngredient(
                        i.ItemId, i.Quantity)).ToArray(),
                recipes.Source.GameVersion, recipes.Source.LocalHashVerified,
                JobId: r.JobId, RequiredLevel: r.RequiredLevel)).ToArray();
        try
        {
            return new ItemCraftMcpSnapshot(proof, targets,
                inventoryVerified: false, jobLevelsVerified: false,
                recipeCatalog: new ItemCraftMcpRecipeCatalog(proof, catalogRows));
        }
        catch (Exception e) when (e is ArgumentException or OverflowException) { return null; }
    }

    private static string SafeName(string? raw, string fallback) =>
        raw is { Length: > 0 and <= 100 } && !raw.Any(char.IsControl) ? raw : fallback;
}
