using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GivreClone.App.Services;
using GivreClone.App.Services.LotM;

namespace GivreClone.App.Services.Social;

public sealed record ProfileRow(string Label, string Value);

public sealed record EquipmentRow(string Place, ItemHoverModel Card);

public sealed record ItemHoverModel(
    string Name,
    string Subtitle,
    string IdLine,
    string SetLine,
    string EffectsText,
    string WeightLine,
    string PriceLine,
    string Description)
{
    public bool HasSet => SetLine.Length > 0;
    public bool HasEffects => EffectsText.Length > 0;
    public bool MissingEffects => EffectsText.Length == 0;
    public bool HasDescription => Description.Length > 0;
}

public sealed record MonsterHoverModel(string Title, string LevelLine, string XpLine, string MapLine, string MembersText)
{
    public bool HasMap => MapLine.Length > 0;
    public bool HasMembers => MembersText.Length > 0;
}

public static class ProfileText
{
    public const string HiddenTab = "Le joueur ne souhaite pas afficher cet onglet.";
    public const string EmptyTab = "Aucun objet affiché.";
    public const string WaitingTab = "Équipement pas encore reçu.";
    public const string IdleTab = "Demandez un profil pour charger l'équipement.";
    public const string NameNotSeen = "Ce nom n'a pas encore été vu. Le jeu demande le profil par identifiant.";
    public const string FrameReady = "Trame prête. Le parent doit l'injecter.";
    public const string ProfileSent = "Demande de profil envoyée.";
    public const string ProfileMissing = "Le profil n'a pas été renvoyé.";

    public static string LevelText(int level)
        => level > 200
            ? "Oméga " + (level - 200).ToString(CultureInfo.InvariantCulture)
            : "Niveau " + level.ToString(CultureInfo.InvariantCulture);

    public static string Note(EquipmentState state) => state switch
    {
        EquipmentState.Hidden => HiddenTab,
        EquipmentState.Empty => EmptyTab,
        EquipmentState.Waiting => WaitingTab,
        EquipmentState.Idle => IdleTab,
        _ => "",
    };

    public static IReadOnlyList<ProfileRow> General(CharacterHeader? header, SocialData? social, ISocialNames names)
    {
        var rows = new List<ProfileRow>();
        if (header is not null)
        {
            AddNamed(rows, "Titre", header.TitleId, names.Title(header.TitleId));
            AddNamed(rows, "Ornement", header.OrnamentId, names.Ornament(header.OrnamentId));
            if (header.Level > 0) rows.Add(new ProfileRow("Niveau", LevelText(header.Level)));
            AddNamed(rows, "Classe", header.BreedId, names.Breed(header.BreedId));
        }
        if (social is not null)
        {
            if (!string.IsNullOrWhiteSpace(social.GuildName)) rows.Add(new ProfileRow("Guilde", social.GuildName));
            if (!string.IsNullOrWhiteSpace(social.AllianceName)) rows.Add(new ProfileRow("Alliance", social.AllianceName));
            if (social.AlignmentValue is int value)
            {
                string side = social.AlignmentRankId is int rank ? names.AlignmentSideOfRank(rank) ?? "" : "";
                string text = (side + " " + value.ToString(CultureInfo.InvariantCulture)).Trim();
                if (text.Length > 0) rows.Add(new ProfileRow("Alignement", text));
            }
            if (social.Trophy is int trophy) rows.Add(new ProfileRow("Trophées", trophy.ToString(CultureInfo.InvariantCulture)));
            rows.AddRange(Jobs(social.Jobs, names));
        }
        return rows;
    }

    public static IReadOnlyList<ProfileRow> Jobs(IReadOnlyList<ProfileJob> packet, ISocialNames names)
    {
        var levels = new Dictionary<int, int>();
        var extras = new List<int>();
        foreach (var job in packet)
        {
            if (!levels.ContainsKey(job.JobId)) extras.Add(job.JobId);
            levels[job.JobId] = job.Level;
        }
        var rows = new List<ProfileRow>();
        var seen = new HashSet<int>();
        foreach (int id in names.JobIds())
        {
            seen.Add(id);
            int level = levels.TryGetValue(id, out int known) ? known : 0;
            rows.Add(new ProfileRow(names.Job(id) ?? id.ToString(CultureInfo.InvariantCulture), level.ToString(CultureInfo.InvariantCulture)));
        }
        foreach (int id in extras)
        {
            if (seen.Contains(id)) continue;
            rows.Add(new ProfileRow(names.Job(id) ?? id.ToString(CultureInfo.InvariantCulture), levels[id].ToString(CultureInfo.InvariantCulture)));
        }
        return rows;
    }

    public static IReadOnlyList<EquipmentRow> Equipment(EquipmentCardData card, ISocialNames names, Func<long, long?>? price, Func<int, string?>? effectTemplate)
    {
        var rows = new List<EquipmentRow>();
        foreach (var item in card.Cosmetic)
            rows.Add(new EquipmentRow("Apparat · position " + item.Position.ToString(CultureInfo.InvariantCulture), HoverCards.Item(item, names, price, effectTemplate)));
        foreach (var item in card.Worn)
            rows.Add(new EquipmentRow("Équipement · position " + item.Position.ToString(CultureInfo.InvariantCulture), HoverCards.Item(item, names, price, effectTemplate)));
        return rows;
    }

    private static void AddNamed(List<ProfileRow> rows, string label, int id, string? name)
    {
        if (id <= 0 && string.IsNullOrWhiteSpace(name)) return;
        rows.Add(new ProfileRow(label, string.IsNullOrWhiteSpace(name) ? id.ToString(CultureInfo.InvariantCulture) : name!));
    }
}

public static class HoverCards
{
    public static ItemHoverModel Item(long gid, ISocialNames names, long? averagePrice, IReadOnlyList<ProfileEffect>? effects, Func<int, string?>? effectTemplate)
        => Build(gid, 0, names, averagePrice, effects, effectTemplate);

    public static ItemHoverModel Item(ProfileItem item, ISocialNames names, Func<long, long?>? price, Func<int, string?>? effectTemplate)
        => Build(item.Gid, item.Uid, names, price?.Invoke(item.Gid), item.Effects, effectTemplate);

    public static ItemHoverModel Item(ChatItemLink link, ISocialNames names, Func<long, long?>? price, Func<int, string?>? effectTemplate)
    {
        var effects = new List<ProfileEffect>(link.Effects.Count);
        foreach (var effect in link.Effects) effects.Add(new ProfileEffect(effect.Action, effect.Value, effect.Min, effect.Max));
        var card = Build(link.Gid, link.Uid, names, price?.Invoke(link.Gid), effects, effectTemplate);
        if (link.Quantity <= 1) return card;
        string subtitle = card.Subtitle.Length == 0
            ? "Quantité " + link.Quantity.ToString(CultureInfo.InvariantCulture)
            : card.Subtitle + " · Quantité " + link.Quantity.ToString(CultureInfo.InvariantCulture);
        return card with { Subtitle = subtitle };
    }

    public static MonsterHoverModel Monster(MonsterGroupCard group, ISocialNames names)
    {
        int? level = ChatMessages.TotalLevel(group);
        long? xp = ChatMessages.TotalXp(group, names);
        var members = new StringBuilder();
        foreach (var member in group.Members)
        {
            if (members.Length > 0) members.Append('\n');
            string name = names.Monster(member.MonsterId) ?? ("Id " + member.MonsterId.ToString(CultureInfo.InvariantCulture));
            members.Append('×').Append(member.Count.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(name)
                .Append(" (niveau ").Append(member.Level.ToString(CultureInfo.InvariantCulture)).Append(')');
        }
        string map = group.X is int x && group.Y is int y
            ? "Carte " + x.ToString(CultureInfo.InvariantCulture) + ", " + y.ToString(CultureInfo.InvariantCulture)
            : "";
        return new MonsterHoverModel(
            "Groupe",
            level is int total ? "Niveau " + total.ToString(CultureInfo.InvariantCulture) : "Niveau inconnu",
            xp is long points ? "XP " + Group(points) : "XP inconnue",
            map,
            members.ToString());
    }

    public static string Group(long value)
    {
        bool negative = value < 0;
        string digits = Math.Abs(value).ToString(CultureInfo.InvariantCulture);
        var text = new StringBuilder();
        if (negative) text.Append('-');
        int head = digits.Length % 3;
        if (head == 0) head = 3;
        text.Append(digits, 0, head);
        for (int i = head; i < digits.Length; i += 3)
        {
            text.Append(' ');
            text.Append(digits, i, 3);
        }
        return text.ToString();
    }

    private static ItemHoverModel Build(long gid, long uid, ISocialNames names, long? averagePrice, IReadOnlyList<ProfileEffect>? effects, Func<int, string?>? effectTemplate)
    {
        string? name = names.ItemName(gid);
        int level = names.ItemLevel(gid);
        string? type = names.ItemType(gid);
        string? set = names.ItemSet(gid);
        int weight = names.ItemWeight(gid);
        var subtitle = new StringBuilder();
        if (level >= 0)
            subtitle.Append("Niveau ").Append(level.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(type))
        {
            if (subtitle.Length > 0) subtitle.Append(" · ");
            subtitle.Append(type);
        }
        var effectText = new StringBuilder();
        if (effects is not null)
        {
            foreach (var effect in effects)
            {
                if (effectText.Length > 0) effectText.Append('\n');
                effectText.Append(ItemEffectText.Format(effect.Action, effectTemplate?.Invoke(effect.Action), ItemEffectText.Params(effect.Value, effect.Min, effect.Max, 0, null)));
            }
        }
        string id = "Id " + gid.ToString(CultureInfo.InvariantCulture);
        if (uid > 0) id += " · Uid " + uid.ToString(CultureInfo.InvariantCulture);
        return new ItemHoverModel(
            string.IsNullOrWhiteSpace(name) ? "Objet " + gid.ToString(CultureInfo.InvariantCulture) : name!,
            subtitle.ToString(),
            id,
            string.IsNullOrWhiteSpace(set) ? "" : set!,
            effectText.ToString(),
            weight < 0 ? "Poids inconnu" : "Poids " + Group(weight),
            averagePrice is long price ? "Prix moyen " + Group(price) + " kamas" : "Prix moyen inconnu",
            HtmlText.Strip(names.ItemDescription(gid) ?? ""));
    }
}
