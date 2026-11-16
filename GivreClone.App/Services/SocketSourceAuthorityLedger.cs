using System;
using System.Collections.Generic;

namespace GivreClone.App.Services;

/// <summary>
/// Read-only accounting of actual source-stamped Socket observations.
/// Nothing in this projection is a server-generated request correlation, a
/// cast acknowledgement or permission to mutate any gameplay model.
/// </summary>
public readonly record struct SocketSourceAuthoritySnapshot(
    uint Pid, long RunEpoch, long ServerFramesObserved,
    long LocalProtocolFramesObserved, long EngineWritesCompleted,
    long ServerWireBytes, long EngineWriteBytes)
{
    public bool CurrentSourceWasObserved => true;
    public bool OutgoingEngineWritesAreLocalOnly => true;
    public bool ServerAckAttributed => false;
    public bool GameplayAuthorizationAvailable => false;
    public string BlockReason =>
        "Provenance TCP locale observée, mais aucun lien causal request → ACK/effet serveur n'est établi. " +
        "Les décisions de cast, confirmation d'achat et replay restent interdits sur cette seule base.";
}

/// <summary>
/// Typed, exact-link diagnostic for the local Socket write/read observation
/// cycle. A server frame arriving after a local write observation has NO
/// individually identifiable request ID / response ID / causal proof here.
/// This type must never authorize a cast, purchase, retry or gameplay ACK.
/// </summary>
public readonly record struct SocketRequestObservationDiagnostic(
    CaptureLinkIdentity Link, long LastPostFlushOrdinal,
    long ServerFramesWhileWriteObserved, long LastServerFrameSequence)
{
    public bool IndividualResponseIdentified => false;
    public bool ServerAcknowledged => false;
    public bool CanAuthorizeGameplay => false;
    public string BlockReason =>
        "Observation locale post-Flush et lecture serveur sur le même lien TCP ; " +
        "aucun identifiant individuel commun demande/réponse ni ACK causal " +
        "n'est disponible. L'ordre temporel ou le PID ne suffit pas.";
}

/// <summary>
/// One strictly current source per synthetic PID. The producer owns the source
/// token; the ledger only tracks its exact identity after source validation at
/// LaunchService ingress. A stale close cannot erase the replacement source.
/// This class performs no IO, packet dispatch or tax/accounting mutation.
/// </summary>
internal sealed class SocketSourceAuthorityLedger
{
    private const int MaximumSources = 1024;
    private readonly object _gate = new();
    private readonly Dictionary<uint, State> _byPid = new();
    // SocketGameClient has an actual per-connection token. The independently
    // implemented LocalHandshakeRelay issues a source GUID/run/tunnel ID
    // instead. Either is valid only through its matching producer callback.
    private readonly bool _requiresConnectionToken;

    public SocketSourceAuthorityLedger(bool requiresConnectionToken = true)
        => _requiresConnectionToken = requiresConnectionToken;

    private sealed class State(CaptureLinkIdentity link)
    {
        public readonly CaptureLinkIdentity Link = link;
        public long LastDownSequence;
        public long LastUpSequence;
        public long LastEngineWriteOrdinal;
        public long ServerFrames;
        public long LocalProtocolFrames;
        public long EngineWrites;
        public long ServerBytes;
        public long EngineBytes;
        public long ServerFramesWhileWriteObserved;
    }

    public bool ObserveFramedChunk(CaptureLinkIdentity link, long sequence,
        bool clientToServer, int wireBytes)
    {
        if (!Valid(link) || sequence <= 0 ||
            wireBytes is <= 0 or > 1_048_576) return false;
        lock (_gate)
        {
            if (!TryState(link, out var state)) return false;
            long last = clientToServer ? state.LastUpSequence : state.LastDownSequence;
            if (sequence <= last) return false;
            if (clientToServer)
            {
                state.LastUpSequence = sequence;
                state.LocalProtocolFrames = SaturatingAdd(state.LocalProtocolFrames, 1);
            }
            else
            {
                state.LastDownSequence = sequence;
                state.ServerFrames = SaturatingAdd(state.ServerFrames, 1);
                state.ServerBytes = SaturatingAdd(state.ServerBytes, wireBytes);
                if (state.LastEngineWriteOrdinal > 0)
                    state.ServerFramesWhileWriteObserved =
                        SaturatingAdd(state.ServerFramesWhileWriteObserved, 1);
            }
            return true;
        }
    }

    /// <summary>
    /// Consultative only: the last post-flush ordinal and later-delivered
    /// receive observations share an exact link, not a request-response key.
    /// A stale/changed source cannot borrow the current link's counters.
    /// </summary>
    public SocketRequestObservationDiagnostic? InspectRequestObservation(
        CaptureLinkIdentity exactLink)
    {
        if (!Valid(exactLink)) return null;
        lock (_gate)
        {
            if (!_byPid.TryGetValue(exactLink.Pid, out var state) ||
                state.Link != exactLink) return null;
            return new SocketRequestObservationDiagnostic(exactLink,
                state.LastEngineWriteOrdinal, state.ServerFramesWhileWriteObserved,
                state.LastDownSequence);
        }
    }

    public bool ObserveEngineWrite(CaptureLinkIdentity link, long ordinal, int wireBytes)
    {
        if (!Valid(link) || ordinal <= 0 ||
            wireBytes is <= 0 or > 1_048_576) return false;
        lock (_gate)
        {
            // SocketGameClient assigns this ordinal ONLY after completed
            // WriteAsync+FlushAsync, sequentially per stream. An absent ordinal
            // means the consumer missed a write (Stop/reentrancy/overflow), not
            // permission to fill the gap or count a later write as continuous.
            // Check before TryState: a malformed first write must not replace
            // an already-observed link with the same synthetic PID.
            if (_byPid.TryGetValue(link.Pid, out var existing) &&
                existing.Link == link)
            {
                if (existing.LastEngineWriteOrdinal == long.MaxValue ||
                    ordinal != existing.LastEngineWriteOrdinal + 1) return false;
            }
            else if (ordinal != 1) return false;
            if (!TryState(link, out var state)) return false;
            state.LastEngineWriteOrdinal = ordinal;
            state.EngineWrites = SaturatingAdd(state.EngineWrites, 1);
            state.EngineBytes = SaturatingAdd(state.EngineBytes, wireBytes);
            return true;
        }
    }

    public SocketSourceAuthoritySnapshot? Read(uint pid)
    {
        if (pid == 0) return null;
        lock (_gate)
        {
            if (!_byPid.TryGetValue(pid, out var state)) return null;
            return new SocketSourceAuthoritySnapshot(pid, state.Link.RunEpoch,
                state.ServerFrames, state.LocalProtocolFrames,
                state.EngineWrites, state.ServerBytes, state.EngineBytes);
        }
    }

    public void RevokeExact(CaptureLinkIdentity link)
        => TryRevokeExact(link);

    /// <summary>
    /// Precise transition witness. An old EOF must never revoke or expire a
    /// newly accepted same-PID source; true means THIS exact link was removed.
    /// </summary>
    public bool TryRevokeExact(CaptureLinkIdentity link)
    {
        lock (_gate)
        {
            if (_byPid.TryGetValue(link.Pid, out var current) &&
                current.Link == link)
            {
                _byPid.Remove(link.Pid);
                return true;
            }
            return false;
        }
    }

    public CaptureLinkIdentity? CurrentLink(uint pid)
    {
        lock (_gate)
            return _byPid.TryGetValue(pid, out var state) ? state.Link : null;
    }

    private bool TryState(CaptureLinkIdentity link, out State state)
    {
        if (_byPid.TryGetValue(link.Pid, out state!))
        {
            if (state.Link == link) return true;
            // An old connection can never replace a newer connection under
            // the same source instance. Different instances are authorized
            // only by the caller's exact current-source predicate.
            if (state.Link.SourceInstanceId == link.SourceInstanceId &&
                (link.RunEpoch < state.Link.RunEpoch ||
                 link.RunEpoch == state.Link.RunEpoch &&
                 link.TunnelId <= state.Link.TunnelId))
                return false;
        }
        else if (_byPid.Count >= MaximumSources) return false;
        state = new State(link);
        _byPid[link.Pid] = state;
        return true;
    }

    private static long SaturatingAdd(long value, int add)
        => value > long.MaxValue - add ? long.MaxValue : value + add;

    private bool Valid(CaptureLinkIdentity link)
        => link.SourceInstanceId != Guid.Empty && link.Pid > 0 &&
           link.RunEpoch > 0 && link.TunnelId > 0 &&
           (!_requiresConnectionToken || link.ConnectionToken != Guid.Empty);
}
