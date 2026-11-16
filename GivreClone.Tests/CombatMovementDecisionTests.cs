extern alias F06App;
using Xunit;
using Decision = F06App::GivreClone.App.Services.CombatMovementDecision;
using Policy = F06App::GivreClone.App.Services.CombatMovementPolicy;
using Profile = F06App::GivreClone.App.Services.CombatProfile;
using Spell = F06App::GivreClone.App.Services.SpellConfig;
using Style = F06App::GivreClone.App.Services.CombatStyle;
using Sim = F06App::GivreClone.App.Services.CombatSimulator;
using GivreClone.Protocol;

namespace GivreClone.Tests;

public sealed class CombatMovementDecisionTests
{
    [Theory]
    [InlineData(Style.Aggressive)]
    [InlineData(Style.Fleeing)]
    [InlineData(Style.Passive)]
    public void Global_policy_blocks_cast_reposition_even_when_spell_allows_it(Style style)
    {
        Assert.False(Decision.CanReposition(Policy.NeverApproach, style, true));
        Assert.False(Decision.CanReposition(Policy.Standard, style, false));
        Assert.Equal(style != Style.Passive, Decision.CanReposition(Policy.Standard, style, true));
    }

    [Theory]
    [InlineData(8, 3, false)]
    [InlineData(3, 8, true)]
    [InlineData(5, 5, true)]
    [InlineData(0, 0, true)]
    public void Challenge_or_style_destination_cannot_reduce_nearest_enemy_distance(int before, int after, bool allowed)
    {
        Assert.Equal(allowed, Decision.CanMove(Policy.NeverApproach, before, after));
        Assert.True(Decision.CanMove(Policy.Standard, before, after));
    }

    private static Sim.SimState State(int enemyCell = 204) => new()
    {
        SelfCell = 200, Ap = 6, Mp = 3,
        Enemies = [new() { ActorId = -1, Cell = enemyCell, Hp = 100, MaxHp = 100 }],
    };

    [Fact]
    public void Script_destination_cannot_hide_an_approaching_intermediate_step()
    {
        var (cx, cy) = MapPathfinder.MapPointOf(300);
        int Cell(int x, int y) => MapPathfinder.CellFromMapPoint(cx + x - 8, cy + y - 8);
        int[] path = [Cell(8, 8), Cell(9, 8), Cell(9, 9), Cell(8, 9), Cell(8, 10)];
        Assert.DoesNotContain(-1, path);
        int enemy = Cell(10, 8);
        bool Walk(int c) => path.Contains(c);
        bool Terrain(int from, int to) => Enumerable.Range(0, path.Length - 1)
            .Any(i => path[i] == from && path[i + 1] == to || path[i + 1] == from && path[i] == to);
        int Nearest(int c) => MapPathfinder.CellDistance(c, enemy);
        Assert.True(Decision.CanMove(Policy.NeverApproach, Nearest(path[0]), Nearest(path[^1])));

        int standard = CombatMovePlanner.PickDestination(path[0], path[^1], 4, Walk,
            Decision.RestrictSteps(Policy.Standard, Nearest, Terrain), out _);
        int restricted = CombatMovePlanner.PickDestination(path[0], path[^1], 4, Walk,
            Decision.RestrictSteps(Policy.NeverApproach, Nearest, Terrain), out _);

        Assert.Equal(path[^1], standard);
        Assert.Equal(-1, restricted);
    }

    [Fact]
    public void Policy_steps_preserve_terrain_and_allow_retreat_without_enemies()
    {
        var restricted = Decision.RestrictSteps(Policy.NeverApproach, _ => 0, (from, to) => to != 2);

        Assert.True(restricted(1, 3));
        Assert.False(restricted(1, 2));
        Assert.True(Decision.RestrictSteps(Policy.NeverApproach, c => c, null)(1, 2));
        Assert.False(Decision.RestrictSteps(Policy.NeverApproach, c => c, null)(2, 1));
    }

    private static Sim.SimSpell Attack(bool allowApproach = true) => new()
    {
        Cfg = new Spell { SpellId = 1, AllowApproach = allowApproach },
        Ap = 3, Range = 6, AvgDamage = 1,
    };

    [Fact]
    public void Simulator_blocks_cast_reposition_but_still_casts_in_range()
    {
        var profile = new Profile { MovementPolicy = Policy.NeverApproach };
        Assert.Contains(Sim.SimulateTurn(State(), [Attack()], new Profile()), s => s.Kind == Sim.SimStepKind.Move);
        var blocked = Sim.SimulateTurn(State(), [Attack()], profile);
        Assert.DoesNotContain(blocked, s => s.Kind == Sim.SimStepKind.Move);
        Assert.DoesNotContain(blocked, s => s.Kind == Sim.SimStepKind.Cast);

        var inRange = Sim.SimulateTurn(State(201), [Attack()], profile);
        Assert.Contains(inRange, s => s.Kind == Sim.SimStepKind.Cast);
        Assert.DoesNotContain(inRange, s => s.Kind == Sim.SimStepKind.Move);
    }

    [Fact]
    public void Per_spell_toggle_does_not_disable_standard_aggressive_end_movement()
    {
        var standard = Sim.SimulateTurn(State(), [Attack(false)], new Profile());
        Assert.Contains(standard, s => s.Kind == Sim.SimStepKind.Move);
        var never = Sim.SimulateTurn(State(), [Attack(false)],
            new Profile { MovementPolicy = Policy.NeverApproach });
        Assert.DoesNotContain(never, s => s.Kind == Sim.SimStepKind.Move);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    public void Fleeing_can_retreat_with_global_policy(int maxDistance)
    {
        var start = State();
        var steps = Sim.SimulateTurn(start, [], new Profile
        {
            Style = Style.Fleeing, MovementPolicy = Policy.NeverApproach,
            KiteMinDistance = 10, KiteMaxDistance = maxDistance,
        });
        var move = Assert.Single(steps.Where(s => s.Kind == Sim.SimStepKind.Move));
        Assert.True(MapPathfinder.CellDistance(move.ToCell, 204) > MapPathfinder.CellDistance(start.SelfCell, 204));
    }

    [Fact]
    public void Fleeing_cannot_approach_its_band_when_global_policy_is_enabled()
    {
        var profile = new Profile { Style = Style.Fleeing, KiteMinDistance = 2, KiteMaxDistance = 4 };
        Assert.Contains(Sim.SimulateTurn(State(), [], profile), s => s.Kind == Sim.SimStepKind.Move);
        profile.MovementPolicy = Policy.NeverApproach;
        Assert.DoesNotContain(Sim.SimulateTurn(State(), [], profile), s => s.Kind == Sim.SimStepKind.Move);
    }
}
