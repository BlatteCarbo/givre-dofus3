using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.Engine;

/// <summary>
/// Choix du sort selon la SITUATION du tour : valeur attendue par PA = dégâts d'un lancer (avec mes caractéristiques) × cibles touchées,
/// plus un bonus pour chaque cible qu'un lancer suffit à achever (un ennemi mort n'inflige plus rien). Un sort de zone qui touche deux
/// ennemis vaut donc deux fois un sort mono-cible de mêmes dégâts. Pur, sans dépendance au combat : testable sur des chiffres.
/// </summary>
public static class SpellChoicePlanner
{
    /// <summary>Un sort offensif vu depuis la case courante : coût, dégâts moyens d'UN lancer sur UNE cible, ennemis touchés par le meilleur
    /// centre de zone autorisé (alliés épargnés), et combien d'entre eux ce lancer suffit à achever.</summary>
    public readonly record struct Candidate(int Ap, double AvgDamage, int TargetsHit, int Kills);

    /// <summary>Poids d'une cible achevée, en « cibles touchées » équivalentes : tuer vaut un demi-ennemi touché de plus.</summary>
    public const double KillWeight = 0.5;

    /// <summary>Valeur par PA. 0 si le sort ne peut rien toucher, ne coûte rien ou ne fait pas de dégâts.</summary>
    public static double Score(Candidate c)
    {
        if (c.Ap <= 0 || c.AvgDamage <= 0 || c.TargetsHit <= 0) return 0;
        int kills = Math.Min(c.Kills, c.TargetsHit);
        return c.AvgDamage * (c.TargetsHit + KillWeight * kills) / c.Ap;
    }

    /// <summary>Hits on the special treasure guard remove one chest HP per cast, irrespective of damage. Callers supply the remaining legal
    /// casts after cooldown, range, LOS and per-turn/per-target limits. Unavailable spells have no value.</summary>
    public static double HitCountScore(int ap, int availableAp, int remainingCasts)
        => ap > 0 && availableAp >= ap && remainingCasts > 0 ? 1.0 / ap : 0;

    /// <summary>
    /// Réordonne UNIQUEMENT les éléments offensifs, parmi les emplacements qu'ils occupaient : les buffs, invocations et soins gardent leur
    /// place (priorité choisie par le joueur), les offensifs se répartissent leurs emplacements par valeur décroissante. À valeur égale,
    /// l'ordre d'origine est conservé (ordre stable). Renvoie une NOUVELLE liste ; l'entrée n'est pas modifiée.
    /// </summary>
    public static List<T> ReorderOffensive<T>(IReadOnlyList<T> items, Func<T, bool> isOffensive, Func<T, double> score)
    {
        var result = items.ToList();
        var slots = new List<int>();
        for (int i = 0; i < items.Count; i++) if (isOffensive(items[i])) slots.Add(i);
        var sorted = slots.Select(i => (Index: i, Item: items[i], Score: score(items[i])))
            .OrderByDescending(x => x.Score).ThenBy(x => x.Index).Select(x => x.Item).ToList();
        for (int k = 0; k < slots.Count; k++) result[slots[k]] = sorted[k];
        return result;
    }
}
