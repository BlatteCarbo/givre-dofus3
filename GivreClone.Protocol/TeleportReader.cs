using System;
using System.Collections.Generic;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>
/// Lit un TeleportDestinationsEvent (liste des zaaps atteignables, envoyee a l'ouverture d'un zaap ou du havre-sac).
///
/// ⚠️ Trame réelle confirmée (capture wire capture_mitm_afk_20261017c, type.ankama.com/hgq) :
///     hgq  #1 spawn_map_id #2 type #4 msg { #2 cost ; #4 map_id ; #5 level ; #6 subarea_id } xN
/// soit { destinations = répété #4 ; type = #2 ; spawn_map_id = #1 }, chaque TeleportDestination portant son
/// map_id au champ #4 et son coût kamas au champ #2 (TeleportDestination.cost).
/// Les numéros de champs sont résolus par nom via <see cref="UseCatalog"/>.
/// </summary>
public static class TeleportReader
{
    private static Func<string, string, int?>? _fieldNumber;

    public static void UseCatalog(Func<string, string, int?> fieldNumber) => _fieldNumber = fieldNumber;

    private static int F(string message, string field) => _fieldNumber?.Invoke(message, field) ?? FieldMiss.Of(message, field);   // mapping streamé seul

    /// <summary>Retourne (map_id des destinations, spawn_map_id).</summary>
    public static (HashSet<long> Maps, long Spawn) ReadDestinations(IReadOnlyList<ProtoField> tree)
    {
        int fDest = F("TeleportDestinationsEvent", "destinations");
        int fSpawn = F("TeleportDestinationsEvent", "spawn_map_id");
        int fMap = F("TeleportDestination", "map_id");

        long spawn = (long)(tree.FirstOrDefault(x => x.Field == fSpawn && x.Wire == WireType.Varint)?.Varint ?? 0);
        var maps = new HashSet<long>();
        foreach (var f in tree)
        {
            if (f.Field != fDest || f.Message is not { } dest) continue;
            long m = (long)(dest.FirstOrDefault(x => x.Field == fMap && x.Wire == WireType.Varint)?.Varint ?? 0);
            if (m != 0) maps.Add(m);   // Coût kamas confirmé au champ #2 (TeleportDestination.cost) par capture wire (capture_mitm_afk_20261017c)
        }
        return (maps, spawn);
    }

    /// <summary>Une destination de la liste. <see cref="CostKnown"/> est faux si TeleportDestination.cost est absent
    /// (proto3 : un 0 lu par défaut n'est pas un coût annoncé). Le coût 0 annoncé reste connu.
    /// <see cref="TypeKnown"/> est faux si TeleportDestination.type est absent : un zaap omet ce champ, un zaapi envoie 1.</summary>
    public readonly record struct Destination(long MapId, long Cost, int SubAreaId, int Level, bool CostKnown = false, int Type = 0, bool TypeKnown = false);

    /// <summary>Destinations détaillées (ordre du paquet). Champs résolus PAR NOM ; un champ absent vaut 0 (proto3).</summary>
    public static IReadOnlyList<Destination> ReadDestinationDetails(IReadOnlyList<ProtoField> tree)
    {
        int fDest = F("TeleportDestinationsEvent", "destinations");
        int fMap = F("TeleportDestination", "map_id"), fCost = F("TeleportDestination", "cost");
        int fSub = F("TeleportDestination", "subarea_id"), fLvl = F("TeleportDestination", "level");
        int fKind = F("TeleportDestination", "type");
        long V(IReadOnlyList<ProtoField> d, int f) => (long)(d.FirstOrDefault(x => x.Field == f && x.Wire == WireType.Varint)?.Varint ?? 0);
        var list = new List<Destination>();
        var seen = new HashSet<long>();
        foreach (var f in tree)
        {
            if (f.Field != fDest || f.Message is not { } d) continue;
            long m = V(d, fMap);
            if (m == 0 || !seen.Add(m)) continue;
            bool costKnown = d.Any(x => x.Field == fCost && x.Wire == WireType.Varint);
            bool typeKnown = d.Any(x => x.Field == fKind && x.Wire == WireType.Varint);
            list.Add(new Destination(m, costKnown ? V(d, fCost) : 0, (int)V(d, fSub), (int)V(d, fLvl), costKnown,
                typeKnown ? (int)V(d, fKind) : 0, typeKnown));
        }
        return list;
    }

    /// <summary>Type de la liste (TeleportDestinationsEvent.type = Teleporter : 0 Zaap, 1 Subway/zaapi, 2 Prisme,
    /// 3 Havre-sac, 4 Anomalie ; capture : ouverture depuis le havre-sac = 3). 0 si absent (défaut proto3).</summary>
    public static int ReadListType(IReadOnlyList<ProtoField> tree)
    {
        int fType = F("TeleportDestinationsEvent", "type");
        return (int)(tree.FirstOrDefault(x => x.Field == fType && x.Wire == WireType.Varint)?.Varint ?? 0);
    }
}
