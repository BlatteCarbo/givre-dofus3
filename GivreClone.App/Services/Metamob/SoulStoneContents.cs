using System.Collections.Generic;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;

namespace GivreClone.App.Services.Metamob;

/// <summary>Une pierre d'âme observée. <see cref="Effects"/> est la liste déjà décodée : elle ne contient pas le monstre.</summary>
public sealed record SoulStoneObservation(int Uid, long Gid, int StackQuantity, IReadOnlyList<InventoryReader.ItemEffect> Effects);

public sealed record SoulHoldingsSnapshot(
    bool BagSeeded,
    bool ChestSeeded,
    IReadOnlyList<SoulStoneObservation> Bag,
    IReadOnlyList<SoulStoneObservation> Chest);

public readonly record struct SoulContentsResult(bool IsKnown, IReadOnlyDictionary<int, int> Quantities, string? Reason, string? CaptureNeeded)
{
    public static SoulContentsResult Known(IReadOnlyDictionary<int, int> quantities) => new(true, quantities, null, null);
    public static SoulContentsResult Unknown(string reason, string? capture) => new(false, Empty, reason, capture);
    private static readonly IReadOnlyDictionary<int, int> Empty = new Dictionary<int, int>();
}

/// <summary>Décide si les effets d'objet déjà lus portent les monstres d'une pierre. Ils ne le portent pas.</summary>
public static class SoulStoneContents
{
    public const string UnknownReason =
        "Contenus inconnus : les effets d'objet déjà décodés ne disent pas quels monstres sont dans la pierre. Aucune écriture.";

    public const string CaptureNeeded =
        "Capture nécessaire : ObjectItem.effects d'une pierre d'âme remplie. ItemEffect recopie action, value_int, min_max, dice (num, side, const), value_string et object_gid. Aucun n'est un identifiant de monstre. ObjectEffect.monster_count (champ 2) n'est pas recopié, et ce nom est un compte, pas une identité. Les champs non lus (duration_minute, paddock_gauge_effect, dofus_date, gbwj, ride_parent) n'ont pas de sens établi pour une âme. Il faut le champ qui identifie le monstre et celui qui donne sa quantité, recoupés avec l'infobulle.";

    public const string InconsistentUid = "Le même uid désigne deux objets. Aucune écriture.";

    public static bool IsSoulStone(long gid) => gid is > 0 and <= int.MaxValue && SoulCapture.StoneById((int)gid) is not null;

    public static SoulContentsResult Read(IReadOnlyList<SoulStoneObservation>? stones)
    {
        var unique = new Dictionary<int, SoulStoneObservation>();
        foreach (var stone in stones ?? System.Array.Empty<SoulStoneObservation>())
        {
            if (stone.Uid <= 0 || stone.StackQuantity <= 0 || !IsSoulStone(stone.Gid)) continue;
            if (unique.TryGetValue(stone.Uid, out var prior))
            {
                if (prior.Gid != stone.Gid) return SoulContentsResult.Unknown(InconsistentUid, CaptureNeeded);
                continue;
            }
            unique[stone.Uid] = stone;
        }
        if (unique.Count == 0) return SoulContentsResult.Known(new Dictionary<int, int>());

        var quantities = new Dictionary<int, int>();
        foreach (var stone in unique.Values)
        {
            // Une pierre présente sans monstre décodé n'est pas une pierre vide : on refuse tout le stock.
            if (!TryAddDecodedMonsters(stone.Effects, quantities))
                return SoulContentsResult.Unknown(UnknownReason, CaptureNeeded);
        }
        return SoulContentsResult.Known(quantities);
    }

    private static bool TryAddDecodedMonsters(IReadOnlyList<InventoryReader.ItemEffect> effects, IDictionary<int, int> quantities)
    {
        _ = effects.Count;
        _ = quantities;
        return false;
    }
}

public static class SoulStock
{
    public const string BagMissing = "Inventaire non reçu : stock partiel. Aucune écriture.";
    public const string ChestMissing = "Coffre non reçu : stock partiel. Aucune écriture.";
    public const string OutsideScope = "Des pierres d'âme sont hors du périmètre choisi : stock partiel. Aucune écriture.";
    public const string WrongServer = "La quête n'est pas sur le serveur choisi. Aucune écriture.";
    public const string InvalidQuest = "Quête invalide. Aucune écriture.";

    public static SoulContentsResult Evaluate(SoulScope scope, SoulHoldingsSnapshot holdings)
    {
        if (!holdings.BagSeeded) return SoulContentsResult.Unknown(BagMissing, null);
        if (!holdings.ChestSeeded) return SoulContentsResult.Unknown(ChestMissing, null);

        var included = new List<SoulStoneObservation>();
        var excluded = new List<SoulStoneObservation>();
        Take(scope is SoulScope.Bag or SoulScope.Both, holdings.Bag, included, excluded);
        Take(scope is SoulScope.Chest or SoulScope.Both, holdings.Chest, included, excluded);
        if (excluded.Count > 0) return SoulContentsResult.Unknown(OutsideScope, SoulStoneContents.CaptureNeeded);
        return SoulStoneContents.Read(included);
    }

    private static void Take(bool inScope, IReadOnlyList<SoulStoneObservation>? items, List<SoulStoneObservation> included, List<SoulStoneObservation> excluded)
    {
        if (items is null) return;
        foreach (var stone in items)
        {
            if (stone.Uid <= 0 || stone.StackQuantity <= 0 || !SoulStoneContents.IsSoulStone(stone.Gid)) continue;
            (inScope ? included : excluded).Add(stone);
        }
    }
}
