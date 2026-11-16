using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Lot I (quêtes, équipement) : forges et lecteurs vérifiés contre le VRAI catalogue livré (branché par TestSetup). Codes et
/// numéros attendus = data/matching_current.json + data/fields.json (⚠ QuestObjectiveValidationRequest a objective_id=1 /
/// quest_id=2, l'INVERSE du .proto de référence). Les lecteurs imbriqués sont rejoués par le chemin CATALOGUE.
/// </summary>
public class LotIQuestInventoryTests
{
    private static readonly ProtocolCatalog Mapping = ProtocolCatalog.LoadFromDirectory(
        System.IO.Path.Combine(System.AppContext.BaseDirectory, "data"));
    private static string Code(string name) => Mapping.CodeForName(name)
        ?? throw new System.InvalidOperationException($"Mapping missing message {name}");
    private static int F(string name, string field) => Mapping.FieldNumber(name, field)
        ?? throw new System.InvalidOperationException($"Mapping missing field {name}.{field}");
    // --- encodeur protobuf minimal (mêmes helpers que Patch361112Tests) ---
    private static byte[] Cat(params byte[][] parts)
    {
        int n = 0; foreach (var p in parts) n += p.Length;
        var b = new byte[n]; int o = 0;
        foreach (var p in parts) { p.CopyTo(b, o); o += p.Length; }
        return b;
    }
    private static byte[] Var(ulong v)
    {
        var l = new List<byte>();
        do { byte c = (byte)(v & 0x7F); v >>= 7; if (v != 0) c |= 0x80; l.Add(c); } while (v != 0);
        return l.ToArray();
    }
    private static byte[] Tag(int field, int wire) => Var((ulong)((field << 3) | wire));
    private static byte[] I(int field, long v) => Cat(Tag(field, 0), Var(unchecked((ulong)v)));
    private static byte[] M(int field, params byte[][] parts) { var b = Cat(parts); return Cat(Tag(field, 2), Var((ulong)b.Length), b); }
    private static IReadOnlyList<ProtoField> Parse(params byte[][] parts) => RawProtobuf.Parse(Cat(parts));

    private static IReadOnlyList<ProtoField> Frame(byte[] framed)
    {
        Assert.NotEmpty(framed);
        int pos = 0;
        Assert.True(RawProtobuf.TryReadVarint(framed, ref pos, out ulong len));
        Assert.Equal((int)len, framed.Length - pos);
        return RawProtobuf.Parse(framed[pos..]);
    }
    private static (string Code, IReadOnlyList<ProtoField> Payload) Request(byte[] framed)
    {
        var gm = Frame(framed);
        var req = gm.FirstOrDefault(f => f.Field == F("GameMessage", "request") && f.Message is not null)?.Message;
        Assert.NotNull(req);
        var any = req!.FirstOrDefault(f => f.Field == F("Request", "content") && f.Message is not null)?.Message;
        Assert.NotNull(any);
        var url = any!.FirstOrDefault(f => f.Field == 1)?.String;
        Assert.NotNull(url);
        var payload = any.FirstOrDefault(f => f.Field == 2 && f.Message is not null)?.Message ?? new List<ProtoField>();
        return (url!["type.ankama.com/".Length..], payload);
    }
    private static long V(IReadOnlyList<ProtoField> p, int field) => p.First(f => f.Field == field && f.Wire == WireType.Varint).AsSignedVarint;

    // =============================================================== forges
    [Theory]
    [InlineData(6)]
    [InlineData(28)]
    public void ObjectSetPosition_forge_uses_mapping_numbers(int position)
    {
        var (code, p) = Request(MoveForge.BuildObjectSetPositionFramed(14852379, position));
        Assert.Equal(Code("ObjectSetPositionRequest"), code);
        Assert.Equal(position, V(p, F("ObjectSetPositionRequest", "position")));
        Assert.Equal(14852379, V(p, F("ObjectSetPositionRequest", "object_uid")));
        Assert.Equal(1, V(p, F("ObjectSetPositionRequest", "quantity")));
    }

    [Fact]
    public void QuestObjectiveValidation_forge_has_objective_first()
    {
        var (code, p) = Request(MoveForge.BuildQuestObjectiveValidationFramed(1234, 9001));
        Assert.Equal(Code("QuestObjectiveValidationRequest"), code);
        Assert.Equal(9001, V(p, F("QuestObjectiveValidationRequest", "objective_id")));
        Assert.Equal(1234, V(p, F("QuestObjectiveValidationRequest", "quest_id")));
    }

    [Fact]
    public void QuestStepInformation_forge_omits_obfuscated_field()
    {
        var (code, p) = Request(MoveForge.BuildQuestStepInformationFramed(1234));
        Assert.Equal(Code("QuestStepInformationRequest"), code);
        Assert.Equal(1234, V(p, F("QuestStepInformationRequest", "quest_id")));
        Assert.DoesNotContain(p, f => f.Field == F("QuestStepInformationRequest", "fpbs"));
    }

    // =============================================================== lecteurs de quêtes
    [Fact]
    public void QuestsEvent_snapshot_reads_finished_and_active_with_details()
    {
        var tree = Parse(
            M(F("QuestsEvent", "finished_quests"), I(F("QuestFinished", "quest_id"), 230), I(F("QuestFinished", "finished_count"), 1)),
            I(F("QuestsEvent", "player_id"), 41234567890),
            M(F("QuestsEvent", "active_quests"),
                I(F("QuestActive", "quest_id"), 1234),
                M(F("QuestActive", "details"),
                    I(F("Details", "step_id"), 5678),
                    M(F("Details", "objectives"),
                        I(F("QuestObjective", "objective_reached"), 1),
                        M(F("QuestObjective", "completion"),
                            I(F("Completion", "current_completion"), 3),
                            I(F("Completion", "max_completion"), 10)),
                        I(F("QuestObjective", "objective_id"), 9001)),
                    M(F("Details", "objectives"), I(F("QuestObjective", "objective_id"), 9002)))),
            M(F("QuestsEvent", "active_quests"), I(F("QuestActive", "quest_id"), 777)));
        var s = QuestReader.ReadQuests(tree);
        Assert.Equal(new[] { 230 }, s.Finished);
        Assert.Empty(s.LegacyExtra);
        Assert.Equal(41234567890, s.PlayerId);
        Assert.Equal(2, s.Active.Count);
        var a = s.Active[0];
        Assert.Equal(1234, a.Id);
        Assert.True(a.HasDetails);
        Assert.Equal(5678, a.StepId);
        Assert.Equal(2, a.Objectives.Count);
        Assert.Equal(new QuestReader.Objective(9001, true, 3, 10), a.Objectives[0]);
        Assert.Equal(new QuestReader.Objective(9002, false, 0, 0), a.Objectives[1]);
        Assert.Equal(777, s.Active[1].Id);
        Assert.False(s.Active[1].HasDetails);
    }

    [Fact]
    public void QuestsEvent_legacy_second_field_is_kept_apart()
    {
        var s = QuestReader.ReadQuests(Parse(
            M(F("QuestsEvent", "finished_quests"),
                I(F("QuestFinished", "quest_id"), 230),
                I(F("QuestFinished", "fpci"), 55))));
        Assert.Equal(new[] { 230 }, s.Finished);
        Assert.Equal(new[] { 55 }, s.LegacyExtra);
    }

    [Fact]
    public void Quest_incremental_events_are_read_by_name()
    {
        var (player, info) = QuestReader.ReadStepInformation(Parse(
            I(F("QuestStepInformationEvent", "player_id"), 41234567890),
            M(F("QuestStepInformationEvent", "information"),
                I(F("QuestActive", "quest_id"), 1234),
                M(F("QuestActive", "details"), I(F("Details", "step_id"), 42)))));
        Assert.Equal(41234567890, player);
        Assert.NotNull(info);
        Assert.Equal(1234, info!.Id);
        Assert.Equal(42, info.StepId);
        Assert.True(info.HasDetails);

        Assert.Equal((1234, 9001), QuestReader.ReadObjectiveValidated(Parse(
            I(F("QuestObjectiveValidatedEvent", "objective_id"), 9001),
            I(F("QuestObjectiveValidatedEvent", "quest_id"), 1234))));
        Assert.Equal((1234, 42), QuestReader.ReadStepEvent(Parse(
            I(F("QuestStepStartedEvent", "step_id"), 42), I(F("QuestStepStartedEvent", "quest_id"), 1234)), "QuestStepStartedEvent"));
        Assert.Equal((1234, 42), QuestReader.ReadStepEvent(Parse(
            I(F("QuestStepValidatedEvent", "step_id"), 42), I(F("QuestStepValidatedEvent", "quest_id"), 1234)), "QuestStepValidatedEvent"));
        Assert.Equal(1234, QuestReader.ReadQuestId(Parse(I(F("QuestValidatedEvent", "quest_id"), 1234)), "QuestValidatedEvent"));
        Assert.Equal(1234, QuestReader.ReadQuestId(Parse(I(F("QuestStartedEvent", "quest_id"), 1234),
            I(F("QuestStartedEvent", "fozm"), 7)), "QuestStartedEvent"));

        var followed = QuestReader.ReadFollowed(Parse(
            M(F("QuestsFollowedEvent", "quests"),
                I(F("QuestActive", "quest_id"), 11),
                M(F("QuestActive", "details"), I(F("Details", "step_id"), 3))),
            M(F("QuestsFollowedEvent", "quests"), I(F("QuestActive", "quest_id"), 12))));
        Assert.Equal(new[] { 11, 12 }, followed.Select(q => q.Id));
        Assert.True(followed[0].HasDetails);
        Assert.False(followed[1].HasDetails);
    }

    // =============================================================== inventaire
    [Fact]
    public void ObjectMovement_absent_position_is_amulet()
    {
        int uid = F("ObjectMovementEvent", "object_uid"), pos = F("ObjectMovementEvent", "position");
        Assert.Equal((18207351, 6), InventoryReader.ReadMovement(Parse(I(pos, 6), I(uid, 18207351))));
        Assert.Equal((18207351, 0), InventoryReader.ReadMovement(Parse(I(uid, 18207351))));
        Assert.Null(InventoryReader.ReadMovement(Parse(I(pos, 6))));
    }

    [Fact]
    public void Snapshot_absent_position_is_amulet_not_legacy_field_one()
    {
        // Position absente = 0 (amulette). Les valeurs exactes viennent du catalogue courant.
        var items = InventoryReader.ReadContent(Parse(M(F("InventoryContentEvent", "objects"),
            M(F("ObjectItemInventory", "item"),
                I(F("ObjectItem", "uid"), 18207351),
                I(F("ObjectItem", "quantity"), 1),
                I(F("ObjectItem", "gid"), 2412)))));
        var it = Assert.Single(items);
        Assert.Equal(18207351, it.Uid);
        Assert.Equal(0, it.Position);
    }

    [Theory]
    [InlineData(new[] { 0 }, true)]                 // amulette
    [InlineData(new[] { 2, 4 }, true)]              // anneaux
    [InlineData(new[] { 9, 10, 11, 12, 13, 14 }, true)]   // dofus / trophées
    [InlineData(new[] { 15 }, true)]                // bouclier
    [InlineData(new[] { 0, 2, 3, 4, 5, 6, 7, 8 }, false)]   // super type 22 (multi)
    [InlineData(new[] { 0, 3, 5, 7, 6, 2, 4 }, false)]      // super type 69 (multi)
    [InlineData(new[] { 20 }, false)]               // >= 16 (monture, compagnon...)
    [InlineData(new int[0], false)]
    public void Stuff_eligibility_follows_game_positions(int[] positions, bool expected)
        => Assert.Equal(expected, LuaCompat.AutoStuffEligible(positions));
}
