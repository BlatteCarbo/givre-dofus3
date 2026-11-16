using System;
using GivreClone.Protocol;

namespace GivreClone.App.Services.Combat;

/// <summary>
/// Résultat de l'évaluation de légalité d'un lancer de sort.
/// Implémente une conversion implicite vers bool pour les vérifications booléennes rapides.
/// </summary>
public readonly record struct CastLegalityResult(bool Ok, string? Reason)
{
    public static readonly CastLegalityResult Success = new(true, null);
    public static CastLegalityResult Fail(string reason) => new(false, reason);
    public static implicit operator bool(CastLegalityResult r) => r.Ok;
}

/// <summary>
/// Contexte d'évaluation d'un lancer de sort : géométrie, cibles, contraintes de cellule,
/// plafonds par tour/cible, état du registre de lancers (recharge, refus) et verrous de combat (défis, PV).
/// </summary>
public sealed record CastLegalityContext
{
    public int FromCell { get; init; }
    public int TargetCell { get; init; }
    public long TargetActorId { get; init; }

    public int SpellId { get; init; }
    public int ApCost { get; init; }
    public int MinRange { get; init; }
    public int MaxRange { get; init; }
    public bool CastInLine { get; init; }
    public bool CastInDiagonal { get; init; }
    public bool CastTestLos { get; init; }
    public bool NeedFreeCell { get; init; }
    public bool NeedTakenCell { get; init; }

    public int MaxPerTurn { get; init; }
    public int MaxPerTarget { get; init; }
    public int CastsThisTurn { get; init; }
    public int CastsOnTarget { get; init; }

    public int Ap { get; init; } = int.MaxValue;
    public int TurnNo { get; init; } = 1;
    public int Cooldown { get; init; }
    public int MinCastInterval { get; init; }
    public int InitialCooldown { get; init; }

    public bool ChallengeOk { get; init; } = true;
    public bool TargetAllowed { get; init; } = true;
    public bool SelfHpOk { get; init; } = true;
    public bool TargetHpOk { get; init; } = true;
    public bool ZoneOk { get; init; } = true;

    public bool TargetOccupied { get; init; }
    public bool IsTargetMarked { get; init; }
    /// <summary>Le sort lance POSE (ou PEUT poser) une marque au sol (piege, glyphe, rune, portail) ?
    /// true = oui ; false = non (effets connus, aucune pose de marque) ; null = INCONNU (metadonnees
    /// d'effets absentes ou incertaines). Une marque au sol n'interdit QUE la pose d'une AUTRE marque : le
    /// refus serveur 229 « Il y a un piege sur cette case » concerne la pose d'un piege sur une case deja
    /// marquee, pas l'attaque, le deplacement ou le tir a travers un portail sur cette case.
    /// Le fait est exprime au niveau du SORT : un sort qui pose sa marque ailleurs que sur la case visee
    /// (14591 « Sillage » : portail sous le lanceur) donne une sur-refusal prudente, pas un defaut.</summary>
    public bool? SpellPlacesGroundMark { get; init; }
    public Func<int, bool>? BlocksLos { get; init; }
    public Func<int, bool>? Occupied { get; init; }
    public CastLedger? Ledger { get; init; }
}

/// <summary>
/// Module unifié et sans état répondant à la question « le sort S est-il lançable de la case F sur la case C maintenant ».
/// Rendu du premier motif de refus selon les libellés éprouvés du filet de combat.
/// </summary>
public static class CastLegality
{
    public static CastLegalityResult CanCast(in CastLegalityContext ctx) => Evaluate(ctx);

    public static CastLegalityResult Evaluate(in CastLegalityContext ctx)
    {
        // 1. Fenêtres de refus (CastLedger : refus ce tour, refus récent 175, case refusée 174)
        if (ctx.Ledger is not null && ctx.SpellId != 0)
        {
            if (ctx.Ledger.IsRefusedThisTurn(ctx.SpellId))
                return CastLegalityResult.Fail("sort refusé ce tour");
            if (ctx.Ledger.IsRecentlyRefused(ctx.SpellId, ctx.TurnNo))
                return CastLegalityResult.Fail("sort refusé récemment");
            if (ctx.Ledger.IsCellRefused(ctx.SpellId, ctx.TargetCell))
                return CastLegalityResult.Fail("case refusée");
        }

        // 2. Recharge (CastLedger.IsRechargeActive)
        if (ctx.Ledger is not null && ctx.SpellId != 0
            && ctx.Ledger.IsRechargeActive(ctx.SpellId, ctx.TurnNo, ctx.Cooldown, ctx.MinCastInterval, ctx.InitialCooldown))
        {
            return CastLegalityResult.Fail("recharge en cours");
        }

        // 3. Points d'action
        if (ctx.Ap < ctx.ApCost)
        {
            return CastLegalityResult.Fail($"PA {ctx.Ap}<{ctx.ApCost}");
        }

        // 4. Verrou lanceur / PV lanceur
        if (!ctx.SelfHpOk)
        {
            return CastLegalityResult.Fail("condition du sort");
        }

        // 5. Verrou défis
        if (!ctx.ChallengeOk)
        {
            return CastLegalityResult.Fail("bloqué par un défi");
        }

        // 6. Portée minimale et maximale (avec bonus de PO / PlacementRange)
        int d = MapPathfinder.CellDistance(ctx.FromCell, ctx.TargetCell);
        if (d < ctx.MinRange)
        {
            return CastLegalityResult.Fail($"trop près ({d}<{ctx.MinRange})");
        }
        if (d > ctx.MaxRange)
        {
            return CastLegalityResult.Fail($"hors portée ({d}>{ctx.MaxRange})");
        }

        // 7. Alignement, Diagonale, Ligne de vue
        if (ctx.CastInLine && !MapPathfinder.Aligned(ctx.FromCell, ctx.TargetCell))
        {
            return CastLegalityResult.Fail("pas aligné / ligne de vue");
        }
        if (ctx.CastInDiagonal && !MapPathfinder.Diagonal(ctx.FromCell, ctx.TargetCell))
        {
            return CastLegalityResult.Fail("ligne de vue / alignement");
        }
        if (ctx.CastTestLos && d > 1 && ctx.BlocksLos is not null && ctx.Occupied is not null)
        {
            if (!MapPathfinder.HasLineOfSight(ctx.FromCell, ctx.TargetCell, ctx.BlocksLos, ctx.Occupied))
            {
                return CastLegalityResult.Fail(ctx.CastInLine ? "pas aligné / ligne de vue" : "ligne de vue / alignement");
            }
        }

        // 8. Cible autorisée (visibilité, défi cible)
        if (!ctx.TargetAllowed)
        {
            return CastLegalityResult.Fail("cible non autorisée (visibilité ou défi)");
        }

        // 9. Verrou cible / PV cible
        if (!ctx.TargetHpOk)
        {
            return CastLegalityResult.Fail("condition de la cible");
        }

        // 10. Contraintes de cellule : case libre requise, case occupée requise, marque au sol (pièges Sram)
        if (ctx.NeedFreeCell && ctx.TargetOccupied)
        {
            return CastLegalityResult.Fail("case ciblée invalide");
        }
        if (ctx.NeedTakenCell && !ctx.TargetOccupied)
        {
            return CastLegalityResult.Fail("case ciblée invalide");
        }
        // Une marque au sol (piège, glyphe, rune, portail) n'interdit QUE la POSE d'une AUTRE marque : le serveur
        // refuse (message 229 « Il y a un piège sur cette case ») de poser un piège sur une case DÉJÀ marquée.
        // Attaquer, se déplacer, se téléporter ou tirer À TRAVERS un portail sur une case marquée reste LÉGAL :
        // le refus inconditionnel coupait le tour (portail d'entrée Eliotrope visé par le planificateur puis
        // refusé à l'exécution) et interdisait toute attaque sur case de glyphe.
        // Nature de pose INCONNUE (métadonnées d'effets absentes ou incertaines) -> doute conservé, on refuse
        // (prudence) : `false` doit signifier « effets connus, aucune pose de marque ».
        // Limite connue : la règle ne porte que sur la case LIBRE (une case occupée ET marquée échappe au conflit).
        if (!ctx.TargetOccupied && ctx.IsTargetMarked && ctx.SpellPlacesGroundMark != false)
        {
            return CastLegalityResult.Fail("case ciblée invalide");
        }

        // 11. Zone (dégâts alliés interdits, défis de zone)
        if (!ctx.ZoneOk)
        {
            return CastLegalityResult.Fail("zone (allié ou défi)");
        }

        // 12. Plafonds de lancers par tour et par cible
        if (ctx.MaxPerTurn > 0 && ctx.CastsThisTurn >= ctx.MaxPerTurn)
        {
            return CastLegalityResult.Fail("plafond de lancers");
        }
        if (ctx.MaxPerTarget > 0 && ctx.TargetActorId != 0 && ctx.CastsOnTarget >= ctx.MaxPerTarget)
        {
            return CastLegalityResult.Fail("plafond de lancers");
        }

        return CastLegalityResult.Success;
    }

    /// <summary>
    /// Contrôle géométrique pur : portée [min, range] + ligne + diagonale + ligne de vue.
    /// </summary>
    public static bool CanCastGeometry(int from, int to, int minRange, int range,
        bool castInLine, bool castInDiagonal, bool castTestLos,
        Func<int, bool>? blocksLos, Func<int, bool>? occupied, out string? reason)
    {
        int d = MapPathfinder.CellDistance(from, to);
        if (d < minRange)
        {
            reason = $"trop près ({d}<{minRange})";
            return false;
        }
        if (d > range)
        {
            reason = $"hors portée ({d}>{range})";
            return false;
        }
        if (castInLine && !MapPathfinder.Aligned(from, to))
        {
            reason = "pas aligné / ligne de vue";
            return false;
        }
        if (castInDiagonal && !MapPathfinder.Diagonal(from, to))
        {
            reason = "ligne de vue / alignement";
            return false;
        }
        if (castTestLos && d > 1 && blocksLos is not null && occupied is not null)
        {
            if (!MapPathfinder.HasLineOfSight(from, to, blocksLos, occupied))
            {
                reason = castInLine ? "pas aligné / ligne de vue" : "ligne de vue / alignement";
                return false;
            }
        }
        reason = null;
        return true;
    }

    public static bool CanCastGeometry(int from, int to, int minRange, int range,
        bool castInLine, bool castInDiagonal, bool castTestLos,
        Func<int, bool>? blocksLos, Func<int, bool>? occupied)
        => CanCastGeometry(from, to, minRange, range, castInLine, castInDiagonal, castTestLos, blocksLos, occupied, out _);

    /// <summary>
    /// Contraintes de cellule d'un sort : case libre, case occupée, case marquée (pièges Sram).
    /// Même règle que <see cref="Evaluate"/> : une case marquée n'est refusée que si le sort POSE une marque
    /// (<paramref name="spellPlacesGroundMark"/> = true) ou si le fait est INCONNU (null, prudence).
    /// </summary>
    public static bool CastCellConstraintsOk(bool needFreeCell, bool needTakenCell, bool occupied,
        bool isMarked = false, bool? spellPlacesGroundMark = null)
    {
        if (needFreeCell && occupied) return false;
        if (needTakenCell && !occupied) return false;
        if (!occupied && isMarked && spellPlacesGroundMark != false) return false;
        return true;
    }

    /// <summary>
    /// Conditions SELF/GLOBAL d'un sort : PA mini, tour mini, ennemis mini, PV% du lanceur.
    /// </summary>
    public static bool SelfSpellGateOk(int minAp, int minTurn, int minEnemies, int selfHpMin, int selfHpMax,
        int ap, int turnNo, int enemyCount, int? selfHpPercent)
    {
        if (minAp > 0 && ap < minAp) return false;
        if (minTurn > 0 && turnNo < minTurn) return false;
        if (minEnemies > 0 && enemyCount < minEnemies) return false;
        if ((selfHpMin > 0 || selfHpMax < 100) && selfHpPercent is int hp && (hp < selfHpMin || hp > selfHpMax)) return false;
        return true;
    }

    /// <summary>
    /// Condition PV de la cible d'un sort. PV cible inconnu -> true (fail-open).
    /// </summary>
    public static bool TargetSpellGateOk(int tgtHpMin, int tgtHpMax, int? targetHpPercent)
    {
        if (tgtHpMin <= 0 && tgtHpMax >= 100) return true;
        return targetHpPercent is not int hp || (hp >= tgtHpMin && hp <= tgtHpMax);
    }
}
