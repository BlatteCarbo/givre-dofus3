using System.Collections.Generic;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>
/// Decode un MapMovementRefusedEvent (code obfusque jsq? -&gt; ici capte par code "jqt", S-&gt;C) : le serveur REFUSE le
/// deplacement et renvoie la position REELLE du perso en coordonnees point-carte. Champs wire (patch actuel) :
///   #1 = cell_x (int32, signe) ; #2 = cell_y (int32, signe).
/// On convertit (x,y) -&gt; cellId via le repere point-carte standard (MapPathfinder.CellFromMapPoint). C'est LA verite
/// serveur a utiliser pour recaler la position (sinon le bot re-tente sans fin le meme move depuis une cellule perimee).
/// Methode alignee sur un bot tiers (HandleMapMovementRefusedEvent: CharacterCellId = GetCellIdFromMapPoint(CellX, CellY)).
/// </summary>
public static class MapMovementRefusedReader
{
    /// <summary>(x, y, cellId) de la position reelle au refus, ou null si illisible / hors grille.</summary>
    public static (int X, int Y, int Cell)? Read(IReadOnlyList<ProtoField> tree, int xField = 1, int yField = 2)
    {
        var fx = tree.FirstOrDefault(f => f.Field == xField && f.Wire == WireType.Varint);
        var fy = tree.FirstOrDefault(f => f.Field == yField && f.Wire == WireType.Varint);
        // ⚠ PIÈGE PROTO3 : un champ dont la valeur est 0 est OMIS du wire (index 0 = champ ABSENT, pas illisible). Or les
        // coordonnées point-carte croisent 0 en permanence (une case sur X a x=0 OU y=0) -> exiger les DEUX champs présents
        // rejetait ~la moitié des refus comme « illisibles » => la position n'était jamais recalée et le bot RESTAIT BLOQUÉ
        // (moves refusés en boucle, jamais recalé). On lit donc un champ absent comme 0. null seulement si AUCUN des deux
        // (paquet sans position) ou case hors grille.
        if (fx is null && fy is null) return null;
        int x = fx is null ? 0 : (int)unchecked((long)fx.Varint);   // int32 signe (coordonnees negatives possibles)
        int y = fy is null ? 0 : (int)unchecked((long)fy.Varint);
        int cell = MapPathfinder.CellFromMapPoint(x, y);
        if (cell < 0) return null;
        return (x, y, cell);
    }
}
