using System.IO.Compression;
using Microsoft.Data.Sqlite;
using GivreClone.App.Services;

int checks = 0;
void Check(bool ok, string label)
{
    if (!ok) throw new InvalidOperationException("MZP11 Cytrus test: " + label);
    checks++;
}
var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(),
    "MZP11-Synthetic-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
var sqlite = Path.Combine(root, "fixture.sqlite");
var manifest = Path.Combine(root, "manifest.cytrus");
void Create(string version, string game = "dofus", bool duplicate = false)
{
    if (File.Exists(sqlite)) File.Delete(sqlite);
    using (var db = new SqliteConnection(new SqliteConnectionStringBuilder
           { DataSource = sqlite, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ConnectionString))
    {
        db.Open();
        using var ddl = db.CreateCommand();
        ddl.CommandText = "CREATE TABLE game(gameUid TEXT, releaseName TEXT, platformName TEXT, releaseVersion TEXT)";
        ddl.ExecuteNonQuery();
        using var insert = db.CreateCommand();
        insert.CommandText = "INSERT INTO game VALUES ($game, 'dofus3', 'windows', $version)";
        insert.Parameters.AddWithValue("$game", game);
        insert.Parameters.AddWithValue("$version", version);
        insert.ExecuteNonQuery();
        if (duplicate) insert.ExecuteNonQuery();
    }
    using var source = File.OpenRead(sqlite);
    using var dest = File.Create(manifest);
    using var zipped = new GZipStream(dest, CompressionLevel.Optimal);
    source.CopyTo(zipped);
}

try
{
    var missing = MZP11CytrusVersionProbe.InspectManifest(Path.Combine(root, "missing.cytrus"));
    Check(!missing.HasLocalVersionEvidence && !missing.MayLaunchOrInject, "missing manifest denied");
    Create("6.0_3.7.11.11");
    var good = MZP11CytrusVersionProbe.InspectManifest(manifest);
    Check(good.HasLocalVersionEvidence && good.ProtocolVersion == "3.7.11.11", "real sqlite/gzip train decoded");
    Check(good.Train == "6.0" && good.ManifestSha256 is { Length: 64 }, "metadata and digest");
    Check(MZP11CytrusVersionProbe.InspectManifest(manifest).ManifestSha256 == good.ManifestSha256,
        "repeated read only accepts exactly identical bytes");
    Check(!good.AuthenticatedGameTraffic && !good.MayLaunchOrInject, "manifest is NOT ACK or auth");
    Create("6.0_3.8.0.0");
    Check(MZP11CytrusVersionProbe.InspectManifest(manifest).ProtocolVersion == "3.8.0.0",
        "changed bytes invalidate cache and new minor is detected");
    Create("6.0_3.7.12.2", "unknown-game");
    Check(!MZP11CytrusVersionProbe.InspectManifest(manifest).HasLocalVersionEvidence,
        "wrong game identifier denied");
    Create("6.0_3.7.12.2", duplicate: true);
    Check(!MZP11CytrusVersionProbe.InspectManifest(manifest).HasLocalVersionEvidence,
        "ambiguous two game rows denied");
    Create("6.0_invalid");
    Check(!MZP11CytrusVersionProbe.InspectManifest(manifest).HasLocalVersionEvidence,
        "unrecognized client version denied");
    File.WriteAllText(manifest, "not-gzip");
    Check(!MZP11CytrusVersionProbe.InspectManifest(manifest).HasLocalVersionEvidence,
        "corrupt gzip denied");
    var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ankama", "Games", "dofus", "Dofus.exe");
    if (File.Exists(installed))
    {
        var observation = MZP11CytrusVersionProbe.InspectInstalledDofus(installed);
        Check(observation.HasLocalVersionEvidence && observation.ProtocolVersion is not null,
            "installed game manifest read-only version probe");
        Console.WriteLine("INSTALLED_GAME_VERSION_FROM_CYTRUS=" + observation.ProtocolVersion);
        Console.WriteLine("INSTALLED_MANIFEST_SHA256=" + observation.ManifestSha256);
    }
    Console.WriteLine($"MZP11 Cytrus real production probe PASS ({checks} checks). No game process, account, ACK or network.");
}
finally
{
    var anchor = Path.GetFullPath(Path.GetTempPath());
    if (!root.StartsWith(anchor, StringComparison.OrdinalIgnoreCase) ||
        !Path.GetFileName(root).StartsWith("MZP11-Synthetic-", StringComparison.Ordinal) ||
        File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
        throw new IOException("Unsafe temporary fixture cleanup.");
    Directory.Delete(root, recursive: true);
}
