using System;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services.Transport.Socket;

/// <summary>
/// One-session, opt-in parking point before character selection. Owns no frames,
/// credentials or streams. This is a mirror prerequisite, not a session handoff.
/// </summary>
internal sealed class SocketCharacterSelectionGate
{
    private readonly object _sync = new();
    private readonly TaskCompletionSource _parked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _entered;
    private bool _ended;

    public Task Parked => _parked.Task;

    public async Task WaitBeforeSelectionAsync(CancellationToken sessionToken)
    {
        lock (_sync)
        {
            sessionToken.ThrowIfCancellationRequested();
            if (_ended) throw new OperationCanceledException("Socket session ended.");
            if (_entered) throw new InvalidOperationException("Selection gate is single-use.");
            _entered = true;
            _parked.TrySetResult();
        }
        await _resume.Task.WaitAsync(sessionToken).ConfigureAwait(false);
        sessionToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_ended) throw new OperationCanceledException("Socket session ended.");
        }
    }

    /// <summary>Release only an actually parked session; an early click cannot bypass parking.</summary>
    public bool TryResume()
    {
        lock (_sync)
        {
            return !_ended && _entered && _resume.TrySetResult();
        }
    }

    /// <summary>Unblocks waiters even if the connection failed before character selection.</summary>
    public void EndSession()
    {
        lock (_sync)
        {
            _ended = true;
            _parked.TrySetCanceled();
            _resume.TrySetCanceled();
        }
    }
}
