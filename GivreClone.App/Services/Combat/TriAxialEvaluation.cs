using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Combat;

/// <summary>
/// Pondérations de l'arbitrage tri-axial pour le planificateur de combat en équipe ou solo.
/// </summary>
public sealed record TriAxialWeights(
    double SpeedWeight,
    double DamageWeight,
    double SurvivabilityWeight)
{
    public static TriAxialWeights FarmSpeed => new(0.55, 0.35, 0.10);
    public static TriAxialWeights Balanced => new(0.25, 0.45, 0.30);
    public static TriAxialWeights HighSurvival => new(0.05, 0.25, 0.70);
}

/// <summary>
/// Résultat détaillé de l'évaluation d'une action ou d'un tour complet.
/// </summary>
public readonly record struct TriAxialEvaluation(
    double SpeedScore,
    double DamageScore,
    double SurvivabilityScore,
    double Penalty,
    double TotalUtility)
{
    public static TriAxialEvaluation Compute(
        double speed,
        double damage,
        double surv,
        double penalty,
        TriAxialWeights weights)
    {
        double utility = (weights.SpeedWeight * speed)
                       + (weights.DamageWeight * damage)
                       + (weights.SurvivabilityWeight * surv)
                       - penalty;
        return new TriAxialEvaluation(speed, damage, surv, penalty, utility);
    }
}

/// <summary>
/// Moteur de scoring tri-axial (Rapidité, Dégâts, Survivabilité) pour l'IA tactique multi-comptes et de classe.
/// </summary>
public static class TriAxialEvaluator
{
    /// <summary>
    /// Calcule le score de vitesse (réduction du nombre de tours estimés + minimisation des délais d'animation).
    /// </summary>
    public static double EvaluateSpeed(int turnsRemainingEst, int animDurationMs, bool killsEnemy)
    {
        double turnBonus = Math.Exp(-0.45 * Math.Max(0, turnsRemainingEst));
        double animPenalty = 1.0 / (1.0 + 0.00035 * Math.Max(0, animDurationMs));
        double killBonus = killsEnemy ? 0.3 : 0.0;
        return Math.Clamp((0.65 * turnBonus) + (0.35 * animPenalty) + killBonus, 0.0, 1.0);
    }

    /// <summary>
    /// Calcule le score de dégâts utiles (dégâts effectifs sans gaspillage d'overkill + bonus létalité).
    /// </summary>
    public static double EvaluateDamage(int projectedDmg, int targetCurrentHp, bool targetKilled, double targetThreat = 1.0)
    {
        if (targetCurrentHp <= 0 || projectedDmg <= 0) return 0.0;
        int effectiveDmg = Math.Min(projectedDmg, targetCurrentHp);
        double ratio = Math.Clamp((double)effectiveDmg / targetCurrentHp, 0.0, 1.0);
        double killBonus = targetKilled ? (0.5 + 0.25 * targetThreat) : 0.0;
        return Math.Clamp(ratio * 0.7 + killBonus, 0.0, 1.0);
    }

    /// <summary>
    /// Calcule le score de survivabilité (préservation de la santé alliée, boucliers, évitement du tacle).
    /// </summary>
    public static double EvaluateSurvivability(
        int selfHp, int selfMaxHp, 
        int distanceToNearestEnemy, 
        bool isTackled, 
        int shieldGained = 0,
        int healGained = 0)
    {
        double hpRatio = selfMaxHp > 0 ? Math.Clamp((double)(selfHp + healGained) / selfMaxHp, 0.0, 1.0) : 1.0;
        double shieldRatio = selfMaxHp > 0 ? Math.Clamp((double)shieldGained / (selfMaxHp * 0.5), 0.0, 1.0) : 0.0;
        double distSafety = Math.Clamp(distanceToNearestEnemy / 8.0, 0.0, 1.0);
        double tacklePenalty = isTackled ? 0.4 : 0.0;
        
        return Math.Clamp((hpRatio * 0.5) + (shieldRatio * 0.25) + (distSafety * 0.25) - tacklePenalty, 0.0, 1.0);
    }
}
