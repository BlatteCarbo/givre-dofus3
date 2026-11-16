using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace GivreClone.Tests;

/// <summary>Scan statique du code : toutes les recherches de mapping (message, champ) écrites en clair. Partagé par
/// MappingContractTests (le fields.json livré les contient-il ?) et par le manifeste RequiredMapping (contrôle du bundle
/// streamé au chargement, côté app).</summary>
internal static class MappingScan
{
    // Lectures : helper(« Message », « champ »).
    private static readonly Regex Lookup = new(
        @"\b(F|FNum|FN|fn|FieldOf|FieldNumber|Num|VN|SN|Slot|FNo|SF|FieldNo|FieldNum|ErrNum)\(\s*""([A-Za-z0-9_]+)""\s*,\s*""([A-Za-z0-9_]+)""",
        RegexOptions.Compiled);

    // Forges (MoveForge) : new Msg(« Message »).Vi/.Vu/.Str/.Bytes/.Sub/.Require(« champ »), chaînes multilignes et
    // sous-messages imbriqués compris : chaque champ est rattaché à la TÊTE de sa chaîne d'appels (ou à la variable reçue).
    private static readonly Regex MsgField = new(@"\.(Vi|Vu|Str|Bytes|Sub|Require)\(\s*""([A-Za-z0-9_]+)""", RegexOptions.Compiled);
    private static readonly Regex MsgAssign = new(@"\b([A-Za-z_]\w*)\s*=\s*new Msg\(\s*""([A-Za-z0-9_]+)""\s*\)", RegexOptions.Compiled);
    private static readonly Regex LiteralArg = new(@"^\s*""([A-Za-z0-9_]+)""\s*$", RegexOptions.Compiled);

    public static readonly string[] SourceDirs = { "GivreClone.App", "GivreClone.Protocol", "GivreClone.Engine" };

    public sealed record Hit(string Message, string Field, string Where);

    public static List<Hit> Scan(string root)
    {
        var hits = new List<Hit>();
        foreach (var src in SourceDirs)
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, src), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || Path.GetFileName(file) == "RequiredMapping.cs") continue;
            string rel = Path.GetRelativePath(root, file);
            string code = StripComments(File.ReadAllText(file));
            foreach (Match m in Lookup.Matches(code))
                hits.Add(new Hit(m.Groups[2].Value, m.Groups[3].Value, $"{rel}:{LineOf(code, m.Index)}"));
            if (Path.GetFileName(file) == "MoveForge.cs") hits.AddRange(ScanForge(code, rel));
        }
        return hits;
    }

    private static IEnumerable<Hit> ScanForge(string code, string rel)
    {
        var assigns = MsgAssign.Matches(code).Select(m => (Var: m.Groups[1].Value, Pos: m.Index, Msg: m.Groups[2].Value)).ToList();
        foreach (Match f in MsgField.Matches(code))
            if (Owner(code, f.Index, assigns) is { } owner)
                yield return new Hit(owner, f.Groups[2].Value, $"{rel}:{LineOf(code, f.Index)}");
    }

    // Remonte la chaîne d'appels à gauche du « . » : saute les groupes de parenthèses équilibrés (.Sub(...), .Vi(...)) jusqu'à
    // la tête new Msg("X") -> X ; ou jusqu'à une variable (m.Vu(...)) -> message de sa dernière affectation new Msg("X").
    private static string? Owner(string code, int dot, List<(string Var, int Pos, string Msg)> assigns)
    {
        int j = dot - 1;
        while (j >= 0)
        {
            char c = code[j];
            if (char.IsWhiteSpace(c) || c == '.') { j--; continue; }
            if (c == ')')
            {
                int close = j, depth = 1;
                j--;
                while (j >= 0 && depth > 0) { if (code[j] == ')') depth++; else if (code[j] == '(') depth--; j--; }
                int open = j + 1;                                    // position de la '(' correspondante
                int k = j;
                while (k >= 0 && (char.IsLetterOrDigit(code[k]) || code[k] == '_')) k--;
                string ident = code.Substring(k + 1, j - k);
                if (ident == "Msg")
                {
                    int q = k;
                    while (q >= 0 && char.IsWhiteSpace(code[q])) q--;
                    if (q >= 2 && code.Substring(q - 2, 3) == "new")
                    {
                        var arg = LiteralArg.Match(code.Substring(open + 1, close - open - 1));
                        return arg.Success ? arg.Groups[1].Value : null;   // new Msg(variable) : code dynamique, non vérifiable
                    }
                }
                if (ident.Length == 0) return null;
                j = k;
                continue;
            }
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                int k = j;
                while (k >= 0 && (char.IsLetterOrDigit(code[k]) || code[k] == '_')) k--;
                string ident = code.Substring(k + 1, j - k);
                var a = assigns.Where(x => x.Var == ident && x.Pos < dot).OrderByDescending(x => x.Pos).FirstOrDefault();
                return a.Msg;
            }
            return null;
        }
        return null;
    }

    // Commentaires remplacés par des espaces (longueur et retours à la ligne conservés -> numéros de ligne exacts).
    private static string StripComments(string s)
    {
        var b = new StringBuilder(s);
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '"' || c == '\'')
            {
                bool verbatim = c == '"' && i > 0 && s[i - 1] == '@';
                i++;
                while (i < s.Length && s[i] != c)
                {
                    if (!verbatim && s[i] == '\\') i++;
                    else if (s[i] == '\n' && !verbatim) break;
                    i++;
                }
                i++;
                continue;
            }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') { b[i] = ' '; i++; }
                continue;
            }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                while (i < s.Length && !(s[i] == '*' && i + 1 < s.Length && s[i + 1] == '/')) { if (s[i] != '\n') b[i] = ' '; i++; }
                if (i < s.Length) { b[i] = ' '; b[i + 1] = ' '; i += 2; }
                continue;
            }
            i++;
        }
        return b.ToString();
    }

    private static int LineOf(string code, int index)
    {
        int n = 1;
        for (int i = 0; i < index; i++) if (code[i] == '\n') n++;
        return n;
    }

    // NOMS DE MESSAGE comparés ou émis : p.Name == / is … or … / case "…": (noms en …Event/Request/Response), CodeForName,
    // SendGame, new Msg. Les tables de compatibilité Lua (anciens noms) et le manifeste sont exclus.
    private static readonly Regex MsgNameLiteral = new(@"""([A-Z][a-z][A-Za-z0-9]{3,})""", RegexOptions.Compiled);
    private static readonly Regex NameCompare = new(@"\bName\s*(?:==|!=|is\b)", RegexOptions.Compiled);
    private static readonly Regex CaseLabel = new(@"\bcase\s+""([A-Z][a-z][A-Za-z0-9]{3,})""\s*:", RegexOptions.Compiled);
    private static readonly Regex EmitName = new(@"\b(?:CodeForName|SendGame)\(\s*""([A-Za-z0-9_]+)""", RegexOptions.Compiled);
    private static readonly Regex NewMsgName = new(@"\bnew Msg\(\s*""([A-Za-z0-9_]+)""", RegexOptions.Compiled);
    private static readonly HashSet<string> NameScanSkip = new(StringComparer.Ordinal)
        { "LuaMessageAliases.cs", "LuaSnowCompat.cs", "RequiredMapping.cs", "RequiredMapping.Extra.cs" };

    public static List<Hit> ScanMessageNames(string root)
    {
        var hits = new List<Hit>();
        foreach (var src in SourceDirs)
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, src), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || NameScanSkip.Contains(Path.GetFileName(file))) continue;
            string rel = Path.GetRelativePath(root, file);
            string code = StripComments(File.ReadAllText(file));
            // new Msg("X") : message ÉMIS (code requis) sauf s'il est le 2e argument d'un .Sub(champ, new Msg(...)) -> sous-message.
            foreach (Match m in NewMsgName.Matches(code))
            {
                int k = m.Index - 1;
                while (k >= 0 && char.IsWhiteSpace(code[k])) k--;
                if (k >= 0 && code[k] == ',') continue;
                hits.Add(new Hit(m.Groups[1].Value, "", $"{rel}:{LineOf(code, m.Index)}"));
            }
            var lines = code.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];
                if (NameCompare.IsMatch(l))
                    foreach (Match m in MsgNameLiteral.Matches(l))
                    {
                        // Préfixe/fragment (p.Name.StartsWith("Exchange")…) : pas un nom de message.
                        string before = l[..m.Index].TrimEnd();
                        if (before.EndsWith("StartsWith(") || before.EndsWith("EndsWith(") || before.EndsWith("Contains(")) continue;
                        hits.Add(new Hit(m.Groups[1].Value, "", $"{rel}:{i + 1}"));
                    }
                foreach (Match m in CaseLabel.Matches(l)) hits.Add(new Hit(m.Groups[1].Value, "", $"{rel}:{i + 1}"));
                foreach (Match m in EmitName.Matches(l)) hits.Add(new Hit(m.Groups[1].Value, "", $"{rel}:{i + 1}"));
            }
        }
        return hits;
    }

    public static string RepoRoot([CallerFilePath] string sourceFile = "")
        => WalkUp(static d => File.Exists(Path.Combine(d.FullName, "GivreClone.slnx")), sourceFile)?.FullName
            ?? throw new DirectoryNotFoundException("racine du dépôt (GivreClone.slnx)");

    /// <summary>Premier dossier, en partant du marqueur <c>givre-repo-root.txt</c> puis de la sortie et des sources,
    /// qui satisfait <paramref name="match"/>. Le marqueur couvre une sortie hors dépôt (<c>--artifacts-path</c>).</summary>
    public static DirectoryInfo? WalkUp(Func<DirectoryInfo, bool> match, [CallerFilePath] string sourceFile = "")
    {
        foreach (var start in RepoCandidates(sourceFile))
        {
            if (string.IsNullOrEmpty(start)) continue;
            for (var d = new DirectoryInfo(start); d is not null; d = d.Parent)
                if (match(d)) return d;
        }
        return null;
    }

    private static IEnumerable<string?> RepoCandidates(string sourceFile)
    {
        var marker = Path.Combine(AppContext.BaseDirectory, "givre-repo-root.txt");
        if (File.Exists(marker))
        {
            var line = File.ReadAllText(marker).Trim();
            if (line.Length > 0) yield return line;
        }
        yield return AppContext.BaseDirectory;
        yield return Environment.CurrentDirectory;
        if (string.IsNullOrEmpty(sourceFile)) yield break;
        yield return Path.GetDirectoryName(sourceFile);
        if (Path.IsPathRooted(sourceFile)) yield break;
        yield return Path.GetDirectoryName(Path.GetFullPath(sourceFile));
        yield return Path.GetDirectoryName(Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, sourceFile)));
        yield return Path.GetDirectoryName(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, sourceFile)));
    }
}
