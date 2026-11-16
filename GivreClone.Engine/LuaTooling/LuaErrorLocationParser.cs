using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GivreClone.Engine.LuaTooling;

/// <summary>Emplacement d'une erreur MoonSharp. Line 1-based ; colonnes 0-based comme MoonSharp (fin EXCLUSIVE).</summary>
public sealed record LuaErrorLocation(string Chunk, int Line, int Column, int? EndLine, int? EndColumn, string Message);

/// <summary>
/// Lecture des messages décorés MoonSharp (« chunk:(L,C-C2): msg », « chunk:(L1,C1-L2,C2): msg », « chunk:(L,C): msg »,
/// « chunk:L: msg ») et aide au diagnostic : nom de la fonction appelée à l'emplacement, explication en français.
/// Constat MoonSharp 2.0 : pour un appel, la plage couvre souvent les ARGUMENTS « (…) » (ou toute l'instruction), pas le nom.
/// </summary>
public static class LuaErrorLocationParser
{
    /// <summary>
    /// Extrait l'emplacement d'un message décoré, même noyé dans une ligne plus longue (« erreur d'exécution :
    /// recolte.lua:(148,4-31): … »). Le nom de chunk peut contenir « : » (lib:utils, file:x). Retient le DERNIER groupe
    /// « (L,C…) » suivi de « : ».
    /// </summary>
    public static bool TryParse(string? decorated, out LuaErrorLocation? location)
    {
        location = null;
        if (string.IsNullOrEmpty(decorated)) return false;
        string s = decorated;

        int bestAt = -1, bl = 0, bc = 0, afterColon = 0;
        int? bel = null, bec = null;
        for (int p = s.IndexOf(":(", StringComparison.Ordinal); p >= 0; p = s.IndexOf(":(", p + 1, StringComparison.Ordinal))
        {
            if (TryParseGroup(s, p + 2, out int l1, out int c1, out int? el, out int? ec, out int after)
                && after < s.Length && s[after] == ':' && p > 0)
            {
                bestAt = p; bl = l1; bc = c1; bel = el; bec = ec; afterColon = after + 1;
            }
        }
        if (bestAt >= 0)
        {
            location = new LuaErrorLocation(ChunkBefore(s, bestAt), bl, bc, bel, bec, s[afterColon..].Trim());
            return true;
        }

        // Forme Lua standard « chunk:L: msg » (la dernière occurrence ; le chunk doit contenir une lettre : pas « 12:30: »).
        for (int p = s.LastIndexOf(':'); p > 0; p = s.LastIndexOf(':', p - 1))
        {
            int j = p + 1, l = 0, digits = 0;
            while (j < s.Length && char.IsAsciiDigit(s[j]) && digits < 9) { l = l * 10 + (s[j] - '0'); j++; digits++; }
            if (digits == 0 || j >= s.Length || s[j] != ':' || l <= 0) continue;
            string chunk = ChunkBefore(s, p);
            if (!chunk.Any(char.IsLetter)) continue;
            location = new LuaErrorLocation(chunk, l, 0, null, null, s[(j + 1)..].Trim());
            return true;
        }
        return false;
    }

    // "L,C)" | "L,C-C2)" | "L1,C1-L2,C2)" à partir de i ; after = index juste après ')'.
    private static bool TryParseGroup(string s, int i, out int l1, out int c1, out int? el, out int? ec, out int after)
    {
        l1 = c1 = 0; el = ec = null; after = i;
        if (!ReadInt(s, ref i, out l1) || i >= s.Length || s[i] != ',') return false;
        i++;
        if (!ReadInt(s, ref i, out c1) || i >= s.Length) return false;
        if (s[i] == '-')
        {
            i++;
            if (!ReadInt(s, ref i, out int x) || i >= s.Length) return false;
            if (s[i] == ',')
            {
                i++;
                if (!ReadInt(s, ref i, out int y) || i >= s.Length) return false;
                el = x; ec = y;
            }
            else { el = l1; ec = x; }
        }
        if (s[i] != ')') return false;
        after = i + 1;
        return l1 > 0;
    }

    private static bool ReadInt(string s, ref int i, out int v)
    {
        v = 0;
        int start = i;
        while (i < s.Length && char.IsAsciiDigit(s[i]) && i - start < 9) { v = v * 10 + (s[i] - '0'); i++; }
        return i > start;
    }

    // Nom du chunk devant « :( » : ce qui suit le dernier « : » + blanc (préfixe type « erreur d'exécution : », ou
    // emplacement externe d'une erreur relancée). Un nom de fichier ne contient jamais « : » + blanc ; il peut contenir
    // des espaces et des crochets (« [MZN] - Money.lua ») : gardés tels quels.
    private static string ChunkBefore(string s, int colonAt)
    {
        string prefix = s[..colonAt];
        int nl = prefix.LastIndexOfAny(['\n', '\r']);
        if (nl >= 0) prefix = prefix[(nl + 1)..];
        for (int k = prefix.Length - 2; k >= 0; k--)
            if (prefix[k] == ':' && char.IsWhiteSpace(prefix[k + 1])) { prefix = prefix[(k + 1)..]; break; }
        return prefix.Trim().Trim('«', '»', '"', '\'').Trim();
    }

    /// <summary>
    /// Pour « attempt to call a nil value » : meilleur candidat pour le nom de la fonction appelée (getJobLevel,
    /// map:getPods, a.b.c) parmi les appels dont l'argument ( '(' / chaîne / table ) commence dans la plage. Les fonctions
    /// Lua de base (print, pairs…) et celles que le script définit passent après les autres. null si aucun appel.
    /// </summary>
    public static string? CalleeNameAt(string source, LuaErrorLocation loc) => CalleeNameAt(source, loc, null);

    /// <summary>Variante qui écarte en priorité les noms connus de <paramref name="names"/> (API + Lua) : le nom inconnu
    /// est le plus probablement celui qui vaut nil.</summary>
    public static string? CalleeNameAt(string source, LuaErrorLocation loc, ILuaNameSet? names)
    {
        if (string.IsNullOrEmpty(source) || loc is null) return null;
        try
        {
            var starts = LuaText.LineStarts(source);
            int from = LuaText.OffsetOf(source, starts, loc.Line, loc.Column);
            int to;
            if (loc.EndLine is int el && loc.EndColumn is int ec) to = LuaText.OffsetOf(source, starts, el, ec);
            else to = LuaText.LineEnd(source, starts[Math.Clamp(loc.Line - 1, 0, starts.Count - 1)]);
            if (to <= from) to = Math.Min(source.Length, from + 1);

            var toks = LuaLexer.TokenizeList(source);
            HashSet<string>? declaredFns = null;
            var cands = new List<(string Name, int Opener, bool Known)>();
            for (int i = 1; i < toks.Count; i++)
            {
                var t = toks[i];
                if (t.Start < from) continue;
                if (t.Start >= to) break;
                bool isArg = LuaText.IsPunct(source, t, '(') || LuaText.IsPunct(source, t, '{') || t.Kind == LuaTokenKind.String;
                if (!isArg || toks[i - 1].Kind != LuaTokenKind.Identifier) continue;
                string? chain = ChainEndingAt(source, toks, i - 1, out int chainStartIdx, out bool complete);
                if (chain is null) continue;
                // définition « function nom( » : pas un appel
                if (chainStartIdx > 0 && LuaText.IsKw(source, toks[chainStartIdx - 1], "function")) continue;
                declaredFns ??= DeclaredFunctionNames(source, toks);
                bool known = complete && IsLikelyKnown(chain, names, declaredFns);
                cands.Add((chain, t.Start, known));
            }
            if (cands.Count == 0) return null;
            string? pick = Pick(cands, from, onlyUnknown: true) ?? Pick(cands, from, onlyUnknown: false);
            return pick;
        }
        catch { return null; }
    }

    private static string? Pick(List<(string Name, int Opener, bool Known)> cands, int from, bool onlyUnknown)
    {
        (string Name, int Opener, bool Known)? last = null;
        foreach (var c in cands)
        {
            if (onlyUnknown && c.Known) continue;
            if (c.Opener == from) return c.Name;   // la plage commence sur l'argument de CET appel
            last = c;
        }
        return last?.Name;   // sinon l'appel le plus intérieur / le plus à droite
    }

    private static bool IsLikelyKnown(string chain, ILuaNameSet? names, HashSet<string> declaredFns)
    {
        int sep = chain.LastIndexOfAny(['.', ':']);
        if (sep < 0)
            return declaredFns.Contains(chain) || LuaApiCatalog.IsLuaBaseGlobal(chain) || (names?.IsKnownGlobal(chain) ?? false);
        string root = chain[..sep], member = chain[(sep + 1)..];
        if (declaredFns.Contains(chain)) return true;
        if (names is not null && root.IndexOfAny(['.', ':']) < 0 && names.IsNamespace(root) && names.IsKnownMember(root, member)) return true;
        return LuaApiCatalog.IsLuaStdTable(root) && root.IndexOfAny(['.', ':']) < 0 && (names?.IsKnownMember(root, member) ?? true);
    }

    // Noms de fonctions définies par le script (function f / local function f / f = function / function a.b / a:b).
    private static HashSet<string> DeclaredFunctionNames(string text, List<LuaToken> toks)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i + 1 < toks.Count; i++)
        {
            if (LuaText.IsKw(text, toks[i], "function") && toks[i + 1].Kind == LuaTokenKind.Identifier)
            {
                int j = i + 1;
                string name = LuaLexer.Slice(text, toks[j]);
                while (j + 2 < toks.Count && LuaText.IsMemberSep(text, toks[j + 1]) && toks[j + 2].Kind == LuaTokenKind.Identifier)
                {
                    name += text[toks[j + 1].Start] + LuaLexer.Slice(text, toks[j + 2]);
                    j += 2;
                }
                set.Add(name);
            }
            else if (toks[i].Kind == LuaTokenKind.Identifier && i + 2 < toks.Count && LuaText.IsOp(text, toks[i + 1], "=")
                     && LuaText.IsKw(text, toks[i + 2], "function") && !LuaText.AfterMemberSep(text, toks, i))
                set.Add(LuaLexer.Slice(text, toks[i]));
        }
        return set;
    }

    /// <summary>Chaîne d'identifiants (a.b:c) qui se termine au jeton <paramref name="lastIdx"/>. complete=false si elle
    /// part d'une expression (f().x, t[1]:m) : seul le suffixe nommé est rendu.</summary>
    internal static string? ChainEndingAt(string text, List<LuaToken> toks, int lastIdx, out int startIdx, out bool complete)
    {
        startIdx = lastIdx;
        complete = true;
        if (lastIdx < 0 || toks[lastIdx].Kind != LuaTokenKind.Identifier) return null;
        int j = lastIdx;
        while (j >= 2 && LuaText.IsMemberSep(text, toks[j - 1]) && toks[j - 2].Kind == LuaTokenKind.Identifier) j -= 2;
        if (j >= 1 && LuaText.IsMemberSep(text, toks[j - 1])) complete = false;   // précédé d'une expression
        startIdx = j;
        return text.Substring(toks[j].Start, toks[lastIdx].End - toks[j].Start);
    }

    /// <summary>
    /// Explication courte en français d'un message MoonSharp courant (« attempt to call a nil value » → « fonction
    /// inexistante (faute de frappe ? fonction pas encore définie ?) »…). null si le message n'est pas reconnu.
    /// </summary>
    public static string? FrenchHint(string moonSharpMessage)
    {
        if (string.IsNullOrWhiteSpace(moonSharpMessage)) return null;
        string m = moonSharpMessage;
        bool Has(string x) => m.Contains(x, StringComparison.OrdinalIgnoreCase);

        if (Has("attempt to call a nil value")) return "fonction inexistante (faute de frappe ? fonction pas encore définie ?)";
        if (Has("attempt to call a")) return "tu appelles comme une fonction une valeur qui n'en est pas une";
        if (Has("attempt to index a nil value")) return "lecture d'un champ (x.y ou x:y()) sur une valeur nil : la variable n'existe pas ou n'est pas encore remplie";
        if (Has("attempt to index a")) return "lecture d'un champ sur une valeur qui n'est pas une table";
        if (Has("attempt to perform arithmetic on a nil value")) return "calcul avec une valeur nil : une variable du calcul n'est pas définie (ou une fonction a renvoyé nil)";
        if (Has("attempt to perform arithmetic")) return "calcul sur une valeur qui n'est pas un nombre (utilise tonumber(...))";
        if (Has("attempt to concatenate a nil value")) return "concaténation (..) avec une valeur nil : vérifie la variable ou entoure-la de tostring(...)";
        if (Has("attempt to concatenate")) return "on ne concatène (..) que des chaînes et des nombres : utilise tostring(...)";
        if (Has("attempt to compare")) return "comparaison impossible entre ces deux valeurs (ex. nombre < nil, ou nombre < chaîne)";
        if (Has("attempt to get length of")) return "« # » sur une valeur qui n'est ni une table ni une chaîne (souvent nil)";
        if (Has("table index is nil")) return "clé de table nil (t[nil] = …)";
        if (Has("stack overflow")) return "récursion sans fin : une fonction s'appelle elle-même en boucle";

        int ee = m.IndexOf("' expected", StringComparison.OrdinalIgnoreCase);
        if (ee > 0)
        {
            int q = m.LastIndexOf('\'', ee - 1);
            string what = q >= 0 ? m[(q + 1)..ee] : "";
            string? closeInfo = ToClose(m);
            switch (what)
            {
                case "end": return closeInfo is null ? "il manque un « end » pour fermer un bloc (function, if, for, while, do)" : $"il manque un « end » pour fermer {closeInfo}";
                case "until": return closeInfo is null ? "il manque « until » pour fermer le « repeat »" : $"il manque « until » pour fermer {closeInfo}";
                case "then": return "il manque « then » après la condition du if / elseif";
                case "do": return "il manque « do » après l'en-tête du for / while";
                case "=": return "« = » attendu : instruction incomplète (ex. « x + 1 » seul sur une ligne, ou « += » qui n'existe pas en Lua)";
                case ")": return "parenthèse fermante « ) » manquante";
                case "}": return "accolade fermante « } » manquante";
                case "]": return "crochet fermant « ] » manquant";
                case "<name>": return "nom attendu : un mot-clé Lua (goto, end, function…) ne peut pas servir de nom";
                case "<eof>": return "« end » (ou « until ») en trop : rien à fermer à cet endroit";
            }
        }
        if (Has("<eof> expected")) return "« end » (ou « until ») en trop : rien à fermer à cet endroit";
        if (Has("unfinished long string")) return "chaîne longue [[ … ]] jamais fermée";
        if (Has("unfinished long comment")) return "commentaire long --[[ … ]] jamais fermé";
        if (Has("unfinished string")) return "chaîne non terminée : il manque le guillemet fermant";
        if (Has("invalid escape sequence")) return "séquence d'échappement invalide dans une chaîne (pour un « \\ » littéral, écris « \\\\ »)";
        if (Has("malformed number")) return "nombre mal écrit";
        if (Has("no visible label")) return "« goto » vers une étiquette ::nom:: introuvable dans ce bloc";
        if (Has("unexpected symbol near '<eof>'") || (Has("near '<eof>'") && Has("unexpected")))
            return "fin du fichier atteinte trop tôt : une expression ou un bloc n'est pas terminé";
        if (Has("unexpected symbol near")) return "symbole inattendu : vérifie la ligne (parenthèse, virgule ou opérateur en trop / manquant, mot-clé mal placé…)";
        if (Has("<eof>")) return "fin du fichier atteinte trop tôt : un bloc (function, if, for…) n'est sans doute pas fermé par « end »";
        if (Has("bad argument #"))
        {
            int h = m.IndexOf("bad argument #", StringComparison.OrdinalIgnoreCase) + 14;
            int j = h;
            while (j < m.Length && char.IsAsciiDigit(m[j])) j++;
            string n = m[h..j];
            string fn = "";
            int to = m.IndexOf(" to '", j, StringComparison.OrdinalIgnoreCase);
            if (to >= 0)
            {
                int q2 = m.IndexOf('\'', to + 5);
                if (q2 > to + 5) fn = m[(to + 5)..q2];
            }
            string who = fn.Length > 0 ? $" passé à « {fn} »" : "";
            return n.Length > 0 ? $"mauvais argument n°{n}{who} (type ou valeur inattendu)" : $"mauvais argument{who} (type ou valeur inattendu)";
        }
        return null;
    }

    // « (to close 'function' at line 12) » -> « le « function » ouvert ligne 12 ».
    private static string? ToClose(string m)
    {
        int i = m.IndexOf("to close '", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        int a = i + 10, b = m.IndexOf('\'', a);
        if (b <= a) return null;
        string kw = m[a..b];
        int li = m.IndexOf("at line ", b, StringComparison.OrdinalIgnoreCase);
        if (li < 0) return $"le « {kw} »";
        int j = li + 8, k = j;
        while (k < m.Length && char.IsAsciiDigit(m[k])) k++;
        return k > j && int.TryParse(m.AsSpan(j, k - j), NumberStyles.None, CultureInfo.InvariantCulture, out int line)
            ? $"le « {kw} » ouvert ligne {line}"
            : $"le « {kw} »";
    }
}
