using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>Une anomalie de sous-zone. <see cref="ClosingRaw"/> est la valeur BRUTE reçue (unité non établie : voir AnomalyClock).</summary>
public sealed record AnomalyInfo(int SubareaId, bool Open, long ClosingRaw, int RewardRate);

/// <summary>Proposition de téléportation reçue. Kind : group | player | buddy | buddies. TimeLeft = valeur reçue (unité non établie).</summary>
public sealed record TeleportOfferInfo(string Kind, long RequesterId, string RequesterName, long MapId, int DungeonId, long TimeLeft);

/// <summary>
/// Lecteurs du lot M (succès, anomalies, téléportation, document), tous PAR NOM via le catalogue (comme les autres lecteurs). Ils lisent ce
/// que le mapping 3.7 nomme ; les champs obfusqués non nommés sont ignorés. Aucun n'a été validé contre une trame réelle.
/// </summary>
public static class LotMReader
{
    private static Func<string, string, int?>? _fieldNumber;
    public static void UseCatalog(Func<string, string, int?> fieldNumber) => _fieldNumber = fieldNumber;
    private static int F(string message, string field) => _fieldNumber?.Invoke(message, field) ?? FieldMiss.Of(message, field);

    // ---------------------------------------------------------------- succès
    /// <summary>Un succès terminé : AchievedAchievement { achievement_id, achieved_by, finished_level (optionnel), pioneer_rank }.</summary>
    public sealed record AchievedInfo(int Id, int FinishedLevel);

    private static AchievedInfo? Achieved(ProtoField f)
    {
        int fId = F("AchievedAchievement", "achievement_id"), fLvl = F("AchievedAchievement", "finished_level");
        if (f.Message is not { } m || !m.Any(x => x.Field == fId)) return null;
        return new AchievedInfo((int)PayloadReader.Int(m, fId, 0), (int)PayloadReader.Int(m, fLvl, 0));
    }

    /// <summary>AchievementsEvent : succès terminés par ce perso (achieved_achievements = liste d'AchievedAchievement).</summary>
    public static IReadOnlyList<AchievedInfo> AchievedList(IReadOnlyList<ProtoField> p)
    {
        int fList = F("AchievementsEvent", "achieved_achievements");
        return p.Where(f => f.Field == fList).Select(Achieved).OfType<AchievedInfo>().GroupBy(a => a.Id).Select(g => g.First()).ToList();
    }

    /// <summary>AchievementFinishedEvent : le succès qui vient d'être terminé (AchievedAchievement en champ `achievement`), ou null.</summary>
    public static AchievedInfo? Finished(IReadOnlyList<ProtoField> p)
        => p.FirstOrDefault(f => f.Field == F("AchievementFinishedEvent", "achievement")) is { } f ? Achieved(f) : null;

    /// <summary>AchievementRewardResultEvent : (id, succès). Un échec peut omettre `success` (proto3 : faux).</summary>
    public static (int Id, bool Success) RewardResult(IReadOnlyList<ProtoField> p)
        => ((int)PayloadReader.Int(p, F("AchievementRewardResultEvent", "achievement_id"), 0),
            PayloadReader.Bool(p, F("AchievementRewardResultEvent", "success"), false));

    // ---------------------------------------------------------------- anomalies
    /// <summary>AnomalyStateEvent : état d'UNE sous-zone.</summary>
    public static AnomalyInfo AnomalyState(IReadOnlyList<ProtoField> p)
        => new((int)PayloadReader.Int(p, F("AnomalyStateEvent", "subarea_id"), 0), PayloadReader.Bool(p, F("AnomalyStateEvent", "open"), false),
               PayloadReader.Int(p, F("AnomalyStateEvent", "closing_time"), 0), 0);

    /// <summary>AnomalyOpenedEvent : une anomalie vient de s'ouvrir dans cette sous-zone (id seul ; fermeture inconnue).</summary>
    public static int AnomalyOpenedSubarea(IReadOnlyList<ProtoField> p) => (int)PayloadReader.Int(p, F("AnomalyOpenedEvent", "subarea_id"), 0);

    /// <summary>AnomalySubareaInformationEvent : état de plusieurs sous-zones.</summary>
    public static IReadOnlyList<AnomalyInfo> AnomalySubareas(IReadOnlyList<ProtoField> p)
    {
        int fList = F("AnomalySubareaInformationEvent", "subareas");
        int fHas = F("AnomalySubareaInformation", "has_anomaly"), fRate = F("AnomalySubareaInformation", "reward_rate");
        int fSub = F("AnomalySubareaInformation", "subarea_id"), fClose = F("AnomalySubareaInformation", "anomaly_closing_time");
        var list = new List<AnomalyInfo>();
        foreach (var f in p.Where(x => x.Field == fList && x.Message is not null))
        {
            var m = f.Message!;
            list.Add(new AnomalyInfo((int)PayloadReader.Int(m, fSub, 0), PayloadReader.Bool(m, fHas, false), PayloadReader.Int(m, fClose, 0), (int)PayloadReader.Int(m, fRate, 0)));
        }
        return list;
    }

    // ---------------------------------------------------------------- téléportation
    /// <summary>Propositions : TeleportPlayerOfferEvent, GroupTeleportPlayerOfferEvent, TeleportToBuddyOfferEvent, TeleportBuddiesRequestedEvent.
    /// Null pour tout autre message.</summary>
    public static TeleportOfferInfo? TeleportOffer(string? messageName, IReadOnlyList<ProtoField> p) => messageName switch
    {
        "TeleportPlayerOfferEvent" => new("player", PayloadReader.Int(p, F(messageName, "requester_id"), 0), "", PayloadReader.Int(p, F(messageName, "map_id"), 0), 0, PayloadReader.Int(p, F(messageName, "time_left"), 0)),
        "GroupTeleportPlayerOfferEvent" => new("group", PayloadReader.Int(p, F(messageName, "requester_id"), 0), PayloadReader.Str(p, F(messageName, "requester_name")) ?? "", PayloadReader.Int(p, F(messageName, "map_id"), 0), 0, PayloadReader.Int(p, F(messageName, "time_left"), 0)),
        "TeleportToBuddyOfferEvent" => new("buddy", PayloadReader.Int(p, F(messageName, "buddy_id"), 0), "", 0, (int)PayloadReader.Int(p, F(messageName, "dungeon_id"), 0), PayloadReader.Int(p, F(messageName, "time_left"), 0)),
        "TeleportBuddiesRequestedEvent" => new("buddies", PayloadReader.Int(p, F(messageName, "inviter_id"), 0), "", 0, (int)PayloadReader.Int(p, F(messageName, "dungeon_id"), 0), 0),
        _ => null,
    };

    /// <summary>La proposition a été close (acceptée, refusée ou expirée) : message de fermeture correspondant à un kind.</summary>
    public static string? TeleportClosedKind(string? messageName) => messageName switch
    {
        "TeleportPlayerClosedEvent" => "player",
        "GroupTeleportPlayerClosedEvent" => "group",
        "TeleportToBuddyClosedEvent" => "buddy",
        _ => null,
    };

    // ---------------------------------------------------------------- boutique de PNJ
    /// <summary>Un objet vendu par un PNJ : gid, prix en kamas (0 si absent), critère d'achat (« » si aucun).</summary>
    public sealed record ShopItem(int Gid, long Price, string Criterion);

    /// <summary>ExchangeNpcShopStartedEvent : (PNJ vendeur, objets). Objet = ItemMinimalInformation { object_gid, item_to_sell_in_npc_shop { price, buy_criterion } }.</summary>
    public static (long NpcId, IReadOnlyList<ShopItem> Items) NpcShop(IReadOnlyList<ProtoField> p)
    {
        int fSeller = F("ExchangeNpcShopStartedEvent", "npc_seller_id"), fObjs = F("ExchangeNpcShopStartedEvent", "objects");
        int fGid = F("ItemMinimalInformation", "object_gid"), fShop = F("ItemMinimalInformation", "item_to_sell_in_npc_shop");
        int fPrice = F("ItemToSellInNpcShop", "price"), fCrit = F("ItemToSellInNpcShop", "buy_criterion");
        var items = new List<ShopItem>();
        foreach (var o in p.Where(f => f.Field == fObjs && f.Message is not null))
        {
            var m = o.Message!;
            var shop = m.FirstOrDefault(x => x.Field == fShop)?.Message;
            items.Add(new ShopItem((int)PayloadReader.Int(m, fGid, 0), shop is null ? 0 : PayloadReader.Int(shop, fPrice, 0), shop is null ? "" : PayloadReader.Str(shop, fCrit) ?? ""));
        }
        return (PayloadReader.Int(p, fSeller, 0), items);
    }

    // ---------------------------------------------------------------- historique des prix (HDV)
    /// <summary>Un point de l'historique du prix moyen : date (texte du serveur), prix moyen, quantité vendue.</summary>
    public sealed record PricePoint(string Date, long Price, int Sold);

    /// <summary>ObjectAveragePricesHistoryEvent : points de l'historique (le plus ancien d'abord tel que reçu).</summary>
    public static IReadOnlyList<PricePoint> PriceHistory(IReadOnlyList<ProtoField> p)
    {
        int fList = F("ObjectAveragePricesHistoryEvent", "objects_average_prices_history");
        int fPrice = F("ObjectAveragePriceHistory", "average_price"), fDate = F("ObjectAveragePriceHistory", "date"), fSold = F("ObjectAveragePriceHistory", "items_sold");
        return p.Where(f => f.Field == fList && f.Message is not null)
                .Select(f => new PricePoint(PayloadReader.Str(f.Message!, fDate) ?? "", PayloadReader.Int(f.Message!, fPrice, 0), (int)PayloadReader.Int(f.Message!, fSold, 0))).ToList();
    }

    // ---------------------------------------------------------------- document
    /// <summary>DocumentReadingBeginEvent : id du document affiché.</summary>
    public static int DocumentId(IReadOnlyList<ProtoField> p) => (int)PayloadReader.Int(p, F("DocumentReadingBeginEvent", "document_id"), 0);

    // ---------------------------------------------------------------- sortie de donjon
    /// <summary>Valeur de <c>DungeonExitResponse.fyaz</c> observée le 31 oct 2026 sur une sortie réussie (varint 1),
    /// suivie du changement de carte. Le nom du champ vient du mapping ; une autre valeur est le motif de refus.</summary>
    public const ulong DungeonExitSuccessValue = 1;

    /// <summary>Numéro wire de l'unique champ mappé de DungeonExitResponse (<c>fyaz</c>), ou <see cref="FieldMiss.None"/>.</summary>
    public static int DungeonExitFieldNumber() => F("DungeonExitResponse", "fyaz");

    /// <summary>Lit le champ mappé. Succès seulement si sa valeur est <see cref="DungeonExitSuccessValue"/>.
    /// Sinon le motif est la valeur reçue (entier ou texte), jamais un champ inventé.</summary>
    public static (bool Success, string Reason) DungeonExit(IReadOnlyList<ProtoField> payload, int field)
    {
        if (field < 1) return (false, "champ de DungeonExitResponse absent du mapping");
        var f = PayloadReader.Field(payload, field);
        if (f is null) return (false, "résultat absent");
        if (f.Wire == WireType.Varint && f.Varint == DungeonExitSuccessValue) return (true, "");
        if (f.Wire == WireType.Varint) return (false, unchecked((long)f.Varint).ToString(CultureInfo.InvariantCulture));
        if (PayloadReader.Str(payload, field) is { Length: > 0 } text) return (false, text);
        return (false, "résultat refusé");
    }

    /// <summary>La sortie n'est acquise que si la réponse est un succès ET la carte suivante est une autre carte.</summary>
    public static (bool Ok, string Reason, long MapId) DungeonExitConfirmed((bool Success, string Reason) answer, long fromMap, long arrivedMap)
    {
        long where = arrivedMap != 0 ? arrivedMap : fromMap;
        if (!answer.Success) return (false, answer.Reason, where);
        if (arrivedMap != 0 && arrivedMap != fromMap) return (true, "", arrivedMap);
        return (false, "la carte n'a pas changé", where);
    }
}
