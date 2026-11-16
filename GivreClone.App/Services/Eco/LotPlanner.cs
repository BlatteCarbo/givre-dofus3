using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Eco;

public enum LotPolicy
{
    LargestFirst,   // 1000 → 100 → 10 → 1
    Smart,          // achat : le moins cher à la marge près ; vente : le plus cher à la marge près
}

/// <summary>Une offre de lot à l'HDV : taille du lot (1, 10, 100, 1000) et prix unitaire.</summary>
public readonly record struct LotOffer(int Size, decimal UnitPrice);

/// <summary>Choix de la taille de lot pour acheter ou vendre une quantité (idée de la doc publique de Docraft) : pur. Politique « grand d'abord » ; politique « marge » : un lot plus petit est préféré
/// au plus grand dès qu'il est meilleur d'au moins la marge (en % du prix du plus grand) — moins cher à l'achat, plus cher à la vente.</summary>
public static class LotPlanner
{
    /// <summary>Taille du prochain lot pour une quantité restante, ou null si aucune offre ne tient. <paramref name="buying"/> : sens (achat = moins cher, vente = plus cher).</summary>
    public static LotOffer? Next(int remaining, IReadOnlyList<LotOffer> offers, LotPolicy policy, double marginPercent, bool buying)
    {
        var fit = offers.Where(o => o.Size >= 1 && o.Size <= remaining).OrderByDescending(o => o.Size).ToList();
        if (fit.Count == 0) return null;
        var largest = fit[0];
        if (policy == LotPolicy.LargestFirst) return largest;
        LotOffer? best = null;
        foreach (var o in fit.Skip(1))
        {
            if (largest.UnitPrice <= 0) break;
            double gain = buying ? (double)((largest.UnitPrice - o.UnitPrice) / largest.UnitPrice) * 100 : (double)((o.UnitPrice - largest.UnitPrice) / largest.UnitPrice) * 100;
            if (gain <= marginPercent) continue;
            if (best is null || (buying ? o.UnitPrice < best.Value.UnitPrice : o.UnitPrice > best.Value.UnitPrice)) best = o;
        }
        return best ?? largest;
    }

    /// <summary>Découpe la quantité en lots successifs (même règle à chaque pas). Reste non couvert s'il manque la petite taille.</summary>
    public static (IReadOnlyList<LotOffer> Lots, int Uncovered) Decompose(int quantity, IReadOnlyList<LotOffer> offers, LotPolicy policy, double marginPercent, bool buying)
    {
        var lots = new List<LotOffer>();
        int left = quantity;
        while (left > 0 && Next(left, offers, policy, marginPercent, buying) is { } lot) { lots.Add(lot); left -= lot.Size; }
        return (lots, left);
    }
}
