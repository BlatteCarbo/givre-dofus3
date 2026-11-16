using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Transport;
using GivreClone.App.Services.Transport.Socket;
using GivreClone.Engine.Accounts;
using GivreClone.Engine.Proxies;
using GivreClone.Protocol;
using GivreLauncher.Core.Ankama;

namespace GivreClone.App.ViewModels;

// ===================== Mode Socket (clientless) : pid synthétique, StartSocketCore, backoff de reconnexion automatique =====================
public partial class AccountsViewModel
{
    /// <summary>État de diagnostic du canal de commande Socket. « channel-registered » prouve uniquement
    /// que LaunchService possède un injecteur pour ce PID ; ni sa fenêtre de disponibilité interne,
    /// ni l'ACK du serveur ne sont garantis. Conçu pour la fiche bot / journal sans trafic réseau.</summary>
    public sealed record SocketControlObservation(string State, bool InjectorRegistered, string Explanation);

    /// <summary>Lecture hors réseau utilisable par BotsViewModel : distingue « carte reçue » de
    /// « injecteur réellement publié ». Ne déduit jamais la présence de l'injecteur de l'activité En jeu.
    /// Le contrôle des ACK et des capacités par action appartient au transport.</summary>
    public SocketControlObservation ObserveSocketControl(AccountRow? row)
    {
        if (row is not { Running: true, IsSocketSession: true, Pid: int pid } || pid <= 0)
            return new("offline", false, "Aucune session Socket active.");
        uint socketPid = (uint)pid;
        if (row.SocketBackoff || row.SocketParked || _launch.IsLinkSuspended(socketPid))
            return new("suspended", false, "Session Socket déconnectée ou en reconnexion.");
        if (!CanSocket || !LicenseService.IsAuthorized)
            return new("license-blocked", false, "Droit Socket absent ou session de licence non autorisée.");
        if (!LicenseService.CanBot)
            return new("botting-paused", false, "Les commandes du bot sont suspendues par la licence.");
        if (MappingCatalog.GetEffective() is null || MappingCatalog.MissingEssentials().Count > 0)
            return new("mapping-unavailable", false, "Mapping du protocole indisponible ou incomplet.");
        bool registered = _launch.HasSocketInjector(socketPid);
        if (_launch.CurrentMap(socketPid) is null)
            return new("awaiting-map", registered, "Carte serveur attendue avant le pilotage.");
        if (!registered)
            return new("observation-only", false, "Carte observée, mais injecteur de commandes absent (revoir la compatibilité du protocole et la session)." );
        if (_launch.IsReviewedSocketMovementOnly(socketPid))
            return new("movement-diagnostic-only", true,
                "Revue inter-patch : déplacements de diagnostic uniquement. Combat et expéditions non disponibles sans ACK validés.");
        return new("channel-registered", true, "Injecteur enregistré ; disponibilité de l'écriture et ACK serveur encore à vérifier.");
    }

    // ---------- MODE SOCKET (test étape A) : connexion clientless, sans Dofus.exe. Tout est journalisé en [socket*]
    //            dans la Console. Ne touche PAS au MITM. Présent uniquement sur la branche `socket`. ----------
    // Pid synthétique des sessions Socket : hors de la plage des vrais pid Windows (jamais aussi hauts) et impair (les
    // vrais pid sont multiples de 4) -> aucune collision, et les gardes IsSocketSession évitent toute opération Process.
    private int _socketPidSeq;
    private int NextSocketPid() => (int)LaunchService.SocketPidBase + (System.Threading.Interlocked.Increment(ref _socketPidSeq) * 2 - 1);

    // AllowConcurrentExecutions : la commande est PARTAGÉE par toutes les cartes -> sans lui, un clic Socket en attente du
    // mapping (jusqu'à ~40 s) grisait le bouton Socket de TOUS les comptes. Double clic sans risque : StartSocketCore
    // re-teste row.Running de façon synchrone (le 2e appel ne lance rien).
    /// <summary>Lève le marqueur « banni » d'un compte (après vérification par l'utilisateur) : les lancements automatiques reprennent.</summary>
    [RelayCommand]
    private void Unban(AccountRow? row)
    {
        if (row is null || !row.IsBanned) return;
        Dialogs.Confirm(Loc.T("L.Accounts.UnbanTitle"), Loc.Fmt("L.Accounts.UnbanBody", row.Login), onConfirm: () =>
        {
            GivreClone.Engine.Accounts.BanGate.Clear(row.Model);
            Stores.Accounts.Persist();
            row.RefreshBan();
            Toasts.Show(Loc.Fmt("L.Accounts.Unbanned", row.Login));
        });
    }

    /// <summary>Refuse un lancement AUTOMATIQUE d'un compte marqué banni (toast). Un lancement manuel n'appelle pas ce garde.</summary>
    private static bool BlockedByBan(AccountRow row)
    {
        if (GivreClone.Engine.Accounts.BanGate.ClearIfExpired(row.Model, DateTime.UtcNow))
        {
            Stores.Accounts.Persist();   // bannissement échu (ex. relance de l'appli) : marqueur levé, le lancement continue
            row.RefreshBan();
            return false;
        }
        if (!GivreClone.Engine.Accounts.BanGate.BlocksAutoLaunch(row.Model, DateTime.UtcNow)) return false;
        Toasts.Show(Loc.Fmt("L.Accounts.BannedNoAuto", row.Login), success: false);
        return true;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SocketTestAsync(AccountRow? row)
    {
        if (row is null) return;
        if (row.Running) { Toasts.Show($"{row.Login} est déjà lancé — arrête-le d'abord.", success: false); return; }
        // Le mapping est STREAMÉ après le login licence : sans lui aucun message ne se forge (codes/champs renumérotés à
        // chaque patch). On l'attend (modal de chargement, comme Lancer) ; StartSocketCore refuse s'il manque encore.
        if (MainViewModel.Instance is { } mvm) await mvm.EnsureMappingReadyAsync();
        if (row.Running) return;   // lancé entre-temps (autre clic, planning…) : ni 2e session ni changement de mode
        // Bouton « Socket » : devient le mode MÉMORISÉ du compte (planning, Lancer tout, équipe, relance) — seulement si la
        // session démarre vraiment (un refus : droit, mapping, multi, proxy… laisse le mode tel quel). allowSelect : ce lancement
        // MANUEL honore le « Type de connexion » du compte (écran de choix du serveur / du personnage) via un modal ; les
        // lancements automatiques (planning, équipe, Lancer tout) restent DIRECT en jeu (pas de modal).
        if (StartSocketCore(row, unattendedShield: null, character: null, allowSelect: true)) row.IsSocketMode = true;
    }

    // Lancement d'un compte en mode SOCKET (clientless). Mêmes gardes que le MITM (LaunchCoreAsync) : droit « multi »,
    // droits de ROUTE (adaptateur/proxy) et proxy FAIL-CLOSED (jamais de repli silencieux sur l'IP réelle). false = refusé
    // (toast déjà affiché). unattendedShield non null = sans surveillance (planning) : aucun modal Bouclier. character =
    // personnage à sélectionner (planning) ; null = le premier de la liste. allowSelect = ce lancement peut ouvrir le modal de
    // choix serveur/perso selon le « Type de connexion » du compte (bouton Socket manuel) ; false = toujours direct en jeu.
    private bool StartSocketCore(AccountRow row, NoShieldPrompt? unattendedShield, string? character, bool allowSelect = false,
        int explicitServerId = 0, int relaunchPid = 0, bool createCharacter = false)
    {
        if (row.SocketRunTask is { IsCompleted: false } && (!row.Running || relaunchPid > 0)) return false;
        if (row.SessionTransitionActive && relaunchPid == 0) return false;
        if (relaunchPid > 0)
        {
            if (!row.Running || row.Pid != relaunchPid || !RelaunchOwnedBy(relaunchPid, row)) return false;
        }
        else if (row.Running) return true;
        if (!allowSelect && BlockedByBan(row)) return false;   // lancement automatique d'un compte banni
        // Droit serveur « socket » (gate au POINT D'ACTION : bouton, planning, Lancer tout, équipe, relance passent tous ici).
        if (!CanSocket)
        {
            AudioService.PlayError();
            Toasts.Show($"{row.Login} : " + Loc.T("L.Perm.SocketDenied"), success: false);
            return false;
        }
        // Sans mapping, rien ne se forge (codes/champs renumérotés à chaque patch) : en clientless il n'y a pas de client
        // pour jouer « à l'aveugle » comme le propose le MITM -> refus net.
        if (MappingCatalog.GetEffective() is null || MappingCatalog.MissingEssentials().Count > 0)
        {
            AudioService.PlayError();
            Toasts.Show($"{row.Login} : mapping du protocole pas encore reçu — connexion socket impossible pour l'instant.", success: false);
            return false;
        }
        if (!MultiAllowed(row)) { Toasts.Show(Loc.T("L.Perm.MultiDenied"), success: false); return false; }
        if (!TryResolveRoute(row, out var entry, out var ps)) return false;
        if (!TryResolveGameRoute(row, out var gameEntry, out var gamePs)) return false;   // proxy du serveur de jeu (optionnel)
        ++row.LaunchGen;   // génération de CE lancement (le planning la compare pour adopter/vérifier le perso)
        row.ServerKickedOffline = false;   // nouvelle session : le « Reconnecter » d'après déconnexion serveur n'a plus lieu d'être
        // Socket supporte les DEUX sources : navigateur (tokens HAAPI) ET Ankama Launcher (keydata Zaap, comme le MITM).
        bool keyData = row.Model.Source == AuthSource.KeyData;
        IShieldCodePrompt shield = unattendedShield is not null ? unattendedShield : this;
        // « Type de connexion » du compte, honoré UNIQUEMENT pour un lancement manuel attendu (bouton Socket) : 0 = écran de choix
        // du serveur, 1 = choix du personnage (modal), 2 = direct en jeu (dernier connecté). Planning/équipe/relance = toujours direct.
        int selectMode = createCharacter ? 0 : explicitServerId > 0 ? 2 : SocketScriptedPickRegistry.Has(row.Login) ? 0 : allowSelect && unattendedShield is null ? row.Model.AutoConnectType : 2;   // 0 aussi pour un choix imposé par script

        // Le compte apparaît comme un BOT (onglet Bots) avec sa propre Console, comme en MITM : pid synthétique + Running.
        int pid = NextSocketPid();
        var cts = new System.Threading.CancellationTokenSource();
        row.IsSocketSession = true;
        // Un miroir à chaud ne doit pas annuler ce jeton : l'amont de jeu du Socket reste la seule session.
        row.SocketCts = cts;
        // Route figée (comme le MITM) : une révocation LIVE du droit adaptateur/proxy coupe ce compte (OnPermissionsChanged).
        row.Routing = RoutingOf(entry);
        row.GameRouting = RoutingOf(gameEntry);
        if (relaunchPid > 0) RekeyPid(row, relaunchPid, pid);
        else row.Pid = pid;
        row.Running = true;
        row.Activity = BotActivity.Connecting;
        row.Character = "Socket";
        row.Status = "Socket : connexion…";
        if (ps is null) SocketLog.Info((uint)pid, "route : SANS proxy (choix explicite du compte) — IP réelle");
        JournalRoute(row.Model.Id, (uint)pid, entry, ps, gameEntry, gamePs);
        if (gamePs is not null) SocketLog.Info((uint)pid, $"route : serveur de jeu via le proxy de jeu « {gameEntry!.Name} » ({gamePs.Kind} {gamePs.Host}:{gamePs.Port})");
        // This only starts the asynchronous connection task. The server may
        // still refuse HAAPI, the login, the selected server or the game ticket.
        // Never report a successful connection before the server response.
        Toasts.Show($"Tentative Socket démarrée pour {row.Login} — statut réel dans Bots, Console.",
            success: true, sound: unattendedShield is null);

        // OPSEC : confirme l'IP publique réellement utilisée (celle que voient les serveurs Ankama). Même chemin de
        // sortie que le socket de jeu (proxy/interface identiques) -> affichée dans la Console du bot ET la ligne État.
        _ = VerifySocketExitIpAsync(row, ps, gamePs, (uint)pid);   // IP de connexion puis, s'il y en a un, IP vue EN JEU

        // Fond : auth HAAPI + connexion + session de jeu, bloquants. Les logs sont routés vers CE pid -> Console du bot.
        // Boucle de SESSION : une coupure INATTENDUE (kick serveur, réseau, proxy 4G qui change d'IP…) déclenche une
        // reconnexion AUTOMATIQUE sur le MÊME pid (Console, entrée Bots et hôte Lua conservés, le script reprend dès que la
        // nouvelle session publie son injecteur). Garde d'un bot tiers « jamais deux connexions à moins d'1 min » + BACKOFF sur
        // les échecs consécutifs (1, 2, 4, 8, 15 min) et abandon au 7e ; un refus DÉFINITIF (banni, identifiants, Bouclier
        // exigé, keydata absent…) n'est JAMAIS retenté. Déconnexion/Arrêter = fin VOULUE : jamais de reconnexion.
        // Déconnexion IMPOSÉE par le serveur : session PARQUÉE (pid, entrée Bots et script gardés, « Déconnecté »), jamais de
        // reconnexion automatique ; « Reconnecter » la réveille. « Reconnecter » sur une session vivante / en attente :
        // reconnexion IMMÉDIATE, sans le plancher anti-empreinte (réservé à la reconnexion AUTOMATIQUE).
        row.SocketRunTask = Task.Run(async () =>
        {
            // permanent : refus définitif -> le planning ne relance pas ce compte avant la fin de sa plage (comme un arrêt
            // utilisateur). Sinon (coupures répétées) : le planning PREND la relance à son compte s'il gère ce compte,
            // exactement comme un crash de client MITM (OnClientExited).
            // status : statut de ligne explicite, sinon le statut générique refusé / perdu.
            void GiveUp(string reason, string toast, bool permanent, string? status = null) => Dispatcher.UIThread.Post(() =>
            {
                SocketLog.Info((uint)pid, reason);
                if (!row.IsSocketSession || row.Pid != pid) return;   // déjà arrêté par l'utilisateur entre-temps
                bool planning = !permanent && PlanningClaimsCrash?.Invoke(row) == true;
                StopCore(row);
                if (permanent) UserStopped?.Invoke(row);
                row.Status = status ?? (permanent ? "Socket : refusé — " + toast : "Socket : connexion perdue");
                AudioService.PlayError();
                Toasts.Show(planning ? $"{toast} (le planning reprendra la main)" : toast, success: false);
            });

            // Décodeur prêt AVANT la 1re trame (catalogue branché sur l'enveloppe, tables de noms de serveur lancées) : pendant
            // l'auth HAAPI, pas sur le thread UI.
            string accountId = row.Model.Id;
            string deviceSeed;
            try
            {
                _launch.WarmUpForSocket();
                deviceSeed = Stores.Accounts.GetOrCreateDeviceSeed(accountId);
            }
            catch (Exception e)
            {
                // A local decoder/store initialization failure happens BEFORE SocketProbe's own try/catch.
                // Without this guard the detached Task dies while Running=true and the synthetic bot never closes.
                GiveUp($"✘ préparation Socket impossible ({e.GetType().Name})",
                       $"{row.Login} : préparation du Socket impossible — consulte la Console.", permanent: true);
                return;
            }
            // Identité machine PAR COMPTE (device_identifier unique/stable) : forgée+persistée à la 1re connexion Socket,
            // stable ensuite. Sans ça, tous les comptes de la machine partagent le même device_identifier -> un serveur
            // mono-compte refuse structurellement le 2e compte (même « machine »). Le HWID de licence reste inchangé.
            var currentShield = shield;
            int fails = 0;   // échecs CONSÉCUTIFS (remis à 1 après une session saine)
            // Choix serveur/perso : demandé UNE fois (1re session), puis MÉMORISÉ -> les reconnexions repartent droit dessus, sans
            // re-demander. pickedServerId 0 = auto/pas encore choisi ; pickedCharacter null = 1er perso / auto.
            int pickedServerId = explicitServerId;
            string? pickedCharacter = character;
            bool selectAborted = false;   // l'utilisateur a fermé le modal de choix -> arrêt propre (pas de reconnexion)
            while (true)
            {
                if (cts.IsCancellationRequested) break; // arrêté durant le préchauffage ou entre deux sessions
                // Numéro de CETTE session (une coupure / une reprise de script vise une session précise, jamais la suivante).
                int seq = Interlocked.Increment(ref row.SocketSessionSeq);
                _launch.NoteSocketSessionGeneration((uint)pid, seq);
                try { _launch.ReleaseLinkDown((uint)pid); }
                catch (Exception e)
                {
                    GiveUp($"✘ préparation du lien Socket impossible ({e.GetType().Name})",
                           $"{row.Login} : réinitialisation Socket impossible.", permanent: true);
                    return;
                }
                long started = Environment.TickCount64;
                // Jetons relus à CHAQUE (re)connexion : l'auth HAAPI a pu les renouveler entre-temps. Comptes Ankama Launcher :
                // rien ici, le jeton vient du keydata Zaap dans SocketProbe.
                string? apiKey = null, refresh = null, certId = null, certEnc = null;
                try
                {
                    if (!keyData)
                    {
                        (apiKey, refresh) = Stores.Accounts.GetBrowserTokens(accountId);
                        (certId, certEnc) = Stores.Accounts.GetCertificate(accountId);
                    }
                }
                catch (Exception e)
                {
                    // DPAPI / store unreadable: do not leave Running=true or loop on the same broken secrets.
                    GiveUp($"✘ jetons du compte illisibles ({e.GetType().Name})",
                           $"{row.Login} : jetons de connexion illisibles — reconnecte le compte.", permanent: true);
                    return;
                }
                SocketOutcome outcome;
                bool manual, sessionCancelled;
                // CTS PROPRE à la session (lié à celui du compte) : « Reconnecter » coupe CETTE session sans arrêter le compte.
                using (var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
                {
                    var session = new SocketSessionHandle(sessionCts, seq);
                    Volatile.Write(ref row.SocketSession, session);
                    SocketLog.Info((uint)pid, $"session n°{seq} : ouverture de la connexion");
                    // Sélecteur serveur/perso : actif seulement tant qu'aucun choix n'a été fait (1re session d'un lancement en mode
                    // « écran de choix… »). Une fois choisi -> pickedServerId > 0, chooser null, les reconnexions repartent direct.
                    Func<IReadOnlyList<ConnServer>, CancellationToken, Task<(int ServerId, string? Character)?>>? chooser =
                        selectMode != 2 && pickedServerId == 0
                            ? async (servers, cct) =>
                            {
                                var pick = await PromptSocketServerCharAsync(row, servers, selectMode, cct, createCharacter).ConfigureAwait(false);
                                if (pick is { } pk && pk.ServerId > 0) { pickedServerId = pk.ServerId; pickedCharacter = pk.Character; return (pk.ServerId, pk.Character); }
                                selectAborted = true; return null;
                            }
                            : null;
                    try
                    {
                        outcome = await SocketProbe.RunAsync((uint)pid, row.Login, deviceSeed, ps, keyData, apiKey, refresh, certId, certEnc,
                                                         pickedServerId, pickedCharacter, currentShield,
                                                         // Bouclier forgé pendant l'auth -> persisté (chiffré), comme le lancement MITM.
                                                         onCertMinted: (cid, enc) => Stores.Accounts.SetCertificate(accountId, cid, enc),
                                                         ct: sessionCts.Token,
                                                         // 2e essai « session précédente encore tenue » : l'attente est expliquée sur la ligne.
                                                         onGhostRetry: secs => Dispatcher.UIThread.Post(() =>
                                                         {
                                                             if (!row.IsSocketSession || row.Pid != pid) return;
                                                             // Compteurs recréés par les trames de la tentative éjectée : sans ça,
                                                             // UpdateCapture ré-écrivait « Connexion » par-dessus ce statut en ≤ 2,5 s.
                                                             _pidStats.TryRemove((uint)pid, out _);
                                                             row.Activity = BotActivity.Connecting;
                                                             row.Status = Loc.Fmt("L.Reconnect.SocketGhost", secs);
                                                         }),
                                                         chooseServerChar: chooser,
                                                         gameProxy: gamePs).ConfigureAwait(false);   // session de jeu via le proxy de jeu du compte
                    }
                    catch (OperationCanceledException) when (sessionCts.IsCancellationRequested)
                    {
                        outcome = new SocketOutcome(SocketOutcomeKind.Cancelled, "session annulée");
                    }
                    catch (Exception e)
                    {
                        // Any future transport exception still goes through the ordinary retry budget/teardown.
                        SocketLog.Info((uint)pid, $"échec Socket hors transport ({e.GetType().Name})");
                        outcome = new SocketOutcome(SocketOutcomeKind.Transient, $"erreur de session ({e.GetType().Name})");
                    }
                    Interlocked.CompareExchange(ref row.SocketSession, null, session);
                    // Coupée à la demande (« Reconnecter ») ? Passage ATOMIQUE à « close » : une demande arrivée après ne compte pas.
                    manual = Interlocked.Exchange(ref session.State, 2) == 1;
                    sessionCancelled = sessionCts.IsCancellationRequested;
                }
                // Choix serveur/perso fermé par l'utilisateur : arrêt PROPRE, jamais retenté (ni reconnexion auto, ni « planning reprend »).
                if (selectAborted && !cts.IsCancellationRequested)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        SocketLog.Info((uint)pid, "sélection serveur/personnage annulée — arrêt.");
                        if (!row.IsSocketSession || row.Pid != pid) return;
                        StopCore(row);
                        UserStopped?.Invoke(row);
                        row.Status = "Socket : sélection annulée";
                    });
                    break;
                }
                if (cts.IsCancellationRequested) break;                                  // Déconnexion / Arrêter : fin voulue
                if (row.SessionTransitionActive)
                {
                    // Explicit transitions own one attempt. Never adopt an automatic retry as the
                    // requested target or leave confirmation waiting on an unowned Socket sequence.
                    GiveUp("transition explicite : session cible terminée avant confirmation.",
                        outcome.Reason, permanent: false, status: "Session transition: disconnected");
                    return;
                }
                // Filet : « annulée » alors que PERSONNE n'a rien annulé (ni le compte, ni la session) = délai dépassé quelque part
                // (HttpClient…) -> passager (backoff / abandon normaux), jamais une sortie muette qui laisse la ligne « en ligne »
                // sans session.
                if (outcome.Kind == SocketOutcomeKind.Cancelled && !manual && !sessionCancelled)
                {
                    SocketLog.Info((uint)pid, $"⚠ session n°{seq} : annulation inattendue (délai dépassé) — traitée comme une coupure passagère");
                    outcome = new SocketOutcome(SocketOutcomeKind.Transient, "annulation inattendue (délai dépassé)");
                }
                if (outcome.Kind == SocketOutcomeKind.Cancelled && !manual) break;

                // Déconnexion IMPOSÉE par le serveur (message système hang_up : inactivité, modération…) : raison lue AVANT Forget.
                string? hangUp = _launch.RecentHangUpReason((uint)pid);
                _launch.Forget((uint)pid);   // état moteur périmé (combat en cours, carte, move en attente) : repeuplé à la reconnexion
                bool attended;               // reconnexion demandée par l'utilisateur (modal Bouclier possible) ou automatique
                if (manual)
                {
                    // « Reconnecter » sur une session vivante : l'onglet Bots a déjà suspendu le script (avant la coupure) ; il le
                    // reprend quand la NOUVELLE session aura chargé sa carte. Prioritaire sur l'issue de la session coupée.
                    SocketLog.Info((uint)pid, $"=== session n°{seq} coupée à la demande (« Reconnecter ») — reconnexion IMMÉDIATE, aucun délai ===");
                    // Cette transition précède OBLIGATOIREMENT la nouvelle connexion. Avec Post, le thread
                    // de session repartait immédiatement et la callback différée pouvait effacer la NOUVELLE
                    // carte reçue entre-temps (et remettre le bot « Connexion » malgré son contexte vivant).
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!row.IsSocketSession || row.Pid != pid) return;
                        _pidStats.TryRemove((uint)pid, out _);
                        Stores.World.ClearMap((uint)pid);
                        row.MapId = null; row.MapCoords = null;
                        row.Activity = BotActivity.Connecting;
                        row.Status = Loc.T("L.Reconnect.SocketNow");
                        RefreshBotSnapshot();
                    });
                    fails = 0; attended = true;
                }
                else if (outcome.ServerKick || (outcome.Kind == SocketOutcomeKind.Permanent && hangUp is not null))
                {
                    // Déconnexion IMPOSÉE par le serveur : session PARQUÉE (pid, entrée Bots, Console et script gardés), comme un
                    // MITM « Déconnecté ». Jamais de reconnexion automatique : « Reconnecter » réveille, « Arrêter » coupe.
                    string reason = hangUp ?? outcome.Reason;
                    SocketLog.Info((uint)pid, $"✘ {reason} : session n°{seq} PARQUÉE (pid conservé) — aucune reconnexion automatique ; " +
                                              "« Reconnecter » relance, « Arrêter » coupe.");
                    _launch.HoldLinkDown((uint)pid, reason, imposed: true, byServer: true);
                    var parkWake = new TaskCompletionSource<bool>(seq, TaskCreationOptions.RunContinuationsAsynchronously);
                    Volatile.Write(ref row.SocketWake, parkWake);   // publié AVANT que l'UI ne montre la session parquée
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!row.IsSocketSession || row.Pid != pid) return;
                        _pidStats.TryRemove((uint)pid, out _);
                        Stores.World.ClearMap((uint)pid);
                        row.MapId = null; row.MapCoords = null;
                        row.Activity = BotActivity.Disconnected;
                        row.Status = Loc.Fmt("L.Accounts.ServerKicked", reason);
                        row.RecordDisconnect(row.Status);
                        row.SocketParked = true; row.ReconnectInPlace = false;
                        SocketSessionLost?.Invoke(pid, SocketPauseKind.Parked, seq);   // onglet Bots : script suspendu, gardé
                        RefreshBotSnapshot(); LicenseService.PulseBotTelemetry();
                        AudioService.PlayError();
                        Toasts.Show(Loc.Fmt("L.Accounts.SocketKickedToast", row.Login, reason), success: false, sound: false);
                    });
                    try { await parkWake.Task.WaitAsync(cts.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }   // Arrêter / Déconnexion / planning : fin voulue
                    finally
                    {
                        Interlocked.CompareExchange(ref row.SocketWake, null, parkWake);
                        // Doit être terminé AVANT l'itération suivante : un Post différé pourrait effacer
                        // le drapeau de la nouvelle session, ou laisser l'ancien à true indéfiniment.
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (row.IsSocketSession && row.Pid == pid) row.SocketParked = false;
                        });
                    }
                    _launch.ReleaseLinkDown((uint)pid);
                    SocketLog.Info((uint)pid, $"reprise de la session parquée n°{seq} (« Reconnecter ») — reconnexion IMMÉDIATE");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!row.IsSocketSession || row.Pid != pid) return;
                        row.Activity = BotActivity.Connecting;
                        row.Status = Loc.T("L.Reconnect.SocketNow");
                    });
                    fails = 0; attended = true;
                }
                else if (outcome.Kind == SocketOutcomeKind.Permanent)
                {
                    TimeSpan? resumeIn = null;
                    if (outcome.Banned)
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            // Une seule alerte par bannissement : relancer le compte pendant le même ban ne la renvoie pas.
                            bool known = GivreClone.Engine.Accounts.BanGate.IsBanned(row.Model) && row.Model.BanEnd == outcome.BanEnd;
                            GivreClone.Engine.Accounts.BanGate.Record(row.Model, DateTime.UtcNow, outcome.BanEnd);
                            Stores.Accounts.Persist();
                            row.RefreshBan();
                            resumeIn = AppConfig.Current.AutoRelaunch
                                ? GivreClone.Engine.Accounts.BanGate.AutoResumeDelay(row.Model, DateTime.UtcNow) : null;
                            if (known) row.SetBanResume(resumeIn is { } at ? DateTime.Now.Add(at) : null);
                            if (!known)
                            {
                                row.SetBanResume(resumeIn is { } at ? DateTime.Now.Add(at) : null);
                                string text = resumeIn is not null
                                    ? $"⛔ {row.Login} : {row.BanLabel}."
                                    : $"⛔ {row.Login} : {row.BanLabel}. Plus aucun lancement automatique jusqu'au déblocage.";
                                Toasts.Show(text, success: false);
                                Services.PhoneAlerts.PhoneAlertHub.Raise("ban", row.Login, text, key: $"ban|{row.Login}|{outcome.BanEnd}");
                            }
                        });
                    if (resumeIn is { } banWait)
                    {
                        // Suspension TEMPORAIRE avec date de fin : une seule tentative après l'échéance, jamais de boucle
                        // pendant la suspension. « Reconnecter » écourte l'attente, « Annuler la reprise » / Déconnexion l'arrêtent.
                        SocketLog.Info((uint)pid, $"⛔ suspension temporaire jusqu'au {outcome.BanEnd} — reconnexion automatique dans {(int)banWait.TotalSeconds} s");
                        var banWake = new TaskCompletionSource<bool>(seq, TaskCreationOptions.RunContinuationsAsynchronously);
                        Volatile.Write(ref row.SocketWake, banWake);
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (!row.IsSocketSession || row.Pid != pid) return;
                            _pidStats.TryRemove((uint)pid, out _);
                            Stores.World.ClearMap((uint)pid);
                            row.MapId = null; row.MapCoords = null;
                            row.Activity = BotActivity.Disconnected;
                            row.Status = $"Suspendu — reconnexion auto à {DateTime.Now.Add(banWait):HH:mm}";
                            row.SocketBackoff = true;
                            row.ReconnectInPlace = false;
                            SocketSessionLost?.Invoke(pid, SocketPauseKind.Lost, seq);   // script suspendu, repris après la reconnexion
                        });
                        bool banResumeCancelled = false, banWoken = false;
                        try
                        {
                            using var banCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                            var banDelay = Task.Delay(banWait, banCts.Token);
                            if (await Task.WhenAny(banDelay, banWake.Task).ConfigureAwait(false) != banWake.Task)
                                await banDelay.ConfigureAwait(false);
                            else { banResumeCancelled = !banWake.Task.Result; banWoken = !banResumeCancelled; banCts.Cancel(); }
                        }
                        catch (OperationCanceledException) { break; }   // Déconnexion pendant la suspension
                        finally
                        {
                            Interlocked.CompareExchange(ref row.SocketWake, null, banWake);
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                row.SetBanResume(null);
                                if (row.IsSocketSession && row.Pid == pid) row.SocketBackoff = false;
                            });
                        }
                        if (banResumeCancelled)
                        {
                            GiveUp("reprise automatique annulée par l'utilisateur : aucune nouvelle tentative.",
                                   Loc.Fmt("L.Accounts.ResumeCancelledToast", row.Login), permanent: true,
                                   status: Loc.T("L.Accounts.ResumeCancelledStatus"));
                            return;
                        }
                        // Décision unique (BanGate) : « relance auto » coupée pendant l'attente = annulé (sauf réveil manuel) ;
                        // marqueur levé seulement si la date de fin est passée (réveil manuel avant l'échéance : ban toujours actif).
                        bool banRelaunch = false;
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            banRelaunch = GivreClone.Engine.Accounts.BanGate.CompleteResumeWait(
                                row.Model, DateTime.UtcNow, banWoken, AppConfig.Current.AutoRelaunch);
                            if (!banRelaunch) return;
                            Stores.Accounts.Persist();
                            row.RefreshBan();
                            if (!row.IsSocketSession || row.Pid != pid) return;
                            row.Activity = BotActivity.Connecting;
                            row.Status = Loc.T("L.Reconnect.SocketNow");
                        });
                        if (!banRelaunch)
                        {
                            GiveUp("relance auto désactivée pendant l'attente : reconnexion annulée.", $"{row.Login} : reconnexion socket annulée.", permanent: false);
                            return;
                        }
                        SocketLog.Info((uint)pid, banWoken ? "réveil manuel pendant la suspension : reconnexion" : "suspension échue : reconnexion");
                        fails = 0; attended = banWoken;
                    }
                    else
                    {
                    GiveUp($"✘ refus DÉFINITIF — {outcome.Reason} : aucune reconnexion automatique (intervention requise).",
                           $"{row.Login} : {outcome.Reason}", permanent: true);
                    return;
                    }
                }
                else
                {
                    // Coupure INATTENDUE (réseau, proxy 4G, session terminée…) : reconnexion AUTOMATIQUE après le backoff. Le
                    // plancher anti-empreinte (15 s après la coupure, jamais deux connexions à moins d'1 min) ne vaut QUE pour elle.
                    long ran = Environment.TickCount64 - started;
                    fails = outcome.Kind == SocketOutcomeKind.SessionEnded && ran > SocketHealthySessionMs ? 1 : fails + 1;
                    if (!AppConfig.Current.AutoRelaunch)
                    {
                        GiveUp($"⚠ connexion perdue ({outcome.Reason}) — relance auto désactivée (Réglages) : arrêt.",
                               $"{row.Login} : connexion socket perdue.", permanent: false);
                        return;
                    }
                    if (fails > SocketMaxConsecutiveFails)
                    {
                        GiveUp($"⚠ {fails - 1} échecs de connexion consécutifs ({outcome.Reason}) : reconnexion suspendue.",
                               $"{row.Login} : socket coupé à répétition — reconnexion suspendue.", permanent: false);
                        return;
                    }
                    long backoff = Math.Min(SocketBackoffCapMs, SocketReconnectMinGapMs << Math.Min(fails - 1, 8));
                    long wait = Math.Max(SocketReconnectMinDelayMs, backoff - ran);
                    SocketLog.Info((uint)pid, $"⚠ connexion perdue ({outcome.Reason}) — session n°{seq} terminée, reconnexion automatique dans {wait / 1000} s " +
                                              $"(échec consécutif n°{fails}, jamais deux connexions à moins d'1 min)");
                    // « Reconnecter » (onglet Bots / carte compte) peut ÉCOURTER cette attente (départ immédiat) : réveil publié
                    // AVANT que l'UI ne montre l'attente (SocketBackoff). AsyncState = numéro de la session terminée.
                    var wake = new TaskCompletionSource<bool>(seq, TaskCreationOptions.RunContinuationsAsynchronously);
                    Volatile.Write(ref row.SocketWake, wake);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!row.IsSocketSession || row.Pid != pid) return;
                        // État périmé : sans ça le poll (UpdateCapture) ré-affichait « Jeu » + l'ancienne carte pendant l'attente, et
                        // planning/équipe/télémétrie croyaient le bot toujours en jeu. Le nom du perso est gardé (adoption planning).
                        _pidStats.TryRemove((uint)pid, out _);
                        Stores.World.ClearMap((uint)pid);
                        row.MapId = null; row.MapCoords = null;
                        row.Activity = BotActivity.Connecting;
                        row.Status = $"Socket : reconnexion dans {wait / 1000} s…";
                        row.SocketBackoff = true;
                        row.ReconnectInPlace = false;   // une reconnexion demandée qui échoue rend la main : « Reconnecter » réveille l'attente
                        SocketSessionLost?.Invoke(pid, SocketPauseKind.Lost, seq);   // onglet Bots : suspend le script, le reprendra après la reconnexion
                        Toasts.Show($"{row.Login} : connexion socket perdue — reconnexion auto dans {wait / 1000} s.", success: false);
                        ConnectionJournal.ForBot((uint)pid, Loc.Fmt("L.Journal.Reconnecting", wait / 1000), LuaLogLevel.Warning);
                    });
                    bool woken = false, resumeCancelled = false;
                    try
                    {
                        using var backoffCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                        var delay = Task.Delay(TimeSpan.FromMilliseconds(wait), backoffCts.Token);
                        if (await Task.WhenAny(delay, wake.Task).ConfigureAwait(false) != wake.Task)
                            await delay.ConfigureAwait(false);   // échéance du backoff (Déconnexion : annulation propagée)
                        else if (!wake.Task.Result)
                        {
                            resumeCancelled = true;   // « Annuler la reprise » (carte compte) : plus aucune tentative
                            backoffCts.Cancel();
                        }
                        else
                        {
                            woken = true;
                            backoffCts.Cancel();   // minuterie du backoff libérée
                            // Demande EXPLICITE de l'utilisateur : départ immédiat, sans plancher (le 2e essai « session précédente
                            // encore tenue » de SocketProbe couvre une session que le serveur n'aurait pas encore lâchée).
                            SocketLog.Info((uint)pid, "réveil manuel (« Reconnecter ») : départ IMMÉDIAT, aucun délai");
                            Dispatcher.UIThread.Post(() =>
                            {
                                if (row.IsSocketSession && row.Pid == pid) row.Status = Loc.T("L.Reconnect.SocketNow");
                            });
                        }
                    }
                    catch (OperationCanceledException) { break; }   // Déconnexion pendant l'attente = reconnexion annulée
                    finally
                    {
                        Interlocked.CompareExchange(ref row.SocketWake, null, wake);
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (row.IsSocketSession && row.Pid == pid) row.SocketBackoff = false;
                        });
                    }
                    if (resumeCancelled)
                    {
                        GiveUp("reprise automatique annulée par l'utilisateur : aucune nouvelle tentative.",
                               Loc.Fmt("L.Accounts.ResumeCancelledToast", row.Login), permanent: true,
                               status: Loc.T("L.Accounts.ResumeCancelledStatus"));
                        return;
                    }
                    // Relance auto désactivée pendant l'attente -> annulée, sauf réveil MANUEL (« Reconnecter » = demande explicite).
                    if (!woken && !AppConfig.Current.AutoRelaunch)
                    {
                        GiveUp("relance auto désactivée pendant l'attente : reconnexion annulée.", $"{row.Login} : reconnexion socket annulée.", permanent: false);
                        return;
                    }
                    attended = woken;
                }
                // Licence tuée/expirée, mapping purgé ou droit « socket » retiré pendant l'attente : on ne rouvre RIEN (ni HAAPI,
                // ni serveur de connexion).
                if (!LicenseService.IsAuthorized || MappingCatalog.GetEffective() is null || MappingCatalog.MissingEssentials().Count > 0 || !CanSocket)
                {
                    GiveUp("licence non autorisée / mapping purgé / droit « socket » retiré : reconnexion annulée.",
                           $"{row.Login} : reconnexion socket annulée (licence).", permanent: true);
                    return;
                }
                // Route RE-RÉSOLUE à chaque reconnexion (fail-closed) : droit adaptateur/proxy retiré, carte réseau 4G éteinte,
                // proxy supprimé… -> on s'arrête au lieu de repartir en direct (IP réelle) ou sur une route périmée.
                if (entry is not null)
                {
                    // Énumération des cartes réseau protégée : elle peut lever pendant le débranchement d'un adaptateur 4G/USB
                    // (NetworkInformationException), ce qui aurait tué cette boucle et laissé le compte « en ligne » sans session.
                    GivreLauncher.Core.Proxy.ProxySettings? again = null;
                    try { if (RoutingStillAllowed(row)) again = ProxyResolver.Resolve(entry, Stores.Proxies); }
                    catch (Exception ex) { SocketLog.Info((uint)pid, $"route : résolution impossible ({ex.GetType().Name})"); }
                    if (again is null)
                    {
                        GiveUp("route (proxy/adaptateur) injoignable ou droit retiré : reconnexion annulée — jamais de repli sur l'IP réelle.",
                               $"{row.Login} : proxy injoignable — reconnexion socket annulée.", permanent: false);
                        return;
                    }
                    ps = again;
                }
                if (gameEntry is not null)
                {
                    GivreLauncher.Core.Proxy.ProxySettings? gameAgain = null;
                    try { if (RoutingStillAllowed(row)) gameAgain = ProxyResolver.Resolve(gameEntry, Stores.Proxies); }
                    catch (Exception ex) { SocketLog.Info((uint)pid, $"route de jeu : résolution impossible ({ex.GetType().Name})"); }
                    if (gameAgain is null)
                    {
                        GiveUp("proxy de jeu injoignable ou droit retiré : reconnexion annulée — jamais de repli sur l'IP réelle.",
                               $"{row.Login} : proxy de jeu injoignable — reconnexion socket annulée.", permanent: false);
                        return;
                    }
                    gamePs = gameAgain;
                }
                SocketLog.Info((uint)pid, attended ? "=== RECONNEXION demandée ===" : "=== RECONNEXION automatique ===");
                // Reconnexion automatique = SANS SURVEILLANCE (souvent la nuit) : jamais de modal Bouclier ; un code exigé fait
                // échouer proprement cette tentative au lieu d'ouvrir une fenêtre que personne ne remplira. « Reconnecter » :
                // l'utilisateur est là -> modal Bouclier possible.
                currentShield = attended ? this : (IShieldCodePrompt)new NoShieldPrompt();
                _ = VerifySocketExitIpAsync(row, ps, gamePs, (uint)pid);   // l'IP de sortie a pu changer (proxy 4G) -> re-confirmée
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (row.IsSocketSession && row.Pid == pid) row.Status = "Socket : arrêté";
            });
        });
        return true;
    }

    // Reconnexion auto du mode Socket : délai = max(15 s après la coupure, backoff depuis le début de la connexion
    // précédente). Backoff = 1 min doublé à chaque échec
    // consécutif, plafonné à 15 min ; abandon après 6 reconnexions ratées d'affilée (~45 min d'essais). Une session qui a
    // tenu 5 min remet le compteur à zéro. Reconnexion AUTOMATIQUE seulement : « Reconnecter » (clic) part tout de suite.
    private const long SocketReconnectMinGapMs = 60_000;
    private const long SocketReconnectMinDelayMs = 15_000;
    private const long SocketBackoffCapMs = 15 * 60_000;
    private const int SocketMaxConsecutiveFails = 6;
    private const long SocketHealthySessionMs = 5 * 60_000;
}
