using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Engine.LuaTooling;
using GivreClone.Protocol;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// ===================== Interpréteur de trajet déclaratif (RunRoute, ChoosePath) + garde-fous de voyage =====================
public sealed partial class GivreLuaHost
{
    // ============ INTERPRETEUR DE TRAJET DECLARATIF ============
    // move()/bank() renvoient une LISTE d'etapes : { map="x,y" ou id, path=<sortie>, gather=bool, fight=bool, npcBank=... }.
    // A chaque tick : on trouve l'etape de la CARTE COURANTE, on recolte (filtre ELEMENTS_TO_GATHER) puis combat, puis on suit path.
    // `path`/`changeMap` accepte DESORMAIS, en plus d'une chaine unique ("right" / "x,y" / id) :
    //   - une LISTE de sorties : path = { "left", "right", "bottom" }  -> une est choisie au hasard,
    //   - une liste PONDEREE :   path = { { direction="left", weight=50 }, { direction="right", weight=30 } },
    // avec ANTI-RETOUR : la direction opposee a la derniere sortie prise est evitee tant qu'une AUTRE sortie existe
    // (evite de revenir aussitot sur la carte precedente) -> reseau de cartes plutot que boucle lineaire.
    private int _lastNoRouteMap = int.MinValue;
    private int _offGraphTicks;                 // ticks CONSÉCUTIFS passés sur une carte hors world-graph (escalade vers un arrêt clair)
    private const int MaxOffGraphTicks = 15;    // ~20-25 s à ~1,4 s/tick : encaisse un glitch post-combat mais borne l'idle si zone non navigable
    private const int PostFightGraceMs = 4000;  // après un combat, la carte courante reste ~0,5 s l'INSTANCE de combat (hors graphe) : on l'attend en SILENCE
    private MapDir? _lastExitDir;   // derniere direction de sortie prise (anti-retour des sorties multiples)

    // Cap anti-boucle du voyage : si GoToMapId échoue N fois de suite vers la MÊME carte (sortie inaccessible / obstacle),
    // on ARRÊTE proprement le script au lieu de re-tenter indéfiniment. Reset dès qu'un voyage aboutit ou change de cible.
    private long _travelStuckMap; private int _travelStuckCount;
    private const int MaxTravelFails = 3;
    // Borne ANTI-ERRANCE du rejoin « hors trajet » : la carte de route la plus proche est RECALCULÉE à chaque tick depuis la
    // position courante -> après un téléport (repli coordonnée) la cible CHANGE, et TravelCapped (qui compte PAR cible) ne
    // borne plus rien. On compte donc les échecs CONSÉCUTIFS hors trajet toutes cibles confondues ; reset dès qu'une étape
    // re-matche (retour sur la route) ou au (re)démarrage du script.
    private int _offRouteFails;
    private const int MaxOffRouteFails = 5;

    // `door` d'une étape = ID de cellule d'un interactif. "a|b|c" = plusieurs cellules -> une au hasard.
    private static readonly System.Random _doorRng = new();
    private static int ChooseDoorCell(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return -1;
        var parts = raw.Split('|', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return -1;
        string pick = parts.Length == 1 ? parts[0] : parts[_doorRng.Next(parts.Length)];
        return int.TryParse(pick, out int c) ? c : -1;
    }

    // true = voyage abouti. false = échec ; si le cap est atteint, le script est arrêté (Stop) — l'appelant ne doit plus rien tenter.
    private bool TravelCapped(long map, CancellationToken ct)
    {
        if (GoToMapId(map, ct, probe: true)) { _travelStuckMap = 0; _travelStuckCount = 0; return true; }   // plafonné -> sondage zaap autorisé
        if (ct.IsCancellationRequested) return false;   // arrêt demandé (Stop / relance) : pas un blocage -> ni compté ni StuckStop
        if (LinkSuspended) return false;                // lien de jeu suspendu (MITM) : injections refusées -> pas un blocage, non compté
        // Échec dû à un COMBAT (aggro en route / fin de combat toute fraîche) : pas un blocage -> NON compté. Sinon 3 aggros
        // sur un trajet banque/route = arrêt + planning en pause pour toute la plage (vérif adverse).
        if (_launch.IsInFight(_pid) || _launch.MsSinceFightEnd(_pid) < 5000) return false;
        if (map != _travelStuckMap) { _travelStuckMap = map; _travelStuckCount = 0; }
        if (++_travelStuckCount >= MaxTravelFails)
        {
            Err("trajet", Loc.Fmt("L.Lua.TravelStuck", map, _travelStuckCount));
            _travelStuckMap = 0; _travelStuckCount = 0;
            StuckStop();
        }
        return false;
    }

    // Carte de route à rejoindre quand on est HORS trajet, CONSCIENTE DE L'ACCESSIBILITÉ. Avant : la plus proche en
    // coordonnées, même ISOLÉE (de l'autre côté de l'eau / trou de graphe) -> le bot s'y acharnait jusqu'à l'arrêt alors que
    // d'autres cartes du trajet étaient joignables (vérif adverse, cas signalé « village côtier »). Ordre :
    //  (1) la plus proche JOIGNABLE À PIED d'ici (quêtes faites incluses) — cas normal (sortie de banque) : même choix qu'avant ;
    //  (2) sinon la plus proche joignable À PIED DEPUIS UN ZAAP CONNU (TravelCapped téléportera puis marchera) ;
    //  (3) sinon la plus proche en coordonnées (le sondage de TravelCapped ouvre un zaap -> liste apprise -> le tick suivant
    //      saura choisir en (2)). Coût borné : seules les N plus proches sont évaluées (BFS).
    private const int RejoinWalkCandidates = 10;
    private const int RejoinZaapCandidates = 5;
    private long PickRejoinTarget(Table route, long mapId, int cx, int cy)
    {
        int? layer = _launch.WorldMapOf(mapId);
        var cands = new List<(long id, int d)>();
        var seen = new HashSet<long>();
        foreach (var v in route.Values)
        {
            if (v.Type != DataType.Table) continue;
            if (!TryParseCoord(v.Table.Get("map").CastToString() ?? "", out int mx, out int my)) continue;
            int d = System.Math.Abs(mx - cx) + System.Math.Abs(my - cy);
            if (d > 0 && _launch.MapIdAt(mx, my, layer) is long id && id != mapId && seen.Add(id)) cands.Add((id, d));   // meme calque (Incarnam != Astrub)
        }
        if (cands.Count == 0) return 0;
        cands.Sort((a, b) => a.d.CompareTo(b.d));
        foreach (var (id, d) in cands.Take(RejoinWalkCandidates))
        {
            // Carte ADJACENTE du même calque connu : joignable même si le graphe a un trou (court-circuit « 1 carte » de GoToMapId).
            if (d == 1 && layer is int l && _launch.WorldMapOf(id) == l) return id;
            if (_launch.WorldPath((int)mapId, (int)id, null, _pid).Count > 0) return id;
        }
        if (_launch.KnownZaapMaps(_pid).Count > 0)
            foreach (var (id, _) in cands.Take(RejoinZaapCandidates))
                if (NearestZaapFor(id, allowGated: true) is not null) return id;
        return cands[0].id;
    }

    /// <summary>Le script s'est ARRÊTÉ LUI-MÊME faute de pouvoir progresser (voyage bloqué, route injoignable, carte hors
    /// graphe). L'hôte (BotsViewModel) met alors le PLANNING en pause pour ce perso (comme un arrêt manuel) : sans ça le
    /// scheduler le relançait ~1,5 s plus tard et chaque plafond (téléports payants inclus) se rejouait en boucle.</summary>
    public event Action? StuckStopped;
    private void StuckStop()
    {
        // Lien de jeu SUSPENDU (MITM : session perdue, ou client sorti de la session et attendu en jeu) : l'échec vient de la
        // coupure, pas d'un blocage -> ni arrêt « stuck » ni pause du planning. Courte tempo (pas de rafale de la même erreur
        // à chaque tick) ; si la coupure se confirme, le script est arrêté « disconnected » (GameLinkLosing).
        if (LinkSuspended) { Volatile.Read(ref _cts)?.Token.WaitHandle.WaitOne(1500); return; }
        // Run DÉJÀ en cours d'arrêt (« Reconnecter », suspension socket, arrêt utilisateur…) : l'échec vient de cet arrêt, pas
        // d'un blocage -> jamais de StuckStopped (il mettrait le planning en pause jusqu'à la fin de la plage).
        var c = Volatile.Read(ref _cts);
        if (c is null || c.IsCancellationRequested) return;
        Stop("stuck"); StuckStopped?.Invoke();
    }

    // Lien de jeu MITM suspendu (coupé, ou attente de retour en jeu) : les échecs d'action ne comptent pas comme un blocage.
    private bool LinkSuspended => _launch.IsLinkSuspended(_pid);

    private const int MaxCustomRouteDepth = 3;   // sous-trajets renvoyés par `custom` imbriqués : profondeur max

    // Sous-trajet renvoyé par un `custom`: la table elle-même si au moins une de ses valeurs est une étape
    // (table avec `map`) ; une table-étape isolée ({map=…}) est enveloppée dans une liste ; sinon null.
    private static Table? AsSubRoute(Table t, Script s)
    {
        if (t.Values.Any(v => v.Type == DataType.Table && !v.Table.Get("map").IsNil())) return t;
        if (!t.Get("map").IsNil())
        {
            var wrap = new Table(s);
            wrap.Set(1, DynValue.NewTable(t));
            return wrap;
        }
        return null;
    }

    private void RunRoute(Table route, CancellationToken ct, int depth = 0)
    {
        ThrowIfStopping(ct);
        // Explicit opt-in to RECONCILE, not to RESEND. A global read-only probe
        // cannot fall through to legacy off-route travel or teleport retries.
        if (ActivityRecoveryReconcileGlobalEnabled)
        {
            InspectActivityRecoveryFromLua(ct);
            return;
        }
        // Une action recovery deja engagee peut avoir ete acceptee par le serveur meme si
        // son resultat TCS est timeout/refus apparent. Sans ACK sequence-correlable,
        // aucune etape suivante (meme non opt-in) ne peut produire un second envoi.
        long mapId = _launch.CurrentMap(_pid) ?? 0;
        // The per-step opt-in must be admitted BEFORE the legacy off-graph /
        // havenbag / rejoin code, not after it. Otherwise a script restarted on
        // an unmatched map can emit TravelCapped or ToggleOutOfHavenBag before
        // ever reaching its opted-in step (and before checking the durable marker).
        bool routeHasRecovery = route.Values.Any(v => v.Type == DataType.Table &&
            (v.Table.Get("activityRecovery").CastToBool() ||
             v.Table.Get("activityRecoveryReconcile").CastToBool()));
        // A route entered directly from a Lua callback or a nested custom
        // subroute must honor the archived marker even after Stop/Start and
        // even when its new Lua script no longer opts in. No old attempt may
        // be replayed by changing the entrypoint to a legacy route.
        // Explicit read-only reconciliation remains available below.
        if (!routeHasRecovery && RecoveryBlocksRoute(checkDurable: true,
                ownerToken: ct)) return;
        // L'option globale concerne aussi les chemins hors-graphe : leur reparcours
        // historique peut emettre plusieurs transports. N'admettre ici que les etapes
        // DEJA presentes et observees sur la carte, sans rejoin/zaap automatique.
        if (ActivityRecoveryGlobalEnabled || routeHasRecovery)
        {
            var current = _world.Get(_pid);
            if (mapId <= 0 || current.MapId != mapId ||
                !current.SelfReadyFor(mapId) || !current.CellsReadyFor(mapId))
            {
                NoteActivityRecoveryRunBlockV18(
                    ActivityRecoveryTransitionReasonV18.MapUnconfirmed, ct,
                    "route Lua: carte, cellules ou acteur non confirmes");
                RecoveryBlocksRoute(checkDurable: true, ownerToken: ct);
                RecoveryWarning("carte/position non confirmees ; aucun envoi avant admission durable");
                return;
            }
            var exact = MatchStepHere(route, mapId, current.MapX, current.MapY);
            if (exact is null)
            {
                NoteActivityRecoveryRunBlockV18(
                    ActivityRecoveryTransitionReasonV18.RouteUnmatched, ct,
                    "route Lua: aucune etape attestee sur la carte actuelle");
                RecoveryBlocksRoute(checkDurable: true, ownerToken: ct);
                RecoveryWarning("hors d'une etape connue ; rejoin automatique interdit en mode recovery");
                StopRecoveryPlanningOnce(ct);
                return;
            }
            // An explicitly read-only inspection must still be possible with a
            // pending/corrupt marker. It never reaches the gameplay sender.
            if (exact.Get("activityRecoveryReconcile").CastToBool())
            {
                InspectActivityRecoveryFromLua(ct);
                return;
            }
            if (RecoveryBlocksRoute(checkDurable: true, ownerToken: ct)) return;
            if (ActivityRecoveryGlobalEnabled || exact.Get("activityRecovery").CastToBool())
                RunRouteActivityRecovery(exact, ct);
            else
            {
                NoteActivityRecoveryRunBlockV18(
                    ActivityRecoveryTransitionReasonV18.RouteUnmatched, ct,
                    "route Lua mixte: etape sans admission recovery");
                RecoveryWarning("etape non opt-in dans une route recovery mixte ; aucune action legacy sans admission");
                StopRecoveryPlanningOnce(ct);
            }
            return;
        }
        if (mapId == 0) return;
        Table? step = null;
        // HAVRE-SAC appris (étape `havenbag`) : il est HORS world-graph -> tranché AVANT la garde hors-graphe (qui menait
        // sinon à StuckStop). On y exécute l'étape map='havenbag' (ou son id) — ex. path='zaap(<id>)' ; sans étape, on RESSORT.
        if (InHavenBag(mapId))
        {
            step = HavenBagStepOf(route, mapId);
            if (step is null)
            {
                Info("havre-sac", Loc.Fmt("L.Lua.HavenBagNoStep", _havenBagEntryMap));
                if (!ToggleOutOfHavenBag(mapId, ct))
                {
                    if (!LinkSuspended && ++_offGraphTicks >= MaxOffGraphTicks)   // lien suspendu : sortie refusée, pas un blocage
                    {
                        Err("trajet", Loc.Fmt("L.Lua.TravelOffGraphStuck", mapId, _offGraphTicks));
                        _offGraphTicks = 0; StuckStop(); return;
                    }
                    InterruptibleDelay(1500, ct);
                }
                return;
            }
        }
        // Carte HORS world-graph, tranchée AVANT WaitSelfReady. C'est CRUCIAL juste après un combat : la carte courante reste
        // un instant l'INSTANCE de combat (id ex 153883648, coord (0,0), absente du graphe). Or WaitSelfReady attend jusqu'à
        // 8 s le self-cell d'une carte qui ne l'émettra JAMAIS (instance abandonnée) -> ~15 s de gel par tick avant de reprendre.
        // Ici : pas d'attente self, journal (dédup), petite tempo (la carte de jeu de rôle revient en ~1 s) + escalade bornée
        // si on reste vraiment coincé (compteur remis à 0 dès qu'on repasse sur une carte connue, juste en dessous).
        // Lot J : intérieur de maison hors world-graph avec une étape à son id exact -> on la joue (IsHouseStep).
        if (step is null && !IsHouseStep(route, mapId) && !_launch.WorldGraphKnows((int)mapId))
        {
            // FENÊTRE POST-COMBAT : juste après un combat, _lastMap reste ~0,5 s l'INSTANCE de combat (id 8821xxxx, coord
            // 0,0) car map_<instance> existe dans le bundle -> la garde jru ne l'écarte pas. On l'attend en SILENCE : sinon,
            // l'id d'instance CHANGEANT à chaque combat, la dédup par id ne tait rien -> une ligne "hors du world-graph" par
            // combat (bruit pur, le farm progresse). L'escalade bornée reste active pour un VRAI blocage (DC / zone non extraite).
            bool postFight = _launch.MsSinceFightEnd(_pid) < PostFightGraceMs;
            if (!postFight && _lastNoRouteMap != (int)mapId)
            {
                var sog = _world.Get(_pid);
                string posog = sog.MapX is int ox && sog.MapY is int oy ? $"{ox},{oy}" : "?";
                _lastNoRouteMap = (int)mapId; Warn("trajet", Loc.Fmt("L.Lua.TravelOffGraph", mapId, posog));
            }
            // Havre-sac non appris (script lancé DEPUIS le havre-sac) : on sonde une sortie avant d'escalader vers l'arrêt.
            // Réussi = on est ressorti sur une carte du graphe -> on reprend le trajet au prochain tick.
            if (!postFight && TryHavenBagExitProbe(mapId, ct)) { _offGraphTicks = 0; _lastNoRouteMap = int.MinValue; return; }
            if (!LinkSuspended && ++_offGraphTicks >= MaxOffGraphTicks)   // lien suspendu : la carte ne changera pas, pas un blocage
            {
                Err("trajet", Loc.Fmt("L.Lua.TravelOffGraphStuck", mapId, _offGraphTicks));
                _offGraphTicks = 0; _lastNoRouteMap = int.MinValue; StuckStop(); return;
            }
            InterruptibleDelay(postFight ? 300 : 500, ct);   // court : la vraie carte revient vite après le combat -> reprise quasi immédiate
            return;
        }
        _offGraphTicks = 0; _offGraphExitTries = 0;   // carte connue -> réarme l'escalade hors-graphe et les sondes de sortie
        // Attendre que la self-cell (ET les interactifs/acteurs) soient CONFIRMES pour la carte courante via le jss, au lieu
        // d'un delai fixe de 700 ms : le jss arrive ~1 s apres le jru, donc 700 ms lisait la cellule d'exit PERIMEE.
        WaitSelfReady(ct);

        var st = _world.Get(_pid);
        int? px = st.MapX, py = st.MapY;
        string pos = px is int x && py is int y ? $"{x},{y}" : "?";

        // Match en DEUX passes, l'ID de carte AVANT la coordonnee. Un interieur atteint par une PORTE partage souvent la
        // MEME coordonnee monde que la carte exterieure (ex : l'exterieur 205260292 et les interieurs 207619084 / 207620108
        // sont tous en 25,-22). Avec un seul passage dans l'ordre du tableau, l'etape coord "25,-22" (ecrite avant l'etape id)
        // re-matchait sur l'interieur -> on rejouait "voyage vers 207619084" alors qu'on y etait deja = boucle sur place.
        // L'id numerique est le plus SPECIFIQUE : une etape { map = "<id>" } doit toujours gagner sur une etape coord au meme (x,y).
        // La coord reste en repli (passe 2) et garde la normalisation des tirets unicode/espaces (copier-coller).
        // Compat un bot tiers: PLUSIEURS étapes pour la même carte sont FUSIONNÉES clé par clé, la DERNIÈRE qui définit une clé
        // gagne (parité GetInfoMap : deux `path` différents -> celui de l'étape écrite en dernier).
        // Jamais de mélange id / coordonnée : dès qu'une étape vise l'id exact, les étapes en coordonnée sont ignorées (un
        // intérieur partage la coordonnée de l'extérieur). Une seule étape -> elle-même (comportement inchangé).
        step ??= MatchStepHere(route, mapId, px, py);   // passe 1 : id exact ; passe 2 : coordonnée (GivreLuaHost.SnowScript.cs)

        if (step is null)
        {
            // La carte courante n'est PAS un nœud du world-graph (« source connue : False » : coord (0,0) sentinelle,
            // instance de combat, ou zone non extraite). AUCUN voyage world-graph ne peut PARTIR d'ici : ni « rejoindre la
            // carte de route la plus proche » (le calcul se ferait depuis (0,0) -> carte du monde en (0,0), aberrant), ni le
            // cap d'échecs de TravelCapped — qui ARRÊTAIT tout le script (« trajet bloqué … arrêt du script » après 3 combats).
            // On journalise UNE fois (dédup par _lastNoRouteMap) puis on temporise : un décrochage TRANSITOIRE (glitch
            // post-combat) se résorbe en 1-2 ticks et NE DOIT PAS stopper. Mais un perso VRAIMENT coincé sur une carte
            // hors-graphe ne doit pas non plus tourner idle à l'infini (pire qu'un arrêt clair pour un run sans surveillance) :
            // après MaxOffGraphTicks ticks CONSÉCUTIFS hors-graphe, on arrête PROPREMENT avec un message distinct. Le
            // compteur retombe à zéro dès qu'une étape re-matche (retour sur carte connue, cf. _offGraphTicks = 0 plus bas).
            // (Le cas « carte hors world-graph » est déjà tranché en TÊTE de RunRoute, avant WaitSelfReady : ici mapId est un nœud connu.)
            // Aucune etape pour la carte courante. Si le trajet vise une CARTE PRECISE (id numerique, ex banque),
            // on VOYAGE jusqu'a elle (BFS multi-cartes) plutot que d'abandonner. Les etapes de farm sont en coord "x,y"
            // (long.TryParse echoue) -> non affectees ; seule une destination par id de carte declenche le voyage.
            foreach (var v in route.Values)
            {
                if (v.Type != DataType.Table) continue;
                string m = (v.Table.Get("map").CastToString() ?? "").Trim();
                if (long.TryParse(m, out long destId) && destId > 0 && destId != mapId)
                {
                    if (!TravelCapped(destId, ct)) InterruptibleDelay(1500, ct);   // arrive -> le tick suivant matchera l'etape (npcBank...)
                    return;
                }
            }
            // Trajet en COORDONNEES (farm) et perso HORS trajet (ex : sorti de banque) : on rejoint la carte de route la
            // PLUS PROCHE via le MEME voyage multi-cartes (BFS), puis la recolte reprend au tick suivant.
            if (px is int cx && py is int cy)
            {
                long bestMap = PickRejoinTarget(route, mapId, cx, cy);
                if (bestMap != 0)
                {
                    Info("trajet", Loc.Fmt("L.Lua.TravelOffRoute", cx, cy, bestMap));
                    if (!TravelCapped(bestMap, ct))
                    {
                        // Un échec dû à un COMBAT (aggro en route) ou au lien de jeu suspendu n'est pas un blocage : non compté.
                        if (!ct.IsCancellationRequested && !_launch.IsInFight(_pid) && !LinkSuspended && ++_offRouteFails >= MaxOffRouteFails)
                        {
                            Err("trajet", Loc.Fmt("L.Lua.TravelOffRouteStuck", _offRouteFails));
                            _offRouteFails = 0;
                            StuckStop();
                        }
                        InterruptibleDelay(1500, ct);   // lève ScriptStopException si le script vient d'être arrêté (même patron que TravelCapped)
                    }
                    return;
                }
            }
            if (_lastNoRouteMap != (int)mapId)   // une seule fois par carte -> pas de spam ; on DUMPE le trajet pour comparer
            {
                _lastNoRouteMap = (int)mapId;
                var coords = route.Values.Where(v => v.Type == DataType.Table)
                                         .Select(v => v.Table.Get("map").CastToString() ?? "?").Take(40);
                Warn("trajet", Loc.Fmt("L.Lua.TravelNoStep", pos, mapId, string.Join(" ", coords)));
            }
            return;
        }
        _lastNoRouteMap = int.MinValue;
        _offGraphTicks = 0;   // étape re-matchée -> on est de nouveau sur une carte connue : réarme l'escalade hors-graphe
        _offRouteFails = 0;   // de retour sur la route -> réarme la borne anti-errance du rejoin hors trajet
        _coordFallbackVisited.Clear();   // épisode hors-trajet terminé -> les zaaps de repli redeviennent candidats
        _lastZaapDecisionLog = 0;        // nouvel épisode -> le diagnostic de décision zaap sera de nouveau journalisé
        _lastLongWalkLog = 0;             // idem pour le diagnostic de longue marche sans zaap

        // Mode explicite de reprise conservatrice. Une seule action au maximum avec
        // preuve de carte/cellule/stock puis suspension faute de sequence ACK.
        // Sans option Lua, le parcours historique reste strictement inchange.
        if (step.Get("activityRecoveryReconcile").CastToBool())
        {
            InspectActivityRecoveryFromLua(ct);
            return;
        }
        if (step.Get("activityRecovery").CastToBool() || ActivityRecoveryGlobalEnabled)
        {
            RunRouteActivityRecovery(step, ct);
            return;
        }

        // Lot F: clé d'étape `regeneration`: `= n` -> régénère si PV% < n; `= true` -> seuil MIN_LIFE_PERCENT
        // (sinon 100). Cible = REGEN_UP_TO_PERCENT, jamais sous le seuil. Absente/false -> rien ne change.
        DynValue regen = step.Get("regeneration");
        if (regen.Type is DataType.Number or DataType.Boolean && !(regen.Type == DataType.Boolean && !regen.Boolean))
        {
            int trig = regen.Type == DataType.Number ? (int)regen.Number : (ReadInt(_script!, "MIN_LIFE_PERCENT", 0) is int m && m > 0 ? m : 100);
            if (trig > 0 && _launch.SelfLifePercent(_pid) is int lp && lp < trig)
                Regenerate(Math.Clamp(ReadInt(_script!, "REGEN_UP_TO_PERCENT", 100), Math.Min(trig, 100), 100), -1, ct);
            if (_launch.IsInFight(_pid)) return;
        }

        // Recolte UNE ressource par tick: on RE-ENTRE tant qu'il en reste avant de changer de
        // carte. Sinon on file a la map suivante alors que le perso est encore en train de recolter -> deplacements refuses.
        // Lot E: forcegather / forcefight = le bot RESTE sur la carte de l'étape (récolte puis attente des respawns /
        // attente d'un groupe éligible) ; path/changeMap/door/custom ne sont JAMAIS exécutés pour cette étape. Sorties : pods
        // pleins (mode banque de RunLoop), combat, arrêt du script. Variantes forceGather/forceFight acceptées.
        bool forceGather = step.Get("forcegather").CastToBool() || step.Get("forceGather").CastToBool();
        bool forceFight = step.Get("forcefight").CastToBool() || step.Get("forceFight").CastToBool();
        if (step.Get("gather").CastToBool() || forceGather)
        {
            if (GatherOne(ct)) return;               // a tente une ressource -> le tick suivant prendra la suivante
            if (_launch.IsInFight(_pid)) return;     // aggro pendant la recolte
        }
        if (step.Get("fight").CastToBool() || forceFight)
        {
            if (FightOne(ct, -1, forceFight)) return;
            if (_launch.IsInFight(_pid)) return;
        }
        bool campFight = forceFight && LicenseService.HasPermission(LicenseService.Perm.Fight);   // sans droit : pas de camp inutile -> trajet normal
        if (forceGather || campFight) { CampWait(mapId, pos, forceGather, campFight, ct); return; }
        _campMap = -1;

        if (step.Get("npcBank").CastToBool())
        {
            // Le perso est sur la carte de la banque (RunRoute n'a matche cette etape que parce que mapId courant == map
            // de l'etape). On depose TOUT le sac via la sequence PNJ banquier (iov/ios/ioy/iwb/kez/kla).
            long bankMap = _launch.CurrentMap(_pid) ?? 0;
            Info("banque", Loc.T("L.Lua.BankDepositing"));
            bool ok = _launch.BankDepositAllAsync((uint)_pid, bankMap, ct).GetAwaiter().GetResult();
            if (ok) Ok("banque", Loc.T("L.Lua.BankDeposited")); else Warn("banque", Loc.T("L.Lua.BankDepositFailed"));
            InterruptibleDelay(1000, ct);
            return;
        }
        // Lot J : `lockedStorage = "cell|code[|skillId]"` (coffre à code -> dépôt mode onglet Banque -> fermeture) et
        // `lockedHouse = "cell|code[|owner]"` / `"owner|code"` (entrée dans une maison à code). Absentes -> rien ne change.
        if (RunLockRouteKeys(step, ct)) return;
        // `cell` : positionne le perso sur une cellule précise de la carte (= goToCellId), en préfixe de l'étape.
        // Un bot tiers n'a pas ce champ déclaratif (il passe par map.moveToCell dans un custom) mais on l'offre en confort.
        if (step.Get("cell").Type == DataType.Number)
        {
            int cell = (int)step.Get("cell").Number;
            if (cell >= 0)
            {
                Info("trajet", Loc.Fmt("L.Lua.TravelCellMove", cell));
                // Option par etape : { cell = 320, resumableRouteDiagnostic = true }.
                // La confirmation serveur ne fournit pas de sequence correlative au LuaHost ;
                // seule une evaluation bornee + la primitive de navigation existante sont autorisees.
                bool diagnostic = step.Get("resumableRouteDiagnostic").CastToBool();
                if (diagnostic && !RunRouteCellWithRecoveryDiagnostic(cell, ct)) return;
                if (!diagnostic) GoToCell(cell, ct);
                if (_launch.IsInFight(_pid)) return;
            }
        }
        // `custom`: bloc Lua arbitraire pour CETTE carte. Elle peut positionner,
        // parler à un PNJ, franchir une porte, etc. via les verbes impératifs. Si elle change de carte / lance un combat,
        // on rend la main (le tick suivant re-matche la nouvelle étape) ; sinon on poursuit vers door/path.
        // Si elle RENVOIE une table d'étapes, ce sous-trajet est exécuté comme move (étape de la carte courante,
        // sinon voyage/rejoin) ; profondeur bornée. Toute autre valeur de retour -> comportement historique (door/path).
        if (step.Get("custom").Type == DataType.Function)
        {
            long before = _launch.CurrentMap(_pid) ?? mapId;
            Info("trajet", Loc.T("L.Lua.TravelCustomRun"));
            DynValue ret = DynValue.Nil;
            try { ret = CallLua(_script!, step.Get("custom"), ct); }
            catch (System.OperationCanceledException) { throw; }
            catch (System.Exception ex) { Warn("trajet", Loc.Fmt("L.Lua.TravelCustomError", ex.Message)); }
            ThrowIfStopping(ct);
            if ((_launch.CurrentMap(_pid) ?? before) != before || _launch.IsInFight(_pid)) return;
            if (ret is not null && ret.Type == DataType.Tuple && ret.Tuple.Length > 0) ret = ret.Tuple[0];
            if (ret is not null && ret.Type == DataType.Table && ret.Table.OwnerScript is Script owner && AsSubRoute(ret.Table, owner) is Table sub)
            {
                if (depth >= MaxCustomRouteDepth) { Warn("trajet", Loc.Fmt("L.Lua.TravelCustomDepth", MaxCustomRouteDepth)); return; }
                Info("trajet", Loc.Fmt("L.Lua.TravelCustomSubRoute", sub.Length));
                RunRoute(sub, ct, depth + 1);
                return;
            }
        }
        // Lot K: `lockedCustom` = `custom` sous VERROU D'ÉQUIPE -> RunLockedCustom.
        if (step.Get("lockedCustom").Type == DataType.Function && RunLockedCustom(step, mapId, ct)) return;
        // `door`: la valeur est l'ID DE CELLULE d'une porte/interactif. On
        // utilise l'interactif à cette cellule pour franchir vers la carte suivante, puis on rend la main.
        if (step.Get("door").Type is DataType.String or DataType.Number)
        {
            int doorCell = ChooseDoorCell(step.Get("door").CastToString() ?? "");
            if (doorCell >= 0) { Info("trajet", Loc.Fmt("L.Lua.TravelDoorCross", doorCell)); UseInteractive(doorCell, ct, verbose: true); return; }
        }
        // `exitCell`: quitter la carte par CETTE cellule de bord (un bot tiers MovePlayerAndChangeMap(cell,-1), un bot tiers "dir(cell)").
        // `cell=` garde son sens actuel (positionnement seul) -> scripts existants inchangés.
        DynValue ec = step.Get("exitCell");
        if (ec.Type is DataType.Number or DataType.String && int.TryParse(ec.CastToString()?.Trim(), out int exitCell) && exitCell >= 0)
        {
            if (_launch.ExitDirOfCell(mapId, exitCell) is MapDir xd)
            {
                string tok = $"{DirName(xd)}({exitCell})";
                Info("trajet", Loc.Fmt("L.Lua.TravelExitCell", exitCell, DirName(xd)));
                ChangeMap(tok, ct);   // même code que 'dir(cell)' (re-tentatives + équipe + followers)
            }
            else { Warn("trajet", Loc.Fmt("L.Lua.TravelExitCellInvalid", exitCell)); InterruptibleDelay(800, ct); }
            return;
        }

        // `changeMap` prime sur `path` s'il est present (chaine non vide OU liste) ; sinon on prend `path`.
        DynValue rawPath = step.Get("changeMap");
        // `changeMap` peut être une CHAÎNE ("right"/"x,y"/"<id>"), une TABLE (sorties multiples/pondérées) OU un NOMBRE
        // (id de carte Lua non quoté : `changeMap = 88085761`). Sans le cas Number, un changeMap numérique était traité
        // comme « absent » -> on retombait sur `path` (souvent nil) -> étape terminale -> le bot restait sur place et
        // re-récoltait à vide en boucle. ChoosePath gère déjà le Number ; il ne manquait que ce garde. cf path numérique.
        bool hasChange = rawPath.Type is DataType.Table or DataType.Number
                      || (rawPath.Type == DataType.String && rawPath.String!.Length > 0);
        if (!hasChange) rawPath = step.Get("path");
        if (rawPath.IsNil()) rawPath = step.Get("paths");   // compat tiers: `paths` (chef + équipe) = `path`; le suivi d'équipe fait le reste
        string path = StripTravelPrefix(ChoosePath(rawPath, pos));   // chaine unique, liste de directions, ou liste ponderee (+ anti-retour)
        if (string.IsNullOrEmpty(path)) return;                              // etape terminale sans chemin
        // zaap(id) / zaapi(id) / zaapcarte(id) / havenbag: téléport depuis cette carte, repli voyage.
        if (LuaCompat.TryParseTeleportKeyword(path, out var kw, out long kwMap)) { RunTeleportKeyword(kw, kwMap, ct); return; }
        if (LuaCompat.TryParseDirToken(path, out _, out _)) { ChangeMap(path, ct); return; }   // direction cardinale, ou "dir(cell)" (cellule forcée)
        // Coord ADJACENTE -> direction cardinale, MAIS seulement si le world-graph a une sortie RÉELLE dans ce sens.
        // Certaines cartes n'ont PAS de bord dans la direction attendue (falaise/mur — ex -27,9 → -27,8 : aucune sortie
        // NORD dans le graphe). Sans ce garde-fou, ChangeMap échouait en boucle (« pas de sortie top » à l'infini). Si
        // aucune arête cardinale, on RETOMBE sur le voyage world-graph (BFS) vers la carte cible (résolu en waypoint plus
        // bas), qui contourne (ex -27,9 → est → nord → -27,8, 3 sauts).
        if (TryDirToCoord(pos, path, out string dir)
            && !(TryParseDir(dir, out var md) && _launch.WorldTransitionInDirection(mapId, md) is null))
        { ChangeMap(dir, ct); return; }
        // NON adjacent -> au lieu de staller, on VOYAGE (BFS world-graph + raccourci zaap, comme travelTo / une etape mapId).
        // ADDITIF : ne modifie QUE l'ancien cas mort (qui journalisait "non supporte"). Un `path` peut etre un mapId
        // numerique ("va a cette carte") OU une coord LOINTAINE ("va au spot (x,y)") -> mode waypoints du Script Creator.
        long wpMap = 0;
        if (long.TryParse(path.Trim(), out long wpId) && wpId > 0) wpMap = wpId;                                         // path = mapId
        else if (TryParseCoord(path, out int tx, out int ty) && _launch.MapIdAt(tx, ty, _launch.CurrentWorldMap(_pid)) is long tid && tid != 0) wpMap = tid;  // path = coord lointaine, meme calque
        if (wpMap != 0)
        {
            Info("trajet", Loc.Fmt("L.Lua.TravelWaypoint", path, wpMap));
            if (!TravelCapped(wpMap, ct)) InterruptibleDelay(1500, ct);      // arrive -> le tick suivant matche l'etape de la carte cible
            return;
        }
        Warn("trajet", Loc.Fmt("L.Lua.TravelNonAdjacent", path));            // ni direction, ni coord/carte resoluble -> ignore
        InterruptibleDelay(800, ct);
    }

    // Tolère les préfixes DSL d'autres bots / de scripts générés par IA sur un `path`/`changeMap` : "goto:<id|x,y>",
    // "usezaap:<mapId>", "zaap:<mapId>", "travel:<id>", "map:<id>". Pour GivreClone, aller à une carte (id OU coord) EST déjà
    // un voyage qui prend le zaap tout seul si utile -> on retire juste le préfixe et on résout la valeur normalement
    // (direction / coord "x,y" / id de carte). Sans ':' (direction, coord, id nu) -> chaîne inchangée.
    private static string StripTravelPrefix(string p)
    {
        if (string.IsNullOrEmpty(p)) return p;
        int c = p.IndexOf(':');
        if (c > 0 && c < p.Length - 1)
        {
            string head = p.Substring(0, c).Trim().ToLowerInvariant();
            if (head is "goto" or "go" or "usezaap" or "zaap" or "travel" or "map" or "changemap")
                return p.Substring(c + 1).Trim();
        }
        return p;
    }

    // Choisit UNE sortie parmi un `path`/`changeMap` qui peut etre : une CHAINE unique ("right"/"x,y"/id, inchange),
    // une LISTE de directions { "left", "right" }, ou une liste PONDEREE { { direction="left", weight=50 }, ... }.
    // ANTI-RETOUR : si plusieurs candidats et que l'un est la direction OPPOSEE a la derniere sortie prise, on
    // l'ecarte TANT QU'IL RESTE une autre sortie (evite le va-et-vient entre deux cartes). Puis tirage PONDERE.
    // La resolution finale (direction cardinale vs coord vs mapId) reste faite par l'appelant sur la chaine choisie.
    private string ChoosePath(DynValue raw, string pos)
    {
        if (raw is null) return "";
        if (raw.Type == DataType.String) return LuaCompat.PickAlternative(raw.String ?? "", Random.Shared);   // "a|b" -> une au hasard
        // `path = 159489` NON quoté = NOMBRE Lua (id de carte). Le Script Creator génère des ids numériques ; sans ce
        // cas, ChoosePath renvoyait "" -> RunRoute sortait en silence (étape ignorée = script FIGÉ après la récolte, sans
        // aucun log). CastToString (InvariantCulture) rend "159489", résolu ensuite en voyage comme un mapId.
        if (raw.Type == DataType.Number) return raw.CastToString() ?? "";
        if (raw.Type != DataType.Table) return "";

        var cands = new List<(string Path, int Weight)>();
        foreach (var e in raw.Table.Values)
        {
            if (e.Type == DataType.String)
            {
                var s = e.String?.Trim();
                if (!string.IsNullOrEmpty(s)) cands.Add((s, 1));
            }
            else if (e.Type == DataType.Number)   // liste d'ids numériques : path = { 154133, 154134 }
            {
                var s = e.CastToString();
                if (!string.IsNullOrEmpty(s)) cands.Add((s, 1));
            }
            else if (e.Type == DataType.Table)
            {
                string d = (e.Table.Get("direction").CastToString() ?? e.Table.Get("path").CastToString() ?? "").Trim();
                if (d.Length == 0) continue;
                int w = (int)e.Table.Get("weight").CastToNumber();   // nil/absent -> 0
                cands.Add((d, w < 1 ? 1 : w));
            }
        }
        if (cands.Count == 0) return "";
        if (cands.Count == 1) return LuaCompat.PickAlternative(cands[0].Path, Random.Shared);

        // Anti-retour : retirer la direction opposee a la derniere sortie SI d'autres candidats restent. On resout
        // aussi les candidats en COORD adjacente vers leur direction cardinale pour pouvoir les comparer.
        if (_lastExitDir is MapDir last && Opposite(last) is MapDir back)
        {
            var kept = cands.Where(c => CandDir(c.Path, pos) != back).ToList();
            if (kept.Count > 0) cands = kept;
        }

        int total = 0; foreach (var c in cands) total += c.Weight;
        int roll = Random.Shared.Next(total);
        foreach (var c in cands) { roll -= c.Weight; if (roll < 0) return LuaCompat.PickAlternative(c.Path, Random.Shared); }
        return LuaCompat.PickAlternative(cands[cands.Count - 1].Path, Random.Shared);
    }

    // Direction cardinale d'un candidat de sortie, ou null : soit une direction ("left" / "left(123)"), soit une coord ADJACENTE.
    private static MapDir? CandDir(string cand, string pos)
    {
        if (LuaCompat.TryParseDirToken(cand, out var d, out _)) return d;
        if (TryDirToCoord(pos, cand, out var dir) && TryParseDir(dir, out var d2)) return d2;
        return null;
    }

    private static MapDir Opposite(MapDir d) => d switch
    {
        MapDir.Top => MapDir.Bottom,
        MapDir.Bottom => MapDir.Top,
        MapDir.Left => MapDir.Right,
        MapDir.Right => MapDir.Left,
        _ => d,
    };

    // Parse un coord "x,y" -> (x,y). NORMALISE les tirets unicode (‐‑‒–—―−) en '-' et ignore les espaces : c'est LA cause
    // des "aucune etape" alors que la carte est dans le trajet (un coord copie-colle contient souvent un faux tiret).
    private static bool TryParseCoord(string s, out int x, out int y)
    {
        x = y = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Replace('‐', '-').Replace('‑', '-').Replace('‒', '-').Replace('–', '-')
             .Replace('—', '-').Replace('―', '-').Replace('−', '-');
        var parts = s.Split(',');
        return parts.Length == 2 && int.TryParse(parts[0].Trim(), out x) && int.TryParse(parts[1].Trim(), out y);
    }

    // Convertit un chemin "x,y" en direction cardinale SI la carte cible est adjacente (Dofus : bas = y+1, haut = y-1).
    private static bool TryDirToCoord(string from, string to, out string dir)
    {
        dir = "";
        if (!TryParseCoord(from, out int fx, out int fy) || !TryParseCoord(to, out int tx, out int ty)) return false;
        int dx = tx - fx, dy = ty - fy;
        if (dx == 1 && dy == 0) dir = "right";
        else if (dx == -1 && dy == 0) dir = "left";
        else if (dx == 0 && dy == 1) dir = "bottom";
        else if (dx == 0 && dy == -1) dir = "top";
        else return false;
        return true;
    }
}
