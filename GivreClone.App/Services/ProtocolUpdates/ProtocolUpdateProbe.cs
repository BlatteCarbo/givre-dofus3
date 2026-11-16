using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GivreClone.App.Services.ProtocolUpdates;

/// <summary>Version and binary drift are observations, never proof of a changed wire protocol.</summary>
public enum ProtocolUpdateStatus { UNKNOWN, VERSION_CHANGED, HASH_CHANGED, UNCHANGED }

public sealed record ProtocolArtifactHashes(string DofusExeSha256, string GameAssemblySha256,
    string GlobalMetadataSha256, string VersionMarkerSha256)
{
    public bool IsValid => Valid(DofusExeSha256) && Valid(GameAssemblySha256) &&
                           Valid(GlobalMetadataSha256) && Valid(VersionMarkerSha256);

    public bool Matches(ProtocolArtifactHashes? other) => other is not null && IsValid && other.IsValid &&
        Equal(DofusExeSha256, other.DofusExeSha256) && Equal(GameAssemblySha256, other.GameAssemblySha256) &&
        Equal(GlobalMetadataSha256, other.GlobalMetadataSha256) && Equal(VersionMarkerSha256, other.VersionMarkerSha256);

    private static bool Valid(string? hash) => hash?.Length == 64 && hash.All(Uri.IsHexDigit);
    private static bool Equal(string a, string b) => CryptographicOperations.FixedTimeEquals(
        Convert.FromHexString(a), Convert.FromHexString(b));
}

/// <summary>
/// A baseline is trusted by a separate publishing process, not by this scanner.
/// A random local JSON or the installed client must not become trusted automatically.
/// </summary>
public sealed record ProtocolTrustedBaseline(string Version, ProtocolArtifactHashes Hashes,
    string EvidenceId, bool IsTrusted)
{
    public bool IsUsable => IsTrusted && !string.IsNullOrWhiteSpace(EvidenceId) &&
                            EvidenceId.Length <= 200 && ProtocolUpdateProbe.ValidVersion(Version) &&
                            Hashes is { IsValid: true };
}

public sealed record ProtocolUpdateReport(
    ProtocolUpdateStatus Status, string Message, bool NeedsReview, bool CanRun,
    string? InstalledVersion, ProtocolArtifactHashes? InstalledSnapshot,
    string? DeclaredBundleVersion)
{
    public ProtocolSchemaEvidenceReport? SchemaEvidence { get; init; }
    public bool SchemaUnchangedProven => SchemaEvidence?.SchemaUnchangedProven == true;
    public bool ProtoChangedProven => false;
    public bool GameplayCertified => false;
    public bool CanAutoUpgrade => false;

    public ProtocolUpdateReport WithLocalSchemaEvidence(string indexerRoot, string referenceVersion) => this with
    {
        SchemaEvidence = ProtocolSchemaEvidenceProbe.Inspect(indexerRoot, referenceVersion,
            InstalledVersion, InstalledSnapshot)
    };
}

/// <summary>
/// Offline installed-client inspector: only Dofus.exe, GameAssembly.dll,
/// global-metadata.dat and StreamingAssets/version from the selected root.
/// Neither network nor process inspection is performed.
/// </summary>
public static class ProtocolUpdateProbe
{
    private static readonly Regex VersionPattern = new(@"\A[1-9][0-9]{0,8}(?:\.(?:0|[1-9][0-9]{0,8})){3}\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private const long MaxExecutableBytes = 8L * 1024 * 1024 * 1024;
    private const int MaxMarkerBytes = 4096;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static bool ValidVersion(string? version) => version is not null && VersionPattern.IsMatch(version);

    /// <summary>
    /// Synchronous by design: call from a background task at app startup.
    /// Does not persist or promote a baseline; caller supplies a previously trusted receipt.
    /// </summary>
    public static ProtocolUpdateReport Inspect(string? dofusExePath, string? declaredBundleVersion,
        ProtocolTrustedBaseline? trusted = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dofusExePath)) return Unknown(null, declaredBundleVersion,
            "Chemin Dofus.exe absent : empreintes non vérifiées.");
        try
        {
            string exe = Path.GetFullPath(dofusExePath);
            if (!string.Equals(Path.GetFileName(exe), "Dofus.exe", StringComparison.OrdinalIgnoreCase))
                return Unknown(null, declaredBundleVersion, "Sélectionnez un vrai chemin Dofus.exe.");
            string root = Path.GetDirectoryName(exe)!;
            if (!Directory.Exists(root)) return Unknown(null, declaredBundleVersion, "Installation Dofus introuvable.");

            // Never follow a symlink or junction inside the selected client directory.
            string assembly = CheckedPath(root, "GameAssembly.dll");
            string metadata = CheckedPath(root, "Dofus_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
            string marker = CheckedPath(root, "Dofus_Data", "StreamingAssets", "version");
            CheckedPath(root, "Dofus.exe");

            byte[] markerBytes = ReadMarker(marker);
            string text = StrictUtf8.GetString(markerBytes).TrimStart('\ufeff');
            string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            // The real Ankama marker includes BuildDate=... and ConfigUrl=... after Version=.
            // Only the first line defines identity. A second Version= is ambiguous and rejected.
            string first = lines[0].Trim();
            if (!first.StartsWith("Version=", StringComparison.Ordinal) ||
                !ValidVersion(first["Version=".Length..]) ||
                lines.Skip(1).Any(line => line.TrimStart().StartsWith("Version=", StringComparison.Ordinal)))
                return Unknown(null, declaredBundleVersion, "Marqueur de version Dofus invalide.");

            string installed = first["Version=".Length..];
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = new ProtocolArtifactHashes(
                HashFile(exe, cancellationToken), HashFile(assembly, cancellationToken),
                HashFile(metadata, cancellationToken), Convert.ToHexString(SHA256.HashData(markerBytes)));
            return Compare(installed, snapshot, declaredBundleVersion, trusted);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or DecoderFallbackException or System.Security.SecurityException)
        {
            return Unknown(null, declaredBundleVersion,
                "Lecture locale incomplète (fichier absent, redirigé ou accès refusé). Vérification impossible.");
        }
    }

    /// <summary>Pure comparison used for deterministic tests and by Inspect after hashing.</summary>
    public static ProtocolUpdateReport Compare(string? installedVersion, ProtocolArtifactHashes? current,
        string? declaredBundleVersion, ProtocolTrustedBaseline? trusted = null)
    {
        if (!ValidVersion(installedVersion) || current is not { IsValid: true })
            return Unknown(installedVersion, declaredBundleVersion, "Preuves locales de version ou de SHA-256 incomplètes.");

        // The bundle only provides its declared build; it has no trustworthy binary fingerprints.
        // A different build is enough to request a protocol review, not to assert a wire change.
        if (ValidVersion(declaredBundleVersion) &&
            !string.Equals(installedVersion, declaredBundleVersion, StringComparison.Ordinal))
            return new(ProtocolUpdateStatus.VERSION_CHANGED,
                $"Version Dofus {installedVersion}, bundle {declaredBundleVersion} : revue protocole nécessaire (changement non prouvé).",
                true, true, installedVersion, current, declaredBundleVersion);

        if (trusted is { IsUsable: true } &&
            !string.Equals(installedVersion, trusted.Version, StringComparison.Ordinal))
            return new(ProtocolUpdateStatus.VERSION_CHANGED,
                $"Version installée {installedVersion} différente de la référence fiable {trusted.Version}. Revue nécessaire.",
                true, true, installedVersion, current, declaredBundleVersion);

        if (trusted is { IsUsable: true } && !current.Matches(trusted.Hashes))
            return new(ProtocolUpdateStatus.HASH_CHANGED,
                "Empreinte(s) SHA-256 Dofus modifiée(s) à version identique : inspection du protocole recommandée.",
                true, true, installedVersion, current, declaredBundleVersion);

        if (!ValidVersion(declaredBundleVersion))
            return Unknown(installedVersion, declaredBundleVersion,
                "Version du bundle inconnue : impossible de conclure à la compatibilité ; revue locale possible.", current);

        if (trusted is not { IsUsable: true })
            return Unknown(installedVersion, declaredBundleVersion,
                "Aucune référence d'empreintes explicitement fiable : comparaison du protocole indéterminée.", current);

        return new(ProtocolUpdateStatus.UNCHANGED,
            $"Version et quatre empreintes locales conformes à la référence {trusted.EvidenceId}. Le protocole réseau reste non certifié.",
            false, false, installedVersion, current, declaredBundleVersion);
    }

    private static ProtocolUpdateReport Unknown(string? installed, string? bundle, string message,
        ProtocolArtifactHashes? snapshot = null) => new(
            ProtocolUpdateStatus.UNKNOWN, message, true,
            ValidVersion(installed) && snapshot is { IsValid: true }, installed, snapshot, bundle);

    private static string CheckedPath(string root, params string[] parts)
    {
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Root redirected");
        string path = root;
        foreach (string part in parts)
        {
            path = Path.Combine(path, part);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Client path redirected");
        }
        return path;
    }

    private static byte[] ReadMarker(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
        if (file.Length is < 1 or > MaxMarkerBytes) throw new IOException("Bad version marker size");
        byte[] bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        return bytes;
    }

    private static string HashFile(string path, CancellationToken cancellationToken)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.SequentialScan);
        if (file.Length is <= 0 or > MaxExecutableBytes) throw new IOException("Unexpected binary size");
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[65536];
        int count;
        while ((count = file.Read(buffer)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sha.AppendData(buffer, 0, count);
        }
        return Convert.ToHexString(sha.GetHashAndReset());
    }
}
