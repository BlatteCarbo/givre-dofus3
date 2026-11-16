using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.Protocol;

/// <summary>
/// Manifeste des champs que le code lit ou forge, vérifié contre le BUNDLE STREAMÉ au chargement du mapping (côté app).
/// Le 04/10, la prod tournait avec un bundle serveur différent de celui testé : des lectures de combat entières étaient
/// mortes et rien ne le signalait avant qu'un combat passe par là. Désormais, dès le login, tout champ requis absent du
/// bundle reçu est listé d'un coup.
///  - Scanned (RequiredMapping.cs) : généré par les tests depuis le code, jamais édité à la main.
///  - Extra (ici) : recherches que le scan ne voit pas (noms tirés d'une table). À compléter quand on en ajoute.
/// </summary>
public static partial class RequiredMapping
{
    public static readonly (string Message, string Field)[] Extra =
    {
        // MapFightReader.ReadRelocation : table (porteur, sous-message) -> { target_id, cell }.
        ("GameActionFightEvent", "teleport_on_same_map"), ("TeleportOnSameMap", "target_id"), ("TeleportOnSameMap", "cell"),
        ("GameActionFightEvent", "throw_character"), ("ThrowCharacter", "target_id"), ("ThrowCharacter", "cell"),
        ("GameActionFightEvent", "drop_character"), ("DropCharacter", "target_id"), ("DropCharacter", "cell"),
        ("GameActionFightEvent", "carry_character"), ("CarryCharacter", "target_id"), ("CarryCharacter", "cell"),
        ("GameActionFightEvent", "invisible_detected"), ("InvisibleDetected", "target_id"), ("InvisibleDetected", "cell"),
        // FightVisibilityReader resolves state fields through its Field helper.
        ("FightCharacteristics", "invisibility_state"),
        ("GameActionFightEvent", "invisibility"), ("Invisibility", "target_id"), ("Invisibility", "invisibility_state"),
        // LaunchService.Packets : recharge du relief en combat, map_id lu via le nom du message reçu.
        ("ContextReadyRequest", "map_id"), ("MapInformationRequest", "map_id"),
    };

    /// <summary>Champs requis absents d'un catalogue (« Message.champ »), triés. Vide = le bundle couvre tout le code.</summary>
    public static IReadOnlyList<string> MissingIn(Func<string, string, int?> fieldNumber) =>
        Scanned.Concat(Extra).Distinct()
               .Where(p => fieldNumber(p.Message, p.Field) is null)
               .Select(p => $"{p.Message}.{p.Field}")
               .OrderBy(s => s, StringComparer.Ordinal)
               .ToList();
}
