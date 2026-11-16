using System;
using System.Collections.Generic;

namespace GivreClone.Engine.LuaTooling;

/// <summary>Région repliable : de la fin de la 1re ligne de l'ouvrant (qui reste visible) à la fin du fermant.</summary>
public readonly record struct LuaFold(int StartOffset, int EndOffset, string Title);

/// <summary>
/// Régions repliables Lua (multi-lignes uniquement) : function..end, if..end, for/while..do..end, do..end, repeat..until,
/// constructeurs de table {..} et commentaires longs --[[..]]. Tolérant au code déséquilibré (fermants orphelins ignorés).
/// </summary>
public static class LuaFolding
{
    private enum Fk : byte { Function, If, Loop, Do, Repeat, Brace, Paren, Bracket }

    private struct Frame
    {
        public Fk Kind;
        public int Start;      // offset du jeton ouvrant
        public int Line;       // ligne 1-based de l'ouvrant
        public bool AwaitDo;   // for / while en attente de leur « do »
    }

    /// <summary>Calcule les replis, triés par StartOffset (puis du plus large au plus étroit). Ne lève jamais.</summary>
    public static IReadOnlyList<LuaFold> Compute(string text)
    {
        var result = new List<LuaFold>();
        if (string.IsNullOrEmpty(text)) return result;
        try { ComputeCore(text, result); }
        catch { /* au mieux : ce qui a été trouvé */ }
        result.Sort(static (a, b) => a.StartOffset != b.StartOffset ? a.StartOffset.CompareTo(b.StartOffset) : b.EndOffset.CompareTo(a.EndOffset));
        return result;
    }

    private static void ComputeCore(string text, List<LuaFold> result)
    {
        var toks = LuaLexer.TokenizeList(text);
        var stack = new List<Frame>();
        for (int i = 0; i < toks.Count; i++)
        {
            var t = toks[i];
            if (t.Kind == LuaTokenKind.Comment)
            {
                if (!t.Unterminated && LuaLexer.IsLongComment(text, t))
                {
                    int le = LuaText.LineEnd(text, t.Start);
                    if (t.End > le)
                    {
                        int lvl = LuaLexer.LongBracketLevel(text, t.Start + 2);
                        result.Add(new LuaFold(le, t.End, "… ]" + new string('=', Math.Max(0, lvl)) + "]"));
                    }
                }
                continue;
            }
            if (t.Kind == LuaTokenKind.Keyword)
            {
                if (LuaText.AfterMemberSep(text, toks, i)) continue;
                if (LuaText.Is(text, t, "function")) Push(stack, Fk.Function, t);
                else if (LuaText.Is(text, t, "if")) Push(stack, Fk.If, t);
                else if (LuaText.Is(text, t, "for") || LuaText.Is(text, t, "while")) Push(stack, Fk.Loop, t, awaitDo: true);
                else if (LuaText.Is(text, t, "do"))
                {
                    if (stack.Count > 0 && stack[^1].Kind == Fk.Loop && stack[^1].AwaitDo)
                    {
                        var f = stack[^1]; f.AwaitDo = false; stack[^1] = f;
                    }
                    else Push(stack, Fk.Do, t);
                }
                else if (LuaText.Is(text, t, "repeat")) Push(stack, Fk.Repeat, t);
                else if (LuaText.Is(text, t, "end"))
                {
                    if (Pop(stack, static k => k is Fk.Function or Fk.If or Fk.Loop or Fk.Do, static k => k is Fk.Brace or Fk.Paren or Fk.Bracket) is { } f)
                        Emit(text, result, f, t, "… end");
                }
                else if (LuaText.Is(text, t, "until"))
                {
                    if (Pop(stack, static k => k == Fk.Repeat, static k => k is Fk.Brace or Fk.Paren or Fk.Bracket) is { } f)
                        Emit(text, result, f, t, "… until");
                }
                continue;
            }
            if (t.Kind == LuaTokenKind.Punctuation && t.Length == 1)
            {
                switch (text[t.Start])
                {
                    case '{': Push(stack, Fk.Brace, t); break;
                    case '(': Push(stack, Fk.Paren, t); break;
                    case '[': Push(stack, Fk.Bracket, t); break;
                    case '}':
                        if (Pop(stack, static k => k == Fk.Brace, static _ => true) is { } fb) Emit(text, result, fb, t, "… }");
                        break;
                    case ')': Pop(stack, static k => k == Fk.Paren, static _ => true); break;
                    case ']': Pop(stack, static k => k == Fk.Bracket, static _ => true); break;
                }
            }
        }
    }

    private static void Push(List<Frame> stack, Fk kind, in LuaToken t, bool awaitDo = false) =>
        stack.Add(new Frame { Kind = kind, Start = t.Start, Line = t.Line, AwaitDo = awaitDo });

    private static Frame? Pop(List<Frame> stack, Func<Fk, bool> accepts, Func<Fk, bool> skippable)
    {
        for (int j = stack.Count - 1; j >= 0; j--)
        {
            if (accepts(stack[j].Kind))
            {
                var f = stack[j];
                stack.RemoveRange(j, stack.Count - j);
                return f;
            }
            if (!skippable(stack[j].Kind)) return null;
        }
        return null;
    }

    private static void Emit(string text, List<LuaFold> result, Frame f, in LuaToken closer, string title)
    {
        if (closer.Line <= f.Line) return;   // une seule ligne : rien à replier
        int start = LuaText.LineEnd(text, f.Start);
        if (start < closer.End) result.Add(new LuaFold(start, closer.End, title));
    }
}
