using GivreClone.App.Services.Treasure;
using Xunit;

namespace GivreClone.Tests;

public class TreasureTargetTests
{
    [Theory]
    [InlineData(0, 3, 5, -2)]   // droite : x + 3
    [InlineData(2, 2, 2, 0)]    // bas : y + 2
    [InlineData(4, 1, 1, -2)]   // gauche : x - 1
    [InlineData(6, 3, 2, -5)]   // haut : y - 3
    public void Direction_steps_land_count_maps_away_on_the_right_axis(int direction, int count, int ex, int ey)
        => Assert.Equal((ex, ey), TreasureTarget.Offset(2, -2, direction, count));

    [Fact]
    public void Unknown_direction_has_no_landing() => Assert.Null(TreasureTarget.Offset(0, 0, 1, 3));

    [Fact]
    public void Describe_shows_progress_and_step()
    {
        Assert.Equal("Étape 2/5 · ↑ Indice : Rocher", TreasureTarget.Describe("poi", 6, 0, "Rocher", 1, 5));
        Assert.Equal("→ 4 carte(s)", TreasureTarget.Describe("direction", 0, 4, null, 0, 0));
        Assert.True(TreasureTarget.CanGo("poi"));
        Assert.False(TreasureTarget.CanGo("hint"));
    }
}
