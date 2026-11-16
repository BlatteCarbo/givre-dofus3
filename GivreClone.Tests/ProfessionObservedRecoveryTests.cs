using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.App.Services.ProfessionPlanner;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Only synthetic, caller-attested observations: no game state or ACK.</summary>
public sealed class ProfessionObservedRecoveryTests
{
    private const string Version = "fixture-observed-v1";

    private static ProfessionRecipe Recipe(params RecipeIngredient[] ingredients) =>
        new(900, 9000, 1, ingredients, 1, 7,
            SourceVersion: Version, Verified: true, SecondsPerCraft: 1,
            KamasPerCraft: 0);

    private static ProfessionPlanRequest Request(params RecipeIngredient[] ingredients) =>
        new(new ProfessionGoal(7, "Synthetic profession", 10, 10),
            null, null, new[] { Recipe(ingredients) },
            Array.Empty<ProfessionInventoryStack>(), Array.Empty<MaterialSource>(),
            Preferences: new ProfessionPreferences(1, 0));

    private static MaterialSource Gather(long cap, string version = Version) =>
        new(10, AcquisitionMethod.Gather, 2, 1, version, true, cap);

    private static MaterialSource Buy(long cap, string version = Version) =>
        new(10, AcquisitionMethod.Buy, 1, 5, version, true, cap);

    private static ProfessionInventoryStack Stock(string id, long count) =>
        new(id, 10, count, "bag");

    private static ProfessionRecoveryObservation Observe(
        long revision, long inventory, long capacity,
        string subject = "fixture-character", string version = Version) =>
        new(subject, revision, version, true, true,
            inventory == 0 ? Array.Empty<ProfessionInventoryStack>() : new[] { Stock("bag-1", inventory) },
            new[] { Gather(capacity, version) });

    private static ProfessionRecoveryResult Begin(
        ProfessionPlanRequest request, ProfessionRecoveryObservation observation) =>
        ProfessionPlannerEngine.BeginObservedItem(request, observation, 9000, 1);

    private static ProfessionRecoveryResult Resume(
        ProfessionPlanRequest request, ProfessionRecoveryResult previous,
        ProfessionRecoveryObservation original, ProfessionRecoveryObservation next) =>
        ProfessionPlannerEngine.ResumeObserved(request, previous.Checkpoint!,
            previous.Plan!, original, next);

    [Fact]
    public void Fresh_observation_replans_pending_material_and_discards_the_previous_shortfall()
    {
        var request = Request(new RecipeIngredient(10, 4));
        var firstObservation = Observe(10, inventory: 1, capacity: 2);
        var first = Begin(request, firstObservation);
        Assert.True(first.Accepted);
        Assert.False(first.Plan!.IsComplete);
        Assert.Single(first.Plan.Steps, s => s.Kind == PlanStepKind.Unresolved && s.Quantity == 3);

        var newObservation = Observe(11, inventory: 2, capacity: 2);
        var resumed = Resume(request, first, firstObservation, newObservation);
        Assert.True(resumed.Accepted);
        Assert.True(resumed.PreviousProposalInvalidated);
        Assert.True(resumed.Plan!.IsComplete);
        Assert.Equal(2, resumed.Plan.UsedStock[10]);
        Assert.Single(resumed.Plan.Steps, s => s.Kind == PlanStepKind.Gather && s.Quantity == 2);
        Assert.Equal(11, resumed.Checkpoint!.Revision);
        Assert.False(resumed.Plan.Executed);
        Assert.False(resumed.Plan.Acknowledged);
    }

    [Fact]
    public void Confirmed_finished_output_in_a_new_inventory_snapshot_prevents_duplicate_craft()
    {
        var request = Request(new RecipeIngredient(10, 2));
        var originalObservation = Observe(1, 0, 2);
        var original = Begin(request, originalObservation);
        Assert.True(original.Plan!.IsComplete);
        Assert.Single(original.Plan.Steps, s => s.Kind == PlanStepKind.Craft);

        var later = Observe(2, 0, 2) with
        {
            Inventory = new[] { new ProfessionInventoryStack("new-output", 9000, 1, "bag") }
        };
        var resumed = Resume(request, original, originalObservation, later);
        Assert.True(resumed.Accepted);
        Assert.True(resumed.Plan!.IsComplete);
        Assert.Single(resumed.Plan.Steps);
        Assert.Single(resumed.Plan.Steps, s => s.Kind == PlanStepKind.UseInventory &&
            s.ItemGid == 9000 && s.Quantity == 1);
        Assert.DoesNotContain(resumed.Plan.Steps, s => s.Kind is PlanStepKind.Craft or PlanStepKind.Gather);
        Assert.False(resumed.Plan.Executed);
    }

    [Fact]
    public void Source_capacity_reduction_never_reuses_an_earlier_complete_proposal()
    {
        var request = Request(new RecipeIngredient(10, 3));
        var firstObservation = Observe(1, inventory: 0, capacity: 3);
        var original = Begin(request, firstObservation);
        Assert.True(original.Plan!.IsComplete);

        var resumed = Resume(request, original, firstObservation, Observe(2, inventory: 0, capacity: 1));
        Assert.True(resumed.Accepted);
        Assert.True(resumed.PreviousProposalInvalidated);
        Assert.False(resumed.Plan!.IsComplete);
        Assert.Null(resumed.Plan.FullKamasEstimate);
        Assert.DoesNotContain(resumed.Plan.Steps, s => s.Kind == PlanStepKind.Gather);
        Assert.Single(resumed.Plan.Steps, s => s.Kind == PlanStepKind.Unresolved && s.Quantity == 3);
    }

    [Fact]
    public void Explicit_zero_remaining_source_capacity_is_an_exhausted_observation()
    {
        var request = Request(new RecipeIngredient(10, 1));
        var originalObservation = Observe(1, 0, 1);
        var original = Begin(request, originalObservation);
        Assert.True(original.Plan!.IsComplete);

        var exhausted = Resume(request, original, originalObservation, Observe(2, 0, 0));
        Assert.True(exhausted.Accepted);
        Assert.True(exhausted.PreviousProposalInvalidated);
        Assert.False(exhausted.Plan!.IsComplete);
        Assert.Single(exhausted.Plan.Steps, s => s.Kind == PlanStepKind.Unresolved && s.ItemGid == 10);
        Assert.DoesNotContain(exhausted.Plan.Steps, s => s.Kind == PlanStepKind.Gather);
    }

    [Fact]
    public void Incomplete_inventory_or_source_cap_snapshot_invalidates_old_preview()
    {
        var request = Request(new RecipeIngredient(10, 2));
        var oldObservation = Observe(1, 0, 2);
        var previous = Begin(request, oldObservation);
        Assert.True(previous.Plan!.IsComplete);

        var partialStock = Resume(request, previous, oldObservation,
            Observe(2, 0, 2) with { InventoryComplete = false });
        Assert.Equal(ProfessionRecoveryIssue.InvalidObservation, partialStock.Issue);
        Assert.True(partialStock.PreviousProposalInvalidated);
        Assert.Null(partialStock.Plan);
        Assert.Null(partialStock.Checkpoint);

        var partialCaps = Resume(request, previous, oldObservation,
            Observe(2, 0, 2) with { SourceCapsComplete = false });
        Assert.Equal(ProfessionRecoveryIssue.InvalidObservation, partialCaps.Issue);
        Assert.Null(partialCaps.Plan);
    }

    [Fact]
    public void Replayed_or_out_of_order_observations_fail_closed()
    {
        var request = Request(new RecipeIngredient(10, 1));
        var oldObservation = Observe(8, 0, 1);
        var first = Begin(request, oldObservation);

        foreach (long revision in new long[] { 7, 8 })
        {
            var resumed = Resume(request, first, oldObservation, Observe(revision, 0, 1));
            Assert.Equal(ProfessionRecoveryIssue.StaleObservation, resumed.Issue);
            Assert.True(resumed.PreviousProposalInvalidated);
            Assert.Null(resumed.Plan);
        }
    }

    [Fact]
    public void Different_character_or_game_version_blocks_cross_scope_recovery()
    {
        var request = Request(new RecipeIngredient(10, 1));
        var originalObservation = Observe(1, 0, 1);
        var initial = Begin(request, originalObservation);

        var switchedCharacter = Resume(request, initial, originalObservation,
            Observe(2, 0, 1, subject: "other-character"));
        Assert.Equal(ProfessionRecoveryIssue.SubjectChanged, switchedCharacter.Issue);
        Assert.Null(switchedCharacter.Plan);

        var updatedCatalog = Resume(request, initial, originalObservation,
            Observe(2, 0, 1, version: "fixture-new-version"));
        Assert.Equal(ProfessionRecoveryIssue.CatalogChanged, updatedCatalog.Issue);
        Assert.Null(updatedCatalog.Plan);
    }

    [Fact]
    public void Altered_recipe_or_previous_proposal_cannot_be_resumed_as_old_plan()
    {
        var request = Request(new RecipeIngredient(10, 2));
        var oldObservation = Observe(1, 0, 2);
        var initial = Begin(request, oldObservation);
        var changedRequest = request with
        {
            Recipes = new[] { Recipe(new RecipeIngredient(10, 3)) }
        };
        var rejectedRequest = Resume(changedRequest, initial, oldObservation, Observe(2, 0, 2));
        Assert.Equal(ProfessionRecoveryIssue.RequestChanged, rejectedRequest.Issue);
        Assert.Null(rejectedRequest.Plan);

        var changedPlan = initial.Plan! with { Executed = true };
        var rejectedProposal = ProfessionPlannerEngine.ResumeObserved(request,
            initial.Checkpoint!, changedPlan, oldObservation, Observe(2, 0, 2));
        Assert.Equal(ProfessionRecoveryIssue.PreviousProposalChanged, rejectedProposal.Issue);
        Assert.Null(rejectedProposal.Plan);
    }

    [Fact]
    public void Changed_original_inventory_invalidates_the_checkpoint()
    {
        var request = Request(new RecipeIngredient(10, 2));
        var oldObservation = Observe(1, 0, 2);
        var initial = Begin(request, oldObservation);
        var alteredOriginal = oldObservation with
        {
            Inventory = new[] { Stock("late-insert", 1) }
        };
        var resume = ProfessionPlannerEngine.ResumeObserved(request,
            initial.Checkpoint!, initial.Plan!, alteredOriginal, Observe(2, 0, 2));
        Assert.Equal(ProfessionRecoveryIssue.PreviousProposalChanged, resume.Issue);
        Assert.True(resume.PreviousProposalInvalidated);
        Assert.Null(resume.Checkpoint);
    }

    [Fact]
    public void Changed_cursor_target_or_quantity_is_rejected_by_replaying_the_original_plan()
    {
        var request = Request(new RecipeIngredient(10, 2));
        var originalObservation = Observe(1, 0, 2);
        var initial = Begin(request, originalObservation);
        Assert.True(initial.Plan!.IsComplete);

        var modifiedQuantity = initial.Checkpoint! with { ItemQuantity = 2 };
        var rejectedQuantity = ProfessionPlannerEngine.ResumeObserved(request,
            modifiedQuantity, initial.Plan, originalObservation, Observe(2, 0, 2));
        Assert.Equal(ProfessionRecoveryIssue.PreviousProposalChanged, rejectedQuantity.Issue);
        Assert.Null(rejectedQuantity.Plan);

        var modifiedTarget = initial.Checkpoint! with { ItemGid = 10 };
        var rejectedTarget = ProfessionPlannerEngine.ResumeObserved(request,
            modifiedTarget, initial.Plan, originalObservation, Observe(2, 0, 2));
        Assert.Equal(ProfessionRecoveryIssue.PreviousProposalChanged, rejectedTarget.Issue);
        Assert.Null(rejectedTarget.Plan);
    }

    [Fact]
    public void Global_capacity_allocation_is_recomputed_after_restart_with_multiple_recipe_branches()
    {
        var request = Request(new RecipeIngredient(10, 2), new RecipeIngredient(10, 2));
        var oldObservation = Observe(1, 0, 3);
        var initial = Begin(request, oldObservation);
        Assert.False(initial.Plan!.IsComplete);
        Assert.Equal(2, initial.Plan.Steps.Where(s => s.Kind == PlanStepKind.Gather).Sum(s => s.Quantity));

        var resumed = Resume(request, initial, oldObservation, Observe(2, 0, 4));
        Assert.True(resumed.Plan!.IsComplete);
        Assert.Equal(4, resumed.Plan.Steps.Where(s => s.Kind == PlanStepKind.Gather).Sum(s => s.Quantity));
        Assert.Equal(4m, resumed.Plan.FullKamasEstimate);
        Assert.Equal(9L, resumed.Plan.FullSecondsEstimate);
    }

    [Fact]
    public void Unbounded_or_duplicated_sources_are_not_accepted_as_finite_observed_capacity()
    {
        var request = Request(new RecipeIngredient(10, 2));
        var observation = Observe(1, 0, 2);

        var unbounded = Begin(request, observation with
        {
            Sources = new[] { Gather(2) with { MaxUnits = null } }
        });
        Assert.Equal(ProfessionRecoveryIssue.InvalidObservation, unbounded.Issue);
        Assert.Null(unbounded.Plan);

        var duplicated = Begin(request, observation with
        {
            Sources = new[] { Gather(2), Gather(2) with { MaxUnits = 3 } }
        });
        Assert.Equal(ProfessionRecoveryIssue.InvalidObservation, duplicated.Issue);
        Assert.Null(duplicated.Plan);
    }

    [Fact]
    public void Observed_price_or_provenance_incident_never_creates_a_successful_preview()
    {
        var request = Request(new RecipeIngredient(10, 2));
        var snapshot = Observe(1, 0, 2);

        var wrongVersion = Begin(request, snapshot with
        {
            Sources = new[] { Gather(2, "unknown-v2") }
        });
        Assert.Equal(ProfessionRecoveryIssue.InvalidObservation, wrongVersion.Issue);

        var explodingCost = Begin(request, snapshot with
        {
            Sources = new[] { Gather(2) with { KamasPerUnit = decimal.MaxValue } }
        });
        Assert.Equal(ProfessionRecoveryIssue.PlanningFailed, explodingCost.Issue);
        Assert.Null(explodingCost.Plan);
        Assert.Null(explodingCost.Checkpoint);
    }

    [Fact]
    public void Two_comparable_sources_share_remaining_supply_in_recovery_without_overallocation()
    {
        var request = Request(new RecipeIngredient(10, 4));
        var observed = Observe(1, 0, 2) with
        {
            Sources = new[] { Gather(2), Buy(2) }
        };
        var initial = Begin(request, observed);
        Assert.True(initial.Accepted);
        Assert.True(initial.Plan!.IsComplete);
        Assert.Single(initial.Plan.Steps, s => s.Kind == PlanStepKind.Gather && s.Quantity == 2);
        Assert.Single(initial.Plan.Steps, s => s.Kind == PlanStepKind.Buy && s.Quantity == 2);
        Assert.Equal(12m, initial.Plan.FullKamasEstimate);
    }

    [Fact]
    public void Same_snapshot_replay_is_deterministic_but_never_claims_real_execution()
    {
        var request = Request(new RecipeIngredient(10, 3));
        var observed = Observe(1, 0, 1) with
        {
            Inventory = new[] { Stock("stack-b", 1), Stock("stack-a", 1) }
        };
        var first = Begin(request, observed);
        var second = Begin(request, observed with
        {
            Inventory = observed.Inventory.Reverse().ToArray()
        });
        Assert.True(first.Accepted);
        Assert.Equal(first.Checkpoint, second.Checkpoint);
        Assert.Equal(first.Plan!.Steps.Select(s => (s.Kind, s.ItemGid, s.Quantity)),
            second.Plan!.Steps.Select(s => (s.Kind, s.ItemGid, s.Quantity)));
        Assert.False(first.Plan.Executed);
        Assert.False(second.Plan.Acknowledged);
    }

    [Fact]
    public void Existing_training_planner_is_accessible_through_observed_entrypoint()
    {
        var request = Request(new RecipeIngredient(10, 2)) with
        {
            TrainingRecipeId = 900,
            RequestedCrafts = 1
        };
        var observed = Observe(1, 0, 2);
        var first = ProfessionPlannerEngine.BeginObservedTraining(request, observed);
        Assert.True(first.Accepted);
        Assert.Equal(ProfessionRecoveryMode.Training, first.Checkpoint!.Mode);
        Assert.Contains(first.Plan!.Steps, s => s.Kind == PlanStepKind.Craft && s.RecipeId == 900);

        var recovered = Resume(request, first, observed, Observe(2, 1, 1));
        Assert.True(recovered.Accepted);
        Assert.Equal(1, recovered.Plan!.UsedStock[10]);
        Assert.Single(recovered.Plan.Steps, s => s.Kind == PlanStepKind.Gather && s.Quantity == 1);
        Assert.False(recovered.Plan.Acknowledged);
    }
}
