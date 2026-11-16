using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GivreClone.App.Services;

/// <summary>
/// Texte d'un effet d'objet comme dans l'infobulle du jeu (« 143 Vitalité », « -8% Résistance Air »). Pur : testé tel quel.
/// Gabarit du client (effectsdataroot.descriptionId) : #1..#4 = paramètres ; {…} = bloc ; dans un bloc, ~N exige le
/// paramètre N (sinon le reste du bloc tombe), ~s / ~p gardent le reste au singulier / au pluriel, ~z si la valeur est 0.
/// Ex. « #1{~1~2 à }#2 Vitalité » : 143 -&gt; « 143 Vitalité » ; 10..15 -&gt; « 10 à 15 Vitalité ».
/// </summary>
public static class ItemEffectText
{
    /// <summary>Paramètres #1..#4 d'un effet : valeur entière, ou min/max (jet), ou dés (num, side, const), ou texte (#4).
    /// Un max égal au min (ou nul) n'est pas un intervalle : #2 reste vide et « {~1~2 à } » disparaît.</summary>
    public static string?[] Params(int value, int min, int max, int cst, string? text, string? objectName = null)
    {
        var p = new string?[4];
        if (min != 0 || max != 0)
        {
            p[0] = N(min);
            if (max != 0 && max != min) p[1] = N(max);
            if (cst != 0) p[2] = N(cst);
        }
        else if (value != 0) p[0] = N(value);
        else if (objectName is not null) p[0] = objectName;
        if (!string.IsNullOrEmpty(text)) p[3] = text;
        return p;
    }

    /// <summary>Texte final d'un effet. Sans gabarit connu : « Effet N : valeur ».</summary>
    public static string Format(int effectId, string? template, string?[] p)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            string val = string.Join(" – ", Array.FindAll(p, x => x is not null));
            return val.Length == 0 ? $"Effet {effectId}" : $"Effet {effectId} : {val}";
        }
        string s = Decode(StripTags(template), p);
        return Spaces.Replace(s, " ").Trim();
    }

    /// <summary>Malus (affiché en rouge) : bonusType négatif, valeur négative, ou texte qui commence par « - ».</summary>
    public static bool IsNegative(int bonusType, int value, string text)
        => bonusType < 0 || value < 0 || text.StartsWith('-');

    private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static readonly Regex Tags = new("<[^<>]*>", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s{2,}", RegexOptions.Compiled);
    private static string StripTags(string s) => Tags.Replace(s, "");

    private static string Decode(string s, string?[] p)
    {
        var sb = new StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '#' && i + 1 < s.Length && s[i + 1] is >= '1' and <= '9')
            {
                int n = s[i + 1] - '1';
                if (n < p.Length && p[n] is { } v) sb.Append(v);
                i++;
            }
            else if (c == '~' && i + 1 < s.Length)
            {
                char k = s[i + 1];
                bool keep = k switch
                {
                    >= '1' and <= '9' => k - '1' < p.Length && p[k - '1'] is not null,
                    's' => !Plural(p),
                    'p' => Plural(p),
                    'z' => IsZero(p),
                    _ => true,
                };
                if (!keep) return sb.ToString();   // condition fausse : le reste du bloc tombe
                i++;
            }
            else if (c == '{')
            {
                int end = Closing(s, i);
                if (end < 0) { sb.Append(c); continue; }
                sb.Append(Decode(s.Substring(i + 1, end - i - 1), p));
                i = end;
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static int Closing(string s, int open)
    {
        int depth = 0;
        for (int j = open; j < s.Length; j++)
        {
            if (s[j] == '{') depth++;
            else if (s[j] == '}' && --depth == 0) return j;
        }
        return -1;
    }

    // Pluriel : la plus grande valeur numérique (le max d'un jet) dépasse 1 en valeur absolue.
    private static bool Plural(string?[] p)
    {
        long best = 0;
        for (int i = 0; i < Math.Min(2, p.Length); i++)
            if (long.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) best = Math.Max(best, Math.Abs(v));
        return best > 1;
    }

    private static bool IsZero(string?[] p) => p.Length == 0 || p[0] is null || p[0] == "0";
}
