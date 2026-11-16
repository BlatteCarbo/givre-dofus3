using MoonSharp.Interpreter;
using Xunit;

namespace GivreClone.Tests;

public sealed class TemplarbotTempleSafetyTests
{
    private static Script Load(string setup = "")
    {
        var directory = MappingScan.WalkUp(d => File.Exists(Path.Combine(d.FullName, "scripts", "Templarbot-Temple-Introduction.lua")));
        var lua = new Script(CoreModules.Preset_SoftSandbox);
        lua.DoString("""
            name,server,current='Templarbot','Kourial',241443329
            function getCharacterName() return name end
            function getServerName() return server end
            function getMapId() return current end
            function isInFight() return false end
            function print(s) end
            function printWarning(s) end
            function printError(s) end
            function delay(ms) end
            quest={}
            function quest:loaded() return true end
            function quest:isFinished(id) return false end
            function quest:isActive(id) return false end
            function quest:questObjectiveDone(id,objective) return false end
            function quest:refresh(id) end
            function quest:objectives(id) return {} end
            map={}
            function map:getNpcs() return {{npcId=7581}} end
            npc={}
            function npc:npc(id,action) opened=true return true end
            function npc:question() return {messageId=1,replies={999},text='unknown'} end
            function npc:hasReply(id) return false end
            function npc:reply(id) replied=id return true end
            function npc:leaveDialog() left=true end
            """);
        lua.DoString(setup);
        lua.DoString(File.ReadAllText(Path.Combine(directory!.FullName, "scripts", "Templarbot-Temple-Introduction.lua")));
        return lua;
    }

    [Theory]
    [InlineData("name='Foragerbot'")]
    [InlineData("server='Mikhal'")]
    [InlineData("current=192415750")]
    public void Identity_and_map_guards_precede_any_action(string setup)
    {
        var lua = Load(setup);
        Assert.False(lua.Call(lua.Globals.Get("move")).Boolean);
        Assert.True(lua.Globals.Get("opened").IsNil());
    }

    [Fact]
    public void Unknown_dialogue_is_closed_without_guessing_a_reply()
    {
        var lua = Load();
        Assert.False(lua.Call(lua.Globals.Get("move")).Boolean);
        Assert.True(lua.Globals.Get("left").Boolean);
        Assert.True(lua.Globals.Get("replied").IsNil());
    }

    [Fact]
    public void Known_reply_is_sent_once_and_no_scene_is_assumed_complete()
    {
        var lua = Load("function npc:hasReply(id) return id==77751 end");
        Assert.False(lua.Call(lua.Globals.Get("move")).Boolean);
        Assert.Equal(77751, lua.Globals.Get("replied").Number);
        Assert.True(lua.Globals.Get("left").Boolean);
    }

    [Fact]
    public void Unknown_quest_state_has_a_finite_wait_and_no_npc_action()
    {
        var lua = Load("function quest:loaded() return false end");
        for (int i = 0; i < 4; i++) Assert.True(lua.Call(lua.Globals.Get("move")).Boolean);
        Assert.False(lua.Call(lua.Globals.Get("move")).Boolean);
        Assert.True(lua.Globals.Get("opened").IsNil());
    }

    [Fact]
    public void Unexpected_fight_stops_before_the_next_dialogue()
    {
        var lua = Load();
        lua.DoString("onFightEnd({won=true})");
        Assert.False(lua.Call(lua.Globals.Get("move")).Boolean);
        Assert.True(lua.Globals.Get("opened").IsNil());
    }

    [Fact]
    public void Last_reply_without_a_new_question_still_refreshes_the_quest()
    {
        var lua = Load("""
            function npc:hasReply(id) return id==77751 end
            function npc:reply(id) return false end
            function quest:refresh(id) refreshed=id end
            """);
        Assert.False(lua.Call(lua.Globals.Get("move")).Boolean);
        Assert.Equal(1631, lua.Globals.Get("refreshed").Number);
    }

    [Fact]
    public void Completed_objectives_allow_only_the_confirmed_next_room()
    {
        var lua = Load("""
            function quest:isActive(id) return true end
            function quest:questObjectiveDone(id,objective) return true end
            function travelTo(id) traveled=id current=id return true end
            """);
        Assert.False(lua.Call(lua.Globals.Get("move")).Boolean);
        Assert.Equal(241444353, lua.Globals.Get("traveled").Number);
        Assert.True(lua.Globals.Get("opened").Boolean);
    }

    [Fact]
    public void Accepted_travel_without_arrival_cannot_open_the_next_dialogue()
    {
        var lua = Load("""
            function quest:isActive(id) return true end
            function quest:questObjectiveDone(id,objective) return true end
            function travelTo(id) return true end
            """);
        Assert.False(lua.Call(lua.Globals.Get("move")).Boolean);
        Assert.True(lua.Globals.Get("opened").IsNil());
    }
}
