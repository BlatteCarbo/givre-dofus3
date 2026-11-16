using System.Text;
using System.Text.Json;
using GivreClone.App.Services.WorldAtlasMcp;
using Xunit;

namespace GivreClone.Tests;

public sealed class WorldAtlasMcpFacadeTests
{
    private static readonly string Digest = new('A', 64);

    private static AtlasProvenance Source(bool permit = true, string kind = "unity") =>
        new(kind, "atlas-fixture", "3.7.11.11", Digest, permit,
            permit ? "review-approved-fixture" : null);

    private static AtlasMap Map(long id, AtlasEvidenceStatus status = AtlasEvidenceStatus.VERIFIED,
        bool permit = true, params string[] features) =>
        new(id, (int)id, -3, 1, 479, features, status, Source(permit)) { WorldId = 1 };

    private static AtlasTransition Edge(string id, long from, long? to,
        AtlasEvidenceStatus status = AtlasEvidenceStatus.VERIFIED,
        AtlasTransitionKind kind = AtlasTransitionKind.CAVE,
        AtlasProvenance? source = null) =>
        new(id, from, to, kind, "inside", status, source ?? Source()) { CellId = 210, FromZone = 1, ToZone = 2 };

    private static WorldAtlasMcpSnapshot Sample() => new("3.7.11.11",
        new[] { Map(1, features: ["cave_entrance", "mine"]), Map(2, features: ["cave"]),
            Map(3, features: ["cave"]), Map(4, features: ["zaap"]) },
        [Edge("cave-1", 1, 2), Edge("cave-2", 2, 3) with { FromZone = 2, ToZone = 3 },
            Edge("zaap-candidate", 1, 4, AtlasEvidenceStatus.CANDIDATE, AtlasTransitionKind.ZAAP)]);

    private static JsonDocument Query(string json, WorldAtlasMcpSnapshot? atlas = null) =>
        JsonDocument.Parse(WorldAtlasMcpFacade.QueryJson(json, atlas ?? Sample()));

    private static string Status(JsonDocument result) => result.RootElement.GetProperty("status").GetString()!;

    [Fact]
    public void Capabilities_are_local_bounded_and_do_not_require_snapshot()
    {
        using var doc = JsonDocument.Parse(WorldAtlasMcpFacade.QueryJson("{\"op\":\"capabilities\"}", null));
        Assert.Equal("KNOWN", Status(doc));
        Assert.True(doc.RootElement.GetProperty("readOnly").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("clientContacted").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("runtimeCertified").GetBoolean());
        Assert.Equal(5, doc.RootElement.GetProperty("operations").GetArrayLength());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("{\"op\":\"list_maps\",}")]
    [InlineData("{\"op\":\"list_maps\"} garbage")]
    [InlineData("{\"op\":\"list_maps\",\"limit\":2,\"limit\":2}")]
    [InlineData("{\"op\":\"list_maps\",\"run_lua\":true}")]
    [InlineData("{\"op\":\"write_map\"}")]
    [InlineData("{\"op\":\"list_maps\",\"limit\":0}")]
    [InlineData("{\"op\":\"list_maps\",\"limit\":51}")]
    [InlineData("{\"op\":\"list_maps\",\"offset\":-1}")]
    [InlineData("{\"op\":\"inspect_map\",\"mapId\":\"1\"}")]
    [InlineData("{\"op\":\"inspect_map\",\"mapId\":1.1}")]
    [InlineData("{\"op\":\"inspect_map\",\"mapId\":-1}")]
    [InlineData("{\"op\":\"map_transitions\",\"mapId\":1,\"feature\":\"cave\"}")]
    [InlineData("{\"op\":\"find_path\",\"mapId\":1,\"targetMapId\":3,\"maxHops\":33}")]
    [InlineData("{\"op\":\"capabilities\",\"mapId\":1}")]
    [InlineData("{\"op\":\"list_maps\",\"feature\":\"password\"}")]
    public void Malformed_or_unsafe_requests_are_refused(string input)
    {
        using var result = Query(input);
        Assert.Equal("INVALID_REQUEST", Status(result));
        Assert.False(result.RootElement.GetProperty("executed").GetBoolean());
    }

    [Fact]
    public void Request_byte_limit_rejects_unbounded_payload()
    {
        using var doc = Query("{\"op\":\"list_maps\",\"a\":\"" + new string('x', 6000) + "\"}");
        Assert.Equal("INVALID_REQUEST", Status(doc));
    }

    [Fact]
    public void Missing_snapshot_is_unknown_not_empty_world()
    {
        using var doc = JsonDocument.Parse(WorldAtlasMcpFacade.QueryJson("{\"op\":\"list_maps\"}", null));
        Assert.Equal("UNKNOWN", Status(doc));
        Assert.False(doc.RootElement.GetProperty("clientContacted").GetBoolean());
    }

    [Fact]
    public void List_maps_filters_cave_entrances_without_guessing_coordinates()
    {
        using var result = Query("{\"op\":\"list_maps\",\"feature\":\"cave_entrance\",\"limit\":3}");
        Assert.Equal("KNOWN", Status(result));
        Assert.Equal(1, result.RootElement.GetProperty("total").GetInt32());
        var map = result.RootElement.GetProperty("maps")[0];
        Assert.Equal(1, map.GetProperty("mapId").GetInt64());
        Assert.Equal("VERIFIED", map.GetProperty("status").GetString());
        Assert.Equal("atlas-fixture", map.GetProperty("provenance").GetProperty("sourceId").GetString());
    }

    [Fact]
    public void Page_is_sorted_deterministically_and_bounded()
    {
        using var result = Query("{\"op\":\"list_maps\",\"offset\":1,\"limit\":2}");
        Assert.Equal(4, result.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(2, result.RootElement.GetProperty("maps").GetArrayLength());
        Assert.Equal(2, result.RootElement.GetProperty("maps")[0].GetProperty("mapId").GetInt64());
        Assert.True(result.RootElement.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public void Map_inspection_returns_unknown_for_missing_or_unapproved_source()
    {
        var atlas = new WorldAtlasMcpSnapshot("3.7.11.11", new[] { Map(1, permit: false) }, []);
        using var result = Query("{\"op\":\"inspect_map\",\"mapId\":1}", atlas);
        Assert.Equal("UNKNOWN", Status(result));
        Assert.DoesNotContain("atlas-fixture", result.RootElement.ToString());
    }

    [Fact]
    public void Dofusdb_sources_are_never_disclosed_while_partner_ai_terms_unverified()
    {
        var map = Map(1) with { Provenance = Source(kind: "dofusdb") };
        var atlas = new WorldAtlasMcpSnapshot("3.7.11.11", [map], []);
        using var result = Query("{\"op\":\"list_maps\"}", atlas);
        Assert.Equal("UNKNOWN", Status(result));
        Assert.Equal(0, result.RootElement.GetProperty("total").GetInt32());
        Assert.DoesNotContain("dofusdb", result.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inspect_shows_explicit_candidate_without_promoting_it()
    {
        var atlas = new WorldAtlasMcpSnapshot("3.7.11.11",
            [Map(1, AtlasEvidenceStatus.CANDIDATE)], []);
        using var result = Query("{\"op\":\"inspect_map\",\"mapId\":1}", atlas);
        Assert.Equal("CANDIDATE", Status(result));
        Assert.False(result.RootElement.GetProperty("runtimeCertified").GetBoolean());
    }

    [Fact]
    public void Transitions_preserve_cave_provenance_cell_and_direction()
    {
        using var result = Query("{\"op\":\"map_transitions\",\"mapId\":1}");
        Assert.Equal("KNOWN", Status(result));
        var item = result.RootElement.GetProperty("transitions")[0];
        Assert.Equal("cave-1", item.GetProperty("edgeId").GetString());
        Assert.Equal(2, item.GetProperty("toMapId").GetInt64());
        Assert.Equal("CAVE", item.GetProperty("kind").GetString());
        Assert.Equal(210, item.GetProperty("cellId").GetInt32());
        Assert.Equal("VERIFIED", item.GetProperty("status").GetString());
        Assert.Equal(Digest, item.GetProperty("provenance").GetProperty("sha256").GetString());
    }

    [Fact]
    public void Unknown_destination_is_never_fabricated_or_routed()
    {
        var atlas = new WorldAtlasMcpSnapshot("3.7.11.11", [Map(1), Map(2)],
            [Edge("unknown-cave", 1, null)]);
        using var transitions = Query("{\"op\":\"map_transitions\",\"mapId\":1}", atlas);
        Assert.Equal("UNKNOWN", Status(transitions));
        var edge = transitions.RootElement.GetProperty("transitions")[0];
        Assert.Equal(JsonValueKind.Null, edge.GetProperty("toMapId").ValueKind);
        Assert.Equal("UNKNOWN", edge.GetProperty("status").GetString());
        using var path = Query("{\"op\":\"find_path\",\"mapId\":1,\"targetMapId\":2}", atlas);
        Assert.Equal("UNKNOWN", Status(path));
    }

    [Fact]
    public void Verified_path_uses_only_real_declared_edges_with_provenance()
    {
        using var result = Query("{\"op\":\"find_path\",\"mapId\":1,\"targetMapId\":3}");
        Assert.Equal("KNOWN", Status(result));
        Assert.Equal(2, result.RootElement.GetProperty("hopCount").GetInt32());
        var edges = result.RootElement.GetProperty("edges");
        Assert.Equal("cave-1", edges[0].GetProperty("edgeId").GetString());
        Assert.Equal("cave-2", edges[1].GetProperty("edgeId").GetString());
        Assert.False(result.RootElement.GetProperty("executed").GetBoolean());
    }

    [Fact]
    public void Different_sectors_cannot_be_crossed_by_map_only_pathfinding()
    {
        var graph = new WorldAtlasMcpSnapshot("3.7.11.11", [Map(1), Map(2), Map(3)],
            [Edge("door-a", 1, 2), Edge("door-b", 2, 3) with { FromZone = 1 }]);
        using var response = Query("{\"op\":\"find_path\",\"mapId\":1,\"targetMapId\":3}", graph);
        Assert.Equal("UNKNOWN", Status(response));
    }

    [Fact]
    public void Conditional_edge_is_not_traversable_without_server_proof()
    {
        var graph = new WorldAtlasMcpSnapshot("3.7.11.11", [Map(1), Map(2)],
            [Edge("door-q", 1, 2) with { Criterion = "Qf230" }]);
        using var response = Query("{\"op\":\"find_path\",\"mapId\":1,\"targetMapId\":2}", graph);
        Assert.Equal("UNKNOWN", Status(response));
    }

    [Fact]
    public void Ambiguous_start_sector_requires_explicit_zone()
    {
        var graph = new WorldAtlasMcpSnapshot("3.7.11.11", [Map(1), Map(2), Map(3)],
            [Edge("east", 1, 2), Edge("west", 1, 3) with { FromZone = 2 }]);
        using var unknown = Query("{\"op\":\"find_path\",\"mapId\":1,\"targetMapId\":2}", graph);
        Assert.Equal("UNKNOWN", Status(unknown));
        using var known = Query("{\"op\":\"find_path\",\"mapId\":1,\"targetMapId\":2,\"startZone\":1}", graph);
        Assert.Equal("KNOWN", Status(known));
    }

    [Fact]
    public void Candidate_zaap_edge_does_not_become_a_verified_route()
    {
        using var result = Query("{\"op\":\"find_path\",\"mapId\":1,\"targetMapId\":4}");
        Assert.Equal("UNKNOWN", Status(result));
        Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("hopCount").ValueKind);
    }

    [Fact]
    public void Unknown_kind_cannot_be_routed_even_if_edge_label_says_verified()
    {
        var atlas = new WorldAtlasMcpSnapshot("3.7.11.11", [Map(1), Map(2)],
            [Edge("unknown-kind", 1, 2, kind: AtlasTransitionKind.UNKNOWN)]);
        using var transition = Query("{\"op\":\"map_transitions\",\"mapId\":1}", atlas);
        Assert.Equal("UNKNOWN",
            transition.RootElement.GetProperty("transitions")[0].GetProperty("status").GetString());
        using var path = Query("{\"op\":\"find_path\",\"mapId\":1,\"targetMapId\":2}", atlas);
        Assert.Equal("UNKNOWN", Status(path));
    }

    [Fact]
    public void Hidden_destination_does_not_leak_its_id_through_transitions()
    {
        var atlas = new WorldAtlasMcpSnapshot("3.7.11.11",
            [Map(1), Map(2, permit: false)], [Edge("hidden-destination", 1, 2)]);
        using var response = Query("{\"op\":\"map_transitions\",\"mapId\":1}", atlas);
        Assert.Equal("UNKNOWN",
            response.RootElement.GetProperty("transitions")[0].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null,
            response.RootElement.GetProperty("transitions")[0].GetProperty("toMapId").ValueKind);
        using var path = Query("{\"op\":\"find_path\",\"mapId\":1,\"targetMapId\":2}", atlas);
        Assert.Equal("UNKNOWN", Status(path));
    }

    [Fact]
    public void DofusDB_aliases_are_restricted()
    {
        foreach (var alias in new[] { "dofusdb", "Dofus-DB", "dofus.db", "DOFUS_DB" })
        {
            var atlas = new WorldAtlasMcpSnapshot("3.7.11.11",
                [Map(1) with { Provenance = Source(kind: alias) }], []);
            using var result = Query("{\"op\":\"inspect_map\",\"mapId\":1}", atlas);
            Assert.Equal("UNKNOWN", Status(result));
        }
    }

    [Fact]
    public void No_recorded_edges_is_unknown_not_unreachable()
    {
        using var result = Query("{\"op\":\"map_transitions\",\"mapId\":4}");
        Assert.Equal("UNKNOWN", Status(result));
        Assert.Contains("not proof", result.RootElement.GetProperty("note").GetString()!);
    }

    [Fact]
    public void Max_hops_stops_route_without_extrapolating()
    {
        using var result = Query("{\"op\":\"find_path\",\"mapId\":1,\"targetMapId\":3,\"maxHops\":1}");
        Assert.Equal("UNKNOWN", Status(result));
    }

    [Fact]
    public void Duplicate_maps_edges_and_excessive_volume_fail_closed()
    {
        Assert.Throws<ArgumentException>(() => new WorldAtlasMcpSnapshot("3.7.11.11", [Map(1), Map(1)], []));
        Assert.Throws<ArgumentException>(() => new WorldAtlasMcpSnapshot("3.7.11.11",
            [Map(1), Map(2)], [Edge("a", 1, 2), Edge("a", 1, 2)]));
        var over = Enumerable.Range(1, WorldAtlasMcpSnapshot.MaxMaps + 1).Select(i => Map(i)).ToArray();
        Assert.Throws<ArgumentException>(() => new WorldAtlasMcpSnapshot("3.7.11.11", over, []));
        Assert.Throws<ArgumentException>(() => new WorldAtlasMcpSnapshot("3.7.11.11",
            [Map(1) with { Status = (AtlasEvidenceStatus)999 }], []));
    }

    [Fact]
    public void Invalid_version_and_invalid_provenance_never_leak_map()
    {
        Assert.Throws<ArgumentException>(() => new WorldAtlasMcpSnapshot("3.7.11", [Map(1)], []));
        var bad = Map(1) with { Provenance = Source() with { Sha256 = "bad" } };
        using var result = Query("{\"op\":\"inspect_map\",\"mapId\":1}",
            new WorldAtlasMcpSnapshot("3.7.11.11", [bad], []));
        Assert.Equal("UNKNOWN", Status(result));
    }

    [Fact]
    public void Atlas_snapshot_features_are_copied_to_prevent_external_mutation()
    {
        string[] features = ["cave_entrance"];
        var atlas = new WorldAtlasMcpSnapshot("3.7.11.11", [Map(1, features: features)], []);
        features[0] = "zaap";
        using var result = Query("{\"op\":\"inspect_map\",\"mapId\":1}", atlas);
        Assert.Equal("cave_entrance", result.RootElement.GetProperty("map").GetProperty("features")[0].GetString());
    }
}
