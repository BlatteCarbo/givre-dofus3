using System.Security.Cryptography;
using System.Text.Json;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

// Read-only, bounded to the documented temple-door map; no renderer or client access.
const int mapId = 241445377;
const int cellId = 475;
const string bundlePath = "%USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3/Dofus_Data/StreamingAssets/Content/Map/Data/mapdata_assets_world_921.bundle";
using var input = File.OpenRead(bundlePath);
string bundleHash = Convert.ToHexStringLower(SHA256.HashData(input));
var manager = new AssetsManager();
var maps = new List<object>();
try
{
    var bundle = manager.LoadBundleFile(bundlePath, true);
    for (int fileIndex = 0; fileIndex < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; fileIndex++)
    {
        var asset = manager.LoadAssetsFileFromBundle(bundle, fileIndex, false);
        foreach (var info in asset.file.AssetInfos.Where(i => i.TypeId == 114))
        {
            var root = manager.GetBaseField(asset, info);
            if (root["m_Name"].AsString != $"map_{mapId}" || root["id"].AsInt != mapId) continue;
            var registry = root["references"].Value?.AsManagedReferencesRegistry
                ?? throw new InvalidDataException("Target map has no managed-reference registry");
            var definitions = registry.references.GroupBy(r => r.rid).ToDictionary(g => g.Key, g => g.ToArray());
            var groups = new List<object>();
            var memberships = new Dictionary<long, int>();
            int unresolved = 0;
            var collectionNames = root.Children.Where(c => c.FieldName != "references" && c.FieldName != "cellsData")
                .Select(c => c.FieldName).ToArray();
            void Visit(AssetTypeValueField field, string path)
            {
                if (field.FieldName is "references" or "cellsData") return;
                if (!field["cellId"].IsDummy)
                {
                    var references = new List<long>();
                    void FindReferences(AssetTypeValueField node)
                    {
                        if (node.FieldName == "rid") references.Add(node.AsLong);
                        else foreach (var child in node.Children) FindReferences(child);
                    }
                    FindReferences(field);
                    foreach (long rid in references) memberships[rid] = memberships.GetValueOrDefault(rid) + 1;
                    if (field["cellId"].AsInt == cellId)
                    {
                        var members = references.Select(rid =>
                        {
                            if (!definitions.TryGetValue(rid, out var matches))
                            {
                                unresolved++;
                                return (object)new { rid, unresolved = true };
                            }
                            return new
                            {
                                rid,
                                definitions = matches.Select(r => new
                                {
                                    concreteClass = r.type?.ClassName,
                                    fields = Describe(r.data),
                                }).ToArray(),
                            };
                        }).ToArray();
                        groups.Add(new { path, type = field.TypeName, fields = Describe(field), members });
                    }
                }
                int index = 0;
                foreach (var child in field.Children) Visit(child, $"{path}/{child.FieldName}[{index++}]");
            }
            Visit(root, "map");
            maps.Add(new
            {
                fileIndex, pathId = info.PathId, collectionNames,
                cell475_group_count = groups.Count,
                unresolved_rid_count = unresolved,
                duplicate_definition_count = definitions.Count(p => p.Value.Length > 1),
                multiple_membership_count = memberships.Count(p => p.Value > 1),
                groups,
            });
        }
    }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        bundlePath, bundleHash, mapId, cellId,
        target_map_count = maps.Count,
        status = maps.Count == 0 ? "target_map_absent" : "source_classification_only",
        maps,
    }, new JsonSerializerOptions { WriteIndented = true }));
}
finally { manager.UnloadAll(); }

static object Describe(AssetTypeValueField field) => new
{
    name = field.FieldName,
    type = field.TypeName,
    value = field.Value?.ToString(),
    children = field.Children.Select(Describe).ToArray(),
};
