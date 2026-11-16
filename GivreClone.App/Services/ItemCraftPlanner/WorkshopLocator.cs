using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using GivreClone.App.Services.LotM;
using GivreClone.Protocol;

namespace GivreClone.App.Services.ItemCraftPlanner;

/// <summary>Live workshop machine: element and skill-instance ids copied from a map interactive.
/// Neither value is invented from a profession name or an element type.</summary>
public readonly record struct ItemCraftWorkshop(int ElementId, int SkillInstanceUid, int SkillId);

/// <summary>Reads the catalog skill id already stored on RecipeData.skillId and matches it to an enabled
/// interactive. ElementId and SkillInstanceUid come only from that interactive. resultLevel is a job level,
/// never an output count. SQLite RecipeData has no output quantity and no workshop fee.</summary>
public static class WorkshopLocator
{
    public static int? SkillId(IReadOnlyDictionary<string, object?>? recipeRow)
    {
        if (recipeRow is null || !recipeRow.TryGetValue("skillId", out var value)) return null;
        return Integral(value);
    }

    /// <summary>First enabled interactive whose catalog skill id matches. Disabled skills, element 0 and
    /// instance 0 are not usable. ElementTypeId is not a skill id.</summary>
    public static ItemCraftWorkshop? Match(int? skillId, IEnumerable<MapInteractive>? interactives)
    {
        if (skillId is not int skill || skill <= 0 || interactives is null) return null;
        foreach (var interactive in interactives)
        {
            if (interactive.ElementId <= 0 || interactive.Skills is null) continue;
            foreach (var candidate in interactive.Skills)
            {
                if (!candidate.Enabled || candidate.SkillId != skill || candidate.SkillInstanceUid <= 0) continue;
                return new ItemCraftWorkshop(interactive.ElementId, candidate.SkillInstanceUid, skill);
            }
        }
        return null;
    }

    /// <summary>Builds a recipe only when the quantities JSON matches the ingredient list one for one.
    /// A missing or shorter list is incomplete. resultQuantity is used only when that column is present.</summary>
    public static ItemCraftProductionRecipe? Build(long resultGid, IReadOnlyDictionary<string, object?>? row,
        IReadOnlyList<(long Gid, int Quantity)>? ingredients, long? kamasPerCraft)
    {
        if (resultGid <= 0 || row is null || ingredients is null || ingredients.Count == 0) return null;
        if (kamasPerCraft is < 0) return null;
        if (!row.TryGetValue("quantities", out var raw)) return null;
        var parsed = IngredientQuantities(Convert.ToString(raw, CultureInfo.InvariantCulture));
        if (parsed is null || parsed.Count != ingredients.Count) return null;
        var inputs = new ItemCraftProductionInput[parsed.Count];
        for (int i = 0; i < parsed.Count; i++)
        {
            var (gid, quantity) = ingredients[i];
            if (gid <= 0 || quantity <= 0 || quantity != parsed[i]) return null;
            inputs[i] = new ItemCraftProductionInput(gid, quantity, false);
        }
        return new ItemCraftProductionRecipe(resultGid, PositiveColumn(row, "resultQuantity"), SkillId(row), kamasPerCraft, inputs);
    }

    /// <summary>One RecipeData row for this result, or null when the table is missing, ambiguous, or its
    /// ingredient quantities do not match. Does not travel and does not invent an element id.</summary>
    public static ItemCraftProductionRecipe? ReadRecipe(GameDb? db, long resultGid)
    {
        if (db is null || resultGid <= 0) return null;
        try
        {
            string id = resultGid.ToString(CultureInfo.InvariantCulture);
            var rows = db.Find("RecipeData", new[] { "resultId=" + id }, 2);
            if (rows.Count != 1) return null;
            var info = db.Recipe(resultGid);
            if (info is null || info.Ingredients.Count == 0) return null;
            var ingredients = info.Ingredients.Select(i => (i.Gid, i.Quantity)).ToArray();
            return Build(resultGid, rows[0], ingredients, null);
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or FormatException
            or InvalidCastException or OverflowException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    public static IReadOnlyList<int>? IngredientQuantities(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var values = new List<int>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out int qty) || qty <= 0)
                    return null;
                values.Add(qty);
            }
            return values;
        }
        catch (JsonException) { return null; }
    }

    private static int? PositiveColumn(IReadOnlyDictionary<string, object?> row, string column)
        => row.TryGetValue(column, out var value) ? Integral(value) : null;

    private static int? Integral(object? value)
    {
        switch (value)
        {
            case byte or sbyte or short or ushort or int or long or uint or ulong:
                try
                {
                    long number = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                    return number > 0 && number <= int.MaxValue ? (int)number : null;
                }
                catch (OverflowException) { return null; }
            case string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                return parsed > 0 && parsed <= int.MaxValue ? (int)parsed : null;
            default:
                return null;
        }
    }
}
