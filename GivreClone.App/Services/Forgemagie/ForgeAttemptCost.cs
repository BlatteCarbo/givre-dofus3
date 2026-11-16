using System.Globalization;

namespace GivreClone.App.Services.Forgemagie;

public static class ForgeAttemptCost
{
    public static decimal? PerAttempt(int attempts, long cashSpent, long stockValue, int unknownValues)
        => attempts <= 0 || unknownValues > 0 ? null : Math.Round((cashSpent + stockValue) / (decimal)attempts, 0, MidpointRounding.AwayFromZero);

    public static string Describe(int attempts, long cashSpent, long stockValue, int unknownValues)
    {
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        if (attempts <= 0) return "Coût par tentative : aucune rune posée.";
        if (PerAttempt(attempts, cashSpent, stockValue, unknownValues) is { } k)
            return $"Coût par tentative : {k.ToString("N0", fr)} k ({(cashSpent + stockValue).ToString("N0", fr)} k mesurés / {attempts} rune(s)).";
        return $"Coût par tentative : inconnu ({unknownValues} rune(s) sans valeur connue) ; au moins {Math.Floor((cashSpent + stockValue) / (decimal)attempts).ToString("N0", fr)} k.";
    }
}
