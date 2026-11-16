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

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("GivreClone.Tests")]

namespace GivreClone.App.Services;

// ===================== Équipe : groupe Dofus (invitation / acceptation / départ), auto-join de combat, composition pour le script =====================
public sealed partial class LaunchService
{
    // ===== ÉQUIPE / GROUPE (multi-comptes) : invitation par NOM, acceptation, quitter. Code+champs résolus par nom
    //       via le mapping streamé (MoveForge REFUSE si un champ manque -> jamais de trame partielle). =====

    /// <summary>Invitation reçue : (pid, from_player_id, from_player_name, party_id). Le VM auto-accepte si l'invit vient de NOTRE chef.</summary>
    public event Action<uint, long, string, long>? PartyInvited;

    /// <summary>Un MEMBRE du groupe est entré en combat (PartyMemberInFightEvent) : (pid receveur, member_id = combattant/chef,
    /// fight_id). Sert à l'auto-join coordonné d'équipe (le follower rejoint ce fight_id) — indépendant de la carte courante.</summary>
    public event Action<uint, long, long>? PartyMemberInFight;

    /// <summary>Le bot `pid` invite `characterName` (même serveur) dans son groupe. false si non forgeable / pas de relais.</summary>
    /// <summary>Motif d'échec de la dernière <see cref="PartyInvite"/> (pour l'afficher à l'utilisateur), ou null si succès.</summary>
    public string? LastPartyInviteError { get; private set; }

    public bool PartyInvite(uint pid, string characterName)
    {
        LastPartyInviteError = null;
        if (!LicenseService.HasPermission(LicenseService.Perm.Multi)) { LastPartyInviteError = "droit « multi » (multi-comptes) requis"; return false; }   // groupe/équipe = feature multi-comptes -> POINT D'ACTION (verbe Lua team:invite + UI contournent CanMulti)
        if (string.IsNullOrWhiteSpace(characterName)) { LastPartyInviteError = "nom vide"; TransportLog.Line($"[équipe] invitation ignorée : nom vide (pid {pid})"); return false; }
        var relay = RelayForInject();
        if (relay is null) { LastPartyInviteError = "relais/licence absent (droit proxy ?)"; TransportLog.Line($"[équipe] invitation IMPOSSIBLE : relais/licence absent (pid {pid} -> {characterName})"); return false; }
        byte[] pkt = MoveForge.BuildPartyInviteFramed(characterName.Trim());
        if (pkt.Length == 0) { LastPartyInviteError = MoveForge.LastForgeError ?? "forge refusée"; TransportLog.Line($"[équipe] invitation NON forgée pid {pid} -> {characterName} : {LastPartyInviteError}"); return false; }
        bool ok = relay.Inject(pid, pkt);
        if (!ok) LastPartyInviteError = "injection refusée";
        ShowInjected(pid, pkt);
        TransportLog.Line($"[équipe] invitation envoyée pid {pid} -> {characterName} : {pkt.Length} o, inject={(ok ? "OK" : "échec")}");
        return ok;
    }

    /// <summary>Le bot `pid` accepte l'invitation du groupe `partyId`.</summary>
    public bool PartyAccept(uint pid, long partyId)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Multi)) return false;   // groupe/équipe = Perm.Multi au POINT D'ACTION
        var relay = RelayForInject();
        if (relay is null || partyId == 0) return false;
        byte[] pkt = MoveForge.BuildPartyAcceptFramed(partyId);
        if (pkt.Length == 0) { TransportLog.Line($"[équipe] acceptation NON forgée pid {pid} party {partyId}"); return false; }
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[équipe] invitation acceptée pid {pid} party {partyId} ({(ok ? "injectée" : "échec")})");
        return ok;
    }

    /// <summary>Le bot `pid` quitte le groupe `partyId`.</summary>
    public bool PartyLeave(uint pid, long partyId)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Multi)) return false;   // groupe/équipe = Perm.Multi au POINT D'ACTION
        var relay = RelayForInject();
        if (relay is null || partyId == 0) return false;
        byte[] pkt = MoveForge.BuildPartyLeaveFramed(partyId);
        if (pkt.Length == 0) return false;
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[équipe] quitte le groupe pid {pid} party {partyId}");
        return ok;
    }

    /// <summary>Écrit une ligne de diagnostic ÉQUIPE dans la console transport (utile pour tracer le suivi/l'auto-join) ET
    /// la diffuse à la console « Équipe » commune de l'UI (event TeamLogged).</summary>
    public void TeamLog(string message) { TransportLog.Line(message); TeamLogged?.Invoke(message); }
    /// <summary>Ligne de diagnostic ÉQUIPE (orchestration : invitations, suivi, auto-join) -> console Équipe commune.</summary>
    public event Action<string>? TeamLogged;

    /// <summary>Le bot `pid` REJOINT le combat `fightId` du camp de `fighterId` (le chef) : auto-join coordonné d'équipe.
    /// Prérequis (assurés par l'appelant) : le follower est sur la carte du chef et pas déjà en combat. false si non
    /// forgeable / pas de relais.</summary>
    public bool FightJoin(uint pid, long fightId, long fighterId)
    {
        if (!CanJoinTeamFight(pid, fightId, fighterId)) return false;
        if (!LicenseService.HasPermission(LicenseService.Perm.Fight)) return false;   // rejoindre/participer à un combat = Perm.Fight (sibling d'AttackMonster, oublié au lot combat : auto-join d'équipe)
        var relay = RelayForInject();
        if (relay is null || fightId == 0) return false;
        byte[] pkt = MoveForge.BuildFightJoinFramed(fightId, fighterId);
        if (pkt.Length == 0) { TeamLog($"[équipe] join NON forgé pid {pid} fight {fightId} : {MoveForge.LastForgeError}"); return false; }
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        TeamLog($"[équipe] join envoyé pid {pid} -> fight {fightId} (chef {fighterId}) : {(ok ? "injecté" : "échec inject")} — ma carte {CurrentMap(pid)}");
        return ok;
    }

    // Les tentatives de join sont conservées ~9 s côté VM. Une équipe désactivée, un départ de groupe, une re-clé
    // ou un nouveau personnage peuvent survenir entre l'event et une re-tentative : revalider au point d'action.
    private bool CanJoinTeamFight(uint pid, long fightId, long fighterId)
    {
        if (pid == 0 || fightId <= 0 || fighterId <= 0 || CurrentMap(pid) is not > 0
            || IsLinkSuspended(pid) || IsInFight(pid)) return false;
        var snapshot = _teamView;
        if (!snapshot.TryGetValue(pid, out var view)
            || !_teamCombatWait.TryGetValue(view.LeaderPid, out var followers)) return false;
        var me = view.Members.FirstOrDefault(m => m.Pid == pid);
        var fighter = view.Members.FirstOrDefault(m => m.Pid != pid && CharacterId(m.Pid) == fighterId);
        if (me.Pid == 0 || fighter.Pid == 0
            || !snapshot.TryGetValue(fighter.Pid, out var other) || !ReferenceEquals(view, other)
            || (pid != view.LeaderPid && !followers.Contains(pid))
            || (fighter.Pid != view.LeaderPid && !followers.Contains(fighter.Pid))) return false;
        // Le PID seul ne prouve pas l'identité (reconnexion, réutilisation Windows, homonyme sur un autre serveur).
        if (CharacterId(pid) == 0 || SelfCharacterInfo(pid)?.Name != me.Name
            || SelfCharacterInfo(fighter.Pid)?.Name != fighter.Name
            || ServerIdOf(pid) == 0 || ServerIdOf(pid) != ServerIdOf(fighter.Pid)
            || IsLinkSuspended(fighter.Pid) || CurrentMap(fighter.Pid) is not > 0) return false;
        if (PartyIdOf(pid) == 0 || !IsInParty(pid, fighterId)) return false; // membre parti / groupe supprimé
        long currentFight = CurrentFightId(fighter.Pid);
        // L'event de groupe peut précéder FightStartingEvent sur la connexion du combattant : 0 reste inconnu,
        // mais un identifiant connu différent interdit de rejoindre un ancien combat.
        return currentFight == 0 || currentFight == fightId;
    }

    // pid -> ce bot est un FOLLOWER (mule) d'une équipe ACTIVE. Le follower NE décide PAS des challenges (c'est le CHEF qui
    // choisit) : en pré-combat il se contente de se PLACER puis de faire PRÊT, sans demander/valider de challenge ni attendre
    // la gate de 4,5 s. Positionné par BotsViewModel (même condition que FollowerLocked) ; volatile car lu depuis le thread combat.
    private readonly ConcurrentDictionary<uint, bool> _teamFollower = new();
    /// <summary>Marque (ou non) un bot comme FOLLOWER d'équipe : en combat il ne fait QUE placer + prêt (le chef décide des challenges).</summary>
    public void SetTeamFollower(uint pid, bool isFollower)
    {
        if (isFollower) _teamFollower[pid] = true;
        else _teamFollower.TryRemove(pid, out _);
    }
    private bool IsTeamFollower(uint pid) => _teamFollower.TryGetValue(pid, out var f) && f;

    // ATTENTE D'ÉQUIPE AVANT COMBAT : snapshot poussé au Tick par BotsViewModel (chef pid -> pids des followers attendus).
    // FightOne (thread script) le consulte AVANT d'engager pour ne plus partir combattre SEUL quand une mule n'est pas là.
    private volatile IReadOnlyDictionary<uint, uint[]> _teamCombatWait = new Dictionary<uint, uint[]>();   // attente AVANT combat
    private volatile IReadOnlyDictionary<uint, uint[]> _teamMapWait = new Dictionary<uint, uint[]>();      // attente AVANT de QUITTER la carte
    /// <summary>Remplace le snapshot des attentes AVANT COMBAT (chef pid -> followers attendus). Clé absente = pas d'attente.</summary>
    public void SetTeamCombatWait(IReadOnlyDictionary<uint, uint[]> snapshot)
    {
        _teamCombatWait = CopyTeamWait(snapshot);
    }
    /// <summary>Remplace le snapshot des attentes AVANT CHANGEMENT DE CARTE (chef pid -> followers attendus).</summary>
    public void SetTeamMapChangeWait(IReadOnlyDictionary<uint, uint[]> snapshot)
    {
        _teamMapWait = CopyTeamWait(snapshot);
    }

    private static IReadOnlyDictionary<uint, uint[]> CopyTeamWait(IReadOnlyDictionary<uint, uint[]> snapshot)
    {
        var copy = new Dictionary<uint, uint[]>(snapshot.Count);
        foreach (var kv in snapshot) copy[kv.Key] = (uint[])kv.Value.Clone();
        return copy; // l'ancien snapshot reste lisible jusqu'à cette publication atomique
    }

    // ================= COMPOSITION D'ÉQUIPE POUR LE SCRIPT (Lua team:members/isLeader/leaderName) =================
    // BotsViewModel (thread UI) POUSSE un snapshot par-pid de l'équipe (identités STABLES : pid + nom + qui est chef). Le
    // thread de script ne lit QUE ce snapshot (référence atomique remplacée d'un bloc) — il ne touche JAMAIS aux VM. Les
    // données VIVANTES (carte, en-combat) sont relues par pid côté binding Lua, pas figées ici. Calque SetTeamCombatWait.
    public readonly record struct TeamMemberInfo(uint Pid, string Name, bool IsLeader);
    public sealed record TeamView(uint LeaderPid, string LeaderName, IReadOnlyList<TeamMemberInfo> Members);
    private volatile IReadOnlyDictionary<uint, TeamView> _teamView = new Dictionary<uint, TeamView>();
    /// <summary>Dernier ennemi frappé par le MOTEUR de ce pid (acteur, instant) : consigne de cible pour ses suiveurs (TeamFocus).</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, (long Actor, long At)> _lastEngineHit = new();
    /// <summary>Remplace le snapshot de composition (chaque membre EN JEU pid -> la vue de SON équipe). Clé absente = hors équipe.</summary>
    public void SetTeamSnapshot(IReadOnlyDictionary<uint, TeamView> snapshot) => _teamView = snapshot;
    /// <summary>Vue de l'équipe à laquelle appartient `pid` (chef + membres EN JEU), ou null si le perso n'est dans aucune équipe.</summary>
    public TeamView? TeamViewOf(uint pid) => _teamView.TryGetValue(pid, out var v) ? v : null;
    /// <summary>Membres EN JEU de l'équipe de `pid` (vide si hors équipe). Identités stables ; carte/combat à relire live par pid.</summary>
    public IReadOnlyList<TeamMemberInfo> TeamMembers(uint pid) => _teamView.TryGetValue(pid, out var v) ? v.Members : System.Array.Empty<TeamMemberInfo>();
    /// <summary>true si `pid` est le CHEF de son équipe (false s'il est follower ou hors équipe).</summary>
    public bool IsTeamLeader(uint pid) => _teamView.TryGetValue(pid, out var v) && v.LeaderPid == pid;
    /// <summary>Nom du chef de l'équipe de `pid` (chaîne vide si hors équipe).</summary>
    public string TeamLeaderName(uint pid) => _teamView.TryGetValue(pid, out var v) ? v.LeaderName : "";

    // BROADCAST d'un snippet Lua à toute l'équipe. Le thread de script ne peut PAS démarrer les hôtes des autres pids (gérés
    // par BotsViewModel, thread UI) : il ENFILE la demande ici, BotsViewModel la draine au Tick et lance chaque hôte membre.
    private readonly ConcurrentQueue<(uint FromPid, string Script)> _teamBroadcasts = new();
    /// <summary>Enfile un broadcast d'équipe (appelé du thread script via team:broadcast). Best-effort, non bloquant.</summary>
    public void EnqueueTeamBroadcast(uint fromPid, string script) { if (!LicenseService.HasPermission(LicenseService.Perm.Multi)) return; if (!string.IsNullOrWhiteSpace(script)) _teamBroadcasts.Enqueue((fromPid, script)); }   // broadcast = feature équipe -> Perm.Multi (cohérence famille)
    /// <summary>BotsViewModel draine ceci au Tick (thread UI) pour démarrer le snippet sur les hôtes des membres.</summary>
    public bool TryDequeueTeamBroadcast(out (uint FromPid, string Script) req) => _teamBroadcasts.TryDequeue(out req);

    // Ordres de ralliement (team:joinLeader / joinBoss Lua) : pid du CHEF, drainés par le VM (Tick) -> HandleJoinBoss.
    private readonly ConcurrentQueue<uint> _teamJoinLeader = new();
    /// <summary>Met en file un ralliement de l'équipe sur le chef <paramref name="leaderPid"/>. false si pas de Perm.Multi
    /// ou si ce pid n'est pas chef d'équipe.</summary>
    public bool EnqueueTeamJoinLeader(uint leaderPid)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Multi) || !IsTeamLeader(leaderPid)) return false;
        _teamJoinLeader.Enqueue(leaderPid);
        return true;
    }
    public bool TryDequeueTeamJoinLeader(out uint pid) => _teamJoinLeader.TryDequeue(out pid);
    /// <summary>true si le chef `pid` doit ENCORE attendre avant d'engager : au moins un follower attendu est EN JEU, hors
    /// combat, PAS encore sur sa carte. FightOne boucle dessus (borné par timeout). Offline/déjà-là = pas d'attente ; un autre combat impose une attente.</summary>
    public bool TeamCombatWaitPending(uint pid) => AnyTeamFollowerAway(pid, _teamCombatWait);
    /// <summary>true si le chef `pid` doit attendre AVANT de quitter sa carte : au moins un follower attendu n'est pas encore
    /// avec lui. Le chef bloque son propre changement de carte (borné par timeout) -> voyage groupé, plus de « partent pas ensemble ».</summary>
    public bool TeamMapChangeWaitPending(uint pid) => AnyTeamFollowerAway(pid, _teamMapWait);
    /// <summary>true si le chef `pid` a ENCORE des followers (EN JEU) qui n'ont PAS rejoint son combat. Le chef s'en sert
    /// pendant le PLACEMENT pour ne pas se mettre PRÊT trop tôt (sinon il validait le combat en SOLO avant que la mule ait
    /// pu rejoindre — surtout en suivi collé où elle marchait encore). Réutilise le snapshot combat (chef -> followers).</summary>
    public bool TeamFollowersJoinPending(uint pid)
    {
        if (!_teamCombatWait.TryGetValue(pid, out var followers) || followers.Length == 0) return false;
        foreach (var fp in followers)
            if ((CurrentMap(fp) ?? 0) != 0 && (!IsInFight(fp) || CurrentFightId(fp) == 0 || CurrentFightId(fp) != CurrentFightId(pid))) return true;   // follower en jeu, pas encore DANS le combat -> on l'attend
        return false;
    }

    internal const int TeamJoinNoProgressWaitMs = 8000;   // fenêtre max sans progrès d'un follower avant d'abandonner l'attente
    internal const int TeamJoinMaxWaitMs = 20000;         // plafond dur absolu de l'attente des followers
    private const int TeamJoinWaitMs = 8000;              // attente MAX (placement) que les followers rejoignent avant que le chef ne valide

    /// <summary>
    /// Décide si le chef d'équipe doit continuer d'attendre ses suiveurs pendant le placement :
    /// continue tant qu'au moins un suiveur progresse vers le combat, s'arrête dès que tous les suiveurs sont placés,
    /// abandonne si aucun progrès n'est constaté pendant 8000 ms, et ne dépasse jamais le plafond absolu de 20000 ms.
    /// </summary>
    internal static bool KeepWaitingForFollowers(long elapsedMs, long msSinceLastProgress, bool allPlaced)
    {
        if (allPlaced) return false;
        if (elapsedMs >= TeamJoinMaxWaitMs) return false;
        if (msSinceLastProgress >= TeamJoinNoProgressWaitMs) return false;
        return true;
    }

    /// <summary>Cellule RÉELLEMENT observée de `pid` (0-559), ou -1 si inconnue. BotWorldStore est alimenté par les
    /// événements serveur (jss/jsj/jqt) : un pas DANS la carte y apparaît même quand carte/combat/fight ne changent pas.</summary>
    private static int TeamJoinObservedCell(uint pid) => Stores.World.Get(pid).SelfCell ?? -1;

    /// <summary>
    /// Attente ADAPTATIVE du ralliement des followers (POLITIQUE DE PROGRÈS, utilisée en production) : on attend tant
    /// qu'au moins un follower PROGRESSE (changement de carte / de cellule / d'état de combat / de fight), on s'arrête dès que tous
    /// les followers attendus ont rejoint le combat du chef, on abandonne après 8000 ms SANS progrès, et on ne
    /// dépasse JAMAIS le plafond dur de 20000 ms (la décision passe par <see cref="KeepWaitingForFollowers"/>).
    /// L'IDENTITÉ DU PLACEMENT (même combat, placement encore ouvert, pas de reprise manuelle) est revérifiée à
    /// chaque tour : si elle est perdue, l'attente rend la main SANS valider de défi (le chef ne part pas seul) —
    /// l'ANNULATION (ct) est respectée et remonte en OperationCanceledException.
    /// </summary>
    internal async Task<bool> WaitForTeamJoinProgressAsync(uint pid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (IsTeamFollower(pid)) return true;

        long placementStart = _fightStartAt.GetValueOrDefault(pid);
        long startedAt = Environment.TickCount64;
        long lastProgressAt = startedAt;
        var previousStates = new Dictionary<uint, (long Map, int Cell, bool InFight, long FightId)>();

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // Identité du placement : combat remplacé, phase de placement close ou reprise manuelle -> plus rien à attendre.
            if (!IsCurrentTeamPlacement(pid, placementStart)) return false;

            long now = Environment.TickCount64;
            long elapsed = now - startedAt;

            // Le snapshot est RELELU à chaque tour : un membre qui quitte l'équipe attendue ne doit pas figer l'attente.
            bool allJoined = !TeamFollowersJoinPending(pid);
            bool anyProgress = false;
            if (_teamCombatWait.TryGetValue(pid, out var followers))
            {
                foreach (var fp in followers)
                {
                    // La CELLULE réellement observée (BotWorldStore, alimenté par jss/jsj/jqt) fait partie du progrès :
                    // un follower qui MARCHE dans la même carte ne change ni sa carte ni son état de combat. Sans elle,
                    // son déplacement était compté comme immobile et l'attente coupait à 8 s malgré la marche en cours.
                    var cur = (CurrentMap(fp) ?? 0, TeamJoinObservedCell(fp), IsInFight(fp), CurrentFightId(fp));
                    if (!previousStates.TryGetValue(fp, out var prev) || !prev.Equals(cur))
                    {
                        anyProgress = true;   // le follower avance (il marche — même dans la carte —, entre en combat, rejoint le fight)
                        previousStates[fp] = cur;
                    }
                }
            }
            if (anyProgress) lastProgressAt = now;

            long msSinceLastProgress = now - lastProgressAt;
            if (!KeepWaitingForFollowers(elapsed, msSinceLastProgress, allJoined))
            {
                if (!allJoined)
                    TransportLog.Line($"[équipe] pid {pid} : attente du ralliement abandonnée après {elapsed} ms (aucun progrès depuis {msSinceLastProgress} ms)");
                return allJoined && IsCurrentTeamPlacement(pid, placementStart);
            }

            await Task.Delay(200, ct).ConfigureAwait(false);
        }
    }

    private bool AnyTeamFollowerAway(uint pid, IReadOnlyDictionary<uint, uint[]> snap)
    {
        if (!snap.TryGetValue(pid, out var followers) || followers.Length == 0) return false;
        long myMap = CurrentMap(pid) ?? 0; if (myMap == 0) return false;
        foreach (var fp in followers)
        {
            long fm = CurrentMap(fp) ?? 0;
            if (fm != 0 && (IsInFight(fp) || fm != myMap)) return true;   // en jeu, ailleurs ou occupé dans un autre combat -> on l'attend
        }
        return false;
    }
}
