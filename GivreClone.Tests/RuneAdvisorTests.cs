using System.Collections.Generic;
using GivreClone.App.Services.Forgemagie;
using Xunit;

namespace GivreClone.Tests;

public class RuneAdvisorTests
{
    private static Dictionary<(string, string), int> Stock(params (string Stat, string Tier, int N)[] items)
    {
        var d = new Dictionary<(string, string), int>();
        foreach (var (s, t, n) in items) d[(s, t)] = n;
        return d;
    }

    [Fact]
    public void Picks_the_biggest_tier_that_fits_the_gap()
    {
        var a = RuneAdvisor.Advise(new[] { new ForgeLine("Vitalité", 100, 181, 230, 230) }, Stock(("Vitalité", "Ba", 9), ("Vitalité", "Pa", 9), ("Vitalité", "Ra", 9)), new AdvisorSettings(AllowOver: true));
        Assert.Equal(AdviceKind.Rune, a.Kind);
        Assert.Equal("Ra", a.Rune!.Tier);   // écart 130 : Ra (+50) tient
    }

    [Fact]
    public void Gap_of_three_uses_a_Pa_rune_when_it_fits()
    {
        var a = RuneAdvisor.Advise(new[] { new ForgeLine("Force", 12, 11, 15, 15) }, Stock(("Force", "Ba", 5), ("Force", "Pa", 5)));
        Assert.Equal("Pa", a.Rune!.Tier);   // écart 3 : Pa (+3) tient et 12 + 3 = 15 ne dépasse pas le max
    }

    [Fact]
    public void Never_exceeds_the_max_roll_without_over()
    {
        var line = new ForgeLine("Vitalité", 228, 181, 230, 235);
        var noOver = RuneAdvisor.Advise(new[] { line }, Stock(("Vitalité", "Ba", 9)));
        Assert.Equal(AdviceKind.NoRune, noOver.Kind);   // 228 + 5 > 230
        var over = RuneAdvisor.Advise(new[] { line }, Stock(("Vitalité", "Ba", 9)), new AdvisorSettings(AllowOver: true));
        Assert.Equal(AdviceKind.Rune, over.Kind);
    }

    [Fact]
    public void Cap_limits_the_target_and_done_is_reported()
    {
        var a = RuneAdvisor.Advise(new[] { new ForgeLine("Sagesse", 30, 26, 40, 40, Cap: 30) }, Stock(("Sagesse", "Ba", 9)));
        Assert.Equal(AdviceKind.Done, a.Kind);
    }

    [Fact]
    public void Pa_and_Ra_thresholds_and_switches_are_respected()
    {
        var line = new ForgeLine("Chance", 10, 26, 40, 40, PaFrom: 20);
        var a = RuneAdvisor.Advise(new[] { line }, Stock(("Chance", "Ba", 3), ("Chance", "Pa", 3)));
        Assert.Equal("Ba", a.Rune!.Tier);   // Pa interdite tant que le jet < 20
        var b = RuneAdvisor.Advise(new[] { line with { Current = 22 } }, Stock(("Chance", "Ba", 3), ("Chance", "Pa", 3)), new AdvisorSettings(AllowPa: false));
        Assert.Equal("Ba", b.Rune!.Tier);   // Pa coupée par les réglages
    }

    [Fact]
    public void Empty_stock_unknown_stat_and_no_lines_explain_themselves()
    {
        Assert.Equal(AdviceKind.NoRune, RuneAdvisor.Advise(new[] { new ForgeLine("Force", 5, 1, 9, 9) }, Stock()).Kind);
        var unknown = RuneAdvisor.Advise(new[] { new ForgeLine("Dommages", 1, 1, 9, 9) }, Stock());
        Assert.Equal(AdviceKind.NoRune, unknown.Kind);
        Assert.Equal(AdviceKind.NoLine, RuneAdvisor.Advise(new ForgeLine[0], Stock()).Kind);
    }

    [Fact]
    public void Moves_on_to_the_next_line_when_the_first_has_no_rune()
    {
        var a = RuneAdvisor.Advise(new[] { new ForgeLine("Force", 5, 1, 9, 9), new ForgeLine("Agilité", 5, 1, 9, 9) }, Stock(("Agilité", "Ba", 2)));
        Assert.Equal("Agilité", a.Stat);
    }

    [Fact]
    public void Reached_target_finishes_the_run_and_a_blocked_one_stops_with_the_reason()
    {
        var done = RuneAdvisor.Advise(new[] { new ForgeLine("Sagesse", 30, 26, 40, 40, Cap: 30) }, Stock(("Sagesse", "Ba", 9)));
        Assert.Equal(GivreClone.App.Services.Eco.ForgeActionKind.Finish, RuneAdvisor.StopAction(done).Kind);
        var blocked = RuneAdvisor.Advise(new[] { new ForgeLine("Force", 5, 1, 9, 9) }, Stock());
        var stop = RuneAdvisor.StopAction(blocked);
        Assert.Equal(GivreClone.App.Services.Eco.ForgeActionKind.Skip, stop.Kind);
        Assert.Contains("Force", stop.Reason);
        Assert.Equal("", stop.Rune);
    }
}
