using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Eco;

public enum RuneGrade { Simple, Pa, Ra }

// Total price of an observed lot, not an average or guide price. Callers supply one family only.
public sealed record RuneMarketLot(int ServerId, long Gid, RuneGrade Grade, int Size, long Price, int AvailableLots);
public sealed record RuneLotPurchase(RuneMarketLot Offer, int Lots);
public sealed record RuneAcquisitionRoute(RuneGrade SourceGrade, int RequiredInputs, int PurchasedInputs,
    int FusionOperations, long PurchaseCost, long TransportCost, IReadOnlyList<RuneLotPurchase> Purchases)
{
    public long TotalCost => checked(PurchaseCost + TransportCost);
}

public sealed record RuneAcquisitionComparison(IReadOnlyList<RuneAcquisitionRoute> Routes)
{
    public RuneAcquisitionRoute? Cheapest => Routes.OrderBy(r => r.TotalCost).ThenBy(r => r.FusionOperations).FirstOrDefault();
}

/// <summary>Advisory only: compare direct purchase and 3:1 fusion using current-server observed stock.
/// Unknown prices/transport exclude that route. Fusion fees, if any, belong in fusionTransportCost.</summary>
public static class RuneAcquisitionPolicy
{
    public static RuneAcquisitionComparison Compare(int serverId, RuneGrade target, int quantity,
        IEnumerable<RuneMarketLot> familyOffers, long? directTransportCost, long? fusionTransportCost)
    {
        if (serverId <= 0) throw new ArgumentOutOfRangeException(nameof(serverId));
        if (target < RuneGrade.Simple || target > RuneGrade.Ra) throw new ArgumentOutOfRangeException(nameof(target));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        var offers = familyOffers.Where(o => o.ServerId == serverId && o.Gid > 0 && o.Price > 0 &&
            o.AvailableLots > 0 && o.Size is 1 or 10 or 100 or 1000).ToArray();
        var routes = new List<RuneAcquisitionRoute>();
        for (int source = (int)target; source >= 0; source--)
        {
            long? transport = source == (int)target ? directTransportCost : fusionTransportCost;
            if (transport is null or < 0) continue;
            int factor = 1;
            for (int step = source; step < (int)target; step++) factor = checked(factor * 3);
            int required = checked(quantity * factor);
            var selected = CheapestLots(required, offers.Where(o => (int)o.Grade == source).ToArray());
            if (selected is null) continue;
            int purchased = selected.Sum(p => checked(p.Offer.Size * p.Lots));
            long price = selected.Sum(p => checked(p.Offer.Price * p.Lots));
            int operations = factor == 9 ? checked(quantity * 4) : factor == 3 ? quantity : 0;
            routes.Add(new((RuneGrade)source, required, purchased, operations, price, transport.Value, selected));
        }
        return new(routes.AsReadOnly());
    }

    private sealed record Choice(long Cost, RuneLotPurchase? Purchase, Choice? Previous);

    private static IReadOnlyList<RuneLotPurchase>? CheapestLots(int required, RuneMarketLot[] offers)
    {
        if (offers.Length == 0) return null;
        // Any cheapest positive-price covering set has less than one largest lot of surplus.
        int limit = checked(required + offers.Max(o => o.Size) - 1);
        var choices = new Dictionary<int, Choice> { [0] = new(0, null, null) };
        foreach (var offer in offers)
        {
            int remaining = Math.Min(offer.AvailableLots, checked((required - 1) / offer.Size + 1));
            int block = 1;
            while (remaining > 0)
            {
                int count = Math.Min(block, remaining);
                int added = checked(offer.Size * count);
                long cost = checked(offer.Price * count);
                foreach (var (amount, previous) in choices.ToArray())
                {
                    int total = checked(amount + added);
                    if (total > limit) continue;
                    long nextCost = checked(previous.Cost + cost);
                    if (!choices.TryGetValue(total, out var existing) || nextCost < existing.Cost)
                        choices[total] = new(nextCost, new(offer, count), previous);
                }
                remaining -= count;
                if (remaining > 0) block = (int)Math.Min((long)block * 2, remaining);
            }
        }
        var best = choices.Where(c => c.Key >= required).OrderBy(c => c.Value.Cost).ThenBy(c => c.Key).FirstOrDefault();
        if (best.Value is null) return null;
        var purchases = new List<RuneLotPurchase>();
        for (Choice? choice = best.Value; choice?.Purchase is { } purchase; choice = choice.Previous)
            purchases.Add(purchase);
        return purchases.GroupBy(p => p.Offer).Select(g => new RuneLotPurchase(g.Key, g.Sum(p => p.Lots))).ToArray();
    }
}
