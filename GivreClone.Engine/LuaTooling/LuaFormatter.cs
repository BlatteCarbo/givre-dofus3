using System;
using System.Collections.Generic;
using System.Text;

namespace GivreClone.Engine.LuaTooling;

/// <summary>Options du formateur Lua.</summary>
public sealed record LuaFormatOptions(int IndentSize = 4, bool UseTabs = false, bool TrimTrailingWhitespace = true, bool EnsureFinalNewline = true, int MaxConsecutiveBlankLines = 2);

/// <summary>
/// Ré-indentation Lua par structure de blocs (function/do/then/repeat/( /{ /[ ouvrent ; end/until/)/}/] ferment ;
/// else/elseif désindentent leur propre ligne). Le code reste sur ses lignes (aucune refonte) ; le contenu des chaînes et
/// commentaires n'est jamais touché (lignes internes d'une chaîne / d'un commentaire long recopiées telles quelles).
/// Plusieurs ouvertures sur une même ligne ne valent qu'UN niveau (foo(function() … end)). Idempotent.
/// </summary>
public static class LuaFormatter
{
    private enum Fk : byte { Then, Else, Block, Repeat, Paren, Brace, Bracket }
    private readonly record struct Frame(Fk Kind, int Base);
    private enum Act : byte { None, OpenThen, OpenBlock, OpenRepeat, OpenParen, OpenBrace, OpenBracket, CloseEnd, CloseUntil, CloseParen, CloseBrace, CloseBracket, Else, ElseIf }

    /// <summary>Formate le texte (voir la classe). Ne lève jamais : sur du code invalide, sortie au mieux sans perdre de
    /// caractère autre que des blancs de fin de ligne / lignes vides en excès.</summary>
    public static string Format(string text, LuaFormatOptions? options = null)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        options ??= new LuaFormatOptions();
        try { return FormatCore(text, options); }
        catch { return text; }   // filet : jamais d'exception vers l'éditeur
    }

    private static string FormatCore(string text, LuaFormatOptions options)
    {
        var toks = LuaLexer.TokenizeList(text);
        var starts = LuaText.LineStarts(text);
        int nLines = starts.Count;
        var lineEnd = new int[nLines];
        for (int l = 0; l < nLines; l++) lineEnd[l] = LuaText.LineEnd(text, starts[l]);
        ComputeLineFlags(text, toks, starts, lineEnd, out var startIn, out var endIn);

        string unit = options.UseTabs ? "\t" : new string(' ', Math.Clamp(options.IndentSize, 1, 16));
        int maxBlank = Math.Max(0, options.MaxConsecutiveBlankLines);
        string defaultNl = DetectNewline(text);
        var stack = new List<Frame>();
        var sb = new StringBuilder(text.Length + text.Length / 8 + 16);
        int blankRun = 0, ti = 0, lastContentLen = -1;
        string lastTerm = "";
        bool lastEndIn = false;

        for (int l = 0; l < nLines; l++)
        {
            int a = ti;
            while (ti < toks.Count && toks[ti].Line == l + 1) ti++;
            int b = ti;

            int lineIndent = stack.Count == 0 ? 0 : stack[^1].Base + 1;
            int k = a;
            if (!startIn[l])
            {
                bool popped = false;
                int minBase = int.MaxValue;
                while (k < b)
                {
                    var act = Classify(text, toks, k);
                    if (!IsCloser(act)) break;
                    if (PopFor(stack, act) is { } f)
                    {
                        popped = true;
                        minBase = Math.Min(minBase, f.Base);
                        if (act == Act.Else) stack.Add(new Frame(Fk.Else, f.Base));
                    }
                    k++;
                    if (act is Act.Else or Act.ElseIf) break;
                }
                if (popped) lineIndent = minBase;
            }
            for (; k < b; k++) Apply(stack, Classify(text, toks, k), lineIndent);

            // Émission de la ligne.
            int ls = starts[l], le = lineEnd[l];
            int termEnd = l + 1 < nLines ? starts[l + 1] : text.Length;
            string term = text.Substring(le, termEnd - le);
            if (!startIn[l])
            {
                int cs = ls;
                while (cs < le && IsWs(text[cs])) cs++;
                int ce = le;
                if (options.TrimTrailingWhitespace && !endIn[l]) while (ce > cs && IsWs(text[ce - 1])) ce--;
                if (cs == le && !endIn[l])
                {
                    if (++blankRun > maxBlank) continue;   // ligne vide en trop : supprimée
                    sb.Append(term);
                    continue;
                }
                blankRun = 0;
                for (int d = 0; d < lineIndent; d++) sb.Append(unit);
                sb.Append(text, cs, ce - cs);
            }
            else
            {
                // Ligne qui commence DANS une chaîne / un commentaire long : recopiée telle quelle.
                int ce = le;
                if (options.TrimTrailingWhitespace && !endIn[l]) while (ce > ls && IsWs(text[ce - 1])) ce--;
                blankRun = 0;
                sb.Append(text, ls, ce - ls);
            }
            lastContentLen = sb.Length;
            lastTerm = term;
            lastEndIn = endIn[l];
            sb.Append(term);
        }

        // Chaîne / commentaire long ouvert jusqu'à la fin : la fin du texte lui appartient, on n'y touche pas.
        bool eofInToken = toks.Count > 0 && toks[^1].Unterminated && toks[^1].End == text.Length
                          && (LuaLexer.IsLongString(text, toks[^1]) || LuaLexer.IsLongComment(text, toks[^1]));
        if (options.EnsureFinalNewline && !eofInToken)
        {
            if (lastContentLen < 0) return "";   // que des blancs
            if (!lastEndIn)
            {
                sb.Length = lastContentLen;   // retire les lignes vides finales
                sb.Append(lastTerm.Length > 0 ? lastTerm : defaultNl);
            }
        }
        return sb.ToString();
    }

    /// <summary>Devine l'indentation existante : tabulations ou espaces, et taille 2 ou 4 (4 par défaut).</summary>
    public static LuaFormatOptions DetectOptions(string text)
    {
        if (string.IsNullOrEmpty(text)) return new LuaFormatOptions();
        try
        {
            var toks = LuaLexer.TokenizeList(text);
            var starts = LuaText.LineStarts(text);
            int nLines = starts.Count;
            var lineEnd = new int[nLines];
            for (int l = 0; l < nLines; l++) lineEnd[l] = LuaText.LineEnd(text, starts[l]);
            ComputeLineFlags(text, toks, starts, lineEnd, out var startIn, out _);
            int tabs = 0, spaces = 0, votes2 = 0, votes4 = 0, prev = -1;
            for (int l = 0; l < nLines; l++)
            {
                if (startIn[l]) continue;
                int s = starts[l], e = lineEnd[l], j = s, width = 0;
                bool tab = false;
                while (j < e && (text[j] == ' ' || text[j] == '\t'))
                {
                    if (text[j] == '\t') { tab = true; width += 4; } else width++;
                    j++;
                }
                if (j == e) continue;   // ligne vide
                if (j > s) { if (text[s] == '\t') tabs++; else spaces++; }
                if (!tab && prev >= 0 && width > prev)
                {
                    int d = width - prev;
                    if (d == 2) votes2++;
                    else if (d == 4 || d == 8) votes4++;
                }
                prev = width;
            }
            return new LuaFormatOptions(IndentSize: votes2 > votes4 ? 2 : 4, UseTabs: tabs > spaces);
        }
        catch { return new LuaFormatOptions(); }
    }

    /// <summary>
    /// Auto-indentation à l'Entrée : 1 si la ligne ouvre un bloc qu'elle ne referme pas ('if x then', 'function f()',
    /// 'for … do', 'repeat', 'local t = {', 'else'…), 0 sinon. Plafonné à 1, comme le formateur (une ligne = un niveau).
    /// </summary>
    public static int IndentDeltaAfterLine(string lineText)
    {
        if (string.IsNullOrEmpty(lineText)) return 0;
        try
        {
            var toks = LuaLexer.TokenizeList(lineText);
            int net = 0, k = 0;
            while (k < toks.Count)
            {
                var act = Classify(lineText, toks, k);
                if (!IsCloser(act)) break;
                k++;
                if (act == Act.Else) { net = 1; break; }
                if (act == Act.ElseIf) break;
            }
            for (; k < toks.Count; k++)
            {
                switch (Classify(lineText, toks, k))
                {
                    case Act.OpenThen: case Act.OpenBlock: case Act.OpenRepeat: case Act.OpenParen: case Act.OpenBrace: case Act.OpenBracket:
                        net++; break;
                    case Act.CloseEnd: case Act.CloseUntil: case Act.CloseParen: case Act.CloseBrace: case Act.CloseBracket: case Act.ElseIf:
                        net--; break;
                }
            }
            return net > 0 ? 1 : 0;
        }
        catch { return 0; }
    }

    /// <summary>true si la ligne (sans blancs de tête) commence par end / else / elseif / until / } / ) / ] : l'éditeur
    /// la désindente d'un niveau à la frappe.</summary>
    public static bool IsDedentLine(string lineTextTrimmedStart)
    {
        if (string.IsNullOrEmpty(lineTextTrimmedStart)) return false;
        var s = lineTextTrimmedStart.AsSpan().TrimStart();
        if (s.Length == 0) return false;
        if (s[0] is '}' or ')' or ']') return true;
        return StartsWithWord(s, "end") || StartsWithWord(s, "else") || StartsWithWord(s, "elseif") || StartsWithWord(s, "until");
    }

    private static bool StartsWithWord(ReadOnlySpan<char> s, string w) =>
        s.StartsWith(w, StringComparison.Ordinal) && (s.Length == w.Length || !LuaLexer.IsIdentPart(s[w.Length]));

    // ------------------------------------------------------------------ interne

    /// <summary>startIn[l] : la ligne commence DANS un jeton multi-ligne ; endIn[l] : sa fin de ligne est DANS un jeton.</summary>
    internal static void ComputeLineFlags(string text, List<LuaToken> toks, List<int> starts, int[] lineEnd, out bool[] startIn, out bool[] endIn)
    {
        int n = starts.Count;
        startIn = new bool[n];
        endIn = new bool[n];
        foreach (var t in toks)
        {
            if (t.Kind != LuaTokenKind.String && t.Kind != LuaTokenKind.Comment) continue;
            int l = t.Line - 1;
            // Chaîne / commentaire long jamais fermé : tout le reste du texte lui appartient (y compris ses blancs de fin).
            bool open = t.Unterminated && (LuaLexer.IsLongString(text, t) || LuaLexer.IsLongComment(text, t));
            if (l < 0 || l >= n || (!open && t.End <= lineEnd[l])) continue;
            while (l < n && (open || lineEnd[l] < t.End))
            {
                endIn[l] = true;
                if (l + 1 < n) startIn[l + 1] = true;
                l++;
            }
        }
    }

    private static Act Classify(string text, List<LuaToken> toks, int i)
    {
        var t = toks[i];
        if (t.Kind == LuaTokenKind.Keyword)
        {
            if (LuaText.AfterMemberSep(text, toks, i)) return Act.None;
            switch (t.Length)
            {
                case 2: return LuaText.Is(text, t, "do") ? Act.OpenBlock : Act.None;
                case 3: return LuaText.Is(text, t, "end") ? Act.CloseEnd : Act.None;
                case 4: return LuaText.Is(text, t, "then") ? Act.OpenThen : LuaText.Is(text, t, "else") ? Act.Else : Act.None;
                case 5: return LuaText.Is(text, t, "until") ? Act.CloseUntil : Act.None;
                case 6: return LuaText.Is(text, t, "repeat") ? Act.OpenRepeat : LuaText.Is(text, t, "elseif") ? Act.ElseIf : Act.None;
                case 8: return LuaText.Is(text, t, "function") ? Act.OpenBlock : Act.None;
                default: return Act.None;
            }
        }
        if (t.Kind == LuaTokenKind.Punctuation && t.Length == 1)
        {
            return text[t.Start] switch
            {
                '(' => Act.OpenParen, '{' => Act.OpenBrace, '[' => Act.OpenBracket,
                ')' => Act.CloseParen, '}' => Act.CloseBrace, ']' => Act.CloseBracket,
                _ => Act.None,
            };
        }
        return Act.None;
    }

    private static bool IsCloser(Act a) => a is Act.CloseEnd or Act.CloseUntil or Act.CloseParen or Act.CloseBrace or Act.CloseBracket or Act.Else or Act.ElseIf;

    private static void Apply(List<Frame> stack, Act act, int lineIndent)
    {
        switch (act)
        {
            case Act.OpenThen: stack.Add(new Frame(Fk.Then, lineIndent)); break;
            case Act.OpenBlock: stack.Add(new Frame(Fk.Block, lineIndent)); break;
            case Act.OpenRepeat: stack.Add(new Frame(Fk.Repeat, lineIndent)); break;
            case Act.OpenParen: stack.Add(new Frame(Fk.Paren, lineIndent)); break;
            case Act.OpenBrace: stack.Add(new Frame(Fk.Brace, lineIndent)); break;
            case Act.OpenBracket: stack.Add(new Frame(Fk.Bracket, lineIndent)); break;
            case Act.Else:
                if (PopFor(stack, act) is { } f) stack.Add(new Frame(Fk.Else, f.Base));
                break;
            case Act.CloseEnd: case Act.CloseUntil: case Act.CloseParen: case Act.CloseBrace: case Act.CloseBracket: case Act.ElseIf:
                PopFor(stack, act);
                break;
        }
    }

    // Dépile jusqu'au cadre que ferme 'act' (en sautant les cadres « sautables » laissés ouverts) ; null = fermant orphelin.
    private static Frame? PopFor(List<Frame> stack, Act act)
    {
        for (int j = stack.Count - 1; j >= 0; j--)
        {
            var k = stack[j].Kind;
            if (Accepts(act, k))
            {
                var f = stack[j];
                stack.RemoveRange(j, stack.Count - j);
                return f;
            }
            if (!Skippable(act, k)) return null;
        }
        return null;
    }

    private static bool Accepts(Act a, Fk k) => a switch
    {
        Act.CloseEnd => k is Fk.Then or Fk.Else or Fk.Block,
        Act.CloseUntil => k == Fk.Repeat,
        Act.CloseParen => k == Fk.Paren,
        Act.CloseBrace => k == Fk.Brace,
        Act.CloseBracket => k == Fk.Bracket,
        Act.Else or Act.ElseIf => k == Fk.Then,
        _ => false,
    };

    private static bool Skippable(Act a, Fk k) => a switch
    {
        Act.CloseParen or Act.CloseBrace or Act.CloseBracket => true,
        _ => k is Fk.Paren or Fk.Brace or Fk.Bracket,
    };

    private static bool IsWs(char c) => c == ' ' || c == '\t' || c == '\f' || c == '\v';

    private static string DetectNewline(string text)
    {
        int i = text.IndexOfAny(['\r', '\n']);
        if (i < 0) return "\n";
        return text[i] == '\r' ? (i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : "\r") : "\n";
    }
}
