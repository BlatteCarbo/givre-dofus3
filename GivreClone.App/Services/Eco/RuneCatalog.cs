using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Eco;

/// <summary>Une famille de runes : code du nom en jeu (« Rune Pa Fo » -> « Fo »), id d'effet (action d'ObjectEffect) et points de la rune de base.
/// Palier Pa = 3 × base, Ra = 10 × base. Les noms viennent des 103 runes (ItemData typeId 78) ; les ids d'effet et quantités suivent les
/// conventions du jeu (inchangées depuis Dofus 2) : à recouper avec un résultat d'atelier réel.</summary>
public sealed record RuneFamily(string Code, int EffectId, int Base, string Label);

public static class RuneCatalog
{
    public static IReadOnlyList<RuneFamily> Families { get; } = new RuneFamily[]
    {
        new("Fo", 118, 1, "Force"), new("Ine", 126, 1, "Intelligence"), new("Cha", 123, 1, "Chance"), new("Age", 119, 1, "Agilité"),
        new("Sa", 124, 1, "Sagesse"), new("Vi", 125, 5, "Vitalité"), new("Ini", 174, 10, "Initiative"), new("Pod", 158, 10, "Pods"),
        new("Prospe", 176, 1, "Prospection"), new("Pui", 138, 1, "Puissance"), new("Ga Pa", 111, 1, "PA"), new("Ga Pme", 128, 1, "PM"),
        new("Po", 117, 1, "Portée"), new("Invo", 182, 1, "Invocations"), new("Cri", 115, 1, "Critique"), new("So", 178, 1, "Soins"),
        new("Do", 112, 1, "Dommages"), new("Do Pi", 225, 1, "Dommages pièges"), new("Per Pi", 226, 1, "Puissance pièges"),
        new("Do Ren", 220, 1, "Renvoi de dommages"), new("Do Cri", 418, 1, "Dommages critiques"), new("Ré Cri", 420, 1, "Résistance critiques"),
        new("Do Pou", 414, 1, "Dommages poussée"), new("Ré Pou", 416, 1, "Résistance poussée"),
        new("Do Terre", 422, 1, "Dommages Terre"), new("Do Feu", 424, 1, "Dommages Feu"), new("Do Eau", 426, 1, "Dommages Eau"),
        new("Do Air", 428, 1, "Dommages Air"), new("Do Neutre", 430, 1, "Dommages Neutre"),
        new("Ré Terre", 240, 1, "Résistance Terre"), new("Ré Eau", 241, 1, "Résistance Eau"), new("Ré Air", 242, 1, "Résistance Air"),
        new("Ré Feu", 243, 1, "Résistance Feu"), new("Ré Neutre", 244, 1, "Résistance Neutre"),
        new("Ré Per Terre", 210, 1, "% Résistance Terre"), new("Ré Per Eau", 211, 1, "% Résistance Eau"), new("Ré Per Air", 212, 1, "% Résistance Air"),
        new("Ré Per Feu", 213, 1, "% Résistance Feu"), new("Ré Per Neutre", 214, 1, "% Résistance Neutre"),
        new("Tac", 753, 1, "Tacle"), new("Fui", 752, 1, "Fuite"), new("Ret Pa", 410, 1, "Retrait PA"), new("Ret Pme", 412, 1, "Retrait PM"),
        new("Ré Pa", 160, 1, "Esquive PA"), new("Ré Pme", 161, 1, "Esquive PM"),
    };

    private static readonly Dictionary<int, RuneFamily> ByEffect = Families.ToDictionary(f => f.EffectId);

    public static RuneFamily? Of(int effectId) => ByEffect.TryGetValue(effectId, out var f) ? f : null;

    /// <summary>Nom d'objet de la rune du palier (« Rune Fo », « Rune Pa Fo », « Rune Ra Fo »). PA/PM n'existent qu'en « Ga ».</summary>
    public static string ItemName(RuneFamily f, string tier) => tier switch
    {
        "Pa" => $"Rune Pa {f.Code}", "Ra" => $"Rune Ra {f.Code}", _ => $"Rune {f.Code}",
    };

    public static int Amount(RuneFamily f, string tier) => tier switch { "Pa" => f.Base * 3, "Ra" => f.Base * 10, _ => f.Base };

    /// <summary>Paliers dans l'ordre Ba, Pa, Ra.</summary>
    public static readonly string[] Tiers = { "Ba", "Pa", "Ra" };

    /// <summary>Skills d'atelier de forgemagie (SkillData.isForgemagus) : Forgemager, Sculptemager, Cordomager, Costumager, Joaillomager, Façomager.</summary>
    public static readonly IReadOnlySet<int> SmithSkills = new HashSet<int> { 113, 118, 164, 166, 168, 351 };

    /// <summary>Atelier de forgemagie utilisable parmi les interactifs d'une carte (skill isForgemagus actif) : (élément, uid du skill), ou null.</summary>
    public static (int ElementId, int SkillUid)? Workshop(IEnumerable<GivreClone.Protocol.MapInteractive>? interactives)
        => interactives?.SelectMany(i => i.Skills.Where(s => s.Enabled && SmithSkills.Contains(s.SkillId)).Select(s => ((int, int)?)(i.ElementId, s.SkillInstanceUid)))
            .FirstOrDefault();

    /// <summary>Gains (Ba, Pa, Ra) par libellé de caractéristique, pour <see cref="Forgemagie.RuneAdvisor"/>.</summary>
    public static IReadOnlyDictionary<string, (int Ba, int Pa, int Ra)> Gains { get; } =
        Families.ToDictionary(f => f.Label, f => (f.Base, f.Base * 3, f.Base * 10), StringComparer.OrdinalIgnoreCase);

    /// <summary>Palier conseillé pour monter une ligne de `current` vers `target` : la plus grosse rune qui ne dépasse pas l'écart,
    /// et jamais au-dessus du palier autorisé par la valeur (seuils PaFrom / RaFrom, 0 = dès le départ).</summary>
    public static string TierFor(RuneFamily f, int current, int target, int paFrom, int raFrom)
    {
        int gap = target - current;
        if (current >= raFrom && gap >= Amount(f, "Ra")) return "Ra";
        if (current >= paFrom && gap >= Amount(f, "Pa")) return "Pa";
        return "Ba";
    }
}
