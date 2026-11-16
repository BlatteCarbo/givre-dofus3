using GivreClone.App.Services.LotM;
using GivreClone.Protocol;
using MoonSharp.Interpreter;
using Xunit;

namespace GivreClone.Tests;

internal sealed class FakeGame : ILotMGame, IDisposable
{
    private readonly FakeGameDb? _fakeDb;
    public GameDb? Db { get; }
    public DateTime UtcNow { get; set; } = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    public string GameVersion { get; set; } = "3.7.7.6";
    public bool CanAct { get; set; } = true;
    public bool HasHdv { get; set; } = true;
    public long CharacterId { get; set; } = 4242;
    public CharacterSheetData? SheetData { get; set; } = new(4242, "Njalla", "Féca", 50, 1_000_000, 1_200_000, 900, 1000, 8000, 10000, 123_456);
    public CharacterSheetData? Sheet() => SheetData;
    public SheetExtras Extras { get; set; } = new(Array.Empty<(int, string, int)>(), Array.Empty<(int, string, int)>(), Array.Empty<(int, int, string, int)>(), -1);
    public SheetExtras SheetMore() => Extras;
    public long CurrentMapId { get; set; }
    public PlaceOrigin? PlaceOrigin { get; set; }
    public IReadOnlyList<PlaceDoor> PlaceDoors(long mapId) => Array.Empty<PlaceDoor>();
    public bool InFight { get; set; }
    public int ServerId { get; set; } = 10;
    public Dictionary<int, int> JobLevels = new();
    public int JobLevel(int jobId) => JobLevels.GetValueOrDefault(jobId);
    public Dictionary<long, int> Items = new();
    public int ItemCount(long gid) => Items.GetValueOrDefault(gid);

    public AchievementLedger Achievements { get; } = new();
    public Dictionary<int, bool> ClaimResults = new();
    public List<int> Claims = new();
    public Task<bool> ClaimAchievementAsync(int id, CancellationToken ct)
    {
        Claims.Add(id);
        bool ok = ClaimResults.GetValueOrDefault(id, true);
        Achievements.OnRewardResult(id, ok);
        return Task.FromResult(ok);
    }
    public bool AutoClaim { get; set; } = true;   // comme l'original

    public List<AnomalyState> AnomalyList = new();
    public IReadOnlyList<AnomalyState> Anomalies() => AnomalyList;

    public OfferState? Offer { get; set; }
    public OfferState? OfferToDeliver { get; set; }
    public List<bool> Answers = new();
    public OfferState? CurrentOffer() => Offer;
    public async Task<OfferState?> WaitOfferAsync(int ms, CancellationToken ct)
    {
        await Task.Delay(Math.Min(ms, 200), ct);
        if (OfferToDeliver is { } o) { Offer = o; return o; }
        return null;
    }
    public bool AnswerOffer(bool accept) { Answers.Add(accept); Offer = null; return true; }

    public (bool Ok, string Reason, long MapId) DungeonExit { get; set; } = (true, "", 120063489);
    public Task<(bool Ok, string Reason, long MapId)> ExitDungeonAsync(CancellationToken ct) => Task.FromResult(DungeonExit);
    public int DocumentId { get; set; }
    public bool DocumentClosed;
    public int OpenDocumentId() => DocumentId;
    public bool CloseDocument() { DocumentClosed = true; DocumentId = 0; return true; }

    public Dictionary<long, int> Bank = new();
    public int BankKnownCount(long gid) => Bank.GetValueOrDefault(gid);
    public IReadOnlyList<LotMReader.PricePoint>? History { get; set; }
    public Task<IReadOnlyList<LotMReader.PricePoint>?> PriceHistoryAsync(long gid, CancellationToken ct) => Task.FromResult(History);
    public (long Gid, IReadOnlyList<KeepSize> Sizes, long Undercut, long? Price, long? Min)? KeepCall;
    public (int, string?) KeepResult = (2, null);
    public Task<(int Placed, string? Reason)> KeepOnSaleAsync(long gid, IReadOnlyList<KeepSize> sizes, long undercut, long? defaultPrice, long? defaultMin, CancellationToken ct)
    { KeepCall = (gid, sizes, undercut, defaultPrice, defaultMin); return Task.FromResult(KeepResult); }

    public HashSet<int> Finished = new(), Active = new();
    public IReadOnlyCollection<int> FinishedQuests() => Finished;
    public IReadOnlyCollection<int> ActiveQuests() => Active;
    public (bool, string) StartResult = (true, "");
    public List<int> StartedQuests = new();
    public Task<(bool Ok, string Reason)> StartQuestAsync(int questId, CancellationToken ct) { StartedQuests.Add(questId); return Task.FromResult(StartResult); }

    public bool ShopIsOpen { get; set; }
    public List<LotMReader.ShopItem> Shop = new();
    public Dictionary<int, ShopVisit> Visits = new();
    public List<int> OpenedShops = new();
    public bool ShopWillOpen = true;
    public bool ShopOpen() => ShopIsOpen;
    public IReadOnlyList<LotMReader.ShopItem> OpenShopItems() => Shop;
    public ShopVisit? LastShopVisit(int npcId) => Visits.GetValueOrDefault(npcId);
    public Task<bool> OpenShopAsync(int npcId, CancellationToken ct) { OpenedShops.Add(npcId); ShopIsOpen = ShopWillOpen; return Task.FromResult(ShopWillOpen); }
    public bool CloseShop() { var was = ShopIsOpen; ShopIsOpen = false; return was; }
    public List<(string Verb, int Gid, int Qty)> NpcCalls { get; } = new();
    public (bool Ok, int Done, string Reason) NpcResult { get; set; } = (true, 0, "");
    public Task<(bool Ok, int Done, string Reason)> NpcBuyAsync(int gid, int qty, CancellationToken ct) { NpcCalls.Add(("buy", gid, qty)); return Task.FromResult(NpcResult.Ok ? (true, qty, "") : NpcResult); }
    public Task<(bool Ok, int Done, string Reason)> NpcSellAsync(int gid, int qty, CancellationToken ct) { NpcCalls.Add(("sell", gid, qty)); return Task.FromResult(NpcResult.Ok ? (true, qty, "") : NpcResult); }
    public NpcSightingStore Sightings { get; } = new();

    public Dictionary<string, string> Scripts = new();
    public string? ReadScriptFile(string simpleName) => Scripts.GetValueOrDefault(simpleName);

    public FakeGame(bool withDb = true)
    {
        if (!withDb) return;
        _fakeDb = new FakeGameDb();
        Db = GameDb.TryOpen(_fakeDb.File);
    }

    public void Dispose() { Db?.Dispose(); _fakeDb?.Dispose(); }
}

public class LotMLuaModuleTests
{
    private sealed class Run : IDisposable
    {
        public readonly FakeGame Game;
        public readonly List<string> Out = new(), Warn = new();
        public readonly Script S = new(CoreModules.Preset_SoftSandbox);
        public readonly CancellationTokenSource Cts = new();

        public Run(FakeGame? game = null, bool multi = true)
        {
            Game = game ?? new FakeGame();
            S.Globals["print"] = DynValue.NewCallback((_, a) =>
            {
                var parts = new List<string>();
                for (int i = 0; i < a.Count; i++) parts.Add(a[i].ToPrintString());
                Out.Add(string.Join("\t", parts));
                return DynValue.Nil;
            });
            Table Mod(string name)
            {
                if (S.Globals.Get(name).Type == DataType.Table) return S.Globals.Get(name).Table;
                var t = new Table(S); S.Globals[name] = DynValue.NewTable(t); return t;
            }
            new LotMLuaModule(S, Game, Cts.Token, Mod, () => new OperationCanceledException(), Warn.Add).Bind();
        }

        public Run Do(string code) { S.DoString(code); return this; }
        public void Dispose() { Cts.Dispose(); Game.Dispose(); }
    }

    // ---------------------------------------------------------------- achievement
    [Fact]
    public void Achievement_reads_state_names_and_the_search_functions()
    {
        using var r = new Run();
        r.Game.Achievements.Seed(new[] { 7 });
        r.Game.Achievements.OnFinished(8, 42);
        r.Do("""
            print(achievement:getName(7), "[" .. achievement:getName(999) .. "]", tostring(achievement:isDone(7)), tostring(achievement:isDone(9)))
            print(table.concat(achievement:finished(), ","))
            local f = achievement:find("plaine")
            print(#f, f[1].id, f[1].name, f[1].category, f[1].points, tostring(f[1].done))
            print(#achievement:find("plaine", "done"), #achievement:find("plaine", "todo"), #achievement:find("", "all"))
            local c = achievement:category(4)
            print(#c, c[1].id, tostring(c[1].done), tostring(c[2].done))
            local o = achievement:objectives(8)
            print(#o, o[1].name, o[2].name, tostring(o[1].done), o[1].current, o[1].max)
            """);
        Assert.Equal(new[]
        {
            "Succès Alpha\t[]\ttrue\tfalse", "7,8", "2\t8\tExplorateur de la plaine\tExploration\t20\ttrue", "1\t1\t3", "2\t8\ttrue\tfalse",
            "2\tRamasser du blé\tVisiter la plaine\ttrue\t0\t0",
        }, r.Out);
    }

    [Fact]
    public void Achievement_rewards_are_listed_and_claimed_with_the_server_answer()
    {
        using var r = new Run();
        r.Game.Achievements.OnFinished(7, 30); r.Game.Achievements.OnFinished(8, 31); r.Game.ClaimResults[8] = false;
        r.Do("""
            print(achievement:rewardCount(), tostring(achievement:hasReward()), tostring(achievement:hasReward(7)), tostring(achievement:hasReward(5)))
            for _, a in ipairs(achievement:getRewardable()) do print(a.id, a.name, a.level) end
            print(tostring(achievement:claimReward(7)), tostring(achievement:claimReward(8)))
            print(achievement:rewardCount(), tostring(achievement:hasReward(7)), tostring(achievement:hasReward(8)))
            print(achievement:claimAllRewards())
            """);
        Assert.Equal(new[] { "2\ttrue\ttrue\tfalse", "7\tSuccès Alpha\t30", "8\tExplorateur de la plaine\t31", "true\tfalse", "1\tfalse\ttrue", "0" }, r.Out);   // 8 refusé : reste en attente, re-demandé par claimAll (refusé encore : 0 obtenu)
        Assert.Equal(new[] { 7, 8, 8 }, r.Game.Claims);
    }

    [Fact]
    public void Achievement_actions_need_the_gather_permission()
    {
        using var r = new Run();
        r.Game.CanAct = false;
        r.Game.Achievements.OnFinished(7, 1);
        r.Do("print(tostring(achievement:claimReward(7)), achievement:claimAllRewards(), tostring(achievement:setAutoClaim(true)), tostring(achievement:isAutoClaim()))");
        // sans le droit : aucune action ; l'indicateur garde le défaut de l'original (vrai) et setAutoClaim(true) est refusé (false)
        Assert.Equal("false\t0\tfalse\ttrue", r.Out.Single());
        Assert.Empty(r.Game.Claims);
    }

    [Fact]
    public void Auto_claim_defaults_to_on_when_called_without_argument_and_can_be_switched_off()
    {
        using var r = new Run();
        r.Do("print(tostring(achievement:setAutoClaim()), tostring(achievement:isAutoClaim()), tostring(achievement:setAutoClaim(false)), tostring(achievement:isAutoClaim()))");
        Assert.Equal("true\ttrue\tfalse\tfalse", r.Out.Single());
    }

    [Fact]
    public void Achievement_name_and_search_degrade_without_the_game_database()
    {
        using var r = new Run(new FakeGame(withDb: false));
        r.Do("print('[' .. achievement:getName(7) .. ']', #achievement:find('x'), #achievement:category(4), #achievement:objectives(8))");
        Assert.Equal("[]\t0\t0\t0", r.Out.Single());
    }

    // ---------------------------------------------------------------- anomaly
    [Fact]
    public void Anomaly_functions_use_the_bots_server_and_report_remaining_seconds()
    {
        using var r = new Run();
        long Epoch(int plus) => (long)(r.Game.UtcNow - DateTime.UnixEpoch).TotalSeconds + plus;
        r.Game.AnomalyList.Add(new AnomalyState(50, true, Epoch(900), 25, r.Game.UtcNow));
        r.Game.AnomalyList.Add(new AnomalyState(51, false, 0, 0, r.Game.UtcNow));
        r.Do("""
            print(tostring(anomaly:isOpen("Plaine")), tostring(anomaly:isOpen(50)), tostring(anomaly:isOpen(51)), tostring(anomaly:isOpen("inconnue")))
            print(anomaly:remaining("Plaine"), anomaly:remaining(51), anomaly:remaining("inconnue"))
            local a = anomaly:get("Plaine")
            print(a.subareaId, a.name, a.serverId, a.areaId, a.area, a.level, a.zaapMapId, a.remaining, a.bonus)
            print(tostring(anomaly:get(51)))
            local l = anomaly:list()
            print(#l, l[1].name)
            print(tostring(anomaly:isOpen("Plaine", 10)), tostring(anomaly:isOpen("Plaine", 11)), #anomaly:list(11), anomaly:remaining(50, 11))
            """);
        Assert.Equal(new[] { "true\ttrue\tfalse\tfalse", "900\t0\t0", "50\tPlaine\t10\t2\tTerres\t12\t777\t900\t25", "nil", "1\tPlaine", "true\tfalse\t0\t0" }, r.Out);
    }

    [Fact]
    public void Anomaly_list_is_sorted_by_remaining_time_most_first()
    {
        using var r = new Run();
        long Epoch(int plus) => (long)(r.Game.UtcNow - DateTime.UnixEpoch).TotalSeconds + plus;
        r.Game.AnomalyList.Add(new AnomalyState(50, true, Epoch(100), 0, r.Game.UtcNow));
        r.Game.AnomalyList.Add(new AnomalyState(51, true, Epoch(5000), 0, r.Game.UtcNow));
        r.Do("local l = anomaly:list() print(l[1].subareaId, l[2].subareaId, tostring(l[1].bonus), tostring(l[2].zaapMapId))");
        Assert.Equal("51\t50\tnil\t777", r.Out.Single());   // 51 n'existe pas dans la base : sans zaap ; bonus absent quand 0
    }

    // ---------------------------------------------------------------- teleport
    private static OfferState GroupOffer(DateTime at) => new(new TeleportOfferInfo("group", 777, "Njalla", 88213269, 0, 30), at);

    [Fact]
    public void Teleport_offer_table_and_age()
    {
        using var r = new Run();
        r.Do("print(tostring(teleport:offer()))");
        r.Game.Offer = GroupOffer(r.Game.UtcNow.AddSeconds(-12));
        r.Do("local o = teleport:offer() print(o.kind, o.requesterId, o.requesterName, o.mapId, o.dungeonId, o.timeLeft, o.age)");
        Assert.Equal(new[] { "nil", "group\t777\tNjalla\t88213269\t0\t30\t12" }, r.Out);
    }

    [Fact]
    public void Teleport_wait_returns_a_pending_offer_waits_for_a_new_one_or_gives_nil()
    {
        using var r = new Run();
        r.Do("print(tostring(teleport:wait(50)))");
        r.Game.OfferToDeliver = GroupOffer(r.Game.UtcNow);
        r.Do("print(teleport:wait(1000).kind)");
        r.Do("print(teleport:wait(1000).kind)");   // déjà en attente : rendue tout de suite
        Assert.Equal(new[] { "nil", "group", "group" }, r.Out);
    }

    [Fact]
    public void Teleport_accept_needs_permission_and_a_pending_offer_refuse_is_always_allowed()
    {
        using var r = new Run();
        r.Do("print(tostring(teleport:accept()), tostring(teleport:refuse()))");   // aucune offre
        r.Game.Offer = GroupOffer(r.Game.UtcNow); r.Game.CanAct = false;
        r.Do("print(tostring(teleport:accept()))");
        Assert.Empty(r.Game.Answers);
        r.Do("print(tostring(teleport:refuse()))");
        r.Game.Offer = GroupOffer(r.Game.UtcNow); r.Game.CanAct = true;
        r.Do("print(tostring(teleport:accept()))");
        Assert.Equal(new[] { "false\tfalse", "false", "true", "true" }, r.Out);
        Assert.Equal(new[] { false, true }, r.Game.Answers);
    }

    // ---------------------------------------------------------------- dungeon
    [Fact]
    public void Dungeon_current_isInside_and_info()
    {
        using var r = new Run();
        r.Game.CurrentMapId = 902;
        r.Do("""
            local d = dungeon:current()
            print(d.id, d.name, d.room, d.rooms, d.entranceMapId, d.exitMapId, d.optimalLevel, #d.mapIds, d.mapIds[2])
            print(d.keys[1].gid, d.keys[1].qty, d.keys[1].name, d.bosses[1].id, d.bosses[1].name)
            print(tostring(dungeon:isInside()))
            local i = dungeon:info(1)
            print(i.name, tostring(i.room), i.rooms)
            print(tostring(dungeon:info(99)))
            """);
        Assert.Equal(new[] { "1\tDonjon Bouftou\t2\t3\t900\t901\t30\t3\t902", "103\t1\tClé du Bouftou\t77\tBouftou Royal", "true", "Donjon Bouftou\tnil\t3", "nil" }, r.Out);
        r.Game.CurrentMapId = 5;
        r.Do("print(tostring(dungeon:current()), tostring(dungeon:isInside()))");
        Assert.Equal("nil\tfalse", r.Out[^1]);
    }

    [Fact]
    public void Dungeon_exit_reports_permission_fight_success_and_failure()
    {
        using var r = new Run();
        r.Game.CanAct = false;
        r.Do("print(dungeon:exit())");
        r.Game.CanAct = true; r.Game.InFight = true;
        r.Do("print(dungeon:exit())");
        r.Game.InFight = false;
        r.Do("print(dungeon:exit())");
        r.Game.DungeonExit = (false, "timeout", 0);
        r.Do("print(dungeon:exit())");
        Assert.Equal(new[] { "false\tnoperm", "false\tfight", "true\t120063489", "false\ttimeout" }, r.Out);
    }

    [Fact]
    public void Map_leaveDungeon_refuses_a_fight_and_reports_the_exit_result()
    {
        using var r = new Run();
        r.Game.CanAct = false;
        r.Do("print(map:leaveDungeon())");
        r.Game.CanAct = true; r.Game.InFight = true;
        r.Do("print(map:leaveDungeon())");
        r.Game.InFight = false;
        r.Do("print(map:leaveDungeon())");
        r.Game.DungeonExit = (false, "3", 11);
        r.Do("print(map:leaveDungeon())");
        Assert.Equal(new[] { "false\tnoperm", "false\tfight", "true\t120063489", "false\t3" }, r.Out);
    }

    // ---------------------------------------------------------------- document
    [Fact]
    public void Document_id_isOpen_close_and_get()
    {
        using var r = new Run();
        r.Do("print(document:id(), tostring(document:isOpen()), tostring(document:close()), tostring(document:get()))");
        r.Game.DocumentId = 17;
        r.Do("print(document:id(), tostring(document:isOpen()))");
        r.Do("local d = document:get() print(d.id, d.title, d.text, #d.links)");
        r.Do("local d = document:get(17) print(d.title)");
        r.Do("print(tostring(document:get(404)), tostring(document:close()), document:id())");
        Assert.Equal(new[] { "0\tfalse\tfalse\tnil", "17\ttrue", "17\tLégende\tIl était une fois\nun blé & une plaine\t0", "Légende", "nil\ttrue\t0" }, r.Out);
        Assert.True(r.Game.DocumentClosed);
    }

    // ---------------------------------------------------------------- data
    [Fact]
    public void Data_tables_get_recipe_and_sources()
    {
        using var r = new Run();
        r.Do("""
            local t = data:tables()
            local set = {} for _, n in ipairs(t) do set[n] = true end
            print(tostring(set.items), tostring(set.recipes), tostring(set.quests), tostring(set.translations), tostring(set.itemdata))
            local row = data:get("items", 100)
            print(row.id, row.name, row.level)
            print(row.resourcesBySubarea[1].values.Array[1])
            print(tostring(data:get("items", 9999)), tostring(data:get("nope", 1)))
            local rc = data:recipe(102)
            print(rc.name, rc.level, rc.job, rc.ingredients[1].name, rc.ingredients[1].quantity, rc.ingredients[2].name, rc.ingredients[2].quantity)
            print(tostring(data:recipe(100)))
            local s = data:sources(100)
            print(s.name, tostring(s.craft), s.harvest.skills[1], s.harvest.zones[1], s.monsters[1].name, s.monsters[1].dropRate)
            local b = data:sources("pain")
            print(b.craft.job, b.craft.level)
            print(data:sources("zzz"))
            """);
        Assert.Equal(new[]
        {
            "true\ttrue\ttrue\ttrue\tnil", "100\tBlé\t1", "50", "nil\tnil", "Pain\t10\tBoulanger\tFarine\t3\tBlé\t2", "nil",
            "Blé\tnil\tCueillette\tPlaine\tLarve\t12.5", "Boulanger\t10", "nil\tobjet inconnu",
        }, r.Out);
    }

    [Fact]
    public void Data_without_the_database_says_so()
    {
        using var r = new Run(new FakeGame(withDb: false));
        r.Do("print(#data:tables(), tostring(data:get('items', 1)), tostring(data:recipe(1)))  print(data:sources(1))");
        Assert.Equal("0\tnil\tnil", r.Out[0]);
        Assert.StartsWith("nil\tdonnées du jeu indisponibles", r.Out[1]);
    }

    // ---------------------------------------------------------------- job / quest
    [Fact]
    public void Job_recipes_default_to_the_characters_job_level_and_count_what_can_be_crafted_now()
    {
        using var r = new Run();
        r.Game.JobLevels[13] = 5;
        r.Game.Items[101] = 7; r.Game.Items[100] = 5;
        r.Do("""
            local l = job:recipes("Boulanger")
            print(#l, l[1].name, l[1].level)
            local all = job:recipes("Boulanger", 10)
            print(#all, all[1].name, all[2].name, tostring(all[1].craftableNow))
            local c = job:recipes(13, 10, true)
            print(c[1].craftableNow, c[2].craftableNow)
            print(job:recipes("inconnu"))
            """);
        Assert.Equal(new[] { "1\tFarine\t3", "2\tPain\tFarine\tnil", "2\t5", "nil\tmétier inconnu" }, r.Out);   // pain : min(7/3, 5/2) = 2 ; farine : 5/1 = 5
    }

    [Fact]
    public void Quest_find_filters_and_evaluates_the_start_state()
    {
        using var r = new Run();
        r.Game.Sheet_Level(2);   // niveau 2 : "PL>4" bloque la quête 10
        r.Do("""
            local l = quest:find("qu")
            print(#l, l[1].id, l[1].state.status, l[1].state.blockedBy[1], l[2].id, l[2].state.status, l[1].category, l[1].levelMin)
            print(#quest:find({ query = "qu", status = "blocked" }), #quest:find({ status = "available" }), #quest:find({ category = "annexe" }), #quest:find({ zone = "plaine" }))
            print(#quest:find({ max = 1 }))
            print(quest:find("zzz"))
            """);
        Assert.Equal(new[] { "2\t10\tblocked\tPL>4\t11\tavailable\tPrincipale\t5", "1\t1\t1\t1", "1", "nil\taucune quête" }, r.Out);
        r.Game.Finished.Add(10); r.Game.Active.Add(11);
        r.Do("local l = quest:find('qu') print(l[1].state.status, l[2].state.status)");
        Assert.Equal("done\tactive", r.Out[^1]);
    }

    [Fact]
    public void Quest_start_returns_the_servers_answer()
    {
        using var r = new Run();
        r.Do("print(quest:start(10))");
        r.Game.StartResult = (false, "refusé");
        r.Do("print(quest:start(11))  print(quest:start('x'))");
        Assert.Equal(new[] { "true\t", "false\trefusé", "false\tbadargs" }, r.Out);
        Assert.Equal(new[] { 10, 11 }, r.Game.StartedQuests);
    }

    // ---------------------------------------------------------------- character
    [Fact]
    public void Character_id_sheet_cost_and_switch()
    {
        using var r = new Run();
        r.Do("""
            print(character:id())
            local s = character:sheet()
            print(s.name, s.class, s.level, s.experience.total, s.experience.toNextLevel, s.experience.nextLevelAt, s.life, s.energy, s.kamas, s.characterId)
            print(character:getCostVitality(), character:getCostWisdom(), character:getCostStrenght(), character:getCostIntelligence(), character:getCostChance(), character:getCostAgility())
            print(character:switch("Autre"))
            """);
        Assert.Equal(new[] { "4242", "Njalla\tFéca\t50\t1000000\t1200000\t2200000\t900/1000\t8000/10000\t123456\t4242", "-1\t-1\t-1\t-1\t-1\t-1", "false\tchangement de perso ou de serveur non câblé dans le clone" }, r.Out);
        Assert.Single(r.Warn);   // comme l'original : un seul avertissement pour toute la famille getCost*
        Assert.Equal("character:getCost* : fonction Givre sans équivalent dans GivreClone — appel ignoré.", r.Warn[0]);
        r.Do("character:getCostVitality() character:getCostVitality()");
        Assert.Single(r.Warn);
    }

    [Fact]
    public void Sheet_has_the_key_set_of_the_original_with_life_and_energy_as_text()
    {
        using var r = new Run();
        r.Do("""
            local ks = {}
            for k in pairs(character:sheet()) do ks[#ks+1] = k end
            table.sort(ks)
            print(table.concat(ks, ","))
            print(type(character:sheet().life), type(character:sheet().energy), type(character:sheet().characteristics), type(character:sheet().characteristicsNote))
            """);
        // clés de l'original : characterId, characteristics, characteristicsNote, class, energy, equipment, experience, jobs, kamas, level, life, name, spells, unspentCharacteristicPoints
        // (unspentCharacteristicPoints absent ici : valeur inconnue = -1, jamais inventée)
        Assert.Equal("characterId,characteristics,characteristicsNote,class,energy,equipment,experience,jobs,kamas,level,life,name,spells", r.Out[0]);
        Assert.Equal("string\tstring\ttable\tstring", r.Out[1]);
    }

    [Fact]
    public void Sheet_lists_spells_jobs_and_equipment_by_position_plus_one_with_the_original_slot_names()
    {
        using var r = new Run();
        r.Game.Extras = new SheetExtras(
            new[] { (13561, "La Bloqueuse", 3) }, new[] { (36, "Pêcheur", 185) },
            new[] { (0, 8453, "Collier du Rat Blanc", 102), (1, 8450, "Rapière du Rat Blanc", 109), (15, 1, "Bouclier", 5), (31, 2, "Truc", 1), (12, 3, "Trophée", 1) }, 2);
        r.Do("""
            local s = character:sheet()
            print(s.unspentCharacteristicPoints, #s.spells, s.spells[1].name, s.spells[1].level, s.spells[1].spellId)
            print(#s.jobs, s.jobs[1].jobId, s.jobs[1].name, s.jobs[1].level)
            print(s.equipment[1].slot, s.equipment[1].position, s.equipment[1].gid, s.equipment[2].slot, s.equipment[13].slot, s.equipment[16].slot, s.equipment[32].slot, s.equipment[32].position)
            """);
        Assert.Equal(new[] { "2\t1\tLa Bloqueuse\t3\t13561", "1\t36\tPêcheur\t185", "amulet\t0\t8453\tweapon\tdofus / trophy\tshield\tslot 31\t31" }, r.Out);
    }

    [Fact]
    public void Achievement_auto_claim_is_on_by_default_like_the_original()
    {
        using var r = new Run();
        r.Do("print(tostring(achievement:isAutoClaim()))");
        Assert.Equal("true", r.Out[0]);
    }

    [Fact]
    public void Character_id_is_nil_and_sheet_explains_when_the_character_is_unknown()
    {
        using var r = new Run();
        r.Game.CharacterId = 0; r.Game.SheetData = null;
        r.Do("print(tostring(character:id())) print(character:sheet())");
        Assert.Equal("nil", r.Out[0]);
        Assert.StartsWith("nil\tfiche indisponible", r.Out[1]);
    }

    // ---------------------------------------------------------------- bank / sale
    [Fact]
    public void Bank_known_count()
    {
        using var r = new Run();
        r.Game.Bank[100] = 250;
        r.Do("print(bank:knownCount(100), bank:knownCount(5), bank:knownCount('x'))");
        Assert.Equal("250\t0\t0", r.Out.Single());
    }

    [Fact]
    public void Sale_price_history_returns_points_or_nil()
    {
        using var r = new Run();
        r.Do("print(tostring(sale:priceHistory(100)))");
        r.Game.History = new[] { new LotMReader.PricePoint("2026-10-10", 1500, 12), new LotMReader.PricePoint("2026-10-11", 1620, 7) };
        r.Do("local h = sale:priceHistory(100) print(#h, h[1].date, h[1].price, h[1].sold, h[2].price)");
        Assert.Equal(new[] { "nil", "2\t2026-10-10\t1500\t12\t1620" }, r.Out);
    }

    [Fact]
    public void KeepOnSale_parses_the_single_size_form()
    {
        using var r = new Run();
        r.Do("print(sale:keepOnSale(2303, 1000, 2, { price = 9000, minPrice = 8000, undercut = 5 }))");
        Assert.Equal("2\tnil", r.Out.Single());
        var c = r.Game.KeepCall!.Value;
        Assert.Equal((2303L, 5L, (long?)9000, (long?)8000), (c.Gid, c.Undercut, c.Price, c.Min));
        Assert.Equal(new[] { new KeepSize(1000, 2, null, null) }, c.Sizes);
    }

    [Fact]
    public void KeepOnSale_parses_the_table_form_with_givre_indexes_and_per_size_prices()
    {
        using var r = new Run();
        r.Game.KeepResult = (1, "nostock");
        r.Do("print(sale:keepOnSale(2303, { [1000] = 1, [100] = { count = 2, minPrice = 4500 }, [2] = 4 }))");   // [2] = index Givre = taille 10
        Assert.Equal("1	nostock", r.Out.Single());
        var sizes = r.Game.KeepCall!.Value.Sizes;
        Assert.Equal(3, sizes.Count);
        Assert.Contains(new KeepSize(100, 2, null, 4500), sizes);
        Assert.Contains(new KeepSize(1000, 1, null, null), sizes);
        Assert.Contains(new KeepSize(10, 4, null, null), sizes);
    }

    [Fact]
    public void KeepOnSale_refuses_the_same_size_given_twice()
    {
        using var r = new Run();
        r.Do("print(sale:keepOnSale(2303, { [3] = 1, [100] = 2 }))");   // 3 (index) et 100 désignent la même taille
        Assert.Equal("0	badargs", r.Out.Single());
        Assert.Null(r.Game.KeepCall);
    }

    [Fact]
    public void KeepOnSale_refuses_without_permission_with_bad_sizes_or_bad_arguments()
    {
        using var r = new Run();
        r.Do("print(sale:keepOnSale(1, 7, 1))  print(sale:keepOnSale(1, { [50] = 1 }))  print(sale:keepOnSale('x', 10, 1))");
        Assert.Equal(new[] { "0\tbadsize", "0\tbadsize", "0\tbadargs" }, r.Out);
        Assert.Null(r.Game.KeepCall);
        r.Game.HasHdv = false;
        r.Do("print(sale:keepOnSale(1, 10, 1))");
        Assert.Equal("0\tnoperm", r.Out[^1]);
    }

    // ---------------------------------------------------------------- npc / loadfile
    [Fact]
    public void Npc_shop_state_open_items_and_last_visit()
    {
        using var r = new Run();
        r.Game.Shop.Add(new LotMReader.ShopItem(100, 12, "PL>9"));
        r.Game.Visits[42] = new ShopVisit(new[] { new LotMReader.ShopItem(102, 40, "") }, r.Game.UtcNow.AddDays(-2), "3.7.5.5");
        r.Do("""
            print(tostring(npc:isShopOpen()), #npc:shopItems())
            print(tostring(npc:openShop(42)), tostring(npc:isShopOpen()))
            local it = npc:shopItems()[1]
            print(it.gid, it.name, it.price, it.criterion)
            local items, meta = npc:shopItems(42)
            print(#items, items[1].name, items[1].price, tostring(meta.olderGame), meta.seen > 0)
            print(#npc:shopItems(43))
            print(tostring(npc:closeShop()), tostring(npc:isShopOpen()))
            """);
        Assert.Equal(new[] { "false\t1", "true\ttrue", "100\tBlé\t12\tPL>9", "1\tPain\t40\ttrue\ttrue", "0", "true\tfalse" }, r.Out);
        Assert.Equal(new[] { 42 }, r.Game.OpenedShops);
    }

    [Fact]
    public void Npc_open_shop_needs_permission_and_reports_a_failed_open()
    {
        using var r = new Run();
        r.Game.CanAct = false;
        r.Do("print(tostring(npc:openShop(42)))");
        r.Game.CanAct = true; r.Game.ShopWillOpen = false;
        r.Do("print(tostring(npc:openShop(42)), tostring(npc:openShop('x')))");
        Assert.Equal(new[] { "false", "false\tfalse" }, r.Out);
    }

    [Fact]
    public void Npc_locate_lists_the_maps_where_a_bot_saw_the_npc_newest_first()
    {
        using var r = new Run();
        r.Game.Sightings.Record(42, 100, 12, 4, -17, r.Game.UtcNow.AddHours(-5), "3.7.7.6");
        r.Game.Sightings.Record(42, 200, 33, 5, -17, r.Game.UtcNow.AddHours(-1), "3.6.12.16");
        r.Do("""
            local l = npc:locate(42)
            print(#l, l[1].mapId, l[1].cell, l[1].x, l[1].y, tostring(l[1].olderGame), tostring(l[2].olderGame), l[1].seen > l[2].seen)
            print(#npc:locate(7))
            """);
        Assert.Equal(new[] { "2\t200\t33\t5\t-17\ttrue\tfalse\ttrue", "0" }, r.Out);
    }

    [Fact]
    public void Npc_buy_checks_shop_listing_price_and_permission_before_sending()
    {
        using var r = new Run();
        r.Game.Shop.Add(new LotMReader.ShopItem(6765, 4, ""));
        r.Game.Shop.Add(new LotMReader.ShopItem(6857, 40, ""));
        r.Do("""
            print(npc:buy(6765, 2))                 -- boutique fermée
            """);
        Assert.Equal("false\t0\tnoshop", r.Out[0]);
        r.Game.ShopIsOpen = true;
        r.Do("""
            print(npc:buy(1, 1))                    -- objet absent de la boutique
            print(npc:buy(6857, 1, 10))             -- 40 > prix maximal 10
            print(npc:buy(6765, 0))
            print(npc:buy("x", 1))
            print(npc:buy(6765, 3, 4))              -- exactement au prix maximal : accepté
            """);
        Assert.Equal(new[] { "false\t0\tnoshop", "false\t0\tnotsold", "false\t0\tprice", "false\t0\tbadargs", "false\t0\tbadargs", "true\t3" }, r.Out);
        Assert.Equal(new[] { ("buy", 6765, 3) }, r.Game.NpcCalls.ToArray());
    }

    [Fact]
    public void Npc_sell_and_failures_report_the_reason_and_the_quantity_done()
    {
        using var r = new Run();
        r.Game.ShopIsOpen = true;
        r.Do("print(npc:sell(55, 4))");
        Assert.Equal("true\t4", r.Out[0]);
        r.Game.NpcResult = (false, 1, "refused");
        r.Do("print(npc:sell(55, 4))");
        Assert.Equal("false\t1\trefused", r.Out[1]);
        r.Game.CanAct = false;
        r.Do("print(npc:sell(55, 1)) print(npc:buy(1, 1))");
        Assert.Equal("false\t0\tnoperm", r.Out[2]);
        Assert.Equal("false\t0\tnoperm", r.Out[3]);
    }

    [Fact]
    public void BuyFromNpc_opens_buys_and_closes_only_when_it_opened_the_shop()
    {
        using var r = new Run();
        r.Game.Shop.Add(new LotMReader.ShopItem(6765, 4, ""));
        r.Do("print(buyFromNpc(42, 6765, 2))");
        Assert.Equal("true\t2", r.Out[0]);
        Assert.Equal(new[] { 42 }, r.Game.OpenedShops.ToArray());
        Assert.False(r.Game.ShopIsOpen);                   // refermée : elle l'avait ouverte
        r.Game.ShopIsOpen = true;                           // déjà ouverte par le script
        r.Do("print(buyFromNpc(42, 6765, 1))");
        Assert.True(r.Game.ShopIsOpen);                    // laissée ouverte
        r.Game.ShopIsOpen = false; r.Game.ShopWillOpen = false;
        r.Do("print(buyFromNpc(42, 6765, 1))");
        Assert.Equal("false\t0\tnoshop", r.Out[2]);
    }

    [Fact]
    public void Loadfile_compiles_a_library_script_by_name_only()
    {
        using var r = new Run();
        r.Game.Scripts["utile.lua"] = "return 6 * 7";
        r.Game.Scripts["casse.lua"] = "return (";
        r.Do("""
            local f = loadfile("utile")
            print(f())
            print(loadfile("inconnu"))
            local g, err = loadfile("casse")
            print(tostring(g), err ~= nil)
            print(loadfile("../secret"))
            print(loadfile("print('x')"))
            """);
        Assert.Equal("42", r.Out[0]);
        Assert.Equal("nil\tscript introuvable : inconnu", r.Out[1]);
        Assert.Equal("nil\ttrue", r.Out[2]);
        Assert.Equal("nil\tun script se désigne par son nom, jamais par un chemin", r.Out[3]);
        Assert.Equal("nil\tun nom de script est attendu, pas du code", r.Out[4]);
    }

    [Fact]
    public void Calls_work_with_colon_and_dot_syntax()
    {
        using var r = new Run();
        r.Game.Bank[100] = 3;
        r.Do("print(bank:knownCount(100), bank.knownCount(100), anomaly.remaining(1), dungeon.isInside() == false)");
        Assert.Equal("3\t3\t0\ttrue", r.Out.Single());
    }

    [Fact]
    public void A_cancelled_blocking_call_unwinds_through_the_stop_exception()
    {
        using var r = new Run();
        r.Cts.Cancel();
        r.Game.Achievements.OnFinished(7, 1);
        var ex = Assert.ThrowsAny<Exception>(() => r.Do("teleport:wait(60000)"));
        Assert.True(ex is OperationCanceledException || ex.InnerException is OperationCanceledException);
    }
}

internal static class FakeGameExt
{
    public static void Sheet_Level(this FakeGame g, int level) => g.SheetData = g.SheetData! with { Level = level };
}
