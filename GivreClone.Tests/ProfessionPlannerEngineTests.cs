using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.App.Services.ProfessionPlanner;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Fiction pédagogique Bijoutier/plumes/huile/sésame. Les GID et XP sont SYNTHÉTIQUES,
/// ne représentent aucune recette ni donnée de prix attestée par Dofus.</summary>
public sealed class ProfessionPlannerEngineTests
{
    private static ProfessionGoal Goal(long? xp = 100, bool verified = true)
        => new(7, "Bijoutier (exemple fictif)", 20, 30, xp, verified);
    private static XpTable Xp() => new("fixture-synthetic-v1", true, new[]
    {
        new XpThreshold(20, 100), new XpThreshold(30, 200)
    });
    private static ProfessionRecipe Jewelry(params RecipeIngredient[] parts) =>
        new(1000, 500, 1, parts, 20, 7, 10, true, "fixture-synthetic-v1", true, 4, 0);
    private static ProfessionRecipe Oil() =>
        new(1001, 20, 1, new[] { new RecipeIngredient(30, 1) },
            5, 8, 0, true, "fixture-synthetic-v1", true, 2, 0);
    private static MaterialSource Drop(long gid, long time = 10) =>
        new(gid, AcquisitionMethod.Drop, time, 0, "fixture-synthetic-v1", true);
    private static MaterialSource Gather(long gid, long time = 5) =>
        new(gid, AcquisitionMethod.Gather, time, 0, "fixture-synthetic-v1", true);
    private static ProfessionInventoryStack Stack(string id, long gid, long qty, string origin = "bag")
        => new(id, gid, qty, origin);
    private static ProfessionPlanRequest Request(
        IReadOnlyList<ProfessionRecipe>? recipes = null,
        IReadOnlyList<ProfessionInventoryStack>? stock = null,
        IReadOnlyList<MaterialSource>? sources = null,
        long? requested = null,
        XpTable? xp = null,
        ProfessionGoal? goal = null,
        IReadOnlyDictionary<long, AcquisitionMethod>? methods = null,
        IReadOnlyDictionary<long, long>? choices = null,
        IReadOnlyDictionary<int, int>? jobs = null) =>
        new(goal ?? Goal(), 1000, requested,
            recipes ?? new[] { Jewelry(new(10, 2), new(20, 2)), Oil() },
            stock ?? Array.Empty<ProfessionInventoryStack>(),
            sources ?? new[] { Drop(10), Gather(30) }, xp ?? Xp(),
            new ProfessionPreferences(1, 1), choices, methods, jobs ?? new Dictionary<int, int> { [8] = 10 });

    [Fact]
    public void Synthetic_jeweler_20_to_30_explodes_recipes_with_exact_xp_and_partial_inventory()
    {
        var plan = ProfessionPlannerEngine.Plan(Request(stock: new[]
        {
            Stack("bag-plumes", 10, 4), Stack("bank-plumes", 10, 6, "bank"),
            Stack("bag-sesame", 30, 8)
        }));
        Assert.Equal(100, plan.Progress.NeededXp);
        Assert.Equal(10, plan.Progress.PlannedCrafts);
        Assert.Equal(100, plan.Progress.PotentialXpEarned);
        Assert.True(plan.Progress.TargetReachableFromKnownXp);
        Assert.Equal(10, plan.UsedStock[10]); // deux piles ne sont pas consommées chacune deux fois
        Assert.Equal(8, plan.UsedStock[30]);
        Assert.Single(plan.Steps, s => s.Kind == PlanStepKind.Drop && s.ItemGid == 10 && s.Quantity == 10);
        Assert.Single(plan.Steps, s => s.Kind == PlanStepKind.Gather && s.ItemGid == 30 && s.Quantity == 12);
        Assert.Contains(plan.Steps, s => s.Kind == PlanStepKind.Craft && s.ItemGid == 20 && s.Quantity == 20);
        Assert.Contains(plan.Steps, s => s.Kind == PlanStepKind.Craft && s.ItemGid == 500 && s.Quantity == 10);
        Assert.Equal(240, plan.FullSecondsEstimate);
        Assert.Equal(0, plan.FullKamasEstimate);
        Assert.True(plan.IsComplete);
        Assert.False(plan.Executed);
        Assert.False(plan.Acknowledged);
    }

    [Fact]
    public void Inventory_is_globally_reserved_across_multiple_recipe_branches()
    {
        var oil = Oil() with { Ingredients = new[] { new RecipeIngredient(10, 9) } };
        var request = Request(
            recipes: new[] { Jewelry(new RecipeIngredient(10, 4), new RecipeIngredient(20, 1)), oil },
            stock: new[] { Stack("a", 10, 10) },
            requested: 1);
        // Tester la BOM indépendante du niveau cible : un seul craft ne suffit PAS à atteindre 30.
        var result = ProfessionPlannerEngine.PlanItem(request, 500, 1);
        Assert.Equal(10, result.UsedStock[10]);
        Assert.Equal(0, result.RemainingStock[10]);
        Assert.Contains(result.Steps, s => s.Kind == PlanStepKind.Drop && s.ItemGid == 10 && s.Quantity == 3);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Duplicate_inventory_stack_ids_are_rejected_not_double_counted()
    {
        Assert.Throws<ArgumentException>(() => ProfessionPlannerEngine.Plan(Request(
            stock: new[] { Stack("same", 10, 5), Stack("same", 10, 5) })));
    }

    [Fact]
    public void Missing_xp_table_yields_no_fabricated_20_to_30_craft_count()
    {
        var r = Request() with { XpTable = null };
        var plan = ProfessionPlannerEngine.Plan(r);
        Assert.Null(plan.Progress.PlannedCrafts);
        Assert.Null(plan.Progress.NeededXp);
        Assert.Empty(plan.Steps);
        Assert.Contains(plan.Warnings, w => w.Contains("XP", StringComparison.Ordinal));
    }

    [Fact]
    public void Unverified_current_xp_or_unverified_table_blocks_progress_prediction()
    {
        var first = ProfessionPlannerEngine.Plan(Request(goal: Goal(100, false)));
        Assert.Null(first.Progress.PlannedCrafts);
        var second = ProfessionPlannerEngine.Plan(Request() with {
            XpTable = Xp() with { Verified = false }
        });
        Assert.Null(second.Progress.TargetXp);
        Assert.Null(second.Progress.PlannedCrafts);
    }

    [Fact]
    public void Unverified_recipe_xp_never_predicts_required_crafts()
    {
        var baseRequest = Request();
        var r = baseRequest with { Recipes = baseRequest.Recipes.Select(x => x.RecipeId == 1000
            ? x with { XpVerified = false } : x).ToArray() };
        var plan = ProfessionPlannerEngine.Plan(r);
        Assert.Equal(100, plan.Progress.NeededXp);
        Assert.Null(plan.Progress.PlannedCrafts);
    }

    [Fact]
    public void Recipe_xp_from_another_version_is_not_mixed_with_xp_thresholds()
    {
        var baseline = Request();
        var recipes = baseline.Recipes.Select(r => r.RecipeId == 1000
            ? r with { SourceVersion = "another-game-version" } : r).ToArray();
        var plan = ProfessionPlannerEngine.Plan(baseline with { Recipes = recipes });
        Assert.Null(plan.Progress.PlannedCrafts);
        Assert.Equal(100, plan.Progress.NeededXp);
    }

    [Fact]
    public void Manual_crafts_work_without_xp_evidence_but_do_not_claim_level_up()
    {
        var plan = ProfessionPlannerEngine.Plan(Request(requested: 3) with { XpTable = null });
        Assert.Equal(3, plan.Progress.PlannedCrafts);
        Assert.Null(plan.Progress.NeededXp);
        Assert.False(plan.Progress.TargetReachableFromKnownXp);
        Assert.Contains(plan.Steps, s => s.Kind == PlanStepKind.Craft && s.ItemGid == 500 && s.Quantity == 3);
    }

    [Fact]
    public void Missing_recipe_or_external_evidence_returns_unresolved_not_fake_farm_or_hdv()
    {
        var plan = ProfessionPlannerEngine.PlanItem(Request(recipes: Array.Empty<ProfessionRecipe>(),
            sources: Array.Empty<MaterialSource>()), 999, 4);
        var step = Assert.Single(plan.Steps);
        Assert.Equal(PlanStepKind.Unresolved, step.Kind);
        Assert.False(plan.IsComplete);
        Assert.Null(plan.FullKamasEstimate);
        Assert.Null(plan.FullSecondsEstimate);
    }

    [Fact]
    public void Recipe_cycle_is_detected_without_unbounded_recursion()
    {
        var a = Jewelry(new RecipeIngredient(20, 1));
        var b = Oil() with { Ingredients = new[] { new RecipeIngredient(500, 1) } };
        var result = ProfessionPlannerEngine.PlanItem(Request(recipes: new[] { a, b },
            sources: Array.Empty<MaterialSource>()), 500, 1);
        Assert.Contains(result.Steps, s => s.Kind == PlanStepKind.Unresolved &&
            s.Detail.Contains("Cycle", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.Steps.Count <= 5);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void Foreign_job_level_must_be_proven_before_subcraft()
    {
        var result = ProfessionPlannerEngine.Plan(Request(jobs: new Dictionary<int, int>()));
        Assert.Contains(result.Steps, s => s.Kind == PlanStepKind.Unresolved &&
            s.Detail.Contains("Niveau", StringComparison.OrdinalIgnoreCase));
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void Multiple_sources_without_comparable_cost_and_time_remain_a_manual_choice()
    {
        var sources = new MaterialSource[]
        {
            Drop(10), new(10, AcquisitionMethod.Buy, null, 75, "observed-fixture", true)
        };
        var plan = ProfessionPlannerEngine.PlanItem(Request(sources: sources), 10, 10);
        var step = Assert.Single(plan.Steps);
        Assert.Equal(PlanStepKind.Unresolved, step.Kind);
        Assert.Equal(2, step.Alternatives.Count);
        Assert.Contains(step.Alternatives, s => s.Method == AcquisitionMethod.Buy && s.KamasPerUnit == 75);
        Assert.Null(plan.FullKamasEstimate);
    }

    [Fact]
    public void Weighted_optimizer_chooses_only_when_both_candidates_are_comparable_and_verified()
    {
        var sources = new[]
        {
            new MaterialSource(10, AcquisitionMethod.Gather, 20, 0, "fixture-a", true),
            new MaterialSource(10, AcquisitionMethod.Buy, 2, 5, "fixture-b", true)
        };
        var req = Request(sources: sources) with { Preferences = new ProfessionPreferences(1, 1) };
        var plan = ProfessionPlannerEngine.PlanItem(req, 10, 4);
        var step = Assert.Single(plan.Steps);
        Assert.Equal(PlanStepKind.Buy, step.Kind);
        Assert.Equal(20m, step.Kamas);
        Assert.Equal(8L, step.Seconds);
        Assert.True(plan.IsComplete);
        Assert.True(plan.Acknowledged == false && plan.Executed == false);
        var timeCheap = ProfessionPlannerEngine.PlanItem(req with
        { Preferences = new ProfessionPreferences(10, 0) }, 10, 4);
        Assert.Equal(PlanStepKind.Gather, Assert.Single(timeCheap.Steps).Kind);
    }

    [Fact]
    public void Explicit_method_can_be_planned_with_unknown_price_but_total_cost_stays_unknown()
    {
        var source = new MaterialSource(30, AcquisitionMethod.Buy, 5, null, "source-v1", true);
        var plan = ProfessionPlannerEngine.PlanItem(Request(sources: new[] { source },
            methods: new Dictionary<long, AcquisitionMethod> { [30] = AcquisitionMethod.Buy }), 30, 6);
        Assert.Equal(PlanStepKind.Buy, Assert.Single(plan.Steps).Kind);
        Assert.Null(plan.FullKamasEstimate);
        Assert.Equal(30, plan.FullSecondsEstimate);
    }

    [Fact]
    public void Recipe_result_stack_surplus_is_saved_for_next_branch()
    {
        var subcraft = Oil() with { ResultQty = 10, Ingredients = new[] { new RecipeIngredient(30, 1) } };
        var jewelry = Jewelry(new RecipeIngredient(20, 3), new RecipeIngredient(20, 4));
        var plan = ProfessionPlannerEngine.PlanItem(Request(recipes: new[] { jewelry, subcraft },
            sources: new[] { Gather(30) }), 500, 1);
        Assert.Contains(plan.Steps, s => s.Kind == PlanStepKind.Craft && s.ItemGid == 20 && s.Quantity == 10);
        Assert.Equal(1, plan.Steps.Count(s => s.Kind == PlanStepKind.Craft && s.ItemGid == 20));
        Assert.Equal(3, plan.RemainingStock[20]);
        Assert.Equal(4, plan.UsedStock[20]);
    }

    [Fact]
    public void Source_not_verified_or_capped_supply_is_not_chosen()
    {
        var source = Drop(10) with { Verified = false };
        var first = ProfessionPlannerEngine.PlanItem(Request(sources: new[] { source }), 10, 2);
        Assert.Equal(PlanStepKind.Unresolved, Assert.Single(first.Steps).Kind);
        var capped = ProfessionPlannerEngine.PlanItem(Request(sources: new[]
            { Drop(10) with { MaxUnits = 1 } }), 10, 2);
        Assert.Equal(PlanStepKind.Unresolved, Assert.Single(capped.Steps).Kind);
    }

    [Fact]
    public void Recipe_selection_requires_provenance_and_valid_level_and_does_not_force_other_options()
    {
        var a = Jewelry(new RecipeIngredient(10, 1));
        var b = a with { RecipeId = 1010, Ingredients = new[] { new RecipeIngredient(30, 1) } };
        var req = Request(recipes: new[] { a, b }, sources: new[] { Drop(10), Gather(30) });
        var unknown = ProfessionPlannerEngine.PlanItem(req, 500, 1);
        Assert.Equal(PlanStepKind.Unresolved, Assert.Single(unknown.Steps).Kind);
        var selected = ProfessionPlannerEngine.PlanItem(req with
            { RecipeChoices = new Dictionary<long, long> { [500] = 1010 } }, 500, 1);
        Assert.Contains(selected.Steps, s => s.Kind == PlanStepKind.Craft && s.RecipeId == 1010);
        var bad = ProfessionPlannerEngine.PlanItem(req with
            { RecipeChoices = new Dictionary<long, long> { [500] = 9999 } }, 500, 1);
        Assert.Equal(PlanStepKind.Unresolved, Assert.Single(bad.Steps).Kind);
    }

    [Fact]
    public void Large_bom_and_invalid_inputs_fail_closed_instead_of_wrapping_or_hanging()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProfessionPlannerEngine.PlanItem(
            Request(), 10, 1_000_000_001));
        Assert.Throws<ArgumentException>(() => ProfessionPlannerEngine.Plan(Request(stock: new[]
            { Stack("a", 10, -1) })));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProfessionPlannerEngine.Plan(Request() with
            { RequestedCrafts = 1_000_000_001 }));
        var veryLarge = Request(recipes: new[]
            { Jewelry(new RecipeIngredient(10, 1_000_000_000)) }, requested: 1_000_000_000);
        Assert.ThrowsAny<Exception>(() => ProfessionPlannerEngine.Plan(veryLarge));
    }

    [Fact]
    public void All_plan_outputs_are_deterministic_with_stable_dependency_order()
    {
        var input = Request(stock: new[] { Stack("b", 30, 2), Stack("a", 10, 3) });
        var a = ProfessionPlannerEngine.Plan(input);
        var b = ProfessionPlannerEngine.Plan(input);
        // ProfessionPlanStep contient des tableaux : comparer leur contenu, pas leurs références.
        static string StepSignature(ProfessionPlanStep step) =>
            string.Join("|", step.Id, step.Kind, step.ItemGid, step.Quantity, step.RecipeId,
                step.Verified, step.Kamas, step.Seconds,
                string.Join(",", step.Dependencies),
                string.Join(",", step.Alternatives.Select(x => $"{x.Method}/{x.RecipeId}/{x.Verified}/{x.SourceVersion}")));
        Assert.Equal(a.Steps.Select(StepSignature), b.Steps.Select(StepSignature));
        Assert.Equal(a.Warnings, b.Warnings);
        Assert.Equal(a.UsedStock, b.UsedStock);
        Assert.All(a.Steps, step => Assert.All(step.Dependencies,
            dep => Assert.True(dep < step.Id, "Dependencies are topologically earlier than their craft")));
    }

    [Fact]
    public void Comparable_training_recipes_recommend_lower_weighted_time_and_cost()
    {
        var a = Jewelry(new RecipeIngredient(10, 1));
        var b = a with { RecipeId = 2000, ResultGid = 501,
            Ingredients = new[] { new RecipeIngredient(30, 1) }, SecondsPerCraft = 1 };
        var request = Request(recipes: new[] { a, b }, sources: new[] { Drop(10), Gather(30) });
        var suggestion = ProfessionPlannerEngine.SuggestTrainingRecipes(request);
        Assert.Equal(2, suggestion.Candidates.Count);
        Assert.All(suggestion.Candidates, candidate => Assert.True(candidate.Comparable));
        Assert.Equal(2000, suggestion.SuggestedRecipeId);
    }

    [Fact]
    public void Incomparable_training_recipes_never_get_automatic_recommendation()
    {
        var a = Jewelry(new RecipeIngredient(10, 1));
        var b = a with { RecipeId = 2000, ResultGid = 501,
            Ingredients = new[] { new RecipeIngredient(30, 1) } };
        var sources = new[] { Drop(10), new MaterialSource(30, AcquisitionMethod.Buy,
            5, null, "price-not-observed", true) };
        var suggestion = ProfessionPlannerEngine.SuggestTrainingRecipes(
            Request(recipes: new[] { a, b }, sources: sources));
        Assert.Null(suggestion.SuggestedRecipeId);
        Assert.Contains(suggestion.Candidates, x => !x.Comparable && x.RecipeId == 2000);
        Assert.Contains("manuellement", suggestion.Reason, StringComparison.Ordinal);
    }
}
