using System;
using System.Collections.Generic;

namespace GivreClone.App.Services;

/// <summary>Sliding-window write rate of the transport log. Pure (caller supplies the clock) so the alert is testable.
/// Retention already caps disk use (segments x sessions); this flags a verbose burst that would rotate sessions away.</summary>
public sealed class LogGrowthMonitor
{
    private readonly Queue<(long Ticks, long Bytes)> _samples = new();
    private readonly long _windowTicks;
    private readonly long _alertBytesPerMinute;
    private long _windowBytes;

    public LogGrowthMonitor(TimeSpan window, long alertBytesPerMinute)
    {
        if (window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
        if (alertBytesPerMinute <= 0) throw new ArgumentOutOfRangeException(nameof(alertBytesPerMinute));
        _windowTicks = window.Ticks;
        _alertBytesPerMinute = alertBytesPerMinute;
    }

    public void Record(long utcTicks, long bytes)
    {
        if (bytes <= 0) return;
        _samples.Enqueue((utcTicks, bytes));
        _windowBytes += bytes;
        Expire(utcTicks);
    }

    public long BytesPerMinute(long utcTicks)
    {
        Expire(utcTicks);
        return (long)(_windowBytes * (TimeSpan.TicksPerMinute / (double)_windowTicks));
    }

    public bool Alert(long utcTicks) => BytesPerMinute(utcTicks) >= _alertBytesPerMinute;

    private void Expire(long now)
    {
        while (_samples.Count > 0 && now - _samples.Peek().Ticks >= _windowTicks)
            _windowBytes -= _samples.Dequeue().Bytes;
    }
}
