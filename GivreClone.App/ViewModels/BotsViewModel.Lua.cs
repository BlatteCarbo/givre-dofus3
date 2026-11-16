using System;
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
using GivreClone.App.Services;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

// ===================== Scripting Lua : hôtes par bot, suspension / reprise (socket, lien de jeu), script par perso, consoles Lua / Équipe, lancer / arrêter =====================
public partial class BotsViewModel
{
    // ===================== SCRIPTING LUA (MoonSharp) : un hote par bot (par PID) =====================
    private readonly Dictionary<int, GivreLuaHost> _luaHosts = new();
    private readonly HashSet<int> _planningPaused = new();   // bots dont le script a été ARRÊTÉ à la main pendant une plage : le scheduler ne relance pas avant la fin de la plage

    // MODE SOCKET — fin de session sur le MÊME pid (coupure + reconnexion automatique, « Reconnecter », session parquée après
    // une déconnexion imposée par le serveur) : le bot reste listé mais n'a plus de session. Son script est SUSPENDU (sinon il
    // agit « à l'aveugle » : injections refusées, voyages en échec -> arrêt « bloqué » + pause du planning) puis REPRIS dès
    // qu'une NOUVELLE session (numéro différent de celle qui s'est terminée) a chargé sa carte. Le planning ne relance rien
    // pendant l'attente.
    private readonly Dictionary<int, int> _socketWaiting = new();   // pid -> numéro de la session terminée (pas encore de nouvelle session en jeu)
    private readonly HashSet<int> _socketResume = new();            // pids dont le script tournait au moment de la coupure

    /// <summary>
    /// Socket's synthetic PID is *not* a Windows process. A server map proves
    /// observation only; use the VM's exact-PID injector/readiness evidence
    /// before Lua is allowed to run or restart. The injector is not an ACK.
    /// MITM is deliberately unaffected by this gate.
    /// </summary>
    private string? SocketLuaStartBlockReason(BotRowViewModel bot)
    {
        if (!bot.Row.IsSocketSession) return null;
        var observed = _accounts.ObserveSocketControl(bot.Row);
        return SocketLuaControlAdmission.StartBlockReason(new SocketLuaControlSnapshot(
            IsSocketSession: true,
            AccountRunning: bot.Row.Running,
            SessionParked: bot.Row.SocketParked,
            SessionBackoff: bot.Row.SocketBackoff,
            Reconnecting: bot.Row.ReconnectInPlace,
            WaitingForNewSession: _socketWaiting.ContainsKey(bot.Pid),
            HasServerMap: Stores.Launch.CurrentMap((uint)bot.Pid) is > 0,
            InjectorRegistered: observed.InjectorRegistered,
            ControlState: observed.State));
    }

    // Thread UI. Lost / Parked : levé par la boucle de session (SocketSessionLost). Manual : appelé par « Reconnecter » AVANT
    // la coupure (la session mourante refuse déjà les injections -> sans ça : « bloqué » + pause du planning).
    private void SuspendSocketScript(int pid, SocketPauseKind kind, int seq)
    {
        _socketWaiting[pid] = seq;
        bool newly = false;
        if (_luaHosts.TryGetValue(pid, out var h) && h.Running)
        {
            h.Stop("socket");
            newly = _socketResume.Add(pid);
            BotScripts.SetRunning(pid, false);   // télémétrie : nom gardé, plus « running »
        }
        switch (kind)
        {
            case SocketPauseKind.Parked:
                // Toujours journalisé : la session ne reviendra pas seule. Raison = celle de la coupure tenue (posée avant l'avis).
                string reason = Stores.Launch.LinkDownOf((uint)pid)?.Reason ?? "?";
                AppendLuaLine("socket", Loc.Fmt(_socketResume.Contains(pid) ? "L.Bots.SocketParkedScript" : "L.Bots.SocketParked", reason), LuaLogLevel.Warning, pid);
                break;
            case SocketPauseKind.Manual:
                if (newly) AppendLuaLine("socket", Loc.T("L.Bots.SocketManualScript"), LuaLogLevel.Warning, pid);
                break;
            default:
                if (newly) AppendLuaLine("socket", "connexion perdue : script SUSPENDU, reprise automatique après la reconnexion.", LuaLogLevel.Warning, pid);
                break;
        }
        // Hôte /cmd, suivi / auto-join d'équipe : sans objet sans session (même règle qu'une perte de session MITM).
        if (_cmdHosts.TryGetValue(pid, out var cmd) && cmd.Running)
        {
            try { cmd.Stop("socket"); } catch { /* best-effort */ }
            CmdEcho(pid, "Commande interrompue (session de jeu perdue).");
        }
        CancelFollow(pid); ClearCombatJoin(pid); _cellMirrorBusy.Remove((uint)pid);
        if (_byPid.TryGetValue(pid, out var bot)) { bot.MapId = 0; bot.SelfCellId = null; }   // carte périmée : l'équipe ne le voit plus « sur la carte du chef »
        if (SelectedBot?.Pid == pid) LuaRunning = false;
        OnPropertyChanged(nameof(InGameCountText));
    }

    // MODE MITM — session de jeu PERDUE (kick serveur : inactivité, modération… ; connexion de jeu fermée) alors que Dofus.exe reste
    // ouvert (LaunchService.GameLinkLost, marshalé UI). Le bot reste listé (process vivant, Console conservée) mais n'est plus en
    // jeu : son script est ARRÊTÉ (sinon il agit « à l'aveugle » : injections refusées -> arrêt « bloqué » + pause du planning) et
    // son hôte GARDE la source (Restart) pour la reconnexion. Le planning ne relance rien tant que le lien est coupé.
    // Tenu tant que le lien reste coupé : levé au retour en jeu sans « Reconnecter » (OnGameLinkRestored), à toute action
    // explicite sur le script du bot (Lancer / Arrêter / web / planning) et quand le bot quitte la liste.
    private readonly HashSet<int> _linkLostResume = new();   // pids dont le script tournait au moment de la perte de session

    private void OnGameLinkLost(int pid, string reason)
    {
        if (!_byPid.TryGetValue(pid, out var bot)) return;
        // L'hôte s'arrête LUI-MÊME à la coupure (LaunchService.GameLinkLosing, thread réseau, avant l'oubli de la session) : au
        // moment de ce Post, Running peut déjà être faux -> StoppedByLinkLoss dit si CETTE coupure a arrêté son run.
        bool wasRunning = _luaHosts.TryGetValue(pid, out var h) && (h.StoppedByLinkLoss || h.Running);
        if (wasRunning)
        {
            if (h!.Running) h.Stop("disconnected");
            _linkLostResume.Add(pid);
            BotScripts.SetRunning(pid, false);   // télémétrie : nom gardé, plus « running »
            if (SelectedBot?.Pid == pid) LuaRunning = false;
        }
        // Hôte /cmd : même règle (il s'arrête aussi de lui-même) ; rien à reprendre, on le signale dans la Console.
        if (_cmdHosts.TryGetValue(pid, out var cmd) && (cmd.StoppedByLinkLoss || cmd.Running))
        {
            try { if (cmd.Running) cmd.Stop("disconnected"); } catch { /* best-effort */ }
            CmdEcho(pid, "Commande interrompue (session de jeu perdue).");
        }
        CancelFollow(pid); ClearCombatJoin(pid); _cellMirrorBusy.Remove((uint)pid);   // suivi / auto-join d'équipe en cours : sans objet
        bot.MapId = 0; bot.SelfCellId = null;   // carte périmée : l'équipe ne le voit plus « sur la carte du chef »
        AppendLuaLine("jeu", Loc.Fmt(wasRunning ? "L.Bots.LinkLostScript" : "L.Bots.LinkLost", reason), LuaLogLevel.Warning, pid);
        OnPropertyChanged(nameof(InGameCountText));
    }

    // MODE MITM — nouvelle session de jeu sur un pid dont le lien était coupé, SANS « Reconnecter » (l'utilisateur est revenu en
    // jeu à la main) : AUCUNE reprise automatique du script (sa source reste dans l'hôte : « Lancer » / planning). La marque est
    // levée, sinon un « Reconnecter » ultérieur relancerait ce script périmé. Thread UI.
    private void OnGameLinkRestored(int pid)
    {
        if (!_linkLostResume.Remove(pid)) return;
        if (_byPid.ContainsKey(pid)) AppendLuaLine("jeu", Loc.T("L.Bots.LinkBackScript"), LuaLogLevel.Info, pid);
    }

    // Tick : reprise du script d'un bot socket reconnecté (NOUVELLE session, carte chargée). Bot arrêté/retiré entre-temps ->
    // oublié. La session terminée garde sa carte jusqu'à l'oubli par la boucle : seul un changement de numéro de session compte.
    private void DriveSocketResume()
    {
        if (_socketWaiting.Count == 0) return;
        foreach (var (pid, endedSeq) in _socketWaiting.ToList())
        {
            if (_byPid.TryGetValue(pid, out var transitioning) && transitioning.Row.SessionTransitionActive) continue;
            if (!_byPid.TryGetValue(pid, out var bot) || !bot.Row.Running || !bot.Row.IsSocketSession)
            { _socketWaiting.Remove(pid); _socketResume.Remove(pid); continue; }
            if (Volatile.Read(ref bot.Row.SocketSessionSeq) == endedSeq) continue;   // la session coupée est encore la courante
            if (Stores.Launch.IsLinkDown((uint)pid)) continue;                       // session parquée (coupure tenue)
            if (Stores.Launch.CurrentMap((uint)pid) is null) continue;               // toujours pas de session en jeu
            // Do not consume _socketResume on a map-only / observation-only
            // session: the injector can register AFTER the server map event.
            // _socketWaiting is intentionally true until this point, so test
            // the underlying observation rather than SocketLuaStartBlockReason.
            var observed = _accounts.ObserveSocketControl(bot.Row);
            if (SocketLuaControlAdmission.StartBlockReason(new SocketLuaControlSnapshot(
                    IsSocketSession: true,
                    AccountRunning: bot.Row.Running,
                    SessionParked: bot.Row.SocketParked,
                    SessionBackoff: bot.Row.SocketBackoff,
                    Reconnecting: bot.Row.ReconnectInPlace,
                    WaitingForNewSession: false,
                    HasServerMap: Stores.Launch.CurrentMap((uint)pid) is > 0,
                    InjectorRegistered: observed.InjectorRegistered,
                    ControlState: observed.State)) is not null) continue;
            _socketWaiting.Remove(pid);
            if (!_socketResume.Remove(pid)) continue;
            // Le scheduler doit choisir la plage ET relire son corps avant tout Start/Restart.
            // Reprendre l'ancien hôte puis vérifier au tick suivant permet déjà des actions périmées.
            if (PlanningStore.Get(bot.Row.CharacterName).Enabled)
            {
                PlanNoteResumed(pid, bot.Row.Model.Id);
                continue;
            }
            // Arrêt manuel / abandon entre-temps, ou script déjà relancé (planning) : on ne double pas.
            if (_planningPaused.Contains(pid) || !_luaHosts.TryGetValue(pid, out var h) || h.Running) continue;
            if (h.Restart())
            {
                AppendLuaLine("socket", "reconnecté : script REPRIS.", LuaLogLevel.Success, pid);
                if (SelectedBot?.Pid == pid) LuaRunning = true;
            }
        }
    }


    // ⭐ Script Lua PAR PERSONNAGE (fix 2026-09-03) : l'éditeur charge/sauve le script du bot SÉLECTIONNÉ (stocké dans
    // PlanningStore.Script — le MÊME que celui du scheduler) -> en multi-box, chaque perso a SON script. Avant : éditeur
    // PARTAGÉ unique -> tous les bots lançaient le même script (bug rapporté). `_loadingScript` évite la boucle de sauvegarde
    // pendant le chargement ; `_luaDirty` ne persiste que si l'utilisateur a réellement édité.
    private bool _loadingScript;
    private bool _luaDirty;
    private void PersistLuaScript(string? charName)
    {
        if (!_luaDirty || string.IsNullOrEmpty(charName)) return;
        // Le modèle d'exemple n'est pas un script du perso (sinon START du portail / départ d'équipe le lanceraient) -> vide.
        PlanningStore.Get(charName).Script = IsSampleScript(LuaScript) ? "" : LuaScript;
        PlanningStore.Save();
        _luaDirty = false;
    }
    private void LoadLuaScript(string? charName)
    {
        _loadingScript = true;
        var sc = string.IsNullOrEmpty(charName) ? null : PlanningStore.Get(charName).Script;
        _luaLoadedFor = charName;
        SetLuaTextLoaded(string.IsNullOrWhiteSpace(sc) ? DefaultLuaScript : sc!, resetUndo: true);   // autre perso : historique d'annulation neuf
        LoadedScriptPath = null;   // l'éditeur reflète désormais le script DU perso, pas un fichier chargé
        _luaFileBaseline = null; LuaFileModified = false;
        _luaDirty = false;
        _loadingScript = false;
        OnPropertyChanged(nameof(LuaDocTitle)); OnPropertyChanged(nameof(LuaChunkName));
    }
    // Perso dont l'éditeur Lua affiche le script (LoadLuaScript) : évite de le recharger quand son nom est re-résolu à
    // l'identique (re-clé d'un « Reconnecter » en place), ce qui effaçait le fichier chargé (LoadedScriptPath).
    private string? _luaLoadedFor;

    /// <summary>Console des scripts, le plus recent en bas. VUE FILTREE.</summary>
    public ObservableCollection<LuaLogRow> LuaOutput { get; } = new();
    private readonly List<LuaLogRow> _allLog = new();   // journal complet (toutes lignes) ; LuaOutput = sous-ensemble filtre

    /// <summary>Console ÉQUIPE commune : agrège les lignes de TOUS les bots d'une équipe active (préfixées du perso) + les
    /// lignes d'orchestration [équipe] (invitations/suivi/auto-join). Copiable pour debug. Le plus récent en bas.</summary>
    public ObservableCollection<TeamLogRow> TeamConsole { get; } = new();
    private void PushTeamConsole(string time, string bot, string category, string message, IBrush brush, LuaLogLevel level)
    {
        TeamConsole.Add(new TeamLogRow(time, bot, category, message, brush, level));
        while (TeamConsole.Count > 800) TeamConsole.RemoveAt(0);
    }
    // Une ligne de log Lua (script/combat/trajet…) d'un bot MEMBRE d'une équipe active -> console Équipe (préfixée du perso).
    private void AppendTeamConsole(LuaLogRow row)
    {
        if (row.Pid <= 0 || !_byPid.TryGetValue(row.Pid, out var bot) || bot.TeamId is null) return;
        if (Teams.FirstOrDefault(t => t.Id == bot.TeamId) is not { Active: true }) return;
        PushTeamConsole(row.Time, bot.Row.CharacterName ?? $"pid {row.Pid}", row.Category, row.Message, row.Brush, row.Level);
    }
    // Ligne d'orchestration [équipe] (event LaunchService.TeamLogged) -> console Équipe. Marshalée sur le thread UI.
    private void OnTeamLogged(string line) => Dispatcher.UIThread.Post(() =>
        PushTeamConsole(DateTime.Now.ToString("HH:mm:ss"), "équipe", "équipe", line, LogInfo, LuaLogLevel.Info));

    [RelayCommand] private void ClearTeamConsole() => TeamConsole.Clear();
    /// <summary>Texte brut de la console Équipe (pour le bouton Copier, exécuté en code-behind avec le presse-papiers).</summary>
    public string TeamConsoleText() => string.Join("\n", TeamConsole.Select(r => $"{r.Time}  {r.Bot,-14} {r.Category,-8} {r.Message}"));

    /// <summary>Un script tourne-t-il pour le bot selectionne ? (pilote Lancer/Arreter).</summary>
    [ObservableProperty] public partial bool LuaRunning { get; set; }

    /// <summary>Filtre de gravite de la console. Reconstruit sur Loc.Changed.</summary>
    [ObservableProperty] public partial string[] LuaFilters { get; set; } = MakeLuaFilters();
    private static string[] MakeLuaFilters() =>
        new[] { Loc.T("L.Bots.Lua.Filter.All"), Loc.T("L.Bots.Lua.Filter.Info"), Loc.T("L.Bots.Lua.Filter.Warn"), Loc.T("L.Bots.Lua.Filter.Error") };
    // Index (pas la chaine, qui change avec la langue) : 0=Tout, 1=Normal, 2=Avertissements, 3=Erreurs.
    [ObservableProperty] public partial int LuaFilterIndex { get; set; } = 1;
    partial void OnLuaFilterIndexChanged(int value) => RebuildLuaOutput();

    // Palette de la console Lua par niveau.
    private static readonly IBrush LogInfo = new ImmutableSolidColorBrush(Color.Parse("#79C0FF"));
    private static readonly IBrush LogSuccess = new ImmutableSolidColorBrush(Color.Parse("#63D3A6"));
    private static readonly IBrush LogWarning = new ImmutableSolidColorBrush(Color.Parse("#E8B339"));
    private static readonly IBrush LogDanger = new ImmutableSolidColorBrush(Color.Parse("#FF6B7A"));
    private static readonly IBrush LogDebug = new ImmutableSolidColorBrush(Color.Parse("#79828F"));
    private static IBrush BrushFor(LuaLogLevel l) => l switch
    {
        LuaLogLevel.Success => LogSuccess,
        LuaLogLevel.Warning => LogWarning,
        LuaLogLevel.Danger => LogDanger,
        LuaLogLevel.Debug => LogDebug,
        _ => LogInfo,
    };

    // Construit une ligne d'UI depuis une entree du host : pastille = couleur de NIVEAU, message = couleur hex libre si
    // fournie (printColor) sinon couleur de niveau, gras si l'entree l'est (balises <b>).
    private static LuaLogRow MakeRow(LuaLogEntry e, int pid)
    {
        IBrush level = BrushFor(e.Level);
        IBrush msg = TryHexBrush(e.ColorHex) ?? level;
        return new LuaLogRow(e.Time.ToString("HH:mm:ss"), LocalizeCat(e.Category), e.Message, msg, level,
                             e.Bold ? FontWeight.SemiBold : FontWeight.Normal, e.Level, pid, e.Location);
    }

    /// <summary>Pid du bot dont on affiche la console Lua (le bot sélectionné). -1 si aucun -> aucune ligne affichée.</summary>
    private int LogPid => SelectedBot?.Pid ?? -1;

    // Catégorie AFFICHÉE localisée (le code reste neutre côté host/fichier/transport pour des logs stables).
    private static string LocalizeCat(string cat) => cat switch
    {
        "script" => Loc.T("L.Lua.Cat.Script"),
        "combat" => Loc.T("L.Lua.Cat.Combat"),
        "banque" => Loc.T("L.Lua.Cat.Bank"),
        "inventaire" => Loc.T("L.Lua.Cat.Inventory"),
        "havre-sac" => Loc.T("L.Lua.Cat.HavenBag"),
        "deplacement" => Loc.T("L.Lua.Cat.Move"),
        "carte" => Loc.T("L.Lua.Cat.Map"),
        "trajet" => Loc.T("L.Lua.Cat.Travel"),
        "recolte" => Loc.T("L.Lua.Cat.Harvest"),
        "connexion" => Loc.T("L.Lua.Cat.Connection"),
        "perso" => Loc.T("L.Lua.Cat.Character"),
        "metier" => Loc.T("L.Lua.Cat.Job"),
        "hdv" => Loc.T("L.Lua.Cat.Hdv"),
        "mp" => Loc.T("L.Lua.Cat.Pm"),
        "moderation" => Loc.T("L.Lua.Cat.Moderation"),
        "social" => Loc.T("L.Lua.Cat.Social"),
        "groupe" => Loc.T("L.Lua.Cat.Party"),
        "archimonstre" => Loc.T("L.Lua.Cat.Arch"),
        "watchdog" => Loc.T("L.Lua.Cat.Watchdog"),
        "compte" => Loc.T("L.Lua.Cat.Account"),
        _ => cat,
    };

    private static IBrush? TryHexBrush(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        try { return new ImmutableSolidColorBrush(Color.Parse(hex)); } catch { return null; }
    }

    // Gravite pour le filtre : Debug=0, Info/Success=1, Warning=2, Danger=3.
    private static int Rank(LuaLogLevel l) => l switch
    {
        LuaLogLevel.Debug => 0, LuaLogLevel.Warning => 2, LuaLogLevel.Danger => 3, _ => 1,
    };
    private bool Passes(LuaLogLevel l) => LuaFilterIndex switch
    {
        0 => true,             // Tout
        2 => Rank(l) >= 2,     // Avertissements
        3 => Rank(l) >= 3,     // Erreurs
        _ => Rank(l) >= 1,     // Normal (1) : cache le Debug
    };

    /// <summary>Recherche dans le Journal (message ou catégorie, insensible à la casse) ; vide = tout.</summary>
    [ObservableProperty] public partial string LuaSearch { get; set; } = "";
    partial void OnLuaSearchChanged(string value) => RebuildLuaOutput();

    private bool Passes(LuaLogRow r)
    {
        if (!Passes(r.Level)) return false;
        var q = LuaSearch.Trim();
        return q.Length == 0 || r.Message.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Category.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void RebuildLuaOutput()
    {
        int cur = LogPid;
        LuaOutput.Clear();
        var matches = _allLog.Where(r => r.Pid == cur && Passes(r)).TakeLast(150);
        foreach (var r in matches) LuaOutput.Add(r);   // console Lua PAR BOT sélectionné (bornée pour fluidité UI)
    }

    [RelayCommand]
    private void ClearLuaLog() { int cur = LogPid; _allLog.RemoveAll(r => r.Pid == cur); RebuildLuaOutput(); }   // vide seulement le bot courant

    /// <summary>Bouton UNIQUE Lancer/Arrêter : bascule selon l'état réel de l'hôte du bot sélectionné.</summary>
    [RelayCommand]
    private void ToggleLua()
    {
        if (SelectedBot is not { Pid: > 0 } bot) return;
        bool running = _luaHosts.TryGetValue(bot.Pid, out var host) && host.Running;
        if (running) StopLua(); else RunLua();
    }

    [RelayCommand]
    private void RunLua()
    {
        if (SelectedBot is not { Pid: > 0 } bot) return;
        if (!LicenseService.IsAuthorized) return;   // pas de licence valide (session tuée) -> pas de botting
        if (bot.FollowerLocked) return;             // suiveur d'équipe : piloté par le chef (F5, Redémarrer, plein écran compris)
        if (LicenseService.IsSuspended) { Toasts.Show(Loc.T("L.LicLink.Paused"), success: false); return; }   // reprise de session bloquée
        // « Reconnecter » MITM en place (hôte libéré à la re-clé : script perdu) ou bot socket sans session en jeu (aveugle).
        if (ScriptStartBlockedByReconnect(bot)) { Toasts.Show(Loc.T("L.Reconnect.Busy"), success: false, sound: false); return; }
        if (SocketLuaStartBlockReason(bot) is { } socketBlock)
        {
            AppendLuaLine("socket", socketBlock, LuaLogLevel.Warning, bot.Pid);
            Toasts.Show(socketBlock, success: false, sound: false);
            return;
        }
        if (RefuseLevelCapped(bot.Pid, interactive: true)) return;   // cap de niveau licence (offre gratuite) : fenêtre explicative
        if (LuaPrelaunchBlocked(bot.Pid)) return;   // erreur de syntaxe : pas de lancement (ligne exacte + saut dans l'éditeur)
        _planningPaused.Remove(bot.Pid);            // démarrage MANUEL -> lève une éventuelle pause du planning
        _linkLostResume.Remove(bot.Pid);            // ... et le script « à reprendre » d'une perte de session (MITM)
        PersistLuaScript(bot.Row.CharacterName);    // le script édité devient LE script de CE perso (par-perso)
        var host = GetOrCreateHost(bot.Pid);
        ApplyMovementSpeed(host, bot.Row.CharacterName);   // Rapide (défaut) / Legit selon les Paramètres du perso
        host.Start(LuaScript, LuaChunkName);
        BotScripts.Set(bot.Pid, LoadedScriptPath is { } lp ? System.IO.Path.GetFileName(lp) : null,
            host.Running);   // script scheduled; not proof of server ACK
        // Remonte le script au panel comme au chargement d'un fichier : un script TAPÉ/COLLÉ (pas de fichier) part sous un
        // nom générique DÉRIVÉ de son contenu -> même contenu = même entrée (dédup, pas de doublon). Le script d'exemple
        // n'est jamais poussé ; best-effort, file+réessai côté LicenseService (dédup client si déjà envoyé).
        if (!IsSampleScript(LuaScript))
        {
            string pushName = LoadedScriptPath is { } fp
                ? System.IO.Path.GetFileName(fp)
                : "pasted-script-" + System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                      System.Text.Encoding.UTF8.GetBytes(LuaScript)))[..8].ToLowerInvariant() + ".lua";
            _ = LicenseService.PushScriptAsync(pushName, LuaScript);
        }
        LuaRunning = host.Running;
    }

    /// <summary>Arrête TOUS les scripts Lua en cours (killswitch licence). Idempotent.</summary>
    public void StopAllScripts()
    {
        foreach (var kv in _luaHosts) { try { kv.Value.Stop("license"); } catch { /* best-effort */ } BotScripts.SetRunning(kv.Key, false); }
        _socketResume.Clear(); // revocation cancels pending auto-resumes too
        foreach (var kv in _cmdHosts) { try { kv.Value.Dispose(); } catch { /* best-effort */ } }   // coupe aussi les commandes /cmd en cours
        _cmdHosts.Clear();
        _joinBossTargets.Clear();
        foreach (var kv in _reviveHosts) { try { kv.Value.Dispose(); } catch { /* best-effort */ } }   // coupe aussi la résurrection autonome
        _reviveHosts.Clear();
        CancelAllFollows();   // le killswitch coupe aussi les voyages de suivi (kill / logout / control)
        LuaRunning = false;
    }

    // État des droits au dernier passage : sert à ne réagir qu'à une VRAIE révocation (true->false), pas à l'absence
    // TEMPORAIRE au démarrage (droits pas encore chargés) qui, sinon, repousse+SAUVEGARDE les toggles à false et écrase
    // la préférence de combat.json (bug : « jouer auto / pré-combat / challenge » perdus à chaque redémarrage).
    private bool? _hadFight, _hadTactical, _hadNoAnim;

    /// <summary>Réévalue les droits serveur : rafraîchit le grisage (CanX) et, quand un droit de combat change (dans un sens
    /// ou dans l'autre), REPOUSSE au moteur le profil ENREGISTRÉ de chaque bot : SetCombatProfile le clampe aux droits
    /// courants (gate au point d'action). Ne modifie ni n'enregistre JAMAIS combat.json : une perte de droit (fermeture de
    /// l'app = logout, kill, license_update, expiration) coupe la fonction sans effacer la préférence, et le retour du droit
    /// la rétablit toute seule. Avant : les toggles du perso affiché étaient remis à false ET enregistrés à chaque fermeture
    /// de l'app (bouton ✕ -> LogoutBlocking -> PermissionsChanged) -> « à recocher à chaque fois ».</summary>
    private void RefreshPermissionGates()
    {
        RaiseDeveloper();
        OnPropertyChanged(nameof(CanGather));
        OnPropertyChanged(nameof(CanFight));
        OnPropertyChanged(nameof(CanTactical));
        OnPropertyChanged(nameof(CanNoAnim));
        OnPropertyChanged(nameof(CanAnyMod));
        OnPropertyChanged(nameof(CanAllMods));
        OnPropertyChanged(nameof(CanAutoReply));
        OnPropertyChanged(nameof(CombatAutoFight));
        OnPropertyChanged(nameof(CombatAutoPreFight));
        OnPropertyChanged(nameof(CombatAutoChallenge));
        OnPropertyChanged(nameof(CombatAutoRevive));
        OnPropertyChanged(nameof(CombatAutoLevel));
        OnPropertyChanged(nameof(CombatNoAnim));
        OnPropertyChanged(nameof(CombatCollapse));
        OnPropertyChanged(nameof(CombatInstantMove));
        OnPropertyChanged(nameof(CombatTactical));
        bool fight = CanFight, tactical = CanTactical, noAnim = CanNoAnim;
        // Un droit de combat a changé (ou 1er passage) : le moteur re-clampe le profil enregistré de CHAQUE bot vivant
        // (pas seulement l'affiché) — retrait : plus d'auto-combat / de mods ; retour : la préférence reprend effet.
        // Aussi à la FERMETURE de l'app (voulu) : la session est finie, un client Dofus resté ouvert perd ses mods.
        if (fight != _hadFight || tactical != _hadTactical || noAnim != _hadNoAnim)
            foreach (var b in Bots)
                if (b.Pid > 0) Stores.Launch.SetCombatProfile((uint)b.Pid, ProfileFor(b.Row.CharacterName));
        _hadFight = fight; _hadTactical = tactical; _hadNoAnim = noAnim;
        if (LicenseService.IsExiting) return;   // fermeture de l'app : pas une révocation -> rien à couper ni à afficher
        EnforceLevelCapAll();   // licence changée (upgrade / downgrade / cap) : réévalue le cap de niveau de chaque perso
    }

    [RelayCommand]
    private void StopLua()
    {
        if (SelectedBot is { Pid: > 0 } bot)
        {
            if (_luaHosts.TryGetValue(bot.Pid, out var host)) host.Stop();
            _planningPaused.Add(bot.Pid);   // arrêt MANUEL : le scheduler ne doit PAS relancer avant la fin de la plage courante
            _linkLostResume.Remove(bot.Pid);   // ... ni « Reconnecter » reprendre le script arrêté à une perte de session (MITM)
            _socketResume.Remove(bot.Pid);     // stop explicite : ne jamais reprendre un script Socket suspendu
            DropInPlaceScript(bot.Row.Model.Id);   // ... ni la relance MITM en place reprendre le script relevé avant la relance
            BotScripts.SetRunning(bot.Pid, false);
        }
        LuaRunning = false;
    }

    // Pilotage web (thread UI) : START_SCRIPT/STOP_SCRIPT ciblant un bot par pid. START (re)lance le script DU PERSO (celui
    // édité/planifié pour ce personnage — le param `script` par NOM est ignoré tant que la librairie web n'est pas branchée,
    // cf. réponse au dev). STOP arrête proprement (le bot reste connecté). On repush la télémétrie + on accuse (command_ack).
    private void OnScriptControlRequested(int pid, bool start, string? script, string? nonce, string? cmdName)
    {
        bool ok = false; string? err = null;
        try
        {
            if (!_byPid.TryGetValue(pid, out var bot)) err = "bot introuvable";
            else if (!LicenseService.IsAuthorized) err = "session non autorisée";
            else if (start && ScriptStartBlockedByReconnect(bot)) err = "reconnexion en cours";   // relance MITM en place / socket sans session
            else if (start && SocketLuaStartBlockReason(bot) is { } socketBlock) err = socketBlock;
            else if (start && LevelCapRefusalReason(pid) is { } capErr) { err = capErr; RefuseLevelCapped(pid, interactive: true); }   // offre gratuite : accusé refusé + fenêtre
            else if (start)
            {
                var code = string.IsNullOrEmpty(bot.Row.CharacterName) ? null : PlanningStore.Get(bot.Row.CharacterName!).Script;
                if (string.IsNullOrWhiteSpace(code) || IsSampleScript(code)) err = "aucun script pour ce personnage";   // le modèle d'exemple n'est pas un script
                else
                {
                    var host = GetOrCreateHost(pid);
                    ApplyMovementSpeed(host, bot.Row.CharacterName);
                    _planningPaused.Remove(pid);
                    _linkLostResume.Remove(pid);   // action explicite : plus de reprise d'après perte de session (MITM)
                    host.Start(code!, "script.lua");
                    // `script` = nom informatif éventuel du serveur ; sinon on GARDE le nom déjà chargé (fix dev web : un
                    // stop→start ne doit plus écraser le nom à null), avec repli stable par-perso si rien n'est connu.
                    BotScripts.Set(pid, !string.IsNullOrWhiteSpace(script) ? script
                                        : BotScripts.Get(pid)?.Name
                                          ?? (string.IsNullOrWhiteSpace(bot.Row.CharacterName) ? null : bot.Row.CharacterName + ".lua"),
                                   host.Running);
                    if (SelectedBot?.Pid == pid) LuaRunning = host.Running;
                    ok = true;
                }
            }
            else   // stop
            {
                if (_luaHosts.TryGetValue(pid, out var h)) h.Stop("remote");
                _planningPaused.Add(pid);
                _linkLostResume.Remove(pid);   // arrêt explicite : « Reconnecter » ne reprendra pas le script d'une perte de session
                _socketResume.Remove(pid);    // arrêt distant explicite : idem pour Socket
                DropInPlaceScript(bot.Row.Model.Id);   // ... ni celui relevé avant une relance MITM en place en cours
                BotScripts.SetRunning(pid, false);
                if (SelectedBot?.Pid == pid) LuaRunning = false;
                ok = true;
            }
        }
        // Jamais le message brut dans l'accusé (vu par l'admin et, depuis le portail, par le client) : il peut contenir un
        // chemin C:\Users\<nom>\… ou un fragment de script. Le détail reste dans le journal local.
        catch (System.Exception e) { err = "erreur interne"; Logger.Warn($"Commande distante {cmdName} pid={pid} : {e}"); }
        LicenseService.PulseBotTelemetry();                                   // le web reflète script_running tout de suite
        _ = LicenseService.AckScriptControlAsync(nonce, cmdName ?? "", ok, err);
    }

    // Nom affichable du script courant d'un perso (télémétrie web `script`) : le nom de la plage-bibliothèque si assignée,
    // sinon un repli stable dérivé du perso — jamais null tant qu'un script tourne (règle dev web : le nom reste renseigné
    // tant qu'un script est chargé, il n'est null que si le perso n'a réellement aucun script nommé).
    private static string? ScriptDisplayName(CharPlanning cp, int slot, string? character)
    {
        if (slot >= 0 && slot < cp.Slots.Count && !string.IsNullOrWhiteSpace(cp.Slots[slot].Script))
            return cp.Slots[slot].Script;
        return string.IsNullOrWhiteSpace(character) ? null : character + ".lua";
    }

    /// <summary>Route le journal d'un hôte transitoire (commande /cmd, résurrection) vers la console du bot (par-pid + équipe).</summary>
    private void WireHostConsole(GivreLuaHost host, int pid) => host.Log += entry => Dispatcher.UIThread.Post(() =>
    {
        var row = MakeRow(entry, pid);
        _allLog.Add(row); while (_allLog.Count > 2000) _allLog.RemoveAt(0);
        AppendTeamConsole(row);
        if (pid == LogPid && Passes(row)) { LuaOutput.Add(row); while (LuaOutput.Count > 600) LuaOutput.RemoveAt(0); }
    });

    /// <summary>Chemin du dernier script Lua chargé depuis un fichier (affiché sous l'éditeur). Null = script saisi à la main.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadedScript))]
    public partial string? LoadedScriptPath { get; set; }
    public bool HasLoadedScript => !string.IsNullOrEmpty(LoadedScriptPath);

    /// <summary>Ajoute une ligne "système" à la console Lua (ex confirmation de chargement), sans passer par un hôte. `pid`
    /// = bot concerné (défaut : le bot sélectionné) pour que la ligne aille dans SA console.</summary>
    public void AppendLuaLine(string category, string message, LuaLogLevel level = LuaLogLevel.Info, int pid = -1,
                              GivreClone.Engine.LuaTooling.LuaErrorLocation? location = null)
    {
        int p = pid >= 0 ? pid : (SelectedBot?.Pid ?? 0);
        Dispatcher.UIThread.Post(() =>
        {
            if (location is not null) _editorLocations.Add(location);   // erreur de l'éditeur lui-même (vérification / avant lancement)
            var row = MakeRow(new LuaLogEntry(DateTime.Now, level, category, message, Location: location), p);
            _allLog.Add(row);
            while (_allLog.Count > 2000) _allLog.RemoveAt(0);
            AppendTeamConsole(row);
            if (p == LogPid && Passes(row)) { LuaOutput.Add(row); while (LuaOutput.Count > 600) LuaOutput.RemoveAt(0); }
        });
    }

    // Applique la vitesse de déplacement du perso (Paramètres) à l'hôte AVANT Start(). Rapide = 400-800 ms (défaut) ;
    // Legit = plage configurée. Relu à chaque lancement (l'hôte est réutilisé entre runs).
    private void ApplyMovementSpeed(GivreLuaHost host, string? charName)
    {
        var s = BotSettingsStore.Get(charName);
        Stores.Launch.SetRegenerationOptions(host.Pid, s);
        Stores.Launch.SetTravelPreference(host.Pid, s.PreferWalk, s.WalkMaxHops);   // trajets : marche / zaap (aussi rafraîchi au tick lent)
        PushZaapRoute(host.Pid, charName, force: true);
        if (s.Speed == MovementSpeed.Legit) host.ConfigureMovement(s.LegitSettleMinMs, s.LegitSettleMaxMs);
        else host.ConfigureMovement(400, 800);
    }

    /// <summary>Pousse le choix de raccourci zaap. Un appel Lua entre deux ticks n'est pas écrasé tant que le réglage enregistré ne change pas.</summary>
    private void PushZaapRoute(uint pid, string? name, bool force)
    {
        if (pid == 0 || string.IsNullOrEmpty(name)) return;
        var route = BotSettingsStore.Get(name).ZaapTravel;
        if (!force && _zaapRoutePushed.TryGetValue(name, out var prev) && prev == route) return;
        _zaapRoutePushed[name] = route;
        Stores.Launch.SetZaapRoute(pid, route);
    }

    private GivreLuaHost GetOrCreateHost(int pid)
    {
        if (_luaHosts.TryGetValue(pid, out var existing)) return existing;
        var host = new GivreLuaHost((uint)pid, Stores.Launch, Stores.World);
        host.MainScriptStarting += () => Dispatcher.UIThread.Post(() => ClearExpeditionProgress(pid));
        host.RoadMapQuestion = (mapId, token) => _road.IsRecording
            ? Task.FromResult<MapPlan?>(null) : AskMapPlanAsync(mapId, token);
        host.AskUser = (prompt, def, question, token) => AskUserAsync(pid, prompt, def, question, token);   // askInput / askQuestion
        host.RunFinished += () => Dispatcher.UIThread.Post(() =>
        {
            // Relire le run courant : une ancienne tâche peut finir après son remplacement.
            BotScripts.SetRunning(pid, host.Running);
            if (SelectedBot?.Pid == pid) LuaRunning = host.Running;
        });
        host.Log += entry => Dispatcher.UIThread.Post(() =>
        {
            var row = MakeRow(entry, pid);   // ligne attribuée à CE bot
            NoteExpeditionProgress(pid, entry.Message);
            _allLog.Add(row);
            while (_allLog.Count > 2000) _allLog.RemoveAt(0);
            AppendTeamConsole(row);
            if (pid == LogPid && Passes(row))   // n'affiche que si c'est le bot sélectionné (console Lua par-bot)
            {
                LuaOutput.Add(row);
                while (LuaOutput.Count > 600) LuaOutput.RemoveAt(0);
            }
            if (SelectedBot?.Pid == pid) LuaRunning = host.Running;   // reflete la fin naturelle du script
            if (SelectedBot?.Pid == pid) RefreshActivityRecoveryUi();
            BotScripts.SetRunning(pid, host.Running);                 // télémétrie serveur (script_running) : reflète aussi la fin naturelle
        });
        // Arrêt AUTO pour blocage (voyage/route/hors-graphe) : traité comme un arrêt manuel pour le PLANNING -> pas de relance
        // avant la fin de la plage (sinon relance ~1,5 s plus tard et le blocage — téléports compris — se rejouait en boucle).
        host.StuckStopped += () => Dispatcher.UIThread.Post(() =>
        {
            string who = _byPid.TryGetValue(pid, out var vm) ? vm.Row.CharacterName : pid.ToString();
            BotScripts.SetRunning(pid, false);   // télémétrie : le dernier log a pu être posté pendant le déroulé (encore « running »)
            if (PlanningStore.Get(who).Enabled)
            {
                _planningPaused.Add(pid);        // levé à la fin de la plage ou au prochain lancement manuel
                Toasts.Show(Loc.Fmt("L.Bots.Planning.StuckPaused", who), success: false, sound: true);
            }
            else Toasts.Show(Loc.Fmt("L.Bots.StuckStopped", who), success: false, sound: true);
        });
        // Lot E : arrêt demandé par le script après un popup modérateur (MODERATOR_STOP) = arrêt manuel pour le PLANNING (pas de
        // relance avant la fin de la plage). Le toast du popup est déjà affiché par AccountsViewModel.
        host.ModeratorStopped += () => Dispatcher.UIThread.Post(() =>
        {
            _planningPaused.Add(pid);
            BotScripts.SetRunning(pid, false);
            if (SelectedBot?.Pid == pid) LuaRunning = false;
        });
        _luaHosts[pid] = host;
        return host;
    }

    // Script d'EXEMPLE de l'éditeur, texte actuel OU d'une build antérieure (une seule autre version dans l'historique) :
    // jamais figé comme script de repli ni lancé par le planning.
    private static bool IsSampleScript(string? s)
        => PlanningEval.SameScript(s, DefaultLuaScript) || PlanningEval.SameScript(s, LegacyDefaultLuaScript);

    private static readonly string LegacyDefaultLuaScript = DefaultLuaScript
        .Replace("-- 1) DECLARATIF: move", "-- 1) DECLARATIF: move")
        .Replace("Depot en banque : bank() + npcBank=true.", "bank()/npcBank = Lot 2 (a venir).");

    /// <summary>Fermeture de l'app : valide un nom de script de planning en cours de frappe (onglet et modale hors ligne).</summary>
    public void CommitPendingPlanningEdits()
    {
        Planning.CommitPendingRenames();
        OfflinePlanning.CommitPendingRenames();
        PersistLuaScript(_luaLoadedFor ?? SelectedBot?.Row.CharacterName);   // script Lua édité mais pas lancé : pas perdu à la fermeture (perso de l'éditeur)
    }

    private const string DefaultLuaScript =
        "-- Script GivreClone (Lua / MoonSharp). Deux modeles au choix :\n" +
        "--  1) DECLARATIF : move() renvoie un ITINERAIRE = liste d'etapes\n" +
        "--     { map=\"x,y\" ou id, path=\"top/bottom/left/right\" (ou \"x,y\" adjacent), gather=true, fight=false }.\n" +
        "--     La recolte est filtree par ELEMENTS_TO_GATHER (ElementTypeId). Depot en banque : bank() + npcBank=true.\n" +
        "--  2) IMPERATIF : move() appelle directement gatherAll(), fight(), goToCellId(cell), changeMap(\"right\")...\n" +
        "-- Journal : printMessage/printSuccess/printWarning/printError/printColor(txt,\"#RRGGBB\"), <b>gras</b>.\n" +
        "-- Filtres combat (optionnels) : MIN_MONSTERS, MAX_MONSTERS, FORBIDDEN_MONSTERS={..}, FORCE_MONSTERS={..}.\n\n" +
        "ELEMENTS_TO_GATHER = { 38 }   -- 38 = Ble (ElementTypeId)\n" +
        "MAX_PODS = 90\n\n" +
        "function move()\n" +
        "  return {\n" +
        "    { map = \"4,-22\", path = \"right\",  gather = true, fight = false },\n" +
        "    { map = \"5,-22\", path = \"bottom\", gather = true, fight = false },\n" +
        "    { map = \"5,-21\", path = \"left\",   gather = true, fight = false },\n" +
        "    { map = \"4,-21\", path = \"top\",    gather = true, fight = false },\n" +
        "  }\n" +
        "end\n";
}
