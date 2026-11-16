using GivreClone.Engine.Accounts;

namespace GivreClone.Engine;

public sealed record SessionIdentity(int ServerId, long CharacterId, string Character)
{
    public bool IsKnown => ServerId > 0 && CharacterId > 0 && !string.IsNullOrWhiteSpace(Character);
    public bool Matches(SessionIdentity other) => IsKnown && other.IsKnown
        && ServerId == other.ServerId && CharacterId == other.CharacterId
        && string.Equals(Character, other.Character, StringComparison.Ordinal);
}

/// <summary>An operator's confirmation belongs to this source, not the next session on the same PID.</summary>
public sealed record SessionSource(string AccountId, AuthSource Authentication, int Generation, int Pid,
    ConnectionMode Mode, int SocketSequence, SessionIdentity Identity);

public sealed record SessionTarget(ConnectionMode Mode, SessionIdentity Identity);
public sealed record SessionTransitionRequest(SessionSource Source, SessionTarget Target, bool ResumeScript = true);
public enum SessionTransitionState { Idle, Preflight, Closing, Launching, Confirming, Completed, Rejected, Cancelled, Superseded, Disconnected }
public sealed record SessionTransitionResult(SessionTransitionState State, bool Committed, string Message)
{
    public bool Ok => State == SessionTransitionState.Completed;
}

/// <summary>
/// Single-use transition transaction. Before commit, cancellation has no side effects on the source.
/// After commit, source disposal must finish before another authentication can begin. An obsolete
/// transaction never disconnects a newer owner. Delegates run on the caller's synchronization context.
/// </summary>
public sealed class SessionTransition
{
    private int _started;
    public SessionTransitionState State { get; private set; }
    public bool Committed { get; private set; }
    public bool Active => State is SessionTransitionState.Preflight or SessionTransitionState.Closing
        or SessionTransitionState.Launching or SessionTransitionState.Confirming;

    public async Task<SessionTransitionResult> RunAsync(
        Func<CancellationToken, Task<string?>> preflight,
        Func<bool> owns,
        Func<Task> commitAndClose,
        Func<Task<bool>> launch,
        Func<CancellationToken, Task<bool>> confirm,
        Action<string> disconnect,
        Action<SessionTransitionState> changed,
        CancellationToken cancellationToken,
        Func<bool>? tryCommit = null)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return new(SessionTransitionState.Rejected, false, "Transition already started.");
        void Set(SessionTransitionState state) { State = state; changed(state); }
        SessionTransitionResult End(SessionTransitionState state, string message)
        {
            // A failed committed transaction is disconnected, never restored to a fictitious source.
            if (Committed && state != SessionTransitionState.Completed && owns()) disconnect(message);
            Set(state);
            return new(state, Committed, message);
        }
        try
        {
            Set(SessionTransitionState.Preflight);
            cancellationToken.ThrowIfCancellationRequested();
            if (!owns()) return End(SessionTransitionState.Superseded, "Source session changed.");
            string? refusal = await preflight(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!owns()) return End(SessionTransitionState.Superseded, "Source session changed during preflight.");
            if (refusal is not null) return End(SessionTransitionState.Rejected, refusal);
            if (tryCommit is not null && !tryCommit()) return End(SessionTransitionState.Superseded, "Source session ended before commit.");

            Committed = true;
            Set(SessionTransitionState.Closing);
            // Deliberately not WaitAsync(cancellationToken): abandoning this wait would permit a second
            // login while the cancelled Socket loop still owns its upstream connection.
            await commitAndClose();
            cancellationToken.ThrowIfCancellationRequested();
            if (!owns()) return End(SessionTransitionState.Superseded, "Session ownership changed while closing.");
            Set(SessionTransitionState.Launching);
            bool launched = await launch();
            cancellationToken.ThrowIfCancellationRequested();
            if (!owns()) return End(SessionTransitionState.Superseded, "Session ownership changed while launching.");
            if (!launched) return End(SessionTransitionState.Disconnected, "Target launch failed.");
            Set(SessionTransitionState.Confirming);
            bool confirmed = await confirm(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!owns()) return End(SessionTransitionState.Superseded, "Session ownership changed before confirmation.");
            return confirmed
                ? End(SessionTransitionState.Completed, "Target identity confirmed in game.")
                : End(SessionTransitionState.Disconnected, "Target identity did not match.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return End(SessionTransitionState.Cancelled, Committed ? "Transition cancelled; session disconnected." : "Transition cancelled; source preserved.");
        }
        catch (TimeoutException)
        {
            return End(Committed ? SessionTransitionState.Disconnected : SessionTransitionState.Rejected, "Personnage non confirmé en jeu dans le délai.");
        }
        catch (Exception e)
        {
            return End(Committed ? SessionTransitionState.Disconnected : SessionTransitionState.Rejected, e.Message);
        }
    }
}
