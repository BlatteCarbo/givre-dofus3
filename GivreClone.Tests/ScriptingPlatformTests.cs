using System.IO;
using System.Linq;
using System.Text;
using GivreClone.Engine;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Lot G (plateforme de scripting) : arbre neutre des paquets hookés (PacketTree, noms/types par le VRAI catalogue),
/// lecteur des événements de groupe (PartyReader) et résolution sûre des modules Lua (LuaModulePath).
/// </summary>
public class ScriptingPlatformTests
{
    private static readonly ProtocolCatalog Cat = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
    private static int F(string message, string field) => Cat.FieldNumber(message, field)
        ?? throw new Xunit.Sdk.XunitException($"Champ manquant du mapping livré : {message}.{field}");

    // --- encodeur protobuf minimal ---
    private static byte[] Cat2(params byte[][] parts)
    {
        var l = new List<byte>();
        foreach (var p in parts) l.AddRange(p);
        return l.ToArray();
    }
    private static byte[] Var(ulong v)
    {
        var l = new List<byte>();
        do { byte c = (byte)(v & 0x7F); v >>= 7; if (v != 0) c |= 0x80; l.Add(c); } while (v != 0);
        return l.ToArray();
    }
    private static byte[] Tag(int field, int wire) => Var((ulong)((field << 3) | wire));
    private static byte[] I(int field, long v) => Cat2(Tag(field, 0), Var(unchecked((ulong)v)));
    private static byte[] Str(int field, string s) { var b = Encoding.UTF8.GetBytes(s); return Cat2(Tag(field, 2), Var((ulong)b.Length), b); }
    private static byte[] M(int field, params byte[][] parts) { var b = Cat2(parts); return Cat2(Tag(field, 2), Var((ulong)b.Length), b); }
    private static List<ProtoField> Parse(params byte[][] parts) => RawProtobuf.Parse(Cat2(parts));

    private static List<ProtoField> ChatPayload() => Parse(
        Str(F("ChatChannelMessageEvent", "content"), "hi"),
        Str(F("ChatChannelMessageEvent", "sender_name"), "Bob"),
        I(F("ChatChannelMessageEvent", "sender_character_id"), 123),
        I(F("ChatChannelMessageEvent", "channel"), 10));
    private static byte[] Character(long id, string name) =>
        Cat2(M(F("Character", "character_basic_information"),
                Str(F("CharacterBasicInformation", "name"), name)),
             I(F("Character", "id"), id));
    private static List<ProtoField> JoinPayload() => Parse(
        M(F("PartyJoinEvent", "members"), Character(11, "A")),
        M(F("PartyJoinEvent", "members"), Character(22, "B")),
        I(F("PartyJoinEvent", "leader_id"), 22),
        I(F("PartyJoinEvent", "party_id"), 77));

    [Fact]
    public void PacketTree_NamesFieldsAndDecodesBySchema()
    {
        var payload = ChatPayload();
        // Piège du parseur brut : "hi" (0x68 0x69) ressort en Message -> le décodage doit suivre le TYPE du schéma.
        Assert.NotNull(payload.First(f => f.Field == F("ChatChannelMessageEvent", "content")).Message);
        var t = PacketTree.Build(Cat, "ChatChannelMessageEvent", payload);
        Assert.Equal("hi", t["content"]);
        Assert.Equal(10L, t["channel"]);
        Assert.Equal("Bob", t["sender_name"]);
        Assert.Equal(123L, t["sender_character_id"]);
        Assert.Equal("ChatChannelMessageEvent", t["__name"]);
        Assert.Equal("in", t["__dir"]);
        Assert.False(t.ContainsKey("__truncated"));
    }

    [Fact]
    public void PacketTree_NestedAndRepeated()
    {
        var t = PacketTree.Build(Cat, "PartyJoinEvent", JoinPayload(), outgoing: true);
        var members = Assert.IsType<List<object?>>(t["members"]);
        Assert.Equal(2, members.Count);
        var m0 = Assert.IsType<Dictionary<string, object?>>(members[0]);
        var basic = Assert.IsType<Dictionary<string, object?>>(m0["character_basic_information"]);
        Assert.Equal("A", basic["name"]);
        Assert.Equal(11L, m0["id"]);
        Assert.Equal(77L, t["party_id"]);
        Assert.Equal(22L, t["leader_id"]);
        Assert.Equal("out", t["__dir"]);
    }

    [Fact]
    public void PacketTree_UnknownMessageUsesFieldNumbers()
    {
        var t = PacketTree.Build(Cat, null, ChatPayload());
        Assert.Equal(123L, t["f" + F("ChatChannelMessageEvent", "sender_character_id")]);
        Assert.Equal(10L, t["f" + F("ChatChannelMessageEvent", "channel")]);
        Assert.Equal("Bob", t["f" + F("ChatChannelMessageEvent", "sender_name")]);
        Assert.True(t.ContainsKey("f" + F("ChatChannelMessageEvent", "content")));
        Assert.Equal("", t["__name"]);
    }

    [Fact]
    public void PacketTree_WithoutCatalogStillBuilds()
    {
        var t = PacketTree.Build(null, "ChatChannelMessageEvent", ChatPayload());
        Assert.Equal(123L, t["f" + F("ChatChannelMessageEvent", "sender_character_id")]);
        Assert.Equal("ChatChannelMessageEvent", t["__name"]);
    }

    [Fact]
    public void PacketTree_Limits()
    {
        var many = Parse(Enumerable.Range(0, 10).Select(i =>
            M(F("PartyJoinEvent", "members"), Character(100 + i, "N" + i))).ToArray());
        var capped = PacketTree.Build(Cat, "PartyJoinEvent", many, limits: new PacketTree.Limits(MaxArray: 3));
        Assert.Equal(3, Assert.IsType<List<object?>>(capped["members"]).Count);

        var tiny = PacketTree.Build(Cat, "PartyJoinEvent", JoinPayload(), limits: new PacketTree.Limits(MaxNodes: 5));
        Assert.Equal(true, tiny["__truncated"]);
        Assert.Equal("PartyJoinEvent", tiny["__name"]);

        var shallow = PacketTree.Build(Cat, "PartyJoinEvent", JoinPayload(), limits: new PacketTree.Limits(MaxDepth: 1));
        var m0 = Assert.IsType<Dictionary<string, object?>>(Assert.IsType<List<object?>>(shallow["members"])[0]);
        Assert.False(m0.ContainsKey("character_basic_information"));   // profondeur 2 coupée
    }

    [Fact]
    public void PartyReader_Join()
    {
        var (partyId, leaderId, members) = PartyReader.ReadJoin(JoinPayload());
        Assert.Equal(77, partyId);
        Assert.Equal(22, leaderId);
        Assert.Equal(2, members.Count);
        Assert.Equal(new PartyReader.PartyMember(11, "A"), members[0]);
        Assert.Equal(new PartyReader.PartyMember(22, "B"), members[1]);
    }

    [Fact]
    public void PartyReader_NewMemberAndRemove()
    {
        var (pid, member) = PartyReader.ReadNewMember(Parse(
            M(F("PartyNewMemberEvent", "member"), Character(555, "Mule")),
            I(F("PartyNewMemberEvent", "party_id"), 77)));
        Assert.Equal(77, pid);
        Assert.Equal(new PartyReader.PartyMember(555, "Mule"), member);

        var (pid2, leaving) = PartyReader.ReadMemberRemove(Parse(
            I(F("PartyMemberRemoveEvent", "leaving_player_id"), 555),
            I(F("PartyMemberRemoveEvent", "party_id"), 77)));
        Assert.Equal(77, pid2);
        Assert.Equal(555, leaving);

        var (pid3, upd) = PartyReader.ReadMemberUpdate(Parse(
            I(F("PartyMemberUpdateEvent", "party_id"), 77),
            M(F("PartyMemberUpdateEvent", "member"), Character(555, "Mule"))));
        Assert.Equal(77, pid3);
        Assert.Equal(555, upd?.Id);

        Assert.Equal(77, PartyReader.ReadPartyId("PartyLeaveEvent",
            Parse(I(F("PartyLeaveEvent", "party_id"), 77))));
        Assert.Equal((77L, 555L), PartyReader.ReadLeaderUpdate(Parse(
            I(F("PartyLeaderUpdateEvent", "party_id"), 77),
            I(F("PartyLeaderUpdateEvent", "party_leader_id"), 555))));
    }

    [Theory]
    [InlineData("util", "util.lua")]
    [InlineData("util.lua", "util.lua")]
    [InlineData(" UTIL.LUA ", "UTIL.lua")]
    [InlineData("a.b", "a/b.lua")]
    [InlineData("my-lib_2.x", "my-lib_2/x.lua")]
    public void LuaModulePath_Accepts(string name, string expectedRel)
    {
        string root = Path.Combine(Path.GetTempPath(), "givre-lib-test", "lib");
        Assert.True(LuaModulePath.TryResolve(root, name, out var full, out var err));
        Assert.Null(err);
        string expected = Path.GetFullPath(Path.Combine(root, expectedRel.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Equal(expected, full);
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("..")]
    [InlineData("a..b")]
    [InlineData(".a")]
    [InlineData("a.")]
    [InlineData("C:\\x")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a.b.c.d.e")]
    [InlineData("a b")]
    public void LuaModulePath_Rejects(string name)
    {
        string root = Path.Combine(Path.GetTempPath(), "givre-lib-test", "lib");
        Assert.False(LuaModulePath.TryResolve(root, name, out _, out var err));
        Assert.Equal("bad-name", err);
    }

    [Fact]
    public void LuaModulePath_RejectsTooLong()
    {
        string root = Path.Combine(Path.GetTempPath(), "givre-lib-test", "lib");
        Assert.False(LuaModulePath.TryResolve(root, new string('a', 65), out _, out _));
        Assert.True(LuaModulePath.TryResolve(root, new string('a', 64), out _, out _));
        Assert.True(LuaModulePath.TryResolve(root, "a.b.c.d", out _, out _));
    }
}
