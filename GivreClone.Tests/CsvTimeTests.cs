using System;
using GivreClone.App.Services.Eco;
using Xunit;

namespace GivreClone.Tests;

public class CsvTimeTests
{
    private static readonly TimeZoneInfo Paris = TimeZoneInfo.CreateCustomTimeZone("test-paris", TimeSpan.FromHours(1), "Paris", "Paris",
        "Paris été", new[] { TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday)) });

    [Theory]
    [InlineData("2026-07-01T10:00:00Z", "2026-07-01 12:00:00")]
    [InlineData("2026-11-03T10:00:00Z", "2026-11-03 11:00:00")]
    public void Local_column_follows_daylight_saving_while_the_utc_column_stays_exact(string utc, string local)
    {
        long ms = DateTimeOffset.Parse(utc).ToUnixTimeMilliseconds();
        Assert.Equal(local, CsvTime.Local(ms, Paris));
        Assert.Equal(utc, CsvTime.Utc(ms));
    }
}
