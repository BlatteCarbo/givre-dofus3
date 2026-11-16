using System;
using System.Collections.Generic;

namespace GivreClone.Protocol;

/// <summary>Aides PURES des verrous à code (coffres / maisons, Lot J) : normalisation du code, variantes à essayer (brut
/// puis complété par '_'), correspondance propriétaire, découpe des valeurs de route. Testables sans l'App.</summary>
public static class LockCode
{
    public const int DefaultSize = 8;

    /// <summary>"-1" / "" / null -&gt; <paramref name="code"/> = null (pas de code : propriétaire / non verrouillé), true.
    /// Sinon code rogné ; invalide (plus de <see cref="DefaultSize"/> caractères, contient '_' ou un espace) -&gt; false
    /// (ne lève jamais).</summary>
    public static bool TryNormalize(string? raw, out string? code)
    {
        code = null;
        string t = (raw ?? "").Trim();
        if (t.Length == 0 || t == "-1") return true;
        if (t.Length > DefaultSize) return false;
        foreach (char c in t) if (c == '_' || char.IsWhiteSpace(c) || char.IsControl(c)) return false;
        code = t;
        return true;
    }

    /// <summary>Variantes à essayer, dans l'ordre : [brut] puis [brut complété par '_' jusqu'à codeSize] s'il est plus court
    ///. preferPadded=true inverse l'ordre (variante validée retenue par le moteur).
    /// codeSize &lt;= 0 (code_size omis) -&gt; <see cref="DefaultSize"/>.</summary>
    public static IReadOnlyList<string> Variants(string code, int codeSize, bool preferPadded)
    {
        int size = codeSize <= 0 ? DefaultSize : codeSize;
        if (code.Length >= size) return new[] { code };
        string padded = code.PadRight(size, '_');
        return preferPadded ? new[] { padded, code } : new[] { code, padded };
    }

    /// <summary>"Nick" (pseudo, insensible à la casse) ou "Nick#1234" (pseudo + tag).</summary>
    public static bool OwnerMatches(HouseInstanceInfo i, string owner)
    {
        string o = (owner ?? "").Trim();
        if (o.Length == 0) return false;
        int h = o.IndexOf('#');
        string nick = h >= 0 ? o[..h].Trim() : o;
        if (!string.Equals(i.Nickname, nick, StringComparison.OrdinalIgnoreCase)) return false;
        if (h < 0) return true;
        return string.Equals(i.Tag, o[(h + 1)..].Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Valeur de route "a|b[|c]" ou "a;b[;c]" -&gt; parties rognées. false si moins de 2 parties.</summary>
    public static bool TrySplitSpec(string raw, out string[] parts)
    {
        parts = (raw ?? "").Split(new[] { '|', ';' }, StringSplitOptions.None);
        for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
        return parts.Length >= 2;
    }
}
