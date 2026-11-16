using System;
using System.Diagnostics;
using System.IO;

namespace GivreClone.App.Services;

// Log diagnostic. Édition locale (ou build Debug) : aussi un fichier license.log dans le dossier de données local. Autrement (Release
// hors édition locale) : uniquement Debug.WriteLine, aucun fichier. Aucun secret n'est journalisé.
public static class Logger
{
    private static readonly object _lock = new();
    private static string? _path;

    private static string Path_()
    {
        if (_path is not null) return _path;
        try
        {
            var dir = MZP19LocalClone.DataRoot;
            Directory.CreateDirectory(dir);
            return _path = System.IO.Path.Combine(dir, "license.log");
        }
        catch { return _path = ""; }
    }

    private static void Write(string level, string m)
    {
        Debug.WriteLine($"[{level}] {m}");
#if !DEBUG
        if (!LocalEdition.Enabled) return;
#endif
        var path = Path_();
        if (path.Length == 0) return;
        try { lock (_lock) File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {m}{Environment.NewLine}"); }
        catch { /* best-effort */ }
    }

    public static void Info(string m)  => Write("info", m);
    public static void Warn(string m)  => Write("warn", m);
    public static void Error(string m) => Write("error", m);
}
