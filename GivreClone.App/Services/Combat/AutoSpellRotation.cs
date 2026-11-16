using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Combat;

/// <summary>Un sort appris candidat au mode automatique : coût en PA et dégâts moyens d'un lancer (0 = pas de dégâts connus).</summary>
public readonly record struct LearnedSpellInfo(int SpellId, int Ap, int AvgDamage, SpellCastDescriptor? Descriptor = null);

public sealed record AutoSpellOptions(AutoCombatElements Elements = AutoCombatElements.Automatic,
    IReadOnlyDictionary<int, int>? Characteristics = null, bool RequireDescriptors = false);

/// <summary>Mode automatique du combat (option « Tous mes sorts appris ») : complète la rotation du profil avec les sorts APPRIS
/// dont les effets sont modélisés et n'y figurent pas encore. Un sort présent dans le profil garde ses réglages ; décoché,
/// il reste écarté. Les utilitaires sont des candidats : seul le planificateur décide de les lancer.
/// Ordre des ajouts : dégâts par PA décroissants, puis dégâts, puis id ; priorités placées APRÈS celles du profil.</summary>
public static class AutoSpellRotation
{
    public static bool RequiresTacticalPlanning(SpellCastDescriptor spell)
    {
        if (spell.Effects.Any(e => e.ActionId is 1181 or 1183 or 405)) return true;
        bool Deferred(SpellEffectDescriptor effect) => effect.Delay > 0
            || !string.IsNullOrWhiteSpace(effect.Triggers) && effect.Triggers.Trim() != "I";
        // A state or subcast accompanying a trigger does not establish an
        // immediate benefit. The planner must justify the setup sequence.
        return spell.Effects.Any(Deferred)
            && spell.Effects.All(e => Deferred(e) || e.ActionId is 950 or 951 or 1160);
    }

    public static bool ShouldUsePlanner(IEnumerable<SpellCastDescriptor?> spells)
    {
        bool hasModeledDamage = false, hasUtility = false, needsFallback = false;
        foreach (var spell in spells)
        {
            if (spell is null) { needsFallback = true; continue; }
            // Le planificateur n'admet un critère d'état que si les états du lanceur sont observés ; sans cette observation
            // l'attaque conditionnée resterait inutilisée : elle garde la rotation classique.
            if (!string.IsNullOrWhiteSpace(spell.StatesCriterion)
                && spell.Effects.Any(e => SpellResolver.IsDamageAction(e.ActionId))) { needsFallback = true; continue; }
            // Inspect the whole rotation: a subsequent unknown/conditional attack
            // must retain access to the classic fallback even if an earlier spell
            // is perfectly modeled by the tactical planner.
            bool modeledDamage = CombatTurnPlanner.HasModeledEnemyDamage(spell);
            hasModeledDamage |= modeledDamage;
            // A correctly modeled immediate attack is not a reason to fall back.
            // Keep the classic rotation when any attack row cannot be evaluated
            // immediately, even if another effect on the spell is modeled.
            needsFallback |= spell.Effects.Any(e => SpellResolver.IsDamageAction(e.ActionId)
                && (!modeledDamage || !SpellResolver.IsImmediateModeled(e)
                    && !SpellResolver.IsModeledTurnTrigger(spell.SpellId, e)));
            hasUtility |= CombatTurnPlanner.IsAdmitted(spell)
                && spell.Effects.Any(e => (e.Kind is SpellEffectKind.Heal or SpellEffectKind.Shield or SpellEffectKind.Teleport
                        or SpellEffectKind.Push or SpellEffectKind.Pull or SpellEffectKind.Swap
                        || e.ActionId is 150 or 1183
                        || e.ActionId is 111 or 112 or 115 or 118 or 119 or 120 or 123 or 126 or 128 or 138 or 418
                            && e.TargetMask.Split(',', StringSplitOptions.TrimEntries)
                                .Any(t => t is "C" or "c" or "a" or "g" or "i" or "j" or "h" or "l" or "m")
                        || e.ActionId is 1079 or 1080 or 168 or 169
                            && e.TargetMask.Split(',', StringSplitOptions.TrimEntries).Any(t => t is "A" or "I" or "J" or "H" or "L" or "M"))
                    && SpellResolver.IsImmediateModeled(e));
        }
        return (hasModeledDamage || hasUtility) && !needsFallback;
    }

    public static List<SpellConfig> Build(IReadOnlyList<SpellConfig> profile, IEnumerable<LearnedSpellInfo> learned,
        AutoSpellOptions? options = null)
    {
        var listed = new HashSet<int>(profile.Select(s => s.SpellId));
        learned = learned.ToArray();
        int last = profile.Count == 0 ? 0 : profile.Max(s => s.Priority);
        int next = last < int.MaxValue ? last + 1 : int.MaxValue;
        var rotation = profile.Where(s => s.Enabled && s.SpellId > 0).ToList();

        // Détection automatique des éléments maîtrisés du personnage pour éliminer les sorts hors-élément ridicules
        int GetStat(int statId) => options?.Characteristics?.GetValueOrDefault(statId) ?? 0;
        int str = GetStat(CharIds.Strength);
        int intel = GetStat(CharIds.Intelligence);
        int cha = GetStat(CharIds.Chance);
        int agi = GetStat(CharIds.Agility);
        int maxStat = Math.Max(str, Math.Max(intel, Math.Max(cha, agi)));

        AutoCombatElements detectedElements = AutoCombatElements.Automatic;
        // Seuil d'activation du filtre elementaire. Il etait a 80 points de caracteristique : TOUT personnage en cours de
        // montee (niveaux 1 a ~40, ou en panoplie sagesse) restait sous ce seuil, le filtre ne s'activait pas, et la
        // rotation admettait les 4 elements. Comme les degats de BASE d'un sort hors-element depassent souvent ceux d'un
        // petit sort dans l'element maitre, le sort hors-element passait devant et tapait pour presque rien -- le defaut
        // signale en jeu. A 15 points l'intention du joueur est deja lisible, et le seuil relatif ci-dessous (35 % de la
        // stat dominante, plancher proportionnel) garde les builds hybrides.
        if (options is not null && options.Elements == AutoCombatElements.Automatic && maxStat >= 15)
        {
            detectedElements = 0;
            // Plancher proportionnel : a 80+ on garde l'ancien plancher de 40, en dessous on suit la stat dominante pour
            // ne pas exiger 40 points d'un personnage qui n'en a que 20 (sinon aucun element ne passe et on retombe en
            // Automatic, c'est-a-dire sans filtre du tout).
            int threshold = Math.Max(Math.Min(40, (int)(maxStat * 0.6)), (int)(maxStat * 0.35));
            if (str >= threshold) detectedElements |= AutoCombatElements.Earth | AutoCombatElements.Neutral;
            if (intel >= threshold) detectedElements |= AutoCombatElements.Fire;
            if (cha >= threshold) detectedElements |= AutoCombatElements.Water;
            if (agi >= threshold) detectedElements |= AutoCombatElements.Air;
            if (detectedElements == 0) detectedElements = AutoCombatElements.Automatic;
        }

        var effectiveElements = options?.Elements == AutoCombatElements.Automatic && detectedElements != AutoCombatElements.Automatic
            ? detectedElements
            : options?.Elements ?? AutoCombatElements.Automatic;

        bool ElementsAllow(SpellCastDescriptor descriptor)
        {
            if (effectiveElements == AutoCombatElements.Automatic) return true;
            // Automatic detection ranks damage elements; a modeled push remains
            // useful for placement even when its accompanying damage is weak.
            if (options?.Elements == AutoCombatElements.Automatic
                && descriptor.Effects.Any(e => e.Kind == SpellEffectKind.Push
                    && SpellResolver.IsImmediateModeled(e))) return true;
            var dmgEffects = descriptor.Effects.Where(e => e.Kind == SpellEffectKind.Damage
                && !e.TargetMask.Split(',', StringSplitOptions.TrimEntries).All(t => t is "C" or "c" or "a" or "g")).ToList();
            if (dmgEffects.Count == 0) return true;
            bool Matches(SpellEffectDescriptor e) =>
                (effectiveElements & (DamageEstimator.ElementOf(e, options?.Characteristics ?? System.Collections.Immutable.ImmutableDictionary<int, int>.Empty) switch
                {
                    DamageElement.Neutral => AutoCombatElements.Neutral,
                    DamageElement.Earth => AutoCombatElements.Earth,
                    DamageElement.Fire => AutoCombatElements.Fire,
                    DamageElement.Water => AutoCombatElements.Water,
                    DamageElement.Air => AutoCombatElements.Air,
                    _ => AutoCombatElements.Automatic,
                })) != 0;
            return options?.Elements == AutoCombatElements.Automatic ? dmgEffects.Any(Matches) : dmgEffects.All(Matches);
        }
        double Damage(LearnedSpellInfo spell)
        {
            if (spell.Descriptor is not { } descriptor || options?.Characteristics is not { } characteristics)
                return spell.AvgDamage;

            double dmg = descriptor.Effects.Where(e => e.Kind == SpellEffectKind.Damage
                && !e.TargetMask.Split(',', StringSplitOptions.TrimEntries).All(t => t is "C" or "c" or "a" or "g"))
                .Sum(e => DamageEstimator.Estimate(e.Min, e.Max, DamageEstimator.ElementOf(e, characteristics), characteristics).Average);

            // Bonus pour sorts à faible coût PA (2 PA) : permet un packing optimal (ex: 2x 2 PA pour 4 PA au lieu d'1 sort 3 PA)
            if (spell.Ap == 2) dmg *= 1.25;

            // Bonus pour sorts avec vol/retrait PM ou PA (ex: Flèche d'Immobilisation)
            if (descriptor.Effects.Any(e => e.ActionId is 168 or 169))
                dmg += 20.0;

            // Bonus pour sorts à montée en puissance ou buffs cumulables par coup
            if (descriptor.Effects.Any(e => e.ActionId is 112 or 138 or 2822 or 115))
                dmg += 15.0;

            return dmg;
        }
        foreach (var sp in learned
                     .Where(l => l.SpellId > 0 && l.SpellId is not (413 or 414) && !listed.Contains(l.SpellId) && l.Ap > 0
                         && (l.Descriptor is { } d ? (options?.RequireDescriptors == true
                                 ? CombatTurnPlanner.IsAdmitted(d) : d.CanPlan) && ElementsAllow(d)
                             : options?.RequireDescriptors != true && l.AvgDamage > 0))
                     .GroupBy(l => l.SpellId).Select(g => g.First())
                     .OrderByDescending(l => Damage(l) / l.Ap).ThenByDescending(Damage).ThenBy(l => l.SpellId))
        {
            var targets = sp.Descriptor?.Targets ?? SpellTargets.Enemy;
            bool alliedReplacement = sp.Descriptor is { } replacement && replacement.Effects.All(e =>
                e.ActionId == 405 && e.TargetMask.Split(',', StringSplitOptions.TrimEntries).Contains("a")
                && !e.TargetMask.Split(',', StringSplitOptions.TrimEntries).Contains("A"));
            bool support = sp.Descriptor is { } desc && desc.Effects.All(e =>
                e.Kind is SpellEffectKind.Heal or SpellEffectKind.Shield
                || e.ActionId is 111 or 112 or 115 or 118 or 119 or 120 or 123 or 126 or 128 or 138 or 150 or 406 or 418);
            if (support && sp.Descriptor is { } supportSpell)
            {
                // Portal conditions select a bonus row; they do not change its beneficiaries.
                // Use the same category parser after removing only these known conditions.
                targets = supportSpell.Effects.Aggregate(
                    SpellTargets.Self | SpellTargets.Ally | SpellTargets.Enemy | SpellTargets.FreeCell,
                    (allowed, effect) => allowed & (effect with
                    {
                        TargetMask = string.Join(",", effect.TargetMask.Split(',', StringSplitOptions.TrimEntries)
                            .Where(token => token is not ("r" or "R")))
                    }).Targets);
            }
            // Intervalle de relance du jeu (Entraide : 3 tours) : 1 lancer par tour et Cooldown = intervalle - 1, sinon la
            // rotation classique le relancait a chaque tour -> 68 refus 175 sur 128 lancers d'Entraide (09/11).
            int interval = sp.Descriptor?.MinCastInterval ?? 0;
            rotation.Add(new SpellConfig
            {
                SpellId = sp.SpellId, Priority = next,
                PerTurn = interval > 0 ? 1 : sp.Descriptor is { MaxPerTurn: > 0 } caps
                    ? caps.MaxPerTurn + learned.SelectMany(s => s.Descriptor?.Effects ?? System.Collections.Immutable.ImmutableArray<SpellEffectDescriptor>.Empty)
                        .Where(e => e.ActionId == 290 && e.DiceNum == sp.SpellId && SpellResolver.IsImmediateModeled(e))
                        .Select(e => Math.Max(0, e.Value)).DefaultIfEmpty(0).Max()
                    : 99,
                Cooldown = Math.Max(0, interval - 1),
                // Un sort de portee minimale >= 1 ne se lance jamais sur sa propre case : les fleches Cra (32435/32436) portent
                // un bonus sur le lanceur (masque C) qui reduisait leurs cibles a « soi » -> lancer refuse, 95 % des tours de
                // Archerbot finissaient avec 3+ PA inutilises (10/11).
                Enabled = true, OnMe = targets == SpellTargets.Self && sp.Descriptor is not { MinRange: > 0 },
                TargetKind = alliedReplacement || support && (targets & SpellTargets.Ally) != 0 ? SpellTargetKind.Ally : SpellTargetKind.Enemy,
                SelfBuff = support,
            });
            if (next < int.MaxValue) next++;
        }
        return rotation;
    }
}
