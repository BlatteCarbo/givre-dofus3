extern alias F06App;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;
using Xunit;
using Xunit.Abstractions;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using CombatProfile = F06App::GivreClone.App.Services.CombatProfile;
using CombatSpeed = F06App::GivreClone.App.Services.CombatSpeed;
using GameNameResolver = F06App::GivreClone.App.Services.GameNameResolver;
using MapCellReader = F06App::GivreClone.App.Services.MapCellReader;

namespace GivreClone.Tests;

public class LiveCombatRegressionTests(ITestOutputHelper output)
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(LaunchService service, string name) => (T)typeof(LaunchService).GetField(name, Private)!.GetValue(service)!;

    [Theory]
    [InlineData(65)]
    [InlineData(170)]
    public async Task Text_before_sequence_wakes_pending_cast_without_fictional_ack(int message)
    {
        const uint pid = 123;
        var service = new LaunchService();
        Field<ConcurrentDictionary<uint, bool>>(service, "_inFight")[pid] = true;
        Field<ConcurrentDictionary<uint, bool>>(service, "_myTurnOpen")[pid] = true;
        Field<ConcurrentDictionary<uint, CombatProfile>>(service, "_combat")[pid] = new() { Speed = CombatSpeed.Turbo };
        var begin = typeof(LaunchService).GetMethod("NoteTurboRequestTx", Private)!;
        var reject = typeof(LaunchService).GetMethod("NoteTurboCastRejection", Private)!;
        var wait = typeof(LaunchService).GetMethod("WaitTurboActionBarrier", Private)!;
        var query = typeof(LaunchService).GetMethod("TryTurboCastRejection", Private)!;
        long request = (long)begin.Invoke(service, [pid, "cast:14396"])!;
        Assert.True(request > 0, "turbo metrics must arm the request in this setup");
        var pending = (Task<bool>)wait.Invoke(service, [pid, request, 60000])!;
        Assert.False(pending.IsCompleted);
        reject.Invoke(service, [pid, message, new[] { "2", "3" }]);
        // Bounded wait on the refusal signal itself (not a sleep): the full suite can starve the thread pool for seconds.
        Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
        object[] args = [pid, request, 0];
        Assert.True((bool)query.Invoke(service, args)!);
        Assert.Equal(message == 170 ? 2 : -1, (int)args[2]);
        Assert.Empty(Field<ConcurrentDictionary<(uint Pid, long ActionId), long>>(service, "_turboAcked"));
        long next = (long)begin.Invoke(service, [pid, "cast:13531"])!;
        Field<ConcurrentDictionary<uint, long>>(service, "_turboSeqOuterStartAt")[pid] = next;
        reject.Invoke(service, [pid, message, new[] { "2", "3" }]);
        args = [pid, next, 0];
        Assert.False((bool)query.Invoke(service, args)!);
    }

    [Theory]
    [InlineData(597)]
    [InlineData(188)]
    [InlineData(1376)]
    public void Challenge_failure_does_not_reject_the_pending_cast(int message)
    {
        const uint pid = 123;
        var service = new LaunchService();
        Field<ConcurrentDictionary<uint, bool>>(service, "_inFight")[pid] = true;
        Field<ConcurrentDictionary<uint, bool>>(service, "_myTurnOpen")[pid] = true;
        Field<ConcurrentDictionary<uint, CombatProfile>>(service, "_combat")[pid] = new() { Speed = CombatSpeed.Turbo };
        typeof(LaunchService).GetMethod("NoteTurboRequestTx", Private)!.Invoke(service, [pid, "cast:4245"]);

        typeof(LaunchService).GetMethod("NoteTurboCastRejection", Private)!.Invoke(service,
            [pid, message, message == 1376 ? new[] { "485" }
                : message == 188 ? new[] { "Bastionbot", "9" } : new[] { "8", "Bastionbot", "45" }]);

        Assert.Empty(Field<ConcurrentDictionary<uint, (long RequestAt, int AvailableAp)>>(service, "_turboCastRejected"));
    }

    [Fact]
    public void Installed_live_board_reports_spell_geometry_and_bounded_plan_time()
    {
        var maps = new MapCellReader();
        if (!maps.Available) { output.WriteLine("Client map data unavailable"); return; }
        var cells = maps.LoadCells(54156618);
        Assert.NotNull(cells);
        var names = new GameNameResolver(); names.EnsureCombatLoaded();
        foreach (int cell in new[] { 218, 274, 261, 344 })
        {
            var c = cells!.Single(c => c.CellId == cell);
            output.WriteLine($"live-cell={cell} mov={c.Mov} fightBlocked={c.NonWalkableDuringFight} los={c.Los} distance274={MapPathfinder.CellDistance(274, cell)}");
        }
        var spells = new List<PlannerSpell>();
        foreach (int id in new[] { 13526, 13528, 14396, 13531, 13515, 13525 })
        {
            var d = names.GetSpellDescriptor(id, 1);
            if (d is null) continue;
            output.WriteLine($"spell={id} range={d.MinRange}..{d.Range} rangeable={d.Rangeable} flags={d.Flags} effects={string.Join(';', d.Effects.Select(e => $"{e.ActionId}:{e.Shape}/{e.Size}:{e.TargetMask}"))}");
            // App descriptors are aliased; serialize only to detach identical value contracts for the linked planner.
            var cast = System.Text.Json.JsonSerializer.Deserialize<SpellCastDescriptor>(System.Text.Json.JsonSerializer.Serialize(d))!;
            if (CombatTurnPlanner.IsAdmitted(cast)) spells.Add(new(cast with { Range = cast.Range + (cast.Rangeable ? 5 : 0) }, 2, 2,
                ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L, 2L, -1L, -2L)));
        }
        var board = cells!.Select(c => new PlannerCell(c.CellId, c.Mov && !c.NonWalkableDuringFight, !c.Los,
            MapPathfinder.InteractionNeighbours(c.CellId).Where(n => MapPathfinder.CellDistance(c.CellId, n) == 1).ToImmutableArray())).ToImmutableArray();
        var snapshot = new CombatTurnSnapshot(board, ImmutableArray.Create(
            new PlannerFighter(1, 343, false, 2199, 2199), new PlannerFighter(2, 357, false, 3048, 3048),
            new PlannerFighter(-1, 261, true, 2900, 2900), new PlannerFighter(-2, 344, true, 2900, 2900)),
            1, 10, 5, spells.ToImmutableArray(), ImmutableDictionary<int, int>.Empty);
        var timer = Stopwatch.StartNew();
        var plan = CombatTurnPlanner.Choose(snapshot, 18_000);
        timer.Stop();
        output.WriteLine($"plan_ms={timer.Elapsed.TotalMilliseconds:F1} work={plan.Work} actions=[{string.Join(" -> ", plan.Actions)}]");
        Assert.InRange(plan.Work, 1, 18_000);
        Assert.NotEmpty(plan.Actions);
        var repeat = CombatTurnPlanner.Choose(snapshot, 18_000);
        Assert.Equal(plan.Actions.Select(a => a.ToString()), repeat.Actions.Select(a => a.ToString()));
    }
    [Fact]
    public void Observed_enemy_hit_keeps_the_strongest_blow_and_is_forgotten_with_the_fight()
    {
        const uint pid = 7;
        var service = new LaunchService();
        var note = typeof(LaunchService).GetMethod("NoteEnemyHit", Private)!;
        var forget = typeof(LaunchService).GetMethod("ForgetEnemyHits", Private)!;
        Assert.Null(service.ObservedEnemyHit(pid, -5));
        note.Invoke(service, [pid, -5L, 420]);
        note.Invoke(service, [pid, -5L, 180]);
        Assert.Equal(420, service.ObservedEnemyHit(pid, -5));
        forget.Invoke(service, [pid]);
        Assert.Null(service.ObservedEnemyHit(pid, -5));
    }
    // Live 02/11 21:25 : un monstre vivant (aucune annonce de mort) avec des PV suivis à 0 disparaissait des ennemis.
    [Fact]
    public void Enemy_still_tracked_with_zero_tracked_life_stays_listed_until_its_death()
    {
        const uint pid = 9;
        var service = new LaunchService();
        Field<ConcurrentDictionary<uint, IReadOnlyList<(int Cell, long ActorId)>>>(service, "_fighters")[pid] =
            new List<(int Cell, long ActorId)> { (115, -2) };
        var stats = new ConcurrentDictionary<long, FighterStats>();
        stats[-2] = new FighterStats(-2, 10, 5, 0, 2709, 10, 5);
        Field<ConcurrentDictionary<uint, ConcurrentDictionary<long, FighterStats>>>(service, "_fighterStats")[pid] = stats;
        var fighter = Assert.Single(service.FightersDetail(pid));
        Assert.Equal(-2, fighter.ActorId);
        var lines = new List<string>();
        void Capture(uint _, string line) { lock (lines) lines.Add(line); }
        bool wasEnabled = GivreLauncher.Core.Transport.TransportLog.Enabled;
        GivreLauncher.Core.Transport.TransportLog.Enabled = true;
        GivreLauncher.Core.Transport.TransportLog.EmittedWithPid += Capture;
        try { typeof(LaunchService).GetMethod("NoteTurnHp", Private)!.Invoke(service, [pid, 9]); }
        finally
        {
            GivreLauncher.Core.Transport.TransportLog.EmittedWithPid -= Capture;
            GivreLauncher.Core.Transport.TransportLog.Enabled = wasEnabled;
        }
        lock (lines) Assert.Contains(lines, l => l.Contains("ennemis [115:0/2709]"));
    }
}
