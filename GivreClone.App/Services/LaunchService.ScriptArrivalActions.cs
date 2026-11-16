using System;
using System.Threading;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    // Serializes cancellation, arming and dispatch. A late callback from run A cannot remove run B's actions.
    private static readonly object ArrivalActionGate = new();
    private static readonly AsyncLocal<CancellationToken> ScriptActionOwner = new();

    internal IDisposable ScriptArrivalScope(CancellationToken owner)
    {
        var previous = ScriptActionOwner.Value;
        ScriptActionOwner.Value = owner;
        return new ArrivalScope(previous);
    }

    private sealed class ArrivalScope(CancellationToken previous) : IDisposable
    {
        public void Dispose() => ScriptActionOwner.Value = previous;
    }

    internal void CancelScriptArrivalActions(uint pid, CancellationTokenSource run)
    {
        lock (ArrivalActionGate)
        {
            CancellationToken owner;
            try { owner = run.Token; } catch (ObjectDisposedException) { return; }
            try { run.Cancel(); }
            catch (AggregateException) { /* A failing observer cannot prevent arrival cleanup. */ }
            finally { ClearScriptArrivalActions(pid, owner); }
        }
    }

    internal void ClearScriptArrivalActions(uint pid, CancellationToken owner)
    {
        lock (ArrivalActionGate)
        {
            if (_pendingMapChange.TryGetValue(pid, out var map) && map.Owner == owner)
                _pendingMapChange.TryRemove(pid, out _);
            if (_pendingHarvest.TryGetValue(pid, out var harvest) && harvest.Owner == owner)
                _pendingHarvest.TryRemove(pid, out _);
            if (_pendingAttack.TryGetValue(pid, out var attack) && attack.Owner == owner)
                _pendingAttack.TryRemove(pid, out _);
        }
    }
}
