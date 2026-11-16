using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public sealed class LineOfSightRegressionTests
{
    [Fact] public void Refusal_352_259_313() => Check(352, 259, 313);
    [Fact] public void Refusal_240_372_299() => Check(240, 372, 299);
    [Fact] public void Refusal_456_285_327() => Check(456, 285, 327);
    [Fact] public void Refusal_371_213_328() => Check(371, 213, 328);

    private static void Check(int from, int to, int opaqueCell)
    {
        var fwd = MapPathfinder.LineCells(from, to);
        var rev = MapPathfinder.LineCells(to, from);

        Assert.Contains(opaqueCell, fwd);
        Assert.Contains(opaqueCell, rev);

        bool BlocksLos(int c) => c == opaqueCell;
        bool Occupied(int c) => false;

        Assert.False(MapPathfinder.HasLineOfSight(from, to, BlocksLos, Occupied));
        Assert.False(MapPathfinder.HasLineOfSight(to, from, BlocksLos, Occupied));
        Assert.False(MapPathfinder.HasLineOfSight(from, to, _ => false, c => c == opaqueCell));
        Assert.True(MapPathfinder.HasLineOfSight(from, to, _ => false, _ => false));
    }

    [Fact]
    public void Clear_control_segment_should_have_unobstructed_line_of_sight()
    {
        int from = 371;
        int to = 385;
        bool BlocksLos(int c) => false;
        bool Occupied(int c) => false;

        Assert.True(MapPathfinder.HasLineOfSight(from, to, BlocksLos, Occupied));
    }

    [Fact]
    public void Traversal_is_reversible_for_every_valid_endpoint_pair()
    {
        for (int from = 0; from < MapPathfinder.CellCount; from++)
        for (int to = from; to < MapPathfinder.CellCount; to++)
        {
            var forward = MapPathfinder.LineCells(from, to);
            var reverse = MapPathfinder.LineCells(to, from);
            Assert.Equal(forward.Count, reverse.Count);
            for (int i = 0; i < forward.Count; i++)
                Assert.Equal(forward[i], reverse[reverse.Count - 1 - i]);
        }
    }

    [Fact]
    public void Diagonal_corner_touches_do_not_block_and_endpoints_are_excluded()
    {
        // (20,-6) -> (19,-5) touches the corners of (19,-6) and (20,-5).
        Assert.Equal(new[] { 371, 343 }, MapPathfinder.LineCells(371, 343));
        Assert.True(MapPathfinder.HasLineOfSight(371, 343, _ => true, _ => true));
        Assert.Equal(new[] { 371 }, MapPathfinder.LineCells(371, 371));
        Assert.Empty(MapPathfinder.LineCells(-1, 371));
        Assert.Empty(MapPathfinder.LineCells(371, MapPathfinder.CellCount));
    }
}
