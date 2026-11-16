"""Recover non-secret configuration field names, never account or browser material."""
import json, pathlib, re

ROOT = pathlib.Path(__file__).parent
SOURCE = pathlib.Path(r"%USERPROFILE%\AppData\Roaming\MizanBot")
OUT = ROOT / "schemas"
OUT.mkdir(exist_ok=True)

def walk(v, path="$"):
    result = []
    if isinstance(v, dict):
        for key, val in v.items():
            result.append({"path": path + "." + key, "type": type(val).__name__})
            result.extend(walk(val, path + "." + key))
    elif isinstance(v, list):
        for val in v[:1]:
            result.extend(walk(val, path + "[]"))
    return result

for name in ["bot-settings.json", "combat.json", "game-atlas.json", "hdv.json",
             "notifications.json", "planning.json", "proxy-sync.json",
             "settings.json", "treasure-hints.json"]:
    p = SOURCE / name
    if p.exists():
        try:
            data = json.loads(p.read_text(encoding="utf-8-sig"))
            report = {"source_file": name, "values_removed": True, "fields": walk(data)}
            (OUT / name).write_text(json.dumps(report, indent=2), encoding="utf-8")
            print(name, len(report["fields"]), "fields")
        except Exception as e:
            print(name, repr(e))
