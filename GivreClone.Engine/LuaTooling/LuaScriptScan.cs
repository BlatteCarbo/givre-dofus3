using System;
using System.Collections.Generic;
using System.Text;

namespace GivreClone.Engine.LuaTooling;

/// <summary>
/// Balayage des déclarations d'un script (approximatif, portée = fichier) : locales, fonctions, paramètres, variables de
/// boucle, globals assignés, membres définis (function M:f / M.x = …), chargements externes (dofile/require/load) et
/// manipulations dynamiques de _G. Partagé par le linter et <see cref="LuaCallContext.Declarations"/>.
/// </summary>
internal sealed class LuaScriptScan
{
    internal readonly record struct FuncDef(string Name, int TokIdx, bool IsLocal, bool TopLevel);

    public readonly List<(string Name, bool IsFunction, int Line, string? Args)> Decls = new();
    public readonly HashSet<string> Declared = new(StringComparer.Ordinal);
    public readonly HashSet<string> GlobalAssigned = new(StringComparer.Ordinal);
    public readonly HashSet<string> DefinedMembers = new(StringComparer.Ordinal);   // "ns\u0001forme tolérante"
    public readonly List<FuncDef> FuncDefs = new();
    public bool HasExternalLoad, DynamicGlobals, TopLevelReturn;

    private readonly Dictionary<string, int> _declIndex = new(StringComparer.Ordinal);
    private readonly string _text;
    private readonly List<LuaToken> _toks;

    private const byte Block = 0, LoopHead = 1, Brace = 2, Paren = 3, Bracket = 4;

    private LuaScriptScan(string text, List<LuaToken> toks) { _text = text; _toks = toks; }

    public static string MemberKey(string ns, string member) => ns + "\u0001" + LuaNameMatching.Tolerant(member);

    public static LuaScriptScan Run(string text, List<LuaToken> toks, bool[]? suppressed = null)
    {
        var s = new LuaScriptScan(text, toks);
        s.Scan(suppressed);
        return s;
    }

    private string S(int i) => LuaLexer.Slice(_text, _toks[i]);

    private void Scan(bool[]? suppressed)
    {
        var ctx = new List<byte>();
        int blockDepth = 0, localEq = -1, n = _toks.Count;
        for (int i = 0; i < n; i++)
        {
            if (suppressed is not null && suppressed[i]) continue;
            var t = _toks[i];
            switch (t.Kind)
            {
                case LuaTokenKind.Keyword:
                {
                    if (LuaText.AfterMemberSep(_text, _toks, i)) break;
                    if (LuaText.Is(_text, t, "function"))
                    {
                        ScanFunction(i, blockDepth == 0);
                        ctx.Add(Block); blockDepth++;
                    }
                    else if (LuaText.Is(_text, t, "local"))
                    {
                        if (i + 1 < n && LuaText.IsKw(_text, _toks[i + 1], "function")) break;
                        localEq = ScanLocal(i, blockDepth == 0);
                    }
                    else if (LuaText.Is(_text, t, "for"))
                    {
                        ScanForVars(i);
                        ctx.Add(LoopHead); blockDepth++;
                    }
                    else if (LuaText.Is(_text, t, "while")) { ctx.Add(LoopHead); blockDepth++; }
                    else if (LuaText.Is(_text, t, "do"))
                    {
                        if (ctx.Count > 0 && ctx[^1] == LoopHead) ctx[^1] = Block;
                        else { ctx.Add(Block); blockDepth++; }
                    }
                    else if (LuaText.Is(_text, t, "if") || LuaText.Is(_text, t, "repeat")) { ctx.Add(Block); blockDepth++; }
                    else if (LuaText.Is(_text, t, "end") || LuaText.Is(_text, t, "until"))
                    {
                        for (int j = ctx.Count - 1; j >= 0; j--)
                        {
                            if (ctx[j] <= LoopHead) { ctx.RemoveRange(j, ctx.Count - j); blockDepth--; break; }
                        }
                    }
                    else if (LuaText.Is(_text, t, "return") && blockDepth == 0) TopLevelReturn = true;
                    break;
                }
                case LuaTokenKind.Punctuation:
                {
                    if (t.Length != 1) break;
                    char c = _text[t.Start];
                    if (c == '{') ctx.Add(Brace);
                    else if (c == '(') ctx.Add(Paren);
                    else if (c == '[') ctx.Add(Bracket);
                    else if (c is '}' or ')' or ']')
                    {
                        byte want = c == '}' ? Brace : c == ')' ? Paren : Bracket;
                        for (int j = ctx.Count - 1; j >= 0; j--)
                        {
                            if (ctx[j] == want) { ctx.RemoveRange(j, ctx.Count - j); break; }
                            if (ctx[j] <= LoopHead) break;   // ne traverse pas un bloc
                        }
                    }
                    break;
                }
                case LuaTokenKind.Operator:
                    if (i != localEq && t.Length == 1 && _text[t.Start] == '=' && (ctx.Count == 0 || ctx[^1] == Block))
                        ScanAssignment(i, blockDepth == 0);
                    break;
                case LuaTokenKind.Identifier:
                    ScanSpecialCall(i);
                    break;
            }
        }
    }

    // function nom(...) / function a.b:c(...) / function(...) ; i = index du mot-clé function.
    private void ScanFunction(int i, bool topLevel)
    {
        int n = _toks.Count, j = i + 1;
        bool isLocal = i > 0 && LuaText.IsKw(_text, _toks[i - 1], "local");
        string? name = null;
        int nameIdx = -1;
        if (j < n && _toks[j].Kind == LuaTokenKind.Identifier)
        {
            int k = j;
            while (k + 2 < n && LuaText.IsMemberSep(_text, _toks[k + 1]) && _toks[k + 2].Kind == LuaTokenKind.Identifier) k += 2;
            if (k == j) { name = S(j); nameIdx = j; }
            else if (k == j + 2)
            {
                string root = S(j), member = S(k);
                DefinedMembers.Add(MemberKey(root, member));
                if (root == "_G") { Declared.Add(member); GlobalAssigned.Add(member); }
            }
            else DefinedMembers.Add(MemberKey(S(k - 2), S(k)));
            j = k + 1;
        }
        var (args, paramIdx) = ReadParams(j);
        if (name is not null)
        {
            Declared.Add(name);
            if (!isLocal) GlobalAssigned.Add(name);
            AddDecl(name, true, _toks[nameIdx].Line, args);
            FuncDefs.Add(new FuncDef(name, nameIdx, isLocal, topLevel));
        }
        foreach (int p in paramIdx) { string pn = S(p); Declared.Add(pn); AddDecl(pn, false, _toks[p].Line, null); }
    }

    // Paramètres entre ( ) à partir de j : texte « a, b, ... » et index des identifiants.
    private (string? Args, List<int> Params) ReadParams(int j)
    {
        var idx = new List<int>();
        int n = _toks.Count;
        if (j >= n || !LuaText.IsPunct(_text, _toks[j], '(')) return (null, idx);
        var sb = new StringBuilder();
        for (int p = j + 1; p < n; p++)
        {
            var t = _toks[p];
            if (LuaText.IsPunct(_text, t, ')')) break;
            if (t.Kind == LuaTokenKind.Identifier) { idx.Add(p); sb.Append(LuaLexer.Slice(_text, t)); }
            else if (LuaText.IsOp(_text, t, "...")) sb.Append("...");
            else if (LuaText.IsPunct(_text, t, ',')) sb.Append(", ");
            else break;
        }
        return (sb.ToString(), idx);
    }

    // local a, b <attr>, c = … ; rend l'index du « = » (ou -1).
    private int ScanLocal(int i, bool topLevel)
    {
        int n = _toks.Count, j = i + 1;
        var names = new List<int>();
        while (j < n && _toks[j].Kind == LuaTokenKind.Identifier)
        {
            names.Add(j);
            j++;
            if (j + 2 < n && LuaText.IsOp(_text, _toks[j], "<") && _toks[j + 1].Kind == LuaTokenKind.Identifier && LuaText.IsOp(_text, _toks[j + 2], ">")) j += 3;
            if (j < n && LuaText.IsPunct(_text, _toks[j], ',')) { j++; continue; }
            break;
        }
        bool hasEq = j < n && LuaText.IsOp(_text, _toks[j], "=");
        bool isFn = hasEq && names.Count == 1 && j + 1 < n && LuaText.IsKw(_text, _toks[j + 1], "function");
        string? args = isFn ? ReadParams(j + 2).Args : null;
        foreach (int k in names)
        {
            string nm = S(k);
            Declared.Add(nm);
            AddDecl(nm, isFn, _toks[k].Line, args);
            if (isFn) FuncDefs.Add(new FuncDef(nm, k, true, topLevel));
        }
        return hasEq ? j : -1;
    }

    private void ScanForVars(int i)
    {
        int n = _toks.Count;
        for (int j = i + 1; j < n; j++)
        {
            var t = _toks[j];
            if (t.Kind == LuaTokenKind.Identifier) { string nm = S(j); Declared.Add(nm); AddDecl(nm, false, t.Line, null); }
            else if (!LuaText.IsPunct(_text, t, ',')) break;
        }
    }

    // Cibles d'une affectation (x = …, a, b = …, M.f = …, _G.x = …, _G["x"] = …).
    private void ScanAssignment(int eq, bool topLevel)
    {
        int k = eq - 1, targets = 0;
        bool rhsFunction = eq + 1 < _toks.Count && LuaText.IsKw(_text, _toks[eq + 1], "function");
        while (k >= 0)
        {
            var t = _toks[k];
            if (LuaText.IsPunct(_text, t, ']') && k >= 3 && LuaText.IsPunct(_text, _toks[k - 2], '[')
                && _toks[k - 3].Kind == LuaTokenKind.Identifier && S(k - 3) == "_G"
                && LuaText.SimpleStringValue(_text, _toks[k - 1]) is { Length: > 0 } gname)
            {
                Declared.Add(gname); GlobalAssigned.Add(gname);
                k -= 4;
            }
            else if (t.Kind == LuaTokenKind.Identifier)
            {
                LuaErrorLocationParser.ChainEndingAt(_text, _toks, k, out int startIdx, out bool complete);
                if (!complete) return;
                if (startIdx == k)
                {
                    string nm = S(k);
                    Declared.Add(nm);
                    GlobalAssigned.Add(nm);
                    bool fn = rhsFunction && targets == 0 && (k == 0 || !LuaText.IsPunct(_text, _toks[k - 1], ','));
                    if (topLevel) AddDecl(nm, fn, t.Line, fn ? ReadParams(eq + 2).Args : null);
                    if (fn) FuncDefs.Add(new FuncDef(nm, k, false, topLevel));
                }
                else
                {
                    string root = S(k - 2), member = S(k);
                    DefinedMembers.Add(MemberKey(root, member));
                    if (startIdx == k - 2 && root == "_G") { Declared.Add(member); GlobalAssigned.Add(member); }
                }
                k = startIdx - 1;
            }
            else return;
            targets++;
            if (k >= 0 && LuaText.IsPunct(_text, _toks[k], ',')) { k--; continue; }
            return;
        }
    }

    // dofile / require / load… (globals possiblement définis ailleurs), setmetatable(_G…) / setfenv (globals dynamiques),
    // rawset(_G, "nom", …).
    private void ScanSpecialCall(int i)
    {
        if (LuaText.AfterMemberSep(_text, _toks, i)) return;
        var t = _toks[i];
        int n = _toks.Count;
        bool call = i + 1 < n && (LuaText.IsPunct(_text, _toks[i + 1], '(') || _toks[i + 1].Kind == LuaTokenKind.String);
        switch (t.Length)
        {
            case 4 when LuaText.Is(_text, t, "load"):
            case 6 when LuaText.Is(_text, t, "dofile"):
            case 7 when LuaText.Is(_text, t, "require"):
            case 8 when LuaText.Is(_text, t, "loadsafe"):
            case 10 when LuaText.Is(_text, t, "loadstring"):
                if (call) HasExternalLoad = true;
                break;
            case 4 when LuaText.Is(_text, t, "_ENV"):
            case 7 when LuaText.Is(_text, t, "setfenv") || LuaText.Is(_text, t, "getfenv"):
                DynamicGlobals = true;
                break;
            case 12 when LuaText.Is(_text, t, "setmetatable"):
                if (i + 2 < n && LuaText.IsPunct(_text, _toks[i + 1], '(') && _toks[i + 2].Kind == LuaTokenKind.Identifier && S(i + 2) == "_G")
                    DynamicGlobals = true;
                break;
            case 6 when LuaText.Is(_text, t, "rawset"):
                if (i + 4 < n && LuaText.IsPunct(_text, _toks[i + 1], '(') && _toks[i + 2].Kind == LuaTokenKind.Identifier && S(i + 2) == "_G"
                    && LuaText.IsPunct(_text, _toks[i + 3], ',') && LuaText.SimpleStringValue(_text, _toks[i + 4]) is { Length: > 0 } g)
                { Declared.Add(g); GlobalAssigned.Add(g); }
                break;
        }
    }

    private void AddDecl(string name, bool isFunction, int line, string? args)
    {
        if (_declIndex.TryGetValue(name, out int at))
        {
            // une fonction l'emporte sur une simple variable (x = nil puis function x())
            if (isFunction && !Decls[at].IsFunction) Decls[at] = (name, true, line, args);
            return;
        }
        _declIndex[name] = Decls.Count;
        Decls.Add((name, isFunction, line, args));
    }
}
