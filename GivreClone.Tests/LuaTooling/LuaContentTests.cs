using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GivreClone.Engine.LuaTooling;
using Xunit;

namespace GivreClone.Tests.LuaTooling;

/// <summary>
/// Contenu de l'éditeur Lua : bibliothèque standard (LuaStdLib), modèles de scripts et extraits (LuaTemplates).
/// Garde-fous : identifiants uniques, textes FR, syntaxe des extraits, et SURTOUT chaque nom d'API utilisé dans un
/// modèle / extrait / exemple existe dans docs/portal/lua-api-doc.json ou dans LuaStdLib (ou est déclaré par le texte).
/// La compilation MoonSharp de chaque corps est vérifiée hors tests (sonde) : ici, équilibre des blocs et syntaxe Lua 5.2.
/// </summary>
public class LuaContentTests
{
    // =====================================================================================================
    // Documentation de l'API (lua-api-doc.json), trouvée en remontant depuis la sortie de test.
    // =====================================================================================================

    private sealed class ApiDoc
    {
        public HashSet<string> Globals { get; } = new(StringComparer.Ordinal);          // verbes + callbacks + config
        public HashSet<string> ConfigGlobals { get; } = new(StringComparer.Ordinal);    // MAX_PODS…
        public HashSet<string> Callbacks { get; } = new(StringComparer.Ordinal) { "move", "bank" };
        public Dictionary<string, HashSet<string>> Members { get; } = new(StringComparer.Ordinal);   // "map" -> {getId…}
    }

    private static readonly Lazy<ApiDoc> Doc = new(LoadDoc);

    private static string FindDocPath()
    {
        var dir = GivreClone.Tests.MappingScan.WalkUp(d => File.Exists(Path.Combine(d.FullName, "docs", "portal", "lua-api-doc.json")));
        if (dir is not null) return Path.Combine(dir.FullName, "docs", "portal", "lua-api-doc.json");
        throw new FileNotFoundException("docs/portal/lua-api-doc.json introuvable en remontant depuis " + AppContext.BaseDirectory);
    }

    private static ApiDoc LoadDoc()
    {
        var doc = new ApiDoc();
        using var json = JsonDocument.Parse(File.ReadAllText(FindDocPath()));
        foreach (var cat in json.RootElement.GetProperty("categories").EnumerateArray())
        {
            string catId = cat.GetProperty("id").GetString()!;
            if (cat.TryGetProperty("globals", out var globals))
                foreach (var g in globals.EnumerateArray())
                {
                    string name = g.GetProperty("name").GetString()!;
                    doc.Globals.Add(name);
                    if (catId == "config") doc.ConfigGlobals.Add(name);
                }
            if (!cat.TryGetProperty("methods", out var methods)) continue;
            foreach (var m in methods.EnumerateArray())
                foreach (var sig in m.GetProperty("sig").EnumerateArray())
                {
                    string name = sig.GetProperty("name").GetString()!;
                    string? ns = sig.TryGetProperty("ns", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                    if (string.IsNullOrEmpty(ns))
                    {
                        doc.Globals.Add(name);
                        if (catId == "callbacks") doc.Callbacks.Add(name);
                    }
                    else
                    {
                        string key = ns.TrimEnd(':', '.');
                        if (!doc.Members.TryGetValue(key, out var set)) doc.Members[key] = set = new(StringComparer.Ordinal);
                        set.Add(name);
                    }
                }
        }
        foreach (var cb in doc.Callbacks) doc.Globals.Add(cb);
        return doc;
    }

    private static HashSet<string> StdGlobals() =>
        LuaStdLib.Entries.Where(e => e.Namespace is null).Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

    private static Dictionary<string, HashSet<string>> StdMembers() =>
        LuaStdLib.Entries.Where(e => e.Namespace is not null)
            .GroupBy(e => e.Namespace!)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Name).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

    // Tous les corps vérifiés : modèles, extraits (défauts déployés), exemples de LuaStdLib.
    private static IEnumerable<(string Label, string Code)> AllBodies()
    {
        foreach (var t in LuaTemplates.Scripts) yield return ("modèle " + t.Id, t.Body);
        foreach (var s in LuaTemplates.Snippets) yield return ("extrait " + s.Trigger, LuaSnippetSyntax.DefaultText(s.Body));
        foreach (var e in LuaStdLib.Entries)
            if (e.Example is { } ex) yield return ("exemple " + e.Id, ex);
    }

    // =====================================================================================================
    // Mini-lexer Lua autonome (le lexer du composant éditeur n'est pas requis ici).
    // =====================================================================================================

    private enum TK { Name, Keyword, Number, String, Op }

    private readonly record struct Tok(TK Kind, string Text, int Line);

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if", "in",
        "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while",
    };

    // Opérateurs à 2-3 caractères, dont les constructions NON Lua repérées pour être refusées (!=, &&, +=, //…).
    private static readonly string[] MultiOps =
        ["...", "==", "~=", "<=", ">=", "..", "::", "!=", "&&", "||", "+=", "-=", "*=", "/=", "++", "//"];

    private static List<Tok> Lex(string s)
    {
        var r = new List<Tok>();
        int i = 0, line = 1;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '\n') { line++; i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '-' && i + 1 < s.Length && s[i + 1] == '-')
            {
                i += 2;
                int lvl = LongOpenLevel(s, i);
                if (lvl >= 0) { i = SkipLong(s, i, lvl, ref line); continue; }
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }
            if (c == '[')
            {
                int lvl = LongOpenLevel(s, i);
                if (lvl >= 0)
                {
                    int st = i, l0 = line;
                    i = SkipLong(s, i, lvl, ref line);
                    r.Add(new Tok(TK.String, s[st..i], l0));
                    continue;
                }
            }
            if (c is '"' or '\'')
            {
                int st = i++;
                while (i < s.Length && s[i] != c && s[i] != '\n') { if (s[i] == '\\') i++; i++; }
                i = Math.Min(i + 1, s.Length);
                r.Add(new Tok(TK.String, s[st..i], line));
                continue;
            }
            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < s.Length && char.IsAsciiDigit(s[i + 1])))
            {
                int st = i++;
                while (i < s.Length && (char.IsAsciiLetterOrDigit(s[i]) || s[i] == '.'
                       || (s[i] is '+' or '-' && s[i - 1] is 'e' or 'E' or 'p' or 'P'))) i++;
                r.Add(new Tok(TK.Number, s[st..i], line));
                continue;
            }
            if (char.IsAsciiLetter(c) || c == '_')
            {
                int st = i++;
                while (i < s.Length && (char.IsAsciiLetterOrDigit(s[i]) || s[i] == '_')) i++;
                string w = s[st..i];
                r.Add(new Tok(Keywords.Contains(w) ? TK.Keyword : TK.Name, w, line));
                continue;
            }
            string? op = MultiOps.FirstOrDefault(o => string.CompareOrdinal(s, i, o, 0, o.Length) == 0);
            op ??= c.ToString();
            r.Add(new Tok(TK.Op, op, line));
            i += op.Length;
        }
        return r;
    }

    // "[[" / "[==[" à la position i : niveau (nombre de '='), sinon -1.
    private static int LongOpenLevel(string s, int i)
    {
        if (i >= s.Length || s[i] != '[') return -1;
        int j = i + 1, lvl = 0;
        while (j < s.Length && s[j] == '=') { lvl++; j++; }
        return j < s.Length && s[j] == '[' ? lvl : -1;
    }

    private static int SkipLong(string s, int i, int lvl, ref int line)
    {
        string close = "]" + new string('=', lvl) + "]";
        int start = i + lvl + 2;
        int end = s.IndexOf(close, start, StringComparison.Ordinal);
        int stop = end < 0 ? s.Length : end + close.Length;
        for (int k = i; k < stop; k++) if (s[k] == '\n') line++;
        return stop;
    }

    private static bool Is(Tok t, string text) => t.Text == text && t.Kind is TK.Op or TK.Keyword;

    // =====================================================================================================
    // Analyse des noms : déclarations du texte, appels libres, accès ns.membre / ns:membre.
    // =====================================================================================================

    private static (HashSet<string> Declared, HashSet<string> Locals) Declarations(List<Tok> t)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var locals = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < t.Count; i++)
        {
            var k = t[i];
            if (Is(k, "local"))
            {
                if (i + 2 < t.Count && Is(t[i + 1], "function") && t[i + 2].Kind == TK.Name)
                {
                    declared.Add(t[i + 2].Text);
                    locals.Add(t[i + 2].Text);
                    continue;
                }
                for (int j = i + 1; j < t.Count && t[j].Kind == TK.Name; j += 2)
                {
                    declared.Add(t[j].Text);
                    locals.Add(t[j].Text);
                    if (j + 1 >= t.Count || !Is(t[j + 1], ",")) break;
                }
            }
            else if (Is(k, "function"))
            {
                int j = i + 1;
                if (j < t.Count && t[j].Kind == TK.Name)
                {
                    bool chain = j + 1 < t.Count && (Is(t[j + 1], ".") || Is(t[j + 1], ":"));
                    if (!chain) declared.Add(t[j].Text);
                    while (j + 2 < t.Count && (Is(t[j + 1], ".") || Is(t[j + 1], ":")) && t[j + 2].Kind == TK.Name) j += 2;
                    j++;
                }
                if (j < t.Count && Is(t[j], "("))
                    for (j++; j < t.Count && !Is(t[j], ")"); j++)
                        if (t[j].Kind == TK.Name) { declared.Add(t[j].Text); locals.Add(t[j].Text); }
            }
            else if (Is(k, "for"))
            {
                for (int j = i + 1; j < t.Count && t[j].Kind == TK.Name; j += 2)
                {
                    declared.Add(t[j].Text);
                    locals.Add(t[j].Text);
                    if (j + 1 >= t.Count || !Is(t[j + 1], ",")) break;
                }
            }
            else if (Is(k, "="))
            {
                // cibles d'affectation : a = … / a, b = … (les champs de constructeur { x = … } sont aussi pris : sans effet)
                for (int j = i - 1; j >= 0 && t[j].Kind == TK.Name; j -= 2)
                {
                    if (j > 0 && (Is(t[j - 1], ".") || Is(t[j - 1], ":"))) break;
                    declared.Add(t[j].Text);
                    if (j == 0 || !Is(t[j - 1], ",")) break;
                }
            }
        }
        return (declared, locals);
    }

    /// <summary>Noms inconnus d'un corps : appel libre non déclaré et absent de l'API / de Lua ; ns.membre ou ns:membre
    /// d'un module de l'API ou d'une table standard absent de sa liste (casse EXACTE : les modèles doivent être propres).</summary>
    private static List<string> UnknownNames(string code)
    {
        var doc = Doc.Value;
        var stdGlobals = StdGlobals();
        var stdMembers = StdMembers();
        var t = Lex(code);
        var (declared, locals) = Declarations(t);
        var problems = new List<string>();
        for (int i = 0; i < t.Count; i++)
        {
            if (t[i].Kind != TK.Name) continue;
            if (i > 0 && (Is(t[i - 1], ".") || Is(t[i - 1], ":"))) continue;   // pas une tête de chaîne
            var parts = new List<string> { t[i].Text };
            int j = i + 1;
            while (j + 1 < t.Count && (Is(t[j], ".") || Is(t[j], ":")) && t[j + 1].Kind == TK.Name)
            {
                parts.Add(t[j + 1].Text);
                j += 2;
            }
            bool isCall = j < t.Count && (Is(t[j], "(") || Is(t[j], "{") || t[j].Kind == TK.String);
            bool isDefinition = i > 0 && Is(t[i - 1], "function");
            string head = parts[0];
            if (parts.Count == 1)
            {
                if (isCall && !isDefinition && !declared.Contains(head) && !doc.Globals.Contains(head) && !stdGlobals.Contains(head))
                    problems.Add($"ligne {t[i].Line} : appel de « {head} » inconnu");
                continue;
            }
            if (locals.Contains(head)) continue;   // table locale homonyme : pas le module
            string member = parts[1];
            if (doc.Members.TryGetValue(head, out var api))
            {
                if (!api.Contains(member)) problems.Add($"ligne {t[i].Line} : « {head}:{member} » absent de lua-api-doc.json");
            }
            else if (stdMembers.TryGetValue(head, out var std))
            {
                if (!std.Contains(member)) problems.Add($"ligne {t[i].Line} : « {head}.{member} » absent de LuaStdLib");
            }
        }
        return problems;
    }

    // =====================================================================================================
    // Modèles
    // =====================================================================================================

    [Fact]
    public void Templates_have_unique_ids_and_french_metadata()
    {
        var scripts = LuaTemplates.Scripts;
        Assert.InRange(scripts.Count, 6, 14);
        Assert.Equal(scripts.Count, scripts.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var s in scripts)
        {
            Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", s.Id);
            Assert.False(string.IsNullOrWhiteSpace(s.Title), s.Id);
            Assert.False(string.IsNullOrWhiteSpace(s.Category), s.Id);
            AssertFrenchSentence(s.Description, "description du modèle " + s.Id);
            Assert.False(string.IsNullOrWhiteSpace(s.Body), s.Id);
            Assert.EndsWith("\n", s.Body);
            Assert.DoesNotContain('\r', s.Body);
            Assert.DoesNotContain('\t', s.Body);
            Assert.DoesNotContain('$', s.Body);   // aucun espace réservé d'extrait dans un script complet
            Assert.StartsWith("--", s.Body);      // en-tête de commentaire qui explique le script
        }
    }

    [Fact]
    public void Templates_define_an_entry_point_and_known_config_globals()
    {
        var doc = Doc.Value;
        foreach (var s in LuaTemplates.Scripts)
        {
            var t = Lex(s.Body);
            var defined = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < t.Count; i++)
                if (Is(t[i], "function") && t[i + 1].Kind == TK.Name && (i == 0 || !Is(t[i - 1], "local")))
                    defined.Add(t[i + 1].Text);
            Assert.True(defined.Overlaps(doc.Callbacks), $"{s.Id} : ni move() ni callback du moteur");

            // Globals EN MAJUSCULES affectés hors local = réglages du moteur : une faute de frappe n'aurait aucun effet.
            for (int i = 0; i + 1 < t.Count; i++)
            {
                if (t[i].Kind != TK.Name || !Regex.IsMatch(t[i].Text, "^[A-Z][A-Z0-9_]*$") || !Is(t[i + 1], "=")) continue;
                if (i > 0 && (Is(t[i - 1], "local") || Is(t[i - 1], ",") || Is(t[i - 1], ".") || Is(t[i - 1], "{"))) continue;
                Assert.True(doc.ConfigGlobals.Contains(t[i].Text), $"{s.Id} ligne {t[i].Line} : « {t[i].Text} » n'est pas un global de configuration documenté");
            }
        }
    }

    // =====================================================================================================
    // Extraits
    // =====================================================================================================

    [Fact]
    public void Snippets_have_unique_triggers_and_french_metadata()
    {
        var snippets = LuaTemplates.Snippets;
        Assert.InRange(snippets.Count, 18, 30);
        Assert.Equal(snippets.Count, snippets.Select(s => s.Trigger).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var s in snippets)
        {
            Assert.Matches("^[A-Za-z][A-Za-z0-9]*$", s.Trigger);
            Assert.False(string.IsNullOrWhiteSpace(s.Title), s.Trigger);
            AssertFrenchSentence(s.Description, "description de l'extrait " + s.Trigger);
            Assert.DoesNotContain('\r', s.Body);
            Assert.DoesNotContain('\t', s.Body);
            Assert.False(s.Body.EndsWith('\n'), s.Trigger);
            foreach (var line in s.Body.Split('\n'))
            {
                int lead = line.Length - line.TrimStart(' ').Length;
                Assert.True(lead % 4 == 0, $"{s.Trigger} : indentation non multiple de 4 ({line})");
            }
        }
    }

    [Fact]
    public void Snippet_placeholders_are_well_formed()
    {
        foreach (var s in LuaTemplates.Snippets)
        {
            var errors = LuaSnippetSyntax.Validate(s.Body);
            Assert.True(errors.Count == 0, $"{s.Trigger} : {string.Join(" ; ", errors)}");
            var ex = LuaSnippetSyntax.Expand(s.Body);
            Assert.True(ex.Stops.Count > 0, $"{s.Trigger} : aucun arrêt ${{1:…}}");
            Assert.DoesNotContain('$', ex.Text);
            foreach (var st in ex.Stops) Assert.InRange(st.Offset + st.Length, 0, ex.Text.Length);
            Assert.InRange(ex.FinalCaretOffset, 0, ex.Text.Length);
            // arrêts liés : même texte à chaque occurrence
            foreach (var g in ex.Stops.GroupBy(x => x.Index))
                Assert.Single(g.Select(x => ex.Text.Substring(x.Offset, x.Length)).Distinct());
        }
    }

    [Fact]
    public void Snippet_syntax_validator_rejects_malformed_bodies()
    {
        Assert.NotEmpty(LuaSnippetSyntax.Validate("f(${1:a)"));           // accolade non fermée
        Assert.NotEmpty(LuaSnippetSyntax.Validate("cout $x"));            // '$' isolé
        Assert.NotEmpty(LuaSnippetSyntax.Validate("f(${2:a})"));          // ${1} manquant
        Assert.NotEmpty(LuaSnippetSyntax.Validate("${1:a} = ${1:b}"));    // liés, défauts différents
        Assert.NotEmpty(LuaSnippetSyntax.Validate("a $0 b $0"));          // deux curseurs finaux
        Assert.NotEmpty(LuaSnippetSyntax.Validate("${0:x}"));             // ${0:…} interdit
        Assert.NotEmpty(LuaSnippetSyntax.Validate("${1:a\nb}"));          // saut de ligne dans un défaut
        Assert.Empty(LuaSnippetSyntax.Validate("${1:a} = ${1:a} + $2 -- $$ $0"));
        Assert.Empty(LuaSnippetSyntax.Validate("print(1)"));
    }

    [Fact]
    public void Snippet_expansion_computes_stops_caret_and_indentation()
    {
        var ex = LuaSnippetSyntax.Expand("if ${1:cond} then\n    $0\nend", "  ");
        Assert.Equal("if cond then\n      \n  end", ex.Text);
        var stop = Assert.Single(ex.Stops);
        Assert.Equal(new LuaSnippetStop(1, 3, 4), stop);
        Assert.Equal("if cond then\n      ".Length, ex.FinalCaretOffset);

        var linked = LuaSnippetSyntax.Expand("${1:x} = ${1:x} + ${2:1}");
        Assert.Equal("x = x + 1", linked.Text);
        Assert.Equal(new[] { new LuaSnippetStop(1, 0, 1), new LuaSnippetStop(1, 4, 1), new LuaSnippetStop(2, 8, 1) }, linked.Stops);
        Assert.Equal(linked.Text.Length, linked.FinalCaretOffset);   // sans $0 : curseur en fin

        var tabs = LuaSnippetSyntax.Expand("a\n    b\n        c\n      d", "", "\t");
        Assert.Equal("a\n\tb\n\t\tc\n\t  d", tabs.Text);

        Assert.Equal("cout $ ", LuaSnippetSyntax.Expand("cout $$ ").Text);
        Assert.Equal("f()", LuaSnippetSyntax.DefaultText("f($1)"));
    }

    // =====================================================================================================
    // Tous les corps : noms d'API, syntaxe Lua 5.2 / MoonSharp, équilibre des blocs
    // =====================================================================================================

    [Fact]
    public void Every_api_name_used_in_templates_snippets_and_examples_exists()
    {
        var problems = new List<string>();
        foreach (var (label, code) in AllBodies())
            foreach (var p in UnknownNames(code))
                problems.Add($"{label} : {p}");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Name_checker_catches_typos()
    {
        // garde-fou du garde-fou : le vérificateur voit bien une faute de frappe
        Assert.NotEmpty(UnknownNames("function move() getJobLevell(2) end"));
        Assert.NotEmpty(UnknownNames("function move() map:getPod() end"));
        Assert.NotEmpty(UnknownNames("print(string.formatt('%d', 1))"));
        Assert.Empty(UnknownNames("local function f(a) return a end\nfunction move() f(map:getId()) end"));
        Assert.Empty(UnknownNames("local map = {}\nmap:whatever()"));   // local homonyme d'un module
    }

    [Fact]
    public void Bodies_use_only_lua52_syntax_accepted_by_moonsharp()
    {
        var bad = new HashSet<string>(StringComparer.Ordinal) { "!=", "&&", "||", "+=", "-=", "*=", "/=", "++", "//", "!", "&", "|" };
        foreach (var (label, code) in AllBodies())
        {
            var t = Lex(code);
            for (int i = 0; i < t.Count; i++)
            {
                if (t[i].Kind == TK.Op) Assert.False(bad.Contains(t[i].Text), $"{label} ligne {t[i].Line} : opérateur non Lua « {t[i].Text} »");
                if (t[i].Kind == TK.Name && t[i].Text == "continue")
                    Assert.True(i > 0 && (Is(t[i - 1], "goto") || Is(t[i - 1], "::")), $"{label} ligne {t[i].Line} : « continue » n'existe pas en Lua");
                if (t[i].Kind == TK.Name && t[i].Text is "zaap" or "zaapi" && i + 2 < t.Count && Is(t[i + 2], "goto"))
                    Assert.Fail($"{label} : zaap:goto est une erreur de syntaxe (goto est un mot-clé), utilise zaap:go");
            }
        }
    }

    [Fact]
    public void Bodies_have_balanced_blocks_and_brackets()
    {
        foreach (var (label, code) in AllBodies())
        {
            var t = Lex(code);
            int blocks = 0, paren = 0, brace = 0, bracket = 0;
            foreach (var k in t)
            {
                if (k.Kind == TK.Keyword)
                {
                    if (k.Text is "function" or "if" or "do" or "repeat") blocks++;   // for/while ouvrent via leur do
                    else if (k.Text is "end" or "until") blocks--;
                }
                else if (k.Kind == TK.Op)
                {
                    paren += k.Text == "(" ? 1 : k.Text == ")" ? -1 : 0;
                    brace += k.Text == "{" ? 1 : k.Text == "}" ? -1 : 0;
                    bracket += k.Text == "[" ? 1 : k.Text == "]" ? -1 : 0;
                }
                Assert.True(blocks >= 0 && paren >= 0 && brace >= 0 && bracket >= 0, $"{label} ligne {k.Line} : fermeture sans ouverture");
            }
            Assert.True(blocks == 0, $"{label} : {blocks} bloc(s) non fermé(s)");
            Assert.True(paren == 0 && brace == 0 && bracket == 0, $"{label} : parenthèses / accolades / crochets déséquilibrés");
        }
    }

    // =====================================================================================================
    // Bibliothèque standard
    // =====================================================================================================

    [Fact]
    public void StdLib_entries_are_consistent()
    {
        var entries = LuaStdLib.Entries;
        Assert.Equal(entries.Count, entries.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count());
        foreach (var e in entries)
        {
            Assert.Equal(LuaApiKind.StdLib, e.Kind);
            Assert.Equal("stdlib", e.CategoryId);
            Assert.Equal("Lua standard", e.CategoryTitle);
            Assert.Empty(e.Badges);
            AssertFrenchSentence(e.Description, "description de " + e.Id);
            if (e.Args is null) Assert.Empty(e.Params);
            Assert.DoesNotContain(e.Params, p => string.IsNullOrWhiteSpace(p.Name));
        }
        // une entrée « table » (Namespace null, sans Args) pour chaque table, et LuaStdLib.Tables = ces tables
        var namespaces = entries.Where(e => e.Namespace is not null).Select(e => e.Namespace!).Distinct().ToHashSet();
        Assert.Equal(namespaces.OrderBy(x => x, StringComparer.Ordinal), LuaStdLib.Tables.OrderBy(x => x, StringComparer.Ordinal));
        foreach (var ns in namespaces)
        {
            var table = Assert.Single(entries, e => e.Namespace is null && e.Name == ns);
            Assert.Null(table.Args);
            Assert.StartsWith("Bibliothèque", table.Description);
        }
        foreach (var ns in new[] { "string", "table", "math", "os", "coroutine", "bit32", "json" })
            Assert.Contains(ns, LuaStdLib.Tables);
    }

    [Fact]
    public void StdLib_lists_exactly_the_softsandbox_inventory()
    {
        // Relevé par sonde : MoonSharp 2.0.0, new Script(CoreModules.Preset_SoftSandbox), globals + membres des tables.
        // Volontairement omis : string.dump (inutile en bac à sable), _MOONSHARP, package (internes MoonSharp).
        // Absents du bac à sable (donc jamais listés) : io, os.execute/exit/getenv/remove/rename/tmpname, debug, load*
        // (load / loadstring / dofile / require sont ajoutés par l'hôte et documentés dans lua-api-doc.json).
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "assert", "collectgarbage", "error", "getmetatable", "ipairs", "next", "pack", "pairs", "pcall", "print",
            "rawequal", "rawget", "rawlen", "rawset", "select", "setmetatable", "tonumber", "tostring", "type", "unpack",
            "xpcall", "_G", "_VERSION",
            "string", "table", "math", "os", "coroutine", "bit32", "json", "dynamic",
        };
        void Add(string ns, string names) { foreach (var n in names.Split(' ')) expected.Add(ns + "." + n); }
        Add("string", "byte char contains endsWith find format gmatch gsub len lower match rep reverse startsWith sub unicode upper");
        Add("table", "concat insert pack remove sort unpack");
        Add("math", "abs acos asin atan atan2 ceil cos cosh deg exp floor fmod frexp huge ldexp log max min modf pi pow rad random randomseed sin sinh sqrt tan tanh");
        Add("os", "clock date difftime time");
        Add("coroutine", "create resume running status wrap yield");
        Add("bit32", "arshift band bnot bor btest bxor extract lrotate lshift replace rrotate rshift");
        Add("json", "isnull null parse serialize");
        Add("dynamic", "eval prepare");

        var actual = LuaStdLib.Entries.Select(e => e.Namespace is null ? e.Name : e.Namespace + "." + e.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(expected.OrderBy(x => x, StringComparer.Ordinal), actual.OrderBy(x => x, StringComparer.Ordinal));
        foreach (var forbidden in new[] { "io", "os.execute", "os.exit", "os.getenv", "os.remove", "load", "loadstring", "dofile", "require", "debug" })
            Assert.DoesNotContain(forbidden, actual);
    }

    [Fact]
    public void StdLib_does_not_collide_with_api_names_except_print()
    {
        var doc = Doc.Value;
        var stdFunctions = LuaStdLib.Entries.Where(e => e.Namespace is null && e.Args is not null).Select(e => e.Name);
        Assert.Equal(new[] { "print" }, stdFunctions.Where(doc.Globals.Contains).ToArray());   // print : remplacé par l'hôte, documenté des deux côtés
        foreach (var table in LuaStdLib.Tables) Assert.DoesNotContain(table, doc.Members.Keys);
    }

    [Fact]
    public void StdLib_params_follow_the_optional_and_default_notation()
    {
        LuaApiEntry Get(string? ns, string name) => Assert.Single(LuaStdLib.Entries, e => e.Namespace == ns && e.Name == name);

        Assert.Equal(new[] { new LuaParam("s", false, null), new LuaParam("i", false, null), new LuaParam("j", true, "-1") },
            Get("string", "sub").Params);
        Assert.Equal(new[] { new LuaParam("t", false, null), new LuaParam("position", true, null), new LuaParam("valeur", false, null) },
            Get("table", "insert").Params);
        Assert.Equal(new[] { new LuaParam("n", false, null), new LuaParam("…", true, null) }, Get(null, "select").Params);
        Assert.Equal(new[] { new LuaParam("s", false, null), new LuaParam("i", true, "1"), new LuaParam("j", true, "i") },
            Get("string", "byte").Params);
        Assert.Empty(Get("os", "clock").Params);
        Assert.Null(Get("math", "pi").Args);
        Assert.Equal("number", Get("math", "pi").ConstType);
    }

    // =====================================================================================================
    // Textes FR (tutoiement)
    // =====================================================================================================

    [Fact]
    public void User_facing_text_uses_tutoiement()
    {
        var vous = new Regex(@"\b(vous|votre|vos)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var texts = new List<(string Where, string Text)>();
        foreach (var t in LuaTemplates.Scripts) texts.AddRange([(t.Id, t.Title), (t.Id, t.Description), (t.Id, t.Body)]);
        foreach (var s in LuaTemplates.Snippets) texts.AddRange([(s.Trigger, s.Title), (s.Trigger, s.Description), (s.Trigger, s.Body)]);
        foreach (var e in LuaStdLib.Entries) texts.AddRange([(e.Id, e.Description), (e.Id, e.Example ?? "")]);
        foreach (var (where, text) in texts)
            Assert.False(vous.IsMatch(text), $"{where} : vouvoiement (« {vous.Match(text).Value} »), l'app tutoie");
    }

    [Fact]
    public void All_descriptions_are_french_sentences()
    {
        var problems = new List<string>();
        foreach (var t in LuaTemplates.Scripts) Collect(t.Description, "modèle " + t.Id);
        foreach (var s in LuaTemplates.Snippets) Collect(s.Description, "extrait " + s.Trigger);
        foreach (var e in LuaStdLib.Entries) Collect(e.Description, e.Id);
        Assert.True(problems.Count == 0, string.Join("\n", problems));

        void Collect(string text, string where)
        {
            if (FrenchSentenceProblem(text) is { } p) problems.Add($"{where} : {p} ({text})");
        }
    }

    private static void AssertFrenchSentence(string? text, string where)
    {
        var p = FrenchSentenceProblem(text);
        Assert.True(p is null, $"{where} : {p} ({text})");
    }

    // Phrase FR : non vide, sans blanc autour, majuscule initiale, point final, mot-outil ou lettre accentuée du français
    // (garde contre un texte anglais oublié). null = conforme.
    private static string? FrenchSentenceProblem(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "vide";
        if (text != text.Trim()) return "blancs en trop";
        if (!(char.IsUpper(text[0]) || text[0] is '«' or '"')) return "doit commencer par une majuscule";
        if (!text.EndsWith('.')) return "doit finir par un point";
        var words = Regex.Split(text.ToLowerInvariant(), @"[^\p{L}']+").Where(w => w.Length > 0).ToHashSet();
        string[] fr = ["le", "la", "les", "de", "des", "du", "un", "une", "et", "ou", "à", "en", "si", "sur", "par", "pour", "tu", "ton", "ta", "tes", "qui", "au", "aux", "sans", "est", "dans", "comme", "avec", "ce", "ces", "son", "sa", "ses", "que", "pas"];
        bool french = fr.Any(words.Contains) || words.Any(w => w.StartsWith("l'") || w.StartsWith("d'"))
                      || text.IndexOfAny(['é', 'è', 'ê', 'à', 'ù', 'ç', 'â', 'î', 'ô', 'û']) >= 0;
        return french ? null : "texte non français ?";
    }
}
