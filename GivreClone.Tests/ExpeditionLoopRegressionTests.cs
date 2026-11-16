using MoonSharp.Interpreter;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class ExpeditionLoopRegressionTests
{
    [Fact]
    public void No_route_map_is_warned_once_and_skipped_without_activities_on_later_circuits()
    {
        var lua = Load(new ExpeditionProfile("disconnected", "10;11;12", Gather: true,
            ActivityMaps: new long[] { 10, 11, 12 }));
        lua.DoString("""
            current = 99
            visits, harvests, attacks, warnings, errors = {}, {}, {}, {}, {}
            function travelTo(m)
                table.insert(visits, m)
                if m == 10 then return false, 'no_route' end
                current = m
                return true
            end
            function gatherAll() table.insert(harvests, current); return true end
            function fight() table.insert(attacks, current); return false, 'no_matching_group' end
            function printWarning(m) table.insert(warnings, m) end
            function printError(m) table.insert(errors, m) end
            """);

        for (int i = 0; i < 6; i++) Assert.True(BoundedMove(lua));

        Assert.Equal(new double[] { 10, 10, 10, 11, 12, 11, 12 },
            lua.Globals.Get("visits").Table.Values.Select(v => v.Number));
        Assert.Equal(new double[] { 11, 12, 11, 12 },
            lua.Globals.Get("harvests").Table.Values.Select(v => v.Number));
        Assert.Equal(new double[] { 11, 12, 11, 12 },
            lua.Globals.Get("attacks").Table.Values.Select(v => v.Number));
        var warning = Assert.Single(lua.Globals.Get("warnings").Table.Values).String;
        Assert.Contains("map=10", warning);
        Assert.Contains("reason=no_route", warning);
        Assert.Empty(lua.Globals.Get("errors").Table.Values);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void All_activity_maps_without_routes_stop_even_when_approach_maps_remain_reachable(bool selectedActivities)
    {
        var lua = Load(new ExpeditionProfile("disconnected", "1;10;11", Gather: true,
            ActivityMaps: selectedActivities ? new long[] { 10, 11 } : null));
        lua.DoString(selectedActivities ? "reachableApproach = true" : "reachableApproach = false");
        lua.DoString("""
            visits, warnings, errors = {}, {}, {}
            harvests = 0
            function travelTo(m)
                table.insert(visits, m)
                if reachableApproach and m == 1 then return true end
                return false, 'no_route'
            end
            function gatherAll() harvests = harvests + 1; return true end
            function printWarning(m) table.insert(warnings, m) end
            function printError(m) table.insert(errors, m) end
            """);

        Assert.True(BoundedMove(lua));
        Assert.True(BoundedMove(lua));
        Assert.False(BoundedMove(lua));

        Assert.Equal(selectedActivities ? new double[] { 1, 10, 10, 10, 11, 11, 11 }
                : new double[] { 1, 1, 1, 10, 10, 10, 11, 11, 11 },
            lua.Globals.Get("visits").Table.Values.Select(v => v.Number));
        Assert.Equal(selectedActivities ? 2 : 3, lua.Globals.Get("warnings").Table.Length);
        Assert.Contains("reason=all_activity_maps_unreachable", Assert.Single(lua.Globals.Get("errors").Table.Values).String);
        Assert.Equal(0, lua.Globals.Get("harvests").Number);
        Assert.Equal(0, lua.Globals.Get("attempts").Number);
    }

    private static bool BoundedMove(Script lua)
    {
        var coroutine = lua.CreateCoroutine(lua.Globals.Get("move")).Coroutine;
        coroutine.AutoYieldCounter = 100_000;
        var result = coroutine.Resume();
        Assert.Equal(CoroutineState.Dead, coroutine.State);
        return result.Boolean;
    }

    [Theory]
    [InlineData(false, false, 0, 0)]
    [InlineData(false, true, 0, 2)]
    [InlineData(true, false, 2, 0)]
    [InlineData(true, true, 2, 2)]
    public void Selected_activities_run_on_each_visited_map(bool gather, bool combat, int harvests, int attacks)
    {
        var profile = new ExpeditionProfile("mixed", "10;11", Gather: gather, Fight: combat, GatherItemId: 289);
        var lua = Load(profile);
        lua.DoString("harvests = 0; function gatherAll() harvests = harvests + 1; crop = ITEMS_TO_GATHER[1]; return true end");

        lua.Call(lua.Globals.Get("move"));
        lua.Call(lua.Globals.Get("move"));

        Assert.Equal(harvests, lua.Globals.Get("harvests").Number);
        Assert.Equal(attacks, lua.Globals.Get("attempts").Number);
        if (gather) Assert.Equal(289, lua.Globals.Get("crop").Number);
    }

    [Fact]
    public void Harvest_filling_pods_yields_before_combat_and_resumes_next_map_after_bank()
    {
        var lua = Load(new ExpeditionProfile("mixed", "10;11", Gather: true, BankMap: 20, StopAtPodsPercent: 80));
        lua.DoString("visits = {}; function travelTo(m) table.insert(visits,m); return true end; function gatherAll() pods = 80; return true end");

        Assert.True(lua.Call(lua.Globals.Get("move")).Boolean);
        Assert.Equal(0, lua.Globals.Get("attempts").Number);
        lua.DoString("pods = 0; function gatherAll() return false end");
        Assert.True(lua.Call(lua.Globals.Get("move")).Boolean);

        Assert.Equal(1, lua.Globals.Get("attempts").Number);
        Assert.Equal(11, lua.Globals.Get("visits").Table.Get(2).Number);
    }

    private static Script Load(ExpeditionProfile profile)
    {
        var lua = new Script(CoreModules.Preset_SoftSandbox);
        lua.DoString("""
            config = {}
            function config:setMinMonsters(n) end
            function config:setMaxMonsters(n) end
            function config:setMandatoryMonsters(t) end
            function config:setForbiddenMonsters(t) end
            function config:setAmountOfSpecificMonsters(t) end
            fights, pods, attempts, waits = 0, 0, 0, 0
            function getCountFight() return fights end
            function getPodsPercent() return pods end
            function travelTo(m) return true end
            function fight() attempts = attempts + 1; return false, 'no_matching_group' end
            function delay(ms) waits = waits + 1 end
            function printWarning(s) end
            function printMessage(s) end
            function printSuccess(s) end
            function printError(s) end
            """);
        lua.DoString(ExpeditionScript.Build(profile));
        return lua;
    }

    [Theory]
    [InlineData("10")]
    [InlineData("10;11")]
    public void Harvest_reaching_stop_pods_prevents_combat_on_the_same_map(string maps)
    {
        var lua = Load(new ExpeditionProfile("mixed", maps, Gather: true, StopAtPodsPercent: 80));
        lua.DoString("function gatherAll() pods = 80; return true end");

        Assert.False(lua.Call(lua.Globals.Get("move")).Boolean);
        Assert.Equal(0, lua.Globals.Get("attempts").Number);
    }

    [Fact]
    public void Missing_gather_api_stops_instead_of_silently_running_combat_only()
    {
        var lua = Load(new ExpeditionProfile("mixed", "10;11", Gather: true));

        Assert.False(lua.Call(lua.Globals.Get("move")).Boolean);
        Assert.Equal(0, lua.Globals.Get("attempts").Number);
    }

    [Fact]
    public void Approach_maps_run_once_then_the_zone_circuit_repeats()
    {
        var lua = Load(new ExpeditionProfile("approach", "1;2;10;11;20;11", Fight: false, LoopStartIndex: 3));
        lua.DoString("visits = {}; function travelTo(m) table.insert(visits,m); return true end");

        for (int i = 0; i < 10; i++) Assert.True(lua.Call(lua.Globals.Get("move")).Boolean);

        Assert.Equal(new double[] { 1, 2, 10, 11, 20, 11, 10, 11, 20, 11 },
            lua.Globals.Get("visits").Table.Values.Select(v => v.Number));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Only_selected_zone_maps_run_activities_after_route_expansion(bool gather, bool combat)
    {
        var selected = new long[] { 10, 20 };
        var legs = new Dictionary<(long, long), IReadOnlyList<long>>
        {
            [(54169888, 10)] = new long[] { 2, 10 },
            [(10, 20)] = new long[] { 11, 20 },
            [(20, 10)] = new long[] { 11, 10 }
        };
        var route = ExpeditionScript.TraversedMaps(selected, 54169888, (from, to) => legs[(from, to)]);
        var lua = Load(new ExpeditionProfile("zone", string.Join(";", route), Gather: gather, Fight: combat,
            LoopStartIndex: route.IndexOf(selected[0]) + 1, ActivityMaps: selected));
        lua.DoString("""
            current, cleared = 54169888, false
            visits, harvests, attacks = {}, {}, {}
            function travelTo(m)
                current, cleared = m, false
                table.insert(visits, m)
                return true
            end
            function gatherAll() table.insert(harvests, current); return true end
            function fight()
                table.insert(attacks, current)
                if not cleared then cleared = true; return true end
                return false, 'no_matching_group'
            end
            """);

        for (int i = 0; i < 2; i++) Assert.True(lua.Call(lua.Globals.Get("move")).Boolean);
        Assert.Empty(lua.Globals.Get("harvests").Table.Values);
        Assert.Empty(lua.Globals.Get("attacks").Table.Values);

        for (int i = 0; i < 8; i++) Assert.True(lua.Call(lua.Globals.Get("move")).Boolean);

        Assert.Equal(new double[] { 54169888, 2, 10, 11, 20, 11, 10, 11, 20, 11 },
            lua.Globals.Get("visits").Table.Values.Select(v => v.Number));
        Assert.Equal(gather ? new double[] { 10, 20, 10, 20 } : Array.Empty<double>(),
            lua.Globals.Get("harvests").Table.Values.Select(v => v.Number));
        Assert.Equal(combat ? new double[] { 10, 10, 20, 20, 10, 10, 20, 20 } : Array.Empty<double>(),
            lua.Globals.Get("attacks").Table.Values.Select(v => v.Number));
    }

    [Fact]
    public void Single_map_without_respawn_returns_to_host_then_honors_fight_limit()
    {
        var lua = Load(new ExpeditionProfile("test", "10", MaxFights: 1));
        Assert.True(lua.Call(lua.Globals.Get("move")).Boolean);
        Assert.Equal(1, lua.Globals.Get("attempts").Number);
        Assert.Equal(1, lua.Globals.Get("waits").Number);
        lua.Globals["fights"] = 1;
        Assert.False(lua.Call(lua.Globals.Get("move")).Boolean);
        Assert.Equal(1, lua.Globals.Get("attempts").Number);
    }

    [Fact]
    public void Single_map_rechecks_anomaly_closure_between_respawn_polls()
    {
        var lua = Load(new ExpeditionProfile("test", "10", AnomalySubarea: 7));
        lua.DoString("open = true; anomaly = {}; function anomaly:isOpen(id) return open end");
        Assert.True(lua.Call(lua.Globals.Get("move")).Boolean);
        lua.Globals["open"] = false;
        Assert.False(lua.Call(lua.Globals.Get("move")).Boolean);
        Assert.Equal(1, lua.Globals.Get("attempts").Number);
    }

    [Fact]
    public void Clearing_multiple_maps_yields_to_bank_as_soon_as_pods_reach_threshold()
    {
        var lua = Load(new ExpeditionProfile("test", "10;11", BankMap: 20, StopAtPodsPercent: 80));
        lua.DoString("function fight() attempts = attempts + 1; pods = 80; return attempts < 4 end");
        Assert.True(lua.Call(lua.Globals.Get("move")).Boolean);
        Assert.Equal(1, lua.Globals.Get("attempts").Number);
    }

    [Fact]
    public void Single_map_rechecks_time_limit_after_empty_map_poll()
    {
        var lua = Load(new ExpeditionProfile("test", "10", MaxMinutes: 1));
        // Replace the sandbox clock before regenerating the script so its start is deterministic.
        lua.DoString("now = 100; os.time = function() return now end");
        lua.DoString(ExpeditionScript.Build(new ExpeditionProfile("test", "10", MaxMinutes: 1)));
        Assert.True(lua.Call(lua.Globals.Get("move")).Boolean);
        lua.Globals["now"] = 160;
        Assert.False(lua.Call(lua.Globals.Get("move")).Boolean);
        Assert.Equal(1, lua.Globals.Get("attempts").Number);
    }

    [Fact]
    public void Multi_map_loop_stops_before_an_extra_fight_at_limit()
    {
        var lua = Load(new ExpeditionProfile("test", "10;11", MaxFights: 2));
        lua.DoString("function fight() attempts = attempts + 1; fights = fights + 1; return true end");
        Assert.True(lua.Call(lua.Globals.Get("move")).Boolean);
        Assert.Equal(2, lua.Globals.Get("attempts").Number);
        Assert.False(lua.Call(lua.Globals.Get("move")).Boolean);
        Assert.Equal(2, lua.Globals.Get("attempts").Number);
    }

    [Fact]
    public void Nearest_order_handles_full_integer_coordinate_range()
    {
        var maps = new (long, (int, int)? )[] { (1, (int.MinValue, int.MinValue)), (2, (1, 1)) };
        Assert.Equal(new long[] { 2, 1 }, ExpeditionScript.NearestOrder(maps, (int.MaxValue, int.MaxValue)));
    }

    [Fact]
    public void Previously_empty_map_is_checked_on_every_circuit_for_new_eligible_groups()
    {
        var lua = Load(new ExpeditionProfile("test", "10;11"));
        lua.DoString("""
            current, visits, secondCleared, firstWon = 0, 0, false, false
            function travelTo(m)
                current = m
                if m == 10 then visits = visits + 1 end
                secondCleared = false
                return true
            end
            function fight()
                if current == 11 and not secondCleared then secondCleared = true; return true end
                if current == 10 and visits == 4 and not firstWon then firstWon = true; fights = fights + 1; return true end
                return false, 'no_matching_group'
            end
            """);

        for (int visit = 0; visit < 7; visit++) lua.Call(lua.Globals.Get("move"));

        Assert.Equal(1, lua.Globals.Get("fights").Number);
        Assert.Equal(4, lua.Globals.Get("visits").Number);
    }
}
