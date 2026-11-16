"""Bounded offline read-only ASAR header audit of the NEW Launcher 2 only.

No app.asar extraction, no account/keydata/profile/log access, no running processes,
and no reading/executing packaged JavaScript. Metadata supports scoped follow-up.
"""
import json
import os
from pathlib import Path
import struct

local = Path(os.environ["LOCALAPPDATA"]).resolve()
asar = local / "Programs" / "Ankama Launcher 2" / "resources" / "app.asar"
if not asar.is_file() or asar.is_symlink():
    raise SystemExit("Launcher 2 read-only ASAR file not present/safe")
if asar.stat().st_size > 1_000_000_000:
    raise SystemExit("ASAR exceeds bounded size")

with asar.open("rb") as f:
    head = f.read(16)
    if len(head) != 16:
        raise SystemExit("Malformed ASAR header")
    # Standard Chromium pickle: size-prefix block, then encoded JSON string.
    # Reject ambiguous lengths and huge metadata instead of scanning the body.
    outer, payload_size, header_size, text_size = struct.unpack("<IIII", head)
    if not (4 <= outer <= 16 and 4 <= payload_size <= 8_000_000 and
            4 <= header_size <= 8_000_000 and
            2 <= text_size <= 8_000_000):
        raise SystemExit("Unexpected ASAR header bounds")
    f.seek(16)
    raw = f.read(text_size)
    if len(raw) != text_size:
        raise SystemExit("Incomplete header")
    doc = json.loads(raw.decode("utf-8"))

if not isinstance(doc, dict) or not isinstance(doc.get("files"), dict):
    raise SystemExit("Unexpected ASAR tree")
count = 0
classified = {"zaap": 0, "thrift": 0, "launcher": 0, "cytrus": 0, "release": 0}
examples = {key: [] for key in classified}
todo = [("", doc["files"])]
while todo:
    prefix, children = todo.pop()
    if count > 120_000 or len(prefix) > 1024:
        raise SystemExit("ASAR metadata traversal budget exceeded")
    for name, item in children.items():
        if not isinstance(name, str) or name in (".", "..") or "/" in name or "\\" in name:
            raise SystemExit("Unsafe path in ASAR header")
        path = prefix + name
        if not isinstance(item, dict):
            raise SystemExit("Invalid file node")
        if isinstance(item.get("files"), dict):
            todo.append((path + "/", item["files"]))
            continue
        count += 1
        low = path.casefold()
        for token in classified:
            if token in low:
                classified[token] += 1
                if len(examples[token]) < 10 and (low.endswith(".js") or low.endswith(".json") or
                                                  low.endswith(".thrift")):
                    examples[token].append(path)

print("LAUNCHER2_ASAR_HEADER_OFFLINE=PASS")
print("BUNDLED_FILES_METADATA_COUNT=" + str(count))
for key, number in classified.items():
    print(f"{key.upper()}_PATHS={number}")
    for path in examples[key]:
        print(f"{key.upper()}_SOURCE_PATH={path}")
print("PROTOCOL_VERSION=UNKNOWN, NO_TOKEN_OR_LAUNCH_AUTHORITY")
