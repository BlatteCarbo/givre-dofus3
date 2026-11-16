using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GivreClone.Protocol;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

/// <summary>Politique d'un script EN COURS (globals Lua lus sur le thread du script, poussés au moteur) : verrouillage auto
/// du combat (FIGHT_LOCK_*) et refus auto des demandes d'échange / de défi (IGNORE_REQUEST_*). RunId = run propriétaire
/// (un ancien run qui se déroule n'efface pas la politique du nouveau). Tous les défauts à false = comportement d'avant.</summary>
public sealed record ScriptPolicy(long RunId, bool LockSpec, bool LockJoin, bool LockParty, bool IgnoreExchange, bool IgnoreDuel, bool IgnoreGuild = false);

// ===================== Lot E : petites forges (options de combat, respawn zaap, refus auto, variantes, abandon, modération) =====================
// Tout l'état est écrit par les décodeurs (thread réseau) dans des ConcurrentDictionary et lu par le thread du script.
// Les automatismes (verrouillage, refus) tournent dans des Task lancées depuis le décodeur : JAMAIS de Lua, jamais de _codeWait.
public sealed partial class LaunchService
{
    /// <summary>Popup de modération reçu (PopupWarningEvent) : contenu, auteur, durée de silence (s), instant UTC.</summary>
    public sealed record ModeratorPopup(string Content, string Author, int LockSeconds, DateTime AtUtc);

    /// <summary>Options de combat verrouillées automatiquement (FIGHT_LOCK_*) : (pid, options appliquées).</summary>
    public event Action<uint, IReadOnlyList<int>>? FightLocksApplied;
    /// <summary>Demande reçue refusée automatiquement (IGNORE_REQUEST_*) : (pid, "exchange"|"duel", characterId émetteur).</summary>
    public event Action<uint, string, long>? RequestAutoRefused;
    /// <summary>Popup de modération reçu par ce bot (thread réseau).</summary>
    public event Action<uint, ModeratorPopup>? ModeratorPopupReceived;

    private readonly ConcurrentDictionary<uint, ScriptPolicy> _scriptPolicy = new();
    /// <summary>Pose la politique du script courant (thread du script, au démarrage puis à chaque changement de global).</summary>
    public void SetScriptPolicy(uint pid, ScriptPolicy p) => _scriptPolicy[pid] = p;
    /// <summary>Efface la politique SI elle appartient encore au run `runId` (un ancien run n'efface pas celle du nouveau).</summary>
    public void ClearScriptPolicy(uint pid, long runId)
    {
        if (_scriptPolicy.TryGetValue(pid, out var p) && p.RunId == runId)
            _scriptPolicy.TryRemove(new KeyValuePair<uint, ScriptPolicy>(pid, p));
    }
    // N'importe quel perso piloté par l'app (équipe incluse) : jamais refusé automatiquement.
    private bool IsOwnCharacter(long id) => id != 0 && _charId.Values.Contains(id);

    // ---------------------------------------------------------------- options de combat (FIGHT_LOCK_*, combat:lock)
    private const int FightOptSecret = 0, FightOptPartyOnly = 1, FightOptClosed = 2;
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<int, bool>> _fightOptState = new();  // état reçu (jyq) du combat courant
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<int, byte>> _fightOptSent = new();   // bascules envoyées CE combat
    private readonly ConcurrentDictionary<uint, string> _lockSkipLog = new();   // une raison de « rien envoyé » par combat, pas deux lignes identiques

    /// <summary>Option de combat `opt` active dans le combat courant (état connu par FightOptionUpdateEvent). Lecture pure.</summary>
    public bool FightOptionOn(uint pid, int opt) => _fightOptState.TryGetValue(pid, out var d) && d.TryGetValue(opt, out var v) && v;
    /// <summary>Option déjà basculée par le bot pendant CE combat (accusé serveur peut-être pas encore reçu). Lecture pure.</summary>
    public bool FightOptionSent(uint pid, int opt) => _fightOptSent.TryGetValue(pid, out var d) && d.ContainsKey(opt);

    // Le message BASCULE l'option : l'envoyer deux fois la déverrouille -> idempotent (état reçu OU déjà basculé ce combat).
    private bool ToggleFightOptionOn(uint pid, int opt)
    {
        if (opt == FightOptClosed && TeamFollowersJoinPending(pid)) return false;
        if (FightOptionOn(pid, opt)) return true;
        var sent = _fightOptSent.GetOrAdd(pid, _ => new());
        if (!sent.TryAdd(opt, 0)) return true;   // déjà basculée ce combat (jyq pas encore reçu) -> ne JAMAIS re-basculer
        var relay = RelayForInject();
        if (relay is null) { sent.TryRemove(opt, out _); return false; }
        byte[] pkt = MoveForge.BuildFightOptionToggleFramed(opt);
        if (pkt.Length == 0) { sent.TryRemove(opt, out _); TransportLog.Line($"[combat] option {opt} NON forgée : {MoveForge.LastForgeError}"); return false; }
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        if (!ok) sent.TryRemove(opt, out _);   // rien n'est parti : une nouvelle tentative reste possible
        TransportLog.Line($"[combat] pid {pid} : FightOptionToggleRequest option={opt} inject={(ok ? "OK" : "ECHEC")}{(ok ? " ; confirmation serveur en attente" : "")}");
        return ok;
    }

    /// <summary>Verrouille les options demandées du combat COURANT (spec = pas de spectateurs, join = fermé, party = groupe
    /// seulement). Idempotent. false si hors combat, sans droit, ou si une bascule n'a pas pu partir. Gaté Perm.Fight.</summary>
    public bool FightLock(uint pid, bool spec, bool join, bool party)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Fight) || !IsInFight(pid)) return false;
        bool ok = true;
        if (spec) ok &= ToggleFightOptionOn(pid, FightOptSecret);
        if (party) ok &= ToggleFightOptionOn(pid, FightOptPartyOnly);
        if (join)
        {
            // PIÈGE ÉQUIPE (cf. ApplyFightLocksAsync / RunPreFight) : un chef qui ferme le combat pendant que ses mules
            // marchent encore vers lui les EXCLUT (ex. combat:lock depuis onPlacement). -> fermeture DIFFÉRÉE jusqu’à la jointure de l’équipe, jamais de Lua (tâche de fond). Sans équipe en attente : immédiat, comme avant.
            if (TeamFollowersJoinPending(pid)) _ = CloseFightWhenTeamJoinedAsync(pid, CurrentFightId(pid));
            else ok &= ToggleFightOptionOn(pid, FightOptClosed);
        }
        return ok;
    }

    private async Task CloseFightWhenTeamJoinedAsync(uint pid, long fightId)
    {
        long fightStart = _fightStartAt.GetValueOrDefault(pid);
        try
        {
            bool Still() => IsCurrentTeamPlacement(pid, fightStart) && (fightId == 0 || CurrentFightId(pid) == fightId);
            if (!await WaitTeamJoinBeforeChallengeAsync(pid, System.Threading.CancellationToken.None, untilJoined: true).ConfigureAwait(false)) return;
            await Task.Delay(Random.Shared.Next(150, 400)).ConfigureAwait(false);
            if (Still()) ToggleFightOptionOn(pid, FightOptClosed);
        }
        catch (Exception ex) { TransportLog.Line($"[combat] verrouillage : {ex.Message}"); }
    }

    /// <summary>Un script en cours pose-t-il ses propres verrous de combat (FIGHT_LOCK_*) ?</summary>
    public bool ScriptLocksFight(uint pid) => _scriptPolicy.TryGetValue(pid, out var p) && (p.LockSpec || p.LockJoin || p.LockParty);

    // Verrouillage AUTOMATIQUE (globals FIGHT_LOCK_*) au début d'un combat. Tâche lancée par le décodeur : jamais de Lua.
    // Les options 0 (spectateurs) et 1 (groupe) partent au début du placement, AVANT le délai. L'option 2 (fermer)
    // garde le délai et l'attente des mules. Rien envoyé -> une ligne avec la raison. 0/1/2 ne changent pas.
    private async Task ApplyFightLocksAsync(uint pid, long fightId)
    {
        long fightStart = _fightStartAt.GetValueOrDefault(pid);
        try
        {
            _scriptPolicy.TryGetValue(pid, out var sp);
            _combat.TryGetValue(pid, out var prof);
            var pol = FightLocks.Merge(new FightLocks(sp?.LockSpec ?? false, sp?.LockJoin ?? false, sp?.LockParty ?? false), prof);   // script OU profil
            if (!pol.Any) return;
            // Au FightStarting le placement vient d'être effacé : cet appel ne fait qu'expliquer pourquoi 0/1 ne
            // partent pas encore. FightPlacementPossiblePositionsEvent rappelle SendSpectatorAndPartyLocks dès l'ouverture.
            if (pol.Spectators || pol.Party)
                SendSpectatorAndPartyLocks(pid, fightId);
            if (!pol.Join) return;
            if (LockSkipReason(pid, fightStart, fightId) is { } early && early is not ("placement fermé" or "placement pas encore ouvert"))
            {
                LogLockSkip(pid, early);
                return;
            }
            await Task.Delay(Random.Shared.Next(400, 900)).ConfigureAwait(false);
            if (LockSkipReason(pid, fightStart, fightId) is { } late)
            {
                LogLockSkip(pid, late);
                return;
            }
            // PIÈGE ÉQUIPE (cf. RunPreFight) : fermer le combat AVANT que les mules l'aient rejoint les EXCLUT. Même attente stricte.
            if (!await WaitTeamJoinBeforeChallengeAsync(pid, System.Threading.CancellationToken.None, untilJoined: true).ConfigureAwait(false))
            {
                LogLockSkip(pid, "équipe incomplète");
                return;
            }
            await Task.Delay(Random.Shared.Next(150, 400)).ConfigureAwait(false);
            if (LockSkipReason(pid, fightStart, fightId) is { } after)
            {
                LogLockSkip(pid, after);
                return;
            }
            if (FightOptionOn(pid, FightOptClosed) || FightOptionSent(pid, FightOptClosed))
            {
                LogLockSkip(pid, "déjà actifs ou déjà basculés ce combat");
                return;
            }
            if (ToggleFightOptionOn(pid, FightOptClosed)) FightLocksApplied?.Invoke(pid, new[] { FightOptClosed });
            else LogLockSkip(pid, "injection impossible");
        }
        catch (Exception ex) { TransportLog.Line($"[combat] verrouillage : {ex.Message}"); }
    }

    /// <summary>Options 0 et 1, sans délai. N'envoie rien si le placement n'est pas ouvert, si le perso est suiveur,
    /// ou si l'option est déjà basculée. Journalise la raison quand aucun paquet ne part.</summary>
    private void SendSpectatorAndPartyLocks(uint pid, long fightId)
    {
        long fightStart = _fightStartAt.GetValueOrDefault(pid);
        _scriptPolicy.TryGetValue(pid, out var sp);
        _combat.TryGetValue(pid, out var prof);
        var pol = FightLocks.Merge(new FightLocks(sp?.LockSpec ?? false, sp?.LockJoin ?? false, sp?.LockParty ?? false), prof);
        if (!pol.Spectators && !pol.Party) return;
        if (LockSkipReason(pid, fightStart, fightId) is { } reason)
        {
            LogLockSkip(pid, reason);
            return;
        }
        var done = new List<int>();
        bool blocked = false, failed = false;
        if (pol.Spectators)
        {
            if (FightOptionOn(pid, FightOptSecret) || FightOptionSent(pid, FightOptSecret)) blocked = true;
            else if (ToggleFightOptionOn(pid, FightOptSecret)) done.Add(FightOptSecret);
            else failed = true;
        }
        if (pol.Party)
        {
            if (FightOptionOn(pid, FightOptPartyOnly) || FightOptionSent(pid, FightOptPartyOnly)) blocked = true;
            else if (ToggleFightOptionOn(pid, FightOptPartyOnly)) done.Add(FightOptPartyOnly);
            else failed = true;
        }
        if (done.Count > 0) FightLocksApplied?.Invoke(pid, done);
        else LogLockSkip(pid, failed && !blocked ? "injection impossible" : "déjà actifs ou déjà basculés ce combat");
    }

    private string? LockSkipReason(uint pid, long fightStart, long fightId)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Fight)) return "pas de droit combat";
        if (IsTeamFollower(pid)) return "suiveur d'équipe (verrous réservés au chef)";
        if (ManualCombatOn(pid)) return "combat manuel";
        if (!IsInFight(pid)) return "hors combat";
        if (_fightStartAt.GetValueOrDefault(pid) != fightStart) return "combat remplacé";
        if (fightId != 0 && CurrentFightId(pid) != fightId) return "autre combat";
        if (!_placementOpen.TryGetValue(pid, out var open) || !open)
            return _placementOpen.ContainsKey(pid) ? "placement fermé" : "placement pas encore ouvert";
        return null;
    }

    private void LogLockSkip(uint pid, string reason)
    {
        if (_lockSkipLog is { } seen)
        {
            if (seen.TryGetValue(pid, out var prev) && prev == reason) return;
            seen[pid] = reason;
        }
        TransportLog.Line($"[combat] pid {pid} : verrous non envoyés : {reason}");
    }

    // ---------------------------------------------------------------- point de respawn (map:saveZaap)
    private readonly ConcurrentDictionary<uint, long> _respawnMap = new(), _respawnSeq = new();
    /// <summary>Carte du point de respawn connu (spawn_map_id de la liste des zaaps, ou ZaapRespawnUpdatedEvent), 0 = inconnu.</summary>
    public long RespawnMap(uint pid) => _respawnMap.TryGetValue(pid, out var m) ? m : 0;
    /// <summary>N° de séquence des ZaapRespawnUpdatedEvent reçus (s'incrémente à chaque sauvegarde confirmée).</summary>
    public long RespawnSeq(uint pid) => _respawnSeq.TryGetValue(pid, out var s) ? s : 0;

    /// <summary>Sauvegarde le point de respawn sur le zaap OUVERT (ZaapRespawnSaveRequest). Gaté Perm.Gather (interactifs).</summary>
    public bool ZaapRespawnSave(uint pid)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        var relay = RelayForInject();
        if (relay is null) return false;
        byte[] pkt = MoveForge.BuildZaapRespawnSaveFramed();
        if (pkt.Length == 0) { TransportLog.Line($"[zaap] respawn NON forgé : {MoveForge.LastForgeError}"); return false; }
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[zaap] pid {pid} : ZaapRespawnSaveRequest inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    // ---------------------------------------------------------------- défis reçus (IGNORE_REQUEST_DUEL)
    private readonly ConcurrentDictionary<uint, (long FightId, long Source)> _pendingDuel = new();

    /// <summary>Répond à un défi amical reçu. Non gaté : refuser = fermer une demande, toujours permis ; ACCEPTER lance un
    /// combat -> gaté Perm.Fight.</summary>
    public bool FriendlyDuelAnswer(uint pid, long fightId, bool accept)
    {
        if (accept && !LicenseService.HasPermission(LicenseService.Perm.Fight)) return false;
        var relay = RelayForInject();
        if (relay is null || fightId == 0) return false;
        byte[] pkt = MoveForge.BuildFriendlyDuelAnswerFramed(fightId, accept);
        if (pkt.Length == 0) { TransportLog.Line($"[combat] réponse de défi NON forgée : {MoveForge.LastForgeError}"); return false; }
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[combat] pid {pid} : PlayerFightFriendlyAnswerRequest fight {fightId} accept={accept} inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    // Refus auto d'une demande d'ÉCHANGE reçue (appelé par OnExchangePacket, thread réseau). La demande est déjà mémorisée
    // dans _tradePending. Ne ferme JAMAIS un dialogue légitime (PNJ / banque / HDV / échange déjà ouvert).
    private bool DialogBusyForAutoRefuse(uint pid) => _lastNpcDialog.ContainsKey(pid) || HdvAnyOpen(pid) || StorageOpen(pid) || TradeOpen(pid) || ChestSessionActive(pid);   // Lot J : coffre à code ouvert = dialogue occupé
    private void MaybeAutoRefuseTrade(uint pid, long src)
    {
        if (!_scriptPolicy.TryGetValue(pid, out var pol) || !pol.IgnoreExchange || IsOwnCharacter(src) || DialogBusyForAutoRefuse(pid)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Random.Shared.Next(900, 1700)).ConfigureAwait(false);
                if (!_scriptPolicy.TryGetValue(pid, out var p2) || !p2.IgnoreExchange || DialogBusyForAutoRefuse(pid)) return;   // script arrêté / global repassé à false
                if (TradePending(pid) is { SourceId: var s } && s == src && TradeCancel(pid))
                {
                    _tradePending.TryRemove(pid, out _);
                    RequestAutoRefused?.Invoke(pid, "exchange", src);
                }
            }
            catch (Exception ex) { TransportLog.Line($"[échange] refus auto : {ex.Message}"); }
        });
    }

    // ---------------------------------------------------------------- variantes de sort
    private readonly ConcurrentDictionary<uint, (int SpellId, bool Effective, long Seq)> _variantResult = new();
    /// <summary>Dernière réponse du serveur à une activation de variante (spell_id, effective, n° de séquence).</summary>
    public (int SpellId, bool Effective, long Seq) LastSpellVariantResult(uint pid) => _variantResult.TryGetValue(pid, out var r) ? r : (0, false, 0);

    /// <summary>Active la variante `spellId` (SpellVariantActivationRequest). HORS combat seulement. Gaté Perm.Fight
    /// (équipement de combat).</summary>
    public bool SpellVariantActivate(uint pid, int spellId)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Fight) || IsInFight(pid) || spellId <= 0) return false;
        var relay = RelayForInject();
        if (relay is null) return false;
        byte[] pkt = MoveForge.BuildSpellVariantActivationFramed(spellId);
        if (pkt.Length == 0) { TransportLog.Line($"[sort] variante NON forgée : {MoveForge.LastForgeError}"); return false; }
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[sort] pid {pid} : SpellVariantActivationRequest sort {spellId} inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    // ---------------------------------------------------------------- abandon (combat:giveUp)
    /// <summary>Abandonne le combat courant (ContextQuitRequest). JAMAIS hors combat (sens inconnu hors combat). Gaté Perm.Fight.</summary>
    public bool FightGiveUp(uint pid)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Fight) || !IsInFight(pid)) return false;
        var relay = RelayForInject();
        if (relay is null) return false;
        byte[] pkt = MoveForge.BuildContextQuitFramed();
        if (pkt.Length == 0) { TransportLog.Line($"[combat] abandon NON forgé : {MoveForge.LastForgeError}"); return false; }
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[combat] pid {pid} : abandon ContextQuitRequest inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    // ---------------------------------------------------------------- modération
    private readonly ConcurrentDictionary<uint, ModeratorPopup> _lastPopup = new();
    /// <summary>Dernier popup de modération reçu par ce bot, ou null.</summary>
    public ModeratorPopup? LastModeratorPopup(uint pid) => _lastPopup.TryGetValue(pid, out var m) ? m : null;

    // ---------------------------------------------------------------- décodage (thread réseau, appelé par OnDecoded)
    private void OnLotEPacket(DecodedPacket p)
    {
        if (p.ClientToServer || p.Name is null) return;
        switch (p.Name)
        {
            case "FightOptionUpdateEvent":
                if (_inFight.TryGetValue(p.Pid, out var io) && io)
                {
                    var (opt, state, fid, team) = LotEEventsReader.ReadFightOptionUpdate(p.Payload);
                    long cur = CurrentFightId(p.Pid);
                    // Options des AUTRES combats de la carte : ignorées (fight_id différent). Options de l'équipe ADVERSE
                    // (PvP / percepteur / duel) : ignorées aussi, sinon leur verrou passait pour le nôtre et le bot ne
                    // basculait jamais le sien. Équipe inconnue (jxg pas encore lu) -> comportement d'origine.
                    bool otherTeam = TeamOf(p.Pid, SelfActorId(p.Pid)) is int mine && team != mine;
                    if ((fid == 0 || cur == 0 || fid == cur) && !otherTeam)
                    {
                        _fightOptState.GetOrAdd(p.Pid, _ => new())[opt] = state;
                        TransportLog.Line($"[combat] pid {p.Pid} : option {opt} état serveur confirmé={(state ? "actif" : "inactif")} combat={fid} équipe={team}");
                    }
                }
                break;
            case "ZaapRespawnUpdatedEvent":
            {
                long m = LotEEventsReader.ReadRespawnMap(p.Payload);
                if (m != 0) _respawnMap[p.Pid] = m;   // AVANT le seq : le seq sert de barrière de publication
                _respawnSeq.AddOrUpdate(p.Pid, 1, static (_, s) => s + 1);
                TransportLog.Line($"[zaap] pid {p.Pid} : point de respawn -> {m}");
                break;
            }
            case "DialogLeaveEvent":
                _tradePending.TryRemove(p.Pid, out _);
                break;
            case "PlayerFightFriendlyRequestedEvent":
            {
                var (fid, src, tgt) = LotEEventsReader.ReadDuelRequest(p.Payload);
                long me = CharacterId(p.Pid);
                if (me == 0 || tgt != me || src == me || fid == 0) break;   // id inconnu (repli sûr) ou écho de NOTRE défi
                _pendingDuel[p.Pid] = (fid, src);
                TransportLog.Line($"[combat] défi reçu pid {p.Pid} de {src} (fight {fid})");
                if (_scriptPolicy.TryGetValue(p.Pid, out var pol) && pol.IgnoreDuel && !IsOwnCharacter(src))
                {
                    uint pid = p.Pid;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(Random.Shared.Next(900, 1700)).ConfigureAwait(false);
                            if (!_scriptPolicy.TryGetValue(pid, out var p2) || !p2.IgnoreDuel) return;   // script arrêté / global repassé à false
                            if (_pendingDuel.TryGetValue(pid, out var d) && d.FightId == fid && FriendlyDuelAnswer(pid, fid, false))
                            {
                                _pendingDuel.TryRemove(pid, out _);
                                RequestAutoRefused?.Invoke(pid, "duel", src);
                            }
                        }
                        catch (Exception ex) { TransportLog.Line($"[combat] refus auto : {ex.Message}"); }
                    });
                }
                break;
            }
            case "FightRequestCanceledEvent":
            case "PlayerFightFriendlyAnsweredEvent":
            {
                long fid = LotEEventsReader.ReadFightId(p.Payload, p.Name);
                if (_pendingDuel.TryGetValue(p.Pid, out var d) && (fid == 0 || d.FightId == fid)) _pendingDuel.TryRemove(p.Pid, out _);
                break;
            }
            case "SpellVariantActivationEvent":
            {
                var (spellId, effective) = LotEEventsReader.ReadSpellVariantResult(p.Payload);
                _variantResult.AddOrUpdate(p.Pid, (spellId, effective, 1), (_, r) => (spellId, effective, r.Seq + 1));
                TransportLog.Line($"[sort] pid {p.Pid} : variante {spellId} effective={effective}");
                if (effective && spellId > 0) ApplySpellVariant(p.Pid, spellId);
                break;
            }
            case "PopupWarningEvent":
            {
                var (content, author, lockS) = LotEEventsReader.ReadPopup(p.Payload);
                var mp = new ModeratorPopup(content, author, lockS, DateTime.UtcNow);
                _lastPopup[p.Pid] = mp;
                TransportLog.Line($"[modération] pid {p.Pid} : popup de {author} (verrou {lockS}s)");
                ModeratorPopupReceived?.Invoke(p.Pid, mp);
                break;
            }
            case "SystemMessageDisplayEvent":
                OnServerSystemMessage(p);   // hang_up : le serveur coupe la session (inactivité, modération…) -> LaunchService.GameLink
                break;
            case "ReconnectToGameServerEvent":
            case "ArenaSwitchToFightServerEvent":
                NoteServerTransfer(p.Pid);   // transfert vers un autre serveur (jeu / combat d'arène) : la fermeture qui suit n'est pas une coupure
                break;
            default:
                // Ouverture d'un échange (banque, HDV, joueur…) : la demande reçue n'est plus en attente.
                if (p.Name.StartsWith("ExchangeStarted", StringComparison.Ordinal) || p.Name is "ExchangeBidSellerStartedEvent" or "ExchangeBidBuyerStartedEvent")
                    _tradePending.TryRemove(p.Pid, out _);   // HDV : noms v5 (ExchangeBid…StartedEvent), hors du préfixe « ExchangeStarted »
                break;
        }
    }

    // Variante activée : remplace ses sœurs dans les sorts appris (le grade de la sœur est recopié) -> combat:getSpells,
    // l'onglet Combat et l'auto-combat voient la variante active.
    private void ApplySpellVariant(uint pid, int spellId)
    {
        var pair = _gameNames.SpellVariantPair(spellId);
        var cur = LearnedSpells(pid);
        if (cur.Count == 0) return;
        var siblings = pair.Where(x => x != spellId).ToHashSet();
        var levels = _charSpellLevels.TryGetValue(pid, out var lv) ? new Dictionary<int, int>(lv) : new Dictionary<int, int>();
        int grade = 0;
        foreach (var sib in siblings) if (levels.TryGetValue(sib, out var g) && g > grade) grade = g;
        var list = cur.Where(x => !siblings.Contains(x)).ToList();
        if (!list.Contains(spellId)) list.Add(spellId);
        if (grade > 0 && !levels.ContainsKey(spellId)) levels[spellId] = grade;
        foreach (var sib in siblings) levels.Remove(sib);
        _charSpellLevels[pid] = levels;
        _charSpells[pid] = list;
        CharacterSpellsUpdated?.Invoke(pid, list);
    }

    // Purge de l'état Lot E d'un client arrêté (pid réutilisable par l'OS).
    private void ForgetLotE(uint pid)
    {
        _pendingDuel.TryRemove(pid, out _);
        _fightOptState.TryRemove(pid, out _); _fightOptSent.TryRemove(pid, out _);
        _lastPopup.TryRemove(pid, out _);
        _respawnMap.TryRemove(pid, out _); _respawnSeq.TryRemove(pid, out _);
        _variantResult.TryRemove(pid, out _);
    }

    // Reset des options de combat au début / à la fin d'un combat.
    private void ResetFightOptions(uint pid) { _fightOptState.TryRemove(pid, out _); _fightOptSent.TryRemove(pid, out _); _lockSkipLog?.TryRemove(pid, out _); }
}
