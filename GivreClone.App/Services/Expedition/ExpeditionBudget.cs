using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Expedition;

/// <summary>
/// Matériau nécessaire à chaque passage : l'inventaire sac/banque est un état
/// observé et le prix un relevé optionnel, jamais extrapolé à partir du GID.
/// </summary>
public sealed record ExpeditionMaterial(
    long Gid, string Name, long PerRun, long InBag, long InBank,
    decimal? ObservedUnitPrice = null, DateTimeOffset? PriceObservedAt = null);

public sealed record ExpeditionBudgetLine(
    long Gid, string Name, long Required, long Available, long Missing,
    decimal? UnitPrice, decimal? MissingCost, bool QuoteFresh);

public sealed record ExpeditionBudgetResult(
    int Runs, bool Unlimited,
    IReadOnlyList<ExpeditionBudgetLine> Lines,
    decimal KnownSubtotal, decimal? FullEstimate,
    bool RequiresPriceObservations, bool RequiresMaterials);

/// <summary>
/// Planification locale pure, ne déclenche aucun achat ni script. Une dépense
/// totale est inconnue si le prix d'au moins un matériau manquant est absent.
/// Si runs vaut 0 (mode sans limite), les besoins sont donnés PAR PASSAGE,
/// sans prétendre connaître le total de l'expédition.
/// </summary>
public static class ExpeditionBudget
{
    public static ExpeditionBudgetResult Estimate(
        IEnumerable<ExpeditionMaterial> materials, int runs,
        DateTimeOffset observedNow, TimeSpan? maxQuoteAge = null)
    {
        ArgumentNullException.ThrowIfNull(materials);
        if (runs < 0 || runs > 100_000) throw new ArgumentOutOfRangeException(nameof(runs));
        var ttl = maxQuoteAge ?? TimeSpan.FromHours(24);
        if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxQuoteAge));
        bool unlimited = runs == 0;
        int count = unlimited ? 1 : runs;
        var lines = new List<ExpeditionBudgetLine>();

        // Grouper avant soustraction : une même pile de clé ne doit jamais être
        // comptée deux fois si plusieurs salles l'exigent.
        foreach (var group in materials.GroupBy(m => m.Gid).OrderBy(g => g.Key))
        {
            if (group.Key <= 0) throw new ArgumentException("GID invalide.", nameof(materials));
            var entries = group.ToArray();
            if (entries.Any(x => x.PerRun < 0 || x.InBag < 0 || x.InBank < 0 || x.ObservedUnitPrice < 0))
                throw new ArgumentException("Quantité ou prix négatif.", nameof(materials));
            var first = entries[0];
            if (entries.Any(x => x.InBag != first.InBag || x.InBank != first.InBank ||
                x.ObservedUnitPrice != first.ObservedUnitPrice || x.PriceObservedAt != first.PriceObservedAt))
                throw new ArgumentException("Stocks ou prix contradictoires pour un même GID.", nameof(materials));
            long perRun = entries.Aggregate(0L, (acc, x) => checked(acc + x.PerRun));
            long required = checked(perRun * count);
            long available = checked(first.InBag + first.InBank);
            long missing = Math.Max(0, required - available);
            bool fresh = first.ObservedUnitPrice is >= 0 && first.PriceObservedAt is { } at &&
                         at <= observedNow && observedNow - at <= ttl;
            decimal? cost = missing == 0 ? 0m : fresh ? checked(missing * first.ObservedUnitPrice!.Value) : null;
            lines.Add(new(first.Gid, first.Name, required, available, missing,
                fresh ? first.ObservedUnitPrice : null, cost, fresh));
        }

        decimal subtotal = lines.Where(l => l.MissingCost is not null).Sum(l => l.MissingCost ?? 0);
        bool anyUnknown = lines.Any(l => l.Missing > 0 && l.MissingCost is null);
        bool anyMissing = lines.Any(l => l.Missing > 0);
        return new(runs, unlimited, lines, subtotal,
            unlimited || anyUnknown ? null : subtotal,
            anyUnknown, anyMissing);
    }
}
