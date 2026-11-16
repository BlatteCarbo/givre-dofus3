using System;
using System.Collections.Generic;
using System.Text;

namespace GivreClone.Engine.LuaTooling;

/// <summary>
/// Lecture au mieux des arguments en texte libre de la doc (« GID, quantité, price », « gid [, n] », « [bool=true] »,
/// « mapId | x, y », « {noms} [, timeoutMs=10000] », « … »). Les virgules DANS { } / ( ) / guillemets ne séparent pas ;
/// un paramètre ouvert entre [ ] est optionnel ; « nom=valeur » donne une valeur par défaut. Ne lève jamais.
/// </summary>
public static class LuaArgsParser
{
    public static IReadOnlyList<LuaParam> Parse(string? args)
    {
        if (string.IsNullOrWhiteSpace(args)) return Array.Empty<LuaParam>();
        try { return ParseCore(args); }
        catch { return Array.Empty<LuaParam>(); }
    }

    private static List<LuaParam> ParseCore(string s)
    {
        var res = new List<LuaParam>();
        var cur = new StringBuilder();
        int optDepth = 0, nest = 0;
        char quote = '\0';
        bool started = false, curOptional = false;

        void MarkStart()
        {
            if (started) return;
            started = true;
            curOptional = optDepth > 0;
        }

        void Flush()
        {
            string raw = cur.ToString().Trim();
            cur.Clear();
            bool opt = curOptional;
            started = false;
            curOptional = false;
            if (raw.Length == 0) return;
            string name = raw;
            string? def = null;
            int eq = TopLevelIndexOf(raw, '=');
            if (eq > 0)
            {
                name = raw[..eq].Trim();
                string d = raw[(eq + 1)..].Trim();
                def = d.Length > 0 ? d : null;
            }
            if (name.Length == 0) return;
            if (def is not null || name is "…" or "...") opt = true;
            res.Add(new LuaParam(name, opt, def));
        }

        foreach (char c in s)
        {
            if (quote != '\0')
            {
                cur.Append(c);
                if (c == quote) quote = '\0';
                continue;
            }
            switch (c)
            {
                case '"': case '\'':
                    MarkStart(); quote = c; cur.Append(c);
                    break;
                case '[':
                    if (nest == 0) optDepth++; else cur.Append(c);
                    break;
                case ']':
                    if (nest == 0) optDepth = Math.Max(0, optDepth - 1); else cur.Append(c);
                    break;
                case '{': case '(':
                    MarkStart(); nest++; cur.Append(c);
                    break;
                case '}': case ')':
                    nest = Math.Max(0, nest - 1); cur.Append(c);
                    break;
                case ',':
                    if (nest == 0) Flush(); else cur.Append(c);
                    break;
                default:
                    if (char.IsWhiteSpace(c)) { if (started) cur.Append(' '); }
                    else { MarkStart(); cur.Append(c); }
                    break;
            }
        }
        Flush();
        return res;
    }

    private static int TopLevelIndexOf(string s, char target)
    {
        int nest = 0;
        char quote = '\0';
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            if (c is '"' or '\'') quote = c;
            else if (c is '{' or '(') nest++;
            else if (c is '}' or ')') nest = Math.Max(0, nest - 1);
            else if (c == target && nest == 0) return i;
        }
        return -1;
    }
}
