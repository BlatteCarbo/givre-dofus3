namespace GivreClone.App.Services;

/// <summary>Rôle d'un bot dans l'équipe (multi-comptes). Concept CROSS-perso (vit dans BotsViewModel, pas par-pid).</summary>
public enum TeamRoleKind { None, Leader, Follower }

/// <summary>Présence d'un membre, calculée en lecture seule (comparaison de carte). MVP : Offline/Connected/SameMap/Elsewhere.
/// Traveling/InFight arrivent avec le moteur de suivi + le signal de combat (v2).</summary>
public enum TeamPresenceKind { Offline, Connected, SameMap, Elsewhere, Traveling, InFight, OtherServer }
