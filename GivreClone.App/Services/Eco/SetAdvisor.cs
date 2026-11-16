using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Eco;

public enum SetVerdict { Craft, Buy, Unknown }

public sealed record SetItemRow(long Gid, string Name, decimal? CraftCost, decimal? BuyCost, SetVerdict Verdict, decimal? Chosen);

/// <param name="TotalChosen">Coût de la panoplie en prenant, pour chaque pièce, le moins cher entre fabriquer et acheter (null si une pièce n'a aucun prix).</param>
public sealed record SetAdvice(IReadOnlyList<SetItemRow> Items, decimal? TotalChosen, decimal? TotalBuy, decimal? TotalCraft, int Unknown,
                               IReadOnlyList<(long Gid, string Name, long Quantity, decimal? Cost)> Shopping);

/// <summary>Coût d'une panoplie entière : pour chaque pièce, fabriquer ou acheter (le moins cher), totaux « tout acheter » / « tout fabriquer »
/// et liste de courses fusionnée (ingrédients à acheter pour les pièces fabriquées + pièces à acheter). Pure.</summary>
public static class SetAdvisor
{
    public static SetAdvice Analyze(IEnumerable<long> pieceGids, Func<long, CraftAdvice> adviceFor, Func<long, string> name)
    {
        var rows = new List<SetItemRow>();
        var shopping = new Dictionary<long, (string Name, long Qty, decimal? Cost)>();
        void Add(long gid, string n, long qty, decimal? cost)
        {
            var cur = shopping.TryGetValue(gid, out var v) ? v : (n, 0L, (decimal?)0);
            shopping[gid] = (n, cur.Item2 + qty, cur.Item3 is { } a && cost is { } c ? a + c : null);
        }
        foreach (var gid in pieceGids.Distinct())
        {
            var a = adviceFor(gid);
            decimal? craft = a.CraftCost, buy = a.BuyCost;
            SetVerdict v = (craft, buy) switch
            {
                ({ } c, { } b) => c < b ? SetVerdict.Craft : SetVerdict.Buy,
                ({ }, null) => SetVerdict.Craft,
                (null, { }) => SetVerdict.Buy,
                _ => SetVerdict.Unknown,
            };
            decimal? chosen = v switch { SetVerdict.Craft => craft, SetVerdict.Buy => buy, _ => null };
            rows.Add(new SetItemRow(gid, name(gid), craft, buy, v, chosen));
            if (v == SetVerdict.Craft) foreach (var s in CraftAdvisor.ShoppingList(a.Root)) Add(s.Gid, s.Name, s.Quantity, s.Cost);
            else if (v == SetVerdict.Buy) Add(gid, name(gid), 1, buy);
        }
        int unknown = rows.Count(r => r.Verdict == SetVerdict.Unknown);
        decimal? Sum(Func<SetItemRow, decimal?> f) => rows.Count > 0 && rows.All(r => f(r) is not null) ? rows.Sum(r => f(r)!.Value) : null;
        return new SetAdvice(rows, unknown == 0 && rows.Count > 0 ? rows.Sum(r => r.Chosen!.Value) : null, Sum(r => r.BuyCost), Sum(r => r.CraftCost), unknown,
            shopping.Select(kv => (kv.Key, kv.Value.Name, kv.Value.Qty, kv.Value.Cost)).OrderByDescending(x => x.Item4 ?? 0).ThenBy(x => x.Item2, StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
