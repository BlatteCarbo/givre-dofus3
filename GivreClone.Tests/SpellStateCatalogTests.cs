using Microsoft.Data.Sqlite;
using GivreClone.App.Services.LotM;
using Xunit;

namespace GivreClone.Tests;

public sealed class SpellStateCatalogTests
{
    [Fact]
    public void Missing_state_table_is_unknown_not_a_negative_immunity_fact()
    {
        using var fixture = new FakeGameDb();
        using var db = GameDb.TryOpen(fixture.File);
        Assert.NotNull(db);
        Assert.Null(db.SpellStateDamage(56));
    }

    [Fact]
    public void Damage_properties_follow_catalog_rows_without_hard_coded_state_ids()
    {
        using var fixture = new FakeGameDb();
        using (var writer = new SqliteConnection($"Data Source={fixture.File}"))
        {
            writer.Open();
            using var command = writer.CreateCommand();
            command.CommandText = """
                create table SpellStateData(id integer primary key, invulnerable integer,
                    invulnerableMelee integer, invulnerableRange integer, cantDealDamage integer);
                insert into SpellStateData values
                    (9001,1,0,0,0),(9002,0,1,0,0),(9003,0,0,1,0),(9004,0,0,0,1),(9005,0,0,0,0);
                """;
            command.ExecuteNonQuery();
        }
        using var db = GameDb.TryOpen(fixture.File);
        Assert.NotNull(db);
        Assert.Equal(new SpellStateDamageRule(true, false, false, false), db.SpellStateDamage(9001));
        Assert.Equal(new SpellStateDamageRule(false, true, false, false), db.SpellStateDamage(9002));
        Assert.Equal(new SpellStateDamageRule(false, false, true, false), db.SpellStateDamage(9003));
        Assert.Equal(new SpellStateDamageRule(false, false, false, true), db.SpellStateDamage(9004));
        Assert.Equal(new SpellStateDamageRule(false, false, false, false), db.SpellStateDamage(9005));
        Assert.Null(db.SpellStateDamage(9999));
        Assert.Null(db.SpellStateDamage(0));
    }
}
