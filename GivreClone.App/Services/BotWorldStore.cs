using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

/// <summary>
/// Etat de jeu HEADLESS d'un bot (par PID) : carte, cellule du perso, marchabilite, interactifs, acteurs, pods, niveau.
/// Alimente par les MEMES evenements de <see cref="LaunchService"/> que le VM UI (BotsViewModel) — mais sans thread UI,
/// pour que le moteur de script Lua (<c>GivreLuaHost</c>) puisse lire l'etat et fournir source/mapId/marchabilite aux
/// verbes de deplacement. Les champs sont ecrits depuis les threads du relais ; lectures best-effort (references atomiques).
/// </summary>
public sealed class BotWorldState
{
    public long MapId;
    public int? MapX, MapY;              // coordonnees de carte "x,y" (pour getCurrentPos, matching des maps dans les scripts)
    public int? SelfCell;                // cellule 0-559 du perso
    public long? SelfCellMap;            // carte pour laquelle SelfCell est CONFIRMEE (via jss/MapComplementaryInformationEvent).
                                         // INVALIDEE au changement de carte : evite de calculer un move sur la cellule d'exit
                                         // PERIMEE de l'ancienne carte (le jss d'entree arrive ~1 s apres le jru).
    public bool SelfCellConfirmed;       // SelfCell vient-elle d'une VERITE SERVEUR (jss / echo de move jsj / recalage jqt) ?
                                         // FALSE quand elle n'est qu'une PREDICTION OPTIMISTE posee a l'injection d'un move
                                         // (avant tout echo serveur). Un changement de carte ne doit JAMAIS envoyer un jqk
                                         // direct depuis une position seulement PREDITE (cf audit un bot tiers: on ne change
                                         // jamais de carte sans une cellule de bord CONFIRMEE) -> sinon le serveur refuse.
    public long SelfActorId;             // actorId du perso (pour suivre ses deplacements)
    public int? Level;
    public string? CharacterName;
    public (int Cur, int Max) Pods;
    public bool PodsAuthoritative;
    public IReadOnlyList<MapCell> Cells = Array.Empty<MapCell>();
    public long CellsMap = -1;           // carte pour laquelle Cells (geometrie mov/los/floor) est CHARGEE (estampille par MapCellsLoaded).
    public IReadOnlyList<MapInteractive> Interactives = Array.Empty<MapInteractive>();
    public IReadOnlyList<MapActor> Actors = Array.Empty<MapActor>();
    // Monotonically increasing map/actor-snapshot epoch. A click from an older
    // map (even with a recycled contextual actor ID) cannot authorize attack.
    public long MapGeneration;
    // Sequence lock for server-published actor/position/map snapshots.
    // Odd: writer in progress. Even: complete publication. Separate from the
    // map identity generation so unrelated actors may move without expiring a
    // correctly identified, unchanged monster group.
    public long ActorPublicationSequence;
    internal readonly object ActorPublicationGate = new();

    // Modele de connexion Dofus 3: pour une cellule NON marchable, Floor=Zone=-1.
    private bool[] _walk = new bool[MapPathfinder.CellCount];
    private int[] _zone = new int[MapPathfinder.CellCount];          // (linkedZone & 0xF8) >> 4, ou -1 si non marchable
    private int[] _floor = new int[MapPathfinder.CellCount];         // floor, ou -1 si non marchable
    private bool[] _canMoveZone = new bool[MapPathfinder.CellCount]; // moveZone == 1

    internal void RebuildWalk()
    {
        var w = new bool[MapPathfinder.CellCount];
        var z = new int[MapPathfinder.CellCount];
        var f = new int[MapPathfinder.CellCount];
        var cmz = new bool[MapPathfinder.CellCount];
        for (int i = 0; i < z.Length; i++) { z[i] = -1; f[i] = -1; }   // defaut = non marchable
        foreach (var c in Cells) if ((uint)c.CellId < w.Length)
        {
            w[c.CellId] = c.Mov;
            cmz[c.CellId] = c.MoveZone == 1;
            if (c.Mov) { z[c.CellId] = (c.LinkedZone & 0xF8) >> 4; f[c.CellId] = c.Floor; }
        }
        _walk = w; _zone = z; _floor = f; _canMoveZone = cmz;   // swap atomique des references (tableaux coherents)
    }

    /// <summary>Zone (secteur du world-graph, <c>linkedZone &gt;&gt; 4</c> — règle du client officiel) de la cellule du perso sur
    /// <paramref name="map"/>, ou 0 si inconnue (cellule pas encore confirmée, géométrie pas chargée, cellule sans zone).
    /// Choisit le sommet de départ du voyage : sur une carte à plusieurs secteurs, seules les sorties du sien sont joignables.</summary>
    public int RpZoneFor(long map)
    {
        if (!SelfReadyFor(map) || SelfCell is not int c || CellsMap != map) return 0;
        // Lu dans Cells (posé AVANT CellsMap par MapCellsLoaded) et non dans _zone, reconstruit APRÈS : pas de zone de la carte
        // précédente dans la fenêtre entre les deux.
        var cells = Cells;
        MapCell? cell = (uint)c < (uint)cells.Count && cells[c].CellId == c ? cells[c] : cells.FirstOrDefault(x => x.CellId == c);
        if (cell is not { Mov: true } mc) return 0;
        int zone = (mc.LinkedZone & 0xF0) >> 4;
        return zone > 0 ? zone : 0;
    }

    /// <summary>Predicat de marchabilite (snapshot courant), utilisable directement par les verbes de LaunchService.</summary>
    public bool IsWalkable(int cell) { var w = _walk; return (uint)cell < w.Length && w[cell]; }

    /// <summary>Regle de deplacement Dofus 3 (moveZone/floor) entre deux cellules adjacentes — a passer au pathfinding pour
    /// que le serveur ACCEPTE le chemin (sinon il traverse des cellules marchables mais de secteurs deconnectes -&gt; refus).</summary>
    public bool StepAllowed(int from, int to) => MapPathfinder.Communicates(from, to, _floor, _zone, _canMoveZone);

    /// <summary>Pods en % (0-100), ou 100 si inconnu/non authoritatif. Sert au declenchement du bank() (pods% >= MAX_PODS).</summary>
    public int PodsPercent => PodsAuthoritative && Pods.Max > 0 ? (int)(100L * Pods.Cur / Pods.Max) : 100;

    /// <summary>Groupes de monstres sur la carte (acteurs Monster), pour le ciblage de combat.</summary>
    public IEnumerable<MapActor> Monsters => Actors.Where(a => a.Kind == MapActorKind.Monster);

    /// <summary>Interactif recoltable pose sur une cellule donnee (ou null).</summary>
    public MapInteractive? InteractiveAt(int cell) => Interactives.FirstOrDefault(i => i.CellId == cell);

    /// <summary>La cellule du perso est-elle CONFIRMEE pour la carte <paramref name="map"/> ? (jss traite pour cette carte).
    /// Un script ne doit calculer un deplacement/changement de carte que quand c'est vrai — sinon il utilise une cellule
    /// d'exit perimee de la carte precedente (bug des refus de move apres changement de carte).</summary>
    public bool SelfReadyFor(long map)
        => SelfCell is not null && SelfCellMap == map && SelfCellConfirmed;

    /// <summary>La geometrie de cellules de CETTE carte est-elle chargee (bundle client) ? Sinon le pathfinding tournerait
    /// sur une carte perimee -> marche refusee. Gate evenementiel qui remplace le settle fixe apres un changement de carte.</summary>
    public bool CellsReadyFor(long map) => CellsMap == map && Cells.Count > 0;

    // ---- Lot K : obstacles PERSONNALISÉS du script (map:setCustomObstacles) ----
    // Instantané IMMUABLE remplacé d'un bloc (écrit par le thread du script, vidé par le thread du relais au changement de
    // carte). Portée = UNE carte; le test `Map != MapId`
    // neutralise aussi la course ajout (script) / vidage (relais).
    public sealed record CustomObstacleSet(long Map, bool[] Cells);
    private volatile CustomObstacleSet? _customObstacles;
    public CustomObstacleSet? CustomObstacles => _customObstacles;

    /// <summary>AJOUTE des cases interdites sur la carte `map`. Cases hors 0-559 ignorées.</summary>
    public void AddCustomObstacles(long map, IEnumerable<int> cells)
    {
        var cur = _customObstacles; var arr = new bool[MapPathfinder.CellCount];
        if (cur is not null && cur.Map == map) Array.Copy(cur.Cells, arr, arr.Length);
        foreach (var c in cells) if ((uint)c < (uint)arr.Length) arr[c] = true;
        _customObstacles = new CustomObstacleSet(map, arr);
    }

    public void ClearCustomObstacles() => _customObstacles = null;

    /// <summary>Cases interdites de la carte COURANTE (vide si le jeu d'obstacles vise une autre carte).</summary>
    public IReadOnlyList<int> CustomObstacleCells
    {
        get
        {
            var o = _customObstacles;
            if (o is null || o.Map != MapId) return Array.Empty<int>();
            var l = new List<int>();
            for (int i = 0; i < o.Cells.Length; i++) if (o.Cells[i]) l.Add(i);
            return l;
        }
    }

    public int CustomObstacleCount => CustomObstacleCells.Count;

    /// <summary>Marchabilité pour les déplacements SUR LA CARTE pilotés par le script : relief ET pas un obstacle personnalisé
    /// de la carte COURANTE. Sans obstacle = exactement <see cref="IsWalkable"/> (scripts existants inchangés).</summary>
    public bool IsWalkableForScript(int cell)
    {
        if (!IsWalkable(cell)) return false;
        var o = _customObstacles;
        return o is null || o.Map != MapId || !((uint)cell < (uint)o.Cells.Length && o.Cells[cell]);
    }
}

/// <summary>
/// Agrege l'etat de jeu par PID (<see cref="BotWorldState"/>) en s'abonnant aux evenements de <see cref="LaunchService"/>.
/// Un seul store partage pour toute l'app (<c>Stores.World</c>). Deterministe : consomme le meme flux d'evenements que
/// le VM -> pas de divergence. Consomme par <c>GivreLuaHost</c> (source d'etat headless pour les scripts Lua).
/// </summary>
public sealed class BotWorldStore
{
    private readonly ConcurrentDictionary<uint, BotWorldState> _byPid = new();

    public BotWorldState Get(uint pid) => _byPid.GetOrAdd(pid, static _ => new BotWorldState());

    private static void PublishActorState(BotWorldState state, Action update)
    {
        lock (state.ActorPublicationGate)
        {
            System.Threading.Interlocked.Increment(ref state.ActorPublicationSequence);
            try { update(); }
            finally { System.Threading.Interlocked.Increment(ref state.ActorPublicationSequence); }
        }
    }

    /// <summary>Oublie la CARTE du bot (carte, coordonnées, cellule self, relief, interactifs, acteurs, obstacles du script) en
    /// gardant son identité (nom, niveau, pods, actorId) : session de jeu perdue (MITM) -> la télémétrie et les scripts ne
    /// lisent plus une carte périmée. La 1re carte de la session suivante repeuple tout.</summary>
    public void ClearMap(uint pid)
    {
        if (!_byPid.TryGetValue(pid, out var s)) return;
        PublishActorState(s, () =>
        {
            System.Threading.Interlocked.Increment(ref s.MapGeneration);
            s.MapId = 0; s.MapX = null; s.MapY = null;
            s.SelfCell = null; s.SelfCellMap = null; s.SelfCellConfirmed = false;
            s.Cells = Array.Empty<MapCell>(); s.CellsMap = -1; s.RebuildWalk();
            s.Interactives = Array.Empty<MapInteractive>(); s.Actors = Array.Empty<MapActor>();
            s.ClearCustomObstacles();
        });
    }

    public BotWorldStore(LaunchService launch)
    {
        launch.GameLinkLost += (pid, _, _) => ClearMap(pid);   // MITM : kick serveur / connexion de jeu fermée, Dofus.exe ouvert
        launch.CharacterIdentified += (pid, ci) => { var s = Get(pid); s.CharacterName = ci.Name; if (ci.Level is int l) s.Level = l; };
        launch.CharacterLeveledUp += (pid, lvl) => Get(pid).Level = lvl;
        launch.MapChanged += (pid, map, x, y) =>
        {
            var s = Get(pid);
            PublishActorState(s, () =>
            {
            // VRAIE transition de carte -> la self-cell (souvent la cellule d'exit optimiste de l'ancienne carte) devient
            // PERIMEE. On l'INVALIDE : elle sera re-confirmee par le jss (MapActorsDecoded, match self par nom) ~1 s plus tard.
            if (s.MapId != map)
            {
                System.Threading.Interlocked.Increment(ref s.MapGeneration);
                s.SelfCell = null; s.SelfCellMap = null; s.SelfCellConfirmed = false; s.ClearCustomObstacles();
            }   // Lot K : obstacles personnalisés = portée d'UNE carte
            s.MapId = map; s.MapX = x; s.MapY = y;
            });
        };
        launch.MapCellsLoaded += (pid, map, cells) => { var s = Get(pid); s.Cells = cells; s.CellsMap = map; s.RebuildWalk(); };
        launch.MapInteractivesDecoded += (pid, ints) => Get(pid).Interactives = ints;
        launch.MapInteractiveSkillsUpdated += (pid, elementId, skills) =>
        {
            var s = Get(pid);
            s.Interactives = s.Interactives.Select(x => x.ElementId == elementId ? x with { Skills = skills } : x).ToList();
        };
        launch.MapInteractiveUpdated += (pid, elementId, cell, state) =>
        {
            var s = Get(pid);
            s.Interactives = s.Interactives.Select(x => x.ElementId == elementId ? x with { State = state } : x).ToList();
        };
        launch.MapActorsDecoded += (pid, actors) =>
        {
            var s = Get(pid);
            PublishActorState(s, () =>
            {
            System.Threading.Interlocked.Increment(ref s.MapGeneration);
            s.Actors = actors;
            // IDENTIFICATION SELF ROBUSTE (l'ancien match par NOM EXACT cassait dès que le pseudo décodait autrement
            // — apostrophe/casse/accent/titre — ou que l'identité de login n'était pas captée -> SelfCell JAMAIS confirmée
            // -> "position self inconnue" en boucle, surtout en multi-comptes). 3 niveaux, du plus fiable au repli :
            //   1) charId AUTHENTIFIÉ (l'actorId roleplay du perso == son characterId, comme en combat) ;
            //   2) NOM (repli si charId pas encore capté) ;
            //   3) si l'identité de login n'a jamais été captée (kvw raté, compte multi-perso) : LE joueur du jss dont
            //      l'actorId est un des persos du compte (kvi) -> c'est forcément le mien ; on l'adopte comme charId.
            long charId = launch.CharacterId(pid);
            MapActor? self = charId != 0 ? actors.FirstOrDefault(a => a.Kind == MapActorKind.Player && a.ActorId == charId) : null;
            if (self is null && s.CharacterName is { Length: > 0 } name)
                self = actors.FirstOrDefault(a => a.Kind == MapActorKind.Player && a.Name == name);
            if (self is null && charId == 0)
            {
                var mine = launch.AccountCharacterIds(pid);
                if (mine.Count > 0)
                {
                    self = actors.FirstOrDefault(a => a.Kind == MapActorKind.Player && mine.Contains(a.ActorId));
                    if (self is not null) launch.AdoptCharacterId(pid, self.ActorId);   // débloque aussi l'identité de combat
                }
            }
            // Seul le jss porte les VRAIS acteurs (le jru envoie une liste VIDE -> self=null -> on ne touche a rien).
            // Match self -> on ESTAMPILLE SelfCellMap = carte courante : la cellule d'entree est desormais confirmee.
            if (self is not null) { s.SelfActorId = self.ActorId; s.SelfCell = self.CellId; s.SelfCellMap = s.MapId; s.SelfCellConfirmed = true; }
            });
        };
        launch.MapActorsAdded += (pid, added) =>
        {
            var s = Get(pid);
            PublishActorState(s, () =>
            {
            var list = s.Actors.ToList();
            foreach (var a in added) { int i = list.FindIndex(x => x.ActorId == a.ActorId); if (i >= 0) list[i] = a; else list.Add(a); }
            s.Actors = list;
            });
        };
        launch.MapActorRemoved += (pid, actorId) =>
        {
            var s = Get(pid);
            PublishActorState(s, () =>
            {
                if (s.Actors.Any(a => a.ActorId == actorId)) s.Actors = s.Actors.Where(a => a.ActorId != actorId).ToList();
            });
        };
        launch.MapActorMoved += (pid, actorId, path) =>
        {
            if (path.Count == 0) return;
            var s = Get(pid);
            PublishActorState(s, () =>
            {
            int dest = path[path.Count - 1];
            // jsj (MapMovementEvent) = echo SERVEUR du move -> position AUTORITAIRE.
            if (actorId == s.SelfActorId && (uint)dest < MapPathfinder.CellCount) { s.SelfCell = dest; s.SelfCellMap = s.MapId; s.SelfCellConfirmed = true; }
            s.Actors = s.Actors.Select(a => a.ActorId == actorId ? a with { CellId = dest } : a).ToList();
            });
        };
        // Deplacement self OPTIMISTE (predit a l'injection du move, AVANT tout echo serveur) : on pose la cellule cible pour
        // les lectures immediates, mais NON CONFIRMEE. Un vrai echo jsj (MapActorMoved) ou un jss la confirmera ; un refus
        // (jqt) la recalera. Tant qu'elle est predite, aucun changement de carte ne l'utilisera pour un jqk direct.
        launch.SelfMoved += (pid, path) => { if (path.Count > 0) { var s = Get(pid); PublishActorState(s, () => { s.SelfCell = path[path.Count - 1]; s.SelfCellMap = s.MapId; s.SelfCellConfirmed = false; }); } };
        launch.SelfPositionCorrected += (pid, cell) =>
        {
            if ((uint)cell >= MapPathfinder.CellCount) return;
            var s = Get(pid);
            PublishActorState(s, () =>
            {
                s.SelfCell = cell; s.SelfCellMap = s.MapId; s.SelfCellConfirmed = true;
            });
        };
        launch.PodsUpdated += (pid, cur, max) => { var st = Get(pid); st.Pods = (cur, max); st.PodsAuthoritative = max > 0; };
    }
}
