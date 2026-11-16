using System;
using System.Collections.Generic;
using System.IO;
using GivreLauncher.Core.Launch;

namespace GivreClone.App.Services;

/// <summary>
/// Racine(s) d'installation Dofus 3 = le(s) dossier(s) contenant <c>Dofus_Data</c>. Résout d'abord le chemin RÉEL de
/// Dofus.exe (custom persisté par l'utilisateur via le sélecteur, sinon auto-détection release.json Zaap / dossier
/// standard) et en dérive la racine, PUIS ajoute les emplacements standard en repli. Ainsi TOUS les extracteurs
/// (icônes, mapcache, world-graph, positions de carte, cellules, sous-zones, tuiles…) suivent une installation HORS
/// emplacement standard, pas seulement le lancement du jeu. Ne renvoie que des dossiers qui existent, custom EN TÊTE.
/// </summary>
public static class DofusPaths
{
    /// <summary>Racines d'installation candidates (contenant Dofus_Data), la plus probable d'abord. Peut être vide
    /// (aucun client détecté) -> les extracteurs retombent alors proprement sur leur repli (null/cache).</summary>
    public static IReadOnlyList<string> InstallRoots()
    {
        var list = new List<string>();
        void Add(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            if (Directory.Exists(dir) && !list.Contains(dir!, StringComparer.OrdinalIgnoreCase)) list.Add(dir!);
        }

        // 1) Le Dofus.exe RÉEL (custom choisi par l'utilisateur, sinon Zaap release.json / dossier standard) -> sa racine.
        try
        {
            var exe = GameLauncher.ResolveDofusPath(AppConfig.Current.DofusExePath);
            if (File.Exists(exe)) Add(Path.GetDirectoryName(exe));
        }
        catch { /* résolution best-effort */ }

        // 2) Emplacements standard explicites (repli si la résolution ci-dessus a échoué ou pointe ailleurs).
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Add(Path.Combine(local, "Ankama", "Games", "dofus")); // Launcher 2
        Add(Path.Combine(local, "Ankama", "Dofus-dofus3"));
        Add(Path.Combine(local, "Ankama", "Dofus-experimental"));
        return list;
    }

    /// <summary>Dossier <c>Dofus_Data/StreamingAssets/Content</c> de la 1re racine qui l'a, ou null.</summary>
    public static string? ContentDir()
    {
        foreach (var baseDir in InstallRoots())
        {
            var d = Path.Combine(baseDir, "Dofus_Data", "StreamingAssets", "Content");
            if (Directory.Exists(d)) return d;
        }
        return null;
    }
}
