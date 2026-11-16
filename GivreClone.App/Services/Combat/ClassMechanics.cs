using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GivreClone.Protocol;
using GivreCore.Protobuf;

namespace GivreClone.App.Services.Combat;

public static class ClassMechanics
{
    public const int RogueBreedId = 13;

    // Installed 3.7.13.17 SpellBombData: Explobombe summon template 3112, wall data id 2,
    // linear wall with minHop=1 and maxHop=7. Treat these hop bounds only as a
    // conservative candidate geometry filter, NOT as verified wall activation
    // or an interpreted server collision rule. Other templates and actual wall
    // activation are NOT established by these rows.
    // Source: docs/validation/roublard-bomb-contract-20261105.md.
    public const int VerifiedExplobombTemplate = 3112;
    public const int VerifiedExplobombWall = 2;
    private const int ExplobombWallMinimumGap = 1;
    private const int ExplobombWallMaximumGap = 7;

    public static bool IsRogueBomb(PlannerFighter fighter, long casterId)
    {
        // Never attribute an unknown summon, an enemy or an unconfirmed location
        // to the Roublard. 2831..2836 were not verified as bomb templates in the
        // installed SpellBombData and must not be interpreted as such here.
        return casterId != 0 && !fighter.Enemy && fighter.Life > 0
            && fighter.SummonerId == casterId
            && fighter.PositionCertainty == FighterPositionCertainty.Confirmed
            && fighter.MonsterTemplate == VerifiedExplobombTemplate;
    }

    /// <summary>
    /// Candidate geometry only, NOT proof of a live bomb wall or its damage.
    /// Installed SpellBombWallData id 2 declares linear minHop=1/maxHop=7.
    /// </summary>
    public static ImmutableArray<int> GetBombWallCells(int cellA, int cellB, Func<int, bool> isWalkable)
    {
        if (cellA == cellB || (uint)cellA >= MapPathfinder.CellCount
            || (uint)cellB >= MapPathfinder.CellCount || !MapPathfinder.Aligned(cellA, cellB))
            return ImmutableArray<int>.Empty;
        int dist = MapPathfinder.CellDistance(cellA, cellB);
        if (dist - 1 < ExplobombWallMinimumGap || dist - 1 > ExplobombWallMaximumGap)
            return ImmutableArray<int>.Empty;

        var line = MapPathfinder.StraightLine(cellA, cellB).ToArray();
        // A truncated line is not a wall through missing terrain, even when its
        // endpoints happen to be aligned in the global 560-cell geometry.
        if (line.Length != dist + 1 || line[0] != cellA || line[^1] != cellB)
            return ImmutableArray<int>.Empty;
        var wallCells = line.Skip(1).Take(dist - 1).ToImmutableArray();
        return wallCells.All(isWalkable) ? wallCells : ImmutableArray<int>.Empty;
    }

    /// <summary>
    /// Evidence supplied by a future server-event adapter, not derivable from two
    /// bomb locations alone. The wall data ID and exact active cells must be
    /// separately observed and attributed to both distinct owned bomb actors.
    /// </summary>
    public sealed record VerifiedRogueWall(int WallDataId, long OwnerId, long FirstBombId,
        long SecondBombId, ImmutableHashSet<int> ActiveCells, bool Active);

    /// <summary>
    /// Existing planner call: no active wall event/owner/identity is supplied.
    /// It is unsafe to award an imaginary +85 wall-hit bonus just because two
    /// bombs appear on the same line. Leave this heuristic neutral until the
    /// caller has authoritative wall evidence and uses the overload below.
    /// </summary>
    public static double EvaluateRogueBombTactics(
        int casterCell,
        IEnumerable<PlannerFighter> bombs,
        IEnumerable<PlannerFighter> enemies,
        Func<int, bool> isWalkable)
        => 0;

    /// <summary>
    /// Positional heuristic for server-confirmed active Explobombe wall cells.
    /// This is NOT an explosion/detonation/damage estimate. One visible enemy
    /// actor on a verified wall scores once, even when walls overlap.
    /// </summary>
    public static double EvaluateRogueBombTactics(long casterId,
        IEnumerable<PlannerFighter> bombs, IEnumerable<PlannerFighter> enemies,
        Func<int, bool> isWalkable, IEnumerable<VerifiedRogueWall> observedWalls)
    {
        if (casterId == 0) return 0;
        var owned = bombs.Where(b => IsRogueBomb(b, casterId))
            .GroupBy(b => b.Id).Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single());
        var activeCells = new HashSet<int>();
        foreach (var wall in observedWalls)
        {
            if (!wall.Active || wall.WallDataId != VerifiedExplobombWall || wall.OwnerId != casterId
                || wall.FirstBombId == wall.SecondBombId || wall.ActiveCells is null
                || !owned.TryGetValue(wall.FirstBombId, out var first)
                || !owned.TryGetValue(wall.SecondBombId, out var second)) continue;
            var candidate = GetBombWallCells(first.Cell, second.Cell, isWalkable);
            if (candidate.IsEmpty || !wall.ActiveCells.SetEquals(candidate)) continue;
            activeCells.UnionWith(candidate);
        }
        // Bounded preference for an already observed tactical occupancy, never
        // a claimed HP loss or fabricated bomb age/combo multiplier.
        return enemies.Where(e => e.Enemy && e.Life > 0 && e.CanTarget && activeCells.Contains(e.Cell))
            .Select(e => e.Id).Distinct().Count() * 12.0;
    }

    // Id de classe Dofus (1-based, = ClassIconProvider.BreedId(index)) : Xélor a breedIndex=4 -> id 5.
    public const int XelorBreedId = 5;
    public const int StateTelefrag = 251;
    public const int StateAllyTelefrag = 244;

    public static bool GeneratesTelefrag(int fromCell, int toCell, int targetCell, bool isOccupied)
    {
        if (fromCell == toCell) return false;
        return targetCell == toCell && isOccupied;
    }

    public static double EvaluateXelorTelefrag(bool triggersTelefrag, bool targetHasTelefrag)
    {
        if (triggersTelefrag)
        {
            return targetHasTelefrag ? 30.0 : 65.0;
        }
        return 0;
    }

    // Id de classe Dofus (1-based, = ClassIconProvider.BreedId(index)) : Eliobot a breedIndex=15 -> id 16. La valeur 15
    // (Steamer) desactivait la reconnaissance des portails et les bonus Eliotrope du planificateur.
    public const int EliotropeBreedId = 16;

    public sealed record Portal(int Id, int Cell, long OwnerId, bool Active = true);

    public static double PortalDamageMultiplier(int networkDistance)
    {
        if (networkDistance <= 0) return 1.0;
        // +2 % par case d'eloignement entre portails : donnees client, action 1181 diceNum = 2 (le simulateur
        // eliotrope.net arrondit a 1 % pour son calculateur d'entrainement).
        return 1.0 + Math.Min(0.50, networkDistance * 0.02);
    }

    /// <summary>Tir a travers les portails, regles du jeu (eliotrope.net, regles 02, 03, 05, 06) : le lanceur vise le
    /// portail d'ENTREE ; le reseau va ensuite au portail inutilise le plus proche, jusqu'au dernier qui est la sortie ;
    /// le sort ressort de la sortie avec le MEME decalage que lanceur -> entree. Rend la case d'impact et la distance
    /// parcourue dans le reseau, ou null si l'egalite de distance entre deux portails (departagee en jeu par une regle
    /// horaire non modelisee) rend la sortie incertaine, ou si l'impact sort de la grille.</summary>
    public static (int Impact, int NetworkDistance, int Exit)? ProjectThroughPortals(int casterCell, int entryCell, IReadOnlyList<int> portalCells)
    {
        // The client observes at most four portals in a single owned network.
        // Duplicate, invalid or over-capacity cells are corrupt/ambiguous state,
        // not evidence for choosing an arbitrary exit.
        if ((uint)casterCell >= MapPathfinder.CellCount || (uint)entryCell >= MapPathfinder.CellCount
            || portalCells is null || portalCells.Count is < 2 or > 4
            || portalCells.Any(c => (uint)c >= MapPathfinder.CellCount)
            || portalCells.Distinct().Count() != portalCells.Count
            || !portalCells.Contains(entryCell)) return null;
        var unused = portalCells.Where(c => c != entryCell).ToList();
        int current = entryCell, travelled = 0;
        while (unused.Count > 0)
        {
            int best = unused.Min(c => MapPathfinder.CellDistance(current, c));
            var nearest = unused.Where(c => MapPathfinder.CellDistance(current, c) == best).ToList();
            if (nearest.Count > 1) return null;
            travelled += best;
            current = nearest[0];
            unused.Remove(current);
        }
        var (cx, cy) = MapPathfinder.MapPointOf(casterCell);
        var (ex, ey) = MapPathfinder.MapPointOf(entryCell);
        var (ox, oy) = MapPathfinder.MapPointOf(current);
        int impact = MapPathfinder.CellFromMapPoint(ox + (ex - cx), oy + (ey - cy));
        return impact < 0 ? null : (impact, travelled, current);
    }

    /// <summary>
    /// Owner-scoped counterpart for observed marks. Inactive portals and marks
    /// belonging to another owner cannot silently join this network. Duplicate
    /// mark identities make the current snapshot ambiguous (fail closed).
    /// The existing cell-only API remains for nodes with a proven owner-scoped
    /// list produced by the planner.
    /// </summary>
    public static (int Impact, int NetworkDistance, int Exit)? ProjectThroughPortals(
        int casterCell, int entryCell, IReadOnlyList<Portal> observedPortals, long ownerId)
    {
        if (ownerId == 0 || observedPortals is null) return null;
        var owned = observedPortals.Where(p => p.OwnerId == ownerId).ToArray();
        if (owned.Any(p => p.Id <= 0) || owned.Select(p => p.Id).Distinct().Count() != owned.Length)
            return null;
        return ProjectThroughPortals(casterCell, entryCell, owned.Where(p => p.Active).Select(p => p.Cell).ToArray());
    }

    // =========================================================================
    // CONFIRMED, ORDERED CLASS COMBO EVIDENCE (NOT A CAST PREDICTOR)
    // =========================================================================

    public const int EcaflipBreedId = 6;
    public const string VerifiedClassClientVersion = "3.7.13.17";

    public enum ComboKind { RogueWall, EliotropeRedirect, XelorTelefrag, FecaGlyph, EcaflipCard, SacrierSuffering }

    public enum ComboEventKind
    {
        Cast, Summon, SummonRemoved, WallActivated, MarkPlaced, MarkActivated,
        MarkRemoved, Move, StateAdded, StateRemoved, Effect, HpChanged, PortalDeactivated,
        // Only an adapter which independently correlates a server-confirmed
        // bomb detonation to a specific bomb and explosion spell may emit this.
        // A client descriptor or a damage row alone MUST NOT synthesize it.
        DetonationConfirmed, MarkDeactivated
    }

    /// <summary>
    /// Only the packet adapter may set ServerConfirmed=true. Order and Correlation
    /// must come from one authoritative, monotonically ordered packet stream:
    /// a log timestamp, attempted send or speculative SpellResolver event is NOT
    /// proof. A missing correlation prevents certifying a cast/effect chain.
    /// </summary>
    public sealed record ComboObservation(
        long Order, long FightId, int Generation, int Turn, long Source, long Subject,
        ComboEventKind Kind, int SpellId = 0, int Grade = 0, int ActionId = 0,
        int StateId = 0, int TemplateId = 0, int MarkId = 0,
        int FromCell = -1, int ToCell = -1, int Delta = 0,
        long Correlation = 0, bool ServerConfirmed = false, int LifeAfter = 0,
        int MarkType = 0);

    public sealed record ComboFrame(long FightId, int Generation, int FirstTurn, int LastTurn,
        long ActorId, int BreedId);

    /// <summary>
    /// A registry snapshot is useful only with the fight/decoder generation that
    /// produced it. The owner of an activation is NOT in the activation payload:
    /// it must be resolved against this earlier server-observed snapshot.
    /// </summary>
    public sealed record ComboMarkRegistryEvidence(long FightId, int Generation,
        IReadOnlyDictionary<int, ObservedFightMark> Marks);

    /// <summary>
    /// Narrow adapter for inbound GameActionFightEvent marks decoded with the
    /// official catalog (MapFightReader). The caller MUST supply an authenticated
    /// inbound packet, a current-fight membership/epoch and a monotonic order;
    /// these cannot be established from the mark payload itself. No inference
    /// from mark_type=4 (Huppermage rune vs Eliotrope portal) or wallData=2.
    ///
    /// A mark event does NOT carry the original spell id/grade, cast ACK,
    /// target or cast correlation. Consequently ActionId and Correlation stay
    /// ZERO. This adapter can establish mark ID/owner/cell and invalidations,
    /// but can NEVER manufacture a Cast, StateAdded, Telefrag, DetonationConfirmed,
    /// WallActivated or verified portal-cast sequence. No observed cast effect
    /// or ACK is fed by the simulation-only SpellResolver here.
    /// </summary>
    public static ComboObservation? ReadConfirmedFightMarkObservation(
        string messageName, bool inboundFromServer, bool inCurrentFight,
        ComboFrame frame, int turn, long order, IReadOnlyList<ProtoField> payload,
        ComboMarkRegistryEvidence? previousMarks)
    {
        if (messageName != "GameActionFightEvent" || !inboundFromServer || !inCurrentFight
            || frame is null || payload is null || previousMarks is null
            || frame.FightId <= 0 || frame.Generation <= 0 || frame.ActorId == 0
            || turn < frame.FirstTurn || turn > frame.LastTurn || order <= 0
            || previousMarks.FightId != frame.FightId
            || previousMarks.Generation != frame.Generation || previousMarks.Marks is null)
            return null;

        var placed = MapFightReader.ReadMark(payload);
        var removed = MapFightReader.ReadUnmark(payload);
        var activation = MapFightReader.ReadMarkActivation(payload);
        if ((placed.HasValue ? 1 : 0) + (removed.HasValue ? 1 : 0)
            + (activation.HasValue ? 1 : 0) != 1)
            return null;

        ComboObservation Event(ComboEventKind kind, int markId, long source = 0,
            int cell = -1, int markType = 0)
            => new(order, frame.FightId, frame.Generation, turn, source, 0,
                kind, MarkId: markId, ToCell: cell, MarkType: markType,
                // The authoritative mark event contains no spell/cast ACK.
                ActionId: 0, Correlation: 0, ServerConfirmed: true);

        if (placed is { } mark)
        {
            // A reused mark ID cannot be taken as a NEW placement, and the
            // action source must agree with the mark's recorded author.
            if (mark.Id <= 0 || mark.Author == 0 || mark.Type <= 0 || mark.Cells.Count == 0
                || mark.Cells.Any(c => (uint)c >= MapPathfinder.CellCount)
                || mark.Cells.Distinct().Count() != mark.Cells.Count
                || previousMarks.Marks.ContainsKey(mark.Id)
                || FightStatsReader.ReadActionSource(payload) != mark.Author)
                return null;
            return Event(ComboEventKind.MarkPlaced, mark.Id, mark.Author,
                mark.Cells.Count == 1 ? mark.Cells[0] : -1, mark.Type);
        }

        if (removed is int unmarked)
            return unmarked > 0 ? Event(ComboEventKind.MarkRemoved, unmarked) : null;

        if (activation is { } change && change.Id > 0)
        {
            // Inactive is an authoritative negative even if the original mark
            // owner is unavailable; do not treat deactivation as deletion.
            if (!change.Active) return Event(ComboEventKind.MarkDeactivated, change.Id);

            // An activation without a known owner and matching action source
            // must NOT satisfy VerifyObservedCombo's OwnMark requirement.
            if (!previousMarks.Marks.TryGetValue(change.Id, out var known)
                || known.Id != change.Id || known.Author == 0 || known.Type <= 0
                || FightStatsReader.ReadActionSource(payload) != known.Author)
                return null;
            return Event(ComboEventKind.MarkActivated, change.Id, known.Author,
                known.Cells.Length == 1 ? known.Cells[0] : -1, known.Type);
        }
        return null;
    }

    /// <summary>
    /// The caller must resolve Source/Followup/StateSource from the SAME client
    /// version and the actor's observed learned grades. Zero/null means unknown,
    /// never a wildcard. Mark IDs are wire identity, not SpellBombWallData IDs.
    /// </summary>
    public sealed record ComboEvidence(
        long TargetId = 0, SpellCastDescriptor? SourceSpell = null,
        SpellCastDescriptor? FollowupSpell = null, SpellCastDescriptor? StateSourceSpell = null,
        int StateId = 0, int FirstMarkId = 0, int SecondMarkId = 0,
        int FirstCell = -1, int SecondCell = -1, int TargetCell = -1,
        long FirstBombId = 0, long SecondBombId = 0,
        VerifiedRogueWall? Wall = null, IReadOnlyList<PlannerFighter>? CurrentBombs = null,
        IReadOnlyList<Portal>? Portals = null,
        int EffectActionId = 0, long SourceCorrelation = 0, long FollowupCorrelation = 0,
        int CasterCell = -1, string ClientVersion = "", int SourceBreedId = 0,
        int FollowupBreedId = 0, int StateSourceBreedId = 0,
        IReadOnlyDictionary<int, int>? ObservedLearnedGrades = null,
        bool? TargetIsEnemy = null, int DisplacedToCell = -1,
        // Optional for legacy replays; MANDATORY before a Sacrier suffering
        // sequence can attribute a self-state to a particular observed cast.
        // Neither a descriptor nor a local send supplies this correlation.
        long StateSourceCorrelation = 0);

    /// <summary>Historical packet-sequence evidence only. Complete does NOT
    /// authorize a future cast, promise damage, or preserve a temporary state
    /// after a later event. ConfirmedStages is useful for replay diagnostics.
    /// </summary>
    public sealed record ComboProof(bool Complete, int ConfirmedStages, int RequiredStages,
        string Reason, long LastOrder = 0);

    // Historical replay must stay bounded even when an external caller hands
    // over a very long fight or a sparse, stale event buffer. These are limits
    // on diagnostic proof construction, never server sequence IDs or ACKs.
    private const int MaxComboReplayTurns = 32;
    private const int MaxComboReplayObservations = 512;
    private const long MaxComboReplayOrderSpan = 8192;
    // Multi-turn combos (e.g. Roublard walls) may span turns, but a very old
    // effect must not be spliced into a new causal chain indefinitely.
    private const int MaxComboInterstageTurnGap = 8;
    // Completion is historical. When the caller can separately establish the
    // CURRENT fight turn, a historical positive cannot remain current forever.
    private const int MaxComboCurrentProofAgeTurns = 2;

    /// <summary>
    /// Six conservative, replayable sequence signatures. Every named phase
    /// needs a separate server observation in this fight generation, in order.
    /// An ambiguous/unsupported data row, missing identity, removed state/mark,
    /// absent cast correlation or mixed-fight sequence cannot be upgraded into a
    /// successful combo. This function has no side effects or gameplay sends.
    /// </summary>
    public static ComboProof VerifyObservedCombo(ComboKind kind, ComboFrame frame,
        ComboEvidence evidence, IReadOnlyList<ComboObservation> observations,
        int? observedAsOfTurn = null)
    {
        static ComboProof Refuse(string why) => new(false, 0, 0, why);
        if (frame is null || evidence is null || observations is null
            || frame.FightId <= 0 || frame.Generation <= 0 || frame.FirstTurn <= 0
            || frame.LastTurn < frame.FirstTurn || frame.ActorId == 0
            || (long)frame.LastTurn - frame.FirstTurn >= MaxComboReplayTurns
            || observations.Count > MaxComboReplayObservations
            || observedAsOfTurn.HasValue && (observedAsOfTurn < frame.FirstTurn
                || observedAsOfTurn > frame.LastTurn)
            || observations.Any(e => e is null))
            return Refuse("fight/epoch/actor or event stream missing");
        int breed = kind switch
        {
            ComboKind.RogueWall => RogueBreedId,
            ComboKind.EliotropeRedirect => EliotropeBreedId,
            ComboKind.XelorTelefrag => XelorBreedId,
            ComboKind.FecaGlyph => FecaBreedId,
            ComboKind.EcaflipCard => EcaflipBreedId,
            ComboKind.SacrierSuffering => SacrierBreedId,
            _ => 0
        };
        if (breed == 0 || frame.BreedId != breed || evidence.TargetId == 0)
            return Refuse("class/target is not established");
        // SpellCastDescriptor does not carry a breed or catalog-version field.
        // Require these joins explicitly from the caller's trusted client-data
        // loader, plus actually learned grade for spells the player must cast.
        if (evidence.ClientVersion != VerifiedClassClientVersion || evidence.SourceBreedId != breed
            || kind != ComboKind.XelorTelefrag && evidence.FollowupBreedId != breed
            || kind == ComboKind.SacrierSuffering && evidence.StateSourceBreedId != breed)
            return Refuse("client version or class-spell provenance absent");
        bool Spell(SpellCastDescriptor? d) => d is { SpellId: > 0, Grade: > 0, Uncertain: false }
            && !d.Effects.IsDefaultOrEmpty;
        bool Learned(SpellCastDescriptor? d) => Spell(d)
            && evidence.ObservedLearnedGrades is not null
            && evidence.ObservedLearnedGrades.TryGetValue(d!.SpellId, out int grade) && grade == d.Grade;
        if (!Learned(evidence.SourceSpell) || kind != ComboKind.RogueWall
            && kind != ComboKind.XelorTelefrag && !Learned(evidence.FollowupSpell))
            return Refuse("actor's learned spell grade not observed");
        bool Action(SpellCastDescriptor? d, Func<SpellEffectDescriptor, bool> test)
            => Spell(d) && d!.Effects.Any(test);
        bool StateRow(SpellCastDescriptor? d) => Action(d, x => x.ActionId == 950
            && (x.Value != 0 ? x.Value : x.Min) == evidence.StateId);
        bool Correlated(ComboObservation e, long id) => id > 0 && e.Correlation == id;
        bool Cast(ComboObservation e, SpellCastDescriptor? d, long correlation)
            => d is not null && e.Kind == ComboEventKind.Cast && e.Source == frame.ActorId
                && e.SpellId == d.SpellId && e.Grade == d.Grade && Correlated(e, correlation);
        bool Effect(ComboObservation e, SpellCastDescriptor? d, long correlation)
            => d is not null && e.Kind == ComboEventKind.Effect && e.Source == frame.ActorId
                && e.Subject == evidence.TargetId && e.SpellId == d.SpellId && e.Grade == d.Grade
                && e.ActionId == evidence.EffectActionId && Correlated(e, correlation);
        bool OwnMark(ComboObservation e, ComboEventKind eventKind, int id)
            => id > 0 && e.Kind == eventKind && e.Source == frame.ActorId && e.MarkId == id;
        bool Move(ComboObservation e, long actor, int from, int to)
            => e.Kind == ComboEventKind.Move && e.Subject == actor
                && e.FromCell == from && e.ToCell == to
                && (uint)from < MapPathfinder.CellCount && (uint)to < MapPathfinder.CellCount;
        bool State(ComboObservation e) => e.Kind == ComboEventKind.StateAdded
            && e.Source == frame.ActorId
            && e.Subject == (kind is ComboKind.SacrierSuffering or ComboKind.EcaflipCard
                ? frame.ActorId : evidence.TargetId)
            && e.StateId == evidence.StateId;

        var steps = new List<Func<ComboObservation, bool>>();
        var forbiddenMarks = new HashSet<int>();
        var forbiddenActors = new HashSet<long>();
        int protectedState = 0;
        long protectedStateActor = 0;
        switch (kind)
        {
            case ComboKind.RogueWall:
            {
                // Client 3.7.13.17: 13444/grade -> 1008/template3112;
                // SpellBombData3112 -> wallData2, explosion spell13455/action99.
                // WallActivated is NOT a raw FightMark.mark_type event: the
                // adapter must independently prove the bomb-pair/wallData join
                // and actual server activation. `mark_type == 2` alone is NOT it.
                var w = evidence.Wall;
                if (!Action(evidence.SourceSpell, x => x.ActionId == 1008 && x.DiceNum == VerifiedExplobombTemplate)
                    || evidence.SourceSpell!.SpellId != 13444
                    || !Action(evidence.FollowupSpell, x => x.ActionId == 99)
                    || evidence.FollowupSpell!.SpellId != 13455
                    || evidence.EffectActionId != 99 || evidence.FollowupCorrelation <= 0
                    || evidence.TargetId == frame.ActorId
                    || w is not { Active: true, WallDataId: VerifiedExplobombWall }
                    || w.OwnerId != frame.ActorId || w.FirstBombId == w.SecondBombId
                    || w.FirstBombId == 0 || w.SecondBombId == 0
                    || evidence.TargetId == w.FirstBombId || evidence.TargetId == w.SecondBombId
                    || evidence.FirstBombId != w.FirstBombId || evidence.SecondBombId != w.SecondBombId
                    || evidence.FirstMarkId <= 0 || w.ActiveCells is null || evidence.CurrentBombs is null
                    || w.ActiveCells.Count == 0 || !w.ActiveCells.Contains(evidence.TargetCell))
                    return Refuse("bomb grade/template, wall owner or explosion proof missing");
                var matchingBombs = evidence.CurrentBombs.Where(b => b is not null
                    && (b.Id == w.FirstBombId || b.Id == w.SecondBombId))
                    .ToArray();
                if (matchingBombs.Length != 2 || matchingBombs.Select(b => b.Id).Distinct().Count() != 2
                    || matchingBombs.Any(b => !IsRogueBomb(b, frame.ActorId))
                    || !w.ActiveCells.SetEquals(GetBombWallCells(matchingBombs[0].Cell,
                        matchingBombs[1].Cell, _ => true)))
                    return Refuse("active bomb actors or wall geometry unverified");
                steps.Add(e => e.Kind == ComboEventKind.Summon && e.Source == frame.ActorId
                    && e.Subject == w.FirstBombId && e.SpellId == 13444
                    && e.Grade == evidence.SourceSpell.Grade && e.TemplateId == VerifiedExplobombTemplate);
                steps.Add(e => e.Kind == ComboEventKind.Summon && e.Source == frame.ActorId
                    && e.Subject == w.SecondBombId && e.SpellId == 13444
                    && e.Grade == evidence.SourceSpell.Grade && e.TemplateId == VerifiedExplobombTemplate);
                steps.Add(e => OwnMark(e, ComboEventKind.WallActivated, evidence.FirstMarkId)
                    && e.TemplateId == VerifiedExplobombWall);
                steps.Add(e => e.Kind == ComboEventKind.Move && e.Subject == evidence.TargetId
                    && (e.Source == evidence.TargetId || e.Source == frame.ActorId)
                    && e.ToCell == evidence.TargetCell && w.ActiveCells.Contains(e.ToCell));
                // Damage action 99 in spell 13455 is only a damage descriptor,
                // not proof of Detonateur, bomb removal or chain initiation.
                // Demand a separate positive causal witness for exactly ONE
                // previously identified, living bomb actor before crediting the
                // explosion damage. This breaks the older 5-step synthetic
                // "Rogue success" fixtures until they supply a real event.
                steps.Add(e => e.Kind == ComboEventKind.DetonationConfirmed
                    && e.Source == frame.ActorId
                    && (e.Subject == w.FirstBombId || e.Subject == w.SecondBombId)
                    && e.SpellId == 13455 && e.Grade == evidence.FollowupSpell.Grade
                    && e.TemplateId == VerifiedExplobombTemplate
                    && Correlated(e, evidence.FollowupCorrelation));
                steps.Add(e => Effect(e, evidence.FollowupSpell, evidence.FollowupCorrelation));
                forbiddenActors.Add(w.FirstBombId); forbiddenActors.Add(w.SecondBombId);
                forbiddenMarks.Add(evidence.FirstMarkId);
                break;
            }
            case ComboKind.EliotropeRedirect:
            {
                // Portals must already be positively identified as Eliotrope
                // owner marks. In particular an Huppermage type-4 rune cannot
                // become a portal merely because its mark_type is 4.
                if (!Action(evidence.SourceSpell, x => x.ActionId == 1181)
                    || !Action(evidence.FollowupSpell, x => x.ActionId == evidence.EffectActionId)
                    || evidence.EffectActionId <= 0 || evidence.FollowupCorrelation <= 0
                    || evidence.FirstMarkId <= 0 || evidence.SecondMarkId <= 0
                    || evidence.FirstMarkId == evidence.SecondMarkId || evidence.Portals is null
                    || evidence.FirstCell < 0 || evidence.SecondCell < 0 || evidence.TargetCell < 0
                    || evidence.CasterCell < 0
                    || evidence.Portals.Count(p => p.Active && p.OwnerId == frame.ActorId) != 2
                    || evidence.Portals.Any(p => p.OwnerId != frame.ActorId
                        && (p.Id == evidence.FirstMarkId || p.Id == evidence.SecondMarkId)))
                    return Refuse("portal owner/grade/active network or effect missing");
                var active = evidence.Portals.Where(p => p.Active && p.OwnerId == frame.ActorId).ToArray();
                if (!active.Any(p => p.Id == evidence.FirstMarkId && p.Cell == evidence.FirstCell)
                    || !active.Any(p => p.Id == evidence.SecondMarkId && p.Cell == evidence.SecondCell)
                    || ProjectThroughPortals(evidence.CasterCell, evidence.FirstCell, active, frame.ActorId)
                        is not { } projected || projected.Impact != evidence.TargetCell)
                    return Refuse("portal mark identity/route unverified");
                steps.Add(e => OwnMark(e, ComboEventKind.MarkPlaced, evidence.FirstMarkId)
                    && e.ActionId == 1181 && e.ToCell == evidence.FirstCell);
                steps.Add(e => OwnMark(e, ComboEventKind.MarkPlaced, evidence.SecondMarkId)
                    && e.ActionId == 1181 && e.ToCell == evidence.SecondCell);
                steps.Add(e => OwnMark(e, ComboEventKind.MarkActivated, evidence.FirstMarkId));
                steps.Add(e => OwnMark(e, ComboEventKind.MarkActivated, evidence.SecondMarkId));
                steps.Add(e => Cast(e, evidence.FollowupSpell, evidence.FollowupCorrelation)
                    && e.ToCell == evidence.FirstCell);
                steps.Add(e => Effect(e, evidence.FollowupSpell, evidence.FollowupCorrelation));
                if (evidence.FollowupSpell!.Effects.Any(row => row.ActionId == evidence.EffectActionId
                        && row.Kind is SpellEffectKind.Push or SpellEffectKind.Pull))
                {
                    // An observed push effect without the target's actual move
                    // is insufficient to claim repositioning through a portal.
                    if (evidence.DisplacedToCell < 0 || evidence.DisplacedToCell == evidence.TargetCell)
                        return Refuse("displacement confirmation missing");
                steps.Add(e => Move(e, evidence.TargetId, evidence.TargetCell,
                        evidence.DisplacedToCell) && e.Source == frame.ActorId
                    && Correlated(e, evidence.FollowupCorrelation));
                }
                forbiddenMarks.Add(evidence.FirstMarkId); forbiddenMarks.Add(evidence.SecondMarkId);
                break;
            }
            case ComboKind.XelorTelefrag:
            {
                if (!Action(evidence.SourceSpell, e => e.Kind == SpellEffectKind.Swap)
                    || evidence.SourceCorrelation <= 0 || !evidence.TargetIsEnemy.HasValue
                    || evidence.StateId != (evidence.TargetIsEnemy.Value ? StateTelefrag : StateAllyTelefrag)
                    || evidence.TargetId == frame.ActorId
                    || evidence.FirstCell == evidence.SecondCell
                    || evidence.TargetCell != evidence.SecondCell)
                    return Refuse("swap grade/Telefrag state/positions not established");
                steps.Add(e => Cast(e, evidence.SourceSpell, evidence.SourceCorrelation)
                    && e.ToCell == evidence.SecondCell);
                // Either server movement order is valid; the second movement is
                // checked as a separate phase, not inferred from the first.
                steps.Add(e => Move(e, frame.ActorId, evidence.FirstCell, evidence.SecondCell)
                    && Correlated(e, evidence.SourceCorrelation));
                steps.Add(e => Move(e, evidence.TargetId, evidence.SecondCell, evidence.FirstCell)
                    && Correlated(e, evidence.SourceCorrelation));
                steps.Add(e => State(e) && Correlated(e, evidence.SourceCorrelation));
                protectedState = evidence.StateId; protectedStateActor = evidence.TargetId;
                break;
            }
            case ComboKind.FecaGlyph:
            {
                if (!Action(evidence.SourceSpell, x => x.PlacesGroundMark && x.ActionId != 1181)
                    || !Action(evidence.FollowupSpell, x => x.ActionId == evidence.EffectActionId)
                    || evidence.FirstMarkId <= 0 || evidence.TargetCell < 0 || evidence.EffectActionId <= 0
                    || evidence.SourceCorrelation <= 0 || evidence.FollowupCorrelation <= 0
                    || evidence.SourceCorrelation == evidence.FollowupCorrelation)
                    return Refuse("glyph grade/identity/trigger source not established");
                steps.Add(e => Cast(e, evidence.SourceSpell, evidence.SourceCorrelation)
                    && e.ToCell == evidence.TargetCell);
                steps.Add(e => OwnMark(e, ComboEventKind.MarkPlaced, evidence.FirstMarkId)
                    && e.ToCell == evidence.TargetCell
                    && evidence.SourceSpell!.Effects.Any(row => row.PlacesGroundMark && row.ActionId == e.ActionId));
                steps.Add(e => OwnMark(e, ComboEventKind.MarkActivated, evidence.FirstMarkId));
                // An actor entering a glyph is NOT proof of its turn-start/end
                // damage: the separately correlated follow-up cast and effect
                // must be explicitly observed. A mark activation is not a cast.
                steps.Add(e => Cast(e, evidence.FollowupSpell, evidence.FollowupCorrelation));
                steps.Add(e => Effect(e, evidence.FollowupSpell, evidence.FollowupCorrelation)
                    && e.MarkId == evidence.FirstMarkId);
                forbiddenMarks.Add(evidence.FirstMarkId);
                break;
            }
            case ComboKind.EcaflipCard:
            {
                if (evidence.StateId <= 0 || !StateRow(evidence.SourceSpell)
                    || !Action(evidence.FollowupSpell, x => x.ActionId == evidence.EffectActionId)
                    || evidence.EffectActionId <= 0 || evidence.SourceCorrelation <= 0
                    || evidence.FollowupCorrelation <= 0 || evidence.SourceCorrelation == evidence.FollowupCorrelation
                    || (uint)evidence.CasterCell >= MapPathfinder.CellCount
                    || (uint)evidence.TargetCell >= MapPathfinder.CellCount)
                    return Refuse("card state/grade/consumption effect unknown in client data");
                steps.Add(e => Cast(e, evidence.SourceSpell, evidence.SourceCorrelation)
                    && e.ToCell == evidence.CasterCell);
                steps.Add(e => State(e) && Correlated(e, evidence.SourceCorrelation));
                steps.Add(e => Cast(e, evidence.FollowupSpell, evidence.FollowupCorrelation)
                    && e.ToCell == evidence.TargetCell);
                steps.Add(e => Effect(e, evidence.FollowupSpell, evidence.FollowupCorrelation));
                protectedState = evidence.StateId; protectedStateActor = frame.ActorId;
                break;
            }
            case ComboKind.SacrierSuffering:
            {
                if (evidence.StateId <= 0 || !StateRow(evidence.StateSourceSpell)
                    || !Learned(evidence.StateSourceSpell)
                    || !Action(evidence.SourceSpell, x => x.Kind is SpellEffectKind.Pull or SpellEffectKind.Swap)
                    || !Action(evidence.FollowupSpell, x => x.ActionId == evidence.EffectActionId
                        && x.Kind == SpellEffectKind.Damage)
                    || evidence.EffectActionId <= 0 || evidence.SourceCorrelation <= 0
                    || evidence.FollowupCorrelation <= 0 || evidence.SourceCorrelation == evidence.FollowupCorrelation
                    || evidence.StateSourceCorrelation <= 0
                    || evidence.StateSourceCorrelation == evidence.SourceCorrelation
                    || evidence.StateSourceCorrelation == evidence.FollowupCorrelation
                    || (uint)evidence.CasterCell >= MapPathfinder.CellCount
                    || evidence.FirstCell < 0 || evidence.SecondCell < 0
                    || evidence.FirstCell == evidence.SecondCell)
                    return Refuse("suffering state source/grade/position/damage missing");
                steps.Add(e => e.Kind == ComboEventKind.HpChanged && e.Subject == frame.ActorId
                    && e.Delta != 0 && e.LifeAfter > 0);
                steps.Add(e => Cast(e, evidence.StateSourceSpell,
                    evidence.StateSourceCorrelation) && e.ToCell == evidence.CasterCell);
                steps.Add(e => State(e) && Correlated(e, evidence.StateSourceCorrelation));
                steps.Add(e => Cast(e, evidence.SourceSpell, evidence.SourceCorrelation)
                    && e.ToCell == evidence.FirstCell);
                steps.Add(e => Move(e, evidence.TargetId, evidence.FirstCell, evidence.SecondCell)
                    && Correlated(e, evidence.SourceCorrelation));
                steps.Add(e => Cast(e, evidence.FollowupSpell, evidence.FollowupCorrelation));
                steps.Add(e => Effect(e, evidence.FollowupSpell, evidence.FollowupCorrelation));
                protectedState = evidence.StateId; protectedStateActor = frame.ActorId;
                break;
            }
        }

        if (steps.Count == 0) return Refuse("no verified class sequence");
        var scoped = observations.Where(e => e.FightId == frame.FightId && e.Generation == frame.Generation
            && e.Turn >= frame.FirstTurn && e.Turn <= frame.LastTurn).ToArray();
        if (scoped.Any(e => e.Order <= 0) || scoped.Zip(scoped.Skip(1), (a, b) => a.Order >= b.Order).Any(x => x)
            || scoped.Zip(scoped.Skip(1), (a, b) => a.Turn > b.Turn).Any(x => x)
            || observedAsOfTurn.HasValue && scoped.Any(e => e.Turn > observedAsOfTurn.Value)
            || scoped.Length > 1 && scoped[^1].Order - scoped[0].Order > MaxComboReplayOrderSpan)
            return Refuse("duplicate, reversed or unsequenced server events");

        // A correlation is scoped to exactly one selected cast identity. A
        // foreign source, spell, grade or target sharing the same correlation
        // contradicts the request, even if the correct cast appears later.
        // These are *consistency checks* on caller-supplied replay rows, not
        // proof that a transport-bound server ACK is available.
        bool MatchesCastIdentity(ComboObservation row, SpellCastDescriptor? spell,
            int? targetCell = null)
            => spell is not null && row.Source == frame.ActorId
                && row.SpellId == spell.SpellId && row.Grade == spell.Grade
                && (!targetCell.HasValue || row.ToCell == targetCell.Value);

        bool ConflictingCastCorrelation(ComboObservation row)
        {
            if (row.Kind != ComboEventKind.Cast || row.Correlation <= 0) return false;
            bool knownCorrelation = row.Correlation == evidence.SourceCorrelation
                && evidence.SourceCorrelation > 0
                || row.Correlation == evidence.FollowupCorrelation
                    && evidence.FollowupCorrelation > 0
                || row.Correlation == evidence.StateSourceCorrelation
                    && evidence.StateSourceCorrelation > 0;
            // A pending/unconfirmed attempt under a selected request identity
            // cannot be silently skipped in favour of a later 'successful'
            // matching cast. No local send or linked row is a server ACK.
            if (knownCorrelation && !row.ServerConfirmed) return true;
            if (!row.ServerConfirmed) return false;
            if (row.Correlation == evidence.StateSourceCorrelation
                && evidence.StateSourceCorrelation > 0
                && !MatchesCastIdentity(row, evidence.StateSourceSpell,
                    kind == ComboKind.SacrierSuffering ? evidence.CasterCell : null))
                return true;
            if (row.Correlation == evidence.SourceCorrelation
                && evidence.SourceCorrelation > 0)
            {
                int? target = kind switch
                {
                    ComboKind.XelorTelefrag => evidence.SecondCell,
                    ComboKind.FecaGlyph => evidence.TargetCell,
                    ComboKind.EcaflipCard => evidence.CasterCell,
                    ComboKind.SacrierSuffering => evidence.FirstCell,
                    _ => null
                };
                if (!MatchesCastIdentity(row, evidence.SourceSpell, target))
                    return true;
            }
            if (row.Correlation == evidence.FollowupCorrelation
                && evidence.FollowupCorrelation > 0)
            {
                int? target = kind switch
                {
                    ComboKind.EliotropeRedirect => evidence.FirstCell,
                    ComboKind.FecaGlyph => evidence.TargetCell,
                    ComboKind.EcaflipCard => evidence.TargetCell,
                    _ => null
                };
                if (!MatchesCastIdentity(row, evidence.FollowupSpell, target))
                    return true;
            }
            return false;
        }

        ComboProof Scan(IReadOnlyList<Func<ComboObservation, bool>> pattern)
        {
            int matched = 0;
            long lastOrder = 0;
            int lastMatchedTurn = 0;
            var seenXelorMovement = new HashSet<long>();
            var seenOwnedMarks = new HashSet<int>();
            var seenActivatedMarks = new HashSet<int>();
            var seenRelatedCasts = new HashSet<(int Spell, long Correlation)>();
            foreach (var e in scoped)
            {
                if (ConflictingCastCorrelation(e))
                    return new(false, matched, pattern.Count,
                        "selected cast correlation changed author, spell, grade or target", lastOrder);
                // Contradictory server evidence cannot be skipped over just to
                // find a happy path later in the same requested window.
                if (e.ServerConfirmed &&
                    ((e.Kind is ComboEventKind.MarkRemoved or ComboEventKind.PortalDeactivated
                        or ComboEventKind.MarkDeactivated
                        && forbiddenMarks.Contains(e.MarkId))
                    || (e.Kind == ComboEventKind.SummonRemoved && forbiddenActors.Contains(e.Subject))
                    || (e.Kind == ComboEventKind.StateRemoved && e.Subject == protectedStateActor
                        && e.StateId == protectedState && protectedState != 0)))
                    return new(false, matched, pattern.Count, "authoritative removal invalidates combo", lastOrder);
                if (!e.ServerConfirmed) continue;

                // Reusing a mark ID or replaying the same correlated actor cast
                // cannot satisfy a fresh phase. The observation stream is an
                // ordered history, not a multiset where old successes can be
                // repeatedly counted. Ignore unrelated IDs and other actors.
                if (e.Kind == ComboEventKind.MarkPlaced && e.MarkId > 0
                    && forbiddenMarks.Contains(e.MarkId)
                    && !seenOwnedMarks.Add(e.MarkId))
                    return new(false, matched, pattern.Count,
                        "duplicate owned mark identity in replay", lastOrder);
                if (e.MarkId > 0 && forbiddenMarks.Contains(e.MarkId)
                    && (e.Kind is ComboEventKind.MarkActivated or ComboEventKind.WallActivated)
                    && !seenActivatedMarks.Add(e.MarkId))
                    return new(false, matched, pattern.Count,
                        "duplicate selected mark activation in replay", lastOrder);
                if (e.Kind == ComboEventKind.Cast && e.Source == frame.ActorId
                    && e.Correlation > 0
                    && ((evidence.SourceSpell is { } source
                            && e.SpellId == source.SpellId
                            && e.Correlation == evidence.SourceCorrelation)
                        || (evidence.FollowupSpell is { } candidateFollowup
                            && e.SpellId == candidateFollowup.SpellId
                            && e.Correlation == evidence.FollowupCorrelation))
                    && !seenRelatedCasts.Add((e.SpellId, e.Correlation)))
                    return new(false, matched, pattern.Count,
                        "repeated correlated class cast in replay", lastOrder);

                // Mark identity is scoped to its owner, not to the most recent
                // actor that happened to send a similarly shaped effect. An
                // event with the SAME selected mark ID but a different author
                // is contradictory evidence, not harmless unrelated activity.
                if (e.MarkId > 0
                    && (e.MarkId == evidence.FirstMarkId || e.MarkId == evidence.SecondMarkId)
                    && (e.Kind is ComboEventKind.MarkPlaced or ComboEventKind.MarkActivated
                        or ComboEventKind.WallActivated)
                    && e.Source != frame.ActorId)
                    return new(false, matched, pattern.Count, "mark ownership conflict", lastOrder);

                // The mark-registry adapter can supply the active cell from
                // the same observed mark ID. Never accept a contradictory cell
                // while silently treating the ID as the selected portal/glyph.
                if (e.Kind == ComboEventKind.MarkActivated && e.ToCell >= 0
                    && (kind == ComboKind.EliotropeRedirect
                        && (e.MarkId == evidence.FirstMarkId
                            && e.ToCell != evidence.FirstCell
                            || e.MarkId == evidence.SecondMarkId
                            && e.ToCell != evidence.SecondCell)
                        || kind == ComboKind.FecaGlyph
                            && e.MarkId == evidence.FirstMarkId
                            && e.ToCell != evidence.TargetCell))
                    return new(false, matched, pattern.Count,
                        "mark activation cell contradicts observed identity", lastOrder);

                if (kind == ComboKind.RogueWall
                    && e.Kind == ComboEventKind.Summon
                    && (e.Subject == evidence.FirstBombId || e.Subject == evidence.SecondBombId)
                    && (e.Source != frame.ActorId || e.SpellId != 13444
                        || e.Grade != evidence.SourceSpell!.Grade
                        || e.TemplateId != VerifiedExplobombTemplate))
                    return new(false, matched, pattern.Count, "bomb summoning provenance conflict", lastOrder);

                if (kind == ComboKind.RogueWall
                    && e.Kind == ComboEventKind.Move && e.Subject == evidence.TargetId
                    && e.ToCell == evidence.TargetCell
                    && e.Source != frame.ActorId && e.Source != evidence.TargetId)
                    return new(false, matched, pattern.Count, "target movement origin unknown", lastOrder);

                if (kind == ComboKind.XelorTelefrag
                    && e.Kind == ComboEventKind.Cast && e.SpellId == evidence.SourceSpell!.SpellId
                    && Correlated(e, evidence.SourceCorrelation)
                    && !Cast(e, evidence.SourceSpell, evidence.SourceCorrelation))
                    return new(false, matched, pattern.Count, "Xelor cast author or grade mismatch", lastOrder);

                // A confirmed event for a later phase cannot be parked until a
                // different cast retrospectively makes it look sequential.
                // The two Xelor movement packets are explicitly unordered; we
                // test both complete permutations below, while independently
                // checking their actor/position/cast identity here.
                if (matched < pattern.Count && e.Kind != ComboEventKind.Move
                    && pattern.Skip(matched + 1).Any(phase => phase(e)))
                    return new(false, matched, pattern.Count, "confirmed phase arrived out of order", lastOrder);

                if (kind == ComboKind.XelorTelefrag
                    && matched > 0 && e.Kind == ComboEventKind.Move
                    && (e.Subject == frame.ActorId || e.Subject == evidence.TargetId))
                {
                    bool expected = e.Source == frame.ActorId
                        && Correlated(e, evidence.SourceCorrelation)
                        && (e.Subject == frame.ActorId
                            ? Move(e, frame.ActorId, evidence.FirstCell, evidence.SecondCell)
                            : Move(e, evidence.TargetId, evidence.SecondCell, evidence.FirstCell));
                    if (!expected || !seenXelorMovement.Add(e.Subject))
                        return new(false, matched, pattern.Count, "Xelor movement provenance/sequence conflict", lastOrder);
                }

                // Same action/correlation, but another author or target: do not
                // skip an inconsistent server row and later merge two distinct
                // effects into a falsely complete class combo.
                if (e.Kind == ComboEventKind.Effect
                    && evidence.FollowupSpell is { } follow && e.SpellId == follow.SpellId
                    && e.ActionId == evidence.EffectActionId && Correlated(e, evidence.FollowupCorrelation)
                    && !Effect(e, follow, evidence.FollowupCorrelation))
                    return new(false, matched, pattern.Count, "effect author or target mismatch", lastOrder);
                if (e.Kind == ComboEventKind.Effect && evidence.FollowupSpell is { } candidate
                    && e.SpellId == candidate.SpellId && e.ActionId == evidence.EffectActionId
                    && e.Source == frame.ActorId && e.Subject == evidence.TargetId
                    && e.Correlation != evidence.FollowupCorrelation)
                    return new(false, matched, pattern.Count,
                        "effect correlation conflicts with selected follow-up", lastOrder);
                if (e.Kind == ComboEventKind.Effect && evidence.FollowupSpell is { } targetSpell
                    && e.SpellId == targetSpell.SpellId
                    && e.ActionId == evidence.EffectActionId
                    && Correlated(e, evidence.FollowupCorrelation)
                    && e.ToCell >= 0 && evidence.TargetCell >= 0
                    && e.ToCell != evidence.TargetCell)
                    return new(false, matched, pattern.Count,
                        "effect target cell contradicts the selected actor", lastOrder);

                if (e.Kind == ComboEventKind.DetonationConfirmed
                    && kind == ComboKind.RogueWall && Correlated(e, evidence.FollowupCorrelation)
                    && !pattern.Any(phase => phase(e)))
                    return new(false, matched, pattern.Count, "bomb detonation source or identity mismatch", lastOrder);

                if (protectedState != 0 && e.Kind == ComboEventKind.StateAdded
                    && e.Subject == protectedStateActor && e.StateId == protectedState
                    && (kind == ComboKind.XelorTelefrag ? Correlated(e, evidence.SourceCorrelation) : true)
                    && !State(e))
                    return new(false, matched, pattern.Count, "state author mismatch", lastOrder);
                if (protectedState != 0 && e.Kind == ComboEventKind.StateAdded
                    && e.Subject == protectedStateActor && e.StateId == protectedState
                    && e.Correlation > 0
                    && e.Correlation != (kind == ComboKind.SacrierSuffering
                        ? evidence.StateSourceCorrelation : evidence.SourceCorrelation))
                    return new(false, matched, pattern.Count,
                        "state correlation does not match selected cast", lastOrder);
                if (matched < pattern.Count && pattern[matched](e))
                {
                    if (lastMatchedTurn > 0
                        && (long)e.Turn - lastMatchedTurn > MaxComboInterstageTurnGap)
                        return new(false, matched, pattern.Count,
                            "class proof exceeded bounded interstage turn gap", lastOrder);
                    matched++;
                    lastOrder = e.Order;
                    lastMatchedTurn = e.Turn;
                }
            }
            return new(matched == pattern.Count, matched, pattern.Count,
                matched == pattern.Count ? "ordered, server-confirmed observations" : "missing confirmed class step",
                lastOrder);
        }

        // A completed legacy sequence signature is not automatically a fully
        // attributable cast chain. The optional current-turn review is strict:
        // every summoned bomb or portal mark must join a DISTINCT earlier
        // positive cast with the same request correlation. The real mark
        // adapter supplies Correlation=0, so it correctly fails this path until
        // an independently authenticated transport/decoder join exists.
        bool CurrentCastReceiptsAttributable()
        {
            bool OneCast(long correlation, SpellCastDescriptor? spell, long beforeOrder,
                int observedTurn, int? targetCell = null)
            {
                if (correlation <= 0 || spell is null) return false;
                var casts = scoped.Where(e => e.Kind == ComboEventKind.Cast
                    && e.Correlation == correlation).ToArray();
                // Count *all* rows under the selected correlation, not merely
                // the favourable positive row before the effect. Late repeats,
                // foreign authors, and sent-only rows invalidate attribution.
                return casts.Length == 1 && casts[0].ServerConfirmed
                    && casts[0].Source == frame.ActorId
                    && casts[0].SpellId == spell.SpellId
                    && casts[0].Grade == spell.Grade
                    && casts[0].Order < beforeOrder
                    && casts[0].Turn == observedTurn
                    && (!targetCell.HasValue || casts[0].ToCell == targetCell.Value);
            }

            // Treat a locally seen, but unauthenticated cancellation as
            // unresolved authority. It must not be silently dropped while a
            // prior positive frame is still advertised as current.
            if (scoped.Any(e => !e.ServerConfirmed
                && ((e.Kind is ComboEventKind.MarkRemoved
                        or ComboEventKind.MarkDeactivated
                        or ComboEventKind.PortalDeactivated)
                    && forbiddenMarks.Contains(e.MarkId)
                    || e.Kind == ComboEventKind.SummonRemoved
                        && forbiddenActors.Contains(e.Subject)
                    || e.Kind == ComboEventKind.StateRemoved && protectedState > 0
                        && e.Subject == protectedStateActor && e.StateId == protectedState)))
                return false;

            // Every claimed damage/trigger must link to exactly one selected
            // server cast and exactly one target effect. Several independent
            // area targets are fine, but the chosen target cannot be replayed
            // twice or borrowed from an unrelated cast/turn.
            if (kind != ComboKind.XelorTelefrag)
            {
                var effects = scoped.Where(e => e.Kind == ComboEventKind.Effect
                    && e.SpellId == evidence.FollowupSpell?.SpellId
                    && e.ActionId == evidence.EffectActionId
                    && e.Subject == evidence.TargetId).ToArray();
                var followups = scoped.Where(e => e.Kind == ComboEventKind.Cast
                    && e.Correlation == evidence.FollowupCorrelation).ToArray();
                if (effects.Length != 1 || followups.Length != 1
                    || !effects[0].ServerConfirmed || !followups[0].ServerConfirmed
                    || effects[0].Source != frame.ActorId
                    || effects[0].Grade != evidence.FollowupSpell?.Grade
                    || effects[0].Correlation != evidence.FollowupCorrelation
                    || followups[0].Source != frame.ActorId
                    || followups[0].SpellId != evidence.FollowupSpell?.SpellId
                    || followups[0].Grade != evidence.FollowupSpell?.Grade
                    || followups[0].Order >= effects[0].Order
                    || effects[0].Turn < followups[0].Turn
                    || (long)effects[0].Turn - followups[0].Turn > MaxComboCurrentProofAgeTurns)
                    return false;
                int expectedCell = kind == ComboKind.SacrierSuffering
                    ? evidence.SecondCell : evidence.TargetCell;
                if (effects[0].ToCell >= 0 && expectedCell >= 0
                    && effects[0].ToCell != expectedCell)
                    return false;
                int selectedCastCell = kind == ComboKind.EliotropeRedirect
                    ? evidence.FirstCell : expectedCell;
                if ((uint)selectedCastCell >= MapPathfinder.CellCount
                    || followups[0].ToCell != selectedCastCell)
                    return false;
            }
            switch (kind)
            {
                case ComboKind.RogueWall:
                {
                    var summons = scoped.Where(e => e.ServerConfirmed
                        && e.Kind == ComboEventKind.Summon
                        && (e.Subject == evidence.FirstBombId || e.Subject == evidence.SecondBombId))
                        .ToArray();
                    if (summons.Length != 2 || summons[0].Correlation <= 0
                        || summons[1].Correlation <= 0
                        || summons[0].Correlation == summons[1].Correlation
                        || evidence.CurrentBombs is null) return false;
                    foreach (var summon in summons)
                    {
                        var bomb = evidence.CurrentBombs.FirstOrDefault(b => b is not null
                            && b.Id == summon.Subject);
                        if (bomb is null || !OneCast(summon.Correlation,
                            evidence.SourceSpell, summon.Order, summon.Turn, bomb.Cell)) return false;
                    }
                    var detonations = scoped.Where(e => e.ServerConfirmed
                        && e.Kind == ComboEventKind.DetonationConfirmed
                        && e.Correlation == evidence.FollowupCorrelation).ToArray();
                    return detonations.Length == 1 && OneCast(evidence.FollowupCorrelation,
                        evidence.FollowupSpell, detonations[0].Order, detonations[0].Turn);
                }
                case ComboKind.EliotropeRedirect:
                {
                    var placements = scoped.Where(e => e.ServerConfirmed
                        && e.Kind == ComboEventKind.MarkPlaced
                        && (e.MarkId == evidence.FirstMarkId
                            || e.MarkId == evidence.SecondMarkId)).ToArray();
                    return placements.Length == 2 && placements[0].Correlation > 0
                        && placements[1].Correlation > 0
                        && placements[0].Correlation != placements[1].Correlation
                        && placements.All(e => OneCast(e.Correlation,
                            evidence.SourceSpell, e.Order, e.Turn, e.ToCell));
                }
                case ComboKind.FecaGlyph:
                {
                    var marks = scoped.Where(e => e.ServerConfirmed
                        && e.Kind == ComboEventKind.MarkPlaced
                        && e.MarkId == evidence.FirstMarkId).ToArray();
                    return marks.Length == 1
                        && marks[0].Correlation == evidence.SourceCorrelation
                        && OneCast(evidence.SourceCorrelation, evidence.SourceSpell,
                            marks[0].Order, marks[0].Turn, evidence.TargetCell);
                }
                case ComboKind.XelorTelefrag:
                {
                    var casts = scoped.Where(e => e.Kind == ComboEventKind.Cast
                        && e.Correlation == evidence.SourceCorrelation).ToArray();
                    var movements = scoped.Where(e => e.Kind == ComboEventKind.Move
                        && e.Correlation == evidence.SourceCorrelation
                        && (e.Subject == frame.ActorId || e.Subject == evidence.TargetId))
                        .ToArray();
                    var states = scoped.Where(e => e.Kind == ComboEventKind.StateAdded
                        && e.Subject == evidence.TargetId && e.StateId == evidence.StateId)
                        .ToArray();
                    if (casts.Length != 1 || movements.Length != 2 || states.Length != 1
                        || !casts[0].ServerConfirmed || !states[0].ServerConfirmed
                        || states[0].Source != frame.ActorId
                        || states[0].Correlation != evidence.SourceCorrelation
                        || movements.Any(e => !e.ServerConfirmed || e.Source != frame.ActorId
                            || e.Turn != states[0].Turn)
                        || states[0].Order <= movements.Max(e => e.Order)
                        || ((long)states[0].Turn - casts[0].Turn) is < 0 or > 1)
                        return false;
                    return OneCast(evidence.SourceCorrelation, evidence.SourceSpell,
                        movements.Min(e => e.Order), casts[0].Turn, evidence.SecondCell);
                }
                case ComboKind.EcaflipCard:
                {
                    var state = scoped.Where(e => e.Kind == ComboEventKind.StateAdded
                        && e.Subject == frame.ActorId && e.StateId == evidence.StateId)
                        .ToArray();
                    var follow = scoped.Where(e => e.Kind == ComboEventKind.Cast
                        && e.Correlation == evidence.FollowupCorrelation).ToArray();
                    if (state.Length != 1 || follow.Length != 1 || !state[0].ServerConfirmed
                        || state[0].Source != frame.ActorId
                        || state[0].Correlation != evidence.SourceCorrelation
                        || follow[0].Order <= state[0].Order
                        || ((long)follow[0].Turn - state[0].Turn) is < 0 or > 2)
                        return false;
                    return OneCast(evidence.SourceCorrelation, evidence.SourceSpell,
                        state[0].Order, state[0].Turn, evidence.CasterCell)
                        && follow[0].ToCell == evidence.TargetCell;
                }
                case ComboKind.SacrierSuffering:
                {
                    var state = scoped.Where(e => e.Kind == ComboEventKind.StateAdded
                        && e.Subject == frame.ActorId && e.StateId == evidence.StateId)
                        .ToArray();
                    var stateCasts = scoped.Where(e => e.Kind == ComboEventKind.Cast
                        && e.Correlation == evidence.StateSourceCorrelation).ToArray();
                    var moves = scoped.Where(e => e.Kind == ComboEventKind.Move
                        && e.Subject == evidence.TargetId && e.Correlation == evidence.SourceCorrelation)
                        .ToArray();
                    var follow = scoped.Where(e => e.Kind == ComboEventKind.Cast
                        && e.Correlation == evidence.FollowupCorrelation).ToArray();
                    if (state.Length != 1 || stateCasts.Length != 1
                        || moves.Length != 1 || follow.Length != 1
                        || !state[0].ServerConfirmed || !moves[0].ServerConfirmed
                        || state[0].Source != frame.ActorId
                        || state[0].Correlation != evidence.StateSourceCorrelation
                        || moves[0].Source != frame.ActorId
                        || moves[0].FromCell != evidence.FirstCell
                        || moves[0].ToCell != evidence.SecondCell
                        || ((long)moves[0].Turn - state[0].Turn) is < 0 or > 2
                        || ((long)follow[0].Turn - state[0].Turn) is < 0 or > 2)
                        return false;
                    return OneCast(evidence.StateSourceCorrelation,
                        evidence.StateSourceSpell, state[0].Order, state[0].Turn,
                        evidence.CasterCell)
                        && OneCast(evidence.SourceCorrelation, evidence.SourceSpell,
                            moves[0].Order, moves[0].Turn, evidence.FirstCell)
                        && follow[0].ToCell == evidence.SecondCell;
                }
                default: return false;
            }
        }

        ComboProof Finish(ComboProof proof)
        {
            if (!proof.Complete || !observedAsOfTurn.HasValue) return proof;
            if (!CurrentCastReceiptsAttributable())
                return new ComboProof(false, proof.ConfirmedStages, proof.RequiredStages,
                    "current combo lacks individually attributed cast receipts", proof.LastOrder);
            int lastTurn = scoped.First(e => e.Order == proof.LastOrder).Turn;
            return observedAsOfTurn.Value - lastTurn > MaxComboCurrentProofAgeTurns
                ? new ComboProof(false, proof.ConfirmedStages, proof.RequiredStages,
                    "historical combo completion expired at current fight turn", proof.LastOrder)
                : proof;
        }
        var primary = Scan(steps);
        if (kind != ComboKind.XelorTelefrag || primary.Complete
            || primary.Reason is not ("confirmed phase arrived out of order"
                or "missing confirmed class step"))
            return Finish(primary);
        // The wire can report the target's position change before the caster's.
        // Both movement confirmations are mandatory regardless of their order.
        var reverse = steps.ToArray();
        (reverse[1], reverse[2]) = (reverse[2], reverse[1]);
        var alternative = Scan(reverse);
        return Finish(alternative.Complete || alternative.ConfirmedStages > primary.ConfirmedStages
            ? alternative : primary);
    }

    // =========================================================================
    // SYNERGIES & COMBOS MULTI-CLASSES (Panda+Roublard, Iop+Elio, Osa+Zobal, etc.)
    // =========================================================================

    public const int IopBreedId = 8;
    public const int FecaBreedId = 1;
    public const int SacrierBreedId = 11;
    public const int PandawaBreedId = 12;
    public const int OsamodasBreedId = 2;
    public const int MasqueraiderBreedId = 14;
    public const int ForgelanceBreedId = 20;

    /// <summary>
    /// Ancienne heuristique Sacrieur. Ni les sorts réellement appris et lançables,
    /// ni les états Souffrance/leur historique, ni le coût de la chaîne ne sont
    /// fournis par cette signature. Le planificateur simule déjà les effets des
    /// sorts reconnus : ne pas ajouter +60/+85 de gain imaginaire pour une
    /// proximité sans combo légal prouvé.
    /// </summary>
    public static double EvaluateSacrieurCombo(int casterCell, int targetCell, bool isAligned, int distance, int availableAp)
        => 0;

    /// <summary>
    /// Ancienne heuristique Panda + Roublard (+75/+120 fixes). Une case de mur et une
    /// estimation ne prouvent aucune activation : comme la surcharge Roublard, aucun
    /// bonus sans évènement de mur résolu. Les dégâts réels restent simulés ailleurs.
    /// </summary>
    public static double EvaluatePandaThrowIntoBombWall(int targetThrowCell, ImmutableHashSet<int> wallCells, int targetHp, int wallDmgEst)
        => 0;

    /// <summary>
    /// Ancienne heuristique Zobal (+30 par invocation proche). Elle ne lisait que la
    /// distance : aucun Plastron lancé ni bouclier résolu. Un vrai sort de bouclier est
    /// déjà valorisé par SpellResolver via le bouclier accordé ; aucun gain imaginaire.
    /// </summary>
    public static double EvaluateZobalShieldOnSummons(int zobalCell, IEnumerable<PlannerFighter> summons, int plastronRadius = 3)
        => 0;

    /// <summary>
    /// Ancienne heuristique Panda + Forgelance (+45/+90 pour des ennemis groupés). Elle
    /// s'ajoutait à chaque branche quel que soit le sort lancé. Les dégâts de zone qui
    /// touchent réellement sont simulés et comptés comme dégâts ; la géométrie seule non.
    /// </summary>
    public static double EvaluatePandaForgelanceCluster(IEnumerable<int> enemyCells, int aoeRadius = 2)
        => 0;
}
