using GivreClone.App.Services.NoCode;
using Xunit;

namespace GivreClone.Tests;

public class NoCodeLuaGeneratorTests
{
    private static NoCodeRouteOptions Valid() => new(
        "4,-22:right\n5,-22:bottom\n5,-21:left\n4,-21:top",
        Gather: true, Fight: true, GatherElementIds: "38, 42",
        MaxPods: "90", MinMonsters: "2", MaxMonsters: "5");

    [Fact]
    public void Generates_declared_route_without_executing_or_interpolating_untrusted_code()
    {
        var result = NoCodeLuaGenerator.Generate(Valid());
        Assert.True(result.IsValid, result.Message);
        Assert.Contains("function move()", result.Lua);
        Assert.Contains("ELEMENTS_TO_GATHER = { 38, 42 }", result.Lua);
        Assert.Contains("MIN_MONSTERS = 2", result.Lua);
        Assert.Contains("MAX_MONSTERS = 5", result.Lua);
        Assert.Contains("{ map = \"4,-22\", path = \"right\", gather = true, fight = true }", result.Lua);
        Assert.Equal(4, result.Lua.Split("gather = true").Length - 1);
    }

    [Theory]
    [InlineData("4,-22:right\n6,-22:bottom")]
    [InlineData("4,-22:top\n5,-22:bottom")]
    [InlineData("4,-22:right\n4,-22:top")]
    [InlineData("4,-22:right\n4,-21:bottom")]
    [InlineData("4,-22:os.execute('x')")]
    [InlineData("4,-22:right\n5,-22:right\n4,-22:left")]
    [InlineData("0:right")]
    [InlineData("2147483648:right")]
    [InlineData("1.5,0:left")]
    [InlineData("4,-22:right -- injected")]
    public void Invalid_map_inputs_fail_closed(string maps)
    {
        var result = NoCodeLuaGenerator.Generate(Valid() with { Maps = maps });
        Assert.False(result.IsValid);
        Assert.Empty(result.Lua);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("90; print('injected')")]
    [InlineData("")]
    public void Invalid_pods_fail_closed(string pods)
    {
        Assert.False(NoCodeLuaGenerator.Generate(Valid() with { MaxPods = pods }).IsValid);
    }

    [Theory]
    [InlineData("38,38")]
    [InlineData("-1")]
    [InlineData("38, os.execute('x')")]
    [InlineData("")]
    public void Invalid_gather_ids_fail_closed(string ids)
    {
        Assert.False(NoCodeLuaGenerator.Generate(Valid() with { GatherElementIds = ids }).IsValid);
    }

    [Theory]
    [InlineData("6", "5")]
    [InlineData("0", "4")]
    [InlineData("1", "9")]
    [InlineData("1", "5); print('x')")]
    public void Invalid_fight_bounds_fail_closed(string min, string max)
    {
        Assert.False(NoCodeLuaGenerator.Generate(Valid() with { MinMonsters = min, MaxMonsters = max }).IsValid);
    }

    [Fact]
    public void Route_only_never_adds_resource_or_combat_filters()
    {
        var result = NoCodeLuaGenerator.Generate(Valid() with
        {
            Maps = "12345:right",
            Gather = false, Fight = false,
            GatherElementIds = "invalid", MinMonsters = "invalid", MaxMonsters = "invalid"
        });
        Assert.True(result.IsValid, result.Message);
        Assert.Contains("map = 12345", result.Lua);
        Assert.Contains("gather = false, fight = false", result.Lua);
        Assert.DoesNotContain("ELEMENTS_TO_GATHER =", result.Lua);
        Assert.DoesNotContain("MIN_MONSTERS =", result.Lua);
    }

    [Fact]
    public void Route_must_be_bounded()
    {
        var result = NoCodeLuaGenerator.Generate(Valid() with { Maps = string.Join("\n", System.Linq.Enumerable.Repeat("1:right", 25)) });
        Assert.False(result.IsValid);
        Assert.Empty(result.Lua);
    }
}
