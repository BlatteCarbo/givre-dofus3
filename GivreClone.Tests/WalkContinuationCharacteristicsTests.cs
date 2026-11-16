using GivreClone.App.Services.CombatParity;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public class WalkContinuationCharacteristicsTests
{
    [Theory]
    [InlineData(4, 100, true)]
    [InlineData(3, 100, false)]
    [InlineData(4, 90, false)]
    public void Confirmed_walk_accepts_only_expected_pm_consumption(int remaining, int vitality, bool matches)
    {
        var before = new Dictionary<int, int> { [FightStatsReader.IdPm] = 5, [FightStatsReader.IdVitality] = 100 };
        var after = new Dictionary<int, int> { [FightStatsReader.IdPm] = remaining, [FightStatsReader.IdVitality] = vitality };

        Assert.Equal(matches, WalkContinuationCharacteristics.Match(before, after, 4));
    }

    [Fact]
    public void First_authoritative_pm_observation_must_match_expected_remaining_points()
    {
        var before = new Dictionary<int, int> { [FightStatsReader.IdPa] = 8 };
        var after = new Dictionary<int, int>(before) { [FightStatsReader.IdPm] = 4 };

        Assert.True(WalkContinuationCharacteristics.Match(before, after, 4));
    }

    [Theory]
    [InlineData(FightStatsReader.IdPa)]
    [InlineData(FightStatsReader.IdRange)]
    public void Walk_does_not_accept_other_characteristic_changes(int characteristic)
    {
        var before = new Dictionary<int, int> { [characteristic] = 8, [FightStatsReader.IdPm] = 5 };
        var after = new Dictionary<int, int> { [characteristic] = 7, [FightStatsReader.IdPm] = 4 };

        Assert.False(WalkContinuationCharacteristics.Match(before, after, 4));
    }

    [Fact]
    public void Missing_authoritative_pm_does_not_validate_a_previous_observation()
    {
        var before = new Dictionary<int, int> { [FightStatsReader.IdPm] = 5 };

        Assert.False(WalkContinuationCharacteristics.Match(before, new Dictionary<int, int>(), 4));
    }
}
