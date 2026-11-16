namespace GivreClone.App.Services.CombatParity;

/// <summary>
/// Conservative classification for the two post-movement offensive recovery passes.
/// Those passes select an enemy's occupied cell; an invocation, self spell or ally spell
/// must never be reinterpreted as an ordinary enemy-targeted attack.
///
/// This does NOT grant permission to cast. The caller must still verify game-observed
/// AP, spell availability, position, range, line of sight, turn and target restrictions.
/// </summary>
public static class OffensiveFallbackPolicy
{
    public static bool IsEligible(bool enabled, int spellId, int perTurn, bool onMe,
        bool isSummon, SpellTargetKind targetKind)
        => enabled && spellId > 0 && perTurn > 0 && !onMe && !isSummon
            && targetKind == SpellTargetKind.Enemy;
}
