using GivreClone.Protocol;

namespace GivreClone.App.Services.CombatParity;

internal static class WalkContinuationCharacteristics
{
    public static bool Match(IReadOnlyDictionary<int, int> expected,
        IReadOnlyDictionary<int, int> observed, int remainingMp)
    {
        // PM is an absolute remaining-point observation, not a fixed stat.
        // All other changes still invalidate the pre-walk continuation.
        if (observed.TryGetValue(FightStatsReader.IdPm, out int mp) && mp != remainingMp)
            return false;
        foreach (var (id, value) in expected)
            if (id != FightStatsReader.IdPm
                && (!observed.TryGetValue(id, out int current) || current != value)) return false;
        foreach (var id in observed.Keys)
            if (id != FightStatsReader.IdPm && !expected.ContainsKey(id)) return false;
        return !expected.ContainsKey(FightStatsReader.IdPm) || observed.ContainsKey(FightStatsReader.IdPm);
    }
}
