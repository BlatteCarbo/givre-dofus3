"""Audit documentaire offline de N01, sans chargement du client ni du jeu."""
import collections
import csv
import hashlib
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
N01 = HERE.parent.parent / "rapports-v2" / "N01"
EFFECTS = N01 / "EFFECTS_3.7.13.17.csv"
LEVELS = N01 / "LEVELS_3.7.13.17.csv"
OUTPUT = HERE / "critiques_candidates.json"
SELECTION = ("12902", "12922", "12938", "13433", "13445",
             "13488", "13283", "13298", "14575", "14581", "14587", "14610")
STRUCT = ("actionId", "targetMask", "duration", "delay", "effectElement",
          "effectCondition", "triggers", "random", "group", "dispellable",
          "effectTriggerDuration", "zoneShape", "zoneSize", "zoneMinSize",
          "zoneForcedDirection", "zoneIncludeCarried", "zoneOnlyIfInSightLine",
          "zoneDescrJson", "zoneCellIdsJson")


def read_csv(path):
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle))


levels = {(x["spellId"], x["grade"]): x for x in read_csv(LEVELS)}
groups = collections.defaultdict(lambda: collections.defaultdict(list))
for row in read_csv(EFFECTS):
    groups[(row["class"], row["spellId"], row["grade"])][row["branch"]].append(row)

stats = collections.Counter()
examples = []
for (cl, sid, grade), branches in sorted(groups.items()):
    normal = branches.get("normal", [])
    critical = branches.get("critical", [])
    stats["grades_total"] += 1
    if not critical:
        stats["grades_without_critical_rows"] += 1
        continue
    stats["grades_with_critical_rows"] += 1
    structural_normal = collections.Counter(tuple(e[k] for k in STRUCT) for e in normal)
    structural_critical = collections.Counter(tuple(e[k] for k in STRUCT) for e in critical)
    if structural_normal != structural_critical:
        stats["structural_multiset_differences"] += 1
    if len(normal) != len(critical):
        stats["different_effect_counts"] += 1
    if any(a["diceNum"] != b["diceNum"] or a["diceSide"] != b["diceSide"]
           for a, b in zip(normal, critical)):
        stats["grades_with_dice_changes_by_index"] += 1
    if any(a["value"] != b["value"] for a, b in zip(normal, critical)):
        stats["grades_with_value_changes_by_index"] += 1
    if sid not in SELECTION or grade != "1":
        continue
    changes = []
    for index, (a, b) in enumerate(zip(normal, critical)):
        fields = {
            k: [a[k], b[k]]
            for k in ("actionId", "diceNum", "diceSide", "value", "targetMask", "duration")
            if a[k] != b[k]
        }
        if fields:
            changes.append({
                "effectIndex": index, "actionId": a["actionId"],
                "normalEffectUid": a["effectUid"], "criticalEffectUid": b["effectUid"],
                "diff": fields
            })
    examples.append({
        "class": cl, "spellId": int(sid), "spellName": levels[(sid, grade)]["spellName"],
        "grade": int(grade), "clientGradeCriticalHitProbability":
            int(levels[(sid, grade)]["criticalHitProbability"]),
        "changesByEffectIndex": changes
    })

output = {
    "schema": "dofus-371317-crit-candidates-1", "status": "client_serialized_only",
    "source": str(EFFECTS.relative_to(HERE.parent.parent)).replace("\\", "/"),
    "source_sha256": hashlib.sha256(EFFECTS.read_bytes()).hexdigest(),
    "levels_sha256": hashlib.sha256(LEVELS.read_bytes()).hexdigest(),
    "statistics": dict(stats), "examples": examples,
    "limitations": [
        "Order changes are not necessarily structural changes",
        "Critical probability in client grade is not a measured per-cast effective probability",
        "No authoritative server cast or damage confirmation supplied"
    ]
}
OUTPUT.write_text(json.dumps(output, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print("P04_AUDIT_OK", json.dumps(dict(stats), sort_keys=True),
      "examples=" + str(len(examples)))
