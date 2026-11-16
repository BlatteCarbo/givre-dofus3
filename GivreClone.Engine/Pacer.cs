namespace GivreClone.Engine;

/// <summary>
/// Cadence humaine des actions. GivreCore.Replay est fire-and-forget sans throttle : TOUTE action passe par
/// ici pour eviter une cadence robotique detectable. Phase 0 : delai borne simple ; un modele plus riche
/// (jitter, rafales, fatigue) viendra plus tard.
/// </summary>
public interface IPacer
{
    /// <summary>A attendre avant d'emettre la prochaine action.</summary>
    Task BeforeActionAsync(CancellationToken ct = default);
}

/// <summary>Delai aleatoire borne entre deux actions.</summary>
public sealed class HumanPacer : IPacer
{
    private readonly int _minMs;
    private readonly int _maxMs;
    private readonly Random _rng = new();
    private readonly object _rngGate = new();

    public HumanPacer(int minMs = 350, int maxMs = 900)
    {
        if (minMs < 0 || maxMs < minMs) throw new ArgumentOutOfRangeException(nameof(maxMs));
        _minMs = minMs;
        _maxMs = maxMs;
    }

    public Task BeforeActionAsync(CancellationToken ct = default)
    {
        int delay;
        lock (_rngGate) delay = _rng.Next(_minMs, _maxMs + 1);
        return Task.Delay(delay, ct);
    }
}

/// <summary>Pacer no-op (tests / actions instantanees).</summary>
public sealed class NoPacer : IPacer
{
    public Task BeforeActionAsync(CancellationToken ct = default) => Task.CompletedTask;
}
