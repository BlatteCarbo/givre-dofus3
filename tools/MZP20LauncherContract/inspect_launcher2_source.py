"""Read-only, bounded source inspection of *installed Launcher 2* startup scripts.

No process, network, original Givre, authentication store, or account access.
Only reads an exact allow-list of public JavaScript bundled inside app.asar.
This is source evidence, not evidence of execution or a valid game ticket.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import struct


ALLOW = {
    "electron-main.js": 800_000,
    "package.json": 50_000,
    "node_modules/protocol-generated-nodejs/ZaapService.js": 200_000,
}


def member_bytes(name: str) -> bytes:
    if name not in ALLOW:
        raise ValueError("Member is outside explicit startup/source allow-list")
    path = (Path(os.environ["LOCALAPPDATA"]) / "Programs" /
            "Ankama Launcher 2" / "resources" / "app.asar")
    if not path.is_file() or path.is_symlink() or path.stat().st_size > 800_000_000:
        raise ValueError("Launcher 2 ASAR is missing or unsafe")
    with path.open("rb") as stream:
        header = stream.read(16)
        if len(header) != 16:
            raise ValueError("Truncated ASAR header")
        outer, header_size, _json_header_size, text_size = struct.unpack("<IIII", header)
        if not (outer in (4, 8) and 32 < header_size < 8_000_000
                and 32 < text_size < 8_000_000):
            raise ValueError("Malformed ASAR header bounds")
        tree = json.loads(stream.read(text_size).decode("utf-8"))["files"]
        for part in name.split("/"):
            node = tree[part]
            if part != name.split("/")[-1]:
                tree = node["files"]
        size = node.get("size")
        offset = int(node.get("offset", -1))
        if (not isinstance(size, int) or not 0 < size <= ALLOW[name]
                or offset < 0 or node.get("unpacked")):
            raise ValueError("ASAR member bounds or layout changed")
        first = 8 + header_size + offset
        if first + size > path.stat().st_size:
            raise ValueError("ASAR member exceeds file")
        stream.seek(first)
        data = stream.read(size)
        if len(data) != size:
            raise ValueError("ASAR member truncated")
        return data


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--member", choices=sorted(ALLOW), default="electron-main.js")
    parser.add_argument("--terms", nargs="+", required=True)
    parser.add_argument("--limit", type=int, default=3)
    parser.add_argument("--radius", type=int, default=180)
    parser.add_argument("--offset", type=int)
    parser.add_argument("--span", type=int, default=0)
    args = parser.parse_args()
    if not 1 <= args.limit <= 10 or not 0 <= args.radius <= 300:
        raise SystemExit("Inspection budget exceeded")
    if any(not re.fullmatch(r"[\w.\-/()@:=]+", term, flags=re.ASCII)
           for term in args.terms):
        raise SystemExit("Invalid search term")
    payload = member_bytes(args.member)
    source = payload.decode("utf-8", "strict")
    print("MEMBER", args.member, "BYTES", len(payload),
          "SHA256", hashlib.sha256(payload).hexdigest(),
          "NEWLINES", source.count("\n"))
    if args.offset is not None:
        if (args.offset < 0 or not 1 <= args.span <= 5000
                or args.offset + args.span > len(source)):
            raise SystemExit("Invalid source window bounds")
        print("SOURCE_WINDOW", args.offset, args.offset + args.span,
              source[args.offset:args.offset + args.span].replace("\r", " ").replace("\n", " "))
    for term in args.terms:
        matches = list(re.finditer(re.escape(term), source, re.IGNORECASE))
        print("TERM", term, "COUNT", len(matches))
        for match in matches[:args.limit]:
            before = source[max(0, match.start() - args.radius):match.start()]
            after = source[match.end():min(len(source), match.end() + args.radius)]
            # Keep snippets bounded; do not follow external imported resources.
            snippet = (before + "[MATCH:" + term + "]" + after).replace("\r", " ").replace("\n", " ")
            print("OFFSET", match.start(), "LINE", source.count("\n", 0, match.start()) + 1,
                  "SNIPPET", snippet)


if __name__ == "__main__":
    main()
