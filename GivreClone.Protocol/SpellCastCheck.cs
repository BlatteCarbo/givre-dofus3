using System;

namespace GivreClone.Protocol;

/// <summary>Raison d'impossibilité de lancer un sort — codes IDENTIQUES à un bot tiers <c>SpellInabilityReason</c> (portabilité des
/// scripts : <c>fightAction:canCastSpellOnCell</c> rend ce nombre, 0 = OK). 9/12/13 (invocations max, états requis/interdits)
/// ne sont pas évalués (données non suivies en direct) et ne sont donc jamais rendus.</summary>
public enum SpellCastVerdict
{
    None = 0, TooManyLaunch = 1, Cooldown = 2, ActionPoints = 3, TooManyLaunchOnCell = 4,
    MaxRange = 5, MinRange = 6, NotInLine = 7, LineOfSight = 8, NeedFreeCell = 10, NeedTakenCell = 11,
    NotInDiagonal = 14, UnknownSpell = 15, Unknown = 16,
}

/// <summary>Entrée PURE du test de lancer (tout est résolu par l'appelant : données du sort, compteurs du tour, occupation).</summary>
public readonly record struct SpellCastInput(int From, int Target, int ApCost, int MinRange, int MaxRange,
    bool CastInLine, bool CastInDiagonal, bool CastTestLos, int ApLeft, bool OnCooldown,
    int CastsThisTurn, int MaxPerTurn, int CastsOnTarget, int MaxPerTarget,
    bool NeedFreeCell, bool NeedTakenCell, bool TargetOccupied);

/// <summary>
/// Test « ce sort est-il lançable de From sur Target ? » avec la RAISON. La partie
/// géométrique est IDENTIQUE au moteur (<c>LaunchService.CanCast</c>) : portée diamant [min,max], contrainte en ligne /
/// diagonale, et ligne de vue SEULEMENT si le sort l'exige (castTestLos) ET à distance (d &gt; 1) — jamais de LdV forcée.
/// Purement géométrique + compteurs : 0 réseau, testable hors app.
/// </summary>
public static class SpellCastCheck
{
    public static SpellCastVerdict Evaluate(in SpellCastInput x, Func<int, bool> blocksLos, Func<int, bool> occupied)
    {
        if (x.ApLeft < x.ApCost) return SpellCastVerdict.ActionPoints;
        if (x.OnCooldown) return SpellCastVerdict.Cooldown;
        if (x.MaxPerTurn > 0 && x.CastsThisTurn >= x.MaxPerTurn) return SpellCastVerdict.TooManyLaunch;
        int d = MapPathfinder.CellDistance(x.From, x.Target);
        if (d < x.MinRange) return SpellCastVerdict.MinRange;
        if (d > x.MaxRange) return SpellCastVerdict.MaxRange;
        if (x.CastInLine && !MapPathfinder.Aligned(x.From, x.Target)) return SpellCastVerdict.NotInLine;
        if (x.CastInDiagonal && !MapPathfinder.Diagonal(x.From, x.Target)) return SpellCastVerdict.NotInDiagonal;
        if (x.NeedTakenCell && !x.TargetOccupied) return SpellCastVerdict.NeedTakenCell;
        if (x.NeedFreeCell && x.TargetOccupied) return SpellCastVerdict.NeedFreeCell;
        if (x.MaxPerTarget > 0 && x.CastsOnTarget >= x.MaxPerTarget) return SpellCastVerdict.TooManyLaunchOnCell;
        if (x.CastTestLos && d > 1 && !MapPathfinder.HasLineOfSight(x.From, x.Target, blocksLos, occupied)) return SpellCastVerdict.LineOfSight;
        return SpellCastVerdict.None;
    }
}
