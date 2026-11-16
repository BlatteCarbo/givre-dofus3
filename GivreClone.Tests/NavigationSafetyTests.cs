using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public class NavigationSafetyTests
{
    [Fact]
    public void Blocked_destination_is_never_encoded_as_a_movement()
    {
        var path = MapPathfinder.KeyMovements(240, 226, c => c != 226);
        Assert.Empty(path);
    }

    [Fact]
    public void Sector_permission_does_not_allow_crossing_two_blocked_corners()
    {
        // These two cells are diagonal in the pathfinder's coordinate system.
        var path = MapPathfinder.KeyMovements(240, 241, c => c is 240 or 241,
            stepAllowed: (_, _) => true);
        Assert.Empty(path);
    }

    [Fact]
    public void A_blocked_source_can_still_be_left()
    {
        var path = MapPathfinder.KeyMovements(240, 226, c => c != 240);
        Assert.NotEmpty(path);
        Assert.Equal(226, path[^1] & 0xFFF);
    }
}
