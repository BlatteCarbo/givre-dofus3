extern alias F06App;

using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;
using Xunit;
using ActivityRecoveryWorkflow = F06App::GivreClone.App.Services.ActivityRecoveryWorkflow;
using ActivityRecoverySnapshot = F06App::GivreClone.App.Services.ActivityRecoverySnapshot;
using ActivityRecoverySegment = F06App::GivreClone.App.Services.ActivityRecoverySegment;
using ActivityRecoveryKind = F06App::GivreClone.App.Services.ActivityRecoveryKind;
using ActivityRecoveryDecision = F06App::GivreClone.App.Services.ActivityRecoveryDecision;

namespace GivreClone.Tests;

/// <summary>Pure offline contract; no launch service, game, packets, socket or shared harness.</summary>
public sealed class ActivityRecoveryWorkflowTests
{
    private static readonly ResumableRouteMap Map = new(90703360, 19);
    private static readonly ActivityRecoverySegment Cell =
        new(ActivityRecoveryKind.Cell, 320);
    private static readonly ActivityRecoverySegment Harvest =
        new(ActivityRecoveryKind.Gather, 12, Cell: 241, ElementId: 12);
    private static readonly ActivityRecoverySegment Door =
        new(ActivityRecoveryKind.Interaction, 12, Cell: 241, ElementId: 12);
    private static bool WalkAll(int _) => true;

    private static ActivityRecoverySnapshot Observed(int source = 240) =>
        new(Map, Map.MapId, Map.MapId, source, true, false, false);

    private static ActivityRecoverySnapshot Resource(int stock = 5) =>
        Observed() with
        {
            ResourcePresent = true, ResourceAvailable = true, AlreadyInReach = true,
            StockGid = 2416, StockCount = stock
        };

    [Fact]
    public void Already_observed_cell_is_a_zero_dispatch_checkpoint()
    {
        var workflow = ActivityRecoveryWorkflow.Start(Map.MapId);
        var probe = workflow.Evaluate(Cell, Observed(320), WalkAll);
        Assert.Equal(ActivityRecoveryDecision.AlreadyObserved, probe.Decision);
        Assert.False(probe.CanAttempt);
        Assert.Same(workflow, workflow.Begin(probe, Observed(320), walkable: WalkAll));
        Assert.Equal(0, workflow.Attempts);
    }

    [Fact]
    public void Cell_preflight_uses_exact_astar_route_and_one_shot_fifo_lease()
    {
        var workflow = ActivityRecoveryWorkflow.Start(Map.MapId);
        var snapshot = Observed();
        var probe = workflow.Evaluate(Cell, snapshot, WalkAll);
        Assert.True(probe.CanAttempt);
        Assert.Equal(MapPathfinder.PathCells(240, 320, WalkAll), probe.AdvisoryPath!.Cells);
        Assert.Equal(MapPathfinder.KeyMovements(240, 320, WalkAll), probe.AdvisoryPath.KeyCells);
        Assert.Null(probe.AdvisoryPath.PendingSequence);

        var issued = workflow.Begin(probe, snapshot, walkable: WalkAll);
        Assert.NotSame(workflow, issued);
        Assert.Equal(1, issued.Attempts);
        Assert.True(issued.IsQuarantined);
        Assert.Null(issued.PendingMoveSequence); // sent != server ACK; no fabricated sequence
        Assert.Equal(ActivityRecoveryDecision.AwaitingCorrelatedAck,
            issued.Evaluate(Cell, Observed(320), WalkAll).Decision);
        Assert.Same(issued, issued.Begin(probe, snapshot, walkable: WalkAll));
        Assert.Same(issued, issued.ConfirmCorrelatedAck(1, Observed(320)));
        Assert.False(workflow.IsQuarantined); // source snapshot remains immutable
    }

    [Fact]
    public void Abort_timeout_or_refusal_without_correlated_ack_does_not_replay()
    {
        var snapshot = Observed();
        var workflow = ActivityRecoveryWorkflow.Start(Map.MapId);
        var pending = workflow.Begin(workflow.Evaluate(Cell, snapshot, WalkAll), snapshot,
            walkable: WalkAll);
        foreach (var after in new[]
        {
            snapshot, snapshot with { PositionConfirmed = false },
            snapshot with { Interrupted = true },
            snapshot with { Map = new ResumableRouteMap(Map.MapId, Map.Generation + 1) },
            snapshot with { Map = new ResumableRouteMap(Map.MapId + 1, Map.Generation + 1) }
        })
        {
            var current = pending.ObserveEffect(after);
            Assert.True(current.IsQuarantined);
            Assert.Equal(ActivityRecoveryDecision.AwaitingCorrelatedAck,
                current.Evaluate(Cell, after, WalkAll).Decision);
            Assert.Null(current.PendingMoveSequence);
        }
    }

    [Fact]
    public void Stale_preflight_or_changed_stock_cannot_reserve_a_send()
    {
        var workflow = ActivityRecoveryWorkflow.Start(Map.MapId);
        var snapshot = Resource();
        var probe = workflow.Evaluate(Harvest, snapshot);
        Assert.True(probe.CanAttempt);
        Assert.Same(workflow, workflow.Begin(probe, snapshot with { StockCount = 6 }));
        Assert.Same(workflow, workflow.Begin(probe,
            snapshot with { Map = new ResumableRouteMap(Map.MapId, Map.Generation + 1) }));
        Assert.Same(workflow, workflow.Begin(probe, snapshot with { ServerCell = 242 }));
        Assert.Same(workflow, workflow.Begin(probe, snapshot with { ResourceAvailable = false }));
        Assert.Equal(0, workflow.Attempts);
    }

    [Fact]
    public void Stock_increase_or_exhaustion_is_diagnostic_not_an_ack()
    {
        var workflow = ActivityRecoveryWorkflow.Start(Map.MapId);
        var before = Resource();
        var pending = workflow.Begin(workflow.Evaluate(Harvest, before), before);
        var after = before with { StockCount = 8, ResourceAvailable = false };
        var observed = pending.ObserveEffect(after);
        Assert.True(observed.ObservedEffectWithoutAck);
        Assert.True(observed.IsQuarantined);
        Assert.Same(observed, observed.ConfirmCorrelatedAck(77, after));
        Assert.Equal(ActivityRecoveryDecision.AwaitingCorrelatedAck,
            observed.Evaluate(Harvest, after).Decision);
        Assert.Equal(1, observed.Attempts);

        var unchanged = pending.ObserveEffect(before);
        Assert.False(unchanged.ObservedEffectWithoutAck);
        Assert.True(unchanged.IsQuarantined);
    }

    [Fact]
    public void Exhausted_resource_can_be_skipped_only_if_present_in_current_snapshot()
    {
        var workflow = ActivityRecoveryWorkflow.Start(Map.MapId);
        var exhausted = Resource() with { ResourceAvailable = false };
        Assert.Equal(ActivityRecoveryDecision.AlreadyObserved,
            workflow.Evaluate(Harvest, exhausted).Decision);
        Assert.Equal(ActivityRecoveryDecision.TargetUnavailable,
            workflow.Evaluate(Harvest, exhausted with { ResourcePresent = false }).Decision);
        Assert.Equal(ActivityRecoveryDecision.TargetUnavailable,
            workflow.Evaluate(Door, exhausted).Decision);
        Assert.Equal(ActivityRecoveryDecision.TargetUnavailable,
            workflow.Evaluate(Door, exhausted with { ResourcePresent = false }).Decision);
    }

    [Fact]
    public void Composite_interaction_movement_is_not_authorized_by_the_recovery_gate()
    {
        var workflow = ActivityRecoveryWorkflow.Start(Map.MapId);
        var tooFar = Resource() with { AlreadyInReach = false };
        Assert.Equal(ActivityRecoveryDecision.MovementRequired,
            workflow.Evaluate(Harvest, tooFar).Decision);
        Assert.Equal(ActivityRecoveryDecision.MovementRequired,
            workflow.Evaluate(Door, tooFar).Decision);
        Assert.Equal(ActivityRecoveryDecision.Ready,
            workflow.Evaluate(Door, Resource()).Decision);
        Assert.Equal(ActivityRecoveryDecision.InvalidSegment,
            workflow.Evaluate(new ActivityRecoverySegment(ActivityRecoveryKind.Gather,
                Target: 12, Cell: 241, ElementId: 13), Resource()).Decision);
    }

    [Fact]
    public void Interrupted_stale_geometry_unknown_position_or_wrong_map_fails_closed()
    {
        var workflow = ActivityRecoveryWorkflow.Start(Map.MapId);
        var current = Observed();
        Assert.Equal(ActivityRecoveryDecision.Interrupted,
            workflow.Evaluate(Cell, current with { Interrupted = true }, WalkAll).Decision);
        Assert.Equal(ActivityRecoveryDecision.Suspended,
            workflow.Evaluate(Cell, current with { Suspended = true }, WalkAll).Decision);
        Assert.Equal(ActivityRecoveryDecision.GeometryStale,
            workflow.Evaluate(Cell, current with { GeometryMap = Map.MapId + 1 }, WalkAll).Decision);
        Assert.Equal(ActivityRecoveryDecision.PositionUnconfirmed,
            workflow.Evaluate(Cell, current with { PositionConfirmed = false }, WalkAll).Decision);
        Assert.Equal(ActivityRecoveryDecision.PositionUnconfirmed,
            workflow.Evaluate(Cell, current with { PositionMap = Map.MapId + 1 }, WalkAll).Decision);
        Assert.Equal(ActivityRecoveryDecision.PositionUnconfirmed,
            workflow.Evaluate(Cell, current with { ServerCell = 560 }, WalkAll).Decision);
        Assert.Equal(ActivityRecoveryDecision.MapChanged,
            workflow.Evaluate(Cell, current with { Map = new(Map.MapId + 1, 20) }, WalkAll).Decision);
        Assert.Equal(ActivityRecoveryDecision.InvalidSegment,
            workflow.Evaluate(new ActivityRecoverySegment(ActivityRecoveryKind.Cell, -1),
                current, WalkAll).Decision);
    }

    [Fact]
    public void New_obstacles_and_directional_gates_are_rechecked_at_reservation_time()
    {
        var workflow = ActivityRecoveryWorkflow.Start(Map.MapId);
        var snapshot = Observed();
        var walk = Enumerable.Repeat(true, MapPathfinder.CellCount).ToArray();
        bool Walk(int c) => walk[c];
        var probe = workflow.Evaluate(Cell, snapshot, Walk);
        Assert.True(probe.CanAttempt);
        walk[probe.AdvisoryPath!.Cells[1]] = false;
        Assert.Same(workflow, workflow.Begin(probe, snapshot, walkable: Walk));
        walk[probe.AdvisoryPath.Cells[1]] = true;
        Assert.True(workflow.Begin(probe, snapshot, walkable: Walk).IsQuarantined);

        static bool OnlyEndpoints(int c) => c is 240 or 241;
        Assert.Equal(ActivityRecoveryDecision.PathBlocked,
            workflow.Evaluate(new ActivityRecoverySegment(ActivityRecoveryKind.Cell, 241),
                snapshot, OnlyEndpoints).Decision);
        Assert.Equal(ActivityRecoveryDecision.PathBlocked,
            workflow.Evaluate(new ActivityRecoverySegment(ActivityRecoveryKind.Cell, 241),
                snapshot, WalkAll, (_, _) => false).Decision);
    }

    [Fact]
    public void Only_matching_real_sequence_and_observed_target_can_release_a_movement()
    {
        var workflow = ActivityRecoveryWorkflow.Start(Map.MapId);
        var snapshot = Observed();
        var pending = workflow.Begin(workflow.Evaluate(Cell, snapshot, WalkAll),
            snapshot, realSequence: 925, walkable: WalkAll);
        Assert.True(pending.IsQuarantined);
        Assert.Equal(925L, pending.PendingMoveSequence);
        Assert.Same(pending, pending.ConfirmCorrelatedAck(924, Observed(320)));
        Assert.Same(pending, pending.ConfirmCorrelatedAck(925, Observed(241)));
        Assert.Same(pending, pending.ConfirmCorrelatedAck(925,
            Observed(320) with { Map = new(Map.MapId, Map.Generation + 1) }));
        var done = pending.ConfirmCorrelatedAck(925, Observed(320));
        Assert.False(done.IsQuarantined);
        Assert.Equal(1, done.CorrelatedCompletions);
        Assert.Equal(0, done.Attempts);
        Assert.Null(done.PendingMoveSequence);
    }

    [Fact]
    public void Map_transition_effect_alone_is_not_ack_and_wrong_map_is_rejected()
    {
        const long destination = 90703361;
        var workflow = ActivityRecoveryWorkflow.Start(Map.MapId);
        var sourceSnapshot = Observed();
        var segment = new ActivityRecoverySegment(ActivityRecoveryKind.MapTransition, destination);
        var pending = workflow.Begin(workflow.Evaluate(segment, sourceSnapshot), sourceSnapshot, realSequence: 1001);
        var switched = sourceSnapshot with
        {
            Map = new(destination, Map.Generation + 1),
            GeometryMap = destination, PositionMap = destination, ServerCell = 241
        };
        Assert.True(pending.ObserveEffect(switched).IsQuarantined);
        Assert.Same(pending, pending.ConfirmCorrelatedAck(1001,
            switched with { Map = new(destination + 1, Map.Generation + 1) }));
        Assert.Equal(1, pending.ConfirmCorrelatedAck(1001, switched).CorrelatedCompletions);
    }

    [Fact]
    public void Fixed_seed_paths_preserve_exact_encoding_across_interrupted_snapshots()
    {
        var rng = new Random(20261114);
        for (int grid = 0; grid < 6; grid++)
        {
            var open = Enumerable.Range(0, MapPathfinder.CellCount)
                .Select(_ => rng.Next(100) >= grid * 13).ToArray();
            bool Walk(int c) => open[c];
            for (int i = 0; i < 40; i++)
            {
                int source = rng.Next(MapPathfinder.CellCount);
                int target = rng.Next(MapPathfinder.CellCount);
                var workflow = ActivityRecoveryWorkflow.Start(Map.MapId);
                var snapshot = Observed(source);
                var segment = new ActivityRecoverySegment(ActivityRecoveryKind.Cell, target);
                var observed = workflow.Evaluate(segment, snapshot, Walk);
                var baseline = MapPathfinder.KeyMovements(source, target, Walk);
                if (source == target)
                {
                    Assert.Equal(ActivityRecoveryDecision.AlreadyObserved, observed.Decision);
                    continue;
                }
                if (baseline.Count == 0)
                {
                    Assert.Equal(ActivityRecoveryDecision.PathBlocked, observed.Decision);
                    continue;
                }
                Assert.True(observed.CanAttempt);
                Assert.Equal(baseline, observed.AdvisoryPath!.KeyCells);
                Assert.Equal(MapPathfinder.Compress(observed.AdvisoryPath.Cells.ToList()), baseline);
                Assert.Equal(ActivityRecoveryDecision.Interrupted,
                    workflow.Evaluate(segment, snapshot with { Interrupted = true }, Walk).Decision);
            }
        }
    }
}
