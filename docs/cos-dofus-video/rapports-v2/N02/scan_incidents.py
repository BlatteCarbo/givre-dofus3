"""Historical read-only incident scanner. Export NO raw text, names, PIDs,
original actor IDs, map IDs, accounts, IPs or secrets. Journal summaries are
not authoritative packet receipts; preserve unknown results explicitly.
"""
from pathlib import Path
import hashlib
import json
import re

base = Path.home() / "AppData/Local/GivreReplica/TestRuns/givre-app-offline/userdata/combat"
out = Path(__file__).resolve().parent / "incidents_anonymises.json"
t_rx = re.compile(r"^\s*\+\s*(\d+(?:\.\d+)?)\s+s\s+")
start_rx = re.compile(r"d[eé]but de tour\s*:\s*COMPAGNON", re.I)
end_rx = re.compile(r"d[eé]but de tour\s*:", re.I)
cast_rx = re.compile(r"\bSORT\b|lance le sort|sort [0-9]+.*cible", re.I)
walk_rx = re.compile(r"\bDEPL\.|marche|d[eé]plac[eé]", re.I)
buff_rx = re.compile(r"\bbuff\b|bonus (?:PA|PM)|am[eé]lioration|boost", re.I)
resource_rx = re.compile(r"\bPA[ =:]+11\b|\b11\s*PA\b|\bPM[ =:]+6\b|\b6\s*PM\b", re.I)
refusal_rx = re.compile(r"refus[eé]|annul[eé]|non confirm[eé]|ECHEC", re.I)

def timeline(lines):
    entries = []
    for i, raw in enumerate(lines, 1):
        m = t_rx.search(raw)
        if m:
            part = raw[m.end():]
            kinds = []
            for key, rx in (("companion_turn", start_rx), ("turn_any", end_rx),
                            ("cast_report", cast_rx), ("movement_report", walk_rx),
                            ("buff_mention", buff_rx), ("resource_mention", resource_rx),
                            ("refusal_mention", refusal_rx)):
                if rx.search(part): kinds.append(key)
            entries.append({"line":i,"t_sec":float(m.group(1)),"kinds":kinds})
    return entries

files = sorted(base.rglob("*.log"), reverse=True)
summary={"files_seen":0,"files_containing_prudent":0,"files_containing_companion":0,
         "files_containing_buff":0,"files_containing_walk":0}
empty_candidates, move_candidates, buff_candidates = [], [], []
for p in files:
    try: data=p.read_text(encoding="utf-8-sig",errors="replace")
    except OSError: continue
    summary["files_seen"] += 1
    low=data.lower()
    prudent="prudent" in low
    companion="compagnon" in low
    if prudent: summary["files_containing_prudent"] += 1
    if companion: summary["files_containing_companion"] += 1
    if "buff" in low: summary["files_containing_buff"] += 1
    if "marche" in low: summary["files_containing_walk"] += 1
    if not (prudent or companion): continue
    lines=data.splitlines()
    entries=timeline(lines)
    if not entries: continue
    src={"source_sha256":hashlib.sha256(data.encode("utf-8",errors="replace")).hexdigest(),
         "source_date_only":p.parent.name}
    turns=[j for j, e in enumerate(entries) if "companion_turn" in e["kinds"]]
    for ix in turns:
        begin=entries[ix]
        end=next((k for k in range(ix+1,len(entries)) if "turn_any" in entries[k]["kinds"]),len(entries))
        seg=entries[ix:end]
        casts=[e for e in seg if "cast_report" in e["kinds"]]
        moves=[e for e in seg if "movement_report" in e["kinds"]]
        res=[e for e in seg if "resource_mention" in e["kinds"]]
        candidate={**src, "prudent_mentioned_in_fight":prudent,
                   "companion_turn_started_line":begin["line"],
                   "start_t_sec":begin["t_sec"],
                   "observed_end_t_sec":seg[-1]["t_sec"] if seg else None,
                   "cast_report_count_in_segment":len(casts),
                   "move_report_count_in_segment":len(moves),
                   "resource_mentions":[{"line":e["line"],"t_sec":e["t_sec"]} for e in res[:5]],
                   "event_outline":[{"line":e["line"],"t_sec":e["t_sec"],"kinds":e["kinds"]}
                                    for e in seg[:26]],
                   "server_command_receipt":"unknown: summary not packet sequence",
                   "next_fighter_state":"unknown unless explicit subsequent event"}
        if not casts and len(empty_candidates)<6: empty_candidates.append(candidate)
        if len(moves)>=3 and len(move_candidates)<6: move_candidates.append(candidate)
        if len(buff_candidates)<6 and any("buff_mention" in e["kinds"] for e in seg):
            buff_candidates.append(candidate)

# Rank by explicit Prudent evidence, keep at most one per type; separate
# observations from hypotheses about intent, useful damage or true PM expenditure.
def best(xs):
    return sorted(xs,key=lambda x:(not x["prudent_mentioned_in_fight"],
                                    -x["move_report_count_in_segment"]))[0] if xs else None
payload={"schema":"givre-n02-incidents-1","scan_counts":summary,
         "cases":{
            "prudent_companion_no_cast":best(empty_candidates),
            "consecutive_companion_movements":best(move_candidates),
            "buff_mentioned_during_companion_turn":best(buff_candidates)},
         "unproven":[
             "11PA_6PM_exact_at_companion_turn_if_no_explicit_resource_line",
             "planner_decision_if_not_logged",
             "command_receipt_vs_server_effect",
             "target_benefit_for_buff_without_simulation",
             "single_MP_per_movement_without_resource_deltas",
             "companion_owner_and_account_identity"]}
out.write_text(json.dumps(payload,indent=2,ensure_ascii=False)+"\n",encoding="utf-8")
print("INCIDENT_SCAN_OK files=%d prudent=%d companion=%d empty=%s moves=%s buff=%s" %
      (summary["files_seen"],summary["files_containing_prudent"],summary["files_containing_companion"],
       bool(payload["cases"]["prudent_companion_no_cast"]),
       bool(payload["cases"]["consecutive_companion_movements"]),
       bool(payload["cases"]["buff_mentioned_during_companion_turn"])))
