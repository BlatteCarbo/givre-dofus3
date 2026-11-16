using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Engine;
using GivreClone.Engine.LuaTooling;
using GivreClone.Protocol;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

/// <summary>Niveau d'un evenement de script -> couleur cote UI.</summary>
public enum LuaLogLevel { Info, Success, Warning, Danger, Debug }

public enum LuaRunOutcome { Completed, Stopped, Failed }

/// <summary>Une ligne de journal d'un script : horodatage, niveau (couleur/pastille), categorie ([deplacement]...),
/// texte, + option couleur hex libre et gras (balises &lt;b&gt;...&lt;/b&gt;).</summary>
public sealed record LuaLogEntry(DateTime Time, LuaLogLevel Level, string Category, string Message,
                                 string? ColorHex = null, bool Bold = false, LuaErrorLocation? Location = null);

/// <summary>
/// Hote de script LUA d'UN bot (par PID). Moteur = MoonSharp (100% manage: pas de DLL native, arret
/// cooperatif propre). Modele de script = un bot tiers: le script definit des globals de config (MAX_PODS, MIN/MAX_MONSTERS,
/// FORBIDDEN/FORCE_MONSTERS) + une fonction <c>move()</c> (et optionnellement <c>bank()</c>), pilotees en boucle.
///
/// Les verbes (goToCellId, goUseInteractive, gatherAll, fight, changeMap) delegent au <see cref="LaunchService"/> partage
/// et lisent l'etat via <see cref="BotWorldStore"/> (headless). Les deplacements sont BLOQUANTS cote script : ils
/// attendent la resolution serveur (jsq/jqt) via <see cref="LaunchService.SendAndWaitAsync"/>. L'arret (stopScript ou
/// Stop()) annule un CancellationToken que chaque verbe honore -> le script se deroule proprement (pas de Thread.Abort).
///
/// CHAQUE action journalise ses faits et gestes via <see cref="Log"/> (categorie + niveau), facon console un bot tiers.
/// </summary>
public sealed partial class GivreLuaHost : IDisposable
{
    private const int MoveTimeoutMs = 16000;            // marche + CADENCE du jqi de secours (~ duree du trajet + marge, jusqu'a 8 s) + ack jsq
    private const int MapChangeAttempts = 3;            // le 1er move de transition juste apres un chargement de carte est PARFOIS ignore (client/serveur pas prets) -> on re-tente
    private const int MapChangeAttemptTimeoutMs = 16000;// par tentative : marche jusqu'au bord + jqi cadence (jusqu'a 8 s) + jqk + jru ; sinon la tentative expire avant le commit
    // Settle apres un changement de carte. AVANT : 1500 ms FIXES couvrant (a) le serveur qui refuse encore les moves un
    // court instant, (b) le chargement ASYNC des cellules (bundle) pas encore fini. Le (b) est desormais garanti par
    // WaitCellsReady (gate evenementiel dans WaitSelfReady) -> ce delai ne couvre plus que (a) + l'humanisation, d'ou une
    // plage JITTEE bien plus courte. Filet : si la geometrie n'arrive jamais, WaitCellsReady expire (CellsReadyTimeoutMs).
    private const int MapSettleMinMs = 400;
    private const int MapSettleMaxMs = 800;
    private const int TurboMapSettleMinMs = 15;
    private const int TurboMapSettleMaxMs = 35;
    private const int CellsReadyTimeoutMs = 5000;       // attente MAX de la geometrie de cellules de la nouvelle carte (bundle ~3.6 s en 1re visite) ; best-effort
    // Bornes du settle EFFECTIVES (par bot). Defaut = 400/800 (mode Rapide) ; le mode Legit (option Parametres) les
    // remplace par une plage plus large/humaine via ConfigureMovement() avant Start(). Instance, pas static, pour etre
    // configurable par personnage sans muter les autres hotes.
    private int _settleMinMs = MapSettleMinMs;
    private int _settleMaxMs = MapSettleMaxMs;
    private int MapSettle()
    {
        if (_launch?.IsTurboAckAll(_pid) == true)
            return Random.Shared.Next(TurboMapSettleMinMs, TurboMapSettleMaxMs + 1);
        return Random.Shared.Next(_settleMinMs, _settleMaxMs + 1);
    }
    // Après un combat la carte se recharge en ~30 ms côté serveur et WaitSelfReady attend la position confirmée : le « settle » complet (400-800 ms) n'est
    // plus nécessaire en mode Rapide ; le mode Legit (plage élargie) garde sa plage humaine.
    private int PostFightSettle() => _launch?.IsTurboAckAll(_pid) == true ? MapSettle() : _settleMinMs > MapSettleMinMs ? MapSettle() : Random.Shared.Next(150, 351);
    // Anti-detection : humanise les delais FIXES hors-combat (recolte/voyage/attentes) de +/-15%. Un bot qui attend
    // pile 2000 ms a chaque carte est signable ; un humain non. Applique de facon centralisee dans InterruptibleDelay,
    // donc couvre les ~35 sites + le verbe Lua delay(). NE touche PAS le pacing combat (Pacer.cs, jitte a part).
    private const double JitterPct = 0.15;
    private static int Jitter(int ms) => ms <= 1 ? ms : (int)Math.Clamp(ms + Math.Round(ms * JitterPct * (Random.Shared.NextDouble() * 2 - 1)), 0, int.MaxValue);
    private const int HarvestTimeoutMs = 12000;   // recolte = marche + animation; Un bot tiers attend 10 s l'ObjectHarvested
    private const int FightStartTimeoutMs = 12000;
    private const int FightEndTimeoutMs = 600000;   // 10 min : un combat auto peut etre long
    private const int TeamWaitTimeoutMs = 30000;    // attente MAX des followers avant que le chef n'engage (borne : jamais de blocage infini)
    private const int ZaapDestTimeoutMs = 5000;             // attente de la liste des destinations (hjj) apres ouverture du zaap
    private const int HavenBagInteractiveTimeoutMs = 6000;  // attente que le zaap du havre-sac soit decode (interactifs en tache de fond)
    private const int ZaapWalkMargin = 2;                   // hysteresis : on ne teleporte que si la marche directe coute > hops(zaap->cible) + marge
    private const int ZaapMinHops = 6;                      // liste des zaaps INCONNUE : on va apprendre au havre-sac seulement si la cible est a plus de N cartes a pied
    private const int ZaapWalkMarginInterior = 6;           // depuis un INTERIEUR (calque -1) : l'aller-retour havre-sac coute plus -> marge plus large
    private const int LongWalkLogHops = 15;                 // au-dela de N cartes a pied sans zaap, on journalise POURQUOI (plus de longue marche muette)
    private long _lastLongWalkLog;                          // anti-spam de ce diagnostic (une ligne par cible)
    private const int InteractivesDecodeTimeoutMs = 8000;   // attente MAX du decodage async des interactifs avant de conclure "rien a recolter". Le decodage attend le chargement du bundle de cellules (jusqu'a ~3.6 s en 1re visite) -> 3 s etait TROP court (skip de ressources). On sort DES que le decodage tombe (rapide si carte en cache ou vide), le cap ne sert que de filet.
    private const int SelfReadyTimeoutMs = 8000;    // attente MAX du jss (position self) apres un changement de carte

    private readonly uint _pid;
    private readonly LaunchService _launch;
    private readonly BotWorldStore _world;
    private readonly GivreClone.Engine.BehaviouralCircuitBreaker _behaviouralCircuitBreaker = new();
    private string? _fightFailureReason = "unknown";
    private bool _expeditionFightAttempt;
    private string? _travelFailureReason = "unknown";
    private readonly Action<uint, int, int, int, string> _onHarvest;
    private readonly Action<uint, int> _onUseEnded;
    private readonly Action<uint, int> _onUseFailed;

    private CancellationTokenSource? _cts;
    private Task<LuaRunOutcome>? _runTask;
    private LuaRunOutcome _runOutcome;
    private Script? _script;
    private int _harvestTick;    // ++ a chaque ObjectHarvested (itn) : objet recolte (non utilise pour l'attente : voir GatherResource)
    private int _lastHarvestQty; // quantite CUMULEE des ObjectHarvested (itn) depuis le debut de la recolte en cours -> log "Récolté ×N"
    private int _endedElement;   // element_id du DERNIER iwi (InteractiveUseEnded) recu pour ce pid : fin d'anim, perso libre
    private int _errorElement;   // element_id du DERNIER iwq (InteractiveUseError) recu pour ce pid : recolte refusee
    private int _usedElement;    // element_id du DERNIER InteractiveUsedEvent PAR MOI (primitive map:useById : actions sans iwi, ex zaap/levier)
    private int _usedDuration;   // duration (dixiemes de s) du DERNIER InteractiveUsedEvent PAR MOI : ecrit AVANT _usedElement (lu apres)
    private readonly Action<uint, int, int, int> _onUsedSelf;
    private const int InteractiveUseTimeoutMs = 8000;   // attente MAX de la confirmation serveur d'un usage d'interactif NON récolte

    // ---- Lot B : compteurs de SESSION du script (stats:, getCountGather/Fight, afterFight) ; remis à 0 à chaque Start().
    //      Écrits par les events moteur (thread réseau) via Interlocked/Volatile, lus sur le thread du script. ----
    private int _sHarvests, _sResources, _sFights, _sWon, _sLost;
    private int _afterFight;             // 1 = un combat s'est terminé depuis le dernier afterFight
    private readonly Action<uint> _onFightEnded;
    private readonly Action<uint, bool, bool> _onFightOutcome;

    // ---- HOOKS DE COMBAT (Lot C1) : onTurn()/fightManagement() à chaque tour, onPlacement() au placement. Appelés
    //      UNIQUEMENT sur le thread du script (pompe dans RunLoop / WaitUntil / InterruptibleDelay) : MoonSharp n'est pas
    //      thread-safe, le moteur ne fait que TENDRE le tour (jeton) que le script réclame. ----
    private DynValue? _turnFn, _placeFn;
    private string _turnFnName = "onTurn";
    private bool _inHook;                 // un hook est en cours (pas de pompe réentrante)
    private int _hookEp;                  // époque du tour RÉCLAMÉ par le hook onTurn en cours (0 = hors hook de tour)
    private int _turnBudgetMs = 20000;    // TURN_TIME_LIMIT (ms), borné [3000, 25000]
    private object _runToken = new();     // propriétaire des hooks / override d'auto-combat du run COURANT

    // ---- Lot C2 : réglages de combat du script (génération du run) + hook onFightEnd(result) ----
    private int _runGen;                  // ++ à chaque Start() : l'ancien run n'efface pas les surcharges du nouveau
    private long _fightEndSeqSeen;        // Seq du dernier bilan de combat déjà livré à onFightEnd

    /// <summary>Journal du script (chaque fait et geste + statut). L'UI s'y abonne (et marshale sur le thread UI elle-meme).</summary>
    public event Action<LuaLogEntry>? Log;
    /// <summary>Émis après la fin effective de la tâche, y compris erreur ou annulation.</summary>
    public event Action? RunFinished;
    /// <summary>Nouveau script principal, après la sortie du run précédent ; permet d'oublier son ancien suivi de parcours.</summary>
    public event Action? MainScriptStarting;

    private void TrackRun(Task<LuaRunOutcome> task)
    {
        _runTask = task;
        _ = task.ContinueWith(_ => RunFinished?.Invoke(), CancellationToken.None,
            TaskContinuationOptions.None, TaskScheduler.Default);
    }

    // Un seul run touche les champs de cet hôte à la fois. Le remplacement annule
    // immédiatement l'ancien, puis attend sa sortie (stopped inclus) sans bloquer l'UI.
    private readonly object _runGate = new();
    private void QueueRun(Action<CancellationTokenSource> body, string reason = "restart")
    {
        lock (_runGate)
        {
            var previous = _runTask;
            Stop(reason);
            Volatile.Write(ref _stoppedByLinkLoss, false);
            var cts = new CancellationTokenSource();
            _cts = cts;
            TrackRun(Task.Run(async () =>
            {
                await LuaSnowCompatRuntime.RunAfterAsync(previous, cts.Token, () =>
                {
                    _runOutcome = LuaRunOutcome.Completed;
                    using var actions = _launch.ScriptArrivalScope(cts.Token);
                    LastTravelResult = null;
                    try { body(cts); }
                    finally { _launch.ClearScriptArrivalActions(_pid, cts.Token); }
                }).ConfigureAwait(false);
                return cts.IsCancellationRequested ? LuaRunOutcome.Stopped : _runOutcome;
            }));
        }
    }

    public bool Running => _runTask is { IsCompleted: false };

    /// <summary>Completion of the captured run, unaffected by a later replacement.</summary>
    public Task<LuaRunOutcome> RunCompletion => _runTask ?? Task.FromResult(LuaRunOutcome.Completed);

    public Task WaitForRunAsync(CancellationToken ct)
        => ((Task?)_runTask ?? Task.CompletedTask).WaitAsync(ct);

    /// <summary>Le run COURANT / DERNIER a-t-il été arrêté par la perte de la session de jeu (MITM, <see
    /// cref="LaunchService.GameLinkLosing"/>) ? Remis à faux à chaque démarrage. Sert à reprendre le script à la reconnexion
    /// même s'il ne tourne déjà plus quand l'UI traite la coupure.</summary>
    public bool StoppedByLinkLoss => Volatile.Read(ref _stoppedByLinkLoss);
    private bool _stoppedByLinkLoss;
    private readonly Action<uint, string> _onLinkLosing;

    public GivreLuaHost(uint pid, LaunchService launch, BotWorldStore world)
    {
        _pid = pid; _launch = launch; _world = world;
        // Lien de jeu MITM en train de tomber (thread réseau, SYNCHRONE, AVANT que les attentes du script ne soient rendues) :
        // le run en cours est arrêté « disconnected » tout de suite -> ses verbes voient l'annulation au lieu d'un échec et il
        // ne finit jamais « ended » sur la coupure. Rapide et sans Lua : un drapeau + l'annulation d'un CTS.
        _onLinkLosing = (p, _) => { if (p == _pid) OnGameLinkLosing(); };
        _launch.GameLinkLosing += _onLinkLosing;
        _onHarvest = (p, gid, qty, icon, name) => { if (p == _pid) { Interlocked.Increment(ref _harvestTick); Interlocked.Add(ref _lastHarvestQty, qty); Interlocked.Increment(ref _sHarvests); Interlocked.Add(ref _sResources, qty); } };
        _onUseEnded = (p, elem) => { if (p == _pid) Volatile.Write(ref _endedElement, elem); };
        _onUseFailed = (p, elem) => { if (p == _pid) Volatile.Write(ref _errorElement, elem); };
        _launch.ResourceHarvested += _onHarvest;
        _launch.InteractiveUseEnded += _onUseEnded;
        _launch.InteractiveUseFailed += _onUseFailed;
        _onUsedSelf = (p, elem, sk, dur) => { if (p == _pid) { Volatile.Write(ref _usedDuration, dur); Volatile.Write(ref _usedElement, elem); } };
        _launch.InteractiveUsedBySelf += _onUsedSelf;
        _onFightEnded = p => { if (p == _pid) { Interlocked.Increment(ref _sFights); Volatile.Write(ref _afterFight, 1); } };
        _onFightOutcome = (p, won, lost) => { if (p == _pid) { if (won) Interlocked.Increment(ref _sWon); else if (lost) Interlocked.Increment(ref _sLost); } };
        _launch.FightEnded += _onFightEnded;
        _launch.FightOutcome += _onFightOutcome;
        // Lot E : événements moteur journalisés dans la console du script (thread réseau : Emit est thread-safe, AUCUN Lua).
        _onFightLocks = OnFightLocksApplied;
        _onAutoRefused = OnRequestAutoRefused;
        _onModPopup = OnModeratorPopup;
        _launch.FightLocksApplied += _onFightLocks;
        _launch.RequestAutoRefused += _onAutoRefused;
        _launch.ModeratorPopupReceived += _onModPopup;
        SubscribeLotG();   // Lot G : hooks de paquets / MP du script (enfilage seulement, jamais de Lua hors thread script)
    }

    /// <summary>Règle les bornes (ms) du settle post-carte pour CE bot. Rapide (défaut) = 400/800 ; Legit = plage plus
    /// large (option Paramètres). À appeler AVANT Start() (l'hôte est réutilisé entre lancements -> config relue à chaque run).</summary>
    /// <summary>Pid du bot piloté par cet hôte.</summary>
    public uint Pid => _pid;

    public void ConfigureMovement(int settleMinMs, int settleMaxMs)
    {
        // borne défensive : min>=100, max>=min (une plage aberrante ne doit pas figer le bot ni casser Random.Next).
        _settleMinMs = System.Math.Max(100, settleMinMs);
        _settleMaxMs = System.Math.Max(_settleMinMs, settleMaxMs);
    }

    /// <summary>Nom du script dans les messages d'erreur (« recolte.lua:148 — … ») : nom du fichier chargé ou du perso.</summary>
    public string ChunkName { get; private set; } = "script.lua";

    // Point d'admission CENTRAL : les démarrages planning/équipe/supervision
    // peuvent appeler directement host.Start(), sans passer par le bouton Lua.
    // Un bot Socket avec une carte décodée mais sans injecteur ne doit jamais
    // apparaître comme exécutant un script capable de déplacer/combattre.
    private bool SocketControlAvailableForScript(bool requireFullGameplay)
    {
        if (!LaunchService.IsSocketPid(_pid) ||
            (requireFullGameplay ? _launch.HasFullSocketControl(_pid) : _launch.HasSocketInjector(_pid)))
            return true;
        Stop("socket-control-unavailable");
        Log?.Invoke(new LuaLogEntry(DateTime.Now, LuaLogLevel.Warning, "socket",
            _launch.IsReviewedSocketMovementOnly(_pid)
                ? "Script continu refusé : ce Socket inter-patch permet seulement un essai manuel de déplacement. " +
                  "Combat, récolte, expédition et automatisation exigent un protocole d'action validé."
                : "Script non lancé : canal de commandes Socket absent ou non prêt. " +
                  "Le personnage peut être visible en jeu sans pilotage actif."));
        return false;
    }

    /// <summary>Charge et lance un script (thread de fond). Remplace un script en cours (Stop d'abord). <paramref name="chunkName"/> :
    /// nom affiché dans les erreurs (défaut « script.lua »).</summary>
    public void Start(string scriptText, string? chunkName = null)
    {
        if (!SocketControlAvailableForScript(requireFullGameplay: true)) { LastTravelResult = null; return; }
        QueueRun(cts =>
        {
            // Toujours le nom de CE run (jamais celui du précédent : une erreur porterait le mauvais script).
            ChunkName = string.IsNullOrWhiteSpace(chunkName) ? "script.lua" : chunkName!;
            Volatile.Write(ref _stoppedByLinkLoss, false);   // nouveau run : pas (encore) arrêté par une coupure du lien de jeu
            Interlocked.Exchange(ref _modStopPending, 0);   // Lot E : un popup reçu AVANT ce run n'arrête pas le nouveau script
            _lastScriptText = scriptText;   // retenu pour Restart() (watchdog anti-stuck)
            if (_firstStartAt == 0) _firstStartAt = Environment.TickCount64;   // global:elapsedTime
            _offRouteFails = 0;             // nouveau run : pas d'échecs hors trajet hérités du run précédent
            _lockFails = 0;                 // Lot J : échecs de verrou (coffre / maison) du run précédent oubliés
            _world.Get(_pid).ClearCustomObstacles();   // Lot K : un nouveau run démarre sans obstacles personnalisés hérités
            _travelStuckMap = 0; _travelStuckCount = 0;   // ni d'échecs de voyage par cible (sinon une nouvelle plage s'arrêtait au 1er échec)
            _coordFallbackVisited.Clear();  // idem pour les zaaps de repli déjà visités
            _refusedZaaps.Clear();          // un nouveau run peut retenter une destination refusée
            _lastZaapDecisionLog = 0;       // et le diagnostic de décision zaap est réarmé
            _lastLongWalkLog = 0;             // idem pour le diagnostic de longue marche sans zaap
            ResetSessionCounters();         // stats: / getCountGather / afterFight : compteurs de CE run
            _maintDone = false;             // Lot D : maintenance pods (OPEN_BAGS/AUTO_DELETE) réarmée
            _launch.SetTradeAutoAcceptTeam(_pid, false);   // Lot D : nouveau run -> mode mule éteint (le script le réactive)
            int gen = Interlocked.Increment(ref _runGen);   // Lot C2 : génération du run (surcharges de combat script)
            MainScriptStarting?.Invoke();
            RunLoop(scriptText, cts, gen);
        }, "restart");
    }

    private string? _lastScriptText;   // source du DERNIER script lancé via Start() (pour le redémarrage anti-stuck)

    /// <summary>REDÉMARRE le dernier script lancé (Stop + Start de la même source). Utilisé par le watchdog anti-stuck comme
    /// cran de récupération opt-in. false si aucun script n'a jamais été démarré (ex : hôte Revive/RunOnce).</summary>
    public bool Restart()
    {
        if (_lastScriptText is not { } s) return false;
        Start(s, ChunkName);   // même source, même nom de script
        return true;
    }

    /// <summary>Exécute une CHAÎNE Lua UNE FOIS (commande console /cmd) sans exiger move()/bank() : Bind + DoString au
    /// top-level (les verbes bloquants fonctionnent ; l'annulation coopérative via Stop() les déroule). Thread de fond.</summary>
    public void RunOnce(string lua)
    {
        // The game transport still filters every one-shot frame by type in
        // reviewed cross-patch sessions (only movement/confirm accepted).
        if (!SocketControlAvailableForScript(requireFullGameplay: false)) return;
        QueueRun(cts => RunOnceCore(lua, cts.Token));
    }

    /// <summary>Pilote la RÉSURRECTION au Phénix EN CONTINU, SANS script Lua (thread de fond) : boucle HandleDeath tant que
    /// le perso est mort. Utilisé par le watcher AUTONOME (BotsViewModel.DriveAutoRevive) pour ressusciter même hors script.</summary>
    public void StartRevive()
    {
        if (!SocketControlAvailableForScript(requireFullGameplay: true)) return;
        QueueRun(cts => ReviveLoop(cts.Token));
    }

    private void ReviveLoop(CancellationToken ct)
    {
        GivreLauncher.Core.Transport.TransportLog.Scope.Value = _pid;   // les lignes vont dans la Console de CE bot
        if (_launch.LifeStatusOf(_pid) == 0) return;
        Warn("mort", Loc.T("L.Lua.Died"));
        while (!ct.IsCancellationRequested && _launch.LifeStatusOf(_pid) != 0)
        {
            try { HandleDeath(_launch.LifeStatusOf(_pid), ct); }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested || Root(ex) is ScriptStopException) break;
                Warn("mort", Loc.Fmt("L.Lua.TravelCustomError", Root(ex).Message));
                InterruptibleDelay(2000, ct);   // backoff : ne re-tente pas en rafale sur un échec (Phénix injoignable, etc.)
            }
        }
        if (!ct.IsCancellationRequested && _launch.LifeStatusOf(_pid) == 0) Ok("mort", Loc.T("L.Lua.Revived"));
    }

    private void RunOnceCore(string lua, CancellationToken ct)
    {
        GivreLauncher.Core.Transport.TransportLog.Scope.Value = _pid;
        try
        {
            // V17: /cmd executes arbitrary top-level verbs immediately after
            // Bind. A prior durable route/harvest attempt cannot be bypassed
            // by switching from a loop script to an imperative command.
            if (BlockQuarantinedLuaTopLevelV17(ct)) return;
            var script = new Script(CoreModules.Preset_SoftSandbox);   // même sandbox que RunLoop (pas d'io/os)
            _script = script;
            // Jeton propre à la commande, JAMAIS libéré ici : un combat:setAutoFight lancé en /cmd dure jusqu'au prochain
            // démarrage de script de ce bot (RunLoop le remet à zéro).
            _runToken = new object();
            Bind(script, ct);              // verbes liés AVANT DoString
            CallLua(script, script.LoadString(lua, null, "cmd"), ct);   // exécute la commande (peut appeler des verbes bloquants)
            RestoreBankModule(script);     // Lot D : une commande qui déclare function bank() ne masque plus le module bank: —
                                           // AVANT messagesRegistering() (qui voit donc la table, comme dans RunLoop)
            AfterTopLevel(script, ct);     // Lot G : onPrivateMessage + messagesRegistering (parité RunLoop)
            if (!ct.IsCancellationRequested) Ok("cmd", "Commande terminée.");
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested || Root(ex) is ScriptStopException) { _runOutcome = LuaRunOutcome.Stopped; Warn("cmd", Loc.T("L.Lua.ScriptStopped")); }
            else { _runOutcome = LuaRunOutcome.Failed; ScriptErr("cmd", ex, d => Loc.Fmt("L.Lua.ScriptLoadError", d)); }
        }
        finally { RetireRuntime(ct); }   // Lot G : plus rien n'est enfilé pour cette commande
    }

    /// <summary>Demande l'arret cooperatif : le prochain point d'annulation d'un verbe leve et deroule le script.
    /// Raison transmise au callback Lua stopped(reason) : "user" (bouton Arrêter, arrêt d'équipe).</summary>
    public void Stop() => Stop("user");

    /// <summary>Arrêt coopératif avec sa RAISON (callback Lua stopped(reason) : user, script, stuck, planning, remote, team,
    /// levelcap, license, moderator, restart, shutdown, disconnected (session de jeu perdue, bot gardé : MITM avec Dofus.exe
    /// ouvert ou relancé par « Reconnecter », session socket parquée par le serveur), socket (connexion socket perdue ou coupée
    /// par « Reconnecter » : script suspendu, repris après la reconnexion)). La première raison posée pour un run gagne.
    /// Thread-safe : aussi appelé depuis le thread réseau (coupure du lien de jeu).</summary>
    public void Stop(string reason)
    {
        if (reason is "user" or "supervisor" or "remote") _launch.CancelAutomaticStatUpgrade(_pid);
        var c = Volatile.Read(ref _cts);
        if (c is not null) CancelRun(c, reason);
    }

    // Un verbe d'un ancien run (notamment stopped()) ne doit pas annuler le suivant,
    // déjà en attente dans QueueRun. L'annulation vise la CTS capturée, jamais une autre.
    private void StopRun(CancellationToken ct, string reason)
    {
        var c = Volatile.Read(ref _cts);
        if (c is not null && c.Token == ct) CancelRun(c, reason);
    }

    private void CancelRun(CancellationTokenSource c, string reason)
    {
        lock (_stopReasons) { if (!_stopReasons.TryGetValue(c, out _)) _stopReasons.Add(c, reason); }
        _launch.CancelScriptArrivalActions(_pid, c);
        // N'efface _cts que s'il désigne encore CE run : un Stop concurrent d'un Start (thread réseau vs UI) ne doit pas
        // effacer le CTS du NOUVEAU run (il deviendrait inarrêtable).
        Interlocked.CompareExchange(ref _cts, null, c);
    }

    // Coupure du lien de jeu de CE pid (LaunchService.GameLinkLosing, thread réseau, sous le verrou du lien) : drapeau posé
    // AVANT l'annulation (l'UI qui lit StoppedByLinkLoss après la fin du run le voit), puis arrêt « disconnected ».
    // Le drapeau décrit TOUJOURS la dernière coupure : hôte déjà à l'arrêt (ou run déjà en cours d'arrêt pour une autre
    // raison) -> faux, jamais le reste d'une coupure précédente (reconnexion manuelle en jeu sans relancer le script).
    private void OnGameLinkLosing()
    {
        var c = Volatile.Read(ref _cts);
        bool live = Running && c is not null && !c.IsCancellationRequested;
        Volatile.Write(ref _stoppedByLinkLoss, live);
        if (live) Stop("disconnected");
    }

    // Raison d'arrêt par run (clé = CTS du run, faible : aucune fuite pour les runs terminés sans lecture).
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<CancellationTokenSource, string> _stopReasons = new();
    private string ReasonOf(CancellationTokenSource cts)
    {
        lock (_stopReasons) return _stopReasons.TryGetValue(cts, out var r) ? r : "user";
    }

    // Raison de fin d'un run ANNULÉ. Filet : lien de jeu coupé (MITM) -> toujours "disconnected", même si un arrêt concurrent
    // a posé sa raison le premier (ex. « stuck » sur un échec causé par la coupure juste avant sa détection).
    private string CancelReasonOf(CancellationTokenSource cts) => _launch.IsLinkDown(_pid) ? "disconnected" : ReasonOf(cts);

    public void Dispose()
    {
        Stop("shutdown");
        _launch.GameLinkLosing -= _onLinkLosing;
        _launch.FightLocksApplied -= _onFightLocks;
        _launch.RequestAutoRefused -= _onAutoRefused;
        _launch.ModeratorPopupReceived -= _onModPopup;
        _launch.ResourceHarvested -= _onHarvest;
        _launch.InteractiveUseEnded -= _onUseEnded;
        _launch.InteractiveUseFailed -= _onUseFailed;
        _launch.InteractiveUsedBySelf -= _onUsedSelf;
        _launch.FightEnded -= _onFightEnded;
        _launch.FightOutcome -= _onFightOutcome;
        UnsubscribeLotG();
        lock (_fileLock) { try { _file?.Dispose(); } catch { } _file = null; }
    }

    // ------------------------------------------------------------------ boucle de pilotage
    private void RunLoop(string scriptText, CancellationTokenSource cts, int gen)
    {
        var ct = cts.Token;
        // Pid ambiant : les lignes émises par le script/les verbes de CE bot vont dans SA Console (par-bot).
        GivreLauncher.Core.Transport.TransportLog.Scope.Value = _pid;
        // Lot C2 : réglages de combat du script PRÉCÉDENT (ou d'un /cmd) effacés AVANT DoString -> les setters appelés au
        // top-level par CE script s'appliquent. Sans setter : aucune surcharge -> rien ne change.
        _launch.ClearScriptCombatOverrides(_pid);
        // Lot E : politique du script (FIGHT_LOCK_* / IGNORE_REQUEST_*) propre à CE run (RunId) ; effacée à la sortie.
        long runId = Interlocked.Increment(ref s_runSeq);
        Interlocked.Exchange(ref _loopRunId, runId);
        (Script? Script, string? Reason) end = (null, null);
        try { end = RunLoopGuarded(scriptText, cts, gen, runId); }
        finally
        {
            _launch.ClearScriptPolicy(_pid, runId);
            RetireRuntime(ct);   // Lot G : hooks de paquets / MP de CE run désarmés
            if (Interlocked.CompareExchange(ref _loopRunId, 0, runId) == runId) _modWebhook = null;
        }
        // Lot D : le mode mule (trade:autoAcceptTeam) vit le temps du script. Seulement si aucun run plus récent n'a démarré
        // (l'ancien run arrêté n'éteint pas le mode du nouveau).
        if (Volatile.Read(ref _runGen) == gen) _launch.SetTradeAutoAcceptTeam(_pid, false);
        // Lot E : callback stopped(reason), UNE fois, APRÈS que le moteur a repris la main (combat/hooks libérés).
        if (end.Script is not null && end.Reason is not null) InvokeStopped(end.Script, end.Reason, cts);
    }

    private (Script? Script, string? Reason) RunLoopGuarded(string scriptText, CancellationTokenSource cts, int gen, long runId)
    {
        var ct = cts.Token;
        // Jeton du run : possède les hooks de combat et l'override d'auto-combat (combat:setAutoFight). Remis à zéro AVANT
        // DoString -> un setAutoFight appelé au top-level survit ; libéré dans le finally (le moteur reprend la main).
        var runToken = new object();
        _runToken = runToken;
        _turnFn = null; _placeFn = null; _inHook = false; _hookEp = 0; _listenerOnly = false;
        _snowCmFn = null; _snowCmPending = false;   // compat tiers: challengeManagement du run précédent oublié
        _launch.SetAutoFightOverride(_pid, runToken, null);
        try
        {
            return RunLoopCore(scriptText, runToken, cts, runId);
        }
        finally
        {
            // Lot C2 : réglages du script effacés AVANT de rendre la main au moteur (ReleaseScriptCombat relance RunPreFight /
            // le tour, qui clonent le profil tout de suite) -> la reprise suit le profil de l'onglet Combat. Seulement si aucun
            // run plus récent n'a démarré (l'ancien run arrêté n'efface pas les réglages du nouveau).
            if (Volatile.Read(ref _runGen) == gen) _launch.ClearScriptCombatOverrides(_pid);
            _launch.ReleaseScriptCombat(_pid, runToken);
            if (ReferenceEquals(_runToken, runToken)) { _turnFn = null; _placeFn = null; _inHook = false; _hookEp = 0; _listenerOnly = false; _snowCmFn = null; _snowCmPending = false; }
        }
    }

    // Renvoie (script, raison de fin) pour le callback stopped(reason) ; (null, null) = pas de callback (erreur de chargement,
    // script sans move ni hook, arrêt pendant le top-level).
    private (Script? Script, string? Reason) RunLoopCore(string scriptText, object runToken, CancellationTokenSource cts, long runId)
    {
        var ct = cts.Token;
        Script? script = null;
        DynValue bankFn;
        try
        {
            // Bind exposes travelTo/changeMap/craftItem before Lua DoString.
            // The legacy per-tick recovery check runs too late for those
            // imperative top-level calls. Preflight the archived marker here.
            if (BlockQuarantinedLuaTopLevelV17(ct)) return (null, null);
            script = new Script(CoreModules.Preset_SoftSandbox);   // pas d'acces io/os depuis un script
            _script = script;
            Bind(script, ct);              // verbes lies AVANT DoString
            CallLua(script, script.LoadString(scriptText, null, ChunkName), ct);   // top-level : DEFINIT move()/bank() + peut deja appeler les verbes (ils ne valent plus nil)
            // Lot D : bank() du script capturée PUIS le module bank: remis en global (bank() reste appelable via __call) —
            // DÈS la fin du top-level, AVANT messagesRegistering() : lui aussi voit la table (bank:open… utilisable). Seul le
            // code top-level exécuté APRÈS la déclaration de function bank() voit encore la fonction (inhérent à Lua).
            bankFn = RestoreBankModule(script);
            AfterTopLevel(script, ct);     // Lot G: onPrivateMessage + messagesRegistering
        }
        catch (Exception ex)
        {
            // Le top-level peut desormais appeler des verbes bloquants (travelTo/craftItem/...) : un stop/annulation
            // pendant DoString n'est PAS une erreur de chargement, on l'annonce comme un arret propre.
            if (ct.IsCancellationRequested || Root(ex) is ScriptStopException) { _runOutcome = LuaRunOutcome.Stopped; Warn("script", Loc.T("L.Lua.ScriptStopped")); }
            else { _runOutcome = LuaRunOutcome.Failed; ScriptErr("script", ex, d => Loc.Fmt("L.Lua.ScriptLoadError", d)); }
            return (null, null);
        }

        // Lot C2 : un combat terminé AVANT le démarrage du script ne déclenche pas onFightEnd.
        _fightEndSeqSeen = _launch.LastFightResult(_pid)?.Seq ?? 0;
        DynValue moveFn = script.Globals.Get("move");
        // Explicit V9 table-only recovery mode: the script supplies a
        // declarative route, rather than executing an unrestricted move()/
        // bank() callback on every tick. This does NOT sandbox code invoked
        // at top level; scripts must still be trusted at load time.
        bool strictRecoveryTable = ReadBool(script, "ACTIVITY_RECOVERY_TABLE_ONLY", false);
        DynValue recoveryRoute = script.Globals.Get("ACTIVITY_RECOVERY_ROUTE");
        if (strictRecoveryTable &&
            (!ReadBool(script, "ACTIVITY_RECOVERY", false) ||
             recoveryRoute.Type != DataType.Table ||
             !recoveryRoute.Table.Values.Any(v => v.Type == DataType.Table)))
        {
            _runOutcome = LuaRunOutcome.Failed;
            Err("trajet", "ACTIVITY_RECOVERY_TABLE_ONLY exige ACTIVITY_RECOVERY=true " +
                "et ACTIVITY_RECOVERY_ROUTE={ { map=..., activityRecovery=true, ... } }. " +
                "Aucune action hors combat ne sera émise.");
            return (null, null);
        }
        // (bankFn : capturée juste après DoString, voir plus haut ; appelée par la boucle quand les pods sont pleins.)
        // Hooks de combat (globals): onTurn prime sur fightManagement; onPlacement optionnel.
        DynValue onTurn = script.Globals.Get("onTurn"), fightMgmt = script.Globals.Get("fightManagement"), onPlace = script.Globals.Get("onPlacement");
        if (onTurn.Type == DataType.Function) { _turnFn = onTurn; _turnFnName = "onTurn"; }
        else if (fightMgmt.Type == DataType.Function) { _turnFn = fightMgmt; _turnFnName = "fightManagement"; }
        else _turnFn = null;
        _placeFn = onPlace.Type == DataType.Function ? onPlace : null;
        _placeFn ??= BuildSnowPlacementHook(script, ct);   // compat tiers: prefightManagement / challengeManagement (notifications, le moteur place)
        ReadSnowRunGlobals(script);                    // compat tiers: GIVRE_TIMEOUT, PLANNING / FIGHT_KICK réarmés
        bool hasMove = moveFn.Type == DataType.Function;
        bool hasHooks = _turnFn is not null || _placeFn is not null;
        bool hasPacketHooks = PacketHooksActive(ct);   // Lot G : registerMessage / onPrivateMessage posés au top-level
        if (!hasMove && !strictRecoveryTable && !hasHooks && !hasPacketHooks) { _runOutcome = LuaRunOutcome.Failed; Err("script", Loc.T("L.Lua.ScriptNeedsMove")); return (null, null); }
        if (!hasMove && !strictRecoveryTable) Info("script", Loc.T(hasHooks ? "L.Lua.HooksOnlyNoMove" : "L.Lua.PacketHooksOnlyNoMove"));
        // Lot G : script « écouteur » (hooks de paquets / MP seulement) = inactif PAR CONCEPTION -> l'anti-stuck ne le juge pas figé.
        _listenerOnly = !hasMove && !strictRecoveryTable && !hasHooks && hasPacketHooks;
        _turnBudgetMs = Math.Clamp(ReadInt(script, "TURN_TIME_LIMIT", 20000), 3000, 25000);
        if (hasHooks)
        {
            _launch.SetScriptFightHooks(_pid, runToken, _turnFn is not null, _placeFn is not null);
            if (!LicenseService.HasPermission(LicenseService.Perm.Fight)) Warn("combat", Loc.T("L.Lua.NoPermFight"));
            // Script: son bloc fightManagement REMPLACE désormais l'auto-combat du profil -> on le dit.
            if (_turnFn is not null && _turnFnName == "fightManagement" && _launch.ProfileAutoFight(_pid))
                Info("combat", Loc.T("L.Lua.FightMgmtReplacesEngine"));
        }
        // Lot C2 : setChallengeMode("script") sans onPlacement = aucun code du script ne tourne pendant le placement -> aucun
        // défi ne sera choisi (le moteur traite alors ce mode comme « off »). On le signale une fois.
        if (_placeFn is null && _launch.ScriptChallengeModeIsScript(_pid))
            Warn("combat", Loc.Fmt("L.Lua.ChallengeScriptNoPlacement", "combat:setChallengeMode(\"script\")"));

        int maxPods = ReadInt(script, "MAX_PODS", 95);
        Ok("script", Loc.T("L.Lua.ScriptStarted"));
        string endReason = "ended";
        try
        {
            bool bankMode = false;
            bool wasInFight = false;
            bool wasDead = false;
            PushPolicy(script, runId);   // Lot E : FIGHT_LOCK_* / IGNORE_REQUEST_* (globals posés au top-level)
            while (!ct.IsCancellationRequested)
            {
                // Lot E : politique + webhook modérateur relus à chaque tour (globals modifiables à chaud, ex. dans move()), puis
                // arrêt demandé par un popup modérateur (MODERATOR_STOP) — jamais au milieu d'un trajet. EN TÊTE, avant la
                // branche combat (le Lot C l'a aussi modifiée) : pris en compte même en combat.
                PushPolicy(script, runId);
                if (CurrentRuntime is { } lotG && lotG.Token == ct) RefreshPmHook(lotG);   // Lot G : onPrivateMessage défini/retiré à chaud
                _modWebhook = ReadStr(script, "MODERATOR_WEBHOOK");
                if (Interlocked.Exchange(ref _modStopPending, 0) == 1 && ReadBool(script, "MODERATOR_STOP", false))
                {
                    Warn("modération", Loc.T("L.Lua.ModeratorStop"));
                    StopRun(ct, "moderator");
                    ModeratorStopped?.Invoke();
                    ThrowIfStopping(ct);
                }
                if (strictRecoveryTable && _launch.CurrentMap(_pid) is not > 0)
                {
                    // Never call a Lua verb or default pods maintenance while
                    // the server identity/world snapshot is incomplete.
                    InterruptibleDelay(500, ct);
                    continue;
                }
                if (strictRecoveryTable && RecoveryBlocksRoute(
                        checkDurable: true, ownerToken: ct))
                {
                    // An outstanding irreversible route attempt cannot be
                    // rearmed by bank(), pods helpers or onFightEnd on the next
                    // scheduler tick. The recovery service will Stop/mark
                    // stuck if the pending marker requires operator review.
                    InterruptibleDelay(900, ct);
                    continue;
                }
                // V14 strict one-shot scripts cannot silently invoke the
                // legacy Phoenix auto-revive/travel path before RunRoute's
                // durable admission. Death requires an explicit human restart
                // and fresh goal/source validation, not an unjournaled move.
                if (strictRecoveryTable && _launch.LifeStatusOf(_pid) is int strictLife &&
                    strictLife != 0)
                {
                    Warn("mort", "ACTIVITY_RECOVERY_TABLE_ONLY : mort/fantôme ; " +
                        "phénix et déplacements automatiques interdits avant admission durable.");
                    Stop("stuck");
                    continue;
                }
                // MORT -> résurrection auto au Phénix: PRIORITAIRE sur récolte/combat/trajet
                // (tout est refusé tant qu'on est tombe/fantôme). Gaté par le profil (AutoRevive, défaut ON). Réutilise le
                // voyage (GoToMapId) et l'usage d'interactif (UseInteractive) de l'hôte -> un fantôme rejoint le Phénix à pied.
                // Compat un bot tiers: un script qui définit phenix pilote lui-même le fantôme jusqu'au Phénix (même si l'option
                // AutoRevive du profil est coupée) ; sans étape pour la carte courante, le moteur reprend la main.
                DynValue phenixFn = script.Globals.Get("phenix");
                bool scriptPhenix = phenixFn.Type == DataType.Function;
                if ((scriptPhenix || _launch.AutoReviveEnabled(_pid)) && _launch.LifeStatusOf(_pid) is int life && life != 0)
                {
                    if (!wasDead) { wasDead = true; Warn("mort", Loc.T("L.Lua.Died")); }
                    if (life == 2 && scriptPhenix && RunScriptPhenix(script, phenixFn, ct)) continue;
                    HandleDeath(life, ct);
                    continue;
                }
                if (wasDead) { wasDead = false; Ok("mort", Loc.T("L.Lua.Revived")); InterruptibleDelay(MapSettle(), ct); }

                // Combat (y compris AGGRO pendant la recolte) : joue par le moteur de combat (RunFightTurn). On met la
                // recolte EN PAUSE et on le SIGNALE dans la console; a la fin, on reprend apres un settle
                // (la carte se recharge + la self-cell doit etre re-confirmee avant de re-recolter).
                if (_launch.IsInFight(_pid))
                {
                    if (!wasInFight)
                    {
                        wasInFight = true;
                        Info("combat", _turnFn is not null && !_launch.IsAutoFightEffective(_pid)
                            ? Loc.Fmt("L.Lua.CombatDetectedScript", _turnFnName) : Loc.T("L.Lua.CombatDetected"));
                    }
                    SnowFightKickTick(script);   // compat tiers: FIGHT_KICK (placement), no-op sans le global
                    if (_turnFn is null && _placeFn is null) { InterruptibleDelay(1000, ct, pump: true); continue; }   // chemin d'origine (aucun hook de combat ; pompe des hooks de paquets seulement s'il y en a)
                    // Hooks : pompe serrée (~1 s au total) pour réclamer tour/placement dès qu'ils sont tendus.
                    for (int i = 0; i < 25 && _launch.IsInFight(_pid); i++)
                    {
                        PumpFightHooks(ct);
                        PumpHooks(ct);   // Lot G : hooks de paquets entre deux tours (no-op sans hook)
                        if (ct.WaitHandle.WaitOne(40)) throw new ScriptStopException();
                    }
                    continue;
                }
                if (wasInFight)
                {
                    wasInFight = false;
                    Ok("combat", Loc.T("L.Lua.CombatEndedResume"));
                    InterruptibleDelay(PostFightSettle(), ct);
                }
                // FightEnd precedes the roleplay snapshot. Never run move()/bank()/onFightEnd on the combat instance.
                if (_launch.IsPostFightMapPending(_pid))
                {
                    WaitForPostFightRoleplay(ct);
                    continue;
                }
                if (strictRecoveryTable)
                {
                    // Unlike the historical per-tick script callback below,
                    // this mode never invokes move(), bank(), onFightEnd or
                    // pods-maintenance callbacks before RunRoute's durable
                    // admission. When pods are full, pausing is preferable
                    // to an unjournaled automatic multi-send bank deposit.
                    int strictPods = _world.Get(_pid).PodsPercent;
                    if (strictPods >= maxPods)
                    {
                        Warn("banque", "ACTIVITY_RECOVERY_TABLE_ONLY : pods au seuil ; " +
                            "banque imperative non certifiee, trajet suspendu sans emission.");
                        Stop("stuck");
                        continue;
                    }
                    RunRoute(recoveryRoute.Table, ct);
                    InterruptibleDelay(_launch.IsTurboAckAll(_pid) ? 15 : 200,
                        ct, pump: true);
                    continue;
                }
                // V13: legacy move()/bank() and pods-maintenance run BEFORE
                // RunRoute. A persistent pending ActivityRecovery segment from
                // an earlier run must therefore be checked HERE as well: a
                // script restarted with imperative bank() cannot evade the
                // route's durable admission check. Clear legacy scripts keep
                // their original callbacks; an unknown opted-in identity or
                // quarantined checkpoint is never treated as a server ACK.
                var beforeImperative = ReadDurableRecoveryStatus();
                if ((ActivityRecoveryGlobalEnabled ||
                     (beforeImperative.Scope.IsValid && beforeImperative.IsQuarantined) ||
                     _activityRecovery is { IsQuarantined: true }) &&
                    RecoveryBlocksRoute(checkDurable: true, ownerToken: ct))
                {
                    InterruptibleDelay(900, ct);
                    continue;
                }
                DispatchFightEnd(script, ct);   // Lot C2 : hook onFightEnd(result), carte posée (peut se déplacer / combattre)
                SnowPlanningTick(script);   // compat tiers: PLANNING = heures de déconnexion (chef ou solo)
                if (!hasMove) { InterruptibleDelay(500, ct, pump: true); continue; }   // script « combat seul » : rien à faire hors combat
                int podsPct = _world.Get(_pid).PodsPercent;
                bool full = podsPct >= maxPods;
                // Lot D: pods pleins -> OPEN_BAGS / AUTO_DELETE AVANT bank, même sans bank.
                // Rejoué au plus toutes les 20 s tant que plein ; réarmé dès que les pods redescendent. Globals absents = no-op.
                if (full && (!_maintDone || Environment.TickCount64 - _maintAt > 20000))
                {
                    _maintDone = true; _maintAt = Environment.TickCount64;
                    if (RunPodsMaintenance(script, podsPct, ct))
                    {
                        InterruptibleDelay(900, ct);
                        podsPct = _world.Get(_pid).PodsPercent;
                        full = podsPct >= maxPods;
                    }
                }
                else if (!full) _maintDone = false;
                DynValue fn;
                if (full && bankFn.Type == DataType.Function)
                {
                    fn = bankFn;
                    if (!bankMode)   // annonce UNE fois pourquoi on ne recolte plus (evite la confusion avec un bug de trajet)
                    {
                        bankMode = true;
                        Warn("banque", Loc.Fmt("L.Lua.PodsFullBank", podsPct, maxPods));
                    }
                }
                else
                {
                    if (full) Warn("script", Loc.Fmt("L.Lua.PodsFullNoBank", podsPct));
                    else if (bankMode) { bankMode = false; Ok("script", Loc.Fmt("L.Lua.PodsDownResume", podsPct)); }
                    fn = moveFn;
                }
                DynValue result = CallLua(script, fn, ct);
                if (result.Type == DataType.Table) RunRoute(result.Table, ct);                // script DECLARATIF
                else if (result.Type == DataType.Boolean && result.Boolean == false) break;   // script IMPERATIF : return false -> stop
                int loopDelay = bankMode ? 1500 : (_launch?.IsTurboAckAll(_pid) == true ? 15 : 200);
                InterruptibleDelay(loopDelay, ct, pump: true);                              // en attente de banque : pas de spin serre
            }
            endReason = ct.IsCancellationRequested ? CancelReasonOf(cts) : "ended";   // return false -> "ended"
            Ok("script", Loc.T("L.Lua.ScriptEnded"));
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested || Root(ex) is ScriptStopException) { _runOutcome = LuaRunOutcome.Stopped; endReason = CancelReasonOf(cts); Warn("script", Loc.T("L.Lua.ScriptStopped")); }
            else { _runOutcome = LuaRunOutcome.Failed; endReason = "error"; ScriptErr("script", ex, d => Loc.Fmt("L.Lua.ScriptRuntimeError", d)); }
        }
        return (script, endReason);
    }

    /// <summary>Only the real checksum-verified ActivityRecovery scope can
    /// revoke execution; never infer a clear or success from local inventory.
    /// This blocks imperative Lua before any user-defined callback can send.
    /// Human read-only inspection remains available in the existing panel.</summary>
    private bool BlockQuarantinedLuaTopLevelV17(CancellationToken ct)
    {
        var marker = ReadDurableRecoveryStatus();
        if (!marker.Scope.IsValid || !marker.IsQuarantined) return false;
        RecoveryBlocksRoute(checkDurable: true, ownerToken: ct);
        _runOutcome = LuaRunOutcome.Stopped;
        Warn("trajet", "V17 : Lua top-level bloqué par ActivityRecovery " +
            marker.State + " ; tentative antérieure incertaine. " +
            "Inspecter le journal durable sans relancer ni effacer le marqueur.");
        return true;
    }

    // ------------------------------------------------------------------ helpers d'attente (cooperatifs)
    // pump=true (Lot G) : UNIQUEMENT aux points sûrs (verbe delay, boucle RunLoop). Hooks de paquets servis pendant l'attente
    // SEULEMENT si le run en a (sinon chemin d'origine inchangé) et jamais DANS onTurn (timing du tour prévisible).
    private void InterruptibleDelay(int ms, CancellationToken ct, bool pump = false)
    {
        ThrowIfStopping(ct);
        bool pk = false;
        if (pump && HookEpoch == 0 && CurrentRuntime is { } rt && rt.Token == ct)
        {
            RefreshPmHook(rt);   // onPrivateMessage défini/retiré (aussi pour un /cmd qui boucle sur delay)
            pk = rt.HasWork;
            if (pk) PumpHooks(ct);
        }
        if (ms <= 0) return;
        ms = Jitter(ms);   // anti-detection : humanise le delai (+/-15%)
        if (pk)
        {
            // Hooks de paquets actifs : même durée totale, découpée en tranches <= 40 ms entre lesquelles on pompe les hooks
            // de paquets (et ceux de combat s'ils sont actifs, comme la branche ci-dessous).
            long pEnd = Environment.TickCount64 + ms;
            while (true)
            {
                // Jamais DANS un hook de paquet (delay() appelé par le hook) : onTurn y tournerait imbriqué, avec waitFor refusé.
                if (HooksActive && CurrentRuntime?.InPump != true) PumpFightHooks(ct);
                long left = pEnd - Environment.TickCount64;
                if (left <= 0) return;
                if (ct.WaitHandle.WaitOne((int)Math.Min(40, left))) throw new ScriptStopException();
                PumpHooks(ct);
            }
        }
        if (HookEpoch != 0)
        {
            // DANS onTurn : délai découpé ; dès que le tour réclamé est PERDU (budget écoulé, tour suivant, fin de combat)
            // le hook est déroulé (HookExpiredException) -> le tour suivant peut être réclamé normalement.
            ThrowIfHookTurnGone();
            long hEnd = Environment.TickCount64 + ms;
            while (true)
            {
                long left = hEnd - Environment.TickCount64;
                if (left <= 0) return;
                if (ct.WaitHandle.WaitOne((int)Math.Min(40, left))) throw new ScriptStopException();
                ThrowIfHookTurnGone();
            }
        }
        if (!HooksActive)
        {
            if (ct.WaitHandle.WaitOne(ms)) throw new ScriptStopException();   // reveille immediatement si annule (chemin d'origine)
            return;
        }
        // Hooks de combat actifs : même durée totale, découpée en tranches <= 40 ms entre lesquelles on pompe les hooks
        // (un tour tendu pendant un delay() du script est réclamé sans attendre la fin du délai).
        long end = Environment.TickCount64 + ms;
        while (true)
        {
            PumpFightHooks(ct);
            long left = end - Environment.TickCount64;
            if (left <= 0) return;
            if (ct.WaitHandle.WaitOne((int)Math.Min(40, left))) throw new ScriptStopException();
        }
    }

    // Attend qu'une condition devienne vraie (polling 80 ms, ou 10 ms en turbo), true si atteinte avant timeout. Leve si annule.
    // pump=true (Lot G) : seulement l'attente de fin de combat de fight() (script au repos) ; no-op sans hook de paquets.
    private bool WaitUntil(Func<bool> cond, int timeoutMs, CancellationToken ct, bool pump = false)
    {
        long end = Environment.TickCount64 + Math.Max(0, timeoutMs);
        int pollMs = _launch?.IsTurboAckAll(_pid) == true ? 10 : 80;
        while (true)
        {
            ThrowIfStopping(ct);
            ThrowIfHookTurnGone();
            if (HooksActive) PumpFightHooks(ct);
            if (pump && HookEpoch == 0) PumpHooks(ct);
            ThrowIfStopping(ct);
            if (cond()) return true;
            long left = end - Environment.TickCount64;
            if (left <= 0) return false;
            if (ct.WaitHandle.WaitOne((int)Math.Min(pollMs, left))) throw new ScriptStopException();
        }
    }

    private static void ThrowIfStopping(CancellationToken ct) { if (ct.IsCancellationRequested) throw new ScriptStopException(); }

    // Attend que la CELLULE du perso soit CONFIRMEE pour la carte courante (jss/MapComplementaryInformationEvent traite,
    // self matche par nom).: on ne calcule JAMAIS un deplacement sur une self-cell anterieure au
    // traitement des donnees de la nouvelle carte. Remplace le settle fixe de 700 ms (plus court que la latence ~1 s du jss).
    // An unknown/optimistic position must never be treated as a confirmed start cell.
    private bool WaitSelfReady(CancellationToken ct)
    {
        long map = _launch.CurrentMap(_pid) ?? 0;
        if (map == 0) return false;
        bool self = WaitUntil(() => _world.Get(_pid).SelfReadyFor(map), SelfReadyTimeoutMs, ct);
        // Attend AUSSI que la geometrie de cellules de CETTE carte soit chargee (bundle async) : sinon le pathfinding qui
        // suit tournerait sur une carte perimee -> marche refusee. C'est ce gate evenementiel qui remplace le gros settle
        // fixe. A timeout fails the current operation safely instead of letting a
        // subsequent send depend on the old map geometry or a predicted cell.
        bool cells = WaitUntil(() => _world.Get(_pid).CellsReadyFor(map), CellsReadyTimeoutMs, ct);
        if (!self || !cells || _launch.CurrentMap(_pid) != map || !_world.Get(_pid).SelfReadyFor(map))
            throw new InvalidOperationException($"Position ou geometrie de carte non confirmee (map={map}); action annulee.");
        return true;
    }

    // ------------------------------------------------------------------ lecture des globals de config du script
    private static int ReadInt(Script s, string key, int fallback)
    {
        var v = s.Globals.Get(key);
        return v.Type == DataType.Number ? (int)v.Number : fallback;
    }

    // Lit une table Lua de monstres (par NOM string ou GENERIC_ID number) -> deux ensembles. Table absente -> vides.
    private static (HashSet<string> Names, HashSet<int> Ids) ReadMonsterSet(Script s, string key)
    {
        var names = new HashSet<string>();
        var ids = new HashSet<int>();
        var v = s.Globals.Get(key);
        if (v.Type == DataType.Table)
            foreach (var item in v.Table.Values)
            {
                if (item.Type == DataType.Number) ids.Add((int)item.Number);
                else if (item.Type == DataType.String) names.Add(item.String.ToLowerInvariant());
            }
        return (names, ids);
    }

    // Direction cardinale SIMPLE (sans cellule forcée). Les appelants qui acceptent « top(123) » appellent directement
    // LuaCompat.TryParseDirToken (mêmes synonymes : top/haut/north/nord…).
    private static bool TryParseDir(string dir, out MapDir d)
        => LuaCompat.TryParseDirToken(dir, out d, out int c) && c < 0;

    // Nom canonique d'une direction (jeton du DSL : "top"/"bottom"/"left"/"right").
    private static string DirName(MapDir d) => d switch
    {
        MapDir.Top => "top", MapDir.Bottom => "bottom", MapDir.Left => "left", _ => "right",
    };

    // Deballe la chaine d'InnerException (MoonSharp enveloppe parfois l'exception CLR d'un callback).
    private static Exception Root(Exception ex)
    {
        while (ex.InnerException is not null) ex = ex.InnerException;
        return ex;
    }

    // Marqueur d'arret cooperatif : leve par les verbes quand le CancellationToken est annule ; deroule proprement le script.
    private sealed class ScriptStopException : Exception { }

    // Levée DANS onTurn (delay/attentes) quand le tour réclamé est perdu (budget écoulé, tour suivant, fin de combat) :
    // déroule le hook sans erreur ; attrapée par RunTurnHook uniquement.
    private sealed class HookExpiredException : Exception { }
}
