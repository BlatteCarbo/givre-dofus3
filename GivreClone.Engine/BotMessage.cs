using GivreCore.Mitm;
using GivreCore.Protobuf;

namespace GivreClone.Engine;

/// <summary>
/// Un message decode et resolu au niveau bot : code obfusque + nom clair (si mappe) + payload decode.
/// C'est l'unite que le PacketBus diffuse et sur laquelle les comportements attendent/reagissent.
/// </summary>
public sealed record BotMessage(
    long Seq,
    Direction Direction,
    int ConnectionId,
    string Code,
    string? Name,
    IReadOnlyList<ProtoField> Payload,
    byte[] RawFrame)
{
    public bool Is(string clearName) => string.Equals(Name, clearName, StringComparison.Ordinal);
    public bool IsFromServer => Direction == Direction.ServerToClient;
    public bool IsFromClient => Direction == Direction.ClientToServer;
}
