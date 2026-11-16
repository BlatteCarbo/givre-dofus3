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

// ===================== Interactifs : récolte, portes, usage d'éléments, havre-sac, fermeture de dialogue, téléport / zaaps =====================
public sealed partial class LaunchService
{
    /// <summary>
    /// Recolte/utilise un interactif: choisit une cellule VOISINE marchable et
    /// atteignable (8-voisinage, chemin le plus court qui contourne l'element ; a egalite orthogonale puis diagonale ;
    /// jamais la cellule de l'element), MARCHE jusqu'a
    /// elle, puis a l'arrivee (ack serveur jsq) envoie InteractiveUseRequest (iwo) {skill_instance_uid, element_id}.
    /// Deja voisin -> iwo direct.
    /// Retourne false si non recoltable, pas de cellule adjacente accessible, ou deplacement deja en cours.
    /// </summary>
    public bool Harvest(uint pid, long mapId, int source, MapInteractive interactive, Func<int, bool> isWalkable,
                        Func<int, int, bool>? stepAllowed = null)
    {
        var relay = RelayForInject();
        if (relay is null || mapId == 0) return false;
        if (interactive.HarvestSkill is not { } skill)
        {
            TransportLog.Line($"[recolte] pid {pid} : {interactive.Name} (element {interactive.ElementId}) INDISPONIBLE (aucun skill actif) - recolte annulee");
            return false;
        }
        int element = interactive.ElementId, skillUid = skill.SkillInstanceUid;

        // Case d'où interagir : portée 1 -> une VOISINE de l'élément (déjà voisin -> sur place ; sinon la voisine au chemin
        // le plus court qui contourne l'élément, à égalité orthogonale puis diagonale) ; pêche -> case à portée. Centralisé dans HarvestStandCell (partagé récolte/zaap/HDV).
        if (HarvestStandCell(pid, mapId, source, interactive, isWalkable, stepAllowed, out var moveWalkable) is not int best)
        {
            TransportLog.Line($"[recolte] pid {pid} : aucune cellule d'interaction pour {interactive.Name} (element {element}, cellule {interactive.CellId})");
            return false;
        }
        // Déjà bien placé (case du perso = case d'interaction / à portée) -> iwo direct, sans bouger.
        if (best == source)
            return SendInteractiveUse(pid, skillUid, element, interactive.Name, IsGatherResource(interactive));

        // moveWalkable exclut la cellule de l'élément : le chemin ne traverse jamais la plante (même prédicat que le choix).
        long armedSeq = 0;
        bool moved = MoveTo(pid, mapId, source, best, moveWalkable,
            allowDiag: true, stepAllowed: stepAllowed, beforeInject: seq =>
            {
                lock (ArrivalActionGate)
                {
                    var owner = ScriptActionOwner.Value;
                    if (owner.IsCancellationRequested || _pendingHarvest.ContainsKey(pid) ||
                        _pendingMapChange.ContainsKey(pid)) return false;
                    armedSeq = seq;
                    _harvestArrivalIntentV21[pid] = NewArrivalIntentV21(pid, seq,
                        mapId, source, best, owner);
                    _pendingHarvest[pid] = (skillUid, element,
                        IsGatherResource(interactive), owner);
                    return true;
                }
            });
        if (moved)
        {
            TransportLog.Line($"[recolte] pid {pid} : marche {source}->{best}, utilisation de {interactive.Name} (element {element}, skillUid {skillUid}) en attente d'une confirmation INDIVIDUELLEMENT attribuable (V21)");
        }
        else
        {
            if (armedSeq != 0)
                lock (ArrivalActionGate)
                    if (_harvestArrivalIntentV21.TryGetValue(pid, out var ticket) && ticket.MoveSeq == armedSeq)
                    {
                        _harvestArrivalIntentV21.TryRemove(pid, out _);
                        _pendingHarvest.TryRemove(pid, out _);
                    }
            TransportLog.Line($"[recolte] pid {pid} : impossible de marcher vers {best} (pas de chemin ou deplacement en cours)");
        }
        return moved;
    }

    /// <summary>Cellule OU se placer pour recolter: la case du perso s'il est deja a
    /// portee, sinon la case marchable a portee la plus proche ET atteignable, sinon null (injoignable). Pour le scripting :
    /// permet d'AWAIT le deplacement AVANT d'envoyer l'iwo -> recolte STRICTEMENT sequentielle (aucun chevauchement).
    /// <paramref name="moveWalkable"/> : prédicat à passer au MoveTo vers la case rendue (exclut la cellule de l'élément,
    /// cf <c>MapPathfinder.StandCellFor</c>).</summary>
    public int? HarvestStandCell(uint pid, long mapId, int source, MapInteractive interactive, Func<int, bool> isWalkable,
                                 Func<int, int, bool>? stepAllowed, out Func<int, bool> moveWalkable)
    {
        moveWalkable = isWalkable;
        return interactive.HarvestSkill is { } s
            ? InteractiveStandCell(pid, mapId, source, interactive, s, isWalkable, stepAllowed, out moveWalkable)
            : null;
    }

    /// <summary>Case d'où utiliser <paramref name="it"/> avec un skill EXPLICITE (primitive générique map:useById). Même
    /// logique que la récolte (<see cref="MapPathfinder.StandCellFor"/>) : PORTÉE 1 (ressource / zaap / porte / mécanisme /
    /// hôtelier…) -> une VOISINE de l'élément dans le 8-voisinage (déjà voisin -> sur place ; sinon la voisine au chemin le
    /// plus court, à égalité orthogonale puis diagonale), JAMAIS sa propre cellule (plantes sur cases marchables : le bot marchait dessus depuis 09-22).
    /// La cellule lue dans la mapData (<see cref="InteractionCell"/>) n'est retenue que si c'est une vraie case d'appui
    /// (distincte ET voisine) — en pratique c'est la cellule de l'élément lui-même, donc ignorée. Pêche (range 10) : on
    /// interagit à distance. Bundle lu seulement si portée 1 (paresseux). Portée 1 : la voisine au chemin le plus COURT qui
    /// contourne l'élément ; <paramref name="moveWalkable"/> (cellule de l'élément exclue) DOIT être passé au MoveTo.</summary>
    public int? InteractiveStandCell(uint pid, long mapId, int source, MapInteractive it, InteractiveSkill skill,
                                     Func<int, bool> isWalkable, Func<int, int, bool>? stepAllowed, out Func<int, bool> moveWalkable)
    {
        int range = _gameNames.SkillRange(skill.SkillId);
        int? canon = range <= 1 ? InteractionCell(mapId, it.ElementId) : null;
        return MapPathfinder.StandCellFor(source, it.CellId, range, canon, isWalkable, stepAllowed, out moveWalkable);
    }

    /// <summary>Utilise <paramref name="it"/> avec <paramref name="skill"/> MAINTENANT (primitive map:useById) — l'appelant
    /// a DÉJÀ rejoint la case d'interaction. Gate au POINT D'ACTION : Perm.Gather (socle interactifs). Compté comme récolte
    /// (activité + cap de niveau) seulement si le skill produit un objet récolté. false = refusé / non envoyé.</summary>
    private readonly ConcurrentDictionary<uint, (long Map, IReadOnlyList<MapInteractive> Items)> _placelessInteractives = new();

    /// <summary>Interactifs annoncés par le serveur sur la carte courante dont la CELLULE est inconnue (absents de la mapData du
    /// client et du world-graph) : inaffichables, mais utilisables à distance comme le fait le jeu (hôteliers d'HDV notamment).</summary>
    public IReadOnlyList<MapInteractive> PlacelessInteractives(uint pid, long map)
        => _placelessInteractives.TryGetValue(pid, out var e) && e.Map == map ? e.Items : Array.Empty<MapInteractive>();

    public bool UseInteractiveElement(uint pid, MapInteractive it, InteractiveSkill skill)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        if (IsInFight(pid) || !skill.Enabled || !it.Skills.Contains(skill)) return false;
        return SendInteractiveUse(pid, skill.SkillInstanceUid, it.ElementId, it.Name, IsGatherSkill(it, skill));
    }

    /// <summary>Le skill <paramref name="skill"/> ENVOYÉ sur <paramref name="it"/> est-il une récolte (produit un objet
    /// récolté) ? Données de skills non chargées : repli sur <see cref="IsGatherResource"/>. Règle UNIQUE partagée par le
    /// moteur (activité/cap de niveau) et l'hôte Lua (attente de fin de récolte).</summary>
    public bool IsGatherSkill(MapInteractive it, InteractiveSkill skill)
        => _gameNames.SkillDataLoaded ? _gameNames.GatheredItemForSkill(skill.SkillId) > 0 : IsGatherResource(it);

    /// <summary>Cellule de l'élément dans la mapData du client pour un element_id (m_interactionId → cellId, via
    /// <see cref="MapCellReader.LoadInteractivePositions"/>). ATTENTION : c'est la cellule d'ANCRAGE de l'élément (égale à
    /// sa cellule protocolaire pour ~100 % des éléments de la carte courante), PAS une case où se tenir ; la table peut aussi
    /// contenir des éléments de cartes voisines à des cellules sans rapport. Null si inconnue. Cachée par map.</summary>
    public int? InteractionCell(long mapId, int elementId)
        => _cellReader.LoadInteractivePositions(mapId) is { } p && p.TryGetValue(elementId, out var c) ? c : (int?)null;

    /// <summary>Case où se placer pour franchir une PORTE (transition world-graph type 32). D'abord la règle commune
    /// (<see cref="HarvestStandCell"/>) : une VOISINE de la porte (8-voisinage) au chemin le plus court, à égalité
    /// orthogonale puis diagonale — les portes dont la seule voisine marchable est en diagonale (352 -&gt; 380 sur
    /// 206177286, 554 -&gt; 555 sur 212861952) donnaient « porte injoignable ». Une diagonale strictement plus proche peut
    /// désormais être préférée à l'orthogonale choisie avant (le serveur accepte les deux). Dernier
    /// recours seulement (ancien comportement 09-12) : la cellule mapData de l'élément si elle est DISTINCTE de la tuile de
    /// la porte, marchable et joignable -&gt; on marche dessus. <paramref name="moveWalkable"/> : prédicat à passer au MoveTo
    /// (voisine : cellule de la porte exclue — sans effet, une porte n'est pas marchable ; dernier recours : prédicat brut,
    /// celui qui a prouvé le chemin).</summary>
    public int? DoorStandCell(uint pid, long mapId, int source, MapInteractive door, Func<int, bool> isWalkable,
                              Func<int, int, bool>? stepAllowed, out Func<int, bool> moveWalkable)
    {
        if (HarvestStandCell(pid, mapId, source, door, isWalkable, stepAllowed, out moveWalkable) is int stand) return stand;   // voisine (8-voisinage)
        moveWalkable = isWalkable;
        if (InteractionCell(mapId, door.ElementId) is int cell && cell != door.CellId
            && (uint)cell < MapPathfinder.CellCount && isWalkable(cell))
        {
            if (cell == source) return source;                                                        // déjà sur la case
            if (MapPathfinder.KeyMovements(source, cell, isWalkable, allowDiag: true, stepAllowed).Count > 0) return cell;   // joignable → on marche DESSUS
        }
        return null;
    }

    /// <summary>Envoie l'InteractiveUseRequest (iwo) MAINTENANT — le perso DOIT deja etre a portee. Pour le scripting
    /// (l'hote Lua a deja awaite l'arrivee). false si l'interactif n'a pas de skill actif.</summary>
    public bool SendHarvest(uint pid, MapInteractive interactive)
        => interactive.HarvestSkill is { } skill && SendInteractiveUse(pid, skill.SkillInstanceUid, interactive.ElementId, interactive.Name, IsGatherResource(interactive));

    /// <summary>Utilise un interactif avec un skill EXPLICITE (ex zaapi = skill 157) : le skill actif `skillId` s'il existe,
    /// sinon le skill par défaut (HarvestSkill). Jamais compté comme récolte. Le perso DOIT déjà être à portée.</summary>
    public bool SendInteractiveSkill(uint pid, MapInteractive it, int skillId)
    {
        var sk = it.Skills.FirstOrDefault(s => s.SkillId == skillId && s.Enabled) ?? it.HarvestSkill;
        return sk is not null && SendInteractiveUse(pid, sk.SkillInstanceUid, it.ElementId, it.Name, isGather: false);
    }

    private bool SendInteractiveUse(uint pid, int skillInstanceUid, int elementId, string? name = null, bool isGather = false)
    {
        if (isGather && LevelBlocked(pid)) return false;   // cap de niveau licence (Free > 30) : plus de récolte au POINT D'ACTION
        var relay = RelayForInject();
        if (relay is null) return false;
        byte[] pkt = MoveForge.BuildInteractiveUseFramed(skillInstanceUid, elementId);   // InteractiveUseRequest {element_id #1, skill_instance_uid #5}
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        // Activité « Récolte » UNIQUEMENT pour une vraie ressource (isGather) : un zaap/porte/PNJ passe aussi par ici
        // (SendHarvest) mais ne doit PAS afficher « Récolte » (cf voyage par zaap -> ouverture zaap = InteractiveUseRequest).
        if (ok && isGather) { _gatherUntil[pid] = Environment.TickCount64 + GatherWindowMs; RaiseActivity(pid); }   // "En récolte" immédiat (fermée à l'iwi/iwq)
        TransportLog.Line($"[recolte] pid {pid} : InteractiveUseRequest {name} element {elementId}, skillUid {skillInstanceUid} inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    /// <summary>Entre dans le HAVRE-SAC (forge jbn avec owner=characterId capté). false si charId inconnu / pas de relais.</summary>
    public bool EnterHavenBag(uint pid)
    {
        var relay = RelayForInject();
        if (relay is null) return false;
        long owner = CharacterId(pid);
        if (owner == 0) { TransportLog.Line($"[havre-sac] pid {pid} : characterId inconnu (reconnexion nécessaire pour le capter)"); return false; }
        string? code = _catalog?.CodeForName("HavenBagEnterRequest");   // code résolu du mapping STREAMÉ (pas de hardcode)
        if (code is null) { TransportLog.Line($"[havre-sac] pid {pid} : code HavenBagEnterRequest non mappé (session ?)"); return false; }
        byte[] pkt = MoveForge.BuildHavenBagEnterFramed(code, owner);   // {owner @#2}
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[havre-sac] pid {pid} : {ProtoLog.Named("HavenBagEnterRequest", code)} owner {owner} inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    /// <summary>Ferme un dialogue ouvert (zaap du havre-sac, PNJ, coffre…) via DialogLeaveRequest. À appeler AVANT de
    /// sortir du havre-sac : tant qu'un dialogue (ex zaap qu'on vient d'ouvrir) est actif, le serveur REFUSE la sortie
    /// (TextInformationEvent « action impossible ») et le perso reste coincé dedans.</summary>
    public bool LeaveDialog(uint pid)
    {
        var relay = RelayForInject(capExempt: true);   // sortie propre d'un dialogue, même pour un perso capé
        if (relay is null) return false;
        _lastNpcDialog.TryRemove(pid, out _);   // oublie le dialogue mémorisé (replyNpc) dès qu'on ferme
        if (HdvAnyOpen(pid)) ResetHdvState(pid);   // notre propre fermeture ne repasse pas par le décodeur
        _teleportOpen.TryRemove(pid, out _);    // idem pour le dialogue de téléportation (zaap / zaapi / havre-sac)
        byte[] pkt = MoveForge.BuildDialogLeaveFramed();
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[dialogue] pid {pid} : DialogLeaveRequest inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    /// <summary>Téléporte via un zaap OUVERT (au sol ou celui du havre-sac) vers `mapId` (forge hjc). sourceType : 0=ZAAP
    /// (champ omis), 3=HAVEN_BAG. PRÉREQUIS : zaap ouvert (iwo) ET `mapId` ∈ destinations connues (hjj) — cf CanTeleportTo.</summary>
    public bool Teleport(uint pid, long mapId, int sourceType = 0, int destinationType = 0)
    {
        // All call paths (including Lua and direct control tools) go through
        // this lower-level method. The anomaly tab has no verified wire action:
        // listing a destination is NOT authorization to forge a teleport.
        if (!CanTeleportTo(pid, mapId) || sourceType == 4 || destinationType == 4)
        {
            TransportLog.Line($"[zaap] pid {pid} : téléportation refusée (dialogue absent, destination non confirmée ou anomalie non supportée).");
            return false;
        }
        var relay = RelayForInject();
        if (relay is null) return false;
        string? code = _catalog?.CodeForName("TeleportRequest");   // code résolu du mapping STREAMÉ (pas de hardcode)
        if (code is null) { TransportLog.Line($"[zaap] pid {pid} : code TeleportRequest non mappé (session ?)"); return false; }
        byte[] pkt = MoveForge.BuildTeleportFramed(code, mapId, sourceType, destinationType);   // { source_type, destination_type (omis si 0), map_id } par NOM
        long smithCost = 0;
        if (InSmithSpending)
        {
            var destination = TeleportView(pid)?.Destinations.FirstOrDefault(d => d.MapId == mapId);
            if (_catalog?.FieldNumber("TeleportDestination", "cost") is not > 0
                || destination is null || destination.Value.MapId != mapId || destination.Value.Cost < 0) return false;
            smithCost = destination.Value.Cost;
        }
        bool ok = SendSmithSpend(pid, "travel", smithCost, 0, 0, 0, mapId, () => relay.Inject(pid, pkt), out _);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[zaap] pid {pid} : {ProtoLog.Named("TeleportRequest", code)} -> map {mapId} (src {sourceType}{(destinationType != 0 ? $", dst {destinationType}" : "")}) inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    // Destinations de téléport connues (map_id des zaaps) par pid — remplies par TeleportDestinationsEvent (hjj) à
    // l'ouverture d'un zaap / du havre-sac. Sert au raccourci de voyage "havre-sac -> zaap le plus proche -> marche le reste".
    private readonly ConcurrentDictionary<uint, IReadOnlyCollection<long>> _knownZaaps = new();
    // Destinations du DERNIER dialogue de téléport ouvert, QUEL QUE SOIT son type (zaap, zaapi, prisme…) : c'est ce que le
    // serveur accepte comme cible de hjc tant que ce dialogue est ouvert (verbes Lua zaap:canGoTo/travel). _knownZaaps, lui,
    // ne garde QUE les listes de ZAAPS (choix du zaap le plus proche de la cible) -> une liste de zaapis ne l'écrase plus.
    private readonly ConcurrentDictionary<uint, IReadOnlyCollection<long>> _openTeleportDests = new();
    // Compteur de listes de ZAAPS reçues (hjj acceptés) par pid : « la liste est arrivée » = le compteur a AUGMENTÉ depuis
    // l'ouverture. Avant, on attendait KnownZaapMaps.Count > 0 — jamais vidé -> vrai tout de suite dès qu'une liste avait été
    // vue ce login, donc décision sur une liste PÉRIMÉE (et téléport envoyé sans zaap réellement ouvert).
    private readonly ConcurrentDictionary<uint, long> _zaapListSeq = new();
    // Compteur de dialogues de téléport reçus (TOUS types : zaap, zaapi, prisme…) + type de la DERNIÈRE liste. Sert au
    // verbe zaapi:go (une liste de zaapis n'incrémente PAS _zaapListSeq, volontairement).
    private readonly ConcurrentDictionary<uint, long> _teleportDialogSeq = new();
    private readonly ConcurrentDictionary<uint, int> _openTeleportType = new();
    /// <summary>N° de séquence du dernier dialogue de téléport reçu, quel que soit son type.</summary>
    public long TeleportDialogSeq(uint pid) => _teleportDialogSeq.TryGetValue(pid, out var s) ? s : 0;
    /// <summary>Type (Teleporter) de la dernière liste de destinations reçue, -1 si inconnu.</summary>
    public int OpenTeleportType(uint pid) => _openTeleportType.TryGetValue(pid, out var t) ? t : -1;
    /// <summary>Cartes de zaap connues (téléportables) du perso ; vide tant qu'aucun zaap/hjj n'a été vu ce login.</summary>
    public IReadOnlyCollection<long> KnownZaapMaps(uint pid) => _knownZaaps.TryGetValue(pid, out var z) ? z : System.Array.Empty<long>();

    // Zaaps débloqués (ZaapKnownListEvent à la connexion) : cibles autorisées de la téléportation DIRECTE.
    private readonly ConcurrentDictionary<uint, IReadOnlyCollection<long>> _directZaaps = new();
    public IReadOnlyCollection<long> DirectZaapMaps(uint pid) => _directZaaps.TryGetValue(pid, out var z) ? z : System.Array.Empty<long>();

    /// <summary>Téléportation depuis la carte du monde : TeleportRequest { map_id, source_type = 5 }, sans zaap ouvert.
    /// La destination doit être dans la liste TeleportDestinationsEvent type 0 ou 3, avec un coût payable.
    /// true = paquet injecté. L'arrivée se juge sur le changement de carte, pas sur cet envoi.
    /// Pendant une dépense de forgemagie, refus immédiat (le test de budget ne doit rien injecter ici).</summary>
    public bool TeleportDirect(uint pid, long mapId)
    {
        if (InSmithSpending) return false;
        return TrySendWorldMapZaap(pid, mapId);
    }
    public const int DirectTeleportSource = WorldMapZaapTravel.SourceType;
    /// <summary>N° de séquence de la dernière liste de ZAAPS reçue (s'incrémente à chaque hjj de type zaap accepté).</summary>
    public long ZaapListSeq(uint pid) => _zaapListSeq.TryGetValue(pid, out var s) ? s : 0;
    /// <summary>Vrai si `mapId` est une destination du dialogue de téléport ouvert (garde anti-refus AVANT d'envoyer hjc).</summary>
    public bool CanTeleportTo(uint pid, long mapId)
    {
        var view = TeleportView(pid);
        return view is not null &&
            TeleportActionPolicy.CanSend(view.Type, OpenTeleportType(pid), view.OpenedOnMap,
                CurrentMap(pid) ?? 0, mapId,
                _openTeleportDests.TryGetValue(pid, out var listed) && listed.Contains(mapId));
    }
}
