using GivreClone.App.Services.Expedition;
using Xunit;

namespace GivreClone.Tests;

public class ZoneValueTests
{
    private static decimal? Price(long gid) => gid switch { 1 => 100m, 2 => 1000m, _ => null };

    [Fact]
    public void Monster_value_is_rate_times_price_and_counts_unpriced()
    {
        var m = new MonsterDropSet(7, "Bouftou", new[] { new MonsterDrop(1, 50), new MonsterDrop(2, 1), new MonsterDrop(9, 10), new MonsterDrop(1, 0) });
        var v = ZoneValue.ForMonster(m, Price);
        Assert.Equal(60m, v.Value);       // 50 + 10
        Assert.Equal(1, v.UnpricedDrops); // gid 9 ; le drop à 0 % est ignoré
    }

    [Fact]
    public void Zone_averages_and_ranks_best_first()
    {
        var a = new MonsterDropSet(1, "A", new[] { new MonsterDrop(2, 10) });   // 100
        var b = new MonsterDropSet(2, "B", new[] { new MonsterDrop(1, 20) });   // 20
        var r = ZoneValue.ForZone(new[] { b, a }, Price, 1)!;
        Assert.Equal(60m, r.AveragePerMonster);
        Assert.Equal("A", Assert.Single(r.Best).Name);
        Assert.Null(ZoneValue.ForZone(System.Array.Empty<MonsterDropSet>(), Price));
    }

    [Fact]
    public void Dungeon_run_net_is_boss_drops_minus_keys_and_null_when_key_unpriced()
    {
        var boss = new MonsterDropSet(1, "Boss", new[] { new MonsterDrop(2, 10) });   // 100
        var ok = DungeonProfit.Estimate(new[] { (1L, 2) }, new[] { boss }, Price);     // clés 200
        Assert.Equal(200m, ok.KeyCost);
        Assert.Equal(100m, ok.BossDropValue);
        Assert.Equal(-100m, ok.Net);
        var unknown = DungeonProfit.Estimate(new[] { (1L, 1), (9L, 1) }, new[] { boss }, Price);
        Assert.Null(unknown.Net);
        Assert.Equal(1, unknown.UnpricedKeys);
    }

    [Fact]
    public void Percent_is_capped_at_100()
        => Assert.Equal(100m, ZoneValue.ForMonster(new(1, "x", new[] { new MonsterDrop(1, 250) }), Price).Value);
}
