using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Engine.LuaTooling;
using GivreClone.Protocol;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// ===================== Journalisation du script : niveaux, couleurs, persistance fichier =====================
public sealed partial class GivreLuaHost
{
    // ------------------------------------------------------------------ journalisation (categorie + niveau -> couleur UI + fichier)
    private void Emit(LuaLogLevel level, string cat, string msg, string? colorHex = null, LuaErrorLocation? location = null)
    {
        msg ??= "";
        bool bold = false;
        if (msg.Contains("<b>") && msg.Contains("</b>")) { msg = msg.Replace("<b>", "").Replace("</b>", ""); bold = true; }
        var entry = new LuaLogEntry(DateTime.Now, level, cat, msg, colorHex, bold, location);
        RememberConsoleLine(entry);   // compat tiers global:consoleLines
        Mcp.McpConsole.For(_launch.ServerIdOf(_pid), _world.Get(_pid).CharacterName, _pid)
            .Add(level.ToString().ToLowerInvariant(), cat, msg);
        Log?.Invoke(entry);
        WriteToFile(entry);
        GivreLauncher.Core.Transport.TransportLog.Line($"[lua pid {_pid}] {cat}: {msg}");
    }
    private void Info(string cat, string msg) => Emit(LuaLogLevel.Info, cat, msg);
    private void Ok(string cat, string msg) => Emit(LuaLogLevel.Success, cat, msg);
    private void Warn(string cat, string msg) => Emit(LuaLogLevel.Warning, cat, msg);
    private void Err(string cat, string msg) => Emit(LuaLogLevel.Danger, cat, msg);

    // Erreur de SCRIPT : message avec emplacement (« recolte.lua:148 — attempt to call a nil value « getJobLevel » (fonction
    // inexistante…) ») + emplacement STRUCTURÉ porté jusqu'à la console (clic -> ligne dans l'éditeur).
    private void ScriptErr(string cat, Exception ex, Func<string, string> format, LuaLogLevel level = LuaLogLevel.Danger)
    {
        var (text, loc) = DescribeError(ex);
        Emit(level, cat, format(text), null, loc);
    }

    /// <summary>Texte lisible d'une erreur Lua + son emplacement (null si MoonSharp n'en donne pas : exception CLR brute).</summary>
    private (string Text, LuaErrorLocation? Location) DescribeError(Exception ex)
    {
        string raw = LuaErr(ex);
        if (!LuaErrorLocationParser.TryParse(raw, out var loc) || loc is null) return (Root(ex).Message, null);
        string text = $"{loc.Chunk}:{loc.Line} — {loc.Message}";
        if (loc.Message.Contains("attempt to call a nil value", StringComparison.Ordinal)
            && string.Equals(loc.Chunk, ChunkName, StringComparison.Ordinal) && _lastScriptText is { } src
            && LuaErrorLocationParser.CalleeNameAt(src, loc) is { } callee)
            text += Loc.CurrentCode == "fr" ? $" « {callee} »" : $" '{callee}'";
        // Aide en français seulement (textes non traduits) : interface en/es -> message MoonSharp seul.
        if (Loc.CurrentCode == "fr" && LuaErrorLocationParser.FrenchHint(loc.Message) is { } hint) text += $" ({hint})";
        return (text, loc);
    }

    private static LuaLogLevel ParseLevel(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "green" or "success" or "ok" => LuaLogLevel.Success,
        "yellow" or "warning" or "warn" => LuaLogLevel.Warning,
        "red" or "danger" or "error" => LuaLogLevel.Danger,
        "grey" or "gray" or "debug" => LuaLogLevel.Debug,
        _ => LuaLogLevel.Info,
    };

    // Code couleur hexadecimal valide (#RGB ou #RRGGBB) printColor.
    private static bool IsHex(string? s)
    {
        if (string.IsNullOrEmpty(s) || s[0] != '#' || (s.Length != 4 && s.Length != 7)) return false;
        for (int i = 1; i < s.Length; i++) if (!Uri.IsHexDigit(s[i])) return false;
        return true;
    }

    // ------------------------------------------------------------------ persistance fichier
    private readonly object _fileLock = new();
    private System.IO.StreamWriter? _file;
    private bool _fileTried;

    private void WriteToFile(LuaLogEntry e)
    {
        lock (_fileLock)
        {
            if (_file is null)
            {
                if (_fileTried) return;                 // deja tente et echoue -> on n'insiste pas (best-effort)
                _fileTried = true;
                try
                {
                    string dir = System.IO.Path.Combine(MZP19LocalClone.DataRoot, "lua");
                    System.IO.Directory.CreateDirectory(dir);
                    string who = SanitizeFile(_world.Get(_pid).CharacterName) ?? _pid.ToString();
                    _file = new System.IO.StreamWriter(System.IO.Path.Combine(dir, who + ".log"), append: true) { AutoFlush = true };
                    _file.WriteLine($"===== session {DateTime.Now:yyyy-MM-dd HH:mm:ss} (pid {_pid}) =====");
                }
                catch { _file = null; return; }
            }
            try { _file.WriteLine($"{e.Time:HH:mm:ss} [{Tag(e.Level)}] {e.Category,-11} {e.Message}"); } catch { /* best-effort */ }
        }
    }

    private static string Tag(LuaLogLevel l) => l switch
    {
        LuaLogLevel.Success => "OK  ", LuaLogLevel.Warning => "WARN", LuaLogLevel.Danger => "ERR ",
        LuaLogLevel.Debug => "DBG ", _ => "INFO",
    };

    private static string? SanitizeFile(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}
