using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Controls;
using GivreClone.App.Services;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

/// <summary>Une ligne de la console Lua : horodatage, categorie, message, couleur du message (niveau OU hex libre),
/// couleur de la pastille de gravite (toujours le niveau), graisse (gras &lt;b&gt;), et niveau (pour le filtre).</summary>
public sealed record LuaLogRow(string Time, string Category, string Message, IBrush Brush, IBrush DotBrush, FontWeight Weight, LuaLogLevel Level, int Pid,
                               GivreClone.Engine.LuaTooling.LuaErrorLocation? Location = null)
{
    /// <summary>Ligne d'erreur située dans un script (clic -> éditeur à la ligne).</summary>
    public bool HasLocation => Location is not null;
    public string LocationLabel => Location is { } l ? "L" + l.Line : "";
    public string LocationTip => Location is { } l ? l.Chunk + ":" + l.Line + (l.Column > 0 ? ":" + (l.Column + 1) : "") : "";
}

/// <summary>Ligne de la console ÉQUIPE commune : horodatage + PERSO (ou « équipe » pour l'orchestration) + catégorie + message.</summary>
public sealed record TeamLogRow(string Time, string Bot, string Category, string Message, IBrush Brush, LuaLogLevel Level);

/// <summary>
/// Onglet Bots : reunit les comptes EN JEU (running + pid) en "bots", avec le detail du bot selectionne
/// (etat live + console des paquets decodes en direct). Consomme le flux partage Stores.Launch.PacketDecoded
/// et le route vers le bon bot par PID. Le pilotage reel (actions/scripts) viendra avec l'injection.
/// </summary>
public partial class BotsViewModel : ViewModelBase
{
    private readonly AccountsViewModel _accounts;
    private readonly DispatcherTimer _timer;
    // Lu depuis les threads du relais (OnPacket), muté sur le thread UI (Resync, re-clé « Reconnecter » MITM) -> concurrent.
    private readonly ConcurrentDictionary<int, BotRowViewModel> _byPid = new();
    private readonly HashSet<int> _bottingPids = new();   // bots dont une session de botting est en cours (pour start/stop)
    // Increments on EVERY selection change, including A -> B -> A: a context
    // menu opened in the former selection must never be revived.
    private long _mapAttackSelectionGeneration = 1;
    private int _statsTick;                                // throttle du rafraichissement de l'onglet Stats (~1,5 s)
    private readonly Dictionary<string, ZaapRoute> _zaapRoutePushed = new(StringComparer.Ordinal);

    public ObservableCollection<BotRowViewModel> Bots { get; } = new();

    /// <summary>Journal GLOBAL des archimonstres repérés (partagé, tous bots) : source du panneau « Archimonstres récents »
    /// de l'onglet Carte. Alimenté par AccountsViewModel sur détection (LaunchService.ArchMonsterSighted).</summary>
    public ObservableCollection<ArchSightingRow> ArchSightings => ArchMonsterLog.Recent;
    public bool HasArchSightings => ArchMonsterLog.Recent.Count > 0;

    /// <summary>VM de l'onglet Stats (par perso selectionne) : cartes KPI + feed + courbe de botting.</summary>
    public BotStatsViewModel Stats { get; } = new();
    public PlanningViewModel Planning { get; } = new();
    public BotSettingsViewModel Settings { get; } = new();   // onglet Paramètres : déplacement + messagerie (par perso)
    public TreasureViewModel Treasure { get; } = new();
    public SmithMagicViewModel SmithMagic { get; } = new();
    public BakViewModel Bak { get; } = new();
    /// <summary>Onglet HDV du bot SÉLECTIONNÉ. Un VM PAR BOT (<see cref="_hdvByBot"/>) : le repricing automatique, une mise en vente
    /// (même réduite) ou un voyage vers l'HDV continuent quand on sélectionne un autre bot ou qu'on change de page — seul l'arrêt
    /// manuel (ou la fermeture du bot) les stoppe. Avant : un VM unique, dont AttachPid annulait tout au changement de sélection.</summary>
    public HdvViewModel Hdv { get => _hdv ?? _hdvIdle; private set => _hdv = value; }
    private HdvViewModel? _hdv;
    private readonly HdvViewModel _hdvIdle = new();                              // aucun bot sélectionné
    private readonly Dictionary<BotRowViewModel, HdvViewModel> _hdvByBot = new();

    private HdvViewModel HdvFor(BotRowViewModel? bot)
    {
        if (bot is null) return _hdvIdle;
        if (!_hdvByBot.TryGetValue(bot, out var vm)) _hdvByBot[bot] = vm = new HdvViewModel();
        return vm;
    }

    private void SelectHdv(BotRowViewModel? bot)
    {
        var vm = HdvFor(bot);
        if (ReferenceEquals(vm, Hdv)) return;
        Hdv = vm;
        OnPropertyChanged(nameof(Hdv));
    }

    private void ForgetHdv(BotRowViewModel bot)
    {
        if (!_hdvByBot.Remove(bot, out var vm)) return;
        vm.AttachPid(0);   // pid change -> arrête repricing, mise en vente et voyage de CE bot
        if (ReferenceEquals(Hdv, vm)) { Hdv = _hdvIdle; OnPropertyChanged(nameof(Hdv)); }
    }

    /// <summary>Type de dépôt banque (onglet Banque), persisté globalement dans AppConfig : 0=tout, 1=déjà présents, 2=visibles.
    /// Reconstruit sur Loc.Changed (sinon figé dans la langue de démarrage). Les ComboBox bindent SelectedIndex -> l'index tient.</summary>
    [ObservableProperty] public partial string[] BankDepositModes { get; set; } = MakeBankDepositModes();
    private static string[] MakeBankDepositModes() =>
        new[] { Loc.T("L.Bots.Bank.DepAll"), Loc.T("L.Bots.Bank.DepExisting"), Loc.T("L.Bots.Bank.DepVisible") };

    /// <summary>Modes de tri des onglets Inventaire/Banque : 0=défaut, 1=Nom (A→Z), 2=Quantité (↓). Reconstruit sur Loc.Changed.</summary>
    [ObservableProperty] public partial string[] ItemSortModes { get; set; } = MakeItemSortModes();
    private static string[] MakeItemSortModes() =>
        new[] { Loc.T("L.Bots.Sort.Default"), Loc.T("L.Bots.Sort.Name"), Loc.T("L.Bots.Sort.Qty"), Loc.T("L.Bots.Sort.Category") };
    [ObservableProperty] public partial int BankDepositIndex { get; set; }
    partial void OnBankDepositIndexChanged(int value)
    {
        AppConfig.Current.BankDeposit = value switch { 1 => "existing", 2 => "visible", _ => "all" };
        AppConfig.Save();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(LevelCapBlocked))]
    [NotifyPropertyChangedFor(nameof(CanRunLua))]
    public partial BotRowViewModel? SelectedBot { get; set; }

    public bool HasBots => Bots.Count > 0;
    public string InGameCountText => Loc.Fmt("L.Bots.InGameCount", Bots.Count(b => !b.Row.IsDisconnected));   // compteur localisé ; un bot MITM « Déconnecté » (Dofus.exe ouvert, hors jeu) n'est pas compté
    public bool HasSelection => SelectedBot is not null;

    /// <summary>Skin un bot tiers active (fixée au démarrage) -> la vue Bots masque sa propre liste « Bots en jeu »
    /// et passe le détail en pleine largeur.</summary>
    public bool IsSnowSkin => Services.SkinService.IsSnow;

    /// <summary>Placement du bandeau d'onglets du détail bot: à GAUCHE
    /// sous la skin un bot tiers, en HAUT (défaut) sinon.</summary>
    public Avalonia.Controls.Dock BotTabPlacement => IsSnowSkin ? Avalonia.Controls.Dock.Left : Avalonia.Controls.Dock.Top;

    public BotsViewModel(AccountsViewModel accounts)
    {
        EnsureSessionLists();   // listes de persos observées avant l'ouverture du menu
        _accounts = accounts;
        EnsureLuaDocumentHooked();   // éditeur Lua : document partagé + analyse en arrière-plan
        Planning.GetCurrentScript = () => LuaScript;   // le planning fige le script courant a l'activation
        // ... jamais le MODÈLE d'exemple affiché à un perso sans script (le planning le lancerait tel quel).
        Planning.IsTemplateScript = IsSampleScript;
        OfflinePlanning.IsTemplateScript = IsSampleScript;
        Planning.GetCurrentAccountId = () => SelectedBot?.Row.Model.Id;   // « connexion auto » : compte qui porte le perso affiché
        Planning.Log = (m, l) => AppendLuaLine("planning", m, l);
        // Issue d'un push de script vers le panel (immédiat OU différé/rejoué) -> ligne dans la console du bot sélectionné.
        // AppendLuaLine marshalle déjà vers l'UI ; BotsViewModel est un singleton (créé une fois par MainViewModel) -> pas de désabonnement.
        // Modale « planning d'un perso hors ligne » : liée au compte DU perso, jamais à l'éditeur / au bot sélectionné.
        OfflinePlanning.GetCurrentAccountId = () => OfflinePlanningTarget?.AccountId;
        OfflinePlanning.Log = (m, l) => Toasts.Show(m, success: l != LuaLogLevel.Danger, sound: false);
        _accounts.UserStopped += OnAccountUserStopped;   // arrêt manuel -> le planning ne relance pas avant la fin de la plage
        _accounts.PlanningClaimsCrash = PlanningClaimsCrash;   // crash d'un compte connecté par le planning -> relancé par le planning
        // Fin de session socket (reconnexion auto / session parquée) -> script suspendu puis repris sur une NOUVELLE session.
        _accounts.SocketSessionLost += (pid, kind, seq) => SuspendSocketScript(pid, kind, seq);
        _accounts.PidRekeyed += OnPidRekeyed;                  // « Reconnecter » MITM en place : même entrée Bots sur le nouveau pid
        _accounts.ReconnectRequested = RequestReconnectAsync;  // « Reconnecter » d'une carte compte -> même flux que le bandeau Bots
        // MITM : session de jeu perdue, Dofus.exe ouvert (kick serveur…) -> script arrêté mais gardé pour la reconnexion (thread réseau -> UI).
        Stores.Launch.GameLinkLost += (pid, reason, _) => Dispatcher.UIThread.Post(() => OnGameLinkLost((int)pid, reason));
        // MITM : retour en jeu SANS « Reconnecter » (reconnexion à la main dans Dofus) -> plus de script « à reprendre » (thread réseau -> UI).
        Stores.Launch.GameLinkRestored += pid => Dispatcher.UIThread.Post(() => OnGameLinkRestored((int)pid));
        // Journal de connexion (proxy, IP, serveurs, bascule de proxy, perso) -> console Lua du bot (thread quelconque -> UI).
        ConnectionJournal.Logged += (acc, pid, msg, lvl) => Dispatcher.UIThread.Post(() => OnJournal(acc, (int)pid, msg, lvl));
        Stores.Launch.CharacterIdentified += (pid, info) => Dispatcher.UIThread.Post(() => OnJournalCharacter((int)pid, info));
        WireAnomalyAlerts();   // notifications Discord : anomalie ouverte / anomalie farmée fermée
        Stores.Launch.ServerIdentified += (pid, name) => Dispatcher.UIThread.Post(() => OnJournal(null, (int)pid, Loc.Fmt("L.Journal.Server", name), LuaLogLevel.Info));
        WireGameJournal();   // événements de jeu (niveau, combats, récoltes, pods, ventes, MP…) dans le journal du bot
        InitSupervisors();   // MZP23 : section « Superviseurs »
        BankDepositIndex =AppConfig.Current.BankDeposit switch { "existing" => 1, "visible" => 2, _ => 0 };
        // Re-localise les listes construites en code au changement de langue (sinon figées dans la langue de démarrage).
        Loc.Changed += () => { BankDepositModes = MakeBankDepositModes(); ItemSortModes = MakeItemSortModes(); LuaFilters = MakeLuaFilters(); ChatChannels = MakeChatChannels(); };
        _accounts.Accounts.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(Resync);
        Stores.Launch.PacketDecoded += OnPacket;
        Stores.Launch.MapActorsDecoded += OnMapActors;
        Stores.Launch.MapInteractivesDecoded += OnMapInteractives;
        Stores.Launch.MapInteractiveUpdated += OnInteractiveUpdated;
        Stores.Launch.MapInteractiveSkillsUpdated += OnInteractiveSkillsUpdated;
        Stores.Launch.PodsUpdated += OnPodsUpdated;
        Stores.Launch.FightersUpdated += OnFightersUpdated;
        Stores.Launch.CombatTacticalAdvisoryUpdated += OnCombatTacticalAdvisoryUpdated;
        Stores.Launch.FighterKilled += OnFighterKilled;
        Stores.Launch.FighterStatsUpdated += OnFighterStatsUpdated;
        Stores.Launch.CharacterSpellsUpdated += OnCharacterSpellsUpdated;
        Stores.Launch.MapActorsAdded += OnActorsAdded;
        Stores.Launch.MapActorRemoved += OnActorRemoved;
        Stores.Launch.MapCellsLoaded += OnMapCells;
        Stores.Launch.MapExitsLoaded += OnMapExits;
        Stores.Launch.MapActorMoved += OnActorMoved;
        Stores.Launch.MapChanged += OnMapChanged;
        Stores.Launch.SelfMoved += OnLeaderSelfMoved;   // suivi COLLÉ niveau cellule (SYNC_MOVE) : les followers rejouent la marche du chef
        Stores.Launch.LeaderMapExit += OnLeaderMapExit;  // suivi COLLÉ : rejeu de la SORTIE de carte du chef (off-graph-safe, Incarnam/instance)
        Stores.Launch.TeamLogged += OnTeamLogged;        // console Équipe commune (lignes d'orchestration [équipe])
        Stores.Launch.SelfMoved += OnSelfMoved;
        Stores.Launch.PartyInvited += OnPartyInvited;
        Stores.Launch.PartyMemberInFight += OnPartyMemberInFight;   // auto-join du combat du chef (event-driven)
        Stores.Launch.CharacterLevelKnown += OnCharacterLevelKnownCap;   // cap de niveau licence (offre gratuite) : arrêt net dès que le niveau connu le dépasse

        // Enregistreur de trajet (« Road Creator » natif) : la modale par-carte est servie par la VM (overlay au niveau
        // fenêtre RoadRecorderOverlays), le journal est routé vers la console Lua du perso enregistré.
        _road.PromptForMap = AskMapPlanAsync;
        _road.Log = s => Dispatcher.UIThread.Post(() => AppendLuaLine("trajet", s, LuaLogLevel.Info, (int)_road.RecordingPid));

        // Killswitch licence : session tuée côté panel (ban/expiration/révocation) -> on ARRÊTE tous les scripts Lua
        // TOUT DE SUITE (sinon le trajet continue). Le scheduler reste bloqué tant que non autorisé (cf EvaluatePlanning).
        LicenseService.SessionEnded += _ => Dispatcher.UIThread.Post(StopAllScripts);
        // Kill switch RUNTIME (launcher control activé pendant que l'app tourne) -> arrêt immédiat des scripts.
        LicenseService.ControlBlocked += _ => Dispatcher.UIThread.Post(StopAllScripts);
        // Droits serveur changés (login/logout/kill/expiration) -> réévaluer le grisage + repousser les toggles interdits.
        LicenseService.PermissionsChanged += () => Dispatcher.UIThread.Post(RefreshPermissionGates);
        // Mode d'affichage / mode discret (Réglages) + droit développeur -> rafraîchir les liaisons de divulgation progressive.
        AppConfig.UiPreferencesChanged += RaiseUiPreferences;
        LicenseService.PermissionsChanged += () => Dispatcher.UIThread.Post(RaiseUiPreferences);
        // Pilotage du script d'un bot depuis le web (START_SCRIPT/STOP_SCRIPT par pid) -> marshalé UI.
        LicenseService.ScriptControlRequested += (pid, start, script, nonce, cmd) =>
            Dispatcher.UIThread.Post(() => OnScriptControlRequested(pid, start, script, nonce, cmd));

        ArchMonsterLog.Recent.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasArchSightings));   // panneau « Archimonstres récents »

        _quickReady = true;
        LoadTeams();   // équipes persistées (teams.json) -> recomposées par nom+serveur à mesure que les bots se connectent
        Resync();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    // Reconstruit la liste des bots = comptes running avec un pid (diff, sans casser les instances existantes).
    private void Resync()
    {
        var running = _accounts.Accounts.Where(a => a.Running && a.Pid is int).ToList();

        foreach (var b in Bots.ToList())
            if (!running.Any(a => a.Pid == b.Pid))
            {
                KeepKickedScript(b);   // MITM arrêté après une déconnexion serveur : script gardé pour « Reconnecter » (avant Forget / Dispose)
                Bots.Remove(b); _byPid.TryRemove(b.Pid, out _); Stores.Launch.SetCombatTacticalAdvisoryEnabled((uint)b.Pid, false); Stores.Launch.ClearCombatProfile((uint)b.Pid); _pushedName.Remove(b.Pid); _teamAutoInvited.Remove(b.Pid); BotScripts.Forget(b.Pid);
                ForgetHdv(b);   // bot fermé : SON repricing / sa mise en vente s'arrêtent avec lui
                _linkLostResume.Remove(b.Pid);   // bot fermé : plus de reprise de script après une perte de session
                // Plage servie par le planning : gardée par COMPTE pour une reprise « Reconnecter » hors ligne (PlanNoteResumed :
                // le script repris est alors confronté à la plage active — bascule si la plage a changé entre-temps).
                if (_planRun.Remove(b.Pid, out var planRun)) _planRunKept[b.Row.Model.Id] = planRun;
                _planRecheck.Remove(b.Pid);
                if (_luaHosts.Remove(b.Pid, out var deadHost)) deadHost.Dispose();   // bot ferme -> arrete + libere son hote Lua
                if (_reviveHosts.Remove(b.Pid, out var deadRevive)) deadRevive.Dispose();   // + son hote de resurrection autonome
                if (_cmdHosts.Remove(b.Pid, out var deadCmd)) { try { deadCmd.Dispose(); } catch { /* best-effort */ } }   // + son hote /cmd (sinon abonne aux events d'un pid reutilisable)
            }

        foreach (var a in running)
            if (a.Pid is int pid && !_byPid.ContainsKey(pid))
            {
                var vm = new BotRowViewModel(a);
                Bots.Add(vm);
                _byPid[pid] = vm;
            }

        if (SelectedBot is null || !Bots.Contains(SelectedBot))
            SelectedBot = Bots.FirstOrDefault();

        // Équipes : réconcilie les membres VIVANTS avec le roster persisté (matérialise les bots qui viennent de se
        // connecter, retire ceux qui se sont fermés). Le roster (par nom+serveur) SURVIT à la déconnexion -> l'équipe
        // enregistrée se recompose toute seule au retour ; suppression seulement manuelle via RemoveTeam.
        ReconcileTeams();

        OnPropertyChanged(nameof(HasBots)); OnPropertyChanged(nameof(InGameCountText)); OnPropertyChanged(nameof(CanFormTeam)); OnPropertyChanged(nameof(ShowNoBotsHint));
    }

    // Threads du relais : route le paquet vers le bon bot (comptage + file console).
    private void OnPacket(DecodedPacket p)
    {
        if (_byPid.TryGetValue((int)p.Pid, out var bot)) bot.Feed(p);
    }

    // Nouvelle carte (map event) : remplace les acteurs et bumpe MapEpoch -> repositionnement SANS animation.
    private void OnMapActors(uint pid, IReadOnlyList<MapActor> actors) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!_byPid.TryGetValue((int)pid, out var bot)) return;
            bot.MapActors = actors;
            bot.MapEpoch++;
            // Capte l'acteur self : son ActorId (pour reconnaitre son jsj) et sa cellule (source de pathfinding initiale).
            if (FindSelf(bot, actors) is { } self) { bot.SelfActorId = self.ActorId; bot.SelfCellId = self.CellId; }
        });

    // Notre perso parmi les acteurs : même ordre que le moteur (BotWorldStore) — characterId authentifié, puis nom (sans
    // tenir compte de la casse), puis persos connus du compte. Le nom seul ratait le perso (nom pas encore résolu, « ? »,
    // casse) : plus de marqueur jaune, plus de source de déplacement.
    private static MapActor? FindSelf(BotRowViewModel bot, IReadOnlyList<MapActor> actors)
    {
        uint pid = (uint)Math.Max(0, bot.Pid);
        long charId = pid > 0 ? Stores.Launch.CharacterId(pid) : 0;
        if (charId != 0 && actors.FirstOrDefault(a => a.Kind == MapActorKind.Player && a.ActorId == charId) is { } byId) return byId;
        if (bot.SelfActorId is long known && known != 0 && actors.FirstOrDefault(a => a.Kind == MapActorKind.Player && a.ActorId == known) is { } byKnown) return byKnown;
        var name = bot.Row.CharacterName;
        if (!string.IsNullOrEmpty(name) && actors.FirstOrDefault(a => a.Kind == MapActorKind.Player && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) is { } byName) return byName;
        if (charId == 0 && pid > 0)
        {
            var mine = Stores.Launch.AccountCharacterIds(pid);
            if (mine.Count > 0) return actors.FirstOrDefault(a => a.Kind == MapActorKind.Player && mine.Contains(a.ActorId));
        }
        return null;
    }

    // Acteurs qui APPARAISSENT (jsn) : on les FUSIONNE dans la carte courante (ajout / maj par actorId), sans reset.
    private void OnActorsAdded(uint pid, IReadOnlyList<MapActor> added) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!_byPid.TryGetValue((int)pid, out var bot)) return;
            var list = bot.MapActors?.ToList() ?? new List<MapActor>();
            foreach (var a in added)
            {
                int i = list.FindIndex(x => x.ActorId == a.ActorId);
                if (i >= 0) list[i] = a; else list.Add(a);
            }
            bot.MapActors = list;
        });

    // Acteur qui DISPARAIT (kmu) : on le retire de la carte par actorId (no-op si ce n'etait pas un de nos acteurs).
    private void OnActorRemoved(uint pid, long actorId) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!_byPid.TryGetValue((int)pid, out var bot) || bot.MapActors is null) return;
            if (bot.MapActors.All(a => a.ActorId != actorId)) return;
            bot.MapActors = bot.MapActors.Where(a => a.ActorId != actorId).ToList();
        });

    // Tache de fond (extraction bundle) : marshale sur l'UI, pose les cellules (relief) sur le bon bot.
    private void OnMapCells(uint pid, long map, IReadOnlyList<MapCell> cells) =>
        Dispatcher.UIThread.Post(() => { if (_byPid.TryGetValue((int)pid, out var bot)) bot.MapCells = cells; });

    // Elements interactifs de la nouvelle carte -> rendu (icones/marqueurs) + clic droit pour recolter.
    private void OnMapInteractives(uint pid, IReadOnlyList<MapInteractive> interactives) =>
        Dispatcher.UIThread.Post(() => { if (_byPid.TryGetValue((int)pid, out var bot)) bot.MapInteractives = interactives; });

    // Recolte : maj de l'etat d'un interactif (StatedElementUpdatedEvent) -> reflet live (vert -> gris).
    private void OnInteractiveUpdated(uint pid, int elementId, int cell, int state) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!_byPid.TryGetValue((int)pid, out var bot) || bot.MapInteractives is null) return;
            bot.MapInteractives = bot.MapInteractives.Select(x => x.ElementId == elementId ? x with { State = state } : x).ToList();
        });

    // Recolte : maj des SKILLS d'un interactif (InteractiveElementUpdatedEvent iwm) -> Harvestable change -> losange
    // vert (dispo) <-> gris (epuise / en cours de recolte). On remplace la liste (nouvelle ref) pour re-render la carte.
    private void OnInteractiveSkillsUpdated(uint pid, int elementId, IReadOnlyList<GivreClone.Protocol.InteractiveSkill> skills) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!_byPid.TryGetValue((int)pid, out var bot) || bot.MapInteractives is null) return;
            bot.MapInteractives = bot.MapInteractives.Select(x => x.ElementId == elementId ? x with { Skills = skills } : x).ToList();
        });

    // Sorties de la nouvelle carte (voisins) -> active les boutons de changement de carte disponibles.
    private void OnMapExits(uint pid, MapExits exits) =>
        Dispatcher.UIThread.Post(() => { if (_byPid.TryGetValue((int)pid, out var bot)) bot.Exits = exits; });

    // Nouvelle carte : retient le mapId (requis pour forger un deplacement / clic-pour-deplacer).
    private void OnMapChanged(uint pid, long mapId, int? x, int? y) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_byPid.TryGetValue((int)pid, out var bot)) bot.MapId = mapId;
            // Le CHEF d'une équipe en suivi vient de changer de carte -> on relance SON suivi TOUT DE SUITE (sans attendre le
            // tick ~300 ms ni le répit). SUIVI COLLÉ (SYNC_MOVE) : on dérive sa DIRECTION de sortie (delta de coord carte de
            // départ -> carte d'arrivée) et un follower encore sur la carte de départ rejoue la MÊME sortie (lockstep).
            // Sinon (dérivé, ou direction indérivable = porte/zaap) -> chasse simultanée. No-op pour un pid non-chef.
            if (!LicenseService.CanBot) return;
            foreach (var team in Teams)
            {
                if (!team.Active || !team.FollowEnabled) continue;
                if (team.Leader is not { } leader || (uint)leader.Pid != pid) continue;
                _leaderPrevMap[(int)pid] = mapId;
                DriveFollowForTeam(team, Environment.TickCount64);   // chasse IMMÉDIATE de la carte du chef ; le rejeu de la sortie EXACTE (OnLeaderMapExit) gère le cas collé off-graph
                break;
            }
        });

    /// <summary>Diagnostic transport live (onglet Console), alimente par TransportDiag.</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> ConsoleLines => TransportDiag.Lines;

    public void OnMoveRequested(int source, int target)
    {
        if (SelectedBot is not { } b) return;
        bool ok = b.RequestMove(source, target);
        b.Inspector.NoteAction(Loc.Fmt("L.MapInsp.MoveAction", target), target, ok);
    }

    /// <summary>Mode compact multibot : liste des comptes (nom + position) pour changer de bot sans quitter la carte. Persisté.</summary>
    [ObservableProperty] public partial bool MapCompact { get; set; } = AppConfig.Current.MapCompact;
    partial void OnMapCompactChanged(bool value) { AppConfig.Current.MapCompact = value; AppConfig.Save(); }
    [RelayCommand] public void SelectBot(BotRowViewModel? bot) { if (bot is not null && Bots.Contains(bot)) SelectedBot = bot; }

    /// <summary>Relais du clic-droit "Recolter" (IsoMapControl.HarvestRequested) vers le bot selectionne : marche
    /// jusqu'a une cellule adjacente a la ressource puis envoie le InteractiveUseRequest a l'arrivee.</summary>
    public void OnHarvestRequested(GivreClone.Protocol.MapInteractive interactive)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) { AppendLuaLine("recolte", Loc.T("L.Lua.NoPermGather"), LuaLogLevel.Warning); return; }
        // Un atelier de craft (plan de travail, alambic…) s'ouvre dans la carte : ses recettes, pas l'interface brute du jeu.
        if (SelectedBot is { } b0 && b0.Inspector.TryOpenStation(interactive)) return;
        if (SelectedBot is not { } b) return;
        bool ok = b.RequestHarvest(interactive);
        b.Inspector.NoteAction(Loc.Fmt("L.MapInsp.UseAction", interactive.Name ?? ("#" + interactive.ElementTypeId)), -1, ok);   // preuve : aucune ici -> « inconnu » après le délai
    }

    /// <summary>
    /// Captures the bot currently displayed by IsoMapControl. The map binds to
    /// Inspector.VisibleActors (filtered), not to MapActors itself. A binding
    /// still displaying the previous bot is not an attack-capable snapshot.
    /// </summary>
    public MapAttackMenuOrigin? CaptureAttackMenuOrigin(IEnumerable? displayedActors, int displayedEpoch)
    {
        if (SelectedBot is not { Pid: > 0, MapId: > 0 } bot
            || bot.MapActors is null || !Bots.Contains(bot)) return null;
        var world = Stores.World.Get((uint)bot.Pid);
        long generation = Interlocked.Read(ref world.MapGeneration);
        if (generation <= 0 || world.MapId != bot.MapId || !world.SelfReadyFor(bot.MapId)
            || Interlocked.Read(ref world.MapGeneration) != generation) return null;
        return MapAttackMenuOrigin.Capture(bot, bot.Pid, bot.MapId, bot.MapEpoch,
            Interlocked.Read(ref _mapAttackSelectionGeneration),
            generation,
            bot.Inspector.VisibleActors, displayedActors, displayedEpoch);
    }

    /// <summary>Clic droit sur un joueur de la carte : échange ou invitation de groupe (mêmes verbes que le jeu).</summary>
    public void OnPlayerAction(GivreClone.Protocol.MapActor player, string action)
    {
        if (SelectedBot is not { Pid: > 0 } b) return;
        uint pid = (uint)b.Pid;
        bool ok = action == "trade" ? Stores.Launch.TradeWithPlayer(pid, player.ActorId) : Stores.Launch.PartyInvite(pid, player.Name ?? "");
        AppendLuaLine("carte", ok ? $"{(action == "trade" ? "Échange proposé à" : "Invitation de groupe envoyée à")} {player.Name}" : $"Action refusée pour {player.Name}" + (action == "party" && Stores.Launch.LastPartyInviteError is { } e ? $" ({e})" : ""), ok ? LuaLogLevel.Success : LuaLogLevel.Warning);
    }

    /// <summary>
    /// Fail closed on changed bot, PID, map, map/actor snapshot or selection
    /// revision. MapInspector buttons retain their direct AttackHandler path.
    /// </summary>
    public void OnAttackRequested(MapAttackMenuRequest? request)
    {
        if (request?.Origin is null || SelectedBot is not { } bot) return;
        var current = CaptureAttackMenuOrigin(request.Origin.VisibleActors, request.Origin.MapEpoch);
        if (!request.Origin.Allows(current, request.Target, bot.MapActors)) return;
        if (!LicenseService.HasPermission(LicenseService.Perm.Fight)) { AppendLuaLine("combat", Loc.T("L.Lua.NoPermFight"), LuaLogLevel.Warning); return; }
        bot.RequestAttack(request.Target);
    }

    /// <summary>Pods (poids) du perso mis a jour (InventoryWeightEvent iun) -> "cur / max" sur la fiche du bot.</summary>
    private void OnPodsUpdated(uint pid, int cur, int max) =>
        Dispatcher.UIThread.Post(() => { if (_byPid.TryGetValue((int)pid, out var bot)) { bot.Pods = $"{cur} / {max}"; bot.PodsCur = cur; bot.PodsMax = max; } });

    /// <summary>Combat : positions des combattants (kmk) -> acteurs sur la carte. actorId positif = joueur (moi surligne
    /// via le nom, ou allie), negatif = monstre. On recale aussi SelfCellId sur ma cellule de combat.</summary>
    private void OnFightersUpdated(uint pid, IReadOnlyList<(int Cell, long ActorId)> fighters) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!_byPid.TryGetValue((int)pid, out var bot)) return;
            long charId = Stores.Launch.CharacterId(pid);
            long? self = charId != 0 ? charId : bot.SelfActorId;
            if (self is long sid && sid != 0) bot.SelfActorId = sid;
            var actors = new List<MapActor>(fighters.Count);
            foreach (var (cell, id) in fighters)
            {
                bool isSelf = self is long s && id == s;
                var kind = id >= 0 ? MapActorKind.Player : MapActorKind.Monster;
                string name = isSelf ? (bot.Row.CharacterName ?? Loc.T("L.Bots.Actor.Me")) : (id >= 0 ? Loc.T("L.Bots.Actor.Ally") : Loc.T("L.Bots.Actor.Monster"));
                actors.Add(new MapActor(cell, 0, id, name, kind));
                if (isSelf) bot.SelfCellId = cell;
            }
            bot.MapActors = actors;
        });

    /// <summary>Combat : un combattant est mort (GameActionFightEvent.Death) -> on le retire de la carte (no-op s'il
    /// n'y est pas / c'est un vivant : garde la liste inchangee sinon).</summary>
    private void OnFighterKilled(uint pid, long deadId) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!_byPid.TryGetValue((int)pid, out var bot) || bot.MapActors is null) return;
            var kept = bot.MapActors.Where(a => a.ActorId != deadId).ToList();
            if (kept.Count != bot.MapActors.Count) bot.MapActors = kept;
        });

    // Recalage de la cellule REELLE du perso (self) : destination d'un jrw sortant (prediction) ou cellule serveur au
    // refus (jqt). On l'ecrit sur l'acteur self (retrouve par NOM) -> la source du pathfinding (IsoMapControl.SelfCell)
    // reste fiable meme si le jsj ne matche pas l'acteur par actorId (cause du "bloque en 440").
    // Pilotage du perso (self) depuis le jrw SORTANT : le marqueur suit le chemin DEROULE (cellule par cellule = le vrai
    // trajet du jeu), de facon FIABLE (on ne depend pas du match actorId du jsj). La derniere cellule = nouvelle source.
    private void OnSelfMoved(uint pid, IReadOnlyList<int> path) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!_byPid.TryGetValue((int)pid, out var bot) || path.Count == 0) return;
            int dest = path[path.Count - 1];
            if ((uint)dest >= 560) return;
            bot.SelfCellId = dest;   // SOURCE de pathfinding
            if (bot.MapActors is null) return;

            // Retrouve l'acteur self (id puis nom) -> pose sa cellule + anime le long des cellules du chemin.
            var self = FindSelf(bot, bot.MapActors);
            if (self is null) return;
            bot.SelfActorId = self.ActorId;
            bot.MapActors = bot.MapActors.Select(a => a.ActorId == self.ActorId ? a with { CellId = dest } : a).ToList();
            bot.LastMove = new MoveInfo(self.ActorId, path);
        });

    // Deplacement live : position finale (destination) dans le modele + signal du CHEMIN pour l'animation.
    private void OnActorMoved(uint pid, long actorId, IReadOnlyList<int> cells) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!_byPid.TryGetValue((int)pid, out var bot) || bot.MapActors is null || cells.Count == 0) return;
            int dest = cells[cells.Count - 1];
            // Le SELF est pilote par le jrw sortant (OnSelfMoved) - fiable et exact. Ici on ne fait que tenir sa SOURCE
            // a jour (verite serveur) sans re-declencher l'anim (eviterait un double), et on ne l'anime PAS via le jsj.
            if (actorId == bot.SelfActorId) { if ((uint)dest < 560) bot.SelfCellId = dest; return; }
            if (!bot.MapActors.Any(a => a.ActorId == actorId)) return;   // acteur pas (encore) sur notre carte
            bot.MapActors = bot.MapActors.Select(a => a.ActorId == actorId ? a with { CellId = dest } : a).ToList();
            bot.LastMove = new MoveInfo(actorId, cells);                 // -> anime le long du chemin
        });

    // Cost of the 300 ms refresh, observable without a profiler: get_app_status / diagnostics read these.
    internal static long TickCount, TickAllocatedBytes, TickElapsedTicks;

    private void Tick()
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread(), started = System.Diagnostics.Stopwatch.GetTimestamp();
        try { TickCore(); }
        finally
        {
            Interlocked.Add(ref TickAllocatedBytes, GC.GetAllocatedBytesForCurrentThread() - allocated);
            Interlocked.Add(ref TickElapsedTicks, System.Diagnostics.Stopwatch.GetTimestamp() - started);
            Interlocked.Increment(ref TickCount);
        }
    }

    private void TickCore()
    {
        Resync();                       // capte les bascules Running (pas notifiees par CollectionChanged)
        CheckBotSignals();
        foreach (var b in Bots) b.DrainToUi();   // CHAQUE bot vide sa file dans SON backing (historique par-bot) ; la vue observable ne bouge que pour le bot affiché
        foreach (var b in Bots)
        {
            b.RefreshSummary();
            // Pousse (ou re-pousse) le profil de combat du bot quand son NOM se resout : chaque perso recupere alors sa
            // propre config -> l'auto-combat fonctionne meme sans avoir selectionne le bot (ex. reprise apres relance).
            if (b.Pid > 0)
            {
                var nm = b.Row.CharacterName;
                if (!_pushedName.TryGetValue(b.Pid, out var prev) || prev != nm)
                {
                    Stores.Launch.SetCombatProfile((uint)b.Pid, ProfileFor(nm));
                    _pushedName[b.Pid] = nm;
                    if (_profileBot == b) LoadCombatProfile(b);   // le bot affiche vient d'etre identifie -> recharge l'onglet
                    // Le nom se resout APRES la selection (via kvi) : recharge le planning sur la BONNE cle (sinon il etait
                    // edite sous "?" tandis que le scheduler evaluait le vrai nom -> restait "En attente").
                    PlanningStore.MigratePlaceholder(nm);   // rattache les plages saisies sous "?" au vrai nom
                    BotSettingsStore.MigratePlaceholder(nm);   // idem pour les réglages Paramètres saisis avant résolution du nom
                    HdvProfileStore.MigratePlaceholder(nm);    // idem pour la config HDV saisie avant résolution du nom
                    if (ReferenceEquals(SelectedBot, b))
                    {
                        Planning.Load(nm); Settings.Load(nm); Hdv.Load(nm); Hdv.AttachPid(b.Pid > 0 ? (uint)b.Pid : 0u);
                        // Éditeur Lua : chargé à la sélection AVANT l'identification (-> modèle d'exemple) : on affiche
                        // maintenant le script DU perso, sauf édition en cours non sauvée (fichier chargé, frappe) — et
                        // seulement si l'éditeur ne montre pas DÉJÀ ce perso (re-clé d'un « Reconnecter » en place : le nom
                        // ne change pas, recharger effacerait le fichier chargé et le nom de script de la télémétrie).
                        if (!_luaDirty && !string.IsNullOrEmpty(nm) && _luaLoadedFor != nm) LoadLuaScript(nm);
                    }
                }

                // Suivi du temps de BOTTING (stats persistees) : detecte le passage running<->arrete du script (y compris
                // fin naturelle) -> ouvre/ferme une session ; heartbeat pendant qu'il tourne (survit a un crash).
                bool botting = _luaHosts.TryGetValue(b.Pid, out var host) && host.Running;
                if (botting && _bottingPids.Add(b.Pid)) { Stores.Stats.StartBotting(nm); Stores.Launch.NoteProgress((uint)b.Pid); }   // grâce anti-stuck au démarrage
                else if (!botting && _bottingPids.Remove(b.Pid)) { Stores.Stats.StopBotting(nm); _stuckStage.Remove(b.Pid); }
                // Le bouton Lancer/Arrêter reflète l'état RÉEL de l'hôte : un script qui s'arrête TOUT SEUL (voyage bloqué,
                // fin naturelle, erreur) rebascule le bouton sur « Lancer » au tick suivant (~300 ms), sans clic.
                if (SelectedBot?.Pid == b.Pid && LuaRunning != botting) LuaRunning = botting;
                if (SelectedBot?.Pid == b.Pid) RefreshActivityRecoveryUi();

                // Résurrection AUTONOME au Phénix : agit même SANS script (combat-seul, follower, script arrêté). Ne fait
                // rien si un script tourne (il gère déjà sa propre résurrection) ni si le perso est vivant / AutoRevive off.
                DriveAutoRevive(b.Pid);

                // Lot F : PV hors combat EXTRAPOLÉS (régénération en cours) -> l'onglet Infos monte en direct. Lectures
                // thread-safe (ConcurrentDictionary + record immuable) ; ObservableProperty : pas de notif si valeur identique.
                if (!Stores.Launch.IsInFight((uint)b.Pid) && Stores.Launch.SelfLife((uint)b.Pid) is { } lf)
                {
                    b.CharLife = lf.Cur; b.CharMaxLife = lf.Max;
                    b.LifeIsEstimated = Stores.Launch.AuthoritativeLifePercent((uint)b.Pid) is null
                        || Stores.Launch.SelfLifeAuthoritative((uint)b.Pid) != lf.Cur;
                }

                // Watchdog ANTI-STUCK : détecte un script qui tourne mais n'agit plus, récupération DOUCE bornée.
                DriveAntiStuck(b.Pid, nm);
            }
        }

        DriveSocketResume();         // bot socket reconnecté : reprise du script suspendu à la coupure
        DrivePendingDisconnects();   // compte à rebours de la modale « déconnexion planifiée » (à chaque tick : décompte fluide)
        UpdateTeam();       // présence des membres (comparaison de carte) ; no-op si aucune équipe
        PushTeamCombatWait();   // snapshot « le chef attend l'équipe avant combat » -> lu par FightOne (thread script)
        DrainTeamBroadcasts();  // team:broadcast Lua : lance le snippet enfilé sur les hôtes des autres membres
        DrainTeamRunOnce();     // Lot G : team:runOnce Lua -> extrait exécuté sur l'hôte /cmd transitoire du membre ciblé
        DrainScriptSwitches();  // Lot G : loadScript / reloadScript Lua -> bascule du script principal (bibliothèque planning / dossier scripts)
        DrainTeamJoinLeader();  // team:joinLeader / joinBoss() Lua : ralliement de l'équipe sur la carte du chef (= /joinBoss)
        PushScriptAccounts();   // compat tiers: instantané des comptes (givreController:*, global:username)
        DrainAccountActions();  // compat tiers: global:disconnect / reconnect*, givreController:loadAnAccount / unloadAnAccount
        DriveScriptReconnects();// relances programmées par global:reconnect*
        DriveAutoStartScripts();// option de compte : script du perso démarré dès qu'il est en jeu
        DriveConnectionJournal();// journal de connexion : étapes en attente de leur bot + « Bienvenue dans DOFUS ! »
        DriveTeamFollow();  // suivi synthétique : les followers rejoignent la carte de leur chef
        DriveTeamCombat();  // combat coordonné : les followers rejoignent le combat du chef
        if (SelectedBot is { } shown)   // fenêtre d'interaction (banque / échange / boutique) : réactive, à chaque tick
        {
            shown.Interaction.ScriptRunning ??= () => _luaHosts.TryGetValue(shown.Pid, out var h) && h.Running;
            shown.Interaction.Poll();
            shown.RefreshTravelShortcuts();   // havre-sac / zaap / zaapi de la carte (boutons de l'onglet Carte)
            RefreshQuickActions();
        }
        DriveNearbyTravel();   // « À proximité » : fin du trajet en cours -> état du bouton
        DriveRoadEditor();     // onglet Trajets : position du perso sur la mini-carte

        // Throttle ~1,5 s : heartbeat des sessions ouvertes, persistance debounce, et rafraichissement de l'onglet Stats.
        if (++_statsTick >= 5)
        {
            _statsTick = 0;
            foreach (var b in Bots)
            {
                if (b.Pid <= 0) continue;
                if (_bottingPids.Contains(b.Pid)) Stores.Stats.BottingHeartbeat(b.Row.CharacterName);
                // Kamas/XP échantillonnés pour TOUT bot en jeu (même hors script) -> les totaux apparaissent dès qu'ils changent.
                Stores.Stats.SampleKamas(b.Row.CharacterName, Stores.Launch.Kamas((uint)b.Pid));
                Stores.Stats.SampleXp(b.Row.CharacterName, Stores.Launch.CharacterXp((uint)b.Pid));
                if (Stores.Launch.Pods((uint)b.Pid) is { } pw) Stores.Stats.SamplePods(b.Row.CharacterName, pw.Cur);
                var bs = BotSettingsStore.Get(b.Row.CharacterName);
                Stores.Launch.SetRegenerationOptions((uint)b.Pid, bs);
                Stores.Launch.SetAutoClaimSetting((uint)b.Pid, bs.AchievementAutoClaim);                // Paramètres > Succès
                Stores.Launch.SetTravelPreference((uint)b.Pid, bs.PreferWalk, bs.WalkMaxHops);          // Paramètres > Déplacement
                PushZaapRoute((uint)b.Pid, b.Row.CharacterName, force: false);
            }
            Stores.Stats.FlushDue();
            Stats.Refresh(SelectedBot?.Row.CharacterName);
            if (SelectedBot is { Pid: > 0 } regenBot) Settings.RefreshRegeneration((uint)regenBot.Pid);
            SelectedBot?.RefreshInventory();   // onglet Inventaire (throttle ~1,5 s)
            SelectedBot?.RefreshGearSets();    // panoplies du perso (nom résolu après la sélection)
            SelectedBot?.RefreshInfos();       // onglet Infos / carte État : zone, abonnement, kamas, XP, énergie
            if (_profileBot is not null) RefreshLiveCombatVisualStatus();
            Hdv.RefreshRelistStats();          // HDV : lots en vente / en stock de chaque règle de remise en vente
            CheckFarmedAnomalies();            // anomalie farmée arrivée au bout de son temps -> notification Discord
            CheckResaleOpportunities();        // objet du sac au-dessus de la moyenne HDV -> notification Discord
            CheckPriceAlerts();                // alertes de prix (cible atteinte à l'HDV observé) -> Discord
            CheckArchMonsters();               // archimonstre / monstre à âme capturable apparu sur la carte -> téléphone + Discord
            CheckSubscriptions();              // fin d'abonnement -> suivi + alerte Discord (7 / 3 / 1 jour, expiration)
            RefreshMuleCandidates();           // mules possibles (autres bots en jeu)
            if (ExpTabSelected) RefreshAnomalies();   // onglet Expédition : anomalies ouvertes (bonus, temps restant)
            SelectedBot?.RefreshBank();        // onglet Banque (contenu du coffre)
            SelectedBot?.Inspector.RefreshFreshness();
            SelectedBot?.Inspector.PollFeedback();   // attente -> confirmé / inconnu (preuve ou délai)
            SelectedBot?.Inspector.PollDialog();   // dialogue PNJ : question / réponses reçues -> fiche + historique court   // « données reçues il y a N s » (carte inspectable)
            SelectedBot?.RefreshJobs();        // métiers reçus, XP seulement si champs valides du serveur
            Treasure.AttachPid(SelectedBot?.Pid ?? 0);
            Treasure.Refresh();
            SmithMagic.AttachPid(SelectedBot?.Pid ?? 0);
            DrivePlanningConnect();            // « vrai planning » : connecte / déconnecte les comptes selon les plages
            EvaluatePlanning();                // scheduler : lance/arrete le script selon les plages du planning
            RefreshPlannedOffline();           // lignes « hors-ligne · géré par le planning » + badge comptes + horloge modale
            ReconcileTeams();                  // capte les bots dont le nom/serveur s'est résolu après connexion
            DriveTeamAutoForm();               // « Connecter en jeu » : le chef invite les membres à mesure qu'ils arrivent
            if (_teamsDirty) SaveTeamsNow();   // équipes modifiées -> persiste (teams.json), débounce ~1,5 s
        }
    }

    partial void OnSelectedBotChanged(BotRowViewModel? oldValue, BotRowViewModel? newValue)
    {
        Interlocked.Increment(ref _mapAttackSelectionGeneration);
        StatSpendArmed = false;
        StatSpendText = "";
        if (oldValue?.Pid != newValue?.Pid)
            try { _quickCts?.Cancel(); } catch { /* trajet déjà fini */ }
        // Sauve le script de l'ancien perso : celui pour lequel l'éditeur a été chargé (le nom de la ligne peut avoir changé ou été vidé).
        if (oldValue is not null)
        {
            oldValue.Selected = false;
            PersistLuaScript(!string.IsNullOrEmpty(_luaLoadedFor) ? _luaLoadedFor : oldValue.Row.CharacterName);
            SaveExpedition(!string.IsNullOrEmpty(oldValue.Row.CharacterName) ? oldValue.Row.CharacterName : null);
        }
        if (newValue is not null) { newValue.Selected = true; }
        Planning.EnableLocked = newValue?.FollowerLocked ?? false;
        LuaRunning = newValue is { Pid: > 0 } b && _luaHosts.TryGetValue(b.Pid, out var h) && h.Running;   // etat du bouton Lancer/Arreter
        RefreshActivityRecoveryUi();
        OnPropertyChanged(nameof(SocketAuthorityStatusText));
        SelectHdv(newValue);                                       // VM HDV DE CE bot (celui de l'ancien bot continue en fond)
        Hdv.AttachPid(newValue is { } hb && hb.Pid > 0 ? (uint)hb.Pid : 0u);   // PID connu avant de choisir la config par serveur
        uint curBotPid = newValue is { } hbPid && hbPid.Pid > 0 ? (uint)hbPid.Pid : 0u;
        Bak.AttachCharacter(newValue?.Row.CharacterName ?? "", curBotPid, Stores.Launch.ServerIdOf(curBotPid), Stores.Launch.ServerNameOf(curBotPid));

        // Chargements secondaires différés en priorité Background : ouverture de fiche INSTANTANÉE (0 ms de freeze)
        var selectedSnapshot = newValue;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(SelectedBot, selectedSnapshot) || selectedSnapshot is null) return;
            selectedSnapshot.SyncPacketView();
            LoadCombatProfile(selectedSnapshot);
            LoadLuaScript(selectedSnapshot.Row.CharacterName);
            RestoreExpedition(selectedSnapshot.Row.CharacterName);
            OnPropertyChanged(nameof(ExpRouteProgress));
            UpdateExpRoutePreview();
            Stats.Refresh(selectedSnapshot.Row.CharacterName, force: true);
            Planning.Load(selectedSnapshot.Row.CharacterName);
            Settings.Load(selectedSnapshot.Row.CharacterName);
            Hdv.Load(selectedSnapshot.Row.CharacterName);
            if (ExpDungeonMode) RefreshExpDungeonKeys();
            RebuildLuaOutput();
        }, Avalonia.Threading.DispatcherPriority.Background);
        Treasure.ScriptRunning ??= pid => _luaHosts.TryGetValue((int)pid, out var h) && h.Running;
        Treasure.Travel ??= async (pid, map, ct) =>
        {
            var host = NewFollowHost((int)pid);
            try { return await host.TravelToMapAsync(map, ct).ConfigureAwait(true); }
            finally { try { host.Dispose(); } catch { } }
        };
        Treasure.StartAutoHuntScript ??= (pid, level, fails) => StartNamedScript(pid, "Chasse au trésor", Services.Treasure.TreasureScript.Build(level, fails));
        Treasure.StartFouduglenScript ??= (pid, count, budget, maxUnitPrice) => StartNamedScript(pid, "Fouduglen", Services.Treasure.FouduglenScript.Build(count, budget, maxUnitPrice));
        Treasure.StopHuntScript ??= (pid, file) => StopNamedHuntScript(pid, file);
        Treasure.MapHasHuntChest ??= HuntChestElementOnMap;
        Treasure.NearestHuntChest ??= (_, here) => Services.Treasure.HuntChestMaps.Nearest(here, Stores.Launch.MapCoordOf);
        Treasure.PoiName ??= id => Stores.Launch.GameNames.PoiName(id);
        Treasure.NpcName ??= id => Stores.Launch.GameNames.NpcName(id);
        Treasure.Confirm ??= (title, body, ok) => Dialogs.Confirm(title, body, ok, danger: true);
        Treasure.ResolveStepMap ??= ResolveTreasureStepMapAsync;
        Treasure.AttachPid(newValue?.Pid ?? 0);
        SmithMagic.InteractivesForPid ??= pid =>
        {
            long map = Stores.Launch.CurrentMap(pid) ?? 0;
            return map > 0 && Stores.Launch.InteractivesDecodedMap(pid) == map
                ? Stores.World.Get(pid).Interactives.Concat(Stores.Launch.PlacelessInteractives(pid, map)).ToArray() : null;
        };
        SmithMagic.CurrentMap ??= pid => Stores.Launch.CurrentMap(pid) ?? 0;
        SmithMagic.Travel ??= TravelSmithWorkshopAsync;
        SmithMagic.EnterInterior ??= EnterSmithInteriorAsync;
        SmithMagic.ApproachWorkshop ??= ApproachSmithMachineAsync;
        SmithMagic.BuyRunes ??= BuyRunesAsync;
        SmithMagic.BuyRunesAtLotPrices ??= BuyRunesAtLotPricesAsync;
        SmithMagic.BuyLevelingSupport ??= BuyLevelingSupportAsync;
        SmithMagic.CrushItems ??= (pid, element, skill, items, ct) => Stores.Launch.CrushAsync(pid, element, skill, items, ct);
        SmithMagic.Notify ??= (pid, text) =>
        {
            Toasts.Show(text);
            if (!_byPid.TryGetValue((int)pid, out var b)) return;
            var cfg = BotSettingsStore.Get(b.Row.CharacterName);
            if (cfg.NotifyForge && LicenseService.HasPermission(LicenseService.Perm.PmRelay))
                WebhookService.Post(cfg.DiscordWebhookUrl, $"{b.Row.CharacterName} · {text}");
        };
        SmithMagic.AttachPid(newValue?.Pid ?? 0);
        RefreshQuickActions();
    }

    [RelayCommand]
    private void SetFilter(string filter)
    {
        if (SelectedBot is not null) SelectedBot.Filter = filter;
    }

    /// <summary>Bascule la vue de l'inspecteur du paquet sélectionné : "tree" (arbre décodé), "json", "hex".</summary>
    [RelayCommand]
    private void SetPacketView(string view)
    {
        if (SelectedBot is not null) SelectedBot.PacketView = view switch { "json" => 1, "hex" when LicenseService.IsDeveloper => 2, _ => 0 };
    }

    /// <summary>Licence DÉVELOPPEUR : outils de reverse visibles (vue hex des trames, codes).</summary>
    public bool IsDeveloper => LicenseService.IsDeveloper;
    // ===== Mode d'affichage (standard / expert / dev) + mode discret : persistés dans AppConfig =====
    public string UiMode => AppConfig.Current.UiMode;
    public string EffectiveUiMode => AppConfig.EffectiveUiMode;
    public bool IsStandardUi => EffectiveUiMode == "standard";
    public bool IsExpertUi => EffectiveUiMode is "expert" or "dev";
    public bool IsDeveloperUi => EffectiveUiMode == "dev";
    public bool IsDeveloperLicense => LicenseService.IsDeveloper;
    public bool StreamerPrivacy => AppConfig.Current.StreamerPrivacy;
    private void RaiseUiPreferences()
    {
        OnPropertyChanged(nameof(UiMode));
        OnPropertyChanged(nameof(EffectiveUiMode));
        OnPropertyChanged(nameof(IsStandardUi));
        OnPropertyChanged(nameof(IsExpertUi));
        OnPropertyChanged(nameof(IsDeveloperUi));
        OnPropertyChanged(nameof(IsDeveloperLicense));
        OnPropertyChanged(nameof(StreamerPrivacy));
    }

    private void RaiseDeveloper()
    {
        OnPropertyChanged(nameof(IsDeveloper));
        if (!LicenseService.IsDeveloper && SelectedBot is { PacketView: 2 } b) b.PacketView = 0;
    }

    /// <summary>Surligne les cellules de transition (mapChangeData) sur la carte interactive.</summary>
    [ObservableProperty] private bool _showTransitions = true;

    /// <summary>Affiche le numéro (id) de chaque cellule sur la carte interactive (repère manuel des cellId).</summary>
    [ObservableProperty] private bool _showCellIds;

    // Image du jeu (DofusDB) en fond de la carte du bot + calage manuel (décalage de la grille dans l'image, zoom, opacité des cases).
    [ObservableProperty] private bool _showGameImage;
    [ObservableProperty] private double _imgOffX = 240;
    [ObservableProperty] private double _imgOffY = 20;
    [ObservableProperty] private double _imgScale = 1;
    [ObservableProperty] private double _imgCellOpacity = 0.3;

    // Changement de carte (boutons directionnels autour de la carte). Un 2e clic sur la même direction ANNULE la sortie en cours ;
    // un clic sur une autre direction annule puis repart vers celle-là dès que la marche en cours est terminée.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTopPending), nameof(IsBottomPending), nameof(IsLeftPending), nameof(IsRightPending))]
    private string _pendingMapDir = "";
    public bool IsTopPending => PendingMapDir == "top";
    public bool IsBottomPending => PendingMapDir == "bottom";
    public bool IsLeftPending => PendingMapDir == "left";
    public bool IsRightPending => PendingMapDir == "right";

    [RelayCommand]
    private async Task MapChange(string dir)
    {
        MapDir? d = dir switch
        {
            "top" => MapDir.Top, "bottom" => MapDir.Bottom, "left" => MapDir.Left, "right" => MapDir.Right, _ => null,
        };
        if (d is not MapDir md || SelectedBot is not { Pid: > 0 } bot) return;
        uint pid = (uint)bot.Pid;
        if (PendingMapDir.Length > 0)
        {
            Stores.Launch.CancelPendingMapChange(pid);
            string was = PendingMapDir; PendingMapDir = "";
            if (was == dir) return;   // même bouton : annulation seule
            for (int i = 0; i < 40 && Stores.Launch.MoveInProgress(pid); i++) await Task.Delay(150);   // laisse finir la marche en cours
        }
        long startMap = bot.MapId;
        bot.RequestMapChange(md);
        if (!Stores.Launch.HasPendingMapChange(pid)) return;
        PendingMapDir = dir;
        for (int i = 0; i < 60 && PendingMapDir == dir && bot.MapId == startMap && Stores.Launch.HasPendingMapChange(pid); i++) await Task.Delay(150);
        if (PendingMapDir == dir) PendingMapDir = "";   // sortie faite, refusée ou expirée
    }

    [RelayCommand]
    private void ClearConsole() => SelectedBot?.ResetConsole();

    [RelayCommand]
    private void Disconnect()
    {
        if (SelectedBot is not null) _accounts.StopCommand.Execute(SelectedBot.Row);
    }

    [RelayCommand]
    private async Task CopyLoginAsync()
    {
        if (SelectedBot is null) return;
        await ClipboardService.CopyAsync(SelectedBot.Row.Login).ConfigureAwait(true);
        Toasts.Show(Loc.Fmt("L.Bots.LoginCopied", SelectedBot.Row.Login), sound: false);
    }
}
