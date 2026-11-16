using System;
using System.Collections.Generic;

namespace GivreClone.Protocol;

/// <summary>
/// Géométrie des ZONES de sort Dofus (formes d'AoE), portée d'un bot tiers (Combat\Zones\*.vb) sur les primitives
/// <see cref="MapPathfinder"/> (repère MapPoint standard, métrique diamant = Manhattan). <c>shapeCode</c> = code ASCII
/// de la lettre de zone Ankama (ex 'X'=88 croix, 'C'=67 losange plein, 'V'=86 cône). <see cref="GetCells"/> rend les
/// cellules réellement touchées par un sort lancé sur <c>center</c>, orienté <c>dir</c> (0-7) pour les formes
/// directionnelles, rayon <c>radius</c> (rayon interne <c>min</c> pour les anneaux). <c>passable(cell)</c> = cellule
/// franchissable en combat : les lignes/cônes s'y arrêtent (murs), et les formes pleines ne retiennent que ces cellules.
/// Sert au ciblage AoE correct + au test « allié dans la zone » (anti-friendly-fire). Purement géométrique (0 réseau).
/// </summary>
public static class CombatZones
{
    // Vecteurs unitaires des 8 directions dans le repère MapPoint.
    private static readonly (int Dx, int Dy)[] Dirs =
        { (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1), (0, 1) };

    /// <summary>Direction 0-7 de <c>from</c> vers <c>to</c> (meilleure des 8 par cosinus). Le produit scalaire doit être
    /// normalisé : sans cela un vecteur diagonal (longueur √2) égale l'axe sur un voisin cardinal — (0,-1) donnait la
    /// diagonale 2 au lieu de 3, et une zone en T devenait une croix complète incluant le lanceur.</summary>
    public static int OrientationTo(int from, int to)
    {
        var (fx, fy) = MapPathfinder.MapPointOf(from);
        var (tx, ty) = MapPathfinder.MapPointOf(to);
        int dx = tx - fx, dy = ty - fy;
        if (dx == 0 && dy == 0) return 1;
        int best = 1; double bestCos = double.MinValue;
        for (int d = 0; d < 8; d++)
        {
            double cos = (dx * Dirs[d].Dx + dy * Dirs[d].Dy) / (d % 2 == 0 ? Math.Sqrt(2) : 1.0);
            if (cos > bestCos + 1e-9) { bestCos = cos; best = d; }
        }
        return best;
    }

    /// <summary>Cellules touchées par une zone <c>shapeCode</c> centrée sur <c>center</c>. Forme inconnue / mono-cible =
    /// la cellule centrale seule.</summary>
    public static IReadOnlyList<int> GetCells(int shapeCode, int center, int dir, int min, int radius, Func<int, bool>? passable = null)
    {
        var set = new HashSet<int>();
        var (cx, cy) = MapPathfinder.MapPointOf(center);
        Func<int, bool> pass = passable ?? (_ => true);
        switch (shapeCode)
        {
            case 35: Cross(set, cx, cy, 1, radius, diagonal: true, all: false, onlyPerp: false, dir, pass); break;   // '#'
            case 42: Cross(set, cx, cy, 0, radius, false, all: true, onlyPerp: false, dir, pass); break;             // '*'
            case 43: Cross(set, cx, cy, 0, radius, diagonal: true, all: false, onlyPerp: false, dir, pass); break;   // '+'
            case 45: Cross(set, cx, cy, 0, radius, diagonal: true, all: false, onlyPerp: true, dir, pass); break;    // '-'
            case 47: case 76: Line(set, cx, cy, min, radius, dir, pass); break;                                      // '/', 'L'
            case 65: Lozenge(set, cx, cy, 0, 63, pass); break;                                                       // 'A' (carte entière)
            case 67: Lozenge(set, cx, cy, 0, radius, pass); break;                                                   // 'C' losange plein
            case 68: case 88: Cross(set, cx, cy, 0, radius, false, false, false, dir, pass); break;                  // 'D','X' croix
            case 71: Square(set, cx, cy, 0, radius, diagFree: false, pass); break;                                   // 'G' carré
            case 73: Lozenge(set, cx, cy, radius, 63, pass); break;                                                  // 'I' anneau externe
            case 79: Lozenge(set, cx, cy, radius, radius, pass); break;                                              // 'O' anneau exact
            case 81: Cross(set, cx, cy, 1, radius, false, false, false, dir, pass); break;                           // 'Q' croix sans centre
            case 84: Cross(set, cx, cy, 0, radius, false, false, onlyPerp: true, dir, pass); break;                  // 'T' demi-croix
            case 85: HalfLozenge(set, cx, cy, 0, radius, dir, pass); break;                                          // 'U' demi-losange
            case 86: Cone(set, cx, cy, 0, radius, dir, pass); break;                                                 // 'V' cône
            case 87: Square(set, cx, cy, 0, radius, diagFree: true, pass); break;                                    // 'W' carré sans diagonales
            default: Put(set, cx, cy, pass); break;                                                                  // inconnu / mono-cible
        }
        return new List<int>(set);
    }

    /// <summary>Formes simples AVEC rayon minimal:
    /// 'S' carré (Tchebychev ∈ [min, radius]), 'X' croix cardinale, 'C' losange (Manhattan ∈ [min, radius]), 'D' diagonales
    /// du carré. min = 0 inclut le centre. Aucune notion de mur (géométrie pure).</summary>
    public static IReadOnlyList<int> SimpleShape(char kind, int center, int min, int radius)
    {
        var set = new HashSet<int>();
        var (cx, cy) = MapPathfinder.MapPointOf(center);
        min = Math.Max(0, min); radius = Math.Max(0, radius);
        Func<int, bool> pass = _ => true;
        switch (char.ToUpperInvariant(kind))
        {
            case 'S': Square(set, cx, cy, min, radius, diagFree: false, pass); break;
            case 'X': Cross(set, cx, cy, min, radius, diagonal: false, all: false, onlyPerp: false, 1, pass); break;
            case 'C': Lozenge(set, cx, cy, min, radius, pass); break;
            case 'D':
                for (int i = Math.Max(min, 0); i <= radius; i++)
                {
                    if (i == 0) { Put(set, cx, cy, pass); continue; }
                    Put(set, cx + i, cy + i, pass); Put(set, cx - i, cy - i, pass);
                    Put(set, cx + i, cy - i, pass); Put(set, cx - i, cy + i, pass);
                }
                break;
        }
        return new List<int>(set);
    }

    /// <summary>Deux cellules sur une même ligne (même X ou même Y dans le repère MapPoint).</summary>
    public static bool Aligned(int a, int b)
    {
        var (ax, ay) = MapPathfinder.MapPointOf(a);
        var (bx, by) = MapPathfinder.MapPointOf(b);
        return ax == bx || ay == by;
    }

    /// <summary>Deux cellules sur une même diagonale (|dx| == |dy| dans le repère MapPoint).</summary>
    public static bool InDiagonal(int a, int b)
    {
        var (ax, ay) = MapPathfinder.MapPointOf(a);
        var (bx, by) = MapPathfinder.MapPointOf(b);
        return Math.Abs(ax - bx) == Math.Abs(ay - by);
    }

    private static void Put(HashSet<int> set, int x, int y, Func<int, bool> pass)
    {
        int c = MapPathfinder.CellFromMapPoint(x, y);
        if (c >= 0 && pass(c)) set.Add(c);
    }

    // Croix : bras cardinaux (le long de X/Y) et/ou diagonaux, de rayon `radius`, à partir de `min`.
    // AllDirections = les deux jeux (étoile 8 bras) ; Diagonal = bras diagonaux ; sinon cardinaux. OnlyPerpendicular
    // restreint les bras cardinaux à l'axe perpendiculaire à `dir` (approx pour les formes rares '-'/'T').
    private static void Cross(HashSet<int> set, int cx, int cy, int min, int radius, bool diagonal, bool all, bool onlyPerp, int dir, Func<int, bool> pass)
    {
        if (min == 0) Put(set, cx, cy, pass);
        bool keepX = true, keepY = true;   // bras (cx±i,cy) le long de X / (cx,cy±i) le long de Y
        if (onlyPerp)
        {
            if (dir == 1 || dir == 5) keepX = false;       // dir sur X -> garde l'axe Y
            else if (dir == 3 || dir == 7) keepY = false;  // dir sur Y -> garde l'axe X
        }
        for (int i = radius; i >= 1; i--)
        {
            if (i < min) continue;
            if (!diagonal || all)
            {
                if (keepX) { Put(set, cx + i, cy, pass); Put(set, cx - i, cy, pass); }
                if (keepY) { Put(set, cx, cy + i, pass); Put(set, cx, cy - i, pass); }
            }
            if (diagonal || all)
            {
                Put(set, cx + i, cy - i, pass); Put(set, cx - i, cy + i, pass);
                Put(set, cx + i, cy + i, pass); Put(set, cx - i, cy - i, pass);
            }
        }
    }

    // Ligne directionnelle: avance case par case dans `dir`, s'ARRÊTE au 1er mur / hors-carte.
    private static void Line(HashSet<int> set, int cx, int cy, int min, int radius, int dir, Func<int, bool> pass)
    {
        if (min <= 0) Put(set, cx, cy, pass);
        var (dx, dy) = Dirs[dir & 7];
        for (int i = Math.Max(1, min); i <= radius; i++)
        {
            int x = cx + dx * i, y = cy + dy * i;
            int c = MapPathfinder.CellFromMapPoint(x, y);
            if (c < 0 || !pass(c)) break;   // hors carte ou mur -> tronque
            set.Add(c);
        }
    }

    // Losange : toutes les cellules dont la distance diamant (Manhattan) ∈ [min, max]. min>0 = anneau ; min=max = anneau exact.
    private static void Lozenge(HashSet<int> set, int cx, int cy, int min, int max, Func<int, bool> pass)
    {
        for (int dx = -max; dx <= max; dx++)
            for (int dy = -max; dy <= max; dy++)
            {
                int m = Math.Abs(dx) + Math.Abs(dy);
                if (m < min || m > max) continue;
                Put(set, cx + dx, cy + dy, pass);
            }
    }

    // Carré : distance de Tchebychev (max) ∈ [min, radius]. DiagonalFree ('W') retire les diagonales |dx|==|dy|.
    private static void Square(HashSet<int> set, int cx, int cy, int min, int radius, bool diagFree, Func<int, bool> pass)
    {
        for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) < min) continue;
                if (diagFree && Math.Abs(dx) == Math.Abs(dy)) continue;
                Put(set, cx + dx, cy + dy, pass);
            }
    }

    // Cône directionnel : coin s'élargissant dans `dir` (diagonale coercée en cardinale la plus proche).
    private static void Cone(HashSet<int> set, int cx, int cy, int min, int radius, int dir, Func<int, bool> pass)
    {
        int d = dir & 7;
        if ((d & 1) == 0) d = (d + 1) & 7;   // 0/2/4/6 (diagonales) -> cardinale
        for (int c = 0; c <= radius; c++)
            for (int w = -c; w <= c; w++)
            {
                int x, y;
                switch (d)
                {
                    case 1: x = cx + c; y = cy + w; break;   // +X
                    case 5: x = cx - c; y = cy + w; break;   // -X
                    case 3: x = cx + w; y = cy - c; break;   // -Y
                    default: x = cx + w; y = cy + c; break;  // 7 : +Y
                }
                if (Math.Abs(cx - x) + Math.Abs(cy - y) < min) continue;
                Put(set, x, y, pass);
            }
    }

    // Demi-losange : la moitié du losange orientée vers `dir` (produit scalaire >= 0 avec le vecteur direction).
    private static void HalfLozenge(HashSet<int> set, int cx, int cy, int min, int radius, int dir, Func<int, bool> pass)
    {
        var (ux, uy) = Dirs[dir & 7];
        for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            {
                int m = Math.Abs(dx) + Math.Abs(dy);
                if (m < min || m > radius) continue;
                if (dx * ux + dy * uy < 0) continue;   // garde la moitié vers `dir`
                Put(set, cx + dx, cy + dy, pass);
            }
    }
}
