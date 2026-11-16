using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Eco;

/// <summary>Un achat de runes à l'HDV : <see cref="Count"/> lots de <see cref="Lot"/> runes, chaque lot payé au plus <see cref="MaxLotPrice"/>.</summary>
public sealed record RunePurchase(long Gid, int Lot, int Count, long MaxLotPrice);

/// <summary>Achat des runes qui manquent (option de l'onglet Forgemagie) : lots de 100 / 10 / 1 couvrant le besoin, chaque lot plafonné à
/// <c>maxPercentOfAverage</c> % du prix moyen du serveur, total sous le budget en kamas. Une rune sans prix moyen connu n'est pas achetée
/// (aucune référence pour juger le prix). Pur : les achats eux-mêmes passent par l'HDV en mode achat.</summary>
public static class RunePurchasePlan
{
    private static readonly int[] Lots = { 100, 10, 1 };

    /// <summary>Échantillon d'exploration sur les prix RÉELS des lots : pour chaque rune, le lot admissible au coût unitaire le plus bas
    /// (prix réel <= plafond % de la référence), puis un lot des deux runes les moins chères, le tout sous 5000 kamas de prix réels.</summary>
    public static IReadOnlyList<RunePurchase> PlanExploration(IEnumerable<long> candidates, Func<long, long> referencePrice,
        Func<long, int, long> lotPrice, int maxPercentOfAverage, long budget, int minimumSample = 5)
    {
        long left = Math.Min(5000, Math.Max(0, budget));
        var plan = new List<RunePurchase>();
        if (minimumSample <= 0) return plan;
        var offers = candidates.Where(gid => gid > 0).Distinct()
            .SelectMany(gid => Lots.Select(lot => (Gid: gid, Lot: lot, Price: lotPrice(gid, lot))))
            .Where(o => o.Price > 0 && referencePrice(o.Gid) > 0 && maxPercentOfAverage > 0
                && o.Price <= (decimal)referencePrice(o.Gid) * o.Lot * (maxPercentOfAverage / 100m))
            .ToArray();
        while (plan.Count < 2)
        {
            // Re-rank all affordable sizes after each debit. The cheapest unit
            // price can belong to an unaffordable lot, including the second family.
            var feasible = offers.Where(o => (decimal)o.Price * (1 + (minimumSample - 1) / o.Lot) <= left
                    && plan.All(p => p.Gid != o.Gid))
                .OrderBy(o => (decimal)o.Price / o.Lot).ThenByDescending(o => o.Lot).ThenBy(o => o.Gid).ToArray();
            if (feasible.Length == 0) break;
            var offer = feasible[0];
            int count = 1 + (minimumSample - 1) / offer.Lot;
            plan.Add(new RunePurchase(offer.Gid, offer.Lot, count, offer.Price));
            left -= offer.Price * count;
        }
        return plan;
    }

    public static IReadOnlyList<RunePurchase> Plan(IEnumerable<(long Gid, int Quantity)> needs, Func<long, long> averagePrice,
        int maxPercentOfAverage, long budget)
    {
        var plan = new List<RunePurchase>();
        long left = Math.Max(0, budget);
        foreach (var (gid, qty) in needs.Where(n => n.Gid > 0 && n.Quantity > 0).GroupBy(n => n.Gid).Select(g => (g.Key, g.Sum(x => x.Quantity))))
        {
            long avg = averagePrice(gid);
            if (avg <= 0 || maxPercentOfAverage <= 0) continue;
            int rest = qty;
            foreach (int lot in Lots)
            {
                // Pas de lot plus gros que le besoin, sauf le plus petit qui couvre un reste.
                int count = lot == 1 ? rest : rest / lot;
                if (count <= 0) continue;
                long maxLot = checked(avg * lot) * maxPercentOfAverage / 100;
                if (maxLot <= 0) continue;
                int affordable = (int)Math.Min(count, left / maxLot);
                if (affordable <= 0) continue;
                plan.Add(new RunePurchase(gid, lot, affordable, maxLot));
                left -= affordable * maxLot;
                rest -= affordable * lot;
            }
        }
        return plan;
    }

    /// <summary>Prix de référence d'une rune : la moyenne du serveur si elle est connue, sinon le prix unitaire affiché à l'HDV des runes
    /// (en mode Socket la table des moyennes n'arrive jamais). 0 = aucune référence, la rune n'est pas achetée.</summary>
    public static long Reference(long average, long liveUnitPrice) => average > 0 ? average : Math.Max(0, liveUnitPrice);

    /// <summary>Runes à prévoir pour monter une ligne de <paramref name="gap"/> points avec une rune de <paramref name="amount"/> points :
    /// le nombre de passages nécessaires, doublé pour absorber les échecs (une rune ratée ne rapporte rien).</summary>
    public static int Needed(int gap, int amount) => gap <= 0 || amount <= 0 ? 0 : (int)Math.Ceiling(gap / (double)amount) * 2;

    /// <summary>Carte la plus proche de (x, y) parmi <paramref name="maps"/> (distance en cartes sur la grille du monde), ou null si aucune
    /// coordonnée connue. Sert à choisir l'HDV de runes.</summary>
    public static long? Nearest(IEnumerable<long> maps, int x, int y, Func<long, (int X, int Y)?> coordOf)
        => maps.Select(m => (m, c: coordOf(m))).Where(t => t.c is not null)
            .OrderBy(t => Math.Abs(t.c!.Value.X - x) + Math.Abs(t.c!.Value.Y - y))
            .Select(t => (long?)t.m).FirstOrDefault();
}
