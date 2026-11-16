using System.Collections.Immutable;
using System.Linq;
using GivreClone.App.Services;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public class PlannerSetupPotentialRegressionTests
{
    private static int Cell(int i) => MapPathfinder.CellFromMapPoint(8 + i, 8);

    [Fact]
    public void Zero_result_setup_cast_does_not_outrank_productive_approach_walking()
    {
        var cells = Enumerable.Range(0, 7).Select(i => new PlannerCell(Cell(i), true, false,
            Enumerable.Range(0, 7).Where(j => System.Math.Abs(i - j) == 1)
                .Select(Cell).ToImmutableArray())).ToImmutableArray();
        PlannerSpell Spell(int id, int action, string mask, int amount)
        {
            var effect = new SpellEffectDescriptor(action, SpellEffectDescriptor.Classify(action),
                amount, amount, 0, 0, mask, 'P', 0, 0);
            var cast = new SpellCastDescriptor(id, 1, 3, 0, 1, 0, false, 1, 1, 0, 0, "",
                ImmutableArray.Create(effect));
            return new(cast, 1, 1, ImmutableDictionary<long, int>.Empty,
                ImmutableHashSet.Create(1L, 2L, 3L));
        }
        var snapshot = new CombatTurnSnapshot(cells,
            ImmutableArray.Create(new PlannerFighter(1, Cell(0), false, 100, 100),
                new PlannerFighter(2, Cell(5), true, 500, 500, MovePoints: 1,
                    AttackRange: 1, ExpectedDamage: 0, ThreatKnown: true)),
            1, 6, 2, ImmutableArray.Create(Spell(10, 950, "C", 42), Spell(20, 100, "A", 40)),
            ImmutableDictionary<int, int>.Empty, Style: CombatStyle.Aggressive, MaxCasts: 2);

        var plan = CombatTurnPlanner.Choose(snapshot);

        // Distance5, MP2, range1: no attack now. State42 enables no follow-up.
        Assert.DoesNotContain(plan.Actions, a => a.SpellId == 10);
        Assert.Contains(plan.Actions, a => a.Kind == PlannerActionKind.Move);
    }
}
