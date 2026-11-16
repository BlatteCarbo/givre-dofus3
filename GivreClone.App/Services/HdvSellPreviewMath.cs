using System;

namespace GivreClone.App.Services;

/// <summary>
/// Arithmetic for indicative HDV figures only. Saturated estimates are never
/// legitimate listing prices and must not be passed to the seller API.
/// </summary>
public static class HdvSellPreviewMath
{
    public static long EstimatedValue(long averageUnit, int quantity)
    {
        if (averageUnit <= 0 || quantity <= 0) return 0;
        return averageUnit > long.MaxValue / quantity
            ? long.MaxValue : averageUnit * quantity;
    }

    public static long AddValue(long current, long next)
    {
        if (current <= 0) return Math.Max(0, next);
        if (next <= 0) return current;
        return next > long.MaxValue - current ? long.MaxValue : current + next;
    }

    public static int AddQuantity(int current, int next)
    {
        if (current <= 0) return Math.Max(0, next);
        if (next <= 0) return current;
        return next > int.MaxValue - current ? int.MaxValue : current + next;
    }

    /// <summary>
    /// The cap warning is indicative, never an authorization to sell.
    /// Unknown tax rates remain unknown (no fabricated 2% replacement).
    /// </summary>
    public static (bool Warn, bool UnknownRate, long RemainingListingValue) TaxCapHint(
        long cap, long observedTax, long estimatedListingsValue, float percent)
    {
        if (cap <= 0) return (false, false, 0);
        if (observedTax < 0 || !float.IsFinite(percent) || percent <= 0 || percent > 100)
            return (true, true, 0);

        long? estimatedTax = estimatedListingsValue <= 0
            ? 0 : HdvTaxBudget.EstimateUpperBound(estimatedListingsValue, percent);
        if (estimatedTax is null) return (true, true, 0);
        if (observedTax <= cap && estimatedTax <= cap - observedTax)
            return (false, false, 0);

        long remainingTax = observedTax >= cap ? 0 : cap - observedTax;
        double ceiling = remainingTax * 100.0 / percent;
        long remainingListings = !double.IsFinite(ceiling) || ceiling >= long.MaxValue
            ? long.MaxValue : (long)Math.Max(0, Math.Floor(ceiling));
        return (true, false, remainingListings);
    }
}
