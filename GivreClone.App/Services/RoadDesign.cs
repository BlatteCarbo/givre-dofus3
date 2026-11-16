using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GivreClone.App.Services;

/// <summary>Un point de passage de l'éditeur de trajet : une carte et ce qu'on y fait. Pas de dépôt banque ici : une étape
/// npcBank dans move() redéposerait à chaque tick sans jamais repartir ; le dépôt passe par bank() (pods pleins).</summary>
public sealed record RoadWaypoint(long MapId, bool Gather = false, bool Fight = false);

/// <summary>État d'une étape (point i -&gt; point suivant) après vérification sur le graphe de navigation du jeu.</summary>
public enum RoadLegState { Ok, Teleport, Unreachable, Unknown }

/// <summary>Étape vérifiée : du point <see cref="From"/> au point <see cref="To"/> (indices), nombre de cartes à pied (-1 = aucun chemin).</summary>
public sealed record RoadLeg(int From, int To, RoadLegState State, int Hops);

/// <summary>Vente à l'HDV avant la banque : HDV cible du perso (onglet HDV) et sa liste de remise en vente (objet, taille de lot,
/// prix minimum ; 0 = prix du marché).</summary>
public sealed record HdvPlan(long Map, IReadOnlyList<(long Gid, int Lot, long MinPrice)> Rules);

/// <summary>Réglages du trajet généré.</summary>
public sealed record RoadOptions(
    string Name = "Trajet",
    bool Loop = true,                 // la dernière carte ramène à la première (tournée de farm)
    int MaxPods = 90,                 // déclenche bank() quand une banque est connue
    int MinMonsters = 1,
    int MaxMonsters = 8,
    IReadOnlyList<int>? GatherElements = null,   // ElementTypeId à récolter (vide = tout ce que le moteur sait récolter)
    long BankMap = 0,                 // > 0 : pods pleins -> cette banque, dépôt, retour sur le trajet
    int BankDoor = -1,                // cellule de la porte du bâtiment sur BankMap (-1 = banquier sur BankMap même)
    long BankInsideMap = 0,           // carte atteinte par cette porte (l'intérieur, où se trouve le banquier)
    HdvPlan? Hdv = null);             // pods pleins : vendre d'abord à l'HDV (liste de remise en vente), puis banque

/// <summary>Résultat d'un import : points retrouvés + réglages, et ce qui n'a pas pu être relu.</summary>
public sealed record RoadImport(IReadOnlyList<RoadWaypoint> Waypoints, RoadOptions Options, IReadOnlyList<string> Notes);

/// <summary>
/// Éditeur de trajet (logique PURE, liée telle quelle dans les tests) : vérification des étapes et génération d'un trajet déclaratif
/// GivreClone (move() -&gt; table d'étapes, comme l'enregistreur). Chaque point vise la carte suivante par son id
/// (<c>changeMap = "&lt;id&gt;"</c>) : le moteur calcule lui-même le chemin (marche, portes, zaap / havre-sac), donc aucune carte
/// intermédiaire n'est à saisir. Un bloc <c>-- @road</c> en tête permet de rouvrir le trajet dans l'éditeur à l'identique.
/// </summary>
public static class RoadDesign
{
    private const string MetaPrefix = "-- @road ";
    private static string I(long v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Vérifie chaque étape. <paramref name="hops"/> : nombre de cartes à pied de a à b (-1 = aucun chemin) ;
    /// <paramref name="known"/> : carte connue du graphe ; <paramref name="world"/> : calque (1 = Monde des Douze, où zaaps et
    /// havre-sac existent). Sans chemin à pied, une étape entre deux cartes du Monde des Douze reste jouable via zaap / havre-sac.</summary>
    public static IReadOnlyList<RoadLeg> Validate(IReadOnlyList<RoadWaypoint> wps, bool loop,
        Func<long, long, int> hops, Func<long, bool> known, Func<long, int?> world)
    {
        var legs = new List<RoadLeg>();
        int n = wps.Count;
        if (n < 2) return legs;
        int last = loop ? n : n - 1;
        for (int i = 0; i < last; i++)
        {
            int j = (i + 1) % n;
            long a = wps[i].MapId, b = wps[j].MapId;
            if (a == b) { legs.Add(new RoadLeg(i, j, RoadLegState.Ok, 0)); continue; }
            if (!known(a) || !known(b)) { legs.Add(new RoadLeg(i, j, RoadLegState.Unknown, -1)); continue; }
            int h = hops(a, b);
            if (h >= 0) { legs.Add(new RoadLeg(i, j, RoadLegState.Ok, h)); continue; }
            bool worldOfTwelve = world(a) == 1 && world(b) == 1;
            legs.Add(new RoadLeg(i, j, worldOfTwelve ? RoadLegState.Teleport : RoadLegState.Unreachable, -1));
        }
        return legs;
    }

    /// <summary>Indices des points dont la carte apparaît plusieurs fois : le moteur fusionne les étapes d'une même carte (la
    /// dernière sortie l'emporte), donc un trajet qui repasse par une carte ne la rejoue pas comme prévu.</summary>
    public static IReadOnlyList<int> Duplicates(IReadOnlyList<RoadWaypoint> wps)
    {
        var seen = new Dictionary<long, int>();
        var dup = new SortedSet<int>();
        for (int i = 0; i < wps.Count; i++)
        {
            if (seen.TryGetValue(wps[i].MapId, out int first)) { dup.Add(first); dup.Add(i); }
            else seen[wps[i].MapId] = i;
        }
        return dup.ToList();
    }

    /// <summary>Génère le trajet Lua. <paramref name="label"/> : commentaire lisible par carte (coordonnées, sous-zone).</summary>
    public static string Generate(IReadOnlyList<RoadWaypoint> wps, RoadOptions o, Func<long, string>? label = null)
    {
        var sb = new StringBuilder();
        string name = Clean(o.Name);
        sb.AppendLine($"-- Trajet « {name} » — créé avec l'éditeur de trajet GivreClone ({wps.Count} carte(s)).");
        sb.AppendLine(MetaPrefix + Meta(wps, o));
        sb.AppendLine();
        var elements = o.GatherElements ?? Array.Empty<int>();
        sb.AppendLine($"ELEMENTS_TO_GATHER = {{ {string.Join(", ", elements.Where(e => e > 0).Distinct())} }}");
        sb.AppendLine($"MAX_PODS = {Math.Clamp(o.MaxPods, 10, 100)}");
        int min = Math.Clamp(o.MinMonsters, 1, 8), max = Math.Clamp(o.MaxMonsters, 1, 8);
        if (min > max) (min, max) = (max, min);
        sb.AppendLine($"MIN_MONSTERS = {min}");
        sb.AppendLine($"MAX_MONSTERS = {max}");
        sb.AppendLine();
        bool hdv = HasHdv(o.Hdv);
        sb.AppendLine("function move()");
        if (hdv) sb.AppendLine("  HDV_DONE = false   -- pods redescendus : la prochaine fois qu'ils sont pleins, nouveau passage à l'HDV");
        sb.AppendLine("  return {");
        for (int i = 0; i < wps.Count; i++)
        {
            var w = wps[i];
            bool hasNext = i + 1 < wps.Count || (o.Loop && wps.Count > 1);
            long next = hasNext ? wps[(i + 1) % wps.Count].MapId : 0;
            var fields = new List<string> { $"map = \"{I(w.MapId)}\"" };
            if (w.Gather) fields.Add("gather = true");
            if (w.Fight) fields.Add("fight = true");
            if (hasNext && next != w.MapId) fields.Add($"changeMap = \"{I(next)}\"");
            string comment = label is null ? "" : "  -- " + Clean(label(w.MapId));
            if (!hasNext) comment += (comment.Length == 0 ? "  -- " : " ; ") + "fin du trajet";
            sb.AppendLine($"    {{ {string.Join(", ", fields)} }},{comment}");
        }
        sb.AppendLine("  }");
        sb.AppendLine("end");
        if (o.BankMap > 0 || hdv) { sb.AppendLine(); sb.Append(BankFunction(o.BankMap, o.BankDoor, o.BankInsideMap, o.Hdv)); }
        return sb.ToString();
    }

    /// <summary>Fonction Lua <c>bank()</c> déclarative, appelée par le moteur quand les pods atteignent MAX_PODS. Hors de ces cartes il
    /// voyage jusqu'à la 1re (marche, zaap, havre-sac, détection de blocage), passe la porte si la banque est dans un bâtiment, dépose
    /// au banquier puis rend la main : move() reprend dès que les pods redescendent. Partagée par l'éditeur, l'expédition et le donjon.</summary>
    public static string BankFunction(long bankMap, int door, long insideMap, HdvPlan? hdv = null)
    {
        var sb = new StringBuilder();
        bool sell = HasHdv(hdv);
        if (sell)
        {
            sb.AppendLine($"local HDV_MAP = {I(hdv!.Map)}");
            sb.AppendLine("local HDV_RULES = {   -- liste de remise en vente (onglet HDV) : objet, taille du lot, prix minimum (0 = prix du marché)");
            foreach (var r in hdv.Rules.Where(r => r.Gid > 0 && r.Lot > 0))
                sb.AppendLine($"  {{ {I(r.Gid)}, {r.Lot}, {I(Math.Max(0, r.MinPrice))} }},");
            sb.AppendLine("}");
            sb.AppendLine("HDV_DONE = false");
            sb.AppendLine();
            sb.AppendLine("-- Une seule fois par passage à pods pleins : HDV (si le sac contient au moins un lot à vendre), mise en vente, fermeture.");
            sb.AppendLine("local function sellAtHdv()");
            sb.AppendLine("  if HDV_DONE then return end");
            sb.AppendLine("  HDV_DONE = true");
            sb.AppendLine("  local any = false");
            sb.AppendLine("  for _, r in ipairs(HDV_RULES) do if getInventoryItemCount(r[1]) >= r[2] then any = true break end end");
            sb.AppendLine("  if not any then return end");
            sb.AppendLine("  if not goToHdv(HDV_MAP) then printWarning('HDV : trajet ou ouverture impossible, direction la banque') return end");
            sb.AppendLine("  local total = 0");
            sb.AppendLine("  for _, r in ipairs(HDV_RULES) do");
            sb.AppendLine("    if getInventoryItemCount(r[1]) >= r[2] then");
            sb.AppendLine("      local placed = sale:keepOnSale(r[1], r[2], 1000, r[3] > 0 and { minPrice = r[3] } or nil)");
            sb.AppendLine("      total = total + (placed or 0)");
            sb.AppendLine("    end");
            sb.AppendLine("  end");
            sb.AppendLine("  printSuccess('HDV : ' .. total .. ' lot(s) mis en vente')");
            sb.AppendLine("  leaveDialog()");
            sb.AppendLine("  delay(800)");
            sb.AppendLine("end");
            sb.AppendLine();
        }
        sb.AppendLine("function bank()");
        if (sell) sb.AppendLine("  sellAtHdv()");
        if (bankMap > 0)
        {
            sb.AppendLine("  return {");
            if (door >= 0 && insideMap > 0)
            {
                sb.AppendLine($"    {{ map = \"{I(bankMap)}\", door = \"{door}\" }},  -- porte de la banque");
                sb.AppendLine($"    {{ map = \"{I(insideMap)}\", npcBank = true }},  -- banquier");
            }
            else
                sb.AppendLine($"    {{ map = \"{I(bankMap)}\", npcBank = true }},  -- banque");
            sb.AppendLine("  }");
        }
        else
        {
            // HDV seul (aucune banque connue) : si la vente n'a pas suffi à vider le sac, arrêt propre plutôt qu'une attente sans fin.
            sb.AppendLine("  if getPodsPercent() >= MAX_PODS then printWarning('Pods toujours pleins et aucune banque connue : arrêt') return false end");
            sb.AppendLine("  return true");
        }
        sb.AppendLine("end");
        return sb.ToString();
    }

    private static bool HasHdv(HdvPlan? h) => h is { Map: > 0 } && h.Rules.Any(r => r.Gid > 0 && r.Lot > 0);

    // Métadonnées compactes (une ligne) : nom|boucle|pods|min|max|banque|porte|éléments|points|intérieur ; point = id:flags (g f).
    private static string Meta(IReadOnlyList<RoadWaypoint> wps, RoadOptions o)
    {
        string pts = string.Join(";", wps.Select(w => I(w.MapId) + ":" + (w.Gather ? "g" : "") + (w.Fight ? "f" : "")));
        string els = string.Join(",", (o.GatherElements ?? Array.Empty<int>()).Where(e => e > 0));
        return string.Join("|", Uri.EscapeDataString(Clean(o.Name)), o.Loop ? 1 : 0, o.MaxPods, o.MinMonsters, o.MaxMonsters,
                           I(o.BankMap), o.BankDoor, els, pts, I(o.BankInsideMap));
    }

    private static string Clean(string? s) => (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();

    private static readonly Regex StepRx = new(@"\{\s*map\s*=\s*""(?<map>[^""]+)""(?<rest>[^\n]*)", RegexOptions.Compiled);

    /// <summary>Relit un trajet : à l'identique s'il vient de l'éditeur (bloc <c>-- @road</c>), sinon au mieux depuis les étapes
    /// <c>{ map = "…", gather = …, fight = …, npcBank = … }</c> (enregistreur, scripts écrits à la main). Les cartes en coordonnées
    /// « x,y » passent par <paramref name="coordToMap"/>. null si rien d'exploitable.</summary>
    public static RoadImport? Parse(string? lua, Func<int, int, long?>? coordToMap = null)
    {
        if (string.IsNullOrWhiteSpace(lua)) return null;
        var notes = new List<string>();
        foreach (var line in lua.Split('\n'))
        {
            string t = line.Trim();
            if (!t.StartsWith(MetaPrefix, StringComparison.Ordinal)) continue;
            if (ParseMeta(t[MetaPrefix.Length..].Trim()) is { } exact) return exact;
            notes.Add("bloc @road illisible : relecture des étapes");
            break;
        }
        // Seules les étapes de move() comptent (pas celles de bank()) : on coupe au début de « function bank ».
        int cut = lua.IndexOf("function bank", StringComparison.Ordinal);
        string body = cut >= 0 ? lua[..cut] : lua;
        var wps = new List<RoadWaypoint>();
        foreach (Match m in StepRx.Matches(body))
        {
            string key = m.Groups["map"].Value.Trim();
            string rest = m.Groups["rest"].Value;
            long id = 0;
            if (long.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)) id = parsed;
            else
            {
                var c = key.Split(',');
                if (c.Length == 2 && int.TryParse(c[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
                    && int.TryParse(c[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
                    id = coordToMap?.Invoke(x, y) ?? 0;
            }
            if (id <= 0) { notes.Add($"carte « {key} » non reconnue : ignorée"); continue; }
            bool F(string k) => Regex.IsMatch(rest, $@"\b{k}\s*=\s*true\b");
            if (F("npcBank")) notes.Add($"carte {key} : dépôt banque déplacé vers bank() (pods pleins)");
            wps.Add(new RoadWaypoint(id, F("gather") || F("forceGather") || F("forcegather"), F("fight") || F("forceFight") || F("forcefight")));
        }
        if (wps.Count == 0) return null;
        bool loop = wps.Count > 1 && Regex.IsMatch(body, $@"changeMap\s*=\s*""{wps[0].MapId}""");
        return new RoadImport(wps, new RoadOptions(Loop: loop), notes);
    }

    private static RoadImport? ParseMeta(string meta)
    {
        var p = meta.Split('|');
        if (p.Length is not (9 or 10)) return null;
        static bool N(string s, out long v) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        if (!N(p[1], out long loop) || !N(p[2], out long pods) || !N(p[3], out long min) || !N(p[4], out long max)
            || !N(p[5], out long bank) || !N(p[6], out long door)) return null;
        var els = p[7].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(e => int.TryParse(e, out int v) ? v : 0).Where(v => v > 0).ToList();
        var wps = new List<RoadWaypoint>();
        foreach (var token in p[8].Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = token.Split(':');
            if (!N(kv[0], out long id) || id <= 0) return null;
            string f = kv.Length > 1 ? kv[1] : "";
            wps.Add(new RoadWaypoint(id, f.Contains('g'), f.Contains('f')));
        }
        string name;
        try { name = Uri.UnescapeDataString(p[0]); } catch { name = p[0]; }
        long inside = p.Length == 10 && N(p[9], out long ins) ? ins : 0;
        return new RoadImport(wps, new RoadOptions(name, loop != 0, (int)pods, (int)min, (int)max, els, bank, (int)door, inside), Array.Empty<string>());
    }

    /// <summary>Ordre de visite le plus court (plus proche voisin) en gardant le 1er point comme départ. Les points sans coordonnées
    /// restent à la fin, dans leur ordre.</summary>
    public static IReadOnlyList<RoadWaypoint> Optimize(IReadOnlyList<RoadWaypoint> wps, Func<long, (int X, int Y)?> coord)
    {
        if (wps.Count < 3) return wps.ToList();
        var order = ExpeditionScript.NearestOrder(wps.Select(w => (w.MapId, coord(w.MapId))).ToList(), coord(wps[0].MapId));
        var byId = new Dictionary<long, Queue<RoadWaypoint>>();
        foreach (var w in wps) { if (!byId.TryGetValue(w.MapId, out var q)) byId[w.MapId] = q = new(); q.Enqueue(w); }
        return order.Select(id => byId[id].Dequeue()).ToList();
    }
}
