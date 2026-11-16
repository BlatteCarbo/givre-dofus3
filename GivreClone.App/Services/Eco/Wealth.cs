using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Eco;

public sealed record WealthItem(long Gid, string Name, long Quantity, decimal? Unit)
{
    public decimal? Total => Unit is { } u ? u * Quantity : null;
}

/// <param name="Total">Valeur estimée des objets dont le prix est connu.</param>
/// <param name="Unpriced">Nombre d'objets distincts sans prix connu (non comptés).</param>
public sealed record WealthReport(decimal Total, int Priced, int Unpriced, long Stacks, IReadOnlyList<WealthItem> Top);

/// <summary>Patrimoine estimé d'un sac ou d'une banque : quantités regroupées par objet × prix HDV connu (marché ou moyenne). Indicatif : un objet peu
/// échangé peut valoir moins que son prix affiché, et les objets sans prix ne sont pas comptés. Pure.</summary>
public static class Wealth
{
    public static WealthReport Compute(IEnumerable<(long Gid, string Name, long Quantity)> items, Func<long, decimal?> unitPrice, int top = 15)
    {
        var grouped = new Dictionary<long, (string Name, long Qty)>();
        long stacks = 0;
        foreach (var (gid, name, qty) in items)
        {
            if (qty <= 0) continue;
            stacks++;
            grouped[gid] = grouped.TryGetValue(gid, out var g) ? (g.Name, g.Qty + qty) : (name, qty);
        }
        var rows = grouped.Select(kv => new WealthItem(kv.Key, kv.Value.Name, kv.Value.Qty, unitPrice(kv.Key) is { } p and > 0 ? p : null)).ToList();
        decimal total = rows.Sum(r => r.Total ?? 0);
        return new WealthReport(total, rows.Count(r => r.Unit is not null), rows.Count(r => r.Unit is null), stacks,
            rows.Where(r => r.Total is not null).OrderByDescending(r => r.Total).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Take(Math.Max(0, top)).ToArray());
    }
}
