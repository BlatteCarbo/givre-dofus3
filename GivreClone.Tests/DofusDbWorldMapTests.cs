using System.Collections.Generic;
using System.Linq;
using GivreClone.App.Services.WorldMap;
using Xunit;

namespace GivreClone.Tests;

public class DofusDbWorldMapTests
{
    private static DofusDbWorldMap Sample() => new(new Dictionary<long, (int X, int Y, int World)>
    {
        [200] = (0, 0, 1), [100] = (0, 0, 1),   // même coordonnée : la plus petite id gagne
        [101] = (1, 0, 1), [102] = (1, 1, 1), [300] = (5, 5, 2),
    });

    [Fact]
    public void One_map_per_coordinate_the_lowest_id_and_per_world()
    {
        var m = Sample();
        Assert.Equal(100, m.MapAt(1, 0, 0));
        Assert.Null(m.MapAt(2, 0, 0));
        Assert.Equal(new[] { 1, 2 }, m.Worlds.ToArray());
        Assert.Equal(3, m.TileCount(1));
        Assert.Equal((0, 0, 1, 1), m.Bounds(1) is { } b ? (b.MinX, b.MinY, b.MaxX, b.MaxY) : default);
    }

    [Fact]
    public void TilesIn_returns_only_known_cells_of_the_rectangle()
    {
        var ids = Sample().TilesIn(1, 0, 0, 1, 0).Select(t => t.MapId).OrderBy(i => i).ToArray();
        Assert.Equal(new long[] { 100, 101 }, ids);
    }

    [Fact]
    public void Screen_conversion_round_trips_including_negative_coordinates()
    {
        foreach (var (x, y) in new[] { (0, 0), (3, -7), (-12, 40) })
        {
            var (px, py) = DofusDbWorldMap.ToScreen(x, y, 0.4, 100, -50);
            Assert.Equal((x, y), DofusDbWorldMap.FromScreen(px + 1, py + 1, 0.4, 100, -50));
        }
    }
}

public class CoordDirectionsTests
{
    [Fact]
    public void Larger_axis_first_and_empty_when_arrived()
    {
        Assert.Equal(new[] { "right", "top" }, CoordWalk.Directions(0, 5, 4, 3));
        Assert.Equal(new[] { "bottom", "left" }, CoordWalk.Directions(2, 0, 1, 6));
        Assert.Empty(CoordWalk.Directions(1, 1, 1, 1));
    }
}
