using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using GivreClone.App.Services.GameDataUpdates;
using Xunit;

namespace GivreClone.Tests;

public sealed class GameDataUpdateMapsTests
{
    private const string Verified371212MapsSha =
        "10f0e6f54ec1bd49f3458a55768096813d4f1be3d08afbe91c9d6be479d1cf0b";

    private sealed class Fixture : IDisposable
    {
        internal readonly string Folder = Path.Combine(AppContext.BaseDirectory,
            "GameDataTestCases", Guid.NewGuid().ToString("N"));
        internal string FileName => Path.Combine(Folder, "maps.sqlite");
        internal string Hash => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(FileName))).ToLowerInvariant();

        internal Fixture(bool validSchema = true)
        {
            Directory.CreateDirectory(Folder);
            SQLitePCL.Batteries.Init();
            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = FileName,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = validSchema
                ? """
                  CREATE TABLE map_interactions(mapId INTEGER, worldId INTEGER, gfxId INTEGER, cellId INTEGER, interactionId INTEGER);
                  INSERT INTO map_interactions VALUES(100, 1, 5000, 12, 700);
                  INSERT INTO map_interactions VALUES(100, 1, 5001, 13, 701);
                  INSERT INTO map_interactions VALUES(100, 1, 5002, 14, 702);
                  INSERT INTO map_interactions VALUES(200, 1, 5003, 12, 703);
                  """
                : "CREATE TABLE map_interactions(mapId INTEGER, worldId INTEGER);";
            cmd.ExecuteNonQuery();
        }

        public void Dispose()
        {
            if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
        }
    }

    [Fact]
    public void Exact_hash_and_schema_allow_bounded_numeric_preview_without_runtime_import()
    {
        using var fixture = new Fixture();
        string original = fixture.Hash;
        var data = MapInteractionReviewReader.Preview(fixture.FileName, original, mapId: 100, maxRows: 2);
        Assert.True(data.LocalHashVerified);
        Assert.True(data.SchemaVerified);
        Assert.True(data.Truncated);
        Assert.False(data.PublisherVerified);
        Assert.False(data.RuntimeImportAuthorized);
        Assert.Empty(data.Issues);
        Assert.Equal([12L, 13L], data.Rows.Select(r => r.CellId));
        Assert.All(data.Rows, r => Assert.Equal(100, r.MapId));
        Assert.Equal(original, fixture.Hash);
        Assert.Empty(Directory.GetFiles(fixture.Folder, "maps.sqlite-*"));
    }

    [Fact]
    public void ReadOnly_preview_filters_by_exact_map_id_and_returns_no_fabricated_resource_names()
    {
        using var fixture = new Fixture();
        var data = MapInteractionReviewReader.Preview(fixture.FileName, fixture.Hash, mapId: 200);
        Assert.Single(data.Rows);
        Assert.Equal(5003, data.Rows[0].GfxId);
        Assert.Equal(703, data.Rows[0].InteractionId);
        Assert.False(data.Truncated);
        Assert.Empty(data.Issues);
        Assert.Empty(MapInteractionReviewReader.Preview(fixture.FileName, fixture.Hash, mapId: 300).Rows);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    public void Unbounded_preview_requests_are_refused(int count)
    {
        using var fixture = new Fixture();
        var report = MapInteractionReviewReader.Preview(fixture.FileName, fixture.Hash, maxRows: count);
        Assert.False(report.LocalHashVerified);
        Assert.Empty(report.Rows);
    }

    [Fact]
    public void Incorrect_sha_or_unpinned_file_is_rejected_before_sqlite_open()
    {
        using var fixture = new Fixture();
        var invalid = MapInteractionReviewReader.Preview(fixture.FileName, new string('0', 64));
        Assert.False(invalid.LocalHashVerified);
        Assert.False(invalid.SchemaVerified);
        Assert.NotEmpty(invalid.Issues);
        Assert.False(MapInteractionReviewReader.Preview(fixture.FileName, "missing").LocalHashVerified);
    }

    [Fact]
    public void Unknown_schema_is_never_presented_as_supported_interactions()
    {
        using var fixture = new Fixture(validSchema: false);
        var data = MapInteractionReviewReader.Preview(fixture.FileName, fixture.Hash);
        Assert.True(data.LocalHashVerified);
        Assert.False(data.SchemaVerified);
        Assert.Empty(data.Rows);
    }

    [Fact]
    public void Invalid_negative_map_id_is_refused()
    {
        using var fixture = new Fixture();
        Assert.False(MapInteractionReviewReader.Preview(fixture.FileName, fixture.Hash, mapId: -1).LocalHashVerified);
    }

    [Fact]
    public void If_present_the_pinned_local_371212_archive_is_read_without_rewrite()
    {
        var root = MappingScan.WalkUp(d => File.Exists(Path.Combine(d.FullName, "artifacts", "game-data-review",
            "3.7.12.12", "maps.sqlite")));
        string? local = root is null ? null : Path.Combine(root.FullName, "artifacts", "game-data-review",
            "3.7.12.12", "maps.sqlite");
        // Portable deterministic synthetic tests above remain independent of the optional review archive.
        if (local is null) return;
        string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(local))).ToLowerInvariant();
        Assert.Equal(Verified371212MapsSha, before);
        var report = MapInteractionReviewReader.Preview(local, before, maxRows: 5);
        Assert.True(report.LocalHashVerified, string.Join("; ", report.Issues));
        Assert.True(report.SchemaVerified, string.Join("; ", report.Issues));
        Assert.NotEmpty(report.Rows);
        Assert.Empty(report.Issues);
        Assert.False(report.PublisherVerified);
        Assert.False(report.RuntimeImportAuthorized);
        string after = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(local))).ToLowerInvariant();
        Assert.Equal(before, after);
    }
}
