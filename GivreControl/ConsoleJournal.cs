using System.Text.RegularExpressions;

namespace GivreControl;

public sealed record ConsoleEntry(long Cursor, DateTimeOffset Time, string Level, string Category, string Message);
public sealed record ConsolePage(IReadOnlyList<ConsoleEntry> Lines, long Cursor, bool Truncated);

public sealed class ConsoleJournal
{
    private readonly object _gate = new();
    private readonly Queue<ConsoleEntry> _lines = new();
    private TaskCompletionSource _changed = NewSignal();
    private long _cursor;
    private int _characters;
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public long Cursor { get { lock (_gate) return _cursor; } }

    public void Add(string level, string category, string message)
    {
        lock (_gate)
        {
            message = message.Length > 16000 ? message[..16000] : message;
            _lines.Enqueue(new ConsoleEntry(++_cursor, DateTimeOffset.UtcNow, level, category, message));
            _characters += message.Length;
            while (_lines.Count > 3000 || _characters > 1_000_000) _characters -= _lines.Dequeue().Message.Length;
            var previous = _changed; _changed = NewSignal(); previous.TrySetResult();
        }
    }

    public ConsolePage Read(long since, int limit, string pattern = "", string level = "")
    {
        if (since < 0) throw new ArgumentOutOfRangeException(nameof(since));
        if (pattern.Length > 500) throw new ArgumentException("Console pattern is too long.");
        var regex = pattern.Length == 0 ? null : new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500));
        lock (_gate)
        {
            var lines = _lines.Where(l => l.Cursor > since && (level.Length == 0 || l.Level.Equals(level, StringComparison.OrdinalIgnoreCase))
                && (regex is null || regex.IsMatch(l.Message))).Take(Math.Clamp(limit, 1, 500)).ToArray();
            long next = lines.Length == 0 ? _cursor : lines[^1].Cursor;
            return new ConsolePage(lines, next, _lines.TryPeek(out var first) && since > 0 && since < first.Cursor - 1);
        }
    }

    public async Task<ConsolePage> WaitAsync(long since, string pattern, int seconds, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 50)));
        long cursor = since == 0 ? Cursor : since;
        while (true)
        {
            Task changed;
            ConsolePage page;
            lock (_gate) { page = Read(cursor, 100, pattern); changed = _changed.Task; }
            if (page.Lines.Count > 0) return page;
            cursor = page.Cursor;
            try { await changed.WaitAsync(timeout.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Read(cursor, 100, pattern); }
        }
    }
}
