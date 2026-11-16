using System;
using System.Collections.Generic;
using GivreCore.Protobuf;

namespace GivreClone.App.Services;

/// <summary>Secours de <see cref="CaptureDecoder"/> pour une trame que RawProtobuf refuse à cause de son plafond de champs
/// (16 384) : la table des prix moyens (ObjectAveragePricesEvent, ~80 Ko) en compte bien plus. Même lecture (Any : type_url
/// puis value), profondeur bornée, aucun plafond de champs ; une trame malformée rend null.</summary>
internal static class LargeFrameDecoder
{
    private const string TypePrefix = "type.ankama.com/";
    private const int MaximumDepth = 12;

    public static (string Code, IReadOnlyList<ProtoField> Payload)? Decode(byte[] frame)
    {
        try { return Find(Parse(frame, 0), 0); }
        catch (FormatException) { return null; }
    }

    private static (string, IReadOnlyList<ProtoField>)? Find(List<ProtoField> tree, int depth)
    {
        if (depth > MaximumDepth) return null;
        ProtoField? url = null, value = null;
        foreach (var f in tree)
        {
            if (f.Field == 1 && f.Wire == WireType.Len && f.String?.StartsWith(TypePrefix, StringComparison.Ordinal) == true) url ??= f;
            else if (f.Field == 2 && f.Wire == WireType.Len) value ??= f;
        }
        if (url is not null)
            return (url.String![TypePrefix.Length..], value?.Bytes is { } body ? Parse(body, 0) : new List<ProtoField>());
        foreach (var f in tree)
            if (f.Message is not null && Find(f.Message, depth + 1) is { } hit) return hit;
        return null;
    }

    private static List<ProtoField> Parse(ReadOnlySpan<byte> input, int depth)
    {
        if (depth > MaximumDepth) throw new FormatException("profondeur");
        var fields = new List<ProtoField>();
        int i = 0;
        while (i < input.Length)
        {
            ulong key = Varint(input, ref i);
            ulong number = key >> 3;
            if (number is 0 or > 536_870_911) throw new FormatException("numéro de champ");
            var wire = (WireType)(key & 7);
            var f = new ProtoField { Field = (int)number, Wire = wire };
            switch (wire)
            {
                case WireType.Varint: f.Varint = Varint(input, ref i); break;
                case WireType.I32:
                case WireType.I64:
                    int width = wire == WireType.I32 ? 4 : 8;
                    if (input.Length - i < width) throw new FormatException("tronqué");
                    f.Bytes = input.Slice(i, width).ToArray(); i += width; break;
                case WireType.Len:
                    ulong len = Varint(input, ref i);
                    if (len > (ulong)(input.Length - i)) throw new FormatException("longueur");
                    var body = input.Slice(i, (int)len); i += (int)len;
                    f.Bytes = body.ToArray();
                    if (body.Length is > 0 and <= 512 && IsText(body)) f.String = System.Text.Encoding.UTF8.GetString(body);
                    if (body.Length > 0)
                        try { var nested = Parse(body, depth + 1); if (nested.Count > 0) f.Message = nested; }
                        catch (FormatException) { }
                    break;
                default: throw new FormatException("type de fil");
            }
            fields.Add(f);
        }
        return fields;
    }

    private static bool IsText(ReadOnlySpan<byte> body)
    {
        foreach (byte b in body) if (b < 0x20 || b > 0x7E) return false;
        return true;
    }

    private static ulong Varint(ReadOnlySpan<byte> input, ref int i)
    {
        ulong value = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            if (i >= input.Length) throw new FormatException("varint");
            byte b = input[i++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
        }
        throw new FormatException("varint");
    }
}
