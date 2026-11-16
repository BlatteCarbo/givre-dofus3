using GivreClone.Protocol;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class CombatDecisionTests
{
    [Theory]
    [InlineData(false, SpellTargetKind.Enemy, true)]
    [InlineData(false, SpellTargetKind.Ally, false)]
    [InlineData(true, SpellTargetKind.Enemy, false)]
    [InlineData(true, SpellTargetKind.Ally, false)]
    public void Offensive_fallback_never_redirects_support_spells_to_an_enemy(bool onMe,
        SpellTargetKind targetKind, bool allowed)
    {
        // Le filet de secours ne doit admettre qu'un sort réellement défini :
        // un objet par défaut avec SpellId=0 est volontairement refusé.
        var spell = new SpellConfig { SpellId = 123, OnMe = onMe, TargetKind = targetKind };
        Assert.Equal(allowed, spell.CanUseOffensiveFallback());
    }

    [Fact]
    public void Cast_position_uses_reachable_steps_and_deterministic_ties()
    {
        var reach = MapPathfinder.OrthoReachable(300, 4, _ => true);
        var targets = reach.Where(x => x.Value == 2).Select(x => x.Key).ToHashSet();
        int cell = CombatPositionPlanner.FindCastCell(300, 4, _ => true, targets.Contains, out int steps);
        Assert.Equal(targets.Min(), cell);
        Assert.Equal(2, steps);
    }

    [Fact]
    public void A_sector_barrier_prevents_a_cast_position()
    {
        int cell = CombatPositionPlanner.FindCastCell(300, 4, _ => true, c => c != 300,
            out int steps, (_, _) => false);
        Assert.Equal(-1, cell);
        Assert.Equal(0, steps);
    }

    [Fact]
    public void Cast_positions_are_explored_once_within_the_grid_budget()
    {
        int calls = 0;
        int cell = CombatPositionPlanner.FindCastCell(300, 560, _ => { calls++; return true; },
            _ => false, out int steps);
        Assert.Equal(-1, cell);
        Assert.Equal(0, steps);
        Assert.InRange(calls, 1, 2240);
    }

    [Theory]
    [InlineData(-1, 4)]
    [InlineData(560, 4)]
    [InlineData(300, 0)]
    public void Invalid_origin_or_empty_movement_budget_does_not_search(int from, int mp)
    {
        int cell = CombatPositionPlanner.FindCastCell(from, mp, _ => throw new Exception(),
            _ => throw new Exception(), out int steps);
        Assert.Equal(-1, cell);
        Assert.Equal(0, steps);
    }

    [Fact]
    public void An_area_touching_an_ally_is_rejected_even_with_many_enemies()
    {
        var result = CombatAoeSafety.Evaluate(new[] { 1, 2, 3 }, new[] { 1, 2 }, new[] { 3 }, 2);
        Assert.False(result.Allowed);
        Assert.Equal("ally_in_zone", result.Reason);
    }

    [Fact]
    public void Minimum_hits_is_checked_on_the_actual_zone_without_double_counting()
    {
        var result = CombatAoeSafety.Evaluate(new[] { 1, 1, 2 }, new[] { 1, 1, 3 }, Array.Empty<int>(), 2);
        Assert.False(result.Allowed);
        Assert.Equal(1, result.EnemiesHit);
        Assert.Equal("too_few_enemies", result.Reason);
    }

    // Combat réel 24/10 11:02 (Crâ, sort 32443, zone T taille 2) : au contact, l'orientation tombait sur une diagonale,
    // la zone devenait une croix incluant le lanceur et le sort était refusé quatre tours de suite -> défaite.
    [Theory]
    [InlineData(218, 232, 3)]
    [InlineData(232, 246, 1)]
    [InlineData(188, 246, 1)]
    public void Orientation_toward_a_cardinal_neighbour_is_the_axis_not_a_diagonal(int from, int to, int expected)
    {
        Assert.Equal(expected, CombatZones.OrientationTo(from, to));
    }

    [Theory]
    [InlineData(218, 232)]
    [InlineData(232, 246)]
    public void Perpendicular_T_zone_cast_at_contact_never_contains_the_caster(int from, int to)
    {
        var area = CombatZones.GetCells((byte)'T', to, CombatZones.OrientationTo(from, to), 0, 2);
        Assert.Equal(5, area.Count);
        Assert.DoesNotContain(from, area);
        Assert.True(CombatAoeSafety.Evaluate(area, new[] { to }, new[] { from }, 0).Allowed);
    }

    [Fact]
    public void Cast_position_can_leave_the_damage_zone_without_spending_extra_steps()
    {
        const int target = 300;
        var area = CombatZones.GetCells((byte)'C', target, 1, 0, 2).ToHashSet();
        int from = area.First(c => MapPathfinder.CellDistance(c, target) == 2);
        bool CanCastSafely(int c) => MapPathfinder.CellDistance(c, target) <= 6 &&
            CombatAoeSafety.Evaluate(area, new[] { target }, new[] { c }, 1).Allowed;

        int cell = CombatPositionPlanner.FindCastCell(from, 3, c => c != target,
            CanCastSafely, out int steps);

        Assert.False(CanCastSafely(from));
        Assert.True(cell >= 0);
        Assert.DoesNotContain(cell, area);
        Assert.Equal(1, steps);
    }

    [Fact]
    public void A_clean_zone_with_enough_enemies_is_allowed()
    {
        var result = CombatAoeSafety.Evaluate(new[] { 1, 2 }, new[] { 1, 2, 3 }, new[] { 4 }, 2);
        Assert.True(result.Allowed);
        Assert.Equal(2, result.EnemiesHit);
    }
}
