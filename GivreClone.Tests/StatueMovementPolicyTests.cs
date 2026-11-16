using GivreClone.Engine;
using Xunit;

namespace GivreClone.Tests;

public sealed class StatueMovementPolicyTests
{
    [Theory]
    [InlineData(100, 114, 100, 1, 1, true)]
    [InlineData(100, 100, 114, 1, 3, false)]
    [InlineData(100, 114, 128, 1, 3, false)]
    [InlineData(100, 114, 100, 2, 1, false)]
    [InlineData(100, 114, 100, -1, 3, false)]
    [InlineData(-1, 114, -1, 1, 3, false)]
    public void Restore_requires_displacement_and_reachable_original_cell(int start, int current, int destination, int cost, int mp, bool expected)
        => Assert.Equal(expected, StatueMovementPolicy.CanRestore(start, current, destination, cost, mp));

    [Theory]
    [InlineData(0, true, false, true, 1)]
    [InlineData(1, true, false, true, 2)]
    [InlineData(1, true, true, true, 0)]
    [InlineData(1, true, false, false, 0)]
    [InlineData(1, false, false, true, 0)]
    public void Stall_requires_consecutive_attacks_blocked_by_statue(int previous, bool active, bool attacked, bool blocked, int expected)
        => Assert.Equal(expected, StatueMovementPolicy.ConsecutiveBlockedTurns(previous, active, attacked, blocked));
}
