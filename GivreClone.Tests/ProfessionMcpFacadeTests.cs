using System.Text.Json;
using GivreClone.App.Services.ProfessionMcp;
using GivreClone.App.Services.ProfessionPlanner;
using Xunit;

namespace GivreClone.Tests;

public sealed class ProfessionMcpFacadeTests
{
    private const string Version = "3.7.10.10";
    private static readonly string Digest = new('A', 64);
    private const string QueryGoal = "{\"op\":\"plan_job\",\"jobId\":28,\"startLevel\":20,\"targetLevel\":30}";
    private const string QueryMaterials = "{\"op\":\"materials\",\"jobId\":28,\"startLevel\":20,\"targetLevel\":30}";
    private const string QuerySteps = "{\"op\":\"steps\",\"jobId\":28,\"startLevel\":20,\"targetLevel\":30}";

    private static ProfessionMcpProvenance Evidence(bool grant = true, string kind = "local-owned") =>
        new(kind, "profession-test-fixture", Version, Digest, grant,
            grant ? "independent-rights-review" : null);

    private static ProfessionRecipe Training(params RecipeIngredient[] ingredients) =>
        new(5, 99, 1, ingredients, 20, JobId: 28, XpPerCraft: 50,
            XpVerified: true, SourceVersion: Version, Verified: true,
            SecondsPerCraft: 5, KamasPerCraft: 0);

    private static ProfessionPlanRequest Request(
        IReadOnlyList<ProfessionRecipe>? recipes = null,
        IReadOnlyList<MaterialSource>? sources = null,
        IReadOnlyList<ProfessionInventoryStack>? inventory = null,
        XpTable? xp = null,
        ProfessionGoal? goal = null) =>
        new(goal ?? new ProfessionGoal(28, null, 20, 30, 100, true), 5, null,
            recipes ?? [Training(new RecipeIngredient(20, 2))],
            inventory ?? Array.Empty<ProfessionInventoryStack>(),
            sources ?? [new MaterialSource(20, AcquisitionMethod.Gather, 2, 0,
                Version, Verified: true)],
            xp ?? new XpTable(Version, true, [new XpThreshold(20, 100),
                new XpThreshold(30, 200)]));

    private static ProfessionMcpSnapshot Snapshot(ProfessionPlanRequest? request = null,
        ProfessionMcpProvenance? evidence = null) =>
        new(request ?? Request(), evidence ?? Evidence());

    private static JsonDocument Query(string request = QueryGoal, ProfessionMcpSnapshot? snapshot = null) =>
        JsonDocument.Parse(ProfessionMcpFacade.QueryJson(request, snapshot ?? Snapshot()));

    private static string Status(JsonDocument document) =>
        document.RootElement.GetProperty("status").GetString()!;

    [Fact]
    public void Capabilities_require_no_profession_data_and_are_explicitly_read_only()
    {
        using var result = JsonDocument.Parse(ProfessionMcpFacade.QueryJson(
            "{\"op\":\"capabilities\"}", null));
        Assert.Equal("KNOWN", Status(result));
        Assert.Equal(4, result.RootElement.GetProperty("operations").GetArrayLength());
        Assert.Equal(4096, result.RootElement.GetProperty("bounds")
            .GetProperty("maxRequestBytes").GetInt32());
        Assert.True(result.RootElement.GetProperty("readOnly").GetBoolean());
        Assert.False(result.RootElement.GetProperty("executed").GetBoolean());
        Assert.False(result.RootElement.GetProperty("clientContacted").GetBoolean());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("{\"op\":\"plan_job\",}")]
    [InlineData("{\"op\":\"plan_job\",\"op\":\"plan_job\"}")]
    [InlineData("{\"op\":\"run_lua\"}")]
    [InlineData("{\"op\":\"capabilities\",\"jobId\":28}")]
    [InlineData("{\"op\":\"steps\",\"jobId\":28,\"startLevel\":20,\"targetLevel\":30,\"script\":\"move()\"}")]
    [InlineData("{\"op\":\"plan_job\",\"jobId\":\"28\",\"startLevel\":20,\"targetLevel\":30}")]
    [InlineData("{\"op\":\"plan_job\",\"jobId\":28,\"startLevel\":30,\"targetLevel\":20}")]
    [InlineData("{\"op\":\"plan_job\",\"jobId\":28,\"startLevel\":0,\"targetLevel\":30}")]
    [InlineData("{\"op\":\"plan_job\",\"jobId\":28,\"startLevel\":20,\"targetLevel\":201}")]
    [InlineData("{\"op\":\"plan_job\",\"jobId\":28,\"jobName\":\"Bijoutier\",\"startLevel\":20,\"targetLevel\":30}")]
    [InlineData("{\"op\":\"plan_job\",\"jobId\":28,\"startLevel\":20,\"targetLevel\":30,\"limit\":1}")]
    [InlineData("{\"op\":\"steps\",\"jobId\":28,\"startLevel\":20,\"targetLevel\":30,\"limit\":41}")]
    [InlineData("{\"op\":\"materials\",\"jobId\":28,\"startLevel\":20,\"targetLevel\":30,\"offset\":2049}")]
    [InlineData("{\"op\":\"steps\",\"jobId\":28,\"startLevel\":20,\"targetLevel\":30,\"requestedCrafts\":-1}")]
    public void Invalid_schema_duplicates_commands_and_unbounded_fields_are_rejected(string json)
    {
        using var result = Query(json);
        Assert.Equal("INVALID_REQUEST", Status(result));
        Assert.False(result.RootElement.GetProperty("executed").GetBoolean());
    }

    [Fact]
    public void Too_large_or_deep_request_is_rejected()
    {
        using var large = Query("{\"op\":\"plan_job\",\"noise\":\"" + new string('é', 3000) + "\"}");
        Assert.Equal("INVALID_REQUEST", Status(large));
        using var deep = Query("{\"op\":[[[[[[[[[[[[[[\"plan_job\"]]]]]]]]]]]]]]}");
        Assert.Equal("INVALID_REQUEST", Status(deep));
    }

    [Fact]
    public void Absent_snapshot_yields_unknown_without_invented_profession()
    {
        using var result = JsonDocument.Parse(ProfessionMcpFacade.QueryJson(QueryGoal, null));
        Assert.Equal("UNKNOWN", Status(result));
        Assert.DoesNotContain("Bijoutier", result.RootElement.ToString());
    }

    [Fact]
    public void Permission_or_evidence_missing_is_fail_closed()
    {
        foreach (var provenance in new[]
        {
            Evidence(grant: false),
            Evidence() with { AuthorizationEvidenceId = null },
            Evidence() with { ManifestSha256 = "unverified" },
            Evidence() with { GameVersion = "3.7.10" }
        })
        {
            using var result = Query(QueryGoal, Snapshot(evidence: provenance));
            Assert.Equal("UNKNOWN", Status(result));
            Assert.DoesNotContain("profession-test-fixture", result.RootElement.ToString());
        }
    }

    [Theory]
    [InlineData("dofusdb")]
    [InlineData("Dofus-DB")]
    [InlineData("dofus.db")]
    [InlineData("DOFUS_DB")]
    public void Dofusdb_origin_remains_invisible_without_separate_ai_rights(string source)
    {
        using var result = Query(QueryGoal, Snapshot(evidence: Evidence(kind: source)));
        Assert.Equal("UNKNOWN", Status(result));
        Assert.DoesNotContain("dofus", result.RootElement.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mismatched_version_for_recipe_source_or_xp_denies_entire_dataset()
    {
        var variants = new[]
        {
            Request(recipes: [Training() with { SourceVersion = "3.7.9.9" }]),
            Request(sources: [new MaterialSource(20, AcquisitionMethod.Gather, 2, 0,
                "3.7.9.9", true)]),
            Request(xp: new XpTable("3.7.9.9", true,
                [new XpThreshold(20, 100), new XpThreshold(30, 200)]))
        };
        foreach (var input in variants)
        {
            using var result = Query(QueryGoal, Snapshot(input));
            Assert.Equal("UNKNOWN", Status(result));
            Assert.DoesNotContain("3.7.9.9", result.RootElement.ToString());
        }
    }

    [Fact]
    public void Valid_plan_uses_real_engine_calculations_and_manifest_evidence()
    {
        using var result = Query();
        Assert.Equal("KNOWN", Status(result));
        Assert.False(result.RootElement.GetProperty("partial").GetBoolean());
        var progress = result.RootElement.GetProperty("progress");
        Assert.Equal(100, progress.GetProperty("NeededXp").GetInt64());
        Assert.Equal(2, progress.GetProperty("PlannedCrafts").GetInt64());
        Assert.Equal(200, progress.GetProperty("PotentialFinalXp").GetInt64());
        Assert.Equal(2, result.RootElement.GetProperty("stepCount").GetInt32());
        Assert.Equal(Digest, result.RootElement.GetProperty("provenance")
            .GetProperty("manifestSha256").GetString());
        Assert.Equal("UNKNOWN", result.RootElement.GetProperty("routeStatus").GetString());
        Assert.Empty(result.RootElement.GetProperty("routeLinks").EnumerateArray());
        Assert.False(result.RootElement.GetProperty("acknowledged").GetBoolean());
    }

    [Fact]
    public void Material_quantities_are_aggregated_from_actual_acquisition_steps()
    {
        using var result = Query(QueryMaterials);
        Assert.Equal("KNOWN", Status(result));
        Assert.Equal(1, result.RootElement.GetProperty("total").GetInt32());
        var material = result.RootElement.GetProperty("materials")[0];
        Assert.Equal(20, material.GetProperty("itemGid").GetInt64());
        Assert.Equal(4, material.GetProperty("toAcquire").GetInt64());
        Assert.Equal(0, material.GetProperty("usedStock").GetInt64());
        Assert.Equal("GATHER", material.GetProperty("methods")[0].GetString());
    }

    [Fact]
    public void Existing_inventory_is_reserved_once_and_not_double_counted()
    {
        var stock = new[] { new ProfessionInventoryStack("known-1", 20, 3, "inventory") };
        using var result = Query(QueryMaterials, Snapshot(Request(inventory: stock)));
        Assert.Equal("KNOWN", Status(result));
        var material = result.RootElement.GetProperty("materials")[0];
        Assert.Equal(3, material.GetProperty("usedStock").GetInt64());
        Assert.Equal(1, material.GetProperty("toAcquire").GetInt64());
    }

    [Fact]
    public void Steps_are_dependency_ordered_and_paginated_without_running_actions()
    {
        var request = QuerySteps[..^1] + ",\"limit\":1,\"offset\":1}";
        using var result = Query(request);
        Assert.Equal(2, result.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, result.RootElement.GetProperty("steps").GetArrayLength());
        var step = result.RootElement.GetProperty("steps")[0];
        Assert.Equal("CRAFT", step.GetProperty("kind").GetString());
        Assert.Equal(1, step.GetProperty("dependencies")[0].GetInt32());
        Assert.False(result.RootElement.GetProperty("executed").GetBoolean());
        Assert.False(result.RootElement.GetProperty("acknowledged").GetBoolean());
    }

    [Fact]
    public void Missing_xp_gives_unknown_with_no_invented_crafts()
    {
        var noXp = Request(xp: new XpTable(Version, false, []));
        using var result = Query(QueryGoal, Snapshot(noXp));
        Assert.Equal("UNKNOWN", Status(result));
        Assert.Equal(JsonValueKind.Null,
            result.RootElement.GetProperty("progress").GetProperty("PlannedCrafts").ValueKind);
        Assert.Equal(0, result.RootElement.GetProperty("stepCount").GetInt32());
    }

    [Fact]
    public void Missing_training_recipe_does_not_generate_fake_ingredients()
    {
        using var result = Query(QueryGoal, Snapshot(Request(recipes: [])));
        Assert.Equal("UNKNOWN", Status(result));
        Assert.True(result.RootElement.GetProperty("partial").GetBoolean());
    }

    [Fact]
    public void Unknown_acquisition_source_reports_unresolved_instead_of_a_route()
    {
        using var result = Query(QuerySteps, Snapshot(Request(sources: [])));
        Assert.Equal("UNKNOWN", Status(result));
        Assert.Contains(result.RootElement.GetProperty("steps").EnumerateArray(),
            s => s.GetProperty("kind").GetString() == "UNRESOLVED");
        Assert.Empty(result.RootElement.GetProperty("routeLinks").EnumerateArray());
    }

    [Fact]
    public void Recursive_cycle_is_bounded_and_unresolved()
    {
        var cyclic = Request(recipes: [
            Training(new RecipeIngredient(20, 2)),
            new ProfessionRecipe(6, 20, 1, [new RecipeIngredient(99, 1)], 20,
                SourceVersion: Version, Verified: true)
        ], sources: []);
        using var result = Query(QuerySteps, Snapshot(cyclic));
        Assert.Equal("UNKNOWN", Status(result));
        Assert.Contains(result.RootElement.GetProperty("steps").EnumerateArray(),
            s => s.GetProperty("kind").GetString() == "UNRESOLVED");
    }

    [Fact]
    public void Contradictory_profession_goal_fails_without_disclosing_plan()
    {
        using var result = Query(QueryGoal.Replace("28", "29", StringComparison.Ordinal));
        Assert.Equal("UNKNOWN", Status(result));
        Assert.False(result.RootElement.TryGetProperty("progress", out _));
    }

    [Fact]
    public void Excessive_catalogue_volumes_are_rejected_before_planning()
    {
        var tooMany = Enumerable.Range(0, ProfessionMcpSnapshot.MaxInventoryStacks + 1)
            .Select(i => new ProfessionInventoryStack("stack" + i, 20, 1, "inventory")).ToArray();
        Assert.Throws<ArgumentException>(() => Snapshot(Request(inventory: tooMany)));
        Assert.Throws<ArgumentException>(() => Snapshot(Request(
            recipes: [Training() with { Ingredients = Enumerable.Repeat(
                new RecipeIngredient(20, 1), 65).ToArray() }])));
    }

    [Fact]
    public void Snapshot_defensively_copies_mutable_recipe_and_inventory_lists()
    {
        var ingredients = new List<RecipeIngredient> { new(20, 2) };
        var recipe = Training(ingredients.ToArray()) with { Ingredients = ingredients };
        var recipes = new List<ProfessionRecipe> { recipe };
        var inventory = new List<ProfessionInventoryStack>();
        var snapshot = Snapshot(Request(recipes: recipes, inventory: inventory));
        ingredients[0] = new RecipeIngredient(20, 999);
        recipes.Clear();
        inventory.Add(new ProfessionInventoryStack("injected", 20, 4, "inventory"));
        using var result = Query(QueryMaterials, snapshot);
        Assert.Equal(4, result.RootElement.GetProperty("materials")[0]
            .GetProperty("toAcquire").GetInt64());
    }
}
