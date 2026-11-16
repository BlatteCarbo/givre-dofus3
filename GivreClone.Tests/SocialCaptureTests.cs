using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.App.Services.LotM;
using GivreClone.App.Services.Social;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public class SocialCaptureTests
{
    private const long SarkahId = 45948469602;
    private const long PoussId = 46534885730;

    private const string SarkahInfoHex = "22220803121E0A13747970652E616E6B616D612E636F6D2F6B7577120708E282F895AB01";
    private const string SarkahViewHex = "22220804121E0A13747970652E616E6B616D612E636F6D2F6B7574120708E282F895AB01";
    private const string PoussInfoHex = "22220805121E0A13747970652E616E6B616D612E636F6D2F6B7577120708E282C8ADAD01";
    private const string PoussViewHex = "22220806121E0A13747970652E616E6B616D612E636F6D2F6B7574120708E282C8ADAD01";
    private const string EquipmentHex = "2224080712200A13747970652E616E6B616D612E636F6D2F6B7574120908E282C8ADAD011001";
    private const string LoginSubscribeHex = "223A08FFFFFFFFFFFFFFFFFF01122D0A13747970652E616E6B616D612E636F6D2F6B707512160A0C00010203040A0C0D0B1012131206050608090F11";
    private const string RecruitSubscribeHex = "223A08FFFFFFFFFFFFFFFFFF01122D0A13747970652E616E6B616D612E636F6D2F6B707512160A0D0001020304060A0C0D0B10121312050508090F11";
    private const string TradeSubscribeHex = "223A08FFFFFFFFFFFFFFFFFF01122D0A13747970652E616E6B616D612E636F6D2F6B707512160A0E000102030405060A0C0D0B101213120408090F11";
    private const string PlayerCardHex = "224708FFFFFFFFFFFFFFFFFF01123A0A13747970652E616E6B616D612E636F6D2F6B707712231A1F7B706C61796572436172642C5361726B61682C34353934383436393630327D2200";
    private const string MonsterHex = "22A80108FFFFFFFFFFFFFFFFFF01129A010A13747970652E616E6B616D612E636F6D2F6B70771282011A3B7B6D6F6E7374657247726F757042657374696172792C2D32303030337D207B6C6F63616C697A654D6F6E7374657247726F75702C2D32303030337D22431241120D08E8FFFFFFFFFFFFFFFF011807180120DDE3FEFFFFFFFFFFFF013221327837397831377C347834387831397C3378343832307831387C32783738783137404F";
    private const string ChannelThreeHex = "222E08FFFFFFFFFFFFFFFFFF0112210A13747970652E616E6B616D612E636F6D2F6B7077120A10031A04746573742200";

    private static readonly ProtocolCatalog Mapping = ProtocolCatalog.LoadFromDirectory(
        System.IO.Path.Combine(AppContext.BaseDirectory, "data"));

    [Fact]
    public void Card_requests_match_the_captured_game_messages()
    {
        Assert.Equal(SarkahInfoHex, Hex(SocialFrames.CardInfoMessage(Code, Field, SarkahId, 3)));
        Assert.Equal(SarkahViewHex, Hex(SocialFrames.CardViewMessage(Code, Field, SarkahId, 0, 4)));
        Assert.Equal(PoussInfoHex, Hex(SocialFrames.CardInfoMessage(Code, Field, PoussId, 5)));
        Assert.Equal(PoussViewHex, Hex(SocialFrames.CardViewMessage(Code, Field, PoussId, 0, 6)));
        Assert.Equal(EquipmentHex, Hex(SocialFrames.CardViewMessage(Code, Field, PoussId, 1, 7)));

        Assert.Equal(SarkahId, SocialFields.Var(Payload(SarkahInfoHex), 1));
        Assert.Equal(1, SocialFields.Var(Payload(EquipmentHex), 2));
        Assert.Equal(PoussId, SocialFields.Var(Payload(EquipmentHex), 1));
    }

    [Theory]
    [InlineData(LoginSubscribeHex)]
    [InlineData(RecruitSubscribeHex)]
    [InlineData(TradeSubscribeHex)]
    public void Subscribe_requests_round_trip_the_captured_channel_bytes(string hex)
    {
        var (active, disabled) = ChannelPlan.Read(Payload(hex), Field);
        Assert.Equal(hex, Hex(SocialFrames.SubscribeMessage(Code, Field, active, disabled, -1)));
    }

    [Fact]
    public void Login_channel_bytes_are_the_every_channel_list()
    {
        var (active, disabled) = ChannelPlan.Read(Payload(LoginSubscribeHex), null);
        Assert.Equal(ChannelPlan.LoginActive, active);
        Assert.Equal(ChannelPlan.LoginDisabled, disabled);
        Assert.Equal(ChannelPlan.LoginActive.Concat(ChannelPlan.LoginDisabled), ChannelPlan.AllChannels);

        var all = SocialFrames.SubscribeMessage(Code, Field, ChannelPlan.AllChannels, Array.Empty<int>(), -1);
        var (rebuilt, cut) = ChannelPlan.Read(Payload(Hex(all)), null);
        Assert.Equal(ChannelPlan.AllChannels, rebuilt);
        Assert.Empty(cut);
    }

    [Fact]
    public void Framed_request_keeps_the_game_message_after_its_length()
    {
        byte[] message = SocialFrames.CardInfoMessage(Code, Field, SarkahId, 3);
        byte[] framed = SocialFrames.Prefix(message);
        int pos = 0;
        Assert.True(RawProtobuf.TryReadVarint(framed, ref pos, out ulong len));
        Assert.Equal((ulong)message.Length, len);
        Assert.Equal(Hex(message), Hex(framed[pos..]));
    }

    [Fact]
    public void Mitm_subscribe_stays_local_until_the_option_is_on()
    {
        Assert.False(ChannelPlan.MaySend(mitm: true, allowGameCheckboxChange: false));
        Assert.True(ChannelPlan.MaySend(mitm: true, allowGameCheckboxChange: true));
        Assert.True(ChannelPlan.MaySend(mitm: false, allowGameCheckboxChange: false));
        Assert.False(ChannelPlan.Shows(5, new HashSet<int> { 0, 11 }));
        Assert.True(ChannelPlan.Shows(11, new HashSet<int> { 0, 11 }));
        Assert.Equal("Commerce", ChannelPlan.Label(5));
        Assert.Equal("Recrutement", ChannelPlan.Label(6));
        Assert.Equal("Communauté", ChannelPlan.Label(11));
        Assert.Equal("Kolizeum", ChannelPlan.Label(12));
        Assert.Equal("Canal 13", ChannelPlan.Label(13));
    }

    [Fact]
    public void Captured_chat_requests_keep_channel_content_and_the_monster_group()
    {
        var player = ChatMessages.ReadRequest(Payload(PlayerCardHex), Field);
        Assert.Equal(0, player.Channel);
        Assert.Equal("{playerCard,Sarkah,45948469602}", player.Content);
        Assert.Empty(player.Items);
        Assert.Empty(player.Groups);

        var channel = ChatMessages.ReadRequest(Payload(ChannelThreeHex), Field);
        Assert.Equal(3, channel.Channel);
        Assert.Equal("test", channel.Content);

        var monster = ChatMessages.ReadRequest(Payload(MonsterHex), Field);
        Assert.Equal("{monsterGroupBestiary,-20003} {localizeMonsterGroup,-20003}", monster.Content);
        var group = Assert.Single(monster.Groups);
        Assert.Equal(-20003, group.GroupId);
        Assert.Equal(7, group.X);
        Assert.Equal(-24, group.Y);
        Assert.Equal(1, group.Marker);
        Assert.Equal(79, group.MainMonsterId);
        Assert.Equal(new[] { (2, 79, 17), (4, 48, 19), (3, 4820, 18), (2, 78, 17) },
            group.Members.Select(m => (m.Count, m.MonsterId, m.Level)).ToArray());
        Assert.Equal(198, ChatMessages.TotalLevel(group));

        var card = HoverCards.Monster(group, new FixedXp(10));
        Assert.Equal("Niveau 198", card.LevelLine);
        Assert.Equal("XP 110", card.XpLine);
        Assert.Equal("Carte 7, -24", card.MapLine);
        Assert.Contains("Id 79", card.MembersText);
        Assert.Contains("Id 4820", card.MembersText);

        var unknown = HoverCards.Monster(group, new FixedXp(10, missing: 4820));
        Assert.Equal("XP inconnue", unknown.XpLine);
    }

    [Fact]
    public void Chat_markup_from_the_log_becomes_names_coordinates_and_links()
    {
        var player = Assert.Single(ChatLinks.Links("{playerCard,Sarkah,45948469602}"));
        Assert.Equal("player", player.Kind);
        Assert.Equal("Sarkah", player.Text);
        Assert.Equal(SarkahId, player.A);
        Assert.Equal("Sarkah", ChatLinks.Readable("{playerCard,Sarkah,45948469602}", null));

        const string sale = "vend {chatitem,4644892,23740} {chatitem,4485660,23741} 2m5/u";
        var items = ChatLinks.Links(sale);
        Assert.Equal(2, items.Count);
        Assert.Equal((4644892L, 23740L), (items[0].A, items[0].B));
        Assert.Equal((4485660L, 23741L), (items[1].A, items[1].B));
        Assert.Equal("vend Copie objet 2m5/u", ChatLinks.Readable(sale, gid => gid == 23740 ? "Copie" : "objet"));

        var monsters = ChatLinks.Links("{monsterGroupBestiary,-20003} {localizeMonsterGroup,-20003}");
        var monster = Assert.Single(monsters);
        Assert.Equal("monster", monster.Kind);
        Assert.Equal(-20003, monster.A);
        Assert.Equal("groupe", ChatLinks.Readable("{monsterGroupBestiary,-20003} {localizeMonsterGroup,-20003}", null));

        const string maps = "OUVERTURE KRALA : {{map,-49,9,1}} FULL -- {{map,-52,15,1}} 3/12 -- {{map,-55,11,1}} 5/12 -- {{map,-56,4,1}} 11/12 ! GOGOGO";
        Assert.Contains("[-49, 9]", ChatLinks.Readable(maps, null));
        Assert.Contains("[-52, 15]", ChatLinks.Readable(maps, null));
        Assert.Equal(4, ChatLinks.Links(maps).Count(link => link.Kind == "map"));

        const string recruit = "Coucou tout le monde ! {guild,27485,Les Fragments du Krosmoz,False,True} recrute de nouveaux membres !";
        string readable = ChatLinks.Readable(recruit, null);
        Assert.Contains("Les Fragments du Krosmoz", readable);
        Assert.DoesNotContain("{guild", readable);
    }

    [Fact]
    public void Directory_remembers_a_seen_name_without_inventing_a_lookup()
    {
        var directory = new PlayerDirectory();
        directory.Remember("Sarkah", SarkahId);
        Assert.True(directory.TryGet("sarkah", out long id));
        Assert.Equal(SarkahId, id);
        Assert.False(directory.TryGet("Inconnu", out _));
    }

    [Fact]
    public void Sarkah_profile_rows_use_the_captured_fields()
    {
        var header = PlayerProfileReader.ReadInfo(SarkahInfo(), null);
        Assert.NotNull(header);
        Assert.Equal(SarkahId, header!.CharacterId);
        Assert.Equal("Sarkah", header.Name);
        Assert.Equal(281, header.Level);
        Assert.Equal(15, header.BreedId);
        Assert.Equal(166, header.TitleId);
        Assert.Equal(92, header.OrnamentId);
        Assert.Equal(69752533, header.AccountId);
        Assert.Equal(22, header.JobCount);
        Assert.Equal(6, header.Wire10);
        Assert.Equal(1, header.Wire11);

        var view = PlayerProfileReader.ReadView(SarkahView(), null);
        Assert.False(view.Failed);
        Assert.Null(view.Equipment);
        Assert.NotNull(view.Social);
        Assert.Equal("Les Matinaux", view.Social!.GuildName);
        Assert.Equal(15818, view.Social.GuildId);
        Assert.Equal("Allocations Familiales", view.Social.AllianceName);
        Assert.Equal(72, view.Social.AlignmentValue);
        Assert.Equal(4, view.Social.AlignmentWire2);
        Assert.Equal(31, view.Social.AlignmentRankId);
        Assert.Equal(12813, view.Social.Trophy);
        Assert.Contains(view.Social.Jobs, job => job.JobId == 2 && job.Level == 197);
        Assert.Equal(20, view.Social.Jobs.Count);

        var names = new CaptureNames();
        var rows = ProfileText.General(header, view.Social, names);
        Assert.Contains(rows, row => row.Label == "Niveau" && row.Value == "Oméga 81");
        Assert.Contains(rows, row => row.Label == "Classe" && row.Value == "Steamer");
        Assert.Contains(rows, row => row.Label == "Titre" && row.Value == "Traqueur Implacable / Traqueuse Implacable");
        Assert.Contains(rows, row => row.Label == "Ornement" && row.Value == "Score vénérable");
        Assert.Contains(rows, row => row.Label == "Guilde" && row.Value == "Les Matinaux");
        Assert.Contains(rows, row => row.Label == "Alliance" && row.Value == "Allocations Familiales");
        Assert.Contains(rows, row => row.Label == "Alignement" && row.Value == "Brâkmarien 72");
        Assert.Contains(rows, row => row.Label == "Trophées" && row.Value == "12813");
        Assert.Contains(rows, row => row.Label == "Bûcheron" && row.Value == "197");
        Assert.Contains(rows, row => row.Label == "Parchomage" && row.Value == "0");
        Assert.DoesNotContain(rows, row => row.Label.Contains("Ordre", StringComparison.Ordinal));

        var memory = new ProfileMemory();
        memory.Begin(true);
        memory.ApplyInfo(header);
        memory.ApplyView(view);
        Assert.Equal(EquipmentState.Waiting, memory.EquipmentState);
        memory.ApplyView(view);
        Assert.Equal(EquipmentState.Hidden, memory.EquipmentState);
        Assert.Equal(ProfileText.HiddenTab, ProfileText.Note(memory.EquipmentState));
        Assert.Equal("Le joueur ne souhaite pas afficher cet onglet.", ProfileText.HiddenTab);
    }

    [Fact]
    public void Pouss_general_tab_has_no_guild_and_level_151()
    {
        var header = PlayerProfileReader.ReadInfo(PoussInfo(), null);
        Assert.NotNull(header);
        Assert.Equal(151, header!.Level);
        Assert.Equal(5, header.BreedId);
        Assert.Equal(22, header.JobCount);
        var view = PlayerProfileReader.ReadView(PoussView(), null);
        Assert.Null(view.Social!.GuildName);
        Assert.Null(view.Social.AllianceName);
        Assert.Equal(43, view.Social.AlignmentValue);
        Assert.Equal(3, view.Social.AlignmentWire2);
        Assert.Equal(8, view.Social.AlignmentRankId);
        Assert.Equal(3900, view.Social.Trophy);
        var rows = ProfileText.General(header, view.Social, new CaptureNames());
        Assert.Contains(rows, row => row.Value == "Niveau 151");
        Assert.Contains(rows, row => row.Value == "Xélor");
        Assert.Contains(rows, row => row.Value == "Bontarien 43");
        Assert.DoesNotContain(rows, row => row.Label == "Guilde");
    }

    [Fact]
    public void Hidden_empty_and_visible_equipment_stay_distinct()
    {
        var hidden = new ProfileMemory();
        hidden.Begin(true);
        hidden.ApplyView(new CardViewBody(false, SarkahSocialData(), null));
        hidden.ApplyView(new CardViewBody(true, null, null));
        Assert.Equal(EquipmentState.Hidden, hidden.EquipmentState);

        var empty = new ProfileMemory();
        empty.Begin(true);
        empty.ApplyView(new CardViewBody(false, null, new EquipmentCardData(Array.Empty<ProfileItem>(), Array.Empty<ProfileItem>())));
        Assert.Equal(EquipmentState.Empty, empty.EquipmentState);
        Assert.Equal("Aucun objet affiché.", ProfileText.Note(empty.EquipmentState));

        var shown = PlayerProfileReader.ReadView(PoussEquipment(), null);
        Assert.NotNull(shown.Equipment);
        long[] cosmetic = { 23479, 32111, 33532, 33531, 33534, 33533 };
        Assert.Equal(cosmetic, shown.Equipment!.Cosmetic.Select(item => item.Gid).ToArray());
        var cage = Assert.Single(shown.Equipment.Worn, item => item.Position == 0);
        Assert.Equal(33577, cage.Gid);
        Assert.Equal(1240139, cage.Uid);
        Assert.Equal(new[] { (48, 125), (21, 124), (15, 418), (6, 115), (2, 214), (1, 111), (1, 182) },
            cage.Effects.Select(effect => (effect.Value, effect.Action)).ToArray());
        var missing = Assert.Single(shown.Equipment.Worn, item => item.Gid == 33585);
        Assert.Contains(missing.Effects, effect => effect.Action == 97 && effect.Min == 25 && effect.Max == 21);

        var rows = ProfileText.Equipment(shown.Equipment, new CaptureNames(), _ => 1250, null);
        Assert.Contains(rows, row => row.Card.IdLine.Contains("33577", StringComparison.Ordinal) && row.Card.EffectsText.Contains("48", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Card.Name == "Objet 33585" && row.Card.PriceLine == "Prix moyen 1 250 kamas");
        Assert.Contains(rows, row => row.Place == "Apparat · position 5");

        var memory = new ProfileMemory();
        memory.Begin(true);
        memory.ApplyView(new CardViewBody(false, SarkahSocialData(), null));
        memory.ApplyView(shown);
        Assert.Equal(EquipmentState.Shown, memory.EquipmentState);
    }

    [Fact]
    public void Sale_metadata_keeps_instance_effects_and_items_without_effects()
    {
        var sale = ChatMessages.ReadEvent(SaleEvent(), null);
        Assert.Equal(5, sale.Channel);
        Assert.Equal("Kheibar-Shekan", sale.Sender);
        Assert.Equal(2, sale.Items.Count);
        Assert.Equal(23740, sale.Items[0].Gid);
        Assert.Equal(4644892, sale.Items[0].Uid);
        Assert.Empty(sale.Items[0].Effects);
        var plain = HoverCards.Item(sale.Items[0], new CaptureNames(), null, null);
        Assert.True(plain.MissingEffects);
        Assert.Equal("Prix moyen inconnu", plain.PriceLine);

        var priced = ChatMessages.ReadEvent(PricedItemEvent(), null);
        var item = Assert.Single(priced.Items);
        Assert.Equal(27512, item.Gid);
        Assert.Equal(1407294, item.Uid);
        var effect = Assert.Single(item.Effects);
        Assert.Equal(581, effect.Value);
        Assert.Equal(174, effect.Action);
        var card = HoverCards.Item(item, new CaptureNames(), gid => 2278, null);
        Assert.Contains("581", card.EffectsText, StringComparison.Ordinal);
        Assert.Contains("174", card.EffectsText, StringComparison.Ordinal);
        Assert.Equal("Prix moyen 2 278 kamas", card.PriceLine);
        Assert.Equal("Poids inconnu", card.WeightLine);
    }

    [Fact]
    public void Channel_update_uses_the_captured_field_numbers()
    {
        var recruit = ChatMessages.ReadUpdate(new[] { V(2, 6), V(3, 1) }, null);
        Assert.Equal(6, recruit.Channel);
        Assert.True(recruit.Enable);
        var general = ChatMessages.ReadUpdate(new[] { V(3, 1) }, null);
        Assert.Equal(0, general.Channel);
        Assert.True(general.Enable);
        Assert.Equal("Oméga 81", ProfileText.LevelText(281));
        Assert.Equal("Niveau 151", ProfileText.LevelText(151));
        Assert.Equal("1 000", HoverCards.Group(1000));
        Assert.Equal("2 278", HoverCards.Group(2278));
    }

    [FactIfGameData]
    public void Local_game_data_names_the_captured_ids()
    {
        using var db = GameDb.TryOpen(RealGameData.Path!);
        Assert.NotNull(db);
        var names = db!.SocialNames();
        Assert.Equal(22, names.JobIds().Count);
        Assert.DoesNotContain(1, names.JobIds());
        Assert.Contains("Steamer", names.Breed(15));
        Assert.Contains("lor", names.Breed(5));
        Assert.Contains("kmarien", names.AlignmentSideOfRank(31));
        Assert.Contains("Bontarien", names.AlignmentSideOfRank(8) ?? "");
        Assert.Contains("Traqueur", names.Title(166));
        Assert.Contains("Traqueuse", names.Title(166));
        Assert.Contains("cheron", names.Job(2));
        Assert.Contains("Pissenlit", names.Monster(79));
        Assert.Equal(2278, names.MonsterXp(79, 17));
        Assert.Contains("Cania", names.ItemName(23740));
        Assert.Equal(100, names.ItemLevel(23740));
        Assert.Contains("Fragment", names.ItemType(23740));
        Assert.Null(names.ItemSet(23740));
        Assert.Equal(1, names.ItemWeight(23740));
        Assert.StartsWith("Cette copie partielle", names.ItemDescription(23740));
        Assert.Null(names.ItemName(33585));

        var card = HoverCards.Item(23740, names, null, null, null);
        Assert.Contains("Cania", card.Name, StringComparison.Ordinal);
        Assert.Contains("Niveau 100", card.Subtitle, StringComparison.Ordinal);
        Assert.Contains("Fragment", card.Subtitle, StringComparison.Ordinal);
        Assert.Equal("Poids 1", card.WeightLine);
        Assert.StartsWith("Cette copie partielle", card.Description);
        Assert.True(card.MissingEffects);

        var group = new MonsterGroupCard(-1, null, null, null, 79, "1x79x17", new[] { new GroupMember(1, 79, 17) });
        var monster = HoverCards.Monster(group, names);
        Assert.Equal("XP 2 278", monster.XpLine);
        Assert.Contains("Pissenlit", monster.MembersText, StringComparison.Ordinal);
    }

    private static string? Code(string name) => Mapping.CodeForName(name);
    private static int? Field(string message, string name) => Mapping.FieldNumber(message, name);
    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes);

    private static IReadOnlyList<ProtoField> Payload(string hex)
    {
        var hits = RawProtobuf.FindTypeUrls(RawProtobuf.Parse(Convert.FromHexString(hex)));
        var hit = Assert.Single(hits);
        return hit.Payload ?? new List<ProtoField>();
    }

    private static ProtoField V(int field, long value) => new() { Field = field, Wire = WireType.Varint, Varint = unchecked((ulong)value) };
    private static ProtoField S(int field, string text) => new() { Field = field, Wire = WireType.Len, String = text };
    private static ProtoField M(int field, params ProtoField[] children) => new() { Field = field, Wire = WireType.Len, Message = new List<ProtoField>(children) };

    private static List<ProtoField> SarkahInfo()
    {
        var card = M(1,
            V(1, 166), V(2, SarkahId), V(3, 15), S(4, "Sarkah"), V(5, 281),
            M(6, V(1, 8419)),
            V(7, 92), V(8, 69752533), V(10, 6), V(11, 1), V(12, 22));
        return new List<ProtoField> { M(3, card, S(2, "2026-10-31T20:50:49.971244496Z")) };
    }

    private static List<ProtoField> SarkahView() => new() { M(2, SarkahSocial()) };

    private static SocialData SarkahSocialData()
    {
        SocialData? social = PlayerProfileReader.ReadView(SarkahView(), null).Social;
        Assert.NotNull(social);
        return social;
    }

    private static ProtoField SarkahSocial()
    {
        var children = new List<ProtoField>
        {
            M(2, V(2, 15818), S(3, "Les Matinaux")),
            M(3, V(1, 72), V(2, 4), V(3, 31)),
        };
        foreach (var (id, level) in SarkahJobs) children.Add(M(4, V(1, id), V(2, level)));
        children.Add(V(7, 12813));
        children.Add(M(8, S(2, "3ad2cad6-4e0a-4b63-b1aa-ef1a0eee5ad6"), S(4, "Allocations Familiales")));
        return new ProtoField { Field = 1, Wire = WireType.Len, Message = children };
    }

    private static readonly (int Id, int Level)[] SarkahJobs =
    {
        (64, 1), (65, 177), (2, 197), (36, 32), (41, 200), (74, 1), (11, 30), (44, 1), (13, 63), (15, 65),
        (79, 132), (16, 62), (48, 1), (24, 200), (26, 200), (27, 66), (28, 133), (60, 121), (62, 1), (63, 1),
    };

    private static List<ProtoField> PoussInfo()
    {
        var card = M(1,
            V(1, 147), V(2, PoussId), V(3, 5), S(4, "Pouss-Hier"), V(5, 151),
            V(7, 68), V(8, 188807160), V(10, 1), V(11, 1), V(12, 22));
        return new List<ProtoField> { M(3, card, S(2, "2026-10-31T20:52:04.596031942Z")) };
    }

    private static List<ProtoField> PoussView()
    {
        var children = new List<ProtoField> { M(3, V(1, 43), V(2, 3), V(3, 8)) };
        foreach (var (id, level) in PoussJobs) children.Add(M(4, V(1, id), V(2, level)));
        children.Add(V(7, 3900));
        var social = new ProtoField { Field = 1, Wire = WireType.Len, Message = children };
        return new List<ProtoField> { M(2, social) };
    }

    private static readonly (int Id, int Level)[] PoussJobs =
    {
        (64, 70), (65, 70), (2, 109), (36, 100), (41, 110), (74, 70), (11, 97), (44, 98), (13, 83), (15, 70),
        (79, 36), (16, 71), (48, 70), (24, 101), (26, 99), (27, 71), (28, 100), (60, 70), (62, 70), (63, 70),
    };

    private static List<ProtoField> PoussEquipment()
    {
        var children = new List<ProtoField>
        {
            M(2, V(1, 5), M(2, V(2, 1), V(3, 23479))),
            M(2, V(1, 23), M(2, V(2, 1), V(3, 32111))),
            M(2, V(1, 9), M(2, V(2, 1), V(3, 33532))),
            M(2, V(1, 10), M(2, V(2, 1), V(3, 33531))),
            M(2, V(1, 11), M(2, V(2, 1), V(3, 33534))),
            M(2, V(1, 12), M(2, V(2, 1), V(3, 33533))),
            Worn(0, 1240139, 33577, (48, 125), (21, 124), (15, 418), (6, 115), (2, 214), (1, 111), (1, 182)),
            WornRange(1, 3970283, 33585),
        };
        var card = new ProtoField { Field = 3, Wire = WireType.Len, Message = children };
        return new List<ProtoField> { M(2, card) };
    }

    private static ProtoField Worn(int position, long uid, long gid, params (int Value, int Action)[] effects)
    {
        var item = new List<ProtoField>();
        foreach (var (value, action) in effects) item.Add(M(1, V(5, value), V(10, action)));
        item.Add(V(3, uid));
        item.Add(V(4, gid));
        return M(3, V(1, position), new ProtoField { Field = 2, Wire = WireType.Len, Message = item });
    }

    private static ProtoField WornRange(int position, long uid, long gid)
    {
        var item = new List<ProtoField>
        {
            M(1, M(8, V(1, 25), V(3, 21)), V(10, 97)),
            M(1, M(8, V(1, 25), V(3, 21)), V(10, 99)),
            V(3, uid),
            V(4, gid),
        };
        return M(3, V(1, position), new ProtoField { Field = 2, Wire = WireType.Len, Message = item });
    }

    private static List<ProtoField> SaleEvent() => new()
    {
        V(1, 5),
        S(4, "Kheibar-Shekan"),
        M(6, M(1, V(3, 1), V(4, 23740), V(5, 4644892)), M(1, V(3, 1), V(4, 23741), V(5, 4485660))),
        V(8, 29438640482),
        S(9, "vend {chatitem,4644892,23740} {chatitem,4485660,23741} 2m5/u"),
    };

    private static List<ProtoField> PricedItemEvent() => new()
    {
        V(1, 5),
        S(4, "Brahmar"),
        M(6, M(1, V(3, 1), V(4, 27512), V(5, 1407294), M(7, V(5, 581), V(10, 174)))),
        S(9, "vente"),
    };

    private sealed class CaptureNames : ISocialNames
    {
        public string? Breed(int id) => id == 15 ? "Steamer" : id == 5 ? "Xélor" : null;
        public string? Job(int id) => id switch { 2 => "Bûcheron", 75 => "Parchomage", _ => null };
        public IReadOnlyList<int> JobIds() => new[] { 2, 75 };
        public string? Title(int id) => id == 166 ? "Traqueur Implacable / Traqueuse Implacable" : null;
        public string? Ornament(int id) => id == 92 ? "Score vénérable" : null;
        public string? AlignmentRank(int rankId) => null;
        public string? AlignmentSideOfRank(int rankId) => rankId == 31 ? "Brâkmarien" : rankId == 8 ? "Bontarien" : null;
        public string? ItemName(long gid) => null;
        public int ItemLevel(long gid) => -1;
        public string? ItemType(long gid) => null;
        public string? ItemSet(long gid) => null;
        public int ItemWeight(long gid) => -1;
        public string? ItemDescription(long gid) => null;
        public string? Monster(int id) => null;
        public long? MonsterXp(int monsterId, int level) => null;
    }

    private sealed class FixedXp : ISocialNames
    {
        private readonly long _xp;
        private readonly int _missing;
        public FixedXp(long xp, int missing = 0) { _xp = xp; _missing = missing; }
        public string? Breed(int id) => null;
        public string? Job(int id) => null;
        public IReadOnlyList<int> JobIds() => Array.Empty<int>();
        public string? Title(int id) => null;
        public string? Ornament(int id) => null;
        public string? AlignmentRank(int rankId) => null;
        public string? AlignmentSideOfRank(int rankId) => null;
        public string? ItemName(long gid) => null;
        public int ItemLevel(long gid) => -1;
        public string? ItemType(long gid) => null;
        public string? ItemSet(long gid) => null;
        public int ItemWeight(long gid) => -1;
        public string? ItemDescription(long gid) => null;
        public string? Monster(int id) => null;
        public long? MonsterXp(int monsterId, int level) => monsterId == _missing ? null : _xp;
    }
}
