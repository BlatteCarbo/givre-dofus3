using System;
using System.Collections.Generic;
using System.Linq;
using GivreControl;

namespace GivreClone.App.Services.Mcp;

public static class McpConsole
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, ConsoleJournal> Journals = new();
    public static ConsoleJournal For(int server, string? character, uint pid)
    {
        string key = string.IsNullOrEmpty(character) ? "#" + pid : server + ":" + character;
        lock (Gate)
        {
            if (Journals.TryGetValue(key, out var journal)) return journal;
            if (Journals.Count >= 128) Journals.Remove(Journals.Keys.First());
            return Journals[key] = new ConsoleJournal();
        }
    }
}
