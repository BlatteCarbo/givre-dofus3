namespace GivreClone.App.Services;

/// <summary>Politique commune au moteur et au simulateur. Un défi ne peut pas lever l'interdiction d'approcher.</summary>
public static class CombatMovementDecision
{
    public static bool CanReposition(CombatMovementPolicy policy, CombatStyle style, bool allowApproach)
        => policy != CombatMovementPolicy.NeverApproach && style != CombatStyle.Passive && allowApproach;

    // Comparer la distance à l'ennemi vivant le plus proche, avant/après le déplacement.
    // Sans ennemi, les appelants fournissent la même distance (aucun rapprochement).
    public static bool CanMove(CombatMovementPolicy policy, int nearestBefore, int nearestAfter)
        => policy != CombatMovementPolicy.NeverApproach || nearestAfter >= nearestBefore;

    public static System.Func<int, int, bool> RestrictSteps(CombatMovementPolicy policy,
        System.Func<int, int> nearestEnemy, System.Func<int, int, bool>? terrain)
        => (from, to) => (terrain?.Invoke(from, to) ?? true)
            && CanMove(policy, nearestEnemy(from), nearestEnemy(to));
}
