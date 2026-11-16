using Microsoft.Data.Sqlite;
using GivreClone.App.Services.LotM;
using Xunit;

namespace GivreClone.Tests;

public class ItemCardTests
{
    [Fact]
    public void Missing_optional_metadata_is_unknown_not_zero()
    {
        using var fake = new FakeGameDb();
        using var db = GameDb.TryOpen(fake.File)!;
        var card = db.ItemCard(100)!;
        Assert.Equal("Blé", card.Name);
        Assert.Equal(-1, card.Weight);
        Assert.Empty(card.Set);
        Assert.Null(db.ItemCard(99999));
    }

    [Fact]
    public void Resolves_set_type_level_and_zero_weight_from_catalogue()
    {
        using var fake = new FakeGameDb();
        using (var c = new SqliteConnection($"Data Source={fake.File}"))
        {
            c.Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "alter table ItemData add column typeId integer; alter table ItemData add column itemSetId integer; alter table ItemData add column realWeight integer; " +
                "create table ItemTypeData(id integer, nameId integer); create table ItemSetData(id integer, nameId integer); " +
                "insert into ItemTypeData values(8,4); insert into ItemSetData values(9,7); update ItemData set typeId=8,itemSetId=9,realWeight=0 where id=100;";
            cmd.ExecuteNonQuery();
        }
        using var db = GameDb.TryOpen(fake.File)!;
        var card = db.ItemCard(100)!;
        Assert.Equal(1, card.Level);
        Assert.Equal("Paysan", card.Type);
        Assert.Equal("Donjon Bouftou", card.Set);
        Assert.Equal(0, card.Weight);
    }
}
