using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GivreClone.App.Services.Eco;

/// <summary>Lecture d'une liste de prix collée (copiée d'un site ou d'un tableur) : une ligne = « nom » puis « prix » séparés par une tabulation,
/// « ; », « : » ou simplement des espaces. Accepte « 12 500 », « 12.500 », « 12,5k » (×1 000), « 1,2m » (×1 000 000). Pure.</summary>
public static class PriceImport
{
    private static readonly Regex NumberToken = new(@"^\s*(?<n>\d[\d\s  \.,]*)\s*(?<u>[kKmM]?)\s*(?:k|kamas?|€)?\s*$", RegexOptions.Compiled);

    public sealed record Line(string Name, decimal Price);

    public static IReadOnlyList<Line> Parse(string? text)
    {
        var res = new List<Line>();
        if (string.IsNullOrWhiteSpace(text)) return res;
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim().TrimEnd('\r');
            if (line.Length < 3) continue;
            if (TrySplit(line, out var name, out var price) && name.Length >= 2 && price > 0) res.Add(new Line(name, price));
        }
        return res;
    }

    private static bool TrySplit(string line, out string name, out decimal price)
    {
        name = ""; price = 0;
        // 1) séparateur explicite : tabulation, « ; » ou « : » (le dernier)
        foreach (var sep in new[] { '\t', ';', ':' })
        {
            int i = line.LastIndexOf(sep);
            if (i > 0 && TryNumber(line[(i + 1)..], out price)) { name = line[..i].Trim().Trim('"'); return true; }
        }
        // 2) dernier « mot » numérique (éventuellement « 12 500 » : jusqu'à trois groupes de chiffres)
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int take = Math.Min(3, parts.Length - 1); take >= 1; take--)
        {
            string tail = string.Join(" ", parts[^take..]);
            if (TryNumber(tail, out price)) { name = string.Join(" ", parts[..^take]).Trim('"'); return name.Length > 0; }
        }
        return false;
    }

    public static bool TryNumber(string text, out decimal value)
    {
        value = 0;
        var m = NumberToken.Match(text ?? "");
        if (!m.Success) return false;
        string n = Regex.Replace(m.Groups["n"].Value, @"[\s  ]", "");
        // « . » ou « , » suivis d'exactement 3 chiffres en fin de nombre = séparateur de milliers ; sinon décimal.
        n = Regex.Replace(n, @"[\.,](?=\d{3}(?:[\.,]\d{3})*$)", "");
        n = n.Replace(',', '.');
        if (!decimal.TryParse(n, NumberStyles.Number, CultureInfo.InvariantCulture, out value)) return false;
        string u = m.Groups["u"].Value;
        if (u.Length > 0) value *= char.ToLowerInvariant(u[0]) == 'k' ? 1_000m : 1_000_000m;
        return value >= 0 && value <= 10_000_000_000m;
    }
}
