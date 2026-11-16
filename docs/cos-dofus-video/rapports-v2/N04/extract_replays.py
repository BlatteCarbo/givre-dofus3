"""Read historical local combat journal summaries and emit strictly structured,
anonymized event streams. No raw journal line, persistent identifier, name,
account, process ID, network address, token or full file path is exported.

Only processes existing journal files, never game/application state.
"""
from __future__ import annotations
import hashlib
import json
from pathlib import Path
import re
import sys

base = Path.home() / "AppData" / "Local" / "GivreReplica" / "TestRuns" / "givre-app-offline" / "userdata" / "combat"
out = Path(__file__).resolve().parent / "replays_anonymises.json"
if not base.is_dir():
    raise SystemExit("BLOCKED: existing combat journal directory absent")

time_rx = re.compile(r"^\s*\+\s*([0-9.]+)\s+s\s+")
actor_rx = re.compile(r"(?:combattant|ennemi|COMPAGNON)\s+(-?\d+)", re.IGNORECASE)
seen_rx = re.compile(r"combattant\s+(-?\d+)\s+vu\s*->\s*case\s+(\d+)", re.IGNORECASE)
move_rx = re.compile(r"ennemi\s+(-?\d+)\s+d[eé]plac[eé]\s*->\s*case\s+(\d+)", re.IGNORECASE)
start_companion_rx = re.compile(r"d[eé]but de tour\s*:\s*COMPAGNON\s+(-?\d+)", re.IGNORECASE)
control_rx = re.compile(r"contr[oô]le serveur\s*:\s*combattant\s+(-?\d+)", re.IGNORECASE)
refused_rx = re.compile(r"(?:(?:d[eé]placement|marche|mouvement).{0,100}(?:refus[eé]|annul[eé]|ECHEC|bloqu[eé]))", re.IGNORECASE)
command_rx = re.compile(r"(?:marche|d[eé]placement).{0,70}(?:inject=OK|demande envoy[eé]e)", re.IGNORECASE)
turn_end_rx = re.compile(r"fin de tour", re.IGNORECASE)

def parse(src: Path):
    actors: dict[str, str] = {}
    cells: dict[str, int] = {}
    events = []
    has_companion = has_refusal = False
    def alias(raw: str | None) -> str | None:
        if raw is None: return None
        if raw not in actors: actors[raw] = f"actor_{len(actors)+1}"
        return actors[raw]
    for n, raw in enumerate(src.read_text(encoding="utf-8-sig", errors="replace").splitlines(), 1):
        tm = time_rx.search(raw)
        if not tm: continue
        at = round(float(tm.group(1)), 3)
        part = raw[tm.end():]
        kind, actor, cell, proof = None, None, None, "journal_summary_only"
        if m := start_companion_rx.search(part):
            kind, actor, proof = "companion_turn_started", alias(m.group(1)), "journal_turn_start"
            has_companion = True
        elif m := seen_rx.search(part):
            kind, actor, cell, proof = "fighter_cell_seen", alias(m.group(1)), int(m.group(2)), "journal_observed_fighter"
        elif m := move_rx.search(part):
            kind, actor, cell, proof = "position_change_seen", alias(m.group(1)), int(m.group(2)), "journal_observed_movement"
        elif m := control_rx.search(part):
            kind, actor, proof = "server_control_reported", alias(m.group(1)), "journal_server_control"
        elif refused_rx.search(part):
            kind, proof, has_refusal = "movement_refusal_reported", "journal_problem_text", True
        elif command_rx.search(part):
            kind, proof = "movement_command_sent", "journal_command_only"
        elif turn_end_rx.search(part):
            kind, proof = "turn_end_reported", "journal_turn_end"
        if kind is None: continue
        event = {"seq": len(events)+1, "line": n, "t_sec": at,
                 "kind": kind, "actor": actor, "evidence": proof}
        if cell is not None:
            event["cell"] = cell
            event["previous_known_cell"] = cells.get(actor)
            cells[actor] = cell
        events.append(event)
    raw_bytes = src.read_bytes()
    date = src.parent.name
    return {"source_kind": "historical_journal_summary", "source_sha256": hashlib.sha256(raw_bytes).hexdigest(),
            "date_only": date, "event_count": len(events), "events": events,
            "initial_state": {"actor_cells": "unknown_until_first_seen"},
            "final_known_cells": cells,
            "not_present_or_unproven": ["raw_packet_bytes", "authoritative_server_receipt_per_event",
                "actor_account_id", "full_stable_entity_id", "exact_companion_owner", "complete_initial_fight_state"]}, has_companion, has_refusal

# Choose deterministically with a bounded number of files, prefer explicit companion turn.
files = sorted(base.rglob("*.log"), reverse=True)
selected = []
for f in files:
    try:
        replay, companion, refused = parse(f)
    except (UnicodeError, OSError, ValueError):
        continue
    if companion and len(replay["events"]) >= 4:
        selected.append(replay)
        break
if not selected:
    raise SystemExit("BLOCKED: no journal with explicit companion turn")
for f in files:
    try:
        replay, companion, refused = parse(f)
    except (UnicodeError, OSError, ValueError):
        continue
    if refused and replay["source_sha256"] != selected[0]["source_sha256"]:
        selected.append(replay)
        break

if len(selected) == 1:
    # Not a real server event: an explicit synthetic adversarial contract.
    selected.append({"source_kind": "synthetic_negative_counterfactual",
                     "based_on_sha256": selected[0]["source_sha256"],
                     "initial_state": {"actor_cells": {"actor_1": 100}},
                     "events": [{"seq": 1, "t_sec": 0, "kind": "movement_command_sent", "actor": "actor_1",
                                 "requested_cell": 101, "evidence": "synthetic"},
                                {"seq": 2, "t_sec": 0.03, "kind": "movement_refusal_reported", "actor": "actor_1",
                                 "evidence": "synthetic"}],
                     "final_known_cells": {"actor_1": 100},
                     "not_present_or_unproven": ["real_server_refusal_in_this_fixture"]})

payload = {"schema": "givre-v2-replay-minimal-1",
           "caution": "Anonymized journal summaries, not full packet replays; synthetic negative explicitly marked.",
           "replays": selected}
out.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print("EXTRACT_OK replays=%d kinds=%s first_events=%d" %
      (len(selected), ",".join(x["source_kind"] for x in selected), len(selected[0]["events"])))
