using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using GivreClone.App.Services.GameDataUpdates;
using Xunit;

namespace GivreClone.Tests;

public sealed class GameDataUpdateSnapshotTests
{
    // Fixtures live only under the current test output, never in the user data root
    // or either application/install directory. All files are synthetic, offline.
    private sealed class Fixture : IDisposable
    {
        internal readonly string Folder = Path.Combine(AppContext.BaseDirectory,
            "GameDataTestCases", Guid.NewGuid().ToString("N"));

        internal Fixture(bool createDb = true, bool validSchema = true)
        {
            Directory.CreateDirectory(Folder);
            if (createDb) BuildSqlite(validSchema);
        }

        internal string Db => Path.Combine(Folder, "dofus.sqlite");
        internal string Manifest => Path.Combine(Folder, "manifest.json");

        private void BuildSqlite(bool validSchema)
        {
            SQLitePCL.Batteries.Init();
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Db,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString());
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = validSchema
                ? """
                  CREATE TABLE translations (id TEXT, lang TEXT, value TEXT);
                  CREATE TABLE ItemData (id INTEGER, nameId INTEGER, typeId INTEGER);
                  CREATE TABLE SpellData (id INTEGER, nameId INTEGER);
                  CREATE TABLE RecipeData (id TEXT, resultId INTEGER);
                  CREATE TABLE MapPositionData (id INTEGER);
                  CREATE TABLE QuestData (id INTEGER, nameId INTEGER);
                  """
                : "CREATE TABLE foo (id INTEGER);";
            cmd.ExecuteNonQuery();
        }

        internal string Hash() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Db))).ToLowerInvariant();

        internal void WriteManifest(string version = "3.7.11.11", string? overrideSha = null,
            string? file = null, string? source = null, long? bytes = null)
        {
            var payload = new
            {
                schemaVersion = 1,
                source = source ?? "ledouxm/dofus-sqlite",
                gameVersion = version,
                assets = new[]
                {
                    new { role = "gamedata", file = file ?? "dofus.sqlite",
                        bytes = bytes ?? new FileInfo(Db).Length,
                        sha256 = overrideSha ?? Hash() }
                }
            };
            File.WriteAllText(Manifest, JsonSerializer.Serialize(payload), Encoding.UTF8);
        }

        public void Dispose()
        {
            if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
        }
    }

    [Fact]
    public void Local_snapshot_validates_bounded_sqlite_schema_without_modifying_file()
    {
        using var fixture = new Fixture();
        fixture.WriteManifest();
        string hash = fixture.Hash();
        var snapshot = GameDataSnapshotReader.ReadDirectory(fixture.Folder, "3.7.11.11");
        Assert.True(snapshot.Reviewable, string.Join("; ", snapshot.Issues));
        Assert.True(snapshot.VerifiedLocalBytes);
        Assert.True(snapshot.SchemaInspected);
        Assert.False(snapshot.PublisherVerified);
        Assert.False(snapshot.EligibleForRuntimeImport);
        Assert.Equal("3.7.11.11", snapshot.Version);
        Assert.Equal(5, snapshot.Features.Count);
        Assert.All(snapshot.Features, f => Assert.True(f.Available, f.Category));
        Assert.Equal(hash, fixture.Hash());
        Assert.Empty(Directory.GetFiles(fixture.Folder, "dofus.sqlite-*"));
    }

    [Fact]
    public void Two_independently_hashed_versions_compare_schema_only()
    {
        using var older = new Fixture();
        using var newer = new Fixture();
        older.WriteManifest("3.7.11.11");
        newer.WriteManifest("3.7.12.12");
        var comparison = GameDataSnapshotReader.Compare(
            GameDataSnapshotReader.ReadDirectory(older.Folder),
            GameDataSnapshotReader.ReadDirectory(newer.Folder));
        Assert.Equal("SCHEMA_ONLY", comparison.Status);
        Assert.False(comparison.RuntimeCompatibleProven);
        Assert.Contains(comparison.Categories, c => c.Category == "items" && c.State == "SCHEMA_SAME");
        Assert.Contains("No row equality", comparison.Reason);
    }

    [Fact]
    public void Version_mismatch_fails_closed_before_probing_database()
    {
        using var fixture = new Fixture();
        fixture.WriteManifest();
        var mismatch = GameDataSnapshotReader.ReadDirectory(fixture.Folder, "3.7.12.12");
        Assert.False(mismatch.Reviewable);
        Assert.False(mismatch.VerifiedLocalBytes);
        Assert.False(mismatch.SchemaInspected);
    }

    [Fact]
    public void Tampering_with_database_does_not_promote_the_manifest()
    {
        using var fixture = new Fixture();
        fixture.WriteManifest();
        using (var handle = new FileStream(fixture.Db, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            handle.Position = handle.Length - 1;
            handle.WriteByte(0x55);
        }
        var rejected = GameDataSnapshotReader.ReadDirectory(fixture.Folder);
        Assert.False(rejected.VerifiedLocalBytes);
        Assert.False(rejected.Reviewable);
        Assert.Contains(rejected.Issues, reason => reason.Contains("SHA-256", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("../outside.sqlite")]
    [InlineData("DOFUS.SQLITE")]
    [InlineData("maps.sqlite")]
    [InlineData("https://example.invalid/dofus.sqlite")]
    public void Manifest_rejects_asset_path_indirection_and_unsupported_assets(string path)
    {
        using var fixture = new Fixture();
        fixture.WriteManifest(file: path);
        var rejected = GameDataSnapshotReader.ReadDirectory(fixture.Folder);
        Assert.False(rejected.VerifiedLocalBytes);
        Assert.False(rejected.EligibleForRuntimeImport);
    }

    [Theory]
    [InlineData("3.7.12")]
    [InlineData("3.7.12.12-TEST")]
    [InlineData("3.7.12.999999")]
    [InlineData("../3.7.12.12")]
    public void Invalid_version_is_rejected_without_trusting_directory_name(string version)
    {
        using var fixture = new Fixture();
        fixture.WriteManifest(version);
        Assert.False(GameDataSnapshotReader.ReadDirectory(fixture.Folder).Reviewable);
    }

    [Fact]
    public void Oversized_or_missing_manifest_never_opens_data()
    {
        using var fixture = new Fixture();
        Assert.False(GameDataSnapshotReader.ReadDirectory(fixture.Folder).Reviewable);
        File.WriteAllText(fixture.Manifest, new string('a', GameDataSnapshotReader.MaxManifestBytes + 1));
        Assert.False(GameDataSnapshotReader.ReadDirectory(fixture.Folder).Reviewable);
    }

    [Fact]
    public void Zero_byte_and_over_limit_sqlite_claims_are_rejected()
    {
        using var fixture = new Fixture();
        fixture.WriteManifest(bytes: 0);
        Assert.False(GameDataSnapshotReader.ReadDirectory(fixture.Folder).Reviewable);
        fixture.WriteManifest(bytes: GameDataSnapshotReader.MaxSqliteBytes + 1);
        Assert.False(GameDataSnapshotReader.ReadDirectory(fixture.Folder).Reviewable);
    }

    [Fact]
    public void Missing_known_schema_is_not_promoted_to_reviewable()
    {
        using var fixture = new Fixture(validSchema: false);
        fixture.WriteManifest();
        var rejected = GameDataSnapshotReader.ReadDirectory(fixture.Folder);
        Assert.True(rejected.VerifiedLocalBytes);
        Assert.True(rejected.SchemaInspected);
        Assert.False(rejected.Reviewable);
        Assert.NotEmpty(rejected.Issues);
    }

    [Fact]
    public void Duplicate_manifest_keys_fail_even_if_one_value_looks_valid()
    {
        using var fixture = new Fixture();
        fixture.WriteManifest();
        string valid = File.ReadAllText(fixture.Manifest);
        // A second source is not allowed; do not accept last-key-wins semantics.
        File.WriteAllText(fixture.Manifest,
            valid.Replace("\"source\":", "\"source\":\"OpenDofusDB\",\"source\":", StringComparison.Ordinal));
        Assert.False(GameDataSnapshotReader.ReadDirectory(fixture.Folder).Reviewable);
    }

    [Fact]
    public void Untrusted_source_label_can_never_become_verified_publisher_evidence()
    {
        using var fixture = new Fixture();
        fixture.WriteManifest(source: "OpenDofusDB");
        var snapshot = GameDataSnapshotReader.ReadDirectory(fixture.Folder);
        Assert.True(snapshot.Reviewable);
        Assert.Equal("OpenDofusDB", snapshot.Source);
        Assert.False(snapshot.PublisherVerified);
        Assert.False(snapshot.EligibleForRuntimeImport);
    }

    [Fact]
    public void Invalid_pair_never_produces_false_comparison()
    {
        using var first = new Fixture();
        using var missing = new Fixture(createDb: false);
        first.WriteManifest();
        var report = GameDataSnapshotReader.Compare(
            GameDataSnapshotReader.ReadDirectory(first.Folder),
            GameDataSnapshotReader.ReadDirectory(missing.Folder));
        Assert.Equal("NOT_COMPARABLE", report.Status);
        Assert.False(report.RuntimeCompatibleProven);
    }
}
