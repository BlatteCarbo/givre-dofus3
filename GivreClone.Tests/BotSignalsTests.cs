using System.Linq;
using GivreClone.App.Services.Signals;
using Xunit;

namespace GivreClone.Tests;

public class BotSignalsTests
{
    [Fact]
    public void Full_pods_and_low_life_out_of_fight_are_reported()
    {
        var s = BotSignals.For(new BotVitals(Life: 20, MaxLife: 100, PodsCur: 960, PodsMax: 1000, InFight: false)).Select(x => x.Category).ToArray();
        Assert.Equal(new[] { "Inventaire", "Santé" }, s);
    }

    [Fact]
    public void Edges_fire_once_and_rearm_after_the_signal_clears()
    {
        var e = new SignalEdges();
        Assert.Equal(new[] { "a" }, e.Rising(new[] { "a" }));
        Assert.Empty(e.Rising(new[] { "a" }));
        Assert.Equal(new[] { "b" }, e.Rising(new[] { "a", "b" }));
        Assert.Empty(e.Rising(new string[0]));
        Assert.Equal(new[] { "a" }, e.Rising(new[] { "a" }));
    }

    [Fact]
    public void Low_life_in_fight_and_unknown_values_stay_silent()
    {
        Assert.Empty(BotSignals.For(new BotVitals(5, 100, 100, 1000, InFight: true)));
        Assert.Empty(BotSignals.For(new BotVitals(0, 0, 0, 0, InFight: false)));
        Assert.Empty(BotSignals.For(new BotVitals(25, 100, 949, 1000, InFight: false)));
    }
}
