using GivreClone.App.Services.CombatParity;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Tests déterministes de la même implémentation LastChanceCastPolicy liée au projet.
/// Aucune dépendance à l'App compilée ou aux paquets du jeu.
/// </summary>
public sealed class CombatParityLastChanceTests
{
    [Theory]
    [InlineData(1, 0, 6, 3, 0, 5, null)]
    [InlineData(2, 1, 6, 3, 0, 5, 5)]
    [InlineData(1, 0, 6, 3, 2, 5, 2)]
    [InlineData(1, 0, 3, 3, 1, 10, 8)]
    public void Valid_last_chance_still_allowed_when_profile_budget_is_available(
        int perTurn, int used, int ap, int cost, int cooldown, int turn, int? lastCast)
        => Assert.True(LastChanceCastPolicy.CanAttempt(perTurn, used, ap, cost, cooldown, turn, lastCast));

    [Theory]
    [InlineData(0, 0, 6, 3, 0, 5, null)]
    [InlineData(1, 1, 6, 3, 0, 5, null)]
    [InlineData(2, 2, 6, 3, 0, 5, null)]
    [InlineData(1, -1, 6, 3, 0, 5, null)]
    [InlineData(1, 0, 2, 3, 0, 5, null)]
    [InlineData(1, 0, -1, 3, 0, 5, null)]
    [InlineData(1, 0, 6, 0, 0, 5, null)]
    [InlineData(1, 0, 6, -1, 0, 5, null)]
    [InlineData(1, 0, 6, 3, 2, 5, 5)]
    [InlineData(1, 0, 6, 3, 2, 5, 4)]
    [InlineData(1, 0, 6, 3, 2, 5, 3)]
    [InlineData(1, 0, 6, 3, 0, 5, 6)]
    [InlineData(1, 0, 6, 3, 0, 0, null)]
    [InlineData(1, 0, 6, 3, -1, 5, null)]
    public void Last_chance_rejects_exhausted_limits_invalid_inputs_and_unexpired_cooldown(
        int perTurn, int used, int ap, int cost, int cooldown, int turn, int? lastCast)
        => Assert.False(LastChanceCastPolicy.CanAttempt(perTurn, used, ap, cost, cooldown, turn, lastCast));

    [Fact]
    public void Cooldown_boundary_is_inclusive_and_next_turn_is_eligible()
    {
        Assert.False(LastChanceCastPolicy.CanAttempt(1, 0, 6, 3, 2, 10, 8));
        Assert.True(LastChanceCastPolicy.CanAttempt(1, 0, 6, 3, 2, 11, 8));
    }
}
