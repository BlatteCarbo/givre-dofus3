using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GivreClone.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace GivreClone.Tests;

/// <summary>
/// Reproductible : 96 requetes sur une grille fixe. Compare uniquement le cout d'une
/// reprise depuis un chemin DEJA emis au cout d'un nouveau A* depuis le checkpoint.
/// Les deux solutions peuvent choisir des virages differents; aucun seuil de vitesse
/// ni de ratio allocation n'est requis pour le succes.
/// </summary>
public sealed class ResumableRouteBenchmarkTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "PerformanceObservation")]
    public void Observed_suffix_recovery_vs_new_Astar_has_deterministic_workload_and_logs_measurements()
    {
        var map = new ResumableRouteMap(90703360, 1);
        var rng = new Random(20261113);
        var blocked = Enumerable.Range(0, MapPathfinder.CellCount)
            .Select(_ => rng.Next(100) < 12).ToArray();
        bool Walk(int cell) => !blocked[cell];
        var scenarios = new List<(ResumableRoute Pending, int Checkpoint, int Target)>();
        for (int candidate = 0; candidate < 1200 && scenarios.Count < 96; candidate++)
        {
            int source = rng.Next(MapPathfinder.CellCount), target = rng.Next(MapPathfinder.CellCount);
            var route = ResumableRoute.Start(map, source, target, Walk);
            if (route.Cells.Count < 4) continue;
            int observed = route.Cells[route.Cells.Count / 2];
            scenarios.Add((route.BeginDispatch(map, candidate + 1L), observed, target));
        }
        Assert.Equal(96, scenarios.Count);

        foreach (var (pending, checkpoint, target) in scenarios)
        {
            var resumed = pending.Confirmed(map, pending.PendingSequence!.Value, checkpoint, true, Walk);
            Assert.Equal(ResumableRoutePhase.Ready, resumed.Phase);
            Assert.Equal(checkpoint, resumed.Cells[0]);
            Assert.Equal(target, resumed.Cells[^1]);
            Assert.True(MapPathfinder.IsMovementPathLegal(resumed.Cells, Walk));
            Assert.Equal(MapPathfinder.Compress(resumed.Cells.ToList()), resumed.KeyCells);
            Assert.NotEmpty(MapPathfinder.KeyMovements(checkpoint, target, Walk));
        }

        static (long Ticks, long Bytes, int Count) Measure(Func<int> work)
        {
            int count = 0;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var clock = Stopwatch.StartNew();
            for (int repeat = 0; repeat < 3; repeat++) count += work();
            clock.Stop();
            return (clock.ElapsedTicks, GC.GetAllocatedBytesForCurrentThread() - allocated, count);
        }
        int Recompute()
        {
            int count = 0;
            foreach (var (_, checkpoint, target) in scenarios)
                count += MapPathfinder.KeyMovements(checkpoint, target, Walk).Count;
            return count;
        }
        int Resume()
        {
            int count = 0;
            foreach (var (pending, checkpoint, _) in scenarios)
                count += pending.Confirmed(map, pending.PendingSequence!.Value, checkpoint, true, Walk).KeyCells.Count;
            return count;
        }

        // JIT warmup, puis ordre alterne pour que le benchmark reste indicatif.
        _ = Recompute();
        _ = Resume();
        var recompute = Measure(Recompute);
        var resume = Measure(Resume);
        Assert.True(recompute.Count > 0);
        Assert.True(resume.Count > 0);
        output.WriteLine($"96 deterministic checkpoints x3; recompute-A*: {recompute.Ticks} ticks, " +
                         $"{recompute.Bytes} allocated bytes, {recompute.Count} encoded cells; " +
                         $"resume-suffix: {resume.Ticks} ticks, {resume.Bytes} allocated bytes, " +
                         $"{resume.Count} encoded cells. Counts represent potentially different exact routes.");
    }
}
