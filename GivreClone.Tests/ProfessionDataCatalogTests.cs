using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using GivreClone.App.Services.ProfessionData;
using Xunit;

namespace GivreClone.Tests;

public sealed class ProfessionDataCatalogTests
{
    private sealed class Fixture : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "GivreProfessionDataTests",
            Guid.NewGuid().ToString("N"));
        public string Asset(string format) => Path.Combine(Folder,
            format == "json" ? "recipes.json" : "dofus.sqlite");

        public Fixture() => Directory.CreateDirectory(Folder);

        public void WriteJson(bool invalidQuantity = false)
        {
            var obj = new
            {
                schemaVersion = 1,
                items = new[] { new { id = 101, name = "Plume", level = 1 },
                    new { id = 102, name = "Huile", level = 5 },
                    new { id = 103, name = "Bague", level = 25 } },
                jobs = new[] { new { id = 13, name = "Bijoutier" } },
                recipes = new[] { new
                {
                    id = "bijou-103", jobId = 13, resultItemId = 103, requiredLevel = 25,
                    resultQuantity = (int?)null, experiencePerCraft = (long?)null,
                    ingredients = new[]
                    {
                        new { itemId = 101, quantity = invalidQuantity ? 0 : 20 },
                        new { itemId = 102, quantity = 3 }
                    }
                } },
                levelThresholds = Array.Empty<object>()
            };
            File.WriteAllText(Asset("json"), JsonSerializer.Serialize(obj));
        }

        public void WriteSqlite(bool incorrectPairs = false, bool missingItem = false)
        {
            SQLitePCL.Batteries.Init();
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Asset("sqlite"), Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE ItemData(id INTEGER PRIMARY KEY,nameId INTEGER,level INTEGER);
                CREATE TABLE JobData(id INTEGER PRIMARY KEY,nameId INTEGER);
                CREATE TABLE translations(id TEXT,lang TEXT,value TEXT,PRIMARY KEY(id,lang));
                CREATE TABLE RecipeData(id TEXT PRIMARY KEY,jobId INTEGER,quantities TEXT,
                    resultId INTEGER,resultLevel INTEGER);
                CREATE TABLE RecipeData_ingredientIds_junction(RecipeData_id TEXT,target_id INTEGER);
                INSERT INTO ItemData VALUES(101,1,1),(102,2,5),(103,3,25);
                INSERT INTO JobData VALUES(13,4);
                INSERT INTO translations VALUES('1','fr','Plume'),('2','fr','Huile'),
                    ('3','fr','Bague'),('4','fr','Bijoutier');
                INSERT INTO RecipeData VALUES('bijou-103',13,'[20,3]',103,25);
                INSERT INTO RecipeData_ingredientIds_junction VALUES('bijou-103',101);
                """ + (incorrectPairs ? "" :
                "INSERT INTO RecipeData_ingredientIds_junction VALUES('bijou-103',102);");
            command.ExecuteNonQuery();
            if (missingItem)
            {
                command.CommandText = "DELETE FROM ItemData WHERE id=102; UPDATE ItemData SET level=0 WHERE id=101;";
                command.ExecuteNonQuery();
            }
        }

        public void WriteManifest(string format, string source = "local-extraction",
            string permission = "owner-authorized", string? sha = null, string? filename = null,
            string version = "3.7.12.12")
        {
            string asset = Asset(format);
            var meta = new
            {
                schemaVersion = 1, gameVersion = version, source, permission, format,
                asset = new
                {
                    file = filename ?? Path.GetFileName(asset),
                    bytes = new FileInfo(asset).Length,
                    sha256 = sha ?? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(asset)))
                }
            };
            File.WriteAllText(Path.Combine(Folder, "manifest.json"), JsonSerializer.Serialize(meta));
        }

        public void Dispose()
        {
            if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("sqlite")]
    public void Legitimate_offline_manifest_yields_recipes_with_no_fabricated_xp(string format)
    {
        using var data = new Fixture();
        if (format == "sqlite") data.WriteSqlite();
        else data.WriteJson();
        data.WriteManifest(format);
        byte[] original = File.ReadAllBytes(data.Asset(format));

        var catalog = ProfessionCatalogReader.LoadDirectory(data.Folder, "3.7.12.12");
        Assert.True(catalog.Admitted, string.Join("; ", catalog.Issues));
        Assert.True(catalog.Source.LocalHashVerified);
        Assert.False(catalog.Source.PublisherVerified);
        Assert.False(catalog.HasVerifiedGameExperience);
        var recipe = Assert.Single(catalog.RecipesForJob(13, 30));
        Assert.Equal(25, recipe.RequiredLevel);
        Assert.Null(recipe.ExperiencePerCraft);
        Assert.Null(recipe.ResultQuantity);
        Assert.Equal(new long[] { 101, 102 }, recipe.Ingredients.Select(x => x.ItemId));
        Assert.Equal(new[] { 20, 3 }, recipe.Ingredients.Select(x => x.Quantity));
        Assert.Equal("Bague", catalog.FindItem(103)!.Name);
        Assert.Empty(catalog.RecipesForJob(13, 20));
        Assert.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(data.Asset(format))));
        Assert.False(File.Exists(data.Asset(format) + "-wal"));
        Assert.False(File.Exists(data.Asset(format) + "-shm"));
    }

    [Fact]
    public void Never_load_without_manifest_and_reject_sha_tamper()
    {
        using var data = new Fixture();
        data.WriteJson();
        Assert.False(ProfessionCatalogReader.LoadDirectory(data.Folder).Admitted);
        data.WriteManifest("json", sha: new string('A', 64));
        Assert.False(ProfessionCatalogReader.LoadDirectory(data.Folder).Admitted);
        data.WriteManifest("json");
        File.AppendAllText(data.Asset("json"), " ");
        var result = ProfessionCatalogReader.LoadDirectory(data.Folder);
        Assert.False(result.Admitted);
        Assert.Empty(result.Recipes);
    }

    [Theory]
    [InlineData("DofusDB-public-api", "owner-authorized")]
    [InlineData("local-extraction", "unverified")]
    [InlineData("DofusDB-public-api", "unverified")]
    public void Unapproved_public_api_or_permission_must_fail(string source, string permission)
    {
        using var data = new Fixture();
        data.WriteJson();
        data.WriteManifest("json", source, permission);
        Assert.False(ProfessionCatalogReader.LoadDirectory(data.Folder).Admitted);
    }

    [Fact]
    public void Rejects_wrong_game_version_and_asset_path_traversal()
    {
        using var data = new Fixture();
        data.WriteJson();
        data.WriteManifest("json");
        Assert.False(ProfessionCatalogReader.LoadDirectory(data.Folder, "3.7.11.11").Admitted);
        data.WriteManifest("json", filename: "../recipes.json");
        Assert.False(ProfessionCatalogReader.LoadDirectory(data.Folder).Admitted);
    }

    [Fact]
    public void Rejects_sqlite_mismatched_ingredients_instead_of_inventing_quantities()
    {
        using var data = new Fixture();
        data.WriteSqlite(incorrectPairs: true);
        data.WriteManifest("sqlite");
        var catalog = ProfessionCatalogReader.LoadDirectory(data.Folder);
        Assert.False(catalog.Admitted);
        Assert.Empty(catalog.Recipes);
    }

    [Fact]
    public void Rejects_invalid_json_quantity_and_stops_partial_catalogs()
    {
        using var data = new Fixture();
        data.WriteJson(invalidQuantity: true);
        data.WriteManifest("json");
        var catalog = ProfessionCatalogReader.LoadDirectory(data.Folder);
        Assert.False(catalog.Admitted);
        Assert.Empty(catalog.Items);
        Assert.Empty(catalog.Recipes);
    }

    [Fact]
    public void Sqlite_missing_item_metadata_is_explicit_unknown_and_not_a_fabricated_item()
    {
        using var data = new Fixture();
        data.WriteSqlite(missingItem: true);
        data.WriteManifest("sqlite");
        var catalog = ProfessionCatalogReader.LoadDirectory(data.Folder);
        Assert.True(catalog.Admitted, string.Join("; ", catalog.Issues));
        Assert.True(catalog.FindItem(102)!.IsPlaceholder);
        Assert.Null(catalog.FindItem(102)!.Name);
        Assert.Null(catalog.FindItem(101)!.Level);
        Assert.False(catalog.FindItem(101)!.IsPlaceholder);
    }

    [Fact]
    public void Planner_projection_requires_known_output_count_and_never_automatically_verifies_xp()
    {
        using var data = new Fixture();
        data.WriteJson();
        data.WriteManifest("json");
        var catalog = ProfessionCatalogReader.LoadDirectory(data.Folder);
        var projection = ProfessionPlannerCatalogAdapter.ForJob(catalog, 13);
        Assert.Empty(projection.Recipes); // JSON resultQuantity UNKNOWN, cannot fabricate 1.
        Assert.Contains(projection.Warnings, s => s.Contains("exclue"));

        string path = data.Asset("json");
        string json = File.ReadAllText(path);
        File.WriteAllText(path, json.Replace("\"resultQuantity\":null", "\"resultQuantity\":1"));
        data.WriteManifest("json");
        var provenQuantity = ProfessionCatalogReader.LoadDirectory(data.Folder);
        var first = Assert.Single(ProfessionPlannerCatalogAdapter.ForJob(provenQuantity, 13).Recipes);
        var again = Assert.Single(ProfessionPlannerCatalogAdapter.ForJob(provenQuantity, 13).Recipes);
        Assert.Equal(first.RecipeId, again.RecipeId);
        Assert.False(first.Verified); // local hash only; no source attestation
        Assert.False(first.XpVerified);
        Assert.Null(first.XpPerCraft);
    }
}
