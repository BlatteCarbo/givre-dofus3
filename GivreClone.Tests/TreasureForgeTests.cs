using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Chasse au trésor: forges résolues PAR NOM contre le vrai catalogue livré (branché par
/// TestSetup). Codes attendus = data/matching_current.json ; numéros = data/fields.json. Une valeur 0 est OMISE (proto3).</summary>
public class TreasureForgeTests
{
    private static readonly ProtocolCatalog Mapping = ProtocolCatalog.LoadFromDirectory(
        System.IO.Path.Combine(System.AppContext.BaseDirectory, "data"));
    private static string Code(string name) => Mapping.CodeForName(name)
        ?? throw new System.InvalidOperationException($"Mapping missing message {name}");
    private static int Field(string name, string property) => Mapping.FieldNumber(name, property)
        ?? throw new System.InvalidOperationException($"Mapping missing field {name}.{property}");
    private static (string Code, IReadOnlyList<ProtoField> Payload) Request(byte[] framed)
    {
        Assert.NotEmpty(framed);
        int pos = 0;
        Assert.True(RawProtobuf.TryReadVarint(framed, ref pos, out ulong len));
        Assert.Equal((int)len, framed.Length - pos);
        var gm = RawProtobuf.Parse(framed[pos..]);
        var req = gm.First(f => f.Field == Field("GameMessage", "request") && f.Message is not null).Message!;
        var any = req.First(f => f.Field == Field("Request", "content") && f.Message is not null).Message!;
        var url = any.First(f => f.Field == 1).String!;
        var payload = any.FirstOrDefault(f => f.Field == 2 && f.Message is not null)?.Message ?? new List<ProtoField>();
        return (url["type.ankama.com/".Length..], payload);
    }
    private static long V(IReadOnlyList<ProtoField> p, int field) => p.First(f => f.Field == field && f.Wire == WireType.Varint).AsSignedVarint;

    [Fact]
    public void Observed_treasure_flag_preserves_its_server_map()
    {
        // Captured flag on 2026-10-30: field 2 = map 99355648.
        // Fixture bytes are independent of the field catalogue being checked.
        var payload = RawProtobuf.Parse(new byte[] { 0x10, 0x80, 0x98, 0xb0, 0x2f });

        long map = PayloadReader.Int(payload, Field("TreasureHuntFlag", "map_id"), 0);

        Assert.Equal(99355648, map);
    }

    [Fact]
    public void Legendary_forge_carries_the_requested_hunt_id()
    {
        var (code, p) = Request(MoveForge.BuildTreasureLegendaryFramed(29));
        Assert.Equal(Code("TreasureHuntLegendaryRequest"), code);
        Assert.Equal(29, V(p, Field("TreasureHuntLegendaryRequest", "legendary_id")));
    }

    [Fact]
    public void Flag_forge_carries_index_and_quest_type()
    {
        var (code, p) = Request(MoveForge.BuildTreasureFlagFramed(3, 1));
        Assert.Equal(Code("TreasureHuntFlagRequest"), code);
        Assert.Equal(3, V(p, Field("TreasureHuntFlagRequest", "index")));
        Assert.Equal(1, V(p, Field("TreasureHuntFlagRequest", "quest_type")));
    }

    [Fact]
    public void Flag_forge_omits_zero_values_like_the_real_client()
    {
        var (code, p) = Request(MoveForge.BuildTreasureFlagFramed(0, 0));
        Assert.Equal(Code("TreasureHuntFlagRequest"), code);
        Assert.Empty(p);
    }

    [Fact]
    public void Dig_giveup_and_flag_remove_forges_resolve_by_name()
    {
        var (dig, dp) = Request(MoveForge.BuildTreasureDigFramed(2));
        Assert.Equal(Code("TreasureHuntDigRequest"), dig);
        Assert.Equal(2, V(dp, Field("TreasureHuntDigRequest", "quest_type")));
        Assert.Single(dp);

        var (give, gp) = Request(MoveForge.BuildTreasureGiveUpFramed(2));
        Assert.Equal(Code("TreasureHuntGiveUpRequest"), give);
        Assert.Equal(2, V(gp, Field("TreasureHuntGiveUpRequest", "quest_type")));

        var (rem, rp) = Request(MoveForge.BuildTreasureFlagRemoveFramed(1, 2));
        Assert.Equal(Code("TreasureHuntFlagRemoveRequest"), rem);
        Assert.Equal(1, V(rp, Field("TreasureHuntFlagRemoveRequest", "index")));
        Assert.Equal(2, V(rp, Field("TreasureHuntFlagRemoveRequest", "quest_type")));
    }

    [Fact]
    public void Guild_invitation_refusal_omits_the_false_flag()
    {
        // IGNORE_REQUEST_GUILD : refus = accepted false, OMIS comme le vrai client proto3.
        var (code, p) = Request(MoveForge.BuildGuildInvitationAnswerFramed(false));
        Assert.Equal(Code("GuildInvitationAnswerRequest"), code);
        Assert.Empty(p);
        var (code2, p2) = Request(MoveForge.BuildGuildInvitationAnswerFramed(true));
        Assert.Equal(Code("GuildInvitationAnswerRequest"), code2);
        Assert.Equal(1, V(p2, Field("GuildInvitationAnswerRequest", "accepted")));
    }
}
