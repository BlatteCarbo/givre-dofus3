using GivreClone.Engine.LuaTooling;
using Xunit;

namespace GivreClone.Tests.LuaTooling;

public class LuaErrorLocationParserTests
{
    private static LuaErrorLocation P(string s)
    {
        Assert.True(LuaErrorLocationParser.TryParse(s, out var loc), s);
        Assert.NotNull(loc);
        return loc!;
    }

    [Fact]
    public void Same_line_range()
    {
        var l = P("chunk:(3,2-8): unexpected symbol near 'x'");
        Assert.Equal(new LuaErrorLocation("chunk", 3, 2, 3, 8, "unexpected symbol near 'x'"), l);
    }

    [Fact]
    public void Multi_line_range()
    {
        var l = P("recolte.lua:(1,5-3,4): attempt to call a nil value");
        Assert.Equal(new LuaErrorLocation("recolte.lua", 1, 5, 3, 4, "attempt to call a nil value"), l);
    }

    [Fact]
    public void Single_position()
    {
        var l = P("chunk:(4,3): 'end' expected near '<eof>'");
        Assert.Equal(("chunk", 4, 3), (l.Chunk, l.Line, l.Column));
        Assert.Null(l.EndLine);
        Assert.Null(l.EndColumn);
        Assert.Equal("'end' expected near '<eof>'", l.Message);
    }

    [Fact]
    public void Standard_lua_line_format()
    {
        var l = P("input:12: attempt to index a nil value");
        Assert.Equal(new LuaErrorLocation("input", 12, 0, null, null, "attempt to index a nil value"), l);
    }

    [Theory]
    [InlineData("lib:utils:(3,2-8): boom", "lib:utils")]
    [InlineData("file:x:(1,1-3): attempt to call a nil value", "file:x")]
    [InlineData("[MZN] - Money.lua:(10,4-9): boom", "[MZN] - Money.lua")]
    [InlineData("chunk_0:(1,0-2): boom", "chunk_0")]
    public void Chunk_names_may_contain_colons_spaces_and_brackets(string s, string chunk)
    {
        Assert.Equal(chunk, P(s).Chunk);
    }

    [Fact]
    public void Embedded_in_a_longer_line()
    {
        var l = P("erreur d'exécution : recolte.lua:(148,4-31): attempt to call a nil value");
        Assert.Equal(new LuaErrorLocation("recolte.lua", 148, 4, 148, 31, "attempt to call a nil value"), l);
    }

    [Fact]
    public void Last_location_wins_for_a_rethrown_error()
    {
        var l = P("outer.lua:(10,2-20): inner.lua:(3,4-8): boom");
        Assert.Equal(new LuaErrorLocation("inner.lua", 3, 4, 3, 8, "boom"), l);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no location here")]
    [InlineData("[12:30:45] démarrage")]
    [InlineData("chunk:(x,2): nope")]
    [InlineData("chunk:(1,2) missing colon")]
    public void Unparseable_inputs(string? s)
    {
        Assert.False(LuaErrorLocationParser.TryParse(s, out var loc));
        Assert.Null(loc);
    }

    // ---- CalleeNameAt (plages réelles relevées sur MoonSharp 2.0.0 : la plage couvre souvent les arguments « (…) »)

    [Theory]
    [InlineData("local x = 1\n    getJobLevell(26)", 2, 16, 2, 20, "getJobLevell")]
    [InlineData("local m = {}\nm:getPodz()", 2, 2, 2, 11, "m:getPodz")]
    [InlineData("print(foo(1))", 1, 5, 1, 13, "foo")]
    [InlineData("foo(print(1))", 1, 3, 1, 13, "foo")]
    [InlineData("local x = foo(1)", 1, 0, 1, 16, "foo")]
    [InlineData("print(1, foo 'a')", 1, 5, 1, 17, "foo")]
    [InlineData("foo{1}", 1, 3, 1, 6, "foo")]
    [InlineData("local t = {} t.fn = nil t.fn()", 1, 28, 1, 30, "t.fn")]
    [InlineData("print(\n  foo(\n 1))", 1, 5, 3, 4, "foo")]
    [InlineData("x()", 1, 1, 1, 3, "x")]
    public void CalleeNameAt_moonsharp_spans(string src, int l1, int c1, int l2, int c2, string expected)
    {
        var loc = new LuaErrorLocation("c", l1, c1, l2, c2, "attempt to call a nil value");
        Assert.Equal(expected, LuaErrorLocationParser.CalleeNameAt(src, loc));
    }

    [Fact]
    public void CalleeNameAt_prefers_names_unknown_to_the_catalog()
    {
        const string src = "local n = printSuccess(getJobLevell(26))";
        var loc = new LuaErrorLocation("c", 1, 0, 1, src.Length, "attempt to call a nil value");
        Assert.Equal("getJobLevell", LuaErrorLocationParser.CalleeNameAt(src, loc, LuaToolingTestData.Catalog));
    }

    [Fact]
    public void CalleeNameAt_without_a_call_or_out_of_range_is_null()
    {
        Assert.Null(LuaErrorLocationParser.CalleeNameAt("local x = 1 + y", new LuaErrorLocation("c", 1, 0, 1, 15, "attempt to perform arithmetic on a nil value")));
        Assert.Null(LuaErrorLocationParser.CalleeNameAt("", new LuaErrorLocation("c", 1, 0, null, null, "x")));
        Assert.Null(LuaErrorLocationParser.CalleeNameAt("f()", new LuaErrorLocation("c", 99, 0, null, null, "x")));
        Assert.Null(LuaErrorLocationParser.CalleeNameAt("function f() end", new LuaErrorLocation("c", 1, 0, 1, 16, "x")));   // définition
    }

    [Fact]
    public void CalleeNameAt_without_end_column_scans_the_line()
    {
        Assert.Equal("getJobLevell", LuaErrorLocationParser.CalleeNameAt("a = 1\nb = getJobLevell(26)", new LuaErrorLocation("c", 2, 0, null, null, "attempt to call a nil value")));
    }

    // ---- FrenchHint

    [Theory]
    [InlineData("attempt to call a nil value", "fonction inexistante (faute de frappe ? fonction pas encore définie ?)")]
    [InlineData("attempt to index a nil value", "nil")]
    [InlineData("attempt to perform arithmetic on a nil value", "calcul")]
    [InlineData("attempt to concatenate a nil value", "concaténation")]
    [InlineData("attempt to concatenate a table value", "tostring")]
    [InlineData("attempt to compare number with nil", "comparaison")]
    [InlineData("unexpected symbol near 'x'", "symbole inattendu")]
    [InlineData("unexpected symbol near '<eof>'", "fin du fichier")]
    [InlineData("'=' expected near 'x'", "« = »")]
    [InlineData("'then' expected near 'y'", "then")]
    [InlineData("unfinished string near 'abc'", "guillemet")]
    [InlineData("unfinished long string near 'abc'", "[[")]
    [InlineData("unfinished long comment near 'x'", "--[[")]
    [InlineData("'end' expected near '<eof>'", "end")]
    [InlineData("<eof> expected near 'end'", "en trop")]
    [InlineData("bad argument #1 to 'rep' (string expected, got no value)", "n°1 passé à « rep »")]
    [InlineData("no visible label 'nowhere' for <goto> at line 1", "goto")]
    [InlineData("invalid escape sequence near '\\q'", "échappement")]
    public void FrenchHint_known_messages(string msg, string expectedFragment)
    {
        var h = LuaErrorLocationParser.FrenchHint(msg);
        Assert.NotNull(h);
        Assert.Contains(expectedFragment, h);
    }

    [Fact]
    public void FrenchHint_end_expected_to_close_names_the_block_and_line()
    {
        var h = LuaErrorLocationParser.FrenchHint("'end' expected (to close 'function' at line 12) near '<eof>'");
        Assert.Equal("il manque un « end » pour fermer le « function » ouvert ligne 12", h);
    }

    [Theory]
    [InlineData("")]
    [InlineData("boom")]
    [InlineData("quelque chose d'autre")]
    public void FrenchHint_unknown_messages_are_null(string msg)
    {
        Assert.Null(LuaErrorLocationParser.FrenchHint(msg));
    }
}
