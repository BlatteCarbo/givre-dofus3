using GivreClone.App.Services.Mcp;
using GivreClone.App.Services.Treasure;
using Xunit;

namespace GivreClone.Tests;

public class TreasureScriptTests
{
    [Fact]
    public void Generated_script_is_valid_lua_and_uses_the_resolver_and_the_hunt_verbs()
    {
        string code = TreasureScript.Build(120, 4);
        Assert.Null(SavedLuaScripts.SyntaxError(code));
        Assert.Contains("local HUNT_LEVEL = 120", code);
        Assert.Contains("local MAX_FAILS = 4", code);
        foreach (var verb in new[] { "treasure:startHunt(", "treasure:isHunting()", "treasure:isReadyToDig()", "treasure:isFightStep()", "treasure:huntDig()", "treasure:huntFlag()", "treasure:stepType()",
                                     "treasure:getNextFlagName()", "treasure:getNextFlagDirection()", "treasure:getStartMapId()", "treasure:getPos(", "treasure:isPhorreurInMap()", "treasure:quit()",
                                     "treasure.findClues(", "travelTo(", "changeMap(" })
            Assert.Contains(verb, code);
    }

    [Fact]
    public void Parameters_are_clamped()
    {
        string code = TreasureScript.Build(999, 0);
        Assert.Contains("local HUNT_LEVEL = 200", code);
        Assert.Contains("local MAX_FAILS = 1", code);
        Assert.Contains("local MAX_FAILS = 10", TreasureScript.Build(0, 99));
    }
}
