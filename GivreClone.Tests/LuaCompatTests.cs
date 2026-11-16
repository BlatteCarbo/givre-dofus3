using System;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Helpers purs de compatibilité Lua (Lot A) : lots HDV, index 1-based, ordre des args MP, jetons du DSL de trajet.</summary>
public class LuaCompatTests
{
    [Theory]
    [InlineData(-5, 1)] [InlineData(0, 1)] [InlineData(1, 1)]
    [InlineData(2, 10)] [InlineData(3, 100)] [InlineData(4, 1000)]
    [InlineData(10, 10)] [InlineData(100, 100)] [InlineData(1000, 1000)]
    [InlineData(5, 5)] [InlineData(42, 42)]
    public void LotSizeFromArg_maps_index_or_size(int x, int expected) => Assert.Equal(expected, LuaCompat.LotSizeFromArg(x));

    [Theory]
    [InlineData(1, 1)] [InlineData(10, 2)] [InlineData(100, 3)] [InlineData(1000, 4)]
    [InlineData(0, 0)] [InlineData(2, 0)] [InlineData(50, 0)] [InlineData(-1, 0)]
    public void LotIndexOfQuantity_returns_reference_index(int qty, int expected) => Assert.Equal(expected, LuaCompat.LotIndexOfQuantity(qty));

    [Theory]
    [InlineData(-3, -1)] [InlineData(-1, -1)] [InlineData(0, 0)] [InlineData(1, 0)] [InlineData(2, 1)] [InlineData(10, 9)]
    public void ZeroBasedFromLuaIndex_is_one_based_with_zero_alias(int i, int expected) => Assert.Equal(expected, LuaCompat.ZeroBasedFromLuaIndex(i));

    [Fact]
    public void ShouldSwapPmArgs_detects_reference_order()
    {
        Assert.True(LuaCompat.ShouldSwapPmArgs("salut toi", "Bob"));
        Assert.False(LuaCompat.ShouldSwapPmArgs("Bob", "salut toi"));
        Assert.False(LuaCompat.ShouldSwapPmArgs("ok", "Bob"));      // limite connue : message sans espace non détectable
        Assert.False(LuaCompat.ShouldSwapPmArgs("a b", ""));
        Assert.False(LuaCompat.ShouldSwapPmArgs(null!, null!));
    }

    [Fact]
    public void TryParseDirToken_plain_and_forced_cell()
    {
        Assert.True(LuaCompat.TryParseDirToken("top", out var d, out int c));
        Assert.Equal(MapDir.Top, d); Assert.Equal(-1, c);

        Assert.True(LuaCompat.TryParseDirToken(" Droite ( 42 ) ", out d, out c));
        Assert.Equal(MapDir.Right, d); Assert.Equal(42, c);

        Assert.True(LuaCompat.TryParseDirToken("SUD", out d, out c));
        Assert.Equal(MapDir.Bottom, d); Assert.Equal(-1, c);

        Assert.True(LuaCompat.TryParseDirToken("left(0)", out d, out c));
        Assert.Equal(MapDir.Left, d); Assert.Equal(0, c);

        Assert.False(LuaCompat.TryParseDirToken("top(600)", out _, out _));
        Assert.False(LuaCompat.TryParseDirToken("top(560)", out _, out _));
        Assert.False(LuaCompat.TryParseDirToken("top(x)", out _, out _));
        Assert.False(LuaCompat.TryParseDirToken("top(-1)", out _, out _));
        Assert.False(LuaCompat.TryParseDirToken("top()", out _, out _));
        Assert.False(LuaCompat.TryParseDirToken("12,5", out _, out _));
        Assert.False(LuaCompat.TryParseDirToken("", out _, out _));
        Assert.False(LuaCompat.TryParseDirToken("zaap(12)", out _, out _));
    }

    [Fact]
    public void TryParseTeleportKeyword_forms()
    {
        Assert.True(LuaCompat.TryParseTeleportKeyword("zaap(191105026)", out var k, out long m));
        Assert.Equal(TeleportKeyword.Zaap, k); Assert.Equal(191105026L, m);

        Assert.True(LuaCompat.TryParseTeleportKeyword("ZAAPI( 12 )", out k, out m));
        Assert.Equal(TeleportKeyword.Zaapi, k); Assert.Equal(12L, m);

        Assert.True(LuaCompat.TryParseTeleportKeyword("havenbag", out k, out _));
        Assert.Equal(TeleportKeyword.HavenBag, k);

        Assert.True(LuaCompat.TryParseTeleportKeyword("havenbag()", out k, out _));
        Assert.Equal(TeleportKeyword.HavenBag, k);

        Assert.False(LuaCompat.TryParseTeleportKeyword("zaap()", out _, out _));
        Assert.False(LuaCompat.TryParseTeleportKeyword("zaap:123", out _, out _));
        Assert.False(LuaCompat.TryParseTeleportKeyword("zaap(0)", out _, out _));
        Assert.False(LuaCompat.TryParseTeleportKeyword("top", out _, out _));
        Assert.False(LuaCompat.TryParseTeleportKeyword("191105026", out _, out _));

        Assert.True(LuaCompat.TryParseTeleportKeyword("zaapcarte(164364304)", out k, out m));
        Assert.Equal(TeleportKeyword.WorldMap, k);
        Assert.Equal(164364304L, m);
        Assert.False(LuaCompat.TryParseTeleportKeyword("zaapcarte()", out _, out _));
    }

    [Fact]
    public void PickAlternative_splits_on_pipe()
    {
        var rng = new Random(1234);
        Assert.Equal("left", LuaCompat.PickAlternative("left", rng));
        for (int i = 0; i < 20; i++)
            Assert.Contains(LuaCompat.PickAlternative("a| |b", rng), new[] { "a", "b" });
        Assert.Equal("", LuaCompat.PickAlternative("|", rng));
        Assert.Equal("top(12)", LuaCompat.PickAlternative(" top(12) |", rng));
    }
}
