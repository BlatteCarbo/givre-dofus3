using GivreClone.App.Services.CombatParity;
using Xunit;

namespace GivreClone.Tests;

public class AoeAimPolicyTests
{
    [Fact]
    public void Single_enemy_out_of_direct_range_can_be_hit_from_an_empty_center()
        => Assert.Equal(4, AoeAimPolicy.Find(5, new[] { 3, 4, 5 }, c => c <= 4,
            c => new[] { c - 1, c, c + 1 }, new[] { 5 }));

    [Fact]
    public void Caster_cell_can_be_the_best_center()
        => Assert.Equal(10, AoeAimPolicy.Find(9, new[] { 9, 10, 11 }, _ => true,
            c => new[] { c - 1, c, c + 1 }, new[] { 9, 11 }));

    [Fact]
    public void Empty_center_does_not_bypass_occupancy_allies_or_target_gates()
        => Assert.Equal(-1, AoeAimPolicy.Find(5, new[] { 3, 4, 5 }, _ => false,
            c => new[] { c - 1, c, c + 1 }, new[] { 5 }));

    [Fact]
    public void Center_must_hit_the_selected_enemy_and_ties_keep_direct_target()
    {
        Assert.Equal(5, AoeAimPolicy.Find(5, new[] { 4, 6, 10 }, _ => true,
            c => new[] { c - 1, c, c + 1 }, new[] { 5, 9, 10, 11 }));
    }
}
