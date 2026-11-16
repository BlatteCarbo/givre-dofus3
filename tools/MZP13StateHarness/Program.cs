using GivreClone.Engine;
using GivreClone.Protocol;
using GivreCore.Mitm;
using GivreCore.Protobuf;

int count = 0;
void Check(bool condition, string title)
{
    if (!condition) throw new Exception("MZP13: " + title);
    count++;
}

var fields = """{"MapCurrentEvent":{"map_id":7},"MapComplementaryInformationEvent":{"map_id":4}}""";
ProtocolCatalog Catalog(string mapping, string? fieldJson = null)
    => ProtocolCatalog.LoadFromStrings(
        "syntax = \"proto3\"; message MapCurrentEvent { int64 map_id = 1; } message MapComplementaryInformationEvent { int64 map_id = 1; }",
        mapping, "{}", fieldJson ?? fields);
var catalog = Catalog("""{"type.ankama.com/testmap":"MessageType.MapCurrentEvent","type.ankama.com/testdetail":"MessageType.MapComplementaryInformationEvent"}""");
BotMessage Msg(string code, string? name, Direction dir, params ProtoField[] payload)
    => new(1, dir, 14, code, name, payload, Array.Empty<byte>());
ProtoField V(int id, ulong number) => new() { Field = id, Wire = WireType.Varint, Varint = number };
ProtoField S(int id, string value) => new() { Field = id, Wire = WireType.Len, String = value };
bool Read(BotMessage packet, ProtocolCatalog? selected = null)
    => MZP13VerifiedMapReader.TryRead(packet, selected ?? catalog, out _);

var valid = Msg("testmap", "MapCurrentEvent", Direction.ServerToClient, V(7, 191105026));
Check(MZP13VerifiedMapReader.TryRead(valid, catalog, out var id) && id == 191105026,
    "real catalog field number used instead of beta proto #1");
Check(!Read(Msg("testmap", "MapCurrentEvent", Direction.ClientToServer, V(7, 42))),
    "outgoing frames cannot modify server state");
Check(!Read(Msg("wrong", "MapCurrentEvent", Direction.ServerToClient, V(7, 42))),
    "false clear name cannot override code mapping");
Check(!Read(Msg("testmap", "AnyOtherEvent", Direction.ServerToClient, V(7, 42))),
    "different event cannot be promoted to map");
Check(!Read(Msg("testmap", "MapCurrentEvent", Direction.ServerToClient, V(1, 42))),
    "schema beta field #1 not accepted as current wire");
Check(!Read(Msg("testmap", "MapCurrentEvent", Direction.ServerToClient, S(7,"42"))),
    "string wire cannot masquerade as map_id");
Check(!Read(Msg("testmap", "MapCurrentEvent", Direction.ServerToClient, V(7,0))),
    "map zero rejected");
Check(!Read(Msg("testmap", "MapCurrentEvent", Direction.ServerToClient, V(7,ulong.MaxValue))),
    "map overflow rejected");
Check(!Read(Msg("testmap", "MapCurrentEvent", Direction.ServerToClient, V(7,50),V(7,51))),
    "duplicate singular map rejected");
Check(!Read(Msg("testmap", "MapCurrentEvent", Direction.ServerToClient, V(8,50))),
    "missing map field rejected");
Check(!Read(Msg("testmap", "MapCurrentEvent", Direction.ServerToClient, V(7,50)),
    Catalog("""{"type.ankama.com/testmap":"MessageType.MapCurrentEvent"}""", "{}")),
    "without confirmed wire field there is no inference");
Check(MZP13VerifiedMapReader.TryRead(
    Msg("testdetail","MapComplementaryInformationEvent",Direction.ServerToClient,V(4,512)),catalog,out id) && id==512,
    "complementary map requires its own independent field mapping");

var state = new GameState();
Check(!state.MapKnown && state.MapId is null && !state.FightKnown,
    "unknown initial game state remains unknown");
state.Apply(valid);
Check(!state.MapKnown && state.MapId is null,
    "unverified legacy Apply cannot certify map");
state.ApplyGuardedInbound(valid,catalog);
Check(state.MapKnown && state.MapId == 191105026 && state.CellId is null,
    "guarded inbound admits precisely mapped map ID");
state.SetPosition(191105026, 183);
state.ApplyGuardedInbound(valid,catalog);
Check(state.CellId == 183,"repeat same map retains verified cell");
state.ApplyGuardedInbound(
    Msg("testdetail","MapComplementaryInformationEvent",Direction.ServerToClient,V(4,191105027)),catalog);
Check(state.MapId == 191105027 && state.CellId is null,
    "new map revokes stale cell");
state.ApplyGuardedInbound(
    Msg("testmap","MapCurrentEvent",Direction.ClientToServer,V(7,9999)),catalog);
Check(state.MapId == 191105027,"untrusted outbound cannot mutate state");
state.ClearVerifiedSession();
Check(state.CharacterId is null && state.CharacterName is null && state.MapId is null &&
      state.CellId is null && !state.MapKnown && !state.CharacterKnown && !state.FightKnown &&
      !state.InFight && !state.IsMyTurn,
      "revocation fully clears character/map/fight info");
Console.WriteLine($"MZP13 verified map fold: PASS ({count} offline assertions). No live Dofus map, authentication or ACK.");
