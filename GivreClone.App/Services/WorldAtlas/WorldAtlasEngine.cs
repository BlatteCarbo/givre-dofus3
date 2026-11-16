using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace GivreClone.App.Services.WorldAtlas;

/// <summary>
/// Stable DTOs for read-only UI/MCP integration. Null means NOT OBSERVED, not zero.
/// A map's worldId/layer/subarea and coordinates are independent dimensions:
/// same (x,y) is never proof of a traversable underground passage.
/// </summary>
public sealed record AtlasMap(long MapId, int? X, int? Y, int? WorldId,
    int? Layer, int? SubAreaId, IReadOnlyList<string> Features, string Status);

/// <summary>
/// Transition descriptors always identify both ends; direction/cell/zone may be unknown.
/// SourceHash is the SHA-256 of the edge input file, not a server ACK.
/// 'verified' describes a separately pinned provenance and an observed edge record;
/// it does not guarantee future in-game usability.
/// </summary>
public sealed record AtlasTransition(string EdgeId, long FromMapId, long ToMapId,
    string Kind, int? Direction, int? CellId, int? FromZone, int? ToZone,
    string? Criterion, string SourceId, string SourceKind, string SourceVersion,
    string SourceHash, string Status);

public sealed class AtlasSnapshot
{
    public string GameVersion { get; }
    public string SourceId { get; }
    public string SourceKind { get; }
    public string SourceHash { get; }
    public bool TrustedManifestMatched { get; }
    public IReadOnlyDictionary<long, AtlasMap> Maps { get; }
    public IReadOnlyList<AtlasTransition> Transitions { get; }

    internal AtlasSnapshot(string gameVersion, string sourceId, string sourceKind,
        string sourceHash, bool trustedManifestMatched,
        Dictionary<long, AtlasMap> maps, List<AtlasTransition> transitions)
    {
        GameVersion = gameVersion;
        SourceId = sourceId;
        SourceKind = sourceKind;
        SourceHash = sourceHash;
        TrustedManifestMatched = trustedManifestMatched;
        Maps = new ReadOnlyDictionary<long, AtlasMap>(maps);
        Transitions = transitions.AsReadOnly();
    }
}

public sealed record AtlasImportResult(string Status, AtlasSnapshot? Snapshot,
    IReadOnlyList<string> Issues)
{
    public bool RuntimeImportAuthorized => false;
}

public sealed record AtlasRoute(string Status, IReadOnlyList<AtlasTransition> Steps, string Reason)
{
    public bool CanExecuteAutomatically => false;
}

/// <summary>Pure, bounded graph operations. No network, Dofus client, script or packet.</summary>
public static class WorldAtlasEngine
{
    public static AtlasMap? GetMap(AtlasSnapshot? snapshot, long mapId)
        => snapshot is not null && snapshot.Maps.TryGetValue(mapId, out var map) ? map : null;

    public static IReadOnlyList<AtlasTransition> GetTransitions(AtlasSnapshot? snapshot, long mapId,
        bool includeUnverified = true, int limit = 128)
    {
        if (snapshot is null || mapId <= 0 || limit is < 1 or > 512)
            return [];
        return snapshot.Transitions.Where(e => e.FromMapId == mapId
                && (includeUnverified || e.Status == "verified"))
            .OrderBy(e => e.EdgeId, StringComparer.Ordinal).Take(limit).ToArray();
    }

    public static IReadOnlyList<AtlasMap> FindMaps(AtlasSnapshot? snapshot,
        int? x = null, int? y = null, int? worldId = null, int? layer = null,
        int? subAreaId = null, string? feature = null, int limit = 128)
    {
        if (snapshot is null || limit is < 1 or > 512) return [];
        var records = snapshot.Maps.Values.AsEnumerable();
        if (x is not null) records = records.Where(m => m.X == x);
        if (y is not null) records = records.Where(m => m.Y == y);
        if (worldId is not null) records = records.Where(m => m.WorldId == worldId);
        if (layer is not null) records = records.Where(m => m.Layer == layer);
        if (subAreaId is not null) records = records.Where(m => m.SubAreaId == subAreaId);
        if (feature is not null)
        {
            if (feature.Length is < 1 or > 48) return [];
            records = records.Where(m => m.Features.Contains(feature, StringComparer.Ordinal));
        }
        return records.OrderBy(m => m.MapId).Take(limit).ToArray();
    }

    /// <summary>
    /// A route uses ONLY explicitly verified, condition-free edges with known source
    /// and destination maps, positive zones, and a consistent zone chain. This is
    /// deliberately stricter than the live WorldGraph service. For an unknown
    /// initial zone on a multi-sector map, routing refuses to guess.
    /// A found path is an offline suggestion, never authorization to travel.
    /// </summary>
    public static AtlasRoute FindRoute(AtlasSnapshot? snapshot,
        long fromMapId, long toMapId, int maxHops = 64, int? startZone = null)
    {
        if (snapshot is null || fromMapId <= 0 || toMapId <= 0
            || maxHops is < 1 or > 128 || startZone is <= 0)
            return Unverified("Invalid route request or missing snapshot.");
        if (GetMap(snapshot, fromMapId) is not { Status: "verified" }
            || GetMap(snapshot, toMapId) is not { Status: "verified" })
            return Unverified("Start or destination map has no verified identity.");
        if (fromMapId == toMapId)
            return new AtlasRoute("SAME_MAP", [], "Identical mapId, not a cross-map path.");
        if (!snapshot.TrustedManifestMatched)
            return Unverified("Snapshot has no caller-pinned manifest identity.");

        var eligible = snapshot.Transitions.Where(e => e.Status == "verified"
            && e.Criterion is null && e.FromZone is > 0 && e.ToZone is > 0
            && e.Kind is "walk" or "door" or "stair" or "cave" or "zaap"
            && snapshot.Maps.TryGetValue(e.FromMapId, out var a) && a.Status == "verified"
            && snapshot.Maps.TryGetValue(e.ToMapId, out var b) && b.Status == "verified")
            .GroupBy(e => e.FromMapId)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.EdgeId, StringComparer.Ordinal).ToArray());
        if (!eligible.TryGetValue(fromMapId, out var starts) || starts.Length == 0)
            return Unverified("No independently verified outgoing edge from start map.");
        int initialZone;
        if (startZone is { } provided)
            initialZone = provided;
        else
        {
            var zones = starts.Select(e => e.FromZone!.Value).Distinct().ToArray();
            if (zones.Length != 1)
                return Unverified("Ambiguous start zone: current cell/sector required.");
            initialZone = zones[0];
        }

        // Vertex identity includes the server-side sector to prevent crossing
        // disconnected parts of a multi-zone map through a fictitious shortcut.
        var queue = new Queue<(long Map, int Zone, int Depth)>();
        var start = (Map: fromMapId, Zone: initialZone);
        var parent = new Dictionary<(long Map, int Zone),
            ((long Map, int Zone) Previous, AtlasTransition? Edge)> { [start] = (start, null) };
        queue.Enqueue((fromMapId, initialZone, 0));
        while (queue.Count != 0)
        {
            var (map, zone, depth) = queue.Dequeue();
            if (depth >= maxHops || !eligible.TryGetValue(map, out var outgoing)) continue;
            foreach (var edge in outgoing)
            {
                if (edge.FromZone != zone) continue;
                var next = (Map: edge.ToMapId, Zone: edge.ToZone!.Value);
                if (parent.ContainsKey(next)) continue;
                parent[next] = ((map, zone), edge);
                if (edge.ToMapId == toMapId)
                {
                    var steps = new List<AtlasTransition>();
                    for (var pos = next; parent[pos].Edge is { } step; pos = parent[pos].Previous)
                        steps.Add(step);
                    steps.Reverse();
                    return new AtlasRoute("VERIFIED_SOURCE_PATH", steps,
                        "Pinned offline edge evidence only; live traversal/permissions still unverified.");
                }
                queue.Enqueue((next.Map, next.Zone, depth + 1));
            }
        }
        return Unverified("No fully proven conditional-free zone-compatible path.");
    }

    private static AtlasRoute Unverified(string reason)
        => new("UNVERIFIED", [], reason);
}
