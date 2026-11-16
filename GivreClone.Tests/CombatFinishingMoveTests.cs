using GivreClone.App.Services.CombatParity;
using Xunit;

namespace GivreClone.Tests;

public class CombatFinishingMoveTests
{
    [Theory]
    [InlineData(0, 8, 0, 2, true, true)]
    [InlineData(8, 8, 0, 2, true, false)]
    [InlineData(1, 8, 2, 2, true, false)]
    [InlineData(1, 8, 0, 0, true, false)]
    [InlineData(1, 8, 1, 2, false, false)]
    public void Exhausted_cast_limits_cannot_trigger_a_lethal_area_move(int total, int limit,
        int spell, int configured, bool serverOk, bool expected)
    {
        Assert.Equal(expected, FinishingMovePolicy.ShouldPrepare(false, 3, false,
            () => FinishingMovePolicy.HasCastBudget(total, limit, spell, configured, serverOk)));
    }

    [Fact]
    public void Low_hp_only_prepares_initial_move_but_new_lethal_area_still_prepares()
    {
        Assert.True(FinishingMovePolicy.ShouldPrepare(true, 3, true, () => false));
        Assert.False(FinishingMovePolicy.ShouldPrepare(false, 3, true, () => false));
        Assert.True(FinishingMovePolicy.ShouldPrepare(false, 3, true, () => true));
        Assert.True(FinishingMovePolicy.ShouldPrepare(false, 1, false, () => false));
        Assert.False(FinishingMovePolicy.ShouldPrepare(true, 0, true, () => true));
    }

    [Fact]
    public void Refusal_does_not_retry_on_each_attack_but_changed_geometry_allows_retry()
    {
        var attempts = new ChallengeMoveAttempts();
        var foes = new[] { (1L, 200), (2L, 201) };
        Assert.True(attempts.TryBegin(0, 300, 6, foes));
        for (int attack = 0; attack < 10; attack++)
            Assert.False(attempts.TryBegin(0, 300, 6, foes.Reverse()));
        Assert.True(attempts.TryBegin(0, 300, 6, new[] { (2L, 201) })); // death
        Assert.True(attempts.TryBegin(0, 300, 6, new[] { (1L, 202), (2L, 201) }));
        Assert.True(attempts.TryBegin(1, 301, 5, foes)); // truncated move
        Assert.False(attempts.TryBegin(1, 301, 5, foes));
        Assert.True(attempts.TryBegin(1, 301, 0, foes)); // tackle/PM loss
    }

    [Theory]
    [InlineData(6, 6, 2, 6, 4)]
    [InlineData(6, 6, 2, 1, 1)]
    [InlineData(6, 6, 2, 0, 0)]
    [InlineData(6, 6, 8, 3, 0)]
    [InlineData(2, 6, 1, 5, 2)]
    public void Style_move_uses_confirmed_steps_and_server_pm(int local, int start, int walked, int server, int expected)
        => Assert.Equal(expected, FinishingMovePolicy.RemainingMp(local, start, walked, server));

    [Theory]
    [InlineData(3, 3, 2, 3, 2, 3)]
    [InlineData(2, 0, 0, 2, 2, 2)]
    [InlineData(3, 3, 2, 1, 2, 1)]
    [InlineData(1, 3, 2, 3, 2, 1)]
    [InlineData(4, 3, 4, 4, 3, 2)]
    public void Gained_mp_extends_the_turn_ceiling_without_restoring_spent_steps(
        int local, int start, int walked, int server, int gained, int expected)
        => Assert.Equal(expected, FinishingMovePolicy.RemainingMp(local, start, walked, server, gained));
}
