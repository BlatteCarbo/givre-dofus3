using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Voyage par SECTEUR (zone du world-graph) + critère MI des portes. Fixture = arêtes RÉELLES du client (bundle
/// worldassets, dump du 2026-09-28) autour de 126093080 (-2,-53) : secteur 1 (haut) avec les sorties est 251 / nord 7,
/// secteur 2 (bas, où l'on arrive toujours) avec les sorties sud 538 / ouest 392, et une PORTE (cellule 302, élément 484493,
/// skill 184, critère « MI=484493,0&amp;MI=484493,0 ») qui mène à 126093592 depuis les deux secteurs. Cas signalé : depuis le
/// secteur bas, le voyage partait par le bord est 251 (injoignable à pied) au lieu de la porte.</summary>
public class ZoneRoutingTests
{
    private const int Map = 126093080, Target = 126093592, DoorElement = 484493;
    private const string DoorCrit = "MI=484493,0&MI=484493,0";

    private const string Fixture = """
{"_v":5,
 "126093080":[[7,126093081,6,1],[251,126093592,0,1],[302,126093592,0,32,"MI=484493,0&MI=484493,0",0,1,1,484493,184],
              [538,126093079,2,1,null,0,2,1,0,0],[392,126092568,4,1,null,0,2,1,0,0],[302,126093592,0,32,"MI=484493,0&MI=484493,0",0,2,1,484493,184]],
 "126093081":[[540,126093080,2,2,null,0,1,2,0,0],[111,126093593,0,1]],
 "126093593":[[541,126093592,2,1]],
 "126093079":[[6,126093080,6,1,null,0,1,2,0,0]],
 "126092568":[[405,126093080,0,1,null,0,1,2,0,0]],
 "126093592":[[210,126093080,4,2,null,0,1,2,0,0]]}
""";

    private static WorldGraph Graph() => new(WorldGraphCache.Parse(Fixture));

    // Critère évalué comme en jeu : état LIVE de l'interactif sur la carte courante.
    private static System.Func<string, bool> Live(int? doorState)
        => c => QuestCriteria.Eval(c, _ => false, e => e == DoorElement ? doorState : null);

    [Fact]
    public void Lower_sector_takes_the_door_not_the_unreachable_east_border()
    {
        var path = Graph().PathToMap(Map, Target, criterionMet: Live(0), startZone: 2);
        var hop = Assert.Single(path);
        Assert.Equal(32, hop.Type);
        Assert.Equal(302, hop.CellId);
        Assert.Equal(DoorElement, hop.ElementId);
        Assert.Equal(184, hop.SkillId);
        Assert.Equal(2, hop.FromZone);
    }

    [Fact]
    public void Upper_sector_still_uses_its_own_east_border()
    {
        var hop = Assert.Single(Graph().PathToMap(Map, Target, criterionMet: Live(0), startZone: 1));
        Assert.Equal(251, hop.CellId);
        Assert.Equal(1, hop.Type);
    }

    [Fact]
    public void Unknown_zone_keeps_the_historical_map_level_route()
    {
        var hop = Assert.Single(Graph().PathToMap(Map, Target, criterionMet: Live(0)));
        Assert.Equal(251, hop.CellId);
    }

    [Fact]
    public void Door_in_another_state_is_not_taken_and_falls_back_to_map_level()
    {
        // Porte hors de son état normal (live 1) : aucun chemin par secteur -> repli niveau carte (comportement d'avant).
        var hop = Assert.Single(Graph().PathToMap(Map, Target, criterionMet: Live(1), startZone: 2));
        Assert.Equal(251, hop.CellId);
    }

    [Fact]
    public void Without_criterion_evaluator_doors_are_never_used()
    {
        var hop = Assert.Single(Graph().PathToMap(Map, Target, startZone: 2));
        Assert.NotEqual(32, hop.Type);
    }

    [Fact]
    public void Zone_route_follows_arrival_sectors_across_maps()
    {
        // Depuis 126093081 (sans passer par 593) : sud 540 -> arrivée en SECTEUR 2 de 126093080 -> la PORTE. Au niveau carte,
        // le même trajet enchaînait sur le bord est 251 du secteur 1, injoignable depuis l'arrivée (le bug, un saut plus loin).
        var g = Graph();
        var avoid = new HashSet<int> { 126093593 };
        var zoned = g.PathToMap(126093081, Target, avoid, criterionMet: Live(0), startZone: 1);
        Assert.Equal(new[] { 540, 302 }, zoned.Select(t => t.CellId));
        Assert.Equal(2, zoned[1].FromZone);
        var mapLevel = g.PathToMap(126093081, Target, avoid, criterionMet: Live(0));
        Assert.Equal(new[] { 540, 251 }, mapLevel.Select(t => t.CellId));
    }

    [Fact]
    public void Sole_zone_says_when_the_self_cell_is_needed()
    {
        var g = Graph();
        Assert.Equal(-1, g.SoleZone(Map));          // deux secteurs : il faut la cellule du perso
        Assert.Equal(1, g.SoleZone(126093081));     // un seul : connu sans attendre la cellule
        Assert.Equal(0, g.SoleZone(999));           // hors graphe
        var v4 = new WorldGraph(WorldGraphCache.Parse("""{"1":[[10,2,0,1]]}"""));
        Assert.Equal(0, v4.SoleZone(1));            // ancien cache : zones inconnues
    }

    [Fact]
    public void Mi_criterion_reads_live_state_and_presumes_only_the_default_state()
    {
        Assert.True(QuestCriteria.Eval(DoorCrit, _ => false, e => 0));
        Assert.False(QuestCriteria.Eval(DoorCrit, _ => false, e => 1));
        Assert.True(QuestCriteria.Eval(DoorCrit, _ => false, _ => null));            // autre carte : état par défaut présumé
        Assert.False(QuestCriteria.Eval("MI=483209,1", _ => false, _ => null));      // levier : jamais présumé activé
        Assert.True(QuestCriteria.Eval("MI=483209,1", _ => false, e => e == 483209 ? 1 : null));
        Assert.True(QuestCriteria.Eval(DoorCrit, _ => false));                        // surcharge sans état live : état par défaut présumé
    }

    [Fact]
    public void Interactive_state_only_detection()
    {
        Assert.True(QuestCriteria.IsInteractiveStateOnly(DoorCrit));
        Assert.True(QuestCriteria.IsInteractiveStateOnly("MI=488188,1"));
        Assert.False(QuestCriteria.IsInteractiveStateOnly("MI=523611,1&PL>49"));
        Assert.False(QuestCriteria.IsInteractiveStateOnly("Qf=230"));
        Assert.False(QuestCriteria.IsInteractiveStateOnly(null));
    }

    [Fact]
    public void Cache_v5_round_trips_zones_and_door_and_v4_leaves_zones_unknown()
    {
        var src = new Dictionary<int, IReadOnlyList<MapTransition>>
        {
            [1] = new List<MapTransition>
            {
                new(10, 2, 0, 1, null, 0, 1, 1),                        // cas général v5 (zones 1) : écrit compact
                new(302, 3, 0, 32, DoorCrit, 0, 2, 1, DoorElement, 184),
                new(20, 3, 2, 2, null, 4, 1, 2),                        // ScrollAction arrivant en secteur 2
            },
        };
        string json = WorldGraphCache.Serialize(src);
        Assert.Contains("[10,2,0,1]", json);
        var back = WorldGraphCache.Parse(json)[1];
        Assert.Equal(src[1], back);

        var legacy = WorldGraphCache.Parse("""{"1":[[10,2,0,1],[20,3,2,2,null,4]]}""")[1];
        Assert.All(legacy, t => { Assert.Equal(0, t.FromZone); Assert.Equal(0, t.ArrivalZone); });
    }
}
