using System.Security.Cryptography;
using System.Text;

namespace GivreClone.Protocol;

/// <summary>
/// Explicit wire-level declarations, NOT types inferred from fields.json.
/// Proto3 int32/int64 are two's complement varints, never zig-zag sint32/64.
/// </summary>
public enum MZP7ValueKind
{
    Int32, Int64, UInt32, UInt64, Boolean, Utf8String, Bytes,
    Fixed32, Fixed64, EmbeddedMessage
}

/// <summary>
/// A caller-supplied, externally reviewed proto field declaration. MZP4
/// fields.json has tag NUMBERS only and is insufficient to infer these types.
/// </summary>
public sealed record MZP7FieldDeclaration(
    string Name, MZP7ValueKind Kind, bool Repeated = false,
    bool PackedAllowed = false, string? EmbeddedTypeName = null,
    bool Required = false);

public sealed record MZP7MessageDeclaration(
    string Name, IReadOnlyList<MZP7FieldDeclaration> Fields);

/// <summary>
/// Separate source reference; digest equality is not proof that any version,
/// typed manifest, client or server is authentic.
/// </summary>
public sealed record MZP7TypeSource(
    string ClientVersion, string MatchingSha256, string FieldsSha256,
    string ManifestSha256, string SourceLabel);

public enum MZP7TypedStatus
{
    ShapeAcceptedOnly, MissingOrUnrecognizedMessage, AliasMismatch,
    InvalidWireMessage, DuplicateSingular, RequiredFieldMissing,
    WireKindMismatch, InvalidScalar, InvalidUtf8, PackedVarintMalformed,
    NestedMessageMalformed, BudgetExceeded
}

/// <summary>Typed value with exactly one active kind. Bytes return a copy.</summary>
public sealed class MZP7TypedValue
{
    private readonly byte[]? _bytes;

    internal MZP7TypedValue(MZP7ValueKind kind, long? signed = null,
        ulong? unsigned = null, bool? boolean = null,
        string? text = null, byte[]? bytes = null,
        MZP7TypedMessage? nested = null)
    {
        Kind = kind;
        Signed = signed;
        Unsigned = unsigned;
        Boolean = boolean;
        Text = text;
        _bytes = bytes is null ? null : (byte[])bytes.Clone();
        Nested = nested;
    }

    public MZP7ValueKind Kind { get; }
    public long? Signed { get; }
    public ulong? Unsigned { get; }
    public bool? Boolean { get; }
    public string? Text { get; }
    public MZP7TypedMessage? Nested { get; }
    public byte[]? CopyBytes() => _bytes is null ? null : (byte[])_bytes.Clone();
}

/// <summary>
/// Typed view over a LOSSLESS original MZP3 wire message. Values are private
/// application data; never put this object into UI/diagnostic logs.
/// </summary>
public sealed class MZP7TypedMessage
{
    private readonly Dictionary<string, IReadOnlyList<MZP7TypedValue>> _values;
    private readonly MZP3WireMessage _original;
    internal MZP7TypedMessage(string name,
        Dictionary<string, IReadOnlyList<MZP7TypedValue>> values,
        MZP3WireMessage original, int unknownFieldCount)
    {
        MessageName = name;
        _values = values;
        _original = original;
        UnknownFieldCount = unknownFieldCount;
    }
    public string MessageName { get; }
    public int UnknownFieldCount { get; }
    public IReadOnlyList<MZP7TypedValue> Values(string fieldName) =>
        _values.TryGetValue(fieldName, out var values)
            ? values : Array.Empty<MZP7TypedValue>();
    public MZP7TypedValue? Single(string fieldName)
    {
        var values = Values(fieldName);
        return values.Count == 1 ? values[0] : null;
    }
    public byte[] EncodeLossless(MZP3Limits? limits = null) => _original.Encode(limits);
    public bool IsAuthenticated => false;
    public bool ServerAckCertified => false;
}

public sealed record MZP7TypedResult(
    MZP7TypedStatus Status, MZP7TypedMessage? Message)
{
    public bool Accepted => Status == MZP7TypedStatus.ShapeAcceptedOnly && Message is not null;
    public bool TypeSourceAuthenticated => false;
    public bool ClientAuthenticated => false;
    public bool ServerAckCertified => false;
}

public sealed record MZP7TypedOpenResult(string Reason, MZP7TypedCodec? Codec)
{
    public bool IsOfflineCandidate => Codec is not null;
    public bool TypesIndependentlyAuthenticated => false;
    public bool MayInject => false;
}

/// <summary>
/// Fully typed, source-only adapter over *real* MZP3/4/5. It cannot replace
/// GivreCore.Protobuf and deliberately declares NO types in that namespace.
/// </summary>
public sealed class MZP7TypedCodec
{
    private sealed record ResolvedField(MZP7FieldDeclaration Declaration, int Number);
    private sealed class ResolvedMessage
    {
        public required string Name;
        public required Dictionary<int, ResolvedField> ByNumber;
        public required ResolvedField[] AllFields;
    }

    private readonly MZP4ProtocolAdapter _catalogue;
    private readonly Dictionary<string, ResolvedMessage> _schemas;
    private readonly MZP3Limits _limits;

    private MZP7TypedCodec(MZP4ProtocolAdapter catalogue,
        Dictionary<string, ResolvedMessage> schemas, MZP3Limits limits)
    {
        _catalogue = catalogue;
        _schemas = schemas;
        _limits = limits;
    }

    /// <summary>
    /// Deterministic, order-independent digest of EXPLICIT type declarations,
    /// for verification against an independently supplied expected digest.
    /// Self-pinning cannot prove schema type accuracy or client authenticity.
    /// </summary>
    public static string ComputeManifestSha256(IReadOnlyList<MZP7MessageDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        var manifest = new StringBuilder("MZP7-TYPE-DECLARATIONS-V1\n");
        foreach (var message in declarations.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            manifest.Append("M:").Append(message.Name).Append('\n');
            foreach (var field in message.Fields.OrderBy(x => x.Name, StringComparer.Ordinal))
                manifest.Append("F:").Append(field.Name).Append('|')
                    .Append((int)field.Kind).Append('|')
                    .Append(field.Repeated ? '1' : '0').Append('|')
                    .Append(field.PackedAllowed ? '1' : '0').Append('|')
                    .Append(field.Required ? '1' : '0').Append('|')
                    .Append(field.EmbeddedTypeName ?? "").Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest.ToString())));
    }

    /// <summary>
    /// Validate that the explicitly declared type manifest refers to fields
    /// actually present in the admitted MZP4 catalogue; never guess types.
    /// </summary>
    public static MZP7TypedOpenResult Open(
        MZP4ProtocolAdapter? catalogue, MZP7TypeSource? source,
        IReadOnlyList<MZP7MessageDeclaration>? declarations,
        MZP3Limits? limits = null)
    {
        if (catalogue is null || source is null ||
            !MZP0ProtocolAdmission.Supports37Family(source.ClientVersion) ||
            !MZP0ProtocolAdmission.Supports37Family(catalogue.Reference.ClientVersion) ||
            !string.Equals(catalogue.Reference.MatchingSha256, source.MatchingSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(catalogue.Reference.FieldsSha256, source.FieldsSha256,
                StringComparison.OrdinalIgnoreCase))
            return new("CATALOGUE_VERSION_OR_PINS_MISSING", null);
        if (!ValidSourceLabel(source.SourceLabel) || declarations is null ||
            declarations.Count is < 1 or > 128)
            return new("DECLARATIONS_MISSING", null);
        var budget = limits ?? MZP3Limits.Default;
        if (!budget.IsValid) return new("LIMITS_INVALID", null);

        try
        {
            // Check sizes and names FIRST. In particular, do not hash a hostile,
            // excessively large manifest before applying its field-count budget.
            if (!ValidSha(source.ManifestSha256))
                return new("TYPE_MANIFEST_DIGEST_MISMATCH", null);
            var resolved = new Dictionary<string, ResolvedMessage>(StringComparer.Ordinal);
            int count = 0;
            foreach (var message in declarations)
            {
                if (message is null || !ValidName(message.Name, 128) ||
                    message.Fields is null || message.Fields.Count is < 1 or > 256 ||
                    resolved.ContainsKey(message.Name))
                    return new("INVALID_MESSAGE_DECLARATION", null);
                var byNumber = new Dictionary<int, ResolvedField>();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var field in message.Fields)
                {
                    if (field is null || !ValidName(field.Name, 128) ||
                        !Enum.IsDefined(field.Kind) || !names.Add(field.Name) ||
                        field.Required && field.Repeated ||
                        field.PackedAllowed && (!field.Repeated ||
                            field.Kind is not (MZP7ValueKind.Int32 or
                                MZP7ValueKind.Int64 or MZP7ValueKind.UInt32 or
                                MZP7ValueKind.UInt64 or MZP7ValueKind.Boolean)) ||
                        (field.Kind == MZP7ValueKind.EmbeddedMessage)
                            != (field.EmbeddedTypeName is not null) ||
                        field.EmbeddedTypeName is not null &&
                            !ValidName(field.EmbeddedTypeName, 128))
                        return new("INVALID_FIELD_DECLARATION", null);
                    var number = catalogue.FieldNumber(message.Name, field.Name);
                    if (number is null ||
                        !byNumber.TryAdd(number.Value, new ResolvedField(field, number.Value)))
                        return new("MISSING_OR_AMBIGUOUS_FIELD_TAG", null);
                    if (++count > 4096) return new("DECLARATION_BUDGET_EXCEEDED", null);
                }
                resolved.Add(message.Name, new ResolvedMessage
                {
                    Name = message.Name,
                    ByNumber = byNumber,
                    AllFields = byNumber.Values.ToArray()
                });
            }
            // A typed EmbeddedMessage is ONLY valid if its referenced message
            // is fully declared and its tag names exist in the same catalogue.
            foreach (var message in resolved.Values)
                foreach (var field in message.AllFields)
                    if (field.Declaration.EmbeddedTypeName is { } nested &&
                        !resolved.ContainsKey(nested))
                        return new("NESTED_SCHEMA_MISSING", null);

            if (!string.Equals(ComputeManifestSha256(declarations),
                source.ManifestSha256, StringComparison.OrdinalIgnoreCase))
                return new("TYPE_MANIFEST_DIGEST_MISMATCH", null);

            return new("OFFLINE_TYPES_DECLARED_NOT_VERIFIED",
                new MZP7TypedCodec(catalogue, resolved, budget));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or
            NullReferenceException)
        {
            return new("TYPE_MANIFEST_INVALID", null);
        }
    }

    /// <summary>Parse wire and validate typed fields against the supplied manifest.</summary>
    public MZP7TypedResult ReadUnframedPayload(
        string? messageName, ReadOnlySpan<byte> bytes)
    {
        if (messageName is null || !_schemas.ContainsKey(messageName))
            return Deny(MZP7TypedStatus.MissingOrUnrecognizedMessage);
        var wire = MZP3RawProtobuf.TryParse(bytes, _limits);
        if (!wire.Ok || wire.Message is null)
            return Deny(wire.Error is
                MZP3WireError.TooLarge or MZP3WireError.FieldTooLarge or
                MZP3WireError.TooManyFields or MZP3WireError.DepthExceeded or
                MZP3WireError.InvalidLimits
                    ? MZP7TypedStatus.BudgetExceeded
                    : MZP7TypedStatus.InvalidWireMessage);
        return ReadWire(messageName, wire.Message);
    }

    public MZP7TypedResult ReadWire(string? messageName, MZP3WireMessage? wire)
    {
        if (wire is null || messageName is null || !_schemas.ContainsKey(messageName))
            return Deny(MZP7TypedStatus.MissingOrUnrecognizedMessage);
        // Enforce parser budgets even if caller has fabricated a wire object via
        // MZP3MessageBuilder, not only for bytes from MZP3RawProtobuf.TryParse.
        try
        {
            var bytes = wire.Encode(_limits);
            var normalized = MZP3RawProtobuf.TryParse(bytes, _limits);
            if (!normalized.Ok || normalized.Message is null)
                return Deny(MZP7TypedStatus.InvalidWireMessage);
            int budget = Math.Min(_limits.MaxFields * 2, 8192);
            return ReadNested(_schemas[messageName], normalized.Message, 0, ref budget);
        }
        catch (MZP3FormatException) { return Deny(MZP7TypedStatus.BudgetExceeded); }
        catch (ArgumentException) { return Deny(MZP7TypedStatus.BudgetExceeded); }
    }

    /// <summary>
    /// Guard caller-built MZP4DecodedRequest against mismatched alias/catalogue.
    /// A public record alone is NOT a provenance authority.
    /// </summary>
    public MZP7TypedResult FromMZP4(MZP4DecodedRequest? request)
    {
        if (request is null || request.ClearName is null || request.Code is null)
            return Deny(MZP7TypedStatus.MissingOrUnrecognizedMessage);
        if (!string.Equals(_catalogue.CodeForName(request.ClearName), request.Code,
                StringComparison.Ordinal))
            return Deny(MZP7TypedStatus.AliasMismatch);
        return ReadWire(request.ClearName, request.Payload);
    }

    /// <summary>
    /// MZP5 direction, provenance, sequence and any server "Response" remain
    /// declared-only. No conversion establishes an ACK.
    /// </summary>
    public MZP7TypedResult FromMZP5(MZP5DecodedPacket? packet)
    {
        if (packet?.ClearName is null)
            return Deny(MZP7TypedStatus.MissingOrUnrecognizedMessage);
        if (!string.Equals(_catalogue.CodeForName(packet.ClearName), packet.Code,
                StringComparison.Ordinal))
            return Deny(MZP7TypedStatus.AliasMismatch);
        return ReadWire(packet.ClearName, packet.Payload);
    }

    private MZP7TypedResult ReadNested(ResolvedMessage schema,
        MZP3WireMessage message, int depth, ref int budget)
    {
        if (depth > _limits.MaxDepth) return Deny(MZP7TypedStatus.BudgetExceeded);
        var values = new Dictionary<string, List<MZP7TypedValue>>(StringComparer.Ordinal);
        int unknown = 0;
        foreach (var field in message.Fields)
        {
            if (--budget < 0) return Deny(MZP7TypedStatus.BudgetExceeded);
            if (!schema.ByNumber.TryGetValue(field.Number, out var spec))
            {
                unknown++;
                continue;
            }
            var declaration = spec.Declaration;
            if (!values.TryGetValue(declaration.Name, out var list))
                values.Add(declaration.Name, list = new List<MZP7TypedValue>());
            if (!declaration.Repeated && list.Count != 0)
                return Deny(MZP7TypedStatus.DuplicateSingular);
            if (declaration.PackedAllowed && field.WireType == MZP3WireType.LengthDelimited)
            {
                byte[] contents = field.GetData();
                var offset = 0;
                while (offset < contents.Length)
                {
                    if (--budget < 0) return Deny(MZP7TypedStatus.BudgetExceeded);
                    var read = MZP3RawProtobuf.ReadVarint(contents, ref offset, out var number);
                    if (read != MZP3WireError.None)
                        return Deny(MZP7TypedStatus.PackedVarintMalformed);
                    var packed = ConvertVarint(declaration.Kind, number);
                    if (packed is null) return Deny(MZP7TypedStatus.InvalidScalar);
                    list.Add(packed);
                }
                continue;
            }
            MZP7TypedValue? item = null;
            switch (declaration.Kind)
            {
                case MZP7ValueKind.Int32:
                case MZP7ValueKind.Int64:
                case MZP7ValueKind.UInt32:
                case MZP7ValueKind.UInt64:
                case MZP7ValueKind.Boolean:
                    if (field.WireType != MZP3WireType.Varint)
                        return Deny(MZP7TypedStatus.WireKindMismatch);
                    item = ConvertVarint(declaration.Kind, field.UnsignedValue!.Value);
                    if (item is null) return Deny(MZP7TypedStatus.InvalidScalar);
                    break;
                case MZP7ValueKind.Fixed32:
                    if (field.WireType != MZP3WireType.Fixed32)
                        return Deny(MZP7TypedStatus.WireKindMismatch);
                    item = new MZP7TypedValue(declaration.Kind, unsigned: field.Fixed32!.Value);
                    break;
                case MZP7ValueKind.Fixed64:
                    if (field.WireType != MZP3WireType.Fixed64)
                        return Deny(MZP7TypedStatus.WireKindMismatch);
                    item = new MZP7TypedValue(declaration.Kind, unsigned: field.Fixed64!.Value);
                    break;
                case MZP7ValueKind.Utf8String:
                    if (field.WireType != MZP3WireType.LengthDelimited)
                        return Deny(MZP7TypedStatus.WireKindMismatch);
                    if (!field.TryGetStrictUtf8(out var text))
                        return Deny(MZP7TypedStatus.InvalidUtf8);
                    item = new MZP7TypedValue(declaration.Kind, text: text);
                    break;
                case MZP7ValueKind.Bytes:
                    if (field.WireType != MZP3WireType.LengthDelimited)
                        return Deny(MZP7TypedStatus.WireKindMismatch);
                    item = new MZP7TypedValue(declaration.Kind, bytes: field.GetData());
                    break;
                case MZP7ValueKind.EmbeddedMessage:
                    if (field.WireType != MZP3WireType.LengthDelimited)
                        return Deny(MZP7TypedStatus.WireKindMismatch);
                    var embedded = field.ParseEmbedded(_limits, depth + 1);
                    if (!embedded.Ok || embedded.Message is null)
                        return Deny(MZP7TypedStatus.NestedMessageMalformed);
                    var nested = ReadNested(_schemas[declaration.EmbeddedTypeName!],
                        embedded.Message, depth + 1, ref budget);
                    if (!nested.Accepted || nested.Message is null) return nested;
                    item = new MZP7TypedValue(declaration.Kind, nested: nested.Message);
                    break;
            }
            if (item is null) return Deny(MZP7TypedStatus.InvalidScalar);
            list.Add(item);
        }
        foreach (var field in schema.AllFields)
        {
            var decl = field.Declaration;
            if (decl.Required &&
                (!values.TryGetValue(decl.Name, out var v) || v.Count != 1))
                return Deny(MZP7TypedStatus.RequiredFieldMissing);
        }
        var result = new Dictionary<string, IReadOnlyList<MZP7TypedValue>>(StringComparer.Ordinal);
        foreach (var pair in values)
            result.Add(pair.Key, Array.AsReadOnly(pair.Value.ToArray()));
        return new(MZP7TypedStatus.ShapeAcceptedOnly,
            new MZP7TypedMessage(schema.Name, result, message, unknown));
    }

    private static MZP7TypedValue? ConvertVarint(MZP7ValueKind kind, ulong raw)
    {
        switch (kind)
        {
            case MZP7ValueKind.Int64:
                return new(kind, signed: unchecked((long)raw));
            case MZP7ValueKind.Int32:
                // Protobuf int32 negative values are sign-extended to 64 bits;
                // a truncated low 32-bit negative is not canonical int32.
                long signed = unchecked((long)raw);
                return signed is >= int.MinValue and <= int.MaxValue
                    ? new(kind, signed: signed) : null;
            case MZP7ValueKind.UInt64:
                return new(kind, unsigned: raw);
            case MZP7ValueKind.UInt32:
                return raw <= uint.MaxValue ? new(kind, unsigned: raw) : null;
            case MZP7ValueKind.Boolean:
                return raw is 0 or 1 ? new(kind, boolean: raw == 1) : null;
            default: return null;
        }
    }

    private static bool ValidName(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length > max ||
            !(char.IsAsciiLetter(value[0]) || value[0] == '_')) return false;
        return value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    }

    private static bool ValidSourceLabel(string? value) =>
        value is { Length: >= 1 and <= 80 } &&
        char.IsAsciiLetterOrDigit(value[0]) &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    private static bool ValidSha(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static MZP7TypedResult Deny(MZP7TypedStatus code) => new(code, null);
}
