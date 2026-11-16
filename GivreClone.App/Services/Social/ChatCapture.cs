using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using GivreCore.Protobuf;

namespace GivreClone.App.Services.Social;

public sealed record ChatEffect(int Action, int Value, int Min, int Max);
public sealed record ChatItemLink(long Uid, long Gid, int Quantity, IReadOnlyList<ChatEffect> Effects);
public sealed record GroupMember(int Count, int MonsterId, int Level);

public sealed record MonsterGroupCard(
    long GroupId,
    int? X,
    int? Y,
    int? Marker,
    int? MainMonsterId,
    string? MemberText,
    IReadOnlyList<GroupMember> Members);

public sealed record CapturedChat(
    int Channel,
    string Content,
    string Sender,
    long SenderCharacterId,
    long SenderAccountId,
    IReadOnlyList<ChatItemLink> Items,
    IReadOnlyList<MonsterGroupCard> Groups);

public sealed record ChannelToggle(int Channel, bool Enable);

public sealed record ParsedLink(string Kind, string Text, long A, long B, long C);

public static class ChatMessages
{
    public static CapturedChat ReadRequest(IReadOnlyList<ProtoField> payload, Func<string, string, int?>? map)
    {
        int channel = SocialFields.N(map, "ChatChannelMessageRequest", "channel", 2);
        int content = SocialFields.N(map, "ChatChannelMessageRequest", "content", 3);
        int metadata = SocialFields.N(map, "ChatChannelMessageRequest", "metadata", 4);
        return Read(payload, channel, content, metadata, sender: -1, senderId: -1, account: -1, map);
    }

    public static CapturedChat ReadEvent(IReadOnlyList<ProtoField> payload, Func<string, string, int?>? map)
    {
        int channel = SocialFields.N(map, "ChatChannelMessageEvent", "channel", 1);
        int sender = SocialFields.N(map, "ChatChannelMessageEvent", "sender_name", 4);
        int metadata = SocialFields.N(map, "ChatChannelMessageEvent", "metadata", 6);
        int account = SocialFields.N(map, "ChatChannelMessageEvent", "sender_account_id", 7);
        int senderId = SocialFields.N(map, "ChatChannelMessageEvent", "sender_character_id", 8);
        int content = SocialFields.N(map, "ChatChannelMessageEvent", "content", 9);
        return Read(payload, channel, content, metadata, sender, senderId, account, map);
    }

    public static ChannelToggle ReadUpdate(IReadOnlyList<ProtoField> payload, Func<string, string, int?>? map)
    {
        int channel = SocialFields.N(map, "ChannelUpdateEvent", "channel", 2);
        int enable = SocialFields.N(map, "ChannelUpdateEvent", "enable", 3);
        long? id = SocialFields.Var(payload, channel);
        long? on = SocialFields.Var(payload, enable);
        return new ChannelToggle(id is null ? 0 : (int)id.Value, on is > 0);
    }

    private static CapturedChat Read(IReadOnlyList<ProtoField> payload, int channelField, int contentField, int metadataField,
        int sender, int senderId, int account, Func<string, string, int?>? map)
    {
        long? channel = SocialFields.Var(payload, channelField);
        var meta = SocialFields.Node(payload, metadataField);
        var (items, groups) = meta is null ? (new List<ChatItemLink>(), new List<MonsterGroupCard>()) : ReadMetadata(meta, map);
        return new CapturedChat(
            channel is null ? 0 : (int)channel.Value,
            SocialFields.Text(payload, contentField) ?? "",
            sender < 0 ? "" : SocialFields.Text(payload, sender) ?? "",
            senderId < 0 ? 0 : SocialFields.Var(payload, senderId) ?? 0,
            account < 0 ? 0 : SocialFields.Var(payload, account) ?? 0,
            items,
            groups);
    }

    private static (List<ChatItemLink> Items, List<MonsterGroupCard> Groups) ReadMetadata(IReadOnlyList<ProtoField> metadata, Func<string, string, int?>? map)
    {
        var items = new List<ChatItemLink>();
        var groups = new List<MonsterGroupCard>();
        // Le conteneur de métadonnées n'a pas de nom. Le journal met ObjectItem en 1 (68967) et MonsterGroup en 2 (70125).
        foreach (var item in SocialFields.Nodes(metadata, 1))
            items.Add(ReadItem(item, map));
        foreach (var group in SocialFields.Nodes(metadata, 2))
            if (ReadGroup(group, map) is { } card) groups.Add(card);
        return (items, groups);
    }

    private static ChatItemLink ReadItem(IReadOnlyList<ProtoField> item, Func<string, string, int?>? map)
    {
        int quantity = SocialFields.N(map, "ObjectItem", "quantity", 3);
        int gid = SocialFields.N(map, "ObjectItem", "gid", 4);
        int uid = SocialFields.N(map, "ObjectItem", "uid", 5);
        int effects = SocialFields.N(map, "ObjectItem", "effects", 7);
        int value = SocialFields.N(map, "ObjectEffect", "value_int", 5);
        int minMax = SocialFields.N(map, "ObjectEffect", "min_max", 8);
        int action = SocialFields.N(map, "ObjectEffect", "action", 10);
        var list = new List<ChatEffect>();
        foreach (var effect in SocialFields.Nodes(item, effects))
        {
            int? act = Fit(SocialFields.Var(effect, action));
            if (act is null or 0) continue;
            int amount = Fit(SocialFields.Var(effect, value)) ?? 0;
            int lo = 0, hi = 0;
            if (SocialFields.Node(effect, minMax) is { } range)
            {
                lo = Fit(SocialFields.Var(range, SocialFields.N(map, "ObjectEffectMinMaxValue", "min", 1))) ?? 0;
                hi = Fit(SocialFields.Var(range, SocialFields.N(map, "ObjectEffectMinMaxValue", "max", 3))) ?? 0;
            }
            list.Add(new ChatEffect(act.Value, amount, lo, hi));
        }
        int count = Fit(SocialFields.Var(item, quantity)) ?? 1;
        return new ChatItemLink(SocialFields.Var(item, uid) ?? 0, SocialFields.Var(item, gid) ?? 0, count, list);
    }

    public static MonsterGroupCard? ReadGroup(IReadOnlyList<ProtoField> group, Func<string, string, int?>? map)
    {
        int coords = SocialFields.N(map, "MonsterGroup", "fzba", 2);
        int marker = SocialFields.N(map, "MonsterGroup", "fzbb", 3);
        int id = SocialFields.N(map, "MonsterGroup", "fzbf", 4);
        int members = SocialFields.N(map, "MonsterGroup", "fzbd", 6);
        int main = SocialFields.N(map, "MonsterGroup", "fzbe", 8);
        long? groupId = SocialFields.Var(group, id);
        if (groupId is null) return null;
        int? x = null, y = null;
        if (SocialFields.Node(group, coords) is { } cell)
        {
            y = Fit(SocialFields.Var(cell, SocialFields.N(map, "MapCoordinates", "world_y", 1)));
            x = Fit(SocialFields.Var(cell, SocialFields.N(map, "MapCoordinates", "world_x", 3)));
        }
        string? text = SocialFields.Text(group, members);
        return new MonsterGroupCard(groupId.Value, x, y, Fit(SocialFields.Var(group, marker)), Fit(SocialFields.Var(group, main)), text, ParseMembers(text));
    }

    public static IReadOnlyList<GroupMember> ParseMembers(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<GroupMember>();
        var list = new List<GroupMember>();
        foreach (var part in raw.Split('|'))
        {
            var bits = part.Split('x');
            if (bits.Length != 3) continue;
            if (int.TryParse(bits[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
                && int.TryParse(bits[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int monster)
                && int.TryParse(bits[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int level)
                && count > 0 && monster > 0 && level >= 0)
                list.Add(new GroupMember(count, monster, level));
        }
        return list;
    }

    public static int? TotalLevel(MonsterGroupCard group)
    {
        if (string.IsNullOrWhiteSpace(group.MemberText) || group.Members.Count == 0) return null;
        int segments = 0;
        foreach (var part in group.MemberText.Split('|'))
            if (part.Length > 0) segments++;
        if (segments != group.Members.Count) return null;
        int sum = 0;
        foreach (var member in group.Members) sum += member.Count * member.Level;
        return sum;
    }

    public static long? TotalXp(MonsterGroupCard group, ISocialNames names)
    {
        if (TotalLevel(group) is null) return null;
        long sum = 0;
        foreach (var member in group.Members)
        {
            long? xp = names.MonsterXp(member.MonsterId, member.Level);
            if (xp is null) return null;
            sum += xp.Value * member.Count;
        }
        return sum;
    }

    private static int? Fit(long? value)
        => value is null || value.Value is < int.MinValue or > int.MaxValue ? null : (int)value.Value;
}

public static class ChatLinks
{
    private static readonly Regex Player = new(@"\{playerCard,([^{},]+),(-?\d+)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Item = new(@"\{chatitem,(-?\d+),(-?\d+)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MonsterPair = new(@"\{monsterGroupBestiary,(-?\d+)\}\s*\{localizeMonsterGroup,\1\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MonsterOne = new(@"\{(?:monsterGroupBestiary|localizeMonsterGroup),(-?\d+)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Map = new(@"\{\{map,(-?\d+),(-?\d+),(-?\d+)\}\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Guild = new(@"\{guild,\d+,([^{},]*)(?:,[^{}]*)?\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<ParsedLink> Links(string? content)
    {
        var list = new List<ParsedLink>();
        if (string.IsNullOrEmpty(content)) return list;
        string text = WebUtility.HtmlDecode(content);
        var covered = new List<(int Start, int End)>();
        foreach (Match match in MonsterPair.Matches(text))
        {
            if (!long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)) continue;
            list.Add(new ParsedLink("monster", "", id, 0, 0));
            covered.Add((match.Index, match.Index + match.Length));
        }
        foreach (Match match in MonsterOne.Matches(text))
        {
            if (covered.Exists(span => match.Index >= span.Start && match.Index < span.End)) continue;
            if (long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
                list.Add(new ParsedLink("monster", "", id, 0, 0));
        }
        foreach (Match match in Player.Matches(text))
            if (long.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
                list.Add(new ParsedLink("player", match.Groups[1].Value, id, 0, 0));
        foreach (Match match in Item.Matches(text))
            if (long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long uid)
                && long.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long gid))
                list.Add(new ParsedLink("item", "", uid, gid, 0));
        foreach (Match match in Map.Matches(text))
            if (long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long x)
                && long.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long y)
                && long.TryParse(match.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long world))
                list.Add(new ParsedLink("map", "", x, y, world));
        return list;
    }

    public static string Readable(string? content, Func<long, string?>? itemName)
    {
        if (string.IsNullOrEmpty(content)) return "";
        string text = WebUtility.HtmlDecode(content);
        text = MonsterPair.Replace(text, "groupe");
        text = MonsterOne.Replace(text, "groupe");
        text = Item.Replace(text, match =>
        {
            if (!long.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long gid))
                return "objet";
            string? name = itemName?.Invoke(gid);
            return string.IsNullOrWhiteSpace(name) ? "objet" : name!;
        });
        text = Player.Replace(text, match => match.Groups[1].Value);
        text = Guild.Replace(text, match => match.Groups[1].Value);
        text = Map.Replace(text, match => "[" + match.Groups[1].Value + ", " + match.Groups[2].Value + "]");
        return text;
    }
}
