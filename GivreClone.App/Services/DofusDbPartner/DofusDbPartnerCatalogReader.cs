using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GivreClone.App.Services.DofusDbPartner;

/// <summary>
/// Bounded offline read-only importer for the Scrapy partner-review cache.
/// Page SHA-256 and pagination are checked before any rows are admitted.
/// Rights and game version are not inferred from data or operator assertions.
/// </summary>
public static class DofusDbPartnerCatalogReader
{
    public const int MaxPageBytes = 8 * 1024 * 1024;
    public const int MaxSummaryBytes = 65_536;
    public const int MaxMetaBytes = 8_192;
    public const int MaxPagesPerCollection = 20_000;
    public const int MaxDocumentsPerCollection = 2_000_000;
    private const int PageSize = 50;
    private static readonly string[] AllCollections =
    [
        "items", "item-sets", "recipes", "monsters", "map-positions", "interactives",
        "jobs", "subareas", "areas", "worlds", "map-references", "transport-from-maps",
        "point-of-interest", "dungeons", "quests", "npcs", "achievements", "mounts", "spells"
    ];
    private static readonly string[] IndexedCollections =
    ["items", "item-sets", "recipes", "jobs", "monsters", "map-positions"];
    private static readonly Regex ApprovalId = new(
        @"\A[A-Za-z0-9_.:-]{6,100}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex FileHash = new(
        @"\A[0-9a-fA-F]{64}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Returns a rejected result on any invalid approval, cache, page, SHA,
    /// count or duplicate identity. Never reads network, never creates files.
    /// </summary>
    public static DofusDbPartnerLoadResult LoadDirectory(
        string cacheRoot, DofusDbPartnerAdmission admission)
    {
        if (admission is null || !admission.OperatorApprovedRuntimeUse ||
            !ApprovalId.IsMatch(admission.OperatorEvidenceId ?? ""))
            return DofusDbPartnerLoadResult.Denied("OPERATOR_RUNTIME_APPROVAL_REQUIRED");
        if (string.IsNullOrWhiteSpace(cacheRoot))
            return DofusDbPartnerLoadResult.Denied("CACHE_PATH_REQUIRED");

        try
        {
            string root = Path.GetFullPath(cacheRoot);
            CheckDirectory(root);
            byte[] reviewBytes = ReadFile(Path.Combine(root, "review-summary.json"), MaxSummaryBytes);
            using var review = Parse(reviewBytes);
            var reviewRoot = review.RootElement;
            if (!Bool(reviewRoot, "complete", true) ||
                Number(reviewRoot, "schema") != 1 ||
                String(reviewRoot, "status") != "REVIEW_ONLY_COMPLETE" ||
                String(reviewRoot, "sourceGameVersion") != "UNKNOWN" ||
                !Bool(reviewRoot, "sourceRightsVerifiedIndependently", false) ||
                !Bool(reviewRoot, "aiMcpAllowed", false) ||
                !Bool(reviewRoot, "authorizedRuntimeUse", false) ||
                !Bool(reviewRoot, "gameplayCertified", false))
                throw new InvalidDataException("REVIEW_SAFETY_FIELDS_MISMATCH");

            var summary = reviewRoot.GetProperty("collections");
            if (summary.ValueKind != JsonValueKind.Object ||
                summary.EnumerateObject().Count() != AllCollections.Length ||
                summary.EnumerateObject().Any(x => !AllCollections.Contains(x.Name,
                    StringComparer.Ordinal)))
                throw new InvalidDataException("REVIEW_COLLECTIONS_MISMATCH");
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            long claimedAll = 0;
            foreach (var category in AllCollections)
            {
                var entry = summary.GetProperty(category);
                int expected = checked((int)Number(entry, "expected"));
                int captured = checked((int)Number(entry, "captured"));
                int pages = checked((int)Number(entry, "pages"));
                if (expected < 0 || captured != expected || expected > MaxDocumentsPerCollection ||
                    pages < 0 || pages > MaxPagesPerCollection ||
                    !Bool(entry, "complete", true) ||
                    (expected > 0 && (pages != (expected + PageSize - 1) / PageSize)) ||
                    (expected == 0 && pages != 0))
                    throw new InvalidDataException("REVIEW_COUNTS_INVALID");
                counts.Add(category, captured);
                claimedAll += captured;
            }
            if (claimedAll > 2_000_000)
                throw new InvalidDataException("REVIEW_GLOBAL_DOCUMENT_CAP");

            var items = new Dictionary<long, DofusDbPartnerItem>();
            var sets = new Dictionary<long, DofusDbPartnerSet>();
            var recipes = new Dictionary<long, DofusDbPartnerRecipe>();
            var jobs = new Dictionary<long, DofusDbPartnerJob>();
            var monsters = new Dictionary<long, DofusDbPartnerMonster>();
            var positions = new Dictionary<long, DofusDbPartnerMapPosition>();
            var verified = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var category in IndexedCollections)
            {
                string directory = Path.Combine(root, category);
                CheckDirectory(directory);
                var seenDocuments = new HashSet<string>(StringComparer.Ordinal);
                int expected = counts[category];
                int expectedPages = (expected + PageSize - 1) / PageSize;
                var files = Directory.EnumerateFiles(directory).Select(Path.GetFileName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (files.Count != expectedPages * 2)
                    throw new InvalidDataException("PAGE_FILE_COUNT_MISMATCH");
                for (int n = 0; n < expected; n += PageSize)
                {
                    string prefix = "page-" + n.ToString("D8", CultureInfo.InvariantCulture);
                    if (!files.Contains(prefix + ".json") ||
                        !files.Contains(prefix + ".meta.json"))
                        throw new InvalidDataException("PAGE_FILE_SEQUENCE_MISMATCH");
                }
                int readCount = 0;
                for (int offset = 0; offset < expected; offset += PageSize)
                {
                    var page = LoadVerifiedPage(root, category, offset, expected);
                    using (page)
                    {
                        foreach (var record in page.RootElement.GetProperty("data").EnumerateArray())
                        {
                            string identity = ReadIdentity(record);
                            if (!seenDocuments.Add(identity))
                                throw new InvalidDataException("DUPLICATE_DOCUMENT_ID");
                            switch (category)
                            {
                                case "items":
                                {
                                    var item = new DofusDbPartnerItem(PositiveId(record),
                                        Name(record, "fr"), Name(record, "en"),
                                        OptionalInt(record, "level", 0, 9999),
                                        OptionalId(record, "itemSetId"));
                                    Add(items, item.Id, item); break;
                                }
                                case "item-sets":
                                {
                                    long setId = PositiveId(record);
                                    long[] memberIds = ArrayField(record, "items")
                                        .Select(PositiveId).ToArray();
                                    if (memberIds.Length > 128 ||
                                        memberIds.Distinct().Count() != memberIds.Length)
                                        throw new InvalidDataException("INVALID_SET_MEMBERS");
                                    Add(sets, setId, new DofusDbPartnerSet(setId,
                                        Name(record, "fr"), Name(record, "en"),
                                        Array.AsReadOnly(memberIds)));
                                    break;
                                }
                                case "recipes":
                                {
                                    long recipeId = PositiveId(record);
                                    long resultId = RequiredPositive(record, "resultId");
                                    var ingredientIds = ArrayField(record, "ingredientIds");
                                    var quantities = ArrayField(record, "quantities");
                                    if (ingredientIds.Count != quantities.Count ||
                                        ingredientIds.Count > 64)
                                        throw new InvalidDataException("RECIPE_INGREDIENT_COUNTS_MISMATCH");
                                    var ingredients = new DofusDbPartnerIngredient[ingredientIds.Count];
                                    for (int i = 0; i < ingredients.Length; i++)
                                    {
                                        long gid = PositiveInteger(ingredientIds[i]);
                                        long? qty = quantities[i].ValueKind == JsonValueKind.Null
                                            ? null : PositiveInteger(quantities[i]);
                                        ingredients[i] = new(gid, qty);
                                    }
                                    Add(recipes, recipeId, new DofusDbPartnerRecipe(
                                        recipeId, resultId, null, Array.AsReadOnly(ingredients),
                                        OptionalId(record, "jobId")));
                                    break;
                                }
                                case "jobs":
                                {
                                    long jobId = PositiveId(record);
                                    Add(jobs, jobId, new DofusDbPartnerJob(
                                        jobId, Name(record, "fr"), Name(record, "en")));
                                    break;
                                }
                                case "monsters":
                                {
                                    long monsterId = PositiveId(record);
                                    long[] subareas = ArrayField(record, "subareas")
                                        .Select(PositiveInteger).ToArray();
                                    if (subareas.Length > 2048)
                                        throw new InvalidDataException("MONSTER_SUBAREA_LIMIT");
                                    Add(monsters, monsterId, new DofusDbPartnerMonster(
                                        monsterId, Name(record, "fr"), Name(record, "en"),
                                        Array.AsReadOnly(subareas)));
                                    break;
                                }
                                case "map-positions":
                                {
                                    long mapId = IdAllowZero(record);
                                    var x = RangeInt(record, "posX", -100_000, 100_000);
                                    var y = RangeInt(record, "posY", -100_000, 100_000);
                                    Add(positions, mapId, new DofusDbPartnerMapPosition(
                                        mapId, x, y, OptionalId(record, "subAreaId"),
                                        OptionalId(record, "worldMap")));
                                    break;
                                }
                            }
                            readCount++;
                        }
                    }
                }
                if (readCount != expected || seenDocuments.Count != expected)
                    throw new InvalidDataException("REVIEW_COUNT_NOT_MATCHING_PAGES");
                verified[category] = readCount;
            }

            var provenance = new DofusDbPartnerProvenance(
                "dofusdb", "UNKNOWN", false, false, false,
                admission.OperatorApprovedRuntimeUse,
                admission.OperatorApprovedAiMcpUse,
                admission.OperatorEvidenceId!,
                Convert.ToHexStringLower(SHA256.HashData(reviewBytes)),
                new ReadOnlyDictionary<string, int>(counts),
                new ReadOnlyDictionary<string, int>(verified));
            return new DofusDbPartnerLoadResult(true,
                new DofusDbPartnerCatalog(provenance, items, sets, recipes, jobs, monsters,
                    positions), "READ_ONLY_REVIEW_CATALOG_LOADED");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            InvalidDataException or JsonException or ArgumentException or
            OverflowException or NotSupportedException)
        {
            return DofusDbPartnerLoadResult.Denied(
                ex is InvalidDataException ? ex.Message : "REJECTED_" + ex.GetType().Name);
        }
    }

    private static JsonDocument LoadVerifiedPage(string root, string category,
        int offset, int expected)
    {
        string folder = Path.Combine(root, category);
        string name = "page-" + offset.ToString("D8", CultureInfo.InvariantCulture);
        byte[] metaBytes = ReadFile(Path.Combine(folder, name + ".meta.json"), MaxMetaBytes);
        using var metaDoc = Parse(metaBytes);
        var meta = metaDoc.RootElement;
        int size = checked((int)Number(meta, "bytes"));
        int count = checked((int)Number(meta, "records"));
        string sha = String(meta, "sha256");
        if (Number(meta, "schema") != 1 || String(meta, "category") != category ||
            Number(meta, "skip") != offset || Number(meta, "limit") != PageSize ||
            Number(meta, "total") != expected ||
            count != Math.Min(PageSize, expected - offset) ||
            size is < 2 or > MaxPageBytes || !FileHash.IsMatch(sha) ||
            String(meta, "versionStatus") != "UNKNOWN" ||
            !Bool(meta, "aiMcpAllowed", false) || !Bool(meta, "runtimeCertified", false) ||
            String(meta, "sourceUrl") !=
                $"https://api.dofusdb.fr/{category}?%24limit=50&%24skip={offset}")
            throw new InvalidDataException("CACHE_META_INVALID");
        byte[] bytes = ReadFile(Path.Combine(folder, name + ".json"), MaxPageBytes);
        if (bytes.Length != size ||
            !Convert.ToHexString(SHA256.HashData(bytes)).Equals(sha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CACHE_SHA256_MISMATCH");
        JsonDocument page = Parse(bytes);
        var data = page.RootElement;
        if (Number(data, "skip") != offset || Number(data, "limit") != PageSize ||
            Number(data, "total") != expected ||
            !data.TryGetProperty("data", out var list) ||
            list.ValueKind != JsonValueKind.Array || list.GetArrayLength() != count)
        {
            page.Dispose();
            throw new InvalidDataException("CACHE_ENVELOPE_MISMATCH");
        }
        return page;
    }

    private static void CheckDirectory(string path)
    {
        var dir = new DirectoryInfo(path);
        if (!dir.Exists || (dir.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("CACHE_DIRECTORY_INVALID");
    }
    private static byte[] ReadFile(string path, int cap)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length < 2 || file.Length > int.MaxValue ||
            file.Length > cap || (file.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("CACHE_FILE_MISSING_LINKED_OR_OVERSIZED");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            8192, FileOptions.SequentialScan);
        if (stream.Length != file.Length)
            throw new InvalidDataException("CACHE_FILE_CHANGED");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.Length != bytes.Length) throw new InvalidDataException("CACHE_FILE_CHANGED");
        return bytes;
    }
    private static JsonDocument Parse(byte[] bytes) => JsonDocument.Parse(bytes,
        new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false, MaxDepth = 128 });
    private static long Number(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Number ||
            !el.TryGetInt64(out long value))
            throw new InvalidDataException("INVALID_NUMBER_" + key);
        return value;
    }
    private static int RangeInt(JsonElement obj, string key, int min, int max)
    {
        long n = Number(obj, key);
        if (n < min || n > max) throw new InvalidDataException("INVALID_RANGE_" + key);
        return (int)n;
    }
    private static int? OptionalInt(JsonElement obj, string key, int min, int max)
    {
        if (!obj.TryGetProperty(key, out var el) || el.ValueKind == JsonValueKind.Null)
            return null;
        int n = RangeInt(obj, key, min, max);
        return n;
    }
    private static long? OptionalId(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var el) || el.ValueKind == JsonValueKind.Null)
            return null;
        long value = Number(obj, key);
        return value >= 0 ? value : null;
    }
    private static string String(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("INVALID_TEXT_" + key);
        return el.GetString() ?? "";
    }
    private static bool Bool(JsonElement obj, string key, bool expected)
    {
        if (!obj.TryGetProperty(key, out var el) ||
            el.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("INVALID_BOOL_" + key);
        return el.GetBoolean() == expected;
    }
    private static long RequiredPositive(JsonElement obj, string key)
    {
        var id = Number(obj, key);
        if (id <= 0) throw new InvalidDataException("INVALID_POSITIVE_" + key);
        return id;
    }
    private static long PositiveInteger(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetInt64(out long value) || value <= 0)
            throw new InvalidDataException("INVALID_INTEGER_ARRAY_VALUE");
        return value;
    }
    private static long PositiveId(JsonElement row) => RequiredPositive(row, "id");
    private static long IdAllowZero(JsonElement row)
    {
        long id = Number(row, "id");
        if (id < 0) throw new InvalidDataException("INVALID_DOCUMENT_ID");
        return id;
    }
    private static string ReadIdentity(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("INVALID_RECORD");
        JsonElement identity;
        if (!row.TryGetProperty("_id", out identity) &&
            !row.TryGetProperty("id", out identity))
            throw new InvalidDataException("RECORD_IDENTITY_UNKNOWN");
        string value = identity.ValueKind switch
        {
            JsonValueKind.String => identity.GetString() ?? "",
            JsonValueKind.Number when identity.TryGetInt64(out var n) =>
                n.ToString(CultureInfo.InvariantCulture),
            _ => ""
        };
        if (value.Length is < 1 or > 128) throw new InvalidDataException("INVALID_RECORD_IDENTITY");
        return value;
    }
    private static IReadOnlyList<JsonElement> ArrayField(JsonElement row, string key)
    {
        if (!row.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("INVALID_ARRAY_" + key);
        if (el.GetArrayLength() > 50_000) throw new InvalidDataException("ARRAY_TOO_LARGE");
        return el.EnumerateArray().ToArray();
    }
    private static string? Name(JsonElement row, string language)
    {
        if (!row.TryGetProperty("name", out var names) || names.ValueKind != JsonValueKind.Object ||
            !names.TryGetProperty(language, out var name) ||
            name.ValueKind != JsonValueKind.String)
            return null;
        string? value = name.GetString();
        if (value is null || value.Length > 300 || value.Any(char.IsControl)) return null;
        return value;
    }
    private static void Add<T>(Dictionary<long, T> map, long id, T record)
    {
        if (!map.TryAdd(id, record))
            throw new InvalidDataException("DUPLICATE_BUSINESS_ID");
    }
}
