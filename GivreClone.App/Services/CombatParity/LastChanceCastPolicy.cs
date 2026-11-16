namespace GivreClone.App.Services.CombatParity;

/// <summary>
/// Pure precondition of the terminal offensive recovery pass. An opportunity to
/// attack cannot override the player's configured number of uses or cooldown.
/// Other game-dependent checks (turn ownership, server spell caps, range, LOS,
/// HP gates and challenges) are applied by the existing combat engine.
/// </summary>
public static class LastChanceCastPolicy
{
    public static bool CanAttempt(int configuredPerTurn, int alreadyCastThisTurn,
        int availableAp, int apCost, int cooldown, int turnNo, int? lastCastTurn)
    {
        if (configuredPerTurn <= 0 || alreadyCastThisTurn < 0
            || alreadyCastThisTurn >= configuredPerTurn
            || availableAp < 0 || apCost <= 0 || availableAp < apCost
            || cooldown < 0 || turnNo <= 0)
            return false;

        // A stale/reversed turn number is not proof that a cooldown has passed.
        if (lastCastTurn is { } last)
        {
            if (last > turnNo) return false;
            if (cooldown > 0 && turnNo - last <= cooldown) return false;
        }
        return true;
    }
}
