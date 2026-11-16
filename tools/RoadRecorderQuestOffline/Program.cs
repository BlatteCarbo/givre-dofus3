global using GivreClone.Protocol;
using System;
using System.Collections.Generic;
using GivreClone.App.Services;

namespace GivreClone.Protocol
{
    public sealed record DecodedPacket(uint Pid, bool ClientToServer, string Name,
        Dictionary<string, int>? Fields = null);
}

namespace GivreClone.App.Services
{
    public sealed class MapInteractive
    {
        public int ElementId { get; init; }
        public int ElementTypeId { get; init; }
        public int CellId { get; init; }
    }
    public sealed class Actor
    {
        public long ActorId { get; init; }
        public int NpcId { get; init; }
    }
    public sealed class WorldState
    {
        public List<MapInteractive> Interactives { get; } = new();
        public List<Actor> Actors { get; } = new();
    }
    public sealed class WorldStub
    {
        public WorldState Value { get; } = new();
        public WorldState Get(uint pid) => Value;
    }
    public static class Stores
    {
        public static WorldStub World { get; } = new();
    }
    public sealed class LaunchService
    {
        public event Action<uint, long, int?, int?>? MapChanged;
        public event Action<DecodedPacket>? PacketDecoded;
        private readonly Dictionary<long, (int X, int Y)> _coords = new();
        public long Map { get; private set; } = 100;
        public bool IsInFight(uint pid) => false;
        public (int X, int Y)? MapCoordOf(long map) => _coords.TryGetValue(map, out var value) ? value : null;
        public int PacketInt(DecodedPacket packet, string field, int def = -1)
            => packet.Fields?.GetValueOrDefault(field, def) ?? def;
        public int NpcReplyIndexOf(uint pid, int rid) => rid switch { 101 => 1, 102 => 2, 201 => 1, _ => 0 };
        public bool IsGatherResource(MapInteractive x) => x.ElementTypeId == 38;
        public void Coord(long id, int x, int y) => _coords[id] = (x, y);
        public void Packet(uint pid, bool outgoing, string name, params (string Key, int Value)[] fields)
        {
            var d = new Dictionary<string, int>();
            foreach (var (key, value) in fields) d[key] = value;
            PacketDecoded?.Invoke(new DecodedPacket(pid, outgoing, name, d));
        }
        public void Change(uint pid, long id)
        {
            Map = id;
            var xy = MapCoordOf(id);
            MapChanged?.Invoke(pid, id, xy?.X, xy?.Y);
        }
    }
}

public static class Program
{
    private static void Expect(bool condition, string label)
    {
        if (!condition) throw new Exception("ROAD_NATIVE_FAIL: " + label);
    }

    public static void Main()
    {
        var launch = new LaunchService();
        launch.Coord(100, 0, 0);
        launch.Coord(101, 1, 0);
        launch.Coord(200, 20, 20);
        var st = Stores.World.Get(1);
        st.Actors.Add(new Actor { ActorId = -7, NpcId = 522 });
        st.Actors.Add(new Actor { ActorId = -8, NpcId = 123 });
        st.Interactives.Add(new MapInteractive { ElementId = 50, ElementTypeId = -1, CellId = 34 });

        var rec = new RoadRecorder(launch) { PromptForMap = (_, _) => Task.FromResult<MapPlan?>(new MapPlan(true, false)) };
        rec.Start(1, 100);
        launch.Packet(2, true, "NpcGenericActionRequest", ("npc_id", -7)); // autre personnage : ignoré
        // 2 réponses sur la même carte, dialogue fermé volontairement.
        launch.Packet(1, true, "NpcGenericActionRequest", ("npc_id", -7), ("npc_action_id", 3));
        launch.Packet(1, true, "NpcDialogReplyRequest", ("reply_id", 101));
        launch.Packet(1, true, "NpcDialogReplyRequest", ("reply_id", 102));
        launch.Packet(1, true, "DialogLeaveRequest");
        launch.Packet(1, false, "QuestStartedEvent", ("quest_id", 44));
        launch.Packet(1, false, "QuestObjectiveValidatedEvent", ("quest_id", 44), ("objective_id", 301));
        launch.Change(1, 101);
        // Dialogue PNJ causant une téléportation : dernier reply doit attendre le changement.
        launch.Packet(1, true, "NpcGenericActionRequest", ("npc_id", -8), ("npc_action_id", 3));
        launch.Packet(1, true, "NpcDialogReplyRequest", ("reply_id", 201));
        launch.Change(1, 200);
        // Dialogue final sans changement de carte : sauvegardé lors de Stop().
        launch.Packet(1, true, "NpcGenericActionRequest", ("npc_id", -7), ("npc_action_id", 3));
        launch.Packet(1, true, "NpcDialogReplyRequest", ("reply_id", 999)); // index non résolu : ID conservé
        var lua = rec.Stop();
        Expect(lua.Contains("talkNpc(522) replyNpc(1) replyNpc(2)"), "dialogue sans transition");
        Expect(lua.Contains("changeMap = \"right\""), "bord après le dialogue");
        Expect(lua.Contains("talkNpc(123) if not replyNpcAndChangeMap(1)")
               && lua.Contains("aucun changement de carte"), "PNJ téléportant gardé");
        Expect(lua.Contains("npc:hasReply(999)"), "fallback réponse ID inconnu");
        Expect(lua.Contains("QuestStartedEvent") && lua.Contains("objective=301"), "journal de quête");
        Expect(lua.Contains("gather = true") && lua.Contains("fight = false"), "choix carte");
        Expect(rec.StepCount == 3, "cartes suivies");
        Console.WriteLine("ROAD_NATIVE_QUEST_OFFLINE_PASS (dialogs, replies, quest, portage modelId, teleport, fallback)");
    }
}
