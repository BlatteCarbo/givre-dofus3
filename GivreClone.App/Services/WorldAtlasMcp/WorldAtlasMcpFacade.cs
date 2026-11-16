using System.Text;
using System.Text.Json;

namespace GivreClone.App.Services.WorldAtlasMcp;

/// <summary>
/// Only supports a fixed, read-only JSON query grammar. No native clients,
/// filesystem I/O, network, script evaluation, Lua or MCP transport dependency.
/// Unknown data is reported UNKNOWN rather than guessed or presented as a route.
/// </summary>
public static class WorldAtlasMcpFacade
{
    public const int MaxRequestBytes = 4096;
    public const int MaxResponseBytes = 65536;
    public const int MaxPageSize = 50;
    public const int MaxPathHops = 32;
    public const int MaxPathExpanded = 2048;

    public static string QueryJson(string? requestJson, WorldAtlasMcpSnapshot? snapshot)
    {
        if (requestJson is null || Encoding.UTF8.GetByteCount(requestJson) > MaxRequestBytes)
            return Reply("INVALID_REQUEST", "Malformed or over-limit JSON request.");

        try
        {
            using var doc = JsonDocument.Parse(requestJson, new JsonDocumentOptions
            {
                AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 8
            });
            JsonElement request = doc.RootElement;
            if (request.ValueKind != JsonValueKind.Object)
                return Reply("INVALID_REQUEST", "Request must be a JSON object.");
            string? operation = null;
            long? mapId = null, targetMapId = null;
            int offset = 0, limit = 20, maxHops = 16;
            int? startZone = null;
            string? feature = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var prop in request.EnumerateObject())
            {
                if (!seen.Add(prop.Name)) return Reply("INVALID_REQUEST", "Duplicate JSON property.");
                switch (prop.Name)
                {
                    case "op":
                        if (prop.Value.ValueKind != JsonValueKind.String) return BadRequest();
                        operation = prop.Value.GetString();
                        break;
                    case "mapId":
                        if (!ReadId(prop.Value, out long id)) return BadRequest();
                        mapId = id;
                        break;
                    case "targetMapId":
                        if (!ReadId(prop.Value, out long target)) return BadRequest();
                        targetMapId = target;
                        break;
                    case "offset":
                        if (!ReadInt(prop.Value, 0, WorldAtlasMcpSnapshot.MaxMaps, out offset)) return BadRequest();
                        break;
                    case "limit":
                        if (!ReadInt(prop.Value, 1, MaxPageSize, out limit)) return BadRequest();
                        break;
                    case "maxHops":
                        if (!ReadInt(prop.Value, 1, MaxPathHops, out maxHops)) return BadRequest();
                        break;
                    case "startZone":
                        if (!ReadInt(prop.Value, 1, 255, out var zone)) return BadRequest();
                        startZone = zone;
                        break;
                    case "feature":
                        if (prop.Value.ValueKind != JsonValueKind.String) return BadRequest();
                        feature = prop.Value.GetString();
                        if (!WorldAtlasMcpSnapshot.IsFeature(feature)) return BadRequest();
                        break;
                    default:
                        return Reply("INVALID_REQUEST", "Unknown JSON property.");
                }
            }

            if (operation is null) return BadRequest();
            var allowed = operation switch
            {
                "capabilities" => mapId is null && targetMapId is null && feature is null &&
                                  !seen.Overlaps(["offset", "limit", "maxHops", "startZone"]),
                "list_maps" => mapId is null && targetMapId is null && !seen.Overlaps(["maxHops", "startZone"]),
                "inspect_map" => mapId is not null && targetMapId is null && feature is null &&
                                 !seen.Overlaps(["offset", "limit", "maxHops", "startZone"]),
                "map_transitions" => mapId is not null && targetMapId is null && feature is null &&
                                     !seen.Overlaps(["maxHops", "startZone"]),
                "find_path" => mapId is not null && targetMapId is not null && feature is null &&
                               !seen.Overlaps(["offset", "limit"]),
                _ => false
            };
            if (!allowed) return BadRequest();

            if (operation == "capabilities")
                return Serialize(new
                {
                    status = "KNOWN", readOnly = true, executed = false, clientContacted = false,
                    runtimeCertified = false, schema = "world-atlas-mcp-query-v1",
                    version = snapshot?.GameVersion, provenanceRequired = true,
                    rightsRequired = true,
                    operations = new[] { "capabilities", "list_maps", "inspect_map", "map_transitions", "find_path" },
                    bounds = new { maxRequestBytes = MaxRequestBytes, maxResponseBytes = MaxResponseBytes,
                        maxPageSize = MaxPageSize, maxPathHops = MaxPathHops, maxPathExpanded = MaxPathExpanded }
                });

            if (snapshot is null)
                return Reply("UNKNOWN", "Atlas snapshot unavailable. No maps or paths are inferred.");

            return operation switch
            {
                "list_maps" => ListMaps(snapshot, offset, limit, feature),
                "inspect_map" => InspectMap(snapshot, mapId!.Value),
                "map_transitions" => Transitions(snapshot, mapId!.Value, offset, limit),
                "find_path" => FindPath(snapshot, mapId!.Value, targetMapId!.Value, maxHops, startZone),
                _ => BadRequest()
            };
        }
        catch (JsonException) { return Reply("INVALID_REQUEST", "Invalid JSON structure."); }
        catch (ArgumentException) { return Reply("INVALID_REQUEST", "Invalid JSON argument."); }
    }

    private static string ListMaps(WorldAtlasMcpSnapshot snapshot, int offset, int limit, string? feature)
    {
        var rows = snapshot.Maps.Values
            .Where(m => Visible(m, snapshot) &&
                        (feature is null || m.Features.Contains(feature, StringComparer.Ordinal)))
            .OrderBy(m => m.MapId).ToArray();
        var page = rows.Skip(offset).Take(limit).Select(m => MapView(m, snapshot)).ToArray();
        return Serialize(new
        {
            status = rows.Length == 0 ? "UNKNOWN" : "KNOWN",
            version = snapshot.GameVersion, total = rows.Length, offset, limit, returned = page.Length,
            hasMore = (long)offset + page.Length < rows.Length, maps = page,
            readOnly = true, executed = false, runtimeCertified = false,
            note = "Only explicitly disclosed local atlas records; missing maps remain UNKNOWN."
        });
    }

    private static string InspectMap(WorldAtlasMcpSnapshot snapshot, long mapId)
    {
        if (!snapshot.Maps.TryGetValue(mapId, out var map) || !Visible(map, snapshot))
            return Reply("UNKNOWN", "Map absent, unsupported or not approved for MCP disclosure.");
        return Serialize(new
        {
            status = map.Status.ToString(), version = snapshot.GameVersion,
            map = MapView(map, snapshot), readOnly = true, executed = false, runtimeCertified = false
        });
    }

    private static string Transitions(WorldAtlasMcpSnapshot snapshot, long from, int offset, int limit)
    {
        if (!KnownMap(snapshot, from))
            return Reply("UNKNOWN", "Origin map not available from approved evidence.");
        if (!snapshot.Outgoing.TryGetValue(from, out var edges) || edges.Count == 0)
            return Serialize(new
            {
                status = "UNKNOWN", mapId = from, transitions = Array.Empty<object>(),
                knownCount = 0, total = 0, readOnly = true, executed = false,
                note = "No explicitly recorded transitions; absence is not proof that exits do not exist."
            });
        var visible = edges.Where(e => Visible(e, snapshot)).OrderBy(e => e.EdgeId, StringComparer.Ordinal).ToArray();
        var page = visible.Skip(offset).Take(limit).Select(e => EdgeView(e, snapshot)).ToArray();
        return Serialize(new
        {
            status = visible.Any(e => e.Status == AtlasEvidenceStatus.VERIFIED &&
                e.Kind != AtlasTransitionKind.UNKNOWN && e.ToMapId is { } to &&
                KnownVerifiedMap(snapshot, e.FromMapId) && KnownVerifiedMap(snapshot, to))
                    ? "KNOWN" : "UNKNOWN",
            version = snapshot.GameVersion, mapId = from, total = visible.Length, offset, limit,
            returned = page.Length, hasMore = (long)offset + page.Length < visible.Length,
            transitions = page, readOnly = true, executed = false, runtimeCertified = false,
            note = "Unknown destinations/candidate edges are visible as UNKNOWN/CANDIDATE but never routable."
        });
    }

    private static string FindPath(WorldAtlasMcpSnapshot snapshot, long start, long target, int maxHops, int? startZone)
    {
        if (!KnownMap(snapshot, start) || !KnownMap(snapshot, target))
            return Reply("UNKNOWN", "Origin/destination missing or not approved for MCP disclosure.");

        if (start == target && KnownVerifiedMap(snapshot, start))
            return Serialize(new
            {
                status = "KNOWN", fromMapId = start, toMapId = target, hopCount = 0,
                edges = Array.Empty<object>(), readOnly = true, executed = false,
                runtimeCertified = false, note = "Same map only; no movement requested."
            });

        static bool Traversable(WorldAtlasMcpSnapshot atlas, AtlasTransition e) =>
            e.Status == AtlasEvidenceStatus.VERIFIED && e.Kind != AtlasTransitionKind.UNKNOWN &&
            e.Criterion is null && e.CellId is >= 0 and < 560 &&
            e.FromZone is > 0 && e.ToZone is > 0 && e.ToMapId is { } to &&
            KnownVerifiedMap(atlas, e.FromMapId) && KnownVerifiedMap(atlas, to) && Visible(e, atlas);
        var first = snapshot.Outgoing.TryGetValue(start, out var original)
            ? original.Where(e => Traversable(snapshot, e)).ToArray() : [];
        int initialZone;
        if (startZone is { } explicitlyChosen)
            initialZone = explicitlyChosen;
        else
        {
            var zones = first.Select(e => e.FromZone!.Value).Distinct().ToArray();
            if (zones.Length != 1)
                return Reply("UNKNOWN", "Start sector UNKNOWN or ambiguous. Provide a proven startZone.");
            initialZone = zones[0];
        }
        var startNode = (Map: start, Zone: initialZone);
        var queue = new Queue<(long Map, int Zone)>();
        var parents = new Dictionary<(long Map, int Zone), ((long Map, int Zone) Prev, AtlasTransition? Edge)>
            { [startNode] = (startNode, null) };
        var depths = new Dictionary<(long Map, int Zone), int> { [startNode] = 0 };
        queue.Enqueue(startNode);
        int expanded = 0;

        while (queue.Count > 0 && expanded < MaxPathExpanded)
        {
            var current = queue.Dequeue();
            expanded++;
            if (depths[current] >= maxHops || !snapshot.Outgoing.TryGetValue(current.Map, out var transitions))
                continue;
            // Deterministic BFS; ONLY explicit VERIFIED edges, with separately
            // disclosed source provenance, and known endpoints are traversable.
            foreach (var edge in transitions.OrderBy(e => e.EdgeId, StringComparer.Ordinal))
            {
                if (!Traversable(snapshot, edge) || edge.FromZone != current.Zone ||
                    edge.ToMapId is not { } targetMap)
                    continue;
                var next = (Map: targetMap, Zone: edge.ToZone!.Value);
                if (depths.ContainsKey(next)) continue;
                depths.Add(next, depths[current] + 1);
                parents.Add(next, (current, edge));
                if (next.Map == target)
                {
                    var route = new List<AtlasTransition>();
                    var cursor = next;
                    while (cursor != startNode)
                    {
                        (var prev, var traversed) = parents[cursor];
                        route.Add(traversed!);
                        cursor = prev;
                    }
                    route.Reverse();
                    return Serialize(new
                    {
                        status = "KNOWN", version = snapshot.GameVersion, fromMapId = start, toMapId = target,
                        hopCount = route.Count, edges = route.Select(e => EdgeView(e, snapshot)).ToArray(),
                        readOnly = true, executed = false, runtimeCertified = false,
                        note = "Static graph route, not game navigation or a server ACK; revalidate before action."
                    });
                }
                if (depths.Count < MaxPathExpanded) queue.Enqueue(next);
                else break;
            }
        }
        return Serialize(new
        {
            status = "UNKNOWN", version = snapshot.GameVersion, fromMapId = start, toMapId = target,
            hopCount = (int?)null, edges = Array.Empty<object>(),
            expanded = expanded, maxHops, readOnly = true, executed = false, runtimeCertified = false,
            note = "No verified route within bounds. Not an assertion of unreachable terrain."
        });
    }

    private static bool KnownMap(WorldAtlasMcpSnapshot atlas, long mapId) =>
        atlas.Maps.TryGetValue(mapId, out var map) && Visible(map, atlas);

    private static bool KnownVerifiedMap(WorldAtlasMcpSnapshot atlas, long mapId) =>
        atlas.Maps.TryGetValue(mapId, out var map) && Visible(map, atlas) &&
        map.Status == AtlasEvidenceStatus.VERIFIED;

    private static bool Visible(AtlasMap map, WorldAtlasMcpSnapshot atlas) =>
        map.Provenance.CanDisclose(atlas.GameVersion);

    private static bool Visible(AtlasTransition edge, WorldAtlasMcpSnapshot atlas) =>
        edge.Provenance.CanDisclose(atlas.GameVersion) && KnownMap(atlas, edge.FromMapId);

    private static object MapView(AtlasMap m, WorldAtlasMcpSnapshot snapshot) => new
    {
        mapId = m.MapId, x = m.X, y = m.Y, worldId = m.WorldId, layer = m.Layer, subAreaId = m.SubAreaId,
        features = m.Features, status = m.Status.ToString(),
        provenance = ProvenanceView(m.Provenance), gameVersion = snapshot.GameVersion
    };

    private static object EdgeView(AtlasTransition e, WorldAtlasMcpSnapshot snapshot)
    {
        // Missing/undisclosed destinations are NOT exposed or treated as traversable.
        bool destinationKnown = e.ToMapId is { } to && KnownMap(snapshot, to);
        string status = destinationKnown && e.Kind != AtlasTransitionKind.UNKNOWN &&
            e.ToMapId is { } dest &&
            KnownVerifiedMap(snapshot, e.FromMapId) && KnownVerifiedMap(snapshot, dest)
            ? e.Status.ToString() : "UNKNOWN";
        return new
        {
            edgeId = e.EdgeId, fromMapId = e.FromMapId,
            toMapId = destinationKnown ? e.ToMapId : null,
            kind = e.Kind.ToString(), direction = e.Direction, cellId = e.CellId,
            fromZone = e.FromZone, toZone = e.ToZone, criterion = e.Criterion, status,
            provenance = ProvenanceView(e.Provenance),
            gameVersion = snapshot.GameVersion, runtimeCertified = false
        };
    }

    private static object ProvenanceView(AtlasProvenance p) => new
    {
        sourceKind = p.SourceKind, sourceId = p.SourceId,
        gameVersion = p.GameVersion, sha256 = p.Sha256,
        authorizationEvidenceId = p.AuthorizationEvidenceId
    };

    private static bool ReadId(JsonElement value, out long id)
    {
        id = 0;
        return value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt64(out id) && id > 0;
    }

    private static bool ReadInt(JsonElement value, int min, int max, out int result)
    {
        result = 0;
        return value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt32(out result) && result >= min && result <= max;
    }

    private static string BadRequest() => Reply("INVALID_REQUEST", "Invalid or incompatible operation arguments.");

    private static string Reply(string status, string message) => Serialize(new
    {
        status, message, readOnly = true, executed = false, clientContacted = false, runtimeCertified = false
    });

    private static string Serialize<T>(T payload)
    {
        string json = JsonSerializer.Serialize(payload);
        if (Encoding.UTF8.GetByteCount(json) <= MaxResponseBytes) return json;
        // fail closed; no partial map/route JSON and no unbounded exception text
        return "{\"status\":\"UNKNOWN\",\"message\":\"Response size exceeded\",\"readOnly\":true,\"executed\":false,\"runtimeCertified\":false}";
    }
}
