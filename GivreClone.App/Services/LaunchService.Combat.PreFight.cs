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

namespace GivreClone.App.Services;

// ===================== Combat — pré-combat : attaque, expulsion, placement, défis (challenges), prêt, bilan de combat =====================
public sealed partial class LaunchService
{
    /// <summary>Attaque un groupe de monstres: si le perso n'est PAS deja adjacent au groupe, il MARCHE
    /// jusqu'a une case adjacente puis, a l'arrivee (jsq), envoie AttackMonsterRequest (hqa) avec l'id (int64 signe,
    /// negatif) du groupe. Deja adjacent -> attaque directe. Retourne false si pas de relais / pas de case atteignable.</summary>
    public bool AttackMonster(uint pid, long mapId, int source, long monsterGroupId, int monsterCell, Func<int, bool> isWalkable, string? name = null,
                              Func<int, int, bool>? stepAllowed = null, Func<bool>? targetStillCurrent = null,
                              Func<int, bool>? confirmedArrival = null,
                              Func<int, Func<bool>, bool>? submitWithLease = null)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Fight)) return false;   // engager un combat = Perm.Fight (clic-droit "Attaquer" ET fight() Lua ET attaque coordonnée)
        var relay = RelayForInject();
        if (relay is null || mapId == 0 || monsterGroupId >= 0
            || targetStillCurrent is null || confirmedArrival is null || submitWithLease is null
            || IsInFight(pid) || (CurrentMap(pid) ?? 0) != mapId
            || !AttackTargetLeaseValid(targetStillCurrent)) return false;

        // Deja colle au groupe (distance d'interaction <= 1) -> attaque directe, sans bouger.
        if (source == monsterCell || MapPathfinder.CellDistance(source, monsterCell) <= 1)
            return SubmitAttackWithLease(submitWithLease, source,
                () => SendAttack(pid, monsterGroupId, name));

        // Sinon : case marchable adjacente au groupe, atteignable, la plus proche du perso -> on y va puis on attaque.
        int best = -1;
        foreach (var c in MapPathfinder.StandCellsInRange(monsterCell, 1, source, isWalkable))
            if (MapPathfinder.KeyMovements(source, c, isWalkable, allowDiag: true, stepAllowed).Count > 0) { best = c; break; }
        if (best < 0)
        {
            TransportLog.Line($"[combat] pid {pid} : aucune case adjacente atteignable pour attaquer {name} (cellule {monsterCell})");
            return false;
        }

        // Armer AVANT MoveTo.Inject(): un relais peut publier écho+ACK de
        // façon synchrone. Le MoveSeq empêche de consommer le jsq d'un autre
        // trajet, et le garde d'écho serveur refuse un ACK non attribuable.
        long armedSeq = 0;
        bool moved = MoveTo(pid, mapId, source, best, isWalkable, allowDiag: true,
            stepAllowed: stepAllowed, beforeInject: seq =>
            {
                lock (ArrivalActionGate)
                {
                    if (ScriptActionOwner.Value.IsCancellationRequested || !AttackTargetLeaseValid(targetStillCurrent))
                        return false;
                    armedSeq = seq;
                    _pendingAttack[pid] = (monsterGroupId, name, ScriptActionOwner.Value,
                        mapId, targetStillCurrent, Environment.TickCount64, seq, best, confirmedArrival,
                        submitWithLease);
                    return true;
                }
            });
        if (!moved)
        {
            lock (ArrivalActionGate)
            {
                if (armedSeq != 0 && _pendingAttack.TryGetValue(pid, out var pending) && pending.MoveSeq == armedSeq)
                    _pendingAttack.TryRemove(pid, out _);
            }
            return false;
        }
        TransportLog.Line($"[combat] pid {pid} : marche {source}->{best} puis attaque {name} (id {monsterGroupId})");
        return moved;
    }

        // A missing, failing or throwing lease must never authorize an attack.
    private static bool AttackTargetLeaseValid(Func<bool>? predicate)
    {
        if (predicate is null) return false;
        try { return predicate(); }
        catch { return false; }
    }

    private static bool AttackArrivalLeaseValid(Func<int, bool>? predicate, int cell)
    {
        if (predicate is null) return false;
        try { return predicate(cell); }
        catch { return false; }
    }

    private static bool SubmitAttackWithLease(Func<int, Func<bool>, bool>? submitWithLease,
        int arrivalCell, Func<bool> submit)
    {
        if (submitWithLease is null) return false;
        try { return submitWithLease(arrivalCell, submit); }
        catch { return false; }
    }

    private bool SendAttack(uint pid, long monsterGroupId, string? name)
    {
        var relay = RelayForInject();
        if (relay is null || monsterGroupId >= 0) return false;
        byte[] pkt = MoveForge.BuildAttackMonsterFramed(monsterGroupId);   // AttackMonsterRequest {monster_group_id #1}
        if (pkt.Length == 0)
        {
            TransportLog.Line($"[combat] pid {pid} : attaque groupe {monsterGroupId} refusée, mapping/forge absent");
            return false;
        }
        bool ok;
        try { ok = relay.Inject(pid, pkt); }
        catch (Exception e)
        {
            TransportLog.Line($"[combat] pid {pid} : attaque groupe {monsterGroupId} non envoyée ({e.GetType().Name})");
            return false;
        }
        if (ok) ShowInjected(pid, pkt);
        TransportLog.Line($"[combat] pid {pid} : attaque du groupe {name} (id {monsterGroupId}, AttackMonsterRequest) inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    // AUTO-KICK d'équipe : intrus (joueurs non-membres) déjà expulsés du combat courant, par pid (anti-spam ; vidé en fin de combat).
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<long, byte>> _kickedThisFight = new();

    /// <summary>Expulse un combattant du CONTEXTE de combat (ContextKickRequest/kkj). Le chef vire un intrus non-membre.
    /// Forge résolue par nom (aucun code/numéro en dur) ; refus silencieux si le mapping ne résout pas le message.</summary>
    private bool SendKickFromFight(uint pid, long targetId)
    {
        var relay = RelayForInject();
        if (relay is null) return false;
        byte[] pkt = MoveForge.BuildContextKickFramed(targetId);
        if (pkt.Length == 0) { TransportLog.Line($"[équipe] kick forge refusée : {MoveForge.LastForgeError}"); return false; }
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[équipe] pid {pid} : ContextKickRequest cible {targetId} inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    // ===== AUTONOMIE PRÉ-COMBAT : placement (selon le style) + challenge (XP/Butin) + prêt. Le prêt AUTO du CLIENT Dofus
    // doit être DÉSACTIVÉ (le relais MITM ne bloque pas les paquets du client -> sinon double-envoi). =====
    private bool SendPlacement(uint pid, long entityId, int cell)
    {
        var relay = RelayForInject(); if (relay is null) return false;
        byte[] pkt = MoveForge.BuildPlacementFramed(entityId, cell);   // FightPlacementPositionRequest {cell_id #2, actor_id #5}
        bool ok = relay.Inject(pid, pkt); ShowInjected(pid, pkt);
        TransportLog.Line($"[combat] pid {pid} : placement -> case {cell} inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    private bool SendReady(uint pid)
    {
        if (TeamFollowersJoinPending(pid)) return false;
        var relay = RelayForInject(); if (relay is null) return false;
        byte[] pkt = MoveForge.BuildReadyFramed();   // FightReadyRequest {is_ready #1 = 1}
        bool ok = relay.Inject(pid, pkt); ShowInjected(pid, pkt);
        TransportLog.Line($"[combat] pid {pid} : PRÊT (FightReadyRequest) inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }


    // Challenge en 2 temps (cf capture manuelle) : SÉLECTION (kwv) PUIS VALIDATION (kwj). Sans kwj, le challenge est
    // choisi mais jamais confirmé.
    private bool SendChallengeSelect(uint pid, int challengeId)
    {
        if (TeamFollowersJoinPending(pid)) return false;
        var relay = RelayForInject(); if (relay is null) return false;
        byte[] pkt = MoveForge.BuildChallengeSelectFramed(challengeId);
        if (pkt.Length == 0) { TransportLog.Line($"[combat] pid {pid} : ChallengeModSelectRequest pas encore mappé sur ce patch -> défi non sélectionné (aucun paquet envoyé)."); return false; }
        bool ok = relay.Inject(pid, pkt); ShowInjected(pid, pkt);
        TransportLog.Line($"[combat] pid {pid} : challenge {challengeId} sélectionné inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    private bool SendChallengeValidate(uint pid, int challengeId)
    {
        if (TeamFollowersJoinPending(pid)) return false;
        var relay = RelayForInject(); if (relay is null) return false;
        byte[] pkt = MoveForge.BuildChallengeValidateFramed(challengeId);
        if (pkt.Length == 0) { TransportLog.Line($"[combat] pid {pid} : ChallengeValidateRequest pas encore mappé sur ce patch -> défi non validé (aucun paquet envoyé)."); return false; }
        bool ok = relay.Inject(pid, pkt); ShowInjected(pid, pkt);
        TransportLog.Line($"[combat] pid {pid} : challenge {challengeId} VALIDÉ inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    // Demande au serveur d'ENVOYER les propositions de challenge (kwx) — sinon, en headless, il n'en propose jamais.
    private bool SendChallengeRequest(uint pid)
    {
        if (TeamFollowersJoinPending(pid)) return false;
        var relay = RelayForInject(); if (relay is null) return false;
        byte[] pkt = MoveForge.BuildChallengeRequestFramed();
        if (pkt.Length == 0) { TransportLog.Line($"[combat] pid {pid} : ChallengeListRequest pas encore mappé sur ce patch -> pas de demande de propositions."); return false; }
        bool ok = relay.Inject(pid, pkt); ShowInjected(pid, pkt);
        TransportLog.Line($"[combat] pid {pid} : demande propositions challenge inject={(ok ? "OK" : "ECHEC")}");
        if (ok) _challengeRequested[pid] = 1;   // Lot C2 : combat:challengeProposals ne redemande pas dans ce combat
        return ok;
    }

    private bool SendChallengeSelection(uint pid, int challengeId)
    {
        var relay = RelayForInject(); if (relay is null) return false;
        byte[] pkt = MoveForge.BuildChallengeSelectionFramed(challengeId);
        if (pkt.Length == 0) { TransportLog.Line($"[combat] pid {pid} : ChallengeSelectionRequest pas encore mappé sur ce patch -> défi non pointé."); return false; }
        bool ok = relay.Inject(pid, pkt); ShowInjected(pid, pkt);
        TransportLog.Line($"[combat] pid {pid} : challenge {challengeId} pointé inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    // Ma cellule actuelle, depuis le modèle de combat. -1 si inconnue. Identité résolue par SelfActorId (id AUTHENTIFIÉ
    // _charId d'abord, repli max-positif) : le max-positif SEUL renvoie la case de l'ALLIÉ en multi-compte/groupe.
    private int SelfFighterCell(uint pid)
    {
        long me = SelfActorId(pid);
        return me != 0 && _fighters.TryGetValue(pid, out var f)
            ? f.Where(x => x.ActorId == me).Select(x => x.Cell).DefaultIfEmpty(-1).First() : -1;
    }

    // Les deux pre-combats partagent le selecteur pur ; aucune case occupee par un autre combattant n'est offerte.
    private System.Collections.Concurrent.ConcurrentDictionary<uint, string>? _placementWhy;
    private System.Collections.Concurrent.ConcurrentDictionary<uint, string> PlacementWhyMap => System.Threading.LazyInitializer.EnsureInitialized(ref _placementWhy);
    private string PlacementWhy(uint pid) => PlacementWhyMap.TryGetValue(pid, out var why) ? why : "";

    private int ChoosePlacementCell(uint pid, IReadOnlyList<int> cells, CombatStyle style)
    {
        if (cells is null || cells.Count == 0) return -1;
        long self = SelfActorId(pid);
        PlacementMode mode = _combat is not null && _combat.TryGetValue(pid, out var live)
            ? live.PlacementMode : PlacementMode.LegacyByStyle;
        var roster = _fighters.TryGetValue(pid, out var f) ? f.ToArray() : Array.Empty<(int Cell, long ActorId)>();
        var enemies = roster.Where(x => IsEnemy(pid, x.ActorId)).Select(x => x.Cell).ToArray();
        var occupied = roster.Where(x => x.ActorId != self).Select(x => x.Cell).ToArray();
        var threats = roster.Where(x => IsEnemy(pid, x.ActorId)).Select(x =>
        {
            var bestiary = MonsterThreat(pid, x.ActorId);
            FighterStats? stats = _fighterStats.TryGetValue(pid, out var byId) && byId.TryGetValue(x.ActorId, out var st) ? st : null;
            return new CombatPlacementSelector.PlacementThreat(x.Cell,
                bestiary?.Mp ?? stats?.MaxPm ?? 3,
                bestiary is { Reach: > 0 } known ? known.Reach : 6,
                bestiary?.TurnDamage is int damage and > 0 ? damage : 30);
        }).ToArray();
        int life = SelfLife(pid)?.Cur ?? 0;
        int ownMp = SelfStats(pid) is { MaxPm: > 0 } own ? own.MaxPm : 3;
        int reach = ownMp + 6 + Math.Max(0, SelfRange(pid));
        int preferred = CombatPlacementSelector.Select(mode, style, cells, enemies, occupied, SelfFighterCell(pid));
        int chosen = CombatPlacementSelector.SelectSafe(mode, style, cells, enemies, occupied, SelfFighterCell(pid),
            threats, life, reach);
        string why = CombatPlacementSelector.Explain(chosen, cells, enemies, occupied);
        if (chosen != preferred && chosen >= 0)
            why += $" ; groupe dangereux : exposition {CombatPlacementSelector.Exposure(preferred, threats)} > 60 % de {life} PV sur la case la plus proche,"
                + $" case retenue exposée à {CombatPlacementSelector.Exposure(chosen, threats)}";
        PlacementWhyMap[pid] = why;
        return chosen;
    }

    private static string PlacementDistanceWord(PlacementMode mode, CombatStyle style) => mode switch
    {
        PlacementMode.Keep => "rester",
        PlacementMode.Near => "la plus proche",
        PlacementMode.Far => "la plus loin",
        _ => style == CombatStyle.Aggressive ? "la plus proche" : "la plus loin",
    };

    // Orchestration du pré-combat : place (selon style) -> fixe le bonus (kwo : XP/Butin, LE seul switch) -> demande les
    // propositions (kwr) -> le handler kwx valide UN challenge quelconque (le 1er, kwi/kwv/kwj — le challenge choisi n'a
    // AUCUNE incidence sur XP/Butin) -> prêt. Les DÉLAIS suivent la VITESSE du profil (Instant/Fast/Slow) -> ne bloque
    // plus 6 s. Anti-crash : prêt quoi qu'il arrive (timeout 4,5 s).
    private async Task RunPreFight(uint pid, IReadOnlyList<int> myCells)
    {
        long fightStart = _fightStartAt.GetValueOrDefault(pid);
        try
        {
            if (ManualCombatOn(pid) || !_combat.TryGetValue(pid, out var live) || !live.AutoPreFight) return;
            if (!LicenseService.HasPermission(LicenseService.Perm.Fight)) return;   // pré-combat/challenge = sous-fonctions de "fight" (gate au POINT D'EXÉCUTION)
            var profile = live.Clone();
            int Step() => profile.Speed switch { CombatSpeed.Turbo => 10, CombatSpeed.Instant => 150, CombatSpeed.Fast => Random.Shared.Next(350, 700), _ => Random.Shared.Next(800, 1600) };   // Turbo : quasi aucun délai fixe pré-combat

            // P1 (fix 2026-09-02) : NE PAS décider le placement avant de connaître les positions ENNEMIES. Au kba, `_fighters`
            // est souvent VIDE (les jxg des combattants arrivent PENDANT la phase de placement) -> ChoosePlacementCell
            // retombait sur cells[0] (1re case offerte) au lieu de la case la plus PROCHE d'un ennemi (Agressif) / la plus
            // LOIN (Fuyard/Passif). On attend donc qu'au moins un ENNEMI soit connu (poll borné ~1,6 s, la phase de placement
            // dure bien plus), puis on choisit. Le placement du client Dofus reste possible tant qu'on n'a pas envoyé prêt.
            bool HasEnemies() => _fighters.TryGetValue(pid, out var ff) && ff.Any(x => IsEnemy(pid, x.ActorId));
            for (int w = 0; !HasEnemies() && w < 1600; w += 25)
                await Task.Delay(25).ConfigureAwait(false);
            if (!HasEnemies()) await Task.Delay(Step()).ConfigureAwait(false);   // aucune position connue -> au moins humaniser
            if (!IsCurrentTeamPlacement(pid, fightStart)) return;
            int cell = ChoosePlacementCell(pid, myCells, profile.Style);
            long myId = SelfActorId(pid);
            if (cell >= 0 && myId != 0 && cell != SelfFighterCell(pid)) SendPlacement(pid, myId, cell);
            TransportLog.Line(HasEnemies()
                ? $"[combat] placement mode={profile.PlacementMode} style={profile.Style} -> case {cell} ({PlacementDistanceWord(profile.PlacementMode, profile.Style)} d'un ennemi ; {PlacementWhy(pid)})"
                : $"[combat] placement : positions ennemies inconnues après attente -> case par défaut {cell}");

            // FOLLOWER d'équipe (mule) : il ne DÉCIDE PAS des challenges (c'est le CHEF qui choisit). Il ne demande donc AUCUNE
            // proposition et n'attend PAS la gate de 4,5 s -> il se place puis fait PRÊT immédiatement (le ~7 s de retard venait
            // de ce timeout de gate qui n'était jamais débloqué côté mule). Cf. règle utilisateur « juste à faire prêt ».
            bool follower = IsTeamFollower(pid);
            // ÉQUIPE (chef) : ATTENDRE que les followers aient REJOINT le combat AVANT de toucher au challenge ET au prêt.
            // Retour user (confirmé) : SÉLECTIONNER/VALIDER un challenge FERME le combat aux nouveaux arrivants -> la mule ne
            // pouvait plus rejoindre. On laisse donc la fenêtre de placement GRANDE OUVERTE (ni challenge ni prêt) le temps
            // qu'elle entre — DriveTeamCombat re-tente le join pendant ce temps — PUIS on fait le challenge et le prêt.
            // L’expiration ne doit jamais autoriser un départ sans les membres attendus. Un follower n'a pas de snapshot combat -> n'attend jamais.
            if (!IsCurrentTeamPlacement(pid, fightStart)) return;
            if (!follower && !await WaitTeamJoinBeforeChallengeAsync(pid, CancellationToken.None, untilJoined: true).ConfigureAwait(false)) return;
            // Lot C2 : mode « script » (ChallengeByScript) = on demande AUSSI les propositions mais le CHOIX revient au script
            // (combat:selectChallenge libère la gate) ; attente plus longue mais BORNÉE -> le bot finit toujours prêt.
            // Sans onPlacement, AUCUN code du script ne tourne pendant le placement (move() suspendu, onTurn après) -> personne
            // ne pourrait choisir : le mode « script » se comporte alors comme « off » (pas de kty, pas d'attente de 8 s).
            bool byScript = profile.ChallengeByScript && _scriptPlacementHook.ContainsKey(pid);
            if ((profile.AutoChallenge || byScript) && !follower)
            {
                // On DEMANDE les propositions (ChallengeReadyRequest) et on en SELECTIONNE+VALIDE une (ChallengeSelectionRequest puis ChallengeValidateRequest) pour ACTIVER un challenge.
                // Le TYPE de bonus (XP/Butin) N'est PAS piloté : on ne touche pas au ChallengeBonusChoiceRequest -> le dernier choix mémorisé par
                // le client s'applique (le forçage réseau ne prime pas côté serveur, cf combats de test 2026-08-18).
                await Task.Delay(Step()).ConfigureAwait(false);
                // Lot C2 : pas de 2e kty si un script (/cmd) l'a déjà envoyé CE combat ; un défi déjà choisi ET envoyé a
                // laissé une gate complétée (finally de RunChallengeSelect) -> pas d'attente.
                // Heure relevée AVANT l'envoi : une réponse dans la même milliseconde compte comme reçue.
                long askedAt = Environment.TickCount64;
                bool askedNow = !_challengeRequested.ContainsKey(pid);
                if (askedNow)
                    SendChallengeRequest(pid);   // ChallengeReadyRequest (kty, vide) : DECLENCHE les propositions (sinon rien en headless)
                var gate = _challengeGate.GetOrAdd(pid, static _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
                int fullWaitMs = byScript ? ScriptChallengeWaitMs : 4500;
                if (!askedNow)
                    await Task.WhenAny(gate.Task, Task.Delay(fullWaitMs)).ConfigureAwait(false);   // demande déjà faite par un script : attente d'origine
                else
                {
                    // Combat SANS défi (Poutch, certains donjons) : aucune proposition ne vient et on attendait 4,5 s à chaque
                    // combat. Mesuré sur 223 combats (23-24/10) : propositions reçues en 26 ms médiane, 0,79 s au pire -> sans
                    // proposition après ProposalWaitMs, on passe prêt. Une proposition reçue garde l'attente complète.
                    await Task.WhenAny(gate.Task, WaitChallengeProposals(pid, askedAt, ProposalWaitMs)).ConfigureAwait(false);
                    if (!gate.Task.IsCompleted && _challengePropsAt.TryGetValue(pid, out var propsAt) && propsAt >= askedAt)
                        await Task.WhenAny(gate.Task, Task.Delay(Math.Max(0, fullWaitMs - (int)(Environment.TickCount64 - askedAt)))).ConfigureAwait(false);
                    else if (!gate.Task.IsCompleted)
                        TransportLog.Line($"[combat] pid {pid} : aucune proposition de défi en {ProposalWaitMs} ms -> prêt sans défi");
                }
            }
            // Follower : prêt VITE (juste un micro-délai d'humanisation) ; chef/solo : cadence du profil.
            await Task.Delay(follower ? Math.Min(Step(), 250) : Step()).ConfigureAwait(false);
            if (IsCurrentTeamPlacement(pid, fightStart)) SendReady(pid);
        }
        catch { try { if (IsCurrentTeamPlacement(pid, fightStart)) SendReady(pid); } catch { } }
    }

    private const int ProposalWaitMs = 1200;

    // Attend l'arrivée des propositions de défi demandées à askedAt (ou l'expiration). Sondage court : pas d'évènement dédié.
    private async Task WaitChallengeProposals(uint pid, long askedAt, int timeoutMs)
    {
        long until = askedAt + timeoutMs;
        while (Environment.TickCount64 < until)
        {
            if (_challengePropsAt.TryGetValue(pid, out var at) && at >= askedAt) return;
            await Task.Delay(20).ConfigureAwait(false);
        }
    }

    // Choix d'un challenge comme le client (capture 3.6.11.12) : ChallengeSelectionRequest (kuh) {id} -> ChallengeValidateRequest (kuf) {id}.
    // ⚠️ Le mapping autoritatif tranche : en 3.6.11.12 la SELECTION-avec-id est kuh (ChallengeSelectionRequest), PAS kui
    // (ChallengeModSelectRequest, qui part vide a l'entree du combat). kuf est le paquet qui APPLIQUE le challenge.
    // Debloque ensuite la gate -> RunPreFight met pret.
    // Lot C2 : renvoie true si les DEUX injections (kuh puis kuf) ont réussi ; mémorise le défi choisi pour CE combat
    // (moteur OU script) -> combat:selectChallenge refuse un 2e choix. Les appels `_ = RunChallengeSelect(...)` sont inchangés.
    private async Task<bool> RunChallengeSelect(uint pid, int challengeId)
    {
        long fightStart = _fightStartAt.GetValueOrDefault(pid);
        if (!IsCurrentTeamPlacement(pid, fightStart) || TeamFollowersJoinPending(pid)) return false;
        _challengeChosen[pid] = challengeId;
        bool ok = false;
        try
        {
            bool sel;
            // MODE SOCKET : c'est le CLIENT socket qui envoie la sélection du 1er défi proposé, 1-47 ms après la proposition,
            // comme Dofus.exe (10/10 combats capturés). Le moteur ne la double pas ; un AUTRE choix (script) part au moins 60 ms
            // après la proposition pour être la DERNIÈRE sélection avant la validation.
            int first = _challengeProps.TryGetValue(pid, out var prs) && prs.Count > 0 ? prs[0].Id : 0;
            if (IsSocketPid(pid) && challengeId == first)
            {
                sel = true;
                TransportLog.Line($"[combat] pid {pid} : défi {challengeId} = 1er proposé -> sélection laissée au client socket, validation seule");
            }
            else
            {
                if (IsSocketPid(pid) && _challengePropsAt.TryGetValue(pid, out long at))
                {
                    long wait = at + 60 - Environment.TickCount64;
                    if (wait > 0) await Task.Delay((int)wait).ConfigureAwait(false);
                }
                if (!IsCurrentTeamPlacement(pid, fightStart)) return false;
                sel = SendChallengeSelect(pid, challengeId);        // ChallengeSelectionRequest (kuh) — comme le client
            }
            bool turboPre = _combat.TryGetValue(pid, out var tpf) && tpf.Speed == CombatSpeed.Turbo;
            await Task.Delay(turboPre ? 10 : 200).ConfigureAwait(false);
            if (!IsCurrentTeamPlacement(pid, fightStart)) return false;
            bool val = SendChallengeValidate(pid, challengeId);      // ChallengeValidateRequest (kuf)
            await Task.Delay(turboPre ? 10 : 150).ConfigureAwait(false);
            ok = sel && val;
            if (ok && _fightStartAt.GetValueOrDefault(pid) == fightStart)
            {
                int bonus = _challengeProps.TryGetValue(pid, out var pr2) && pr2.FirstOrDefault(x => x.Id == challengeId) is { } chp
                    ? Math.Max(chp.XpBonus, chp.DropBonus) : 0;
                ChallengeChosen?.Invoke(pid, challengeId, bonus);
            }
            return ok;
        }
        catch { return false; }
        finally
        {
            FinishChallengeSelection(pid, fightStart, challengeId, ok);
        }
    }

    private void FinishChallengeSelection(uint pid, long fightStart, int challengeId, bool ok)
    {
        if (_fightStartAt.GetValueOrDefault(pid) != fightStart) return;
        if (!ok) _challengeChosen.TryRemove(new KeyValuePair<uint, int>(pid, challengeId));
        _challengeGate.GetOrAdd(pid, static _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult(true);
    }

    // Lot C2 : chef d'équipe -> attendre (borné, comme RunPreFight / ScriptFinishPlacementAsync) que les followers aient
    // REJOINT le combat avant toute demande/validation de défi pilotée par script : valider un défi FERME le combat aux
    // nouveaux arrivants. Follower / hors placement / arrêt demandé : aucune attente.
    private bool IsCurrentTeamPlacement(uint pid, long startedAt) => !ManualCombatOn(pid) && IsPlacementOpen(pid)
        && _fightStartAt.GetValueOrDefault(pid) == startedAt;

    private async Task<bool> WaitTeamJoinBeforeChallengeAsync(uint pid, CancellationToken ct,
        bool untilJoined = false, int timeoutMs = TeamJoinWaitMs)
    {
        ct.ThrowIfCancellationRequested();
        if (IsTeamFollower(pid)) return true;
        long startedAt = _fightStartAt.GetValueOrDefault(pid);
        if (untilJoined)
        {
            // Attente ADAPTATIVE du ralliement : la POLITIQUE DE PROGRÈS (Team.cs) borne l'attente — fenêtre de
            // 8000 ms sans progrès, puis plafond DUR de 20000 ms — au lieu de l'attendre indéfiniment (legacy
            // untilJoined). L'identité du placement (même combat, placement ouvert, pas de reprise manuelle) et
            // l'annulation (ct) sont respectées : false = le chef ne valide ni défi ni prêt.
            return await WaitForTeamJoinProgressAsync(pid, ct).ConfigureAwait(false);
        }
        // Mode « borné » (inchangé) : attente plafonnée par timeoutMs, abandonnée si le placement n'est plus valide.
        long deadline = Environment.TickCount64 + Math.Max(0, timeoutMs);
        while (TeamFollowersJoinPending(pid))
        {
            if (!IsCurrentTeamPlacement(pid, startedAt)) return false;
            if (Environment.TickCount64 >= deadline) return false;
            await Task.Delay(200, ct).ConfigureAwait(false);
        }
        return IsCurrentTeamPlacement(pid, startedAt);
    }

    // Lot C2 : validation AUTO (1er défi) quand le placement appartient au script (onPlacement : RunPreFight ne tourne pas,
    // donc pas son attente d'équipe) -> on attend l'équipe d'abord, puis on revérifie avant de valider.
    private async Task AutoSelectChallengeAfterTeamAsync(uint pid, int challengeId)
    {
        try
        {
            if (!await WaitTeamJoinBeforeChallengeAsync(pid, CancellationToken.None, untilJoined: true).ConfigureAwait(false)) return;
            if (!IsInFight(pid) || _placementOver.ContainsKey(pid) || _challengeChosen.ContainsKey(pid)) return;
            await RunChallengeSelect(pid, challengeId).ConfigureAwait(false);
        }
        catch { }
    }

    private readonly ConcurrentDictionary<uint, byte> _prefightRan = new();                          // pid -> pré-combat déjà lancé pour CE combat (anti-double)
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<bool>> _challengeGate = new();  // pid -> débloque le prêt une fois le challenge VALIDÉ (kwx->kwv->kwj)

    // ── Lot C2 : défis pilotés par script + bilan de combat ────────────────────────────────────────────────────────────
    private readonly ConcurrentDictionary<uint, IReadOnlyList<FightPrepReader.ChallengeProposal>> _challengeProps = new();   // propositions reçues CE combat
    private readonly ConcurrentDictionary<uint, long> _challengePropsAt = new();   // instant de réception (mode socket : ordre des sélections)
    private readonly ConcurrentDictionary<uint, byte> _challengeRequested = new();   // kty déjà envoyé CE combat
    private readonly ConcurrentDictionary<uint, int> _challengeChosen = new();       // défi choisi CE combat (moteur OU script)
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<int, bool>> _challengeResults = new();   // id -> réussi ; vidé au PROCHAIN FightStarting
    private readonly ConcurrentDictionary<uint, byte> _placementOver = new();        // FightStartEvent reçu (placement terminé)
    private const int ScriptChallengeWaitMs = 8000;                                  // attente BORNÉE du choix du script avant « prêt »
    private readonly ConcurrentDictionary<uint, long> _fightStartAt = new();         // TickCount64 du FightStarting (durée locale)
    private readonly ConcurrentDictionary<uint, byte> _fightResultStored = new();    // bilan décodé déjà publié pour CE combat
    private readonly ConcurrentDictionary<uint, FightResultInfo> _lastFightResult = new();

    public enum ChallengePick { Ok, NoPerm, NotInFight, PlacementOver, Follower, Unknown, Already, SendFailed }

    /// <summary>Propositions de défi reçues pour le combat EN COURS (null = rien reçu). Lecture pure.</summary>
    public IReadOnlyList<FightPrepReader.ChallengeProposal>? ChallengeProposals(uint pid) => _challengeProps.TryGetValue(pid, out var l) ? l : null;

    /// <summary>Demande les propositions de défi (kty) si ce n'est pas déjà fait CE combat. false = impossible (droit, hors
    /// combat, follower, placement terminé) ou envoi échoué. Chef d'équipe : attend d'abord (borné) que les followers aient
    /// rejoint le combat, comme RunPreFight — le kty déclenche la validation auto en mode « auto ».</summary>
    public async Task<bool> RequestChallengeProposalsAsync(uint pid, CancellationToken ct = default)
    {
        bool Blocked() => !LicenseService.HasPermission(LicenseService.Perm.Fight) || !IsInFight(pid) || IsTeamFollower(pid) || _placementOver.ContainsKey(pid);
        if (Blocked()) return false;
        if (_challengeRequested.ContainsKey(pid)) return true;
        if (!await WaitTeamJoinBeforeChallengeAsync(pid, ct).ConfigureAwait(false)) return false;
        if (ct.IsCancellationRequested || Blocked()) return false;
        return _challengeRequested.ContainsKey(pid) || SendChallengeRequest(pid);
    }

    /// <summary>combat:selectChallenge : sélection + validation (kuh puis kuf, comme le client) d'un défi PROPOSÉ ; libère la
    /// gate de pré-combat (-> prêt). Un seul choix par combat. Chef d'équipe : attente bornée des followers AVANT de valider
    /// (valider un défi ferme le combat aux nouveaux arrivants).</summary>
    public async Task<ChallengePick> SelectChallengeAsync(uint pid, int id, CancellationToken ct = default)
    {
        ChallengePick? Check()
        {
            if (!LicenseService.HasPermission(LicenseService.Perm.Fight)) return ChallengePick.NoPerm;
            if (!IsInFight(pid)) return ChallengePick.NotInFight;
            if (_placementOver.ContainsKey(pid)) return ChallengePick.PlacementOver;
            if (IsTeamFollower(pid)) return ChallengePick.Follower;
            if (!_challengeProps.TryGetValue(pid, out var pr) || !pr.Any(x => x.Id == id)) return ChallengePick.Unknown;   // rien reçu / pas proposé
            if (_challengeChosen.ContainsKey(pid)) return ChallengePick.Already;
            return null;
        }
        if (Check() is { } early) return early;
        if (!await WaitTeamJoinBeforeChallengeAsync(pid, ct).ConfigureAwait(false)) return ChallengePick.SendFailed;
        if (ct.IsCancellationRequested) return ChallengePick.SendFailed;
        if (Check() is { } late) return late;   // placement fini / défi choisi entre-temps (moteur ou autre appel)
        return await RunChallengeSelect(pid, id).ConfigureAwait(false) ? ChallengePick.Ok : ChallengePick.SendFailed;
    }

    /// <summary>Résultat d'un défi du combat en cours / dernier combat: 1 réussi, 0 échoué, -1 inconnu.</summary>
    public int ChallengeResult(uint pid, int id)
        => _challengeResults.TryGetValue(pid, out var d) && d.TryGetValue(id, out var ok) ? (ok ? 1 : 0) : -1;

    /// <summary>Bilan du DERNIER combat de ce bot (null = aucun depuis le lancement de l'app). Lecture pure.</summary>
    public FightResultInfo? LastFightResult(uint pid) => _lastFightResult.TryGetValue(pid, out var r) ? r : null;

    private void PublishFightResult(uint pid, FightEndSummary s, bool decoded)
    {
        long now = Environment.TickCount64;
        long dur = _fightStartAt.TryGetValue(pid, out var t0) ? now - t0 : 0;
        IReadOnlyList<(int Id, bool Success)> ch = _challengeResults.TryGetValue(pid, out var d)
            ? d.Select(kv => (kv.Key, kv.Value)).ToList() : new List<(int, bool)>();
        var info = new FightResultInfo(0, decoded && s.Found, s.Outcome, dur, s.ServerDuration, s.Xp, s.Kamas, s.Loot, ch, CurrentFightId(pid), now)
        {
            Turns = _fightTurnNo.TryGetValue(pid, out int turns) && turns > 0 ? turns : null
        };
        _lastFightResult.AddOrUpdate(pid, _ => info with { Seq = 1 }, (_, old) => info with { Seq = old.Seq + 1 });
        TransportLog.Line($"[combat] pid {pid} : bilan -> issue={s.Outcome} xp={s.Xp?.ToString() ?? "?"} butin=[{string.Join(", ", s.Loot.Select(l => $"{l.Gid}x{l.Qty}"))}]");
    }

    // FightEndEvent décodé -> bilan publié (identité par _charId d'abord : après mort/abandon je ne suis plus sur le plateau).
    private void StoreFightResult(uint pid, IReadOnlyList<ProtoField> payload)
    {
        if (!IsInFight(pid)) return;   // doublon / spectateur : pas de nouveau Seq
        try
        {
            long me = _charId.TryGetValue(pid, out var cid) && cid > 0 ? cid : SelfActorId(pid);
            var s = me != 0 ? FightResultReader.Read(payload, me) : new FightEndSummary(false, -1, 0, null, null, Array.Empty<(int, int)>());
            PublishFightResult(pid, s, decoded: true);
            _fightResultStored[pid] = 1;
        }
        catch (Exception ex) { TransportLog.Line($"[combat] pid {pid} : bilan de combat illisible ({ex.GetType().Name})"); }
    }
}
