using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services;

public sealed record ResaleCandidate(long Gid, string Name, int Quantity, long MarketUnit, long MarketAgeMs, long AverageUnit);
public sealed record ResaleHit(long Gid, string Name, int Quantity, long MarketUnit, long AverageUnit, double GainPercent, decimal NetTotal);

/// <summary>Objets du sac dont le prix HDV le plus bas observé dépasse nettement la moyenne du marché : à revendre maintenant.
/// Pure : n'interroge ni l'HDV ni le réseau, ne tient compte que de prix déjà en cache et assez récents.</summary>
public static class ResaleOpportunity
{
    public const long MaxAgeMs = 30 * 60_000;

    public static IReadOnlyList<ResaleHit> Find(IEnumerable<ResaleCandidate> items, double minGainPercent, long minNetTotal, double taxPercent)
    {
        double net = 1 - Math.Clamp(taxPercent, 0, 99) / 100.0;
        return items.Where(i => i.Quantity > 0 && i.MarketUnit > 0 && i.AverageUnit > 0 && i.MarketAgeMs >= 0 && i.MarketAgeMs <= MaxAgeMs)
            .Select(i => new ResaleHit(i.Gid, i.Name, i.Quantity, i.MarketUnit, i.AverageUnit,
                (i.MarketUnit - i.AverageUnit) * 100.0 / i.AverageUnit, (decimal)(i.MarketUnit * (double)i.Quantity * net)))
            .Where(h => h.GainPercent >= minGainPercent && h.NetTotal >= minNetTotal)
            .OrderByDescending(h => h.NetTotal).ToArray();
    }

    /// <summary>Anti-spam : réalerte si jamais alerté, si le prix a encore monté de 10 % depuis la dernière alerte, ou après 6 h.</summary>
    public static bool ShouldAlert(long lastAlertUnit, long lastAlertAtMs, long unit, long nowMs)
        => lastAlertUnit <= 0 || unit >= lastAlertUnit * 1.1 || nowMs - lastAlertAtMs >= 6 * 3_600_000L;
}
