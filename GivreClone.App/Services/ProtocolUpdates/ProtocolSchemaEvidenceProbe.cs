using System.Security.Cryptography;
using System.Text.Json;

namespace GivreClone.App.Services.ProtocolUpdates;

/// <summary>
/// Identical, independently rehashed local .proto candidate files establish only
/// structural candidate equality. They do not prove stable wire IDs or a codec.
/// </summary>
public sealed record ProtocolSchemaEvidenceReport(
    bool SchemaUnchangedProven, bool CurrentClientBound, bool SchemaChangedCandidate,
    string Message, string? ReferenceProtoSha256, string? TargetProtoSha256)
{
    public bool ProtoChangedProven => false;
    public bool WireCertified => false;
}

public static class ProtocolSchemaEvidenceProbe
{
    public const string NativeCandidateFormat = "dri-native-il2cpp-candidate-v3";

    public static ProtocolSchemaEvidenceReport Inspect(string? indexerRoot, string? referenceVersion,
        string? targetVersion, ProtocolArtifactHashes? installedSnapshot = null)
    {
        if (!ProtocolUpdateProbe.ValidVersion(referenceVersion) ||
            !ProtocolUpdateProbe.ValidVersion(targetVersion) || string.IsNullOrWhiteSpace(indexerRoot))
            return Unknown("Versions de comparaison ou dépôt indexeur invalides.");
        try
        {
            string root = Path.GetFullPath(indexerRoot);
            if (IsRedirected(root)) return Unknown("Racine indexeur redirigée.");
            string native = SafePath(root, "local-output", "historical-extractions-v3");
            var old = ReadEvidence(native, referenceVersion!);
            var current = ReadEvidence(native, targetVersion!);
            if (old is null || current is null)
                return Unknown("Snapshots proto absents, incomplets ou non conformes aux manifestes SHA-256.");

            bool clientBound = installedSnapshot is { IsValid: true } &&
                HashEqual(installedSnapshot.DofusExeSha256, current.DofusExeSha256) &&
                HashEqual(installedSnapshot.GameAssemblySha256, current.GameAssemblySha256) &&
                HashEqual(installedSnapshot.GlobalMetadataSha256, current.GlobalMetadataSha256);
            bool equal = HashEqual(old.ProtoSha256, current.ProtoSha256);

            return new(equal, clientBound, !equal,
                equal
                    ? clientBound
                        ? "Snapshots .proto locaux à SHA-256 identique, cible reliée aux 3 binaires installés. IDs réseau/ACK non certifiés."
                        : "Snapshots .proto locaux à SHA-256 identique ; identité des binaires installés non établie. IDs réseau/ACK non certifiés."
                    : "Snapshots .proto locaux différents : écart structurel candidat, audit wire indispensable.",
                old.ProtoSha256, current.ProtoSha256);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or JsonException or NotSupportedException or System.Security.SecurityException)
        {
            return Unknown("Lecture de preuves proto locales impossible.");
        }
    }

    private sealed record Snapshot(string ProtoSha256, string DofusExeSha256,
        string GameAssemblySha256, string GlobalMetadataSha256);

    private static Snapshot? ReadEvidence(string native, string version)
    {
        string dir = SafePath(native, version);
        string manifestPath = SafePath(dir, "manifest.json");
        string protoPath = SafePath(dir, "dofus-" + version + ".proto");
        using var manifestStream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (manifestStream.Length is < 100 or > 1024 * 1024) return null;
        using var doc = JsonDocument.Parse(manifestStream, new JsonDocumentOptions { MaxDepth = 12 });
        JsonElement manifest = doc.RootElement;
        if (manifest.ValueKind != JsonValueKind.Object ||
            Text(manifest, "format") != NativeCandidateFormat ||
            Text(manifest, "version") != version ||
            !IsBoolean(manifest, "review_only", true) ||
            !IsBoolean(manifest, "certified", false) ||
            !IsBoolean(manifest, "protocol_promotion", false)) return null;

        string? expected = Text(manifest, "proto_sha256");
        if (!ValidHash(expected) || !manifest.TryGetProperty("proto_size", out var protoSize) ||
            !protoSize.TryGetInt64(out long size) || size is < 50 or > 64L * 1024 * 1024) return null;

        if (!manifest.TryGetProperty("counts", out var counts) || counts.ValueKind != JsonValueKind.Object ||
            !Positive(counts, "messages") || !Positive(counts, "fields") ||
            !Positive(counts, "nonempty_messages")) return null;
        if (!manifest.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.Object ||
            !HashEqual(Text(source, "candidate_sha256"), expected) ||
            !source.TryGetProperty("client_sha256", out var binaries) ||
            binaries.ValueKind != JsonValueKind.Object) return null;

        string? dofus = Text(binaries, "dofusExeSha256");
        string? assembly = Text(binaries, "gameAssemblySha256");
        string? metadata = Text(binaries, "globalMetadataSha256");
        if (!ValidHash(dofus) || !ValidHash(assembly) || !ValidHash(metadata)) return null;

        using var protoFile = new FileStream(protoPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
        if (protoFile.Length != size || !HashEqual(Convert.ToHexString(SHA256.HashData(protoFile)), expected))
            return null;
        return new(expected!, dofus!, assembly!, metadata!);
    }

    private static string SafePath(string root, params string[] components)
    {
        string result = root;
        foreach (string name in components)
        {
            result = Path.Combine(result, name);
            if (IsRedirected(result)) throw new IOException("Redirected evidence path");
        }
        return result;
    }

    private static bool IsRedirected(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static bool Positive(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out int count) && count > 0;

    private static bool IsBoolean(JsonElement element, string name, bool expected) =>
        element.TryGetProperty(name, out var value) && value.ValueKind ==
            (expected ? JsonValueKind.True : JsonValueKind.False);

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static bool ValidHash(string? hash) => hash?.Length == 64 && hash.All(Uri.IsHexDigit);

    private static bool HashEqual(string? lhs, string? rhs) => ValidHash(lhs) && ValidHash(rhs) &&
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(lhs!), Convert.FromHexString(rhs!));

    private static ProtocolSchemaEvidenceReport Unknown(string message) =>
        new(false, false, false, message, null, null);
}
