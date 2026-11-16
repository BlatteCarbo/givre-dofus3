using MoonSharp.Interpreter;
using GivreClone.App.Services.Treasure;
using Xunit;
namespace GivreClone.Tests;
public sealed class TreasureScriptExecutionTests
{
    private static Script Host()
    {
        var host = new Script(CoreModules.Preset_SoftSandbox);
        host.DoString("""
            current = 100
            hunting = false
            starts, flags, moves, quits = 0, 0, 0, 0
            function print(...) end
            function delay(...) end
            function stopScript() error("unexpected stop") end
            function getMapId() return current end
            function travelTo(...) return false end
            function changeMap(...) moves = moves + 1; return moves > 1 end
            treasure = {}
            function treasure:isHunting() return hunting end
            function treasure:startHunt(...) starts = starts + 1; return 1 end
            function treasure:isReadyToDig() return false end
            function treasure:isFightStep() return false end
            function treasure:stepType() return "direction" end
            function treasure:getNextFlagName() return "" end
            function treasure:getNextFlagDirection() return "right" end
            function treasure:getNextFlagMapCount() return 2 end
            function treasure:getStartMapId() return 100 end
            function treasure:huntFlag() flags = flags + 1; return true end
            function treasure:quit() quits = quits + 1 end
            function treasure:giveUpCooldown() return 0 end
            """);
        host.DoString(TreasureScript.Build(120, 4));
        return host;
    }
    [Fact]
    public void Failed_return_to_chest_never_requests_a_hunt_on_the_wrong_map()
    {
        var host = Host();
        host.Call(host.Globals.Get("move"));
        Assert.Equal(1, host.Globals.Get("starts").Number);
        host.Globals["current"] = 200;
        host.Call(host.Globals.Get("move"));
        Assert.Equal(1, host.Globals.Get("starts").Number);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void Unconfirmed_abandon_stops_instead_of_resetting_and_continuing(bool quitResult, bool remainsHunting, bool stops)
    {
        var host = Host();
        host.Globals["hunting"] = true;
        host.Globals["quitResult"] = quitResult;
        host.Globals["remainsHunting"] = remainsHunting;
        host.DoString("""
            cooldownChecks = 0
            function treasure:getNextFlagMapCount() return 0 end
            function treasure:quit()
                quits = quits + 1
                hunting = remainsHunting
                return quitResult
            end
            function treasure:giveUpCooldown() cooldownChecks = cooldownChecks + 1; return 0 end
            function stopScript() error("hunt stopped") end
            """);

        if (stops) Assert.Throws<ScriptRuntimeException>(() => host.Call(host.Globals.Get("move")));
        else host.Call(host.Globals.Get("move"));

        Assert.Equal(1, host.Globals.Get("quits").Number);
        Assert.Equal(stops ? 0 : 1, host.Globals.Get("cooldownChecks").Number);
        Assert.Equal(remainsHunting, host.Globals.Get("hunting").Boolean);
    }
    [Fact]
    public void Direction_step_requires_the_server_confirmed_number_of_successful_map_changes()
    {
        var host = Host();
        host.Globals["hunting"] = true;
        for (int i = 0; i < 3; i++) host.Call(host.Globals.Get("move"));
        Assert.Equal(0, host.Globals.Get("flags").Number);
        host.Call(host.Globals.Get("move"));
        Assert.Equal(1, host.Globals.Get("flags").Number);
        Assert.Equal(3, host.Globals.Get("moves").Number); // first move refused, not counted
        Assert.Equal(0, host.Globals.Get("quits").Number);
    }
    [Fact]
    public void Restart_mid_direction_returns_to_the_step_origin_before_counting_again()
    {
        var host = Host();
        host.Globals["hunting"] = true;
        host.Globals["current"] = 200;
        host.DoString("anchors = 0; function travelTo(map) anchors = anchors + 1; current = map; return true end");
        host.Call(host.Globals.Get("move"));
        Assert.Equal(1, host.Globals.Get("anchors").Number);
        Assert.Equal(100, host.Globals.Get("current").Number);
        Assert.Equal(0, host.Globals.Get("flags").Number);
        for (int i = 0; i < 3; i++) host.Call(host.Globals.Get("move"));
        Assert.Equal(1, host.Globals.Get("flags").Number);
    }

    [Fact]
    public void Restart_mid_phorreur_returns_to_the_step_origin_before_searching()
    {
        var host = Host();
        host.Globals["hunting"] = true;
        host.Globals["current"] = 200;
        host.DoString("""
            anchors = 0
            function treasure:stepType() return "hint" end
            function treasure:isPhorreurInMap() return false end
            function travelTo(map) anchors = anchors + 1; current = map; return true end
            """);

        host.Call(host.Globals.Get("move"));

        Assert.Equal(1, host.Globals.Get("anchors").Number);
        Assert.Equal(100, host.Globals.Get("current").Number);
        Assert.Equal(0, host.Globals.Get("quits").Number);
    }

    [Fact]
    public void Failed_phorreur_transitions_do_not_consume_the_search_distance()
    {
        var host = Host();
        host.Globals["hunting"] = true;
        host.DoString("""
            attempts = 0
            function treasure:stepType() return "hint" end
            function treasure:isPhorreurInMap() return false end
            function changeMap(...)
                attempts = attempts + 1
                moves = moves + 1
                return attempts % 4 ~= 0
            end
            """);

        for (int i = 0; i < 11; i++) host.Call(host.Globals.Get("move"));

        Assert.Equal(11, host.Globals.Get("moves").Number);
        Assert.Equal(0, host.Globals.Get("quits").Number);
    }

    [Theory]
    [InlineData("direction")]
    [InlineData("hint")]
    public void Missing_step_origin_stops_without_travel_or_abandon(string kind)
    {
        var host = Host();
        host.Globals["hunting"] = true;
        host.Globals["kind"] = kind;
        host.DoString("""
            stops, travels = 0, 0
            function stopScript() stops = stops + 1 end
            function treasure:stepType() return kind end
            function treasure:getStartMapId() return 0 end
            function treasure:isPhorreurInMap() return false end
            function travelTo(...) travels = travels + 1; return false end
            """);

        var result = host.Call(host.Globals.Get("move"));

        Assert.False(result.Boolean);
        Assert.Equal(1, host.Globals.Get("stops").Number);
        Assert.Equal(0, host.Globals.Get("travels").Number);
        Assert.Equal(0, host.Globals.Get("moves").Number);
        Assert.Equal(0, host.Globals.Get("quits").Number);
    }

    [Fact]
    public void Unreachable_clue_stops_without_consuming_candidates_or_abandoning()
    {
        var host = Host();
        host.Globals["hunting"] = true;
        host.DoString("""
            stops = 0
            function stopScript() stops = stops + 1 end
            function treasure:stepType() return "poi" end
            function treasure:getPos(...) return "[1,2]" end
            function treasure.findClues(...) return {{mapId=200,x=2,y=2,distance=1}} end
            """);

        var result = host.Call(host.Globals.Get("move"));

        Assert.False(result.Boolean);
        Assert.Equal(1, host.Globals.Get("stops").Number);
        Assert.Equal(0, host.Globals.Get("flags").Number);
        Assert.Equal(0, host.Globals.Get("quits").Number);
    }

    [Fact]
    public void Three_chest_start_refusals_stop_the_script_without_abandoning()
    {
        var host = Host();
        host.DoString("""
            stops = 0
            function stopScript() stops = stops + 1 end
            function treasure:startHunt() return 0 end
            """);
        Assert.Contains("startRefusals >= 3", TreasureScript.Build(0, 3));

        host.Call(host.Globals.Get("move"));
        host.Call(host.Globals.Get("move"));
        Assert.Equal(0, host.Globals.Get("stops").Number);
        Assert.Equal(0, host.Globals.Get("quits").Number);

        var third = host.Call(host.Globals.Get("move"));

        Assert.False(third.Boolean);
        Assert.Equal(1, host.Globals.Get("stops").Number);
        Assert.Equal(0, host.Globals.Get("quits").Number);
    }

    [Fact]
    public void A_start_cooldown_does_not_consume_the_three_refusal_cap()
    {
        var host = Host();
        host.DoString("""
            stops = 0
            function stopScript() stops = stops + 1 end
            function treasure:startHunt() return -2 end
            """);

        for (int i = 0; i < 3; i++) Assert.True(host.Call(host.Globals.Get("move")).Boolean);

        Assert.Equal(0, host.Globals.Get("stops").Number);
        Assert.Equal(0, host.Globals.Get("quits").Number);
    }
}
