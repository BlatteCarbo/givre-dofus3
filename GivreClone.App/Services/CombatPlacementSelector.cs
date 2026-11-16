using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

/// <summary>Choix pur d'une case initiale, sans envoi ni lecture d'etat du combat.</summary>
public static class CombatPlacementSelector
{
    public static int Select(PlacementMode mode, CombatStyle style, IReadOnlyList<int> offered,
        IReadOnlyCollection<int> enemies, IReadOnlyCollection<int> occupied, int currentCell)
    {
        var available = offered.Where(c => c >= 0 && c < 560 && !occupied.Contains(c)).ToList();
        if (available.Count == 0) return -1;
        if (mode == PlacementMode.Keep)
            return available.Contains(currentCell) ? currentCell : -1;

        var targets = enemies.Where(c => c >= 0 && c < 560).ToArray();
        // Les anciens profils gardent le repli historique et l'ordre des offres en cas d'egalite.
        if (targets.Length == 0)
            return mode == PlacementMode.LegacyByStyle ? available[0] : -1;
        int Distance(int cell) => targets.Min(enemy => MapPathfinder.CellDistance(cell, enemy));
        bool near = mode == PlacementMode.Near
            || (mode == PlacementMode.LegacyByStyle && style == CombatStyle.Aggressive);
        var ranked = near ? available.OrderBy(Distance) : available.OrderByDescending(Distance);
        return mode == PlacementMode.LegacyByStyle ? ranked.First() : ranked.ThenBy(c => c).First();
    }

    /// <summary>Menace d'un ennemi au premier tour : portée d'attaque + PM (distance diamant, terrain ignoré = prudent).</summary>
    public readonly record struct PlacementThreat(int Cell, int MovePoints, int AttackRange, int ExpectedDamage);

    /// <summary>Dégâts attendus sur <paramref name="cell"/> au premier tour ennemi : somme des ennemis capables de l'atteindre.</summary>
    public static int Exposure(int cell, IReadOnlyCollection<PlacementThreat> threats) => threats
        .Where(t => t.Cell is >= 0 and < 560
            && MapPathfinder.CellDistance(cell, t.Cell) <= Math.Max(0, t.MovePoints) + Math.Max(0, t.AttackRange))
        .Sum(t => Math.Max(0, t.ExpectedDamage));

    /// <summary>Placement « proche » sûr : garde la case habituelle tant que son exposition reste sous
    /// <paramref name="maxExposurePercent"/> % des PV ; sinon prend la case la moins exposée, en préférant à exposition égale
    /// une case d'où un ennemi est à portée d'attaque au tour suivant (PM + portée max propre). Les modes Rester / Loin
    /// sont inchangés.</summary>
    public static int SelectSafe(PlacementMode mode, CombatStyle style, IReadOnlyList<int> offered,
        IReadOnlyCollection<int> enemies, IReadOnlyCollection<int> occupied, int currentCell,
        IReadOnlyCollection<PlacementThreat> threats, int ownLife, int ownReach, int maxExposurePercent = 60)
    {
        int preferred = Select(mode, style, offered, enemies, occupied, currentCell);
        bool near = mode == PlacementMode.Near || (mode == PlacementMode.LegacyByStyle && style == CombatStyle.Aggressive);
        if (preferred < 0 || !near || ownLife <= 0 || threats.Count == 0) return preferred;
        if (Exposure(preferred, threats) * 100L <= (long)ownLife * maxExposurePercent) return preferred;
        var available = offered.Where(c => c >= 0 && c < 560 && !occupied.Contains(c)).ToList();
        bool CanAttackNext(int cell) => threats.Any(t => MapPathfinder.CellDistance(cell, t.Cell) <= Math.Max(0, ownReach));
        return available.OrderBy(c => Exposure(c, threats))
            .ThenByDescending(CanAttackNext)
            .ThenBy(c => enemies.Where(e => e is >= 0 and < 560).Select(e => MapPathfinder.CellDistance(c, e)).DefaultIfEmpty(0).Min())
            .ThenBy(c => c).First();
    }

    /// <summary>Why a cell was kept: distance to the nearest known enemy and how many free cells were offered.</summary>
    public static string Explain(int chosen, IReadOnlyList<int> offered, IReadOnlyCollection<int> enemies, IReadOnlyCollection<int> occupied)
    {
        int free = offered.Count(c => c >= 0 && c < 560 && !occupied.Contains(c));
        if (chosen < 0) return $"aucune case retenue ({free} case(s) libre(s) sur {offered.Count})";
        var targets = enemies.Where(c => c >= 0 && c < 560).ToArray();
        string distance = targets.Length == 0 ? "ennemis inconnus" : $"ennemi le plus proche à {targets.Min(e => MapPathfinder.CellDistance(chosen, e))} case(s)";
        return $"{distance}, {free} case(s) libre(s) sur {offered.Count}";
    }
}
