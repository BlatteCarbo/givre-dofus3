using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Eco;

/// <summary>Prix HDV déjà observés pour un objet : prix du lot le moins cher par taille [1, 10, 100, 1000] (-1 = inconnu) et prix moyen du marché.</summary>
public sealed record MarketQuote(long Gid, string Name, IReadOnlyList<long> LotPrices, long Average, long? AgeMs);

public sealed record MarketDeal(long Gid, string Name, int LotSize, decimal UnitPrice, long Average, double DiscountPercent, long GainPerUnit,
                                long LotCost, long LotGain, long? AgeMs);

/// <summary>Arbitrage de découpe : acheter un lot L, le revendre en SplitCount lots plus petits S. Montants en kamas, taxe HDV incluse.</summary>
public sealed record BatchSplitDeal(long Gid, string Name, int BuyLotSize, long BuyLotCost, int SellLotSize, long SellLotPrice, int SplitCount,
                                    long GrossRevenue, long TaxCost, long NetProfit, double NetMarginPercent, long? AgeMs);

/// <summary>« Bonnes affaires » : lots dont le prix unitaire est nettement sous la moyenne du marché (à revendre ou à acheter pour fabriquer).
/// Pure : n'interroge pas l'HDV, ne lit que des prix déjà reçus ; la moyenne et le lot ne datent pas forcément de la même minute, donc indicatif.</summary>
public static class MarketDeals
{
    public static readonly int[] LotSizes = { 1, 10, 100, 1000 };

    public static IReadOnlyList<MarketDeal> Find(IEnumerable<MarketQuote> quotes, double minDiscountPercent, long minLotGain, long? maxAgeMs = null)
    {
        var res = new List<MarketDeal>();
        foreach (var q in quotes)
        {
            if (q.Average <= 0) continue;
            if (maxAgeMs is { } max && (q.AgeMs is null || q.AgeMs > max)) continue;
            MarketDeal? best = null;
            for (int i = 0; i < LotSizes.Length && i < q.LotPrices.Count; i++)
            {
                long lot = q.LotPrices[i];
                if (lot <= 0) continue;
                int size = LotSizes[i];
                decimal unit = lot / (decimal)size;
                double disc = (double)((q.Average - unit) * 100m / q.Average);
                long gainUnit = (long)Math.Floor(q.Average - unit);
                long lotGain = (long)Math.Floor(q.Average * (double)size) - lot;
                if (disc < minDiscountPercent || lotGain < minLotGain) continue;
                var d = new MarketDeal(q.Gid, q.Name, size, unit, q.Average, disc, gainUnit, lot, lotGain, q.AgeMs);
                if (best is null || d.LotGain > best.LotGain) best = d;
            }
            if (best is not null) res.Add(best);
        }
        return res.OrderByDescending(d => d.LotGain).ThenByDescending(d => d.DiscountPercent).ToArray();
    }

    private static readonly int[] SplitBuyLots = { 10, 100, 1000 };
    private static readonly int[] SplitSellLots = { 1, 10, 100 };

    /// <summary>Découpe de lots : un lot observé L (10/100/1000) acheté puis revendu en L/S lots S plus petits (1/10/100) au prix observé du lot S,
    /// taxe HDV déduite (arrondie au kama supérieur). Pure et indicative : ne lit que des prix déjà reçus, n'envoie aucun ordre ;
    /// les prix peuvent avoir bougé et la revente suppose assez de demande au prix observé.</summary>
    public static IReadOnlyList<BatchSplitDeal> FindBatchSplits(IEnumerable<MarketQuote> quotes, double minNetMarginPercent, long minNetProfit,
        decimal taxRate = 0.02m, long? maxAgeMs = null)
    {
        var res = new List<BatchSplitDeal>();
        foreach (var q in quotes)
        {
            if (maxAgeMs is { } max && (q.AgeMs is null || q.AgeMs > max)) continue;
            BatchSplitDeal? best = null;
            foreach (int big in SplitBuyLots)
            {
                long bigPrice = PriceOfLot(q, big);
                if (bigPrice <= 0) continue;
                foreach (int small in SplitSellLots)
                {
                    if (big <= small) continue;
                    long smallPrice = PriceOfLot(q, small);
                    if (smallPrice <= 0) continue;
                    int count = big / small;
                    long gross = checked(count * smallPrice);
                    long tax = (long)Math.Ceiling(gross * taxRate);
                    long net = gross - tax - bigPrice;
                    double margin = net * 100.0 / bigPrice;
                    if (net <= 0 || net < minNetProfit || margin < minNetMarginPercent) continue;
                    var d = new BatchSplitDeal(q.Gid, q.Name, big, bigPrice, small, smallPrice, count, gross, tax, net, margin, q.AgeMs);
                    if (best is null || d.NetProfit > best.NetProfit || (d.NetProfit == best.NetProfit && d.NetMarginPercent > best.NetMarginPercent)) best = d;
                }
            }
            if (best is not null) res.Add(best);
        }
        return res.OrderByDescending(d => d.NetProfit).ThenByDescending(d => d.NetMarginPercent).ToArray();
    }

    private static long PriceOfLot(MarketQuote q, int size)
    {
        int i = Array.IndexOf(LotSizes, size);
        return i >= 0 && i < q.LotPrices.Count ? q.LotPrices[i] : -1;
    }
}
