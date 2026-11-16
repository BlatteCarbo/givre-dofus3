using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using GivreClone.App.Services.LotM;
using Xunit;

namespace GivreClone.Tests;

public sealed class HdvScannerDatasetReadOnlyTests
{
    [Fact]
    public void RecipesFeedScannerWithoutChangingDatasetOrInventingPrices()
    {
        SQLitePCL.Batteries.Init();
        string path = Path.Combine(Path.GetTempPath(), $"r6-hdv-{Guid.NewGuid():N}.sqlite");
        try
        {
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                   { DataSource = path, Pooling = false }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE translations(id TEXT, lang TEXT, value TEXT);
                    CREATE TABLE ItemData(id INTEGER, nameId INTEGER);
                    CREATE TABLE JobData(id INTEGER, nameId INTEGER);
                    CREATE TABLE RecipeData(id TEXT, jobId INTEGER, quantities TEXT, resultId INTEGER, resultLevel INTEGER);
                    CREATE TABLE RecipeData_ingredientIds_junction(RecipeData_id TEXT, target_id TEXT);
                    INSERT INTO translations VALUES('1','fr','Craft'),('2','fr','Ingredient'),('3','fr','Job');
                    INSERT INTO ItemData VALUES(100,1),(200,2);
                    INSERT INTO JobData VALUES(1,3);
                    INSERT INTO RecipeData VALUES('r',1,'[3]',100,10);
                    INSERT INTO RecipeData_ingredientIds_junction VALUES('r','200');
                    """;
                command.ExecuteNonQuery();
            }
            byte[] before = SHA256.HashData(File.ReadAllBytes(path));
            using (var db = GameDb.TryOpen(path)!)
            {
                var recipe = Assert.Single(db.RecipesOfJob(1, 20));
                Assert.Equal(3, Assert.Single(recipe.Ingredients).Quantity);
                var input = new CraftScanRecipe(recipe.ResultId, recipe.Name, recipe.Level, recipe.JobId,
                    recipe.Ingredients.Select(i => new CraftScanIngredient(i.Gid, i.Quantity)).ToArray());
                var unknown = Assert.Single(HdvCraftScanner.Estimate(new[] { input }, new Dictionary<long, CraftPrice>(), 2));
                Assert.Null(unknown.Cost);
                Assert.Null(unknown.Value);
                var prices = new Dictionary<long, CraftPrice>
                {
                    [100] = HdvCraftScanner.SelectPrice(100, 10, 0),
                    [200] = HdvCraftScanner.SelectPrice(0, long.MaxValue, 10)
                };
                var estimate = Assert.Single(HdvCraftScanner.Estimate(new[] { input }, prices, 2));
                Assert.Equal(60m, estimate.Cost);
                Assert.Equal(200m, estimate.Value);
            }
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
        }
        finally { File.Delete(path); }
    }
}
