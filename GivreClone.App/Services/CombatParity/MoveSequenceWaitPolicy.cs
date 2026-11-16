namespace GivreClone.App.Services.CombatParity;

public static class MoveSequenceWaitPolicy
{
    public static bool CanCheckQuiet(int completed, int required, bool turbo,
        bool turnAuthoritySeen, bool ownTurnOpen, int? openSequences)
        => completed == 0 || completed >= required
            || (turbo && turnAuthoritySeen && ownTurnOpen && openSequences == 0);
}
