using System;
using System.Collections.Generic;
using GivreClone.Protocol;
using GivreCore.Protobuf;

namespace GivreClone.App.Services.Social;

/// <summary>
/// Numéros de repli = fields.json de cette version, confirmés par l'arbre du journal du 31/10/2026.
/// Un catalogue branché remplace le repli. Un champ sans nom dans fields.json garde le numéro vu dans le journal.
/// </summary>
public static class SocialFields
{
    public static int N(Func<string, string, int?>? map, string message, string name, int captured)
    {
        int? resolved = map?.Invoke(message, name);
        return resolved is > 0 ? resolved.Value : captured;
    }

    public static long? Var(IReadOnlyList<ProtoField> payload, int field)
    {
        foreach (var f in payload)
            if (f.Field == field && f.Wire == WireType.Varint)
                return f.AsSignedVarint;
        return null;
    }

    public static string? Text(IReadOnlyList<ProtoField> payload, int field) => PayloadReader.Str(payload, field);

    /// <summary>Sous-message présent (liste vide s'il est vide). Null si le champ est absent.</summary>
    public static IReadOnlyList<ProtoField>? Node(IReadOnlyList<ProtoField> payload, int field)
    {
        foreach (var f in payload)
        {
            if (f.Field != field || f.Wire != WireType.Len) continue;
            if (f.Message is { Count: > 0 } message) return message;
            if (f.Bytes is { Length: > 0 } bytes)
            {
                try { return RawProtobuf.Parse(bytes); }
                catch (FormatException) { return Array.Empty<ProtoField>(); }
            }
            return (IReadOnlyList<ProtoField>?)f.Message ?? Array.Empty<ProtoField>();
        }
        return null;
    }

    public static IEnumerable<IReadOnlyList<ProtoField>> Nodes(IReadOnlyList<ProtoField> payload, int field)
    {
        foreach (var f in payload)
        {
            if (f.Field != field || f.Wire != WireType.Len) continue;
            if (f.Message is { Count: > 0 } message) { yield return message; continue; }
            if (f.Bytes is { Length: > 0 } bytes)
            {
                IReadOnlyList<ProtoField> parsed;
                try { parsed = RawProtobuf.Parse(bytes); }
                catch (FormatException) { parsed = Array.Empty<ProtoField>(); }
                yield return parsed;
                continue;
            }
            yield return (IReadOnlyList<ProtoField>?)f.Message ?? Array.Empty<ProtoField>();
        }
    }

    /// <summary>Liste d'octets brute (canaux). Un octet = un identifiant. Jamais un varint packé.</summary>
    public static int[] RawBytes(IReadOnlyList<ProtoField> payload, int field)
    {
        foreach (var f in payload)
        {
            if (f.Field != field || f.Wire != WireType.Len || f.Bytes is not { } raw) continue;
            var ids = new int[raw.Length];
            for (int i = 0; i < raw.Length; i++) ids[i] = raw[i];
            return ids;
        }
        return Array.Empty<int>();
    }
}
