using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.App.Services.LotM;

namespace GivreClone.App.Services.Eco;

public enum CraftDecision { Buy, Craft, Unknown }
public enum CraftForce { Auto, Buy, Craft }

/// <summary>Prix unitaire connu d'un objet : valeur, âge de l'observation (ms, null = inconnu) et provenance (« marché », « moyenne », « manuel »).</summary>
public sealed record PriceQuote(decimal Unit, long? AgeMs, string Source);

/// <summary>Un noeud de l'arbre : l'objet, la quantité totale voulue, son prix d'achat, son coût de fabrication et le choix retenu.</summary>
public sealed record CraftNode(long Gid, string Name, long Quantity, decimal? BuyUnit, decimal? CraftUnit, CraftDecision Decision,
                               decimal? UnitCost, bool Stale, string PriceSource, IReadOnlyList<CraftNode> Children)
{
    public decimal? TotalCost => UnitCost is { } u ? u * Quantity : null;
    public bool Craftable => Children.Count > 0;
}

/// <param name="CraftCost">Coût pour fabriquer la quantité demandée (ingrédients au moins cher de « acheter » ou « fabriquer »).</param>
/// <param name="BuyCost">Coût pour acheter directement cette quantité au prix HDV.</param>
/// <param name="Saving">BuyCost − CraftCost : positif = fabriquer est moins cher.</param>
/// <param name="ResaleProfit">Valeur de revente nette de la taxe − CraftCost.</param>
public sealed record CraftAdvice(CraftNode Root, decimal? CraftCost, decimal? BuyCost, decimal? Saving, double? SavingPercent,
                                 decimal? ResaleProfit, bool AnyStale, IReadOnlyList<long> MissingPrices, string Recommendation);

/// <summary>« Fabriquer ou acheter ? » (comme Dofus-Craft) : arbre récursif, chaque ingrédient fabricable est compté au moins cher de son prix
/// HDV et de sa propre fabrication, avec la possibilité de FORCER « acheter » ou « fabriquer » par objet. Pure : ne lit ni HDV ni base.</summary>
public static class CraftAdvisor
{
    public const int MaxDepth = 6;
    public const long DefaultStaleAfterMs = 48L * 3_600_000;

    public static CraftAdvice Analyze(long gid, long quantity, Func<long, CraftScanRecipe?> recipe, Func<long, PriceQuote?> price,
        Func<long, string> name, IReadOnlyDictionary<long, CraftForce>? force = null, decimal salePercent = 0, long staleAfterMs = DefaultStaleAfterMs)
    {
        if (quantity < 1 || quantity > 100_000) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (salePercent is < 0 or >= 100) throw new ArgumentOutOfRangeException(nameof(salePercent));
        var missing = new SortedSet<long>();
        bool anyStale = false;

        CraftNode Build(long id, long qty, int depth, HashSet<long> stack, bool isRoot)
        {
            var quote = price(id);
            decimal? buy = quote is { Unit: > 0 } ? quote.Unit : null;
            bool stale = quote?.AgeMs is { } age && age > staleAfterMs;
            var rec = depth < MaxDepth && !stack.Contains(id) ? recipe(id) : null;
            var children = new List<CraftNode>();
            decimal? craftUnit = null;
            if (rec is { Ingredients.Count: > 0 })
            {
                stack.Add(id);
                bool known = true; decimal total = 0;
                foreach (var ing in rec.Ingredients)
                {
                    if (ing.Gid <= 0 || ing.Quantity <= 0) { known = false; continue; }
                    var child = Build(ing.Gid, qty * ing.Quantity, depth + 1, stack, false);
                    children.Add(child);
                    if (child.TotalCost is { } c) total += c; else known = false;
                }
                stack.Remove(id);
                craftUnit = known ? total / Math.Max(1, qty) : null;
            }
            var f = force is not null && force.TryGetValue(id, out var ff) ? ff : CraftForce.Auto;
            CraftDecision decision;
            if (isRoot) decision = craftUnit is not null ? CraftDecision.Craft : buy is not null ? CraftDecision.Buy : CraftDecision.Unknown;
            else if (f == CraftForce.Buy) decision = buy is not null ? CraftDecision.Buy : CraftDecision.Unknown;
            else if (f == CraftForce.Craft) decision = craftUnit is not null ? CraftDecision.Craft : buy is not null ? CraftDecision.Buy : CraftDecision.Unknown;
            else decision = (craftUnit, buy) switch
            {
                ({ } c, { } b) => c < b ? CraftDecision.Craft : CraftDecision.Buy,
                ({ }, null) => CraftDecision.Craft,
                (null, { }) => CraftDecision.Buy,
                _ => CraftDecision.Unknown,
            };
            decimal? unitCost = decision switch { CraftDecision.Craft => craftUnit, CraftDecision.Buy => buy, _ => null };
            bool nodeStale = decision == CraftDecision.Buy && stale;
            if (nodeStale) anyStale = true;
            if (decision == CraftDecision.Unknown && children.Count == 0 && !isRoot) missing.Add(id);
            if (decision == CraftDecision.Unknown && children.Count == 0 && isRoot) missing.Add(id);
            return new CraftNode(id, name(id), qty, buy, craftUnit, decision, unitCost, nodeStale,
                decision == CraftDecision.Buy ? quote?.Source ?? "" : "", decision == CraftDecision.Buy ? Array.Empty<CraftNode>() : children);   // inconnu : on garde l'arbre pour saisir les prix manquants
        }

        var root = Build(gid, quantity, 0, new HashSet<long>(), true);
        decimal? craftCost = root.CraftUnit is { } cu ? cu * quantity : null;
        decimal? buyCost = root.BuyUnit is { } bu ? bu * quantity : null;
        decimal? saving = craftCost is { } cc && buyCost is { } bc ? bc - cc : null;
        double? pct = saving is { } s && buyCost is > 0 ? (double)(s * 100m / buyCost.Value) : null;
        decimal? resale = craftCost is { } c2 && buyCost is { } b2 ? b2 * (100m - salePercent) / 100m - c2 : null;
        string reco = craftCost is null
            ? $"Données incomplètes : {(missing.Count > 0 ? missing.Count + " prix manquant(s)" : "aucune recette")}."
            : buyCost is null ? "Prix HDV du résultat inconnu : impossible de comparer."
            : saving > 0 ? $"Fabriquer : économie de {saving:N0} kamas ({pct:0.#} %)."
            : saving < 0 ? $"Acheter : fabriquer coûterait {-saving:N0} kamas de plus ({-pct:0.#} %)."
            : "Égalité : acheter ou fabriquer coûte pareil.";
        if (craftCost is not null && buyCost is not null && anyStale) reco += " Non garanti : certains prix ont plus de 48 h.";
        return new CraftAdvice(root, craftCost, buyCost, saving, pct, resale, anyStale, missing.ToArray(), reco);
    }

    /// <summary>Aplati l'arbre pour l'affichage : (profondeur, noeud) en ordre d'apparition.</summary>
    public static IReadOnlyList<(int Depth, CraftNode Node)> Flatten(CraftNode root)
    {
        var res = new List<(int, CraftNode)>();
        void Walk(CraftNode n, int d) { res.Add((d, n)); foreach (var c in n.Children) Walk(c, d + 1); }
        Walk(root, 0);
        return res;
    }

    /// <summary>Liste de courses : feuilles à ACHETER (regroupées par objet), avec la quantité totale et le coût.</summary>
    public static IReadOnlyList<(long Gid, string Name, long Quantity, decimal? Cost)> ShoppingList(CraftNode root)
    {
        var map = new Dictionary<long, (string Name, long Qty, decimal? Cost)>();
        void Walk(CraftNode n)
        {
            if (n.Decision != CraftDecision.Buy && n.Children.Count > 0) { foreach (var c in n.Children) Walk(c); return; }
            if (ReferenceEquals(n, root)) return;
            var cur = map.TryGetValue(n.Gid, out var v) ? v : (n.Name, 0L, (decimal?)0);
            map[n.Gid] = (n.Name, cur.Item2 + n.Quantity, cur.Item3 is { } a && n.TotalCost is { } t ? a + t : null);
        }
        Walk(root);
        return map.Select(kv => (kv.Key, kv.Value.Name, kv.Value.Qty, kv.Value.Cost)).OrderByDescending(x => x.Cost ?? 0).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
