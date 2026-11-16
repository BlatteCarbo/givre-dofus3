using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace GivreClone.App.Services.Treasure;

/// <summary>Un indice de chasse au trésor : sa position, sa distance depuis le point de départ et son libellé français.</summary>
public sealed record TreasureClue(int Id, int X, int Y, int Distance, string Name);

public sealed record TreasurePage(int Total, int Skip, int Count, IReadOnlyList<TreasureClue> Clues);

/// <summary>Chasse au trésor, partie pure : directions, requête DofusDB (<c>/treasure-hunt?x&amp;y&amp;direction</c>), lecture VALIDÉE de la réponse et recherche d'un
/// indice par son nom. Le format de la requête et les contrôles de cohérence s'inspirent de dofus-miniatures (avec l'accord de son auteur).
/// Directions du jeu : 0 = droite (est), 2 = bas (sud), 4 = gauche (ouest), 6 = haut (nord) ; l'API renvoie les cartes alignées jusqu'à 10 pas.</summary>
public static class TreasureClues
{
    public const int MaxDistance = 10, PageSize = 50, MaxTotal = 500, Limit = 1000;

    public static bool TryDirection(string? text, out int direction)
    {
        direction = -1;
        switch ((text ?? "").Trim().ToLowerInvariant())
        {
            case "0" or "right" or "east" or "e" or "droite" or "est": direction = 0; break;
            case "2" or "bottom" or "down" or "south" or "s" or "bas" or "sud": direction = 2; break;
            case "4" or "left" or "west" or "w" or "o" or "gauche" or "ouest": direction = 4; break;
            case "6" or "top" or "up" or "north" or "n" or "haut" or "nord": direction = 6; break;
            default: return false;
        }
        return true;
    }

    public static string DirectionName(int d) => d switch { 0 => "right", 2 => "bottom", 4 => "left", 6 => "top", _ => "?" };

    public static bool IsValid(int x, int y, int direction)
        => x is >= -Limit and <= Limit && y is >= -Limit and <= Limit && direction is 0 or 2 or 4 or 6;

    public static string RequestPath(int x, int y, int direction, int skip)
    {
        if (!IsValid(x, y, direction) || skip is < 0 or > MaxTotal) throw new ArgumentException("Coordonnées ou direction invalides.");
        return $"/treasure-hunt?x={x}&y={y}&direction={direction}&%24limit={PageSize}&%24skip={skip}&lang=fr";
    }

    /// <summary>Lit une page de la réponse. Contrôle le sens et la distance sur l'axe demandé, sans exclure les cartes décalées latéralement.</summary>
    public static TreasurePage ParsePage(string json, int x, int y, int direction, int skip)
    {
        if (json.Length > 1024 * 1024 || !IsValid(x, y, direction)) throw new FormatException("Réponse DofusDB trop volumineuse.");
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            int total = Int(root.GetProperty("total"), 0, MaxTotal), pageSkip = Int(root.GetProperty("skip"), 0, MaxTotal);
            var data = root.GetProperty("data");
            if (data.ValueKind != JsonValueKind.Array || pageSkip != skip || data.GetArrayLength() > PageSize || skip + data.GetArrayLength() > total)
                throw new FormatException("Réponse DofusDB incohérente.");
            var clues = new List<TreasureClue>();
            foreach (var map in data.EnumerateArray())
            {
                int mx = Int(map.GetProperty("posX"), -Limit, Limit), my = Int(map.GetProperty("posY"), -Limit, Limit);
                int dx = mx - x, dy = my - y;
                int distance = Int(map.GetProperty("distance"), 1, MaxDistance);
                int progress = direction switch { 0 => dx, 2 => dy, 4 => -dx, _ => -dy };
                int lateral = direction is 0 or 4 ? dy : dx;
                // Some directional paths shift sideways (observed in the Cania desert).
                // The API distance measures progress on the requested axis, not Manhattan distance.
                if (distance != progress || Math.Abs(lateral) > MaxDistance) throw new FormatException("Position d'indice DofusDB invalide.");
                var pois = map.GetProperty("pois");
                if (pois.ValueKind != JsonValueKind.Array || pois.GetArrayLength() > 128) throw new FormatException("Liste d'indices invalide.");
                foreach (var poi in pois.EnumerateArray())
                {
                    string name = poi.GetProperty("name").GetProperty("fr").GetString() ?? "";
                    if (name.Length is 0 or > 256 || name.Any(c => c < 32 || c == '<' || c == '>')) throw new FormatException("Libellé d'indice invalide.");
                    clues.Add(new TreasureClue(Int(poi.GetProperty("id"), 1, int.MaxValue), mx, my, distance, name));
                }
            }
            if (data.GetArrayLength() == 0 && skip < total) throw new FormatException("Pagination DofusDB interrompue.");
            return new TreasurePage(total, pageSkip, data.GetArrayLength(), clues);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { throw new FormatException("Réponse DofusDB illisible.", e); }
    }

    private static int Int(JsonElement v, int min, int max)
    {
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt64(out var n) || n < min || n > max) throw new FormatException("Nombre DofusDB invalide.");
        return (int)n;
    }

    private const string From = "àâäáãåçéèêëíìîïñóòôöõúùûüýÿœæ’`´";
    private const string To = "aaaaaaceeeeiiiinooooouuuuyyoa'''";

    /// <summary>Clé de comparaison : minuscules, sans accents, apostrophes unifiées, espaces simples (indépendante de la culture).</summary>
    public static string SearchKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var sb = new StringBuilder(text.Length);
        bool space = false;
        foreach (char raw in text.Trim())
        {
            char c = char.ToLowerInvariant(raw);
            int i = From.IndexOf(c);
            if (i >= 0) c = To[i];
            if (char.IsWhiteSpace(c) || c == ' ') { space = true; continue; }
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Cartes où l'indice se trouve, de la plus proche à la plus lointaine : nom identique d'abord, sinon nom qui le contient. Une même carte n'apparaît qu'une fois.</summary>
    public static IReadOnlyList<TreasureClue> Matches(IEnumerable<TreasureClue> clues, string? name)
    {
        string key = SearchKey(name);
        if (key.Length == 0) return Array.Empty<TreasureClue>();
        var all = clues.ToList();
        var exact = all.Where(c => SearchKey(c.Name) == key).ToList();
        var pick = exact.Count > 0 ? exact : all.Where(c => SearchKey(c.Name).Contains(key, StringComparison.Ordinal)).ToList();
        return pick.GroupBy(c => (c.X, c.Y)).Select(g => g.First()).OrderBy(c => c.Distance).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>Liste des indices distincts (plus proche occurrence), triés par nom — pour proposer un choix à l'écran.</summary>
    public static IReadOnlyList<TreasureClue> Distinct(IEnumerable<TreasureClue> clues)
        => clues.GroupBy(c => c.Id).Select(g => g.OrderBy(c => c.Distance).First()).OrderBy(c => SearchKey(c.Name), StringComparer.Ordinal).ToList();
}
