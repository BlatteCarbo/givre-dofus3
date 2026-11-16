namespace GivreClone.App.Services;

/// <summary>
/// Canonical local order identity shared by the desktop and Lua send paths.
/// This is an anti-replay key, NOT an order ID/receipt issued by the server.
/// The source UID, quantity, price and observed stack count must not change
/// across attempts merely because a different UI initiated the same order.
/// </summary>
public static class HdvTaxOrderKeys
{
    public static string Sell(int bagUid, int quantity, long price, int observedStock)
        => $"sell:{bagUid}:{quantity}:{price}:{observedStock}";

    public static string Reprice(int exchangeUid, int quantity, long price)
        => $"reprice:{exchangeUid}:{quantity}:{price}";
}
