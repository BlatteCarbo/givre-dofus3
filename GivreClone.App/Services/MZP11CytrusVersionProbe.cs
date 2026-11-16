using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace GivreClone.App.Services;

/// <summary>
/// Offline, read-only view of the installed GAME version. On current Ankama
/// Launcher builds, Games/dofus/manifest.cytrus may remain on an older build
/// after Dofus-dofus3 binaries have been updated. Prefer the version marker
/// physically next to the selected Dofus.exe, then bind it to that install's
/// binary hashes. A Cytrus SQLite value is NOT independent game traffic.
/// This reader never launches/attaches to the Launcher, inspects account
/// folders, opens Zaap keys, or reads live process state.
/// </summary>
public static class MZP11CytrusVersionProbe
{
    private const long MaxCompressed = 64L * 1024 * 1024;
    private const long MaxUncompressed = 128L * 1024 * 1024;
    // On a reconnect loop, decompressing the 80 MB Cytrus database twice
    // per account is unnecessary. A cache hit STILL hashes the 22 MB original
    // while holding its read-only file handle, so unchanged timestamps/size
    // cannot hide a modified version or database.
    private static readonly object CacheGate = new();
    private sealed record Cached(string FilePath, string Sha256, Result Metadata);
    private static Cached? _lastValidated;
    private static readonly Regex VersionFormat = new(
        @"\A(?<train>[0-9]{1,3}\.[0-9]{1,3})_(?<game>[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,4}\.[0-9]{1,4})\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public sealed record Result(string? ProtocolVersion, string? Train, string? ManifestSha256,
        string Diagnostic)
    {
        public bool HasLocalVersionEvidence => ProtocolVersion is not null;
        public bool AuthenticatedGameTraffic => false;
        public bool MayLaunchOrInject => false;
    }

    public static Result InspectInstalledDofus(string? gameExecutable)
    {
        if (string.IsNullOrWhiteSpace(gameExecutable)) return Refuse("Aucun Dofus.exe sélectionné.");
        try
        {
            var exe = Path.GetFullPath(gameExecutable);
            if (!string.Equals(Path.GetFileName(exe), "Dofus.exe", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(exe) || File.GetAttributes(exe).HasFlag(FileAttributes.ReparsePoint))
                return Refuse("Exécutable Dofus absent ou non conforme.");
            string root = Path.GetDirectoryName(exe)!;
            string marker = Path.Combine(root, "Dofus_Data", "StreamingAssets", "version");
            // If there is a version marker, never substitute a potentially
            // stale Launcher manifest after the marker fails validation.
            if (File.Exists(marker)) return InspectInstalledMarker(root, marker);
            return InspectManifest(Path.Combine(root, "manifest.cytrus"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return Refuse("Chemin d'installation illisible."); }
    }

    /// <summary>
    /// The official Unity StreamingAssets/version marker describes THIS
    /// installed directory. Fingerprint all local binary inputs so an update
    /// during a socket session invalidates the previous mapping binding.
    /// No other game directory, account file or launcher database is used.
    /// </summary>
    public static Result InspectInstalledMarker(string gameDirectory, string markerPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(gameDirectory) || string.IsNullOrWhiteSpace(markerPath))
                return Refuse("Installation ou marqueur absent.");
            string root = Path.GetFullPath(gameDirectory);
            string marker = Path.GetFullPath(markerPath);
            string expected = Path.GetFullPath(Path.Combine(root, "Dofus_Data", "StreamingAssets", "version"));
            if (!string.Equals(marker, expected, StringComparison.OrdinalIgnoreCase))
                return Refuse("Marqueur hors de l'installation choisie.");
            var paths = new[]
            {
                marker,
                Path.Combine(root, "Dofus.exe"),
                Path.Combine(root, "GameAssembly.dll"),
                Path.Combine(root, "Dofus_Data", "il2cpp_data", "Metadata", "global-metadata.dat")
            };
            foreach (var path in paths)
                if (!File.Exists(path) || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                    return Refuse("Marqueur ou binaires installés absents/redirigés.");

            byte[] content;
            using (var stream = new FileStream(marker, FileMode.Open, FileAccess.Read,
                       FileShare.Read, 4096, FileOptions.SequentialScan))
            {
                if (stream.Length is < 10 or > 2048) return Refuse("Marqueur du jeu invalide.");
                content = new byte[checked((int)stream.Length)];
                stream.ReadExactly(content);
            }
            string markerText = Encoding.UTF8.GetString(content).TrimStart('\uFEFF');
            string? version = null;
            foreach (var rawLine in markerText.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (line.StartsWith("Version=", StringComparison.Ordinal))
                {
                    if (version is not null) return Refuse("Marqueur contient plusieurs versions.");
                    version = line[8..];
                }
            }
            if (!MZP10MappingVersionAdmission.TryParse(version, out _, out _, out _, out _))
                return Refuse("Version du marqueur installé invalide.");

            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var path in paths)
            {
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 65536, FileOptions.SequentialScan);
                // A hash of all four files is bound to their names and sizes,
                // not only to timestamps or an outdated Cytrus SQLite value.
                digest.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, path)
                    .Replace('\\', '/') + ":" + input.Length + ":"));
                var buffer = new byte[65536];
                int count;
                while ((count = input.Read(buffer)) > 0)
                    digest.AppendData(buffer.AsSpan(0, count));
            }
            return new Result(version, "installed", Convert.ToHexString(digest.GetHashAndReset()),
                "Version issue du marqueur du client installé + empreintes des binaires (pas un ACK serveur).");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or System.Security.SecurityException or OverflowException)
        { return Refuse("Marqueur du jeu ou binaires installés illisibles."); }
    }

    /// <summary>Separate path for isolated offline SQLite/gzip fixtures.</summary>
    public static Result InspectManifest(string? manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath)) return Refuse("Manifeste absent.");
        string? scratch = null;
        try
        {
            var manifest = Path.GetFullPath(manifestPath);
            if (!string.Equals(Path.GetFileName(manifest), "manifest.cytrus", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(manifest) || File.GetAttributes(manifest).HasFlag(FileAttributes.ReparsePoint))
                return Refuse("Le manifeste Cytrus est introuvable ou redirigé.");

            // FileShare.Read prevents an updater writing while we parse. We do
            // not modify or replace the original game installation file.
            using var source = new FileStream(manifest, FileMode.Open, FileAccess.Read,
                FileShare.Read, 65536, FileOptions.SequentialScan);
            if (source.Length is <= 0 or > MaxCompressed) return Refuse("Taille du manifeste interdite.");
            var sha = Convert.ToHexString(SHA256.HashData(source));
            source.Position = 0;
            lock (CacheGate)
                if (_lastValidated is { } prior &&
                    string.Equals(prior.FilePath, manifest, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(prior.Sha256, sha, StringComparison.Ordinal))
                    return prior.Metadata;

            // SqliteConnection only accepts filenames, so isolate a temporary
            // copy of this non-account, non-secret manifest. It is removed in
            // finally and never enters the application publish artifacts.
            var tempAnchor = Path.GetFullPath(Path.GetTempPath());
            scratch = Path.Combine(tempAnchor, "MZP11-Cytrus-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            if (File.GetAttributes(scratch).HasFlag(FileAttributes.ReparsePoint))
                return Refuse("Répertoire temporaire redirigé.");
            var copy = Path.Combine(scratch, "manifest-readonly.sqlite");
            using (var output = new FileStream(copy, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 65536, FileOptions.SequentialScan))
            using (var gzip = new GZipStream(source, CompressionMode.Decompress, leaveOpen: true))
            {
                var buffer = new byte[65536];
                long total = 0;
                while (true)
                {
                    int count = gzip.Read(buffer);
                    if (count == 0) break;
                    total += count;
                    if (total > MaxUncompressed) return Refuse("SQLite Cytrus décompressé trop volumineux.");
                    output.Write(buffer, 0, count);
                }
            }
            var config = new SqliteConnectionStringBuilder
            {
                DataSource = copy,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
                Cache = SqliteCacheMode.Private
            };
            SQLitePCL.Batteries.Init(); // bundled Windows-system winsqlite3.dll provider
            using var db = new SqliteConnection(config.ConnectionString);
            db.Open();
            using var statement = db.CreateCommand();
            statement.CommandText = "SELECT gameUid, releaseName, platformName, releaseVersion FROM game LIMIT 2";
            using var cursor = statement.ExecuteReader();
            if (!cursor.Read()) return Refuse("Aucune ligne jeu dans Cytrus.");
            if (cursor.IsDBNull(0) || cursor.IsDBNull(1) || cursor.IsDBNull(2) || cursor.IsDBNull(3))
                return Refuse("Métadonnées Cytrus incomplètes.");
            string gameId = cursor.GetString(0), release = cursor.GetString(1),
                platform = cursor.GetString(2), rawVersion = cursor.GetString(3);
            if (cursor.Read() || !string.Equals(gameId, "dofus", StringComparison.Ordinal) ||
                !string.Equals(release, "dofus3", StringComparison.Ordinal) ||
                !string.Equals(platform, "windows", StringComparison.Ordinal))
                return Refuse("Le manifeste n'identifie pas un unique client Dofus PC.");
            var match = VersionFormat.Match(rawVersion);
            if (!match.Success) return Refuse("Version Cytrus dans un format inconnu.");
            string version = match.Groups["game"].Value;
            if (!MZP10MappingVersionAdmission.TryParse(version, out _, out _, out _, out _))
                return Refuse("Numéro de version Cytrus invalide.");
            var result = new Result(version, match.Groups["train"].Value, sha,
                "Version issue du manifeste installé (métadonnées locales, pas d'ACK serveur).");
            lock (CacheGate) _lastValidated = new Cached(manifest, sha, result);
            return result;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or SqliteException or System.Security.SecurityException)
        { return Refuse("Manifeste Cytrus absent, incohérent ou illisible."); }
        finally
        {
            if (scratch is not null)
            {
                try
                {
                    var root = Path.GetFullPath(Path.GetTempPath());
                    var dir = Path.GetFullPath(scratch);
                    if (dir.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                        Path.GetFileName(dir).StartsWith("MZP11-Cytrus-", StringComparison.Ordinal) &&
                        Directory.Exists(dir) && !File.GetAttributes(dir).HasFlag(FileAttributes.ReparsePoint))
                        Directory.Delete(dir, recursive: true);
                }
                catch { /* Test-only temporary copy cleanup error; never delete outside scoped folder. */ }
            }
        }
    }

    private static Result Refuse(string why) => new(null, null, null, why);
}
