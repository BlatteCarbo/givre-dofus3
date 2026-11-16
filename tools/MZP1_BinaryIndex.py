#!/usr/bin/env python3
"""Bounded read-only inventory of detached PE files under approved /givre.

Only names and file metadata are catalogued for general binaries. Hash/PE
metadata are read for narrowly scoped GivreClone.exe and Givre* binary candidates.
No process enumeration, installation probing, account data, dumps or captures.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import stat
import struct
from pathlib import Path

BLOCKED_NAMES = frozenset({
    ".git", ".vs", ".idea", "node_modules", "__pycache__", ".nuget",
    "captures", "capture", "pcaps", "pcap", "secrets", "secret",
    "configs", "config", "configuration", "profiles", "accounts",
    "credentials", "tokens", "userdata", "appdata", "installed",
    "installation", "live", "runtime-dumps", "dump", "dumps",
    "browser-data", "logs", "cache", "obj", ".venv", "venv", "site-packages",
    "assets", "asset", "third_party", "vendor",
})
MAX_DEPTH = 10
MAX_DIRECTORIES = 25_000
MAX_ENTRIES = 250_000
MAX_BINARY_RECORDS = 3_000
MAX_CANDIDATES = 64
MAX_HASH_SIZE = 512 * 1024 * 1024


def prohibited(name: str) -> bool:
    label = name.casefold()
    return (label in BLOCKED_NAMES or label.startswith(("capture-", "captures-", "secret-", "secrets-",
                                                           "givre-live-", "account-", "config-", "dump-"))
            or label.endswith((".dmp", ".mdmp", ".core")))


def is_reparse(entry: os.DirEntry[str]) -> bool:
    try:
        info = entry.stat(follow_symlinks=False)
        return (entry.is_symlink() or bool(getattr(info, "st_file_attributes", 0) &
                    getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0)))
    except OSError:
        return True


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as fp:
        for block in iter(lambda: fp.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def pe_header(path: Path) -> dict:
    """Minimal immutable offline classification for named binary candidates."""
    try:
        with path.open("rb") as fp:
            header = fp.read(64)
            if len(header) != 64 or header[:2] != b"MZ":
                return {"status": "not_pe"}
            offset = struct.unpack_from("<I", header, 0x3C)[0]
            if offset < 0x40 or offset > 8 * 1024 * 1024 or offset > path.stat().st_size - 24:
                return {"status": "invalid_pe_pointer"}
            fp.seek(offset)
            coff = fp.read(24)
            if len(coff) != 24 or coff[:4] != b"PE\0\0":
                return {"status": "invalid_pe_signature"}
            machine, section_count = struct.unpack_from("<HH", coff, 4)
            optional_size = struct.unpack_from("<H", coff, 20)[0]
            if section_count not in range(1, 97) or optional_size not in range(96, 4097):
                return {"status": "invalid_optional_header"}
            opt = fp.read(optional_size)
            if len(opt) != optional_size:
                return {"status": "truncated_optional_header"}
            magic = struct.unpack_from("<H", opt, 0)[0]
            if magic not in (0x10B, 0x20B):
                return {"status": "unknown_pe_format"}
            directory_base = 112 if magic == 0x20B else 96
            directory_count = struct.unpack_from("<I", opt, directory_base - 4)[0]
            clr_directory = directory_base + 14 * 8
            clr_rva, clr_size = (struct.unpack_from("<II", opt, clr_directory)
                                 if directory_count >= 15 and len(opt) >= clr_directory + 8 else (0, 0))
            sections = fp.read(section_count * 40)
            if len(sections) != section_count * 40:
                return {"status": "truncated_section_table"}
            section_ranges = []
            for i in range(section_count):
                entry = sections[i * 40:i * 40 + 40]
                address, raw_size, raw_offset = (struct.unpack_from("<I", entry, 12)[0],
                                                 struct.unpack_from("<I", entry, 16)[0],
                                                 struct.unpack_from("<I", entry, 20)[0])
                section_ranges.append((address, raw_size, raw_offset))

            def from_rva(rva: int, size: int) -> bytes | None:
                for address, raw_size, raw_offset in section_ranges:
                    delta = rva - address
                    if 0 <= delta and delta + size <= raw_size and raw_offset + delta + size <= path.stat().st_size:
                        fp.seek(raw_offset + delta)
                        value = fp.read(size)
                        return value if len(value) == size else None
                return None

            metadata_valid = False
            il_only = False
            if clr_rva and clr_size >= 0x48:
                cor20 = from_rva(clr_rva, 0x48)
                if cor20 and struct.unpack_from("<I", cor20, 0)[0] >= 0x48:
                    meta_rva, meta_size = struct.unpack_from("<II", cor20, 8)
                    metadata_valid = bool(meta_size >= 16 and from_rva(meta_rva, 4) == b"BSJB")
                    il_only = bool(struct.unpack_from("<I", cor20, 16)[0] & 1)
            return {"status": "pe", "architecture": {0x8664: "x64", 0x014c: "x86", 0xaa64: "arm64"}.get(machine, "unknown"),
                    "section_count": section_count, "clr_directory_present": bool(clr_rva and clr_size),
                    "clr_metadata_signature_valid": metadata_valid, "clr_il_only": il_only}
    except (OSError, ValueError, struct.error):
        return {"status": "unreadable_pe_header"}


def inventory(root: Path, *, max_dirs: int = MAX_DIRECTORIES, max_entries: int = MAX_ENTRIES,
              max_records: int = MAX_BINARY_RECORDS) -> dict:
    root = root.resolve(strict=True)
    if not root.is_dir():
        raise ValueError("approved root is not a directory")
    queue = [(root, 0)]
    dirs_seen = 0
    entries_seen = 0
    skipped = {"prohibited": 0, "reparse": 0, "depth": 0, "nonbinary": 0,
               "unreadable": 0, "limit": 0}
    records: list[dict] = []
    candidates: list[dict] = []
    complete = True
    while queue:
        if dirs_seen >= max_dirs or entries_seen >= max_entries:
            complete = False
            skipped["limit"] += len(queue)
            break
        directory, depth = queue.pop(0)
        dirs_seen += 1
        try:
            with os.scandir(directory) as opened:
                children = sorted(list(opened), key=lambda item: item.name.casefold())
        except OSError:
            skipped["unreadable"] += 1
            complete = False
            continue
        def priority(entry: os.DirEntry[str]) -> tuple[int, str]:
            label = entry.name.casefold()
            return (0 if label == "givresharework" else
                    1 if label.startswith("givre") else
                    2 if any(part in label for part in ("offline", "detached", "release", "publish", "binary")) else
                    3, label)

        # The scan is bounded; inspect named local Givre/offline trees before
        # unrelated gigantic Unity/source trees so negative Givre findings are useful.
        children.sort(key=priority)
        for entry in children:
            entries_seen += 1
            if entries_seen > max_entries:
                complete = False
                skipped["limit"] += 1
                break
            if prohibited(entry.name):
                skipped["prohibited"] += 1
                continue
            if is_reparse(entry):
                skipped["reparse"] += 1
                continue
            try:
                if entry.is_dir(follow_symlinks=False):
                    if depth >= MAX_DEPTH:
                        skipped["depth"] += 1
                        complete = False
                    else:
                        queue.append((Path(entry.path), depth + 1))
                    continue
                if not entry.is_file(follow_symlinks=False):
                    continue
            except OSError:
                skipped["unreadable"] += 1
                complete = False
                continue
            if Path(entry.name).suffix.casefold() not in (".exe", ".dll"):
                skipped["nonbinary"] += 1
                continue
            if len(records) >= max_records:
                skipped["limit"] += 1
                complete = False
                continue
            path = Path(entry.path)
            rel = path.relative_to(root).as_posix()
            try:
                size = entry.stat(follow_symlinks=False).st_size
            except OSError:
                skipped["unreadable"] += 1
                complete = False
                continue
            name = entry.name.casefold()
            row = {"relative_path": rel, "bytes": size,
                   "family": "givrebot_exe" if name == "givrebot.exe"
                   else "givre_named" if name.startswith("givre") else "other"}
            records.append(row)
            if (row["family"] != "other" and len(candidates) < MAX_CANDIDATES and
                256 <= size <= MAX_HASH_SIZE):
                try:
                    candidate = {**row, "sha256": sha256(path), "pe": pe_header(path)}
                    candidates.append(candidate)
                except OSError:
                    skipped["unreadable"] += 1
                    complete = False
        if entries_seen > max_entries:
            break
    records.sort(key=lambda row: row["relative_path"].casefold())
    candidates.sort(key=lambda row: row["relative_path"].casefold())
    exact = [r for r in candidates if r["family"] == "givrebot_exe"]
    return {"schema_version": 1, "classification": "offline_readonly_bounded_inventory",
            "limits": {"depth": MAX_DEPTH, "directories": max_dirs,
                       "entries": max_entries, "binary_records": max_records,
                       "named_candidates_hashed": MAX_CANDIDATES},
            "coverage_complete_within_limits": complete,
            "directories_scanned": dirs_seen, "entries_inspected": entries_seen,
            "binary_files_indexed": len(records), "skipped": skipped,
            "givrebot_exe_found": bool(exact), "givrebot_candidates": exact,
            "named_candidates": candidates, "files": records,
            "installation_or_process_inspected": False}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    root = Path(__file__).resolve().parents[2]
    parser.add_argument("--focus", choices=("all", "GivreWork", "GivreAI",
                                            "GivreKnowledgeCrawler", "GivreSacrieurAutopilot"),
                        default="all", help="approved priority subtree only")
    parser.add_argument("--output", type=Path, default=None,
                        help="optional JSON report file (only within GivreWork/tools/MZP1_Binary*)")
    args = parser.parse_args()
    focus_root = root if args.focus == "all" else root / args.focus
    report = inventory(focus_root)
    report["scope"] = args.focus
    rendered = json.dumps(report, sort_keys=True, indent=2, ensure_ascii=False) + "\n"
    if args.output:
        destination = args.output.absolute()
        if (destination.parent.resolve() != Path(__file__).resolve().parent
            or not destination.name.startswith("MZP1_Binary") or destination.suffix != ".json"):
            parser.error("report file must be new tools/MZP1_Binary*.json")
        if destination.exists():
            parser.error("refusing to overwrite report")
        with destination.open("x", encoding="utf-8") as fp:
            fp.write(rendered)
    print(json.dumps({k: v for k, v in report.items() if k not in ("files", "named_candidates")},
                     sort_keys=True, indent=2, ensure_ascii=False))
    return 0 if report["coverage_complete_within_limits"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
