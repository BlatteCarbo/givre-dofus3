using System.Text;
using GivreClone.Protocol;
using GivreCore.Protobuf;

namespace GivreClone.Engine;

/// <summary>
/// Narrow, typed CharacterSelectionEvent.success reader. Reads only verified
/// field IDs from an externally admitted mapping. The intermediate Success
/// wrapper has no unique name in old proto; accept a Character structure ONLY
/// when the fully mapped Character.id and BasicInformation.name occur together,
/// uniquely and within a bounded depth. Never infer ID from the first varint.
/// </summary>
public static class MZP14VerifiedCharacterReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public sealed record SelectedCharacter(long Id, string Name);

    public static bool TryRead(BotMessage inbound, ProtocolCatalog catalog,
        out SelectedCharacter? selected)
    {
        selected = null;
        ArgumentNullException.ThrowIfNull(inbound);
        ArgumentNullException.ThrowIfNull(catalog);
        if (!inbound.IsFromServer || inbound.Name != "CharacterSelectionEvent" ||
            string.IsNullOrWhiteSpace(inbound.Code) ||
            !string.Equals(catalog.NameForCode(inbound.Code), inbound.Name,
                StringComparison.Ordinal))
            return false;

        int? success = catalog.FieldNumber("CharacterSelectionEvent", "success");
        int? basic = catalog.FieldNumber("Character", "character_basic_information");
        int? id = catalog.FieldNumber("Character", "id");
        int? name = catalog.FieldNumber("CharacterBasicInformation", "name");
        if (success is not > 0 || basic is not > 0 || id is not > 0 || name is not > 0 ||
            inbound.Payload.Count > 256) return false;

        IReadOnlyList<ProtoField>? successBody = null;
        foreach (var field in inbound.Payload.Where(f => f.Field == success.Value))
        {
            if (successBody is not null || field.Wire != WireType.Len ||
                field.Message is null) return false;
            successBody = field.Message;
        }
        if (successBody is null) return false;

        var candidates = new List<SelectedCharacter>(2);
        var consumed = 0;
        // Depth 0-2 within the actual Success oneof, no guessed field IDs.
        Walk(successBody, depth: 0);
        if (candidates.Count != 1) return false;
        selected = candidates[0];
        return true;

        void Walk(IReadOnlyList<ProtoField> node, int depth)
        {
            if (consumed > 256 || candidates.Count > 1 || depth > 2) return;
            consumed += node.Count;
            if (consumed > 256) return;
            if (TryCharacter(node, basic.Value, id.Value, name.Value, out var character))
                candidates.Add(character!);
            if (depth == 2 || candidates.Count > 1) return;
            foreach (var item in node)
            {
                if (item.Wire != WireType.Len || item.Message is null) continue;
                Walk(item.Message, depth + 1);
                if (consumed > 256 || candidates.Count > 1) break;
            }
        }
    }

    private static bool TryCharacter(IReadOnlyList<ProtoField> node, int basicField,
        int idField, int nameField, out SelectedCharacter? selected)
    {
        selected = null;
        ProtoField? id = null;
        IReadOnlyList<ProtoField>? basic = null;
        foreach (var f in node)
        {
            if (f.Field == idField && f.Wire == WireType.Varint)
            {
                if (id is not null || f.Varint is 0 or > long.MaxValue) return false;
                id = f;
            }
            if (f.Field == basicField && f.Wire == WireType.Len && f.Message is not null)
            {
                if (basic is not null) return false;
                basic = f.Message;
            }
        }
        if (id is null || basic is null) return false;
        ProtoField? named = null;
        foreach (var f in basic.Where(f => f.Field == nameField))
        {
            if (named is not null || f.Wire != WireType.Len) return false;
            named = f;
        }
        if (named is null) return false;
        string value;
        try
        {
            value = named.Bytes is not null
                ? StrictUtf8.GetString(named.Bytes)
                : named.String ?? string.Empty;
        }
        catch (DecoderFallbackException) { return false; }
        if (value.Length is < 2 or > 40 ||
            !value.Any(char.IsLetter) ||
            value.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_' or '\'')))
            return false;
        selected = new SelectedCharacter((long)id.Varint, value);
        return true;
    }
}
