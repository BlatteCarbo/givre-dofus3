using System;

namespace GivreClone.Engine.Recovery;

/// <summary>Local send/receipt bookkeeping. Result codes are deliberately not interpreted.</summary>
public sealed class TreasureRequestTracker
{
    public string? Action { get; private set; }
    public long BaselineSequence { get; private set; }
    public DateTimeOffset SentAt { get; private set; }
    public bool Pending => Action is not null;
    public void Clear() { Action = null; BaselineSequence = 0; SentAt = default; }
    public void Begin(string action, long sequence, DateTimeOffset now)
    {
        if (Pending) throw new InvalidOperationException("A treasure request is already awaiting a receipt.");
        Action = action; BaselineSequence = sequence; SentAt = now;
    }
    public bool Receive(long sequence)
    {
        if (!Pending || sequence <= BaselineSequence) return false;
        Clear(); return true;
    }
    public bool Expire(DateTimeOffset now, TimeSpan timeout)
    {
        if (!Pending || now - SentAt < timeout) return false;
        Clear(); return true;
    }
    public static string Direction(int direction) => direction switch
    {
        0 => "→", 2 => "↓", 4 => "←", 6 => "↑", _ => $"? ({direction})"
    };
}
