using System;

namespace GivreClone.App.Services;

/// <summary>Shared price decision for the HDV engine and its preview.</summary>
public static class HdvRepricePolicy
{
    public static long Target(long price, long market, long minimum, long step, double floorPercent,
        bool ownAtMarket, bool ignored = false, bool fetchError = false)
    {
        if (market <= 0 || ignored || fetchError || price <= 0 || minimum <= 0) return -1;
        long? marketFloor = HdvSalePriceMath.MarketPercentageFloor(market, floorPercent);
        if (marketFloor is not long safeFloor) return -1;
        long target = ownAtMarket ? market : Math.Max(
            Math.Max(1, market - Math.Max(1, step)),
            safeFloor);
        target = Math.Max(target, Math.Max(1, minimum));
        return price > target ? target : -1;
    }

    public static bool CooldownElapsed(long nowMs, long? lastRepriceMs, int cooldownSec)
        => cooldownSec <= 0 || lastRepriceMs is not long last || nowMs - last >= cooldownSec * 1000L;
}
