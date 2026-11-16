using System;
using System.Collections.Generic;

namespace GivreClone.App.Services;

/// <summary>Limites de vente choisies par l'utilisateur : le lot demandé ne peut jamais justifier son propre prix.
/// <paramref name="ceilingMultiplier"/> &lt;= 0 désactive le plafond de prix ; <paramref name="maxTax"/> &lt;= 0 désactive
/// le plafond de taxe par opération. Sans référence indépendante, aucun plafond de prix ne s'applique.</summary>
public static class HdvSalePriceGuard
{
    public const decimal DefaultCeilingMultiplier = 10m;

    public static string? Refusal(int quantity, long price, long averageUnit,
        IReadOnlyList<long> marketPrices, decimal taxPercent,
        decimal ceilingMultiplier = DefaultCeilingMultiplier, long maxTax = 0)
    {
        if (quantity <= 0 || price <= 0) return "Prix ou quantité invalide.";
        if (ceilingMultiplier > 0)
        {
            decimal reference = averageUnit > 0 ? averageUnit : decimal.MaxValue;
            int[] sizes = { 1, 10, 100, 1000 };
            for (int i = 0; i < sizes.Length && i < marketPrices.Count; i++)
                if (sizes[i] != quantity && marketPrices[i] > 0)
                    reference = Math.Min(reference, (decimal)marketPrices[i] / sizes[i]);
            if (reference != decimal.MaxValue)
            {
                decimal ceiling;
                try { ceiling = reference * quantity * ceilingMultiplier; }
                catch (OverflowException)
                {
                    // No mathematically justified Int64 order price can be
                    // derived from a non-representable configured ceiling.
                    return "Vente bloquée : plafond de prix non représentable.";
                }
                if (price > ceiling)
                    return $"Vente bloquée : prix {price:N0} supérieur au plafond {decimal.Floor(ceiling):N0} kamas ({ceilingMultiplier:0.##} × référence unitaire, réglable dans l'onglet HDV).";
            }
        }
        if (maxTax <= 0) return null;
        if (taxPercent <= 0 || taxPercent > 100)
            return "Vente bloquée : taux de taxe non observé, impossible de vérifier le plafond de taxe choisi.";
        decimal tax = decimal.Ceiling(price * taxPercent / 100m);
        return tax > maxTax
            ? $"Vente bloquée : taxe estimée {tax:N0} supérieure au plafond choisi de {maxTax:N0} kamas par opération."
            : null;
    }
}
