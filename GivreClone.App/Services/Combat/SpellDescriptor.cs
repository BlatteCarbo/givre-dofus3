using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization;

namespace GivreClone.App.Services.Combat;

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<AutoCombatElements>))]
public enum AutoCombatElements
{
    Automatic = 0, Neutral = 1, Earth = 2, Fire = 4, Water = 8, Air = 16
}

public enum SpellEffectKind { Unknown, Damage, Heal, Shield, Teleport, Dash, Push, Pull, Swap, Buff, Debuff, Summon }

[Flags]
public enum SpellTargets { None = 0, Self = 1, Ally = 2, Enemy = 4, FreeCell = 8 }

/// <summary>Raw per-effect data is retained even when this slice cannot simulate it.</summary>
public sealed record SpellEffectDescriptor(
    int ActionId, SpellEffectKind Kind, int Min, int Max, int Element, int Duration,
    string TargetMask, int Shape, int Size, int MinSize, bool Conditional = false,
    int DiceNum = 0, int DiceSide = 0, int Value = 0, int Delay = 0, int Random = 0,
    string Triggers = "", string Condition = "", int EffectTriggerDuration = 0)
{
    public SpellTargets Targets
    {
        get
        {
            if (Kind == SpellEffectKind.Teleport && TargetMask is "C" or "c") return SpellTargets.FreeCell;
            SpellTargets result = SpellTargets.None;
            foreach (string token in TargetMask.Split(',', StringSplitOptions.TrimEntries))
            {
                switch (token)
                {
                    case "C": case "c": result |= SpellTargets.Self; break;
                    case "a": result |= SpellTargets.Self | SpellTargets.Ally; break;
                    case "g": result |= SpellTargets.Ally; break;
                    case "A": result |= SpellTargets.Enemy; break;
                    default: return SpellTargets.None; // State, breed, summon and trigger masks need their actual facts.
                }
            }
            return result;
        }
    }

    /// <summary>
    /// L'effet POSE (ou PEUT poser) une marque au sol (piege, glyphe, rune, portail) ?
    /// Ensemble d'actions deja classee « pose de marque » par le resolveur de production (SpellResolver,
    /// meme liste) : 400 = pose de piege (sorts 12914/12921, voir
    /// docs/validation/sram-mark-catalog-join-20261106.md), 1181 = « Pose un portail » (Eliotrope, voir
    /// reference/eliotrope-sorts-donnees-client-20261109.md), 401/402/1091/1165/2022 = poses de meme
    /// nature (glyphe, rune, bombe). AUCUN identifiant de SORT n'est devine ici : seuls des identifiants
    /// d'EFFET deja presents dans les metadonnees client sont utilises.
    /// Les effets DIFFERES ou conditionnels comptent aussi (ex. 14611 « Resonance » : 1181 avec
    /// triggers=D|XD, le portail est pose plus tard) : le fait est « peut poser », pas « pose ».
    /// </summary>
    public bool PlacesGroundMark => ActionId is 400 or 401 or 402 or 1091 or 1165 or 2022 or 1181;

    public bool IsCertain => !Conditional && Targets != SpellTargets.None && Shape == 'P'
        && (Kind switch
        {
            SpellEffectKind.Damage => ActionId is >= 96 and <= 100 && Duration == 0,
            SpellEffectKind.Heal => ActionId is 108 or 143 or 407 or 1109 && Duration == 0,
            SpellEffectKind.Shield => ActionId == 1040 && Duration > 0,
            SpellEffectKind.Teleport => ActionId == 4 && Targets == SpellTargets.FreeCell && Duration == 0,
            SpellEffectKind.Push => Duration == 0 && (ActionId == 783 || ActionId == 5 && Min == Max && Min > 0),
            SpellEffectKind.Pull => ActionId == 6 && Duration == 0 && Min == Max && Min > 0,
            SpellEffectKind.Swap => ActionId == 8 && Duration == 0 && Min == 0 && Max == 0,
            _ => false,
        });

    // Action IDs are data identifiers, not spell IDs. Unmodeled variants remain explicitly uncertain.
    public static SpellEffectKind Classify(int action) => action switch
    {
        >= 91 and <= 100 or 82 or 89 or 144 or 279 or 1223 or 2822 or 2828 or 2832 or 2890 => SpellEffectKind.Damage,
        81 or 108 or 143 or 407 or 1109 or >= 2998 and <= 3002 => SpellEffectKind.Heal,
        1020 or 1039 or 1040 => SpellEffectKind.Shield,
        4 => SpellEffectKind.Teleport,
        5 or 783 or 1021 or 1103 => SpellEffectKind.Push,
        6 => SpellEffectKind.Pull,
        8 or 1023 => SpellEffectKind.Swap,
        181 or 1008 or 1011 => SpellEffectKind.Summon,
        111 or 112 or 128 or 117 or 118 or 119 or 123 or 124 or 126 or 138 or 950 or >= 280 and <= 299 => SpellEffectKind.Buff,
        101 or 127 or 168 or 169 or 170 or 171 or 172 or 173 or 951 or 952 => SpellEffectKind.Debuff,
        _ => SpellEffectKind.Unknown,
    };
}

public sealed record SpellCastDescriptor(
    int SpellId, int Grade, int Ap, int MinRange, int Range, int Flags, bool Rangeable,
    int MaxPerTurn, int MaxPerTarget, int MinCastInterval, int InitialCooldown,
    string StatesCriterion, ImmutableArray<SpellEffectDescriptor> Effects, bool Uncertain = false)
{
    public bool NeedFreeCell => (Flags & 8) != 0;
    public bool NeedTakenCell => (Flags & 16) != 0;
    public bool CastInLine => (Flags & 1) != 0;
    public bool CastInDiagonal => (Flags & 2) != 0;
    public bool TestLos => (Flags & 4) != 0;
    public bool IsTeleport => Effects.Any(e => e.Kind == SpellEffectKind.Teleport);
    /// <summary>
    /// Le sort POSE (ou PEUT poser) une marque au sol ?
    /// true = oui ; false = non (effets connus, aucune pose de marque) ; null = INCONNU.
    /// INVARIANT : `false` signifie « effets CONNUS, aucune pose de marque ». Toute INCERTITUDE de lecture
    /// doit produire `null`, jamais `false` : le predicite de production (CastLegality) refuse une case
    /// marquee sur `true` ET sur `null`, et n'accepte que sur `false` — une incertitude transformee en
    /// `false` deviendrait une PERMISSION (refus serveur 229 « Il y a un piege sur cette case » gaspille).
    /// Pourquoi `Uncertain` suffit : le chargeur (GameNameResolver.ReadSpellDamage) peut PERDRE une ligne
    /// d'effet (une exception sur delay/random/effectElement/diceNum/diceSide/effectCondition fait tomber
    /// la ligne entiere de la liste) et pose alors `Uncertain = true` : la liste d'effets reste NON VIDE mais
    /// INCOMPLETE, et l'effet de pose manquant donnerait un `false` affirmatif. D'ou `Uncertain` -> `null`.
    /// CAUTION connue et assumee : `Uncertain` est aussi pose par le chargeur pour un motif SANS RAPPORT avec
    /// la completude des effets (`globalCooldown != 0`). Les sorts a recharge globale retombent donc sur le
    /// refus prudent (comportement d'avant le correctif) : perimetre deverrouille plus ETROIT, jamais plus
    /// permissif. Un indicateur distinct `EffectsIncomplete` dans le chargeur leverait cette limite ; il n'est
    /// PAS ajoute ici (pas de sous-systeme de chargement pour ce seul fait).
    /// Portee du fait : il est exprime au niveau du SORT (« ce sort pose une marque NULLE PART »), alors que
    /// le conflit se joue au niveau de la CASE (« ce sort pose une marque SUR LA CASE VISEE ») : pour un sort
    /// qui pose un portail sous le lanceur puis teleporte (14591 « Sillage »), le fait vaut `true` alors que
    /// la marque n'atterrit pas sur la case visee -> sur-refusal prudente, pas de defaut de surete.
    /// </summary>
    public bool? PlacesGroundMark => Uncertain || Effects.IsDefaultOrEmpty ? null : Effects.Any(e => e.PlacesGroundMark);
    public bool IsForcedMovement => Effects.Any(e => e.Kind is SpellEffectKind.Push or SpellEffectKind.Pull or SpellEffectKind.Swap);
    public SpellTargets Targets => Effects.IsDefaultOrEmpty ? SpellTargets.None
        : Effects.Aggregate(SpellTargets.Self | SpellTargets.Ally | SpellTargets.Enemy | SpellTargets.FreeCell,
            (targets, e) => targets & e.Targets);
    public bool CanPlan => !Uncertain && string.IsNullOrWhiteSpace(StatesCriterion)
        && !Effects.IsDefaultOrEmpty && Effects.All(e => e.IsCertain) && Targets != SpellTargets.None
        && (!IsTeleport || Effects.Length == 1 && !NeedTakenCell)
        && (!IsForcedMovement || Effects.Length == 1 && (!NeedFreeCell || Effects[0].ActionId == 783));
}
