using System;
using System.Collections.Generic;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.App.Services;

/// <summary>
/// Read-only, mapping-driven UID diagnostics. The formatters accept no price,
/// account identifier or raw payload and never imply a server acknowledgement.
/// They do not participate in HDV sale/removal correlation.
/// </summary>
public static class HdvUidDiagnostic
{
    public static int? ListingUid(IReadOnlyList<ProtoField> payload,
        Func<string, string, int?> fieldNumber)
    {
        int? itemField = fieldNumber("ExchangeBidHouseItemAddedEvent", "item");
        int? uidField = fieldNumber("BidItem", "uid");
        if (itemField is not > 0 || uidField is not > 0) return null;

        int? candidate = null;
        foreach (var item in payload.Where(f => f.Field == itemField && f.Wire == WireType.Len && f.Message is not null))
        {
            foreach (var value in item.Message!.Where(f => f.Field == uidField && f.Wire == WireType.Varint))
            {
                if (value.Varint is not (> 0 and <= int.MaxValue)) continue;
                int uid = (int)value.Varint;
                if (candidate is > 0 && candidate != uid) return null; // ambiguous diagnostic
                candidate = uid;
            }
        }
        return candidate;
    }

    private static string Uid(int? uid) =>
        uid is > 0 ? uid.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "?";

    private static string Event(string? name) => name switch
    {
        null => "timeout",
        "ExchangeBidHouseItemAddedEvent" or "ObjectQuantityEvent" or "ObjectsQuantityEvent"
            or "ObjectDeletedEvent" or "ObjectsDeletedEvent" or "ExchangeErrorEvent"
            or "ObjectErrorEvent" or "ExchangeBidHouseItemRemovedEvent"
            or "ExchangeBidHouseInListRemovedEvent" => name,
        _ => "other"
    };

    public static string SaleRequest(uint pid, int bagUid, int quantity, bool injected) =>
        FormattableString.Invariant($"[hdv] pid {pid} : vente demande uid-sac={bagUid} quantite={quantity} inject={(injected ? "OK" : "ECHEC")} (aucun ACK impliqué).");

    public static string SaleListing(uint pid, int? listingUid) =>
        FormattableString.Invariant($"[hdv] pid {pid} : annonce reçue uid-lot={Uid(listingUid)} (candidat, non corrélé).");

    public static string SaleResult(uint pid, int bagUid, HdvSellDisposition result, int? sellId, string? eventName) =>
        FormattableString.Invariant($"[hdv] pid {pid} : vente uid-sac={bagUid} disposition={result} sell_id={Uid(sellId)} event={Event(eventName)}.");

    public static string RepriceListing(uint pid, int oldSellId, int? listingUid) =>
        FormattableString.Invariant($"[hdv] pid {pid} : reprice ancien sell_id={oldSellId} uid-lot-annonce={Uid(listingUid)} (réponse non corrélée à la demande).");

    public static string RemoveRequest(uint pid, int sellId, int quantity, bool injected, bool linked) =>
        FormattableString.Invariant($"[hdv] pid {pid} : retrait demande sell_id={sellId} quantite={quantity} inject={(injected ? "OK" : "ECHEC")} lien-sac-verifie={linked} (aucun ACK impliqué).");

    public static string RemoveResponse(uint pid, string? eventName, int sellId, HdvSellDisposition result, bool linked) =>
        FormattableString.Invariant($"[hdv] pid {pid} : retrait reponse event={Event(eventName)} sell_id-demande={sellId} disposition={result} lien-sac-verifie={linked}.");
}
