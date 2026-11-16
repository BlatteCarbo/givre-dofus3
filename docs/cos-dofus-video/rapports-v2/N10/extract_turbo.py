"""Historical combat *journal* timeline, NOT a live Turbo/native measurement."""
from pathlib import Path
import hashlib
import json
import re

base = Path.home() / "AppData/Local/GivreReplica/TestRuns/givre-app-offline/userdata/combat"
out = Path(__file__).resolve().parent / "turbo_chronologie_anonymisee.json"
timeline_rx = re.compile(r"^\s*\+\s*([\d.]+)\s+s\s+(.*)$")
metrics = [
    ("request_tx", re.compile("mesure turbo : request_tx", re.I)),
    ("sequence_end_self", re.compile("mesure turbo : sequence_end_self", re.I)),
    ("ack_tx", re.compile("mesure turbo : ack_tx", re.I)),
    ("barrier_closed", re.compile("mesure turbo : barrier_closed", re.I)),
    ("barrier_not_closed", re.compile("mesure turbo : BARRIERE NON FERMEE", re.I)),
    ("second_chance", re.compile("mesure turbo : barrière lente", re.I)),
    ("enemy_turn", re.compile("d[eé]but de tour : (?:ENNEMI|MONSTRE)", re.I)),
    ("player_turn", re.compile("d[eé]but de tour : MOI", re.I)),
    ("companion_turn", re.compile("d[eé]but de tour : COMPAGNON", re.I)),
    ("fight_attack_order", re.compile("attaque du groupe", re.I)),
    ("fight_ready", re.compile("PRÊT \(FightReadyRequest\)", re.I)),
]
lat_rx = re.compile(r"(request_to_sequence_end_ms|sequence_end_to_ack_ms|request_to_ack_ms|ack_to_request_ms)=(-?\d+)")

picked = None
for path in sorted(base.rglob("*.log"), reverse=True):
    try: lines = path.read_text(encoding="utf-8-sig", errors="replace").splitlines()
    except OSError: continue
    parsed = []
    for line_no, line in enumerate(lines, 1):
        m = timeline_rx.match(line)
        if not m: continue
        t, content = float(m.group(1)), m.group(2)
        for kind, rx in metrics:
            if rx.search(content):
                value = {k: int(v) for k, v in lat_rx.findall(content)}
                parsed.append({"line": line_no, "t_sec": t, "kind": kind, "time_metrics_ms": value})
                break
    kinds = {e["kind"] for e in parsed}
    if {"request_tx", "sequence_end_self", "ack_tx", "barrier_closed"} <= kinds and len(parsed) >= 7:
        picked = (path, parsed)
        break
if picked is None:
    raise SystemExit("BLOCKED: no one journal with requested metric sequence")
source, events = picked
payload = {
    "schema": "givre-turbo-offline-timeline-1",
    "source_kind": "historical_combat_journal_summary",
    "source_sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
    "source_date_only": source.parent.name,
    "events": events,
    "first_seen_event_t_sec": events[0]["t_sec"],
    "last_seen_event_t_sec": events[-1]["t_sec"],
    "unproven": ["actual_speedhack_lease_owner", "native_module_loaded", "effective_timeScale_multiplier",
                 "first_enemy_turn_if_no_explicit_event", "return_to_roleplay_server_receipt",
                 "wall_clock_vs_cpu_time_partition", "MITM_socket_comparable_baseline"],
    "privacy": "No PID/actor/account/name/raw text/full source path carried over."
}
out.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
print("TURBO_TIMELINE_OK events=%d categories=%s" %
      (len(events), ",".join(sorted({e["kind"] for e in events}))))
