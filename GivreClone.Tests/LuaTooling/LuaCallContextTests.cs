using System.Linq;
using GivreClone.Engine.LuaTooling;
using Xunit;

namespace GivreClone.Tests.LuaTooling;

public class LuaCallContextTests
{
    // « ‸ » marque le curseur dans le texte de test.
    private static (string Text, int Caret) C(string s)
    {
        int i = s.IndexOf('‸');
        Assert.True(i >= 0);
        return (s.Remove(i, 1), i);
    }

    private static (string? Ns, string Name, int Arg, int Open)? At(string s)
    {
        var (t, c) = C(s);
        return LuaCallContext.At(t, c);
    }

    [Fact]
    public void At_module_call_and_argument_index()
    {
        var (t, c) = C("x = map:moveToward(12, ‸");
        var r = LuaCallContext.At(t, c);
        Assert.Equal(("map", "moveToward", 1, t.IndexOf('(')), r);
    }

    [Theory]
    [InlineData("getPods(‸", null, "getPods", 0)]
    [InlineData("print(string.format(\"%d, %s\", ‸", "string", "format", 1)]
    [InlineData("print(string.format(\"%d\", 1), ‸", null, "print", 1)]
    [InlineData("f(a, {1, 2, ‸", null, "f", 1)]
    [InlineData("f(a, (b + ‸", null, "f", 1)]
    [InlineData("print(\"a, b‸", null, "print", 0)]          // dans une chaîne non terminée : argument en cours
    [InlineData("a.b.c(1, 2, ‸)", "a.b", "c", 2)]
    [InlineData("obj:method(x, g(y), ‸", "obj", "method", 2)]
    [InlineData("sale:SellItem(1, --[[ , ]] 2, ‸", "sale", "SellItem", 2)]
    [InlineData("foo(\n  1,\n  ‸", null, "foo", 1)]
    public void At_cases(string s, string? ns, string name, int arg)
    {
        var r = At(s);
        Assert.NotNull(r);
        Assert.Equal((ns, name, arg), (r!.Value.Ns, r.Value.Name, r.Value.Arg));
    }

    [Theory]
    [InlineData("foo(1)‸")]
    [InlineData("x = 1‸")]
    [InlineData("function foo(a, ‸")]
    [InlineData("local function foo(‸")]
    [InlineData("local f = function(a‸")]
    [InlineData("-- print(‸")]
    [InlineData("x = (1 + ‸")]
    [InlineData("f()(‸")]
    [InlineData("‸")]
    public void At_outside_a_call_is_null(string s)
    {
        Assert.Null(At(s));
    }

    [Theory]
    [InlineData("map:getP‸", "map", ':', "getP")]
    [InlineData("x = string.fo‸", "string", '.', "fo")]
    [InlineData("map:‸", "map", ':', "")]
    [InlineData("  self:‸", "self", ':', "")]
    [InlineData("a.b.c‸", "a.b", '.', "c")]
    [InlineData("getPo‸", null, ' ', "getPo")]
    [InlineData("x = y ..get‸", null, ' ', "get")]
    [InlineData("if ‸", null, ' ', "")]
    [InlineData("function mo‸", null, ' ', "mo")]
    public void CompletionAt_cases(string s, string? qual, char sep, string prefix)
    {
        var (t, c) = C(s);
        var r = LuaCallContext.CompletionAt(t, c);
        Assert.NotNull(r);
        Assert.Equal((qual, sep, prefix, c - prefix.Length), r!.Value);
    }

    [Theory]
    [InlineData("print(\"map:ge‸")]
    [InlineData("-- map:ge‸")]
    [InlineData("--[[ map:ge‸ ]]")]
    [InlineData("local na‸")]
    [InlineData("local function he‸")]
    [InlineData("for k‸")]
    [InlineData("goto con‸")]
    [InlineData("x = 12‸")]
    [InlineData("x = 1.‸")]
    [InlineData("f():ge‸")]
    [InlineData("t[1].x‸")]
    [InlineData("::lab‸")]
    public void CompletionAt_null_cases(string s)
    {
        var (t, c) = C(s);
        Assert.Null(LuaCallContext.CompletionAt(t, c));
    }

    [Fact]
    public void SymbolAt_chains()
    {
        const string s = "x = map:getPods() + string.format('%d', 1)";
        int onGet = s.IndexOf("getPods") + 2;
        Assert.Equal(("map", "getPods", s.IndexOf("map"), s.IndexOf("getPods") + 7), LuaCallContext.SymbolAt(s, onGet));
        int onMap = s.IndexOf("map") + 1;
        Assert.Equal(((string?)null, "map", s.IndexOf("map"), s.IndexOf("map") + 3), LuaCallContext.SymbolAt(s, onMap));
        Assert.Equal(("string", "format"), (LuaCallContext.SymbolAt(s, s.IndexOf("format"))!.Value.Namespace, LuaCallContext.SymbolAt(s, s.IndexOf("format"))!.Value.Name));
        Assert.Equal((null, "x"), (LuaCallContext.SymbolAt(s, 0)!.Value.Namespace, LuaCallContext.SymbolAt(s, 0)!.Value.Name));
        Assert.Null(LuaCallContext.SymbolAt(s, s.IndexOf("'%d'") + 1));   // dans une chaîne
        Assert.Null(LuaCallContext.SymbolAt(s, s.IndexOf("+")));
        Assert.Null(LuaCallContext.SymbolAt("", 0));
        Assert.Null(LuaCallContext.SymbolAt("if x then", 1));             // mot-clé
    }

    [Fact]
    public void Declarations_of_a_script()
    {
        const string s = "MAX_PODS = 90\n" +
                         "local helper = function(a, b) return a end\n" +
                         "local count, total = 0, 0\n" +
                         "function move()\n" +
                         "  for i, v in ipairs({}) do print(v) end\n" +
                         "  inner = 1\n" +
                         "end\n" +
                         "local function util(x, ...) end\n" +
                         "function M.helper2() end\n";
        var d = LuaCallContext.Declarations(s);
        var byName = d.ToDictionary(x => x.Name);
        Assert.Equal((false, 1, (string?)null), (byName["MAX_PODS"].IsFunction, byName["MAX_PODS"].Line, byName["MAX_PODS"].Args));
        Assert.Equal((true, 2, "a, b"), (byName["helper"].IsFunction, byName["helper"].Line, byName["helper"].Args));
        Assert.False(byName["count"].IsFunction);
        Assert.Equal(3, byName["total"].Line);
        Assert.Equal((true, 4, ""), (byName["move"].IsFunction, byName["move"].Line, byName["move"].Args));
        Assert.Equal(5, byName["i"].Line);
        Assert.True(byName.ContainsKey("v"));
        Assert.Equal((true, "x, ..."), (byName["util"].IsFunction, byName["util"].Args));
        Assert.True(byName.ContainsKey("a") && byName.ContainsKey("x"));      // paramètres (portée fichier)
        Assert.False(byName.ContainsKey("inner"));                            // global assigné hors racine
        Assert.DoesNotContain(d, x => x.Name.Contains('.') || x.Name.Contains(':'));
        Assert.Equal(d.Count, d.Select(x => x.Name).Distinct().Count());       // un nom = une entrée
        Assert.Empty(LuaCallContext.Declarations(""));
    }
}
