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

// ===================== TeamPilot : suivi synthétique des followers, auto-invitation, combat coordonné (auto-join) =====================
public partial class BotsViewModel
{
    // ---- TeamPilot : suivi synthétique (appelé au Tick, après UpdateTeam) ----
    // Pour chaque équipe active dont le suivi est ON, chaque follower dont la carte diffère de celle de son chef part en
    // voyage vers la carte du chef (échelonné par SyncMs), via le voyage world-graph (aucun paquet party). Calque un bot tiers.
    private void DriveTeamFollow()
    {
        if (!LicenseService.CanBot) return;   // killswitch : pas de session valide -> aucun pilotage
        long now = Environment.TickCount64;
        foreach (var team in Teams)
        {
            if (team.Leader is { Pid: > 0 } leader)
                foreach (var f in team.Members.Where(f => !ReferenceEquals(f, leader) && f.Pid > 0))
                {
                    Stores.Launch.ConfigureNativeFollow((uint)f.Pid, (uint)leader.Pid, team.Active && team.FollowEnabled);
                    // Annule le rattrapage seulement quand le suivi natif peut vraiment agir : suiveur sur la carte du chef.
                    if (Stores.Launch.NativeFollowConfirmed((uint)f.Pid, (uint)leader.Pid)
                        && (Stores.Launch.CurrentMap((uint)f.Pid) ?? 0) == (Stores.Launch.CurrentMap((uint)leader.Pid) ?? -1))
                        CancelFollow(f.Pid);
                }
            DriveFollowForTeam(team, now);
        }
    }

    // Pilote le suivi d'UNE équipe. Appelé au Tick (toutes les équipes) ET, en suivi SIMULTANÉ, DÈS que le chef change de
    // carte (OnLeaderMapChanged) pour repartir sans attendre le prochain tick. En mode simultané le follower CHASSE la carte
    // vivante du chef (FollowLeaderMapAsync, re-lue à chaque saut) avec un répit quasi nul ; sinon voyage vers la carte figée.
    private void DriveFollowForTeam(Team team, long now)
    {
        if (!team.Active || !team.FollowEnabled || team.Leader is not { } leader || leader.Pid <= 0) return;
        long leaderMap = Stores.Launch.CurrentMap((uint)leader.Pid) ?? 0;
        if (leaderMap == 0) return;
        bool leaderInFight = Stores.Launch.IsInFight((uint)leader.Pid);
        // Chef sur une carte HORS world-graph (hors combat) : coord (0,0), instance ou zone non extraite. Y router un
        // follower le ferait ERRER vers la carte du monde en (0,0) (le « follower s'est déplacé seul » observé) ou échouer
        // en boucle. On s'abstient et on attend que le chef revienne sur une carte connue. (En COMBAT la carte du chef EST
        // l'instance off-graph -> normal, on ne signale pas.)
        if (!leaderInFight && !Stores.Launch.WorldGraphKnows((int)leaderMap))
        {
            if (_followOffGraphLogged.Add(leaderMap))
                Stores.Launch.TeamLog($"[équipe] {leader.Row.CharacterName} sur une carte hors world-graph ({leaderMap}) → suivi en pause (les followers restent en place au lieu d'errer).");
            return;
        }
        const bool tight = true;   // suivi un bot tiers par défaut: chasse serrée de la carte vivante du chef (plus de mode « simultané/collé » séparé)
        foreach (var f in team.Members)
        {
            if (ReferenceEquals(f, leader) || f.Pid <= 0) continue;
            int pid = f.Pid;
            // Le suivi NATIF du jeu ne fait suivre que d'une carte voisine du chef : un suiveur sur une AUTRE carte (reconnexion,
            // zaap, havre-sac) doit encore etre ramene par ce rattrapage, sinon le chef l'attend sans fin avant de partir (02/11).
            if (_teamPiloted.Contains(pid)) continue;                                  // déjà en voyage/chasse de suivi
            if (Stores.Launch.IsLinkDown((uint)pid)) continue;                         // session de jeu perdue (MITM) : rien à suivre
            if (leaderInFight || Stores.Launch.IsInFight((uint)pid)) continue;         // combat -> on attend (auto-join = Lot 3)
            long fMap = Stores.Launch.CurrentMap((uint)pid) ?? 0;
            if (fMap == 0) continue;                                                   // pas (encore) en jeu (connexion, reconnexion) : rien à router
            if (fMap == leaderMap) continue;                                           // déjà sur la carte du chef
            if (_luaHosts.TryGetValue(pid, out var h) && h.Running) continue;         // un script pilote ce bot -> on cède
            if (_followNextAt.TryGetValue(pid, out var next) && now < next) continue;  // cooldown anti-boucle
            StartFollowTravel(pid, (uint)leader.Pid, leaderMap, team.SyncMinMs, team.SyncMaxMs, tight);
        }
    }

    // SUIVI COLLÉ niveau CELLULE: quand le CHEF d'une équipe en suivi collé MARCHE vers
    // une cellule (event SelfMoved, injecté OU jrw manuel), chaque follower DÉJÀ sur sa carte marche vers la MÊME cellule ->
    // ils se déplacent pareil À L'INTÉRIEUR d'une carte, pas seulement à chaque changement de carte. MoveTo a sa propre garde
    // « un seul move à la fois » (anti-spam intégré) et ne ré-émet SelfMoved que pour le déplacé -> pas de boucle de rétroaction.
    private void OnLeaderSelfMoved(uint pid, IReadOnlyList<int> path)
    {
        if (path is null || path.Count == 0) return;
        int destCell = path[path.Count - 1];
        Dispatcher.UIThread.Post(() =>
        {
            if (!LicenseService.CanBot) return;
            foreach (var team in Teams)
            {
                // Suivi RAW-MOVE actif dès que le suivi est ON: c'est la SEULE méthode qui marche en zone off-graph
                // (Incarnam & co, où coord et world-graph sont vides). Plus besoin d'une case « collé » séparée.
                if (!team.Active || !team.FollowEnabled) continue;
                if (team.Leader is not { } leader || (uint)leader.Pid != pid) continue;
                if (Stores.Launch.IsInFight((uint)leader.Pid)) return;                 // pas de mirroring en combat (l'auto-join gère)
                long leaderMap = Stores.Launch.CurrentMap((uint)leader.Pid) ?? 0;
                if (leaderMap == 0) return;
                int idx = 0;
                foreach (var f in team.Members)
                {
                    if (ReferenceEquals(f, leader) || f.Pid <= 0) continue;
                    if (Stores.Launch.NativeFollowConfirmed((uint)f.Pid, (uint)leader.Pid)) continue;
                    if (_teamPiloted.Contains(f.Pid)) continue;                        // en rattrapage de CARTE -> pas de cell-mirror
                    if (Stores.Launch.IsInFight((uint)f.Pid)) continue;
                    if ((Stores.Launch.CurrentMap((uint)f.Pid) ?? 0) != leaderMap) continue;   // pas encore sur la carte du chef -> le suivi de carte s'en charge
                    StartCellMirror((uint)f.Pid, (uint)leader.Pid, leaderMap, idx++, team.SyncMinMs, team.SyncMaxMs);
                }
                return;
            }
        });
    }

    // SUIVI COLLÉ — rejeu de la SORTIE DE CARTE: le chef vient de décider de quitter sa carte dans une
    // DIRECTION. Chaque follower co-localisé rejoue EXACTEMENT cette direction (ChangeMap(dir)), SANS world-graph ni coord ->
    // marche en zone OFF-GRAPH (Incarnam/instance) où le pathfinding échoue. C'est le remplaçant off-graph-safe de la dérivation
    // par coordonnée (LockstepDir), qui, elle, donnait null en (0,0) et retombait sur la chasse graphe (bloquée).
    private void OnLeaderMapExit(uint pid, string dir) => Dispatcher.UIThread.Post(() =>
    {
        if (!LicenseService.CanBot) return;
        foreach (var team in Teams)
        {
            if (!team.Active || !team.FollowEnabled) continue;
            if (team.Leader is not { } leader || (uint)leader.Pid != pid) continue;
            if (Stores.Launch.IsInFight((uint)leader.Pid)) return;
            long chiefMap = Stores.Launch.CurrentMap((uint)leader.Pid) ?? 0;   // encore la carte de DÉPART (transition pas encore franchie)
            if (chiefMap == 0) return;
            foreach (var f in team.Members)
            {
                if (ReferenceEquals(f, leader) || f.Pid <= 0) continue;
                if (Stores.Launch.NativeFollowConfirmed((uint)f.Pid, (uint)leader.Pid)) continue;
                if (Stores.Launch.IsInFight((uint)f.Pid) || _teamPiloted.Contains(f.Pid)) continue;
                long fMap = Stores.Launch.CurrentMap((uint)f.Pid) ?? 0;
                if (fMap != chiefMap)   // pas co-localisé -> il ne PEUT PAS rejouer la sortie (c'est LÀ que la mule décroche off-graph)
                {
                    Stores.Launch.TeamLog($"[équipe] {f.Row.CharacterName} : chef sort ({dir}) mais mule PAS sur sa carte ({fMap} vs {chiefMap}) -> décroché, ne suit pas");
                    continue;
                }
                _cellMirrorBusy.Remove((uint)f.Pid);   // stoppe une chasse de cellule -> priorité au changement de carte
                Stores.Launch.TeamLog($"[équipe] {f.Row.CharacterName} : rejoue la sortie du chef ({dir})");
                StartFollowStep(f.Pid, (uint)leader.Pid, dir, team.SyncMinMs, team.SyncMaxMs);
            }
            return;
        }
    });

    // Hôte Lua TRANSITOIRE de suivi AVEC ses logs routés vers le journal + la console Équipe (attribués au follower) : sinon
    // les actions de suivi de la mule (carte/trajet) étaient INVISIBLES (l'hôte transitoire n'avait aucun Log branché) ->
    // impossible de voir pourquoi elle ne suit/rejoint pas.
    private GivreLuaHost NewFollowHost(int pid)
    {
        var h = new GivreLuaHost((uint)pid, Stores.Launch, Stores.World);
        h.Log += entry => Dispatcher.UIThread.Post(() =>
        {
            var row = MakeRow(entry, pid);
            _allLog.Add(row); while (_allLog.Count > 2000) _allLog.RemoveAt(0);
            AppendTeamConsole(row);
            if (pid == LogPid && Passes(row)) { LuaOutput.Add(row); while (LuaOutput.Count > 600) LuaOutput.RemoveAt(0); }
        });
        return h;
    }

    // CHASSE de cellule (suivi collé intra-carte) : le follower REJOINT la cellule VIVANTE du chef, re-lue à chaque pas et
    // attendue jusqu'à l'arrivée avant de re-viser. Corrige le « une fois sur deux » : la garde MoveTo (un seul move à la fois)
    // jetait les pas alternés d'un simple fire-and-forget. UNE seule chasse par follower (_cellMirrorBusy). S'arrête net si le
    // CHEF entre en combat -> le follower est LIBRE pour rejoindre le combat (sinon il mirroir-marchait et ratait la fenêtre).
    private void StartCellMirror(uint fpid, uint leaderPid, long map, int index, int syncMinMs, int syncMaxMs)
    {
        if (!_cellMirrorBusy.Add(fpid)) return;
        int lo = Math.Max(0, syncMinMs), hi = Math.Max(lo, syncMaxMs);
        int stagger = (lo == hi ? lo : Random.Shared.Next(lo, hi + 1)) + index * 80;
        _ = Task.Run(async () =>
        {
            try
            {
                if (stagger > 0) await Task.Delay(stagger).ConfigureAwait(false);
                for (int stepN = 0; stepN < 16; stepN++)
                {
                    if (Stores.Launch.NativeFollowConfirmed(fpid, leaderPid)) break;
                    if ((Stores.Launch.CurrentMap(fpid) ?? 0) != map || Stores.Launch.IsInFight(fpid)) break;
                    if (Stores.Launch.IsInFight(leaderPid)) break;                     // chef en combat -> on lâche : le follower doit rejoindre, pas marcher
                    var lead = Stores.World.Get(leaderPid);
                    var self = Stores.World.Get(fpid);
                    if (lead.SelfCell is not int lc || self.SelfCell is not int sc) break;
                    int destination = TeamFollowPosition.AdjacentDestination(sc, lc, self.IsWalkable, self.StepAllowed);
                    if (destination < 0 || destination == sc) break;
                    if (!Stores.Launch.MoveTo(fpid, map, sc, destination, c => c != lc && self.IsWalkable(c), true, self.StepAllowed))
                    { await Task.Delay(250).ConfigureAwait(false); continue; }         // refusé (encore en move) -> re-vise vite
                    for (int w = 0; w < 18; w++)                                        // attend l'arrivée (SelfCell == cible) ou ~2,3 s
                    {
                        await Task.Delay(130).ConfigureAwait(false);
                        if ((Stores.World.Get(fpid).SelfCell ?? -1) == destination) break;
                    }
                }
            }
            catch { /* best-effort */ }
            finally { Dispatcher.UIThread.Post(() => _cellMirrorBusy.Remove(fpid)); }
        });
    }

    // Rejoue UN pas directionnel (comme StartFollowTravel mais via StepMapDirectionAsync). Si le pas collé échoue (sortie
    // bloquée pour CE follower), repli sur la chasse de la carte vivante du chef -> jamais coincé.
    private void StartFollowStep(int pid, uint leaderPid, string dir, int syncMinMs, int syncMaxMs)
    {
        _teamPiloted.Add(pid);
        var cts = new CancellationTokenSource();
        _followCts[pid] = cts;
        int lo = Math.Max(0, syncMinMs), hi = Math.Max(lo, syncMaxMs);
        int stagger = lo == hi ? lo : Random.Shared.Next(lo, hi + 1);
        _ = Task.Run(async () =>
        {
            var host = NewFollowHost(pid);
            try
            {
                if (stagger > 0) await Task.Delay(stagger, cts.Token).ConfigureAwait(false);
                bool ok = await host.StepMapDirectionAsync(dir, cts.Token).ConfigureAwait(false);
                if (!ok) await host.FollowLeaderMapAsync(() => Stores.Launch.CurrentMap(leaderPid) ?? 0, cts.Token).ConfigureAwait(false);
            }
            catch { /* annulé / échec -> best-effort */ }
            finally
            {
                try { host.Dispose(); } catch { }
                cts.Dispose();
                Dispatcher.UIThread.Post(() =>
                {
                    _teamPiloted.Remove(pid);
                    if (_followCts.TryGetValue(pid, out var c) && ReferenceEquals(c, cts)) _followCts.Remove(pid);
                    _followNextAt[pid] = Environment.TickCount64 + 150;   // collé : re-évalue quasi aussitôt
                });
            }
        });
    }

    // ---- Auto-invitation (« Connecter en jeu ») : appelé au Tick (throttle ~1,5 s), après ReconcileTeams ----
    // Pour chaque équipe armée : dès que le chef est en jeu et qu'un follower est prêt, on forme le groupe (le chef
    // invite) ; les followers RETARDATAIRES sont invités un à un à mesure qu'ils arrivent. On désarme quand tous les
    // membres attendus sont invités, ou après expiration de la fenêtre (comptes qui ne se sont pas connectés).
    private void DriveTeamAutoForm()
    {
        if (_autoFormTeams.Count == 0) return;
        long now = Environment.TickCount64;
        foreach (var team in Teams.ToList())
        {
            if (!_autoFormTeams.Contains(team.Id)) continue;
            if (_autoFormArmedAt.TryGetValue(team.Id, out var armedAt) && now - armedAt > AutoFormWindowMs)
            { DisarmAutoForm(team); continue; }                       // fenêtre expirée -> abandon des absents
            if (team.Leader is not { } leader || leader.Pid <= 0) continue;   // chef pas encore en jeu -> on patiente
            var followers = team.Members.Where(m => !ReferenceEquals(m, leader) && SameServer(m, leader)).ToList();
            if (followers.Count == 0) continue;                       // aucun follower en jeu encore -> on patiente

            if (!team.Active)
            {
                FormTeam(team);                                       // 1re fois : verrouille + stoppe scripts solo + invite le lot présent
                foreach (var f in followers) if (f.Pid > 0) _teamAutoInvited.Add(f.Pid);
            }
            else
                foreach (var f in followers)                          // retardataires : groupe déjà formé -> invite ceux qui viennent d'arriver
                    if (f.Pid > 0 && f.Row.CharacterName is { Length: > 0 } fn && _teamAutoInvited.Add(f.Pid))
                        Stores.Launch.PartyInvite((uint)leader.Pid, fn);

            // Tous les followers du ROSTER sont en jeu et invités -> mission remplie, on désarme.
            int rosterFollowers = team.Roster.Count(e => !e.Leader);
            int invitedInGame = followers.Count(f => f.Pid > 0 && _teamAutoInvited.Contains(f.Pid));
            if (invitedInGame >= rosterFollowers) DisarmAutoForm(team);
        }
    }

    // Désarme l'auto-invitation d'une équipe et oublie les pids invités de SES membres (pids réutilisables entre sessions).
    private void DisarmAutoForm(Team team)
    {
        _autoFormTeams.Remove(team.Id);
        _autoFormArmedAt.Remove(team.Id);
        foreach (var m in team.Members) _teamAutoInvited.Remove(m.Pid);
    }

    // Lance UN suivi pour un follower (hôte Lua transitoire, comme HdvViewModel). Sérialisé par pid via _teamPiloted ;
    // annulable par _followCts. `tight` = suivi SIMULTANÉ : chasse la carte VIVANTE du chef (leaderPid re-lu à chaque saut)
    // avec un répit quasi nul -> re-chasse aussitôt si le chef a rebougé ; sinon voyage vers la carte FIGÉE + répit 1,5 s.
    private void StartFollowTravel(int pid, uint leaderPid, long targetMap, int syncMinMs, int syncMaxMs, bool tight)
    {
        _teamPiloted.Add(pid);
        var cts = new CancellationTokenSource();
        _followCts[pid] = cts;
        int lo = Math.Max(0, syncMinMs), hi = Math.Max(lo, syncMaxMs);
        int stagger = lo == hi ? lo : Random.Shared.Next(lo, hi + 1);   // délai inter-bots ALÉATOIRE dans [min,max]
        _ = Task.Run(async () =>
        {
            var host = NewFollowHost(pid);
            try
            {
                if (stagger > 0) await Task.Delay(stagger, cts.Token).ConfigureAwait(false);
                if (tight)
                    await host.FollowLeaderMapAsync(() => Stores.Launch.CurrentMap(leaderPid) ?? 0, cts.Token).ConfigureAwait(false);
                else
                    await host.TravelToMapAsync(targetMap, cts.Token).ConfigureAwait(false);
            }
            catch { /* annulé / échec -> best-effort, on re-tentera au prochain Tick */ }
            finally
            {
                try { host.Dispose(); } catch { }
                cts.Dispose();
                Dispatcher.UIThread.Post(() =>
                {
                    _teamPiloted.Remove(pid);
                    if (_followCts.TryGetValue(pid, out var c) && ReferenceEquals(c, cts)) _followCts.Remove(pid);
                    _followNextAt[pid] = Environment.TickCount64 + (tight ? 150 : 1500);   // simultané : re-chasse quasi immédiate ; sinon répit
                });
            }
        });
    }

    private void CancelFollow(int pid)
    { if (_followCts.TryGetValue(pid, out var cts)) { try { cts.Cancel(); } catch { } } }

    private void CancelAllFollows()
    { foreach (var cts in _followCts.Values.ToList()) { try { cts.Cancel(); } catch { } } }

    // ---- Combat coordonné (Lot 3) : auto-join EVENT-DRIVEN ----
    // Le follower reçoit PartyMemberInFightEvent (le chef entre en combat) -> OnPartyMemberInFight enregistre une demande
    // de join (fight_id + id du chef + deadline). DriveTeamCombat (Tick) la (re)tente jusqu'à l'entrée ou l'expiration.
    // NB : on NE compare PLUS la carte courante du chef (elle devient l'instance de COMBAT pendant le combat) — l'event
    // porte directement le fight_id, ce qui rendait l'ancien test d'égalité de carte toujours faux.
    private void OnPartyMemberInFight(uint pid, long memberId, long fightId)
        => Dispatcher.UIThread.Post(() =>
        {
            if (fightId == 0 || memberId == 0) return;
            // AGGRO PARTAGÉE (any-member) : le receveur peut être le chef OU un follower — le combat de N'IMPORTE QUEL
            // coéquipier déclenche le join (avant : follower-only + combat DU CHEF uniquement).
            if (!_byPid.TryGetValue((int)pid, out var me) || me.TeamId is null) return;
            if (Teams.FirstOrDefault(t => t.Id == me.TeamId) is not { Active: true, CombatJoinEnabled: true } team) return;
            if (Stores.Launch.CharacterId(pid) == memberId) return;   // ne JAMAIS rejoindre mon propre combat
            // le combattant memberId doit être un MEMBRE CONNU de mon équipe (un PartyMemberInFightEvent peut venir d'un
            // groupe manuel avec un étranger -> on ne pile pas sur le combat d'un non-coéquipier).
            if (!team.Members.Any(m => m.Pid > 0 && Stores.Launch.CharacterId((uint)m.Pid) == memberId)) return;
            if (Stores.Launch.IsInFight(pid)) return;
            _pendingJoin[(int)pid] = (fightId, memberId, Environment.TickCount64 + 9000);   // fenêtre de placement ~9 s
            long myMap = Stores.Launch.CurrentMap(pid) ?? 0;
            Stores.Launch.TeamLog($"[équipe] {me.Row.CharacterName} : coéquipier en combat {fightId} -> tentative de rejoint (ma carte {myMap})");
            // ⭐ JOIN IMMÉDIAT : la fenêtre joignable (ShowChallenge hpo -> RemoveChallenge hpr) peut ne durer que ~0,6 s. On NE
            // PEUT PAS attendre le prochain Tick (~300 ms) + l'échelonnement aléatoire SyncMin/SyncMax (200-800 ms, fait pour
            // HUMANISER le SUIVI, pas le combat) : ça faisait partir le 1er join à ~0,8 s = TROP TARD (fenêtre déjà fermée).
            // On est déjà sur le thread UI -> on injecte le 1er FightJoinRequest TOUT DE SUITE ; DriveTeamCombat ne fait plus
            // que re-tenter rapidement (JoinRetryMs) jusqu'à l'entrée ou la deadline. fighter_id = memberId (= id perso du chef).
            Stores.Launch.FightJoin(pid, fightId, memberId);
            _joinNextAt[(int)pid] = Environment.TickCount64 + JoinRetryMs;
        });

    private void DriveTeamCombat()
    {
        if (_pendingJoin.Count == 0) return;
        if (!LicenseService.CanBot) { _pendingJoin.Clear(); return; }
        long now = Environment.TickCount64;
        foreach (var (pid, info) in _pendingJoin.ToList())
        {
            if (!_byPid.TryGetValue(pid, out _) || Stores.Launch.IsInFight((uint)pid) || now > info.deadline)
            { _pendingJoin.Remove(pid); _joinNextAt.Remove(pid); continue; }   // rejoint / expiré / bot fermé
            if (_joinNextAt.TryGetValue(pid, out var next) && now < next) continue;   // fenêtre de re-tentative rapide (JoinRetryMs)
            ScheduleJoin(pid, info.fightId, info.memberId);   // aucune condition de carte (l'event porte le fight_id ; cf note ci-dessus)
        }
    }

    // Re-tentative de join. Le 1er FightJoinRequest est envoyé IMMÉDIATEMENT dans OnPartyMemberInFight ; ici on ne fait que
    // ré-injecter, SANS échelonnement aléatoire (le join est CRITIQUE en temps : la fenêtre joignable ShowChallenge->
    // RemoveChallenge peut ne durer que ~0,6 s). On reprogramme juste la prochaine tentative rapide (JoinRetryMs).
    private void ScheduleJoin(int pid, long fightId, long chefId)
    {
        _joinNextAt[pid] = Environment.TickCount64 + JoinRetryMs;
        if (!Stores.Launch.IsInFight((uint)pid))
            Stores.Launch.FightJoin((uint)pid, fightId, chefId);
    }

    private void ClearCombatJoin(int pid)
    { _joinNextAt.Remove(pid); _pendingJoin.Remove(pid); }

    // --- TeamPilot (suivi synthétique) : un follower rejoint la carte de son chef via le voyage world-graph, sans paquet
    // party. Sérialisé PAR PID (_moveWait de LaunchService est mono-slot -> 2 voyages concurrents sur le
    // même pid se cassent), exclusif avec le scheduler de planning (via _teamPiloted, cf EvaluatePlanning).
    private readonly HashSet<int> _teamPiloted = new();                       // followers dont un voyage de suivi est EN COURS
    private readonly Dictionary<int, CancellationTokenSource> _followCts = new();
    private readonly Dictionary<int, long> _followNextAt = new();             // cooldown anti-boucle par pid (TickCount64)
    private readonly HashSet<long> _followOffGraphLogged = new();             // cartes chef HORS graphe déjà signalées (anti-spam du log de suivi)
    private readonly Dictionary<int, long> _leaderPrevMap = new();            // dernière carte connue de chaque chef (pour dériver sa DIRECTION de sortie en suivi collé)
    private readonly HashSet<uint> _cellMirrorBusy = new();                   // followers dont une CHASSE de cellule (suivi collé intra-carte) est EN COURS
    // Auto-join de combat (Lot 3) : le chef entre en combat -> le follower rejoint son fight_id. 1er join IMMÉDIAT (fenêtre
    // joignable courte ~0,6 s), puis re-tentatives rapides toutes les JoinRetryMs jusqu'à l'entrée ou la deadline.
    private const int JoinRetryMs = 400;                                     // cadence de re-tentative (avant : 2500 ms = trop lent, ratait la fenêtre)
    private readonly Dictionary<int, long> _joinNextAt = new();              // fenêtre de re-tentative par pid (TickCount64)
    // Auto-join EVENT-DRIVEN : un follower a reçu PartyMemberInFightEvent -> join à retenter jusqu'à entrée ou deadline.
    private readonly Dictionary<int, (long fightId, long memberId, long deadline)> _pendingJoin = new();
}
