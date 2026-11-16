using System;
using System.IO;
using System.Linq;
using MoonSharp.Interpreter;
using GivreClone.App.Services.Mcp;
using GivreClone.Engine.LuaTooling;
using GivreClone.Tests.LuaTooling;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Pack scripts/kamas-2026 : existence, syntaxe, lint, chargement MoonSharp et comportements clés avec un monde simulé.</summary>
public class Kamas2026ScriptsTests
{
    private const string Dir = "scripts/kamas-2026";

    public static TheoryData<string> AllScripts => new()
    {
        "Mineur-1-200-Alliages-Sylvanbot.lua",
        "Bucheron-1-200-Planches-Substrats.lua",
        "Paysan-Alchimiste-1-200-Consommables.lua",
        "Pandala-Farm-Prudent-1-3-Mobs-Recolte.lua",
        "Pecheur-1-200-Poissons-Rares.lua",
        "Audit-Rentabilite-Brisage-Et-Lots-Readonly.lua",
        "Chasseur-Anomalies-Temporelles-Bonus.lua",
        "Preparation-Almanax-Et-Artisanat-Quotidien.lua",
    };

    public static TheoryData<string> ProgressiveScripts => new()
    {
        "Mineur-1-200-Alliages-Sylvanbot.lua",
        "Bucheron-1-200-Planches-Substrats.lua",
        "Paysan-Alchimiste-1-200-Consommables.lua",
        "Pecheur-1-200-Poissons-Rares.lua",
    };

    /// <summary>Scripts qui exposent move/bank/onFightEnd/stopped et la série de défaites : les quatre progressifs + anomalies + stock Almanax.</summary>
    public static TheoryData<string> EngineScripts => new()
    {
        "Mineur-1-200-Alliages-Sylvanbot.lua",
        "Bucheron-1-200-Planches-Substrats.lua",
        "Paysan-Alchimiste-1-200-Consommables.lua",
        "Pecheur-1-200-Poissons-Rares.lua",
        "Chasseur-Anomalies-Temporelles-Bonus.lua",
        "Preparation-Almanax-Et-Artisanat-Quotidien.lua",
    };

    private const string AnomalyScript = "Chasseur-Anomalies-Temporelles-Bonus.lua";
    private const string AlmanaxScript = "Preparation-Almanax-Et-Artisanat-Quotidien.lua";
    private const string AllStocked = "INITIAL_STOCK = { Ortie = 999, Sauge = 999, Trefle = 999, Ble = 999, Orge = 999, Frene = 999, Chataignier = 999, Fer = 999, Cuivre = 999 }";

    private static string PathOf(string name) => Path.Combine(MappingScan.RepoRoot(), Dir, name);

    private static string Read(string name) => File.ReadAllText(PathOf(name));

    [Fact]
    public void Readme_exists_next_to_the_scripts()
    {
        Assert.True(File.Exists(PathOf("README.md")));
    }

    [Theory]
    [MemberData(nameof(AllScripts))]
    public void Script_file_exists(string name)
    {
        Assert.True(File.Exists(PathOf(name)), PathOf(name));
    }

    [Theory]
    [MemberData(nameof(AllScripts))]
    public void Script_has_no_syntax_error(string name)
    {
        Assert.Null(SavedLuaScripts.SyntaxError(Read(name)));
    }

    [Theory]
    [MemberData(nameof(AllScripts))]
    public void Linter_reports_no_error_or_warning_on_the_real_catalog(string name)
    {
        var bad = LuaLinter.Lint(Read(name), LuaToolingTestData.Catalog)
            .Where(d => d.Severity != LuaDiagnosticSeverity.Info)
            .Select(d => $"{d.Line}:{d.Column} {d.Code} {d.Message}")
            .ToList();
        Assert.True(bad.Count == 0, name + " : " + string.Join(" | ", bad));
    }

    [Theory]
    [MemberData(nameof(AllScripts))]
    public void Script_declares_the_standard_configuration_header(string name)
    {
        string lua = Read(name);
        Assert.Contains("PODS_MAX =", lua);
        Assert.Contains("MAX_CONSECUTIVE_DEFEATS =", lua);
        Assert.Contains("BANK_MAP =", lua);
    }

    [Theory]
    [MemberData(nameof(AllScripts))]
    public void Script_never_reads_character_energy(string name)
    {
        string lua = Read(name).ToLowerInvariant();
        Assert.DoesNotContain("energ", lua);
    }

    [Theory]
    [MemberData(nameof(AllScripts))]
    public void Script_loads_in_moonsharp_and_exposes_move(string name)
    {
        var script = LoadWithStubs(Read(name));
        Assert.Equal(DataType.Function, script.Globals.Get("move").Type);
    }

    [Theory]
    [MemberData(nameof(EngineScripts))]
    public void Engine_scripts_expose_the_engine_callbacks(string name)
    {
        var script = LoadWithStubs(Read(name));
        foreach (var callback in new[] { "move", "bank", "onFightEnd", "stopped" })
            Assert.Equal(DataType.Function, script.Globals.Get(callback).Type);
        Assert.Equal(85, script.Globals.Get("MAX_PODS").Number);
    }

    // ---------------------------------------------------------------- comportements simulés

    [Theory]
    [MemberData(nameof(ProgressiveScripts))]
    public void Harvest_script_travels_gathers_and_summarises(string name)
    {
        var script = LoadWithWorld(Read(name));
        Assert.True(script.DoString("return move()").Boolean);
        Assert.True(script.DoString("return calls.travel > 0 and calls.gather > 0").Boolean);
        Assert.True(script.DoString("return move()").Boolean);
        script.DoString("stopped('user')");
        Assert.Contains("BILAN D'ARRET", script.Globals.Get("log_text").String);
        Assert.Contains("JALON", script.Globals.Get("log_text").String);
    }

    [Theory]
    [MemberData(nameof(ProgressiveScripts))]
    public void Harvest_script_banks_at_pods_max_then_resumes(string name)
    {
        var script = LoadWithWorld(Read(name));
        script.DoString("pods_percent = 90");
        Assert.True(script.DoString("return move()").Boolean);
        Assert.Equal(1, script.DoString("return calls.deposit").Number);
        Assert.Equal(0, script.DoString("return calls.gather").Number);
        script.DoString("pods_percent = 5");
        Assert.True(script.DoString("return move()").Boolean);
        Assert.True(script.DoString("return calls.gather > 0").Boolean);
    }

    [Theory]
    [MemberData(nameof(EngineScripts))]
    public void Engine_script_stops_cleanly_at_the_defeat_streak_and_victory_resets_it(string name)
    {
        var script = LoadWithWorld(Read(name));
        Assert.Equal(3, script.Globals.Get("MAX_CONSECUTIVE_DEFEATS").Number);
        script.DoString("onFightEnd({ won = false }) onFightEnd({ won = false }) onFightEnd({ won = true }) onFightEnd({ won = false }) onFightEnd({ won = false })");
        Assert.True(script.DoString("return move()").Boolean);
        script.DoString("onFightEnd({ won = false })");
        Assert.False(script.DoString("return move()").Boolean);
    }

    [Theory]
    [MemberData(nameof(EngineScripts))]
    public void Engine_script_with_zero_limit_never_stops_on_defeats(string name)
    {
        var script = LoadWithWorld(Read(name).Replace("MAX_CONSECUTIVE_DEFEATS = 3", "MAX_CONSECUTIVE_DEFEATS = 0"));
        for (int i = 0; i < 10; i++) script.DoString("onFightEnd({ won = false })");
        Assert.True(script.DoString("return move()").Boolean);
    }

    // ---------------------------------------------------------------- chasseur d'anomalies

    [Fact]
    public void Anomaly_hunter_farms_the_open_anomaly_zone_with_the_bonus()
    {
        var script = LoadWithWorld(Read(AnomalyScript));
        script.DoString("open_subareas['Plantala'] = true");
        Assert.True(script.DoString("return move()").Boolean);
        string log = script.Globals.Get("log_text").String;
        Assert.Contains("ANOMALIE OUVERTE : Plantala (Pandala)", log);
        Assert.Contains("bonus=true", log);
        Assert.True(script.DoString("return calls.fight == 2 and calls.travel > 0").Boolean);
        Assert.True(script.DoString("return isopen_queries[1] == 'Plantala'").Boolean);
        Assert.InRange(script.DoString("return map_id").Number, 205520896, 205522438);
    }

    [Fact]
    public void Anomaly_hunter_falls_back_to_the_default_zone_when_nothing_is_open_and_switches_when_one_opens()
    {
        var script = LoadWithWorld(Read(AnomalyScript));
        Assert.True(script.DoString("return move()").Boolean);
        string log = script.Globals.Get("log_text").String;
        Assert.Contains("repli sur la zone par defaut Plantala (Pandala)", log);
        Assert.Contains("bonus=false", log);
        Assert.Equal(2, script.DoString("return calls.fight").Number);

        script.DoString("open_subareas['Plantala'] = true");
        Assert.True(script.DoString("return move()").Boolean);
        Assert.Contains("ANOMALIE OUVERTE : Plantala (Pandala)", script.Globals.Get("log_text").String);

        script.DoString("open_subareas['Plantala'] = nil");
        Assert.True(script.DoString("return move()").Boolean);
        string after = script.Globals.Get("log_text").String;
        Assert.Equal(2, CountOf(after, "repli sur la zone par defaut"));
        Assert.Equal(1, CountOf(after, "ANOMALIE OUVERTE : Plantala"));
        script.DoString("stopped('user')");
        Assert.Contains("ZONE Plantala (Pandala) : combats bonus=2 combats repli=4", script.Globals.Get("log_text").String);
    }

    [Fact]
    public void Anomaly_hunter_waits_without_fighting_when_fallback_is_off_and_gives_up_after_the_idle_limit()
    {
        string lua = Read(AnomalyScript)
            .Replace("FALLBACK_TO_DEFAULT = true    --", "FALLBACK_TO_DEFAULT = false   --")
            .Replace("MAX_IDLE_CHECKS = 0", "MAX_IDLE_CHECKS = 2");
        var script = LoadWithWorld(lua);
        Assert.True(script.DoString("return move()").Boolean);
        Assert.Equal(0, script.DoString("return calls.fight + calls.travel").Number);
        Assert.Contains("attente", script.Globals.Get("log_text").String);
        Assert.False(script.DoString("return move()").Boolean);
        Assert.Contains("aucune anomalie ouverte", script.Globals.Get("warning_text").String);

        var opens = LoadWithWorld(lua);
        opens.DoString("open_subareas['Plantala'] = true");
        Assert.True(opens.DoString("return move()").Boolean);
        Assert.True(opens.DoString("return calls.fight > 0").Boolean);
    }

    [Fact]
    public void Anomaly_hunter_prefers_the_first_open_zone_and_respects_the_level_window()
    {
        string wide = Read(AnomalyScript).Replace("minLevel = 1, maxLevel = 25", "minLevel = 1, maxLevel = 0");
        var both = LoadWithWorld(wide);
        both.DoString("open_subareas['Plantala'] = true open_subareas['449'] = true");
        both.DoString("move()");
        Assert.Contains("ANOMALIE OUVERTE : Plantala (Pandala)", both.Globals.Get("log_text").String);

        var second = LoadWithWorld(wide);
        second.DoString("open_subareas['449'] = true move()");
        Assert.Contains("ANOMALIE OUVERTE : Cimetiere (Incarnam)", second.Globals.Get("log_text").String);
        Assert.InRange(second.DoString("return map_id").Number, 153880064, 153881601);

        var low = LoadWithWorld(Read(AnomalyScript));
        low.DoString("character_level = 20 open_subareas['Plantala'] = true open_subareas['449'] = true move()");
        string log = low.Globals.Get("log_text").String;
        Assert.Contains("ANOMALIE OUVERTE : Cimetiere (Incarnam)", log);
        Assert.DoesNotContain("ANOMALIE OUVERTE : Plantala", log);
        Assert.Equal("449", low.DoString("return isopen_queries[1]").String);
    }

    [Fact]
    public void Anomaly_hunter_stops_fighting_the_moment_the_anomaly_closes()
    {
        var script = LoadWithWorld(Read(AnomalyScript));
        script.DoString("open_subareas['Plantala'] = true fight_hook = function() open_subareas['Plantala'] = nil end");
        Assert.True(script.DoString("return move()").Boolean);
        Assert.Equal(1, script.DoString("return calls.fight").Number);
        Assert.Contains("anomalie fermee", script.Globals.Get("log_text").String);
    }

    [Fact]
    public void Anomaly_hunter_clamps_the_group_size_and_pushes_it_to_the_engine()
    {
        var normal = LoadWithWorld(Read(AnomalyScript));
        normal.DoString("move()");
        Assert.Equal(1, normal.Globals.Get("MIN_MONSTERS").Number);
        Assert.Equal(3, normal.Globals.Get("MAX_MONSTERS").Number);
        Assert.Equal("1,3", normal.DoString("return table.concat(config_calls, ',')").String);
        var wild = LoadWithWorld(Read(AnomalyScript).Replace("MIN_MONSTERS = 1 ", "MIN_MONSTERS = 0 ").Replace("MAX_MONSTERS = 3 ", "MAX_MONSTERS = 99 "));
        wild.DoString("move()");
        Assert.Equal(1, wild.Globals.Get("MIN_MONSTERS").Number);
        Assert.Equal(8, wild.Globals.Get("MAX_MONSTERS").Number);
    }

    [Fact]
    public void Anomaly_hunter_banks_at_pods_max_then_resumes_fighting()
    {
        var script = LoadWithWorld(Read(AnomalyScript));
        script.DoString("pods_percent = 90");
        Assert.True(script.DoString("return move()").Boolean);
        Assert.Equal(1, script.DoString("return calls.deposit").Number);
        Assert.Equal(0, script.DoString("return calls.fight").Number);
        script.DoString("pods_percent = 5");
        Assert.True(script.DoString("return move()").Boolean);
        Assert.True(script.DoString("return calls.fight > 0").Boolean);
    }

    [Fact]
    public void Anomaly_hunter_stops_with_a_warning_at_the_defeat_threshold()
    {
        var script = LoadWithWorld(Read(AnomalyScript));
        script.DoString("onFightEnd({ won = false }) onFightEnd({ won = false }) onFightEnd({ won = false })");
        Assert.False(script.DoString("return move()").Boolean);
        Assert.Contains("defaites consecutives", script.Globals.Get("warning_text").String);
        Assert.Equal(0, script.DoString("return calls.fight").Number);
    }

    // ---------------------------------------------------------------- stock Almanax / artisanat

    [Fact]
    public void Almanax_stock_builder_tracks_every_staple_with_a_quota()
    {
        var script = LoadWithWorld(Read(AlmanaxScript));
        script.DoString("stopped('user')");
        string log = script.Globals.Get("log_text").String;
        foreach (var staple in new[] { "Ortie", "Sauge", "Trefle", "Ble", "Orge", "Frene", "Chataignier", "Fer", "Cuivre" })
            Assert.Contains("STOCK " + staple + " 0/", log);
        Assert.Contains("QUOTAS atteints 0/9", log);
    }

    [Fact]
    public void Almanax_stock_builder_counts_harvests_toward_the_quota_then_moves_to_the_next_resource()
    {
        var script = LoadWithWorld(Read(AlmanaxScript));
        script.DoString("bag_gain = { gid = 11, name = 'Ortie', qty = 40 }");
        for (int i = 0; i < 3; i++) Assert.True(script.DoString("return move()").Boolean);
        script.DoString("bag_gain = nil");
        Assert.True(script.DoString("return move()").Boolean);
        script.DoString("stopped('user')");
        string log = script.Globals.Get("log_text").String;
        Assert.Contains("RESSOURCE Ortie : stock 0/100, reste 100", log);
        Assert.Contains("RESSOURCE Sauge", log);
        Assert.Contains("STOCK Ortie 120/100 [####################] 100% (session +120) OK", log);
        Assert.Contains("QUOTAS atteints 1/9", log);
    }

    [Fact]
    public void Almanax_stock_builder_skips_resources_already_stocked_or_above_the_job_level()
    {
        var stocked = LoadWithWorld(Read(AlmanaxScript).Replace("INITIAL_STOCK = {}", "INITIAL_STOCK = { Ortie = 100 }"));
        Assert.True(stocked.DoString("return move()").Boolean);
        Assert.Contains("RESSOURCE Sauge", stocked.Globals.Get("log_text").String);

        var novice = LoadWithWorld(Read(AlmanaxScript).Replace("INITIAL_STOCK = {}", "INITIAL_STOCK = { Ortie = 100 }"));
        novice.DoString("job_level = 10");
        Assert.True(novice.DoString("return move()").Boolean);
        string log = novice.Globals.Get("log_text").String;
        Assert.Contains("RESSOURCE Ble", log);
        Assert.DoesNotContain("RESSOURCE Sauge", log);
    }

    [Fact]
    public void Almanax_stock_builder_deposits_once_when_all_quotas_are_met_then_stops()
    {
        var script = LoadWithWorld(Read(AlmanaxScript).Replace("INITIAL_STOCK = {}", AllStocked.Replace("Fer = 999", "Fer = 95")));
        script.DoString("bag_gain = { gid = 21, name = 'Fer', qty = 10 }");
        Assert.True(script.DoString("return move()").Boolean);   // recolte : Fer 105/100
        Assert.True(script.DoString("return move()").Boolean);   // quotas atteints : depot final
        Assert.Equal(1, script.DoString("return calls.deposit").Number);
        Assert.False(script.DoString("return move()").Boolean);  // rien a deposer : fin
        Assert.Equal(1, script.DoString("return calls.deposit").Number);
        string log = script.Globals.Get("log_text").String;
        Assert.Contains("QUOTAS atteints 9/9", log);
        Assert.Contains("TERMINE", log);
    }

    [Fact]
    public void Almanax_stock_builder_with_every_quota_met_at_start_stops_without_depositing()
    {
        var script = LoadWithWorld(Read(AlmanaxScript).Replace("INITIAL_STOCK = {}", AllStocked));
        Assert.False(script.DoString("return move()").Boolean);
        Assert.Equal(0, script.DoString("return calls.deposit + calls.travel + calls.gather").Number);
    }

    [Fact]
    public void Almanax_stock_builder_banks_at_pods_max_then_resumes_gathering()
    {
        var script = LoadWithWorld(Read(AlmanaxScript));
        script.DoString("pods_percent = 90");
        Assert.True(script.DoString("return move()").Boolean);
        Assert.Equal(1, script.DoString("return calls.deposit").Number);
        Assert.Equal(0, script.DoString("return calls.gather").Number);
        script.DoString("pods_percent = 5");
        Assert.True(script.DoString("return move()").Boolean);
        Assert.True(script.DoString("return calls.gather > 0").Boolean);
    }

    private static int CountOf(string text, string part)
    {
        int count = 0;
        for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    [Fact]
    public void Miner_unlocks_ore_tiers_with_the_job_level()
    {
        string lua = Read("Mineur-1-200-Alliages-Sylvanbot.lua");
        var low = LoadWithWorld(lua);
        low.DoString("job_level = 10 move()");
        Assert.Contains("Fer", low.Globals.Get("log_text").String);
        Assert.DoesNotContain("Obsidienne", low.Globals.Get("log_text").String);
        var high = LoadWithWorld(lua);
        high.DoString("job_level = 200 move()");
        Assert.Contains("Dolomite", high.Globals.Get("log_text").String);
        Assert.Contains("Silicate/Obsidienne", high.Globals.Get("log_text").String);
        high.DoString("stopped('user')");
        foreach (var milestone in new[] { "Ferrite", "Bakelite", "Kriptonite", "Rutile", "Ardonite" })
            Assert.Contains("JALON " + milestone + " : ATTEINT", high.Globals.Get("log_text").String);
    }

    [Fact]
    public void Lumberjack_reports_substrate_readiness()
    {
        var script = LoadWithWorld(Read("Bucheron-1-200-Planches-Substrats.lua"));
        script.DoString("stopped('user')");
        Assert.Contains("SUBSTRATS", script.Globals.Get("log_text").String);
    }

    [Fact]
    public void Fisher_counts_rare_fish_jackpots()
    {
        var script = LoadWithWorld(Read("Pecheur-1-200-Poissons-Rares.lua"));
        script.DoString("bag_gain = { gid = 77, name = \"Greuvette d'Or\", qty = 2 }");
        Assert.True(script.DoString("return move()").Boolean);
        script.DoString("stopped('user')");
        string log = script.Globals.Get("log_text").String;
        Assert.Contains("JACKPOT #1", log);
        Assert.Contains("JACKPOTS poissons rares : evenements=1 unites=2", log);
    }

    [Fact]
    public void Pandala_farm_clamps_group_size_per_profile_and_to_three()
    {
        string lua = Read("Pandala-Farm-Prudent-1-3-Mobs-Recolte.lua");
        var shadowbot = LoadWithWorld(lua);
        shadowbot.DoString("character_name = 'Shadowbot' move()");
        Assert.Equal(1, shadowbot.Globals.Get("MIN_MONSTERS").Number);
        Assert.Equal(2, shadowbot.Globals.Get("MAX_MONSTERS").Number);
        Assert.Equal("1,2", shadowbot.DoString("return table.concat(config_calls, ',')").String);
        var bastionbot = LoadWithWorld(lua);
        bastionbot.DoString("character_name = 'Bastionbot' move()");
        Assert.Equal(3, bastionbot.Globals.Get("MAX_MONSTERS").Number);
        var wild = LoadWithWorld(lua.Replace("groupMax = 3", "groupMax = 9"));
        wild.DoString("character_name = 'Bastionbot' move()");
        Assert.Equal(3, wild.Globals.Get("MAX_MONSTERS").Number);
    }

    [Fact]
    public void Pandala_farm_fights_then_harvests_and_banks_at_pods_max()
    {
        var script = LoadWithWorld(Read("Pandala-Farm-Prudent-1-3-Mobs-Recolte.lua"));
        Assert.True(script.DoString("return move()").Boolean);
        Assert.True(script.DoString("return calls.fight > 0 and calls.gather > 0").Boolean);
        script.DoString("pods_percent = 90");
        Assert.True(script.DoString("return move()").Boolean);
        Assert.Equal(1, script.DoString("return calls.deposit").Number);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Pandala_farm_stops_exactly_at_the_defeat_threshold(int limit)
    {
        var script = LoadWithWorld(Read("Pandala-Farm-Prudent-1-3-Mobs-Recolte.lua")
            .Replace("MAX_CONSECUTIVE_DEFEATS = 3", $"MAX_CONSECUTIVE_DEFEATS = {limit}"));
        for (int i = 1; i < limit; i++)
        {
            script.DoString("onFightEnd({ won = false })");
            Assert.True(script.DoString("return move()").Boolean);
        }
        script.DoString("onFightEnd({ won = false })");
        Assert.False(script.DoString("return move()").Boolean);
        Assert.Contains("defaites consecutives", script.Globals.Get("warning_text").String);
    }

    [Fact]
    public void Pandala_farm_victory_resets_the_streak_and_zero_means_unlimited()
    {
        string lua = Read("Pandala-Farm-Prudent-1-3-Mobs-Recolte.lua");
        var script = LoadWithWorld(lua);
        script.DoString("onFightEnd({ won = false }) onFightEnd({ won = false }) onFightEnd({ won = true }) onFightEnd({ won = false }) onFightEnd({ won = false })");
        Assert.True(script.DoString("return move()").Boolean);
        var unlimited = LoadWithWorld(lua.Replace("MAX_CONSECUTIVE_DEFEATS = 3", "MAX_CONSECUTIVE_DEFEATS = 0"));
        for (int i = 0; i < 25; i++) unlimited.DoString("onFightEnd({ won = false })");
        Assert.True(unlimited.DoString("return move()").Boolean);
    }

    [Fact]
    public void Audit_is_read_only_and_prints_the_checklist_formula_and_rune_table()
    {
        string lua = Read("Audit-Rentabilite-Brisage-Et-Lots-Readonly.lua");
        var script = LoadWithWorld(lua.Replace("-- { \"Ortie\", 12, 110, 1000 },", "{ \"Ortie\", 12, 110, 1000 }, { \"Sauge\", 12, 100, 1000 },"));
        script.DoString("bag[1] = { uid = 1, gid = 5, qty = 3, name = 'Ortie', pos = 63 }");
        Assert.False(script.DoString("return move()").Boolean);
        Assert.False(script.DoString("return move()").Boolean);   // un seul passage : le second appel ne refait rien
        string log = script.Globals.Get("log_text").String;
        Assert.Contains("Prix(100) / 10 < Prix(10) * 0.98", log);
        Assert.Contains("A FAIRE 1.", log);
        foreach (var rune in new[] { "Ga PA | poids=100 | focus=60", "Ga PM | poids=90 | focus=54", "Po (Portee) | poids=51", "Vitalite (Vi) | poids=0.25", "Ra=2.5" })
            Assert.Contains(rune, log);
        Assert.Contains("SAC gid=5 Ortie x3", log);
        Assert.Contains("LOT Ortie", log);
        Assert.Contains("OPPORTUNITE", log);      // 1000 / 10 = 100 < 110 * 0.98
        Assert.Contains("pas d'ecart exploitable", log);   // Sauge : 1000 / 10 = 100, pas sous 100 * 0.98
        Assert.Equal(0, script.DoString("return calls.mutating").Number);
    }

    [Fact]
    public void Audit_source_never_mentions_a_trading_crafting_or_travel_verb()
    {
        string lua = Read("Audit-Rentabilite-Brisage-Et-Lots-Readonly.lua");
        var code = string.Join("\n", lua.Split('\n').Where(l => !l.TrimStart().StartsWith("--")));
        foreach (var forbidden in new[] { "sale:", "SellItem", "buyItem", "trade:", "goToHdv", "openHdv", "craft:", "craftItem", "forgeItem", "fm:", "travelTo", "gatherAll", "fight(", "exchange:", "bank:", "npc:", "putAllItems", "delay(" })
            Assert.DoesNotContain(forbidden, code, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Batch_arbitrage_formula_matches_the_documented_condition()
    {
        var script = LoadWithWorld(Read("Audit-Rentabilite-Brisage-Et-Lots-Readonly.lua"));
        // Prix(100)/10 = 100 < 110 * 0.98 = 107.8 : oui ; = 110 < 107.8 : non.
        Assert.True(script.DoString("local ok = batchArbitrage(110, 1000) return ok").Boolean);
        Assert.False(script.DoString("local ok = batchArbitrage(110, 1100) return ok").Boolean);
        Assert.Equal(DataType.Nil, script.DoString("return (batchArbitrage(0, 1000))").Type);
        Assert.Equal(DataType.Nil, script.DoString("return (batchArbitrage(10, nil))").Type);
        Assert.Equal(60, script.DoString("return focusWeight(100)").Number, 6);
    }

    // ---------------------------------------------------------------- aides

    /// <summary>Charge le script avec seulement print* : le chargement n'appelle aucune API de jeu.</summary>
    private static Script LoadWithStubs(string lua)
    {
        var script = new Script();
        script.DoString("""
            function print() end
            function printWarning() end
            function printSuccess() end
            function printError() end
            """);
        script.DoString(lua);
        return script;
    }

    /// <summary>
    /// Monde simulé minimal : tout trajet réussit, les récoltes ajoutent <c>bag_gain</c> au sac, la banque vide les pods.
    /// Les compteurs d'appels sont lus par les tests ; <c>calls.mutating</c> compte tout ordre de marché ou de craft.
    /// </summary>
    private static Script LoadWithWorld(string lua)
    {
        var script = new Script();
        script.DoString("""
            log_text, warning_text = "", ""
            calls = { travel = 0, gather = 0, fight = 0, deposit = 0, mutating = 0 }
            config_calls = {}
            pods_percent, pods, life_percent, job_level, character_level = 5, 1000, 100, 120, 150
            open_subareas, isopen_queries, fight_hook = {}, {}, nil
            character_name, map_id, current_pos = "Shadowbot", 0, ""
            bag_gain, bag = nil, {}
            function print(s) log_text = log_text .. tostring(s) .. "\n" end
            function printSuccess(s) log_text = log_text .. tostring(s) .. "\n" end
            function printWarning(s) warning_text = warning_text .. tostring(s) .. "\n" log_text = log_text .. tostring(s) .. "\n" end
            function printError(s) warning_text = warning_text .. tostring(s) .. "\n" end
            function delay() end
            function leaveDialog() end
            function isInFight() return false end
            function isDead() return false end
            function getPodsPercent() return pods_percent end
            function getPods() return pods end
            function getPodsMax() return 10000 end
            function getKamas() return 250000 end
            function getLifePointsPercent() return life_percent end
            function getJobLevel() return job_level end
            function getCharacterName() return character_name end
            function getCharacterLevel() return character_level end
            function getServerName() return "Test" end
            function getMapId() return map_id end
            function getCurrentPos() return current_pos end
            function getCountGather() return calls.gather end
            function regenerate() return true end
            function stopScript() end
            function travelTo(a, b)
                calls.travel = calls.travel + 1
                if b ~= nil then current_pos = a .. "," .. b else map_id = a end
                return true
            end
            function gatherAll()
                calls.gather = calls.gather + 1
                if bag_gain then bag[#bag + 1] = { uid = #bag + 1, gid = bag_gain.gid, qty = bag_gain.qty, name = bag_gain.name, pos = 63 } end
                return true
            end
            function fight() calls.fight = calls.fight + 1 if fight_hook then fight_hook() end return true end
            anomaly = { isOpen = function(_, subarea)
                isopen_queries[#isopen_queries + 1] = tostring(subarea)
                return open_subareas[tostring(subarea)] == true
            end }
            map = { sendMapRequest = function() return true end }
            inventory = {
                items = function() return bag end,
                getAveragePrice = function() return 0 end,
            }
            npc = { npcBank = function() return true end }
            exchange = { putAllItems = function()
                calls.deposit = calls.deposit + 1
                pods_percent, pods = 5, 100
                return true
            end }
            config = {
                setMinMonsters = function(_, n) config_calls[#config_calls + 1] = n end,
                setMaxMonsters = function(_, n) config_calls[#config_calls + 1] = n end,
            }
            combat = {
                setAutoFight = function() end,
                setAutoPreFight = function() end,
                setChallengeMode = function() end,
            }
            -- Tout ordre de marché / craft compterait comme une mutation : ces tables n'existent pas dans les scripts consultatifs.
            sale = setmetatable({}, { __index = function() calls.mutating = calls.mutating + 1 return function() return false end end })
            craft = setmetatable({}, { __index = function() calls.mutating = calls.mutating + 1 return function() return false end end })
            trade = setmetatable({}, { __index = function() calls.mutating = calls.mutating + 1 return function() return false end end })
            """);
        script.DoString(lua);
        return script;
    }
}
