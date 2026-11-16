using System;
using System.Collections.Concurrent;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    private readonly ConcurrentDictionary<uint, (decimal Ceiling, long MaxTax)> _hdvSaleLimits = new();

    public void SetHdvSaleLimits(uint pid, decimal ceilingMultiplier, long maxTaxPerOperation)
        => _hdvSaleLimits[pid] = (Math.Max(0, ceilingMultiplier), Math.Max(0, maxTaxPerOperation));

    public string? HdvSalePriceRefusal(uint pid, long gid, int quantity, long price)
    {
        // AveragePrice consumes an Int32 GID. Never wrap an untrusted Int64
        // script/adapter GID into a different item's market reference.
        if (gid <= 0 || gid > int.MaxValue)
            return "Vente bloquée : GID hors plage du catalogue.";
        long average = AveragePrice(pid, (int)gid);
        if (average <= 0) average = Hdv.AverageFor(pid, gid);
        // The shared ceiling guard intentionally does not impose a minimum:
        // its API only models user-chosen limits. The actual dispatch boundary
        // MUST also enforce the server-average safety floor. Without this
        // check a manual price, Lua script, or production listing could send
        // x10 for 1 kama despite a confirmed 100-kama average, because the
        // UI's SanityFloor only checked that a floor existed (> 0).
        // Use exact decimal arithmetic; a non-representable floor is a refusal,
        // not a wrapped, clamped, or silently disabled protection.
        if (average > 0)
        {
            long? minimum = HdvSalePriceMath.HalfAverageFloor(average, quantity);
            if (minimum is null)
                return "Vente bloquée : plancher serveur non représentable.";
            if (price < minimum.Value)
                return $"Vente bloquée : prix {price:N0} sous le plancher serveur {minimum.Value:N0} kamas.";
        }
        var prices = Hdv.PriceAgeMs(pid, gid) <= 90_000
            ? Hdv.MarketPricesFor(pid, gid) : Array.Empty<long>();
        var limits = _hdvSaleLimits.TryGetValue(pid, out var l) ? l : (HdvSalePriceGuard.DefaultCeilingMultiplier, 0L);
        float taxPercent = HdvTaxPercent(pid);
        if (!float.IsFinite(taxPercent) || taxPercent < 0 || taxPercent > 100)
            return "Vente bloquée : taux de taxe HDV invalide.";
        return HdvSalePriceGuard.Refusal(quantity, price, average, prices,
            (decimal)taxPercent, limits.Item1, limits.Item2);
    }
}
