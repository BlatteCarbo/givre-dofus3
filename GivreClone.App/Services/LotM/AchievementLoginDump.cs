using System;
using System.Collections.Generic;
using System.Text;
using GivreCore.Protobuf;

namespace GivreClone.App.Services.LotM;

/// <summary>
/// Une connexion (ticket de jeu accepté, ou sélection de perso réussie) rouvre le droit à UN dump.
/// Le premier AchievementsEvent du pid est aussi éligible, avant que ces messages n'aient été vus.
/// </summary>
public sealed class AchievementLoginCapture
{
    private readonly object _gate = new();
    private bool _open = true;

    public void OpenLogin() { lock (_gate) _open = true; }

    /// <summary>Vrai une seule fois tant qu'un <see cref="OpenLogin"/> n'a pas rouvert la fenêtre.</summary>
    public bool TryTake()
    {
        lock (_gate)
        {
            if (!_open) return false;
            _open = false;
            return true;
        }
    }
}

/// <summary>
/// Texte intégral d'un AchievementsEvent pour la capture de découverte. Aucun plafond de lignes ni de champs :
/// <c>TransportLog.Line</c> coupe un appel à 4096 caractères, donc <see cref="Chunks"/> découpe sous cette borne
/// et la concaténation des morceaux est le texte complet.
/// </summary>
public static class AchievementEventDump
{
    public const int TransportLineMax = 4096;

    public static string Format(string? code, IReadOnlyList<ProtoField> payload)
    {
        var sb = new StringBuilder();
        sb.Append("[pkt-dbg] S->C AchievementsEvent [").Append(code ?? "").Append("] FULL ")
          .Append(payload.Count).Append(" champ(s)").AppendLine();
        Append(sb, payload, 1);
        int lines = 0;
        for (int i = 0; i < sb.Length; i++) if (sb[i] == '\n') lines++;
        sb.Append("FULL-END champs=").Append(payload.Count).Append(" lignes=").Append(lines + 1).AppendLine();
        return sb.ToString();
    }

    public static IReadOnlyList<string> Chunks(string text, int maxChars = TransportLineMax)
    {
        if (maxChars < 1) throw new ArgumentOutOfRangeException(nameof(maxChars));
        if (text.Length == 0) return Array.Empty<string>();
        if (text.Length <= maxChars) return new[] { text };
        var parts = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            int room = Math.Min(maxChars, text.Length - i);
            int take = room;
            if (i + room < text.Length)
            {
                int nl = text.LastIndexOf('\n', i + room - 1, room);
                if (nl >= i) take = nl - i + 1;
            }
            if (take < 1) take = room;
            parts.Add(text.Substring(i, take));
            i += take;
        }
        return parts;
    }

    private static void Append(StringBuilder sb, IReadOnlyList<ProtoField> fields, int depth)
    {
        foreach (var f in fields)
        {
            sb.Append(' ', depth * 2).Append('#').Append(f.Field).Append(' ');
            switch (f.Wire)
            {
                case WireType.Varint:
                    sb.Append("int ").Append(f.Varint);
                    long sgn = f.AsSignedVarint;
                    if (sgn < 0) sb.Append(" (=").Append(sgn).Append(')');
                    break;
                case WireType.I32: sb.Append("i32 ").Append(f.AsI32); break;
                case WireType.I64: sb.Append("i64 ").Append(f.AsI64); break;
                case WireType.Len when f.Message is not null: sb.Append("msg"); break;
                case WireType.Len when f.String is not null: sb.Append("str \"").Append(f.String).Append('"'); break;
                case WireType.Len: sb.Append("bytes[").Append(f.Bytes?.Length ?? 0).Append(']'); break;
                default: sb.Append('?'); break;
            }
            sb.AppendLine();
            if (f.Message is not null) Append(sb, f.Message, depth + 1);
        }
    }
}
