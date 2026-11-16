using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.ProfessionData;

/// <summary>Provenance applies to local bytes, not independent publisher or gameplay attestation.</summary>
public sealed record ProfessionSource(
    string GameVersion, string SourceLabel, string PermissionClaim,
    string FileSha256, string AssetFormat, bool LocalHashVerified,
    bool PublisherVerified = false);

/// <param name="IsPlaceholder">Only an ingredient/result ID was present; item metadata is UNKNOWN.</param>
public sealed record ProfessionItem(long Id, string? Name, int? Level, bool IsPlaceholder = false);
public sealed record ProfessionJob(int Id, string? Name);
public sealed record ProfessionIngredient(long ItemId, int Quantity);
public sealed record ProfessionLevelThreshold(int JobId, int Level, long CumulativeExperience);

/// <param name="ResultQuantity">Null when the source has no evidenced output count.</param>
/// <param name="ExperiencePerCraft">Null means UNKNOWN; item craftXpRatio and resultLevel are not XP.</param>
public sealed record ProfessionRecipe(
    string Id, int JobId, long ResultItemId, int? RequiredLevel,
    int? ResultQuantity, long? ExperiencePerCraft,
    IReadOnlyList<ProfessionIngredient> Ingredients);

public sealed class ProfessionCatalog
{
    public ProfessionSource Source { get; }
    public IReadOnlyList<ProfessionItem> Items { get; }
    public IReadOnlyList<ProfessionJob> Jobs { get; }
    public IReadOnlyList<ProfessionRecipe> Recipes { get; }
    public IReadOnlyList<ProfessionLevelThreshold> LevelThresholds { get; }
    public IReadOnlyList<string> Issues { get; }
    public bool Admitted { get; }
    public bool HasVerifiedGameExperience => false;

    internal ProfessionCatalog(ProfessionSource source,
        IReadOnlyList<ProfessionItem> items, IReadOnlyList<ProfessionJob> jobs,
        IReadOnlyList<ProfessionRecipe> recipes,
        IReadOnlyList<ProfessionLevelThreshold> thresholds,
        IReadOnlyList<string> issues, bool admitted)
    {
        Source = source;
        Items = Array.AsReadOnly(items.ToArray());
        Jobs = Array.AsReadOnly(jobs.ToArray());
        Recipes = Array.AsReadOnly(recipes.ToArray());
        LevelThresholds = Array.AsReadOnly(thresholds.ToArray());
        Issues = Array.AsReadOnly(issues.ToArray());
        Admitted = admitted;
    }

    /// <summary>Deterministic read-only data for a profession planner; does not choose routes or prices.</summary>
    public IReadOnlyList<ProfessionRecipe> RecipesForJob(int jobId, int? maximumLevel = null) =>
        !Admitted || jobId <= 0 ? Array.Empty<ProfessionRecipe>() :
        Recipes.Where(r => r.JobId == jobId &&
            (maximumLevel is null || r.RequiredLevel is not null && r.RequiredLevel <= maximumLevel))
            .OrderBy(r => r.RequiredLevel ?? int.MaxValue)
            .ThenBy(r => r.ResultItemId)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToArray();

    public ProfessionItem? FindItem(long id) => Admitted ? Items.FirstOrDefault(i => i.Id == id) : null;

    internal static ProfessionCatalog Rejected(string reason, string? version = null) =>
        new(new ProfessionSource(version ?? "", "", "", "", "", false), [], [], [], [], [reason], false);
}
