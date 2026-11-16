using System;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public sealed class LogGrowthMonitorTests
{
    private static readonly long Start = new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc).Ticks;

    [Theory]
    [InlineData(999, false)]
    [InlineData(1000, true)]
    public void Alert_fires_at_the_threshold_within_the_window(long bytes, bool alert)
    {
        var monitor = new LogGrowthMonitor(TimeSpan.FromMinutes(1), 1000);

        monitor.Record(Start, bytes);

        Assert.Equal(alert, monitor.Alert(Start + TimeSpan.FromSeconds(30).Ticks));
    }

    [Fact]
    public void Burst_older_than_the_window_no_longer_counts()
    {
        var monitor = new LogGrowthMonitor(TimeSpan.FromMinutes(1), 1000);
        monitor.Record(Start, 5000);

        long later = Start + TimeSpan.FromMinutes(1).Ticks;

        Assert.Equal(0, monitor.BytesPerMinute(later));
        Assert.False(monitor.Alert(later));
    }

    [Fact]
    public void Rate_is_normalised_to_one_minute_for_a_shorter_window()
    {
        var monitor = new LogGrowthMonitor(TimeSpan.FromSeconds(30), 1000);
        monitor.Record(Start, 300);
        monitor.Record(Start + TimeSpan.FromSeconds(10).Ticks, 300);

        Assert.Equal(1200, monitor.BytesPerMinute(Start + TimeSpan.FromSeconds(20).Ticks));
    }
}
