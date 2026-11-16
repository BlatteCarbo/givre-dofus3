using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;

namespace GivreClone.App.Services.Eco;

/// <summary>Pont entre le résultat d'atelier (effets d'objet) et <see cref="ForgeEngine"/> : traduit les effets en caractéristiques
/// nommées comme les runes de <see cref="Forgemagie"/> (« Fo », « Ine », « Vi »…), clés insensibles à la casse.</summary>
public static class ForgeState
{
    public static bool HasUsableResult(InventoryReader.MagicResult result)
        => result.Gid > 0 && result.Code is InventoryReader.CraftFailed or InventoryReader.CraftSuccess or InventoryReader.CraftNeutral;

    public static Dictionary<string, int> Stats(IEnumerable<InventoryReader.ItemEffect> effects)
    {
        var stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, v) in ById(effects))
            if (RuneCatalog.Of(id) is { } f) stats[f.Code] = v;
        return stats;
    }

    /// <summary>Valeur courante par id d'effet (toutes les lignes, runables ou non).</summary>
    public static Dictionary<int, int> ById(IEnumerable<InventoryReader.ItemEffect> effects)
    {
        var byId = new Dictionary<int, int>();
        foreach (var e in effects)
        {
            // Jet courant : value_int pour un jet figé ; sinon le maximum de la fourchette (objet encore au jet d'origine).
            int v = e.Value != 0 ? e.Value : e.Max;
            byId[e.ActionId] = byId.TryGetValue(e.ActionId, out var cur) ? cur + v : v;
        }
        return byId;
    }

    /// <summary>Noms d'objet candidats pour une rune demandée par le moteur (« Ra Fo » ou « Rune Ra Fo »).</summary>
    public static IEnumerable<string> RuneItemNames(string rune)
    {
        var r = rune.Trim();
        if (r.StartsWith("Rune ", StringComparison.OrdinalIgnoreCase)) yield return r;
        else { yield return "Rune " + r; yield return r; }
    }
}
