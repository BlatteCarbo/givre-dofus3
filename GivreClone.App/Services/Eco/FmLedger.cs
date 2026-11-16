using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Eco;

public sealed class FmPurchase { public string Rune { get; set; } = ""; public long Quantity { get; set; } public decimal UnitPrice { get; set; } }
public sealed class FmUse { public string Rune { get; set; } = ""; public int Count { get; set; } public string Note { get; set; } = ""; }

public sealed class FmSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Item { get; set; } = "";
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public bool Paused { get; set; }
    public bool Archived { get; set; }
    public string Notes { get; set; } = "";
    public List<FmPurchase> Purchases { get; set; } = new();
    public List<FmUse> Uses { get; set; } = new();
}

public sealed record FmRuneLine(string Rune, long Bought, decimal AveragePrice, long Used, long Stock);
public sealed record FmSummary(decimal TotalSpent, long RunesBought, long RunesUsed, decimal? CostPerUse, IReadOnlyList<FmRuneLine> Runes);

/// <summary>Suivi d'une session de forgemagie (comme la page FM de Dofus-Craft, en local) : achats de runes, runes utilisées, dépense totale,
/// prix moyen par rune, coût par tentative, stock restant. Pure ; la persistance JSON est dans <see cref="FmLedgerStore"/>.</summary>
public static class FmLedger
{
    public static FmSummary Summarize(FmSession s)
    {
        var lines = new List<FmRuneLine>();
        var names = s.Purchases.Select(p => p.Rune).Concat(s.Uses.Select(u => u.Rune)).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        foreach (var n in names)
        {
            var buys = s.Purchases.Where(p => string.Equals(p.Rune, n, StringComparison.OrdinalIgnoreCase) && p.Quantity > 0).ToList();
            long bought = buys.Sum(b => b.Quantity);
            decimal avg = bought > 0 ? buys.Sum(b => b.UnitPrice * b.Quantity) / bought : 0;
            long used = s.Uses.Where(u => string.Equals(u.Rune, n, StringComparison.OrdinalIgnoreCase)).Sum(u => (long)Math.Max(0, u.Count));
            lines.Add(new FmRuneLine(n, bought, Math.Round(avg, 2), used, bought - used));
        }
        decimal spent = s.Purchases.Where(p => p.Quantity > 0 && p.UnitPrice >= 0).Sum(p => p.UnitPrice * p.Quantity);
        long usedTotal = lines.Sum(l => l.Used);
        // Coût de la dépense réellement consommée : prix moyen × runes utilisées (le stock restant n'est pas perdu).
        decimal consumed = lines.Sum(l => l.AveragePrice * l.Used);
        return new FmSummary(spent, lines.Sum(l => l.Bought), usedTotal, usedTotal > 0 ? consumed / usedTotal : null, lines);
    }

    /// <summary>Prix médian d'une liste ; null si vide.</summary>
    public static decimal? Median(IEnumerable<decimal> values)
    {
        var v = values.OrderBy(x => x).ToList();
        if (v.Count == 0) return null;
        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }

    /// <summary>Un prix saisi à plus de `tolerancePercent` % de la médiane connue est suspect (même garde-fou que Dofus-Craft : 25 %).</summary>
    public static bool IsOutlier(decimal price, decimal? median, double tolerancePercent = 25)
        => median is > 0 && Math.Abs((double)((price - median.Value) / median.Value)) * 100 > tolerancePercent;
}
