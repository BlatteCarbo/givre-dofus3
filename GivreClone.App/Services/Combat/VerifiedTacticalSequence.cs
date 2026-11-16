using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GivreClone.Protocol;

namespace GivreClone.App.Services.Combat;

/// <summary>
/// A bounded, advisory class sequence. Each call suggests at most ONE candidate.
/// A candidate is never a dispatch authorization. The caller would still have
/// to establish transport identity, real server receipts, and all production
/// cast/movement guards. Neither plans nor synthetic tests are server receipts.
/// </summary>
public static class VerifiedTacticalSequence
{
    public sealed record Options(int MaxSteps = 2, bool AllowMovement = false, int MaxMpPerMove = 3);

    public sealed record Action(SpellCastDescriptor Spell, int TargetCell, long TargetActorId = 0,
        int StandCell = -1);

    /// <summary>
    /// All epochs and remaining resources must come from the same current fight.
    /// Missing board cells and unconfirmed fighter positions are unknown, not empty.
    /// LastServerOrder is the authoritative high-water mark for the supplied stream.
    /// PendingAction null means the transport/ACK state is unknown.
    /// Connection is an observed map-sector step predicate, not a guessed shortcut.
    /// KnownSpellGrades is optional for legacy advisory use; observed decision
    /// execution requires it on EVERY revalidation and rejects missing grades.
    /// </summary>
    public sealed record Snapshot(long FightId, int Generation, int MapGeneration, int Turn,
        long ActorId, int BreedId, int ActorCell, int? Ap, int? Mp, long LastServerOrder,
        bool? PendingAction, IReadOnlyList<PlannerCell>? Cells, IReadOnlyList<PlannerFighter>? Fighters,
        IReadOnlySet<int>? MarkedCells = null, IReadOnlyList<ClassMechanics.Portal>? Portals = null,
        Func<int, int, bool?>? Connection = null,
        IReadOnlyDictionary<int, int>? KnownSpellGrades = null);

    public sealed record Plan(ClassMechanics.ComboKind Kind, ClassMechanics.ComboFrame Frame,
        ClassMechanics.ComboEvidence Evidence, int MapGeneration, int Turn, int InitialActorCell,
        long BaseServerOrder, int InitialAp, int InitialMp, Options Limits,
        ImmutableArray<Action> Actions);

    public sealed record PlanResult(Plan? Value, string Reason)
    {
        public bool Accepted => Value is not null;
    }

    /// <summary>Instruction is a MOVE or CAST *suggestion*, never both.
    /// A completed move must be observed on the server before a CAST can be proposed.
    /// No path here grants transport-bound dispatch authorization.</summary>
    public sealed record Decision(bool Ready, Action? Next, PlannerAction? Instruction,
        int Stage, string Reason)
    {
        public bool CanDispatch => false;
        public static Decision Refuse(string why, int stage = 0)
            => new(false, null, null, stage, why);
    }

    /// <summary>
    /// Snapshot-local board index. A full board is validated once for an
    /// observed decision search; subsequent candidates reuse the exact same
    /// cells and living actor positions rather than rebuilding 560-entry
    /// dictionaries and repeated LINQ groupings. An index NEVER crosses a
    /// snapshot boundary, generation, turn or observation update.
    /// </summary>
    internal sealed class CastBoardIndex
    {
        internal readonly Snapshot Snapshot;
        internal readonly PlannerCell?[] Cells;
        internal readonly PlannerFighter?[] Occupants;
        internal readonly PlannerFighter Self;

        internal CastBoardIndex(Snapshot snapshot, PlannerCell?[] cells,
            PlannerFighter?[] occupants, PlannerFighter self)
        {
            Snapshot = snapshot;
            Cells = cells;
            Occupants = occupants;
            Self = self;
        }
    }

    internal static CastBoardIndex? IndexCastBoard(Snapshot snapshot)
    {
        if (snapshot?.Cells is null || snapshot.Fighters is null
            || (uint)snapshot.ActorCell >= MapPathfinder.CellCount)
            return null;
        var cells = new PlannerCell?[MapPathfinder.CellCount];
        foreach (var cell in snapshot.Cells)
        {
            if (cell is null || (uint)cell.Id >= MapPathfinder.CellCount
                || cells[cell.Id] is not null) return null;
            cells[cell.Id] = cell;
        }
        var occupants = new PlannerFighter?[MapPathfinder.CellCount];
        var actorIds = new HashSet<long>();
        PlannerFighter? self = null;
        foreach (var fighter in snapshot.Fighters)
        {
            if (fighter is null || fighter.Life <= 0) continue;
            if (fighter.Id == 0 || !fighter.CanTarget
                || (uint)fighter.Cell >= MapPathfinder.CellCount
                || !actorIds.Add(fighter.Id) || occupants[fighter.Cell] is not null)
                return null;
            occupants[fighter.Cell] = fighter;
            if (fighter.Id == snapshot.ActorId)
            {
                if (fighter.Enemy || fighter.Cell != snapshot.ActorCell)
                    return null;
                self = fighter;
            }
        }
        return self is null ? null : new(snapshot, cells, occupants, self);
    }

    private static int Breed(ClassMechanics.ComboKind kind) => kind switch
    {
        ClassMechanics.ComboKind.RogueWall => ClassMechanics.RogueBreedId,
        ClassMechanics.ComboKind.EliotropeRedirect => ClassMechanics.EliotropeBreedId,
        ClassMechanics.ComboKind.XelorTelefrag => ClassMechanics.XelorBreedId,
        ClassMechanics.ComboKind.FecaGlyph => ClassMechanics.FecaBreedId,
        ClassMechanics.ComboKind.EcaflipCard => ClassMechanics.EcaflipBreedId,
        ClassMechanics.ComboKind.SacrierSuffering => ClassMechanics.SacrierBreedId,
        _ => 0
    };

    private static bool HasEffect(SpellCastDescriptor spell, Func<SpellEffectDescriptor, bool> match)
        => !spell.Effects.IsDefaultOrEmpty && spell.Effects.Any(match);

    private static bool TrustedSpell(ClassMechanics.ComboEvidence evidence, SpellCastDescriptor? spell)
        => spell is { SpellId: > 0, Grade: > 0, Ap: > 0, Uncertain: false }
            && spell.MinRange >= 0 && spell.Range >= spell.MinRange
            && spell.MaxPerTurn >= 0 && spell.MaxPerTarget >= 0
            && string.IsNullOrEmpty(spell.StatesCriterion)
            && !spell.Effects.IsDefaultOrEmpty
            && evidence.ObservedLearnedGrades is not null
            && evidence.ObservedLearnedGrades.TryGetValue(spell.SpellId, out int grade)
            && grade == spell.Grade;

    private static bool ValidRoles(ClassMechanics.ComboKind kind,
        ClassMechanics.ComboEvidence evidence, ImmutableArray<Action> actions)
    {
        if (actions.IsDefaultOrEmpty || !TrustedSpell(evidence, evidence.SourceSpell)
            || actions[0].Spell != evidence.SourceSpell) return false;
        var source = actions[0].Spell;
        if (actions.Length > 1 && (!TrustedSpell(evidence, evidence.FollowupSpell)
            || actions[1].Spell != evidence.FollowupSpell
            || evidence.FollowupBreedId != Breed(kind))) return false;
        return kind switch
        {
            // The installed 13455 explosion descriptor is NOT a player-cast
            // Detonator. No second Rogue cast is inferred from it.
            ClassMechanics.ComboKind.RogueWall => actions.Length == 1
                && source.SpellId == 13444
                && HasEffect(source, e => e.ActionId == 1008
                    && e.DiceNum == ClassMechanics.VerifiedExplobombTemplate),
            ClassMechanics.ComboKind.EliotropeRedirect => HasEffect(source, e => e.ActionId == 1181)
                && (actions.Length == 1 || evidence.EffectActionId > 0
                    && HasEffect(actions[1].Spell, e => e.ActionId == evidence.EffectActionId)),
            ClassMechanics.ComboKind.XelorTelefrag => HasEffect(source, e => e.Kind == SpellEffectKind.Swap
                    && e.ActionId == 8)
                && (actions.Length == 1 || evidence.StateId is ClassMechanics.StateTelefrag
                    or ClassMechanics.StateAllyTelefrag),
            ClassMechanics.ComboKind.FecaGlyph => HasEffect(source, e => e.PlacesGroundMark
                    && e.ActionId != 1181)
                && (actions.Length == 1 || evidence.EffectActionId > 0
                    && HasEffect(actions[1].Spell, e => e.ActionId == evidence.EffectActionId)),
            ClassMechanics.ComboKind.EcaflipCard => evidence.StateId > 0
                && HasEffect(source, e => e.ActionId == 950
                    && (e.Value != 0 ? e.Value : e.Min) == evidence.StateId)
                && (actions.Length == 1 || evidence.EffectActionId > 0
                    && HasEffect(actions[1].Spell, e => e.ActionId == evidence.EffectActionId)),
            ClassMechanics.ComboKind.SacrierSuffering => HasEffect(source, e =>
                    e.Kind is SpellEffectKind.Pull or SpellEffectKind.Swap)
                && (actions.Length == 1 || evidence.StateId > 0
                    && TrustedSpell(evidence, evidence.StateSourceSpell)
                    && evidence.StateSourceBreedId == ClassMechanics.SacrierBreedId
                    && HasEffect(evidence.StateSourceSpell!, e => e.ActionId == 950
                        && (e.Value != 0 ? e.Value : e.Min) == evidence.StateId)
                    && HasEffect(actions[1].Spell, e => e.Kind == SpellEffectKind.Damage
                        && e.ActionId == evidence.EffectActionId)),
            _ => false
        };
    }

    public static PlanResult Prepare(ClassMechanics.ComboKind kind,
        ClassMechanics.ComboFrame frame, ClassMechanics.ComboEvidence evidence,
        Snapshot snapshot, IReadOnlyList<Action> proposed, Options? options = null)
        => PrepareCore(kind, frame, evidence, snapshot, proposed, options, null);

    internal static PlanResult PrepareIndexed(ClassMechanics.ComboKind kind,
        ClassMechanics.ComboFrame frame, ClassMechanics.ComboEvidence evidence,
        Snapshot snapshot, IReadOnlyList<Action> proposed, CastBoardIndex index,
        Options? options = null)
        => PrepareCore(kind, frame, evidence, snapshot, proposed, options, index);

    private static PlanResult PrepareCore(ClassMechanics.ComboKind kind,
        ClassMechanics.ComboFrame frame, ClassMechanics.ComboEvidence evidence,
        Snapshot snapshot, IReadOnlyList<Action> proposed, Options? options,
        CastBoardIndex? index)
    {
        static PlanResult No(string reason) => new(null, reason);
        var limits = options ?? new Options();
        if (frame is null || evidence is null || snapshot is null || proposed is null
            || limits.MaxSteps is < 1 or > 2 || limits.MaxMpPerMove is < 0 or > 12
            || proposed.Count is < 1 or > 2 || proposed.Count > limits.MaxSteps
            || frame.FightId <= 0 || frame.Generation <= 0 || frame.ActorId == 0
            || frame.FirstTurn <= 0 || frame.LastTurn < frame.FirstTurn
            || snapshot.FightId != frame.FightId || snapshot.Generation != frame.Generation
            || snapshot.MapGeneration <= 0 || snapshot.Turn < frame.FirstTurn
            || snapshot.Turn > frame.LastTurn || snapshot.ActorId != frame.ActorId
            || snapshot.BreedId != frame.BreedId || Breed(kind) != frame.BreedId)
            return No("fight, map, turn or breed provenance missing");
        if (evidence.ClientVersion != ClassMechanics.VerifiedClassClientVersion
            || evidence.SourceBreedId != frame.BreedId)
            return No("catalog client version or spell breed missing");
        if (snapshot.Ap is not >= 0 || snapshot.Mp is not >= 0 || snapshot.PendingAction != false
            || snapshot.LastServerOrder < 0)
            return No("current AP, MP, server order or ACK state unknown");
        var actions = proposed.ToImmutableArray();
        if (!ValidRoles(kind, evidence, actions))
            return No("unverified class spell, grade or sequence role");
        long totalAp = actions.Sum(a => (long)a.Spell.Ap);
        if (totalAp > snapshot.Ap || actions.Length > 1 && actions[1].StandCell >= 0
            || actions.Any(a => a.StandCell >= 0 && a.StandCell != snapshot.ActorCell)
                && !limits.AllowMovement)
            return No("planned AP/MP budget or movement policy");
        var initial = CheckCast(snapshot, actions[0], limits, out _, index);
        if (initial is not null) return No(initial);
        return new(new Plan(kind, frame, evidence, snapshot.MapGeneration, snapshot.Turn,
            snapshot.ActorCell,
            snapshot.LastServerOrder, snapshot.Ap.Value, snapshot.Mp.Value, limits, actions),
            "bounded candidate; revalidate before every action");
    }

    public static Decision Next(Plan plan, Snapshot now,
        IReadOnlyList<ClassMechanics.ComboObservation> observations)
        => NextCore(plan, now, observations, null);

    internal static Decision NextIndexed(Plan plan, Snapshot now,
        IReadOnlyList<ClassMechanics.ComboObservation> observations,
        CastBoardIndex index)
        => NextCore(plan, now, observations, index);

    private static Decision NextCore(Plan plan, Snapshot now,
        IReadOnlyList<ClassMechanics.ComboObservation> observations,
        CastBoardIndex? index)
    {
        if (plan is null || now is null || observations is null
            || now.FightId != plan.Frame.FightId || now.Generation != plan.Frame.Generation
            || now.MapGeneration != plan.MapGeneration || now.Turn != plan.Turn
            || now.ActorId != plan.Frame.ActorId || now.BreedId != plan.Frame.BreedId
            || now.PendingAction != false || now.Ap is not >= 0 || now.Mp is not >= 0
            || now.Fighters is null || now.Cells is null
            || now.LastServerOrder < plan.BaseServerOrder)
            return Decision.Refuse("stale generation, turn, map or unknown resource/ACK state");
        if (observations.Count == 0)
        {
            // Common first-step advisory path: no packet observations exist
            // after admission. Avoid Zip/Where/Any iterator allocations per
            // candidate, but refuse a high-water mark that indicates lost
            // server activity (empty stream must NOT imply an idle transport).
            if (now.LastServerOrder != plan.BaseServerOrder)
                return Decision.Refuse("mixed, out-of-order or incomplete server stream");
            return Decide(now, plan, plan.Actions[0], 0,
                Array.Empty<ClassMechanics.ComboObservation>(), plan.BaseServerOrder, index);
        }
        if (observations.Any(e => e.FightId != plan.Frame.FightId
            || e.Generation != plan.Frame.Generation || e.Turn != plan.Turn
            || e.Order <= 0)
            || observations.Zip(observations.Skip(1), (a, b) => a.Order >= b.Order).Any(x => x)
            || (observations.Count == 0 ? plan.BaseServerOrder
                : observations[^1].Order) != now.LastServerOrder)
            return Decision.Refuse("mixed, out-of-order or incomplete server stream");

        var recent = observations.Where(e => e.Order > plan.BaseServerOrder).ToArray();
        var first = plan.Actions[0];
        // Any matching cast, including an attempted/unconfirmed row, blocks
        // resending stage 0. A successful send itself is not an ACK.
        var attempts = recent.Where(e => e.Kind == ClassMechanics.ComboEventKind.Cast
            && e.SpellId == first.Spell.SpellId).ToArray();
        if (attempts.Length == 0)
        {
            if (recent.Any(e => e.Kind == ClassMechanics.ComboEventKind.Cast
                && e.Source == now.ActorId))
                return Decision.Refuse("another caster action intervened");
            return Decide(now, plan, first, 0, recent, plan.BaseServerOrder, index);
        }
        if (attempts.Length != 1) return Decision.Refuse("ambiguous or repeated first cast");
        var cast = attempts[0];
        if (!cast.ServerConfirmed || cast.Source != now.ActorId || cast.Grade != first.Spell.Grade
            || cast.ToCell != first.TargetCell || cast.Correlation <= 0)
            return Decision.Refuse("missing or mismatched authoritative cast ACK");
        if (plan.Actions.Length == 1)
            return Decision.Refuse("one-action sub-sequence complete", 1);
        if (!ConfirmedEffect(plan, now, cast, recent))
            return Decision.Refuse("class effect, ownership or ordered proof missing", 1);
        var second = plan.Actions[1];
        if (recent.Any(e => e.Kind == ClassMechanics.ComboEventKind.Cast
            && e.SpellId == second.Spell.SpellId && e.Order > cast.Order))
            return Decision.Refuse("second action already sent or acknowledged", 2);
        return Decide(now, plan, second, 1, recent, cast.Order, index);
    }

    /// <summary>
    /// Historical confirmation for an already observed second cast. Never
    /// reconstruct AP or pretend the second cast is currently legal after
    /// resources have been consumed. Its prerequisites must have been observed
    /// before that cast and must not have been invalidated by later events.
    /// This is a REPLAY check, never a send authorization.
    /// </summary>
    internal static bool HasCurrentFollowupProof(Plan plan, Snapshot now,
        IReadOnlyList<ClassMechanics.ComboObservation> observations, long secondCastOrder)
    {
        if (plan is null || now is null || observations is null
            || plan.Actions.Length != 2 || now.Fighters is null
            || secondCastOrder <= plan.BaseServerOrder)
            return false;
        var recent = observations.Where(e => e.Order > plan.BaseServerOrder).ToArray();
        var before = recent.Where(e => e.Order < secondCastOrder).ToArray();
        var casts = before.Where(e => e.Kind == ClassMechanics.ComboEventKind.Cast
            && e.SpellId == plan.Actions[0].Spell.SpellId).ToArray();
        if (casts.Length != 1) return false;
        var first = casts[0];
        return first.ServerConfirmed && first.Correlation > 0
            && first.Source == now.ActorId && first.Grade == plan.Actions[0].Spell.Grade
            && first.ToCell == plan.Actions[0].TargetCell
            && ConfirmedEffect(plan, now, first, before)
            && ConfirmedEffect(plan, now, first, recent);
    }

    private static Decision Decide(Snapshot now, Plan plan, Action action, int stage,
        ClassMechanics.ComboObservation[] recent, long previousCastOrder,
        CastBoardIndex? index)
    {
        var reason = CheckCast(now, action, plan.Limits, out int mpCost, index);
        if (reason is not null) return Decision.Refuse(reason, stage);
        if (action.StandCell >= 0 && action.StandCell != now.ActorCell)
            return new(true, action, new PlannerAction(PlannerActionKind.Move,
                action.StandCell, Mp: mpCost), stage, "move candidate; revalidate after server position");
        if (stage == 0 && action.StandCell >= 0
            && action.StandCell != plan.InitialActorCell
            && !recent.Any(e => e.ServerConfirmed && e.Kind == ClassMechanics.ComboEventKind.Move
                && e.Source == now.ActorId && e.Subject == now.ActorId
                && e.FromCell == plan.InitialActorCell && e.ToCell == action.StandCell
                && e.Order > previousCastOrder))
            return Decision.Refuse("server move confirmation missing", stage);
        return new(true, action, new PlannerAction(PlannerActionKind.Cast,
            action.TargetCell, action.Spell.SpellId, action.TargetActorId, Ap: action.Spell.Ap),
            stage, "cast candidate; execution legality must still be checked");
    }

    private static string? CheckCast(Snapshot now, Action action, Options options,
        out int mpCost, CastBoardIndex? index = null)
    {
        mpCost = 0;
        if ((uint)now.ActorCell >= MapPathfinder.CellCount
            || (uint)action.TargetCell >= MapPathfinder.CellCount
            || action.StandCell < -1 || action.StandCell >= MapPathfinder.CellCount
            || now.Cells is null || now.Fighters is null
            || now.Ap is not int ap || now.Mp is not int mp
            || action.Spell.Ap <= 0 || action.Spell.Ap > ap
            || action.Spell.Targets == SpellTargets.None)
            return "invalid geometry or AP/MP/board unknown";
        var board = index is { } cached && ReferenceEquals(cached.Snapshot, now)
            ? cached : IndexCastBoard(now);
        if (board is null) return "fighter position, identity or visibility unknown";
        var cells = board.Cells;
        var occupants = board.Occupants;
        int from = action.StandCell < 0 ? now.ActorCell : action.StandCell;
        if (cells[from] is null || cells[action.TargetCell] is null)
            return "missing map cells";
        if (from != now.ActorCell)
        {
            if (!options.AllowMovement || now.Connection is null
                || !cells[from]!.Walkable || occupants[from] is not null)
                return "movement not proven";
            bool unknown = false;
            var reachable = MapPathfinder.OrthoReachable(now.ActorCell,
                Math.Min(mp, options.MaxMpPerMove),
                c => (uint)c < MapPathfinder.CellCount && cells[c] is { Walkable: true }
                    && occupants[c] is null,
                (a, b) =>
                {
                    bool? connection = now.Connection(a, b);
                    if (!connection.HasValue) unknown = true;
                    return connection == true;
                });
            if (unknown || !reachable.TryGetValue(from, out mpCost))
                return "MP path or terrain connection unverified";
        }
        int distance = MapPathfinder.CellDistance(from, action.TargetCell);
        if (distance < action.Spell.MinRange || distance > action.Spell.Range
            || action.Spell.CastInLine && !MapPathfinder.Aligned(from, action.TargetCell)
            || action.Spell.CastInDiagonal && !MapPathfinder.Diagonal(from, action.TargetCell))
            return "cast range or alignment invalid";
        if (action.Spell.TestLos)
        {
            // CastLegality's nullable callbacks convention permits unknown LoS.
            // Here each traversed ray cell must be present before delegating.
            if (MapPathfinder.LineCells(from, action.TargetCell)
                .Skip(1).SkipLast(1).Any(c =>
                    (uint)c >= MapPathfinder.CellCount || cells[c] is null))
                return "LoS unknown";
            if (!MapPathfinder.HasLineOfSight(from, action.TargetCell,
                c => (uint)c >= MapPathfinder.CellCount || cells[c] is not { BlocksLos: false },
                c => c != from && c != action.TargetCell
                    && (uint)c < MapPathfinder.CellCount && occupants[c] is not null))
                return "LoS blocked";
        }
        bool occupied = occupants[action.TargetCell] is not null;
        if (action.Spell.NeedFreeCell && occupied || action.Spell.NeedTakenCell && !occupied
            || action.Spell.NeedTakenCell && action.TargetActorId == 0)
            return "target occupancy invalid";
        // A known bomb summon also creates persistent ground occupancy and
        // must not select a cell with an observed active mark of uncertain
        // interaction. Never assume the mark and bomb can coexist legally.
        bool persistentPlacement = action.Spell.PlacesGroundMark != false
            || action.Spell.Effects.Any(e => e.ActionId == 1008
                && e.DiceNum == ClassMechanics.VerifiedExplobombTemplate);
        if (persistentPlacement)
        {
            if (now.MarkedCells is null) return "mark coverage unknown";
            if (!occupied && now.MarkedCells.Contains(action.TargetCell))
                return "ground mark already occupies target";
        }
        if (action.TargetActorId != 0)
        {
            var target = occupants[action.TargetCell];
            if (target is null || target.Cell != action.TargetCell)
                return "target actor or position unconfirmed";
            if (target.Id != action.TargetActorId)
                return "target actor or position unconfirmed";
            var wanted = target.Id == now.ActorId ? SpellTargets.Self
                : target.Enemy ? SpellTargets.Enemy : SpellTargets.Ally;
            if ((action.Spell.Targets & wanted) == 0) return "spell target mask unverified";
        }
        else if (occupied && action.Spell.NeedFreeCell)
            return "free-cell action targets occupied cell";
        return null;
    }

    private static bool ConfirmedEffect(Plan plan, Snapshot now,
        ClassMechanics.ComboObservation cast, ClassMechanics.ComboObservation[] stream)
    {
        var e = plan.Evidence;
        var after = stream.Where(x => x.Order > cast.Order).ToArray();
        bool Row(ClassMechanics.ComboObservation x, ClassMechanics.ComboEventKind kind)
            => x.ServerConfirmed && x.Kind == kind && x.Source == now.ActorId
                && x.Correlation == cast.Correlation;
        bool State(int id, long actor) => id > 0 && after.Any(x => Row(x,
                ClassMechanics.ComboEventKind.StateAdded) && x.Subject == actor && x.StateId == id)
            && !after.Any(x => x.ServerConfirmed && x.Kind == ClassMechanics.ComboEventKind.StateAdded
                && x.Subject == actor && x.StateId == id && (x.Source != now.ActorId
                    || x.Correlation != cast.Correlation))
            && !after.Any(x => x.ServerConfirmed && x.Kind == ClassMechanics.ComboEventKind.StateRemoved
                && x.Subject == actor && x.StateId == id)
            && now.Fighters!.Any(f => f.Id == actor && f.StatesKnown == true
                && f.States?.Contains(id) == true);
        bool Mark(int actionId, int cell, out int markId)
        {
            markId = 0;
            var placed = after.Where(x => Row(x, ClassMechanics.ComboEventKind.MarkPlaced)
                && x.ActionId == actionId && x.ToCell == cell && x.MarkId > 0).ToArray();
            if (placed.Length != 1 || now.MarkedCells?.Contains(cell) != true) return false;
            int matchedMarkId = placed[0].MarkId;
            markId = matchedMarkId;
            return after.Any(x => x.Order > placed[0].Order
                    && x.ServerConfirmed && x.Kind == ClassMechanics.ComboEventKind.MarkActivated
                    && x.Source == now.ActorId && x.MarkId == matchedMarkId)
                && !after.Any(x => x.ServerConfirmed && x.MarkId == matchedMarkId
                    && x.Kind is ClassMechanics.ComboEventKind.MarkPlaced
                        or ClassMechanics.ComboEventKind.MarkActivated
                    && x.Source != now.ActorId)
                && !after.Any(x => x.ServerConfirmed && x.MarkId == matchedMarkId
                    && x.Kind is ClassMechanics.ComboEventKind.MarkRemoved
                        or ClassMechanics.ComboEventKind.MarkDeactivated
                        or ClassMechanics.ComboEventKind.PortalDeactivated);
        }
        switch (plan.Kind)
        {
            case ClassMechanics.ComboKind.EliotropeRedirect:
            {
                if (!Mark(1181, plan.Actions[0].TargetCell, out int markId)
                    || now.Portals is null || e.TargetId == 0) return false;
                var owned = now.Portals.Where(p => p.Active && p.OwnerId == now.ActorId).ToArray();
                if (owned.Length is < 2 or > 4
                    || owned.Any(p => p.Id <= 0)
                    || owned.Select(p => p.Id).Distinct().Count() != owned.Length
                    || !owned.Any(p => p.Id == markId && p.Cell == plan.Actions[0].TargetCell))
                    return false;
                var next = plan.Actions[1];
                var shot = ClassMechanics.ProjectThroughPortals(now.ActorCell, next.TargetCell,
                    owned, now.ActorId);
                return shot is { } hit && hit.Impact == e.TargetCell
                    && now.Fighters!.Any(f => f.Id == e.TargetId && f.Cell == hit.Impact && f.CanTarget);
            }
            case ClassMechanics.ComboKind.XelorTelefrag:
            {
                if (e.TargetId == 0 || !e.TargetIsEnemy.HasValue || e.FirstCell < 0
                    || e.SecondCell < 0 || e.FirstCell == e.SecondCell
                    || e.StateId != (e.TargetIsEnemy.Value
                        ? ClassMechanics.StateTelefrag : ClassMechanics.StateAllyTelefrag))
                    return false;
                bool Move(long subject, int start, int end) => after.Count(x =>
                    Row(x, ClassMechanics.ComboEventKind.Move) && x.Subject == subject
                        && x.FromCell == start && x.ToCell == end) == 1;
                return Move(now.ActorId, e.FirstCell, e.SecondCell)
                    && Move(e.TargetId, e.SecondCell, e.FirstCell)
                    && now.ActorCell == e.SecondCell
                    && now.Fighters!.Any(f => f.Id == e.TargetId && f.Cell == e.FirstCell
                        && f.Enemy == e.TargetIsEnemy.Value)
                    && State(e.StateId, e.TargetId);
            }
            case ClassMechanics.ComboKind.FecaGlyph:
                return plan.Actions[0].Spell.Effects.Where(x => x.PlacesGroundMark
                    && x.ActionId != 1181).Any(x =>
                        Mark(x.ActionId, plan.Actions[0].TargetCell, out _));
            case ClassMechanics.ComboKind.EcaflipCard:
                return State(e.StateId, now.ActorId);
            case ClassMechanics.ComboKind.SacrierSuffering:
                return e.TargetId != 0 && State(e.StateId, now.ActorId)
                    && e.FirstCell >= 0 && e.SecondCell >= 0 && e.FirstCell != e.SecondCell
                    && after.Count(x => Row(x, ClassMechanics.ComboEventKind.Move)
                        && x.Subject == e.TargetId && x.FromCell == e.FirstCell
                        && x.ToCell == e.SecondCell) == 1
                    && now.Fighters!.Any(f => f.Id == e.TargetId && f.Cell == e.SecondCell);
            default: return false;
        }
    }
}
