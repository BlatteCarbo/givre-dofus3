using MoonSharp.Interpreter;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class ExpeditionCompletionRegressionTests
{
    private static Script World()
    {
        var s = new Script(CoreModules.Preset_SoftSandbox);
        s.DoString("""
            here, now, attempts, exits, deposits, travels = 11, 100, 0, 0, 0, 0
            result = nil
            os.time = function() return now end
            config = setmetatable({}, { __index = function() return function() end end })
            combat = { lastFightResult = function() return result end }
            map = { currentMapId = function() return here end, leaveDungeon = function() exits = exits + 1; here = 10; return true, 10 end }
            dungeon = { exit = function() exits = exits + 1; here = 10 end }
            function fight() attempts = attempts + 1; return false, 'no_matching_group' end
            function getCountFight() return 0 end
            function getInventoryItemCount() return 1 end
            function getPodsPercent() return 0 end
            function travelTo(m) travels = travels + 1; here = m return true end
            function delay(ms) end
            function printMessage(s) end
            function printWarning(s) end
            function printSuccess(s) end
            function printError(s) end
            npc = { npcBank = function() return true end }
            bank = { close = function() return true end }
            exchange = { putAllItems = function() deposits = deposits + 1 return false end }
            """);
        return s;
    }
    private static bool Move(Script s) => s.Call(s.Globals.Get("move")).Boolean;
    private static void Dungeon(Script s, int minutes = 0, long bank = 0) => s.DoString(
        ExpeditionScript.BuildDungeon(new DungeonProfile("test", new long[] { 11 }, 10,
            Array.Empty<(long, int, string)>(), Runs: 1, MaxMinutes: minutes,
            StopAtPodsPercent: bank > 0 ? 80 : 0, BankMap: bank)));

    [Fact]
    public void Leaving_an_empty_boss_room_does_not_count_as_a_won_run()
    {
        var s = World(); Dungeon(s);
        Assert.True(Move(s)); // empty boss room -> exit
        Assert.True(Move(s)); // must enter again, not stop at Runs=1
        Assert.Equal(11, s.Globals.Get("here").Number);
    }

    [Fact]
    public void Only_a_new_confirmed_boss_victory_completes_the_run()
    {
        var s = World(); Dungeon(s);
        s.DoString("function fight() attempts = attempts + 1; if attempts == 1 then result = {won=true, fightId=2}; return true end return false end");
        Assert.True(Move(s)); // victory
        Assert.True(Move(s)); // empty room -> exit
        Assert.False(Move(s)); // limit reached outside
    }

    [Fact]
    public void Previous_victory_cannot_complete_another_boss_run()
    {
        var s = World(); Dungeon(s);
        s.DoString("result = {won=true, fightId=2}; function fight() attempts=attempts+1; return attempts == 1 end");
        Assert.True(Move(s));
        Assert.True(Move(s));
        Assert.True(Move(s));
    }

    [Fact]
    public void Time_limit_is_observed_inside_the_dungeon_before_another_fight()
    {
        var s = World(); Dungeon(s, minutes: 1);
        s.Globals["now"] = 160;
        Assert.False(Move(s));
        Assert.Equal(0, s.Globals.Get("attempts").Number);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Confirmed_defeat_stops_expedition_or_dungeon(bool dungeon)
    {
        var s = World();
        if (dungeon) Dungeon(s);
        else s.DoString(ExpeditionScript.Build(new ExpeditionProfile("test", "11;12")));
        s.DoString("function fight() attempts=attempts+1; result={lost=true, fightId=3}; return true end");
        Assert.False(Move(s));
        Assert.Equal(1, s.Globals.Get("attempts").Number);
    }

    // Réglage « continuer après une défaite » : plus de Phénix en jeu, le perso repart directement -> aucune défaite n'arrête le script.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Continue_after_death_never_stops_on_defeats(bool dungeon)
    {
        var s = World();
        if (dungeon) s.DoString(ExpeditionScript.BuildDungeon(new DungeonProfile("test", new long[] { 11 }, 10,
            Array.Empty<(long, int, string)>(), ContinueAfterDeath: true)));
        else s.DoString(ExpeditionScript.Build(new ExpeditionProfile("test", "11;12", ContinueAfterDeath: true, MaxConsecutiveDefeats: 0)));
        s.DoString("""
            outcomes = { 'lost', 'lost', 'won', 'lost', 'lost', 'lost' }
            function fight() attempts = attempts + 1; local o = outcomes[attempts]
                if not o then return false, 'no_matching_group' end
                result = { lost = o == 'lost', won = o == 'won', fightId = attempts }; here = 11; return true end
            """);
        int moves = 0;
        while (moves < 20 && Move(s)) moves++;
        Assert.Equal(20, moves);                              // jamais arrêté par une défaite
        Assert.Equal(3, s.Globals.Get("DEFEATS").Number);     // série en cours : la victoire du 3e combat a remis à zéro
    }

    [Fact]
    public void Continue_after_death_is_saved_and_old_lines_default_to_stop()
    {
        var st = new ExpeditionState(4, 77, 2, 5, new[] { 31 }, new int[0], 30, 45, 90, Bank: true, Hdv: true, ContinueAfterDeath: true);
        Assert.True(ExpeditionState.Parse(st.Serialize())!.ContinueAfterDeath);
        Assert.False(ExpeditionState.Parse("4|77|2|5|31||30|45|90|1|0|1|0|1")!.ContinueAfterDeath);   // ligne à 14 champs
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 3)]
    public void Expedition_continue_and_defeat_limit_roundtrip_exactly(bool continueAfterDeath, int maxConsecutiveDefeats)
    {
        var original = new ExpeditionState(4, 77, 1, 8, Array.Empty<int>(), Array.Empty<int>(), 20, 40, 95,
            ContinueAfterDeath: continueAfterDeath, MaxConsecutiveDefeats: maxConsecutiveDefeats);
        var restored = ExpeditionState.Parse(original.Serialize());
        Assert.NotNull(restored);
        Assert.Equal(continueAfterDeath, restored.ContinueAfterDeath);
        Assert.Equal(maxConsecutiveDefeats, restored.MaxConsecutiveDefeats);
    }

    [Fact]
    public void Unconfirmed_bank_deposit_cannot_allow_another_dungeon_run()
    {
        var s = World(); Dungeon(s, bank: 20);
        s.DoString("here=10; function getPodsPercent() return 90 end");
        Assert.False(Move(s));
        Assert.Equal(1, s.Globals.Get("deposits").Number);
        Assert.Equal(0, s.Globals.Get("attempts").Number);
    }

    [Fact]
    public void Empty_catalog_stops_before_travel_or_engagement()
    {
        var s = World();
        s.DoString(ExpeditionScript.Build(new ExpeditionProfile("empty", "")));
        Assert.False(Move(s));
        Assert.Equal(0, s.Globals.Get("travels").Number);
        Assert.Equal(0, s.Globals.Get("attempts").Number);
    }

    [Theory]
    [InlineData("11")]
    [InlineData("11;12")]
    public void Pods_at_the_configured_limit_stop_before_travel(string maps)
    {
        var s = World();
        s.DoString(ExpeditionScript.Build(new ExpeditionProfile("pods", maps, StopAtPodsPercent: 80)));
        s.DoString("function getPodsPercent() return 80 end");
        Assert.False(Move(s));
        Assert.Equal(0, s.Globals.Get("travels").Number);
        Assert.Equal(0, s.Globals.Get("attempts").Number);
    }

    [Fact]
    public void Three_attempts_without_successful_travel_keep_the_route_alive()
    {
        // Trois essais de trajet infructueux rendent la main (carte suivante au tour d'apres) au lieu de terminer la
        // route : une carte sans cellule de transition decodee arretait l'expedition de Shadowbot et de Bastionbot.
        var s = World();
        s.DoString(ExpeditionScript.Build(new ExpeditionProfile("unreachable", "11;12")));
        s.DoString("function travelTo(m) travels=travels+1 return false end");
        Assert.True(Move(s));
        Assert.Equal(3, s.Globals.Get("travels").Number);
        Assert.Equal(0, s.Globals.Get("attempts").Number);
    }

    [Fact]
    public void No_eligible_group_still_observes_the_time_limit()
    {
        var s = World();
        s.DoString(ExpeditionScript.Build(new ExpeditionProfile("no-group", "11;12", MaxMinutes: 1)));
        Assert.True(Move(s));
        Assert.True(Move(s));
        s.Globals["now"] = 160;
        Assert.False(Move(s));
        Assert.Equal(2, s.Globals.Get("attempts").Number);
        Assert.Equal(2, s.Globals.Get("travels").Number);
    }
}
