using System.Collections.Generic;

namespace GivreClone.App.Services;

/// <summary>
/// Lot C2 : bilan du DERNIER combat d'un bot (combat:lastFightResult / hook onFightEnd). Immuable : écrit par le thread
/// réseau (FightEndEvent, ou repli de fin de combat sans FightEndEvent -> Decoded=false), lu par le thread du script.
/// Seq s'incrémente à chaque combat publié (le hook onFightEnd ne se déclenche qu'une fois par Seq).
/// Outcome : 2 = victoire, 1 = nul, 0 = défaite, -1 = inconnu. DurationMs = mesure LOCALE (FightStarting -> fin, placement
/// inclus) ; ServerDuration = valeur brute du serveur (unité non vérifiée). Xp/Kamas null = non lus.
/// </summary>
public sealed record FightResultInfo(long Seq, bool Decoded, int Outcome, long DurationMs, long ServerDuration, long? Xp, long? Kamas,
    IReadOnlyList<(int Gid, int Qty)> Loot, IReadOnlyList<(int Id, bool Success)> Challenges, long FightId, long EndedAtTick)
{
    // Turns taken by this character through the bot engine or script; not server round count.
    // Null means no such turn was observed.
    public int? Turns { get; init; }
}
