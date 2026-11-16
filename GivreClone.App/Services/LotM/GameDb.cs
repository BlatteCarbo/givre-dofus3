using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace GivreClone.App.Services.LotM;

public sealed record RecipeIngredient(long Gid, string Name, int Quantity);
public sealed record RecipeInfo(long ResultId, string Name, int Level, int JobId, string Job, IReadOnlyList<RecipeIngredient> Ingredients);
public sealed record JobInfo(int Id, string Name);
public sealed record DungeonKey(long Gid, int Quantity, string Name);
public sealed record DungeonInfo(int Id, string Name, IReadOnlyList<long> MapIds, long EntranceMapId, long ExitMapId, int OptimalLevel,
                                 IReadOnlyList<DungeonKey> Keys, IReadOnlyList<(int Id, string Name)> Bosses);
public sealed record MonsterSource(int MonsterId, string Name, double DropRate);
public sealed record ItemSources(long Gid, string Name, (string Job, int Level)? Craft, IReadOnlyList<string> HarvestSkills,
                                 IReadOnlyList<string> HarvestZones, IReadOnlyList<MonsterSource> Monsters);
public sealed record QuestSummary(int Id, string Name, int CategoryId, string Category, int LevelMin, int LevelMax, string StartCriterion,
                                  IReadOnlyList<(long MapId, int NpcId)> StartPositions);
public sealed record AchievementInfo(int Id, string Name, int Level, int CategoryId);
public sealed record AchievementRow(int Id, string Name, string Category, int Points, int Level);
public sealed record SubAreaInfo(int Id, string Name, int AreaId, string Area, int Level, long ZaapMapId);
public sealed record DocumentInfo(int Id, string Title, string Text);

/// <summary>
/// Accès en LECTURE SEULE à la base de données du jeu extraite du client (dofus.sqlite : succès, objets, recettes, métiers, donjons,
/// quêtes, traductions). Pas un clone de l'original : c'est la base du jeu installé, la même source que d'autres outils lisent.
/// Les noms viennent de la table <c>translations</c> (id texte, langue). Version de la base : voir <see cref="Path"/> / son manifeste.
/// </summary>
public sealed partial class GameDb : IDisposable
{
    private static readonly object InitGate = new();
    private static bool _init;

    private readonly SqliteConnection _c;
    private readonly string _lang;
    private readonly Dictionary<string, string?> _textCache = new();
    public string Path { get; }

    private GameDb(SqliteConnection c, string path, string lang) { _c = c; Path = path; _lang = lang; }

    /// <summary>Ouvre la base en lecture seule ; null si le fichier est absent, vide ou illisible.</summary>
    public static GameDb? TryOpen(string path, string lang = "fr")
    {
        SqliteConnection? c = null;
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length < 4096) return null;
            lock (InitGate) { if (!_init) { SQLitePCL.Batteries.Init(); _init = true; } }
            // Pooling désactivé : fermer la connexion libère le fichier (sinon une base refusée resterait verrouillée).
            var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
            c = new SqliteConnection(cs);
            c.Open();
            using var probe = c.CreateCommand();
            probe.CommandText = "select count(*) from sqlite_master where name in ('translations','ItemData')";
            if (Convert.ToInt32(probe.ExecuteScalar(), CultureInfo.InvariantCulture) < 2) { c.Dispose(); return null; }
            return new GameDb(c, path, lang);
        }
        catch { c?.Dispose(); return null; }
    }

    /// <summary>Cherche la base : variable GIVRE_GAMEDATA, puis « gamedata/dofus.sqlite » sous <paramref name="dataRoot"/>, puis les jeux de
    /// données les plus récents sous les dossiers donnés (…/DataSets/&lt;version&gt;/dofus.sqlite). Null si introuvable.</summary>
    public static string? Locate(string dataRoot, IEnumerable<string>? searchRoots = null)
    {
        var env = Environment.GetEnvironmentVariable("GIVRE_GAMEDATA");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
        var local = System.IO.Path.Combine(dataRoot, "gamedata", "dofus.sqlite");
        if (File.Exists(local)) return local;
        foreach (var root in searchRoots ?? Array.Empty<string>())
        {
            try
            {
                var sets = System.IO.Path.Combine(root, "DataSets");
                if (!Directory.Exists(sets)) continue;
                var best = Directory.EnumerateDirectories(sets).Select(d => (Dir: d, Ver: VersionKey(System.IO.Path.GetFileName(d))))
                                    .OrderByDescending(x => x.Ver).Select(x => System.IO.Path.Combine(x.Dir, "dofus.sqlite")).FirstOrDefault(File.Exists);
                if (best is not null) return best;
            }
            catch { /* dossier illisible : suivant */ }
        }
        return null;
    }

    private static long VersionKey(string v)
    {
        long k = 0;
        foreach (var p in v.Split('.').Take(4)) k = k * 1000 + (long.TryParse(p, out var n) ? n : 0);
        return k;
    }

    public void Dispose() { lock (_c) _c.Dispose(); }

    // ---------------------------------------------------------------- bas niveau
    private IEnumerable<object?[]> Query(string sql, params object?[] args)
    {
        // Microsoft.Data.Sqlite does not support concurrent use of a connection,
        // including command creation and disposal. Materialize before releasing it.
        lock (_c)
        {
            using var cmd = _c.CreateCommand();
            cmd.CommandText = sql;
            for (int i = 0; i < args.Length; i++) cmd.Parameters.AddWithValue("$" + i, args[i] ?? DBNull.Value);
            using var r = cmd.ExecuteReader();
            var rows = new List<object?[]>();
            while (r.Read())
            {
                var row = new object?[r.FieldCount];
                for (int i = 0; i < row.Length; i++) row[i] = r.IsDBNull(i) ? null : r.GetValue(i);
                rows.Add(row);
            }
            return rows;
        }
    }

    private object? Scalar(string sql, params object?[] args) => Query(sql, args).FirstOrDefault()?[0];
    private static long L(object? o) => o is null ? 0 : Convert.ToInt64(o, CultureInfo.InvariantCulture);
    private static int I(object? o) => (int)L(o);
    private static string S(object? o) => o is null ? "" : Convert.ToString(o, CultureInfo.InvariantCulture) ?? "";

    /// <summary>Texte traduit d'un identifiant de nom (nameId) dans la langue de la base ouverte ; null si absent.</summary>
    public string? Text(long nameId)
    {
        if (nameId <= 0) return null;
        lock (_c)
        {
            var key = nameId.ToString(CultureInfo.InvariantCulture);
            if (_textCache.TryGetValue(key, out var hit)) return hit;
            var v = Scalar("select value from translations where id = $0 and lang = $1", key, _lang) as string;
            if (_textCache.Count > 5000) _textCache.Clear();
            return _textCache[key] = v;
        }
    }

    private string NameOf(string table, long id)
    {
        if (!IsKnownTable(table)) return "";
        return Text(L(Scalar($"select nameId from \"{table}\" where id = $0", id))) ?? "";
    }

    // ---------------------------------------------------------------- data:tables / data:get
    private HashSet<string>? _tables;
    public IReadOnlyList<string> Tables()
    {
        lock (_c)
        {
            _tables ??= Query("select name from sqlite_master where type = 'table' and name not like 'sqlite_%' order by name").Select(r => S(r[0])).ToHashSet();
            return _tables.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public bool IsKnownTable(string table) { Tables(); return _tables!.Contains(table); }

    /// <summary>Nom exact d'une table, insensible à la casse et à un « Data » final (« items » → « ItemData »). Null si inconnu.</summary>
    public string? ResolveTable(string name)
    {
        var all = Tables();
        string N(string s) => s.Replace("_", "").ToLowerInvariant();
        var n = N(name);
        return all.FirstOrDefault(t => N(t) == n) ?? all.FirstOrDefault(t => N(t) == n + "data") ?? all.FirstOrDefault(t => N(t) == n.TrimEnd('s') + "data");
    }

    /// <summary>Ligne brute d'une table par `id` (colonnes → valeurs ; JSON laissé en texte). Null si absente.</summary>
    public IReadOnlyDictionary<string, object?>? Row(string table, string id)
    {
        if (ResolveTable(table) is not { } t) return null;
        lock (_c)
        {
            using var cmd = _c.CreateCommand();
            cmd.CommandText = $"select * from \"{t}\" where id = $0 limit 1";
            cmd.Parameters.AddWithValue("$0", long.TryParse(id, out var n) ? n : id);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            var d = new Dictionary<string, object?>();
            for (int i = 0; i < r.FieldCount; i++) d[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            return d;
        }
    }

    // ---------------------------------------------------------------- data:find / data:search
    private const int ScanCap = 5000;   // maximum de RÉSULTATS retournés par appel, jamais un plafond de données parcourues
    private const int ScanPageSize = 1024;

    private IEnumerable<IReadOnlyDictionary<string, object?>> Scan(string table)
    {
        if (ResolveTable(table) is not { } t) yield break;
        // Le jeu dépasse largement 5 000 objets : chaque page est lue en entier
        // avant de passer à la suivante. Une correspondance après la page 5 n'est
        // ainsi jamais silencieusement omise. ResolveTable limite le nom SQL.
        for (long offset = 0; ; offset += ScanPageSize)
        {
            var page = new List<IReadOnlyDictionary<string, object?>>(ScanPageSize);
            lock (_c)
            {
                using var cmd = _c.CreateCommand();
                cmd.CommandText = $"select * from \"{t}\" order by rowid limit $limit offset $offset";
                cmd.Parameters.AddWithValue("$limit", ScanPageSize);
                cmd.Parameters.AddWithValue("$offset", offset);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var d = new Dictionary<string, object?>();
                    for (int i = 0; i < r.FieldCount; i++) d[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                    page.Add(d);
                }
            }
            foreach (var row in page) yield return row;
            if (page.Count < ScanPageSize) yield break;
        }
    }

    /// <summary>Valeurs candidates d'un champ à chemin pointé (« a.b.c ») ; un tableau JSON donne tous ses éléments.</summary>
    private static List<string> FieldValues(IReadOnlyDictionary<string, object?> row, string path)
    {
        var res = new List<string>();
        var parts = path.Split('.');
        if (!row.TryGetValue(parts[0], out var v) || v is null) return res;
        var text = Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        bool json = text.Length > 1 && (text[0] == '[' || text[0] == '{');
        if (parts.Length == 1 && !json) { res.Add(text); return res; }
        try { using var doc = System.Text.Json.JsonDocument.Parse(text); Walk(doc.RootElement, parts, 1, res); }
        catch (System.Text.Json.JsonException) { /* pas du JSON : champ ignoré */ }
        return res;
    }

    private static void Walk(System.Text.Json.JsonElement e, string[] parts, int i, List<string> res)
    {
        if (e.ValueKind == System.Text.Json.JsonValueKind.Array) { foreach (var item in e.EnumerateArray()) Walk(item, parts, i, res); return; }
        if (i < parts.Length)
        {
            if (e.ValueKind == System.Text.Json.JsonValueKind.Object && e.TryGetProperty(parts[i], out var child)) Walk(child, parts, i + 1, res);
            return;
        }
        res.Add(e.ValueKind == System.Text.Json.JsonValueKind.String ? e.GetString() ?? "" : e.GetRawText());
    }

    private static readonly string[] Ops = { ">=", "<=", "!=", "=", ">", "<", "~" };

    private static bool FilterHolds(IReadOnlyDictionary<string, object?> row, string filter)
    {
        foreach (var op in Ops)
        {
            int at = filter.IndexOf(op, StringComparison.Ordinal);
            if (at <= 0) continue;
            string field = filter[..at].Trim(), want = filter[(at + op.Length)..].Trim();
            bool numeric = double.TryParse(want, NumberStyles.Float, CultureInfo.InvariantCulture, out var wn);
            var values = FieldValues(row, field);
            bool Test(string got)
            {
                bool gotNum = double.TryParse(got, NumberStyles.Float, CultureInfo.InvariantCulture, out var gn);
                int cmp = numeric && gotNum ? gn.CompareTo(wn) : string.Compare(got, want, StringComparison.OrdinalIgnoreCase);
                return op switch
                {
                    "=" => cmp == 0, "!=" => cmp != 0, ">" => cmp > 0, ">=" => cmp >= 0, "<" => cmp < 0, "<=" => cmp <= 0,
                    _ => got.Contains(want, StringComparison.OrdinalIgnoreCase),
                };
            }
            // « != » tient seulement si AUCUN élément n'est égal ; les autres opérateurs, si UN élément convient.
            return op == "!=" ? values.Count > 0 && values.All(Test) : values.Any(Test);
        }
        return false;
    }

    /// <summary>Lignes d'une table qui vérifient TOUS les filtres « champ op valeur » (op : = != &gt; &gt;= &lt; &lt;= ~). Table inconnue → liste vide.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Find(string table, IReadOnlyList<string> filters, int limit)
        => Scan(table).Where(r => filters.All(f => FilterHolds(r, f))).Take(Math.Clamp(limit, 1, ScanCap)).ToList();

    /// <summary>Page de correspondances parmi TOUTES les lignes du jeu (page 0 = première).
    /// La pagination porte sur les résultats filtrés, pas sur les lignes brutes.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> FindPage(string table, IReadOnlyList<string> filters, int page, int pageSize)
    {
        if (page < 0 || pageSize <= 0) return Array.Empty<IReadOnlyDictionary<string, object?>>();
        int boundedPageSize = Math.Min(pageSize, ScanCap);
        long skip = (long)page * boundedPageSize;
        if (skip > int.MaxValue) return Array.Empty<IReadOnlyDictionary<string, object?>>();
        return Scan(table).Where(r => filters.All(f => FilterHolds(r, f)))
            .Skip((int)skip).Take(boundedPageSize).ToList();
    }

    /// <summary>Lignes dont le nom ou la description traduits contiennent le texte cherché (insensible à la casse).</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> SearchText(string table, string text, int limit)
        => Scan(table).Where(r => new[] { "nameId", "descriptionId" }.Any(c => r.TryGetValue(c, out var v) && v is not null && Text(L(v)) is { } t && t.Contains(text, StringComparison.OrdinalIgnoreCase)))
                      .Take(Math.Clamp(limit, 1, ScanCap)).ToList();

    /// <summary>Recherche textuelle paginée exhaustive, y compris les objets après la 5000e ligne.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> SearchTextPage(string table, string text, int page, int pageSize)
    {
        if (page < 0 || pageSize <= 0) return Array.Empty<IReadOnlyDictionary<string, object?>>();
        int boundedPageSize = Math.Min(pageSize, ScanCap);
        long skip = (long)page * boundedPageSize;
        if (skip > int.MaxValue) return Array.Empty<IReadOnlyDictionary<string, object?>>();
        return Scan(table).Where(r => new[] { "nameId", "descriptionId" }
                .Any(c => r.TryGetValue(c, out var v) && v is not null && Text(L(v)) is { } t && t.Contains(text, StringComparison.OrdinalIgnoreCase)))
            .Skip((int)skip).Take(boundedPageSize).ToList();
    }

    // ---------------------------------------------------------------- succès
    public AchievementInfo? Achievement(int id)
        => Query("select nameId, level, categoryId from AchievementData where id = $0", id).FirstOrDefault() is { } r
            ? new AchievementInfo(id, Text(L(r[0])) ?? "", I(r[1]), I(r[2])) : null;

    /// <summary>Succès dont le nom, la description ou la catégorie contient TOUS les mots de la requête (au plus <paramref name="max"/>).</summary>
    public IReadOnlyList<AchievementRow> FindAchievements(string query, int max)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var args = new List<object?> { _lang };
        var conds = new List<string>();
        foreach (var w in words) { args.Add("%" + w + "%"); conds.Add($"(tn.value like ${args.Count - 1} or ifnull(td.value,'') like ${args.Count - 1} or ifnull(tc.value,'') like ${args.Count - 1})"); }
        var sql = "select a.id, a.nameId, a.categoryId, a.points, a.level from AchievementData a " +
                  "join translations tn on tn.id = cast(a.nameId as text) and tn.lang = $0 " +
                  "left join translations td on td.id = cast(a.descriptionId as text) and td.lang = $0 " +
                  "left join AchievementCategoryData c on c.id = a.categoryId " +
                  "left join translations tc on tc.id = cast(c.nameId as text) and tc.lang = $0 " +
                  (conds.Count > 0 ? "where " + string.Join(" and ", conds) : "") + $" order by a.id limit {Math.Clamp(max, 1, 500)}";
        return Query(sql, args.ToArray()).Select(r => new AchievementRow(I(r[0]), Text(L(r[1])) ?? "", CategoryName(I(r[2])), I(r[3]), I(r[4]))).ToList();
    }

    private string CategoryName(int categoryId) => Text(L(Scalar("select nameId from AchievementCategoryData where id = $0", categoryId))) ?? "";

    public IReadOnlyList<AchievementRow> AchievementsOfCategory(int categoryId)
        => Query("select a.id, a.nameId, a.categoryId, a.points, a.level from AchievementCategoryData_achievementIds_junction j join AchievementData a on a.id = cast(j.target_id as integer) where j.AchievementCategoryData_id = $0 order by a.id",
                 categoryId.ToString(CultureInfo.InvariantCulture)).Select(r => new AchievementRow(I(r[0]), Text(L(r[1])) ?? "", CategoryName(I(r[2])), I(r[3]), I(r[4]))).ToList();

    /// <summary>Objectifs d'un succès (id, texte). La progression chiffrée n'est pas dans la base : elle vient du serveur.</summary>
    public IReadOnlyList<(int Id, int NameId, string Name)> AchievementObjectives(int achievementId)
        => Query("select id, nameId from AchievementObjectiveData where achievementId = $0 order by \"order\", id", achievementId).Select(r => (I(r[0]), I(r[1]), Text(L(r[1])) ?? "")).ToList();

    // ---------------------------------------------------------------- sous-zones
    public SubAreaInfo? SubArea(int id)
        => Query("select nameId, areaId, level, associatedZaapMapId from SubAreaData where id = $0", id).FirstOrDefault() is { } r
            ? new SubAreaInfo(id, Text(L(r[0])) ?? "", I(r[1]), Text(L(Scalar("select nameId from AreaData where id = $0", I(r[1])))) ?? "", I(r[2]), L(r[3])) : null;

    /// <summary>Sous-zone par id (texte numérique) ou par nom (exact insensible à la casse, sinon contient).</summary>
    public int? SubAreaIdByName(string nameOrId)
    {
        if (string.IsNullOrWhiteSpace(nameOrId)) return null;
        if (int.TryParse(nameOrId, out var id)) return SubArea(id) is null ? null : id;
        var n = nameOrId.Trim();
        const string sql = "select s.id from SubAreaData s join translations t on t.id = cast(s.nameId as text) and t.lang = $1 where WHERE order by length(t.value) limit 1";
        var hit = Query(sql.Replace("WHERE", "lower(t.value) = lower($0)"), n, _lang).FirstOrDefault()
               ?? Query(sql.Replace("WHERE", "t.value like $0"), "%" + n + "%", _lang).FirstOrDefault();
        return hit is null ? null : I(hit[0]);
    }

    // ---------------------------------------------------------------- documents
    public DocumentInfo? Document(int id)
        => Query("select titleId, contentId from DocumentData where id = $0", id).FirstOrDefault() is { } r
            ? new DocumentInfo(id, Text(L(r[0])) ?? "", HtmlText.Strip(Text(L(r[1])) ?? "")) : null;

    // ---------------------------------------------------------------- objets
    public string? ItemName(long gid) => Text(L(Scalar("select nameId from ItemData where id = $0", gid)));

    /// <summary>Gid d'un objet par nom (exact insensible à la casse, sinon premier nom contenant la requête). Null si introuvable.</summary>
    public long? ItemGidByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var exact = Query("select i.id from ItemData i join translations t on t.id = cast(i.nameId as text) and t.lang = $1 where lower(t.value) = lower($0) limit 1", name.Trim(), _lang).FirstOrDefault();
        if (exact is not null) return L(exact[0]);
        var like = Query("select i.id from ItemData i join translations t on t.id = cast(i.nameId as text) and t.lang = $1 where t.value like $0 order by length(t.value) limit 1", "%" + name.Trim() + "%", _lang).FirstOrDefault();
        return like is null ? null : L(like[0]);
    }

    // ---------------------------------------------------------------- métiers et recettes
    public IReadOnlyList<JobInfo> Jobs() => Query("select id, nameId from JobData order by id").Select(r => new JobInfo(I(r[0]), Text(L(r[1])) ?? "")).ToList();

    public JobInfo? Job(string idOrName)
    {
        var jobs = Jobs();
        if (int.TryParse(idOrName, out var id)) return jobs.FirstOrDefault(j => j.Id == id);
        return jobs.FirstOrDefault(j => j.Name.Equals(idOrName.Trim(), StringComparison.CurrentCultureIgnoreCase))
            ?? jobs.FirstOrDefault(j => j.Name.Contains(idOrName.Trim(), StringComparison.CurrentCultureIgnoreCase));
    }

    private RecipeInfo ToRecipe(object?[] r)
    {
        // r : id, jobId, quantities, resultId, resultLevel
        string rid = S(r[0]);
        var qty = ParseInts(S(r[2]));
        var ids = Query("select target_id from RecipeData_ingredientIds_junction where RecipeData_id = $0 order by rowid", rid).Select(x => L(x[0])).ToList();
        var ing = ids.Select((gid, i) => new RecipeIngredient(gid, ItemName(gid) ?? "", i < qty.Count ? qty[i] : 1)).ToList();
        int job = I(r[1]);
        return new RecipeInfo(L(r[3]), ItemName(L(r[3])) ?? "", I(r[4]), job, NameOf("JobData", job), ing);
    }

    /// <summary>Recette qui fabrique cet objet (la première), ou null s'il ne se fabrique pas.</summary>
    public RecipeInfo? Recipe(long resultGid)
        => Query("select id, jobId, quantities, resultId, resultLevel from RecipeData where resultId = $0 order by rowid limit 1", resultGid).FirstOrDefault() is { } r ? ToRecipe(r) : null;

    /// <summary>Recettes d'un métier jusqu'au niveau donné, les plus hautes d'abord.</summary>
    public IReadOnlyList<RecipeInfo> RecipesOfJob(int jobId, int maxLevel)
        => Query("select id, jobId, quantities, resultId, resultLevel from RecipeData where jobId = $0 and resultLevel <= $1 order by resultLevel desc, resultId", jobId, maxLevel).Select(ToRecipe).ToList();

    private static List<int> ParseInts(string json)
    {
        try { return JsonSerializer.Deserialize<List<int>>(string.IsNullOrWhiteSpace(json) ? "[]" : json) ?? new(); }
        catch { return new(); }
    }

    // ---------------------------------------------------------------- donjons
    private DungeonInfo ToDungeon(object?[] r)
    {
        // r : id, entranceMapId, exitMapId, nameId, optimalPlayerLevel, requiredObjects, bosses
        int id = I(r[0]);
        var maps = Query("select target_id from DungeonData_mapIds_junction where DungeonData_id = $0 order by rowid", id.ToString(CultureInfo.InvariantCulture)).Select(x => L(x[0])).ToList();
        var keys = new List<DungeonKey>();
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(S(r[5])) ? "[]" : S(r[5]));
            foreach (var e in doc.RootElement.EnumerateArray())
                keys.Add(new DungeonKey(e.GetProperty("id").GetInt64(), e.TryGetProperty("quantity", out var q) ? q.GetInt32() : 1, ItemName(e.GetProperty("id").GetInt64()) ?? ""));
        }
        catch { /* clés illisibles : liste vide */ }
        var bosses = ParseInts(S(r[6])).Select(b => (b, Text(L(Scalar("select nameId from MonsterData where id = $0", b))) ?? "")).ToList();
        return new DungeonInfo(id, Text(L(r[3])) ?? "", maps, L(r[1]), L(r[2]), I(r[4]), keys, bosses);
    }

    private const string DungeonCols = "id, entranceMapId, exitMapId, nameId, optimalPlayerLevel, requiredObjects, bosses";

    public DungeonInfo? DungeonById(int id) => Query($"select {DungeonCols} from DungeonData where id = $0", id).FirstOrDefault() is { } r ? ToDungeon(r) : null;

    /// <summary>Donjon qui contient cette carte (une de ses salles), ou null hors donjon.</summary>
    public DungeonInfo? DungeonOfMap(long mapId)
        => Scalar("select DungeonData_id from DungeonData_mapIds_junction where target_id = $0 limit 1", mapId.ToString(CultureInfo.InvariantCulture)) is { } id
            ? DungeonById(int.Parse(S(id), CultureInfo.InvariantCulture)) : null;

    public DungeonInfo? DungeonByName(string name)
    {
        var hit = Query("select d.id from DungeonData d join translations t on t.id = cast(d.nameId as text) and t.lang = $1 where t.value like $0 order by length(t.value) limit 1", "%" + name.Trim() + "%", _lang).FirstOrDefault();
        return hit is null ? null : DungeonById(I(hit[0]));
    }

    // ---------------------------------------------------------------- d'où vient un objet
    public ItemSources? Sources(long gid)
    {
        var name = ItemName(gid);
        if (name is null) return null;
        (string, int)? craft = Recipe(gid) is { } rc ? (rc.Job, rc.Level) : null;

        var skills = Query("select nameId from SkillData where gatheredRessourceItem = $0", gid).Select(r => Text(L(r[0])) ?? "").Where(s => s.Length > 0).Distinct().ToList();
        var zones = new List<string>();
        if (skills.Count > 0 && Scalar("select resourcesBySubarea from ItemData where id = $0", gid) is string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var e in doc.RootElement.EnumerateArray())
                    if (e.TryGetProperty("values", out var v) && v.TryGetProperty("Array", out var arr) && arr.GetArrayLength() > 0)
                        zones.Add(Text(L(Scalar("select nameId from SubAreaData where id = $0", arr[0].GetInt64()))) ?? "");
            }
            catch { /* JSON de zones illisible : aucune zone */ }
        }

        var monsters = new List<MonsterSource>();
        foreach (var m in Query("select target_id from ItemData_dropMonsterIds_junction where ItemData_id = $0", gid.ToString(CultureInfo.InvariantCulture)))
        {
            int mid = I(m[0]);
            double rate = 0;
            try
            {
                using var doc = JsonDocument.Parse(S(Scalar("select drops from MonsterData where id = $0", mid)));
                foreach (var d in doc.RootElement.EnumerateArray())
                    if (d.TryGetProperty("objectId", out var o) && o.GetInt64() == gid && d.TryGetProperty("percentDropForGrade1", out var p)) { rate = p.GetDouble(); break; }
            }
            catch { /* drops illisibles : taux 0 */ }
            monsters.Add(new MonsterSource(mid, Text(L(Scalar("select nameId from MonsterData where id = $0", mid))) ?? "", rate));
        }
        return new ItemSources(gid, name, craft, skills, zones.Where(z => z.Length > 0).Distinct().ToList(), monsters.OrderByDescending(x => x.DropRate).ToList());
    }

    // ---------------------------------------------------------------- quêtes
    private QuestSummary ToQuest(object?[] r)
    {
        // r : id, nameId, categoryId, levelMin, levelMax, startCriterion, startPosition
        var pos = new List<(long, int)>();
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(S(r[6])) ? "[]" : S(r[6]));
            foreach (var e in doc.RootElement.EnumerateArray()) pos.Add((e.GetProperty("mapId").GetInt64(), e.TryGetProperty("npcId", out var n) ? n.GetInt32() : 0));
        }
        catch { /* position de départ illisible */ }
        return new QuestSummary(I(r[0]), Text(L(r[1])) ?? "", I(r[2]), NameOf("QuestCategoryData", I(r[2])), I(r[3]), I(r[4]), S(r[5]), pos);
    }

    private const string QuestCols = "q.id, q.nameId, q.categoryId, q.levelMin, q.levelMax, q.startCriterion, q.startPosition";

    public QuestSummary? Quest(int id) => Query($"select {QuestCols} from QuestData q where q.id = $0", id).FirstOrDefault() is { } r ? ToQuest(r) : null;

    /// <summary>Quêtes par fragment de nom, catégorie (nom) et/ou sous-zone de départ (nom), au plus <paramref name="max"/>.</summary>
    public IReadOnlyList<QuestSummary> FindQuests(string? query, string? category, string? zone, int max)
    {
        var where = new List<string>(); var args = new List<object?>();
        string P(object? v) { args.Add(v); return "$" + (args.Count - 1); }
        var from = "from QuestData q join translations t on t.id = cast(q.nameId as text) and t.lang = " + P(_lang);
        if (!string.IsNullOrWhiteSpace(query)) where.Add("t.value like " + P("%" + query.Trim() + "%"));
        if (!string.IsNullOrWhiteSpace(category))
            where.Add("q.categoryId in (select c.id from QuestCategoryData c join translations tc on tc.id = cast(c.nameId as text) and tc.lang = " + P(_lang) + " where tc.value like " + P("%" + category.Trim() + "%") + ")");
        var rows = Query($"select {QuestCols} {from} {(where.Count > 0 ? "where " + string.Join(" and ", where) : "")} order by q.id limit {Math.Clamp(max, 1, 500) * (string.IsNullOrWhiteSpace(zone) ? 1 : 20)}", args.ToArray())
                   .Select(ToQuest).ToList();
        if (!string.IsNullOrWhiteSpace(zone))
        {
            var maps = Query("select m.target_id from SubAreaData s join translations ts on ts.id = cast(s.nameId as text) and ts.lang = $1 join SubAreaData_mapIds_junction m on m.SubAreaData_id = cast(s.id as text) where ts.value like $0", "%" + zone.Trim() + "%", _lang)
                       .Select(r => L(r[0])).ToHashSet();
            rows = rows.Where(q => q.StartPositions.Any(p => maps.Contains(p.MapId))).ToList();
        }
        return rows.Take(Math.Clamp(max, 1, 500)).ToList();
    }
}

/// <summary>Texte brut d'un document du jeu (HTML simplifié : paragraphes, images repérées par ##png,N##).</summary>
public static class HtmlText
{
    public static string Strip(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var t = System.Text.RegularExpressions.Regex.Replace(html, @"<\s*(pagefeed|br)\s*/?\s*>|</\s*(p|li|div|h[1-6])\s*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"<[^>]+>", "");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"##[a-z]+,\d+##", "");
        t = System.Net.WebUtility.HtmlDecode(t);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"[ \t\u00a0]+", " ");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\s*\n\s*", "\n");
        return t.Trim();
    }
}
