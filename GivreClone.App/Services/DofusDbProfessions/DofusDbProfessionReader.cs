using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GivreClone.App.Services.DofusDbProfessions;

/// <summary>
/// A local, quarantined DofusDB public API capture. Integrity is not game/version,
/// publisher, licence or AI/MCP certification. This DTO must not be sent to MCP.
/// </summary>
public sealed class DofusDbProfessionReview
{
    internal DofusDbProfessionReview(string status, string sourceSha, int pageCount,
        Dictionary<int, string?> items, Dictionary<int, string?> jobs, List<DofusDbRecipeRow> recipes,
        IReadOnlyList<string> issues)
    {
        Status = status;
        SourceProofSha256 = sourceSha;
        PagesVerified = pageCount;
        Items = items;
        Jobs = jobs;
        Recipes = recipes;
        Issues = issues;
    }

    public bool AdmittedForLocalReview => Status == "REVIEW_ONLY_COMPLETE";
    public string Status { get; }
    public string SourceGameVersion => "UNKNOWN";
    public string SourceProofSha256 { get; }
    public bool AllowedForAiOrMcp => false;
    public bool CertifiedForGameRuntime => false;
    public int PagesVerified { get; }
    public int ItemCount => Items.Count;
    public int JobCount => Jobs.Count;
    public int RecipeCount => Recipes.Count;
    public IReadOnlyList<string> Issues { get; }
    internal Dictionary<int, string?> Items { get; }
    internal Dictionary<int, string?> Jobs { get; }
    internal List<DofusDbRecipeRow> Recipes { get; }

    /// <summary>Read-only recipe lines for local UI consultation, never gameplay or MCP admission.</summary>
    public IReadOnlyList<DofusDbRecipeEvidence> FindRecipesForJob(string? jobName, int max = 200)
    {
        if (!AdmittedForLocalReview || string.IsNullOrWhiteSpace(jobName) || max is < 1 or > 200)
            return [];
        var jobs = Jobs.Where(j => string.Equals(j.Value, jobName.Trim(),
            StringComparison.OrdinalIgnoreCase)).ToArray();
        if (jobs.Length != 1) return [];
        return Recipes.Where(r => r.JobId == jobs[0].Key)
            .OrderBy(r => r.RequiredLevel).ThenBy(r => r.ResultId)
            .ThenBy(r => r.Id, StringComparer.Ordinal).Take(max)
            .Select(r => new DofusDbRecipeEvidence(r.Id, r.JobId, r.ResultId,
                r.RequiredLevel, Items.TryGetValue(r.ResultId, out var itemName) &&
                                 !string.IsNullOrWhiteSpace(itemName)
                    ? itemName : r.ResultName,
                r.Ingredients.Select(i => new DofusDbIngredientEvidence(i.ItemId, i.Quantity)).ToArray(),
                ResultQuantity: null, ExperiencePerCraft: null)).ToArray();
    }
}

public sealed record DofusDbIngredientEvidence(int ItemId, int Quantity);

/// <summary>
/// Source records are based on actual captured API bytes and SHA-checked pages.
/// Null result quantity and craft experience explicitly mean UNKNOWN.
/// </summary>
public sealed record DofusDbRecipeEvidence(string Id, int JobId, int ResultItemId,
    int RequiredLevel, string? ResultName, IReadOnlyList<DofusDbIngredientEvidence> Ingredients,
    int? ResultQuantity, long? ExperiencePerCraft);

internal sealed record DofusDbIngredientRow(int ItemId, int Quantity);
internal sealed record DofusDbRecipeRow(string Id, int JobId, int ResultId,
    int RequiredLevel, string? ResultName, IReadOnlyList<DofusDbIngredientRow> Ingredients);

/// <summary>
/// Offline-only adapter for the existing Scrapy review cache. No website, client,
/// socket, MCP or game call is possible here. Fails closed on incomplete capture,
/// source mutation, missing hash, invalid provenance and malformed recipe pairs.
/// </summary>
public static partial class DofusDbProfessionReader
{
    private const int MaxSummaryBytes = 131_072;
    private const int MaxMetaBytes = 8_192;
    private const int MaxPageBytes = 8 * 1024 * 1024;
    private const int PageSize = 50;
    private const int MaxPages = 600;
    private const int MaxRecords = 30_000;
    private static readonly string[] Categories = ["jobs", "items", "recipes"];

    public static DofusDbProfessionReview LoadReview(string? cacheRoot)
    {
        try { return Read(cacheRoot); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidDataException or ArgumentException or JsonException or
                                   NotSupportedException or OverflowException)
        {
            return new DofusDbProfessionReview("BLOCKED", "", 0, [], [], [],
                [ex is InvalidDataException ? ex.Message : ex.GetType().Name]);
        }
    }

    private static DofusDbProfessionReview Read(string? cacheRoot)
    {
        if (string.IsNullOrWhiteSpace(cacheRoot))
            throw new InvalidDataException("Missing full-catalog directory.");
        var root = new DirectoryInfo(Path.GetFullPath(cacheRoot));
        if (!root.Exists || (root.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Full-catalog directory absent or linked.");
        var summaryBytes = ReadBytes(Path.Combine(root.FullName, "review-summary.json"), MaxSummaryBytes);
        using var summary = Parse(summaryBytes);
        var report = summary.RootElement;
        if (String(report, "status") != "REVIEW_ONLY_COMPLETE" ||
            !Boolean(report, "complete") ||
            String(report, "sourceGameVersion") != "UNKNOWN" ||
            Boolean(report, "aiMcpAllowed") ||
            Boolean(report, "authorizedRuntimeUse") ||
            Boolean(report, "gameplayCertified") ||
            Boolean(report, "sourceRightsVerifiedIndependently"))
            throw new InvalidDataException("Capture not a complete isolated review.");
        var collections = GetObject(report, "collections");
        using var proof = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        proof.AppendData(summaryBytes);
        var items = new Dictionary<int, string?>();
        var jobs = new Dictionary<int, string?>();
        var recipes = new List<DofusDbRecipeRow>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        int pageCount = 0;
        foreach (var category in Categories)
        {
            var categoryDir = new DirectoryInfo(Path.Combine(root.FullName, category));
            if (!categoryDir.Exists || (categoryDir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Missing or linked collection: " + category);
            var stats = GetObject(collections, category);
            int total = Number(stats, "expected", 0, MaxRecords);
            if (!Boolean(stats, "complete") || Number(stats, "captured", 0, MaxRecords) != total ||
                total == 0)
                throw new InvalidDataException("Incomplete collection: " + category);
            int expectedPages = (total + PageSize - 1) / PageSize;
            if (Number(stats, "pages", 1, MaxPages) != expectedPages)
                throw new InvalidDataException("Collection page count mismatch: " + category);
            var actualPages = categoryDir.GetFiles("page-*.json", SearchOption.TopDirectoryOnly);
            if (actualPages.Length != expectedPages * 2)
                throw new InvalidDataException("Page/metadata file count mismatch: " + category);
            for (int skip = 0; skip < total; skip += PageSize)
            {
                string prefix = "page-" + skip.ToString("D8", CultureInfo.InvariantCulture);
                var raw = ReadBytes(Path.Combine(categoryDir.FullName, prefix + ".json"), MaxPageBytes);
                var metaBytes = ReadBytes(Path.Combine(categoryDir.FullName, prefix + ".meta.json"), MaxMetaBytes);
                using var meta = Parse(metaBytes);
                var evidence = meta.RootElement;
                string hash = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
                string expectedUrl = $"https://api.dofusdb.fr/{category}?%24limit=50&%24skip={skip}";
                int count = Math.Min(PageSize, total - skip);
                if (Number(evidence, "schema", 1, 1) != 1 ||
                    String(evidence, "category") != category ||
                    Number(evidence, "skip", 0, MaxRecords) != skip ||
                    Number(evidence, "limit", 1, PageSize) != PageSize ||
                    Number(evidence, "total", 1, MaxRecords) != total ||
                    Number(evidence, "records", 1, PageSize) != count ||
                    Number(evidence, "bytes", 1, MaxPageBytes) != raw.Length ||
                    !string.Equals(String(evidence, "sha256"), hash, StringComparison.OrdinalIgnoreCase) ||
                    String(evidence, "sourceUrl") != expectedUrl ||
                    String(evidence, "versionStatus") != "UNKNOWN" ||
                    Boolean(evidence, "aiMcpAllowed") ||
                    Boolean(evidence, "runtimeCertified"))
                    throw new InvalidDataException("Page integrity/provenance mismatch: " + category);
                using var page = Parse(raw);
                var envelope = page.RootElement;
                if (Number(envelope, "total", 1, MaxRecords) != total ||
                    Number(envelope, "skip", 0, MaxRecords) != skip ||
                    Number(envelope, "limit", 1, PageSize) != PageSize)
                    throw new InvalidDataException("Page pagination drift.");
                var data = GetArray(envelope, "data", PageSize);
                if (data.GetArrayLength() != count)
                    throw new InvalidDataException("Partial page encountered.");
                foreach (var row in data.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("Invalid source record.");
                    string identity = String(row, "_id");
                    if (identity.Length > 128 || !identities.Add(category + "/" + identity))
                        throw new InvalidDataException("Duplicate or oversized source identity.");
                    switch (category)
                    {
                        case "jobs":
                            int jobId = Number(row, "id", 1, 1000);
                            if (!jobs.TryAdd(jobId, FrenchName(row, "name")))
                                throw new InvalidDataException("Duplicate job ID.");
                            break;
                        case "items":
                            int itemId = Number(row, "id", 1, int.MaxValue);
                            if (!items.TryAdd(itemId, FrenchName(row, "name")))
                                throw new InvalidDataException("Duplicate item ID.");
                            break;
                        case "recipes":
                            if (recipes.Count >= 50_000)
                                throw new InvalidDataException("Too many recipes.");
                            int profession = Number(row, "jobId", 1, 1000);
                            int result = Number(row, "resultId", 1, int.MaxValue);
                            int level = Number(row, "resultLevel", 1, 200);
                            var ingredientIds = GetArray(row, "ingredientIds", 32);
                            var quantities = GetArray(row, "quantities", 32);
                            if (ingredientIds.GetArrayLength() is < 1 or > 32 ||
                                quantities.GetArrayLength() != ingredientIds.GetArrayLength())
                                throw new InvalidDataException("Ingredient/quantity array mismatch.");
                            var ingredients = new List<DofusDbIngredientRow>();
                            var distinct = new HashSet<int>();
                            int ordinal = 0;
                            foreach (var ingredient in ingredientIds.EnumerateArray())
                            {
                                int gid = Positive(ingredient, int.MaxValue);
                                int qty = Positive(quantities[ordinal++], 1_000_000);
                                if (!distinct.Add(gid)) throw new InvalidDataException("Duplicate ingredient ID.");
                                ingredients.Add(new DofusDbIngredientRow(gid, qty));
                            }
                            recipes.Add(new DofusDbRecipeRow(identity, profession, result, level,
                                FrenchName(row, "resultName"), ingredients.AsReadOnly()));
                            break;
                    }
                }
                proof.AppendData(Encoding.UTF8.GetBytes($"{category}/{skip}/{hash}\n"));
                pageCount++;
                // Catch edits during parse of this page.
                string after = Convert.ToHexString(SHA256.HashData(
                    ReadBytes(Path.Combine(categoryDir.FullName, prefix + ".json"), MaxPageBytes))).ToLowerInvariant();
                if (after != hash) throw new InvalidDataException("Page changed during reading.");
            }
        }
        if (jobs.Count != Number(GetObject(collections, "jobs"), "expected", 1, MaxRecords) ||
            items.Count != Number(GetObject(collections, "items"), "expected", 1, MaxRecords) ||
            recipes.Count != Number(GetObject(collections, "recipes"), "expected", 1, MaxRecords) ||
            recipes.Any(r => !jobs.ContainsKey(r.JobId)))
            throw new InvalidDataException("Record counts or referenced professions inconsistent.");
        if (!summaryBytes.AsSpan().SequenceEqual(ReadBytes(
                Path.Combine(root.FullName, "review-summary.json"), MaxSummaryBytes)))
            throw new InvalidDataException("Review summary changed during reading.");
        return new DofusDbProfessionReview("REVIEW_ONLY_COMPLETE",
            Convert.ToHexString(proof.GetHashAndReset()).ToLowerInvariant(),
            pageCount, items, jobs, recipes, []);
    }

    private static byte[] ReadBytes(string path, int maximum)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists || (fi.Attributes & FileAttributes.ReparsePoint) != 0 ||
            fi.Length is < 2 || fi.Length > maximum) throw new InvalidDataException("Missing/linked/oversized cache file.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
        byte[] result = new byte[checked((int)fi.Length)];
        stream.ReadExactly(result);
        return result;
    }

    private static JsonDocument Parse(byte[] bytes) =>
        JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 96 });
    private static JsonElement GetObject(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Object
            ? value : throw new InvalidDataException("Missing/invalid object: " + key);
    private static JsonElement GetArray(JsonElement element, string key, int max) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array &&
        value.GetArrayLength() <= max ? value : throw new InvalidDataException("Missing/oversized array: " + key);
    private static int Positive(JsonElement element, int max) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number) &&
        number is >= 1 && number <= max ? number : throw new InvalidDataException("Invalid positive integer.");
    private static int Number(JsonElement element, string key, int min, int max) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number) && number >= min && number <= max
            ? number : throw new InvalidDataException("Invalid source integer: " + key);
    private static string String(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String &&
        value.GetString() is { Length: > 0 and <= 2048 } result
            ? result : throw new InvalidDataException("Missing source string: " + key);
    private static bool Boolean(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : throw new InvalidDataException("Missing source boolean: " + key);
    private static string? FrenchName(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var name) || name.ValueKind != JsonValueKind.Object ||
            !name.TryGetProperty("fr", out var language) || language.ValueKind != JsonValueKind.String)
            return null;
        var value = language.GetString();
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 256 ? value : null;
    }
}
