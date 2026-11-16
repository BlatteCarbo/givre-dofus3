using System.Linq;
using GivreClone.Engine.LuaTooling;
using Xunit;

namespace GivreClone.Tests.LuaTooling;

public class LuaFoldingTests
{
    private static string L(params string[] lines) => string.Join("\n", lines);

    // Texte replié : de la fin de la 1re ligne à la fin du fermant.
    private static string[] Folded(string s) => LuaFolding.Compute(s).Select(f => s[f.StartOffset..f.EndOffset]).ToArray();

    [Fact]
    public void Function_fold_keeps_the_first_line_visible_and_ends_after_end()
    {
        string s = L("function move()", "    gatherAll()", "end", "x = 1");
        var f = Assert.Single(LuaFolding.Compute(s));
        Assert.Equal("function move()".Length, f.StartOffset);
        Assert.Equal(s.IndexOf("end") + 3, f.EndOffset);
        Assert.Equal("… end", f.Title);
    }

    [Fact]
    public void Single_line_blocks_are_not_folded()
    {
        Assert.Empty(LuaFolding.Compute("function f() return 1 end\nif x then y() end\nlocal t = { 1, 2 }"));
        Assert.Empty(LuaFolding.Compute(""));
    }

    [Fact]
    public void Nested_blocks_are_sorted_by_start_outer_first()
    {
        string s = L("function f()", "    for i = 1, 3 do", "        if i then", "            x()", "        end", "    end", "end");
        var folds = LuaFolding.Compute(s);
        Assert.Equal(3, folds.Count);
        Assert.True(folds[0].StartOffset < folds[1].StartOffset && folds[1].StartOffset < folds[2].StartOffset);
        Assert.EndsWith("\nend", Folded(s)[0]);
        Assert.StartsWith("\n        if i then", Folded(s)[1]);
    }

    [Fact]
    public void Loops_repeat_do_and_if_with_elseif_each_give_one_fold()
    {
        string s = L("while x do", "  a()", "end", "for k, v in pairs(t) do", "  b()", "end", "repeat", "  c()", "until done",
                     "do", "  d()", "end", "if a then", "  e()", "elseif b then", "  f()", "else", "  g()", "end");
        var folds = LuaFolding.Compute(s);
        Assert.Equal(5, folds.Count);
        Assert.Contains(folds, f => f.Title == "… until" && s[..f.EndOffset].EndsWith("until"));
        var ifFold = folds.Last();
        Assert.Equal(s.Length, ifFold.EndOffset);
        Assert.Contains("elseif", s[ifFold.StartOffset..ifFold.EndOffset]);
    }

    [Fact]
    public void Multi_line_tables_and_long_comments()
    {
        string s = L("local t = {", "  a = 1,", "  { b = 2 },", "}", "--[==[", "  notes", "]==]", "--[[ une ligne ]]");
        var folds = LuaFolding.Compute(s);
        Assert.Equal(2, folds.Count);
        Assert.Equal("… }", folds[0].Title);
        Assert.Equal("… ]==]", folds[1].Title);
        Assert.EndsWith("]==]", s[..folds[1].EndOffset]);
    }

    [Fact]
    public void Strings_and_comments_do_not_open_blocks()
    {
        string s = L("print(\"function if do\") -- repeat", "x = [[", "end", "]]");
        Assert.Empty(LuaFolding.Compute(s));
    }

    [Fact]
    public void Unbalanced_code_is_tolerated()
    {
        // end en trop ignoré ; function sans end : pas de repli pour elle, mais le if interne reste repliable.
        string extra = L("end", "if x then", "  y()", "end", "end");
        Assert.Single(LuaFolding.Compute(extra));
        string missing = L("function f()", "  if x then", "    y()", "  end");
        var f = Assert.Single(LuaFolding.Compute(missing));
        Assert.StartsWith("\n    y()", missing[f.StartOffset..f.EndOffset]);
        Assert.Empty(LuaFolding.Compute("--[[ jamais fermé\nx = 1"));
        Assert.Empty(LuaFolding.Compute("x = 'abc\nfunction"));
    }

    [Fact]
    public void Callback_argument_and_crlf()
    {
        string s = "foo(function()\r\n  bar()\r\nend)\r\n";
        var f = Assert.Single(LuaFolding.Compute(s));
        Assert.Equal("foo(function()".Length, f.StartOffset);   // avant le \r
        Assert.Equal(s.IndexOf("end") + 3, f.EndOffset);
    }

    [Fact]
    public void Doc_samples_fold_without_error_and_with_valid_ranges()
    {
        foreach (var (_, code) in LuaToolingTestData.DocSamples())
            foreach (var f in LuaFolding.Compute(code))
                Assert.True(f.StartOffset < f.EndOffset && f.EndOffset <= code.Length);
    }
}
