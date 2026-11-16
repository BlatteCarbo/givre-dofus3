using MoonSharp.Interpreter;
using GivreClone.App.Services;
using GivreClone.App.Services.Mcp;
using Xunit;

namespace GivreClone.Tests;

public class ExpeditionDefeatStreakTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public void Positive_limit_stops_exactly_at_the_threshold(int limit)
    {
        string lua = ExpeditionScript.Build(new ExpeditionProfile("Test", "1", ContinueAfterDeath: true,
            MaxConsecutiveDefeats: limit));

        Assert.Contains($"if DEFEATS >= {limit} then", lua);
        Assert.Contains("reason=defeat_streak", lua);
        Assert.Null(SavedLuaScripts.SyntaxError(lua));

        var script = Load(lua);
        for (int i = 1; i < limit; i++) Assert.True(script.Call(script.Globals.Get("onDefeat")).Boolean);
        Assert.False(script.Call(script.Globals.Get("onDefeat")).Boolean);
        Assert.Equal(limit, script.Globals.Get("DEFEATS").Number);
        Assert.Contains("reason=defeat_streak", script.Globals.Get("errorMessage").String);
    }

    [Fact]
    public void Zero_omits_the_limit_and_keeps_continuing()
    {
        string lua = ExpeditionScript.Build(new ExpeditionProfile("Test", "1", ContinueAfterDeath: true,
            MaxConsecutiveDefeats: 0));

        Assert.DoesNotContain("if DEFEATS >=", lua);
        Assert.DoesNotContain("reason=defeat_streak", lua);
        Assert.Null(SavedLuaScripts.SyntaxError(lua));

        var script = Load(lua);
        for (int i = 0; i < 10; i++) Assert.True(script.Call(script.Globals.Get("onDefeat")).Boolean);
    }

    [Fact]
    public void Default_limit_is_three_and_unchecked_continuation_stops_on_first_loss()
    {
        var profile = new ExpeditionProfile("Test", "1");
        Assert.Equal(3, profile.MaxConsecutiveDefeats);
        string lua = ExpeditionScript.Build(profile);
        Assert.Null(SavedLuaScripts.SyntaxError(lua));
        Assert.False(Load(lua).DoString("return onDefeat()").Boolean);
    }

    [Fact]
    public void Victory_resets_the_streak_before_the_next_defeat()
    {
        var script = Load(ExpeditionScript.Build(new ExpeditionProfile("Test", "1", ContinueAfterDeath: true,
            MaxConsecutiveDefeats: 2)));
        script.DoString("""
            function getCountFight() return 0 end
            function getPodsPercent() return 0 end
            function travelTo() return true end
            lost = true
            function fight() return true end
            combat = { lastFightResult = function() return { lost = lost } end }
            """);

        Assert.True(script.DoString("return move()").Boolean);
        Assert.Equal(1, script.Globals.Get("DEFEATS").Number);
        Assert.True(script.DoString("lost = false return move()").Boolean);
        Assert.Equal(0, script.Globals.Get("DEFEATS").Number);
        Assert.True(script.DoString("lost = true return move()").Boolean);
        Assert.False(script.DoString("return move()").Boolean);
    }

    private static Script Load(string lua)
    {
        var script = new Script();
        script.DoString("""
            config = setmetatable({}, { __index = function() return function() end end })
            function printWarning() end
            function printError(message) errorMessage = message end
            function printMessage() end
            """);
        script.DoString(lua);
        return script;
    }
}
