using GivreClone.Protocol;
using GivreCore.Protobuf;

namespace GivreClone.Engine;

/// <summary>
/// Restricted fight state transitions from inbound, independently admitted,
/// version-mapped protocol messages. All field numbers come from fields.json,
/// never the beta .proto or the legacy Dofus 2 aliases. No fake turn/ACK.
/// </summary>
public static class MZP15VerifiedFightReader
{
    public enum Transition { Start, Turn, End }
    public sealed record FightUpdate(Transition Type, long? FightId, long? ActorId);

    public static bool TryRead(BotMessage inbound, ProtocolCatalog catalog, out FightUpdate? update)
    {
        update = null;
        ArgumentNullException.ThrowIfNull(inbound);
        ArgumentNullException.ThrowIfNull(catalog);
        if (!inbound.IsFromServer || string.IsNullOrWhiteSpace(inbound.Code) ||
            !string.Equals(catalog.NameForCode(inbound.Code), inbound.Name,
                StringComparison.Ordinal)) return false;

        switch (inbound.Name)
        {
            case "FightStartingEvent":
                if (!SinglePositive(inbound, catalog.FieldNumber("FightStartingEvent", "fight_id"),
                        out long fightId)) return false;
                update = new(Transition.Start, fightId, null);
                return true;
            case "FightTurnEvent":
                if (!SinglePositive(inbound, catalog.FieldNumber("FightTurnEvent", "character_id"),
                        out long actorId)) return false;
                update = new(Transition.Turn, null, actorId);
                return true;
            case "FightEndEvent":
                // Valid even if the server omits zero/default scalar duration
                // in proto3. The confirmed field's availability in the active
                // schema is necessary, but a server-authenticated event is the
                // authoritative end marker, not an arbitrary payload number.
                if (catalog.FieldNumber("FightEndEvent", "duration") is not > 0)
                    return false;
                update = new(Transition.End, null, null);
                return true;
            default:
                return false;
        }
    }

    private static bool SinglePositive(BotMessage packet, int? number, out long value)
    {
        value = 0;
        if (number is not > 0) return false;
        bool found = false;
        foreach (var field in packet.Payload)
        {
            if (field.Field != number.Value) continue;
            if (found || field.Wire != WireType.Varint ||
                field.Varint is 0 or > long.MaxValue) return false;
            value = (long)field.Varint;
            found = true;
        }
        return found;
    }
}
