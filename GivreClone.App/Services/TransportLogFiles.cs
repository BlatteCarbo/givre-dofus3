using System;
using System.IO;
using System.Linq;

namespace GivreClone.App.Services;

/// <summary>Le fichier ouvert fait foi : les métadonnées Windows peuvent rester périmées jusqu'à sa fermeture.</summary>
public static class TransportLogFiles
{
    public static string? Select(string directory, string requested, string? current)
    {
        if (requested.Length > 0)
        {
            string name = Path.GetFileName(requested);
            if (!string.Equals(name, requested, StringComparison.Ordinal)
                || !name.StartsWith("transport-", StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Only transport-*.log filenames are permitted.");
            string explicitPath = Path.Combine(directory, name);
            return File.Exists(explicitPath) ? explicitPath : null;
        }
        if (!Directory.Exists(directory)) return null;
        if (!string.IsNullOrEmpty(current)
            && string.Equals(Path.GetDirectoryName(Path.GetFullPath(current)), Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase)
            && File.Exists(current)) return current;
        return new DirectoryInfo(directory).GetFiles("transport-*.log")
            .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName;
    }
}
