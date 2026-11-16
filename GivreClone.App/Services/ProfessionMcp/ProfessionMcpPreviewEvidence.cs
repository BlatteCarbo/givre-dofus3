using GivreClone.App.Services.ProfessionPlanner;
using System.Text.RegularExpressions;

namespace GivreClone.App.Services.ProfessionMcp;

/// <summary>
/// Operator attestation is NOT independent legal or publisher verification.
/// Only an internal loader that validates a local SHA-pinned receipt may create
/// one; NEVER deserialize it from a tool request.
/// </summary>
public sealed record ProfessionMcpPartnerGrant(
    string SourceKind, string SourceSurface, string SourceGameVersion,
    string SourceSha256, string ReceiptId, DateOnly ValidUntil,
    bool AiPermitted, bool McpPermitted, bool OperatorAttested,
    bool IndependentlyVerified = false)
{
    private static readonly Regex Sha = new(@"\A[A-Fa-f0-9]{64}\z", RegexOptions.Compiled);
    private static readonly Regex Id = new(@"\Aoperator-[A-Fa-f0-9]{32}\z", RegexOptions.Compiled);
    private static readonly Regex Version = new(@"\A(?:0|[1-9][0-9]{0,8})(?:\.(?:0|[1-9][0-9]{0,8})){3}\z", RegexOptions.Compiled);
    private static readonly Regex Label = new(@"\A[A-Za-z0-9_.:-]{1,80}\z", RegexOptions.Compiled);

    public bool Allows(ProfessionMcpProvenance? source)
    {
        if (source is null || !OperatorAttested || !AiPermitted || !McpPermitted ||
            SourceSurface is not ("site" or "api") || !Sha.IsMatch(SourceSha256 ?? "") ||
            !Version.IsMatch(SourceGameVersion ?? "") ||
            !Label.IsMatch(source.SourceKind ?? "") || !Label.IsMatch(source.SourceId ?? "") ||
            !Id.IsMatch(ReceiptId ?? "") || ValidUntil < DateOnly.FromDateTime(DateTime.UtcNow))
            return false;
        // Never mask a DofusDB origin with a synthetic local-owned label.
        bool isDofusDb = Origin(source.SourceKind) || Origin(source.SourceId);
        return isDofusDb &&
            string.Equals(SourceKind, "dofusdb", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(source.GameVersion, SourceGameVersion, StringComparison.Ordinal) &&
            string.Equals(source.ManifestSha256, SourceSha256, StringComparison.OrdinalIgnoreCase) &&
            source.AiDisclosureAuthorized &&
            string.Equals(source.AuthorizationEvidenceId, ReceiptId, StringComparison.Ordinal);
    }

    private static bool Origin(string? value) =>
        new string((value ?? "").Where(char.IsAsciiLetterOrDigit).ToArray())
            .Contains("dofusdb", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One independently reviewed world-graph route; never created by coordinate adjacency.</summary>
public sealed record ProfessionMcpRouteEvidence(
    string RouteId, long ItemGid, AcquisitionMethod Method,
    IReadOnlyList<long> MapIds, IReadOnlyList<string> EdgeIds,
    string GameVersion, string SourceKind, string SourceId, string SourceSha256,
    bool SourceVerified, bool RightsGranted, string? AuthorizationEvidenceId);

public sealed class ProfessionMcpPreviewSnapshot
{
    public const int MaxRoutes = 512;
    public const int MaxRouteMaps = 33;
    public ProfessionMcpSnapshot Profession { get; }
    public IReadOnlyList<ProfessionMcpRouteEvidence> Routes { get; }
    public ProfessionMcpPartnerGrant? PartnerGrant { get; }

    public ProfessionMcpPreviewSnapshot(ProfessionMcpSnapshot profession,
        IReadOnlyCollection<ProfessionMcpRouteEvidence>? routes = null,
        ProfessionMcpPartnerGrant? partnerGrant = null)
    {
        Profession = profession ?? throw new ArgumentNullException(nameof(profession));
        PartnerGrant = partnerGrant;
        routes ??= Array.Empty<ProfessionMcpRouteEvidence>();
        if (routes.Count > MaxRoutes || routes.Any(r => r is null ||
            r.MapIds is null || r.EdgeIds is null ||
            r.MapIds.Count is < 2 or > MaxRouteMaps || r.EdgeIds.Count != r.MapIds.Count - 1))
            throw new ArgumentException("Route evidence exceeds safety bounds.");
        Routes = Array.AsReadOnly(routes.Select(r => r with
        {
            MapIds = Array.AsReadOnly(r.MapIds.ToArray()),
            EdgeIds = Array.AsReadOnly(r.EdgeIds.ToArray())
        }).ToArray());
    }

    public bool CanInspect()
    {
        var p = Profession.Provenance;
        if (!(Profession.CanPlan() || PartnerGrant?.Allows(p) == true)) return false;
        var r = Profession.Request;
        return r.Recipes.All(x => x.SourceVersion == p.GameVersion) &&
            r.Sources.All(x => x.SourceVersion == p.GameVersion) &&
            (r.XpTable is null || r.XpTable.Version == p.GameVersion);
    }

    public bool IsRouteAdmitted(ProfessionMcpRouteEvidence r)
    {
        var p = Profession.Provenance;
        if (!CanInspect() || !r.SourceVerified || !r.RightsGranted ||
            r.ItemGid <= 0 || r.Method == AcquisitionMethod.Craft ||
            r.MapIds.Count is < 2 or > MaxRouteMaps ||
            r.MapIds.Any(map => map <= 0) ||
            r.EdgeIds.Count != r.MapIds.Count - 1 ||
            r.EdgeIds.Any(id => !SafeLabel(id)) ||
            !SafeLabel(r.RouteId) || !SafeLabel(r.SourceId) ||
            r.GameVersion != p.GameVersion ||
            r.AuthorizationEvidenceId is null || !SafeLabel(r.AuthorizationEvidenceId) ||
            r.SourceSha256 is not { Length: 64 } sha || !sha.All(Uri.IsHexDigit))
            return false;
        bool partnerOrigin = Origin(r.SourceKind);
        return partnerOrigin
            ? PartnerGrant?.Allows(p) == true &&
              r.AuthorizationEvidenceId == PartnerGrant.ReceiptId &&
              string.Equals(sha, PartnerGrant.SourceSha256, StringComparison.OrdinalIgnoreCase)
            : SafeLabel(r.SourceKind);
    }

    private static bool Origin(string? source) =>
        new string((source ?? "").Where(char.IsAsciiLetterOrDigit).ToArray())
            .Contains("dofusdb", StringComparison.OrdinalIgnoreCase);
    private static bool SafeLabel(string? value) =>
        value is { Length: >= 1 and <= 80 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or ':' or '-' or '_');
}
