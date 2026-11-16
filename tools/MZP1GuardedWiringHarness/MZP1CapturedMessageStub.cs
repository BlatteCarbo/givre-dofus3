// Offline type only, used to compile the actual BotSession and MZP1 ingress seam.
// The harness never opens a socket or launches/injects any client.
using GivreCore.Protobuf;

namespace GivreCore.Mitm;

public sealed record CapturedMessage(
    long Seq,
    Direction Direction,
    int ConnectionId,
    uint ProcessId,
    string Code,
    IReadOnlyList<ProtoField> Payload,
    byte[] RawFrame);
