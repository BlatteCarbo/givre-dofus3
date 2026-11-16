using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Mcp;

public enum McpActionState { Running, Returned, Accepted, Refused, Failed, Cancelled, ExecutionEnded, ExecutionCompleted, ExecutionFailed }

public sealed record McpActionEntry(long Id, DateTimeOffset Started, string Tool, string Target, McpActionState State)
{
    public string TimeText => Started.ToLocalTime().ToString("HH:mm:ss");
    public string StatusText => State switch
    {
        McpActionState.Running => "Appel en cours",
        McpActionState.Returned => "Appel terminé",
        McpActionState.Accepted => "Accepté ; fin d'exécution non confirmée",
        McpActionState.Refused => "Refusé",
        McpActionState.Failed => "Erreur d'appel",
        McpActionState.Cancelled => "Appel annulé",
        McpActionState.ExecutionEnded => "Exécution arrêtée ; détail dans le journal du bot",
        McpActionState.ExecutionCompleted => "Script terminé normalement",
        McpActionState.ExecutionFailed => "Erreur d'exécution ; détail dans le journal du bot",
        _ => throw new ArgumentOutOfRangeException(),
    };
}

/// <summary>Deliberately stores no arguments, result bodies, code, messages or exception text.</summary>
public sealed class McpActionJournal
{
    private readonly object _gate = new();
    private readonly List<McpActionEntry> _entries = new();
    private readonly int _limit;
    private long _sequence;

    public McpActionJournal(int limit = 200)
    {
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        _limit = limit;
    }

    public long Begin(string tool, string verifiedTarget)
    {
        string name = tool.Length is > 0 and <= 80 && tool.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? tool : "(outil invalide)";
        lock (_gate)
        {
            long id = ++_sequence;
            _entries.Add(new(id, DateTimeOffset.UtcNow, name, verifiedTarget, McpActionState.Running));
            if (_entries.Count > _limit) _entries.RemoveAt(0);
            return id;
        }
    }

    public void SetState(long id, McpActionState state)
    {
        lock (_gate)
        {
            int index = _entries.FindIndex(e => e.Id == id);
            if (index >= 0) _entries[index] = _entries[index] with { State = state };
        }
    }

    public IReadOnlyList<McpActionEntry> Read()
    {
        lock (_gate) return _entries.AsEnumerable().Reverse().ToArray();
    }
}
