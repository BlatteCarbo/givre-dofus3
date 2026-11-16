using System;
using System.Collections.Generic;

namespace GivreClone.App.Services.Combat;

/// <summary>En équipe, un suiveur frappe d'abord l'ennemi que son chef vient de toucher : on regroupe les dégâts pour tuer plus vite.
/// Déplacement STABLE de cette cible en tête ; le tri « achever les tuables » appliqué ensuite la garde derrière les ennemis
/// tuables ce tour. Sans cible du chef (ou cible absente de la liste : morte, hors portée) l'ordre est inchangé.</summary>
public static class TeamFocus
{
    /// <summary>Durée pendant laquelle le dernier coup du chef reste une consigne (au-delà, ce n'est plus « le tour du chef »).</summary>
    public const long FreshMs = 45_000;

    public static List<T> LeaderTargetFirst<T>(List<T> ordered, Func<T, long> actorOf, long? leaderTarget)
    {
        if (leaderTarget is not { } target || ordered.Count < 2) return ordered;
        int i = ordered.FindIndex(e => actorOf(e) == target);
        if (i <= 0) return ordered;
        var result = new List<T>(ordered.Count) { ordered[i] };
        for (int k = 0; k < ordered.Count; k++) if (k != i) result.Add(ordered[k]);
        return result;
    }
}
