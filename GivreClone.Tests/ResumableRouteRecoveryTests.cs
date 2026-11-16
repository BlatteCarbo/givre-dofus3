using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public sealed class ResumableRouteRecoveryTests
{
    private static readonly ResumableRouteMap Map = new(90703360, 1);
    private static readonly ResumableRouteMap Reconnected = new(90703360, 2);
    private const int Source = 240;
    private const int Target = 320;
    private static bool All(int _) => true;

    private static ResumableRoute Start(ResumableRoutePolicy? policy = null)
        => ResumableRoute.Start(Map, Source, Target, All, policy: policy);

    [Fact]
    public void Initial_plan_has_exact_Astar_cells_and_wire_encoding()
    {
        var state = Start();
        Assert.Equal(ResumableRoutePhase.Ready, state.Phase);
        Assert.True(state.CanDispatch);
        Assert.Equal(Source, state.Checkpoint);
        Assert.Equal(Target, state.Destination);
        Assert.Equal(MapPathfinder.PathCells(Source, Target, All), state.Cells);
        Assert.Equal(MapPathfinder.KeyMovements(Source, Target, All), state.KeyCells);
        Assert.True(MapPathfinder.IsMovementPathLegal(state.Cells, All));
        Assert.False(MapPathfinder.IsMovementPathLegal(new[] { 240, 320 }, All));
        Assert.False(MapPathfinder.IsMovementPathLegal(new[] { 240, -1 }, All));
        Assert.False(MapPathfinder.IsMovementPathLegal(new[] { 240, 240 }, All));
    }

    [Fact]
    public void Pending_dispatch_is_fifo_and_unverified_observations_do_not_advance_position()
    {
        var ready = Start();
        var pending = ready.BeginDispatch(Map, 1001);
        Assert.Equal(ResumableRoutePhase.InFlight, pending.Phase);
        Assert.False(pending.CanDispatch);
        Assert.Equal(1001L, pending.PendingSequence);
        Assert.Equal(1, pending.DispatchCount);
        Assert.Same(pending, pending.BeginDispatch(Map, 1002));
        Assert.Same(pending, pending.Confirmed(Map, 1001, Target, false, All)); // echos non conclusifs
        Assert.Same(pending, pending.Confirmed(Map, 1002, Target, true, All));  // ordre/seq errones
        Assert.Same(pending, pending.Confirmed(Reconnected, 1001, Target, true, All)); // ancienne carte/generation
        Assert.Same(pending, pending.Refused(Map, 1002, Source, true, All));
        Assert.Equal(Source, pending.Checkpoint);
        Assert.Equal(MapPathfinder.KeyMovements(Source, Target, All), pending.KeyCells);

        var completed = pending.Confirmed(Map, 1001, Target, true, All);
        Assert.Equal(ResumableRoutePhase.Completed, completed.Phase);
        Assert.False(completed.CanDispatch);
        Assert.Empty(completed.KeyCells);
        Assert.Same(completed, completed.BeginDispatch(Map, 1003));
    }

    [Fact]
    public void BeginDispatch_rejects_previous_generation_even_before_transport_submission()
    {
        var route = Start();
        var invalidated = route.BeginDispatch(Reconnected, 1);
        Assert.Equal(ResumableRoutePhase.Invalidated, invalidated.Phase);
        Assert.Equal(ResumableRouteReason.GenerationChanged, invalidated.Reason);
        Assert.Empty(invalidated.KeyCells);
        Assert.False(invalidated.CanDispatch);
        Assert.Equal(ResumableRoutePhase.Ready, route.Phase); // original immutable
    }

    [Fact]
    public void Confirmed_mid_route_checkpoint_preserves_exact_original_suffix()
    {
        var initial = Start();
        Assert.True(initial.Cells.Count > 3);
        int middleIndex = initial.Cells.Count / 2;
        int observed = initial.Cells[middleIndex];
        var pending = initial.BeginDispatch(Map, 7);
        var resumed = pending.Confirmed(Map, 7, observed, true, All);
        var originalSuffix = initial.Cells.Skip(middleIndex).ToArray();
        Assert.Equal(ResumableRoutePhase.Ready, resumed.Phase);
        Assert.Equal(observed, resumed.Checkpoint);
        Assert.Equal(0, resumed.StallCount);
        Assert.Equal(0, resumed.ReplanCount);
        Assert.Equal(originalSuffix, resumed.Cells);
        Assert.Equal(MapPathfinder.Compress(originalSuffix.ToList()), resumed.KeyCells);
        Assert.Same(resumed, resumed.BeginDispatch(Map, 7)); // sequence ancienne interdite
        var pending2 = resumed.BeginDispatch(Map, 8);
        Assert.Equal(ResumableRoutePhase.InFlight, pending2.Phase);
        Assert.Same(pending2, pending2.Confirmed(Map, 7, Target, true, All)); // ACK du move precedent
        Assert.Equal(ResumableRoutePhase.Completed, pending2.Confirmed(Map, 8, Target, true, All).Phase);
    }

    [Fact]
    public void Refusal_without_observed_cell_waits_and_can_reanchor_only_with_same_correlated_sequence()
    {
        var pending = Start().BeginDispatch(Map, 12);
        var waiting = pending.Refused(Map, 12, null, false, All);
        Assert.Equal(ResumableRoutePhase.AwaitingPosition, waiting.Phase);
        Assert.Equal(ResumableRouteReason.PositionUnconfirmed, waiting.Reason);
        Assert.Empty(waiting.KeyCells);
        Assert.False(waiting.CanDispatch);
        Assert.Same(waiting, waiting.BeginDispatch(Map, 13));
        Assert.Same(waiting, waiting.ReanchorAfterRefusal(Map, 13, Source, true, All));
        Assert.Same(waiting, waiting.ReanchorAfterRefusal(Map, 12, Source, false, All));
        Assert.Same(waiting, waiting.ReanchorAfterRefusal(Reconnected, 12, Source, true, All));

        var resumed = waiting.ReanchorAfterRefusal(Map, 12, Source, true, All);
        Assert.Equal(ResumableRoutePhase.Ready, resumed.Phase);
        Assert.Equal(1, resumed.StallCount);
        Assert.Equal(Source, resumed.Checkpoint);
        Assert.Equal(MapPathfinder.KeyMovements(Source, Target, All), resumed.KeyCells);
    }

    [Fact]
    public void Refusal_with_authoritative_partial_progress_reuses_the_remaining_original_route()
    {
        var original = Start();
        var pending = original.BeginDispatch(Map, 17);
        int index = original.Cells.Count / 2;
        var resumed = pending.Refused(Map, 17, original.Cells[index], true, All);
        Assert.Equal(ResumableRoutePhase.Ready, resumed.Phase);
        Assert.Equal(original.Cells.Skip(index), resumed.Cells);
        Assert.Equal(MapPathfinder.Compress(original.Cells.Skip(index).ToList()), resumed.KeyCells);
        Assert.Equal(0, resumed.StallCount);
        Assert.Equal(1, resumed.DispatchCount);
        Assert.False(pending.CanDispatch); // old snapshot still in flight; caller publishes one new state
    }

    [Fact]
    public void Off_route_or_invalid_confirmed_checkpoint_invalidates_instead_of_guessing()
    {
        var pending = Start().BeginDispatch(Map, 14);
        int offPath = Enumerable.Range(0, MapPathfinder.CellCount)
            .First(c => !pending.Cells.Contains(c));
        var invalid = pending.Confirmed(Map, 14, offPath, true, All);
        Assert.Equal(ResumableRoutePhase.Invalidated, invalid.Phase);
        Assert.Equal(ResumableRouteReason.OffRouteCheckpoint, invalid.Reason);
        Assert.False(invalid.CanDispatch);
        Assert.Empty(invalid.KeyCells);
        Assert.Equal(ResumableRouteReason.InvalidCheckpoint,
            pending.Confirmed(Map, 14, int.MaxValue, true, All).Reason);
    }

    [Fact]
    public void Reconnect_and_map_change_invalidate_even_if_cell_and_map_id_look_identical()
    {
        var pending = Start().BeginDispatch(Map, 15);
        var stale = pending.Refresh(Reconnected, All);
        Assert.Equal(ResumableRoutePhase.Invalidated, stale.Phase);
        Assert.Equal(ResumableRouteReason.GenerationChanged, stale.Reason);
        Assert.False(stale.CanDispatch);
        Assert.Same(stale, stale.Confirmed(Map, 15, Target, true, All));

        var newSession = ResumableRoute.Start(Reconnected, Source, Target, All);
        Assert.Equal(ResumableRoutePhase.Ready, newSession.Phase);
        Assert.Same(newSession, newSession.Confirmed(Map, 15, Target, true, All));
        Assert.Equal(ResumableRoutePhase.Invalidated,
            newSession.Refresh(new ResumableRouteMap(Map.MapId + 1, 2), All).Phase);
    }

    [Fact]
    public void Changed_obstacles_invalidate_cached_suffix_and_replan_from_observed_checkpoint()
    {
        var initial = Start();
        var walk = Enumerable.Repeat(true, MapPathfinder.CellCount).ToArray();
        bool Walk(int cell) => walk[cell];
        int blocked = initial.Cells[initial.Cells.Count / 2];
        walk[blocked] = false;
        var refreshed = initial.Refresh(Map, Walk);
        Assert.Equal(ResumableRoutePhase.Ready, refreshed.Phase);
        Assert.Equal(1, refreshed.ReplanCount);
        Assert.DoesNotContain(blocked, refreshed.Cells);
        Assert.Equal(MapPathfinder.KeyMovements(Source, Target, Walk), refreshed.KeyCells);

        // L'obstacle apparait PENDANT le move : aucun nouveau send n'est autorise avant observation.
        var flight = initial.BeginDispatch(Map, 20);
        Assert.Same(flight, flight.Refresh(Map, Walk));
        Assert.Equal(ResumableRoutePhase.InFlight, flight.Phase);
        int checkpoint = initial.Cells[1];
        var resumed = flight.Confirmed(Map, 20, checkpoint, true, Walk);
        Assert.Equal(ResumableRoutePhase.Ready, resumed.Phase);
        Assert.Equal(checkpoint, resumed.Checkpoint);
        Assert.DoesNotContain(blocked, resumed.Cells);
        Assert.True(MapPathfinder.IsMovementPathLegal(resumed.Cells, Walk));
        Assert.Equal(MapPathfinder.Compress(resumed.Cells.ToList()), resumed.KeyCells);

        walk[Target] = false;
        var noPath = resumed.Refresh(Map, Walk);
        Assert.Equal(ResumableRoutePhase.Blocked, noPath.Phase);
        Assert.Equal(ResumableRouteReason.NoPath, noPath.Reason);
        Assert.False(noPath.CanDispatch);
        walk[Target] = true;
        Assert.Equal(ResumableRoutePhase.Ready, noPath.Refresh(Map, Walk).Phase);
    }

    [Fact]
    public void Both_blocked_corners_and_directional_sector_permissions_are_rejected()
    {
        var diagonal = MapPathfinder.PathCells(240, 241, All);
        Assert.Equal(new[] { 240, 241 }, diagonal);
        bool OnlyEndpoints(int c) => c is 240 or 241;
        Assert.False(MapPathfinder.IsMovementPathLegal(diagonal, OnlyEndpoints, true));
        Assert.False(MapPathfinder.IsMovementPathLegal(diagonal, All, allowDiag: false));
        Assert.False(MapPathfinder.IsMovementPathLegal(diagonal, All, true, (_, _) => false));

        var route = ResumableRoute.Start(Map, 240, 241, All);
        Assert.Equal(ResumableRoutePhase.Blocked, route.Refresh(Map, OnlyEndpoints).Phase);
        bool Directed(int a, int b) => a < b;
        var forward = ResumableRoute.Start(Map, 240, 241, All, stepAllowed: Directed);
        var backward = ResumableRoute.Start(Map, 241, 240, All, stepAllowed: Directed);
        Assert.Equal(ResumableRoutePhase.Ready, forward.Phase);
        Assert.Equal(ResumableRoutePhase.Blocked, backward.Phase);
    }

    [Fact]
    public void Local_dispatch_failure_rechecks_current_geometry_and_keeps_monotonic_sequence()
    {
        var flight = Start().BeginDispatch(Map, 41);
        var ready = flight.DispatchFailed(Map, 41, All);
        Assert.Equal(ResumableRoutePhase.Ready, ready.Phase);
        Assert.Equal(1, ready.DispatchCount);
        Assert.Same(ready, ready.BeginDispatch(Map, 41));
        Assert.Same(ready, ready.DispatchFailed(Map, 41, All));
        Assert.Equal(ResumableRoutePhase.InFlight, ready.BeginDispatch(Map, 42).Phase);
    }

    [Fact]
    public void Repeated_stalls_and_dispatch_budgets_cannot_loop_forever()
    {
        var policy = new ResumableRoutePolicy(MaxDispatches: 8, MaxStalls: 2);
        var first = Start(policy).BeginDispatch(Map, 1).Refused(Map, 1, Source, true, All);
        Assert.Equal(ResumableRoutePhase.Ready, first.Phase);
        Assert.Equal(1, first.StallCount);
        var second = first.BeginDispatch(Map, 2).Refused(Map, 2, Source, true, All);
        Assert.Equal(ResumableRoutePhase.Exhausted, second.Phase);
        Assert.Equal(ResumableRouteReason.StallBudget, second.Reason);
        Assert.Empty(second.KeyCells);

        var one = Start(new ResumableRoutePolicy(MaxDispatches: 1)).BeginDispatch(Map, 1);
        var partial = one.Confirmed(Map, 1, one.Cells[1], true, All);
        Assert.Equal(ResumableRoutePhase.Exhausted, partial.Phase);
        Assert.Equal(ResumableRouteReason.DispatchBudget, partial.Reason);
    }

    [Fact]
    public void Visit_budget_prevents_repeated_authoritative_reanchor_cycle()
    {
        var state = Start(new ResumableRoutePolicy(MaxVisitsPerCell: 1));
        int other = state.Cells[1];
        state = state.BeginDispatch(Map, 1).Refused(Map, 1, null, false, All)
            .ReanchorAfterRefusal(Map, 1, other, true, All);
        Assert.Equal(ResumableRoutePhase.Ready, state.Phase);
        state = state.BeginDispatch(Map, 2).Refused(Map, 2, null, false, All)
            .ReanchorAfterRefusal(Map, 2, Source, true, All);
        Assert.Equal(ResumableRoutePhase.Exhausted, state.Phase);
        Assert.Equal(ResumableRouteReason.VisitBudget, state.Reason);
    }

    [Fact]
    public void Replan_budget_caps_repeated_obstacle_revision()
    {
        var state = Start(new ResumableRoutePolicy(MaxReplans: 1));
        var open = Enumerable.Repeat(true, MapPathfinder.CellCount).ToArray();
        bool Walk(int c) => open[c];
        open[state.Cells[1]] = false;
        state = state.Refresh(Map, Walk);
        Assert.Equal(ResumableRoutePhase.Ready, state.Phase);
        Assert.Equal(1, state.ReplanCount);
        open[state.Cells[1]] = false;
        state = state.Refresh(Map, Walk);
        Assert.Equal(ResumableRoutePhase.Exhausted, state.Phase);
        Assert.Equal(ResumableRouteReason.ReplanBudget, state.Reason);
    }

    [Fact]
    public void Invalid_inputs_are_fail_closed_and_a_valid_already_arrived_route_is_complete()
    {
        var invalidMaps = new[] { new ResumableRouteMap(0, 1), new ResumableRouteMap(Map.MapId, 0) };
        foreach (var map in invalidMaps)
            Assert.Equal(ResumableRoutePhase.Invalidated, ResumableRoute.Start(map, Source, Target, All).Phase);
        Assert.Equal(ResumableRoutePhase.Invalidated, ResumableRoute.Start(Map, -1, Target, All).Phase);
        Assert.Equal(ResumableRoutePhase.Invalidated,
            ResumableRoute.Start(Map, Source, MapPathfinder.CellCount, All).Phase);
        Assert.Equal(ResumableRoutePhase.Invalidated,
            ResumableRoute.Start(Map, Source, Target, All, policy: new ResumableRoutePolicy(MaxDispatches: int.MaxValue)).Phase);
        var done = ResumableRoute.Start(Map, Source, Source, All);
        Assert.Equal(ResumableRoutePhase.Completed, done.Phase);
        Assert.Empty(done.KeyCells);
    }
}
