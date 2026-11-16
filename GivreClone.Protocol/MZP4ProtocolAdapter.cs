using System.Text.Json;

namespace GivreClone.Protocol;

/// <summary>
/// SHA pins must be supplied by a separate, trusted catalogue distribution step.
/// SourceLabel is diagnostic provenance only: it is NOT a client or sender attestation.
/// </summary>
public sealed record MZP4CatalogReference(
    string ClientVersion, string MatchingSha256, string FieldsSha256,
    string SourceLabel);

public sealed record MZP4OpenResult(string Reason, MZP4ProtocolAdapter? Adapter,
    MZP0MappingReview? MappingReview)
{
    public bool IsOfflineCandidate => Adapter is not null;
    public bool IsRuntimeCertified => false;
}

/// <summary>
/// A neutral, production-source protocol seam for MZP3. Admits only SHA-pinned,
/// structurally unambiguous version-specific catalogues as OFFLINE candidates.
/// No capability in this class authorizes a live client, framing, or ACK.
/// </summary>
public sealed class MZP4ProtocolAdapter
{
    private const string TypePrefix = MZP3EnvelopeCodec.TypeUrlPrefix;
    private const string NamePrefix = "MessageType.";
    private readonly Dictionary<string, string> _codesByName;
    private readonly Dictionary<string, string> _namesByCode;
    private readonly Dictionary<string, Dictionary<string, int>> _fields;
    private readonly MZP3RequestSlots _slots;

    private MZP4ProtocolAdapter(MZP4CatalogReference reference,
        Dictionary<string, string> codesByName, Dictionary<string, string> namesByCode,
        Dictionary<string, Dictionary<string, int>> fields, MZP3RequestSlots slots)
    {
        Reference = reference;
        _codesByName = codesByName;
        _namesByCode = namesByCode;
        _fields = fields;
        _slots = slots;
    }

    public MZP4CatalogReference Reference { get; }
    public bool IsRuntimeCertified => false;
    public bool CanSendToGame => false;
    public bool CanCertifyServerAck => false;

    /// <summary>
    /// Validate independent SHA pins, exact version, catalogue shape, all tag
    /// collisions and mandatory Request slots BEFORE publishing an adapter.
    /// Payload field names can be additionally required by the caller.
    /// </summary>
    public static MZP4OpenResult Open(ReadOnlySpan<byte> matchingUtf8,
        ReadOnlySpan<byte> fieldsUtf8, MZP4CatalogReference? reference,
        IReadOnlyCollection<MZP0RequiredField>? additionalRequired = null)
    {
        if (reference is null || string.IsNullOrWhiteSpace(reference.SourceLabel) ||
            reference.SourceLabel.Length > 256)
            return new("PROVENANCE_REFERENCE_MISSING", null, null);
        if (!MZP0ProtocolAdmission.Supports37Family(reference.ClientVersion))
            return new("CLIENT_VERSION_NOT_VERIFIED", null, null);

        var required = new List<MZP0RequiredField>
        {
            new("GameMessage", "request"), new("Request", "content"), new("Request", "uid")
        };
        if (additionalRequired is not null)
        {
            if (additionalRequired.Count > 4096)
                return new("REQUIRED_FIELDS_EXCESSIVE", null, null);
            required.AddRange(additionalRequired);
        }
        var review = MZP0ProtocolAdmission.Review(matchingUtf8, fieldsUtf8,
            reference.ClientVersion, reference.MatchingSha256,
            reference.FieldsSha256, required);
        if (review.Verdict != MZP0MappingVerdict.OfflineCandidateOnly)
            return new(review.Reason, null, review);

        // Review already validated shape and pins. Build private immutable-in-practice
        // dictionaries from the *same* in-memory bytes, never from a fallback file.
        try
        {
            using var match = JsonDocument.Parse(matchingUtf8.ToArray());
            using var fieldDoc = JsonDocument.Parse(fieldsUtf8.ToArray());
            var byName = new Dictionary<string, string>(StringComparer.Ordinal);
            var byCode = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var mapping in match.RootElement.EnumerateObject())
            {
                string code = mapping.Name[TypePrefix.Length..];
                string clearName = mapping.Value.GetString()![NamePrefix.Length..];
                if (!byName.TryAdd(clearName, code) || !byCode.TryAdd(code, clearName))
                    return new("ALIAS_AMBIGUOUS", null, review);
            }

            var fields = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            foreach (var message in fieldDoc.RootElement.EnumerateObject())
            {
                var namedFields = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var field in message.Value.EnumerateObject())
                    namedFields.Add(field.Name, field.Value.GetInt32());
                fields.Add(message.Name, namedFields);
            }
            int requestSlot = fields["GameMessage"]["request"];
            int contentSlot = fields["Request"]["content"];
            int uidSlot = fields["Request"]["uid"];
            var slots = new MZP3RequestSlots(requestSlot, contentSlot, uidSlot);
            slots.Validate();
            return new("OFFLINE_CANDIDATE_ONLY",
                new MZP4ProtocolAdapter(reference, byName, byCode, fields, slots), review);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or
            KeyNotFoundException or InvalidOperationException)
        {
            return new("CATALOGUE_RESOLUTION_FAILED", null, review);
        }
    }

    public string? CodeForName(string clearName) =>
        clearName is not null && _codesByName.TryGetValue(clearName, out var code) ? code : null;

    public string? NameForCode(string code) =>
        code is not null && _namesByCode.TryGetValue(code, out var name) ? name : null;

    public int? FieldNumber(string message, string field) =>
        message is not null && field is not null &&
        _fields.TryGetValue(message, out var names) && names.TryGetValue(field, out var number)
            ? number : null;

    public MZP4NamedPayloadBuilder ForMessage(string clearName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clearName);
        if (!_codesByName.ContainsKey(clearName))
            throw new InvalidOperationException($"Message '{clearName}' absent du catalogue épinglé.");
        if (!_fields.ContainsKey(clearName))
            throw new InvalidOperationException($"Champs de '{clearName}' absents du catalogue épinglé.");
        return new MZP4NamedPayloadBuilder(this, clearName);
    }

    internal int RequireField(string message, string field) =>
        FieldNumber(message, field) ?? throw new InvalidOperationException(
            $"Champ '{message}.{field}' absent du catalogue épinglé.");

    /// <summary>
    /// Build UNFRAMED bytes only. Payload must come from THIS adapter's named
    /// builder, so names/tags from two catalogues cannot be mixed accidentally.
    /// </summary>
    public byte[] BuildClientRequest(MZP4NamedPayloadBuilder payload, long uid,
        MZP3Limits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!ReferenceEquals(payload.Owner, this))
            throw new InvalidOperationException("Payload et enveloppe de catalogues différents.");
        var budget = limits ?? MZP3Limits.Default;
        byte[] body = payload.Build().Encode(budget);
        string code = _codesByName[payload.ClearName];
        return MZP3EnvelopeCodec.BuildClientRequest(_slots, code, body, uid, budget);
    }

    /// <summary>
    /// Offline extractor for an already separated UNFRAMED buffer. It validates
    /// shape, known alias and payload wire; it never authenticates origin.
    /// </summary>
    public bool TryReadClientRequest(ReadOnlySpan<byte> unframed,
        out MZP4DecodedRequest? decoded, out string reason, MZP3Limits? limits = null)
    {
        decoded = null;
        reason = "INVALID_ENVELOPE";
        if (!MZP3EnvelopeCodec.TryReadClientRequest(unframed, _slots,
            out var envelope, limits) || envelope is null) return false;
        if (!_namesByCode.TryGetValue(envelope.Code, out var name))
        {
            reason = "CODE_NOT_IN_PINNED_CATALOGUE";
            return false;
        }
        var payload = MZP3RawProtobuf.TryParse(envelope.Payload, limits);
        if (!payload.Ok)
        {
            reason = $"PAYLOAD_{payload.Error}";
            return false;
        }
        decoded = new MZP4DecodedRequest(name, envelope.Code, envelope.Uid ?? 0,
            payload.Message!);
        reason = "OFFLINE_STRUCTURE_ONLY";
        return true;
    }

    /// <summary>
    /// Strictly ONE generic length-prefixed record (not a Dofus framing claim).
    /// Reject coalesced/trailing frames and oversized input to avoid ambiguity.
    /// </summary>
    public bool TryReadOneNeutralFrame(ReadOnlySpan<byte> prefixed,
        out MZP4DecodedRequest? decoded, out string reason,
        MZP3Limits? limits = null)
    {
        decoded = null;
        var budget = limits ?? MZP3Limits.Default;
        var frame = MZP3FrameCodec.TryReadOne(prefixed, budget.MaxMessageBytes, true);
        if (!frame.Complete)
        {
            reason = $"FRAME_{frame.Error}";
            return false;
        }
        return TryReadClientRequest(prefixed.Slice(frame.BodyOffset, frame.BodyLength),
            out decoded, out reason, budget);
    }
}

public sealed record MZP4DecodedRequest(string ClearName, string Code, long Uid,
    MZP3WireMessage Payload)
{
    public bool IsRuntimeAuthenticated => false;
    public bool ServerAckCertified => false;
}

/// <summary>
/// Explicitly typed wire writer with names and field numbers from a pinned
/// catalogue. fields.json supplies numbers, NOT types; callers choose types.
/// </summary>
public sealed class MZP4NamedPayloadBuilder
{
    private readonly MZP3MessageBuilder _wire = new();
    internal MZP4ProtocolAdapter Owner { get; }
    public string ClearName { get; }

    internal MZP4NamedPayloadBuilder(MZP4ProtocolAdapter owner, string clearName)
    {
        Owner = owner;
        ClearName = clearName;
    }

    private int Field(string name) => Owner.RequireField(ClearName, name);

    public MZP4NamedPayloadBuilder Int64(string field, long value)
    { _wire.AddSignedInt64(Field(field), value); return this; }

    public MZP4NamedPayloadBuilder Bool(string field, bool value)
    { _wire.AddVarint(Field(field), value ? 1UL : 0UL); return this; }

    public MZP4NamedPayloadBuilder Utf8(string field, string value)
    { _wire.AddUtf8(Field(field), value); return this; }

    public MZP4NamedPayloadBuilder Bytes(string field, ReadOnlySpan<byte> value)
    { _wire.AddBytes(Field(field), value); return this; }

    public MZP4NamedPayloadBuilder Fixed32(string field, uint value)
    { _wire.AddFixed32(Field(field), value); return this; }

    public MZP4NamedPayloadBuilder Fixed64(string field, ulong value)
    { _wire.AddFixed64(Field(field), value); return this; }

    public MZP4NamedPayloadBuilder RepeatedInt64(string field, IEnumerable<long> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        int tag = Field(field);
        foreach (var value in values) _wire.AddSignedInt64(tag, value);
        return this;
    }

    public MZP4NamedPayloadBuilder PreserveUnknown(MZP3WireField field)
    { _wire.AddRaw(field); return this; }

    public MZP3WireMessage Build() => _wire.Build();
}
