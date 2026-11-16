using System;
using System.Linq;

namespace GivreClone.Protocol;

public static class TeamFollowPosition
{
    public static int AdjacentDestination(int source, int leader, Func<int, bool> walk,
        Func<int, int, bool>? stepAllowed = null)
    {
        if (source != leader && MapPathfinder.CellDistance(source, leader) == 1) return source;
        return MapPathfinder.InteractionNeighbours(leader)
            .Where(c => c != leader && MapPathfinder.CellDistance(c, leader) == 1 && walk(c))
            .Select(c => (Cell: c, Path: MapPathfinder.KeyMovements(source, c, x => x != leader && walk(x), true, stepAllowed)))
            .Where(p => p.Path.Count > 0)
            .OrderBy(p => MapPathfinder.ExpandPath(p.Path.Select(k => k & 0xFFF).ToList()).Count)
            .ThenBy(p => p.Cell).Select(p => p.Cell).DefaultIfEmpty(-1).First();
    }
}
