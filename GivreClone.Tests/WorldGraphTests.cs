using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

/// <summary>World-graph : carte d'ARRIVÉE (m_to) vs id ENVOYÉ (m_transitionMapId). Fixture = arêtes RÉELLES du client
/// (bundle worldassets du 2026-09-22) autour du zaap de Bonta, au format du cache disque. Cas signalé par l'user : zaap
/// 212600323 [-31,-56] -> atelier 217062406 [-34,-54] = 16 sauts (planification sur l'id envoyé, les ScrollAction du
/// plateau menant à des cartes FANTÔMES) alors que le vrai chemin (fait à la main) est de 6 sauts.</summary>
public class WorldGraphTests
{
    private const int Zaap = 212600323, Workshop = 217062406;

    // Sous-graphe réel (généré depuis le dump du bundle) : cartes des chemins à 16 et à 6 sauts, TOUTES leurs arêtes.
    private const string BontaFixture = """
{"212599813":[[537,212599814,2,1],[392,212599301,4,1],[391,212600325,0,1],[343,217058310,255,32]],"212600326":[[536,212600327,2,1],[364,212599814,4,1],[125,212600838,0,1],[7,212600325,6,1],[512,217059334,255,32]],"212600322":[[535,212600323,2,1],[9,212600321,6,1],[468,217059328,255,32],[512,217060352,255,32],[237,212600834,0,2,null,212601347],[476,212599810,4,1]],"212601345":[[293,212601857,0,1],[541,212601346,2,1],[409,217062408,255,32],[11,212601344,6,1]],"212599302":[[400,217062406,255,32],[545,212599814,0,1],[294,212598790,4,1],[6,212599301,6,1],[540,212599303,2,1]],"212600320":[[490,212599808,4,1],[10,212600577,6,1],[535,212600321,2,1]],"212601346":[[223,212601858,0,1],[554,212601347,2,1],[476,212600321,4,2],[9,212601345,6,1],[238,212600321,4,2]],"212601348":[[3,212601347,6,1],[356,217055236,255,32],[538,212601349,2,1],[437,212600323,4,32],[307,212601860,0,1]],"212601347":[[335,212601859,0,1],[549,212601348,2,1],[231,217055238,255,32],[421,212600322,4,32],[9,212601346,6,1]],"212600325":[[419,212600837,0,1],[542,212600326,2,1],[378,212599813,4,1],[190,212599811,6,32]],"212601349":[[252,212600837,4,1],[535,212601350,2,1],[6,212601348,6,1],[349,212601861,0,1]],"212601344":[[544,212601345,2,1],[307,212603138,0,2],[490,212600577,4,32]],"212600323":[[224,212599811,4,1],[321,212600835,0,2,null,212601348],[537,212600324,2,2,null,212600837],[3,212600322,6,1]],"212599811":[[539,212599812,2,2,null,212600325],[392,212599299,4,1],[283,217056264,255,32],[10,212599810,6,1],[237,212600323,0,1]],"212600321":[[541,212600322,2,1],[140,212599809,4,1],[2,212600320,6,1],[461,212600833,0,2,null,212601346]],"212600577":[[445,212601344,0,32],[555,212600320,2,1],[210,212600065,4,1]],"212599814":[[496,217056260,255,32],[392,212599302,4,1],[539,212599815,2,1],[391,212600326,0,1],[18,212599813,6,1]],"212600838":[[84,212600326,4,1],[5,212600837,6,1],[538,212600839,2,1],[223,212601350,0,1]],"212600837":[[406,212600325,4,1],[538,212600838,2,1],[203,212600323,6,32],[265,212601349,0,1]],"217062406":[[437,212599302,255,32]]}
""";

    private static Dictionary<int, IReadOnlyList<MapTransition>> Fixture() => WorldGraphCache.Parse(BontaFixture);

    [Fact]
    public void Zaap_scroll_exits_send_a_phantom_id_and_arrive_on_m_to()
    {
        var g = new WorldGraph(Fixture());
        var south = g.Transitions(Zaap).Single(t => t.CellId == 537);
        Assert.Equal(2, south.Type);
        Assert.Equal(212600324, south.TransitionMapId);   // id à envoyer (voisin théorique, aucune carte réelle)
        Assert.Equal(212600837, south.Arrival);           // carte où l'on arrive vraiment
        var east = g.Transitions(Zaap).Single(t => t.CellId == 321);
        Assert.Equal(212600835, east.TransitionMapId);
        Assert.Equal(212601348, east.Arrival);
    }

    [Fact]
    public void Routing_on_arrival_finds_the_6_hop_route()
    {
        var path = new WorldGraph(Fixture()).PathToMap(Zaap, Workshop);
        Assert.Equal(6, path.Count);
        Assert.Equal(Workshop, path[^1].Arrival);
        Assert.Equal(32, path[^1].Type);                  // porte de l'atelier
        Assert.Contains(path, t => t.Type == 2 && t.Arrival != t.TransitionMapId);   // descente du plateau par ScrollAction
    }

    [Fact]
    public void Legacy_graph_without_arrival_still_routes_on_the_sent_id()
    {
        // Ancien cache (sans 6e élément) : Arrival retombe sur l'id envoyé -> comportement historique (16 sauts), sans crash.
        var legacy = Fixture().ToDictionary(kv => kv.Key,
            kv => (IReadOnlyList<MapTransition>)kv.Value.Select(t => t with { ArrivalMapId = 0 }).ToList());
        Assert.Equal(16, new WorldGraph(legacy).PathToMap(Zaap, Workshop).Count);
    }

    [Fact]
    public void Cache_round_trip_keeps_arrival_and_criterion()
    {
        var src = new Dictionary<int, IReadOnlyList<MapTransition>>
        {
            [1] = new List<MapTransition>
            {
                new(10, 2, 0, 1),                              // cas général
                new(20, 3, 2, 2, null, 4),                     // ScrollAction : arrivée différente, sans critère
                new(30, 5, 6, 32, "Qf=230"),                   // porte conditionnelle
                new(40, 6, 4, 2, "SC=5&ST=7", 7),              // critère ET arrivée
            },
        };
        var back = WorldGraphCache.Parse(WorldGraphCache.Serialize(src))[1];
        Assert.Equal(src[1], back);
        Assert.Equal(4, back[1].Arrival);
        Assert.Null(back[1].Criterion);
        Assert.Equal("SC=5&ST=7", back[3].Criterion);
        Assert.Equal(7, back[3].Arrival);
    }

    [Fact]
    public void Exit_cells_fall_back_to_diagonal_bits_on_a_border_without_cardinal_bit()
    {
        // Zaap de Bonta : aucune cellule avec le bit SUD (4) ; la vraie sortie sud 551 (dernière rangée) porte le bit 8 (SO),
        // 448 (colonne gauche, rangée 32) aussi -> sortie OUEST possible, pas sud.
        var cells = new List<MapCell>
        {
            new(551, true, true, 0, MapChangeData: 8),
            new(448, true, true, 0, MapChangeData: 8),
            new(537, true, true, 0, MapChangeData: 0),
        };
        Assert.Equal(new[] { 551 }, MapExitCells.Candidates(cells, MapDir.Bottom).Select(c => c.CellId));
        Assert.Contains(448, MapExitCells.Candidates(cells, MapDir.Left).Select(c => c.CellId));

        // Bord « normal » (au moins une cellule au bit cardinal) : comportement historique, pas de repli diagonal.
        var normal = new List<MapCell> { new(545, true, true, 0, MapChangeData: 4), new(551, true, true, 0, MapChangeData: 8) };
        Assert.Equal(new[] { 545 }, MapExitCells.Candidates(normal, MapDir.Bottom).Select(c => c.CellId));
    }

    [Fact]
    public void Diagonal_fallback_ignores_corners_of_the_adjacent_border()
    {
        // Coins réels du bord VOISIN (données .mbin) : 13:224 = sortie HAUT, 27:131 = DROITE, 532:56 = GAUCHE, 546:14 = BAS.
        static MapCell C(int id, int mcd) => new(id, true, true, 0, MapChangeData: mcd);
        Assert.DoesNotContain(13, MapExitCells.Candidates(new List<MapCell> { C(13, 224) }, MapDir.Right).Select(c => c.CellId));
        Assert.DoesNotContain(27, MapExitCells.Candidates(new List<MapCell> { C(27, 131), C(7, 32) }, MapDir.Top).Select(c => c.CellId));
        Assert.DoesNotContain(532, MapExitCells.Candidates(new List<MapCell> { C(532, 56), C(546, 10) }, MapDir.Bottom).Select(c => c.CellId));
        Assert.DoesNotContain(546, MapExitCells.Candidates(new List<MapCell> { C(546, 14), C(532, 40) }, MapDir.Left).Select(c => c.CellId));
        Assert.Equal(new[] { 7 }, MapExitCells.Candidates(new List<MapCell> { C(7, 32), C(27, 131) }, MapDir.Top).Select(c => c.CellId));
    }

    [Fact]
    public void Arrival_maps_are_known_to_the_graph()
    {
        var g = new WorldGraph(Fixture());
        Assert.True(g.IsKnownMap(212600837));   // arrivée réelle de la descente sud (aussi source d'arêtes)
        Assert.True(g.IsKnownMap(Workshop));
    }
}
