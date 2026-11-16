using System;
using System.Collections.Generic;
using GivreClone.Protocol;
using GivreCore.Protobuf;

namespace GivreClone.App.Services.Social;

/// <summary>GameMessage de demande, octets comparables au hex= du journal. Prefix ajoute la longueur pour Inject.</summary>
public static class SocialFrames
{
    public static byte[] CardInfoMessage(Func<string, string?>? codeForName, Func<string, string, int?>? fieldNumber, long characterId, int uid)
    {
        string? code = codeForName?.Invoke("CardInfoRequest");
        if (string.IsNullOrEmpty(code)) return Array.Empty<byte>();
        int id = SocialFields.N(fieldNumber, "CardInfoRequest", "fzvn", 1);
        return Request(code, new List<ProtoField> { Var(id, characterId) }, uid);
    }

    public static byte[] CardViewMessage(Func<string, string?>? codeForName, Func<string, string, int?>? fieldNumber, long characterId, int tab, int uid)
    {
        string? code = codeForName?.Invoke("CardViewRequest");
        if (string.IsNullOrEmpty(code)) return Array.Empty<byte>();
        int id = SocialFields.N(fieldNumber, "CardViewRequest", "fzus", 1);
        int tabField = SocialFields.N(fieldNumber, "CardViewRequest", "fzuq", 2);
        var payload = new List<ProtoField> { Var(id, characterId) };
        if (tab != 0) payload.Add(Var(tabField, tab));
        payload.Sort((a, b) => a.Field.CompareTo(b.Field));
        return Request(code, payload, uid);
    }

    public static byte[] SubscribeMessage(Func<string, string?>? codeForName, Func<string, string, int?>? fieldNumber, IReadOnlyList<int> active, IReadOnlyList<int> disabled, int uid)
    {
        string? code = codeForName?.Invoke("SubscribeMultipleChannelRequest");
        if (string.IsNullOrEmpty(code)) return Array.Empty<byte>();
        if (!BytesOf(active, out byte[] activeBytes) || !BytesOf(disabled, out byte[] disabledBytes))
            return Array.Empty<byte>();
        int activeField = SocialFields.N(fieldNumber, "SubscribeMultipleChannelRequest", "active_channels", 1);
        int disabledField = SocialFields.N(fieldNumber, "SubscribeMultipleChannelRequest", "disabled_channels", 2);
        var payload = new List<ProtoField>();
        if (activeBytes.Length > 0)
            payload.Add(new ProtoField { Field = activeField, Wire = WireType.Len, Bytes = activeBytes });
        if (disabledBytes.Length > 0)
            payload.Add(new ProtoField { Field = disabledField, Wire = WireType.Len, Bytes = disabledBytes });
        payload.Sort((a, b) => a.Field.CompareTo(b.Field));
        return Request(code, payload, uid);
    }

    public static byte[] Prefix(byte[] gameMessage)
    {
        if (gameMessage.Length == 0) return Array.Empty<byte>();
        byte[] length = RawProtobuf.EncodeVarint((ulong)gameMessage.Length);
        var framed = new byte[length.Length + gameMessage.Length];
        Buffer.BlockCopy(length, 0, framed, 0, length.Length);
        Buffer.BlockCopy(gameMessage, 0, framed, length.Length, gameMessage.Length);
        return framed;
    }

    private static ProtoField Var(int field, long value)
        => new() { Field = field, Wire = WireType.Varint, Varint = unchecked((ulong)value) };

    private static bool BytesOf(IReadOnlyList<int> ids, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (ids.Count == 0) return true;
        var raw = new byte[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] is < 0 or > 255) return false;
            raw[i] = (byte)ids[i];
        }
        bytes = raw;
        return true;
    }

    private static byte[] Request(string code, List<ProtoField> payload, int uid)
    {
        try { return EnvelopeBuilder.BuildClientRequest(code, payload, uid); }
        catch (InvalidOperationException) { return Array.Empty<byte>(); }
        catch (FormatException) { return Array.Empty<byte>(); }
    }
}
