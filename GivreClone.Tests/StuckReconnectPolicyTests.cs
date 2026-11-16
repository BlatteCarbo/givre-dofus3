using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class StuckReconnectPolicyTests
{
    private const long Hour = 3_600_000;
    private static StuckReconnectDecision D(bool enabled = true, bool fight = false, TimeSpan? mod = null, int pods = 10, long[]? prev = null, long now = 10 * Hour)
        => StuckReconnectPolicy.Decide(enabled, fight, mod, pods, prev ?? Array.Empty<long>(), now);

    [Fact] public void Allows_a_healthy_stuck_bot() => Assert.Equal(StuckReconnectDecision.Allow, D());
    [Fact] public void Respects_setting() => Assert.Equal(StuckReconnectDecision.Disabled, D(enabled: false));
    [Fact] public void Never_in_fight() => Assert.Equal(StuckReconnectDecision.InFight, D(fight: true));
    [Fact] public void Never_after_recent_moderator_contact() => Assert.Equal(StuckReconnectDecision.ModeratorContact, D(mod: TimeSpan.FromMinutes(90)));
    [Fact] public void Old_moderator_contact_is_fine() => Assert.Equal(StuckReconnectDecision.Allow, D(mod: TimeSpan.FromHours(3)));
    [Fact] public void Never_when_overloaded() => Assert.Equal(StuckReconnectDecision.Overloaded, D(pods: 97));

    [Fact]
    public void Three_per_hour_then_rate_limited_and_window_slides()
    {
        long now = 10 * Hour;
        Assert.Equal(StuckReconnectDecision.Allow, D(prev: new[] { now - 10, now - 20 }));
        Assert.Equal(StuckReconnectDecision.RateLimited, D(prev: new[] { now - 10, now - 20, now - 30 }));
        Assert.Equal(StuckReconnectDecision.Allow, D(prev: new[] { now - Hour - 1, now - 20, now - 30 }));   // la plus ancienne est sortie de l'heure
    }
}
