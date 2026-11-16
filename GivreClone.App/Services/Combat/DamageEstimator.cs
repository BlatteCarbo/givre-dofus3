using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Combat;

/// <summary>Identifiants de caractéristiques du jeu (table CharacteristicData du client) utiles au calcul de dégâts.</summary>
public static class CharIds
{
    // CharacteristicData: maxSummonedCreaturesBoost (tx_summonableCreaturesBoost).
    public const int MaxSummonedCreatures = 26;
    public const int Strength = 10, Chance = 13, Agility = 14, Intelligence = 15;
    public const int AllDamageBonus = 16, DamagePercent = 25, CriticalHit = 18, CriticalDamageBonus = 86;
    public const int PushDamageBonus = 84;
    public const int DealtDamageMultiplier = 107;
    public const int DealtHealMultiplier = 143;
    public const int Range = 19;
    public const int ResistEarth = 33, ResistFire = 34, ResistWater = 35, ResistAir = 36, ResistNeutral = 37;
    public const int ReductionEarth = 54, ReductionFire = 55, ReductionWater = 56, ReductionAir = 57, ReductionNeutral = 58;
    public const int BonusEarth = 88, BonusFire = 89, BonusWater = 90, BonusAir = 91, BonusNeutral = 92;
}

/// <summary>Cible d'une estimation : résistances en % et réductions fixes par élément (clés = <see cref="DamageElement"/>), PV éventuels.</summary>
public sealed record DamageTarget(IReadOnlyDictionary<DamageElement, int>? ResistPercent = null, IReadOnlyDictionary<DamageElement, int>? FlatReduction = null, int? Life = null);

public enum DamageElement { Neutral, Earth, Fire, Water, Air }

public sealed record DamageEstimate(DamageElement Element, double Min, double Max, double Average, double AverageWithCrit, double CritChancePercent, double PercentBonus, double FlatBonus, double? HitsToKill);

/// <summary>Estimation des dégâts d'un sort (pure). Formule classique de Dofus : dégâts = base × (100 + caractéristique de l'élément + % Dommages) / 100 + dommages fixes (tous + de l'élément),
/// puis cible : × (1 − résistance %) − réduction fixe. Le coup critique ajoute les dommages critiques ; la chance de critique est celle du personnage (la base propre au sort n'est pas lue).
/// ESTIMATION : ignore les modificateurs propres au sort, la puissance, les états et les effets particuliers (vol de vie, poussée…). Un résultat n'est jamais présenté comme exact.</summary>
public static class DamageEstimator
{
    /// <summary>Élément du jeu (effectElement) : 0 neutre, 1 terre, 2 feu, 3 eau, 4 air.</summary>
    public static DamageElement ElementOf(int effectElement) => effectElement switch { 1 => DamageElement.Earth, 2 => DamageElement.Fire, 3 => DamageElement.Water, 4 => DamageElement.Air, _ => DamageElement.Neutral };

    public static DamageElement ElementOf(SpellEffectDescriptor effect, IReadOnlyDictionary<int, int> characteristics)
    {
        if (effect.ActionId is not (2822 or 2828 or 2832 or 2890)) return ElementOf(effect.Element);
        var choices = new[] { DamageElement.Earth, DamageElement.Fire, DamageElement.Water, DamageElement.Air };
        return (effect.ActionId is 2832 or 2890
            ? choices.OrderBy(e => characteristics.GetValueOrDefault(StatId(e)))
            : choices.OrderByDescending(e => characteristics.GetValueOrDefault(StatId(e)))).First();
    }

    public static int StatId(DamageElement e) => e switch { DamageElement.Fire => CharIds.Intelligence, DamageElement.Water => CharIds.Chance, DamageElement.Air => CharIds.Agility, _ => CharIds.Strength };
    private static int FlatId(DamageElement e) => e switch { DamageElement.Fire => CharIds.BonusFire, DamageElement.Water => CharIds.BonusWater, DamageElement.Air => CharIds.BonusAir, DamageElement.Earth => CharIds.BonusEarth, _ => CharIds.BonusNeutral };

    /// <summary>Estimation pour un sort de dégâts : <paramref name="min"/>/<paramref name="max"/> = dés de base du sort.</summary>
    public static DamageEstimate Estimate(int min, int max, DamageElement element, IReadOnlyDictionary<int, int> me, DamageTarget? target = null,
        double damageMultiplier = 1)
    {
        int Stat(int id) => me.TryGetValue(id, out var v) ? v : 0;
        double percent = Stat(StatId(element)) + Stat(CharIds.DamagePercent);
        double flat = Stat(CharIds.AllDamageBonus) + Stat(FlatId(element));
        double critChance = Math.Clamp(Stat(CharIds.CriticalHit), 0, 100);
        double critFlat = Stat(CharIds.CriticalDamageBonus);
        double resist = target?.ResistPercent is { } rp && rp.TryGetValue(element, out var r) ? Math.Clamp(r, -100, 100) : 0;   // résistance observée : 100 % = immunité à l'élément
        double reduction = target?.FlatReduction is { } fr && fr.TryGetValue(element, out var f) ? f : 0;

        double One(double roll, bool crit)
        {
            double d = roll * (100 + percent) / 100.0 + flat + (crit ? critFlat : 0);
            d *= Math.Max(0, me.GetValueOrDefault(CharIds.DealtDamageMultiplier, 100)) / 100.0;
            d *= damageMultiplier;
            d = d * (1 - resist / 100.0) - reduction;
            return Math.Max(0, d);
        }
        double lo = One(Math.Min(min, max), false), hi = One(Math.Max(min, max), false);
        double avgRoll = (min + max) / 2.0;
        double avg = One(avgRoll, false), avgCrit = One(avgRoll, true);
        double expected = avg * (1 - critChance / 100.0) + avgCrit * (critChance / 100.0);
        double? hits = target?.Life is int life && life > 0 && expected > 0 ? Math.Ceiling(life / expected) : null;
        return new DamageEstimate(element, Math.Round(lo, 1), Math.Round(hi, 1), Math.Round(avg, 1), Math.Round(expected, 1), critChance, percent, flat, hits);
    }
}
