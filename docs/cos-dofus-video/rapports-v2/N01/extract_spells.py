#!/usr/bin/env python3
"""Offline export of Dofus Unity spell tables for a pinned installed build.

Provenance: native Content/Data Unity bundles; read-only to game and source tree.
UnityPy 1.25.4 installed in Temp by an earlier independent investigation.
Outputs are written only beneath the selected N01 directory.
This extracts serialized fields, not engine-confirmed spell behavior.
"""
from __future__ import annotations
import argparse
import csv
import hashlib
import json
import sys
from pathlib import Path

CLIENT = Path(r"%USERPROFILE%\AppData\Local\Ankama\Dofus-dofus3")
DATA_REL = Path("Dofus_Data/StreamingAssets/Content/Data")
VERSION_REL = Path("Dofus_Data/StreamingAssets/version")
I18N_REL = Path("Dofus_Data/StreamingAssets/Content/I18n/fr.bin")
READER = Path(r"%USERPROFILE%\AppData\Local\Temp\senpi-unity-reader")
FALLBACK_UNITY_VERSION = "6000.3.16f1"
TARGET = {"sram": 4, "roublard": 13, "xelor": 5, "eliotrope": 16}
ALIASES = {"sram": ("Sram",), "roublard": ("Roublard",), "xelor": ("Xelor", "Xélor"), "eliotrope": ("Eliotrope", "Éliotrope")}

def sha256(path: Path) -> str:
    digest=hashlib.sha256()
    with path.open("rb") as f:
        while chunk:=f.read(1024*1024):
            digest.update(chunk)
    return digest.hexdigest()

def read_bundle(data: Path, name: str):
    import UnityPy
    UnityPy.config.FALLBACK_UNITY_VERSION = FALLBACK_UNITY_VERSION
    file=data / ("data_assets_" + name + "dataroot.asset.bundle")
    if not file.is_file():
        raise FileNotFoundError(str(file))
    env=UnityPy.load(str(file))
    arr=[]
    for o in env.objects:
        if o.type.name=="MonoBehaviour":
            t=o.read_typetree()
            if t.get("m_Name","").lower()!= (name+"dataroot").lower():
                raise ValueError("Unexpected root "+t.get("m_Name",""))
            keys=t["objectsById"]["m_keys"]
            values=t["objectsById"]["m_values"]
            refs=t["references"]["RefIds"]
            if len(keys)!=len(values) or len(refs)!=len(keys):
                raise ValueError(f"Managed reference lengths inconsistent: {name}")
            by_rid={x["rid"]:x for x in refs}
            if len(by_rid)!=len(refs):
                raise ValueError(f"Duplicate managed reference rid: {name}")
            for key,v in zip(keys,values):
                ref=by_rid.get(v["rid"])
                if ref is None or "data" not in ref:
                    raise ValueError(f"Missing managed reference: {name}, key {key}")
                d=ref["data"]
                if d.get("id") != key:
                    raise ValueError(f"Managed reference id mismatch: {name}: {key} / {d.get('id')}")
                arr.append(d)
            return arr,{"file":file.name,"bytes":file.stat().st_size,"sha256":sha256(file),
                        "records":len(arr), "managedReferenceRoot":t["m_Name"],"class":refs[0]["type"] if refs else None}
    raise ValueError("No MonoBehaviour in "+name)

def i18n_names(path: Path, wanted: set[int]) -> dict[int,str]:
    """Targeted reader matching GameNameResolver.ReadI18nTexts; offsets absolute."""
    import struct
    if not path.is_file():
        raise FileNotFoundError(str(path))
    offsets={}
    with path.open("rb") as f:
        n=f.read(1)
        if not n: raise ValueError("empty i18n")
        f.seek(n[0],1)
        count=struct.unpack("<I",f.read(4))[0]
        if count==0 or count>5_000_000:raise ValueError("invalid i18n count")
        for _ in range(count):
            key,offset=struct.unpack("<iI",f.read(8))
            if key in wanted: offsets[key]=offset
        results={}
        size=path.stat().st_size
        for key,off in offsets.items():
            if off>=size:continue
            f.seek(off)
            length=0
            shift=0
            while True:
                a=f.read(1)
                if not a or shift>35: raise ValueError(f"bad varint {key}")
                b=a[0]
                length|=(b&127)<<shift
                if not b&128:break
                shift+=7
            if not (0<=length<=2_000_000 and f.tell()+length<=size):continue
            results[key]=f.read(length).decode("utf-8")
    return results

def write_csv(path: Path,rows:list[dict],fields:list[str]):
    with path.open("w",encoding="utf-8",newline="") as f:
        w=csv.DictWriter(f,fieldnames=fields,extrasaction="ignore")
        w.writeheader()
        w.writerows(rows)

def normalized_level(level: dict, cls: str, spell_name: str):
    return {
        "class":cls, "spellId":level["spellId"], "spellName":spell_name,
        "levelId":level.get("id"), "grade":level.get("grade"),
        "minPlayerLevel":level.get("minPlayerLevel"),"spellBreed":level.get("spellBreed"),
        "apCost":level.get("apCost"),"minRange":level.get("minRange"),"range":level.get("range"),
        "m_flags":level.get("m_flags"),"criticalHitProbability":level.get("criticalHitProbability"),
        "maxStack":level.get("maxStack"),"maxCastPerTurn":level.get("maxCastPerTurn"),
        "maxCastPerTarget":level.get("maxCastPerTarget"),"maxGlobalCastPerTurn":level.get("maxGlobalCastPerTurn"),
        "maxGlobalCastPerTarget":level.get("maxGlobalCastPerTarget"),"minCastInterval":level.get("minCastInterval"),
        "initialCooldown":level.get("initialCooldown"),"globalCooldown":level.get("globalCooldown"),
        "statesCriterion":level.get("statesCriterion"),
        "ignorePrisonGlyph":level.get("ignorePrisonGlyph"),
        "normalEffects":len(level.get("effects") or []),"criticalEffects":len(level.get("criticalEffect") or [])
    }

def normalized_effect(level:dict,e:dict,cls:str,spell_name:str,kind:str,index:int):
    zone=e.get("zoneDescr") or {}
    row={"class":cls,"spellId":level["spellId"],"spellName":spell_name,
         "levelId":level.get("id"),"grade":level.get("grade"),
         "branch":kind,"effectIndex":index,"effectUid":e.get("effectUid"),
         "baseEffectId":e.get("baseEffectId"),"actionId":e.get("actionId"),
         "effectSpellId":e.get("spellId"),"order":e.get("order"),"targetId":e.get("targetId"),
         "targetMask":e.get("targetMask"),"effectElement":e.get("effectElement"),
         "duration":e.get("duration"),"delay":e.get("delay"),"triggers":e.get("triggers"),
         "effectCondition":e.get("effectCondition"),
         "diceNum":e.get("diceNum"),"diceSide":e.get("diceSide"),"value":e.get("value"),
         "random":e.get("random"),"group":e.get("group"),"dispellable":e.get("dispellable"),
         "effectTriggerDuration":e.get("effectTriggerDuration"),"zoneShape":zone.get("shape"),
         "zoneSize":zone.get("param1"),"zoneMinSize":zone.get("param2"),
         "zoneForcedDirection":zone.get("forcedDirection"),
         "zoneIncludeCarried":zone.get("includeCarried"),
         "zoneOnlyIfInSightLine":zone.get("onlyAffectIfInSightLine"),
         "zoneDescrJson":json.dumps(zone,ensure_ascii=False,sort_keys=True,separators=(",",":")),
         "zoneCellIdsJson":json.dumps(zone.get("cellIds",[]),ensure_ascii=False,separators=(",",":"))}
    # Explicit distinction: effectUid identifies an effect instance; actionId identifies action category.
    return row

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--client-root",type=Path,default=CLIENT)
    parser.add_argument("--output",type=Path,default=Path(__file__).resolve().parent)
    parser.add_argument("--expected-version",default="3.7.13.17")
    ns=parser.parse_args()
    output=ns.output.resolve()
    allowed=Path(__file__).resolve().parent
    if output!=allowed:
        raise ValueError(f"Output restricted to {allowed} for this worker")
    raw_version=(ns.client_root / VERSION_REL).read_text(encoding="utf-8")
    fields=dict(row.split("=",1) for row in raw_version.splitlines() if "=" in row)
    if fields.get("Version")!=ns.expected_version:raise ValueError("Installed build is "+str(fields.get("Version")))
    sys.path.insert(0,str(READER))
    import UnityPy
    from importlib.metadata import version
    data_dir=ns.client_root / DATA_REL
    tables={}
    sources={}
    for name in ("breeds","spells","spelllevels","spellvariants"):
        tables[name],sources[name]=read_bundle(data_dir,name)
    source_ver=ns.client_root / VERSION_REL
    i18n_file=ns.client_root / I18N_REL
    sources["version"]={"file":str(VERSION_REL).replace("\\","/"),"sha256":sha256(source_ver),"bytes":source_ver.stat().st_size}
    sources["i18nFr"]={"file":str(I18N_REL).replace("\\","/"),"sha256":sha256(i18n_file),"bytes":i18n_file.stat().st_size}
    # Pin executable/IL2CPP metadata identity as a separate binary provenance check.
    for label,rel in (("gameAssembly",Path("GameAssembly.dll")),
                      ("globalMetadata",Path("Dofus_Data/il2cpp_data/Metadata/global-metadata.dat"))):
        full=ns.client_root/rel
        if not full.is_file():
            raise FileNotFoundError(f"Required client identity file absent: {full}")
        sources[label]={"file":str(rel).replace("\\","/"),"sha256":sha256(full),
                        "bytes":full.stat().st_size}
    breeds={x["id"]:x for x in tables["breeds"]}
    names={int(x["nameId"]) for x in tables["spells"] if isinstance(x.get("nameId"),int)}
    names.update(int(x["shortNameId"]) for x in breeds.values()
                 if str(x.get("shortNameId","")).isdigit())
    translations=i18n_names(i18n_file,names)
    spell_by_id={x["id"]:x for x in tables["spells"]}
    levels_by_spell={}
    for level in tables["spelllevels"]:
        levels_by_spell.setdefault(level.get("spellId"),[]).append(level)
    variants={}
    for v in tables["spellvariants"]:
        variants.setdefault(v["breedId"],[]).append(v)
    spell_rows=[]
    level_rows=[]
    effect_rows=[]
    variant_rows=[]
    records={}
    audit={}
    for cls,breed_id in TARGET.items():
        breed=breeds.get(breed_id)
        if breed is None:raise ValueError(f"Breed {breed_id} absent for {cls}")
        label=translations.get(int(breed["shortNameId"])) if str(breed.get("shortNameId","")).isdigit() else None
        if not label:
            raise ValueError(f"Missing i18n breed label {cls} {breed}")
        if not any(a.casefold() in label.casefold() for a in ALIASES[cls]):
            raise ValueError(f"Unexpected breed for {cls}: id {breed_id} label {label}")
        raw_variants=variants.get(breed_id,[])
        if not raw_variants:raise ValueError(f"No spell variants for breed {breed_id}")
        spell_ids={sid for v in raw_variants for sid in v.get("spellIds",[]) if isinstance(sid,int) and sid>0}
        for v in raw_variants:
            variant_rows.append({"class":cls,"breedId":breed_id,"variantId":v["id"],"spellIdsJson":json.dumps(v.get("spellIds",[]),separators=(",",":"))})
        items=[]
        missing=[]
        dangling=[]
        for sid in sorted(spell_ids):
            spell=spell_by_id.get(sid)
            if spell is None:
                missing.append(sid)
                continue
            nm=translations.get(spell.get("nameId"))
            lv=sorted(levels_by_spell.get(sid,[]),key=lambda x:(x.get("grade",0),x.get("id",0)))
            referenced_ids=set(spell.get("spellLevels",[]))
            dangling.extend(sorted(referenced_ids-{x.get("id") for x in lv}))
            spell_rows.append({"class":cls,"breedId":breed_id,"spellId":sid,"nameId":spell.get("nameId"),
                               "name":nm or "","descriptionId":spell.get("descriptionId"),
                               "spellTypeId":spell.get("typeId"),"spellOrder":spell.get("order"),
                               "scriptId":spell.get("scriptId"),"iconId":spell.get("iconId"),
                               "spellLevelsJson":json.dumps(spell.get("spellLevels",[]),separators=(",",":"))})
            for level in lv:
                level_rows.append(normalized_level(level,cls,nm or ""))
                for kind,k in (("normal","effects"),("critical","criticalEffect")):
                    for idx,e in enumerate(level.get(k) or []):
                        effect_rows.append(normalized_effect(level,e,cls,nm or "",kind,idx))
            items.append({"spellId":sid,"name":nm,"spellData":spell,"spellLevels":lv,
                         "variantIds":[v["id"] for v in raw_variants if sid in v.get("spellIds",[])]})
        records[cls]={"className":label,"breedId":breed_id,"breedData":breed,
                      "variants":raw_variants,"spells":items}
        audit[cls]={"breedId":breed_id,"classLabelFr":label,"variantEntries":len(raw_variants),
                    "spellIdsReferenced":len(spell_ids),"spellsResolved":len(items),
                    "missingSpells":missing,"danglingLevelIds":sorted(set(dangling)),
                    "levelRecords":sum(len(x["spellLevels"]) for x in items)}
        if missing or dangling: raise ValueError(f"Unresolved entries for {cls}: {audit[cls]}")
    # Raw serialized content is kept for each chosen class; no fake per-effect unique key.
    manifest={"schemaVersion":1,"clientVersion":fields["Version"],"buildDate":fields.get("BuildDate"),
              "source":"installed-official-client-bundles-local","unityPyVersion":version("UnityPy"),
              "readerFallbackUnityVersion":FALLBACK_UNITY_VERSION,
              "sourceBundleManifest":sources,"audit":audit,
              "note":"Fields are native serialized data; names from fr.bin. Not a server behavior proof."}
    (output/"CLIENT_SOURCES.json").write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    (output/"SORTS_3.7.13.17.json").write_text(json.dumps(records,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    write_csv(output/"SPELLS_3.7.13.17.csv",spell_rows,["class","breedId","spellId","nameId","name","descriptionId","spellTypeId","spellOrder","scriptId","iconId","spellLevelsJson"])
    write_csv(output/"LEVELS_3.7.13.17.csv",level_rows,list(level_rows[0]))
    write_csv(output/"EFFECTS_3.7.13.17.csv",effect_rows,list(effect_rows[0]))
    write_csv(output/"VARIANTS_3.7.13.17.csv",variant_rows,list(variant_rows[0]))
    print(json.dumps({"version":fields["Version"],"buildDate":fields.get("BuildDate"),
                      "totalSpells":len(spell_rows),"totalLevels":len(level_rows),"totalEffects":len(effect_rows),
                      "totalVariants":len(variant_rows),"classes":audit,
                      "sourceSha":{k:v["sha256"] for k,v in sources.items()}},ensure_ascii=False,indent=2))

if __name__ == "__main__":
    main()

