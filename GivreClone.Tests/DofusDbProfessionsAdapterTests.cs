using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GivreClone.App.Services.DofusDbProfessions;
using Xunit;

namespace GivreClone.Tests
{
    public sealed class DofusDbProfessionsAdapterTests
    {
        private sealed class Fixture : IDisposable
        {
            public readonly string Folder = Path.Combine(Path.GetTempPath(), "DofusDbProfessionsTest",
                Guid.NewGuid().ToString("N"));
            public bool Complete = true;
            public bool AllowAi;
            public string Version = "UNKNOWN";
            public bool InvalidQuantity;
            public bool BrokenSourceUrl;
            public bool MissingRecipePage;

            public Fixture() => Directory.CreateDirectory(Folder);

            public void Write()
            {
                var kinds = new Dictionary<string, object[]>
                {
                    ["jobs"] = [new { _id = "doc-job", id = 16, name = new { fr = "Bijoutier" } }],
                    ["items"] = [
                        new { _id = "doc-a", id = 100, name = new { fr = "Bague de test" } },
                        new { _id = "doc-b", id = 101, name = new { fr = "Pépite fictive" } },
                        new { _id = "doc-c", id = 102, name = new { fr = "Plume fictive" } }],
                    ["recipes"] = [
                        new
                        {
                            _id = "doc-recipe-uuid", jobId = 16, resultId = 100, resultLevel = 20,
                            ingredientIds = new[] { 101, 102 }, quantities = new[] { InvalidQuantity ? 0 : 3, 7 },
                            resultName = new { fr = "Bague de test" }
                        }]
                };
                var stats = new Dictionary<string, object>();
                foreach (var (category, rows) in kinds)
                {
                    var folder = Path.Combine(Folder, category);
                    Directory.CreateDirectory(folder);
                    byte[] page = JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        total = rows.Length, limit = 50, skip = 0, data = rows
                    });
                    string sha = Convert.ToHexString(SHA256.HashData(page)).ToLowerInvariant();
                    string sourceUrl = $"https://api.dofusdb.fr/{category}?%24limit=50&%24skip=0";
                    var meta = new
                    {
                        schema = 1, category, total = rows.Length, limit = 50,
                        skip = 0, records = rows.Length, bytes = page.Length, sha256 = sha,
                        sourceUrl = BrokenSourceUrl ? "https://evil.example/recipes" : sourceUrl,
                        versionStatus = Version, aiMcpAllowed = AllowAi, runtimeCertified = false
                    };
                    if (category != "recipes" || !MissingRecipePage)
                    {
                        File.WriteAllBytes(Path.Combine(folder, "page-00000000.json"), page);
                        File.WriteAllText(Path.Combine(folder, "page-00000000.meta.json"),
                            JsonSerializer.Serialize(meta));
                    }
                    stats[category] = new { expected = rows.Length, captured = rows.Length,
                        pages = 1, complete = true };
                }
                var report = new
                {
                    status = "REVIEW_ONLY_COMPLETE", complete = Complete,
                    sourceGameVersion = Version, aiMcpAllowed = AllowAi, authorizedRuntimeUse = false,
                    gameplayCertified = false, sourceRightsVerifiedIndependently = false,
                    collections = stats
                };
                File.WriteAllText(Path.Combine(Folder, "review-summary.json"),
                    JsonSerializer.Serialize(report));
            }

            public void Dispose()
            {
                if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
            }
        }

        [Fact]
        public void Verified_pages_support_local_recipe_evidence_but_not_certified_yield_xp()
        {
            using var f = new Fixture();
            f.Write();
            var result = DofusDbProfessionReader.LoadReview(f.Folder);
            Assert.True(result.AdmittedForLocalReview, string.Join("; ", result.Issues));
            Assert.Equal("UNKNOWN", result.SourceGameVersion);
            Assert.Equal(3, result.PagesVerified);
            Assert.Equal(1, result.RecipeCount);
            Assert.Equal(3, result.ItemCount);
            Assert.Equal(1, result.JobCount);
            Assert.Equal(64, result.SourceProofSha256.Length);
            Assert.False(result.AllowedForAiOrMcp);
            Assert.False(result.CertifiedForGameRuntime);
            var row = Assert.Single(result.FindRecipesForJob("Bijoutier"));
            Assert.Equal("doc-recipe-uuid", row.Id);
            Assert.Equal("Bague de test", row.ResultName);
            Assert.Null(row.ResultQuantity);
            Assert.Null(row.ExperiencePerCraft);
            Assert.Equal(new[] { 101, 102 }, row.Ingredients.Select(i => i.ItemId));
            Assert.Equal(new[] { 3, 7 }, row.Ingredients.Select(i => i.Quantity));
        }

        [Fact]
        public void Read_only_recipe_rows_preserve_source_level_without_xp()
        {
            using var f = new Fixture();
            f.Write();
            var review = DofusDbProfessionReader.LoadReview(f.Folder);
            var recipe = Assert.Single(review.FindRecipesForJob("Bijoutier"));
            Assert.Equal(20, recipe.RequiredLevel);
            Assert.Equal(100, recipe.ResultItemId);
            Assert.Null(recipe.ExperiencePerCraft);
            Assert.Null(recipe.ResultQuantity);
        }

        [Fact]
        public void Job_selection_does_not_guess_synonyms()
        {
            using var f = new Fixture(); f.Write();
            var review = DofusDbProfessionReader.LoadReview(f.Folder);
            Assert.Empty(review.FindRecipesForJob("Sculpteur"));
        }

        [Theory]
        [InlineData("missing")]
        [InlineData("modified")]
        [InlineData("rights")]
        [InlineData("version")]
        [InlineData("incomplete")]
        [InlineData("quantity")]
        [InlineData("url")]
        [InlineData("missingpage")]
        public void Rejects_tampered_incomplete_unlicensed_or_mismatched_cache(string scenario)
        {
            if (scenario == "missing")
            {
                Assert.False(DofusDbProfessionReader.LoadReview(
                    Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).AdmittedForLocalReview);
                return;
            }
            using var f = new Fixture
            {
                AllowAi = scenario == "rights", Version = scenario == "version" ? "3.7.12.12" : "UNKNOWN",
                Complete = scenario != "incomplete", InvalidQuantity = scenario == "quantity",
                BrokenSourceUrl = scenario == "url", MissingRecipePage = scenario == "missingpage"
            };
            f.Write();
            if (scenario == "modified")
                File.AppendAllText(Path.Combine(f.Folder, "recipes", "page-00000000.json"), " ");
            var result = DofusDbProfessionReader.LoadReview(f.Folder);
            Assert.False(result.AdmittedForLocalReview);
            Assert.Empty(result.FindRecipesForJob("Bijoutier"));
        }

        [Fact]
        public void Unknown_job_has_no_output()
        {
            using var f = new Fixture(); f.Write();
            var result = DofusDbProfessionReader.LoadReview(f.Folder);
            Assert.Empty(result.FindRecipesForJob("Inconnu"));
            Assert.Empty(result.FindRecipesForJob("Bijoutier", 201));
        }

        [Fact]
        public void Existing_complete_quarantined_capture_has_hash_validated_real_coverage_when_present()
        {
            string root = Path.Combine(MappingScan.RepoRoot(), "tools", "dofusdb-partner-catalog",
                ".cache", "full-catalog");
            if (!File.Exists(Path.Combine(root, "review-summary.json")))
                return; // External ignored cache is optional outside the operator's machine.
            var review = DofusDbProfessionReader.LoadReview(root);
            Assert.True(review.AdmittedForLocalReview, string.Join("; ", review.Issues));
            Assert.Equal(528, review.PagesVerified);
            Assert.Equal(4_743, review.RecipeCount);
            Assert.Equal(21_554, review.ItemCount);
            Assert.Equal(23, review.JobCount);
            Assert.Equal("UNKNOWN", review.SourceGameVersion);
            Assert.False(review.AllowedForAiOrMcp);
            Assert.False(review.CertifiedForGameRuntime);
            Assert.NotEmpty(review.FindRecipesForJob("Bijoutier"));
        }
    }
}
