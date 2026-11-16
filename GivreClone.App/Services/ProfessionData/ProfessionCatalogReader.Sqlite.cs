using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace GivreClone.App.Services.ProfessionData;

public static partial class ProfessionCatalogReader
{
    private static void ReadSqlite(string path, string language,
        List<ProfessionItem> items, List<ProfessionJob> jobs,
        List<ProfessionRecipe> recipes)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            byte[] prefix = new byte[16];
            stream.ReadExactly(prefix);
            if (!prefix.AsSpan().SequenceEqual("SQLite format 3\0"u8))
                throw new InvalidDataException("Not a SQLite 3 file.");
        }
        lock (SqliteInit)
            if (!_sqliteInitialized) { SQLitePCL.Batteries.Init(); _sqliteInitialized = true; }
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            Cache = SqliteCacheMode.Private
        }.ToString();
        using var db = new SqliteConnection(connectionString);
        db.Open();
        Execute(db, "PRAGMA query_only=ON; PRAGMA trusted_schema=OFF;");
        RequireTable(db, "ItemData", "id", "nameId");
        RequireTable(db, "RecipeData", "id", "jobId", "quantities", "resultId", "resultLevel");
        RequireTable(db, "RecipeData_ingredientIds_junction", "RecipeData_id", "target_id");
        bool hasItemLevel = HasColumn(db, "ItemData", "level");
        bool hasTranslations = HasTable(db, "translations") &&
            new[] { "id", "lang", "value" }.All(c => HasColumn(db, "translations", c));
        bool hasJobs = HasTable(db, "JobData") &&
            new[] { "id", "nameId" }.All(c => HasColumn(db, "JobData", c));
        string itemQuery = "SELECT i.id, " +
            (hasItemLevel ? "i.level" : "NULL") + ", " +
            (hasTranslations ? "t.value" : "NULL") +
            " FROM ItemData i " +
            (hasTranslations
                ? "LEFT JOIN translations t ON t.id=CAST(i.nameId AS TEXT) AND t.lang=$lang "
                : "") +
            "ORDER BY i.id LIMIT " + (MaxItems + 1).ToString(CultureInfo.InvariantCulture);
        using (var command = Prepare(db, itemQuery, hasTranslations ? language : null))
        using (var rows = command.ExecuteReader())
        {
            while (rows.Read())
            {
                if (items.Count == MaxItems) throw new InvalidDataException("Too many items.");
                long id = ReadPositiveLong(rows, 0);
                int? level = rows.IsDBNull(1) ? null : ItemLevel(rows.GetValue(1));
                string? name = SafeName(rows, 2);
                items.Add(new ProfessionItem(id, name, level));
            }
        }
        if (hasJobs)
        {
            using var command = Prepare(db,
                "SELECT j.id, " +
                (hasTranslations ? "t.value" : "NULL") + " FROM JobData j " +
                (hasTranslations
                    ? "LEFT JOIN translations t ON t.id=CAST(j.nameId AS TEXT) AND t.lang=$lang "
                    : "") +
                "ORDER BY j.id LIMIT " + (MaxJobs + 1).ToString(CultureInfo.InvariantCulture),
                hasTranslations ? language : null);
            using var rows = command.ExecuteReader();
            while (rows.Read())
            {
                if (jobs.Count == MaxJobs) throw new InvalidDataException("Too many jobs.");
                jobs.Add(new ProfessionJob(checked((int)ReadPositiveLong(rows, 0)), SafeName(rows, 1)));
            }
        }
        var ingredients = new Dictionary<string, List<long>>(StringComparer.Ordinal);
        int ingredientsTotal = 0;
        using (var command = Prepare(db, "SELECT RecipeData_id,target_id FROM " +
            "RecipeData_ingredientIds_junction ORDER BY rowid LIMIT " +
            (MaxIngredients + 1).ToString(CultureInfo.InvariantCulture)))
        using (var rows = command.ExecuteReader())
        {
            while (rows.Read())
            {
                if (++ingredientsTotal > MaxIngredients)
                    throw new InvalidDataException("Too many ingredient links.");
                string id = ReadIdentity(rows, 0);
                long target = ReadPositiveLong(rows, 1);
                if (!ingredients.TryGetValue(id, out var list))
                    ingredients.Add(id, list = []);
                if (list.Count == 32) throw new InvalidDataException("Recipe has too many ingredients.");
                list.Add(target);
            }
        }
        using (var command = Prepare(db,
            "SELECT id, jobId, quantities, resultId, resultLevel FROM RecipeData " +
            "ORDER BY resultId,id LIMIT " + (MaxRecipes + 1).ToString(CultureInfo.InvariantCulture)))
        using (var rows = command.ExecuteReader())
        {
            while (rows.Read())
            {
                if (recipes.Count == MaxRecipes) throw new InvalidDataException("Too many recipes.");
                string recipeId = ReadIdentity(rows, 0);
                if (!ingredients.Remove(recipeId, out var ids) || ids.Count == 0)
                    throw new InvalidDataException("Missing ingredient links for recipe " + recipeId);
                if (rows.IsDBNull(2))
                    throw new InvalidDataException("Recipe quantities are null.");
                string quantityJson = rows.GetString(2);
                if (quantityJson.Length > 4096)
                    throw new InvalidDataException("Recipe quantities are oversized.");
                using var quantitiesDoc = JsonDocument.Parse(quantityJson,
                    new JsonDocumentOptions { MaxDepth = 3 });
                if (quantitiesDoc.RootElement.ValueKind != JsonValueKind.Array ||
                    quantitiesDoc.RootElement.GetArrayLength() != ids.Count)
                    throw new InvalidDataException("Ingredient and quantity counts differ.");
                var parts = new List<ProfessionIngredient>(ids.Count);
                int n = 0;
                foreach (var qty in quantitiesDoc.RootElement.EnumerateArray())
                {
                    if (!qty.TryGetInt32(out int amount) || amount is < 1 or > 1_000_000)
                        throw new InvalidDataException("Invalid ingredient quantity.");
                    parts.Add(new ProfessionIngredient(ids[n++], amount));
                }
                recipes.Add(new ProfessionRecipe(recipeId,
                    checked((int)ReadPositiveLong(rows, 1)), ReadPositiveLong(rows, 3),
                    CheckedLevel(rows.GetValue(4)),
                    ResultQuantity: null, ExperiencePerCraft: null, parts.ToArray()));
            }
        }
        if (ingredients.Count != 0)
            throw new InvalidDataException("Orphaned ingredient junction records.");
        // Actual offline game exports can reference ingredient IDs absent from
        // ItemData. Keep the observed ID and explicitly mark metadata UNKNOWN.
        // Fabricating a name, level or rejecting an otherwise complete recipe
        // would lose useful evidence of the raw ingredient links.
        var knownItemIds = items.Select(i => i.Id).ToHashSet();
        foreach (long id in recipes.SelectMany(r => r.Ingredients.Select(i => i.ItemId))
            .Concat(recipes.Select(r => r.ResultItemId)).Distinct().OrderBy(id => id))
        {
            if (!knownItemIds.Contains(id))
            {
                if (items.Count == MaxItems)
                    throw new InvalidDataException("Too many items including missing references.");
                items.Add(new ProfessionItem(id, null, null, IsPlaceholder: true));
            }
        }
        // SQLite RecipeData does not contain craft XP, output stack count or XP thresholds.
        // resultLevel is a level requirement and ItemData.craftXpRatio is not XP.
    }

    private static SqliteCommand Prepare(SqliteConnection db, string sql, string? language = null)
    {
        var command = db.CreateCommand();
        command.CommandTimeout = 10;
        command.CommandText = sql;
        if (language is not null) command.Parameters.AddWithValue("$lang", language);
        return command;
    }

    private static void Execute(SqliteConnection db, string sql)
    {
        using var command = Prepare(db, sql);
        command.ExecuteNonQuery();
    }

    private static bool HasTable(SqliteConnection db, string table)
    {
        using var command = Prepare(db,
            "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name LIMIT 1");
        command.Parameters.AddWithValue("$name", table);
        return command.ExecuteScalar() is not null;
    }

    private static bool HasColumn(SqliteConnection db, string table, string column)
    {
        // The table string is strictly one of the hard-coded names in this file.
        using var command = Prepare(db, "PRAGMA table_info(\"" + table + "\")");
        using var rows = command.ExecuteReader();
        int count = 0;
        while (rows.Read())
        {
            if (++count > 96) throw new InvalidDataException("Too many table columns.");
            if (rows.GetString(1) == column) return true;
        }
        return false;
    }

    private static void RequireTable(SqliteConnection db, string table, params string[] columns)
    {
        if (!HasTable(db, table) || columns.Any(c => !HasColumn(db, table, c)))
            throw new InvalidDataException("Missing required table/columns: " + table);
    }

    private static string ReadIdentity(SqliteDataReader row, int column)
    {
        if (row.IsDBNull(column)) throw new InvalidDataException("Null recipe identity.");
        string? value = Convert.ToString(row.GetValue(column), CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            throw new InvalidDataException("Invalid recipe identity.");
        return value;
    }

    private static long ReadPositiveLong(SqliteDataReader row, int column)
    {
        if (row.IsDBNull(column)) throw new InvalidDataException("Null numeric identity.");
        long n = Convert.ToInt64(row.GetValue(column), CultureInfo.InvariantCulture);
        if (n < 1) throw new InvalidDataException("Nonpositive numeric identity.");
        return n;
    }

    private static int CheckedLevel(object raw)
    {
        int level = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
        if (level is < 1 or > 200) throw new InvalidDataException("Invalid level.");
        return level;
    }

    private static int? ItemLevel(object raw)
    {
        int level = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
        // ItemData has level=0 on real local exports. This is not a profession
        // level gate and must not be represented as a known positive level.
        if (level == 0) return null;
        if (level is < 0 or > 200) throw new InvalidDataException("Invalid item level.");
        return level;
    }

    private static string? SafeName(SqliteDataReader rows, int column)
    {
        if (rows.IsDBNull(column)) return null;
        string value = rows.GetString(column);
        if (value.Length > 256) throw new InvalidDataException("Oversized translation.");
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
