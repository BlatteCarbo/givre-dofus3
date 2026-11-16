"""Lecture diagnostique en lecture seule des bundles de sorts Dofus 3.7.13.17.
Tous les fichiers créés restent dans N01. Aucune invocation du client.
"""
import sys, json
from pathlib import Path
sys.path.insert(0, r"%USERPROFILE%\AppData\Local\Temp\senpi-unity-reader")
import UnityPy
UnityPy.config.FALLBACK_UNITY_VERSION = "6000.3.16f1"
ROOT = Path(r"%USERPROFILE%\AppData\Local\Ankama\Dofus-dofus3\Dofus_Data\StreamingAssets\Content\Data")
for name in ("spells", "spelllevels", "spellvariants"):
    matches = list(ROOT.glob(f"data_assets_{name}dataroot.asset.bundle"))
    if not matches:
        print(json.dumps({"dataset": name, "status": "missing"}))
        continue
    path = matches[0]
    try:
        env = UnityPy.load(str(path))
        print(json.dumps({"dataset": name, "bytes": path.stat().st_size,
                          "objects": [(str(o.type.name), o.path_id) for o in env.objects][:12]}))
        for obj in env.objects:
            if obj.type.name == "MonoBehaviour":
                try:
                    tree = obj.read_typetree()
                    summary = {"dataset": name, "keys": list(tree), "types": {k: type(v).__name__ for k, v in tree.items()}}
                    for k in ("objectsById", "references"):
                        v=tree.get(k)
                        if isinstance(v, dict):
                            summary[k] = {"keys": list(v), "array_sizes": {j:len(q) for j,q in v.items() if isinstance(q,list)}, "samples":{j:q[:1] for j,q in v.items() if isinstance(q,list) and j not in ("m_keys","m_values")}}
                            if k=="objectsById": summary[k]["m_values_first"]=v.get("m_values",[])[:1]
                        elif isinstance(v,list):
                            summary[k] = {"length": len(v), "first": v[:1]}
                    print(json.dumps(summary,ensure_ascii=False,default=str)[:11000])
                except Exception as exc:
                    print(json.dumps({"dataset": name, "read_error": repr(exc)}))
                break
    except Exception as exc:
        print(json.dumps({"dataset": name, "load_error": repr(exc)}))

