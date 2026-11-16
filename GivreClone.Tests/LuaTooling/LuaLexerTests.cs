using System;
using System.Linq;
using System.Text;
using GivreClone.Engine.LuaTooling;
using Xunit;

namespace GivreClone.Tests.LuaTooling;

public class LuaLexerTests
{
    private static (LuaTokenKind Kind, string Text)[] Toks(string s) =>
        LuaLexer.Tokenize(s).Select(t => (t.Kind, LuaLexer.Slice(s, t))).ToArray();

    [Fact]
    public void Basic_statement_kinds_and_positions()
    {
        const string s = "local x = 1";
        var t = LuaLexer.Tokenize(s);
        Assert.Equal(new[] { LuaTokenKind.Keyword, LuaTokenKind.Identifier, LuaTokenKind.Operator, LuaTokenKind.Number }, t.Select(x => x.Kind));
        Assert.Equal(6, t[1].Start);
        Assert.Equal(1, t[1].Length);
        Assert.Equal(7, t[1].End);
        Assert.All(t, x => Assert.Equal(1, x.Line));
        Assert.Equal(10, t[3].Column);
    }

    [Fact]
    public void Whitespace_and_newlines_are_not_tokens_and_lines_are_counted_for_all_newline_styles()
    {
        const string s = "a\r\n  b\n\tc\rd";
        var t = LuaLexer.Tokenize(s);
        Assert.Equal(4, t.Count);
        Assert.Equal((1, 0), (t[0].Line, t[0].Column));
        Assert.Equal((2, 2), (t[1].Line, t[1].Column));
        Assert.Equal((3, 1), (t[2].Line, t[2].Column));
        Assert.Equal((4, 0), (t[3].Line, t[3].Column));
    }

    [Fact]
    public void Keywords_are_the_22_of_lua_52()
    {
        Assert.Equal(22, LuaLexer.Keywords.Count);
        foreach (var k in new[] { "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if", "in", "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while" })
            Assert.True(LuaLexer.IsKeyword(k), k);
        Assert.False(LuaLexer.IsKeyword("continue"));
        Assert.False(LuaLexer.IsKeyword("End"));
        Assert.False(LuaLexer.IsKeyword(""));
        Assert.Equal(LuaTokenKind.Keyword, LuaLexer.Tokenize("goto")[0].Kind);
        Assert.Equal(LuaTokenKind.Identifier, LuaLexer.Tokenize("ending")[0].Kind);
    }

    [Theory]
    [InlineData("[[abc]]")]
    [InlineData("[==[ a ]] ]=] b ]==]")]
    [InlineData("[=[\nline ]] still\n]=]")]
    [InlineData("[[]]")]
    public void Long_strings_of_any_level_are_one_token(string s)
    {
        var t = LuaLexer.Tokenize(s);
        Assert.Single(t);
        Assert.Equal(LuaTokenKind.String, t[0].Kind);
        Assert.False(t[0].Unterminated);
        Assert.Equal(s.Length, t[0].Length);
    }

    [Fact]
    public void Long_bracket_closes_only_with_the_same_level()
    {
        const string s = "x = [==[ a ]=] ]] ]===] ]==] .. y";
        var t = Toks(s);
        Assert.Equal((LuaTokenKind.String, "[==[ a ]=] ]] ]===] ]==]"), t[2]);
        Assert.Equal((LuaTokenKind.Operator, ".."), t[3]);
        Assert.Equal((LuaTokenKind.Identifier, "y"), t[4]);
    }

    [Fact]
    public void Long_comments_and_line_comments()
    {
        const string s = "a --[==[ multi\n ]] still ]==] b -- line [[ not long\nc --[ not long either\nd";
        var t = Toks(s);
        Assert.Equal((LuaTokenKind.Identifier, "a"), t[0]);
        Assert.Equal(LuaTokenKind.Comment, t[1].Kind);
        Assert.Equal("--[==[ multi\n ]] still ]==]", t[1].Text);
        Assert.Equal((LuaTokenKind.Identifier, "b"), t[2]);
        Assert.Equal((LuaTokenKind.Comment, "-- line [[ not long"), t[3]);
        Assert.Equal((LuaTokenKind.Identifier, "c"), t[4]);
        Assert.Equal((LuaTokenKind.Comment, "--[ not long either"), t[5]);
        Assert.Equal((LuaTokenKind.Identifier, "d"), t[6]);
        Assert.Equal(4, LuaLexer.Tokenize(s)[6].Line);
    }

    [Theory]
    [InlineData("\"a\\\"b\"")]
    [InlineData("'it\\'s'")]
    [InlineData("\"back\\\\\"")]
    [InlineData("'x\\n\\t\\065\\x41'")]
    [InlineData("\"a\\\nb\"")]
    [InlineData("\"a\\z   \n   b\"")]
    public void Escapes_keep_the_string_as_one_token(string s)
    {
        var t = LuaLexer.Tokenize(s);
        Assert.Single(t);
        Assert.Equal(LuaTokenKind.String, t[0].Kind);
        Assert.False(t[0].Unterminated);
        Assert.Equal(s.Length, t[0].Length);
    }

    [Fact]
    public void Line_counting_continues_after_an_escaped_newline_inside_a_string()
    {
        var t = LuaLexer.Tokenize("s = \"a\\\nb\" x");
        Assert.Equal(2, t[^1].Line);
    }

    [Theory]
    [InlineData("0x1p4")]
    [InlineData("1e-3")]
    [InlineData("1E+10")]
    [InlineData(".5")]
    [InlineData("3.")]
    [InlineData("0xFF")]
    [InlineData("0x1.8p3")]
    [InlineData("3.14159")]
    [InlineData("0XaBc")]
    public void Numbers_are_single_tokens(string s)
    {
        var t = LuaLexer.Tokenize(s);
        Assert.Single(t);
        Assert.Equal(LuaTokenKind.Number, t[0].Kind);
        Assert.Equal(s.Length, t[0].Length);
    }

    [Fact]
    public void Concat_dot_and_varargs_are_distinguished()
    {
        Assert.Equal(new[] { (LuaTokenKind.Identifier, "a"), (LuaTokenKind.Operator, ".."), (LuaTokenKind.Identifier, "b") }, Toks("a..b"));
        Assert.Equal(new[] { (LuaTokenKind.Identifier, "a"), (LuaTokenKind.Punctuation, "."), (LuaTokenKind.Identifier, "b") }, Toks("a.b"));
        Assert.Equal((LuaTokenKind.Operator, "..."), Toks("f(...)")[2]);
        Assert.Equal(new[] { (LuaTokenKind.Number, "1"), (LuaTokenKind.Operator, ".."), (LuaTokenKind.Number, "2") }, Toks("1 .. 2"));
        Assert.Equal((LuaTokenKind.Operator, ".."), Toks("t[1]..x")[4]);
    }

    [Fact]
    public void Comparison_and_label_punctuation()
    {
        Assert.Equal((LuaTokenKind.Operator, "~="), Toks("a ~= b")[1]);
        Assert.Equal((LuaTokenKind.Operator, "=="), Toks("a == b")[1]);
        Assert.Equal((LuaTokenKind.Operator, "<="), Toks("a <= b")[1]);
        Assert.Equal((LuaTokenKind.Unknown, "~"), Toks("~a")[0]);
        Assert.Equal(new[] { (LuaTokenKind.Punctuation, "::"), (LuaTokenKind.Identifier, "continue"), (LuaTokenKind.Punctuation, "::") }, Toks("::continue::"));
        Assert.Equal((LuaTokenKind.Punctuation, ":"), Toks("map:getId()")[1]);
    }

    [Theory]
    [InlineData("a != b", "!=")]
    [InlineData("a && b", "&&")]
    [InlineData("a || b", "||")]
    [InlineData("a += 1", "+=")]
    [InlineData("a -= 1", "-=")]
    [InlineData("a *= 2", "*=")]
    [InlineData("a /= 2", "/=")]
    [InlineData("a ++", "++")]
    [InlineData("a // b", "//")]
    public void Common_invalid_operators_are_single_operator_tokens(string s, string op)
    {
        Assert.Equal((LuaTokenKind.Operator, op), Toks(s)[1]);
    }

    [Fact]
    public void Minus_minus_is_a_comment_and_minus_space_minus_is_two_operators()
    {
        Assert.Equal(LuaTokenKind.Comment, Toks("x--y")[1].Kind);
        Assert.Equal(new[] { "x", "-", "-", "1" }, Toks("x - -1").Select(t => t.Text));
    }

    [Fact]
    public void Unterminated_short_string_stops_before_the_newline()
    {
        const string s = "local s = 'abc\nx = 1";
        var t = LuaLexer.Tokenize(s);
        var str = t[3];
        Assert.Equal(LuaTokenKind.String, str.Kind);
        Assert.True(str.Unterminated);
        Assert.Equal("'abc", LuaLexer.Slice(s, str));
        Assert.Equal((LuaTokenKind.Identifier, 2), (t[4].Kind, t[4].Line));
    }

    [Theory]
    [InlineData("x = [[abc\n def", LuaTokenKind.String)]
    [InlineData("x = [==[abc ]] ]=]", LuaTokenKind.String)]
    [InlineData("--[[ jamais fermé\nprint(1)", LuaTokenKind.Comment)]
    public void Unterminated_long_tokens_run_to_the_end(string s, LuaTokenKind kind)
    {
        var last = LuaLexer.Tokenize(s)[^1];
        Assert.Equal(kind, last.Kind);
        Assert.True(last.Unterminated);
        Assert.Equal(s.Length, last.End);
    }

    [Fact]
    public void Non_ascii_identifiers_and_bom()
    {
        var t = Toks("\uFEFFlocal quantité = 3");
        Assert.Equal((LuaTokenKind.Keyword, "local"), t[0]);
        Assert.Equal((LuaTokenKind.Identifier, "quantité"), t[1]);
    }

    [Fact]
    public void Unknown_characters()
    {
        Assert.Equal((LuaTokenKind.Unknown, "$"), Toks("a $ b")[1]);
        Assert.Equal((LuaTokenKind.Unknown, "&"), Toks("a & b")[1]);
        Assert.Equal((LuaTokenKind.Unknown, "|"), Toks("|x| x")[0]);
    }

    [Fact]
    public void Tokenize_never_throws_and_covers_every_non_blank_char()
    {
        var rnd = new Random(1234);
        const string alphabet = "abc_XYZ019 \t\r\n\"'\\[]=-.:;,(){}<>~!&|+*/%^#?$@`éà€\u2028";
        for (int round = 0; round < 400; round++)
        {
            var sb = new StringBuilder();
            int len = rnd.Next(0, 120);
            for (int i = 0; i < len; i++) sb.Append(alphabet[rnd.Next(alphabet.Length)]);
            string s = sb.ToString();
            var toks = LuaLexer.Tokenize(s);
            int prevEnd = 0;
            foreach (var t in toks)
            {
                Assert.True(t.Start >= prevEnd && t.Length > 0 && t.End <= s.Length);
                for (int k = prevEnd; k < t.Start; k++) Assert.True(char.IsWhiteSpace(s[k]) || s[k] == '\uFEFF', $"caractère perdu en {k} dans {s}");
                prevEnd = t.End;
            }
            for (int k = prevEnd; k < s.Length; k++) Assert.True(char.IsWhiteSpace(s[k]));
        }
        Assert.Empty(LuaLexer.Tokenize(""));
        Assert.Empty(LuaLexer.Tokenize(null!));
    }

    [Fact]
    public void IsInCodeAt_strings()
    {
        const string s = "x = \"abc\" y";
        Assert.True(LuaLexer.IsInCodeAt(s, 4));    // juste avant le guillemet ouvrant
        Assert.False(LuaLexer.IsInCodeAt(s, 5));   // dans la chaîne
        Assert.False(LuaLexer.IsInCodeAt(s, 8));   // avant le guillemet fermant
        Assert.True(LuaLexer.IsInCodeAt(s, 9));    // après la chaîne
        Assert.True(LuaLexer.IsInCodeAt(s, s.Length));
    }

    [Fact]
    public void IsInCodeAt_comments_and_unterminated()
    {
        const string line = "x = 1 -- note";
        Assert.True(LuaLexer.IsInCodeAt(line, 5));
        Assert.False(LuaLexer.IsInCodeAt(line, 8));
        Assert.False(LuaLexer.IsInCodeAt(line, line.Length));   // fin d'un commentaire de ligne : toujours dedans
        Assert.True(LuaLexer.IsInCodeAt(line + "\ny", line.Length + 1));

        const string lc = "--[[ a ]] b";
        Assert.False(LuaLexer.IsInCodeAt(lc, 5));
        Assert.True(LuaLexer.IsInCodeAt(lc, 9));

        const string open = "print(\"abc";
        Assert.False(LuaLexer.IsInCodeAt(open, open.Length));   // chaîne non terminée : on tape dedans
        Assert.False(LuaLexer.IsInCodeAt("s = [[ abc", 10));
    }

    [Fact]
    public void Slice_returns_the_token_text()
    {
        const string s = "map:getPods()";
        var t = LuaLexer.Tokenize(s);
        Assert.Equal("getPods", LuaLexer.Slice(s, t[2]));
    }
}
