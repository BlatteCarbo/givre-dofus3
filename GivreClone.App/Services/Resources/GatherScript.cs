using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace GivreClone.App.Services.Resources;

/// <summary>Circuit de récolte et script Lua qui le déroule. Pur : testable sans jeu. Le script n'utilise que l'API existante (ITEMS_TO_GATHER, MAX_PODS, travelTo, gatherAll, log).</summary>
public static class GatherScript
{
    /// <summary>Choisit <paramref name="count"/> cartes : parmi les plus proches de l'origine (2× le nombre voulu), les plus riches ; puis les ordonne en tournée « plus proche voisin »
    /// depuis l'origine. Sans origine : les plus riches, tournée depuis la première. Les cartes en intérieur ne sont prises que si demandé.</summary>
    public static IReadOnlyList<ResourceNode> Route(ResourceMap map, int itemId, int? fromX, int? fromY, int? world, int count, bool includeIndoor = false)
    {
        count = Math.Clamp(count, 1, 30);
        bool hasOrigin = fromX is not null && fromY is not null;
        var pool = map.Best(itemId, fromX, fromY, world, includeIndoor, count * 2).Select(h => h.Node).ToList();
        var chosen = pool.OrderByDescending(n => n.Count).ThenBy(n => n.MapId).Take(count).ToList();
        if (chosen.Count == 0) return chosen;
        var route = new List<ResourceNode>();
        int cx = hasOrigin ? fromX!.Value : chosen[0].X, cy = hasOrigin ? fromY!.Value : chosen[0].Y;
        while (chosen.Count > 0)
        {
            var next = chosen.OrderBy(n => Math.Abs(n.X - cx) + Math.Abs(n.Y - cy)).ThenByDescending(n => n.Count).ThenBy(n => n.MapId).First();
            route.Add(next); chosen.Remove(next); cx = next.X; cy = next.Y;
        }
        return route;
    }

    /// <summary>Script Lua : boucle sur le circuit (carte par carte), récolte tout ce qui est demandé, reprend au début à la fin.</summary>
    public static string Build(ResourceInfo resource, IReadOnlyList<ResourceNode> route, int maxPodsPercent = 90)
    {
        if (route.Count == 0) throw new ArgumentException("circuit vide");
        string name = resource.Name.Replace("\r", " ").Replace("\n", " ").Replace("]]", "] ]");
        var sb = new StringBuilder();
        sb.Append("-- Récolte de « ").Append(name).Append(" » (").Append(ResourceMap.JobName(resource.Job)).Append(" niv. ").Append(resource.Level).Append(") — script généré par Givre.\n");
        sb.Append("-- Circuit de ").Append(route.Count).Append(" carte(s), de la plus proche à la plus lointaine, puis on recommence. À adapter : banque, filtres, pauses.\n\n");
        sb.Append("ITEMS_TO_GATHER = { ").Append(resource.ItemId.ToString(CultureInfo.InvariantCulture)).Append(" }   -- ").Append(name).Append('\n');
        sb.Append("MAX_PODS = ").Append(Math.Clamp(maxPodsPercent, 10, 100)).Append('\n').Append('\n');
        sb.Append("-- { id de carte, \"x,y\", points de récolte recensés }\n");
        sb.Append("local ROUTE = {\n");
        foreach (var n in route)
            sb.Append("  { ").Append(n.MapId.ToString(CultureInfo.InvariantCulture)).Append(", \"").Append(n.X.ToString(CultureInfo.InvariantCulture)).Append(',').Append(n.Y.ToString(CultureInfo.InvariantCulture))
              .Append("\", ").Append(n.Count).Append(" },").Append(n.Indoor ? "   -- intérieur" : "").Append('\n');
        sb.Append("}\n\n");
        sb.Append("local step = 0\n\n");
        sb.Append("function move()\n");
        sb.Append("  step = step % #ROUTE + 1\n");
        sb.Append("  local target = ROUTE[step]\n");
        sb.Append("  if getMapId() ~= target[1] then\n");
        sb.Append("    if not travelTo(target[1]) then\n");
        sb.Append("      print(\"Trajet impossible vers la carte \" .. target[2] .. \" (\" .. target[1] .. \") : on passe à la suivante\")\n");
        sb.Append("      return true\n");
        sb.Append("    end\n");
        sb.Append("  end\n");
        sb.Append("  gatherAll()\n");
        sb.Append("  return true\n");
        sb.Append("end\n");
        return sb.ToString();
    }
}
