using Microsoft.Data.Sqlite;
using GivreClone.App.Services.LotM;
using Xunit;

namespace GivreClone.Tests;

public sealed class ObservedSpellLevelTests
{
    [Theory]
    [InlineData(32780, 86051, 2)]
    [InlineData(32781, 86051, null)]
    [InlineData(32780, 2, null)]
    public void Observed_level_resolves_only_for_its_catalogue_spell(int spell, int level, int? grade)
    {
        using var fixture = new FakeGameDb();
        using (var writer = new SqliteConnection($"Data Source={fixture.File}"))
        {
            writer.Open();
            using var command = writer.CreateCommand();
            command.CommandText = "create table SpellLevelData(id integer primary key, spellId integer, grade integer); insert into SpellLevelData values(86051,32780,2)";
            command.ExecuteNonQuery();
        }
        using var db = GameDb.TryOpen(fixture.File);

        var resolved = db!.SpellGradeForLevel(spell, level);

        Assert.Equal(grade, resolved);
    }
}
