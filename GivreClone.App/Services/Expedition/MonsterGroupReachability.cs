using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;

namespace GivreClone.App.Services.Expedition;

public static class MonsterGroupReachability
{
    /// <summary>Mêmes cases d'appui que l'attaque réelle ; un groupe inaccessible ne masque pas les suivants.</summary>
    public static MapActor? Select(IEnumerable<MapActor> groups, int source, int preferredCell,
        Func<int, bool> walkable, Func<int, int, bool>? stepAllowed = null, bool geometryKnown = true)
    {
        int origin = preferredCell >= 0 ? preferredCell : source;
        foreach (var group in groups.OrderBy(g => MapPathfinder.CellDistance(origin, g.CellId)))
        {
            if (!geometryKnown) return group; // géométrie encore inconnue : laisser le chemin existant se synchroniser
            if ((uint)source >= MapPathfinder.CellCount || (uint)group.CellId >= MapPathfinder.CellCount) continue;
            if (MapPathfinder.CellDistance(source, group.CellId) <= 1) return group;
            foreach (int cell in MapPathfinder.StandCellsInRange(group.CellId, 1, source, walkable))
                if (MapPathfinder.KeyMovements(source, cell, walkable, allowDiag: true, stepAllowed).Count > 0) return group;
        }
        return null;
    }
}
