using Microsoft.Data.Sqlite;
using GivreClone.App.Services.LotM;
using Xunit;

namespace GivreClone.Tests;

public class ExpeditionCatalogueTests
{
    [Fact]
    public void Navigation_filters_placeholder_before_deduplication_and_keeps_real_zero_coordinate()
    {
        var graph = new GivreClone.Protocol.WorldGraph(new Dictionary<int, IReadOnlyList<GivreClone.Protocol.MapTransition>>
        {
            [10] = new[] { new GivreClone.Protocol.MapTransition(1, 11, 0, 1) }
        });
        var maps = new (long, (int, int)?, int?)[]
        {
            (153887744, (0, 0), 2), // instance sans transition, avant la vraie carte
            (11, (0, 0), 2),        // connue uniquement comme arrivée
            (12, (4, 1), 2),        // graphe incomplet hors 0,0 : conserver le repli voyage
        };
        var route = GivreClone.App.Services.ExpeditionScript.CleanMaps(maps, id => graph.IsKnownMap((int)id));
        Assert.Equal(new long[] { 11, 12 }, route);
    }

    [Fact]
    public void Off_graph_interior_maps_are_dropped_but_off_graph_atlas_maps_keep_the_travel_fallback()
    {
        // Live 06/11 Dojo du Vent (950) : cartes de donjon en calque -1, absentes du graphe -> no_route garanti.
        var maps = new (long, (int, int)?, int?)[]
        {
            (205785110, (20, -37), -1),
            (207880192, (20, -37), -1),
            (12, (4, 1), 2),
        };

        var route = GivreClone.App.Services.ExpeditionScript.CleanMaps(maps, _ => false);

        Assert.Equal(new long[] { 12 }, route);
    }

    [Fact]
    public void A_dungeon_only_subarea_without_graph_nodes_becomes_empty_instead_of_failing_at_runtime()
    {
        var maps = new (long, (int, int)?, int?)[] { (205785110, (20, -37), -1), (207880192, (20, -37), -1) };

        Assert.Empty(GivreClone.App.Services.ExpeditionScript.CleanMaps(maps, _ => false));
        Assert.Single(GivreClone.App.Services.ExpeditionScript.CleanMaps(maps, _ => null));
    }

    [Fact]
    public void Missing_navigation_data_preserves_the_route_but_known_absence_can_empty_it()
    {
        var maps = new (long, (int, int)?, int?)[] { (153887744, (0, 0), 2) };
        Assert.Equal(new long[] { 153887744 }, GivreClone.App.Services.ExpeditionScript.CleanMaps(maps, _ => null));
        Assert.Empty(GivreClone.App.Services.ExpeditionScript.CleanMaps(maps, _ => false));
    }

    [FactIfGameData]
    public void Installed_incarnam_route_excludes_logged_combat_instances_and_interiors()
    {
        using var db = GameDb.TryOpen(RealGameData.Path!)!;
        var positions = db.AllMapPositions();
        foreach (int subarea in new[] { 449, 450 })
        {
            var maps = db.ExpeditionMaps(subarea).Where(positions.ContainsKey)
                .Select(id => (id, ((int, int)?)(positions[id].X, positions[id].Y), (int?)positions[id].World)).ToList();
            // Les journaux prouvent l'absence de chemin de ces instances 0,0. Les
            // autres cartes ne sont pas classées par ce test comme combat ou navigation.
            var route = GivreClone.App.Services.ExpeditionScript.CleanMaps(maps,
                id => positions[id] is { X: 0, Y: 0 } ? false : null);
            Assert.Equal(7, route.Count);
            Assert.All(route, id => Assert.True(positions[id].World >= 0));
            Assert.DoesNotContain(route, id => positions[id] is { X: 0, Y: 0 });
        }
    }

    [Fact]
    public void Lists_translated_areas_subareas_and_unique_monsters_with_the_release_schema()
    {
        using var fake = new FakeGameDb();
        using (var c = new SqliteConnection($"Data Source={fake.File}"))
        {
            c.Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "alter table SubAreaData add column monsters text; alter table MonsterData add column grades text; " +
                "update SubAreaData set monsters='[78,78,77,999]' where id=50; " +
                "update MonsterData set grades='[{\"level\":10},{\"level\":15}]' where id=78; " +
                "insert into SubAreaData(id,nameId,areaId,level,monsters) values(51,12,2,1,'[ ]'),(52,12,2,1,'invalid'); " +
                "insert into AreaData values(3,21); insert into SubAreaData(id,nameId,areaId,level,monsters) values(53,12,3,1,'[ ]');";
            cmd.ExecuteNonQuery();
        }
        using var db = GameDb.TryOpen(fake.File)!;
        Assert.Equal(2, Assert.Single(db.ExpeditionAreas()).Id);
        Assert.Equal(50, Assert.Single(db.ExpeditionSubAreas(2)).Id);
        var monsters = db.ExpeditionMonsters(50);
        Assert.Equal(2, monsters.Count);
        var larva = Assert.Single(monsters.Where(m => m.Id == 78));
        Assert.Equal("Larve", larva.Name);
        Assert.Equal((10, 15), (larva.MinLevel, larva.MaxLevel));
    }

    [FactIfGameData]
    public void Installed_catalogue_lists_a_complete_expedition_selection()
    {
        using var db = GameDb.TryOpen(RealGameData.Path!);
        Assert.NotNull(db);
        var areas = db.ExpeditionAreas();
        Assert.NotEmpty(areas);
        Assert.Contains(areas, a => db.ExpeditionSubAreas(a.Id).Any(s => db.ExpeditionMonsters(s.Id).Count > 0));
    }
    [FactIfGameData]
    public void Installed_astrub_farm_route_keeps_the_atlas_and_excludes_visited_workshops()
    {
        using var db = GameDb.TryOpen(RealGameData.Path!)!;
        var positions = db.AllMapPositions();
        var maps = db.ExpeditionMaps(95).Where(positions.ContainsKey)
            .Select(id => (id, ((int, int)?)(positions[id].X, positions[id].Y), (int?)positions[id].World)).ToList();
        var route = GivreClone.App.Services.ExpeditionScript.CleanMaps(maps);
        Assert.NotEmpty(route);
        Assert.Contains(191102980L, route);
        Assert.Contains(188745222L, route);
        Assert.DoesNotContain(192939016L, route);
        Assert.DoesNotContain(83886090L, route);
        Assert.All(route, id => Assert.True(positions[id].World >= 0));
    }
}
