using System.Collections.Immutable;
using System.Linq;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Offline partition and end-to-end regression for Roublard actors.
/// Only Explobombe/3112 is established by installed client 3.7.13.17.
/// No wall, bomb age, chain, detonation or speculative damage is certified.
/// </summary>
public sealed class CombatRoguePlannerClassificationTests
{
    private static int Cell(int step)
    {
        int cell = MapPathfinder.CellFromMapPoint(8 + step, 8);
        Assert.InRange(cell, 0, MapPathfinder.CellCount - 1);
        return cell;
    }

    private static PlannerFighter Caster() => new(1, Cell(0), false, 500, 500,
        Breed: ClassMechanics.RogueBreedId);

    private static PlannerFighter Bomb(int template = ClassMechanics.VerifiedExplobombTemplate,
        long? owner = 1, bool enemy = false, int life = 100,
        FighterPositionCertainty position = FighterPositionCertainty.Confirmed)
        => new(10, Cell(2), enemy, life, 100,
            MonsterTemplate: template, SummonerId: owner, PositionCertainty: position);

    private static PlannerFighter Mob(long id, int step, int template = 5000)
        => new(id, Cell(step), true, 200, 200, MovePoints: 0, AttackRange: 0,
            ExpectedDamage: 0, ThreatKnown: true, MonsterTemplate: template);

    private static CombatTurnSnapshot Snapshot(params PlannerFighter[] fighters) => new(
        Enumerable.Range(0, 8).Select(i => new PlannerCell(Cell(i), true, false,
            (i == 0 ? new[] { 1 } : i == 7 ? new[] { 6 } : new[] { i - 1, i + 1 })
                .Select(Cell).ToImmutableArray())).ToImmutableArray(),
        fighters.ToImmutableArray(), 1, 3, 0, ImmutableArray<PlannerSpell>.Empty,
        ImmutableDictionary<int, int>.Empty);

    private static SpellBoard Board(params PlannerFighter[] fighters) => new(
        fighters.Select(f => new SpellFighter(f.Id, f.Cell, f.Enemy, f.Life, f.MaxLife,
            f.Shield, MonsterTemplate: f.MonsterTemplate, SummonerId: f.SummonerId,
            Visibility: f.Visibility)).ToArray());

    private static (ImmutableArray<PlannerFighter> OwnedBombs, ImmutableArray<PlannerFighter> LiveEnemies)
        Partition(PlannerFighter[] observed, params SpellFighter[] overrides)
    {
        var board = overrides.Length == 0 ? Board(observed) : new SpellBoard(overrides);
        return CombatTurnPlanner.ClassifyRogueCombatants(Snapshot(observed), board);
    }

    [Fact]
    public void Observed_owned_3112_bomb_is_kept_allied_while_real_mobs_remain_enemies()
    {
        var observed = new[]
        {
            Caster(), Bomb(), Mob(20, 4, 2831), Mob(21, 5, 3112), Mob(22, 6),
            new PlannerFighter(23, Cell(7), false, 100, 100,
                MonsterTemplate: 2832, SummonerId: 1)
        };
        var (bombs, enemies) = Partition(observed);

        Assert.Equal(new[] { 10L }, bombs.Select(b => b.Id));
        Assert.Equal(new[] { 20L, 21L, 22L }, enemies.Select(e => e.Id));
        Assert.DoesNotContain(enemies, e => e.Id == 10 || e.Id == 23);
        Assert.All(bombs, b => Assert.True(ClassMechanics.IsRogueBomb(b, 1)));
        // A candidate straight line has no server-activated wall evidence.
        Assert.Equal(0, ClassMechanics.EvaluateRogueBombTactics(Cell(0), bombs,
            enemies, _ => true));
    }

    [Theory]
    [InlineData(2831)]
    [InlineData(2832)]
    [InlineData(2833)]
    [InlineData(2834)]
    [InlineData(2835)]
    [InlineData(2836)]
    public void Old_2831_through_2836_templates_never_claim_the_verified_3112_bomb_role(int template)
    {
        var observed = new[] { Caster(), Bomb(template: template), Mob(20, 5, template) };
        var (bombs, enemies) = Partition(observed);

        Assert.Empty(bombs);
        Assert.Equal(new[] { 20L }, enemies.Select(e => e.Id));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(2L)]
    [InlineData(99L)]
    public void Matching_bomb_template_without_the_actual_summoner_does_not_claim_ownership(long owner)
    {
        var observed = new[] { Caster(), Bomb(owner: owner), Mob(20, 5) };
        var (bombs, enemies) = Partition(observed);

        Assert.Empty(bombs);
        Assert.Equal(new[] { 20L }, enemies.Select(e => e.Id));
    }

    [Fact]
    public void Unknown_owner_dead_actor_and_unconfirmed_position_are_not_accepted_as_owned_bombs()
    {
        var self = Caster();
        Assert.Empty(Partition(new[] { self, Bomb(owner: null) }).OwnedBombs);
        Assert.Empty(Partition(new[] { self, Bomb(life: 0) }).OwnedBombs);
        Assert.Empty(Partition(new[] { self, Bomb(position: FighterPositionCertainty.Unknown) }).OwnedBombs);
        Assert.Empty(Partition(new[] { self, Bomb(position: FighterPositionCertainty.LastKnown) }).OwnedBombs);
        Assert.Empty(Partition(new[] { self, Bomb(enemy: true) }).OwnedBombs);
        Assert.Equal(new[] { 10L }, Partition(new[] { self, Bomb(enemy: true) }).LiveEnemies.Select(e => e.Id));
    }

    [Fact]
    public void Resolver_only_or_simulated_moved_bomb_cannot_gain_server_confirmed_location()
    {
        var observed = new[] { Caster(), Bomb(), Mob(20, 5) };
        var board = Board(observed).Fighters.ToArray();
        board[1] = board[1] with { Cell = Cell(3) };
        var moved = Partition(observed, board);
        Assert.Empty(moved.OwnedBombs);
        Assert.Equal(new[] { 20L }, moved.LiveEnemies.Select(e => e.Id));

        var newlySummoned = board.Append(new SpellFighter(11, Cell(6), false, 100, 100,
            MonsterTemplate: 3112, SummonerId: 1)).ToArray();
        Assert.Empty(Partition(observed, newlySummoned).OwnedBombs);
    }

    [Fact]
    public void Resolver_cannot_change_identity_team_template_or_owner_into_a_verified_bomb()
    {
        var observed = new[] { Caster(), Bomb(), Mob(20, 5) };
        var board = Board(observed).Fighters.ToArray();
        foreach (var fake in new[]
        {
            board[1] with { MonsterTemplate = 2831 },
            board[1] with { SummonerId = 2 },
            board[1] with { Enemy = true }
        })
        {
            var changed = board.ToArray();
            changed[1] = fake;
            Assert.Empty(Partition(observed, changed).OwnedBombs);
            if (fake.Enemy)
                Assert.DoesNotContain(Partition(observed, changed).LiveEnemies, e => e.Id == 10);
        }
    }

    [Fact]
    public void A_new_unobserved_enemy_monster_stays_an_enemy_but_a_simulated_ally_side_change_does_not()
    {
        var observed = new[] { Caster(), Bomb(), Mob(20, 5, 2831) };
        var board = Board(observed).Fighters.ToArray();
        var changedAlly = board.Select(f => f.Id == 10 ? f with { Enemy = true } : f);
        var newlyAppeared = new SpellFighter(99, Cell(6), true, 80, 80, MonsterTemplate: 2834);
        var (bombs, enemies) = Partition(observed, changedAlly.Append(newlyAppeared).ToArray());

        Assert.Empty(bombs);
        Assert.Equal(new[] { 20L, 99L }, enemies.Select(e => e.Id));
    }

    [Fact]
    public void Duplicate_actor_identity_cannot_be_counted_as_two_owned_bombs()
    {
        var observed = new[] { Caster(), Bomb(), Mob(20, 5) };
        var board = Board(observed).Fighters.ToArray();
        var duplicate = board.Append(board[1]).ToArray();
        Assert.Empty(Partition(observed, duplicate).OwnedBombs);

        var ambiguousObserved = observed.Append(observed[1]).ToArray();
        Assert.Empty(Partition(ambiguousObserved).OwnedBombs);
    }

    [Fact]
    public void A_real_non_bomb_monster_remains_a_legal_damage_target_in_the_presence_of_an_owned_3112_bomb()
    {
        var bomb = Bomb();
        var enemy = Mob(20, 5, 2831);
        var damage = new SpellEffectDescriptor(99, SpellEffectKind.Damage, 40, 40, 2,
            0, "A", 'P', 0, 0, DiceNum: 40, DiceSide: 40, Triggers: "I");
        var cast = new SpellCastDescriptor(2000, 1, 3, 1, 7, 0, false,
            1, 1, 0, 0, "", ImmutableArray.Create(damage));
        var configured = new PlannerSpell(cast, 1, 1,
            ImmutableDictionary<long, int>.Empty,
            ImmutableHashSet.Create(10L, 20L));
        var snapshot = Snapshot(Caster(), bomb, enemy) with
        { Spells = ImmutableArray.Create(configured), MaxCasts = 1 };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Contains(plan.Actions, a => a.Kind == PlannerActionKind.Cast
            && a.SpellId == 2000 && a.Target == enemy.Id);
        Assert.DoesNotContain(plan.Actions, a => a.Kind == PlannerActionKind.Cast
            && a.Target == bomb.Id);
        Assert.True(plan.Score.Damage > 0);
    }
}
