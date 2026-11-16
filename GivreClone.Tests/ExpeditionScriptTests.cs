using System.Linq;
using System.Collections.Generic;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class ExpeditionScriptTests
{
    [Fact]
    public void Maps_accept_coordinates_and_ids_and_report_garbage()
    {
        var bad = new List<string>();
        var maps = ExpeditionScript.ParseMaps("-15, 39 ; 188745730\n4,-20;abc", bad);
        Assert.Equal(new[] { "\"-15,39\"", "188745730", "\"4,-20\"" }, maps);
        Assert.Equal(new[] { "abc" }, bad);
    }

    [Fact]
    public void Amounts_accept_range_and_single_value_and_clamp()
    {
        var a = ExpeditionScript.ParseAmounts("101:2-4 102:3 103:9-1");
        Assert.Equal((101L, 2, 4), a[0]);
        Assert.Equal((102L, 3, 3), a[1]);
        Assert.Equal((103L, 1, 8), a[2]);   // 9 borné à 8, bornes permutées
    }

    [Fact]
    public void Script_carries_the_filters_and_limits_and_warns_on_contradictions()
    {
        var w = new List<string>();
        var lua = ExpeditionScript.Build(new ExpeditionProfile("Test", "-15,39", MinMonsters: 5, MaxMonsters: 2,
            Mandatory: "55", Forbidden: "55 66", Amounts: "77:1-2", MaxFights: 30, MaxMinutes: 45, StopAtPodsPercent: 90), w);
        Assert.Contains("config:setMinMonsters(2)", lua);          // min/max permutés
        Assert.Contains("config:setMaxMonsters(5)", lua);
        Assert.Contains("config:setMandatoryMonsters({ 55 })", lua);
        Assert.Contains("config:setForbiddenMonsters({ 55, 66 })", lua);
        Assert.Contains("config:setAmountOfSpecificMonsters({ { 77, 1, 2 } })", lua);
        Assert.Contains("local MAX_FIGHTS = 30", lua);
        Assert.Contains("local STOP_PODS = 90", lua);
        Assert.Contains(w, x => x.Contains("obligatoire et interdit"));
    }

    [Fact]
    public void NearestOrder_walks_the_closest_map_first_and_parks_unknown_ones_last()
    {
        var maps = new List<(long, (int, int)?)> { (1, (10, 10)), (2, (0, 1)), (3, null), (4, (0, 3)) };
        Assert.Equal(new long[] { 2, 4, 1, 3 }, ExpeditionScript.NearestOrder(maps, (0, 0)));
    }

    [Fact]
    public void State_round_trips_and_rejects_garbage()
    {
        var s = new ExpeditionState(4, 77, 2, 5, new[] { 31, 34 }, new[] { 46 }, 30, 45, 90);
        var back = ExpeditionState.Parse(s.Serialize())!;
        Assert.Equal(s.Serialize(), back.Serialize());
        Assert.Equal(new[] { 31, 34 }, back.Mandatory);
        Assert.Null(ExpeditionState.Parse("n'importe quoi"));
    }

    [Fact]
    public void Empty_zone_is_reported()
    {
        var w = new List<string>();
        ExpeditionScript.Build(new ExpeditionProfile("", "zzz"), w);
        Assert.Contains(w, x => x.Contains("aucune carte valide"));
    }

    [Fact]
    public void Expedition_script_emits_economy_reporting_in_limit_reached()
    {
        var lua = ExpeditionScript.Build(new ExpeditionProfile("Test", "-15,39"));
        Assert.Contains("local function ecoReport()", lua);
        Assert.Contains("[economie] kamas/h=", lua);
        var normalized = lua.Replace("\r\n", "\n");
        Assert.Contains("local function limitReached()\n    ecoReport()", normalized);
    }
}

public class ExpeditionDungeonTests
{
    [Fact]
    public void CleanMaps_keeps_outdoor_maps_of_the_main_layer_once_per_coordinate()
    {
        var maps = new List<(long, (int, int)?, int?)>
        {
            (1, (0, 0), 1), (2, (0, 0), 1),   // intérieur à la même coordonnée : écarté
            (3, (1, 0), 1), (4, null, null),  // sans coordonnée : écartée
            (5, (2, 0), 2),                   // autre calque minoritaire : écartée
            (6, (3, 0), 1),
        };
        Assert.Equal(new long[] { 1, 3, 6 }, ExpeditionScript.CleanMaps(maps));
        Assert.Equal(new long[] { 7, 8 }, ExpeditionScript.CleanMaps(new List<(long, (int, int)?, int?)> { (7, null, null), (8, null, null) }));
    }

    [Fact]
    public void Interior_majority_cannot_replace_the_known_atlas_layer()
    {
        var maps = new List<(long, (int, int)?, int?)>
        {
            (192939016, (4, -16), -1), // Astrub shoemaker workshop, same coordinate as exterior
            (83886090, (4, -15), -1),
            (192414728, (5, -15), -1),
            (192937990, (6, -15), -1),
            (100, (7, -15), null),
            (188745222, (4, -16), 1),
            (191102980, (3, -17), 1),
        };
        Assert.Equal(new long[] { 188745222, 191102980 }, ExpeditionScript.CleanMaps(maps));
    }

    [Fact]
    public void Maps_without_an_atlas_layer_keep_the_existing_fallback()
    {
        var maps = new List<(long, (int, int)?, int?)>
        {
            (1, (0, 0), -1), (2, (0, 0), -1), (3, (1, 0), -1),
        };
        Assert.Equal(new long[] { 1, 3 }, ExpeditionScript.CleanMaps(maps));
    }

    [Fact]
    public void Bank_mode_replaces_the_pods_stop_by_a_bank_trip()
    {
        var lua = ExpeditionScript.Build(new ExpeditionProfile("Z", "1;2", StopAtPodsPercent: 85, BankMap: 191104002, BankDoor: 300, BankInside: 191105026));
        Assert.Contains("local STOP_PODS = 0", lua);
        Assert.Contains("MAX_PODS = 85", lua);
        Assert.Contains("function bank()", lua);
        Assert.Contains("{ map = \"191104002\", door = \"300\" }", lua);       // porte du bâtiment
        Assert.Contains("{ map = \"191105026\", npcBank = true }", lua);        // banquier à l'intérieur
        Assert.Contains("travelFailures[MAPS[idx]]", lua);   // cartes injoignables écartées
        Assert.Contains("unreachable[MAPS[idx]] = true", lua);

        var noBank = ExpeditionScript.Build(new ExpeditionProfile("Z", "1", StopAtPodsPercent: 85));
        Assert.Contains("local STOP_PODS = 85", noBank);
        Assert.DoesNotContain("function bank()", noBank);
    }

    [Fact]
    public void Dungeon_script_lists_rooms_keys_and_runs()
    {
        var w = new List<string>();
        var lua = ExpeditionScript.BuildDungeon(new DungeonProfile("Bouftous", new long[] { 11, 12, 12, 13 }, 10,
            new[] { (8320L, 1, "Clef d'l'antre") }, Runs: 3), w);
        Assert.Contains("local ROOMS = { 11, 12, 13 }", lua);
        Assert.Contains("local ENTRANCE = 10", lua);
        Assert.Contains("{ 8320, 1, 'Clef d\\'l\\'antre' }", lua);   // apostrophes échappées pour Lua
        Assert.Contains("local RUNS = 3", lua);
        Assert.DoesNotContain("function bank()", lua);   // jamais de bank() globale : le moteur l'appellerait en pleine salle
        Assert.Empty(w);
        ExpeditionScript.BuildDungeon(new DungeonProfile("", new long[0], 0, new (long, int, string)[0]), w);
        Assert.Contains(w, x => x.Contains("sans salle"));
    }

    [Fact]
    public void State_reads_the_old_format_and_round_trips_the_new_fields()
    {
        var old = ExpeditionState.Parse("4|77|2|5|31,34|46|30|45|90")!;
        Assert.False(old.Bank);
        Assert.Equal(0, old.DungeonId);
        var s = new ExpeditionState(4, 77, 2, 5, new[] { 31 }, new int[0], 30, 45, 90, Bank: true, DungeonId: 12, Runs: 0, DungeonMode: true, Hdv: true);
        var back = ExpeditionState.Parse(s.Serialize())!;
        Assert.True(back.Bank && back.DungeonMode && back.Hdv);
        Assert.False(ExpeditionState.Parse("4|77|2|5|||30|45|90|1|12|0|1")!.Hdv);   // format à 13 champs (version précédente)
        Assert.Equal(12, back.DungeonId);
        Assert.Equal(0, back.Runs);
    }

    [Fact]
    public void Anomaly_farm_stops_when_the_anomaly_closes()
    {
        Assert.Contains("anomaly:isOpen(445)", ExpeditionScript.Build(new ExpeditionProfile("A", "1", AnomalySubarea: 445)));
        Assert.DoesNotContain("anomaly:isOpen", ExpeditionScript.Build(new ExpeditionProfile("A", "1")));
    }

    [Fact]
    public void Uchronic_elixir_is_bought_drunk_and_drunk_again_when_it_expires()
    {
        var major = UchronicElixirs.ByRank(3)!;
        Assert.Equal((26872L, 52, 5), (major.Gid, major.AlterationId, major.Fights));
        Assert.Equal(new[] { 15, 10, 5 }, UchronicElixirs.All.Select(t => t.Fights));   // infobulles du jeu : mineur, amélioré, majeur
        Assert.True(UchronicElixirs.IsFightCount(major, 5));
        Assert.False(UchronicElixirs.IsFightCount(major, 0));
        Assert.False(UchronicElixirs.IsFightCount(major, 1_760_000_000_000));   // une date, pas un nombre de combats
        Assert.Equal(major, UchronicElixirs.ByAlteration(52));
        string lua = ExpeditionScript.Build(new ExpeditionProfile("A", "1;2", Elixir: new ElixirPlan(major.Gid, major.AlterationId, 73400321, 50000, 2)));
        Assert.DoesNotContain("ensureElixir", ExpeditionScript.Build(new ExpeditionProfile("A", "1")));

        // Jeu simulé : sac, altération, HDV. travelTo échoue -> move() s'arrête juste après ensureElixir().
        var s = new MoonSharp.Interpreter.Script();
        s.DoString(@"
            bag, alt, bought, used, hdv = 0, false, 0, 0, 0
            local noop = setmetatable({}, { __index = function() return function() end end })
            config = noop
            function getCountFight() return 0 end
            function getPodsPercent() return 0 end
            function travelTo() return false end
            function printMessage() end function printWarning() end function printSuccess() end function printError() end
            function delay() end function leaveDialog() return true end
            function hasAlteration(id) return id == 52 and alt end
            function getInventoryItemCount(gid) return gid == 26872 and bag or 0 end
            function goToHdvBuy(map) hdv = map return true end
            sale = { buyItem = function(self, gid, qty, max) if gid ~= 26872 or qty ~= 1 or max ~= 50000 then return false end bag = bag + 1 bought = bought + 1 return true end }
            function useItem(gid, n) if gid == 26872 and n == 1 and bag > 0 then bag = bag - 1 used = used + 1 alt = true return true end return false end
        ");
        s.DoString(lua);
        double G(string k) => s.Globals.Get(k).Number;

        s.Call(s.Globals.Get("move"));                       // rien dans le sac : achat de 2, puis 1 bu
        Assert.Equal((2d, 1d, 1d, 73400321d), (G("bought"), G("used"), G("bag"), G("hdv")));
        s.Call(s.Globals.Get("move"));                       // altération active : rien
        Assert.Equal((2d, 1d), (G("bought"), G("used")));
        s.Globals["alt"] = false;                            // combats épuisés : l'altération disparaît
        s.Call(s.Globals.Get("move"));                       // le 2e élixir du sac est bu, sans rachat
        Assert.Equal((2d, 2d, 0d), (G("bought"), G("used"), G("bag")));
    }

    [Theory]
    [InlineData(9000, 80000, 2, 2)]      // lot de 10 moins cher à l'unité : 2 lots de 10, puis 2 à l'unité
    [InlineData(9000, 95000, 0, 22)]     // lot de 10 plus cher à l'unité : tout à l'unité
    [InlineData(9000, 120000, 0, 22)]    // lot de 10 au-dessus du plafond (10 x 10 000) : tout à l'unité
    public void Uchronic_elixirs_are_bought_by_ten_only_when_cheaper(long p1, long p10, int tens, int ones)
    {
        string lua = ExpeditionScript.Build(new ExpeditionProfile("A", "1", Elixir: new ElixirPlan(26872, 52, 5, 10000, 22)));
        var s = new MoonSharp.Interpreter.Script();
        s.DoString($@"
            bag, tens, ones = 0, 0, 0
            config = setmetatable({{}}, {{ __index = function() return function() end end }})
            function getCountFight() return 0 end function getPodsPercent() return 0 end function travelTo() return false end
            function printMessage() end function printWarning() end function printSuccess() end function printError() end
            function delay() end function leaveDialog() return true end function goToHdvBuy() return true end
            function hasAlteration() return false end
            function getInventoryItemCount() return bag end
            function useItem() bag = bag - 1 return true end
            sale = {{
                GetPriceItem = function(self, gid, lot) return lot == 10 and {p10} or {p1} end,
                buyItem = function(self, gid, qty, max)
                    local price = qty == 10 and {p10} or {p1}
                    if price > max then return false end
                    bag = bag + qty
                    if qty == 10 then tens = tens + 1 else ones = ones + 1 end
                    return true
                end }}
        ");
        s.DoString(lua);
        s.Call(s.Globals.Get("move"));
        Assert.Equal((double)tens, s.Globals.Get("tens").Number);
        Assert.Equal((double)ones, s.Globals.Get("ones").Number);
    }

    [Fact]
    public void Dungeon_drinks_the_elixir_outside_between_runs()
    {
        var dp = new DungeonProfile("D", new long[] { 1, 2 }, 3, new[] { (8L, 1, "Clé") }, Elixir: new ElixirPlan(26872, 52, 5, 10000));
        string lua = ExpeditionScript.BuildDungeon(dp);
        Assert.Contains("ensureElixir()", lua);
        Assert.True(lua.IndexOf("ensureElixir()   --", StringComparison.Ordinal) < lua.IndexOf("travelTo(ENTRANCE)", StringComparison.Ordinal));
        Assert.DoesNotContain("ensureElixir", ExpeditionScript.BuildDungeon(dp with { Elixir = null }));
        new MoonSharp.Interpreter.Script().LoadString(lua);
    }

    [Fact]
    public void Dungeon_boss_room_leaves_by_request_and_does_not_walk_out()
    {
        string lua = ExpeditionScript.BuildDungeon(new DungeonProfile("D", new long[] { 11, 12 }, 10, Array.Empty<(long, int, string)>()));
        Assert.Contains("map:leaveDungeon()", lua);
        Assert.DoesNotContain("dungeon:exit", lua);
        Assert.DoesNotContain("changeMap", lua);
        var s = new MoonSharp.Interpreter.Script();
        s.DoString(@"
            calls = 0
            config = setmetatable({}, { __index = function() return function() end end })
            function printMessage() end function printWarning() end function printSuccess() end function printError() end
            function delay() end function travelTo() error('walk') end function fight() return false end
            map = { currentMapId = function() return 12 end, leaveDungeon = function() calls = calls + 1; return false, '3' end }
        ");
        s.DoString(lua);
        s.Call(s.Globals.Get("move"));
        Assert.Equal(1d, s.Globals.Get("calls").Number);
    }
}

