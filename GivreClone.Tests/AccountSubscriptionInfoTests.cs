using System;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class AccountSubscriptionInfoTests
{
    [Fact]
    public void New_identification_clears_previous_subscription_without_touching_other_accounts()
    {
        AccountSubscriptionInfo.Set(4250, "2030-01-01T00:00:00Z", false, false);
        AccountSubscriptionInfo.Set(4251, null, false, true);
        AccountSubscriptionInfo.Clear(4250);
        AccountSubscriptionInfo.Clear(4250);
        Assert.Null(AccountSubscriptionInfo.EffectiveEnd(AccountSubscriptionInfo.Get(4250)));
        Assert.Equal(2999, AccountSubscriptionInfo.EffectiveEnd(AccountSubscriptionInfo.Get(4251))!.Value.Year);
        AccountSubscriptionInfo.Set(4250, "2031-01-01T00:00:00Z", false, false);
        Assert.Equal(2031, AccountSubscriptionInfo.Get(4250)!.EndUtc!.Value.Year);
        AccountSubscriptionInfo.Clear(4250);
        AccountSubscriptionInfo.Clear(4251);
    }

    [Fact]
    public void Parses_iso_dates_as_utc_and_ignores_empty_or_garbage()
    {
        Assert.Equal(new DateTime(2026, 10, 25, 20, 7, 0, DateTimeKind.Utc), AccountSubscriptionInfo.ParseEnd("2026-10-25T20:07:00Z"));
        Assert.Equal(new DateTime(2026, 10, 25, 20, 7, 0, DateTimeKind.Utc), AccountSubscriptionInfo.ParseEnd("2026-10-25T22:07:00+02:00"));
        Assert.Null(AccountSubscriptionInfo.ParseEnd(""));
        Assert.Null(AccountSubscriptionInfo.ParseEnd("pas une date"));
    }

    [Fact]
    public void Effective_end_covers_unlimited_free_and_unknown()
    {
        Assert.Null(AccountSubscriptionInfo.EffectiveEnd(null));
        Assert.Equal(DateTime.UnixEpoch, AccountSubscriptionInfo.EffectiveEnd(new AccountSubscriptionInfo.Info(null, false, false)));   // non abonné
        Assert.Equal(2999, AccountSubscriptionInfo.EffectiveEnd(new AccountSubscriptionInfo.Info(null, false, true))!.Value.Year);
        var d = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(d, AccountSubscriptionInfo.EffectiveEnd(new AccountSubscriptionInfo.Info(d, false, false)));
    }

    [Fact]
    public void Registry_is_per_pid()
    {
        AccountSubscriptionInfo.Set(4242, "2030-01-01T00:00:00Z", false, false);
        Assert.Equal(2030, AccountSubscriptionInfo.Get(4242)!.EndUtc!.Value.Year);
        Assert.Null(AccountSubscriptionInfo.Get(4243));
    }
}
