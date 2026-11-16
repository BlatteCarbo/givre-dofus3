using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public class NavigationProbeTests
{
    private static MapCell[] Grid() => Enumerable.Range(0, 560)
        .Select(i => new MapCell(i, true, true, 0)).ToArray();

    [Fact]
    public void Current_complete_geometry_produces_a_route_without_sending_it()
    {
        var result = NavigationProbe.Evaluate(5, 5, 5, true, 240, Grid(), 226);
        Assert.Equal("path_found", result.Status);
        Assert.Equal(new[] { 240, 226 }, result.Cells);
    }

    [Theory]
    [InlineData(0, 0, 0, true, "map_unknown")]
    [InlineData(5, 4, 5, true, "geometry_stale")]
    [InlineData(5, 5, 4, true, "position_stale")]
    [InlineData(5, 5, 5, false, "position_unconfirmed")]
    public void Incomplete_state_returns_an_explicit_reason(long map, long geometry, long position,
        bool confirmed, string status)
    {
        var result = NavigationProbe.Evaluate(map, geometry, position, confirmed, 240, Grid(), 226);
        Assert.Equal(status, result.Status);
        Assert.Empty(result.Cells);
    }

    [Fact]
    public void Partial_geometry_is_not_treated_as_a_ready_map()
    {
        var result = NavigationProbe.Evaluate(5, 5, 5, true, 240, Grid()[..100], 226);
        Assert.Equal("geometry_incomplete", result.Status);
    }

    [Fact]
    public void Custom_obstacles_block_the_destination()
    {
        var result = NavigationProbe.Evaluate(5, 5, 5, true, 240, Grid(), 226,
            customObstacles: new[] { 226 });
        Assert.Equal("target_blocked", result.Status);
        Assert.Empty(result.Cells);
    }

    [Fact]
    public void Fight_only_obstacles_are_respected()
    {
        var grid = Grid();
        grid[226] = grid[226] with { NonWalkableDuringFight = true };
        Assert.Equal("target_blocked", NavigationProbe.Evaluate(5, 5, 5, true, 240, grid, 226, inFight: true).Status);
        Assert.Equal("path_found", NavigationProbe.Evaluate(5, 5, 5, true, 240, grid, 226).Status);
    }

    [Fact]
    public void Duplicate_cell_ids_do_not_count_as_complete_geometry()
    {
        var grid = Grid();
        grid[559] = grid[558];
        Assert.Equal("geometry_incomplete", NavigationProbe.Evaluate(5, 5, 5, true, 240, grid, 226).Status);
    }

    [Fact]
    public void Already_at_target_is_distinct_from_no_path()
    {
        Assert.Equal("already_there", NavigationProbe.Evaluate(5, 5, 5, true, 240, Grid(), 240).Status);
    }
}
