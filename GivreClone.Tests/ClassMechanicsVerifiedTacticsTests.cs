extern alias F06App;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Deterministic, out-of-game contracts. A geometric candidate is not an
/// observed wall; a portal only belongs to an observed active owner network.
/// No bomb damage, combo age, Ecaflip card or Sacrieur suffering rule is guessed.
/// </summary>
public sealed class ClassMechanicsVerifiedTacticsTests
{
    private static int Cell(int x, int y = 0)
    {
        int cell = MapPathfinder.CellFromMapPoint(x, y);
        Assert.InRange(cell, 0, MapPathfinder.CellCount - 1);
        return cell;
    }

    private static PlannerFighter Bomb(long id, int cell, long? owner = 77,
        int template = ClassMechanics.VerifiedExplobombTemplate, int life = 100,
        bool enemy = false, FighterPositionCertainty position = FighterPositionCertainty.Confirmed)
        => new(id, cell, enemy, life, 100, MonsterTemplate: template, SummonerId: owner,
            PositionCertainty: position);

    private static PlannerFighter Target(long id, int cell, bool enemy = true, int life = 50,
        FighterPositionCertainty position = FighterPositionCertainty.Confirmed)
        => new(id, cell, enemy, life, 100, PositionCertainty: position);

    private static ClassMechanics.VerifiedRogueWall Wall(int first, int second, params int[] cells)
        => new(ClassMechanics.VerifiedExplobombWall, 77, first, second,
            cells.ToImmutableHashSet(), Active: true);

    [Fact]
    public void Verified_explobombe_template_comes_from_installed_bomb_data_not_old_unverified_templates()
    {
        // 3.7.13.17: SpellData 13444 -> action 1008 -> MonsterData/SpellBombData 3112.
        // docs/validation/roublard-bomb-contract-20261105.md:26-30,61-76,107-120.
        Assert.Equal(3112, ClassMechanics.VerifiedExplobombTemplate);
        Assert.Equal(2, ClassMechanics.VerifiedExplobombWall);
        Assert.True(ClassMechanics.IsRogueBomb(Bomb(1, Cell(10)), 77));
        foreach (int unverified in new[] { 2831, 2832, 2833, 2834, 2835, 2836, 0, 3113, 3114, 5161 })
            Assert.False(ClassMechanics.IsRogueBomb(Bomb(1, Cell(10), template: unverified), 77));
    }

    [Fact]
    public void Bomb_identity_is_actor_scoped_alive_and_position_confirmed()
    {
        var proven = Bomb(1, Cell(10));
        Assert.False(ClassMechanics.IsRogueBomb(proven, 0));
        Assert.False(ClassMechanics.IsRogueBomb(proven, 99));
        Assert.False(ClassMechanics.IsRogueBomb(proven with { SummonerId = null }, 77));
        Assert.False(ClassMechanics.IsRogueBomb(proven with { SummonerId = 0 }, 77));
        Assert.False(ClassMechanics.IsRogueBomb(proven with { Enemy = true }, 77));
        Assert.False(ClassMechanics.IsRogueBomb(proven with { Life = 0 }, 77));
        Assert.False(ClassMechanics.IsRogueBomb(proven with
            { PositionCertainty = FighterPositionCertainty.LastKnown }, 77));
        Assert.True(ClassMechanics.IsRogueBomb(proven, 77));
    }

    [Theory]
    [InlineData(12, 1)]
    [InlineData(13, 2)]
    [InlineData(18, 7)]
    public void Wall_geometry_uses_installed_linear_min_and_max_hop(int endX, int expectedCells)
    {
        var cells = ClassMechanics.GetBombWallCells(Cell(10), Cell(endX), _ => true);
        Assert.Equal(expectedCells, cells.Length);
        Assert.Equal(Enumerable.Range(11, expectedCells).Select(x => Cell(x)).ToArray(), cells.ToArray());
        Assert.DoesNotContain(Cell(10), cells);
        Assert.DoesNotContain(Cell(endX), cells);
    }

    [Fact]
    public void Wall_geometry_rejects_adjacent_overlong_blocked_offaxis_and_invalid_cells()
    {
        static bool Free(int _) => true;
        Assert.Empty(ClassMechanics.GetBombWallCells(Cell(10), Cell(11), Free));
        Assert.Empty(ClassMechanics.GetBombWallCells(Cell(10), Cell(19), Free));
        Assert.Empty(ClassMechanics.GetBombWallCells(Cell(10), Cell(12, 1), Free));
        Assert.Empty(ClassMechanics.GetBombWallCells(-1, Cell(12), Free));
        Assert.Empty(ClassMechanics.GetBombWallCells(Cell(10), 560, Free));
        Assert.Empty(ClassMechanics.GetBombWallCells(Cell(10), Cell(13), c => c != Cell(12)));
    }

    [Fact]
    public void Legacy_planner_call_never_promotes_geometric_alignment_to_unobserved_wall_damage()
    {
        var bombs = new[] { Bomb(1, Cell(10)), Bomb(2, Cell(13)) };
        var enemies = new[] { Target(8, Cell(11)) };
        Assert.Equal(0, ClassMechanics.EvaluateRogueBombTactics(Cell(10), bombs, enemies, _ => true));
    }

    [Fact]
    public void Server_confirmed_matching_active_wall_scores_only_distinct_visible_enemies_on_its_cells()
    {
        var bombs = new[] { Bomb(1, Cell(10)), Bomb(2, Cell(13)) };
        var enemies = new[]
        {
            Target(8, Cell(11)), Target(8, Cell(11)), Target(9, Cell(12)),
            Target(10, Cell(14)), Target(11, Cell(12), enemy: false),
            Target(12, Cell(11), life: 0), Target(13, Cell(11), position: FighterPositionCertainty.LastKnown)
        };
        var evidence = Wall(1, 2, Cell(11), Cell(12));
        Assert.Equal(24, ClassMechanics.EvaluateRogueBombTactics(77, bombs, enemies,
            _ => true, new[] { evidence, evidence }));
    }

    [Fact]
    public void Wall_proof_is_bound_to_owner_bomb_identity_active_cells_and_live_bombs()
    {
        var bombs = new[] { Bomb(1, Cell(10)), Bomb(2, Cell(13)) };
        var enemies = new[] { Target(8, Cell(11)) };
        double Score(ClassMechanics.VerifiedRogueWall proof, IEnumerable<PlannerFighter>? actors = null)
            => ClassMechanics.EvaluateRogueBombTactics(77, actors ?? bombs, enemies,
                _ => true, new[] { proof });
        var valid = Wall(1, 2, Cell(11), Cell(12));

        Assert.Equal(12, Score(valid));
        Assert.Equal(0, Score(valid with { Active = false }));
        Assert.Equal(0, Score(valid with { OwnerId = 999 }));
        Assert.Equal(0, Score(valid with { WallDataId = 0 }));
        Assert.Equal(0, Score(valid with { FirstBombId = 44 }));
        Assert.Equal(0, Score(valid with { SecondBombId = 1 }));
        Assert.Equal(0, Score(valid with { ActiveCells = ImmutableHashSet.Create(Cell(11)) }));
        Assert.Equal(0, Score(valid with { ActiveCells = ImmutableHashSet.Create(Cell(11), Cell(12), Cell(14)) }));
        Assert.Equal(0, Score(valid, bombs.Select(b => b.Id == 1 ? b with { Life = 0 } : b)));
        Assert.Equal(0, Score(valid, bombs.Select(b => b.Id == 2 ? b with { SummonerId = 66 } : b)));
        Assert.Equal(0, Score(valid, bombs.Select(b => b.Id == 2 ? b with { MonsterTemplate = 2831 } : b)));
        Assert.Equal(0, Score(valid, bombs.Concat(new[] { bombs[0] })));
        Assert.Equal(0, ClassMechanics.EvaluateRogueBombTactics(0, bombs, enemies,
            _ => true, new[] { valid }));
    }

    [Fact]
    public void Verified_wall_does_not_claim_an_obstruction_has_a_live_damage_effect()
    {
        var bombs = new[] { Bomb(1, Cell(10)), Bomb(2, Cell(13)) };
        var enemies = new[] { Target(8, Cell(11)) };
        Assert.Equal(0, ClassMechanics.EvaluateRogueBombTactics(77, bombs, enemies,
            c => c != Cell(12), new[] { Wall(1, 2, Cell(11), Cell(12)) }));
    }

    [Theory]
    [InlineData(1, true, 7)]
    [InlineData(3, true, 7)]
    [InlineData(3, false, 10)]
    [InlineData(7, true, 10)]
    public void Sacrieur_never_receives_unproved_contact_or_suffering_combo_bonus(
        int distance, bool aligned, int ap)
    {
        // The old +60/+85 heuristic used only distance and an assumed AP cost.
        // There is no observed suffering state, learned-spell legality or trigger
        // history in that signature. Productive real casts are still scored by
        // CombatTurnPlanner/SpellResolver; no passive is inferred here.
        Assert.Equal(0, ClassMechanics.EvaluateSacrieurCombo(Cell(10), Cell(10 + distance),
            aligned, distance, ap));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void Zobal_never_receives_a_shield_bonus_for_nearby_summons_it_did_not_shield(int nearSummons)
    {
        // The old +30 per summon read only distance: no Plastron cast, no shield
        // effect resolved, no summon actually protected. A real shield cast is
        // already scored by SpellResolver through the shield it grants.
        var summons = Enumerable.Range(0, nearSummons)
            .Select(i => new PlannerFighter(100 + i, Cell(11 + i), false, 100, 100))
            .ToArray();
        Assert.Equal(0, ClassMechanics.EvaluateZobalShieldOnSummons(Cell(10), summons));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void Forgelance_never_receives_a_cluster_bonus_for_enemies_it_did_not_hit(int grouped)
    {
        // The old +45/+90 rewarded enemies merely standing together, on every
        // branch, whatever was cast. Area damage that really lands is scored by
        // SpellResolver as damage; geometry alone proves nothing.
        var enemies = Enumerable.Range(0, grouped).Select(i => Cell(10 + i));
        Assert.Equal(0, ClassMechanics.EvaluatePandaForgelanceCluster(enemies));
    }

    [Fact]
    public void Panda_bomb_wall_throw_grants_no_fixed_bonus_without_a_resolved_wall_hit()
    {
        // Same contract as the Rogue wall overload: no wall-activation event
        // is available from a cell set and an estimate, so no fixed 75/120.
        var wall = ImmutableHashSet.Create(Cell(12), Cell(13));
        Assert.Equal(0, ClassMechanics.EvaluatePandaThrowIntoBombWall(Cell(12), wall, 100, 500));
        Assert.Equal(0, ClassMechanics.EvaluatePandaThrowIntoBombWall(Cell(12), wall, 1000, 50));
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10, -1)]
    [InlineData(560, 10)]
    [InlineData(10, 560)]
    public void Portal_projection_refuses_invalid_board_cells(int caster, int entry)
    {
        Assert.Null(ClassMechanics.ProjectThroughPortals(caster, entry, new[] { entry, Cell(13) }));
    }

    [Fact]
    public void Portal_projection_refuses_duplicate_over_capacity_or_invalid_networks()
    {
        int caster = Cell(10), entry = Cell(12);
        Assert.Null(ClassMechanics.ProjectThroughPortals(caster, entry, new[] { entry, entry, Cell(15) }));
        Assert.Null(ClassMechanics.ProjectThroughPortals(caster, entry,
            new[] { entry, Cell(13), Cell(14), Cell(15), Cell(16) }));
        Assert.Null(ClassMechanics.ProjectThroughPortals(caster, entry, new[] { entry, 560 }));
        Assert.Null(ClassMechanics.ProjectThroughPortals(caster, entry, new[] { Cell(13), Cell(14) }));

        var valid = ClassMechanics.ProjectThroughPortals(caster, entry, new[] { entry, Cell(15) });
        Assert.NotNull(valid);
        Assert.Equal(Cell(15), valid!.Value.Exit);
        Assert.Equal(Cell(17), valid.Value.Impact);
    }

    [Fact]
    public void Observed_portal_network_excludes_inactive_and_foreign_owner_marks()
    {
        int caster = Cell(10), entry = Cell(12), exit = Cell(17);
        var marks = new[]
        {
            new ClassMechanics.Portal(1, entry, 42),
            new ClassMechanics.Portal(2, Cell(13), 8), // belongs to a second Eliotrope
            new ClassMechanics.Portal(3, Cell(14), 42, Active: false),
            new ClassMechanics.Portal(4, exit, 42)
        };
        var route = ClassMechanics.ProjectThroughPortals(caster, entry, marks, ownerId: 42);
        Assert.NotNull(route);
        Assert.Equal(exit, route!.Value.Exit);
        Assert.Equal(Cell(19), route.Value.Impact);
        Assert.Equal(MapPathfinder.CellDistance(entry, exit), route.Value.NetworkDistance);
        Assert.Null(ClassMechanics.ProjectThroughPortals(caster, entry, marks, ownerId: 8));
    }

    [Fact]
    public void Observed_portal_network_fails_closed_for_unknown_or_duplicate_mark_identity()
    {
        int caster = Cell(10), entry = Cell(12), exit = Cell(16);
        var valid = new[]
        {
            new ClassMechanics.Portal(1, entry, 42),
            new ClassMechanics.Portal(2, exit, 42)
        };
        Assert.NotNull(ClassMechanics.ProjectThroughPortals(caster, entry, valid, 42));
        Assert.Null(ClassMechanics.ProjectThroughPortals(caster, entry, valid, 0));
        Assert.Null(ClassMechanics.ProjectThroughPortals(caster, entry,
            valid.Select(p => p with { Id = 0 }).ToArray(), 42));
        Assert.Null(ClassMechanics.ProjectThroughPortals(caster, entry,
            new[] { valid[0], valid[1] with { Id = valid[0].Id } }, 42));
        Assert.Null(ClassMechanics.ProjectThroughPortals(caster, entry,
            valid.Select(p => p with { Active = false }).ToArray(), 42));
    }
}
