using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace GivreClone.App.Services;

/// <summary>
/// An explicit operator-controlled local review of a cross-patch Protobuf
/// candidate. Checks a hash-pinned receipt and that THIS mapping pack and THIS
/// installed client have not changed since the operator's review. Does not
/// establish wire alias, server ownership or gameplay ACK compatibility.
/// No rights, mapping files or installed clients are modified.
/// </summary>
public static class MZP12LocalCompatibilityReview
{
    public const string ReceiptFile = "compatibility-review.json";

    public static string? TryAdmit(string? dataDirectory, string? sourceBuild,
        string? installedBuild, string? installedFingerprint)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory) || string.IsNullOrWhiteSpace(sourceBuild) ||
            string.IsNullOrWhiteSpace(installedBuild) || !Sha(installedFingerprint) ||
            sourceBuild == installedBuild) return null;
        try
        {
            if (!MZP10MappingVersionAdmission.TryParse(sourceBuild, out int sm, out int sn, out _, out _) ||
                !MZP10MappingVersionAdmission.TryParse(installedBuild, out int tm, out int tn, out _, out _) ||
                sm != tm || sn != tn) return null;
            string folder = Path.GetFullPath(dataDirectory);
            string file = Path.Combine(folder, ReceiptFile);
            if (!File.Exists(file) || File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint))
                return null;
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, FileOptions.SequentialScan);
            if (stream.Length is <= 2 or > 4096) return null;
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("operatorConfirmedSameMappings", out var approved) || approved.ValueKind != JsonValueKind.True ||
                !root.TryGetProperty("reviewOnly", out var review) || review.ValueKind != JsonValueKind.True ||
                Property(root, "sourceMappingBuild") != sourceBuild ||
                Property(root, "installedGameBuild") != installedBuild ||
                !string.Equals(Property(root, "installedFingerprintSha256"), installedFingerprint,
                    StringComparison.OrdinalIgnoreCase)) return null;
            string? sourceProto = Property(root, "sourceProtoSha256");
            string? targetProto = Property(root, "targetProtoSha256");
            if (!Sha(sourceProto) || !string.Equals(sourceProto, targetProto,
                    StringComparison.OrdinalIgnoreCase)) return null;
            foreach (var name in new[] { "matching_current.json", "fields.json" })
            {
                string? recorded = Property(root, name.Replace('.', '_') + "Sha256");
                if (!Sha(recorded)) return null;
                string asset = Path.Combine(folder, name);
                if (!File.Exists(asset) || File.GetAttributes(asset).HasFlag(FileAttributes.ReparsePoint))
                    return null;
                using var input = new FileStream(asset, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 65536, FileOptions.SequentialScan);
                if (input.Length is <= 0 or > 20_000_000 ||
                    !string.Equals(Convert.ToHexString(SHA256.HashData(input)), recorded,
                        StringComparison.OrdinalIgnoreCase)) return null;
            }
            return sourceProto;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
            JsonException or ArgumentException or NotSupportedException or InvalidOperationException)
        { return null; }
    }

    private static string? Property(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static bool Sha(string? hash) => hash is { Length: 64 } &&
        hash.All(Uri.IsHexDigit);
}
