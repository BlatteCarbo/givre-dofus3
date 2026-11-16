using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.Protocol;

public sealed record NavigationProbeResult(string Status, IReadOnlyList<int> Cells,
    IReadOnlyList<int> KeyCells, int WalkableCells);

public static class NavigationProbe
{
    public static NavigationProbeResult Evaluate(long mapId, long geometryMap, long? positionMap,
        bool positionConfirmed, int? source, IReadOnlyList<MapCell> cells, int target,
        bool inFight = false, IEnumerable<int>? customObstacles = null)
    {
        NavigationProbeResult Empty(string status, int count = 0) => new(status,
            Array.Empty<int>(), Array.Empty<int>(), count);
        if (mapId <= 0) return Empty("map_unknown");
        if (geometryMap != mapId) return Empty("geometry_stale");
        if (positionMap != mapId) return Empty("position_stale");
        if (!positionConfirmed) return Empty("position_unconfirmed");
        if (source is not int from || (uint)from >= MapPathfinder.CellCount)
            return Empty("position_unknown");
        if ((uint)target >= MapPathfinder.CellCount) return Empty("target_invalid");
        if (cells.Count != MapPathfinder.CellCount ||
            cells.Any(c => (uint)c.CellId >= MapPathfinder.CellCount) ||
            cells.Select(c => c.CellId).Distinct().Count() != MapPathfinder.CellCount)
            return Empty("geometry_incomplete");

        var walk = new bool[MapPathfinder.CellCount];
        var floors = new int[walk.Length];
        var zones = new int[walk.Length];
        var bridges = new bool[walk.Length];
        foreach (var cell in cells)
        {
            int id = cell.CellId;
            walk[id] = cell.Mov && (!inFight || !cell.NonWalkableDuringFight);
            floors[id] = walk[id] ? cell.Floor : -1;
            zones[id] = walk[id] ? (cell.LinkedZone & 0xF8) >> 4 : -1;
            bridges[id] = cell.MoveZone == 1;
        }
        if (customObstacles is not null)
            foreach (int cell in customObstacles)
                if ((uint)cell < walk.Length) walk[cell] = false;
        int count = walk.Count(w => w);
        if (from == target) return new("already_there", new[] { from }, Array.Empty<int>(), count);
        if (!walk[target]) return Empty("target_blocked", count);
        var keys = MapPathfinder.KeyMovements(from, target, c => walk[c], !inFight,
            (a, b) => MapPathfinder.Communicates(a, b, floors, zones, bridges));
        if (keys.Count == 0) return Empty("unreachable", count);
        var route = MapPathfinder.ExpandPath(keys.Select(k => k & 0xFFF).ToArray());
        return new("path_found", route, keys, count);
    }
}
