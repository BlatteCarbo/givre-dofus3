using System;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

/// <summary>
/// One-shot route/gather activity recovery. This is a pure state machine, never a packet sender.
/// The LuaHost must not infer a correlated ACK from a completed Task, a map change, a matching
/// cell, an iwi/itn, or an inventory difference. A duplicate action is worse than stopping.
/// </summary>
public enum ActivityRecoveryKind { Cell, Gather, Interaction, MapTransition }

public enum ActivityRecoveryDecision
{
    Ready, AlreadyObserved, AwaitingCorrelatedAck, InvalidSnapshot,
    Interrupted, Suspended, MapChanged, GeometryStale, PositionUnconfirmed,
    TargetUnavailable, MovementRequired, PathBlocked, InvalidSegment
}

public sealed record ActivityRecoverySegment(
    ActivityRecoveryKind Kind, long Target, int Cell = -1, int ElementId = 0);

/// <summary>
/// Atomic-as-available snapshot supplied by the LuaHost from the current world state.
/// ResourcePresent distinguishes 'known exhausted' from 'not decoded yet'.
/// Stock is optional diagnostic information; it never replaces an ACK.
/// </summary>
public readonly record struct ActivityRecoverySnapshot(
    ResumableRouteMap Map,
    long GeometryMap,
    long? PositionMap,
    int? ServerCell,
    bool PositionConfirmed,
    bool Interrupted,
    bool Suspended,
    bool ResourcePresent = false,
    bool ResourceAvailable = false,
    bool AlreadyInReach = false,
    int? StockGid = null,
    int? StockCount = null,
    // The interactive can retain its element id and cell while the server
    // republishes a different skill instance. Pre-send comparisons must not
    // mistake that replacement for the same gather/door target.
    int? ResourceTypeId = null,
    int? SkillInstanceUid = null,
    int? SkillId = null);

public readonly record struct ActivityRecoveryEvaluation(
    ActivityRecoveryDecision Decision, ActivityRecoveryWorkflow Workflow,
    ActivityRecoverySegment Segment, ActivityRecoverySnapshot Preflight,
    ResumableRoute? AdvisoryPath = null)
{
    public bool CanAttempt => Decision == ActivityRecoveryDecision.Ready;
}

public sealed record ActivityRecoveryWorkflow
{
    public long SourceMap { get; private init; }
    public int Attempts { get; private init; }
    public int CorrelatedCompletions { get; private init; }
    public ActivityRecoverySegment? PendingSegment { get; private init; }
    public ActivityRecoverySnapshot? AttemptSnapshot { get; private init; }
    public long? PendingMoveSequence { get; private init; }
    /// <summary>True for an on-disk marker, including incomplete/corrupt ones with no
    /// recoverable segment identity. No synthetic gameplay IDs are created.</summary>
    public bool DurableQuarantine { get; private init; }
    public bool IsQuarantined => DurableQuarantine || PendingSegment is not null;
    public bool ObservedEffectWithoutAck { get; private init; }

    private ActivityRecoveryWorkflow() { }

    public static ActivityRecoveryWorkflow Start(long mapId) => new() { SourceMap = mapId };

    /// <summary>
    /// Immutable, read-only UI reconstruction from the durable authority after restart.
    /// Never grants admission or clears a marker. An unreadable marker is still quarantined.
    /// </summary>
    public static ActivityRecoveryWorkflow FromDurableStatus(ActivityRecoveryDurableStatus status) =>
        new()
        {
            SourceMap = status.MapId,
            DurableQuarantine = status.IsQuarantined,
            PendingSegment = status.Segment,
            Attempts = status.State == ActivityRecoveryDurableState.Pending ? 1 : 0
        };

    /// <summary>
    /// Validate the current observation BEFORE any legacy action. Only one action for this
    /// workflow can be admitted; a later generation, obstacle or stock snapshot never resets
    /// the pending lease. Pure cell planning uses the same A* as the actual movement sender.
    /// </summary>
    public ActivityRecoveryEvaluation Evaluate(ActivityRecoverySegment segment,
        ActivityRecoverySnapshot snapshot, Func<int, bool>? walkable = null,
        Func<int, int, bool>? stepAllowed = null)
    {
        ActivityRecoveryEvaluation Deny(ActivityRecoveryDecision reason, ResumableRoute? path = null) =>
            new(reason, this, segment, snapshot, path);
        if (IsQuarantined) return Deny(ActivityRecoveryDecision.AwaitingCorrelatedAck);
        if (SourceMap <= 0 || !snapshot.Map.IsValid ||
            segment.Target <= 0 && segment.Kind != ActivityRecoveryKind.Cell ||
            segment.Kind == ActivityRecoveryKind.Cell &&
            (segment.Target < 0 || segment.Target >= MapPathfinder.CellCount))
            return Deny(ActivityRecoveryDecision.InvalidSegment);
        if (snapshot.Map.MapId != SourceMap) return Deny(ActivityRecoveryDecision.MapChanged);
        if (snapshot.Interrupted) return Deny(ActivityRecoveryDecision.Interrupted);
        if (snapshot.Suspended) return Deny(ActivityRecoveryDecision.Suspended);
        if (snapshot.GeometryMap != snapshot.Map.MapId) return Deny(ActivityRecoveryDecision.GeometryStale);
        if (!snapshot.PositionConfirmed || snapshot.PositionMap != snapshot.Map.MapId ||
            snapshot.ServerCell is not int from || (uint)from >= MapPathfinder.CellCount)
            return Deny(ActivityRecoveryDecision.PositionUnconfirmed);

        if (segment.Kind == ActivityRecoveryKind.Cell)
        {
            if (snapshot.ServerCell == segment.Target)
                return Deny(ActivityRecoveryDecision.AlreadyObserved);
            if (walkable is null) return Deny(ActivityRecoveryDecision.InvalidSnapshot);
            var route = ResumableRoute.Start(snapshot.Map, from, (int)segment.Target, walkable,
                allowDiagonal: true, stepAllowed: stepAllowed);
            return route.CanDispatch &&
                   MapPathfinder.IsMovementPathLegal(route.Cells, walkable, true, stepAllowed)
                ? new(ActivityRecoveryDecision.Ready, this, segment, snapshot, route)
                : Deny(ActivityRecoveryDecision.PathBlocked, route);
        }

        if (segment.Kind is ActivityRecoveryKind.Gather or ActivityRecoveryKind.Interaction)
        {
            if ((uint)segment.Cell >= MapPathfinder.CellCount || segment.ElementId <= 0 ||
                segment.Target != segment.ElementId)
                return Deny(ActivityRecoveryDecision.InvalidSegment);
            if (!snapshot.ResourcePresent)
                return Deny(ActivityRecoveryDecision.TargetUnavailable);
            if (!snapshot.ResourceAvailable)
                return Deny(segment.Kind == ActivityRecoveryKind.Gather
                    ? ActivityRecoveryDecision.AlreadyObserved : ActivityRecoveryDecision.TargetUnavailable);
            if (!snapshot.AlreadyInReach)
                return Deny(ActivityRecoveryDecision.MovementRequired);
            return new(ActivityRecoveryDecision.Ready, this, segment, snapshot);
        }

        if (segment.Kind == ActivityRecoveryKind.MapTransition)
            return new(ActivityRecoveryDecision.Ready, this, segment, snapshot);
        return Deny(ActivityRecoveryDecision.InvalidSegment);
    }

    /// <summary>
    /// Reserve BEFORE calling the existing gameplay primitive. If the call reports NotSent,
    /// timeout, cancellation or success-without-sequence, the reservation remains terminal.
    /// A real monotonic sender sequence MAY be passed, but never fabricated.
    /// </summary>
    public ActivityRecoveryWorkflow Begin(ActivityRecoveryEvaluation evaluation,
        ActivityRecoverySnapshot atDispatch, long? realSequence = null,
        Func<int, bool>? walkable = null, Func<int, int, bool>? stepAllowed = null)
    {
        if (!ReferenceEquals(evaluation.Workflow, this) || !evaluation.CanAttempt || IsQuarantined ||
            Attempts != 0 || evaluation.Segment.Target <= 0 && evaluation.Segment.Kind != ActivityRecoveryKind.Cell ||
            evaluation.Segment.Kind == ActivityRecoveryKind.Cell &&
                (evaluation.AdvisoryPath is null || atDispatch.Map != evaluation.AdvisoryPath.Map))
            return this;
        if (atDispatch != evaluation.Preflight ||
            atDispatch.Map.MapId != SourceMap || !atDispatch.Map.IsValid ||
            !atDispatch.PositionConfirmed || atDispatch.PositionMap != SourceMap ||
            atDispatch.GeometryMap != SourceMap || atDispatch.Interrupted || atDispatch.Suspended ||
            realSequence is <= 0 ||
            evaluation.Segment.Kind == ActivityRecoveryKind.Cell &&
                (evaluation.AdvisoryPath is null || walkable is null ||
                 !MapPathfinder.IsMovementPathLegal(evaluation.AdvisoryPath.Cells, walkable,
                     true, stepAllowed)))
            return this;
        return this with
        {
            Attempts = 1, PendingSegment = evaluation.Segment,
            AttemptSnapshot = atDispatch, PendingMoveSequence = realSequence
        };
    }

    /// <summary>
    /// Record externally observed effects for diagnostics only. A stock increase and the
    /// source element becoming unavailable are useful witnesses, NOT a sequence-bound ACK.
    /// </summary>
    public ActivityRecoveryWorkflow ObserveEffect(ActivityRecoverySnapshot snapshot)
    {
        if (PendingSegment is not { } segment || AttemptSnapshot is not { } before)
            return this;
        bool effect = segment.Kind switch
        {
            ActivityRecoveryKind.Cell => snapshot.Map.MapId == before.Map.MapId &&
                snapshot.PositionConfirmed && snapshot.PositionMap == before.Map.MapId &&
                snapshot.ServerCell == segment.Target,
            ActivityRecoveryKind.MapTransition => snapshot.Map.MapId == segment.Target,
            ActivityRecoveryKind.Gather =>
                snapshot.Map.MapId == before.Map.MapId &&
                (snapshot.ResourcePresent && !snapshot.ResourceAvailable ||
                 before.StockGid is > 0 && before.StockGid == snapshot.StockGid &&
                 before.StockCount is int previous && snapshot.StockCount is int current && current > previous),
            ActivityRecoveryKind.Interaction => snapshot.Map.MapId != before.Map.MapId ||
                snapshot.ResourcePresent && !snapshot.ResourceAvailable,
            _ => false
        };
        return effect ? this with { ObservedEffectWithoutAck = true } : this;
    }

    /// <summary>
    /// The only automatic release point is a sequence-bound ACK from the transport that
    /// issued the movement, plus fresh authoritative observation. No LuaHost caller currently
    /// has that data, so it MUST NOT call this with a synthetic sequence.
    /// </summary>
    public ActivityRecoveryWorkflow ConfirmCorrelatedAck(long actualSequence,
        ActivityRecoverySnapshot snapshot)
    {
        if (PendingMoveSequence is not > 0 || actualSequence != PendingMoveSequence ||
            AttemptSnapshot is not { } before || PendingSegment is not { } segment ||
            !snapshot.PositionConfirmed || snapshot.PositionMap != snapshot.Map.MapId)
            return this;
        if (segment.Kind == ActivityRecoveryKind.MapTransition)
        {
            if (snapshot.Map.MapId != segment.Target ||
                snapshot.Map.Generation <= before.Map.Generation) return this;
        }
        else if (snapshot.Map != before.Map)
            return this;
        if (segment.Kind == ActivityRecoveryKind.Cell && snapshot.ServerCell != segment.Target)
            return this;
        if (segment.Kind is ActivityRecoveryKind.Gather or ActivityRecoveryKind.Interaction &&
            !ObserveEffect(snapshot).ObservedEffectWithoutAck)
            return this;
        return this with { PendingSegment = null, AttemptSnapshot = null,
            Attempts = 0, CorrelatedCompletions = CorrelatedCompletions + 1,
            PendingMoveSequence = null, ObservedEffectWithoutAck = false };
    }
}
