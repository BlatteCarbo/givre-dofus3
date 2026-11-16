extern alias F06App;

using F06App::GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public sealed class AntiAfkTests
{
    private static AntiAfkGates Idle(bool enabled = true, int minutes = 10, bool inGame = true, bool inFight = false,
        bool moving = false, bool dialog = false, bool exchange = false)
        => new(enabled, minutes, inGame, inFight, moving, dialog, exchange);

    private static AntiAfkSession Session(params int[] jitters)
    {
        int i = 0;
        return new AntiAfkSession(() => jitters[Math.Min(i++, jitters.Length - 1)]);
    }

    [Fact]
    public void Defaults_are_on_and_ten_minutes()
    {
        var settings = new BotSettings();
        Assert.True(settings.AntiAfkEnabled);
        Assert.Equal(AntiAfkPolicy.DefaultIdleMinutes, settings.AntiAfkIdleMinutes);
        Assert.Equal(10, AntiAfkPolicy.DefaultIdleMinutes);
    }

    [Fact]
    public void Copy_keeps_the_toggle_and_the_delay()
    {
        var src = new BotSettings { AntiAfkEnabled = false, AntiAfkIdleMinutes = 12 };
        var dst = new BotSettings();
        BotSettingsCopy.ApplyAll(src, dst);
        Assert.False(dst.AntiAfkEnabled);
        Assert.Equal(12, dst.AntiAfkIdleMinutes);
    }

    [Fact]
    public void Fake_clock_sends_one_orientation_at_the_idle_boundary()
    {
        var clock = new AntiAfkClock();
        var session = Session(0);
        session.NoteAction(clock.NowMs);
        var gates = Idle();

        clock.AdvanceMs(AntiAfkPolicy.IdleBudgetMs(10, 0) - 1);
        var early = session.Evaluate(clock.NowMs, gates);
        Assert.False(early.Send);
        Assert.Equal(AntiAfkBlock.NotDue, early.Block);

        clock.AdvanceMs(1);
        var due = session.Evaluate(clock.NowMs, gates);
        Assert.True(due.Send);
        Assert.Equal(1, due.Direction);
        Assert.Equal(AntiAfkBlock.None, due.Block);

        session.CommitKeepalive(clock.NowMs, due.Direction);
        Assert.False(session.Evaluate(clock.NowMs, gates).Send);
    }

    [Fact]
    public void Jitter_is_frozen_until_the_keepalive()
    {
        var clock = new AntiAfkClock();
        var session = Session(45_000, 0);
        session.NoteAction(clock.NowMs);
        var gates = Idle();
        long baseMs = AntiAfkPolicy.IdleBudgetMs(10, 0);

        clock.NowMs = baseMs;
        Assert.False(session.Evaluate(clock.NowMs, gates).Send);

        clock.NowMs = baseMs + 45_000;
        var due = session.Evaluate(clock.NowMs, gates);
        Assert.True(due.Send);
        Assert.Equal(1, due.Direction);

        session.CommitKeepalive(clock.NowMs, due.Direction);
        clock.AdvanceMs(baseMs);
        var next = session.Evaluate(clock.NowMs, gates);
        Assert.True(next.Send);
        Assert.Equal(3, next.Direction);
    }

    [Fact]
    public void Direction_turns_a_quarter_and_wraps()
    {
        Assert.Equal(1, AntiAfkPolicy.NextDirection(null));
        Assert.Equal(1, AntiAfkPolicy.NextDirection(-1));
        Assert.Equal(1, AntiAfkPolicy.NextDirection(8));
        Assert.Equal(2, AntiAfkPolicy.NextDirection(0));
        Assert.Equal(0, AntiAfkPolicy.NextDirection(6));
        Assert.Equal(1, AntiAfkPolicy.NextDirection(7));

        var session = Session(0);
        session.NoteAction(0);
        session.NoteDirection(4);
        var plan = session.Evaluate(AntiAfkPolicy.IdleBudgetMs(10, 0), Idle());
        Assert.True(plan.Send);
        Assert.Equal(6, plan.Direction);
    }

    [Theory]
    [InlineData(true, false, false, false, false, false, AntiAfkBlock.Disabled)]
    [InlineData(false, true, false, false, false, false, AntiAfkBlock.NotInGame)]
    [InlineData(false, false, true, false, false, false, AntiAfkBlock.InFight)]
    [InlineData(false, false, false, false, false, true, AntiAfkBlock.Moving)]
    [InlineData(false, false, false, false, true, false, AntiAfkBlock.Exchange)]
    [InlineData(false, false, false, true, false, false, AntiAfkBlock.Dialog)]
    public void Guards_block_a_due_keepalive(bool disabled, bool outOfGame, bool fight, bool dialog, bool exchange, bool moving, AntiAfkBlock block)
    {
        var session = Session(0);
        session.NoteAction(0);
        var gates = Idle(
            enabled: !disabled,
            inGame: !outOfGame,
            inFight: fight,
            moving: moving,
            dialog: dialog,
            exchange: exchange);
        var plan = session.Evaluate(AntiAfkPolicy.IdleBudgetMs(10, 0), gates);
        Assert.False(plan.Send);
        Assert.Equal(block, plan.Block);
        Assert.True(session.Evaluate(AntiAfkPolicy.IdleBudgetMs(10, 0), Idle()).Send);
    }

    [Fact]
    public void Arrival_starts_the_clock_once()
    {
        var session = Session(0);
        session.NoteArrival(0);
        Assert.False(session.Evaluate(AntiAfkPolicy.IdleBudgetMs(10, 0) - 1, Idle()).Send);
        session.NoteArrival(50_000);
        Assert.True(session.Evaluate(AntiAfkPolicy.IdleBudgetMs(10, 0), Idle()).Send);
    }

    [Fact]
    public void Housekeeping_is_not_an_action()
    {
        Assert.False(AntiAfkPolicy.CountsAsAction(null));
        Assert.False(AntiAfkPolicy.CountsAsAction(""));
        Assert.False(AntiAfkPolicy.CountsAsAction("PingRequest"));
        Assert.False(AntiAfkPolicy.CountsAsAction("SequenceNumberRequest"));
        Assert.False(AntiAfkPolicy.CountsAsAction("BasicLatencyStatsRequest"));
        Assert.False(AntiAfkPolicy.CountsAsAction("ClientChallengeInitRequest"));
        Assert.False(AntiAfkPolicy.CountsAsAction("ClientChallengeProofRequest"));
        Assert.False(AntiAfkPolicy.CountsAsAction("ClientIdRequest"));
        Assert.False(AntiAfkPolicy.CountsAsAction("FightPingsRequest"));
        Assert.False(AntiAfkPolicy.CountsAsAction("GameActionAcknowledgementRequest"));
        Assert.True(AntiAfkPolicy.CountsAsAction("MapMovementRequest"));
        Assert.True(AntiAfkPolicy.CountsAsAction("MapChangeOrientationRequest"));
    }

    [Fact]
    public void Delay_is_clamped()
    {
        Assert.Equal(1, AntiAfkPolicy.ClampMinutes(0));
        Assert.Equal(30, AntiAfkPolicy.ClampMinutes(99));
        Assert.Equal(10 * 60_000L, AntiAfkPolicy.IdleBudgetMs(10, -5));
        Assert.Equal(10 * 60_000L + AntiAfkPolicy.JitterMaxMs, AntiAfkPolicy.IdleBudgetMs(10, 999_999));
        Assert.Equal(60_000L, AntiAfkPolicy.IdleBudgetMs(0, 0));
    }
}
