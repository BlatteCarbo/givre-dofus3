using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Equipment;

/// <summary>Effet brut d'un exemplaire d'objet, identifié par son actionId du jeu.
/// Value null signifie que la valeur numérique n'a pas été décodée ; aucun jet n'est déduit du GID.</summary>
public sealed record GearEffectSnapshot(int EffectId, decimal? Value, string? Description = null);

/// <summary>Photo en lecture seule d'un emplacement. EffectsComplete doit être vrai uniquement lorsque
/// TOUS les effets de l'exemplaire ont réellement été lus. Un GID ne prouve pas ses jets de forgemagie.</summary>
public sealed record GearItemSnapshot(int Position, long ItemGid, long? UniqueId = null,
    IReadOnlyList<GearEffectSnapshot>? Effects = null, bool EffectsComplete = false);

public sealed record GearSnapshot(string Name, IReadOnlyList<GearItemSnapshot> Items);

public enum GearSlotChangeKind { Added, Removed, Replaced, PotentiallyModified, Unchanged }

public sealed record GearSlotDifference(int Position, GearItemSnapshot? Before, GearItemSnapshot? After,
    GearSlotChangeKind Kind);

/// <summary>KnownBefore/After n'additionnent que les valeurs décodées.
/// Delta est null dès qu'un effet manquant/inconnu pourrait fausser le résultat.</summary>
public sealed record GearEffectDifference(int EffectId, decimal KnownBefore, decimal KnownAfter,
    decimal? Delta, bool BeforeComplete, bool AfterComplete);

public sealed record GearComparisonResult(
    IReadOnlyList<GearSlotDifference> Slots,
    IReadOnlyList<GearEffectDifference> Effects,
    bool EffectsFullyComparable,
    bool HasUndescribedEffects);

/// <summary>Comparaison déterministe de deux instantanés d'équipement. Aucun accès jeu, inventaire, HDV ou réseau.</summary>
public static class GearComparison
{
    public static GearComparisonResult Compare(GearSnapshot before, GearSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var left = ValidateAndIndex(before);
        var right = ValidateAndIndex(after);

        var slots = left.Keys.Union(right.Keys).OrderBy(p => p)
            .Select(position =>
            {
                left.TryGetValue(position, out var a);
                right.TryGetValue(position, out var b);
                GearSlotChangeKind kind = a is null ? GearSlotChangeKind.Added
                    : b is null ? GearSlotChangeKind.Removed
                    : a.ItemGid != b.ItemGid ? GearSlotChangeKind.Replaced
                    : FullyIdentical(a, b) ? GearSlotChangeKind.Unchanged
                    : GearSlotChangeKind.PotentiallyModified;
                return new GearSlotDifference(position, a, b, kind);
            }).ToArray();

        // Toutes les valeurs/occurrences doivent être connues sur CHAQUE objet pour calculer un delta global.
        // Une panoplie vide est connue et totalise bien zéro.
        bool leftComplete = before.Items.All(IsNumericComplete);
        bool rightComplete = after.Items.All(IsNumericComplete);
        var leftSums = SumKnown(before.Items);
        var rightSums = SumKnown(after.Items);
        var effects = leftSums.Keys.Union(rightSums.Keys).OrderBy(x => x)
            .Select(id =>
            {
                leftSums.TryGetValue(id, out decimal a);
                rightSums.TryGetValue(id, out decimal b);
                return new GearEffectDifference(id, a, b,
                    leftComplete && rightComplete ? checked(b - a) : null,
                    leftComplete, rightComplete);
            }).ToArray();

        return new GearComparisonResult(slots, effects, leftComplete && rightComplete,
            !leftComplete || !rightComplete);
    }

    private static Dictionary<int, GearItemSnapshot> ValidateAndIndex(GearSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot.Items);
        var indexed = new Dictionary<int, GearItemSnapshot>();
        foreach (var item in snapshot.Items)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (item.Position < 0 || item.ItemGid <= 0)
                throw new ArgumentOutOfRangeException(nameof(snapshot), "Position/GID d'équipement invalide.");
            if (item.UniqueId is <= 0)
                throw new ArgumentOutOfRangeException(nameof(snapshot), "Identifiant d'exemplaire invalide.");
            if (item.EffectsComplete && item.Effects is null)
                throw new ArgumentException("EffectsComplete requiert une liste explicite, éventuellement vide.", nameof(snapshot));
            if (item.Effects?.Any(e => e is null || e.EffectId <= 0) == true)
                throw new ArgumentException("ActionId d'effet invalide.", nameof(snapshot));
            if (!indexed.TryAdd(item.Position, item))
                throw new ArgumentException("Plusieurs objets occupent le même emplacement.", nameof(snapshot));
        }
        return indexed;
    }

    private static bool IsNumericComplete(GearItemSnapshot item)
        => item.EffectsComplete && item.Effects is not null && item.Effects.All(e => e.Value.HasValue);

    private static bool FullyIdentical(GearItemSnapshot a, GearItemSnapshot b)
    {
        if (!IsNumericComplete(a) || !IsNumericComplete(b)) return false;
        if (a.UniqueId != b.UniqueId) return false;
        // L'ordre de décodage des effets n'a pas de signification ; leurs occurrences en ont une.
        var x = a.Effects!.Select(e => (e.EffectId, e.Value)).OrderBy(e => e.EffectId).ThenBy(e => e.Value);
        var y = b.Effects!.Select(e => (e.EffectId, e.Value)).OrderBy(e => e.EffectId).ThenBy(e => e.Value);
        return x.SequenceEqual(y);
    }

    private static Dictionary<int, decimal> SumKnown(IEnumerable<GearItemSnapshot> items)
    {
        var sums = new Dictionary<int, decimal>();
        foreach (var item in items)
            foreach (var effect in item.Effects ?? Array.Empty<GearEffectSnapshot>())
            {
                if (!effect.Value.HasValue) continue;
                sums.TryGetValue(effect.EffectId, out decimal previous);
                sums[effect.EffectId] = checked(previous + effect.Value.Value);
            }
        return sums;
    }
}
