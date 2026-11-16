using System.Collections.Generic;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>ZaapKnownListEvent { destinations } : ids de carte des zaaps débloqués, en liste compacte (packed varints, capture 23/10 :
/// un seul champ #1 de 187 octets). Une forme non compacte (un varint par occurrence) est acceptée aussi.</summary>
public static class ZaapKnownListReader
{
    public static IReadOnlyList<long> Read(IReadOnlyList<ProtoField> payload, int field)
    {
        var maps = new List<long>();
        foreach (var f in payload)
        {
            if (f.Field != field) continue;
            if (f.Bytes is { } b) maps.AddRange(Packed(b));
            else if (f.Wire == WireType.Varint && f.Varint != 0) maps.Add((long)f.Varint);
        }
        return maps;
    }

    public static IEnumerable<long> Packed(byte[] b)
    {
        int i = 0;
        while (i < b.Length)
        {
            ulong v = 0; int shift = 0;
            while (i < b.Length)
            {
                byte x = b[i++];
                v |= (ulong)(x & 0x7F) << shift;
                if (x < 0x80) break;
                shift += 7;
                if (shift > 63) yield break;   // varint corrompu : on arrête proprement
            }
            if (v != 0) yield return (long)v;
        }
    }
}
