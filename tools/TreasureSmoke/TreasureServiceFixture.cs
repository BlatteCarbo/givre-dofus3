// Offline replacement for the transport-facing service. Production VM source is linked unchanged.
namespace GivreClone.App.Services;
public static class Stores { public static LaunchService Launch { get; } = new(); }
public sealed class LaunchService
{
    public sealed record TreasureStep(string Kind, int Direction, int NpcId, int PoiLabelId, int MapCount);
    public sealed record TreasureFlag(long MapId, int State);
    public sealed record TreasureHunt(int QuestType, long StartMapId, int TotalStepCount, int CurrentCheckPoint,
        int TotalCheckPoint, int AvailableRetryCount, IReadOnlyList<TreasureFlag> Flags, IReadOnlyList<TreasureStep> Steps, long Seq);
    public readonly record struct TreasureAnswer(int Result, int Extra, long Seq);
    public Dictionary<uint, TreasureHunt> Hunts = new();
    public Dictionary<uint, TreasureAnswer> DigAnswers = new(), FlagAnswers = new();
    public Dictionary<uint, long> Finished = new();
    public long SnowSeq { get; set; }
    public bool SendResult { get; set; } = true;
    public List<(uint Pid, string Action, int Index)> Sends = new();
    public TreasureHunt? Treasure(uint pid) => Hunts.GetValueOrDefault(pid);
    public TreasureAnswer? TreasureDigAnswer(uint pid) => DigAnswers.TryGetValue(pid, out var value) ? value : null;
    public TreasureAnswer? TreasureFlagAnswer(uint pid) => FlagAnswers.TryGetValue(pid, out var value) ? value : null;
    public long TreasureFinishedSeq(uint pid) => Finished.GetValueOrDefault(pid);
    private bool Send(uint pid, string action, int index = -1) { Sends.Add((pid, action, index)); return SendResult; }
    public bool TreasureSendFlag(uint pid, int index, int quest) => Send(pid, "flag", index);
    public bool TreasureSendFlagRemove(uint pid, int index, int quest) => Send(pid, "remove", index);
    public bool TreasureSendDig(uint pid, int quest) => Send(pid, "dig");
    public bool TreasureSendGiveUp(uint pid, int quest) => Send(pid, "giveup");
}
