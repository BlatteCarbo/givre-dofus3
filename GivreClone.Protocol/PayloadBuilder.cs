using System.Text;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>
/// Construit le payload (liste de ProtoField) d'un message a partir de valeurs nommees, en resolvant les
/// numeros de champ via la definition .proto (ProtoMessageDef). Piloter par NOM de champ -&gt; survit au
/// reshuffle des tags tant que le .proto est a jour. Types supportes : entiers, bool, string, repeated int.
/// </summary>
public sealed class PayloadBuilder
{
    private readonly ProtoMessageDef _def;
    private readonly List<ProtoField> _fields = new();

    public PayloadBuilder(ProtoMessageDef def) => _def = def;

    private ProtoFieldDef Resolve(string fieldName) =>
        _def.ByName.TryGetValue(fieldName, out var d)
            ? d
            : throw new InvalidOperationException($"Champ '{fieldName}' absent du message '{_def.Name}' (proto perime ?).");

    public PayloadBuilder Int(string field, long value)
    {
        var d = Resolve(field);
        _fields.Add(new ProtoField { Field = d.Number, Wire = WireType.Varint, Varint = unchecked((ulong)value) });
        return this;
    }

    public PayloadBuilder Bool(string field, bool value)
    {
        var d = Resolve(field);
        _fields.Add(new ProtoField { Field = d.Number, Wire = WireType.Varint, Varint = value ? 1UL : 0UL });
        return this;
    }

    public PayloadBuilder Str(string field, string value)
    {
        var d = Resolve(field);
        _fields.Add(new ProtoField { Field = d.Number, Wire = WireType.Len, String = value });
        return this;
    }

    /// <summary>repeated int (encode en champs varint repetes ; le serveur accepte packed OU non-packed).</summary>
    public PayloadBuilder Ints(string field, IEnumerable<int> values)
    {
        var d = Resolve(field);
        foreach (var v in values)
            _fields.Add(new ProtoField { Field = d.Number, Wire = WireType.Varint, Varint = unchecked((ulong)(long)v) });
        return this;
    }

    public IReadOnlyList<ProtoField> Build() => _fields;
}

/// <summary>Lecture typee simple d'un payload decode (acces par numero de champ).</summary>
public static class PayloadReader
{
    public static ProtoField? Field(IReadOnlyList<ProtoField> payload, int number)
    {
        foreach (var f in payload) if (f.Field == number) return f;
        return null;
    }

    public static long Int(IReadOnlyList<ProtoField> payload, int number, long fallback = 0)
    {
        var f = Field(payload, number);
        return f is null ? fallback : unchecked((long)f.Varint);
    }

    public static bool Bool(IReadOnlyList<ProtoField> payload, int number, bool fallback = false)
    {
        var f = Field(payload, number);
        return f is null ? fallback : f.Varint != 0;
    }

    // UTF-8 STRICT (lève sur séquence invalide) : sert de test « ces octets SONT-ils du texte ? ».
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Chaîne d'un champ. Le décodeur brut ne classe en String que l'ASCII imprimable (32-126) ; un texte
    /// ACCENTUÉ ("té" = 74 C3 A9) tombe donc en Bytes seul. On retente ici un décodage UTF-8 STRICT des octets bruts :
    /// valide -> la chaîne (accents inclus) ; invalide (vrai binaire) -> null (comportement d'avant). Corrige la perte des
    /// MP / noms accentués.</summary>
    public static string? Str(IReadOnlyList<ProtoField> payload, int number)
    {
        var f = Field(payload, number);
        if (f is null) return null;
        if (f.String is not null) return f.String;
        if (f.Bytes is { } b)
        {
            try { return StrictUtf8.GetString(b); } catch { return null; }
        }
        return null;
    }

    public static IEnumerable<int> Ints(IReadOnlyList<ProtoField> payload, int number)
    {
        foreach (var f in payload)
            if (f.Field == number && f.Wire == WireType.Varint)
                yield return unchecked((int)(long)f.Varint);
    }

    /// <summary>Toutes les chaînes d'un champ RÉPÉTÉ (repeated string). Même repli UTF-8 strict que <see cref="Str"/> pour
    /// récupérer les octets classés en Bytes (accents). Sert p.ex. à lire les paramètres d'un TextInformationEvent (lqn).</summary>
    public static IReadOnlyList<string> Strs(IReadOnlyList<ProtoField> payload, int number)
    {
        var list = new List<string>();
        foreach (var f in payload)
        {
            if (f.Field != number) continue;
            if (f.String is not null) { list.Add(f.String); continue; }
            if (f.Bytes is { } b) { try { list.Add(StrictUtf8.GetString(b)); } catch { /* vrai binaire -> ignoré */ } }
        }
        return list;
    }
}
