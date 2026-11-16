using GivreClone.Engine;
using Xunit;

namespace GivreClone.Tests;

public class TreasureFightPolicyTests
{
    private static TreasureFightPolicy.Guard[] Guards(params string?[] looks) =>
        looks.Select((look, i) => new TreasureFightPolicy.Guard(-i - 1, 100 + i, look)).ToArray();

    [Fact]
    public void Exactly_one_structurally_different_guard_is_selected()
        => Assert.Equal(-3, TreasureFightPolicy.Select(Guards("same", "same", "four-legs", "same"))!.Value.Actor);

    [Fact]
    public void Incomplete_and_non_unique_looks_never_guess()
    {
        Assert.Null(TreasureFightPolicy.Select(Guards("a", "a", "b")));
        Assert.Null(TreasureFightPolicy.Select(Guards("a", "a", "a", null)));
        Assert.Null(TreasureFightPolicy.Select(Guards("a", "a", "a", "a")));
        Assert.Null(TreasureFightPolicy.Select(Guards("a", "a", "b", "b")));
        Assert.Null(TreasureFightPolicy.Select(Guards("a", "b", "c", "d")));
    }

    [Fact]
    public void Only_explicit_point_effects_including_criticals_are_safe()
    {
        Assert.True(TreasureFightPolicy.SingleTargetEffects(new[] { (int)'P', 'P' }, new[] { (int)'P' }));
        Assert.True(TreasureFightPolicy.SingleTargetEffects(new[] { (int)'P' }, Array.Empty<int>()));
        Assert.False(TreasureFightPolicy.SingleTargetEffects(new[] { (int)'P' }, new[] { (int)'C' }));
        Assert.False(TreasureFightPolicy.SingleTargetEffects(new[] { (int)'P', 'X' }, Array.Empty<int>()));
        Assert.False(TreasureFightPolicy.SingleTargetEffects(new[] { 0 }, Array.Empty<int>()));
        Assert.False(TreasureFightPolicy.SingleTargetEffects(Array.Empty<int>(), Array.Empty<int>()));
    }
}
