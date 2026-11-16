using System;
using System.IO;

namespace GivreClone.App.Services;

/// <summary>
/// Discovers a locally captured DofusDB review snapshot without downloading or
/// authenticating. This locates files only: all hashes, licenses, version binding,
/// completeness and schema MUST be checked by the consuming reader.
/// </summary>
internal static class DofusDbReviewDiscovery
{
    private const string RelativeCache = "tools/dofusdb-partner-catalog/.cache/full-catalog";

    public static string? FindDefaultCache()
    {
        string? explicitDir = Environment.GetEnvironmentVariable("GIVRE_DOFUSDB_REVIEW_DIR");
        if (!string.IsNullOrWhiteSpace(explicitDir))
            return ValidCache(explicitDir);

        // Running the integration checkout from bin/Release/net10.0 should
        // locate the workspace's cache. This is intentionally NOT installed
        // globally and never scans unrelated user directories.
        DirectoryInfo? parent = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && parent is not null; i++, parent = parent.Parent)
        {
            string? candidate = ValidCache(Path.Combine(parent.FullName, RelativeCache));
            if (candidate is not null) return candidate;
        }
        return null;
    }

    private static string? ValidCache(string path)
    {
        try
        {
            string absolute = Path.GetFullPath(path);
            if (Directory.Exists(absolute) &&
                File.Exists(Path.Combine(absolute, "review-summary.json")) &&
                !new DirectoryInfo(absolute).Attributes.HasFlag(FileAttributes.ReparsePoint))
                return absolute;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Optional review-only source not present or inaccessible.
        }
        return null;
    }
}
