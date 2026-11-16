using System.Collections.Generic;
using System.Linq;

namespace GivreClone.Protocol;

public static class MovementEchoPolicy
{
    // A server may stop a movement early. Only an exact prefix of this request
    // is accepted as a shortened echo; unrelated destinations are never ACKed.
    public static bool Matches(IReadOnlyList<int> requested, IReadOnlyList<int>? accepted)
    {
        if (requested.Count < 2 || accepted is not { Count: > 1 }
            || requested.Any(c => (uint)c >= MapPathfinder.CellCount)
            || requested[0] != accepted[0] || accepted.Any(c => (uint)c >= MapPathfinder.CellCount)) return false;
        if (accepted[^1] == requested[^1]) return true; // server may choose another route to the same endpoint
        return accepted.Count < requested.Count && accepted.SequenceEqual(requested.Take(accepted.Count));
    }
}
