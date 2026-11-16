using System.Collections.ObjectModel;
using GivreClone.App.Services.ItemCraftPlanner;

namespace GivreClone.App.Services.ItemCraftMcp;

/// <summary>
/// Local IDs are labels of source-owned records (including UUIDs), never
/// official Dofus recipe IDs. Nullable yield/ingredient amounts retain UNKNOWN.
/// </summary>
public sealed record ItemCraftMcpRecipeRow(
    string LocalRecipeKey, long ResultItemGid, long? ResultQuantity,
    IReadOnlyList<CraftIngredient> Ingredients, string SourceVersion, bool Verified,
    int? JobId = null, int? RequiredLevel = null, decimal? KamasPerCraft = null,
    long? SecondsPerCraft = null, bool RequiresWorkshop = false,
    bool WorkshopVerified = false);

/// <summary>
/// Source-approved catalog with bounded, index-backed closure per requested
/// target. A large global index is not passed to the 4,000-recipe engine.
/// Any over-limit closure is rejected in full, never silently truncated.
/// </summary>
public sealed class ItemCraftMcpRecipeCatalog
{
    public const int MaxCatalogRecipes = 50_000;
    public const int MaxCatalogIngredients = 500_000;
    public const int MaxClosureRecipes = 4_000;
    public const int MaxClosureItems = 8_192;

    public ItemCraftMcpEvidence Evidence { get; }
    public IReadOnlyList<ItemCraftMcpRecipeRow> Recipes { get; }
    private readonly IReadOnlyDictionary<long, IReadOnlyList<ItemCraftMcpRecipeRow>> _byProduct;
    private readonly IReadOnlyDictionary<string, long> _ephemeralIds;
    private readonly IReadOnlyDictionary<long, string> _localKeys;

    public ItemCraftMcpRecipeCatalog(ItemCraftMcpEvidence evidence,
        IReadOnlyCollection<ItemCraftMcpRecipeRow> recipes)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(recipes);
        if (recipes.Count > MaxCatalogRecipes)
            throw new ArgumentException("Catalog contains too many recipes.");

        Evidence = evidence;
        var all = new List<ItemCraftMcpRecipeRow>(recipes.Count);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int ingredientCount = 0;
        foreach (var row in recipes)
        {
            if (row is null || !SafeKey(row.LocalRecipeKey) || !used.Add(row.LocalRecipeKey) ||
                row.ResultItemGid <= 0 || row.ResultQuantity is <= 0 or > ItemCraftMcpSnapshot.MaxQuantity ||
                row.Ingredients is null || row.Ingredients.Count > 64 ||
                row.Ingredients.Any(i => i is null || i.ItemId <= 0 ||
                    i.Quantity is <= 0 or > ItemCraftMcpSnapshot.MaxQuantity) ||
                row.RequiredLevel is < 1 or > 200 || row.JobId is <= 0 ||
                row.KamasPerCraft is < 0 || row.SecondsPerCraft is < 0)
                throw new ArgumentException("Malformed or duplicated local recipe.");
            ingredientCount = checked(ingredientCount + row.Ingredients.Count);
            if (ingredientCount > MaxCatalogIngredients)
                throw new ArgumentException("Too many ingredient entries.");
            all.Add(row with { Ingredients = Array.AsReadOnly(row.Ingredients.ToArray()) });
        }

        var sorted = all.OrderBy(x => x.LocalRecipeKey, StringComparer.Ordinal).ToArray();
        Recipes = Array.AsReadOnly(sorted);
        var byProduct = sorted.GroupBy(x => x.ResultItemGid).ToDictionary(
            g => g.Key, g => (IReadOnlyList<ItemCraftMcpRecipeRow>)Array.AsReadOnly(g.ToArray()));
        _byProduct = new ReadOnlyDictionary<long, IReadOnlyList<ItemCraftMcpRecipeRow>>(byProduct);
        _ephemeralIds = new ReadOnlyDictionary<string, long>(
            sorted.Select((r, i) => (r.LocalRecipeKey, LocalId: (long)i + 1))
                .ToDictionary(x => x.LocalRecipeKey, x => x.LocalId, StringComparer.Ordinal));
        _localKeys = new ReadOnlyDictionary<long, string>(
            sorted.Select((r, i) => (LocalId: (long)i + 1, r.LocalRecipeKey))
                .ToDictionary(x => x.LocalId, x => x.LocalRecipeKey));
    }

    public bool CanDisclose(ItemCraftMcpEvidence snapshotEvidence) =>
        Evidence.CanDisclose() &&
        string.Equals(Evidence.GameVersion, snapshotEvidence.GameVersion, StringComparison.Ordinal) &&
        string.Equals(Evidence.SourceKind, snapshotEvidence.SourceKind, StringComparison.Ordinal) &&
        string.Equals(Evidence.SourceId, snapshotEvidence.SourceId, StringComparison.Ordinal) &&
        string.Equals(Evidence.ManifestSha256, snapshotEvidence.ManifestSha256,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Evidence.AuthorizationEvidenceId, snapshotEvidence.AuthorizationEvidenceId,
            StringComparison.Ordinal) &&
        Recipes.All(r => string.Equals(r.SourceVersion, Evidence.GameVersion,
            StringComparison.Ordinal));

    public string? GetLocalKey(long ephemeralId) =>
        _localKeys.TryGetValue(ephemeralId, out var key) ? key : null;

    /// <summary>
    /// Returns false when graph limits are reached. Deterministic traversal
    /// includes ALL candidate recipes of any visited product, including unknown
    /// yields, and their complete ingredient dependencies.
    /// </summary>
    public bool TryClosure(IEnumerable<long> rootItemGids,
        out IReadOnlyList<CraftRecipe> closure)
    {
        closure = Array.Empty<CraftRecipe>();
        ArgumentNullException.ThrowIfNull(rootItemGids);
        var roots = rootItemGids.ToArray();
        if (roots.Length > ItemCraftMcpSnapshot.MaxPiecesPerSet ||
            roots.Any(id => id <= 0)) return false;

        var queue = new Queue<long>(roots.Distinct().OrderBy(x => x));
        var visited = new HashSet<long>();
        var selected = new List<CraftRecipe>();
        int queuedLinks = queue.Count;
        while (queue.Count > 0)
        {
            var gid = queue.Dequeue();
            if (!visited.Add(gid)) continue;
            if (visited.Count > MaxClosureItems) return false;
            if (!_byProduct.TryGetValue(gid, out var options)) continue;
            if (selected.Count + options.Count > MaxClosureRecipes)
                return false;
            foreach (var recipe in options)
            {
                var localId = _ephemeralIds[recipe.LocalRecipeKey];
                selected.Add(new CraftRecipe(localId, recipe.ResultItemGid,
                    recipe.ResultQuantity, recipe.Ingredients, recipe.SourceVersion,
                    recipe.Verified, recipe.JobId, recipe.RequiredLevel, recipe.KamasPerCraft,
                    recipe.SecondsPerCraft, recipe.RequiresWorkshop, recipe.WorkshopVerified));
                foreach (var ingredient in recipe.Ingredients)
                    if (!visited.Contains(ingredient.ItemId))
                    {
                        if (++queuedLinks > 50_000) return false;
                        queue.Enqueue(ingredient.ItemId);
                    }
            }
        }
        closure = Array.AsReadOnly(selected.OrderBy(x => x.RecipeId).ToArray());
        return true;
    }

    private static bool SafeKey(string? key)
    {
        if (key is not { Length: >= 1 and <= 80 } ||
            !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.'))
            return false;
        var normalized = new string(key.Where(char.IsAsciiLetterOrDigit).ToArray());
        return !normalized.Contains("dofusdb", StringComparison.OrdinalIgnoreCase);
    }
}
