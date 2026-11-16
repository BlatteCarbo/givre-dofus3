using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services.LocalLaunch;

/// <summary>
/// Thrift « binary protocol » strict, juste ce que le Zaap local de Dofus emploie : messages dont les arguments sont des chaînes,
/// des i32 ou des booléens, réponses à un seul champ « success ». Sans paquet externe. Réimplémentation de ZaapServer.cs de ZaapEmu.
/// </summary>
public static class ThriftBinary
{
    public const byte TStop = 0, TBool = 2, TI32 = 8, TString = 11;
    private const int Version1 = unchecked((int)0x80010000);
    public const int Call = 1, Reply = 2;

    public sealed record Message(string Name, int SeqId, int Type, IReadOnlyDictionary<short, object?> Args);

    private static async Task<byte[]> ReadAsync(Stream s, int n, CancellationToken ct)
    {
        var b = new byte[n];
        await s.ReadExactlyAsync(b, ct).ConfigureAwait(false);
        return b;
    }
    private static async Task<int> ReadI32Async(Stream s, CancellationToken ct)
    {
        var b = await ReadAsync(s, 4, ct).ConfigureAwait(false);
        return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
    }
    private static async Task<string> ReadStringAsync(Stream s, CancellationToken ct)
    {
        int len = await ReadI32Async(s, ct).ConfigureAwait(false);
        if (len is < 0 or > 1 << 20) throw new InvalidDataException("Chaîne Thrift hors limites.");
        return Encoding.UTF8.GetString(await ReadAsync(s, len, ct).ConfigureAwait(false));
    }

    /// <summary>Lit un message (appel) : en-tête strict, puis la structure d'arguments jusqu'à Stop. Type de champ inconnu = erreur.</summary>
    public static async Task<Message> ReadMessageAsync(Stream s, CancellationToken ct)
    {
        int head = await ReadI32Async(s, ct).ConfigureAwait(false);
        if ((head & unchecked((int)0xFFFF0000)) != Version1) throw new InvalidDataException("En-tête Thrift non strict.");
        int type = head & 0xFF;
        string name = await ReadStringAsync(s, ct).ConfigureAwait(false);
        int seq = await ReadI32Async(s, ct).ConfigureAwait(false);
        var args = new Dictionary<short, object?>();
        while (true)
        {
            byte t = (await ReadAsync(s, 1, ct).ConfigureAwait(false))[0];
            if (t == TStop) break;
            var idb = await ReadAsync(s, 2, ct).ConfigureAwait(false);
            short id = (short)((idb[0] << 8) | idb[1]);
            args[id] = t switch
            {
                TString => await ReadStringAsync(s, ct).ConfigureAwait(false),
                TI32 => await ReadI32Async(s, ct).ConfigureAwait(false),
                TBool => (await ReadAsync(s, 1, ct).ConfigureAwait(false))[0] != 0,
                _ => throw new InvalidDataException($"Type de champ Thrift {t} non géré."),
            };
        }
        return new Message(name, seq, type, args);
    }

    private static void WriteI32(Stream s, int v) { Span<byte> b = stackalloc byte[4]; b[0] = (byte)(v >> 24); b[1] = (byte)(v >> 16); b[2] = (byte)(v >> 8); b[3] = (byte)v; s.Write(b); }
    private static void WriteString(Stream s, string v) { var d = Encoding.UTF8.GetBytes(v); WriteI32(s, d.Length); s.Write(d); }

    /// <summary>Écrit la réponse : structure « résultat » avec un champ 0 « success » (chaîne, i32 ou booléen), ou vide pour void/null.</summary>
    public static async Task WriteReplyAsync(Stream s, string name, int seqId, object? result, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        WriteI32(ms, Version1 | Reply);
        WriteString(ms, name);
        WriteI32(ms, seqId);
        switch (result)
        {
            case string str: ms.WriteByte(TString); ms.WriteByte(0); ms.WriteByte(0); WriteString(ms, str); break;
            case bool b: ms.WriteByte(TBool); ms.WriteByte(0); ms.WriteByte(0); ms.WriteByte(b ? (byte)1 : (byte)0); break;
            case int i: ms.WriteByte(TI32); ms.WriteByte(0); ms.WriteByte(0); WriteI32(ms, i); break;
        }
        ms.WriteByte(TStop);
        await s.WriteAsync(ms.ToArray(), ct).ConfigureAwait(false);
        await s.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Encode un appel (pour les tests et les clients de vérification) : arguments par numéro de champ croissant.</summary>
    public static byte[] EncodeCall(string name, int seqId, IReadOnlyDictionary<short, object> args)
    {
        using var ms = new MemoryStream();
        WriteI32(ms, Version1 | Call);
        WriteString(ms, name);
        WriteI32(ms, seqId);
        foreach (var kv in new SortedDictionary<short, object>(new Dictionary<short, object>(args)))
        {
            switch (kv.Value)
            {
                case string s: ms.WriteByte(TString); ms.WriteByte((byte)(kv.Key >> 8)); ms.WriteByte((byte)kv.Key); WriteString(ms, s); break;
                case int i: ms.WriteByte(TI32); ms.WriteByte((byte)(kv.Key >> 8)); ms.WriteByte((byte)kv.Key); WriteI32(ms, i); break;
                case bool b: ms.WriteByte(TBool); ms.WriteByte((byte)(kv.Key >> 8)); ms.WriteByte((byte)kv.Key); ms.WriteByte(b ? (byte)1 : (byte)0); break;
                default: throw new ArgumentException("Type d'argument non géré.");
            }
        }
        ms.WriteByte(TStop);
        return ms.ToArray();
    }
}
