using MoonSharp.Interpreter;
using Xunit;

namespace GivreClone.Tests;

public sealed class BastionbotFarmSafetyTests
{
    private static string Source(string file)
    {
        var directory = MappingScan.WalkUp(d => File.Exists(Path.Combine(d.FullName, "scripts", file)));
        return File.ReadAllText(Path.Combine(directory!.FullName, "scripts", file));
    }

    [Theory]
    [InlineData("Bastionbot-Farm-Cycle-Controller.lua")]
    [InlineData("Bastionbot-Multi-Metiers-Cycle.lua")]
    public void Loss_or_unknown_result_stops_before_any_new_movement_or_commerce(string file)
    {
        var lua = new Script();
        lua.DoString("function print(s) end function getCharacterName() return 'Bastionbot' end function isInFight() return false end");
        lua.DoString(Source(file));
        lua.DoString("onFightEnd({lost=true})");
        Assert.False(lua.Call(lua.Globals.Get("move")).Boolean);
        lua.DoString(Source(file));
        lua.DoString("onFightEnd(nil)");
        Assert.False(lua.Call(lua.Globals.Get("move")).Boolean);
    }

    [Fact]
    public void Character_guard_refuses_the_wrong_account_before_actions()
    {
        var lua = new Script();
        lua.DoString("function printError(s) end function getCharacterName() return 'Other' end");
        lua.DoString(Source("Bastionbot-Multi-Metiers-Cycle.lua"));
        Assert.False(lua.Call(lua.Globals.Get("move")).Boolean);
    }
}
