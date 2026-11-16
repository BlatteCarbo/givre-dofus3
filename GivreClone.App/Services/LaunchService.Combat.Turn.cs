using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.Transport;
using GivreClone.Engine.Accounts;
using GivreClone.Engine;
using GivreClone.Protocol;
using GivreClone.App.Services.CombatParity;
using GivreCore.Protobuf;
using GivreLauncher.Core.Ankama;
using GivreLauncher.Core.Launch;
using GivreLauncher.Core.Proxy;
using GivreLauncher.Core.Transport;
using GivreLauncher.Core.Zaap;
using CoreAccount = GivreLauncher.Core.Accounts.Account;

namespace GivreClone.App.Services;

// ===================== Combat — tour automatique : RunFightTurn, lancer / fin de tour, mutation des combattants =====================
public sealed partial class LaunchService
{
    private readonly ConcurrentDictionary<uint, int> _fightTurnStartCell = new();
    private readonly ConcurrentDictionary<uint, int> _tackledCell = new();
    private void ForgetStatueFight(uint pid)
    {
        _tackledCell.TryRemove(pid, out _);
        _fightTurnStartCell.TryRemove(pid, out _);
    }
    private void CaptureFightTurnStartCell(uint pid) => _fightTurnStartCell[pid] = SelfFighterCell(pid);

    private bool SendCast(uint pid, int cell, int spellId) => SendCast(pid, cell, spellId, out _);

    private bool SendCast(uint pid, int cell, int spellId, out long turboRequestAt, bool companion = false, int journalRound = 0)
    {
        turboRequestAt = 0;
        long journalFightStart = _fightStartAt.GetValueOrDefault(pid);
        int castJournalRound = journalRound > 0 ? journalRound : System.Math.Max(1,
            _teamIntentRound.GetValueOrDefault(pid, _fightTurnNo.GetValueOrDefault(pid)));
        // Recheck live observations at dispatch, including legacy casts selected before a visibility event.
        if (IsInFight(pid) && _fighters.TryGetValue(pid, out var liveFighters)
            && liveFighters.Any(f => f.Cell == cell && f.ActorId != SelfActorId(pid)
                && !(_fighterVisibility.GetValueOrDefault(pid)?.Get(f.ActorId).CanTarget ?? false))) return false;
        var relay = RelayForInject();
        if (relay is null) return false;
        // GARDE DE FENÊTRE DE TOUR : hors de MA fenêtre (jzc d'un autre combattant, ou après MON jxy) le serveur JETTE le cast
        // en silence. C'est ce qui produisait « il est au contact et ne fait rien » : 13 casts injectés, 3 seulement arrivés —
        // un monstre a encaissé NEUF casts fantômes avant de mourir au premier cast réellement dans la fenêtre.
        if (_inFight.TryGetValue(pid, out var infC) && infC && !_myTurnOpen.GetValueOrDefault(pid))
        {
            TransportLog.Line($"[combat] cast {spellId} ANNULÉ : hors de ma fenêtre de tour -> le serveur le jetterait (aucune injection)");
            return false;
        }
        // Même trou que TryMove (fix R1) : si RIEN n'est injecté, aucun isq ne viendra jamais pour ce seuil -> toutes les
        // barrières suivantes épuiseraient leur timeout complet. On rend donc le seuil précédent quand l'injection échoue.
        int prevArm = _seqArmIsq.GetValueOrDefault(pid);
        ArmSeqEnd(pid);   // barrière B : on armera l'attente de la fin de séquence de CE cast (le caller await WaitSeqEnd)
        byte[] pkt = MoveForge.BuildCastFramed(cell, spellId);   // GameActionFightCastRequest {spell_id #1, cell #2}
        // Arm before dispatch: a synchronous/fast server rejection must see this cast.
        turboRequestAt = NoteTurboRequestTx(pid, $"cast:{spellId}");
        NoteCastForModifiers(pid, spellId); // armer avant l'envoi : le réseau peut confirmer pendant Inject
        bool ok = relay.Inject(pid, pkt);
        // Un buff sur MA case ne compte pas : live 06/11 20:33, 12913 relancé tous les 4 tours sur soi pendant 45 tours vides.
        if (ok && cell != SelfFighterCell(pid)) NoteTurnAction(pid);
        if (!ok)
        {
            _lastCastForMods.TryRemove(pid, out _);
            _seqArmIsq[pid] = prevArm;
            if (turboRequestAt > 0)
            {
                _turboLastRequestAt.TryRemove(new KeyValuePair<uint, long>(pid, turboRequestAt));
                _turboCastRejected.TryRemove(pid, out _);
            }
            turboRequestAt = 0;
        }
        ShowInjected(pid, pkt);
        if (_fightStartAt.GetValueOrDefault(pid) == journalFightStart)
            TransportLog.Line(pid, $"[combat] pid {pid} : lance le sort {spellId} sur la cellule {cell} inject={(ok ? "OK" : "ECHEC")} owner={(companion ? "companion" : "self")} round={castJournalRound}");
        return ok;
    }

    // Tours consécutifs terminés sans AUCUNE action injectée (ni sort, ni déplacement). Les 4 suspensions de 5 min
    // (05-06/11) ont toutes suivi 45 à 73 tours vides enchaînés en Turbo (< 1 s chacun) : ennemi pas classé (vague
    // FightNewWaveEvent non suivie), invocations qui se tuent seules, ou défi Statue + cible hors de portée.
    private readonly ConcurrentDictionary<uint, int> _idleTurns = new();
    private readonly ConcurrentDictionary<uint, int> _totalIdleTurnsInFight = new();
    private readonly ConcurrentDictionary<uint, bool> _actedThisTurn = new();
    internal const int IdleTurnsBeforeGiveUp = 6;
    internal const int TotalIdleTurnsBeforeGiveUp = 8;
    private void NoteTurnAction(uint pid) => _actedThisTurn[pid] = true;

    // Garde de rythme Turbo (cf TurboPacing) : horodatages (TickCount64) de MES tours terminés, par pid, élagués à 10 min.
    private readonly ConcurrentDictionary<uint, Queue<long>> _turnDoneTicks = new();
    private readonly ConcurrentDictionary<uint, long> _turboHumanLoggedFight = new();
    // Diagnostic tour vide (GAP-5 / todo 5) : tags de diagnostic déjà sérialisés ce combat (max 1 dump par combat par tag sauf si PacketDumpAll).
    private readonly ConcurrentDictionary<uint, HashSet<string>> _emptyDumpedTagsThisFight = new();
    private void NoteTurnDone(uint pid)
    {
        var q = _turnDoneTicks.GetOrAdd(pid, static _ => new Queue<long>());
        long now = System.Environment.TickCount64;
        lock (q) { q.Enqueue(now); Combat.TurboPacing.PruneAndCount(q, now); }
    }
    /// <summary>true = ce tour (n° <paramref name="turnNo"/> dans le combat) doit tourner à allure humaine. Journalise une fois par combat.</summary>
    private bool TurboHumanPace(uint pid, int turnNo)
    {
        var q = _turnDoneTicks.GetOrAdd(pid, static _ => new Queue<long>());
        int inWindow, inMinute;
        lock (q)
        {
            long now = System.Environment.TickCount64;
            inWindow = Combat.TurboPacing.PruneAndCount(q, now) + 1;   // + ce tour
            inMinute = Combat.TurboPacing.CountLastMinute(q, now) + 1;
        }
        var cfg = AppConfig.Current;
        if (!Combat.TurboPacing.UseHumanPace(turnNo, inWindow, inMinute, cfg.TurboMaxTurnsPerFight, cfg.TurboMaxTurnsPer10Min, cfg.TurboMaxTurnsPerMinute)) return false;
        long fightStart = _fightStartAt.GetValueOrDefault(pid);
        if (!_turboHumanLoggedFight.TryGetValue(pid, out long logged) || logged != fightStart)
        {
            _turboHumanLoggedFight[pid] = fightStart;
            TransportLog.Line($"[combat] rythme humain activé (tour {turnNo} / {inWindow} tours en 10 min) - anti-suspension");
        }
        return true;
    }

    private bool SendEndTurn(uint pid)
    {
        var relay = RelayForInject();
        if (relay is null) return false;
        NoteTurnDone(pid);
        bool actedThisTurn = _actedThisTurn.TryRemove(pid, out var acted) && acted;
        int idle = actedThisTurn ? 0 : _idleTurns.GetValueOrDefault(pid) + 1;
        int totalIdle = actedThisTurn ? _totalIdleTurnsInFight.GetValueOrDefault(pid) : _totalIdleTurnsInFight.GetValueOrDefault(pid) + 1;
        _idleTurns[pid] = idle;
        _totalIdleTurnsInFight[pid] = totalIdle;
        if ((idle >= IdleTurnsBeforeGiveUp || totalIdle >= TotalIdleTurnsBeforeGiveUp) && IsInFight(pid))
        {
            TransportLog.Line($"[combat] {idle} tours consécutifs ({totalIdle} au total) sans aucune action -> abandon du combat (évite la suspension serveur pour tours passés en rafale)");
            _idleTurns.TryRemove(pid, out _);
            _totalIdleTurnsInFight.TryRemove(pid, out _);
            if (FightGiveUp(pid)) { _myTurnOpen[pid] = false; return true; }
        }
        byte[] pkt = MoveForge.BuildTurnFinishFramed();   // FightTurnFinishRequest (vide)
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        // Mon tour est FERMÉ à partir d'ici : horodaté pour reconnaître l'écho serveur qui suit (tour fantôme) et pour que
        // toute action encore en vol s'arrête au lieu d'injecter hors tour.
        _turnEndedAt[pid] = System.Environment.TickCount64;
        _myTurnOpen[pid] = false;
        TransportLog.Line($"[combat] pid {pid} : fin du tour (FightTurnFinishRequest) inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    // Auto-combat V1: a MON tour, cible l'ennemi vivant le plus proche; si castable d'ici (portee +
    // ligne droite si castInLine + ligne de vue si castTestLos) -> lance ; sinon cherche une case atteignable (dans mes
    // PM) d'ou le sort est valide et s'y deplace ; puis finit le tour. Base PA=6, PM=3. Async (delais), anti-reentrance.
    // scriptDriven=true : tour RÉCLAMÉ par un script (combat:playTurn / reprise à l'arrêt du script) — l'autorité du tour et
    // le n° de tour ont déjà été établis par TryClaimScriptTurn ; on part des PA/PM/lancers restants du tracker script.
    // scriptDriven=false (défaut) : chemin auto-combat STRICTEMENT inchangé.
    private async Task RunFightTurn(uint pid, int epoch, bool scriptDriven = false, CancellationToken ct = default, ControlledCombatContext? controlled = null, int journalRound = 0)
    {
        long journalFightStart = _fightStartAt.GetValueOrDefault(pid);
        if (journalRound <= 0) journalRound = _teamIntentRound.GetValueOrDefault(pid);
        // On ATTEND brièvement qu'un run périmé se termine au lieu de PERDRE le tour en silence (ancien `TryAdd -> return`).
        // Si une époque plus récente arrive pendant l'attente, c'est que CE tour-ci est déjà dépassé -> on abandonne.
        ct.ThrowIfCancellationRequested();
        bool owned = false;
        for (int w = 0; w < 60 && !owned; w++)
        {
            owned = _turnRunning.TryAdd(pid, true);
            if (owned) break;
            if (epoch != _turnEpoch.GetValueOrDefault(pid)) return;
            await Task.Delay(25, ct).ConfigureAwait(false);
        }
        if (!owned) { TransportLog.Line("[combat] TOUR PERDU : le tour précédent ne s'est pas terminé à temps (run périmé bloquant)"); return; }
        var previousControlled = _engineControlled.Value;
        _engineControlled.Value = controlled;
        try
        {
            ct.ThrowIfCancellationRequested();
            bool Cast(int cell, int spellId, out long requestAt)
            {
                ct.ThrowIfCancellationRequested();
                requestAt = 0;
                TreasureFightPolicy.Guard? treasureHit = null;
                if (IsTreasureFight(pid))
                {
                    var target = TreasureTarget(pid);
                    if (target is not { } guard || guard.Cell != cell || !TreasureSpellAllowed(pid, spellId)) return false;
                    treasureHit = guard;
                }
                bool sent = epoch == _turnEpoch.GetValueOrDefault(pid)
                    && SendCast(pid, cell, spellId, out requestAt, controlled is not null, journalRound);
                if (sent && treasureHit is { } hit) _treasureAttacked[pid] = (hit.Actor, hit.Cell, _fightTurnNo.GetValueOrDefault(pid));
                return sent;
            }
            // Sans hook script ni override : EngineAutoPlays(pid) ≡ live.AutoFight (condition d'origine).
            if (ManualCombatOn(pid) || !_combat.TryGetValue(pid, out var live) || !(controlled is not null || scriptDriven || EngineAutoPlays(pid))) return;
            if (!LicenseService.HasPermission(LicenseService.Perm.Fight)) return;   // droit serveur "fight" requis pour jouer les tours (gate au POINT D'EXÉCUTION)
            if (controlled is not null) CaptureFightTurnStartCell(pid);
            var profile = live.Clone();                    // snapshot fige pour tout le tour (a l'abri des editions UI)
            // Compteur de MES tours dans CE combat (pas de roundNumber suivi côté proxy) : sert au temps de relance des sorts.
            // Tour script : déjà compté à la réclamation (TryClaimScriptTurn).
            int turnNo = controlled is not null ? ++controlled.Turn : scriptDriven ? System.Math.Max(1, _fightTurnNo.GetValueOrDefault(pid)) : _fightTurnNo.AddOrUpdate(pid, 1, (_, v) => v + 1);
            var ledger = ActiveLedger(pid);
            ledger.StartTurn(turnNo);
            // Snapshot owned by this invocation, never inferred from a later turn header.
            int moveJournalRound = journalRound > 0 ? journalRound : turnNo;
            journalRound = moveJournalRound;
            // Modificateurs de sort (buff de portée…) : ceux dont la durée est écoulée tombent au début de CE tour.
            long modActor = controlled is not null ? controlled.Fighter.ActorId : SelfActorId(pid);
            SpellModsOf(pid, modActor).ExpireAt(turnNo);
            if (AppConfig.Current.AbandonFightAfterTurns is int giveUpAt and > 0 && turnNo > giveUpAt)
            {
                TransportLog.Line($"[combat] tour {turnNo} > {giveUpAt} : combat sans fin, abandon (réglage explicite)");
                FightGiveUp(pid);
                return;
            }
            if (AppConfig.Current.PauseFightAfterTurns is int pauseAt and > 0 && turnNo > pauseAt)
            {
                if (turnNo == pauseAt + 1) TransportLog.Line($"[combat] tour {turnNo} > {pauseAt} : combat anormalement long, le moteur ne joue plus (pause) — reprends la main ou abandonne");
                return;
            }

            // Garde anti-suspension : allure humaine (délais Fast + pause avant fin de tour) au-delà des plafonds Turbo. Ne touche ni
            // le planificateur ni les barrières/accusés Turbo : uniquement des délais en plus.
            bool humanPace = profile.Speed == CombatSpeed.Turbo && TurboHumanPace(pid, turnNo);
            Task HumanEndPause() => humanPace ? Task.Delay(Combat.TurboPacing.EndTurnDelayMs(Random.Shared), ct) : Task.CompletedTask;
            int ap = 6, mp = 3, castsThisTurn = 0;   // repli 6/3 si stats pas encore recues
            void SyncRejectedCast(long requestAt)
            {
                if (TryTurboCastRejection(pid, requestAt, out int availableAp) && availableAp >= 0) ap = availableAp;
            }
            // Delais entre actions selon la vitesse choisie. Fast/Slow = ALEATOIRES (moins detectables) ; Instant = juste ce
            // qu'il faut (le DEPLACEMENT garde ~400ms meme en instantane, le temps que le serveur confirme/refuse - jsj/jqt).
            int CastDelay() => humanPace ? Combat.TurboPacing.CastDelayMs(Random.Shared) : profile.Speed switch { CombatSpeed.Turbo or CombatSpeed.Instant => 150, CombatSpeed.Fast => Random.Shared.Next(450, 900), _ => Random.Shared.Next(1000, 2500) };
            // Instant => 0 : ce 400 ms était l'ANCIEN mécanisme de confirmation, remplacé depuis par le SONDAGE de SelfCell()
            // (le jsj serveur tombe en ~33 ms). Il ne restait qu'un temps mort étiqueté « humanisation » : 375 ms perdues par
            // déplacement, mesurées 3 fois dans la capture. Fast/Slow gardent leur aléa (humanisation réelle).
            int MoveDelay() => humanPace ? Random.Shared.Next(600, 1100) : profile.Speed switch { CombatSpeed.Turbo or CombatSpeed.Instant => 0, CombatSpeed.Fast => Random.Shared.Next(600, 1100), _ => Random.Shared.Next(1200, 3000) };
            int StepDelay() => profile.Speed switch { CombatSpeed.Turbo or CombatSpeed.Instant => 150, CombatSpeed.Fast => Random.Shared.Next(300, 700), _ => Random.Shared.Next(700, 1500) };
            Action refreshRotation = static () => { };   // branché une fois la rotation construite
            int rotationModsVersion = SpellModsOf(pid, modActor).Version;
            // A server ApplySpellModifierEvent may arrive AFTER WaitAfterCast
            // has returned. Refresh at the next decision boundary as well:
            // otherwise a precomputed tuple keeps obsolete AP/range/LoS and
            // the next spell is sent with a legality window the server changed.
            void RefreshRotationOnServerModifierChange()
            {
                int currentVersion = SpellModsOf(pid, modActor).Version;
                if (currentVersion == rotationModsVersion) return;
                refreshRotation();
                rotationModsVersion = currentVersion;
            }
            async Task WaitAfterCast(long requestAt)
            {
                await WaitAfterCastCore(requestAt).ConfigureAwait(false);
                // Buff de portée / PA / lancers : confirmé par le serveur -> les fenêtres de lancer de la rotation changent MAINTENANT
                // (validé ici, ou déjà par l'événement serveur : la version du jeu de modificateurs tranche).
                CommitCastModifiers(pid, turnNo);
                RefreshRotationOnServerModifierChange();
            }
            async Task WaitAfterCastCore(long requestAt)
            {
                // Turbo MITM ne dort plus arbitrairement 150 ms : il attend la fermeture AUTORITAIRE de la séquence puis son
                // accusé (envoyé par le bot en Turbo-ack, sinon par le client visible). Le skipper natif ne réduit que l'affichage.
                if (profile.Speed == CombatSpeed.Turbo && IsSocketPid(pid) && requestAt > 0)
                {
                    // Socket : fin de séquence autoritaire avec sonde courte (600 ms si aucune séquence, grâce si séquence en cours).
                    await WaitTurboActionBarrierWithGrace(pid, requestAt).WaitAsync(ct).ConfigureAwait(false);
                    SyncRejectedCast(requestAt);
                    if (humanPace) await Task.Delay(CastDelay(), ct).ConfigureAwait(false);
                    return;
                }
                if (profile.Speed == CombatSpeed.Turbo && !IsSocketPid(pid))
                {
                    // Profil modifié entre le snapshot du tour et l'injection : si la télémétrie n'a pas armé cette requête,
                    // on garde le délai historique plutôt que de créer une action Turbo non corrélable.
                    if (requestAt <= 0) { await Task.Delay(CastDelay(), ct).ConfigureAwait(false); return; }
                    if (!await WaitTurboActionBarrierWithGrace(pid, requestAt).WaitAsync(ct).ConfigureAwait(false))
                    {
                        // Aucune séquence depuis le lancer : le serveur l'a ignoré (Poutch 24/10 13:51, sort juste après un buff
                        // ou refus de portée). Le tour continue au lieu de crasher sur InvalidOperationException.
                        TransportLog.Line("[combat] sort sans réponse du serveur -> ignoré, le tour continue");
                        return;
                    }
                    SyncRejectedCast(requestAt);
                    if (humanPace) await Task.Delay(CastDelay(), ct).ConfigureAwait(false);
                    return;
                }
                await Task.Delay(CastDelay(), ct).ConfigureAwait(false);
            }

            // BARRIÈRE A : « tu peux jouer » (jyj FightTurnStartPlayingEvent), désormais par FRONT (cf WaitTurnPlayEdge).
            // Sans front jyj = ce n'est PAS mon tour (écho de MA fin de tour pris pour un début) -> on n'injecte RIEN.
            // ⚠ SURTOUT PAS de SendEndTurn ici : un jxy hors tour re-déclenche l'écho = boucle de tours fantômes.
            // Tour script : le front jyj a déjà été consommé à la réclamation (repli) ou _myTurnOpen vient de jzc -> même
            // autorité, relue telle quelle (pas assouplie).
            bool turnOk = scriptDriven ? _myTurnOpen.GetValueOrDefault(pid) : await WaitTurnPlayEdge(pid, 1200).WaitAsync(ct).ConfigureAwait(false);
            if (!turnOk)
            {
                TransportLog.Line($"[combat] TOUR IGNORÉ : aucun début de tour ({_jyjCount.GetValueOrDefault(pid)}/{_jyjConsumed.GetValueOrDefault(pid)}), {System.Environment.TickCount64 - _turnEndedAt.GetValueOrDefault(pid)} ms après MA fin de tour -> ce n'est PAS mon tour (écho de fin de tour), AUCUNE action injectée.");
                return;
            }
            var (mapId, cells) = LoadFightMap(pid);
            ct.ThrowIfCancellationRequested();
            if (mapId == 0 || !_fighters.TryGetValue(pid, out var start) || start.Count == 0) { SendEndTurn(pid); return; }
            _moveGuardUntil[pid] = 0;   // mon tour : la boucle sequence les moves via await -> la garde ne doit pas bloquer
            // ANTI-STUCK : les cases refusees aux tours PRECEDENTS (l'ennemi a bouge depuis, la geometrie a change) ne doivent
            // pas murer l'approche pour tout le combat. Ardoise propre a CHAQUE tour : l'anti-boucle reste valable DANS le tour
            // courant (les refus s'accumulent pendant le tour) mais expire au suivant -> fini le « il passe eternellement sans
            // avancer » cause par un refus isole grave jusqu'a la fin du combat.
            _blockedCells.TryRemove(pid, out _);

            var walk = new bool[MapPathfinder.CellCount];
            var los = new bool[MapPathfinder.CellCount];
            // MARCHABILITÉ DE COMBAT = Mov ET pas `nonWalkableDuringFight` ( `IsWalkableFight` / un bot tiers
            // `NonWalkableDuringFight==1 continue`) : une case marchable hors combat mais bloquée EN combat, si on tente
            // d'y aller, le serveur IGNORE le move en silence -> case bloquée -> il passe. On l'exclut donc du pathfinding.
            if (cells is not null) foreach (var c in cells) if ((uint)c.CellId < walk.Length) { walk[c.CellId] = c.Mov && !c.NonWalkableDuringFight; los[c.CellId] = c.Los; }
            bool IsWalk(int c) => (uint)c < walk.Length && walk[c];
            bool BlocksLos(int c) => (uint)c < los.Length && !los[c];

            // SECTEURS (floor/zone/moveZone) pour la RÈGLE DE CONNEXION Dofus 3 (MapPathfinder.Communicates), reconstruite
            // EXACTEMENT comme le déplacement de MONDE (BotWorldStore.RebuildWalk). RACINE du « des fois il n'attaque pas »
            //: un pas entre 2 cases marchables mais de SECTEURS DÉCONNECTÉS est
            // REFUSÉ EN SILENCE par le serveur (aucun jsj ni jqt → move « ignoré »). Le monde appliquait déjà `StepAllowed`,
            // le COMBAT non → le bot visait des cases de tir injoignables → passait son tour. On thread `StepAllowed` dans
            // TOUT le pathfinding de combat (case de tir / rapprochement / kite / move) pour ne proposer que des pas que le
            // serveur ACCEPTE.
            var floorF = new int[MapPathfinder.CellCount];
            var zoneF = new int[MapPathfinder.CellCount];
            var canMoveZoneF = new bool[MapPathfinder.CellCount];
            for (int i = 0; i < floorF.Length; i++) { floorF[i] = -1; zoneF[i] = -1; }
            if (cells is not null) foreach (var c in cells) if ((uint)c.CellId < floorF.Length)
            {
                canMoveZoneF[c.CellId] = c.MoveZone == 1;
                // secteurs calés sur la marchabilité de COMBAT (Mov && !nonWalkableDuringFight) : une case bloquée en
                // combat n'est pas un pas valide -> floor/zone = -1 -> Communicates la rejette comme le serveur.
                if (c.Mov && !c.NonWalkableDuringFight) { zoneF[c.CellId] = (c.LinkedZone & 0xF8) >> 4; floorF[c.CellId] = c.Floor; }
            }
            bool StepAllowed(int a, int b) => MapPathfinder.Communicates(a, b, floorF, zoneF, canMoveZoneF);

            // _fighters = SEULE verite de ma position (maj par jsj a l'arrivee, jqt au refus, optimiste au move). On la RELIT
            // toujours -> jamais de desync "je crois etre en X". Identité = SelfActorId (id AUTHENTIFIÉ _charId d'abord),
            // PAS le max-positif : sinon en multi-compte/groupe _fighters contient l'allié (id plus grand) et SelfCell
            // renverrait SA case -> je tire/marche depuis la mauvaise case -> refusé (le même symptôme que le bug kkr).
            int SelfCell()
            {
                long me = SelfActorId(pid);
                return me != 0 && _fighters.TryGetValue(pid, out var f)
                    ? f.Where(x => x.ActorId == me).Select(x => x.Cell).DefaultIfEmpty(-1).First() : -1;
            }

            // Case à éviter : soit bloquée CE TOUR (refus transitoire, ex combattant qui bougera), soit OBSTACLE PERSISTANT
            // du combat (mur invoqué, bombe, idole, portail, décor détruit... = case marchable en statique mais bloquée en
            // combat par un état LIVE qu'on n'a pas décodé). On APPREND ces obstacles des refus serveur (cf audit un bot tiers
            // `UpdateObstacles`/occupancy) et on les mémorise pour TOUT le combat -> on ne re-tente plus la même case.
            bool Blocked(int c) => (_blockedCells.TryGetValue(pid, out var b) && b.ContainsKey(c))
                                   || (_fightObstacles.TryGetValue(pid, out var o) && o.ContainsKey(c));

            // Marchabilité de combat LIVE : relit _fighters à CHAQUE appel (jamais un snapshot périmé). CRUCIAL — un move
            // await WaitMoveClear (≤1,5 s) pendant lequel les ennemis MOBILES bougent (jsj retardés arrivent dans _fighters) :
            // forger le chemin avec l'occupation capturée AVANT l'attente pathait vers/à travers une case fraîchement occupée
            // -> refus serveur (le « des fois il n'attaque pas » sur maps de chasse). On relit donc toujours ici + dans TryMove.
            // occSelf = ma propre case (je ne me bloque pas moi-même). isOccupied / un bot tiers IsCellOccupied.
            bool WalkFight(int c)
            {
                if (!IsWalk(c) || Blocked(c)) return false;
                if (_fighters.TryGetValue(pid, out var fl))
                {
                    int me = SelfCell();
                    foreach (var x in fl) if (x.Cell == c && x.Cell != me) return false;
                }
                return true;
            }

            // TACLE (verrouillage Dofus 3) : un ennemi VIVANT orthogonalement adjacent (CellDistance==1) TACLE -> QUITTER cette
            // case draine les PM/PA (souvent TOUT = verrou total) et le serveur REFUSE le déplacement, ALORS QUE le CAST n'est
            // JAMAIS taclé. C'est LA cause racine du « déplacement refusé (communicates=True walk=True occupée=False) » pendant
            // qu'un sort sur la même case PASSE (audit comparatif un bot tiers 2026-09-01; formule un bot tiers:
            // ratio=(esquive+2)/(2*(blocage+2)), perteMP=round(PM*(1-ratio))). Tant qu'on ne décode pas le coût exact (round 2),
            // on ÉVITE le piège (« ne bouge pas si un ennemi est au contact »): si taclé, on NE quitte PAS la
            // case, on cast sur place -> = « distance de kite 0 » / melee-first en Aggressive, et plus aucun refus de tacle.
            bool EnemyAdjacent(int cell) => _fighters.TryGetValue(pid, out var fa)
                && fa.Any(f => IsEnemy(pid, f.ActorId) && MapPathfinder.CellDistance(cell, f.Cell) == 1);

            // Deplacement de combat robuste : injecte (orthogonal + evite les combattants), maj optimiste, attend, relit la
            // vraie case (jqt recale si refus). Renvoie true si on a REELLEMENT bouge (false = refuse / sans effet).
            int moveAttempts = 0;   // plafond d'injections de déplacement PAR TOUR : borne le spam quand beaucoup de cases
                                    // sont refusées (chaque essai coûte ~1 s d'attente de confirmation). Un tour normal en fait 1-3.
            // DÉFIS : contraintes du tour (posées plus bas, une fois PA/PM connus) + PM réellement marchés ce tour (Zombie/Nomade).
            ChallengePlan? plan = null;
            int stepsUsed = 0, statueMoveBudget = 0;
            int statueStart = _fightTurnStartCell.GetValueOrDefault(pid, -1);
            bool PolicyMoveOk(int from, int to)
            {
                var enemies = _fighters.TryGetValue(pid, out var fs)
                    ? fs.Where(f => IsEnemy(pid, f.ActorId)).Select(f => f.Cell).ToList() : new List<int>();
                return CombatMovementDecision.CanMove(profile.MovementPolicy,
                    enemies.Count == 0 ? 0 : enemies.Min(c => MapPathfinder.CellDistance(from, c)),
                    enemies.Count == 0 ? 0 : enemies.Min(c => MapPathfinder.CellDistance(to, c)));
            }
            async Task<bool> TryMove(int from, int to, IReadOnlyList<int>? plannedPath = null)
            {
                ct.ThrowIfCancellationRequested();
                if (to < 0 || to == from) return false;
                if (!PolicyMoveOk(from, to))
                {
                    TransportLog.Line($"[combat] déplacement {from}->{to} annulé : politique ne jamais approcher (défis inclus)");
                    return false;
                }
                if (moveAttempts >= 8) return false;   // trop d'essais ce tour -> on arrête de tenter des déplacements
                if (plan is { NoMove: true } && (from == statueStart || to != statueStart)) return false;
                // GARDE DE FENÊTRE DE TOUR (même raison que SendCast) : un move injecté hors de ma fenêtre jzc est jeté en
                // SILENCE (ni jsj ni jqt) -> on attendait 800 ms puis on loggait « refusé par le serveur ». C'était ça, les refus.
                if (!_myTurnOpen.GetValueOrDefault(pid))
                {
                    TransportLog.Line($"[combat] déplacement {from}->{to} ANNULÉ : hors de ma fenêtre de tour (aucune injection)");
                    return false;
                }
                // BARRIÈRE (moves SEULEMENT) : avant d'injecter un déplacement, on ATTEND que la séquence de l'action
                // PRÉCÉDENTE soit résolue (isq/jxh reçu, OU flux de combat calmé ~350 ms, OU timeout 1500 ms). C'est ELLE qui
                // faisait rejeter le move (jqt) quand on injectait en pleine séquence. Les casts, eux, n'attendent pas.
                // Fenêtres ÉCHELONNÉES par vitesse (les délais cast/move l'étaient déjà, pas celle-ci) : en Instant on ne paie
                // plus 350 ms de silence + 1,5 s de plafond alors que le serveur répond en ~34 ms.
                await WaitMoveClear(pid,
                    profile.Speed switch { CombatSpeed.Turbo => 20, CombatSpeed.Instant => 140, CombatSpeed.Fast => 250, _ => 350 },   // Turbo : la barrière de séquence a déjà attendu l'action précédente
                    profile.Speed switch { CombatSpeed.Turbo => 300, CombatSpeed.Instant => 700, CombatSpeed.Fast => 1100, _ => 1500 }).WaitAsync(ct).ConfigureAwait(false);
                // RELECTURE APRÈS L'ATTENTE (le point clé du bug de refus sur maps de chasse) : pendant WaitMoveClear (≤1,5 s)
                // ma case a pu changer (poussée) ET des ennemis mobiles ont pu se déplacer (jsj retardés). On relit donc ma
                // vraie case, et si `to` est MAINTENANT occupée on annule tout de suite : forger vers une case fraîchement
                // prise = refus serveur garanti. Le caller re-cherchera une autre case (la boucle de placement).
                int src = SelfCell(); if (src < 0) src = from;
                if (plannedPath is not null && (src != from || plannedPath.Count - 1 > MoveBudget()
                    || plannedPath.SkipLast(1).Any(EnemyAdjacent)
                    || plannedPath.Skip(1).Where((c, i) => !PolicyMoveOk(plannedPath[i], c)).Any())) return false;
                if (epoch != _turnEpoch.GetValueOrDefault(pid) || !_myTurnOpen.GetValueOrDefault(pid)) return false;
                if (src == to) return true;                     // déjà arrivé pendant l'attente
                if (!PolicyMoveOk(src, to)) return false;        // ennemis/position relus après la barrière
                bool WalkMove(int c) => WalkFight(c) && (plan is not { NoMove: true } || c == statueStart || !EnemyAdjacent(c));
                if (plan is { NoMove: true })
                {
                    var restoreReach = MapPathfinder.OrthoReachable(src, statueMoveBudget, WalkMove, StepAllowed);
                    int cost = restoreReach.GetValueOrDefault(to, -1);
                    if (!StatueMovementPolicy.CanRestore(statueStart, src, to, cost, statueMoveBudget) || EnemyAdjacent(src)) return false;
                }
                if (_fighters.TryGetValue(pid, out var flNow) && flNow.Any(x => x.Cell == to && x.Cell != src))
                {
                    _blockedCells.GetOrAdd(pid, static _ => new ConcurrentDictionary<int, byte>())[to] = 0;
                    TransportLog.Line($"[combat] déplacement {src}->{to} annulé : case occupée (position fraîche relue après barrière) -> le caller cherche une autre case");
                    return false;
                }
                // Plus aucun ennemi (dernier tué) : le combat se termine côté serveur, tout déplacement serait ignoré (~0,8 s de timeout chacun).
                if (_fighters.TryGetValue(pid, out var flAlive) && !flAlive.Any(x => IsEnemy(pid, x.ActorId)))
                {
                    TransportLog.Line($"[combat] déplacement {src}->{to} ignoré : plus aucun ennemi (fin de combat)");
                    return false;
                }
                // On arme la séquence de CE move (attendue par la PROCHAINE action). `prevArm` = seuil AVANT armement : si le
                // move est REFUSÉ, aucun isq ne sera jamais émis pour lui -> laisser le seuil armé ferait attendre le TIMEOUT
                // COMPLET (1,5 s) à CHAQUE action suivante (« moins rapide même en Instant » : 1,499 s entre le calcul et
                // l'injection). On le REMET donc en place sur tous les chemins d'échec.
                int prevArm = _seqArmIsq.GetValueOrDefault(pid);
                ArmSeqEnd(pid);
                // stepAllowed = règle de connexion Dofus 3 : un chemin qui traverse un secteur déconnecté est refusé en
                // silence par le serveur -> on le passe au forge pour que KeyMovements n'émette QUE des pas acceptables.
                // WalkFight = occupation LIVE (relue ici, à l'instant du forge) : plus aucun snapshot périmé.
                // map_id de la FORGE = l'instance de COMBAT (jpw), PAS _lastMap (map de jeu de rôle qui sert au relief) :
                // le serveur refuse un move de combat portant l'id de jeu de rôle (« coordonnées illisibles »). Repli sur
                // mapId si jpw pas encore vu (jamais en pratique : jpw arrive à l'entrée du combat, avant tout tour).
                long forgeMap = _serverMapId.TryGetValue(pid, out var smid) && smid != 0 ? smid : mapId;
                // Pas RÉELS du chemin (BFS combat, mêmes règles que le forge) : comptés dans stepsUsed (défis Zombie/Nomade).
                var stepsFrom = MapPathfinder.OrthoReachable(src, 63, WalkMove, StepAllowed);
                long injAt = System.Environment.TickCount64;
                ct.ThrowIfCancellationRequested();
                // Arm correlation and refusal generation before dispatch: a local/fast server can reply synchronously.
                int refused0 = _moveRefused.GetValueOrDefault(pid);
                long moveRequestAt = NoteTurboRequestTx(pid, $"move:{src}->{to}");
                if (!MoveTo(pid, forgeMap, src, to, WalkMove, allowDiag: false, StepAllowed, plannedPath))
                {
                    _seqArmIsq[pid] = prevArm;
                    if (moveRequestAt > 0) _turboLastRequestAt.TryRemove(new KeyValuePair<uint, long>(pid, moveRequestAt));
                    return false;
                }
                from = src;   // la suite (attente de confirmation, diag de refus) raisonne sur la case RÉELLE de départ
                moveAttempts++;
                _moveGuardUntil[pid] = 0;
                // Position CONFIRMÉE par le SERVEUR, jamais prédite (: la case du joueur vient de
                // MapMovementEvent `Cells.Last()` — HandleMapMovementEvent maj CellId même pour actorId>0 = moi). Le serveur
                // diffuse MON déplacement de combat en jsj (-> SelfCell change) et un refus en jqt (-> recale). On ATTEND
                // que SelfCell() change au lieu de SUPPOSER l'arrivée : l'ancienne prédiction optimiste (SetSelfFighterCell)
                // desyncait — un move rejeté/tronqué sans jqt immédiat laissait ma case périmée -> tirs hors portée + moves
                // refusés en cascade (le « il passe / il tape sans PO »). On relit toujours SelfCell() ensuite (source unique).
                // ORDRE (fix 2026-09-07) : on SONDE d'abord la confirmation serveur, on humanise ENSUITE avec le RESTE du
                // budget. L'ordre inverse payait le délai ENTIER alors que la réponse était déjà arrivée (jsj à +33 ms) ->
                // les deux s'additionnaient au lieu de se recouvrir. Sondage à 15 ms au lieu de 50 (jitter d'échantillonnage).
                while (SelfCell() == from && System.Environment.TickCount64 - injAt < MoveConfirmWindowMs(pid))
                {
                    if (_moveRefused.GetValueOrDefault(pid) > refused0) break;   // refus EXPLICITE reçu -> inutile d'attendre
                    await Task.Delay(PollMs(pid, 15), ct).ConfigureAwait(false);
                }
                int leftover = MoveDelay() - (int)(System.Environment.TickCount64 - injAt);
                if (leftover > 0) await Task.Delay(leftover, ct).ConfigureAwait(false);
                int after = SelfCell();
                string moveOutcome = _moveRefused.GetValueOrDefault(pid) > refused0 ? "refused" : after >= 0 && after != from ? "confirmed" : "timeout";
                if (_fightStartAt.GetValueOrDefault(pid) == journalFightStart)
                    TransportLog.Line(pid, $"[combat] move_result owner={(controlled is null ? "self" : "companion")} round={moveJournalRound} from={from} target={to} final={after} outcome={moveOutcome}");
                if (TurboMetricsEnabled(pid))
                {
                    long elapsed = System.Environment.TickCount64 - injAt;
                    TransportLog.Line($"[turbo-metric] move_request_to_server_{moveOutcome}_ms={elapsed} from={from} target={to} final={after} journal=canonical");
                }
                if (after != from && _moveRefused.GetValueOrDefault(pid) > refused0)
                {
                    // Refus EXPLICITE avec recalage AILLEURS : ma case connue était périmée (poussée non vue). Ce n'est PAS
                    // une arrivée : aucune séquence de déplacement ne viendra. Avant : « after != from » était pris pour un
                    // déplacement réussi -> barrière attendue 5 s -> tour abandonné (Shadowbot 24/10 14:16:12, 14:24:54).
                    // La case visée n'est pas bloquée : elle peut être atteignable depuis la vraie case.
                    _seqArmIsq[pid] = prevArm;
                    TransportLog.Line($"[combat] déplacement {from}->{to} refusé : position réelle {after} -> le tour continue depuis cette case");
                    return false;
                }
                if (after != from && profile.Speed == CombatSpeed.Turbo && IsSocketPid(pid)) await WaitTurboActionBarrier(pid, moveRequestAt, 1200).WaitAsync(ct).ConfigureAwait(false);
                else if (after != from && profile.Speed == CombatSpeed.Turbo && moveRequestAt > 0
                    && !await WaitTurboActionBarrierWithGrace(pid, moveRequestAt).WaitAsync(ct).ConfigureAwait(false))
                    TransportLog.Line("[combat] deplacement sans reponse du serveur -> le tour continue");
                if (after != from) stepsUsed += stepsFrom.TryGetValue(after, out var stp) ? stp : MapPathfinder.CellDistance(from, after);
                if (after == to) return true;                 // arrivé (la séquence de ce move sera attendue par la PROCHAINE action)
                if (after != from) return false;              // arrivé AILLEURS (tronqué/poussé) : SelfCell()=réel, pas de tir d'ici
                // Toujours en `from` après le timeout (le serveur a REFUSÉ/IGNORÉ le move : jqt recalé sur `from`, ou rien).
                // On CLASSE le refus:
                //  - case OCCUPÉE par un combattant suivi -> transitoire (il peut bouger) : bloquée SEULEMENT ce tour.
                //  - case LIBRE de tout combattant suivi mais refusée -> OBSTACLE PERSISTANT (mur/bombe/idole/occupant non
                //    décodé) : on la mémorise pour TOUT le combat -> on ne la re-proposera plus (fini le « il retape la même
                //    case refusée à chaque tour et passe »).
                // REVERT (apprentissage persistant retiré) : les refus se sont révélés SYSTÉMATIQUES (pas de vrais obstacles
                // — `communicates=True walk=True occupée=False` partout), donc les mémoriser emmurait le bot. On bloque
                // seulement CE TOUR (vidé au tour suivant) le temps que l'audit trouve la vraie cause.
                bool occ = _fighters.TryGetValue(pid, out var ff) && ff.Any(x => x.Cell == to && x.Cell != from);
                _blockedCells.GetOrAdd(pid, static _ => new ConcurrentDictionary<int, byte>())[to] = 0;
                // tacleSuspect = ma case de départ est-elle au contact d'un ennemi ? (test DISCRIMINANT demandé par l'audit) :
                //   tacleSuspect=True + case cible vide -> TACLE confirmé (verrou). tacleSuspect=False -> occupation non suivie/autre.
                TransportLog.Line($"[combat] déplacement {from}->{to} refusé par le serveur -> bloqué ce tour. diag: communicates={StepAllowed(from, to)} walk={IsWalk(to)} occupée={occ} tacleSuspect={EnemyAdjacent(from)} floor {floorF[from]}->{floorF[to]} zone {zoneF[from]}->{zoneF[to]}");
                if (EnemyAdjacent(from)) _tackledCell[pid] = from;
                // Move DROPPÉ par le serveur : sa séquence n'existera jamais -> on rend le seuil précédent, sinon la barrière
                // du prochain essai/action épuise le timeout complet (cascade de lenteur observée après un refus).
                _seqArmIsq[pid] = prevArm;
                return false;
            }

            // Rotation : sorts actifs valides tries par priorite, avec leurs donnees de combat (PA/portee/en-ligne/LoS).
            _gameNames.EnsureCombatLoaded();   // données de SORTS seulement (pas tout EnsureLoaded) : sans elles la rotation est VIDE au 1er tour
            var myCharsMap = SelfCharacteristics(pid);
            var learnedIds = LearnedSpells(pid);
            var learnedSet = learnedIds.Count > 0 ? learnedIds.ToHashSet() : null;
            IReadOnlyList<SpellConfig> validProfileSpells = learnedSet is null
                ? profile.Spells
                : profile.Spells.Where(sp => sp.SpellId <= 0 || sp.SpellId == 413 || learnedSet.Contains(sp.SpellId)).ToList();
            bool fallbackAutoForForeignProfile = !profile.AutoSpells
                && learnedSet is { Count: > 0 }
                && profile.Spells.Any(sp => sp.Enabled && sp.SpellId > 0)
                && !validProfileSpells.Any(sp => sp.Enabled && sp.SpellId > 0);
            if (fallbackAutoForForeignProfile)
                TransportLog.Line($"[combat] pid {pid} : sorts du profil absents des sorts appris du perso -> repli automatique sur les sorts appris");
            IEnumerable<SpellConfig> source = validProfileSpells;
            if (controlled is not null) { profile.Spells.Clear(); profile.AutoSpells = true; }
            if (profile.AutoSpells || fallbackAutoForForeignProfile)   // mode automatique : + sorts appris à dégâts absents de la liste (ordre dégâts/PA)
                source = GivreClone.App.Services.Combat.AutoSpellRotation.Build(validProfileSpells, learnedIds.Select(id =>
                {
                    int lvl = LearnedSpellLevel(pid, id);
                    return new GivreClone.App.Services.Combat.LearnedSpellInfo(id, _gameNames.SpellCombat(id, lvl)?.Ap ?? 0,
                        _gameNames.GetSpellDamage(id, lvl)?.Avg ?? 0, scriptDriven ? null : _gameNames.GetSpellDescriptor(id, lvl));
                }), scriptDriven ? null : new Combat.AutoSpellOptions(
                    controlled is null ? profile.AutoElements : Combat.AutoCombatElements.Automatic,
                    myCharsMap, RequireDescriptors: true));
            var rotation = source
                .Where(sp => sp.Enabled && sp.SpellId > 0)
                .OrderBy(sp => sp.Priority)
                .Select(sp => new RotationSpell(sp, SpellCombatLive(pid, sp.SpellId, LearnedSpellLevel(pid, sp.SpellId))))
                .Where(x => x.Data is not null)
                .ToList();
            // Les données d'un sort changent EN COURS de tour (buff de portée) : on met à jour l'objet, jamais la liste, que les
            // boucles foreach en cours parcourent (List<T> lève « Collection was modified » sinon).
            refreshRotation = () =>
            {
                foreach (var rs in rotation) rs.Data = SpellCombatLive(pid, rs.Cfg.SpellId, LearnedSpellLevel(pid, rs.Cfg.SpellId));
            };

            // Plafonds de lancer du SORT honorés CE tour (données du jeu via GetSpellDamage), MIROIR COMPLET de CombatSimulator.CapOk :
            //  - maxCastPerTarget : (sort, cible) -> nb. Clé = ActorId (PAS la cellule : un sort de poussée/attirance déplace la
            //    cible en cours de tour -> la cellule désync du combattant ; l'ActorId est stable, comme dans le simulateur).
            //  - maxCastPerTurn : (sort) -> nb total ce tour. cap<=0 (donnée absente) = illimité. Sans ça le serveur rejetait les
            //    lancers au-delà de la limite (PA/tour gaspillés, injections inutiles). Dicts par-tour (réinit. à chaque RunFightTurn).
            var turnMods = SpellModsOf(pid, modActor);   // « +N lancers par tour / par cible » (effets 290/291) relèvent les plafonds du sort
            bool TargetCapOk(int spellId, long actor, int cap) => cap <= 0 || ledger.GetTargetCastsThisTurn(spellId, actor) < cap + turnMods.ExtraCastsPerTarget(spellId);
            bool SpellTurnCapOk(int spellId, int cap) => cap <= 0 || ledger.GetCastsThisTurn(spellId) < cap + turnMods.ExtraCastsPerTurn(spellId);
            void MarkSpellCast(int spellId)
            {
                NoteChallengeSpell(pid, spellId);
            }
            var teamDamage = new Dictionary<long, int>();
            var teamSupport = new List<Combat.TeamSupportIntent>();
            var teamStartLife = start.ToDictionary(f => f.ActorId, f => FighterAbsHp(pid, f.ActorId));
            var teamStartShield = start.ToDictionary(f => f.ActorId, f =>
                _fighterStats.TryGetValue(pid, out var shieldStats) && shieldStats.TryGetValue(f.ActorId, out var shieldFighter)
                    && shieldFighter.Characteristics.TryGetValue(FightStatsReader.IdShield, out int shield) ? (int?)shield : null);
            var sharedFight = TeamIntentFight(pid);
            int teamRound = Math.Max(1, _teamIntentRound.GetValueOrDefault(pid, turnNo));
            var teamView = sharedFight is { } shared ? _teamIntents.Read(shared.Key, teamRound, SelfActorId(pid)) : Combat.TeamIntentView.Empty;
            using var teamPublication = new TeamIntentPublication(() =>
            {
                if (sharedFight is null) return;
                var targets = teamDamage.Select(hit => new Combat.TeamTargetIntent(hit.Key, hit.Value,
                    teamStartLife.GetValueOrDefault(hit.Key) is int hp && hit.Value >= hp, teamStartLife.GetValueOrDefault(hit.Key))).ToArray();
                PublishTeamIntent(pid, new Combat.TeamIntent(SelfActorId(pid), teamRound, targets, SelfCell(), teamSupport, Environment.TickCount64));
            });
            void MarkCast(int spellId, long actor)
            {
                NoteChallengeSpell(pid, spellId);
                _lastEngineHit[pid] = (actor, Environment.TickCount64);
                if (sharedFight is null) return;
                if (IsEnemy(pid, actor))
                {
                    if (_gameNames.GetSpellDamage(spellId, LearnedSpellLevel(pid, spellId)) is { Max: > 0 } damage)
                        teamDamage[actor] = teamDamage.GetValueOrDefault(actor) + PerCastDamage(damage);
                }
                else if (_gameNames.GetSpellDescriptor(spellId, LearnedSpellLevel(pid, spellId)) is { } descriptor)
                {
                    int heal = 0, shield = 0;
                    foreach (var effect in descriptor.Effects.Where(e => e.IsCertain))
                    {
                        int amount = Math.Max(0, (effect.Min + effect.Max) / 2);
                        if (effect.Kind == Combat.SpellEffectKind.Heal) heal += amount;
                        if (effect.Kind == Combat.SpellEffectKind.Shield) shield += amount;
                    }
                    if (heal > 0 || shield > 0) teamSupport.Add(new Combat.TeamSupportIntent(actor, heal, shield,
                        teamStartLife.GetValueOrDefault(actor), teamStartShield.GetValueOrDefault(actor)));
                }
            }

            // Ciblage « finish/execute » (profile.FinishKill) : dégâts que la rotation ABORDABLE (PA COURANTS, ordre de
            // priorité, plafonds par-cible) peut poser sur UNE cible ce tour. Sert à flotter en tête les ennemis tuables.
            // Moyenne (conservateur-ish), ignore portée/LdV (optimiste — le ciblage aval filtre). Recalculé au fil des PA.
            // Dégâts d'UN lancer pour le budget « achever » : estimation avec MES caractéristiques (force/intel…, % dommages, dommages fixes) quand la fiche est connue
            // (prudente : -10 %, résistances de la cible ignorées), sinon les dés de base du sort comme avant.
            int PerCastDamage(SpellDamageInfo dd)
            {
                if (myCharsMap.Count == 0) return dd.Avg;
                int est = (int)System.Math.Round(GivreClone.App.Services.Combat.DamageEstimator.Estimate(dd.Min, dd.Max, GivreClone.App.Services.Combat.DamageEstimator.ElementOf(dd.Element), myCharsMap).Average * 0.9);
                return est > 0 ? est : dd.Avg;
            }
            int KillBudget()
            {
                RefreshRotationOnServerModifierChange();
                int budget = ap, dmg = 0;
                foreach (var (c, d) in rotation)
                {
                    if (c.OnMe || c.TargetKind == SpellTargetKind.Ally || c.IsSummon || d is null) continue;
                    int cost = d.Value.Ap; if (cost <= 0) continue;
                    if (_gameNames.GetSpellDamage(c.SpellId, LearnedSpellLevel(pid, c.SpellId)) is not { Max: > 0 } dd || dd.Avg <= 0) continue;   // Avg>0 : parité simulateur (sp.AvgDamage) + ne pas budgéter un sort à 0 dégât
                    int maxc = System.Math.Max(1, c.PerTurn); if (dd.MaxPerTarget > 0) maxc = System.Math.Min(maxc, dd.MaxPerTarget);
                    int aff = System.Math.Min(maxc, budget / cost);
                    if (aff <= 0) continue;
                    dmg += aff * PerCastDamage(dd); budget -= aff * cost;
                }
                return dmg;
            }
            // Réordonne (STABLE) : ennemis tuables ce tour d'abord, ordre du mode de ciblage préservé dans chaque groupe.
            // No-op si FinishKill off / budget nul / rien à séparer -> ordre inchangé (fail-open, jamais de cast en moins).
            System.Collections.Generic.List<(int Cell, long ActorId)> KillFirst(System.Collections.Generic.List<(int Cell, long ActorId)> ordered)
            {
                if (teamView.Intents.Count > 0)
                    return teamView.OrderTargets(ordered, e => e.ActorId, actor => FighterAbsHp(pid, actor), KillBudget());
                // Suiveur d'équipe : la cible que le chef vient de frapper passe en tête (derrière les tuables, posés ensuite).
                if (TeamViewOf(pid) is { } tv && tv.LeaderPid != pid && _lastEngineHit.TryGetValue(tv.LeaderPid, out var lh)
                    && Environment.TickCount64 - lh.At < GivreClone.App.Services.Combat.TeamFocus.FreshMs)
                    ordered = GivreClone.App.Services.Combat.TeamFocus.LeaderTargetFirst(ordered, e => e.ActorId, lh.Actor);
                if (!profile.FinishKill || ordered.Count < 2) return ordered;
                int budget = KillBudget(); if (budget <= 0) return ordered;
                System.Collections.Generic.List<(int Cell, long ActorId)> kill = new(), rest = new();
                foreach (var e in ordered) { if (FighterAbsHp(pid, e.ActorId) is int h && h <= budget) kill.Add(e); else rest.Add(e); }
                if (kill.Count == 0 || rest.Count == 0) return ordered;
                kill.AddRange(rest); return kill;
            }
            // PA/PM COURANTS au debut du tour (deja regeneres, et amputes d'une eventuelle reduction ennemie) -> budget
            // REEL, sinon on sur-budgete (ex : PA reduit a 5 mais on tente 2 casts a 3 = "2 PA dispo, il en faut 3").
            bool mpLive = SelfStats(pid) is { FighterId: not 0 };
            (ap, mp) = TurnStartBudget(pid);
            if (sharedFight is { } teamFight)
            {
                var focusOrder = teamView.OrderTargets(start.Where(f => IsEnemy(pid, f.ActorId)), f => f.ActorId,
                    actor => FighterAbsHp(pid, actor), KillBudget());
                TransportLog.Line($"[combat-team] turn={turnNo} allies={teamFight.Allies} focus={focusOrder.FirstOrDefault().ActorId} reserved=[{string.Join(",", teamView.ReservedKills.OrderBy(a => a))}]");
            }
            // Tour script confié au moteur (combat:playTurn) : on repart de ce qu'il RESTE (PA/PM dépensés par le script,
            // lancers déjà faits ce tour -> plafonds par sort/par cible honorés).
            if (scriptDriven && ActiveScriptTurn(pid) is { } sst)
                lock (sst)
                {
                    ap = System.Math.Min(ap, sst.ApLeft); mp = System.Math.Min(mp, sst.MpLeft); castsThisTurn = sst.TotalCasts;
                    // Le SCRIPT reste l'auteur du compte (Script.cs -> RecordCast) : on NE rejoue PAS ses compteurs.
                    // On relève chaque compteur à son maximum : la reprise complète ce qui manque (le compte par
                    // cible, que le script n'enregistre pas) et ne peut ni effacer ni doubler un lancer déjà compté
                    // (seconde reprise du même tour).
                    ledger.ImportScriptCasts(turnNo, sst.CastsPerSpell, sst.CastsPerTarget);
                }
            int myRange = SelfRange(pid);   // Portée (caractéristique) -> allonge des sorts boostables
            TransportLog.Line($"[combat] TOUR style={profile.Style} cible={profile.Target} vitesse={profile.Speed} sorts=[{string.Join(",", rotation.Select(r => r.Cfg.SpellId))}] maCellule={SelfCell()} (PA{ap} PM{mp} PO+{myRange}) [seq fin={_isqCount.GetValueOrDefault(pid)} début={_jyjCount.GetValueOrDefault(pid)}/{_jyjConsumed.GetValueOrDefault(pid)}]");
            TransportLog.Line("[combat] portées réelles : " + string.Join(", ", rotation.Where(r => r.Data is not null && !r.Cfg.OnMe)
                .Select(r => Combat.SpellReach.Describe(r.Cfg.SpellId, r.Data!.Value.Range, r.Data!.Value.MinRange, r.Data!.Value.Rangeable, myRange))));
            if (myCharsMap.Count > 0)
            {
                var estLine = rotation.Where(r => !r.Cfg.OnMe && r.Cfg.TargetKind != SpellTargetKind.Ally && !r.Cfg.IsSummon && r.Data is not null && _gameNames.GetSpellDamage(r.Cfg.SpellId, LearnedSpellLevel(pid, r.Cfg.SpellId)) is { Max: > 0 })
                    .Select(r => { var dd = _gameNames.GetSpellDamage(r.Cfg.SpellId, LearnedSpellLevel(pid, r.Cfg.SpellId))!.Value; return $"{r.Cfg.SpellId}≈{GivreClone.App.Services.Combat.DamageEstimator.Estimate(dd.Min, dd.Max, GivreClone.App.Services.Combat.DamageEstimator.ElementOf(dd.Element), myCharsMap).Average:0}"; }).ToList();
                if (estLine.Count > 0) TransportLog.Line($"[combat] dégâts estimés par lancer (sans résistances) : {string.Join(" ", estLine)}");
            }
            NoteTurnHp(pid, turnNo);   // replay de combat : PV de chacun au début de mon tour
            // Baseline de séquence pour le 1er déplacement du tour : le début de tour OUVRE une séquence (animation d'ouverture,
            // déplacements d'ennemis pré-tour…). On mémorise le compteur isq à cet instant -> WaitMoveClear n'autorisera l'injection
            // du 1er move QUE lorsque cette séquence se sera refermée (isq > baseline). Corrige le "1er move de chaque tour refusé"
            // (log : il partait dans la séquence d'ouverture encore ouverte -> DROP silencieux -> tour passé). Ré-armé par chaque
            // cast/move ensuite (ArmSeqEnd), donc les actions suivantes attendent la fermeture de LEUR propre séquence.
            _seqArmIsq[pid] = _isqCount.GetValueOrDefault(pid);

            // DÉFIS actifs non tranchés -> contraintes du tour : déplacement (Statue/Zombie/Nomade), sorts (Versatile/Économe/
            // Élémentaire), cibles (Premier/Élitiste/Focus/Cruel…) et case de fin de tour (Hardi/Prudent/Collant…). null = libre.
            plan = BuildChallengePlan(pid, profile, SelfCell(), IsWalk, BlocksLos, System.Environment.TickCount64);
            plan = ApplyChallengeHpThreshold(pid, profile, plan);
            int mpStart = mp, gainedMp = 0;
            int MoveBudget()
            {
                // Pre-attack challenge moves also spend PM. Their confirmed steps must not
                // become available again when the normal rotation searches for a firing cell.
                int remaining = CombatParity.FinishingMovePolicy.RemainingMp(mp, mpStart, stepsUsed, mpLive ? SelfStats(pid)?.Pm : null, gainedMp);
                return plan is null ? remaining : plan.NoMove ? 0
                    : plan.OneMove ? Math.Max(0, Math.Min(remaining, 1 - stepsUsed)) : remaining;
            }
            bool SpellOk(int spellId) => ChallengeSpellOk(pid, plan, spellId, ledger.GetSpellCastsSnapshot());
            bool TargetOk(long actor) => _fighterVisibility.TryGetValue(pid, out var visibility)
                && visibility.Get(actor).CanTarget && (plan is null || plan.TargetOk(actor));
            bool ZoneOk(int spellId, int from, int targetCell) => ChallengeZoneOk(pid, plan, spellId, targetCell, from, targetCell);
            bool EnemyZoneOk(int spellId, int from, int targetCell, int minimumHits)
            {
                if (!ZoneOk(spellId, from, targetCell)) return false;
                var zone = _gameNames.SpellZone(spellId, LearnedSpellLevel(pid, spellId));
                if (zone is not { Size: > 0 } z) return true;
                if (!_fighters.TryGetValue(pid, out var snapshot)) return false;
                var area = CombatZones.GetCells(z.Shape, targetCell,
                    CombatZones.OrientationTo(from, targetCell), z.MinSize, z.Size);
                // Les vrais alliés sur la carte sont protégés du tir ami. On n'ajoute PAS `from` (la case du lanceur) :
                // sinon tout sort de zone lancé au contact ou autour de soi est rejeté avec "ally_in_zone", ce qui forçait
                // le bot à décaler son tir sur une case vide à côté ("sort de zone à côté au lieu de taper tout le monde").
                return CombatAoeSafety.Evaluate(area,
                    snapshot.Where(f => IsEnemy(pid, f.ActorId) && TargetOk(f.ActorId)).Select(f => f.Cell),
                    snapshot.Where(f => f.ActorId != SelfActorId(pid) && !IsEnemy(pid, f.ActorId)).Select(f => f.Cell),
                    minimumHits).Allowed;
            }

            // Portée maximale de nos sorts offensifs (portée modifiable comprise) : sert au Fuyard trop loin pour tirer.
            int FleeingAttackReach() => rotation
                .Where(r => !r.Cfg.OnMe && !r.Cfg.SelfBuff && !r.Cfg.IsSummon && r.Cfg.TargetKind != SpellTargetKind.Ally && r.Data is not null)
                .Select(r => r.Data!.Value.Range + (r.Data!.Value.Rangeable ? myRange : 0)).DefaultIfEmpty(0).Max();

            // Départage des cases de tir à coût en PM égal (inspiré du planificateur de Jitsuri) :
            //  1) sort de ZONE : le plus d'ennemis touchés, alliés pénalisés (CombatAoeSafety.Score) ;
            //  2) moins d'ennemis AU CONTACT de la case (chaque contact = risque de tacle au prochain déplacement) ;
            //  3) distance à la cible : Agressif se colle, les autres styles gardent la portée maximale.
            int CastCellTie(int spellId, int apCost, int cell, int targetCell)
            {
                int score = 0, contacts = 0;
                if (_fighters.TryGetValue(pid, out var snap))
                {
                    var foes = snap.Where(f => IsEnemy(pid, f.ActorId)).Select(f => f.Cell).ToList();
                    contacts = foes.Count(fc => fc != targetCell && MapPathfinder.CellDistance(cell, fc) <= 1);
                    if (_gameNames.SpellZone(spellId, LearnedSpellLevel(pid, spellId)) is { Size: > 0 } z)
                    {
                        var area = CombatZones.GetCells(z.Shape, targetCell, CombatZones.OrientationTo(cell, targetCell), z.MinSize, z.Size).ToHashSet();
                        int allies = snap.Count(f => f.ActorId != SelfActorId(pid) && !IsEnemy(pid, f.ActorId) && area.Contains(f.Cell));
                        score = CombatAoeSafety.Score(foes.Distinct().Count(area.Contains), allies, apCost);
                    }
                }
                int dist = MapPathfinder.CellDistance(cell, targetCell);
                return -score + contacts * 50 + (profile.Style == CombatStyle.Aggressive ? dist : -dist);
            }

            // DIAG "Log tout" : si des combattants sont SUIVIS mais AUCUN n'est classé ennemi, dire POURQUOI (id, case,
            // allié ?, confirmé ennemi ?, équipe) au lieu de passer le tour en SILENCE. Rend visible le reclassement kae
            // (bug "a attaqué 10 tours puis stoppé") et tout futur drift du champ team du jxg. Ne logge QUE le cas pathologique.
            if (_fighters.TryGetValue(pid, out var dfs) && dfs.Count > 0 && !dfs.Any(f => IsEnemy(pid, f.ActorId)))
            {
                long meId = SelfActorId(pid);
                TransportLog.Line($"[combat] ⚠ 0 ennemi classé sur {dfs.Count} combattant(s) suivis (moi={meId}, monÉquipe={(TeamOf(pid, meId)?.ToString() ?? "?")}) : "
                    + string.Join(" ; ", dfs.Select(f => $"id={f.ActorId}@{f.Cell} allié={(_allies.TryGetValue(pid, out var al) && al.ContainsKey(f.ActorId))} confEnn={IsConfirmedEnemy(pid, f.ActorId)} équipe={(TeamOf(pid, f.ActorId)?.ToString() ?? "?")}"))
                    + " -> tour passé sans cible (cause probable : équipe mal lue — FightTeamUpdateEvent / GameFightShowFighter)");
            }

            // Nb d'ennemis vivants suivis CE tour (pour la condition « nb d'ennemis mini » par sort). Snapshot en tête de tour.
            int enemyCountTurn = _fighters.TryGetValue(pid, out var allFt) ? allFt.Count(f => IsEnemy(pid, f.ActorId)) : 0;

            bool AreaCanFinishFight() => AreaCanFinishFrom(SelfCell());
            // Un sort offensif de la rotation peut-il toucher un ennemi autorisé depuis `here` (portée, ligne de vue, zone sûre) ?
            bool CanHitFrom(int here)
            {
                if (here < 0 || castsThisTurn >= profile.MaxCastsPerTurn || !_fighters.TryGetValue(pid, out var snapshot)) return false;
                RefreshRotationOnServerModifierChange();
                int me = SelfCell();
                foreach (var (cfg, data) in rotation)
                {
                    if (cfg.OnMe || cfg.SelfBuff || cfg.IsSummon || cfg.TargetKind == SpellTargetKind.Ally
                        || data is null || data.Value.Ap > ap || !SpellOk(cfg.SpellId)) continue;
                    var s = data.Value;
                    int range = Math.Max(s.MinRange, s.Range + (s.Rangeable ? myRange : 0));
                    var (lo, hi) = PlacementRange(cfg, s.MinRange, range);
                    foreach (var e in snapshot.Where(f => IsEnemy(pid, f.ActorId)))
                    {
                        if (!TargetOk(e.ActorId)) continue;
                        var occupied = snapshot.Where(f => f.Cell != me && f.Cell != here && f.Cell != e.Cell).Select(f => f.Cell).ToHashSet();
                        if (Combat.CastLegality.CanCastGeometry(here, e.Cell, lo, hi, s.CastInLine, s.CastInDiagonal, s.CastTestLos, BlocksLos, occupied.Contains)
                            && EnemyZoneOk(cfg.SpellId, here, e.Cell, cfg.MinAoeHits)) return true;
                    }
                }
                return false;
            }
            bool AreaCanFinishFrom(int here)
            {
                if (castsThisTurn >= profile.MaxCastsPerTurn || here < 0 || !_fighters.TryGetValue(pid, out var snapshot)) return false;
                RefreshRotationOnServerModifierChange();
                var enemies = snapshot.Where(f => IsEnemy(pid, f.ActorId)).ToList();
                var lives = enemies.Select(e => (e.Cell, FighterAbsHp(pid, e.ActorId))).ToList();
                foreach (var (cfg, data) in rotation)
                {
                    if (cfg.SelfBuff || cfg.IsSummon || cfg.TargetKind == SpellTargetKind.Ally
                        || data is null || data.Value.Ap > ap || !SpellOk(cfg.SpellId)) continue;
                    if (!SelfSpellGateOk(pid, cfg, ap, turnNo, enemies.Count)) continue;
                    var desc830 = _gameNames.GetSpellDescriptor(cfg.SpellId, LearnedSpellLevel(pid, cfg.SpellId));
                    if (ledger.IsRechargeActive(cfg.SpellId, turnNo, cfg.Cooldown, desc830?.MinCastInterval ?? 0, desc830?.InitialCooldown ?? 0)) continue;
                    int level = LearnedSpellLevel(pid, cfg.SpellId);
                    if (_gameNames.SpellZone(cfg.SpellId, level) is not { Size: > 0 } zone
                        || _gameNames.GetSpellDamage(cfg.SpellId, level) is not { Max: > 0 } damage) continue;
                    if (!CombatParity.FinishingMovePolicy.HasCastBudget(castsThisTurn, profile.MaxCastsPerTurn,
                        ledger.GetCastsThisTurn(cfg.SpellId), cfg.PerTurn,
                        SpellTurnCapOk(cfg.SpellId, damage.MaxPerTurn))) continue;
                    int targetCap = damage.MaxPerTarget;
                    if (cfg.PerTarget > 0) targetCap = targetCap > 0 ? Math.Min(targetCap, cfg.PerTarget) : cfg.PerTarget;
                    double estimate = GivreClone.App.Services.Combat.DamageEstimator.Estimate(damage.Min, damage.Max,
                        GivreClone.App.Services.Combat.DamageEstimator.ElementOf(damage.Element), myCharsMap).Min * 0.9;
                    foreach (int center in cfg.OnMe ? new[] { here } : enemies.Select(e => e.Cell))
                    {
                        if (!cfg.OnMe)
                        {
                            var s = data.Value;
                            int range = Math.Max(s.MinRange, s.Range + (s.Rangeable ? myRange : 0));
                            var (lo, hi) = PlacementRange(cfg, s.MinRange, range);
                            var occupied = snapshot.Where(f => f.Cell != here && f.Cell != center).Select(f => f.Cell).ToHashSet();
                            var target = enemies.First(e => e.Cell == center);
                            if (!TargetOk(target.ActorId) || !TargetSpellGateOk(pid, cfg, target.ActorId)
                                || !TargetCapOk(cfg.SpellId, target.ActorId, targetCap)
                                || !CastCellOk(pid, cfg.SpellId, center, true)
                                || !Combat.CastLegality.CanCastGeometry(here, center, lo, hi, s.CastInLine, s.CastInDiagonal, s.CastTestLos, BlocksLos, occupied.Contains)
                                || !EnemyZoneOk(cfg.SpellId, here, center, cfg.MinAoeHits)) continue;
                        }
                        int toward = cfg.OnMe ? enemies.OrderBy(e => MapPathfinder.CellDistance(here, e.Cell)).Select(e => e.Cell).FirstOrDefault(here) : center;
                        if (cfg.OnMe && (!TargetCapOk(cfg.SpellId, SelfActorId(pid), targetCap)
                            || !ChallengeZoneOk(pid, plan, cfg.SpellId, here, here, toward))) continue;
                        var area = CombatZones.GetCells(zone.Shape, center, CombatZones.OrientationTo(here, toward), zone.MinSize, zone.Size).ToHashSet();
                        if (ChallengeFinishPolicy.CanFinishArea(lives, area, estimate)) return true;
                    }
                }
                return false;
            }

            // Tactical treasure fights never enter the damage-based rotation or its AoE/last-chance fallbacks.
            if (IsTreasureFight(pid))
            {
                while (castsThisTurn < profile.MaxCastsPerTurn && _myTurnOpen.GetValueOrDefault(pid))
                {
                    ct.ThrowIfCancellationRequested();
                    RefreshRotationOnServerModifierChange();
                    if (TreasureTarget(pid) is not { } target || !_fighters.TryGetValue(pid, out var snapshot))
                    {
                        TransportLog.Line($"[combat] coffre : aucune invocation sur pattes identifiee ; fin du tour");
                        break;
                    }
                    int here = SelfCell();
                    RotationSpell? best = null;
                    double bestScore = 0;
                    foreach (var r in rotation)
                    {
                        var c = r.Cfg;
                        int id = c.SpellId;
                        if (r.Data is not { } s || c.OnMe || c.SelfBuff || c.IsSummon || c.TargetKind == SpellTargetKind.Ally
                            || !TreasureSpellAllowed(pid, id) || !SpellOk(id) || !TargetOk(target.Actor)
                            || !SelfSpellGateOk(pid, c, ap, turnNo, enemyCountTurn) || !TargetSpellGateOk(pid, c, target.Actor)
                            || _gameNames.GetSpellDamage(id, LearnedSpellLevel(pid, id)) is not { Max: > 0 } damage) continue;
                        int used = ledger.GetCastsThisTurn(id);
                        var desc890 = _gameNames.GetSpellDescriptor(id, LearnedSpellLevel(pid, id));
                        if (ledger.IsRechargeActive(id, turnNo, c.Cooldown, desc890?.MinCastInterval ?? 0, desc890?.InitialCooldown ?? 0)) continue;
                        int remaining = Math.Min(c.PerTurn - used, profile.MaxCastsPerTurn - castsThisTurn);
                        if (damage.MaxPerTurn > 0) remaining = Math.Min(remaining, damage.MaxPerTurn + turnMods.ExtraCastsPerTurn(id) - used);
                        int cap = damage.MaxPerTarget;
                        if (c.PerTarget > 0) cap = cap > 0 ? Math.Min(cap, c.PerTarget) : c.PerTarget;
                        if (cap > 0) remaining = Math.Min(remaining, cap + turnMods.ExtraCastsPerTarget(id) - ledger.GetTargetCastsThisTurn(id, target.Actor));
                        double score = SpellChoicePlanner.HitCountScore(s.Ap, ap, remaining);
                        if (score <= bestScore || here < 0 || !CastCellOk(pid, id, target.Cell, true)) continue;
                        var (lo, hi) = PlacementRange(c, s.MinRange, Math.Max(s.MinRange, s.Range + (s.Rangeable ? myRange : 0)));
                        var occupied = snapshot.Where(f => f.Cell != here && f.Cell != target.Cell).Select(f => f.Cell).ToHashSet();
                        if (!Combat.CastLegality.CanCastGeometry(here, target.Cell, lo, hi, s.CastInLine, s.CastInDiagonal, s.CastTestLos, BlocksLos, occupied.Contains)
                            || !ZoneOk(id, here, target.Cell)) continue;
                        best = r; bestScore = score;
                    }
                    if (best is null)
                    {
                        TransportLog.Line($"[combat] coffre : cible {target.Actor} cellule {target.Cell}, aucun sort mono-cible autorise depuis {here} (PA{ap})");
                        break;
                    }
                    int spellId = best.Cfg.SpellId;
                    TransportLog.Line($"[combat] coffre : invocation sur pattes {target.Actor} cellule {target.Cell}, sort {spellId}");
                    if (!Cast(target.Cell, spellId, out long requestAt)) break;
                    ap -= best.Data!.Value.Ap; castsThisTurn++; MarkCast(spellId, target.Actor);
                    ledger.RecordCast(spellId, target.Actor, turnNo);
                    await WaitAfterCast(requestAt).ConfigureAwait(false);
                    // Next iteration reads appearance and positions again. Missing/partial swaps end the turn,
                    // never trigger a speculative hit on the chest or on the guard now occupying the old cell.
                }
                await HumanEndPause().ConfigureAwait(false);
                if (epoch == _turnEpoch.GetValueOrDefault(pid) && _myTurnOpen.GetValueOrDefault(pid)) SendEndTurn(pid);
                return;
            }

            // IA DU CHOIX DE SORT (mode automatique « tous mes sorts appris » : le joueur délègue l'ordre au moteur). Les sorts OFFENSIFS
            // sont classés selon la situation de CE tour : dégâts d'un lancer avec MES caractéristiques × ennemis qu'il touche (centre de
            // zone autorisé, alliés et défis respectés) + bonus par cible qu'il suffit à achever, le tout par PA. Un sort de zone qui touche
            // deux ennemis passe devant un mono-cible de mêmes dégâts. Buffs, soins et invocations gardent leur place ; une rotation réglée
            // à la main n'est JAMAIS réordonnée (c'est la stratégie du joueur).
            if (profile.AutoSpells && scriptDriven && rotation.Count > 1 && _fighters.TryGetValue(pid, out var aiSnap) && SelfCell() is int aiCell and >= 0)
            {
                var foes = aiSnap.Where(f => IsEnemy(pid, f.ActorId)).ToList();
                if (foes.Count > 0)
                {
                    var aiScores = new Dictionary<int, (double Score, double Avg, int Hits, int Kills)>();
                    bool AiOffensive(RotationSpell r) => !r.Cfg.OnMe && !r.Cfg.SelfBuff && !r.Cfg.IsSummon && r.Cfg.TargetKind != SpellTargetKind.Ally
                        && r.Data is not null && _gameNames.GetSpellDamage(r.Cfg.SpellId, LearnedSpellLevel(pid, r.Cfg.SpellId)) is { Max: > 0 };
                    double AiValue(RotationSpell r)
                    {
                        int id = r.Cfg.SpellId, lvl = LearnedSpellLevel(pid, id);
                        var dd = _gameNames.GetSpellDamage(id, lvl)!.Value;
                        double avg = myCharsMap.Count > 0
                            ? GivreClone.App.Services.Combat.DamageEstimator.Estimate(dd.Min, dd.Max, GivreClone.App.Services.Combat.DamageEstimator.ElementOf(dd.Element), myCharsMap).Average
                            : dd.Avg;
                        var zone = _gameNames.SpellZone(id, lvl);
                        int hits = 0; HashSet<int>? bestArea = null; int bestCell = -1;
                        foreach (var e in foes)
                        {
                            if (!EnemyZoneOk(id, aiCell, e.Cell, r.Cfg.MinAoeHits)) continue;
                            HashSet<int>? area = null; int h = 1;
                            if (zone is { Size: > 0 } z)
                            {
                                area = CombatZones.GetCells(z.Shape, e.Cell, CombatZones.OrientationTo(aiCell, e.Cell), z.MinSize, z.Size).ToHashSet();
                                h = foes.Count(f => area.Contains(f.Cell));
                            }
                            if (h > hits) { hits = h; bestArea = area; bestCell = e.Cell; }
                        }
                        int kills = 0;
                        if (hits > 0)
                            foreach (var f in foes)
                                if ((bestArea is null ? f.Cell == bestCell : bestArea.Contains(f.Cell)) && FighterAbsHp(pid, f.ActorId) is int hp && hp > 0 && hp <= avg * 0.9) kills++;
                        double score = SpellChoicePlanner.Score(new SpellChoicePlanner.Candidate(r.Data!.Value.Ap, avg, hits, kills));
                        aiScores[id] = (score, avg, hits, kills);
                        return score;
                    }
                    var reordered = SpellChoicePlanner.ReorderOffensive(rotation, AiOffensive, AiValue);
                    if (!reordered.SequenceEqual(rotation))
                    {
                        rotation.Clear(); rotation.AddRange(reordered);
                        TransportLog.Line("[combat] IA : sorts offensifs classés selon la situation (dégâts × ennemis touchés ÷ PA) : "
                            + string.Join(" > ", rotation.Where(AiOffensive).Take(6).Select(r => aiScores.TryGetValue(r.Cfg.SpellId, out var v)
                                ? $"{r.Cfg.SpellId}({v.Avg:0}×{v.Hits}{(v.Kills > 0 ? $"+{v.Kills} kill" : "")}/{r.Data!.Value.Ap}PA)" : r.Cfg.SpellId.ToString())));
                    }
                }
            }

            var challengeMoveAttempts = new CombatParity.ChallengeMoveAttempts();
            // Re-evaluate before attacks: earlier casts may have made an area lethal during this same turn.
            async Task PrepareFinishingMove(bool initial = false)
            {
                if (plan is not { Moves: true, NoMove: false }
                    || !(plan.OneMove && stepsUsed == 0 || plan.AllMove && mpStart > stepsUsed)
                    || !_fighters.TryGetValue(pid, out var current)) return;
                var enemies = current.Where(f => IsEnemy(pid, f.ActorId)).ToList();
                if (!CombatParity.FinishingMovePolicy.ShouldPrepare(initial, enemies.Count,
                    enemies.Any(f => FighterHpPercent(pid, f.ActorId) is int hp && hp <= 30), AreaCanFinishFight)) return;
                TransportLog.Line("[combat] défis Zombie/Nomade : le tour peut finir le combat -> déplacement exigé AVANT le coup de grâce");
                await ChallengeEndMove(recast: false).ConfigureAwait(false);
            }
            await PrepareFinishingMove(initial: true).ConfigureAwait(false);

            // Régression live 02/11 (Eniripsa niv 37) : les vrais sorts d'attaque portent des effets que le planificateur ne modélise pas
            // (CanPlan=false) -> il jouait tous les tours SANS frapper (293 -> 27 PV, zéro dégât). Sans AUCUN sort offensif planifiable,
            // La rotation classique garde ces attaques ; une rotation uniquement de soutien peut utiliser le planificateur.
            bool plannerCanAct = Combat.AutoSpellRotation.ShouldUsePlanner(rotation.Select(r =>
                _gameNames.GetSpellDescriptor(r.Cfg.SpellId, LearnedSpellLevel(pid, r.Cfg.SpellId))));
            if (profile.AutoSpells && !scriptDriven && !plannerCanAct)
                TransportLog.Line($"[combat-planner] turn={turnNo} ignoré : rotation non admissible dans [{string.Join(",", rotation.Select(r => r.Cfg.SpellId))}] -> rotation classique");
            if (profile.AutoSpells && !scriptDriven && plannerCanAct)
            {
                // Search observed state; batch the exact walk prefix and keep its continuation only while
                // observations match. Every cast invalidates the continuation before the next decision.
                bool logged = false;
                var plannerUsedSupport = new HashSet<(int Spell, long Actor)>();
                var plannerLearnedIds = LearnedSpells(pid).ToHashSet();
                Combat.CombatTurnPlan? remainingPlan = null;
                ImmutableArray<Combat.PlannerFighter> expectedFighters = default;
                IReadOnlyDictionary<int, int>? expectedCharacteristics = null;
                int expectedCell = -1, expectedAp = -1, expectedMp = -1, expectedObservedMp = -1;
                for (int decision = 0; decision < 32 && epoch == _turnEpoch.GetValueOrDefault(pid)
                    && _myTurnOpen.GetValueOrDefault(pid); decision++)
                {
                    ct.ThrowIfCancellationRequested();
                    var enforcedPlan = ApplyChallengeHpThreshold(pid, profile, plan);
                    if (plan is not null && enforcedPlan is null) remainingPlan = null;
                    plan = enforcedPlan;
                    await PrepareFinishingMove().ConfigureAwait(false);
                    int here = SelfCell();
                    if (!_fighters.TryGetValue(pid, out var observed) || here < 0) break;
                    long selfId = SelfActorId(pid);
                    if (!observed.Any(f => IsEnemy(pid, f.ActorId))) break;
                    if (SelfStats(pid) is { } liveStats)
                    {
                        ap = Math.Min(ap, Math.Max(0, liveStats.Pa));
                        mp = Math.Min(mp, Math.Max(0, liveStats.Pm));
                    }
                    myRange = SelfRange(pid);
                    myCharsMap = SelfCharacteristics(pid).ToImmutableDictionary()
                        .SetItems(SelfStats(pid)?.Characteristics ?? ImmutableDictionary<int, int>.Empty);
                    refreshRotation();
                    var partnerSpells = new Dictionary<long, ImmutableArray<Combat.SpellCastDescriptor>>();
                    foreach (var account in _fightId.Where(p => p.Value == CurrentFightId(pid) && IsInFight(p.Key)))
                    {
                        long actor = CharacterId(account.Key);
                        if (actor == selfId || !observed.Any(f => f.ActorId == actor) || IsEnemy(pid, actor)) continue;
                        if (!_charSpells.TryGetValue(account.Key, out var book)) continue;
                        var levels = _charSpellLevels.GetValueOrDefault(account.Key);
                        int range = _selfRange.GetValueOrDefault(account.Key);
                        partnerSpells[actor] = book.Select(id => _gameNames.GetSpellDescriptor(id, levels?.GetValueOrDefault(id, 1) ?? 1))
                            .Where(d => d is not null && Combat.CombatTurnPlanner.HasModeledEnemyDamage(d))
                            .Select(d => d! with { Range = Combat.SpellReach.Effective(d!.Range, d.MinRange, d.Rangeable, range) })
                            .ToImmutableArray();
                    }
                    var fighters = observed.Select(f =>
                    {
                        FighterStats? stats = _fighterStats.TryGetValue(pid, out var byActor) && byActor.TryGetValue(f.ActorId, out var value)
                            ? value : f.ActorId == selfId ? SelfStats(pid) : null;
                        var monster = _fightMonsters.TryGetValue(pid, out var knownMonsters)
                            ? knownMonsters.GetValueOrDefault(f.ActorId) : null;
                        // Unknown HP never earns a predicted kill or heal. Unknown enemy spell knowledge is explicitly
                        // conservative (six-cell reach, 30 life per reply), not an assertion that it cannot attack.
                        var bestiary = f.ActorId != selfId && IsEnemy(pid, f.ActorId) ? MonsterThreat(pid, f.ActorId) : null;
                        var enemyOffensive = ImmutableArray<Combat.SpellCastDescriptor>.Empty;
                        if (IsEnemy(pid, f.ActorId)
                            && _observedEnemySpellLevels.TryGetValue(pid, out var enemyBooks)
                            && enemyBooks.TryGetValue(f.ActorId, out var enemyBook))
                            enemyOffensive = enemyBook.Select(entry => GameData?.SpellGradeForLevel(entry.Key, entry.Value) is > 0 and var grade
                                    ? _gameNames.GetSpellDescriptor(entry.Key, grade) : null)
                                .Where(d => d is not null && Combat.CombatTurnPlanner.HasModeledEnemyDamage(d))
                                .Select(d => d!).ToImmutableArray();
                        int? observedHit = f.ActorId != selfId ? ObservedEnemyHit(pid, f.ActorId) : null;
                        // Still in _fighters = the server has NOT sent its death (that removes it). A 0 tracked life is then a
                        // bookkeeping drift (shield/erosion), never a corpse: keep it targetable. Live 02/11 21:25: a monster at
                        // "0" kept hitting while both bots skipped 9 turns with no target.
                        int trackedLife = stats?.Life ?? 1_000_000;
                        if (trackedLife <= 0 && f.ActorId != selfId)
                        {
                            if (_lifeDriftLogged.TryAdd((pid, f.ActorId), 0))
                                TransportLog.Line($"[combat] PV suivis de {f.ActorId} à 0 sans annonce de mort -> reste ciblable (écart de suivi des PV)");
                            trackedLife = 1;
                        }
                        return new Combat.PlannerFighter(f.ActorId, f.Cell, IsEnemy(pid, f.ActorId),
                            trackedLife, stats?.MaxLife ?? 0,
                            Shield: Math.Max(0, stats?.Characteristics.GetValueOrDefault(FightStatsReader.IdShield) ?? 0),
                            MovePoints: bestiary?.Mp ?? stats?.MaxPm ?? 3,
                            // Portée et dégâts par tour du bestiaire Dofensive (id + grade du monstre) ; le plus gros coup RÉEL
                            // observé ce combat l'emporte s'il est plus fort ; sinon l'estimation prudente (6 cases, 30 PV).
                            AttackRange: bestiary is { Reach: > 0 } known ? known.Reach : 6,
                            ExpectedDamage: Math.Max(bestiary?.TurnDamage ?? 0, observedHit ?? 0) is int expected and > 0 ? expected : 30,
                            ThreatKnown: bestiary is { AttackKnown: true } || observedHit is > 0,
                            Characteristics: stats?.Characteristics,
                            States: FighterActiveStates(pid, f.ActorId).ToImmutableHashSet(),
                            StatesKnown: false,
                            AbsentStates: FighterAbsentStates(pid, f.ActorId).ToImmutableHashSet(),
                            Visibility: _fighterVisibility.GetValueOrDefault(pid)?.Get(f.ActorId).Visibility ?? FighterVisibility.Unknown,
                            PositionCertainty: f.ActorId == selfId ? FighterPositionCertainty.Confirmed
                                : _fighterVisibility.GetValueOrDefault(pid)?.Get(f.ActorId).PositionCertainty ?? FighterPositionCertainty.Unknown,
                            MonsterTemplate: monster?.GenericId ?? 0,
                            MonsterGrade: monster?.Grade ?? 0,
                            SummonerId: _summonOwners.TryGetValue(pid, out var summonOwners) && summonOwners.TryGetValue(f.ActorId, out long summoner)
                                ? summoner : monster?.Summoner,
                            TurnAp: stats is { } apStats && apStats.Characteristics.ContainsKey(FightStatsReader.IdPa)
                                ? apStats.MaxPa : null,
                            Breed: _fighterBreeds.TryGetValue(pid, out var breeds) && breeds.TryGetValue(f.ActorId, out int observedBreed)
                                ? observedBreed : f.ActorId == CharacterId(pid) && SelfCharacterInfo(pid)?.BreedIndex is int breed
                                ? ClassIconProvider.BreedId(breed) : null,
                            OffensiveSpells: partnerSpells.TryGetValue(f.ActorId, out var partnerOffensive)
                                ? partnerOffensive : enemyOffensive);
                    }).ToImmutableArray();
                    var plannerSpells = ImmutableArray.CreateBuilder<Combat.PlannerSpell>();
                    var excluded = new List<string>();   // diagnostic : pourquoi un sort de la rotation n'entre pas au planificateur
                    var occupiedCells = observed.Select(f => f.Cell).ToHashSet();
                    // Cases portant une marque au sol observee par le serveur CE tour : interdites comme case de lancer.
                    var markedCells = MarkedCells(pid);
                    // Les portails Eliotrope sont des marques, mais viser un portail est justement le tir a travers le reseau.
                    // (La pose d'un portail sur un portail existant reste interdite par le planificateur.)
                    var portalCells = Portals(pid);
                    markedCells.ExceptWith(portalCells.Select(p => p.Cell));
                    for (int configurationIndex = 0; configurationIndex < rotation.Count; configurationIndex++)
                    {
                        var r = rotation[configurationIndex];
                        var cfg = r.Cfg;
                        int id = cfg.SpellId, level = LearnedSpellLevel(pid, id);
                        var desc = _gameNames.GetSpellDescriptor(id, level);
                        string? why = !plannerLearnedIds.Contains(id) ? "non-appris" : ledger.IsRefusedThisTurn(id) || ledger.IsRecentlyRefused(id, turnNo) ? "refusé"
                            : r.Data is null ? "sans-données" : desc is null ? $"sans-descripteur(niv{level})"
                            : !Combat.CombatTurnPlanner.IsAdmitted(desc) ? "non-planifiable(couverture-résolveur)"
                            : !SpellOk(id) ? "SpellOk" : !SelfSpellGateOk(pid, cfg, ap, turnNo, enemyCountTurn) ? "condition-lanceur" : null;
                        if (why is not null) { excluded.Add($"{id}:{why}"); continue; }
                        var data = r.Data!.Value; var descriptor = desc!;
                        int used = ledger.GetCastsThisTurn(id);
                        if (ledger.IsRechargeActive(id, turnNo, cfg.Cooldown, descriptor.MinCastInterval, descriptor.InitialCooldown)) continue;
                        int left = Math.Min(cfg.PerTurn - used, profile.MaxCastsPerTurn - castsThisTurn);
                        int configuredLeft = left;
                        if (descriptor.MaxPerTurn > 0) left = Math.Min(left, descriptor.MaxPerTurn + turnMods.ExtraCastsPerTurn(id) - used);
                        if (descriptor.MinCastInterval > 0 || plan is { OncePerTurn: true } or { OncePerFight: true })
                        {
                            left = Math.Min(left, 1 - used);
                            configuredLeft = Math.Min(configuredLeft, 1 - used);
                        }
                        if (configuredLeft <= 0) continue;
                        int targetCap = descriptor.MaxPerTarget > 0 ? descriptor.MaxPerTarget + turnMods.ExtraCastsPerTarget(id) : 0;
                        if (cfg.PerTarget > 0) targetCap = targetCap > 0 ? Math.Min(targetCap, cfg.PerTarget) : cfg.PerTarget;
                        var (lo, hi) = PlacementRange(cfg, data.MinRange, Math.Max(data.MinRange, data.Range + (data.Rangeable ? myRange : 0)));
                        descriptor = descriptor with { Ap = data.Ap, MinRange = lo, Range = hi };
                        bool configured = profile.Spells.Any(c => c.SpellId == id);
                        bool support = descriptor.Effects.All(e => e.Kind is Combat.SpellEffectKind.Heal or Combat.SpellEffectKind.Shield);
                        bool enemyDamage = Combat.CombatTurnPlanner.HasModeledEnemyDamage(descriptor);
                        var allowedTargets = observed.Where(f =>
                        {
                            if (plannerUsedSupport.Contains((id, f.ActorId))) return false;
                            if (!TargetSpellGateOk(pid, cfg, f.ActorId)) return false;
                            // Un sort offensif (qui inflige des dégâts à l'ennemi) ne cible JAMAIS le lanceur lui-même,
                            // même s'il n'est pas configuré explicitement dans le profil. Corrige le bug où l'Eniripsa
                            // se lançait son propre sort de zone dessus.
                            if (enemyDamage && f.ActorId == selfId
                                && !Combat.CombatTurnPlanner.CanHealAlliesWithoutFriendlyDamage(descriptor)) return false;
                            if (configured && (cfg.OnMe ? f.ActorId != selfId
                                : cfg.TargetKind == SpellTargetKind.Ally ? IsEnemy(pid, f.ActorId) || f.ActorId == selfId
                                : !IsEnemy(pid, f.ActorId))) return false;
                            return !IsEnemy(pid, f.ActorId) || TargetOk(f.ActorId);
                        }).Select(f => f.ActorId).ToImmutableHashSet();
                        if (cfg.TargetKind == SpellTargetKind.Adjacent) descriptor = descriptor with { MinRange = 1, Range = 1 };
                        var cellConstraints = _gameNames.SpellCastConstraints(id, level);
                        var zone = _gameNames.SpellZone(id, level);
                        plannerSpells.Add(new(descriptor, left, targetCap,
                            ledger.GetTargetCastsSnapshot(id).ToImmutableDictionary(),
                            allowedTargets, cfg.AllowApproach, cfg.MinAp, cfg.SelfHpMin, cfg.SelfHpMax,
                            cfg.TgtHpMin, cfg.TgtHpMax, support && configured ? 101 : 60,
                            ConfigurationIndex: configurationIndex, ChallengeElement: SpellElement(pid, id),
                            CastsLimit: configuredLeft,
                            AllowNewAlliedSummons: !cfg.OnMe && (!configured || cfg.TargetKind == SpellTargetKind.Ally),
                            MaxSummons: cfg.IsSummon ? cfg.MaxSummons : null,
                            RangeCap: cfg.TargetKind == SpellTargetKind.Adjacent ? 1 : cfg.CastRangeMax > 0 ? cfg.CastRangeMax : null,
                            ZonePolicy: new Combat.PlannerZonePolicy(zone?.Shape ?? 'P', zone?.Size ?? 0, zone?.MinSize ?? 0,
                                cfg.MinAoeHits, enemyDamage, observed.Where(f => IsEnemy(pid, f.ActorId)
                                    && plan is { Restricts: true } && !plan.TargetOk(f.ActorId))
                                    .Select(f => f.ActorId).ToImmutableHashSet()),
                            AllowedCells: Enumerable.Range(0, walk.Length).Where(c =>
                                IsWalk(c) && !ledger.IsCellRefused(id, c)
                                // Une case DEJA marquee (piege, glyphe) n'est refusee par le serveur (message 229
                                // « Il y a un piege sur cette case ») que si CE sort POSE une marque au sol : une
                                // attaque, un deplacement ou un tir A TRAVERS le portail d'entree restent legaux.
                                // Meme regle que CastLegality.Evaluate : seule une nature de pose CONNUE « ne pose
                                // aucune marque » (false) autorise la case ; une nature INCONNUE (null) reste refusee
                                // (prudence) et ne devient JAMAIS permissive ici (le filtre et l'execution concordent).
                                && (!markedCells.Contains(c) || descriptor.PlacesGroundMark == false)
                                && (cellConstraints is not { } constraints
                                    || (!constraints.NeedFreeCell || !occupiedCells.Contains(c))
                                    && (!constraints.NeedTakenCell || occupiedCells.Contains(c)))).ToImmutableHashSet()));
                    }
                    var board = Enumerable.Range(0, walk.Length).Select(c => new Combat.PlannerCell(c,
                        IsWalk(c) && !Blocked(c), BlocksLos(c),
                        MapPathfinder.InteractionNeighbours(c).Where(n => MapPathfinder.CellDistance(c, n) == 1
                            && StepAllowed(c, n)).ToImmutableArray())).ToImmutableArray();
                    var endCells = plan is { HasEndCell: true }
                        ? Enumerable.Range(0, walk.Length).Where(plan.EndCellOk).ToImmutableHashSet() : null;
                    int? challengeElement;
                    long challengeFocus;
                    var challengeState = ChallengeState(pid);
                    lock (challengeState)
                    {
                        challengeElement = challengeState.Element;
                        challengeFocus = challengeState.Focus;
                    }
                    var observedTriggers = ImmutableArray<Combat.DeferredSpellEffect>.Empty;
                    if (FighterActiveStates(pid, selfId).Contains(6252)
                        && _gameNames.GetSpellDescriptor(14596, LearnedSpellLevel(pid, 14596)) is { } entraide)
                        observedTriggers = entraide.Effects.Where(e => e.ActionId == 1160
                            && e.Triggers == "CPT" && e.DiceNum == 14598 && e.DiceSide == 1)
                            .Select(e => new Combat.DeferredSpellEffect(14596, selfId, here, e)).ToImmutableArray();
                    if (_distributionTriggers.TryGetValue(pid, out var distributionTracker)
                        && _teamIntentRound.TryGetValue(pid, out int observedRound))
                        foreach (var trigger in distributionTracker.Active(selfId, observedRound))
                        {
                            var marked = fighters.FirstOrDefault(f => f.Id == trigger.ActorId);
                            int sourceSpell = trigger.SourceSpellId!.Value;
                            if (sourceSpell == 21978)
                            {
                                var passive = _gameNames.GetSpellDescriptor(21978, 1);
                                if (passive is not null)
                                    observedTriggers = observedTriggers.AddRange(passive.Effects
                                        .Where(e => e.ActionId == 792 && e.Triggers == "IOFF" && e.DiceNum == 21979 && e.DiceSide == 1)
                                        .Select(e => new Combat.DeferredSpellEffect(21978, selfId, marked.Cell, e, marked.Id)));
                                continue;
                            }
                            int grade = sourceSpell == 14611 ? LearnedSpellLevel(pid, sourceSpell) : trigger.SubSpellGrade!.Value;
                            var descriptor = _gameNames.GetSpellDescriptor(sourceSpell, grade);
                            if (marked is null || descriptor is null) continue;
                            observedTriggers = observedTriggers.AddRange(descriptor.Effects
                                .Where(e => e.ActionId == 1160 && e.Triggers == "D|XD" && e.DiceNum == trigger.SubSpellId
                                    && e.DiceSide == trigger.SubSpellGrade)
                                .Select(e => new Combat.DeferredSpellEffect(sourceSpell, selfId, marked.Cell, e, marked.Id)));
                        }
                    if (_distributionTriggers.TryGetValue(pid, out var timedTracker)
                        && _teamIntentRound.TryGetValue(pid, out int timedRound))
                        foreach (var timed in timedTracker.ActiveTimed(selfId, timedRound))
                        {
                            var marked = fighters.FirstOrDefault(f => f.Id == timed.ActorId);
                            var descriptor = _gameNames.GetSpellDescriptor(timed.SourceSpellId!.Value, timed.SourceGrade!.Value);
                            if (marked is null || descriptor is null) continue;
                            // Use the catalogue's ordinary expectation; raw rolled values do not establish critical/portal context.
                            observedTriggers = observedTriggers.AddRange(descriptor.Effects
                                .Where(e => e.ActionId == timed.ActionId && e.Triggers == timed.Trigger)
                                .Select(e => new Combat.DeferredSpellEffect(descriptor.SpellId, selfId, marked.Cell, e, marked.Id)));
                        }
                    var summonRanks = plannerSpells.SelectMany(s => s.Cast.Effects)
                        .Where(e => e.ActionId is 405 or 181 or 1008).Select(e => (e.DiceNum, e.DiceSide))
                        .Concat(fighters.Where(f => f.SummonerId == selfId && f.MonsterTemplate > 0)
                            .Select(f => (DiceNum: f.MonsterTemplate, DiceSide: f.MonsterGrade))).Distinct()
                        .ToDictionary(key => key, key => GameData?.SummonGradeStats(key.DiceNum, key.DiceSide, CharacterLevel(pid)));
                    var snapshot = new Combat.CombatTurnSnapshot(board, fighters, selfId, ap, MoveBudget(),
                        plannerSpells.ToImmutable(), myCharsMap.ToImmutableDictionary(), profile.MovementPolicy, profile.Style,
                        Math.Max(0, profile.MaxCastsPerTurn - castsThisTurn), endCells,
                        MobilityAllowed: plan is not { NoMove: true } && plan is not { OneMove: true } && plan is not { AllMove: true },
                        FleeBelowHpPercent: profile.FleeBelowHpPercent,
                        SpellLookup: _gameNames.GetSpellDescriptor, CasterLevel: CharacterLevel(pid), TeamView: ReadTeamView(pid),
                        SingleElement: plan is { ElementLock: true }, LockedElement: challengeElement,
                        FocusTargets: plan?.Rules.Contains(ChallengeRule.Focus) == true, FocusTarget: challengeFocus,
                        StateDamageRules: fighters.SelectMany(f => f.States ?? ImmutableHashSet<int>.Empty)
                            .Distinct().Select(state => (State: state, Rule: GameData?.SpellStateDamage(state)))
                            .Where(entry => entry.Rule is not null)
                            .ToImmutableDictionary(entry => entry.State, entry => entry.Rule!),
                        UnsafeForcedMovementCells: ForcedMovementUnsafeCells(pid),
                        Portals: Portals(pid, includeInactive: true).ToImmutableArray(),
                        EmptyStatBuffInventory: HasEmptyStatBuffInventory(pid), ObservedDeferredEffects: observedTriggers,
                        SummonLookup: (template, grade, _) => summonRanks.GetValueOrDefault((template, grade)),
                        AbsentStatBuffSources: plannerSpells.SelectMany(s => s.Cast.Effects)
                            .Where(e => e.ActionId == 406).Select(e => e.Value != 0 ? e.Value : e.DiceNum)
                            .Where(source => IsStatBuffSourceAbsent(pid, source)).ToImmutableHashSet());
                    bool reuse = remainingPlan is not null && !remainingPlan.Actions.IsEmpty
                        && !remainingPlan.Actions.Any(a => _gameNames.GetSpellDescriptor(a.SpellId, LearnedSpellLevel(pid, a.SpellId))
                            ?.Effects.Any(e => e.ActionId == 406) == true)
                        && here == expectedCell && ap == expectedAp && MoveBudget() == expectedMp
                        && expectedCharacteristics is not null
                        && CombatParity.WalkContinuationCharacteristics.Match(expectedCharacteristics, myCharsMap, expectedObservedMp)
                        && fighters.Length == expectedFighters.Length
                        && fighters.Where((f, i) => f.Id != expectedFighters[i].Id
                            || f.Cell != (f.Id == selfId ? here : expectedFighters[i].Cell)
                            || f.Life != expectedFighters[i].Life || f.Shield != expectedFighters[i].Shield
                            || f.Breed != expectedFighters[i].Breed
                             || !f.HasSameStateKnowledge(expectedFighters[i])
                             || f.TurnAp != expectedFighters[i].TurnAp
                            || f.Visibility != expectedFighters[i].Visibility || f.PositionCertainty != expectedFighters[i].PositionCertainty
                            || (f.Id == selfId
                                ? !CombatParity.WalkContinuationCharacteristics.Match(
                                    expectedFighters[i].Characteristics ?? ImmutableDictionary<int, int>.Empty,
                                    f.Characteristics ?? ImmutableDictionary<int, int>.Empty, expectedObservedMp)
                                : !(f.Characteristics ?? ImmutableDictionary<int, int>.Empty).OrderBy(p => p.Key)
                                    .SequenceEqual((expectedFighters[i].Characteristics ?? ImmutableDictionary<int, int>.Empty).OrderBy(p => p.Key)))).Any() == false;
                    var planWatch = System.Diagnostics.Stopwatch.StartNew();
                    var chosen = reuse ? remainingPlan! : Combat.CombatTurnPlanner.Choose(snapshot, maxWork: 60_000);
                    planWatch.Stop();
                    if (chosen.Intent is { } intent)
                        PublishTeamIntent(pid, intent with { Round = teamRound, Timestamp = Environment.TickCount64 });
                    if (!reuse)
                    {
                        TransportLog.Line($"[combat-planner] turn={turnNo} [{string.Join(" -> ", chosen.Actions)}] {chosen.Score}"
                            + $" plan_ms={planWatch.Elapsed.TotalMilliseconds:F1} work={chosen.Work} bounded={chosen.BudgetExhausted} enemy-reach=estimated shield-pools=unknown tackle=unknown"
                            + $" spells={plannerSpells.Count}/{rotation.Count} unmodeled={chosen.Unmodeled} team={snapshot.TeamView?.Intents.Count ?? 0}"
                            + (snapshot.Portals.IsDefaultOrEmpty ? "" : $" portails={snapshot.Portals.Length}")
                            + (excluded.Count > 0 ? $" exclus=[{string.Join(" ", excluded)}]" : ""));
                        logged = true;
                    }
                    if (chosen.Actions.IsEmpty)
                    {
                        // Le dump complet (~56 Ko de JSON serialise DANS le chemin critique du tour) ne sert qu'au tour
                        // REELLEMENT inactif. Un plan vide apres au moins une action est la fin de tour normale : il
                        // representait 8,5 Mo, soit 57 % du log du 09/11, et serialisait a chaque fin de tour.
                        if (decision == 0 && !_actedThisTurn.GetValueOrDefault(pid))
                        {
                            string diagnosticTag = controlled is not null ? "combat-companion-empty" : "combat-player-empty";
                            bool dumpAll = AppConfig.Current.PacketDumpAll;
                            bool firstDumpForTag = false;
                            if (!dumpAll)
                            {
                                var dumpedTags = _emptyDumpedTagsThisFight.GetOrAdd(pid, static _ => new HashSet<string>());
                                lock (dumpedTags)
                                {
                                    firstDumpForTag = dumpedTags.Add(diagnosticTag);
                                }
                            }
                            if (dumpAll || firstDumpForTag)
                            {
                                string diagnostic = System.Text.Json.JsonSerializer.Serialize(new
                                {
                                    Turn = turnNo, snapshot.SelfId, snapshot.Ap, snapshot.Mp,
                                    snapshot.Characteristics, snapshot.Fighters, snapshot.Spells, snapshot.Cells,
                                    snapshot.SingleElement, snapshot.LockedElement, snapshot.FocusTargets, snapshot.FocusTarget,
                                    snapshot.MovementPolicy, snapshot.Style, snapshot.MaxCasts,
                                    snapshot.EscapeCosts, snapshot.TackleImmune, snapshot.ForcedMovementSafe,
                                     snapshot.MobilityAllowed, snapshot.CasterLevel,
                                     snapshot.EndCells, snapshot.ObservedDeferredEffects,
                                     Team = snapshot.TeamView, chosen.Score, chosen.Work,
                                     chosen.BudgetExhausted
                                });
                                for (int offset = 0; offset < diagnostic.Length; offset += 3500)
                                    TransportLog.Line($"[{diagnosticTag}] pid={pid} turn={turnNo} offset={offset} "
                                        + diagnostic.Substring(offset, Math.Min(3500, diagnostic.Length - offset)));
                            }
                        }
                        break;
                    }
                    var action = chosen.Actions[0];
                    if (action.Kind == Combat.PlannerActionKind.Move)
                    {
                        var prefix = chosen.Actions.TakeWhile(a => a.Kind == Combat.PlannerActionKind.Move).ToArray();
                        int portalEntry = Array.FindIndex(prefix, a => portalCells.Any(p => p.Cell == a.Cell));
                        if (portalEntry >= 0) prefix = prefix.Take(portalEntry + 1).ToArray();
                        var path = new[] { here }.Concat(prefix.Select(a => a.Cell)).ToArray();
                        if (MoveBudget() <= 0) break;
                        int expectedRemainingMp = Math.Max(0, MoveBudget() - prefix.Sum(a => a.Mp));
                        if (!await TryMove(here, path[^1], path).ConfigureAwait(false))
                        {
                            remainingPlan = null;
                            if (SelfCell() != here) continue; // Shortened walk or displacement: replan from observations.
                            break;
                        }
                        mp = Math.Max(0, mp - prefix.Sum(a => a.Mp));
                        remainingPlan = chosen with { Actions = chosen.Actions.RemoveRange(0, prefix.Length) };
                        expectedFighters = fighters; expectedCell = path[^1]; expectedAp = ap; expectedMp = expectedRemainingMp;
                        expectedObservedMp = Math.Max(0,
                            myCharsMap.GetValueOrDefault(FightStatsReader.IdPm, expectedRemainingMp + prefix.Sum(a => a.Mp))
                            - prefix.Sum(a => a.Mp));
                        expectedCharacteristics = myCharsMap;
                        continue;
                    }
                    remainingPlan = null; // Casts always reconcile the real board before another search.
                    var selected = rotation[action.ConfigurationIndex];
                    var spell = plannerSpells.FirstOrDefault(s => s.ConfigurationIndex == action.ConfigurationIndex);
                    if (spell is null || !spell.AllowedCells!.Contains(action.Cell)) continue;
                    // Recheck authoritative execution gates, including challenge changes from the preceding cast.
                    bool occupied = observed.Any(f => f.Cell == action.Cell);
                    bool teleport = spell.Cast.IsTeleport;
                    int zoneOrigin = here, zoneCell = action.Cell;
                    if (action.ViaPortal)
                    {
                        if (!portalCells.Any(p => p.Cell == action.Cell)) break;
                        var projection = Combat.ClassMechanics.ProjectThroughPortals(here, action.Cell,
                            portalCells.OrderBy(p => p.Id).Select(p => p.Cell).ToArray());
                        if (projection is not { } shot || action.Target != 0 && !observed.Any(f => f.ActorId == action.Target && f.Cell == shot.Impact)
                            || spell.Cast.TestLos && !Combat.CastLegality.CanCastGeometry(shot.Exit, shot.Impact, 0, 560,
                                false, false, true, BlocksLos,
                                c => c != shot.Exit && c != shot.Impact && observed.Any(f => f.Cell == c))) break;
                        zoneOrigin = shot.Exit;
                        zoneCell = shot.Impact;
                    }
                    if (epoch != _turnEpoch.GetValueOrDefault(pid) || castsThisTurn >= profile.MaxCastsPerTurn
                        || spell.Cast.Effects.Any(e => (e.Kind == Combat.SpellEffectKind.Summon || e.ActionId == 405)
                            && (snapshot.SummonLookup?.Invoke(e.DiceNum != 0 ? e.DiceNum : e.Min,
                                e.DiceSide, snapshot.CasterLevel)?.SummonCost ?? 1) > 0)
                            && CountMySummons(pid) >= Combat.CombatTurnPlanner.SummonCapacity(
                                SelfCharacteristics(pid).ToImmutableDictionary()
                                    .SetItems(SelfStats(pid)?.Characteristics ?? ImmutableDictionary<int, int>.Empty), spell.MaxSummons)
                        || teleport && (!PolicyMoveOk(here, action.Cell) || !snapshot.MobilityAllowed)
                        || !ZoneOk(action.SpellId, zoneOrigin, zoneCell)
                        || Combat.CombatTurnPlanner.HasModeledEnemyDamage(spell.Cast)
                            && !(action.Target != 0 && !IsEnemy(pid, action.Target)
                                && Combat.CombatTurnPlanner.CanHealAlliesWithoutFriendlyDamage(spell.Cast))
                            && !EnemyZoneOk(action.SpellId, zoneOrigin, zoneCell, selected.Cfg.MinAoeHits)
                        || action.Target != 0 && IsEnemy(pid, action.Target) && (!TargetOk(action.Target)
                            || !EnemyZoneOk(action.SpellId, zoneOrigin, zoneCell, selected.Cfg.MinAoeHits))) break;
                    var planConstraints = _gameNames.SpellCastConstraints(action.SpellId, LearnedSpellLevel(pid, action.SpellId));
                    var planLegality = Combat.CastLegality.CanCast(new Combat.CastLegalityContext
                    {
                        FromCell = here,
                        TargetCell = action.Cell,
                        TargetActorId = action.Target,
                        SpellId = action.SpellId,
                        ApCost = spell.Cast.Ap,
                        MinRange = spell.Cast.MinRange,
                        MaxRange = spell.Cast.Range,
                        CastInLine = spell.Cast.CastInLine,
                        CastInDiagonal = spell.Cast.CastInDiagonal,
                        CastTestLos = spell.Cast.TestLos,
                        NeedFreeCell = planConstraints?.NeedFreeCell ?? false,
                        NeedTakenCell = planConstraints?.NeedTakenCell ?? false,
                        MaxPerTurn = 0,
                        MaxPerTarget = action.Target != 0 ? spell.TargetCap + turnMods.ExtraCastsPerTarget(action.SpellId) : 0,
                        CastsThisTurn = castsThisTurn,
                        CastsOnTarget = action.Target != 0 ? ledger.GetTargetCastsThisTurn(action.SpellId, action.Target) : 0,
                        Ap = ap,
                        TurnNo = turnNo,
                        ChallengeOk = SpellOk(action.SpellId),
                        SelfHpOk = SelfSpellGateOk(pid, selected.Cfg, ap, turnNo, enemyCountTurn),
                        TargetHpOk = action.Target == 0 || TargetSpellGateOk(pid, selected.Cfg, action.Target),
                        TargetOccupied = occupied,
                        IsTargetMarked = MarkedCells(pid).Contains(action.Cell),
                        // Le sort POSE-t-il une marque au sol ? (fait du descripteur client : vrai / faux / INCONNU)
                        // Sans ce fait, la regle « case marquee » resterait prudente pour TOUT sort et le tir
                        // A TRAVERS le portail d'entree (case marquee) serait refuse a l'execution.
                        SpellPlacesGroundMark = spell.Cast.PlacesGroundMark,
                        BlocksLos = BlocksLos,
                        Occupied = c => c != here && c != action.Cell && observed.Any(f => f.Cell == c),
                        Ledger = ledger,
                    });
                    if (!planLegality) break;
                    TransportLog.Line($"[combat-planner] execute spell={action.SpellId} from={here} cell={action.Cell} target={action.Target}"
                        + (action.ViaPortal ? " via-portail" : ""));
                    if (!Cast(action.Cell, action.SpellId, out long requestAt)) break;
                    await WaitAfterCast(requestAt).ConfigureAwait(false);
                    if (TryTurboCastRejectionWithMessage(pid, requestAt, out int refusedAp, out int messageId))
                    {
                        ledger.RecordRefusal(action.SpellId, action.Cell, messageId, turnNo);
                        if (refusedAp >= 0) ap = refusedAp;
                        continue;
                    }
                    if (requestAt > 0 && !TurboSequenceStartedSince(pid, requestAt))
                    {
                        ledger.RecordRefusal(action.SpellId, action.Cell, 0, turnNo);
                        continue; // No acknowledged execution: never spend AP or repeat the silent request.
                    }
                    ap -= action.Ap; castsThisTurn++;
                    // A learned AP/MP gain must stay spendable: the next decision still takes min(local, observed).
                    var regained = Combat.CombatTurnPlanner.ExpectedSelfPointGain(snapshot, spell.Cast, action.Cell, action.ViaPortal);
                    ap += regained.Ap; mp += regained.Mp;
                    gainedMp += regained.Mp;
                    MarkSpellCast(action.SpellId);
                    if (action.Target != 0)
                    {
                        _lastEngineHit[pid] = (action.Target, Environment.TickCount64);
                    }
                    ledger.RecordCast(action.SpellId, action.Target, turnNo);
                    bool damage = spell.Cast.Effects.Any(e => e.Kind == Combat.SpellEffectKind.Damage);
                    if (damage && action.Target != 0) MarkEnemyConfirmed(pid, action.Target);
                    else if (!teleport) plannerUsedSupport.Add((action.SpellId, action.Target));
                    if (teleport && SelfCell() != action.Cell)
                        ledger.RecordRefusal(action.SpellId, action.Cell, 0, turnNo);
                    else
                    {
                        // Final publication must retain acknowledged outcomes, not the unexecuted beam continuation.
                        foreach (var fighter in fighters)
                        {
                            int? observedLife = _fighterStats.TryGetValue(pid, out var lifeStats)
                                && lifeStats.TryGetValue(fighter.Id, out var lifeFighter) ? lifeFighter.Life : null;
                            if (observedLife is not int life) continue;
                            if (fighter.Enemy && life < fighter.Life)
                                teamDamage[fighter.Id] = teamDamage.GetValueOrDefault(fighter.Id) + fighter.Life - life;
                            else if (!fighter.Enemy)
                            {
                                int shield = _fighterStats.TryGetValue(pid, out var currentStats)
                                    && currentStats.TryGetValue(fighter.Id, out var currentFighter)
                                    ? currentFighter.Characteristics.GetValueOrDefault(FightStatsReader.IdShield) : fighter.Shield;
                                if (life > fighter.Life || shield > fighter.Shield)
                                    teamSupport.Add(new(fighter.Id, Heal: Math.Max(0, life - fighter.Life), Shield: Math.Max(0, shield - fighter.Shield),
                                        LifeBefore: fighter.Life, ShieldBefore: fighter.Shield));
                            }
                        }
                    }
                }
                if (!logged) TransportLog.Line($"[combat-planner] turn={turnNo} [] no-live-target");
                // Le planificateur n'a RIEN lancé de tout le tour alors qu'il reste des PA : la rotation classique prend le relais
                // (live 06/11 : Shadowbot 10 PA, deux tours sans un seul sort, puis mort). Elle garde tous ses propres garde-fous.
                // Sous le seuil de fuite, un plan sans sort est un choix de survie : la rotation classique le defaisait en
                // revenant au contact (Shadowbot, 10/11 : 13 % de PV, retour au corps a corps, mort au tour suivant).
                bool fleeingNow = profile.FleeBelowHpPercent > 0 && SelfHpPercent(pid) is int hpNow && hpNow < profile.FleeBelowHpPercent;
                if (castsThisTurn == 0 && ap > 0 && !fleeingNow && epoch == _turnEpoch.GetValueOrDefault(pid) && _myTurnOpen.GetValueOrDefault(pid))
                {
                    TransportLog.Line($"[combat-planner] turn={turnNo} aucun sort lancé avec {ap} PA -> rotation classique");
                    goto ClassicRotation;
                }
                // Only mandatory challenge movement may supersede the chosen end cell, never the old style/fallback.
                if (plan is { NoMove: true } || plan is { OneMove: true } && stepsUsed == 0
                    || plan is { AllMove: true } && MoveBudget() > 0
                    || plan is { HasEndCell: true } && !plan.EndCellOk(SelfCell()))
                    await ChallengeEndMove(recast: false).ConfigureAwait(false);
                await HumanEndPause().ConfigureAwait(false);
                if (epoch == _turnEpoch.GetValueOrDefault(pid) && _myTurnOpen.GetValueOrDefault(pid))
                {
                    SendEndTurn(pid);
                }
                return;
            }

            ClassicRotation:
            bool budgetHit = false;
            foreach (var rotationEntry in rotation)
            {
                if (budgetHit) break;
                RefreshRotationOnServerModifierChange();
                var cfg = rotationEntry.Cfg;
                if (rotationEntry.Data is not { } s) continue;
                var caps = _gameNames.GetSpellDamage(cfg.SpellId, LearnedSpellLevel(pid, cfg.SpellId));
                int perTargetCap = caps?.MaxPerTarget ?? 0, perTurnCap = caps?.MaxPerTurn ?? 0;   // plafonds de CE sort (0 = illimité)
                if (cfg.PerTarget > 0) perTargetCap = perTargetCap > 0 ? Math.Min(perTargetCap, cfg.PerTarget) : cfg.PerTarget;   // réglage « × / cible » du joueur
                // Temps de relance: ce sort a été lancé il y a
                // <= Cooldown tours -> on le saute pour CE tour (le nb de lancers/tour reste géré par PerTurn).
                var descOnMe = _gameNames.GetSpellDescriptor(cfg.SpellId, LearnedSpellLevel(pid, cfg.SpellId));
                // Ground targeting and trigger-only setup need a tactical benefit,
                // not an unconditional self-buff cast after an empty plan.
                if (descOnMe is not null && Combat.AutoSpellRotation.RequiresTacticalPlanning(descOnMe)) continue;
                if (ledger.IsRechargeActive(cfg.SpellId, turnNo, cfg.Cooldown, descOnMe?.MinCastInterval ?? 0, descOnMe?.InitialCooldown ?? 0)) continue;
                // Le serveur a refusé ce sort à ce tour ou au précédent (« Impossible de lancer ce sort actuellement »,
                // message 175) : son vrai temps de relance n'est pas celui que nous connaissons. Le relancer coûtait un
                // aller-retour serveur de ~42 ms et perdait le sort — 147 refus mesurés le 09/11 sur les 4 comptes.
                if (ledger.IsRecentlyRefused(cfg.SpellId, turnNo))
                {
                    TransportLog.Line($"[combat] sort {cfg.SpellId} sauté : refusé par le serveur au tour précédent (message 175)");
                    continue;
                }
                // Conditions SELF/GLOBAL du sort (PA mini / n° de tour / nb d'ennemis / PV% du lanceur). 0 = désactivé.
                if (!SelfSpellGateOk(pid, cfg, ap, turnNo, enemyCountTurn))
                {
                    TransportLog.Line($"[combat] sort {cfg.SpellId} sauté : conditions non remplies (PA{ap} tour{turnNo} ennemis{enemyCountTurn} PV{SelfHpPercent(pid)?.ToString() ?? "?"}%)");
                    continue;
                }
                for (int i = 0; i < cfg.PerTurn; i++)
                {
                    // A buff can be observed between two casts of THIS spell.
                    // Do not keep the old deconstructed range/AP across await.
                    RefreshRotationOnServerModifierChange();
                    if (rotationEntry.Data is not { } liveData) break;
                    s = liveData;
                    if (castsThisTurn >= profile.MaxCastsPerTurn) { budgetHit = true; break; }
                    if (!SpellOk(cfg.SpellId)) break;   // défis Versatile / Économe / Élémentaire
                    if (!SpellTurnCapOk(cfg.SpellId, perTurnCap)) break;   // maxCastPerTurn (donnée du sort) atteint -> sort suivant
                    if (ap < s.Ap) break;                       // plus assez de PA pour ce sort
                    await PrepareFinishingMove().ConfigureAwait(false);
                    if (!_fighters.TryGetValue(pid, out var fs)) { budgetHit = true; break; }
                    int myCell = SelfCell();
                    if (myCell < 0) { budgetHit = true; break; }

                    // sort sur soi (buff/soin/zone centrée sur moi) : aucun ciblage, mais la portée MINIMALE du sort
                    // s'applique quand même. Un sort de portée min 1 visé sur sa propre case (distance 0) est refusé par
                    // le serveur : 149 refus « Vous avez une portée de 1 à 1 et vous visez à 0 ! » (message 171), soit un
                    // sort gâché et ~44 ms perdus à chaque essai, sur Archerbot (32464) et Sylvanbot.
                    if (cfg.OnMe)
                    {
                        var selfLegality = Combat.CastLegality.CanCast(new Combat.CastLegalityContext
                        {
                            FromCell = myCell,
                            TargetCell = myCell,
                            TargetActorId = SelfActorId(pid),
                            SpellId = cfg.SpellId,
                            ApCost = s.Ap,
                            MinRange = s.MinRange,
                            MaxRange = s.Range,
                            CastInLine = s.CastInLine,
                            CastInDiagonal = s.CastInDiagonal,
                            CastTestLos = s.CastTestLos,
                            NeedFreeCell = false,
                            NeedTakenCell = false,
                            MaxPerTurn = perTurnCap > 0 ? perTurnCap + turnMods.ExtraCastsPerTurn(cfg.SpellId) : 0,
                            MaxPerTarget = perTargetCap > 0 ? perTargetCap + turnMods.ExtraCastsPerTarget(cfg.SpellId) : 0,
                            CastsThisTurn = ledger.GetCastsThisTurn(cfg.SpellId),
                            CastsOnTarget = ledger.GetTargetCastsThisTurn(cfg.SpellId, SelfActorId(pid)),
                            Ap = ap,
                            TurnNo = turnNo,
                            Cooldown = cfg.Cooldown,
                            MinCastInterval = descOnMe?.MinCastInterval ?? 0,
                            InitialCooldown = descOnMe?.InitialCooldown ?? 0,
                            ChallengeOk = SpellOk(cfg.SpellId),
                            SelfHpOk = SelfSpellGateOk(pid, cfg, ap, turnNo, enemyCountTurn),
                            TargetHpOk = true,
                            TargetOccupied = true,
                            IsTargetMarked = false,
                            BlocksLos = BlocksLos,
                            Occupied = _ => false,
                            Ledger = ledger,
                        });
                        if (!selfLegality)
                        {
                            if (s.MinRange > 0)
                                TransportLog.Line($"[combat] sort sur MOI {cfg.SpellId} NON lancé : portée minimale {s.MinRange} > 0, une case distante est requise");
                            break;
                        }
                        // Un offensif sur soi exige une zone connue, assez d'ennemis et aucun autre allié touché.
                        var selfDmg = _gameNames.GetSpellDamage(cfg.SpellId, LearnedSpellLevel(pid, cfg.SpellId));
                        bool offensiveSelf = !cfg.SelfBuff && selfDmg is { } sd && sd.Max > 0;
                        if (offensiveSelf)
                        {
                            int nearestE = fs.Where(f => IsEnemy(pid, f.ActorId)).Select(f => f.Cell)
                                .OrderBy(c => MapPathfinder.CellDistance(myCell, c)).DefaultIfEmpty(myCell).First();
                            var verdict = Combat.CombatSelfZoneSafety.Evaluate(
                                _gameNames.SpellZone(cfg.SpellId, LearnedSpellLevel(pid, cfg.SpellId)),
                                myCell, nearestE,
                                fs.Where(f => IsEnemy(pid, f.ActorId) && TargetOk(f.ActorId)).Select(f => f.Cell),
                                fs.Where(f => f.ActorId != SelfActorId(pid) && !IsEnemy(pid, f.ActorId)).Select(f => f.Cell),
                                cfg.MinAoeHits);
                            if (!verdict.Allowed)
                            {
                                TransportLog.Line($"[combat] sort sur MOI {cfg.SpellId} NON lancé : {verdict.Reason}");
                                break;
                            }
                        }
                        if (offensiveSelf && plan is { Restricts: true })
                        {
                            int towardE = fs.Where(f => IsEnemy(pid, f.ActorId)).Select(f => f.Cell).DefaultIfEmpty(myCell)
                                            .OrderBy(c => MapPathfinder.CellDistance(myCell, c)).First();
                            if (!ChallengeZoneOk(pid, plan, cfg.SpellId, myCell, myCell, towardE))
                            {
                                TransportLog.Line($"[combat] sort sur MOI {cfg.SpellId} NON lancé : sa zone toucherait un ennemi interdit par un défi -> sort suivant");
                                break;
                            }
                        }
                        if (!TargetCapOk(cfg.SpellId, SelfActorId(pid), perTargetCap)) break;   // maxCastPerTarget atteint sur moi -> sort suivant
                        TransportLog.Line($"[combat] sort {cfg.SpellId} sur MOI ({myCell}){(offensiveSelf ? " [zone, ennemi présent]" : " [buff]")}");
                        // Le bool de SendCast n'était pas lu : PA, castsThisTurn, cooldown et MarkEnemyConfirmed (verrou
                        // PERMANENT) étaient appliqués à des paquets JAMAIS partis, et on attendait un CastDelay complet
                        // pour rien. Un refus = fenêtre de tour fermée -> plus rien ne partira ce tour, on sort.
                        if (!Cast(myCell, cfg.SpellId, out var castRequestAt)) { budgetHit = true; break; }
                        await WaitAfterCast(castRequestAt).ConfigureAwait(false);
                        if (TryTurboCastRejectionWithMessage(pid, castRequestAt, out _, out int selfRefusedMessage))
                        {
                            ledger.RecordRefusal(cfg.SpellId, myCell, selfRefusedMessage, turnNo);
                            break;
                        }
                        ap -= s.Ap; castsThisTurn++; MarkCast(cfg.SpellId, SelfActorId(pid));
                        ledger.RecordCast(cfg.SpellId, SelfActorId(pid), turnNo);
                        continue;
                    }

                    var enemies = fs.Where(f => IsEnemy(pid, f.ActorId)).ToList();
                    if (enemies.Count == 0) { budgetHit = true; break; }
                    // (occupation relue LIVE par le WalkFight de tour — plus de snapshot figé ici)

                    // LdV : on FAIT CONFIANCE au flag castTestLos décodé du sort (m_flags bit2, GameNameResolver.SpellCombat) au
                    // lieu de la forcer en dur. Vérifié 2026-09-06 contre un bot tiers: les TROIS lisent ce même
                    // bit par-sort et SAUTENT complètement la LdV quand il est faux (nos positions de bits — CastInLine=bit0,
                    // CastTestLos=bit2 — correspondent exactement à leur SpellLevelFlags, et CastInLine bit0 est déjà fiable ici).
                    // Forcer castTestLos=true FAISAIT LE BUG « il se déplace à côté mais n'attaque PAS » : un sort sans exigence de
                    // LdV (AoE/pluie…) était quand même bloqué par notre géométrie LdV -> FindCastCell ne trouvait AUCUNE case de
                    // tir -> repli « rapprochement » -> tour passé alors que PO+PA suffisaient. CanCast saute aussi la LdV au CaC
                    //. Notre géométrie HasLineOfSight (flag los par-cellule dédié, extrémités exclues) est
                    // correcte — le seul défaut était de sur-gater la LdV. Le tir à travers un mur reste bloqué par le VRAI bit.
                    // Portée TOTALE : un sort "boostable" (rangeCanBeBoosted) gagne la caractéristique Portée du perso.
                    // Sans ça on visait à la PO de BASE -> le bot ne tirait pas d'aussi loin qu'il le peut réellement.
                    // myRange = Portée COURANTE en combat (kub pré-combat, MAJ live via jxw #19 = malus/bonus PO d'un ennemi).
                    // On applique AUSSI un malus (négatif) — un monstre qui retire de la PO doit RÉDUIRE la portée effective,
                    // sinon le bot tire hors portée (« portée 1 à 2 mais vise à 4 »). Plancher au MinRange.
                    int effRange = s.Range + (s.Rangeable ? myRange : 0);
                    if (effRange < s.MinRange) effRange = s.MinRange;
                    var (loRange, hiRange) = PlacementRange(cfg, s.MinRange, effRange);   // surcharges Placement (kite mini / plafond de portée)
                    var castS = (s.Ap, loRange, hiRange, s.CastInLine, s.CastInDiagonal, s.CastTestLos);

                    // Occupation pour la LdV VERS une cible donnée = tous les autres combattants sauf moi et CETTE cible.
                    Func<int, bool> OccFor(int targetCell)
                    {
                        var occ = new HashSet<int>(fs.Where(f => f.Cell != myCell && f.Cell != targetCell).Select(f => f.Cell));
                        return c => occ.Contains(c);
                    }

                    // SORT D'ALLIÉ (Lot 1-E) : soin/buff ciblé sur un COÉQUIPIER (ou une invoc à moi), pas un ennemi. V1 :
                    // cast D'ICI si à portée (pas de déplacement dédié) ; cible = l'allié le plus BAS en PV (soin) qui passe
                    // la condition PV cible du sort (TgtHpMin/Max -> ex « ne soigner que sous 60% »). Exclut soi (OnMe gère soi).
                    if (cfg.TargetKind == SpellTargetKind.Ally)
                    {
                        var ally = fs.Where(f => !IsEnemy(pid, f.ActorId) && f.Cell != myCell)
                            .Where(a => TargetSpellGateOk(pid, cfg, a.ActorId) && CastCellOk(pid, cfg.SpellId, a.Cell, true) && TargetCapOk(cfg.SpellId, a.ActorId, perTargetCap) && Combat.CastLegality.CanCastGeometry(myCell, a.Cell, loRange, hiRange, s.CastInLine, s.CastInDiagonal, s.CastTestLos, BlocksLos, OccFor(a.Cell)))
                            .OrderBy(a => FighterHpPercent(pid, a.ActorId) ?? 100)
                            .ThenBy(a => MapPathfinder.CellDistance(myCell, a.Cell))
                            .FirstOrDefault();
                        if (ally.ActorId == 0) break;   // aucun allié castable d'ici -> sort suivant
                        TransportLog.Line($"[combat] sort ALLIÉ {cfg.SpellId} -> allié {ally.Cell} (PV {FighterHpPercent(pid, ally.ActorId)?.ToString() ?? "?"}%)");
                        if (!Cast(ally.Cell, cfg.SpellId, out var castRequestAt)) { budgetHit = true; break; }
                        ap -= s.Ap; castsThisTurn++; MarkCast(cfg.SpellId, ally.ActorId);
                        ledger.RecordCast(cfg.SpellId, ally.ActorId, turnNo);
                        await WaitAfterCast(castRequestAt).ConfigureAwait(false);
                        continue;
                    }

                    // SORT SUR CASE ADJACENTE : lancé sur une des 4 cases collées à moi (corps-à-corps, sorts de zone autour de soi, poussées…). On prend la case qui
                    // touche le plus d'ennemis dans la zone du sort (au moins 1, ou MinAoeHits) ; sans zone : la case d'un ennemi collé.
                    if (cfg.TargetKind == SpellTargetKind.Adjacent)
                    {
                        var foesAdj = fs.Where(f => IsEnemy(pid, f.ActorId)).Select(f => f.Cell).Distinct().ToList();
                        var adjZone = _gameNames.SpellZone(cfg.SpellId, LearnedSpellLevel(pid, cfg.SpellId));
                        int bestAdj = -1, bestAdjHits = 0;
                        for (int c = 0; c < MapPathfinder.CellCount; c++)
                        {
                            if (MapPathfinder.CellDistance(myCell, c) != 1) continue;
                            bool occupiedAdj = fs.Any(f => f.Cell == c);
                            if (!occupiedAdj && !IsWalk(c)) continue;
                            if (!CastCellOk(pid, cfg.SpellId, c, occupiedAdj) || !Combat.CastLegality.CanCastGeometry(myCell, c, loRange, hiRange, s.CastInLine, s.CastInDiagonal, s.CastTestLos, BlocksLos, OccFor(c))) continue;
                            if (!EnemyZoneOk(cfg.SpellId, myCell, c, cfg.MinAoeHits)) continue;
                            int hitsAdj = adjZone is { Size: > 0 } az2
                                ? CombatZones.GetCells(az2.Shape, c, CombatZones.OrientationTo(myCell, c), az2.MinSize, az2.Size).Count(foesAdj.Contains)
                                : (foesAdj.Contains(c) ? 1 : 0);
                            if (hitsAdj > bestAdjHits) { bestAdj = c; bestAdjHits = hitsAdj; }
                        }
                        if (bestAdj < 0 || bestAdjHits < System.Math.Max(1, cfg.MinAoeHits)) break;   // rien à toucher autour de moi -> sort suivant
                        TransportLog.Line($"[combat] sort ADJACENT {cfg.SpellId} -> case {bestAdj} ({bestAdjHits} ennemi(s) touché(s))");
                        if (!Cast(bestAdj, cfg.SpellId, out var castRequestAt)) { budgetHit = true; break; }
                        ap -= s.Ap; castsThisTurn++; MarkSpellCast(cfg.SpellId);
                        ledger.RecordCast(cfg.SpellId, 0, turnNo);
                        await WaitAfterCast(castRequestAt).ConfigureAwait(false);
                        continue;
                    }

                    // INVOCATION : place une créature sur une CELLULE LIBRE à portée (pas de cible ennemie). Plafonnée à
                    // MaxSummons invocations VIVANTES à moi. Réutilise StandCellsInRange (cases marchables inoccupées, WalkFight
                    // = false pour toute case occupée) + CanCast (portée/alignement/LdV selon le sort). BuildCastFramed est déjà
                    // générique cellule -> aucun forge dédié. Placé AVANT le ciblage ennemi : donner à ce sort une priorité
                    // basse (n° petit) le fait invoquer en début de tour.
                    if (cfg.IsSummon)
                    {
                        int cap = Combat.CombatTurnPlanner.SummonCapacity(
                            SelfCharacteristics(pid).ToImmutableDictionary()
                                .SetItems(SelfStats(pid)?.Characteristics ?? ImmutableDictionary<int, int>.Empty), cfg.MaxSummons);
                        int mine = CountMySummons(pid);
                        var summonDescriptor = _gameNames.GetSpellDescriptor(cfg.SpellId, LearnedSpellLevel(pid, cfg.SpellId));
                        bool consumesCapacity = summonDescriptor is null || summonDescriptor.Effects
                            .Where(e => e.Kind == Combat.SpellEffectKind.Summon)
                            .Any(e => (GameData?.SummonGradeStats(e.DiceNum != 0 ? e.DiceNum : e.Min,
                                e.DiceSide, CharacterLevel(pid))?.SummonCost ?? 1) > 0);
                        if (consumesCapacity && mine >= cap) { TransportLog.Line($"[combat] invocation {cfg.SpellId} : plafond atteint ({mine}/{cap}) -> sort suivant"); break; }
                        int? placeCell = null;
                        foreach (var c in MapPathfinder.StandCellsInRange(myCell, hiRange, myCell, WalkFight))
                        {
                            if (loRange > 0 && MapPathfinder.CellDistance(myCell, c) < loRange) continue;
                            if (CastCellOk(pid, cfg.SpellId, c, false) && Combat.CastLegality.CanCastGeometry(myCell, c, loRange, hiRange, s.CastInLine, s.CastInDiagonal, s.CastTestLos, BlocksLos, OccFor(c))) { placeCell = c; break; }
                        }
                        if (placeCell is not int pc) { TransportLog.Line($"[combat] invocation {cfg.SpellId} : aucune cellule libre à portée -> sort suivant"); break; }
                        TransportLog.Line($"[combat] invocation {cfg.SpellId} -> cellule libre {pc} ({mine + 1}/{cap})");
                        if (!Cast(pc, cfg.SpellId, out var castRequestAt)) { budgetHit = true; break; }
                        ap -= s.Ap; castsThisTurn++; MarkSpellCast(cfg.SpellId);   // invocation = pas de cible-combattant -> plafond par-tour seul
                        ledger.RecordCast(cfg.SpellId, 0, turnNo);
                        await WaitAfterCast(castRequestAt).ConfigureAwait(false);
                        continue;
                    }

                    // Ennemis dans l'ordre de préférence (mode de ciblage). On n'attaque PAS bêtement le plus proche s'il est
                    // hors LdV : on prend le plus prioritaire qu'on peut RÉELLEMENT toucher (un monstre devant un autre ne
                    // fige plus le bot). Correspond au cas signalé : cible basse-PV derrière un monstre => on frappe le tirable.
                    // DÉFIS (Premier, Élitiste, Focus, Cruel, Dernier, Duel…) : seuls les ennemis AUTORISÉS sont visés.
                    var allowed = enemies.Where(e => TargetOk(e.ActorId)).ToList();
                    if (allowed.Count == 0) break;
                    var ordered = KillFirst(OrderTargets(profile.Target, allowed, myCell, a => FighterHpPercent(pid, a)));

                    var aoeZone = _gameNames.SpellZone(cfg.SpellId, LearnedSpellLevel(pid, cfg.SpellId));
                    if (aoeZone is { Size: > 0 } az)
                    {
                        var preference = ordered;
                        var safeTargets = ordered.Where(e => EnemyZoneOk(cfg.SpellId, myCell, e.Cell, cfg.MinAoeHits))
                            .OrderByDescending(e =>
                            {
                                var area = CombatZones.GetCells(az.Shape, e.Cell,
                                    CombatZones.OrientationTo(myCell, e.Cell), az.MinSize, az.Size).ToHashSet();
                                return fs.Count(f => IsEnemy(pid, f.ActorId) && area.Contains(f.Cell));
                            }).ThenBy(e => preference.IndexOf(e)).ToList();
                        if (safeTargets.Count > 0) ordered = safeTargets.Concat(preference.Where(e => !safeTargets.Contains(e))).ToList();
                        else
                        {
                            TransportLog.Line($"[combat] AoE {cfg.SpellId} : aucune zone sûre depuis {myCell}, recherche d'un autre placement autorisé");
                        }
                    }

                    var areaCache = new Dictionary<int, HashSet<int>>();
                    var aimConstraints = _gameNames.SpellCastConstraints(cfg.SpellId, LearnedSpellLevel(pid, cfg.SpellId));
                    int AimCell(int enemyCell)
                    {
                        // Case portant deja une marque (piege, glyphe) : refus serveur 229 « Il y a un piege sur cette case ».
                        // La rotation classique reposait le meme piege sur la meme case (Shadowbot, 10/11 : 9 refus en 12 min).
                        bool Legal(int c) => Combat.CastLegality.CanCast(new Combat.CastLegalityContext
                        {
                            FromCell = myCell,
                            TargetCell = c,
                            SpellId = cfg.SpellId,
                            ApCost = s.Ap,
                            MinRange = loRange,
                            MaxRange = hiRange,
                            CastInLine = s.CastInLine,
                            CastInDiagonal = s.CastInDiagonal,
                            CastTestLos = s.CastTestLos,
                            NeedFreeCell = aimConstraints?.NeedFreeCell ?? false,
                            NeedTakenCell = aimConstraints?.NeedTakenCell ?? false,
                            TargetOccupied = fs.Any(f => f.Cell == c),
                            IsTargetMarked = MarkedCells(pid).Contains(c),
                            // Rotation classique : meme fait que l'execution du plan (descripteur du sort de la
                            // rotation). null = metadonnees d'effets inconnues -> doute conserve, case refusee.
                            SpellPlacesGroundMark = descOnMe?.PlacesGroundMark,
                            BlocksLos = BlocksLos,
                            Occupied = OccFor(c),
                            Ledger = ledger,
                            TurnNo = turnNo,
                            ChallengeOk = SpellOk(cfg.SpellId),
                            ZoneOk = EnemyZoneOk(cfg.SpellId, myCell, c, cfg.MinAoeHits),
                        }).Ok;
                        if (aoeZone is not { Size: > 0 } zz) return Legal(enemyCell) ? enemyCell : -1;
                        IReadOnlyCollection<int> Area(int c)
                        {
                            if (!areaCache.TryGetValue(c, out var area)) areaCache[c] = area = CombatZones.GetCells(zz.Shape, c,
                                CombatZones.OrientationTo(myCell, c), zz.MinSize, zz.Size).ToHashSet();
                            return area;
                        }
                        bool Safe(int c) => (fs.Any(f => f.Cell == c) || IsWalk(c)) && Legal(c)
                            && fs.Where(f => IsEnemy(pid, f.ActorId) && Area(c).Contains(f.Cell))
                                .All(f => TargetSpellGateOk(pid, cfg, f.ActorId) && TargetCapOk(cfg.SpellId, f.ActorId, perTargetCap));
                        return AoeAimPolicy.Find(enemyCell, Enumerable.Range(0, MapPathfinder.CellCount), Safe, Area,
                            fs.Where(f => IsEnemy(pid, f.ActorId)).Select(f => f.Cell).Distinct().ToArray());
                    }

                    // A legal center can be empty or the caster's own cell, even when no enemy cell is castable.
                    int aim = -1;
                    var hitNow = ordered.FirstOrDefault(e => TargetSpellGateOk(pid, cfg, e.ActorId)
                        && TargetCapOk(cfg.SpellId, e.ActorId, perTargetCap) && (aim = AimCell(e.Cell)) >= 0);
                    if (hitNow.ActorId != 0)
                    {
                        // SORT DE ZONE (croix, cercle, ligne…) : viser la case qui touche LE PLUS d'ennemis, pas forcément la case d'un ennemi
                        // (ex. Orage en croix entre deux ennemis éloignés de 2 cases) ; mêmes garde-fous qu'avant (portée, LdV, alliés, défis).
                        if (aim != hitNow.Cell) TransportLog.Line($"[combat] zone {cfg.SpellId} : centre {hitNow.Cell} -> {aim} (centre de zone autorisé)");
                        TransportLog.Line($"[combat] sort {cfg.SpellId} -> cible {aim} (dist {MapPathfinder.CellDistance(myCell, aim)})");
                        if (!Cast(aim, cfg.SpellId, out var castRequestAt)) { budgetHit = true; break; }   // cf note site précédent
                        await WaitAfterCast(castRequestAt).ConfigureAwait(false);
                        if (TryTurboCastRejectionWithMessage(pid, castRequestAt, out _, out int rangedRefusedMessage))
                        {
                            ledger.RecordRefusal(cfg.SpellId, aim, rangedRefusedMessage, turnNo);
                            break;
                        }
                        MarkEnemyConfirmed(pid, hitNow.ActorId); ap -= s.Ap; castsThisTurn++; MarkCast(cfg.SpellId, hitNow.ActorId);
                        if (areaCache.TryGetValue(aim, out var hitArea))
                            foreach (var secondary in fs.Where(f => f.ActorId != hitNow.ActorId && IsEnemy(pid, f.ActorId) && hitArea.Contains(f.Cell)))
                            {
                                ledger.RecordTargetHit(cfg.SpellId, secondary.ActorId);
                            }
                        ledger.RecordCast(cfg.SpellId, hitNow.ActorId, turnNo);
                        continue;
                    }

                    // pas castable d'ici : le PASSIF ne se deplace jamais pour tirer -> on tente le sort suivant depuis la meme case.
                    if (profile.Style == CombatStyle.Passive) break;

                    // Placement désactivé pour CE sort, OU politique de profil « ne jamais approcher » : aucun
                    // repositionnement -> on ne tire que si déjà à portée (hitNow), sinon sort suivant depuis la même case.
                    if (!CombatMovementDecision.CanReposition(profile.MovementPolicy, profile.Style, cfg.AllowApproach)) break;

                    // TACLE / melee-first (kite 0) : si un ennemi est au CONTACT, se repositionner serait REFUSÉ (verrou) et
                    // gâcherait le tour en refus en boucle -> on NE bouge PAS pour ce sort, on garde les PA (l'ennemi adjacent
                    // reste castable sur place via hitNow/FILET). On passe au sort suivant (toujours évalué depuis la case courante).
                    if (EnemyAdjacent(myCell)) break;

                    // DÉFIS Statue (0 PM) / Zombie (1 PM au total) : budget de déplacement épuisé -> pas de repositionnement.
                    if (MoveBudget() <= 0) break;

                    // 2) sinon, se placer sur une CASE DE TIR atteignable ce tour (portée + LdV réelle) pour la cible la plus
                    //    prioritaire qu'on peut engager, puis relancer CE sort de là.
                    bool moved = false;
                    // Un sort a case LIBRE (glyphe, Nimbostratus 4244 du compagnon) ne vise jamais la case d'un ennemi : marcher vers
                    // une case de tir pour lui gachait les PM puis le FILET le refusait (« case ciblee invalide », 245 fois le 10/11).
                    foreach (var e in ordered.Where(e => TargetSpellGateOk(pid, cfg, e.ActorId) && TargetCapOk(cfg.SpellId, e.ActorId, perTargetCap)
                        && CastCellOk(pid, cfg.SpellId, e.Cell, true)))   // ne se déplace que vers une cible castable (condition PV + plafond par cible + case visable)
                    {
                        // Si une case de tir est REFUSÉE (obstacle appris), on RE-CHERCHE une AUTRE case pour le même ennemi
                        // (FindCastCell exclut déjà les cases apprises via WalkFight) au lieu d'abandonner -> on ne gâche plus
                        // le tour sur un seul essai. Borné (3) pour ne pas spammer les injections.
                        bool placed = false;
                        for (int attempt = 0; attempt < 3 && !placed; attempt++)
                        {
                            int castCell = FindCastCell(myCell, e.Cell, castS, MoveBudget(), WalkFight, BlocksLos, OccFor(e.Cell), out int steps, StepAllowed,
                                c => EnemyZoneOk(cfg.SpellId, c, e.Cell, cfg.MinAoeHits), c => CastCellTie(cfg.SpellId, s.Ap, c, e.Cell));
                            if (castCell < 0 || castCell == myCell) break;   // plus aucune case de tir pour cet ennemi
                            TransportLog.Line($"[combat] sort {cfg.SpellId} : case de tir {castCell} ({steps} PM) pour ennemi {e.ActorId}@{e.Cell}");
                            if (await TryMove(myCell, castCell)) { mp -= steps; i--; moved = true; placed = true; }  // retente CE sort de la nouvelle case
                        }
                        if (placed) break;
                    }
                    if (moved) continue;
                    break;   // pas de tir possible pour ce sort ce tour -> sort suivant (le placement final se fait en fin de tour)
                }
            }

            // Après un rapprochement de fin de tour, on est peut-être MAINTENANT à portée -> on RETENTE les sorts offensifs
            // depuis la nouvelle case avec les PA restants. Corrige le « il se rapproche à portée PUIS passe le tour » : le
            // rapprochement de fin de tour ne relançait aucun cast. (Les buffs/sorts-soi restent gérés par la boucle principale.)
            async Task RecastAfterApproach()
            {
                if (!_fighters.TryGetValue(pid, out var fnow)) return;
                int here = SelfCell();
                if (here < 0) return;
                var ens = fnow.Where(f => IsEnemy(pid, f.ActorId)).ToList();
                if (ens.Count == 0) return;
                var otherCells = fnow.Where(f => f.Cell != here).Select(f => f.Cell).ToList();
                foreach (var entry in rotation)
                {
                    RefreshRotationOnServerModifierChange();
                    var (rc, rd) = entry;
                    if (!rc.CanUseOffensiveFallback() || rd is null) continue;
                    var rs = rd.Value;
                    var descRecast = _gameNames.GetSpellDescriptor(rc.SpellId, LearnedSpellLevel(pid, rc.SpellId));
                    if (descRecast is not null && Combat.AutoSpellRotation.RequiresTacticalPlanning(descRecast)) continue;
                    if (ledger.IsRechargeActive(rc.SpellId, turnNo, rc.Cooldown, descRecast?.MinCastInterval ?? 0, descRecast?.InitialCooldown ?? 0)) continue;
                    if (!SelfSpellGateOk(pid, rc, ap, turnNo, ens.Count)) continue;   // conditions self/global aussi après rapprochement
                    var rcaps = _gameNames.GetSpellDamage(rc.SpellId, LearnedSpellLevel(pid, rc.SpellId));
                    int cap = rcaps?.MaxPerTarget ?? 0, capTurn = rcaps?.MaxPerTurn ?? 0;
                    for (int k = 0; k < rc.PerTurn; k++)
                    {
                        RefreshRotationOnServerModifierChange();
                        if (entry.Data is not { } liveRecast) break;
                        rs = liveRecast;
                        if (castsThisTurn >= profile.MaxCastsPerTurn || ap < rs.Ap) break;
                        if (!SpellOk(rc.SpellId)) break;   // défis Versatile / Économe / Élémentaire
                        if (!SpellTurnCapOk(rc.SpellId, capTurn)) break;   // maxCastPerTurn atteint -> sort suivant
                        await PrepareFinishingMove().ConfigureAwait(false);
                        if (!_fighters.TryGetValue(pid, out fnow)) return;
                        here = SelfCell(); if (here < 0) return;
                        ens = fnow.Where(f => IsEnemy(pid, f.ActorId)).ToList();
                        if (ens.Count == 0) return;
                        otherCells = fnow.Where(f => f.Cell != here).Select(f => f.Cell).ToList();
                        int er = rs.Range + (rs.Rangeable ? myRange : 0); if (er < rs.MinRange) er = rs.MinRange;
                        var (loR, hiR) = PlacementRange(rc, rs.MinRange, er);   // mêmes surcharges Placement qu'en boucle principale
                        var cS = (rs.Ap, loR, hiR, rs.CastInLine, rs.CastInDiagonal, rs.CastTestLos);   // LdV = flag décodé, pas forcé (cf castS)
                        (int Cell, long ActorId) hit = default;
                        var rCellConstraints = _gameNames.SpellCastConstraints(rc.SpellId, LearnedSpellLevel(pid, rc.SpellId));
                        foreach (var e in KillFirst(OrderTargets(profile.Target, ens.Where(x => TargetOk(x.ActorId)).ToList(), here, a => FighterHpPercent(pid, a))))
                        {
                            var occ = new HashSet<int>(otherCells.Where(cc => cc != e.Cell));
                            var legality = Combat.CastLegality.CanCast(new Combat.CastLegalityContext
                            {
                                FromCell = here,
                                TargetCell = e.Cell,
                                TargetActorId = e.ActorId,
                                SpellId = rc.SpellId,
                                ApCost = rs.Ap,
                                MinRange = loR,
                                MaxRange = hiR,
                                CastInLine = rs.CastInLine,
                                CastInDiagonal = rs.CastInDiagonal,
                                CastTestLos = rs.CastTestLos,
                                NeedFreeCell = rCellConstraints?.NeedFreeCell ?? false,
                                NeedTakenCell = rCellConstraints?.NeedTakenCell ?? false,
                                MaxPerTurn = capTurn > 0 ? capTurn + turnMods.ExtraCastsPerTurn(rc.SpellId) : 0,
                                MaxPerTarget = cap > 0 ? cap + turnMods.ExtraCastsPerTarget(rc.SpellId) : 0,
                                CastsThisTurn = ledger.GetCastsThisTurn(rc.SpellId),
                                CastsOnTarget = ledger.GetTargetCastsThisTurn(rc.SpellId, e.ActorId),
                                Ap = ap,
                                TurnNo = turnNo,
                                ChallengeOk = SpellOk(rc.SpellId),
                                TargetAllowed = TargetOk(e.ActorId),
                                SelfHpOk = SelfSpellGateOk(pid, rc, ap, turnNo, ens.Count),
                                TargetHpOk = TargetSpellGateOk(pid, rc, e.ActorId),
                                ZoneOk = EnemyZoneOk(rc.SpellId, here, e.Cell, rc.MinAoeHits),
                                TargetOccupied = true,
                                IsTargetMarked = MarkedCells(pid).Contains(e.Cell),
                                BlocksLos = BlocksLos,
                                Occupied = occ.Contains,
                                Ledger = ledger,
                            });
                            if (legality) { hit = e; break; }
                        }
                        if (hit.ActorId == 0) break;
                        TransportLog.Line($"[combat] (après rapprochement) sort {rc.SpellId} -> cible {hit.Cell} (dist {MapPathfinder.CellDistance(here, hit.Cell)})");
                        if (!Cast(hit.Cell, rc.SpellId, out var castRequestAt)) return;   // fenêtre fermée -> inutile de continuer ce tour
                        await WaitAfterCast(castRequestAt).ConfigureAwait(false);
                        if (TryTurboCastRejectionWithMessage(pid, castRequestAt, out _, out int approachRefusedMessage))
                        {
                            ledger.RecordRefusal(rc.SpellId, hit.Cell, approachRefusedMessage, turnNo);
                            break;
                        }
                        MarkEnemyConfirmed(pid, hit.ActorId); ap -= rs.Ap; castsThisTurn++; MarkCast(rc.SpellId, hit.ActorId);
                        ledger.RecordCast(rc.SpellId, hit.ActorId, turnNo);
                    }
                }
            }

            // FILET « attaque de dernier recours » : de ma case FINALE, retente un sort offensif de rotation
            // qui passe TOUS les verrous du profil. Peut récupérer un tir oublié après le mouvement, mais ne
            // contourne plus Cooldown, PerTurn, conditions du lanceur/de cible ou portée Placement.
            // Ne bouge PAS ; la validation normale PA, caps serveur, défis, LdV et tour ouvert reste obligatoire.
            async Task LastChanceAttack()
            {
                for (int guard = 0; guard < 12; guard++)   // borne dure (PA + plafond de casts bornent déjà) : anti-boucle
                {
                    if (castsThisTurn >= profile.MaxCastsPerTurn) return;
                    await PrepareFinishingMove().ConfigureAwait(false);
                    if (!_fighters.TryGetValue(pid, out var fnow)) return;
                    int here = SelfCell(); if (here < 0) return;
                    var ens = fnow.Where(f => IsEnemy(pid, f.ActorId)).ToList();
                    if (ens.Count == 0) return;
                    var others = fnow.Where(f => f.Cell != here).Select(f => f.Cell).ToList();
                    bool didCast = false;
                    foreach (var entry in rotation)
                    {
                        RefreshRotationOnServerModifierChange();
                        var (rc, rd) = entry;
                        if (!rc.CanUseOffensiveFallback() || rd is null) continue;   // sorts sur soi gérés par la boucle principale
                        var rs = rd.Value;
                        var descLast = _gameNames.GetSpellDescriptor(rc.SpellId, LearnedSpellLevel(pid, rc.SpellId));
                        if (descLast is not null && Combat.AutoSpellRotation.RequiresTacticalPlanning(descLast)) continue;
                        int effCd = Math.Max(rc.Cooldown, descLast?.MinCastInterval ?? 0);
                        if (turnNo <= (descLast?.InitialCooldown ?? 0)) continue;
                        if (!CombatParity.LastChanceCastPolicy.CanAttempt(
                            rc.PerTurn, ledger.GetCastsThisTurn(rc.SpellId),
                            ap, rs.Ap, effCd, turnNo, ledger.GetLastCastTurn(rc.SpellId))) continue;
                        if (!SelfSpellGateOk(pid, rc, ap, turnNo, ens.Count)) continue;
                        if (!SpellOk(rc.SpellId)) continue;   // défis Versatile / Économe / Élémentaire
                        var lcaps = _gameNames.GetSpellDamage(rc.SpellId, LearnedSpellLevel(pid, rc.SpellId));
                        int cap = lcaps?.MaxPerTarget ?? 0, capTurn = lcaps?.MaxPerTurn ?? 0;
                        if (!SpellTurnCapOk(rc.SpellId, capTurn)) continue;   // maxCastPerTurn atteint -> sort suivant
                        int er = rs.Range + (rs.Rangeable ? myRange : 0); if (er < rs.MinRange) er = rs.MinRange;
                        var (loR, hiR) = PlacementRange(rc, rs.MinRange, er);
                        var cS = (rs.Ap, loR, hiR, rs.CastInLine, rs.CastInDiagonal, rs.CastTestLos);
                        var lCellConstraints = _gameNames.SpellCastConstraints(rc.SpellId, LearnedSpellLevel(pid, rc.SpellId));
                        foreach (var e in KillFirst(OrderTargets(profile.Target, ens.Where(x => TargetOk(x.ActorId)).ToList(), here, a => FighterHpPercent(pid, a))))
                        {
                            var occ = new HashSet<int>(others.Where(cc => cc != e.Cell));
                            var legality = Combat.CastLegality.CanCast(new Combat.CastLegalityContext
                            {
                                FromCell = here,
                                TargetCell = e.Cell,
                                TargetActorId = e.ActorId,
                                SpellId = rc.SpellId,
                                ApCost = rs.Ap,
                                MinRange = loR,
                                MaxRange = hiR,
                                CastInLine = rs.CastInLine,
                                CastInDiagonal = rs.CastInDiagonal,
                                CastTestLos = rs.CastTestLos,
                                NeedFreeCell = lCellConstraints?.NeedFreeCell ?? false,
                                NeedTakenCell = lCellConstraints?.NeedTakenCell ?? false,
                                MaxPerTurn = capTurn > 0 ? capTurn + turnMods.ExtraCastsPerTurn(rc.SpellId) : 0,
                                MaxPerTarget = cap > 0 ? cap + turnMods.ExtraCastsPerTarget(rc.SpellId) : 0,
                                CastsThisTurn = ledger.GetCastsThisTurn(rc.SpellId),
                                CastsOnTarget = ledger.GetTargetCastsThisTurn(rc.SpellId, e.ActorId),
                                Ap = ap,
                                TurnNo = turnNo,
                                ChallengeOk = SpellOk(rc.SpellId),
                                TargetAllowed = TargetOk(e.ActorId),
                                SelfHpOk = SelfSpellGateOk(pid, rc, ap, turnNo, ens.Count),
                                TargetHpOk = TargetSpellGateOk(pid, rc, e.ActorId),
                                ZoneOk = EnemyZoneOk(rc.SpellId, here, e.Cell, rc.MinAoeHits),
                                TargetOccupied = true,
                                IsTargetMarked = MarkedCells(pid).Contains(e.Cell),
                                BlocksLos = BlocksLos,
                                Occupied = occ.Contains,
                                Ledger = ledger,
                            });
                            if (!legality) continue;
                            TransportLog.Line($"[combat] FILET attaque obligatoire : sort {rc.SpellId} -> cible {e.Cell} (dist {MapPathfinder.CellDistance(here, e.Cell)}, PA{ap}) [castsThisTurn={castsThisTurn}]");
                            if (!Cast(e.Cell, rc.SpellId, out var castRequestAt)) return;   // fenêtre fermée -> inutile de continuer ce tour
                            ap -= rs.Ap; castsThisTurn++;
                            await WaitAfterCast(castRequestAt).ConfigureAwait(false);
                            if (TryTurboCastRejectionWithMessage(pid, castRequestAt, out int refusedAp, out int messageId))
                            {
                                ledger.RecordRefusal(rc.SpellId, e.Cell, messageId, turnNo);
                                if (refusedAp >= 0) ap = refusedAp;
                                else ap += rs.Ap;
                                castsThisTurn--;
                                continue;
                            }
                            MarkEnemyConfirmed(pid, e.ActorId);
                            ledger.RecordCast(rc.SpellId, e.ActorId, turnNo);
                            NoteChallengeSpell(pid, rc.SpellId);
                            didCast = true;
                            break;
                        }
                        if (didCast) break;   // on relit positions/PA et on recommence
                    }
                    if (!didCast)
                    {
                        if (castsThisTurn == 0)   // on N'a RIEN fait ce tour : dire POURQUOI (portée/LdV) pour les rares vrais blocages
                        {
                            int nd = ens.Min(e => MapPathfinder.CellDistance(here, e.Cell));
                            // POURQUOI chaque sort offensif n'est pas castable sur l'ennemi le plus proche (portée, alignement, ligne de vue, conditions, plafonds…).
                            var nearest = ens.OrderBy(e => MapPathfinder.CellDistance(here, e.Cell)).First();
                            var why = new List<string>();
                            foreach (var (wc, wd) in rotation)
                            {
                                if (wc.OnMe || wd is null) continue;
                                var ws = wd.Value; string r;
                                int wer = ws.Range + (ws.Rangeable ? myRange : 0); if (wer < ws.MinRange) wer = ws.MinRange;
                                var (wlo, whi) = PlacementRange(wc, ws.MinRange, wer);
                                var wocc = new HashSet<int>(others.Where(cc => cc != nearest.Cell));
                                var wfc = _gameNames.SpellCastConstraints(wc.SpellId, LearnedSpellLevel(pid, wc.SpellId));
                                var wcaps = _gameNames.GetSpellDamage(wc.SpellId, LearnedSpellLevel(pid, wc.SpellId));
                                var wdesc = _gameNames.GetSpellDescriptor(wc.SpellId, LearnedSpellLevel(pid, wc.SpellId));
                                if (!wc.CanUseOffensiveFallback()) r = "filet désactivé";
                                else
                                {
                                    var res = Combat.CastLegality.CanCast(new Combat.CastLegalityContext
                                    {
                                        FromCell = here,
                                        TargetCell = nearest.Cell,
                                        TargetActorId = nearest.ActorId,
                                        SpellId = wc.SpellId,
                                        ApCost = ws.Ap,
                                        MinRange = wlo,
                                        MaxRange = whi,
                                        CastInLine = ws.CastInLine,
                                        CastInDiagonal = ws.CastInDiagonal,
                                        CastTestLos = ws.CastTestLos,
                                        NeedFreeCell = wfc?.NeedFreeCell ?? false,
                                        NeedTakenCell = wfc?.NeedTakenCell ?? false,
                                        MaxPerTurn = (wcaps?.MaxPerTurn ?? 0) > 0 ? (wcaps?.MaxPerTurn ?? 0) + turnMods.ExtraCastsPerTurn(wc.SpellId) : 0,
                                        MaxPerTarget = (wcaps?.MaxPerTarget ?? 0) > 0 ? (wcaps?.MaxPerTarget ?? 0) + turnMods.ExtraCastsPerTarget(wc.SpellId) : 0,
                                        CastsThisTurn = ledger.GetCastsThisTurn(wc.SpellId),
                                        CastsOnTarget = ledger.GetTargetCastsThisTurn(wc.SpellId, nearest.ActorId),
                                        Ap = ap,
                                        TurnNo = turnNo,
                                        Cooldown = wc.Cooldown,
                                        MinCastInterval = wdesc?.MinCastInterval ?? 0,
                                        InitialCooldown = wdesc?.InitialCooldown ?? 0,
                                        ChallengeOk = SpellOk(wc.SpellId),
                                        TargetAllowed = TargetOk(nearest.ActorId),
                                        SelfHpOk = SelfSpellGateOk(pid, wc, ap, turnNo, ens.Count),
                                        TargetHpOk = TargetSpellGateOk(pid, wc, nearest.ActorId),
                                        ZoneOk = EnemyZoneOk(wc.SpellId, here, nearest.Cell, wc.MinAoeHits),
                                        TargetOccupied = true,
                                        IsTargetMarked = MarkedCells(pid).Contains(nearest.Cell),
                                        BlocksLos = BlocksLos,
                                        Occupied = wocc.Contains,
                                        Ledger = ledger,
                                    });
                                    r = res.Reason ?? "plafond de lancers";
                                }
                                why.Add($"{wc.SpellId}:{r}");
                            }
                            TransportLog.Line($"[combat] FILET : aucun sort castable de {here} (ennemi le + proche à {nd}, PA{ap}) sorts=[{string.Join(",", rotation.Where(r => !r.Cfg.OnMe).Select(r => r.Cfg.SpellId))}] raisons=[{string.Join(" ; ", why)}] -> tour passé sans attaque");
                        }
                        return;
                    }
                }
            }

            // DÉFIS : déplacement de fin de tour imposé (Statue : aucun ; Zombie : exactement 1 PM ; Nomade : tous les PM ; Hardi,
            // Prudent, Collant, Misanthrope, lignée, diagonale, ligne de mire, pas vu pas pris, raser les murs, Conquérant : case de
            // fin conforme). REMPLACE le déplacement du style, dont la préférence départage les cases conformes.
            async Task ChallengeEndMove(bool recast = true)
            {
                if (plan is null) return;
                if (plan.NoMove)
                {
                    int current = SelfCell();
                    statueMoveBudget = System.Math.Max(0, System.Math.Min(mp, mpStart - stepsUsed));
                    if (SelfStats(pid) is { } restoreStats) statueMoveBudget = System.Math.Min(statueMoveBudget, System.Math.Max(0, restoreStats.Pm));
                    if (plan.OneMove) statueMoveBudget = System.Math.Min(statueMoveBudget, System.Math.Max(0, 1 - stepsUsed));
                    if (current >= 0 && current != statueStart && statueStart >= 0)
                    {
                        await TryMove(current, statueStart).ConfigureAwait(false);
                        if (SelfCell() != statueStart) TransportLog.Line($"[combat] défi Statue : retour à {statueStart} impossible depuis {SelfCell()} après déplacement forcé ; défi non garanti");
                    }
                    return;
                }
                int here = SelfCell(); if (here < 0) return;
                int left = MoveBudget();
                bool needOne = plan.OneMove && stepsUsed == 0;
                bool needAll = plan.AllMove && left > 0;
                bool cellBad = plan.HasEndCell && !plan.EndCellOk(here);
                int budget = plan.OneMove ? System.Math.Max(0, System.Math.Min(left, 1 - stepsUsed)) : left;
                if (budget <= 0)
                {
                    if (cellBad) TransportLog.Line($"[combat] défis : case de fin {here} non conforme mais plus aucun PM -> défi probablement raté");
                    return;
                }
                var positions = _fighters.TryGetValue(pid, out var moveSnapshot)
                    ? moveSnapshot.Select(f => (f.ActorId, f.Cell)) : Enumerable.Empty<(long, int)>();
                if (!challengeMoveAttempts.TryBegin(stepsUsed, here, budget, positions)) return;
                var reach = MapPathfinder.OrthoReachable(here, budget, WalkFight, StepAllowed);
                var cands = reach.Where(kv => (!plan.HasEndCell || plan.EndCellOk(kv.Key)) && PolicyMoveOk(here, kv.Key)).ToList();   // inclut ma case (0 pas) si conforme
                if (needOne) cands = cands.Where(kv => kv.Value == 1).ToList();
                else if (needAll)
                {
                    int far = cands.Count > 0 ? cands.Max(kv => kv.Value) : 0;
                    cands = cands.Where(kv => kv.Value == far && far > 0).ToList();
                    // Tout droit mène au CAC (taclé : PM perdus, défi raté) : on RECULE d'abord puis on REVIENT (2 déplacements qui épuisent tous les PM sans finir collé).
                    if (cands.Count > 0 && cands.All(kv => EnemyAdjacent(kv.Key)))
                    {
                        var foes = _fighters.TryGetValue(pid, out var nf) ? nf.Where(f => IsEnemy(pid, f.ActorId)).Select(f => f.Cell).ToList() : new List<int>();
                        (int Back, int Dest, int Score)? bestTwo = null;
                        foreach (var kv in reach.Where(k => k.Value >= 1 && k.Value < budget && !EnemyAdjacent(k.Key) && PolicyMoveOk(here, k.Key)))
                        {
                            var second = MapPathfinder.OrthoReachable(kv.Key, budget - kv.Value, WalkFight, StepAllowed);
                            foreach (var d2 in second.Where(k2 => k2.Value == budget - kv.Value && k2.Key != here && !EnemyAdjacent(k2.Key) && (!plan.HasEndCell || plan.EndCellOk(k2.Key)) && PolicyMoveOk(kv.Key, k2.Key)))
                            {
                                int score = foes.Count == 0 ? 0 : -foes.Min(fc => MapPathfinder.CellDistance(d2.Key, fc));   // le plus près possible sans être au contact
                                if (bestTwo is null || score > bestTwo.Value.Score) bestTwo = (kv.Key, d2.Key, score);
                            }
                        }
                        if (bestTwo is { } two)
                        {
                            TransportLog.Line($"[combat] défi Nomade : reculer {here} -> {two.Back} puis revenir {two.Dest} (évite le tacle)");
                            if (await TryMove(here, two.Back)) { int mid = SelfCell(); if (mid >= 0 && mid != two.Dest && await TryMove(mid, two.Dest) && recast) await RecastAfterApproach(); }
                            return;
                        }
                    }
                }
                else if (!cellBad)
                {
                    // Case déjà conforme : le style peut encore bouger, vers une case CONFORME seulement (Agressif se rapproche,
                    // Fuyard s'écarte s'il est trop près, Passif reste). « Ne jamais approcher » coupe le rapprochement ;
                    // le fuyard peut encore s'éloigner.
                    if (!profile.AllowsApproach && profile.Style != CombatStyle.Fleeing) return;
                    var ens = _fighters.TryGetValue(pid, out var fe) ? fe.Where(f => IsEnemy(pid, f.ActorId)).Select(f => f.Cell).ToList() : new List<int>();
                    if (profile.Style == CombatStyle.Passive || ens.Count == 0) return;
                    if (EnemyAdjacent(here)) return;   // au contact : partir serait taclé (refus / PM perdus) pour rien
                    if (profile.Style == CombatStyle.Fleeing && ens.Min(c => MapPathfinder.CellDistance(here, c)) >= profile.KiteMinDistance) return;
                }
                if (cands.Count == 0)
                {
                    TransportLog.Line($"[combat] défis : aucune case de fin de tour conforme atteignable avec la politique de déplacement ({budget} PM) -> je reste en {here}");
                    return;
                }
                // Distance de MARCHE réelle jusqu'aux ennemis (contourne les obstacles). À vol d'oiseau, deux cases de part et d'autre
                // d'un obstacle étaient à égalité : Zombie (1 PM) faisait gauche-droite en boucle sans jamais contourner (capture 23/10).
                var foeCells = _fighters.TryGetValue(pid, out var ff2) ? ff2.Where(f => IsEnemy(pid, f.ActorId)).Select(f => f.Cell).ToList() : new List<int>();
                var walkFrom = foeCells.Select(fc => MapPathfinder.OrthoReachable(fc, 40, c => c == fc || WalkFight(c), StepAllowed)).ToList();
                int WalkDist(int c) { int best = int.MaxValue; foreach (var m in walkFrom) if (m.TryGetValue(c, out var d) && d < best) best = d; return best; }
                if (!recast && cands.Count > 1)
                {
                    // Déplacement AVANT le coup de grâce : la case d'arrivée doit permettre de tirer. Sinon (combat perdu du
                    // 24/10 11:02) le bot marchait ses PM vers une case sans ligne de tir et passait le tour sans attaquer.
                    var finishers = cands.Where(kv => AreaCanFinishFrom(kv.Key)).ToList();
                    var shooters = finishers.Count > 0 ? finishers : cands.Where(kv => CanHitFrom(kv.Key)).ToList();
                    if (shooters.Count > 0 && shooters.Count < cands.Count)
                    {
                        TransportLog.Line($"[combat] défis : {shooters.Count}/{cands.Count} cases conformes permettent de tirer{(finishers.Count > 0 ? " (coup de grâce)" : "")}");
                        cands = shooters;
                    }
                }
                int dest = PreferEndCell(pid, plan, profile, cands, walkFrom.Count > 0 ? WalkDist : null);
                if (dest == here) return;
                TransportLog.Line($"[combat] défis : fin de tour -> {dest} ({reach[dest]} PM){(cellBad ? " [case imposée]" : "")}{(needOne ? " [1 PM]" : "")}{(needAll ? " [tous les PM]" : "")}");
                if (await TryMove(here, dest) && recast) await RecastAfterApproach();
            }

            // MOUVEMENT DE FIN DE TOUR selon le STYLE (avec les PM restants) — ou celui des DÉFIS s'ils en imposent un.
            if (plan is { Moves: true }) await ChallengeEndMove().ConfigureAwait(false);
            else if (MoveBudget() > 0 && _fighters.TryGetValue(pid, out var fend))
            {
                int myCell = SelfCell();
                var enemies = fend.Where(f => IsEnemy(pid, f.ActorId)).ToList();
                // (occupation relue LIVE par le WalkFight de tour — plus de snapshot figé ici)
                if (myCell >= 0 && enemies.Count > 0)
                {
                    if (profile.Style == CombatStyle.Aggressive)
                    {
                        if (!profile.AllowsApproach)
                            TransportLog.Line($"[combat] fin de tour : politique sans approche -> pas de rapprochement (case {myCell})");
                        else if (EnemyAdjacent(myCell))
                        {
                            // TACLE / melee-first : au contact d'un ennemi = verrouillé -> quitter la case serait refusé, ET on
                            // VEUT rester au corps-à-corps (demande user : « surtout si encerclé il doit attaquer »). On ne bouge
                            // pas ; le FILET « attaque obligatoire » (plus bas) frappe l'adjacent avec les PA restants.
                            TransportLog.Line($"[combat] fin de tour : ennemi au contact (case {myCell}) -> je reste au CAC (pas de déplacement taclé) [kite 0]");
                        }
                        else
                        {
                            // RE-SÉLECTION (fix P5) + RETRY (fix refus silencieux 2026-09-03) : on parcourt les ennemis VIVANTS
                            // du + proche au + loin, on approche le PREMIER atteignable ; si le déplacement est REFUSÉ (drop
                            // serveur silencieux), la case refusée est bloquée ce tour -> on RÉ-ESSAIE (jusqu'à 3×) une AUTRE
                            // case d'approche au lieu de « passer » sur un seul essai. Avec la barrière WaitMoveClear corrigée
                            // (attente d'une VRAIE fin de séquence, plus de TCS périmé), l'essai a bien plus de chances de passer.
                            bool approached = false;
                            for (int attempt = 0; attempt < 3 && !approached; attempt++)
                            {
                                int myNow = attempt == 0 ? myCell : SelfCell();
                                if (myNow < 0) break;
                                int chosen = -1, chSteps = 0, chTarget = -1;
                                var approachSet = enemies.Where(e => TargetOk(e.ActorId)).ToList();   // défis : cibles autorisées d'abord
                                foreach (var t in KillFirst(OrderTargets(profile.Target, approachSet.Count > 0 ? approachSet : enemies, myNow, a => FighterHpPercent(pid, a))))
                                {
                                    int approach = FindApproachCell(myNow, t.Cell, MoveBudget(), WalkFight, out int aSteps, StepAllowed);
                                    if (approach >= 0 && approach != myNow) { chosen = approach; chSteps = aSteps; chTarget = t.Cell; break; }
                                }
                                if (chosen < 0) break;   // plus aucune case d'approche (toutes bloquées / injoignables)
                                TransportLog.Line($"[combat] fin de tour : rapprochement -> {chosen} ({chSteps} PM, dist cible {MapPathfinder.CellDistance(chosen, chTarget)}){(attempt > 0 ? $" [essai {attempt + 1}]" : "")}");
                                if (await TryMove(myNow, chosen)) { await RecastAfterApproach(); approached = true; }   // accepté -> à portée ? on tire
                            }
                            if (!approached)
                            {
                                // Aucun ennemi vivant approchable : dire POURQUOI (diagnostic « il passe sans avancer »).
                                int blk = (_blockedCells.TryGetValue(pid, out var bb) ? bb.Count : 0)
                                        + (_fightObstacles.TryGetValue(pid, out var ob) ? ob.Count : 0);
                                int reachN = MapPathfinder.OrthoReachable(myCell, MoveBudget(), WalkFight, StepAllowed).Count - 1;
                                var t0 = SelectTarget(profile.Target, enemies, myCell, a => FighterHpPercent(pid, a));
                                // Diagnostic géométrie vs encerclement réel : floor[myCell]==-1 => géométrie de combat
                                // ABSENTE (LoadCells a rendu null / mauvaise carte) ; floor>=0 avec reachN==0 =>
                                // vraiment encerclé/bloqué. Lève l'angle mort du log (cf audit déplacement combat).
                                int walkN = 0; for (int c = 0; c < MapPathfinder.CellCount; c++) if (IsWalk(c)) walkN++;
                                TransportLog.Line($"[combat] fin de tour : AUCUN rapprochement (cible + proche {t0.Cell} dist {MapPathfinder.CellDistance(myCell, t0.Cell)}, {mp} PM, {reachN} cases atteignables, {blk} bloquees, floor[moi]={floorF[myCell]}, {walkN} cases marchables sur la carte) -> je passe");
                            }
                        }
                    }
                    else if (profile.Style == CombatStyle.Fleeing)
                    {
                        if (!EnemyAdjacent(myCell)) _tackledCell.TryRemove(pid, out _);
                        if (EnemyAdjacent(myCell) && _tackledCell.TryGetValue(pid, out int tc) && tc == myCell)
                        {
                            TransportLog.Line($"[combat] fin de tour : ennemi au contact (case {myCell}) et tacle confirmé -> je reste au contact (évite refus répété) [kite 0]");
                        }
                        else
                        {
                            var enemyCells = enemies.Select(e => e.Cell).ToList();
                        int nearest = enemyCells.Min(e => MapPathfinder.CellDistance(myCell, e));
                        int bandMax = profile.KiteMaxDistance;
                        if (bandMax > 0 && bandMax >= profile.KiteMinDistance)   // BANDE [min..max] : trop pres OU trop loin -> rejoindre la bande
                        {
                            // Trop loin = rapprochement : coupé par « ne jamais approcher ». Trop près = s'éloigner, conservé.
                            bool tooClose = nearest < profile.KiteMinDistance;
                            bool tooFar = nearest > bandMax;
                            if (tooClose || (profile.AllowsApproach && tooFar))
                            {
                                int dest = FindBandCell(myCell, enemyCells, MoveBudget(), profile.KiteMinDistance, bandMax, WalkFight, out int bSteps, StepAllowed);
                                if (dest >= 0 && dest != myCell)
                                {
                                    TransportLog.Line($"[combat] fin de tour : KITE bande [{profile.KiteMinDistance}..{bandMax}] -> {dest} ({bSteps} PM, écart {enemyCells.Min(e => MapPathfinder.CellDistance(dest, e))})");
                                    await TryMove(myCell, dest);
                                }
                            }
                        }
                        else if (profile.AllowsApproach && FleeingAttackReach() is int reach and > 0 && nearest > reach)
                        {
                            // Hors de portée de TOUS nos sorts : fuir encore n'a aucun sens (Poutch 24/10 13:49 : 38 tours sur place,
                            // ennemi à 13, portée 5). On se rapproche jusqu'à la portée de tir, sans passer sous la distance de kite.
                            int dest = FindBandCell(myCell, enemyCells, MoveBudget(), Math.Min(profile.KiteMinDistance, reach), reach, WalkFight, out int aSteps, StepAllowed);
                            if (dest >= 0 && dest != myCell)
                            {
                                TransportLog.Line($"[combat] fin de tour : FUYARD hors de portée ({nearest} > {reach}) -> rapprochement {dest} ({aSteps} PM, écart {enemyCells.Min(e => MapPathfinder.CellDistance(dest, e))})");
                                // Arrive a portee avec les PA intacts : tirer AVANT de passer (78 tours a 10 PA sans sort sur
                                // Eliobot le 09/11, toujours suivis de ce rapprochement sans relance).
                                if (await TryMove(myCell, dest)) await RecastAfterApproach();
                            }
                        }
                        else if (nearest < profile.KiteMinDistance)   // pas de borne haute (max=0) : ancien kite (s'éloigner au maximum)
                        {
                            int kite = FindKiteCell(myCell, enemyCells, MoveBudget(), WalkFight, out int kSteps, StepAllowed);
                            if (kite >= 0 && kite != myCell)
                            {
                                TransportLog.Line($"[combat] fin de tour : KITE -> {kite} ({kSteps} PM, ecart {enemyCells.Min(e => MapPathfinder.CellDistance(kite, e))})");
                                await TryMove(myCell, kite);
                            }
                        }
                        }
                    }
                    // Passif : aucun deplacement.
                }
            }

            // FILET « ATTAQUE OBLIGATOIRE » (demande explicite : quoi qu'il arrive il doit attaquer s'il le peut, surtout
            // encerclé). Tout dernier geste avant de finir, depuis ma case FINALE.
            await LastChanceAttack().ConfigureAwait(false);
            if (plan is { NoMove: true }) await ChallengeEndMove().ConfigureAwait(false);

            // Après chaque action Turbo MITM, WaitAfterCast/TryMove ont déjà attendu SequenceEnd+self ACK. Aucun sleep local
            // supplémentaire avant jxy : la sécurité vient de la barrière, pas d'un délai fixe de 150 ms.
            if (humanPace)
                await HumanEndPause().ConfigureAwait(false);   // 1,5-3 s avant FightTurnFinishRequest
            else if (profile.Speed != CombatSpeed.Turbo)
                await Task.Delay(StepDelay(), ct).ConfigureAwait(false);
            // Ne PAS injecter jxy si (a) ma fenêtre s'est refermée (un jzc a donné le tour à un autre combattant : mon tour
            // est DÉJÀ fini côté serveur), ou (b) ce run est PÉRIMÉ — un nouveau tour à moi s'est ouvert entre-temps, et
            // son jxy couperait net le tour COURANT (qui vient à peine de commencer).
            ct.ThrowIfCancellationRequested();
            if (epoch != _turnEpoch.GetValueOrDefault(pid))
                TransportLog.Line("[combat] fin de tour ignorée : run PÉRIMÉ (un tour plus récent est en cours)");
            else if (_myTurnOpen.GetValueOrDefault(pid)) SendEndTurn(pid);
            else TransportLog.Line("[combat] fin de tour ignorée : ma fenêtre était déjà refermée (tour donné à un autre combattant)");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            TransportLog.Line($"[combat] pid {pid} : arrêt du tour epoch={epoch} courant={_turnEpoch.GetValueOrDefault(pid)} fenêtre={_myTurnOpen.GetValueOrDefault(pid)} séquences={_isqCount.GetValueOrDefault(pid)} : {ex.GetType().Name} — {ex.Message}");
            if (ex.StackTrace is { } stack)
                foreach (string frame in stack.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    TransportLog.Line($"[combat-turn-error] pid={pid} epoch={epoch} {frame.Trim()}");
            try { if (epoch == _turnEpoch.GetValueOrDefault(pid) && _myTurnOpen.GetValueOrDefault(pid)) SendEndTurn(pid); }
            catch (Exception endError) { TransportLog.Line($"[combat] pid {pid} : échec de fin du tour après erreur : {endError.GetType().Name} — {endError.Message}"); }
        }
        finally { _engineControlled.Value = previousControlled; _turnRunning.TryRemove(pid, out _); }
    }

    // Reecrit la CELLULE de MON combattant (le joueur = actorId positif le plus grand) dans _fighters, et notifie l'UI.
    // Utilise pour l'update OPTIMISTE d'un deplacement (RunFightTurn) et la CORRECTION au refus (jqt). No-op hors combat.
    private readonly object _fightersLock = new();

    // Modification ATOMIQUE de _fighters[pid] (lecture-calcul-ecriture sous verrou) : sans ca, l'update optimiste self (thread
    // du tour) et les jsj/slide/mort issus de MES casts (thread paquet) se clobberent -> une position de combattant est
    // perdue -> le bot se deplace/vise une case que le serveur refuse (occupee / vue bloquee). mutate renvoie null = pas de
    // changement. Renvoie la nouvelle liste (ou null) pour que l'appelant puisse logguer.
    private IReadOnlyList<(int Cell, long ActorId)>? MutateFighters(uint pid, Func<IReadOnlyList<(int Cell, long ActorId)>, IReadOnlyList<(int Cell, long ActorId)>?> mutate)
    {
        IReadOnlyList<(int Cell, long ActorId)>? result;
        lock (_fightersLock)
        {
            if (!_fighters.TryGetValue(pid, out var cur)) return null;
            result = mutate(cur);
            if (result is null) return null;
            _fighters[pid] = result;
        }
        FightersUpdated?.Invoke(pid, result);
        return result;
    }

    private void SetSelfFighterCell(uint pid, int cell) => MutateFighters(pid, fs =>
    {
        // Identité = SelfActorId (id AUTHENTIFIÉ _charId d'abord, repli max-positif) : le max-positif SEUL bougerait la
        // case de l'ALLIÉ en multi-compte/groupe. SelfActorId relit la MÊME liste (sous _fightersLock) -> cohérent.
        long selfId = SelfActorId(pid);
        return selfId == 0 ? null : fs.Select(f => f.ActorId == selfId ? (cell, f.ActorId) : f).ToList();
    });
}
