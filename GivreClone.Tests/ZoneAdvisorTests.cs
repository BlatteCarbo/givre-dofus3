using System.Linq;
using GivreClone.App.Services.Progression;
using Xunit;

namespace GivreClone.Tests;

public class ZoneAdvisorTests
{
    private static ZoneInfo Z(int id, string name, int lvl, int maps = 8, int count = 4) =>
        new(id, "Zone", name, Enumerable.Range(0, count).Select(_ => (lvl, lvl)).ToList(), maps);

    [Fact]
    public void Fit_depends_on_the_monster_to_character_level_ratio()
    {
        Assert.Equal(ZoneFit.TooEasy, ZoneAdvisor.Evaluate(Z(1, "a", 50), 100)!.Fit);
        Assert.Equal(ZoneFit.Good, ZoneAdvisor.Evaluate(Z(2, "b", 100), 100)!.Fit);
        Assert.Equal(ZoneFit.Good, ZoneAdvisor.Evaluate(Z(3, "c", 120), 100)!.Fit);
        Assert.Equal(ZoneFit.Hard, ZoneAdvisor.Evaluate(Z(4, "d", 135), 100)!.Fit);
        Assert.Equal(ZoneFit.TooHard, ZoneAdvisor.Evaluate(Z(5, "e", 200), 100)!.Fit);
    }

    [Fact]
    public void Rank_prefers_the_ideal_range_hides_hard_zones_and_empty_zones()
    {
        var zones = new[] { Z(1, "facile", 40), Z(2, "ideal", 110), Z(3, "limite", 80), Z(4, "dur", 135), Z(5, "sans carte", 110, maps: 0) };
        var top = ZoneAdvisor.Rank(zones, 100, 10).Select(a => a.Zone.Name).ToArray();
        Assert.Equal("ideal", top[0]);
        Assert.DoesNotContain("dur", top);
        Assert.DoesNotContain("facile", top);
        Assert.DoesNotContain("sans carte", top);
        Assert.Contains("dur", ZoneAdvisor.Rank(zones, 100, 10, includeHard: true).Select(a => a.Zone.Name));
    }

    [Fact]
    public void A_zone_without_monster_levels_is_ignored()
    {
        Assert.Null(ZoneAdvisor.Evaluate(new ZoneInfo(1, "a", "b", new (int, int)[0], 5), 100));
    }
}
