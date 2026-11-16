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

// ===================== Lancement / arrêt des comptes : Dofus.exe, planning, « Reconnecter », relance en place, route réseau, LaunchCoreAsync, surveillance du client, Lancer / Arrêter tout =====================
public partial class AccountsViewModel
{
    // Garantit qu'on connaît un Dofus.exe EXISTANT avant de lancer. Chemin effectif = custom persisté (s'il existe)
    // sinon auto-détection (release.json Zaap / dossier standard). S'il n'existe pas -> sélecteur natif "localise
    // Dofus.exe" ; le chemin choisi est enregistré (AppConfig) pour tous les lancements suivants. Renvoie false si
    // l'utilisateur annule (aucun chemin valide) -> le lancement est abandonné proprement. Internal : pré-requis de « Reconnecter »
    // (onglet Bots), vérifié AVANT de couper la session.
    internal static async Task<bool> EnsureDofusExeAsync()
    {
        var path = GivreLauncher.Core.Launch.GameLauncher.ResolveDofusPath(AppConfig.Current.DofusExePath);
        if (System.IO.File.Exists(path)) return true;

        var picked = await Dialogs.PickFileAsync(Loc.T("L.Dofus.PickTitle"), "Dofus.exe", "Dofus.exe", "*.exe").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(picked) || !System.IO.File.Exists(picked))
        {
            AudioService.PlayError();
            Toasts.Show(Loc.T("L.Dofus.NotSelected"), success: false);
            return false;
        }
        AppConfig.Current.DofusExePath = picked!;
        AppConfig.Save();
        Toasts.Show(Loc.T("L.Dofus.Saved"), success: true);
        return true;
    }

    [RelayCommand]
    private async Task LaunchAsync(AccountRow row)
    {
        if (row.Running) return;
        // Dofus.exe trouvable ? Sinon, au lieu d'un échec sec « introuvable à … », on ouvre un sélecteur pour que
        // l'utilisateur localise SON Dofus.exe (installation hors emplacement standard), puis on le mémorise pour les
        // fois d'après. S'il annule -> abandon propre (pas de tentative de lancement vouée à l'échec) ET mode inchangé.
        if (!await EnsureDofusExeAsync()) return;
        if (row.Running) return;   // lancé entre-temps (planning…) pendant le sélecteur : ni relance ni changement de mode
        // Bouton « Lancer » = MITM, quel que soit le mode mémorisé (manualMitm : pas d'aiguillage Socket). Il ne devient le mode
        // MÉMORISÉ du compte (planning, Lancer tout, équipe, relance) que si le lancement passe TOUTES ses gardes : posé DANS
        // LaunchCoreAsync — un avertissement mapping annulé, un refus multi/route laissent le mode tel quel.
        // Course login->lancement : le mapping est STREAMÉ après le login (async). Si on lance trop vite il n'est pas
        // encore là -> au lieu d'un « manquant » sec, on affiche un modal de chargement et on ATTEND le stream. On est
        // sûr que le protocole est bien présent avant d'agir. (Sans session / échec durable -> l'avertissement reprend.)
        if (MainViewModel.Instance is { } mvm) await mvm.EnsureMappingReadyAsync();
        var missing = MappingCatalog.MissingEssentials();
        if (missing.Count > 0) { WarnMappingMissing(missing, () => _ = LaunchCoreAsync(row, manualMitm: true)); return; }
        await LaunchCoreAsync(row, manualMitm: true);
    }

    // Avertit que des fichiers de mapping ESSENTIELS manquent/incomplets (le bot ne décodera pas les paquets) ;
    // onProceed n'est appelé que si l'utilisateur choisit de lancer quand même.
    private static void WarnMappingMissing(IReadOnlyList<string> missing, Action onProceed) =>
        Dialogs.Confirm(
            Loc.T("L.Mapping.MissingTitle"),
            Loc.Fmt("L.Mapping.MissingBody", missing.Count),
            onProceed,
            okText: Loc.T("L.Mapping.LaunchAnyway"),
            danger: true);

    /// <summary>Lance un compte pour l'onglet Équipe : le fait aller DIRECTEMENT en jeu (AutoConnectType=2, quelle que soit
    /// la préférence du compte) afin qu'il soit tout de suite invitable, via SON proxy (défini dans « Mon compte », PAR
    /// compte). Réutilise TOUT le pipeline de lancement (résolution proxy, modal, attente du mapping). No-op si déjà lancé.
    /// Renvoie true si le client a bien démarré.</summary>
    public async Task<bool> LaunchInGameForTeamAsync(AccountRow row, string? character = null)
    {
        if (row.Running) return true;
        // Compte en mode Socket : aucun Dofus.exe requis (toujours direct en jeu, sur `character` s'il est donné), le
        // mapping reste indispensable.
        if (row.Model.Mode == ConnectionMode.Socket)
        {
            if (MainViewModel.Instance is { } mv) await mv.EnsureMappingReadyAsync();
            return StartSocketCore(row, unattendedShield: null, character: character);
        }
        if (!await EnsureDofusExeAsync()) return false;
        if (MainViewModel.Instance is { } mvm) await mvm.EnsureMappingReadyAsync();   // attend le mapping streamé (comme le bouton Lancer)
        // An explicit character must reach the selection screen; direct login selects the last played character first.
        return await LaunchCoreAsync(row, autoConnect: string.IsNullOrWhiteSpace(character) ? 2 : 1, character: character);
    }

    // ---------- Planning « vrai planning » (connexion/déconnexion auto par plage) ----------

    /// <summary>Arrêt demandé par l'UTILISATEUR (Arrêter / Arrêter tout / déconnexion web / révocation) : le planning ne
    /// relance pas ce compte avant la fin de la plage en cours.</summary>
    public event Action<AccountRow>? UserStopped;
    /// <summary>Le client d'un compte est tombé de façon INATTENDUE (crash, fermeture externe) : alimente l'évènement « crashed » des superviseurs.</summary>
    public event Action<AccountRow>? ClientCrashed;

    /// <summary>Fourni par le scheduler de planning, appelé quand un client meurt de façon inattendue : renvoie vrai si CE
    /// compte a été connecté par le planning (ou l'est en ce moment) — le planning PREND alors le crash en charge (remet son
    /// état à zéro, relance lui-même dans la plage avec son budget) et la relance auto générique ne s'applique pas.</summary>
    public Func<AccountRow, bool>? PlanningClaimsCrash { get; set; }

    /// <summary>MODE SOCKET : la session d'un bot s'est terminée, le bot reste listé sur le MÊME pid (thread UI) : (pid, genre,
    /// numéro de la session terminée). Lost = reconnexion automatique programmée ; Parked = déconnexion imposée par le serveur,
    /// session parquée jusqu'à « Reconnecter ». L'onglet Bots suspend le script du bot et le reprend quand une NOUVELLE session
    /// a chargé sa carte. (Coupure demandée par « Reconnecter » : l'onglet Bots suspend lui-même le script AVANT la coupure.)</summary>
    public event Action<int, SocketPauseKind, int>? SocketSessionLost;

    /// <summary>« Reconnecter » MITM en place : la ligne du compte est passée de l'ancien pid au nouveau (ligne, ancien, nouveau).
    /// Thread UI, synchrone, juste après la publication du nouveau pid : l'onglet Bots y rattache la MÊME entrée.</summary>
    public event Action<AccountRow, int, int>? PidRekeyed;

    /// <summary>Dofus.exe trouvable SANS demander (le lancement planifié ne peut pas ouvrir de sélecteur de fichier).</summary>
    public static bool DofusExeKnown()
        => System.IO.File.Exists(GivreLauncher.Core.Launch.GameLauncher.ResolveDofusPath(AppConfig.Current.DofusExePath));

    /// <summary>Lancement SANS SURVEILLANCE pour le planning : direct en jeu (dernier perso joué), aucun modal (ni lancement,
    /// ni sélecteur Dofus.exe, ni code Bouclier -> échec propre si un code est exigé). Même pipeline que Lancer sinon.
    /// <c>NeedsShieldCode</c> : Ankama a exigé un code du Bouclier (inutile de réessayer sans l'utilisateur : chaque essai
    /// lui renverrait un e-mail de code).</summary>
    public async Task<(bool Ok, bool NeedsShieldCode)> LaunchForPlanningAsync(AccountRow row, string? character = null)
    {
        if (row.Running) return (true, false);
        // Le planning peut démarrer dès l'ouverture de l'application, avant que le mapping licence ait fini
        // d'arriver. Le bouton Socket et « Lancer tout » l'attendent déjà ; le planning doit faire de même,
        // sans ouvrir le modal interactif d'EnsureMappingReadyAsync pendant une plage sans surveillance.
        if (row.Model.Mode == ConnectionMode.Socket && MappingCatalog.GetEffective() is null)
            await MappingCatalog.EnsureFetchedAsync().ConfigureAwait(true);
        if (row.Running) return (true, false); // un autre déclencheur a lancé ce compte pendant l'attente
        var shield = new NoShieldPrompt();
        // character : perso planifié -> en mode Socket il est sélectionné DIRECTEMENT (le MITM ouvre le choix du personnage).
        bool ok = await LaunchCoreAsync(row, autoConnect: 2, unattendedShield: shield, character: character).ConfigureAwait(true);
        return (ok, shield.Asked);
    }

    /// <summary>Le compte se lance en mode Socket (mode mémorisé) : pas de Dofus.exe requis pour un lancement automatique.</summary>
    public static bool UsesSocket(AccountRow row) => row.Model.Mode == ConnectionMode.Socket;

    /// <summary>Déconnexion décidée par le planning (fin de plage, perso inattendu, délai dépassé) : même teardown qu'Arrêter,
    /// SANS l'événement <see cref="UserStopped"/> ; <paramref name="status"/> remplace le statut « inactif » de la ligne.</summary>
    public void StopForPlanning(AccountRow row, string? status = null)
    {
        if (!row.Running) return;
        StopCore(row);
        if (!string.IsNullOrEmpty(status)) row.Status = status!;
    }

    // ---------- « Reconnecter » : servi par l'onglet Bots (pré-requis, arrêt, relance surveillée, reprise du script) ----------

    /// <summary>Fourni par l'onglet Bots : « Reconnecter » demandé depuis une carte de compte.</summary>
    public Func<AccountRow, Task>? ReconnectRequested { get; set; }

    [RelayCommand]
    private Task ReconnectAsync(AccountRow? row) => row is not null && ReconnectRequested is { } rq ? rq(row) : Task.CompletedTask;

    /// <summary>« Reconnecter » : lancement SURVEILLÉ direct en jeu (modal de lancement, code du Bouclier possible) ;
    /// <paramref name="character"/> = perso resélectionné en Socket (sélection explicite aussi en MITM). Aucun await avant
    /// le lancement proprement dit (Dofus.exe / mapping vérifiés au clic) : LaunchGen est incrémenté de façon synchrone. Mêmes
    /// gardes que Lancer : multi, route (fail-closed), droit « socket », mapping (socket).</summary>
    public Task<bool> LaunchForReconnectAsync(AccountRow row, string? character)
        => LaunchCoreAsync(row, autoConnect: 2, character: character);

    /// <summary>Pré-requis de route d'un lancement (droits adaptateur/proxy + proxy joignable), vérifié AVANT de couper une
    /// session : false = refusé (toast affiché).</summary>
    internal static bool RouteReady(AccountRow row) => TryResolveRoute(row, out _, out _) && TryResolveGameRoute(row, out _, out _);

    /// <summary>Droit « multi » pour (re)lancer ce compte (le 1er compte en ligne est libre).</summary>
    internal bool MultiAllowedFor(AccountRow row) => MultiAllowed(row);

    /// <summary>SOCKET en attente (backoff de la reconnexion automatique, ou session parquée) : « Reconnecter » la réveille,
    /// départ IMMÉDIAT. Renvoie le numéro de la session terminée, -1 = pas (plus) en attente. Thread UI.</summary>
    private static int WakeSocketReconnect(AccountRow row)
    {
        if (!row.Running || !row.IsSocketSession || !(row.SocketBackoff || row.SocketParked)) return -1;
        if (Volatile.Read(ref row.SocketWake) is not { } w || !w.TrySetResult(true)) return -1;
        return w.Task.AsyncState is int seq ? seq : Volatile.Read(ref row.SocketSessionSeq);
    }

    /// <summary>« Reconnecter » d'un bot SOCKET, SUR PLACE (même pid, entrée Bots gardée) et SANS délai : en attente (backoff /
    /// parquée) -> réveil immédiat ; session vivante -> <paramref name="beforeCut"/> (numéro de la session : l'onglet Bots y
    /// suspend le script) puis coupure de CETTE session, la boucle se reconnecte aussitôt (seul le 2e essai « session
    /// précédente encore tenue » de SocketProbe peut attendre). Renvoie le numéro de la session coupée / terminée, -1 = rien à
    /// couper ni à réveiller (connexion en cours, autre session que celle confirmée, bot arrêté). <paramref name="expectSeq"/> :
    /// numéro de la session confirmée par l'utilisateur (-1 = pas d'épinglage). Thread UI.</summary>
    public int ReconnectSocketNow(AccountRow row, int expectSeq, Action<int>? beforeCut)
    {
        if (row.SessionTransitionActive) return -1;
        if (!row.Running || !row.IsSocketSession || row.Pid is not int pid) return -1;
        if (row.SocketBackoff || row.SocketParked)
        {
            bool parked = row.SocketParked;
            int ended = WakeSocketReconnect(row);
            if (ended >= 0)
                SocketLog.Info((uint)pid, parked ? $"« Reconnecter » : réveil de la session parquée (n°{ended}) — reconnexion immédiate…"
                                                 : $"« Reconnecter » : attente de la reconnexion automatique écourtée (session n°{ended}) — départ immédiat…");
            else SocketLog.Info((uint)pid, "« Reconnecter » : l'attente vient de se terminer — rien à réveiller.");
            return ended;
        }
        var s = Volatile.Read(ref row.SocketSession);
        if (s is null)
        {
            SocketLog.Info((uint)pid, "« Reconnecter » : aucune session vivante à couper (fin de session / reconnexion en cours) — ignoré.");
            return -1;
        }
        // Coupure ÉPINGLÉE : la session confirmée a été remplacée pendant la confirmation (coupure + reconnexion automatique…)
        // -> on ne coupe pas la suivante, qui se connecte peut-être encore.
        if (expectSeq >= 0 && s.Seq != expectSeq)
        {
            SocketLog.Info((uint)pid, $"« Reconnecter » : la session confirmée (n°{expectSeq}) n'est plus la courante (n°{s.Seq}) — rien n'est coupé.");
            return -1;
        }
        // Session pas (encore) en jeu (auth HAAPI, attente « session précédente encore tenue »…) : la couper ne ferait que retarder.
        if (_launch.CurrentMap((uint)pid) is null)
        {
            SocketLog.Info((uint)pid, $"« Reconnecter » : session n°{s.Seq} pas encore en jeu (connexion en cours) — rien n'est coupé.");
            return -1;
        }
        if (Interlocked.CompareExchange(ref s.State, 1, 0) != 0)
        {
            SocketLog.Info((uint)pid, "« Reconnecter » : aucune session vivante à couper (fin de session / reconnexion en cours) — ignoré.");
            return -1;
        }
        try { beforeCut?.Invoke(s.Seq); } catch (Exception e) { SocketLog.Info((uint)pid, $"« Reconnecter » : suspension du script : {e.Message}"); }
        SocketLog.Info((uint)pid, $"« Reconnecter » : coupure de la session n°{s.Seq} — reconnexion IMMÉDIATE (même pid)…");
        try { s.Cts.Cancel(); } catch (ObjectDisposedException) { /* session déjà close : la boucle voit State=1 */ }
        return s.Seq;
    }

    /// <summary>« Reconnecter » MITM EN PLACE : relance le client du compte SANS le sortir de l'onglet Bots (entrée re-clée
    /// sur le pid du nouveau client, <see cref="PidRekeyed"/>). Toutes les gardes d'un lancement (multi, route, Bouclier
    /// surveillé, LaunchGen) ; aucun modal de lancement (l'entrée Bots montre les étapes). <paramref name="onCommit"/> : appelé
    /// au point de validation (gardes passées, AVANT la mise à mort de l'ancien client) — l'onglet Bots y arrête les hôtes du
    /// pid. false sans validation = refusé (toast déjà affiché). Thread UI.</summary>
    public Task<bool> RelaunchInPlaceAsync(AccountRow row, Action onCommit)
    {
        if (row.Running && !row.IsSocketSession && row.Pid is int p && p > 0 && !_relaunching.ContainsKey(p))
            return LaunchCoreAsync(row, autoConnect: 2, relaunchPid: p, onCommit: onCommit);
        return Task.FromResult(false);
    }

    // PidRekeyed, abonné par abonné : un abonné qui lève n'empêche pas les autres (même patron que GameLinkLosing).
    private void RaisePidRekeyed(AccountRow row, int old, int @new)
    {
        if (PidRekeyed is not { } ev) return;
        foreach (var d in ev.GetInvocationList())
        {
            try { ((Action<AccountRow, int, int>)d)(row, old, @new); }
            catch (Exception e) { GivreLauncher.Core.Transport.TransportLog.Line((uint)@new, $"[reconnexion] re-clé pid {old} -> {@new} : {e.Message}"); }
        }
    }

    // Point de validation d'une relance MITM en place (thread UI, SANS await) : hôtes du pid arrêtés par l'onglet Bots (avant
    // toute perte d'état), ancien client oublié puis tué, son pid TENU « en relance » (activité « Connexion ») : ni trame
    // tardive ni Process.Exited ne le font revivre.
    private void BeginInPlaceRelaunch(AccountRow row, int old, Action? onCommit)
    {
        _relaunching[old] = row;
        try { onCommit?.Invoke(); }
        catch (Exception e) { GivreLauncher.Core.Transport.TransportLog.Line((uint)old, $"[reconnexion] validation : {e.Message}"); }
        if (_watched.TryRemove(old, out var wp)) { try { wp.Dispose(); } catch { } }   // mort VOULUE : jamais prise pour un crash
        GivreLauncher.Core.Transport.TransportLog.Line((uint)old, $"[reconnexion] « Reconnecter » : arrêt du client pid {old} puis relance immédiate (entrée Bots conservée)");
        TryKill(old);   // tout de suite : une exception plus bas ne peut plus laisser l'ancien client vivant sans surveillance
        try
        {
        _pidStats.TryRemove((uint)old, out _);
        _launch.Forget((uint)old);   // AVANT le nouveau client : un pid réutilisé par l'OS ne perdra pas son état naissant
        _launch.HoldLinkDown((uint)old, Loc.T("L.Reconnect.Status"), imposed: false, byServer: false, relaunching: true);
        Stores.World.ClearMap((uint)old);
        row.Activity = BotActivity.Connecting;
        row.MapId = null; row.MapCoords = null; row.ExitIp = "-";
        row.Status = Loc.T("L.Reconnect.Status");
        RefreshBotSnapshot(); LicenseService.PulseBotTelemetry();
        }
        catch (Exception e) { GivreLauncher.Core.Transport.TransportLog.Line((uint)old, $"[reconnexion] préparation : {e.Message}"); }
    }

    // Nouveau client adopté (thread UI, SANS await) : la ligne passe sur son pid, la Console suit, l'onglet Bots re-clé l'entrée.
    private void RekeyPid(AccountRow row, int old, int @new)
    {
        _launch.ReleaseLinkDown((uint)old);
        if (@new != old) { _launch.Forget((uint)old); _pidStats.TryRemove((uint)old, out _); }
        TransportDiag.Rekey((uint)old, (uint)@new);
        GivreLauncher.Core.Transport.TransportLog.Line((uint)@new, $"[reconnexion] client relancé : pid {old} -> {@new}");
        row.Pid = @new;
        RaisePidRekeyed(row, old, @new);
    }

    // Bouclier sans surveillance : personne pour saisir le code -> annulation immédiate (le lancement échoue proprement).
    // Une instance PAR lancement : `Asked` dit au planning que le compte exige un code (-> ne pas réessayer).
    private sealed class NoShieldPrompt : IShieldCodePrompt
    {
        public volatile bool Asked;
        public Task<string?> RequestSecurityCodeAsync(string login, CancellationToken ct = default)
        {
            Asked = true;
            return Task.FromResult<string?>(null);
        }
    }

    // Droit « multi » (multi-boxing payant) : le 1er compte en ligne est libre, tout compte EN PLUS l'exige (MITM ou Socket).
    private bool MultiAllowed(AccountRow row)
        => LicenseService.HasPermission(LicenseService.Perm.Multi) || !Accounts.Any(a => a.Running && a != row);

    // Route réseau d'un lancement (MITM OU Socket) : droits licence (adaptateur/proxy) + résolution du proxy. FAIL-CLOSED :
    // un proxy/adaptateur assigné mais injoignable BLOQUE le lancement — jamais de repli silencieux sur l'IP réelle.
    // false = lancement refusé (toast affiché). true + ps null = « Sans proxy » EXPLICITE (IP réelle assumée).
    private static bool TryResolveRoute(AccountRow row, out ProxyEntry? entry, out GivreLauncher.Core.Proxy.ProxySettings? ps)
    {
        ps = null;
        entry = row.SelectedProxy;
        if (entry is not null && entry.IsNone) entry = null;   // "Sans proxy" explicite -> lancement direct (IP reelle)
        entry ??= row.Model.ProxyId is { } id ? Stores.Proxies.Get(id) : null;
        // Proxy mémorisé mais supprimé (agent retiré du portail, proxy effacé) : refus, jamais de repli sur l'IP réelle.
        if (entry is null && row.Model.ProxyId is { Length: > 0 })
        {
            AudioService.PlayError();
            Toasts.Show(Loc.Fmt("L.Accounts.ProxyRemoved", row.Login), success: false);
            return false;
        }
        if (entry is null) return true;
        return TryResolveEntry(entry, out ps);
    }

    // Proxy du SERVEUR DE JEU (paramètres du compte) : mêmes règles que le proxy du compte (droits, agent GivreRelay, fail-closed).
    // true + gamePs null = aucun proxy de jeu distinct (la session de jeu garde le proxy du compte).
    internal static bool TryResolveGameRoute(AccountRow row, out ProxyEntry? gameEntry, out GivreLauncher.Core.Proxy.ProxySettings? gamePs)
    {
        gamePs = null;
        gameEntry = null;
        if (row.Model.GameProxyId is not { Length: > 0 } gid) return true;
        gameEntry = Stores.Proxies.Get(gid);
        if (gameEntry is null)
        {
            AudioService.PlayError();
            Toasts.Show(Loc.Fmt("L.Accounts.GameProxyRemoved", row.Login), success: false);
            return false;
        }
        return TryResolveEntry(gameEntry, out gamePs);
    }

    // Journal de connexion : proxy de la connexion, et proxy de jeu prévu (la bascule elle-même est journalisée quand elle a lieu).
    internal static void JournalRoute(string accountId, uint pid, ProxyEntry? entry, GivreLauncher.Core.Proxy.ProxySettings? ps,
                                      ProxyEntry? gameEntry, GivreLauncher.Core.Proxy.ProxySettings? gamePs)
    {
        void J(string m, LuaLogLevel l) { if (pid != 0) ConnectionJournal.ForBot(pid, m, l); else ConnectionJournal.ForAccount(accountId, m, l); }
        J(ps is null ? Loc.T("L.Journal.NoProxy") : Loc.Fmt("L.Journal.Proxy", ConnectionJournal.Describe(entry?.Name, ps, entry?.FromRelay == true)),
          ps is null ? LuaLogLevel.Warning : LuaLogLevel.Info);
        if (gamePs is not null)
            J(Loc.Fmt("L.Journal.GameProxyPlanned", ConnectionJournal.Describe(gameEntry?.Name, gamePs, gameEntry?.FromRelay == true)), LuaLogLevel.Info);
    }

    internal static LaunchRouting RoutingOf(ProxyEntry? e) =>
        e is null ? LaunchRouting.None : e.FromRelay ? LaunchRouting.Relay : e.FromAdapter ? LaunchRouting.Adapter : LaunchRouting.Proxy;

    // Un proxy précis -> réglages prêts à l'emploi, avec le message exact en cas de refus (droit licence, agent indisponible,
    // proxy injoignable). Jamais de repli silencieux sur l'IP réelle.
    private static bool TryResolveEntry(ProxyEntry entry, out GivreLauncher.Core.Proxy.ProxySettings? ps)
    {
        ps = null;
        if (entry.FromRelay) return TryResolveRelay(entry, out ps);
        // Compte routé par un ADAPTATEUR mais licence sans le droit "adapter" -> message précis (sinon le null ci-dessous
        // afficherait le générique "proxy injoignable"). Le proxy d'interface ne démarrera pas.
        if (entry.FromAdapter && !LicenseService.HasPermission(LicenseService.Perm.Adapter))
        {
            AudioService.PlayError();
            Toasts.Show(Loc.T("L.Perm.AdapterLaunchDenied"), success: false);
            return false;
        }
        // Compte routé par un PROXY externe (SOCKS5/HTTP) mais licence sans le droit "proxy" -> message précis.
        if (!entry.FromAdapter && !LicenseService.HasPermission(LicenseService.Perm.Proxy))
        {
            AudioService.PlayError();
            Toasts.Show(Loc.T("L.Perm.ProxyLaunchDenied"), success: false);
            return false;
        }
        ps = ProxyResolver.Resolve(entry, Stores.Proxies);
        // Proxy assigne mais injoignable (adaptateur eteint ?) -> on bloque : pas de fuite silencieuse.
        if (ps is null) { Toasts.Show(Loc.T("L.Accounts.ProxyUnresolved"), success: false); return false; }
        return true;
    }

    // Partage GivreRelay : refusé avec la raison exacte (relais coupé, partage hors ligne, quota atteint…), une seule fois,
    // sans nouvelle tentative. Jamais de repli sur l'IP réelle.
    internal static bool TryResolveRelay(ProxyEntry entry, out GivreLauncher.Core.Proxy.ProxySettings? ps)
    {
        ps = ProxyResolver.Resolve(entry, Stores.Proxies);
        if (ps is not null) return true;
        AudioService.PlayError();
        Toasts.Show(Loc.Fmt("L.Relay.LaunchRefused", entry.Name,
                            Services.Relay.GivreRelay.Unavailable(entry.RelayExitId!) ?? Loc.T("L.Relay.R.RelayOffline")), success: false);
        return false;
    }

    // unattendedShield non null = lancement SANS SURVEILLANCE (planning) : aucun modal, Bouclier refusé d'office.
    // Point d'entrée UNIQUE des lancements automatiques (planning, Lancer tout, relance) : suit le MODE mémorisé du compte.
    // manualMitm = bouton « Lancer » de la carte : MITM FORCÉ (pas d'aiguillage Socket), et le MITM ne devient le mode mémorisé
    // qu'une fois toutes les gardes passées (juste avant Running) -> un lancement refusé ne change pas le mode du compte.
    // relaunchPid > 0 = « Reconnecter » MITM EN PLACE (RelaunchInPlaceAsync) : le compte RESTE en ligne (Running, entrée Bots),
    // mêmes gardes (multi — le compte ne compte pas deux fois —, route, Bouclier surveillé, LaunchGen), aucun modal. Validation
    // (onCommit + mise à mort de l'ancien client) juste après les gardes ; succès -> ligne re-clée sur le nouveau pid ; échec ->
    // arrêt SANS UserStopped (l'entrée Bots part, la raison reste affichée).
    private async Task<bool> LaunchCoreAsync(AccountRow row, int? autoConnect = null, NoShieldPrompt? unattendedShield = null,
                                             string? character = null, bool manualMitm = false, int relaunchPid = 0, Action? onCommit = null,
                                             bool sourceAlreadyClosed = false)
    {
        if (row.SocketRunTask is { IsCompleted: false } && !row.Running) return false;
        if (row.SessionTransitionActive && !sourceAlreadyClosed) return false;
        bool unattended = unattendedShield is not null;
        bool inPlace = relaunchPid > 0;
        bool modal = !unattended && !inPlace;   // relance en place : l'entrée Bots montre les étapes, le modal la masquerait
        if (inPlace)
        {
            // Relance en place : la session à relancer doit être toujours celle-là (MITM, même pid).
            if (!row.Running || row.IsSocketSession || row.Pid != relaunchPid) return false;
        }
        // Déjà lancé (ou en cours de lancement) : jamais un 2e client sur le même compte. Chemins concernés : la
        // confirmation « lancer quand même » d'un avertissement mapping restée ouverte pendant un lancement planifié.
        else if (row.Running) return true;
        if (!manualMitm && !inPlace && BlockedByBan(row)) return false;   // compte banni : aucun lancement automatique
        // Compte en mode SOCKET : connexion clientless (mêmes gardes licence/route, aucun Dofus.exe, direct en jeu). Sauf clic
        // « Lancer » (manualMitm) : l'utilisateur a choisi le MITM.
        if (!inPlace && !manualMitm && row.Model.Mode == ConnectionMode.Socket) return StartSocketCore(row, unattendedShield, character);
        // Multi-boxing gaté par le droit serveur "multi" : le 1er compte est libre, tout compte lancé EN PLUS d'un
        // compte déjà en jeu l'exige (audit P1-2 : gate au point d'action). Le lancement mono-compte reste gratuit.
        if (!MultiAllowed(row))
        {
            Toasts.Show(Loc.T("L.Perm.MultiDenied"), success: false);
            return false;
        }

        if (!TryResolveRoute(row, out var entry, out var ps)) return false;
        // ps == null -> lancement SANS proxy (IP reelle), autorise et assume par l'utilisateur.
        if (!TryResolveGameRoute(row, out var gameEntry, out var gamePs)) return false;   // proxy du serveur de jeu (optionnel)
        JournalRoute(row.Model.Id, 0, entry, ps, gameEntry, gamePs);

        // Modal de lancement : montre qu'on tente de passer par le proxy, puis les etapes. Jamais en lancement planifié
        // (sans surveillance) : il masquerait/écraserait le modal d'un lancement manuel concurrent.
        if (modal)
        {
            LaunchModalLogin = row.Login;
            LaunchModalProxy = (ps is null
                ? Loc.T("L.Accounts.NoProxyMode")
                : entry is { FromRelay: true } ? $"{Loc.T("L.Relay.Tag")}  ·  {entry.Name}"
                : Loc.Fmt("L.Accounts.ViaProxy", ps.Kind, ps.Host, ps.Port))
                + (gameEntry is not null ? "\n" + Loc.Fmt("L.Accounts.GameProxyVia", gameEntry.Name) : "");
            LaunchModalStep = Loc.T("L.Accounts.Launching");
            LaunchModalBusy = true; LaunchModalOk = false; LaunchModalFailed = false;
            LaunchModalVisible = true;
        }

        // Fige le droit dont dépendra la connexion (pour couper ce compte si "adapter"/"proxy" est retiré en cours).
        row.Routing = RoutingOf(entry);
        row.GameRouting = RoutingOf(gameEntry);
        int gen = ++row.LaunchGen;
        if (inPlace && !sourceAlreadyClosed) BeginInPlaceRelaunch(row, relaunchPid, onCommit);   // point de validation : hôtes arrêtés, ancien client tué
        else if (!inPlace)
        {
            // Clic « Lancer » : toutes les gardes sont passées -> le MITM devient le mode MÉMORISÉ du compte (pastille, planning,
            // Lancer tout, équipe, relance). Posé AVANT Running (la pastille lit ce mode tant que la session n'est pas lancée).
            if (manualMitm) row.IsSocketMode = false;
            row.Running = true;
            row.ServerKickedOffline = false;   // nouvelle session : plus de « Reconnecter » d'après déconnexion serveur
            row.Status = Loc.T("L.Accounts.Launching");
        }
        bool rekeyed = false;   // relance en place : la ligne est passée sur le nouveau client (le filtre catch ci-dessous ne vaut plus)
        try
        {
            var (apiKey, refresh) = row.Model.Source == AuthSource.Browser ? Stores.Accounts.GetBrowserTokens(row.Model.Id) : (null, null);
            // Certificat Shield déjà forgé (compte sécurisé) : réutilisé -> pas de re-mint refusé (ALREADYSECURED) au relancement.
            var (certId, certEnc) = row.Model.Source == AuthSource.Browser ? Stores.Accounts.GetCertificate(row.Model.Id) : (null, null);
            // « Empreinte machine par compte » (TOUJOURS actif en MITM) : graine PAR COMPTE (résolue+persistée au besoin)
            // transmise à GivreKama (GIVRE_HWSEED) qui falsifie les MAC dans le client -> device_identifier unique par compte.
            var devSeed = Stores.Accounts.GetOrCreateDeviceSeed(row.Model.Id);
            // autoConnect surchargé (ex. « Connecter en jeu » de l'équipe force 2 = direct en jeu) sinon la préférence du compte.
            int effectiveAutoConnect = autoConnect ?? row.Model.AutoConnectType;
            if (row.Model.AutoConnectType == 2 && string.IsNullOrWhiteSpace(character))
            {
                effectiveAutoConnect = 2; // Accès direct en jeu (dernier personnage joué)
            }
            var req = new LaunchRequest(row.Login, row.Model.Source, ps, apiKey, refresh, effectiveAutoConnect, certId, certEnc,
                                        DeviceSeed: devSeed, GameProxy: gamePs,
                                        GameProxyLabel: gamePs is null ? null : ConnectionJournal.Describe(gameEntry?.Name, gamePs, gameEntry?.FromRelay == true),
                                        CharacterAutoPick: character);

            var outcome = await _launch.LaunchAsync(req, unattendedShield is not null ? unattendedShield : this,
                s => Dispatcher.UIThread.Post(() =>
                {
                    if (gen == row.LaunchGen) row.Status = inPlace ? Loc.Fmt("L.Reconnect.StatusStep", s) : s;
                    ConnectionJournal.ForAccount(row.Model.Id, s);   // étapes du lancement (authentification, client, hook…)
                    if (modal) LaunchModalStep = s;
                }),
                // Shield fraîchement forgé -> on le mémorise (chiffré) pour tous les prochains lancements de ce compte.
                onCertMinted: (cid, enc) => Stores.Accounts.SetCertificate(row.Model.Id, cid, enc)).ConfigureAwait(true);
            // Lancement DÉPASSÉ : un lancement plus récent de ce compte a démarré pendant l'await (arrêt puis relance) -> on
            // tue le client né de CE lancement et on n'écrit rien sur la ligne (elle appartient au lancement récent).
            if (gen != row.LaunchGen)
            {
                if (outcome.Ok) TryKill(outcome.Pid);
                if (modal) LaunchModalVisible = false;
                return false;
            }
            if (!inPlace) row.Status = outcome.Message;   // relance en place : écrit après les vérifications ci-dessous
            if (modal) { LaunchModalStep = outcome.Message; LaunchModalBusy = false; }

            if (outcome.Ok)
            {
                // FENÊTRE DE LANCEMENT : row.Running est passé true AVANT l'await ci-dessus, mais le pid n'est publié
                // qu'ICI. Un arrêt (Stop/Arrêter tout/Supprimer) ou une RÉVOCATION de droit a pu tomber pendant l'await :
                // StopCore n'a alors pas pu tuer (pid null) et a juste remis Running=false. Si le lancement n'est plus
                // voulu — ou si le droit de la route (adaptateur/proxy) a été retiré — on tue le client tout juste né et on
                // n'adopte NI le pid NI la surveillance : jamais d'orphelin, et la révocation fail-closed reste étanche.
                // (Relance en place : un Arrêter pendant l'await a tout arrêté -> même chemin.)
                if (!row.Running || !RoutingStillAllowed(row))
                {
                    TryKill(outcome.Pid);
                    if (row.Running) StopCore(row);   // droit de route révoqué mais StopRevoked pas encore passé -> on finalise (pas de ligne zombie Running=true/Pid=null)
                    if (modal) LaunchModalVisible = false;
                    return false;
                }
                if (inPlace)
                {
                    RekeyPid(row, relaunchPid, outcome.Pid);   // même entrée Bots, Console et historique sur le nouveau pid
                    rekeyed = true;
                    row.Status = outcome.Message;
                }
                else row.Pid = outcome.Pid;
                row.Activity = BotActivity.Connecting;   // lancé, pas encore localisé -> "Connexion" jusqu'à la 1re carte
                if (modal) LaunchModalOk = true;
                WatchClient(row, outcome.Pid);            // détection de crash / fermeture externe (relance auto opt-in)
                RefreshBotSnapshot(); LicenseService.PulseBotTelemetry();   // bot en ligne -> panel rafraichi tout de suite
                if (!unattended) AudioService.PlayUi(AudioService.Launch);   // lancement planifié (parfois la nuit) : sans son
                Toasts.Show(Loc.Fmt("L.Accounts.LaunchedOk", row.Login), sound: !unattended);
                _ = VerifyExitIpAsync(row, ps, gamePs);   // IP de connexion puis, s'il y en a un, IP vue EN JEU (proxy de jeu)
                if (modal) await Task.Delay(1300).ConfigureAwait(true);
            }
            else if (inPlace)
            {
                // Relance en place ratée : le compte s'arrête (SANS UserStopped : la plage du planning reste acquise), l'entrée
                // Bots part, la raison reste affichée. Arrêté entre-temps par l'utilisateur -> rien (son « arrêté » est gardé).
                // Dernier perso + script restent proposés par « Reconnecter » sur la carte du compte (ServerKickedOffline).
                if (row.Running && row.Pid == relaunchPid)
                {
                    StopCore(row);
                    row.ServerKickedOffline = !string.IsNullOrEmpty(row.LastCharacter);
                    row.Status = outcome.Message;
                    AudioService.PlayError();
                    Toasts.Show(outcome.Message, success: false);
                }
            }
            else
            {
                row.Running = false;
                AudioService.PlayError();
                Toasts.Show(outcome.Message, success: false);
                if (modal) { LaunchModalFailed = true; await Task.Delay(2400).ConfigureAwait(true); }
            }
            if (modal) LaunchModalVisible = false;
            return outcome.Ok;
        }
        catch (Exception e) when (inPlace && !rekeyed && row.Running && row.Pid == relaunchPid)
        {
            // Exception imprévue APRÈS la validation (ancien client déjà tué) : arrêt ICI, tant que son pid est encore tenu « en
            // relance » -> StopCore ne le re-tue pas (l'OS a pu le réattribuer). Même issue qu'une relance ratée.
            StopCore(row);
            row.ServerKickedOffline = !string.IsNullOrEmpty(row.LastCharacter);
            row.Status = Loc.Fmt("L.Accounts.LaunchError", e.Message);
            throw;
        }
        finally { if (inPlace && RelaunchOwnedBy(relaunchPid, row)) _relaunching.Remove(relaunchPid); }
    }

    [RelayCommand]
    private void CloseLaunchModal() => LaunchModalVisible = false;

    // IP de sortie d'une session SOCKET : ipify via le MÊME proxy/interface que le socket de jeu -> l'IP vue par Ankama.
    private static async Task VerifySocketExitIpAsync(AccountRow row, GivreLauncher.Core.Proxy.ProxySettings? ps,
                                                      GivreLauncher.Core.Proxy.ProxySettings? gamePs, uint pid)
    {
        var game = gamePs ?? ps;
        var via = game is null ? "connexion DIRECTE (IP réelle)" : $"{game.Kind} {game.Host}:{game.Port}";
        SocketLog.Info(pid, $"vérification de l'IP de sortie (ipify via {via})…");
        string? ip = await JournalExitIpsAsync(pid, ps, gamePs).ConfigureAwait(true);
        // Le contrôle réseau est lent (proxy 4G, DNS). Arrêter puis relancer la carte pendant la sonde ne doit
        // pas écrire l'ancienne IP sur la nouvelle session, ni toucher une propriété Avalonia hors thread UI.
        Dispatcher.UIThread.Post(() =>
        {
            if (!row.IsSocketSession || row.Pid != (int)pid) return;
            row.ExitIp = ip ?? "?";
            SocketLog.Info(pid, ip is not null ? $"► IP DE SORTIE (vue par les serveurs Ankama) : {ip}  —  via {via}" : $"IP de sortie INDÉTERMINÉE (via {via})");
        });
    }

    private static async Task VerifyExitIpAsync(AccountRow row, GivreLauncher.Core.Proxy.ProxySettings? ps, GivreLauncher.Core.Proxy.ProxySettings? gamePs)
    {
        uint pid = row.Pid is int p ? (uint)p : 0;
        string? ip = await JournalExitIpsAsync(pid, ps, gamePs).ConfigureAwait(true);
        row.ExitIp = ip ?? "?";
        // Appose [IP: x.x.x.x] dans le titre de la fenetre du client (proxy ou IP reelle), comme GivreLauncher.
        if (ip is not null && row.Pid is int wpid) _ = GameTagger.TagAsync(wpid, ip);
    }

    // Journal de connexion : IP de CONNEXION (proxy du compte) puis, s'il y en a un, IP EN JEU (proxy de jeu). Renvoie l'IP vue en
    // jeu (null si indéterminée). Un proxy rotatif change d'IP à chaque connexion : l'IP de connexion affichée n'est qu'un exemple.
    private static async Task<string?> JournalExitIpsAsync(uint pid, GivreLauncher.Core.Proxy.ProxySettings? ps, GivreLauncher.Core.Proxy.ProxySettings? gamePs)
    {
        static async Task<string?> Probe(GivreLauncher.Core.Proxy.ProxySettings? s)
        {
            try { var (ok, ip, _) = await ProxyTester.TestAsync(s).ConfigureAwait(true); return ok && !string.IsNullOrWhiteSpace(ip) ? ip : null; }
            catch { return null; }
        }
        string? conn = await Probe(ps).ConfigureAwait(true);
        if (pid != 0)
            ConnectionJournal.ForBot(pid, conn is null ? Loc.T("L.Journal.IpUnknown")
                                          : ps is null ? Loc.Fmt("L.Journal.IpReal", conn) : Loc.Fmt("L.Journal.Ip", conn),
                                     conn is null || ps is null ? LuaLogLevel.Warning : LuaLogLevel.Info);
        if (gamePs is null) return conn;
        string? game = await Probe(gamePs).ConfigureAwait(true);
        if (pid != 0)
            ConnectionJournal.ForBot(pid, game is null ? Loc.T("L.Journal.GameIpUnknown") : Loc.Fmt("L.Journal.GameIp", game),
                                     game is null ? LuaLogLevel.Warning : LuaLogLevel.Info);
        return game;
    }

    // Le droit dont dépend la ROUTE figée au lancement (adaptateur/proxy) est-il toujours accordé ? Sert à ne pas
    // adopter un client dont l'entitlement de routage a été retiré pendant la fenêtre de lancement (révocation fail-closed).
    private static bool RoutingStillAllowed(AccountRow row) => RoutingAllowed(row.Routing) && RoutingAllowed(row.GameRouting);

    private static bool RoutingAllowed(LaunchRouting r) => r switch
    {
        LaunchRouting.Adapter => LicenseService.HasPermission(LicenseService.Perm.Adapter),
        LaunchRouting.Proxy => LicenseService.HasPermission(LicenseService.Perm.Proxy),
        _ => true,
    };

    // Observe le process du client pour détecter sa mort. Process.Exited (fiable, pas de réutilisation de pid car on
    // tient l'objet Process) marshale vers l'UI. Si le process est DÉJÀ mort (crash instantané au lancement) avant
    // qu'on ait pu s'abonner, on déclenche tout de suite. Une mort ATTENDUE (arrêt manuel) est reconnue dans
    // OnClientExited (row.Running déjà false / pid déjà changé) -> pas de fausse relance.
    private void WatchClient(AccountRow row, int pid)
    {
        try
        {
            var p = Process.GetProcessById(pid);
            p.EnableRaisingEvents = true;
            p.Exited += (_, _) => Dispatcher.UIThread.Post(() => OnClientExited(row, pid));
            _watched[pid] = p;
            if (p.HasExited) { _watched.TryRemove(pid, out _); try { p.Dispose(); } catch { } Dispatcher.UIThread.Post(() => OnClientExited(row, pid)); }
        }
        catch
        {
            // GetProcessById a levé -> le process n'existe déjà plus (mort immédiate) : traite comme un crash.
            Dispatcher.UIThread.Post(() => OnClientExited(row, pid));
        }
    }

    // Le client `pid` s'est terminé. Deux cas :
    //  - ATTENDU (arrêt manuel/révocation/Arrêter tout) : StopCore a déjà nettoyé (Running=false ou Pid changé) -> on ignore.
    //  - INATTENDU (crash instantané / fermeture externe) : on nettoie proprement, on prévient, et si la relance auto est
    //    active (et le budget anti-boucle le permet) on relance le compte.
    private void OnClientExited(AccountRow row, int pid)
    {
        if (_watched.TryRemove(pid, out var wp)) { try { wp.Dispose(); } catch { } }
        if (RelaunchOwnedBy(pid, row)) return;        // client tué par « Reconnecter » en place : mort VOULUE (éventuellement déjà postée)
        if (!row.Running || row.Pid != pid) return;   // mort ATTENDUE (déjà arrêté par l'app) -> rien à faire

        // MITM « Déconnecté » par une coupure IMPOSÉE par le serveur (inactivité, modération…) : l'utilisateur ferme la fenêtre
        // Dofus restée ouverte -> PAS un crash (ni relance auto, ni reprise par le planning). Arrêt manuel ; la ligne garde
        // « Reconnecter » (dernier perso) et le script. Coupure lue AVANT StopCore (Forget l'efface) ; drapeau posé AVANT le
        // Resync qui libère l'hôte Lua (KeepKickedScript s'y fie). Coupure non imposée : chemin « crash » ci-dessous.
        if (!row.IsSocketSession && _launch.LinkDownOf((uint)pid) is { Imposed: true } down)
        {
            row.ServerKickedOffline = true;
            StopCore(row);
            UserStopped?.Invoke(row);
            row.Status = Loc.Fmt("L.Accounts.ServerKicked", down.Reason);
            row.RecordDisconnect(row.Status);
            return;
        }

        // Compte connecté PAR LE PLANNING : c'est le planning qui le relance (dans la plage, budget par plage). Sinon la
        // relance auto le reconnecterait aussi -> double lancement, ou reconnexion hors plage pendant la déconnexion de fin.
        if (PlanningClaimsCrash?.Invoke(row) == true)
        {
            ClientCrashed?.Invoke(row);
            StopCore(row);
            row.Status = Loc.T("L.Accounts.Crashed");
            row.RecordDisconnect(row.Status);
            AudioService.PlayError();
            Toasts.Show(Loc.Fmt("L.Accounts.CrashedPlanning", row.Login), success: false);
            return;
        }

        bool relaunch = AppConfig.Current.AutoRelaunch && AllowRelaunch(row);
        ClientCrashed?.Invoke(row);                    // avant StopCore : le superviseur voit encore le bot en jeu
        StopCore(row);                                 // teardown : tue (déjà mort), reset état, retire du panel
        row.Status = Loc.T("L.Accounts.Crashed");
        row.RecordDisconnect(row.Status);
        AudioService.PlayError();
        if (relaunch)
        {
            Toasts.Show(Loc.Fmt("L.Accounts.CrashedRelaunch", row.Login), success: false);
            ConnectionJournal.ForAccount(row.Model.Id, Loc.T("L.Journal.CrashRelaunch"), LuaLogLevel.Danger);   // affiché sur le bot relancé
            _ = LaunchCoreAsync(row);                  // relance complète (proxy/perm/hook ré-évalués)
        }
        else if (AppConfig.Current.AutoRelaunch)
            Toasts.Show(Loc.Fmt("L.Accounts.CrashLoop", row.Login), success: false);   // budget épuisé -> relance suspendue
        else
            Toasts.Show(Loc.Fmt("L.Accounts.CrashedToast", row.Login), success: false);
    }

    // Budget anti-boucle : autorise une relance si le compte n'a pas déjà dépassé RelaunchMax relances dans la fenêtre
    // glissante RelaunchWindowMs. Incrémente le compteur à chaque appel autorisé. Évite un aller-retour crash<->relance infini.
    private bool AllowRelaunch(AccountRow row)
    {
        long now = Environment.TickCount64;
        var b = _relaunchBudget.GetValueOrDefault(row.Model.Id);
        if (b.WindowStart == 0 || now - b.WindowStart > RelaunchWindowMs) b = (0, now);   // (re)démarre la fenêtre
        b.Count++;
        _relaunchBudget[row.Model.Id] = b;
        return b.Count <= RelaunchMax;
    }

    [RelayCommand]
    private void Stop(AccountRow row)
    {
        // Compte HORS LIGNE avec une reconnexion manuelle en attente : rien à tuer, on annule seulement la relance programmée
        // (onglet Bots, via UserStopped).
        if (!row.Running && row.ReconnectPending) { CancelPendingReconnect(row); return; }
        StopCore(row);
        UserStopped?.Invoke(row);
        Toasts.Show(Loc.Fmt("L.Accounts.Stopped", row.Login));
    }

    // Teardown SANS toast : kill du process + reset d'état. Le toast (arrêt manuel vs révocation) est choisi par l'appelant.
    private void StopCore(AccountRow row, bool cancelTransition = true)
    {
        if (cancelTransition) row.SessionTransitionCancellation?.Cancel();
        // Retire d'abord la surveillance (arrêt VOULU) : le Process.Exited qui suivra le kill sera ignoré (row.Running
        // passe false ci-dessous) -> jamais pris pour un crash, donc jamais de relance auto sur un arrêt manuel.
        if (row.Pid is int pid)
        {
            // Session Socket : pid synthétique -> on annule la connexion clientless, JAMAIS de kill de process (il n'y en a pas).
            if (row.IsSocketSession)
            {
                // L'arrêt côté VM doit rendre les commandes impossibles immédiatement. Le finally de
                // SocketGameClient retire lui aussi l'injecteur, mais seulement après la fin async du flux :
                // entre l'annulation et cette fin, une action tardive pouvait encore trouver l'ancien pid.
                try { row.SocketCts?.Cancel(); } catch { }
                _launch.UnregisterSocketInjector((uint)pid);
                SocketStats.Forget((uint)pid);
            }
            else
            {
                if (_watched.TryRemove(pid, out var wp)) { try { wp.Dispose(); } catch { } }
                // Relance MITM en place en cours : ce pid est celui de l'ANCIEN client, déjà tué à la validation — l'OS a pu le
                // réattribuer à un process sans rapport -> jamais tué ici. Le NOUVEAU client est tué par la fenêtre de lancement
                // (LaunchCoreAsync : Running=false -> TryKill(outcome.Pid)) ou par LaunchAsync lui-même s'il échoue.
                if (!RelaunchOwnedBy(pid, row)) TryKill(pid);
                // Propriétaire arrêté : plus de pid tenu (le NOUVEAU client éventuel est tué par la fenêtre de lancement) — un
                // autre client qui hériterait du pid ne sera jamais épargné à tort.
                else _relaunching.Remove(pid);
            }
            _pidStats.TryRemove((uint)pid, out _); TransportDiag.Forget((uint)pid); _launch.Forget((uint)pid);
        }
        // _launch.Forget ci-dessus lève aussi toute coupure TENUE (session parquée, relance en place).
        row.IsSocketSession = false; row.SocketCts = null; row.SocketBackoff = false; row.SocketParked = false;
        row.ReconnectInPlace = false; Volatile.Write(ref row.SocketSession, null);
        row.Pid = null; row.Running = false; row.Routing = LaunchRouting.None; row.GameRouting = LaunchRouting.None; row.ExitIp = "-"; row.Character = "-"; row.Activity = BotActivity.Offline;
        row.CharacterName = null; row.Level = null; row.Breed = null; row.Gender = null; row.MapId = null; row.MapCoords = null; row.Server = "-";
        row.BreedIndex = null; row.GenderIndex = null;
        row.Status = Loc.T("L.Accounts.StatusIdle");
        RefreshBotSnapshot(); LicenseService.PulseBotTelemetry();   // bot hors ligne -> retire du panel tout de suite (ou bots:[] si dernier)
    }

    // Coupe un compte déjà EN LIGNE dont le droit vient d'être retiré (révocation live) : sinon le client reste
    // connecté (adapter/proxy) ou le multi-boxing persiste. On tue le process, cohérent avec le kill-switch fail-closed.
    private void StopRevoked(AccountRow row, string permName)
    {
        StopCore(row);
        UserStopped?.Invoke(row);   // droit retiré : le planning ne doit pas reboucler sur des lancements refusés
        AudioService.PlayError();
        Toasts.Show(Loc.Fmt("L.Accounts.StoppedRevoked", row.Login, permName), success: false);
    }

    // Révocation LIVE : coupe les comptes déjà en ligne devenus non autorisés (routage adapter/proxy retiré), puis
    // applique le plafond "multi" (ne garde qu'un seul compte en ligne). Rafraîchit aussi le bandeau CanMulti.
    private void OnPermissionsChanged()
    {
        OnPropertyChanged(nameof(CanMulti));
        OnPropertyChanged(nameof(ShowLegacyMultiLicenseNotice));
        OnPropertyChanged(nameof(CanSocket));
        OnPropertyChanged(nameof(SocketButtonTip));
        // Fermeture de l'app : la déconnexion de sortie n'est pas un retrait de droit. Avant, les comptes routés proxy/adaptateur
        // étaient coupés (client Dofus fermé + toast « droit retiré ») pendant la fermeture, les autres non.
        if (LicenseService.IsExiting) return;

        bool adapter = LicenseService.HasPermission(LicenseService.Perm.Adapter);
        bool proxy   = LicenseService.HasPermission(LicenseService.Perm.Proxy);
        // Droit « socket » retiré EN DIRECT (licence toujours authentifiée) : les sessions sans Dofus.exe sont coupées. Sur une
        // déconnexion complète de licence, la purge du mapping ferme déjà les sessions socket (pas de double toast).
        bool socketRevoked = LicenseService.IsAuthorized && !CanSocket;
        foreach (var row in Accounts.Where(a => a.Running).ToList())
        {
            if (row.IsSocketSession && socketRevoked) StopRevoked(row, Loc.T("L.Perm.socket"));
            else if ((row.Routing == LaunchRouting.Adapter || row.GameRouting == LaunchRouting.Adapter) && !adapter) StopRevoked(row, Loc.T("L.Perm.adapter"));
            else if ((row.Routing == LaunchRouting.Proxy || row.GameRouting == LaunchRouting.Proxy) && !proxy) StopRevoked(row, Loc.T("L.Perm.proxy"));
        }

        // "multi" retiré alors que plusieurs comptes tournent -> on garde le premier de la liste, on coupe le reste.
        // Uniquement si la licence reste AUTHENTIFIÉE (retrait ciblé du droit "multi") : sur une déconnexion complète
        // (IsAuthorized=false), l'écran de blocage prend le relais — on ne tue pas arbitrairement "tout sauf un".
        if (LicenseService.IsAuthorized && !CanMulti)
            foreach (var row in Accounts.Where(a => a.Running).Skip(1).ToList())
                StopRevoked(row, Loc.T("L.Perm.multi"));
    }

    [RelayCommand]
    private async Task LaunchAllAsync()
    {
        // « Lancer tout » = action GLOBALE (démarre plusieurs clients Dofus d'un coup), distincte du Lancer par-compte.
        // On CONFIRME d'abord ; l'attente du mapping + le lancement se font dans la callback (Dialogs.Confirm = callback).
        int idle = Accounts.Count(a => !a.Running);
        if (idle == 0) return;
        Dialogs.Confirm(Loc.T("L.Accounts.LaunchAllTitle"), Loc.Fmt("L.Accounts.LaunchAllBody", idle),
            onConfirm: () => _ = LaunchAllConfirmedAsync());
        await Task.CompletedTask;
    }

    private async Task LaunchAllConfirmedAsync()
    {
        // Même garde que Launch : on s'assure que le mapping streamé est prêt (modal de chargement) avant de tout lancer.
        if (MainViewModel.Instance is { } mvm) await mvm.EnsureMappingReadyAsync();
        var missing = MappingCatalog.MissingEssentials();
        if (missing.Count > 0) { WarnMappingMissing(missing, () => _ = LaunchAllCoreAsync()); return; }
        await LaunchAllCoreAsync();
    }

    private async Task LaunchAllCoreAsync()
    {
        bool multi = LicenseService.HasPermission(LicenseService.Perm.Multi);
        foreach (var row in Accounts.ToList())
            if (!row.Running)
            {
                // Sans le droit "multi", on ne lance qu'UN compte au total (multi-boxing payant) -> stop après le 1er.
                if (!multi && Accounts.Any(a => a.Running)) { Toasts.Show(Loc.T("L.Perm.MultiDenied"), success: false); break; }
                await LaunchCoreAsync(row).ConfigureAwait(true);
            }
    }

    /// <summary>Comptes que le tableau de bord lancerait en un clic : activés, non lancés, sans bannissement bloquant.</summary>
    public System.Collections.Generic.List<AccountRow> DashboardLaunchTargets()
        => Accounts.Where(a => a.Model.Enabled && !a.Running && !BanGate.BlocksAutoLaunch(a.Model, DateTime.UtcNow)).ToList();

    /// <summary>Comptes lancés par le tableau de bord : leur script démarre dès l'entrée en jeu (une fois), sans modifier l'option
    /// persistée « Démarrer automatiquement le script » du compte.</summary>
    public System.Collections.Generic.HashSet<string> ScriptOnNextSession { get; } = new();

    /// <summary>« Lancer les bots » du tableau de bord : confirme, puis connecte directement en jeu chaque compte activé et démarre son script.</summary>
    public void LaunchEnabledWithScripts()
    {
        var rows = DashboardLaunchTargets();
        if (rows.Count == 0) { Toasts.Show("Aucun compte activé à lancer.", success: false); return; }
        Dialogs.Confirm("Lancer les bots",
            $"Connecter directement en jeu {rows.Count} compte(s) activé(s) et démarrer leur script ?",
            onConfirm: () => _ = LaunchEnabledWithScriptsAsync(rows));
    }

    private async Task LaunchEnabledWithScriptsAsync(System.Collections.Generic.List<AccountRow> rows)
    {
        if (MainViewModel.Instance is { } mvm) await mvm.EnsureMappingReadyAsync();   // même garde que « Lancer tout »
        var missing = MappingCatalog.MissingEssentials();
        if (missing.Count > 0) { WarnMappingMissing(missing, () => _ = LaunchEnabledWithScriptsCoreAsync(rows)); return; }
        await LaunchEnabledWithScriptsCoreAsync(rows);
    }

    private async Task LaunchEnabledWithScriptsCoreAsync(System.Collections.Generic.List<AccountRow> rows)
    {
        bool multi = LicenseService.HasPermission(LicenseService.Perm.Multi);
        foreach (var row in rows)
        {
            if (row.Running) continue;
            if (!multi && Accounts.Any(a => a.Running)) { Toasts.Show(Loc.T("L.Perm.MultiDenied"), success: false); break; }
            ScriptOnNextSession.Add(row.Model.Id);
            if (!await LaunchCoreAsync(row, autoConnect: 2).ConfigureAwait(true)) ScriptOnNextSession.Remove(row.Model.Id);
        }
    }

    [RelayCommand]
    private void StopAll()
    {
        // « Arrêter tout » = action GLOBALE DESTRUCTRICE (tue TOUS les clients Dofus en cours), distincte du Stop par-compte
        // (immédiat, lui). On confirme avant. StopCore = pas de toast par compte -> un seul toast récapitulatif.
        // Reconnexion manuelle en attente sur un compte hors ligne (« Reconnecter » pas encore relancé) : comptée et annulée aussi.
        int running = Accounts.Count(a => a.Running || a.ReconnectPending);
        if (running == 0) return;
        Dialogs.Confirm(Loc.T("L.Accounts.StopAllTitle"), Loc.Fmt("L.Accounts.StopAllBody", running),
            onConfirm: () =>
            {
                int n = 0;
                foreach (var row in Accounts.ToList())
                {
                    if (row.Running) { StopCore(row); UserStopped?.Invoke(row); n++; }
                    else if (row.ReconnectPending) { CancelPendingReconnect(row); n++; }
                }
                if (n > 0) Toasts.Show(Loc.Fmt("L.Accounts.StoppedAll", n));
            },
            danger: true);
    }

    // Compte HORS LIGNE : annule la reconnexion manuelle en attente. UserStopped -> onglet Bots (relance programmée retirée,
    // pré-requis en cours annulés, drapeaux ReconnectPending resynchronisés ; le planning ne relance pas avant la fin de la plage).
    private void CancelPendingReconnect(AccountRow row)
    {
        UserStopped?.Invoke(row);
        row.Status = Loc.T("L.Accounts.StatusIdle");
    }

    public enum ResumeCancelOutcome { None, SocketBackoff, PendingReconnect }

    /// <summary>« Annuler la reprise » de la carte compte. Attente du backoff socket : le réveil est complété à FAUX -> la boucle
    /// de session abandonne (arrêt définitif, aucune nouvelle tentative, planning suspendu comme un arrêt manuel). Reconnexion
    /// manuelle en attente d'un compte hors ligne : relance programmée retirée. Thread UI.</summary>
    public ResumeCancelOutcome CancelResumeCore(AccountRow row)
    {
        if (row.SocketBackoff && Volatile.Read(ref row.SocketWake) is { } w && w.TrySetResult(false))
            return ResumeCancelOutcome.SocketBackoff;
        if (!row.Running && row.ReconnectPending) { CancelPendingReconnect(row); return ResumeCancelOutcome.PendingReconnect; }
        return ResumeCancelOutcome.None;
    }

    [RelayCommand]
    private void CancelResume(AccountRow? row)
    {
        if (row is null) return;
        if (CancelResumeCore(row) == ResumeCancelOutcome.PendingReconnect)
            Toasts.Show(Loc.Fmt("L.Accounts.ResumeCancelledToast", row.Login), success: false);
    }
}
