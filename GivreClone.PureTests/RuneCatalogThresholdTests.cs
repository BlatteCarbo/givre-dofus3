using GivreClone.App.Services.Eco;
using Xunit;

namespace GivreClone.Tests;

public sealed class RuneCatalogThresholdTests
{
    [Theory]
    [InlineData(0, 30, 0, 0, "Ra")]
    [InlineData(0, 5, 0, 0, "Pa")]
    [InlineData(0, 2, 0, 0, "Ba")]
    [InlineData(3, 14, 5, 7, "Ba")]
    [InlineData(6, 17, 5, 7, "Pa")]
    [InlineData(8, 19, 5, 7, "Ra")]
    public void Tier_selection_respects_zero_as_an_immediate_threshold(
        int current, int target, int paFrom, int raFrom, string expected)
    {
        var force = RuneCatalog.Of(118)!;
        Assert.Equal(expected, RuneCatalog.TierFor(force, current, target, paFrom, raFrom));
    }
}
