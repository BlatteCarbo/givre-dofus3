using GivreClone.App.Services.Expedition;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public class MonsterGroupReachabilityTests
{
    [Fact]
    public void An_isolated_nearby_group_does_not_hide_a_reachable_group()
    {
        int source = 240;
        var p = MapPathfinder.MapPointOf(source);
        int Cell(int x, int y) => MapPathfinder.CellFromMapPoint(p.X + x, p.Y + y);
        var near = new MapActor(Cell(0, 3), 0, -1, "isolé", MapActorKind.Monster);
        var far = new MapActor(Cell(5, 0), 0, -2, "accessible", MapActorKind.Monster);
        var walk = Enumerable.Range(0, 5).Select(x => Cell(x, 0)).ToHashSet();
        Assert.Same(far, MonsterGroupReachability.Select(new[] { near, far }, source, -1, walk.Contains));
        Assert.Null(MonsterGroupReachability.Select(new[] { near }, source, -1, walk.Contains));
        Assert.Null(MonsterGroupReachability.Select(new[] { far }, source, -1, walk.Contains, (_, _) => false));
    }

    [Fact]
    public void Unknown_geometry_keeps_the_existing_attempt_instead_of_assuming_no_path()
    {
        var group = new MapActor(300, 0, -1, "groupe", MapActorKind.Monster);
        Assert.Same(group, MonsterGroupReachability.Select(new[] { group }, 240, -1, _ => false, geometryKnown: false));
    }
}
