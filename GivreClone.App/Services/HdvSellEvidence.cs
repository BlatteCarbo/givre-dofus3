namespace GivreClone.App.Services;

public enum HdvSellDisposition { Refused, Confirmed, NeedsReview }

/// <summary>La classe de l'événement n'est jamais une corrélation de transaction.
/// Sans identifiant/proof transactionnel, « ItemAdded » ou « InListUpdated » reste incertain.</summary>
public static class HdvSellEvidence
{
    public static HdvSellDisposition Classify(string? eventName, bool transactionCorrelated)
    {
        // The server can explicitly refuse the request without echoing the bag UID.
        // This negative result never proves a successful sale.
        if (eventName is "ExchangeErrorEvent" or "ObjectErrorEvent")
            return HdvSellDisposition.Refused;
        if (!transactionCorrelated) return HdvSellDisposition.NeedsReview;
        return eventName switch
        {
            "ExchangeBidHouseItemAddedEvent" or "ExchangeBidHouseInListUpdatedEvent" => HdvSellDisposition.Confirmed,
            _ => HdvSellDisposition.NeedsReview
        };
    }
}
