using System.Security.Cryptography;
using System.Text.Json;
using GivreMcp;
using Xunit;

namespace GivreClone.Tests;

public sealed class DofusDbMcpCatalogTests
{
    private sealed class SyntheticCache : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "dofusdb-mcp-fixture-" +
            Guid.NewGuid().ToString("N"));
        public string SummaryHash { get; private set; } = "";
        public DofusDbCatalogAccess Approved =>
            new(Root, SummaryHash, true, "api_ai_mcp");

        public SyntheticCache()
        {
            Directory.CreateDirectory(Root);
            Write("items", new
            {
                id = 44, name = new { fr = "Épée de Boisaille", en = "Twiggy Sword" },
                level = 7, typeId = 6, itemSetId = 2
            });
            Write("recipes", new
            {
                resultId = 44, ingredientIds = new long[] { 16512, 303 },
                quantities = new long[] { 3, 3 }, jobId = 11
            });
            Write("item-sets", new
            {
                id = 2, name = new { fr = "Panoplie test" },
                items = new object[] { new { id = 44 }, new { id = 45 } }
            });
            UpdateSummary();
        }

        private static object SummaryCollection() => new { captured = 1, complete = true, expected = 1, pages = 1 };
        public string Page(string collection) => Path.Combine(Root, collection, "page-00000000.json");
        public string Meta(string collection) => Path.Combine(Root, collection, "page-00000000.meta.json");
        public void Write(string collection, object entry)
        {
            var path = Path.Combine(Root, collection);
            Directory.CreateDirectory(path);
            byte[] contents = JsonSerializer.SerializeToUtf8Bytes(new
            {
                data = new[] { entry }, skip = 0, limit = 50, total = 1
            });
            File.WriteAllBytes(Page(collection), contents);
            File.WriteAllText(Meta(collection), JsonSerializer.Serialize(new
            {
                category = collection, skip = 0, limit = 50, records = 1,
                total = 1, bytes = contents.Length, runtimeCertified = false,
                aiMcpAllowed = false, versionStatus = "UNKNOWN",
                sha256 = Convert.ToHexString(SHA256.HashData(contents))
            }));
        }
        public void UpdateSummary()
        {
            string file = Path.Combine(Root, "review-summary.json");
            File.WriteAllText(file, JsonSerializer.Serialize(new
            {
                complete = true, aiMcpAllowed = false, authorizedRuntimeUse = false,
                gameplayCertified = false, sourceGameVersion = "UNKNOWN",
                collections = new Dictionary<string, object>
                {
                    ["items"] = SummaryCollection(), ["recipes"] = SummaryCollection(),
                    ["item-sets"] = SummaryCollection()
                }
            }));
            SummaryHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        }
        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private static JsonElement Query(SyntheticCache data, string op, string? query = null,
        long id = 0, int offset = 0, int limit = 20) =>
        DofusDbCatalogReadTools.Query(op, data.Approved, query, id, offset, limit);

    private static string Status(JsonElement result) => result.GetProperty("status").GetString()!;

    [Fact]
    public void Capabilities_can_be_read_without_any_cache_or_runtime()
    {
        var result = DofusDbCatalogReadTools.Query("capabilities", null);
        Assert.Equal("KNOWN", Status(result));
        Assert.True(result.GetProperty("readOnly").GetBoolean());
        Assert.False(result.GetProperty("clientContacted").GetBoolean());
        Assert.Equal("UNKNOWN", result.GetProperty("gameVersion").GetString());
    }

    [Fact]
    public void Operator_flag_and_scope_and_pin_are_all_required()
    {
        using var fixture = new SyntheticCache();
        foreach (var access in new[]
        {
            fixture.Approved with { OperatorEnabledReviewOnly = false },
            fixture.Approved with { OperatorScope = null },
            fixture.Approved with { OperatorScope = "site_ai_mcp" },
            fixture.Approved with { ExpectedSummarySha256 = null },
            fixture.Approved with { ExpectedSummarySha256 = new string('A', 64) },
            fixture.Approved with { CacheDirectory = null },
        })
        {
            Assert.NotEqual("REVIEW_ONLY",
                Status(DofusDbCatalogReadTools.Query("catalog_status", access)));
        }
    }

    [Fact]
    public void Authenticated_fixture_returns_only_bounded_metadata()
    {
        using var fixture = new SyntheticCache();
        var status = Query(fixture, "catalog_status");
        Assert.Equal("REVIEW_ONLY", Status(status));
        Assert.Equal(1, status.GetProperty("items").GetInt32());
        Assert.False(status.GetProperty("source").GetProperty("rightsIndependentlyVerified").GetBoolean());
        Assert.Equal("UNKNOWN", status.GetProperty("source").GetProperty("gameVersion").GetString());
        Assert.False(status.GetProperty("executed").GetBoolean());
    }

    [Fact]
    public void Search_item_names_is_readonly_paginated_and_source_labelled()
    {
        using var fixture = new SyntheticCache();
        var search = Query(fixture, "search_items", "bois");
        Assert.Equal("REVIEW_ONLY", Status(search));
        Assert.Equal(1, search.GetProperty("total").GetInt32());
        var item = search.GetProperty("items")[0];
        Assert.Equal(44, item.GetProperty("itemId").GetInt64());
        Assert.Equal(2, item.GetProperty("itemSetId").GetInt64());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("hdvPrice").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("dropRate").ValueKind);
        Assert.Equal("dofusdb", search.GetProperty("source").GetProperty("kind").GetString());
        Assert.Equal(0, Query(fixture, "search_items", "bois", offset: 1)
            .GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void Get_item_has_only_known_fields_and_null_prices()
    {
        using var fixture = new SyntheticCache();
        var result = Query(fixture, "get_item", id: 44);
        Assert.Equal(7, result.GetProperty("item").GetProperty("Level").GetInt32());
        Assert.Equal(JsonValueKind.Null,
            result.GetProperty("item").GetProperty("xpPerCraft").ValueKind);
    }

    [Fact]
    public void Get_recipe_never_invents_yield_xp_price_or_route()
    {
        using var fixture = new SyntheticCache();
        var result = Query(fixture, "get_recipe", id: 44);
        Assert.Equal("REVIEW_ONLY", Status(result));
        var recipe = result.GetProperty("recipe");
        Assert.Equal(16512, recipe.GetProperty("ingredients")[0].GetProperty("itemId").GetInt64());
        Assert.Equal(3, recipe.GetProperty("ingredients")[0].GetProperty("quantity").GetInt64());
        Assert.Equal(JsonValueKind.Null, recipe.GetProperty("resultQuantity").ValueKind);
        Assert.Equal(JsonValueKind.Null, recipe.GetProperty("xpPerCraft").ValueKind);
        Assert.Equal(JsonValueKind.Null, recipe.GetProperty("hdvPrice").ValueKind);
        Assert.Equal("UNKNOWN", result.GetProperty("routeStatus").GetString());
        Assert.Empty(result.GetProperty("routes").EnumerateArray());
    }

    [Fact]
    public void Get_and_list_sets_do_not_invent_set_members()
    {
        using var fixture = new SyntheticCache();
        var set = Query(fixture, "get_set", id: 2);
        Assert.Equal(2, set.GetProperty("set").GetProperty("itemIds").GetArrayLength());
        var list = Query(fixture, "list_sets", offset: 0, limit: 1);
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Single(list.GetProperty("sets").EnumerateArray());
    }

    [Theory]
    [InlineData("do_gameplay", null, 0, 0, 1)]
    [InlineData("search_items", "a", 0, 0, 10)]
    [InlineData("search_items", null, 0, 0, 10)]
    [InlineData("search_items", "long", 0, -1, 10)]
    [InlineData("search_items", "long", 0, 0, 31)]
    [InlineData("get_item", null, 0, 0, 10)]
    [InlineData("get_recipe", null, -1, 0, 10)]
    [InlineData("get_set", null, -1, 0, 10)]
    public void Invalid_queries_are_refused_before_disclosure(string op, string? query,
        long id, int offset, int limit)
    {
        using var fixture = new SyntheticCache();
        Assert.Equal("INVALID_REQUEST", Status(Query(fixture, op, query, id, offset, limit)));
    }

    [Fact]
    public void Absent_id_produces_unknown_not_empty_success()
    {
        using var fixture = new SyntheticCache();
        Assert.Equal("UNKNOWN", Status(Query(fixture, "get_recipe", id: 777)));
        Assert.Equal("UNKNOWN", Status(Query(fixture, "get_item", id: 777)));
        Assert.Equal("UNKNOWN", Status(Query(fixture, "get_set", id: 777)));
    }

    [Fact]
    public void Changed_page_without_updated_hash_is_denied()
    {
        using var fixture = new SyntheticCache();
        File.AppendAllText(fixture.Page("items"), "\n");
        Assert.Equal("UNKNOWN", Status(Query(fixture, "get_item", id: 44)));
    }

    [Fact]
    public void Changed_manifest_without_repin_is_blocked()
    {
        using var fixture = new SyntheticCache();
        File.AppendAllText(Path.Combine(fixture.Root, "review-summary.json"), " ");
        Assert.Equal("BLOCK", Status(Query(fixture, "get_item", id: 44)));
    }

    [Fact]
    public void Incorrect_page_counts_fail_closed()
    {
        using var fixture = new SyntheticCache();
        File.Delete(fixture.Meta("recipes"));
        Assert.Equal("UNKNOWN", Status(Query(fixture, "get_recipe", id: 44)));
    }

    [Fact]
    public void Unknown_game_version_remains_unknown_even_in_reviewed_results()
    {
        using var fixture = new SyntheticCache();
        var result = Query(fixture, "get_item", id: 44);
        Assert.Equal("UNKNOWN", result.GetProperty("source").GetProperty("gameVersion").GetString());
        Assert.False(result.GetProperty("source").GetProperty("sourceGameVersionVerified").GetBoolean());
    }

    [Fact]
    public void Wrong_rights_shape_in_metadata_blocks_disclosure()
    {
        using var fixture = new SyntheticCache();
        string file = fixture.Meta("items");
        File.WriteAllText(file, File.ReadAllText(file).Replace("\"aiMcpAllowed\":false",
            "\"aiMcpAllowed\":true", StringComparison.Ordinal));
        Assert.Equal("UNKNOWN", Status(Query(fixture, "search_items", "bois")));
    }
}
