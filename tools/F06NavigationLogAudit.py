"""Audit F06 en lecture seule. Ne produit que des compteurs et des durées, aucun journal brut."""
import argparse
import collections
import hashlib
import json
import re
import statistics
from pathlib import Path


def elapsed(line):
    m = re.match(r"(\d{2}):(\d{2}):(\d{2})\.(\d{3})", line)
    return sum(int(v) * scale for v, scale in zip(m.groups(), (3600000, 60000, 1000, 1))) if m else 0


def stats(values):
    values = sorted(values)
    return {"n": len(values), "median_ms": int(statistics.median(values)),
            "p95_ms": values[min(len(values)-1, int(len(values)*.95))], "max_ms": values[-1]} if values else {"n": 0}


def audit(path, limit=None):
    # Le fichier peut encore grandir : lecture bornée, dernière ligne incomplète exclue.
    with path.open("rb") as stream:
        data = stream.read(limit if limit is not None else path.stat().st_size)
    data = data[:data.rfind(b"\n")+1]
    counts = collections.Counter()
    refusals = collections.Counter()
    types = collections.Counter()
    maps, uses, pending_hops, changes, teleports = {}, {}, {}, {}, {}
    map_history = collections.defaultdict(list)
    reverse = collections.Counter()
    hop_times = collections.defaultdict(list)
    change_times, teleport_times, post_map_timeout = [], [], []
    for line in data.decode("utf-8-sig", errors="replace").splitlines():
        match = re.search(r"<(\d+)>", line)
        if not match:
            continue
        pid, at = match[1], elapsed(line)
        if "[move] REFUS" in line:
            counts["refusals"] += 1
            refusals[pid] += 1
        if "position NON CONFIRMEE (predite)" in line:
            counts["direct_exit_unconfirmed"] += 1
        if "[lua" in line and "recolte:" in line:
            if "Utiliser: non confirmée (délai)" in line:
                counts["use_timeouts"] += 1
                use = uses.get(pid)
                if use and use[2] is not None and 0 <= at-use[0] <= 16000:
                    counts["use_timeouts_after_map_change"] += 1
                    post_map_timeout.append(at-use[2])
            if "déplacement non abouti (Timeout)" in line:
                counts["movement_timeouts"] += 1
        if "[lua" in line and "trajet:" in line:
            hop = re.search(r"saut (\d+) → (\d+) par .*?\(type (\d+),", line)
            if hop:
                types[hop[3]] += 1
                pending_hops[pid] = (at, hop[1], hop[2], hop[3])
            for key, marker in (("hop_send_failure", ": envoi échoué."),
                    ("hop_timeout", "non confirmé après 16s"), ("door_missing", "porte introuvable"),
                    ("door_walk_refused", "non aboutie (Refused)")):
                if marker in line:
                    counts[key] += 1
        use = re.search(r"\[recolte\].*InteractiveUseRequest .* element (\d+),", line)
        if use:
            uses[pid] = (at, maps.get(pid), None)
        if "[map]" in line and "MapChangeRequest) inject=OK" in line:
            changes[pid] = (at, maps.get(pid))
        tp = re.search(r"\[zaap\].*téléportation DIRECTE -> map (\d+) inject=OK", line)
        if tp:
            counts["zaap_direct_dispatched"] += 1
            teleports[pid] = (at, tp[1])
        arrival = re.search(r"MapInformationRequest : carte courante -> (\d+)", line)
        if arrival:
            dest, old = arrival[1], maps.get(pid)
            if dest != old:
                history = map_history[pid]
                if len(history) >= 2 and history[-2] == dest:
                    reverse["-".join(sorted((history[-1], dest)))] += 1
                history.append(dest)
                history[:] = history[-2:]
                use = uses.get(pid)
                if use and use[1] is not None and use[1] != dest and use[2] is None:
                    uses[pid] = (use[0], use[1], at)
            maps[pid] = dest
            hop = pending_hops.get(pid)
            if hop and dest != hop[1]:
                hop_times[hop[3]].append(at-hop[0])
                del pending_hops[pid]
            change = changes.get(pid)
            if change and change[1] is not None and dest != change[1]:
                change_times.append(at-change[0])
                del changes[pid]
            teleport = teleports.get(pid)
            if teleport and dest == teleport[1]:
                teleport_times.append(at-teleport[0])
                del teleports[pid]
    return {"file": path.name, "bytes_complete_lines": len(data), "sha256": hashlib.sha256(data).hexdigest(),
            "counts": dict(counts), "refusals_per_pid": dict(refusals), "hops_per_type": dict(types),
            "hop_to_mapinfo_per_type": {t: stats(v) for t, v in hop_times.items()},
            "mapchange_dispatch_to_mapinfo": stats(change_times), "zaap_dispatch_to_mapinfo": stats(teleport_times),
            "mapinfo_to_use_timeout": stats(post_map_timeout), "observed_ABA": sum(reverse.values()),
            "most_common_ABA_pairs": reverse.most_common(3)}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("logs", type=Path, nargs="+")
    parser.add_argument("--limit", action="append", default=[], help="nom.log=nombre_octets")
    args = parser.parse_args()
    limits = {k: int(v) for k, v in (s.rsplit("=", 1) for s in args.limit)}
    for path in args.logs:
        print(json.dumps(audit(path, limits.get(path.name)), ensure_ascii=True))
