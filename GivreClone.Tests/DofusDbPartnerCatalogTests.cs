using System.Security.Cryptography;
using System.Text.Json;
using GivreClone.App.Services.DofusDbPartner;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Fixtures are synthetic and strictly offline. No Dofus gameplay or service requests.</summary>
public sealed class DofusDbPartnerCatalogTests
{
    private static readonly string[] Categories =
    [
        "items", "item-sets", "recipes", "monsters", "map-positions", "interactives",
        "jobs", "subareas", "areas", "worlds", "map-references", "transport-from-maps",
        "point-of-interest", "dungeons", "quests", "npcs", "achievements", "mounts", "spells"
    ];
    private static readonly string[] Indexed =
    ["items", "item-sets", "recipes", "jobs", "monsters", "map-positions"];
    private static readonly DofusDbPartnerAdmission Granted = new(
        OperatorApprovedRuntimeUse: true, OperatorApprovedAiMcpUse: false,
        OperatorEvidenceId: "synthetic-rights-proof");

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "Givre-DofusDb-" + Guid.NewGuid().ToString("N"));
        public Dictionary<string, List<object>> Records { get; } = new()
        {
            ["items"] = [new { _id = "document-item", id = 2411,
                name = new { fr = "Chapeau imaginaire", en = "Imaginary hat" },
                level = 20, itemSetId = 1 }],
            ["item-sets"] = [new { _id = "document-set", id = 1,
                name = new { fr = "Panoplie fictive", en = "Fictional set" },
                items = new[] { new { id = 2411 } } }],
            ["recipes"] = [new { _id = "document-recipe", id = 44, resultId = 2411,
                ingredientIds = new[] { 123, 124 }, quantities = new[] { 2, 3 }, jobId = 2 }],
            ["jobs"] = [new { _id = "document-job", id = 2,
                name = new { fr = "Métier inventé", en = "Fake job" } }],
            ["monsters"] = [new { _id = "document-monster", id = 31,
                name = new { fr = "Monstre inventé", en = "Fake monster" },
                subareas = new[] { 1, 27 } }],
            ["map-positions"] = [new { _id = "document-map", id = 0, posX = 3, posY = -3,
                subAreaId = 10, worldMap = 1 }]
        };

        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Write();
        }
        public void Write()
        {
            foreach (var c in Indexed)
            {
                Directory.CreateDirectory(Path.Combine(Root, c));
                var records = Records[c];
                for (int n = 0; n < records.Count; n += 50)
                    WritePage(c, n, records.Skip(n).Take(50).ToArray(), records.Count);
            }
            WriteSummary();
        }
        public void WritePage(string category, int skip, object[] rows, int total)
        {
            string folder = Path.Combine(Root, category);
            Directory.CreateDirectory(folder);
            string stem = "page-" + skip.ToString("D8");
            byte[] raw = JsonSerializer.SerializeToUtf8Bytes(new
            {
                data = rows, total, skip, limit = 50
            });
            File.WriteAllBytes(Path.Combine(folder, stem + ".json"), raw);
            var meta = new
            {
                aiMcpAllowed = false, bytes = raw.Length, category, limit = 50,
                records = rows.Length, runtimeCertified = false, schema = 1,
                sha256 = Convert.ToHexStringLower(SHA256.HashData(raw)), skip,
                sourceUrl = $"https://api.dofusdb.fr/{category}?%24limit=50&%24skip={skip}",
                total, versionStatus = "UNKNOWN"
            };
            File.WriteAllText(Path.Combine(folder, stem + ".meta.json"),
                JsonSerializer.Serialize(meta));
        }
        public void WriteSummary(bool complete = true, bool aiMcpAllowed = false,
            string version = "UNKNOWN")
        {
            var counts = Categories.ToDictionary(c => c, c =>
            {
                int n = Records.GetValueOrDefault(c)?.Count ?? 0;
                return new
                {
                    expected = n, captured = n, pages = (n + 49) / 50, complete = true
                };
            });
            File.WriteAllText(Path.Combine(Root, "review-summary.json"),
                JsonSerializer.Serialize(new
                {
                    aiMcpAllowed, authorizedRuntimeUse = false, collections = counts,
                    complete, gameplayCertified = false, schema = 1,
                    source = "DofusDB public API - user-asserted partner review",
                    sourceGameVersion = version,
                    sourceRightsVerifiedIndependently = false,
                    status = "REVIEW_ONLY_COMPLETE"
                }));
        }
        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        public DofusDbPartnerLoadResult Load(DofusDbPartnerAdmission? approval = null) =>
            DofusDbPartnerCatalogReader.LoadDirectory(Root, approval ?? Granted);
    }

    [Fact]
    public void Complete_signed_pages_admit_all_six_indexes_and_total_provenance()
    {
        using var fixture = new Fixture();
        var loaded = fixture.Load();
        Assert.True(loaded.Success, loaded.Reason);
        var catalog = Assert.IsType<DofusDbPartnerCatalog>(loaded.Catalog);
        Assert.Equal(1, catalog.ItemCount);
        Assert.Equal(1, catalog.SetCount);
        Assert.Equal(1, catalog.RecipeCount);
        Assert.Equal(1, catalog.JobCount);
        Assert.Equal(1, catalog.MonsterCount);
        Assert.Equal(1, catalog.MapPositionCount);
        Assert.Equal(19, catalog.Provenance.SummaryCounts.Count);
        Assert.Equal(6, catalog.Provenance.VerifiedCounts.Count);
        Assert.Matches("^[a-f0-9]{64}$", catalog.Provenance.ReviewSummarySha256);
        Assert.Equal("dofusdb", catalog.Provenance.SourceKind);
        Assert.Equal("UNKNOWN", catalog.Provenance.SourceGameVersion);
    }

    [Fact]
    public void Find_item_by_french_english_or_number_does_not_invent_names()
    {
        using var fixture = new Fixture();
        var c = fixture.Load().Catalog!;
        Assert.Equal(2411, Assert.Single(c.SearchItems("Imaginary")).Id);
        Assert.Equal(2411, Assert.Single(c.SearchItems("imaginaire")).Id);
        Assert.Equal(2411, Assert.Single(c.SearchItems("2411")).Id);
        Assert.Empty(c.SearchItems("Unrelated"));
        Assert.Null(c.GetItem(9988));
        Assert.Equal(20, c.GetItem(2411)?.Level);
    }

    [Fact]
    public void Set_membership_is_from_embedded_members_only()
    {
        using var fixture = new Fixture();
        var c = fixture.Load().Catalog!;
        Assert.Equal(new long[] { 2411 }, c.GetSet(1)!.MemberItemGids);
        Assert.Equal(1, Assert.Single(c.GetSetsContaining(2411)).Id);
        Assert.Empty(c.GetSetsContaining(9988));
        Assert.Equal(1, Assert.Single(c.SearchSets("Panoplie fictive")).Id);
    }

    [Fact]
    public void Per_result_recipes_preserve_ingredient_quantities_and_unknown_yield_xp()
    {
        using var fixture = new Fixture();
        var c = fixture.Load().Catalog!;
        var recipe = Assert.Single(c.GetRecipesForItem(2411));
        Assert.Equal(44, recipe.SourceRecipeId);
        Assert.Equal(new long[] { 123, 124 }, recipe.Ingredients.Select(i => i.ItemGid));
        Assert.Equal(new long?[] { 2, 3 }, recipe.Ingredients.Select(i => i.Quantity));
        Assert.Equal(2, recipe.JobId);
        Assert.Null(recipe.ResultQuantity);
        Assert.Null(recipe.XpPerCraft);
        Assert.False(recipe.GameVersionVerified);
        Assert.Empty(c.GetRecipesForItem(987654));
    }

    [Fact]
    public void Job_monster_and_positions_keep_source_fields_not_runtime_routes()
    {
        using var fixture = new Fixture();
        var c = fixture.Load().Catalog!;
        Assert.Equal("Métier inventé", c.GetJob(2)!.NameFr);
        Assert.Equal(new long[] { 1, 27 }, c.GetMonster(31)!.SubareaIds);
        Assert.Equal(31, Assert.Single(c.SearchMonsters("Fake monster")).Id);
        var pos = Assert.Single(c.FindMapPositions(3, -3, 1));
        Assert.Equal(0, pos.Id);
        Assert.Equal(10, pos.SubAreaId);
        Assert.Empty(c.FindMapPositions(3, -3, 5));
        Assert.False(c.Provenance.GameplayCertified);
        Assert.False(c.Provenance.RuntimeCertified);
    }

    [Fact]
    public void Missing_operator_approval_or_evidence_blocks_all_records()
    {
        using var fixture = new Fixture();
        foreach (var admission in new[]
        {
            new DofusDbPartnerAdmission(),
            new DofusDbPartnerAdmission(OperatorApprovedRuntimeUse:true),
            new DofusDbPartnerAdmission(true, false, "short")
        })
        {
            var result = fixture.Load(admission);
            Assert.False(result.Success);
            Assert.Null(result.Catalog);
            Assert.Equal("OPERATOR_RUNTIME_APPROVAL_REQUIRED", result.Reason);
        }
    }

    [Fact]
    public void Ai_rights_are_separate_operator_assertion_never_independent_proof()
    {
        using var fixture = new Fixture();
        var c = fixture.Load(new DofusDbPartnerAdmission(
            true, true, "operator-private-approval" )).Catalog!;
        Assert.True(c.Provenance.OperatorApprovedAiMcpUse);
        Assert.True(c.Provenance.OperatorApprovedRuntimeUse);
        Assert.False(c.Provenance.SourceAiMcpAllowed);
        Assert.False(c.Provenance.SourceRightsVerifiedIndependently);
        Assert.False(c.Provenance.AiMcpRightsIndependentlyVerified);
    }

    [Fact]
    public void Review_must_remain_complete_and_game_version_unknown()
    {
        using var fixture = new Fixture();
        fixture.WriteSummary(complete: false);
        Assert.False(fixture.Load().Success);
        fixture.WriteSummary(version: "3.7.10.10");
        Assert.False(fixture.Load().Success);
        fixture.WriteSummary(aiMcpAllowed: true);
        Assert.False(fixture.Load().Success);
    }

    [Fact]
    public void Byte_corruption_causes_sha_mismatch_and_no_partial_index()
    {
        using var fixture = new Fixture();
        var file = Path.Combine(fixture.Root, "items", "page-00000000.json");
        File.AppendAllText(file, " ");
        var result = fixture.Load();
        Assert.False(result.Success);
        Assert.Null(result.Catalog);
        Assert.Equal("CACHE_SHA256_MISMATCH", result.Reason);
    }

    [Fact]
    public void Broken_metadata_or_mismatched_page_offset_is_rejected()
    {
        using var fixture = new Fixture();
        var meta = Path.Combine(fixture.Root, "recipes", "page-00000000.meta.json");
        File.WriteAllText(meta, File.ReadAllText(meta).Replace(
            "https://api.dofusdb.fr", "https://example.invalid"));
        Assert.Equal("CACHE_META_INVALID", fixture.Load().Reason);
        fixture.Write();
        var page = Path.Combine(fixture.Root, "recipes", "page-00000000.json");
        File.WriteAllText(page, File.ReadAllText(page).Replace("\"skip\":0", "\"skip\":50"));
        Assert.False(fixture.Load().Success);
    }

    [Fact]
    public void Missing_page_or_missing_meta_returns_rejection_without_fallback()
    {
        using var fixture = new Fixture();
        File.Delete(Path.Combine(fixture.Root, "items", "page-00000000.json"));
        Assert.False(fixture.Load().Success);
        fixture.Write();
        File.Delete(Path.Combine(fixture.Root, "jobs", "page-00000000.meta.json"));
        Assert.False(fixture.Load().Success);
    }

    [Fact]
    public void Duplicate_document_ids_are_detected_across_page_boundaries()
    {
        using var fixture = new Fixture();
        var first = Enumerable.Range(1, 50).Select(n => (object)new
        {
            _id = "key-" + n, id = n,
            name = new { fr = "Fictif", en = "Fake" }, level = 1, itemSetId = -1
        }).ToList();
        first.Add(new { _id = "key-1", id = 99,
            name = new { fr = "Fictif", en = "Fake" }, level = 1, itemSetId = -1 });
        fixture.Records["items"] = first;
        fixture.Write();
        var result = fixture.Load();
        Assert.Equal("DUPLICATE_DOCUMENT_ID", result.Reason);
    }

    [Fact]
    public void Duplicate_business_ids_not_masked_by_different_mongo_ids()
    {
        using var fixture = new Fixture();
        fixture.Records["items"].Add(new { _id = "different-mongo-id", id = 2411,
            name = new { fr = "Fictif", en = "Fake" }, level = 1, itemSetId = -1 });
        fixture.Write();
        Assert.Equal("DUPLICATE_BUSINESS_ID", fixture.Load().Reason);
    }

    [Fact]
    public void Unequal_ingredient_ids_and_quantities_rejected_instead_of_guessing()
    {
        using var fixture = new Fixture();
        fixture.Records["recipes"][0] = new { _id = "document-recipe", id = 44,
            resultId = 2411, ingredientIds = new[] { 123, 124 },
            quantities = new[] { 3 }, jobId = 2 };
        fixture.Write();
        Assert.Equal("RECIPE_INGREDIENT_COUNTS_MISMATCH", fixture.Load().Reason);
    }

    [Fact]
    public void Null_ingredient_quantity_remains_nullable_and_does_not_guess_zero_or_one()
    {
        using var fixture = new Fixture();
        fixture.Records["recipes"][0] = new { _id = "document-recipe", id = 44,
            resultId = 2411, ingredientIds = new[] { 123 },
            quantities = new int?[] { null }, jobId = 2 };
        fixture.Write();
        var result = fixture.Load();
        Assert.True(result.Success, result.Reason);
        Assert.Null(Assert.Single(result.Catalog!.GetRecipesForItem(2411)).Ingredients[0].Quantity);
    }

    [Fact]
    public void Query_and_pagination_are_bounded_even_for_read_only_calls()
    {
        using var fixture = new Fixture();
        var c = fixture.Load().Catalog!;
        Assert.Throws<ArgumentOutOfRangeException>(() => c.SearchItems("", limit: 101));
        Assert.Throws<ArgumentOutOfRangeException>(() => c.SearchSets("", offset: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => c.FindMapPositions(3, -3, limit: 0));
        Assert.Throws<ArgumentException>(() => c.SearchItems(new string('x', 101)));
        Assert.Empty(c.SearchItems("", offset: 1));
    }

    [Fact]
    public void Unexpected_extra_page_file_is_rejected_not_silently_ignored()
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.Root, "items", "page-00000050.json"), "{}");
        Assert.Equal("PAGE_FILE_COUNT_MISMATCH", fixture.Load().Reason);
    }

    [Fact]
    public void Actual_partner_review_snapshot_validates_all_six_collections_when_available()
    {
        // Offline smoke when the explicit local cache is present. No network.
        // This test-only assertion is NOT evidence of independent AI usage rights.
        var root = Environment.GetEnvironmentVariable("GIVRE_DOFUSDB_LOCAL_TEST_CACHE");
        if (string.IsNullOrWhiteSpace(root))
        {
            var cwd = Directory.GetCurrentDirectory();
            var path = Path.Combine(cwd, "tools", "dofusdb-partner-catalog",
                ".cache", "full-catalog");
            if (Directory.Exists(path)) root = path;
        }
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
        var result = DofusDbPartnerCatalogReader.LoadDirectory(root, Granted);
        Assert.True(result.Success, result.Reason);
        var catalog = Assert.IsType<DofusDbPartnerCatalog>(result.Catalog);
        Assert.Equal(21_554, catalog.ItemCount);
        Assert.Equal(940, catalog.SetCount);
        Assert.Equal(4_743, catalog.RecipeCount);
        Assert.Equal(23, catalog.JobCount);
        Assert.Equal(5_129, catalog.MonsterCount);
        Assert.Equal(15_409, catalog.MapPositionCount);
        Assert.Equal(87_327, catalog.Provenance.SummaryCounts.Values.Sum());
        Assert.Equal(47_798, catalog.Provenance.VerifiedCounts.Values.Sum());
        Assert.False(catalog.Provenance.SourceAiMcpAllowed);
        Assert.Equal("UNKNOWN", catalog.Provenance.SourceGameVersion);
        Assert.Null(catalog.GetRecipesForItem(44).FirstOrDefault()?.ResultQuantity);
    }
}
