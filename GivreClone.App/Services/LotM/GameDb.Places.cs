using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GivreClone.Protocol;

namespace GivreClone.App.Services.LotM;

public sealed record PlaceOrigin(int X, int Y, int WorldMapId);
public sealed record PlaceDoor(int Cell, long MapId)
{
    public static IReadOnlyList<PlaceDoor> FromTransitions(IEnumerable<MapTransition> transitions)
        => transitions.Where(t => t.Type == 32 && t.Criterion is null)
                      .Select(t => new PlaceDoor(t.CellId, t.Arrival)).ToArray();
}
public sealed record WorldPlace(string Name, string Category, long MapId, string Coords, string SubArea,
                                string Area, string Layer, int? Distance, IReadOnlyList<PlaceDoor> Doors);

public static class PlaceText
{
    public static string Normalize(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (char original in text)
        {
            char c = char.ToLowerInvariant(original);
            result.Append(c switch {
                'à' or 'á' or 'â' or 'ã' or 'ä' or 'å' => "a", 'æ' => "ae", 'ç' => "c",
                'è' or 'é' or 'ê' or 'ë' => "e", 'ì' or 'í' or 'î' or 'ï' => "i", 'ñ' => "n",
                'ò' or 'ó' or 'ô' or 'õ' or 'ö' => "o", 'ù' or 'ú' or 'û' or 'ü' => "u",
                'ý' or 'ÿ' => "y", 'œ' => "oe", '’' => "'", _ => c.ToString(),
            });
        }
        return result.ToString().Trim();
    }
}

public sealed partial class GameDb
{
    private sealed record PlaceHint(string Name, string Category, long MapId, int X, int Y,
                                    string SubArea, string Area, int WorldMapId, string Layer);
    private PlaceHint[]? _placeHints;
    private readonly object _placeLoadGate = new();

    private PlaceHint[]? PlaceHints()
    {
        lock (_placeLoadGate)
        {
            if (!IsKnownTable("HintData")) return null;
            return _placeHints ??= Query("select nameId, categoryId, mapId, x, y, subareaId, worldMapId from HintData")
                .Select(r => {
                    var sub = IsKnownTable("SubAreaData") && IsKnownTable("AreaData") ? SubArea(I(r[5])) : null;
                    return new PlaceHint(Text(L(r[0])) ?? "", NameOf("HintCategoryData", L(r[1])), L(r[2]),
                        I(r[3]), I(r[4]), sub?.Name ?? "", sub?.Area ?? "", I(r[6]), NameOf("WorldMapData", L(r[6])));
                }).ToArray();
        }
    }

    public IReadOnlyList<WorldPlace>? Places(string query, PlaceOrigin? origin, int max = 10,
                                             Func<long, IReadOnlyList<PlaceDoor>>? doors = null)
    {
        if (PlaceHints() is not { } hints) return null;
        var words = PlaceText.Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool Matches(string value)
        {
            var normalized = PlaceText.Normalize(value);
            return words.All(w => normalized.Contains(w, StringComparison.Ordinal));
        }
        int? Distance(PlaceHint hint) => origin is not null && hint.WorldMapId == origin.WorldMapId
            ? Math.Abs(hint.X - origin.X) + Math.Abs(hint.Y - origin.Y) : null;
        return hints.Where(h => Matches(h.Name) || Matches(h.Category))
            .Select(h => (Hint: h, Distance: Distance(h)))
            .OrderBy(p => p.Distance ?? int.MaxValue).ThenBy(p => p.Hint.Name, StringComparer.CurrentCulture)
            .Take(max < 1 ? 10 : Math.Min(max, 50))
            .Select(p => new WorldPlace(p.Hint.Name, p.Hint.Category, p.Hint.MapId,
                FormattableString.Invariant($"{p.Hint.X},{p.Hint.Y}"), p.Hint.SubArea, p.Hint.Area, p.Hint.Layer,
                p.Distance, doors?.Invoke(p.Hint.MapId) ?? Array.Empty<PlaceDoor>())).ToArray();
    }

    /// <summary>Indices HintData des cartes demandées. Null si la table est absente. Les noms viennent des traductions.</summary>
    public IReadOnlyList<MapHintRow>? MapHints(IEnumerable<long> mapIds)
    {
        if (!IsKnownTable("HintData") || !IsKnownTable("HintCategoryData")) return null;
        var ids = new List<long>();
        if (mapIds is not null)
        {
            foreach (long id in mapIds)
            {
                if (id <= 0 || ids.Contains(id)) continue;
                ids.Add(id);
                if (ids.Count == 200) break;
            }
        }
        if (ids.Count == 0) return System.Array.Empty<MapHintRow>();
        var args = new object?[ids.Count];
        var slots = new string[ids.Count];
        for (int i = 0; i < ids.Count; i++) { args[i] = ids[i]; slots[i] = "$" + i.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        return Query(
            "select h.mapId, h.nameId, h.categoryId, c.nameId, h.x, h.y from HintData h " +
            "left join HintCategoryData c on c.id = h.categoryId where h.mapId in (" + string.Join(",", slots) + ") order by h.id",
            args).Select(r => new MapHintRow(L(r[0]), Text(L(r[1])) ?? "", I(r[2]), Text(L(r[3])) ?? "", I(r[4]), I(r[5]))).ToArray();
    }
}

public readonly record struct MapHintRow(long MapId, string Name, int CategoryId, string Category, int X, int Y);
