extern alias F06App;

using Xunit;
using ForgeHistoryStore = F06App::GivreClone.App.Services.ForgeHistoryStore;
using JobProgressSnapshot = F06App::GivreClone.App.Services.JobProgressSnapshot;

namespace GivreClone.Tests;

public sealed class ForgeHistoryPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "forge-history-" + Guid.NewGuid().ToString("N"));
    private static Dictionary<int, JobProgressSnapshot> Jobs(long? xp = null) => new() { [44] = new(10, xp) };

    [Fact]
    public async Task Background_load_preserves_complete_history_before_accepting_more_records()
    {
        var original = new ForgeHistoryStore(_directory);
        var id = original.Begin(101, "a", 44, "ui", Jobs(10));
        original.Record(id, "cash", Jobs(20), cashExpense: 7);

        var reopened = new ForgeHistoryStore(_directory, loadInBackground: true);
        await reopened.Ready.WaitAsync(TimeSpan.FromSeconds(10));
        reopened.Record(id, "cash", Jobs(30), cashExpense: 3);

        Assert.True(reopened.IsReady);
        var page = reopened.Read();
        Assert.Equal(new long[] { 1, 2, 3 }, page.Entries.Select(e => e.Cursor));
        Assert.Equal(10, Assert.Single(page.Sessions).ActualCashExpense);
    }

    [Fact]
    public async Task Background_load_failure_is_reported_without_publishing_an_empty_archive()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "00000000000000000001.json"), "{broken");

        var store = new ForgeHistoryStore(_directory, loadInBackground: true);
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => store.Ready);

        Assert.False(store.IsReady);
        Assert.Throws<System.Text.Json.JsonException>(() => store.Read());
        Assert.Throws<System.Text.Json.JsonException>(() => store.Begin(101, "a", 44, "ui", Jobs()));
        Assert.Single(Directory.GetFiles(_directory, "*.json"));
    }

    [Fact]
    public void Small_page_does_not_open_commits_beyond_its_lookahead()
    {
        var store = new ForgeHistoryStore(_directory);
        var id = store.Begin(101, "a", 44, "ui", Jobs());
        for (int i = 0; i < 4; i++) store.Record(id, "rune", Jobs(), outcome: 2);
        using var lockedTail = new FileStream(
            Path.Combine(_directory, "00000000000000000005.json"),
            FileMode.Open, FileAccess.Read, FileShare.None);

        var page = store.Read(accountId: "a", limit: 1);

        Assert.Equal(1, Assert.Single(page.Entries).Cursor);
        Assert.Equal(4, Assert.Single(page.Sessions).RuneCount);
        Assert.Equal(1, page.Cursor);
    }

    [Fact]
    public void Every_measurement_survives_reopen_without_a_shutdown_flush()
    {
        var store = new ForgeHistoryStore(_directory);
        var initial = Jobs();
        var objectiveId = Guid.NewGuid();
        var id = store.Begin(101, "a", 44, "ui", initial, objectiveId);
        var stats = new Dictionary<string, int> { ["Fo"] = 5 };
        store.Record(id, "rune", Jobs(1200), itemUid: 123, itemGid: 456, rune: "Fo",
            outcome: 2, stats: stats, pool: 12, runeGid: 1519, quantity: 1, stockUnitValue: 42);
        store.Record(id, "cash", Jobs(1250), cashExpense: 180, reason: "receipt");
        store.Record(id, "end", Jobs(1250), reason: "completed");
        var before = store.Read();
        initial[44] = new(200);
        stats["Fo"] = 999;

        var reopened = new ForgeHistoryStore(_directory);
        var page = reopened.Read();
        Assert.Equal(before.Cursor, page.Cursor);
        Assert.Equal(before.Entries.Select(e => (e.Cursor, e.Time, e.SessionId, e.Kind)),
            page.Entries.Select(e => (e.Cursor, e.Time, e.SessionId, e.Kind)));
        var session = Assert.Single(page.Sessions);
        Assert.Equal(id, session.Id);
        Assert.Equal(objectiveId, session.ObjectiveId);
        Assert.Equal(before.Sessions[0].Started, session.Started);
        Assert.Equal(before.Sessions[0].Ended, session.Ended);
        Assert.Equal("completed", session.Stop);
        Assert.Equal(180, session.ActualCashExpense);
        Assert.Equal(1, session.RuneCount);
        Assert.Null(session.InitialProgress[44].Experience);
        Assert.Equal(1250, session.LatestProgress[44].Experience);
        var rune = Assert.Single(page.Entries, e => e.Kind == "rune");
        Assert.Equal(123, rune.ItemUid);
        Assert.Equal(456, rune.ItemGid);
        Assert.Equal("Fo", rune.Rune);
        Assert.Equal(1519, rune.RuneGid);
        Assert.Equal(1, rune.Quantity);
        Assert.Equal(42, rune.StockUnitValue);
        Assert.Equal(2, rune.Outcome);
        Assert.Equal(12, rune.Pool);
        Assert.Equal(5, rune.Stats!["Fo"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, int>)rune.Stats)["Fo"] = 77);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<int, JobProgressSnapshot>)session.LatestProgress)[44] = new(200));
        reopened.Record(id, "cash", Jobs(), cashExpense: 999);
        Assert.Equal(180, Assert.Single(new ForgeHistoryStore(_directory).Read().Sessions).ActualCashExpense);
    }

    [Fact]
    public void Late_confirmed_debit_preserves_ended_attempt_progress_and_is_idempotent_after_restart()
    {
        var store = new ForgeHistoryStore(_directory);
        var id = store.Begin(101, "a", 44, "ui", Jobs(1200));
        store.Record(id, "end", Jobs(1250), reason: "cancelled");
        var operation = Guid.NewGuid();

        store.Record(id, "cash", new Dictionary<int, JobProgressSnapshot>(), cashExpense: 42, operationId: operation);
        store = new ForgeHistoryStore(_directory);
        store.Record(id, "cash", Jobs(), cashExpense: 42, operationId: operation);

        var page = store.Read();
        var session = Assert.Single(page.Sessions);
        Assert.NotNull(session.Ended);
        Assert.Equal("cancelled", session.Stop);
        Assert.Equal(1250, session.LatestProgress[44].Experience);
        Assert.Equal(42, session.ActualCashExpense);
        Assert.Single(page.Entries, e => e.Kind == "cash");
    }

    [Fact]
    public void Reopened_archive_filters_accounts_and_paginates_past_unrelated_facts()
    {
        var store = new ForgeHistoryStore(_directory);
        var a = store.Begin(101, "a", 44, "ui", Jobs());
        var b = store.Begin(101, "b", 44, "lua", Jobs());
        store.Record(a, "rune", Jobs(), outcome: 2);
        store.Record(b, "cash", Jobs(), cashExpense: 7);
        store.Record(a, "end", Jobs(), reason: "stop");

        store = new ForgeHistoryStore(_directory, entryLimit: 1, sessionLimit: 1);
        var first = store.Read(pid: 999, accountId: "a", limit: 1);
        Assert.Equal(1, Assert.Single(first.Entries).Cursor);
        Assert.Equal(1, first.Cursor);
        Assert.Equal(a, Assert.Single(first.Sessions).Id);
        var second = store.Read(accountId: "a", since: first.Cursor, limit: 1);
        Assert.Equal(3, Assert.Single(second.Entries).Cursor);
        Assert.Equal(3, second.Cursor);
        var last = store.Read(accountId: "a", since: second.Cursor, limit: 1);
        Assert.Equal(5, Assert.Single(last.Entries).Cursor);
        Assert.Equal(5, last.Cursor);
        Assert.False(first.Truncated);
        Assert.False(second.Truncated);
        Assert.False(last.Truncated);
        Assert.Empty(store.Read(accountId: "a", since: last.Cursor).Entries);
        Assert.Equal(new long[] { 2, 4 }, store.Read(accountId: "b").Entries.Select(e => e.Cursor));
        Assert.Equal(7, Assert.Single(store.Read(accountId: "b").Sessions).ActualCashExpense);
        Assert.Equal(5, store.Read(pid: 101).Entries.Count);
        Assert.Empty(store.Read(pid: 999).Entries);
    }

    [Fact]
    public void Memory_retention_never_deletes_archive_or_account_session_summaries()
    {
        var store = new ForgeHistoryStore(_directory, entryLimit: 2, sessionLimit: 1);
        var old = store.Begin(101, "a", 44, "lua", Jobs());
        for (int i = 0; i < 5; i++) store.Record(old, "rune", Jobs(), outcome: 1);
        store.Record(old, "end", Jobs(), reason: "done");
        var current = store.Begin(202, "b", 44, "ui", Jobs());
        store.Record(current, "cash", Jobs(), cashExpense: 10);
        Assert.Equal(9, Directory.GetFiles(_directory, "*.json").Length);

        foreach (var reader in new[] { store, new ForgeHistoryStore(_directory, entryLimit: 1, sessionLimit: 1) })
        {
            var cursors = new List<long>();
            long cursor = 0;
            do
            {
                var page = reader.Read(accountId: "a", since: cursor, limit: 2);
                Assert.False(page.Truncated);
                var session = Assert.Single(page.Sessions);
                Assert.Equal(old, session.Id);
                Assert.Equal(5, session.RuneCount);
                Assert.NotNull(session.Ended);
                cursors.AddRange(page.Entries.Select(e => e.Cursor));
                Assert.True(page.Cursor > cursor);
                cursor = page.Cursor;
            } while (cursor < 9);
            Assert.Equal(Enumerable.Range(1, 7).Select(n => (long)n), cursors);
            Assert.Equal(2, reader.Read().Sessions.Count);
        }
    }

    [Fact]
    public void Reopen_continues_archived_open_session_and_preserves_old_commit_bytes()
    {
        var store = new ForgeHistoryStore(_directory, entryLimit: 1, sessionLimit: 1);
        var old = store.Begin(101, "a", 44, "lua", Jobs());
        store.Record(old, "cash", Jobs(), cashExpense: 3);
        store.Begin(202, "b", 44, "ui", Jobs());
        var files = Directory.GetFiles(_directory, "*.json").ToDictionary(p => p, File.ReadAllText);
        var reopened = new ForgeHistoryStore(_directory, entryLimit: 1, sessionLimit: 1);
        reopened.Record(old, "cash", Jobs(99), cashExpense: 4);
        foreach (var file in files) Assert.Equal(file.Value, File.ReadAllText(file.Key));
        var final = new ForgeHistoryStore(_directory).Read(accountId: "a");
        Assert.Equal(4, final.Cursor);
        Assert.Equal(new long[] { 1, 2, 4 }, final.Entries.Select(e => e.Cursor));
        Assert.Equal(7, Assert.Single(final.Sessions).ActualCashExpense);
        Assert.Equal(99, final.Sessions[0].LatestProgress[44].Experience);
    }

    [Fact]
    public void Interrupted_atomic_write_is_ignored_and_next_record_reuses_uncommitted_cursor()
    {
        var store = new ForgeHistoryStore(_directory);
        var id = store.Begin(101, "a", 44, "ui", Jobs());
        File.WriteAllText(Path.Combine(_directory, "00000000000000000002.json.tmp"), "{partial");
        var reopened = new ForgeHistoryStore(_directory);
        Assert.Equal(1, reopened.Read().Cursor);
        reopened.Record(id, "cash", Jobs(), cashExpense: 8);
        var page = new ForgeHistoryStore(_directory).Read();
        Assert.Equal(new long[] { 1, 2 }, page.Entries.Select(e => e.Cursor));
        Assert.Equal(8, Assert.Single(page.Sessions).ActualCashExpense);
    }

    [Fact]
    public void Missing_or_corrupt_committed_history_is_not_silently_reported_as_complete()
    {
        var store = new ForgeHistoryStore(_directory);
        var id = store.Begin(101, "a", 44, "ui", Jobs());
        store.Record(id, "cash", Jobs(), cashExpense: 8);
        string first = Path.Combine(_directory, "00000000000000000001.json");
        string saved = File.ReadAllText(first);
        File.Delete(first);
        Assert.Throws<InvalidDataException>(() => new ForgeHistoryStore(_directory));
        Assert.Throws<InvalidDataException>(() => store.Read());
        File.WriteAllText(first, "{broken");
        Assert.Throws<System.Text.Json.JsonException>(() => new ForgeHistoryStore(_directory));
        Assert.Equal("{broken", File.ReadAllText(first));
        File.WriteAllText(first, saved);
        Assert.Equal(8, Assert.Single(new ForgeHistoryStore(_directory).Read().Sessions).ActualCashExpense);
    }

    [Fact]
    public void Failed_atomic_commit_does_not_publish_a_session_or_consume_a_cursor()
    {
        Directory.CreateDirectory(_directory);
        string blocked = Path.Combine(_directory, "blocked");
        File.WriteAllText(blocked, "not a directory");
        var store = new ForgeHistoryStore(blocked);
        Assert.Throws<IOException>(() => store.Begin(101, "a", 44, "ui", Jobs()));
        Assert.Empty(store.Read().Sessions);
        Assert.Equal(0, store.Read().Cursor);
        File.Delete(blocked);
        var id = store.Begin(101, "a", 44, "ui", Jobs());
        Assert.Equal(1, Assert.Single(new ForgeHistoryStore(blocked).Read().Entries).Cursor);
        string collision = Path.Combine(blocked, "00000000000000000002.json");
        File.WriteAllText(collision, "occupied");
        Assert.Throws<IOException>(() => store.Record(id, "cash", Jobs(), cashExpense: 7));
        File.Delete(collision);
        Assert.Equal(1, store.Read().Cursor);
        Assert.Equal(0, Assert.Single(store.Read().Sessions).ActualCashExpense);
        store.Record(id, "cash", Jobs(), cashExpense: 7);
        var page = new ForgeHistoryStore(blocked).Read();
        Assert.Equal(2, page.Cursor);
        Assert.Equal(7, Assert.Single(page.Sessions).ActualCashExpense);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
