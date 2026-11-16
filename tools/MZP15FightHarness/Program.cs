using GivreCore.Mitm;
using GivreCore.Protobuf;
using GivreClone.Engine;
using GivreClone.Protocol;

var tests = 0;
void Check(bool v,string label)
{
    if (!v) throw new InvalidOperationException("MZP15 " + label);
    tests++;
}
const string fields = """{"FightStartingEvent":{"fight_id":3},"FightTurnEvent":{"character_id":6},"FightEndEvent":{"duration":2},"CharacterSelectionEvent":{"success":8},"Character":{"id":3,"character_basic_information":6},"CharacterBasicInformation":{"name":5}}""";
var catalog = ProtocolCatalog.LoadFromStrings(
    "message FightStartingEvent { int64 fight_id = 1; } message FightTurnEvent { int64 character_id = 1; } message FightEndEvent { int32 duration = 1; }",
    """{"type.ankama.com/start":"MessageType.FightStartingEvent","type.ankama.com/turn":"MessageType.FightTurnEvent","type.ankama.com/end":"MessageType.FightEndEvent","type.ankama.com/selected":"MessageType.CharacterSelectionEvent"}""",
    "{}", fields);
ProtoField Var(int id, ulong value) => new() {Field=id,Wire=WireType.Varint,Varint=value};
ProtoField Sub(int id,params ProtoField[] v) => new() {Field=id,Wire=WireType.Len,Message=v.ToList()};
ProtoField Text(int id,string value) => new() {Field=id,Wire=WireType.Len,String=value};
BotMessage Packet(string code,string name,Direction direction,params ProtoField[] p)
    => new(1,direction,12,code,name,p,[]);
BotMessage Start(ulong id) => Packet("start","FightStartingEvent",Direction.ServerToClient,Var(3,id));
BotMessage Turn(ulong id) => Packet("turn","FightTurnEvent",Direction.ServerToClient,Var(6,id));
BotMessage End() => Packet("end","FightEndEvent",Direction.ServerToClient);
bool Read(BotMessage b) => MZP15VerifiedFightReader.TryRead(b,catalog,out _);
Check(MZP15VerifiedFightReader.TryRead(Start(12),catalog,out var start) &&
      start is {Type:MZP15VerifiedFightReader.Transition.Start,FightId:12},"typed fight start");
Check(MZP15VerifiedFightReader.TryRead(Turn(22),catalog,out var turn) &&
      turn is {Type:MZP15VerifiedFightReader.Transition.Turn,ActorId:22},"typed turn owner");
Check(MZP15VerifiedFightReader.TryRead(End(),catalog,out var end) &&
      end is {Type:MZP15VerifiedFightReader.Transition.End},"typed end with absent proto3 defaults");
Check(!Read(Start(44) with {Direction=Direction.ClientToServer}),"outgoing denied");
Check(!Read(Start(44) with {Code="fake"}),"forged mapping denied");
Check(!Read(Packet("start","FightStartingEvent",Direction.ServerToClient,Var(1,42))),"beta tag denied");
Check(!Read(Start(0)),"zero fight id denied");
Check(!Read(Start(ulong.MaxValue)),"overflow id denied");
Check(!Read(Packet("turn","FightTurnEvent",Direction.ServerToClient,Var(6,7),Var(6,8))),
    "duplicate turn-owner denied");
Check(!Read(Packet("turn","FightTurnEvent",Direction.ServerToClient,
    new ProtoField {Field=6,Wire=WireType.Len,String="55"})),"wrong wire type denied");
Check(!MZP15VerifiedFightReader.TryRead(Start(55),
    ProtocolCatalog.LoadFromStrings(null,"""{"start":"FightStartingEvent"}""",null,"{}"),out _),
    "missing confirmed fields denies fight");
var state = new GameState();
Check(!state.FightKnown && !state.InFight && state.FightId is null,"unknown initial fight");
state.Apply(Start(44));
Check(!state.FightKnown,"unguarded legacy cannot start fight");
state.ApplyGuardedInbound(Turn(33),catalog);
Check(!state.FightKnown,"stray turn never creates a fight");
state.ApplyGuardedInbound(Start(44),catalog);
Check(state.FightKnown && state.InFight && state.FightId==44 && !state.IsMyTurn,
    "guarded start");
state.ApplyGuardedInbound(Turn(33),catalog);
Check(!state.IsMyTurn,"turn owner unknown to self cannot claim turn");
var selected = Packet("selected","CharacterSelectionEvent",Direction.ServerToClient,
    Sub(8,Sub(2,Var(3,33),Sub(6,Text(5,"Player")))));
state.ApplyGuardedInbound(selected,catalog);
// A new character identity resets stale fight. Re-enter with proven start.
state.ApplyGuardedInbound(Start(45),catalog);
state.ApplyGuardedInbound(Turn(31),catalog);
Check(!state.IsMyTurn,"other fighter turn is not my turn");
state.ApplyGuardedInbound(Turn(33),catalog);
Check(state.IsMyTurn,"my selected character's turn");
state.ApplyGuardedInbound(End(),catalog);
Check(state.FightKnown && !state.InFight && !state.IsMyTurn && state.FightId is null,
    "verified fight end cleans flags");
state.ApplyGuardedInbound(Start(46),catalog);
state.ClearVerifiedSession();
Check(!state.FightKnown && !state.InFight && state.FightId is null && !state.IsMyTurn,
    "revocation erases fight");
Console.WriteLine($"MZP15 guarded fight state PASS ({tests} offline checks). No server ACK or live combat.");
