using System.Linq;
using GivreClone.App.Services.Mcp;
using GivreClone.App.Services.Resources;
using Xunit;

namespace GivreClone.Tests;

public class GatherScriptTests
{
    private const string Mini = """
        {"resources":[[303,2,1,38017,"Frêne"]],
         "nodes":[[1,1,0,0,303,5,0],[2,1,1,0,303,9,0],[3,1,2,0,303,3,0],[4,1,30,30,303,50,0],[5,1,1,1,303,40,1]]}
        """;
    private static ResourceMap Map => ResourceMap.Parse(Mini);

    [Fact]
    public void Route_takes_the_richest_of_the_nearest_and_orders_them_as_a_tour()
    {
        var route = GatherScript.Route(Map, 303, 0, 0, 1, count: 3);
        Assert.Equal(3, route.Count);
        Assert.DoesNotContain(route, n => n.Indoor);                 // intérieurs exclus par défaut
        Assert.Equal(1, route[0].MapId);                              // part de l'origine : la plus proche d'abord
    }

    [Fact]
    public void Route_can_include_indoor_maps_and_never_exceeds_what_exists()
    {
        Assert.Contains(GatherScript.Route(Map, 303, 0, 0, 1, 30, includeIndoor: true), n => n.Indoor);
        Assert.Equal(4, GatherScript.Route(Map, 303, 0, 0, 1, 30).Count);   // 4 cartes extérieures seulement
        Assert.Empty(GatherScript.Route(Map, 999, 0, 0, 1, 5));
    }

    [Fact]
    public void Build_produces_valid_lua_with_the_gather_filter_and_every_map()
    {
        var res = Map.Resolve("frene")!;
        var route = GatherScript.Route(Map, 303, 0, 0, 1, 3);
        string code = GatherScript.Build(res, route, 85);
        Assert.Null(SavedLuaScripts.SyntaxError(code));
        Assert.Contains("ITEMS_TO_GATHER = { 303 }", code);
        Assert.Contains("MAX_PODS = 85", code);
        foreach (var n in route) Assert.Contains($"{{ {n.MapId}, \"{n.X},{n.Y}\", {n.Count} }}", code);
        Assert.Contains("travelTo(", code);
        Assert.Contains("gatherAll()", code);
    }

    [Fact]
    public void Build_refuses_an_empty_route_and_clamps_pods()
    {
        Assert.Throws<System.ArgumentException>(() => GatherScript.Build(Map.Resolve("frene")!, System.Array.Empty<ResourceNode>()));
        Assert.Contains("MAX_PODS = 100", GatherScript.Build(Map.Resolve("frene")!, GatherScript.Route(Map, 303, 0, 0, 1, 1), 500));
    }
}
