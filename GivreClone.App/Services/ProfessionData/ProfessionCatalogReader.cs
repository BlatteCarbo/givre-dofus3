using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace GivreClone.App.Services.ProfessionData;

/// <summary>
/// Only caller-selected, offline, explicitly pinned data is admitted. Manifest statements
/// about permission and game version are claims, not independently verified attestations.
/// Public DofusDB API exports are outside the accepted source labels.
/// </summary>
public static partial class ProfessionCatalogReader
{
    public const int MaxManifestBytes = 65_536;
    public const long MaxSqliteBytes = 1_073_741_824;
    public const int MaxJsonBytes = 24 * 1024 * 1024;
    public const int MaxItems = 100_000;
    public const int MaxJobs = 1_000;
    public const int MaxRecipes = 50_000;
    public const int MaxIngredients = 500_000;
    public const int MaxThresholds = 50_000;
    private static readonly Regex VersionPattern = new(@"^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly object SqliteInit = new();
    private static bool _sqliteInitialized;

    /// <summary>
    /// manifest.json, plus either recipes.json or dofus.sqlite in the SAME directory.
    /// No recursion, fallback search, network, writes, runtime client or price inference.
    /// An invalid record rejects the whole catalog (no misleading partial graph).
    /// </summary>
    public static ProfessionCatalog LoadDirectory(string directory, string? expectedGameVersion = null,
        string language = "fr")
    {
        if (string.IsNullOrWhiteSpace(directory))
            return ProfessionCatalog.Rejected("Missing catalog directory.");
        if (expectedGameVersion is not null && !VersionPattern.IsMatch(expectedGameVersion))
            return ProfessionCatalog.Rejected("Invalid expected game version.");
        if (language is not ("fr" or "en"))
            return ProfessionCatalog.Rejected("Unsupported translation language.");
        try
        {
            string root = Path.GetFullPath(directory);
            var dir = new DirectoryInfo(root);
            if (!dir.Exists || (dir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Directory is missing or linked.");
            string manifestPath = Path.Combine(root, "manifest.json");
            byte[] manifestBytes = ReadBounded(manifestPath, MaxManifestBytes);
            using var manifest = JsonDocument.Parse(manifestBytes,
                new JsonDocumentOptions { MaxDepth = 8 });
            var meta = manifest.RootElement;
            RequireFields(meta, ["schemaVersion", "gameVersion", "source", "permission", "format", "asset"]);
            if (Integer(meta, "schemaVersion", 1, 1) != 1)
                throw new InvalidDataException("Unsupported manifest schemaVersion.");
            string version = String(meta, "gameVersion", 32);
            if (!VersionPattern.IsMatch(version) ||
                version.Split('.').Any(x => !ushort.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
                throw new InvalidDataException("Invalid game version.");
            if (expectedGameVersion is not null && version != expectedGameVersion)
                throw new InvalidDataException("Game version mismatch.");
            string source = String(meta, "source", 64);
            // A provenance label by itself cannot prove a licence. Only the named
            // locally owned/private channels are permitted for this adapter.
            if (source is not ("local-extraction" or "privately-licensed-offline"))
                throw new InvalidDataException("Source not admitted: public API input is forbidden.");
            string permission = String(meta, "permission", 64);
            if (permission != "owner-authorized")
                throw new InvalidDataException("Explicit owner permission missing.");
            string format = String(meta, "format", 16);
            if (format is not ("json" or "sqlite"))
                throw new InvalidDataException("Unknown asset format.");
            var asset = meta.GetProperty("asset");
            RequireFields(asset, ["file", "bytes", "sha256"]);
            string fileName = String(asset, "file", 64);
            if (fileName != (format == "json" ? "recipes.json" : "dofus.sqlite"))
                throw new InvalidDataException("Asset must have the canonical local file name.");
            long bytes = Integer(asset, "bytes", 2, format == "json" ? MaxJsonBytes : MaxSqliteBytes);
            if (format == "sqlite" && bytes < 4096)
                throw new InvalidDataException("SQLite file is too small.");
            string hash = String(asset, "sha256", 64);
            if (hash.Length != 64 || hash.Any(x => !Uri.IsHexDigit(x)))
                throw new InvalidDataException("SHA-256 is missing or malformed.");
            string path = Path.Combine(root, fileName);
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length != bytes || (fi.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Asset is absent, linked or has unexpected byte count.");
            if (format == "sqlite" && (File.Exists(path + "-wal") || File.Exists(path + "-shm") || File.Exists(path + "-journal")))
                throw new InvalidDataException("Mutable SQLite sidecar exists: require a sealed snapshot.");
            string actual = HashFile(path);
            if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256 mismatch.");

            var items = new List<ProfessionItem>();
            var jobs = new List<ProfessionJob>();
            var recipes = new List<ProfessionRecipe>();
            var thresholds = new List<ProfessionLevelThreshold>();
            if (format == "json")
                ReadJson(path, items, jobs, recipes, thresholds);
            else
                ReadSqlite(path, language, items, jobs, recipes);
            if (new FileInfo(path).Length != bytes || HashFile(path) != actual)
                throw new InvalidDataException("Asset changed during reading.");
            Validate(items, jobs, recipes, thresholds);
            return new ProfessionCatalog(
                new ProfessionSource(version, source, permission, actual.ToLowerInvariant(), format, true),
                items.OrderBy(i => i.Id).ToArray(),
                jobs.OrderBy(j => j.Id).ToArray(),
                recipes.OrderBy(r => r.JobId).ThenBy(r => r.RequiredLevel ?? int.MaxValue)
                    .ThenBy(r => r.ResultItemId).ThenBy(r => r.Id, StringComparer.Ordinal).ToArray(),
                thresholds.OrderBy(t => t.JobId).ThenBy(t => t.Level).ToArray(),
                [], true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidDataException or JsonException or SqliteException or
                                   ArgumentException or NotSupportedException or OverflowException or
                                   FormatException or InvalidOperationException)
        {
            return ProfessionCatalog.Rejected("Catalog rejected: " +
                (ex is InvalidDataException ? ex.Message : ex.GetType().Name));
        }
    }

    private static byte[] ReadBounded(string path, int maximum)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists || fi.Length is < 2 || (fi.Attributes & FileAttributes.ReparsePoint) != 0 ||
            fi.Length > maximum)
            throw new InvalidDataException("Missing, linked or oversized JSON file.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            8192, FileOptions.SequentialScan);
        if (stream.Length != fi.Length) throw new InvalidDataException("File changed during read.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65_536, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void RequireFields(JsonElement node, string[] allowed, bool optional = false)
    {
        if (node.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected JSON object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prop in node.EnumerateObject())
            if (!seen.Add(prop.Name) || !allowed.Contains(prop.Name, StringComparer.Ordinal))
                throw new InvalidDataException("Unknown or repeated property: " + prop.Name);
        if (!optional && seen.Count != allowed.Length)
            throw new InvalidDataException("Missing JSON field.");
    }

    private static string String(JsonElement node, string field, int maximum)
    {
        if (!node.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Expected text: " + field);
        string str = value.GetString() ?? "";
        if (str.Length is < 1 || str.Length > maximum) throw new InvalidDataException("Invalid length: " + field);
        return str;
    }

    private static long Integer(JsonElement node, string field, long minimum, long maximum)
    {
        if (!node.TryGetProperty(field, out var value) || !value.TryGetInt64(out long n) ||
            n < minimum || n > maximum)
            throw new InvalidDataException("Invalid integer: " + field);
        return n;
    }

    private static int? OptionalInt(JsonElement node, string field, int min, int max)
        => !node.TryGetProperty(field, out var x) || x.ValueKind == JsonValueKind.Null
            ? null : checked((int)Integer(node, field, min, max));

    private static long? OptionalLong(JsonElement node, string field, long min, long max)
        => !node.TryGetProperty(field, out var x) || x.ValueKind == JsonValueKind.Null
            ? null : Integer(node, field, min, max);

    private static string? OptionalName(JsonElement node, string field)
    {
        if (!node.TryGetProperty(field, out var x) || x.ValueKind == JsonValueKind.Null) return null;
        return String(node, field, 256);
    }

    private static JsonElement Array(JsonElement node, string field, int maximum, bool required = true)
    {
        if (!node.TryGetProperty(field, out var x))
        {
            if (required) throw new InvalidDataException("Missing array " + field);
            return default;
        }
        if (x.ValueKind != JsonValueKind.Array || x.GetArrayLength() > maximum)
            throw new InvalidDataException("Invalid or oversized array " + field);
        return x;
    }

    private static void ReadJson(string path, List<ProfessionItem> items,
        List<ProfessionJob> jobs, List<ProfessionRecipe> recipes,
        List<ProfessionLevelThreshold> thresholds)
    {
        using var document = JsonDocument.Parse(ReadBounded(path, MaxJsonBytes),
            new JsonDocumentOptions { MaxDepth = 12 });
        var data = document.RootElement;
        RequireFields(data, ["schemaVersion", "items", "jobs", "recipes", "levelThresholds"]);
        if (Integer(data, "schemaVersion", 1, 1) != 1)
            throw new InvalidDataException("Unsupported recipes.json schema.");
        foreach (var it in Array(data, "items", MaxItems).EnumerateArray())
        {
            RequireFields(it, ["id", "name", "level"], optional: true);
            items.Add(new ProfessionItem(Integer(it, "id", 1, long.MaxValue),
                OptionalName(it, "name"), OptionalInt(it, "level", 1, 200)));
        }
        foreach (var job in Array(data, "jobs", MaxJobs).EnumerateArray())
        {
            RequireFields(job, ["id", "name"], optional: true);
            jobs.Add(new ProfessionJob(checked((int)Integer(job, "id", 1, int.MaxValue)),
                OptionalName(job, "name")));
        }
        int totalIngredients = 0;
        foreach (var recipe in Array(data, "recipes", MaxRecipes).EnumerateArray())
        {
            RequireFields(recipe, ["id", "jobId", "resultItemId", "requiredLevel",
                "resultQuantity", "experiencePerCraft", "ingredients"], optional: true);
            var ingredients = new List<ProfessionIngredient>();
            foreach (var part in Array(recipe, "ingredients", 32).EnumerateArray())
            {
                RequireFields(part, ["itemId", "quantity"]);
                ingredients.Add(new ProfessionIngredient(
                    Integer(part, "itemId", 1, long.MaxValue),
                    checked((int)Integer(part, "quantity", 1, 1_000_000))));
                if (++totalIngredients > MaxIngredients)
                    throw new InvalidDataException("Too many ingredients.");
            }
            recipes.Add(new ProfessionRecipe(String(recipe, "id", 128),
                checked((int)Integer(recipe, "jobId", 1, int.MaxValue)),
                Integer(recipe, "resultItemId", 1, long.MaxValue),
                OptionalInt(recipe, "requiredLevel", 1, 200),
                OptionalInt(recipe, "resultQuantity", 1, 1_000_000),
                OptionalLong(recipe, "experiencePerCraft", 0, long.MaxValue),
                ingredients.ToArray()));
        }
        foreach (var threshold in Array(data, "levelThresholds", MaxThresholds).EnumerateArray())
        {
            RequireFields(threshold, ["jobId", "level", "cumulativeExperience"]);
            thresholds.Add(new ProfessionLevelThreshold(
                checked((int)Integer(threshold, "jobId", 1, int.MaxValue)),
                checked((int)Integer(threshold, "level", 1, 200)),
                Integer(threshold, "cumulativeExperience", 0, long.MaxValue)));
        }
    }

    private static void Validate(List<ProfessionItem> items, List<ProfessionJob> jobs,
        List<ProfessionRecipe> recipes, List<ProfessionLevelThreshold> thresholds)
    {
        if (items.GroupBy(i => i.Id).Any(g => g.Count() != 1) ||
            jobs.GroupBy(j => j.Id).Any(g => g.Count() != 1) ||
            recipes.GroupBy(r => r.Id, StringComparer.Ordinal).Any(g => g.Count() != 1) ||
            thresholds.GroupBy(t => (t.JobId, t.Level)).Any(g => g.Count() != 1))
            throw new InvalidDataException("Duplicate catalog identity.");
        if (recipes.Any(r => r.Ingredients.Count is < 1 or > 32 ||
            r.Ingredients.GroupBy(i => i.ItemId).Any(g => g.Count() != 1)))
            throw new InvalidDataException("Missing or duplicate recipe ingredients.");
        var knownItems = items.Select(i => i.Id).ToHashSet();
        var knownJobs = jobs.Select(j => j.Id).ToHashSet();
        if (recipes.Any(r => !knownItems.Contains(r.ResultItemId) ||
            r.Ingredients.Any(i => !knownItems.Contains(i.ItemId)) ||
            jobs.Count > 0 && !knownJobs.Contains(r.JobId)))
            throw new InvalidDataException("Recipe refers to an absent catalog item/job.");
        if (thresholds.Any(t => jobs.Count > 0 && !knownJobs.Contains(t.JobId)) ||
            thresholds.GroupBy(t => t.JobId).Any(group =>
                group.OrderBy(x => x.Level).Zip(group.OrderBy(x => x.Level).Skip(1),
                    (a, b) => b.CumulativeExperience <= a.CumulativeExperience).Any(x => x)))
            throw new InvalidDataException("Invalid job experience thresholds.");
    }
}
