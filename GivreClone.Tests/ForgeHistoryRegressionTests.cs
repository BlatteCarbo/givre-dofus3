extern alias F06App;

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using Xunit;
using ForgeHistoryStore = F06App::GivreClone.App.Services.ForgeHistoryStore;
using JobProgressSnapshot = F06App::GivreClone.App.Services.JobProgressSnapshot;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;

namespace GivreClone.Tests;

public sealed class ForgeHistoryRegressionTests
{
    [Theory]
    [InlineData(1, 123, true)]
    [InlineData(2, 123, true)]
    [InlineData(3, 123, true)]
    [InlineData(0, 123, false)]
    [InlineData(4, 123, false)]
    [InlineData(99, 123, false)]
    [InlineData(2, 0, false)]
    public void Only_confirmed_object_results_refresh_the_watchdog(int code, long gid, bool valid)
    {
        var launch = (LaunchService)RuntimeHelpers.GetUninitializedObject(typeof(LaunchService));
        var actions = new ConcurrentDictionary<uint, long>();
        typeof(LaunchService).GetField("_lastAction", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(launch, actions);
        var result = new InventoryReader.MagicResult(code, gid, [], null, null);
        Assert.Equal(valid, LaunchService.ConfirmForgeProgress(result, () => launch.NoteProgress(101)));
        Assert.Equal(valid, actions.ContainsKey(101));
        Assert.False(actions.ContainsKey(202));
    }

    [Fact]
    public void Shared_session_preserves_measured_progress_supports_and_actual_cash()
    {
        var store = new ForgeHistoryStore();
        var before = new Dictionary<int, JobProgressSnapshot> { [44] = new(10, 1200) };
        var after = new Dictionary<int, JobProgressSnapshot> { [44] = new(11, 1350) };
        var id = store.Begin(101, "account-a", 44, "ui", before);
        var stats = new Dictionary<string, int> { ["Fo"] = 5 };
        store.Record(id, "support", before, itemUid: 123, itemGid: 456, stats: stats);
        store.Record(id, "rune", after, itemUid: 123, itemGid: 456, rune: "Fo", outcome: 2, stats: stats, pool: 12);
        store.Record(id, "cash", after, cashExpense: 180, reason: "runes");
        store.Record(id, "end", after, reason: "completed");
        stats["Fo"] = 999;
        before[44] = new(200);

        var page = store.Read(accountId: "account-a");
        var session = Assert.Single(page.Sessions);
        Assert.Equal(180, session.ActualCashExpense);
        Assert.Equal(1, session.RuneCount);
        Assert.Equal(1200, session.InitialProgress[44].Experience);
        Assert.Equal(1350, session.LatestProgress[44].Experience);
        Assert.Equal(11, session.LatestProgress[44].Level);
        Assert.NotNull(session.Ended);
        var rune = Assert.Single(page.Entries.Where(e => e.Kind == "rune"));
        Assert.Equal(123, rune.ItemUid);
        Assert.Equal(456, rune.ItemGid);
        Assert.Equal(5, rune.Stats!["Fo"]);
        Assert.Equal(12, rune.Pool);
        Assert.Equal(2, rune.Outcome);
        Assert.Empty(store.Read(accountId: "account-b").Entries);
        Assert.Empty(store.Read(accountId: "account-a", since: page.Cursor).Entries);
        store.Record(id, "cash", after, cashExpense: 200);
        Assert.Equal(180, store.Read().Sessions.Single().ActualCashExpense);
    }

    [Fact]
    public void Unknown_XP_stays_unknown_and_level_changes_are_not_inferred()
    {
        var store = new ForgeHistoryStore();
        var id = store.Begin(101, "a", 44, "lua", new Dictionary<int, JobProgressSnapshot> { [44] = new(10) });
        store.Record(id, "end", new Dictionary<int, JobProgressSnapshot> { [44] = new(11) }, reason: "stop");
        var session = Assert.Single(store.Read().Sessions);
        Assert.Null(session.InitialProgress[44].Experience);
        Assert.Null(session.LatestProgress[44].Experience);
        Assert.Equal(11, session.LatestProgress[44].Level);
        Assert.Equal(0, session.ActualCashExpense);
    }

    [Fact]
    public void Cursor_paginates_without_losing_entries_and_marks_retention_gaps()
    {
        var store = new ForgeHistoryStore(entryLimit: 3, sessionLimit: 2);
        var jobs = new Dictionary<int, JobProgressSnapshot>();
        var id = store.Begin(101, "a", null, "lua", jobs);
        for (int i = 0; i < 4; i++) store.Record(id, "rune", jobs, outcome: 2);
        var first = store.Read(accountId: "a", limit: 2);
        Assert.True(first.Truncated);
        Assert.Equal(new long[] { 3, 4 }, first.Entries.Select(e => e.Cursor));
        Assert.Equal(4, first.Cursor);
        var second = store.Read(accountId: "a", since: first.Cursor, limit: 2);
        Assert.False(second.Truncated);
        Assert.Equal(5, Assert.Single(second.Entries).Cursor);
        store.Begin(202, "b", null, "ui", jobs);
        store.Begin(303, "c", null, "ui", jobs);
        Assert.Equal(2, store.Read().Sessions.Count);
        Assert.Empty(store.Read(accountId: "a").Sessions);
    }

    [Fact]
    public async Task Concurrent_writers_have_unique_ordered_cursors_and_exact_cash()
    {
        var store = new ForgeHistoryStore(entryLimit: 1000);
        var jobs = new Dictionary<int, JobProgressSnapshot>();
        var id = store.Begin(101, "a", null, "lua", jobs);
        await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
            store.Record(id, "cash", jobs, cashExpense: 7))));
        var page = store.Read(limit: 1000);
        Assert.Equal(101, page.Entries.Count);
        Assert.Equal(101, page.Entries.Select(e => e.Cursor).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 101).Select(x => (long)x), page.Entries.Select(e => e.Cursor));
        Assert.Equal(700, Assert.Single(page.Sessions).ActualCashExpense);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Record(id, "cash", jobs, cashExpense: -1));
    }
}
