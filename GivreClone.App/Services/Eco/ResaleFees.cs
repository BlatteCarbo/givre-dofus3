using System.Globalization;

namespace GivreClone.App.Services.Eco;

public static class ResaleFees
{
    public static decimal Tax(decimal salePrice, decimal taxPercent) => Math.Round(salePrice * taxPercent / 100m, 0, MidpointRounding.AwayFromZero);

    // taxPercent comes from the HDV seller panel (0 until a seller window has been opened on this session).
    public static string Describe(decimal? salePrice, decimal? resaleProfit, decimal taxPercent)
    {
        if (salePrice is not { } price || resaleProfit is not { } profit) return "";
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        string sign = profit >= 0 ? "+" : "";
        if (taxPercent <= 0)
            return $"Revente : {sign}{profit.ToString("N0", fr)} kamas, taxe non incluse (taux inconnu : ouvre l'HDV en mode vente pour le lire).";
        return $"Revente : {price.ToString("N0", fr)} − taxe {taxPercent.ToString("0.#", fr)} % ({Tax(price, taxPercent).ToString("N0", fr)} k, taux lu à l'HDV) → {sign}{profit.ToString("N0", fr)} kamas nets.";
    }
}
