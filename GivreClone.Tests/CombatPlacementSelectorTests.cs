extern alias F06App;
using System.Linq;
using Xunit;
using Selector = F06App::GivreClone.App.Services.CombatPlacementSelector;
using Mode = F06App::GivreClone.App.Services.PlacementMode;
using Style = F06App::GivreClone.App.Services.CombatStyle;

namespace GivreClone.Tests;

public sealed class CombatPlacementSelectorTests
{
    // Cells 0..4 lie on one diagonal: distance between consecutive cells is two.
    [Theory]
    [InlineData(Style.Aggressive)]
    [InlineData(Style.Fleeing)]
    [InlineData(Style.Passive)]
    public void Explicit_modes_score_the_nearest_enemy_independently_of_style(Style style)
    {
        int[] offered = [3, 2, 1];
        int[] enemies = [0, 4];
        Assert.Equal(1, Selector.Select(Mode.Near, style, offered, enemies, [], 3));
        Assert.Equal(2, Selector.Select(Mode.Far, style, offered, enemies, [], 3));
        Assert.Equal(3, Selector.Select(Mode.Keep, style, offered, enemies, [], 3));
    }

    [Theory]
    [InlineData(Mode.Near)]
    [InlineData(Mode.Far)]
    public void Equal_distances_use_the_lowest_cell_id_regardless_of_offer_order(Mode mode)
    {
        Assert.Equal(1, Selector.Select(mode, Style.Aggressive, [3, 1], [0, 4], [], 3));
        Assert.Equal(1, Selector.Select(mode, Style.Aggressive, [1, 3], [4, 0], [], 3));
    }

    [Fact]
    public void Occupied_and_invalid_cells_are_excluded_and_empty_choices_send_nothing()
    {
        Assert.Equal(3, Selector.Select(Mode.Far, Style.Aggressive, [-1, 560, 2, 1, 3], [0, 4], [1, 2], 3));
        Assert.Equal(-1, Selector.Select(Mode.Near, Style.Aggressive, [1, 2], [0], [1, 2], 3));
        Assert.Equal(-1, Selector.Select(Mode.Far, Style.Passive, [], [0], [], 3));
        Assert.Equal(-1, Selector.Select(Mode.Near, Style.Passive, [-1, 560], [0], [], 3));
    }

    [Theory]
    [InlineData(Mode.Near)]
    [InlineData(Mode.Far)]
    public void Explicit_distance_modes_do_not_move_without_a_valid_enemy(Mode mode)
    {
        Assert.Equal(-1, Selector.Select(mode, Style.Aggressive, [3, 1], [], [], 3));
        Assert.Equal(-1, Selector.Select(mode, Style.Aggressive, [3, 1], [-1, 560], [], 3));
    }

    [Fact]
    public void None_never_selects_another_cell()
    {
        Assert.Equal(3, Selector.Select(Mode.Keep, Style.Aggressive, [1, 3], [], [], 3));
        Assert.Equal(-1, Selector.Select(Mode.Keep, Style.Aggressive, [1], [0], [], 3));
        Assert.Equal(-1, Selector.Select(Mode.Keep, Style.Aggressive, [1, 3], [0], [3], 3));
    }

    [Fact]
    public void Existing_profiles_keep_style_scoring_offer_order_and_no_enemy_fallback()
    {
        Assert.Equal(1, Selector.Select(Mode.LegacyByStyle, Style.Aggressive, [3, 2, 1], [0], [], 3));
        Assert.Equal(3, Selector.Select(Mode.LegacyByStyle, Style.Fleeing, [3, 2, 1], [0], [], 3));
        Assert.Equal(3, Selector.Select(Mode.LegacyByStyle, Style.Passive, [3, 1], [0, 4], [], 3));
        Assert.Equal(3, Selector.Select(Mode.LegacyByStyle, Style.Aggressive, [3, 1], [], [], 3));
    }

    private static int At(int x, int y) => GivreClone.Protocol.MapPathfinder.CellFromMapPoint(x, y);

    private static Selector.PlacementThreat[] Group(int damage) =>
        [new(At(16, 0), 2, 1, damage), new(At(17, 0), 2, 1, damage), new(At(18, 0), 2, 1, damage)];

    [Fact]
    public void Dangerous_group_moves_near_placement_out_of_reach_but_keeps_an_attack_next_turn()
    {
        var threats = Group(1000);
        int[] offered = [At(15, 0), At(11, 0), At(8, 0)];

        int chosen = Selector.SelectSafe(Mode.Near, Style.Aggressive, offered, threats.Select(t => t.Cell).ToArray(), [], -1,
            threats, ownLife: 2763, ownReach: 5);

        Assert.Equal(At(11, 0), chosen);
    }

    [Fact]
    public void Weak_group_keeps_the_usual_nearest_cell()
    {
        var threats = Group(200);
        int[] offered = [At(15, 0), At(11, 0), At(8, 0)];

        int chosen = Selector.SelectSafe(Mode.Near, Style.Aggressive, offered, threats.Select(t => t.Cell).ToArray(), [], -1,
            threats, ownLife: 2763, ownReach: 5);

        Assert.Equal(At(15, 0), chosen);
    }

    [Fact]
    public void When_every_free_cell_is_dangerous_the_least_exposed_free_cell_wins()
    {
        var threats = Group(1000);
        int[] offered = [At(15, 0), At(14, 0), At(11, 0)];

        int chosen = Selector.SelectSafe(Mode.Near, Style.Aggressive, offered, threats.Select(t => t.Cell).ToArray(), [At(11, 0)], -1,
            threats, ownLife: 2763, ownReach: 5);

        Assert.Equal(At(14, 0), chosen);
    }

    [Theory]
    [InlineData(Mode.Far)]
    [InlineData(Mode.Keep)]
    public void Far_and_keep_modes_are_not_overridden(Mode mode)
    {
        var threats = Group(1000);
        int[] offered = [At(15, 0), At(11, 0), At(8, 0)];
        int expected = Selector.Select(mode, Style.Aggressive, offered, threats.Select(t => t.Cell).ToArray(), [], At(15, 0));

        Assert.Equal(expected, Selector.SelectSafe(mode, Style.Aggressive, offered, threats.Select(t => t.Cell).ToArray(), [], At(15, 0),
            threats, ownLife: 2763, ownReach: 5));
    }

    [Fact]
    public void Explanation_names_the_distance_to_the_nearest_enemy_and_the_free_cells()
    {
        Assert.Equal("ennemi le plus proche à 2 case(s), 2 case(s) libre(s) sur 3", Selector.Explain(1, [3, 2, 1], [0, 4], [2]));
        Assert.Equal("ennemis inconnus, 3 case(s) libre(s) sur 3", Selector.Explain(3, [3, 2, 1], [], []));
        Assert.Equal("aucune case retenue (0 case(s) libre(s) sur 2)", Selector.Explain(-1, [1, 2], [0], [1, 2]));
    }
}
