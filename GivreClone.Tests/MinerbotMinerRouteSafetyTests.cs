using MoonSharp.Interpreter;
using Xunit;

namespace GivreClone.Tests;

public sealed class MinerbotMinerRouteSafetyTests
{
    private static Script Load()
    {
        var directory = MappingScan.WalkUp(d => File.Exists(Path.Combine(d.FullName, "scripts", "Mineur-H24-Minerbot-Vaincra.lua")));
        var lua = new Script();
        lua.DoString("""
            now=1000
            os.time=function() return now end
            function printMessage(...) end
            function printWarning(...) end
            function printError(...) end
            function printSuccess(...) end
            function delay(ms) waited=ms end
            function getCharacterName() return 'Minerbot-Vaincra' end
            """);
        lua.DoString(File.ReadAllText(Path.Combine(directory!.FullName, "scripts", "Mineur-H24-Minerbot-Vaincra.lua")) + """

            TELEMETRY_PERSIST=false
            __test={}
            function __test.finish(resources,value,reason)
                startRouteRun('Hurlement')
                sessionResources=sessionResources+resources
                sessionValue=sessionValue+value
                finalizeRouteRun(reason)
                return routeCooldown.Hurlement
            end
            function __test.chooseAllResting()
                MINER_LEVEL=146
                for name,_ in pairs(ROUTES) do routeCooldown[name]=now+300 end
                chooseRoute()
                return currentRoute
            end
            function __test.chooseWithOnlyHurlementReady()
                MINER_LEVEL=146
                for name,_ in pairs(ROUTES) do routeCooldown[name]=now+300 end
                routeCooldown.Hurlement=now
                chooseRoute()
                return currentRoute
            end
            """);
        return lua;
    }

    [Fact]
    public void Empty_circuit_rests_for_five_minutes()
    {
        var lua = Load();
        Assert.Equal(1300, lua.DoString("return __test.finish(0,0,'exit')").Number);
    }

    [Theory]
    [InlineData(1, 0, "exit")]
    [InlineData(0, 1, "exit")]
    [InlineData(0, 0, "bank")]
    public void Productive_or_interrupted_circuit_is_not_mislabeled_empty(int resources, int value, string reason)
    {
        var lua = Load();
        Assert.True(lua.DoString($"return __test.finish({resources},{value},'{reason}')").IsNil());
    }

    [Fact]
    public void All_routes_resting_wait_without_choosing_or_traveling()
    {
        var lua = Load();
        Assert.True(lua.DoString("return __test.chooseAllResting()").IsNil());
        Assert.Equal(15000, lua.Globals.Get("waited").Number);
    }

    [Fact]
    public void Route_is_eligible_again_at_the_end_of_its_rest()
    {
        var lua = Load();
        Assert.Equal("Hurlement", lua.DoString("return __test.chooseWithOnlyHurlementReady()").String);
    }

    [Theory]
    [InlineData("move")]
    [InlineData("bank")]
    public void Wrong_character_cannot_mine_or_use_bank(string function)
    {
        var lua = Load();
        lua.DoString("function getCharacterName() return 'Templarbot' end");
        Assert.False(lua.Call(lua.Globals.Get(function)).Boolean);
    }

    [Theory]
    [InlineData("nil")]
    [InlineData("{won=false}")]
    public void Loss_or_unknown_result_stops_instead_of_restarting(string result)
    {
        var lua = Load();
        lua.DoString("function stopScript() stopped=true end");
        lua.DoString($"onFightEnd({result})");
        Assert.True(lua.Globals.Get("stopped").Boolean);
    }
}
