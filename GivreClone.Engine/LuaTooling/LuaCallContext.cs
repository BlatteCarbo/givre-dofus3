using System;
using System.Collections.Generic;

namespace GivreClone.Engine.LuaTooling;

/// <summary>
/// Contexte au curseur pour l'éditeur : appel en cours (aide de signature), mot en cours de frappe (complétion),
/// symbole survolé, et noms déclarés par le script. Chaînes et commentaires ignorés ; ne lève jamais.
/// </summary>
public static class LuaCallContext
{
    /// <summary>
    /// Appel ouvert le plus intérieur contenant le curseur : appelé (ns?, nom), index 0-based de l'argument (virgules au
    /// niveau de sa parenthèse) et offset de « ( ». null hors d'un appel (ou dans un commentaire). Une parenthèse de
    /// groupement « (a + b) » est traversée vers l'appel englobant ; une définition « function f(a, b) » n'est pas un appel.
    /// Namespace : « map » pour map:getPods, « string » pour string.format, « a.b » pour a.b.c.
    /// </summary>
    public static (string? Namespace, string Name, int ArgIndex, int OpenParenOffset)? At(string text, int caretOffset)
    {
        if (string.IsNullOrEmpty(text)) return null;
        try
        {
            int caret = Math.Clamp(caretOffset, 0, text.Length);
            var toks = LuaLexer.TokenizeList(text);
            var frames = new List<(char Kind, int Tok, int Args)>();
            for (int i = 0; i < toks.Count; i++)
            {
                var t = toks[i];
                if (t.Start >= caret) break;
                if (t.End > caret || (t.End == caret && (t.Unterminated || (t.Kind == LuaTokenKind.Comment && !LuaLexer.IsLongComment(text, t)))))
                {
                    // Curseur DANS ce jeton : dans un commentaire -> rien ; dans une chaîne -> argument en cours.
                    if (t.Kind == LuaTokenKind.Comment) return null;
                    break;
                }
                if (t.Kind != LuaTokenKind.Punctuation || t.Length != 1) continue;
                char c = text[t.Start];
                switch (c)
                {
                    case '(': case '{': case '[': frames.Add((c, i, 0)); break;
                    case ')': PopTo(frames, '('); break;
                    case '}': PopTo(frames, '{'); break;
                    case ']': PopTo(frames, '['); break;
                    case ',':
                        if (frames.Count > 0 && frames[^1].Kind == '(') frames[^1] = (frames[^1].Kind, frames[^1].Tok, frames[^1].Args + 1);
                        break;
                }
            }
            for (int f = frames.Count - 1; f >= 0; f--)
            {
                if (frames[f].Kind != '(') continue;
                int p = frames[f].Tok;
                if (p == 0) continue;
                var prev = toks[p - 1];
                if (prev.Kind != LuaTokenKind.Identifier)
                {
                    if (prev.Kind == LuaTokenKind.Keyword && LuaText.Is(text, prev, "function")) return null;   // function(a, b|
                    if (prev.Kind is LuaTokenKind.Punctuation && prev.Length == 1 && text[prev.Start] is ')' or ']' or '}') return null;   // f()(|
                    if (prev.Kind == LuaTokenKind.String) return null;
                    continue;   // parenthèse de groupement : appel englobant
                }
                string? chain = LuaErrorLocationParser.ChainEndingAt(text, toks, p - 1, out int startIdx, out _);
                if (chain is null) return null;
                if (startIdx > 0 && LuaText.IsKw(text, toks[startIdx - 1], "function")) return null;   // définition
                var (ns, name) = SplitChain(chain);
                return (ns, name, frames[f].Args, toks[p].Start);
            }
            return null;
        }
        catch { return null; }
    }

    private static void PopTo(List<(char Kind, int Tok, int Args)> frames, char open)
    {
        for (int j = frames.Count - 1; j >= 0; j--)
            if (frames[j].Kind == open) { frames.RemoveRange(j, frames.Count - j); return; }
    }

    /// <summary>
    /// Mot en cours de frappe et son qualificatif : « map:getP| » -&gt; ("map", ':', "getP", début) ; « getPo| » -&gt;
    /// (null, ' ', "getPo", début) ; « map:| » -&gt; ("map", ':', "", curseur). null dans une chaîne / un commentaire,
    /// dans un nombre, après « local » / « for » / « goto » (nom qu'on invente), ou après un qualificatif non nommé (f():x).
    /// </summary>
    public static (string? Qualifier, char Separator, string Prefix, int PrefixStart)? CompletionAt(string text, int caretOffset)
    {
        text ??= "";
        try
        {
            int caret = Math.Clamp(caretOffset, 0, text.Length);
            if (!LuaLexer.IsInCodeAt(text, caret)) return null;
            int ps = caret;
            while (ps > 0 && LuaLexer.IsIdentPart(text[ps - 1])) ps--;
            string prefix = text[ps..caret];
            if (prefix.Length > 0 && !LuaLexer.IsIdentStart(prefix[0])) return null;   // 12|, 3e|

            if (ps >= 2 && text[ps - 1] == ':' && text[ps - 2] == ':') return null;   // étiquette ::nom::
            bool concat = ps >= 2 && text[ps - 1] == '.' && text[ps - 2] == '.';      // « .. » : nom global après
            if (ps > 0 && text[ps - 1] is ':' or '.' && !concat)
            {
                char sep = text[ps - 1];
                int q = ps - 1;
                // Qualificatif : chaîne d'identifiants a.b.c juste avant le séparateur.
                int end = q, s = q;
                while (true)
                {
                    int e2 = s;
                    while (s > 0 && LuaLexer.IsIdentPart(text[s - 1])) s--;
                    if (s == e2 || !LuaLexer.IsIdentStart(text[s])) return null;
                    if (s >= 1 && text[s - 1] is '.' or ':')
                    {
                        if (s >= 2 && text[s - 2] == text[s - 1]) break;               // « .. » (concaténation) / « :: »
                        if (s >= 2 && LuaLexer.IsIdentPart(text[s - 2])) { s--; continue; }
                        return null;                                                   // f().x, t[1]:m, "abc":upper
                    }
                    break;
                }
                string qual = text[s..end];
                if (LuaLexer.IsKeyword(qual)) return null;
                return (qual, sep, prefix, ps);
            }

            // Nom qu'on est en train d'inventer : pas de complétion.
            int k = ps;
            while (k > 0 && (text[k - 1] == ' ' || text[k - 1] == '\t')) k--;
            int ws = k;
            while (ws > 0 && LuaLexer.IsIdentPart(text[ws - 1])) ws--;
            if (ws < k)
            {
                string prevWord = text[ws..k];
                if (prevWord is "local" or "for" or "goto") return null;
                if (prevWord == "function")
                {
                    int k2 = ws;
                    while (k2 > 0 && (text[k2 - 1] == ' ' || text[k2 - 1] == '\t')) k2--;
                    int w2 = k2;
                    while (w2 > 0 && LuaLexer.IsIdentPart(text[w2 - 1])) w2--;
                    if (w2 < k2 && text[w2..k2] == "local") return null;
                }
            }
            return (null, ' ', prefix, ps);
        }
        catch { return null; }
    }

    /// <summary>
    /// Symbole sous l'offset (survol) : « map:getPods » -&gt; ("map", "getPods", début de map, fin de getPods) ;
    /// « getPods » -&gt; (null, …) ; sur « map » dans map:getPods -&gt; (null, "map", …). null hors identifiant.
    /// </summary>
    public static (string? Namespace, string Name, int Start, int End)? SymbolAt(string text, int offset)
    {
        if (string.IsNullOrEmpty(text)) return null;
        try
        {
            var toks = LuaLexer.TokenizeList(text);
            int idx = -1;
            for (int i = 0; i < toks.Count; i++)
            {
                var t = toks[i];
                if (t.Start > offset) break;
                if (offset < t.End) { idx = i; break; }
                if (offset == t.End && t.Kind == LuaTokenKind.Identifier) idx = i;   // juste après le mot
            }
            if (idx < 0 || toks[idx].Kind != LuaTokenKind.Identifier) return null;
            string? chain = LuaErrorLocationParser.ChainEndingAt(text, toks, idx, out int startIdx, out _);
            if (chain is null) return null;
            var (ns, name) = SplitChain(chain);
            return (ns, name, toks[startIdx].Start, toks[idx].End);
        }
        catch { return null; }
    }

    /// <summary>
    /// Noms déclarés par le script (complétion des noms utilisateur) : fonctions (globales et locales), locales, globals
    /// assignés au niveau racine, paramètres (portée approximative = fichier), variables de boucle — avec la ligne de
    /// déclaration et, pour une fonction, ses paramètres (« a, b, ... »). Un nom = une entrée (première déclaration).
    /// </summary>
    public static IReadOnlyList<(string Name, bool IsFunction, int Line, string? Args)> Declarations(string text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<(string, bool, int, string?)>();
        try { return LuaScriptScan.Run(text, LuaLexer.TokenizeList(text)).Decls; }
        catch { return Array.Empty<(string, bool, int, string?)>(); }
    }

    private static (string? Ns, string Name) SplitChain(string chain)
    {
        int sep = chain.LastIndexOfAny(['.', ':']);
        return sep < 0 ? (null, chain) : (chain[..sep], chain[(sep + 1)..]);
    }
}
