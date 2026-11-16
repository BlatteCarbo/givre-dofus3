using GivreClone.App.Services.Supervisor;
using Xunit;

namespace GivreClone.Tests;

public class ScriptResolverTests
{
    private static readonly (string, string)[] Lib = { ("farm-otomai", "function move() end"), ("Banque.lua", "function move() bank() end"), ("vide", "  ") };
    private static string? Disk(string n) => n switch { "utile.lua" => "x = 1", "farm-otomai.lua" => "DISQUE", _ => null };

    [Theory]
    [InlineData("travelTo(212861955)")]
    [InlineData("x = 1")]
    [InlineData("a\nb")]
    [InlineData("print 'x'\r\n")]
    public void Text_with_a_newline_a_parenthesis_or_an_equals_sign_is_code(string s)
    {
        var (r, why) = ScriptResolver.Resolve(s, Lib, Disk);
        Assert.True(r!.FromCode);
        Assert.Equal(s, r.Code);
        Assert.Equal("", why);
    }

    [Fact]
    public void A_name_is_looked_up_in_the_planning_library_first_ignoring_case_and_the_lua_suffix()
    {
        Assert.Equal("function move() end", ScriptResolver.Resolve("FARM-OTOMAI", Lib, Disk).Script!.Code);   // la bibliothèque prime sur le disque
        Assert.Equal("function move() bank() end", ScriptResolver.Resolve("banque", Lib, Disk).Script!.Code);
        Assert.Equal("farm-otomai", ScriptResolver.Resolve("farm-otomai.lua", Lib, Disk).Script!.Name);
    }

    [Fact]
    public void Then_the_scripts_directory_with_or_without_the_extension()
    {
        Assert.Equal("x = 1", ScriptResolver.Resolve("utile", Lib, Disk).Script!.Code);
        Assert.Equal("utile.lua", ScriptResolver.Resolve("utile.lua", Lib, Disk).Script!.Name);
    }

    [Fact]
    public void An_empty_library_entry_falls_through_to_the_disk_and_unknown_names_are_reported()
    {
        Assert.Equal("script introuvable : vide", ScriptResolver.Resolve("vide", Lib, Disk).Reason);
        var (r, why) = ScriptResolver.Resolve("inconnu", Lib, Disk);
        Assert.Null(r);
        Assert.Equal("script introuvable : inconnu", why);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../secret")]
    [InlineData("%USERPROFILE%\\x.lua")]
    [InlineData("sous/dossier")]
    [InlineData("a:b")]
    public void A_name_is_never_a_path(string s)
    {
        var read = new List<string>();
        var (r, why) = ScriptResolver.Resolve(s, Lib, n => { read.Add(n); return "X"; });
        Assert.Null(r);
        Assert.Equal("un script se désigne par son nom, jamais par un chemin", why);
        Assert.Empty(read);   // le disque n'est même pas consulté
    }

    [Fact]
    public void Blank_input_is_refused()
    {
        Assert.Equal("script manquant", ScriptResolver.Resolve("  ", Lib, Disk).Reason);
        Assert.Equal("script manquant", ScriptResolver.Resolve("", Lib, Disk).Reason);
    }
}
