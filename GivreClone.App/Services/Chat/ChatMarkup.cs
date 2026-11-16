using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace GivreClone.App.Services.Chat;

public sealed record ChatPart(string Text, string? Kind = null, int Id = 0);

/// <summary>Décode les liens du jeu sans interpréter le message comme du HTML.</summary>
public static class ChatMarkup
{
    private static readonly Regex Links = new(@"\{(chatitem|chatmonster|chatachievement|lobby),([^{}]*)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<ChatPart> Parse(string content)
    {
        string decoded = WebUtility.HtmlDecode(content);
        var parts = new List<ChatPart>();
        int start = 0;
        foreach (Match m in Links.Matches(decoded))
        {
            if (m.Index > start) parts.Add(new(decoded[start..m.Index]));
            string kind = m.Groups[1].Value;
            string[] args = m.Groups[2].Value.Split(',');
            // chatitem : uid d'instance, puis gid du modèle (pas l'inverse).
            int index = kind == "chatitem" && args.Length > 1 ? 1 : 0;
            if (kind == "lobby") parts.Add(new("Groupe", kind));
            else if (int.TryParse(args[index], out int id) && id > 0) parts.Add(new(m.Value, kind, id));
            else parts.Add(new(m.Value)); // lien malformé : conserver l'information
            start = m.Index + m.Length;
        }
        if (start < decoded.Length) parts.Add(new(decoded[start..]));
        return parts;
    }
}
