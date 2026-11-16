using System;
using System.Linq;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Lot C1 (hook onTurn): briques PURES du scripting de combat — raison de non-lancer, choix de la
/// case d'un déplacement tronqué aux PM, et forge « plus prêt » (corps vide).</summary>
public class ScriptCombatTests
{
    private const int Center = 300;

    private static SpellCastInput In(int from, int target, int apCost = 3, int minR = 1, int maxR = 8,
        bool inLine = false, bool inDiag = false, bool los = false, int apLeft = 6, bool cd = false,
        int castsTurn = 0, int maxTurn = 0, int castsTarget = 0, int maxTarget = 0,
        bool needFree = false, bool needTaken = false, bool occupied = false)
        => new(from, target, apCost, minR, maxR, inLine, inDiag, los, apLeft, cd, castsTurn, maxTurn, castsTarget, maxTarget,
               needFree, needTaken, occupied);

    private static readonly Func<int, bool> NoBlock = _ => false;

    private static int CellAt(int from, int dist, Func<int, bool>? extra = null)
        => Enumerable.Range(0, MapPathfinder.CellCount)
            .First(c => MapPathfinder.CellDistance(from, c) == dist && (extra?.Invoke(c) ?? true));

    [Fact]
    public void SpellCastCheck_reports_reference_reason_codes()
    {
        int t3 = CellAt(Center, 3, c => MapPathfinder.Aligned(Center, c));
        Assert.Equal(SpellCastVerdict.None, SpellCastCheck.Evaluate(In(Center, t3), NoBlock, NoBlock));
        Assert.Equal(SpellCastVerdict.ActionPoints, SpellCastCheck.Evaluate(In(Center, t3, apLeft: 2), NoBlock, NoBlock));
        Assert.Equal(3, (int)SpellCastCheck.Evaluate(In(Center, t3, apLeft: 2), NoBlock, NoBlock));
        Assert.Equal(SpellCastVerdict.Cooldown, SpellCastCheck.Evaluate(In(Center, t3, cd: true), NoBlock, NoBlock));
        Assert.Equal(SpellCastVerdict.TooManyLaunch, SpellCastCheck.Evaluate(In(Center, t3, castsTurn: 2, maxTurn: 2), NoBlock, NoBlock));
        Assert.Equal(SpellCastVerdict.MinRange, SpellCastCheck.Evaluate(In(Center, t3, minR: 4), NoBlock, NoBlock));
        Assert.Equal(SpellCastVerdict.MaxRange, SpellCastCheck.Evaluate(In(Center, t3, maxR: 2), NoBlock, NoBlock));
        Assert.Equal(SpellCastVerdict.TooManyLaunchOnCell, SpellCastCheck.Evaluate(In(Center, t3, castsTarget: 1, maxTarget: 1), NoBlock, NoBlock));
    }

    [Fact]
    public void SpellCastCheck_line_and_diagonal_constraints()
    {
        int diag = CellAt(Center, 2, c => MapPathfinder.Diagonal(Center, c));
        int aligned = CellAt(Center, 2, c => MapPathfinder.Aligned(Center, c));
        Assert.Equal(SpellCastVerdict.NotInLine, SpellCastCheck.Evaluate(In(Center, diag, inLine: true), NoBlock, NoBlock));
        Assert.Equal(SpellCastVerdict.None, SpellCastCheck.Evaluate(In(Center, aligned, inLine: true), NoBlock, NoBlock));
        Assert.Equal(SpellCastVerdict.NotInDiagonal, SpellCastCheck.Evaluate(In(Center, aligned, inDiag: true), NoBlock, NoBlock));
        Assert.Equal(SpellCastVerdict.None, SpellCastCheck.Evaluate(In(Center, diag, inDiag: true), NoBlock, NoBlock));
    }

    [Fact]
    public void SpellCastCheck_target_cell_constraints()
    {
        int t = CellAt(Center, 2);
        Assert.Equal(SpellCastVerdict.NeedTakenCell, SpellCastCheck.Evaluate(In(Center, t, needTaken: true, occupied: false), NoBlock, NoBlock));
        Assert.Equal(SpellCastVerdict.NeedFreeCell, SpellCastCheck.Evaluate(In(Center, t, needFree: true, occupied: true), NoBlock, NoBlock));
        Assert.Equal(SpellCastVerdict.None, SpellCastCheck.Evaluate(In(Center, t, needTaken: true, occupied: true), NoBlock, NoBlock));
    }

    [Fact]
    public void SpellCastCheck_line_of_sight_only_when_flagged_and_at_distance()
    {
        int t3 = CellAt(Center, 3, c => MapPathfinder.Aligned(Center, c));
        int blocker = MapPathfinder.LineCells(Center, t3).First(c => c != Center && c != t3);
        Func<int, bool> blocks = c => c == blocker;
        Assert.Equal(SpellCastVerdict.LineOfSight, SpellCastCheck.Evaluate(In(Center, t3, los: true), blocks, NoBlock));
        Assert.Equal(8, (int)SpellCastCheck.Evaluate(In(Center, t3, los: true), blocks, NoBlock));
        // LdV NON exigée par le sort -> jamais testée (jamais de LdV forcée).
        Assert.Equal(SpellCastVerdict.None, SpellCastCheck.Evaluate(In(Center, t3, los: false), blocks, NoBlock));
        // Un occupant intermédiaire bloque aussi la vue.
        Assert.Equal(SpellCastVerdict.LineOfSight, SpellCastCheck.Evaluate(In(Center, t3, los: true), NoBlock, blocks));
        // Au corps-à-corps (d=1) la LdV n'est jamais testée, même avec tout bloqué.
        int t1 = CellAt(Center, 1);
        Assert.Equal(SpellCastVerdict.None, SpellCastCheck.Evaluate(In(Center, t1, los: true), _ => true, _ => true));
    }

    private static readonly Func<int, bool> Open = _ => true;

    [Fact]
    public void PickDestination_reaches_target_within_mp()
    {
        var reach = MapPathfinder.OrthoReachable(Center, 5, Open);
        int t = reach.First(kv => kv.Value == 2).Key;
        int dest = CombatMovePlanner.PickDestination(Center, t, 3, Open, null, out int steps);
        Assert.Equal(t, dest);
        Assert.Equal(2, steps);
    }

    [Fact]
    public void PickDestination_truncates_to_mp()
    {
        var reach = MapPathfinder.OrthoReachable(Center, 5, Open);
        int t = reach.First(kv => kv.Value == 5).Key;
        int dest = CombatMovePlanner.PickDestination(Center, t, 3, Open, null, out int steps);
        Assert.True(dest >= 0);
        Assert.Equal(3, steps);
        Assert.Equal(2, MapPathfinder.OrthoDistanceField(t, Open)[dest]);
    }

    [Fact]
    public void PickDestination_occupied_target_stops_adjacent()
    {
        var reach = MapPathfinder.OrthoReachable(Center, 5, Open);
        int t = reach.First(kv => kv.Value == 2).Key;
        Func<int, bool> walk = c => c != t;   // la cible est un combattant (non marchable)
        int dest = CombatMovePlanner.PickDestination(Center, t, 3, walk, null, out int steps);
        Assert.True(dest >= 0 && dest != t);
        Assert.Equal(1, steps);
        Assert.Equal(1, MapPathfinder.OrthoDistanceField(t, walk)[dest]);
    }

    [Fact]
    public void PickDestination_follows_a_directed_detour_when_partial_progress_increases_air_distance()
    {
        var (cx, cy) = MapPathfinder.MapPointOf(300);
        int Cell(int x, int y) => MapPathfinder.CellFromMapPoint(cx + x - 8, cy + y - 8);
        int[] path = [Cell(8, 8), Cell(8, 9), Cell(9, 9), Cell(10, 9), Cell(10, 8)];
        Assert.DoesNotContain(-1, path);
        bool Step(int from, int to) => Enumerable.Range(0, path.Length - 1)
            .Any(i => path[i] == from && path[i + 1] == to);

        int dest = CombatMovePlanner.PickDestination(path[0], path[^1], 2,
            c => path.Contains(c), Step, out int steps);

        Assert.Equal(path[2], dest);
        Assert.Equal(2, steps);
    }

    [Fact]
    public void PickDestination_walled_or_no_mp_returns_minus_one()
    {
        var reach = MapPathfinder.OrthoReachable(Center, 5, Open);
        int t = reach.First(kv => kv.Value == 4).Key;
        Assert.Equal(-1, CombatMovePlanner.PickDestination(Center, t, 3, c => c == Center, null, out _));
        Assert.Equal(-1, CombatMovePlanner.PickDestination(Center, t, 0, Open, null, out _));
    }

    [Fact]
    public void BuildReadyFramed_false_is_empty_body_with_ready_code()
    {
        var catalog = ProtocolCatalog.LoadFromDirectory(System.IO.Path.Combine(AppContext.BaseDirectory, "data"));
        var readyField = catalog.FieldNumber("FightReadyRequest", "is_ready")
            ?? throw new InvalidOperationException("Champ FightReadyRequest.is_ready absent du catalogue livré");
        var on = Decode(MoveForge.BuildReadyFramed());
        var off = Decode(MoveForge.BuildReadyFramed(false));
        Assert.Equal(catalog.CodeForName("FightReadyRequest"), on.Code);
        Assert.Equal(on.Code, off.Code);
        Assert.Equal(1UL, on.Payload.First(f => f.Field == readyField).Varint);
        Assert.Empty(off.Payload);                   // proto3 : is_ready=0 omis -> corps vide
    }

    private static (string Code, System.Collections.Generic.List<ProtoField> Payload) Decode(byte[] framed)
    {
        Assert.NotEmpty(framed);
        var frames = RawProtobuf.ConsumeFrames(framed, out var rest);
        Assert.Single(frames);
        Assert.Empty(rest);
        var hits = RawProtobuf.FindTypeUrls(RawProtobuf.Parse(frames[0]));
        Assert.Single(hits);
        return (hits[0].Code, hits[0].Payload?.ToList() ?? new System.Collections.Generic.List<ProtoField>());
    }
}
