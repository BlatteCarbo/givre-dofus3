namespace GivreClone.Engine;

/// <summary>
/// Résolution SÛRE d'un nom de module Lua (<c>require("util")</c>, <c>require("a.b")</c>, <c>loadScript("vente")</c>) en
/// chemin de fichier SOUS un dossier racine. Pur (aucune E/S) : existence, taille et points de réanalyse sont vérifiés par
/// l'appelant. Refuse tout ce qui pourrait sortir de la racine (.., séparateurs, lecteur, noms trop longs).
/// </summary>
public static class LuaModulePath
{
    public const int MaxNameLength = 64;
    public const int MaxSegments = 4;

    /// <summary>Normalise un nom (trim + suppression d'un suffixe .lua) ; null si invalide. Segments séparés par '.',
    /// chacun non vide et limité à [A-Za-z0-9_-] ; 1 à 4 segments ; 1 à 64 caractères.</summary>
    public static string? Normalize(string? name)
    {
        if (name is null) return null;
        string n = name.Trim();
        if (n.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
        if (n.Length < 1 || n.Length > MaxNameLength) return null;
        int segments = 1, segLen = 0;
        foreach (char c in n)
        {
            if (c == '.')
            {
                if (segLen == 0) return null;
                segments++; segLen = 0;
                if (segments > MaxSegments) return null;
                continue;
            }
            bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
            if (!ok) return null;
            segLen++;
        }
        return segLen == 0 ? null : n;
    }

    public static bool TryResolve(string baseDir, string name, out string fullPath, out string? error)
    {
        fullPath = "";
        error = null;
        var n = Normalize(name);
        if (n is null) { error = "bad-name"; return false; }
        try
        {
            string rel = string.Join(Path.DirectorySeparatorChar, n.Split('.')) + ".lua";
            string root = Path.GetFullPath(baseDir);
            if (!root.EndsWith(Path.DirectorySeparatorChar)) root += Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(root, rel));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { error = "outside"; return false; }
            fullPath = full;
            return true;
        }
        catch
        {
            error = "bad-name";
            return false;
        }
    }
}
