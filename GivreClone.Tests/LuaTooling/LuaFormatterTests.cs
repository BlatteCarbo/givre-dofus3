using System;
using System.Linq;
using GivreClone.Engine.LuaTooling;
using Xunit;

namespace GivreClone.Tests.LuaTooling;

public class LuaFormatterTests
{
    private static string L(params string[] lines) => string.Join("\n", lines) + "\n";

    private static string Fmt(string s, LuaFormatOptions? o = null)
    {
        string once = LuaFormatter.Format(s, o);
        Assert.Equal(once, LuaFormatter.Format(once, o));   // idempotent, toujours
        return once;
    }

    private static string NonWhite(string s) => new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());

    [Fact]
    public void Reindents_nested_blocks()
    {
        string src = L(
            "function move()",
            "for i = 1, 3 do",
            "if i == 2 then",
            "print(i)",
            "end",
            "end",
            "while true do",
            "  break",
            "      end",
            "end");
        Assert.Equal(L(
            "function move()",
            "    for i = 1, 3 do",
            "        if i == 2 then",
            "            print(i)",
            "        end",
            "    end",
            "    while true do",
            "        break",
            "    end",
            "end"), Fmt(src));
    }

    [Fact]
    public void Else_and_elseif_dedent_their_own_line()
    {
        string src = L("if a then", "x()", "elseif b then", "y()", "else", "z()", "end");
        Assert.Equal(L("if a then", "    x()", "elseif b then", "    y()", "else", "    z()", "end"), Fmt(src));
    }

    [Fact]
    public void One_line_blocks_do_not_change_indentation()
    {
        string src = L("function f()", "if x then y() end", "local g = function() end", "for i=1,2 do print(i) end", "return 1", "end");
        Assert.Equal(L("function f()", "    if x then y() end", "    local g = function() end", "    for i=1,2 do print(i) end", "    return 1", "end"), Fmt(src));
    }

    [Fact]
    public void Function_expressions_and_callbacks_passed_as_arguments_indent_one_level()
    {
        string src = L("local f = function(a)", "return a", "end", "foo(function()", "bar()", "end)", "pcall(function() x() end)");
        Assert.Equal(L("local f = function(a)", "    return a", "end", "foo(function()", "    bar()", "end)", "pcall(function() x() end)"), Fmt(src));
    }

    [Fact]
    public void Tables_multi_line_nested_and_adjacent()
    {
        string src = L(
            "local t = {",
            "{ map = \"1,2\", path = \"left\" },",
            "{",
            "map = \"3,4\",",
            "custom = function()",
            "gatherAll()",
            "end,",
            "}, {",
            "map = 5",
            "},",
            "}");
        Assert.Equal(L(
            "local t = {",
            "    { map = \"1,2\", path = \"left\" },",
            "    {",
            "        map = \"3,4\",",
            "        custom = function()",
            "            gatherAll()",
            "        end,",
            "    }, {",
            "        map = 5",
            "    },",
            "}"), Fmt(src));
    }

    [Fact]
    public void Repeat_until_and_chained_ends()
    {
        string src = L("repeat", "x = x + 1", "until x > 3", "function a()", "if b then", "for i=1,2 do", "do", "c()", "end end", "end", "end");
        Assert.Equal(L("repeat", "    x = x + 1", "until x > 3", "function a()", "    if b then", "        for i=1,2 do", "            do",
                       "                c()", "        end end", "    end", "end"), Fmt(src));
    }

    [Fact]
    public void Multi_line_call_arguments_and_closing_paren()
    {
        string src = L("local x = foo(", "a,", "b", ")");
        Assert.Equal(L("local x = foo(", "    a,", "    b", ")"), Fmt(src));
    }

    [Fact]
    public void Multi_line_long_strings_and_comments_are_left_verbatim()
    {
        string src = "function f()\n" +
                     "local s = [[\n" +
                     "   keep   \n" +
                     "end   if then\n" +
                     "  ]] .. g(\n" +
                     "1)\n" +
                     "--[==[\n" +
                     "      function x()\n" +
                     "]==]\n" +
                     "return s\n" +
                     "end\n";
        string expected = "function f()\n" +
                          "    local s = [[\n" +
                          "   keep   \n" +          // espaces de fin DANS la chaîne : conservés
                          "end   if then\n" +
                          "  ]] .. g(\n" +          // commence dans la chaîne : recopiée telle quelle
                          "        1)\n" +
                          "    --[==[\n" +
                          "      function x()\n" +
                          "]==]\n" +
                          "    return s\n" +
                          "end\n";
        Assert.Equal(expected, Fmt(src));
    }

    [Fact]
    public void Keywords_inside_strings_and_comments_do_not_open_blocks()
    {
        string src = L("print(\"function end if then\") -- do repeat", "x = 'end'", "y = 1");
        Assert.Equal(src, Fmt(src));
    }

    [Fact]
    public void Trailing_whitespace_blank_lines_and_final_newline()
    {
        string src = "x = 1   \n\n\n\n\ny = 2\t\n\n\n";
        Assert.Equal("x = 1\n\n\ny = 2\n", Fmt(src));
        Assert.Equal("x = 1\n\ny = 2\n", Fmt(src, new LuaFormatOptions(MaxConsecutiveBlankLines: 1)));
        Assert.Equal("a = 1\n", Fmt("a = 1"));
        Assert.Equal("a = 1", Fmt("a = 1", new LuaFormatOptions(EnsureFinalNewline: false)));
        Assert.Equal("", Fmt(""));
        Assert.Equal("", Fmt("   \n\n  "));
    }

    [Fact]
    public void Crlf_line_endings_are_preserved()
    {
        string src = "if x then\r\ny()\r\nend";
        Assert.Equal("if x then\r\n    y()\r\nend\r\n", Fmt(src));
    }

    [Fact]
    public void Tabs_and_indent_size_options()
    {
        string src = L("if x then", "y()", "end");
        Assert.Equal(L("if x then", "\ty()", "end"), Fmt(src, new LuaFormatOptions(UseTabs: true)));
        Assert.Equal(L("if x then", "  y()", "end"), Fmt(src, new LuaFormatOptions(IndentSize: 2)));
    }

    [Fact]
    public void Comment_lines_follow_the_code_indentation()
    {
        Assert.Equal(L("function f()", "    -- note", "    x()", "end"), Fmt(L("function f()", "-- note", "      x()", "end")));
    }

    [Fact]
    public void Unbalanced_code_is_formatted_best_effort_without_losing_characters()
    {
        string extraEnd = L("x()", "end", "  end", "function f()", "y()", "end");
        string f1 = Fmt(extraEnd);
        Assert.Equal(NonWhite(extraEnd), NonWhite(f1));
        Assert.StartsWith("x()\nend\nend\nfunction f()\n    y()\nend", f1);

        string missingEnd = L("function f()", "if x then", "y()", "end");
        string f2 = Fmt(missingEnd);
        Assert.Equal(NonWhite(missingEnd), NonWhite(f2));

        string unterminated = L("local s = 'abc", "if x then", "y()", "end");
        string f3 = Fmt(unterminated);
        Assert.Equal(NonWhite(unterminated), NonWhite(f3));
        Assert.Contains("    y()", f3);

        string openLong = "x = [[ never\n   closed   \n";
        Assert.Equal(openLong, LuaFormatter.Format(openLong));   // jeton ouvert jusqu'à la fin : aucune retouche en fin de texte
    }

    [Fact]
    public void Reformatting_the_doc_examples_after_stripping_indentation_is_faithful_and_safe()
    {
        int identical = 0, total = 0;
        var nonIdentical = new System.Collections.Generic.List<string>();
        foreach (var (name, code) in LuaToolingTestData.DocSamples())
        {
            total++;
            var stripped = string.Join("\n", code.Split('\n').Select(l => l.TrimStart()));
            var f = LuaFormatter.Format(stripped, new LuaFormatOptions(EnsureFinalNewline: false));
            Assert.Equal(NonWhite(code), NonWhite(f));
            Assert.Equal(f, LuaFormatter.Format(f, new LuaFormatOptions(EnsureFinalNewline: false)));
            var tokensBefore = LuaLexer.Tokenize(code).Select(t => (t.Kind, LuaLexer.Slice(code, t).Trim())).ToList();
            var tokensAfter = LuaLexer.Tokenize(f).Select(t => (t.Kind, LuaLexer.Slice(f, t).Trim())).ToList();
            Assert.True(tokensBefore.SequenceEqual(tokensAfter), name);
            if (f == string.Join("\n", code.Split('\n').Select(l => l.TrimEnd()))) identical++;
            else
            {
                var expectedLines = string.Join("\n", code.Split('\n').Select(l => l.TrimEnd())).Split('\n');
                var actualLines = f.Split('\n');
                int first = Enumerable.Range(0, Math.Min(expectedLines.Length, actualLines.Length))
                    .FirstOrDefault(i => expectedLines[i] != actualLines[i], -1);
                nonIdentical.Add(first < 0 ? name + " (nombre de lignes)" :
                    $"{name} L{first + 1} attendu=[{expectedLines[first]}] formaté=[{actualLines[first]}]");
            }
        }
        Assert.True(total > 100);
        // Six exemples publiés ont une indentation atypique (blocs insérés avec un retrait
        // arbitraire), mais ont déjà passé les contrôles ci-dessus de tokens, non-blanc et idempotence.
        // N'autoriser que ces écarts connus : un nouvel écart doit encore faire échouer ce test.
        string[] knownNonCanonical = { "code:mod-fightaction", "code:mod-fightcharacter", "code:mod-bank",
            "ex:sc.accounts", "ex:sc.load", "code:declarative" };
        Assert.True(identical >= total - knownNonCanonical.Length &&
                    nonIdentical.All(diff => knownNonCanonical.Any(name => diff.StartsWith(name + " L", StringComparison.Ordinal))),
            $"{identical}/{total} exemples reformatés à l'identique ; différences : {string.Join(", ", nonIdentical)}");
    }

    [Fact]
    public void DetectOptions_guesses_tabs_and_size()
    {
        Assert.Equal(new LuaFormatOptions(IndentSize: 4, UseTabs: true), LuaFormatter.DetectOptions(L("if x then", "\ty()", "\tif z then", "\t\tw()", "\tend", "end")));
        Assert.Equal(new LuaFormatOptions(IndentSize: 2), LuaFormatter.DetectOptions(L("if x then", "  y()", "  if z then", "    w()", "  end", "end")));
        Assert.Equal(new LuaFormatOptions(IndentSize: 4), LuaFormatter.DetectOptions(L("if x then", "    y()", "    if z then", "        w()", "    end", "end")));
        Assert.Equal(new LuaFormatOptions(), LuaFormatter.DetectOptions(""));
        Assert.Equal(new LuaFormatOptions(), LuaFormatter.DetectOptions("x = 1"));
        // L'indentation interne d'une chaîne longue ne compte pas.
        Assert.Equal(new LuaFormatOptions(IndentSize: 4), LuaFormatter.DetectOptions(L("s = [[", "  a", "    b", "  c", "]]", "if x then", "    y()", "end")));
    }

    [Theory]
    [InlineData("if x then", 1)]
    [InlineData("function f()", 1)]
    [InlineData("local function f(a, b)", 1)]
    [InlineData("for i = 1, 10 do", 1)]
    [InlineData("while true do", 1)]
    [InlineData("repeat", 1)]
    [InlineData("local t = {", 1)]
    [InlineData("foo(", 1)]
    [InlineData("else", 1)]
    [InlineData("elseif y then", 1)]
    [InlineData("foo(function()", 1)]
    [InlineData("}, {", 1)]
    [InlineData("if x then y() end", 0)]
    [InlineData("local f = function() end", 0)]
    [InlineData("end", 0)]
    [InlineData("end)", 0)]
    [InlineData("until x", 0)]
    [InlineData("x = 1", 0)]
    [InlineData("print(\"if x then\") -- function", 0)]
    [InlineData("", 0)]
    [InlineData("x = 1 end", 0)]
    public void IndentDeltaAfterLine_cases(string line, int expected)
    {
        Assert.Equal(expected, LuaFormatter.IndentDeltaAfterLine(line));
    }

    [Theory]
    [InlineData("end", true)]
    [InlineData("end)", true)]
    [InlineData("else", true)]
    [InlineData("elseif x then", true)]
    [InlineData("until done", true)]
    [InlineData("}", true)]
    [InlineData(")", true)]
    [InlineData("]", true)]
    [InlineData("  end", true)]
    [InlineData("ending = 1", false)]
    [InlineData("endless()", false)]
    [InlineData("elsewhere = 2", false)]
    [InlineData("x = 1", false)]
    [InlineData("", false)]
    [InlineData("en", false)]
    public void IsDedentLine_cases(string line, bool expected)
    {
        Assert.Equal(expected, LuaFormatter.IsDedentLine(line));
    }
}
