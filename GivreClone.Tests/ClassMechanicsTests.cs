extern alias F06App;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public sealed class ClassMechanicsTests
{
    [Fact]
    public void Rogue_BombWall_Formed_When_Aligned_Within_Range()
    {
        var cells = ClassMechanics.GetBombWallCells(200, 254, _ => true);
        Assert.False(cells.IsEmpty);
        Assert.Equal(3, cells.Length);
    }

    [Fact]
    public void Rogue_BombWall_Empty_When_Not_Aligned()
    {
        var cells = ClassMechanics.GetBombWallCells(200, 215, _ => true);
        Assert.True(cells.IsEmpty);
    }

    [Fact]
    public void Xelor_GeneratesTelefrag_When_Destination_Occupied()
    {
        bool telefrag = ClassMechanics.GeneratesTelefrag(150, 155, 155, isOccupied: true);
        Assert.True(telefrag);

        bool noTelefrag = ClassMechanics.GeneratesTelefrag(150, 155, 155, isOccupied: false);
        Assert.False(noTelefrag);
    }

    [Fact]
    public void Eliotrope_projection_copies_caster_to_entry_offset_from_the_exit_portal()
    {
        // Regle du jeu : le sort ressort du portail de sortie avec le meme decalage que lanceur -> portail d'entree.
        int caster = MapPathfinder.CellFromMapPoint(10, 0), entry = MapPathfinder.CellFromMapPoint(12, 0);
        int exit = MapPathfinder.CellFromMapPoint(10, 9), expected = MapPathfinder.CellFromMapPoint(12, 9);
        var shot = ClassMechanics.ProjectThroughPortals(caster, entry, new[] { entry, exit });
        Assert.NotNull(shot);
        Assert.Equal(expected, shot!.Value.Impact);
        Assert.Equal(exit, shot.Value.Exit);
        Assert.Equal(MapPathfinder.CellDistance(entry, exit), shot.Value.NetworkDistance);
    }

    [Fact]
    public void Eliotrope_projection_routes_to_nearest_unused_portal_and_refuses_ties()
    {
        int caster = MapPathfinder.CellFromMapPoint(10, 0);
        int a = MapPathfinder.CellFromMapPoint(12, 0), b = MapPathfinder.CellFromMapPoint(12, 3), c = MapPathfinder.CellFromMapPoint(12, 10);
        // A -> B (le plus proche) -> C : C est la sortie.
        Assert.Equal(c, ClassMechanics.ProjectThroughPortals(caster, a, new[] { a, c, b })!.Value.Exit);
        // Deux portails a egale distance de l'entree : sortie incertaine (regle horaire non modelisee) -> aucun tir.
        int left = MapPathfinder.CellFromMapPoint(12, -3);
        Assert.Null(ClassMechanics.ProjectThroughPortals(caster, a, new[] { a, b, left }));
    }

    [Fact]
    public void Eliotrope_planner_aims_the_entry_portal_to_hit_an_enemy_out_of_direct_range()
    {
        int caster = MapPathfinder.CellFromMapPoint(10, 0), entry = MapPathfinder.CellFromMapPoint(12, 0);
        int exit = MapPathfinder.CellFromMapPoint(10, 9), enemy = MapPathfinder.CellFromMapPoint(12, 9);
        var spellDesc = new SpellCastDescriptor(14579, 1, 3, 1, 5, 512, false, 0, 0, 1, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(99, SpellEffectKind.Damage, 30, 35, 1, 0, "A", 'P', 1, 0, DiceNum: 30, DiceSide: 35, Triggers: "I")));
        var plannerSpell = new PlannerSpell(spellDesc, 1, 1, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L, 2L));
        var plannerCells = new[] { caster, entry, exit, enemy }
            .Select(c => new PlannerCell(c, true, false, ImmutableArray<int>.Empty)).ToImmutableArray();
        var fighters = ImmutableArray.Create(
            new PlannerFighter(1, caster, false, 1000, 1000, Breed: ClassMechanics.EliotropeBreedId, States: ImmutableHashSet<int>.Empty),
            new PlannerFighter(2, enemy, true, 500, 500, States: ImmutableHashSet<int>.Empty));
        var snapshot = new CombatTurnSnapshot(plannerCells, fighters, 1, 3, 0,
            ImmutableArray.Create(plannerSpell), ImmutableDictionary<int, int>.Empty,
            UnsafeForcedMovementCells: ImmutableHashSet<int>.Empty,
            Portals: ImmutableArray.Create(new ClassMechanics.Portal(1, entry, 1), new ClassMechanics.Portal(2, exit, 1)));
        var plan = CombatTurnPlanner.Choose(snapshot);
        var cast = Assert.Single(plan.Actions, a => a.Kind == PlannerActionKind.Cast);
        Assert.Equal(entry, cast.Cell);          // case envoyee au serveur : le portail d'entree
        Assert.Equal(2L, cast.Target);
        Assert.True(plan.Score.Damage > 0);

        // Un obstacle entre la SORTIE et l'ennemi : le serveur refuse (174), le planificateur ne doit plus proposer ce tir.
        var los = spellDesc with { Flags = spellDesc.Flags | 4 };
        int wall = MapPathfinder.CellFromMapPoint(11, 9);
        var walled = snapshot with
        {
            Spells = ImmutableArray.Create(plannerSpell with { Cast = los }),
            Cells = plannerCells.Add(new PlannerCell(wall, false, true, ImmutableArray<int>.Empty)),
        };
        Assert.DoesNotContain(CombatTurnPlanner.Choose(walled).Actions, a => a.Kind == PlannerActionKind.Cast);
    }

    [Fact]
    public void Eliotrope_Portal_DamageMultiplier_Scales_With_Distance()
    {
        double mult = ClassMechanics.PortalDamageMultiplier(10);
        Assert.True(mult > 1.15 && mult <= 1.50);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Eliotrope_builds_a_new_two_portal_network_before_attacking_out_of_range(bool realSpells)
    {
        int caster = MapPathfinder.CellFromMapPoint(10, 0);
        int enemy = MapPathfinder.CellFromMapPoint(realSpells ? 18 : 16, 0);
        var cells = Enumerable.Range(10, 9).SelectMany(x => Enumerable.Range(0, 5)
            .Select(y => MapPathfinder.CellFromMapPoint(x, y)))
            .Select(c => new PlannerCell(c, true, false, ImmutableArray<int>.Empty)).ToImmutableArray();
        var portal = new SpellCastDescriptor(14574, 3, 1, 1, 6, 8, false, 2, 0, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(1181, SpellEffectKind.Unknown, 0, 0, 0, -1, "C", 'P', 0, 0)));
        var attack = new SpellCastDescriptor(14579, 1, 3, 1, 3, 512, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(99, SpellEffectKind.Damage, 30, 35, 1, 0, "A", 'P', 0, 0,
                DiceNum: 30, DiceSide: 35, Triggers: "I")));
        if (realSpells)
        {
            var names = new F06App::GivreClone.App.Services.GameNameResolver();
            names.EnsureLoaded(); names.EnsureCombatLoaded();
            SpellCastDescriptor Load(int id, int grade)
            {
                var descriptor = names.GetSpellDescriptor(id, grade);
                Assert.NotNull(descriptor);
                return System.Text.Json.JsonSerializer.Deserialize<SpellCastDescriptor>(
                    System.Text.Json.JsonSerializer.Serialize(descriptor))!;
            }
            portal = Load(14574, 3);
            attack = Load(14579, 3);
        }
        var fighters = ImmutableArray.Create(
            new PlannerFighter(1, caster, false, 1000, 1000, Breed: ClassMechanics.EliotropeBreedId,
                States: ImmutableHashSet<int>.Empty),
            new PlannerFighter(2, enemy, true, 500, 500, MovePoints: 0, AttackRange: 0, ExpectedDamage: 0,
                ThreatKnown: true, States: ImmutableHashSet<int>.Empty));
        var spells = ImmutableArray.Create(
            new PlannerSpell(portal, 2, 0, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L, 2L)),
            new PlannerSpell(attack, 1, 1, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(2L)));
        int comboAp = portal.Ap * 2 + attack.Ap;
        var snapshot = new CombatTurnSnapshot(cells, fighters, 1, comboAp, 0, spells,
            ImmutableDictionary<int, int>.Empty, UnsafeForcedMovementCells: ImmutableHashSet<int>.Empty,
            Portals: ImmutableArray<ClassMechanics.Portal>.Empty);

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(new[] { 14574, 14574, 14579 }, plan.Actions
            .Where(a => a.Kind == PlannerActionKind.Cast).Select(a => a.SpellId));
        var entries = plan.Actions.Where(a => a.SpellId == 14574).Select(a => a.Cell).ToArray();
        var shot = plan.Actions.Last(a => a.Kind == PlannerActionKind.Cast);
        Assert.Contains(shot.Cell, entries);
        var projection = ClassMechanics.ProjectThroughPortals(caster, shot.Cell, entries)!.Value;
        Assert.True(shot.ViaPortal);
        if (realSpells)
        {
            // Rayon hits along a line from the exit: its center need not contain the enemy.
            var damageEffect = Assert.Single(attack.Effects, e => e.ActionId == 99);
            Assert.Contains(enemy, CombatZones.GetCells(damageEffect.Shape, projection.Impact,
                CombatZones.OrientationTo(projection.Exit, projection.Impact), damageEffect.MinSize, damageEffect.Size));
        }
        else Assert.Equal(enemy, projection.Impact);
        Assert.True(plan.Score.Damage > 0);

        var insufficientAp = CombatTurnPlanner.Choose(snapshot with { Ap = comboAp - 1 });
        Assert.DoesNotContain(insufficientAp.Actions, a => a.SpellId == 14579);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Eliotrope_lays_an_escape_portal_far_from_a_threat_it_cannot_kill(bool threatened, bool expectPortal)
    {
        // Fleeing Eliotrope next to an enemy it cannot kill this turn: an existing portal
        // beside it plus a new one far away gives a safe exit next turn. With no threat,
        // the same portal buys nothing and must not be laid.
        int caster = MapPathfinder.CellFromMapPoint(10, 0);
        int nearPortal = MapPathfinder.CellFromMapPoint(11, 0);
        int enemy = MapPathfinder.CellFromMapPoint(9, 0);
        var cells = Enumerable.Range(4, 15).SelectMany(x => Enumerable.Range(0, 5)
            .Select(y => MapPathfinder.CellFromMapPoint(x, y)))
            .Select(c => new PlannerCell(c, true, false, ImmutableArray<int>.Empty)).ToImmutableArray();
        var portal = new SpellCastDescriptor(14574, 3, 2, 1, 6, 8, false, 2, 0, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(1181, SpellEffectKind.Unknown, 0, 0, 0, -1, "C", 'P', 0, 0)));
        var fighters = ImmutableArray.Create(
            new PlannerFighter(1, caster, false, 1000, 1000, Breed: ClassMechanics.EliotropeBreedId,
                States: ImmutableHashSet<int>.Empty),
            new PlannerFighter(2, enemy, true, 5000, 5000, MovePoints: 1, AttackRange: 1,
                ExpectedDamage: threatened ? 400 : 0, ThreatKnown: true, States: ImmutableHashSet<int>.Empty));
        var snapshot = new CombatTurnSnapshot(cells, fighters, 1, portal.Ap, 0,
            ImmutableArray.Create(new PlannerSpell(portal, 2, 0, ImmutableDictionary<long, int>.Empty,
                ImmutableHashSet.Create(1L, 2L))),
            ImmutableDictionary<int, int>.Empty, Style: GivreClone.App.Services.CombatStyle.Fleeing,
            UnsafeForcedMovementCells: ImmutableHashSet<int>.Empty,
            Portals: ImmutableArray.Create(new ClassMechanics.Portal(1, nearPortal, 1, Active: false)));

        var plan = CombatTurnPlanner.Choose(snapshot);

        var laid = plan.Actions.Where(a => a.SpellId == 14574).ToArray();
        Assert.Equal(expectPortal, laid.Length == 1);
        if (expectPortal)
            Assert.True(MapPathfinder.CellDistance(laid[0].Cell, enemy) > 2,
                "the escape portal must land out of the enemy's reach");
    }

    [Fact]
    public void Eliotrope_CombatTurnPlanner_Never_Targets_Out_Of_Range_Enemy_Cell_Even_With_Portals()
    {
        // Le serveur controle la case visee comme un tir direct : viser la case de l'ennemi a distance 11 avec un sort
        // 1-5 est refuse (message 171), portails ou non (21 refus en jeu le 09/11). Le planificateur ne doit donc pas
        // produire ce tir ; le vrai tir par portail (viser le portail d'entree) n'est pas encore modelise.
        int casterCell = 198;
        int portal1Cell = 200; // dist 4 du lanceur (<= 5)
        int portal2Cell = 350; // dist 4 de la cible (<= 5)
        int targetCell = 352;  // dist 11 du lanceur (hors portée directe 5 !)

        var spellDesc = new SpellCastDescriptor(14575, 1, 3, 1, 5, 512, false, 0, 0, 1, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(96, SpellEffectKind.Damage, 30, 35, 1, 0, "A", 'P', 1, 0, DiceNum: 30, DiceSide: 35, Triggers: "I")));
        var plannerSpell = new PlannerSpell(spellDesc, 1, 1, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L, 2L));

        var cells = new[] { casterCell, portal1Cell, portal2Cell, targetCell };
        var plannerCells = cells.Select(c => new PlannerCell(c, true, false, ImmutableArray<int>.Empty)).ToImmutableArray();
        var fighters = ImmutableArray.Create(
            new PlannerFighter(1, casterCell, false, 1000, 1000, Breed: ClassMechanics.EliotropeBreedId, States: ImmutableHashSet<int>.Empty),
            new PlannerFighter(2, targetCell, true, 500, 500, States: ImmutableHashSet<int>.Empty));

        var portals = ImmutableArray.Create(
            new ClassMechanics.Portal(1, portal1Cell, 1),
            new ClassMechanics.Portal(2, portal2Cell, 1));

        // 1. Sans portails : hors de portée directe (dist 11 > 5) -> aucune action d'attaque
        var snapWithoutPortals = new CombatTurnSnapshot(plannerCells, fighters, 1, 3, 0,
            ImmutableArray.Create(plannerSpell), ImmutableDictionary<int, int>.Empty,
            UnsafeForcedMovementCells: ImmutableHashSet<int>.Empty,
            Portals: ImmutableArray<ClassMechanics.Portal>.Empty);
        var planWithout = CombatTurnPlanner.Choose(snapWithoutPortals);
        Assert.DoesNotContain(planWithout.Actions, a => a.Kind == PlannerActionKind.Cast);

        // 2. Avec les portails actifs : toujours aucun tir sur la case hors de portee.
        var snapWithPortals = snapWithoutPortals with { Portals = portals };
        var planWith = CombatTurnPlanner.Choose(snapWithPortals);
        Assert.DoesNotContain(planWith.Actions, a => a.Kind == PlannerActionKind.Cast && a.Cell == targetCell);
    }

                                [Fact]
    public void Xelor_telefrag_swap_outranks_equal_damage_non_telefrag_for_xelor_only()
    {
        Assert.Equal(251, ClassMechanics.StateTelefrag);
        // Deux sorts a 3 PA et degats identiques (30) :
        // 1. nonSwapSpell (2000) : tir direct sans swap.
        // 2. swapSpell (2001) : frappe et echange (SpellEffectKind.Swap).
        int casterCell = MapPathfinder.CellFromMapPoint(10, 0);
        int enemyCell = MapPathfinder.CellFromMapPoint(11, 0);

        var plannerCells = new[] { casterCell, enemyCell }
            .Select(c => new PlannerCell(c, true, false, ImmutableArray<int>.Empty)).ToImmutableArray();

        // Etat Telefrag ennemi du catalogue officiel actuel.
        var enemy = new PlannerFighter(2, enemyCell, true, 500, 500, MovePoints: 0, AttackRange: 0, ExpectedDamage: 0, ThreatKnown: true,
            States: ImmutableHashSet.Create(ClassMechanics.StateTelefrag));

        var nonSwapDesc = new SpellCastDescriptor(2000, 1, 3, 1, 5, 512, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(99, SpellEffectKind.Damage, 30, 30, 1, 0, "A", 'P', 1, 0, DiceNum: 30, DiceSide: 30, Triggers: "I")));
        var nonSwapSpell = new PlannerSpell(nonSwapDesc, 1, 1, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(2L));

        var swapDesc = new SpellCastDescriptor(2001, 1, 3, 1, 5, 512, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(
                new SpellEffectDescriptor(99, SpellEffectKind.Damage, 30, 30, 1, 0, "A", 'P', 1, 0, DiceNum: 30, DiceSide: 30, Triggers: "I"),
                new SpellEffectDescriptor(8, SpellEffectKind.Swap, 0, 0, 1, 0, "A", 'P', 1, 0, Triggers: "I")));
        var swapSpell = new PlannerSpell(swapDesc, 1, 1, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(2L));

        // nonSwapSpell (2000 < 2001) est classe en premier dans snapshot.Spells -> vainqueur initial a score egal.
        var spells = ImmutableArray.Create(nonSwapSpell, swapSpell);

        // 1. Non-Xelor (Feca) : scores identiques, aucun bonus telefrag dans Rank -> nonSwapSpell (2000) reste choisi.
        var nonXelorCaster = new PlannerFighter(1, casterCell, false, 1000, 1000, MovePoints: 0, Breed: ClassMechanics.FecaBreedId, States: ImmutableHashSet<int>.Empty);
        var nonXelorSnap = new CombatTurnSnapshot(plannerCells, ImmutableArray.Create(nonXelorCaster, enemy), 1, 3, 0, spells, ImmutableDictionary<int, int>.Empty, Style: GivreClone.App.Services.CombatStyle.Fleeing, UnsafeForcedMovementCells: ImmutableHashSet<int>.Empty);
        var nonXelorPlan = CombatTurnPlanner.Choose(nonXelorSnap);
        Assert.Equal(2000, nonXelorPlan.Actions[0].SpellId);
        Assert.Equal(30, (int)nonXelorPlan.Score.Damage);

        // 2. Xelor : la cible ayant deja l etat telefrag, EvaluateXelorTelefrag(true, true) ajoute +30.0 dans Rank.
        // A scores egaux, le swap (2001) surclasse l action non-telefrag (2000).
        var xelorCaster = nonXelorCaster with { Breed = ClassMechanics.XelorBreedId };
        var xelorSnap = nonXelorSnap with { Fighters = ImmutableArray.Create(xelorCaster, enemy) };
        var xelorPlan = CombatTurnPlanner.Choose(xelorSnap);
        Assert.Equal(2001, xelorPlan.Actions[0].SpellId);
        Assert.Equal(30, (int)xelorPlan.Score.Damage);
    }

    [Theory]
    [InlineData("Feca", 0, ClassMechanics.FecaBreedId)]
    [InlineData("Osamodas", 1, ClassMechanics.OsamodasBreedId)]
    [InlineData("Xelor", 4, ClassMechanics.XelorBreedId)]
    [InlineData("Iop", 7, ClassMechanics.IopBreedId)]
    [InlineData("Sacrier", 10, ClassMechanics.SacrierBreedId)]
    [InlineData("Pandawa", 11, ClassMechanics.PandawaBreedId)]
    [InlineData("Rogue", 12, ClassMechanics.RogueBreedId)]
    [InlineData("Masqueraider", 13, ClassMechanics.MasqueraiderBreedId)]
    [InlineData("Eliotrope", 15, ClassMechanics.EliotropeBreedId)]
    [InlineData("Forgelance", 18, ClassMechanics.ForgelanceBreedId)]
    public void ClassMechanics_BreedIds_Match_ClassIconProvider_Mapping(string breedName, int breedIndex, int constantBreedId)
    {
        _ = breedName;
        int mappedId = F06App::GivreClone.App.Services.ClassIconProvider.BreedId(breedIndex);
        Assert.Equal(mappedId, constantBreedId);
    }
}
