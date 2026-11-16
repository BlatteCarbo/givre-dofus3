using System.Collections.Immutable;
using System.Diagnostics;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace GivreClone.Tests;

public class CombatPlannerPerformanceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Full_board_budgeted_search_is_repeatable(bool withZonePolicy)
    {
        var cells = Enumerable.Range(0, 560).Select(c => new PlannerCell(c, true, false,
            Enumerable.Range(0, 560).Where(n => MapPathfinder.CellDistance(c, n) == 1)
                .ToImmutableArray())).ToImmutableArray();
        var fighters = ImmutableArray.Create(new PlannerFighter(1, 327, false, 2000, 2000),
            new PlannerFighter(2, 371, true, 1800, 1800),
            new PlannerFighter(3, 370, true, 1400, 1400));
        var spells = Enumerable.Range(0, 19).Select(i => new PlannerSpell(
            new SpellCastDescriptor(100 + i, 1, 3 + i % 2, 0, 6, 0, false, 1, 1, 0, 0, "",
                ImmutableArray.Create(new SpellEffectDescriptor(100, SpellEffectKind.Damage,
                    30 + i, 40 + i, 0, 0, "A", i % 3 == 0 ? 'C' : 'P', i % 3 == 0 ? 2 : 0, 0))),
            2, 2, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(2L, 3L),
            ZonePolicy: withZonePolicy ? new(i % 3 == 0 ? 'C' : 'P', i % 3 == 0 ? 2 : 0,
                0, 1, true, ImmutableHashSet<long>.Empty) : null))
            .ToImmutableArray();
        var snapshot = new CombatTurnSnapshot(cells, fighters, 1, 11, 5, spells,
            ImmutableDictionary<int, int>.Empty, TackleImmune: true);
        CombatTurnPlan Choose() => CombatTurnPlanner.Choose(snapshot, 60_000);
        var expected = Choose();
        for (int i = 0; i < 5; i++) Choose();
        var times = new List<double>();
        var allocations = new List<long>();
        for (int i = 0; i < 15; i++)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            var plan = Choose();
            watch.Stop();
            allocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocated);
            times.Add(watch.Elapsed.TotalMilliseconds);
            Assert.Equal(expected.Actions.ToArray(), plan.Actions.ToArray());
            Assert.Equal(expected.Score, plan.Score);
            Assert.Equal(expected.Work, plan.Work);
        }
        Assert.Equal(60_000, expected.Work);
        Assert.Contains(expected.Actions, a => a.Kind == PlannerActionKind.Cast);
        times.Sort();
        allocations.Sort();
        output.WriteLine($"Choose samples=15 median_ms={times[7]:F3} min_ms={times[0]:F3} max_ms={times[^1]:F3} allocated_bytes={allocations[7]} work={expected.Work} bounded={expected.BudgetExhausted}");
        output.WriteLine($"Actions={string.Join(" -> ", expected.Actions)} score={expected.Score}");
    }
}
