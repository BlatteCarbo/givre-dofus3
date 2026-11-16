using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.LotM;

public sealed record PlannedCraft(RecipeInfo Recipe, int Count);
public sealed record CraftNeed(long Gid, int Quantity);
public sealed record CraftPlan(IReadOnlyList<PlannedCraft> Crafts, IReadOnlyList<CraftNeed> Materials,
                               IReadOnlyList<CraftNeed> AlreadyOwned);

public static class CraftPlanner
{
    public static CraftPlan Build(GameDb db, CraftNeed target, Func<long, int> owned)
    {
        if (target.Quantity < 1) throw new ArgumentOutOfRangeException(nameof(target));
        var remaining = new Dictionary<long, int>();
        var consumed = new Dictionary<long, int>();
        var materials = new Dictionary<long, int>();
        var crafts = new Dictionary<long, PlannedCraft>();
        var path = new HashSet<long>();
        int visits = 0;
        void Need(long gid, int quantity)
        {
            if (++visits > 10000 || path.Count >= 64) throw new InvalidOperationException("recipe graph limit exceeded");
            if (!remaining.TryGetValue(gid, out int stock)) remaining[gid] = stock = Math.Max(0, owned(gid));
            int used = Math.Min(stock, quantity);
            remaining[gid] -= used;
            consumed[gid] = checked(consumed.GetValueOrDefault(gid) + used);
            quantity -= used;
            if (quantity == 0) return;
            if (!path.Add(gid)) throw new InvalidOperationException("cyclic recipe graph");
            try
            {
                if (db.Recipe(gid) is not { } recipe || recipe.Ingredients.Count == 0)
                {
                    materials[gid] = checked(materials.GetValueOrDefault(gid) + quantity);
                    return;
                }
                foreach (var ingredient in recipe.Ingredients)
                {
                    if (ingredient.Quantity < 1) throw new InvalidOperationException("invalid ingredient quantity");
                    Need(ingredient.Gid, checked(quantity * ingredient.Quantity));
                }
                crafts[gid] = new PlannedCraft(recipe, checked((crafts.GetValueOrDefault(gid)?.Count ?? 0) + quantity));
            }
            finally { path.Remove(gid); }
        }
        Need(target.Gid, target.Quantity);
        var ordered = new List<PlannedCraft>();
        var emitted = new HashSet<long>();
        void Emit(long gid)
        {
            if (!crafts.TryGetValue(gid, out var craft) || !emitted.Add(gid)) return;
            foreach (var ingredient in craft.Recipe.Ingredients) Emit(ingredient.Gid);
            ordered.Add(craft);
        }
        Emit(target.Gid);
        return new CraftPlan(ordered, materials.Select(p => new CraftNeed(p.Key, p.Value)).ToArray(),
            consumed.Where(p => p.Value > 0).Select(p => new CraftNeed(p.Key, p.Value)).ToArray());
    }
}
