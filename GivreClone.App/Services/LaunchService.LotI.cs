using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

// ===================== Lot I : quêtes, sous-zones, équipement =====================
// Écrit par les décodeurs (thread réseau) dans des ConcurrentDictionary aux valeurs IMMUABLES (records remplacés d'un bloc) ;
// lu par le thread du script (quest:/map:/inventory:) et l'UI. Envois SYNCHRONES depuis le thread du script (relay.Inject),
// confirmation par sondage (WaitUntil côté hôte) sur les horodatages ci-dessous — aucune TCS, aucun _codeWait.
public sealed partial class LaunchService
{
    // ---------------------------------------------------------------- état des quêtes
    // Quêtes TERMINÉES : ensemble STRICT (QuestFinished.quest_id + QuestValidatedEvent) et ensemble LEGACY (#2 de QuestFinished,
    // lu par l'ancien code sous un nom périmé : fpci non résolu). CriterionMet garde l'UNION des deux (comportement de voyage
    // inchangé) ; quest:isFinished n'utilise que le strict.
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<int, byte>> _finishedQuests = new(); // quest_id stricts
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<int, byte>> _finishedLegacy = new(); // #2 legacy (CriterionMet seul)
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<int, QuestReader.Quest>> _activeQuests = new();
    private readonly ConcurrentDictionary<uint, byte> _questsLoaded = new();
    private readonly ConcurrentDictionary<(uint Pid, int Quest), long> _questInfoTick = new();      // dernier QuestStepInformationEvent
    private readonly ConcurrentDictionary<(uint Pid, int Quest), long> _questProgressTick = new();  // StepValidated / Validated
    private readonly ConcurrentDictionary<(uint Pid, int Quest, int Obj), long> _questObjTick = new();
    private readonly ConcurrentDictionary<uint, long> _objectErrorTick = new();
    // ---------------------------------------------------------------- sous-zones
    private readonly ConcurrentDictionary<uint, int> _subArea = new();   // sous-zone LIVE (MapComplementaryInformationEvent.subarea_id)
    private volatile IReadOnlyDictionary<long, int>? _mapSubArea;       // table statique map_id -> subAreaId (extraction des cartes)

    /// <summary>Le critère (m_criterion) d'une arête conditionnelle est-il PROUVÉ satisfait pour ce perso ? Opérateurs
    /// interprétés : Qf (quête terminée, ensemble strict ∪ legacy) et MI (état d'interactif, via <paramref name="interactiveState"/>
    /// = état LIVE sur la carte courante) ; tout le reste -> false (conservateur). Voir <see cref="QuestCriteria"/>.</summary>
    public bool CriterionMet(uint pid, string criterion, Func<int, int?>? interactiveState = null)
    {
        _finishedQuests.TryGetValue(pid, out var strict);
        _finishedLegacy.TryGetValue(pid, out var legacy);
        return QuestCriteria.Eval(criterion, id => (strict is not null && strict.ContainsKey(id)) || (legacy is not null && legacy.ContainsKey(id)),
                                  interactiveState);
    }

    private static ConcurrentDictionary<int, byte> IdSet(IEnumerable<int> ids)
    {
        var d = new ConcurrentDictionary<int, byte>();
        foreach (var i in ids) d[i] = 1;
        return d;
    }

    // Snapshot QuestsEvent : REMPLACE les ensembles de quêtes finies (si lisibles) et la liste des quêtes en cours.
    private void OnQuestsSnapshot(uint pid, IReadOnlyList<ProtoField> payload)
    {
        var s = QuestReader.ReadQuests(payload);
        long me = CharacterId(pid);
        if (s.PlayerId != 0 && me != 0 && s.PlayerId != me)           // snapshot d'un autre joueur (tracé : diagnostic si player_id ≠ id perso)
        {
            TransportLog.Line($"[quetes] pid {pid} : liste de quêtes d'un autre joueur ({s.PlayerId}) ignorée");
            return;
        }
        // Identité confirmée (player_id = id du perso) : une liste vide est VALIDE (perso sans quête finie, ex. après changement
        // de perso sur le même client) -> on remplace. Sinon « rien de lisible » -> on garde l'existant (décodage raté).
        bool identityConfirmed = s.PlayerId != 0 && me != 0 && s.PlayerId == me;
        if (identityConfirmed || s.Finished.Count + s.LegacyExtra.Count > 0)
        {
            _finishedQuests[pid] = IdSet(s.Finished);
            _finishedLegacy[pid] = IdSet(s.LegacyExtra);
        }
        var act = new ConcurrentDictionary<int, QuestReader.Quest>();
        foreach (var q in s.Active) act[q.Id] = q;
        _activeQuests[pid] = act;                                       // liste vide = état valide
        _questsLoaded[pid] = 1;
        bool has230 = s.Finished.Contains(230) || s.LegacyExtra.Contains(230);
        TransportLog.Line($"[quetes] pid {pid} : {s.Finished.Count} terminée(s), {s.Active.Count} en cours"
            + (has230 ? " (dont 230)" : ""));   // 230 = repère de validation du pont Qf=230
    }

    // QuestValidatedEvent : quête terminée -> ensemble strict, retirée des quêtes en cours.
    private void OnQuestValidated(uint pid, IReadOnlyList<ProtoField> payload)
    {
        int qid = QuestReader.ReadQuestId(payload, "QuestValidatedEvent");
        if (qid <= 0) return;
        _finishedQuests.GetOrAdd(pid, _ => new ConcurrentDictionary<int, byte>())[qid] = 1;
        if (_activeQuests.TryGetValue(pid, out var act)) act.TryRemove(qid, out _);
        _questProgressTick[(pid, qid)] = Environment.TickCount64;
        TransportLog.Line($"[quetes] pid {pid} : quête {qid} terminée (QuestValidatedEvent)");
    }

    private ConcurrentDictionary<int, QuestReader.Quest> Active(uint pid) => _activeQuests.GetOrAdd(pid, static _ => new());

    // Fusion : on n'écrase JAMAIS des détails connus par un enregistrement sans détails.
    private void UpsertQuest(uint pid, QuestReader.Quest q)
    {
        var act = Active(pid);
        if (!q.HasDetails && act.TryGetValue(q.Id, out var old) && old.HasDetails) return;
        act[q.Id] = q;
    }

    /// <summary>Événements du Lot I (serveur -&gt; client) : quêtes (incrémental) + erreur d'objet.</summary>
    private void OnLotIPacket(DecodedPacket p)
    {
        if (p.ClientToServer || p.Name is null) return;
        long now = Environment.TickCount64;
        switch (p.Name)
        {
            case "QuestsEvent": OnQuestsSnapshot(p.Pid, p.Payload); break;
            case "QuestValidatedEvent": OnQuestValidated(p.Pid, p.Payload); break;
            case "QuestStartedEvent":
            {
                int q = QuestReader.ReadQuestId(p.Payload, "QuestStartedEvent");
                if (q > 0) Active(p.Pid).TryAdd(q, new QuestReader.Quest(q, 0, Array.Empty<QuestReader.Objective>(), false));
                break;
            }
            case "QuestStepStartedEvent":
            {
                var (q, step) = QuestReader.ReadStepEvent(p.Payload, "QuestStepStartedEvent");
                // Nouvelle étape : ses objectifs sont inconnus tant qu'un QuestStepInformationEvent ne les a pas détaillés.
                if (q > 0) Active(p.Pid)[q] = new QuestReader.Quest(q, step, Array.Empty<QuestReader.Objective>(), false);
                break;
            }
            case "QuestStepValidatedEvent":
            {
                var (q, _) = QuestReader.ReadStepEvent(p.Payload, "QuestStepValidatedEvent");
                if (q > 0) _questProgressTick[(p.Pid, q)] = now;
                break;
            }
            case "QuestObjectiveValidatedEvent":
            {
                var (q, o) = QuestReader.ReadObjectiveValidated(p.Payload);
                if (q <= 0 || o <= 0) break;
                var act = Active(p.Pid);
                if (act.TryGetValue(q, out var old))
                {
                    var objs = old.Objectives.ToList();
                    int i = objs.FindIndex(x => x.Id == o);
                    if (i >= 0) objs[i] = objs[i] with { Reached = true };
                    else objs.Add(new QuestReader.Objective(o, true, 0, 0));
                    act[q] = old with { Objectives = objs };
                }
                _questObjTick[(p.Pid, q, o)] = now;
                break;
            }
            case "QuestStepInformationEvent":
            {
                var (player, info) = QuestReader.ReadStepInformation(p.Payload);
                long me = CharacterId(p.Pid);
                if (player != 0 && me != 0 && player != me) break;   // information d'un autre joueur
                if (info is null) break;
                UpsertQuest(p.Pid, info);
                _questInfoTick[(p.Pid, info.Id)] = now;
                break;
            }
            case "QuestsFollowedEvent":
                foreach (var q in QuestReader.ReadFollowed(p.Payload))
                    if (q.HasDetails) UpsertQuest(p.Pid, q);
                break;
            case "ObjectErrorEvent":
                _objectErrorTick[p.Pid] = now;
                break;
        }
    }

    // ---------------------------------------------------------------- lectures publiques (thread-safe, sans gate)
    public bool QuestsLoaded(uint pid) => _questsLoaded.ContainsKey(pid);
    public bool IsQuestFinished(uint pid, int questId) => _finishedQuests.TryGetValue(pid, out var s) && s.ContainsKey(questId);
    public IReadOnlyList<int> FinishedQuests(uint pid)
        => _finishedQuests.TryGetValue(pid, out var s) ? s.Keys.OrderBy(x => x).ToList() : Array.Empty<int>();
    public IReadOnlyList<QuestReader.Quest> ActiveQuests(uint pid)
        => _activeQuests.TryGetValue(pid, out var a) ? a.Values.OrderBy(q => q.Id).ToList() : Array.Empty<QuestReader.Quest>();
    public QuestReader.Quest? ActiveQuest(uint pid, int questId)
        => _activeQuests.TryGetValue(pid, out var a) && a.TryGetValue(questId, out var q) ? q : null;
    /// <summary>Objectif confirmé depuis t0 : QuestObjectiveValidatedEvent(q,o), ou QuestStepValidated/QuestValidated(q).</summary>
    public bool QuestObjectiveConfirmedSince(uint pid, int q, int o, long t0)
        => (_questObjTick.TryGetValue((pid, q, o), out var t) && t >= t0) || (_questProgressTick.TryGetValue((pid, q), out var t2) && t2 >= t0);
    public bool QuestInfoSince(uint pid, int q, long t0) => _questInfoTick.TryGetValue((pid, q), out var t) && t >= t0;
    public bool ObjectErrorSince(uint pid, long t0) => _objectErrorTick.TryGetValue(pid, out var t) && t >= t0;

    // ---------------------------------------------------------------- envois (thread du script, synchrones)
    /// <summary>QuestObjectiveValidationRequest (idm). Gate Perm.Gather (socle des actions hors combat). true si injecté.</summary>
    public bool SendQuestObjectiveValidation(uint pid, int questId, int objectiveId)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        var relay = RelayForInject();
        if (relay is null || questId <= 0 || objectiveId <= 0) return false;
        byte[] pkt = MoveForge.BuildQuestObjectiveValidationFramed(questId, objectiveId);
        if (pkt.Length == 0) { TransportLog.Line($"[quetes] pid {pid} : validation d'objectif indisponible sur ce patch -> rien envoyé"); return false; }
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[quetes] pid {pid} : QuestObjectiveValidationRequest quête {questId} objectif {objectiveId} inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    /// <summary>QuestStepInformationRequest (idy) : demande d'information, sans effet de jeu -> pas de gate. true si injecté.</summary>
    public bool SendQuestStepInformation(uint pid, int questId)
    {
        var relay = RelayForInject();
        if (relay is null || questId <= 0) return false;
        byte[] pkt = MoveForge.BuildQuestStepInformationFramed(questId);
        if (pkt.Length == 0) { TransportLog.Line($"[quetes] pid {pid} : demande d'étape indisponible sur ce patch -> rien envoyé"); return false; }
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[quetes] pid {pid} : QuestStepInformationRequest quête {questId} inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    /// <summary>ObjectSetPositionRequest (isz) : équipe (0..15) / déséquipe (63) un objet par UID. Gate Perm.Gather ; refusé en
    /// combat. true si injecté (la confirmation arrive par ObjectMovementEvent / ObjectErrorEvent).</summary>
    public bool SendObjectSetPosition(uint pid, int uid, int position)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        if (IsInFight(pid)) { TransportLog.Line($"[inventaire] pid {pid} : équipement impossible en combat"); return false; }
        var relay = RelayForInject();
        if (relay is null || uid == 0) return false;
        byte[] pkt = MoveForge.BuildObjectSetPositionFramed(uid, position, 1);
        if (pkt.Length == 0) { TransportLog.Line($"[inventaire] pid {pid} : changement d'équipement indisponible sur ce patch -> rien envoyé"); return false; }
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[inventaire] pid {pid} : ObjectSetPositionRequest uid {uid} -> position {position} inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    // ---------------------------------------------------------------- sous-zones
    // Sous-zone LIVE lue sur MapComplementaryInformationEvent (champ résolu PAR NOM ; non résolu = non lu). Absente/0 -> on oublie
    // la valeur live (repli sur la table statique) plutôt que de garder celle de la carte précédente.
    private void OnMapSubArea(uint pid, IReadOnlyList<ProtoField> payload)
    {
        if (_catalog?.FieldNumber("MapComplementaryInformationEvent", "subarea_id") is not int sf) return;
        int sa = (int)PayloadReader.Int(payload, sf, 0);
        if (sa > 0) _subArea[pid] = sa; else _subArea.TryRemove(pid, out _);
    }

    /// <summary>Sous-zone d'une carte d'après la table statique (extraction des cartes du client), 0 si inconnue.</summary>
    public int SubAreaOf(long mapId)
        => mapId > 0 && _mapSubArea is { } t && t.TryGetValue(mapId, out var sa) ? sa : 0;

    /// <summary>Sous-zone courante du perso : valeur live si connue, sinon table statique de la carte courante. 0 = inconnue.</summary>
    public int CurrentSubArea(uint pid)
        => _subArea.TryGetValue(pid, out var sa) && sa > 0 ? sa : SubAreaOf(CurrentMap(pid) ?? 0);

    private void ForgetLotI(uint pid)
    {
        _finishedQuests.TryRemove(pid, out _); _finishedLegacy.TryRemove(pid, out _);
        _activeQuests.TryRemove(pid, out _); _questsLoaded.TryRemove(pid, out _);
        _objectErrorTick.TryRemove(pid, out _); _subArea.TryRemove(pid, out _);
        foreach (var k in _questInfoTick.Keys) if (k.Pid == pid) _questInfoTick.TryRemove(k, out _);
        foreach (var k in _questProgressTick.Keys) if (k.Pid == pid) _questProgressTick.TryRemove(k, out _);
        foreach (var k in _questObjTick.Keys) if (k.Pid == pid) _questObjTick.TryRemove(k, out _);
    }
}
