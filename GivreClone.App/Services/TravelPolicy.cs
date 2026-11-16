namespace GivreClone.App.Services;

/// <summary>
/// Choix « marcher ou prendre zaap / havre-sac » d'un trajet, selon le réglage Déplacement du perso. Pur : testé tel quel.
/// Marche de préférence : zaap et havre-sac ne servent que si la cible n'est PAS joignable à pied (autre île, Otomaï,
/// Frigost…) ou si la marche dépasse la distance maximale choisie (0 = aucune limite). Sinon, règle habituelle du moteur
/// (zaap dès qu'il raccourcit nettement le trajet).
/// </summary>
public static class TravelPolicy
{
    /// <summary>Vrai : le trajet se fait à pied, sans tenter zaap ni havre-sac.</summary>
    public static bool WalkFirst(bool preferWalk, int walkMaxHops, int walkHops, bool unreachableOnFoot)
        => preferWalk && !unreachableOnFoot && walkHops > 0 && (walkMaxHops <= 0 || walkHops <= walkMaxHops);
}
