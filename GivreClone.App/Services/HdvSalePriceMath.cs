using System;

namespace GivreClone.App.Services;

/// <summary>
/// Strict arithmetic for REAL HDV order prices. Unlike preview math, values
/// outside the Int64 domain MUST be refused, never saturated or wrapped into
/// a plausible listing price.
/// </summary>
public static class HdvSalePriceMath
{
    public static long? HalfAverageFloor(long averageUnit, int lotSize)
    {
        if (lotSize <= 0 || averageUnit < 0) return null;
        if (averageUnit == 0) return 1; // Existing unknown-average policy.
        try
        {
            decimal floor = decimal.Ceiling((decimal)averageUnit * lotSize / 2m);
            return floor > long.MaxValue ? null : Math.Max(1, (long)floor);
        }
        catch (OverflowException) { return null; }
    }

    public static long? DerivedPrice(decimal unitPrice, int lotSize)
    {
        if (lotSize <= 0 || unitPrice <= 0) return null;
        try
        {
            decimal price = decimal.Ceiling(unitPrice * lotSize);
            if (price > long.MaxValue) return null;
            return Math.Max(lotSize, (long)price);
        }
        catch (OverflowException) { return null; }
    }

    public static long? MarketPercentageFloor(long market, double floorPercent)
    {
        if (market <= 0 || !double.IsFinite(floorPercent)) return null;
        decimal percent = (decimal)Math.Clamp(floorPercent, 0, 100);
        decimal price = decimal.Floor(market * (100m - percent) / 100m);
        return (long)price; // 0 <= price <= market <= Int64.MaxValue.
    }
}
