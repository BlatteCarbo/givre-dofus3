using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services.LotM;

/// <summary>
/// Modules Lua du lot M : achievement:, anomaly:, teleport:, dungeon:, document:, data:, et des ajouts à job:, quest:, character:, bank:,
/// sale:, npc: + loadfile. Autonome (pas d'hôte complet) : tout passe par <see cref="ILotMGame"/> ; testable avec un faux jeu et de vrais scripts.
/// Ce qui n'a pas de source dans le clone (positions de PNJ hors observations, achat/vente en boutique, fiche complète) est dit dans les retours.
/// </summary>
public sealed partial class LotMLuaModule
{
    public const int WaitMaxMs = 30 * 60 * 1000;

    private readonly Script _s;
    private readonly ILotMGame _g;
    private readonly CancellationToken _ct;
    private readonly Func<string, Table> _module;
    private readonly Func<Exception> _makeStop;
    private readonly Action<string> _warn;
    private readonly HashSet<string> _warned = new();

    public LotMLuaModule(Script script, ILotMGame game, CancellationToken ct, Func<string, Table> module, Func<Exception> makeStop, Action<string> warn)
        => (_s, _g, _ct, _module, _makeStop, _warn) = (script, game, ct, module, makeStop, warn);

    // ---------------------------------------------------------------- aides
    private static DynValue Arg(List<DynValue> a, int i) => i < a.Count ? a[i] : DynValue.Nil;
    private static string? Str(DynValue v) => v.Type == DataType.String ? v.String : v.Type == DataType.Number ? v.CastToString() : null;
    private static long? Num(DynValue v) => v.Type == DataType.Number ? (long)v.Number : v.Type == DataType.String && long.TryParse(v.String, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    private static DynValue Pair(params DynValue[] v) => DynValue.NewTuple(v);

    private void Add(Table mod, string name, Func<List<DynValue>, DynValue> body)
    {
        mod[name] = DynValue.NewCallback((_, a) =>
        {
            var l = new List<DynValue>(a.Count);
            for (int i = 0; i < a.Count; i++) l.Add(a[i]);
            if (l.Count > 0 && l[0].Type == DataType.Table && ReferenceEquals(l[0].Table, mod)) l.RemoveAt(0);   // mod:verbe(...)
            return body(l);
        });
    }

    private DynValue Tbl(params (string Key, object? Value)[] fields)
    {
        var t = new Table(_s);
        foreach (var (k, v) in fields) if (v is not null) t[k] = v is DynValue dv ? dv : DynValue.FromObject(_s, v);
        return DynValue.NewTable(t);
    }

    private DynValue Arr<T>(IEnumerable<T> items, Func<T, DynValue> make)
    {
        var t = new Table(_s);
        int i = 1;
        foreach (var it in items) t[i++] = make(it);
        return DynValue.NewTable(t);
    }

    private T Wait<T>(Task<T> task)
    {
        try { task.Wait(_ct); return task.Result; }
        catch (OperationCanceledException) { throw _makeStop(); }
        catch (AggregateException ae) when (ae.InnerException is OperationCanceledException) { throw _makeStop(); }
        catch (AggregateException ae) { throw new ScriptRuntimeException(ae.GetBaseException().Message); }
    }

    private void WarnOnce(string key, string msg) { if (_warned.Add(key)) _warn(msg); }
    private static DynValue Nope(string reason) => Pair(DynValue.Nil, DynValue.NewString(reason));
    private const string NoData = "données du jeu indisponibles (dofus.sqlite introuvable)";

    // ---------------------------------------------------------------- entrée
    public void Bind()
    {
        BindAchievement(_module("achievement"));
        BindAnomaly(_module("anomaly"));
        BindTeleport(_module("teleport"));
        BindDungeon(_module("dungeon"));
        BindMapLeave(_module("map"));
        BindDocument(_module("document"));
        BindData(_module("data"));
        BindCraftPlan(_module("craft"));
        BindJobQuest(_module("job"), _module("quest"));
        BindCharacter(_module("character"));
        BindMisc(_module("bank"), _module("sale"), _module("npc"));
    }

    // ================================================================ achievement:
    private void BindAchievement(Table m)
    {
        Add(m, "getName", a => DynValue.NewString(Num(Arg(a, 0)) is long id ? _g.Db?.Achievement((int)id)?.Name ?? "" : ""));
        Add(m, "isDone", a => DynValue.NewBoolean(Num(Arg(a, 0)) is long id && _g.Achievements.IsDone((int)id)));
        Add(m, "finished", _ => Arr(_g.Achievements.Finished(), id => DynValue.NewNumber(id)));
        Add(m, "getRewardable", _ => Arr(_g.Achievements.Rewardable(), r => Tbl(("id", r.Id), ("name", _g.Db?.Achievement(r.Id)?.Name ?? ""), ("level", r.Level))));
        Add(m, "rewardCount", _ => DynValue.NewNumber(_g.Achievements.RewardCount()));
        Add(m, "hasReward", a => DynValue.NewBoolean(Arg(a, 0).Type == DataType.Nil ? _g.Achievements.HasReward() : Num(Arg(a, 0)) is long id && _g.Achievements.HasReward((int)id)));

        Add(m, "claimReward", a =>
        {
            if (!_g.CanAct || Num(Arg(a, 0)) is not long id) return DynValue.False;
            return DynValue.NewBoolean(Wait(_g.ClaimAchievementAsync((int)id, _ct)));
        });
        Add(m, "claimAllRewards", _ =>
        {
            if (!_g.CanAct) return DynValue.NewNumber(0);
            int ok = 0;
            // Pas de message « tout récupérer » nommé dans le mapping : une demande par succès en attente, l'une après l'autre.
            foreach (var (id, _) in _g.Achievements.Rewardable())
            {
                _ct.ThrowIfCancellationRequested();
                if (Wait(_g.ClaimAchievementAsync(id, _ct))) ok++;
            }
            return DynValue.NewNumber(ok);
        });
        Add(m, "setAutoClaim", a =>
        {
            if (!_g.CanAct) return DynValue.False;
            _g.AutoClaim = Arg(a, 0).Type != DataType.Boolean || Arg(a, 0).Boolean;
            return DynValue.NewBoolean(_g.AutoClaim);
        });
        Add(m, "isAutoClaim", _ => DynValue.NewBoolean(_g.AutoClaim));

        Add(m, "find", a =>
        {
            if (_g.Db is not { } db) return Arr(Array.Empty<int>(), _ => DynValue.Nil);
            var status = (Str(Arg(a, 1)) ?? "all").ToLowerInvariant();
            var rows = db.FindAchievements(Str(Arg(a, 0)) ?? "", 500)
                         .Select(r => (Row: r, Done: _g.Achievements.IsDone(r.Id)))
                         .Where(x => status switch { "done" => x.Done, "todo" => !x.Done, _ => true }).Take(50);
            return Arr(rows, x => AchievementRowTable(x.Row, x.Done));
        });
        Add(m, "category", a =>
        {
            if (_g.Db is not { } db || Num(Arg(a, 0)) is not long cat) return Arr(Array.Empty<int>(), _ => DynValue.Nil);
            return Arr(db.AchievementsOfCategory((int)cat), r => AchievementRowTable(r, _g.Achievements.IsDone(r.Id)));
        });
        Add(m, "objectives", a =>
        {
            if (_g.Db is not { } db || Num(Arg(a, 0)) is not long id) return Arr(Array.Empty<int>(), _ => DynValue.Nil);
            bool done = _g.Achievements.IsDone((int)id);
            // La progression chiffrée vient du serveur (AchievementDetailsEvent) : non demandée ici -> current = max = 0 (comme « sans réponse »).
            return Arr(db.AchievementObjectives((int)id), o => Tbl(("id", o.Id), ("name", o.Name), ("current", 0), ("max", 0), ("done", done)));
        });
    }

    private DynValue AchievementRowTable(AchievementRow r, bool done)
        => Tbl(("id", r.Id), ("name", r.Name), ("category", r.Category), ("points", r.Points), ("level", r.Level), ("done", done));

    // ================================================================ anomaly:
    /// <summary>Anomalie ouverte (celles du serveur du bot) en table Lua ; null si fermée ou inconnue de la base.</summary>
    private DynValue? AnomalyTable(AnomalyState a)
    {
        if (!a.Open) return null;
        var sub = _g.Db?.SubArea(a.SubareaId);
        double remaining = AnomalyClock.RemainingSeconds(a.ClosingRaw, a.ReceivedUtc, _g.UtcNow);
        return Tbl(("subareaId", a.SubareaId), ("name", sub?.Name ?? ""), ("serverId", _g.ServerId), ("areaId", sub?.AreaId), ("area", sub?.Area ?? ""),
                   ("level", sub?.Level ?? 0), ("zaapMapId", sub is { ZaapMapId: > 0 } ? sub.ZaapMapId : null),
                   ("closingTime", AnomalyClock.ClosingEpochSeconds(a.ClosingRaw, a.ReceivedUtc)), ("remaining", remaining),
                   ("bonus", a.RewardRate > 0 ? a.RewardRate : null));
    }

    private bool OwnServer(DynValue server) => server.Type == DataType.Nil || (Num(server) is long n && n == _g.ServerId);

    private AnomalyState? FindAnomaly(List<DynValue> a)
    {
        if (!OwnServer(Arg(a, 1))) return null;   // seules les anomalies du serveur du bot sont connues
        var key = Str(Arg(a, 0));
        int? id = key is null ? null : _g.Db?.SubAreaIdByName(key) ?? (int.TryParse(key, out var n) ? n : null);
        return id is null ? null : _g.Anomalies().FirstOrDefault(x => x.SubareaId == id);
    }

    private void BindAnomaly(Table m)
    {
        Add(m, "list", a => OwnServer(Arg(a, 0))
            ? Arr(_g.Anomalies().Select(AnomalyTable).Where(t => t is not null).Select(t => t!)
                  .OrderByDescending(t => t.Table.Get("remaining").Number), t => t)
            : Arr(Array.Empty<int>(), _ => DynValue.Nil));
        Add(m, "isOpen", a => DynValue.NewBoolean(FindAnomaly(a) is { Open: true }));
        Add(m, "get", a => FindAnomaly(a) is { } st && AnomalyTable(st) is { } t ? t : DynValue.Nil);
        Add(m, "remaining", a => DynValue.NewNumber(FindAnomaly(a) is { Open: true } st ? AnomalyClock.RemainingSeconds(st.ClosingRaw, st.ReceivedUtc, _g.UtcNow) : 0.0));
    }

    private DynValue OfferTable(OfferState o)
        => Tbl(("kind", o.Offer.Kind), ("requesterId", o.Offer.RequesterId), ("requesterName", o.Offer.RequesterName), ("mapId", o.Offer.MapId),
               ("dungeonId", o.Offer.DungeonId), ("timeLeft", o.Offer.TimeLeft), ("age", Math.Max(0.0, Math.Floor((_g.UtcNow - o.AtUtc).TotalSeconds))));

    private void BindTeleport(Table m)
    {
        Add(m, "offer", _ => _g.CurrentOffer() is { } o ? OfferTable(o) : DynValue.Nil);
        Add(m, "wait", a =>
        {
            int ms = (int)Math.Clamp(Arg(a, 0).CastToNumber() ?? 30000.0, 0.0, 1800000.0);
            var o = _g.CurrentOffer() ?? Wait(_g.WaitOfferAsync(ms, _ct));
            return o is not null ? OfferTable(o) : DynValue.Nil;
        });
        Add(m, "accept", _ => DynValue.NewBoolean(_g.CanAct && _g.CurrentOffer() is not null && _g.AnswerOffer(accept: true)));
        Add(m, "refuse", _ => DynValue.NewBoolean(_g.CurrentOffer() is not null && _g.AnswerOffer(accept: false)));
    }

    private DynValue DungeonTable(DungeonInfo d, int? room)
        => Tbl(("id", d.Id), ("name", d.Name), ("room", room), ("rooms", d.MapIds.Count), ("mapIds", Arr(d.MapIds, id => DynValue.NewNumber(id))),
               ("entranceMapId", d.EntranceMapId), ("exitMapId", d.ExitMapId), ("optimalLevel", d.OptimalLevel),
               ("keys", Arr(d.Keys, k => Tbl(("gid", k.Gid), ("qty", k.Quantity), ("name", k.Name)))),
               ("bosses", Arr(d.Bosses, b => Tbl(("id", b.Id), ("name", b.Name)))));

    private DungeonInfo? CurrentDungeon() => _g.Db?.DungeonOfMap(_g.CurrentMapId);

    private void BindDungeon(Table m)
    {
        Add(m, "current", _ => CurrentDungeon() is { } d ? DungeonTable(d, d.MapIds.ToList().IndexOf(_g.CurrentMapId) + 1) : DynValue.Nil);
        Add(m, "isInside", _ => DynValue.NewBoolean(CurrentDungeon() is not null));
        Add(m, "info", a => Num(Arg(a, 0)) is long id && _g.Db?.DungeonById((int)id) is { } d ? DungeonTable(d, null) : DynValue.Nil);
        Add(m, "exit", _ => LeaveDungeon());
    }

    /// <summary>map:leaveDungeon() — même verbe que le bouton rouge (DungeonExitRequest). Refusé en combat.
    /// true + id de carte, ou false + motif.</summary>
    private void BindMapLeave(Table map) => Add(map, "leaveDungeon", _ => LeaveDungeon());

    private DynValue LeaveDungeon()
    {
        if (!_g.CanAct) return Pair(DynValue.False, DynValue.NewString("noperm"));
        if (_g.InFight) return Pair(DynValue.False, DynValue.NewString("fight"));
        var (ok, reason, map) = Wait(_g.ExitDungeonAsync(_ct));
        return !ok ? Pair(DynValue.False, DynValue.NewString(reason)) : Pair(DynValue.True, DynValue.NewNumber(map));
    }

    private void BindDocument(Table m)
    {
        Add(m, "id", _ => DynValue.NewNumber(_g.OpenDocumentId()));
        Add(m, "isOpen", _ => DynValue.NewBoolean(_g.OpenDocumentId() != 0));
        Add(m, "close", _ => DynValue.NewBoolean(_g.OpenDocumentId() != 0 && _g.CloseDocument()));
        Add(m, "get", a =>
        {
            int id = Num(Arg(a, 0)) is long n ? (int)n : _g.OpenDocumentId();
            if (id != 0 && _g.Db?.Document(id) is { } doc)
                return Tbl(("id", doc.Id), ("title", doc.Title), ("text", doc.Text), ("links", DynValue.NewTable(new Table(_s))));
            return DynValue.Nil;
        });
    }

    // ================================================================ data:
    private static string Friendly(string table)
        => table.EndsWith("Data", StringComparison.Ordinal) ? table[..^4].ToLowerInvariant() + "s" : table.ToLowerInvariant();

    private DynValue JsonToLua(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => DynValue.NewTable(e.EnumerateObject().Aggregate(new Table(_s), (t, p) => { t[p.Name] = JsonToLua(p.Value); return t; })),
        JsonValueKind.Array => Arr(e.EnumerateArray(), JsonToLua),
        JsonValueKind.String => DynValue.NewString(e.GetString() ?? ""),
        JsonValueKind.Number => DynValue.NewNumber(e.GetDouble()),
        JsonValueKind.True => DynValue.True,
        JsonValueKind.False => DynValue.False,
        _ => DynValue.Nil,
    };

    private DynValue RecipeTable(RecipeInfo r, int? craftable = null)
        => Tbl(("resultId", r.ResultId), ("name", r.Name), ("level", r.Level), ("jobId", r.JobId), ("job", r.Job),
               ("ingredients", Arr(r.Ingredients, i => Tbl(("gid", i.Gid), ("name", i.Name), ("quantity", i.Quantity)))), ("craftableNow", craftable));

    private void BindData(Table m)
    {
        Add(m, "tables", _ => _g.Db is { } db ? Arr(db.Tables().Select(Friendly).Distinct(), n => DynValue.NewString(n)) : Arr(Array.Empty<string>(), _ => DynValue.Nil));
        // data:places(query [, max=10]) : lieux de la carte du monde ("banque", "hôtel de vente", "atelier", "zaap", "donjon"…), les plus proches d'abord.
        Add(m, "places", a =>
        {
            if (_g.Db is not { } db) return Nope("données du jeu indisponibles (dofus.sqlite introuvable)");
            string query = Str(Arg(a, 0)) ?? "";
            int max = Num(Arg(a, 1)) is long mx ? (int)mx : 10;
            if (db.Places(query, _g.PlaceOrigin, max, _g.PlaceDoors) is not { } places) return Nope("données des lieux indisponibles (HintData absent)");
            return places.Count == 0 ? Nope($"No place named like '{query}'. Try 'banque', 'hôtel de vente', 'atelier', 'zaap' or 'donjon'.")
                : Arr(places, p => Tbl(("name", p.Name), ("category", p.Category), ("mapId", p.MapId), ("coords", p.Coords), ("subArea", p.SubArea),
                    ("area", p.Area), ("layer", p.Layer), ("distance", p.Distance),
                    ("doors", p.Doors.Count == 0 ? null : Arr(p.Doors, d => Tbl(("cell", d.Cell), ("mapId", d.MapId))))));
        });
        DynValue RowToLua(GameDb db, IReadOnlyDictionary<string, object?> row)
        {
            var t = new Table(_s);
            foreach (var (k, v) in row)
            {
                if (v is string s && s.Length > 1 && (s[0] == '[' || s[0] == '{'))
                {
                    try { using var doc = JsonDocument.Parse(s); t[k] = JsonToLua(doc.RootElement); continue; } catch (JsonException) { /* texte brut */ }
                }
                t[k] = v is null ? DynValue.Nil : v is long l ? DynValue.NewNumber(l) : v is double d ? DynValue.NewNumber(d) : DynValue.NewString(Convert.ToString(v, CultureInfo.InvariantCulture) ?? "");
            }
            if (row.TryGetValue("nameId", out var nid) && nid is long n && db.Text(n) is { } name) t["name"] = name;
            if (row.TryGetValue("descriptionId", out var did) && did is long dn && db.Text(dn) is { } desc) t["description"] = desc;
            return DynValue.NewTable(t);
        }
        Add(m, "get", a =>
        {
            if (_g.Db is not { } db || Str(Arg(a, 0)) is not { } table || Str(Arg(a, 1)) is not { } id) return DynValue.Nil;
            return db.Row(table, id) is { } row ? RowToLua(db, row) : DynValue.Nil;
        });
        // data:find(table, filtres [, limite=50]) : filtres = une chaîne « champ op valeur » ou une liste de chaînes (toutes doivent tenir).
        Add(m, "find", a =>
        {
            if (_g.Db is not { } db || Str(Arg(a, 0)) is not { } table) return Arr(Array.Empty<string>(), _ => DynValue.Nil);
            var filters = new List<string>();
            if (Arg(a, 1).Type == DataType.Table) { foreach (var p in Arg(a, 1).Table.Values) if (Str(p) is { } f) filters.Add(f); }
            else if (Str(Arg(a, 1)) is { } one) filters.Add(one);
            int limit = Num(Arg(a, 2)) is long l ? (int)l : 50;
            return Arr(db.Find(table, filters, limit), r => RowToLua(db, r));
        });
        Add(m, "search", a =>
        {
            if (_g.Db is not { } db || Str(Arg(a, 0)) is not { } table || Str(Arg(a, 1)) is not { } text) return Arr(Array.Empty<string>(), _ => DynValue.Nil);
            int limit = Num(Arg(a, 2)) is long l ? (int)l : 50;
            return Arr(db.SearchText(table, text, limit), r => RowToLua(db, r));
        });
        Add(m, "recipe", a => _g.Db?.Recipe(Num(Arg(a, 0)) ?? 0) is { } r ? RecipeTable(r) : DynValue.Nil);
        Add(m, "sources", a =>
        {
            if (_g.Db is not { } db) return Nope(NoData);
            long? gid = Num(Arg(a, 0)) ?? (Str(Arg(a, 0)) is { } name ? db.ItemGidByName(name) : null);
            if (gid is null || db.Sources(gid.Value) is not { } s) return Nope("objet inconnu");
            return Tbl(("gid", s.Gid), ("name", s.Name),
                       ("craft", s.Craft is { } c ? Tbl(("job", c.Job), ("level", c.Level)) : null),
                       ("harvest", Tbl(("zones", Arr(s.HarvestZones, z => DynValue.NewString(z))), ("skills", Arr(s.HarvestSkills, k => DynValue.NewString(k))))),
                       ("monsters", Arr(s.Monsters, x => Tbl(("id", x.MonsterId), ("name", x.Name), ("dropRate", x.DropRate)))),
                       ("questRewards", DynValue.NewTable(new Table(_s))), ("soldBy", DynValue.NewTable(new Table(_s))), ("npcDialogs", DynValue.NewTable(new Table(_s))));
        });
    }

    // ================================================================ job: / quest:
    private void BindJobQuest(Table job, Table quest)
    {
        Add(job, "recipes", a =>
        {
            if (_g.Db is not { } db) return Nope(NoData);
            if (Str(Arg(a, 0)) is not { } key || db.Job(key) is not { } j) return Nope("métier inconnu");
            int level = Num(Arg(a, 1)) is long l ? (int)l : (_g.JobLevel(j.Id) is var jl and > 0 ? jl : 200);
            bool craftable = Arg(a, 2).Type == DataType.Boolean && Arg(a, 2).Boolean;
            var recipes = db.RecipesOfJob(j.Id, level);
            return Arr(recipes, r => RecipeTable(r, craftable ? r.Ingredients.Count == 0 ? 0 : r.Ingredients.Min(i => i.Quantity <= 0 ? 0 : _g.ItemCount(i.Gid) / i.Quantity) : null));
        });

        Add(quest, "find", a =>
        {
            if (_g.Db is not { } db) return Nope(NoData);
            string? query, zone = null, category = null, status = null; int max = 20;
            if (Arg(a, 0).Type == DataType.Table)
            {
                var t = Arg(a, 0).Table;
                query = Str(t.Get("query")); zone = Str(t.Get("zone")); category = Str(t.Get("category")); status = Str(t.Get("status"));
                if (Num(t.Get("max")) is long mx) max = (int)Math.Clamp(mx, 1, 200);
            }
            else query = Str(Arg(a, 0));
            var finished = _g.FinishedQuests(); var active = _g.ActiveQuests(); int level = _g.Sheet()?.Level ?? 0;
            var found = db.FindQuests(query, category, zone, 500).Select(q =>
            {
                string st;
                CriterionResult c = CriterionEval.Evaluate(q.StartCriterion, level, finished.Contains, active.Contains);
                st = finished.Contains(q.Id) ? "done" : active.Contains(q.Id) ? "active" : c.Satisfied ? "available" : "blocked";
                return (Quest: q, Status: st, Criterion: c);
            }).Where(x => status is null || x.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).Take(max).ToList();
            if (found.Count == 0) return Nope("aucune quête");
            return Arr(found, x => Tbl(("id", x.Quest.Id), ("name", x.Quest.Name), ("category", x.Quest.Category), ("levelMin", x.Quest.LevelMin), ("levelMax", x.Quest.LevelMax),
                ("state", Tbl(("status", x.Status), ("blockedBy", Arr(x.Criterion.BlockedBy, s => DynValue.NewString(s))), ("notChecked", Arr(x.Criterion.NotChecked, s => DynValue.NewString(s)))))));
        });
        Add(quest, "start", a =>
        {
            if (Num(Arg(a, 0)) is not long id) return Pair(DynValue.False, DynValue.NewString("badargs"));
            var (ok, reason) = Wait(_g.StartQuestAsync((int)id, _ct));
            return Pair(DynValue.NewBoolean(ok), DynValue.NewString(reason));
        });
        // quest:plan([zone]) (doc originale) regroupe les quêtes EN COURS par sous-zone de leur objectif ACTUEL.
        // Ce jeu de données (dofus.sqlite) n'a pas de QuestObjectiveData (pas de carte/sous-zone par objectif, seulement
        // QuestStepData sans position) : grouper par la position de DÉPART de la quête donnerait une sous-zone fausse
        // dès qu'elle a avancé -> jamais inventer cette position, dire le manque plutôt qu'un plan trompeur.
        Add(quest, "plan", _ => Nope("quest:plan : pas de données d'objectif par carte dans ce jeu de données (QuestObjectiveData absente)"));
    }

    // ================================================================ character:
    private void BindCharacter(Table m)
    {
        Add(m, "id", _ => _g.CharacterId != 0 ? DynValue.NewNumber(_g.CharacterId) : DynValue.Nil);
        Add(m, "sheet", _ =>
        {
            if (_g.Sheet() is not { } s) return Nope("fiche indisponible (perso pas encore identifié)");
            var x = _g.SheetMore();
            // Forme relevée sur le GivreClone d'origine (eval_lua, 2026-10-17) : characterId, characteristics, characteristicsNote, class, energy et life EN TEXTE « cur/max »,
            // equipment indexé par position + 1, experience {total, toNextLevel, nextLevelAt}, jobs, kamas, level, name, spells, unspentCharacteristicPoints.
            // Ce que le clone ne décode pas est ABSENT (jamais une valeur inventée) : characteristics (valeurs investies), xp/toNextLevel des métiers.
            var t = new Table(_s);
            t["characterId"] = DynValue.NewNumber(s.Id);
            t["name"] = DynValue.NewString(s.Name);
            t["class"] = DynValue.NewString(s.Breed);
            t["level"] = DynValue.NewNumber(s.Level);
            t["kamas"] = DynValue.NewNumber(s.Kamas);
            if (s.Life >= 0 && s.MaxLife >= 0) t["life"] = DynValue.NewString($"{s.Life}/{s.MaxLife}");
            if (s.Energy >= 0 && s.MaxEnergy >= 0) t["energy"] = DynValue.NewString($"{s.Energy}/{s.MaxEnergy}");
            if (s.XpTotal >= 0 && s.XpToNextLevel >= 0)
                t["experience"] = Tbl(("total", s.XpTotal), ("toNextLevel", s.XpToNextLevel), ("nextLevelAt", s.XpTotal + s.XpToNextLevel));
            if (x.UnspentPoints >= 0) t["unspentCharacteristicPoints"] = DynValue.NewNumber(x.UnspentPoints);
            t["characteristics"] = DynValue.NewTable(new Table(_s));
            t["characteristicsNote"] = DynValue.NewString("Valeurs investies non décodées par le clone : table vide (aucune valeur inventée).");
            t["jobs"] = Arr(x.Jobs, j => Tbl(("jobId", j.Id), ("name", j.Name), ("level", j.Level)));
            t["spells"] = Arr(x.Spells, sp => Tbl(("spellId", sp.Id), ("name", sp.Name), ("level", sp.Level)));
            var eq = new Table(_s);
            foreach (var e in x.Equipment.OrderBy(e => e.Position))
                eq[e.Position + 1] = Tbl(("position", e.Position), ("slot", SlotName(e.Position)), ("gid", e.Gid), ("name", e.Name), ("level", e.Level));
            t["equipment"] = DynValue.NewTable(eq);
            return DynValue.NewTable(t);
        });
        foreach (var name in new[] { "getCostVitality", "getCostWisdom", "getCostStrenght", "getCostIntelligence", "getCostChance", "getCostAgility" })
        {
            var n = name;
            // Texte et portée relevés sur le GivreClone d'origine : UN avertissement pour toute la famille getCost*, valeur -1.
            Add(m, n, _ => { WarnOnce("getCost*", "character:getCost* : fonction Givre sans équivalent dans GivreClone — appel ignoré."); return DynValue.NewNumber(-1); });
        }
        Add(m, "switch", _ => Pair(DynValue.False, DynValue.NewString("changement de perso ou de serveur non câblé dans le clone")));
    }


    /// <summary>Nom d'emplacement d'équipement, tel que le GivreClone d'origine l'écrit (relevé sur deux personnages, 2026-10-17).</summary>
    public static string SlotName(int position) => position switch
    {
        0 => "amulet", 1 => "weapon", 2 => "ring (left)", 3 => "belt", 4 => "ring (right)", 5 => "boots", 6 => "hat", 7 => "cloak", 8 => "pet",
        >= 9 and <= 14 => "dofus / trophy", 15 => "shield",
        _ => $"slot {position}",
    };

    // ================================================================ bank: / sale: / npc: / loadfile
    private void BindMisc(Table bank, Table sale, Table npc)
    {
        Add(bank, "knownCount", a => DynValue.NewNumber(Num(Arg(a, 0)) is long gid ? _g.BankKnownCount(gid) : 0));

        Add(sale, "priceHistory", a =>
        {
            if (Num(Arg(a, 0)) is not long gid) return DynValue.Nil;
            var h = Wait(_g.PriceHistoryAsync(gid, _ct));
            return h is null ? DynValue.Nil : Arr(h, p => Tbl(("date", p.Date), ("price", p.Price), ("sold", p.Sold)));
        });
        Add(sale, "keepOnSale", a =>
        {
            if (!_g.HasHdv) return Pair(DynValue.NewNumber(0), DynValue.NewString("noperm"));
            if (Num(Arg(a, 0)) is not long gid) return Pair(DynValue.NewNumber(0), DynValue.NewString("badargs"));
            var sizes = new List<KeepSize>();
            DynValue opts;
            if (Arg(a, 1).Type == DataType.Table)
            {
                opts = Arg(a, 2);
                foreach (var p in Arg(a, 1).Table.Pairs)
                {
                    if (Num(p.Key) is not long raw || KeepOnSalePlanner.NormalizeSize(raw) is not int size) return Pair(DynValue.NewNumber(0), DynValue.NewString("badsize"));
                    if (p.Value.Type == DataType.Table)
                        sizes.Add(new KeepSize(size, (int)(Num(p.Value.Table.Get("count")) ?? 0), Num(p.Value.Table.Get("price")), Num(p.Value.Table.Get("minPrice"))));
                    else sizes.Add(new KeepSize(size, (int)(Num(p.Value) ?? 0), null, null));
                }
            }
            else
            {
                opts = Arg(a, 3);
                if (Num(Arg(a, 1)) is not long raw || KeepOnSalePlanner.NormalizeSize(raw) is not int size) return Pair(DynValue.NewNumber(0), DynValue.NewString("badsize"));
                sizes.Add(new KeepSize(size, (int)(Num(Arg(a, 2)) ?? 0), null, null));
            }
            if (sizes.Select(x => x.Size).Distinct().Count() != sizes.Count) return Pair(DynValue.NewNumber(0), DynValue.NewString("badargs"));   // même taille deux fois : ambigu
            long? price = null, min = null; long undercut = 1;
            if (opts.Type == DataType.Table) { price = Num(opts.Table.Get("price")); min = Num(opts.Table.Get("minPrice")); undercut = Num(opts.Table.Get("undercut")) ?? 1; }
            var (placed, reason) = Wait(_g.KeepOnSaleAsync(gid, sizes, undercut, price, min, _ct));
            return Pair(DynValue.NewNumber(placed), reason is null ? DynValue.Nil : DynValue.NewString(reason));
        });

        Add(npc, "isShopOpen", _ => DynValue.NewBoolean(_g.ShopOpen()));
        Add(npc, "closeShop", _ => DynValue.NewBoolean(_g.CloseShop()));
        Add(npc, "openShop", a => Num(Arg(a, 0)) is long id && _g.CanAct && (_g.ShopOpen() || Wait(_g.OpenShopAsync((int)id, _ct))) ? DynValue.True : DynValue.False);
        Add(npc, "shopItems", a =>
        {
            DynValue Items(IEnumerable<LotMReader.ShopItem> items)
                => Arr(items, i => Tbl(("gid", i.Gid), ("name", _g.Db?.ItemName(i.Gid) ?? ""), ("price", i.Price), ("criterion", i.Criterion)));
            if (Arg(a, 0).Type == DataType.Nil) return Items(_g.OpenShopItems());
            if (Num(Arg(a, 0)) is not long npcId || _g.LastShopVisit((int)npcId) is not { } v) return Arr(Array.Empty<int>(), _ => DynValue.Nil);
            return Pair(Items(v.Items), Tbl(("seen", (long)(v.SeenUtc - DateTime.UnixEpoch).TotalSeconds), ("olderGame", v.GameVersion != _g.GameVersion)));
        });
        Add(npc, "locate", a =>
            Num(Arg(a, 0)) is long npcId
                ? Arr(_g.Sightings.Locate((int)npcId), s => Tbl(("mapId", s.MapId), ("cell", s.Cell), ("x", s.X), ("y", s.Y),
                      ("seen", (long)(s.SeenUtc - DateTime.UnixEpoch).TotalSeconds), ("olderGame", s.GameVersion != _g.GameVersion)))
                : Arr(Array.Empty<int>(), _ => DynValue.Nil));
        // Achat / vente en boutique : formes de requête observées sur le client officiel 3.7.11.11 (capture passive, 2026-10-16).
        DynValue Trade(string verb, int gid, int qty, long? maxUnit)
        {
            if (qty <= 0 || gid <= 0) return Pair(DynValue.False, DynValue.NewNumber(0), DynValue.NewString("badargs"));
            if (!_g.CanAct) return Pair(DynValue.False, DynValue.NewNumber(0), DynValue.NewString("noperm"));
            if (!_g.ShopOpen()) return Pair(DynValue.False, DynValue.NewNumber(0), DynValue.NewString("noshop"));
            if (verb == "buy")
            {
                var item = _g.OpenShopItems().FirstOrDefault(i => i.Gid == gid);
                if (item is null) return Pair(DynValue.False, DynValue.NewNumber(0), DynValue.NewString("notsold"));
                if (maxUnit is long mx && item.Price > mx) return Pair(DynValue.False, DynValue.NewNumber(0), DynValue.NewString("price"));
            }
            var (ok, done, reason) = Wait(verb == "buy" ? _g.NpcBuyAsync(gid, qty, _ct) : _g.NpcSellAsync(gid, qty, _ct));
            return ok ? Pair(DynValue.True, DynValue.NewNumber(done)) : Pair(DynValue.False, DynValue.NewNumber(done), DynValue.NewString(reason));
        }
        Add(npc, "buy", a => Num(Arg(a, 0)) is long g && Num(Arg(a, 1)) is long q
            ? Trade("buy", (int)g, (int)q, Num(Arg(a, 2)))
            : Pair(DynValue.False, DynValue.NewNumber(0), DynValue.NewString("badargs")));
        Add(npc, "sell", a => Num(Arg(a, 0)) is long g && Num(Arg(a, 1)) is long q
            ? Trade("sell", (int)g, (int)q, null)
            : Pair(DynValue.False, DynValue.NewNumber(0), DynValue.NewString("badargs")));

        // loadfile(nom) : compile un script de la bibliothèque PAR NOM (jamais un chemin) et rend la fonction, ou nil et l'erreur.
        // buyFromNpc(npcId, gid, qty [, maxUnitPrice]) : ouvre la boutique du PNJ, achète, referme ; mêmes retours que npc:buy.
        _s.Globals["buyFromNpc"] = DynValue.NewCallback((_, ca) =>
        {
            var a = new List<DynValue>(ca.Count);
            for (int i = 0; i < ca.Count; i++) a.Add(ca[i]);
            if (Num(Arg(a, 0)) is not long npcId || Num(Arg(a, 1)) is not long g || Num(Arg(a, 2)) is not long q)
                return Pair(DynValue.False, DynValue.NewNumber(0), DynValue.NewString("badargs"));
            if (!_g.CanAct) return Pair(DynValue.False, DynValue.NewNumber(0), DynValue.NewString("noperm"));
            bool wasOpen = _g.ShopOpen();
            if (!wasOpen && !Wait(_g.OpenShopAsync((int)npcId, _ct))) return Pair(DynValue.False, DynValue.NewNumber(0), DynValue.NewString("noshop"));
            try { return Trade("buy", (int)g, (int)q, Num(Arg(a, 3))); }
            finally { if (!wasOpen) _g.CloseShop(); }
        });
        _s.Globals["loadfile"] = DynValue.NewCallback((_, a) =>
        {
            var name = a.Count > 0 ? Str(a[0]) : null;
            var (script, why) = Supervisor.ScriptResolver.Resolve(name ?? "", Array.Empty<(string, string)>(), _g.ReadScriptFile);
            if (script is null || script.FromCode) return Pair(DynValue.Nil, DynValue.NewString(script is null ? why : "un nom de script est attendu, pas du code"));
            try { return _s.LoadString(script.Code, null, script.Name); }
            catch (SyntaxErrorException e) { return Pair(DynValue.Nil, DynValue.NewString(e.DecoratedMessage ?? e.Message)); }
        });
    }
}
