using System;
using System.IO;
using System.Linq;
using GivreClone.App.Services.Resources;
using Xunit;

namespace GivreClone.Tests;

public class ResourceMapTests
{
    private const string Mini = """
        {"resources":[[303,2,1,38017,"Frêne"],[312,24,20,38020,"Fer"],[311,1,1,38011,"Eau"]],
         "nodes":[[100,1,0,0,303,5,0],[101,1,3,0,303,9,0],[102,1,10,10,303,20,0],[103,1,1,1,303,7,1],[200,1,2,2,312,4,0]]}
        """;
    private static ResourceMap Map => ResourceMap.Parse(Mini);

    [Fact]
    public void Find_ignores_case_accents_and_filters_by_job()
    {
        Assert.Equal(new[] { 303 }, Map.Find("FRENE").Select(r => r.ItemId).ToArray());
        Assert.Equal(new[] { 311, 303, 312 }, Map.Find("").Select(r => r.ItemId).ToArray());   // métier (1 < 2 < 24) puis niveau
        Assert.Equal(new[] { 312 }, Map.Find("", job: 24).Select(r => r.ItemId).ToArray());
        Assert.Empty(Map.Find("introuvable"));
    }

    [Fact]
    public void Resolve_accepts_an_id_or_a_name()
    {
        Assert.Equal(312, Map.Resolve("312")!.ItemId);
        Assert.Equal(303, Map.Resolve("frêne")!.ItemId);
        Assert.Equal(312, Map.Resolve("fe")!.ItemId);          // « Fer » contient « fe »
        Assert.Null(Map.Resolve(""));
        Assert.Null(Map.Resolve("zzz"));
    }

    [Fact]
    public void Best_without_origin_returns_the_richest_maps_first()
        => Assert.Equal(new long[] { 102, 101, 103, 100 }, Map.Best(303).Select(h => h.Node.MapId).ToArray());

    [Fact]
    public void Best_with_origin_returns_the_nearest_first_and_can_skip_indoor_maps()
    {
        var near = Map.Best(303, 0, 0);
        Assert.Equal(new long[] { 100, 103, 101, 102 }, near.Select(h => h.Node.MapId).ToArray());
        Assert.Equal(new[] { 0, 2, 3, 20 }, near.Select(h => h.Distance).ToArray());
        Assert.DoesNotContain(Map.Best(303, 0, 0, includeIndoor: false), h => h.Node.Indoor);
        Assert.Single(Map.Best(303, 0, 0, limit: 1));
        Assert.Empty(Map.Best(303, 0, 0, world: 7));              // aucun point de ce monde
    }

    [Fact]
    public void Shipped_dataset_has_the_expected_shape()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Data", "resource_map.json");
        Assert.True(File.Exists(path), "jeu de données non copié dans le dossier de test");
        var map = ResourceMap.Parse(File.ReadAllText(path));
        Assert.Equal(86, map.Resources.Count);
        Assert.Equal(7773, map.NodeCount);
        Assert.Equal(16, map.Resources.Count(r => r.Job == 24));                // les 16 minerais
        Assert.All(map.Resources, r => Assert.False(string.IsNullOrWhiteSpace(r.Name)));
        var frene = map.Resolve("Frêne")!;
        Assert.Equal(2, frene.Job);
        Assert.NotEmpty(map.Best(frene.ItemId));
    }
}
