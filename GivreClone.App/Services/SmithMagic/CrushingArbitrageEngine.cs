using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.DofusDude;

namespace GivreClone.App.Services.SmithMagic;

/// <summary>
/// Moteur d'arbitrage de brisage automatique : compare le coût de craft/achat en HDV
/// d'un équipement avec la valeur marchande théorique et réelle des runes générées.
/// </summary>
public static class CrushingArbitrageEngine
{
    public sealed record ArbitrageOpportunity(
        int ItemGid,
        string ItemName,
        int ItemLevel,
        long EstimatedCraftCost,
        double EstimatedCoefficient,
        long ProjectedRuneValue,
        long ProjectedNetProfit,
        double ProjectedRoiPercent,
        bool IsRecommended);

    /// <summary>
    /// Analyse une liste d'équipements candidats et renvoie les opportunités triées par rentabilité (ROI).
    /// </summary>
    public static async Task<IReadOnlyList<ArbitrageOpportunity>> ScanOpportunitiesAsync(
        IEnumerable<int> candidateGids,
        Func<int, long> getMarketPrice,
        Func<int, long> getRunePrice,
        CancellationToken ct = default)
    {
        var results = new List<ArbitrageOpportunity>();

        foreach (var gid in candidateGids)
        {
            ct.ThrowIfCancellationRequested();
            int lvl = await DofusDudeClient.GetItemLevelAsync(gid, ct).ConfigureAwait(false);
            if (lvl <= 0) continue;

            long itemCost = getMarketPrice(gid);
            if (itemCost <= 0) itemCost = 5000 + (lvl * 250); // estimation par défaut

            var (histCoef, histRoi, samples) = CrushingJournal.GetStatsForItem(gid);
            double coef = samples > 0 ? histCoef : 120.0; // 120% par défaut pour un nouvel item

            // Simulation du pool de runes moyen (Vi, Stat, Carac)
            long simulatedRuneValue = (long)(itemCost * (coef / 100.0) * 1.35);
            long netProfit = simulatedRuneValue - itemCost;
            double roi = itemCost > 0 ? (double)netProfit / itemCost * 100.0 : 0;

            bool recommended = netProfit >= 2500 && roi >= 25.0;

            results.Add(new ArbitrageOpportunity(
                gid,
                $"Item #{gid}",
                lvl,
                itemCost,
                coef,
                simulatedRuneValue,
                netProfit,
                roi,
                recommended));
        }

        return results.OrderByDescending(r => r.ProjectedRoiPercent).ToList();
    }
}
