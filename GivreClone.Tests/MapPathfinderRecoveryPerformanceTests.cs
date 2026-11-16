using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GivreClone.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace GivreClone.Tests;

/// <summary>
/// Reference A* kept independent of the production precomputed topology/stack spans.
/// Compare complete encoded routes (including turning points/directions), not just reachability.
/// Fixed seeds make map generation and benchmark workloads reproducible; elapsed time is observational.
/// </summary>
public sealed class MapPathfinderRecoveryPerformanceTests(ITestOutputHelper output)
{
    private static readonly (int X, int Y)[] Points = Enumerable.Range(0, MapPathfinder.CellCount)
        .Select(id => (X: id % 14 - id / 28 + 19, Y: id % 14 - id / 28 + 19 + id / 14))
        .ToArray();

    private static readonly Dictionary<int, int> CellLookup = Enumerable.Range(0, MapPathfinder.CellCount)
        .ToDictionary(id => Key(Points[id].X, Points[id].Y), id => id);

    private static int Key(int x, int y) => (x << 8) | (y & 0xFF);
    private static int CellAt(int x, int y) => CellLookup.TryGetValue(Key(x, y), out var id) ? id : -1;

    private static int Octile(int a, int b)
    {
        var (ax, ay) = Points[a];
        var (bx, by) = Points[b];
        int dx = Math.Abs(ax - bx), dy = Math.Abs(ay - by);
        return 10 * Math.Max(dx, dy) + 4 * Math.Min(dx, dy);
    }

    // Historical heap/dictionary implementation, preserved as a differential oracle.
    private static IReadOnlyList<int> Baseline(int source, int target, Func<int, bool> walk,
        bool allowDiag, Func<int, int, bool>? stepAllowed)
    {
        if (source == target || (uint)source >= MapPathfinder.CellCount ||
            (uint)target >= MapPathfinder.CellCount || !walk(target)) return Array.Empty<int>();

        var open = new PriorityQueue<int, int>();
        var came = new Dictionary<int, int>();
        var g = new Dictionary<int, int> { [source] = 0 };
        var closed = new HashSet<int>();
        open.Enqueue(source, Octile(source, target));
        while (open.TryDequeue(out int cur, out _))
        {
            if (cur == target)
            {
                var path = new List<int> { cur };
                while (came.TryGetValue(cur, out int prev)) { cur = prev; path.Add(cur); }
                path.Reverse();
                return MapPathfinder.Compress(path);
            }
            if (!closed.Add(cur)) continue;
            var (cx, cy) = Points[cur];
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    if (!allowDiag && dx != 0 && dy != 0) continue;
                    int nb = CellAt(cx + dx, cy + dy);
                    if (nb < 0 || closed.Contains(nb)) continue;
                    if (nb != target && !walk(nb)) continue;
                    if (dx != 0 && dy != 0)
                    {
                        int c1 = CellAt(cx + dx, cy), c2 = CellAt(cx, cy + dy);
                        bool c1ok = c1 >= 0 && walk(c1) && (stepAllowed == null || stepAllowed(cur, c1));
                        bool c2ok = c2 >= 0 && walk(c2) && (stepAllowed == null || stepAllowed(cur, c2));
                        if (!c1ok && !c2ok) continue;
                    }
                    if (stepAllowed != null && !stepAllowed(cur, nb)) continue;
                    int cost = (dx != 0 && dy != 0) ? 14 : 10;
                    int tentative = g[cur] + cost;
                    if (!g.TryGetValue(nb, out int old) || tentative < old)
                    {
                        came[nb] = cur;
                        g[nb] = tentative;
                        open.Enqueue(nb, tentative + Octile(nb, target));
                    }
                }
        }
        return Array.Empty<int>();
    }

    private static int BaselineSteps(int source, int target, Func<int, bool> walk,
        Func<int, int, bool>? stepAllowed)
    {
        var keys = Baseline(source, target, walk, allowDiag: true, stepAllowed: stepAllowed);
        return keys.Count == 0 ? -1 : MapPathfinder.ExpandPath(keys.Select(k => k & 0xFFF).ToArray()).Count - 1;
    }

    private static int? BaselineStandCell(int source, int target, int? canonical,
        Func<int, bool> walk, Func<int, int, bool>? stepAllowed)
    {
        if ((uint)target >= MapPathfinder.CellCount) return null;
        if (MapPathfinder.InteractionAdjacent(source, target)) return source;
        var candidates = MapPathfinder.InteractionStandCandidates(source, target, canonical);
        bool Avoid(int c) => c != target && walk(c);
        int best = -1, stepsBest = int.MaxValue;
        foreach (int candidate in candidates)
        {
            if (candidate == source) return source;
            if (!Avoid(candidate) || MapPathfinder.Distance(source, candidate) >= stepsBest) continue;
            int steps = BaselineSteps(source, candidate, Avoid, stepAllowed);
            if (steps > 0 && steps < stepsBest) { best = candidate; stepsBest = steps; }
        }
        if (best >= 0) return best;
        foreach (int candidate in candidates)
            if (walk(candidate) && Baseline(source, candidate, walk, true, stepAllowed).Count > 0)
                return candidate;
        return source == target ? source : null;
    }

    [Fact]
    public void Weighted_routes_match_historical_Astar_in_both_directions_across_seeded_maps()
    {
        var rng = new Random(20261113);
        foreach (int walkPercent in new[] { 20, 45, 70, 100 })
        {
            for (int map = 0; map < 3; map++)
            {
                var cells = Enumerable.Range(0, MapPathfinder.CellCount)
                    .Select(_ => rng.Next(100) < walkPercent).ToArray();
                bool Walk(int c) => (uint)c < (uint)cells.Length && cells[c];
                int boundary = rng.Next(100, 460);
                // A directed gate is intentional: stepAllowed cannot be assumed symmetric.
                bool Step(int from, int to) =>
                    ((from < boundary) == (to < boundary) || (from < boundary && to >= boundary && map == 1)) &&
                    (from * 7 + to * 11) % 31 != 0;
                for (int i = 0; i < 60; i++)
                {
                    int source = rng.Next(MapPathfinder.CellCount), target = rng.Next(MapPathfinder.CellCount);
                    foreach (bool diagonals in new[] { false, true })
                    {
                        Assert.Equal(Baseline(source, target, Walk, diagonals, Step),
                            MapPathfinder.KeyMovements(source, target, Walk, diagonals, Step));
                        Assert.Equal(Baseline(target, source, Walk, diagonals, Step),
                            MapPathfinder.KeyMovements(target, source, Walk, diagonals, Step));
                    }
                }
            }
        }
    }

    [Fact]
    public void Multi_target_interaction_stand_selection_matches_reference_under_blockers_and_directed_gates()
    {
        var rng = new Random(20261115);
        for (int map = 0; map < 12; map++)
        {
            var cells = Enumerable.Range(0, MapPathfinder.CellCount)
                .Select(_ => rng.Next(100) < 65).ToArray();
            bool Walk(int c) => (uint)c < (uint)cells.Length && cells[c];
            bool Step(int a, int b) => (a * 3 + b * 5) % 17 != 0;
            for (int i = 0; i < 10; i++)
            {
                int target = rng.Next(MapPathfinder.CellCount);
                int source = rng.Next(5) == 0 ? target : rng.Next(MapPathfinder.CellCount);
                var neighbors = MapPathfinder.InteractionNeighbours(target);
                int? canonical = rng.Next(3) switch
                {
                    0 when neighbors.Count > 0 => neighbors[rng.Next(neighbors.Count)],
                    1 => target, // invalid canonical: must not accidentally return the target
                    _ => null
                };
                Assert.Equal(BaselineStandCell(source, target, canonical, Walk, Step),
                    MapPathfinder.StandCellFor(source, target, 1, canonical, Walk, Step));
            }
        }
    }

    [Fact]
    public void Invalid_blocked_corner_and_disconnected_targets_remain_rejected()
    {
        static bool All(int _) => true;
        foreach (int invalid in new[] { -1, MapPathfinder.CellCount, int.MinValue, int.MaxValue })
        {
            Assert.Empty(MapPathfinder.KeyMovements(invalid, 240, All));
            Assert.Empty(MapPathfinder.KeyMovements(240, invalid, All));
        }
        Assert.Empty(MapPathfinder.KeyMovements(240, 240, All));
        Assert.Empty(MapPathfinder.KeyMovements(240, 226, c => c != 226));
        Assert.Empty(MapPathfinder.KeyMovements(240, 241, c => c is 240 or 241));
        Assert.Empty(MapPathfinder.KeyMovements(240, 241, c => c is 240 or 241,
            stepAllowed: (_, _) => true));

        static bool Split(int a, int b) => (a < 280) == (b < 280);
        Assert.Empty(MapPathfinder.KeyMovements(240, 320, All, stepAllowed: Split));
        Assert.Equal(Baseline(240, 320, All, true, Split),
            MapPathfinder.KeyMovements(240, 320, All, stepAllowed: Split));
    }

    [Fact]
    public void Same_delegate_rechecks_walkability_and_sector_permissions_after_map_generation_changes()
    {
        var cells = new bool[MapPathfinder.CellCount];
        bool permit = false;
        bool Walk(int c) => (uint)c < (uint)cells.Length && cells[c];
        bool Step(int _, int __) => permit;
        const int source = 240, target = 320;
        Assert.Empty(MapPathfinder.KeyMovements(source, target, Walk, stepAllowed: Step));
        Array.Fill(cells, true);
        Assert.Empty(MapPathfinder.KeyMovements(source, target, Walk, stepAllowed: Step));
        permit = true;
        var openRoute = MapPathfinder.KeyMovements(source, target, Walk, stepAllowed: Step);
        Assert.NotEmpty(openRoute);
        Assert.Equal(Baseline(source, target, Walk, true, Step), openRoute);
        cells[target] = false;
        Assert.Empty(MapPathfinder.KeyMovements(source, target, Walk, stepAllowed: Step));
        cells[target] = true;
        permit = false;
        Assert.Empty(MapPathfinder.KeyMovements(source, target, Walk, stepAllowed: Step));
    }

    [Fact]
    public void Delegate_invocation_order_matches_reference_with_corner_and_sector_gates()
    {
        var rng = new Random(13112026);
        for (int sample = 0; sample < 40; sample++)
        {
            bool[] cells = Enumerable.Range(0, MapPathfinder.CellCount)
                .Select(_ => rng.Next(4) != 0).ToArray();
            int source = rng.Next(MapPathfinder.CellCount), target = rng.Next(MapPathfinder.CellCount);
            var originalTrace = new List<string>();
            var newTrace = new List<string>();
            bool OriginalWalk(int c) { originalTrace.Add($"W{c}"); return cells[c]; }
            bool CurrentWalk(int c) { newTrace.Add($"W{c}"); return cells[c]; }
            bool OriginalStep(int a, int b) { originalTrace.Add($"S{a}:{b}"); return (a + b) % 13 != 0; }
            bool CurrentStep(int a, int b) { newTrace.Add($"S{a}:{b}"); return (a + b) % 13 != 0; }
            Assert.Equal(Baseline(source, target, OriginalWalk, true, OriginalStep),
                MapPathfinder.KeyMovements(source, target, CurrentWalk, true, CurrentStep));
            Assert.Equal(originalTrace, newTrace);
        }
    }

    [Fact]
    public void Extreme_floor_differences_cannot_wrap_to_an_allowed_connection()
    {
        var floors = new int[MapPathfinder.CellCount];
        var zones = new int[MapPathfinder.CellCount];
        var bridges = new bool[MapPathfinder.CellCount];
        floors[240] = int.MinValue;
        floors[241] = int.MaxValue;
        zones[240] = zones[241] = 1;
        bridges[240] = bridges[241] = true;
        Assert.False(MapPathfinder.Communicates(240, 241, floors, zones, bridges));
        bridges[240] = bridges[241] = false;
        Assert.False(MapPathfinder.Communicates(240, 241, floors, zones, bridges));
        floors[241] = int.MinValue;
        Assert.True(MapPathfinder.Communicates(240, 241, floors, zones, bridges));
    }

    [Fact]
    [Trait("Category", "PerformanceObservation")]
    public void Deterministic_workload_compares_baseline_and_optimized_without_a_speed_assumption()
    {
        var rng = new Random(20261114);
        var cells = Enumerable.Range(0, MapPathfinder.CellCount)
            .Select(_ => rng.Next(100) < 88).ToArray();
        bool Walk(int c) => cells[c];
        var targets = Enumerable.Range(0, 96).Select(_ =>
            (Source: rng.Next(MapPathfinder.CellCount), Target: rng.Next(MapPathfinder.CellCount))).ToArray();
        foreach (var (source, target) in targets)
            Assert.Equal(Baseline(source, target, Walk, true, null),
                MapPathfinder.KeyMovements(source, target, Walk, true));

        // Warm both implementations before observing costs. Results are environment-dependent diagnostics only.
        int RunOld() { int n = 0; foreach (var (s, t) in targets) n += Baseline(s, t, Walk, true, null).Count; return n; }
        int RunNew() { int n = 0; foreach (var (s, t) in targets) n += MapPathfinder.KeyMovements(s, t, Walk).Count; return n; }
        Assert.Equal(RunOld(), RunNew());
        static (long Ticks, long Bytes, int Result) Measure(Func<int> run)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var clock = Stopwatch.StartNew();
            int count = 0;
            for (int i = 0; i < 3; i++) count += run();
            clock.Stop();
            return (clock.ElapsedTicks, GC.GetAllocatedBytesForCurrentThread() - before, count);
        }
        var old = Measure(RunOld);
        var current = Measure(RunNew);
        Assert.Equal(old.Result, current.Result);
        output.WriteLine($"96 deterministic pairs x3; baseline={old.Ticks} ticks/{old.Bytes} B; " +
                         $"optimized={current.Ticks} ticks/{current.Bytes} B; no speed threshold asserted.");
    }
}
