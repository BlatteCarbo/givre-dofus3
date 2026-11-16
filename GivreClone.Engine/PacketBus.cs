namespace GivreClone.Engine;

/// <summary>Flux de messages decodes d'UNE session, avec une primitive await-packet.</summary>
public interface IPacketBus
{
    /// <summary>S'abonne au flux ; Dispose pour se desabonner.</summary>
    IDisposable Subscribe(Action<BotMessage> handler);

    /// <summary>Attend le prochain message satisfaisant le predicat (ou timeout -&gt; TimeoutException).</summary>
    Task<BotMessage> WaitForAsync(Func<BotMessage, bool> predicate, TimeSpan timeout, CancellationToken ct = default);
}

/// <summary>
/// Implementation thread-safe. Reprend l'idee SuspendScriptUntil / WaitForMessageAsync (CoreMitm):
/// transformer le flux d'evenements asynchrone en etapes attendables pour ecrire des comportements lineaires.
/// </summary>
public sealed class PacketBus : IPacketBus
{
    private readonly object _gate = new();
    private readonly List<Action<BotMessage>> _subscribers = new();
    private readonly List<Waiter> _waiters = new();

    private sealed class Waiter
    {
        public required Func<BotMessage, bool> Predicate;
        public required TaskCompletionSource<BotMessage> Tcs;
        public CancellationTokenRegistration Registration;
        public Timer? Timeout;
        // All three fields below are protected by _gate. Only the winner may complete Tcs.
        public bool Pending = true;
        // Register() may invoke the callback synchronously before it returns the registration.
        // In that case cleanup belongs to WaitForAsync after resources are assigned.
        public bool Initialized;
    }

    public IDisposable Subscribe(Action<BotMessage> handler)
    {
        lock (_gate) _subscribers.Add(handler);
        return new Unsub(this, handler);
    }

    public void Publish(BotMessage message)
    {
        Action<BotMessage>[] subs;
        List<Waiter>? matched = null;
        lock (_gate)
        {
            subs = _subscribers.ToArray();
            // A predicate may synchronously cancel its own token or even publish
            // reentrantly. Iterate a snapshot so reentrant removal never shifts
            // indices or accidentally settles the next waiter.
            var candidates = _waiters.ToArray();
            for (int i = candidates.Length - 1; i >= 0; i--)
            {
                var w = candidates[i];
                if (!w.Pending) continue;
                bool hit;
                try { hit = w.Predicate(message); } catch { hit = false; }
                if (hit && w.Pending && _waiters.Remove(w))
                {
                    w.Pending = false;
                    // Linearize completion with the removal: a concurrent timeout/cancel
                    // cannot win while subscribers are executing. Continuations are async.
                    w.Tcs.TrySetResult(message);
                    (matched ??= new List<Waiter>()).Add(w);
                }
            }
        }
        // Dispose registrations outside _gate: cancellation disposal can wait for a
        // callback which itself needs _gate. Never dispose under the shared lock.
        if (matched is not null)
            foreach (var w in matched) Release(w);

        // Subscribers run without _gate, preserving fanout and exception isolation.
        foreach (var s in subs)
        {
            try { s(message); } catch { /* un abonne ne doit pas casser la diffusion */ }
        }
    }

    public Task<BotMessage> WaitForAsync(Func<BotMessage, bool> predicate, TimeSpan timeout, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (timeout != Timeout.InfiniteTimeSpan &&
            (timeout < TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1d))
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var tcs = new TaskCompletionSource<BotMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiter = new Waiter { Predicate = predicate, Tcs = tcs };
        bool cleanupAfterInitialization = false;
        Exception? initializationError = null;
        lock (_gate)
        {
            if (ct.IsCancellationRequested) return Task.FromCanceled<BotMessage>(ct);
            if (timeout == TimeSpan.Zero)
                return Task.FromException<BotMessage>(new TimeoutException("Aucun message attendu recu avant le delai."));

            _waiters.Add(waiter);
            try
            {
                if (ct.CanBeCanceled)
                    waiter.Registration = ct.UnsafeRegister(static state =>
                    {
                        var callback = (CancellationState)state!;
                        callback.Bus.CancelWaiter(callback.Waiter, callback.Token);
                    }, new CancellationState(this, waiter, ct));

                // A cancellation fired synchronously during UnsafeRegister may already
                // have removed and settled the waiter; do not create a redundant timer.
                if (waiter.Pending && timeout != Timeout.InfiniteTimeSpan)
                    waiter.Timeout = new Timer(static state =>
                    {
                        var callback = (TimeoutState)state!;
                        callback.Bus.TimeoutWaiter(callback.Waiter);
                    }, new TimeoutState(this, waiter), timeout, Timeout.InfiniteTimeSpan);
            }
            catch (Exception ex)
            {
                initializationError = ex;
                if (waiter.Pending)
                {
                    waiter.Pending = false;
                    _waiters.Remove(waiter);
                }
            }
            waiter.Initialized = true;
            cleanupAfterInitialization = !waiter.Pending;
        }

        if (cleanupAfterInitialization) Release(waiter);
        if (initializationError is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(initializationError).Throw();

        return tcs.Task;
    }

    private sealed record CancellationState(PacketBus Bus, Waiter Waiter, CancellationToken Token);
    private sealed record TimeoutState(PacketBus Bus, Waiter Waiter);

    private void CancelWaiter(Waiter waiter, CancellationToken ct)
    {
        bool release;
        lock (_gate)
        {
            if (!waiter.Pending) return;
            waiter.Pending = false;
            _waiters.Remove(waiter);
            waiter.Tcs.TrySetCanceled(ct);
            release = waiter.Initialized;
        }
        if (release) Release(waiter);
    }

    private void TimeoutWaiter(Waiter waiter)
    {
        bool release;
        lock (_gate)
        {
            if (!waiter.Pending) return;
            waiter.Pending = false;
            _waiters.Remove(waiter);
            waiter.Tcs.TrySetException(new TimeoutException("Aucun message attendu recu avant le delai."));
            release = waiter.Initialized;
        }
        if (release) Release(waiter);
    }

    private static void Release(Waiter waiter)
    {
        // Disposing a timer from its own callback is safe. Disposing a cancellation
        // registration from its own callback is supported and avoids retaining waiter
        // and bus references for the lifetime of a long-lived CancellationTokenSource.
        waiter.Timeout?.Dispose();
        waiter.Registration.Dispose();
    }

    private sealed class Unsub(PacketBus bus, Action<BotMessage> handler) : IDisposable
    {
        public void Dispose() { lock (bus._gate) bus._subscribers.Remove(handler); }
    }
}
