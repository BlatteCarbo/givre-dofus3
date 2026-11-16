using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Forgemagie;

/// <summary>Une ligne d'effet de l'objet : jet actuel, bornes du jet (min-max d'origine), objectif voulu et plafond (0 = aucun).</summary>
public sealed record ForgeLine(string Stat, int Current, int Min, int Max, int Target, int Cap = 0, int PaFrom = 0, int RaFrom = 0);

/// <summary>Une rune disponible : caractéristique, palier (Ba, Pa, Ra) et gain en points de caractéristique par rune.</summary>
public sealed record RuneKind(string Stat, string Tier, int Gain);

public enum AdviceKind { Rune, Done, NoRune, NoLine }

/// <summary>Conseil du moteur : la rune à poser et pourquoi, ou la raison de l'arrêt. Jamais une règle inconnue présentée comme certaine : <see cref="Reason"/> dit toujours d'où vient le choix.</summary>
public sealed record RuneAdvice(AdviceKind Kind, RuneKind? Rune, string Stat, string Reason);

/// <summary>Réglages du conseiller. <c>AllowOver</c> : accepter de dépasser le jet maximal de la ligne (over) ; sinon une rune qui ferait dépasser le maximum est écartée.</summary>
public sealed record AdvisorSettings(bool AllowPa = true, bool AllowRa = true, bool AllowOver = false);

/// <summary>Conseiller de forgemagie PUR (aucun paquet, aucune action en jeu) : à partir des jets actuels, des objectifs et du stock de runes, propose la prochaine rune.
/// Les gains par rune sont une table réglable (<see cref="DefaultGains"/>, relevée sur les guides, à vérifier en jeu) ; le reliquat n'est pas modélisé (inconnu tant qu'aucune capture ne le montre).</summary>
public static class RuneAdvisor
{
    /// <summary>Gain par rune (Ba, Pa, Ra) des caractéristiques courantes. À vérifier en jeu : les valeurs sont celles des guides, pas d'un paquet.</summary>
    public static readonly IReadOnlyDictionary<string, (int Ba, int Pa, int Ra)> DefaultGains = new Dictionary<string, (int, int, int)>(StringComparer.OrdinalIgnoreCase)
    {
        ["Vitalité"] = (5, 15, 50), ["Sagesse"] = (1, 3, 10), ["Force"] = (1, 3, 10), ["Intelligence"] = (1, 3, 10), ["Chance"] = (1, 3, 10), ["Agilité"] = (1, 3, 10),
        ["Initiative"] = (10, 30, 100), ["Prospection"] = (1, 3, 10), ["Pods"] = (10, 30, 100),
    };

    public static IReadOnlyList<RuneKind> RunesFor(string stat, IReadOnlyDictionary<string, (int Ba, int Pa, int Ra)>? gains = null)
    {
        var table = gains ?? DefaultGains;
        if (!table.TryGetValue(stat, out var g)) return Array.Empty<RuneKind>();
        return new[] { new RuneKind(stat, "Ba", g.Ba), new RuneKind(stat, "Pa", g.Pa), new RuneKind(stat, "Ra", g.Ra) };
    }

    /// <summary>Prochaine rune à poser. <paramref name="stock"/> : quantité par (caractéristique, palier). Les lignes sont traitées dans l'ordre donné.</summary>
    /// <summary>Non-rune advice ends the forge run: Done = target reached (finish), otherwise stop with the advisor reason.</summary>
    public static GivreClone.App.Services.Eco.ForgeAction StopAction(RuneAdvice advice) => advice.Kind == AdviceKind.Done
        ? GivreClone.App.Services.Eco.ForgeAction.Finish
        : GivreClone.App.Services.Eco.ForgeAction.SkipBecause(advice.Reason);

    public static RuneAdvice Advise(IReadOnlyList<ForgeLine> lines, IReadOnlyDictionary<(string Stat, string Tier), int> stock, AdvisorSettings? settings = null,
                                    IReadOnlyDictionary<string, (int Ba, int Pa, int Ra)>? gains = null)
    {
        settings ??= new AdvisorSettings();
        if (lines.Count == 0) return new RuneAdvice(AdviceKind.NoLine, null, "", "aucune ligne d'effet à forgemager");
        bool pending = false, missing = false;
        string missingStat = "";
        foreach (var l in lines)
        {
            int target = l.Cap > 0 ? Math.Min(l.Target, l.Cap) : l.Target;
            int gap = target - l.Current;
            if (gap <= 0) continue;
            pending = true;
            var runes = RunesFor(l.Stat, gains);
            if (runes.Count == 0) { missing = true; missingStat = l.Stat; continue; }
            // Du plus gros palier autorisé au plus petit : le plus gros qui ne dépasse ni l'objectif ni (sans over) le maximum du jet.
            foreach (var r in runes.OrderByDescending(x => x.Gain))
            {
                if (r.Tier == "Pa" && (!settings.AllowPa || l.Current < l.PaFrom)) continue;
                if (r.Tier == "Ra" && (!settings.AllowRa || l.Current < l.RaFrom)) continue;
                if (stock.GetValueOrDefault((l.Stat, r.Tier)) <= 0) continue;
                bool smallest = r.Tier == "Ba";
                if (!smallest && r.Gain > gap) continue;                                  // un gros palier ne doit pas dépasser l'objectif
                if (!settings.AllowOver && l.Max > 0 && l.Current + r.Gain > l.Max) continue;   // ni dépasser le jet maximal
                string why = smallest && r.Gain > gap ? $"{l.Stat} {l.Current} → objectif {target} (la rune de base dépasse un peu l'objectif)" : $"{l.Stat} {l.Current} → objectif {target} (écart {gap}, rune {r.Tier} +{r.Gain})";
                return new RuneAdvice(AdviceKind.Rune, r, l.Stat, why);
            }
            missing = true; missingStat = l.Stat;
        }
        if (!pending) return new RuneAdvice(AdviceKind.Done, null, "", "tous les objectifs sont atteints");
        return new RuneAdvice(AdviceKind.NoRune, null, missingStat, $"aucune rune utilisable pour {missingStat} (stock vide, palier non autorisé ou jet maximal atteint)");
    }
}
