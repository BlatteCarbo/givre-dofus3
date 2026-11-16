using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace GivreClone.App.Services.Eco;

/// <summary>Recherche locale : chaque mot peut apparaître dans un champ différent, accents ignorés.</summary>
public static class NotebookSearch
{
    private static string Normalize(string text)
    {
        var result = new StringBuilder();
        foreach (char c in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                result.Append(char.ToUpperInvariant(c));
        return result.ToString();
    }

    public static bool Matches(string query, params string[] fields)
    {
        string haystack = Normalize(string.Join(" ", fields));
        return Normalize(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).All(haystack.Contains);
    }
}
