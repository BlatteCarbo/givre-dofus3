using System;
using System.Collections.Generic;
using System.Text;

namespace GivreClone.App.Services.Transport.Socket;

/// <summary>
/// Encodeur/décodeur protobuf MINIMAL et autonome pour le protocole de CONNEXION Dofus 3 (login / liste serveurs /
/// sélection). Volontairement séparé du catalogue de mapping du jeu (qui, lui, est streamé et renumérote aux patchs) :
/// le protocole de connexion est stable, un petit codec écrit à la main suffit et reste léger (objectif ressources).
/// Wire types utilisés : 0 = varint, 2 = length-delimited. Tag = (field &lt;&lt; 3) | wireType.
/// </summary>
internal static class ProtoWire
{
    // ---------- écriture ----------
    public sealed class Writer
    {
        private readonly List<byte> _b = new(64);

        public void Varint(ulong v)
        {
            while (v >= 0x80) { _b.Add((byte)(v | 0x80)); v >>= 7; }
            _b.Add((byte)v);
        }

        public void Tag(int field, int wireType) => Varint((ulong)((field << 3) | wireType));

        public void String(int field, string? s)
        {
            var bytes = Encoding.UTF8.GetBytes(s ?? "");
            Tag(field, 2);
            Varint((ulong)bytes.Length);
            _b.AddRange(bytes);
        }

        public void Int64(int field, long v) { Tag(field, 0); Varint((ulong)v); }
        public void Int32(int field, int v) { Tag(field, 0); Varint((ulong)(uint)v); }

        /// <summary>Champ imbriqué (message) : tag + longueur + corps.</summary>
        public void Message(int field, byte[] body)
        {
            Tag(field, 2);
            Varint((ulong)body.Length);
            _b.AddRange(body);
        }

        public byte[] ToArray() => _b.ToArray();

        /// <summary>Préfixe la trame par sa longueur en varint (format de trame du transport Dofus).</summary>
        public byte[] ToFramed()
        {
            var body = _b.ToArray();
            var framed = new Writer();
            framed.Varint((ulong)body.Length);
            framed._b.AddRange(body);
            return framed._b.ToArray();
        }
    }

    // ---------- lecture ----------
    public ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _s;
        private int _pos;

        public Reader(ReadOnlySpan<byte> s) { _s = s; _pos = 0; }

        public bool End => _pos >= _s.Length;
        public int Position => _pos;

        public ulong Varint()
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                byte b = _s[_pos++];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
            }
            return result;
        }

        /// <summary>Lit un tag ; renvoie (field, wireType).</summary>
        public (int Field, int Wire) Tag()
        {
            ulong t = Varint();
            return ((int)(t >> 3), (int)(t & 0x7));
        }

        public ReadOnlySpan<byte> Bytes()
        {
            int len = (int)Varint();
            var slice = _s.Slice(_pos, len);
            _pos += len;
            return slice;
        }

        public string String() => Encoding.UTF8.GetString(Bytes());

        /// <summary>Saute un champ selon son wire type (0 varint, 1 i64, 2 len, 5 i32).</summary>
        public void Skip(int wire)
        {
            switch (wire)
            {
                case 0: Varint(); break;
                case 1: _pos += 8; break;
                case 2: { int len = (int)Varint(); _pos += len; break; }
                case 5: _pos += 4; break;
                default: throw new InvalidOperationException($"wire type inconnu {wire}");
            }
        }
    }
}
