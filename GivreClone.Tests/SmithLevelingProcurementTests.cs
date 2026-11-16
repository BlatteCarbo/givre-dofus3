using Microsoft.Data.Sqlite;
using GivreClone.App.Services.Eco;
using GivreClone.App.Services.LotM;
using Xunit;

namespace GivreClone.Tests;

public sealed class SmithLevelingProcurementTests
{
    [Fact]
    public void Support_requires_a_feasible_rune_sample_and_ranks_total_measured_cost_per_xp()
    {
        SmithSupportOffer[] offers =
        [
            new(1, 30, 1, 100, null),
            new(2, 30, 2, 100, 10000),
            new(3, 29, 3, 100, 900, 10),
            new(4, 28, 4, 200, 0, 20),
        ];
        Assert.Equal(new long[] { 4, 3 }, SmithSupportPurchase.Rank(offers, 30, 10000, 1000).Select(o => o.Gid));
    }

    [Fact]
    public void Unmeasured_support_exploration_includes_the_native_rune_cash_estimate()
    {
        SmithSupportOffer[] offers = [new(1, 30, 1, 100, 500), new(2, 29, 2, 200, 0)];
        Assert.Equal(2, SmithSupportPurchase.Rank(offers, 30, 10000, 1000)[0].Gid);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(200000, 200000, 0)]
    [InlineData(199999, 200000, 0)]
    [InlineData(200003, 200000, 0)]
    [InlineData(200004, 200000, 1)]
    [InlineData(220000, 200000, 5000)]
    [InlineData(400000, 200000, 8000)]
    [InlineData(400000, -1, 0)]
    public void Support_cap_uses_current_cash_and_the_absolute_floor(long cash, long floor, long expected)
        => Assert.Equal(expected, SmithSupportPurchase.Cap(cash, floor));

    [Fact]
    public void Travel_debits_reduce_the_purchase_cap_and_unknown_live_prices_never_buy()
    {
        Assert.True(SmithSupportPurchase.CanBuy(220000, 200000, 5000));
        Assert.False(SmithSupportPurchase.CanBuy(219999, 200000, 5000));
        Assert.False(SmithSupportPurchase.CanBuy(220000, 200000, 0));
        Assert.False(SmithSupportPurchase.CanBuy(220000, 200000, -1));
    }

    [Fact]
    public void Offers_prefer_an_upgrade_then_the_eight_level_band_then_cheap_actual_prices()
    {
        SmithSupportOffer[] offers =
        [
            new(100, 10, 1, 1), new(101, 22, 2, 100), new(102, 29, 3, 700),
            new(103, 28, 4, 300), new(104, 31, 5, 1), new(105, 30, 6, -1),
            new(106, 30, 7, 8001), new(107, 30, 0, 1),
        ];
        Assert.Equal(new long[] { 103, 102, 101, 100 },
            SmithSupportPurchase.Rank(offers, 30, 400000, 200000).Select(offer => offer.Gid));
    }

    [Fact]
    public void Returned_uid_is_an_admitted_matching_bag_stack_that_actually_increased()
    {
        var before = new Dictionary<int, int> { [10] = 1, [11] = 1 };
        (int Uid, long Gid, int Quantity, bool Admitted)[] after =
        [
            (10, 100, 1, true), // pre-existing support, not purchased
            (11, 100, 2, false), // rejected binding or position
            (12, 999, 1, true), // unrelated incoming item
            (13, 100, 1, true),
        ];
        Assert.Equal(13, SmithSupportPurchase.AcquiredUid(100, before, after));
        Assert.Equal(0, SmithSupportPurchase.AcquiredUid(200, before, after));
        Assert.Equal(11, SmithSupportPurchase.AcquiredUid(100, before, [(11, 100, 2, true)]));
    }

    [Fact]
    public void Query_derives_the_profession_and_slots_and_excludes_pets_and_overlevel_items()
    {
        using var fake = new FakeGameDb();
        using (var writer = new SqliteConnection($"Data Source={fake.File}"))
        {
            writer.Open();
            using var command = writer.CreateCommand();
            command.CommandText = """
                alter table ItemData add column typeId integer;
                alter table SkillData add column parentJobId integer;
                alter table SkillData add column isForgemagus integer;
                create table ItemTypeData(id integer, superTypeId integer);
                create table ItemSuperTypeData(id integer, possiblePositions text);
                create table SkillData_modifiableItemTypeIds_junction(SkillData_id text, target_id text);
                insert into ItemSuperTypeData values(1,'[6]'),(2,'[8]'),(3,'[]');
                insert into ItemTypeData values(10,1),(20,2),(30,3);
                insert into SkillData(id,parentJobId,isForgemagus) values(9876,64,1),(9877,62,1),(9878,99,0);
                insert into SkillData_modifiableItemTypeIds_junction values('9876','10'),('9876','20'),('9876','30'),('9877','10'),('9878','10');
                insert into ItemData(id,level,typeId) values(1000,1,10),(1001,10,10),(1002,11,10),(1003,10,20),(1004,10,30);
                """;
            command.ExecuteNonQuery();
        }
        using var db = GameDb.TryOpen(fake.File)!;
        Assert.Equal(new long[] { 1001, 1000 }, db.SmithLevelingSupports(64, 10).Select(item => item.Gid));
        Assert.Equal(1000, Assert.Single(db.SmithLevelingSupports(64, 1)).Gid);
        Assert.Equal(1000, Assert.Single(db.SmithLevelingSupports(62, 1)).Gid);
        Assert.Empty(db.SmithLevelingSupports(99, 10));
        Assert.Empty(db.SmithLevelingSupports(64, 0));
        Assert.Empty(db.SmithLevelingSupports(63, 10));
    }

    [FactIfGameData]
    public void Installed_data_has_level_one_costumage_and_cordomage_supports()
    {
        using var db = GameDb.TryOpen(RealGameData.Path!)!;
        Assert.NotEmpty(db.SmithLevelingSupports(64, 1));
        Assert.NotEmpty(db.SmithLevelingSupports(62, 1));
    }
}
