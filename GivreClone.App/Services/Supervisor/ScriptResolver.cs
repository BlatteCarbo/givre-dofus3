using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Supervisor;

/// <summary>Script résolu pour <c>bots:startScript</c> / <c>teams:start</c> : le code à lancer et un nom affichable.</summary>
public sealed record ResolvedScript(string Code, string Name, bool FromCode);

/// <summary>
/// Règle du portail : « script = nom (bibliothèque du planning du perso, puis dossier scripts) ou code Lua (texte contenant un saut de
/// ligne, une parenthèse ou un =) ». Un nom ne désigne jamais un chemin : séparateurs et « .. » sont refusés.
/// </summary>
public static class ScriptResolver
{
    public static bool LooksLikeCode(string s) => s.IndexOfAny(new[] { '\n', '\r', '(', '=' }) >= 0;

    /// <param name="library">Bibliothèque du planning du perso : (nom, corps).</param>
    /// <param name="readFromScriptsDir">Lit un fichier du dossier scripts par nom relatif simple ; null si absent.</param>
    public static (ResolvedScript? Script, string Reason) Resolve(string input, IEnumerable<(string Name, string Body)> library, Func<string, string?> readFromScriptsDir)
    {
        if (string.IsNullOrWhiteSpace(input)) return (null, "script manquant");
        if (LooksLikeCode(input)) return (new ResolvedScript(input, "code", true), "");

        var name = input.Trim();
        if (name.Contains('/') || name.Contains('\\') || name.Contains(':') || name.Contains(".."))
            return (null, "un script se désigne par son nom, jamais par un chemin");

        string Bare(string n) => n.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n;
        var lib = library.FirstOrDefault(l => string.Equals(Bare(l.Name), Bare(name), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(lib.Name) && !string.IsNullOrWhiteSpace(lib.Body)) return (new ResolvedScript(lib.Body, lib.Name, false), "");

        foreach (var candidate in new[] { name, Bare(name) + ".lua" })
            if (readFromScriptsDir(candidate) is { } body && !string.IsNullOrWhiteSpace(body))
                return (new ResolvedScript(body, candidate, false), "");
        return (null, $"script introuvable : {name}");
    }
}
