using System;

namespace GivreClone.App.Services.Treasure;

/// <summary>
/// Cartes où <c>treasure:startHunt</c> peut utiliser l'élément interactif du coffre.
/// <see cref="ElementId"/> est le même id que <c>GivreLuaHost.TreasureStartElement</c> (484993, Boîte d'Enutrof).
/// <see cref="MalleAuxTresors"/> est la carte déjà visée par <c>FouduglenScript</c> (<c>travelTo(128452097)</c>).
/// Le journal du 30/10/2026 la relie à cet élément : arrivée sur 128452097, interactif 484993, cellule 290, type 453
/// (<c>docs/codex/LIVE_TEST_20261030.md</c>). Aucun autre id de carte n'est ajouté ici.
/// </summary>
public static class HuntChestMaps
{
    public const int ElementId = 484993;
    public const long MalleAuxTresors = 128452097;

    public static readonly long[] Known = [MalleAuxTresors];

    public static bool Contains(long mapId)
    {
        foreach (long id in Known)
            if (id == mapId) return true;
        return false;
    }

    /// <summary>Carte de coffre la plus proche en distance de Manhattan. Coordonnées inconnues : la carte connue (il n'y en a qu'une).</summary>
    public static long Nearest(long here, Func<long, (int X, int Y)?>? coord)
    {
        if (Contains(here)) return here;
        if (coord is null) return Known[0];
        var origin = coord(here);
        long best = Known[0];
        int bestDistance = int.MaxValue;
        bool scored = false;
        foreach (long map in Known)
        {
            if (origin is not { } from || coord(map) is not { } to) continue;
            int distance = Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y);
            if (scored && distance >= bestDistance) continue;
            scored = true;
            bestDistance = distance;
            best = map;
        }
        return best;
    }
}
