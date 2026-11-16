using System;

namespace GivreClone.Protocol;

public static class CombatPositionPlanner
{
    public static int FindCastCell(int from, int mp, Func<int, bool> walk,
        Func<int, bool> canCastFrom, out int steps, Func<int, int, bool>? stepAllowed = null, Func<int, int>? tieBreak = null)
    {
        // Parmi les cases de tir au MÊME coût en PM, on prend la meilleure (tieBreak plus petit = meilleur), puis la plus petite cellule :
        // avant, c'était la plus petite cellule seule, c'est-à-dire un choix arbitraire (zone touchée, distance, risque de tacle ignorés).
        steps = 0;
        if ((uint)from >= MapPathfinder.CellCount || mp <= 0) return -1;
        var reachable = MapPathfinder.OrthoReachable(from, mp, walk, stepAllowed);
        int best = -1, bestSteps = int.MaxValue, bestTie = int.MaxValue;
        foreach (var (cell, distance) in reachable)
        {
            if (cell == from || distance <= 0 || distance > bestSteps) continue;
            if (!canCastFrom(cell)) continue;
            int tie = tieBreak?.Invoke(cell) ?? 0;
            if (distance == bestSteps && best >= 0 && (tie > bestTie || (tie == bestTie && cell >= best))) continue;
            best = cell;
            bestSteps = distance;
            bestTie = tie;
        }
        if (best >= 0) steps = bestSteps;
        return best;
    }
}
