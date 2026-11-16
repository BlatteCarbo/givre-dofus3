extern alias F06App;

using F06App::GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class MemoryTrimmerTests
{
    private const int ThresholdMb = 1536;
    private const long ThresholdBytes = 1536L * 1024L * 1024L;

    [Fact]
    public void Working_set_below_threshold_does_not_trim()
    {
        long workingSetBytes = ThresholdBytes - 1024;
        long lastTrimTicks = 0;
        long nowTicks = 100_000_000L;

        bool result = MemoryTrimmer.ShouldTrim(workingSetBytes, lastTrimTicks, nowTicks, ThresholdMb);

        Assert.False(result);
    }

    [Fact]
    public void Working_set_above_threshold_and_never_trimmed_trims()
    {
        long workingSetBytes = ThresholdBytes + 1024;
        long lastTrimTicks = 0;
        long nowTicks = 100_000_000L;

        bool result = MemoryTrimmer.ShouldTrim(workingSetBytes, lastTrimTicks, nowTicks, ThresholdMb);

        Assert.True(result);
    }

    [Fact]
    public void Working_set_above_threshold_but_trimmed_recently_does_not_trim()
    {
        long workingSetBytes = ThresholdBytes + 1024;
        long nowTicks = 100_000_000L;
        // Trimmed 2 minutes ago (120,000 ms)
        long lastTrimTicks = nowTicks - (2L * 60L * 1000L);

        bool result = MemoryTrimmer.ShouldTrim(workingSetBytes, lastTrimTicks, nowTicks, ThresholdMb);

        Assert.False(result);
    }

    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(true, 99_880_000, false)]
    [InlineData(true, 0, true)]
    [InlineData(true, 99_400_000, true)]
    public void Application_pressure_gate_controls_collection_and_working_set_release(
        bool aboveThreshold, long lastTrimTicks, bool expected)
    {
        var actions = new System.Collections.Generic.List<string>();
        bool applied = MemoryTrimmer.TrimApplication(ThresholdBytes + (aboveThreshold ? 1024 : -1024),
            lastTrimTicks, 100_000_000, ThresholdMb,
            () => actions.Add("collect"), () => actions.Add("release"));

        Assert.Equal(expected, applied);
        Assert.Equal(expected ? new[] { "collect", "release" } : System.Array.Empty<string>(), actions);
    }
}
