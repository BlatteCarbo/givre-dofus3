using GivreClone.App.Services.Combat;
using Xunit;

namespace GivreClone.Tests;

public class MonsterThreatCatalogTests
{
    private const string Json = "{\"2845\":{\"1\":[10,5,1,231],\"3\":[11,5,2,300]},\"99\":{\"2\":[6,3,7,80]}}";

    [Fact]
    public void Exact_grade_is_read_with_its_reach_and_turn_damage()
    {
        var catalog = MonsterThreatCatalog.Parse(Json);
        Assert.Equal(3, catalog.Count);
        Assert.Equal(new MonsterThreat(11, 5, 2, 300), catalog.Find(2845, 3));
    }

    [Fact]
    public void Missing_grade_falls_back_to_nearest_lower_then_lowest_known()
    {
        var catalog = MonsterThreatCatalog.Parse(Json);
        Assert.Equal(new MonsterThreat(10, 5, 1, 231), catalog.Find(2845, 2));
        Assert.Equal(new MonsterThreat(6, 3, 7, 80), catalog.Find(99, 1));
        Assert.Null(catalog.Find(12345, 1));
    }

    [Fact]
    public void Incomplete_attack_rows_preserve_stats_without_claiming_known_threat()
    {
        var catalog = MonsterThreatCatalog.Parse(
            "{\"1\":{\"1\":[10,5,0,0]},\"2\":{\"1\":[10,5,6,0]},\"3\":{\"1\":[10,5,0,454]},\"4\":{\"1\":[10,5,1,454]}}");

        Assert.Equal(10, catalog.Find(1, 1)!.Ap);
        Assert.Equal(5, catalog.Find(1, 1)!.Mp);
        Assert.False(catalog.Find(1, 1)!.AttackKnown);
        Assert.False(catalog.Find(2, 1)!.AttackKnown);
        Assert.False(catalog.Find(3, 1)!.AttackKnown);
        Assert.True(catalog.Find(4, 1)!.AttackKnown);
    }

    [Fact]
    public void Missing_file_yields_an_empty_catalog()
    {
        var catalog = MonsterThreatCatalog.Load(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "no-such-dofensive-" + System.Guid.NewGuid() + ".json"));
        Assert.Equal(0, catalog.Count);
    }
    [Fact]
    public void Installed_bestiary_gives_sanglacier_its_melee_reach_and_real_turn_damage()
    {
        if (!System.IO.File.Exists(MonsterThreatCatalog.DefaultPath)) return;
        var threat = MonsterThreatCatalog.Load(MonsterThreatCatalog.DefaultPath).Find(2845, 1);
        Assert.NotNull(threat);
        Assert.Equal(10, threat!.Ap);
        Assert.Equal(5, threat.Mp);
        Assert.True(threat.TurnDamage >= 454, $"turn damage {threat.TurnDamage}");
    }
}
