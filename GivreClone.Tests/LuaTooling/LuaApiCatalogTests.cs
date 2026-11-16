using System.IO;
using System.Linq;
using System.Text;
using GivreClone.Engine.LuaTooling;
using Xunit;

namespace GivreClone.Tests.LuaTooling;

public class LuaApiCatalogTests
{
    private static LuaApiCatalog Cat => LuaToolingTestData.Catalog;

    [Fact]
    public void Loads_the_real_doc_and_flattens_every_signature()
    {
        Assert.True(Cat.Entries.Count > 750, $"{Cat.Entries.Count} entrées");
        // toutes les signatures de la doc (sauf mots-clés) + move/bank + constantes + modules + stdlib
        Assert.True(Cat.Entries.Count(e => e.Kind is LuaApiKind.Function or LuaApiKind.Callback) >= 760);
    }

    [Theory]
    [InlineData("getPods")]
    [InlineData("getPodsMax")]
    [InlineData("getPodsPercent")]
    public void Pods_getters_are_plain_global_functions(string name)
    {
        var e = Cat.Find(null, name);
        Assert.NotNull(e);
        Assert.Equal(LuaApiKind.Function, e!.Kind);
        Assert.Null(e.Namespace);
        Assert.Equal("number", e.Returns);
        Assert.Equal("state", e.CategoryId);
        Assert.Contains(Cat.Globals, g => g.Name == name);
    }

    [Fact]
    public void Namespaces_are_the_36_modules_without_colon()
    {
        Assert.Equal(36, Cat.Namespaces.Count);   // 36e : fm: (forgemagie)
        Assert.Contains("fm", Cat.Namespaces);
        Assert.Contains("map", Cat.Namespaces);
        Assert.Contains("inventory", Cat.Namespaces);
        Assert.Contains("d2data", Cat.Namespaces);
        Assert.All(Cat.Namespaces, n => Assert.DoesNotContain(":", n));
        Assert.All(Cat.Namespaces, n => Assert.Contains(Cat.Globals, g => g.Name == n && g.Kind == LuaApiKind.Namespace));
        Assert.Equal("map (module)", Cat.Find(null, "map")!.Display);
    }

    [Fact]
    public void Map_members()
    {
        var m = Cat.MembersOfNamespace("map");
        Assert.True(m.Count > 50);
        Assert.All(m, e => Assert.Equal("map", e.Namespace));
        Assert.Contains(m, e => e.Name == "getId");
        Assert.Equal(m.Count, Cat.MembersOfNamespace("MAP").Count);    // nom de module insensible à la casse
        Assert.Equal(m.Count, Cat.MembersOfNamespace("map:").Count);
        Assert.Empty(Cat.MembersOfNamespace("nope"));
        Assert.Equal("map:moveToward(mapId | x, y) → bool", Cat.Find("map", "moveToward")!.Display);
    }

    [Fact]
    public void Stdlib_tables_are_members_and_globals()
    {
        Assert.Contains(Cat.MembersOfNamespace("string"), e => e.Name == "format" && e.Kind == LuaApiKind.StdLib);
        Assert.Contains(Cat.Globals, g => g.Name == "string" && g.Kind == LuaApiKind.StdLib);
        Assert.StartsWith("string.format(", Cat.Find("string", "format")!.Display);
        Assert.EndsWith("→ string", Cat.Find("string", "format")!.Display);
        Assert.Equal("string (bibliothèque Lua)", Cat.Globals.First(g => g.Name == "string").Display);
        Assert.False(Cat.IsNamespace("string"));
    }

    [Fact]
    public void Move_and_bank_callbacks_are_added_by_hand()
    {
        foreach (var n in new[] { "move", "bank" })
        {
            var e = Cat.Find(null, n);
            Assert.NotNull(e);
            Assert.Equal(LuaApiKind.Callback, e!.Kind);
            Assert.Equal("callbacks", e.CategoryId);
            Assert.False(string.IsNullOrWhiteSpace(e.Description));
            Assert.True(Cat.IsCallback(n));
        }
        Assert.Equal("move", Cat.InCategory("callbacks")[0].Name);
        foreach (var n in new[] { "onTurn", "fightManagement", "onPlacement", "onFightEnd", "stopped", "onPrivateMessage", "messagesRegistering", "phenix", "prefightManagement", "challengeManagement" })
        {
            Assert.True(Cat.IsCallback(n), n);
            Assert.Equal(LuaApiKind.Callback, Cat.Find(null, n)!.Kind);
        }
        Assert.False(Cat.IsCallback("getPods"));
        Assert.False(Cat.IsCallback("onturn"));
    }

    [Fact]
    public void Goto_is_never_offered()
    {
        Assert.DoesNotContain(Cat.Entries, e => e.Name == "goto");
        Assert.DoesNotContain(Cat.MembersOfNamespace("zaap"), e => e.Name == "goto");
        Assert.DoesNotContain(Cat.MembersOfNamespace("zaapi"), e => e.Name == "goto");
        Assert.Contains(Cat.MembersOfNamespace("zaap"), e => e.Name == "go");
        Assert.DoesNotContain(Cat.Entries, e => LuaLexer.IsKeyword(e.Name));
    }

    [Fact]
    public void Search_ranks_getPods_first()
    {
        var r = Cat.Search("pods");
        Assert.Equal("getPods", r[0].Name);
        Assert.Null(r[0].Namespace);
        Assert.Contains(r, e => e.Namespace == "inventory" && e.Name == "pods");
        Assert.Equal("getPods", Cat.Search("  PODS ")[0].Name);
    }

    [Fact]
    public void Search_by_qualified_name_description_and_diacritics()
    {
        Assert.Equal(("map", "getId"), (Cat.Search("map:getId")[0].Namespace, Cat.Search("map:getId")[0].Name));
        Assert.Contains(Cat.Search("map.getid"), e => e.Namespace == "map" && e.Name == "getId");
        Assert.NotEmpty(Cat.Search("récolte"));
        Assert.Equal(Cat.Search("récolte").Count, Cat.Search("RECOLTE").Count);
        Assert.NotEmpty(Cat.Search("carte courante"));
        Assert.Empty(Cat.Search("zzzqqqxxx"));
        Assert.Empty(Cat.Search(""));
        Assert.Equal(3, Cat.Search("get", 3).Count);
    }

    [Fact]
    public void Overloads_exact_then_tolerant_then_alias()
    {
        var travel = Cat.Overloads(null, "travelTo");
        Assert.Equal(2, travel.Count);
        Assert.Contains(travel, e => e.Args == "x, y");
        Assert.Equal("getId", Cat.Overloads("map", "getid").Single().Name);
        Assert.Equal("getId", Cat.Overloads("map", "get_id").Single().Name);
        Assert.Equal("getId", Cat.Overloads("MAP", "GetId").Single().Name);
        Assert.Equal("getId", Cat.Overloads("map:", "getId").Single().Name);
        Assert.Equal("SellItem", Cat.Find("sale", "sellItem")!.Name);
        Assert.Equal("data", Cat.Find("d2data", "monsterName")!.Namespace);   // alias d2data -> data
        Assert.Empty(Cat.Overloads(null, "getpods"));                          // globals : casse exacte
        Assert.Empty(Cat.Overloads("nope", "getId"));
        Assert.Null(Cat.Find(null, "nope"));
    }

    [Fact]
    public void Constants_from_the_config_category()
    {
        var e = Cat.Find(null, "MAX_PODS")!;
        Assert.Equal(LuaApiKind.Constant, e.Kind);
        Assert.Equal("number", e.ConstType);
        Assert.Equal("95", e.ConstDefault);
        Assert.Equal("MAX_PODS : number = 95", e.Display);
        Assert.Equal("MAX_PODS", e.InsertText);
        Assert.True(Cat.IsKnownGlobal("ELEMENTS_TO_GATHER"));
    }

    [Fact]
    public void Display_insert_text_params_and_shared_metadata()
    {
        var e = Cat.Find(null, "getPods")!;
        Assert.Equal("getPods() → number", e.Display);
        Assert.Equal("getPods", e.InsertText);
        var sell = Cat.Find("sale", "SellItem")!;
        Assert.Equal(new[] { "GID", "quantité", "price" }, sell.Params.Select(p => p.Name));
        Assert.Contains("blocking", sell.Badges);
        Assert.Equal("SellItem", sell.InsertText);
        // Les signatures d'une même méthode partagent id / description.
        var a = Cat.Find(null, "getPodsMax")!;
        Assert.Equal(e.Id, a.Id);
        Assert.Equal(e.Description, a.Description);
    }

    [Fact]
    public void Stub_verbs_are_flagged()
    {
        Assert.True(Cat.Find("global", "password")!.IsStub);
        Assert.True(Cat.Overloads("mount", Cat.MembersOfNamespace("mount")[0].Name)[0].IsStub);   // mount:*
        Assert.False(Cat.Find(null, "getPods")!.IsStub);
    }

    [Fact]
    public void Categories_and_in_category()
    {
        Assert.Contains(Cat.Categories, c => c.Id == "config");
        Assert.Contains(Cat.Categories, c => c.Id == "mod-map" && c.Blurb is not null);
        Assert.DoesNotContain(Cat.Categories, c => c.Id == "stdlib");   // la ligne « Lua standard » est ajoutée par l'UI
        Assert.All(Cat.Categories, c => Assert.NotEmpty(Cat.InCategory(c.Id)));
        Assert.All(Cat.InCategory("config"), e => Assert.Equal(LuaApiKind.Constant, e.Kind));
        Assert.Empty(Cat.InCategory("nope"));
        Assert.Contains(Cat.StepFields, f => f.Name == "map");
    }

    [Fact]
    public void Name_set_for_the_linter()
    {
        Assert.True(Cat.IsKnownGlobal("print"));
        Assert.True(Cat.IsKnownGlobal("tostring"));      // filet Lua de base, même sans entrée stdlib
        Assert.True(Cat.IsKnownGlobal("MAX_PODS"));
        Assert.True(Cat.IsKnownGlobal("map"));
        Assert.True(Cat.IsKnownGlobal("move"));
        Assert.False(Cat.IsKnownGlobal("getJobLevell"));
        Assert.False(Cat.IsKnownGlobal("GetPods"));
        Assert.Contains("getJobLevel", Cat.AllGlobalNames);
        Assert.True(Cat.IsNamespace("map"));
        Assert.False(Cat.IsNamespace("Map"));
        Assert.True(Cat.IsKnownMember("map", "getId"));
        Assert.True(Cat.IsKnownMember("map", "GetId"));
        Assert.True(Cat.IsKnownMember("map", "get_id"));
        Assert.True(Cat.IsKnownMember("character", "askShieldCode"));   // stub hors méthodes documentées
        Assert.True(Cat.IsKnownMember("character", "getCostVitality")); // API indisponible, nom explicite
        Assert.False(Cat.IsKnownMember("character", "getCostAnything")); // F07 : pas de méthode inventée par wildcard
        Assert.True(Cat.IsKnownMember("string", "upper"));
        Assert.False(Cat.IsKnownMember("map", "xyzzy"));
        Assert.Contains("getId", Cat.MembersOf("map"));
        Assert.Contains("monsterName", Cat.MembersOf("d2data"));
    }

    [Fact]
    public void Empty_catalog_is_permissive()
    {
        var e = LuaApiCatalog.Empty;
        Assert.Empty(e.Entries);
        Assert.Empty(e.Namespaces);
        Assert.True(e.IsKnownGlobal("anything"));
        Assert.True(e.IsKnownMember("x", "y"));
        Assert.True(e.IsCallback("move"));
        Assert.Empty(e.Search("pods"));
    }

    [Fact]
    public void TryLoad_rejects_garbage()
    {
        Assert.False(LuaApiCatalog.TryLoad(new MemoryStream(Encoding.UTF8.GetBytes("{ not json")), out var c));
        Assert.Same(LuaApiCatalog.Empty, c);
        Assert.False(LuaApiCatalog.TryLoad(null, out _));
        Assert.True(LuaApiCatalog.TryLoad(File.OpenRead(LuaToolingTestData.DocPath), out var ok));
        Assert.NotEmpty(ok.Entries);
        var tiny = LuaApiCatalog.Load(new MemoryStream(Encoding.UTF8.GetBytes(
            "{\"categories\":[{\"id\":\"mod-x\",\"title\":\"X\",\"methods\":[{\"id\":\"x.a\",\"sig\":[{\"ns\":\"x:\",\"name\":\"a\",\"args\":\"n [, m=2]\",\"returns\":\"bool\"},{\"ns\":\"x:\",\"name\":\"goto\"}],\"desc\":\"d\"}]}]}")));
        Assert.Equal(new[] { "x" }, tiny.Namespaces);
        Assert.Equal("x:a(n [, m=2]) → bool", tiny.Find("x", "a")!.Display);
        Assert.Null(tiny.Find("x", "goto"));
    }
}
