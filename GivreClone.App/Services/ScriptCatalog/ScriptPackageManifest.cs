using System.Text.Json.Serialization;

namespace GivreClone.App.Services.ScriptCatalog;

/// <summary>
/// Describes one local Lua package. The manifest is metadata only; reading it never runs code.
/// Each package occupies catalogRoot/id/manifest.json and references a contained UTF-8 .lua file.
/// </summary>
public sealed class ScriptPackageManifest
{
    public int SchemaVersion { get; init; }
    public string Id { get; init; } = "";
    public string Version { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public string EntryPoint { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string ApiVersion { get; init; } = "";
    public string[] Capabilities { get; init; } = [];
}

public enum ScriptReviewStatus
{
    Invalid,
    Blocked,
    NeedsPermissionReview,
    VerifiedForPreview
}

public enum ScriptCapabilityRisk
{
    Observation,
    RequiresGrant,
    Denied
}

public sealed record ScriptCapabilityReview(string Name, ScriptCapabilityRisk Risk, string Explanation);

public sealed record ScriptReviewReport(
    string PackageId,
    ScriptReviewStatus Status,
    ScriptPackageManifest? Manifest,
    IReadOnlyList<ScriptCapabilityReview> Capabilities,
    IReadOnlyList<string> Findings,
    string? Preview)
{
    /// <summary>Never authorizes execution. There is intentionally no Run or AutoLoad API.</summary>
    [JsonIgnore]
    public bool CanExecute => false;

    [JsonIgnore]
    public bool CanPreview => Status is ScriptReviewStatus.VerifiedForPreview or ScriptReviewStatus.NeedsPermissionReview;
}
