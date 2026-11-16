extern alias F06App;

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using GivreClone.Protocol;
using Xunit;
using MM = F06App::GivreClone.App.Services.Metamob;
using VM = F06App::GivreClone.App.ViewModels;

namespace GivreClone.Tests;

/// <summary>Contrat Metamob sur un handler HTTP fictif. La clé ci-dessous est une fixture : le fichier réel n'est jamais ouvert.</summary>
public sealed class MetamobSyncTests
{
    private const string Key = "unit-test-key";
    private const string Slug = "a1b2c3d4";

    private const string ServersJson =
        "{\"data\":[{\"id\":7,\"name\":\"Imagiro\",\"game_version\":{\"name\":\"3.0\"},\"community\":\"fr\"},{\"id\":8,\"name\":\"Orukam\",\"game_version\":{\"name\":\"\"}},{\"id\":0,\"name\":\"Zero\"},{\"name\":\"Sans\"}]}";

    private const string QuestsJson =
        "{\"data\":[" +
        "{\"slug\":\"a1b2c3d4\",\"character_name\":\"Héros\",\"server\":{\"id\":7,\"name\":\"Imagiro\"},\"quest_template\":{\"quest_type\":{\"name\":{\"fr\":\"Âmes\",\"en\":\"Souls\"}},\"monster_count\":12},\"current_step\":3,\"parallel_quests\":1,\"show_trades\":false}," +
        "{\"slug\":\"bbbbbbbb\",\"character_name\":\"Second\",\"server\":{\"id\":7,\"name\":\"Imagiro\"},\"quest_template\":{\"quest_type\":{\"name\":{\"en\":\"Souls\"}}}}," +
        "{\"slug\":\"cccccccc\",\"character_name\":\"\",\"server\":{\"id\":8,\"name\":\"Orukam\"}}," +
        "{\"slug\":\"a/b\",\"character_name\":\"Bad\",\"server\":{\"id\":7,\"name\":\"Imagiro\"}}," +
        "{\"slug\":\"noserver\",\"character_name\":\"X\"}" +
        "]}";

    [Fact]
    public void Key_file_reads_only_the_path_passed_by_the_caller()
    {
        var root = Path.Combine(Path.GetTempPath(), "givre-metamob-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Null(MM.MetamobKeyFile.TryRead(Path.Combine(root, "missing.key")));
            var empty = Path.Combine(root, "empty.key");
            File.WriteAllText(empty, "  \r\n");
            Assert.Null(MM.MetamobKeyFile.TryRead(empty));
            var prefixed = Path.Combine(root, "prefixed.key");
            File.WriteAllText(prefixed, " Bearer " + Key + " \n");
            Assert.Equal(Key, MM.MetamobKeyFile.TryRead(prefixed));
            Assert.EndsWith(Path.Combine("GivreReplica", "secrets", "metamob.key"), MM.MetamobKeyFile.DefaultPath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Errors_drop_the_key_and_auth_failures_keep_a_fixed_message()
    {
        Assert.True(MM.MetamobText.IsSlug("ab_cd-12"));
        Assert.False(MM.MetamobText.IsSlug("a/b"));
        Assert.False(MM.MetamobText.IsSlug(""));
        Assert.False(MM.MetamobText.IsSlug(new string('a', 129)));
        Assert.Equal("nope ab", MM.MetamobText.Scrub("nope ab", "ab"));
        Assert.Equal("nope [clé]", MM.MetamobText.Scrub("nope " + Key, Key));
        var refused = MM.MetamobText.PublicError(401, "{\"message\":\"" + Key + "\"}", Key);
        Assert.Equal("Clé refusée.", refused);
        Assert.Equal("Clé refusée.", MM.MetamobText.PublicError(403, "{\"error\":\"" + Key + "\"}", Key));
        Assert.DoesNotContain(Key, refused);
        Assert.Equal("[clé] refusée", MM.MetamobText.PublicError(422, "{\"messages\":{\"error\":\"" + Key + " refusée\",\"quantity\":\"bad\"}}", Key));
        Assert.Equal("quantity: trop grand", MM.MetamobText.PublicError(422, "{\"messages\":{\"quantity\":\"trop grand\"}}", Key));
        Assert.Equal("HTTP 418", MM.MetamobText.PublicError(418, "not json", Key));
        Assert.Equal("Réseau indisponible.", MM.MetamobText.PublicError(0, null, Key));
        var ex = Assert.Throws<ArgumentException>(() => new MM.MetamobClient("   "));
        Assert.StartsWith("Clé API absente.", ex.Message);
        Assert.DoesNotContain(Key, ex.Message);
    }

    [Fact]
    public void Row_labels_are_french()
    {
        var updated = new MM.MetamobSyncRow(MM.SoulSyncOutcome.Updated, 12, "Bouftou", 4, 0, "4 → 0");
        var skipped = new MM.MetamobSyncRow(MM.SoulSyncOutcome.Skipped, 12, "Bouftou", 0, 0, "déjà 0");
        var failed = new MM.MetamobSyncRow(MM.SoulSyncOutcome.Failed, 12, "Bouftou", 4, 0, "absent de la réponse");
        Assert.Equal("Mis à jour", updated.KindLabel);
        Assert.Equal("Ignoré", skipped.KindLabel);
        Assert.Equal("Échec", failed.KindLabel);
        Assert.Equal("Mis à jour · 12 · Bouftou", updated.Line);
    }

    [Fact]
    public async Task Decoded_effects_do_not_name_the_monster_so_a_stone_is_not_written()
    {
        var handler = new FakeHandler();
        var stone = Stone();
        var report = await Execute(handler, Request(Ready(stone, stone with { Uid = 8, StackQuantity = 2 })));
        AssertNoHttp(handler, report);
        Assert.Equal(MM.SoulStoneContents.UnknownReason, report.Status);
        Assert.Equal(MM.SoulStoneContents.CaptureNeeded, report.CaptureNeeded);
        Assert.Contains("monster_count", report.CaptureNeeded);
        Assert.DoesNotContain("77", report.Status);
        Assert.DoesNotContain("12345", report.CaptureNeeded);
        Assert.Equal(705, stone.Effects[0].ActionId);
        Assert.Equal(77, stone.Effects[0].Value);
        Assert.Equal(12345, stone.Effects[0].ObjectGid);
    }

    [Fact]
    public async Task Same_uid_and_gid_stays_unknown_and_two_gids_are_inconsistent()
    {
        var same = new FakeHandler();
        var sameReport = await Execute(same, Request(Ready(Stone(5), Stone(5, 9686, 3))));
        AssertNoHttp(same, sameReport);
        Assert.Equal(MM.SoulStoneContents.UnknownReason, sameReport.Status);

        var split = new FakeHandler();
        var splitReport = await Execute(split, Request(Ready(Stone(5, 9686), Stone(5, 9687))));
        AssertNoHttp(split, splitReport);
        Assert.Equal(MM.SoulStoneContents.InconsistentUid, splitReport.Status);
        Assert.Equal(MM.SoulStoneContents.CaptureNeeded, splitReport.CaptureNeeded);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Unseeded_store_blocks_even_for_a_bag_only_scope(bool bag, bool chest)
    {
        var handler = new FakeHandler();
        var holdings = new MM.SoulHoldingsSnapshot(bag, chest, Array.Empty<MM.SoulStoneObservation>(), Array.Empty<MM.SoulStoneObservation>());
        var report = await Execute(handler, Request(holdings, MM.SoulScope.Bag));
        AssertNoHttp(handler, report);
        Assert.Equal(bag ? MM.SoulStock.ChestMissing : MM.SoulStock.BagMissing, report.Status);
        Assert.Null(report.CaptureNeeded);
    }

    [Fact]
    public async Task Empty_effect_list_is_not_treated_as_an_empty_stone()
    {
        var handler = new FakeHandler();
        var stone = new MM.SoulStoneObservation(9, 9686, 1, Array.Empty<InventoryReader.ItemEffect>());
        var report = await Execute(handler, Request(Ready(stone)));
        AssertNoHttp(handler, report);
        Assert.Equal(MM.SoulStoneContents.UnknownReason, report.Status);
        Assert.Equal(MM.SoulStoneContents.CaptureNeeded, report.CaptureNeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Stone_outside_the_scope_blocks_the_whole_sync(int scopeValue)
    {
        var scope = (MM.SoulScope)scopeValue;
        var stone = Stone();
        var holdings = scope == MM.SoulScope.Bag
            ? Ready(Array.Empty<MM.SoulStoneObservation>(), new[] { stone })
            : Ready(new[] { stone }, Array.Empty<MM.SoulStoneObservation>());
        var handler = new FakeHandler();
        var report = await Execute(handler, Request(holdings, scope));
        AssertNoHttp(handler, report);
        Assert.Equal(MM.SoulStock.OutsideScope, report.Status);
        Assert.Equal(MM.SoulStoneContents.CaptureNeeded, report.CaptureNeeded);
    }

    [Fact]
    public async Task Unknown_contents_win_over_a_bad_quest_or_server()
    {
        var handler = new FakeHandler();
        var report = await Execute(handler, Request(Ready(Stone()), slug: "a/b", questServerId: 8));
        AssertNoHttp(handler, report);
        Assert.Equal(MM.SoulStoneContents.UnknownReason, report.Status);
    }

    [Fact]
    public async Task Bad_slug_or_server_is_refused_before_http()
    {
        var slug = new FakeHandler();
        var slugReport = await Execute(slug, Request(Ready(), slug: "a/b"));
        AssertNoHttp(slug, slugReport);
        Assert.Equal(MM.SoulStock.InvalidQuest, slugReport.Status);

        var server = new FakeHandler();
        var serverReport = await Execute(server, Request(Ready(), questServerId: 8));
        AssertNoHttp(server, serverReport);
        Assert.Equal(MM.SoulStock.WrongServer, serverReport.Status);
    }

    [Fact]
    public async Task Ignored_items_leave_a_known_empty_census_that_sends_absolute_zeros()
    {
        var holdings = Ready(new[]
        {
            Stone(1, 9686, 0),
            new MM.SoulStoneObservation(2, 123, 5, new[] { Rich() }),
            Stone(0, 9686, 4),
        }, new[]
        {
            new MM.SoulStoneObservation(3, 100, 1, new[] { Rich() }),
        });
        var handler = new FakeHandler();
        handler.Respond = (seen, _) => seen.Method == "GET"
            ? Json(200, Page(2, (4, 4, "Bouftou Royal"), (5, 0, "Tofu")))
            : Json(200, Ack((4, 0)));
        var report = await Execute(handler, Request(holdings, MM.SoulScope.Bag));
        AssertSafe(handler);
        var patch = Assert.Single(handler.Calls, call => call.Method == "PATCH");
        Assert.Equal("/api/v1/quests/" + Slug + "/monsters", patch.Path);
        Assert.Equal(new[] { 4 }, PatchQuantities(patch.Body).Keys.ToArray());
        Assert.Equal(0, PatchQuantities(patch.Body)[4]);
        Assert.Contains("\"quantity\":0", patch.Body);
        Assert.True(report.SentWrite);
        Assert.DoesNotContain(report.Rows, row => row.MonsterId is 77 or 12345 or 705 or 123);
        Assert.Equal("4 → 0", report.Rows.Single(row => row.MonsterId == 4).Detail);
        Assert.Equal("déjà 0", report.Rows.Single(row => row.MonsterId == 5).Detail);
        Assert.Equal("Mis à jour : 1. Ignorés : 1. Échecs : 0.", report.Status);
    }

    [Fact]
    public async Task Repeating_the_same_empty_census_does_not_patch_again()
    {
        var remote = new Dictionary<int, int> { [4] = 4, [5] = 0 };
        var handler = new FakeHandler();
        handler.Respond = (seen, _) =>
        {
            if (seen.Method == "GET")
                return Json(200, Page(remote.Count, remote.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value, pair.Key == 4 ? "Bouftou Royal" : "Tofu")).ToArray()));
            foreach (var pair in PatchQuantities(seen.Body)) remote[pair.Key] = pair.Value;
            return Json(200, Echo(seen.Body));
        };
        using var client = Client(handler);
        var request = Request(Ready());
        var first = await MM.MetamobSync.ExecuteAsync(client, request, new MM.SoulSyncStop());
        var second = await MM.MetamobSync.ExecuteAsync(client, request, new MM.SoulSyncStop());
        AssertSafe(handler);
        Assert.Equal(new[] { "GET", "PATCH", "GET" }, handler.Calls.Select(call => call.Method).ToArray());
        Assert.Contains("/api/v1/quests/" + Slug + "/monsters?limit=200&offset=0", handler.Calls[0].Path);
        Assert.True(first.SentWrite);
        Assert.False(second.SentWrite);
        Assert.Equal("Mis à jour · 4 · Bouftou Royal", first.Rows.Single(row => row.MonsterId == 4).Line);
        Assert.Equal("déjà 0", second.Rows.Single(row => row.MonsterId == 4).Detail);
        Assert.Equal("Mis à jour : 0. Ignorés : 2. Échecs : 0.", second.Status);
    }

    [Fact]
    public async Task Empty_remote_list_sends_nothing()
    {
        var handler = new FakeHandler();
        handler.Respond = (_, _) => Json(200, Page(0));
        var report = await Execute(handler, Request(Ready()));
        AssertSafe(handler);
        Assert.Single(handler.Calls);
        Assert.False(report.SentWrite);
        Assert.Empty(report.Rows);
        Assert.Equal("Mis à jour : 0. Ignorés : 0. Échecs : 0.", report.Status);
    }

    [Fact]
    public async Task Monster_pages_are_followed_then_one_patch_is_sent()
    {
        var handler = new FakeHandler();
        handler.Respond = (seen, _) =>
        {
            if (seen.Method != "GET") return Json(200, Echo(seen.Body));
            if (seen.Path.Contains("offset=0", StringComparison.Ordinal)) return Json(200, Page(3, (1, 1, "A"), (2, 1, "B")));
            if (seen.Path.Contains("offset=2", StringComparison.Ordinal)) return Json(200, Page(3, (3, 1, "C")));
            return Json(500, "{\"message\":\"page\"}");
        };
        var report = await Execute(handler, Request(Ready(), pageSize: 2));
        AssertSafe(handler);
        Assert.Equal(2, handler.Calls.Count(call => call.Method == "GET"));
        Assert.Contains("limit=2", handler.Calls[0].Path);
        var patch = Assert.Single(handler.Calls, call => call.Method == "PATCH");
        Assert.Equal(new[] { 1, 2, 3 }, PatchQuantities(patch.Body).Keys.OrderBy(id => id).ToArray());
        Assert.All(PatchQuantities(patch.Body).Values, qty => Assert.Equal(0, qty));
        Assert.Equal(3, report.Rows.Count(row => row.Outcome == MM.SoulSyncOutcome.Updated));
        Assert.True(report.SentWrite);
    }

    [Fact]
    public async Task Stop_during_an_incomplete_page_sends_nothing()
    {
        var stop = new MM.SoulSyncStop();
        var handler = new FakeHandler();
        handler.Respond = (seen, _) =>
        {
            stop.Request();
            return Json(200, Page(4, (1, 1, "A"), (2, 1, "B")));
        };
        var report = await Execute(handler, Request(Ready(), pageSize: 2), stop);
        AssertSafe(handler);
        Assert.Single(handler.Calls);
        Assert.Equal("GET", handler.Calls[0].Method);
        Assert.False(report.SentWrite);
        Assert.Empty(report.Rows);
        Assert.Equal("Arrêté avant l'envoi. Aucune écriture.", report.Status);
    }

    [Fact]
    public async Task Stop_after_a_complete_read_skips_the_pending_batch()
    {
        var stop = new MM.SoulSyncStop();
        var handler = new FakeHandler();
        handler.Respond = (_, _) =>
        {
            stop.Request();
            return Json(200, Page(1, (4, 4, "A")));
        };
        var report = await Execute(handler, Request(Ready()), stop);
        AssertSafe(handler);
        Assert.Single(handler.Calls);
        Assert.False(report.SentWrite);
        Assert.Equal("Arrêté avant le lot suivant.", report.Rows.Single().Detail);
        Assert.Equal("Arrêté avant le lot suivant. Mis à jour : 0. Ignorés : 1. Échecs : 0.", report.Status);
    }

    [Fact]
    public async Task Stop_already_requested_does_not_call_http()
    {
        var stop = new MM.SoulSyncStop();
        stop.Request();
        var handler = new FakeHandler();
        using var client = Client(handler);
        var report = await MM.MetamobSync.PushAbsoluteAsync(client, Slug, new Dictionary<int, int> { [1] = 1 }, stop);
        Assert.Empty(handler.Calls);
        Assert.False(report.SentWrite);
        Assert.Equal("Arrêté avant l'envoi. Aucune écriture.", report.Status);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = await MM.MetamobSync.PushAbsoluteAsync(client, Slug, new Dictionary<int, int> { [1] = 1 }, new MM.SoulSyncStop(), cancellationToken: cts.Token);
        Assert.Empty(handler.Calls);
        Assert.Equal("Arrêté avant l'envoi. Aucune écriture.", cancelled.Status);
    }

    [Fact]
    public async Task Stop_during_a_patch_lets_that_batch_finish_and_skips_the_next()
    {
        var stop = new MM.SoulSyncStop();
        var handler = new FakeHandler();
        handler.Respond = (seen, _) =>
        {
            if (seen.Method == "GET") return Json(200, Page(3, (1, 1, "A"), (2, 1, "B"), (3, 1, "C")));
            stop.Request();
            return Json(200, Echo(seen.Body));
        };
        using var client = Client(handler);
        var report = await MM.MetamobSync.PushAbsoluteAsync(client, Slug, new Dictionary<int, int>(), stop, batchSize: 1);
        AssertSafe(handler);
        var patch = Assert.Single(handler.Calls, call => call.Method == "PATCH");
        Assert.Equal(new[] { 1 }, PatchQuantities(patch.Body).Keys.ToArray());
        Assert.Equal("1 → 0", report.Rows.Single(row => row.Outcome == MM.SoulSyncOutcome.Updated).Detail);
        Assert.Equal(2, report.Rows.Count(row => row.Detail == "Arrêté avant le lot suivant."));
        Assert.Equal("Arrêté avant le lot suivant. Mis à jour : 1. Ignorés : 2. Échecs : 0.", report.Status);
        Assert.True(report.SentWrite);
    }

    [Theory]
    [InlineData(422, "no")]
    [InlineData(500, "boom")]
    public async Task Failed_write_is_not_retried_and_later_batches_are_skipped(int status, string message)
    {
        var delays = new List<TimeSpan>();
        var handler = new FakeHandler();
        handler.Respond = (seen, _) => seen.Method == "GET"
            ? Json(200, Page(3, (1, 1, "A"), (2, 1, "B"), (3, 1, "C")))
            : Json(status, "{\"message\":\"" + message + "\"}");
        using var client = Client(handler, delays);
        var report = await MM.MetamobSync.PushAbsoluteAsync(client, Slug, new Dictionary<int, int>(), new MM.SoulSyncStop(), batchSize: 1);
        AssertSafe(handler);
        Assert.Empty(delays);
        Assert.Single(handler.Calls, call => call.Method == "PATCH");
        Assert.True(report.SentWrite);
        Assert.Equal(message, report.Rows.Single(row => row.Outcome == MM.SoulSyncOutcome.Failed).Detail);
        Assert.Equal(2, report.Rows.Count(row => row.Detail == "Non envoyé : le lot précédent a échoué."));
        Assert.Equal("Envoi arrêté après un lot en échec. Mis à jour : 0. Ignorés : 2. Échecs : 1.", report.Status);
        Assert.Equal(new[] { MM.SoulSyncOutcome.Failed, MM.SoulSyncOutcome.Skipped, MM.SoulSyncOutcome.Skipped }, report.Rows.Select(row => row.Outcome).ToArray());
    }

    [Fact]
    public async Task Retry_after_is_honored_until_sixty_seconds()
    {
        var delays = new List<TimeSpan>();
        var patches = 0;
        var handler = new FakeHandler();
        handler.Respond = (seen, _) =>
        {
            if (seen.Method == "GET") return Json(200, Page(1, (1, 4, "A")));
            return ++patches == 1
                ? Json(429, "{\"message\":\"ralentir\"}", TimeSpan.FromSeconds(1))
                : Json(200, Ack((1, 0)));
        };
        using var client = Client(handler, delays);
        var report = await MM.MetamobSync.PushAbsoluteAsync(client, Slug, new Dictionary<int, int>(), new MM.SoulSyncStop());
        AssertSafe(handler);
        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(delays));
        Assert.Equal(2, handler.Calls.Count(call => call.Method == "PATCH"));
        Assert.Equal("4 → 0", report.Rows.Single().Detail);
        Assert.True(report.SentWrite);
    }

    [Fact]
    public async Task Retry_after_beyond_sixty_seconds_is_not_waited()
    {
        var delays = new List<TimeSpan>();
        var handler = new FakeHandler();
        handler.Respond = (seen, _) => seen.Method == "GET"
            ? Json(200, Page(1, (1, 4, "A")))
            : Json(429, "{\"message\":\"ralentir\"}", TimeSpan.FromSeconds(61));
        using var client = Client(handler, delays);
        var report = await MM.MetamobSync.PushAbsoluteAsync(client, Slug, new Dictionary<int, int>(), new MM.SoulSyncStop());
        AssertSafe(handler);
        Assert.Empty(delays);
        Assert.Single(handler.Calls, call => call.Method == "PATCH");
        Assert.Equal("ralentir", report.Rows.Single().Detail);
        Assert.True(report.SentWrite);
    }

    [Fact]
    public async Task Get_500_is_retried_and_then_a_write_is_not_required()
    {
        var delays = new List<TimeSpan>();
        var gets = 0;
        var handler = new FakeHandler();
        handler.Respond = (seen, _) =>
        {
            Assert.Equal("GET", seen.Method);
            return ++gets == 1 ? Json(500, "{\"message\":\"temp\"}") : Json(200, Page(0));
        };
        using var client = Client(handler, delays);
        var report = await MM.MetamobSync.PushAbsoluteAsync(client, Slug, new Dictionary<int, int>(), new MM.SoulSyncStop());
        AssertSafe(handler);
        Assert.Equal(TimeSpan.FromMilliseconds(500), Assert.Single(delays));
        Assert.Equal(2, gets);
        Assert.DoesNotContain(handler.Calls, call => call.Method == "PATCH");
        Assert.False(report.SentWrite);
        Assert.Equal("Mis à jour : 0. Ignorés : 0. Échecs : 0.", report.Status);
    }

    [Fact]
    public async Task Prepared_census_skips_out_of_range_and_monsters_absent_from_the_quest()
    {
        var handler = new FakeHandler();
        handler.Respond = (seen, _) => seen.Method == "GET"
            ? Json(200, "{\"data\":{\"monsters\":[" +
                "{\"id\":1,\"quantity\":0,\"name\":{\"fr\":\"\"}}," +
                "{\"id\":2,\"quantity\":0,\"name\":{\"fr\":\"Deux\"}}," +
                "{\"id\":3,\"quantity\":0,\"name\":{\"fr\":\"Trois\"}}," +
                "{\"id\":4,\"quantity\":5,\"name\":{\"fr\":\"Quatre\"}}],\"pagination\":{\"total\":4}}}")
            : Json(200, Echo(seen.Body));
        using var client = Client(handler);
        var absolute = new Dictionary<int, int> { [1] = 31, [2] = -1, [3] = 30, [99] = 4, [100] = 0 };
        var report = await MM.MetamobSync.PushAbsoluteAsync(client, Slug, absolute, new MM.SoulSyncStop());
        AssertSafe(handler);
        var patch = Assert.Single(handler.Calls, call => call.Method == "PATCH");
        var sent = PatchQuantities(patch.Body);
        Assert.Equal(new[] { 3, 4 }, sent.Keys.OrderBy(id => id).ToArray());
        Assert.Equal(30, sent[3]);
        Assert.Equal(0, sent[4]);
        Assert.Equal("quantité 31 hors limite 0-30, non envoyée", report.Rows.Single(row => row.MonsterId == 1).Detail);
        Assert.Equal("quantité -1 hors limite 0-30, non envoyée", report.Rows.Single(row => row.MonsterId == 2).Detail);
        Assert.Equal("1", report.Rows.Single(row => row.MonsterId == 1).Name);
        Assert.Equal("absent de la quête, non envoyé", report.Rows.Single(row => row.MonsterId == 99).Detail);
        Assert.DoesNotContain(report.Rows, row => row.MonsterId == 100);
        Assert.Contains(report.Rows, row => row.MonsterId == 3 && row.Outcome == MM.SoulSyncOutcome.Updated);
    }

    [Fact]
    public async Task Duplicate_short_or_oversized_pages_are_not_patched()
    {
        await AssertUnreadable(seen => seen.Path.Contains("offset=2", StringComparison.Ordinal)
            ? Page(3, (1, 1, "A"))
            : Page(3, (1, 1, "A"), (2, 1, "B")), "Liste des monstres illisible.");
        await AssertUnreadable(seen => seen.Path.Contains("offset=2", StringComparison.Ordinal)
            ? Page(5, (3, 1, "C"))
            : Page(5, (1, 1, "A"), (2, 1, "B")), "Liste des monstres incomplète.");
        await AssertUnreadable(_ => Page(1, (1, 1, "A"), (2, 1, "B")), "Liste des monstres illisible.", 1);
    }

    [Fact]
    public async Task Fifty_full_pages_without_a_total_are_incomplete()
    {
        var gets = 0;
        var handler = new FakeHandler();
        handler.Respond = (seen, _) =>
        {
            Assert.Equal("GET", seen.Method);
            var id = ++gets;
            return Json(200, Page(null, (id, 1, "M")));
        };
        using var client = Client(handler);
        var report = await MM.MetamobSync.PushAbsoluteAsync(client, Slug, new Dictionary<int, int>(), new MM.SoulSyncStop(), pageSize: 1);
        AssertSafe(handler);
        Assert.Equal(50, gets);
        Assert.DoesNotContain(handler.Calls, call => call.Method == "PATCH");
        Assert.False(report.SentWrite);
        Assert.Equal("Liste des monstres incomplète. Aucune écriture.", report.Status);
    }

    [Fact]
    public async Task Ack_must_repeat_each_sent_quantity()
    {
        var handler = new FakeHandler();
        handler.Respond = (seen, _) =>
        {
            if (seen.Method == "GET") return Json(200, Page(2, (1, 4, "A"), (2, 4, "B")));
            return Json(200, "{\"data\":{\"monsters\":[{\"monster_id\":1,\"quantity\":9}]}}");
        };
        using var client = Client(handler);
        var report = await MM.MetamobSync.PushAbsoluteAsync(client, Slug, new Dictionary<int, int>(), new MM.SoulSyncStop());
        AssertSafe(handler);
        Assert.True(report.SentWrite);
        Assert.Equal("accusé 9, attendu 0", report.Rows.Single(row => row.MonsterId == 1).Detail);
        Assert.Equal("absent de la réponse", report.Rows.Single(row => row.MonsterId == 2).Detail);
        Assert.Equal(new[] { MM.SoulSyncOutcome.Failed, MM.SoulSyncOutcome.Failed }, report.Rows.Select(row => row.Outcome).ToArray());
        Assert.Equal(new[] { 1, 2 }, report.Rows.Select(row => row.MonsterId).ToArray());
    }

    [Fact]
    public async Task Duplicate_ack_keeps_the_first_and_an_empty_body_fails_the_batch()
    {
        var duplicate = new FakeHandler();
        duplicate.Respond = (seen, _) => seen.Method == "GET"
            ? Json(200, Page(1, (1, 4, "A")))
            : Json(200, "{\"data\":{\"monsters\":[{\"monster_id\":1,\"quantity\":0},{\"monster_id\":1,\"quantity\":9}]}}");
        using var duplicateClient = Client(duplicate);
        var duplicateReport = await MM.MetamobSync.PushAbsoluteAsync(duplicateClient, Slug, new Dictionary<int, int>(), new MM.SoulSyncStop());
        Assert.Equal(MM.SoulSyncOutcome.Updated, duplicateReport.Rows.Single().Outcome);

        var empty = new FakeHandler();
        empty.Respond = (seen, _) => seen.Method == "GET" ? Json(200, Page(1, (1, 4, "A"))) : Json(200, "");
        using var emptyClient = Client(empty);
        var emptyReport = await MM.MetamobSync.PushAbsoluteAsync(emptyClient, Slug, new Dictionary<int, int>(), new MM.SoulSyncStop());
        Assert.True(emptyReport.SentWrite);
        Assert.Equal("Réponse du lot illisible.", emptyReport.Rows.Single().Detail);
    }

    [Fact]
    public async Task Refused_key_never_reaches_the_report()
    {
        var handler = new FakeHandler();
        handler.Respond = (_, _) => Json(401, "{\"message\":\"bad " + Key + "\"}");
        var report = await Execute(handler, Request(Ready()));
        AssertSafe(handler);
        Assert.Single(handler.Calls);
        Assert.False(report.SentWrite);
        Assert.Equal("Clé refusée. Aucune écriture.", report.Status);
        Assert.DoesNotContain(Key, report.Status);
        Assert.Empty(report.Rows);
    }

    [Fact]
    public async Task Redirect_is_an_error_and_is_not_followed()
    {
        var handler = new FakeHandler();
        handler.Respond = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://evil.example/api/v1/quests/" + Slug + "/monsters");
            return response;
        };
        var report = await Execute(handler, Request(Ready()));
        AssertSafe(handler);
        Assert.Single(handler.Calls);
        Assert.False(report.SentWrite);
        Assert.Equal("HTTP 302 Aucune écriture.", report.Status);
    }

    [Fact]
    public async Task Cancellation_during_a_patch_marks_the_batch_interrupted()
    {
        using var cts = new CancellationTokenSource();
        var handler = new FakeHandler();
        handler.Respond = (seen, _) =>
        {
            if (seen.Method == "GET") return Json(200, Page(1, (1, 4, "A")));
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        };
        using var client = Client(handler);
        var report = await MM.MetamobSync.PushAbsoluteAsync(client, Slug, new Dictionary<int, int>(), new MM.SoulSyncStop(), cancellationToken: cts.Token);
        Assert.Equal(new[] { "GET", "PATCH" }, handler.Calls.Select(call => call.Method).ToArray());
        Assert.True(report.SentWrite);
        Assert.Equal("Interrompu.", report.Rows.Single().Detail);
        Assert.Contains("Envoi arrêté après un lot en échec.", report.Status);
    }

    [Fact]
    public async Task Client_rejects_invalid_calls_before_http()
    {
        var handler = new FakeHandler();
        using var client = Client(handler);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetQuestMonstersAsync("abc", 0, 0, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetQuestMonstersAsync("abc", 201, 0, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetQuestMonstersAsync("abc", 1, -1, CancellationToken.None));
        await Assert.ThrowsAsync<MM.MetamobContractException>(() => client.GetQuestMonstersAsync("a/b", 1, 0, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.UpdateQuantitiesAsync("abc", Array.Empty<MM.QuantityChange>(), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.UpdateQuantitiesAsync("abc", new[] { new MM.QuantityChange(1, "A", 0, 31) }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.UpdateQuantitiesAsync("abc", new[] { new MM.QuantityChange(0, "A", 0, 1) }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.UpdateQuantitiesAsync("abc", Enumerable.Range(1, 201).Select(id => new MM.QuantityChange(id, "n", 0, 1)).ToArray(), CancellationToken.None));
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task Account_servers_and_quests_use_the_documented_fields()
    {
        var handler = new FakeHandler();
        handler.Respond = (seen, _) => seen.Path.EndsWith("/me", StringComparison.Ordinal)
            ? Json(200, "{\"data\":{\"username\":\"player1\"}}")
            : seen.Path.EndsWith("/servers", StringComparison.Ordinal) ? Json(200, ServersJson) : Json(200, QuestsJson);
        using var client = Client(handler);
        await client.ConfirmKeyAsync(CancellationToken.None);
        var servers = await client.ListServersAsync(CancellationToken.None);
        var quests = await client.ListQuestsAsync(CancellationToken.None);
        AssertSafe(handler);
        Assert.Equal(new[] { "GET", "GET", "GET" }, handler.Calls.Select(call => call.Method).ToArray());
        Assert.EndsWith("/me", handler.Calls[0].Path);
        Assert.Equal(new[] { 7, 8 }, servers.Select(server => server.Id).ToArray());
        Assert.Equal("Imagiro · 3.0", servers[0].Label);
        Assert.Equal("Orukam", servers[1].Label);
        Assert.Equal(new[] { Slug, "bbbbbbbb", "cccccccc" }, quests.Select(quest => quest.Slug).ToArray());
        Assert.Equal("Héros · Âmes · Imagiro", quests[0].Label);
        Assert.Equal("Second · Imagiro", quests[1].Label);
        Assert.Equal("cccccccc · Orukam", quests[2].Label);
        Assert.Equal(7, quests[0].ServerId);
        var rejected = new FakeHandler();
        rejected.Respond = (_, _) => Json(200, "{\"username\":\"player1\"}");
        using var rejectedClient = Client(rejected);
        var error = await Assert.ThrowsAsync<MM.MetamobContractException>(() => rejectedClient.ConfirmKeyAsync(CancellationToken.None));
        Assert.Equal("Réponse du compte illisible.", error.Message);
        Assert.DoesNotContain("player1", error.Message);
    }

    [Fact]
    public async Task Unreadable_monster_json_is_not_a_write()
    {
        var handler = new FakeHandler();
        handler.Respond = (_, _) => Json(200, "{\"data\":{\"nope\":true}}");
        var report = await Execute(handler, Request(Ready()));
        AssertSafe(handler);
        Assert.False(report.SentWrite);
        Assert.Contains("illisible", report.Status);
        Assert.Contains("Aucune écriture", report.Status);
    }

    [Fact]
    public void Screen_starts_on_both_stores_and_follows_the_selected_character()
    {
        var vm = Screen(new FixedHoldings(), new FakeHandler(), new List<TimeSpan>(), () => null);
        Assert.Equal(new[] { "Sac", "Coffre", "Sac et coffre" }, vm.Scopes.Select(scope => scope.Label).ToArray());
        Assert.Equal(MM.SoulScope.Both, vm.SelectedScope!.Scope);
        Assert.False(vm.HasCapture);
        Assert.False(Can(vm, "SynchronizeCommand"));
        Assert.True(Can(vm, "RefreshListsCommand"));
        Assert.False(Can(vm, "StopCommand"));
        Assert.Equal("Sélectionnez un personnage.", vm.Status);
        vm.AttachPid(4);
        Assert.Equal("Choisissez le serveur, la quête et le périmètre, puis Synchroniser.", vm.Status);
        vm.Status = "manuel";
        vm.AttachPid(4);
        Assert.Equal("manuel", vm.Status);
        vm.AttachPid(0);
        Assert.Equal("Sélectionnez un personnage.", vm.Status);
        Assert.NotNull(vm.GetType().GetProperty("SynchronizeCommand"));
        Assert.NotNull(vm.GetType().GetProperty("StopCommand"));
        Assert.NotNull(vm.GetType().GetProperty("RefreshListsCommand"));
    }

    [Fact]
    public async Task Refresh_loads_lists_without_writing_or_showing_the_account()
    {
        var handler = new FakeHandler();
        handler.Respond = (seen, _) => seen.Path.EndsWith("/me", StringComparison.Ordinal)
            ? Json(200, "{\"data\":{\"username\":\"player1\",\"email\":\"hidden@example.test\"}}")
            : seen.Path.EndsWith("/servers", StringComparison.Ordinal) ? Json(200, ServersJson) : Json(200, QuestsJson);
        var vm = Screen(new FixedHoldings(), handler, new List<TimeSpan>());
        vm.Rows.Add(new MM.MetamobSyncRow(MM.SoulSyncOutcome.Skipped, 1, "A", 0, 0, "déjà 0"));
        await vm.RefreshListsAsync();
        AssertSafe(handler);
        Assert.DoesNotContain(handler.Calls, call => call.Method == "PATCH");
        Assert.Equal("Listes à jour. Clé acceptée.", vm.Status);
        Assert.DoesNotContain("player1", vm.Status);
        Assert.DoesNotContain("hidden@example.test", vm.Status);
        Assert.DoesNotContain(Key, vm.Status);
        Assert.Single(vm.Rows);
        Assert.Equal(7, vm.SelectedServer!.Id);
        Assert.Equal(Slug, vm.SelectedQuest!.Slug);
        vm.SelectedQuest = vm.Quests.Single(quest => quest.Slug == "bbbbbbbb");
        await vm.RefreshListsAsync();
        Assert.Equal("bbbbbbbb", vm.SelectedQuest!.Slug);
        var slugs = new List<string>();
        foreach (var server in vm.Servers.ToList())
        {
            vm.SelectedServer = server;
            slugs.AddRange(vm.Quests.Select(quest => quest.Slug));
        }
        Assert.Equal(new[] { Slug, "bbbbbbbb", "cccccccc" }, slugs);
        Assert.DoesNotContain(slugs, slug => slug.Contains('/', StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refresh_reports_an_empty_server_list_and_a_refused_key()
    {
        var empty = new FakeHandler();
        empty.Respond = (seen, _) => seen.Path.EndsWith("/me", StringComparison.Ordinal)
            ? Json(200, "{\"data\":{\"username\":\"player1\"}}")
            : Json(200, "{\"data\":[]}");
        var emptyScreen = Screen(new FixedHoldings(), empty, new List<TimeSpan>());
        await emptyScreen.RefreshListsAsync();
        Assert.Equal("Aucun serveur renvoyé.", emptyScreen.Status);
        Assert.Null(emptyScreen.SelectedServer);
        Assert.DoesNotContain(empty.Calls, call => call.Method == "PATCH");

        var refused = new FakeHandler();
        refused.Respond = (_, _) => Json(401, "{\"message\":\"" + Key + "\"}");
        var refusedScreen = Screen(new FixedHoldings(), refused, new List<TimeSpan>());
        await refusedScreen.RefreshListsAsync();
        Assert.Equal("Clé refusée.", refusedScreen.Status);
        Assert.DoesNotContain(Key, refusedScreen.Status);
        Assert.EndsWith("/me", Assert.Single(refused.Calls).Path);
    }

    [Fact]
    public async Task Button_refuses_unknown_partial_and_mismatched_input_without_http()
    {
        var holdings = new FixedHoldings { Snapshot = Ready(Stone()) };
        var stones = new FakeHandler();
        var stoneScreen = Screen(holdings, stones, new List<TimeSpan>());
        Prepare(stoneScreen);
        stoneScreen.Rows.Add(new MM.MetamobSyncRow(MM.SoulSyncOutcome.Updated, 1, "vieux", 1, 0, "1 → 0"));
        await stoneScreen.SynchronizeAsync();
        Assert.Empty(stones.Calls);
        Assert.Equal(MM.SoulStoneContents.UnknownReason, stoneScreen.Status);
        Assert.Equal(MM.SoulStoneContents.CaptureNeeded, stoneScreen.CaptureNeeded);
        Assert.True(stoneScreen.HasCapture);
        Assert.Empty(stoneScreen.Rows);

        holdings.Snapshot = Ready(Array.Empty<MM.SoulStoneObservation>(), new[] { Stone() });
        var outside = new FakeHandler();
        var outsideScreen = Screen(holdings, outside, new List<TimeSpan>());
        Prepare(outsideScreen);
        outsideScreen.SelectedScope = outsideScreen.Scopes[0];
        await outsideScreen.SynchronizeAsync();
        Assert.Empty(outside.Calls);
        Assert.Equal(MM.SoulStock.OutsideScope, outsideScreen.Status);

        var wrong = new FakeHandler();
        var wrongScreen = Screen(new FixedHoldings(), wrong, new List<TimeSpan>());
        Prepare(wrongScreen);
        wrongScreen.SelectedQuest = new MM.MetamobQuest(Slug, "Héros", 8, "Orukam", "Âmes");
        await wrongScreen.SynchronizeAsync();
        Assert.Empty(wrong.Calls);
        Assert.Equal(MM.SoulStock.WrongServer, wrongScreen.Status);

        var missing = Screen(new FixedHoldings(), new FakeHandler(), new List<TimeSpan>(), () => "  ");
        Prepare(missing);
        missing.Rows.Add(new MM.MetamobSyncRow(MM.SoulSyncOutcome.Skipped, 1, "vieux", 0, 0, "déjà 0"));
        await missing.SynchronizeAsync();
        Assert.Equal("Clé API absente. Aucune écriture.", missing.Status);
        Assert.Empty(missing.Rows);
        Assert.Equal("", missing.CaptureNeeded);
    }

    [Fact]
    public async Task Button_sends_changed_zeros_and_stop_cuts_the_next_batch()
    {
        var delays = new List<TimeSpan>();
        var handler = new FakeHandler();
        VM.SoulSyncViewModel? vm = null;
        handler.Respond = (seen, _) =>
        {
            if (seen.Method == "GET") return Json(200, Page(3, (1, 1, "A"), (2, 1, "B"), (3, 1, "C")));
            Assert.False(Can(vm!, "RefreshListsCommand"));
            Assert.False(Can(vm!, "SynchronizeCommand"));
            Assert.True(Can(vm!, "StopCommand"));
            Press(vm!, "StopCommand");
            return Json(200, Echo(seen.Body));
        };
        vm = Screen(new FixedHoldings(), handler, delays, pageSize: 200, batchSize: 1);
        Prepare(vm);
        await vm.SynchronizeAsync();
        AssertSafe(handler);
        Assert.Empty(delays);
        Assert.Single(handler.Calls, call => call.Method == "PATCH");
        Assert.Equal("1 → 0", vm.Rows.Single(row => row.Outcome == MM.SoulSyncOutcome.Updated).Detail);
        Assert.Equal(2, vm.Rows.Count(row => row.Detail == "Arrêté avant le lot suivant."));
        Assert.False(Can(vm, "StopCommand"));
        Assert.True(Can(vm, "SynchronizeCommand"));
        Assert.DoesNotContain(handler.Calls, call => call.Path.Contains("/me", StringComparison.Ordinal));
        Assert.DoesNotContain(Key, vm.Status);
    }

    [Fact]
    public async Task Button_scrubs_a_reflected_key_from_failed_rows()
    {
        var handler = new FakeHandler();
        handler.Respond = (seen, _) => seen.Method == "GET"
            ? Json(401, "{\"message\":\"bad " + Key + "\"}")
            : Json(400, "{\"message\":\"nope " + Key + "\"}");
        var refused = Screen(new FixedHoldings(), handler, new List<TimeSpan>());
        Prepare(refused);
        await refused.SynchronizeAsync();
        Assert.Equal("Clé refusée. Aucune écriture.", refused.Status);
        Assert.DoesNotContain(Key, refused.Status);
        Assert.Empty(refused.Rows);

        var failed = new FakeHandler();
        failed.Respond = (seen, _) => seen.Method == "GET"
            ? Json(200, Page(1, (4, 4, "A")))
            : Json(400, "{\"message\":\"nope " + Key + "\"}");
        var screen = Screen(new FixedHoldings(), failed, new List<TimeSpan>());
        Prepare(screen);
        await screen.SynchronizeAsync();
        AssertSafe(failed);
        Assert.Equal("nope [clé]", screen.Rows.Single().Detail);
        Assert.DoesNotContain(Key, screen.Rows.Single().Detail);
        Assert.DoesNotContain(Key, screen.Status);
        Assert.Contains("Échec", screen.Rows.Single().Line);
    }

    private static async Task AssertUnreadable(Func<Seen, string> body, string reason, int pageSize = 2)
    {
        var handler = new FakeHandler();
        handler.Respond = (seen, _) =>
        {
            Assert.Equal("GET", seen.Method);
            return Json(200, body(seen));
        };
        using var client = Client(handler);
        var report = await MM.MetamobSync.PushAbsoluteAsync(client, Slug, new Dictionary<int, int>(), new MM.SoulSyncStop(), pageSize);
        AssertSafe(handler);
        Assert.DoesNotContain(handler.Calls, call => call.Method == "PATCH");
        Assert.False(report.SentWrite);
        Assert.Equal(reason + " Aucune écriture.", report.Status);
    }

    private static async Task<MM.MetamobSyncReport> Execute(FakeHandler handler, MM.MetamobSyncRequest request, MM.SoulSyncStop? stop = null)
    {
        using var client = Client(handler);
        return await MM.MetamobSync.ExecuteAsync(client, request, stop ?? new MM.SoulSyncStop());
    }

    private static void AssertNoHttp(FakeHandler handler, MM.MetamobSyncReport report)
    {
        Assert.Empty(handler.Calls);
        Assert.False(report.SentWrite);
        Assert.Empty(report.Rows);
        Assert.Contains("Aucune écriture", report.Status);
    }

    private static MM.MetamobSyncRequest Request(MM.SoulHoldingsSnapshot holdings, MM.SoulScope scope = MM.SoulScope.Both, string slug = Slug, int serverId = 7, int questServerId = 7, int pageSize = 200, int batchSize = 200)
        => new(slug, serverId, questServerId, scope, holdings, pageSize, batchSize);

    private static MM.SoulHoldingsSnapshot Ready(params MM.SoulStoneObservation[] bag)
        => new(true, true, bag, Array.Empty<MM.SoulStoneObservation>());

    private static MM.SoulHoldingsSnapshot Ready(MM.SoulStoneObservation[] bag, MM.SoulStoneObservation[] chest)
        => new(true, true, bag, chest);

    private static MM.SoulStoneObservation Stone(int uid = 9, long gid = 9686, int qty = 1)
        => new(uid, gid, qty, new[] { Rich() });

    private static InventoryReader.ItemEffect Rich() => new(705, 77, 1, 2, 3, "âme", 12345);

    private static VM.SoulSyncViewModel Screen(MM.ISoulHoldingsSource holdings, FakeHandler handler, List<TimeSpan> delays, Func<string?>? readKey = null, int pageSize = 200, int batchSize = 200)
        => new(holdings, readKey ?? (() => Key), key => new MM.MetamobClient(key, handler, Delay(delays)), pageSize, batchSize);

    private static void Prepare(VM.SoulSyncViewModel vm)
    {
        vm.AttachPid(4);
        vm.SelectedServer = new MM.MetamobServer(7, "Imagiro", "3.0");
        vm.SelectedQuest = new MM.MetamobQuest(Slug, "Héros", 7, "Imagiro", "Âmes");
    }

    private static bool Can(object vm, string command)
    {
        var value = vm.GetType().GetProperty(command)?.GetValue(vm) ?? throw new MissingMemberException(vm.GetType().FullName, command);
        var method = value.GetType().GetMethod("CanExecute", new[] { typeof(object) }) ?? throw new MissingMethodException(value.GetType().FullName, "CanExecute");
        return (bool)method.Invoke(value, new object?[] { null })!;
    }

    private static void Press(object vm, string command)
    {
        var value = vm.GetType().GetProperty(command)!.GetValue(vm)!;
        value.GetType().GetMethod("Execute", new[] { typeof(object) })!.Invoke(value, new object?[] { null });
    }

    private static MM.MetamobClient Client(FakeHandler handler, List<TimeSpan>? delays = null)
        => new(Key, handler, Delay(delays));

    private static Func<TimeSpan, CancellationToken, Task> Delay(List<TimeSpan>? delays) => (span, _) =>
    {
        if (delays is not null) lock (delays) delays.Add(span);
        return Task.CompletedTask;
    };

    private static void AssertSafe(FakeHandler handler)
    {
        Assert.All(handler.Calls, call =>
        {
            Assert.Equal("Bearer", call.Scheme);
            Assert.Equal(Key, call.Parameter);
            Assert.Equal("application/json", call.Accept);
            Assert.Contains("GivreReplica-Metamob/1", call.Agent);
            Assert.DoesNotContain(Key, call.Path);
            Assert.DoesNotContain(Key, call.Body);
            Assert.DoesNotContain(Key, call.Agent);
            Assert.Contains(call.Method, new[] { "GET", "PATCH" });
            Assert.DoesNotContain("/read", call.Path);
            Assert.DoesNotContain("/trade", call.Path);
            Assert.DoesNotContain("/conversations", call.Path);
            if (call.Method != "PATCH")
            {
                Assert.Null(call.ContentType);
                return;
            }
            Assert.Equal("application/json", call.ContentType);
            Assert.EndsWith("/monsters", call.Path);
            Assert.DoesNotContain("?", call.Path);
            using var doc = JsonDocument.Parse(call.Body);
            Assert.Equal(new[] { "monsters" }, doc.RootElement.EnumerateObject().Select(prop => prop.Name).ToArray());
            foreach (var item in doc.RootElement.GetProperty("monsters").EnumerateArray())
            {
                Assert.Equal(new[] { "monster_id", "quantity" }, item.EnumerateObject().Select(prop => prop.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
                Assert.True(item.GetProperty("monster_id").GetInt32() > 0);
                Assert.InRange(item.GetProperty("quantity").GetInt32(), 0, 30);
            }
            Assert.DoesNotContain("current_step", call.Body);
            Assert.DoesNotContain("trade_offer", call.Body);
            Assert.DoesNotContain("never_", call.Body);
            Assert.DoesNotContain("show_trades", call.Body);
            Assert.DoesNotContain("character_name", call.Body);
            Assert.DoesNotContain("parallel_quests", call.Body);
        });
    }

    private static Dictionary<int, int> PatchQuantities(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var map = new Dictionary<int, int>();
        foreach (var item in doc.RootElement.GetProperty("monsters").EnumerateArray())
            map[item.GetProperty("monster_id").GetInt32()] = item.GetProperty("quantity").GetInt32();
        return map;
    }

    private static string Page(int? total, params (int Id, int Qty, string Name)[] monsters)
    {
        var items = new StringBuilder();
        for (var i = 0; i < monsters.Length; i++)
        {
            if (i > 0) items.Append(',');
            var monster = monsters[i];
            items.Append("{\"id\":").Append(monster.Id.ToString(CultureInfo.InvariantCulture))
                .Append(",\"quantity\":").Append(monster.Qty.ToString(CultureInfo.InvariantCulture))
                .Append(",\"name\":{\"fr\":").Append(JsonSerializer.Serialize(monster.Name))
                .Append(",\"en\":\"en\",\"es\":\"es\"}}");
        }
        var page = total is int count ? ",\"pagination\":{\"total\":" + count.ToString(CultureInfo.InvariantCulture) + "}" : "";
        return "{\"data\":{\"monsters\":[" + items + "]" + page + "}}";
    }

    private static string Ack(params (int Id, int Qty)[] rows)
    {
        var items = string.Join(",", rows.Select(row =>
            "{\"monster_id\":" + row.Id.ToString(CultureInfo.InvariantCulture) + ",\"quantity\":" + row.Qty.ToString(CultureInfo.InvariantCulture) + "}"));
        return "{\"data\":{\"monsters\":[" + items + "]}}";
    }

    private static string Echo(string body)
    {
        var rows = PatchQuantities(body).OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value)).ToArray();
        return Ack(rows);
    }

    private static HttpResponseMessage Json(int status, string body, TimeSpan? retryAfter = null)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (retryAfter is TimeSpan wait) response.Headers.RetryAfter = new RetryConditionHeaderValue(wait);
        return response;
    }

    private sealed class FixedHoldings : MM.ISoulHoldingsSource
    {
        public MM.SoulHoldingsSnapshot Snapshot { get; set; } = Ready();
        public MM.SoulHoldingsSnapshot Read(uint pid) => Snapshot;
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        public List<Seen> Calls { get; } = new();
        public Func<Seen, CancellationToken, HttpResponseMessage> Respond { get; set; } = static (_, _) => Json(500, "{\"message\":\"unset\"}");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var seen = new Seen(
                request.Method.Method,
                request.RequestUri?.PathAndQuery ?? "",
                body,
                request.Headers.Authorization?.Scheme ?? "",
                request.Headers.Authorization?.Parameter ?? "",
                request.Headers.UserAgent.ToString(),
                request.Headers.Accept.ToString(),
                request.Content?.Headers.ContentType?.MediaType);
            lock (_gate) Calls.Add(seen);
            return Respond(seen, cancellationToken);
        }
    }

    private sealed record Seen(string Method, string Path, string Body, string Scheme, string Parameter, string Agent, string Accept, string? ContentType);
}
