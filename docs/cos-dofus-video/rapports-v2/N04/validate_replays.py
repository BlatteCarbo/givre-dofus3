"""Offline structural validator: no accounts, sessions or server access."""
import json
from pathlib import Path

path = Path(__file__).resolve().parent / "replays_anonymises.json"
x = json.loads(path.read_text(encoding="utf-8"))
assert x["schema"] == "givre-v2-replay-minimal-1"
assert len(x["replays"]) >= 2
for replay in x["replays"]:
    evts = replay["events"]
    assert [e["seq"] for e in evts] == list(range(1, len(evts)+1))
    assert all(evts[i]["t_sec"] <= evts[i+1]["t_sec"] for i in range(len(evts)-1))
    assert all(e.get("actor") is None or str(e.get("actor")).startswith("actor_") for e in evts)
    assert all("raw" not in e and "text" not in e and "pid" not in e for e in evts)
    assert replay["source_kind"] in ("historical_journal_summary", "synthetic_negative_counterfactual")
raw = path.read_text(encoding="utf-8")
assert not any(k in raw.lower() for k in ("password", "bearer ", "sessiontoken", "accountid", "auth_token"))
print("VALIDATION_OK replays=%d historical=%d" %
      (len(x["replays"]), sum(r["source_kind"] == "historical_journal_summary" for r in x["replays"])))
