extern alias F06App;
using System.Reflection;
using MoonSharp.Interpreter;
using Host = F06App::GivreClone.App.Services.GivreLuaHost;
using Xunit;

namespace GivreClone.Tests;

public sealed class LuaAvailableDataBindingsTests
{
    private static object? Call(string name, params object?[] args)
    {
        try { return typeof(Host).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args); }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void Monster_amounts_sum_all_entries_of_the_same_type(int count, bool allowed)
    {
        GivreClone.Protocol.MonsterEntry[] monsters = [new(42, "a", 1), new(42, "a", count - 1), new(99, "b", 8)];
        var amounts = new Dictionary<int, (int Min, int Max)> { [42] = (2, 3), [100] = (1, 1) };
        Assert.Equal(allowed, Call("MonsterAmountsAllowed", monsters, amounts));
    }

    [Fact]
    public void Empty_monster_constraints_accept_a_known_composition()
        => Assert.Equal(true, Call("MonsterAmountsAllowed", Array.Empty<GivreClone.Protocol.MonsterEntry>(),
            new Dictionary<int, (int Min, int Max)>()));

    [Theory]
    [InlineData(0, 1d)]
    [InlineData(99, 1d)]
    [InlineData(100, 2d)]
    [InlineData(199, 2d)]
    [InlineData(200, 3d)]
    public void Cost_uses_base_threshold_inclusively(int current, double expected)
        => Assert.Equal(expected, Call("ReadCharacteristicCost", "[[0,1],[100,2],[200,3]]", current));

    [Fact]
    public void Cost_accounts_for_gain_without_inventing_a_class_formula()
        => Assert.Equal(0.5d, Call("ReadCharacteristicCost", "[[0,1,2]]", 50));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[[100,2]]")]
    [InlineData("[[0,1],[0,2]]")]
    [InlineData("[[100,2],[0,1]]")]
    [InlineData("[[0,0]]")]
    [InlineData("[[0,1,0]]")]
    [InlineData("[[0,1],[100,\"bad\"]]")]
    [InlineData("[[0,1],null]")]
    [InlineData("not json")]
    public void Unknown_or_invalid_cost_is_nil(string? json)
        => Assert.Null(Call("ReadCharacteristicCost", json, 0));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Speed_preserves_the_engine_enum(int speed)
        => Assert.Equal(speed, Convert.ToInt32(Call("ParseLuaFightSpeed", DynValue.NewNumber(speed))));

    [Theory]
    [InlineData(-1d)]
    [InlineData(4d)]
    [InlineData(1.5d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Invalid_speed_fails_before_any_profile_mutation(double speed)
        => Assert.Throws<ScriptRuntimeException>(() => Call("ParseLuaFightSpeed", DynValue.NewNumber(speed)));

    [Fact]
    public void Missing_speed_is_an_error()
        => Assert.Throws<ScriptRuntimeException>(() => Call("ParseLuaFightSpeed", DynValue.Nil));
}
