using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Engine.LuaTooling;
using GivreClone.Protocol;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// ===================== Récolte : ressources, camping de carte, sacs de ressources, maintenance des pods =====================
public sealed partial class GivreLuaHost
{
    // Recolte UNE ressource en DEUX etapes AWAITEES, exactement (MoveAndGather = MovePlayerOnCellId PUIS
    // ForgeAndSendGatherMessage) :
    //   1) SE PLACER a portee et ATTENDRE l'arrivee (jsq) — bloquant. Tant qu'on n'est pas arrive, on N'ENVOIE PAS l'iwo.
    //   2) ENVOYER l'iwo et ATTENDRE la fin reelle (itn objet OU iwi fin d'anim OU iwq echec OU combat OU timeout).
    // C'est ce qui empeche le CHEVAUCHEMENT (long deplacement + event tardif de la ressource precedente) qui faisait
    // repartir la boucle trop tot -> iwo direct sur une cellule optimiste -> refus serveur en cascade.
    private bool GatherResource(MapInteractive inter, CancellationToken ct, bool verbose,
        bool strictRecovery = false)
    {
        ThrowIfStopping(ct);
        strictRecovery |= ActivityRecoveryGlobalEnabled;
        // A restarted legacy Lua script can call gatherAll() at top level,
        // before RunLoop reaches its guarded declarative route. The durable
        // pending record must quarantine this shared harvest entrypoint too.
        if (RecoveryBlocksRoute(checkDurable: true, ownerToken: ct)) return false;
        _behaviouralCircuitBreaker.CheckAndApplyPauses(ms => InterruptibleDelay(ms, ct, pump: true), msg => { Info("recolte", msg); GivreLauncher.Core.Transport.TransportLog.Line(_pid, "[circuit] " + msg); });
        // Droit serveur "gather" (audit P1-2 : gater au POINT D'ACTION, pas juste dans l'UI). GatherResource est LE
        // point de passage unique de toute récolte (gatherAll / goUseInteractive / étape de trajet) -> refuser ici
        // bloque la récolte quel que soit le chemin, sans casser les transitions de carte par porte (SendHarvest :396).
        bool offlineGatherPermission = strictRecovery &&
            ActivityRecoveryOfflineDispatchForTests is not null &&
            ActivityRecoveryOfflineGatherAllowedForTests;
        if (!offlineGatherPermission && !LicenseService.HasPermission(LicenseService.Perm.Gather))
        {
            if (verbose) Warn("recolte", Loc.T("L.Lua.NoPermGather"));
            return false;
        }
        WaitSelfReady(ct);   // attend la position self confirmee (surtout juste apres un changement de carte)
        var st = _world.Get(_pid);
        if (!st.PodsAuthoritative)
        {
            if (WaitUntil(() => _world.Get(_pid).PodsAuthoritative, 5000, ct))
            {
                st = _world.Get(_pid);
            }
            else
            {
                if (strictRecovery)
                {
                    NoteActivityRecoveryRunBlockV18(
                        ActivityRecoveryTransitionReasonV18.PodsUnconfirmed, ct,
                        "pods serveur absents apres attente pour recolte");
                    RecoveryWarning("pods non confirmes apres attente ; aucune recolte stricte");
                    StopRecoveryPlanningOnce(ct);
                    return false;
                }
                Warn("recolte", "pods non reçus du serveur, récolte sans contrôle de pods");
            }
        }
        if (strictRecovery && st.PodsAuthoritative &&
            st.PodsPercent >= Math.Clamp(
                _script is { } script ? ReadInt(script, "MAX_PODS", 95) : 95, 1, 100))
        {
            NoteActivityRecoveryRunBlockV18(
                ActivityRecoveryTransitionReasonV18.PodsAtThreshold, ct,
                "pods au seuil avant reserve de recolte");
            RecoveryWarning("pods au seuil avant admission de recolte ; aucun iwo");
            StopRecoveryPlanningOnce(ct);
            return false;
        }
        long map = _launch.CurrentMap(_pid) ?? 0;
        if (strictRecovery && _launch.InteractivesDecodedMap(_pid) != map)
        {
            RecoveryWarning("interactifs non attestes pour cette carte ; recolte non envoyee");
            return false;
        }
        if (map == 0 || st.SelfCell is not int src) { if (verbose) Warn("recolte", Loc.T("L.Lua.HarvestUnknownCell")); return false; }
        if (!inter.Harvestable) { if (verbose) Warn("recolte", Loc.Fmt("L.Lua.HarvestNothing", inter.CellId)); return false; }
        string what = inter.Name ?? $"cellule {inter.CellId}";
        Info("recolte", Loc.Fmt("L.Lua.HarvestStart", what, inter.CellId));

        // ---- Etape 1 : se placer a portee, en ATTENDANT l'arrivee (aucun iwo tant que le perso n'est pas arrive) ----
        // standWalk = IsWalkableForScript PRIVÉ de la cellule de la ressource : le chemin vers la voisine ne traverse jamais
        // la plante (avant : A* sur IsWalkableForScript brut -> 212>…>338>337>322, le bot marchait SUR l'Ortie en 337).
        int? stand = _launch.HarvestStandCell(_pid, map, src, inter, st.IsWalkableForScript, st.StepAllowed, out var standWalk);
        if (stand is null)
        {
            if (strictRecovery)
                NoteActivityRecoveryRunBlockV18(
                    ActivityRecoveryTransitionReasonV18.ResourceOutOfReach, ct,
                    "ressource hors d'atteinte par une cellule de recolte");
            if (verbose) Warn("recolte", Loc.Fmt("L.Lua.HarvestNoReach", what));
            return false;
        }
        // Dans ACTIVITY_RECOVERY, aucun mouvement de placement suivi d'un iwo n'est admis :
        // l'ACK du mouvement n'expose pas sa sequence au LuaHost. Exiger la portee DEJA acquise.
        if (strictRecovery && stand.Value != src)
        {
            NoteActivityRecoveryRunBlockV18(
                ActivityRecoveryTransitionReasonV18.ResourceOutOfReach, ct,
                "recolte exigerait un placement puis une action composite");
            RecoveryWarning($"ressource {inter.ElementId} hors de portee ; segment composite interdit");
            return false;
        }
        if (stand.Value != src)
        {
            int standCell = stand.Value;
            var mv = _launch.SendAndWaitAsync(_pid, () => _launch.MoveTo(_pid, map, src, standCell, standWalk, allowDiag: true, st.StepAllowed), MoveTimeoutMs, ct)
                            .GetAwaiter().GetResult();
            ThrowIfStopping(ct);
            if (mv != LaunchService.MoveOutcome.Arrived) { if (verbose) Warn("recolte", Loc.Fmt("L.Lua.HarvestMoveFail", what, mv)); return false; }
        }

        // Le déplacement laisse le temps à la carte/ressource de changer : ne jamais envoyer
        // l'identifiant d'un ancien snapshot (ressource prise par un autre joueur, changement de carte).
        if (_launch.CurrentMap(_pid) != map) return false;
        var current = _world.Get(_pid).Interactives.FirstOrDefault(i => i.ElementId == inter.ElementId);
        if (current is null || !current.Harvestable || current.HarvestSkill is null) return false;
        if (strictRecovery)
        {
            // La selection initiale ne suffit pas si le skill/element a ete republie
            // pendant WaitSelfReady : ne jamais envoyer depuis une ancienne cellule.
            if (current.CellId != inter.CellId ||
                current.HarvestSkill.SkillInstanceUid != inter.HarvestSkill?.SkillInstanceUid ||
                _launch.HarvestStandCell(_pid, map, src, current, st.IsWalkableForScript,
                    st.StepAllowed, out _) != src)
            {
                RecoveryWarning("resource/skill/portee modifies depuis la selection ; aucun iwo");
                return false;
            }
        }
        inter = current;

        // ---- Etape 2 : recolter (iwo) et ATTENDRE la FIN D'ANIMATION (iwi InteractiveUseEnded = perso libre) DE CET
        // element PRECIS (match par element_id). On N'ATTEND PAS l'item obtenu (itn ObjectHarvested):
        //      l'itn arrive APRES l'iwi et porte l'item gid (IDENTIQUE pour 2 Ble) -> il n'identifie PAS la ressource ; un
        //      itn/iwi TARDIF de la ressource PRECEDENTE faisait sortir la courante ~0,1 s apres l'iwo -> perso encore en
        //      anim -> move suivant REFUSE. En matchant l'iwi (ou l'iwq) A L'element_id courant, un event d'une AUTRE
        //      ressource est ignore. On remet les marqueurs a 0 juste avant l'iwo. iwi couvre AUSSI la recolte AVEC BONUS.
        int elementId = inter.ElementId;
        Volatile.Write(ref _endedElement, 0);
        Volatile.Write(ref _errorElement, 0);
        Interlocked.Exchange(ref _lastHarvestQty, 0);   // remet a 0 le cumul de quantite pour CETTE recolte
        RecoveryDispatchLease gatherLease = default;
        if (strictRecovery && !ReserveRecovery(new ActivityRecoverySegment(
                ActivityRecoveryKind.Gather, elementId, inter.CellId, elementId), ct, out gatherLease,
                inReach: true, requiredSource: src))
            return false;
        try
        {
        if (strictRecovery && !RecoveryMaySend(gatherLease, ct))
        {
            RecoveryWarning("session/ressource alteree apres reservation ; iwo annule");
            StopRecoveryPlanningOnce(ct);
            return false;
        }
        if (strictRecovery && CaptureOfflineRecoveryDispatch(new ActivityRecoverySegment(
                ActivityRecoveryKind.Gather, elementId, inter.CellId, elementId)))
            return true;
        if (!_launch.SendHarvest(_pid, inter)) { if (verbose) Warn("recolte", Loc.Fmt("L.Lua.HarvestSendFail", what)); return false; }
        WaitUntil(() =>
            Volatile.Read(ref _endedElement) == elementId   // iwi InteractiveUseEnded POUR CET element = perso libre
            || Volatile.Read(ref _errorElement) == elementId // iwq InteractiveUseError POUR CET element = echec
            || _launch.IsInFight(_pid),                      // aggro
            HarvestTimeoutMs, ct);
        bool ended = Volatile.Read(ref _endedElement) == elementId;
        bool errored = Volatile.Read(ref _errorElement) == elementId;
        if (errored && !ended) { if (verbose) Warn("recolte", Loc.Fmt("L.Lua.HarvestRefused", what)); return false; }
        if (ended)
        {
            // L'itn (ObjectHarvested, quantite) arrive PARFOIS juste APRES l'iwi -> on lui laisse un court delai de grace
            // pour ne pas logger "Récolté Blé" sans la quantite (le cas ou qty=0 au moment de l'iwi).
            if (Volatile.Read(ref _lastHarvestQty) == 0)
                WaitUntil(() => Volatile.Read(ref _lastHarvestQty) > 0, 700, ct);
            int qty = Volatile.Read(ref _lastHarvestQty);
            // Recolte coupee par une AGGRO : l'itn/iwc (quantite) n'arrive pas -> ne pas pretendre "Récolté" a tort.
            if (qty == 0 && _launch.IsInFight(_pid)) { Warn("recolte", Loc.Fmt("L.Lua.HarvestInterrupted", what)); return true; }
            Ok("recolte", qty > 0 ? Loc.Fmt("L.Lua.HarvestOkQty", qty, what) : Loc.Fmt("L.Lua.HarvestOk", what));
            return true;
        }
        if (!_launch.IsInFight(_pid)) Warn("recolte", Loc.Fmt("L.Lua.HarvestUnconfirmed", what));
        return false;
        }
        finally
        {
            // iwi/iwq/itn sont des observations utiles, mais pas une sequence ACK.
            // Le lease reste ferme meme si l'envoi a retourne false ou s'est interrompu.
            if (strictRecovery) RecordRecoveryEffect(elementId, inter.CellId, ct, gatherLease);
        }
    }

    // Ressources injoignables sur la carte COURANTE (echec alors qu'elles restent recoltables) -> on ne les retente plus.
    private readonly HashSet<int> _gatherBlocked = new();
    private long _gatherBlockedMap = -1;

    // Recolte UNE seule ressource (la plus proche non bloquee, filtree par ELEMENTS_TO_GATHER), facon HandleGather d'un bot tiers.
    // Renvoie true si une ressource a ete tentee (le tick suivant en prendra une autre), false si plus rien a recolter.
    private bool GatherOne(CancellationToken ct, bool strictRecovery = false)
    {
        strictRecovery |= ActivityRecoveryGlobalEnabled;
        if (RecoveryBlocksRoute(checkDurable: true, ownerToken: ct)) return true;
        long map = _launch.CurrentMap(_pid) ?? 0;
        // Les interactifs sont décodés en TÂCHE DE FOND (chargement de bundle). Avant de conclure « rien à récolter »
        // (ce qui ferait changer de carte), on ATTEND que le décodage soit prêt POUR LA CARTE COURANTE — sinon on skip
        // des ressources bien présentes (bug « belladone présente mais ignorée » : la liste était encore vide/périmée).
        bool decoded = WaitUntil(() => _launch.InteractivesDecodedMap(_pid) == map, InteractivesDecodeTimeoutMs, ct);
        if (strictRecovery && (!decoded || _launch.InteractivesDecodedMap(_pid) != map))
        {
            NoteActivityRecoveryRunBlockV18(
                ActivityRecoveryTransitionReasonV18.MapUnconfirmed, ct,
                "interactifs non decodes pour la carte de recolte");
            RecoveryWarning("interactifs non decodes pour carte courante ; aucune recolte");
            StopRecoveryPlanningOnce(ct);
            return false;
        }
        if (map != _gatherBlockedMap) { _gatherBlockedMap = map; _gatherBlocked.Clear(); }   // nouvelle carte -> reset

        var filter = GatherFilter();
        var st = _world.Get(_pid);
        if (strictRecovery &&
            (map == 0 || st.MapId != map || !st.SelfReadyFor(map) ||
             !st.CellsReadyFor(map) || _launch.CurrentMap(_pid) != map))
        {
            NoteActivityRecoveryRunBlockV18(
                ActivityRecoveryTransitionReasonV18.MapUnconfirmed, ct,
                "carte/position non confirmees apres decodage");
            RecoveryWarning("carte/position non confirmees apres decodage des ressources ; aucun choix");
            if (map == 0 || st.MapId != map || _launch.CurrentMap(_pid) != map)
                StopRecoveryPlanningOnce(ct);
            return false;
        }
        int src = st.SelfCell ?? -1;
        // On ne considère JAMAIS un interactif non-récoltable (porte, atelier, zaap, coffre, enclume, établi…) comme une
        // ressource. Filtre EXPLICITE (ELEMENTS_TO_GATHER non vide) : l'utilisateur a nommé l'ElementTypeId -> on lui fait
        // confiance (skill actif requis). Mode « RÉCOLTE TOUT » (filtre vide) : STRICT — on EXCLUT les actions PASSIVES qui
        // ne récoltent RIEN (le bot ne doit pas cliquer « Utiliser » = traverser une porte / changer de carte / activer).
        // Signaux ROBUSTES même quand les données de skills du jeu ne sont pas chargées (fail-open de IsGatherResource) :
        //   (a) cellule qui est une TRANSITION du world-graph (porte/passage) ; (b) skill générique/« Utiliser » (184) connu.
        // Si les données de skills SONT là, on exige EN PLUS que le skill produise un objet récolté (gatheredRessourceItem>0).
        bool Wanted(MapInteractive i) => IsWantedResource(i, map, filter);
        var candidates = st.Interactives
            .Where(i => i.Harvestable && !_gatherBlocked.Contains(i.ElementId) && Wanted(i))
            .ToList();
        if (strictRecovery && candidates.Count > 0)
        {
            // A table-only route has ONE durable action available. An ordinary
            // GatherOne may walk toward its closest resource before harvesting,
            // which would escape this single-action admission. Prefer only
            // elements already usable from the server-confirmed self cell;
            // if none are at range, let the declarative cell step admit a single
            // movement instead of blacklisting a still-live distant resource.
            candidates = candidates.Where(i =>
                _launch.HarvestStandCell(_pid, map, src, i,
                    st.IsWalkableForScript, st.StepAllowed, out _) == src).ToList();
        }
        if (candidates.Count == 0)
        {
            int matchable = st.Interactives.Count(i => i.Harvestable && Wanted(i));
            if (strictRecovery)
                NoteActivityRecoveryRunBlockV18(
                    matchable > 0
                        ? ActivityRecoveryTransitionReasonV18.ResourceOutOfReach
                        : ActivityRecoveryTransitionReasonV18.ResourceUnavailable,
                    ct, matchable > 0
                        ? "ressources voulues presentes mais hors portee serveur"
                        : "aucune ressource voulue recoltable dans la carte decodee");
            // "récoltable" au sens strict : vraies ressources (pas les portes/ateliers) -> le diag ne les compte plus.
            var present = st.Interactives.Where(i => i.Harvestable && _launch.IsGatherResource(i)).ToList();
            if (matchable == 0 && present.Count > 0 && filter.Count > 0)
            {
                // AIDE : on liste les ElementTypeId RÉELLEMENT présents ici -> l'utilisateur voit quels ids mettre dans
                // ELEMENTS_TO_GATHER (ex : les poissons de cette carte ne sont pas les 81/263/264 attendus).
                string types = string.Join(",", present.Select(i => i.ElementTypeId).Distinct().OrderBy(x => x));
                Warn("recolte", Loc.Fmt("L.Lua.HarvestNoMatch", present.Count, string.Join(",", filter), types));
            }
            return false;
        }
        var target = candidates.OrderBy(i => src >= 0 ? MapPathfinder.CellDistance(src, i.CellId) : i.CellId).First();
        bool ok = GatherResource(target, ct, verbose: true, strictRecovery: strictRecovery);
        if (!ok && st.Interactives.FirstOrDefault(i => i.ElementId == target.ElementId)?.Harvestable == true)
        {
            _gatherBlocked.Add(target.ElementId);   // toujours recoltable apres echec = injoignable -> on l'ignore sur cette carte
            Warn("recolte", Loc.Fmt("L.Lua.HarvestUnreachable", target.Name ?? ("element " + target.ElementId)));
        }
        return true;
    }

    // Filtre « ressource voulue » de GatherOne (factorisé pour CampWait, comportement inchangé) : voir le commentaire de GatherOne.
    private bool IsWantedResource(MapInteractive i, long map, HashSet<int> filter)
    {
        if (i.HarvestSkill is not { } hs || !_launch.IsGatherResource(i)) return false;
        if (filter.Count > 0) return filter.Contains(i.ElementTypeId);
        // Generated routes use item GIDs, whereas ELEMENTS_TO_GATHER uses interactive
        // type IDs. Keep those namespaces separate; unknown skill data must not
        // silently turn a requested crop into "gather everything".
        if (_script?.Globals.Get("ITEMS_TO_GATHER") is { Type: DataType.Table } items && items.Table.Values.Any())
        {
            int gid = _launch.GatheredItemGid(i);
            return gid > 0 && items.Table.Values.Any(v => v.Type == DataType.Number && v.Number == gid);
        }
        if (_launch.IsWorldTransitionCell(map, i.CellId)) return false;              // (a) porte / changement de carte
        if (hs.SkillId is 184 or 339 or 360 or 361 or 362) return false;             // (b) « Utiliser » + skills génériques
        return !_launch.SkillDataLoaded || _launch.ProducesGatheredItem(i);          // strict si données de skills dispo
    }

    // ---- Lot E : étape forcegather / forcefight = CAMPER la carte (attente des respawns / d'un groupe éligible) ----
    private const int CampSliceMs = 15000;
    private long _campMap = -1;
    private long _campBlockedResetAt;
    private void CampWait(long map, string pos, bool g, bool f, CancellationToken ct)
    {
        if (_campMap != map) { _campMap = map; _campBlockedResetAt = Environment.TickCount64; Info("trajet", Loc.Fmt("L.Lua.CampWaiting", pos)); }
        var filter = g ? GatherFilter() : null;
        bool HasGather() => _world.Get(_pid).Interactives.Any(i => i.Harvestable && !_gatherBlocked.Contains(i.ElementId) && IsWantedResource(i, map, filter!));
        if (f && map != _campFightBlockedMap) { _campFightBlockedMap = map; _campFightBlocked.Clear(); }
        bool HasFight() { var st = _world.Get(_pid); return st.SelfCell is int src && SelectMonster(st, src, -1, _campFightBlocked) is not null; }
        // WATCHDOG : camper est un état LÉGITIMEMENT silencieux (sinon l'anti-blocage le signalerait au bout de 180 s).
        _launch.NoteProgress(_pid);
        // Un groupe éligible est DÉJÀ là alors que FightOne vient d'échouer (chemin refusé, combat pas lancé) : on ne se
        // réveille pas dessus tout de suite (sinon re-tentative en rafale toutes les 200 ms) -> courte pause puis nouvel essai.
        if (f && HasFight())
        {
            WaitUntil(() => _launch.IsInFight(_pid) || (_launch.CurrentMap(_pid) ?? 0) != map || (g && HasGather()), 3000, ct);
            _launch.NoteProgress(_pid);
            return;
        }
        bool woke = WaitUntil(() => _launch.IsInFight(_pid) || (_launch.CurrentMap(_pid) ?? 0) != map || (g && HasGather()) || (f && HasFight()), CampSliceMs, ct);
        _launch.NoteProgress(_pid);
        // Re-tente les ressources jugées injoignables.
        if (!woke && (g || f) && Environment.TickCount64 - _campBlockedResetAt > 60000) { if (g) _gatherBlocked.Clear(); if (f) _campFightBlocked.Clear(); _campBlockedResetAt = Environment.TickCount64; }
    }

    // Recolte imperative gatherAll() : enchaine GatherOne jusqu'a ce qu'il n'y ait plus rien (ou un combat).
    private bool GatherAll(CancellationToken ct)
    {
        bool any = false;
        while (!_launch.IsInFight(_pid))
        {
            ThrowIfStopping(ct);
            if (ActivityRecoveryGlobalEnabled && RecoveryBlocksRoute(ownerToken: ct)) break;
            if (_world.Get(_pid).PodsPercent >= (_script is { } script ? ReadInt(script, "MAX_PODS", 95) : 95)) break;
            if (!GatherOne(ct)) break;
            any = true;
        }
        return any;
    }

    // openBags() : ouvre TOUS les sacs de ressources du sac (objets « Sac de … » / « Sac d'… ») : un ObjectUseRequest (iuu)
    // par exemplaire, groupé par gid. Renvoie le nb d'exemplaires ouverts. (useInventoryItem par gid).
    // Lot D: détection = typeId 100 OU heuristique de nom (repli); quiet = pas de ligne « aucun sac »
    // (maintenance automatique des pods) ; ouverture via UseItems (1 ObjectUseMultipleRequest par stack, repli iuu).
    private int OpenResourceBags(CancellationToken ct, bool quiet = false)
    {
        int opened = 0;
        var bags = _launch.Inventory.Items(_pid)
            .Where(it => it.Position == 63 && (_launch.ItemTypeId(it.Gid) == ResourceBagTypeId || IsResourceBagName(_launch.ItemDisplayName(it.Gid))))
            .GroupBy(it => it.Gid)
            .ToList();
        foreach (var g in bags)
        {
            ThrowIfStopping(ct);
            long gid = g.Key;
            int qty = _launch.InventoryCount(_pid, gid);
            if (qty <= 0) continue;
            if (UseItems(gid, qty, ct))
            {
                opened += qty;
                Ok("inventaire", Loc.Fmt("L.Lua.BagsOpened", qty, _launch.ItemDisplayName(gid) ?? gid.ToString()));
                InterruptibleDelay(400, ct);   // laisse le serveur traiter les iuu avant le sac suivant
            }
        }
        if (opened == 0 && !quiet) Info("inventaire", Loc.T("L.Lua.NoBags"));
        return opened;
    }

    private const int ResourceBagTypeId = 100;   // type d'objet « sac de ressources »

    // ObjectUseMultipleRequest désactivé pour la session (process entier : même protocole pour tous les bots) dès qu'un
    // envoi n'a aucun effet -> repli définitif sur la boucle ObjectUseRequest.
    private static volatile bool s_useMultipleOff;

    /// <summary>Utilise `n` exemplaires d'un objet par GID. n&gt;1 : UN ObjectUseMultipleRequest par stack, effet vérifié
    /// (baisse de la quantité en ≤ 2 s) ; sans effet -&gt; repli iuu pour le reste (et pour la session). Thread du script.</summary>
    private bool UseItems(long gid, int n, CancellationToken ct)
    {
        if (n <= 1 || s_useMultipleOff) return _launch.UseInventoryItem(_pid, gid, n);
        var stacks = _launch.Inventory.Items(_pid).Where(i => i.Gid == gid && i.Qty > 0).ToList();
        if (stacks.Count == 0) return _launch.UseInventoryItem(_pid, gid, n);   // absent : chemin d'origine (journalise + false)
        int left = n;
        foreach (var st in stacks)
        {
            if (left <= 0) break;
            ThrowIfStopping(ct);
            int k = System.Math.Min(left, st.Qty);
            int before = _launch.InventoryCount(_pid, gid);
            if (!_launch.UseInventoryItemMultiple(_pid, st.Uid, k, out bool unforgeable))
            {
                // Message absent du mapping = défaut durable -> repli iuu pour la session. Échec PASSAGER (relais, permission,
                // injection) : repli pour cet appel seulement (UseInventoryItem échouera de même s'il n'y a ni relais ni droit).
                if (unforgeable) s_useMultipleOff = true;
                return _launch.UseInventoryItem(_pid, gid, left);
            }
            // Effet = baisse de la quantité. Délai de grâce en 2 temps (2 s + 1,5 s) : sur une connexion lente (4G/proxy) la
            // mise à jour arrive en retard, et un repli trop tôt consommerait les objets EN DOUBLE.
            if (!WaitUntil(() => _launch.InventoryCount(_pid, gid) <= before - k, 2000, ct))
                WaitUntil(() => _launch.InventoryCount(_pid, gid) <= before - k, 1500, ct);
            int consumed = System.Math.Max(0, before - _launch.InventoryCount(_pid, gid));
            if (consumed == 0)
            {
                // Rien consommé après la grâce : le serveur ignore ce paquet -> repli iuu pour le reste (et pour la session).
                s_useMultipleOff = true;
                GivreLauncher.Core.Transport.TransportLog.Line("[inventaire] ObjectUseMultipleRequest sans effet -> repli ObjectUseRequest");
                return _launch.UseInventoryItem(_pid, gid, left);
            }
            // Consommation (au moins partielle) constatée : on décompte ce qui a VRAIMENT disparu, sans jamais renvoyer le reste
            // de ce stack (il peut encore être en cours de traitement côté serveur).
            left -= System.Math.Min(k, consumed);
        }
        return true;   // (n > stock : tout ce qui existait a été utilisé — même résultat que la boucle iuu d'origine)
    }

    /// <summary>Maintenance « pods pleins »: OPEN_BAGS=true -&gt; ouvre
    /// tous les sacs de ressources ; AUTO_DELETE={gid,...} -&gt; détruit ces objets. true si quelque chose a été fait.</summary>
    private bool RunPodsMaintenance(Script s, int podsPct, CancellationToken ct)
    {
        bool announced = false;
        void Announce() { if (!announced) { announced = true; Info("inventaire", Loc.Fmt("L.Lua.PodsMaintenance", podsPct)); } }
        bool acted = false;

        var ob = s.Globals.Get("OPEN_BAGS");
        bool openBags = ob.Type == DataType.Boolean ? ob.Boolean : ob.Type == DataType.Number && ob.Number != 0;
        if (openBags && _launch.Inventory.Items(_pid).Any(it => it.Position == 63 && (_launch.ItemTypeId(it.Gid) == ResourceBagTypeId || IsResourceBagName(_launch.ItemDisplayName(it.Gid)))))
        {
            Announce();
            if (OpenResourceBags(ct, quiet: true) > 0) acted = true;
        }

        var ad = s.Globals.Get("AUTO_DELETE");
        if (ad.Type == DataType.Table)
        {
            var gids = new List<long>();
            foreach (var v in ad.Table.Values)
            {
                double d = v.Type == DataType.Number ? v.Number
                    : v.Type == DataType.String && double.TryParse(v.String, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 0;
                if (d > 0 && !gids.Contains((long)d)) gids.Add((long)d);
            }
            foreach (long gid in gids)
            {
                ThrowIfStopping(ct);
                // SAC uniquement (position 63) : un objet ÉQUIPÉ du même gid n'est jamais détruit. Le moteur détruit stack par
                // stack avec la quantité propre à chacun (équipements droppés = 1 uid par exemplaire).
                if (!_launch.Inventory.Items(_pid).Any(i => i.Gid == gid && i.Position == 63 && i.Qty > 0)) continue;
                Announce();
                if (_launch.DeleteInventoryItem(_pid, gid, 0, out int deleted))   // gate Perm.Gather au point d'action (moteur)
                {
                    acted = true;
                    Ok("inventaire", Loc.Fmt("L.Lua.AutoDeleted", deleted, _launch.ItemDisplayName(gid), gid));
                    InterruptibleDelay(300, ct);
                }
            }
        }
        return acted;
    }

    // Un « sac de ressources » Dofus est nommé « Sac de <ressource> » / « Sac d'<ressource> » -> heuristique de nom (le
    // datacenter n'expose pas de type dédié fiable). Un script peut toujours ouvrir un gid précis via useItem(gid).
    private static bool IsResourceBagName(string? name)
        => name is not null && (name.StartsWith("Sac de ", System.StringComparison.OrdinalIgnoreCase)
                                || name.StartsWith("Sac d'", System.StringComparison.OrdinalIgnoreCase));

    // ELEMENTS_TO_GATHER = liste d'ElementTypeId a recolter (ex {38} = Ble). Vide -> on recolte tout (mode imperatif gatherAll()).
    private HashSet<int> GatherFilter()
    {
        var set = new HashSet<int>();
        var v = _script?.Globals.Get("ELEMENTS_TO_GATHER");
        if (v is not { Type: DataType.Table }) v = _script?.Globals.Get("GATHER");   // nom tiers
        if (v is { Type: DataType.Table })
            foreach (var it in v.Table.Values) if (it.Type == DataType.Number) set.Add((int)it.Number);
        return set;
    }
}
