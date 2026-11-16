using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GivreClone.Engine.LuaTooling;

/// <summary>Nature d'une entrée du catalogue (complétion, coloration, référence).</summary>
public enum LuaApiKind { Function, Callback, Constant, Namespace, Keyword, StdLib, Snippet, Local }

/// <summary>Paramètre lu dans la signature en texte libre de la doc.</summary>
public sealed record LuaParam(string Name, bool Optional, string? Default);

/// <summary>
/// Une signature de l'API (une entrée par signature : les alias d'une même méthode partagent Id, description, exemple
/// et badges). Namespace sans « : » (« map »), null pour un global.
/// </summary>
public sealed record LuaApiEntry(
    string Id, string? Namespace, string Name, string? Args, IReadOnlyList<LuaParam> Params, string? Returns,
    string Description, string? Example, IReadOnlyList<string> Badges, string CategoryId, string CategoryTitle, LuaApiKind Kind,
    string? ConstType = null, string? ConstDefault = null)
{
    /// <summary>Séparateur d'appel : « . » pour la bibliothèque standard (string.format), « : » pour les modules Givre.</summary>
    public char Separator => Kind == LuaApiKind.StdLib ? '.' : ':';

    /// <summary>« map:getPods », « string.format », « getPods ».</summary>
    public string QualifiedName => Namespace is null ? Name : Namespace + Separator + Name;

    /// <summary>Ligne de signature : « map:moveToward(mapId | x, y) → bool », « getPods() → number »,
    /// « MAX_PODS : number = 90 », « map (module) ».</summary>
    public string Display => Kind switch
    {
        LuaApiKind.Namespace => Name + " (module)",
        LuaApiKind.Constant => Name + (ConstType is { Length: > 0 } t ? " : " + t : "") + (ConstDefault is { Length: > 0 } d ? " = " + d : ""),
        LuaApiKind.Keyword or LuaApiKind.Snippet or LuaApiKind.Local => Name,
        LuaApiKind.StdLib when Namespace is null && Args is null && LuaApiCatalog.IsLuaStdTable(Name) => Name + " (bibliothèque Lua)",
        _ => QualifiedName + "(" + (Args ?? string.Join(", ", (Params ?? Array.Empty<LuaParam>()).Select(p => p.Optional ? "[" + p.Name + "]" : p.Name))) + ")"
             + (string.IsNullOrEmpty(Returns) ? "" : " → " + Returns),
    };

    /// <summary>Texte inséré par la complétion : le nom seul (le préfixe « map: » est déjà tapé).</summary>
    public string InsertText => Name;

    /// <summary>Verbe présent mais sans effet (catégorie « Non supporté » de la doc, badge « stub »).</summary>
    public bool IsStub => CategoryId == "stubs" || (Badges is not null && Badges.Contains("stub"));
}

/// <summary>
/// Catalogue de l'API Lua (docs/portal/lua-api-doc.json) + bibliothèque standard : complétion, aide de signature,
/// survol, référence, et jeu de noms du linter. Les globals sont en casse exacte ; les membres de modules sont
/// tolérants (casse, 1re lettre, snake_case), comme le moteur.
/// </summary>
public sealed class LuaApiCatalog : ILuaNameSet
{
    // ------------------------------------------------------------------ noms Lua connus sans la doc (filet de sécurité)

    private static readonly HashSet<string> BaseGlobals = new(StringComparer.Ordinal)
    {
        "_G", "_VERSION", "_MOONSHARP", "assert", "collectgarbage", "dofile", "error", "getmetatable", "ipairs", "load",
        "loadfile", "loadsafe", "loadstring", "next", "pairs", "pcall", "print", "rawequal", "rawget", "rawlen", "rawset",
        "require", "select", "setmetatable", "tonumber", "tostring", "type", "unpack", "xpcall", "getfenv", "setfenv", "module",
        "string", "table", "math", "os", "io", "coroutine", "bit32", "debug", "package", "json", "dynamic",
    };

    private static readonly Dictionary<string, string[]> StdMembers = new(StringComparer.Ordinal)
    {
        ["string"] = ["byte", "char", "dump", "find", "format", "gmatch", "gsub", "len", "lower", "match", "rep", "reverse", "sub", "upper", "unicode", "startsWith", "endsWith", "contains"],
        ["table"] = ["concat", "insert", "maxn", "pack", "remove", "sort", "unpack"],
        ["math"] = ["abs", "acos", "asin", "atan", "atan2", "ceil", "cos", "cosh", "deg", "exp", "floor", "fmod", "frexp", "huge", "ldexp", "log", "log10", "max", "min", "modf", "pi", "pow", "rad", "random", "randomseed", "sin", "sinh", "sqrt", "tan", "tanh"],
        ["os"] = ["clock", "date", "difftime", "time", "getenv", "exit", "execute", "remove", "rename", "tmpname"],
        ["coroutine"] = ["create", "resume", "running", "status", "wrap", "yield", "isyieldable"],
        ["bit32"] = ["arshift", "band", "bnot", "bor", "btest", "bxor", "extract", "replace", "lrotate", "lshift", "rrotate", "rshift"],
        ["json"] = ["parse", "serialize", "isNull", "null"],
        ["io"] = ["close", "flush", "input", "lines", "open", "output", "read", "stderr", "stdin", "stdout", "tmpfile", "type", "write"],
        ["debug"] = ["traceback", "getinfo", "getlocal", "sethook", "gethook", "debug", "getmetatable", "setmetatable", "getupvalue", "setupvalue", "getregistry", "upvalueid", "upvaluejoin", "getuservalue", "setuservalue"],
        ["package"] = ["path", "cpath", "loaded", "preload", "searchers", "searchpath", "config", "loadlib"],
        ["dynamic"] = ["eval", "prepare"],
    };

    private static readonly string[] BaseCallbacks =
    [
        "move", "bank", "onTurn", "fightManagement", "onPlacement", "onFightEnd", "stopped", "onPrivateMessage",
        "messagesRegistering", "phenix", "prefightManagement", "challengeManagement",
    ];

    // Alias de modules : d2data: accepte aussi les verbes de data: (doc : « data: (alias d2data:) »).
    private static readonly Dictionary<string, string> NsAliases = new(StringComparer.Ordinal) { ["d2data"] = "data" };

    /// <summary>true pour une fonction / table de base de Lua (print, pairs, string, math…).</summary>
    internal static bool IsLuaBaseGlobal(string name) => BaseGlobals.Contains(name);

    /// <summary>true pour une table de la bibliothèque standard (string, table, math, os, coroutine, bit32, json…).</summary>
    public static bool IsLuaStdTable(string name) => name is not null && StdMembers.ContainsKey(name);

    // ------------------------------------------------------------------ état

    private readonly bool _permissive;
    private readonly List<LuaApiEntry> _entries;
    private readonly List<string> _namespaces;
    private readonly List<LuaApiEntry> _globals;
    private readonly HashSet<string> _apiNs;
    private readonly Dictionary<string, string> _nsCanonical;                        // insensible à la casse -> nom exact
    private readonly Dictionary<string, List<LuaApiEntry>> _exact;                   // "ns\u0001name" -> entrées
    private readonly Dictionary<string, Dictionary<string, List<LuaApiEntry>>> _tolerant; // ns -> forme tolérante -> entrées
    private readonly Dictionary<string, List<LuaApiEntry>> _members;                 // ns (exact) -> membres (modules + tables std)
    private readonly Dictionary<(string Ns, string Form), string> _unavailable = new();   // (ns, forme tolérante) -> raison (liaisons qui lèvent une erreur Lua)
    private readonly Dictionary<string, HashSet<string>> _knownMemberForms;          // ns -> formes tolérantes connues (doc + stubs + alias)
    private readonly Dictionary<string, List<string>> _memberNames;                  // ns -> noms distincts (suggestions)
    private readonly HashSet<string> _wildcardNs;
    private readonly Dictionary<string, List<string>> _prefixWildcards;
    private readonly HashSet<string> _globalNames;
    private readonly List<string> _allGlobalNames;
    private readonly HashSet<string> _callbacks;
    private readonly List<(string Id, string Title, string? Blurb)> _categories;
    private readonly Dictionary<string, List<LuaApiEntry>> _byCategory;
    private readonly List<(string Name, string Type, string Role)> _stepFields;
    private readonly string[] _foldName, _foldQual, _foldId, _foldDesc;

    /// <summary>Catalogue vide et PERMISSIF (ressource absente) : aucun nom n'est signalé inconnu par le linter.</summary>
    public static LuaApiCatalog Empty { get; } = new(new LuaApiDocRoot(), Array.Empty<LuaApiEntry>(), permissive: true);

    /// <summary>Charge lua-api-doc.json (STJ source-generated). Lève <see cref="JsonException"/> /
    /// <see cref="InvalidDataException"/> si le JSON est illisible.</summary>
    public static LuaApiCatalog Load(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var root = JsonSerializer.Deserialize(json, LuaApiJsonContext.Default.LuaApiDocRoot)
                   ?? throw new InvalidDataException("lua-api-doc.json vide");
        return new LuaApiCatalog(root, LuaStdLib.Entries, permissive: false);
    }

    /// <summary>Comme <see cref="Load"/> sans lever : <see cref="Empty"/> en cas d'échec.</summary>
    public static bool TryLoad(Stream? json, out LuaApiCatalog catalog)
    {
        try
        {
            if (json is not null) { catalog = Load(json); return true; }
        }
        catch { /* repli ci-dessous */ }
        catalog = Empty;
        return false;
    }

    private LuaApiCatalog(LuaApiDocRoot root, IReadOnlyList<LuaApiEntry> stdlib, bool permissive)
    {
        _permissive = permissive;
        _entries = new List<LuaApiEntry>(900);
        _categories = new List<(string, string, string?)>();
        _stepFields = new List<(string, string, string)>();
        _apiNs = new HashSet<string>(StringComparer.Ordinal);
        _namespaces = new List<string>();
        _wildcardNs = new HashSet<string>(StringComparer.Ordinal);
        _prefixWildcards = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var stubForms = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var nsCategory = new Dictionary<string, LuaApiDocCategory>(StringComparer.Ordinal);
        var docCats = root.Categories ?? new List<LuaApiDocCategory>();

        // 1) Espaces de noms (ordre d'apparition) — nécessaires pour lire les stubs.
        foreach (var cat in docCats)
            foreach (var m in cat.Methods ?? new List<LuaApiDocMethod>())
                foreach (var sig in m.Sig ?? new List<LuaApiDocSig>())
                    if (NormNs(sig.Ns) is { } ns && _apiNs.Add(ns)) { _namespaces.Add(ns); nsCategory[ns] = cat; }

        // 2) Entrées de la doc.
        foreach (var cat in docCats)
        {
            string cid = cat.Id ?? "", ctitle = cat.Title ?? cid;
            if (cid == "callbacks")
            {
                _entries.Add(new LuaApiEntry("cb.move", null, "move", null, Array.Empty<LuaParam>(), "table | false",
                    "Point d'entrée du script : le moteur l'appelle en boucle. Renvoie une table d'étapes (trajet déclaratif : { map, path, gather, fight… }), ou pilote avec des verbes (goToCellId, gatherAll, fight…) et renvoie false pour arrêter le script.",
                    "function move()\n    return {\n        { map = \"-15,39\", path = \"right\", gather = true },\n        { map = \"-14,39\", path = \"left\", gather = true },\n    }\nend",
                    Array.Empty<string>(), cid, ctitle, LuaApiKind.Callback));
                _entries.Add(new LuaApiEntry("cb.bank", null, "bank", null, Array.Empty<LuaParam>(), "table | false",
                    "Jouée à la place de move() quand les pods atteignent MAX_PODS : même forme que move() (trajet jusqu'à la banque, dépôt…). Facultative.",
                    "function bank()\n    return {\n        { map = \"-15,39\", path = \"top\" },\n    }\nend",
                    Array.Empty<string>(), cid, ctitle, LuaApiKind.Callback));
            }
            if (cid == "config")
                foreach (var g in cat.Globals ?? new List<LuaApiDocGlobal>())
                    if (!string.IsNullOrWhiteSpace(g.Name))
                        _entries.Add(new LuaApiEntry("config." + g.Name.Trim(), null, g.Name.Trim(), null, Array.Empty<LuaParam>(), null,
                            g.Role ?? "", null, Array.Empty<string>(), cid, ctitle, LuaApiKind.Constant, g.Type, g.Def));
            foreach (var m in cat.Methods ?? new List<LuaApiDocMethod>())
            {
                bool isCallback = cid == "callbacks" || (m.Id ?? "").StartsWith("cb.", StringComparison.Ordinal);
                IReadOnlyList<string> badges = m.Badges is { Count: > 0 } b ? b.ToArray() : Array.Empty<string>();
                foreach (var sig in m.Sig ?? new List<LuaApiDocSig>())
                {
                    string name = (sig.Name ?? "").Trim();
                    if (name.Length == 0 || LuaLexer.IsKeyword(name)) continue;   // zaap:goto / zaapi:goto : jamais proposés
                    string? args = string.IsNullOrWhiteSpace(sig.Args) ? null : sig.Args.Trim();
                    if (string.Equals(sig.ImplementationStatus, "unavailable", StringComparison.OrdinalIgnoreCase) && NormNs(sig.Ns) is { } uns)
                        _unavailable[(uns, LuaNameMatching.Tolerant(name))] = string.IsNullOrWhiteSpace(sig.Throws) ? "API non disponible" : sig.Throws.Trim();
                    _entries.Add(new LuaApiEntry(m.Id ?? name, NormNs(sig.Ns), name, args, LuaArgsParser.Parse(args),
                        string.IsNullOrWhiteSpace(sig.Returns) ? null : sig.Returns.Trim(), m.Desc ?? "", m.Example, badges,
                        cid, ctitle, isCallback ? LuaApiKind.Callback : LuaApiKind.Function));
                }
            }
            if (cid == "declarative")
                foreach (var f in cat.Fields ?? new List<LuaApiDocField>())
                    if (!string.IsNullOrWhiteSpace(f.Name)) _stepFields.Add((f.Name.Trim(), f.Type ?? "", f.Role ?? ""));
            foreach (var st in cat.Stubs ?? new List<LuaApiDocStub>())
                foreach (var item in st.Items ?? new List<string>())
                    ParseStubItem(item, stubForms);
        }

        // 3) Modules.
        foreach (var ns in _namespaces)
        {
            var cat = docCats.FirstOrDefault(c => string.Equals(c.Id, "mod-" + ns, StringComparison.OrdinalIgnoreCase)) ?? nsCategory[ns];
            string desc = !string.IsNullOrWhiteSpace(cat.Blurb) ? cat.Blurb! : (cat.Title ?? ns);
            _entries.Add(new LuaApiEntry("ns." + ns, null, ns, null, Array.Empty<LuaParam>(), null, desc, null,
                Array.Empty<string>(), cat.Id ?? "", cat.Title ?? "", LuaApiKind.Namespace));
        }

        // 4) Stubs : badge « stub » sur les verbes listés « Non supporté ».
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            if (e.Namespace is null || e.Kind != LuaApiKind.Function || e.Badges.Contains("stub")) continue;
            if (IsStubMember(e.Namespace, e.Name, stubForms)) _entries[i] = e with { Badges = e.Badges.Append("stub").ToArray() };
        }

        // 5) Bibliothèque standard (+ tables std sans entrée propre).
        var stdGlobalNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in stdlib)
        {
            if (e is null) continue;
            _entries.Add(e);
            if (e.Namespace is null) stdGlobalNames.Add(e.Name);
        }
        foreach (var t in stdlib.Where(e => e?.Namespace is not null).Select(e => e.Namespace!).Distinct(StringComparer.Ordinal).ToList())
            if (!stdGlobalNames.Contains(t) && !_apiNs.Contains(t))
                _entries.Add(new LuaApiEntry("std." + t, null, t, null, Array.Empty<LuaParam>(), "table",
                    $"Bibliothèque standard Lua « {t} » : {t}.nom(...).", null, Array.Empty<string>(),
                    stdlib.First(e => e?.Namespace == t).CategoryId, stdlib.First(e => e?.Namespace == t).CategoryTitle, LuaApiKind.StdLib));

        // 6) Index.
        _nsCanonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ns in _namespaces) _nsCanonical.TryAdd(ns, ns);
        _exact = new Dictionary<string, List<LuaApiEntry>>(StringComparer.Ordinal);
        _tolerant = new Dictionary<string, Dictionary<string, List<LuaApiEntry>>>(StringComparer.Ordinal);
        _members = new Dictionary<string, List<LuaApiEntry>>(StringComparer.Ordinal);
        _byCategory = new Dictionary<string, List<LuaApiEntry>>(StringComparer.Ordinal);
        _globals = new List<LuaApiEntry>();
        _callbacks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in _entries)
        {
            Add(_exact, Key(e.Namespace, e.Name), e);
            Add(_byCategory, e.CategoryId ?? "", e);
            if (e.Namespace is null)
            {
                if (e.Kind is LuaApiKind.Function or LuaApiKind.Callback or LuaApiKind.Constant or LuaApiKind.Namespace or LuaApiKind.StdLib)
                    _globals.Add(e);
                if (e.Kind == LuaApiKind.Callback) _callbacks.Add(e.Name);
            }
            else
            {
                Add(_members, e.Namespace, e);
                if (!_tolerant.TryGetValue(e.Namespace, out var tol)) _tolerant[e.Namespace] = tol = new Dictionary<string, List<LuaApiEntry>>(StringComparer.Ordinal);
                Add(tol, LuaNameMatching.Tolerant(e.Name), e);
            }
        }
        if (permissive) foreach (var cb in BaseCallbacks) _callbacks.Add(cb);
        // Ordre des globals : API (fonctions, callbacks, constantes), modules, puis bibliothèque standard.
        _globals = _globals.OrderBy(e => e.Kind switch { LuaApiKind.Namespace => 1, LuaApiKind.StdLib => 2, _ => 0 }).ToList();

        _knownMemberForms = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        _memberNames = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var ns in _namespaces)
        {
            var forms = new HashSet<string>(StringComparer.Ordinal);
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void AddFrom(string src)
            {
                if (!_members.TryGetValue(src, out var list)) return;
                foreach (var e in list)
                {
                    forms.Add(LuaNameMatching.Tolerant(e.Name));
                    if (seen.Add(e.Name)) names.Add(e.Name);
                }
            }
            AddFrom(ns);
            if (NsAliases.TryGetValue(ns, out var alias)) AddFrom(alias);
            if (stubForms.TryGetValue(ns, out var sf)) forms.UnionWith(sf);
            _knownMemberForms[ns] = forms;
            _memberNames[ns] = names;
        }

        _globalNames = new HashSet<string>(StringComparer.Ordinal);
        _allGlobalNames = new List<string>();
        foreach (var e in _globals) if (_globalNames.Add(e.Name)) _allGlobalNames.Add(e.Name);
        foreach (var bg in BaseGlobals) if (_globalNames.Add(bg)) _allGlobalNames.Add(bg);

        foreach (var c in docCats)
            if (c.Id is { Length: > 0 } id && _byCategory.ContainsKey(id))
                _categories.Add((id, c.Title ?? id, string.IsNullOrWhiteSpace(c.Blurb) ? null : c.Blurb));

        // Formes repliées pour la recherche (précalculées une fois).
        int n = _entries.Count;
        _foldName = new string[n]; _foldQual = new string[n]; _foldId = new string[n]; _foldDesc = new string[n];
        for (int i = 0; i < n; i++)
        {
            var e = _entries[i];
            _foldName[i] = LuaNameMatching.Fold(e.Name);
            _foldQual[i] = e.Namespace is null ? _foldName[i] : LuaNameMatching.Fold(e.Namespace) + ":" + _foldName[i];
            _foldId[i] = LuaNameMatching.Fold(e.Id);
            _foldDesc[i] = LuaNameMatching.Fold(e.Description);
        }
    }

    // ------------------------------------------------------------------ API publique

    public IReadOnlyList<LuaApiEntry> Entries => _entries;

    /// <summary>Modules de l'API Givre (« map », « inventory »…), sans « : », ordre de la doc.</summary>
    public IReadOnlyList<string> Namespaces => _namespaces;

    /// <summary>Entrées globales (Namespace null) : fonctions, callbacks, constantes, modules, bibliothèque standard.
    /// Les surcharges d'un même nom sont toutes présentes (grouper par nom côté UI).</summary>
    public IReadOnlyList<LuaApiEntry> Globals => _globals;

    /// <summary>Champs d'une étape de trajet déclaratif (map, path, gather, fight…).</summary>
    public IReadOnlyList<(string Name, string Type, string Role)> StepFields => _stepFields;

    public IReadOnlyList<(string Id, string Title, string? Blurb)> Categories => _categories;

    public IReadOnlyList<LuaApiEntry> InCategory(string categoryId) =>
        categoryId is not null && _byCategory.TryGetValue(categoryId, out var l) ? l : Array.Empty<LuaApiEntry>();

    /// <summary>Membres d'un module Givre ou d'une table standard (string, table, math…) ; nom insensible à la casse,
    /// « : » final toléré. d2data: inclut les verbes de data:.</summary>
    public IReadOnlyList<LuaApiEntry> MembersOfNamespace(string ns)
    {
        string? key = ResolveNs(ns);
        if (key is null || !_members.TryGetValue(key, out var list)) return Array.Empty<LuaApiEntry>();
        if (!NsAliases.TryGetValue(key, out var alias) || !_members.TryGetValue(alias, out var extra)) return list;
        var names = new HashSet<string>(list.Select(e => e.Name), StringComparer.Ordinal);
        return list.Concat(extra.Where(e => !names.Contains(e.Name))).ToList();
    }

    /// <summary>Toutes les signatures d'un nom : exactes d'abord ; sinon, pour un module, forme tolérante (casse,
    /// snake_case) puis alias (d2data -&gt; data). Globals : casse exacte.</summary>
    public IReadOnlyList<LuaApiEntry> Overloads(string? ns, string name)
    {
        if (string.IsNullOrEmpty(name)) return Array.Empty<LuaApiEntry>();
        string? key = null;
        if (!string.IsNullOrEmpty(ns))
        {
            key = ResolveNs(ns);
            if (key is null) return Array.Empty<LuaApiEntry>();
        }
        if (_exact.TryGetValue(Key(key, name), out var exact)) return exact;
        if (key is null) return Array.Empty<LuaApiEntry>();
        string form = LuaNameMatching.Tolerant(name);
        if (_tolerant.TryGetValue(key, out var tol) && tol.TryGetValue(form, out var hits)) return hits;
        if (NsAliases.TryGetValue(key, out var alias))
        {
            if (_exact.TryGetValue(Key(alias, name), out var ex2)) return ex2;
            if (_tolerant.TryGetValue(alias, out var tol2) && tol2.TryGetValue(form, out var hits2)) return hits2;
        }
        return Array.Empty<LuaApiEntry>();
    }

    public LuaApiEntry? Find(string? ns, string name)
    {
        var o = Overloads(ns, name);
        return o.Count > 0 ? o[0] : null;
    }

    /// <summary>
    /// Recherche pour la référence : nom, « ns:nom », id et mots de la description (insensible à la casse et aux
    /// accents). Classement : correspondance de nom (exact, préfixe, début de mot camelCase) &gt; nom contenant &gt; id
    /// &gt; description ; à égalité, verbes globaux de l'API d'abord, puis modules, membres, bibliothèque standard.
    /// </summary>
    public IReadOnlyList<LuaApiEntry> Search(string query, int max = 200)
    {
        if (string.IsNullOrWhiteSpace(query) || max <= 0) return Array.Empty<LuaApiEntry>();
        string q = LuaNameMatching.Fold(query.Trim()).Replace('.', ':');
        var words = q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var scored = new List<(int Group, int KindRank, int Sub, int Len, int Idx)>();
        for (int i = 0; i < _entries.Count; i++)
        {
            string name = _foldName[i], qual = _foldQual[i];
            int group, sub = 0;
            if (words.Length == 1)
            {
                string w = words[0];
                if (name == w || qual == w) { group = 0; sub = 0; }
                else if (name.StartsWith(w, StringComparison.Ordinal) || (w.Contains(':') && qual.StartsWith(w, StringComparison.Ordinal))) { group = 0; sub = 1; }
                else if (CamelWordStart(_entries[i].Name, w)) { group = 0; sub = 2; }
                else if (qual.Contains(w, StringComparison.Ordinal)) group = 1;
                else if (_foldId[i].Contains(w, StringComparison.Ordinal)) group = 2;
                else if (_foldDesc[i].Contains(w, StringComparison.Ordinal)) group = 3;
                else continue;
            }
            else
            {
                bool allName = true, all = true;
                foreach (var w in words)
                {
                    bool inName = qual.Contains(w, StringComparison.Ordinal);
                    if (!inName) allName = false;
                    if (!inName && !_foldId[i].Contains(w, StringComparison.Ordinal) && !_foldDesc[i].Contains(w, StringComparison.Ordinal)) { all = false; break; }
                }
                if (!all) continue;
                group = allName ? 1 : 3;
            }
            scored.Add((group, KindRank(_entries[i]), sub, _entries[i].Name.Length, i));
        }
        scored.Sort();
        var res = new List<LuaApiEntry>(Math.Min(max, scored.Count));
        foreach (var s in scored)
        {
            res.Add(_entries[s.Idx]);
            if (res.Count >= max) break;
        }
        return res;
    }

    // ------------------------------------------------------------------ ILuaNameSet

    public bool IsKnownGlobal(string name) => _permissive || (name is not null && _globalNames.Contains(name));

    public IEnumerable<string> AllGlobalNames => _allGlobalNames;

    public bool IsNamespace(string name) => name is not null && _apiNs.Contains(name);

    public string? UnavailableReason(string ns, string member)
    {
        if (_unavailable.Count == 0 || string.IsNullOrEmpty(ns) || string.IsNullOrEmpty(member)) return null;
        return ResolveNs(ns) is { } key && _unavailable.TryGetValue((key, LuaNameMatching.Tolerant(member)), out var why) ? why : null;
    }

    public bool IsKnownMember(string ns, string member)
    {
        if (_permissive) return true;
        if (string.IsNullOrEmpty(ns) || string.IsNullOrEmpty(member)) return false;
        string? key = ResolveNs(ns);
        if (key is not null && _apiNs.Contains(key))
        {
            string form = LuaNameMatching.Tolerant(member);
            if (_knownMemberForms.TryGetValue(key, out var forms) && forms.Contains(form)) return true;
            if (_wildcardNs.Contains(key)) return true;
            if (_prefixWildcards.TryGetValue(key, out var prefixes))
                foreach (var p in prefixes) if (form.StartsWith(p, StringComparison.Ordinal)) return true;
            return false;
        }
        string t = ns.TrimEnd(':');
        if (StdMembers.TryGetValue(t, out var std) && std.Contains(member)) return true;
        return _exact.ContainsKey(Key(t, member));
    }

    public IEnumerable<string> MembersOf(string ns)
    {
        string? key = ResolveNs(ns);
        if (key is null) return Array.Empty<string>();
        if (_memberNames.TryGetValue(key, out var names)) return names;
        var list = _members.TryGetValue(key, out var m) ? m.Select(e => e.Name).Distinct(StringComparer.Ordinal).ToList() : new List<string>();
        if (StdMembers.TryGetValue(key, out var std)) foreach (var s in std) if (!list.Contains(s)) list.Add(s);
        return list;
    }

    public bool IsCallback(string name) => name is not null && _callbacks.Contains(name);

    // ------------------------------------------------------------------ interne

    private static string? NormNs(string? ns)
    {
        if (string.IsNullOrWhiteSpace(ns)) return null;
        string t = ns.Trim().TrimEnd(':', '.').Trim();
        return t.Length == 0 ? null : t;
    }

    // Module Givre (casse ignorée) -> nom exact ; table standard -> telle quelle ; sinon null.
    private string? ResolveNs(string? ns)
    {
        string? t = NormNs(ns);
        if (t is null) return null;
        if (_nsCanonical.TryGetValue(t, out var canon)) return canon;
        if (_members.ContainsKey(t) || StdMembers.ContainsKey(t)) return t;
        foreach (var k in _members.Keys) if (string.Equals(k, t, StringComparison.OrdinalIgnoreCase)) return k;
        return null;
    }

    private static string Key(string? ns, string name) => (ns ?? "") + "\u0001" + name;

    private static void Add<TK>(Dictionary<TK, List<LuaApiEntry>> d, TK key, LuaApiEntry e) where TK : notnull
    {
        if (!d.TryGetValue(key, out var l)) d[key] = l = new List<LuaApiEntry>();
        l.Add(e);
    }

    private static int KindRank(LuaApiEntry e) => e.Kind switch
    {
        LuaApiKind.Function or LuaApiKind.Callback or LuaApiKind.Constant when e.Namespace is null => 0,
        LuaApiKind.Namespace => 1,
        LuaApiKind.Function or LuaApiKind.Callback => 2,
        LuaApiKind.StdLib => 3,
        _ => 4,
    };

    // « pods » commence un mot de « getPods » (majuscule après minuscule, ou après « _ »).
    private static bool CamelWordStart(string name, string foldedWord)
    {
        for (int p = 1; p < name.Length; p++)
        {
            bool boundary = (char.IsUpper(name[p]) && !char.IsUpper(name[p - 1])) || name[p - 1] == '_';
            if (!boundary) continue;
            if (name.Length - p < foldedWord.Length) return false;
            if (LuaNameMatching.Fold(name[p..]).StartsWith(foldedWord, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    // Item de la catégorie « Non supporté » : « ns:a / b / c → valeur ; d → … » ; « ns:* » ; « ns:getCost* ».
    private void ParseStubItem(string item, Dictionary<string, HashSet<string>> forms)
    {
        if (string.IsNullOrWhiteSpace(item)) return;
        string? curNs = null;
        foreach (var seg in item.Split(';'))
        {
            int arrow = seg.IndexOf('→');
            string s = arrow >= 0 ? seg[..arrow] : seg;
            foreach (var raw in s.Split('/', ','))
            {
                string piece = raw.Trim();
                int i = 0;
                while (i < piece.Length && (char.IsAsciiLetterOrDigit(piece[i]) || piece[i] == '_')) i++;
                if (i == 0) { if (piece.StartsWith('*') && curNs is not null) _wildcardNs.Add(curNs); continue; }
                string first = piece[..i];
                if (i < piece.Length && (piece[i] == ':' || piece[i] == '.'))
                {
                    int j = i + 1;
                    while (j < piece.Length && (char.IsAsciiLetterOrDigit(piece[j]) || piece[j] == '_' || piece[j] == '*')) j++;
                    curNs = _apiNs.Contains(first) ? first : null;
                    if (curNs is not null) Register(curNs, piece[(i + 1)..j], forms);
                }
                else if (curNs is not null)
                {
                    string member = i < piece.Length && piece[i] == '*' ? first + "*" : first;
                    Register(curNs, member, forms);
                }
            }
        }
    }

    private void Register(string ns, string member, Dictionary<string, HashSet<string>> forms)
    {
        if (member.Length == 0) return;
        if (member == "*") { _wildcardNs.Add(ns); return; }
        if (member.EndsWith('*'))
        {
            if (!_prefixWildcards.TryGetValue(ns, out var l)) _prefixWildcards[ns] = l = new List<string>();
            l.Add(LuaNameMatching.Tolerant(member.TrimEnd('*')));
            return;
        }
        if (!forms.TryGetValue(ns, out var set)) forms[ns] = set = new HashSet<string>(StringComparer.Ordinal);
        set.Add(LuaNameMatching.Tolerant(member));
    }

    private bool IsStubMember(string ns, string name, Dictionary<string, HashSet<string>> forms)
    {
        string form = LuaNameMatching.Tolerant(name);
        if (_wildcardNs.Contains(ns)) return true;
        if (forms.TryGetValue(ns, out var set) && set.Contains(form)) return true;
        if (_prefixWildcards.TryGetValue(ns, out var prefixes))
            foreach (var p in prefixes) if (form.StartsWith(p, StringComparison.Ordinal)) return true;
        return false;
    }
}
