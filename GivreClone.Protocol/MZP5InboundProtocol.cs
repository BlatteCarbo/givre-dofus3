namespace GivreClone.Protocol;

/// <summary>Direction DECLARED by the caller, never inferred as authenticated transport origin.</summary>
public enum MZP5Direction { Unknown = 0, ClientToServer = 1, ServerToClient = 2 }

/// <summary>Envelope branch resolved by the pinned fields.json (no Dofus defaults).</summary>
public enum MZP5Branch { Request = 1, Event = 2, Response = 3 }

/// <summary>
/// Untrusted metadata for a single logical stream. A caller MAY label an input
/// "ServerToClient", but this label cannot certify its actual network provenance.
/// Use a NEW scope/generation and reader after a legitimate, separately verified reset.
/// </summary>
public sealed record MZP5SourceScope(
    string SourceLabel, string StreamKey, long ConnectionId, long Generation,
    string DeclaredClientVersion, string MatchingSha256, string FieldsSha256);

/// <summary>One independently sequenced UNFRAMED observation.</summary>
public sealed record MZP5InboundObservation(
    MZP5SourceScope Scope, MZP5Direction Direction, long Sequence);

public enum MZP5InboundStatus
{
    AcceptedShapeOnly,
    InvalidObservation,
    SourceMismatch,
    VersionMismatch,
    InvalidDirection,
    SequenceReplayOrReorder,
    SequenceGap,
    StreamQuarantined,
    MalformedWire,
    AmbiguousEnvelope,
    DirectionConflict,
    UnknownEnvelopeCodeSyntax
}

/// <summary>
/// Data is retained privately by this immutable-in-practice packet. The four
/// messages preserve original field order, repetitions and unknown wire bytes.
/// Consumers must not print payloads or mistake a decoded Response for an ACK.
/// </summary>
public sealed record MZP5DecodedPacket(
    MZP5Branch Branch, MZP5Direction ClaimedDirection,
    long ClaimedSequence, string Code, string? ClearName, long? Uid,
    MZP3WireMessage Root, MZP3WireMessage BranchFields,
    MZP3WireMessage AnyFields, MZP3WireMessage Payload)
{
    public bool KnownAlias => ClearName is not null;
    public bool IsAuthenticated => false;
    public bool ServerAckCertified => false;
    public bool ReplayAuthorized => false;
}

/// <summary>
/// No payload/identifiers/bytes in the rejection diagnostic. Parsing failures
/// are never promoted to a partial packet.
/// </summary>
public sealed record MZP5InboundResult(
    MZP5InboundStatus Status, MZP5DecodedPacket? Packet,
    MZP3WireError WireError = MZP3WireError.None)
{
    public bool Parsed => Status == MZP5InboundStatus.AcceptedShapeOnly && Packet is not null;
    public bool IsAuthenticated => false;
    public bool ServerAckCertified => false;
}

public sealed record MZP5InboundOpenResult(string Reason, MZP5InboundReader? Reader)
{
    public bool IsOfflineCandidate => Reader is not null;
    public bool IsRuntimeCertified => false;
}

/// <summary>Only categorical counters: never reveal source labels or packet contents.</summary>
public sealed record MZP5InboundSnapshot(
    long ClientPacketsAccepted, long ServerPacketsAccepted,
    long ObservationsRejected, bool ClientDirectionQuarantined,
    bool ServerDirectionQuarantined)
{
    public bool IsAuthenticated => false;
    public bool ServerAckCertified => false;
}

/// <summary>
/// Product-source neutral inbound protocol reader. Callers supply UNFRAMED
/// GameMessage bytes and an independently obtained, version-pinned MZP4 adapter.
/// The contract does NOT produce data from a socket, game, process, relay, or ACK.
/// </summary>
public sealed class MZP5InboundReader
{
    private readonly record struct Slots(
        int Request, int Event, int Response,
        int RequestContent, int RequestUid, int EventContent,
        int ResponseContent, int ResponseUid);

    private readonly MZP4ProtocolAdapter _catalogue;
    private readonly MZP5SourceScope _scope;
    private readonly Slots _slots;
    private readonly MZP3Limits _limits;
    private readonly object _gate = new();
    private long _clientSeq;
    private long _serverSeq;
    private long _clientAccepted;
    private long _serverAccepted;
    private long _rejected;
    private bool _clientQuarantined;
    private bool _serverQuarantined;

    private MZP5InboundReader(
        MZP4ProtocolAdapter catalogue, MZP5SourceScope scope,
        Slots slots, MZP3Limits limits)
    {
        _catalogue = catalogue;
        _scope = scope;
        _slots = slots;
        _limits = limits;
    }

    /// <summary>
    /// Admission is ONLY of a structurally reviewed offline snapshot and
    /// caller-declared scope. Source labels / digest equality are not attestation.
    /// </summary>
    public static MZP5InboundOpenResult Open(
        MZP4ProtocolAdapter? catalogue, MZP5SourceScope? scope,
        MZP3Limits? limits = null)
    {
        if (catalogue is null || scope is null || !ValidLabel(scope.SourceLabel) ||
            !ValidLabel(scope.StreamKey) || scope.ConnectionId <= 0 ||
            scope.Generation <= 0)
            return new("SCOPE_MISSING_OR_INVALID", null);
        if (!MZP0ProtocolAdmission.Supports37Family(scope.DeclaredClientVersion) ||
            !MZP0ProtocolAdmission.Supports37Family(catalogue.Reference.ClientVersion) ||
            !string.Equals(scope.MatchingSha256, catalogue.Reference.MatchingSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(scope.FieldsSha256, catalogue.Reference.FieldsSha256,
                StringComparison.OrdinalIgnoreCase))
            return new("VERSION_OR_CATALOGUE_MISMATCH", null);
        var budget = limits ?? MZP3Limits.Default;
        if (!budget.IsValid || budget.MaxDepth < 3)
            return new("INVALID_WIRE_LIMITS", null);

        int? Required(string message, string field) => catalogue.FieldNumber(message, field);
        var request = Required("GameMessage", "request");
        var evt = Required("GameMessage", "event");
        var response = Required("GameMessage", "response");
        var reqContent = Required("Request", "content");
        var reqUid = Required("Request", "uid");
        var eventContent = Required("Event", "content");
        var respContent = Required("Response", "content");
        var respUid = Required("Response", "uid");
        if (request is null || evt is null || response is null ||
            reqContent is null || reqUid is null || eventContent is null ||
            respContent is null || respUid is null)
            return new("INBOUND_SLOTS_MISSING", null);
        if (new HashSet<int> { request.Value, evt.Value, response.Value }.Count != 3 ||
            reqContent == reqUid || respContent == respUid)
            return new("INBOUND_SLOTS_COLLIDE", null);
        var slots = new Slots(request.Value, evt.Value, response.Value,
            reqContent.Value, reqUid.Value, eventContent.Value,
            respContent.Value, respUid.Value);
        return new("OFFLINE_SHAPE_ONLY",
            new MZP5InboundReader(catalogue, scope, slots, budget));
    }

    /// <summary>
    /// Strict contiguous sequence per direction, starting at 1. Once a decoded
    /// stream has a gap, replay or malformed bytes, that DIRECTION is quarantined.
    /// The only recovery is constructing a fresh reader with an explicitly new
    /// scope/generation: there is no ACK inference or blind sequence reset.
    /// </summary>
    public MZP5InboundResult ReadUnframed(
        MZP5InboundObservation? observation, ReadOnlySpan<byte> unframed)
    {
        lock (_gate)
        {
            if (observation?.Scope is null) return Reject(MZP5InboundStatus.InvalidObservation);
            if (observation.Scope.DeclaredClientVersion != _scope.DeclaredClientVersion)
                return Reject(MZP5InboundStatus.VersionMismatch);
            if (observation.Scope != _scope)
                return Reject(MZP5InboundStatus.SourceMismatch);
            if (observation.Direction is not (MZP5Direction.ClientToServer or MZP5Direction.ServerToClient))
                return Reject(MZP5InboundStatus.InvalidDirection);

            bool fromClient = observation.Direction == MZP5Direction.ClientToServer;
            if (fromClient ? _clientQuarantined : _serverQuarantined)
                return Reject(MZP5InboundStatus.StreamQuarantined);
            long previous = fromClient ? _clientSeq : _serverSeq;
            if (observation.Sequence <= previous || observation.Sequence <= 0)
                return Quarantine(fromClient, MZP5InboundStatus.SequenceReplayOrReorder);
            if (previous == long.MaxValue || observation.Sequence != previous + 1)
                return Quarantine(fromClient, MZP5InboundStatus.SequenceGap);

            var parsed = Decode(unframed, observation.Direction, observation.Sequence);
            if (!parsed.Parsed)
                return Quarantine(fromClient, parsed.Status, parsed.WireError);

            if (fromClient) { _clientSeq = observation.Sequence; _clientAccepted++; }
            else { _serverSeq = observation.Sequence; _serverAccepted++; }
            return parsed;
        }
    }

    public MZP5InboundSnapshot Snapshot()
    {
        lock (_gate)
            return new(_clientAccepted, _serverAccepted, _rejected,
                _clientQuarantined, _serverQuarantined);
    }

    private MZP5InboundResult Decode(
        ReadOnlySpan<byte> bytes, MZP5Direction direction, long sequence)
    {
        var root = MZP3RawProtobuf.TryParse(bytes, _limits);
        if (!root.Ok) return InvalidWire(root.Error);

        MZP3WireField? branchField = null;
        MZP5Branch branchType = default;
        foreach (var field in root.Message!.Fields)
        {
            MZP5Branch current = field.Number == _slots.Request ? MZP5Branch.Request
                : field.Number == _slots.Event ? MZP5Branch.Event
                : field.Number == _slots.Response ? MZP5Branch.Response : default;
            if (current == default) continue;
            if (branchField is not null || field.WireType != MZP3WireType.LengthDelimited)
                return Invalid(MZP5InboundStatus.AmbiguousEnvelope);
            branchField = field;
            branchType = current;
        }
        if (branchField is null) return Invalid(MZP5InboundStatus.AmbiguousEnvelope);
        if ((direction == MZP5Direction.ClientToServer && branchType != MZP5Branch.Request) ||
            (direction == MZP5Direction.ServerToClient && branchType == MZP5Branch.Request))
            return Invalid(MZP5InboundStatus.DirectionConflict);

        var branch = branchField.ParseEmbedded(_limits, 1);
        if (!branch.Ok) return InvalidWire(branch.Error);
        int contentSlot = branchType switch
        {
            MZP5Branch.Request => _slots.RequestContent,
            MZP5Branch.Event => _slots.EventContent,
            _ => _slots.ResponseContent
        };
        int? uidSlot = branchType switch
        {
            MZP5Branch.Request => _slots.RequestUid,
            MZP5Branch.Response => _slots.ResponseUid,
            _ => null
        };
        MZP3WireField? content = null;
        long? uid = null;
        foreach (var field in branch.Message!.Fields)
        {
            if (field.Number == contentSlot)
            {
                if (content is not null || field.WireType != MZP3WireType.LengthDelimited)
                    return Invalid(MZP5InboundStatus.AmbiguousEnvelope);
                content = field;
            }
            if (uidSlot.HasValue && field.Number == uidSlot.Value)
            {
                if (uid is not null || field.WireType != MZP3WireType.Varint)
                    return Invalid(MZP5InboundStatus.AmbiguousEnvelope);
                uid = unchecked((long)field.UnsignedValue!.Value);
            }
        }
        if (content is null) return Invalid(MZP5InboundStatus.AmbiguousEnvelope);
        var any = content.ParseEmbedded(_limits, 2);
        if (!any.Ok) return InvalidWire(any.Error);
        string? code = null;
        MZP3WireField? value = null;
        foreach (var field in any.Message!.Fields)
        {
            if (field.Number == 1)
            {
                if (code is not null || !field.TryGetStrictUtf8(out var typeUrl) ||
                    typeUrl is null || !typeUrl.StartsWith(MZP3EnvelopeCodec.TypeUrlPrefix,
                        StringComparison.Ordinal))
                    return Invalid(MZP5InboundStatus.AmbiguousEnvelope);
                code = typeUrl[MZP3EnvelopeCodec.TypeUrlPrefix.Length..];
                if (!ValidCode(code))
                    return Invalid(MZP5InboundStatus.UnknownEnvelopeCodeSyntax);
            }
            else if (field.Number == 2)
            {
                if (value is not null || field.WireType != MZP3WireType.LengthDelimited)
                    return Invalid(MZP5InboundStatus.AmbiguousEnvelope);
                value = field;
            }
        }
        if (code is null) return Invalid(MZP5InboundStatus.AmbiguousEnvelope);
        var payload = value is null
            ? MZP3RawProtobuf.TryParse(ReadOnlySpan<byte>.Empty, _limits, 3)
            : value.ParseEmbedded(_limits, 3);
        if (!payload.Ok) return InvalidWire(payload.Error);

        // Even an unrecognized alias is structurally parseable; never invent a
        // clear name or silently discard unknown fields from the original root.
        var packet = new MZP5DecodedPacket(branchType, direction, sequence,
            code, _catalogue.NameForCode(code), uid,
            root.Message!, branch.Message!, any.Message!, payload.Message!);
        return new(MZP5InboundStatus.AcceptedShapeOnly, packet);
    }

    private MZP5InboundResult InvalidWire(MZP3WireError error)
        => new(MZP5InboundStatus.MalformedWire, null, error);
    private static MZP5InboundResult Invalid(MZP5InboundStatus reason)
        => new(reason, null);

    private MZP5InboundResult Reject(MZP5InboundStatus reason)
    {
        _rejected++;
        return Invalid(reason);
    }

    private MZP5InboundResult Quarantine(bool fromClient,
        MZP5InboundStatus reason, MZP3WireError wireError = MZP3WireError.None)
    {
        if (fromClient) _clientQuarantined = true;
        else _serverQuarantined = true;
        _rejected++;
        return new(reason, null, wireError);
    }

    private static bool ValidLabel(string? label)
    {
        if (string.IsNullOrEmpty(label) || label.Length > 64 ||
            !char.IsAsciiLetterOrDigit(label[0])) return false;
        return label.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
    }

    private static bool ValidCode(string code)
    {
        if (code.Length is < 1 or > 128 || !char.IsAsciiLetter(code[0])) return false;
        return code.All(c => char.IsAsciiLetterOrDigit(c) || c is '_');
    }
}
