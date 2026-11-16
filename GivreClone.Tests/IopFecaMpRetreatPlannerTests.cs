using System.Collections.Immutable;
using System.Text.Json;
using GivreClone.App.Services;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public class IopFecaMpRetreatPlannerTests
{
    // Parse the real client export rather than replacing its masks with synthetic C/A masks.
    private static readonly ImmutableDictionary<int, SpellCastDescriptor> Fixtures = LoadFixtures();
    private static ImmutableDictionary<int, SpellCastDescriptor> LoadFixtures()
    {
        string root = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "givre-repo-root.txt")).Trim();
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/cos-dofus-video/rapports-v2/N07/spelllevelsdata.3.7.12.12.json")));
        int[] ids = { 41277, 41325, 1076, 46391, 85025, 41715, 85235, 41351 };
        return json.RootElement.GetProperty("references").GetProperty("RefIds").EnumerateArray()
            .Where(r => r.GetProperty("type").GetProperty("class").GetString() == "SpellLevelData")
            .Select(r => r.GetProperty("data")).Where(r => ids.Contains(r.GetProperty("id").GetInt32()))
            .ToImmutableDictionary(r => r.GetProperty("id").GetInt32(), r =>
            {
                int N(string key) => r.GetProperty(key).GetInt32();
                var effects = r.GetProperty("effects").GetProperty("Array").EnumerateArray().Select(e =>
                {
                    int E(string key) => e.GetProperty(key).GetInt32();
                    var z = e.GetProperty("zoneDescr");
                    return new SpellEffectDescriptor(E("actionId"), SpellEffectDescriptor.Classify(E("actionId")),
                        E("diceNum"), Math.Max(E("diceNum"), E("diceSide")), E("effectElement"), E("duration"),
                        e.GetProperty("targetMask").GetString()!, z.GetProperty("shape").GetInt32(),
                        z.GetProperty("param1").GetInt32(), z.GetProperty("param2").GetInt32(),
                        DiceNum: E("diceNum"), DiceSide: E("diceSide"), Value: E("value"), Delay: E("delay"),
                        Random: (int)e.GetProperty("random").GetDouble(), Triggers: e.GetProperty("triggers").GetString()!);
                }).ToImmutableArray();
                return new SpellCastDescriptor(N("spellId"), N("grade"), N("apCost"), N("minRange"), N("range"),
                    N("m_flags"), false, N("maxCastPerTurn"), N("maxCastPerTarget"), N("minCastInterval"),
                    N("initialCooldown"), r.GetProperty("statesCriterion").GetString()!, effects);
            });
    }

    private static int Cell(int x) => MapPathfinder.CellFromMapPoint(8 + x, 8);
    private static PlannerSpell Candidate(SpellCastDescriptor spell, int casts = 1) => new(spell, casts,
        spell.MaxPerTarget, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L, 2L));
    private static CombatTurnSnapshot Board(params PlannerSpell[] spells) => new(
        Enumerable.Range(0, 9).Select(x => new PlannerCell(Cell(x), true, false,
            new[] { x - 1, x + 1 }.Where(n => n >= 0 && n < 9).Select(Cell).ToImmutableArray())).ToImmutableArray(),
        ImmutableArray.Create(new PlannerFighter(1, Cell(2), false, 500, 500, States: ImmutableHashSet<int>.Empty, Breed: 1),
            new PlannerFighter(2, Cell(4), true, 500, 500, MovePoints: 3, AttackRange: 1,
                ExpectedDamage: 80, ThreatKnown: true, States: ImmutableHashSet<int>.Empty)),
        1, 8, 2, spells.ToImmutableArray(), ImmutableDictionary<int, int>.Empty, CasterLevel: 100);

    [Fact]
    public void Real_Bond_then_two_melee_hits_reaches_beyond_walking_range()
    {
        var board = Board(Candidate(Fixtures[41277]), Candidate(Fixtures[41325], 2)) with { Mp = 0 };
        board = board with { Fighters = board.Fighters.SetItem(1, board.Fighters[1] with
            { Cell = Cell(7), MovePoints = 0, ExpectedDamage = 0, TurnAp = 0 }) };
        Assert.Equal(0, CombatTurnPlanner.Choose(board with { Spells = board.Spells.RemoveAt(0) }).Score.Damage);
        var plan = CombatTurnPlanner.Choose(board);
        Assert.Equal(new[] { 13107, 13123, 13123 }, plan.Actions.Select(a => a.SpellId));
        Assert.Equal(Cell(6), plan.Actions[0].Cell);
        Assert.Equal(28, plan.Score.Damage);
        Assert.False(plan.BudgetExhausted);
    }

    [Theory]
    [InlineData(1076)]
    [InlineData(46391)]
    public void Feca_caster_uses_real_modeled_shield_when_exposed_at_full_health(int fixture)
    {
        // This export has no Feca-breed shield row. Use its unmodified shield descriptors
        // on a Feca caster to exercise the class-independent protection planner.
        var board = Board(Candidate(Fixtures[fixture])) with { Mp = 0 };
        var plan = CombatTurnPlanner.Choose(board);
        Assert.Contains(plan.Actions, a => a.SpellId == Fixtures[fixture].SpellId);
        Assert.True(plan.Score.Shielding > 0);
        Assert.Equal(0, plan.Score.Incoming);
    }

    [Theory]
    [InlineData(41715, 80)] // Enutrof Maladresse, dodgeable action 1080.
    [InlineData(85235, 80)] // Cra Cinglante, dodgeable action 1080 plus air damage.
    [InlineData(41351, 0)] // Iop Duel, guaranteed action 169 plus state 269.
    public void Real_MP_removal_retreat_only_guarantees_safety_for_unavoidable_loss(int fixture, int expectedIncoming)
    {
        var spell = Fixtures[fixture];
        var board = Board(Candidate(spell)) with { Ap = spell.Ap, Style = CombatStyle.Fleeing };
        if (fixture == 41351)
            board = board with { Fighters = board.Fighters.SetItem(1, board.Fighters[1] with { Cell = Cell(3) }),
                TackleImmune = true };
        var before = new SpellBoard(board.Fighters.Select(f => new SpellFighter(f.Id, f.Cell, f.Enemy,
            f.Life, f.MaxLife, Mp: f.MovePoints, States: f.States, StatesKnown: true)).ToArray());
        var resolved = SpellResolver.Resolve(before, 0, board.Fighters[1].Cell, spell, (_, _) => null, 100);
        Assert.Empty(resolved.Unmodeled);
        var loss = Assert.Single(resolved.Events.OfType<PointsResolved>());
        Assert.False(loss.IsAp);
        Assert.True(loss.Enemy);
        Assert.True(loss.Delta < 0);
        Assert.Equal(3 + loss.Delta, resolved.Board.Fighters[1].Mp);
        var without = CombatTurnPlanner.Choose(board with { Spells = ImmutableArray<PlannerSpell>.Empty });
        Assert.True(without.Score.Incoming > 0);
        var plan = CombatTurnPlanner.Choose(board);
        var cast = Assert.Single(plan.Actions, a => a.Kind == PlannerActionKind.Cast);
        Assert.Equal(spell.SpellId, cast.SpellId);
        Assert.Contains(plan.Actions.Skip(1), a => a.Kind == PlannerActionKind.Move);
        Assert.Equal(expectedIncoming, plan.Score.Incoming);
        Assert.False(plan.BudgetExhausted);
    }

    [Theory]
    [InlineData(41277)]
    [InlineData(1076)]
    [InlineData(46391)]
    [InlineData(85025)]
    [InlineData(41715)]
    [InlineData(85235)]
    [InlineData(41351)]
    public void Real_descriptors_are_admitted_to_automatic_rotation(int fixture)
    {
        var spell = Fixtures[fixture];
        Assert.True(CombatTurnPlanner.IsAdmitted(spell));
        Assert.True(AutoSpellRotation.ShouldUsePlanner(new[] { spell }));
        var config = Assert.Single(AutoSpellRotation.Build(Array.Empty<SpellConfig>(),
            new[] { new LearnedSpellInfo(spell.SpellId, spell.Ap, 0, spell) }, new(RequireDescriptors: true)));
        Assert.Equal(spell.SpellId, config.SpellId);
        Assert.Equal(spell.Effects.All(e => e.Kind == SpellEffectKind.Shield), config.SelfBuff);
    }
}
