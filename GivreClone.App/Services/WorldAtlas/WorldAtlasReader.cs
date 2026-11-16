using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace GivreClone.App.Services.WorldAtlas;

/// <summary>
/// Bounded, offline, read-only importer. File names and declared versions do not
/// establish authenticity. A caller-supplied trusted manifest SHA-256 pins the
/// exact local review set; without that pin EVERY edge is unverified.
///
/// Snapshot v1 accepts maps.json + edges.json, or the already-established
/// map_positions.json + worldgraph5.json cache format. Legacy cache edges
/// remain CANDIDATE regardless of the manifest pin: no claim that a cached
/// edge equals an observed server-confirmed passage.
/// </summary>
public static class WorldAtlasReader
{
    public const int MaxManifestBytes = 65_536;
    public const long MaxMapsBytes = 8L * 1024 * 1024;
    public const long MaxEdgesBytes = 24L * 1024 * 1024;
    public const int MaxMaps = 25_000;
    public const int MaxEdges = 100_000;
    private sealed record Asset(string File, long Bytes, string Sha256);

    /// <summary>
    /// Preview the existing two locally generated GivreWork cache files without
    /// requiring a new manifest. Both are read as CANDIDATE only; the caller's
    /// version label is unverified and must never be used to certify compatibility.
    /// No cached edges can be routed, even if their local bytes have a SHA-256.
    /// This is intended for the map inspector before a pinned atlas is available.
    /// </summary>
    public static AtlasImportResult ReadLegacyCacheFiles(string dataDir, string expectedVersion)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dataDir) || !ValidVersion(expectedVersion))
                return Rejected("Missing data directory or invalid unverified version label.");
            var dir = new DirectoryInfo(Path.GetFullPath(dataDir));
            if (!dir.Exists || (dir.Attributes & FileAttributes.ReparsePoint) != 0)
                return Rejected("Cache directory missing or linked.");
            byte[] positions = ReadSmall(Path.Combine(dir.FullName, "map_positions.json"), MaxMapsBytes, 2);
            byte[] graph = ReadSmall(Path.Combine(dir.FullName, "worldgraph5.json"), MaxEdgesBytes, 2);
            using var mapDoc = Parse(positions);
            using var graphDoc = Parse(graph);
            var maps = LegacyMaps(mapDoc.RootElement);
            if (maps.Count == 0) return Rejected("No map positions in legacy cache.");
            string edgeHash = Sha(graph);
            var edges = LegacyEdges(graphDoc.RootElement, maps, "local-unpinned-cache",
                "local-client-cache", expectedVersion, edgeHash);
            var snapshot = new AtlasSnapshot(expectedVersion, "local-unpinned-cache",
                "local-client-cache", edgeHash, false, maps, edges);
            return new AtlasImportResult("UNVERIFIED", snapshot,
                ["Version is caller-declared, no trusted manifest; all nodes and edges are candidates."]);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or InvalidDataException or JsonException or ArgumentException or OverflowException
            or NotSupportedException or FormatException)
        {
            return Rejected("Legacy cache rejected: " + error.GetType().Name);
        }
    }

    public static AtlasImportResult ReadDirectory(string folder, string? expectedVersion = null,
        string? trustedManifestSha256 = null)
    {
        var issues = new List<string>();
        try
        {
            if (string.IsNullOrWhiteSpace(folder)
                || (expectedVersion is not null && !ValidVersion(expectedVersion))
                || (trustedManifestSha256 is not null && !HashText(trustedManifestSha256)))
                return Rejected("Invalid input or expected pin.");
            var dir = new DirectoryInfo(Path.GetFullPath(folder));
            if (!dir.Exists || (dir.Attributes & FileAttributes.ReparsePoint) != 0)
                return Rejected("Missing dataset directory or linked directory.");
            byte[] manifestBytes = ReadSmall(Path.Combine(dir.FullName, "atlas-manifest.json"),
                MaxManifestBytes, minBytes: 2);
            var manifestHash = Sha(manifestBytes);
            bool pinned = trustedManifestSha256 is not null
                && manifestHash.Equals(trustedManifestSha256, StringComparison.OrdinalIgnoreCase);
            // An incorrect EXPLICIT pin is a hard failure, never a downgrade to a loose import.
            if (trustedManifestSha256 is not null && !pinned)
                return Rejected("Trusted manifest SHA-256 did not match.");

            using var manifestDoc = Parse(manifestBytes);
            var manifest = manifestDoc.RootElement;
            Properties(manifest, ["schemaVersion", "gameVersion", "sourceId", "sourceKind", "assets"]);
            int schema = Int(manifest, "schemaVersion");
            if (schema != 1) return Rejected("Unsupported atlas schema version.");
            string version = Str(manifest, "gameVersion", 24);
            if (!ValidVersion(version) || expectedVersion is not null && version != expectedVersion)
                return Rejected("Unexpected game version.");
            string sourceId = Str(manifest, "sourceId", 96);
            string sourceKind = Str(manifest, "sourceKind", 48);
            if (!new[] { "local-client-cache", "client-world-graph-reviewed", "observed-transition",
                         "partner-export", "local-extraction" }.Contains(sourceKind, StringComparer.Ordinal))
                return Rejected("Unsupported atlas sourceKind.");

            var assetArray = manifest.GetProperty("assets");
            if (assetArray.ValueKind != JsonValueKind.Array || assetArray.GetArrayLength() != 2)
                return Rejected("Atlas requires exactly two pinned files.");
            var assets = new Dictionary<string, Asset>(StringComparer.Ordinal);
            foreach (var entry in assetArray.EnumerateArray())
            {
                Properties(entry, ["file", "bytes", "sha256"]);
                string name = Str(entry, "file", 32);
                if (name is not ("maps.json" or "edges.json" or "map_positions.json" or "worldgraph5.json"))
                    return Rejected("Disallowed asset file.");
                if (!assets.TryAdd(name, new Asset(name, Long(entry, "bytes"),
                    Str(entry, "sha256", 64).ToLowerInvariant())))
                    return Rejected("Duplicate asset.");
                if (!HashText(assets[name].Sha256))
                    return Rejected("Invalid asset SHA-256.");
            }
            bool modern = assets.ContainsKey("maps.json") && assets.ContainsKey("edges.json");
            bool legacy = assets.ContainsKey("map_positions.json") && assets.ContainsKey("worldgraph5.json");
            if (!modern && !legacy)
                return Rejected("Assets must match one recognized atlas format, no mixing.");

            var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var pair in assets)
            {
                long max = pair.Key is "maps.json" or "map_positions.json" ? MaxMapsBytes : MaxEdgesBytes;
                if (pair.Value.Bytes is < 2 || pair.Value.Bytes > max)
                    return Rejected("Asset size outside import bounds.");
                byte[] content = ReadSmall(Path.Combine(dir.FullName, pair.Key), max, pair.Value.Bytes);
                if (content.LongLength != pair.Value.Bytes || Sha(content) != pair.Value.Sha256)
                    return Rejected("Asset length or SHA-256 mismatch.");
                bytes.Add(pair.Key, content);
            }

            using var mapDoc = Parse(bytes[modern ? "maps.json" : "map_positions.json"]);
            using var edgeDoc = Parse(bytes[modern ? "edges.json" : "worldgraph5.json"]);
            // An externally pinned third-party export may attest bytes but
            // does not attest in-game map availability; reserve 'verified'
            // for an explicitly reviewed client or observed transition source.
            bool reviewedSource = sourceKind is "observed-transition" or "client-world-graph-reviewed";
            var maps = modern ? ModernMaps(mapDoc.RootElement, pinned && reviewedSource) : LegacyMaps(mapDoc.RootElement);
            var transitions = modern
                ? ModernEdges(edgeDoc.RootElement, maps, pinned, sourceId, sourceKind, version,
                    assets["edges.json"].Sha256)
                : LegacyEdges(edgeDoc.RootElement, maps, sourceId, sourceKind, version,
                    assets["worldgraph5.json"].Sha256);
            if (maps.Count == 0)
                return Rejected("No maps were parsed.");
            var snapshot = new AtlasSnapshot(version, sourceId, sourceKind, manifestHash, pinned,
                maps, transitions);
            return new AtlasImportResult(pinned ? "PINNED_REVIEW" : "UNVERIFIED",
                snapshot, issues);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or InvalidDataException or JsonException or ArgumentException or OverflowException
            or NotSupportedException or FormatException)
        {
            return Rejected("Atlas rejected: " + error.GetType().Name
                + (error is InvalidDataException ? ": " + error.Message : ""));
        }
    }

    private static Dictionary<long, AtlasMap> ModernMaps(JsonElement root, bool pinned)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() > MaxMaps)
            throw new InvalidDataException("Map row limit exceeded or invalid map array.");
        var maps = new Dictionary<long, AtlasMap>();
        foreach (var item in root.EnumerateArray())
        {
            Properties(item, ["mapId", "x", "y", "worldId", "layer", "subAreaId", "features", "status"]);
            long id = Long(item, "mapId");
            if (id <= 0 || maps.ContainsKey(id))
                throw new InvalidDataException("Invalid or duplicate mapId.");
            var featuresJson = item.GetProperty("features");
            if (featuresJson.ValueKind != JsonValueKind.Array || featuresJson.GetArrayLength() > 24)
                throw new InvalidDataException("Invalid features array.");
            var features = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in featuresJson.EnumerateArray())
            {
                if (f.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("Non-string feature.");
                string value = f.GetString() ?? "";
                if (value.Length is < 1 or > 48 || !features.Add(value))
                    throw new InvalidDataException("Invalid or duplicated feature.");
            }
            string status = Status(Str(item, "status", 12));
            int? layer = OptionalInt(item, "layer");
            int? world = OptionalInt(item, "worldId");
            int? sub = OptionalInt(item, "subAreaId");
            if (sub is < 0 || layer is < -128 or > 128 || world is < -128 or > 1024)
                throw new InvalidDataException("Invalid world/layer/subarea.");
            maps.Add(id, new AtlasMap(id, OptionalInt(item, "x"), OptionalInt(item, "y"),
                world, layer, sub, features.Order(StringComparer.Ordinal).ToArray(),
                // An untrusted manifest must never display a map as "verified" in UI/MCP.
                pinned ? status : status == "unknown" ? "unknown" : "candidate"));
        }
        return maps;
    }

    private static Dictionary<long, AtlasMap> LegacyMaps(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Legacy map-position cache must be an object.");
        var maps = new Dictionary<long, AtlasMap>();
        foreach (var pair in root.EnumerateObject())
        {
            if (maps.Count >= MaxMaps || !long.TryParse(pair.Name, NumberStyles.None,
                    CultureInfo.InvariantCulture, out long id) || id <= 0
                || pair.Value.ValueKind != JsonValueKind.Array
                || pair.Value.GetArrayLength() is < 3 or > 4)
                throw new InvalidDataException("Invalid legacy map entry.");
            var values = pair.Value;
            if (!maps.TryAdd(id, new AtlasMap(id, values[0].GetInt32(), values[1].GetInt32(),
                    values[2].GetInt32(), null,
                    values.GetArrayLength() == 4 ? values[3].GetInt32() : null,
                    [], "candidate")))
                throw new InvalidDataException("Duplicate legacy mapId.");
        }
        return maps;
    }

    private static List<AtlasTransition> ModernEdges(JsonElement root,
        Dictionary<long, AtlasMap> maps, bool pinned, string sourceId,
        string sourceKind, string version, string sourceSha)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() > MaxEdges)
            throw new InvalidDataException("Edge count cap exceeded.");
        var edges = new List<AtlasTransition>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in root.EnumerateArray())
        {
            Properties(item, ["edgeId", "fromMapId", "toMapId", "kind",
                "direction", "cellId", "fromZone", "toZone", "criterion", "status",
                "evidenceKind", "evidenceHash"]);
            string edgeId = Str(item, "edgeId", 128);
            if (!keys.Add(edgeId)) throw new InvalidDataException("Duplicate edgeId.");
            long from = Long(item, "fromMapId");
            long to = Long(item, "toMapId");
            string kind = Str(item, "kind", 12);
            if (kind is not ("walk" or "door" or "stair" or "cave" or "zaap" or "unknown"))
                throw new InvalidDataException("Unknown edge kind.");
            int? cell = OptionalInt(item, "cellId");
            int? dir = OptionalInt(item, "direction");
            int? fromZone = OptionalInt(item, "fromZone");
            int? toZone = OptionalInt(item, "toZone");
            if (cell is < 0 or >= 560 || dir is < 0 or > 7
                || fromZone is < 0 or > 255 || toZone is < 0 or > 255)
                throw new InvalidDataException("Invalid cell/zone/direction.");
            string? criterion = OptionalString(item, "criterion", 160);
            string declared = Status(Str(item, "status", 12));
            string evidenceKind = Str(item, "evidenceKind", 40);
            string evidenceHash = Str(item, "evidenceHash", 64);
            if (evidenceKind is not ("observed-transition" or "client-world-graph" or "source-inference")
                || (evidenceHash.Length != 0 && !HashText(evidenceHash)))
                throw new InvalidDataException("Invalid transition evidence descriptor.");
            bool sound = pinned && declared == "verified"
                && evidenceKind == "observed-transition" && HashText(evidenceHash)
                && sourceKind is "observed-transition" or "client-world-graph-reviewed"
                && criterion is null && cell is >= 0 and < 560
                && fromZone is > 0 && toZone is > 0 && kind != "unknown"
                && maps.ContainsKey(from) && maps.ContainsKey(to);
            string status = sound ? "verified" : declared == "unknown" ? "unknown" : "candidate";
            // Do not let maps lacking pinned provenance become route vertices.
            // Sources known only by position stay candidate even if they are named in a route.
            edges.Add(new AtlasTransition(edgeId, from, to, kind, dir, cell, fromZone,
                toZone, criterion, sourceId, sourceKind, version, sourceSha, status));
        }
        return edges;
    }

    private static List<AtlasTransition> LegacyEdges(JsonElement root,
        Dictionary<long, AtlasMap> maps, string sourceId, string sourceKind,
        string version, string sourceSha)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("WorldGraphCache must be a JSON object.");
        var edges = new List<AtlasTransition>();
        int cacheVersion = root.TryGetProperty("_v", out var ver) && ver.ValueKind == JsonValueKind.Number
            ? ver.GetInt32() : 0;
        if (cacheVersion != 5)
            throw new InvalidDataException("Only WorldGraphCache v5 with known sector defaults is accepted.");
        foreach (var pair in root.EnumerateObject())
        {
            if (pair.Name == "_v") continue;
            if (!long.TryParse(pair.Name, NumberStyles.None, CultureInfo.InvariantCulture, out long from)
                || from <= 0 || pair.Value.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Invalid outgoing-edge map.");
            int ordinal = 0;
            foreach (var edge in pair.Value.EnumerateArray())
            {
                if (edges.Count >= MaxEdges || edge.ValueKind != JsonValueKind.Array
                    || edge.GetArrayLength() is < 4 or > 10)
                    throw new InvalidDataException("Invalid legacy edge.");
                int cell = edge[0].GetInt32();
                int sendMap = edge[1].GetInt32();
                int direction = edge[2].GetInt32();
                int type = edge[3].GetInt32();
                string? criterion = edge.GetArrayLength() > 4 && edge[4].ValueKind == JsonValueKind.String
                    ? edge[4].GetString() : null;
                int arrived = edge.GetArrayLength() > 5 && edge[5].ValueKind == JsonValueKind.Number
                    ? edge[5].GetInt32() : 0;
                int fromZone = edge.GetArrayLength() > 6 ? edge[6].GetInt32() : 1;
                int toZone = edge.GetArrayLength() > 7 ? edge[7].GetInt32() : 1;
                if (cell is < 0 or >= 560 || direction is < 0 or > 7
                    || sendMap <= 0 || fromZone is < 0 or > 255 || toZone is < 0 or > 255)
                    throw new InvalidDataException("Legacy edge with out-of-bounds coordinates.");
                long destination = arrived > 0 ? arrived : sendMap;
                // 1 = border; 32 = interactive/door (NOT specifically cave);
                // 2 = ScrollAction (NOT proof of zaap); all others unknown.
                string kind = type switch { 1 => "walk", 32 => "door", _ => "unknown" };
                edges.Add(new AtlasTransition(from + ":" + (ordinal++).ToString(CultureInfo.InvariantCulture),
                    from, destination, kind, direction, cell, fromZone, toZone,
                    criterion, sourceId, sourceKind, version, sourceSha, "candidate"));
            }
        }
        return edges;
    }

    private static byte[] ReadSmall(string path, long max, long minBytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0
            || info.Length < minBytes || info.Length > max)
            throw new InvalidDataException("Missing, linked or oversized input.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 32_768, FileOptions.SequentialScan);
        if (stream.Length != info.Length) throw new InvalidDataException("Input changed during read.");
        var result = new byte[checked((int)stream.Length)];
        stream.ReadExactly(result);
        return result;
    }

    private static JsonDocument Parse(byte[] raw)
    {
        int bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF ? 3 : 0;
        return JsonDocument.Parse(new ReadOnlyMemory<byte>(raw, bom, raw.Length - bom),
            new JsonDocumentOptions { MaxDepth = 16, CommentHandling = JsonCommentHandling.Disallow });
    }

    private static void Properties(JsonElement value, string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Expected JSON object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in value.EnumerateObject())
            if (!seen.Add(pair.Name) || !names.Contains(pair.Name, StringComparer.Ordinal))
                throw new InvalidDataException("Duplicate or unsupported property.");
        if (seen.Count != names.Length)
            throw new InvalidDataException("Incomplete record.");
    }

    private static int Int(JsonElement item, string name) => item.GetProperty(name).GetInt32();
    private static long Long(JsonElement item, string name) => item.GetProperty(name).GetInt64();
    private static int? OptionalInt(JsonElement item, string name)
        => item.GetProperty(name).ValueKind == JsonValueKind.Null ? null : item.GetProperty(name).GetInt32();
    private static string? OptionalString(JsonElement item, string name, int maxLength)
        => item.GetProperty(name).ValueKind == JsonValueKind.Null ? null : Str(item, name, maxLength);
    private static string Str(JsonElement item, string name, int maxLength)
    {
        var prop = item.GetProperty(name);
        if (prop.ValueKind != JsonValueKind.String || prop.GetString() is not { } value
            || value.Length is 0 || value.Length > maxLength)
            throw new InvalidDataException("Invalid string field: " + name);
        return value;
    }
    private static bool HashText(string? s)
        => s is { Length: 64 } && s.All(c => c is >= '0' and <= '9'
            or >= 'a' and <= 'f' or >= 'A' and <= 'F');
    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static bool ValidVersion(string version)
    {
        string[] parts = version.Split('.');
        return parts.Length == 4 && parts.All(s => s.Length is > 0 and <= 5
            && s.All(c => c is >= '0' and <= '9')
            && int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n is >= 0 and <= 65535);
    }
    private static string Status(string s)
        => s is "verified" or "candidate" or "unknown" ? s : throw new InvalidDataException("Invalid status.");
    private static AtlasImportResult Rejected(string reason) => new("REJECTED", null, [reason]);
}
