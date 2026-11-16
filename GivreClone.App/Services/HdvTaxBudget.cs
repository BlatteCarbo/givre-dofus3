using System;

namespace GivreClone.App.Services;

/// <summary>
/// Conservative preflight for a sequence of HDV listings. Pending fees must
/// NOT be released by a global kamas/tax delta without order-attributed
/// receipts. This is not a server receipt or a precise tax forecast.
/// </summary>
public static class HdvTaxBudget
{
    public static long? EstimateUpperBound(long listingPrice, float percent)
    {
        if (listingPrice <= 0 || !float.IsFinite(percent) || percent <= 0 || percent > 100)
            return null;
        decimal estimate = Math.Ceiling(listingPrice * (decimal)percent / 100m);
        return estimate > long.MaxValue ? null : Math.Max(1, (long)estimate);
    }

    public static long UnconfirmedReservation(long estimatedTaxSinceStart, long initialObservedTax,
        long currentObservedTax)
    {
        if (estimatedTaxSinceStart <= 0) return 0;
        // Legacy reconciliation is valid ONLY if every receipt is attributed
        // to this run. A global TotalTax ledger also includes external actions
        // and therefore must NOT be used to release a bulk order reservation.
        if (initialObservedTax < 0 || currentObservedTax < 0) return estimatedTaxSinceStart;
        long received = currentObservedTax > initialObservedTax
            ? currentObservedTax - initialObservedTax : 0;
        return Math.Max(0, estimatedTaxSinceStart - received);
    }

    /// <summary>
    /// The current UI tax ledger has no immutable per-order receipt identity.
    /// Until attribution is proven, all estimated fees remain reserved,
    /// including an order whose result is unknown.
    /// </summary>
    public static long ReservationWithoutAttributedReceipts(long estimatedTaxSinceStart)
        => Math.Max(0, estimatedTaxSinceStart);

    public static bool CanReserve(long cap, long observedSpent, long unconfirmedReserved, long nextEstimatedTax)
    {
        if (cap == 0) return observedSpent >= 0 && unconfirmedReserved >= 0
            && nextEstimatedTax >= 0; // Existing setting: zero means unlimited.
        if (cap < 0) return false;
        if (observedSpent < 0 || unconfirmedReserved < 0 || nextEstimatedTax < 0 || observedSpent > cap)
            return false;
        return unconfirmedReserved <= cap - observedSpent
            && nextEstimatedTax <= cap - observedSpent - unconfirmedReserved;
    }
}
