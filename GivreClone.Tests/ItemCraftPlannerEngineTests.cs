using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.App.Services.ItemCraftPlanner;
using Xunit;

namespace GivreClone.Tests;

/// <summary>All names, item ids, recipes and times below are FAKE synthetic fixtures.
/// Not verified Panoplie Bouftou content or Dofus data.</summary>
public sealed class ItemCraftPlannerEngineTests
{
    private const string Version = "FAKE-UNIT-TEST-NOT-DOFUS";

    private static CraftRecipe R(long id, long result, long? yield, params CraftIngredient[] ingredients) =>
        new(id, result, yield, ingredients, Version, true, 5, 20, 0, 3, true, true);
    private static CraftIngredient I(long id, long? count) => new(id, count);
    private static AcquisitionSource Drop(long id, long? seconds = 10, decimal? price = 0,
        long? capacity = null) => new(id, ItemAcquisitionKind.Drop, $"fake-drop-{id}",
        Version, true, price, seconds, capacity);
    private static AcquisitionSource Gather(long id, long? seconds = 5, decimal? price = 0) =>
        new(id, ItemAcquisitionKind.Gather, $"fake-gather-{id}", Version, true, price, seconds);
    private static ItemStockStack St(string stack, long id, long quantity, string container = "bag",
        bool verified = true) => new(stack, id, quantity, container, verified);
    private static ItemCraftContext C(
        IReadOnlyList<CraftRecipe>? recipes = null,
        IReadOnlyList<ItemStockStack>? stock = null,
        IReadOnlyList<AcquisitionSource>? sources = null,
        IReadOnlyList<EquipmentSetDefinition>? sets = null,
        IReadOnlyDictionary<int, int>? jobs = null,
        IReadOnlyDictionary<long, long>? recipesChosen = null,
        IReadOnlyDictionary<long, ItemAcquisitionKind>? methods = null,
        CraftOptimizationWeights? weights = null) => new(
            recipes ?? Array.Empty<CraftRecipe>(),
            stock ?? Array.Empty<ItemStockStack>(), sources ?? Array.Empty<AcquisitionSource>(),
            sets ?? Array.Empty<EquipmentSetDefinition>(),
            jobs ?? new Dictionary<int, int> { [5] = 30 },
            recipesChosen, methods, weights);

    // Synthetically named "Bouftou set" for explaining shared BOM. NOTHING authentic.
    private static ItemCraftContext FakeBouftou() => C(
        recipes: new[]
        {
            R(1000, 100, 1, I(1, 4), I(500, 2)), // FAKE hat
            R(1001, 101, 1, I(1, 3), I(500, 2)), // FAKE cloak
            R(1002, 102, 1, I(1, 2), I(500, 1)), // FAKE shoes
            R(1003, 500, 5, I(1, 1)),            // FAKE thread multi-yield
        },
        stock: new[] { St("bag-wool", 1, 3), St("bank-wool", 1, 4, "bank") },
        sources: new[] { Drop(1) },
        sets: new[] { new EquipmentSetDefinition(999, "Panoplie Bouftou - FAKE fixture",
            new long[] { 100, 101, 102 }, Version, true) });

    [Fact]
    public void One_item_recursively_expands_multi_level_recipe()
    {
        var plan = ItemCraftPlanner.PlanItem(new(100, 1), FakeBouftou());
        Assert.Equal(new long[] { 100 }, plan.Targets.Select(t => t.ItemId));
        Assert.Contains(plan.Steps, s => s.Kind == ItemCraftStepKind.CraftAtWorkshop && s.ItemId == 500 && s.Quantity == 5);
        Assert.Contains(plan.Steps, s => s.Kind == ItemCraftStepKind.CraftAtWorkshop && s.ItemId == 100);
        Assert.Equal(5, plan.UsedStock[1]); // 4 direct wool + 1 sub-craft ingredient
        Assert.True(plan.IsComplete);
        Assert.False(plan.Executed || plan.Acknowledged);
    }

    [Fact]
    public void Fake_full_bouftou_set_consumes_shared_stock_once_and_reuses_subcraft_surplus()
    {
        var plan = ItemCraftPlanner.PlanSet(new EquipmentSetTarget(999, "Bouftou FAKE"), FakeBouftou());
        Assert.Equal(3, plan.Targets.Count);
        Assert.True(plan.SetMembershipVerified);
        Assert.Equal(7, plan.UsedStock[1]); // 7 observed wool shared across ALL parts
        Assert.Equal(3, Assert.Single(plan.ResourceNeeds, x =>
            x.Method == ItemCraftStepKind.Drop && x.ItemId == 1).Quantity);
        Assert.Single(plan.Steps, s => s.Kind == ItemCraftStepKind.CraftAtWorkshop && s.ItemId == 500 && s.Quantity == 5);
        Assert.Equal(2, plan.Steps.Where(s => s.Kind == ItemCraftStepKind.UsePlannedSurplus && s.ItemId == 500).Count());
        Assert.True(plan.IsComplete);
        Assert.Equal(0, plan.PlannedSurplus[500]);
        Assert.DoesNotContain(plan.Targets, x => x.ItemId == 500); // only 3 requested parts
        Assert.False(plan.Acknowledged || plan.Executed);
    }

    [Fact]
    public void One_set_multiplied_by_two_respects_shared_yields_and_quantities()
    {
        var plan = ItemCraftPlanner.PlanSet(new(999, "Fake", Quantity: 2), FakeBouftou());
        Assert.All(plan.Targets, target => Assert.Equal(2, target.Quantity));
        Assert.Equal(7, plan.UsedStock[1]);
        Assert.Equal(13, plan.Steps.Where(s => s.Kind == ItemCraftStepKind.Drop && s.ItemId == 1)
            .Sum(s => s.Quantity ?? 0));
        Assert.Equal(10, plan.Steps.Where(s => s.Kind == ItemCraftStepKind.CraftAtWorkshop && s.ItemId == 500)
            .Sum(s => s.Quantity ?? 0)); // two batches of 5 for 10 thread units
        Assert.True(plan.IsComplete);
    }

    [Fact]
    public void Duplicate_free_list_goals_are_aggregated_once_before_ingredient_expansion()
    {
        var plan = ItemCraftPlanner.PlanList(new[]
            { new ItemCraftTarget(100, 1), new ItemCraftTarget(101, 1), new ItemCraftTarget(100, 2) }, FakeBouftou());
        Assert.Equal(new[] { new ItemCraftTarget(100, 3), new ItemCraftTarget(101, 1) }, plan.Targets);
        Assert.Equal(7, plan.UsedStock[1]);
        Assert.Equal(10, plan.Steps.Where(s => s.Kind == ItemCraftStepKind.CraftAtWorkshop && s.ItemId == 500)
            .Sum(s => s.Quantity ?? 0));
        Assert.True(plan.IsComplete);
    }

    [Fact]
    public void Stock_in_two_containers_is_aggregated_globally_and_not_reused_twice()
    {
        var plan = ItemCraftPlanner.PlanList(new[] { new ItemCraftTarget(1, 6), new ItemCraftTarget(1, 2) }, C(
            stock: new[] { St("A", 1, 3, "bag"), St("B", 1, 4, "bank") },
            sources: new[] { Drop(1) }));
        Assert.Equal(8, Assert.Single(plan.Targets).Quantity);
        Assert.Equal(7, plan.UsedStock[1]);
        Assert.Single(plan.Steps, s => s.Kind == ItemCraftStepKind.Drop && s.ItemId == 1 && s.Quantity == 1);
    }

    [Fact]
    public void Unknown_recipe_output_yield_never_fabricates_count_or_farm_quantities()
    {
        var ctx = C(recipes: new[] { R(1, 500, null, I(1, 9)) }, sources: new[] { Drop(1) });
        var plan = ItemCraftPlanner.PlanItem(new(500, 3), ctx);
        Assert.Equal(3, plan.Missing[500]);
        Assert.Null(plan.Missing[1]);
        Assert.Contains(plan.Steps, x => x.ItemId == 1 && x.Quantity is null &&
            x.Kind == ItemCraftStepKind.Unresolved && x.Explanation.Contains("9 per craft"));
        Assert.Contains(plan.Steps, x => x.ItemId == 500 && x.Quantity is null &&
            x.Kind == ItemCraftStepKind.CraftAtWorkshop && x.Dependencies.Count == 1);
        Assert.DoesNotContain(plan.Steps, x => x.Kind is ItemCraftStepKind.Gather or
            ItemCraftStepKind.Drop or ItemCraftStepKind.Buy or ItemCraftStepKind.UseInventory);
        Assert.Null(plan.FullKamasEstimate);
        Assert.False(plan.IsComplete);
    }

    [Fact]
    public void Unknown_ingredient_amount_is_explicit_null_not_guesswork()
    {
        var ctx = C(recipes: new[] { R(1, 500, 1, I(1, null)) }, sources: new[] { Drop(1) });
        var plan = ItemCraftPlanner.PlanItem(new(500, 2), ctx);
        Assert.True(plan.Missing.ContainsKey(1));
        Assert.Null(plan.Missing[1]);
        Assert.Contains(plan.Steps, s => s.Kind == ItemCraftStepKind.Unresolved &&
            s.ItemId == 1 && s.Quantity is null);
        Assert.False(plan.IsComplete);
    }

    [Fact]
    public void Missing_recipe_and_no_source_cannot_be_reported_complete()
    {
        var plan = ItemCraftPlanner.PlanItem(new(456, 5), C());
        Assert.Equal(5, plan.Missing[456]);
        Assert.Single(plan.Steps, s => s.Kind == ItemCraftStepKind.Unresolved);
        Assert.Null(plan.FullSecondsEstimate);
        Assert.Null(plan.FullKamasEstimate);
    }

    [Fact]
    public void Recipe_cycles_are_detected_and_bounded_without_phantom_items()
    {
        var ctx = C(recipes: new[]
        {
            R(1, 100, 1, I(101, 2)), R(2, 101, 1, I(100, 1))
        });
        var plan = ItemCraftPlanner.PlanItem(new(100, 1), ctx);
        Assert.Contains(plan.Steps, x => x.Kind == ItemCraftStepKind.Unresolved &&
            x.Explanation.Contains("Cycle", StringComparison.OrdinalIgnoreCase));
        Assert.True(plan.Steps.Count <= 8);
        Assert.Empty(plan.PlannedSurplus);
        Assert.False(plan.IsComplete);
    }

    [Fact]
    public void Profession_level_gate_blocks_craft_even_with_verified_recipe()
    {
        var ctx = C(recipes: new[] { R(10, 100, 2, I(1, 1)) }, sources: new[] { Drop(1) },
            jobs: new Dictionary<int, int>());
        var plan = ItemCraftPlanner.PlanItem(new(100, 1), ctx);
        Assert.Contains(plan.Warnings, s => s.Contains("profession", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(plan.PlannedSurplus);
        Assert.False(plan.IsComplete);
        Assert.Equal(1, plan.Missing[100]);
    }

    [Fact]
    public void Workshop_gate_blocks_craft_but_still_displays_required_materials()
    {
        var recipe = R(10, 100, 1, I(1, 4)) with { WorkshopVerified = false };
        var plan = ItemCraftPlanner.PlanItem(new(100, 1),
            C(recipes: new[] { recipe }, sources: new[] { Drop(1) }));
        Assert.Contains(plan.Steps, x => x.Kind == ItemCraftStepKind.Drop && x.ItemId == 1 && x.Quantity == 4);
        Assert.Contains(plan.Warnings, x => x.Contains("Workshop/atelier", StringComparison.OrdinalIgnoreCase));
        Assert.False(plan.IsComplete);
    }

    [Fact]
    public void Verified_multi_yield_excess_gets_consumed_once_and_credited_as_dependency()
    {
        var plan = ItemCraftPlanner.PlanList(new[] { new ItemCraftTarget(500, 2), new ItemCraftTarget(501, 1) },
            C(recipes: new[] { R(1, 500, 5, I(1, 1)), R(2, 501, 1, I(500, 2)) },
                sources: new[] { Drop(1) }));
        Assert.Single(plan.Steps, s => s.Kind == ItemCraftStepKind.CraftAtWorkshop && s.ItemId == 500);
        Assert.Contains(plan.Steps, s => s.Kind == ItemCraftStepKind.UsePlannedSurplus && s.ItemId == 500 && s.Quantity == 2);
        Assert.Equal(1, plan.PlannedSurplus[500]);
        Assert.True(plan.IsComplete);
        var reused = Assert.Single(plan.Steps, s => s.Kind == ItemCraftStepKind.UsePlannedSurplus);
        Assert.Contains(plan.Steps, s => s.Id == Assert.Single(reused.Dependencies) &&
            s.Kind == ItemCraftStepKind.CraftAtWorkshop);
    }

    [Fact]
    public void Set_without_catalog_but_manual_member_list_is_planned_and_marked_unverified()
    {
        var plan = ItemCraftPlanner.PlanSet(new(10000, "User-input fake set",
            new long[] { 1, 1, 2 }), C(stock: new[] { St("s", 1, 2), St("b", 2, 1) }));
        Assert.Equal(new[] { new ItemCraftTarget(1, 2), new ItemCraftTarget(2, 1) }, plan.Targets);
        Assert.False(plan.SetMembershipVerified);
        Assert.False(plan.IsComplete);
        Assert.Contains(plan.Warnings, x => x.Contains("UNVERIFIED", StringComparison.Ordinal));
        Assert.Equal(2, plan.UsedStock[1]);
    }

    [Fact]
    public void Set_without_members_or_catalog_returns_no_fictitious_items()
    {
        var plan = ItemCraftPlanner.PlanSet(new(999999, "Not in catalog"), C());
        Assert.Empty(plan.Targets);
        Assert.False(plan.IsComplete);
        Assert.Contains(plan.Warnings, x => x.Contains("membership unknown", StringComparison.Ordinal));
    }

    [Fact]
    public void Multiple_recipes_require_explicit_recipe_choice()
    {
        var recipes = new[] { R(1, 500, 1, I(1, 1)), R(2, 500, 1, I(2, 1)) };
        var sources = new[] { Drop(1), Drop(2) };
        var unchosen = ItemCraftPlanner.PlanItem(new(500, 1), C(recipes: recipes, sources: sources));
        Assert.Single(unchosen.Steps, s => s.Kind == ItemCraftStepKind.Unresolved);
        var selected = ItemCraftPlanner.PlanItem(new(500, 1), C(recipes: recipes,
            sources: sources, recipesChosen: new Dictionary<long, long> { [500] = 2 }));
        Assert.Contains(selected.Steps, x => x.RecipeId == 2);
        Assert.DoesNotContain(selected.Steps, x => x.ItemId == 1);
    }

    [Fact]
    public void Multiple_sources_compare_only_if_every_option_has_verified_time_price_and_capacity()
    {
        var sources = new[]
        {
            Drop(1, 30, 0), new AcquisitionSource(1, ItemAcquisitionKind.Buy,
                "fake-hdv", Version, true, 5, 3)
        };
        var plan = ItemCraftPlanner.PlanItem(new(1, 2),
            C(sources: sources, weights: new CraftOptimizationWeights(1, 1)));
        Assert.Single(plan.Steps, s => s.Kind == ItemCraftStepKind.Buy);
        Assert.Equal(10, plan.FullKamasEstimate);
        Assert.Equal(6L, plan.FullSecondsEstimate);

        var missing = ItemCraftPlanner.PlanItem(new(1, 2),
            C(sources: sources.Select(x => x.Kind == ItemAcquisitionKind.Buy
                    ? x with { UnitKamas = null } : x).ToArray()));
        Assert.Contains(missing.Steps, x => x.Kind == ItemCraftStepKind.Unresolved);
        Assert.Null(missing.FullKamasEstimate);
    }

    [Fact]
    public void Explicit_buy_without_observed_price_has_unknown_cost_not_zero_or_order_ack()
    {
        var plan = ItemCraftPlanner.PlanItem(new(1, 3), C(sources: new[]
        {
            new AcquisitionSource(1, ItemAcquisitionKind.Buy, "test-hdv", Version, true, null, null)
        }));
        Assert.Single(plan.Steps, x => x.Kind == ItemCraftStepKind.Buy && x.Quantity == 3);
        Assert.Null(plan.FullKamasEstimate);
        Assert.Null(plan.FullSecondsEstimate);
        Assert.False(plan.Executed);
        Assert.False(plan.Acknowledged);
    }

    [Fact]
    public void Unverified_inventory_or_sources_are_not_counted_as_available()
    {
        var plan = ItemCraftPlanner.PlanItem(new(1, 4), C(stock: new[]
        {
            St("unsafe", 1, 100, verified: false)
        }, sources: new[] { Drop(1) with { Verified = false } }));
        Assert.False(plan.UsedStock.ContainsKey(1));
        Assert.Equal(4, plan.Missing[1]);
        Assert.False(plan.IsComplete);
    }

    [Fact]
    public void Source_capacity_is_global_across_targets_and_reports_overflow_as_missing()
    {
        var plan = ItemCraftPlanner.PlanList(new[]
        {
            new ItemCraftTarget(1, 4), new ItemCraftTarget(2, 1)
        }, C(recipes: new[] { R(20, 2, 1, I(1, 4)) },
            sources: new[] { Drop(1, capacity: 5) }));
        Assert.Contains(plan.Steps, x => x.Kind == ItemCraftStepKind.Unresolved && x.ItemId == 1);
        Assert.Equal(3, plan.Missing[1]);
        Assert.False(plan.IsComplete);
    }

    [Fact]
    public void Quantities_and_graph_size_are_bounded_and_overflow_fails_closed()
    {
        Assert.Throws<ArgumentException>(() => ItemCraftPlanner.PlanItem(new(1, 1_000_000_001), C()));
        Assert.ThrowsAny<Exception>(() => ItemCraftPlanner.PlanList(new[]
        { new ItemCraftTarget(1, 1_000_000_000), new ItemCraftTarget(1, 1_000_000_000) }, C()));
        Assert.ThrowsAny<Exception>(() => ItemCraftPlanner.PlanItem(new(500, 1_000_000_000),
            C(recipes: new[] { R(1, 500, 1, I(1, 1_000_000_000)) }, sources: new[] { Drop(1) })));
        Assert.Throws<ArgumentException>(() => ItemCraftPlanner.PlanItem(new(1, 1),
            C(stock: new[] { St("same", 1, 1), St("same", 1, 1) })));
    }

    [Fact]
    public void Graph_has_deterministic_order_and_all_dependencies_appear_before_consumers()
    {
        var first = ItemCraftPlanner.PlanSet(new(999, "Fake"), FakeBouftou());
        var second = ItemCraftPlanner.PlanSet(new(999, "Fake"), FakeBouftou());
        static string Fingerprint(ItemCraftStep x) =>
            $"{x.Id}/{x.Kind}/{x.ItemId}/{x.Quantity}/{x.RecipeId}/{x.SourceVersion}/" +
            string.Join(",", x.Dependencies);
        Assert.Equal(first.Steps.Select(Fingerprint), second.Steps.Select(Fingerprint));
        Assert.All(first.Steps, step => Assert.All(step.Dependencies, id => Assert.True(id < step.Id)));
        Assert.Equal(first.UsedStock, second.UsedStock);
        Assert.Equal(first.Missing, second.Missing);
    }
}
