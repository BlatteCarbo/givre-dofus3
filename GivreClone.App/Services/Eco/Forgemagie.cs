using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Eco;

/// <summary>Une caractéristique d'un objet : id d'effet (EffectData.id), nom affiché, valeur, poids par point (EffectData.effectPowerRate).</summary>
public sealed record FmStat(int EffectId, string Name, int Value, decimal RatePerPoint)
{
    public decimal Weight => Value * RatePerPoint;
}

/// <summary>Une rune : code (« Ra Fo »), caractéristique, palier (Ba / Pa / Ra), id d'effet, quantité de points ajoutés.</summary>
public sealed record RuneDef(string Name, string Stat, string Tier, int EffectId, int Amount);

public sealed record RuneMix(int Ra, int Pa, int Ba)
{
    public int Count => Ra + Pa + Ba;
}

/// <summary>Poids de forgemagie. Les poids PAR POINT viennent des données du jeu (EffectData.effectPowerRate : PA 100, PM 90, Portée 51, Invocation 30,
/// Dommages 20, Sagesse 3, Force 1, Vitalité 0,2…), pas du code. Les quantités des runes (Ba = base ; Pa = 3× ; Ra = 10×) sont celles des runes
/// de base du jeu pour les caractéristiques listées ; les autres runes (Puissance, Prospection, Pièges…) ne sont volontairement pas calculées.
/// Aucune formule de réussite n'est incluse : elle dépend des jets de l'objet (non disponibles hors ligne).</summary>
public static class Forgemagie
{
    // (stat, effectId, quantité de la rune de base). Pa = ×3, Ra = ×10.
    private static readonly (string Stat, int EffectId, int Base)[] Families =
    {
        ("Fo", 118, 1), ("Ine", 126, 1), ("Cha", 123, 1), ("Age", 119, 1), ("Sa", 124, 1), ("Vi", 125, 5), ("Pod", 158, 10), ("Ini", 174, 10),
    };

    public static IReadOnlyList<RuneDef> Runes { get; } = Families.SelectMany(f => new[]
    {
        new RuneDef($"Rune {f.Stat}", f.Stat, "Ba", f.EffectId, f.Base),
        new RuneDef($"Rune Pa {f.Stat}", f.Stat, "Pa", f.EffectId, f.Base * 3),
        new RuneDef($"Rune Ra {f.Stat}", f.Stat, "Ra", f.EffectId, f.Base * 10),
    }).ToArray();

    public static decimal TotalWeight(IEnumerable<FmStat> stats) => stats.Sum(s => s.Weight);

    public static FmStat[] Positive(IEnumerable<FmStat> stats) => stats.Where(s => s.RatePerPoint > 0 && s.Value > 0).ToArray();

    /// <summary>Poids ajouté par UNE rune.</summary>
    public static decimal RuneWeight(RuneDef rune, decimal ratePerPoint) => rune.Amount * ratePerPoint;

    /// <summary>Décomposition en runes d'une hausse de `points` (greedy : Ra, puis Pa, puis Ba). Reste non couvert si `points` n'est pas un multiple de la rune de base.</summary>
    public static (RuneMix Mix, int Remainder)? Decompose(int effectId, int points)
    {
        if (points <= 0) return null;
        var ba = Runes.FirstOrDefault(r => r.EffectId == effectId && r.Tier == "Ba");
        if (ba is null) return null;
        int ra = Runes.First(r => r.EffectId == effectId && r.Tier == "Ra").Amount, pa = Runes.First(r => r.EffectId == effectId && r.Tier == "Pa").Amount;
        int nRa = points / ra; points -= nRa * ra;
        int nPa = points / pa; points -= nPa * pa;
        int nBa = points / ba.Amount; points -= nBa * ba.Amount;
        return (new RuneMix(nRa, nPa, nBa), points);
    }

    /// <summary>Coût d'un mélange de runes avec les prix unitaires donnés (null si un prix manque).</summary>
    public static decimal? MixCost(int effectId, RuneMix mix, Func<string, decimal?> unitPrice)
    {
        decimal total = 0;
        foreach (var (tier, n) in new[] { ("Ra", mix.Ra), ("Pa", mix.Pa), ("Ba", mix.Ba) })
        {
            if (n == 0) continue;
            var rune = Runes.First(r => r.EffectId == effectId && r.Tier == tier);
            if (unitPrice(rune.Name) is not { } p) return null;
            total += p * n;
        }
        return total;
    }

    public static string WeightProvenance(int knownRates, int unknownStats)
        => knownRates == 0
            ? "Poids indisponibles : données du jeu non chargées (EffectData.effectPowerRate). Aucun poids n'est calculé."
            : "Poids par point lus dans les données du jeu (EffectData.effectPowerRate)."
              + (unknownStats > 0 ? $" {unknownStats} caractéristique(s) sans poids connu, non comptées." : "");

    /// <summary>Pourcentage du poids maximum atteint (jets parfaits) ; null si le maximum est inconnu ou nul.</summary>
    public static double? PercentOfMax(decimal weight, decimal maxWeight) => maxWeight > 0 ? (double)(weight * 100m / maxWeight) : null;
}
