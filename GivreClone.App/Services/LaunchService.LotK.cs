using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

// ===================== Lot K : confort (petits verbes) =====================
// Rafraîchissement de carte (MapInformationRequest), statut joueur (privé / disponible), réinitialisation des caractéristiques,
// défis ACTIFS du combat (ChallengeList/Add/Targets). Décodeurs = thread réseau (écritures ConcurrentDictionary aux valeurs
// immuables) ; envois SYNCHRONES depuis le thread du script (relay.Inject), confirmation par sondage côté hôte (compteurs),
// jamais de _codeWait (il n'admet qu'UN attendant par pid et écraserait une séquence banque/PNJ en cours).
public sealed partial class LaunchService
{
    /// <summary>CharacterStatus.status « disponible ». À confirmer en jeu via le log [statut].</summary>
    public const int PlayerStatusAvailable = 1;
    /// <summary>CharacterStatus.status « privé ». À confirmer en jeu via le log [statut].</summary>
    public const int PlayerStatusPrivate = 4;

    private readonly ConcurrentDictionary<uint, int> _mapInfoSeq = new();       // ++ à chaque MapComplementaryInformationEvent
    private readonly ConcurrentDictionary<uint, long> _mapInfoReqAt = new();    // anti-spam sendMapRequest
    private readonly ConcurrentDictionary<uint, int> _playerStatus = new();     // dernier statut serveur (PlayerStatusUpdatedEvent pour MON charId)
    private readonly ConcurrentDictionary<uint, long> _resetStatsAt = new();    // cooldown resetStats
    // Défis ACTIFS (validés) du combat en cours / dernier combat. Vidés au PROCHAIN FightStarting (PAS en fin de combat : le
    // script relit les résultats après fight()). Les résultats vivent dans _challengeResults (Lot C2, même cycle de vie).
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<int, FightPrepReader.FightChallenge>> _challenges = new();
    private const long MapInfoMinIntervalMs = 2000, ResetStatsCooldownMs = 5000;

    /// <summary>Compteur de MapComplementaryInformationEvent reçus (sert d'accusé à map:sendMapRequest). Lecture pure.</summary>
    public int MapInfoSeq(uint pid) => _mapInfoSeq.TryGetValue(pid, out var s) ? s : 0;

    /// <summary>Redemande la carte de jeu de rôle COURANTE (MapInformationRequest). Hors combat seulement ; plancher de 2 s par
    /// pid. Gate Perm.Gather (socle) AU POINT D'ACTION.</summary>
    public bool SendMapInformationRequest(uint pid)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        if (IsInFight(pid) || CurrentMap(pid) is not long map) return false;
        long now = Environment.TickCount64;
        if (_mapInfoReqAt.TryGetValue(pid, out var last) && now - last < MapInfoMinIntervalMs) return false;
        var relay = RelayForInject(); if (relay is null) return false;
        byte[] pkt = MoveForge.BuildMapInformationRequestFramed(map);
        if (pkt.Length == 0) { TransportLog.Line($"[carte] pid {pid} : MapInformationRequest non forgé ({MoveForge.LastForgeError})"); return false; }
        _mapInfoReqAt[pid] = now;
        bool ok = relay.Inject(pid, pkt); ShowInjected(pid, pkt);
        TransportLog.Line($"[carte] pid {pid} : MapInformationRequest {map} inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    /// <summary>PlayerStatusUpdateRequest (privé / disponible). Gate Perm.Gather (socle : action sociale sans effet de jeu).</summary>
    public bool SetPlayerStatus(uint pid, int status)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        var relay = RelayForInject(); if (relay is null) return false;
        byte[] pkt = MoveForge.BuildPlayerStatusFramed(status);
        if (pkt.Length == 0) { TransportLog.Line($"[statut] pid {pid} : PlayerStatusUpdateRequest non forgé ({MoveForge.LastForgeError})"); return false; }
        bool ok = relay.Inject(pid, pkt); ShowInjected(pid, pkt);
        TransportLog.Line($"[statut] pid {pid} : PlayerStatusUpdateRequest statut {status} inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    /// <summary>Dernier statut joueur renvoyé par le serveur pour CE perso (-1 = inconnu). Lecture pure.</summary>
    public int PlayerStatusOf(uint pid) => _playerStatus.TryGetValue(pid, out var s) ? s : -1;

    /// <summary>ResetCharacterCharacteristicsRequest (corps VIDE). Hors combat, cooldown 5 s. Gate Perm.Fight (même famille que
    /// la progression des caractéristiques : montée auto / SendCharacteristicUpgrade sont sous Perm.Fight).</summary>
    public bool ResetCharacteristics(uint pid)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Fight)) return false;
        if (IsInFight(pid)) return false;
        long now = Environment.TickCount64;
        if (_resetStatsAt.TryGetValue(pid, out var last) && now - last < ResetStatsCooldownMs) return false;
        var relay = RelayForInject(); if (relay is null) return false;
        byte[] pkt = MoveForge.BuildResetCharacteristicsFramed();
        if (pkt.Length == 0) { TransportLog.Line($"[carac] pid {pid} : ResetCharacterCharacteristicsRequest non forgé ({MoveForge.LastForgeError})"); return false; }
        _resetStatsAt[pid] = now;
        bool ok = relay.Inject(pid, pkt); ShowInjected(pid, pkt);
        TransportLog.Line($"[carac] pid {pid} : ResetCharacterCharacteristicsRequest inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    /// <summary>Défis ACTIFS du combat en cours / dernier combat, triés par id, avec leur résultat : 1 réussi, 0 échoué,
    /// -1 inconnu (en cours). Lecture pure.</summary>
    public IReadOnlyList<(FightPrepReader.FightChallenge Challenge, int Result)> FightChallenges(uint pid)
    {
        if (!_challenges.TryGetValue(pid, out var d)) return Array.Empty<(FightPrepReader.FightChallenge, int)>();
        _challengeResults.TryGetValue(pid, out var r);
        return d.Values.OrderBy(c => c.Id)
            .Select(c => (c, r is not null && r.TryGetValue(c.Id, out var ok) ? (ok ? 1 : 0) : -1))
            .ToList();
    }

    // ---------------------------------------------------------------- décodage (thread réseau, appelé par OnDecoded)
    // Appelé dans la branche MapComplementaryInformationEvent APRÈS le décodage synchrone des acteurs (le sondage de
    // sendMapRequest enchaîne sur WaitSelfReady : les acteurs frais sont déjà publiés).
    private void OnLotKMapInfo(DecodedPacket p) => _mapInfoSeq.AddOrUpdate(p.Pid, 1, static (_, v) => v + 1);

    private void OnLotKFightStarting(uint pid) => _challenges.TryRemove(pid, out _);

    private void OnLotKPacket(DecodedPacket p)
    {
        if (p.ClientToServer || p.Name is null) return;
        switch (p.Name)
        {
            case "ChallengeListEvent":
            {
                var d = new ConcurrentDictionary<int, FightPrepReader.FightChallenge>();
                foreach (var c in FightPrepReader.ReadChallengeList(p.Payload)) d[c.Id] = c;
                _challenges[p.Pid] = d;
                break;
            }
            case "ChallengeAddEvent":
            case "ChallengeTargetsEvent":
            {
                if (FightPrepReader.ReadChallengeCarrier(p.Name, p.Payload) is not { } ch) break;
                var d = _challenges.GetOrAdd(p.Pid, static _ => new());
                // TargetsEvent = mise à jour des cibles : on garde bonus/état déjà connus (proto3 : un champ omis se lirait 0).
                d[ch.Id] = p.Name == "ChallengeTargetsEvent" && d.TryGetValue(ch.Id, out var old) ? old with { Targets = ch.Targets } : ch;
                break;
            }
            case "ChallengeResultEvent":
                // L'enregistrement du résultat reste dans le bloc Lot C2 (OnDecoded) ; ici seulement la trace console.
                if (FightPrepReader.ReadChallengeResult(p.Payload) is { } res)
                {
                    TransportLog.Line($"[combat] pid {p.Pid} : défi {res.Id} {(res.Success ? "réussi" : "échoué")}");
                    ChallengeOutcomeStore.Record(CharacterId(p.Pid), ChallengeRuleOf(res.Id), res.Success);   // apprend ses vraies chances
                }
                break;
            case "PlayerStatusUpdatedEvent":
            {
                long who = PayloadReader.Int(p.Payload, FNum("PlayerStatusUpdatedEvent", "player_id"), 0);
                int fSt = FNum("PlayerStatusUpdatedEvent", "status");
                var sub = p.Payload.FirstOrDefault(f => f.Field == fSt && f.Message is not null)?.Message;
                if (who != 0 && who == CharacterId(p.Pid) && sub is not null)
                {
                    int fInner = FNum("CharacterStatus", "status");
                    int st = (int)(sub.FirstOrDefault(f => f.Field == fInner && f.Wire == WireType.Varint)?.Varint ?? 0);   // proto3 : absent = 0
                    _playerStatus[p.Pid] = st;
                    TransportLog.Line($"[statut] pid {p.Pid} : statut serveur = {st}");   // sert à CONFIRMER en jeu les valeurs 1/4
                }
                break;
            }
            case "PlayerStatusUpdateErrorEvent":
                TransportLog.Line($"[statut] pid {p.Pid} : changement de statut refusé par le serveur");
                break;
        }
    }

    private void ForgetLotK(uint pid)
    {
        _mapInfoSeq.TryRemove(pid, out _); _mapInfoReqAt.TryRemove(pid, out _); _playerStatus.TryRemove(pid, out _);
        _resetStatsAt.TryRemove(pid, out _); _challenges.TryRemove(pid, out _);
    }
}
