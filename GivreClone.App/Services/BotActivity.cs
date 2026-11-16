namespace GivreClone.App.Services;

/// <summary>
/// Activité de HAUT NIVEAU d'un bot en jeu, dérivée de l'état MITM (LaunchService.ActivityOf) — pas des paquets bruts.
/// Sert d'indicateur clair dans l'arbre « » (Combat / Récolte / Déplacement / Jeu…) à la place du
/// compteur de paquets. Ordre de priorité descendant : Déconnecté &gt; Mort &gt; Combat &gt; Récolte &gt; Déplacement &gt; Jeu &gt; Connexion.
/// </summary>
public enum BotActivity
{
    /// <summary>Pas en jeu (arrêté / non lancé).</summary>
    Offline,
    /// <summary>Client lancé mais pas encore localisé (pas de carte connue) — en cours de connexion/chargement.</summary>
    Connecting,
    /// <summary>En jeu, à l'arrêt (aucune action en cours).</summary>
    InGame,
    /// <summary>En déplacement (un MapMovementRequest est en vol).</summary>
    Moving,
    /// <summary>En récolte (utilisation d'un interactif en cours).</summary>
    Gathering,
    /// <summary>En combat (FightStartingEvent -&gt; FightEndEvent).</summary>
    Combat,
    /// <summary>MORT (tombe/fantôme) — résurrection auto au Phénix en cours (CharacterLifeStatusEvent != vivant). Prioritaire.</summary>
    Dead,
    /// <summary>MITM : session de jeu PERDUE (kick serveur — inactivité, modération… — ou connexion de jeu fermée) alors que
    /// Dofus.exe reste ouvert. Prioritaire sur tout le reste ; levé quand une nouvelle session de jeu démarre.</summary>
    Disconnected,
}
