"""Read exactly one static generated ZaapService.js member of Launcher 2 ASAR.

Hardcoded file allowlist; no game processes, accounts, credentials, tokens,
launcher private config or extracted archive. Only protocol method names appear
in sanitized output, never raw source or API response values.
"""
import json
import os
from pathlib import Path
import re
import struct

asar = Path(os.environ["LOCALAPPDATA"]) / "Programs" / "Ankama Launcher 2" / "resources" / "app.asar"
target = ("node_modules", "protocol-generated-nodejs", "ZaapService.js")
if not asar.is_file() or asar.is_symlink() or asar.stat().st_size > 1_000_000_000:
    raise SystemExit("Unsafe or absent Launcher 2 ASAR")
with asar.open("rb") as f:
    header = f.read(16)
    if len(header) != 16:
        raise SystemExit("Invalid ASAR")
    _, header_size, _, text_size = struct.unpack("<IIII", header)
    if not (32 < header_size <= 8_000_000 and 32 < text_size <= 8_000_000):
        raise SystemExit("Header exceeds limits")
    doc = json.loads(f.read(text_size).decode("utf-8"))
    directory = doc["files"]
    for part in target:
        node = directory[part]
        if part != target[-1]:
            directory = node["files"]
    if node.get("unpacked") or "offset" not in node or "size" not in node:
        raise SystemExit("Unexpected ASAR member type")
    size = node["size"]
    offset = int(node["offset"])
    if not isinstance(size, int) or not (0 < size <= 1_048_576) or offset < 0:
        raise SystemExit("Generated service exceeds bounded size")
    origin = 8 + header_size + offset
    if origin + size > asar.stat().st_size:
        raise SystemExit("Member outside ASAR bounds")
    f.seek(origin)
    source = f.read(size)
if len(source) != size or b"ZaapService" not in source:
    raise SystemExit("Unexpected generated thrift content (offset/version mismatch)")
code = source.decode("utf-8", errors="strict")
names = set()
for regex in (r"ZaapServiceClient\.prototype\.([A-Za-z_]\w*)\s*=",
              r"ZaapService\.Client\.prototype\.([A-Za-z_]\w*)\s*=",
              r"(?:send|recv)_([A-Za-z_]\w*)\s*[:=(]",
              r"'([A-Za-z_]\w*)_args'", r"'([A-Za-z_]\w*)_result'"):
    names.update(re.findall(regex, code))
interesting = ("connect", "auth_getGameToken", "auth_getGameTokenWithWindowId",
               "settings_get", "zaapVersion_get", "userInfo_get")
print("LAUNCHER2_GENERATED_ZAAP_THRIFT_STATIC=PASS")
print("SOURCE_MEMBER_BYTES=" + str(size))
for method in interesting:
    print("METHOD_" + method + "=" + str(method in code))
print("METHOD_NAMES_OBSERVED=" + str(len(names)))
# Only structural markers, never bundled source or credentials.
for method in ("auth_getGameToken", "auth_getGameTokenWithWindowId", "connect"):
    positions = [m.start() for m in re.finditer(re.escape(method) + "_args", code)]
    print("IDL_ARGS_" + method + "_REFERENCES=" + str(len(positions)))
print("AUTHENTICATED_RUNTIME=NO; CLIENT_GAME_PROTOCOL_PROOF=NO")
