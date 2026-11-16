using System;

namespace GivreClone.Protocol;

/// <summary>
/// Choix de la case d'arrivée d'un déplacement de combat demandé par un SCRIPT (combat:moveToCell) : même algorithme que
/// le rapprochement du moteur (<c>LaunchService.FindApproachCell</c>, BFS orthogonal borné aux PM + champ de distance
/// MARCHABLE qui contourne les murs), plus (a) le cas « la cible elle-même est atteignable dans mes PM » et (b) un repli
/// à vol d'oiseau quand le champ n'atteint aucune de mes cases. Pur (0 réseau), testable hors app.
/// </summary>
public static class CombatMovePlanner
{
    /// dest = target itself if reachable within mp; otherwise the reachable cell (OrthoReachable) with the smallest
    /// OrthoDistanceField(target) distance strictly below mine (tie -> fewer steps); if the field reaches none,
    /// fallback to CellDistance strictly decreasing. -1 if nothing brings me closer or mp&lt;=0.
    public static int PickDestination(int self, int target, int mp, Func<int, bool> walk,
                                      Func<int, int, bool>? step, out int steps)
    {
        steps = 0;
        if (mp <= 0 || self == target) return -1;
        if ((uint)self >= MapPathfinder.CellCount || (uint)target >= MapPathfinder.CellCount) return -1;
        var reach = MapPathfinder.OrthoReachable(self, mp, walk, step);
        if (reach.Count <= 1) return -1;                                  // emmuré (seulement ma case)
        if (reach.TryGetValue(target, out int direct) && direct > 0) { steps = direct; return target; }

        // Reverse traversal from the goal must test the corresponding forward edge.
        var field = MapPathfinder.OrthoDistanceField(target, walk,
            step is null ? null : (from, to) => step(to, from));
        int myField = field.TryGetValue(self, out var fd0) ? fd0 : MapPathfinder.CellDistance(self, target);
        int best = -1, bestField = myField, bestSteps = int.MaxValue;
        bool fieldReachesAny = false;
        foreach (var kv in reach)
        {
            int c = kv.Key, st = kv.Value;
            if (c == self || st <= 0) continue;
            if (!field.TryGetValue(c, out int fd)) continue;
            fieldReachesAny = true;
            if (fd >= myField) continue;                                   // ne retient que ce qui rapproche STRICTEMENT
            if (fd < bestField || (fd == bestField && st < bestSteps)) { bestField = fd; bestSteps = st; best = c; }
        }
        if (best < 0 && !fieldReachesAny)
        {
            // Repli : la cible est dans un secteur que le champ marchable n'atteint pas depuis mes cases -> au moins
            // se rapprocher à vol d'oiseau (strictement), le moins de PM à égalité.
            int myDist = MapPathfinder.CellDistance(self, target), bestDist = myDist;
            foreach (var kv in reach)
            {
                int c = kv.Key, st = kv.Value;
                if (c == self || st <= 0) continue;
                int d = MapPathfinder.CellDistance(c, target);
                if (d >= myDist) continue;
                if (d < bestDist || (d == bestDist && st < bestSteps)) { bestDist = d; bestSteps = st; best = c; }
            }
        }
        if (best < 0) return -1;
        steps = bestSteps;
        return best;
    }
}
