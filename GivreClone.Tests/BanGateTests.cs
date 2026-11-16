using System;
using GivreClone.Engine.Accounts;
using Xunit;

namespace GivreClone.Tests;

public class BanGateTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Not_banned_never_blocks()
        => Assert.False(BanGate.BlocksAutoLaunch(new Account(), Now));

    [Fact]
    public void Ban_without_readable_end_blocks_until_cleared()
    {
        var a = new Account();
        BanGate.Record(a, Now, "");
        Assert.True(BanGate.BlocksAutoLaunch(a, Now.AddYears(5)));
        BanGate.Clear(a);
        Assert.False(BanGate.BlocksAutoLaunch(a, Now));
    }

    [Theory]
    [InlineData("2026-10-30T00:00:00Z")]
    [InlineData("1793318400")]      // secondes epoch = 30/10/2026
    [InlineData("1793318400000")]   // millisecondes epoch
    public void Ban_with_end_date_stops_blocking_after_it(string end)
    {
        var a = new Account();
        BanGate.Record(a, Now, end);
        Assert.True(BanGate.BlocksAutoLaunch(a, Now));
        Assert.False(BanGate.BlocksAutoLaunch(a, new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Expired_ban_mark_is_cleared()
    {
        var a = new Account();
        BanGate.Record(a, Now, "2026-10-23T12:05:00Z");

        Assert.False(BanGate.ClearIfExpired(a, Now));
        Assert.True(BanGate.IsBanned(a));
        Assert.True(BanGate.ClearIfExpired(a, Now.AddMinutes(6)));
        Assert.False(BanGate.IsBanned(a));
        Assert.Equal("", a.BanEnd);
    }

    [Fact]
    public void Ban_without_readable_end_is_never_cleared_by_expiry()
    {
        var a = new Account();
        BanGate.Record(a, Now, "");

        Assert.False(BanGate.ClearIfExpired(a, Now.AddYears(5)));
        Assert.True(BanGate.IsBanned(a));
    }

    [Fact]
    public void Manual_wake_before_end_relaunches_but_keeps_the_active_ban_mark()
    {
        var a = new Account();
        BanGate.Record(a, Now, "2026-10-23T12:05:00Z");

        Assert.True(BanGate.CompleteResumeWait(a, Now.AddMinutes(1), manualWake: true, autoRelaunchEnabled: true));
        Assert.True(BanGate.IsBanned(a));
        Assert.Equal("2026-10-23T12:05:00Z", a.BanEnd);
        Assert.True(BanGate.BlocksAutoLaunch(a, Now.AddMinutes(1)));
    }

    [Fact]
    public void Timer_elapsed_resume_clears_the_expired_mark_and_relaunches()
    {
        var a = new Account();
        BanGate.Record(a, Now, "2026-10-23T12:05:00Z");
        var due = Now + BanGate.AutoResumeDelay(a, Now)!.Value;

        Assert.True(BanGate.CompleteResumeWait(a, due, manualWake: false, autoRelaunchEnabled: true));
        Assert.False(BanGate.IsBanned(a));
    }

    [Fact]
    public void Disabling_auto_relaunch_during_the_wait_cancels_the_timed_resume_and_keeps_the_ban()
    {
        var a = new Account();
        BanGate.Record(a, Now, "2026-10-23T12:05:00Z");
        var due = Now + BanGate.AutoResumeDelay(a, Now)!.Value;

        Assert.False(BanGate.CompleteResumeWait(a, due, manualWake: false, autoRelaunchEnabled: false));
        Assert.True(BanGate.IsBanned(a));
    }

    [Fact]
    public void Manual_wake_is_explicit_and_ignores_the_auto_relaunch_setting()
    {
        var a = new Account();
        BanGate.Record(a, Now, "2026-10-23T12:05:00Z");

        Assert.True(BanGate.CompleteResumeWait(a, Now.AddMinutes(10), manualWake: true, autoRelaunchEnabled: false));
        Assert.False(BanGate.IsBanned(a));
    }

    [Fact]
    public void Temporary_ban_resumes_once_just_after_its_end()
    {
        // Live 04/11 23:27 : refus BANNED avec ban_end_date ~5 min après la déconnexion.
        var a = new Account();
        BanGate.Record(a, Now, "2026-10-23T12:05:00Z");

        Assert.Equal(TimeSpan.FromMinutes(5) + BanGate.ResumeMargin, BanGate.AutoResumeDelay(a, Now));
    }

    [Fact]
    public void Already_expired_ban_waits_only_the_safety_margin()
    {
        var a = new Account();
        BanGate.Record(a, Now, "2026-10-23T11:00:00Z");

        Assert.Equal(BanGate.ResumeMargin, BanGate.AutoResumeDelay(a, Now));
    }

    [Theory]
    [InlineData("")]                       // sans date : déblocage manuel
    [InlineData("2026-10-30T00:00:00Z")]  // une semaine : pas une suspension temporaire
    public void Ban_without_end_or_too_long_never_auto_resumes(string end)
    {
        var a = new Account();
        BanGate.Record(a, Now, end);

        Assert.Null(BanGate.AutoResumeDelay(a, Now));
    }
}
