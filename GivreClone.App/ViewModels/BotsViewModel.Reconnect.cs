using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

// ===================== « Reconnecter » (onglet Bots, carte compte) =====================
// SANS délai artificiel et SANS sortir le bot de l'onglet Bots : pré-requis attendus D'ABORD (licence, Dofus.exe pour le MITM,
// droit socket, mapping, route, multi), puis — sans aucun await — reconnexion EN PLACE :
//  - Socket : même pid, par la boucle de session. Session vivante -> script suspendu PUIS session coupée, reconnexion
//    immédiate ; en attente (backoff) ou PARQUÉE (déconnexion imposée par le serveur) -> réveil immédiat. Script repris quand
//    la NOUVELLE session a chargé sa carte (DriveSocketResume).
//  - MITM : hôtes du pid arrêtés, ancien Dofus.exe tué, nouveau client lancé (mêmes gardes que tout lancement, Bouclier
//    surveillé) ; l'entrée Bots est RE-CLÉE sur le nouveau pid (console, sélection, position gardées) ; script repris une fois
//    le même perso de retour en jeu (file des relances, entrée InPlace).
//  - Compte MITM hors ligne (ServerKickedOffline) : relance surveillée immédiate par la file des relances.
// L'entrée ne part que si la relance ÉCHOUE (refus, auth, délai), raison affichée. Jamais d'UserStopped (la plage du planning
// reste acquise). Le plancher anti-empreinte (15 s / 1 min) ne vaut que pour la reconnexion AUTOMATIQUE socket.
// Jamais de reconnexion automatique après une déconnexion imposée par le serveur : uniquement sur clic.
public partial class BotsViewModel
{
    private readonly HashSet<string> _reconnectPreflight = new(StringComparer.Ordinal);   // comptes dont les pré-requis sont en cours
    private readonly HashSet<string> _reconnectAborted = new(StringComparer.Ordinal);     // ... annulés par un arrêt manuel pendant l'attente
    // Script d'un compte MITM arrêté après une déconnexion imposée par le serveur (fenêtre Dofus fermée) ou dont la relance en
    // place a échoué : relevé quand son bot quitte la liste (hôte Lua libéré), repris par « Reconnecter ». Clé = Account.Id.
    private readonly Dictionary<string, (string? Name, string Body)> _kickedScripts = new(StringComparer.Ordinal);

    /// <summary>Bouton « Reconnecter » du bandeau (bot sélectionné).</summary>
    [RelayCommand]
    private Task Reconnect() => SelectedBot?.Row is { } row ? RequestReconnectAsync(row) : Task.CompletedTask;

    private static void ReconnectBusy() => Toasts.Show(Loc.T("L.Reconnect.Busy"), success: false, sound: false);

    /// <summary>Lancement MANUEL d'un script refusé (Lancer, web START_SCRIPT, démarrage d'équipe) : « Reconnecter » MITM en
    /// place (l'hôte du pid sera libéré à la re-clé), ou bot SOCKET sans session en jeu (attente de reconnexion, session
    /// parquée, reconnexion en cours) — le script y tournerait à l'aveugle (injections refusées -> « bloqué »).</summary>
    private bool ScriptStartBlockedByReconnect(BotRowViewModel bot)
        => bot.Row.SessionTransitionActive || _accounts.IsRelaunching(bot.Row)
        || (bot.Row.IsSocketSession && (_socketWaiting.ContainsKey(bot.Pid) || bot.Row.SocketParked || bot.Row.SocketBackoff
                                        || bot.Row.ReconnectInPlace));

    // Point d'entrée commun (bandeau de l'onglet Bots, carte compte). Session vivante en jeu -> confirmation (niveau fenêtre) :
    // reconnecter coupe la session en cours.
    private Task RequestReconnectAsync(AccountRow row)
    {
        if (row.SessionTransitionActive || row.ReconnectPending || _reconnectPreflight.Contains(row.Model.Id) || _accounts.IsRelaunching(row))
        { ReconnectBusy(); return Task.CompletedTask; }
        if (!LicenseService.IsAuthorized) { Toasts.Show(Loc.T("L.Reconnect.NoLicense"), success: false); return Task.CompletedTask; }
        // Hors ligne sans session à reprendre : c'est « Lancer ».
        if (!row.Running) return row.CanReconnect ? ReconnectCoreAsync(row) : Task.CompletedTask;
        if (row.IsSocketSession)
        {
            // En attente (reconnexion automatique) ou parquée : rien à couper, réveil immédiat sans confirmation.
            // Épinglé sur la session TERMINÉE : si la reconnexion auto aboutit pendant les vérifications, on ne coupe pas la neuve.
            if (row.SocketBackoff || row.SocketParked) return ReconnectCoreAsync(row, Volatile.Read(ref row.SocketSessionSeq));
            // Connexion en cours (pas encore en jeu) : on ne coupe pas une connexion qui s'établit.
            if (row.Pid is not int p || Stores.Launch.CurrentMap((uint)p) is null) { ReconnectBusy(); return Task.CompletedTask; }
        }
        else if (row.Pid is not > 0) { ReconnectBusy(); return Task.CompletedTask; }   // client pas encore adopté
        if (!row.IsDisconnected)
        {
            // Confirmation NON bloquante : la session a pu changer pendant qu'elle était ouverte (arrêt, relance, crash) -> on
            // ne coupe que la session confirmée.
            int gen = row.LaunchGen; bool wasRunning = row.Running; int? pid0 = row.Pid;
            // Socket : LaunchGen / Running / pid ne changent pas d'une session à l'autre -> la coupure est ÉPINGLÉE sur le numéro
            // de la session confirmée (une session qui se connecte encore n'est jamais coupée).
            int expectSeq = row.IsSocketSession ? Volatile.Read(ref row.SocketSessionSeq) : -1;
            Dialogs.Confirm(Loc.T("L.Reconnect.ConfirmTitle"), Loc.T("L.Reconnect.ConfirmBody"),
                onConfirm: () =>
                {
                    if (row.LaunchGen != gen || row.Running != wasRunning || row.Pid != pid0) return;
                    _ = ReconnectCoreAsync(row, expectSeq);
                },
                okText: Loc.T("L.Reconnect.Button"), danger: true);
            return Task.CompletedTask;
        }
        return ReconnectCoreAsync(row);
    }

    // expectSeq : session socket confirmée (-1 = pas d'épinglage).
    private async Task ReconnectCoreAsync(AccountRow row, int expectSeq = -1)
    {
        string id = row.Model.Id;
        if (!row.Running && !row.CanReconnect) return;   // plus rien à reconnecter (état changé depuis le clic)
        if (row.SessionTransitionActive || row.ReconnectPending || _accounts.IsRelaunching(row) || !_reconnectPreflight.Add(id)) return;
        _reconnectAborted.Remove(id);
        int gen = row.LaunchGen; bool wasRunning = row.Running; int? pid0 = row.Pid;
        var queuedBefore = _scriptReconnects.Where(r => r.Attended && r.AccountId == id).ToList();
        try
        {
            SyncReconnectFlags();   // bouton grisé pendant les pré-requis
            // 1) Pré-requis (peuvent ouvrir le sélecteur de Dofus.exe / le modal de chargement du protocole) : rien n'est coupé
            //    tant qu'ils ne sont pas réunis.
            bool socket = row.Running ? row.IsSocketSession : AccountsViewModel.UsesSocket(row);
            if (socket && !_accounts.CanSocket)
            { AudioService.PlayError(); Toasts.Show($"{row.Login} : " + Loc.T("L.Perm.SocketDenied"), success: false); return; }
            if (!socket && !await AccountsViewModel.EnsureDofusExeAsync().ConfigureAwait(true)) return;   // toast déjà affiché
            if (MainViewModel.Instance is { } mvm) await mvm.EnsureMappingReadyAsync().ConfigureAwait(true);
            if (MappingCatalog.GetEffective() is null || MappingCatalog.MissingEssentials().Count > 0)
            { AudioService.PlayError(); Toasts.Show($"{row.Login} : " + Loc.T("L.Reconnect.NoMapping"), success: false); return; }
            if (!LicenseService.IsAuthorized) return;
            // L'état a changé pendant les pré-requis (arrêt — « Arrêter » d'un compte hors ligne compris —, relance, autre
            // reconnexion) : on ne coupe rien. (ReconnectPending est posé par NOS pré-requis : c'est la file / la relance en
            // place qui disent si une autre reconnexion est partie.)
            if (_reconnectAborted.Contains(id) || _accounts.IsRelaunching(row) || row.ReconnectInPlace
                || _scriptReconnects.Any(r => r.Attended && r.AccountId == id && !queuedBefore.Contains(r))
                || row.LaunchGen != gen || row.Running != wasRunning || row.Pid != pid0) return;
            if (!_accounts.MultiAllowedFor(row)) { AudioService.PlayError(); Toasts.Show(Loc.T("L.Perm.MultiDenied"), success: false); return; }
            if (!AccountsViewModel.RouteReady(row)) return;   // droit adaptateur/proxy ou proxy injoignable : toast déjà affiché

            // 2) SANS await : reconnexion en place (socket / MITM) ou relance du compte hors ligne.
            if (row.Running && socket) StartSocketReconnect(row, expectSeq);
            else if (row.Running) StartMitmRelaunch(row);
            else StartOfflineReconnect(row);
        }
        catch (Exception e) { Toasts.Show(Loc.Fmt("L.Accounts.LaunchError", e.Message), success: false); }
        finally { _reconnectPreflight.Remove(id); _reconnectAborted.Remove(id); SyncReconnectFlags(); }
    }

    // SOCKET, même pid, AUCUN délai : session vivante -> script suspendu (AVANT la coupure : la session mourante refuse déjà les
    // injections) puis coupure ; en attente / parquée -> réveil. Le script est repris par DriveSocketResume sur la NOUVELLE
    // session (numéro différent) ; aucune entrée dans la file des relances. expectSeq : session confirmée (-1 = pas d'épinglage).
    private void StartSocketReconnect(AccountRow row, int expectSeq)
    {
        if (row.Pid is not int pid) return;
        bool live = !(row.SocketBackoff || row.SocketParked);
        int seq = _accounts.ReconnectSocketNow(row, expectSeq, live ? s => SuspendSocketScript(pid, SocketPauseKind.Manual, s) : null);
        if (seq < 0)
        {
            // Rien à couper ni à réveiller (connexion en cours, autre session que celle confirmée, fin de session en train d'être
            // traitée) : déjà journalisé [socket].
            AppendLuaLine("compte", Loc.T("L.Reconnect.Busy"), LuaLogLevel.Info, pid);
            ReconnectBusy();
            return;
        }
        // Réveil d'une attente / session parquée : un script qui tourne À CE MOMENT (lancé pendant l'attente) agirait à l'aveugle
        // -> suspendu comme à une coupure, repris par DriveSocketResume sur la NOUVELLE session.
        if (!live && _luaHosts.TryGetValue(pid, out var h) && h.Running) SuspendSocketScript(pid, SocketPauseKind.Manual, seq);
        row.ReconnectSeq = seq;
        row.ReconnectInPlace = true;
        row.ReconnectInPlaceAt = Environment.TickCount64;
        ClearPlanDisconnect(row.Model.Id);
        if (live) row.Status = Loc.T("L.Reconnect.SocketNow");
        AppendLuaLine("compte", Loc.T(live ? "L.Reconnect.SocketCutLog" : "L.Reconnect.SocketLog"), LuaLogLevel.Warning, pid);
        Toasts.Show(Loc.Fmt("L.Reconnect.ToastNow", row.Login), sound: false);
        SyncReconnectFlags();
    }

    // MITM, SANS délai : relance du client EN PLACE (entrée Bots re-clée sur le nouveau pid). Perso + script relevés AVANT la
    // validation (qui arrête l'hôte Lua du bot). Tout l'état « reconnexion » est posé AU point de validation (onCommit) : une
    // relance refusée par ses gardes (multi, route) ne touche à rien (décompte du planning, relance programmée, script gardé).
    private void StartMitmRelaunch(AccountRow row)
    {
        if (row.Pid is not int oldPid || oldPid <= 0) return;
        string id = row.Model.Id;
        string character = row.CharacterName ?? row.LastCharacter ?? "";
        var (scriptName, scriptBody) = CaptureReconnectScript(row);
        bool committed = false;
        var relaunch = _accounts.RelaunchInPlaceAsync(row, onCommit: () =>
        {
            committed = true;
            SuspendForRelaunch(oldPid);      // hôtes du pid arrêtés AVANT que l'ancien client ne soit oublié / tué
            _kickedScripts.Remove(id);
            ClearPlanDisconnect(id);         // plus de décompte / déconnexion « après le combat » sur la session relancée
            DropScriptReconnect(id);         // une relance programmée par un script est remplacée par CELLE-CI
            long now = Environment.TickCount64;
            _scriptReconnects.Add(new ScriptReconnect
            {
                AccountId = id, Character = character, ScriptName = scriptName, ScriptBody = scriptBody,
                Launched = true, LaunchedAt = now, Gen = row.LaunchGen, Attended = true, InPlace = true, OldPid = oldPid,
            });
            row.ReconnectInPlace = true;
            row.ReconnectInPlaceAt = now;
            AppendLuaLine("compte", scriptBody is null ? Loc.T("L.Reconnect.LogNow") : Loc.Fmt("L.Reconnect.LogNowScript", scriptName ?? "script"),
                          LuaLogLevel.Warning, oldPid);
            Toasts.Show(Loc.Fmt("L.Reconnect.ToastNow", row.Login), sound: false);
            SyncReconnectFlags();
        });
        _ = ObserveRelaunchAsync(row, oldPid, relaunch, () => committed);
    }

    // Suite de la relance en place : le pipeline de lancement affiche déjà ses erreurs ; seule une exception imprévue est
    // traitée ici (compte arrêté s'il était validé, jamais un client sain avant validation).
    private async Task ObserveRelaunchAsync(AccountRow row, int oldPid, Task<bool> relaunch, Func<bool> committed)
    {
        try { await relaunch.ConfigureAwait(true); }
        catch (Exception e)
        {
            string msg = Loc.Fmt("L.Accounts.LaunchError", e.Message);
            AppendLuaLine("compte", msg, LuaLogLevel.Danger, oldPid);
            Toasts.Show(msg, success: false);
            if (committed() && row.Running && row.Pid == oldPid) _accounts.StopForPlanning(row, msg);
        }
        finally { SyncReconnectFlags(); }
    }

    // Compte MITM HORS LIGNE (déconnecté par le serveur puis fenêtre fermée, ou relance en place ratée) : relance surveillée
    // IMMÉDIATE par la file des relances (DriveScriptReconnects), perso + script repris au retour en jeu.
    private void StartOfflineReconnect(AccountRow row)
    {
        string id = row.Model.Id;
        string character = row.CharacterName ?? row.LastCharacter ?? "";
        var (scriptName, scriptBody) = CaptureReconnectScript(row);
        _kickedScripts.Remove(id);
        bool reselect = SelectedBot is { } sb && ReferenceEquals(sb.Row, row);
        long now = Environment.TickCount64;
        DropPending(id);                 // plus de décompte « déconnexion planifiée »
        DropScriptReconnect(id);         // une relance programmée par un script est remplacée par CELLE-CI
        // Planning « connexion auto » : une exemption « rester en ligne » (hors plage) est tenue pendant la relance, sinon le
        // compte encore hors ligne la perdait et la modale de déconnexion revenait à son retour.
        if (_planConn.TryGetValue(id, out var st) && st.Override)
            st.OverrideKeepUntil = Math.Max(st.OverrideKeepUntil, now + PlanConnectNowHoldMs);
        row.Status = Loc.T("L.Reconnect.Status");
        _scriptReconnects.Add(new ScriptReconnect
        {
            AccountId = id, Character = character, DueAt = now, ScriptName = scriptName, ScriptBody = scriptBody,
            Gen = row.LaunchGen, Attended = true, Reselect = reselect,
        });
        SyncReconnectFlags();
        Toasts.Show(Loc.Fmt("L.Reconnect.ToastNow", row.Login), sound: false);
    }

    // Relance MITM en place, au point de validation (thread UI) : tout ce qui tourne sur l'ancien pid s'arrête AVANT que son
    // état ne soit oublié (sinon un script qui échoue sur la session mourante finissait « bloqué » -> pause du planning).
    // L'hôte principal garde sa source pour mémoire ; il est libéré à la re-clé (pid figé) et recréé à la reprise du script.
    private void SuspendForRelaunch(int pid)
    {
        if (_luaHosts.TryGetValue(pid, out var h) && h.Running) h.Stop("disconnected");
        BotScripts.SetRunning(pid, false);   // télémétrie : nom gardé, plus « running »
        _linkLostResume.Remove(pid);         // le script à reprendre est celui de la file (relevé avant la validation)
        if (_cmdHosts.Remove(pid, out var cmd)) { try { cmd.Dispose(); } catch { /* best-effort */ } }
        if (_reviveHosts.Remove(pid, out var rv)) { try { rv.Dispose(); } catch { /* best-effort */ } }
        _joinBossTargets.Remove(pid);
        CancelFollow(pid); ClearCombatJoin(pid); _cellMirrorBusy.Remove((uint)pid); _stuckStage.Remove(pid);
        if (_byPid.TryGetValue(pid, out var bot)) { bot.MapId = 0; bot.SelfCellId = null; }
        if (SelectedBot?.Pid == pid) LuaRunning = false;
    }

    // « Reconnecter » : la décision de déconnexion du planning (décompte affiché, déconnexion « après le combat ») visait la
    // session coupée — la relance fait oublier le combat, elle partirait sinon aussitôt et tuerait la reconnexion.
    private void ClearPlanDisconnect(string accountId)
    {
        DropPending(accountId);
        if (_planConn.TryGetValue(accountId, out var st)) { st.DisconnectAfterFight = false; st.DrainSince = 0; }
    }

    // « Reconnecter » MITM en place : la ligne du compte est passée de l'ancien pid au nouveau (AccountsViewModel.PidRekeyed,
    // thread UI, synchrone). 1) CRITIQUE d'abord : la MÊME entrée Bots (position, sélection, console, membres d'équipe) passe
    // sur le nouveau pid — sinon Resync la retirerait. 2) Nettoyage par pid, chaque étape isolée (une étape qui lève
    // n'empêche pas les suivantes). Pid réutilisé par l'OS (old == new) : aucune clé ne bouge, les hôtes sont quand même recréés.
    private void OnPidRekeyed(AccountRow row, int old, int @new)
    {
        bool moved = old != @new;
        _byPid.TryRemove(old, out var vm);
        if (vm is not null)
        {
            try { vm.Rekey(@new); }
            catch (Exception e) { TransportDiag.Emit((uint)@new, $"[reconnexion] re-clé de l'entrée : {e.Message}"); }
            _byPid[@new] = vm;
        }
        void Step(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { TransportDiag.Emit((uint)@new, $"[reconnexion] re-clé {old} -> {@new} ({what}) : {e.Message}"); }
        }
        Step("console Lua", () =>
        {
            if (moved)
                for (int i = 0; i < _allLog.Count; i++)
                    if (_allLog[i].Pid == old) _allLog[i] = _allLog[i] with { Pid = @new };
            if (vm is not null && ReferenceEquals(SelectedBot, vm)) RebuildLuaOutput();
        });
        Step("hôtes", () =>
        {
            // Pid figé à la construction : libérés ici, l'hôte principal est RECRÉÉ à la reprise (source dans la file).
            if (_luaHosts.Remove(old, out var h)) { try { h.Dispose(); } catch { /* best-effort */ } }
            if (_reviveHosts.Remove(old, out var rv)) { try { rv.Dispose(); } catch { /* best-effort */ } }
            if (_cmdHosts.Remove(old, out var cmd)) { try { cmd.Dispose(); } catch { /* best-effort */ } }
            _joinBossTargets.Remove(old);
        });
        Step("scripts", () => BotScripts.Forget(old));
        Step("botting", () => { if (_bottingPids.Remove(old)) Stores.Stats.StopBotting(row.CharacterName); });
        // Profil de combat : re-poussé au Tick pour le nouveau pid (+ onglets Planning / Paramètres / HDV du bot affiché).
        Step("profil", () => { _pushedName.Remove(old); Stores.Launch.ClearCombatProfile((uint)old); });
        if (moved)
            Step("états", () =>
            {
                if (_planningPaused.Remove(old)) _planningPaused.Add(@new);   // arrêt manuel pendant la plage : toujours tenu
                // Plage servie par le planning (et script qu'il y a lancé) : suit le bot, pour basculer au bon moment si la
                // plage change pendant la relance (le script repris est celui de l'ancienne plage).
                if (_planRun.Remove(old, out var planRun)) _planRun[@new] = planRun;
                if (_planRecheck.Remove(old)) _planRecheck.Add(@new);
                _planNoScriptLogged.Remove(old); _linkLostResume.Remove(old); _socketWaiting.Remove(old); _socketResume.Remove(old);
                _stuckStage.Remove(old); _teamAutoInvited.Remove(old); _followNextAt.Remove(old); _leaderPrevMap.Remove(old);
                _cellMirrorBusy.Remove((uint)old); _joinNextAt.Remove(old); _pendingJoin.Remove(old);
            });
        Step("suivi", () => CancelFollow(old));
        Step("équipe", ReconcileTeams);   // follower verrouillé sur le NOUVEAU pid (SetTeamFollower)
        Step("sélection", () =>
        {
            if (vm is not null) HdvFor(vm).AttachPid((uint)@new);   // VM HDV du bot reconnecté, sélectionné ou non
            if (vm is null || !ReferenceEquals(SelectedBot, vm)) return;
            LuaRunning = false;
            OnPropertyChanged(nameof(LevelCapBlocked)); OnPropertyChanged(nameof(CanRunLua));
        });
        Step("file", () => { foreach (var r in _scriptReconnects) if (r.InPlace && r.AccountId == row.Model.Id) r.Rekeyed = true; });
        Step("trajet", () =>
        {
            // Limite connue : l'enregistreur de trajet suit un pid fixe -> il devient muet sur le nouveau client.
            if (_road.IsRecording && _road.RecordingPid == (uint)old)
                AppendLuaLine("trajet", Loc.T("L.Reconnect.RoadLost"), LuaLogLevel.Warning, @new);
        });
        row.ReconnectInPlaceAt = Environment.TickCount64;   // délai de retour en jeu compté APRÈS la relance (Bouclier compris)
        OnPropertyChanged(nameof(InGameCountText));
    }

    // Arrêt MANUEL du compte (Arrêter / Arrêter tout / révocation…, via OnAccountUserStopped) : des pré-requis encore en cours
    // n'aboutissent pas à une relance. Thread UI.
    private void AbortReconnectPreflight(string accountId)
    {
        if (_reconnectPreflight.Contains(accountId)) _reconnectAborted.Add(accountId);
    }

    // Script à reprendre au retour en jeu : celui qui tourne, celui arrêté à la perte de session (MITM, TANT QUE le lien est
    // coupé) ou suspendu pendant une coupure (socket) — sauf arrêt manuel entre-temps —, ou celui d'un compte MITM arrêté après
    // une déconnexion imposée par le serveur / une relance en place ratée.
    private (string? Name, string? Body) CaptureReconnectScript(AccountRow row)
    {
        if (row.Pid is int pid && pid > 0 && _luaHosts.TryGetValue(pid, out var h)
            && (h.Running || (((_linkLostResume.Contains(pid) && Stores.Launch.IsLinkDown((uint)pid)) || _socketResume.Contains(pid))
                              && !_planningPaused.Contains(pid)))
            && h.LastScriptText is { Length: > 0 } body)
            return (BotScripts.Get(pid)?.Name, body);
        if (!row.Running && row.ServerKickedOffline && _kickedScripts.TryGetValue(row.Model.Id, out var k)) return (k.Name, k.Body);
        return (null, null);
    }

    // Resync : un bot MITM arrêté après une déconnexion IMPOSÉE par le serveur (fenêtre Dofus fermée ensuite) quitte la liste
    // (son hôte Lua va être libéré) -> son script (s'il tournait, ou arrêté par la coupure) est gardé pour « Reconnecter ».
    // Appelé AVANT BotScripts.Forget / Dispose de l'hôte. Le lien n'existe plus ici (Forget) : ServerKickedOffline tient lieu
    // de « lien coupé » pour _linkLostResume. (Un bot socket déconnecté par le serveur reste listé : session parquée.)
    private void KeepKickedScript(BotRowViewModel b)
    {
        string id = b.Row.Model.Id;
        _kickedScripts.Remove(id);
        if (!b.Row.ServerKickedOffline || !_luaHosts.TryGetValue(b.Pid, out var h)) return;
        bool wanted = h.Running || ((_socketResume.Contains(b.Pid) || _linkLostResume.Contains(b.Pid)) && !_planningPaused.Contains(b.Pid));
        if (wanted && h.LastScriptText is { Length: > 0 } body) _kickedScripts[id] = (BotScripts.Get(b.Pid)?.Name, body);
    }

    // Bouton « Reconnecter » grisé tant qu'une reconnexion MANUELLE du compte est en cours : pré-requis, relance MITM en place
    // (sans limite de durée : code du Bouclier), reconnexion en place pas encore de retour en jeu (au plus ScriptResumeMaxMs),
    // relance hors ligne pas encore partie / perso pas encore en jeu. Thread UI (tick + actions).
    private void SyncReconnectFlags()
    {
        long now = Environment.TickCount64;
        foreach (var row in _accounts.Accounts)
        {
            string id = row.Model.Id;
            bool relaunching = _accounts.IsRelaunching(row);
            // De retour en jeu : lien non coupé + carte chargée ; socket : sur une NOUVELLE session (pas celle coupée / terminée).
            bool back = !row.SessionTransitionActive && !relaunching && row.Pid is int p && p > 0 && !Stores.Launch.IsLinkDown((uint)p) && Stores.Launch.CurrentMap((uint)p) is not null
                        && (!row.IsSocketSession || Volatile.Read(ref row.SocketSessionSeq) != row.ReconnectSeq);
            if (row.ReconnectInPlace && !row.SessionTransitionActive && !relaunching && (!row.Running || back || now - row.ReconnectInPlaceAt > ScriptResumeMaxMs))
            {
                row.ReconnectInPlace = false;
                // MITM : la file des relances journalise le retour (et la reprise du script).
                if (back && row.IsSocketSession && row.Pid is int bp) AppendLuaLine("compte", Loc.T("L.Reconnect.Done"), LuaLogLevel.Success, bp);
            }
            bool pending = row.SessionTransitionActive || (_reconnectPreflight.Contains(id) && !_reconnectAborted.Contains(id))
                || relaunching || row.ReconnectInPlace
                || _scriptReconnects.Any(r => r.Attended && r.AccountId == id
                       && (!r.Launched || row.Pid is not int q || q <= 0 || Stores.Launch.CurrentMap((uint)q) is null));
            if (row.ReconnectPending != pending) row.ReconnectPending = pending;
        }
    }
}
