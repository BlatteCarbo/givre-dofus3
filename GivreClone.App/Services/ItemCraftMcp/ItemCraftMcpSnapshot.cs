using System.Collections.ObjectModel;
using GivreClone.App.Services.ItemCraftPlanner;

namespace GivreClone.App.Services.ItemCraftMcp;

/// <summary>
/// Immutable target index admitted by a trusted, manifest-verifying importer.
/// It is deliberately separate from the game client and from MCP transport.
/// </summary>
public sealed class ItemCraftMcpSnapshot
{
    // Local source currently contains >20k individual items plus ~1k sets.
    // 40k covers the complete approved catalog while bounding allocation.
    public const int MaxTargets = 40_000;
    public const int MaxPiecesPerSet = 32;
    public const int MaxInputRows = 40000;
    public const int MaxPlans = 512;
    public const int MaxPlanRows = 2048;
    public const long MaxQuantity = 1_000_000_000;

    public ItemCraftMcpEvidence Evidence { get; }
    public IReadOnlyDictionary<long, ItemCraftMcpTarget> Items { get; }
    public IReadOnlyDictionary<long, ItemCraftMcpTarget> Sets { get; }
    public IReadOnlyDictionary<(ItemCraftMcpTargetKind Kind, long Id, long Quantity),
        ItemCraftMcpPlanResult> Plans { get; }
    public ItemCraftContext? Context { get; }
    public ItemCraftMcpRecipeCatalog? RecipeCatalog { get; }
    public bool InventoryVerified { get; }
    public bool JobLevelsVerified { get; }

    /// <summary>
    /// Constructs metadata only. Prime must cryptographically validate the hash
    /// and source rights before handing a snapshot to the MCP interface.
    /// </summary>
    public ItemCraftMcpSnapshot(ItemCraftMcpEvidence evidence,
        IReadOnlyCollection<ItemCraftMcpTarget> targets,
        IReadOnlyCollection<ItemCraftMcpPlanResult>? plans = null,
        ItemCraftContext? context = null,
        bool inventoryVerified = false,
        bool jobLevelsVerified = false,
        ItemCraftMcpRecipeCatalog? recipeCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count > MaxTargets)
            throw new ArgumentException("Target index exceeds cap.");

        Evidence = evidence;
        var items = new Dictionary<long, ItemCraftMcpTarget>();
        var sets = new Dictionary<long, ItemCraftMcpTarget>();
        foreach (var target in targets)
        {
            if (target is null || !Enum.IsDefined(target.Kind) || target.Id <= 0 ||
                target.Name is null || target.Name.Length is < 1 or > 100 ||
                target.Name.Any(char.IsControl) ||
                target.PieceGids is null || target.PieceGids.Count > MaxPiecesPerSet ||
                target.PieceGids.Any(x => x <= 0) ||
                (target.Kind == ItemCraftMcpTargetKind.Item && target.PieceGids.Count != 0) ||
                (target.Kind == ItemCraftMcpTargetKind.Set &&
                    target.PieceGids.Distinct().Count() != target.PieceGids.Count) ||
                target.Evidence is null)
                throw new ArgumentException("Malformed craft target.");
            var copy = target with
            {
                PieceGids = Array.AsReadOnly(target.PieceGids.ToArray())
            };
            var index = target.Kind == ItemCraftMcpTargetKind.Item ? items : sets;
            if (!index.TryAdd(target.Id, copy))
                throw new ArgumentException("Duplicate craft target ID.");
        }
        Items = new ReadOnlyDictionary<long, ItemCraftMcpTarget>(items);
        Sets = new ReadOnlyDictionary<long, ItemCraftMcpTarget>(sets);

        var entries = plans ?? Array.Empty<ItemCraftMcpPlanResult>();
        if (entries.Count > MaxPlans) throw new ArgumentException("Plan cache exceeds cap.");
        var planIndex = new Dictionary<(ItemCraftMcpTargetKind, long, long), ItemCraftMcpPlanResult>();
        foreach (var plan in entries)
        {
            if (plan is null || !Enum.IsDefined(plan.Kind) || plan.TargetId <= 0 ||
                plan.Quantity is <= 0 or > MaxQuantity ||
                plan.Materials is null || plan.Steps is null || plan.Warnings is null ||
                plan.Materials.Count > MaxPlanRows || plan.Steps.Count > MaxPlanRows ||
                plan.Warnings.Count > 64 || plan.Evidence is null ||
                plan.Materials.Any(m => m is null || m.ItemGid <= 0 ||
                    m.Required is < 0 or > MaxQuantity ||
                    m.Owned is < 0 or > MaxQuantity ||
                    m.Missing is < 0 or > MaxQuantity ||
                    !SafeMethod(m.Method) || !SafeSource(m.SourceId)) ||
                plan.Steps.Any(s => s is null || s.StepId <= 0 || s.ItemGid <= 0 ||
                    s.Quantity is < 0 or > MaxQuantity || s.RecipeId is <= 0 ||
                    !SafeMethod(s.Method) || !SafeSource(s.SourceId) ||
                    !SafeSource(s.LocalRecipeKey) ||
                    s.Dependencies is null || s.Dependencies.Count > 64) ||
                plan.Warnings.Any(w => w is null || w.Length > 256 || w.Any(char.IsControl)))
                throw new ArgumentException("Malformed cached craft plan.");

            var seen = new HashSet<int>();
            foreach (var step in plan.Steps)
                if (!seen.Add(step.StepId) || step.Dependencies.Any(d => d <= 0 || d >= step.StepId))
                    throw new ArgumentException("Duplicate or invalid dependency in cached plan.");
            if (plan.Steps.Any(s => s.Dependencies.Any(d => !seen.Contains(d))))
                throw new ArgumentException("Plan dependency has no corresponding step.");
            var plansCopy = plan with
            {
                Materials = Array.AsReadOnly(plan.Materials.ToArray()),
                Steps = Array.AsReadOnly(plan.Steps.Select(s => s with
                {
                    Dependencies = Array.AsReadOnly(s.Dependencies.ToArray())
                }).ToArray()),
                Warnings = Array.AsReadOnly(plan.Warnings.ToArray())
            };
            if (!(plan.Kind == ItemCraftMcpTargetKind.Item ? items : sets).ContainsKey(plan.TargetId) ||
                !planIndex.TryAdd((plan.Kind, plan.TargetId, plan.Quantity), plansCopy))
                throw new ArgumentException("Cached plan target unknown or duplicated.");
        }
        Plans = new ReadOnlyDictionary<(ItemCraftMcpTargetKind, long, long),
            ItemCraftMcpPlanResult>(planIndex);
        RecipeCatalog = recipeCatalog;
        InventoryVerified = inventoryVerified;
        JobLevelsVerified = jobLevelsVerified;
        if (recipeCatalog is not null && context is { Recipes.Count: > 0 })
            throw new ArgumentException("Supply recipes through one source-indexed catalog only.");
        if (context is not null)
        {
            if (context.Recipes is null || context.Inventory is null || context.Sources is null ||
                context.Sets is null || context.KnownJobLevels is null ||
                context.Recipes.Count > 4000 || context.Inventory.Count > 20000 ||
                context.Sources.Count > 10000 || context.Sets.Count > 1000 ||
                context.Recipes.Any(r => r is null || r.Ingredients is null) ||
                context.Inventory.Any(s => s is null) ||
                context.Sources.Any(s => s is null || !SafeSource(s.SourceId)) ||
                context.Sets.Any(s => s is null || s.ItemIds is null))
                throw new ArgumentException("Item craft input is incomplete or too large.");
            // An incomplete/partly unverified stack collection is not a certified
            // inventory snapshot even if a caller accidentally sets the flag.
            InventoryVerified = inventoryVerified && context.Inventory.All(s => s.Verified);
            Context = context with
            {
                Recipes = Array.AsReadOnly(context.Recipes.Select(r => r with
                {
                    Ingredients = Array.AsReadOnly(r.Ingredients.ToArray())
                }).ToArray()),
                // No trusted inventory receipt: ignore ALL stock and never fabricate
                // deductions from a client/bank cache that was not independently checked.
                Inventory = InventoryVerified
                    ? Array.AsReadOnly(context.Inventory.ToArray())
                    : Array.AsReadOnly(Array.Empty<ItemStockStack>()),
                Sources = Array.AsReadOnly(context.Sources.ToArray()),
                Sets = Array.AsReadOnly(context.Sets.Select(s => s with
                {
                    ItemIds = Array.AsReadOnly(s.ItemIds.ToArray())
                }).ToArray()),
                KnownJobLevels = new ReadOnlyDictionary<int, int>(
                    jobLevelsVerified ? new Dictionary<int, int>(context.KnownJobLevels) : new()),
                RecipeChoices = context.RecipeChoices is null ? null :
                    new ReadOnlyDictionary<long, long>(new Dictionary<long, long>(context.RecipeChoices)),
                MethodChoices = context.MethodChoices is null ? null :
                    new ReadOnlyDictionary<long, ItemAcquisitionKind>(
                        new Dictionary<long, ItemAcquisitionKind>(context.MethodChoices))
            };
        }
    }

    public bool CanDisclose() => Evidence.CanDisclose() &&
        Items.Values.Concat(Sets.Values).All(t =>
            t.Evidence.CanDisclose() &&
            string.Equals(t.Evidence.GameVersion, Evidence.GameVersion, StringComparison.Ordinal) &&
            string.Equals(t.Evidence.ManifestSha256, Evidence.ManifestSha256, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(t.Evidence.SourceId, Evidence.SourceId, StringComparison.Ordinal) &&
            string.Equals(t.Evidence.SourceKind, Evidence.SourceKind, StringComparison.Ordinal)) &&
        Plans.Values.All(p => p.Evidence.CanDisclose() &&
            string.Equals(p.Evidence.GameVersion, Evidence.GameVersion, StringComparison.Ordinal) &&
            string.Equals(p.Evidence.ManifestSha256, Evidence.ManifestSha256, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.Evidence.SourceId, Evidence.SourceId, StringComparison.Ordinal) &&
            string.Equals(p.Evidence.SourceKind, Evidence.SourceKind, StringComparison.Ordinal)) &&
        (RecipeCatalog is null || RecipeCatalog.CanDisclose(Evidence)) &&
        (Context is null ||
         Context.Recipes.All(r => string.Equals(r.SourceVersion, Evidence.GameVersion, StringComparison.Ordinal)) &&
         Context.Sources.All(s => string.Equals(s.SourceVersion, Evidence.GameVersion, StringComparison.Ordinal) &&
             SafeSource(s.SourceId) && !ForbiddenSource(s.SourceId)) &&
         Context.Sets.All(s => string.Equals(s.SourceVersion, Evidence.GameVersion, StringComparison.Ordinal)));

    public ItemCraftMcpTarget? Find(ItemCraftMcpTargetKind kind, long id) =>
        (kind == ItemCraftMcpTargetKind.Item ? Items : Sets).TryGetValue(id, out var target)
            ? target : null;

    public ItemCraftMcpPlanResult? FindPlan(ItemCraftMcpTargetKind kind, long id, long quantity) =>
        Plans.TryGetValue((kind, id, quantity), out var plan) ? plan : null;

    private static bool ForbiddenSource(string? source)
    {
        var letters = new string((source ?? "").Where(char.IsAsciiLetterOrDigit).ToArray());
        return letters.Contains("dofusdb", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SafeMethod(string? method) => method is
        "CRAFT" or "FARM" or "GATHER" or "DROP" or "BUY" or "STOCK" or "UNRESOLVED" or "UNKNOWN";

    private static bool SafeSource(string? source) =>
        source is null || source.Length is >= 1 and <= 80 &&
        source.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or ':');
}
