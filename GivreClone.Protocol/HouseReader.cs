using System;
using System.Collections.Generic;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>Une instance de maison (plusieurs propriétaires peuvent partager la même porte) : id d'instance à renvoyer
/// dans InteractiveUseRequest.specific_instance_id, propriétaire (pseudo + tag de compte) et verrouillage.</summary>
public sealed record HouseInstanceInfo(int InstanceId, string Nickname, string Tag, bool IsLocked);

/// <summary>Une maison posée sur la carte : id, element_id des portes, instances.</summary>
public sealed record HouseOnMapInfo(int HouseId, IReadOnlyList<int> DoorElementIds, IReadOnlyList<HouseInstanceInfo> Instances);

/// <summary>
/// Lit les MAISONS d'un MapComplementaryInformationEvent (Lot J : maisons à code). Tous les numéros de champ, IMBRIQUÉS
/// compris, sont résolus PAR NOM via <see cref="UseCatalog"/> ; les constantes ne sont qu'un repli (valeurs actuelles).
/// Types (stables) : houses = repeated House ; House = oneof (house_on_map | house_account | house_guild | house_inside) +
/// house_id ; HouseOnMap = { repeated HouseInstance houses_information, repeated int32 doors_on_map (PACKÉ) } ;
/// HouseInstance.instance_id int32, is_locked bool, account_tag = AccountTag{ tag, nickname }.
/// </summary>
public static class HouseReader
{
    private static Func<string, string, int?>? _fieldNumber;

    public static void UseCatalog(Func<string, string, int?> fieldNumber) => _fieldNumber = fieldNumber;

    private static int F(string message, string field) => _fieldNumber?.Invoke(message, field) ?? FieldMiss.Of(message, field);   // mapping streamé seul

    /// <summary><paramref name="tree"/> = payload du MapComplementaryInformationEvent. Maisons sans variante house_on_map
    /// (compte / guilde / intérieur) ignorées.</summary>
    public static IReadOnlyList<HouseOnMapInfo> Extract(IReadOnlyList<ProtoField> tree)
    {
        int fHouses = F("MapComplementaryInformationEvent", "houses");
        int fOnMap = F("House", "house_on_map"), fId = F("House", "house_id");
        int fDoors = F("HouseOnMap", "doors_on_map"), fInfos = F("HouseOnMap", "houses_information");
        int fInst = F("HouseInstance", "instance_id"), fLocked = F("HouseInstance", "is_locked"), fTag = F("HouseInstance", "account_tag");
        int fNick = F("AccountTag", "nickname"), fTagStr = F("AccountTag", "tag");

        var list = new List<HouseOnMapInfo>();
        foreach (var f in tree)
        {
            if (f.Field != fHouses || f.Message is not { } house) continue;
            var onMap = FirstMessage(house, fOnMap);
            if (onMap is null) continue;
            int houseId = (int)PayloadReader.Int(house, fId, 0);

            var doors = new List<int>();
            foreach (var d in onMap)
            {
                if (d.Field != fDoors) continue;
                if (d.Wire == WireType.Varint) { doors.Add(unchecked((int)(long)d.Varint)); continue; }   // élément unique non packé
                if (d.Wire == WireType.Len && d.Bytes is { } b)                                              // packé : Bytes toujours conservé
                {
                    int pos = 0;
                    while (pos < b.Length && RawProtobuf.TryReadVarint(b, ref pos, out ulong v)) doors.Add(unchecked((int)(long)v));
                }
            }

            var instances = new List<HouseInstanceInfo>();
            foreach (var i in onMap)
            {
                if (i.Field != fInfos || i.Message is not { } hi) continue;
                int instId = (int)PayloadReader.Int(hi, fInst, 0);
                bool locked = PayloadReader.Bool(hi, fLocked);   // absent = false (proto3)
                string nick = "", tag = "";
                if (FirstMessage(hi, fTag) is { } at)
                {
                    nick = PayloadReader.Str(at, fNick) ?? "";
                    tag = PayloadReader.Str(at, fTagStr) ?? "";
                }
                instances.Add(new HouseInstanceInfo(instId, nick, tag, locked));
            }
            list.Add(new HouseOnMapInfo(houseId, doors, instances));
        }
        return list;
    }

    /// <summary>Vrai si le perso est DANS une maison : la variante oneof in_house_information est PRÉSENTE (même vide).</summary>
    public static bool IsInHouse(IReadOnlyList<ProtoField> tree)
    {
        int f = F("MapComplementaryInformationEvent", "in_house_information");
        foreach (var x in tree) if (x.Field == f) return true;
        return false;
    }

    // Sous-message d'un champ Len. Un corps VIDE (ou deviné texte) n'a pas de Message : on retombe sur les octets bruts.
    private static IReadOnlyList<ProtoField>? FirstMessage(IReadOnlyList<ProtoField> node, int field)
    {
        foreach (var x in node)
        {
            if (x.Field != field || x.Wire != WireType.Len) continue;
            if (x.Message is { } m) return m;
            if (x.Bytes is { } b)
            {
                try { return RawProtobuf.Parse(b); } catch { return Array.Empty<ProtoField>(); }
            }
            return Array.Empty<ProtoField>();
        }
        return null;
    }
}
