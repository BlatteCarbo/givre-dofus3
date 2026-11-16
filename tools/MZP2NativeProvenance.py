#!/usr/bin/env python3
"""Bounded detached-EXE search, exact SHA admission, read-only PE fingerprint.

The installed executable, live processes, dumps, configs, secrets, captures,
archives, and protected payload unpacking are deliberately OUT OF SCOPE.
No target is ever executed and no protection/licensing state is inspected.
"""

from __future__ import annotations

import argparse
import hashlib
import itertools
import json
import math
import os
import re
import stat
import struct
from collections import Counter
from pathlib import Path

EXPECTED_SHA256 = "21c9de5ba4d15763e9daf029fd7432e3574ea113c9d1f44175fa9e0590290758"
EXPECTED_BYTES = 54_083_584
ROOT_NAMES = (
    "analysis", "artifacts", "DocraftResearch_20261010", "DoFarmCurrentAnalysis",
    "dofusmasters-bot", "DofusMultiPublicSource", "DofusMultiREAReview",
    "DofusMultiRecovered", "DofusReverseIndexer", "DofusSpriteRendererWork",
    "ExoFastResearch_20261010", "ReaReference", "retro-survival-autobot",
    "GivreCompanionReference", "GivreRework", "GivreUnity", "_safety",
    "_tmp_cytrus_studio_20261009", "_tmp_dofus_account_generator_reference",
    "_tmp_givre_native_runtime_compile",
)
EXCLUDE_COMPONENTS = frozenset({
    ".git", ".vs", ".idea", ".venv", "venv", "node_modules", "site-packages",
    "obj", "secrets", "secret", "config", "configs", "configuration", "credentials",
    "token", "tokens", "accounts", "userdata", "appdata", "profile", "profiles",
    "dump", "dumps", "captures", "capture", "pcaps", "pcap", "cache", "logs",
    "installed", "installation", "live", "runtime", "active", "archives",
    "snapshots", "third_party", "vendor", "assets", "asset", "images", "textures",
})
MAX_DEPTH = 10
MAX_DIRS_PER_ROOT = 4_000
MAX_ENTRIES_PER_ROOT = 80_000
MAX_EXES_PER_ROOT = 5_000
MAX_MATCHES = 8
MAX_PE_BYTES = 512 * 1024 * 1024
MAX_SECTIONS = 96
MAX_IMPORT_MODULES = 96
MAX_RESOURCE_TYPES = 96
MAX_SECTION_ENTROPY_SAMPLE = 65_536
RESOURCE_TYPES = {1: "CURSOR", 2: "BITMAP", 3: "ICON", 4: "MENU", 5: "DIALOG",
                  6: "STRING", 7: "FONTDIR", 8: "FONT", 9: "ACCELERATOR",
                  10: "RCDATA", 11: "MESSAGETABLE", 12: "GROUP_CURSOR",
                  14: "GROUP_ICON", 16: "VERSION", 24: "MANIFEST"}


class RejectedOfflineArtifact(ValueError):
    pass


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for part in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(part)
    return digest.hexdigest()


def is_link(path: Path) -> bool:
    if path.is_symlink() or getattr(path, "is_junction", lambda: False)():
        return True
    try:
        info = path.stat(follow_symlinks=False)
    except OSError:
        return True
    return bool(getattr(info, "st_file_attributes", 0) &
                getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0))


def excluded(name: str) -> bool:
    value = name.casefold()
    return (value in EXCLUDE_COMPONENTS or value.startswith((
        "capture-", "captures-", "secret-", "secrets-", "config-",
        "account-", "dump-", "givre-live-", "installed-", "active-")))


def search_detached(root: Path, *, roots: tuple[str, ...] = ROOT_NAMES,
                    max_dirs: int = MAX_DIRS_PER_ROOT,
                    max_entries: int = MAX_ENTRIES_PER_ROOT) -> dict:
    """Search ONLY previously unindexed named trees, never an installation.

    All EXE names and sizes are examined; bytes are hashed only for the exact
    target size, including files renamed from GivreClone.exe. No ZIP inspection.
    """
    root = root.resolve(strict=True)
    if not root.is_dir():
        raise RejectedOfflineArtifact("the approved /givre root is not a directory")
    results = []
    matched = []
    for top in roots:
        if top not in ROOT_NAMES or excluded(top):
            raise RejectedOfflineArtifact("root not on previously unindexed allowlist")
        base = root / top
        if not base.is_dir() or is_link(base):
            results.append({"subtree": top, "status": "absent_or_reparse"})
            continue
        stack = [(base, 0)]
        dirs = entries = exe_names = size_candidates = 0
        skipped = Counter()
        complete = True
        while stack:
            if dirs >= max_dirs or entries >= max_entries:
                skipped["budget"] += len(stack)
                complete = False
                break
            current, depth = stack.pop()
            dirs += 1
            try:
                with os.scandir(current) as listing:
                    # Bounded per-directory memory even for enormous build trees.
                    # Exceeding the remaining budget means the branch is partial.
                    children = list(itertools.islice(listing, max_entries - entries + 1))
                    if len(children) > max_entries - entries:
                        skipped["budget"] += 1
                        complete = False
                        children = children[:max_entries - entries]
                    children.sort(key=lambda item: item.name.casefold())
            except OSError:
                skipped["unreadable_directory"] += 1
                complete = False
                continue
            for child in children:
                entries += 1
                if entries > max_entries:
                    skipped["budget"] += 1
                    complete = False
                    break
                if excluded(child.name):
                    skipped["excluded"] += 1
                    continue
                path = Path(child.path)
                if is_link(path):
                    skipped["reparse"] += 1
                    continue
                try:
                    if child.is_dir(follow_symlinks=False):
                        if depth >= MAX_DEPTH:
                            skipped["max_depth"] += 1
                            complete = False
                        else:
                            stack.append((path, depth + 1))
                        continue
                    if not child.is_file(follow_symlinks=False):
                        continue
                    if child.name.casefold().endswith(".dmp"):
                        skipped["dump"] += 1
                        continue
                    if path.suffix.casefold() != ".exe":
                        continue
                    exe_names += 1
                    if exe_names > MAX_EXES_PER_ROOT:
                        skipped["exe_budget"] += 1
                        complete = False
                        continue
                    size = child.stat(follow_symlinks=False).st_size
                    if size != EXPECTED_BYTES:
                        continue
                    size_candidates += 1
                    # A match by name, timestamp or PE section is never enough:
                    # identity admission requires all 32 SHA-256 bytes.
                    digest = sha256(path)
                    if digest == EXPECTED_SHA256:
                        if len(matched) >= MAX_MATCHES:
                            skipped["match_limit"] += 1
                            complete = False
                        else:
                            matched.append({"path": path.relative_to(root).as_posix(),
                                            "size": size, "sha256": digest,
                                            "exact_reference_match": True})
                    else:
                        skipped["hash_mismatch"] += 1
                except OSError:
                    skipped["unreadable_entry"] += 1
                    complete = False
            if entries > max_entries:
                skipped["budget"] += len(stack)
                break
        results.append({"subtree": top,
                        "status": "complete" if complete else "partial",
                        "directories_scanned": dirs,
                        "entries_examined": entries,
                        "exe_names_seen": min(exe_names, MAX_EXES_PER_ROOT),
                        "exe_size_candidates_hashed": size_candidates,
                        "skipped": dict(sorted(skipped.items()))})
    return {"search_scope": "previously_unindexed_allowlisted_subtrees",
            "binary_reference": {"sha256": EXPECTED_SHA256,
                                 "size_bytes": EXPECTED_BYTES,
                                 "provenance": "MZP1_PRIME_HANDOFF_20261016.md:15-31; installed read-only observation by PRIME"},
            "limits": {"max_depth": MAX_DEPTH, "dirs_per_root": max_dirs,
                       "entries_per_root": max_entries, "exe_per_root": MAX_EXES_PER_ROOT,
                       "matches": MAX_MATCHES},
            "subtrees": results, "all_subtrees_complete": all(r["status"] == "complete" for r in results),
            "exact_sha_matches": matched, "installed_executable_read_or_copied": False,
            "archives_inspected": False, "processes_inspected": False}


def _u16(blob: bytes, offset: int) -> int:
    if offset < 0 or offset + 2 > len(blob):
        raise RejectedOfflineArtifact("truncated PE metadata")
    return struct.unpack_from("<H", blob, offset)[0]


def _u32(blob: bytes, offset: int) -> int:
    if offset < 0 or offset + 4 > len(blob):
        raise RejectedOfflineArtifact("truncated PE metadata")
    return struct.unpack_from("<I", blob, offset)[0]


def static_fingerprint(path: Path, root: Path, *, expected_sha: str = EXPECTED_SHA256) -> dict:
    """Only an already detached, hash-pinned EXE gets PE metadata analysis.

    No calls into the executable, no loader, no unpacker, no resource payload
    extraction, no brute force, no memory inspection. Imports = DLL names only.
    """
    canonical_root = root.resolve(strict=True)
    candidate = path.absolute()
    if not candidate.is_relative_to(canonical_root):
        raise RejectedOfflineArtifact("not within /givre")
    relative = candidate.relative_to(canonical_root)
    if len(relative.parts) < 2 or relative.parts[0] not in ROOT_NAMES or candidate.suffix.casefold() != ".exe":
        raise RejectedOfflineArtifact("not an approved detached EXE from the unindexed trees")
    for ancestor in [candidate, *list(candidate.parents)]:
        if ancestor == canonical_root:
            break
        if excluded(ancestor.name) or is_link(ancestor):
            raise RejectedOfflineArtifact("excluded component or reparse point")
    if not candidate.is_file() or candidate.stat().st_size > MAX_PE_BYTES:
        raise RejectedOfflineArtifact("missing or oversize detached PE")
    current_sha = sha256(candidate)
    if current_sha != expected_sha:
        raise RejectedOfflineArtifact("SHA-256 mismatch; no format probing performed")

    size = candidate.stat().st_size
    with candidate.open("rb") as fd:
        def read(offset: int, count: int) -> bytes:
            if offset < 0 or count < 0 or offset + count > size or count > 1024 * 1024:
                raise RejectedOfflineArtifact("PE offset outside bounded file")
            fd.seek(offset)
            out = fd.read(count)
            if len(out) != count:
                raise RejectedOfflineArtifact("truncated PE contents")
            return out

        dos = read(0, 64)
        if dos[:2] != b"MZ":
            raise RejectedOfflineArtifact("not DOS MZ")
        pe_off = _u32(dos, 60)
        if not 64 <= pe_off <= min(size - 24, 8 * 1024 * 1024):
            raise RejectedOfflineArtifact("invalid PE offset")
        header = read(pe_off, 24)
        if header[:4] != b"PE\0\0":
            raise RejectedOfflineArtifact("missing PE signature")
        machine = _u16(header, 4)
        sections_count = _u16(header, 6)
        opt_len = _u16(header, 20)
        if sections_count not in range(1, MAX_SECTIONS + 1) or not 96 <= opt_len <= 4096:
            raise RejectedOfflineArtifact("invalid section/optional header length")
        optional = read(pe_off + 24, opt_len)
        pe_magic = _u16(optional, 0)
        if pe_magic not in (0x10b, 0x20b):
            raise RejectedOfflineArtifact("unsupported optional header")
        dir_base = 96 if pe_magic == 0x10b else 112
        count_offset = dir_base - 4
        directories_count = _u32(optional, count_offset)
        if directories_count > 128 or opt_len < dir_base + min(directories_count, 16) * 8:
            raise RejectedOfflineArtifact("invalid data directory bounds")
        def directory(index: int) -> tuple[int, int]:
            if directories_count <= index:
                return 0, 0
            offset = dir_base + index * 8
            return _u32(optional, offset), _u32(optional, offset + 4)

        section_bytes = read(pe_off + 24 + opt_len, sections_count * 40)
        sections: list[dict] = []
        spans = []
        for i in range(sections_count):
            h = section_bytes[i * 40:i * 40 + 40]
            name = h[:8].rstrip(b"\0").decode("ascii", "replace")
            virt_size, rva, raw_size, raw_ptr = struct.unpack_from("<IIII", h, 8)
            chars = _u32(h, 36)
            if raw_size and (raw_ptr < pe_off + 24 + opt_len + sections_count * 40 or raw_ptr + raw_size > size):
                raise RejectedOfflineArtifact("section outside PE file")
            if raw_size:
                spans.append((raw_ptr, raw_ptr + raw_size))
            if raw_size:
                sample_len = min(raw_size, MAX_SECTION_ENTROPY_SAMPLE)
                sample = read(raw_ptr, sample_len)
                hist = Counter(sample)
                entropy = -sum(n / sample_len * math.log2(n / sample_len) for n in hist.values())
                approximate = round(entropy, 4)
            else:
                approximate = None
            sections.append({"name": name[:32], "rva": rva,
                             "virtual_bytes": virt_size, "raw_bytes": raw_size,
                             "raw_offset": raw_ptr, "characteristics_hex": f"0x{chars:08x}",
                             "start_sample_entropy_bits_per_byte": approximate,
                             "entropy_sample_bytes": min(raw_size, MAX_SECTION_ENTROPY_SAMPLE)})
        spans.sort()
        if any(b[0] < a[1] for a, b in zip(spans, spans[1:])):
            raise RejectedOfflineArtifact("overlapping PE raw sections")

        def read_rva(rva: int, count: int) -> bytes:
            for s in sections:
                offset = rva - s["rva"]
                if 0 <= offset and offset + count <= s["raw_bytes"]:
                    return read(s["raw_offset"] + offset, count)
            raise RejectedOfflineArtifact("RVA outside mapped sections")

        def ascii_cstr(rva: int, max_len: int = 128) -> str:
            raw = read_rva(rva, max_len).split(b"\0", 1)[0]
            if not raw or len(raw) == max_len:
                raise RejectedOfflineArtifact("invalid or unterminated import string")
            if any(c < 32 or c > 126 for c in raw):
                raise RejectedOfflineArtifact("nonprintable import metadata")
            return raw.decode("ascii")

        imports: list[str] = []
        import_rva, import_size = directory(1)
        if bool(import_rva) != bool(import_size):
            raise RejectedOfflineArtifact("partial import data directory")
        if import_rva:
            max_imports = min(MAX_IMPORT_MODULES, import_size // 20)
            terminated = False
            for i in range(max_imports):
                descriptor = read_rva(import_rva + i * 20, 20)
                if descriptor == b"\0" * 20:
                    terminated = True
                    break
                name_rva = _u32(descriptor, 12)
                if name_rva == 0:
                    raise RejectedOfflineArtifact("import descriptor has no DLL name")
                imports.append(ascii_cstr(name_rva).lower())
            if not terminated:
                raise RejectedOfflineArtifact("unbounded/unterminated import directory")

        resource_rva, resource_size = directory(2)
        types: list[dict] = []
        if bool(resource_rva) != bool(resource_size):
            raise RejectedOfflineArtifact("partial resources directory")
        if resource_rva:
            # Only first resource directory metadata level. NEVER read payload.
            hdr = read_rva(resource_rva, 16)
            count = _u16(hdr, 12) + _u16(hdr, 14)
            if count > MAX_RESOURCE_TYPES or 16 + count * 8 > resource_size:
                raise RejectedOfflineArtifact("resource type table outside bounded directory")
            for i in range(count):
                item = read_rva(resource_rva + 16 + i * 8, 8)
                raw_id = _u32(item, 0)
                if raw_id & 0x80000000:
                    # Deliberately do not decode proprietary resource names.
                    types.append({"type": "named_resource_type", "value": None})
                else:
                    types.append({"type": "standard_or_numeric", "id": raw_id,
                                  "value": RESOURCE_TYPES.get(raw_id, "OTHER")})

        clr_rva, clr_bytes = directory(14)
        entrypoint_rva = _u32(optional, 16)
        return {"classification": "pinned_detached_read_only_pe_fingerprint",
                "sha256": current_sha, "bytes": size,
                "relative_path": candidate.relative_to(canonical_root).as_posix(),
                "coff_machine": f"0x{machine:04x}", "pe_format": "PE32+" if pe_magic == 0x20b else "PE32",
                "coff_timestamp": _u32(header, 8),
                "entrypoint_rva": entrypoint_rva,
                "section_count": len(sections), "sections": sections,
                "import_dlls": sorted(set(imports)), "import_descriptor_count": len(imports),
                "resource_type_count": len(types), "resource_types": types,
                "clr_directory_present": bool(clr_rva and clr_bytes),
                "nativeaot_attested": False, "packer_attested": False,
                "classification_uncertainty": "native, NativeAOT, packer/protection or apphost remain unproven",
                "protection_unpacked_or_bypassed": False, "resource_payloads_read": False,
                "executable_started": False, "installed_executable_accessed": False}


SOURCE_ANCHORS = (
    ("assembly_identity", "GivreClone.App/GivreClone.App.csproj", r"<AssemblyName>GivreClone</AssemblyName>", "GivreClone"),
    ("app_source_version", "GivreClone.App/Services/LicenseService.cs", r'AppVersion\s*=\s*"1\.2\.0"', "1.2.0"),
    ("nativeaot_opt_in", "GivreClone.App/GivreClone.App.csproj", r"<PublishAot>true</PublishAot>", "build_config_only"),
    ("session_engine", "GivreClone.Engine/BotSession.cs", r"\bclass\s+BotSession\b", "source_declaration_only"),
    ("action_sender", "GivreClone.Engine/ActionSender.cs", r"\bclass\s+ActionSender\b", "source_declaration_only"),
    ("proxy_dependency", "GivreClone.Engine/DofusCaptureHost.cs", r"\bclass\s+DofusCaptureHost\b", "source_declaration_only"),
)


def source_version_matrix(givre_root: Path, binary_matched: bool) -> dict:
    """Verifiable source symbols only; no assumption that the EXE has them."""
    root = givre_root.resolve(strict=True)
    entries = []
    for name, rel, needle, value in SOURCE_ANCHORS:
        file_path = root / rel
        if not file_path.is_file() or is_link(file_path) or file_path.stat().st_size > 3 * 1024 * 1024:
            entries.append({"symbol": name, "status": "missing_source", "exe_confirmed": False})
            continue
        source = file_path.read_text(encoding="utf-8", errors="strict")
        pattern = re.compile(needle)
        locations = [i for i, line in enumerate(source.splitlines(), 1) if pattern.search(line)]
        entries.append({"symbol": name, "status": "source_present" if locations else "source_anchor_absent",
                        "file": rel, "lines": locations[:8], "file_sha256": sha256(file_path),
                        "declared_value": value if locations else None,
                        "exe_confirmed": False})
    return {"matrix_version": 1,
            "installed_reference": {"sha256": EXPECTED_SHA256, "ui_title": "v1.3.1",
                "sha_provenance": "MZP1_PRIME_HANDOFF_20261016.md:15-31 (read-only PRIME observation)"},
            "detached_binary_verified": binary_matched,
            "runtime_version_drift": "source_appversion_1.2.0_vs_UI_label_v1.3.1_unlinked",
            "symbol_correspondence": "unproven_even_if_exact_EXE_SHA_matches_prior_installed_observation",
            "source_evidence": entries,
            "product_functionality_proven_from_binary": False,
            "nativeaot_assessment": "unknown", "packer_assessment": "unknown"}


def run(root: Path, *, roots: tuple[str, ...] = ROOT_NAMES) -> dict:
    root = root.resolve(strict=True)
    search = search_detached(root, roots=roots)
    verified = []
    for item in search["exact_sha_matches"]:
        path = root / item["path"]
        try:
            verified.append({"status": "matched", "fingerprint": static_fingerprint(
                path, root, expected_sha=EXPECTED_SHA256)})
        except (RejectedOfflineArtifact, OSError) as exc:
            verified.append({"status": "hash_matched_but_pe_rejected", "reason": str(exc)})
    admitted = any(item["status"] == "matched" for item in verified)
    result_status = ("matching_detached_fingerprint_verified" if admitted else
                     "hash_match_but_pe_not_verified" if verified else
                     "blocked_no_matching_detached_exe")
    return {"schema_version": 1, "status": result_status, "search": search,
            "verified_detached_fingerprints": verified,
            "version_drift": source_version_matrix(root / "GivreWork", admitted),
            "unpacking_attempted": False, "licensing_or_drm_accessed": False,
            "installed_executable_accessed": False}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", default=None,
                        help="new MZP2Native*.json beside this script; never overwrites")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    result = run(root)
    summary = {"status": result["status"], "subtrees": result["search"]["subtrees"],
               "all_subtrees_complete": result["search"]["all_subtrees_complete"],
               "sha_matches": result["search"]["exact_sha_matches"],
               "version_drift": result["version_drift"],
               "installed_executable_accessed": False}
    if args.output:
        name = Path(args.output)
        if name.name != args.output or not name.name.startswith("MZP2Native") or name.suffix != ".json":
            parser.error("destination must be a bare MZP2Native*.json filename")
        output = Path(__file__).resolve().parent / name
        with output.open("x", encoding="utf-8") as handle:
            json.dump(result, handle, ensure_ascii=False, sort_keys=True, indent=2)
            handle.write("\n")
    print(json.dumps(summary, ensure_ascii=False, sort_keys=True, indent=2))
    return 0 if result["status"] == "matching_detached_fingerprint_verified" else 2


if __name__ == "__main__":
    raise SystemExit(main())
