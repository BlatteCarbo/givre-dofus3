using GivreCore.Mitm;
using GivreCore.Protobuf;
using GivreClone.Engine;
using GivreClone.Protocol;

int checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception("MZP14: " + label);
    checks++;
}
const string schema = "message CharacterSelectionEvent { string success = 1; }";
const string names = """{"type.ankama.com/selected":"MessageType.CharacterSelectionEvent"}""";
const string fields = """{"CharacterSelectionEvent":{"success":8},"Character":{"id":3,"character_basic_information":6},"CharacterBasicInformation":{"name":5}}""";
var catalog = ProtocolCatalog.LoadFromStrings(schema,names,"{}",fields);
ProtoField Var(int num,ulong val) => new() { Field=num, Wire=WireType.Varint, Varint=val };
ProtoField Text(int num,string val) => new() { Field=num, Wire=WireType.Len, String=val };
ProtoField Sub(int num,params ProtoField[] values) => new() { Field=num, Wire=WireType.Len, Message=values.ToList() };
BotMessage Selected(ulong id,string name) => new(1,Direction.ServerToClient,12,"selected","CharacterSelectionEvent",
    [Sub(8,Sub(2,Var(3,id),Sub(6,Text(5,name))))],[]);
bool Read(BotMessage message) => MZP14VerifiedCharacterReader.TryRead(message,catalog,out _);
Check(MZP14VerifiedCharacterReader.TryRead(Selected(913412,"K'aos"),catalog,out var selected) &&
      selected is { Id: 913412, Name: "K'aos" },"mapped nested ID/name accepted");
Check(!Read(Selected(72,"Sram") with {Direction=Direction.ClientToServer}),"outbound denied");
Check(!Read(Selected(73,"Sram") with {Code="not-selected"}),"spoofed clear-name denied");
Check(!Read(new BotMessage(1,Direction.ServerToClient,12,"selected","CharacterSelectionEvent",
     [Sub(8,Sub(2,Var(1,1),Sub(6,Text(5,"Fake"))))],[])),"beta id ignored");
Check(!Read(Selected(0,"John")),"zero id denied");
Check(!Read(Selected(ulong.MaxValue,"John")),"overflow id denied");
Check(!Read(new BotMessage(1,Direction.ServerToClient,12,"selected","CharacterSelectionEvent",
     [Sub(8,Sub(2,Var(3,1),Sub(6,Text(5,"Alex"))),
             Sub(2,Var(3,2),Sub(6,Text(5,"Mia"))))],[])),"multiple candidates denied");
Check(!Read(new BotMessage(1,Direction.ServerToClient,12,"selected","CharacterSelectionEvent",
     [Sub(8,Sub(2,Var(3,1),Sub(6,Text(5,"Alex"),Text(5,"Mia"))))],[])),"duplicate name denied");
Check(!Read(Selected(44,"bad\nname")),"control-char name denied");
Check(!Read(Selected(51,"x")),"one letter denied");
Check(!MZP14VerifiedCharacterReader.TryRead(Selected(22,"Hero"),
      ProtocolCatalog.LoadFromStrings(schema,names,"{}","{}"),out _),
      "missing confirmed fields denied");
var state = new GameState();
Check(!state.CharacterKnown && state.CharacterId is null,"initial state unknown");
state.Apply(Selected(20,"Hero"));
Check(!state.CharacterKnown,"unguarded input ignored");
state.ApplyGuardedInbound(Selected(20,"Hero"),catalog);
Check(state.CharacterKnown && state.CharacterId==20 && state.CharacterName=="Hero","guarded selection admitted");
state.SetPosition(191105026,100);
state.ApplyGuardedInbound(Selected(20,"Hero"),catalog);
Check(state.CellId==100,"same character retains current map");
state.ApplyGuardedInbound(Selected(21,"Second"),catalog);
Check(state.CharacterId==21 && state.CellId is null && state.MapId is null &&
      !state.MapKnown && !state.FightKnown,"new character revokes old map");
state.ClearVerifiedSession();
Check(!state.CharacterKnown && state.CharacterId is null && state.CharacterName is null,
    "revocation clears character");
Console.WriteLine($"MZP14 typed character selection: PASS ({checks} OFFLINE checks). No live Dofus identity.");
