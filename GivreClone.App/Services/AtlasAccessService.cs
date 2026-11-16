using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GivreClone.App.Services.WorldAtlas;
using GivreClone.App.Services.WorldAtlasMcp;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Services;

/// <summary>
/// Bridges local, read-only atlas observations to the UI and the MCP read-only facade.
/// The third-party partner export is deliberately EXCLUDED from AI/MCP disclosure until
/// the partner's separate AI-use rights have been reviewed. No network/game access.
/// </summary>
public static class AtlasAccessService
{
    public static AtlasImportResult? LoadLocalAtlas()
    {
        string? version = Environment.GetEnvironmentVariable("GIVRE_ATLAS_VERSION") ?? InstalledVersion();
        string? review = Environment.GetEnvironmentVariable("GIVRE_ATLAS_REVIEW_DIR");
        if (!string.IsNullOrWhiteSpace(review))
            return WorldAtlasReader.ReadDirectory(review, version,
                Environment.GetEnvironmentVariable("GIVRE_ATLAS_MANIFEST_SHA256"));

        if (version is null) return null;
        string data = Path.Combine(AppContext.BaseDirectory, "data");
        if (File.Exists(Path.Combine(data, "atlas-manifest.json")))
            return WorldAtlasReader.ReadDirectory(data, version,
                Environment.GetEnvironmentVariable("GIVRE_ATLAS_MANIFEST_SHA256"));
        return WorldAtlasReader.ReadLegacyCacheFiles(data, version);
    }

    public static WorldAtlasSnapshot? UiSnapshot()
    {
        var result = LoadLocalAtlas();
        if (result?.Snapshot is not { } atlas) return null;
        // A pinned manifest proves file integrity, NOT ownership of third-party
        // data or that a cave/zaap transition actually worked on a live server.
        // Partner exports remain consultative (source-data legal review separate).
        var verified = atlas.TrustedManifestMatched
            && (atlas.SourceKind is "observed-transition" or "client-world-graph-reviewed");
        var rootProof = Proof(atlas.SourceId, atlas.GameVersion, atlas.SourceHash, verified,
            "Snapshot local (manifeste et empreintes en lecture seule)");
        var maps = atlas.Maps.Values.Select(m =>
        {
            var layer = m.Features.Contains("mine") ? WorldAtlasLayer.Mine
                : m.Features.Contains("cave") || m.Features.Contains("cave_entrance") ? WorldAtlasLayer.Cave
                : m.Features.Contains("zaap") ? WorldAtlasLayer.Zaap
                : m.Features.Contains("surface") ? WorldAtlasLayer.Surface : WorldAtlasLayer.Unknown;
            var mapProof = Proof(atlas.SourceId, atlas.GameVersion, atlas.SourceHash,
                verified && m.Status == "verified", "MapId issu du snapshot local");
            return new WorldAtlasMapRecord(m.MapId, m.X, m.Y,
                m.SubAreaId is { } sub ? $"Sous-zone {sub}" : "Zone non renseignée",
                layer, $"Carte {m.MapId}", mapProof);
        }).ToArray();
        var edges = atlas.Transitions.Select(e => new WorldAtlasTransitionRecord(
            e.FromMapId, e.ToMapId,
            $"{e.Kind} · cellule {(e.CellId is { } cell ? cell.ToString() : "UNKNOWN")}" +
            (e.Criterion is null ? "" : " · condition non vérifiée"),
            Proof(e.SourceId, e.SourceVersion, e.SourceHash,
                verified && e.Status == "verified", $"Transition source {e.EdgeId}"))).ToArray();
        return new WorldAtlasSnapshot($"Atlas local · Dofus {atlas.GameVersion}", rootProof, maps, edges);
    }

    /// <summary>Only source-owned local cache material may be disclosed in MCP, and
    /// only while the user has enabled MCP. Partner exports never cross this boundary.</summary>
    public static WorldAtlasMcpSnapshot? McpSnapshot()
    {
        if (!AppConfig.Current.McpEnabled) return null;
        // Only the app's own unpinned client cache is in scope for this local
        // read-only bridge. Partner/manifest imports, even if mislabelled, are excluded.
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GIVRE_ATLAS_REVIEW_DIR"))
            || File.Exists(Path.Combine(AppContext.BaseDirectory, "data", "atlas-manifest.json")))
            return null;
        var data = LoadLocalAtlas()?.Snapshot;
        if (data is null || data.SourceKind != "local-client-cache"
            || data.SourceId != "local-unpinned-cache" || data.TrustedManifestMatched) return null;
        var provenance = new WorldAtlasMcp.AtlasProvenance("local-client-cache",
            "local-unpinned-cache", data.GameVersion, data.SourceHash,
            McpDisclosureAuthorized: true, AuthorizationEvidenceId: "local-user-enabled-mcp");
        var maps = data.Maps.Values.Select(m => new WorldAtlasMcp.AtlasMap(m.MapId, m.X, m.Y,
            m.Layer, m.SubAreaId, m.Features.Where(WorldAtlasMcpSnapshot.IsFeature).ToArray(),
            m.Status == "verified" && data.TrustedManifestMatched
                ? AtlasEvidenceStatus.VERIFIED : AtlasEvidenceStatus.CANDIDATE, provenance)
            { WorldId = m.WorldId }).ToArray();
        var edges = data.Transitions
            .Where(e => e.Criterion is null && e.EdgeId.Length <= 80 && e.EdgeId.All(c =>
                char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-' or ':'))
            .Select(e => new WorldAtlasMcp.AtlasTransition(e.EdgeId, e.FromMapId, e.ToMapId,
                e.Kind switch
                {
                    "walk" => AtlasTransitionKind.WALK, "door" => AtlasTransitionKind.DOOR,
                    "cave" => AtlasTransitionKind.CAVE, "zaap" => AtlasTransitionKind.ZAAP,
                    _ => AtlasTransitionKind.UNKNOWN
                },
                e.Direction switch
                {
                    0 => "east", 2 => "south", 4 => "west", 6 => "north", _ => null
                },
                e.Status == "verified" && data.TrustedManifestMatched
                    ? AtlasEvidenceStatus.VERIFIED : AtlasEvidenceStatus.CANDIDATE, provenance)
            {
                CellId = e.CellId, FromZone = e.FromZone, ToZone = e.ToZone,
                Criterion = null
            }).ToArray();
        try { return new WorldAtlasMcpSnapshot(data.GameVersion, maps, edges); }
        catch (ArgumentException) { return null; }
    }

    private static WorldAtlasEvidence Proof(string source, string version, string hash,
        bool verified, string reference) => new(
        verified ? WorldAtlasEvidenceState.Verified : WorldAtlasEvidenceState.Unverified,
        source, version, hash, reference);

    private static string? InstalledVersion()
    {
        try
        {
            var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Ankama", "Dofus-dofus3", "Dofus_Data", "StreamingAssets", "version");
            if (!File.Exists(p) || new FileInfo(p).Length is < 8 or > 4096) return null;
            var first = File.ReadLines(p).FirstOrDefault();
            return first?.StartsWith("Version=", StringComparison.Ordinal) == true
                ? first["Version=".Length..].Trim() : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
