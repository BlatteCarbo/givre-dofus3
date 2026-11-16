using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public sealed class JobProgressSnapshotTests
{
    [Fact]
    public void Unknown_xp_is_never_displayed_as_zero_percent_progress()
    {
        var progress = new JobProgressSnapshot(112);
        Assert.Equal(112, progress.Level);
        Assert.False(progress.HasExperience);
        Assert.Equal("XP non disponible", progress.ExperienceText);
        Assert.Equal(0, progress.Percent);
    }

    [Theory]
    [InlineData(130L, 100L, 200L, 30d)]
    [InlineData(100L, 100L, 200L, 0d)]
    [InlineData(250L, 100L, 200L, 100d)]
    [InlineData(12L, 12L, 13L, 0d)]
    public void Progress_is_relative_to_the_level_interval(long xp, long floor, long next, double expected)
    {
        var progress = new JobProgressSnapshot(50, xp, floor, next);
        Assert.True(progress.HasExperience);
        Assert.Equal(expected, progress.Percent);
    }

    [Theory]
    [InlineData(120L, 200L, 150L)]
    [InlineData(40L, 50L, 70L)]
    [InlineData(0L, 0L, 0L)]
    public void Invalid_or_missing_thresholds_do_not_claim_a_percentage(long xp, long floor, long next)
    {
        var progress = new JobProgressSnapshot(12, xp, floor, next);
        Assert.False(progress.HasExperience);
        Assert.Equal("XP non disponible", progress.ExperienceText);
    }

    [Fact]
    public void Level_up_clears_stale_xp_until_next_snapshot()
    {
        var old = new JobProgressSnapshot(22, 230, 200, 300);
        var updated = old.WithLevelUp(23);
        Assert.Equal(23, updated.Level);
        Assert.False(updated.HasExperience);
        Assert.Equal(old, old.WithLevelUp(22));
    }

}
