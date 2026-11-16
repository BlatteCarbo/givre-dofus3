using System;
using System.Collections.Generic;

namespace GivreClone.App.Services;

/// <summary>Raccourci de trajet. Auto laisse le planificateur choisir. Les autres sont des choix explicites.</summary>
public enum ZaapRoute
{
    Auto = 0,
    /// <summary>TeleportRequest source_type 5, sans zaap au sol ni havre-sac.</summary>
    WorldMap = 1,
    /// <summary>Zaap physique de la carte courante.</summary>
    Ground = 2,
    /// <summary>Havre-sac, puis son zaap.</summary>
    HavenBag = 3,
    /// <summary>Zaapi de la carte courante. TeleportDestinationsEvent type 1, puis TeleportRequest source_type 1 et destination_type 1.</summary>
    Zaapi = 4,
}

/// <summary>Décision d'un zaap depuis la carte. <see cref="CostKamas"/> est nul quand le coût n'est pas dans les données.</summary>
public readonly record struct WorldMapZaapOffer(bool Known, long? CostKamas, bool CanSend, string Reason);

/// <summary>Zaap retenu, ou le premier refus explicable s'il n'y a rien à envoyer.</summary>
public readonly record struct WorldMapZaapPick(long MapId, int HopsAfter, long? CostKamas, bool CanSend, string Reason);

/// <summary>
/// Zaap depuis la carte du monde. La liste de connexion (zaaps débloqués) remplit le menu tout de suite.
/// Le coût reste nul jusqu'à une liste de type 0 (zaap) ou 3 (havre-sac), qui annonce TeleportDestination.cost.
/// Le raccourci automatique n'envoie rien sans un coût connu et payable : un zaap absent est refusé ici,
/// parce que le serveur répond « Impossible de se téléporter vers cet endroit. ».
/// Le clic du menu envoie quand même source_type 5 si le zaap est débloqué et le coût encore inconnu.
/// L'arrivée n'est vraie que si la carte observée change. Un refus ou une carte inchangée reste un échec.
/// WaypointPriceRequest / WaypointPriceResponse n'ont pas de noms de champs exploitables (fmfq, fmfr, fmfw, fmfv).
/// </summary>
public static class WorldMapZaapTravel
{
    /// <summary>TeleportRequest.source_type. Capture du 31/10 : 5 = carte du monde. Le numéro de champ vient du mapping, pas d'ici.</summary>
    public const int SourceType = 5;

    public const string Label = "Zaap depuis la carte";
    public const string GroundLabel = "Zaap au sol";
    public const string HavenLabel = "Havre-sac";
    public const string ZaapiLabel = "Zaapi";
    public const string AutoLabel = "Automatique";

    public const string UnknownReason = "Ce zaap n'est pas connu.";
    public const string UnknownListReason = "Aucun zaap connu pour l'instant.";
    public const string UnknownCostReason = "Coût en kamas inconnu.";
    public const string UnknownKamasReason = "Kamas du personnage inconnus.";
    public const string NoShortcutReason = "Zaap depuis la carte : aucun zaap connu ne raccourcit ce trajet.";
    public const string CombatReason = "Zaap depuis la carte impossible en combat.";
    public const string SendFailedReason = "Zaap depuis la carte : envoi refusé.";
    public const string RouteNamesReason = "Raccourci zaap inconnu. Choix : auto, carte, sol, havre, zaapi.";

    public static string InsufficientReason(long have, long cost)
        => $"Kamas insuffisants : {have} en poche, {cost} requis (dernier coût annoncé).";

    public static string ReadyReason(long mapId, long cost)
        => $"Zaap depuis la carte vers {mapId} ({cost} kamas, dernier coût annoncé).";

    public static string UnconfirmedReason(long mapId)
        => $"Zaap depuis la carte vers {mapId} non confirmé : la carte n'a pas changé.";

    public static string ArrivedReason(long mapId)
        => $"Zaap depuis la carte : arrivée sur la carte {mapId}.";

    public static string ArrivedReason(long mapId, long? cost)
        => cost is >= 0 ? $"Zaap depuis la carte : arrivée sur la carte {mapId} ({cost} kamas)."
            : ArrivedReason(mapId);

    public static string ChosenReason(ZaapRoute route) => "Raccourci choisi : " + LabelOf(route) + ".";

    /// <summary>0 = zaap, 3 = havre-sac. Zaapi (1), prisme (2) et anomalie (4) ne sont pas la liste des zaaps du personnage.</summary>
    public static bool IsCharacterZaapList(int listType) => listType is 0 or 3;

    public static string LabelOf(ZaapRoute route) => route switch
    {
        ZaapRoute.WorldMap => Label,
        ZaapRoute.Ground => GroundLabel,
        ZaapRoute.HavenBag => HavenLabel,
        ZaapRoute.Zaapi => ZaapiLabel,
        _ => AutoLabel,
    };

    /// <summary>« 210 kamas » si le coût est connu, sinon « inconnu ».</summary>
    public static string FormatCost(long? cost) => cost is long c && c >= 0 ? $"{c} kamas" : "inconnu";

    /// <summary>Succès uniquement quand la carte observée EST la destination. L'envoi du paquet ne suffit pas.</summary>
    public static bool Arrived(long observedMap, long destinationMap)
        => destinationMap > 0 && observedMap == destinationMap;

    public static WorldMapZaapOffer Evaluate(long mapId, bool known, long? costKamas, long kamas, bool allowUnknownCost = false)
    {
        if (mapId <= 0 || !known) return new WorldMapZaapOffer(false, null, false, UnknownReason);
        if (costKamas is not long cost || cost < 0)
        {
            if (allowUnknownCost && kamas >= 1000)
                return new WorldMapZaapOffer(true, null, true, ReadyReason(mapId, 0));
            return new WorldMapZaapOffer(true, null, false, UnknownCostReason);
        }
        if (kamas < 0) return new WorldMapZaapOffer(true, cost, false, UnknownKamasReason);
        if (kamas < cost) return new WorldMapZaapOffer(true, cost, false, InsufficientReason(kamas, cost));
        return new WorldMapZaapOffer(true, cost, true, ReadyReason(mapId, cost));
    }

    /// <summary>
    /// Parmi les zaaps qui raccourcissent le trajet, le moins de cartes restantes dont le coût est connu et payable.
    /// À nombre de cartes restantes égal, préfère le zaap dont le coût en kamas est le plus faible.
    /// Sinon le premier refus (zaap trop cher ou coût inconnu), ou null s'il n'y a aucun raccourci.
    /// </summary>
    public static WorldMapZaapPick? ChooseDestination(
        IReadOnlyList<(long MapId, long? CostKamas, int HopsAfter)> options,
        long kamas, int walkHops, bool unreachable, int margin, bool allowUnknownCost = false)
    {
        WorldMapZaapPick? best = null;
        WorldMapZaapPick? refusal = null;
        foreach (var option in options)
        {
            if (option.MapId <= 0 || option.HopsAfter < 0) continue;
            bool shortens = unreachable || (walkHops > 0 && option.HopsAfter + margin < walkHops);
            if (!shortens) continue;
            var offer = Evaluate(option.MapId, true, option.CostKamas, kamas, allowUnknownCost);
            var pick = new WorldMapZaapPick(option.MapId, option.HopsAfter, offer.CostKamas, offer.CanSend, offer.Reason);
            if (!offer.CanSend)
            {
                refusal ??= pick;
                continue;
            }
            if (best is null
                || pick.HopsAfter < best.Value.HopsAfter
                || (pick.HopsAfter == best.Value.HopsAfter && IsCheaper(pick.CostKamas, best.Value.CostKamas)))
            {
                best = pick;
            }
        }
        return best ?? refusal;
    }

    private static bool IsCheaper(long? candidate, long? current)
    {
        if (candidate is { } c && current is { } cur) return c < cur;
        if (candidate is not null && current is null) return true;
        return false;
    }

    /// <summary>Ordre des tentatives. Marche de préférence : aucun raccourci en mode auto. Un choix explicite reste seul.</summary>
    public static ZaapRoute[] Attempts(ZaapRoute preference, bool walkFirst) => preference switch
    {
        ZaapRoute.WorldMap => new[] { ZaapRoute.WorldMap },
        ZaapRoute.Ground => new[] { ZaapRoute.Ground },
        ZaapRoute.HavenBag => new[] { ZaapRoute.HavenBag },
        ZaapRoute.Zaapi => new[] { ZaapRoute.Zaapi },
        _ when walkFirst => Array.Empty<ZaapRoute>(),
        _ => new[] { ZaapRoute.Zaapi, ZaapRoute.WorldMap, ZaapRoute.Ground, ZaapRoute.HavenBag },
    };

    public static bool TryParseRoute(string? text, out ZaapRoute route)
    {
        route = ZaapRoute.Auto;
        string name = (text ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "");
        switch (name)
        {
            case "auto":
            case "automatique":
                route = ZaapRoute.Auto;
                return true;
            case "carte":
            case "monde":
            case "zaapcarte":
            case "zaapdepuislacarte":
                route = ZaapRoute.WorldMap;
                return true;
            case "sol":
            case "physique":
            case "zaapausol":
                route = ZaapRoute.Ground;
                return true;
            case "havre":
            case "havresac":
            case "havenbag":
                route = ZaapRoute.HavenBag;
                return true;
            case "zaapi":
                route = ZaapRoute.Zaapi;
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// Zaaps du personnage. La liste de connexion les pose sans coût. Une liste de type 0 ou 3 remplit le coût
/// quand le champ est présent, sans retirer les zaaps déjà débloqués.
/// </summary>
public sealed class CharacterZaapBook
{
    private readonly Dictionary<long, long?> _cost = new();

    public int ListType { get; private set; } = -1;
    public long CharacterId { get; private set; }

    public CharacterZaapBook Copy()
    {
        var copy = new CharacterZaapBook { ListType = ListType, CharacterId = CharacterId };
        foreach (var row in _cost) copy._cost[row.Key] = row.Value;
        return copy;
    }

    /// <summary>
    /// Pose les zaaps débloqués à la connexion. Sans liste tarifée, cette liste remplace la précédente
    /// et conserve un coût déjà appris pour la même carte. Une liste tarifée n'est pas effacée : les cartes nouvelles restent sans coût.
    /// </summary>
    public void SeedUnlocked(long characterId, IEnumerable<long> mapIds)
    {
        if (mapIds is null) return;
        if (CharacterId != 0 && characterId != 0 && CharacterId != characterId)
        {
            _cost.Clear();
            ListType = -1;
            CharacterId = characterId;
        }
        else if (CharacterId == 0 && characterId != 0)
            CharacterId = characterId;

        if (!WorldMapZaapTravel.IsCharacterZaapList(ListType))
        {
            var next = new Dictionary<long, long?>();
            foreach (var id in mapIds)
            {
                if (id <= 0) continue;
                next[id] = _cost.TryGetValue(id, out var known) ? known : null;
            }
            _cost.Clear();
            foreach (var row in next) _cost[row.Key] = row.Value;
            return;
        }
        foreach (var id in mapIds)
        {
            if (id <= 0 || _cost.ContainsKey(id)) continue;
            _cost[id] = null;
        }
    }

    /// <summary>
    /// Annonce les coûts d'une liste de type 0 ou 3. Un coût présent remplace l'ancien.
    /// Un coût absent ne l'efface pas. Les zaaps débloqués qui ne sont pas dans cette liste restent.
    /// </summary>
    public void LearnCosts(int listType, long characterId, IEnumerable<(long MapId, long? CostKamas)> rows)
    {
        if (!WorldMapZaapTravel.IsCharacterZaapList(listType) || rows is null) return;
        if (CharacterId != 0 && characterId != 0 && CharacterId != characterId)
        {
            _cost.Clear();
            ListType = -1;
        }
        ListType = listType;
        if (characterId != 0 || CharacterId == 0) CharacterId = characterId;
        foreach (var row in rows)
        {
            if (row.MapId <= 0) continue;
            if (row.CostKamas is long cost && cost >= 0) _cost[row.MapId] = cost;
            else if (!_cost.ContainsKey(row.MapId)) _cost[row.MapId] = null;
        }
    }

    public static CharacterZaapBook From(int listType, long characterId, IEnumerable<(long MapId, long? CostKamas)> rows)
    {
        var book = new CharacterZaapBook();
        book.Replace(listType, characterId, rows);
        return book;
    }

    /// <summary>Remplace la liste seulement pour un type 0 ou 3. Un zaapi, un prisme ou une anomalie ne l'efface pas.</summary>
    public void Replace(int listType, long characterId, IEnumerable<(long MapId, long? CostKamas)> rows)
    {
        if (!WorldMapZaapTravel.IsCharacterZaapList(listType)) return;
        _cost.Clear();
        ListType = listType;
        CharacterId = characterId;
        foreach (var row in rows)
        {
            if (row.MapId <= 0) continue;
            _cost[row.MapId] = row.CostKamas is long cost && cost >= 0 ? cost : null;
        }
    }

    /// <summary>Un identifiant encore inconnu (0) ne tranche pas. Deux identités connues différentes ne partagent pas la liste.</summary>
    public bool Matches(long characterId)
        => CharacterId == 0 || characterId == 0 || CharacterId == characterId;

    public bool TryGet(long mapId, out long? cost) => _cost.TryGetValue(mapId, out cost);

    public IReadOnlyList<(long MapId, long? CostKamas)> Snapshot()
    {
        var copy = new List<(long, long?)>(_cost.Count);
        foreach (var row in _cost) copy.Add((row.Key, row.Value));
        return copy;
    }
}
