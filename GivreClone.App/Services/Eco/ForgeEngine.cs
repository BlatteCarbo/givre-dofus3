using System.Collections.Generic;

namespace GivreClone.App.Services.Eco;

public enum ForgeActionKind { Rune, Skip, Finish }

/// <summary>Ce que la forgemagie doit faire ensuite : poser une rune, sauter l'étape, ou s'arrêter (objet terminé).</summary>
public readonly record struct ForgeAction(ForgeActionKind Kind, string Rune = "", string Reason = "")
{
    public static ForgeAction Use(string rune) => new(ForgeActionKind.Rune, rune);
    public static readonly ForgeAction Skip = new(ForgeActionKind.Skip);
    public static ForgeAction SkipBecause(string reason) => new(ForgeActionKind.Skip, Reason: reason);
    public static readonly ForgeAction Finish = new(ForgeActionKind.Finish);
}

/// <summary>Étape « Cibles » : monter <see cref="Stat"/> jusqu'à <see cref="Target"/> avec <see cref="Rune"/>. Priorité 1 = la plus urgente ; à priorité égale, le plus grand écart passe d'abord.</summary>
public sealed record ForgeTarget(string Stat, int Target, int Priority, string Rune);

public enum ForgeCombine { And, Or }

/// <summary>Étape « Conditions » : si la caractéristique est dans [Min, Max] (et/ou le puits dans [PitMin, PitMax]), fais <see cref="Action"/>. Une borne nulle = non vérifiée.</summary>
public sealed record ForgeCondition(string? Stat, int Min, int Max, ForgeAction Action, int? PitMin = null, int? PitMax = null, ForgeCombine Combine = ForgeCombine.And);

/// <summary>Décision des étapes conditions. <see cref="Halt"/> renseigné : une mesure demandée est absente, aucune rune ne doit partir.</summary>
public readonly record struct ForgeConditionOutcome(ForgeAction Action, string? Halt)
{
    public bool IsHalt => !string.IsNullOrWhiteSpace(Halt);
    public static ForgeConditionOutcome Proceed(ForgeAction action) => new(action, null);
    public static ForgeConditionOutcome Stop(string reason) => new(default, reason);
}

/// <summary>Moteur de décision de forgemagie (règles style ExoFast, d'après sa documentation publique) : pur, sans paquet ni jeu.
/// Prochaine action selon les caractéristiques, le puits et les règles. Une mesure absente n'est jamais remplacée par zéro.</summary>
public static class ForgeEngine
{
    /// <summary>Étape « Cibles » : parmi les caractéristiques sous leur cible, la plus prioritaire (puis le plus grand écart, puis l'ordre de saisie). Toutes atteintes → Terminer.</summary>
    public static ForgeAction DecideTargets(IReadOnlyDictionary<string, int> stats, IReadOnlyList<ForgeTarget> targets)
    {
        ForgeTarget? best = null; int bestGap = 0;
        foreach (var t in targets)
        {
            int gap = t.Target - Current(stats, t.Stat);
            if (gap <= 0) continue;
            if (best is null || t.Priority < best.Priority || (t.Priority == best.Priority && gap > bestGap)) { best = t; bestGap = gap; }
        }
        return best is null ? ForgeAction.Finish : ForgeAction.Use(best.Rune);
    }

    /// <summary>Étape « Conditions » (+ puits) : la première condition vérifiée donne l'action ; aucune → Sauter l'étape.
    /// Une mesure demandée mais absente n'est pas une correspondance : l'action retournée est Sauter, et <see cref="DecideConditionSteps"/> porte le motif d'arrêt.</summary>
    public static ForgeAction DecideConditions(IReadOnlyDictionary<string, int> stats, int? pit, IReadOnlyList<ForgeCondition> conditions)
    {
        var outcome = DecideConditionSteps(stats, pit, conditions);
        return outcome.IsHalt ? ForgeAction.Skip : outcome.Action;
    }

    /// <summary>Même ordre que <see cref="DecideConditions"/>. La première ligne vérifiée gagne.
    /// Si la ligne a besoin d'une caractéristique ou d'un puits absent, arrêt immédiat : cette absence ne compte jamais comme une correspondance, et les lignes suivantes ne sont pas lues.
    /// Une ligne déjà fausse sans cette mesure (ET dont l'autre côté est hors plage) n'arrête pas.</summary>
    public static ForgeConditionOutcome DecideConditionSteps(IReadOnlyDictionary<string, int> stats, int? pit, IReadOnlyList<ForgeCondition> conditions)
    {
        if (conditions is not { Count: > 0 }) return ForgeConditionOutcome.Proceed(ForgeAction.Skip);
        for (int i = 0; i < conditions.Count; i++)
        {
            switch (Classify(conditions[i], stats, pit))
            {
                case ConditionHit.Yes:
                    return ForgeConditionOutcome.Proceed(conditions[i].Action);
                case ConditionHit.No:
                    continue;
                case ConditionHit.UnknownStat:
                    return ForgeConditionOutcome.Stop(UnknownStat(i + 1, StatKey(conditions[i])));
                case ConditionHit.UnknownPit:
                    return ForgeConditionOutcome.Stop(UnknownPit(i + 1));
                case ConditionHit.UnknownBoth:
                    return ForgeConditionOutcome.Stop(UnknownBoth(i + 1, StatKey(conditions[i])));
                default:
                    return ForgeConditionOutcome.Stop(UnknownBoth(i + 1, StatKey(conditions[i])));
            }
        }
        return ForgeConditionOutcome.Proceed(ForgeAction.Skip);
    }

    public static bool Matches(ForgeCondition c, IReadOnlyDictionary<string, int> stats, int? pit)
        => Classify(c, stats, pit) == ConditionHit.Yes;

    private enum ConditionHit { Yes, No, UnknownStat, UnknownPit, UnknownBoth }

    private static ConditionHit Classify(ForgeCondition c, IReadOnlyDictionary<string, int> stats, int? pit)
    {
        var stat = StatClause(c, stats);
        var sink = PitClause(c, pit);
        if (!stat.Active && !sink.Active) return ConditionHit.Yes;
        if (stat.Active && sink.Active) return Combine(c.Combine, stat, sink);
        if (stat.Active) return One(stat, ConditionHit.UnknownStat);
        return One(sink, ConditionHit.UnknownPit);
    }

    private readonly record struct Clause(bool Active, bool Known, bool Inside);

    private static ConditionHit Combine(ForgeCombine combine, Clause stat, Clause sink)
    {
        if (combine == ForgeCombine.Or)
        {
            if ((stat.Known && stat.Inside) || (sink.Known && sink.Inside)) return ConditionHit.Yes;
            if (!stat.Known && !sink.Known) return ConditionHit.UnknownBoth;
            if (!stat.Known) return ConditionHit.UnknownStat;
            if (!sink.Known) return ConditionHit.UnknownPit;
            return ConditionHit.No;
        }
        if (stat.Known && !stat.Inside) return ConditionHit.No;
        if (sink.Known && !sink.Inside) return ConditionHit.No;
        if (!stat.Known && !sink.Known) return ConditionHit.UnknownBoth;
        if (!stat.Known) return ConditionHit.UnknownStat;
        if (!sink.Known) return ConditionHit.UnknownPit;
        return ConditionHit.Yes;
    }

    private static ConditionHit One(Clause clause, ConditionHit unknown)
    {
        if (!clause.Known) return unknown;
        return clause.Inside ? ConditionHit.Yes : ConditionHit.No;
    }

    private static Clause StatClause(ForgeCondition c, IReadOnlyDictionary<string, int> stats)
    {
        string key = StatKey(c);
        if (key.Length == 0) return new(false, true, false);
        if (!stats.TryGetValue(key, out var value)) return new(true, false, false);
        return new(true, true, InRange(value, c.Min, c.Max));
    }

    private static Clause PitClause(ForgeCondition c, int? pit)
    {
        if (c.PitMin is null && c.PitMax is null) return new(false, true, false);
        if (pit is not int value) return new(true, false, false);
        return new(true, true, InRange(value, c.PitMin ?? int.MinValue, c.PitMax ?? int.MaxValue));
    }

    private static string StatKey(ForgeCondition c) => c.Stat?.Trim() ?? "";

    private static string UnknownStat(int step, string stat)
        => $"Étape {step} : caractéristique inconnue « {stat} ». Arrêt, aucune rune passée.";

    private static string UnknownPit(int step)
        => $"Étape {step} : puits inconnu. Arrêt, aucune rune passée.";

    private static string UnknownBoth(int step, string stat)
        => $"Étape {step} : caractéristique inconnue « {stat} » et puits inconnu. Arrêt, aucune rune passée.";

    private static bool InRange(int v, int min, int max) => v >= min && v <= max;
    private static int Current(IReadOnlyDictionary<string, int> stats, string stat) => stats.TryGetValue(stat, out var v) ? v : 0;
}
