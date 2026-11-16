using System;
using System.Collections.Generic;

namespace GivreClone.App.Services;

/// <summary>
/// Décisions pures du Turbo « agressif » (profil Turbo + « Accuser les actions de tous les combattants » / « Passage de tour
/// agressif »), partagées par le transport Socket et le MITM. Calquées sur la recette validée côté Givre : accuser TOUTES les
/// séquences dès leur fin, « tour prêt » anticipé après le premier accusé de séquence de début de combat (type 8), réponse immédiate
/// au marqueur FightIsTurnReady, puis quelques relances espacées de 100 ms tant qu'aucun nouveau tour n'a démarré.
/// </summary>
public static class TurboFightPolicy
{
    public const long FightStartSequenceType = 8;
    public const int ReadyBurstRetries = 2;
    public const int ReadyBurstSpacingMs = 150;

    /// <summary>Accuser la séquence EXTERNE d'un AUTRE combattant : alliés, monstres et joueurs (author != 0), dès que ackAll est actif.</summary>
    public static bool ShouldAckForeignSequence(bool ackAll, long author, long selfId, long actionId)
        => ackAll && author != 0 && author != selfId && actionId != 0;

    /// <summary>« Tour prêt » anticipé : une seule fois par combat, juste après l'accusé de NOTRE séquence de début de combat, avant le 1er tour.</summary>
    public static bool ShouldSendEarlyReady(bool eager, long author, long selfId, long sequenceType, bool noTurnYet, bool alreadySent)
        => eager && selfId != 0 && author == selfId && sequenceType == FightStartSequenceType && noTurnYet && !alreadySent;

    /// <summary>Délai (ms) de la réponse « tour prêt » en mode agressif : calibré à 25-50 ms avec gigue pour éviter la détection burst Ankama.</summary>
    public static int EagerReadyDelayMs() => Random.Shared.Next(25, 55);

    /// <summary>Codes de messages serveur TextInformationEvent correspondant à un rejet d'action ou de tour (170-177).</summary>
    public static bool IsRejectionCode(int messageId) => messageId is >= 170 and <= 177;

    /// <summary>Délai initial (ms) du « tour prêt » adaptatif selon le niveau de backoff (0 = agressif, 1 = modéré, 2 = repli).</summary>
    public static int AdaptiveReadyDelayMs(int backoffLevel) => backoffLevel switch
    {
        >= 2 => Random.Shared.Next(75, 126),
        1 => Random.Shared.Next(45, 81),
        _ => EagerReadyDelayMs()
    };

    /// <summary>Nombre de relances maximal selon le niveau de backoff actuel. 8 relances couvraient ~1,8 s et ont
    /// ramene l'inter-tour ennemi de 3 900 a 1 784 ms, mais ajoutaient 16 requetes/s au debit deja charge par les
    /// accuses : Bastionbot a repris deux bans temporaires (09:47 et 10:42). 5 relances couvrent toujours ~1,1 s, la
    /// ou le timeout serveur d'inactivite est a ~3,3 s.</summary>
    public static int AdaptiveBurstRetries(int backoffLevel) => backoffLevel switch
    {
        >= 2 => 2,
        1 => 3,
        _ => 5
    };

    /// <summary>Espacement progressif (ms) avec gigue pour la relance retryIndex (1-indexed) selon le niveau de backoff.</summary>
    public static int AdaptiveBurstSpacingMs(int retryIndex, int backoffLevel) => backoffLevel switch
    {
        >= 2 => Random.Shared.Next(250, 381),
        1 => Random.Shared.Next(160, 241),
        _ => retryIndex switch
        {
            <= 2 => Random.Shared.Next(85, 126),
            <= 4 => Random.Shared.Next(125, 176),
            <= 6 => Random.Shared.Next(175, 236),
            _ => Random.Shared.Next(235, 321)
        }
    };

    /// <summary>Micro-délai pour les accusés de nos séquences (anti-burst adaptatif).</summary>
    public static int AdaptiveOwnAckDelayMs(int backoffLevel) => backoffLevel switch
    {
        >= 2 => Random.Shared.Next(40, 71),
        1 => Random.Shared.Next(25, 51),
        _ => Random.Shared.Next(15, 36)
    };

    /// <summary>Micro-délai pour les accusés de séquences étrangères.</summary>
    public static int AdaptiveForeignAckDelayMs(int backoffLevel) => backoffLevel switch
    {
        >= 2 => Random.Shared.Next(25, 46),
        1 => Random.Shared.Next(18, 31),
        _ => Random.Shared.Next(10, 21)
    };

    /// <summary>Report maximal (ms) d'un accuse etranger : au-dela le serveur considere l'action non accusee.</summary>
    public const int MaxAckDeferralMs = 900;

    /// <summary>Report maximal (ms) d'un accuse de NOTRE sequence : la barriere Turbo l'attend, donc il reste court.</summary>
    public const int MaxOwnAckDeferralMs = 60;

    /// <summary>Espacement MINIMAL soutenu (ms) entre deux accuses, soit ~55/s en agressif contre les 85-87/s mesures
    /// avant le ban temporaire de Bastionbot, ~28/s en modere et ~16/s en repli.
    /// Calibre sur deux mesures opposees : a 30 ms (plafond ~33/s) les accuses etrangers partaient avec 165 ms de
    /// retard moyen et le tour ennemi coutait 258 ms par sequence, contre 218 ms sans cadence du tout (+18 %). A 18 ms
    /// le retard retombe pres du niveau d'origine en restant largement sous le seuil de rejet serveur.</summary>
    public static int SustainedAckSpacingMs(int backoffLevel) => backoffLevel switch
    {
        >= 2 => 60,
        1 => 35,
        _ => 18
    };

    /// <summary>Horodatage d'une requête Turbo, STRICTEMENT postérieur au dernier événement de séquence/accusé déjà traité.
    /// Horloge à la milliseconde : la requête suivante partait dans la même ms que la fin + l'accusé de la précédente, et la
    /// barrière (≥) prenait cette ancienne séquence pour la nouvelle -> fin de tour injectée pendant la séquence, ignorée par le
    /// serveur, tour perdu jusqu'au chrono (55 s, Socket, 23/10 23:32:48).</summary>
    public static long RequestStamp(long now, long lastEventAt) => lastEventAt >= now ? lastEventAt + 1 : now;

    /// <summary>
    /// Barrière Turbo : l'action suivante peut partir quand NOTRE séquence externe ouverte après la requête est finie (du bon type :
    /// sort -> Spell/Weapon, déplacement -> Move) et accusée. Socket : le bot est le client, la fin suffit. MITM avec accusés du bot :
    /// l'action doit être accusée (par le bot ou par le client visible, dans n'importe quel ordre). MITM sans : l'accusé du client
    /// visible, postérieur à la fin de séquence.
    /// </summary>
    public static bool BarrierClosed(long requestAt, (long ActionId, long At, long StartAt)? selfSeqEnd, long seqType, char requestKind,
                                     bool socket, bool botAcks, long? ackedAt, (long ActionId, long At)? lastClientAck)
    {
        if (requestAt <= 0 || selfSeqEnd is not { } seq || seq.ActionId == 0 || seq.At < requestAt || seq.StartAt < requestAt) return false;
        if (seqType >= 0 && requestKind == 'c' && seqType != 0 && seqType != 1) return false;   // sort -> séquence Spell/Weapon
        if (seqType >= 0 && requestKind == 'm' && seqType != 4) return false;                   // déplacement -> séquence Move
        if (socket) return true;
        if (botAcks) return ackedAt is long currentAck && currentAck >= Math.Max(requestAt, seq.StartAt);
        return lastClientAck is { } ack && ack.ActionId == seq.ActionId && ack.At >= seq.At;
    }
}

/// <summary>
/// Turbo MITM : chaque confirmation de protocole part UNE seule fois vers le serveur, comme avec le vrai client (et en Socket).
/// Le bot répond avant le client visible (décodage avant relais) ; ce registre mémorise ce que le bot a déjà envoyé pour retirer
/// du flux montant les copies du client visible. Pur et thread-safe : testable sans jeu.
/// <list type="bullet">
/// <item>« Tour prêt » : un seul par marqueur FightIsTurnReady, bot OU client (le premier gagne). Le « tour prêt » anticipé du début
/// de combat est compté à part (une fois par combat) : la copie du client visible est retirée, et il survit au premier marqueur.</item>
/// <item>Accusé : un seul par séquence externe et par action_id, pour nos séquences comme pour celles des autres combattants.
/// Les action_id sont réutilisés d'une séquence à l'autre : chaque accusé du bot absorbe exactement UN accusé du client, et un
/// accusé du bot dont le client ne répond jamais (séquence d'un autre combattant) expire vite pour ne jamais manger l'accusé
/// d'une séquence suivante.</item>
/// </list>
/// </summary>
public sealed class TurboUpstreamDedupe
{
    /// <summary>Durée (ms) pendant laquelle une confirmation de NOTRE séquence / le « tour prêt » du bot attend la copie du client visible.</summary>
    public const long ClientCopyGraceMs = 10_000;
    /// <summary>Idem pour l'accusé d'une séquence d'un AUTRE combattant : le vrai client ne les accuse pas, la copie est l'exception.</summary>
    public const long ForeignCopyGraceMs = 2_000;

    private readonly object _gate = new();
    private readonly Dictionary<(uint Pid, long ActionId), (int Count, long ExpiresAt)> _botAcks = new();
    private readonly Dictionary<uint, (int EarlyPending, long EarlyExpiresAt, bool MarkerSent)> _ready = new();

    /// <summary>Vrai tant qu'au moins un accusé du bot attend sa copie client (évite d'analyser le flux montant pour rien).</summary>
    public bool HasPendingAcks { get { lock (_gate) return _botAcks.Count > 0; } }

    /// <summary>Le bot s'apprête à envoyer l'accusé de cette action (AVANT l'injection ; <see cref="UndoBotAck"/> si elle échoue).</summary>
    public void NoteBotAck(uint pid, long actionId, long now, bool foreign = false)
    {
        if (actionId == 0) return;
        long expires = now + (foreign ? ForeignCopyGraceMs : ClientCopyGraceMs);
        lock (_gate)
        {
            _botAcks.TryGetValue((pid, actionId), out var e);
            _botAcks[(pid, actionId)] = (e.Count + 1, System.Math.Max(e.ExpiresAt, expires));
        }
    }

    public void UndoBotAck(uint pid, long actionId)
    {
        lock (_gate)
        {
            if (!_botAcks.TryGetValue((pid, actionId), out var e)) return;
            if (e.Count <= 1) _botAcks.Remove((pid, actionId)); else _botAcks[(pid, actionId)] = (e.Count - 1, e.ExpiresAt);
        }
    }

    /// <summary>Accusé du client visible : à retirer du flux montant si le bot a déjà accusé cette action (consomme UN accusé du bot).</summary>
    public bool ShouldDropClientAck(uint pid, long actionId, long now)
    {
        lock (_gate)
        {
            if (!_botAcks.TryGetValue((pid, actionId), out var e)) return false;
            if (now > e.ExpiresAt) { _botAcks.Remove((pid, actionId)); return false; }
            if (e.Count <= 1) _botAcks.Remove((pid, actionId)); else _botAcks[(pid, actionId)] = (e.Count - 1, e.ExpiresAt);
            return true;
        }
    }

    /// <summary>Nouveau marqueur FightIsTurnReady du serveur : aucun « tour prêt » n'a encore été envoyé pour lui.</summary>
    public void NoteReadyMarker(uint pid)
    {
        lock (_gate) { _ready.TryGetValue(pid, out var r); _ready[pid] = (r.EarlyPending, r.EarlyExpiresAt, false); }
    }

    /// <summary>Le bot peut-il envoyer son « tour prêt » ? Marqueur : non si ce marqueur a déjà reçu sa réponse (bot ou client).
    /// Anticipé (début de combat, déjà limité à une fois par combat) : toujours. Si oui, à injecter (<see cref="UndoBotReady"/> si ça échoue).</summary>
    public bool TryBeginBotReady(uint pid, long now, bool early = false)
    {
        lock (_gate)
        {
            _ready.TryGetValue(pid, out var r);
            if (early) { _ready[pid] = (r.EarlyPending + 1, now + ClientCopyGraceMs, r.MarkerSent); return true; }
            if (r.MarkerSent) return false;
            _ready[pid] = (r.EarlyPending, r.EarlyExpiresAt, true);
            return true;
        }
    }

    public void UndoBotReady(uint pid, bool early = false)
    {
        lock (_gate)
        {
            if (!_ready.TryGetValue(pid, out var r)) return;
            _ready[pid] = early ? (System.Math.Max(0, r.EarlyPending - 1), r.EarlyExpiresAt, r.MarkerSent) : (r.EarlyPending, r.EarlyExpiresAt, false);
        }
    }

    /// <summary>« Tour prêt » du client visible : retiré s'il double celui du bot (anticipé ou de ce marqueur) ou un autre du client pour
    /// ce marqueur ; sinon transmis, et il est alors le seul pour ce marqueur.</summary>
    public bool ShouldDropClientReady(uint pid, long now)
    {
        lock (_gate)
        {
            _ready.TryGetValue(pid, out var r);
            if (r.EarlyPending > 0 && now <= r.EarlyExpiresAt) { _ready[pid] = (r.EarlyPending - 1, r.EarlyExpiresAt, r.MarkerSent); return true; }
            if (r.MarkerSent) return true;
            _ready[pid] = (0, 0, true);
            return false;
        }
    }

    public void Reset(uint pid)
    {
        lock (_gate)
        {
            _ready.Remove(pid);
            foreach (var k in new List<(uint Pid, long ActionId)>(_botAcks.Keys))
                if (k.Pid == pid) _botAcks.Remove(k);
        }
    }
}

/// <summary>
/// Gestionnaire d'état adaptatif pour le passage de tour et les accusés Turbo.
/// Maintient un niveau de repli (backoff) : agressif et rapide lorsque le serveur répond promptement,
/// temporise et réduit le nombre de relances dès l'apparition de rejets (codes 171-177) ou de forte latence.
/// </summary>
public sealed class AdaptiveReadyPolicy
{
    public const int MaxBackoffLevel = 2;
    public const int CleanTurnsToRecover = 2;

    private readonly object _gate = new();
    private int _backoffLevel;
    private int _cleanTurns;
    private long _lastRejectionAt;
    private int _lastRejectionCode;
    private long _lastReadySentAt;
    // Cadence SOUTENUE des accuses : la gigue par message ne borne que l'espacement d'UNE paire. Sur un combat a
    // plusieurs monstres les sequences arrivent en rafale et 10-21 ms par accuse autorisait 85-87 accuses/s (mesure
    // Bastionbot 09:45:13 et 09:46:43, suivie d'une fermeture serveur puis d'un ban temporaire de 5 min). On date le
    // prochain envoi autorise pour tenir la cadence plafond, sans jamais rendre un delai negatif.
    private long _nextAckSlotAt;

    public int BackoffLevel { get { lock (_gate) return _backoffLevel; } }
    public int CleanTurns { get { lock (_gate) return _cleanTurns; } }
    public long LastRejectionAt { get { lock (_gate) return _lastRejectionAt; } }
    public int LastRejectionCode { get { lock (_gate) return _lastRejectionCode; } }

    public void NoteRejection(int messageId, long now)
    {
        if (TurboFightPolicy.IsRejectionCode(messageId))
        {
            lock (_gate)
            {
                _backoffLevel = MaxBackoffLevel;
                _cleanTurns = 0;
                _lastRejectionAt = now;
                _lastRejectionCode = messageId;
            }
        }
    }

    public void NoteReadySent(long now)
    {
        lock (_gate)
        {
            _lastReadySentAt = now;
        }
    }

    public void NoteTurnStart(long now)
    {
        lock (_gate)
        {
            long latency = _lastReadySentAt > 0 ? Math.Max(0, now - _lastReadySentAt) : 0;
            _lastReadySentAt = 0;

            if (_backoffLevel > 0)
            {
                _cleanTurns++;
                if (_cleanTurns >= CleanTurnsToRecover)
                {
                    _backoffLevel = Math.Max(0, _backoffLevel - 1);
                    _cleanTurns = 0;
                }
            }
            else if (latency > 1500)
            {
                _backoffLevel = 1;
                _cleanTurns = 0;
            }
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _backoffLevel = 0;
            _cleanTurns = 0;
            _lastRejectionAt = 0;
            _lastRejectionCode = 0;
            _lastReadySentAt = 0;
            _nextAckSlotAt = 0;
        }
    }

    public int GetBurstRetries()
    {
        lock (_gate) return TurboFightPolicy.AdaptiveBurstRetries(_backoffLevel);
    }

    public int GetInitialDelayMs()
    {
        lock (_gate) return TurboFightPolicy.AdaptiveReadyDelayMs(_backoffLevel);
    }

    public int GetBurstSpacingMs(int retryIndex)
    {
        lock (_gate) return TurboFightPolicy.AdaptiveBurstSpacingMs(retryIndex, _backoffLevel);
    }

    public int GetOwnAckDelayMs()
    {
        lock (_gate) return TurboFightPolicy.AdaptiveOwnAckDelayMs(_backoffLevel);
    }

    /// <summary>Gigue brute (ms) d'un accuse etranger, sans cadence : espacement d'une paire isolee.</summary>
    public int GetForeignAckDelayMs()
    {
        lock (_gate) return TurboFightPolicy.AdaptiveForeignAckDelayMs(_backoffLevel);
    }

    /// <summary>Delai (ms) a appliquer au prochain accuse etranger d'une RAFALE, ou -1 pour l'ABANDONNER : la gigue,
    /// repoussee tant que la cadence soutenue de <see cref="TurboFightPolicy.SustainedAckSpacingMs"/> n'est pas
    /// respectee. Au-dela de <see cref="TurboFightPolicy.MaxAckDeferralMs"/> l'accuse n'a plus de valeur pour le
    /// serveur : on le laisse tomber plutot que de le renvoyer sans espacement (le vrai client n'accuse de toute
    /// facon aucune sequence etrangere, 0/242 sur la capture de reference).</summary>
    public int NextForeignAckDelayMs(long now) => ReserveAckSlot(now, droppable: true);

    /// <summary>Delai (ms) du prochain accuse de NOTRE sequence : sa gigue habituelle, JAMAIS retardee (la barriere
    /// Turbo l'attend, le differer freinerait le tour). Il consomme en revanche un creneau de la cadence partagee,
    /// ce qui repousse les accuses etrangers suivants : le serveur compte le TOTAL d'accuses, et deux compteurs
    /// separes laissaient encore passer 57-60 accuses/s (mesure du 09/11 10:18-10:21).</summary>
    public int NextOwnAckDelayMs(long now)
    {
        lock (_gate)
        {
            int jitter = TurboFightPolicy.AdaptiveOwnAckDelayMs(_backoffLevel);
            // Nos accuses respectent le creneau partage, mais avec un report TRES court : les laisser passer devant sans
            // attendre portait le total a 69 accuses/s (trames masquees incluses, pic de Shadowbot a 13:01), alors que la
            // cadence seule plafonne a ~55/s. Les differer de 900 ms comme les etrangers freinait le tour (258 ms par
            // sequence ennemie contre 218), d'ou ce plafond de report court.
            long earliest = Math.Max(now + jitter, _nextAckSlotAt);
            int delay = (int)Math.Min(earliest - now, TurboFightPolicy.MaxOwnAckDeferralMs);
            // Le creneau partage n'est JAMAIS rembobine : ecreter notre report a 60 ms ne doit pas remettre la file des
            // accuses etrangers au present, sinon la cadence disparait des qu'un de nos accuses passe devant.
            _nextAckSlotAt = Math.Max(earliest, now + delay + TurboFightPolicy.SustainedAckSpacingMs(_backoffLevel));
            return delay;
        }
    }

    private int ReserveAckSlot(long now, bool droppable)
    {
        lock (_gate)
        {
            int jitter = TurboFightPolicy.AdaptiveForeignAckDelayMs(_backoffLevel);
            long earliest = Math.Max(now + jitter, _nextAckSlotAt);
            long delay = earliest - now;
            if (delay > TurboFightPolicy.MaxAckDeferralMs) return -1;
            _nextAckSlotAt = earliest + TurboFightPolicy.SustainedAckSpacingMs(_backoffLevel);
            return (int)delay;
        }
    }
}

