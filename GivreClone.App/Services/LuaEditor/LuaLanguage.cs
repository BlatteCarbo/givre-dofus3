using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using GivreClone.Engine.LuaTooling;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services.LuaEditor;

/// <summary>
/// Services de langage de l'éditeur Lua : catalogue de l'API (doc embarquée, même source que le portail), définition de
/// coloration construite depuis ce catalogue, et analyse d'un script (syntaxe MoonSharp + détections heuristiques).
/// Sans état par éditeur : partagé par l'éditeur de l'onglet, l'éditeur plein écran et les modales.
/// </summary>
public static class LuaLanguage
{
    private static readonly Lazy<LuaApiCatalog> _catalog = new(LoadCatalog, isThreadSafe: true);
    private static IHighlightingDefinition? _highlighting;

    /// <summary>Catalogue de l'API Lua (chargé une fois, à la demande ; vide si la ressource manque — jamais d'exception).</summary>
    public static LuaApiCatalog Catalog => _catalog.Value;

    /// <summary>Précharge le catalogue hors du thread UI (ouverture de l'onglet sans à-coup).</summary>
    public static void Warmup() => System.Threading.Tasks.Task.Run(() => _ = Catalog);

    private static LuaApiCatalog LoadCatalog()
    {
        try
        {
            using var s = typeof(LuaLanguage).Assembly.GetManifestResourceStream("GivreClone.LuaApi.json");
            return s is null ? LuaApiCatalog.Empty : LuaApiCatalog.Load(s);
        }
        catch (Exception e)
        {
            Logger.Warn($"Catalogue de l'API Lua illisible : {e.GetType().Name}");
            return LuaApiCatalog.Empty;
        }
    }

    // ------------------------------------------------------------------ coloration

    /// <summary>Définition de coloration « GivreLua » (thread UI). Mots-clés, chaînes/commentaires (y compris longs), nombres,
    /// et les noms de l'API Givre par famille : modules, fonctions, constantes de config, callbacks, bibliothèque standard.</summary>
    public static IHighlightingDefinition Highlighting => _highlighting ??= BuildHighlighting();

    // Palette (thème sombre de l'app). Couleurs choisies proches des conventions VS Code pour un œil habitué.
    internal const string ColComment = "#6A9955", ColString = "#CE9178", ColKeyword = "#C586C0", ColLiteral = "#569CD6",
        ColNumber = "#B5CEA8", ColFunction = "#DCDCAA", ColNamespace = "#4EC9B0", ColConstant = "#4FC1FF",
        ColCallback = "#FFB86C", ColStdLib = "#9CDCFE", ColMember = "#E6DB9A", ColLabel = "#D7BA7D";

    private static IHighlightingDefinition BuildHighlighting()
    {
        var cat = Catalog;
        static bool Word(string n) => n.Length > 0 && (char.IsLetter(n[0]) || n[0] == '_') && n.All(c => char.IsLetterOrDigit(c) || c == '_');
        var kw = new HashSet<string>(LuaLexer.Keywords.Where(k => k is not ("true" or "false" or "nil")), StringComparer.Ordinal);
        var namespaces = new SortedSet<string>(cat.Namespaces.Where(Word), StringComparer.Ordinal);
        var callbacks = new SortedSet<string>(cat.Globals.Where(e => e.Kind == LuaApiKind.Callback).Select(e => e.Name).Where(Word), StringComparer.Ordinal);
        var constants = new SortedSet<string>(cat.Globals.Where(e => e.Kind == LuaApiKind.Constant).Select(e => e.Name).Where(Word), StringComparer.Ordinal);
        var stdlib = new SortedSet<string>(cat.Globals.Where(e => e.Kind == LuaApiKind.StdLib).Select(e => e.Name).Where(Word), StringComparer.Ordinal);
        var functions = new SortedSet<string>(cat.Globals.Where(e => e.Kind == LuaApiKind.Function).Select(e => e.Name).Where(Word), StringComparer.Ordinal);
        foreach (var set in new[] { namespaces, callbacks, constants, stdlib, functions }) set.ExceptWith(kw);
        functions.ExceptWith(callbacks); functions.ExceptWith(namespaces); stdlib.ExceptWith(namespaces);

        var sb = new StringBuilder();
        sb.Append("<SyntaxDefinition name=\"GivreLua\" extensions=\".lua\" xmlns=\"http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008\">");
        void Color(string name, string fg, string? weight = null, string? style = null)
            => sb.Append($"<Color name=\"{name}\" foreground=\"{fg}\"{(weight is null ? "" : $" fontWeight=\"{weight}\"")}{(style is null ? "" : $" fontStyle=\"{style}\"")} />");
        Color("Comment", ColComment, style: "italic"); Color("String", ColString); Color("Keyword", ColKeyword, "bold");
        Color("Literal", ColLiteral, "bold"); Color("Number", ColNumber); Color("Function", ColFunction); Color("Namespace", ColNamespace);
        Color("Constant", ColConstant); Color("Callback", ColCallback, "bold"); Color("StdLib", ColStdLib); Color("Member", ColMember);
        Color("Label", ColLabel); Color("Escape", "#D7BA7D");
        sb.Append("<RuleSet ignoreCase=\"false\">");
        // Commentaires / chaînes longues : niveaux [[ ]], [=[ ]=] … [===[ ]===] (le moteur xshd n'a pas de fin dynamique).
        for (int lvl = 0; lvl <= 3; lvl++)
        {
            string eq = new('=', lvl);
            sb.Append($"<Span color=\"Comment\" multiline=\"true\"><Begin>--\\[{eq}\\[</Begin><End>\\]{eq}\\]</End></Span>");
        }
        sb.Append("<Span color=\"Comment\"><Begin>--</Begin></Span>");
        for (int lvl = 0; lvl <= 3; lvl++)
        {
            string eq = new('=', lvl);
            sb.Append($"<Span color=\"String\" multiline=\"true\"><Begin>\\[{eq}\\[</Begin><End>\\]{eq}\\]</End></Span>");
        }
        foreach (var q in new[] { "\"", "'" })
            sb.Append($"<Span color=\"String\"><Begin>{Xml(q)}</Begin><End>{Xml(q)}</End><RuleSet><Span color=\"Escape\" begin=\"\\\\\" end=\".\" /></RuleSet></Span>");
        sb.Append("<Rule color=\"Label\">::[A-Za-z_][A-Za-z0-9_]*::</Rule>");
        Keywords("Keyword", kw); Keywords("Literal", new[] { "true", "false", "nil" });
        // Membre appelé après « : » ou « . » (map:getPods, string.format) : AVANT les listes de noms (priorité à la position).
        sb.Append("<Rule color=\"Member\">(?&lt;=[:.])[A-Za-z_][A-Za-z0-9_]*(?=\\s*[(\"'{])</Rule>");
        Keywords("Namespace", namespaces); Keywords("Callback", callbacks); Keywords("Constant", constants);
        Keywords("StdLib", stdlib); Keywords("Function", functions);
        sb.Append("<Rule color=\"Number\">\\b0[xX][0-9a-fA-F]+(\\.[0-9a-fA-F]*)?([pP][+-]?[0-9]+)?|\\b[0-9]+(\\.[0-9]*)?([eE][+-]?[0-9]+)?|\\.[0-9]+([eE][+-]?[0-9]+)?</Rule>");
        sb.Append("</RuleSet></SyntaxDefinition>");

        void Keywords(string color, IEnumerable<string> words)
        {
            var list = words.ToList();
            if (list.Count == 0) return;
            sb.Append($"<Keywords color=\"{color}\">");
            foreach (var w in list) sb.Append("<Word>").Append(Xml(w)).Append("</Word>");
            sb.Append("</Keywords>");
        }

        try
        {
            using var sr = new StringReader(sb.ToString());
            using var xr = XmlReader.Create(sr);
            return HighlightingLoader.Load(xr, HighlightingManager.Instance);
        }
        catch (Exception e)
        {
            Logger.Warn($"Coloration Lua : définition invalide ({e.GetType().Name}) — repli sans noms d'API");
            return HighlightingManager.Instance.GetDefinition("C#")!;
        }
    }

    private static string Xml(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    // ------------------------------------------------------------------ analyse

    /// <summary>Première erreur de SYNTAXE (compilation MoonSharp sans exécution), ou null. Thread quelconque, Script jetable.</summary>
    public static LuaDiagnostic? SyntaxError(string text, string chunk)
    {
        try
        {
            new Script(CoreModules.None).LoadString(text ?? "", null, chunk);
            return null;
        }
        catch (InterpreterException e)
        {
            string raw = e.DecoratedMessage ?? e.Message;
            if (LuaErrorLocationParser.TryParse(raw, out var loc) && loc is not null)
            {
                int endLine = loc.EndLine ?? loc.Line, endCol = loc.EndColumn ?? loc.Column + 1;
                if (endLine == loc.Line && endCol <= loc.Column) endCol = loc.Column + 1;
                return new LuaDiagnostic(loc.Line, loc.Column, endLine, endCol, LuaDiagnosticSeverity.Error, "lua.syntax",
                                         Loc.Fmt("L.Lua.Editor.SyntaxError", loc.Message), LuaErrorLocationParser.FrenchHint(loc.Message));
            }
            return new LuaDiagnostic(1, 0, 1, 1, LuaDiagnosticSeverity.Error, "lua.syntax", Loc.Fmt("L.Lua.Editor.SyntaxError", e.Message),
                                     LuaErrorLocationParser.FrenchHint(e.Message));
        }
        catch (Exception e)
        {
            return new LuaDiagnostic(1, 0, 1, 1, LuaDiagnosticSeverity.Error, "lua.syntax", Loc.Fmt("L.Lua.Editor.SyntaxError", e.Message), null);
        }
    }

    /// <summary>Analyse complète : erreur de syntaxe MoonSharp (autoritaire) + détections heuristiques (fautes courantes, bloc
    /// non fermé à sa LIGNE d'ouverture, fonction inexistante avec suggestion…). Dédoublonne : une détection d'erreur sur la
    /// même ligne que l'erreur de syntaxe la remplace (message plus parlant) ; l'erreur « fin de fichier » de MoonSharp cède
    /// la place au bloc non fermé désigné par sa ligne d'ouverture.</summary>
    public static IReadOnlyList<LuaDiagnostic> Analyze(string text, string chunk)
    {
        var result = new List<LuaDiagnostic>();
        IReadOnlyList<LuaDiagnostic> lint;
        try { lint = LuaLinter.Lint(text ?? "", Catalog); }
        catch (Exception e) { Logger.Warn($"Analyse Lua : {e.GetType().Name}"); lint = Array.Empty<LuaDiagnostic>(); }
        var syntax = SyntaxError(text ?? "", chunk);
        if (syntax is not null)
        {
            bool atEof = syntax.Message.Contains("<eof>", StringComparison.Ordinal);
            bool replaced = lint.Any(d => d.Severity == LuaDiagnosticSeverity.Error
                                         && (d.Line == syntax.Line || (atEof && d.Code == "lua.unclosed-block")));
            if (!replaced) result.Add(syntax);
        }
        result.AddRange(lint);
        result.Sort((a, b) => a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.Column.CompareTo(b.Column));
        return result;
    }
}
