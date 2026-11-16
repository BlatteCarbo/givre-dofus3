using System;
using System.Collections.Generic;

namespace GivreClone.Engine.LuaTooling;

/// <summary>Utilitaires internes partagés : lignes, positions, comparaison de jetons sans allocation.</summary>
internal static class LuaText
{
    /// <summary>Offsets de début de chaque ligne (index 0 = ligne 1). Fins de ligne : \r\n, \n, \r (comme le lexer).</summary>
    public static List<int> LineStarts(string text)
    {
        var starts = new List<int>(text.Length / 32 + 2) { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n') starts.Add(i + 1);
            else if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                starts.Add(i + 1);
            }
        }
        return starts;
    }

    /// <summary>Index 0-based de la ligne contenant <paramref name="offset"/>.</summary>
    public static int LineIndexOf(List<int> starts, int offset)
    {
        int lo = 0, hi = starts.Count - 1, res = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (starts[mid] <= offset) { res = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return res;
    }

    /// <summary>(ligne 1-based, colonne 0-based) d'un offset.</summary>
    public static (int Line, int Column) LineCol(List<int> starts, int offset)
    {
        int li = LineIndexOf(starts, offset);
        return (li + 1, offset - starts[li]);
    }

    /// <summary>Offset de fin de la ligne (avant \r / \n) qui commence en <paramref name="lineStart"/>.</summary>
    public static int LineEnd(string text, int lineStart)
    {
        int j = lineStart;
        while (j < text.Length && text[j] != '\n' && text[j] != '\r') j++;
        return j;
    }

    /// <summary>Offset d'une position (ligne 1-based, colonne 0-based), borné au texte.</summary>
    public static int OffsetOf(string text, List<int> starts, int line, int column)
    {
        if (line < 1) return 0;
        if (line > starts.Count) return text.Length;
        int s = starts[line - 1];
        int e = LineEnd(text, s);
        return Math.Clamp(s + Math.Max(0, column), s, e);
    }

    public static bool Is(string text, in LuaToken t, string s) =>
        t.Length == s.Length && string.CompareOrdinal(text, t.Start, s, 0, s.Length) == 0;

    public static bool IsKw(string text, in LuaToken t, string kw) => t.Kind == LuaTokenKind.Keyword && Is(text, t, kw);

    public static bool IsPunct(string text, in LuaToken t, char c) =>
        t.Length == 1 && t.Kind == LuaTokenKind.Punctuation && text[t.Start] == c;

    public static bool IsOp(string text, in LuaToken t, string op) => t.Kind == LuaTokenKind.Operator && Is(text, t, op);

    /// <summary>« . » ou « : » simple (accès membre / appel de méthode).</summary>
    public static bool IsMemberSep(string text, in LuaToken t) =>
        t.Kind == LuaTokenKind.Punctuation && t.Length == 1 && (text[t.Start] == '.' || text[t.Start] == ':');

    /// <summary>true si le mot-clé d'index i est précédé de « . » / « : » (t.end, zaap:goto : erreur, pas un bloc).</summary>
    public static bool AfterMemberSep(string text, List<LuaToken> toks, int i) => i > 0 && IsMemberSep(text, toks[i - 1]);

    /// <summary>Indentation (espaces/tabs de tête) d'une ligne, en largeur (tab = 4).</summary>
    public static int IndentWidth(string text, int lineStart)
    {
        int w = 0;
        for (int j = lineStart; j < text.Length; j++)
        {
            char c = text[j];
            if (c == ' ') w++;
            else if (c == '\t') w += 4;
            else break;
        }
        return w;
    }

    /// <summary>true si le jeton est le premier de sa ligne (seuls des blancs le précèdent).</summary>
    public static bool IsFirstOnLine(string text, in LuaToken t)
    {
        for (int j = t.Start - 1; j >= 0; j--)
        {
            char c = text[j];
            if (c == '\n' || c == '\r') return true;
            if (c != ' ' && c != '\t' && c != '\f' && c != '\v' && c != '﻿') return false;
        }
        return true;
    }

    /// <summary>Contenu d'une chaîne courte simple ('abc' / "abc") sans échappement, sinon null.</summary>
    public static string? SimpleStringValue(string text, in LuaToken t)
    {
        if (t.Kind != LuaTokenKind.String || t.Unterminated || t.Length < 2) return null;
        char q = text[t.Start];
        if (q != '"' && q != '\'') return null;
        var s = text.Substring(t.Start + 1, t.Length - 2);
        return s.IndexOf('\\') >= 0 ? null : s;
    }
}
