using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using GivreClone.App.Services.ProfessionPlanner;

namespace GivreClone.App.Services.ProfessionMcp;

/// <summary>
/// Evidence must be supplied by a separately reviewed, locally owned snapshot.
/// A caller-supplied flag or an unreviewed third-party manifest is not proof of AI rights.
/// DofusDB-derived records remain excluded pending explicit AI redistribution rights.
/// </summary>
public sealed record ProfessionMcpProvenance(
    string SourceKind, string SourceId, string GameVersion, string ManifestSha256,
    bool AiDisclosureAuthorized = false, string? AuthorizationEvidenceId = null)
{
    private static readonly Regex Label = new(@"\A[A-Za-z0-9_.:-]{1,80}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Digest = new(@"\A[A-Fa-f0-9]{64}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Version = new(@"\A(?:0|[1-9][0-9]{0,8})(?:\.(?:0|[1-9][0-9]{0,8})){3}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public bool CanDisclose() =>
        AiDisclosureAuthorized && Version.IsMatch(GameVersion ?? "") &&
        Label.IsMatch(SourceKind ?? "") && Label.IsMatch(SourceId ?? "") &&
        Label.IsMatch(AuthorizationEvidenceId ?? "") && Digest.IsMatch(ManifestSha256 ?? "") &&
        !IsForbiddenOrigin(SourceKind) && !IsForbiddenOrigin(SourceId);

    private static bool IsForbiddenOrigin(string? value)
    {
        string normalized = new((value ?? "").Where(char.IsAsciiLetterOrDigit).ToArray());
        return normalized.Contains("dofusdb", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Immutable, bounded local input to ProfessionPlannerEngine.Plan. The integrator must
/// verify the manifest, exact game version and AI rights for ALL records BEFORE
/// constructing this snapshot. No file, game client, or network access occurs here.
/// </summary>
public sealed class ProfessionMcpSnapshot
{
    public const int MaxRecipes = 2000;
    public const int MaxInventoryStacks = 10000;
    public const int MaxAcquisitionSources = 10000;
    public ProfessionMcpProvenance Provenance { get; }
    public ProfessionPlanRequest Request { get; }

    public ProfessionMcpSnapshot(ProfessionPlanRequest request, ProfessionMcpProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(request.Goal);
        ArgumentNullException.ThrowIfNull(request.Recipes);
        ArgumentNullException.ThrowIfNull(request.Inventory);
        ArgumentNullException.ThrowIfNull(request.Sources);
        if (request.Recipes.Count > MaxRecipes || request.Inventory.Count > MaxInventoryStacks ||
            request.Sources.Count > MaxAcquisitionSources)
            throw new ArgumentException("Local profession snapshot exceeds permitted volumes.");
        if (request.Recipes.Any(r => r is null || r.Ingredients is null || r.Ingredients.Count > 64) ||
            request.Inventory.Any(s => s is null) || request.Sources.Any(s => s is null) ||
            request.XpTable is { Thresholds: null } ||
            request.XpTable?.Thresholds.Count > 201)
            throw new ArgumentException("Malformed profession snapshot.");

        Provenance = provenance;
        var recipes = request.Recipes.Select(r => r with
        {
            Ingredients = Array.AsReadOnly(r.Ingredients.ToArray())
        }).ToArray();
        var inventory = request.Inventory.ToArray();
        var sources = request.Sources.ToArray();
        var xp = request.XpTable is { } table ? table with
        {
            Thresholds = Array.AsReadOnly(table.Thresholds.ToArray())
        } : null;
        Request = request with
        {
            Recipes = Array.AsReadOnly(recipes),
            Inventory = Array.AsReadOnly(inventory),
            Sources = Array.AsReadOnly(sources),
            XpTable = xp,
            RecipeChoices = request.RecipeChoices is null ? null :
                new ReadOnlyDictionary<long, long>(new Dictionary<long, long>(request.RecipeChoices)),
            MethodChoices = request.MethodChoices is null ? null :
                new ReadOnlyDictionary<long, AcquisitionMethod>(
                    new Dictionary<long, AcquisitionMethod>(request.MethodChoices))
        };
    }

    /// <summary>Never admit a partly unlicensed catalogue or mismatched game version.</summary>
    public bool CanPlan()
    {
        if (!Provenance.CanDisclose()) return false;
        string version = Provenance.GameVersion;
        // A version label supplied by upstream data is not a grant of rights;
        // independently verified, single-origin input is required as well.
        if (Request.Recipes.Any(r => !string.Equals(r.SourceVersion, version, StringComparison.Ordinal)) ||
            Request.Sources.Any(s => !string.Equals(s.SourceVersion, version, StringComparison.Ordinal)) ||
            Request.XpTable is { } xp && !string.Equals(xp.Version, version, StringComparison.Ordinal))
            return false;
        return true;
    }
}
