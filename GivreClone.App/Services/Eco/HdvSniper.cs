using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services.Eco;

/// <summary>Lot surveillé. <see cref="MaxPrice"/> ≤ 0 : le lot n'est pas snipé (pas d'achat sans plafond).</summary>
public sealed record HdvSniperLot(int Quantity, long MaxPrice, bool Allowed);

/// <summary>Objet surveillé. <see cref="QuantityCap"/> null = sans limite ; 0 = rien à acheter ; &gt; 0 = unités restantes.</summary>
public sealed record HdvSniperItem(long Gid, int? QuantityCap, IReadOnlyList<HdvSniperLot> Lots);

/// <summary>
/// <see cref="CeilingPercent"/> est le plafond du réglage affiché (« au plus N % du prix moyen ») : le prix unitaire doit être
/// inférieur ou égal à ce pourcentage du prix moyen serveur. Ce n'est pas un rabais « N % sous » le prix.
/// </summary>
public sealed record HdvSniperConfig(int CeilingPercent, int WaitMinSec, int WaitMaxSec, long Budget);

public readonly record struct HdvSniperPurchase(long Gid, int Quantity, long Price, DateTimeOffset At);

public sealed record HdvSniperResult(int BoughtUnits, long Spent, string Stop, IReadOnlyList<HdvSniperPurchase> Purchases);

public interface IHdvSniperClock
{
    DateTimeOffset UtcNow { get; }
    Task DelayAsync(TimeSpan delay, CancellationToken ct);
}

/// <summary>Borne inclusive. L'implémentation de test renvoie une valeur imposée dans l'intervalle demandé.</summary>
public interface IHdvSniperRandom
{
    int NextInclusive(int minInclusive, int maxInclusive);
}

/// <summary>
/// Prix relu à la demande, prix moyen déjà connu, kamas, achat.
/// <see cref="ConfirmedBuyAsync"/> ne renvoie vrai que sur un accusé serveur (reçu d'achat), jamais sur une simple acceptation.
/// </summary>
public interface IHdvSniperPort
{
    Task<long> FreshLotPriceAsync(long gid, int quantity, CancellationToken ct);
    long ReferenceUnit(long gid);
    long Kamas();
    Task<bool> ConfirmedBuyAsync(long gid, int quantity, long price, CancellationToken ct);
}

/// <summary>
/// Surveille une liste d'achat : un lot autorisé avec plafond est acheté quand son prix unitaire vaut au plus
/// <see cref="HdvSniperConfig.CeilingPercent"/> % du prix moyen. Le prix est relu une seconde fois juste avant l'achat.
/// Le budget total n'est jamais dépassé. L'attente entre deux tours vient de <see cref="IHdvSniperRandom"/> et de l'horloge injectée.
/// </summary>
public static class HdvSniper
{
    public static bool HasDemand(IReadOnlyList<HdvSniperItem>? rows)
        => rows?.Any(row => (row.QuantityCap is null || row.QuantityCap > 0) && Eligible(row).Any()) == true;

    /// <summary>Prix du lot ≤ plafond, et prix unitaire ≤ plafond % du prix moyen. Égalité comprise (« au plus »).</summary>
    public static bool Qualifies(long lotPrice, int quantity, long maxPrice, long referenceUnit, int ceilingPercent)
    {
        if (lotPrice <= 0 || quantity <= 0 || maxPrice <= 0 || lotPrice > maxPrice) return false;
        if (referenceUnit <= 0 || ceilingPercent <= 0) return false;
        int percent = Math.Min(ceilingPercent, 100);
        try
        {
            return checked(lotPrice * 100L) <= checked(referenceUnit * quantity * percent);
        }
        catch (OverflowException) { return false; }
    }

    public static int WaitSeconds(int minSec, int maxSec, IHdvSniperRandom random)
    {
        if (random is null) throw new ArgumentNullException(nameof(random));
        int lo = Math.Max(0, Math.Min(minSec, maxSec));
        int hi = Math.Max(0, Math.Max(minSec, maxSec));
        return random.NextInclusive(lo, hi);
    }

    public static async Task<HdvSniperResult> WatchAsync(
        IHdvSniperPort port,
        IHdvSniperClock clock,
        IHdvSniperRandom random,
        HdvSniperConfig config,
        Func<IReadOnlyList<HdvSniperItem>> items,
        Action<HdvSniperPurchase>? onConfirmed,
        CancellationToken ct)
    {
        if (port is null) throw new ArgumentNullException(nameof(port));
        if (clock is null) throw new ArgumentNullException(nameof(clock));
        if (random is null) throw new ArgumentNullException(nameof(random));
        if (items is null) throw new ArgumentNullException(nameof(items));
        if (config.Budget <= 0) return Finish(0, 0, "budget non défini", Array.Empty<HdvSniperPurchase>());
        if (config.CeilingPercent <= 0) return Finish(0, 0, "pourcentage sniper nul", Array.Empty<HdvSniperPurchase>());

        long spent = 0;
        int bought = 0;
        var purchases = new List<HdvSniperPurchase>();
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (config.Budget - spent <= 0) return Finish(bought, spent, "budget atteint", purchases);
                var snapshot = items() ?? Array.Empty<HdvSniperItem>();
                if (!HasDemand(snapshot))
                    return Finish(bought, spent, bought > 0 ? "quantité atteinte" : "rien à surveiller", purchases);

                long spentAtStart = spent;
                bool shortKamas = false;
                foreach (var item in snapshot)
                {
                    int remaining = item.QuantityCap ?? int.MaxValue;
                    if (remaining <= 0) continue;
                    foreach (var lot in Eligible(item))
                    {
                        while (remaining >= lot.Quantity)
                        {
                            ct.ThrowIfCancellationRequested();
                            long left = config.Budget - spent;
                            if (left <= 0) return Finish(bought, spent, "budget atteint", purchases);

                            long reference = port.ReferenceUnit(item.Gid);
                            long first = await port.FreshLotPriceAsync(item.Gid, lot.Quantity, ct).ConfigureAwait(false);
                            if (!Qualifies(first, lot.Quantity, lot.MaxPrice, reference, config.CeilingPercent)) break;

                            long freshReference = port.ReferenceUnit(item.Gid);
                            long fresh = await port.FreshLotPriceAsync(item.Gid, lot.Quantity, ct).ConfigureAwait(false);
                            if (!Qualifies(fresh, lot.Quantity, lot.MaxPrice, freshReference, config.CeilingPercent)) break;
                            if (fresh > left) break;

                            long kamas = port.Kamas();
                            if (kamas >= 0 && kamas < fresh) { shortKamas = true; break; }

                            ct.ThrowIfCancellationRequested();
                            bool confirmed = await port.ConfirmedBuyAsync(item.Gid, lot.Quantity, fresh, ct).ConfigureAwait(false);
                            if (!confirmed) return Finish(bought, spent, "achat non confirmé", purchases);

                            spent += fresh;
                            bought += lot.Quantity;
                            if (item.QuantityCap is not null) remaining -= lot.Quantity;
                            var purchase = new HdvSniperPurchase(item.Gid, lot.Quantity, fresh, clock.UtcNow);
                            purchases.Add(purchase);
                            onConfirmed?.Invoke(purchase);
                        }
                    }
                }

                if (config.Budget - spent <= 0) return Finish(bought, spent, "budget atteint", purchases);
                if (shortKamas && spent == spentAtStart) return Finish(bought, spent, "pas assez de kamas", purchases);
                if (!HasDemand(items() ?? Array.Empty<HdvSniperItem>()))
                    return Finish(bought, spent, "quantité atteinte", purchases);

                int seconds = WaitSeconds(config.WaitMinSec, config.WaitMaxSec, random);
                await clock.DelayAsync(TimeSpan.FromSeconds(seconds), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return Finish(bought, spent, "arrêté", purchases);
        }
    }

    private static HdvSniperResult Finish(int bought, long spent, string stop, IReadOnlyList<HdvSniperPurchase> purchases)
        => new(bought, spent, stop, purchases.ToArray());

    private static IEnumerable<HdvSniperLot> Eligible(HdvSniperItem item)
        => (item.Lots ?? Array.Empty<HdvSniperLot>()).Where(lot => lot.Allowed && lot.Quantity > 0 && lot.MaxPrice > 0)
            .OrderByDescending(lot => lot.Quantity);
}
