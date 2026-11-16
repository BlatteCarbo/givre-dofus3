using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

// ===================== Chasse au trésor — état décodé PAR NOM + réponses serveur =====================
// Thread réseau : SnowObserveTreasure (depuis SnowObserve) ne fait qu'écrire des enregistrements immuables. Thread script : lectures
// et envois (flag / dig / giveUp), gatés Perm.Gather au point d'action (une chasse = activité hors combat du compte).
public sealed partial class LaunchService
{
    /// <summary>Étape connue : Kind = "poi" (indice à trouver), "hint" (PNJ indice « phorreur »), "direction" (N cartes dans
    /// une direction), "fight" (combat final), "dig" (creuser). Direction = 0 droite, 2 bas, 4 gauche, 6 haut.</summary>
    public sealed record TreasureStep(string Kind, int Direction, int NpcId, int PoiLabelId, int MapCount);
    public sealed record TreasureFlag(long MapId, int State);
    public sealed record TreasureHunt(int QuestType, long StartMapId, int TotalStepCount, int CurrentCheckPoint, int TotalCheckPoint,
                                      int AvailableRetryCount, IReadOnlyList<TreasureFlag> Flags, IReadOnlyList<TreasureStep> Steps, long Seq);
    public readonly record struct TreasureAnswer(int Result, int Extra, long Seq);
    public sealed record TextInfo(int MessageId, int MessageType, IReadOnlyList<string> Params, long Seq);

    private long _snowSeq;
    private readonly ConcurrentDictionary<uint, TreasureHunt> _treasure = new();
    private readonly ConcurrentDictionary<uint, byte> _legendaryTreasure = new();
    private readonly ConcurrentDictionary<uint, TreasureAnswer> _treasureAnswer = new();     // TreasureHuntAnswerEvent (démarrage)
    private readonly ConcurrentDictionary<uint, TreasureAnswer> _treasureDig = new();        // TreasureHuntDigAnswerEvent (Extra = drapeaux faux)
    private readonly ConcurrentDictionary<uint, TreasureAnswer> _treasureFlagAns = new();    // TreasureHuntFlagAnswerEvent (Extra = index)
    private readonly ConcurrentDictionary<uint, long> _treasureFinished = new();             // seq du dernier TreasureHuntFinishedEvent
    private readonly ConcurrentDictionary<uint, TextInfo> _lastTextInfo = new();

    /// <summary>Numéro de séquence global des événements observés (pour attendre « quelque chose de NOUVEAU depuis »).</summary>
    public long SnowSeq => Interlocked.Read(ref _snowSeq);
    public TreasureHunt? Treasure(uint pid) => _treasure.TryGetValue(pid, out var t) ? t : null;
    public TreasureAnswer? TreasureStartAnswer(uint pid) => _treasureAnswer.TryGetValue(pid, out var a) ? a : null;
    public TreasureAnswer? TreasureDigAnswer(uint pid) => _treasureDig.TryGetValue(pid, out var a) ? a : null;
    public TreasureAnswer? TreasureFlagAnswer(uint pid) => _treasureFlagAns.TryGetValue(pid, out var a) ? a : null;
    public long TreasureFinishedSeq(uint pid) => _treasureFinished.TryGetValue(pid, out var s) ? s : 0;
    /// <summary>Dernier TextInformationEvent (message système : délai de chasse, erreurs…), ou null.</summary>
    public TextInfo? LastTextInfo(uint pid) => _lastTextInfo.TryGetValue(pid, out var t) ? t : null;

    private void SnowObserveTreasure(DecodedPacket p, string name)
    {
        switch (name)
        {
            case "TreasureHuntEvent": _treasure[p.Pid] = ReadTreasure(p.Payload, Interlocked.Increment(ref _snowSeq)); break;
            case "TreasureHuntFinishedEvent":
                _treasure.TryRemove(p.Pid, out _); _legendaryTreasure.TryRemove(p.Pid, out _);
                _treasureFinished[p.Pid] = Interlocked.Increment(ref _snowSeq); break;
            case "TreasureHuntAnswerEvent":
                _treasureAnswer[p.Pid] = new TreasureAnswer((int)PayloadReader.Int(p.Payload, FNum("TreasureHuntAnswerEvent", "result"), 0), 0, Interlocked.Increment(ref _snowSeq));
                break;
            case "TreasureHuntDigAnswerEvent":
                _treasureDig[p.Pid] = new TreasureAnswer((int)PayloadReader.Int(p.Payload, FNum("TreasureHuntDigAnswerEvent", "result"), 0),
                    (int)PayloadReader.Int(p.Payload, FNum("TreasureHuntDigAnswerEvent", "wrong_flag_count"), 0), Interlocked.Increment(ref _snowSeq));
                break;
            case "TreasureHuntFlagAnswerEvent":
                _treasureFlagAns[p.Pid] = new TreasureAnswer((int)PayloadReader.Int(p.Payload, FNum("TreasureHuntFlagAnswerEvent", "result"), 0),
                    (int)PayloadReader.Int(p.Payload, FNum("TreasureHuntFlagAnswerEvent", "index"), 0), Interlocked.Increment(ref _snowSeq));
                break;
            case "TextInformationEvent":
            {
                int parF = FNum("TextInformationEvent", "parameters");
                var pars = new List<string>();
                foreach (var f in p.Payload)
                    if (f.Field == parF && f.Wire == WireType.Len)
                    {
                        string? s = f.String;
                        if (s is null && f.Bytes is { } b) { try { s = System.Text.Encoding.UTF8.GetString(b); } catch { s = null; } }
                        if (s is not null) pars.Add(s);
                    }
                _lastTextInfo[p.Pid] = new TextInfo((int)PayloadReader.Int(p.Payload, FNum("TextInformationEvent", "message_id"), 0),
                    (int)PayloadReader.Int(p.Payload, FNum("TextInformationEvent", "message_type"), 0), pars, Interlocked.Increment(ref _snowSeq));
                break;
            }
        }
    }

    private TreasureHunt ReadTreasure(IReadOnlyList<ProtoField> pl, long seq)
    {
        int stepsF = FNum("TreasureHuntEvent", "known_steps"), flagsF = FNum("TreasureHuntEvent", "flags");
        var steps = new List<TreasureStep>();
        var flags = new List<TreasureFlag>();
        int hintF = FNum("TreasureHuntStep", "follow_direction_to_hint"), dirF = FNum("TreasureHuntStep", "follow_direction");
        int fightF = FNum("TreasureHuntStep", "fight"), poiF = FNum("TreasureHuntStep", "follow_direction_to_poi"), digF = FNum("TreasureHuntStep", "dig");
        foreach (var f in pl)
        {
            if (f.Field == stepsF && f.Message is { } st)
            {
                if (st.FirstOrDefault(x => x.Field == poiF) is { } poi)
                {
                    var m = poi.Message ?? (IReadOnlyList<ProtoField>)Array.Empty<ProtoField>();
                    steps.Add(new TreasureStep("poi", (int)PayloadReader.Int(m, FNum("FollowDirectionToPOI", "direction"), 0), 0,
                        (int)PayloadReader.Int(m, FNum("FollowDirectionToPOI", "poi_label_id"), 0), 0));
                }
                else if (st.FirstOrDefault(x => x.Field == hintF) is { } hint)
                {
                    var m = hint.Message ?? (IReadOnlyList<ProtoField>)Array.Empty<ProtoField>();
                    steps.Add(new TreasureStep("hint", (int)PayloadReader.Int(m, FNum("FollowDirectionToHint", "direction"), 0),
                        (int)PayloadReader.Int(m, FNum("FollowDirectionToHint", "npc_id"), 0), 0, 0));
                }
                else if (st.FirstOrDefault(x => x.Field == dirF) is { } dir)
                {
                    var m = dir.Message ?? (IReadOnlyList<ProtoField>)Array.Empty<ProtoField>();
                    steps.Add(new TreasureStep("direction", (int)PayloadReader.Int(m, FNum("FollowDirection", "direction"), 0), 0, 0,
                        (int)PayloadReader.Int(m, FNum("FollowDirection", "map_count"), 0)));
                }
                else if (st.Any(x => x.Field == fightF)) steps.Add(new TreasureStep("fight", 0, 0, 0, 0));
                else if (st.Any(x => x.Field == digF)) steps.Add(new TreasureStep("dig", 0, 0, 0, 0));
                else steps.Add(new TreasureStep("unknown", 0, 0, 0, 0));
            }
            else if (f.Field == flagsF && f.Message is { } fl)
                flags.Add(new TreasureFlag(PayloadReader.Int(fl, FNum("TreasureHuntFlag", "map_id"), 0), (int)PayloadReader.Int(fl, FNum("TreasureHuntFlag", "state"), 0)));
        }
        var hunt = new TreasureHunt(
            (int)PayloadReader.Int(pl, FNum("TreasureHuntEvent", "quest_type"), 0),
            PayloadReader.Int(pl, FNum("TreasureHuntEvent", "start_map_id"), 0),
            (int)PayloadReader.Int(pl, FNum("TreasureHuntEvent", "total_step_count"), 0),
            (int)PayloadReader.Int(pl, FNum("TreasureHuntEvent", "current_check_point"), 0),
            (int)PayloadReader.Int(pl, FNum("TreasureHuntEvent", "total_check_point"), 0),
            (int)PayloadReader.Int(pl, FNum("TreasureHuntEvent", "available_retry_count"), 0),
            flags, steps, seq);
        TransportLog.Line($"[trésor] chasse : étape {hunt.CurrentCheckPoint}/{hunt.TotalCheckPoint}, {flags.Count}/{hunt.TotalStepCount} drapeau(x), dernier indice = {steps.LastOrDefault()?.Kind ?? "-"}");
        return hunt;
    }

    private bool TreasureSend(uint pid, byte[] pkt, string what)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        var relay = RelayForInject();
        if (relay is null) return false;
        if (pkt.Length == 0) { TransportLog.Line($"[trésor] pid {pid} : {what} NON forgé ({MoveForge.LastForgeError})."); return false; }
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[trésor] pid {pid} : {what} inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    public bool TreasureSendFlag(uint pid, int index, int questType) => TreasureSend(pid, MoveForge.BuildTreasureFlagFramed(index, questType), $"drapeau n°{index}");
    public bool TreasureSendFlagRemove(uint pid, int index, int questType) => TreasureSend(pid, MoveForge.BuildTreasureFlagRemoveFramed(index, questType), $"retrait drapeau n°{index}");
    public bool TreasureSendDig(uint pid, int questType) => TreasureSend(pid, MoveForge.BuildTreasureDigFramed(questType), "creuser");
    public bool TreasureSendLegendary(uint pid, int legendaryId)
    {
        if (legendaryId <= 0) return false;
        _legendaryTreasure[pid] = 0;
        if (TreasureSend(pid, MoveForge.BuildTreasureLegendaryFramed(legendaryId), $"chasse legendaire {legendaryId}")) return true;
        _legendaryTreasure.TryRemove(pid, out _);
        return false;
    }
    public bool TreasureSendGiveUp(uint pid, int questType) => TreasureSend(pid, MoveForge.BuildTreasureGiveUpFramed(questType), "abandon");

    private void ForgetTreasure(uint pid)
    {
        _legendaryTreasure.TryRemove(pid, out _);
        _treasure.TryRemove(pid, out _); _treasureAnswer.TryRemove(pid, out _); _treasureDig.TryRemove(pid, out _);
        _treasureFlagAns.TryRemove(pid, out _); _treasureFinished.TryRemove(pid, out _); _lastTextInfo.TryRemove(pid, out _);
    }
}
