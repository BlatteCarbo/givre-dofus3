extern alias F06App;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using GameDb = F06App::GivreClone.App.Services.LotM.GameDb;
using Xunit;

namespace GivreClone.Tests;

public sealed class ForgeRuneCatalogStartupTests
{
    [Fact]
    public void Catalog_uses_database_names_before_bundle_names_are_loaded()
    {
        using var fake = new FakeGameDb();
        using (var connection = new SqliteConnection($"Data Source={fake.File}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                insert into translations values ('9001','Rune Fo','fr');
                insert into ItemData values (1519,9001,1,'[]');
                """;
            command.ExecuteNonQuery();
        }
        using var db = GameDb.TryOpen(fake.File)!;
        var launch = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        typeof(Launch).GetField("_gameDb", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(launch, db);
        var rune = Assert.Single(launch.RuneItemsByGid());
        Assert.Equal(1519, rune.Key);
        Assert.Equal(118, rune.Value.EffectId);
        Assert.Equal("Ba", rune.Value.Tier);
    }
}
