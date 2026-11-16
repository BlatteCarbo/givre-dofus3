extern alias F06App;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GivreClone.Protocol;
using Xunit;
using Xunit.Abstractions;
using ResumableTravelWorkflow = F06App::GivreClone.App.Services.ResumableTravelWorkflow;
using ResumableTravelSnapshot = F06App::GivreClone.App.Services.ResumableTravelSnapshot;
using ResumableTravelProbeStatus = F06App::GivreClone.App.Services.ResumableTravelProbeStatus;

namespace GivreClone.Tests;

/// <summary>Offline only: never instantiates GivreLuaHost, LaunchService, game or sockets.</summary>
public sealed class ResumableTravelWorkflowTests(ITestOutputHelper output)
{
    private static readonly ResumableRouteMap Map = new(90703360, 19);
    private const int Source = 240;
    private const int Target = 320;
    private static bool All(int _) => true;

    private static ResumableTravelSnapshot Observed(int source = Source) =>
        new(Map, Map.MapId, Map.MapId, source, true, false, false);

    [Fact]
    public void Diagnostic_uses_exact_route_and_never_claims_dispatch_or_ack()
    {
        var workflow = ResumableTravelWorkflow.Start(Map, Target);
        var probe = workflow.Probe(Observed(), All);
        Assert.Equal(ResumableTravelProbeStatus.ReadyForLegacyDispatch, probe.Status);
        Assert.True(probe.CanAttempt);
        Assert.NotNull(probe.AdvisoryRoute);
        Assert.Equal(ResumableRoutePhase.Ready, probe.AdvisoryRoute!.Phase);
        Assert.Null(probe.AdvisoryRoute.PendingSequence);
        Assert.Equal(MapPathfinder.PathCells(Source, Target, All), probe.AdvisoryRoute.Cells);
        Assert.Equal(MapPathfinder.KeyMovements(Source, Target, All), probe.AdvisoryRoute.KeyCells);
        Assert.Equal(0, probe.AdvisoryRoute.DispatchCount);
        Assert.Equal(0, workflow.AttemptCount); // observation pure, aucune reservation
        Assert.Equal(1, workflow.NoteLegacyAttempt(probe).AttemptCount);
    }

    [Fact]
    public void Position_and_geometry_must_be_from_the_same_confirmed_map_generation()
    {
        var workflow = ResumableTravelWorkflow.Start(Map, Target);
        var wrongGeneration = Observed() with { Map = new(Map.MapId, Map.Generation + 1) };
        Assert.Equal(ResumableTravelProbeStatus.GenerationChanged, workflow.Probe(wrongGeneration, All).Status);
        Assert.Equal(ResumableTravelProbeStatus.GenerationChanged,
            workflow.Probe(Observed() with { Map = new(Map.MapId + 1, Map.Generation) }, All).Status);
        Assert.Equal(ResumableTravelProbeStatus.GeometryStale,
            workflow.Probe(Observed() with { GeometryMap = Map.MapId - 1 }, All).Status);
        Assert.Equal(ResumableTravelProbeStatus.PositionUnconfirmed,
            workflow.Probe(Observed() with { PositionMap = Map.MapId - 1 }, All).Status);
        Assert.Equal(ResumableTravelProbeStatus.PositionUnconfirmed,
            workflow.Probe(Observed() with { PositionConfirmed = false }, All).Status);
        Assert.Equal(ResumableTravelProbeStatus.PositionUnconfirmed,
            workflow.Probe(Observed() with { ServerCell = null }, All).Status);
        Assert.Equal(ResumableTravelProbeStatus.PositionUnconfirmed,
            workflow.Probe(Observed() with { ServerCell = 560 }, All).Status);
    }

    [Fact]
    public void Interrupted_or_suspended_transport_cannot_admit_a_move()
    {
        var workflow = ResumableTravelWorkflow.Start(Map, Target);
        var interrupted = workflow.Probe(Observed() with { Interrupted = true }, All);
        Assert.Equal(ResumableTravelProbeStatus.Interrupted, interrupted.Status);
        Assert.False(interrupted.CanAttempt);
        Assert.Null(interrupted.AdvisoryRoute);
        Assert.Equal(ResumableTravelProbeStatus.TransportSuspended,
            workflow.Probe(Observed() with { TransportSuspended = true }, All).Status);
        Assert.Equal(0, workflow.NoteLegacyAttempt(interrupted).AttemptCount);
    }

    [Fact]
    public void Dynamic_obstacle_invalidation_recomputes_and_filters_current_route()
    {
        var workflow = ResumableTravelWorkflow.Start(Map, Target);
        var before = workflow.Probe(Observed(), All);
        int blocked = before.AdvisoryRoute!.Cells[before.AdvisoryRoute.Cells.Count / 2];
        var cells = Enumerable.Repeat(true, MapPathfinder.CellCount).ToArray();
        cells[blocked] = false;
        bool Walk(int c) => cells[c];
        var after = workflow.Probe(Observed(), Walk);
        Assert.Equal(ResumableTravelProbeStatus.ReadyForLegacyDispatch, after.Status);
        Assert.DoesNotContain(blocked, after.AdvisoryRoute!.Cells);
        Assert.Equal(MapPathfinder.KeyMovements(Source, Target, Walk), after.AdvisoryRoute.KeyCells);

        cells[Target] = false;
        Assert.Equal(ResumableTravelProbeStatus.Unreachable, workflow.Probe(Observed(), Walk).Status);
        Assert.Equal(ResumableTravelProbeStatus.ReadyForLegacyDispatch, workflow.Probe(Observed(), All).Status);
    }

    [Fact]
    public void Blocked_corners_and_directed_permissions_are_revalidated()
    {
        var workflow = ResumableTravelWorkflow.Start(Map, 241);
        static bool Endpoints(int c) => c is 240 or 241;
        Assert.Equal(ResumableTravelProbeStatus.Unreachable,
            workflow.Probe(Observed(), Endpoints).Status);
        Assert.Equal(ResumableTravelProbeStatus.Unreachable,
            workflow.Probe(Observed(), All, (_, _) => false).Status);
        Assert.Equal(ResumableTravelProbeStatus.ReadyForLegacyDispatch,
            workflow.Probe(Observed(), All).Status);
    }

    [Fact]
    public void Observed_destination_does_not_erase_an_uncorrelated_prior_send()
    {
        var workflow = ResumableTravelWorkflow.Start(Map, Target, maxAttempts: 1);
        var ready = workflow.Probe(Observed(), All);
        workflow = workflow.NoteLegacyAttempt(ready);
        Assert.Equal(ResumableTravelProbeStatus.AwaitingCorrelatedAck,
            workflow.Probe(Observed(), All).Status);
        var arrived = workflow.Probe(Observed(Target), All);
        Assert.Equal(ResumableTravelProbeStatus.AwaitingCorrelatedAck, arrived.Status);
        Assert.False(arrived.CanAttempt);
        Assert.Null(arrived.AdvisoryRoute);
    }

    [Fact]
    public void One_legacy_attempt_latches_without_a_matching_sequence_ack()
    {
        var workflow = ResumableTravelWorkflow.Start(Map, Target, maxAttempts: 2);
        var denied = workflow.Probe(Observed() with { PositionConfirmed = false }, All);
        Assert.Same(workflow, workflow.NoteLegacyAttempt(denied));
        var ready = workflow.Probe(Observed(), All);
        workflow = workflow.NoteLegacyAttempt(ready);
        Assert.Equal(1, workflow.AttemptCount);
        Assert.Same(workflow, workflow.NoteLegacyAttempt(ready)); // probe from an old snapshot
        Assert.True(workflow.AwaitingCorrelatedAck);
        Assert.Equal(ResumableTravelProbeStatus.AwaitingCorrelatedAck, workflow.Probe(Observed(), All).Status);
        Assert.Equal(1, workflow.NoteLegacyAttempt(workflow.Probe(Observed(), All)).AttemptCount);
    }

    [Fact]
    public void Reconnect_requires_fresh_workflow_and_never_revives_stale_state()
    {
        var workflow = ResumableTravelWorkflow.Start(Map, Target, maxAttempts: 1);
        workflow = workflow.NoteLegacyAttempt(workflow.Probe(Observed(), All));
        var resumedMap = new ResumableRouteMap(Map.MapId, Map.Generation + 1);
        var reconnect = Observed() with { Map = resumedMap };
        Assert.Equal(ResumableTravelProbeStatus.AwaitingCorrelatedAck, workflow.Probe(reconnect, All).Status);
        var recreated = ResumableTravelWorkflow.Start(resumedMap, Target);
        Assert.Equal(0, recreated.AttemptCount);
        Assert.Equal(ResumableTravelProbeStatus.ReadyForLegacyDispatch, recreated.Probe(reconnect, All).Status);
    }

    [Fact]
    public void Same_map_actor_snapshot_generation_does_not_reset_retry_budget()
    {
        var workflow = ResumableTravelWorkflow.Start(Map, Target, maxAttempts: 1);
        workflow = workflow.NoteLegacyAttempt(workflow.Probe(Observed(), All));
        var nextGeneration = new ResumableRouteMap(Map.MapId, Map.Generation + 1);
        var next = workflow.RebindMapGeneration(nextGeneration);
        Assert.Equal(1, next.AttemptCount);
        Assert.Equal(ResumableTravelProbeStatus.AwaitingCorrelatedAck,
            next.Probe(Observed() with { Map = nextGeneration }, All).Status);
        Assert.Same(workflow,
            workflow.RebindMapGeneration(new ResumableRouteMap(Map.MapId + 1, Map.Generation + 1)));
    }

    [Fact]
    [Trait("Category", "PerformanceObservation")]
    public void Repeated_preflights_log_deterministic_route_and_allocation_measurements()
    {
        var rng = new Random(20261113);
        var walk = Enumerable.Range(0, MapPathfinder.CellCount)
            .Select(_ => rng.Next(100) > 15).ToArray();
        bool Walk(int c) => walk[c];
        var cases = new List<(ResumableTravelWorkflow Workflow, ResumableTravelSnapshot Snapshot)>();
        for (int i = 0; i < 600 && cases.Count < 64; i++)
        {
            int source = rng.Next(MapPathfinder.CellCount), target = rng.Next(MapPathfinder.CellCount);
            if (MapPathfinder.KeyMovements(source, target, Walk).Count == 0) continue;
            cases.Add((ResumableTravelWorkflow.Start(Map, target), Observed(source)));
        }
        Assert.Equal(64, cases.Count);
        foreach (var (workflow, snapshot) in cases)
        {
            var probe = workflow.Probe(snapshot, Walk);
            Assert.True(probe.CanAttempt);
            Assert.Equal(MapPathfinder.KeyMovements(snapshot.ServerCell!.Value, workflow.Destination, Walk),
                probe.AdvisoryRoute!.KeyCells);
        }

        static (long Ticks, long Bytes, int Encoded) Measure(Func<int> work)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            int encoded = 0;
            for (int repeat = 0; repeat < 3; repeat++) encoded += work();
            watch.Stop();
            return (watch.ElapsedTicks, GC.GetAllocatedBytesForCurrentThread() - before, encoded);
        }
        int Baseline() => cases.Sum(c => MapPathfinder.KeyMovements(
            c.Snapshot.ServerCell!.Value, c.Workflow.Destination, Walk).Count);
        int Diagnostic() => cases.Sum(c => c.Workflow.Probe(c.Snapshot, Walk).AdvisoryRoute!.KeyCells.Count);
        _ = Baseline();
        _ = Diagnostic();
        var old = Measure(Baseline);
        var diagnostic = Measure(Diagnostic);
        Assert.Equal(old.Encoded, diagnostic.Encoded); // exact encoded lengths, full equality checked above
        output.WriteLine($"64 seeded source/destination pairs x3; direct={old.Ticks} ticks/{old.Bytes} B; " +
            $"diagnostic={diagnostic.Ticks} ticks/{diagnostic.Bytes} B. Observation only; no speed assertion.");
    }
}
