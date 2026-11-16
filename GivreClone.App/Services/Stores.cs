using System;
using System.IO;
using GivreClone.Engine.Accounts;
using GivreClone.Engine.Proxies;

namespace GivreClone.App.Services;

/// <summary>Stores persistes partages entre les onglets (une seule source de verite comptes + proxys).</summary>
public static class Stores
{
    private static string Dir => MZP19LocalClone.DataRoot;

    public static AccountManager Accounts { get; } = new(Path.Combine(Dir, "accounts.json"));
    public static ProxyStore Proxies { get; } = new(Path.Combine(Dir, "proxies.json"));

    // Chaine de lancement PARTAGEE : possede le relais (port 5658) et le decodeur MITM. Une seule instance
    // pour tout l'app (l'onglet Comptes lance, l'onglet Bots consomme le flux PacketDecoded). Ne pas dupliquer.
    public static LaunchService Launch { get; } = new();

    // Etat de jeu headless par PID (carte, cellule self, marchabilite, interactifs, monstres, pods) alimente par les
    // evenements de Launch. Source d'etat des scripts Lua (GivreLuaHost). Cree APRES Launch (ordre des champs statiques).
    public static BotWorldStore World { get; } = new(Launch);

    // Stats de botting PAR PERSONNAGE, persistees (recoltes, combats, temps de botting) -> onglet Stats. Alimentee par Launch.
    public static BotStatsStore Stats { get; } = new(Launch);

    // Messagerie privee : reponse auto aux MP (regles/LLM) + relais Discord. S'abonne a Launch.PrivateMessageReceived.
    public static PrivateMessageService PrivateMessages { get; } = new(Launch, World);

    // Icones de monstres extraites du client (genericId -> Bitmap), partagees (utilisees par un converter XAML).
    public static MonsterIconProvider MonsterIcons { get; } = new();

    // Icones d'interactifs (skill_id -> Bitmap de la ressource recoltee), pour le rendu sur la carte.
    public static InteractiveIconProvider InteractiveIcons { get; } = new();

    // Icones de CLASSE (index d'enum Breed + sexe -> Bitmap), pour remplacer l'avatar generique par la tete de classe.
    public static ClassIconProvider ClassIcons { get; } = new();
    public static CosmeticIconProvider CosmeticIcons { get; } = new();

    // Icones de SORTS (iconId -> Bitmap), pour l'onglet Combat (rotation + picker).
    public static SpellIconProvider SpellIcons { get; } = new();

    // Icones de CHALLENGES (iconId -> Bitmap), pour la liste « Challenges acceptés » de l'onglet Combat.
    public static ChallengeIconProvider ChallengeIcons { get; } = new();

    // Icones UI DU JEU par nom (uiassets : onglets map/bank/inventory/..., categories d'HDV auctionHouse*, caracteristiques tx_*).
    public static UiGameIconProvider UiIcons { get; } = new();
}
