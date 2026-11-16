using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class HdvPacingTests
{
    [Fact]
    public void Zero_Stays_Zero_Instead_Of_Being_Clamped_To_A_Hidden_Floor()
    {
        var (min, max) = HdvPacing.Resolve(0, 0);
        Assert.Equal(0, min);
        Assert.Equal(0, max);
    }

    [Fact]
    public void User_Interval_Is_Preserved()
    {
        var (min, max) = HdvPacing.Resolve(60, 180);
        Assert.Equal(60, min);
        Assert.Equal(180, max);
    }

    [Fact]
    public void Negative_Minimum_Becomes_Zero()
    {
        var (min, _) = HdvPacing.Resolve(-500, 100);
        Assert.Equal(0, min);
    }

    [Fact]
    public void Maximum_Below_Minimum_Is_Raised_Never_Inverted()
    {
        var (min, max) = HdvPacing.Resolve(400, 50);
        Assert.Equal(400, min);
        Assert.Equal(400, max);
        Assert.True(max >= min);
    }

    [Fact]
    public void Interval_Is_Capped_At_One_Hour()
    {
        var (min, max) = HdvPacing.Resolve(int.MaxValue, int.MaxValue);
        Assert.Equal(HdvPacing.MaxDelayMs, min);
        Assert.Equal(HdvPacing.MaxDelayMs, max);
    }
}
