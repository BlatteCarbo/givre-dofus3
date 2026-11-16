using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GivreClone.Engine.LuaTooling;

namespace GivreClone.Tests.LuaTooling;

/// <summary>
/// Données partagées des tests d'outillage Lua : le VRAI docs/portal/lua-api-doc.json (trouvé en remontant depuis le
/// dossier de l'assembly de test jusqu'à GivreClone/), chargé une fois.
/// </summary>
internal static class LuaToolingTestData
{
    private static readonly Lazy<string> _docPath = new(FindDocPath);
    private static readonly Lazy<LuaApiCatalog> _catalog = new(() =>
    {
        using var fs = File.OpenRead(DocPath);
        return LuaApiCatalog.Load(fs);
    });

    public static string DocPath => _docPath.Value;
    public static LuaApiCatalog Catalog => _catalog.Value;

    private static string FindDocPath()
    {
        var dir = GivreClone.Tests.MappingScan.WalkUp(d => File.Exists(Path.Combine(d.FullName, "docs", "portal", "lua-api-doc.json")));
        if (dir is not null) return Path.Combine(dir.FullName, "docs", "portal", "lua-api-doc.json");
        throw new FileNotFoundException("docs/portal/lua-api-doc.json introuvable en remontant depuis " + AppContext.BaseDirectory);
    }

    /// <summary>Exemples (methods[].example) et blocs de code (categories[].code) de la doc : du Lua valide.</summary>
    public static IReadOnlyList<(string Name, string Code)> DocSamples()
    {
        var res = new List<(string, string)>();
        using var doc = JsonDocument.Parse(File.ReadAllText(DocPath));
        foreach (var c in doc.RootElement.GetProperty("categories").EnumerateArray())
        {
            string cid = c.GetProperty("id").GetString() ?? "";
            if (c.TryGetProperty("code", out var code) && code.GetString() is { } s) res.Add(("code:" + cid, s));
            if (c.TryGetProperty("methods", out var ms))
                foreach (var m in ms.EnumerateArray())
                    if (m.TryGetProperty("example", out var ex) && ex.GetString() is { } e)
                        res.Add(("ex:" + m.GetProperty("id").GetString(), e));
        }
        return res;
    }

    /// <summary>Jeu de noms minimal et déterministe pour les tests unitaires du linter.</summary>
    public sealed class FakeNames : ILuaNameSet
    {
        private readonly HashSet<string> _globals = new(StringComparer.Ordinal)
        {
            "print", "pairs", "ipairs", "tostring", "tonumber", "type", "pcall", "error", "require", "dofile", "setmetatable",
            "string", "table", "math", "select", "rawset", "getPods", "getPodsMax", "getJobLevel", "gatherAll", "fight",
            "printSuccess", "MAX_PODS", "move", "bank", "onTurn", "stopped", "map", "inventory", "zaap",
        };
        private readonly Dictionary<string, string[]> _members = new(StringComparer.Ordinal)
        {
            ["map"] = ["getId", "currentMap", "getResources", "changeMap"],
            ["inventory"] = ["pods", "podsMax", "itemCount"],
            ["zaap"] = ["go", "known"],
        };
        private readonly HashSet<string> _callbacks = new(StringComparer.Ordinal) { "move", "bank", "onTurn", "stopped" };

        public bool IsKnownGlobal(string name) => _globals.Contains(name);
        public IEnumerable<string> AllGlobalNames => _globals;
        public bool IsNamespace(string name) => _members.ContainsKey(name);
        public bool IsKnownMember(string ns, string member) =>
            _members.TryGetValue(ns, out var m) && m.Any(x => string.Equals(x.Replace("_", ""), member.Replace("_", ""), StringComparison.OrdinalIgnoreCase));
        public IEnumerable<string> MembersOf(string ns) => _members.TryGetValue(ns, out var m) ? m : Array.Empty<string>();
        public bool IsCallback(string name) => _callbacks.Contains(name);
    }
}
