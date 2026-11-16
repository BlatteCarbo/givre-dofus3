using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GivreClone.App.Services.DofusDbProfessions;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Services;

/// <summary>Optional offline consultation of the partner cache in the métiers UI.
/// All recipe quantities, XP, HDV prices and game-version proofs remain unknown
/// unless independently evidenced. No game actions or MCP disclosure.</summary>
internal static class DofusDbProfessionsUiBridge
{
    private static readonly object Gate = new();
    private static Task? _loading;
    private static DofusDbProfessionReview? _review;

    public static DofusDbProfessionReview? Review
    {
        get { lock (Gate) return _review; }
    }

    public static Task StartLoadAsync()
    {
        lock (Gate)
        {
            if (_loading is not null) return _loading;
            if (Environment.GetEnvironmentVariable("GIVRE_DOFUSDB_OPERATOR_REVIEW") != "1")
                return Task.CompletedTask;
            var dir = DofusDbReviewDiscovery.FindDefaultCache();
            if (dir is null) return Task.CompletedTask;
            _loading = Task.Run(() =>
            {
                try
                {
                    var loaded = DofusDbProfessionReader.LoadReview(dir);
                    if (loaded.AdmittedForLocalReview)
                        lock (Gate) _review = loaded;
                }
                catch (Exception)
                {
                    // Optional UI preview. A failed or inconsistent import is
                    // not promoted into recipes, MCP or gameplay.
                }
            });
            return _loading;
        }
    }

    public static IReadOnlyList<ProfessionCatalogRecipe> RecipeCards(string? job) =>
        Review is { AdmittedForLocalReview: true } captured
            ? DofusDbProfessionReader.ListRecipeCards(captured, job, 200)
            : Array.Empty<ProfessionCatalogRecipe>();

    public static ProfessionPlannerSnapshot? BuildReviewPlan(ProfessionPlannerGoal goal) =>
        Review is { AdmittedForLocalReview: true } captured
            ? DofusDbProfessionReader.BuildReviewSnapshot(captured, goal)
            : null;
}
