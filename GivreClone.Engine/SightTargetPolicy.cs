using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;

namespace GivreClone.Engine;

/// <summary>Fallback target for Tueur à vue: locked within a turn, renewed on death or a new turn.</summary>
public sealed class SightTargetPolicy
{
    private int? _epoch;
    private long _target;

    public long Select(int epoch, int currentCell, IReadOnlyList<(int Cell, long ActorId)> enemies,
                       IReadOnlyCollection<long> designated)
    {
        if (_epoch != epoch) { _epoch = epoch; _target = 0; }
        // A server designation supersedes the fallback, including when it arrives mid-turn.
        var server = enemies.Where(e => designated.Contains(e.ActorId)).OrderBy(e => e.ActorId).FirstOrDefault();
        if (server.ActorId != 0) return _target = server.ActorId;
        if (_target != 0 && enemies.Any(e => e.ActorId == _target)) return _target;
        if ((uint)currentCell >= MapPathfinder.CellCount) return _target = 0;
        return _target = enemies.OrderBy(e => MapPathfinder.CellDistance(currentCell, e.Cell))
            .ThenBy(e => e.ActorId).Select(e => e.ActorId).FirstOrDefault();
    }
}
