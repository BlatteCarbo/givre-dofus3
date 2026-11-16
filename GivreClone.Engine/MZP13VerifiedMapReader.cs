using GivreClone.Protocol;
using GivreCore.Protobuf;

namespace GivreClone.Engine;

/// <summary>
/// Read-only projection from independently admitted SERVER INBOUND events.
/// It uses only the *wire-confirmed* fields.json mapping, never the old
/// clear_game_2025.proto field numbers, guesses from string bytes, or code
/// names inferred from payload content. This does NOT certify an ACK.
/// </summary>
public static class MZP13VerifiedMapReader
{
    public static bool TryRead(BotMessage message, ProtocolCatalog catalog, out long mapId)
    {
        mapId = 0;
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(catalog);
        if (!message.IsFromServer || message.Name is not
            ("MapCurrentEvent" or "MapComplementaryInformationEvent"))
            return false;
        // The name must agree with the *currently accepted* code mapping.
        if (string.IsNullOrWhiteSpace(message.Code) ||
            !string.Equals(catalog.NameForCode(message.Code), message.Name,
                StringComparison.Ordinal))
            return false;
        int? fieldId = catalog.FieldNumber(message.Name, "map_id");
        if (fieldId is not > 0)
            return false;
        // Map IDs are positive protobuf integers. Repeated singular fields,
        // truncation or out-of-range numbers must not pollute the state.
        var found = false;
        foreach (var field in message.Payload)
        {
            if (field.Field != fieldId.Value) continue;
            if (found || field.Wire != WireType.Varint ||
                field.Varint is 0 or > long.MaxValue)
                return false;
            found = true;
            mapId = (long)field.Varint;
        }
        return found;
    }
}
