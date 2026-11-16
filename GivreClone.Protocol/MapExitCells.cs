using System.Collections.Generic;
using System.Linq;

namespace GivreClone.Protocol;

/// <summary>Cellules de SORTIE d'une carte dans une direction, d'après <c>mapChangeData</c> (bits : 1=E, 2=SE, 4=S, 8=SO,
/// 16=O, 32=NO, 64=N, 128=NE). Normalement le bit CARDINAL de la direction. Mais certaines cartes n'en portent AUCUN sur un
/// bord pourtant franchissable (ex zaap de Bonta 212600323 : bord sud sans bit 4, la vraie sortie 551 porte le bit 8 SO) :
/// on retombe alors sur les bits DIAGONAUX de ce bord, restreints à la rangée/colonne qui DÉPASSE de ce bord (règle du
/// client : seul là un bit diagonal sort par ce bord) et aux cellules géométriquement de ce bord (<see cref="WorldGraph.BorderOf"/>).
/// Grille Dofus : 14 colonnes × 40 rangées (0..559), rangées impaires décalées d'une demi-case vers la droite.</summary>
public static class MapExitCells
{
    public static int CardinalMask(MapDir d) => d switch
    {
        MapDir.Right => 1, MapDir.Bottom => 4, MapDir.Left => 16, MapDir.Top => 64, _ => 0,
    };

    public static int DiagonalMask(MapDir d) => d switch
    {
        MapDir.Right => 2 | 128, MapDir.Bottom => 2 | 8, MapDir.Left => 8 | 32, MapDir.Top => 32 | 128, _ => 0,
    };

    /// <summary>La cellule est-elle sur la rangée/colonne qui DÉPASSE dans la direction (seul endroit où un bit diagonal sort
    /// par ce bord) : bas = rangée 39, haut = rangée 0, droite = colonne 13 des rangées IMPAIRES, gauche = colonne 0 des
    /// rangées PAIRES. Une bande plus large ramassait les COINS du bord voisin (13/27/532/546 : sorties d'un AUTRE côté).</summary>
    public static bool InBorderStrip(int cell, MapDir d) => d switch
    {
        MapDir.Bottom => cell >= 546 && cell < 560,
        MapDir.Top => cell >= 0 && cell < 14,
        MapDir.Right => cell >= 0 && cell < 560 && cell % 28 == 27,
        MapDir.Left => cell >= 0 && cell < 560 && cell % 28 == 0,
        _ => false,
    };

    /// <summary>Cellules de sortie MARCHABLES dans la direction : bit cardinal ; si AUCUNE cellule de la carte ne porte ce bit,
    /// bits diagonaux dans la bande de bord. Vide = aucune sortie connue.</summary>
    public static List<MapCell> Candidates(IReadOnlyList<MapCell> cells, MapDir d)
    {
        int m = CardinalMask(d);
        if (m == 0) return new List<MapCell>();
        var res = cells.Where(c => (c.MapChangeData & m) != 0 && c.Mov).ToList();
        if (res.Count > 0 || cells.Any(c => (c.MapChangeData & m) != 0)) return res;   // bord « normal » : comportement historique
        int dm = DiagonalMask(d);
        // Coin partagé par deux bords (ex 559, 0): attribué au côté que donne BorderOf.
        return cells.Where(c => (c.MapChangeData & dm) != 0 && c.Mov && InBorderStrip(c.CellId, d)
                                && WorldGraph.BorderOf(c.CellId) == d).ToList();
    }
}
