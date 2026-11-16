using System.Collections.Generic;
using GivreClone.Protocol;

namespace GivreClone.App.Services.Combat;

public static class CombatSelfZoneSafety
{
    // Missing spell data must not turn the spell's casting range into an invented damage area.
    // The caster is deliberately excluded from allyCells by the caller.
    public static CombatAoeVerdict Evaluate((int Shape, int Size, int MinSize)? zone,
        int casterCell, int towardCell, IEnumerable<int> enemyCells, IEnumerable<int> allyCells, int minimumHits)
    {
        if (zone is not { } known) return new(false, "unknown_zone", 0, 0);
        return CombatAoeSafety.Evaluate(CombatZones.GetCells(known.Shape, casterCell,
            CombatZones.OrientationTo(casterCell, towardCell), known.MinSize, known.Size),
            enemyCells, allyCells, System.Math.Max(1, minimumHits));
    }
}
