using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace GivreClone.App.Services.Equipment;

/// <summary>Une caractéristique cumulée de l'équipement : libellé (« Vitalité », « Résistance Air »), total signé, en pourcentage ou non.</summary>
public sealed record StatTotal(string Label, long Total, bool Percent)
{
    public string Text => (Total > 0 ? "+" : "") + Total.ToString(CultureInfo.InvariantCulture) + (Percent ? "%" : "") + " " + Label;
}

/// <summary>Total des caractéristiques d'un équipement (idée du « set builder » de DofusLab, réécrite à partir des lignes d'infobulle du jeu) : on additionne, par libellé, les lignes
/// « 143 Vitalité » de chaque objet porté ; un malus (rouge dans le jeu) se retranche. Les bonus de panoplie ne sont pas inclus. Pur.</summary>
public static partial class BuildStats
{
    [GeneratedRegex(@"^\s*[+-]?(\d+)(?:\s*(?:à|a|-|–)\s*(\d+))?\s*(%?)\s+(.+?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Line();

    /// <summary>Additionne les lignes d'effets (texte, malus) de tous les objets portés. Les lignes sans valeur (« Échangeable le … », dégâts d'arme non chiffrés…) sont ignorées ;
    /// une plage « 10 à 15 » compte pour sa valeur haute. Ordre = première apparition ; les totaux nuls sont retirés.</summary>
    public static IReadOnlyList<StatTotal> Aggregate(IEnumerable<(string Text, bool Negative)> lines)
    {
        var order = new List<(string Label, bool Percent)>();
        var sums = new Dictionary<(string, bool), long>();
        foreach (var (text, negative) in lines)
        {
            var m = Line().Match(text);
            if (!m.Success) continue;
            long v = long.Parse(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (negative) v = -v;
            var key = (m.Groups[4].Value, m.Groups[3].Length > 0);
            if (!sums.ContainsKey(key)) { order.Add(key); sums[key] = 0; }
            sums[key] += v;
        }
        return order.Where(k => sums[k] != 0).Select(k => new StatTotal(k.Label, sums[k], k.Percent)).ToList();
    }
}
