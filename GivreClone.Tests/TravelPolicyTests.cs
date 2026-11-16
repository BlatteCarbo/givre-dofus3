using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class TravelPolicyTests
{
    [Fact]
    public void Default_mode_never_forces_walking()
        => Assert.False(TravelPolicy.WalkFirst(preferWalk: false, walkMaxHops: 0, walkHops: 40, unreachableOnFoot: false));

    [Fact]
    public void Prefer_walk_walks_whenever_a_walking_path_exists()
    {
        Assert.True(TravelPolicy.WalkFirst(true, 0, 60, false));      // Astrub -> Bonta : à pied, même long
        Assert.False(TravelPolicy.WalkFirst(true, 0, 0, true));       // Astrub -> Otomaï : pas de chemin à pied -> zaap / havre-sac
    }

    [Fact]
    public void Prefer_walk_respects_the_max_distance()
    {
        Assert.True(TravelPolicy.WalkFirst(true, 30, 30, false));
        Assert.False(TravelPolicy.WalkFirst(true, 30, 31, false));    // au-delà : règle habituelle (zaap si plus court)
    }
}
