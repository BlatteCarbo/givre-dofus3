using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Services.DofusDbProfessions;

public static partial class DofusDbProfessionReader
{
    /// <summary>Local UI only. No game-version or rights attestation; never used as MCP result.</summary>
    public static IReadOnlyList<ProfessionCatalogRecipe> ListRecipeCards(
        DofusDbProfessionReview review, string? jobName, int max = 200)
    {
        if (review is null || !review.AdmittedForLocalReview || string.IsNullOrWhiteSpace(jobName) ||
            max is < 1 or > 200) return [];
        var matches = review.Jobs.Where(j =>
            string.Equals(j.Value, jobName.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) return [];
        string source = $"DofusDB · cache local · version jeu inconnue · SHA {review.SourceProofSha256[..12]}";
        return review.Recipes.Where(r => r.JobId == matches[0].Key)
            .OrderBy(r => r.RequiredLevel).ThenBy(r => r.ResultId).ThenBy(r => r.Id, StringComparer.Ordinal)
            .Take(max)
            .Select(r => new ProfessionCatalogRecipe(
                r.Id, matches[0].Value ?? "Métier inconnu",
                review.Items.TryGetValue(r.ResultId, out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name : r.ResultName ?? $"Objet GID {r.ResultId}",
                source, IsVerified: false, ProductionLabel: "Rendement inconnu",
                ExperienceLabel: "XP inconnue", PriceLabel: "Prix HDV non observé")).ToArray();
    }

    /// <summary>
    /// UI-only observation: direct ingredients per selected recipe (or for
    /// explicit craft count). No result yield, progression, harvest route,
    /// stock trust, XP, estimated budget, duration or playable instruction.
    /// </summary>
    public static ProfessionPlannerSnapshot BuildReviewSnapshot(
        DofusDbProfessionReview review, ProfessionPlannerGoal goal)
    {
        const string warning = "Version de jeu inconnue ; recettes en revue locale. Rendement, XP, routes, stocks et prix non certifiés.";
        var missing = new ProfessionPlannerSnapshot("Catalogue DofusDB indisponible",
            "Aucune recette certifiée utilisable pour ce projet.", "DofusDB · revue locale",
            false, "XP inconnue", "Coût inconnu", "Durée inconnue", [], warning, "UNVERIFIED");
        if (review is null || !review.AdmittedForLocalReview || goal is null ||
            goal.StartLevel is < 1 or > 200 || goal.TargetLevel is < 1 or > 200 ||
            goal.TargetLevel <= goal.StartLevel) return missing;
        var jobs = review.Jobs.Where(j =>
            string.Equals(j.Value, goal.JobName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (jobs.Length != 1) return missing;
        var selected = review.Recipes.FirstOrDefault(r => r.JobId == jobs[0].Key &&
            r.Id == goal.RecipeId && r.RequiredLevel <= goal.StartLevel);
        if (selected is null) return missing with {
            Title = $"{goal.JobName} · {goal.StartLevel} → {goal.TargetLevel}",
            Explanation = "Choisir une recette accessible au niveau actuel ; progression XP inconnue."
        };
        bool explicitCraftCount = goal.RequestedCrafts is > 0 and <= 1_000_000;
        long quantity = explicitCraftCount ? goal.RequestedCrafts!.Value : 1;
        var rows = new List<ProfessionPlannerStep>();
        string resultName = review.Items.TryGetValue(selected.ResultId, out var result)
            && !string.IsNullOrWhiteSpace(result) ? result :
            selected.ResultName ?? $"Objet GID {selected.ResultId}";
        rows.Add(new ProfessionPlannerStep(selected.ResultId.ToString(CultureInfo.InvariantCulture),
            resultName, "Recette consultative · production par fabrication inconnue",
            0, "Fabrication", explicitCraftCount ? $"{quantity:N0} fabrication(s) demandée(s)" :
            "1 fabrication (exemple)", "Stock non observé", "Rendement inconnu",
            "UNVERIFIED", "DofusDB · cache local ; version UNKNOWN", ["À déterminer"]));
        foreach (var ingredient in selected.Ingredients)
        {
            long needed = checked((long)ingredient.Quantity * quantity);
            string name = review.Items.TryGetValue(ingredient.ItemId, out var itemName) &&
                          !string.IsNullOrWhiteSpace(itemName) ? itemName : $"Objet GID {ingredient.ItemId}";
            rows.Add(new ProfessionPlannerStep(
                ingredient.ItemId.ToString(CultureInfo.InvariantCulture), name,
                explicitCraftCount ? "Ingrédient de recette × nombre de fabrications explicitement demandé" :
                    "Ingrédient requis pour une seule fabrication",
                1, "Ingrédient", $"× {needed:N0}", "Stock inconnu", "Acquisition inconnue",
                "DONNÉE SOURCE NON CERTIFIÉE", "DofusDB · cache local ; version UNKNOWN", ["À déterminer"]));
        }
        return new ProfessionPlannerSnapshot(
            $"{goal.JobName} · niv. {goal.StartLevel} → {goal.TargetLevel} · {resultName}",
            explicitCraftCount
                ? $"Ingrédients de {quantity:N0} fabrication(s) demandée(s) affichés, sans certifier le nombre d'objets produits."
                : "Ingrédients par fabrication affichés ; nombre de fabrications nécessaire pour monter de niveau inconnu.",
            $"DofusDB revue locale · version inconnue · SHA {review.SourceProofSha256[..12]}",
            false, "XP cible inconnue", "Coût inconnu", "Durée inconnue", rows,
            warning, "PLAN PARTIEL · REND/XP/ROUTES UNKNOWN");
    }

}
