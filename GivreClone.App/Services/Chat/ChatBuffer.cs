using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Chat;

/// <summary>Un message de discussion vu par un bot : canal (numéro du protocole), expéditeur, texte, et s'il vient de nous (MP envoyé).</summary>
public sealed record ChatEntry(long Id, DateTime At, int Channel, string Label, string Sender, string Content, bool Outgoing);

/// <summary>Journal de discussion borné (par bot). Numérote les messages, garde les plus récents, filtre par canal. Thread-safe, pur.</summary>
public sealed class ChatBuffer
{
    public const int PrivateChannel = 10;   // MP : confirmé par capture (ChatChannelMessageEvent.channel == 10)
    private readonly int _max;
    private readonly List<ChatEntry> _items = new();
    private readonly object _gate = new();
    private long _next = 1;

    public ChatBuffer(int max = 400) { _max = Math.Max(10, max); }

    /// <summary>Libellé d'un canal. MP (10) confirmé par capture ; les autres suivent la numérotation d'envoi de l'app (0 général, 1 groupe, 2 guilde, 3 alliance, 5 commerce, 6 recrutement), probable à la réception.</summary>
    public static string Label(int channel) => channel switch
    {
        PrivateChannel => "MP", -1 => "Système", 0 => "Général", 1 => "Groupe", 2 => "Guilde", 3 => "Alliance", 4 => "Équipe", 5 => "Commerce", 6 => "Recrutement", 7 => "Débutants", 8 => "Administration", 9 => "Info", 11 => "Communauté", 12 => "Arène", _ => "Canal " + channel,
    };

    public ChatEntry Add(int channel, string sender, string content, bool outgoing = false, DateTime? at = null)
    {
        lock (_gate)
        {
            var e = new ChatEntry(_next++, at ?? DateTime.Now, channel, Label(channel), sender, content, outgoing);
            _items.Add(e);
            if (_items.Count > _max) _items.RemoveRange(0, _items.Count - _max);
            return e;
        }
    }

    /// <summary>Messages plus récents que <paramref name="afterId"/>, du plus ancien au plus récent ; <paramref name="channel"/> = numéro ou « mp » / « all ».</summary>
    public IReadOnlyList<ChatEntry> Since(long afterId, int limit = 100, string? channel = null)
    {
        int? ch = channel is null or "" or "all" ? null : channel.Equals("mp", StringComparison.OrdinalIgnoreCase) ? PrivateChannel : int.TryParse(channel, out var n) ? n : null;
        lock (_gate)
            return _items.Where(e => e.Id > afterId && (ch is null || e.Channel == ch)).TakeLast(Math.Clamp(limit, 1, _max)).ToList();
    }

    public long LatestId { get { lock (_gate) return _next - 1; } }
    public void Clear() { lock (_gate) _items.Clear(); }
}
