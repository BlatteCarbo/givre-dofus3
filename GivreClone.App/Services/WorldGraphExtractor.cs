using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

/// <summary>
/// Extrait le WORLD-GRAPH du client Dofus 3 AU RUNTIME (transitions inter-cartes)-maps-exporter.
/// Le MonoBehaviour "world-graph" (m_vertices / m_edges / m_outgoingEdges) est dans le bundle Addressables
/// aa/StandaloneWindows64/worldassets_assets_all.bundle. m_edges et m_outgoingEdges sont IDENTIQUES sur le client actuel
/// (30908 transitions) ; on lit m_outgoingEdges. On garde TOUTES les transitions SANS critere (marche/porte/soleil...),
/// le TYPE conserve -> par carte : {cellule, carte cible, direction, type}. AssetsTools.NET, type tree embarque.
/// ⚠ Le graphe reflète le client ACTUEL : une carte que le client a retirée du graphe (ex 171443204, restée dans
/// map-positions) n'est PAS routable ici — contrairement à un bot tiers qui embarque un worldgraph.json GELÉ (vieux client).
/// </summary>
public static class WorldGraphExtractor
{
    public static string? BundlePath()
    {
        foreach (var baseDir in DofusPaths.InstallRoots())
        {
            var p = Path.Combine(baseDir, "Dofus_Data", "StreamingAssets", "aa",
                                 "StandaloneWindows64", "worldassets_assets_all.bundle");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>Transitions de marche par carte, ou null si indisponible (client absent / bundle illisible).</summary>
    public static Dictionary<int, IReadOnlyList<MapTransition>>? TryExtract()
    {
        var path = BundlePath();
        if (path is null) return null;
        try
        {
            var mgr = new AssetsManager();
            var bun = mgr.LoadBundleFile(path, true);
            var af = mgr.LoadAssetsFileFromBundle(bun, 0, false);

            AssetTypeValueField? wg = null;
            foreach (var inf in af.file.AssetInfos)
            {
                if (inf.TypeId != 114) continue;   // MonoBehaviour
                AssetTypeValueField bf;
                try { bf = mgr.GetBaseField(af, inf); } catch { continue; }
                if ((bf["m_Name"].AsString ?? "").Contains("graph", StringComparison.OrdinalIgnoreCase)) { wg = bf; break; }
            }
            if (wg is null) { mgr.UnloadAll(); return null; }

            var byMap = new Dictionary<int, List<MapTransition>>();
            var typeHist = new Dictionary<int, int>();   // diagnostic : histogramme des types de transition (sans critere)
            int divergent = 0, divergentNonScroll = 0;   // diagnostic : m_transitionMapId != m_to (attendu : ScrollAction type 2 uniquement)
            foreach (var item in wg["m_outgoingEdges"]["m_values"]["Array"].Children)
                foreach (var edge in item["m_edgeList"]["Array"].Children)
                {
                    int fromMap = edge["m_from"]["m_mapId"].AsInt;
                    // Carte d'ARRIVÉE réelle de l'arête (m_to). ≠ m_transitionMapId pour les ScrollAction : m_transitionMapId est
                    // alors un voisin théorique (souvent SANS carte réelle) qu'il faut ENVOYER, m_to la carte où l'on arrive.
                    int toMap = 0;
                    try { toMap = edge["m_to"]["m_mapId"].AsInt; } catch { toMap = 0; }
                    // SECTEURS (zones) de départ et d'arrivée : une carte peut en compter plusieurs, non reliés à pied (264 cartes).
                    // 0 = inconnu -> planification au niveau carte pour cette arête.
                    int fromZone = 0, toZone = 0;
                    try { fromZone = edge["m_from"]["m_zoneId"].AsInt; } catch { fromZone = 0; }
                    try { toZone = edge["m_to"]["m_zoneId"].AsInt; } catch { toZone = 0; }
                    foreach (var t in edge["m_transitions"]["Array"].Children)
                    {
                        int type = t["m_type"].AsInt;
                        typeHist[type] = typeHist.TryGetValue(type, out var c) ? c + 1 : 1;
                        // On garde TOUTES les transitions : le voyage multi-cartes (BFS) en a besoin pour ATTEINDRE un
                        // intérieur (banque) via sa PORTE — cette arête n'est PAS de type 1. Le type est conservé ->
                        // l'exécuteur choisit : type 1 = bord (jqk) ; sinon = interactif/porte (iwo).
                        // Le CRITÈRE (m_criterion, ex "Qf=230") est désormais CONSERVÉ (null si vide) : une arête
                        // conditionnelle (porte/PNJ gardé par une quête) ne sert qu'en REPLI du BFS et seulement si le
                        // critère est prouvé satisfait. Avant, ces arêtes étaient purement écartées -> un pont gardé par
                        // une quête FAITE restait injoignable (« chemin coupé ») alors que le jeu l'autorise.
                        string crit = t["m_criterion"].AsString ?? "";
                        int sendMap = t["m_transitionMapId"].AsInt;
                        int arrival = toMap != 0 && toMap != sendMap ? toMap : 0;   // 0 = identique (cas général) -> cache compact
                        if (arrival != 0) { divergent++; if (type != 2) divergentNonScroll++; }
                        // Porte (type 32) : interactif (m_id) et skill (m_skillId) exacts à utiliser ; 0 = inconnu.
                        int elementId = 0, skillId = 0;
                        try { elementId = Math.Max(0, t["m_id"].AsInt); } catch { elementId = 0; }
                        try { skillId = Math.Max(0, t["m_skillId"].AsInt); } catch { skillId = 0; }
                        var tr = new MapTransition(t["m_cellId"].AsInt, sendMap, t["m_direction"].AsInt, type,
                                                   string.IsNullOrEmpty(crit) ? null : crit, arrival,
                                                   Math.Max(0, fromZone), Math.Max(0, toZone), elementId, skillId);
                        if (!byMap.TryGetValue(fromMap, out var l)) byMap[fromMap] = l = new List<MapTransition>();
                        l.Add(tr);
                    }
                }
            mgr.UnloadAll();
            try
            {
                var sbT = new StringBuilder();
                foreach (var kv in typeHist) sbT.Append($"type{kv.Key}={kv.Value} ");
                GivreLauncher.Core.Transport.TransportLog.Line($"[world-graph] {byMap.Count} carte(s) avec transitions (marche+porte gardées). Types vus : {sbT}");
                GivreLauncher.Core.Transport.TransportLog.Line($"[world-graph] {divergent} transition(s) dont l'arrivée (m_to) diffère de l'id envoyé ({divergentNonScroll} hors ScrollAction type 2).");
            }
            catch { }

            var res = new Dictionary<int, IReadOnlyList<MapTransition>>(byMap.Count);
            foreach (var kv in byMap) res[kv.Key] = kv.Value;
            return res;
        }
        catch { return null; }
    }

    public static WorldGraph? LoadCache(string path)
    {
        if (!File.Exists(path)) return null;
        try { return new WorldGraph(WorldGraphCache.Parse(File.ReadAllText(path))); }   // format : voir WorldGraphCache
        catch { return null; }
    }

    public static void SaveCache(string path, Dictionary<int, IReadOnlyList<MapTransition>> byMap)
        => File.WriteAllText(path, WorldGraphCache.Serialize(byMap));
}
