using System.Collections.Generic;
using System.Linq;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class RoadDesignTests
{
    private static readonly RoadWaypoint A = new(100, Gather: true), B = new(200, Fight: true), C = new(300, Gather: true, Fight: true);

    [Fact]
    public void Each_point_aims_at_the_next_map_and_the_loop_closes_on_the_first()
    {
        var lua = RoadDesign.Generate(new[] { A, B, C }, new RoadOptions("Bois", Loop: true));
        Assert.Contains("{ map = \"100\", gather = true, changeMap = \"200\" }", lua);
        Assert.Contains("{ map = \"200\", fight = true, changeMap = \"300\" }", lua);
        Assert.Contains("{ map = \"300\", gather = true, fight = true, changeMap = \"100\" }", lua);
        Assert.DoesNotContain("npcBank", lua);   // jamais de dépôt dans move() : il bouclerait sur place

        var open = RoadDesign.Generate(new[] { A, B }, new RoadOptions(Loop: false));
        Assert.Contains("{ map = \"200\", fight = true },", open);   // dernier point : pas de sortie
    }

    [Fact]
    public void Bank_goes_through_the_door_to_the_banker_inside()
    {
        var lua = RoadDesign.Generate(new[] { A, B }, new RoadOptions(BankMap: 191104002, BankDoor: 300, BankInsideMap: 191105026));
        Assert.Contains("function bank()", lua);
        Assert.Contains("{ map = \"191104002\", door = \"300\" }", lua);
        Assert.Contains("{ map = \"191105026\", npcBank = true }", lua);
        var plain = RoadDesign.Generate(new[] { A }, new RoadOptions(BankMap: 5));
        Assert.Contains("{ map = \"5\", npcBank = true }", plain);
    }

    [Fact]
    public void A_generated_route_reopens_identically()
    {
        var o = new RoadOptions("Mon | trajet", Loop: false, MaxPods: 80, MinMonsters: 2, MaxMonsters: 5, BankMap: 9, BankDoor: 12, BankInsideMap: 10);
        var back = RoadDesign.Parse(RoadDesign.Generate(new[] { A, B, C }, o))!;
        Assert.Equal(new[] { A, B, C }, back.Waypoints);
        Assert.Equal(o with { GatherElements = back.Options.GatherElements }, back.Options);
        Assert.Empty(back.Notes);
    }

    [Fact]
    public void A_recorded_route_is_read_as_well_as_possible()
    {
        const string recorded = """
            function move()
              return {
                { map = "100", gather = true, fight = false, changeMap = "right" },
                { map = "4,-18", gather = false, fight = true, changeMap = "100" },
                { map = "zzz", gather = true },
                { map = "300", npcBank = true },
              }
            end
            function bank()
              return { { map = "999", npcBank = true } }
            end
            """;
        var imp = RoadDesign.Parse(recorded, (x, y) => x == 4 && y == -18 ? 200 : null)!;
        Assert.Equal(new long[] { 100, 200, 300 }, imp.Waypoints.Select(w => w.MapId));   // bank() ignorée, « zzz » écartée
        Assert.True(imp.Options.Loop);                                                     // la dernière sortie revient au départ
        Assert.Contains(imp.Notes, n => n.Contains("zzz"));
        Assert.Contains(imp.Notes, n => n.Contains("bank()"));
        Assert.Null(RoadDesign.Parse("print('rien')"));
    }

    [Fact]
    public void Validation_tells_walk_teleport_unreachable_and_unknown_apart()
    {
        var wps = new[] { new RoadWaypoint(1), new RoadWaypoint(2), new RoadWaypoint(3), new RoadWaypoint(4) };
        int Hops(long a, long b) => (a, b) == (1, 2) ? 3 : -1;
        bool Known(long m) => m != 4;
        int? World(long m) => m == 3 ? 2 : 1;
        var legs = RoadDesign.Validate(wps, loop: true, Hops, Known, World);
        Assert.Equal(new[] { RoadLegState.Ok, RoadLegState.Unreachable, RoadLegState.Unknown, RoadLegState.Unknown }, legs.Select(l => l.State));
        Assert.Equal(3, legs[0].Hops);
        var noLoop = RoadDesign.Validate(new[] { new RoadWaypoint(1), new RoadWaypoint(5) }, loop: false, (_, _) => -1, _ => true, _ => 1);
        Assert.Equal(RoadLegState.Teleport, noLoop.Single().State);   // Monde des Douze sans chemin à pied : zaap / havre-sac
    }

    [Fact]
    public void Duplicates_and_optimisation()
    {
        Assert.Equal(new[] { 0, 2 }, RoadDesign.Duplicates(new[] { A, B, A }));
        var coords = new Dictionary<long, (int, int)> { [1] = (0, 0), [2] = (10, 0), [3] = (1, 0), [4] = (11, 0) };
        var opt = RoadDesign.Optimize(new[] { 1L, 2, 3, 4 }.Select(id => new RoadWaypoint(id)).ToList(), id => coords[id]);
        Assert.Equal(new long[] { 1, 3, 2, 4 }, opt.Select(w => w.MapId));
    }

    [Fact]
    public void Hdv_sells_the_relist_list_once_per_full_bag_then_banks()
    {
        var hdv = new HdvPlan(191104004, new[] { (289L, 100, 50L), (303L, 10, 0L) });
        var lua = RoadDesign.Generate(new[] { A, B }, new RoadOptions(BankMap: 5, Hdv: hdv));
        Assert.Contains("HDV_DONE = false   -- pods redescendus", lua);           // remis à zéro par move()
        Assert.Contains("local HDV_MAP = 191104004", lua);
        Assert.Contains("{ 289, 100, 50 },", lua);
        Assert.Contains("{ 303, 10, 0 },", lua);
        Assert.Contains("  sellAtHdv()", lua);
        Assert.Contains("{ map = \"5\", npcBank = true }", lua);

        // HDV sans banque connue : arrêt propre si les pods restent pleins (pas d'attente sans fin).
        var noBank = RoadDesign.Generate(new[] { A }, new RoadOptions(Hdv: hdv));
        Assert.Contains("return false end", noBank);
        Assert.DoesNotContain("npcBank", noBank);

        // Liste vide : rien n'est généré.
        Assert.DoesNotContain("sellAtHdv", RoadDesign.Generate(new[] { A }, new RoadOptions(Hdv: new HdvPlan(1, new (long, int, long)[0]))));
    }

    [Fact]
    public void Generated_scripts_compile_as_lua()
    {
        var hdv = new HdvPlan(1, new[] { (289L, 100, 50L) });
        var scripts = new[]
        {
            RoadDesign.Generate(new[] { A, B, C }, new RoadOptions("Trajet d'essai", BankMap: 9, BankDoor: 3, BankInsideMap: 10, Hdv: hdv), id => $"carte {id}"),
            RoadDesign.Generate(new[] { A }, new RoadOptions(Loop: false, Hdv: hdv)),
            ExpeditionScript.Build(new ExpeditionProfile("Zone d'essai", "1;2;-4,18", Mandatory: "31", Forbidden: "46", BankMap: 5, BankDoor: 2, BankInside: 6, Hdv: hdv, AnomalySubarea: 445)),
            ExpeditionScript.Build(new ExpeditionProfile("Z", "1")),
            ExpeditionScript.Build(new ExpeditionProfile("Z", "1", Elixir: new ElixirPlan(26872, 52, 73400321, 50000))),
            ExpeditionScript.BuildDungeon(new DungeonProfile("Donjon d'essai", new long[] { 1, 2 }, 3, new[] { (8L, 1, "Clé d'or") }, BankMap: 4, BankDoor: 1, BankInside: 7)),
        };
        // LoadString compile sans exécuter : une erreur de syntaxe Lua (apostrophe mal échappée…) lève une exception.
        foreach (var lua in scripts) new MoonSharp.Interpreter.Script().LoadString(lua);
    }
}

