extern alias F06App;

using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using GivreClone.App.Services.Resources;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using Xunit;

namespace GivreClone.Tests;

public class ResourceOriginTests
{
    // Banque et extérieur du trajet serveur documenté dans REPRISE_TOUS_LOTS_20261025.
    private const long Bank = 192415750, Outside = 191104002;

    private static LaunchService GraphService(string transitions, int outsideWorld = 1)
    {
        // Aucun constructeur, compte, transport ni chargement de données utilisateur.
        var service = (LaunchService)RuntimeHelpers.GetUninitializedObject(typeof(LaunchService));
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var graph = new WorldGraph(WorldGraphCache.Parse(transitions));
        typeof(LaunchService).GetField("_worldGraph", fields)!.SetValue(service, graph);
        typeof(LaunchService).GetField("_mapPositions", fields)!.SetValue(service,
            MapPositions.FromDictionary(new Dictionary<long, (int, int, int)>
            {
                [Bank] = (4, -18, -1), [Outside] = (4, -18, outsideWorld),
                [123] = (4, -18, 2), [456] = (0, 0, -1)
            }));
        return service;
    }

    private static (int X, int Y, int? World) GraphOrigin(LaunchService service)
        => ((int X, int Y, int? World))typeof(LaunchService)
            .GetMethod("ResourceOriginOf", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, new object[] { Bank })!;

    // Arête exacte signalée par la revue du cache client worldgraph5.json.
    // Le test traverse Parse -> WorldGraph -> helper LaunchService -> ResourceMap.
    private const string BankGraph = """
        {"_v":5,"192415750":[[409,191104002,255,8]],"191104002":[[288,192415750,255,32]]}
        """;

    [Fact]
    public void Production_graph_source_resolves_the_bank_type8_exit()
    {
        var service = GraphService(BankGraph);
        Assert.Equal((4, -18, (int?)1), GraphOrigin(service));
        var uiDoors = typeof(LaunchService).GetMethod("WorldPlaceDoors", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, new object[] { Bank });
        Assert.Empty((System.Collections.IEnumerable)uiDoors!);
    }

    [Fact]
    public void Production_graph_source_keeps_ambiguous_worlds_unresolved()
        => Assert.Equal(-1, GraphOrigin(GraphService("""
            {"192415750":[[409,191104002,255,8],[410,123,255,32]]}
            """)).World);

    [Fact]
    public void Production_graph_source_uses_actual_destination_world()
        => Assert.Equal((4, -18, (int?)2), GraphOrigin(GraphService(BankGraph, outsideWorld: 2)));

    [Fact]
    public void Production_graph_source_without_exits_preserves_indoor_world()
        => Assert.Equal(-1, GraphOrigin(GraphService("{}" )).World);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(32)]
    public void Production_graph_source_uses_arrival_and_excludes_conditional_or_unknown_types(int type)
    {
        string graph = "{\"192415750\":[[409,456,255," + type +
            ",null,191104002],[410,123,255,32,\"Qf=1\"],[411,123,255,64]]}";
        Assert.Equal((4, -18, (int?)1), GraphOrigin(GraphService(graph)));
    }

    private static (int X, int Y, int? World)? Origin(int? layer = -1, long[]? exits = null)
        => ResourceMap.Origin(Bank,
            id => id is Bank or Outside ? (4, -18) : (1, 1),
            id => id == Bank ? layer : id == Outside ? 1 : 2,
            _ => exits ?? new[] { Outside });

    [Fact]
    public void Bank_exit_restores_main_world_wheat_without_including_other_worlds()
    {
        var map = ResourceMap.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "resource_map.json")));
        int wheat = map.Resolve("Blé")!.ItemId;
        Assert.Empty(map.Best(wheat, 4, -18, -1, includeIndoor: false));
        var origin = Origin()!.Value;
        Assert.Equal((4, -18, (int?)1), origin);
        var hits = map.Best(wheat, origin.X, origin.Y, origin.World, includeIndoor: false);
        Assert.NotEmpty(hits);
        Assert.All(hits, h => { Assert.Equal(1, h.Node.World); Assert.False(h.Node.Indoor); });
        // Les données de l'autre monde restent disponibles, avec leur propre filtre.
        Assert.NotEmpty(map.Best(wheat, world: 2, includeIndoor: false));
    }

    [Fact]
    public void Known_world_is_preserved_even_with_a_door_to_another_world()
        => Assert.Equal((4, -18, (int?)2), Origin(2)!.Value);

    [Fact]
    public void Unknown_world_is_not_guessed_from_coordinates_or_doors()
        => Assert.Equal((4, -18, (int?)null), Origin(null)!.Value);

    [Fact]
    public void Missing_or_ambiguous_exits_preserve_the_indoor_layer()
    {
        Assert.Equal(-1, Origin(exits: Array.Empty<long>())!.Value.World);
        Assert.Equal(-1, Origin(exits: new[] { Outside, 123L })!.Value.World);
        Assert.Equal(1, Origin(exits: new[] { Outside, Outside })!.Value.World);
    }

    [Fact]
    public void Unknown_map_has_no_origin()
    {
        Assert.Null(ResourceMap.Origin(0, _ => (4, -18), _ => 1, _ => Array.Empty<long>()));
        Assert.Null(ResourceMap.Origin(Bank, _ => null, _ => -1, _ => new[] { Outside }));
    }
}
