namespace GivreClone.Engine;

public static class StatueMovementPolicy
{
    public static bool CanRestore(int start, int current, int destination, int pathCost, int remainingMp) =>
        (uint)start < 560 && (uint)current < 560 && current != start && destination == start
        && pathCost > 0 && pathCost <= remainingMp;

    public static int ConsecutiveBlockedTurns(int previous, bool active, bool attacked, bool blockedAttack) =>
        active && !attacked && blockedAttack ? System.Math.Min(2, previous + 1) : 0;
}
