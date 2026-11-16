using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.CombatParity;

/// <summary>Zone d'un effet de sort, telle que lue dans les données du jeu (shape ASCII Ankama, param1, param2).</summary>
public readonly record struct SpellZoneCandidate(int Shape, int Size, int MinSize, bool Damaging, string? TargetMask);

/// <summary>
/// Zone retenue pour un grade de sort. Les données réelles mettent souvent un effet sur le LANCEUR (masque « C », point « P »)
/// avant l'effet de dégâts : Déchaînement (12755) = point r1 sur soi puis carré « G » r1 de dégâts. Garder la première zone à
/// rayon égal réduisait la zone de dégâts à la seule case centrale (filtre des défis, alliés et achèvement de zone faussés).
/// Ordre : effets de dégâts, puis effets visant d'autres combattants que le lanceur, puis le reste ; ensuite le plus grand
/// rayon ; à rayon égal une vraie forme plutôt qu'un point ; enfin l'ordre des effets.
/// </summary>
public static class SpellZoneSelection
{
    public const int PointShape = 'P';

    public static (int Shape, int Size, int MinSize)? Pick(IEnumerable<SpellZoneCandidate> effects)
    {
        var best = effects
            .Select((e, i) => (e, i))
            .Where(x => x.e.Shape != 0)
            .OrderByDescending(x => x.e.Damaging ? 2 : CasterOnly(x.e.TargetMask) ? 0 : 1)
            .ThenByDescending(x => x.e.Size)
            .ThenBy(x => x.e.Shape == PointShape ? 1 : 0)
            .ThenBy(x => x.i)
            .Select(x => (SpellZoneCandidate?)x.e)
            .FirstOrDefault();
        return best is { } z ? (z.Shape, System.Math.Max(0, z.Size), z.MinSize) : null;
    }

    /// <summary>Masque ne visant que le lanceur (« C » / « c », éventuellement répétés).</summary>
    public static bool CasterOnly(string? mask)
    {
        if (string.IsNullOrWhiteSpace(mask)) return false;
        foreach (var part in mask.Split(','))
        {
            var m = part.Trim();
            if (m.Length == 0) continue;
            if (m != "C" && m != "c") return false;
        }
        return true;
    }
}
