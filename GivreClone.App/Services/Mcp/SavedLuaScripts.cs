using System;
using System.IO;
using System.Linq;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services.Mcp;

public sealed class SavedLuaScripts
{
    private readonly string _root;
    private readonly object _gate = new();
    public SavedLuaScripts(string root) => _root = Path.GetFullPath(root);

    private string Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || name.Contains("..") || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Use a simple script name, not a path.");
        if (!name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) name += ".lua";
        string path = Path.Combine(_root, name);
        if (Directory.Exists(_root) && (File.GetAttributes(_root) & FileAttributes.ReparsePoint) != 0
            || File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
            || File.Exists(path + ".bak") && (File.GetAttributes(path + ".bak") & FileAttributes.ReparsePoint) != 0
            || File.Exists(path + ".tmp") && (File.GetAttributes(path + ".tmp") & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Script links are not supported.");
        return path;
    }

    public static string? SyntaxError(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 200_000) return "Script must contain 1 to 200000 characters.";
        try { new Script(CoreModules.Preset_SoftSandbox).LoadString(code); return null; }
        catch (SyntaxErrorException ex) { return ex.DecoratedMessage ?? ex.Message; }
    }

    public string[] List()
    {
        lock (_gate) return Directory.Exists(_root) ? Directory.EnumerateFiles(_root, "*.lua")
            .Select(Path.GetFileName).OfType<string>().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Take(200).ToArray() : Array.Empty<string>();
    }

    public string Read(string name)
    {
        lock (_gate)
        {
            string path = Resolve(name);
            if (!File.Exists(path)) throw new ArgumentException("Saved script not found.");
            if (new FileInfo(path).Length > 800_000) throw new ArgumentException("Saved script is too large.");
            return File.ReadAllText(path);
        }
    }

    public void Save(string name, string code, bool overwrite)
    {
        if (SyntaxError(code) is { } error) throw new ArgumentException(error);
        lock (_gate)
        {
            string path = Resolve(name);
            if (File.Exists(path) && !overwrite) throw new ArgumentException("Script already exists; explicitly request overwrite to replace it.");
            AtomicJson.Write(path, code, keepBackup: true);
        }
    }

    public void Patch(string name, string oldText, string newText)
    {
        if (oldText.Length == 0) throw new ArgumentException("Replacement text must not be empty.");
        lock (_gate)
        {
            string code = Read(name);
            int first = code.IndexOf(oldText, StringComparison.Ordinal);
            if (first < 0 || code.IndexOf(oldText, first + oldText.Length, StringComparison.Ordinal) >= 0)
                throw new ArgumentException("Replacement must match exactly once; read the script first.");
            Save(name, code[..first] + newText + code[(first + oldText.Length)..], overwrite: true);
        }
    }
}
