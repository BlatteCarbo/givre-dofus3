using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GivreClone.App.Services.ProtocolUpdates;

namespace GivreClone.App.Services;

/// <summary>
/// Détecte un changement ENTRE DEUX DÉMARRAGES à version identique même en l'absence
/// de référence binaire certifiée. Ce repère local est NON fiable pour l'admission
/// protocole : il sert seulement à afficher une alerte, jamais à autoriser le bot.
/// Une modification détectée ne remplace PAS silencieusement le repère antérieur.
/// </summary>
public static class ProtocolObservedDrift
{
    public sealed record Result(bool HasBaseline, bool Changed, bool BaselineCreated, string Message);
    private sealed record Snapshot(string Version, ProtocolArtifactHashes Hashes);

    public static Result CompareAndRemember(string observationsRoot, string installationRoot,
        ProtocolUpdateReport report)
    {
        if (report.InstalledSnapshot is not { IsValid: true } ||
            !ProtocolUpdateProbe.ValidVersion(report.InstalledVersion))
            return new(false, false, false, "Aucune empreinte exploitable pour comparer deux démarrages.");
        try
        {
            var observations = Path.GetFullPath(observationsRoot);
            var installation = Path.GetFullPath(installationRoot);
            if (!Directory.Exists(installation) ||
                (File.GetAttributes(installation) & FileAttributes.ReparsePoint) != 0)
                return new(false, false, false, "Installation redirigée ou absente.");
            if (Directory.Exists(observations) &&
                (File.GetAttributes(observations) & FileAttributes.ReparsePoint) != 0)
                return new(false, false, false, "Répertoire de repères redirigé.");
            Directory.CreateDirectory(observations);
            string fingerprint = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(installation.ToUpperInvariant())))[..24];
            string file = Path.Combine(observations, fingerprint + ".json");
            if (File.Exists(file))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    return new(false, false, false, "Repère redirigé, comparaison refusée.");
                using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (input.Length is < 1 or > 4096)
                    return new(false, false, false, "Repère précédent non fiable.");
                var saved = JsonSerializer.Deserialize<Snapshot>(input, new JsonSerializerOptions { MaxDepth = 6 });
                if (saved?.Hashes is not { IsValid: true } || !ProtocolUpdateProbe.ValidVersion(saved.Version))
                    return new(false, false, false, "Repère précédent illisible ou invalide.");
                bool changed = !string.Equals(saved.Version, report.InstalledVersion, StringComparison.Ordinal) ||
                    !saved.Hashes.Matches(report.InstalledSnapshot);
                return new(true, changed, false, changed
                    ? "Changement depuis la dernière observation locale : analyse et revue nécessaires. Repère NON certifié."
                    : "Empreintes identiques à la dernière observation locale (repère NON certifié).");
            }
            // Premier lancement : l'observation ne vaut jamais une certification.
            string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    JsonSerializer.Serialize(output, new Snapshot(report.InstalledVersion!, report.InstalledSnapshot));
                File.Move(temp, file); // pas d'écrasement si un autre processus a créé le repère entre-temps
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return new(false, false, true,
                "Première observation locale enregistrée, sans certification de protocole.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
            or JsonException or NotSupportedException or System.Security.SecurityException)
        {
            return new(false, false, false, "Comparaison locale indisponible, référence non modifiée.");
        }
    }
}
