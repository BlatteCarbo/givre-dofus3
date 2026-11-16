using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GivreClone.Protocol;

namespace GivreClone.App.Services.Combat;

/// <summary>
/// Read-only execution bridge for one bounded tactical plan. The live send seams
/// (LaunchService.SendCast and IActionSender) expose dispatch success, not a
/// transport-generation-bound, correlated positive server cast acknowledgment.
/// CombatComboIngressBuffer presently has no verified transport generation and
/// downgrades its events to ServerConfirmed=false. This bridge has intentionally
/// NO sender and never grants permission to dispatch a move or cast.
///
/// A confirmed phase means that matching, explicitly ServerConfirmed observations
/// were supplied for replay; it never means this bridge sent or acknowledged a
/// command. Synthetic test observations are not a live proof source.
/// </summary>
public sealed class VerifiedTacticalExecutor
{
    public enum Phase
    {
        Disabled,
        Advisory,
        Pending,
        Confirmed,
        Expired
    }

    /// <summary>Bound both work and the amount of unobserved fight activity
    /// between a candidate and its revalidation.</summary>
    public sealed record Limits(int MaxReviews = 64, int MaxObservations = 128,
        int MaxOrderAdvance = 128);

    public sealed record Review(
        Phase State, int ConfirmedStages, PlannerAction? AdvisoryAction,
        string Reason, long LastObservedOrder)
    {
        /// <summary>
        /// Hard false until a future, separately reviewed transport-bound
        /// ingress+dispatch integration exists. A bool supplied by a caller,
        /// a PID, a sequence end, a UID, or a synthetic server event is never
        /// sufficient to turn this into a dispatch authorization.
        /// </summary>
        public bool CanDispatch => false;
        public bool IsAdvisory => AdvisoryAction is not null;
        public string DispatchBlockedBy =>
            "no authenticated transport generation and request-correlated server ACK";
    }

    /// <summary>
    /// Inputs must be copied from a single observed turn, the installed class
    /// catalogue and the actual SpellsEvent grade book. The caller must NOT
    /// substitute the class-spell fallback for ObservedLearnedGrades. Spell
    /// descriptors do not themselves prove the actor owns their spell.
    /// KnownSpellGrades on Snapshot must independently match this book.
    /// </summary>
    public sealed record ObservedDecisionFacts(
        ClassMechanics.ComboFrame Frame,
        VerifiedTacticalSequence.Snapshot Snapshot,
        string ClientVersion,
        IReadOnlyDictionary<int, int>? ObservedLearnedGrades,
        IReadOnlyList<SpellCastDescriptor>? CatalogSpells,
        int MaxCandidateChecks = 4096,
        // Optional, class-specific IDs whose semantic meaning has been
        // independently checked against THIS client. Without such a join,
        // arbitrary ActionId=950 states must not be called Ecaflip cards or
        // Sacrier suffering. This is never filled from a generic buff row.
        IReadOnlySet<int>? VerifiedClassStateIds = null);

    public sealed record ObservedDecisionSelection(
        VerifiedTacticalExecutor? Executor, ClassMechanics.ComboKind? Kind,
        PlannerAction? Preview, int CheckedCandidates, string Reason)
    {
        public bool Accepted => Executor is not null && Preview is not null;
        public bool CanDispatch => false;
        /// <summary>Acceptance is a geometry/grade admission, never an ACK or a
        /// transport-bound permission. This blocker applies even to a two-cast
        /// candidate and cannot be overridden by a caller-provided flag.</summary>
        public string DispatchBlockedBy =>
            "authenticated outbound transport and correlated inbound cast ACK unavailable";
        public int PlannedActions => Executor?.AdvisoryPlan.Actions.Length ?? 0;
    }

    private const int MaxObservedSpells = 64;
    private const int MaxObservedFighters = 64;
    private const int MaxObservedPortals = 64;
    private const int MaxTargetsPerSpell = 64;
    // Only client-exported 3.7.13.17 identities, never guessed effect IDs.
    // A second step is conditional on live, independently observed causal
    // effects; the catalogue is merely a proposed route, never a receipt.
    private const int VerifiedEliotropePortalSpell = 14574;
    private const int VerifiedEliotropeRaySpell = 14579;
    private const int VerifiedEliotropeRayDamageAction = 99;
    private const int VerifiedXelorSwapSpell = 13285;
    private const int VerifiedXelorFreezeSpell = 13245;
    private readonly ImmutableDictionary<int, int>? _boundLearnedGrades;
    private readonly int _boundStateId;
    private readonly bool? _boundStatePresent;
    private readonly ImmutableArray<ClassMechanics.Portal> _boundPortals;
    private bool _observedClassStateGrant;
    public ClassMechanics.ComboKind Tactic => _plan.Kind;

    private readonly object _gate = new();
    private readonly VerifiedTacticalSequence.Plan _plan;
    /// <summary>Immutable, one-turn advisory plan; never an authorization to send.</summary>
    public VerifiedTacticalSequence.Plan AdvisoryPlan => _plan;
    private readonly Limits _limits;
    private int _reviews;
    private int _confirmedStages;
    // Pin the exact server-replay identity on the transition to each stage.
    // Later Inspect calls must not replace an already confirmed cast at the
    // same high-water order with a different positive-looking correlation.
    private long _firstConfirmedOrder;
    private long _firstConfirmedCorrelation;
    private long _secondConfirmedOrder;
    private long _secondConfirmedCorrelation;
    private long _lastObservedOrder;
    private bool _enabled;
    private bool _expired;
    private bool _finished;

    private VerifiedTacticalExecutor(VerifiedTacticalSequence.Plan plan, Limits limits, bool enabled,
        ImmutableDictionary<int, int>? boundLearnedGrades = null,
        int boundStateId = 0, bool? boundStatePresent = null,
        IReadOnlyList<ClassMechanics.Portal>? boundPortals = null)
    {
        _plan = plan;
        _limits = limits;
        _enabled = enabled;
        _lastObservedOrder = plan.BaseServerOrder;
        _boundLearnedGrades = boundLearnedGrades;
        _boundStateId = boundStateId;
        _boundStatePresent = boundStatePresent;
        _boundPortals = boundPortals is null ? default : boundPortals.ToImmutableArray();
    }

    /// <summary>
    /// Deterministic class-aware admission and decision, integrated into the
    /// SAME executor lifecycle as the manually supplied plans. The current
    /// advisory stage contains exactly one cast/move candidate; a second cast
    /// may be admitted as a dormant, bounded continuation only for an installed
    /// class spell/grade/effect pair with independently observed geometry.
    /// Each subsequent stage requires a correlated server-effect replay.
    /// Neither its execution nor the success of a class combo is inferred.
    /// Exact effects and target masks come from supplied grade-specific
    /// descriptors; no spell/state/ACK is synthesized. All six class variants
    /// still use the admission rules of VerifiedTacticalSequence.
    ///
    /// Enabling this API does not grant transport authority: even an accepted
    /// selection and every subsequent Review have CanDispatch=false.
    /// </summary>
    public static ObservedDecisionSelection PrepareObservedDecision(
        ObservedDecisionFacts facts, VerifiedTacticalSequence.Options? options = null,
        Limits? executorLimits = null, bool enabled = false)
    {
        static ObservedDecisionSelection No(string reason, int evaluated = 0)
            => new(null, null, null, evaluated, reason);
        if (facts is null || facts.Frame is null || facts.Snapshot is null
            || facts.ObservedLearnedGrades is null || facts.CatalogSpells is null)
            return No("missing observed decision context");
        var now = facts.Snapshot;
        var limits = executorLimits ?? new Limits();
        var opt = options ?? new VerifiedTacticalSequence.Options();
        if (!ValidLimits(limits) || opt.MaxSteps is < 1 or > 2
            || opt.MaxMpPerMove is < 0 or > 12 || opt.AllowMovement
            || facts.MaxCandidateChecks is < 1 or > 4096
            || facts.ClientVersion != ClassMechanics.VerifiedClassClientVersion
            || facts.Frame.FightId <= 0 || facts.Frame.Generation <= 0
            || facts.Frame.ActorId == 0 || now.ActorId != facts.Frame.ActorId
            || now.BreedId != facts.Frame.BreedId
            || facts.Frame.FirstTurn != now.Turn || facts.Frame.LastTurn != now.Turn
            || now.FightId != facts.Frame.FightId
            || now.Generation != facts.Frame.Generation
            || now.MapGeneration <= 0 || now.PendingAction != false
            || now.Ap is not >= 0 || now.Mp is not >= 0
            || now.LastServerOrder < 0)
            return No("unverified version, turn, actor, resources or pending transport");

        if (facts.CatalogSpells.Count is < 1 or > MaxObservedSpells
            || facts.ObservedLearnedGrades.Count is < 1 or > 128
            || facts.ObservedLearnedGrades.Any(p => p.Key <= 0 || p.Value <= 0)
            || now.KnownSpellGrades is null
            || now.KnownSpellGrades.Count != facts.ObservedLearnedGrades.Count
            || facts.ObservedLearnedGrades.Any(p =>
                !now.KnownSpellGrades.TryGetValue(p.Key, out int grade) || grade != p.Value)
            || facts.CatalogSpells.Any(d => d is null || d.SpellId <= 0 || d.Grade <= 0)
            || facts.CatalogSpells.Select(d => d.SpellId).Distinct().Count()
                != facts.CatalogSpells.Count)
            return No("actual learned-grade or unique catalogue proof missing");
        if (now.Cells is null || now.Cells.Count != MapPathfinder.CellCount
            || now.Cells.Any(c => c is null || (uint)c.Id >= MapPathfinder.CellCount)
            || now.Cells.Select(c => c.Id).Distinct().Count() != MapPathfinder.CellCount
            || now.Fighters is null || now.Fighters.Count is < 2 or > MaxObservedFighters
            || now.Fighters.Any(f => f is null || f.Id == 0 || !f.CanTarget
                || (uint)f.Cell >= MapPathfinder.CellCount)
            || now.Fighters.Select(f => f.Id).Distinct().Count() != now.Fighters.Count
            || now.Fighters.Select(f => f.Cell).Distinct().Count() != now.Fighters.Count
            || now.Fighters.Count(f => f.Id == now.ActorId && !f.Enemy
                && f.Cell == now.ActorCell && f.Life > 0) != 1
            || now.MarkedCells is null)
            return No("unknown or incomplete board, mark, fighter or visibility facts");
        var boardIndex = VerifiedTacticalSequence.IndexCastBoard(now);
        if (boardIndex is null)
            return No("stale, duplicated or otherwise unindexable combat board");

        var self = now.Fighters.Single(f => f.Id == now.ActorId);
        var enemies = now.Fighters.Where(f => f.Enemy && f.Life > 0)
            .OrderBy(f => MapPathfinder.CellDistance(now.ActorCell, f.Cell))
            .ThenBy(f => f.Id).ToArray();
        if (enemies.Length == 0) return No("no observed targetable enemy");
        if (now.Portals is { } portals
            && (portals.Count > MaxObservedPortals
                || portals.Any(p => p is null || p.Id <= 0 || p.OwnerId == 0
                || (uint)p.Cell >= MapPathfinder.CellCount)
                || portals.Select(p => p.Id).Distinct().Count() != portals.Count))
            return No("portal owner, ID or position ambiguous");

        var learned = facts.ObservedLearnedGrades.ToImmutableDictionary();
        var descriptors = facts.CatalogSpells.Where(d =>
                learned.TryGetValue(d.SpellId, out int grade) && grade == d.Grade
                && !d.Uncertain && !d.Effects.IsDefaultOrEmpty)
            .OrderBy(d => d.SpellId).ThenBy(d => d.Grade).ToArray();
        var availableStateSources = descriptors.Where(d => d.Effects.Any(e =>
                e.ActionId == 950 && Immediate(e)
                && (e.Value != 0 ? e.Value : e.Min) > 0))
            .ToArray();
        if (descriptors.Length == 0) return No("no matching learned grade in catalogue");

        // Input order, dictionary insertion order and cell enumeration order
        // cannot change the winner. We never substitute a predicted position,
        // unobserved actor state or damage formula for a current fact.
        // The cached board is already an authoritative occupancy index. Avoid
        // a second HashSet and skip entire cell sorts for class roles that
        // target actors rather than ground/portal entry cells.
        var freeCells = now.BreedId is ClassMechanics.RogueBreedId
            or ClassMechanics.FecaBreedId
            ? now.Cells.Where(c => c.Walkable
                    && boardIndex.Occupants[c.Id] is null
                    && !now.MarkedCells.Contains(c.Id))
                .OrderBy(c => enemies.Min(e => MapPathfinder.CellDistance(c.Id, e.Cell)))
                .ThenBy(c => MapPathfinder.CellDistance(now.ActorCell, c.Id))
                .ThenBy(c => c.Id)
                .Take(MaxTargetsPerSpell)
                .Select(c => c.Id).ToArray()
            : Array.Empty<int>();
        // New portals are an ENTRY positioned relative to the caster, not
        // necessarily near the enemy at the remote EXIT. Candidate selection
        // must not prune a valid projected shot solely by target distance.
        var entryCells = now.BreedId == ClassMechanics.EliotropeBreedId
            ? now.Cells.Where(c => c.Walkable
                    && boardIndex.Occupants[c.Id] is null
                    && !now.MarkedCells.Contains(c.Id))
                .OrderBy(c => MapPathfinder.CellDistance(now.ActorCell, c.Id))
                .ThenBy(c => c.Id)
                .Take(MaxTargetsPerSpell).Select(c => c.Id).ToArray()
            : Array.Empty<int>();
        var enemyCells = enemies.Take(MaxTargetsPerSpell)
            .Select(f => (f.Cell, f.Id)).ToArray();
        // Keep only the current deterministic winner. Do not allocate one
        // full plan/result tuple per legal candidate or sort that list later.
        VerifiedTacticalSequence.Plan? bestPlan = null;
        PlannerAction? bestInstruction = null;
        int bestStateId = 0;
        bool? bestStatePresent = null;
        (int Ap, int NegativeSteps, int EnemyDistance, int ActorDistance,
            int SpellId, int Cell, long Target) bestScore = default;
        int checkedCandidates = 0;
        foreach (var spell in descriptors)
        {
            if (!TryObservedClassRole(now, self, spell, availableStateSources,
                facts.VerifiedClassStateIds,
                out var kind, out var stateId, out var stateSource,
                out var expectedStatePresent))
                continue;
            var targets = kind switch
            {
                ClassMechanics.ComboKind.XelorTelefrag or ClassMechanics.ComboKind.SacrierSuffering
                    => enemyCells,
                ClassMechanics.ComboKind.EcaflipCard
                    => new[] { (now.ActorCell, now.ActorId) },
                ClassMechanics.ComboKind.EliotropeRedirect
                    => entryCells.Select(c => (c, 0L)).ToArray(),
                _ => freeCells.Select(c => (c, 0L)).ToArray()
            };
            foreach (var (cell, targetActor) in targets)
            {
                if (++checkedCandidates > facts.MaxCandidateChecks)
                    return No("bounded tactical decision work budget exhausted", checkedCandidates);
                var evidence = new ClassMechanics.ComboEvidence(
                    TargetId: enemies[0].Id, SourceSpell: spell,
                    StateSourceSpell: stateSource, StateId: stateId,
                    ClientVersion: facts.ClientVersion, SourceBreedId: now.BreedId,
                    StateSourceBreedId: stateSource is not null ? now.BreedId : 0,
                    ObservedLearnedGrades: learned);
                var action = new VerifiedTacticalSequence.Action(spell, cell, targetActor);
                var proposed = VerifiedTacticalSequence.PrepareIndexed(kind,
                    facts.Frame, evidence, now, new[] { action }, boardIndex, opt);
                if (proposed.Value is not { } plan) continue;
                // This is the real admission path called by LaunchService's
                // tactical advisor. Two actions are admitted only if the
                // class-specific join below exists in the actual grade book.
                // It NEVER bypasses Sequence.Prepare/Next or Inspect's ACK
                // barrier, and the second step is only ever advisory.
                if (opt.MaxSteps >= 2 && TryObservedContinuation(kind, now,
                    descriptors, enemies, action, evidence, out var nextAction,
                    out var extendedEvidence))
                {
                    if (++checkedCandidates > facts.MaxCandidateChecks)
                        return No("bounded tactical continuation work exhausted",
                            checkedCandidates);
                    var pair = VerifiedTacticalSequence.PrepareIndexed(kind,
                        facts.Frame, extendedEvidence, now,
                        new[] { action, nextAction }, boardIndex, opt);
                    if (pair.Value is { } observedPair)
                        plan = observedPair;
                }
                var step = VerifiedTacticalSequence.NextIndexed(plan, now,
                    Array.Empty<ClassMechanics.ComboObservation>(), boardIndex);
                if (!step.Ready || step.Stage != 0
                    || step.Instruction is not { Kind: PlannerActionKind.Cast } instruction)
                    continue;
                var score = (instruction.Ap, NegativeSteps: -plan.Actions.Length,
                    EnemyDistance: enemies.Min(e => MapPathfinder.CellDistance(cell, e.Cell)),
                    ActorDistance: MapPathfinder.CellDistance(now.ActorCell, cell),
                    instruction.SpellId, instruction.Cell, instruction.Target);
                if (bestPlan is not null && score.CompareTo(bestScore) >= 0) continue;
                bestPlan = plan;
                bestInstruction = instruction;
                bestScore = score;
                bestStateId = stateId;
                bestStatePresent = expectedStatePresent;
            }
        }

        if (bestPlan is null || bestInstruction is null)
            return No("no geometrically legal and grade-proven class decision",
                checkedCandidates);
        var executor = new VerifiedTacticalExecutor(bestPlan, limits, enabled,
            learned, bestStateId, bestStatePresent,
            bestPlan.Kind == ClassMechanics.ComboKind.EliotropeRedirect
                ? now.Portals : null);
        return new(executor, bestPlan.Kind, bestInstruction,
            checkedCandidates,
            bestPlan.Actions.Length == 2
                ? "bounded two-action sequence; second stage waits for correlated "
                    + "cast and class-effect observations; no transport authorization"
                : "one deterministic advisory step; neither transport nor class outcome certified");
    }

    /// <summary>
    /// A grade-proven continuation, not an inference of future server effects.
    /// Eliotrope's installed 14574 / 14579 pair requires portal entry/exit
    /// and enemy impact to match one already observed owned portal. No mark
    /// ID is invented for the proposed portal; an actual mark ID and server-
    /// ordered activation are required by VerifiedTacticalSequence. Xelor's
    /// installed 13285 / 13245 pair requires both actual swaps and the
    /// Telefrag state before exposing step two. Other classes can still
    /// admit first actions; generic damage or buff rows alone are NOT a
    /// verified class-to-class follow-up relationship.
    /// </summary>
    private static bool TryObservedContinuation(ClassMechanics.ComboKind kind,
        VerifiedTacticalSequence.Snapshot now,
        IReadOnlyList<SpellCastDescriptor> learned,
        IReadOnlyList<PlannerFighter> enemies,
        VerifiedTacticalSequence.Action first, ClassMechanics.ComboEvidence evidence,
        out VerifiedTacticalSequence.Action followup,
        out ClassMechanics.ComboEvidence joined)
    {
        followup = null!;
        joined = evidence;
        if (kind == ClassMechanics.ComboKind.XelorTelefrag)
        {
            // Client N01 pins 13285 Permutation/action8 and 13245
            // Gelure/action96, but NOT an automatic Telefrag. A successful
            // swap, both movements and the correct target state 251 must be
            // observed and correlated before Inspect can expose Gelure.
            if (first.Spell.SpellId != VerifiedXelorSwapSpell
                || first.TargetActorId == 0 || first.TargetActorId == now.ActorId
                || first.TargetCell == now.ActorCell
                || !enemies.Any(f => f.Id == first.TargetActorId
                    && f.Cell == first.TargetCell && f.CanTarget))
                return false;
            var freeze = learned.SingleOrDefault(s =>
                s.SpellId == VerifiedXelorFreezeSpell && !s.Uncertain
                && s.Ap > 0 && (s.Targets & SpellTargets.Enemy) != 0
                && s.Effects.Any(e => e.Kind == SpellEffectKind.Damage
                    && e.ActionId == 96 && Immediate(e)
                    && (e.Targets & SpellTargets.Enemy) != 0));
            if (freeze is null || now.Ap is not int budget
                || (long)first.Spell.Ap + freeze.Ap > budget)
                return false;
            followup = new VerifiedTacticalSequence.Action(
                freeze, now.ActorCell, first.TargetActorId);
            joined = evidence with
            {
                TargetId = first.TargetActorId,
                TargetCell = first.TargetCell,
                FirstCell = now.ActorCell,
                SecondCell = first.TargetCell,
                StateId = ClassMechanics.StateTelefrag,
                TargetIsEnemy = true,
                FollowupSpell = freeze,
                FollowupBreedId = now.BreedId
            };
            return true;
        }
        if (kind != ClassMechanics.ComboKind.EliotropeRedirect
            || first.Spell.SpellId != VerifiedEliotropePortalSpell
            || first.TargetActorId != 0
            || now.Portals is null
            || now.MarkedCells is null
            || now.MarkedCells.Contains(first.TargetCell))
            return false;
        var owned = now.Portals.Where(p => p.Active && p.OwnerId == now.ActorId)
            .ToArray();
        if (owned.Length != 1 || owned[0].Id <= 0
            || owned[0].Cell == first.TargetCell
            || now.Portals.Any(p => p.Id <= 0
                || (uint)p.Cell >= MapPathfinder.CellCount))
            return false;
        var ray = learned.SingleOrDefault(s =>
            s.SpellId == VerifiedEliotropeRaySpell
            && s is { Uncertain: false, Ap: > 0 }
            && s.Targets != SpellTargets.None
            && s.Effects.Any(e => e.ActionId == VerifiedEliotropeRayDamageAction
                && e.Kind == SpellEffectKind.Damage
                && Immediate(e) && (e.Targets & SpellTargets.Enemy) != 0));
        if (ray is null || now.Ap is not int ap
            || (long)first.Spell.Ap + ray.Ap > ap) return false;
        // A cell-only geometric projection is NOT an observed portal mark ID,
        // nor an assumption that the first cast will succeed. When this
        // prospective path becomes real, ConfirmedEffect re-checks actual
        // unique mark IDs, active ownership, projectile impact and target.
        var route = ClassMechanics.ProjectThroughPortals(now.ActorCell,
            first.TargetCell, new[] { first.TargetCell, owned[0].Cell });
        if (route is not { } projected) return false;
        var hit = enemies.SingleOrDefault(f => f.Cell == projected.Impact
            && f.CanTarget && f.Life > 0);
        if (hit is null) return false;
        followup = new VerifiedTacticalSequence.Action(ray, first.TargetCell);
        joined = evidence with
        {
            TargetId = hit.Id,
            TargetCell = hit.Cell,
            FollowupSpell = ray,
            FollowupBreedId = now.BreedId,
            EffectActionId = VerifiedEliotropeRayDamageAction
        };
        return true;
    }

    private static bool ValidLimits(Limits limits)
        => limits.MaxReviews is >= 1 and <= 4096
            && limits.MaxObservations is >= 1 and <= 4096
            && limits.MaxOrderAdvance is >= 1 and <= 4096;

    // Initial portal identities are a real prerequisite of an Eliotrope
    // continuation: an inactive/replaced exit cannot be recovered by the
    // geometry of another mark at the same cell. A newly placed portal may be
    // present only after a uniquely matched first cast, and is still NOT a
    // server-acknowledged effect until ConfirmedEffect checks its ordered mark.
    private bool OriginalPortalNetworkSurvives(
        VerifiedTacticalSequence.Snapshot current,
        IReadOnlyList<ClassMechanics.ComboObservation> observations)
    {
        if (_boundPortals.IsDefault) return true;
        if (current.Portals is null || current.Portals.Count > MaxObservedPortals)
            return false;
        foreach (var original in _boundPortals)
            if (current.Portals.Count(p => p is not null
                && p.Id == original.Id && p == original) != 1)
                return false;
        int additions = current.Portals.Count - _boundPortals.Length;
        if (additions == 0) return true;
        if (additions != 1) return false;
        var first = _plan.Actions[0];
        var added = current.Portals.Single(p => !_boundPortals.Any(b => b.Id == p.Id));
        // A new ID is never interchangeable with the portal placed by this
        // decision. A cast aimed at the selected cell cannot justify a mark
        // appearing elsewhere, under another owner or already inactive.
        if (added.Id <= 0 || !added.Active || added.OwnerId != current.ActorId
            || added.Cell != first.TargetCell
            || current.MarkedCells?.Contains(added.Cell) != true)
            return false;
        var casts = observations.Where(e => e is not null
            && e.Order > _plan.BaseServerOrder
            && e.Kind == ClassMechanics.ComboEventKind.Cast
            && e.Source == current.ActorId && e.SpellId == first.Spell.SpellId
            && e.Grade == first.Spell.Grade && e.ToCell == first.TargetCell
            && e.ServerConfirmed && e.Correlation > 0).ToArray();
        if (casts.Length != 1) return false;
        var cast = casts[0];
        var placements = observations.Where(e => e.Order > _plan.BaseServerOrder
            && e.Kind == ClassMechanics.ComboEventKind.MarkPlaced
            && e.MarkId == added.Id).ToArray();
        if (placements.Length > 1 || placements.Length == 1
            && (!placements[0].ServerConfirmed
                || placements[0].Source != current.ActorId
                || placements[0].ToCell != added.Cell
                || placements[0].ActionId != 1181
                || placements[0].Order <= cast.Order
                || placements[0].Correlation > 0
                    && placements[0].Correlation != cast.Correlation))
            return false;
        var activations = observations.Where(e => e.Order > _plan.BaseServerOrder
            && e.Kind == ClassMechanics.ComboEventKind.MarkActivated
            && e.MarkId == added.Id).ToArray();
        if (activations.Length > 1 || activations.Length == 1
            && (placements.Length != 1 || !activations[0].ServerConfirmed
                || activations[0].Source != current.ActorId
                || activations[0].Order <= placements[0].Order
                || activations[0].ToCell >= 0
                    && activations[0].ToCell != added.Cell))
            return false;
        return !observations.Any(e => e.Order > _plan.BaseServerOrder
            && e.MarkId == added.Id && e.ServerConfirmed
            && e.Kind is ClassMechanics.ComboEventKind.MarkRemoved
                or ClassMechanics.ComboEventKind.MarkDeactivated
                or ClassMechanics.ComboEventKind.PortalDeactivated);
    }

    // This check is only for historical, synthetic or independently certified
    // replay. Production calls InspectDiagnostic, which drops positive claims.
    // The new self-state must belong to the EXACT selected cast, not a later
    // unrelated buff or a caller's asserted current-state snapshot.
    private bool OwnCastGrantedObservedState(
        VerifiedTacticalSequence.Snapshot current,
        IReadOnlyList<ClassMechanics.ComboObservation> observations)
    {
        if (_plan.Kind != ClassMechanics.ComboKind.EcaflipCard
            || _boundStatePresent != false || _boundStateId <= 0)
            return false;
        var first = _plan.Actions[0];
        var casts = observations.Where(e => e.Order > _plan.BaseServerOrder
            && e.Kind == ClassMechanics.ComboEventKind.Cast
            && e.SpellId == first.Spell.SpellId).ToArray();
        if (casts.Length != 1) return false;
        var cast = casts[0];
        if (!cast.ServerConfirmed || cast.Correlation <= 0
            || cast.Source != current.ActorId || cast.Grade != first.Spell.Grade
            || cast.ToCell != first.TargetCell) return false;
        return observations.Count(e => e.Order > cast.Order
                && e.ServerConfirmed && e.Kind == ClassMechanics.ComboEventKind.StateAdded
                && e.Source == current.ActorId && e.Subject == current.ActorId
                && e.StateId == _boundStateId && e.Correlation == cast.Correlation) == 1
            && !observations.Any(e => e.Order > cast.Order && e.ServerConfirmed
                && e.Subject == current.ActorId && e.StateId == _boundStateId
                && (e.Kind == ClassMechanics.ComboEventKind.StateRemoved
                    || e.Kind == ClassMechanics.ComboEventKind.StateAdded
                        && (e.Source != current.ActorId
                            || e.Correlation != cast.Correlation)));
    }

    private static bool Immediate(SpellEffectDescriptor e)
        // The installed resolver treats "I" as immediate (not deferred).
        // Delayed, conditional or random outcomes must never turn into a
        // promised state, summon or positional effect at decision time.
        => !e.Conditional && e.Delay == 0 && e.Random == 0
            && (string.IsNullOrWhiteSpace(e.Triggers) || e.Triggers.Trim() == "I")
            && string.IsNullOrWhiteSpace(e.Condition);

    private static bool TryObservedClassRole(
        VerifiedTacticalSequence.Snapshot now, PlannerFighter self,
        SpellCastDescriptor spell, IReadOnlyList<SpellCastDescriptor> availableStateSources,
        IReadOnlySet<int>? verifiedClassStateIds,
        out ClassMechanics.ComboKind kind, out int stateId,
        out SpellCastDescriptor? stateSource, out bool? statePresent)
    {
        kind = default;
        stateId = 0;
        stateSource = null;
        statePresent = null;
        bool Effect(Func<SpellEffectDescriptor, bool> predicate)
            => spell.Effects.Any(e => Immediate(e) && predicate(e));
        switch (now.BreedId)
        {
            case ClassMechanics.RogueBreedId when spell.SpellId == 13444
                && Effect(e => e.ActionId == 1008 && e.Kind == SpellEffectKind.Summon
                    && e.DiceNum == ClassMechanics.VerifiedExplobombTemplate):
                kind = ClassMechanics.ComboKind.RogueWall;
                return true;
            case ClassMechanics.EliotropeBreedId when now.Portals is not null
                && now.Portals.Count(p => p.Active && p.OwnerId == now.ActorId) < 4
                && Effect(e => e.ActionId == 1181 && e.PlacesGroundMark):
                kind = ClassMechanics.ComboKind.EliotropeRedirect;
                return true;
            case ClassMechanics.XelorBreedId when (spell.Targets & SpellTargets.Enemy) != 0
                && Effect(e => e.ActionId == 8 && e.Kind == SpellEffectKind.Swap):
                kind = ClassMechanics.ComboKind.XelorTelefrag;
                return true;
            case ClassMechanics.FecaBreedId when Effect(e =>
                e.PlacesGroundMark && e.ActionId != 1181):
                kind = ClassMechanics.ComboKind.FecaGlyph;
                return true;
            case ClassMechanics.EcaflipBreedId when self.StatesKnown == true
                && self.States is not null && (spell.Targets & SpellTargets.Self) != 0:
            {
                int[] possible = spell.Effects.Where(e => Immediate(e) && e.ActionId == 950)
                    .Select(e => e.Value != 0 ? e.Value : e.Min)
                    .Where(id => id > 0 && verifiedClassStateIds?.Contains(id) == true
                        && !self.States.Contains(id))
                    .Distinct().OrderBy(id => id).ToArray();
                if (possible.Length != 1) return false;
                kind = ClassMechanics.ComboKind.EcaflipCard;
                stateId = possible[0];
                statePresent = false; // observed absence, NOT a predicted grant
                return true;
            }
            case ClassMechanics.SacrierBreedId when self.StatesKnown == true
                && self.States is not null && (spell.Targets & SpellTargets.Enemy) != 0
                && Effect(e => e.Kind is SpellEffectKind.Pull or SpellEffectKind.Swap):
            {
                var observed = availableStateSources.SelectMany(d => d.Effects
                        .Where(e => Immediate(e) && e.ActionId == 950)
                        .Select(e => (Descriptor: d, Id: e.Value != 0 ? e.Value : e.Min)))
                    .Where(p => p.Id > 0 && self.States.Contains(p.Id))
                    .Where(p => verifiedClassStateIds?.Contains(p.Id) == true)
                    .OrderBy(p => p.Id).ThenBy(p => p.Descriptor.SpellId).ToArray();
                if (observed.Length == 0) return false;
                kind = ClassMechanics.ComboKind.SacrierSuffering;
                stateId = observed[0].Id;
                stateSource = observed[0].Descriptor;
                statePresent = true;
                return true;
            }
            default: return false;
        }
    }

    /// <summary>
    /// Construction always reuses the class/catalog/learned-grade/AP/MP/LoS
    /// admission of VerifiedTacticalSequence.Prepare. No public constructor
    /// accepts an arbitrary supposedly preverified Plan.
    /// </summary>
    public static (VerifiedTacticalExecutor? Executor, string Reason) Prepare(
        ClassMechanics.ComboKind kind, ClassMechanics.ComboFrame frame,
        ClassMechanics.ComboEvidence evidence, VerifiedTacticalSequence.Snapshot snapshot,
        IReadOnlyList<VerifiedTacticalSequence.Action> proposed,
        VerifiedTacticalSequence.Options? sequenceOptions = null,
        Limits? executorLimits = null, bool enabled = false)
    {
        var limits = executorLimits ?? new Limits();
        if (!ValidLimits(limits))
            return (null, "invalid bounded execution limits");
        var planned = VerifiedTacticalSequence.Prepare(kind, frame,
            evidence, snapshot, proposed, sequenceOptions);
        return planned.Value is { } plan
            ? (new VerifiedTacticalExecutor(plan, limits, enabled),
                "advisory-only; no verified transport identity or dispatch authority")
            : (null, planned.Reason);
    }

    /// <summary>
    /// Changes only the advisory projection. After a historically confirmed
    /// stage, opting out irreversibly revokes the old sequence: opting back in
    /// requires fresh PrepareObservedDecision rather than resurrecting an
    /// earlier action/effect replay. Before any receipt, a toggle remains an
    /// ordinary disabled advisory preference. No toggle permits dispatch.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (!enabled && _enabled && _confirmedStages > 0)
                _expired = true;
            _enabled = enabled;
        }
    }

    /// <summary>
    /// Production ingress currently lacks a verified connection generation.
    /// This adapter intentionally drops any caller-provided ServerConfirmed
    /// flag and does not infer the absence of an in-flight external action.
    /// No amount of mark activity can unlock the next cast through this path.
    /// </summary>
    public Review InspectDiagnostic(VerifiedTacticalSequence.Snapshot current,
        IReadOnlyList<ClassMechanics.ComboObservation> observations,
        bool streamTruncated = false)
    {
        lock (_gate)
        {
            // Inspect can be used for synthetic/authoritative historical
            // replay. A later switch to the diagnostic-only ingress is a
            // STRICT trust downgrade: historical replay confirmation must
            // not survive when the current source has no transport binding.
            if (_confirmedStages > 0)
            {
                _expired = true;
                return new Review(Phase.Expired, _confirmedStages, null,
                    "diagnostic ingress cannot retain replay-only cast confirmation",
                    _lastObservedOrder);
            }
            if (current is null || observations is null)
                return Inspect(current!, observations!, streamTruncated: true);
            if (observations.Any(e => e is null))
                return Inspect(current, Array.Empty<ClassMechanics.ComboObservation>(),
                    streamTruncated: true);
            // The live ingress already supplies ServerConfirmed=false. Avoid a
            // clone per UI refresh in that dominant case while keeping a defensive
            // downgrade if another caller supplies positive-looking diagnostics.
            var untrusted = observations.Any(e => e.ServerConfirmed)
                ? observations.Select(e => e with { ServerConfirmed = false }).ToArray()
                : observations;
            // The lock remains held over Inspect (Monitor is reentrant), so
            // no concurrent replay can promote _confirmedStages between the
            // downgrade and the review.
            return Inspect(current with { PendingAction = null }, untrusted, streamTruncated);
        }
    }

    public Review Inspect(VerifiedTacticalSequence.Snapshot current,
        IReadOnlyList<ClassMechanics.ComboObservation> observations,
        bool streamTruncated = false)
    {
        lock (_gate)
        {
            Review Block(Phase phase, string reason)
                => new(phase, _confirmedStages, null, reason, _lastObservedOrder);

            Review Expire(string why)
            {
                _expired = true;
                return Block(Phase.Expired, why);
            }

            if (_expired) return Block(Phase.Expired, "plan already expired");
            if (!_enabled) return Block(Phase.Disabled, "tactical advisory disabled");
            if (++_reviews > _limits.MaxReviews)
                return Expire("bounded advisory review count exhausted");
            if (current is null || observations is null
                || current.FightId != _plan.Frame.FightId
                || current.Generation != _plan.Frame.Generation
                || current.MapGeneration != _plan.MapGeneration
                || current.Turn != _plan.Turn || current.ActorId != _plan.Frame.ActorId
                || current.BreedId != _plan.Frame.BreedId
                || current.Ap is not >= 0 || current.Mp is not >= 0
                || current.LastServerOrder < _lastObservedOrder)
                return Expire("stale fight/turn/map/actor or AP/MP/server order");
            if (current.Fighters is null || current.Fighters.Count is < 2 or > MaxObservedFighters
                || current.Fighters.Any(f => f is null || f.Id == 0
                    || (uint)f.Cell >= MapPathfinder.CellCount)
                || current.Fighters.Select(f => f.Id).Distinct().Count()
                    != current.Fighters.Count
                || current.Fighters.Select(f => f.Cell).Distinct().Count()
                    != current.Fighters.Count
                || current.Fighters.Count(f => f.Id == current.ActorId
                    && !f.Enemy && f.Life > 0 && f.Cell == current.ActorCell) != 1
                || current.Cells is null || current.Cells.Count != MapPathfinder.CellCount
                || current.Cells.Any(c => c is null || (uint)c.Id >= MapPathfinder.CellCount)
                || current.Cells.Select(c => c.Id).Distinct().Count()
                    != MapPathfinder.CellCount)
                return Expire("incomplete or unauthenticated board or fighter identities");
            if (_boundLearnedGrades is not null
                && (current.KnownSpellGrades is null
                    || current.KnownSpellGrades.Count != _boundLearnedGrades.Count
                    || _boundLearnedGrades.Any(pair =>
                        !current.KnownSpellGrades.TryGetValue(pair.Key, out int grade)
                        || grade != pair.Value)))
                return Expire("observed learned spell grades changed or became unknown");
            if (_boundLearnedGrades is not null
                && _plan.Kind == ClassMechanics.ComboKind.EliotropeRedirect
                && (current.Portals is null
                    || current.Portals.Any(p => p is null || p.Id <= 0 || p.OwnerId == 0
                        || (uint)p.Cell >= MapPathfinder.CellCount)
                    || current.Portals.Select(p => p.Id).Distinct().Count()
                        != current.Portals.Count
                    || current.Portals.Count(p => p.Active
                        && p.OwnerId == current.ActorId) >= 4))
                return Expire("observed portal owner/network capacity changed");
            if (streamTruncated || observations.Count > _limits.MaxObservations
                || current.LastServerOrder < _plan.BaseServerOrder
                || current.LastServerOrder - _plan.BaseServerOrder > _limits.MaxOrderAdvance)
                return Expire("missing or unbounded observation history");

            // A high-water mark with a truncated/reordered stream cannot
            // resolve a pending action. Never sort or recover guessed events.
            if (observations.Any(e => e is null
                || e.FightId != _plan.Frame.FightId
                || e.Generation != _plan.Frame.Generation || e.Turn != _plan.Turn
                || e.Order <= 0 || e.Order > current.LastServerOrder)
                || observations.Zip(observations.Skip(1),
                    (a, b) => a.Order >= b.Order).Any(bad => bad)
                || (observations.Count == 0 ? _plan.BaseServerOrder
                    : observations[^1].Order) != current.LastServerOrder)
                return Expire("unattributed, reordered or incomplete observation history");

            // A same-order diagnostic refresh is not proof that an AP/MP,
            // actor-cell, LoS or occupancy change was acknowledged. This must
            // be checked BEFORE the unknown PendingAction barrier below:
            // InspectDiagnostic intentionally sets PendingAction=null, which
            // otherwise leaves an invalid proposed cast forever 'Pending'.
            // Next below is read-only geometry revalidation, never a send or
            // a replacement for a missing transport receipt.
            if (_boundLearnedGrades is not null && _confirmedStages == 0
                && observations.Count == 0 && current.LastServerOrder == _plan.BaseServerOrder)
            {
                if (current.ActorCell != _plan.InitialActorCell
                    || current.Ap != _plan.InitialAp || current.Mp != _plan.InitialMp)
                    return Expire("unattributed actor position or AP/MP change without server observations");
                var legality = VerifiedTacticalSequence.Next(_plan,
                    current with { PendingAction = false },
                    Array.Empty<ClassMechanics.ComboObservation>());
                if (!legality.Ready || legality.Stage != 0
                    || legality.Instruction is not { Kind: PlannerActionKind.Cast })
                    return Expire("initial tactical cast is no longer legal on observed board or LoS");
            }

            if (!OriginalPortalNetworkSurvives(current, observations))
                return Expire("original portal identity, owner, position or activation changed");
            if (_boundStateId > 0 && _boundStatePresent is bool shouldBePresent)
            {
                var self = current.Fighters?.FirstOrDefault(f => f.Id == current.ActorId);
                if (self is null || self.StatesKnown != true || self.States is null)
                    return Expire("observed class state became unknown");
                bool statePresent = self.States.Contains(_boundStateId);
                if (_observedClassStateGrant)
                {
                    if (!statePresent || !OwnCastGrantedObservedState(current, observations))
                        return Expire("previously observed class state was removed or invalidated");
                }
                else if (statePresent != shouldBePresent)
                {
                    if (!OwnCastGrantedObservedState(current, observations))
                        return Expire("class state changed without the selected correlated cast");
                    _observedClassStateGrant = true;
                }
            }

            if (_confirmedStages > 0)
            {
                var firstAction = _plan.Actions[0];
                var firstProof = observations.Where(e => e.Order > _plan.BaseServerOrder
                    && e.Kind == ClassMechanics.ComboEventKind.Cast
                    && e.SpellId == firstAction.Spell.SpellId).ToArray();
                if (firstProof.Length != 1 || !firstProof[0].ServerConfirmed
                    || firstProof[0].Source != current.ActorId
                    || firstProof[0].Grade != firstAction.Spell.Grade
                    || firstProof[0].ToCell != firstAction.TargetCell
                    || firstProof[0].Order != _firstConfirmedOrder
                    || firstProof[0].Correlation != _firstConfirmedCorrelation)
                    return Expire("confirmed first cast order or correlation changed");
                if (_confirmedStages > 1)
                {
                    var lastAction = _plan.Actions[1];
                    var lastProof = observations.Where(e => e.Order > _plan.BaseServerOrder
                        && e.Kind == ClassMechanics.ComboEventKind.Cast
                        && e.SpellId == lastAction.Spell.SpellId).ToArray();
                    if (lastProof.Length != 1 || !lastProof[0].ServerConfirmed
                        || lastProof[0].Source != current.ActorId
                        || lastProof[0].Grade != lastAction.Spell.Grade
                        || lastProof[0].ToCell != lastAction.TargetCell
                        || lastProof[0].Order != _secondConfirmedOrder
                        || lastProof[0].Correlation != _secondConfirmedCorrelation)
                        return Expire("confirmed follow-up cast provenance changed");
                }
                if (observations.Any(e => e.Order > _firstConfirmedOrder
                    && e.Kind == ClassMechanics.ComboEventKind.Cast
                    && e.Source == current.ActorId
                    && (_plan.Actions.Length == 1
                        || _confirmedStages > 1 && e.Order != _secondConfirmedOrder
                        || _confirmedStages == 1
                            && e.SpellId != _plan.Actions[1].Spell.SpellId)))
                    return Expire("unrelated or repeated actor cast changed confirmed sequence");
            }

            _lastObservedOrder = current.LastServerOrder;
            if (_finished)
            {
                // A previously replay-confirmed historical cast does not
                // certify the status of a NEW in-flight transport request.
                // Even a completed plan must stop reporting an actionable
                // 'confirmed' current cycle when pending state becomes unknown.
                if (current.PendingAction != false)
                    return Block(Phase.Pending,
                        "completed historical cast; newer transport pending or unknown");
                if (_plan.Actions.Length == 1)
                {
                    // A completed single-action replay is not a reusable cast
                    // admission. A changed/repeated cast row in later history
                    // must not retain a stale positive review indefinitely.
                    var only = _plan.Actions[0];
                    var casts = observations.Where(e => e.Order > _plan.BaseServerOrder
                        && e.Kind == ClassMechanics.ComboEventKind.Cast
                        && e.Source == current.ActorId).ToArray();
                    if (casts.Length != 1 || !casts[0].ServerConfirmed
                        || casts[0].Correlation <= 0
                        || casts[0].SpellId != only.Spell.SpellId
                        || casts[0].Grade != only.Spell.Grade
                        || casts[0].ToCell != only.TargetCell)
                        return Expire("single-cast replay changed, repeated or lost provenance");
                }
                else
                {
                    // A historical stage-two confirmation must retain the
                    // exact last-cast author, grade, target and correlation.
                    // HasCurrentFollowupProof checks the prerequisites before
                    // that order, not the identity of the second cast itself.
                    var followup = _plan.Actions[1];
                    var finalRows = observations.Where(e =>
                        e.Order > _plan.BaseServerOrder
                        && e.Kind == ClassMechanics.ComboEventKind.Cast
                        && e.SpellId == followup.Spell.SpellId).ToArray();
                    if (finalRows.Length != 1
                        || finalRows[0].Source != current.ActorId
                        || finalRows[0].Grade != followup.Spell.Grade
                        || finalRows[0].ToCell != followup.TargetCell
                        || !finalRows[0].ServerConfirmed
                        || finalRows[0].Correlation <= 0
                        || !VerifiedTacticalSequence.HasCurrentFollowupProof(
                            _plan, current, observations, finalRows[0].Order))
                        return Expire("completed class proof invalidated by later observations");
                }
                return Block(Phase.Confirmed, "last cast observed; plan finished");
            }

            // An inflight request reported by another component is pending,
            // not an ACK. When unknown, do not emit even advisory steps.
            if (current.PendingAction != false)
                return Block(Phase.Pending, "pending or unknown external action; no ACK");

            var recent = observations.Where(e => e.Order > _plan.BaseServerOrder).ToArray();
            var first = _plan.Actions[0];
            var firstRows = recent.Where(e => e.Kind == ClassMechanics.ComboEventKind.Cast
                && e.SpellId == first.Spell.SpellId).ToArray();
            if (firstRows.Length > 1)
                return Expire("ambiguous repeated first cast");
            if (firstRows.Length == 1 && (firstRows[0].Source != current.ActorId
                || firstRows[0].Grade != first.Spell.Grade
                || firstRows[0].ToCell != first.TargetCell))
                return Expire("cast identity, grade or target conflict");

            // Stage two only becomes a candidate after the existing ordered,
            // correlated class-proof guard succeeded. A later cast must never
            // be mistaken for another chance to re-run the first one.
            if (_confirmedStages == 1 && _plan.Actions.Length > 1)
            {
                if (firstRows.Length != 1 || !firstRows[0].ServerConfirmed
                    || firstRows[0].Correlation <= 0)
                    return Expire("previously confirmed first cast no longer present");
                if (!VerifiedTacticalSequence.HasCurrentFollowupProof(
                    _plan, current, observations, long.MaxValue))
                    return Expire("preceding class effect or mark was invalidated");
                var followup = _plan.Actions[1];
                var secondRows = recent.Where(e => e.Kind == ClassMechanics.ComboEventKind.Cast
                    && e.SpellId == followup.Spell.SpellId
                    && e.Order > firstRows[0].Order).ToArray();
                if (secondRows.Length > 1)
                    return Expire("ambiguous repeated follow-up cast");
                if (secondRows.Length == 1)
                {
                    var cast = secondRows[0];
                    if (cast.Source != current.ActorId || cast.Grade != followup.Spell.Grade
                        || cast.ToCell != followup.TargetCell)
                        return Expire("follow-up author/grade/target conflict");
                    if (!cast.ServerConfirmed || cast.Correlation <= 0)
                        return Block(Phase.Pending, "follow-up cast lacks correlated server ACK");
                    if (!VerifiedTacticalSequence.HasCurrentFollowupProof(_plan,
                        current, observations, cast.Order))
                        return Expire("class proof was absent or invalidated before confirmation");
                    _secondConfirmedOrder = cast.Order;
                    _secondConfirmedCorrelation = cast.Correlation;
                    _confirmedStages = 2;
                    _finished = true;
                    return Block(Phase.Confirmed, "observed second cast with positive correlation");
                }
            }

            var evaluation = VerifiedTacticalSequence.Next(_plan, current, observations);
            if (evaluation.Ready && evaluation.Instruction is { } advisory)
            {
                if (evaluation.Stage < _confirmedStages)
                    return Expire("observed tactical stage moved backwards");
                if (evaluation.Stage > _confirmedStages)
                {
                    // Next confirms stage one only with a unique cast and an
                    // independently observed class effect. Bind its identity
                    // BEFORE exposing the advisory follow-up.
                    if (evaluation.Stage != 1 || firstRows.Length != 1
                        || !firstRows[0].ServerConfirmed
                        || firstRows[0].Correlation <= 0)
                        return Expire("stage advanced without unique server cast proof");
                    _firstConfirmedOrder = firstRows[0].Order;
                    _firstConfirmedCorrelation = firstRows[0].Correlation;
                    _confirmedStages = evaluation.Stage;
                    return new Review(Phase.Confirmed, _confirmedStages, advisory,
                        "preceding cast and class effect observed; next step advisory only",
                        _lastObservedOrder);
                }
                return new Review(_confirmedStages > 0 ? Phase.Confirmed : Phase.Advisory,
                    _confirmedStages, advisory,
                    "one bounded candidate; dispatch prohibited without transport provenance",
                    _lastObservedOrder);
            }

            // A decision selected from an observed full-board grade book must
            // not remain an inert but apparently reusable 'Pending' candidate
            // when no cast was attempted and a later board/LoS/PA/PM update
            // proves its original proposed action is no longer legal. Pending
            // is reserved for unknown transport, absent effects and unconfirmed
            // server activity; changed geometry is an irreversible expiration.
            if (_boundLearnedGrades is not null && firstRows.Length == 0
                && current.PendingAction == false
                && !recent.Any(e => e.Kind == ClassMechanics.ComboEventKind.Cast
                    && e.Source == current.ActorId))
                return Expire("observed tactical action no longer legal on current board");

            if (firstRows.Length == 1)
            {
                var cast = firstRows[0];
                if (!cast.ServerConfirmed || cast.Correlation <= 0)
                    return Block(Phase.Pending, "first cast lacks correlated server ACK");
                if (_plan.Actions.Length == 1)
                {
                    _firstConfirmedOrder = cast.Order;
                    _firstConfirmedCorrelation = cast.Correlation;
                    _confirmedStages = 1;
                    _finished = true;
                    return Block(Phase.Confirmed, "single server-observed cast; no invented class effect");
                }
                return Block(Phase.Pending, evaluation.Reason);
            }
            if (recent.Any(e => e.Kind == ClassMechanics.ComboEventKind.Cast
                && e.Source == current.ActorId))
                return Expire("an unrelated cast altered the turn");
            return Block(Phase.Pending, evaluation.Reason);
        }
    }
}
