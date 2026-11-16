namespace GivreClone.Engine;

/// <summary>One selection per launched client, retained while its delayed send is pending.</summary>
public sealed class CharacterSelectionQueue
{
    public sealed record Request(uint Pid, string Character, long Generation, SessionIdentity? Target = null);
    private readonly object _gate = new();
    private readonly Dictionary<uint, (Request Request, bool Claimed)> _requests = new();
    private readonly AsyncLocal<SessionIdentity?> _explicitTarget = new();
    private long _generation;

    // Flows through the asynchronous launcher, before its PID exists and before hook injection.
    // Parallel account launches have independent execution contexts.
    public IDisposable Expect(SessionIdentity target)
    {
        var previous = _explicitTarget.Value;
        _explicitTarget.Value = target;
        return new SelectionScope(() => _explicitTarget.Value = previous);
    }

    private sealed class SelectionScope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    public void Bind(uint pid, string? character)
    {
        lock (_gate)
        {
            _requests.Remove(pid);
            if (pid != 0 && !string.IsNullOrWhiteSpace(character))
                _requests[pid] = (new Request(pid, character.Trim(), ++_generation, _explicitTarget.Value), false);
        }
    }

    public bool TryClaim(uint pid, out Request request)
    {
        lock (_gate)
        {
            if (_requests.TryGetValue(pid, out var entry) && !entry.Claimed)
            {
                request = entry.Request;
                _requests[pid] = (request, true);
                return true;
            }
            request = null!;
            return false;
        }
    }

    public bool IsCurrent(Request request)
    {
        lock (_gate)
            return _requests.TryGetValue(request.Pid, out var entry) && entry.Request == request;
    }

    public void Complete(Request request)
    {
        lock (_gate)
            if (_requests.TryGetValue(request.Pid, out var entry) && entry.Request == request)
                _requests.Remove(request.Pid);
    }

    public void Forget(uint pid) { lock (_gate) _requests.Remove(pid); }
}
