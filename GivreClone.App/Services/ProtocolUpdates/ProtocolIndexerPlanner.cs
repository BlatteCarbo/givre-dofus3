using System.Security.Cryptography;

namespace GivreClone.App.Services.ProtocolUpdates;

/// <summary>
/// Immutable intention to run DofusReverseIndexer in its offline *plan* mode.
/// The command never includes --execute and is never built from arbitrary user arguments.
/// </summary>
public sealed record ProtocolIndexerPlan(
    bool CanRun, string Message, string? Executable, IReadOnlyList<string> Arguments,
    string? WorkingDirectory, IReadOnlyDictionary<string, string> EnvironmentVariables,
    bool ReviewOnly = true, bool DownloadsOrUpgrades = false)
{
    public string? ApprovedExecutableSha256 { get; init; }
}

public static class ProtocolIndexerPlanner
{
    /// <summary>
    /// Paths are fixed relative to the approved GIVRE workspace. No files are written.
    /// A caller path AND exact SHA-256 must be explicitly approved to enable launching;
    /// userConfirmed is normally supplied by the click handler of a clearly labeled button.
    /// </summary>
    public static ProtocolIndexerPlan BuildReviewOnly(ProtocolUpdateReport? report,
        string? workspaceRoot, string? approvedPythonExe = null,
        string? approvedPythonSha256 = null, bool userConfirmed = false,
        string? destinationAppDirectory = null)
    {
        if (report?.CanRun != true || !ProtocolUpdateProbe.ValidVersion(report.InstalledVersion)
            || report.InstalledSnapshot is not { IsValid: true })
            return Blocked("Aucune empreinte Dofus complète nécessitant une revue.");

        if (string.IsNullOrWhiteSpace(workspaceRoot)) return Blocked("Dossier GIVRE absent.");
        try
        {
            string workspace = Path.GetFullPath(workspaceRoot);
            if (!string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(workspace)), "GIVRE",
                    StringComparison.OrdinalIgnoreCase) || IsRedirected(workspace))
                return Blocked("La racine GIVRE doit être un dossier local non redirigé.");

            string indexer = SafeChild(workspace, "DofusReverseIndexer");
            string givreActive = SafeChild(workspace, "GivreActive");
            // Production defaults to GivreWork. The currently shared integration
            // checkout remains supported but must be explicitly selected when both exist.
            string appName = destinationAppDirectory ??
                (Directory.Exists(Path.Combine(workspace, "GivreWork"))
                    ? "GivreWork" : "GivreWork-Integration-20261020");
            if (appName is not ("GivreWork" or "GivreWork-Integration-20261020"))
                return Blocked("Destination GivreWork non autorisée.");
            string app = SafeChild(workspace, appName);
            string indexerSource = SafeChild(indexer, "src", "dri");
            if (!Directory.Exists(indexerSource) || !Directory.Exists(givreActive)
                || !Directory.Exists(app)
                || !File.Exists(SafeChild(indexerSource, "cli.py"))
                || !File.Exists(SafeChild(indexerSource, "autopipeline.py")))
                return Blocked("DofusReverseIndexer, GivreActive ou GivreWork sont absents.");

            // The reviewed indexer rejects output inside GivreActive or its own source.
            string output = SafeChild(app, "artifacts", "protocol-review");
            var arguments = new[] { "-m", "dri", "rework-auto-update",
                "--givre-root", givreActive, "--output-root", output,
                "--version", report.InstalledVersion! };
            var environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PYTHONPATH"] = Path.Combine(indexer, "src"),
                ["PYTHONDONTWRITEBYTECODE"] = "1"
            };

            if (!userConfirmed)
                return new(false, "Plan hors ligne prêt pour confirmation explicite ; aucun processus lancé.",
                    null, arguments, indexer, environment);

            if (string.IsNullOrWhiteSpace(approvedPythonExe) ||
                string.IsNullOrWhiteSpace(approvedPythonSha256) ||
                approvedPythonSha256.Length != 64 || !approvedPythonSha256.All(Uri.IsHexDigit))
                return new(false, "Chemin et SHA-256 approuvés de python.exe requis.",
                    null, arguments, indexer, environment);

            string python = Path.GetFullPath(approvedPythonExe);
            if (!Path.IsPathFullyQualified(approvedPythonExe) ||
                !string.Equals(Path.GetFileName(python), "python.exe", StringComparison.OrdinalIgnoreCase) ||
                IsRedirected(python) || !File.Exists(python))
                return new(false, "Interpréteur approuvé absent, redirigé ou différent de python.exe.",
                    null, arguments, indexer, environment);

            using var file = new FileStream(python, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length is <= 0 or > 512L * 1024 * 1024)
                return new(false, "Taille inattendue de l'interpréteur Python.",
                    null, arguments, indexer, environment);
            byte[] actual = SHA256.HashData(file);
            if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(approvedPythonSha256)))
                return new(false, "Empreinte SHA-256 de l'interpréteur Python incorrecte.",
                    null, arguments, indexer, environment);

            return new(true,
                "Plan CLI vérifié : exécution possible uniquement sur commande explicite, sans --execute.",
                python, arguments, indexer, environment)
            { ApprovedExecutableSha256 = approvedPythonSha256.ToUpperInvariant() };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or System.Security.SecurityException)
        {
            return Blocked("Chemin de l'indexeur ou de l'interpréteur non vérifiable.");
        }
    }

    private static string SafeChild(string root, params string[] components)
    {
        string result = root;
        foreach (string component in components)
        {
            result = Path.Combine(result, component);
            if (IsRedirected(result)) throw new IOException("Repository link rejected");
        }
        return result;
    }

    private static bool IsRedirected(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static ProtocolIndexerPlan Blocked(string message) => new(false, message,
        null, [], null, new Dictionary<string, string>());
}
