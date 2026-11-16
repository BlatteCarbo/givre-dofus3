using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GivreMcp;

/// <summary>
/// Explicit local operator-review disclosure gate. The user's DofusDB
/// partnership claim does NOT become independently verified here. Only exact
/// pinned local bytes are eligible for consultative, read-only MCP responses.
/// The collected sourceGameVersion remains UNKNOWN.
/// </summary>
public sealed record DofusDbCatalogAccess(
    string? CacheDirectory, string? ExpectedSummarySha256,
    bool OperatorEnabledReviewOnly, string? OperatorScope)
{
    public static DofusDbCatalogAccess FromEnvironment() => new(
        Environment.GetEnvironmentVariable("GIVRE_DOFUSDB_MCP_CACHE_ROOT"),
        Environment.GetEnvironmentVariable("GIVRE_DOFUSDB_MCP_SUMMARY_SHA256"),
        string.Equals(Environment.GetEnvironmentVariable("GIVRE_DOFUSDB_MCP_REVIEW_ONLY"), "1",
            StringComparison.Ordinal),
        Environment.GetEnvironmentVariable("GIVRE_DOFUSDB_MCP_OPERATOR_SCOPE"));
}

public static class DofusDbCatalogReadTools
{
    public const int MaxPageSize = 30;
    public const int MaxResponseBytes = 48 * 1024;
    public const int MaxQueryLength = 80;
    private const int MaxPageBytes = 4 * 1024 * 1024;
    private const int MaxSummaryBytes = 16 * 1024;
    private const int MaxMetaBytes = 2048;
    private const string OperatorScope = "api_ai_mcp";
    private static readonly string[] Collections = ["items", "recipes", "item-sets"];
    private static readonly Dictionary<string, int> PageCaps = new(StringComparer.Ordinal)
    {
        ["items"] = 500, ["recipes"] = 120, ["item-sets"] = 30
    };
    private static readonly Dictionary<string, int> RowCaps = new(StringComparer.Ordinal)
    {
        ["items"] = 25_000, ["recipes"] = 6_000, ["item-sets"] = 2_000
    };
    private static readonly object Sync = new();
    private static string? CachedKey;
    private static Catalog? Cached;

    private sealed record Item(long Id, string? NameFr, string? NameEn, int? Level,
        int? TypeId, long? ItemSetId);
    private sealed record Recipe(long ResultItemId, int? JobId,
        long[] IngredientIds, long[] IngredientQuantities);
    private sealed record Set(long Id, string? NameFr, long[] ItemIds);
    private sealed record Catalog(string SummaryHash, IReadOnlyDictionary<long, Item> Items,
        IReadOnlyDictionary<long, Recipe> Recipes, IReadOnlyDictionary<long, Set> Sets);

    /// <summary>
    /// Tests and local MCP wrappers only. No network, pipes or bot invocation.
    /// Inputs are scalar parameters; paths and rights are supplied out-of-band
    /// by the trusted local server configuration, never by a tool caller.
    /// </summary>
    public static JsonElement Query(string operation, DofusDbCatalogAccess? access,
        string? query = null, long id = 0, int offset = 0, int limit = 20)
    {
        if (operation == "capabilities")
            return Result(new
            {
                status = "KNOWN", schema = "dofusdb-local-mcp-v1",
                operations = new[] { "capabilities", "catalog_status", "search_items",
                    "get_item", "get_recipe", "get_set", "list_sets" },
                maxPageSize = MaxPageSize, maxQueryLength = MaxQueryLength,
                requiresOperatorReview = true, rightsIndependentlyVerified = false,
                gameVersion = "UNKNOWN", gameplayCertified = false,
                readOnly = true, executed = false, clientContacted = false
            });

        if (operation is not ("catalog_status" or "search_items" or "get_item" or
            "get_recipe" or "get_set" or "list_sets"))
            return Fail("INVALID_REQUEST");
        if (offset is < 0 or > 25_000 || limit is < 1 or > MaxPageSize ||
            (operation is "get_item" or "get_recipe" or "get_set" && id <= 0) ||
            (operation == "search_items" && (query is not { Length: >= 2 and <= MaxQueryLength } ||
                query.Any(c => char.IsControl(c)))))
            return Fail("INVALID_REQUEST");

        if (!TryGate(access, out var root, out var expected))
            return Fail("BLOCK", "No explicit operator review+API/AI/MCP scope and pinned source SHA.");

        Catalog catalog;
        try
        {
            lock (Sync)
            {
                // Validate the pinned manifest on every query; cached data are
                // still an immutable hash-checked snapshot of prior source bytes.
                string currentHash = HashFile(SafePath(root, "review-summary.json"), MaxSummaryBytes);
                if (!EqualSha(expected, currentHash)) return Fail("BLOCK", "Local source manifest hash changed.");
                var key = root + "|" + expected;
                if (Cached is null || CachedKey != key)
                {
                    catalog = Load(root, expected);
                    Cached = catalog;
                    CachedKey = key;
                }
                else catalog = Cached;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException or JsonException or
            System.Security.SecurityException or NotSupportedException or OverflowException)
        {
            return Fail("UNKNOWN", "Local cache absent, changed or not fully SHA-verified.");
        }

        var source = new
        {
            kind = "dofusdb", gameVersion = "UNKNOWN",
            sourceGameVersionVerified = false, catalogSha256 = catalog.SummaryHash,
            sourceRights = "OPERATOR_REVIEW_ONLY", rightsIndependentlyVerified = false,
            gameplayCertified = false, serverAckVerified = false
        };
        switch (operation)
        {
            case "catalog_status":
                return Result(new
                {
                    status = "REVIEW_ONLY", items = catalog.Items.Count,
                    recipes = catalog.Recipes.Count, sets = catalog.Sets.Count,
                    source, readOnly = true, executed = false, clientContacted = false
                });
            case "search_items":
                {
                    var matches = catalog.Items.Values
                        .Where(item => item.NameFr?.Contains(query!, StringComparison.OrdinalIgnoreCase) == true ||
                            item.NameEn?.Contains(query!, StringComparison.OrdinalIgnoreCase) == true)
                        .OrderBy(x => x.Id).ToArray();
                    var page = matches.Skip(offset).Take(limit)
                        .Select(i => ItemView(i)).ToArray();
                    return Result(new
                    {
                        status = "REVIEW_ONLY", total = matches.Length, offset, limit,
                        hasMore = (long)offset + page.Length < matches.Length, items = page,
                        source, readOnly = true, executed = false, clientContacted = false
                    });
                }
            case "get_item":
                return catalog.Items.TryGetValue(id, out var item)
                    ? Result(new { status = "REVIEW_ONLY", item = ItemView(item),
                        source, readOnly = true, executed = false, clientContacted = false })
                    : Fail("UNKNOWN", "Item ID is not in the hash-checked cache.");
            case "get_recipe":
                return catalog.Recipes.TryGetValue(id, out var recipe)
                    ? Result(new
                    {
                        status = "REVIEW_ONLY", recipe = new
                        {
                            resultItemId = recipe.ResultItemId, jobId = recipe.JobId,
                            ingredients = recipe.IngredientIds.Select((x, i) => new
                            { itemId = x, quantity = recipe.IngredientQuantities[i] }).ToArray(),
                            resultQuantity = (long?)null, xpPerCraft = (long?)null,
                            secondsPerCraft = (long?)null, hdvPrice = (decimal?)null,
                            recipeVersionVerified = false
                        },
                        routeStatus = "UNKNOWN", routes = Array.Empty<object>(),
                        source, readOnly = true, executed = false, clientContacted = false
                    })
                    : Fail("UNKNOWN", "Recipe for this result item is unavailable.");
            case "get_set":
                return catalog.Sets.TryGetValue(id, out var set)
                    ? Result(new
                    {
                        status = "REVIEW_ONLY",
                        set = SetView(set), source, readOnly = true,
                        executed = false, clientContacted = false
                    })
                    : Fail("UNKNOWN", "Set ID is not in the hash-checked cache.");
            case "list_sets":
                {
                    var sets = catalog.Sets.Values.OrderBy(x => x.Id).ToArray();
                    var page = sets.Skip(offset).Take(limit).Select(SetView).ToArray();
                    return Result(new
                    {
                        status = "REVIEW_ONLY", total = sets.Length, offset, limit,
                        hasMore = (long)offset + page.Length < sets.Length, sets = page,
                        source, readOnly = true, executed = false, clientContacted = false
                    });
                }
            default:
                return Fail("INVALID_REQUEST");
        }
    }

    private static object ItemView(Item item) => new
    {
        itemId = item.Id, nameFr = item.NameFr, nameEn = item.NameEn,
        item.Level, item.TypeId, itemSetId = item.ItemSetId,
        gameVersion = "UNKNOWN", xpPerCraft = (long?)null,
        hdvPrice = (decimal?)null, dropRate = (double?)null
    };
    private static object SetView(Set set) => new
    {
        setId = set.Id, nameFr = set.NameFr, itemIds = set.ItemIds,
        gameVersion = "UNKNOWN", itemVersionVerified = false
    };
    private static JsonElement Fail(string status, string? message = null) =>
        Result(new { status, message, sourceKind = "dofusdb",
            gameVersion = "UNKNOWN", rightsIndependentlyVerified = false,
            readOnly = true, executed = false, clientContacted = false });
    private static JsonElement Result<T>(T obj)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(obj);
        return bytes.Length <= MaxResponseBytes
            ? JsonSerializer.Deserialize<JsonElement>(bytes)
            : JsonSerializer.SerializeToElement(new
            {
                status = "UNKNOWN", message = "Bounded output exceeded.",
                readOnly = true, executed = false, clientContacted = false
            });
    }

    private static bool TryGate(DofusDbCatalogAccess? access, out string root, out string sha)
    {
        root = ""; sha = "";
        if (access is not { OperatorEnabledReviewOnly: true } ||
            !string.Equals(access.OperatorScope, OperatorScope, StringComparison.Ordinal) ||
            !IsSha(access.ExpectedSummarySha256) ||
            string.IsNullOrWhiteSpace(access.CacheDirectory)) return false;
        try
        {
            root = Path.GetFullPath(access.CacheDirectory);
            if (!Path.IsPathFullyQualified(root) ||
                root.StartsWith(@"\\", StringComparison.Ordinal) ||
                (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                return false;
            sha = access.ExpectedSummarySha256!.ToLowerInvariant();
            return true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or
            UnauthorizedAccessException or NotSupportedException) { return false; }
    }
    private static Catalog Load(string root, string expected)
    {
        var manifest = ReadJson(SafePath(root, "review-summary.json"), MaxSummaryBytes);
        using (manifest)
        {
            var json = manifest.RootElement;
            if (!Flag(json, "complete", true) ||
                !Flag(json, "aiMcpAllowed", false) ||
                !Flag(json, "authorizedRuntimeUse", false) ||
                !Flag(json, "gameplayCertified", false) ||
                String(json, "sourceGameVersion") != "UNKNOWN" ||
                !json.TryGetProperty("collections", out var collections))
                throw new InvalidDataException("Review manifest not in expected review-only shape.");

            var items = ReadCollection(root, collections, "items", e =>
            {
                long id = PositiveId(e, "id");
                int? level = OptionalInt(e, "level", 0, 250);
                int? type = OptionalInt(e, "typeId", 0, 1_000_000);
                long? setId = OptionalLong(e, "itemSetId", 1, long.MaxValue);
                return new Item(id, Name(e, "name", "fr"), Name(e, "name", "en"), level, type, setId);
            });
            var recipes = ReadCollection(root, collections, "recipes", e =>
            {
                long resultId = PositiveId(e, "resultId");
                int? job = OptionalInt(e, "jobId", 1, 1_000_000);
                var ingredientIds = LongArray(e, "ingredientIds", 64);
                var amounts = LongArray(e, "quantities", 64);
                if (ingredientIds.Length == 0 || ingredientIds.Length != amounts.Length)
                    throw new InvalidDataException("Incomplete recipe ingredients.");
                return new Recipe(resultId, job, ingredientIds, amounts);
            });
            var sets = ReadCollection(root, collections, "item-sets", e =>
            {
                long setId = PositiveId(e, "id");
                if (!e.TryGetProperty("items", out var elements) ||
                    elements.ValueKind != JsonValueKind.Array ||
                    elements.GetArrayLength() > 128)
                    throw new InvalidDataException("Invalid set membership.");
                var ids = elements.EnumerateArray().Select(x => PositiveId(x, "id")).ToArray();
                if (ids.Distinct().Count() != ids.Length)
                    throw new InvalidDataException("Duplicate item in set.");
                return new Set(setId, Name(e, "name", "fr"), ids);
            });
            return new(expected, items, recipes, sets);
        }
    }

    private static IReadOnlyDictionary<long, T> ReadCollection<T>(
        string root, JsonElement summary, string collection, Func<JsonElement, T> project)
    {
        if (!summary.TryGetProperty(collection, out var row) ||
            !Flag(row, "complete", true) ||
            !TryInt(row, "captured", out var captured) ||
            !TryInt(row, "expected", out var expected) || captured != expected ||
            captured <= 0 || captured > RowCaps[collection] ||
            !TryInt(row, "pages", out var pages) ||
            pages < 1 || pages > PageCaps[collection])
            throw new InvalidDataException("Collection manifest missing or incomplete.");
        var directory = SafePath(root, collection);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Collection missing.");
        var observed = Directory.EnumerateFiles(directory, "page-*.json").ToArray();
        if (observed.Length != pages * 2)
            throw new InvalidDataException("Collection page/metafile counts differ.");
        var records = new Dictionary<long, T>();
        int seen = 0;
        for (int i = 0; i < pages; i++)
        {
            int skip = i * 50;
            var prefix = "page-" + skip.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
            var metaPath = SafePath(directory, prefix + ".meta.json");
            using var metaDoc = ReadJson(metaPath, MaxMetaBytes);
            var meta = metaDoc.RootElement;
            if (String(meta, "category") != collection || !Flag(meta, "runtimeCertified", false) ||
                !Flag(meta, "aiMcpAllowed", false) || String(meta, "versionStatus") != "UNKNOWN" ||
                !TryInt(meta, "skip", out var metaSkip) || metaSkip != skip ||
                !TryInt(meta, "limit", out var pageLimit) || pageLimit != 50 ||
                !TryInt(meta, "total", out var total) || total != captured ||
                !TryInt(meta, "records", out var expectedRecords) ||
                expectedRecords != Math.Min(50, captured - skip) ||
                !TryInt(meta, "bytes", out var byteLength) || byteLength < 2 || byteLength > MaxPageBytes ||
                !IsSha(String(meta, "sha256")))
                throw new InvalidDataException("Page metadata inconsistent.");
            var dataFile = SafePath(directory, prefix + ".json");
            byte[] bytes = ReadBytes(dataFile, MaxPageBytes);
            if (bytes.Length != byteLength ||
                !EqualSha(String(meta, "sha256")!, Convert.ToHexString(SHA256.HashData(bytes))))
                throw new InvalidDataException("Page SHA mismatch.");
            using var pageDoc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            var page = pageDoc.RootElement;
            if (!TryInt(page, "skip", out var pSkip) || pSkip != skip ||
                !TryInt(page, "limit", out var pLimit) || pLimit != 50 ||
                !TryInt(page, "total", out var pTotal) || pTotal != captured ||
                !page.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array ||
                data.GetArrayLength() != expectedRecords)
                throw new InvalidDataException("Page payload inconsistent.");
            foreach (var entry in data.EnumerateArray())
            {
                T value = project(entry);
                long key = value switch
                {
                    Item item => item.Id, Recipe recipe => recipe.ResultItemId,
                    Set set => set.Id, _ => throw new InvalidDataException("Invalid collection.")
                };
                if (!records.TryAdd(key, value)) throw new InvalidDataException("Duplicate catalog ID.");
                seen++;
            }
        }
        if (seen != captured) throw new InvalidDataException("Collection incomplete.");
        return records;
    }
    private static long PositiveId(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var number) ||
            number.ValueKind != JsonValueKind.Number ||
            !number.TryGetInt64(out var id) || id <= 0)
            throw new InvalidDataException("Invalid numeric catalog ID.");
        return id;
    }
    private static long[] LongArray(JsonElement obj, string prop, int max)
    {
        if (!obj.TryGetProperty(prop, out var array) ||
            array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > max)
            throw new InvalidDataException("Invalid ingredient array.");
        return array.EnumerateArray().Select(x =>
        {
            if (x.ValueKind != JsonValueKind.Number || !x.TryGetInt64(out long value) || value <= 0)
                throw new InvalidDataException("Invalid ingredient.");
            return value;
        }).ToArray();
    }
    private static string? Name(JsonElement obj, string name, string language)
    {
        if (!obj.TryGetProperty(name, out var names) || names.ValueKind != JsonValueKind.Object ||
            !names.TryGetProperty(language, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        string? label = value.GetString();
        return label is { Length: > 0 and <= 140 } &&
            !label.Any(char.IsControl) ? label : null;
    }
    private static int? OptionalInt(JsonElement obj, string prop, int min, int max) =>
        obj.TryGetProperty(prop, out var value) && value.TryGetInt32(out int num) &&
        num >= min && num <= max ? num : null;
    private static long? OptionalLong(JsonElement obj, string prop, long min, long max) =>
        obj.TryGetProperty(prop, out var value) && value.TryGetInt64(out var num) &&
        num >= min && num <= max ? num : null;
    private static bool TryInt(JsonElement obj, string prop, out int result)
    {
        result = 0;
        return obj.TryGetProperty(prop, out var el) &&
            el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out result);
    }
    private static bool Flag(JsonElement json, string prop, bool expected) =>
        json.TryGetProperty(prop, out var flag) &&
        flag.ValueKind == (expected ? JsonValueKind.True : JsonValueKind.False);
    private static string? String(JsonElement json, string prop) =>
        json.TryGetProperty(prop, out var element) &&
        element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    private static bool IsSha(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool EqualSha(string a, string b) => IsSha(a) && IsSha(b) &&
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(a), Convert.FromHexString(b));
    private static string SafePath(string root, string leaf)
    {
        string result = Path.Combine(root, leaf);
        if ((File.Exists(result) || Directory.Exists(result)) &&
            (File.GetAttributes(result) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Redirected source blocked.");
        return result;
    }
    private static byte[] ReadBytes(string path, int limit)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is <= 0 || file.Length > limit) throw new InvalidDataException("Source size over limit.");
        var bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        return bytes;
    }
    private static JsonDocument ReadJson(string path, int limit) =>
        JsonDocument.Parse(ReadBytes(path, limit), new JsonDocumentOptions { MaxDepth = 32 });
    private static string HashFile(string path, int limit) =>
        Convert.ToHexString(SHA256.HashData(ReadBytes(path, limit)));
}
