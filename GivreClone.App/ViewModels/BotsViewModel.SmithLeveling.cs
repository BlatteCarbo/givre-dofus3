using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services;
using GivreClone.App.Services.Eco;
using GivreClone.Engine.Recovery;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

public partial class BotsViewModel
{
    private async Task<(long Uid, string Report)> BuyLevelingSupportAsync(uint pid, int jobId, int professionLevel,
        long absoluteKamasFloor, IReadOnlySet<long>? allowedSupportGids, IReadOnlySet<long>? allowedRuneGids,
        CancellationToken ct)
    {
        var launch = Stores.Launch;
        if (!LicenseService.HasPermission(LicenseService.Perm.Hdv)) return (0, "droit Hotel de Vente manquant");
        if (_luaHosts.TryGetValue((int)pid, out var running) && running.Running) return (0, "script deja en cours");
        if (SmithSupportPurchase.Cap(launch.Kamas(pid), absoluteKamasFloor) <= 0) return (0, "budget support epuise ou inconnu");
        if (launch.GameData is not { } db) return (0, "donnees du jeu absentes");
        long origin = launch.CurrentMap(pid) ?? 0;
        if (origin <= 0 || launch.MapCoordOf(origin) is not (int x, int y)) return (0, "position du perso inconnue");
        var equipmentMarkets = HdvLocations.All.Where(market => market.IconKey == "auctionHouseEquipments").Select(market => market.MapId);
        if (RunePurchasePlan.Nearest(equipmentMarkets, x, y, launch.MapCoordOf) is not { } marketMap)
            return (0, "aucun HDV d'equipements localise");

        // Average prices only prioritize a bounded search; missing averages never authorize a purchase.
        var candidates = db.SmithLevelingSupports(jobId, professionLevel)
            .Where(item => allowedSupportGids is null || allowedSupportGids.Contains(item.Gid))
            .OrderBy(item => professionLevel - item.Level < 5 ? 0 : professionLevel - item.Level <= 8 ? 1 : 2)
            .ThenBy(item => launch.AveragePrice(pid, (int)item.Gid) > 0 ? 0 : 1)
            .ThenBy(item => launch.AveragePrice(pid, (int)item.Gid) > 0 ? launch.AveragePrice(pid, (int)item.Gid) : long.MaxValue)
            .ThenByDescending(item => item.Level).Take(6).ToArray();
        if (candidates.Length == 0) return (0, "aucun support compatible avec ce metier et ce niveau");

        var host = NewFollowHost((int)pid);
        using var budgetStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        void StopAtFloor(uint source, long _, int? __, int? ___)
        {
            if (source == pid && launch.Kamas(pid) <= absoluteKamasFloor) budgetStop.Cancel();
        }
        launch.MapChanged += StopAtFloor;
        long acquired = 0;
        string report = "aucune offre de support compatible sous le plafond";
        bool back = false;
        bool ValidEffects(IReadOnlyList<InventoryReader.ItemEffect> effects)
            => ItemBindingRules.CanTrade(ItemBindingRules.Classify(effects.Select(effect => effect.ActionId)))
                && !effects.Any(effect => effect.ActionId == 2825)
                // Only supports with a line served by cheap base runes (Fo/Cha/Ine/Age/Sa/Vi/Ini/Pod), not e.g. damage only.
                && effects.Any(effect => effect.ActionId is 118 or 123 or 126 or 119 or 124 or 125 or 174 or 158);
        long? RuneCashCost(IReadOnlyList<InventoryReader.ItemEffect> effects)
        {
            var native = effects.Select(effect => effect.ActionId).ToHashSet();
            var runes = launch.RuneItemsByGid().Where(r => r.Value.Tier == "Ba"
                && (allowedRuneGids is null || allowedRuneGids.Contains(r.Key))
                && r.Value.EffectId is 118 or 123 or 126 or 119 or 124 or 125 or 174 or 158
                && native.Contains(r.Value.EffectId));
            // A bag sample is already funded. Otherwise this is only a five-rune
            // planning estimate; the rune buyer still requires live lot prices.
            return runes.Select(r =>
                {
                    if (launch.Inventory.Items(pid).Any(item => item.Position == 63 && item.Gid == r.Key && item.Qty >= 5))
                        return (long?)0;
                    long price = launch.AveragePrice(pid, (int)r.Key);
                    return price > 0 && price <= long.MaxValue / 5 ? price * 5 : (long?)null;
                })
                .Where(cost => cost.HasValue).OrderBy(cost => cost).FirstOrDefault();
        }
        try
        {
            if (!await host.GoToHdvAndOpenBuyerAsync(marketMap, budgetStop.Token).ConfigureAwait(false))
                report = "HDV d'equipements non ouvert en mode achat";
            else
            {
                var offers = new List<SmithSupportOffer>();
                var inspected = new List<string>();
                foreach (var candidate in candidates)
                {
                    if (SmithSupportPurchase.Cap(launch.Kamas(pid), absoluteKamasFloor) <= 0) break;
                    var rows = await launch.HdvGetBuyerOffersAsync(pid, candidate.Gid, budgetStop.Token).ConfigureAwait(false);
                    int beforeCount = offers.Count;
                    foreach (var offer in rows)
                        if (offer.Gid == candidate.Gid && offer.Prices.Length > 0 && ValidEffects(offer.Effects))
                            offers.Add(new(candidate.Gid, candidate.Level, offer.Uid, offer.Prices[0], RuneCashCost(offer.Effects)));
                    inspected.Add($"{candidate.Gid}: {rows.Count} offres, {offers.Count - beforeCount} admises");
                }
                var selected = SmithSupportPurchase.Rank(offers, professionLevel, launch.Kamas(pid), absoluteKamasFloor).FirstOrDefault();
                if (selected is null) report += " (" + string.Join("; ", inspected) + ")";
                if (selected is not null)
                {
                    // Re-read the exact lot after comparison, then check real cash immediately before buying.
                    var fresh = await launch.HdvGetBuyerOffersAsync(pid, selected.Gid, budgetStop.Token).ConfigureAwait(false);
                    // The getter already waits for fresh offer publication. An empty
                    // response cannot be made trustworthy by waiting a fixed delay.
                    var lot = fresh.FirstOrDefault(offer => offer.Uid == selected.Uid && offer.Gid == selected.Gid
                        && offer.Prices.Length > 0 && offer.Prices[0] == selected.Price && ValidEffects(offer.Effects));
                    report = lot is null
                        ? $"offre {selected.Gid}/{selected.Uid} a {selected.Price} non retrouvee a la revalidation ({fresh.Count} offres)"
                        : "plafond support depasse a la revalidation";
                    if (lot is not null && SmithSupportPurchase.Rank(
                        [selected with { RuneCashCost = RuneCashCost(lot.Effects) }], professionLevel,
                        launch.Kamas(pid), absoluteKamasFloor).Count > 0)
                    {
                        var before = launch.Inventory.Items(pid).ToDictionary(item => item.Uid, item => item.Qty);
                        var received = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
                        long FindPurchased() => SmithSupportPurchase.AcquiredUid(selected.Gid, before,
                            launch.Inventory.Items(pid).Select(item => (item.Uid, item.Gid, item.Qty,
                                item.Position == 63 && ValidEffects(launch.ItemEffects(pid, item.Uid))
                                    && launch.ItemEffects(pid, item.Uid).SequenceEqual(lot.Effects))));
                        void OnBag(uint source)
                        {
                            if (source != pid) return;
                            long uid = FindPurchased();
                            if (uid > 0) received.TrySetResult(uid);
                        }
                        launch.HdvUpdated += OnBag;
                        try
                        {
                            if (await launch.HdvBuyAsync(pid, lot.Uid, 1, lot.Prices[0], budgetStop.Token).ConfigureAwait(false))
                            {
                                OnBag(pid);
                                try { acquired = await received.Task.WaitAsync(TimeSpan.FromSeconds(8), budgetStop.Token).ConfigureAwait(false); }
                                catch (TimeoutException) { report = "achat confirme mais UID admis absent du sac"; }
                                if (acquired > 0) report = $"support niveau {selected.Level} achete pour {lot.Prices[0]} kamas";
                            }
                            else report = "achat du support non confirme";
                        }
                        finally { launch.HdvUpdated -= OnBag; }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && budgetStop.IsCancellationRequested)
        {
            report = "plancher de kamas atteint pendant le trajet";
        }
        finally
        {
            try
            {
                launch.LeaveDialog(pid);
                if (!budgetStop.IsCancellationRequested && launch.Kamas(pid) > absoluteKamasFloor)
                {
                    try
                    {
                        back = await host.TravelToMapAsync(origin, budgetStop.Token).ConfigureAwait(false)
                            && await WaitSmithWorkshopDataAsync(pid, origin, budgetStop.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested && budgetStop.IsCancellationRequested)
                    {
                        report += ", plancher de kamas atteint au retour";
                    }
                }
            }
            finally { launch.MapChanged -= StopAtFloor; host.Dispose(); }
        }
        return (back ? acquired : 0, report + (back ? ", retour a l'atelier" : ", retour a l'atelier non confirme"));
    }
}
