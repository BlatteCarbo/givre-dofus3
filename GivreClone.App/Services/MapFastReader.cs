using System;
using System.Collections.Generic;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

/// <summary>
/// Lecteur binaire SÉLECTIF d'une carte (MonoBehaviour « map_&lt;id&gt; ») : suit le type tree comme AssetsTools.NET (mêmes règles de taille et d'alignement,
/// cf. AssetTypeTemplateField.ReadType) mais ne matérialise QUE ce dont le cache a besoin (nom, cellsData, voisins, flèches, boîtes englobantes, références
/// « Interactive ») et SAUTE le reste sans allouer. <c>GetBaseField</c> construisait des centaines de milliers d'objets par carte (≈ 85 ms) pour n'en
/// garder que quelques centaines. Toute structure inattendue -&gt; null : l'appelant retombe sur l'ancienne lecture complète.
/// </summary>
internal static class MapFastReader
{
    private static readonly HashSet<string> Keep = new(StringComparer.Ordinal)
    {
        "m_Name", "mapData", "cellsData",
        "topNeighbourId", "bottomNeighbourId", "leftNeighbourId", "rightNeighbourId",
        "topArrowCellList", "bottomArrowCellList", "leftArrowCellList", "rightArrowCellList",
        "boundingBoxes", "references", "middlegroundMapElements",
    };

    internal static volatile string? LastError;   // diagnostic : dernière exception avalée par Read

    internal readonly record struct Result(string? Name, bool Accepted, (MapCell[] Cells, MapExits Exits, Dictionary<int, int> Inter)? Data);

    /// <summary>Lit une carte. <paramref name="accept"/> reçoit le nom (m_Name, lu en tête) : faux -&gt; arrêt immédiat (Accepted=false, rien d'autre lu).
    /// Data == null avec Accepted == true : structure inattendue, utiliser la lecture complète.</summary>
    public static Result Read(AssetsFileReader r, AssetTypeTemplateField root, RefTypeManager refMan, Dictionary<string, AssetTypeTemplateField?> refTemplates, long position, Func<string?, bool> accept)
    {
        try
        {
            r.Position = position;
            string? name = null;
            Dictionary<string, object?>? mapData = null;
            var rootVals = new Dictionary<string, object?>(StringComparer.Ordinal);
            var refPairs = new List<(long Rid, int ElementId, int Cell)>();
            foreach (var c in root.Children)
            {
                if (c.Name == "m_Name" && c.ValueType == AssetValueType.String)
                {
                    name = (string?)Materialize(r, c);
                    if (!accept(name)) return new Result(name, false, null);
                    continue;
                }
                if (c.Name == "references" && c.ValueType == AssetValueType.ManagedReferencesRegistry) { ReadRegistry(r, c, refMan, refTemplates, refPairs); continue; }
                if (c.Name == "mapData" && c.ValueType == AssetValueType.None && !c.IsArray) { mapData = ReadStruct(r, c); continue; }
                if (Keep.Contains(c.Name)) rootVals[c.Name] = Materialize(r, c); else Skip(r, c);
            }
            // Certaines cartes portent cellsData/voisins/boundingBoxes DIRECTEMENT à la racine, sans enveloppe mapData (ex. Astrub).
            var md = mapData ?? (rootVals.ContainsKey("cellsData") ? rootVals : null);
            if (md is null) { LastError = "ni mapData ni cellsData racine ; enfants racine lus: " + string.Join(",", rootVals.Keys); return new Result(name, true, null); }
            return new Result(name, true, Build(md, refPairs));
        }
        catch (Exception ex) { LastError = ex.GetType().Name + ": " + ex.Message; return new Result(null, true, null); }
    }

    // ---------------------------------------------------------------- construction du résultat (mêmes règles que MapCellReader.ParseMapFields)

    private static int I(object? v) => v switch { long l => unchecked((int)l), double d => (int)d, _ => throw new InvalidDataException("entier attendu, recu " + (v?.GetType().Name ?? "null")) };
    private static bool B(object? v) => v switch { long l => l != 0, double d => d != 0, _ => throw new InvalidDataException("booléen attendu, recu " + (v?.GetType().Name ?? "null")) };

    private static (MapCell[], MapExits, Dictionary<int, int>)? Build(Dictionary<string, object?> md, List<(long Rid, int ElementId, int Cell)> registry)
    {
        if (Arr(md.GetValueOrDefault("cellsData")) is not { } cellList) { LastError = "cellsData absent/pas liste ; cles: " + string.Join(",", md.Keys); return null; }
        if (cellList.Count != 560) { LastError = "nombre de cellules " + cellList.Count; return null; }
        var cells = new MapCell[cellList.Count];
        for (int i = 0; i < cells.Length; i++)
        {
            if (cellList[i] is not Dictionary<string, object?> c) { LastError = "cellule pas struct"; return null; }
            if (!c.TryGetValue("cellNumber", out var cn) || !c.TryGetValue("mov", out var mov) || !c.TryGetValue("los", out var los)) { LastError = "champs de cellule: " + string.Join(",", c.Keys); return null; }
            // Champs optionnels : lecture défensive comme l'ancienne (try/catch autour de AsInt/AsBool -> valeur par défaut si type inattendu).
            int moveZone = OptInt(c, "moveZone");
            int linkedZone = OptInt(c, "linkedZone");
            bool nwf = OptBool(c, "nonWalkableDuringFight");
            int floor = c.ContainsKey("floor") ? OptInt(c, "floor") : OptInt(c, "altitude");
            int mapChange = OptInt(c, "mapChangeData");
            try { cells[i] = new MapCell(I(cn), B(mov), B(los), floor, mapChange, moveZone, linkedZone, nwf); }
            catch (Exception ex) { LastError = ex.Message + " ; cellule " + i + " : " + string.Join(", ", System.Linq.Enumerable.Select(c, kv => kv.Key + "=" + kv.Value)); return null; }
        }
        for (int i = 0; i < cells.Length; i++) if (cells[i].CellId != i) { LastError = "numérotation des cellules incohérente à " + i; return null; }
        foreach (var k in new[] { "topNeighbourId", "bottomNeighbourId", "leftNeighbourId", "rightNeighbourId" })
            if (!md.ContainsKey(k)) { LastError = "voisin absent: " + k + " ; cles: " + string.Join(",", md.Keys); return null; }
        var exits = new MapExits(I(md["topNeighbourId"]), I(md["bottomNeighbourId"]), I(md["leftNeighbourId"]), I(md["rightNeighbourId"]),
            Arrows(md, "topArrowCellList"), Arrows(md, "bottomArrowCellList"), Arrows(md, "leftArrowCellList"), Arrows(md, "rightArrowCellList"));

        var map = new Dictionary<int, int>();
        if (Arr(md.GetValueOrDefault("boundingBoxes")) is { } boxes)
            foreach (var e in boxes)
                if (e is Dictionary<string, object?> bb && bb.TryGetValue("m_interactionId", out var id) && bb.TryGetValue("cellId", out var cell))
                {
                    int elementId = I(id), cellId = I(cell);
                    if (elementId != 0 && cellId > 0 && cellId < 559) map[elementId] = cellId;
                }
        // ClientMapData : un élément interactif NON animé n'a pas de cellId à lui, il est sur la cellule du groupe middleground qui le liste (par rid).
        var groupCell = new Dictionary<long, int>();
        if (Arr(md.GetValueOrDefault("middlegroundMapElements")) is { } groups)
            foreach (var g in groups)
                if (g is Dictionary<string, object?> gd && gd.TryGetValue("cellId", out var gc) && gc is long gcell && Arr(gd.GetValueOrDefault("mapElements")) is { } els)
                    foreach (var e in els)
                        if (e is Dictionary<string, object?> ed && ed.TryGetValue("rid", out var rid) && rid is long ridL) groupCell[ridL] = unchecked((int)gcell);
        foreach (var (rid, elementId, ownCell) in registry)
        {
            int cellId = ownCell >= 0 ? ownCell : groupCell.TryGetValue(rid, out var gcl) ? gcl : -1;
            if (elementId != 0 && cellId > 0 && cellId < 559) map.TryAdd(elementId, cellId);
        }
        return (cells, exits, map);
    }

    private static int OptInt(Dictionary<string, object?> c, string key) => c.TryGetValue(key, out var v) && v is long l ? unchecked((int)l) : (c.TryGetValue(key, out var d) && d is double dd ? (int)dd : 0);
    private static bool OptBool(Dictionary<string, object?> c, string key) => c.TryGetValue(key, out var v) && ((v is long l && l != 0) || (v is double d && d != 0));

    /// <summary>Un std::vector sérialisé est une structure « vector » à un seul enfant « Array » : renvoie la liste, ou null.</summary>
    private static List<object?>? Arr(object? v) => v switch
    {
        List<object?> l => l,
        Dictionary<string, object?> d when d.TryGetValue("Array", out var inner) && inner is List<object?> l2 => l2,
        _ => null,
    };

    private static IReadOnlyList<int> Arrows(Dictionary<string, object?> md, string field)
    {
        if (Arr(md.GetValueOrDefault(field)) is not { } items) return Array.Empty<int>();
        var list = new List<int>(items.Count);
        foreach (var e in items)
            list.Add(e is Dictionary<string, object?> d ? I(d["cellId"]) : I(e));
        return list;
    }

    // ---------------------------------------------------------------- lecture sélective

    private static int PrimSize(AssetValueType t) => t switch
    {
        AssetValueType.Bool or AssetValueType.Int8 or AssetValueType.UInt8 => 1,
        AssetValueType.Int16 or AssetValueType.UInt16 => 2,
        AssetValueType.Int32 or AssetValueType.UInt32 or AssetValueType.Float => 4,
        AssetValueType.Int64 or AssetValueType.UInt64 or AssetValueType.Double => 8,
        _ => throw new InvalidDataException("type primitif inattendu " + t),
    };

    private static Dictionary<string, object?> ReadStruct(AssetsFileReader r, AssetTypeTemplateField t)
    {
        var d = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var c in t.Children)
        {
            if (Keep.Contains(c.Name)) d[c.Name] = Materialize(r, c); else Skip(r, c);
        }
        if (t.IsAligned) r.Align();
        return d;
    }

    /// <summary>Lit un champ en entier (valeurs primitives en long/double, chaînes, tableaux en List, structures en Dictionary).</summary>
    private static object? Materialize(AssetsFileReader r, AssetTypeTemplateField t)
    {
        if (t.IsArray)
        {
            if (t.Children.Count != 2) throw new InvalidDataException("tableau mal formé");
            int n = r.ReadInt32();
            if (t.ValueType == AssetValueType.ByteArray) { var bytes = r.ReadBytes(n); if (t.IsAligned) r.Align(); return bytes; }
            var el = t.Children[1];
            var list = new List<object?>(n);
            for (int i = 0; i < n; i++) list.Add(Materialize(r, el));
            if (t.IsAligned) r.Align();
            return list;
        }
        switch (t.ValueType)
        {
            case AssetValueType.None:
            {
                var d = new Dictionary<string, object?>(t.Children.Count, StringComparer.Ordinal);
                foreach (var c in t.Children) d[c.Name] = Materialize(r, c);
                if (t.IsAligned) r.Align();
                return d;
            }
            case AssetValueType.String:
            {
                int n = r.ReadInt32();
                string s = Encoding.UTF8.GetString(r.ReadBytes(n));
                r.Align();
                return s;
            }
            case AssetValueType.ManagedReferencesRegistry:
                throw new NotSupportedException("registre imbriqué");
            default:
            {
                if (t.Children.Count != 0) throw new InvalidDataException("primitif avec enfants");
                object v = t.ValueType switch
                {
                    AssetValueType.Int8 => (long)r.ReadSByte(),
                    AssetValueType.UInt8 => (long)r.ReadByte(),
                    AssetValueType.Bool => r.ReadBoolean() ? 1L : 0L,
                    AssetValueType.Int16 => (long)r.ReadInt16(),
                    AssetValueType.UInt16 => (long)r.ReadUInt16(),
                    AssetValueType.Int32 => (long)r.ReadInt32(),
                    AssetValueType.UInt32 => (long)r.ReadUInt32(),
                    AssetValueType.Int64 => r.ReadInt64(),
                    AssetValueType.UInt64 => unchecked((long)r.ReadUInt64()),
                    AssetValueType.Float => (double)r.ReadSingle(),
                    AssetValueType.Double => r.ReadDouble(),
                    _ => throw new InvalidDataException("type inattendu " + t.ValueType),
                };
                if (t.IsAligned) r.Align();
                return v;
            }
        }
    }

    /// <summary>Saute un champ sans rien allouer (même consommation d'octets que <see cref="Materialize"/>).</summary>
    private static void Skip(AssetsFileReader r, AssetTypeTemplateField t)
    {
        if (t.IsArray)
        {
            if (t.Children.Count != 2) throw new InvalidDataException("tableau mal formé");
            int n = r.ReadInt32();
            if (t.ValueType == AssetValueType.ByteArray) { r.Position += n; if (t.IsAligned) r.Align(); return; }
            var el = t.Children[1];
            // Tableau de primitifs non alignés : un seul saut.
            if (el.Children.Count == 0 && !el.IsArray && el.ValueType != AssetValueType.None && el.ValueType != AssetValueType.String
                && el.ValueType != AssetValueType.ManagedReferencesRegistry && !el.IsAligned)
                r.Position += (long)n * PrimSize(el.ValueType);
            else
                for (int i = 0; i < n; i++) Skip(r, el);
            if (t.IsAligned) r.Align();
            return;
        }
        switch (t.ValueType)
        {
            case AssetValueType.None:
                foreach (var c in t.Children) Skip(r, c);
                if (t.IsAligned) r.Align();
                return;
            case AssetValueType.String:
            {
                int n = r.ReadInt32();
                r.Position += n;
                r.Align();
                return;
            }
            case AssetValueType.ManagedReferencesRegistry:
                throw new NotSupportedException("registre imbriqué");
            default:
                if (t.Children.Count != 0) throw new InvalidDataException("primitif avec enfants");
                r.Position += PrimSize(t.ValueType);
                if (t.IsAligned) r.Align();
                return;
        }
    }

    /// <summary>Registre des références managées : seules celles dont la classe contient « Interactive » sont lues (m_interactionId, cellId) ; les autres sautées.</summary>
    private static void ReadRegistry(AssetsFileReader r, AssetTypeTemplateField t, RefTypeManager refMan, Dictionary<string, AssetTypeTemplateField?> refTemplates, List<(long Rid, int ElementId, int Cell)> pairs)
    {
        if (t.Children.Count != 2) throw new InvalidDataException("registre mal formé");
        int version = r.ReadInt32();
        int count = -1;
        if (version != 1) count = r.ReadInt32();
        for (int idx = 0; version == 1 || idx < count; idx++)
        {
            long rid = version != 1 ? r.ReadInt64() : -1;   // rid (v2+)
            var type = new AssetTypeReference();
            type.ReadAsset(r);
            if (version == 1 && type.Equals(AssetTypeReference.TERMINUS)) break;
            // Le modèle d'un type de référence ne change pas d'une carte à l'autre : construit une seule fois par bundle.
            string key = type.ClassName + "|" + type.Namespace + "|" + type.AsmName;
            if (!refTemplates.TryGetValue(key, out var tf)) refTemplates[key] = tf = refMan.GetTemplateField(type);
            if (tf is null) continue;
            if ((type.ClassName ?? "").Contains("Interactive", StringComparison.Ordinal))
            {
                // MapMetadata : m_interactionId ; ClientMapData : <interactiveId>k__BackingField, cellId seulement pour les éléments animés.
                if (Materialize(r, tf) is Dictionary<string, object?> data
                    && (data.TryGetValue("m_interactionId", out var id) || data.TryGetValue("<interactiveId>k__BackingField", out id)))
                    pairs.Add((rid, I(id), data.TryGetValue("cellId", out var cell) ? I(cell) : -1));
            }
            else Skip(r, tf);
        }
    }
}
