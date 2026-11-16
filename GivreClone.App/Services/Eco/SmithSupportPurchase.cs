using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Eco;

public sealed record SmithSupportOffer(long Gid, int Level, int Uid, long Price, long? RuneCashCost = 0,
    decimal? MeasuredXp = null);

/// <summary>All prices here are positive live unit-lot prices, not average-price estimates.</summary>
public static class SmithSupportPurchase
{
    public static long Cap(long cash, long floor)
        => floor < 0 || cash <= floor ? 0 : Math.Min(8000, (cash - floor) / 4);

    public static bool CanBuy(long cash, long floor, long livePrice)
        => livePrice > 0 && livePrice <= Cap(cash, floor);

    public static IReadOnlyList<SmithSupportOffer> Rank(IEnumerable<SmithSupportOffer> offers, int professionLevel, long cash, long floor)
        => offers.Where(offer => offer.Uid > 0 && offer.Gid > 0 && offer.Level > 0
                && offer.Level <= professionLevel && CanBuy(cash, floor, offer.Price)
                && offer.RuneCashCost is >= 0 && offer.RuneCashCost <= cash - floor - offer.Price)
            // XP is usable only when supplied by a local measurement. Unknown choices
            // retain a level-band exploration order, including the rune cash estimate.
            .OrderBy(offer => offer.MeasuredXp > 0 ? 0 : 1)
            .ThenBy(offer => offer.MeasuredXp > 0
                ? ((decimal)offer.Price + offer.RuneCashCost!.Value) / offer.MeasuredXp.Value : 0)
            .ThenBy(offer => professionLevel - offer.Level < 5 ? 0 : professionLevel - offer.Level <= 8 ? 1 : 2)
            .ThenBy(offer => (decimal)offer.Price + offer.RuneCashCost!.Value)
            .ThenByDescending(offer => offer.Level).ThenBy(offer => offer.Gid).ThenBy(offer => offer.Uid)
            .ToArray();

    /// <summary>The market UID is not a bag UID. Require an admitted matching stack to increase after the confirmed purchase.</summary>
    public static long AcquiredUid(long gid, IReadOnlyDictionary<int, int> before,
        IEnumerable<(int Uid, long Gid, int Quantity, bool Admitted)> after)
        => after.Where(item => item.Uid > 0 && item.Gid == gid && item.Admitted
                && item.Quantity > before.GetValueOrDefault(item.Uid))
            .OrderBy(item => item.Uid).Select(item => (long)item.Uid).FirstOrDefault();
}
