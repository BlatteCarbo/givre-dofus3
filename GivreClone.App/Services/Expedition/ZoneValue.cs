using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Expedition;

public sealed record MonsterDrop(long Gid, double Percent);
public sealed record MonsterDropSet(int MonsterId, string Name, IReadOnlyList<MonsterDrop> Drops);
public sealed record MonsterValue(int MonsterId, string Name, decimal Value, int UnpricedDrops);
public sealed record ZoneValueResult(decimal AveragePerMonster, int Monsters, int UnpricedDrops, IReadOnlyList<MonsterValue> Best);

public sealed record DungeonRunValue(decimal? KeyCost, int UnpricedKeys, decimal BossDropValue, int UnpricedDrops)
{
    /// <summary>Gain net par passage (drops des boss − clés) ; null si le coût des clés est incomplet.</summary>
    public decimal? Net => KeyCost is { } c ? BossDropValue - c : null;
}

/// <summary>Rentabilité d'un passage de donjon : clés consommées (prix HDV) contre drops attendus des boss.</summary>
public static class DungeonProfit
{
    public static DungeonRunValue Estimate(IEnumerable<(long Gid, int Quantity)> keys, IEnumerable<MonsterDropSet> bosses, Func<long, decimal?> unitPrice)
    {
        decimal cost = 0; int unpricedKeys = 0;
        foreach (var (gid, qty) in keys)
        {
            if (qty <= 0) continue;
            if (unitPrice(gid) is { } p and > 0) cost += p * qty; else unpricedKeys++;
        }
        var values = bosses.Select(b => ZoneValue.ForMonster(b, unitPrice)).ToList();
        return new(unpricedKeys == 0 ? cost : null, unpricedKeys, values.Sum(v => v.Value), values.Sum(v => v.UnpricedDrops));
    }
}

/// <summary>Valeur HDV attendue des drops d'une zone, par monstre tué (taux de drop du grade 1 × prix connu). Pure : ne demande aucun prix,
/// ignore les drops sans prix (comptés à part) et ne compte pas les kamas ni la prospection.</summary>
public static class ZoneValue
{
    public static MonsterValue ForMonster(MonsterDropSet m, Func<long, decimal?> unitPrice)
    {
        decimal total = 0; int unpriced = 0;
        foreach (var d in m.Drops)
        {
            if (d.Percent <= 0) continue;
            if (unitPrice(d.Gid) is { } p and > 0) total += p * (decimal)Math.Min(d.Percent, 100) / 100m;
            else unpriced++;
        }
        return new(m.MonsterId, m.Name, total, unpriced);
    }

    /// <summary>Moyenne simple sur les monstres retenus (sans pondération par fréquence d'apparition, inconnue).</summary>
    public static ZoneValueResult? ForZone(IEnumerable<MonsterDropSet> monsters, Func<long, decimal?> unitPrice, int bestCount = 3)
    {
        var values = monsters.Select(m => ForMonster(m, unitPrice)).ToList();
        if (values.Count == 0) return null;
        return new(values.Average(v => v.Value), values.Count, values.Sum(v => v.UnpricedDrops),
            values.OrderByDescending(v => v.Value).ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase).Take(Math.Max(0, bestCount)).ToArray());
    }
}
