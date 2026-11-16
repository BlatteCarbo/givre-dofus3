extern alias F06App;
using System;
using System.Collections.Generic;
using Xunit;
using TurboPacing = F06App::GivreClone.App.Services.Combat.TurboPacing;

namespace GivreClone.Tests;

public class TurboPacingTests
{
    // Seuils EXPLICITES (indépendants des défauts AppConfig) : 25 tours / combat, 60 tours / 10 min, 20 tours / 60 s.
    private static bool Human(int turnInFight = 1, int inWindow = 1, int inMinute = 1, int perFight = 25, int per10Min = 60, int perMinute = 20)
        => TurboPacing.UseHumanPace(turnInFight, inWindow, inMinute, perFight, per10Min, perMinute);

    // Live 06-07/11 : 4/4 combats Turbo de 30+ tours bannis 5 min à la fin du combat, aucun combat non banni au-delà de 25 tours.
    // Boundaries explicites par combat : tour 25 -> Turbo, tour 26 -> human
    [Theory]
    [InlineData(1, false)]
    [InlineData(25, false)]   // le plafond lui-même reste Turbo
    [InlineData(26, true)]    // le tour suivant passe à l'allure humaine
    [InlineData(53, true)]
    public void Per_fight_cap_switches_to_human_pace_after_the_limit(int turnInFight, bool human)
        => Assert.Equal(human, Human(turnInFight: turnInFight));

    // Boundaries explicites 10 minutes : N vs N+1 (60 -> Turbo, 61 -> human)
    [Theory]
    [InlineData(1, false)]
    [InlineData(60, false)]
    [InlineData(61, true)]
    [InlineData(133, true)]
    public void Ten_minute_window_cap_switches_to_human_pace_after_the_limit(int turnsInWindow, bool human)
        => Assert.Equal(human, Human(inWindow: turnsInWindow));

    // Shadowbot 07/11 : coupé 50 ms après la fin de combat avec 34 (12:34:43) puis 31 (12:46:43) tours en 60 s ; p99 sans coupure = 25.
    // Boundaries explicites par minute : N vs N+1 (20 -> Turbo, 21 -> human)
    [Theory]
    [InlineData(1, false)]
    [InlineData(20, false)]   // le plafond lui-même reste Turbo
    [InlineData(21, true)]    // le tour suivant passe à l'allure humaine
    [InlineData(31, true)]
    [InlineData(34, true)]
    public void Per_minute_cap_switches_to_human_pace_after_the_limit(int turnsInLastMinute, bool human)
        => Assert.Equal(human, Human(inMinute: turnsInLastMinute));

    [Fact]
    public void Any_cap_alone_is_enough()
    {
        Assert.True(Human(turnInFight: 26));
        Assert.True(Human(inWindow: 61));
        Assert.True(Human(inMinute: 21));
        Assert.False(Human(turnInFight: 25, inWindow: 60, inMinute: 20));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Zero_or_negative_disables_the_per_fight_cap(int cap)
    {
        Assert.False(Human(turnInFight: 500, perFight: cap));
        Assert.True(Human(inWindow: 61, perFight: cap));   // les autres plafonds restent actifs
        Assert.True(Human(inMinute: 21, perFight: cap));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Zero_or_negative_disables_the_ten_minute_cap(int cap)
    {
        Assert.False(Human(inWindow: 5000, per10Min: cap));
        Assert.True(Human(turnInFight: 26, per10Min: cap));
        Assert.True(Human(inMinute: 21, per10Min: cap));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Zero_or_negative_disables_the_per_minute_cap(int cap)
    {
        Assert.False(Human(inMinute: 5000, perMinute: cap));
        Assert.True(Human(turnInFight: 26, perMinute: cap));
        Assert.True(Human(inWindow: 61, perMinute: cap));
    }

    [Fact]
    public void All_caps_disabled_never_slows_down()
        => Assert.False(TurboPacing.UseHumanPace(10_000, 10_000, 10_000, 0, 0, 0));

    [Fact]
    public void Boundaries_with_explicit_thresholds_exact_verification()
    {
        // per fight: turn 25 -> Turbo, turn 26 -> human
        Assert.False(TurboPacing.UseHumanPace(turnInFight: 25, turnsInWindow: 1, turnsInLastMinute: 1, maxTurnsPerFight: 25, maxTurnsPer10Min: 0, maxTurnsPerMinute: 0));
        Assert.True(TurboPacing.UseHumanPace(turnInFight: 26, turnsInWindow: 1, turnsInLastMinute: 1, maxTurnsPerFight: 25, maxTurnsPer10Min: 0, maxTurnsPerMinute: 0));

        // per 10 minutes: N vs N+1 (ex: N=60)
        Assert.False(TurboPacing.UseHumanPace(turnInFight: 1, turnsInWindow: 60, turnsInLastMinute: 1, maxTurnsPerFight: 0, maxTurnsPer10Min: 60, maxTurnsPerMinute: 0));
        Assert.True(TurboPacing.UseHumanPace(turnInFight: 1, turnsInWindow: 61, turnsInLastMinute: 1, maxTurnsPerFight: 0, maxTurnsPer10Min: 60, maxTurnsPerMinute: 0));

        // per minute: N vs N+1 (ex: N=20)
        Assert.False(TurboPacing.UseHumanPace(turnInFight: 1, turnsInWindow: 1, turnsInLastMinute: 20, maxTurnsPerFight: 0, maxTurnsPer10Min: 0, maxTurnsPerMinute: 20));
        Assert.True(TurboPacing.UseHumanPace(turnInFight: 1, turnsInWindow: 1, turnsInLastMinute: 21, maxTurnsPerFight: 0, maxTurnsPer10Min: 0, maxTurnsPerMinute: 20));

        // a threshold of 0 disables the check
        Assert.False(TurboPacing.UseHumanPace(turnInFight: 50, turnsInWindow: 200, turnsInLastMinute: 100, maxTurnsPerFight: 0, maxTurnsPer10Min: 0, maxTurnsPerMinute: 0));
    }

    [Fact]
    public void AppConfig_defaults_are_25_fight_150_ten_min_20_minute()
    {
        var cfg = new F06App::GivreClone.App.Services.AppConfigData();
        Assert.Equal(25, cfg.TurboMaxTurnsPerFight);
        Assert.Equal(150, cfg.TurboMaxTurnsPer10Min);
        Assert.Equal(20, cfg.TurboMaxTurnsPerMinute);
    }

    [Fact]
    public void Ten_minute_window_drops_turns_older_than_ten_minutes_and_keeps_the_boundary_exclusive()
    {
        const long t0 = 1_000_000;
        var q = new Queue<long>(new[] { t0, t0 + 1, t0 + 600_000 });
        // à t0 + 600 000 ms exactement, le tour de t0 a 10 min pile : sorti ; celui de t0 + 1 est encore dedans.
        Assert.Equal(2, TurboPacing.PruneAndCount(q, t0 + 600_000));
        Assert.Equal(1, TurboPacing.PruneAndCount(q, t0 + 600_001));
        Assert.Equal(0, TurboPacing.PruneAndCount(q, t0 + 1_200_001));
    }

    [Fact]
    public void Minute_window_counts_only_the_last_60_seconds_and_does_not_consume_the_queue()
    {
        const long t0 = 1_000_000;
        var q = new Queue<long>(new[] { t0, t0 + 1, t0 + 30_000, t0 + 60_000 });
        // à t0 + 60 000 exactement : le tour de t0 a 60 s pile (hors fenêtre), celui de t0 + 1 est encore dedans.
        Assert.Equal(3, TurboPacing.CountLastMinute(q, t0 + 60_000));
        Assert.Equal(2, TurboPacing.CountLastMinute(q, t0 + 60_001 + 1));
        Assert.Equal(0, TurboPacing.CountLastMinute(q, t0 + 120_001));
        Assert.Equal(4, q.Count);
    }

    [Fact]
    public void Per_minute_cap_relaxes_by_itself_when_the_window_drops_back_under()
    {
        // 21 tours en 20 s : le suivant est humain ; 60 s après le premier, les plus anciens sortent et le Turbo revient.
        const long t0 = 5_000_000;
        var q = new Queue<long>();
        for (int i = 0; i < 21; i++) q.Enqueue(t0 + i * 1_000);   // t0 .. t0 + 20 000
        Assert.True(Human(inMinute: TurboPacing.CountLastMinute(q, t0 + 21_000) + 1));
        // t0 + 70 000 : seuls les tours >= t0 + 10 001 restent (10 tours) -> 11 avec le tour courant, sous le plafond.
        Assert.False(Human(inMinute: TurboPacing.CountLastMinute(q, t0 + 70_000) + 1));
    }

    [Fact]
    public void Human_delays_use_the_Fast_cast_range_and_the_1_5_to_3_s_end_of_turn_range()
    {
        var rnd = new Random(42);
        for (int i = 0; i < 500; i++)
        {
            Assert.InRange(TurboPacing.CastDelayMs(rnd), 450, 899);
            Assert.InRange(TurboPacing.EndTurnDelayMs(rnd), 1500, 3000);
        }
    }
}
