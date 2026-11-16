using System;
using System.Collections.Generic;
using System.Text;

namespace GivreClone.Engine.LuaTooling;

/// <summary>Comparaisons de noms : forme tolérante des modules, repli des accents (recherche), distance d'édition bornée.</summary>
internal static class LuaNameMatching
{
    /// <summary>Forme tolérante d'un membre de module : minuscules, sans « _ » (getPods = GetPods = get_pods = GETPODS).
    /// Un peu plus large que le moteur (1re lettre, snake_case, casse) : jamais de faux positif « membre inconnu ».</summary>
    public static string Tolerant(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            if (c != '_') sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>Minuscules sans accents (recherche insensible à la casse et aux diacritiques ; indépendant de l'ICU).</summary>
    public static string Fold(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (char c0 in s)
        {
            char c = c0 < 128 ? char.ToLowerInvariant(c0) : c0;
            if (c < 128) { sb.Append(c); continue; }
            switch (c)
            {
                case 'à': case 'á': case 'â': case 'ã': case 'ä': case 'å': case 'À': case 'Á': case 'Â': case 'Ã': case 'Ä': case 'Å': sb.Append('a'); break;
                case 'ç': case 'Ç': sb.Append('c'); break;
                case 'è': case 'é': case 'ê': case 'ë': case 'È': case 'É': case 'Ê': case 'Ë': sb.Append('e'); break;
                case 'ì': case 'í': case 'î': case 'ï': case 'Ì': case 'Í': case 'Î': case 'Ï': sb.Append('i'); break;
                case 'ñ': case 'Ñ': sb.Append('n'); break;
                case 'ò': case 'ó': case 'ô': case 'õ': case 'ö': case 'Ò': case 'Ó': case 'Ô': case 'Õ': case 'Ö': sb.Append('o'); break;
                case 'ù': case 'ú': case 'û': case 'ü': case 'Ù': case 'Ú': case 'Û': case 'Ü': sb.Append('u'); break;
                case 'ý': case 'ÿ': case 'Ý': case 'Ÿ': sb.Append('y'); break;
                case 'œ': case 'Œ': sb.Append("oe"); break;
                case 'æ': case 'Æ': sb.Append("ae"); break;
                case '’': sb.Append('\''); break;
                default: sb.Append(char.ToLowerInvariant(c)); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Distance de Levenshtein insensible à la casse, abandonnée au-delà de <paramref name="max"/> (rend max+1).</summary>
    public static int Distance(string a, string b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max) return max + 1;
        int n = a.Length, m = b.Length;
        Span<int> prev = m + 1 <= 128 ? stackalloc int[m + 1] : new int[m + 1];
        Span<int> cur = m + 1 <= 128 ? stackalloc int[m + 1] : new int[m + 1];
        for (int j = 0; j <= m; j++) prev[j] = j;
        for (int i = 1; i <= n; i++)
        {
            cur[0] = i;
            int rowMin = cur[0];
            char ca = char.ToLowerInvariant(a[i - 1]);
            for (int j = 1; j <= m; j++)
            {
                int cost = ca == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                int v = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
                cur[j] = v;
                if (v < rowMin) rowMin = v;
            }
            if (rowMin > max) return max + 1;
            var tmp = prev; prev = cur; cur = tmp;
        }
        return prev[m];
    }

    /// <summary>
    /// Nom proche : égal sans tenir compte de la casse d'abord, sinon distance ≤ 2 (noms d'au moins 4 caractères).
    /// À égalité, le premier candidat de la liste l'emporte (ordre = priorité).
    /// </summary>
    public static string? Closest(string name, IEnumerable<string> candidates)
    {
        if (string.IsNullOrEmpty(name)) return null;
        string? best = null;
        int bestD = int.MaxValue;
        foreach (var c in candidates)
        {
            if (string.IsNullOrEmpty(c) || c == name) continue;
            if (string.Equals(c, name, StringComparison.OrdinalIgnoreCase)) return c;
            if (name.Length < 4 || c.Length < 3 || bestD <= 1) continue;
            int d = Distance(name, c, 2);
            if (d <= 2 && d < bestD) { bestD = d; best = c; }
        }
        return best;
    }
}
