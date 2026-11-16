extern alias F06App;

using System.Collections.ObjectModel;
using Xunit;
using Account = GivreClone.Engine.Accounts.Account;
using Proxy = GivreClone.Engine.Proxies.ProxyEntry;
using Row = F06App::GivreClone.App.ViewModels.AccountRow;

namespace GivreClone.Tests;

public sealed class AccountSessionInfoTests
{
    private static Row NewRow() => new(new Account { Login = "a" }, new ObservableCollection<Proxy>(), "");

    [Theory]
    [InlineData(0, "0 min 00")]
    [InlineData(65, "1 min 05")]
    [InlineData(3599, "59 min 59")]
    [InlineData(3600 * 2 + 7 * 60, "2 h 07")]
    public void Uptime_is_formatted_from_the_launch_instant(int seconds, string expected)
    {
        var start = new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, Row.FormatUptime(start, start.AddSeconds(seconds)));
        Assert.Equal("", Row.FormatUptime(null, start));
        Assert.Equal("0 min 00", Row.FormatUptime(start, start.AddSeconds(-5)));
    }

    [Fact]
    public void Reconnecting_in_place_keeps_the_original_uptime_and_stopping_clears_it()
    {
        var row = NewRow();
        row.Running = true;
        var since = row.RunningSince;
        row.Activity = F06App::GivreClone.App.Services.BotActivity.Disconnected;
        row.Activity = F06App::GivreClone.App.Services.BotActivity.InGame;
        Assert.Equal(since, row.RunningSince);
        row.Running = false;
        Assert.Null(row.RunningSince);
        Assert.Equal("", row.UptimeText);
    }

    [Fact]
    public void Disconnect_history_keeps_the_ten_most_recent_reasons_newest_first()
    {
        var row = NewRow();
        var t0 = new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);
        for (int i = 0; i < 12; i++) row.RecordDisconnect($"motif {i}", t0.AddMinutes(i));
        Assert.Equal(10, row.DisconnectHistory.Count);
        Assert.EndsWith("motif 11", row.DisconnectHistory[0]);
        Assert.EndsWith("motif 2", row.DisconnectHistory[^1]);
        Assert.True(row.HasDisconnectHistory);
        Assert.StartsWith(row.DisconnectHistory[0], row.DisconnectHistoryText);
    }
}
