using System.Collections.Generic;
using GivreClone.Engine;
using Xunit;

namespace GivreClone.Tests;

public sealed class ChallengeFinishPolicyTests
{
    [Fact]
    public void Multiple_full_health_enemies_can_all_die_to_one_area()
        => Assert.True(ChallengeFinishPolicy.CanFinishArea(new[] { (114, (int?)40), (128, (int?)55), (142, (int?)60) }, new HashSet<int> { 114, 128, 142 }, 60));

    [Theory]
    [InlineData(null, 100, false)]
    [InlineData(61, 60, false)]
    [InlineData(60, 60, true)]
    [InlineData(60, 0, false)]
    public void Life_must_be_known_and_within_conservative_damage(int? life, int damage, bool expected)
        => Assert.Equal(expected, ChallengeFinishPolicy.CanFinishArea(new[] { (114, life) }, new HashSet<int> { 114 }, damage));

    [Fact]
    public void Enemies_outside_area_prevent_finish_prediction()
        => Assert.False(ChallengeFinishPolicy.CanFinishArea(new[] { (114, (int?)40), (128, (int?)40) }, new HashSet<int> { 114 }, 100));
}
