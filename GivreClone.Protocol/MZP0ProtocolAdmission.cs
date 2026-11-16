using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace GivreClone.Protocol;

/// <summary>
/// Offline, transport-independent admission review for candidate mapping files.
/// A favorable review NEVER attests a client process, a socket, or a server ACK.
/// The caller must separately prove origin, exact client identity and live transport.
/// </summary>
public static class MZP0ProtocolAdmission
{
    // Historical comparison build. Version-specific aliases/tags must come from
    // the SHA-pinned catalogue rather than requiring this old exact patch.
    public const string TargetVersion = "3.7.10.10";

    /// <summary>Accept structurally valid 3.7.x.y version labels as candidates.
    /// This does not attest identical schemas, live transport or a server ACK.</summary>
    public static bool Supports37Family(string? version)
    {
        if (version is null || version.Length is < 7 or > 32) return false;
        var parts = version.Split('.');
        if (parts.Length != 4 || parts[0] != "3" || parts[1] != "7") return false;
        foreach (var part in parts)
        {
            if (part.Length is < 1 or > 9 || part.Length > 1 && part[0] == '0')
                return false;
            foreach (var c in part)
                if (!char.IsAsciiDigit(c)) return false;
        }
        return true;
    }
    private const int MaxCatalogueBytes = 4 * 1024 * 1024;
    private const int MaxMessages = 10000;
    private const int MaxFields = 300000;
    private const string TypePrefix = "type.ankama.com/";
    private const string NamePrefix = "MessageType.";

    public static MZP0MappingReview Review(
        ReadOnlySpan<byte> matchingUtf8,
        ReadOnlySpan<byte> fieldsUtf8,
        string clientVersion,
        string expectedMatchingSha256,
        string expectedFieldsSha256,
        IReadOnlyCollection<MZP0RequiredField> required)
    {
        if (!Supports37Family(clientVersion))
            return Rejected("VERSION_UNVERIFIED");
        if (matchingUtf8.IsEmpty || fieldsUtf8.IsEmpty ||
            matchingUtf8.Length > MaxCatalogueBytes || fieldsUtf8.Length > MaxCatalogueBytes)
            return Rejected("CATALOGUE_MISSING_OR_OVERSIZED");
        if (!MatchesPin(matchingUtf8, expectedMatchingSha256) ||
            !MatchesPin(fieldsUtf8, expectedFieldsSha256))
            return Rejected("SHA256_PIN_MISMATCH");
        if (required is null || required.Count == 0 || required.Count > MaxFields)
            return Rejected("REQUIRED_MANIFEST_MISSING");

        try
        {
            using var aliases = JsonDocument.Parse(matchingUtf8.ToArray(),
                new JsonDocumentOptions { MaxDepth = 6, AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow });
            using var fieldDoc = JsonDocument.Parse(fieldsUtf8.ToArray(),
                new JsonDocumentOptions { MaxDepth = 6, AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow });
            if (aliases.RootElement.ValueKind != JsonValueKind.Object ||
                fieldDoc.RootElement.ValueKind != JsonValueKind.Object)
                return Rejected("CATALOGUE_SHAPE_INVALID");

            var codes = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in aliases.RootElement.EnumerateObject())
            {
                if (!pair.Name.StartsWith(TypePrefix, StringComparison.Ordinal) ||
                    !ValidId(pair.Name[TypePrefix.Length..]) ||
                    pair.Value.ValueKind != JsonValueKind.String)
                    return Rejected("ALIAS_SHAPE_INVALID");
                var mapped = pair.Value.GetString()!;
                if (!mapped.StartsWith(NamePrefix, StringComparison.Ordinal) ||
                    !ValidId(mapped[NamePrefix.Length..]))
                    return Rejected("ALIAS_SHAPE_INVALID");
                if (!codes.Add(pair.Name) || !names.Add(mapped))
                    return Rejected("DUPLICATE_OR_CONFLICTING_ALIAS");
                if (codes.Count > MaxMessages) return Rejected("ALIAS_BUDGET_EXCEEDED");
            }
            if (codes.Count == 0) return Rejected("NO_ALIASES");

            var fields = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            var collisions = new List<MZP0AmbiguousTag>();
            var fieldCount = 0;
            foreach (var message in fieldDoc.RootElement.EnumerateObject())
            {
                if (!ValidId(message.Name) || message.Value.ValueKind != JsonValueKind.Object ||
                    fields.ContainsKey(message.Name))
                    return Rejected("FIELD_MESSAGE_INVALID_OR_DUPLICATE");
                var byName = new Dictionary<string, int>(StringComparer.Ordinal);
                var byTag = new Dictionary<int, List<string>>();
                foreach (var field in message.Value.EnumerateObject())
                {
                    if (!ValidId(field.Name) ||
                        field.Value.ValueKind != JsonValueKind.Number ||
                        !field.Value.TryGetInt32(out var tag) ||
                        tag < 1 || tag > 536870911 || tag is >= 19000 and <= 19999 ||
                        !byName.TryAdd(field.Name, tag))
                        return Rejected("FIELD_TAG_INVALID_OR_DUPLICATE");
                    if (!byTag.TryGetValue(tag, out var candidates))
                        byTag[tag] = candidates = new List<string>();
                    candidates.Add(field.Name);
                    if (++fieldCount > MaxFields) return Rejected("FIELD_BUDGET_EXCEEDED");
                }
                foreach (var item in byTag.Where(x => x.Value.Count > 1))
                    collisions.Add(new MZP0AmbiguousTag(message.Name, item.Key,
                        item.Value.OrderBy(x => x, StringComparer.Ordinal).ToArray()));
                fields.Add(message.Name, byName);
                if (fields.Count > MaxMessages) return Rejected("MESSAGE_BUDGET_EXCEEDED");
            }
            if (fields.Count == 0) return Rejected("NO_FIELDS");

            var missing = new List<MZP0RequiredField>();
            var requested = new HashSet<MZP0RequiredField>();
            foreach (var item in required)
            {
                if (item is null || !ValidId(item.Message) || !ValidId(item.Field) ||
                    !requested.Add(item))
                    return Rejected("REQUIRED_MANIFEST_INVALID_OR_DUPLICATE");
                if (!fields.TryGetValue(item.Message, out var known) ||
                    !known.ContainsKey(item.Field))
                    missing.Add(item);
            }

            // Do not silently pick the last name associated with a numeric tag.
            // Preserve all candidate names for diagnostics/review instead.
            return new MZP0MappingReview(
                missing.Count == 0 && collisions.Count == 0
                    ? MZP0MappingVerdict.OfflineCandidateOnly
                    : MZP0MappingVerdict.RequiresReview,
                missing.Count > 0 ? "REQUIRED_FIELDS_MISSING"
                    : collisions.Count > 0 ? "AMBIGUOUS_TAGS" : "CANDIDATE_NOT_LIVE_ATTESTED",
                codes.Count, fields.Count, fieldCount,
                missing.ToArray(), collisions.ToArray());
        }
        catch (JsonException) { return Rejected("MALFORMED_JSON"); }
        catch (ArgumentException) { return Rejected("INVALID_CATALOGUE"); }
    }

    private static MZP0MappingReview Rejected(string reason) =>
        new(MZP0MappingVerdict.Blocked, reason, 0, 0, 0,
            Array.Empty<MZP0RequiredField>(), Array.Empty<MZP0AmbiguousTag>());

    private static bool MatchesPin(ReadOnlySpan<byte> bytes, string hex)
    {
        if (hex is null || hex.Length != 64 || !hex.All(Uri.IsHexDigit))
            return false;
        byte[] expected;
        try { expected = Convert.FromHexString(hex); }
        catch (FormatException) { return false; }
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(bytes), expected);
    }

    private static bool ValidId(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 256 ||
            !char.IsAsciiLetter(value[0])) return false;
        foreach (char c in value.AsSpan(1))
            if (!char.IsAsciiLetterOrDigit(c) && c != '_') return false;
        return true;
    }
}

public enum MZP0MappingVerdict
{
    Blocked,
    RequiresReview,
    OfflineCandidateOnly
}

public sealed record MZP0RequiredField(string Message, string Field);

public sealed record MZP0AmbiguousTag(
    string Message, int Tag, IReadOnlyList<string> CandidateFieldNames);

public sealed record MZP0MappingReview(
    MZP0MappingVerdict Verdict,
    string Reason,
    int AliasCount,
    int TypeCount,
    int FieldCount,
    IReadOnlyList<MZP0RequiredField> MissingRequired,
    IReadOnlyList<MZP0AmbiguousTag> AmbiguousTags)
{
    // Explicitly NOT a runtime admission token: there is no true authorization state here.
    public bool PermitsGameplay => false;
}
