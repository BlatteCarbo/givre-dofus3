using System;
using System.Collections.Generic;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

/// <summary>Décision d'un zaapi ouvert. <see cref="CostKamas"/> vient de TeleportDestination.cost, ou null si le champ était absent.</summary>
public readonly record struct ZaapiOffer(bool Listed, long? CostKamas, bool CanSend, string Reason);

/// <summary>Destination retenue, ou le premier refus s'il n'y a rien à envoyer.</summary>
public readonly record struct ZaapiPick(long MapId, int HopsAfter, long? CostKamas, bool CanSend, string Reason);

/// <summary>Indice de carte (HintData) utilisé seulement pour nommer une destination que le serveur a déjà envoyée.</summary>
public readonly record struct ZaapiHint(long MapId, string Name, int CategoryId, string Category, int X, int Y);

/// <summary>Une ligne du zaapi : carte envoyée par le serveur, libellés lus dans les données du jeu pour cette carte.</summary>
public readonly record struct ZaapiStop(
    long MapId, string Name, string District, int? X, int? Y, long? CostKamas,
    int CategoryId, string Category, int SubAreaId);

/// <summary>
/// Zaapi de la carte courante. La liste est TeleportDestinationsEvent type 1 : les cartes, le coût et la sous-zone
/// sont ceux du paquet. Le nom, le quartier, les coordonnées et l'onglet viennent des indices (HintData) de CES cartes.
/// Aucune ville n'est tablée ici : La Bourgade et Bonta n'ont pas les mêmes destinations.
/// Capture du 31/10 : InteractiveUseRequest sur l'élément Zaapi, puis TeleportRequest map_id + source_type 1 + destination_type 1.
/// Les numéros de champ viennent du mapping (map_id, source_type, destination_type, skill_instance_uid, element_id).
/// </summary>
public static class ZaapiTravel
{
    /// <summary>TeleportDestinationsEvent.type. 1 = zaapi.</summary>
    public const int ListType = 1;

    /// <summary>TeleportRequest.source_type. Capture : champ 2 = 1.</summary>
    public const int SourceType = 1;

    /// <summary>TeleportRequest.destination_type. Capture : champ 4 = 1.</summary>
    public const int DestinationType = 1;

    /// <summary>ElementTypeId de l'objet « Zaapi » sur la carte.</summary>
    public const int ElementTypeId = 106;

    /// <summary>Skill d'utilisation du zaapi, envoyé comme skill_instance_uid de CE skill.</summary>
    public const int SkillId = 157;

    public const string NotHereReason = "Aucun zaapi sur cette carte.";
    public const string OpenFailedReason = "Zaapi : ouverture refusée.";
    public const string ListMissingReason = "Zaapi : liste des destinations non reçue.";
    public const string UnknownReason = "Destination absente du zaapi ouvert.";
    public const string UnknownListReason = "Aucun zaapi ouvert pour l'instant.";
    public const string UnknownCostReason = "Coût en kamas inconnu.";
    public const string UnknownKamasReason = "Kamas du personnage inconnus.";
    public const string NoShortcutReason = "Zaapi : aucune destination de cette ville ne raccourcit ce trajet.";
    public const string CombatReason = "Zaapi impossible en combat.";
    public const string SendFailedReason = "Zaapi : envoi refusé.";

    public static string InsufficientReason(long have, long cost)
        => $"Kamas insuffisants : {have} en poche, {cost} requis (coût annoncé par le zaapi).";

    public static string ReadyReason(long mapId, long cost)
        => $"Zaapi vers {mapId} ({cost} kamas, coût annoncé).";

    public static string UnconfirmedReason(long mapId)
        => $"Zaapi vers {mapId} non confirmé : la carte n'a pas changé.";

    public static string ArrivedReason(long mapId, long cost)
        => $"Zaapi : arrivée sur la carte {mapId} ({cost} kamas).";

    /// <summary>
    /// Onglets de la fenêtre zaapi : ids HintCategoryData (hôtels de vente, ateliers, divers dans les données actuelles).
    /// Le texte de l'onglet est le nom de la catégorie, pas ces numéros.
    /// </summary>
    public static bool IsTabCategory(int categoryId) => categoryId is 2 or 3 or 4;

    /// <summary>Vrai si l'interactif est un zaapi utilisable : type 106 ou skill 157, et au moins un skill actif.</summary>
    public static bool IsUsable(int elementTypeId, IEnumerable<(int SkillId, bool Enabled)> skills)
    {
        bool enabled = false, use = false;
        if (skills is not null)
        {
            foreach (var skill in skills)
            {
                if (!skill.Enabled) continue;
                enabled = true;
                if (skill.SkillId == SkillId) use = true;
            }
        }
        return enabled && (elementTypeId == ElementTypeId || use);
    }

    public static string Coordinates(int? x, int? y)
        => x is int px && y is int py ? FormattableString.Invariant($"{px},{py}") : "";

    public static bool Arrived(long observedMap, long destinationMap)
        => WorldMapZaapTravel.Arrived(observedMap, destinationMap);

    public static ZaapiOffer Evaluate(long mapId, bool listed, long? costKamas, long kamas)
    {
        if (mapId <= 0 || !listed) return new ZaapiOffer(false, null, false, UnknownReason);
        if (costKamas is not long cost || cost < 0) return new ZaapiOffer(true, null, false, UnknownCostReason);
        if (kamas < 0) return new ZaapiOffer(true, cost, false, UnknownKamasReason);
        if (kamas < cost) return new ZaapiOffer(true, cost, false, InsufficientReason(kamas, cost));
        return new ZaapiOffer(true, cost, true, ReadyReason(mapId, cost));
    }

    /// <summary>
    /// Parmi les destinations qui raccourcissent le trajet, la moins de cartes restantes dont le coût annoncé est payable.
    /// </summary>
    public static ZaapiPick? Choose(
        IReadOnlyList<(long MapId, long? CostKamas, int HopsAfter)> options,
        long kamas, int walkHops, bool unreachable, int margin)
    {
        ZaapiPick? best = null;
        ZaapiPick? refusal = null;
        foreach (var option in options)
        {
            if (option.MapId <= 0 || option.HopsAfter < 0) continue;
            bool shortens = unreachable || (walkHops > 0 && option.HopsAfter + margin < walkHops);
            if (!shortens) continue;
            var offer = Evaluate(option.MapId, true, option.CostKamas, kamas);
            var pick = new ZaapiPick(option.MapId, option.HopsAfter, offer.CostKamas, offer.CanSend, offer.Reason);
            if (!offer.CanSend)
            {
                refusal ??= pick;
                continue;
            }
            if (best is null || pick.HopsAfter < best.Value.HopsAfter) best = pick;
        }
        return best ?? refusal;
    }

    /// <summary>
    /// Une ligne par indice d'onglet posé sur une carte envoyée. Une carte sans indice d'onglet reste une ligne :
    /// le serveur l'a envoyée, le nom et l'onglet restent vides. Un indice d'une autre carte est ignoré.
    /// </summary>
    public static IReadOnlyList<ZaapiStop> Present(
        IReadOnlyList<TeleportReader.Destination> sent,
        IReadOnlyList<ZaapiHint> hints,
        Func<int, string?>? districtOf,
        Func<long, (int X, int Y)?>? coordOf)
    {
        var byMap = new Dictionary<long, List<ZaapiHint>>();
        if (hints is not null)
        {
            foreach (var hint in hints)
            {
                if (hint.MapId <= 0 || !IsTabCategory(hint.CategoryId)) continue;
                if (!byMap.TryGetValue(hint.MapId, out var list)) byMap[hint.MapId] = list = new List<ZaapiHint>();
                list.Add(hint);
            }
        }
        var stops = new List<ZaapiStop>();
        if (sent is null) return stops;
        foreach (var dest in sent)
        {
            if (dest.MapId <= 0) continue;
            long? cost = dest.CostKnown && dest.Cost >= 0 ? dest.Cost : null;
            string district = districtOf?.Invoke(dest.SubAreaId) ?? "";
            if (byMap.TryGetValue(dest.MapId, out var rows) && rows.Count > 0)
            {
                foreach (var hint in rows)
                    stops.Add(new ZaapiStop(dest.MapId, hint.Name ?? "", district, hint.X, hint.Y, cost,
                        hint.CategoryId, hint.Category ?? "", dest.SubAreaId));
            }
            else
            {
                var xy = coordOf?.Invoke(dest.MapId);
                stops.Add(new ZaapiStop(dest.MapId, "", district, xy?.X, xy?.Y, cost, 0, "", dest.SubAreaId));
            }
        }
        return stops;
    }
}

/// <summary>Dernière liste zaapi (type 1) du perso. Une liste de zaap, de prisme ou d'anomalie ne l'efface pas.</summary>
public sealed class ZaapiBook
{
    private readonly List<TeleportReader.Destination> _rows = new();

    public int ListType { get; private set; } = -1;
    public long CharacterId { get; private set; }

    public IReadOnlyList<TeleportReader.Destination> Rows => _rows;

    public static ZaapiBook From(long characterId, IEnumerable<TeleportReader.Destination> rows)
    {
        var book = new ZaapiBook();
        book.Replace(ZaapiTravel.ListType, characterId, rows);
        return book;
    }

    public void Replace(int listType, long characterId, IEnumerable<TeleportReader.Destination> rows)
    {
        if (listType != ZaapiTravel.ListType) return;
        _rows.Clear();
        ListType = listType;
        CharacterId = characterId;
        if (rows is null) return;
        foreach (var row in rows)
            if (row.MapId > 0) _rows.Add(row);
    }

    public bool Matches(long characterId)
        => CharacterId == 0 || characterId == 0 || CharacterId == characterId;

    public bool Listed(long mapId, out long? cost)
    {
        foreach (var row in _rows)
        {
            if (row.MapId != mapId) continue;
            cost = row.CostKnown && row.Cost >= 0 ? row.Cost : null;
            return true;
        }
        cost = null;
        return false;
    }

    public IReadOnlyList<long> MapIds()
    {
        var ids = new long[_rows.Count];
        for (int i = 0; i < _rows.Count; i++) ids[i] = _rows[i].MapId;
        return ids;
    }
}
