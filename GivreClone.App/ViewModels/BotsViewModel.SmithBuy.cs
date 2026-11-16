using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services;
using GivreClone.App.Services.Eco;

namespace GivreClone.App.ViewModels;

// ===================== Forgemagie : achat des runes manquantes =====================
// HDV de runes le plus proche -> hôtelier en mode achat -> lots plafonnés (RunePurchasePlan) -> retour sur la carte de l'atelier.
// Même hôte de trajet transitoire que « À proximité » / le Trésor. Gaté Perm.Hdv par le moteur (HdvBuyByGidAsync).
public partial class BotsViewModel
{
    private Task<(int Bought, string Report)> BuyRunesAsync(uint pid, IReadOnlyList<(long Gid, int Quantity)> needs,
        Func<Func<long, long>, IReadOnlyList<RunePurchase>> planner, CancellationToken ct)
        => BuyRunesCoreAsync(pid, needs, (reference, _) => planner(reference), false, ct);

    private Task<(int Bought, string Report)> BuyRunesAtLotPricesAsync(uint pid, IReadOnlyList<(long Gid, int Quantity)> needs,
        Func<Func<long, long>, Func<long, int, long>, IReadOnlyList<RunePurchase>> planner, CancellationToken ct)
        => BuyRunesCoreAsync(pid, needs, planner, true, ct);

    private async Task<(int Bought, string Report)> BuyRunesCoreAsync(uint pid, IReadOnlyList<(long Gid, int Quantity)> needs,
        Func<Func<long, long>, Func<long, int, long>, IReadOnlyList<RunePurchase>> planner, bool quoteLots, CancellationToken ct)
    {
        var launch = Stores.Launch;
        if (needs.Count == 0) return (0, "aucune rune manquante");
        if (!LicenseService.HasPermission(LicenseService.Perm.Hdv)) return (0, "droit Hôtel de Vente manquant");
        long here = launch.CurrentMap(pid) ?? 0;
        if (here <= 0 || launch.MapCoordOf(here) is not (int x, int y)) return (0, "position du perso inconnue");
        var markets = HdvLocations.All.Where(h => h.Name.Contains("Rune", StringComparison.OrdinalIgnoreCase)).Select(h => h.MapId);
        if (RunePurchasePlan.Nearest(markets, x, y, launch.MapCoordOf) is not { } hdv) return (0, "aucun HDV de runes localisé");

        var host = NewFollowHost((int)pid);
        int bought = 0;
        try
        {
            if (!await host.GoToHdvAndOpenBuyerAsync(hdv, ct).ConfigureAwait(false)) return (0, "HDV de runes non ouvert en mode achat");
            // Prix de référence : moyenne du serveur, sinon prix unitaire lu ici même (mode Socket : moyennes jamais reçues).
            var reference = new Dictionary<long, long>();
            var quotes = new Dictionary<(long Gid, int Lot), long>();
            foreach (var gid in needs.Select(n => n.Gid).Distinct())
            {
                long average = launch.AveragePrice(pid, (int)gid);
                long live;
                if (quoteLots)
                {
                    var prices = await launch.HdvGetBuyerLotPricesAsync(pid, gid, ct).ConfigureAwait(false);
                    live = prices.Length > 0 ? prices[0] : -1;
                    foreach (int lot in new[] { 100, 10, 1 })
                    {
                        int slot = BidHouseStore.SlotOf(lot);
                        quotes[(gid, lot)] = slot < prices.Length ? prices[slot] : -1;
                    }
                }
                else live = average > 0 ? 0 : await launch.HdvGetLotPriceAsync(pid, gid, 1, ct).ConfigureAwait(false);
                reference[gid] = RunePurchasePlan.Reference(average, live);
            }
            var plan = planner(gid => reference.GetValueOrDefault(gid), (gid, lot) => quotes.GetValueOrDefault((gid, lot)));
            if (plan.Count == 0) { launch.LeaveDialog(pid); await host.TravelToMapAsync(here, ct).ConfigureAwait(false); return (0, "aucune offre de rune sous le plafond, ou budget nul"); }
            foreach (var p in plan)
                for (int i = 0; i < p.Count && !ct.IsCancellationRequested; i++)
                {
                    if (!await launch.HdvBuyByGidAsync(pid, p.Gid, p.Lot, p.MaxLotPrice, ct).ConfigureAwait(false)) break;   // plus d'offre sous le plafond
                    bought += p.Lot;
                }
            launch.LeaveDialog(pid);
            bool back = await host.TravelToMapAsync(here, ct).ConfigureAwait(false);
            back = back && await WaitSmithWorkshopDataAsync(pid, here, ct).ConfigureAwait(false);
            return (back ? bought : 0, back ? $"{bought} rune(s) achetée(s), retour à l'atelier" : $"{bought} rune(s) achetée(s), retour à l'atelier ÉCHOUÉ");
        }
        finally { try { host.Dispose(); } catch { } }
    }
}
