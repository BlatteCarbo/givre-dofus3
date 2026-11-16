using GivreClone.App.Services.ItemCraftPlanner;
using CraftEngine = GivreClone.App.Services.ItemCraftPlanner.ItemCraftPlanner;

namespace GivreClone.App.Services.ItemCraftMcp;

/// <summary>
/// Strict read-only adapter to the canonical shared-ledger planner.
/// All game/source data originate in an admitted, defensively copied context.
/// </summary>
public static class ItemCraftMcpEngineAdapter
{
    public static ItemCraftMcpPlanResult? Compute(ItemCraftMcpSnapshot snapshot,
        ItemCraftMcpTarget target, long quantity)
    {
        if (!snapshot.CanDisclose() ||
            (snapshot.Context is null && snapshot.RecipeCatalog is null) ||
            quantity is <= 0 or > ItemCraftMcpSnapshot.MaxQuantity ||
            snapshot.Find(target.Kind, target.Id) is null)
            return null;

        var context = snapshot.Context;
        if (snapshot.RecipeCatalog is { } catalog)
        {
            var roots = target.Kind == ItemCraftMcpTargetKind.Item
                ? new[] { target.Id } : target.PieceGids.ToArray();
            // Never truncate a source graph because the engine accepts only
            // 4,000 candidate recipes. Missing a recipe can change every need.
            if (!catalog.TryClosure(roots, out var recipes)) return null;
            var approvedSets = snapshot.Sets.Values.Select(s => new EquipmentSetDefinition(
                s.Id, s.Name, s.PieceGids, snapshot.Evidence.GameVersion,
                s.Verified)).ToArray();
            context = context is null
                ? new ItemCraftContext(recipes, Array.Empty<ItemStockStack>(),
                    Array.Empty<AcquisitionSource>(), approvedSets,
                    new Dictionary<int, int>())
                : context with { Recipes = recipes, Sets = approvedSets };
        }
        if (context is null) return null;
        ItemCraftPlan plan = target.Kind == ItemCraftMcpTargetKind.Item
            ? CraftEngine.PlanItem(new ItemCraftTarget(target.Id, quantity), context)
            : CraftEngine.PlanSet(new EquipmentSetTarget(target.Id, target.Name,
                target.PieceGids, quantity), context);

        if (plan.Steps.Count > ItemCraftMcpSnapshot.MaxPlanRows)
            return null;

        // The underlying engine treats (JobId=null, RequiredLevel=null) as
        // a profession-free recipe. In a source import, the pair may instead
        // mean that the profession was NOT EXTRACTED. Fail closed on this
        // ambiguity while retaining the documented partial material graph.
        var unprovenJobRecipeKeys = snapshot.RecipeCatalog is null
            ? new HashSet<long>()
            : context.Recipes.Where(r => r.JobId is null || r.RequiredLevel is null)
                .Select(r => r.RecipeId).ToHashSet();
        bool unprovenJobGate = plan.Steps.Any(s => s.RecipeId is { } id &&
            unprovenJobRecipeKeys.Contains(id));
        var steps = plan.Steps.Select(s =>
        {
            string? localKey = s.RecipeId is { } id
                ? snapshot.RecipeCatalog?.GetLocalKey(id) : null;
            // An integer generated locally for a UUID must never appear as
            // an official Dofus RecipeId in MCP response JSON.
            long? officialId = snapshot.RecipeCatalog is null ? s.RecipeId : null;
            return new ItemCraftMcpStep(
                s.Id, s.ItemId, s.Quantity, Method(s.Kind), officialId,
                s.Dependencies.ToArray(),
                s.Verified && (s.RecipeId is not { } rid || !unprovenJobRecipeKeys.Contains(rid)),
                localKey ?? SourceId(context, s), localKey);
        }).ToArray();
        var leafSteps = plan.Steps.Where(s => s.Kind is
            ItemCraftStepKind.UseInventory or ItemCraftStepKind.Gather or
            ItemCraftStepKind.Drop or ItemCraftStepKind.Buy or ItemCraftStepKind.Unresolved);
        var materials = leafSteps.GroupBy(s => s.ItemId).OrderBy(g => g.Key)
            .Select(group =>
            {
                var all = group.ToArray();
                var used = all.Where(s => s.Kind == ItemCraftStepKind.UseInventory).ToArray();
                var unowned = all.Where(s => s.Kind != ItemCraftStepKind.UseInventory).ToArray();
                bool verified = all.All(s => s.Verified && s.Kind != ItemCraftStepKind.Unresolved);
                var methods = unowned.Select(s => Method(s.Kind)).Distinct(StringComparer.Ordinal).ToArray();
                var sourceIds = all.Select(s => SourceId(context, s))
                    .Where(s => s is not null).Distinct(StringComparer.Ordinal).Take(2).ToArray();
                return new ItemCraftMcpMaterial(group.Key,
                    SumOrNull(all.Select(s => s.Quantity)),
                    snapshot.InventoryVerified ? SumOrNull(used.Select(s => s.Quantity)) : null,
                    snapshot.InventoryVerified ? SumOrNull(unowned.Select(s => s.Quantity)) : null,
                    methods.Length == 1 ? methods[0] : methods.Length == 0 ? "STOCK" : "UNKNOWN",
                    verified, sourceIds.Length == 1 ? sourceIds[0] : null);
            }).ToArray();
        if (materials.Length > ItemCraftMcpSnapshot.MaxPlanRows)
            return null;
        var warnings = plan.Warnings.Take(64)
            .Select(w => new string(w.Where(c => !char.IsControl(c)).Take(256).ToArray()))
            .ToArray();
        return new ItemCraftMcpPlanResult(
            target.Kind, target.Id, quantity, plan.IsComplete && !unprovenJobGate,
            materials, steps, warnings, snapshot.Evidence,
            InventoryVerified: snapshot.InventoryVerified, RuntimeCertified: false);
    }

    private static long? SumOrNull(IEnumerable<long?> quantities)
    {
        long sum = 0;
        foreach (var qty in quantities)
        {
            if (qty is null) return null;
            sum = checked(sum + qty.Value);
        }
        return sum;
    }

    private static string? SourceId(ItemCraftContext context, ItemCraftStep step)
    {
        if (step.Kind == ItemCraftStepKind.CraftAtWorkshop && step.RecipeId is { } recipeId)
            return "recipe:" + recipeId;
        if (step.Kind is ItemCraftStepKind.UseInventory or ItemCraftStepKind.UsePlannedSurplus)
            return null;
        ItemAcquisitionKind? acquisitionKind = step.Kind switch
        {
            ItemCraftStepKind.Gather => ItemAcquisitionKind.Gather,
            ItemCraftStepKind.Drop => ItemAcquisitionKind.Drop,
            ItemCraftStepKind.Buy => ItemAcquisitionKind.Buy,
            _ => null
        };
        if (acquisitionKind is null) return null;
        var matches = context.Sources.Where(s => s.ItemId == step.ItemId &&
            s.Kind == acquisitionKind.Value && s.Verified).Take(2).ToArray();
        return matches.Length == 1 ? matches[0].SourceId : null;
    }

    private static string Method(ItemCraftStepKind kind) => kind switch
    {
        ItemCraftStepKind.UseInventory or ItemCraftStepKind.UsePlannedSurplus => "STOCK",
        ItemCraftStepKind.CraftAtWorkshop => "CRAFT",
        ItemCraftStepKind.Gather => "GATHER",
        ItemCraftStepKind.Drop => "DROP",
        ItemCraftStepKind.Buy => "BUY",
        ItemCraftStepKind.Unresolved => "UNRESOLVED",
        _ => "UNKNOWN"
    };
}
