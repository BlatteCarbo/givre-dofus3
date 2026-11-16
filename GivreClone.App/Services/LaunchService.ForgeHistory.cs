using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

public sealed record ForgeHistoryEntry(long Cursor, DateTimeOffset Time, Guid SessionId, uint Pid,
    string AccountId, string Kind, int? ItemUid, long? ItemGid, string? Rune, int? Outcome,
    IReadOnlyDictionary<string, int>? Stats, int? Pool, long? CashExpense, string? Reason,
    IReadOnlyDictionary<int, JobProgressSnapshot> Progress, long? RuneGid = null,
    int? Quantity = null, long? UnitPrice = null, Guid? OperationId = null, long? StockUnitValue = null);

public sealed record ForgeHistorySession(Guid Id, uint Pid, string AccountId, int? JobId, string Source,
    DateTimeOffset Started, DateTimeOffset? Ended, string? Stop, long ActualCashExpense, int RuneCount,
    IReadOnlyDictionary<int, JobProgressSnapshot> InitialProgress,
    IReadOnlyDictionary<int, JobProgressSnapshot> LatestProgress, Guid? ObjectiveId = null);

public sealed record ForgeHistoryPage(IReadOnlyList<ForgeHistoryEntry> Entries,
    IReadOnlyList<ForgeHistorySession> Sessions, long Cursor, bool Truncated);

/// <summary>Measured history with bounded memory and an optional durable archive. No estimated XP or prices.</summary>
public sealed class ForgeHistoryStore
{
    private readonly object _gate = new();
    private readonly Queue<ForgeHistoryEntry> _entries = new();
    private readonly List<ForgeHistorySession> _sessions = new();
    private readonly int _entryLimit;
    private readonly int _sessionLimit;
    private readonly ForgeHistoryPersistence? _persistence;
    private readonly Task _loadTask = Task.CompletedTask;
    private long _cursor;
    private long _discardedThrough;
    public event Action? Changed;
    public bool HasArchive => _persistence is not null;
    public Task Ready => _loadTask;
    public bool IsReady => _loadTask.IsCompletedSuccessfully;

    public ForgeHistoryStore(int entryLimit = 2000, int sessionLimit = 128)
    {
        if (entryLimit < 1 || sessionLimit < 1) throw new ArgumentOutOfRangeException();
        _entryLimit = entryLimit;
        _sessionLimit = sessionLimit;
    }

    public ForgeHistoryStore(string persistenceDirectory, int entryLimit = 2000, int sessionLimit = 128,
        bool loadInBackground = false)
        : this(entryLimit, sessionLimit)
    {
        _persistence = new ForgeHistoryPersistence(persistenceDirectory);
        if (loadInBackground) _loadTask = Task.Run(LoadArchive);
        else LoadArchive();
    }

    private void LoadArchive()
    {
        try { LoadArchiveCore(); }
        finally { Changed?.Invoke(); }   // archive prete -> les vues qui ont lu une projection partielle se rafraichissent
    }

    private void LoadArchiveCore()
    {
        foreach (var fact in _persistence!.Read())
        {
            _cursor = fact.Entry.Cursor;
            RememberEntry(fact.Entry);
            RememberSession(fact.Session);
        }
    }

    private static IReadOnlyDictionary<TKey, TValue> Copy<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> values) where TKey : notnull =>
        new ReadOnlyDictionary<TKey, TValue>(values.ToDictionary(x => x.Key, x => x.Value));

    public Guid Begin(uint pid, string accountId, int? jobId, string source,
        IReadOnlyDictionary<int, JobProgressSnapshot> progress, Guid? objectiveId = null)
    {
        _loadTask.GetAwaiter().GetResult();
        Guid id;
        lock (_gate)
        {
            var measured = Copy(progress);
            var session = new ForgeHistorySession(Guid.NewGuid(), pid, accountId, jobId, source,
                DateTimeOffset.UtcNow, null, null, 0, 0, measured, measured, objectiveId);
            Append(session, "begin", progress: measured);
            RememberSession(session);
            id = session.Id;
        }
        Changed?.Invoke();
        return id;
    }

    public void Record(Guid id, string kind, IReadOnlyDictionary<int, JobProgressSnapshot> progress,
        int? itemUid = null, long? itemGid = null, string? rune = null, int? outcome = null,
        IReadOnlyDictionary<string, int>? stats = null, int? pool = null, long? cashExpense = null, string? reason = null,
        long? runeGid = null, int? quantity = null, long? unitPrice = null, Guid? operationId = null, long? stockUnitValue = null)
    {
        _loadTask.GetAwaiter().GetResult();
        if (cashExpense < 0) throw new ArgumentOutOfRangeException(nameof(cashExpense));
        lock (_gate)
        {
            int index = _sessions.FindIndex(s => s.Id == id);
            // Indexed lookups: rereading the whole archive per record froze the UI at startup (7359 facts, 2026-10-31).
            var session = index >= 0 ? _sessions[index] : _latestSessions.GetValueOrDefault(id);
            if (session is null || session.Ended is not null && (operationId is null || kind is not ("cash" or "purchase"))) return;
            if (operationId is { } op && _operationKinds.Contains((op, kind))) return;
            var measured = Copy(session.Ended is null ? progress : session.LatestProgress);
            session = session with
            {
                LatestProgress = measured,
                ActualCashExpense = checked(session.ActualCashExpense + (cashExpense ?? 0)),
                RuneCount = session.RuneCount + (kind == "rune" ? 1 : 0),
                Ended = kind == "end" ? DateTimeOffset.UtcNow : session.Ended,
                Stop = kind == "end" ? reason : session.Stop
            };
            Append(session, kind, itemUid, itemGid, rune, outcome, stats is null ? null : Copy(stats),
                pool, cashExpense, reason, measured, runeGid, quantity, unitPrice, operationId, stockUnitValue);
            RememberSession(session);
        }
        Changed?.Invoke();
    }

    private void Append(ForgeHistorySession session, string kind, int? itemUid = null, long? itemGid = null,
        string? rune = null, int? outcome = null, IReadOnlyDictionary<string, int>? stats = null,
        int? pool = null, long? cashExpense = null, string? reason = null,
        IReadOnlyDictionary<int, JobProgressSnapshot>? progress = null, long? runeGid = null,
        int? quantity = null, long? unitPrice = null, Guid? operationId = null, long? stockUnitValue = null)
    {
        var entry = new ForgeHistoryEntry(checked(_cursor + 1), DateTimeOffset.UtcNow, session.Id, session.Pid,
            session.AccountId, kind, itemUid, itemGid, rune, outcome, stats, pool, cashExpense, reason,
            progress ?? session.LatestProgress, runeGid, quantity, unitPrice, operationId, stockUnitValue);
        // Commit before publishing either the cursor or session aggregate. A failed write leaves
        // the in-memory state unchanged; there is no buffered tail to lose on process shutdown.
        _persistence?.Append(new ForgeHistoryFact(entry, session));
        _cursor = entry.Cursor;
        RememberEntry(entry);
    }

    private readonly HashSet<(Guid Operation, string Kind)> _operationKinds = new();
    private readonly Dictionary<Guid, ForgeHistorySession> _latestSessions = new();

    private void RememberEntry(ForgeHistoryEntry entry)
    {
        if (entry.OperationId is { } op) _operationKinds.Add((op, entry.Kind));
        _entries.Enqueue(entry);
        while (_entries.Count > _entryLimit) _discardedThrough = _entries.Dequeue().Cursor;
    }

    private void RememberSession(ForgeHistorySession session)
    {
        _latestSessions[session.Id] = session;
        int index = _sessions.FindIndex(s => s.Id == session.Id);
        if (index >= 0) _sessions[index] = session;
        else
        {
            _sessions.Add(session);
            if (_sessions.Count > _sessionLimit) _sessions.RemoveAt(0);
        }
    }

    public ForgeHistorySession? FindSession(Guid id)
    {
        _loadTask.GetAwaiter().GetResult();
        lock (_gate) return _latestSessions.GetValueOrDefault(id);
    }

    public IReadOnlyList<ForgeHistorySession> ReadSessions()
    {
        _loadTask.GetAwaiter().GetResult();
        lock (_gate) return Array.AsReadOnly(_latestSessions.Values.ToArray());
    }

    public ForgeHistoryPage Read(uint? pid = null, string? accountId = null, long since = 0, int limit = 200)
    {
        if (since < 0 || limit is < 1 or > 2000) throw new ArgumentOutOfRangeException();
        _loadTask.GetAwaiter().GetResult();
        lock (_gate)
        {
            bool Matches(uint p, string a) => accountId is not null ? a == accountId : pid is null || p == pid;
            if (_persistence is not null)
            {
                var archivedEntries = new List<ForgeHistoryEntry>();
                bool archivedMore = false;
                foreach (var fact in _persistence.Read(since))
                {
                    if (!Matches(fact.Entry.Pid, fact.Entry.AccountId)) continue;
                    if (archivedEntries.Count < limit) archivedEntries.Add(fact.Entry);
                    else { archivedMore = true; break; }
                }
                return new ForgeHistoryPage(archivedEntries.AsReadOnly(),
                    Array.AsReadOnly(_latestSessions.Values.Where(s => Matches(s.Pid, s.AccountId)).ToArray()),
                    archivedMore ? archivedEntries[^1].Cursor : Math.Max(since, _cursor), false);
            }
            var entries = _entries.Where(e => e.Cursor > since && Matches(e.Pid, e.AccountId)).Take(limit).ToArray();
            bool more = entries.Length == limit && _entries.Any(e => e.Cursor > entries[^1].Cursor && Matches(e.Pid, e.AccountId));
            long cursor = more ? entries[^1].Cursor : Math.Max(since, _cursor);
            return new ForgeHistoryPage(Array.AsReadOnly(entries),
                Array.AsReadOnly(_sessions.Where(s => Matches(s.Pid, s.AccountId)).ToArray()),
                cursor, since < _discardedThrough);
        }
    }

    /// <summary>Bounded UI projection; archive pagination remains available through Read.
    /// Appelee depuis le thread UI a l'ouverture d'une fiche de bot (SmithMagicViewModel.History) : elle n'ATTEND PAS
    /// la fin du chargement de l'archive, sinon l'ouverture gelait jusqu'a ce que les 7359 faits soient relus. Tant que
    /// l'archive charge, on rend la projection partielle deja en memoire ; <see cref="Changed"/> est leve a la fin du
    /// chargement, ce qui rafraichit la vue. Les ecritures (Begin/Record) continuent d'attendre, elles.</summary>
    public ForgeHistoryPage ReadRecent(uint? pid = null, string? accountId = null)
    {
        lock (_gate)
        {
            bool Matches(uint p, string a) => accountId is not null ? a == accountId : pid is null || p == pid;
            return new ForgeHistoryPage(Array.AsReadOnly(_entries.Where(e => Matches(e.Pid, e.AccountId)).ToArray()),
                Array.AsReadOnly(_sessions.Where(s => Matches(s.Pid, s.AccountId)).ToArray()), _cursor, _discardedThrough > 0);
        }
    }
}

public sealed partial class LaunchService
{
    public ForgeHistoryStore ForgeHistory { get; } = new(
        Path.Combine(MZP19LocalClone.DataRoot, "forge", "history"), loadInBackground: true);

    public Guid BeginForgeHistorySession(uint pid, string accountId, int? jobId = null, string source = "ui", Guid? objectiveId = null) =>
        ForgeHistory.Begin(pid, accountId, jobId, source, JobProgress(pid), objectiveId);

    public void RecordForgeCashExpense(Guid sessionId, long kamas, string reason, Guid? operationId = null)
    {
        var session = ForgeHistory.FindSession(sessionId);
        if (session is not null) ForgeHistory.Record(sessionId, "cash", JobProgress(session.Pid), cashExpense: kamas, reason: reason, operationId: operationId);
    }

    public void EndForgeHistorySession(Guid sessionId, string stop)
    {
        var session = ForgeHistory.FindSession(sessionId);
        if (session is not null) ForgeHistory.Record(sessionId, "end", JobProgress(session.Pid), reason: stop);
    }

    /// <summary>Only a recognized result with the returned object is progress, including a real failed rune.</summary>
    public static bool ConfirmForgeProgress(InventoryReader.MagicResult result, Action noteProgress)
    {
        if (result.Code is not (InventoryReader.CraftFailed or InventoryReader.CraftSuccess or InventoryReader.CraftNeutral)
            || !Eco.ForgeState.HasUsableResult(result)) return false;
        noteProgress();
        return true;
    }
}
