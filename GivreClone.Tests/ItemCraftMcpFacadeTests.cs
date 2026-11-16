using System.Text.Json;
using GivreClone.App.Services.ItemCraftMcp;
using GivreClone.App.Services.ItemCraftPlanner;
using Xunit;

namespace GivreClone.Tests;

/// <summary>All items, set contents, sources and recipes are FICTITIOUS test data.</summary>
public sealed class ItemCraftMcpFacadeTests
{
    private const string Version = "3.7.10.10";
    private const string Item = "{\"op\":\"plan_item\",\"itemGid\":101}";
    private const string Set = "{\"op\":\"plan_set\",\"setId\":999}";
    private const string Materials = "{\"op\":\"materials\",\"kind\":\"set\",\"setId\":999}";
    private const string Steps = "{\"op\":\"steps\",\"kind\":\"set\",\"setId\":999}";
    private static readonly string Hash = new('A', 64);
    private static ItemCraftMcpEvidence Evidence(string kind = "local-test", bool authorized = true) =>
        new(kind, "fixture-recipe-catalog", Version, Hash, authorized,
            authorized ? "independent-permission-fixture" : null);

    private static ItemCraftMcpTarget Target(long id, ItemCraftMcpEvidence? p = null,
        bool verified = true) =>
        new(ItemCraftMcpTargetKind.Item, id, "FICTIVE item " + id, [],
            p ?? Evidence(), verified);

    private static ItemCraftMcpTarget SetTarget(ItemCraftMcpEvidence? p = null,
        params long[] pieces) =>
        new(ItemCraftMcpTargetKind.Set, 999, "FICTIVE equipment set",
            pieces.Length == 0 ? [101, 102] : pieces, p ?? Evidence(), true);

    private static CraftRecipe Recipe(long recipeId, long product, long? output,
        params CraftIngredient[] ingredients) =>
        new(recipeId, product, output, ingredients, Version, true,
            JobId: 5, RequiredLevel: 20, KamasPerCraft: 0,
            SecondsPerCraft: 3, RequiresWorkshop: true, WorkshopVerified: true);

    private static ItemCraftContext Context(
        IReadOnlyList<CraftRecipe>? recipes = null,
        IReadOnlyList<ItemStockStack>? stock = null,
        IReadOnlyList<AcquisitionSource>? sources = null,
        IReadOnlyList<EquipmentSetDefinition>? sets = null) =>
        new(recipes ?? [
                Recipe(1000, 101, 1, new CraftIngredient(1, 4), new CraftIngredient(500, 2)),
                Recipe(1001, 102, 1, new CraftIngredient(1, 3), new CraftIngredient(500, 2)),
                Recipe(1002, 500, 5, new CraftIngredient(1, 1))
            ],
            stock ?? [new ItemStockStack("stock-fictive", 1, 5, "bag")],
            sources ?? [new AcquisitionSource(1, ItemAcquisitionKind.Drop, "fake-drop-1",
                Version, true, UnitKamas: 0, UnitSeconds: 5)],
            sets ?? [new EquipmentSetDefinition(999, "FICTIVE equipment set", [101, 102],
                Version, true)],
            new Dictionary<int, int> { [5] = 30 });

    private static ItemCraftMcpSnapshot Snapshot(
        ItemCraftMcpEvidence? evidence = null,
        ItemCraftContext? context = null,
        IReadOnlyCollection<ItemCraftMcpTarget>? targets = null,
        bool inventoryVerified = true,
        bool jobsVerified = true) =>
        new(evidence ?? Evidence(),
            targets ?? [Target(101), Target(102), Target(500), SetTarget()],
            context: context ?? Context(),
            inventoryVerified: inventoryVerified, jobLevelsVerified: jobsVerified);

    private static JsonDocument Read(string request, ItemCraftMcpSnapshot? snapshot = null) =>
        JsonDocument.Parse(ItemCraftMcpFacade.QueryJson(request, snapshot ?? Snapshot()));
    private static string Status(JsonDocument result) => result.RootElement.GetProperty("status").GetString()!;

    [Fact]
    public void Capabilities_do_not_access_client_and_declares_all_six_safe_operations()
    {
        using var result = JsonDocument.Parse(ItemCraftMcpFacade.QueryJson("{\"op\":\"capabilities\"}", null));
        Assert.Equal("KNOWN", Status(result));
        Assert.Equal(6, result.RootElement.GetProperty("operations").GetArrayLength());
        Assert.True(result.RootElement.GetProperty("readOnly").GetBoolean());
        Assert.False(result.RootElement.GetProperty("executed").GetBoolean());
        Assert.False(result.RootElement.GetProperty("clientContacted").GetBoolean());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("{\"op\":\"list_targets\",}")]
    [InlineData("{\"op\":\"list_targets\",\"op\":\"list_targets\"}")]
    [InlineData("{\"op\":\"run_lua\"}")]
    [InlineData("{\"op\":\"capabilities\",\"quantity\":2}")]
    [InlineData("{\"op\":\"plan_item\",\"itemGid\":101,\"setId\":999}")]
    [InlineData("{\"op\":\"plan_set\",\"setId\":999,\"kind\":\"set\"}")]
    [InlineData("{\"op\":\"plan_item\",\"itemGid\":101,\"execute\":true}")]
    [InlineData("{\"op\":\"plan_item\",\"itemGid\":\"101\"}")]
    [InlineData("{\"op\":\"plan_item\",\"itemGid\":101,\"quantity\":0}")]
    [InlineData("{\"op\":\"plan_item\",\"itemGid\":101,\"quantity\":1000000001}")]
    [InlineData("{\"op\":\"materials\",\"kind\":\"item\",\"setId\":999}")]
    [InlineData("{\"op\":\"steps\",\"kind\":\"untrusted\",\"itemGid\":101}")]
    [InlineData("{\"op\":\"materials\",\"kind\":\"set\",\"setId\":999,\"limit\":33}")]
    [InlineData("{\"op\":\"steps\",\"kind\":\"set\",\"setId\":999,\"offset\":2049}")]
    [InlineData("{\"op\":\"list_targets\",\"kind\":\"weapon\",\"query\":\"test\"}")]
    [InlineData("{\"op\":\"list_targets\",\"itemGid\":101}")]
    [InlineData("{\"op\":\"plan_item\",\"itemGid\":101,\"offset\":1}")]
    public void Parser_rejects_invalid_or_command_like_requests(string json)
    {
        using var result = Read(json);
        Assert.Equal("INVALID_REQUEST", Status(result));
        Assert.False(result.RootElement.GetProperty("executed").GetBoolean());
    }

    [Fact]
    public void Request_utf8_byte_limit_is_enforced()
    {
        using var result = Read("{\"op\":\"list_targets\",\"query\":\"" +
            new string('é', 3000) + "\"}");
        Assert.Equal("INVALID_REQUEST", Status(result));
    }

    [Fact]
    public void Missing_dataset_or_target_is_unknown_without_leaking_names()
    {
        using var absent = JsonDocument.Parse(ItemCraftMcpFacade.QueryJson(Item, null));
        using var missing = Read("{\"op\":\"plan_item\",\"itemGid\":888}");
        Assert.Equal("UNKNOWN", Status(absent));
        Assert.Equal("UNKNOWN", Status(missing));
        Assert.DoesNotContain("fixture-recipe-catalog", missing.RootElement.ToString());
    }

    [Fact]
    public void List_targets_exposes_only_reviewed_labels_with_pagination()
    {
        using var result = Read("{\"op\":\"list_targets\",\"kind\":\"set\",\"limit\":1}");
        Assert.Equal("KNOWN", Status(result));
        Assert.Equal(1, result.RootElement.GetProperty("total").GetInt32());
        var target = result.RootElement.GetProperty("targets")[0];
        Assert.Equal("VERIFIED", target.GetProperty("status").GetString());
        Assert.Equal(2, target.GetProperty("pieceGids").GetArrayLength());
        Assert.Equal(Hash, target.GetProperty("provenance").GetProperty("manifestSha256").GetString());
    }

    [Fact]
    public void Catalog_filters_names_without_creating_or_inferring_targets()
    {
        using var result = Read("{\"op\":\"list_targets\",\"query\":\"NOT-EXISTING\"}");
        Assert.Equal("UNKNOWN", Status(result));
        Assert.Equal(0, result.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public void Denied_rights_invalid_hash_or_missing_approval_never_disclose()
    {
        foreach (var proof in new[]
        {
            Evidence(authorized: false),
            Evidence() with { ManifestSha256 = "bad" },
            Evidence() with { AuthorizationEvidenceId = null },
            Evidence() with { GameVersion = "3.7.10" }
        })
        {
            using var doc = Read(Item, Snapshot(evidence: proof));
            Assert.Equal("UNKNOWN", Status(doc));
            Assert.DoesNotContain("FICTIVE", doc.RootElement.ToString());
        }
    }

    [Theory]
    [InlineData("dofusdb")]
    [InlineData("Dofus-DB")]
    [InlineData("DOFUS_DB")]
    [InlineData("dofus.db")]
    public void Dofusdb_is_denied_even_if_generic_permission_flag_is_set(string origin)
    {
        var evidence = Evidence(kind: origin);
        using var doc = Read(Item, Snapshot(evidence: evidence));
        Assert.Equal("UNKNOWN", Status(doc));
        Assert.DoesNotContain("dofus", doc.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unapproved_target_record_denies_whole_snapshot()
    {
        var targets = new[] { Target(101), Target(102, Evidence(authorized: false)), SetTarget() };
        using var doc = Read("{\"op\":\"list_targets\"}", Snapshot(targets: targets));
        Assert.Equal("UNKNOWN", Status(doc));
        Assert.DoesNotContain("fixture", doc.RootElement.ToString());
    }

    [Fact]
    public void Mismatched_recipe_source_version_denies_plan_and_catalog()
    {
        var ctx = Context(recipes: [Recipe(1, 101, 1) with { SourceVersion = "3.7.9.9" }]);
        using var doc = Read(Item, Snapshot(context: ctx));
        Assert.Equal("UNKNOWN", Status(doc));
        Assert.DoesNotContain("3.7.9.9", doc.RootElement.ToString());
    }

    [Fact]
    public void A_hidden_dofusdb_acquisition_source_cannot_be_laundered_into_plan()
    {
        var ctx = Context(sources: [new AcquisitionSource(1, ItemAcquisitionKind.Drop,
            "dofus.db-source", Version, true, UnitKamas: 0, UnitSeconds: 5)]);
        using var doc = Read(Item, Snapshot(context: ctx));
        Assert.Equal("UNKNOWN", Status(doc));
    }

    [Fact]
    public void Single_item_uses_real_recursive_engine_without_simulating_game_actions()
    {
        using var doc = Read(Item);
        Assert.Equal("KNOWN", Status(doc));
        Assert.Equal(101, doc.RootElement.GetProperty("target").GetProperty("id").GetInt64());
        Assert.False(doc.RootElement.GetProperty("executed").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("acknowledged").GetBoolean());
        Assert.Equal("UNKNOWN", doc.RootElement.GetProperty("routeStatus").GetString());
        Assert.Empty(doc.RootElement.GetProperty("routeLinks").EnumerateArray());
    }

    [Fact]
    public void Verified_set_shares_stock_and_subcraft_surplus_between_items()
    {
        using var doc = Read(Materials);
        Assert.Equal("KNOWN", Status(doc));
        var material = doc.RootElement.GetProperty("materials").EnumerateArray()
            .Single(m => m.GetProperty("itemGid").GetInt64() == 1);
        Assert.Equal(8, material.GetProperty("required").GetInt64());
        Assert.Equal(5, material.GetProperty("owned").GetInt64());
        Assert.Equal(3, material.GetProperty("missing").GetInt64());
        Assert.Equal("DROP", material.GetProperty("method").GetString());
    }

    [Fact]
    public void Set_quantities_multiply_every_piece_and_keep_verified_stock_reservation_once()
    {
        using var doc = Read("{\"op\":\"materials\",\"kind\":\"set\",\"setId\":999,\"quantity\":2}");
        Assert.Equal("KNOWN", Status(doc));
        var material = doc.RootElement.GetProperty("materials").EnumerateArray()
            .Single(m => m.GetProperty("itemGid").GetInt64() == 1);
        Assert.True(material.GetProperty("required").GetInt64() > 8);
        Assert.Equal(5, material.GetProperty("owned").GetInt64());
    }

    [Fact]
    public void Multi_level_steps_are_dependency_ordered_and_paginated()
    {
        using var doc = Read("{\"op\":\"steps\",\"kind\":\"set\",\"setId\":999,\"limit\":1,\"offset\":1}");
        Assert.Equal("KNOWN", Status(doc));
        Assert.Equal(1, doc.RootElement.GetProperty("returned").GetInt32());
        Assert.True(doc.RootElement.GetProperty("total").GetInt32() > 1);
        Assert.True(doc.RootElement.GetProperty("hasMore").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("runtimeCertified").GetBoolean());
    }

    [Fact]
    public void Unknown_inventory_snapshot_never_credits_inferred_bag_or_bank_stock()
    {
        using var doc = Read(Materials, Snapshot(inventoryVerified: false));
        Assert.Equal("UNKNOWN", Status(doc));
        var mat = doc.RootElement.GetProperty("materials").EnumerateArray()
            .Single(m => m.GetProperty("itemGid").GetInt64() == 1);
        Assert.Equal(JsonValueKind.Null, mat.GetProperty("owned").ValueKind);
        Assert.Equal(JsonValueKind.Null, mat.GetProperty("missing").ValueKind);
    }

    [Fact]
    public void One_unverified_stock_row_invalidates_the_whole_inventory_receipt()
    {
        var ctx = Context(stock: [
            new ItemStockStack("verified", 1, 5, "bag", true),
            new ItemStockStack("stale", 1, 50, "bank", false)
        ]);
        var snapshot = Snapshot(context: ctx, inventoryVerified: true);
        Assert.False(snapshot.InventoryVerified);
        Assert.Empty(snapshot.Context!.Inventory);
        using var result = Read(Materials, snapshot);
        Assert.Equal("UNKNOWN", Status(result));
    }

    [Fact]
    public void Unsafe_external_source_identity_is_refused_before_admission()
    {
        var context = Context(sources: [
            new AcquisitionSource(1, ItemAcquisitionKind.Drop,
                "C:\\secret\\token.txt", Version, true)
        ]);
        Assert.Throws<ArgumentException>(() => Snapshot(context: context));
    }

    [Fact]
    public void Job_level_not_verified_keeps_craft_partial()
    {
        using var doc = Read(Item, Snapshot(jobsVerified: false));
        Assert.Equal("UNKNOWN", Status(doc));
        Assert.True(doc.RootElement.GetProperty("partial").GetBoolean());
    }

    [Fact]
    public void Unknown_ingredient_quantity_remains_null_and_plan_partial()
    {
        var recipes = new[] { Recipe(1000, 101, 1, new CraftIngredient(1, null)) };
        using var doc = Read("{\"op\":\"materials\",\"kind\":\"item\",\"itemGid\":101}",
            Snapshot(context: Context(recipes: recipes)));
        Assert.Equal("UNKNOWN", Status(doc));
        var material = doc.RootElement.GetProperty("materials").EnumerateArray()
            .Single(m => m.GetProperty("itemGid").GetInt64() == 1);
        Assert.Equal(JsonValueKind.Null, material.GetProperty("required").ValueKind);
        Assert.Equal(JsonValueKind.Null, material.GetProperty("missing").ValueKind);
    }

    [Fact]
    public void Missing_recipe_yields_unknown_with_unresolved_step()
    {
        var context = Context(recipes: [], sources: []);
        using var doc = Read("{\"op\":\"steps\",\"kind\":\"item\",\"itemGid\":101}",
            Snapshot(context: context));
        Assert.Equal("UNKNOWN", Status(doc));
        Assert.Contains(doc.RootElement.GetProperty("steps").EnumerateArray(),
            x => x.GetProperty("method").GetString() == "UNKNOWN");
    }

    [Fact]
    public void Recipe_cycle_bounded_unknown_without_false_route()
    {
        var cyclic = Context(recipes: [
            Recipe(11, 101, 1, new CraftIngredient(102, 2)),
            Recipe(12, 102, 1, new CraftIngredient(101, 1))
        ], sources: []);
        using var doc = Read(Item, Snapshot(context: cyclic));
        Assert.Equal("UNKNOWN", Status(doc));
        Assert.True(doc.RootElement.GetProperty("stepCount").GetInt32() <= 8);
        Assert.Empty(doc.RootElement.GetProperty("routeLinks").EnumerateArray());
    }

    [Fact]
    public void Set_with_missing_piece_catalog_is_unknown_even_with_recipe_data()
    {
        using var doc = Read(Set,
            Snapshot(targets: [Target(101), SetTarget()]));
        Assert.Equal("UNKNOWN", Status(doc));
        Assert.Equal("UNKNOWN", doc.RootElement.GetProperty("target").GetProperty("status").GetString());
    }

    [Fact]
    public void Invalid_target_data_and_duplicate_ids_fail_fast()
    {
        Assert.Throws<ArgumentException>(() => Snapshot(targets: [Target(101), Target(101)]));
        Assert.Throws<ArgumentException>(() => Snapshot(targets: [
            SetTarget(pieces: [101, 101])]));
        var many = Enumerable.Range(1, ItemCraftMcpSnapshot.MaxTargets + 1)
            .Select(i => Target(i)).ToArray();
        Assert.Throws<ArgumentException>(() => Snapshot(targets: many));
    }

    [Fact]
    public void Mutating_input_targets_and_recipe_list_cannot_change_admitted_snapshot()
    {
        var members = new List<long> { 101, 102 };
        var pieces = new List<CraftIngredient> { new(1, 4) };
        var recipes = new List<CraftRecipe> { Recipe(1000, 101, 1, pieces.ToArray()) with
            { Ingredients = pieces } };
        var input = Context(recipes: recipes);
        var snapshot = Snapshot(context: input,
            targets: [Target(101), Target(102), SetTarget(pieces: members.ToArray())]);
        members.Clear();
        pieces[0] = new CraftIngredient(1, 999);
        recipes.Clear();
        Assert.Equal(2, snapshot.Sets[999].PieceGids.Count);
        Assert.Equal(4, snapshot.Context!.Recipes[0].Ingredients[0].Quantity);
    }

    private static ItemCraftMcpRecipeRow LocalRecipe(string key, long result,
        long? yield = 1, params CraftIngredient[] ingredients) =>
        new(key, result, yield, ingredients, Version, true,
            JobId: 5, RequiredLevel: 20, KamasPerCraft: 0, SecondsPerCraft: 3,
            RequiresWorkshop: true, WorkshopVerified: true);

    private static ItemCraftMcpSnapshot Indexed(
        IReadOnlyCollection<ItemCraftMcpRecipeRow> recipes,
        IReadOnlyCollection<ItemCraftMcpTarget>? targets = null,
        ItemCraftMcpEvidence? catalogProof = null,
        ItemCraftContext? context = null,
        bool verifiedStock = true, bool verifiedJobs = true) =>
        new(Evidence(), targets ?? [Target(101), Target(102), Target(500), SetTarget()],
            context: context ?? Context(recipes: []),
            inventoryVerified: verifiedStock,
            jobLevelsVerified: verifiedJobs,
            recipeCatalog: new ItemCraftMcpRecipeCatalog(catalogProof ?? Evidence(), recipes));

    [Fact]
    public void Large_catalog_4858_recipes_can_plan_a_target_near_the_end()
    {
        var recipes = Enumerable.Range(0, 4858)
            .Select(n => LocalRecipe("00000000-0000-0000-0000-" + n.ToString("x12"),
                100000 + n, 1, new CraftIngredient(1, 2))).ToArray();
        var snapshot = Indexed(recipes, targets: [Target(104857)]);
        Assert.Equal(4858, snapshot.RecipeCatalog!.Recipes.Count);
        using var result = Read("{\"op\":\"plan_item\",\"itemGid\":104857}", snapshot);
        Assert.Equal("KNOWN", Status(result));
        Assert.Equal(2, result.RootElement.GetProperty("stepCount").GetInt32());
        using var steps = Read("{\"op\":\"steps\",\"kind\":\"item\",\"itemGid\":104857}", snapshot);
        var craft = steps.RootElement.GetProperty("steps").EnumerateArray().Single(s =>
            s.GetProperty("method").GetString() == "CRAFT");
        Assert.Equal("00000000-0000-0000-0000-0000000012f9",
            craft.GetProperty("localRecipeKey").GetString());
        Assert.Equal(JsonValueKind.Null, craft.GetProperty("recipeId").ValueKind);
        Assert.False(steps.RootElement.GetProperty("executed").GetBoolean());
    }

    [Fact]
    public void UUID_local_keys_are_deterministic_independent_of_catalog_order()
    {
        var a = LocalRecipe("88e17c1d-46c4-45da-9ed5-a", 101, 1,
            new CraftIngredient(1, 1));
        var b = LocalRecipe("88e17c1d-46c4-45da-9ed5-b", 500, 1,
            new CraftIngredient(1, 1));
        var first = new ItemCraftMcpRecipeCatalog(Evidence(), [a, b]);
        var reverse = new ItemCraftMcpRecipeCatalog(Evidence(), [b, a]);
        Assert.True(first.TryClosure([101], out var left));
        Assert.True(reverse.TryClosure([101], out var right));
        Assert.Equal(left.Select(x => x.RecipeId), right.Select(x => x.RecipeId));
        Assert.Equal(a.LocalRecipeKey, first.GetLocalKey(left[0].RecipeId));
    }

    [Fact]
    public void Set_computes_shared_closure_for_all_members_not_a_prefix()
    {
        var recipes = Enumerable.Range(0, 4856)
            .Select(n => LocalRecipe("background-" + n, 200000 + n, 1))
            .Concat([
                LocalRecipe("uuid-hat", 101, 1, new CraftIngredient(500, 2)),
                LocalRecipe("uuid-coat", 102, 1, new CraftIngredient(500, 1)),
                LocalRecipe("uuid-thread", 500, 5, new CraftIngredient(1, 1))
            ]).ToArray();
        var snapshot = Indexed(recipes);
        using var result = Read(Set, snapshot);
        Assert.Equal("KNOWN", Status(result));
        using var steps = Read(Steps, snapshot);
        Assert.Contains(steps.RootElement.GetProperty("steps").EnumerateArray(),
            s => s.GetProperty("localRecipeKey").GetString() == "uuid-thread");
        Assert.True(result.RootElement.GetProperty("stepCount").GetInt32() >= 4);
    }

    [Fact]
    public void Unknown_recipe_yield_remains_unknown_and_does_not_expand_ingredients()
    {
        var snapshot = Indexed([LocalRecipe("uuid-yield-unknown", 101, null,
            new CraftIngredient(700, 20))]);
        using var summary = Read(Item, snapshot);
        Assert.Equal("UNKNOWN", Status(summary));
        using var materials = Read("{\"op\":\"materials\",\"kind\":\"item\",\"itemGid\":101}", snapshot);
        // The engine may disclose the sourced ingredient as UNKNOWN, but it
        // must never infer 20 units without a verified output yield.
        foreach (var m in materials.RootElement.GetProperty("materials").EnumerateArray())
        {
            if (m.GetProperty("itemGid").GetInt64() != 700) continue;
            Assert.Equal(JsonValueKind.Null, m.GetProperty("required").ValueKind);
            Assert.Equal(JsonValueKind.Null, m.GetProperty("missing").ValueKind);
        }
        Assert.False(materials.RootElement.GetProperty("acknowledged").GetBoolean());
    }

    [Fact]
    public void Missing_ingredient_quantity_remains_null_after_UUID_import()
    {
        var snapshot = Indexed([LocalRecipe("uuid-unknown-ingredient", 101, 1,
            new CraftIngredient(700, null))]);
        using var materials = Read("{\"op\":\"materials\",\"kind\":\"item\",\"itemGid\":101}", snapshot);
        Assert.Equal("UNKNOWN", Status(materials));
        var entry = materials.RootElement.GetProperty("materials")[0];
        Assert.Equal(700, entry.GetProperty("itemGid").GetInt64());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("required").ValueKind);
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("missing").ValueKind);
    }

    [Fact]
    public void Absent_profession_metadata_cannot_turn_source_unknown_into_certified_plan()
    {
        var row = LocalRecipe("uuid-no-job-extracted", 101, 1,
            new CraftIngredient(1, 2)) with { JobId = null, RequiredLevel = null };
        var snapshot = Indexed([row]);
        using var result = Read(Item, snapshot);
        Assert.Equal("UNKNOWN", Status(result));
        Assert.True(result.RootElement.GetProperty("partial").GetBoolean());
        using var steps = Read("{\"op\":\"steps\",\"kind\":\"item\",\"itemGid\":101}", snapshot);
        Assert.Contains(steps.RootElement.GetProperty("steps").EnumerateArray(),
            s => s.GetProperty("itemGid").GetInt64() == 101 &&
                s.GetProperty("status").GetString() == "UNKNOWN");
    }

    [Fact]
    public void More_than_4000_reachable_recipes_refuses_whole_closure()
    {
        var rows = Enumerable.Range(0, 4001)
            .Select(n => LocalRecipe("candidate-" + n, 101, 1,
                new CraftIngredient(1, 1))).ToArray();
        var snapshot = Indexed(rows);
        using var result = Read(Item, snapshot);
        Assert.Equal("UNKNOWN", Status(result));
        Assert.False(result.RootElement.TryGetProperty("stepCount", out _));
        Assert.DoesNotContain("candidate-", result.RootElement.ToString());
    }

    [Fact]
    public void Entire_global_index_is_never_passed_to_engine_for_one_target()
    {
        var rows = Enumerable.Range(0, 4800)
            .Select(n => LocalRecipe("unrelated-" + n, 100000 + n, 1)).ToList();
        rows.Add(LocalRecipe("uuid-root", 101, 1, new CraftIngredient(1, 1)));
        var snapshot = Indexed(rows);
        Assert.True(snapshot.RecipeCatalog!.TryClosure([101], out var closure));
        Assert.Single(closure);
        Assert.Equal("uuid-root", snapshot.RecipeCatalog.GetLocalKey(closure[0].RecipeId));
        using var result = Read(Item, snapshot);
        Assert.Equal("KNOWN", Status(result));
    }

    [Fact]
    public void Undisclosed_or_version_mismatched_large_catalog_never_leaks()
    {
        var data = new[] { LocalRecipe("secret-uuid", 101) };
        var denied = Indexed(data, catalogProof: Evidence(authorized: false));
        using var first = Read(Item, denied);
        Assert.Equal("UNKNOWN", Status(first));
        Assert.DoesNotContain("secret", first.RootElement.ToString());
        var wrong = Indexed([data[0] with { SourceVersion = "3.7.99.99" }]);
        using var second = Read(Item, wrong);
        Assert.Equal("UNKNOWN", Status(second));
        Assert.DoesNotContain("secret", second.RootElement.ToString());
    }

    [Fact]
    public void Public_DofusDB_labeled_catalogue_stays_forbidden()
    {
        var snapshot = Indexed([LocalRecipe("uuid-1", 101)],
            catalogProof: Evidence(kind: "DOFUS-DB"));
        using var result = Read(Item, snapshot);
        Assert.Equal("UNKNOWN", Status(result));
        Assert.DoesNotContain("uuid", result.RootElement.ToString());
        Assert.Throws<ArgumentException>(() => Indexed(
            [LocalRecipe("DOFUS.DB:partner-record", 101)]));
    }

    [Fact]
    public void Duplicate_UUID_case_variants_and_malformed_local_keys_are_rejected()
    {
        var row = LocalRecipe("abcdef00-0000-0000-0000-000000000001", 101);
        Assert.Throws<ArgumentException>(() => new ItemCraftMcpRecipeCatalog(Evidence(),
            [row, row with { LocalRecipeKey = row.LocalRecipeKey.ToUpperInvariant() }]));
        Assert.Throws<ArgumentException>(() => Indexed(
            [LocalRecipe("../../private.pem", 101)]));
        Assert.Throws<ArgumentException>(() => Indexed(
            [LocalRecipe(new string('x', 81), 101)]));
    }

    [Fact]
    public void Local_recipe_catalogue_has_explicit_global_volume_limit()
    {
        var over = Enumerable.Range(0, ItemCraftMcpRecipeCatalog.MaxCatalogRecipes + 1)
            .Select(n => LocalRecipe("id-" + n, 1000000 + n)).ToArray();
        Assert.Throws<ArgumentException>(() => new ItemCraftMcpRecipeCatalog(Evidence(), over));
    }

    [Fact]
    public void Target_index_admits_20979_items_plus_929_sets_with_no_prefix_loss()
    {
        var items = Enumerable.Range(1, 20979).Select(n => Target(n)).ToArray();
        var sets = Enumerable.Range(1, 929).Select(n =>
            new ItemCraftMcpTarget(ItemCraftMcpTargetKind.Set, n, "Synthetic set " + n,
                [n], Evidence(), true)).ToArray();
        var snapshot = new ItemCraftMcpSnapshot(Evidence(), items.Concat(sets).ToArray());
        Assert.Equal(20979, snapshot.Items.Count);
        Assert.Equal(929, snapshot.Sets.Count);
        using var result = Read("{\"op\":\"list_targets\",\"kind\":\"item\",\"query\":\"FICTIVE item 20979\"}", snapshot);
        Assert.Equal("KNOWN", Status(result));
        Assert.Equal(20979, result.RootElement.GetProperty("targets")[0].GetProperty("id").GetInt64());
    }

    [Fact]
    public void Recursive_cycle_in_UUID_graph_is_reported_unknown_without_fake_ACK()
    {
        var snapshot = Indexed([
            LocalRecipe("uuid-a", 101, 1, new CraftIngredient(500, 2)),
            LocalRecipe("uuid-b", 500, 1, new CraftIngredient(101, 1))
        ]);
        using var result = Read(Item, snapshot);
        Assert.Equal("UNKNOWN", Status(result));
        Assert.True(result.RootElement.GetProperty("stepCount").GetInt32() <= 10);
        Assert.False(result.RootElement.GetProperty("acknowledged").GetBoolean());
        Assert.Equal("UNKNOWN", result.RootElement.GetProperty("routeStatus").GetString());
    }

    [Fact]
    public void Imported_rows_and_ingredient_lists_cannot_be_mutated_after_admission()
    {
        var parts = new List<CraftIngredient> { new(1, 2) };
        var rows = new List<ItemCraftMcpRecipeRow> {
            LocalRecipe("uuid-immutable", 101, 1) with { Ingredients = parts }
        };
        var snapshot = Indexed(rows);
        parts[0] = new CraftIngredient(1, 999);
        rows.Clear();
        Assert.Equal(2, snapshot.RecipeCatalog!.Recipes[0].Ingredients[0].Quantity);
        using var result = Read(Item, snapshot);
        Assert.Equal("KNOWN", Status(result));
    }

    [Fact]
    public void Mixed_context_and_indexed_recipes_cannot_silently_override_each_other()
    {
        Assert.Throws<ArgumentException>(() => Indexed([LocalRecipe("uuid-1", 101)],
            context: Context()));
    }
}
