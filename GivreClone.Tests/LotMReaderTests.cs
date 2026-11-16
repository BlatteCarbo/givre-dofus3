using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Lecteurs du lot M sur des trames protobuf synthétiques (octets réels, décodés par RawProtobuf) : numéros de champ du mapping 3.7 candidat.</summary>
public class LotMReaderTests
{
    // ---- mini-encodeur protobuf ----
    private static byte[] Varint(ulong v) { var l = new List<byte>(); while (v >= 0x80) { l.Add((byte)(v | 0x80)); v >>= 7; } l.Add((byte)v); return l.ToArray(); }
    private static byte[] Tag(int field, int wire) => Varint((ulong)((field << 3) | wire));
    private static byte[] V(int field, long v) => Cat(Tag(field, 0), Varint((ulong)v));
    private static byte[] S(int field, string s) { var b = System.Text.Encoding.UTF8.GetBytes(s); return Cat(Tag(field, 2), Varint((ulong)b.Length), b); }
    private static byte[] M(int field, params byte[][] inner) { var b = Cat(inner); return Cat(Tag(field, 2), Varint((ulong)b.Length), b); }
    private static byte[] Cat(params byte[][] parts) => parts.SelectMany(x => x).ToArray();
    private static IReadOnlyList<ProtoField> P(params byte[][] parts) => RawProtobuf.Parse(Cat(parts));

    [Fact]
    public void Achieved_list_reads_ids_and_finish_levels_and_dedupes()
    {
        // AchievementsEvent { achieved_achievements = 1 : AchievedAchievement { achievement_id=1, achieved_by=2, finished_level=3 } }
        var list = LotMReader.AchievedList(P(M(1, V(1, 3), V(2, 99), V(3, 12)), M(1, V(1, 4), V(2, 99)), M(1, V(1, 3), V(2, 99), V(3, 15))));
        Assert.Equal(new[] { (3, 12), (4, 0) }, list.Select(a => (a.Id, a.FinishedLevel)));   // niveau absent = 0 ; doublon ignoré
    }

    [Fact]
    public void Achieved_list_ignores_non_message_entries_and_empty_events()
    {
        Assert.Empty(LotMReader.AchievedList(P()));
        Assert.Empty(LotMReader.AchievedList(P(V(1, 7))));   // varint nu : pas un AchievedAchievement
    }

    [Fact]
    public void Finished_event_carries_the_achievement_in_field_3()
    {
        var f = LotMReader.Finished(P(M(3, V(1, 42), V(2, 7), V(3, 130))))!;
        Assert.Equal((42, 130), (f.Id, f.FinishedLevel));
        Assert.Null(LotMReader.Finished(P(V(1, 5))));
    }

    [Fact]
    public void Reward_result_reads_id_and_success_and_a_missing_flag_is_a_failure()
    {
        Assert.Equal((42, true), LotMReader.RewardResult(P(V(1, 42), V(3, 1))));
        Assert.Equal((42, false), LotMReader.RewardResult(P(V(1, 42))));
    }

    [Fact]
    public void Anomaly_state_and_opened_and_subarea_list()
    {
        var st = LotMReader.AnomalyState(P(V(1, 1_800_000_000), V(2, 1), V(3, 77)));
        Assert.Equal((77, true, 1_800_000_000L), (st.SubareaId, st.Open, st.ClosingRaw));
        Assert.False(LotMReader.AnomalyState(P(V(3, 78))).Open);
        Assert.Equal(55, LotMReader.AnomalyOpenedSubarea(P(V(1, 55))));
        var subs = LotMReader.AnomalySubareas(P(M(1, V(1, 1), V(2, 25), V(3, 10), V(5, 1_800_000_500)), M(1, V(3, 11))));
        Assert.Equal(new[] { (10, true, 1_800_000_500L, 25), (11, false, 0L, 0) }, subs.Select(a => (a.SubareaId, a.Open, a.ClosingRaw, a.RewardRate)));
    }

    [Fact]
    public void Teleport_offers_by_kind()
    {
        var player = LotMReader.TeleportOffer("TeleportPlayerOfferEvent", P(V(1, 555), V(2, 20), V(3, 88213269)))!;
        Assert.Equal(("player", 555L, 88213269L, 20L), (player.Kind, player.RequesterId, player.MapId, player.TimeLeft));
        var group = LotMReader.TeleportOffer("GroupTeleportPlayerOfferEvent", P(V(3, 99), S(2, "Njalla"), V(4, 30), V(5, 777)))!;
        Assert.Equal(("group", "Njalla", 777L, 99L, 30L), (group.Kind, group.RequesterName, group.RequesterId, group.MapId, group.TimeLeft));
        var buddy = LotMReader.TeleportOffer("TeleportToBuddyOfferEvent", P(V(1, 12), V(4, 15), V(5, 321)))!;
        Assert.Equal(("buddy", 321L, 12), (buddy.Kind, buddy.RequesterId, buddy.DungeonId));
        var buddies = LotMReader.TeleportOffer("TeleportBuddiesRequestedEvent", P(V(2, 8), V(3, 654)))!;
        Assert.Equal(("buddies", 654L, 8), (buddies.Kind, buddies.RequesterId, buddies.DungeonId));
        Assert.Null(LotMReader.TeleportOffer("MapChangeEvent", P()));
        Assert.Null(LotMReader.TeleportOffer(null, P()));
    }

    [Fact]
    public void Closed_events_map_to_their_offer_kind()
    {
        Assert.Equal("player", LotMReader.TeleportClosedKind("TeleportPlayerClosedEvent"));
        Assert.Equal("group", LotMReader.TeleportClosedKind("GroupTeleportPlayerClosedEvent"));
        Assert.Equal("buddy", LotMReader.TeleportClosedKind("TeleportToBuddyClosedEvent"));
        Assert.Null(LotMReader.TeleportClosedKind("TeleportPlayerOfferEvent"));
    }

    [Fact]
    public void Npc_shop_reads_gid_price_and_criterion_from_the_nested_item()
    {
        // objects = 3 : ItemMinimalInformation { object_gid=2, quantity=3, item_to_sell_in_npc_shop=4 { price=1, buy_criterion=2 } }
        var (npc, items) = LotMReader.NpcShop(P(V(1, -20000), M(3, V(2, 311), M(4, V(1, 1200), S(2, "PL>=20"))), M(3, V(2, 312), M(4, V(1, 50))), M(3, V(2, 313))));
        Assert.Equal(-20000, npc);
        Assert.Equal(new[] { (311, 1200L, "PL>=20"), (312, 50L, ""), (313, 0L, "") }, items.Select(i => (i.Gid, i.Price, i.Criterion)));
    }

    [Fact]
    public void Price_history_points()
    {
        // objects_average_prices_history = 1 : { average_price=1, date=3, items_sold=4 }
        var h = LotMReader.PriceHistory(P(M(1, V(1, 1500), S(3, "2026-10-10"), V(4, 12)), M(1, V(1, 1620), S(3, "2026-10-11"), V(4, 7))));
        Assert.Equal(new[] { ("2026-10-10", 1500L, 12), ("2026-10-11", 1620L, 7) }, h.Select(x => (x.Date, x.Price, x.Sold)));
        Assert.Empty(LotMReader.PriceHistory(P()));
    }

    [Fact]
    public void Dungeon_exit_reads_the_mapped_field_and_requires_the_following_map()
    {
        var cat = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        int fyaz = cat.FieldNumber("DungeonExitResponse", "fyaz") ?? 0;
        Assert.True(fyaz >= 1);
        Assert.Equal(fyaz, LotMReader.DungeonExitFieldNumber());

        var success = LotMReader.DungeonExit(P(V(fyaz, (long)LotMReader.DungeonExitSuccessValue)), fyaz);
        Assert.Equal((true, ""), success);
        Assert.Equal((true, "", 192937992L), LotMReader.DungeonExitConfirmed(success, 123, 192937992));

        var refused = LotMReader.DungeonExit(P(V(fyaz, 3)), fyaz);
        Assert.Equal((false, "3"), refused);
        Assert.Equal((false, "3", 200L), LotMReader.DungeonExitConfirmed(refused, 100, 200));   // une autre carte ne suffit pas

        Assert.Equal((false, "pas dans un donjon"), LotMReader.DungeonExit(P(S(fyaz, "pas dans un donjon")), fyaz));
        Assert.Equal((false, "résultat absent"), LotMReader.DungeonExit(P(), fyaz));
        Assert.Equal((false, "résultat absent"), LotMReader.DungeonExit(P(V(fyaz == 1 ? 2 : 1, 1)), fyaz));   // un autre numéro n'est pas fyaz
        Assert.Equal((false, "champ de DungeonExitResponse absent du mapping"), LotMReader.DungeonExit(P(V(fyaz, 1)), -1));
        Assert.Equal((false, "la carte n'a pas changé", 100L), LotMReader.DungeonExitConfirmed(success, 100, 100));
        Assert.Equal((false, "la carte n'a pas changé", 100L), LotMReader.DungeonExitConfirmed(success, 100, 0));
    }

    private static bool AnyPayloadHasFields(IReadOnlyList<ProtoField> tree)
    {
        foreach (var f in tree)
        {
            if (f.Message is not { } m) continue;
            if (m.Any(x => x.String != null && x.String.StartsWith("type.ankama.com/", StringComparison.Ordinal))
                && m.Any(x => x.Field == 2 && x.Message is { Count: > 0 })) return true;
            if (AnyPayloadHasFields(m)) return true;
        }
        return false;
    }

    [Fact]
    public void Document_id()
    {
        Assert.Equal(17, LotMReader.DocumentId(P(V(1, 17), V(3, 1))));
        Assert.Equal(0, LotMReader.DocumentId(P()));
    }

    // ---- forges : les trames émises se relisent par les mêmes numéros de champ ----
    private static IReadOnlyList<ProtoField> Unframe(byte[] framed)
    {
        int pos = 0;
        Assert.True(RawProtobuf.TryReadVarint(framed, ref pos, out ulong len));
        Assert.Equal((int)len, framed.Length - pos);
        return RawProtobuf.Parse(framed[pos..]);
    }

    private static string? CodeOf(byte[] framed)
    {
        // GameMessage -> (slot enveloppe) -> Any -> type_url ; on cherche le type_url en profondeur, comme la lecture le fait.
        string? Walk(IReadOnlyList<ProtoField> l)
        {
            foreach (var f in l)
            {
                if (f.String is { } s && s.StartsWith("type.ankama.com/")) return s["type.ankama.com/".Length..];
                if (f.Message is { } m && Walk(m) is { } r) return r;
            }
            return null;
        }
        return Walk(Unframe(framed));
    }

    [Fact]
    public void Forged_requests_resolve_their_codes_and_fields_by_name()
    {
        var cat = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        Assert.Equal(cat.CodeForName("AchievementRewardRequest"), CodeOf(MoveForge.BuildAchievementRewardFramed(42)));
        var exit = MoveForge.BuildDungeonExitFramed();
        Assert.Equal(cat.CodeForName("DungeonExitRequest"), CodeOf(exit));
        Assert.False(AnyPayloadHasFields(Unframe(exit)));   // DungeonExitRequest : 0 champ
        Assert.Equal(cat.CodeForName("TeleportPlayerAnswerRequest"), CodeOf(MoveForge.BuildTeleportAnswerFramed("player", true)));
        Assert.Equal(cat.CodeForName("GroupTeleportPlayerAnswerRequest"), CodeOf(MoveForge.BuildTeleportAnswerFramed("group", false)));
        Assert.Equal(cat.CodeForName("TeleportToBuddyAnswerRequest"), CodeOf(MoveForge.BuildTeleportAnswerFramed("buddy", true)));
        Assert.Equal(cat.CodeForName("TeleportBuddiesAnswerRequest"), CodeOf(MoveForge.BuildTeleportAnswerFramed("buddies", true)));
        Assert.Empty(MoveForge.BuildTeleportAnswerFramed("inconnu", true));
    }
}
