using System;
using System.Collections.Generic;
using GivreCore.Protobuf;

namespace GivreClone.App.Services.Social;

public sealed record ProfileJob(int JobId, int Level);
public sealed record ProfileEffect(int Action, int Value, int Min, int Max);
public sealed record ProfileItem(int Position, long Gid, long Uid, bool Cosmetic, IReadOnlyList<ProfileEffect> Effects);

public sealed record CharacterHeader(
    long CharacterId,
    string Name,
    int Level,
    int BreedId,
    int TitleId,
    int OrnamentId,
    long AccountId,
    int JobCount,
    int? Wire10,
    int? Wire11,
    string? Timestamp);

public sealed record SocialData(
    string? GuildName,
    long? GuildId,
    string? AllianceName,
    int? AlignmentValue,
    int? AlignmentWire2,
    int? AlignmentRankId,
    int? Trophy,
    IReadOnlyList<ProfileJob> Jobs);

public sealed record EquipmentCardData(IReadOnlyList<ProfileItem> Cosmetic, IReadOnlyList<ProfileItem> Worn);

public sealed record CardViewBody(bool Failed, SocialData? Social, EquipmentCardData? Equipment);

public enum EquipmentState { Idle, Waiting, Hidden, Empty, Shown }

/// <summary>Mémoire d'une fiche. Le premier CardView sans équipement est l'onglet général. Le suivant, sans champ équipement, est l'onglet caché.</summary>
public sealed class ProfileMemory
{
    public CharacterHeader? Header { get; private set; }
    public SocialData? Social { get; private set; }
    public EquipmentCardData? Equipment { get; private set; }
    public bool EquipmentAsked { get; private set; }
    public bool GeneralReceived { get; private set; }
    public bool EquipmentReceived { get; private set; }
    public bool EquipmentFailed { get; private set; }

    public EquipmentState EquipmentState
    {
        get
        {
            if (!EquipmentAsked) return EquipmentState.Idle;
            if (!EquipmentReceived) return EquipmentState.Waiting;
            if (EquipmentFailed || Equipment is null) return EquipmentState.Hidden;
            if (Equipment.Cosmetic.Count == 0 && Equipment.Worn.Count == 0) return EquipmentState.Empty;
            return EquipmentState.Shown;
        }
    }

    public void Begin(bool askEquipment)
    {
        Header = null;
        Social = null;
        Equipment = null;
        EquipmentAsked = askEquipment;
        GeneralReceived = false;
        EquipmentReceived = false;
        EquipmentFailed = false;
    }

    public void ApplyInfo(CharacterHeader? header)
    {
        if (header is not null) Header = header;
    }

    public void ApplyView(CardViewBody body)
    {
        if (body.Failed)
        {
            if (GeneralReceived && EquipmentAsked)
            {
                EquipmentFailed = true;
                EquipmentReceived = true;
                Equipment = null;
            }
            return;
        }
        bool alreadyGeneral = GeneralReceived;
        if (body.Social is not null)
        {
            Social = body.Social;
            GeneralReceived = true;
        }
        if (body.Equipment is not null)
        {
            Equipment = body.Equipment;
            EquipmentReceived = true;
            EquipmentFailed = false;
            return;
        }
        if (alreadyGeneral && EquipmentAsked)
        {
            EquipmentReceived = true;
            Equipment = null;
            EquipmentFailed = false;
        }
    }
}

public static class PlayerProfileReader
{
    public static CharacterHeader? ReadInfo(IReadOnlyList<ProtoField> payload, Func<string, string, int?>? map)
    {
        int success = SocialFields.N(map, "CardInfoResponse", "success", 3);
        var body = SocialFields.Node(payload, success);
        if (body is null) return null;
        // Le corps de succès n'a pas de type nommé. Le journal (68304) met CharacterCardInformation en champ 1.
        var card = SocialFields.Node(body, 1);
        if (card is null) return null;
        int title = SocialFields.N(map, "CharacterCardInformation", "gabv", 1);
        int id = SocialFields.N(map, "CharacterCardInformation", "gabz", 2);
        int breed = SocialFields.N(map, "CharacterCardInformation", "gabu", 3);
        int name = SocialFields.N(map, "CharacterCardInformation", "gacb", 4);
        int level = SocialFields.N(map, "CharacterCardInformation", "gaca", 5);
        int ornament = SocialFields.N(map, "CharacterCardInformation", "gabx", 7);
        int account = SocialFields.N(map, "CharacterCardInformation", "gacf", 8);
        int wire10 = SocialFields.N(map, "CharacterCardInformation", "gacg", 10);
        int wire11 = SocialFields.N(map, "CharacterCardInformation", "gaci", 11);
        int jobs = SocialFields.N(map, "CharacterCardInformation", "gacc", 12);
        long? characterId = SocialFields.Var(card, id);
        if (characterId is null) return null;
        return new CharacterHeader(
            characterId.Value,
            SocialFields.Text(card, name) ?? "",
            Fit(SocialFields.Var(card, level)) ?? 0,
            Fit(SocialFields.Var(card, breed)) ?? 0,
            Fit(SocialFields.Var(card, title)) ?? 0,
            Fit(SocialFields.Var(card, ornament)) ?? 0,
            SocialFields.Var(card, account) ?? 0,
            Fit(SocialFields.Var(card, jobs)) ?? 0,
            Fit(SocialFields.Var(card, wire10)),
            Fit(SocialFields.Var(card, wire11)),
            SocialFields.Text(body, 2));
    }

    public static CardViewBody ReadView(IReadOnlyList<ProtoField> payload, Func<string, string, int?>? map)
    {
        int failed = SocialFields.N(map, "CardViewResponse", "failed", 1);
        int success = SocialFields.N(map, "CardViewResponse", "success", 2);
        if (SocialFields.Node(payload, success) is not { } body)
            return new CardViewBody(SocialFields.Node(payload, failed) is not null, null, null);
        // Champ 1 = SocialCard, champ 3 = EquipmentCard. Numéros vus en 68340 et 68624, absents comme noms du parent.
        var social = SocialFields.Node(body, 1);
        var equipment = SocialFields.Node(body, 3);
        return new CardViewBody(false, social is null ? null : ReadSocial(social, map), equipment is null ? null : ReadEquipment(equipment, map));
    }

    private static SocialData ReadSocial(IReadOnlyList<ProtoField> card, Func<string, string, int?>? map)
    {
        int guild = SocialFields.N(map, "SocialCard", "fzxp", 2);
        int alignment = SocialFields.N(map, "SocialCard", "fzxq", 3);
        int jobs = SocialFields.N(map, "SocialCard", "fzxs", 4);
        int trophy = SocialFields.N(map, "SocialCard", "fzxx", 7);
        int alliance = SocialFields.N(map, "SocialCard", "fzxv", 8);
        string? guildName = null;
        long? guildId = null;
        if (SocialFields.Node(card, guild) is { } guildNode)
        {
            guildId = SocialFields.Var(guildNode, SocialFields.N(map, "GuildInfo", "fzwn", 2));
            guildName = SocialFields.Text(guildNode, SocialFields.N(map, "GuildInfo", "fzwm", 3));
        }
        int? alignmentValue = null, alignmentWire2 = null, rankId = null;
        if (SocialFields.Node(card, alignment) is { } align)
        {
            alignmentValue = Fit(SocialFields.Var(align, SocialFields.N(map, "AlignmentLightInformation", "gbti", 1)));
            alignmentWire2 = Fit(SocialFields.Var(align, SocialFields.N(map, "AlignmentLightInformation", "gbtj", 2)));
            rankId = Fit(SocialFields.Var(align, SocialFields.N(map, "AlignmentLightInformation", "gbtl", 3)));
        }
        string? allianceName = null;
        if (SocialFields.Node(card, alliance) is { } allianceNode)
            allianceName = SocialFields.Text(allianceNode, SocialFields.N(map, "AllianceInfo", "fzwv", 4));
        var list = new List<ProfileJob>();
        foreach (var job in SocialFields.Nodes(card, jobs))
        {
            int? jobId = Fit(SocialFields.Var(job, 1));
            int? level = Fit(SocialFields.Var(job, 2));
            if (jobId is null || level is null) continue;
            list.Add(new ProfileJob(jobId.Value, level.Value));
        }
        return new SocialData(guildName, guildId, allianceName, alignmentValue, alignmentWire2, rankId, Fit(SocialFields.Var(card, trophy)), list);
    }

    private static EquipmentCardData ReadEquipment(IReadOnlyList<ProtoField> card, Func<string, string, int?>? map)
    {
        int cosmetic = SocialFields.N(map, "EquipmentCard", "fzyu", 2);
        int worn = SocialFields.N(map, "EquipmentCard", "fzyt", 3);
        return new EquipmentCardData(ReadSlots(card, cosmetic, cosmetic: true, map), ReadSlots(card, worn, cosmetic: false, map));
    }

    private static List<ProfileItem> ReadSlots(IReadOnlyList<ProtoField> card, int field, bool cosmetic, Func<string, string, int?>? map)
    {
        var list = new List<ProfileItem>();
        foreach (var slot in SocialFields.Nodes(card, field))
        {
            int? position = Fit(SocialFields.Var(slot, 1));
            var item = SocialFields.Node(slot, 2);
            if (position is null || item is null) continue;
            long gid = cosmetic
                ? SocialFields.Var(item, SocialFields.N(map, "Object", "object_gid", 3)) ?? 0
                : SocialFields.Var(item, SocialFields.N(map, "Equipment", "gid", 4)) ?? 0;
            if (gid == 0) continue;
            long uid = cosmetic ? 0 : SocialFields.Var(item, SocialFields.N(map, "Equipment", "uid", 3)) ?? 0;
            var effects = cosmetic ? new List<ProfileEffect>() : ReadEffects(item, map);
            list.Add(new ProfileItem(position.Value, gid, uid, cosmetic, effects));
        }
        return list;
    }

    private static List<ProfileEffect> ReadEffects(IReadOnlyList<ProtoField> item, Func<string, string, int?>? map)
    {
        int effects = SocialFields.N(map, "Equipment", "effects", 1);
        int value = SocialFields.N(map, "ObjectEffect", "value_int", 5);
        int minMax = SocialFields.N(map, "ObjectEffect", "min_max", 8);
        int action = SocialFields.N(map, "ObjectEffect", "action", 10);
        int min = SocialFields.N(map, "ObjectEffectMinMaxValue", "min", 1);
        int max = SocialFields.N(map, "ObjectEffectMinMaxValue", "max", 3);
        var list = new List<ProfileEffect>();
        foreach (var effect in SocialFields.Nodes(item, effects))
        {
            int? act = Fit(SocialFields.Var(effect, action));
            if (act is null or 0) continue;
            int amount = Fit(SocialFields.Var(effect, value)) ?? 0;
            int lo = 0, hi = 0;
            if (SocialFields.Node(effect, minMax) is { } range)
            {
                lo = Fit(SocialFields.Var(range, min)) ?? 0;
                hi = Fit(SocialFields.Var(range, max)) ?? 0;
            }
            list.Add(new ProfileEffect(act.Value, amount, lo, hi));
        }
        return list;
    }

    private static int? Fit(long? value)
        => value is null || value.Value is < int.MinValue or > int.MaxValue ? null : (int)value.Value;
}

public sealed class PlayerDirectory
{
    private readonly Dictionary<string, long> _ids = new(StringComparer.OrdinalIgnoreCase);

    public void Remember(string? name, long id)
    {
        if (string.IsNullOrWhiteSpace(name) || id <= 0) return;
        _ids[name.Trim()] = id;
    }

    public bool TryGet(string? name, out long id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(name)) return false;
        return _ids.TryGetValue(name.Trim(), out id);
    }

    public void Clear() => _ids.Clear();
}
