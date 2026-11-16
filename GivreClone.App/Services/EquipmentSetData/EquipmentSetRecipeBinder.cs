using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.App.Services.ProfessionData;

namespace GivreClone.App.Services.EquipmentSetData;

/// <summary>A set piece and zero or more locally recorded craft recipes for that exact item ID.</summary>
public sealed record EquipmentSetRecipePiece(
    EquipmentSetItem Item,
    IReadOnlyList<ProfessionRecipe> Recipes,
    string RecipeStatus);

/// <summary>Consultative join of two independently pinned catalogs. No gameplay validation.</summary>
public sealed record EquipmentSetRecipeProjection(
    bool Available, EquipmentSet? Set,
    IReadOnlyList<EquipmentSetRecipePiece> Pieces,
    string SetVersion, string? RecipeVersion,
    string SetSha256, string? RecipeSha256,
    IReadOnlyList<string> Issues)
{
    public bool AiDisclosureAuthorized => false;
}

public static class EquipmentSetRecipeBinder
{
    /// <summary>
    /// Join only on identical item GIDs and identical gameVersion values.
    /// Recipe absence means only "not found in supplied snapshot", not "cannot be crafted".
    /// Missing ProfessionData permits listing components with an explicit UNKNOWN status.
    /// </summary>
    public static EquipmentSetRecipeProjection ForSet(
        EquipmentSetCatalog? sets, ProfessionCatalog? profession, long setId)
    {
        if (sets is not { Admitted: true } || !sets.Source.LocalHashVerified)
            return Rejected("Equipment set data missing or not admitted.");
        var set = sets.FindSet(setId);
        if (set is null)
            return Rejected("Requested set ID was not found.", sets.Source);
        if (profession is not null &&
            (!profession.Admitted || !profession.Source.LocalHashVerified ||
             !string.Equals(sets.Source.GameVersion, profession.Source.GameVersion,
                 StringComparison.Ordinal)))
            return Rejected("Profession recipes not admitted or game versions do not match.", sets.Source);
        if (profession is not null &&
            (profession.Source.SourceLabel is not ("local-extraction" or "privately-licensed-offline") ||
             profession.Source.PermissionClaim != "owner-authorized"))
            return Rejected("Profession source/permission claim is not admitted.", sets.Source);

        var pieces = new List<EquipmentSetRecipePiece>();
        foreach (var piece in sets.ItemsOfSet(setId))
        {
            var matches = profession?.Recipes
                .Where(r => r.ResultItemId == piece.Id)
                .OrderBy(r => r.Id, StringComparer.Ordinal).ToArray()
                ?? Array.Empty<ProfessionRecipe>();
            if (matches.Length > 32)
                return Rejected("Too many recipes for a single set component.", sets.Source);
            string status = profession is null ? "RECIPE_SOURCE_UNKNOWN" :
                matches.Length == 0 ? "NO_RECIPE_IN_SUPPLIED_SNAPSHOT" :
                "LOCAL_RECIPE_UNVERIFIED";
            pieces.Add(new EquipmentSetRecipePiece(piece, matches, status));
        }
        return new(true, set, pieces.ToArray(),
            sets.Source.GameVersion, profession?.Source.GameVersion,
            sets.Source.FileSha256, profession?.Source.FileSha256,
            profession is null
                ? ["No ProfessionData catalog was supplied; item recipes remain unknown."]
                : ["Local SHA pins do not prove server recipes, price, stock, drop or craft availability."]);
    }

    private static EquipmentSetRecipeProjection Rejected(string reason, EquipmentSetSource? source = null)
        => new(false, null, [], source?.GameVersion ?? "", null,
            source?.FileSha256 ?? "", null, [reason]);
}
