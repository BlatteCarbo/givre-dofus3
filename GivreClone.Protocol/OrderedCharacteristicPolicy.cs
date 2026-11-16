using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace GivreClone.Protocol;

/// <summary>Base cible, sans bonus d'équipement/parchemins ; null = tout le budget restant.</summary>
public sealed record CharacteristicTarget(string Stat = "strength", int? Cap = 100);

public sealed record CharacteristicSpend(string Stat, int Target, int Cost, IReadOnlyDictionary<string, int> Allocation);

/// <summary>Plan pur : première cible non satisfaite, coûts BreedData, aucune dépense si les données sont incomplètes.</summary>
public static class OrderedCharacteristicPolicy
{
    private sealed record Tier(int Base, int Cost, int Gain);

    public static CharacteristicSpend? Next(IReadOnlyDictionary<int, int> bases, int budget,
        IReadOnlyList<CharacteristicTarget> targets, IReadOnlyDictionary<string, string> breedCosts)
    {
        if (budget <= 0 || targets.Count == 0
            || targets.Any(t => t is null || t.Cap < 0 || !CharacteristicUpgradePolicy.Stats.Any(s => s.Name == t.Stat))) return null;
        var tables = new Dictionary<string, List<Tier>>();
        var allocation = new Dictionary<string, int>();
        foreach (var (name, id) in CharacteristicUpgradePolicy.Stats)
        {
            if (!bases.TryGetValue(id, out int basis) || basis < 0 || !breedCosts.TryGetValue(name, out var json)
                || ReadTiers(json) is not { } tiers || Invested(tiers, basis) is not int invested) return null;
            tables[name] = tiers;
            allocation[name] = invested;
        }
        foreach (var target in targets)
        {
            int id = CharacteristicUpgradePolicy.Stats.First(s => s.Name == target.Stat).Id;
            int basis = bases[id], cap = target.Cap ?? int.MaxValue;
            if (basis >= cap) continue;
            int next = basis, spent = 0;
            var tiers = tables[target.Stat];
            while (next < cap)
            {
                var tier = tiers.Last(t => t.Base <= next);
                if (tier.Cost > budget - spent || tier.Gain > cap - next) break;
                int following = next + tier.Gain;
                if (tiers.Any(t => t.Base > next && t.Base < following)) return null;
                next = following;
                spent += tier.Cost;
            }
            // Ne pas contourner une cible prioritaire qu'on ne peut pas encore payer.
            if (next == basis) return null;
            if (allocation[target.Stat] > int.MaxValue - spent) return null;
            allocation[target.Stat] += spent;
            return new CharacteristicSpend(target.Stat, next, spent, allocation);
        }
        return null;
    }

    private static List<Tier>? ReadTiers(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var tiers = new List<Tier>();
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() is < 2 or > 3
                    || !row[0].TryGetInt32(out int basis) || basis < 0
                    || (tiers.Count == 0 ? basis != 0 : basis <= tiers[^1].Base)
                    || !row[1].TryGetInt32(out int cost) || cost <= 0) return null;
                int gain = 1;
                if (row.GetArrayLength() == 3 && (!row[2].TryGetInt32(out gain) || gain <= 0)) return null;
                tiers.Add(new Tier(basis, cost, gain));
            }
            return tiers.Count == 0 ? null : tiers;
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static int? Invested(List<Tier> tiers, int basis)
    {
        long total = 0;
        for (int i = 0; i < tiers.Count; i++)
        {
            var tier = tiers[i];
            int end = i + 1 < tiers.Count ? Math.Min(basis, tiers[i + 1].Base) : basis;
            if (end <= tier.Base) break;
            int points = end - tier.Base;
            if (points % tier.Gain != 0) return null;
            total += (long)(points / tier.Gain) * tier.Cost;
            if (total > int.MaxValue) return null;
        }
        return (int)total;
    }
}
