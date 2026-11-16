using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace GivreClone.App.Services;

/// <summary>Sauvegarde / restauration des réglages de l'appli (profils de combat, plannings, scripts, réglages par perso, éco…) en un zip.
/// Les caches régénérables et les journaux sont exclus ; les comptes, proxys et identifiants d'appareil ne sont inclus que sur demande
/// (ils contiennent des jetons). L'import garde d'abord une copie de ce qu'il remplace.</summary>
public static class SettingsBackup
{
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    { "logs", "mapcache", "worldmap", "looks", "jobicons", "ItemIdOverlay", "backups" };
    private static readonly HashSet<string> SecretFiles = new(StringComparer.OrdinalIgnoreCase)
    { "accounts.json", "proxies.json", "device-ids.json" };

    /// <summary>Fichiers (chemins relatifs) qu'un export prendrait.</summary>
    public static List<string> Collect(string dataRoot, bool includeSecrets)
    {
        var list = new List<string>();
        if (!Directory.Exists(dataRoot)) return list;
        foreach (var f in Directory.EnumerateFiles(dataRoot, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(dataRoot, f);
            var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Length > 1 && SkipDirs.Contains(parts[0])) continue;
            string name = parts[^1];
            if (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
            // AtomicJson keeps older credentials in .bak/.bad-*/.recovered too.
            if (!includeSecrets && SecretFiles.Any(secret =>
                name.Equals(secret, StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(secret + ".", StringComparison.OrdinalIgnoreCase))) continue;
            list.Add(rel);
        }
        return list;
    }

    public static int Export(string dataRoot, string zipPath, bool includeSecrets)
    {
        var files = Collect(dataRoot, includeSecrets);
        string destination = Path.GetFullPath(zipPath);
        files.RemoveAll(rel => string.Equals(Path.GetFullPath(Path.Combine(dataRoot, rel)),
            destination, StringComparison.OrdinalIgnoreCase));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        int n = 0;
        foreach (var rel in files)
        {
            try
            {
                using var src = new FileStream(Path.Combine(dataRoot, rel), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var dst = zip.CreateEntry(rel.Replace('\\', '/')).Open();
                src.CopyTo(dst);
                n++;
            }
            catch (IOException) { /* fichier verrouillé : ignoré */ }
        }
        return n;
    }

    private static string PendingPath(string dataRoot) => Path.Combine(dataRoot, "backups", "restauration-en-attente.zip");

    /// <summary>Prépare une restauration pour le PROCHAIN démarrage (l'appli en cours réécrirait sinon ses réglages en mémoire en quittant).</summary>
    public static void Stage(string zipPath, string dataRoot)
    {
        using (ZipFile.OpenRead(zipPath)) { }   // zip lisible, sinon exception tout de suite
        Directory.CreateDirectory(Path.Combine(dataRoot, "backups"));
        File.Copy(zipPath, PendingPath(dataRoot), overwrite: true);
    }

    /// <summary>Au démarrage, AVANT de charger les réglages : applique une restauration préparée. Renvoie un message, ou null s'il n'y avait rien.</summary>
    public static string? ApplyPending(string dataRoot)
    {
        string pending = PendingPath(dataRoot);
        if (!File.Exists(pending)) return null;
        try
        {
            var (n, backup) = Import(pending, dataRoot);
            return $"Réglages restaurés : {n} fichier(s)." + (backup is null ? "" : $" Ancienne version : backups\\{Path.GetFileName(backup)}");
        }
        catch (Exception e) { return "Restauration des réglages impossible : " + e.Message; }
        finally { try { File.Delete(pending); } catch (IOException) { } }
    }

    /// <summary>Restaure un zip d'export dans <paramref name="dataRoot"/>. Les fichiers remplacés sont d'abord copiés dans
    /// <c>backups/avant-import-*.zip</c>. Refuse toute entrée qui sortirait du dossier. Renvoie (fichiers restaurés, zip de secours ou null).</summary>
    public static (int Restored, string? Backup) Import(string zipPath, string dataRoot)
    {
        string root = Path.GetFullPath(dataRoot) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);
        var targets = new List<(ZipArchiveEntry Entry, string Path)>();
        foreach (var e in zip.Entries)
        {
            if (string.IsNullOrEmpty(e.Name)) continue;   // dossier
            string full = Path.GetFullPath(Path.Combine(root, e.FullName));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Entrée hors du dossier : " + e.FullName);
            var first = e.FullName.Split('/', '\\')[0];
            if (SkipDirs.Contains(first)) continue;
            targets.Add((e, full));
        }
        string? backup = null;
        var existing = targets.Where(t => File.Exists(t.Path)).ToList();
        if (existing.Count > 0)
        {
            backup = Path.Combine(root, "backups", $"avant-import-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            using var bz = ZipFile.Open(backup, ZipArchiveMode.Create);
            foreach (var t in existing) bz.CreateEntryFromFile(t.Path, Path.GetRelativePath(root, t.Path).Replace('\\', '/'));
        }
        foreach (var t in targets)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(t.Path)!);
            t.Entry.ExtractToFile(t.Path, overwrite: true);
        }
        return (targets.Count, backup);
    }
}
