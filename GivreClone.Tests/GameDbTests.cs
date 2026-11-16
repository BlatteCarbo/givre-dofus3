using GivreClone.App.Services.LotM;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Fait SAUTER (et non réussir) un test d'intégration quand la vraie base de données du jeu est absente de cette machine.</summary>
public sealed class FactIfGameDataAttribute : FactAttribute
{
    public FactIfGameDataAttribute() { if (RealGameData.Path is null) Skip = "base dofus.sqlite réelle absente ou non lisible (TestRuns/userdata/gamedata, GIVRE_GAMEDATA ou GivreActive/DataSets)"; }
}

internal static class RealGameData
{
    public static readonly string? Path = GameDb.Locate(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GivreReplica", "TestRuns", "givre-app-offline", "userdata"), new[] { @"%USERPROFILE%\Videos\GIVRE\GivreActive" });
}

/// <summary>Base synthétique minimale (mêmes noms de tables/colonnes que dofus.sqlite) : vérifie les requêtes sans dépendre du jeu installé.</summary>
public sealed class FakeGameDb : IDisposable
{
    public readonly string File = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "givre-test-" + Guid.NewGuid().ToString("N") + ".sqlite");

    public FakeGameDb()
    {
        SQLitePCL.Batteries.Init();
        using var c = new SqliteConnection($"Data Source={File}");
        c.Open();
        using var tx = c.BeginTransaction();   // une seule transaction : ~60 requêtes de préparation, sinon une écriture disque par requête
        void X(string sql) { using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        X("create table translations (id text, value text, lang text)");
        X("create table ItemData (id integer, nameId integer, level integer, resourcesBySubarea text)");
        X("create table AchievementData (id integer, nameId integer, level integer, categoryId integer, descriptionId integer, points integer)");
        X("create table AchievementCategoryData (id integer, nameId integer)");
        X("create table AchievementCategoryData_achievementIds_junction (AchievementCategoryData_id text, target_id text)");
        X("create table AchievementObjectiveData (id integer, achievementId integer, nameId integer, \"order\" integer)");
        X("create table AreaData (id integer, nameId integer)");
        X("create table DocumentData (id integer, titleId text, contentId text)");
        X("create table JobData (id integer, nameId integer)");
        X("create table RecipeData (id text, jobId integer, quantities text, resultId integer, resultLevel integer)");
        X("create table RecipeData_ingredientIds_junction (RecipeData_id text, target_id text)");
        X("create table DungeonData (id integer, entranceMapId integer, exitMapId integer, nameId integer, optimalPlayerLevel integer, requiredObjects text, bosses text)");
        X("create table DungeonData_mapIds_junction (DungeonData_id text, target_id text)");
        X("create table MonsterData (id integer, nameId integer, drops text)");
        X("create table ItemData_dropMonsterIds_junction (ItemData_id text, target_id text)");
        X("create table SkillData (id integer, nameId integer, gatheredRessourceItem integer)");
        X("create table SubAreaData (id integer, nameId integer, areaId integer, level integer, associatedZaapMapId integer)");
        X("create table SubAreaData_mapIds_junction (SubAreaData_id text, target_id text)");
        X("create table QuestData (id integer, nameId integer, categoryId integer, levelMin integer, levelMax integer, startCriterion text, startPosition text)");
        X("create table QuestCategoryData (id integer, nameId integer)");
        // textes
        foreach (var (id, fr) in new (int, string)[] { (1, "Blé"), (2, "Farine"), (3, "Pain"), (4, "Paysan"), (5, "Boulanger"), (6, "Succès Alpha"), (7, "Donjon Bouftou"),
                                                       (8, "Clé du Bouftou"), (9, "Bouftou Royal"), (10, "Larve"), (11, "Cueillette"), (12, "Plaine"), (13, "Une quête de blé"),
                                                       (14, "Quête de plaine"), (15, "Principale"), (16, "Annexe"), (17, "Explorateur de la plaine"), (18, "Exploration"), (19, "Visiter la plaine"), (20, "Ramasser du blé"), (21, "Terres"), (22, "Légende"), (23, "<p>Il était <b>une</b> fois</p><pagefeed/><p>un blé &amp; une plaine</p>##png,94##") })
        {
            X($"insert into translations values ('{id}', '{fr.Replace("'", "''")}', 'fr')");
            X($"insert into translations values ('{id}', 'EN-{id}', 'en')");
        }
        X("insert into ItemData values (100, 1, 1, '[{\"values\":{\"Array\":[50,3]}}]'), (101, 2, 5, '[]'), (102, 3, 10, '[]'), (103, 8, 1, '[]')");
        X("insert into AchievementData values (7, 6, 25, 3, 0, 10), (8, 17, 40, 4, 19, 20), (9, 17, 41, 4, 0, 5)");
        X("insert into AchievementCategoryData values (3, 18), (4, 18)");
        X("insert into AchievementCategoryData_achievementIds_junction values ('4','8'), ('4','9'), ('3','7')");
        X("insert into AchievementObjectiveData values (1, 8, 20, 0), (2, 8, 19, 1)");
        X("insert into AreaData values (2, 21)");
        X("insert into DocumentData values (17, '22', '23')");
        X("insert into JobData values (28, 4), (13, 5)");
        // pain (102) = 3 farine + 2 blé, métier boulanger (13) niveau 10
        X("insert into RecipeData values ('r1', 13, '[3,2]', 102, 10), ('r2', 13, '[1]', 101, 3), ('r3', 28, '[4]', 101, 1)");
        X("insert into RecipeData_ingredientIds_junction values ('r1','101'), ('r1','100'), ('r2','100'), ('r3','100')");
        X("insert into DungeonData values (1, 900, 901, 7, 30, '[{\"id\":103,\"quantity\":1}]', '[77]')");
        X("insert into DungeonData_mapIds_junction values ('1','900'), ('1','902'), ('1','903')");
        X("insert into MonsterData values (77, 9, '[]'), (78, 10, '[{\"objectId\":100,\"percentDropForGrade1\":12.5}]')");
        X("insert into ItemData_dropMonsterIds_junction values ('100','78')");
        X("insert into SkillData values (1, 11, 100)");
        X("insert into SubAreaData values (50, 12, 2, 12, 777)");
        X("insert into SubAreaData_mapIds_junction values ('50','5000'), ('50','5001')");
        X("insert into QuestCategoryData values (1, 15), (2, 16)");
        X("insert into QuestData values (10, 13, 1, 5, 20, 'PL>4', '[{\"mapId\":5000,\"npcId\":42}]'), (11, 14, 2, 1, 200, '', '[{\"mapId\":9999,\"npcId\":43}]')");
        X("create table HintCategoryData (id integer, nameId integer)");
        X("create table HintData (id integer, mapId integer, nameId integer, categoryId integer, x integer, y integer)");
        foreach (var (id, fr) in new (int, string)[] { (40, "Ateliers"), (41, "Divers"), (42, "Transports"), (43, "Sortie Est"), (44, "Atelier des bûcherons"), (45, "Zaap"), (46, "Ailleurs") })
            X($"insert into translations values ('{id}', '{fr}', 'fr')");
        X("insert into HintCategoryData values (3, 40), (4, 41), (9, 42)");
        X("insert into HintData values (1, 54169896, 43, 4, -72, -40), (2, 54169896, 44, 3, -72, -40), (3, 54169896, 45, 9, -78, -41), (4, 999, 46, 4, 1, 2)");
        tx.Commit();
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); try { System.IO.File.Delete(File); } catch { } }
}

public class GameDbTests : IDisposable
{
    private readonly FakeGameDb _fake = new();
    private readonly GameDb _db;
    public GameDbTests() => _db = GameDb.TryOpen(_fake.File) ?? throw new InvalidOperationException("base synthétique non ouverte");
    public void Dispose() { _db.Dispose(); _fake.Dispose(); }

    [Fact]
    public void Texts_come_from_the_requested_language_and_missing_ids_are_null()
    {
        Assert.Equal("Blé", _db.Text(1));
        Assert.Null(_db.Text(999));
        Assert.Null(_db.Text(0));
        using var en = GameDb.TryOpen(_fake.File, "en")!;
        Assert.Equal("EN-1", en.Text(1));
    }

    [Fact]
    public void Open_refuses_missing_empty_or_unrelated_files()
    {
        Assert.Null(GameDb.TryOpen(Path.Combine(Path.GetTempPath(), "n'existe-pas.sqlite")));
        var junk = Path.Combine(Path.GetTempPath(), "junk-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(junk, new byte[8192]);
        try { Assert.Null(GameDb.TryOpen(junk)); } finally { File.Delete(junk); }
    }

    [Fact]
    public void The_database_is_opened_read_only()
    {
        // écrire doit échouer : on ne modifie jamais la base du jeu
        var field = typeof(GameDb).GetField("_c", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var conn = (SqliteConnection)field.GetValue(_db)!;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "insert into JobData values (99, 1)";
        Assert.Throws<SqliteException>(() => cmd.ExecuteNonQuery());
    }

    [Fact]
    public void Generic_search_reads_late_items_and_paginates_matching_results()
    {
        // Dépasse le plafond historique 5 000 : la cible est sur la dernière page.
        // Écrit uniquement la base de test synthétique, jamais une base du jeu.
        using (var writer = new SqliteConnection($"Data Source={_fake.File}"))
        {
            writer.Open();
            using var command = writer.CreateCommand();
            command.CommandText = @"with recursive ids(n) as
                (select 1000 union all select n + 1 from ids where n < 6300)
                insert into ItemData(id, nameId, level, resourcesBySubarea)
                select n, 1, 1, '[]' from ids";
            command.ExecuteNonQuery();
            command.CommandText = "insert into ItemData values(6501,6501,1,'[]')";
            command.ExecuteNonQuery();
            command.CommandText = "insert into translations values('6501','Zirconium après 5000','fr')";
            command.ExecuteNonQuery();
        }

        Assert.Equal(6501L, _db.Find("items", new[] { "id=6501" }, 1).Single()["id"]);
        Assert.Equal(6501L, _db.SearchText("items", "Zirconium", 2).Single()["id"]);
        Assert.Equal(new[] { 6298L, 6299L }, _db.FindPage("items", new[] { "id>=6298" }, 0, 2)
            .Select(row => (long)row["id"]!));
        Assert.Equal(new[] { 6300L, 6501L }, _db.FindPage("items", new[] { "id>=6298" }, 1, 2)
            .Select(row => (long)row["id"]!));
        Assert.Empty(_db.FindPage("items", new[] { "id>=6298" }, 2, 2));
        Assert.Equal(6501L, _db.SearchTextPage("items", "Zirconium", 0, 5).Single()["id"]);
        Assert.Empty(_db.SearchTextPage("items", "Zirconium", 1, 5));
        Assert.Empty(_db.FindPage("items", Array.Empty<string>(), -1, 5));
        var cappedFirst = _db.FindPage("items", new[] { "id>=1000" }, 0, 6000);
        var cappedSecond = _db.FindPage("items", new[] { "id>=1000" }, 1, 6000);
        Assert.Equal(5000, cappedFirst.Count);
        Assert.Equal(302, cappedSecond.Count);
        Assert.Equal(6501L, cappedSecond.Last()["id"]);
    }

    [Fact]
    public void Tables_are_listed_and_resolved_by_tolerant_names()
    {
        Assert.Contains("ItemData", _db.Tables());
        Assert.Equal("ItemData", _db.ResolveTable("items"));
        Assert.Equal("ItemData", _db.ResolveTable("itemdata"));
        Assert.Equal("RecipeData", _db.ResolveTable("recipes"));
        Assert.Equal("QuestData", _db.ResolveTable("Quest"));
        Assert.Null(_db.ResolveTable("nope"));
        Assert.Null(_db.ResolveTable("x\"; drop table ItemData; --"));   // pas d'injection : uniquement des noms connus
    }

    [Fact]
    public void Generic_row_by_id()
    {
        var row = _db.Row("items", "100")!;
        Assert.Equal(1L, row["nameId"]);
        Assert.Null(_db.Row("items", "123456"));
        Assert.Null(_db.Row("nope", "1"));
    }

    [Fact]
    public void Shared_database_supports_concurrent_bot_and_ui_reads()
    {
        System.Threading.Tasks.Parallel.For(0, 8, worker =>
        {
            for (int i = 0; i < 200; i++)
            {
                Assert.Equal(100L, _db.ItemGidByName("Blé"));
                Assert.Equal("Blé", _db.ItemName(100));
                Assert.Equal(1L, _db.Row("items", "100")!["nameId"]);
                Assert.Equal(100L, _db.Find("items", new[] { "id=100" }, 1).Single()["id"]);
            }
        });
    }

    [Fact]
    public void Achievement_and_item_names()
    {
        var a = _db.Achievement(7)!;
        Assert.Equal(("Succès Alpha", 25, 3), (a.Name, a.Level, a.CategoryId));
        Assert.Null(_db.Achievement(999));
        Assert.Equal("Pain", _db.ItemName(102));
        Assert.Null(_db.ItemName(1));
        Assert.Equal(102L, _db.ItemGidByName("pain"));          // exact, casse ignorée
        Assert.Equal(100L, _db.ItemGidByName("bl"));            // contient
        Assert.Null(_db.ItemGidByName("inexistant"));
        Assert.Null(_db.ItemGidByName(""));
    }

    [Fact]
    public void Recipe_pairs_each_ingredient_with_its_quantity_in_order()
    {
        var r = _db.Recipe(102)!;
        Assert.Equal(("Pain", 10, 13, "Boulanger"), (r.Name, r.Level, r.JobId, r.Job));
        Assert.Equal(new[] { ("Farine", 3), ("Blé", 2) }, r.Ingredients.Select(i => (i.Name, i.Quantity)));
        Assert.Null(_db.Recipe(100));   // ne se fabrique pas
    }

    [Fact]
    public void Job_recipes_are_filtered_by_level_and_sorted_highest_first()
    {
        Assert.Equal(new[] { 102L, 101L }, _db.RecipesOfJob(13, 10).Select(r => r.ResultId));
        Assert.Equal(new[] { 101L }, _db.RecipesOfJob(13, 5).Select(r => r.ResultId));
        Assert.Empty(_db.RecipesOfJob(13, 2));
        Assert.Equal("Boulanger", _db.Job("boul")!.Name);
        Assert.Equal(28, _db.Job("28")!.Id);
        Assert.Null(_db.Job("inconnu"));
    }

    [Fact]
    public void Dungeon_lookup_by_map_name_and_id_with_rooms_keys_and_bosses()
    {
        var d = _db.DungeonOfMap(902)!;
        Assert.Equal(("Donjon Bouftou", 900L, 901L, 30), (d.Name, d.EntranceMapId, d.ExitMapId, d.OptimalLevel));
        Assert.Equal(new[] { 900L, 902L, 903L }, d.MapIds);                 // salles dans l'ordre de la base
        Assert.Equal(2, d.MapIds.ToList().IndexOf(903) );                   // la salle de la carte = index + 1
        Assert.Equal(("Clé du Bouftou", 1), (d.Keys.Single().Name, d.Keys.Single().Quantity));
        Assert.Equal("Bouftou Royal", d.Bosses.Single().Name);
        Assert.Null(_db.DungeonOfMap(1234));
        Assert.Equal(1, _db.DungeonByName("bouftou")!.Id);
        Assert.Null(_db.DungeonByName("zzz"));
    }

    [Fact]
    public void Item_sources_craft_harvest_and_monster_drops()
    {
        var s = _db.Sources(100)!;
        Assert.Null(s.Craft);   // le blé ne se fabrique pas
        Assert.Equal(new[] { "Cueillette" }, s.HarvestSkills);
        Assert.Equal(new[] { "Plaine" }, s.HarvestZones);
        var m = Assert.Single(s.Monsters);
        Assert.Equal(("Larve", 12.5), (m.Name, m.DropRate));
        var bread = _db.Sources(102)!;
        Assert.Equal(("Boulanger", 10), bread.Craft!.Value);
        Assert.Null(_db.Sources(5));
    }

    [Fact]
    public void Quests_are_found_by_name_category_and_start_zone()
    {
        Assert.Equal(new[] { 10, 11 }, _db.FindQuests("qu", null, null, 10).Select(q => q.Id));
        Assert.Equal(new[] { 10 }, _db.FindQuests("blé", null, null, 10).Select(q => q.Id));
        Assert.Equal(new[] { 11 }, _db.FindQuests(null, "annexe", null, 10).Select(q => q.Id));
        Assert.Equal(new[] { 10 }, _db.FindQuests(null, null, "plaine", 10).Select(q => q.Id));   // départ sur une carte de la sous-zone « Plaine »
        Assert.Single(_db.FindQuests(null, null, null, 1));
        var q10 = _db.Quest(10)!;
        Assert.Equal(("Une quête de blé", "Principale", 5, 20, "PL>4"), (q10.Name, q10.Category, q10.LevelMin, q10.LevelMax, q10.StartCriterion));
        Assert.Equal((5000L, 42), q10.StartPositions.Single());
        Assert.Null(_db.Quest(999));
    }

    [Fact]
    public void Achievement_search_matches_all_words_in_name_description_or_category()
    {
        Assert.Equal(new[] { 8, 9 }, _db.FindAchievements("plaine", 10).Select(a => a.Id));          // nom
        Assert.Equal(new[] { 8 }, _db.FindAchievements("visiter", 10).Select(a => a.Id));            // description
        Assert.Equal(new[] { 7, 8, 9 }, _db.FindAchievements("exploration", 10).Select(a => a.Id));  // catégorie
        Assert.Equal(new[] { 8 }, _db.FindAchievements("exploration visiter", 10).Select(a => a.Id)); // tous les mots
        Assert.Empty(_db.FindAchievements("zzz", 10));
        Assert.Equal(3, _db.FindAchievements("", 10).Count);
        Assert.Single(_db.FindAchievements("", 1));
        var r = _db.FindAchievements("plaine", 1).Single();
        Assert.Equal(("Explorateur de la plaine", "Exploration", 20, 40), (r.Name, r.Category, r.Points, r.Level));
    }

    [Fact]
    public void Achievement_category_and_objectives()
    {
        Assert.Equal(new[] { 8, 9 }, _db.AchievementsOfCategory(4).Select(a => a.Id));
        Assert.Empty(_db.AchievementsOfCategory(99));
        Assert.Equal(new[] { "Ramasser du blé", "Visiter la plaine" }, _db.AchievementObjectives(8).Select(o => o.Name));
        Assert.Empty(_db.AchievementObjectives(7));
    }

    [Fact]
    public void Sub_area_info_and_lookup_by_name_or_id()
    {
        var s = _db.SubArea(50)!;
        Assert.Equal(("Plaine", "Terres", 12, 777L), (s.Name, s.Area, s.Level, s.ZaapMapId));
        Assert.Null(_db.SubArea(51));
        Assert.Equal(50, _db.SubAreaIdByName("plaine"));
        Assert.Equal(50, _db.SubAreaIdByName("lain"));
        Assert.Equal(50, _db.SubAreaIdByName("50"));
        Assert.Null(_db.SubAreaIdByName("51"));
        Assert.Null(_db.SubAreaIdByName("inconnue"));
        Assert.Null(_db.SubAreaIdByName(""));
    }

    [Fact]
    public void Map_hints_are_read_for_the_requested_maps_only()
    {
        var hints = _db.MapHints(new long[] { 54169896 });
        Assert.NotNull(hints);
        Assert.Equal(3, hints!.Count);
        Assert.Equal(new[] { "Sortie Est", "Atelier des bûcherons", "Zaap" }, hints.Select(h => h.Name).ToArray());
        Assert.Equal(new[] { 4, 3, 9 }, hints.Select(h => h.CategoryId).ToArray());
        Assert.Equal(new[] { "Divers", "Ateliers", "Transports" }, hints.Select(h => h.Category).ToArray());
        Assert.Equal((-72, -40), (hints[0].X, hints[0].Y));
        Assert.DoesNotContain(hints, h => h.MapId == 999);
        Assert.Empty(_db.MapHints(new long[] { 1 })!);
        Assert.Empty(_db.MapHints(Array.Empty<long>())!);
    }

    [Fact]
    public void Documents_are_returned_as_plain_text()
    {
        var d = _db.Document(17)!;
        Assert.Equal("Légende", d.Title);
        Assert.Equal("Il était une fois\nun blé & une plaine", d.Text);
        Assert.Null(_db.Document(18));
    }

    [Fact]
    public void Locate_prefers_the_environment_then_local_data_then_the_newest_dataset()
    {
        var root = Path.Combine(Path.GetTempPath(), "givre-loc-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "DataSets", "3.7.5.5")); File.WriteAllText(Path.Combine(root, "DataSets", "3.7.5.5", "dofus.sqlite"), "x");
            Directory.CreateDirectory(Path.Combine(root, "DataSets", "3.7.7.6")); File.WriteAllText(Path.Combine(root, "DataSets", "3.7.7.6", "dofus.sqlite"), "x");
            Directory.CreateDirectory(Path.Combine(root, "DataSets", "3.10.0.1")); // sans fichier : ignoré
            Assert.EndsWith(Path.Combine("3.7.7.6", "dofus.sqlite"), GameDb.Locate(Path.Combine(root, "nodata"), new[] { root }));
            Directory.CreateDirectory(Path.Combine(root, "data", "gamedata")); File.WriteAllText(Path.Combine(root, "data", "gamedata", "dofus.sqlite"), "x");
            Assert.EndsWith(Path.Combine("gamedata", "dofus.sqlite"), GameDb.Locate(Path.Combine(root, "data"), new[] { root }));   // local avant les jeux de données
            Assert.Null(GameDb.Locate(Path.Combine(root, "vide"), new[] { Path.Combine(root, "inconnu") }));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}

public class GameDbRealDataTests
{
    [FactIfGameData]
    public void The_real_game_database_answers_the_questions_the_lua_api_asks()
    {
        using var db = GameDb.TryOpen(RealGameData.Path!)!;
        Assert.NotNull(db);
        Assert.True(db.Tables().Count > 200);
        // métier + recette connus (vérifiés à la main dans la base 3.7.7.6 : Pain d'Incarnam = 4 Blé, métier Paysan)
        Assert.Contains(db.Jobs(), j => j.Name == "Paysan");
        var bread = db.Recipe(db.ItemGidByName("Pain d'Incarnam")!.Value)!;
        Assert.Equal("Paysan", bread.Job);
        Assert.Equal(("Blé", 4), (bread.Ingredients.Single().Name, bread.Ingredients.Single().Quantity));
        // donjon : la première salle appartient bien à son donjon
        var d = db.DungeonById(1)!;
        Assert.Equal("Cour du Bouftou Royal", d.Name);
        Assert.Equal(d.Id, db.DungeonOfMap(d.MapIds[0])!.Id);
        Assert.True(d.Keys.Count > 0 && d.Bosses.Count > 0);
        // succès et quêtes
        Assert.False(string.IsNullOrEmpty(db.Achievement(3)!.Name));
        Assert.NotEmpty(db.FindQuests("a", null, null, 5));
        // sources : un objet qui tombe d'un monstre
        var larve = db.Sources(21171);
        Assert.NotNull(larve);
        Assert.NotEmpty(larve!.Monsters);
        Assert.True(db.Tables().Contains("translations"));
        Assert.NotNull(db.Row("items", "21171"));
    }
}

public class HtmlTextTests
{
    [Fact]
    public void Strips_tags_page_feeds_images_and_entities()
    {
        Assert.Equal("a\nb", HtmlText.Strip("<p>a</p><pagefeed/><p>b</p>"));
        Assert.Equal("x & y", HtmlText.Strip("<b>x</b> &amp; y##png,94##"));
        Assert.Equal("l1\nl2", HtmlText.Strip("<li>l1</li><li>l2</li>"));
        Assert.Equal("", HtmlText.Strip(""));
        Assert.Equal("a b", HtmlText.Strip("a     b"));
    }
}
