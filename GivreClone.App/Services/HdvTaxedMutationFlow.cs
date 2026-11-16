using System;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services;

/// <summary>
/// One real mutation gate shared by manual sale, bulk sale and repricing.
/// The sender must return true ONLY after it has independently matched the
/// server result to the requested UID, quantity and price. It cannot return a
/// tax receipt. Local intent -> attempted -> observed records remain reserved.
/// </summary>
public static class HdvTaxedMutationFlow
{
    public readonly record struct Result(HdvTaxOrderReservation Decision, bool ServerResultObserved)
    {
        public bool Allowed => Decision == HdvTaxOrderReservation.Reserved;
    }

    public static async Task<Result> ExecuteAsync(HdvTaxReservations ledger, uint pid,
        string? profile, string orderKey, long cap, long observedSpent,
        long listingPrice, float taxRate,
        Func<CancellationToken, Task<bool>> sendAndVerify, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(sendAndVerify);
        // Invalid order prices must never reach a sender, even with the
        // historical unlimited-tax setting. No fee journal can legitimize one.
        if (listingPrice <= 0 || cap < 0)
            return new(HdvTaxOrderReservation.Unavailable, false);
        // Zero is the historical UNLIMITED monetary cap, not permission to
        // bypass durable order identity. Journal intent/attempt even when no
        // fee ceiling applies, so crash, concurrent callers and PID reuse
        // cannot blindly re-emit an ambiguous UID mutation.
        long? reservedFee = HdvTaxBudget.EstimateUpperBound(listingPrice, taxRate);
        if (reservedFee is null) return new(HdvTaxOrderReservation.Unavailable, false);
        HdvTaxOrderReservation result = ledger.TryReserveOrder(pid, profile, orderKey,
            cap, observedSpent, reservedFee.Value);
        if (result != HdvTaxOrderReservation.Reserved) return new(result, false);
        // Durable attempt BEFORE any user delegate or network send.
        if (!ledger.TryAdvanceOrder(pid, profile, orderKey, HdvTaxOrderPhase.Attempted))
            return new(HdvTaxOrderReservation.Unavailable, false);
        bool witnessed;
        try
        {
            ct.ThrowIfCancellationRequested();
            witnessed = await sendAndVerify(ct).ConfigureAwait(false);
        }
        catch
        {
            // A callback or transport can throw AFTER emission; never retry
            // with a fresh UID nor turn this into an assertion of refusal.
            ledger.TryAdvanceOrder(pid, profile, orderKey, HdvTaxOrderPhase.Uncertain);
            throw;
        }
        // False is indeterminate: the sender may have emitted before a timeout.
        // Even a uniquely observed listing success does NOT prove paid tax.
        if (!ledger.TryAdvanceOrder(pid, profile, orderKey,
                witnessed ? HdvTaxOrderPhase.Observed : HdvTaxOrderPhase.Uncertain))
            return new(HdvTaxOrderReservation.Unavailable, false);
        return new(HdvTaxOrderReservation.Reserved, witnessed);
    }
}
