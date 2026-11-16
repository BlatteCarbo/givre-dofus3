using System.Collections.Immutable;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public class SacrieurPlacementChainTests
{
    // SpellLevelData 40571/40574 from client 3.7.12.12, retained in
    // docs/cos-dofus-video/rapports-v2/N07/spelllevelsdata.3.7.12.12.json.
    private static SpellEffectDescriptor Effect(int action, int amount, string mask) =>
        new(action, SpellEffectDescriptor.Classify(action), amount, amount, -1, 0, mask, 'P', 1, 0,
            DiceNum: amount, Triggers: "I");

    private static readonly SpellCastDescriptor Transposition = new(12736, 1, 3, 1, 7, 512, false,
        0, 0, 3, 1, "HS!7", ImmutableArray.Create(Effect(8, 0, "a,A")), Uncertain: true);
    private static readonly SpellCastDescriptor Attirance = new(12735, 1, 2, 2, 8, 533, false,
        2, 1, 0, 0, "", ImmutableArray.Create(Effect(6, 7, "a,A,*e5380"), Effect(6, 11, "a,A,*E5380")));

    private static int Cell(int x, int y) => MapPathfinder.CellFromMapPoint(x, y - 8);
    private static PlannerSpell Candidate(SpellCastDescriptor cast, params long[] targets) =>
        new(cast, 1, cast.MaxPerTarget, ImmutableDictionary<long, int>.Empty, targets.ToImmutableHashSet());

    private static CombatTurnSnapshot Board(bool distractions = false)
    {
        // Only swapping with the ally makes the enemy line-castable; walking is unavailable.
        // Melee and optional weak ranged attacks are synthetic probes of placement search.
        var melee = new SpellCastDescriptor(42, 1, 3, 1, 1, 4, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(Effect(100, 40, "A") with { Size = 0 }));
        var spells = ImmutableArray.Create(Candidate(Transposition, 3), Candidate(Attirance, 2), Candidate(melee, 2));
        if (distractions)
            spells = spells.AddRange(Enumerable.Range(100, 9).Select(id => Candidate(melee with
            {
                SpellId = id, Ap = 1, Range = 8,
                Effects = ImmutableArray.Create(Effect(100, 1, "A") with { Size = 0 })
            }, 2)));
        return new(
            (from x in Enumerable.Range(8, 7) from y in Enumerable.Range(8, 3)
             select new PlannerCell(Cell(x, y), true, false, ImmutableArray<int>.Empty)).ToImmutableArray(),
            ImmutableArray.Create(
                new PlannerFighter(1, Cell(8, 8), false, 500, 500, States: ImmutableHashSet<int>.Empty),
                new PlannerFighter(2, Cell(13, 9), true, 500, 500, MovePoints: 0, ExpectedDamage: 0,
                    ThreatKnown: true, States: ImmutableHashSet<int>.Empty, TurnAp: 0),
                new PlannerFighter(3, Cell(10, 9), false, 500, 500, States: ImmutableHashSet<int>.Empty)),
            1, 8, 0, spells, ImmutableDictionary<int, int>.Empty, ForcedMovementSafe: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Transposition_then_Attirance_then_melee_survives_beam_pruning(bool distractions)
    {
        var plan = CombatTurnPlanner.Choose(Board(distractions));

        Assert.Equal(new[] { 12736, 12735, 42 }, plan.Actions.Select(a => a.SpellId));
        Assert.Equal(new long[] { 3, 2, 2 }, plan.Actions.Select(a => a.Target));
        Assert.Equal(new[] { Cell(10, 9), Cell(13, 9), Cell(11, 9) }, plan.Actions.Select(a => a.Cell));
        Assert.Equal(40, plan.Score.Damage);
        Assert.False(plan.BudgetExhausted);
    }

    [Theory]
    [InlineData("ap")]
    [InlineData("cooldown")]
    [InlineData("state")]
    [InlineData("unknown-state")]
    [InlineData("los")]
    [InlineData("range")]
    [InlineData("line")]
    [InlineData("unsafe")]
    public void Chain_requires_observed_legal_casts(string restriction)
    {
        var board = Board();
        board = restriction switch
        {
            "ap" => board with { Ap = 7 },
            "cooldown" => board with { Spells = board.Spells.SetItem(0, board.Spells[0] with { CastsLeft = 0 }) },
            "state" => board with { Fighters = board.Fighters.SetItem(0, board.Fighters[0] with { States = ImmutableHashSet.Create(7) }) },
            "unknown-state" => board with { Fighters = board.Fighters.SetItem(0, board.Fighters[0] with { States = null, StatesKnown = false }) },
            "los" => board with { Cells = board.Cells.Select(c => c.Id == Cell(11, 9) ? c with { BlocksLos = true } : c).ToImmutableArray() },
            "range" => board with { Fighters = board.Fighters.SetItem(2, board.Fighters[2] with { Cell = Cell(14, 10) }) },
            "line" => board with { Fighters = board.Fighters.SetItem(2, board.Fighters[2] with { Cell = Cell(10, 10) }) },
            "unsafe" => board with { ForcedMovementSafe = false },
            _ => throw new ArgumentException(restriction)
        };

        Assert.Equal(0, CombatTurnPlanner.Choose(board).Score.Damage);
    }

    [Theory]
    [InlineData("HS!7", true)]
    [InlineData("HS!7&HS=5380", true)]
    [InlineData("HS!7|HS=5380", true)]
    [InlineData("HS!7&garbage", false)]
    [InlineData("XX!7", false)]
    public void Only_fully_understood_state_criteria_are_admitted(string criterion, bool admitted) =>
        Assert.Equal(admitted, CombatTurnPlanner.IsAdmitted(Transposition with { StatesCriterion = criterion }));
}
