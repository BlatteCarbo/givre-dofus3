using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>Apparence d'une entité : squelette (bones), peaux (skins = tête, corps, objets portés…), couleurs indexées (index &lt;&lt; 24 | rgb) et échelles en %.</summary>
public sealed record EntitySubLookInfo(int BindingPointCategory, int BindingPointIndex, EntityLookInfo Look);

public sealed record EntityLookInfo(int BonesId, IReadOnlyList<int> Skins, IReadOnlyList<(int Index, int Rgb)> Colors,
    IReadOnlyList<int> Scales, IReadOnlyList<EntitySubLookInfo>? SubEntities = null)
{
    /// <summary>Extrait le look du personnage réel si l'entité actuelle est une monture/montilier portant un cavalier.</summary>
    public EntityLookInfo CharacterLook
    {
        get
        {
            if (SubEntities is { Count: > 0 })
            {
                var rider = SubEntities.FirstOrDefault(s => s.BindingPointCategory == 2 || s.BindingPointCategory == 7);
                if (rider?.Look is { } rLook && rLook.BonesId > 0) return rLook;
            }
            return this;
        }
    }

    /// <summary>Chaîne de look au format historique <c>{bones|skin,skin|index=rgb,…|scale}</c> que comprend le moteur de rendu public d'Ankama.</summary>
    public string ToLookString()
    {
        var inv = CultureInfo.InvariantCulture;
        string skins = string.Join(",", Skins.Select(s => s.ToString(inv)));
        string colors = string.Join(",", Colors.Select(c => c.Index.ToString(inv) + "=" + c.Rgb.ToString(inv)));
        string scale = Scales.Count > 0 ? Scales[0].ToString(inv) : "100";
        string children = SubEntities is { Count: > 0 }
            ? "|" + string.Join(",", SubEntities.Select(s => s.BindingPointCategory.ToString(inv)
                + "@" + s.BindingPointIndex.ToString(inv) + "=" + s.Look.ToLookString())) : "";
        return "{" + (BonesId == 0 ? 1 : BonesId).ToString(inv) + "|" + skins + "|" + colors + "|" + scale + children + "}";
    }
}

/// <summary>Lit un EntityLook (apparence) dans un arbre protobuf décodé, sans numéro de champ figé : tout passe par le catalogue (<c>fieldNumber(message, champ)</c>). Pur.</summary>
public static class EntityLookReader
{
    /// <summary>Un EntityLook (message déjà isolé) → apparence ; null s'il n'a ni squelette ni peau.</summary>
    public static EntityLookInfo? Parse(IReadOnlyList<ProtoField> look, Func<string, string, int?> fieldNumber)
        => ParseNode(look, fieldNumber, 0);

    public static EntityLookInfo? ReadMapSelf(IReadOnlyList<ProtoField> payload, int actorsField,
        long characterId, Func<string, string, int?> fieldNumber)
    {
        if (characterId == 0
            || fieldNumber("ActorPositionInformation", "actor_id") is not int idF
            || fieldNumber("ActorPositionInformation", "actor_information") is not int infoF
            || fieldNumber("ActorInformation", "look") is not int lookF) return null;
        foreach (var entry in payload)
        {
            if (entry.Field != actorsField || entry.Message is not { } actor
                || PayloadReader.Int(actor, idF, 0) != characterId) continue;
            var info = actor.FirstOrDefault(f => f.Field == infoF && f.Message is not null)?.Message;
            var look = info?.FirstOrDefault(f => f.Field == lookF && f.Message is not null)?.Message;
            return look is null ? null : Parse(look, fieldNumber);
        }
        return null;
    }

    private static EntityLookInfo? ParseNode(IReadOnlyList<ProtoField> look,
        Func<string, string, int?> fieldNumber, int depth)
    {
        if (depth > 16) return null;
        if (fieldNumber("EntityLook", "bones_id") is not int bonesF) return null;
        int skinsF = fieldNumber("EntityLook", "skins") ?? -1, colorsF = fieldNumber("EntityLook", "indexed_colors") ?? -1, scalesF = fieldNumber("EntityLook", "scales") ?? -1;
        int bones = (int)PayloadReader.Int(look, bonesF, 0);
        var skins = Ints(look, skinsF);
        if (bones == 0 && skins.Count == 0) return null;
        var colors = Ints(look, colorsF).Select(v => ((int)((uint)v >> 24), v & 0xFFFFFF)).ToList();
        List<EntitySubLookInfo>? children = null;
        if (fieldNumber("EntityLook", "sub_entities") is int subF
            && fieldNumber("SubEntityInformation", "binding_point_category") is int categoryF
            && fieldNumber("SubEntityInformation", "binding_point_index") is int indexF
            && fieldNumber("SubEntityInformation", "sub_entity_look") is int childF)
            foreach (var sub in look.Where(f => f.Field == subF && f.Message is not null))
            {
                var fields = sub.Message!;
                var node = fields.FirstOrDefault(f => f.Field == childF && f.Message is not null)?.Message;
                if (node is null || ParseNode(node, fieldNumber, depth + 1) is not { } child) continue;
                (children ??= new()).Add(new((int)PayloadReader.Int(fields, categoryF, 0),
                    (int)PayloadReader.Int(fields, indexF, 0), child));
            }
        return new EntityLookInfo(bones, skins, colors, Ints(look, scalesF), children);
    }

    /// <summary>ContextRefreshEntityLookEvent : (id de l'entité, nouveau look). Émis quand un équipement / un skin change.</summary>
    public static (long EntityId, EntityLookInfo Look)? ReadRefresh(IReadOnlyList<ProtoField> payload, Func<string, string, int?> fieldNumber)
    {
        if (fieldNumber("ContextRefreshEntityLookEvent", "entity_id") is not int idF || fieldNumber("ContextRefreshEntityLookEvent", "look") is not int lookF) return null;
        var node = payload.FirstOrDefault(f => f.Field == lookF && f.Message is not null)?.Message;
        if (node is null || Parse(node, fieldNumber) is not { } look) return null;
        return (PayloadReader.Int(payload, idF, 0), look);
    }

    /// <summary>Liste des personnages (CharacterListEvent) : apparence de chacun, par identifiant de personnage.</summary>
    public static IReadOnlyDictionary<long, EntityLookInfo> ReadCharacterList(IReadOnlyList<ProtoField> tree, Func<string, string, int?> fieldNumber)
    {
        var res = new Dictionary<long, EntityLookInfo>();
        if (fieldNumber("CharacterListEvent", "characters") is not int chars || fieldNumber("Character", "character_basic_information") is not int basicF
            || fieldNumber("Character", "id") is not int idF || fieldNumber("CharacterBasicInformation", "character_look") is not int clF
            || fieldNumber("CharacterLook", "look") is not int lookF) return res;
        foreach (var entry in tree)
        {
            if (entry.Field != chars || entry.Message is not { } wrap) continue;
            long id = PayloadReader.Int(wrap, idF, 0);
            var basic = wrap.FirstOrDefault(f => f.Field == basicF && f.Message is not null)?.Message;
            var cl = basic?.FirstOrDefault(f => f.Field == clF && f.Message is not null)?.Message;
            var node = cl?.FirstOrDefault(f => f.Field == lookF && f.Message is not null)?.Message;
            if (id != 0 && node is not null && Parse(node, fieldNumber) is { } look) res[id] = look;
        }
        return res;
    }

    // repeated int : une entrée Varint par valeur OU un champ Len « packed ».
    private static List<int> Ints(IReadOnlyList<ProtoField> msg, int field)
    {
        var list = new List<int>();
        if (field < 0) return list;
        foreach (var x in msg)
        {
            if (x.Field != field) continue;
            if (x.Wire == WireType.Varint) list.Add(unchecked((int)(long)x.Varint));
            else if (x.Wire == WireType.Len && x.Bytes is { Length: > 0 } b) foreach (var v in Varints(b)) list.Add(unchecked((int)(long)v));
        }
        return list;
    }

    private static IEnumerable<ulong> Varints(byte[] b)
    {
        int i = 0;
        while (i < b.Length)
        {
            ulong v = 0; int shift = 0;
            while (i < b.Length)
            {
                byte x = b[i++];
                v |= (ulong)(x & 0x7F) << shift;
                if ((x & 0x80) == 0) break;
                shift += 7;
            }
            yield return v;
        }
    }
}
