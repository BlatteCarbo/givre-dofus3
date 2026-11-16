using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using GivreCore.Mitm;
using GivreCore.Protobuf;

namespace GivreClone.App.Services;

/// <summary>Un paquet Dofus decode : PID du client, sens, code obfusque, nom clair (si mappe), champs decodes,
/// + la trame BRUTE (octets du GameMessage, non prefixee) pour pouvoir la rejouer telle quelle (ex. WhoAmI).</summary>
public sealed record DecodedPacket(uint Pid, bool ClientToServer, string Code, string? Name, IReadOnlyList<ProtoField> Payload, byte[] RawFrame)
{
    public bool PayloadDecoded { get; init; } = true;
    // Assigned by CaptureDecoder while holding its framing lock. These are
    // LOCAL decoder identities, NOT authenticated TCP connection identities.
    // Directly constructed/test packets retain zero (unattributed).
    public long DecoderEpoch { get; init; }
    public long DecoderOrder { get; init; }
    // Jamais le code obfusqué via une interpolation distraite ($"{p}") : le nom clair seulement (ProtoLog.Named pour un log dev).
    public override string ToString() => Name ?? "?";
}

/// <summary>
/// Read-only observation of a separate, source-validated HEADLESS transport
/// frame. This is deliberately NOT a DecodedPacket: there is no causal link
/// between BotHostService.TransportFrameObserved and the HandshakeRelay.OnData
/// or SocketGameClient.FeedFrame bytes consumed by LaunchService.
/// An opaque link token, equal PID, matching protobuf body/hash or timestamp
/// cannot bridge two independent transport producers.
/// </summary>
public sealed record TransportCaptureIngressDiagnostic(
    TransportLinkProvenance Link, long FrameSequence, Direction Direction,
    int RawBodyBytes, IReadOnlyList<string> MessageNames)
{
    public bool CanJoinLaunchDecoder => false;
    public bool ServerConfirmed => false;
    public bool CanAuthorizeCast => false;
    public bool IsServerAck => false;
}

/// <summary>
/// Identity that a SAME-source game relay must mint when it accepts a tunnel.
/// SourceInstanceId is an actual relay-instance GUID, not a PID-derived value;
/// RunEpoch and TunnelId must not be reused for that source. The old PID-only
/// OnData callback cannot supply any of these values.
/// </summary>
public readonly record struct CaptureLinkIdentity(
    Guid SourceInstanceId, uint Pid, long RunEpoch, long TunnelId,
    Guid ConnectionToken = default);

/// <summary>
/// Packet decoded from one explicitly linked relay's framed byte stream.
/// This wrapper is only a candidate: call IsCurrentLinkedPacket at the point
/// of use with the SAME relay's IsCurrentGameLink predicate. It is NEVER a
/// correlated positive gameplay receipt or permission to send a cast.
/// </summary>
public sealed record LinkScopedDecodedPacket(
    DecodedPacket Packet, CaptureLinkIdentity Link, long SourceChunkSequence)
{
    public bool ServerConfirmed => false;
    public bool CanAuthorizeCast => false;
    public bool IsServerAck => false;
}

/// <summary>
/// Deframe + decode le flux protobuf tape sur le transport d'un client lance (via HandshakeRelay.OnData).
/// Bufferise par (PID, sens) car une frame protobuf (varint(len)+payload) peut chevaucher plusieurs lectures TCP.
/// Resout le code obfusque -&gt; nom clair via le ProtocolCatalog. N'altere JAMAIS le flux (observation pure).
/// </summary>
public sealed class CaptureDecoder
{
    private sealed class LinkedState(CaptureLinkIdentity link, long epoch)
    {
        public readonly CaptureLinkIdentity Link = link;
        public readonly long Epoch = epoch;
        public readonly List<byte> Up = new();
        public readonly List<byte> Down = new();
        public long LastUpSequence;
        public long LastDownSequence;
    }

    // Strictly separate from legacy PID-only reassembly. A missing or forged
    // link cannot borrow an older buffer or a locally minted decoder epoch.
    private readonly Dictionary<CaptureLinkIdentity, LinkedState> _linkedStates = new();
    private readonly Dictionary<uint, CaptureLinkIdentity> _currentLinkedByPid = new();
    private readonly HashSet<CaptureLinkIdentity> _revokedLinked = new();
    private readonly ConditionalWeakTable<DecodedPacket, CaptureLinkIdentityBox> _linkedOrigin = new();
    private readonly ConditionalWeakTable<LinkScopedDecodedPacket, object> _linkedEnvelope = new();
    private readonly ConditionalWeakTable<LinkScopedDecodedPacket, object> _consumedLinked = new();
    private sealed record CaptureLinkIdentityBox(CaptureLinkIdentity Link, long Epoch);
    private const int MaximumLinkedLifetimes = 4096;

    /// <summary>Independent linked packet channel, never an implicit second
    /// Packet event. The existing PID-only Feed/Packet stay unverified.</summary>
    public event Action<LinkScopedDecodedPacket>? LinkedPacket;

    /// <summary>Exact-link revocation, for diagnostics using linked packets
    /// only. It is NOT a server message, move/cast ACK or proof of game state.</summary>
    public event Action<CaptureLinkIdentity, long>? LinkedLocalEpochRevoked;

    /// <summary>
    /// Linked, framed chunk input for a FUTURE SAME-source LocalHandshakeRelay
    /// adapter. Required producer callback data:
    ///   (CaptureLinkIdentity, strictly increasing chunk sequence per direction,
    ///    clientToServer, length-prefixed bytes, IsCurrentGameLink).
    /// Each chunk must carry the actual source-owned identity: reconstructing
    /// it from PID, matching bodies or BotHostService's independent transport
    /// is forbidden. This adapter is additive, not wired to OnData/Socket.
    /// On each resulting packet it rechecks link activity BEFORE event delivery,
    /// and only emits to LinkedPacket (never to legacy Packet). No ACK/cast
    /// authorization is granted by any linked frame.
    /// </summary>
    public bool FeedLinkScoped(CaptureLinkIdentity link, long sourceChunkSequence,
        bool clientToServer, byte[] framedChunk,
        Func<CaptureLinkIdentity, bool>? isCurrentGameLink)
    {
        if (!ValidLink(link) || sourceChunkSequence <= 0
            || framedChunk is null || framedChunk.Length == 0
            || framedChunk.Length > 1_048_576 || isCurrentGameLink is null
            || !SafeCurrentLink(link, isCurrentGameLink)) return false;

        List<LinkScopedDecodedPacket>? emit = null;
        CaptureLinkIdentity? replaced = null;
        long replacedEpoch = 0;
        bool invalidSequence = false;
        bool invalidFrame = false;
        lock (_gate)
        {
            if (_revokedLinked.Contains(link)) return false;
            if (!_linkedStates.ContainsKey(link)
                && (_linkedStates.Count + _revokedLinked.Count >= MaximumLinkedLifetimes
                    || _lastEpoch == long.MaxValue)) return false;
            if (_currentLinkedByPid.TryGetValue(link.Pid, out var previous)
                && previous != link)
            {
                // One live game tunnel per PID. A new relay instance MUST first
                // revoke the old one. Same-source renewal may advance only
                // through a strictly newer relay run/tunnel identity.
                if (previous.SourceInstanceId != link.SourceInstanceId
                    || link.RunEpoch < previous.RunEpoch
                    || (link.RunEpoch == previous.RunEpoch
                        && link.TunnelId <= previous.TunnelId))
                    return false;
                if (_linkedStates.TryGetValue(previous, out var old))
                {
                    replaced = previous;
                    replacedEpoch = old.Epoch;
                }
                _linkedStates.Remove(previous);
                _currentLinkedByPid.Remove(link.Pid);
                _revokedLinked.Add(previous);
            }

            if (!_linkedStates.TryGetValue(link, out var state))
            {
                _linkedStates[link] = state = new LinkedState(link, ++_lastEpoch);
                _currentLinkedByPid[link.Pid] = link;
            }
            long last = clientToServer ? state.LastUpSequence : state.LastDownSequence;
            if (sourceChunkSequence <= last)
                invalidSequence = true;
            else
            {
                if (clientToServer) state.LastUpSequence = sourceChunkSequence;
                else state.LastDownSequence = sourceChunkSequence;

                List<byte> buffer = clientToServer ? state.Up : state.Down;
                buffer.AddRange(framedChunk);
                byte[][] frames = [];
                try
                {
                    frames = RawProtobuf.ConsumeFrames(buffer.ToArray(), out var rest).ToArray();
                    buffer.Clear();
                    buffer.AddRange(rest);
                    if (buffer.Count > 1_048_576) invalidFrame = true;
                }
                catch { invalidFrame = true; }
                if (invalidFrame) buffer.Clear();

                foreach (var frame in invalidFrame ? Array.Empty<byte[]>() : frames)
                {
                    if (_lastOrder == long.MaxValue) break;
                    try
                    {
                        var tree = RawProtobuf.Parse(frame);
                        foreach (var hit in RawProtobuf.FindTypeUrls(tree))
                        {
                            if (_lastOrder == long.MaxValue) break;
                            IReadOnlyList<ProtoField>? payload = hit.Payload
                                ?? (LargeFrameDecoder.Decode(frame) is { } big && big.Code == hit.Code
                                    ? big.Payload : null);
                            var packet = new DecodedPacket(link.Pid, clientToServer,
                                hit.Code, _catalog.NameForCode(hit.Code),
                                payload ?? Array.Empty<ProtoField>(), frame)
                            {
                                PayloadDecoded = payload is not null,
                                DecoderEpoch = state.Epoch,
                                DecoderOrder = ++_lastOrder
                            };
                            _linkedOrigin.Add(packet, new CaptureLinkIdentityBox(link, state.Epoch));
                            var linked = new LinkScopedDecodedPacket(packet, link, sourceChunkSequence);
                            _linkedEnvelope.Add(linked, new object());
                            (emit ??= new()).Add(linked);
                        }
                    }
                    catch (FormatException) when (LargeFrameDecoder.Decode(frame) is { } big)
                    {
                        // Same valid-large-frame fallback as legacy Feed. A
                        // legitimate server price table must not poison or
                        // revoke the whole linked capture lifetime.
                        if (_lastOrder == long.MaxValue) break;
                        var packet = new DecodedPacket(link.Pid, clientToServer,
                            big.Code, _catalog.NameForCode(big.Code), big.Payload, frame)
                        {
                            DecoderEpoch = state.Epoch,
                            DecoderOrder = ++_lastOrder
                        };
                        _linkedOrigin.Add(packet, new CaptureLinkIdentityBox(link, state.Epoch));
                        var linked = new LinkScopedDecodedPacket(packet, link, sourceChunkSequence);
                        _linkedEnvelope.Add(linked, new object());
                        (emit ??= new()).Add(linked);
                    }
                    catch
                    {
                        // If a complete framed body cannot be decoded, do not
                        // retain its partial state or emit a later mark as if
                        // this stream were uninterrupted.
                        invalidFrame = true;
                        break;
                    }
                }
            }
        }

        if (replaced is CaptureLinkIdentity retired)
            NotifyLinkedRevoked(retired, replacedEpoch);
        if (invalidSequence || invalidFrame || !SafeCurrentLink(link, isCurrentGameLink))
        {
            RevokeLink(link);
            return false;
        }
        if (emit is null) return true;
        foreach (var packet in emit)
        {
            if (!IsCurrentLinkedPacket(packet, isCurrentGameLink))
            {
                RevokeLink(link);
                return false;
            }
            var listeners = LinkedPacket;
            if (listeners is null) continue;
            foreach (Action<LinkScopedDecodedPacket> listener in listeners.GetInvocationList())
            {
                if (!IsCurrentLinkedPacket(packet, isCurrentGameLink))
                {
                    RevokeLink(link);
                    return false;
                }
                try { listener(packet); }
                catch { /* an observer cannot interrupt the source relay */ }
            }
        }
        if (!SafeCurrentLink(link, isCurrentGameLink))
        {
            RevokeLink(link);
            return false;
        }
        return true;
    }

    private static bool ValidLink(CaptureLinkIdentity link) =>
        link.SourceInstanceId != Guid.Empty && link.Pid != 0
        && link.RunEpoch > 0 && link.TunnelId > 0;

    private static bool SafeCurrentLink(CaptureLinkIdentity link,
        Func<CaptureLinkIdentity, bool> check)
    {
        try { return check(link); }
        catch { return false; }
    }

    /// <summary>Revalidate the EXACT emitted object and same-source link.
    /// A forged record clone, stopped relay or reused PID never passes.</summary>
    public bool IsCurrentLinkedPacket(LinkScopedDecodedPacket? observation,
        Func<CaptureLinkIdentity, bool>? isCurrentGameLink)
    {
        if (observation is null || observation.Packet is null
            || isCurrentGameLink is null
            || !SafeCurrentLink(observation.Link, isCurrentGameLink)) return false;
        bool locallyCurrent;
        lock (_gate)
        {
            var p = observation.Packet;
            locallyCurrent = _linkedEnvelope.TryGetValue(observation, out _)
                && _linkedOrigin.TryGetValue(p, out var stamp)
                && stamp.Link == observation.Link && stamp.Epoch == p.DecoderEpoch
                && p.Pid == observation.Link.Pid && p.DecoderOrder > 0
                && !_revokedLinked.Contains(observation.Link)
                && _currentLinkedByPid.TryGetValue(p.Pid, out var active)
                && active == observation.Link
                && _linkedStates.TryGetValue(active, out var state)
                && state.Epoch == p.DecoderEpoch;
        }
        return locallyCurrent && SafeCurrentLink(observation.Link, isCurrentGameLink);
    }

    /// <summary>One-shot admission for a CURRENT canonical linked wrapper.
    /// Two consumers cannot independently consume the same packet as evidence;
    /// a failed post-check remains consumed (fail closed).</summary>
    public bool TryConsumeLinkedPacket(LinkScopedDecodedPacket? observation,
        Func<CaptureLinkIdentity, bool>? isCurrentGameLink)
    {
        if (!IsCurrentLinkedPacket(observation, isCurrentGameLink)
            || observation is null) return false;
        lock (_gate)
        {
            if (!_linkedEnvelope.TryGetValue(observation, out _)
                || _consumedLinked.TryGetValue(observation, out _)
                || _revokedLinked.Contains(observation.Link)
                || !_currentLinkedByPid.TryGetValue(observation.Link.Pid, out var active)
                || active != observation.Link) return false;
            _consumedLinked.Add(observation, new object());
        }
        return IsCurrentLinkedPacket(observation, isCurrentGameLink);
    }

    /// <summary>Current LINKED local epoch only, never a TCP/gameplay ACK.
    /// No fallback to PID-only CurrentLocalEpoch is allowed.</summary>
    public long CurrentLinkedEpoch(CaptureLinkIdentity link,
        Func<CaptureLinkIdentity, bool>? isCurrentGameLink)
    {
        if (!ValidLink(link) || isCurrentGameLink is null
            || !SafeCurrentLink(link, isCurrentGameLink)) return 0;
        long epoch;
        lock (_gate)
            epoch = !_revokedLinked.Contains(link)
                && _currentLinkedByPid.TryGetValue(link.Pid, out var active)
                && active == link && _linkedStates.TryGetValue(link, out var state)
                ? state.Epoch : 0;
        return SafeCurrentLink(link, isCurrentGameLink) ? epoch : 0;
    }

    /// <summary>Revoke only this original relay link. A late close for an
    /// earlier link must not invalidate a newer same-PID tunnel.</summary>
    public void RevokeLink(CaptureLinkIdentity link)
    {
        if (!ValidLink(link)) return;
        long retiredEpoch = 0;
        lock (_gate)
        {
            if (_linkedStates.TryGetValue(link, out var old))
            {
                retiredEpoch = old.Epoch;
                _linkedStates.Remove(link);
            }
            if (_currentLinkedByPid.TryGetValue(link.Pid, out var current)
                && current == link) _currentLinkedByPid.Remove(link.Pid);
            if (_revokedLinked.Count < MaximumLinkedLifetimes)
                _revokedLinked.Add(link);
        }
        if (retiredEpoch > 0) NotifyLinkedRevoked(link, retiredEpoch);
    }

    private void NotifyLinkedRevoked(CaptureLinkIdentity link, long epoch)
    {
        if (LinkedLocalEpochRevoked is not { } listeners) return;
        foreach (Action<CaptureLinkIdentity, long> listener in listeners.GetInvocationList())
            try { listener(link, epoch); }
            catch { /* diagnostic consumers cannot block source teardown */ }
    }

    /// <summary>
    /// A typed, purely observational port for BotHostService's REAL raw-body
    /// event. The caller must pass source.IsCurrentTransportLink as the
    /// validator and must check again before using the returned diagnostic.
    /// Unlike Feed, this NEVER changes PID framing, emits Packet, constructs a
    /// DecodedPacket or mutates ComboIngress. RawFrame here is an UNFRAMED
    /// protobuf body (TcpProxyServer), whereas Feed expects varint-length
    /// prefixed chunks (HandshakeRelay / SocketGameClient).
    ///
    /// Future authoritative integration REQUIRES the SAME producer to deliver
    /// a source-issued link identity on each packet/frame, an IsCurrentLink
    /// check at decode/consume and exact-link Close/Stop revocation. No PID,
    /// payload hash or unrelated BotHostService event may be used as a join.
    /// Even that link would prove transport only, not cast ACK or game effects.
    /// </summary>
    public TransportCaptureIngressDiagnostic? InspectTypedTransportFrame(
        TransportFrameObservation? observation,
        Func<TransportLinkProvenance, bool>? isCurrentLink)
    {
        if (observation is null || isCurrentLink is null
            || observation.FrameSequence <= 0 || observation.RawFrame is not { Length: > 0 and <= 1_048_576 }
            || observation.Provenance.ProxyInstanceId == Guid.Empty
            || observation.Provenance.RunEpoch <= 0 || observation.Provenance.LinkToken <= 0
            || observation.Provenance.ConnectionId <= 0 || observation.Provenance.ProcessId == 0
            || observation.Direction is not (Direction.ClientToServer or Direction.ServerToClient))
            return null;

        // A disconnected link is never sampled even for diagnostics.
        // Double-check the SAME producer after parsing: Stop/reconnect may
        // happen synchronously inside an observer or while parsing the body.
        try
        {
            if (!isCurrentLink(observation.Provenance)) return null;
            var names = RawProtobuf.FindTypeUrls(RawProtobuf.Parse(observation.RawFrame))
                .Take(32).Select(hit => _catalog.NameForCode(hit.Code) ?? hit.Code)
                .Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
            if (!isCurrentLink(observation.Provenance)) return null;
            return new TransportCaptureIngressDiagnostic(observation.Provenance,
                observation.FrameSequence, observation.Direction,
                observation.RawFrame.Length, Array.AsReadOnly(names));
        }
        catch
        {
            // A source can revoke or throw during the check; a malformed body
            // is never admitted to this optional diagnostic port.
            return null;
        }
    }

    private readonly ProtocolCatalog _catalog;
    private readonly object _gate = new();
    private readonly Dictionary<(uint Pid, bool Up), List<byte>> _buffers = new();
    private readonly Dictionary<uint, long> _epochs = new();
    // Serialize legacy PID-only delivery with Forget for the SAME synthetic
    // PID. A bounded stripe pool avoids leaking a lock for every reconnecting
    // PID. Feed never waits for a stripe while holding _gate, so Forget's
    // stripe->gate order cannot deadlock with the framing lock.
    private static readonly object[] DeliveryGates =
        Enumerable.Range(0, 256).Select(_ => new object()).ToArray();
    private static object DeliveryGate(uint pid) => DeliveryGates[pid % (uint)DeliveryGates.Length];
    // Local decoder attribution by object IDENTITY, not record structural equality.
    // A record `with` clone cannot inherit a valid decoder origin. Weak keys do
    // not retain old packet/payload graphs after subscribers release them.
    private readonly ConditionalWeakTable<DecodedPacket, LocalStamp> _origin = new();
    private sealed record LocalStamp(uint Pid, long Epoch, long Order);
    private long _lastEpoch;
    private long _lastOrder;

    public event Action<DecodedPacket>? Packet;
    /// <summary>
    /// Notification that a PID's LOCAL framing epoch was retired by Forget.
    /// This does not identify or authenticate any TCP connection. Subscribers
    /// must compare the retired epoch before clearing newer diagnostic state.
    /// Fired outside the decoder lock to avoid lock inversion with consumers.
    /// </summary>
    public event Action<uint, long>? LocalEpochRevoked;

    public CaptureDecoder(ProtocolCatalog catalog) => _catalog = catalog;

    /// <summary>Alimente le decodeur avec des octets bruts deja transmis (copie appartenant a l'appelant).</summary>
    public void Feed(uint pid, bool clientToServer, byte[] data)
    {
        // A source callback must not acquire a NEW local epoch while a
        // same-PID Forget is waiting to retire the old one. Serializing the
        // entire parse + dispatch with Forget also avoids a replacement
        // ingress being accidentally revoked by the previous close.
        lock (DeliveryGate(pid))
        {
        List<DecodedPacket>? emit = null;
        lock (_gate)
        {
            // Increment when a previously forgotten PID is decoded again.
            // This prevents a frame decoded before Forget from being
            // mistaken for one decoded after it when callbacks race.
            if (!_epochs.TryGetValue(pid, out long epoch))
            {
                if (_lastEpoch == long.MaxValue) return; // never wrap identity
                _epochs[pid] = epoch = ++_lastEpoch;
            }
            var key = (pid, clientToServer);
            if (!_buffers.TryGetValue(key, out var buf)) _buffers[key] = buf = new List<byte>();
            buf.AddRange(data);

            var frames = RawProtobuf.ConsumeFrames(buf.ToArray(), out var rest);
            buf.Clear();
            buf.AddRange(rest);
            // AUDIT M7 : garde-fou anti-DoS. Une trame de jeu légitime est petite ; si le reste incomplet dépasse 1 Mo,
            // c'est un flux hostile/désynchronisé qui ne se recadrera jamais -> on jette le buffer (borne mémoire + coupe le O(n²)).
            if (buf.Count > 1_048_576) { buf.Clear(); GivreLauncher.Core.Transport.TransportLog.Line(pid, "[decode] buffer de reframe > 1 Mo jeté (flux désynchronisé)"); }

            foreach (var frame in frames)
            {
                try
                {
                    var tree = RawProtobuf.Parse(frame);
                    foreach (var hit in RawProtobuf.FindTypeUrls(tree))
                    {
                        var name = _catalog.NameForCode(hit.Code);
                        // Charge utile illisible pour le décodeur générique (plafond de champs : table des prix moyens) -> secours sans plafond.
                        IReadOnlyList<ProtoField>? payload = hit.Payload
                            ?? (LargeFrameDecoder.Decode(frame) is { } big && big.Code == hit.Code ? big.Payload : null);
                        if (_lastOrder == long.MaxValue) continue;
                        var packet = new DecodedPacket(pid, clientToServer, hit.Code, name,
                            payload ?? Array.Empty<ProtoField>(), frame)
                            { PayloadDecoded = payload is not null, DecoderEpoch = epoch, DecoderOrder = ++_lastOrder };
                        _origin.Add(packet, new LocalStamp(pid, epoch, packet.DecoderOrder));
                        (emit ??= new List<DecodedPacket>()).Add(packet);
                    }
                }
                catch (FormatException) when (LargeFrameDecoder.Decode(frame) is { } big)
                {
                    // Trame valide mais au-delà du plafond de champs du décodeur générique (table des prix moyens).
                    if (_lastOrder != long.MaxValue)
                    {
                        var packet = new DecodedPacket(pid, clientToServer, big.Code,
                            _catalog.NameForCode(big.Code), big.Payload, frame)
                            { DecoderEpoch = epoch, DecoderOrder = ++_lastOrder };
                        _origin.Add(packet, new LocalStamp(pid, epoch, packet.DecoderOrder));
                        (emit ??= new List<DecodedPacket>()).Add(packet);
                    }
                }
                catch { /* frame incomplete/malformee : ignoree */ }
            }
        }
        // Deliver outside the framing lock but under the SAME PID delivery
        // gate that protects Forget's epoch revocation. Otherwise an old Feed
        // can parse, lose a race to Forget/reconnect, then mutate the new
        // session via a delayed Packet subscriber. Check per subscriber too:
        // a callback may reenter Forget synchronously.
        if (emit is not null && Packet is { } subscribers)
        {
            // Snapshot subscribers ONCE per received chunk, not once per
            // decoded packet; high-volume fight feeds otherwise allocate a
            // delegate array for each event.
            var handlers = subscribers.GetInvocationList();
            foreach (var p in emit)
            {
                foreach (Action<DecodedPacket> subscriber in handlers)
                {
                    if (!IsCurrentLocalEpoch(p)) break;
                    subscriber(p);
                }
            }
        }
        }
    }

    /// <summary>Oublie les buffers d'un PID (client ferme).</summary>
    public void Forget(uint pid)
    {
        long retired = 0;
        lock (DeliveryGate(pid))
        {
            lock (_gate)
            {
                _buffers.Remove((pid, true));
                _buffers.Remove((pid, false));
                if (_epochs.TryGetValue(pid, out retired)) _epochs.Remove(pid);
            }
        }
        if (retired > 0 && LocalEpochRevoked is { } listeners)
        {
            // A diagnostic subscriber must never interrupt the transport's
            // disconnect/cleanup path or prevent the other revocation listeners.
            foreach (Action<uint, long> callback in listeners.GetInvocationList())
            {
                try { callback(pid, retired); }
                catch (Exception e)
                {
                    GivreLauncher.Core.Transport.TransportLog.Line(pid,
                        $"[decode] local epoch revocation observer failed ({e.GetType().Name})");
                }
            }
        }
    }

    /// <summary>
    /// Is this packet from the decoder's current *local* framing epoch?
    /// A delayed old Feed callback fails after Forget. A new Feed from a stale
    /// socket can still acquire a new epoch, so this never authenticates TCP.
    /// </summary>
    public bool IsCurrentLocalEpoch(DecodedPacket packet)
    {
        if (packet is null || packet.DecoderEpoch <= 0 || packet.DecoderOrder <= 0) return false;
        lock (_gate)
            return _origin.TryGetValue(packet, out var stamp)
                && stamp.Pid == packet.Pid && stamp.Epoch == packet.DecoderEpoch
                && stamp.Order == packet.DecoderOrder
                && _epochs.TryGetValue(packet.Pid, out long epoch)
                && epoch == stamp.Epoch;
    }

    /// <summary>
    /// Current local framing epoch, or zero after Forget. Useful only to
    /// revoke diagnostic snapshots; it cannot attest a TCP peer/session.
    /// </summary>
    public long CurrentLocalEpoch(uint pid)
    {
        lock (_gate) return _epochs.TryGetValue(pid, out long epoch) ? epoch : 0;
    }
}
