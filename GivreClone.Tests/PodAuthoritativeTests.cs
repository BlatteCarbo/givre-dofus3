using Xunit;

namespace GivreClone.Tests;

public class PodAuthoritativeTests
{
    private sealed class TestBotWorldState
    {
        public (int Cur, int Max) Pods { get; set; }
        public bool PodsAuthoritative { get; set; }
        public int PodsPercent => PodsAuthoritative && Pods.Max > 0 ? (int)(100L * Pods.Cur / Pods.Max) : 100;
    }

    [Fact]
    public void PodPercent_returns_100_when_not_authoritative()
    {
        var world = new TestBotWorldState();
        Assert.False(world.PodsAuthoritative);
        Assert.Equal(100, world.PodsPercent);
    }

    [Fact]
    public void PodPercent_calculates_correctly_when_authoritative()
    {
        var world = new TestBotWorldState
        {
            Pods = (500, 1000),
            PodsAuthoritative = true
        };
        Assert.True(world.PodsAuthoritative);
        Assert.Equal(50, world.PodsPercent);
    }

    [Fact]
    public void PodPercent_returns_100_when_authoritative_but_max_zero()
    {
        var world = new TestBotWorldState
        {
            Pods = (0, 0),
            PodsAuthoritative = true
        };
        Assert.Equal(100, world.PodsPercent);
    }
}
