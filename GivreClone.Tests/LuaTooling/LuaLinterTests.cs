using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GivreClone.Engine.LuaTooling;
using Xunit;

namespace GivreClone.Tests.LuaTooling;

public class LuaLinterTests
{
    private static readonly ILuaNameSet Names = new LuaToolingTestData.FakeNames();

    // Script « pilotable » (définit move) : isole les règles testées de givre.no-entry.
    private static IReadOnlyList<LuaDiagnostic> Lint(string body, ILuaNameSet? names = null) =>
        LuaLinter.Lint("function move() end\n" + body, names ?? Names);

    private static LuaDiagnostic[] Of(IReadOnlyList<LuaDiagnostic> d, string code) => d.Where(x => x.Code == code).ToArray();

    private static void NoDiagnostic(string body, ILuaNameSet? names = null)
    {
        var d = Lint(body, names);
        Assert.True(d.Count == 0, string.Join("\n", d.Select(x => $"{x.Line}:{x.Column} {x.Code} {x.Message}")));
    }

    // ------------------------------------------------------------------ lua.*

    [Fact]
    public void Neq_is_a_warning_because_moonsharp_accepts_it()
    {
        var d = Assert.Single(Lint("if getPods() != 2 then print(1) end"));
        Assert.Equal(("lua.neq", LuaDiagnosticSeverity.Warning), (d.Code, d.Severity));
        Assert.Equal("en Lua, « différent de » s'écrit ~=", d.Hint);
        Assert.Equal((2, 13, 2, 15), (d.Line, d.Column, d.EndLine, d.EndColumn));
        NoDiagnostic("if getPods() ~= 2 then print(1) end");
    }

    [Fact]
    public void Logic_operators_from_other_languages()
    {
        var d = Lint("local a, b = 1, 2\nif a && b then end\nif a || b then end\nif !a then end");
        var logic = Of(d, "lua.logic");
        Assert.Equal(3, logic.Length);
        Assert.All(logic, x => Assert.Equal(LuaDiagnosticSeverity.Error, x.Severity));
        Assert.Equal("utilise « and »", logic[0].Hint);
        Assert.Equal("utilise « or »", logic[1].Hint);
        Assert.Contains("not", logic[2].Hint);
        // Lambdas MoonSharp : || expr (sans paramètre) et |x| expr sont valides.
        NoDiagnostic("local f = || 42\nlocal g = |x, y| x + y\nprint(f(), g(1, 2))\nlocal t = { |x| x, || 1 }\nif f and g or t then end");
    }

    [Fact]
    public void Compound_assignments_and_increment()
    {
        var d = Of(Lint("local x, i = 1, 1\nx += 1\nx -= 2\nx *= 3\nx /= 4\ni++\nlocal t = {}\nt.n += 1\nt[i] += 1"), "lua.compound");
        Assert.Equal(7, d.Length);
        Assert.All(d, x => Assert.Equal(LuaDiagnosticSeverity.Error, x.Severity));
        Assert.Equal("écris « x = x + … »", d[0].Hint);
        Assert.Equal("écris « x = x - … »", d[1].Hint);
        Assert.Equal("écris « i = i + 1 »", d[4].Hint);
        Assert.Equal("écris « t.n = t.n + … »", d[5].Hint);
        Assert.Equal("écris « t[i] = t[i] + … »", d[6].Hint);
        NoDiagnostic("local x = 3\nx = x - -1\nx = -1\nx = x+1\nx = x * -2");
    }

    [Fact]
    public void Continue_statement()
    {
        var d = Assert.Single(Of(Lint("for i = 1, 3 do\n  if i == 2 then continue end\n  print(i)\nend"), "lua.continue"));
        Assert.Equal((3, 17), (d.Line, d.Column));
        Assert.Contains("goto continue", d.Hint);
        Assert.Single(Of(Lint("for i = 1, 3 do\n  if i == 2 then\n    continue\n  end\nend"), "lua.continue"));
        // Valides : goto + étiquette, variable nommée continue, champ.
        NoDiagnostic("for i = 1, 3 do\n  if i == 2 then goto continue end\n  print(i)\n  ::continue::\nend");
        NoDiagnostic("local continue = true\nif continue then print(1) end\ncontinue = false");
        NoDiagnostic("local t = {}\nt.continue = 1\nprint(t.continue)");
    }

    [Fact]
    public void C_style_comment_at_statement_start_and_floor_division()
    {
        var d = Lint("// on récolte ici(ble)\nlocal x = 7 // 2");
        var c = Assert.Single(Of(d, "lua.cstyle-comment"));
        Assert.Equal((2, 0, 2, 2), (c.Line, c.Column, c.EndLine, c.EndColumn));
        Assert.Contains("--", c.Hint);
        Assert.Single(Of(d, "lua.floordiv"));
        Assert.Empty(Of(d, "givre.unknown-global"));   // le reste de la ligne « commentée » est ignoré
        NoDiagnostic("-- // pas du code\nlocal url = \"http://x\"\nlocal a = 6 / 3");
    }

    [Fact]
    public void Bitwise_and_unknown_characters()
    {
        var d = Lint("local a = 5 & 3\nlocal b = 5 | 3\nlocal c = ~a\nlocal e = a $ b");
        Assert.Equal(3, Of(d, "lua.bitwise").Length);
        Assert.Single(Of(d, "lua.unknown-char"));
        NoDiagnostic("local v = bit32.band(5, 3)\nlocal w = 5 ~= 3");
    }

    [Fact]
    public void Unfinished_strings_and_comments()
    {
        Assert.Single(Of(Lint("local s = 'abc\nprint(s)"), "lua.unfinished-string"));
        var ls = Assert.Single(Of(Lint("local s = [[abc\nprint(s)"), "lua.unfinished-string"));
        Assert.Equal(ls.Line, ls.EndLine);   // soulignement limité à la 1re ligne
        Assert.Single(Of(Lint("--[==[ jamais\nfermé ]]"), "lua.unfinished-comment"));
        NoDiagnostic("local s = [==[ ]] ]=] ]==]\n--[[ ok ]]\nlocal q = \"a\\\"b\"");
    }

    [Fact]
    public void Unclosed_block_is_reported_at_the_opener_line()
    {
        var d = Assert.Single(Of(LuaLinter.Lint("x = 1\n\nfunction move()\n  gatherAll()\n", Names), "lua.unclosed-block"));
        Assert.Equal(LuaDiagnosticSeverity.Error, d.Severity);
        Assert.Equal("« function » ouvert ligne 3 sans « end »", d.Message);
        Assert.Equal((3, 0, 3, 8), (d.Line, d.Column, d.EndLine, d.EndColumn));
    }

    [Fact]
    public void Missing_inner_end_is_located_by_indentation()
    {
        string src = "function f()\n    for i = 1, 3 do\n        if i then\n            x()\n    end\nend\n";
        var d = Assert.Single(Of(LuaLinter.Lint(src, Names), "lua.unclosed-block"));
        Assert.Equal("« if » ouvert ligne 3 sans « end »", d.Message);
        Assert.Empty(Of(LuaLinter.Lint(src, Names), "lua.extra-end"));
    }

    [Fact]
    public void Repeat_without_until()
    {
        var d = LuaLinter.Lint("repeat\n  x = 1\nend", Names).Where(x => x.Severity == LuaDiagnosticSeverity.Error).ToArray();
        var e = Assert.Single(d);
        Assert.Equal("lua.unclosed-block", e.Code);
        Assert.Contains("repeat", e.Message);
        Assert.Contains("until", e.Hint);
        Assert.Single(Of(LuaLinter.Lint("repeat\n  x = 1\n", Names), "lua.unclosed-block"));
    }

    [Fact]
    public void Extra_end_and_until()
    {
        var d = Assert.Single(Of(Lint("if x then y() end end"), "lua.extra-end"));
        Assert.Equal((2, 18, 2, 21), (d.Line, d.Column, d.EndLine, d.EndColumn));
        Assert.Single(Of(Lint("until x"), "lua.extra-end"));
        // end mal indenté mais en trop : désigné par l'indentation plutôt que le dernier end du fichier.
        var e = Assert.Single(Of(LuaLinter.Lint("function f()\n    if x then\n        y()\n        end\n    end\nend", Names), "lua.extra-end"));
        Assert.Equal(4, e.Line);
    }

    [Fact]
    public void Missing_then_and_do()
    {
        Assert.Single(Of(Lint("if x\n  y()\nend"), "lua.missing-then"));
        Assert.Single(Of(Lint("for i = 1, 3\n  print(i)\nend"), "lua.missing-do"));
        Assert.Single(Of(Lint("while x\n  y()\nend"), "lua.missing-do"));
    }

    [Fact]
    public void Balanced_code_never_reports_block_errors()
    {
        NoDiagnostic(string.Join("\n",
            "local f = function() end",
            "local g = function(a) if a then return 1 end end",
            "while getPods() < 10 do if gatherAll() then break end end",
            "for k, v in pairs({}) do print(k, v) end",
            "do local z = 1 end",
            "repeat local q = 1 until q == 1",
            "if a then elseif b then else end",
            "local t = { f = function() return { function() end } end }",
            "while (function() return false end)() do end",
            "if (function() return true end)() then end",
            "pcall(function() error('x') end)"));
    }

    [Fact]
    public void Keyword_after_dot_or_colon()
    {
        var d = Assert.Single(Of(Lint("local t = {}\nt.end = 1"), "lua.keyword-member"));
        Assert.Contains("t[\"end\"]", d.Hint);
        NoDiagnostic("local t = {}\nt['end'] = 1\nprint(t['end'])");
    }

    // ------------------------------------------------------------------ givre.*

    [Fact]
    public void Goto_member_of_zaap_and_zaapi()
    {
        var d = Assert.Single(Of(Lint("zaap:goto(1234)"), "givre.goto-member"));
        Assert.Equal(LuaDiagnosticSeverity.Error, d.Severity);
        Assert.Equal("goto est un mot-clé Lua : utilise zaap:go(...)", d.Message);
        Assert.Equal((2, 0, 2, 9), (d.Line, d.Column, d.EndLine, d.EndColumn));
        Assert.Equal("goto est un mot-clé Lua : utilise zaapi:go(...)", Assert.Single(Of(Lint("zaapi.goto(1)"), "givre.goto-member")).Message);
        NoDiagnostic("zaap:go(1234)");
    }

    [Fact]
    public void Unknown_global_call_with_suggestion()
    {
        var d = Assert.Single(Lint("local n = getJobLevell(26)"));
        Assert.Equal(("givre.unknown-global", LuaDiagnosticSeverity.Warning), (d.Code, d.Severity));
        Assert.Equal("« getJobLevell » n'existe pas (ni dans l'API Givre, ni dans Lua, ni dans ton script)", d.Message);
        Assert.Equal("Tu voulais dire « getJobLevel » ?", d.Hint);
        Assert.Equal("Tu voulais dire « getPods » ?", Assert.Single(Lint("GetPods()")).Hint);   // casse d'abord
        Assert.Null(Assert.Single(Lint("zorglub()")).Hint);
        Assert.Single(Lint("helper \"x\""));
        Assert.Single(Lint("helper { 1 }"));
    }

    [Fact]
    public void Unknown_global_no_false_positive_on_declarations_and_known_names()
    {
        NoDiagnostic(string.Join("\n",
            "local M = {}",
            "function M:foo() return self end",
            "function M.bar(a, b) return a + b end",
            "M:foo() M.bar(1, 2)",
            "local function helper(x) return x end",
            "helper(1)",
            "later()",                                   // définie plus bas dans le fichier
            "function later() end",
            "local function run(cb) cb() end",           // paramètre appelé
            "local s = string.format('%d', #{1, 2})",    // bibliothèque standard, #
            "local u = require(\"utils\"); u.f()",       // module requis
            "function bank() end",                       // callback
            "local obj = { go = function(self) self:go() self.x() end }",
            "x = function() end x()",
            "print(\"ok\") getPods() tostring(1)",
            "for _, f in ipairs({}) do f() end",
            "local a, b = pcall(helper, 1)"));
    }

    [Fact]
    public void Unknown_global_respects_globals_defined_through_G_and_dynamic_environments()
    {
        NoDiagnostic("_G.h1 = function() end\nh1()\n_G[\"h2\"] = print\nh2()\nrawset(_G, 'h3', print)\nh3()");
        NoDiagnostic("setmetatable(_G, { __index = function() return print end })\nanything()");
    }

    [Fact]
    public void Unknown_global_is_only_info_when_the_script_loads_other_files()
    {
        var d = Assert.Single(Of(Lint("dofile(\"lib\")\nhelperFromLib()"), "givre.unknown-global"));
        Assert.Equal(LuaDiagnosticSeverity.Info, d.Severity);
        Assert.Contains("dofile", d.Hint);
    }

    [Fact]
    public void Unknown_member_of_a_module()
    {
        var d = Assert.Single(Lint("map:getIb()"));
        Assert.Equal(("givre.unknown-member", LuaDiagnosticSeverity.Warning), (d.Code, d.Severity));
        Assert.Equal("« map:getIb » n'existe pas dans le module « map »", d.Message);
        Assert.Equal("Tu voulais dire « map:getId » ?", d.Hint);
        Assert.Equal((2, 0, 2, 9), (d.Line, d.Column, d.EndLine, d.EndColumn));
        var info = Assert.Single(Lint("inventory.zzzqqq()"));
        Assert.Equal(LuaDiagnosticSeverity.Info, info.Severity);
        Assert.Equal("« inventory.zzzqqq » n'existe pas dans le module « inventory »", info.Message);
    }

    [Fact]
    public void Unknown_member_no_false_positive()
    {
        NoDiagnostic(string.Join("\n",
            "map:getId() map:GetId() map:get_id() map.getId()",   // noms tolérants, appel point
            "function map:myHelper() end",                         // membre défini par le script
            "map:myHelper()",
            "inventory.custom = 5",
            "print(inventory.custom)",
            "local r = map:getResources()"));
        NoDiagnostic("local map = {}\nmap:whatever()");            // module masqué par une locale
        NoDiagnostic("function f(inventory) inventory:foo() end");  // paramètre du même nom
    }

    [Fact]
    public void Misspelled_module_receiver_is_reported_only_with_a_suggestion()
    {
        var d = Assert.Single(Lint("Map:getId()"));
        Assert.Equal("givre.unknown-global", d.Code);
        Assert.Equal("Tu voulais dire « map » ?", d.Hint);
        NoDiagnostic("local obj = {}\nobj:method()");
        Assert.Empty(Lint("undeclaredThing:method()"));   // aucun module proche : rien
    }

    [Fact]
    public void Callback_case()
    {
        var d = Assert.Single(Of(Lint("function onturn() end"), "givre.callback-case"));
        Assert.Equal(LuaDiagnosticSeverity.Warning, d.Severity);
        Assert.Equal("le moteur appelle « onTurn » (respecte la casse)", d.Message);
        Assert.Single(Of(Lint("function OnTurn() end"), "givre.callback-case"));
        Assert.Single(Of(Lint("Stopped = function(reason) end"), "givre.callback-case"));
        NoDiagnostic("function onTurn() end\nfunction stopped(r) end");
    }

    [Fact]
    public void Local_callback_at_top_level()
    {
        var d = Assert.Single(Of(LuaLinter.Lint("local function move() end", Names), "givre.local-callback"));
        Assert.Equal(LuaDiagnosticSeverity.Warning, d.Severity);
        Assert.Empty(Of(LuaLinter.Lint("local function move() end", Names), "givre.no-entry"));
        NoDiagnostic("function outer()\n  local function stopped() end\n  stopped()\nend");
        NoDiagnostic("local function onTurn() end\n_G.onTurn = onTurn");
    }

    [Fact]
    public void No_entry_point()
    {
        var d = Assert.Single(LuaLinter.Lint("MAX_PODS = 90\nprint(1)", Names));
        Assert.Equal(("givre.no-entry", LuaDiagnosticSeverity.Info), (d.Code, d.Severity));
        Assert.Equal("aucun move() ni callback : le script ne pilotera rien", d.Message);
        Assert.Equal((1, 0), (d.Line, d.Column));
        Assert.Empty(LuaLinter.Lint("function move() end", Names));
        Assert.Empty(LuaLinter.Lint("function onTurn() end", Names));
        Assert.Empty(LuaLinter.Lint("bank = function() end", Names));
        Assert.Empty(LuaLinter.Lint("local M = {}\nfunction M.f() end\nreturn M", Names));   // bibliothèque
        Assert.Empty(LuaLinter.Lint("", Names));
        Assert.Empty(LuaLinter.Lint("  \n\t\n", Names));
        Assert.Empty(LuaLinter.Lint("-- rien que des commentaires\n--[[ x ]]", Names));
    }

    [Fact]
    public void Diagnostics_are_sorted_by_line_then_column()
    {
        var d = LuaLinter.Lint("x += 1 y += 2\nif a != b then end\nfoo()", Names);
        var keys = d.Select(x => (x.Line, x.Column)).ToList();
        Assert.Equal(keys.OrderBy(k => k.Line).ThenBy(k => k.Column).ToList(), keys);
        Assert.True(d.Count >= 4);
    }

    // ------------------------------------------------------------------ avec le vrai catalogue

    [Fact]
    public void Real_catalog_no_error_on_any_doc_sample()
    {
        var cat = LuaToolingTestData.Catalog;
        foreach (var (name, code) in LuaToolingTestData.DocSamples())
        {
            // « unavailable-api » (F07) est un avertissement VOULU : certains exemples de la doc appellent des liaisons qui lèvent une erreur.
            var d = LuaLinter.Lint(code, cat).Where(x => x.Code != "givre.unavailable-api").ToList();
            Assert.True(d.All(x => x.Severity == LuaDiagnosticSeverity.Info),
                name + " : " + string.Join(" | ", d.Where(x => x.Severity != LuaDiagnosticSeverity.Info).Select(x => $"{x.Line}:{x.Column} {x.Code} {x.Message}")));
        }
    }

    [Fact]
    public void Real_catalog_typical_mistakes()
    {
        var cat = LuaToolingTestData.Catalog;
        var d = LuaLinter.Lint("function move()\n  local lvl = getJobLevell(26)\n  map:getIdd()\n  zaap:goto(1)\nend", cat);
        Assert.Equal("Tu voulais dire « getJobLevel » ?", Assert.Single(Of(d, "givre.unknown-global")).Hint);
        Assert.Equal("Tu voulais dire « map:getId » ?", Assert.Single(Of(d, "givre.unknown-member")).Hint);
        Assert.Single(Of(d, "givre.goto-member"));
        // noms tolérants, alias, stubs, bibliothèque standard
        var ok = LuaLinter.Lint("function move()\n  sale:sellItem(1, 2, 3) sale:SellItem(1, 2, 3) map:change_map('left')\n  d2data:monsterName(31) character:askShieldCode() mount:toggleRiding()\n  local s = string.format('%d', getPods()) print(tostring(getPodsPercent()))\n  local j = json.serialize({}) local b = bit32.band(1, 3)\nend", cat);
        // askShieldCode / toggleRiding sont des liaisons indisponibles (F07) : signalées à part, jamais « inconnues ».
        ok = ok.Where(x => x.Code != "givre.unavailable-api").ToList();
        Assert.True(ok.Count == 0, string.Join(" | ", ok.Select(x => x.Code + " " + x.Message)));
    }

    [Fact]
    public void Lint_never_throws_on_garbage()
    {
        var rnd = new Random(42);
        string[] parts = ["function", "end", "if", "then", "(", ")", "{", "}", "[[", "]]", "--[[", "'", "\"", "\n", " ", "x", ":", ".", "=", "goto", "repeat", "until", "!=", "&&", "//", "map", "zaap", "continue", "local", "for", "do", ",", "|"];
        for (int round = 0; round < 300; round++)
        {
            var sb = new StringBuilder();
            int n = rnd.Next(0, 60);
            for (int i = 0; i < n; i++) sb.Append(parts[rnd.Next(parts.Length)]).Append(rnd.Next(3) == 0 ? " " : "");
            string s = sb.ToString();
            var d = LuaLinter.Lint(s, LuaToolingTestData.Catalog);
            foreach (var x in d) Assert.True(x.Line >= 1 && x.Column >= 0 && (x.EndLine > x.Line || (x.EndLine == x.Line && x.EndColumn >= x.Column)));
            LuaFormatter.Format(s);
            LuaFolding.Compute(s);
        }
        Assert.Empty(LuaLinter.Lint(null!, Names));
        Assert.NotNull(LuaLinter.Lint("x()", null!));
    }

    private static string Script(string body) =>
        "function move()" + Environment.NewLine + "  " + body + Environment.NewLine + "end" + Environment.NewLine;

    // F07 : 55 signatures lèvent désormais une erreur Lua explicite. Le linter l'annonce à la frappe, comme une fonction inconnue.
    [Theory]
    [InlineData("fightSlave:entity()", "fightSlave:entity")]
    [InlineData("fightSlave:castSpellOnCell(12, 300)", "fightSlave:castSpellOnCell")]
    [InlineData("fightSlave:ENTITY()", "fightSlave:ENTITY")]
    public void Unavailable_api_is_flagged_with_a_warning_on_the_real_catalog(string call, string shown)
    {
        var d = LuaLinter.Lint(Script(call), LuaToolingTestData.Catalog)
            .Where(x => x.Code == "givre.unavailable-api").ToList();
        var one = Assert.Single(d);
        Assert.Equal(LuaDiagnosticSeverity.Warning, one.Severity);
        Assert.Contains(shown, one.Message);
        Assert.Contains("pcall", one.Hint);
    }

    [Theory]
    [InlineData("fightBasic:playTurn(2)")]
    [InlineData("map:getId()")]
    [InlineData("character:getLifePointsP()")]
    public void Working_api_is_not_flagged_as_unavailable(string call)
    {
        var d = LuaLinter.Lint(Script(call), LuaToolingTestData.Catalog);
        Assert.DoesNotContain(d, x => x.Code == "givre.unavailable-api");
    }

    [Fact]
    public void A_script_that_defines_its_own_member_is_never_flagged()
    {
        var text = string.Join(Environment.NewLine, "fightSlave = {}", "function fightSlave:entity() return 1 end",
            "function move()", "  fightSlave:entity()", "end") + Environment.NewLine;
        Assert.DoesNotContain(LuaLinter.Lint(text, LuaToolingTestData.Catalog), x => x.Code == "givre.unavailable-api");
    }

    // Scripts venus de l'ancien Givre : io / os.execute / loadfile existaient là-bas, pas dans le bac à sable actuel.
    [Theory]
    [InlineData("io.popen('dir')", "io.popen")]
    [InlineData("io.lines('a.txt')", "io.lines")]
    [InlineData("os.execute('dir')", "os.execute")]
    [InlineData("os.remove('a.txt')", "os.remove")]
    [InlineData("os.getenv('PATH')", "os.getenv")]
    [InlineData("package.loadlib('a', 'b')", "package.loadlib")]
    [InlineData("loadfile('a.lua')", "loadfile")]
    public void Functions_missing_from_the_sandbox_are_flagged(string call, string shown)
    {
        var one = Assert.Single(LuaLinter.Lint(Script(call), LuaToolingTestData.Catalog).Where(x => x.Code == "lua.sandbox-unavailable"));
        Assert.Equal(LuaDiagnosticSeverity.Warning, one.Severity);
        Assert.Contains(shown, one.Message);
    }

    [Theory]
    [InlineData("local t = os.time()")]
    [InlineData("local c = os.clock()")]
    [InlineData("local d = os.date('%H:%M')")]
    [InlineData("local f = io.open('route.txt', 'r')")]   // fourni par l'hôte (Road Creator)
    [InlineData("local l = package.loaded")]
    [InlineData("require('lib')")]                          // fourni par l'hôte
    [InlineData("dofile('a.lua')")]                         // fourni par l'hôte
    [InlineData("local x = tostring(1) .. string.format('%d', 2)")]
    public void Available_functions_are_not_flagged(string line)
    {
        Assert.DoesNotContain(LuaLinter.Lint(Script(line), LuaToolingTestData.Catalog), x => x.Code == "lua.sandbox-unavailable");
    }

    [Fact]
    public void A_script_that_declares_its_own_io_or_loadfile_is_left_alone()
    {
        var text = string.Join(Environment.NewLine, "local io = { popen = function() end }", "local function loadfile(n) return n end",
            "function move()", "  io.popen('x')", "  loadfile('y')", "end") + Environment.NewLine;
        Assert.DoesNotContain(LuaLinter.Lint(text, LuaToolingTestData.Catalog), x => x.Code == "lua.sandbox-unavailable");
    }

    // Le moteur : ce que MoonSharp ne fournit PAS (et que l'hôte ne remplace pas) doit bien être nil.
    [Fact]
    public void Sandbox_list_matches_the_real_runtime()
    {
        var script = new MoonSharp.Interpreter.Script(MoonSharp.Interpreter.CoreModules.Preset_SoftSandbox);
        string TypeOf(string expr) => script.DoString("return type(" + expr + ")").String;
        Assert.Equal("nil", TypeOf("loadfile"));
        foreach (var m in LuaLinter.SandboxPackageAvailable) Assert.Equal("table", TypeOf("package." + m));
        foreach (var m in new[] { "loadlib", "path", "cpath", "preload", "searchers", "config" }) Assert.Equal("nil", TypeOf("package." + m));
        foreach (var f in LuaLinter.SandboxOsAvailable)
            Assert.Equal("function", TypeOf("os." + f));
        foreach (var f in new[] { "execute", "remove", "rename", "exit", "getenv", "tmpname", "setlocale" })
            Assert.Equal("nil", TypeOf("os." + f));
        Assert.Equal("nil", TypeOf("io"));   // l'hôte y ajoute SEULEMENT open (test suivant)
    }

    // L'hôte : ce qu'il ajoute au bac à sable est exactement ce que le linter laisse passer, et il n'ajoute pas loadfile/package.
    [Fact]
    public void Sandbox_additions_match_the_host_source()
    {
        string root = GivreClone.Tests.MappingScan.RepoRoot();
        string host = string.Concat(System.IO.Directory.EnumerateFiles(System.IO.Path.Combine(root, "GivreClone.App", "Services"), "GivreLuaHost*.cs")
            .Select(System.IO.File.ReadAllText));
        Assert.Contains("""Globals["require"]""", host);
        Assert.Contains("""Globals["dofile"]""", host);
        Assert.Contains("""Globals["io"]""", host);
        Assert.Contains("""io["open"]""", host);
        Assert.DoesNotContain("""Globals["loadfile"]""", host);
        Assert.DoesNotContain("""Globals["package"]""", host);
        Assert.DoesNotContain("""io["popen"]""", host);
        Assert.DoesNotContain("""io["lines"]""", host);
        Assert.Equal(new[] { "open" }, LuaLinter.SandboxIoAvailable);
    }
}
