// Test-only stand-ins for missing GivreCore types; no MITM client, socket or process.
// BotMessage.cs and PacketBus.cs are linked directly from the Engine source.
namespace GivreCore.Mitm
{
    public enum Direction { ClientToServer, ServerToClient }
}

namespace GivreCore.Protobuf
{
    public sealed class ProtoField;
}
