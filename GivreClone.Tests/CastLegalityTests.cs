extern alias F06App;
using System.Collections.Generic;
using System.Collections.Immutable;
using GivreClone.Protocol;
using Xunit;
using CastLegality = F06App::GivreClone.App.Services.Combat.CastLegality;
using CastLegalityContext = F06App::GivreClone.App.Services.Combat.CastLegalityContext;
using CastLegalityResult = F06App::GivreClone.App.Services.Combat.CastLegalityResult;
using CastLedger = F06App::GivreClone.App.Services.Combat.CastLedger;
using SpellCastDescriptor = F06App::GivreClone.App.Services.Combat.SpellCastDescriptor;
using SpellEffectDescriptor = F06App::GivreClone.App.Services.Combat.SpellEffectDescriptor;
using SpellEffectKind = F06App::GivreClone.App.Services.Combat.SpellEffectKind;

namespace GivreClone.Tests;

public class CastLegalityTests
{
    private static CastLegalityContext BaseContext(int from = 200, int to = 201) => new()
    {
        FromCell = from,
        TargetCell = to,
        SpellId = 100,
        ApCost = 3,
        Ap = 6,
        MinRange = 1,
        MaxRange = 6,
        CastInLine = false,
        CastInDiagonal = false,
        CastTestLos = false,
        NeedFreeCell = false,
        NeedTakenCell = false,
        MaxPerTurn = 0,
        MaxPerTarget = 0,
        CastsThisTurn = 0,
        CastsOnTarget = 0,
        TurnNo = 1,
        ChallengeOk = true,
        TargetAllowed = true,
        SelfHpOk = true,
        TargetHpOk = true,
        ZoneOk = true,
        TargetOccupied = false,
        IsTargetMarked = false,
        BlocksLos = _ => false,
        Occupied = _ => false,
    };

    [Fact]
    public void Rule01_Range_too_far_returns_failure()
    {
        int from = 100;
        int to = 300;
        int d = MapPathfinder.CellDistance(from, to);
        Assert.True(d > 6);

        var ctx = BaseContext(from, to) with { MinRange = 1, MaxRange = 6 };
        var res = CastLegality.CanCast(ctx);

        Assert.False(res.Ok);
        Assert.Contains("hors portée", res.Reason);
    }

    [Fact]
    public void Rule02_MinRange_too_close_returns_failure()
    {
        var ctx = BaseContext(200, 200) with { MinRange = 2, MaxRange = 6 };
        var res = CastLegality.CanCast(ctx);

        Assert.False(res.Ok);
        Assert.Contains("trop près", res.Reason);
    }

    [Fact]
    public void Rule03_LineOfSight_blocked_returns_failure()
    {
        int from = 200;
        int to = 202;
        int d = MapPathfinder.CellDistance(from, to);
        Assert.True(d > 1);

        var ctx = BaseContext(from, to) with
        {
            CastTestLos = true,
            BlocksLos = c => c != from && c != to,
            Occupied = c => c != from && c != to,
        };
        var res = CastLegality.CanCast(ctx);

        Assert.False(res.Ok);
        Assert.Contains("ligne de vue", res.Reason);

        var clearCtx = ctx with { BlocksLos = _ => false, Occupied = _ => false };
        var clearRes = CastLegality.CanCast(clearCtx);
        Assert.True(clearRes.Ok);
    }

    [Fact]
    public void Rule04_LineOnly_not_aligned_returns_failure()
    {
        int from = 200;
        int to = 201;
        while (MapPathfinder.Aligned(from, to)) to++;

        var ctx = BaseContext(from, to) with { CastInLine = true };
        var res = CastLegality.CanCast(ctx);

        Assert.False(res.Ok);
        Assert.Equal("pas aligné / ligne de vue", res.Reason);
    }

    [Fact]
    public void Rule05_DiagonalOnly_not_diagonal_returns_failure()
    {
        int from = 200;
        int to = 200;
        while (!MapPathfinder.Aligned(from, to) || from == to) to++;
        Assert.False(MapPathfinder.Diagonal(from, to));

        var ctx = BaseContext(from, to) with { CastInDiagonal = true };
        var res = CastLegality.CanCast(ctx);

        Assert.False(res.Ok);
        Assert.Equal("ligne de vue / alignement", res.Reason);
    }

    [Fact]
    public void Rule06_FreeCell_required_fails_when_target_occupied()
    {
        var ctx = BaseContext() with { NeedFreeCell = true, TargetOccupied = true };
        var res = CastLegality.CanCast(ctx);

        Assert.False(res.Ok);
        Assert.Equal("case ciblée invalide", res.Reason);

        var okCtx = ctx with { TargetOccupied = false };
        Assert.True(CastLegality.CanCast(okCtx).Ok);
    }

    [Fact]
    public void Rule07_OccupiedCell_required_fails_when_target_empty()
    {
        var ctx = BaseContext() with { NeedTakenCell = true, TargetOccupied = false };
        var res = CastLegality.CanCast(ctx);

        Assert.False(res.Ok);
        Assert.Equal("case ciblée invalide", res.Reason);

        var okCtx = ctx with { TargetOccupied = true };
        Assert.True(CastLegality.CanCast(okCtx).Ok);
    }

    [Fact]
    public void Rule08_MarkedCell_fails_on_empty_cell()
    {
        // Nature de pose de marque INCONNUE (metadonnees d'effets absentes) : le doute est conserve, la case
        // marquee reste refusee comme case de LANCER de marque (comportement prudent, jamais plus permissif
        // qu'avant le correctif).
        var ctx = BaseContext() with { TargetOccupied = false, IsTargetMarked = true };
        Assert.Null(ctx.SpellPlacesGroundMark);
        var res = CastLegality.CanCast(ctx);

        Assert.False(res.Ok);
        Assert.Equal("case ciblée invalide", res.Reason);

        var okCtx = ctx with { IsTargetMarked = false };
        Assert.True(CastLegality.CanCast(okCtx).Ok);
    }

    [Fact]
    public void Rule09_PerTurnCap_exceeded_returns_failure()
    {
        var ctx = BaseContext() with { MaxPerTurn = 2, CastsThisTurn = 2 };
        var res = CastLegality.CanCast(ctx);

        Assert.False(res.Ok);
        Assert.Equal("plafond de lancers", res.Reason);

        var okCtx = ctx with { CastsThisTurn = 1 };
        Assert.True(CastLegality.CanCast(okCtx).Ok);
    }

    [Fact]
    public void Rule10_PerTargetCap_exceeded_returns_failure()
    {
        var ctx = BaseContext() with { MaxPerTarget = 1, TargetActorId = 555, CastsOnTarget = 1 };
        var res = CastLegality.CanCast(ctx);

        Assert.False(res.Ok);
        Assert.Equal("plafond de lancers", res.Reason);

        var okCtx = ctx with { CastsOnTarget = 0 };
        Assert.True(CastLegality.CanCast(okCtx).Ok);
    }

    [Fact]
    public void Rule11_Recharge_active_returns_failure()
    {
        var ledger = new CastLedger();
        ledger.StartTurn(1);
        ledger.RecordCast(100, 0, turnNo: 1);

        var ctx = BaseContext() with
        {
            SpellId = 100,
            TurnNo = 2,
            Cooldown = 2,
            Ledger = ledger,
        };
        var res = CastLegality.CanCast(ctx);

        Assert.False(res.Ok);
        Assert.Equal("recharge en cours", res.Reason);

        var okCtx = ctx with { TurnNo = 4 };
        Assert.True(CastLegality.CanCast(okCtx).Ok);
    }

    [Fact]
    public void Rule12_Refusal_175_recently_refused_returns_failure()
    {
        var ledger = new CastLedger();
        ledger.StartTurn(2);
        ledger.RecordRefusal(100, cell: 201, messageId: 175, turnNo: 2);

        ledger.StartTurn(3);
        var ctx = BaseContext() with
        {
            SpellId = 100,
            TurnNo = 3,
            Ledger = ledger,
        };
        var res = CastLegality.CanCast(ctx);

        Assert.False(res.Ok);
        Assert.Equal("sort refusé récemment", res.Reason);

        ledger.StartTurn(4);
        var okCtx = ctx with { TurnNo = 4 };
        Assert.True(CastLegality.CanCast(okCtx).Ok);
    }

    [Fact]
    public void Rule13_Refusal_174_cell_refused_returns_failure()
    {
        var ledger = new CastLedger();
        ledger.StartTurn(1);
        ledger.RecordRefusal(100, cell: 201, messageId: 174, turnNo: 1);

        var ctx = BaseContext(200, 201) with
        {
            SpellId = 100,
            TurnNo = 1,
            Ledger = ledger,
        };
        var res = CastLegality.CanCast(ctx);

        Assert.False(res.Ok);
        Assert.Equal("case refusée", res.Reason);

        var okCtx = ctx with { TargetCell = 202 };
        Assert.True(CastLegality.CanCast(okCtx).Ok);
    }

    [Fact]
    public void Rule14_Refusal_this_turn_returns_failure()
    {
        var ledger = new CastLedger();
        ledger.StartTurn(1);
        ledger.RecordRefusal(100, cell: 201, messageId: 229, turnNo: 1);

        var ctx = BaseContext() with
        {
            SpellId = 100,
            TurnNo = 1,
            Ledger = ledger,
        };
        var res = CastLegality.CanCast(ctx);

        Assert.False(res.Ok);
        Assert.Equal("sort refusé ce tour", res.Reason);
    }

    [Fact]
    public void Rule15_Challenge_blocked_returns_failure()
    {
        var ctx = BaseContext() with { ChallengeOk = false };
        var res = CastLegality.CanCast(ctx);

        Assert.False(res.Ok);
        Assert.Equal("bloqué par un défi", res.Reason);

        var okCtx = ctx with { ChallengeOk = true };
        Assert.True(CastLegality.CanCast(okCtx).Ok);
    }

    [Fact]
    public void Rule16_HpGate_self_fails_when_out_of_bounds()
    {
        Assert.False(CastLegality.SelfSpellGateOk(minAp: 0, minTurn: 0, minEnemies: 0, selfHpMin: 50, selfHpMax: 100, ap: 6, turnNo: 1, enemyCount: 1, selfHpPercent: 30));
        Assert.True(CastLegality.SelfSpellGateOk(minAp: 0, minTurn: 0, minEnemies: 0, selfHpMin: 50, selfHpMax: 100, ap: 6, turnNo: 1, enemyCount: 1, selfHpPercent: 70));

        var ctx = BaseContext() with { SelfHpOk = false };
        var res = CastLegality.CanCast(ctx);

        Assert.False(res.Ok);
        Assert.Equal("condition du sort", res.Reason);
    }

    [Fact]
    public void Rule17_HpGate_target_fails_when_out_of_bounds()
    {
        Assert.False(CastLegality.TargetSpellGateOk(tgtHpMin: 50, tgtHpMax: 100, targetHpPercent: 30));
        Assert.True(CastLegality.TargetSpellGateOk(tgtHpMin: 50, tgtHpMax: 100, targetHpPercent: 70));

        var ctx = BaseContext() with { TargetHpOk = false };
        var res = CastLegality.CanCast(ctx);

        Assert.False(res.Ok);
        Assert.Equal("condition de la cible", res.Reason);
    }

    // Matrice des contraintes de CELLULE, avec la nature de pose du sort (true = pose une marque,
    // false = n'en pose aucune, null = INCONNU). Couvre les trois natures exigees par la revue :
    // attaque connue, pose de marque connue, nature inconnue.
    public static TheoryData<bool, bool, bool, bool, bool?> CellConstraintMatrix() => new()
    {
        { false, false, false, false, false },   // case libre, non marquee : rien ne s'y oppose
        { false, false, false, true,  false },   // attaque connue sur case marquee   -> AUTORISE
        { false, false, false, true,  true },    // pose de marque sur case marquee  -> REFUSE
        { false, false, false, true,  null },    // nature inconnue sur case marquee -> REFUSE (prudence)
        { true,  false, true,  false, false },   // case libre requise mais occupee -> REFUSE
        { false, true,  false, false, false },   // case occupee requise mais libre -> REFUSE
        { false, false, true,  true,  true },    // case occupee ET marquee : le conflit ne porte que sur la case LIBRE
    };

    /// <summary>Contrats REELS des deux predicats de production : `Evaluate` decide de TOUT le lancer,
    /// `CastCellConstraintsOk` ne traite QUE les contraintes de cellule (case libre / case occupee /
    /// case marquee). Sur un contexte par ailleurs valide, les deux doivent s'accorder, et `Evaluate`
    /// doit nommer la meme cause. Compare les deux predicats (et non CanCast a lui-meme, qui EST Evaluate).</summary>
    [Theory]
    [MemberData(nameof(CellConstraintMatrix))]
    public void Rule18_Evaluate_and_CastCellConstraintsOk_agree_on_their_real_cell_contracts(
        bool needFreeCell, bool needTakenCell, bool occupied, bool isMarked, bool? placesGroundMark)
    {
        var ctx = BaseContext() with
        {
            NeedFreeCell = needFreeCell,
            NeedTakenCell = needTakenCell,
            TargetOccupied = occupied,
            IsTargetMarked = isMarked,
            SpellPlacesGroundMark = placesGroundMark,
        };

        bool cellsOk = CastLegality.CastCellConstraintsOk(
            needFreeCell, needTakenCell, occupied, isMarked, placesGroundMark);
        var res = CastLegality.CanCast(ctx);

        Assert.Equal(cellsOk, res.Ok);
        if (cellsOk) Assert.Null(res.Reason);
        else Assert.Equal("case ciblée invalide", res.Reason);
    }

    // ── Marque au sol : seule la POSE d'une marque entre en conflit avec une marque existante ──────────
    // Le refus serveur 229 « Il y a un piege sur cette case » sanctionne la POSE d'un piege sur une case DEJA
    // marquee. Il ne sanctionne ni une attaque, ni un deplacement, ni un tir A TRAVERS un portail Eliotrope
    // (le portail est lui-meme une marque) sur une case marquee. Rejeter ces lancers coupait le tour entier.

    [Fact]
    public void Rule19_Attack_on_a_glyph_cell_is_allowed_although_the_cell_carries_a_mark()
    {
        // Attaque (elle ne pose aucune marque au sol) vise une case portant un glyphe : le serveur l'accepte.
        var ctx = BaseContext() with
        {
            TargetOccupied = false,
            IsTargetMarked = true,
            SpellPlacesGroundMark = false,
        };

        var res = CastLegality.CanCast(ctx);

        Assert.True(res.Ok);
        Assert.Null(res.Reason);
    }

    [Fact]
    public void Rule20_Movement_onto_a_marked_cell_is_allowed()
    {
        // Teleportation : la case d'arrivee peut porter une marque (piege, glyphe, rune) ; le sort ne pose
        // aucune marque, le deplacement reste legal.
        var ctx = BaseContext() with
        {
            TargetOccupied = false,
            IsTargetMarked = true,
            NeedFreeCell = true,          // un sort de teleportation vise une case libre
            SpellPlacesGroundMark = false,
        };

        var res = CastLegality.CanCast(ctx);

        Assert.True(res.Ok);
        Assert.Null(res.Reason);
    }

    [Fact]
    public void Rule21_Portal_entry_shot_is_allowed_although_the_portal_cell_carries_a_mark()
    {
        // Portail Eliotrope : la case du portail porte une MARQUE (FightMark.mark_type) et le planificateur
        // vise cette case pour tirer A TRAVERS le reseau de portails. Le tir ne doit pas etre refuse.
        var ctx = BaseContext() with
        {
            SpellId = 14613,              // « Sermon » (attaque, reference/eliotrope-sorts-donnees-client-20261109.md)
            TargetOccupied = false,
            IsTargetMarked = true,
            SpellPlacesGroundMark = false,
        };

        var res = CastLegality.CanCast(ctx);

        Assert.True(res.Ok);
        Assert.Null(res.Reason);
    }

    [Fact]
    public void Rule22_Trap_placement_on_an_already_marked_cell_is_rejected()
    {
        // Pose de piege sur une case qui porte DEJA une marque : conflit reel (refus 229 « Il y a un piege
        // sur cette case ») -> la pose doit etre refusee LOCALEMENT, sans depenser de PA.
        var ctx = BaseContext() with
        {
            TargetOccupied = false,
            IsTargetMarked = true,
            SpellPlacesGroundMark = true,
        };

        var res = CastLegality.CanCast(ctx);

        Assert.False(res.Ok);
        Assert.Equal("case ciblée invalide", res.Reason);

        // Meme sort de pose de marque sur une case NON marquee : autorise.
        Assert.True(CastLegality.CanCast(ctx with { IsTargetMarked = false }).Ok);
    }

    [Fact]
    public void Rule23_Spell_descriptor_reports_ground_mark_placement_from_the_client_effect_actions()
    {
        // Metadonnees reelles : action 400 = pose de piege (sorts 12914/12921, voir
        // docs/validation/sram-mark-catalog-join-20261106.md), action 1181 = « Pose un portail »
        // (Eliotrope, reference/eliotrope-sorts-donnees-client-20261109.md), action 98 = degats (rien pose).
        var trap = new SpellCastDescriptor(12921, 2, 3, 0, 6, 8, false, 0, 0, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(400, SpellEffectKind.Unknown, 0, 0, 2, -1, "C", 'P', 0, 0)));
        var portal = new SpellCastDescriptor(14574, 3, 1, 1, 6, 8, false, 0, 0, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(1181, SpellEffectKind.Unknown, 0, 0, 0, -1, "C", 'P', 0, 0)));
        var shot = new SpellCastDescriptor(14613, 3, 4, 0, 6, 0, false, 0, 0, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(98, SpellEffectKind.Damage, 38, 42, 16, 0, "A", 'P', 0, 0)));
        var noEffects = new SpellCastDescriptor(4242, 1, 2, 0, 2, 0, false, 0, 0, 0, 0, "",
            ImmutableArray<SpellEffectDescriptor>.Empty);

        Assert.True(trap.PlacesGroundMark);
        Assert.True(portal.PlacesGroundMark);
        Assert.False(shot.PlacesGroundMark);
        Assert.Null(noEffects.PlacesGroundMark);      // metadonnees absentes : INCONNU, pas « non »
    }

    [Fact]
    public void Rule24_Descriptor_fact_drives_the_production_predicate()
    {
        // Chaine complete : descripteur client -> fait « pose de marque » -> predicite de production
        // (CastLegality.CanCast, appele par le planificateur ET par la rotation classique).
        var trap = new SpellCastDescriptor(12921, 2, 3, 0, 6, 8, false, 0, 0, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(400, SpellEffectKind.Unknown, 0, 0, 2, -1, "C", 'P', 0, 0)));
        var shot = new SpellCastDescriptor(14613, 3, 4, 0, 6, 0, false, 0, 0, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(98, SpellEffectKind.Damage, 38, 42, 16, 0, "A", 'P', 0, 0)));

        var trapCtx = BaseContext() with
        {
            TargetOccupied = false,
            IsTargetMarked = true,
            SpellPlacesGroundMark = trap.PlacesGroundMark,
        };
        var shotCtx = trapCtx with { SpellPlacesGroundMark = shot.PlacesGroundMark };

        Assert.False(CastLegality.CanCast(trapCtx).Ok);   // pose de piege sur case marquee : refuse
        Assert.True(CastLegality.CanCast(shotCtx).Ok);      // tir sur la meme case marquee : accepte
        Assert.Equal(CastLegality.CanCast(trapCtx).Reason, CastLegality.Evaluate(trapCtx).Reason);
    }

    // ── Metadonnees INCERTAINES : « inconnu » ne doit JAMAIS devenir « faux » ────────────────────────────
    // Le chargeur peut perdre UNE ligne d'effet : dans GameNameResolver.ReadSpellDamage, une exception sur
    // delay / random / effectElement / diceNum / diceSide / effectCondition fait sauter la ligne entiere
    // (elle n'est pas ajoutee a `retained`) et le descripteur sort avec `Uncertain = true`. La liste d'effets
    // est alors NON VIDE mais INCOMPLETE : le fait « pose une marque au sol » doit rester INCONNU (null).
    // `false` est la SEULE valeur qui deverrouille le predicite -> une incertitude de lecture ne doit
    // jamais devenir une permission.

    private static SpellCastDescriptor PartialTrapDescriptor() => new(12921, 2, 3, 0, 6, 8, false, 0, 0, 0, 0, "",
        ImmutableArray.Create(new SpellEffectDescriptor(98, SpellEffectKind.Damage, 38, 42, 16, 0, "A", 'P', 0, 0)),
        Uncertain: true);   // l'effet de pose (400) est celui qui a echoue : il a ete OMIS

    [Fact]
    public void Rule25_Uncertain_descriptor_never_affirms_that_the_spell_places_no_ground_mark()
    {
        // Effets partiels (non vides) + descripteur incertain : le fait doit rester INCONNU.
        Assert.Null(PartialTrapDescriptor().PlacesGroundMark);

        // Effets COMPLETS mais descripteur incertain : meme verdict.
        // CAUTION : `Uncertain` est aussi pose par le chargeur pour un motif sans rapport avec la completude
        // des effets (`globalCooldown != 0`, GameNameResolver) -> ces sorts retombent sur le refus prudent
        // (comportement d'avant le correctif). C'est un perimetre plus etroit, jamais plus permissif.
        var completeButUncertain = new SpellCastDescriptor(14574, 3, 1, 1, 6, 8, false, 0, 0, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(1181, SpellEffectKind.Unknown, 0, 0, 0, -1, "C", 'P', 0, 0)),
            Uncertain: true);
        Assert.Null(completeButUncertain.PlacesGroundMark);
    }

    [Fact]
    public void Rule26_Uncertain_partial_descriptor_keeps_the_prudent_refusal_on_a_marked_cell()
    {
        // Consequence de bout en bout : la pose d'un piege dont les metadonnees sont partielles reste
        // refusee localement sur une case deja marquee (pas de PA gaspillee, pas de refus serveur 229).
        var ctx = BaseContext() with
        {
            TargetOccupied = false,
            IsTargetMarked = true,
            SpellPlacesGroundMark = PartialTrapDescriptor().PlacesGroundMark,
        };

        var res = CastLegality.CanCast(ctx);

        Assert.False(res.Ok);
        Assert.Equal("case ciblée invalide", res.Reason);
    }
}
