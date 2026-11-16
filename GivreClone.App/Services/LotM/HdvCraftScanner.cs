using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace GivreClone.App.Services.LotM;

public enum CraftPriceSource { Unknown, CachedMarketUnit, CachedAverage }
public sealed record CraftPrice(decimal? UnitPrice, CraftPriceSource Source, TimeSpan? Age)
{
    public bool Known => UnitPrice is > 0 && Source != CraftPriceSource.Unknown;
    public string Evidence => !Known ? "prix inconnu" :
        $"{(Source == CraftPriceSource.CachedMarketUnit ? "marché local x1" : "moyenne locale")}, " +
        (Age is { } age && age >= TimeSpan.Zero ? $"âge {age.TotalSeconds:F0}s" : "fraîcheur inconnue");
}
public sealed record CraftScanIngredient(long Gid, int Quantity);
public sealed record CraftScanRecipe(long Gid, string Name, int Level, int JobId,
    IReadOnlyList<CraftScanIngredient> Ingredients);
public sealed record CraftScanEstimate(CraftScanRecipe Recipe, decimal? Cost, decimal? Value,
    IReadOnlyList<long> UnknownGids, string Evidence)
{
    public decimal? Gain => Value - Cost;
    public decimal? GainPercent => Cost is > 0 ? Gain * 100m / Cost : null;
}

/// <summary>Pure estimates for one output per craft, before taxes, fees and lot rounding. Never requests quotes.</summary>
public static class HdvCraftScanner
{
    public static CraftPrice SelectPrice(long marketUnit, long marketAgeMs, long average)
        => marketUnit > 0
            ? new(marketUnit, CraftPriceSource.CachedMarketUnit,
                marketAgeMs >= 0 && marketAgeMs != long.MaxValue ? TimeSpan.FromMilliseconds(marketAgeMs) : null)
            : average > 0 ? new(average, CraftPriceSource.CachedAverage, null)
            : new(null, CraftPriceSource.Unknown, null);

    public const int MaxDecomposeDepth = 4;

    /// <param name="subRecipes">Recettes par objet produit : si fourni, un ingrédient coûte le MOINS CHER entre son prix HDV et sa fabrication
    /// (récursif, profondeur bornée, sans cycle). Null = ingrédients directs au prix HDV.</param>
    /// <param name="saleTaxPercent">Taxe de mise en vente (%) retirée de la valeur de revente ; 0 = ignorée.</param>
    public static IReadOnlyList<CraftScanEstimate> Estimate(IEnumerable<CraftScanRecipe> recipes,
        IReadOnlyDictionary<long, CraftPrice> prices, int quantity, CancellationToken ct = default,
        IReadOnlyDictionary<long, CraftScanRecipe>? subRecipes = null, decimal saleTaxPercent = 0)
    {
        if (saleTaxPercent is < 0 or >= 100) throw new ArgumentOutOfRangeException(nameof(saleTaxPercent));
        ct.ThrowIfCancellationRequested();
        if (quantity < 1 || quantity > 1000) throw new ArgumentOutOfRangeException(nameof(quantity));
        var results = new List<CraftScanEstimate>();
        foreach (var recipe in recipes)
        {
            ct.ThrowIfCancellationRequested();
            var unknown = new HashSet<long>();
            var evidence = new List<string>();
            CraftPrice Price(long gid)
            {
                ct.ThrowIfCancellationRequested();
                var p = prices.TryGetValue(gid, out var found) ? found : new(null, CraftPriceSource.Unknown, null);
                if (!p.Known) unknown.Add(gid);
                evidence.Add($"{gid}: {p.Evidence}");
                return p;
            }
            var output = Price(recipe.Gid);
            decimal cost = 0;
            bool valid = recipe.Gid > 0 && recipe.Ingredients.Count > 0;
            bool allKnown = true;
            foreach (var ingredient in recipe.Ingredients)
            {
                if (ingredient.Gid <= 0 || ingredient.Quantity <= 0) { valid = false; continue; }
                var price = Price(ingredient.Gid);
                decimal? unit = price.Known ? price.UnitPrice : null;
                if (subRecipes is not null && subRecipes.ContainsKey(ingredient.Gid))
                {
                    var made = CraftCost(ingredient.Gid, prices, subRecipes, new HashSet<long> { recipe.Gid }, 1, ct);
                    if (made is { } m && (unit is null || m < unit)) { unit = m; evidence.Add($"{ingredient.Gid}: fabriqué {m:F0}"); }
                }
                if (unit is { } u) { cost += u * ingredient.Quantity * quantity; unknown.Remove(ingredient.Gid); }
                else allKnown = false;
            }
            bool ingredientsKnown = valid && allKnown;
            decimal net = (100m - saleTaxPercent) / 100m;
            results.Add(new(recipe, ingredientsKnown ? cost : null,
                output.Known && valid ? output.UnitPrice * quantity * net : null,
                unknown.OrderBy(id => id).ToArray(),
                $"{quantity} craft(s), " + (saleTaxPercent > 0 ? $"valeur nette de {saleTaxPercent:0.##}% de taxe; " : "estimation brute hors taxes/frais; ") +
                (valid ? "" : "recette invalide; ") + string.Join("; ", evidence)));
        }
        ct.ThrowIfCancellationRequested();
        return results;
    }

    /// <summary>Coût unitaire pour FABRIQUER gid : somme, pour chaque ingrédient, du moins cher entre son prix HDV et sa propre fabrication.
    /// Null si un ingrédient n'a ni prix ni recette exploitable. Profondeur bornée, cycles ignorés.</summary>
    private static decimal? CraftCost(long gid, IReadOnlyDictionary<long, CraftPrice> prices,
        IReadOnlyDictionary<long, CraftScanRecipe> sub, HashSet<long> stack, int depth, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (depth > MaxDecomposeDepth || !sub.TryGetValue(gid, out var r) || !stack.Add(gid)) return null;
        try
        {
            decimal total = 0;
            foreach (var i in r.Ingredients)
            {
                if (i.Gid <= 0 || i.Quantity <= 0) return null;
                decimal? unit = prices.TryGetValue(i.Gid, out var p) && p.Known ? p.UnitPrice : null;
                if (CraftCost(i.Gid, prices, sub, stack, depth + 1, ct) is { } made && (unit is null || made < unit)) unit = made;
                if (unit is null) return null;
                total += unit.Value * i.Quantity;
            }
            return total;
        }
        finally { stack.Remove(gid); }
    }

    public static IReadOnlyList<CraftScanEstimate> Profits(IEnumerable<CraftScanEstimate> estimates,
        long minGain, int minPercent, int sort)
    {
        var known = estimates.Where(e => e.Cost is > 0 && e.Value is > 0 &&
            e.Gain >= Math.Max(0, minGain) && e.GainPercent >= Math.Max(0, minPercent));
        // Matches the current view's three sort choices: gain, percent, name.
        return (sort switch
        {
            1 => known.OrderByDescending(e => e.GainPercent).ThenBy(e => e.Recipe.Name, StringComparer.OrdinalIgnoreCase),
            2 => known.OrderBy(e => e.Recipe.Name, StringComparer.OrdinalIgnoreCase),
            _ => known.OrderByDescending(e => e.Gain).ThenBy(e => e.Recipe.Name, StringComparer.OrdinalIgnoreCase),
        }).ThenBy(e => e.Recipe.Gid).ToArray();
    }
}
