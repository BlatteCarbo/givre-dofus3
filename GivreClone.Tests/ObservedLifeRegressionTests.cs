extern alias F06App;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using GivreClone.Protocol;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Xunit;

namespace GivreClone.Tests;

public sealed class ObservedLifeRegressionTests
{
    [Theory]
    [InlineData(40, 100, 100, 40)]
    [InlineData(80, 100, 100, 80)]
    [InlineData(2028, 2117, 2126, 2037)]
    [InlineData(1814, 2105, 2126, 1833)]
    [InlineData(1556, 2075, 2126, 1595)]
    [InlineData(1950, 2127, 2131, 1954)]
    [InlineData(1855, 2125, 2131, 1861)]
    [InlineData(1707, 2092, 2131, 1739)]
    public void Postfight_seed_preserves_observed_life_ratio_when_maximum_is_restored(
        int remaining, int combatMax, int restoredMax, int expected)
    {
        var launch = new Launch();
        const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        IDictionary Map(string name) => (IDictionary)typeof(Launch).GetField(name, hidden)!.GetValue(launch)!;
        typeof(Launch).GetMethod("SetSelfLife", hidden)!
            .Invoke(launch, [717u, restoredMax / 2, restoredMax, "test", true]);
        typeof(Launch).GetMethod("MarkLifeStaleForFight", hidden)!.Invoke(launch, [717u]);
        Map("_charId")[717u] = 123L;
        Map("_fighters")[717u] = new List<(int Cell, long ActorId)> { (200, 123L) };
        Map("_fighterStats")[717u] = new ConcurrentDictionary<long, FighterStats>(
            new[] { new KeyValuePair<long, FighterStats>(123L, new(123, 6, 3, remaining, combatMax, 6, 3)) });

        typeof(Launch).GetMethod("SeedLifeAfterFight", hidden)!.Invoke(launch, [717u]);

        Assert.Equal(expected, launch.SelfLife(717)!.Value.Cur);
        Assert.Null(launch.SelfLifeAuthoritative(717));
    }

    private static void SetLife(Launch launch, int life, bool observed) =>
        typeof(Launch).GetMethod("SetSelfLife", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(launch, [717u, life, 100, "test", observed]);

    [Fact]
    public void Team_wait_identifies_the_blocking_member_and_clears_after_observed_recovery()
    {
        var launch = new Launch();
        launch.SetRegenerationOptions(716, new F06App::GivreClone.App.Services.BotSettings
        {
            RegenerationEnabled = true, RegenerationWaitForTeam = true,
            RegenerationStartPercent = 60, RegenerationStopPercent = 100
        });
        launch.SetTeamSnapshot(new Dictionary<uint, Launch.TeamView>
        {
            [716] = new(716, "Chef", [new(716, "Chef", true), new(717, "Sylvanbot", false)])
        });
        var maps = (System.Collections.Concurrent.ConcurrentDictionary<uint, long>)typeof(Launch)
            .GetField("_lastMap", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(launch)!;
        maps[716] = maps[717] = 100;
        SetLife(launch, 50, observed: true);
        Assert.False(launch.RegenerationTeamReady(716));
        Assert.Contains("Sylvanbot", launch.RegenerationTeamWaitReason(716));

        SetLife(launch, 100, observed: true);

        Assert.True(launch.RegenerationTeamReady(716));
        Assert.Null(launch.RegenerationTeamWaitReason(716));
    }

    [Fact]
    public void Estimated_seed_does_not_replace_observed_life_or_confirm_a_consumable()
    {
        var launch = new Launch();
        SetLife(launch, 50, observed: true);
        var sequence = launch.LifeSeq(717);

        SetLife(launch, 90, observed: false);

        Assert.Equal(50, launch.SelfLifeAuthoritative(717));
        Assert.Equal(50, launch.AuthoritativeLifePercent(717));
        Assert.Equal(sequence, launch.LifeSeq(717));
    }

    [Fact]
    public void Fight_invalidates_observation_and_estimates_cannot_restore_it()
    {
        var launch = new Launch();
        SetLife(launch, 50, observed: true);
        typeof(Launch).GetMethod("MarkLifeStaleForFight", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(launch, [717u]);

        SetLife(launch, 90, observed: false);

        Assert.Null(launch.AuthoritativeLifePercent(717));
        SetLife(launch, 80, observed: true);
        Assert.Equal(80, launch.AuthoritativeLifePercent(717));
    }
}
