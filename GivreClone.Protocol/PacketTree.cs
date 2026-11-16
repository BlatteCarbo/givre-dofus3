using System.Text;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>
/// Convertit un payload décodé (arbre protobuf brut) en un arbre NEUTRE (<c>Dictionary&lt;string, object?&gt;</c>,
/// <c>List&lt;object?&gt;</c>, <c>long</c>, <c>double</c>, <c>bool</c>, <c>string</c>) clé par NOM de champ. Sert aux hooks
/// de paquets Lua (registerMessage/waitFor) : aucune dépendance MoonSharp, pur, testable.
///
/// Noms de champ = mapping (fields.json / overlay, via <see cref="ProtocolCatalog.FieldName"/>) ; TYPE = .proto clair,
/// cherché PAR NOM (ses numéros sont ceux de la beta, faux sur le fil). Le parseur brut devine String/Message sur les Len
/// (un "hi" ressort en Message) : on décode donc selon le TYPE du schéma, jamais selon la devinette du parseur.
///
/// Pièges documentés : proto3 n'émet pas les valeurs par défaut (0/false/"") -> clé ABSENTE ; champs non schématisés ->
/// clé <c>f&lt;n&gt;</c> ; noms obfusqués (ex. <c>fqgz</c>) tels quels.
/// </summary>
public static class PacketTree
{
    public sealed record Limits(int MaxDepth = 8, int MaxNodes = 2000, int MaxArray = 500, int MaxString = 4096);

    private static readonly Limits Default = new();
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static Dictionary<string, object?> Build(ProtocolCatalog? cat, string? messageName,
                                                    IReadOnlyList<ProtoField> payload, bool outgoing = false, Limits? limits = null)
    {
        try
        {
            var ctx = new Ctx(cat, limits ?? Default);
            var root = ctx.BuildMessage(messageName, payload, 0);
            if (ctx.Truncated) root["__truncated"] = true;
            root["__name"] = messageName ?? "";
            root["__dir"] = outgoing ? "out" : "in";
            return root;
        }
        catch
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["__name"] = messageName ?? "",
                ["__dir"] = outgoing ? "out" : "in",
                ["__error"] = true,
            };
        }
    }

    private sealed class Ctx
    {
        private readonly ProtocolCatalog? _cat;
        private readonly Limits _lim;
        private int _budget;
        public bool Truncated;

        public Ctx(ProtocolCatalog? cat, Limits lim) { _cat = cat; _lim = lim; _budget = Math.Max(0, lim.MaxNodes); }

        private bool Take()
        {
            if (_budget <= 0) { Truncated = true; return false; }
            _budget--;
            return true;
        }

        public Dictionary<string, object?> BuildMessage(string? msgName, IReadOnlyList<ProtoField> fields, int depth)
        {
            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            var order = new List<int>();
            var groups = new Dictionary<int, List<ProtoField>>();
            foreach (var f in fields)
            {
                if (!groups.TryGetValue(f.Field, out var g)) { groups[f.Field] = g = new List<ProtoField>(); order.Add(f.Field); }
                g.Add(f);
            }
            var def = msgName is null ? null : _cat?.Message(msgName);
            foreach (int num in order)
            {
                if (_budget <= 0) { Truncated = true; break; }
                var grp = groups[num];
                string? known = msgName is null ? null : _cat?.FieldName(msgName, num);
                string key = known ?? "f" + num;
                ProtoFieldDef? fd = known is not null && def is not null && def.ByName.TryGetValue(known, out var d) ? d : null;
                string? type = fd is null ? null : SimpleType(fd.ProtoType);
                // En édition locale le .proto clair n'est pas livré. Pour ce seul membre
                // structurel connu, le lecteur PartyReader consomme déjà les mêmes
                // Character imbriqués via fields.json. Sans ce type, les clés deviennent
                // f4/f3 et les hooks Lua perdent le nom du personnage.
                if (type is null && msgName == "PartyJoinEvent" && known == "members"
                    && _cat?.HasFields("Character") == true)
                    type = "Character";
                bool repeated = fd?.Repeated ?? grp.Count > 1;
                var values = new List<object?>();
                foreach (var f in grp)
                {
                    Convert(f, type, known, depth, values);
                    if (values.Count >= _lim.MaxArray || _budget <= 0) break;
                }
                if (values.Count == 0) continue;
                if (repeated)
                {
                    if (values.Count > _lim.MaxArray) values.RemoveRange(_lim.MaxArray, values.Count - _lim.MaxArray);
                    dict[key] = values;
                }
                else dict[key] = values[0];
            }
            return dict;
        }

        private static string SimpleType(string protoType)
        {
            var t = protoType.TrimStart('.');
            int i = t.LastIndexOf('.');
            return i >= 0 ? t[(i + 1)..] : t;
        }

        private bool IsMessageType(string type) => _cat is not null && (_cat.Message(type) is not null || _cat.HasFields(type));

        // Convertit UNE occurrence (un Len packed peut en produire plusieurs) et l'ajoute à `into`.
        private void Convert(ProtoField f, string? type, string? key, int depth, List<object?> into)
        {
            if (type is null) { Heuristic(f, key, depth, into); return; }
            switch (type)
            {
                case "bytes": return;   // binaire opaque : pas de clé
                case "string":
                    if (f.Wire != WireType.Len) break;
                    if (Str(f) is { } s && Take()) into.Add(Cap(s));
                    return;
                case "bool":
                    if (f.Wire == WireType.Varint) { if (Take()) into.Add(f.Varint != 0); return; }
                    if (f.Wire == WireType.Len) { foreach (var v in Packed(f)) { if (!Take() || into.Count >= _lim.MaxArray) return; into.Add(v != 0); } return; }
                    break;
                case "int32": case "int64":
                    if (f.Wire == WireType.Varint) { if (Take()) into.Add(unchecked((long)f.Varint)); return; }
                    if (f.Wire == WireType.Len) { foreach (var v in Packed(f)) { if (!Take() || into.Count >= _lim.MaxArray) return; into.Add(unchecked((long)v)); } return; }
                    break;
                case "uint32": case "uint64":
                    if (f.Wire == WireType.Varint) { if (Take()) into.Add((double)f.Varint); return; }
                    if (f.Wire == WireType.Len) { foreach (var v in Packed(f)) { if (!Take() || into.Count >= _lim.MaxArray) return; into.Add((double)v); } return; }
                    break;
                case "sint32": case "sint64":
                    if (f.Wire == WireType.Varint) { if (Take()) into.Add(f.ZigZag); return; }
                    if (f.Wire == WireType.Len) { foreach (var v in Packed(f)) { if (!Take() || into.Count >= _lim.MaxArray) return; into.Add((long)(v >> 1) ^ -(long)(v & 1)); } return; }
                    break;
                case "double":
                    if (f.Wire == WireType.I64) { if (Take()) into.Add(f.AsDouble); return; }
                    if (f.Wire == WireType.Len) { foreach (var b in Fixed(f, 8)) { if (!Take() || into.Count >= _lim.MaxArray) return; into.Add(BitConverter.ToDouble(b)); } return; }
                    break;
                case "float":
                    if (f.Wire == WireType.I32) { if (Take()) into.Add((double)f.AsFloat); return; }
                    if (f.Wire == WireType.Len) { foreach (var b in Fixed(f, 4)) { if (!Take() || into.Count >= _lim.MaxArray) return; into.Add((double)BitConverter.ToSingle(b)); } return; }
                    break;
                case "fixed32":
                    if (f.Wire == WireType.I32) { if (Take()) into.Add((double)f.AsU32); return; }
                    if (f.Wire == WireType.Len) { foreach (var b in Fixed(f, 4)) { if (!Take() || into.Count >= _lim.MaxArray) return; into.Add((double)BitConverter.ToUInt32(b)); } return; }
                    break;
                case "fixed64":
                    if (f.Wire == WireType.I64) { if (Take()) into.Add((double)f.AsU64); return; }
                    if (f.Wire == WireType.Len) { foreach (var b in Fixed(f, 8)) { if (!Take() || into.Count >= _lim.MaxArray) return; into.Add((double)BitConverter.ToUInt64(b)); } return; }
                    break;
                case "sfixed32":
                    if (f.Wire == WireType.I32) { if (Take()) into.Add((long)f.AsI32); return; }
                    if (f.Wire == WireType.Len) { foreach (var b in Fixed(f, 4)) { if (!Take() || into.Count >= _lim.MaxArray) return; into.Add((long)BitConverter.ToInt32(b)); } return; }
                    break;
                case "sfixed64":
                    if (f.Wire == WireType.I64) { if (Take()) into.Add(f.AsI64); return; }
                    if (f.Wire == WireType.Len) { foreach (var b in Fixed(f, 8)) { if (!Take() || into.Count >= _lim.MaxArray) return; into.Add(BitConverter.ToInt64(b)); } return; }
                    break;
                default:
                    if (IsMessageType(type))
                    {
                        if (f.Wire != WireType.Len) break;   // type du schéma incohérent avec le fil -> heuristique
                        AddSub(f, type, depth, into);
                        return;
                    }
                    // enum (type non scalaire, pas un message connu) : valeur numérique
                    if (f.Wire == WireType.Varint) { if (Take()) into.Add(unchecked((long)f.Varint)); return; }
                    if (f.Wire == WireType.Len) { foreach (var v in Packed(f)) { if (!Take() || into.Count >= _lim.MaxArray) return; into.Add(unchecked((long)v)); } return; }
                    break;
            }
            // Type du schéma incompatible avec le type wire reçu (schéma beta, homonyme imbriqué...) : heuristique.
            Heuristic(f, key, depth, into);
        }

        private void Heuristic(ProtoField f, string? key, int depth, List<object?> into)
        {
            switch (f.Wire)
            {
                case WireType.Varint: if (Take()) into.Add(unchecked((long)f.Varint)); return;
                case WireType.I32: if (Take()) into.Add((long)f.AsI32); return;
                case WireType.I64: if (Take()) into.Add(f.AsI64); return;
                case WireType.Len:
                    // Nom de champ connu + sous-message parsable : type deviné par convention (snake_case -> PascalCase),
                    // s'il existe dans le mapping (ex. character_basic_information -> CharacterBasicInformation).
                    if (key is not null && f.Message is not null && Pascal(key) is { } guess && IsMessageType(guess))
                    { AddSub(f, guess, depth, into); return; }
                    if (TextOrNull(f) is { } s) { if (Take()) into.Add(s); return; }
                    if (f.Message is not null) { AddSub(f, null, depth, into); return; }
                    return;
            }
        }

        private void AddSub(ProtoField f, string? type, int depth, List<object?> into)
        {
            if (depth >= _lim.MaxDepth) return;
            IReadOnlyList<ProtoField> sub = f.Message ?? (f.Bytes is { Length: > 0 } b ? RawProtobuf.Parse(b) : new List<ProtoField>());
            if (!Take()) return;
            into.Add(BuildMessage(type, sub, depth + 1));
        }

        private string Cap(string s) => s.Length > _lim.MaxString ? s[.._lim.MaxString] : s;

        // Chaîne d'un Len selon le SCHÉMA : String du parseur (ASCII) sinon UTF-8 strict des octets bruts (accents).
        private static string? Str(ProtoField f)
        {
            if (f.String is not null) return f.String;
            if (f.Bytes is { } b) { try { return StrictUtf8.GetString(b); } catch { return null; } }
            return f.Message is null ? "" : null;
        }

        // Heuristique : texte UTF-8 strict SANS caractère de contrôle et de taille raisonnable, sinon null.
        private string? TextOrNull(ProtoField f)
        {
            string? s = f.String;
            if (s is null && f.Bytes is { } b) { try { s = StrictUtf8.GetString(b); } catch { s = null; } }
            if (s is null || s.Length > _lim.MaxString) return null;
            foreach (char c in s) if (c < 0x20) return null;
            return s;
        }

        private static IEnumerable<ulong> Packed(ProtoField f)
        {
            var b = f.Bytes;
            if (b is null || b.Length == 0) yield break;
            int pos = 0;
            while (pos < b.Length)
            {
                if (!RawProtobuf.TryReadVarint(b, ref pos, out ulong v)) yield break;
                yield return v;
            }
        }

        private static IEnumerable<byte[]> Fixed(ProtoField f, int size)
        {
            var b = f.Bytes;
            if (b is null || b.Length == 0 || b.Length % size != 0) yield break;
            for (int i = 0; i < b.Length; i += size) yield return b[i..(i + size)];
        }

        private static string? Pascal(string snake)
        {
            if (snake.Length == 0) return null;
            var sb = new StringBuilder(snake.Length);
            bool up = true;
            foreach (char c in snake)
            {
                if (c == '_') { up = true; continue; }
                sb.Append(up ? char.ToUpperInvariant(c) : c);
                up = false;
            }
            return sb.Length == 0 ? null : sb.ToString();
        }
    }
}
