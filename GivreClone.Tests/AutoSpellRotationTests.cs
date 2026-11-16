extern alias F06App;

using System.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;
using GivreClone.App.Services;
using GivreClone.App.Services.Combat;
using GivreClone.App.Services.CombatParity;
using Xunit;

namespace GivreClone.Tests;

public class AutoSpellRotationTests
{
    [Fact]
    public void Learned_superpowerful_targets_allied_tree_and_requires_tactical_planning()
    {
        var names = new F06App::GivreClone.App.Services.GameNameResolver();
        names.EnsureCombatLoaded();
        var actual = names.GetSpellDescriptor(13578, 2)!;
        var descriptor = System.Text.Json.JsonSerializer.Deserialize<SpellCastDescriptor>(
            System.Text.Json.JsonSerializer.Serialize(actual))!;

        var rotation = AutoSpellRotation.Build(System.Array.Empty<SpellConfig>(),
            new[] { new LearnedSpellInfo(13578, descriptor.Ap, 0, descriptor) },
            new AutoSpellOptions(RequireDescriptors: true));

        Assert.Equal(SpellTargetKind.Ally, Assert.Single(rotation).TargetKind);
        Assert.True(AutoSpellRotation.RequiresTacticalPlanning(descriptor));
    }

    [Theory]
    [InlineData(14596, 2, true)]
    [InlineData(14577, 2, true)]
    [InlineData(14574, 3, true)]
    [InlineData(14582, 3, true)]
    [InlineData(14579, 3, false)]
    [InlineData(14587, 3, false)]
    public void Classic_rotation_leaves_trigger_only_utilities_and_portal_targets_to_tactical_planning(
        int spellId, int grade, bool expected)
    {
        var names = new F06App::GivreClone.App.Services.GameNameResolver();
        names.EnsureCombatLoaded();
        var actual = names.GetSpellDescriptor(spellId, grade);
        Assert.NotNull(actual);
        var descriptor = System.Text.Json.JsonSerializer.Deserialize<SpellCastDescriptor>(
            System.Text.Json.JsonSerializer.Serialize(actual))!;

        Assert.Equal(expected, AutoSpellRotation.RequiresTacticalPlanning(descriptor));
    }

    [Fact]
    public void Neutral_without_attacks_uses_the_planner_to_evaluate_portal_danger()
    {
        var neutral = new SpellCastDescriptor(14582, 3, 1, 0, 8, 4, false, 2, 0, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(1183, SpellEffectKind.Unknown, 0, 0, 0, 1,
                "a,A", 'P', 0, 0, Triggers: "I")));

        Assert.True(AutoSpellRotation.ShouldUsePlanner(new[] { neutral }));
    }

    [Fact]
    public void Learned_Cabale_is_configured_for_allies_and_keeps_its_cooldown()
    {
        var names = new F06App::GivreClone.App.Services.GameNameResolver();
        names.EnsureLoaded();
        names.EnsureCombatLoaded();
        var actual = names.GetSpellDescriptor(14617, 3)
            ?? throw new System.InvalidOperationException("Missing Cabale grade 3");
        var descriptor = System.Text.Json.JsonSerializer.Deserialize<SpellCastDescriptor>(
            System.Text.Json.JsonSerializer.Serialize(actual))!;
        var rotation = AutoSpellRotation.Build(System.Array.Empty<SpellConfig>(),
            new[] { new LearnedSpellInfo(14617, descriptor.Ap, 0, descriptor) },
            new AutoSpellOptions(RequireDescriptors: true));

        var spell = Assert.Single(rotation);
        Assert.Equal(SpellTargetKind.Ally, spell.TargetKind);
        Assert.True(spell.SelfBuff);
        Assert.Equal(System.Math.Max(0, descriptor.MinCastInterval - 1), spell.Cooldown);
    }

    private static SpellConfig S(int id, int prio, bool on = true) => new() { SpellId = id, Priority = prio, Enabled = on };
    private static LearnedSpellInfo Damage(int id, params int[] elements)
    {
        var descriptor = new SpellCastDescriptor(id, 1, 3, 0, 6, 0, false, 1, 1, 0, 0, "",
            elements.Select(element => new SpellEffectDescriptor(100, SpellEffectKind.Damage,
                10, 10, element, 0, "A", 'P', 0, 0)).ToImmutableArray());
        return new(id, 3, elements.Length * 10, descriptor);
    }

    [Fact]
    public void Learned_damage_spells_are_appended_after_the_profile_by_damage_per_ap()
    {
        var profile = new[] { S(10, 1), S(11, 2) };
        var learned = new[] { new LearnedSpellInfo(20, 4, 40), new LearnedSpellInfo(21, 2, 30), new LearnedSpellInfo(10, 3, 99) };
        var rot = AutoSpellRotation.Build(profile, learned);
        Assert.Equal(new[] { 10, 11, 21, 20 }, rot.Select(s => s.SpellId).ToArray());   // 21 : 15 dégâts/PA > 20 : 10/PA
        Assert.All(rot.Skip(2), s => Assert.True(s.Priority > 2));
    }

    [Theory]
    [InlineData(108, SpellEffectKind.Heal, 0, true, "a")]
    [InlineData(1040, SpellEffectKind.Shield, 0, true, "a")]
    [InlineData(111, SpellEffectKind.Buff, 0, true, "a")]
    [InlineData(128, SpellEffectKind.Buff, 0, true, "a")]
    [InlineData(111, SpellEffectKind.Buff, 1, false, "a")]
    [InlineData(108, SpellEffectKind.Heal, 1, false, "a")]
    [InlineData(108, SpellEffectKind.Heal, 2, true, "a")]
    [InlineData(108, SpellEffectKind.Heal, 3, false, "a")]
    // A missing descriptor at the start still requires classic fallback,
    // even when a modeled heal and attack follow later in the rotation.
    [InlineData(108, SpellEffectKind.Heal, 4, false, "a")]
    [InlineData(111, SpellEffectKind.Buff, 0, true, "C")]
    [InlineData(120, SpellEffectKind.Unknown, 0, true, "C")]
    [InlineData(128, SpellEffectKind.Buff, 0, true, "C")]
    [InlineData(111, SpellEffectKind.Buff, 1, false, "C")]
    [InlineData(118, SpellEffectKind.Buff, 0, true, "C")]
    [InlineData(115, SpellEffectKind.Unknown, 0, true, "C")]
    [InlineData(138, SpellEffectKind.Buff, 0, true, "a")]
    [InlineData(418, SpellEffectKind.Unknown, 0, true, "C")]
    [InlineData(118, SpellEffectKind.Buff, 1, false, "C")]
    [InlineData(115, SpellEffectKind.Unknown, 3, false, "C")]
    [InlineData(1079, SpellEffectKind.Debuff, 1, false, "H")]
    [InlineData(1080, SpellEffectKind.Debuff, 3, false, "L")]
    public void Support_only_rotation_uses_planner_unless_an_unmodeled_attack_needs_fallback(
        int action, SpellEffectKind kind, int additionalSpell, bool expected, string mask)
    {
        var support = new SpellCastDescriptor(70, 1, 3, 0, 6, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(action, kind, 40, 40, 2, 0, mask, 'P', 0, 0)));
        var descriptors = new List<SpellCastDescriptor?> { support };
        switch (additionalSpell)
        {
            case 1: descriptors.Add(Damage(71, 2).Descriptor! with { StatesCriterion = "HS!97" }); break;
            case 2: descriptors.Add(Damage(71, 2).Descriptor!); break;
            case 3: descriptors.Add(null); break;
            case 4:
                descriptors.Insert(0, null);
                descriptors.Add(Damage(71, 2).Descriptor!);
                break;
        }

        Assert.Equal(expected, AutoSpellRotation.ShouldUsePlanner(descriptors));
    }

    [Fact]
    public void Unchecked_listed_spells_stay_out_and_spells_without_damage_are_not_added()
    {
        var profile = new[] { S(10, 1, on: false) };
        var learned = new[] { new LearnedSpellInfo(10, 3, 50), new LearnedSpellInfo(30, 2, 0), new LearnedSpellInfo(31, 0, 40) };
        Assert.Empty(AutoSpellRotation.Build(profile, learned));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Learned_teleport_can_use_the_planner_without_hiding_an_unmodeled_attack(bool unknownAttack, bool expected)
    {
        var teleport = new SpellCastDescriptor(70, 1, 3, 0, 4, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(4, SpellEffectKind.Teleport,
                0, 0, -1, 0, "C", 'P', 0, 0)));
        var rotation = AutoSpellRotation.Build(new SpellConfig[0],
            new[] { new LearnedSpellInfo(70, 3, 0, teleport) }, new(RequireDescriptors: true));
        Assert.Equal(70, Assert.Single(rotation).SpellId);
        var descriptors = new List<SpellCastDescriptor?> { teleport };
        if (unknownAttack) descriptors.Add(Damage(71, 2).Descriptor! with { StatesCriterion = "HS!97" });

        Assert.Equal(expected, AutoSpellRotation.ShouldUsePlanner(descriptors));
    }

    [Theory]
    [InlineData(80, true)]
    [InlineData(0, false)]
    public void Learned_teleport_without_an_attack_is_spent_only_to_reduce_the_enemy_reply(int incoming, bool escape)
    {
        int Cell(int step) => GivreClone.Protocol.MapPathfinder.CellFromMapPoint(8 + step, 8);
        var teleport = new SpellCastDescriptor(70, 1, 3, 0, 4, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(4, SpellEffectKind.Teleport,
                0, 0, -1, 0, "C", 'P', 0, 0)));
        var rotation = AutoSpellRotation.Build(new SpellConfig[0],
            new[] { new LearnedSpellInfo(70, 3, 0, teleport) }, new(RequireDescriptors: true));
        Assert.True(AutoSpellRotation.ShouldUsePlanner(rotation.Select(_ => teleport)));
        var spells = rotation.Select(s => new PlannerSpell(teleport, s.PerTurn, 1,
            ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L))).ToImmutableArray();
        var snapshot = new CombatTurnSnapshot(
            Enumerable.Range(0, 6).Select(i => new PlannerCell(Cell(i), true, false,
                ImmutableArray<int>.Empty)).ToImmutableArray(),
            ImmutableArray.Create(new PlannerFighter(1, Cell(1), false, 100, 100),
                new PlannerFighter(2, Cell(0), true, 500, 500, MovePoints: 0,
                    AttackRange: 1, ExpectedDamage: incoming, ThreatKnown: true)),
            1, 3, 0, spells, ImmutableDictionary<int, int>.Empty);

        var plan = CombatTurnPlanner.Choose(snapshot);

        if (escape)
        {
            var cast = Assert.Single(plan.Actions);
            Assert.Equal(70, cast.SpellId);
            Assert.True(GivreClone.Protocol.MapPathfinder.CellDistance(Cell(0), cast.Cell) > 1);
            Assert.Equal(0, plan.Score.Incoming);
        }
        else Assert.Empty(plan.Actions);
        Assert.Equal(0, plan.Score.Damage);
    }

    [Theory]
    [InlineData(200, 0, false, 20)]
    [InlineData(0, 200, false, 21)]
    [InlineData(200, 0, true, 20)]
    [InlineData(0, 200, true, 21)]
    public void Actual_elemental_characteristics_determine_generated_attack_order(int intelligence, int strength, bool selfCost, int first)
    {
        var characteristics = new Dictionary<int, int>
        {
            [CharIds.Intelligence] = intelligence, [CharIds.Strength] = strength
        };

        var fire = Damage(20, 2);
        var earth = Damage(21, 5);
        if (selfCost)
        {
            var weaker = first == 20 ? earth : fire;
            weaker = weaker with { Descriptor = weaker.Descriptor! with
            {
                Effects = weaker.Descriptor!.Effects.Add(new SpellEffectDescriptor(100, SpellEffectKind.Damage,
                    100, 100, 0, 0, "C", 'P', 0, 0))
            } };
            if (first == 20) earth = weaker; else fire = weaker;
        }
        var rotation = AutoSpellRotation.Build(new SpellConfig[0],
            new[] { fire, earth }, new(Characteristics: characteristics, RequireDescriptors: true));

        Assert.Equal(first, rotation[0].SpellId);
    }

    [Theory]
    [InlineData(AutoCombatElements.Fire, 100, false)]
    [InlineData(AutoCombatElements.Fire | AutoCombatElements.Water, 100, true)]
    [InlineData(AutoCombatElements.Automatic, 100, true)]
    [InlineData(AutoCombatElements.Fire, 2832, false)]
    [InlineData(AutoCombatElements.Fire | AutoCombatElements.Water, 2832, true)]
    [InlineData(AutoCombatElements.Fire, 2822, true)]
    [InlineData(AutoCombatElements.Fire, 2828, true)]
    [InlineData(AutoCombatElements.Fire, 2890, false)]
    [InlineData(AutoCombatElements.Fire | AutoCombatElements.Water, 2890, true)]
    public void Every_damage_effect_must_match_the_selected_elements(AutoCombatElements elements, int secondAction, bool included)
    {
        var learned = Damage(20, 2, 3);
        learned = learned with { Descriptor = learned.Descriptor! with
        {
            Effects = learned.Descriptor!.Effects.SetItem(1, learned.Descriptor!.Effects[1] with
            {
                ActionId = secondAction, Kind = SpellEffectDescriptor.Classify(secondAction),
                Element = secondAction == 100 ? 3 : 4
            })
        } };
        var rotation = AutoSpellRotation.Build(new SpellConfig[0],
            new[] { learned }, new(elements, new Dictionary<int, int>
            {
                [CharIds.Strength] = 100, [CharIds.Intelligence] = 200,
                [CharIds.Chance] = 0, [CharIds.Agility] = 200
            }, RequireDescriptors: true));

        Assert.Equal(included ? new[] { 20 } : new int[0], rotation.Select(s => s.SpellId));
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(4, false)]
    public void Element_filter_keeps_off_element_self_costs_and_the_planner_still_applies_them(int life, bool cast)
    {
        var attack = Damage(20, 2);
        var descriptor = attack.Descriptor! with
        {
            Effects = attack.Descriptor!.Effects.SetItem(0, attack.Descriptor!.Effects[0] with { Min = 40, Max = 40 })
                .Add(new SpellEffectDescriptor(100, SpellEffectKind.Damage,
                5, 5, 0, 0, "C", 'P', 0, 0))
        };
        var rotation = AutoSpellRotation.Build(new SpellConfig[0],
            new[] { attack with { Descriptor = descriptor } }, new(AutoCombatElements.Fire, RequireDescriptors: true));
        Assert.Equal(20, Assert.Single(rotation).SpellId);
        int caster = GivreClone.Protocol.MapPathfinder.CellFromMapPoint(8, 8);
        int enemy = GivreClone.Protocol.MapPathfinder.CellFromMapPoint(10, 8);
        var snapshot = new CombatTurnSnapshot(
            ImmutableArray.Create(new PlannerCell(caster, true, false, ImmutableArray<int>.Empty),
                new PlannerCell(enemy, true, false, ImmutableArray<int>.Empty)),
            ImmutableArray.Create(new PlannerFighter(1, caster, false, life, 100),
                new PlannerFighter(2, enemy, true, 100, 100, MovePoints: 0, AttackRange: 0,
                    ExpectedDamage: 0, ThreatKnown: true)),
            1, 3, 0, ImmutableArray.Create(new PlannerSpell(descriptor, 1, 1,
                ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(2L))), ImmutableDictionary<int, int>.Empty);

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(cast ? new[] { 20 } : new int[0], plan.Actions.Select(a => a.SpellId));
        Assert.Equal(cast ? 40 : 0, plan.Score.Damage);
        Assert.Equal(cast ? 20 : 0, plan.Score.FriendlyFire);
    }

    [Fact]
    public void Element_selection_preserves_manual_overrides_and_supported_utilities()
    {
        var manual = S(30, 1);
        var heal = new SpellCastDescriptor(40, 1, 3, 0, 0, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(108, SpellEffectKind.Heal,
                20, 20, -1, 0, "C", 'P', 0, 0)));

        var rotation = AutoSpellRotation.Build(new[] { manual, S(31, 2, false) },
            new[] { Damage(30, 5), Damage(31, 2), new LearnedSpellInfo(40, 3, 0, heal) },
            new(AutoCombatElements.Fire, RequireDescriptors: true));

        Assert.Equal(new[] { 30, 40 }, rotation.Select(s => s.SpellId));
        Assert.Same(manual, rotation[0]);
    }

    [Theory]
    [InlineData(AutoCombatElements.Automatic, true)]
    [InlineData(AutoCombatElements.Water, false)]
    public void Automatic_elements_keep_off_element_push_as_a_tactical_candidate(AutoCombatElements elements, bool included)
    {
        var attack = Damage(71, 4);
        var push = attack with { SpellId = 72, Descriptor = attack.Descriptor! with {
            SpellId = 72,
            Effects = attack.Descriptor.Effects.Add(new SpellEffectDescriptor(5,
                SpellEffectKind.Push, 2, 2, -1, 0, "A", 'P', 0, 0))
        } };
        var rotation = AutoSpellRotation.Build(Array.Empty<SpellConfig>(), new[] { attack, push },
            new(elements, new Dictionary<int, int> { [CharIds.Chance] = 300 }, RequireDescriptors: true));

        Assert.DoesNotContain(rotation, spell => spell.SpellId == 71);
        Assert.Equal(included, rotation.Any(spell => spell.SpellId == 72));
    }

    [Fact]
    public void Learned_support_and_teleport_keep_their_targeting_and_server_cast_caps()
    {
        var effects = new[]
        {
            new SpellEffectDescriptor(108, SpellEffectKind.Heal, 20, 20, -1, 0, "C", 'P', 0, 0),
            new SpellEffectDescriptor(1040, SpellEffectKind.Shield, 30, 30, -1, 2, "g", 'P', 0, 0),
            new SpellEffectDescriptor(4, SpellEffectKind.Teleport, 0, 0, -1, 0, "C", 'P', 0, 0),
            new SpellEffectDescriptor(111, SpellEffectKind.Buff, 2, 2, -1, 0, "g", 'P', 0, 0),
            new SpellEffectDescriptor(128, SpellEffectKind.Buff, 2, 2, -1, 0, "g", 'P', 0, 0),
            new SpellEffectDescriptor(120, SpellEffectKind.Buff, 2, 2, -1, 0, "g", 'P', 0, 0),
            new SpellEffectDescriptor(118, SpellEffectKind.Buff, 100, 100, -1, 0, "g", 'P', 0, 0),
            new SpellEffectDescriptor(138, SpellEffectKind.Buff, 100, 100, -1, 0, "g", 'P', 0, 0),
        };
        var learned = effects.Select((effect, i) => new LearnedSpellInfo(40 + i, 3, 0,
            new SpellCastDescriptor(40 + i, 1, 3, 0, 6, 0, false, i + 1, 1, 0, 0, "",
                ImmutableArray.Create(effect))));

        var rotation = AutoSpellRotation.Build(new SpellConfig[0], learned,
            new(AutoCombatElements.Fire, RequireDescriptors: true));

        Assert.Equal(new[] { 40, 41, 42, 43, 44, 45, 46, 47 }, rotation.Select(s => s.SpellId));
        Assert.True(rotation[0].OnMe);
        Assert.True(rotation[0].SelfBuff);
        Assert.False(rotation[1].OnMe);
        Assert.True(rotation[1].SelfBuff);
        Assert.Equal(SpellTargetKind.Ally, rotation[1].TargetKind);
        Assert.False(rotation[2].OnMe);
        Assert.False(rotation[2].SelfBuff);
        Assert.All(rotation.Skip(3), config =>
        {
            Assert.Equal(SpellTargetKind.Ally, config.TargetKind);
            Assert.True(config.SelfBuff);
            Assert.False(config.OnMe);
        });
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, rotation.Select(s => s.PerTurn));
    }

    [Theory]
    [InlineData(30, false, 3, 1)]
    [InlineData(31, false, 2, 1)]
    [InlineData(30, true, 1, 1)]
    [InlineData(30, false, 3, 2)]
    [InlineData(31, false, 2, 2)]
    public void Automatic_cast_ceiling_allows_a_learned_extra_cast_modifier(int modifiedSpell, bool configured, int expected, int used)
    {
        var attack = new SpellCastDescriptor(30, 1, 3, 1, 6, 0, false, 2, 0, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(100, SpellEffectKind.Damage, 100, 100, 0, 0,
                "A", 'P', 0, 0)));
        var boost = new SpellCastDescriptor(20, 1, 1, 0, 0, 0, false, 1, 0, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(290, SpellEffectKind.Buff, 0, 0, 0, 1,
                "C", 'P', 0, 0, DiceNum: modifiedSpell, Value: 1)));

        var profile = configured ? new[] { new SpellConfig { SpellId = 30, Enabled = true, PerTurn = 1 } } : new SpellConfig[0];
        var rotation = AutoSpellRotation.Build(profile, new[] {
            new LearnedSpellInfo(30, 3, 100, attack), new LearnedSpellInfo(20, 1, 0, boost)
        }, new(AutoCombatElements.Automatic, RequireDescriptors: true));

        Assert.Equal(expected, rotation.Single(s => s.SpellId == 30).PerTurn);

        int caster = GivreClone.Protocol.MapPathfinder.CellFromMapPoint(8, 8);
        int enemy = GivreClone.Protocol.MapPathfinder.CellFromMapPoint(10, 8);
        var spells = rotation.Select(s => new PlannerSpell(s.SpellId == 30 ? attack : boost,
            s.SpellId == 30 ? Math.Max(0, 2 - used) : 1, 0, ImmutableDictionary<long, int>.Empty,
            ImmutableHashSet.Create(s.SpellId == 30 ? 2L : 1L),
            CastsLimit: s.SpellId == 30 ? s.PerTurn - used : s.PerTurn)).ToImmutableArray();
        var snapshot = new CombatTurnSnapshot(
            ImmutableArray.Create(new PlannerCell(caster, true, false, ImmutableArray<int>.Empty),
                new PlannerCell(enemy, true, false, ImmutableArray<int>.Empty)),
            ImmutableArray.Create(new PlannerFighter(1, caster, false, 1000, 1000, States: ImmutableHashSet<int>.Empty),
                new PlannerFighter(2, enemy, true, 1000, 1000, MovePoints: 0, ExpectedDamage: 0,
                    ThreatKnown: true, States: ImmutableHashSet<int>.Empty)),
            1, 10, 0, spells, ImmutableDictionary<int, int>.Empty, MaxCasts: 4);

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(Math.Max(0, expected - used), plan.Actions.Count(a => a.SpellId == 30));
        Assert.Equal(expected == 3, plan.Actions.Any(a => a.SpellId == 20));
    }

    [Fact]
    public void Disabled_support_and_conditional_class_effects_are_not_automatically_reintroduced()
    {
        var heal = new SpellEffectDescriptor(108, SpellEffectKind.Heal, 20, 20, -1, 0, "C", 'P', 0, 0);
        var learned = new[]
        {
            new LearnedSpellInfo(40, 3, 0, new(40, 1, 3, 0, 6, 0, false, 1, 1, 0, 0, "", ImmutableArray.Create(heal))),
            new LearnedSpellInfo(41, 3, 0, new(41, 1, 3, 0, 6, 0, false, 1, 1, 0, 0, "",
                ImmutableArray.Create(heal with { Conditional = true }))),
            new LearnedSpellInfo(42, 3, 0, new(42, 1, 3, 0, 6, 0, false, 1, 1, 0, 0, "state-required",
                ImmutableArray.Create(heal))),
        };

        var rotation = AutoSpellRotation.Build(new[] { S(40, 1, false) }, learned,
            new(RequireDescriptors: true));

        Assert.Empty(rotation);
    }

    [Fact]
    public void Learned_life_steal_reaches_the_planner_through_generated_rotation()
    {
        var effect = new SpellEffectDescriptor(95, SpellEffectKind.Damage, 40, 40, 2, 0, "A", 'P', 0, 0);
        var descriptor = new SpellCastDescriptor(70, 1, 3, 0, 6, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(effect));
        var rotation = AutoSpellRotation.Build(new SpellConfig[0],
            new[] { new LearnedSpellInfo(70, 3, 40, descriptor) }, new(RequireDescriptors: true));
        var spells = rotation.Select(s => new PlannerSpell(descriptor, s.PerTurn, 1,
            ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(2L))).ToImmutableArray();
        int caster = GivreClone.Protocol.MapPathfinder.CellFromMapPoint(8, 8);
        int enemy = GivreClone.Protocol.MapPathfinder.CellFromMapPoint(10, 8);
        var snapshot = new CombatTurnSnapshot(
            ImmutableArray.Create(new PlannerCell(caster, true, false, ImmutableArray<int>.Empty),
                new PlannerCell(enemy, true, false, ImmutableArray<int>.Empty)),
            ImmutableArray.Create(new PlannerFighter(1, caster, false, 20, 100, States: ImmutableHashSet<int>.Empty),
                new PlannerFighter(2, enemy, true, 100, 100, MovePoints: 0, AttackRange: 0, ExpectedDamage: 0, ThreatKnown: true)),
            1, 3, 0, spells, ImmutableDictionary<int, int>.Empty);

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(70, Assert.Single(plan.Actions).SpellId);
        Assert.Equal(40, plan.Score.Damage);
        Assert.True(plan.Score.Healing > 0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Learned_ap_boost_enables_an_unaffordable_attack_only_when_not_disabled(bool enabled)
    {
        var boost = new SpellCastDescriptor(70, 1, 1, 0, 0, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(111, SpellEffectKind.Buff,
                2, 2, -1, 0, "C", 'P', 0, 0)));
        var attack = Damage(71, 2).Descriptor!;
        var rotation = AutoSpellRotation.Build(enabled ? new SpellConfig[0] : new[] { S(70, 1, false) },
            new[] { new LearnedSpellInfo(70, 1, 0, boost), new LearnedSpellInfo(71, 3, 10, attack) },
            new(RequireDescriptors: true));
        var spells = rotation.Select(s => new PlannerSpell(s.SpellId == 70 ? boost : attack,
            s.PerTurn, 1, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L, 2L)))
            .ToImmutableArray();
        int caster = GivreClone.Protocol.MapPathfinder.CellFromMapPoint(8, 8);
        int enemy = GivreClone.Protocol.MapPathfinder.CellFromMapPoint(10, 8);
        var snapshot = new CombatTurnSnapshot(
            ImmutableArray.Create(new PlannerCell(caster, true, false, ImmutableArray<int>.Empty),
                new PlannerCell(enemy, true, false, ImmutableArray<int>.Empty)),
            ImmutableArray.Create(new PlannerFighter(1, caster, false, 100, 100),
                new PlannerFighter(2, enemy, true, 100, 100, MovePoints: 0, AttackRange: 0,
                    ExpectedDamage: 0, ThreatKnown: true)),
            1, 2, 0, spells, ImmutableDictionary<int, int>.Empty);

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(enabled ? new[] { 70, 71 } : new int[0], plan.Actions.Select(a => a.SpellId));
        Assert.Equal(enabled ? 10 : 0, plan.Score.Damage);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(true, 8)]
    [InlineData(false, 8)]
    public void Learned_mp_boost_opens_a_walk_then_attack_from_zero_mp(bool enabled, int alternatives)
    {
        var boost = new SpellCastDescriptor(70, 1, 1, 0, 0, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(128, SpellEffectKind.Buff,
                2, 2, -1, 0, "C", 'P', 0, 0)));
        var attack = Damage(71, 2).Descriptor! with { MinRange = 1, Range = 1 };
        var weak = Enumerable.Range(100, alternatives).Select(id => Damage(id, 2).Descriptor! with
        {
            Effects = ImmutableArray.Create(new SpellEffectDescriptor(100, SpellEffectKind.Damage,
                5, 5, 2, 0, "A", 'P', 0, 0))
        }).ToArray();
        var rotation = AutoSpellRotation.Build(enabled ? new SpellConfig[0] : new[] { S(70, 1, false) },
            new[] { new LearnedSpellInfo(70, 1, 0, boost), new LearnedSpellInfo(71, 3, 10, attack) }
                .Concat(weak.Select(d => new LearnedSpellInfo(d.SpellId, 3, 5, d))),
            new(RequireDescriptors: true));
        var spells = rotation.Select(s => new PlannerSpell(s.SpellId == 70 ? boost : s.SpellId == 71 ? attack : weak.Single(d => d.SpellId == s.SpellId),
            s.PerTurn, 1, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L, 2L)))
            .ToImmutableArray();
        int Cell(int step) => GivreClone.Protocol.MapPathfinder.CellFromMapPoint(8 + step, 8);
        var cells = Enumerable.Range(0, 4).Select(i => new PlannerCell(Cell(i), true, false,
            Enumerable.Range(0, 4).Where(j => System.Math.Abs(i - j) == 1)
                .Select(Cell).ToImmutableArray())).ToImmutableArray();
        var snapshot = new CombatTurnSnapshot(cells,
            ImmutableArray.Create(new PlannerFighter(1, Cell(0), false, 100, 100),
                new PlannerFighter(2, Cell(3), true, 100, 100, MovePoints: 0, AttackRange: 0,
                    ExpectedDamage: 0, ThreatKnown: true)),
            1, 4, 0, spells, ImmutableDictionary<int, int>.Empty);

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(enabled ? new[] { 70, 71 } : alternatives > 0 ? new[] { 100 } : new int[0],
            plan.Actions.Where(a => a.Kind == PlannerActionKind.Cast).Select(a => a.SpellId));
        Assert.Equal(enabled ? new[] { Cell(1), Cell(2) } : new int[0],
            plan.Actions.Where(a => a.Kind == PlannerActionKind.Move).Select(a => a.Cell));
        Assert.Equal(enabled ? 10 : alternatives > 0 ? 5 : 0, plan.Score.Damage);
    }

    [Theory]
    [InlineData(111, "C", false, 2, 0)]
    [InlineData(128, "C", false, 0, 2)]
    [InlineData(111, "a", true, 2, 0)]
    [InlineData(111, "a", false, 0, 0)]
    [InlineData(111, "A", false, 0, 0)]
    public void Executed_self_point_gain_remains_spendable_only_for_the_caster(int action, string mask, bool targetedSelf, int ap, int mp)
    {
        var spell = new SpellCastDescriptor(70, 1, 1, 0, 0, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(action, SpellEffectKind.Buff, 2, 2, -1, 0, mask, 'P', 0, 0)));

        Assert.Equal((ap, mp), CombatTurnPlanner.ExpectedSelfPointGain(spell, targetedSelf));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 0)]
    public void Area_point_gain_counts_the_caster_only_inside_the_effect_zone(int distance, int mp)
    {
        int Cell(int step) => GivreClone.Protocol.MapPathfinder.CellFromMapPoint(8 + step, 8);
        var spell = new SpellCastDescriptor(4242, 1, 2, 0, 2, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(128, SpellEffectKind.Buff, 1, 1, -1, 2, "a", 'C', 2, 0, Triggers: "I")));

        Assert.Equal((0, mp), CombatTurnPlanner.ExpectedSelfPointGain(spell, false, (Cell(0), Cell(distance))));
    }

    [Theory]
    [InlineData(1160, true, 0, 0, "C", 0, 2)]
    [InlineData(792, true, 0, 0, "C", 0, 0)]
    [InlineData(1160, false, 0, 0, "C", 0, 0)]
    [InlineData(1160, true, 1, 0, "C", 0, 0)]
    [InlineData(1160, true, 0, 1, "C", 0, 0)]
    [InlineData(1160, true, 0, 0, "unknown", 0, 0)]
    [InlineData(1160, true, 0, 0, "C", 1, 1)]
    public void Executed_subcast_point_gain_respects_recipient_and_immediate_resolution(
        int action, bool childKnown, int delay, int random, string mask, int mpCost, int expectedMp)
    {
        int caster = GivreClone.Protocol.MapPathfinder.CellFromMapPoint(8, 8);
        var child = new SpellCastDescriptor(71, 2, 0, 0, 0, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(128, SpellEffectKind.Buff,
                2, 2, -1, 0, mask, 'P', 0, 0, Delay: delay, Random: random)));
        if (mpCost > 0) child = child with { Effects = child.Effects.Add(
            new SpellEffectDescriptor(1080, SpellEffectKind.Debuff, mpCost, mpCost, -1, 0, "C", 'P', 0, 0)) };
        var spell = child with { SpellId = 70, Effects = ImmutableArray.Create(
            new SpellEffectDescriptor(action, SpellEffectKind.Unknown, 71, 71, -1, 0, "a", 'P', 0, 0,
                DiceNum: 71, DiceSide: 2)) };
        int ally = GivreClone.Protocol.MapPathfinder.CellFromMapPoint(9, 8);
        SpellCastDescriptor? Lookup(int id, int grade) => childKnown && id == 71 && grade == 2 ? child : null;

        var snapshot = new CombatTurnSnapshot(
            ImmutableArray.Create(new PlannerCell(caster, true, false, ImmutableArray<int>.Empty),
                new PlannerCell(ally, true, false, ImmutableArray<int>.Empty)),
            ImmutableArray.Create(new PlannerFighter(17, caster, false, 100, 100),
                new PlannerFighter(18, ally, false, 100, 100)),
            17, 4, 0, ImmutableArray<PlannerSpell>.Empty, ImmutableDictionary<int, int>.Empty,
            SpellLookup: Lookup, CasterLevel: 100);
        var gain = CombatTurnPlanner.ExpectedSelfPointGain(snapshot, spell, ally);

        Assert.Equal((0, expectedMp), gain);
        Assert.Equal(expectedMp, FinishingMovePolicy.RemainingMp(gain.Mp, 0, 0, 2, gain.Mp));
    }

    [Theory]
    [InlineData(111, 12, "a,E97")]
    [InlineData(128, 6, "a,E97")]
    [InlineData(1079, 16, "A")]
    [InlineData(1079, 16, "H")]
    [InlineData(1080, 10, "H")]
    [InlineData(168, 16, "L")]
    [InlineData(169, 10, "L")]
    [InlineData(1080, 10, "A")]
    [InlineData(168, 16, "A")]
    [InlineData(169, 10, "A")]
    [InlineData(1079, 16, "A,E97")]
    [InlineData(1080, 10, "A,E97")]
    [InlineData(168, 16, "A,E97")]
    [InlineData(169, 10, "A,E97")]
    [InlineData(1079, 16, "A,E97", false)]
    public void Learned_point_change_reaches_the_planner_as_recipient_control(int action, int expectedControl, string mask, bool casterStatesKnown = true)
    {
        var descriptor = new SpellCastDescriptor(70, 1, 3, 1, 6, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(action, SpellEffectDescriptor.Classify(action),
                2, 2, -1, 0, mask, 'P', 0, 0)));
        var rotation = AutoSpellRotation.Build(new SpellConfig[0],
            new[] { new LearnedSpellInfo(70, 3, 0, descriptor) }, new(RequireDescriptors: true));
        var spells = rotation.Select(s => new PlannerSpell(descriptor, s.PerTurn, 1,
            ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(2L))).ToImmutableArray();
        int caster = GivreClone.Protocol.MapPathfinder.CellFromMapPoint(8, 8);
        int enemy = GivreClone.Protocol.MapPathfinder.CellFromMapPoint(10, 8);
        var snapshot = new CombatTurnSnapshot(
            ImmutableArray.Create(new PlannerCell(caster, true, false, ImmutableArray<int>.Empty),
                new PlannerCell(enemy, true, false, ImmutableArray<int>.Empty)),
            ImmutableArray.Create(new PlannerFighter(1, caster, false, 100, 100,
                    States: casterStatesKnown ? ImmutableHashSet<int>.Empty : null, StatesKnown: casterStatesKnown),
                new PlannerFighter(2, enemy, action is not (111 or 128), 100, 100, MovePoints: 2, AttackRange: 0,
                    ExpectedDamage: 0, ThreatKnown: true, States: ImmutableHashSet.Create(97), StatesKnown: true)),
            1, 3, 0, spells, ImmutableDictionary<int, int>.Empty);

        Assert.True(AutoSpellRotation.ShouldUsePlanner(rotation.Select(_ => descriptor)));
        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(70, Assert.Single(plan.Actions).SpellId);
        Assert.Equal(expectedControl, plan.Score.Control);
        Assert.Equal(0, plan.Score.Damage);
    }

    [Theory]
    [InlineData(5, new[] { 70, 71 }, 20, 0, 118, 100)]
    [InlineData(2, new[] { 71 }, 10, 0, 118, 100)]
    [InlineData(5, new[] { 70, 71 }, 20, 8, 118, 100)]
    [InlineData(2, new[] { 71 }, 10, 8, 118, 100)]
    [InlineData(2, new[] { 70, 71 }, 20, 8, 126, 100)]
    [InlineData(5, new[] { 71 }, 10, 8, 126, 100)]
    [InlineData(2, new[] { 70, 71 }, 20, 8, 112, 10)]
    public void Learned_stat_boost_is_used_only_when_it_improves_the_attack(
        int element, int[] expectedSpells, int expectedDamage, int alternatives, int action, int amount)
    {
        var boost = new SpellCastDescriptor(70, 1, 1, 0, 0, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(action, SpellEffectDescriptor.Classify(action),
                amount, amount, -1, 0, "C", 'P', 0, 0)));
        var attack = Damage(71, element).Descriptor!;
        attack = attack with { Effects = attack.Effects.SetItem(0,
            attack.Effects[0] with { ActionId = element == 5 ? 97 : 99 }) };
        var learned = new[] { new LearnedSpellInfo(70, 1, 0, boost), new LearnedSpellInfo(71, 3, 10, attack) }
            .Concat(Enumerable.Range(0, alternatives).Select(i =>
                new LearnedSpellInfo(100 + i, 3, 1, attack with { SpellId = 100 + i,
                    Effects = attack.Effects.SetItem(0, attack.Effects[0] with { Min = 1, Max = 1 }) }))).ToArray();
        var rotation = AutoSpellRotation.Build(new SpellConfig[0], learned,
            new(RequireDescriptors: true));
        var spells = rotation.Select(s => new PlannerSpell(learned.Single(l => l.SpellId == s.SpellId).Descriptor!,
            s.PerTurn, 1, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L, 2L)))
            .ToImmutableArray();
        int caster = GivreClone.Protocol.MapPathfinder.CellFromMapPoint(8, 8);
        int enemy = GivreClone.Protocol.MapPathfinder.CellFromMapPoint(10, 8);
        var snapshot = new CombatTurnSnapshot(
            ImmutableArray.Create(new PlannerCell(caster, true, false, ImmutableArray<int>.Empty),
                new PlannerCell(enemy, true, false, ImmutableArray<int>.Empty)),
            ImmutableArray.Create(new PlannerFighter(1, caster, false, 100, 100),
                new PlannerFighter(2, enemy, true, 100, 100, MovePoints: 0, AttackRange: 0,
                    ExpectedDamage: 0, ThreatKnown: true)),
            1, 4, 0, spells, ImmutableDictionary<int, int>.Empty);

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(expectedSpells, plan.Actions.Select(a => a.SpellId));
        Assert.Equal(expectedDamage, plan.Score.Damage);
    }

    [Theory]
    [InlineData(100, SpellEffectKind.Damage, "A", 'C', 1)]
    [InlineData(168, SpellEffectKind.Debuff, "A", 'P', 0)]
    [InlineData(111, SpellEffectKind.Buff, "C", 'P', 0)]
    [InlineData(118, SpellEffectKind.Buff, "C", 'P', 0)]
    public void Planner_rotation_includes_immediate_resolver_effects(int action, SpellEffectKind kind, string mask, char shape, int size)
    {
        var descriptor = new SpellCastDescriptor(70, 1, 3, 0, 6, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(action, kind, 10, 10, 2, 0, mask, shape, size, 0)));

        var rotation = AutoSpellRotation.Build(new SpellConfig[0],
            new[] { new LearnedSpellInfo(70, 3, 0, descriptor) }, new(RequireDescriptors: true));

        Assert.Equal(70, Assert.Single(rotation).SpellId);
    }

    [Theory]
    [InlineData(1, 0, "", "A")]
    [InlineData(0, 1, "", "A")]
    [InlineData(0, 0, "turn-start", "A")]
    [InlineData(0, 0, "", "unrecognized-mask")]
    public void Unresolved_or_deferred_damage_does_not_enter_generated_rotation(int random, int delay, string triggers, string mask)
    {
        var descriptor = new SpellCastDescriptor(70, 1, 3, 0, 6, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(100, SpellEffectKind.Damage, 10, 10, 2, 0, mask, 'P', 0, 0,
                Random: random, Delay: delay, Triggers: triggers)));

        var rotation = AutoSpellRotation.Build(new SpellConfig[0],
            new[] { new LearnedSpellInfo(70, 3, 10, descriptor) }, new(RequireDescriptors: true));

        Assert.Empty(rotation);
    }

    [Fact]
    public void Planner_selection_requires_descriptors_even_when_aggregate_damage_is_positive()
    {
        var rotation = AutoSpellRotation.Build(new SpellConfig[0],
            new[] { new LearnedSpellInfo(20, 3, 100) }, new(RequireDescriptors: true));

        Assert.Empty(rotation);
    }

    [Fact]
    public void Disabled_script_sentinel_does_not_overflow_generated_priorities()
    {
        var rotation = AutoSpellRotation.Build(new[] { S(10, int.MaxValue, false) },
            new[] { Damage(20, 2), Damage(21, 5) });

        Assert.Equal(new[] { int.MaxValue, int.MaxValue }, rotation.Select(s => s.Priority));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Explicit_point_zone_remains_single_target_regardless_of_radius(int size)
    {
        var effect = new SpellEffectDescriptor(100, SpellEffectKind.Damage,
            10, 10, 2, 0, "A", 'P', size, 0);

        Assert.True(effect.IsCertain);
    }
}
