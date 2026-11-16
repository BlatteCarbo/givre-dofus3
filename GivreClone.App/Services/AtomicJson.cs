using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;

namespace GivreClone.App.Services;

/// <summary>
/// Écriture de fichier atomique (temp + File.Move overwrite) : un crash en pleine écriture laisse
/// l'ancien fichier INTACT au lieu d'un JSON tronqué qui bricke le démarrage. Chaque appelant sérialise
/// déjà avec son propre contexte source-gen (AOT/trim-safe) et passe la string finale — d'où la signature
/// string, non générique. Même recette que <c>AdapterSettingsStore</c> / <c>MapCellReader</c>.
/// + Chargement PROTÉGÉ des stores de réglages (<see cref="LoadStore{T}"/>) : un fichier illisible n'est jamais écrasé.
/// </summary>
public static class AtomicJson
{
    /// <summary>Écrit <paramref name="content"/> dans <paramref name="path"/> de façon atomique. Crée le dossier parent au besoin.
    /// <paramref name="keepBackup"/> : garde la version PRÉCÉDENTE en <c>&lt;fichier&gt;.bak</c> (une seule rotation, par
    /// remplacement atomique) — filet de récupération manuelle des stores de réglages.</summary>
    public static void Write(string path, string content, bool keepBackup = false)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        if (keepBackup && File.Exists(path))
        {
            try { File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true); return; }
            catch { /* remplacement refusé (volume, droits, verrou) : écriture atomique simple ci-dessous */ }
        }
        MoveWithTransientRetry(tmp, path);
    }

    // Windows : un antivirus / indexeur peut tenir brièvement le fichier qu'on vient d'écrire -> le renommage échoue une fois
    // (UnauthorizedAccessException ou violation de partage). Trois nouvelles tentatives bornées (10/25/50 ms) sur CE seul
    // renommage ; toute autre erreur, ou l'échec final, remonte telle quelle : jamais de succès inventé.
    private static readonly int[] RenameRetryDelaysMs = { 10, 25, 50 };

    private static void MoveWithTransientRetry(string tmp, string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { File.Move(tmp, path, overwrite: true); return; }
            catch (Exception e) when (attempt < RenameRetryDelaysMs.Length && IsTransientRenameFailure(e) && !Directory.Exists(path))
            {
                System.Threading.Thread.Sleep(RenameRetryDelaysMs[attempt]);
            }
        }
    }

    private static bool IsTransientRenameFailure(Exception e) => e is UnauthorizedAccessException
        || e is IOException io && (io.HResult & 0xFFFF) is 32 or 33;

    /// <summary>Chemin où ÉCRIRE un store : le fichier lui-même, ou <c>&lt;fichier&gt;.recovered</c> quand son chargement a
    /// échoué ET qu'il n'a pas pu être mis de côté (<see cref="StoreLoad.Unreadable"/>) — l'original illisible n'est alors
    /// jamais écrasé.</summary>
    public static string StorePath(string path, bool writeAside) => writeAside ? path + ".recovered" : path;

    /// <summary>Fichiers de réglages illisibles traités au démarrage (nom du fichier, copie mise de côté ou null) : affichés
    /// en toast une fois la fenêtre ouverte (les stores sont chargés avant elle).</summary>
    public static readonly ConcurrentQueue<(string File, string? BadCopy, bool Restored)> StoreNotices = new();

    /// <summary>Charge un store JSON SANS jamais perdre un fichier existant : absent -> vide ; lisible -> données ; illisible
    /// (JSON cassé, type inattendu, lecture refusée) -> renommé en <c>&lt;fichier&gt;.bad-yyyyMMdd-HHmmss</c> et démarrage à
    /// vide (le prochain Save réécrit un fichier sain) ; renommage impossible -> démarrage à vide et
    /// <see cref="StoreLoadResult{T}.WriteAside"/> : les Save vont dans <c>.recovered</c>, l'original reste intact. Avant :
    /// l'erreur était avalée et le Save suivant écrasait le fichier -> perte totale des plannings / profils.
    /// Copie de secours : si <c>&lt;fichier&gt;.bak</c> (dernière version saine, rotation de <see cref="Write"/>) est lisible, on
    /// REPART d'elle (<see cref="StoreLoad.Restored"/>) au lieu d'un store vide ; elle est en plus dupliquée à côté du .bad
    /// (sinon le 2e Save la remplacerait par le store neuf). Original non renommable : on repart du <c>.recovered</c> d'une
    /// session précédente s'il est lisible (jamais écrasé par un store vide), sinon du .bak, sinon de zéro.
    /// <paramref name="log"/> : ligne de diagnostic (nom de fichier seulement, jamais le chemin complet ni le contenu).</summary>
    public static StoreLoadResult<T> LoadStore<T>(string path, Func<string, T?> deserialize, Func<T> empty,
                                                  Action<string>? log = null, DateTime? now = null) where T : class
    {
        if (!File.Exists(path)) return new StoreLoadResult<T>(empty(), StoreLoad.Missing, null);
        string name = Path.GetFileName(path);
        string why;
        try
        {
            return new StoreLoadResult<T>(deserialize(File.ReadAllText(path)) ?? empty(), StoreLoad.Loaded, null);
        }
        catch (Exception e) { why = e.GetType().Name; }

        T? fromBak = TryRead(path + ".bak", deserialize);   // dernière version saine (rotation de Write keepBackup)
        string stamp = (now ?? DateTime.Now).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string bad = path + ".bad-" + stamp;
        try
        {
            for (int k = 2; File.Exists(bad); k++) bad = path + ".bad-" + stamp + "-" + k.ToString(CultureInfo.InvariantCulture);
            File.Move(path, bad);
            // La copie de secours est gardée À CÔTÉ du .bad : le 2e Save (rotation) remplacerait sinon <fichier>.bak par le neuf.
            if (File.Exists(path + ".bak")) { try { File.Copy(path + ".bak", bad + ".bak", overwrite: false); } catch { /* best-effort */ } }
            if (fromBak is not null)
            {
                log?.Invoke($"réglages : {name} illisible ({why}) — mis de côté sous {Path.GetFileName(bad)}, repris depuis la copie de secours.");
                StoreNotices.Enqueue((name, Path.GetFileName(bad), true));
                return new StoreLoadResult<T>(fromBak, StoreLoad.Restored, bad);
            }
            log?.Invoke($"réglages : {name} illisible ({why}) — mis de côté sous {Path.GetFileName(bad)}, démarrage à vide.");
            StoreNotices.Enqueue((name, Path.GetFileName(bad), false));
            return new StoreLoadResult<T>(empty(), StoreLoad.Quarantined, bad);
        }
        catch
        {
            // Original non renommable (verrou, droits) : il reste intact, les Save vont dans .recovered. On repart du .recovered
            // d'une session précédente s'il existe (sinon il serait écrasé par un store vide), sinon de la copie de secours.
            T? fromRecovered = TryRead(path + ".recovered", deserialize);
            T data = fromRecovered ?? fromBak ?? empty();
            bool restored = fromRecovered is not null || fromBak is not null;
            log?.Invoke($"réglages : {name} illisible ({why}) et impossible à mettre de côté — original conservé, écriture dans {name}.recovered"
                        + (fromRecovered is not null ? " (repris de la session précédente)." : fromBak is not null ? " (repris de la copie de secours)." : "."));
            StoreNotices.Enqueue((name, null, restored));
            return new StoreLoadResult<T>(data, StoreLoad.Unreadable, null);
        }
    }

    // Lecture sans exception d'une copie (secours / récupération) : null si absente ou illisible.
    private static T? TryRead<T>(string file, Func<string, T?> deserialize) where T : class
    {
        try { return File.Exists(file) ? deserialize(File.ReadAllText(file)) : null; }
        catch { return null; }
    }
}

/// <summary>Issue de <see cref="AtomicJson.LoadStore{T}"/>.</summary>
public enum StoreLoad
{
    Missing,      // pas de fichier : store vide
    Loaded,       // fichier lu
    Quarantined,  // fichier illisible renommé en .bad-… : store vide, le prochain Save réécrit normalement
    Unreadable,   // fichier illisible NON renommable : Save écrit à côté (.recovered), original intact ; données = .recovered / .bak / vide
    Restored,     // fichier illisible renommé en .bad-… : données reprises de la copie de secours <fichier>.bak
}

/// <summary>Données chargées + issue + copie mise de côté (Quarantined).</summary>
public readonly record struct StoreLoadResult<T>(T Data, StoreLoad Outcome, string? BadCopy)
{
    /// <summary>Les Save doivent écrire à côté (<see cref="AtomicJson.StorePath"/>) : l'original illisible n'a pas pu être
    /// mis de côté et ne doit jamais être écrasé.</summary>
    public bool WriteAside => Outcome == StoreLoad.Unreadable;
}
