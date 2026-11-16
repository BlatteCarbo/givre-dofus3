using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.ItemCraftPlanner;

/// <summary>One requested item, not necessarily an equipable item or profession product.</summary>
public sealed record ItemCraftTarget(long ItemId, long Quantity);

/// <summary>Quantity is the number of sets. ItemIds is an optional USER-SUPPLIED list,
/// never proof of actual set membership without a verified catalog entry.</summary>
public sealed record EquipmentSetTarget(long SetId, string Name, IReadOnlyList<long>? ItemIds = null,
    long Quantity = 1);

public sealed record EquipmentSetDefinition(long SetId, string Name, IReadOnlyList<long> ItemIds,
    string SourceVersion, bool Verified);
public sealed record CraftIngredient(long ItemId, long? Quantity);
public sealed record CraftRecipe(long RecipeId, long ResultItemId, long? ResultQuantity,
    IReadOnlyList<CraftIngredient> Ingredients, string SourceVersion, bool Verified,
    int? JobId = null, int? RequiredLevel = null,
    decimal? KamasPerCraft = null, long? SecondsPerCraft = null,
    bool RequiresWorkshop = false, bool WorkshopVerified = false);
public sealed record ItemStockStack(string StackId, long ItemId, long Quantity,
    string Container, bool Verified = true);
public enum ItemAcquisitionKind { Craft, Gather, Drop, Buy }
public sealed record AcquisitionSource(long ItemId, ItemAcquisitionKind Kind, string SourceId,
    string SourceVersion, bool Verified, decimal? UnitKamas = null, long? UnitSeconds = null,
    long? MaxUnits = null);
public sealed record CraftOptimizationWeights(decimal Kamas = 1, decimal Time = 0);

/// <summary>All external data must be supplied by a source-aware integration. No network or game access.</summary>
public sealed record ItemCraftContext(
    IReadOnlyList<CraftRecipe> Recipes,
    IReadOnlyList<ItemStockStack> Inventory,
    IReadOnlyList<AcquisitionSource> Sources,
    IReadOnlyList<EquipmentSetDefinition> Sets,
    IReadOnlyDictionary<int, int> KnownJobLevels,
    IReadOnlyDictionary<long, long>? RecipeChoices = null,
    IReadOnlyDictionary<long, ItemAcquisitionKind>? MethodChoices = null,
    CraftOptimizationWeights? Weights = null);

public enum ItemCraftStepKind { UseInventory, UsePlannedSurplus, CraftAtWorkshop, Gather, Drop, Buy, Unresolved }
public sealed record ItemCraftAlternative(ItemAcquisitionKind Kind, long? RecipeId, string SourceId,
    string Version, bool Verified, decimal? UnitKamas, long? UnitSeconds);
public sealed record ItemCraftStep(
    int Id, ItemCraftStepKind Kind, long ItemId, long? Quantity, long? RecipeId,
    string Explanation, IReadOnlyList<int> Dependencies, bool Verified,
    string SourceVersion, decimal? Kamas, long? Seconds,
    IReadOnlyList<ItemCraftAlternative> Alternatives);
public sealed record ItemCraftResourceNeed(long ItemId, ItemCraftStepKind Method, long Quantity);
public sealed record ItemCraftTargetSummary(ItemCraftTarget Target, IReadOnlyList<int> RootStepIds,
    bool EvidenceComplete);

/// <summary>Missing[itemId]=null means unquantifiable missing amount. The plan is NEVER a receipt.</summary>
public sealed record ItemCraftPlan(
    IReadOnlyList<ItemCraftTarget> Targets, IReadOnlyList<ItemCraftTargetSummary> TargetSummaries,
    IReadOnlyList<ItemCraftStep> Steps, IReadOnlyDictionary<long, long> UsedStock,
    IReadOnlyDictionary<long, long> RemainingStock, IReadOnlyDictionary<long, long> PlannedSurplus,
    IReadOnlyDictionary<long, long?> Missing, IReadOnlyList<string> Warnings,
    decimal KnownKamasSubtotal, decimal? FullKamasEstimate,
    long KnownSecondsSubtotal, long? FullSecondsEstimate,
    bool SetMembershipVerified, bool IsComplete, bool Executed, bool Acknowledged)
{
    public bool RequiresReview => !IsComplete || Warnings.Count > 0;
    /// <summary>Rolls up repeated leaf actions from every target into ONE per item/method.
    /// Consultative quantities, never a claim that any resources were collected.</summary>
    public IReadOnlyList<ItemCraftResourceNeed> ResourceNeeds => Steps
        .Where(s => s.Quantity is > 0 && s.Kind is ItemCraftStepKind.Gather
            or ItemCraftStepKind.Drop or ItemCraftStepKind.Buy)
        .GroupBy(s => (s.ItemId, s.Kind)).OrderBy(g => g.Key.ItemId).ThenBy(g => g.Key.Kind)
        .Select(g => new ItemCraftResourceNeed(g.Key.ItemId, g.Key.Kind,
            g.Aggregate(0L, (total, step) => checked(total + step.Quantity!.Value))))
        .ToArray();
}

/// <summary>Deterministic bounded BOM expansion with a single shared inventory and surplus ledger.
/// No hard-coded Dofus recipes, set members, prices, XP, drop rates or maps.</summary>
public static class ItemCraftPlanner
{
    public const int MaxDepth = 32;
    public const int MaxSteps = 4096;
    public const long MaxQuantity = 1_000_000_000;

    public static ItemCraftPlan PlanItem(ItemCraftTarget target, ItemCraftContext context) =>
        PlanList(new[] { target }, context);

    public static ItemCraftPlan PlanList(IEnumerable<ItemCraftTarget> targets, ItemCraftContext context)
    {
        var normalized = Normalize(targets);
        return new Context(context).Run(normalized, true);
    }

    public static ItemCraftPlan PlanSet(EquipmentSetTarget set, ItemCraftContext context)
    {
        ArgumentNullException.ThrowIfNull(set);
        ValidateContext(context);
        if (set.SetId <= 0 || set.Quantity <= 0 || set.Quantity > MaxQuantity ||
            string.IsNullOrWhiteSpace(set.Name))
            throw new ArgumentException("Set identifier, name or quantity invalid.", nameof(set));
        var known = context.Sets.SingleOrDefault(x => x.SetId == set.SetId);
        bool knownVerified = known is { Verified: true, SourceVersion.Length: > 0 };
        bool sameMembership = known is not null && (set.ItemIds is null ||
            set.ItemIds.SequenceEqual(known.ItemIds));
        // If user overrides a catalog set, treat the whole list as an unverified manual definition.
        var members = set.ItemIds ?? known?.ItemIds;
        if (members is null || members.Count == 0)
        {
            var empty = new Context(context).Run(Array.Empty<ItemCraftTarget>(), false);
            return empty with
            {
                Warnings = new[] { "Set membership unknown: supply an explicit member list or a versioned catalog." },
                IsComplete = false
            };
        }
        if (members.Count > 128 || members.Any(x => x <= 0))
            throw new ArgumentException("Invalid set members or more than 128 pieces.", nameof(set));
        var targets = Normalize(members.Select(id => new ItemCraftTarget(id, set.Quantity)));
        var plan = new Context(context).Run(targets, knownVerified && sameMembership);
        if (knownVerified && sameMembership) return plan;
        return plan with
        {
            Warnings = plan.Warnings.Concat(new[]
            {
                "Equipment set membership UNVERIFIED; manual members are not an authenticated Dofus set."
            }).ToArray(),
            IsComplete = false
        };
    }

    private static IReadOnlyList<ItemCraftTarget> Normalize(IEnumerable<ItemCraftTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var array = targets.ToArray();
        if (array.Length > 128) throw new ArgumentException("At most 128 target lines.", nameof(targets));
        if (array.Any(x => x is null || x.ItemId <= 0 || x.Quantity <= 0 || x.Quantity > MaxQuantity))
            throw new ArgumentException("Invalid target item or quantity.", nameof(targets));
        return array.GroupBy(x => x.ItemId).OrderBy(x => x.Key)
            .Select(g => new ItemCraftTarget(g.Key, Limit(g.Aggregate(0L, (sum, x) => checked(sum + x.Quantity)))))
            .ToArray();
    }
    private static long Limit(long value) => value is >= 0 and <= MaxQuantity ? value
        : throw new ArgumentOutOfRangeException(nameof(value), "Planning quantity limit reached.");

    private static void ValidateContext(ItemCraftContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(c.Recipes);
        ArgumentNullException.ThrowIfNull(c.Inventory);
        ArgumentNullException.ThrowIfNull(c.Sources);
        ArgumentNullException.ThrowIfNull(c.Sets);
        ArgumentNullException.ThrowIfNull(c.KnownJobLevels);
        if (c.Recipes.Count > 4000 || c.Inventory.Count > 20000 || c.Sources.Count > 10000 ||
            c.Sets.Count > 1000)
            throw new ArgumentException("Input catalog exceeds safety limits.", nameof(c));
        var weights = c.Weights ?? new CraftOptimizationWeights();
        if (weights.Kamas < 0 || weights.Time < 0 || weights.Kamas + weights.Time <= 0 ||
            weights.Kamas > 1_000_000 || weights.Time > 1_000_000)
            throw new ArgumentException("Invalid optimization weights.", nameof(c));
        var stacks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in c.Inventory)
            if (s is null || string.IsNullOrWhiteSpace(s.StackId) || !stacks.Add(s.StackId) ||
                s.ItemId <= 0 || s.Quantity < 0 || s.Quantity > MaxQuantity)
                throw new ArgumentException("Invalid or duplicate inventory stack.", nameof(c));
        foreach (var r in c.Recipes)
            if (r is null || r.RecipeId <= 0 || r.ResultItemId <= 0 ||
                r.ResultQuantity is <= 0 or > MaxQuantity || r.Ingredients is null ||
                r.Ingredients.Count > 64 || r.Ingredients.Any(i => i is null || i.ItemId <= 0 ||
                    i.Quantity is <= 0 or > MaxQuantity) || r.RequiredLevel is < 1 or > 200 ||
                r.JobId is <= 0 || r.KamasPerCraft is < 0 || r.SecondsPerCraft is < 0)
                throw new ArgumentException("Invalid recipe or ingredient quantity.", nameof(c));
        if (c.Recipes.Select(x => x.RecipeId).Distinct().Count() != c.Recipes.Count)
            throw new ArgumentException("Duplicate recipe ID.", nameof(c));
        foreach (var s in c.Sources)
            if (s is null || s.ItemId <= 0 || s.Kind == ItemAcquisitionKind.Craft ||
                !Enum.IsDefined(s.Kind) || string.IsNullOrWhiteSpace(s.SourceId) ||
                s.MaxUnits is <= 0 or > MaxQuantity || s.UnitKamas is < 0 || s.UnitSeconds is < 0)
                throw new ArgumentException("Invalid source.", nameof(c));
        if (c.Sources.Select(x => x.SourceId).Distinct(StringComparer.Ordinal).Count() != c.Sources.Count)
            throw new ArgumentException("Duplicate acquisition source identity.", nameof(c));
        if (c.Sets.Any(x => x is null || x.SetId <= 0 || x.ItemIds is null ||
            x.ItemIds.Count > 128 || x.ItemIds.Any(id => id <= 0)) ||
            c.Sets.Select(x => x.SetId).Distinct().Count() != c.Sets.Count)
            throw new ArgumentException("Invalid set catalog.", nameof(c));
        if (c.KnownJobLevels.Any(x => x.Key <= 0 || x.Value is < 1 or > 200))
            throw new ArgumentException("Invalid job-level evidence.", nameof(c));
        if (c.RecipeChoices?.Any(x => x.Key <= 0 || x.Value <= 0) == true ||
            c.MethodChoices?.Any(x => x.Key <= 0 || !Enum.IsDefined(x.Value)) == true)
            throw new ArgumentException("Invalid explicit choice.", nameof(c));
    }

    private sealed class Context
    {
        private sealed class Layer(long count, int? producer)
        {
            public long Remaining = count;
            public int? Producer = producer;
        }
        private readonly ItemCraftContext _ctx;
        private readonly SortedDictionary<long, List<Layer>> _available = new();
        private readonly SortedDictionary<long, long> _used = new();
        private readonly SortedDictionary<long, long> _surplus = new();
        private readonly SortedDictionary<long, long?> _missing = new();
        private readonly SortedDictionary<string, long> _sourceUsed = new(StringComparer.Ordinal);
        private readonly List<ItemCraftStep> _steps = new();
        private readonly List<string> _warnings = new();
        private readonly Dictionary<long, CraftRecipe[]> _recipes;
        private readonly Dictionary<long, AcquisitionSource[]> _sources;
        private static readonly ItemCraftAlternative[] EmptyOptions = Array.Empty<ItemCraftAlternative>();

        public Context(ItemCraftContext ctx)
        {
            ValidateContext(ctx);
            _ctx = ctx;
            foreach (var s in ctx.Inventory.Where(x => x.Verified).OrderBy(x => x.ItemId)
                         .ThenBy(x => x.Container, StringComparer.Ordinal).ThenBy(x => x.StackId, StringComparer.Ordinal))
                Pool(s.ItemId).Add(new Layer(s.Quantity, null));
            foreach (var s in ctx.Inventory.Where(x => !x.Verified))
                Warn($"Inventory stack {s.StackId} is not verified; not available for planning.");
            _recipes = ctx.Recipes.GroupBy(x => x.ResultItemId)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.RecipeId).ToArray());
            _sources = ctx.Sources.GroupBy(x => x.ItemId)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Kind).ThenBy(x => x.SourceId, StringComparer.Ordinal).ToArray());
        }

        private List<Layer> Pool(long id)
        {
            if (!_available.TryGetValue(id, out var list)) _available[id] = list = new();
            return list;
        }

        private void Warn(string message)
        {
            if (!_warnings.Contains(message, StringComparer.Ordinal)) _warnings.Add(message);
        }

        private static bool Proven(CraftRecipe r) => r.Verified && !string.IsNullOrWhiteSpace(r.SourceVersion);
        private static bool Proven(AcquisitionSource s) => s.Verified && !string.IsNullOrWhiteSpace(s.SourceVersion);

        private IReadOnlyList<ItemCraftAlternative> Alternatives(CraftRecipe[] recipes, AcquisitionSource[] sources) =>
            recipes.Select(r => new ItemCraftAlternative(ItemAcquisitionKind.Craft, r.RecipeId,
                    $"recipe:{r.RecipeId}", r.SourceVersion, Proven(r), r.KamasPerCraft, r.SecondsPerCraft))
                .Concat(sources.Select(s => new ItemCraftAlternative(s.Kind, null, s.SourceId,
                    s.SourceVersion, Proven(s), s.UnitKamas, s.UnitSeconds))).ToArray();

        private int Add(ItemCraftStepKind kind, long id, long? qty, long? recipe, string explanation,
            IEnumerable<int>? deps, bool verified, string version, decimal? kamas, long? seconds,
            IReadOnlyList<ItemCraftAlternative>? options = null)
        {
            if (_steps.Count >= MaxSteps) throw new ArgumentException("Plan exceeds 4096 nodes.");
            int number = _steps.Count + 1;
            _steps.Add(new(number, kind, id, qty, recipe, explanation,
                (deps ?? Array.Empty<int>()).ToArray(), verified, version, kamas, seconds,
                options ?? EmptyOptions));
            return number;
        }

        private int Block(long id, long? count, string reason,
            IReadOnlyList<ItemCraftAlternative>? options = null)
        {
            Warn(reason);
            if (_missing.TryGetValue(id, out var previous))
                _missing[id] = previous is null || count is null ? null : Limit(checked(previous.Value + count.Value));
            else _missing[id] = count;
            return Add(ItemCraftStepKind.Unresolved, id, count, null, reason, null, false, "", null, null, options);
        }

        public ItemCraftPlan Run(IReadOnlyList<ItemCraftTarget> targets, bool setVerified)
        {
            var groups = new List<ItemCraftTargetSummary>();
            foreach (var target in targets)
            {
                var roots = Expand(target.ItemId, target.Quantity, new HashSet<long>(), 0);
                groups.Add(new(target, roots, roots.All(x => _steps[x - 1].Verified)));
            }
            decimal k = 0;
            long seconds = 0;
            bool allMoney = true, allTime = true;
            foreach (var step in _steps)
            {
                if (step.Kamas is { } knownKamas) k = checked(k + knownKamas);
                else allMoney = false;
                if (step.Seconds is { } knownSeconds) seconds = checked(seconds + knownSeconds);
                else allTime = false;
            }
            var remaining = new SortedDictionary<long, long>();
            foreach (var (id, layers) in _available)
            {
                long amount = 0;
                foreach (var layer in layers.Where(l => l.Producer is null))
                    amount = checked(amount + layer.Remaining);
                if (amount > 0) remaining[id] = amount;
            }
            bool fullyVerified = setVerified && _missing.Count == 0 && _steps.All(s => s.Verified);
            return new(targets, groups.AsReadOnly(), _steps.AsReadOnly(),
                new SortedDictionary<long, long>(_used), remaining, new SortedDictionary<long, long>(_surplus),
                new SortedDictionary<long, long?>(_missing), _warnings.AsReadOnly(), k,
                fullyVerified && allMoney ? k : null, seconds, fullyVerified && allTime ? seconds : null,
                setVerified, fullyVerified && _warnings.Count == 0, false, false);
        }

        private IReadOnlyList<int> Expand(long id, long qty, HashSet<long> ancestry, int depth)
        {
            Limit(qty);
            var deps = new List<int>();
            long unmet = qty;
            foreach (var layer in Pool(id))
            {
                if (unmet == 0) break;
                long use = Math.Min(unmet, layer.Remaining);
                if (use <= 0) continue;
                layer.Remaining -= use;
                unmet -= use;
                if (layer.Producer is { } producer)
                {
                    if (_surplus.TryGetValue(id, out long remainder)) _surplus[id] = remainder - use;
                    deps.Add(Add(ItemCraftStepKind.UsePlannedSurplus, id, use, null,
                        "Reuse a previous planned craft's projected excess (NOT observed inventory).",
                        new[] { producer }, true, "", 0, 0));
                }
                else
                {
                    _used.TryGetValue(id, out long previous);
                    _used[id] = checked(previous + use);
                    deps.Add(Add(ItemCraftStepKind.UseInventory, id, use, null,
                        "Reserve once from confirmed shared inventory.", null, true, "", 0, 0));
                }
            }
            if (unmet == 0) return deps;
            if (depth >= MaxDepth || ancestry.Contains(id))
            {
                deps.Add(Block(id, unmet, $"Cycle/depth limit while expanding item {id}."));
                return deps;
            }
            var recipes = _recipes.GetValueOrDefault(id) ?? Array.Empty<CraftRecipe>();
            var sources = _sources.GetValueOrDefault(id) ?? Array.Empty<AcquisitionSource>();
            var alternatives = Alternatives(recipes, sources);
            CraftRecipe? recipe = null;
            AcquisitionSource? source = null;
            if (_ctx.RecipeChoices?.TryGetValue(id, out long recipeId) == true)
                recipe = recipes.SingleOrDefault(r => r.RecipeId == recipeId && Proven(r));
            else if (_ctx.MethodChoices?.TryGetValue(id, out var kind) == true)
            {
                if (kind == ItemAcquisitionKind.Craft)
                {
                    if (recipes.Length == 1 && Proven(recipes[0])) recipe = recipes[0];
                }
                else source = Choose(sources.Where(s => s.Kind == kind).ToArray(), unmet);
            }
            else if (recipes.Length == 1 && Proven(recipes[0]) && sources.Length == 0)
                recipe = recipes[0];
            else if (recipes.Length == 0)
                source = Choose(sources, unmet);

            if (recipe is not null)
                deps.Add(Craft(id, unmet, recipe, ancestry, depth + 1, alternatives));
            else if (source is not null)
                deps.Add(Acquire(id, unmet, source, alternatives));
            else deps.Add(Block(id, unmet, alternatives.Count == 0
                ? $"No verified recipe or gathering/drop/HDV source for item {id}."
                : $"Multiple or unverified/incomparable acquisition methods for item {id}; review required.",
                alternatives));
            return deps;
        }

        private AcquisitionSource? Choose(AcquisitionSource[] sources, long qty)
        {
            if (sources.Length == 0) return null;
            if (sources.Length == 1) return Proven(sources[0]) ? sources[0] : null;
            if (sources.Any(s => !Proven(s) || s.UnitKamas is null ||
                s.UnitSeconds is null || RemainingSource(s) < qty)) return null;
            var weights = _ctx.Weights ?? new CraftOptimizationWeights();
            return sources.OrderBy(s => checked(s.UnitKamas!.Value * weights.Kamas +
                                               s.UnitSeconds!.Value * weights.Time))
                .ThenBy(s => s.Kind).ThenBy(s => s.SourceId, StringComparer.Ordinal).First();
        }

        private long RemainingSource(AcquisitionSource source)
        {
            if (source.MaxUnits is null) return MaxQuantity;
            _sourceUsed.TryGetValue(source.SourceId, out long consumed);
            return Math.Max(0, source.MaxUnits.Value - consumed);
        }

        private int Acquire(long id, long qty, AcquisitionSource source,
            IReadOnlyList<ItemCraftAlternative> options)
        {
            long accepted = Math.Min(qty, RemainingSource(source));
            if (accepted == 0)
                return Block(id, qty, $"Verified source {source.SourceId} exhausted for item {id}.", options);
            var kind = source.Kind switch
            {
                ItemAcquisitionKind.Gather => ItemCraftStepKind.Gather,
                ItemAcquisitionKind.Drop => ItemCraftStepKind.Drop,
                _ => ItemCraftStepKind.Buy
            };
            if (accepted > 0)
            {
                _sourceUsed.TryGetValue(source.SourceId, out long used);
                _sourceUsed[source.SourceId] = checked(used + accepted);
            }
            int step = Add(kind, id, accepted, null,
                "Consultative source only; no drop chance, route, order or acquisition acknowledgment.",
                null, Proven(source) && accepted == qty, source.SourceVersion,
                source.UnitKamas is { } price ? checked(price * accepted) : null,
                source.UnitSeconds is { } time ? checked(time * accepted) : null, options);
            if (accepted < qty)
                Block(id, qty - accepted, $"Verified source capacity exceeded for item {id}.", options);
            return step;
        }

        private int Craft(long id, long qty, CraftRecipe recipe, HashSet<long> ancestry,
            int depth, IReadOnlyList<ItemCraftAlternative> options)
        {
            if (recipe.ResultQuantity is not { } yield)
            {
                // The source still proves the *per-craft* ingredient list, but
                // not how many runs are needed for the requested output. Show
                // those ingredients without reserving stock, multiplying
                // amounts or claiming any craftable quantities.
                string reason = $"Recipe {recipe.RecipeId} has unknown output quantity; " +
                    "total ingredients and craft count are UNKNOWN.";
                Warn(reason);
                if (_missing.TryGetValue(id, out var previous))
                    _missing[id] = previous is null ? null : Limit(checked(previous.Value + qty));
                else _missing[id] = qty;
                var unknownChildren = recipe.Ingredients.OrderBy(x => x.ItemId)
                    .Select(part => Block(part.ItemId, null,
                        $"Ingredient {part.ItemId}: " +
                        (part.Quantity is { } amount ? $"{amount} per craft" : "amount per craft unknown") +
                        "; number of crafts UNKNOWN.")).ToArray();
                return Add(ItemCraftStepKind.CraftAtWorkshop, id, null, recipe.RecipeId,
                    reason + " Per-craft ingredients are shown for reference only.",
                    unknownChildren, false, recipe.SourceVersion, null, null, options);
            }
            long runs = checked(qty / yield + (qty % yield == 0 ? 0 : 1));
            Limit(runs);
            long produced = Limit(checked(runs * yield));
            if (depth >= MaxDepth || ancestry.Contains(id))
                return Block(id, qty, $"Cycle/depth limit in recipe {recipe.RecipeId}.", options);
            var chain = new HashSet<long>(ancestry) { id };
            var children = new List<int>();
            bool unknownQty = false;
            foreach (var part in recipe.Ingredients.OrderBy(x => x.ItemId))
            {
                if (part.Quantity is not { } ingredientQty)
                {
                    children.Add(Block(part.ItemId, null, $"Unknown amount of ingredient {part.ItemId} in recipe {recipe.RecipeId}."));
                    unknownQty = true;
                    continue;
                }
                children.AddRange(Expand(part.ItemId, Limit(checked(ingredientQty * runs)), chain, depth + 1));
            }
            bool jobKnown = recipe.JobId is null && recipe.RequiredLevel is null ||
                recipe.JobId is { } job && recipe.RequiredLevel is { } required &&
                _ctx.KnownJobLevels.TryGetValue(job, out int level) && level >= required;
            if (!jobKnown) Warn($"Required profession level not verified for recipe {recipe.RecipeId}.");
            bool workshopKnown = !recipe.RequiresWorkshop || recipe.WorkshopVerified;
            if (!workshopKnown) Warn($"Workshop/atelier unavailable or not verified for recipe {recipe.RecipeId}.");
            bool verified = Proven(recipe) && jobKnown && workshopKnown && !unknownQty &&
                children.All(x => _steps[x - 1].Verified);
            int craft = Add(ItemCraftStepKind.CraftAtWorkshop, id, produced, recipe.RecipeId,
                $"PROPOSED {runs} craft(s), yield {yield}; not an executed craft or ACK.",
                children, verified, recipe.SourceVersion,
                recipe.KamasPerCraft is { } fee ? checked(fee * runs) : null,
                recipe.SecondsPerCraft is { } seconds ? checked(seconds * runs) : null, options);
            if (verified && produced > qty)
            {
                long excess = produced - qty;
                Pool(id).Add(new Layer(excess, craft));
                _surplus.TryGetValue(id, out long prior);
                _surplus[id] = checked(prior + excess);
            }
            if (!verified)
            {
                if (!_missing.ContainsKey(id)) _missing[id] = qty;
                Warn($"Craft of item {id} is not fully substantiated; projected items are NOT credited to stock.");
            }
            return craft;
        }
    }
}
