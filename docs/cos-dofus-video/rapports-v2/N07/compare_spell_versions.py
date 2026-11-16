#!/usr/bin/env python3
"""Compare verified public DOFUS 3.7.12.12 JSON with locally exported 3.7.13.17.

Offline read only; outputs are restricted to N07. Missing old data are BLOCKED,
not presumed unchanged. The parser streams RefIds to avoid loading a 200MB JSON.
"""
import csv
import hashlib
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parent
N01=ROOT.parent/"N01"
OLD="3.7.12.12"
NEW="3.7.13.17"
OLD_RELEASE="https://github.com/ledouxm/dofus-sqlite/releases/tag/v6.0_3.7.12.12"
OLD_DATE="2026-10-20T11:15:37Z"
DATA={
    "variant":("spellvariantsdata.3.7.12.12.json",214736,
       "c0e3b8b48bcf8dbe06349019c5169f0b41e7ffa4c5d5eaa24f92a304dcc17029"),
    "spell":("spellsdata.3.7.12.12.json",39766555,
       "a44a36651903652bd26c5acb83f7aed78e1d18e29c5b742151474458d2b37a0f"),
    "level":("spelllevelsdata.3.7.12.12.json",200736289,
       "55301a2dc6580a8389ef09f7f861a8ed5618e818aa3715a1dac746b340775dbe")
}
BREEDS={"sram":4,"roublard":13,"xelor":5,"eliotrope":16}

def digest(path):
    h=hashlib.sha256()
    with path.open("rb") as f:
        while chunk:=f.read(1024*1024):h.update(chunk)
    return h.hexdigest()

def unwrap(value):
    if isinstance(value,list):return [unwrap(x) for x in value]
    if isinstance(value,dict):
        if len(value)==1 and "Array" in value and isinstance(value["Array"],list):
            return [unwrap(x) for x in value["Array"]]
        return {k:unwrap(v) for k,v in value.items()}
    return value

def old_refs(path):
    with path.open("r",encoding="utf-8") as f:
        for line in f:
            if '"RefIds"' in line:break
        else:raise ValueError(f"RefIds absent in {path.name}")
        depth=0
        quoted=False
        escaped=False
        buf=[]
        for line in f:
            start=False
            for c in line:
                if not buf and c=="{" and not quoted:start=True
                if not buf and not start:continue
                if c=='"' and not escaped:quoted=not quoted
                if not quoted:
                    if c=="{":depth+=1
                    if c=="}":depth-=1
                if c=="\\" and quoted:escaped=not escaped
                else:escaped=False
            if start or buf:
                buf.append(line)
                if depth==0:
                    entry=json.loads("".join(buf).strip().rstrip(","))
                    if "rid" not in entry or "data" not in entry:
                        raise ValueError(f"Malformed managed ref in {path.name}")
                    yield unwrap(entry["data"])
                    buf=[]
            if depth<0:raise ValueError(f"Invalid nested braces in {path.name}")
        if buf:raise ValueError(f"Unfinished RefIds record in {path.name}")

def differences(old,new,path=""):
    if old==new:return []
    if isinstance(old,dict) and isinstance(new,dict):
        out=[]
        for key in sorted(set(old)|set(new)):
            field=path+"/"+key
            if key not in old:out.append(("field_added",field,None,new[key]))
            elif key not in new:out.append(("field_removed",field,old[key],None))
            else:out.extend(differences(old[key],new[key],field))
        return out
    if isinstance(old,list) and isinstance(new,list):
        out=[]
        for i in range(max(len(old),len(new))):
            field=path+"/"+str(i)
            if i>=len(old):out.append(("element_added",field,None,new[i]))
            elif i>=len(new):out.append(("element_removed",field,old[i],None))
            else:out.extend(differences(old[i],new[i],field))
        return out
    return [("value_changed",path,old,new)]

def main():
    new=json.loads((N01/"SORTS_3.7.13.17.json").read_text(encoding="utf-8"))
    new_sources=json.loads((N01/"CLIENT_SOURCES.json").read_text(encoding="utf-8"))
    if new_sources.get("clientVersion")!=NEW:
        raise ValueError("New build does not match")
    verified={}
    for key,(name,sz,sha) in DATA.items():
        f=ROOT/name
        if not f.is_file() or f.stat().st_size!=sz:
            verified[key]={"status":"missing_or_incomplete","expectedBytes":sz,
                           "actualBytes":f.stat().st_size if f.exists() else None}
            continue
        h=digest(f)
        verified[key]={"status":"verified" if h==sha else "hash_mismatch",
                       "expectedSha256":sha,"actualSha256":h,"bytes":sz,
                       "url":f"https://github.com/ledouxm/dofus-sqlite/releases/download/v6.0_3.7.12.12/{name.split('.')[0]}.json"}
    new_variants={(v["breedId"],v["id"]):v for c in new.values() for v in c["variants"]}
    old_variants={}
    if verified["variant"]["status"]=="verified":
        old_variants={(r["breedId"],r["id"]):r for r in old_refs(ROOT/DATA["variant"][0])
                       if r.get("breedId") in BREEDS.values()}
    breed_names={breed:name for name,breed in BREEDS.items()}
    spell_classes={}
    for variant in list(old_variants.values())+list(new_variants.values()):
        cls=breed_names.get(variant.get("breedId"),"unknown")
        for sid in variant.get("spellIds",[]):
            if isinstance(sid,int):
                old_cls=spell_classes.setdefault(sid,cls)
                if old_cls!=cls:
                    raise ValueError(f"Spell {sid} belongs to multiple distinct classes: {old_cls}, {cls}")
    old_ids={sid for v in old_variants.values() for sid in v.get("spellIds",[]) if isinstance(sid,int)}
    new_spells={s["spellId"]:s["spellData"] for c in new.values() for s in c["spells"]}
    wanted=old_ids|set(new_spells)
    old_spells={}
    if verified["spell"]["status"]=="verified":
        old_spells={r["id"]:r for r in old_refs(ROOT/DATA["spell"][0]) if r.get("id") in wanted}
    new_levels={(l["spellId"],l["grade"]):l for c in new.values() for s in c["spells"] for l in s["spellLevels"]}
    old_levels={}
    if verified["level"]["status"]=="verified":
        old_levels={(r["spellId"],r["grade"]):r for r in old_refs(ROOT/DATA["level"][0])
                    if r.get("spellId") in wanted}
    old_effects={}
    new_effects={}
    if verified["level"]["status"]=="verified":
        for dataset,dest in ((old_levels,old_effects),(new_levels,new_effects)):
            for (sid,grade),level in dataset.items():
                for branch,field in (("normal","effects"),("critical","criticalEffect")):
                    for ordinal,effect in enumerate(level.get(field,[]) or []):
                        # Prefer the real serialized instance identity. The ordinal is
                        # retained as a compared field to reveal reordering. Without
                        # effectUid use a clearly marked positional fallback.
                        uid=effect.get("effectUid")
                        identity=f"uid:{uid}" if isinstance(uid,int) and uid>0 else f"slot:{ordinal}"
                        key=(sid,grade,branch,identity)
                        if key in dest:
                            raise ValueError(f"Duplicate effect identity {key}")
                        dest[key]={"__exportOrdinal":ordinal,**effect}
    results=[]
    summaries={}
    for entity,src1,src2,verified_key in (
        ("variant",old_variants,new_variants,"variant"),
        ("spell",old_spells,new_spells,"spell"),
        ("level",old_levels,new_levels,"level"),
        ("effect",old_effects,new_effects,"level")
    ):
        if verified[verified_key]["status"]!="verified":
            summaries[entity]={"status":"blocked","reason":verified[verified_key]}
            continue
        n=0
        per_class={}
        for stable in sorted(set(src1)|set(src2),key=str):
            cls=breed_names.get(stable[0],"unknown") if entity=="variant" else spell_classes.get(stable if entity=="spell" else stable[0],"unknown")
            a=src1.get(stable)
            b=src2.get(stable)
            if entity=="level":
                if a is not None:a={k:v for k,v in a.items() if k not in ("effects","criticalEffect")}
                if b is not None:b={k:v for k,v in b.items() if k not in ("effects","criticalEffect")}
            if a is None:changes=[("record_added","/",None,b)]
            elif b is None:changes=[("record_removed","/",a,None)]
            else:changes=differences(a,b)
            for kind,path,from_value,to_value in changes:
                results.append({"entity":entity,"class":cls,"stableKey":json.dumps(stable,ensure_ascii=False),
                                "change":kind,"field":path,
                                "old":json.dumps(from_value,ensure_ascii=False,sort_keys=True,separators=(",",":")) if from_value is not None else "",
                                "new":json.dumps(to_value,ensure_ascii=False,sort_keys=True,separators=(",",":")) if to_value is not None else ""})
                n+=1
                per_class[cls]=per_class.get(cls,0)+1
        summaries[entity]={"status":"compared","oldRecords":len(src1),
                           "newRecords":len(src2),"differences":n,"differencesByClass":per_class}
    details={
      "schemaVersion":1,"oldVersion":OLD,"newVersion":NEW,
      "oldRelease":OLD_RELEASE,"oldReleasePublishedAt":OLD_DATE,
      "oldAssets":verified,"newSources":new_sources["sourceBundleManifest"],
      "comparison":summaries,
      "stableKeys":{"variant":"breedId,variantId","spell":"spellId","level":"spellId,grade",
                    "effect":"spellId,grade,branch,effectUid (fallback slot:index only when uid missing)"},
      "scope":"Sram, Roublard, Xelor, Eliotrope; old set includes identifiers from both old and new variants",
      "caution":"A field delta is not a proved change in combat behavior. effectUid may be renumbered across builds; a replacement is not automatically a changed mechanic. Display names excluded from keys. Missing assets are blocked."
    }
    (ROOT/"SOURCE_COMPARAISON.json").write_text(json.dumps(details,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    (ROOT/"ANCIENNE_BASE_CIBLEE.json").write_text(json.dumps({
        "oldVersion":OLD,"oldAssetVerification":{k:v["status"] for k,v in verified.items()},
        "variants":list(old_variants.values()),"spells":list(old_spells.values()),"levels":list(old_levels.values())
    },ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    with (ROOT/"DIFF_3.7.12.12_VS_3.7.13.17.csv").open("w",encoding="utf-8",newline="") as f:
        writer=csv.DictWriter(f,fieldnames=["entity","class","stableKey","change","field","old","new"])
        writer.writeheader()
        writer.writerows(results)
    print(json.dumps({"counts":summaries,"rowsWritten":len(results),
                      "verification":{k:v["status"] for k,v in verified.items()}},ensure_ascii=False,indent=2))

if __name__=="__main__":
    main()

