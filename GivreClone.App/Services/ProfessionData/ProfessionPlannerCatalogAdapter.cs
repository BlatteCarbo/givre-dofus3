using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Planner = GivreClone.App.Services.ProfessionPlanner;

namespace GivreClone.App.Services.ProfessionData;

public sealed record ProfessionPlannerCatalogProjection(
    IReadOnlyList<Planner.ProfessionRecipe> Recipes,
    Planner.XpTable? ExperienceTable,
    IReadOnlyList<string> Warnings);

/// <summary>Conversion explicite vers les DTO du moteur du worker 2.</summary>
public static class ProfessionPlannerCatalogAdapter
{
    /// <summary>
    /// Requires an admitted catalog and caller-selected job ID.
    /// Missing level/output quantity are excluded rather than guessed.
    /// A local SHA-256 does not authorize Verified=true or XpVerified=true.
    /// </summary>
    public static ProfessionPlannerCatalogProjection ForJob(ProfessionCatalog catalog, int jobId)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (!catalog.Admitted || jobId <= 0)
            return new([], null, ["Données de métier absentes ou non admises."]);
        var warnings = new List<string>();
        var result = new List<Planner.ProfessionRecipe>();
        var ids = new HashSet<long>();
        int excluded = 0;
        foreach (var recipe in catalog.RecipesForJob(jobId))
        {
            if (recipe.RequiredLevel is not > 0 || recipe.ResultQuantity is not > 0)
            {
                excluded++;
                continue;
            }
            long stableId = StableRecipeId(catalog.Source.GameVersion, recipe.Id);
            if (!ids.Add(stableId))
                return new([], null, ["Collision d'identifiants de recettes : projection refusée."]);
            result.Add(new Planner.ProfessionRecipe(
                stableId, recipe.ResultItemId, recipe.ResultQuantity.Value,
                recipe.Ingredients.Select(i => new Planner.RecipeIngredient(i.ItemId, i.Quantity)).ToArray(),
                recipe.RequiredLevel.Value, recipe.JobId, recipe.ExperiencePerCraft,
                XpVerified: false, SourceVersion: catalog.Source.GameVersion, Verified: false));
        }
        if (excluded > 0)
            warnings.Add($"{excluded} recette(s) sans niveau ou nombre d'objets produits attesté(s) : projection exclue.");
        if (result.Count > 2_000)
            return new([], null, ["Plus de 2 000 recettes du métier : moteur borné, projection refusée."]);
        var thresholds = catalog.LevelThresholds.Where(t => t.JobId == jobId)
            .Select(t => new Planner.XpThreshold(t.Level, t.CumulativeExperience)).ToArray();
        Planner.XpTable? xp = thresholds.Length == 0 ? null :
            new Planner.XpTable(catalog.Source.GameVersion, Verified: false, thresholds);
        warnings.Add("Empreinte locale validée ; version, provenance éditeur, XP et jeu en direct non attestés.");
        return new(result.ToArray(), xp, warnings.ToArray());
    }

    /// <summary>Stable across runs; no numeric recipe ID invented from its result item ID.</summary>
    private static long StableRecipeId(string version, string sourceId)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(version + "\n" + sourceId));
        long id = BinaryPrimitives.ReadInt64LittleEndian(digest.AsSpan(0, 8)) & long.MaxValue;
        return id == 0 ? 1 : id;
    }
}
