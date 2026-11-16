namespace GivreClone.Engine;

/// <summary>
/// Une unite d'automatisation (route de recolte, IA de combat, suivi de chemin). Volontairement mince :
/// lit l'etat, decide, appelle les actions - la separation "read = etat / act = paquet" d'un bot tiers.
/// </summary>
public interface IBehavior
{
    string Name { get; }
    Task RunAsync(IBotContext context, CancellationToken ct);
}

/// <summary>Heberge et pilote LE comportement actif d'une session (start/stop, une seule execution a la fois).</summary>
public sealed class BehaviorHost(IBotContext context)
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _running;
    private Exception? _lastError;

    private IBehavior? _current;
    public IBehavior? Current { get { lock (_gate) return _current; } }
    public bool IsRunning { get { lock (_gate) return _running is { IsCompleted: false }; } }
    /// <summary>Completion of the latest start (including its predecessor). Safe to await outside callbacks.</summary>
    public Task Completion { get { lock (_gate) return _running ?? Task.CompletedTask; } }
    /// <summary>Last non-cancellation error from the latest behavior; never an unobserved task exception.</summary>
    public Exception? LastError { get { lock (_gate) return _lastError; } }

    public void Start(IBehavior behavior)
    {
        ArgumentNullException.ThrowIfNull(behavior);
        CancellationTokenSource? previousCts;
        lock (_gate)
        {
            previousCts = _cts;
            var cts = new CancellationTokenSource();
            var predecessor = _running ?? Task.CompletedTask;
            _cts = cts;
            _current = behavior;
            _lastError = null;
            // Do not pass the cancellation token to Task.Run: even a cancelled
            // queued start must run its finally block and dispose its CTS.
            _running = Task.Run(() => RunAfterAsync(predecessor, behavior, cts));
        }
        Cancel(previousCts);
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _cts;
            _cts = null;
            _current = null;
        }
        Cancel(cts);
    }

    /// <summary>Request cancellation and await all work scheduled up to this call.</summary>
    public Task StopAsync()
    {
        Task completion;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            completion = _running ?? Task.CompletedTask;
            cts = _cts;
            _cts = null;
            _current = null;
        }
        Cancel(cts);
        return completion;
    }

    private async Task RunAfterAsync(Task predecessor, IBehavior behavior, CancellationTokenSource cts)
    {
        try
        {
            // This await is the ownership barrier: even a behavior which ignores
            // cancellation cannot overlap its successor on this host.
            await predecessor.ConfigureAwait(false);
            if (!cts.IsCancellationRequested)
                await behavior.RunAsync(context, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception error)
        {
            lock (_gate)
                if (ReferenceEquals(_cts, cts)) _lastError = error;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_cts, cts))
                {
                    _cts = null;
                    _current = null;
                }
            }
            cts.Dispose();
        }
    }

    private static void Cancel(CancellationTokenSource? cts)
    {
        // A completed predecessor may have disposed its own CTS meanwhile.
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        // A behavior can register a throwing cancellation callback. It must
        // not prevent a replacement from reaching its sequencing barrier.
        catch (AggregateException) { }
    }
}
