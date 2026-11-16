extern alias F06App;
using System.Reflection;
using System.Runtime.CompilerServices;
using MoonSharp.Interpreter;
using GivreClone.App.Services;
using GivreClone.Protocol;
using Xunit;
using Progress = F06App::GivreClone.App.Services.ExpeditionRouteProgress;
using Launch = F06App::GivreClone.App.Services.LaunchService;

namespace GivreClone.Tests;

public sealed class ExpeditionRouteRegressionTests
{
    [Fact]
    public void Single_map_route_keeps_its_destination_after_the_approach()
    {
        Assert.Equal(new long[] { 1, 2, 10 }, ExpeditionScript.TraversedMaps(new long[] { 10 }, 1,
            (_, _) => new long[] { 2, 10 }));
    }

    [Fact]
    public void Traversed_maps_include_approach_and_closing_leg_without_losing_repeated_visits()
    {
        var legs = new Dictionary<(long, long), IReadOnlyList<long>>
        {
            [(1, 10)] = new long[] { 2, 10 },
            [(10, 20)] = new long[] { 11, 20 },
            [(20, 10)] = new long[] { 11, 10 }
        };
        var unreachableLegs = new List<(long From, long To)>();

        var maps = ExpeditionScript.TraversedMaps(new long[] { 10, 20 }, 1,
            (from, to) => legs.GetValueOrDefault((from, to), Array.Empty<long>()), unreachableLegs);

        Assert.Equal(new long[] { 1, 2, 10, 11, 20, 11 }, maps);
        Assert.Empty(unreachableLegs);
    }

    [Fact]
    public void Disconnected_components_retain_targets_and_report_missing_legs_including_loop_closure()
    {
        var graph = new WorldGraph(new Dictionary<int, IReadOnlyList<MapTransition>>
        {
            [1] = new[] { new MapTransition(1, 10, 0, 1) },
            [10] = new[] { new MapTransition(10, 1, 0, 1) },
            [20] = new[] { new MapTransition(20, 21, 0, 1) },
            [21] = new[] { new MapTransition(21, 20, 0, 1) }
        });
        var unreachableLegs = new List<(long From, long To)>();

        var maps = ExpeditionScript.TraversedMaps(new long[] { 10, 20, 21 }, 1,
            (from, to) => graph.PathToMap((int)from, (int)to).Select(t => (long)t.Arrival).ToArray(), unreachableLegs);

        Assert.Equal(new long[] { 1, 10, 20, 21 }, maps);
        Assert.Equal(new (long From, long To)[] { (10, 20), (21, 10) }, unreachableLegs);
    }

    [Fact]
    public void Same_layer_interior_first_is_replaced_without_reordering_other_coordinates()
    {
        var maps = new (long, (int, int)?, int?)[]
        { (10, (4, -19), 1), (20, (5, -19), 1), (11, (4, -19), 1), (12, (4, -19), 2) };
        Assert.Equal(new long[] { 11, 20 }, ExpeditionScript.CleanMaps(maps, walkScore: id => id == 11 ? 20002 : id == 12 ? 90009 : 1));
        Assert.Equal(new long[] { 10, 20 }, ExpeditionScript.CleanMaps(maps));
        Assert.Equal(new long[] { 10, 20 }, ExpeditionScript.CleanMaps(maps, walkScore: _ => 0));
    }

    [Fact]
    public void Production_score_prefers_walking_or_scroll_exterior_over_door_only_interior()
    {
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        var graph = new WorldGraph(new Dictionary<int, IReadOnlyList<MapTransition>>
        {
            [10] = Enumerable.Range(0, 12).Select(i => new MapTransition(i, 100 + i, -1, 32)).ToArray(),
            [11] = [new(10, 100, 0, 1)],
            [12] = [new(11, 101, 2, 2)]
        });
        typeof(Launch).GetField("_worldGraph", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(service, graph);
        Assert.True(service.ExpeditionWalkScore(11) > service.ExpeditionWalkScore(10));
        Assert.True(service.ExpeditionWalkScore(12) > service.ExpeditionWalkScore(10));
        Assert.Equal(0, service.ExpeditionWalkScore(99));
    }

    private static Script Simulation(string maps = "101;102", string setup = "")
    {
        var script = new Script();
        script.DoString(@"
            visits, messages, errors = {}, {}, {}
            current, pods, wins, remaining, minute = 0, 0, 0, 0, 1000
            os.time = function() return minute end
            config = setmetatable({}, {__index=function() return function() end end})
            function getCountFight() return wins end
            function getPodsPercent() return pods end
            function travelTo(target) current=target table.insert(visits, target) return true end
            function fight() if remaining>0 then remaining=remaining-1 wins=wins+1 return true end return false, 'no_matching_group' end
            function printMessage(m) table.insert(messages,m) end
            function printWarning(m) table.insert(messages,m) end
            function printSuccess(m) table.insert(messages,m) end
            function printError(m) table.insert(errors,m) end
            function delay(ms) end
            combat={lastFightResult=function() return {lost=false} end}
        " + setup);
        script.DoString(ExpeditionScript.Build(new ExpeditionProfile("Test", maps)));
        return script;
    }
    private static void Move(Script s, int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            var coroutine = s.CreateCoroutine(s.Globals.Get("move")).Coroutine;
            coroutine.AutoYieldCounter = 100_000;
            var result = coroutine.Resume();
            Assert.Equal(CoroutineState.Dead, coroutine.State); // Fail a runaway simulation without hanging the testhost.
            Assert.True(result.Boolean);
        }
    }

    [Fact]
    public void Empty_maps_are_revisited_each_circuit_without_stopping_an_all_empty_route()
    {
        var s = Simulation();
        // Exactly one successful fight per productive visit, then no eligible group.
        s.DoString("lastWon=0 function fight() if current==102 and lastWon~=#visits then lastWon=#visits wins=wins+1 return true end return false, 'no_matching_group' end");
        Move(s, 9);
        Assert.Equal(new double[] { 101, 102, 101, 102, 101, 102, 101, 102, 101 },
            s.Globals.Get("visits").Table.Values.Select(x => x.Number));
        Assert.Equal(0, s.Globals.Get("errors").Table.Length);
        var empty = Simulation();
        Move(empty, 30);
        Assert.Equal(30, empty.Globals.Get("visits").Table.Length);
        Assert.Equal(0, empty.Globals.Get("errors").Table.Length);
    }

    [Fact]
    public void A_successful_fight_resets_the_empty_streak_and_single_map_never_goes_on_rest()
    {
        var s = Simulation();
        Move(s, 4);
        s.Globals["remaining"] = 1;
        Move(s, 4);
        Assert.DoesNotContain(s.Globals.Get("messages").Table.Values, x => x.String.Contains("sur 101 après 3"));
        var single = Simulation("101");
        Move(single, 12);
        Assert.Equal(12, single.Globals.Get("visits").Table.Length);
        Assert.DoesNotContain(single.Globals.Get("messages").Table.Values, x => x.String.Contains("repos"));
    }

    [Fact]
    public void Persistent_travel_failure_keeps_the_expedition_running_after_three_attempts()
    {
        // Un trajet refuse en boucle ne termine PLUS l'expedition : Shadowbot et Bastionbot sont restes bloques sur la carte
        // 209192199, dont aucune cellule de transition n'est decodee, et leurs scripts se sont arretes (12:38:27).
        // Les 3 essais et la trace de la raison sont conserves ; le tour rend la main pour tenter la carte suivante.
        var s = Simulation(setup: "function travelTo(target) table.insert(visits,target) return false, 'movement_refused' end");
        Assert.True(s.Call(s.Globals.Get("move")).Boolean);
        Assert.Equal(new double[] { 101, 101, 101 }, s.Globals.Get("visits").Table.Values.Select(x => x.Number));
        Assert.Equal(1, s.Globals.Get("errors").Table.Length);
        Assert.Contains("movement_refused", s.Globals.Get("errors").Table.Get(1).String);
    }

    [Fact]
    public void Temporary_travel_failure_does_not_permanently_exclude_a_map()
    {
        var s = Simulation(setup: """
            firstAttempts=0
            function travelTo(target)
                table.insert(visits,target)
                if target==101 then
                    firstAttempts=firstAttempts+1
                    if firstAttempts<=2 then return false end
                end
                current=target return true
            end
            function fight()
                if current==101 and wins==0 then wins=1 return true end
                return false, 'no_matching_group'
            end
            """);

        Move(s);

        Assert.Equal(1, s.Globals.Get("wins").Number);
        Assert.Equal(3, s.Globals.Get("firstAttempts").Number);
        Assert.Equal(new double[] { 101, 101, 101 }, s.Globals.Get("visits").Table.Values.Select(x => x.Number));
    }

    [Theory]
    [InlineData("map_not_ready")]
    [InlineData("engagement_refused")]
    public void Temporary_fight_failure_retries_the_action_before_advancing(string reason)
    {
        var s = Simulation(setup: $"""
            attacks=0
            function fight()
                attacks=attacks+1
                if attacks<=2 then return false, '{reason}' end
                if attacks==3 then wins=wins+1 return true end
                return false, 'no_matching_group'
            end
            """);
        Move(s);
        Assert.Equal(1, s.Globals.Get("wins").Number);
        Assert.Equal(4, s.Globals.Get("attacks").Number);
        Assert.Equal(101, Assert.Single(s.Globals.Get("visits").Table.Values).Number);
        Move(s);
        Assert.Equal(102, s.Globals.Get("visits").Table.Get(2).Number);
    }

    [Theory]
    [InlineData("101")]
    [InlineData("101;102")]
    public void Persistent_fight_failure_keeps_the_expedition_running_without_skipping_the_intended_map(string maps)
    {
        // Un combat indisponible en boucle ne termine PLUS l'expedition : Shadowbot s'arretait apres 3 essais sur une
        // seule carte (« attempts=3 reason=action_failed »). Le tour rend la main, la carte visee n'est pas sautee,
        // et la raison reste tracee.
        var s = Simulation(maps, "attacks=0 function fight() attacks=attacks+1 return false, 'map_not_ready' end");
        Assert.True(s.Call(s.Globals.Get("move")).Boolean);
        Assert.Equal(3, s.Globals.Get("attacks").Number);
        Assert.Single(s.Globals.Get("visits").Table.Values);
        Assert.Contains("map_not_ready", Assert.Single(s.Globals.Get("errors").Table.Values).String);
    }

    [Theory]
    [InlineData("[expedition-route] 2/5 map=191104000", true)]
    [InlineData("[expedition-route] 1/2 map=-5,19", true)]
    [InlineData("[expedition-route] 0/5 map=1", false)]
    [InlineData("[expedition-route] 6/5 map=1", false)]
    [InlineData("[expedition-route] 2/5 map=0", false)]
    [InlineData("[expedition-route] 2/5 map=9999999999999999999", false)]
    [InlineData("prefix [expedition-route] 2/5 map=1", false)]
    public void Progress_is_bounded_and_requires_the_exact_generated_marker(string message, bool accepted)
        => Assert.Equal(accepted, Progress.TryParse(message, out _));
}
