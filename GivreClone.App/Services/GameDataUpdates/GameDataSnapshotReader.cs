using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace GivreClone.App.Services.GameDataUpdates;

/// <summary>
/// A review-only, offline inventory. Its manifest format is local to GivreWork;
/// arbitrary downloaded SQLite files and filenames alone never establish a Dofus version.
/// A matching digest verifies local bytes, NOT publisher authenticity or game compatibility.
/// </summary>
public static class GameDataSnapshotReader
{
    public const int MaxManifestBytes = 65_536;
    public const long MaxSqliteBytes = 1_073_741_824; // 1 GiB
    private const int MaxAssets = 2;
    private const int MaxSchemaColumns = 96;
    private const int MaxTableNames = 512;
    private static readonly object InitLock = new();
    private static bool _initialized;

    public sealed record Asset(string Role, string FileName, long Bytes, string Sha256);
    public sealed record Feature(string Category, string Table, bool Available, IReadOnlyList<string> Columns);

    /// <param name="Source">Untrusted publisher label, never a verified origin.</param>
    /// <param name="VerifiedLocalBytes">Size + SHA-256 matched for all named files at inspection time.</param>
    /// <param name="PublisherVerified">Always false; this reader has no external signed attestation channel.</param>
    /// <param name="SchemaInspected">Expected SQLite headers and bounded schema inspection succeeded.</param>
    public sealed record Snapshot(
        string? Version, string? Source, bool VerifiedLocalBytes, bool PublisherVerified,
        bool SchemaInspected, IReadOnlyList<Asset> Assets,
        IReadOnlyList<Feature> Features, IReadOnlyList<string> Issues)
    {
        public bool Reviewable => VerifiedLocalBytes && SchemaInspected && Issues.Count == 0;
        public bool EligibleForRuntimeImport => false;
    }

    public sealed record CategoryChange(string Category, string State, string? OlderTable, string? NewerTable);
    public sealed record Comparison(string Status, string? OlderVersion, string? NewerVersion,
        IReadOnlyList<CategoryChange> Categories, string Reason)
    {
        public bool RuntimeCompatibleProven => false;
    }

    // Candidate table layouts are discovery hints, not guarantees; no fabricated table names
    // or missing columns are interpreted as game facts.
    private static readonly (string Category, string[] Tables, string[] Columns)[] SchemaCandidates =
    [
        ("items", ["ItemData"], ["id", "nameId", "typeId"]),
        ("spells", ["SpellData"], ["id", "nameId"]),
        ("crafts", ["RecipeData"], ["id", "resultId"]),
        ("maps", ["MapPositionData", "MapPositionsData"], ["id"]),
        ("quests", ["QuestData"], ["id", "nameId"])
    ];

    /// <summary>
    /// Accepts only a caller-selected directory with a small manifest.json and at most
    /// dofus.sqlite + maps.sqlite in that exact directory. No recursion, network or writes.
    /// </summary>
    public static Snapshot ReadDirectory(string folder, string? expectedVersion = null)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return Failure("Missing dataset directory.");
        if (expectedVersion is not null && !ValidVersion(expectedVersion))
            return Failure("Expected game version is invalid.");

        var issues = new List<string>();
        var assets = new List<Asset>();
        var features = new List<Feature>();
        string? version = null;
        string? source = null;
        bool hashesOk = false, schemaOk = false;
        try
        {
            string root = Path.GetFullPath(folder);
            if (!Directory.Exists(root))
                return Failure("Dataset directory does not exist.");
            var directoryInfo = new DirectoryInfo(root);
            if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                return Failure("Directory links are not accepted.");
            string manifestPath = Path.Combine(root, "manifest.json");
            var info = new FileInfo(manifestPath);
            if (!info.Exists || info.Length < 2 || info.Length > MaxManifestBytes)
                return Failure("Missing or oversized manifest.json.");
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                return Failure("Manifest links are not accepted.");

            byte[] documentBytes;
            using (var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                       8192, FileOptions.SequentialScan))
            {
                if (stream.Length != info.Length || stream.Length > MaxManifestBytes)
                    return Failure("Manifest changed during read.");
                documentBytes = new byte[checked((int)stream.Length)];
                stream.ReadExactly(documentBytes);
            }
            // PowerShell/.NET JSON writers can produce UTF-8 with BOM.
            // Accept that byte-order marker without relaxing strict JSON parsing.
            int bom = documentBytes.Length >= 3 && documentBytes[0] == 0xEF
                && documentBytes[1] == 0xBB && documentBytes[2] == 0xBF ? 3 : 0;
            using var document = JsonDocument.Parse(
                new ReadOnlyMemory<byte>(documentBytes, bom, documentBytes.Length - bom),
                new JsonDocumentOptions { MaxDepth = 12, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            var rootJson = document.RootElement;
            RequireObject(rootJson, ["schemaVersion", "source", "gameVersion", "assets"]);
            if (!GetInt(rootJson, "schemaVersion", out var manifestVersion) || manifestVersion != 1)
                throw new InvalidDataException("Unsupported manifest schemaVersion.");
            source = GetString(rootJson, "source");
            if (source is not ("ledouxm/dofus-sqlite" or "OpenDofusDB" or "local-extraction"))
                throw new InvalidDataException("Unrecognized source label.");
            version = GetString(rootJson, "gameVersion");
            if (!ValidVersion(version))
                throw new InvalidDataException("Invalid gameVersion.");
            if (expectedVersion is not null && !string.Equals(expectedVersion, version, StringComparison.Ordinal))
                throw new InvalidDataException("Game version differs from the explicit expectation.");

            var list = rootJson.GetProperty("assets");
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 1 or > MaxAssets)
                throw new InvalidDataException("Expected one or two assets.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in list.EnumerateArray())
            {
                RequireObject(entry, ["role", "file", "bytes", "sha256"]);
                string role = GetString(entry, "role");
                string file = GetString(entry, "file");
                if ((role != "gamedata" || file != "dofus.sqlite") && (role != "maps" || file != "maps.sqlite"))
                    throw new InvalidDataException("Unrecognized asset role or file.");
                if (!seen.Add(role))
                    throw new InvalidDataException("Duplicate asset role.");
                if (!entry.GetProperty("bytes").TryGetInt64(out long length)
                    || length < 4096 || length > MaxSqliteBytes)
                    throw new InvalidDataException("Asset byte size is outside safe limits.");
                string hash = GetString(entry, "sha256");
                if (!IsSha256(hash))
                    throw new InvalidDataException("Invalid asset SHA-256.");
                assets.Add(new Asset(role, file, length, hash.ToLowerInvariant()));
            }
            if (!seen.Contains("gamedata"))
                throw new InvalidDataException("gamedata must be present; standalone maps are not certifiable.");

            foreach (var asset in assets)
            {
                string path = Path.Combine(root, asset.FileName);
                var fileInfo = new FileInfo(path);
                if (!fileInfo.Exists || fileInfo.Length != asset.Bytes
                    || (fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Missing, linked or length-mismatched " + asset.FileName + ".");
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    65_536, FileOptions.SequentialScan);
                if (input.Length != asset.Bytes)
                    throw new InvalidDataException("Asset changed during validation.");
                byte[] digest = SHA256.HashData(input);
                if (!string.Equals(Convert.ToHexString(digest), asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-256 mismatch for " + asset.FileName + ".");
            }
            hashesOk = true;

            foreach (var asset in assets)
            {
                var path = Path.Combine(root, asset.FileName);
                using var header = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] magic = new byte[16];
                header.ReadExactly(magic);
                if (!magic.AsSpan().SequenceEqual("SQLite format 3\0"u8))
                    throw new InvalidDataException("Not an SQLite 3 database: " + asset.FileName + ".");

                using var db = OpenReadOnly(path);
                // The count and column caps prevent a hostile database from generating huge diagnostics.
                using var query = db.CreateCommand();
                query.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name LIMIT 513";
                query.CommandTimeout = 3;
                var tableNames = new HashSet<string>(StringComparer.Ordinal);
                using (var rows = query.ExecuteReader())
                {
                    while (rows.Read())
                    {
                        if (tableNames.Count >= MaxTableNames)
                            throw new InvalidDataException("Too many SQLite tables.");
                        tableNames.Add(rows.GetString(0));
                    }
                }
                if (asset.Role == "gamedata")
                {
                    foreach (var candidate in SchemaCandidates)
                    {
                        string? table = candidate.Tables.FirstOrDefault(tableNames.Contains);
                        var columns = table is null ? Array.Empty<string>() : ReadColumns(db, table);
                        bool present = table is not null && candidate.Columns.All(c => columns.Contains(c, StringComparer.Ordinal));
                        features.Add(new Feature(candidate.Category, table ?? "", present, columns));
                    }
                    // Names of item/spell/quest records depend on a valid translation table.
                    string[] translationColumns = tableNames.Contains("translations")
                        ? ReadColumns(db, "translations") : [];
                    if (!new[] { "id", "lang", "value" }.All(c => translationColumns.Contains(c, StringComparer.Ordinal)))
                        issues.Add("Missing expected translation schema.");
                    if (!features.Any(f => f.Category != "map-interactions" && f.Available))
                        issues.Add("No recognized game data schema.");
                }
                else
                {
                    features.Add(new Feature("map-interactions", "",
                        tableNames.Count > 0, tableNames.Take(24).ToArray()));
                }
            }
            schemaOk = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidDataException or JsonException or SqliteException or ArgumentException or
                                   NotSupportedException or OverflowException or InvalidCastException or FormatException)
        {
            issues.Add("Dataset rejected: " + ex.GetType().Name + (ex is InvalidDataException ? " (" + ex.Message + ")" : ""));
        }
        return new Snapshot(version, source, hashesOk, false, schemaOk, assets, features, issues);
    }

    /// <summary>Review-only comparison of two *separately verified* local manifests.</summary>
    public static Comparison Compare(Snapshot older, Snapshot newer)
    {
        ArgumentNullException.ThrowIfNull(older);
        ArgumentNullException.ThrowIfNull(newer);
        if (!older.Reviewable || !newer.Reviewable)
            return new Comparison("NOT_COMPARABLE", older.Version, newer.Version, [],
                "Both local file hashes and SQLite schemas must validate independently.");
        if (older.Version == newer.Version || !ValidVersion(older.Version) || !ValidVersion(newer.Version)
            || CompareVersion(older.Version!, newer.Version!) >= 0)
            return new Comparison("NOT_COMPARABLE", older.Version, newer.Version, [],
                "Two distinct, correctly ordered game versions are required.");
        var changes = new List<CategoryChange>();
        foreach (string category in SchemaCandidates.Select(c => c.Category).Append("map-interactions"))
        {
            var a = older.Features.FirstOrDefault(f => f.Category == category);
            var b = newer.Features.FirstOrDefault(f => f.Category == category);
            string state = a?.Available == true && b?.Available == true
                ? (a.Table == b.Table && a.Columns.SequenceEqual(b.Columns) ? "SCHEMA_SAME" : "SCHEMA_CHANGED")
                : a?.Available == b?.Available ? "BOTH_UNAVAILABLE" : "AVAILABILITY_CHANGED";
            changes.Add(new CategoryChange(category, state, a?.Table, b?.Table));
        }
        return new Comparison("SCHEMA_ONLY", older.Version, newer.Version, changes,
            "Schema-only comparison. No row equality, publisher attestation, protocol or gameplay compatibility proven.");
    }

    private static Snapshot Failure(string reason)
        => new(null, null, false, false, false, [], [], [reason]);

    private static bool ValidVersion(string? version)
    {
        if (version is null || version.Length is < 7 or > 23) return false;
        string[] parts = version.Split('.');
        return parts.Length == 4 && parts.All(s => s.Length is >= 1 and <= 5
            && s.All(c => c is >= '0' and <= '9')
            && int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int n)
            && n is >= 0 and <= 65535);
    }

    private static int CompareVersion(string a, string b)
    {
        var partsA = a.Split('.'); var partsB = b.Split('.');
        for (int i = 0; i < 4; i++)
        {
            int cmp = int.Parse(partsA[i], CultureInfo.InvariantCulture).CompareTo(
                int.Parse(partsB[i], CultureInfo.InvariantCulture));
            if (cmp != 0) return cmp;
        }
        return 0;
    }

    private static bool IsSha256(string hash)
        => hash.Length == 64 && hash.All(c => (c >= '0' && c <= '9')
            || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

    private static string GetString(JsonElement obj, string property)
    {
        if (!obj.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Missing or non-string " + property + ".");
        return value.GetString() ?? "";
    }

    private static bool GetInt(JsonElement obj, string property, out int value)
    {
        value = 0;
        return obj.TryGetProperty(property, out var element)
            && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }

    private static void RequireObject(JsonElement element, string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Expected JSON object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!seen.Add(property.Name) || !allowed.Contains(property.Name, StringComparer.Ordinal))
                throw new InvalidDataException("Duplicate or unknown manifest property.");
        if (seen.Count != allowed.Length)
            throw new InvalidDataException("Missing manifest property.");
    }

    private static SqliteConnection OpenReadOnly(string file)
    {
        lock (InitLock)
        {
            if (!_initialized) { SQLitePCL.Batteries.Init(); _initialized = true; }
        }
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = file,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();
        var connection = new SqliteConnection(cs);
        try
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA query_only = ON;";
            cmd.CommandTimeout = 3;
            cmd.ExecuteNonQuery();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private static string[] ReadColumns(SqliteConnection connection, string table)
    {
        // Caller table is a hard-coded literal from SchemaCandidates, never supplied by input.
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(\"" + table + "\")";
        cmd.CommandTimeout = 3;
        var columns = new List<string>();
        using var rows = cmd.ExecuteReader();
        while (rows.Read())
        {
            if (columns.Count >= MaxSchemaColumns)
                throw new InvalidDataException("Too many columns in SQLite table.");
            columns.Add(rows.GetString(1));
        }
        return columns.ToArray();
    }
}
