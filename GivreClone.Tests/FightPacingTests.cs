using System;
using GivreClone.App.Services.Combat;
using Xunit;

namespace GivreClone.Tests;

public class FightPacingTests
{
    [Fact]
    public void First_fight_of_a_run_is_never_delayed()
        => Assert.Equal(0, FightPacing.RemainingPauseMs(0, 1_000_000, 20, 40, new Random(1)));

    [Fact]
    public void Back_to_back_fight_waits_within_the_configured_range()
    {
        // Live 05/11 00:55-00:57 : 8 combats enchaînés à 2-6 s d'intervalle, puis suspension de 5 min.
        int ms = FightPacing.RemainingPauseMs(lastFightEndTick: 100_000, nowTick: 103_000, 20, 40, new Random(7));

        Assert.InRange(ms, 20_000 - 3_000, 40_000 - 3_000);
    }

    [Fact]
    public void Time_already_spent_since_the_fight_counts_toward_the_pause()
        => Assert.Equal(0, FightPacing.RemainingPauseMs(100_000, 100_000 + 41_000, 20, 40, new Random(3)));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, -1)]
    public void Disabled_or_invalid_range_never_waits(int min, int max)
        => Assert.Equal(0, FightPacing.RemainingPauseMs(100_000, 100_500, min, max, new Random(5)));

    [Fact]
    public void Inverted_bounds_are_normalised()
        => Assert.InRange(FightPacing.RemainingPauseMs(100_000, 100_000, 40, 20, new Random(9)), 20_000, 40_000);
}
