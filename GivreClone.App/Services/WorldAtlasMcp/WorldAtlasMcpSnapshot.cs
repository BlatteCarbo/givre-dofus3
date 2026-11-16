using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace GivreClone.App.Services.WorldAtlasMcp;

/// <summary>
/// Static, offline, source-backed facts only; not a Dofus process or MCP transport.
/// Integration with WorldAtlas must validate the input and its licensing independently.
/// </summary>
public enum AtlasEvidenceStatus { UNKNOWN, CANDIDATE, VERIFIED }
public enum AtlasTransitionKind { UNKNOWN, WALK, CAVE, DOOR, ZAAP }

/// <summary>
/// McpDisclosureAuthorized is false by default. In particular a DofusDB source
/// must not be surfaced to an AI/MCP caller until partner authorization is proven.
/// No source file paths, URLs, credentials or raw payloads are exposed.
/// </summary>
public sealed record AtlasProvenance(
    string SourceKind, string SourceId, string GameVersion, string Sha256,
    bool McpDisclosureAuthorized = false, string? AuthorizationEvidenceId = null)
{
    private static readonly Regex SafeLabel = new(@"\A[A-Za-z0-9_.:-]{1,80}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SafeDigest = new(@"\A[A-Fa-f0-9]{64}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    public bool CanDisclose(string snapshotVersion) =>
        McpDisclosureAuthorized &&
        // Explicit temporary hold: public DofusDB pages do not establish AI/MCP
        // redistribution rights. Prime may replace this only after legal review.
        !string.Equals((SourceKind ?? "").Replace(".", "", StringComparison.Ordinal)
                .Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal),
            "dofusdb", StringComparison.OrdinalIgnoreCase) &&
        SafeLabel.IsMatch(SourceKind ?? "") &&
        SafeLabel.IsMatch(SourceId ?? "") &&
        SafeLabel.IsMatch(AuthorizationEvidenceId ?? "") &&
        SafeDigest.IsMatch(Sha256 ?? "") &&
        string.Equals(snapshotVersion, GameVersion, StringComparison.Ordinal);
}

public sealed record AtlasMap(long MapId, int? X, int? Y, int? Layer, int? SubAreaId,
    IReadOnlyList<string> Features, AtlasEvidenceStatus Status, AtlasProvenance Provenance)
{
    public int? WorldId { get; init; }
}

/// <summary>
/// Unknown ToMapId is deliberately nullable; callers MUST NOT guess cave
/// destinations or infer coordinate adjacency as a verified transition.
/// </summary>
public sealed record AtlasTransition(string EdgeId, long FromMapId, long? ToMapId,
    AtlasTransitionKind Kind, string? Direction, AtlasEvidenceStatus Status,
    AtlasProvenance Provenance)
{
    public int? CellId { get; init; }
    public int? FromZone { get; init; }
    public int? ToZone { get; init; }
    public string? Criterion { get; init; }
}

public sealed class WorldAtlasMcpSnapshot
{
    public const int MaxMaps = 25000;
    public const int MaxTransitions = 100000;
    public const int MaxOutgoingPerMap = 256;
    public string GameVersion { get; }
    public IReadOnlyDictionary<long, AtlasMap> Maps { get; }
    public IReadOnlyDictionary<long, IReadOnlyList<AtlasTransition>> Outgoing { get; }

    /// <summary>
    /// Create from independently verified and rights-reviewed WorldAtlas records.
    /// The façade never reads disk and never promotes CANDIDATE to VERIFIED.
    /// </summary>
    public WorldAtlasMcpSnapshot(string gameVersion, IReadOnlyCollection<AtlasMap> maps,
        IReadOnlyCollection<AtlasTransition> transitions)
    {
        if (!IsVersion(gameVersion)) throw new ArgumentException("Exact Dofus version required.");
        if (maps is null || transitions is null || maps.Count > MaxMaps || transitions.Count > MaxTransitions)
            throw new ArgumentException("Atlas absent or larger than safety caps.");
        GameVersion = gameVersion;
        var indexed = new Dictionary<long, AtlasMap>(maps.Count);
        foreach (AtlasMap item in maps)
        {
            if (item is null || item.MapId <= 0 || item.Provenance is null ||
                !Enum.IsDefined(item.Status) ||
                item.Features is null || item.Features.Count > 24 ||
                item.Features.Any(x => x is null || x.Length > 24 || !IsFeature(x)) ||
                (item.Layer is < -10000 or > 10000) ||
                (item.X is < -100000 or > 100000) || (item.Y is < -100000 or > 100000) ||
                item.WorldId is < 0 ||
                item.SubAreaId is < 0 || !indexed.TryAdd(item.MapId,
                    item with { Features = Array.AsReadOnly(item.Features.ToArray()) }))
                throw new ArgumentException("Atlas contains invalid or duplicate maps.");
        }

        var outgoing = new Dictionary<long, List<AtlasTransition>>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (AtlasTransition edge in transitions)
        {
            if (edge is null || edge.FromMapId <= 0 || edge.ToMapId is <= 0 ||
                !Enum.IsDefined(edge.Status) || !Enum.IsDefined(edge.Kind) ||
                edge.Provenance is null || !ValidEdgeId(edge.EdgeId) ||
                (edge.Direction is not null && !ValidDirection(edge.Direction)) ||
                (edge.CellId is < 0 or > 559) ||
                edge.FromZone is < 0 || edge.ToZone is < 0 ||
                (edge.Criterion is not null && !ValidEdgeId(edge.Criterion)) ||
                !ids.Add(edge.EdgeId))
                throw new ArgumentException("Invalid or duplicate atlas transition.");
            if (!outgoing.TryGetValue(edge.FromMapId, out var list))
                outgoing[edge.FromMapId] = list = [];
            if (list.Count >= MaxOutgoingPerMap)
                throw new ArgumentException("A map has too many recorded transitions.");
            list.Add(edge);
        }
        Maps = new ReadOnlyDictionary<long, AtlasMap>(indexed);
        Outgoing = new ReadOnlyDictionary<long, IReadOnlyList<AtlasTransition>>(
            outgoing.ToDictionary(k => k.Key, k => (IReadOnlyList<AtlasTransition>)k.Value.AsReadOnly()));
    }

    internal static bool IsVersion(string? version) =>
        version is { Length: >= 7 and <= 32 } &&
        Regex.IsMatch(version, @"\A(?:0|[1-9][0-9]{0,8})(?:\.(?:0|[1-9][0-9]{0,8})){3}\z");

    internal static bool ValidEdgeId(string? id) => id is { Length: >= 1 and <= 80 } &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-' or ':');

    internal static bool ValidDirection(string dir) => dir is "north" or "south" or "west" or "east" or
        "up" or "down" or "portal" or "inside" or "outside";

    internal static bool IsFeature(string? feature) => feature is
        "cave" or "cave_entrance" or "zaap" or "mine" or "resource" or "dungeon" or "portal" or "surface";
}
