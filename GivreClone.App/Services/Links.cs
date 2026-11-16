using System;
using System.Diagnostics;

namespace GivreClone.App.Services;

/// <summary>Liens externes du produit (une seule source de vérité : écran de connexion, barre de titre, Paramètres, Rich
/// Presence). Ouverts dans le navigateur par défaut.</summary>
public static class Links
{
    /// <summary>Invitation au Discord GivreClone.</summary>
    public const string DiscordInvite = "https://discord.gg/example";

    /// <summary>Ouvre <paramref name="url"/> dans le navigateur par défaut (best-effort : jamais d'exception).</summary>
    public static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Logger.Warn($"Ouverture du lien impossible : {ex.Message}"); }
    }

    public static void OpenDiscord() => Open(DiscordInvite);

    /// <summary>Portail client : offres (« Plans &amp; pricing ») et passage à une licence complète.</summary>
    public const string Plans = "https://example.invalid/portal";

    public static void OpenPlans() => Open(Plans);
}
