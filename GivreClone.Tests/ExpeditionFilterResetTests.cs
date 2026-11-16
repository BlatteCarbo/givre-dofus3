using MoonSharp.Interpreter;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class ExpeditionFilterResetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generated_scripts_explicitly_reset_filters_left_in_the_lua_context(bool dungeon)
    {
        var lua = new Script();
        lua.DoString("""
            FORCE_MONSTERS={999}; FORBIDDEN_MONSTERS={888}; MONSTERS_AMOUNT={{777,1,1}}
            config={}
            function config:setMinMonsters(n) end
            function config:setMaxMonsters(n) end
            function config:setMandatoryMonsters(t) FORCE_MONSTERS=t end
            function config:setForbiddenMonsters(t) FORBIDDEN_MONSTERS=t end
            function config:setAmountOfSpecificMonsters(t) MONSTERS_AMOUNT=t end
            """);
        string script = dungeon ? ExpeditionScript.BuildDungeon(new DungeonProfile("test", new long[] { 10 }, 9, Array.Empty<(long, int, string)>()))
            : ExpeditionScript.Build(new ExpeditionProfile("test", "10"));
        lua.DoString(script);
        foreach (string global in new[] { "FORCE_MONSTERS", "FORBIDDEN_MONSTERS", "MONSTERS_AMOUNT" }) Assert.Empty(lua.Globals.Get(global).Table.Values);
    }

    [Fact]
    public void Oversized_amounts_are_reported_instead_of_throwing()
    {
        var ignored = new List<string>();
        Assert.Empty(ExpeditionScript.ParseAmounts("10:999999999999999 11:1-999999999999999", ignored));
        Assert.Equal(2, ignored.Count);
    }
}
