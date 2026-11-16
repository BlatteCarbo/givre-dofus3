using AssetsTools.NET;
using AssetsTools.NET.Extra;
var dir = @"%USERPROFILE%\AppData\Local\Ankama\Dofus-dofus3\Dofus_Data\StreamingAssets\Content\Map\Data";
var found = new[]{Path.Combine(dir,"mapdata_assets_world_585.bundle")};
foreach(var p in found){Console.WriteLine("BUNDLE "+p); var mgr=new AssetsManager();var b=mgr.LoadBundleFile(p,true);var af=mgr.LoadAssetsFileFromBundle(b,0,false);foreach(var inf in af.file.AssetInfos.Where(i=>i.TypeId==114)){var bf=mgr.GetBaseField(af,inf); if(bf["m_Name"].AsString!="map_153356296")continue;
void Dump(AssetTypeValueField f,int depth){Console.WriteLine(new string(' ',depth)+f.FieldName+"="+(f.Value?.ToString()??"")+" type="+f.TypeName);if(depth<12 && f.FieldName!="references" && f.FieldName!="cellsData")foreach(var c in f.Children)Dump(c,depth+1);}
var reg=bf["references"].Value?.AsManagedReferencesRegistry;
if(reg!=null){var elements=new List<(long Ref,int Id)>();foreach(var r in reg.references){if(r.type?.ClassName!="ClientInteractiveMapElement")continue;var id=r.data["<interactiveId>k__BackingField"];if(!id.IsDummy && id.AsUInt>0)elements.Add((r.rid,(int)id.AsUInt));} foreach(var g in bf["middlegroundMapElements"]["Array"].Children)foreach(var m in g["mapElements"]["Array"].Children)foreach(var e in elements)if(e.Ref==m["rid"].AsLong)Console.WriteLine("POSITION "+e.Id+" "+g["cellId"].AsInt);}
}mgr.UnloadAll();}
