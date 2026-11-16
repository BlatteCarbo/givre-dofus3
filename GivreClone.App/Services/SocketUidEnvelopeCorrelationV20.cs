using System;
using System.Collections.Generic;
using GivreCore.Protobuf;

namespace GivreClone.App.Services;

/// <summary>
/// Exact-link read-only evidence that a REAL server Response envelope reused
/// a unique positive UID from an ENGINE request whose local TCP Flush completed.
/// This proves an envelope-level reply, NOT a successful game mutation.
/// Most gameplay requests use UID -1 and are intentionally never admitted.
/// </summary>
public readonly record struct SocketUidEnvelopeCorrelationV20Snapshot(
    CaptureLinkIdentity Link, long PostFlushRequestsWithUniqueUid,
    long MatchingResponseEnvelopes, long UnmatchedResponseEnvelopes,
    long AmbiguousOrReusedUids, long LastMatchedUid)
{
    public bool PositiveUidResponseObserved =>
        MatchingResponseEnvelopes > 0 && AmbiguousOrReusedUids == 0;
    public bool GameplayOutcomeVerified => false;
    public bool CanAuthorizeGameplay => false;
    public string BlockReason =>
        "UID Request/Response corrélé au mieux au niveau enveloppe ; " +
        "ni succès métier, ni résultat de cast/achat/craft, ni permission de replay.";
}

/// <summary>
/// Bounded projection from a single verified Socket source. It never sends,
/// completes a gameplay waiter or mutates a production journal.
/// </summary>
internal sealed class SocketUidEnvelopeCorrelationV20
{
    private const int MaximumTrackedUids = 128;
    private readonly object _gate = new();
    private readonly Dictionary<uint, LinkState> _byPid = new();

    private sealed class LinkState(CaptureLinkIdentity link)
    {
        public CaptureLinkIdentity Link = link;
        public long LastWriteOrdinal;
        public long LastServerSequence;
        public readonly HashSet<long> SeenRequestUids = new();
        public readonly Dictionary<long, long> Pending = new();
        public long PostFlushRequests;
        public long Matches;
        public long Unmatched;
        public long Ambiguous;
        public long LastMatchedUid;
        public bool CapacityExhausted;
    }

    public static bool TryReadSingleEnvelope(
        byte[]? framed, bool expectResponse,
        Func<string, string, int?>? fieldNumber, out long uid)
    {
        uid = 0;
        if (framed is not { Length: > 1 and <= 1_048_576 } ||
            fieldNumber is null) return false;
        try
        {
            int? request = fieldNumber("GameMessage", "request");
            int? response = fieldNumber("GameMessage", "response");
            int? eventSlot = fieldNumber("GameMessage", "event");
            int? uidSlot = expectResponse
                ? fieldNumber("Response", "uid") : fieldNumber("Request", "uid");
            int? contentSlot = expectResponse
                ? fieldNumber("Response", "content") : fieldNumber("Request", "content");
            if (request is not > 0 || response is not > 0 ||
                eventSlot is not > 0 || uidSlot is not > 0 ||
                contentSlot is not > 0 ||
                request == response || request == eventSlot ||
                response == eventSlot || uidSlot == contentSlot) return false;

            // SocketGameClient sends one complete length-prefixed frame per
            // callback. Do not identify a response from a partial, aggregated
            // or malformed TCP chunk.
            var frames = RawProtobuf.ConsumeFrames(framed, out var rest);
            if (frames.Count != 1 || rest.Length != 0) return false;
            var root = RawProtobuf.Parse(frames[0]);
            ProtoField? branch = null;
            foreach (var field in root)
            {
                if (field.Field == (expectResponse ? response : request))
                {
                    if (branch is not null || field.Wire != WireType.Len ||
                        field.Message is null) return false;
                    branch = field;
                }
                else if (field.Field == request || field.Field == response ||
                         field.Field == eventSlot) return false;
            }
            if (branch?.Message is not { } fields) return false;
            bool hasContent = false;
            bool hasUid = false;
            foreach (var field in fields)
            {
                if (field.Field == uidSlot)
                {
                    if (hasUid || field.Wire != WireType.Varint) return false;
                    hasUid = true;
                    uid = field.AsSignedVarint;
                }
                if (field.Field == contentSlot)
                {
                    if (hasContent || field.Wire != WireType.Len ||
                        field.Message is null) return false;
                    hasContent = true;
                }
            }
            return hasContent && hasUid && uid > 0;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException
                                   or InvalidOperationException
                                   or OverflowException)
        {
            uid = 0;
            return false;
        }
    }

    /// <summary>Called only from a same-source POST-Flush observer.</summary>
    public bool ObservePostFlush(CaptureLinkIdentity link, long ordinal,
        byte[]? framed, Func<string, string, int?>? fieldNumber)
    {
        if (!Valid(link) || ordinal <= 0) return false;
        bool parsed = TryReadSingleEnvelope(framed, false, fieldNumber, out var uid);
        lock (_gate)
        {
            var state = FindOrReplace(link);
            if (state is null || ordinal <= state.LastWriteOrdinal) return false;
            state.LastWriteOrdinal = ordinal;
            if (!parsed) return false;
            if (state.CapacityExhausted) return false;
            if (!state.SeenRequestUids.Add(uid))
            {
                state.Pending.Remove(uid);
                state.Ambiguous++;
                if (state.LastMatchedUid == uid) state.LastMatchedUid = 0;
                return false;
            }
            if (state.SeenRequestUids.Count > MaximumTrackedUids)
            {
                state.CapacityExhausted = true;
                state.Pending.Clear();
                state.Ambiguous++;
                return false;
            }
            state.Pending[uid] = ordinal;
            state.PostFlushRequests++;
            return true;
        }
    }

    /// <summary>Called only after source-bound decoder admission of S->C bytes.</summary>
    public bool ObserveServerFrame(CaptureLinkIdentity link, long sequence,
        byte[]? framed, Func<string, string, int?>? fieldNumber)
    {
        if (!Valid(link) || sequence <= 0) return false;
        bool parsed = TryReadSingleEnvelope(framed, true, fieldNumber, out var uid);
        lock (_gate)
        {
            if (!_byPid.TryGetValue(link.Pid, out var state) ||
                state.Link != link || sequence <= state.LastServerSequence)
                return false;
            state.LastServerSequence = sequence;
            if (!parsed || state.CapacityExhausted) return false;
            if (!state.Pending.Remove(uid))
            {
                state.Unmatched++;
                if (state.SeenRequestUids.Contains(uid))
                {
                    state.Ambiguous++; // duplicate or response after consumed UID
                    if (state.LastMatchedUid == uid) state.LastMatchedUid = 0;
                }
                return false;
            }
            state.Matches++;
            state.LastMatchedUid = uid;
            return true;
        }
    }

    public SocketUidEnvelopeCorrelationV20Snapshot? Read(uint pid)
    {
        lock (_gate)
        {
            if (!_byPid.TryGetValue(pid, out var s)) return null;
            return new SocketUidEnvelopeCorrelationV20Snapshot(s.Link,
                s.PostFlushRequests, s.Matches, s.Unmatched,
                s.Ambiguous, s.LastMatchedUid);
        }
    }

    public bool RevokeExact(CaptureLinkIdentity link)
    {
        lock (_gate)
            return _byPid.TryGetValue(link.Pid, out var s) &&
                s.Link == link && _byPid.Remove(link.Pid);
    }

    private LinkState? FindOrReplace(CaptureLinkIdentity link)
    {
        if (_byPid.TryGetValue(link.Pid, out var existing))
        {
            if (existing.Link == link) return existing;
            if (existing.Link.SourceInstanceId == link.SourceInstanceId &&
                (link.RunEpoch < existing.Link.RunEpoch ||
                 link.RunEpoch == existing.Link.RunEpoch &&
                 link.TunnelId <= existing.Link.TunnelId)) return null;
        }
        else if (_byPid.Count >= 1024) return null;
        var state = new LinkState(link);
        _byPid[link.Pid] = state;
        return state;
    }

    private static bool Valid(CaptureLinkIdentity link)
        => link.Pid > 0 && link.RunEpoch > 0 && link.TunnelId > 0 &&
            link.SourceInstanceId != Guid.Empty &&
            link.ConnectionToken != Guid.Empty;
}
