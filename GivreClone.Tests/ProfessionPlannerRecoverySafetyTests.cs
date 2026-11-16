using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.App.Services.ProfessionPlanner;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Entirely synthetic inventory, recipes and acquisition evidence.
/// Tests capacity accounting in a proposal, never server-side execution or ACKs.</summary>
public sealed class ProfessionPlannerRecoverySafetyTests
{
    private static ProfessionRecipe Recipe(params RecipeIngredient[] ingredients) =>
        new(900, 900, 1, ingredients, 1, 7, SourceVersion: "fixture-craft-v1",
            Verified: true, SecondsPerCraft: 0, KamasPerCraft: 0);

    private static MaterialSource Source(AcquisitionMethod method, long capacity,
        decimal? cost = 1m, long? seconds = 2, string version = "fixture-source-v1") =>
        new(10, method, seconds, cost, version, Verified: true, MaxUnits: capacity);

    private static ProfessionPlanRequest Request(IReadOnlyList<RecipeIngredient> ingredients,
        IReadOnlyList<MaterialSource> sources,
        IReadOnlyList<ProfessionInventoryStack>? inventory = null,
        ProfessionPreferences? preferences = null) =>
        new(new ProfessionGoal(7, "Synthetic profession", 10, 10), null, null,
            new[] { Recipe(ingredients.ToArray()) },
            inventory ?? Array.Empty<ProfessionInventoryStack>(), sources,
            Preferences: preferences ?? new ProfessionPreferences(1, 0));

    [Fact]
    public void Interrupted_material_plan_never_reserves_one_source_capacity_twice_across_branches()
    {
        var request = Request(new[] { new RecipeIngredient(10, 2), new RecipeIngredient(10, 2) },
            new[] { Source(AcquisitionMethod.Gather, 2) });

        var first = ProfessionPlannerEngine.PlanItem(request, 900, 1);
        Assert.Single(first.Steps, s => s.Kind == PlanStepKind.Gather && s.Quantity == 2);
        Assert.Single(first.Steps, s => s.Kind == PlanStepKind.Unresolved &&
            s.ItemGid == 10 && s.Quantity == 2);
        Assert.False(first.IsComplete);
        Assert.Null(first.FullKamasEstimate);
        Assert.Null(first.FullSecondsEstimate);
        Assert.False(first.Executed);
        Assert.False(first.Acknowledged);

        // Replanning the same immutable observation cannot inherit previous reservations
        // or turn a proposed acquisition into evidence of real material availability.
        var replay = ProfessionPlannerEngine.PlanItem(request, 900, 1);
        Assert.Equal(StepSignature(first), StepSignature(replay));
        Assert.Equal(first.Warnings, replay.Warnings);
        Assert.Equal(first.RemainingStock, replay.RemainingStock);
    }

    [Fact]
    public void Proven_source_capacity_can_be_allocated_across_branches_up_to_its_global_limit()
    {
        var request = Request(new[] { new RecipeIngredient(10, 2), new RecipeIngredient(10, 2) },
            new[] { Source(AcquisitionMethod.Gather, 4) });
        var plan = ProfessionPlannerEngine.PlanItem(request, 900, 1);

        Assert.True(plan.IsComplete);
        Assert.Equal(4, plan.Steps.Where(s => s.Kind == PlanStepKind.Gather).Sum(s => s.Quantity));
        Assert.Equal(4m, plan.FullKamasEstimate);
        Assert.Equal(8L, plan.FullSecondsEstimate);
    }

    [Fact]
    public void Two_comparable_sources_split_scarce_material_without_exceeding_either_limit()
    {
        var request = Request(new[] { new RecipeIngredient(10, 4) }, new[]
        {
            Source(AcquisitionMethod.Gather, 2, 1m, 2, "fixture-gather"),
            Source(AcquisitionMethod.Buy, 3, 5m, 1, "fixture-buy")
        });

        var plan = ProfessionPlannerEngine.PlanItem(request, 900, 1);
        Assert.True(plan.IsComplete);
        Assert.Single(plan.Steps, s => s.Kind == PlanStepKind.Gather && s.Quantity == 2);
        Assert.Single(plan.Steps, s => s.Kind == PlanStepKind.Buy && s.Quantity == 2);
        Assert.Equal(12m, plan.FullKamasEstimate);
        Assert.Equal(6L, plan.FullSecondsEstimate);
    }

    [Fact]
    public void Insufficient_combined_capacity_keeps_the_entire_requirement_unresolved()
    {
        var request = Request(new[] { new RecipeIngredient(10, 5) }, new[]
        {
            Source(AcquisitionMethod.Gather, 2, version: "fixture-gather"),
            Source(AcquisitionMethod.Buy, 2, version: "fixture-buy")
        });
        var plan = ProfessionPlannerEngine.PlanItem(request, 900, 1);

        Assert.Single(plan.Steps, s => s.Kind == PlanStepKind.Unresolved && s.ItemGid == 10 && s.Quantity == 5);
        Assert.DoesNotContain(plan.Steps, s => s.Kind is PlanStepKind.Gather or PlanStepKind.Buy);
        Assert.False(plan.IsComplete);
        Assert.Equal(0m, plan.KnownKamasSubtotal);
        Assert.Null(plan.FullKamasEstimate);
    }

    [Fact]
    public void Already_observed_inventory_reduces_required_source_allocation()
    {
        var request = Request(new[] { new RecipeIngredient(10, 4) },
            new[] { Source(AcquisitionMethod.Gather, 1) },
            new[] { new ProfessionInventoryStack("bag-10", 10, 3, "bag") });
        var plan = ProfessionPlannerEngine.PlanItem(request, 900, 1);

        Assert.True(plan.IsComplete);
        Assert.Equal(3L, plan.UsedStock[10]);
        Assert.Single(plan.Steps, s => s.Kind == PlanStepKind.Gather && s.Quantity == 1);
        Assert.Equal(1m, plan.FullKamasEstimate);
        Assert.False(plan.Acknowledged);
    }

    [Fact]
    public void Resume_from_new_observed_stock_recomputes_shortfall_without_inheriting_pending_allocations()
    {
        var baseRequest = Request(new[] { new RecipeIngredient(10, 2) },
            new[] { Source(AcquisitionMethod.Gather, 1) });
        var interrupted = ProfessionPlannerEngine.PlanItem(baseRequest, 900, 1);
        Assert.False(interrupted.IsComplete);
        Assert.DoesNotContain(interrupted.Steps, s => s.Kind == PlanStepKind.Gather);

        // A fresh inventory observation is input from the caller, never inferred from
        // an earlier proposed collection or the engine's in-memory reservation ledger.
        var resumed = ProfessionPlannerEngine.PlanItem(baseRequest with
        {
            Inventory = new[] { new ProfessionInventoryStack("observed-bag", 10, 1, "bag") }
        }, 900, 1);
        Assert.True(resumed.IsComplete);
        Assert.Equal(1L, resumed.UsedStock[10]);
        Assert.Single(resumed.Steps, s => s.Kind == PlanStepKind.Gather && s.Quantity == 1);
        Assert.False(resumed.Executed);
        Assert.False(resumed.Acknowledged);
        Assert.Equal(StepSignature(interrupted), StepSignature(
            ProfessionPlannerEngine.PlanItem(baseRequest, 900, 1)));
    }

    [Fact]
    public void Cyclic_subcrafts_stay_unresolved_even_if_a_separate_material_source_is_available()
    {
        var root = Recipe(new RecipeIngredient(10, 1), new RecipeIngredient(20, 1));
        var cycle = new ProfessionRecipe(901, 20, 1, new[] { new RecipeIngredient(900, 1) },
            1, 7, SourceVersion: "fixture-cycle-v1", Verified: true,
            SecondsPerCraft: 0, KamasPerCraft: 0);
        var baseline = Request(root.Ingredients,
            new[] { Source(AcquisitionMethod.Gather, 1) });
        var plan = ProfessionPlannerEngine.PlanItem(baseline with
        {
            Recipes = new[] { root, cycle }
        }, 900, 1);

        Assert.Single(plan.Steps, s => s.Kind == PlanStepKind.Gather && s.Quantity == 1);
        Assert.Contains(plan.Steps, s => s.Kind == PlanStepKind.Unresolved &&
            s.Detail.Contains("Cycle", StringComparison.OrdinalIgnoreCase));
        Assert.False(plan.IsComplete);
        Assert.Null(plan.FullKamasEstimate);
    }

    [Fact]
    public void Numeric_estimate_overflow_aborts_planning_instead_of_returning_a_complete_plan()
    {
        var seconds = Request(new[] { new RecipeIngredient(10, 2) },
            new[] { Source(AcquisitionMethod.Gather, 2, seconds: long.MaxValue) });
        Assert.Throws<OverflowException>(() => ProfessionPlannerEngine.PlanItem(seconds, 900, 1));

        var kamas = Request(new[] { new RecipeIngredient(10, 2) },
            new[] { Source(AcquisitionMethod.Buy, 2, cost: decimal.MaxValue) });
        Assert.Throws<OverflowException>(() => ProfessionPlannerEngine.PlanItem(kamas, 900, 1));
    }

    [Fact]
    public void Ambiguous_duplicate_source_identity_cannot_multiply_supply_capacity()
    {
        var source = Source(AcquisitionMethod.Gather, 2);
        var request = Request(new[] { new RecipeIngredient(10, 3) },
            new[] { source, source with { KamasPerUnit = 5m, MaxUnits = 3 } });
        Assert.Throws<ArgumentException>(() => ProfessionPlannerEngine.PlanItem(request, 900, 1));
    }

    [Fact]
    public void Unknown_comparative_cost_forbids_automatic_split_even_with_sufficient_total_capacity()
    {
        var request = Request(new[] { new RecipeIngredient(10, 3) }, new[]
        {
            Source(AcquisitionMethod.Gather, 2, version: "fixture-gather"),
            Source(AcquisitionMethod.Buy, 2, cost: null, version: "fixture-buy")
        });
        var plan = ProfessionPlannerEngine.PlanItem(request, 900, 1);

        Assert.False(plan.IsComplete);
        Assert.Single(plan.Steps, s => s.Kind == PlanStepKind.Unresolved && s.Quantity == 3);
        Assert.DoesNotContain(plan.Steps, s => s.Kind == PlanStepKind.Buy);
    }

    private static string[] StepSignature(ProfessionPlan plan) => plan.Steps
        .Select(s => string.Join("|", s.Id, s.Kind, s.ItemGid, s.Quantity, s.RecipeId,
            s.Verified, s.Kamas, s.Seconds, string.Join(",", s.Dependencies)))
        .ToArray();
}
