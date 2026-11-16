using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.Eco;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    public async Task<RuneAcquisitionComparison> CompareRuneAcquisitionAsync(uint pid, long targetGid,
        int quantity, long? fusionTransportCost, CancellationToken ct)
    {
        var catalog = RuneItemsByGid();
        if (!catalog.TryGetValue(targetGid, out var target)) throw new ArgumentException("Rune inconnue.");
        int server = ServerIdOf(pid);
        if (server <= 0) throw new InvalidOperationException("Serveur inconnu.");
        if (!HdvBuyerOpen(pid)) throw new InvalidOperationException("Ouvre l'HDV des runes en mode achat avant la comparaison.");
        static RuneGrade Grade(string tier) => tier switch
        {
            "Pa" => RuneGrade.Pa,
            "Ra" => RuneGrade.Ra,
            _ => RuneGrade.Simple,
        };
        var offers = new List<RuneMarketLot>();
        int[] sizes = [1, 10, 100, 1000];
        foreach (var rune in catalog.Where(r => r.Value.EffectId == target.EffectId))
        {
            var rows = await HdvGetBuyerOffersAsync(pid, rune.Key, ct).ConfigureAwait(false);
            foreach (var row in rows)
                for (int slot = 0; slot < Math.Min(sizes.Length, row.Prices.Length); slot++)
                    if (row.Prices[slot] > 0)
                        offers.Add(new(server, rune.Key, Grade(rune.Value.Tier), sizes[slot], row.Prices[slot], 1));
        }
        // Quotes do not expose full stock. Only one observed lot per price is considered;
        // this comparison never authorizes or executes an acquisition/fusion.
        return RuneAcquisitionPolicy.Compare(server, Grade(target.Tier), quantity, offers, 0, fusionTransportCost);
    }
}
