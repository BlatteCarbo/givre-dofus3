using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace GivreClone.App.Services.GameDataUpdates;

/// <summary>
/// Optional bounded, schema-checked inspection of a single publisher-pinned maps.sqlite.
/// Does not require a full dofus.sqlite dataset and never maps gfxId to a resource name.
/// This is NOT a map navigation graph and never makes an action/packet.
/// </summary>
public static class MapInteractionReviewReader
{
    public const int MaxPreviewRows = 64;
    private static readonly object InitLock = new();
    private static bool _initialized;
    private static readonly string[] RequiredColumns =
        ["mapId", "worldId", "gfxId", "cellId", "interactionId"];

    public sealed record Row(long MapId, long WorldId, long GfxId, long CellId, long InteractionId);
    public sealed record Report(bool LocalHashVerified, bool SchemaVerified, bool PublisherVerified,
        bool Truncated, IReadOnlyList<Row> Rows, IReadOnlyList<string> Issues)
    {
        public bool RuntimeImportAuthorized => false;
    }

    public static Report Preview(string path, string expectedSha256, long? mapId = null, int maxRows = 20)
    {
        if (maxRows is < 1 or > MaxPreviewRows)
            return Failed("maxRows must be 1..64.");
        if (mapId is < 0)
            return Failed("mapId must not be negative.");
        if (string.IsNullOrWhiteSpace(path)
            || Path.GetFileName(path) != "maps.sqlite"
            || !ValidHash(expectedSha256))
            return Failed("Select an exact maps.sqlite and provide a 64-digit SHA-256.");

        bool hashOk = false, schemaOk = false;
        var result = new List<Row>();
        var issues = new List<string>();
        bool truncated = false;
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0
                || file.Length is < 4096 or > GameDataSnapshotReader.MaxSqliteBytes)
                return Failed("Linked, missing or oversized maps.sqlite.");
            using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
                       FileShare.Read, 65_536, FileOptions.SequentialScan))
            {
                if (stream.Length != file.Length)
                    return Failed("maps.sqlite changed during read.");
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)),
                    expectedSha256, StringComparison.OrdinalIgnoreCase))
                    return Failed("SHA-256 mismatch for maps.sqlite.");
            }
            hashOk = true;
            lock (InitLock)
            {
                if (!_initialized) { SQLitePCL.Batteries.Init(); _initialized = true; }
            }
            string connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = file.FullName,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using (var gate = connection.CreateCommand())
            {
                gate.CommandText = "PRAGMA query_only = ON";
                gate.CommandTimeout = 3;
                gate.ExecuteNonQuery();
            }
            var columns = new HashSet<string>(StringComparer.Ordinal);
            using (var schema = connection.CreateCommand())
            {
                schema.CommandText = "PRAGMA table_info(\"map_interactions\")";
                schema.CommandTimeout = 3;
                using var reader = schema.ExecuteReader();
                while (reader.Read())
                {
                    if (columns.Count >= 24)
                        return new Report(true, false, false, false, [], ["Unsupported interaction schema size."]);
                    columns.Add(reader.GetString(1));
                }
            }
            if (!RequiredColumns.All(columns.Contains))
                return new Report(true, false, false, false, [], ["Missing map_interactions columns."]);
            schemaOk = true;
            using var command = connection.CreateCommand();
            command.CommandText = mapId is null
                ? "SELECT mapId, worldId, gfxId, cellId, interactionId FROM map_interactions ORDER BY mapId, cellId LIMIT $max"
                : "SELECT mapId, worldId, gfxId, cellId, interactionId FROM map_interactions WHERE mapId = $map ORDER BY cellId LIMIT $max";
            command.Parameters.AddWithValue("$max", maxRows + 1);
            if (mapId is { } wanted)
                command.Parameters.AddWithValue("$map", wanted);
            command.CommandTimeout = 3;
            using var data = command.ExecuteReader();
            while (data.Read())
            {
                if (result.Count == maxRows) { truncated = true; break; }
                if (data.IsDBNull(0) || data.IsDBNull(1) || data.IsDBNull(2)
                    || data.IsDBNull(3) || data.IsDBNull(4))
                {
                    issues.Add("An interaction row contained an unknown mandatory field.");
                    break;
                }
                result.Add(new Row(data.GetInt64(0), data.GetInt64(1), data.GetInt64(2),
                    data.GetInt64(3), data.GetInt64(4)));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                      SqliteException or InvalidDataException or ArgumentException or
                                      InvalidCastException or FormatException)
        {
            issues.Add("Map review failed: " + error.GetType().Name);
        }
        return new Report(hashOk, schemaOk, false, truncated, result, issues);
    }

    private static bool ValidHash(string? value)
        => value is { Length: 64 }
            && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static Report Failed(string message) => new(false, false, false, false, [], [message]);
}
