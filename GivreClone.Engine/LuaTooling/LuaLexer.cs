using System;
using System.Collections.Generic;

namespace GivreClone.Engine.LuaTooling;

/// <summary>Nature d'un jeton Lua (coloration, formatage, analyse).</summary>
public enum LuaTokenKind { Keyword, Identifier, Number, String, Comment, Operator, Punctuation, Unknown }

/// <summary>
/// Jeton Lua : position absolue (offset UTF-16), ligne 1-based et colonne 0-based UTF-16 de son DÉBUT.
/// <see cref="Unterminated"/> = chaîne, chaîne longue ou commentaire long jamais fermé.
/// </summary>
public readonly record struct LuaToken(LuaTokenKind Kind, int Start, int Length, int Line, int Column, bool Unterminated)
{
    /// <summary>Offset juste après le jeton (exclusif).</summary>
    public int End => Start + Length;
}

/// <summary>
/// Lexer Lua 5.2 (dialecte MoonSharp) tolérant : ne lève jamais, découpe même un texte invalide.
/// Les blancs et fins de ligne ne sont pas des jetons. Les opérateurs INVALIDES courants (!=, &amp;&amp;, ||, +=, -=,
/// *=, /=, ++, //) sont reconnus comme un seul jeton Operator pour que l'analyse puisse les signaler.
/// </summary>
public static class LuaLexer
{
    private static readonly HashSet<string> KeywordSet = new(StringComparer.Ordinal)
    {
        "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if", "in",
        "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while",
    };

    /// <summary>Les 22 mots-clés de Lua 5.2.</summary>
    public static IReadOnlyCollection<string> Keywords => KeywordSet;

    /// <summary>true si le mot est un mot-clé Lua 5.2 (casse exacte).</summary>
    public static bool IsKeyword(string word) => word != null && KeywordSet.Contains(word);

    /// <summary>Version sans allocation de <see cref="IsKeyword(string)"/>.</summary>
    internal static bool IsKeyword(ReadOnlySpan<char> w)
    {
        switch (w.Length)
        {
            case 2: return w.SequenceEqual("do") || w.SequenceEqual("if") || w.SequenceEqual("in") || w.SequenceEqual("or");
            case 3: return w.SequenceEqual("and") || w.SequenceEqual("end") || w.SequenceEqual("for") || w.SequenceEqual("nil") || w.SequenceEqual("not");
            case 4: return w.SequenceEqual("else") || w.SequenceEqual("goto") || w.SequenceEqual("then") || w.SequenceEqual("true");
            case 5: return w.SequenceEqual("break") || w.SequenceEqual("false") || w.SequenceEqual("local") || w.SequenceEqual("until") || w.SequenceEqual("while");
            case 6: return w.SequenceEqual("elseif") || w.SequenceEqual("repeat") || w.SequenceEqual("return");
            case 8: return w.SequenceEqual("function");
            default: return false;
        }
    }

    /// <summary>Texte d'un jeton.</summary>
    public static string Slice(string text, LuaToken t) => text.Substring(t.Start, t.Length);

    /// <summary>
    /// Découpe le texte en jetons. Ne lève jamais. Chaîne courte non fermée : le jeton s'arrête avant la fin de ligne
    /// (Unterminated). Chaîne / commentaire long non fermé : le jeton court jusqu'à la fin du texte (Unterminated).
    /// </summary>
    public static IReadOnlyList<LuaToken> Tokenize(string text) => TokenizeList(text);

    internal static List<LuaToken> TokenizeList(string? text)
    {
        var list = new List<LuaToken>(text is null ? 0 : text.Length / 5 + 4);
        if (string.IsNullOrEmpty(text)) return list;
        int n = text.Length, i = 0, line = 1, lineStart = 0;

        while (i < n)
        {
            char c = text[i];
            if (c == '\n') { i++; line++; lineStart = i; continue; }
            if (c == '\r') { i++; if (i < n && text[i] == '\n') i++; line++; lineStart = i; continue; }
            if (c == ' ' || c == '\t' || c == '\f' || c == '\v' || c == '﻿' || (c > 127 && char.IsWhiteSpace(c))) { i++; continue; }

            int start = i, tokLine = line, col = i - lineStart;

            // Commentaires : --[==[ ... ]==] ou -- jusqu'à la fin de ligne.
            if (c == '-' && i + 1 < n && text[i + 1] == '-')
            {
                int lvl = LongBracketLevel(text, i + 2);
                if (lvl >= 0)
                {
                    bool closed = SkipLongBracket(text, i + 2, lvl, ref line, ref lineStart, out int end);
                    list.Add(new LuaToken(LuaTokenKind.Comment, start, end - start, tokLine, col, !closed));
                    i = end;
                }
                else
                {
                    int j = i + 2;
                    while (j < n && text[j] != '\n' && text[j] != '\r') j++;
                    list.Add(new LuaToken(LuaTokenKind.Comment, start, j - start, tokLine, col, false));
                    i = j;
                }
                continue;
            }

            // Chaînes longues [[ ]] / [==[ ]==].
            if (c == '[')
            {
                int lvl = LongBracketLevel(text, i);
                if (lvl >= 0)
                {
                    bool closed = SkipLongBracket(text, i, lvl, ref line, ref lineStart, out int end);
                    list.Add(new LuaToken(LuaTokenKind.String, start, end - start, tokLine, col, !closed));
                    i = end;
                    continue;
                }
            }

            // Chaînes courtes '..' / "..".
            if (c == '"' || c == '\'')
            {
                bool closed = false;
                int j = i + 1;
                while (j < n)
                {
                    char d = text[j];
                    if (d == c) { j++; closed = true; break; }
                    if (d == '\n' || d == '\r') break;   // fin de ligne non échappée : chaîne non terminée
                    if (d == '\\')
                    {
                        j++;
                        if (j >= n) break;
                        char e = text[j];
                        if (e == '\r' || e == '\n')
                        {
                            // \ + fin de ligne : saut de ligne dans la chaîne
                            j++;
                            if (e == '\r' && j < n && text[j] == '\n') j++;
                            line++; lineStart = j;
                        }
                        else if (e == 'z')
                        {
                            j++;
                            while (j < n)
                            {
                                char w = text[j];
                                if (w == '\r' || w == '\n')
                                {
                                    j++;
                                    if (w == '\r' && j < n && text[j] == '\n') j++;
                                    line++; lineStart = j;
                                }
                                else if (w == ' ' || w == '\t' || w == '\f' || w == '\v') j++;
                                else break;
                            }
                        }
                        else j++;
                        continue;
                    }
                    j++;
                }
                list.Add(new LuaToken(LuaTokenKind.String, start, j - start, tokLine, col, !closed));
                i = j;
                continue;
            }

            // Nombres (algorithme du lexer MoonSharp : un seul '.', exposant e/E ou p/P avec signe).
            if (IsDigit(c) || (c == '.' && i + 1 < n && IsDigit(text[i + 1])))
            {
                int j = ReadNumber(text, i);
                list.Add(new LuaToken(LuaTokenKind.Number, start, j - start, tokLine, col, false));
                i = j;
                continue;
            }

            // Identifiants / mots-clés.
            if (IsIdentStart(c))
            {
                int j = i + 1;
                while (j < n && IsIdentPart(text[j])) j++;
                var kind = IsKeyword(text.AsSpan(i, j - i)) ? LuaTokenKind.Keyword : LuaTokenKind.Identifier;
                list.Add(new LuaToken(kind, start, j - start, tokLine, col, false));
                i = j;
                continue;
            }

            // Opérateurs et ponctuation.
            char nx = i + 1 < n ? text[i + 1] : '\0';
            int len = 1;
            LuaTokenKind k;
            switch (c)
            {
                case '.':
                    if (nx == '.') { len = (i + 2 < n && text[i + 2] == '.') ? 3 : 2; k = LuaTokenKind.Operator; }
                    else k = LuaTokenKind.Punctuation;
                    break;
                case ':':
                    if (nx == ':') len = 2;
                    k = LuaTokenKind.Punctuation;
                    break;
                case '=': case '<': case '>': case '~':
                    if (nx == '=') { len = 2; k = LuaTokenKind.Operator; }
                    else k = c == '~' ? LuaTokenKind.Unknown : LuaTokenKind.Operator;
                    break;
                case '!':
                    if (nx == '=') { len = 2; k = LuaTokenKind.Operator; }
                    else k = LuaTokenKind.Unknown;
                    break;
                case '&':
                    if (nx == '&') { len = 2; k = LuaTokenKind.Operator; }
                    else k = LuaTokenKind.Unknown;
                    break;
                case '|':
                    if (nx == '|') { len = 2; k = LuaTokenKind.Operator; }
                    else k = LuaTokenKind.Unknown;
                    break;
                case '+':
                    if (nx == '=' || nx == '+') len = 2;
                    k = LuaTokenKind.Operator;
                    break;
                case '-': case '*':
                    if (nx == '=') len = 2;
                    k = LuaTokenKind.Operator;
                    break;
                case '/':
                    if (nx == '=' || nx == '/') len = 2;
                    k = LuaTokenKind.Operator;
                    break;
                case '%': case '^': case '#':
                    k = LuaTokenKind.Operator;
                    break;
                case '(': case ')': case '{': case '}': case '[': case ']': case ';': case ',':
                    k = LuaTokenKind.Punctuation;
                    break;
                default:
                    k = LuaTokenKind.Unknown;
                    if (char.IsHighSurrogate(c) && char.IsLowSurrogate(nx)) len = 2;
                    break;
            }
            list.Add(new LuaToken(k, start, len, tokLine, col, false));
            i += len;
        }
        return list;
    }

    /// <summary>
    /// false si <paramref name="offset"/> tombe DANS une chaîne ou un commentaire (entre ses délimiteurs), ou à la fin
    /// d'un commentaire de ligne (ou d'un jeton non terminé). Au bord extérieur d'un jeton fermé : true.
    /// </summary>
    public static bool IsInCodeAt(string text, int offset)
    {
        if (string.IsNullOrEmpty(text)) return true;
        if (offset < 0) offset = 0;
        if (offset > text.Length) offset = text.Length;
        var toks = TokenizeList(text);
        int idx = LastTokenStartingBefore(toks, offset);
        if (idx < 0) return true;
        var t = toks[idx];
        if (t.Kind != LuaTokenKind.String && t.Kind != LuaTokenKind.Comment) return true;
        if (offset < t.End) return false;   // Start < offset < End
        if (offset == t.End)
        {
            if (t.Unterminated) return false;
            if (t.Kind == LuaTokenKind.Comment && !IsLongComment(text, t)) return false;
        }
        return true;
    }

    /// <summary>Index du dernier jeton dont Start &lt; offset (recherche dichotomique), -1 si aucun.</summary>
    internal static int LastTokenStartingBefore(List<LuaToken> toks, int offset)
    {
        int lo = 0, hi = toks.Count - 1, res = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (toks[mid].Start < offset) { res = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return res;
    }

    /// <summary>Niveau d'un crochet long ouvrant en <paramref name="p"/> ([[ = 0, [=[ = 1…), -1 si ce n'en est pas un.</summary>
    internal static int LongBracketLevel(string text, int p)
    {
        if (p >= text.Length || text[p] != '[') return -1;
        int j = p + 1, lvl = 0;
        while (j < text.Length && text[j] == '=') { lvl++; j++; }
        return j < text.Length && text[j] == '[' ? lvl : -1;
    }

    /// <summary>true si le commentaire est un commentaire long (--[[ ... ]]).</summary>
    internal static bool IsLongComment(string text, LuaToken t) =>
        t.Kind == LuaTokenKind.Comment && t.Length >= 4 && LongBracketLevel(text, t.Start + 2) >= 0;

    /// <summary>true si la chaîne est une chaîne longue ([[ ... ]]).</summary>
    internal static bool IsLongString(string text, LuaToken t) =>
        t.Kind == LuaTokenKind.String && t.Length >= 2 && text[t.Start] == '[';

    // Parcourt un crochet long ouvert en p (niveau lvl) ; end = juste après le crochet fermant (ou fin du texte).
    private static bool SkipLongBracket(string text, int p, int lvl, ref int line, ref int lineStart, out int end)
    {
        int n = text.Length;
        int j = p + lvl + 2;
        while (j < n)
        {
            char d = text[j];
            if (d == ']')
            {
                int k = j + 1, eq = 0;
                while (k < n && text[k] == '=' && eq < lvl) { eq++; k++; }
                if (eq == lvl && k < n && text[k] == ']') { end = k + 1; return true; }
                j++;
                continue;
            }
            if (d == '\n') { j++; line++; lineStart = j; continue; }
            if (d == '\r') { j++; if (j < n && text[j] == '\n') j++; line++; lineStart = j; continue; }
            j++;
        }
        end = n;
        return false;
    }

    private static int ReadNumber(string text, int i)
    {
        int n = text.Length;
        bool isHex = false, dot = false, expo = false, signOk = false;
        if (text[i] == '.') dot = true;
        int j = i + 1;
        if (text[i] == '0' && j < n && (text[j] == 'x' || text[j] == 'X')) { isHex = true; j++; }
        while (j < n)
        {
            char c = text[j];
            if (signOk && (c == '+' || c == '-')) { signOk = false; j++; continue; }
            signOk = false;
            if (IsDigit(c)) { j++; continue; }
            if (c == '.' && !dot) { dot = true; j++; continue; }
            if (isHex && !expo && IsHexLetter(c)) { j++; continue; }
            if (!expo && ((!isHex && (c == 'e' || c == 'E')) || (isHex && (c == 'p' || c == 'P'))))
            {
                expo = true; signOk = true; dot = true; j++;
                continue;
            }
            break;
        }
        return j;
    }

    internal static bool IsDigit(char c) => c >= '0' && c <= '9';
    private static bool IsHexLetter(char c) => (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    internal static bool IsIdentStart(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_' || (c > 127 && char.IsLetter(c));
    internal static bool IsIdentPart(char c) => IsIdentStart(c) || IsDigit(c) || (c > 127 && char.IsDigit(c));
}
