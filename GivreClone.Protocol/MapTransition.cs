using System.Collections.Generic;
using System.Linq;

namespace GivreClone.Protocol;

/// <summary>
/// Une transition du WORLD-GRAPH du client (arete sortante d'une carte) : la cellule de BORD a atteindre, la carte
/// CIBLE, la direction Ankama (0=E,2=S,4=O,6=N ; diagonales impaires) et le type (1 = marche normale). Extrait au
/// runtime du bundle worldassets. C'est la donnee EXACTE que le jeu utilise pour traverser.
/// <para><c>Criterion</c> (null = INCONDITIONNEL, franchissement libre) : la condition Ankama que le jeu exige pour
/// emprunter cette arete (ex "Qf=230" = quete 230 terminee, typiquement une porte/PNJ gardant un pont). Ces aretes
/// conditionnelles ne servent QU'EN REPLI dans le BFS (voir <see cref="WorldGraph.PathToMap"/>) et seulement si le
/// critere est prouve satisfait — sinon le graphe se comporte comme avant (elles etaient jadis toutes ignorees).</para>
/// <para>DEUX ids de carte, DEUX rôles (cf capture du vrai client, Bonta 2026-09-22) : <c>TransitionMapId</c>
/// (m_transitionMapId) = l'id à ENVOYER dans le MapChangeRequest ; <c>ArrivalMapId</c> (m_to.m_mapId) = la carte où
/// l'on ARRIVE réellement. Identiques pour les types 1/8/32 ; DIFFÉRENTS pour ~1150 ScrollAction (type 2) dont
/// m_transitionMapId est un voisin « théorique » SANS carte réelle (ex zaap de Bonta : envoi 212600324, arrivée
/// 212600837) — le serveur applique l'action de scroll. Planifier sur l'id envoyé en faisait des culs-de-sac (16 sauts
/// au lieu de 6). 0 = inconnu (ancien cache) -> <see cref="Arrival"/> retombe sur TransitionMapId.</para>
/// <para>ZONES (m_from.m_zoneId / m_to.m_zoneId) : une carte peut compter plusieurs SECTEURS non reliés à pied (264 cartes ;
/// ex 126093080 : secteur haut avec les sorties est/nord, secteur bas avec les sorties sud/ouest, une PORTE entre les deux
/// mondes). Le client officiel planifie sur des sommets (carte, zone) et part de la zone de la cellule du perso
/// (<c>linkedZone &gt;&gt; 4</c>). <c>FromZone</c> = secteur d'où part la transition, <c>ArrivalZone</c> = secteur d'arrivée ;
/// 0 = inconnu (ancien cache) -> planification au niveau carte (comportement historique).
/// <c>ElementId</c> (m_id) / <c>SkillId</c> (m_skillId) : l'interactif et le skill d'une porte (type 32), 0 = inconnu.</para>
/// </summary>
public sealed record MapTransition(int CellId, int TransitionMapId, int Direction, int Type, string? Criterion = null, int ArrivalMapId = 0,
                                   int FromZone = 0, int ArrivalZone = 0, int ElementId = 0, int SkillId = 0)
{
    /// <summary>Carte d'ARRIVÉE réelle (m_to) : nœud du BFS et carte attendue après le saut. L'id à ENVOYER reste
    /// <see cref="TransitionMapId"/> (JAMAIS m_to dans le paquet : le serveur l'ignore — échec du 2026-09-06).</summary>
    public int Arrival => ArrivalMapId != 0 ? ArrivalMapId : TransitionMapId;
}

/// <summary>World-graph reduit : transitions sortantes par carte (marche libre uniquement). Requete O(1) par map.</summary>
public sealed class WorldGraph
{
    private readonly IReadOnlyDictionary<int, IReadOnlyList<MapTransition>> _byMap;
    public int MapCount => _byMap.Count;

    public WorldGraph(IReadOnlyDictionary<int, IReadOnlyList<MapTransition>> byMap) => _byMap = byMap;

    /// <summary>Transitions sortantes d'une carte (vide si inconnue).</summary>
    public IReadOnlyList<MapTransition> Transitions(int mapId)
        => _byMap.TryGetValue(mapId, out var t) ? t : System.Array.Empty<MapTransition>();

    /// <summary>La carte est-elle CONNUE du world-graph (source d'arêtes OU cible d'une transition) ? Diagnostic voyage :
    /// si la cible n'est pas connue, c'est que l'intérieur n'a AUCUNE arête dans le graphe (extraction à revoir).</summary>
    public bool IsKnownMap(int mapId)
    {
        if (_byMap.ContainsKey(mapId)) return true;
        foreach (var list in _byMap.Values) foreach (var t in list) if (t.Arrival == mapId) return true;
        return false;
    }

    /// <summary>Cartes dont une transition sortante porte ce skill (porte, zaapi…). 0 ou négatif : aucune.</summary>
    public IReadOnlyList<int> SourceMapsWithSkill(int skillId)
    {
        if (skillId <= 0) return Array.Empty<int>();
        var found = new List<int>();
        foreach (var pair in _byMap)
        {
            foreach (var transition in pair.Value)
            {
                if (transition.SkillId != skillId) continue;
                found.Add(pair.Key);
                break;
            }
        }
        return found;
    }

    /// <summary>
    /// Premier objectif atteint en marchant sur les arêtes sans critère, dans la limite de <paramref name="maxHops"/>.
    /// La carte de départ, si elle est un objectif, compte pour 0. Une arête conditionnelle n'est pas un passage.
    /// </summary>
    public (int MapId, int Hops)? NearestFree(int startMap, IReadOnlySet<int> goals, int maxHops = 80)
    {
        if (startMap <= 0 || goals is null || goals.Count == 0 || maxHops < 0) return null;
        if (goals.Contains(startMap)) return (startMap, 0);
        var seen = new HashSet<int> { startMap };
        var queue = new Queue<(int Map, int Hops)>();
        queue.Enqueue((startMap, 0));
        while (queue.Count > 0)
        {
            var (cur, hops) = queue.Dequeue();
            foreach (var transition in Transitions(cur))
            {
                if (transition.Criterion is not null) continue;
                int next = transition.Arrival;
                int distance = hops + 1;
                if (next == 0 || distance > maxHops || !seen.Add(next)) continue;
                if (goals.Contains(next)) return (next, distance);
                queue.Enqueue((next, distance));
            }
        }
        return null;
    }

    /// <summary>Plus court CHEMIN de transitions (BFS) de <paramref name="startMap"/> à <paramref name="targetMap"/> :
    /// la liste ORDONNÉE des transitions à franchir (marche/porte), ou vide si aucun chemin (world-graph incomplet, ou
    /// trajet nécessitant zaap/bateau non modélisés). Un saut par carte -> à exécuter en enchaînant les changements.
    /// <para>DEUX PASSES : d'abord sur les arêtes INCONDITIONNELLES uniquement (comportement historique, ZÉRO régression).
    /// Si aucun chemin ET qu'un évaluateur <paramref name="criterionMet"/> est fourni, une SECONDE passe autorise EN PLUS
    /// les arêtes conditionnelles (porte/PNJ) dont le critère est prouvé satisfait — c'est ce qui rend routable un pont
    /// gardé par une quête déjà faite. Sans évaluateur, les arêtes conditionnelles sont ignorées (comme avant).</para></summary>
    /// <summary>Clé d'une ARÊTE sortante pour l'ensemble <c>avoidEdges</c> du BFS : identifiée par (carte source, CELLULE de
    /// bord), PAS par la carte cible — car deux arêtes peuvent viser la MÊME carte par des cellules différentes (ex une PORTE
    /// type 32 et un BORD type 1 menant tous deux au voisin sud : interdire la porte infranchissable ne doit PAS interdire le
    /// bord). Sert à router AUTOUR d'une cellule précise infranchissable (élévation / porte injoignable) sans exclure la
    /// carte cible entière. Cellule ∈ [0,559] -&gt; 16 bits.</summary>
    public static long EdgeKey(int fromMap, int cell) => ((long)fromMap << 16) | (uint)(cell & 0xFFFF);

    public IReadOnlyList<MapTransition> PathToMap(int startMap, int targetMap, IReadOnlySet<int>? avoid = null,
                                                  int maxHops = 80, System.Func<string, bool>? criterionMet = null,
                                                  IReadOnlySet<long>? avoidEdges = null, int startZone = 0)
    {
        // PAR ZONE d'abord (comme le client officiel) quand la zone de départ est connue ET décrite par le graphe : seules
        // les sorties du secteur où se trouve le perso sont empruntables, puis on suit la zone d'ARRIVÉE de chaque saut.
        // Sans ça, sur une carte à plusieurs secteurs, le BFS prenait une sortie de l'AUTRE secteur, injoignable à pied
        // (126093080 : bord est 251 depuis le secteur bas, alors que seule la porte 302 y mène).
        if (startZone > 0 && HasZone(startMap, startZone))
        {
            var zoned = Bfs(startMap, startZone, targetMap, avoid, maxHops, criterionMet, conditional: false, avoidEdges);
            if (zoned.Count > 0) return zoned;
            if (criterionMet is not null)
            {
                zoned = Bfs(startMap, startZone, targetMap, avoid, maxHops, criterionMet, conditional: true, avoidEdges);
                if (zoned.Count > 0) return zoned;
            }
        }
        // Niveau CARTE (historique) : zone inconnue (ancien cache, cellule du perso pas encore confirmée) ou aucun chemin par
        // zone — jamais moins de chemins qu'avant (un saut injoignable échoue alors proprement et le reroute contourne).
        var free = Bfs(startMap, 0, targetMap, avoid, maxHops, criterionMet, conditional: false, avoidEdges);
        if (free.Count > 0 || criterionMet is null) return free;
        return Bfs(startMap, 0, targetMap, avoid, maxHops, criterionMet, conditional: true, avoidEdges);   // repli : inclut les portes conditionnelles franchissables
    }

    /// <summary>Secteur UNIQUE d'où partent les sorties de la carte (&gt; 0), -1 si elle en a plusieurs (il faut alors la cellule
    /// du perso pour savoir lequel), 0 si inconnu (hors graphe, ancien cache sans zones).</summary>
    public int SoleZone(int mapId)
    {
        int zone = 0;
        foreach (var t in Transitions(mapId))
        {
            if (t.FromZone <= 0) return 0;
            if (zone == 0) zone = t.FromZone;
            else if (t.FromZone != zone) return -1;
        }
        return zone;
    }

    /// <summary>Le graphe décrit-il des sorties depuis ce secteur de la carte ? (sinon : zone inconnue -> niveau carte).</summary>
    public bool HasZone(int mapId, int zone)
    {
        foreach (var t in Transitions(mapId)) if (t.FromZone == zone) return true;
        return false;
    }

    // Arête empruntable dans cette passe ? Sans critère : toujours. Critère d'ÉTAT D'INTERACTIF seul (MI : porte dans son état
    // normal, levier activé) : ce n'est pas une progression (quête…) mais un état physique -> satisfait, il compte comme un
    // passage LIBRE dès la 1re passe (sinon un détour libre de 30 cartes l'emporterait sur la porte). Autres critères : seulement
    // en passe de repli (conditional) et s'ils sont prouvés satisfaits.
    private static bool Allowed(MapTransition tr, bool conditional, System.Func<string, bool>? criterionMet)
    {
        if (tr.Criterion is null) return true;
        if (criterionMet is null) return false;
        if (QuestCriteria.IsInteractiveStateOnly(tr.Criterion)) return criterionMet(tr.Criterion);
        return conditional && criterionMet(tr.Criterion);
    }

    // BFS sur des sommets (carte, zone). startZone == 0 : zone ignorée (sommet = carte seule, comportement historique exact).
    // `conditional` : autorise AUSSI les arêtes à critère prouvé satisfait (ex quête terminée).
    // `avoidEdges` : arêtes (carte, cellule) INTERDITES précisément (bord infranchissable) — on route autour sans exclure la carte.
    private IReadOnlyList<MapTransition> Bfs(int startMap, int startZone, int targetMap, IReadOnlySet<int>? avoid, int maxHops,
                                             System.Func<string, bool>? criterionMet, bool conditional, IReadOnlySet<long>? avoidEdges)
    {
        if (startMap == targetMap || startMap == 0 || targetMap == 0) return System.Array.Empty<MapTransition>();
        bool zoned = startZone > 0;
        static long Vertex(int map, int zone) => ((long)map << 8) | (uint)(zone & 0xFF);
        long start = Vertex(startMap, zoned ? startZone : 0);
        var came = new Dictionary<long, (long From, MapTransition? Tr)> { [start] = (start, null) };
        var queue = new Queue<(int Map, int Zone)>();
        queue.Enqueue((startMap, zoned ? startZone : 0));
        while (queue.Count > 0)
        {
            var (cur, zone) = queue.Dequeue();
            long curV = Vertex(cur, zone);
            foreach (var tr in Transitions(cur))
            {
                // Secteur : seules les sorties de la zone où l'on est (zone 0 = inconnue -> toutes, comme avant).
                if (zoned && zone != 0 && tr.FromZone != 0 && tr.FromZone != zone) continue;
                if (!Allowed(tr, conditional, criterionMet)) continue;
                // Nœud suivant = carte d'ARRIVÉE (m_to), pas l'id envoyé : pour une ScrollAction, l'id envoyé est un voisin
                // théorique sans carte réelle -> cul-de-sac (le zaap de Bonta faisait 16 sauts au lieu de 6).
                int nb = tr.Arrival;
                // Arête PRÉCISE interdite (cette CELLULE de bord est infranchissable — élévation / porte injoignable) : on
                // route AUTOUR, la carte cible restant joignable par une AUTRE cellule (ex bord type 1 au lieu de la porte
                // type 32 vers le même voisin, ou approche par le NORD au lieu de l'OUEST).
                if (avoidEdges is not null && avoidEdges.Contains(EdgeKey(cur, tr.CellId))) continue;
                // `avoid` = cartes dont l'entrée a échoué (sortie bloquée) -> on cherche un chemin ALTERNATIF.
                if (nb == 0 || (avoid != null && avoid.Contains(nb))) continue;
                long nbV = Vertex(nb, zoned ? tr.ArrivalZone : 0);
                if (came.ContainsKey(nbV)) continue;
                came[nbV] = (curV, tr);
                if (nb == targetMap)
                {
                    var path = new List<MapTransition>();
                    for (long v = nbV; came[v].Tr is { } t; v = came[v].From) path.Add(t);
                    path.Reverse();
                    return path.Count <= maxHops ? path : System.Array.Empty<MapTransition>();
                }
                queue.Enqueue((nb, zoned ? tr.ArrivalZone : 0));
            }
        }
        return System.Array.Empty<MapTransition>();
    }

    /// <summary>Transition vers la carte voisine dans une direction cardinale (E=0,S=2,O=4,N=6), ou null. Les arêtes
    /// CONDITIONNELLES sont exclues (le pas-à-pas directionnel ne franchit que des bords/scrolls libres).</summary>
    public MapTransition? InDirection(int mapId, int direction)
        => Transitions(mapId).FirstOrDefault(t => t.Criterion is null && t.Direction == direction);

    /// <summary>Transition vers la carte voisine dans une direction cardinale: d'abord par
    /// m_direction (fiable ~99,8% sur le client courant), puis repli GEOMETRIQUE sur le bord de la cellule si
    /// aucune ne matche (transitions dont m_direction est diagonal/inattendu).</summary>
    public MapTransition? InDirection(int mapId, MapDir dir)
    {
        int d = DirOf(dir);
        var trs = Transitions(mapId);
        return trs.FirstOrDefault(t => t.Criterion is null && t.Direction == d)
            ?? trs.FirstOrDefault(t => t.Criterion is null && BorderOf(t.CellId) == dir);
    }

    // Direction Ankama pour un bord (E=droite, S=bas, O=gauche, N=haut).
    public static int DirOf(MapDir d) => d switch
    {
        MapDir.Right => 0, MapDir.Bottom => 2, MapDir.Left => 4, MapDir.Top => 6, _ => -1,
    };

    // Bord d'une cellule sur la grille Dofus 14 colonnes (0-559) MapCommon.CellIdToDirection.
    // Repli quand m_direction ne classe pas la transition en cardinal.
    public static MapDir? BorderOf(int cellId)
    {
        if (cellId == 13) return MapDir.Top;
        if (cellId == 27) return MapDir.Right;
        if (cellId % 14 == 13) return MapDir.Right;
        if (cellId == 545) return MapDir.Bottom;
        if (cellId % 14 == 0 && cellId != 546) return MapDir.Left;
        if (cellId >= 0 && cellId < 27) return MapDir.Top;
        if (cellId > 532 && cellId < 560) return MapDir.Bottom;
        return null;
    }
}
