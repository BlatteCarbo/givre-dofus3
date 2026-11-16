using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace GivreClone.App.Services.UiNavigation;

/// <summary>Actions locales exposées dans la palette. Aucune action de gameplay ni mutation de compte.</summary>
public sealed record QuickNavigationAction(string Key, string Label, string Description, string Keywords);

public static class QuickNavigationCatalog
{
    // Déclaré AVANT All : les initialiseurs statiques s'exécutent dans l'ordre du texte et Tab() lit BotTabTags.
    /// <summary>Tags des onglets Bots, dans l'ordre de BotTabs. La clé « bots/&lt;n&gt; » (historique, Ctrl+K) désigne
    /// BotTabTags[n] ; la vue sélectionne l'onglet par son Tag, jamais par sa position dans le XAML.</summary>
    public static IReadOnlyList<string> BotTabTags { get; } = new[]
    {
        "console", "combat", "expedition", "road", "map", "inventory", "bank", "hdv", "lua",
        "stats", "planning", "settings", "packets", "infos", "treasure", "smithmagic", "mcp"
    };

    public static IReadOnlyList<QuickNavigationAction> All { get; } = new[]
    {
        new QuickNavigationAction("dashboard", "Tableau de bord", "Sessions et supervision", "accueil dashboard vue globale"),
        new QuickNavigationAction("bots", "Bots", "Gestion des bots et scripts", "personnages lua combat scripts"),
        new QuickNavigationAction("accounts", "Comptes", "Gestion des comptes", "identifiants comptes connexion"),
        new QuickNavigationAction("teams", "Équipes", "Groupes de bots", "team groupes multi bots"),
        new QuickNavigationAction("proxys", "Proxys", "Connexions proxy", "proxy connexions"),
        new QuickNavigationAction("adapters", "Réseau", "Interfaces et adaptateurs", "adaptateurs interfaces reseau"),
        new QuickNavigationAction("settings", "Paramètres", "Configuration de l'application", "settings preferences configuration"),
        new QuickNavigationAction("myaccount", "Mon compte", "Licence et profil", "abonnement profil licence"),
        new QuickNavigationAction("visualmods", "Carnet et carte", "Notes et repères locaux, sans injection", "mods visuels positions favoris mini carte"),
        new QuickNavigationAction("worldatlas", "Atlas du monde", "Cartes, grottes et transitions sourcées", "mineur grotte souterrain zaap cartes couche transitions atlas"),
        new QuickNavigationAction("professions", "Coach métiers", "Planifier recettes, XP, ressources et farm", "bijoutier 20 30 artisanat métiers craft recettes farm dropp piou sésame profession"),
        new QuickNavigationAction("ecotools", "Atelier éco", "Craft ou acheter, forgemagie, bonnes affaires, abonnements, carnet", "craft acheter fabriquer forgemagie runes poids fm prix hdv marche abonnement carnet eco rentabilite"),
        new QuickNavigationAction("market", "Marché & kamas", "Consulter l'historique des prix HDV", "marché kamas historique ventes cours prix hdv"),
        new QuickNavigationAction("itemcraft", "Atelier objets & panoplies", "Préparer objets, équipement entier, ingrédients et récolte", "pano bouftou ensemble fabriquer crafter ressources objets sets farm bank banque coffre sac"),
        // Onglets de la page Bots : chaque onglet est désigné par son Tag stable (TabItem.Tag dans BotsView.axaml).
        Tab("console", "Console", "Journal du bot sélectionné", "journal logs messages console erreurs"),
        Tab("combat", "Combat", "Profil de combat, sorts, IA et placement", "combat sorts ia kite fuite placement defis profil"),
        Tab("expedition", "Expédition", "Farmer une zone ou un donjon sans script", "expedition zone donjon farm monstres defaite banque"),
        Tab("road", "Trajets", "Créer et modifier des trajets", "trajets route chemin itineraire editeur"),
        Tab("map", "Carte", "Carte, cellules et acteurs", "carte map cellules acteurs position"),
        Tab("inventory", "Inventaire", "Objets du perso", "inventaire sac objets equipement"),
        Tab("bank", "Banque", "Coffre du compte", "banque coffre depot"),
        Tab("hdv", "HDV", "Hôtel des ventes du perso", "hdv hotel des ventes vendre acheter prix"),
        Tab("lua", "Lua", "Scripts Lua du bot", "lua script editeur lancer"),
        Tab("stats", "Stats", "Statistiques du perso", "stats statistiques caracteristiques xp"),
        Tab("planning", "Planning", "Heures de connexion", "planning horaires heures connexion"),
        Tab("settings", "Paramètres du bot", "Réglages du bot sélectionné", "parametres reglages bot options"),
        Tab("packets", "Paquets", "Trafic réseau du bot", "paquets reseau trafic protocole"),
        Tab("infos", "Infos", "Informations du perso", "infos informations perso personnage"),
        Tab("treasure", "Chasse au trésor", "Chasses et indices", "chasse tresor indices"),
        Tab("smithmagic", "Forgemagie", "Forgemagie du perso", "forgemagie fm runes jets exo"),
        Tab("mcp", "Activité MCP", "Ordres reçus du contrôle local", "mcp ia agent controle ordres")
    };

    public const string BotTabPrefix = "bots/";

    private static QuickNavigationAction Tab(string tag, string label, string description, string keywords)
        => new(BotTabKey(tag), "Bots › " + label, description, "onglet bots " + keywords);

    /// <summary>Clé catalogue de l'onglet Bots portant ce Tag (« combat » -> « bots/1 »).</summary>
    public static string BotTabKey(string tag)
        => BotTabPrefix + (BotTabIndexOf(tag) ?? throw new ArgumentException("Onglet Bots inconnu : " + tag, nameof(tag)));

    /// <summary>Position canonique d'un Tag d'onglet Bots, ou null.</summary>
    public static int? BotTabIndexOf(string? tag)
    {
        for (int i = 0; i < BotTabTags.Count; i++)
            if (string.Equals(BotTabTags[i], tag, StringComparison.Ordinal)) return i;
        return null;
    }

    /// <summary>Tag de l'onglet Bots à cette position canonique, ou null hors bornes.</summary>
    public static string? BotTabTagAt(int index) => index >= 0 && index < BotTabTags.Count ? BotTabTags[index] : null;

    /// <summary>Index d'onglet Bots visé par la clé (« bots/3 » -> 3, « bots/combat » -> 1), ou null si inconnu.</summary>
    public static int? BotTabIndex(string? key)
    {
        if (key is null || !key.StartsWith(BotTabPrefix, StringComparison.Ordinal)) return null;
        var rest = key.AsSpan(BotTabPrefix.Length);
        if (int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out int i))
            return i < BotTabTags.Count ? i : null;
        return BotTabIndexOf(rest.ToString());
    }

    /// <summary>Tag de l'onglet Bots visé par la clé (« bots/1 » ou « bots/combat » -> « combat »), ou null.</summary>
    public static string? BotTabTag(string? key) => BotTabIndex(key) is int i ? BotTabTags[i] : null;

    /// <summary>Recherche bornée, insensible à la casse et aux accents ; chaque mot doit correspondre.</summary>
    public static IReadOnlyList<QuickNavigationAction> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return All;
        var tokens = Normalize(query[..Math.Min(query.Length, 128)]).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return All;
        return All.Where(action =>
        {
            var haystack = Normalize($"{action.Label} {action.Description} {action.Keywords}");
            return tokens.All(token => haystack.Contains(token, StringComparison.Ordinal));
        }).ToArray();
    }

    public static bool IsKnown(string? key) => key is not null && All.Any(x => x.Key == key);

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var normalized = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            normalized.Append(char.IsWhiteSpace(ch) ? ' ' : char.ToLowerInvariant(ch));
        }
        return normalized.ToString().Normalize(NormalizationForm.FormC);
    }
}
