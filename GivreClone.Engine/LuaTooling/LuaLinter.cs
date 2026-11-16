using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.Engine.LuaTooling;

/// <summary>Gravité d'un diagnostic.</summary>
public enum LuaDiagnosticSeverity { Error, Warning, Info }

/// <summary>Diagnostic : lignes 1-based, colonnes 0-based UTF-16, fin EXCLUSIVE (même ligne possible).</summary>
public sealed record LuaDiagnostic(int Line, int Column, int EndLine, int EndColumn, LuaDiagnosticSeverity Severity, string Code, string Message, string? Hint);

/// <summary>Noms connus pour l'analyse (implémenté par <see cref="LuaApiCatalog"/>).</summary>
public interface ILuaNameSet
{
    /// <summary>Global connu (casse exacte) : verbes et callbacks de l'API, constantes, modules, bibliothèque standard.</summary>
    bool IsKnownGlobal(string name);
    /// <summary>Tous les noms globaux connus (suggestions).</summary>
    IEnumerable<string> AllGlobalNames { get; }
    /// <summary>Module de l'API Givre (« map », « inventory »…) — pas les tables standard.</summary>
    bool IsNamespace(string name);
    /// <summary>Membre documenté, ou nom tolérant (casse, 1re lettre, snake_case).</summary>
    bool IsKnownMember(string ns, string member);
    IEnumerable<string> MembersOf(string ns);
    /// <summary>Fonction appelée par le moteur si le script la définit (move, bank, onTurn…).</summary>
    bool IsCallback(string name);
    /// <summary>Raison pour laquelle <c>ns:member</c> est INDISPONIBLE (l'appel lève une erreur Lua, aucune action), ou null.</summary>
    string? UnavailableReason(string ns, string member) => null;
}

/// <summary>
/// Détections heuristiques rapides (≈ 10 000 lignes en quelques ms), à très faible taux de faux positifs : un code Lua
/// valide pour MoonSharp n'est jamais signalé en Error. Codes : lua.* (syntaxe / habitudes d'autres langages),
/// givre.* (API Givre). Diagnostics triés par (ligne, colonne).
/// </summary>
public static class LuaLinter
{
    /// <summary>Fonctions ABSENTES des scripts (voir SandboxCheck) : globaux, fonctions de os et de io qui, elles, existent.</summary>
    public static readonly string[] SandboxMissingGlobals = { "loadfile" };
    public static readonly string[] SandboxOsAvailable = { "time", "clock", "date", "difftime" };
    public static readonly string[] SandboxIoAvailable = { "open" };
    public static readonly string[] SandboxPackageAvailable = { "loaded" };

    public static IReadOnlyList<LuaDiagnostic> Lint(string text, ILuaNameSet names)
    {
        var diags = new List<LuaDiagnostic>();
        if (string.IsNullOrWhiteSpace(text)) return diags;
        try { new Run(text, names ?? LuaApiCatalog.Empty, diags).Execute(); }
        catch { /* analyse au mieux : ce qui a été trouvé */ }
        diags.Sort(static (a, b) => a.Line != b.Line ? a.Line.CompareTo(b.Line)
                                  : a.Column != b.Column ? a.Column.CompareTo(b.Column)
                                  : a.Severity.CompareTo(b.Severity));
        return diags;
    }

    private sealed class Run
    {
        private readonly string _text;
        private readonly ILuaNameSet _names;
        private readonly List<LuaDiagnostic> _diags;
        private readonly List<LuaToken> _toks;
        private readonly List<int> _starts;
        private readonly bool[] _suppressed;
        private LuaScriptScan _scan = null!;
        private readonly Dictionary<string, string?> _globalSuggestCache = new(StringComparer.Ordinal);
        private Dictionary<string, string>? _callbacksLower;
        private List<string>? _suggestNames;

        public Run(string text, ILuaNameSet names, List<LuaDiagnostic> diags)
        {
            _text = text;
            _names = names;
            _diags = diags;
            _toks = LuaLexer.TokenizeList(text);
            _starts = LuaText.LineStarts(text);
            _suppressed = new bool[_toks.Count];
        }

        public void Execute()
        {
            LexicalChecks();
            _scan = LuaScriptScan.Run(_text, _toks, _suppressed);
            BlockChecks();
            SemanticChecks();
            EntryCheck();
        }

        // ------------------------------------------------------------------ jetons

        private void LexicalChecks()
        {
            int n = _toks.Count;
            int lambdaClose = -1;
            for (int i = 0; i < n; i++)
            {
                if (_suppressed[i]) continue;
                var t = _toks[i];
                switch (t.Kind)
                {
                    case LuaTokenKind.String when t.Unterminated:
                        Add(t, LuaDiagnosticSeverity.Error, "lua.unfinished-string",
                            LuaLexer.IsLongString(_text, t) ? "chaîne longue [[ … ]] jamais fermée" : "chaîne non terminée : il manque le guillemet fermant",
                            LuaLexer.IsLongString(_text, t) ? "ferme-la avec ]] (même nombre de = qu'à l'ouverture)" : "ferme la chaîne sur la même ligne (ou utilise [[ … ]] pour du texte sur plusieurs lignes)",
                            firstLineOnly: true);
                        break;
                    case LuaTokenKind.Comment when t.Unterminated:
                        Add(t, LuaDiagnosticSeverity.Error, "lua.unfinished-comment", "commentaire long --[[ … ]] jamais fermé",
                            "ferme-le avec ]] (même nombre de = qu'à l'ouverture)", firstLineOnly: true);
                        break;
                    case LuaTokenKind.Operator:
                        OperatorCheck(i);
                        break;
                    case LuaTokenKind.Unknown:
                        UnknownCheck(i, ref lambdaClose);
                        break;
                    case LuaTokenKind.Keyword when i > 0 && LuaText.IsMemberSep(_text, _toks[i - 1]):
                        KeywordMemberCheck(i);
                        break;
                }
            }
        }

        private void OperatorCheck(int i)
        {
            var t = _toks[i];
            if (t.Length == 1) return;
            string op = LuaLexer.Slice(_text, t);
            switch (op)
            {
                case "!=":
                    // Accepté par MoonSharp (alias de ~=) : avertissement de portabilité seulement.
                    Add(t, LuaDiagnosticSeverity.Warning, "lua.neq", "« != » n'est pas du Lua standard (MoonSharp l'accepte)",
                        "en Lua, « différent de » s'écrit ~=");
                    break;
                case "&&":
                    Add(t, LuaDiagnosticSeverity.Error, "lua.logic", "« && » n'existe pas en Lua", "utilise « and »");
                    break;
                case "||":
                    // MoonSharp : « || expr » = lambda sans paramètre (valide) ; erreur seulement après une expression.
                    if (i > 0 && EndsExpression(i - 1))
                        Add(t, LuaDiagnosticSeverity.Error, "lua.logic", "« || » n'existe pas en Lua", "utilise « or »");
                    break;
                case "+=": case "-=": case "*=": case "/=":
                {
                    string lhs = LhsText(i) ?? "x";
                    Add(t, LuaDiagnosticSeverity.Error, "lua.compound", $"« {op} » n'existe pas en Lua", $"écris « {lhs} = {lhs} {op[0]} … »");
                    break;
                }
                case "++":
                {
                    string lhs = LhsText(i) ?? "x";
                    Add(t, LuaDiagnosticSeverity.Error, "lua.compound", "« ++ » n'existe pas en Lua", $"écris « {lhs} = {lhs} + 1 »");
                    break;
                }
                case "//":
                    if (LuaText.IsFirstOnLine(_text, t))
                        Add(t, LuaDiagnosticSeverity.Error, "lua.cstyle-comment", "« // » ne commence pas un commentaire en Lua",
                            "les commentaires commencent par « -- » (sur plusieurs lignes : --[[ … ]])");
                    else
                        Add(t, LuaDiagnosticSeverity.Error, "lua.floordiv", "« // » n'existe pas en Lua 5.2",
                            "commentaire : « -- » ; division entière : math.floor(a / b)");
                    SuppressRestOfLine(i);
                    break;
            }
        }

        private void UnknownCheck(int i, ref int lambdaClose)
        {
            var t = _toks[i];
            char c = _text[t.Start];
            switch (c)
            {
                case '|':
                    // Lambda MoonSharp |a, b| expr : '|' ouvrant en début d'expression, le suivant ferme.
                    if (i == lambdaClose) return;
                    if (i == 0 || !EndsExpression(i - 1))
                    {
                        for (int j = i + 1; j < _toks.Count && j < i + 64; j++)
                            if (_toks[j].Kind == LuaTokenKind.Unknown && _text[_toks[j].Start] == '|') { lambdaClose = j; break; }
                        return;
                    }
                    Add(t, LuaDiagnosticSeverity.Error, "lua.bitwise", "« | » n'existe pas en Lua 5.2", "utilise bit32.bor(a, b) (ou « or » pour un OU logique)");
                    return;
                case '&':
                    Add(t, LuaDiagnosticSeverity.Error, "lua.bitwise", "« & » n'existe pas en Lua 5.2", "utilise bit32.band(a, b) (ou « and » pour un ET logique)");
                    return;
                case '~':
                    Add(t, LuaDiagnosticSeverity.Error, "lua.bitwise", "« ~ » seul n'existe pas en Lua 5.2", "« différent de » s'écrit ~= ; pour les bits : bit32.bxor / bit32.bnot");
                    return;
                case '!':
                    Add(t, LuaDiagnosticSeverity.Error, "lua.logic", "« ! » n'existe pas en Lua", "utilise « not » (et « ~= » pour « différent de »)");
                    return;
            }
            if (c == '\\')
            {
                Add(t, LuaDiagnosticSeverity.Error, "lua.unknown-char", "« \\ » en dehors d'une chaîne", "les échappements (\\n, \\\") ne s'écrivent que dans une chaîne");
                return;
            }
            Add(t, LuaDiagnosticSeverity.Error, "lua.unknown-char", $"caractère « {LuaLexer.Slice(_text, t)} » inattendu",
                "hors d'une chaîne ou d'un commentaire, ce caractère n'est pas du Lua");
        }

        private void KeywordMemberCheck(int i)
        {
            var t = _toks[i];
            string kw = LuaLexer.Slice(_text, t);
            char sep = _text[_toks[i - 1].Start];
            if (kw == "goto" && i >= 2 && _toks[i - 2].Kind == LuaTokenKind.Identifier)
            {
                string owner = LuaLexer.Slice(_text, _toks[i - 2]);
                if (owner is "zaap" or "zaapi")
                {
                    Add(_toks[i - 2].Start, t.End, LuaDiagnosticSeverity.Error, "givre.goto-member", $"goto est un mot-clé Lua : utilise {owner}:go(...)",
                        $"{owner}{sep}goto(x) est une erreur de syntaxe ; {owner}:go(x) fait le même trajet");
                    return;
                }
            }
            Add(t, LuaDiagnosticSeverity.Error, "lua.keyword-member", $"« {kw} » est un mot-clé Lua : il ne peut pas servir de nom après « {sep} »",
                $"écris t[\"{kw}\"] pour un champ de ce nom");
        }

        // Le jeton d'index i termine-t-il une expression (après lui, un opérateur binaire est attendu) ?
        private bool EndsExpression(int i)
        {
            var t = _toks[i];
            switch (t.Kind)
            {
                case LuaTokenKind.Identifier: case LuaTokenKind.Number: case LuaTokenKind.String: return true;
                case LuaTokenKind.Keyword: return LuaText.Is(_text, t, "nil") || LuaText.Is(_text, t, "true") || LuaText.Is(_text, t, "false") || LuaText.Is(_text, t, "end");
                case LuaTokenKind.Operator: return LuaText.Is(_text, t, "...");
                case LuaTokenKind.Punctuation: return t.Length == 1 && _text[t.Start] is ')' or ']' or '}';
                default: return false;
            }
        }

        private string? LhsText(int opIdx)
        {
            if (opIdx == 0) return null;
            var prev = _toks[opIdx - 1];
            if (prev.Kind == LuaTokenKind.Identifier)
                return LuaErrorLocationParser.ChainEndingAt(_text, _toks, opIdx - 1, out _, out _);
            if (LuaText.IsPunct(_text, prev, ']'))
            {
                // t[i] += 1
                int depth = 0;
                for (int j = opIdx - 1; j >= 0 && j >= opIdx - 40; j--)
                {
                    if (LuaText.IsPunct(_text, _toks[j], ']')) depth++;
                    else if (LuaText.IsPunct(_text, _toks[j], '[') && --depth == 0)
                    {
                        if (j > 0 && _toks[j - 1].Kind == LuaTokenKind.Identifier && _toks[j - 1].Line == prev.Line)
                            return _text.Substring(_toks[j - 1].Start, prev.End - _toks[j - 1].Start);
                        return null;
                    }
                }
            }
            return null;
        }

        private void SuppressRestOfLine(int i)
        {
            int line = _toks[i].Line;
            for (int j = i + 1; j < _toks.Count && _toks[j].Line == line; j++) _suppressed[j] = true;
        }

        // ------------------------------------------------------------------ blocs

        private enum Bk : byte { Function, If, Loop, Do, Repeat }

        private struct Blk
        {
            public Bk Kind;
            public int Tok;
            public int Indent;
            public bool AwaitDo, AwaitThen;
        }

        private void BlockChecks()
        {
            var plain = BlockPass(heuristic: false);
            if (plain.Count == 0) return;
            var guided = BlockPass(heuristic: true);
            _diags.AddRange(guided.Count > 0 && guided.Count <= plain.Count ? guided : plain);
        }

        // Structure des blocs ; heuristic = localisation guidée par l'indentation (seulement si le fichier est déséquilibré).
        private List<LuaDiagnostic> BlockPass(bool heuristic)
        {
            var res = new List<LuaDiagnostic>();
            var stack = new List<Blk>();
            for (int i = 0; i < _toks.Count; i++)
            {
                var t = _toks[i];
                if (t.Kind != LuaTokenKind.Keyword || _suppressed[i] || LuaText.AfterMemberSep(_text, _toks, i)) continue;
                switch (t.Length)
                {
                    case 8 when LuaText.Is(_text, t, "function"): Push(stack, Bk.Function, i); break;
                    case 2 when LuaText.Is(_text, t, "if"): Push(stack, Bk.If, i, awaitThen: true); break;
                    case 3 when LuaText.Is(_text, t, "for"):
                    case 5 when LuaText.Is(_text, t, "while"):
                        Push(stack, Bk.Loop, i, awaitDo: true); break;
                    case 6 when LuaText.Is(_text, t, "repeat"): Push(stack, Bk.Repeat, i); break;
                    case 2 when LuaText.Is(_text, t, "do"):
                        if (stack.Count > 0 && stack[^1].Kind == Bk.Loop && stack[^1].AwaitDo) { var b = stack[^1]; b.AwaitDo = false; stack[^1] = b; }
                        else Push(stack, Bk.Do, i);
                        break;
                    case 4 when LuaText.Is(_text, t, "then"):
                        if (stack.Count > 0 && stack[^1].Kind == Bk.If && stack[^1].AwaitThen) { var b = stack[^1]; b.AwaitThen = false; stack[^1] = b; }
                        break;
                    case 6 when LuaText.Is(_text, t, "elseif"):
                        if (stack.Count > 0 && stack[^1].Kind == Bk.If)
                        {
                            var b = stack[^1];
                            if (b.AwaitThen) MissingThen(res, b);
                            b.AwaitThen = true; stack[^1] = b;
                        }
                        break;
                    case 4 when LuaText.Is(_text, t, "else"):
                        if (stack.Count > 0 && stack[^1].Kind == Bk.If && stack[^1].AwaitThen)
                        {
                            var b = stack[^1]; MissingThen(res, b); b.AwaitThen = false; stack[^1] = b;
                        }
                        break;
                    case 3 when LuaText.Is(_text, t, "end"):
                        CloseEnd(res, stack, i, heuristic);
                        break;
                    case 5 when LuaText.Is(_text, t, "until"):
                    {
                        int r = stack.FindLastIndex(b => b.Kind == Bk.Repeat);
                        if (r < 0)
                        {
                            AddTo(res, t.Start, t.End, LuaDiagnosticSeverity.Error, "lua.extra-end", "« until » sans « repeat » à fermer",
                                "supprime-le, ou vérifie la boucle repeat au-dessus");
                            break;
                        }
                        for (int j = stack.Count - 1; j > r; j--) Unclosed(res, stack[j]);
                        stack.RemoveRange(r, stack.Count - r);
                        break;
                    }
                }
            }
            for (int j = 0; j < stack.Count; j++) Unclosed(res, stack[j]);
            return res;
        }

        private void CloseEnd(List<LuaDiagnostic> res, List<Blk> stack, int i, bool heuristic)
        {
            var t = _toks[i];
            if (stack.Count > 0 && stack[^1].Kind == Bk.Repeat)
            {
                // « end » au lieu de « until » (seul bloc ouvert, ou même indentation que le repeat) : une seule erreur.
                var r = stack[^1];
                bool wrongCloser = stack.Count == 1
                                   || (heuristic && LuaText.IsFirstOnLine(_text, t) && LuaText.IndentWidth(_text, _starts[t.Line - 1]) == r.Indent);
                if (wrongCloser)
                {
                    stack.RemoveAt(stack.Count - 1);
                    var rt = _toks[r.Tok];
                    AddTo(res, t.Start, t.End, LuaDiagnosticSeverity.Error, "lua.unclosed-block",
                        $"« repeat » ouvert ligne {rt.Line} fermé par « end »", "une boucle repeat se termine par « until condition »");
                    return;
                }
                // Sinon le repeat est resté ouvert et ce end ferme le bloc d'en dessous.
                while (stack.Count > 0 && stack[^1].Kind == Bk.Repeat) { Unclosed(res, stack[^1]); stack.RemoveAt(stack.Count - 1); }
            }
            if (stack.Count == 0)
            {
                AddTo(res, t.Start, t.End, LuaDiagnosticSeverity.Error, "lua.extra-end", "« end » en trop : aucun bloc ouvert à fermer",
                    "supprime-le, ou vérifie les blocs au-dessus (un « end » mal placé ?)");
                return;
            }
            if (heuristic && LuaText.IsFirstOnLine(_text, t))
            {
                int endIndent = LuaText.IndentWidth(_text, _starts[t.Line - 1]);
                int top = stack.Count - 1;
                if (endIndent > stack[top].Indent)
                {
                    AddTo(res, t.Start, t.End, LuaDiagnosticSeverity.Error, "lua.extra-end", "« end » en trop : aucun bloc ouvert à fermer",
                        "supprime-le, ou vérifie les blocs au-dessus (un « end » mal placé ?)");
                    return;
                }
                if (endIndent < stack[top].Indent)
                {
                    int match = -1;
                    for (int j = top - 1; j >= 0; j--)
                        if (stack[j].Indent == endIndent && stack[j].Kind != Bk.Repeat) { match = j; break; }
                    if (match >= 0)
                    {
                        for (int j = top; j > match; j--) Unclosed(res, stack[j]);
                        stack.RemoveRange(match + 1, stack.Count - match - 1);
                    }
                }
            }
            var f = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            if (f.Kind == Bk.Loop && f.AwaitDo)
                AddTo(res, _toks[f.Tok].Start, _toks[f.Tok].End, LuaDiagnosticSeverity.Error, "lua.missing-do",
                    $"« {LuaLexer.Slice(_text, _toks[f.Tok])} » ligne {_toks[f.Tok].Line} : il manque « do »", "for … do … end / while … do … end");
            else if (f.Kind == Bk.If && f.AwaitThen) MissingThen(res, f);
        }

        private void Push(List<Blk> stack, Bk kind, int tok, bool awaitDo = false, bool awaitThen = false) =>
            stack.Add(new Blk { Kind = kind, Tok = tok, Indent = LuaText.IndentWidth(_text, _starts[_toks[tok].Line - 1]), AwaitDo = awaitDo, AwaitThen = awaitThen });

        private void Unclosed(List<LuaDiagnostic> res, in Blk b)
        {
            var t = _toks[b.Tok];
            string kw = LuaLexer.Slice(_text, t);
            if (b.Kind == Bk.Repeat)
                AddTo(res, t.Start, t.End, LuaDiagnosticSeverity.Error, "lua.unclosed-block", $"« repeat » ouvert ligne {t.Line} sans « until »",
                    "termine la boucle par « until condition »");
            else
                AddTo(res, t.Start, t.End, LuaDiagnosticSeverity.Error, "lua.unclosed-block", $"« {kw} » ouvert ligne {t.Line} sans « end »",
                    "ajoute « end » à la fin du bloc");
        }

        private void MissingThen(List<LuaDiagnostic> res, in Blk b)
        {
            var t = _toks[b.Tok];
            AddTo(res, t.Start, t.End, LuaDiagnosticSeverity.Error, "lua.missing-then", $"« if » ligne {t.Line} : il manque « then »",
                "if condition then … end");
        }

        // ------------------------------------------------------------------ noms

        private void SemanticChecks()
        {
            int n = _toks.Count;
            for (int i = 0; i < n; i++)
            {
                if (_suppressed[i]) continue;
                var t = _toks[i];
                if (t.Kind != LuaTokenKind.Identifier) continue;
                bool afterSep = i > 0 && LuaText.IsMemberSep(_text, _toks[i - 1]);
                if (afterSep) continue;
                bool nextIsSep = i + 2 < n && LuaText.IsMemberSep(_text, _toks[i + 1]) && _toks[i + 2].Kind == LuaTokenKind.Identifier;

                if (t.Length == 8 && LuaText.Is(_text, t, "continue")) { ContinueCheck(i); continue; }
                if (SandboxCheck(i, nextIsSep)) continue;
                if (nextIsSep) { MemberCheck(i); continue; }
                if (IsCallArg(i + 1, t.Line)) GlobalCallCheck(i);
            }
            CallbackChecks();
        }

        /// <summary>
        /// Fonctions Lua ABSENTES des scripts : le bac à sable MoonSharp (Preset_SoftSandbox) n'a ni io, ni package, ni
        /// loadfile, et un os réduit à l'heure ; l'hôte GivreClone ajoute seulement io.open (fichiers Road Creator),
        /// require, dofile, load et loadstring. Un script venu de l'ancien Givre qui utilisait le reste planterait à
        /// l'exécution (« attempt to call a nil value ») sans rien signaler avant.
        /// Vérifié contre le moteur ET le code de l'hôte par LuaLinterTests (Sandbox_*).
        /// </summary>

        private bool SandboxCheck(int i, bool nextIsSep)
        {
            var t = _toks[i];
            string name = LuaLexer.Slice(_text, t);
            if (IsDefinitionName(i) || _scan.Declared.Contains(name) || _scan.DynamicGlobals) return false;
            if (i > 0 && (LuaText.IsKw(_text, _toks[i - 1], "local") || LuaText.IsKw(_text, _toks[i - 1], "goto"))) return false;
            if (nextIsSep && (name == "io" || name == "package" || name == "os"))
            {
                string member = LuaLexer.Slice(_text, _toks[i + 2]);
                if (name == "os" && Array.IndexOf(SandboxOsAvailable, member) >= 0) return false;
                if (name == "io" && Array.IndexOf(SandboxIoAvailable, member) >= 0) return false;
                if (name == "package" && Array.IndexOf(SandboxPackageAvailable, member) >= 0) return false;
                Add(t.Start, _toks[i + 2].End, LuaDiagnosticSeverity.Warning, "lua.sandbox-unavailable",
                    $"« {name}.{member} » n'existe pas dans le bac à sable des scripts GivreClone : l'appel échouera",
                    name == "os" ? "seuls os.time, os.clock, os.date et os.difftime sont disponibles"
                  : name == "io" ? "seul io.open est disponible (fichiers Road Creator uniquement)"
                  : "un script n'a pas accès aux modules du système : seule l'API Givre est disponible");
                return true;
            }
            if (!nextIsSep && Array.IndexOf(SandboxMissingGlobals, name) >= 0 && IsCallArg(i + 1, t.Line))
            {
                Add(t, LuaDiagnosticSeverity.Warning, "lua.sandbox-unavailable",
                    $"« {name} » n'existe pas dans le bac à sable des scripts GivreClone : l'appel échouera",
                    "utilise dofile(...) ou require(...), fournis par GivreClone");
                return true;
            }
            return false;
        }

        private bool IsCallArg(int j, int line)
        {
            if (j >= _toks.Count || _suppressed[j] || _toks[j].Line != line) return false;
            var a = _toks[j];
            return a.Kind == LuaTokenKind.String || LuaText.IsPunct(_text, a, '(') || LuaText.IsPunct(_text, a, '{');
        }

        private bool IsDefinitionName(int i) => i > 0 && LuaText.IsKw(_text, _toks[i - 1], "function");

        private void GlobalCallCheck(int i)
        {
            if (IsDefinitionName(i)) return;
            if (i > 0 && (LuaText.IsKw(_text, _toks[i - 1], "goto") || LuaText.IsKw(_text, _toks[i - 1], "local"))) return;
            string name = LuaLexer.Slice(_text, _toks[i]);
            if (name == "self" || _scan.DynamicGlobals || _scan.Declared.Contains(name) || _names.IsKnownGlobal(name)) return;
            string? sugg = SuggestGlobal(name);
            var sev = _scan.HasExternalLoad ? LuaDiagnosticSeverity.Info : LuaDiagnosticSeverity.Warning;
            string? hint = sugg is not null ? $"Tu voulais dire « {sugg} » ?"
                         : _scan.HasExternalLoad ? "peut-être définie dans un fichier chargé par dofile / require" : null;
            Add(_toks[i], sev, "givre.unknown-global", $"« {name} » n'existe pas (ni dans l'API Givre, ni dans Lua, ni dans ton script)", hint);
        }

        private void MemberCheck(int i)
        {
            var t = _toks[i];
            string ns = LuaLexer.Slice(_text, t);
            if (ns == "self" || _scan.Declared.Contains(ns)) return;
            var sepTok = _toks[i + 1];
            var memTok = _toks[i + 2];
            char sep = _text[sepTok.Start];
            string member = LuaLexer.Slice(_text, memTok);
            if (_names.IsNamespace(ns))
            {
                if (IsDefinitionName(i) || _scan.DefinedMembers.Contains(LuaScriptScan.MemberKey(ns, member))) return;
                if (_names.IsKnownMember(ns, member))
                {
                    if (_names.UnavailableReason(ns, member) is { } why)
                        Add(t.Start, memTok.End, LuaDiagnosticSeverity.Warning, "givre.unavailable-api",
                            $"« {ns}{sep}{member} » n'est pas disponible dans GivreClone : l'appel lève une erreur Lua et n'effectue aucune action",
                            "entoure l'appel d'un pcall si le script prévoit une alternative — " + why);
                    return;
                }
                string? sugg = LuaNameMatching.Closest(member, _names.MembersOf(ns));
                Add(t.Start, memTok.End, sugg is not null ? LuaDiagnosticSeverity.Warning : LuaDiagnosticSeverity.Info, "givre.unknown-member",
                    $"« {ns}{sep}{member} » n'existe pas dans le module « {ns} »",
                    sugg is not null ? $"Tu voulais dire « {ns}{sep}{sugg} » ?" : "vérifie le nom dans la référence de l'API");
                return;
            }
            // Map:getId() : module mal orthographié (casse, faute de frappe) et jamais déclaré par le script.
            if (_scan.DynamicGlobals || _names.IsKnownGlobal(ns) || IsDefinitionName(i) || !IsCallArg(i + 3, memTok.Line)) return;
            string? nsSugg = SuggestNamespace(ns);
            if (nsSugg is null) return;
            Add(t, _scan.HasExternalLoad ? LuaDiagnosticSeverity.Info : LuaDiagnosticSeverity.Warning, "givre.unknown-global",
                $"« {ns} » n'existe pas (ni dans l'API Givre, ni dans Lua, ni dans ton script)", $"Tu voulais dire « {nsSugg} » ?");
        }

        private void ContinueCheck(int i)
        {
            if (_scan.Declared.Contains("continue")) return;
            if (i > 0)
            {
                var p = _toks[i - 1];
                bool boundary = p.Kind is LuaTokenKind.Identifier or LuaTokenKind.Number or LuaTokenKind.String
                                || (p.Kind == LuaTokenKind.Keyword && (LuaText.Is(_text, p, "then") || LuaText.Is(_text, p, "do") || LuaText.Is(_text, p, "else")
                                                                       || LuaText.Is(_text, p, "end") || LuaText.Is(_text, p, "repeat") || LuaText.Is(_text, p, "break")))
                                || (p.Kind == LuaTokenKind.Punctuation && p.Length == 1 && _text[p.Start] is ';' or ')' or ']' or '}');
                if (!boundary) return;
            }
            if (i + 1 < _toks.Count)
            {
                var nx = _toks[i + 1];
                bool stmtEnd = nx.Line > _toks[i].Line
                               || (nx.Kind == LuaTokenKind.Keyword && !LuaText.Is(_text, nx, "and") && !LuaText.Is(_text, nx, "or"))
                               || LuaText.IsPunct(_text, nx, ';');
                if (!stmtEnd) return;
            }
            Add(_toks[i], LuaDiagnosticSeverity.Error, "lua.continue", "« continue » n'existe pas en Lua",
                "remplace-le par « goto continue » avec « ::continue:: » juste avant le « end » de la boucle, et mets le corps de la boucle dans un bloc « do … end » (MoonSharp refuse un goto qui saute par-dessus un local) — ou tape le snippet « continue »");
        }

        private void CallbackChecks()
        {
            var lower = CallbacksLower();
            foreach (var fd in _scan.FuncDefs)
            {
                var t = _toks[fd.TokIdx];
                if (_names.IsCallback(fd.Name))
                {
                    if (fd.IsLocal && fd.TopLevel && !_scan.GlobalAssigned.Contains(fd.Name))
                        Add(t, LuaDiagnosticSeverity.Warning, "givre.local-callback", $"« {fd.Name} » est déclarée local : le moteur ne la verra pas",
                            $"retire « local » : function {fd.Name}(…) … end");
                    continue;
                }
                if (!fd.IsLocal && lower.TryGetValue(fd.Name.ToLowerInvariant(), out var exact))
                    Add(t, LuaDiagnosticSeverity.Warning, "givre.callback-case", $"le moteur appelle « {exact} » (respecte la casse)",
                        $"renomme en « {exact} »");
            }
        }

        private void EntryCheck()
        {
            if (_scan.TopLevelReturn) return;
            var lower = CallbacksLower();
            foreach (var fd in _scan.FuncDefs)
                if (_names.IsCallback(fd.Name) || lower.ContainsKey(fd.Name.ToLowerInvariant())) return;
            foreach (var g in _scan.GlobalAssigned)
                if (_names.IsCallback(g)) return;
            if (_toks.All(static t => t.Kind == LuaTokenKind.Comment)) return;
            int firstLineEnd = LuaText.LineEnd(_text, 0);
            Add(0, Math.Max(0, firstLineEnd), LuaDiagnosticSeverity.Info, "givre.no-entry", "aucun move() ni callback : le script ne pilotera rien",
                "définis function move() … end (ou un callback comme onTurn) ; ignore ce message pour une bibliothèque chargée par dofile / require");
        }

        // ------------------------------------------------------------------ suggestions

        private Dictionary<string, string> CallbacksLower()
        {
            if (_callbacksLower is not null) return _callbacksLower;
            _callbacksLower = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var g in _names.AllGlobalNames)
                if (_names.IsCallback(g)) _callbacksLower.TryAdd(g.ToLowerInvariant(), g);
            return _callbacksLower;
        }

        private string? SuggestGlobal(string name)
        {
            if (_globalSuggestCache.TryGetValue(name, out var s)) return s;
            _suggestNames ??= _names.AllGlobalNames.Concat(_scan.Declared).Where(static x => !LuaLexer.IsKeyword(x)).ToList();
            s = LuaNameMatching.Closest(name, _suggestNames);
            _globalSuggestCache[name] = s;
            return s;
        }

        private string? SuggestNamespace(string name)
        {
            var modules = _names.AllGlobalNames.Where(_names.IsNamespace).Concat(new[] { "string", "table", "math", "os", "coroutine", "bit32", "json" });
            return LuaNameMatching.Closest(name, modules);
        }

        // ------------------------------------------------------------------ émission

        private void Add(in LuaToken t, LuaDiagnosticSeverity sev, string code, string message, string? hint, bool firstLineOnly = false)
        {
            int end = t.End;
            if (firstLineOnly) end = Math.Min(end, LuaText.LineEnd(_text, t.Start));
            Add(t.Start, end, sev, code, message, hint);
        }

        private void Add(int start, int end, LuaDiagnosticSeverity sev, string code, string message, string? hint) =>
            AddTo(_diags, start, end, sev, code, message, hint);

        private void AddTo(List<LuaDiagnostic> list, int start, int end, LuaDiagnosticSeverity sev, string code, string message, string? hint)
        {
            var (l1, c1) = LuaText.LineCol(_starts, start);
            var (l2, c2) = LuaText.LineCol(_starts, Math.Max(start, end));
            list.Add(new LuaDiagnostic(l1, c1, l2, c2, sev, code, message, hint));
        }
    }
}
