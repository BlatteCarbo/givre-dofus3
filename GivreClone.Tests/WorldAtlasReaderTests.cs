using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GivreClone.App.Services.WorldAtlas;
using Xunit;

namespace GivreClone.Tests;

public sealed class WorldAtlasReaderTests
{
    private sealed class Fixture : IDisposable
    {
        public readonly string Folder = Path.Combine(Path.GetTempPath(), "GivreAtlasTests",
            Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Folder);
        public string File(string name) => Path.Combine(Folder, name);
        public string Sha(string name) => Hex(System.IO.File.ReadAllBytes(File(name)));
        public static string Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        public static object Map(long id, int? x, int? y, int? worldId = 1,
            int? layer = 0, int? subAreaId = 479, string[]? features = null, string status = "verified")
            => new { mapId = id, x, y, worldId, layer, subAreaId,
                features = features ?? Array.Empty<string>(), status };

        public static object Edge(string id, long from, long to, string kind = "walk",
            int? direction = 2, int? cell = 13, int? fromZone = 1, int? toZone = 1,
            string? criterion = null, string status = "verified",
            string evidenceKind = "observed-transition", string? evidenceHash = null)
            => new { edgeId = id, fromMapId = from, toMapId = to, kind,
                direction, cellId = cell, fromZone, toZone, criterion, status,
                evidenceKind, evidenceHash = evidenceHash ?? new string('a', 64) };

        public string Write(object[] maps, object[] edges, string version = "3.7.12.12",
            string sourceKind = "observed-transition")
        {
            System.IO.File.WriteAllText(File("maps.json"), JsonSerializer.Serialize(maps), new UTF8Encoding(false));
            System.IO.File.WriteAllText(File("edges.json"), JsonSerializer.Serialize(edges), new UTF8Encoding(false));
            WriteManifest("maps.json", "edges.json", version, sourceKind);
            return Sha("atlas-manifest.json");
        }

        public string Legacy(string positions, string graph, string version = "3.7.7.6")
        {
            System.IO.File.WriteAllText(File("map_positions.json"), positions, new UTF8Encoding(false));
            System.IO.File.WriteAllText(File("worldgraph5.json"), graph, new UTF8Encoding(false));
            WriteManifest("map_positions.json", "worldgraph5.json", version, "local-client-cache");
            return Sha("atlas-manifest.json");
        }

        private void WriteManifest(string maps, string edges, string version, string sourceKind)
        {
            object Asset(string name) => new
            {
                file = name,
                bytes = new FileInfo(File(name)).Length,
                sha256 = Sha(name)
            };
            var doc = new
            {
                schemaVersion = 1,
                gameVersion = version,
                sourceId = "offline-fixture",
                sourceKind,
                assets = new[] { Asset(maps), Asset(edges) }
            };
            System.IO.File.WriteAllText(File("atlas-manifest.json"),
                JsonSerializer.Serialize(doc), new UTF8Encoding(false));
        }

        public void Dispose()
        {
            if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
        }
    }

    private static AtlasSnapshot BuildPinned(Fixture f, object[] maps, object[] edges)
    {
        string pin = f.Write(maps, edges);
        var result = WorldAtlasReader.ReadDirectory(f.Folder, "3.7.12.12", pin);
        Assert.Equal("PINNED_REVIEW", result.Status);
        Assert.Empty(result.Issues);
        Assert.NotNull(result.Snapshot);
        return result.Snapshot!;
    }

    [Fact]
    public void Pinned_observed_edges_yield_exact_zone_compatible_route_without_geo_shortcut()
    {
        using var f = new Fixture();
        var maps = new[] { Fixture.Map(101, 3, -3), Fixture.Map(102, 3, -3, layer: -1),
            Fixture.Map(103, 4, -3, layer: -1), Fixture.Map(104, 5, -3) };
        var edges = new[] { Fixture.Edge("entry", 101, 102, "cave", fromZone: 1, toZone: 2),
            Fixture.Edge("tunnel", 102, 103, "stair", fromZone: 2, toZone: 3),
            Fixture.Edge("exit", 103, 104, "door", fromZone: 3, toZone: 1) };
        var snapshot = BuildPinned(f, maps, edges);
        var route = WorldAtlasEngine.FindRoute(snapshot, 101, 104, startZone: 1);
        Assert.Equal("VERIFIED_SOURCE_PATH", route.Status);
        Assert.Equal(new[] { "entry", "tunnel", "exit" }, route.Steps.Select(e => e.EdgeId));
        Assert.False(route.CanExecuteAutomatically);
        Assert.Equal("cave", route.Steps[0].Kind);
        Assert.Equal(-1, WorldAtlasEngine.GetMap(snapshot, 102)!.Layer);
        Assert.Equal(2, WorldAtlasEngine.FindMaps(snapshot, x: 3, y: -3).Count);
    }

    [Fact]
    public void Without_external_manifest_pin_no_edges_or_route_can_claim_to_be_verified()
    {
        using var f = new Fixture();
        f.Write([Fixture.Map(100, 0, 0), Fixture.Map(200, 1, 0)],
            [Fixture.Edge("e", 100, 200)]);
        var result = WorldAtlasReader.ReadDirectory(f.Folder);
        Assert.Equal("UNVERIFIED", result.Status);
        Assert.False(result.Snapshot!.TrustedManifestMatched);
        Assert.Equal("candidate", WorldAtlasEngine.GetMap(result.Snapshot, 100)!.Status);
        Assert.Equal("candidate", Assert.Single(result.Snapshot.Transitions).Status);
        Assert.Equal("UNVERIFIED", WorldAtlasEngine.FindRoute(result.Snapshot, 100, 200).Status);
    }

    [Theory]
    [InlineData("client-world-graph", "verified", "candidate")]
    [InlineData("source-inference", "verified", "candidate")]
    [InlineData("observed-transition", "candidate", "candidate")]
    [InlineData("observed-transition", "unknown", "unknown")]
    public void A_pin_alone_does_not_upgrade_source_inference_or_unconfirmed_edges(
        string evidenceKind, string declaredStatus, string expectedStatus)
    {
        using var f = new Fixture();
        var snapshot = BuildPinned(f,
            [Fixture.Map(100, 0, 0), Fixture.Map(200, 1, 0)],
            [Fixture.Edge("e", 100, 200, evidenceKind: evidenceKind, status: declaredStatus)]);
        Assert.Equal(expectedStatus, Assert.Single(snapshot.Transitions).Status);
        Assert.Equal("UNVERIFIED", WorldAtlasEngine.FindRoute(snapshot, 100, 200).Status);
    }

    [Fact]
    public void External_partner_exports_remain_candidate_even_if_exact_bytes_are_pinned()
    {
        using var f = new Fixture();
        string hash = f.Write([Fixture.Map(100, 0, 0), Fixture.Map(200, 0, 0)],
            [Fixture.Edge("partner", 100, 200, "zaap")], sourceKind: "partner-export");
        var report = WorldAtlasReader.ReadDirectory(f.Folder, trustedManifestSha256: hash);
        Assert.Equal("candidate", WorldAtlasEngine.GetMap(report.Snapshot, 100)!.Status);
        Assert.Equal("candidate", Assert.Single(report.Snapshot!.Transitions).Status);
        Assert.Equal("UNVERIFIED", WorldAtlasEngine.FindRoute(report.Snapshot, 100, 200).Status);
    }

    [Fact]
    public void Conditional_edge_not_routed_even_with_pinned_observed_evidence()
    {
        using var f = new Fixture();
        var snapshot = BuildPinned(f,
            [Fixture.Map(100, 0, 0), Fixture.Map(200, 1, 0)],
            [Fixture.Edge("gate", 100, 200, "door", criterion: "Qf=230")]);
        Assert.Equal("candidate", Assert.Single(snapshot.Transitions).Status);
        Assert.Equal("UNVERIFIED", WorldAtlasEngine.FindRoute(snapshot, 100, 200).Status);
    }

    [Fact]
    public void Multi_zone_map_refuses_ambiguous_start_zone_or_cross_sector_fiction()
    {
        using var f = new Fixture();
        var snapshot = BuildPinned(f,
            [Fixture.Map(100, 0, 0), Fixture.Map(200, 1, 0), Fixture.Map(300, 2, 0)],
            [Fixture.Edge("wrong-sector", 100, 200, fromZone: 1, toZone: 1),
             Fixture.Edge("right-sector", 100, 300, fromZone: 2, toZone: 2)]);
        Assert.Equal("UNVERIFIED", WorldAtlasEngine.FindRoute(snapshot, 100, 200).Status);
        Assert.Equal("UNVERIFIED", WorldAtlasEngine.FindRoute(snapshot, 100, 200, startZone: 2).Status);
        Assert.Equal("VERIFIED_SOURCE_PATH", WorldAtlasEngine.FindRoute(snapshot, 100, 200, startZone: 1).Status);
    }

    [Fact]
    public void Unconnected_coincident_maps_never_become_a_route()
    {
        using var f = new Fixture();
        var snapshot = BuildPinned(f,
            [Fixture.Map(100, 3, -3, layer: 0), Fixture.Map(200, 3, -3, layer: -1)],
            []);
        Assert.Equal("UNVERIFIED", WorldAtlasEngine.FindRoute(snapshot, 100, 200).Status);
        Assert.Empty(WorldAtlasEngine.GetTransitions(snapshot, 100));
    }

    [Fact]
    public void Legacy_worldgraph5_adapter_preserves_scroll_arrival_and_stays_candidate()
    {
        using var f = new Fixture();
        // Cache [cell, sent, direction, type, criterion, real arrival, fromZone, toZone, element, skill].
        string pin = f.Legacy(
            """{"100":[3,-3,1,479],"200":[4,-3,1,479]}""",
            """{"_v":5,"100":[[13,999,2,2,null,200,1,1,0,0], [14,200,2,32,null,0,1,1,889,361]]}""");
        var result = WorldAtlasReader.ReadDirectory(f.Folder, "3.7.7.6", pin);
        Assert.Equal("PINNED_REVIEW", result.Status);
        Assert.Equal(2, result.Snapshot!.Transitions.Count);
        Assert.Equal(200, result.Snapshot.Transitions[0].ToMapId);
        Assert.Equal("unknown", result.Snapshot.Transitions[0].Kind); // type 2 NOT automatically zaap
        Assert.Equal("door", result.Snapshot.Transitions[1].Kind); // type 32 NOT automatically cave
        Assert.All(result.Snapshot.Transitions, e => Assert.Equal("candidate", e.Status));
        Assert.Equal("candidate", WorldAtlasEngine.GetMap(result.Snapshot, 100)!.Status);
        Assert.Equal("UNVERIFIED", WorldAtlasEngine.FindRoute(result.Snapshot, 100, 200).Status);
    }

    [Fact]
    public void Legacy_cache_v4_or_missing_world_positions_is_rejected()
    {
        using var f = new Fixture();
        string pin = f.Legacy("""{"100":[3,-3,1]}""", """{"_v":4,"100":[[13,200,2,1]]}""");
        Assert.Equal("REJECTED", WorldAtlasReader.ReadDirectory(f.Folder, trustedManifestSha256: pin).Status);
    }

    [Fact]
    public void Unpinned_runtime_caches_are_previewable_but_never_routable()
    {
        using var f = new Fixture();
        f.Legacy(
            """{"100":[3,-3,1,479],"200":[3,-3,-1,479]}""",
            """{"_v":5,"100":[[13,200,2,32,null,0,1,2,120,361]]}""");
        var preview = WorldAtlasReader.ReadLegacyCacheFiles(f.Folder, "3.7.12.12");
        Assert.Equal("UNVERIFIED", preview.Status);
        Assert.False(preview.Snapshot!.TrustedManifestMatched);
        Assert.Equal("candidate", WorldAtlasEngine.GetMap(preview.Snapshot, 100)!.Status);
        Assert.Equal("candidate", Assert.Single(preview.Snapshot.Transitions).Status);
        Assert.Equal("UNVERIFIED", WorldAtlasEngine.FindRoute(preview.Snapshot, 100, 200, startZone: 1).Status);
        Assert.NotEmpty(preview.Issues);
        Assert.False(preview.RuntimeImportAuthorized);
    }

    [Fact]
    public void Runtime_cache_without_both_files_or_valid_version_refuses_read()
    {
        using var f = new Fixture();
        Assert.Equal("REJECTED", WorldAtlasReader.ReadLegacyCacheFiles(f.Folder, "3.7.12.12").Status);
        f.Legacy("""{"100":[3,-3,1]}""", """{"_v":5,"100":[]}""");
        Assert.Equal("REJECTED", WorldAtlasReader.ReadLegacyCacheFiles(f.Folder, "unknown").Status);
    }

    [Fact]
    public void Tampered_asset_hash_or_wrong_manifest_pin_hard_fails()
    {
        using var f = new Fixture();
        string pin = f.Write([Fixture.Map(100, 0, 0)], []);
        Assert.Equal("REJECTED", WorldAtlasReader.ReadDirectory(f.Folder,
            trustedManifestSha256: new string('a', 64)).Status);
        System.IO.File.AppendAllText(f.File("edges.json"), " ");
        var report = WorldAtlasReader.ReadDirectory(f.Folder, trustedManifestSha256: pin);
        Assert.Equal("REJECTED", report.Status);
        Assert.Null(report.Snapshot);
    }

    [Theory]
    [InlineData("../edges.json")]
    [InlineData("maps.sqlite")]
    [InlineData("https://example.invalid/edges.json")]
    public void Path_escaping_and_unsupported_asset_are_rejected(string fakeName)
    {
        using var f = new Fixture();
        f.Write([Fixture.Map(100, 0, 0)], []);
        string text = System.IO.File.ReadAllText(f.File("atlas-manifest.json"));
        System.IO.File.WriteAllText(f.File("atlas-manifest.json"),
            text.Replace("\"edges.json\"", JsonSerializer.Serialize(fakeName), StringComparison.Ordinal));
        Assert.Equal("REJECTED", WorldAtlasReader.ReadDirectory(f.Folder).Status);
    }

    [Theory]
    [InlineData("3.7.12")]
    [InlineData("3.7.12.12-fake")]
    [InlineData("3.7.999999.12")]
    public void Invalid_or_forged_game_version_is_not_accepted(string version)
    {
        using var f = new Fixture();
        f.Write([Fixture.Map(100, 0, 0)], [], version);
        Assert.Equal("REJECTED", WorldAtlasReader.ReadDirectory(f.Folder).Status);
    }

    [Fact]
    public void Rejected_oversized_or_duplicate_json_keys_cannot_enter_graph()
    {
        using var f = new Fixture();
        f.Write([Fixture.Map(100, 0, 0)], []);
        System.IO.File.WriteAllText(f.File("atlas-manifest.json"),
            new string('x', WorldAtlasReader.MaxManifestBytes + 1));
        Assert.Equal("REJECTED", WorldAtlasReader.ReadDirectory(f.Folder).Status);
        f.Write([Fixture.Map(100, 0, 0)], []);
        string original = System.IO.File.ReadAllText(f.File("maps.json"));
        System.IO.File.WriteAllText(f.File("maps.json"),
            original.Replace("\"mapId\":", "\"mapId\":999,\"mapId\":"));
        // Altered bytes without updating the pin are rejected even before schema parsing.
        Assert.Equal("REJECTED", WorldAtlasReader.ReadDirectory(f.Folder).Status);
    }
}
