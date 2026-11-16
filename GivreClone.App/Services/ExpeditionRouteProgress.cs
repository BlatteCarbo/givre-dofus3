using System.Globalization;
using System.Text.RegularExpressions;

namespace GivreClone.App.Services;

/// <summary>Compte rendu local du script, jamais un ACK serveur ni un taux d'achèvement.</summary>
public readonly record struct ExpeditionRouteProgress(int Index, int Total, string Map)
{
    private static readonly Regex Pattern = new(@"^\[expedition-route\] (\d{1,6})/(\d{1,6}) map=(\d{1,19}|-?\d{1,5},-?\d{1,5})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParse(string message, out ExpeditionRouteProgress progress)
    {
        progress = default;
        if (message.Length > 100) return false;
        var match = Pattern.Match(message);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out int index)
            || !int.TryParse(match.Groups[2].Value, out int total) || index < 1 || index > total) return false;
        string map = match.Groups[3].Value;
        if (!map.Contains(',') && (!long.TryParse(map, NumberStyles.None, CultureInfo.InvariantCulture, out long id) || id <= 0)) return false;
        progress = new(index, total, map);
        return true;
    }
}
