extern alias F06App;
using Microsoft.Data.Sqlite;
using Xunit;
using GameDb = F06App::GivreClone.App.Services.LotM.GameDb;

namespace GivreClone.Tests;

public sealed class MonsterGradeStatsTests
{
    [Theory]
    [InlineData(5894, 1, 513, 0, 0)]
    [InlineData(5899, 3, 769, 6, 5)]
    public void Summon_grade_uses_exact_rank_and_level_scaled_life_observed_on_official_server(
        int template, int rank, int life, int ap, int mp)
    {
        using var db = GameDb.TryOpen(RealGameData.Path!)!;

        var stats = db.SummonGradeStats(template, rank, 161);

        Assert.NotNull(stats);
        Assert.Equal(life, stats.Life);
        Assert.Equal(ap, stats.Ap);
        Assert.Equal(mp, stats.Mp);
        Assert.Equal(template == 5894 ? 0 : 1, stats.SummonCost);
        if (template == 5899)
        {
            Assert.Equal(652, stats.Characteristics[10]);
            Assert.Equal(261, stats.Characteristics[14]);
            Assert.Equal(-5, stats.Characteristics[36]);
            Assert.Contains((14381, 2), stats.Spells!);
            Assert.Contains((14387, 2), stats.Spells!);
        }
        Assert.Null(db.SummonGradeStats(template, 99, 161));
    }

    [Fact]
    public void Monster_grade_preserves_elemental_resistances_and_weaknesses()
    {
        using var fixture = new FakeGameDb();
        using (var connection = new SqliteConnection($"Data Source={fixture.File}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "alter table MonsterData add column grades text; update MonsterData set grades = $grades where id = 77";
            command.Parameters.AddWithValue("$grades", """
                [{"level":130,"lifePoints":2700,"reductionNeutral":5,"reductionFire":10,"reductionWater":-15,"reductionAir":-30,"reductionEarth":40}]
                """);
            command.ExecuteNonQuery();
        }
        using var db = GameDb.TryOpen(fixture.File)!;

        var grade = db.MonsterGradeStats(77, 130);

        Assert.NotNull(grade);
        Assert.Equal(130, grade.Value.Level);
        Assert.Equal(2700, grade.Value.Life);
        Assert.Equal(new[] { 5, 10, -15, -30, 40 }, Enumerable.Range(1, 5).Select(element => grade.Value.ResistByElement[element]));
    }
}
