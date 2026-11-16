using System;
using System.Collections.Generic;

namespace GivreClone.Protocol;

/// <summary>
/// Pathfinding sur la grille Dofus (560 cellules) + compression en keyMovements pour MapMovementRequest.
/// Porte de JsPathFinder: conversion cellId <-> point axonometrique (x,y), A* 8-connexe, et
/// CompressPath qui encode chaque point de rupture en keyMovement = cellId | (orientation &lt;&lt; 12).
/// La walkabilite vient du delegue (cellules mov du bundle client). Retourne les keyCells prets pour jrw.
/// </summary>
public static class MapPathfinder
{
    public const int CellCount = 560;

    private static readonly (int X, int Y)[] _point = new (int, int)[CellCount];
    private static readonly Dictionary<int, int> _cellOf = new(CellCount);
    // Repere "MapPoint" STANDARD Dofus (x,y) -> cellId : celui des coordonnees du MapMovementRefusedEvent (jqt),
    // DISTINCT du repere interne du pathfinder ci-dessus. Table inverse pre-calculee.
    private static readonly Dictionary<(int X, int Y), int> _cellOfMapPoint = new(CellCount);
    private static readonly (int X, int Y)[] _mapPoint = new (int, int)[CellCount];   // cellId -> MapPoint standard (x,y)

    // Topologie immuable, ordonnee comme les boucles dx/dy de l'A* historique.
    // Les filtres de marche, coins et secteurs restent evalues POUR CHAQUE requete : aucun cache de carte/session.
    private readonly struct PathMove
    {
        public readonly int Next, Corner1, Corner2;
        public readonly bool Diagonal;

        public PathMove(int next, int corner1, int corner2, bool diagonal)
        {
            Next = next;
            Corner1 = corner1;
            Corner2 = corner2;
            Diagonal = diagonal;
        }
    }

    private static readonly PathMove[] _pathMoves = new PathMove[CellCount * 8];

    static MapPathfinder()
    {
        for (int id = 0; id < CellCount; id++)
        {
            int row = id % 14 - id / 28;         // maths JS d'origine
            int x = row + 19, y = row + id / 14;
            _point[id] = (x, y);
            _cellOf[Key(x, y)] = id;

            // Point-carte standard : loc2=id/14 ; loc3=(loc2+1)/2 ; X=loc3+(id-loc2*14) ; Y=(id-loc2*14)-(loc2-loc3)
            // (verifie un bot tiers getCellIdByCoord + un bot tiers MapPoint: cell 240 <-> (11,-6)).
            int loc2 = id / 14, loc3 = (loc2 + 1) / 2, rem = id - loc2 * 14;
            int mx = loc3 + rem, my = rem - (loc2 - loc3);
            _cellOfMapPoint[(mx, my)] = id;
            _mapPoint[id] = (mx, my);
        }

        // Tous les CellAt ci-dessous s'effectuent APRES la construction complete de _cellOf.
        for (int id = 0; id < CellCount; id++)
        {
            var (x, y) = _point[id];
            int index = id * 8;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    bool diagonal = dx != 0 && dy != 0;
                    _pathMoves[index++] = new PathMove(CellAt(x + dx, y + dy),
                        diagonal ? CellAt(x + dx, y) : -1,
                        diagonal ? CellAt(x, y + dy) : -1, diagonal);
                }
        }
    }

    /// <summary>MapPoint STANDARD Dofus (x,y) d'une cellule.</summary>
    public static (int X, int Y) MapPointOf(int cell) => (uint)cell < CellCount ? _mapPoint[cell] : (0, 0);

    /// <summary>Distance d'INTERACTION Dofus entre deux cellules (metrique diamant = Manhattan sur le MapPoint standard).
    /// C'est la distance comparee au `range` d'un skill a portee &gt; 1 (peche a 10 cases) et des sorts/attaques. Pour USER un
    /// interactif a portee 1, le client se place dans l'une des 8 voisines : voir <see cref="InteractionAdjacent"/>.</summary>
    public static int CellDistance(int a, int b)
    {
        if ((uint)a >= CellCount || (uint)b >= CellCount) return int.MaxValue;
        var (ax, ay) = _mapPoint[a];
        var (bx, by) = _mapPoint[b];
        return Math.Abs(ax - bx) + Math.Abs(ay - by);
    }

    /// <summary>Cases traversees de <paramref name="from"/> a <paramref name="to"/> inclus, pas a pas (ligne, colonne ou
    /// diagonale iso) ; deux cases non alignees (echange de places) ne renvoient que leurs deux extremites.</summary>
    public static IEnumerable<int> StraightLine(int from, int to)
    {
        yield return from;
        if (from == to || (uint)from >= CellCount || (uint)to >= CellCount) yield break;
        var (x, y) = _mapPoint[from];
        var (tx, ty) = _mapPoint[to];
        int dx = Math.Sign(tx - x), dy = Math.Sign(ty - y);
        if (dx != 0 && dy != 0 && Math.Abs(tx - x) != Math.Abs(ty - y)) { yield return to; yield break; }
        while ((x, y) != (tx, ty))
        {
            x += dx; y += dy;
            int cell = CellFromMapPoint(x, y);
            if (cell < 0) yield break;
            yield return cell;
        }
    }

    /// <summary>Deux cellules sont-elles ALIGNEES (meme ligne ou meme colonne iso) - condition d'un sort "en ligne
    /// droite".</summary>
    public static bool Aligned(int a, int b)
    {
        if ((uint)a >= CellCount || (uint)b >= CellCount) return false;
        var (ax, ay) = _mapPoint[a];
        var (bx, by) = _mapPoint[b];
        return ax == bx || ay == by;
    }

    /// <summary>Deux cellules sont-elles en DIAGONALE (meme ecart en X et en Y, iso) - condition d'un sort "en diagonale"
    /// (castInDiagonal, symetrique de <see cref="Aligned"/> : |dx| == |dy|, non nul). CONTRAINTE geometrique posee PAR-DESSUS
    /// la portee diamant existante (comme castInLine), pas une portee doublee : nos sorts en ligne marchent deja ainsi.</summary>
    public static bool Diagonal(int a, int b)
    {
        if ((uint)a >= CellCount || (uint)b >= CellCount) return false;
        var (ax, ay) = _mapPoint[a];
        var (bx, by) = _mapPoint[b];
        int dx = Math.Abs(ax - bx), dy = Math.Abs(ay - by);
        return dx != 0 && dx == dy;
    }

    /// <summary>Cellules traversees entre les centres de a et b, extremites comprises.
    /// Les distances aux prochains bords sont comparees par produits entiers : aucun arrondi de pente
    /// ne peut omettre une cellule opaque selon le sens du tir. Un coin touche sans traverser
    /// l'interieur d'une cellule laterale conserve le passage diagonal.</summary>
    public static IReadOnlyList<int> LineCells(int a, int b)
    {
        var list = new List<int>();
        if ((uint)a >= CellCount || (uint)b >= CellCount) return list;
        var (sx0, sy0) = _mapPoint[a];
        var (ex0, ey0) = _mapPoint[b];
        void Add(int cx, int cy) { if (_cellOfMapPoint.TryGetValue((cx, cy), out int id) && !list.Contains(id)) list.Add(id); }
        Add(sx0, sy0);   // extremite de depart (HasLineOfSight exclut de toute facon les 2 extremites)

        int dx = Math.Abs(ex0 - sx0), dy = Math.Abs(ey0 - sy0);
        int stepX = Math.Sign(ex0 - sx0), stepY = Math.Sign(ey0 - sy0);
        int x = sx0, y = sy0, crossedX = 0, crossedY = 0;
        while (x != ex0 || y != ey0)
        {
            int nextX = (2 * crossedX + 1) * dy;
            int nextY = (2 * crossedY + 1) * dx;
            if (dy == 0 || dx != 0 && nextX < nextY)
            {
                x += stepX;
                crossedX++;
            }
            else if (dx == 0 || nextY < nextX)
            {
                y += stepY;
                crossedY++;
            }
            else
            {
                x += stepX;
                y += stepY;
                crossedX++;
                crossedY++;
            }
            Add(x, y);
        }
        return list;
    }

    /// <summary>Ligne de vue entre a et b: bloquee si une cellule INTERMEDIAIRE bloque la
    /// vue (blocksLos, ex los=false) ou est occupee par un combattant (occupied). Extremites exclues.</summary>
    public static bool HasLineOfSight(int a, int b, Func<int, bool> blocksLos, Func<int, bool> occupied)
    {
        foreach (var c in LineCells(a, b))
            if (c != a && c != b && (blocksLos(c) || occupied(c))) return false;
        return true;
    }

    /// <summary>Cellules MARCHABLES d'ou l'on peut UTILISER un interactif en cellule `target` avec une portee `range`
    /// (distance d'interaction &lt;= range), triees de la plus proche a la plus loin de `from`. Sert a choisir ou se
    /// placer pour la peche (range 10 -&gt; toute case a &lt;= 10 du poisson) et pour l'attaque au corps a corps (range 1 -&gt;
    /// les 4 cases adjacentes, metrique diamant). Les interactifs a portee 1 passent par <see cref="StandCellFor"/> (8 voisines).</summary>
    public static IReadOnlyList<int> StandCellsInRange(int target, int range, int from, Func<int, bool> isWalkable)
    {
        var list = new List<int>();
        if ((uint)target >= CellCount) return list;
        if (range < 1) range = 1;
        for (int c = 0; c < CellCount; c++)
        {
            if (c == target || !isWalkable(c)) continue;
            if (CellDistance(c, target) <= range) list.Add(c);
        }
        list.Sort((c1, c2) => CellDistance(c1, from).CompareTo(CellDistance(c2, from)));
        return list;
    }

    // Les 8 voisines d'interaction en MapPoint : d'abord les 4 ORTHOGONALES iso, puis les 4 DIAGONALES.
    private static readonly (int Dx, int Dy)[] _interOrtho = { (1, 0), (-1, 0), (0, 1), (0, -1) };
    private static readonly (int Dx, int Dy)[] _interDiag = { (1, 1), (-1, -1), (1, -1), (-1, 1) };

    /// <summary>Voisines d'INTERACTION d'une cellule (8-voisinage Dofus, comme le vrai client qui se place dans l'une des 8
    /// directions) : d'abord les 4 ORTHOGONALES iso (±14 et ±13/±15 selon la parité de la ligne), puis les 4 DIAGONALES
    /// (±1 sur la même ligne, ±28). Calculé via le MapPoint -&gt; parité des lignes et bords de carte justes par construction
    /// (aucun décalage brut). Vide si la cellule est hors grille.</summary>
    public static IReadOnlyList<int> InteractionNeighbours(int cell)
    {
        var l = new List<int>(8);
        if ((uint)cell >= CellCount) return l;
        var (x, y) = _mapPoint[cell];
        foreach (var (dx, dy) in _interOrtho) { int n = CellFromMapPoint(x + dx, y + dy); if (n >= 0) l.Add(n); }
        foreach (var (dx, dy) in _interDiag) { int n = CellFromMapPoint(x + dx, y + dy); if (n >= 0) l.Add(n); }
        return l;
    }

    /// <summary>`a` et `b` sont-elles VOISINES d'interaction (8-voisinage : distance de Tchebychev == 1 sur le MapPoint) ?
    /// Une cellule n'est jamais sa propre voisine. Pas de règle du coin ici : le serveur accepte l'usage depuis une diagonale
    /// dont les deux coins sont bloqués (porte 352 utilisée depuis 380 par le vrai client).</summary>
    public static bool InteractionAdjacent(int a, int b)
    {
        if ((uint)a >= CellCount || (uint)b >= CellCount || a == b) return false;
        var (ax, ay) = _mapPoint[a];
        var (bx, by) = _mapPoint[b];
        return Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by)) == 1;
    }

    /// <summary>Case d'où UTILISER un interactif (voir la surcharge avec <c>moveWalkable</c>). N'expose PAS le prédicat de
    /// marche : réservée aux appelants qui ne se DÉPLACENT pas vers la case rendue (tests, simple lecture). Tout appelant
    /// qui marche jusqu'à la case DOIT utiliser la surcharge et passer <c>moveWalkable</c> au déplacement.</summary>
    public static int? StandCellFor(int source, int targetCell, int range, int? canonicalCell,
                                    Func<int, bool> isWalkable, Func<int, int, bool>? stepAllowed)
        => StandCellFor(source, targetCell, range, canonicalCell, isWalkable, stepAllowed, out _);

    /// <summary>Case d'où UTILISER un interactif en `targetCell` avec une portée `range` depuis `source`.
    /// <para>Portée &gt; 1 (pêche) : la source si déjà à portée (distance diamant), sinon la 1re case à portée joignable
    /// (plus proche d'abord), sinon null. <paramref name="moveWalkable"/> = <paramref name="isWalkable"/>.</para>
    /// <para>Portée &lt;= 1 (récolte, porte, zaap, hôtelier, mécanisme…) : on se place sur une VOISINE de l'élément
    /// (8-voisinage), jamais SUR sa cellule, et on n'y MARCHE jamais non plus en chemin (plantes sur cases marchables :
    /// log 24/09 17:10, Ortie 337, bot en 212 -&gt; 322 via 212&gt;…&gt;338&gt;337&gt;322 alors que 338 était sur la route).
    /// (a) Source déjà voisine (orthogonale OU diagonale) -&gt; source, sans bouger.
    /// (b) Sinon, parmi les candidates (<see cref="InteractionStandCandidates"/> : `canonicalCell` SEULEMENT si vraie case
    /// d'appui, puis 4 orthogonales, puis 4 diagonales) marchables, celle au chemin le PLUS COURT (en pas, même A* et mêmes
    /// règles que le déplacement réel) SANS traverser la cellule de l'élément (<see cref="InteractionMovePredicate"/>) ; à
    /// égalité, l'ordre des candidates (canonique, orthogonale, diagonale). <paramref name="moveWalkable"/> = ce prédicat
    /// qui EXCLUT la cellule de l'élément : l'appelant DOIT le passer au MoveTo, sinon l'A* du déplacement peut encore
    /// couper par la plante.
    /// (b') Repli défensif (aucune candidate joignable en contournant l'élément — en pratique impossible : tout chemin qui
    /// traverse l'élément passe d'abord par l'une de ses 8 voisines) : l'ancienne sélection (1re candidate joignable avec
    /// <paramref name="isWalkable"/> brut) et <paramref name="moveWalkable"/> = <paramref name="isWalkable"/>.
    /// (c) Dernier recours : le perso est déjà SUR la cellule (marchable) de l'élément et aucune voisine n'est joignable
    /// -&gt; on reste (le serveur accepte l'usage depuis la cellule). Sinon null (injoignable).</para></summary>
    public static int? StandCellFor(int source, int targetCell, int range, int? canonicalCell,
                                    Func<int, bool> isWalkable, Func<int, int, bool>? stepAllowed,
                                    out Func<int, bool> moveWalkable)
    {
        moveWalkable = isWalkable;
        if (range > 1)
        {
            if (source == targetCell || CellDistance(source, targetCell) <= range) return source;   // déjà à portée
            foreach (var c in StandCellsInRange(targetCell, range, source, isWalkable))
                if (KeyMovements(source, c, isWalkable, allowDiag: true, stepAllowed).Count > 0) return c;
            return null;
        }

        if ((uint)targetCell >= CellCount) return null;
        var avoid = InteractionMovePredicate(targetCell, isWalkable);   // la source reste permise (FindPath ne la teste pas)
        if (InteractionAdjacent(source, targetCell)) { moveWalkable = avoid; return source; }   // (a) déjà placé

        var cands = InteractionStandCandidates(source, targetCell, canonicalCell);
        int best = -1, bestSteps = int.MaxValue;
        foreach (var c in cands)   // (b) chemin le plus court qui CONTOURNE l'élément (au plus 8 recherches A*)
        {
            if (c == source) { moveWalkable = avoid; return source; }   // (défensif : couvert par (a))
            if (!avoid(c)) continue;
            if (Distance(source, c) >= bestSteps) continue;   // borne basse (Tchebychev) : ne peut pas faire STRICTEMENT mieux
            int steps = PathSteps(source, c, avoid, stepAllowed);
            if (steps > 0 && steps < bestSteps) { best = c; bestSteps = steps; }   // strict : égalité -> ordre des candidates
        }
        if (best >= 0) { moveWalkable = avoid; return best; }

        foreach (var c in cands)   // (b') repli défensif : sélection historique, prédicat brut
        {
            if (!isWalkable(c)) continue;
            if (KeyMovements(source, c, isWalkable, allowDiag: true, stepAllowed).Count > 0) return c;
        }
        return source == targetCell ? source : null;   // (c) déjà SUR l'élément, aucune voisine joignable -> on reste
    }

    /// <summary>Prédicat de marche pour REJOINDRE la case d'appui d'un élément à portée 1 : <paramref name="isWalkable"/>
    /// privé de la cellule de l'élément (une plante sur case marchable ne doit jamais être traversée). Sans effet pour une
    /// porte/zaap (cellule déjà non marchable).</summary>
    public static Func<int, bool> InteractionMovePredicate(int targetCell, Func<int, bool> isWalkable)
        => c => c != targetCell && isWalkable(c);

    // Nombre de PAS du chemin que le déplacement réel suivrait (même FindPath que KeyMovements), -1 si aucun chemin.
    private static int PathSteps(int source, int target, Func<int, bool> isWalkable, Func<int, int, bool>? stepAllowed)
    {
        if (source == target || (uint)source >= CellCount || (uint)target >= CellCount) return -1;
        var path = FindPath(source, target, isWalkable, allowDiag: true, stepAllowed);
        return path is { Count: >= 2 } ? path.Count - 1 : -1;
    }

    /// <summary>Candidates ORDONNÉES (portée 1) d'où utiliser l'élément en `target` : `canonical` s'il est une vraie case
    /// d'appui (≠ cible et voisine d'elle), puis les 4 orthogonales, puis les 4 diagonales — chaque groupe trié par distance
    /// (diamant) à `source`, tri STABLE (ordre de <see cref="InteractionNeighbours"/> à égalité). La cible n'y figure jamais.
    /// Ni marchabilité ni joignabilité ici : c'est à l'appelant (<see cref="StandCellFor"/>, qui retient le chemin le plus
    /// court et ne se sert de cet ordre que pour DÉPARTAGER les égalités).</summary>
    public static IReadOnlyList<int> InteractionStandCandidates(int source, int target, int? canonical)
    {
        var list = new List<int>(9);
        if ((uint)target >= CellCount) return list;
        if (canonical is int ic && ic != target && InteractionAdjacent(ic, target)) list.Add(ic);
        var (x, y) = _mapPoint[target];
        void AddGroup((int Dx, int Dy)[] offsets)
        {
            var g = new List<int>(4);
            foreach (var (dx, dy) in offsets)
            {
                int n = CellFromMapPoint(x + dx, y + dy);
                if (n >= 0 && !list.Contains(n)) g.Add(n);
            }
            list.AddRange(g.OrderBy(c => CellDistance(c, source)));   // OrderBy = tri stable
        }
        AddGroup(_interOrtho);
        AddGroup(_interDiag);
        return list;
    }

    private static int Key(int x, int y) => (x << 8) | (y & 0xFF);
    private static int CellAt(int x, int y) => _cellOf.TryGetValue(Key(x, y), out int id) ? id : -1;

    /// <summary>Point-carte Ankama (x,y) -> cellId (0-559), ou -1 si hors grille. Systeme "MapPoint" standard Dofus
    /// (coordonnees du MapMovementRefusedEvent / jqt), a NE PAS confondre avec le repere interne du pathfinder.</summary>
    public static int CellFromMapPoint(int x, int y)
        => _cellOfMapPoint.TryGetValue((x, y), out int id) ? id : -1;

    /// <summary>Chemin cliquable -> keyMovements (jrw.key_cells). Vide si pas de chemin. isWalkable exclut la source
    /// (toujours accessible) ; la cible doit etre marchable. allowDiag=false -> deplacement ORTHOGONAL uniquement
    ///.</summary>
    /// <summary>Regle de CONNEXION Dofus 3 entre deux cellules ADJACENTES a-&gt;b (port EXACT d'un bot tiers
    /// PathFindingEngine.Communicates). Convention : <c>floor[c] == -1</c> ou <c>zone[c] == -1</c> = cellule NON marchable ;
    /// <c>zone[c] = (linkedZone &amp; 0xF8) &gt;&gt; 4</c> = secteur ; <c>canMoveZone[c] = (moveZone == 1)</c>. Deux cellules ne
    /// "communiquent" (pas legal) que si : meme etage, OU pont de zone (canMoveZone des deux + |denivele| &lt;= 70), OU meme
    /// secteur avec canMoveZone identique et |denivele| &lt;= 50. Sans ca le pathfinding traverse des cellules marchables mais
    /// de secteurs DECONNECTES -&gt; le serveur REFUSE tout le deplacement (aucun jsq/jqt : move ignore).</summary>
    public static bool Communicates(int a, int b, int[] floor, int[] zone, bool[] canMoveZone)
    {
        if ((uint)a >= CellCount || (uint)b >= CellCount ||
            floor.Length < CellCount || zone.Length < CellCount || canMoveZone.Length < CellCount) return true;
        int fa = floor[a], fb = floor[b], za = zone[a], zb = zone[b];
        if (fa == -1 || fb == -1 || za == -1 || zb == -1) return false;   // une des deux non marchable
        if (fa == fb) return true;                                        // meme etage
        // Le cast long avant la soustraction evite l'overflow sur les valeurs d'altitude malformees.
        long heightGap = Math.Abs((long)fa - fb);
        if (canMoveZone[a] && canMoveZone[b] && heightGap <= 70) return true;   // pont de zone
        if (za != zb) return false;                                       // secteurs differents -> deconnectes
        return (canMoveZone[a] == canMoveZone[b]) && heightGap <= 50;
    }

    public static IReadOnlyList<int> KeyMovements(int source, int target, Func<int, bool> isWalkable, bool allowDiag = true,
                                                  Func<int, int, bool>? stepAllowed = null)
    {
        if (source == target || (uint)source >= CellCount || (uint)target >= CellCount) return Array.Empty<int>();
        if (!isWalkable(target)) return Array.Empty<int>();
        var path = FindPath(source, target, isWalkable, allowDiag, stepAllowed);
        return path is { Count: >= 2 } ? Compress(path) : Array.Empty<int>();
    }

    /// <summary>Chemin A* exact, cellule par cellule et source comprise. Conserve les memes choix de route
    /// que <see cref="KeyMovements"/>, afin de reprendre un suffixe sans reconstruire les virages a partir
    /// des seuls keyMovements. La liste retournee ne partage aucun tampon mutable avec l'A*.</summary>
    public static IReadOnlyList<int> PathCells(int source, int target, Func<int, bool> isWalkable,
                                                bool allowDiag = true, Func<int, int, bool>? stepAllowed = null)
    {
        if (source == target || (uint)source >= CellCount || (uint)target >= CellCount)
            return Array.Empty<int>();
        if (!isWalkable(target)) return Array.Empty<int>();
        return FindPath(source, target, isWalkable, allowDiag, stepAllowed)?.ToArray() ?? Array.Empty<int>();
    }

    /// <summary>Revalide un chemin deja calcule avec les obstacles et secteurs ACTUELS.
    /// Chaque pas applique les memes contraintes que l'A* (source non testee, coin diagonal
    /// et permission orientee). Utile apres un checkpoint serveur, avant tout nouvel envoi.</summary>
    public static bool IsMovementPathLegal(IReadOnlyList<int>? cells, Func<int, bool> isWalkable,
                                            bool allowDiag = true, Func<int, int, bool>? stepAllowed = null)
    {
        if (cells is not { Count: >= 2 } || isWalkable is null) return false;
        for (int i = 1; i < cells.Count; i++)
        {
            int from = cells[i - 1], to = cells[i];
            if ((uint)from >= CellCount || (uint)to >= CellCount || !isWalkable(to)) return false;
            bool found = false;
            int offset = from * 8;
            for (int n = 0; n < 8; n++)
            {
                var move = _pathMoves[offset + n];
                if (move.Next != to || (!allowDiag && move.Diagonal)) continue;
                if (move.Diagonal)
                {
                    bool c1ok = move.Corner1 >= 0 && isWalkable(move.Corner1) &&
                                (stepAllowed is null || stepAllowed(from, move.Corner1));
                    bool c2ok = move.Corner2 >= 0 && isWalkable(move.Corner2) &&
                                (stepAllowed is null || stepAllowed(from, move.Corner2));
                    if (!c1ok && !c2ok) return false;
                }
                if (stepAllowed is not null && !stepAllowed(from, to)) return false;
                found = true;
                break;
            }
            if (!found) return false;
        }
        return true;
    }

    private static readonly (int Dx, int Dy)[] _ortho = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>Cases ORTHOGONALES atteignables depuis `from` en &lt;= `mp` pas (BFS 4-connexe, façon deplacement de combat
    /// allowDiag:false). Renvoie cell -&gt; nb de pas EXACT. Inclut `from` (0 pas). Bien plus fiable que N appels A* + un
    /// pre-filtre CellDistance : donne l'ensemble atteignable REEL avec la longueur de chemin exacte, en une passe.</summary>
    public static Dictionary<int, int> OrthoReachable(int from, int mp, Func<int, bool> isWalkable, Func<int, int, bool>? stepAllowed = null)
    {
        var dist = new Dictionary<int, int>();
        if ((uint)from >= CellCount || mp < 0) return dist;
        var q = new Queue<int>();
        dist[from] = 0; q.Enqueue(from);
        while (q.Count > 0)
        {
            int cur = q.Dequeue();
            int d = dist[cur];
            if (d >= mp) continue;
            var (cx, cy) = _point[cur];
            foreach (var (dx, dy) in _ortho)
            {
                int nb = CellAt(cx + dx, cy + dy);
                if (nb < 0 || dist.ContainsKey(nb) || !isWalkable(nb)) continue;
                if (stepAllowed != null && !stepAllowed(cur, nb)) continue;   // secteur déconnecté -> le serveur refuserait
                dist[nb] = d + 1; q.Enqueue(nb);
            }
        }
        return dist;
    }

    /// <summary>Champ de distance MARCHABLE (BFS 4-connexe) depuis `target` : cell -&gt; nb de pas orthogonaux pour ATTEINDRE
    /// `target` en ne traversant que des cases marchables. La cible (occupee par un combattant) sert de graine a 0 et n'est
    /// PAS elle-meme marchable -&gt; on n'etend qu'aux voisines marchables. Sert au rapprochement de combat : contrairement a
    /// la distance a vol d'oiseau (CellDistance), ce champ CONTOURNE les murs -&gt; le bot avance vers l'ouverture au lieu de
    /// se figer face a un obstacle.</summary>
    public static Dictionary<int, int> OrthoDistanceField(int target, Func<int, bool> isWalkable, Func<int, int, bool>? stepAllowed = null)
    {
        var dist = new Dictionary<int, int>();
        if ((uint)target >= CellCount) return dist;
        var q = new Queue<int>();
        dist[target] = 0; q.Enqueue(target);
        while (q.Count > 0)
        {
            int cur = q.Dequeue();
            int d = dist[cur];
            var (cx, cy) = _point[cur];
            foreach (var (dx, dy) in _ortho)
            {
                int nb = CellAt(cx + dx, cy + dy);
                if (nb < 0 || dist.ContainsKey(nb) || !isWalkable(nb)) continue;
                if (stepAllowed != null && !stepAllowed(cur, nb)) continue;   // même connectivité que le déplacement réel
                dist[nb] = d + 1; q.Enqueue(nb);
            }
        }
        return dist;
    }

    private static List<int>? FindPath(int source, int target, Func<int, bool> isWalkable, bool allowDiag = true,
                                       Func<int, int, bool>? stepAllowed = null)
    {
        var open = new PriorityQueue<int, int>();
        // CellCount est borne a 560 : l'etat A* tient dans des spans locaux fixes, meme pour les appels multi-cibles.
        // Un etat propre a chaque appel evite toute reutilisation de graphe apres changement de map/secteur.
        Span<int> came = stackalloc int[CellCount];
        Span<int> g = stackalloc int[CellCount];
        Span<byte> closed = stackalloc byte[CellCount];
        came.Fill(-1);
        g.Fill(int.MaxValue);
        closed.Clear();
        g[source] = 0;
        open.Enqueue(source, HeurW(source, target));

        while (open.TryDequeue(out int cur, out _))
        {
            if (cur == target)
            {
                var path = new List<int> { cur };
                while (came[cur] >= 0) { cur = came[cur]; path.Add(cur); }
                path.Reverse();
                return path;
            }
            if (closed[cur] != 0) continue;
            closed[cur] = 1;
            int moveOffset = cur * 8;
            for (int i = 0; i < 8; i++)
            {
                var move = _pathMoves[moveOffset + i];
                if (!allowDiag && move.Diagonal) continue; // combat : pas de diagonale
                int nb = move.Next;
                if (nb < 0 || closed[nb] != 0) continue;
                if (nb != target && !isWalkable(nb)) continue;
                // REGLE DU COIN: un pas DIAGONAL n'est legal que si AU MOINS une des 2 cases
                // orthogonales du coin COMMUNIQUE avec la source (pas juste "marchable" : meme secteur/etage). Sans ca,
                // sur une carte encombree ou a secteurs, le serveur rejette EN BLOC le chemin -> le perso ne bouge pas.
                if (move.Diagonal)
                {
                    int c1 = move.Corner1, c2 = move.Corner2;
                    bool c1ok = c1 >= 0 && isWalkable(c1) && (stepAllowed == null || stepAllowed(cur, c1));
                    bool c2ok = c2 >= 0 && isWalkable(c2) && (stepAllowed == null || stepAllowed(cur, c2));
                    if (!c1ok && !c2ok) continue;   // aucun coin ne communique -> diagonale interdite
                }
                // Regle Dofus 3 (Communicates) : cur->nb doit relier deux cellules du MEME secteur (ou pont de zone).
                if (stepAllowed != null && !stepAllowed(cur, nb)) continue;
                // COUT PONDERE: pas orthogonal = 10, diagonal = 14 (~10*sqrt2). Un cout
                // UNIFORME (+1 partout) rend equivalents une multitude de chemins de meme longueur ; l'egalite etait
                // tranchee par l'ordre de la file -> chemin en ESCALIER/zigzag -> apres Compress ~27 keyCells (1 virage
                // par marche) la ou le client en produit ~9. Un escalier orthogonal (10+10) coute plus qu'une diagonale
                // (14), donc A* prefere desormais diagonales + lignes droites -> chemin LISSE identique a celui du vrai
                // client -> le serveur l'ACCEPTE.
                int step = move.Diagonal ? 14 : 10;
                int tentative = g[cur] + step;
                if (tentative < g[nb])
                {
                    came[nb] = cur;
                    g[nb] = tentative;
                    open.Enqueue(nb, tentative + HeurW(nb, target));
                }
            }
        }
        return null;
    }

    private static int Heur(int a, int b)
    {
        var (ax, ay) = _point[a];
        var (bx, by) = _point[b];
        return Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by)); // Chebyshev (8-connexe) : metrique "cellule" (Distance/exit)
    }

    // Heuristique OCTILE coherente avec le cout pondere 10/14 de FindPath (admissible : elle n'ecrit jamais plus que le
    // cout reel), pour garder A* rapide malgre le cout non-uniforme. = 10*max(dx,dy) + 4*min(dx,dy). Reservee au pathfinding
    // interne ; la metrique publique "en cellules" reste Heur/Distance (Chebyshev, 1 par pas).
    private static int HeurW(int a, int b)
    {
        var (ax, ay) = _point[a];
        var (bx, by) = _point[b];
        int dx = Math.Abs(ax - bx), dy = Math.Abs(ay - by);
        int lo = Math.Min(dx, dy), hi = Math.Max(dx, dy);
        return 10 * hi + 4 * lo;
    }

    /// <summary>Distance en cases (Chebyshev) entre deux cellules - sert a estimer la duree d'un deplacement.</summary>
    public static int Distance(int a, int b)
        => (uint)a >= CellCount || (uint)b >= CellCount ? 0 : Heur(a, b);

    /// <summary>DEROULE des points de virage (cellIds) vers le chemin CELLULE PAR CELLULE. Contrairement a un
    /// deroulage par bits de direction (convention Ankama incertaine -&gt; faux), on derive le pas du SIGNE de l'ecart
    /// (x,y) entre deux virages consecutifs et on avance via CellAt : les cellules restent SUR LA CARTE (0-559) et
    /// adjacentes. Segments non colineaires (rares) -&gt; on ajoute le virage directement (repli ligne droite).</summary>
    public static IReadOnlyList<int> ExpandPath(IReadOnlyList<int> turnCells)
    {
        var full = new List<int>();
        if (turnCells is null || turnCells.Count == 0) return full;
        if ((uint)turnCells[0] >= CellCount) return full;
        full.Add(turnCells[0]);

        for (int i = 0; i + 1 < turnCells.Count; i++)
        {
            int a = turnCells[i], b = turnCells[i + 1];
            if ((uint)a >= CellCount || (uint)b >= CellCount) { if ((uint)b < CellCount) full.Add(b); continue; }
            var (ax, ay) = _point[a];
            var (bx, by) = _point[b];
            int dx = Math.Sign(bx - ax), dy = Math.Sign(by - ay);
            int x = ax, y = ay, cur = a, guard = 0;
            while (cur != b && guard++ < CellCount && (dx != 0 || dy != 0))
            {
                x += dx; y += dy;
                int nb = CellAt(x, y);
                if (nb < 0) break;
                full.Add(nb);
                cur = nb;
            }
            if (cur != b) full.Add(b);   // repli : segment non colineaire -> on relie directement au virage
        }
        return full;
    }

    // Compresse le chemin en keyMovements : un element par changement d'orientation + la destination.
    public static List<int> Compress(List<int> path)
    {
        var cmp = new List<int>();
        int prev = path[0], prevDir = -1, px = 0, py = 0;
        for (int i = 0; i < path.Count; i++)
        {
            var (x, y) = _point[path[i]];
            int dir;
            if (i == 0) dir = -1;
            else if (y == py) dir = x > px ? 7 : 3;
            else if (x == px) dir = y > py ? 1 : 5;
            else dir = x > px ? (y > py ? 0 : 6) : (y > py ? 2 : 4);

            if (dir != prevDir && i != 0) { cmp.Add(prev | (dir << 12)); prevDir = dir; }
            prev = path[i]; px = x; py = y;
        }
        cmp.Add(prev | (prevDir << 12)); // destination
        return cmp;
    }
}
