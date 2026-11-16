using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.Transport;
using GivreClone.Engine.Accounts;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using GivreLauncher.Core.Ankama;
using GivreLauncher.Core.Launch;
using GivreLauncher.Core.Proxy;
using GivreLauncher.Core.Transport;
using GivreLauncher.Core.Zaap;
using CoreAccount = GivreLauncher.Core.Accounts.Account;

namespace GivreClone.App.Services;

// ===================== Événements publics levés par le décodeur + détection des archimonstres =====================
public sealed partial class LaunchService
{
    /// <summary>Leve pour chaque paquet Dofus decode d'un client lance (MITM d'observation). PID + nom clair + champs.</summary>
    public event Action<DecodedPacket>? PacketDecoded;

    /// <summary>Leve quand un message porteur d'infos perso est decode : (pid, infos personnage).</summary>
    public event Action<uint, CharacterInfo>? CharacterIdentified;

    /// <summary>Leve a chaque changement de NIVEAU (CharacterLevelUpEvent, resolu par nom, #1 = nouveau niveau) : (pid, niveau).</summary>
    public event Action<uint, int>? CharacterLeveledUp;

    /// <summary>Leve quand la map change (MapComplementaryInformationEvent decode) : (pid, map_id, x?, y?).
    /// x/y = position monde resolue via la table map_positions.json (null si carte inconnue).</summary>
    public event Action<uint, long, int?, int?>? MapChanged;
    /// <summary>Le chef SORT d'une carte dans une DIRECTION cardinale (top/bottom/left/right). Sert au suivi COLLÉ à
    /// REJOUER la sortie EXACTE sur les followers, SANS world-graph ni coordonnées -> marche même dans une zone off-graph
    /// (Incarnam, instances) où le pathfinding échoue. C'est le mécanisme un bot tiers (SYNC_MOVE de la direction).</summary>
    public event Action<uint, string>? LeaderMapExit;
    public void NotifyMapExit(uint pid, string dir) => LeaderMapExit?.Invoke(pid, dir);

    /// <summary>Leve avec les acteurs (cellId + nom) de la carte a chaque MapComplementaryInformationEvent : (pid, acteurs).</summary>
    public event Action<uint, IReadOnlyList<MapActor>>? MapActorsDecoded;

    /// <summary>Levé quand un ARCHIMONSTRE est repéré sur la carte du pid (groupe dont un membre a un genericId d'archi).
    /// Dédupliqué par visite de carte (le même groupe n'alerte qu'UNE fois par entrée sur la carte ; revisiter ré-alerte).</summary>
    public event Action<ArchSighting>? ArchMonsterSighted;
    // Anti-spam : par pid, la carte courante + les actorId d'archis déjà signalés dessus. Réinitialisé au changement de carte.
    // (carte courante, {(acteur, archimonstre)} déjà signalés sur cette visite) : un signalement PAR archimonstre et par visite.
    private readonly ConcurrentDictionary<uint, (long MapId, HashSet<(long Actor, int Gid)> Seen)> _archSeen = new();

    /// <summary>Scanne les acteurs décodés d'une carte : tout groupe de monstres contenant un genericId d'archimonstre est
    /// signalé (une fois par visite). Appelé après chaque décodage d'acteurs (carte complète ET apparition d'acteurs).</summary>
    private void DetectArchMonsters(uint pid, IReadOnlyList<MapActor> actors)
    {
        if (actors.Count == 0 || ArchMonsterSighted is null) return;
        long mapId = CurrentMap(pid) ?? 0;
        // Carte inconnue : un map_id 0 est rejeté par le serveur. Rien n'est marqué « vu » : les acteurs reviennent avec
        // l'événement de carte suivant, qui les signalera avec la bonne carte.
        if (mapId == 0) return;
        var box = _archSeen.AddOrUpdate(pid,
            _ => (mapId, new HashSet<(long, int)>()),
            (_, cur) => cur.MapId == mapId ? cur : (mapId, new HashSet<(long, int)>()));   // changement de carte -> on oublie
        foreach (var a in actors)
        {
            if (a.Kind != MapActorKind.Monster || a.Monsters is not { Count: > 0 } mons) continue;
            // TOUS les archimonstres du groupe (avant : seulement le 1er -> un 2e archimonstre du même groupe n'était jamais signalé).
            foreach (var arch in mons.Where(m => ArchMonsters.IsArch(m.GenericId)).GroupBy(m => m.GenericId).Select(g => g.First()))
            {
                lock (box.Seen) { if (!box.Seen.Add((a.ActorId, arch.GenericId))) continue; }   // déjà signalé sur cette carte -> skip
                int gfx = _gameNames.MonsterGfx(arch.GenericId) ?? 0;   // gfxId = id de l'ICÔNE (≠ genericId) -> vignette embed serveur
                TransportLog.Line($"[archi] pid {pid} : archimonstre « {arch.Name} » (gid {arch.GenericId}, gfx {gfx}) repéré cellule {a.CellId} sur la carte {mapId}");
                ArchMonsterSighted.Invoke(new ArchSighting(pid, arch.GenericId, arch.Name, a.CellId, mapId, a.ActorId, gfx));
            }
        }
    }

    /// <summary>Leve avec les ELEMENTS INTERACTIFS (ressources/zaaps/...) decodes du MapComplementaryInformationEvent : (pid, interactifs).</summary>
    public event Action<uint, IReadOnlyList<MapInteractive>>? MapInteractivesDecoded;

    // Carte pour laquelle les interactifs ont été DÉCODÉS (le décodage est ASYNC : chargement de bundle). La récolte
    // attend que ce soit == carte courante avant de conclure « rien à récolter » -> sinon elle skip des ressources
    // pourtant présentes (elle regardait la liste AVANT la fin du décodage) et change de carte.
    private readonly ConcurrentDictionary<uint, long> _interactivesDecodedMap = new();
    /// <summary>Carte pour laquelle les interactifs sont décodés/prêts (0 si aucun décodage encore).</summary>
    public long InteractivesDecodedMap(uint pid) => _interactivesDecodedMap.TryGetValue(pid, out var m) ? m : 0;

    /// <summary>Leve quand l'ETAT d'un interactif change (StatedElementUpdatedEvent iwf) : (pid, elementId, cell, state). state != 0 = epuise.</summary>
    public event Action<uint, int, int, int>? MapInteractiveUpdated;

    /// <summary>Leve quand les SKILLS d'un interactif changent (InteractiveElementUpdatedEvent iwm) : (pid, elementId,
    /// skills). Sert a refleter dispo -&gt; epuise (skill actif -&gt; desactive) = losange vert -&gt; gris.</summary>
    public event Action<uint, int, IReadOnlyList<InteractiveSkill>>? MapInteractiveSkillsUpdated;

    /// <summary>Leve a chaque objet RECOLTE (ObjectHarvestedEvent itn, personnel) : (pid, itemGid, quantite, iconId, nomObjet).
    /// iconId = icone de l'objet (0 si non resolu) -> feed des stats avec l'asset du jeu.</summary>
    public event Action<uint, int, int, int, string>? ResourceHarvested;

    /// <summary>Leve a la FIN de l'utilisation d'un interactif (iwi InteractiveUseEndedEvent) : (pid, element_id). Recolte
    /// terminee, perso libre. element_id permet de matcher la fin a la ressource EXACTE (pas de fausse attribution).</summary>
    public event Action<uint, int>? InteractiveUseEnded;
    /// <summary>Leve quand l'utilisation d'un interactif ECHOUE (iwq InteractiveUseErrorEvent) : (pid, element_id).</summary>
    public event Action<uint, int>? InteractiveUseFailed;
    /// <summary>Levé quand le serveur CONFIRME une utilisation d'interactif PAR MOI (InteractiveUsedEvent) : (pid, element_id,
    /// skill_id, duration). duration = durée annoncée de l'animation en DIXIÈMES de seconde (0 = instantané) : une porte /
    /// un levier animé ne change l'état (carte, …) qu'APRÈS cette durée.</summary>
    public event Action<uint, int, int, int>? InteractiveUsedBySelf;

    // element_id d'un event interactif plat (iwi/iwq : {element_id, skill_id}) = le PLUS GRAND varint de la trame. Les
    // ids d'element sont grands (~515000) alors que skill_id/skill_instance_uid sont petits -> robuste au SHUFFLE des
    // numeros de champ Ankama (pas besoin de connaitre le numero de champ exact sur ce patch).
    private static int InteractiveElementId(IReadOnlyList<ProtoField> payload)
    {
        long best = 0;
        foreach (var f in payload) if (f.Wire == WireType.Varint && (long)f.Varint > best) best = (long)f.Varint;
        return (int)best;
    }

    /// <summary>Leve a chaque maj de poids/pods (InventoryWeightEvent iun) : (pid, poidsActuel, poidsMax).</summary>
    public event Action<uint, int, int>? PodsUpdated;

    /// <summary>Leve avec les COMBATTANTS d'un combat (positions kmk) : (pid, [(cellule, actorId)]). actorId positif =
    /// joueur, negatif = monstre. Sert a afficher moi + allies + ennemis sur la carte de combat.</summary>
    public event Action<uint, IReadOnlyList<(int Cell, long ActorId)>>? FightersUpdated;

    /// <summary>Leve avec les SORTS APPRIS du perso (SpellsEvent hms) : (pid, [spell_id]). Sert a peupler le picker Combat.</summary>
    public event Action<uint, IReadOnlyList<int>>? CharacterSpellsUpdated;

    /// <summary>Leve quand un combattant est TUE (GameActionFightEvent.Death, code jwe) : (pid, actorId du mort). On le
    /// retire de la carte de combat.</summary>
    public event Action<uint, long>? FighterKilled;

    /// <summary>Leve quand des acteurs APPARAISSENT sur la carte (jsn / GameRolePlayShowActor) : (pid, acteurs a fusionner).</summary>
    public event Action<uint, IReadOnlyList<MapActor>>? MapActorsAdded;

    /// <summary>Leve quand un acteur DISPARAIT (kmu / GameContextRemoveElement) : (pid, actorId a retirer).</summary>
    public event Action<uint, long>? MapActorRemoved;

    /// <summary>Leve quand le serveur de jeu est identifie (izs / ReconnectToGameServerEvent) : (pid, nom du serveur).</summary>
    public event Action<uint, string>? ServerIdentified;

    /// <summary>Leve quand la geometrie de cellules d'une map est chargee (bundle client) : (pid, mapId, cellules). Le mapId
    /// permet d'estampiller POUR QUELLE carte la geometrie est prete (WaitCellsReady) sans course avec le changement de carte.</summary>
    public event Action<uint, long, IReadOnlyList<MapCell>>? MapCellsLoaded;

    /// <summary>Leve quand les SORTIES d'une nouvelle map sont lues (voisins + fleches, bundle client) : (pid, sorties).
    /// -> le VM active les boutons de changement de carte disponibles.</summary>
    public event Action<uint, MapExits>? MapExitsLoaded;

    /// <summary>Leve quand un acteur se deplace (MapMovementEvent decode) : (pid, actorId, cellules du chemin).</summary>
    public event Action<uint, long, IReadOnlyList<int>>? MapActorMoved;

    /// <summary>Leve pour PILOTER le perso (self) : (pid, chemin cellule-par-cellule DEROULE). Source = jrw SORTANT
    /// (injecte OU manuel du client) -> anime le marqueur le long du VRAI chemin, de facon FIABLE (independant du match
    /// actorId du jsj). Au refus (jqt) on envoie un chemin d'une seule cellule (la position reelle) -> recale/annule.
    /// La derniere cellule = la nouvelle source de pathfinding.</summary>
    public event Action<uint, IReadOnlyList<int>>? SelfMoved;

    /// <summary>Position réelle renvoyée par le serveur après refus, distincte d'un chemin prédit.</summary>
    public event Action<uint, int>? SelfPositionCorrected;

    /// <summary>MP PRIVÉ reçu d'un AUTRE joueur (ChatChannelMessageEvent kti, canal 10) : (pid, senderName, content,
    /// senderCharacterId). N'inclut PAS l'écho de nos propres MP envoyés (kth exclu). Consommé par le service de
    /// messagerie (réponse auto / relais Discord).</summary>
    public event Action<uint, string, string, long>? PrivateMessageReceived;

    /// <summary>État de vie du perso changé (CharacterLifeStatusEvent) : (pid, avant, après), 0 = vivant, 1 = tombe, 2 = fantôme.</summary>
    public event Action<uint, int, int>? LifeStatusChanged;

    /// <summary>Un métier monte de niveau (JobLevelUpEvent) : (pid, job_id, nouveau niveau).</summary>
    public event Action<uint, int, int>? JobLeveledUp;

    /// <summary>Défi validé pour le combat (moteur ou script) : (pid, id du défi, bonus %).</summary>
    public event Action<uint, int, int>? ChallengeChosen;

    /// <summary>Aucune proposition de défi n'est acceptée par le profil : (pid, ids proposés). Aucun défi validé.</summary>
    public event Action<uint, IReadOnlyList<int>>? ChallengeDeclined;

    /// <summary>Défi tranché par le serveur (ChallengeResultEvent) : (pid, id, réussi).</summary>
    public event Action<uint, int, bool>? ChallengeResolved;
}
