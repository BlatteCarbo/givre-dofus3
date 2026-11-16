using System;
using System.IO;
using System.Text.Json;

namespace GivreClone.App.Services;

public sealed record LocalVersionNotice(bool RequiresAttention, string Message)
{
    /// <summary>
    /// The historical local observation is not an independent trusted baseline.
    /// A new, current-installation SHA-pinned review supersedes *that* old
    /// comparison, but never supersedes a trusted HASH_CHANGED verdict.
    /// </summary>
    public static bool HasUnresolvedHashDrift(bool historicalChanged,
        bool trustedHashChanged, MZP12SocketVersionBinding.Decision? currentAdmission)
        => trustedHashChanged || (historicalChanged &&
            currentAdmission?.IsUnverifiedCrossPatchReview != true);

    public static LocalVersionNotice Compare(string? installed, string? declared,
        MZP12SocketVersionBinding.Decision? socketAdmission = null,
        bool installedHashesChanged = false)
    {
        if (!MZP10MappingVersionAdmission.TryParse(declared, out _, out _, out _, out _))
            return new(true, "Version du bundle absente ou invalide. Vérifiez data/bundle-build.json avant de lancer un bot.");
        if (!MZP10MappingVersionAdmission.TryParse(installed, out _, out _, out _, out _))
            return new(true, $"Version Dofus installée non vérifiée ; bundle {declared}. Revérifiez le chemin du jeu dans Réglages.");
        // Protocol families are major.minor. Native module fingerprints are
        // checked separately and must not turn a patch into a protocol change.
        return MZP10MappingVersionAdmission.IsSameProtocolFamily(installed, declared)
            ? new(false, $"Dofus {installed} · catalogue {declared} : même famille de protocole.")
            : new(true, $"Dofus {installed} · catalogue {declared} : nouvelle famille de protocole, catalogue dédié nécessaire.");
    }

    public static string? ReadDeclaredBuild(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("build", out var build) && build.ValueKind == JsonValueKind.String
                ? build.GetString() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        { return null; }
    }
}
