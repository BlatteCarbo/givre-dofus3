using System.Collections.Generic;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>
/// Decode un MapMovementEvent (code obfusque jsj) : un acteur se deplace le long d'un chemin. Champs wire confirmes
/// (client actuel) : #1 = keyMovements (repeated int32 PACKED -> apparait en "bytes") ; #5 = actorId/contextualId
/// (int64, NEGATIF pour un non-joueur) ; (#2 direction, #6 cautious - non utilises ici).
///
/// keyMovement Ankama = ((orientation &amp; 7) &lt;&lt; 12) | (cellId &amp; 0xFFF). La liste est compressee RLE (depart, points
/// de changement de direction, arrivee). On renvoie TOUTES les cellules du chemin (pour l'animer) ; la DESTINATION
/// = la derniere. Numeros de champ fournis par l'appelant (fields.json) ; renvoie null si illisible.
/// </summary>
public static class MapMovementReader
{
    private const int CellCount = 560;

    /// <summary>(actorId, cellules du chemin) : cellId de chaque keyMovement (depart, virages, arrivee) - orientation
    /// ignoree. On anime en reliant ces points (pas de deroulage cellule-par-cellule : la table de directions Ankama
    /// n'est pas fiable de notre cote -> deroulage faux = acteurs sur des cases invalides). Null si illisible.</summary>
    public static (long ActorId, IReadOnlyList<int> Cells)? Read(IReadOnlyList<ProtoField> tree, int cellsField, int actorField)
    {
        var actorF = tree.FirstOrDefault(f => f.Field == actorField && f.Wire == WireType.Varint);
        if (actorF is null) return null;
        long actorId = unchecked((long)actorF.Varint);   // int64 signe (contextualId negatif possible)

        var cells = Cells(tree, cellsField);
        return cells.Count == 0 ? null : (actorId, cells);
    }

    /// <summary>cellId de chaque keyMovement d'un champ PACKED (ex #1 de jsj, #2 de jrw) : keyMovement &amp; 0xFFF.
    /// Liste vide si illisible.</summary>
    public static IReadOnlyList<int> Cells(IReadOnlyList<ProtoField> tree, int cellsField)
    {
        var cellsF = tree.FirstOrDefault(f => f.Field == cellsField && f.Wire == WireType.Len && f.Bytes is not null);
        if (cellsF?.Bytes is not { Length: > 0 } blob) return System.Array.Empty<int>();

        var cells = new List<int>();
        foreach (var v in Varints(blob))
        {
            int c = (int)(v & 0xFFF);
            if (c is >= 0 and < CellCount) cells.Add(c);
        }
        return cells;
    }

    // Lit une suite de varints (le champ packed repeated int32).
    private static IEnumerable<ulong> Varints(byte[] b)
    {
        int i = 0;
        while (i < b.Length)
        {
            ulong v = 0; int shift = 0;
            while (i < b.Length)
            {
                byte x = b[i++];
                v |= (ulong)(x & 0x7F) << shift;
                if ((x & 0x80) == 0) break;
                shift += 7;
            }
            yield return v;
        }
    }
}
