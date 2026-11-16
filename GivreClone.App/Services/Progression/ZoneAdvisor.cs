using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Progression;

/// <summary>Une sous-zone vue par le conseiller : niveaux des monstres qu'on y croise et nombre de cartes.</summary>
public sealed record ZoneInfo(int SubAreaId, string Area, string Name, IReadOnlyList<(int Min, int Max)> MonsterLevels, int MapCount);

public enum ZoneFit { TooEasy, Good, Hard, TooHard }

public sealed record ZoneAdvice(ZoneInfo Zone, double AverageLevel, int Monsters, ZoneFit Fit, double Score);

/// <summary>Conseiller de zones de combat selon le niveau du personnage (pur). Idéal : monstres du niveau du perso à +25 % (assez d'XP sans être trop dangereux) ; en dessous de 60 % le gain chute,
/// au-dessus de 140 % le risque de défaite domine. À niveau de zone égal, plus de cartes et plus de monstres différents = farm plus fluide. Estimation : ne mesure ni les résistances ni les dégâts réels.</summary>
public static class ZoneAdvisor
{
    public static ZoneAdvice? Evaluate(ZoneInfo z, int level)
    {
        if (level < 1 || z.MonsterLevels.Count == 0) return null;
        double avg = z.MonsterLevels.Average(m => (m.Min + m.Max) / 2.0);
        double ratio = avg / level;
        var fit = ratio < 0.6 ? ZoneFit.TooEasy : ratio <= 1.25 ? ZoneFit.Good : ratio <= 1.4 ? ZoneFit.Hard : ZoneFit.TooHard;
        // proximité de la cible idéale (1,0 à 1,25 du niveau) : 0 sur la cible, croît avec l'écart
        double target = Math.Clamp(ratio, 1.0, 1.25);
        double gap = Math.Abs(ratio - target);
        double score = 100 - gap * 120 - (fit is ZoneFit.TooHard ? 40 : 0) + Math.Min(10, z.MapCount) + Math.Min(6, z.MonsterLevels.Count);
        return new ZoneAdvice(z, Math.Round(avg, 1), z.MonsterLevels.Count, fit, Math.Round(score, 1));
    }

    public static IReadOnlyList<ZoneAdvice> Rank(IEnumerable<ZoneInfo> zones, int level, int limit = 10, bool includeHard = false)
        => zones.Select(z => Evaluate(z, level)).Where(a => a is not null).Select(a => a!)
            .Where(a => a.Fit == ZoneFit.Good || (includeHard && a.Fit == ZoneFit.Hard))
            .Where(a => a.Zone.MapCount > 0)
            .OrderByDescending(a => a.Score).ThenBy(a => a.Zone.Name, StringComparer.Ordinal)
            .Take(Math.Clamp(limit, 1, 100)).ToList();
}
