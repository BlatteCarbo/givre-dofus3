namespace GivreClone.Protocol;

/// <summary>Definition d'un champ protobuf (numero + nom + type + repeated), lue du .proto clair.</summary>
public sealed record ProtoFieldDef(int Number, string Name, string ProtoType, bool Repeated);

/// <summary>Definition d'un message : acces par numero de champ et par nom.</summary>
public sealed class ProtoMessageDef
{
    public string Name { get; }
    public IReadOnlyDictionary<int, ProtoFieldDef> ByNumber { get; }
    public IReadOnlyDictionary<string, ProtoFieldDef> ByName { get; }

    public ProtoMessageDef(string name, IEnumerable<ProtoFieldDef> fields)
    {
        Name = name;
        var byNum = new Dictionary<int, ProtoFieldDef>();
        var byName = new Dictionary<string, ProtoFieldDef>(StringComparer.Ordinal);
        foreach (var f in fields) { byNum[f.Number] = f; byName[f.Name] = f; }
        ByNumber = byNum;
        ByName = byName;
    }
}

/// <summary>
/// Parseur .proto TOLERANT (pas un vrai compilateur protobuf) : extrait, par message, la table
/// numero-de-champ -> (nom, type, repeated). Suffisant pour forger/lire les messages de controle et
/// d'etat. Les corps d'enum sont ignores ; les messages imbriques sont indexes par leur nom simple.
/// </summary>
public static class ProtoSchemaText
{
    public static IReadOnlyDictionary<string, ProtoMessageDef> Parse(string protoText)
    {
        var result = new Dictionary<string, ProtoMessageDef>(StringComparer.Ordinal);
        // pile de contextes : (kind, name, champs accumules). kind = "message" ou "enum".
        var stack = new Stack<(string Kind, string Name, List<ProtoFieldDef> Fields)>();

        foreach (var rawLine in protoText.Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0) continue;

            // ouverture de bloc : "message X {" / "enum X {" (l'accolade peut etre sur la ligne)
            if (TryReadBlockOpen(line, "message", out var mName))
            {
                stack.Push(("message", mName, new List<ProtoFieldDef>()));
                continue;
            }
            if (TryReadBlockOpen(line, "enum", out var eName))
            {
                stack.Push(("enum", eName, new List<ProtoFieldDef>()));
                continue;
            }
            if (line.StartsWith("oneof ", StringComparison.Ordinal) && line.EndsWith("{", StringComparison.Ordinal))
            {
                // un oneof s'ouvre : ses champs appartiennent au message englobant -> on empile un contexte transparent
                stack.Push(("oneof", "", new List<ProtoFieldDef>()));
                continue;
            }

            if (line.StartsWith("}", StringComparison.Ordinal))
            {
                if (stack.Count == 0) continue;
                var top = stack.Pop();
                if (top.Kind == "message")
                    result[top.Name] = new ProtoMessageDef(top.Name, top.Fields);
                else if (top.Kind == "oneof" && stack.Count > 0)
                    // remonte les champs du oneof dans le message parent
                    stack.Peek().Fields.AddRange(top.Fields);
                continue;
            }

            // champ : "[repeated|optional] <type> <name> = <num>;"  (ignore dans un enum)
            if (stack.Count == 0) continue;
            var ctx = stack.Peek();
            if (ctx.Kind == "enum") continue;
            if (TryReadField(line, out var fdef))
                ctx.Fields.Add(fdef);
        }

        return result;
    }

    private static string StripComment(string line)
    {
        int i = line.IndexOf("//", StringComparison.Ordinal);
        return i >= 0 ? line[..i] : line;
    }

    private static bool TryReadBlockOpen(string line, string keyword, out string name)
    {
        name = "";
        if (!line.StartsWith(keyword + " ", StringComparison.Ordinal)) return false;
        if (!line.EndsWith("{", StringComparison.Ordinal)) return false;
        var mid = line[(keyword.Length + 1)..^1].Trim();
        // mid = "Name" (parfois "Name =" pour des variantes ; on garde le 1er token)
        int sp = mid.IndexOfAny(new[] { ' ', '=', '\t' });
        name = sp >= 0 ? mid[..sp].Trim() : mid;
        return name.Length > 0;
    }

    private static bool TryReadField(string line, out ProtoFieldDef def)
    {
        def = null!;
        if (!line.EndsWith(";", StringComparison.Ordinal)) return false;
        int eq = line.IndexOf('=');
        if (eq < 0) return false;

        var left = line[..eq].Trim();
        var right = line[(eq + 1)..].TrimEnd(';', ' ', '\t');
        // numero (peut etre suivi de [options], qu'on ignore)
        int br = right.IndexOf('[');
        if (br >= 0) right = right[..br];
        if (!int.TryParse(right.Trim(), out int number)) return false;

        // left = "[repeated|optional] <type> <name>"
        var tokens = left.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2) return false;
        bool repeated = false;
        int idx = 0;
        if (tokens[idx] is "repeated" or "optional") { repeated = tokens[idx] == "repeated"; idx++; }
        if (tokens.Length - idx < 2) return false;
        // le type peut contenir un point (map<..> non gere ici -> ignore proprement)
        string protoType = tokens[idx];
        string name = tokens[idx + 1];
        if (protoType.StartsWith("map<", StringComparison.Ordinal)) return false;
        def = new ProtoFieldDef(number, name, protoType, repeated);
        return true;
    }
}
