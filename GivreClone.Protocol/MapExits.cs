using System.Collections.Generic;

namespace GivreClone.Protocol;

/// <summary>Direction de sortie d'une carte (bords). Correspond aux 4 voisins du client (top/bottom/left/right).</summary>
public enum MapDir { Top, Bottom, Left, Right }

/// <summary>
/// Sorties d'une carte, lues des VRAIES donnees du client (mapData.{top,bottom,left,right}NeighbourId + ...ArrowCellList).
/// Pour chaque direction : la map VOISINE (id, 0 si aucune) et d'eventuelles cellules-fleches de bord (souvent vides,
/// la traversee se fait alors librement par le bord). C'est la source fiable pour changer de carte (pas un calcul de
/// coordonnees). Le graphe mondial (world-graph) ne sert qu'a l'autopilote multi-cartes, pas au pas-a-pas directionnel.
/// </summary>
public sealed record MapExits(
    int TopNeighbour, int BottomNeighbour, int LeftNeighbour, int RightNeighbour,
    IReadOnlyList<int> TopArrows, IReadOnlyList<int> BottomArrows,
    IReadOnlyList<int> LeftArrows, IReadOnlyList<int> RightArrows)
{
    /// <summary>Id de la carte voisine dans la direction (0 = aucune).</summary>
    public int Neighbour(MapDir d) => d switch
    {
        MapDir.Top => TopNeighbour,
        MapDir.Bottom => BottomNeighbour,
        MapDir.Left => LeftNeighbour,
        MapDir.Right => RightNeighbour,
        _ => 0,
    };

    /// <summary>Cellules-fleches de bord dans la direction (vide si traversee libre).</summary>
    public IReadOnlyList<int> Arrows(MapDir d) => d switch
    {
        MapDir.Top => TopArrows,
        MapDir.Bottom => BottomArrows,
        MapDir.Left => LeftArrows,
        MapDir.Right => RightArrows,
        _ => System.Array.Empty<int>(),
    };

    /// <summary>Vrai s'il existe une carte voisine (id valide) dans cette direction.</summary>
    public bool HasNeighbour(MapDir d) => Neighbour(d) > 0;
}
