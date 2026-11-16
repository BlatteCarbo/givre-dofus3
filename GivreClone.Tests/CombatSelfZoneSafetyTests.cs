extern alias F06App;
using GivreClone.Protocol;
using Safety = F06App::GivreClone.App.Services.Combat.CombatSelfZoneSafety;
using Xunit;

namespace GivreClone.Tests;

public class CombatSelfZoneSafetyTests
{
    [Fact]
    public void Unknown_zone_does_not_invent_a_radius_even_with_a_nearby_enemy()
    {
        var result = Safety.Evaluate(null, 200, 200, new[] { 200 }, Array.Empty<int>(), 1);
        Assert.False(result.Allowed);
        Assert.Equal("unknown_zone", result.Reason);
    }

    [Fact]
    public void Known_zone_enforces_minimum_hits_and_protects_other_allies()
    {
        var cells = CombatZones.GetCells('C', 200, CombatZones.OrientationTo(200, 200), 0, 1).Distinct().ToArray();
        Assert.True(cells.Length >= 2);
        var zone = ((int)'C', 1, 0);
        Assert.True(Safety.Evaluate(zone, 200, 200, cells.Take(2), Array.Empty<int>(), 2).Allowed);
        Assert.Equal("too_few_enemies", Safety.Evaluate(zone, 200, 200, cells.Take(1), Array.Empty<int>(), 2).Reason);
        Assert.Equal("ally_in_zone", Safety.Evaluate(zone, 200, 200, cells.Take(1), cells.Skip(1).Take(1), 1).Reason);
        Assert.False(Safety.Evaluate(zone, 200, 200, Array.Empty<int>(), Array.Empty<int>(), 0).Allowed);
    }
}
