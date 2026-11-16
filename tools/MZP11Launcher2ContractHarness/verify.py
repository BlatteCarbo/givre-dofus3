"""Compare only the generated ZaapService Thrift *schema*, not account data.

Reads one bounded file from the user's locally installed Launcher 2 app.asar,
does not execute any launcher JS, never reads token stores / game processes,
and emits only public method names and wire field types/IDs.
"""
import json
import os
from pathlib import Path
import re
import struct

launcher = (Path(os.environ["LOCALAPPDATA"]) / "Programs" / "Ankama Launcher 2" /
            "resources" / "app.asar")
target = ("node_modules", "protocol-generated-nodejs", "ZaapService.js")
expected = {
    "ZaapService_connect_args": [("gameName", "STRING", 1),
                                   ("releaseName", "STRING", 2),
                                   ("instanceId", "I32", 3), ("hash", "STRING", 4)],
    "ZaapService_auth_getGameToken_args": [("gameSession", "STRING", 1),
                                              ("gameId", "I32", 2)],
    "ZaapService_auth_getGameTokenWithWindowId_args": [("gameSession", "STRING", 1),
                                                          ("gameId", "I32", 2),
                                                          ("windowId", "I32", 3)],
    "ZaapService_connect_result": [("success", "STRING", 0), ("error", "STRUCT", 1)],
    "ZaapService_auth_getGameToken_result": [("success", "STRING", 0),
                                                 ("error", "STRUCT", 1)],
}

if not launcher.is_file() or launcher.is_symlink() or launcher.stat().st_size > 1_000_000_000:
    raise SystemExit("Launcher 2 static archive unavailable or unsafe")
with launcher.open("rb") as handle:
    head = handle.read(16)
    if len(head) != 16:
        raise SystemExit("ASAR header truncated")
    _outer, header_size, _header_info, json_size = struct.unpack("<IIII", head)
    if not 32 < json_size < 8_000_000 or not 32 < header_size < 8_000_000:
        raise SystemExit("ASAR metadata length invalid")
    index = json.loads(handle.read(json_size))
    folder = index["files"]
    for item in target:
        node = folder[item]
        if item != target[-1]:
            folder = node["files"]
    size = node.get("size", 0)
    offset = int(node.get("offset", -1))
    if not 0 < size < 250_000 or offset < 0 or node.get("unpacked"):
        raise SystemExit("Unexpected generated service member")
    handle.seek(8 + header_size + offset)
    code = handle.read(size).decode("utf-8", "strict")

checks = 0
for name, expected_fields in expected.items():
    marker = "const " + name + " = class"
    start = code.find(marker)
    if start < 0:
        raise SystemExit("Missing generated method class: " + name)
    end = code.find("\nconst ", start + len(marker))
    if end < 0 or end - start > 12000:
        raise SystemExit("Method class parse exceeded bounds")
    scope = code[start:end]
    fields = [(fname, dtype, int(number)) for fname, dtype, number in
              re.findall(r"writeFieldBegin\(['\"]([^'\"]+)['\"],\s*Thrift.Type.(\w+),\s*(\d+)\)", scope)]
    if fields != expected_fields:
        raise SystemExit("Wire drift: " + name + " found=" + repr(fields))
    checks += 1
    print("LAUNCHER2_WIRE_CONTRACT=" + name + " " + ",".join(
        f"{field}:{typ}:{tag}" for field, typ, tag in fields))
print(f"MZP11 installed Launcher 2 Thrift wire contracts: PASS ({checks} schema checks).")
print("No authentication, ticket, runtime handshake, licence or gameplay asserted.")
