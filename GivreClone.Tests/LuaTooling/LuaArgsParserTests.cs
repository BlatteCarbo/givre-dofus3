using System.Linq;
using GivreClone.Engine.LuaTooling;
using Xunit;

namespace GivreClone.Tests.LuaTooling;

public class LuaArgsParserTests
{
    private static string Show(string? args) =>
        string.Join(" ; ", LuaArgsParser.Parse(args).Select(p => (p.Optional ? "?" : "") + p.Name + (p.Default is { } d ? "=" + d : "")));

    [Theory]
    [InlineData("GID, quantité, price", "GID ; quantité ; price")]
    [InlineData("gid [, n]", "gid ; ?n")]
    [InlineData("gid [, n=1]", "gid ; ?n=1")]
    [InlineData("[bool=true]", "?bool=true")]
    [InlineData("[bool=true [, timeoutSec=60]]", "?bool=true ; ?timeoutSec=60")]
    [InlineData("mapId | x, y", "mapId | x ; y")]
    [InlineData("[from,] to", "?from ; to")]
    [InlineData("nom [, nomEchec] [, timeoutMs=10000] [, action]", "nom ; ?nomEchec ; ?timeoutMs=10000 ; ?action")]
    [InlineData("{noms} [, timeoutMs=10000] [, action]", "{noms} ; ?timeoutMs=10000 ; ?action")]
    [InlineData("{{gid, min, max}, …}", "{{gid, min, max}, …}")]
    [InlineData("{c…} | c1, c2, …", "{c…} | c1 ; c2 ; ?…")]
    [InlineData("message, \"#RRGGBB\"", "message ; \"#RRGGBB\"")]
    [InlineData("cell | \"c1|c2\"", "cell | \"c1|c2\"")]
    [InlineData("…", "?…")]
    [InlineData("gids…", "gids…")]
    [InlineData("[nom] [, timeoutSec=10]", "?nom ; ?timeoutSec=10")]
    public void Parses_doc_argument_notations(string args, string expected)
    {
        Assert.Equal(expected, Show(args));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_args(string? args)
    {
        Assert.Empty(LuaArgsParser.Parse(args));
    }

    [Theory]
    [InlineData("[[[")]
    [InlineData("]]], ,, =")]
    [InlineData("\"unterminated, x")]
    [InlineData("{{{(")]
    public void Never_throws_on_malformed_text(string args)
    {
        var r = LuaArgsParser.Parse(args);
        Assert.NotNull(r);
        Assert.All(r, p => Assert.False(string.IsNullOrWhiteSpace(p.Name)));
    }

    [Fact]
    public void Every_doc_signature_parses()
    {
        // Args "" = fonction sans argument (os.clock()) : rien à analyser.
        foreach (var e in LuaToolingTestData.Catalog.Entries.Where(e => !string.IsNullOrWhiteSpace(e.Args)))
        {
            Assert.NotEmpty(e.Params);
            Assert.All(e.Params, p => Assert.False(string.IsNullOrWhiteSpace(p.Name)));
        }
    }
}
