using GivreClone.Engine.Accounts;

namespace GivreClone.Engine;

public enum MitmReloginPhase
{
    Idle,
    Armed,
    LinkClosed,
    SessionReady,
    Confirmed,
    TimedOut,
    Cancelled,
}

/// <summary>What the link and the transition may do while a relogin is in progress.</summary>
public readonly record struct MitmReloginDecision(
    MitmReloginPhase Phase,
    bool SameProcess,
    bool ExpectsLinkClose,
    bool SuppressDisconnect,
    bool SuppressLoadingStall,
    bool SuppressTicketRefusal,
    bool FallbackRelaunch);

/// <summary>
/// Same-server MITM character change relaunches Dofus. An injected empty ReloginRequest is answered
/// (2026-11-01 02:52:52.778 request, 02:52:52.813 ReloginTokenEvent) but the client does not close:
/// it did not send the request. After a forced close the process shows the title screen, and only
/// its own JOUER click reconnects (02:54:21.914). This repo has no resolved UI method for that click,
/// and a background input post is not proven. The 2026-10-31 capture (21:43:17 and 21:47:03) is the
/// client's own ReloginRequest, which does close the socket. A different server is not driven here.
/// </summary>
public static class MitmRelogin
{
    /// <summary>Quiet time with no link, ticket, or session progress, then the relaunch fallback.</summary>
    public const int TimeoutMs = 90_000;

    /// <summary>
    /// No ReloginTokenEvent this long after the inject: the client did not start its relogin.
    /// The 2026-10-31 capture answered in 34 ms. This is not a longer copy of <see cref="TimeoutMs"/>.
    /// </summary>
    public const int TokenSilenceMs = 2_000;

    /// <summary>
    /// Token observed, but the game socket is still the original one. The capture closed it after 2.2 s and 4.5 s.
    /// </summary>
    public const int ClientCloseAfterTokenMs = 6_000;

    public const string RequestName = "ReloginRequest";

    public static bool UseInProcess(ConnectionMode source, ConnectionMode target, int sourceServerId, int targetServerId)
    {
        _ = source;
        _ = target;
        _ = sourceServerId;
        _ = targetServerId;
        return false;
    }
}

/// <summary>
/// Clock-driven relogin. A client-closed link, a refused ticket, and a character-select screen are
/// expected until the target is in game or the budget expires. Expiry is the only relaunch request.
/// </summary>
public sealed class MitmReloginSession
{
    private long _progressAt;

    public MitmReloginPhase Phase { get; private set; } = MitmReloginPhase.Idle;

    public MitmReloginDecision Arm(long now)
    {
        if (Phase != MitmReloginPhase.Idle) return Snapshot();
        Phase = MitmReloginPhase.Armed;
        _progressAt = now;
        return Snapshot();
    }

    public MitmReloginDecision OnLinkClosed(long now)
    {
        if (Phase is not (MitmReloginPhase.Armed or MitmReloginPhase.LinkClosed or MitmReloginPhase.SessionReady))
            return Snapshot();
        if (Phase == MitmReloginPhase.Armed) Phase = MitmReloginPhase.LinkClosed;
        _progressAt = now;
        return Snapshot();
    }

    /// <summary>AuthenticationTicketRefusedEvent during the retry. Not a disconnect.</summary>
    public MitmReloginDecision OnTicketRefused(long now)
    {
        if (Phase is MitmReloginPhase.Armed or MitmReloginPhase.LinkClosed or MitmReloginPhase.SessionReady)
            _progressAt = now;
        return Snapshot();
    }

    public MitmReloginDecision OnSessionReady(long now)
    {
        if (Phase is not (MitmReloginPhase.Armed or MitmReloginPhase.LinkClosed or MitmReloginPhase.SessionReady))
            return Snapshot();
        if (Phase is MitmReloginPhase.Armed or MitmReloginPhase.LinkClosed)
            Phase = MitmReloginPhase.SessionReady;
        _progressAt = now;
        return Snapshot();
    }

    public MitmReloginDecision Confirm(long now)
    {
        _ = now;
        if (Phase is not (MitmReloginPhase.Armed or MitmReloginPhase.LinkClosed or MitmReloginPhase.SessionReady))
            return Snapshot();
        Phase = MitmReloginPhase.Confirmed;
        return Snapshot();
    }

    public MitmReloginDecision NoteInjectFailed(long now)
    {
        _ = now;
        if (Phase is not (MitmReloginPhase.Armed or MitmReloginPhase.LinkClosed or MitmReloginPhase.SessionReady))
            return Snapshot();
        Phase = MitmReloginPhase.TimedOut;
        return Snapshot();
    }

    public MitmReloginDecision Tick(long now)
    {
        if (Phase is MitmReloginPhase.Armed or MitmReloginPhase.LinkClosed or MitmReloginPhase.SessionReady
            && now - _progressAt >= MitmRelogin.TimeoutMs)
            Phase = MitmReloginPhase.TimedOut;
        return Snapshot();
    }

    public MitmReloginDecision Cancel()
    {
        if (Phase is MitmReloginPhase.Idle or MitmReloginPhase.Confirmed or MitmReloginPhase.TimedOut)
            return Snapshot();
        Phase = MitmReloginPhase.Cancelled;
        return Snapshot();
    }

    private MitmReloginDecision Snapshot()
    {
        bool open = Phase is MitmReloginPhase.Armed or MitmReloginPhase.LinkClosed or MitmReloginPhase.SessionReady;
        return new(
            Phase,
            SameProcess: Phase != MitmReloginPhase.TimedOut,
            ExpectsLinkClose: open,
            SuppressDisconnect: open,
            SuppressLoadingStall: open,
            SuppressTicketRefusal: open,
            FallbackRelaunch: Phase == MitmReloginPhase.TimedOut);
    }
}

/// <summary>
/// Closes the original game socket only while the relogin is still expected and the replacement
/// game socket has not been installed. A later timer must not kill the socket auto-pick injects on.
/// </summary>
public static class MitmReloginWatch
{
    public static bool ShouldForceClose(bool expected, bool gameSocketReplaced)
        => expected && !gameSocketReplaced;
}

/// <summary>
/// Combat speedhack and the native host stay off until the character is on a map.
/// 2026-11-01 01:09:39 inject with the speedhack off, map at 01:09:44.
/// 2026-11-01 01:59:06 inject, speedhack unloaded at 01:59:08.290. A client-originated selection
/// sends ContextCreationRequest 2.2s later (02:02:40.814 -> 02:02:43.017). No map.
/// 2026-11-01 02:54:58 inject while the speedhack stayed loaded (teardown deferred at 02:54:59).
/// The client sent SequenceNumberRequest and received CharacterLoadingCompleteEvent, then never
/// sent ContextCreationRequest.
/// </summary>
public static class CharacterLoadGuard
{
    public static bool HoldNativeUntilMap(bool mapLoaded) => !mapLoaded;
}

/// <summary>
/// The load stall is measured from the selection, not from ticket accept.
/// 2026-11-01 02:51:35 client selection, ContextCreationRequest at 02:51:38.709, map at 02:51:45.296.
/// The 45s session clock had already logged a stall at 02:51:41 while that load was still moving.
/// </summary>
public static class CharacterLoadWatch
{
    public const int SelectionToContextMs = 12_000;
    public const int ContextToMapMs = 20_000;

    public static bool DeclareStall(long? selectedAt, long? contextAt, bool mapLoaded, long now)
    {
        if (mapLoaded || selectedAt is not long selected) return false;
        if (contextAt is long context && context >= selected)
            return now - context >= ContextToMapMs;
        return now - selected >= SelectionToContextMs;
    }
}

/// <summary>The client's own CharacterSelectionRequest wins. A second inject mid-load does not.
/// An engaged native host or combat speedhack waits: 2026-11-01 01:59 and 02:54.</summary>
public static class CharacterAutoPick
{
    public static bool ShouldInject(bool clientSelectionInFlight, bool alreadyPlaying, bool nativeBusy)
        => !clientSelectionInFlight && !alreadyPlaying && !nativeBusy;
}
