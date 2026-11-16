#!/usr/bin/env python3
"""Offline source-contract and Dofus 3.7 candidate-catalog compatibility gate.

This tool reads files only. A favorable result is never evidence about GivreClone.exe,
a running game client, a real wire exchange, or a server acknowledgement.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path


TARGET_VERSION = "3.7.10.10"
PINS = {
    "matching_current.json": (89144, "c1da4e5090d07c41cc5f6e0d74abe0cdb4777db08ccd2c73de2692e0a4a2dc01"),
    "fields.json": (295299, "58ba845bf9057556127bbe683617f07acbb9dde9824c8be81c70fe1744910f19"),
}
TYPE_PREFIX = "type.ankama.com/"
NAME_PREFIX = "MessageType."
MAX_INPUT_BYTES = 8 * 1024 * 1024
PAIR = re.compile(r'\(\s*"([A-Za-z][A-Za-z0-9_]*)"\s*,\s*"([a-zA-Z][a-zA-Z0-9_]*)"\s*\)')


class GuardError(ValueError):
    pass


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def safe_read(path: Path) -> bytes:
    try:
        if path.stat().st_size > MAX_INPUT_BYTES:
            raise GuardError(f"input_too_large:{path.name}")
        data = path.read_bytes()
        if len(data) > MAX_INPUT_BYTES:
            raise GuardError(f"input_too_large:{path.name}")
        return data
    except GuardError:
        raise
    except (OSError, ValueError) as exc:
        raise GuardError(f"missing_or_unreadable:{path.name}") from exc


def unique_object(pairs: list[tuple[str, object]]) -> dict[str, object]:
    obj: dict[str, object] = {}
    for key, value in pairs:
        if key in obj:
            raise GuardError(f"duplicate_json_key:{key}")
        obj[key] = value
    return obj


def load_json(data: bytes, label: str) -> dict:
    try:
        obj = json.loads(data.decode("utf-8"), object_pairs_hook=unique_object)
    except (UnicodeDecodeError, json.JSONDecodeError, GuardError) as exc:
        raise GuardError(f"malformed_json:{label}:{exc}") from exc
    if not isinstance(obj, dict) or not obj:
        raise GuardError(f"expected_nonempty_object:{label}")
    return obj


def verified_file(path: Path, expected: tuple[int, str]) -> bytes:
    data = safe_read(path)
    if len(data) != expected[0] or sha256(data) != expected[1]:
        raise GuardError(f"sha_or_size_drift:{path.name}")
    return data


def parse_matching(raw: dict) -> dict[str, str]:
    codes: dict[str, str] = {}
    names: dict[str, str] = {}
    for key, value in raw.items():
        if (not isinstance(key, str) or not key.startswith(TYPE_PREFIX)
                or not isinstance(value, str) or not value.startswith(NAME_PREFIX)):
            raise GuardError("invalid_matching_entry")
        code, name = key[len(TYPE_PREFIX):], value[len(NAME_PREFIX):]
        if not re.fullmatch(r"[A-Za-z0-9_]+", code) or not re.fullmatch(r"[A-Za-z0-9_]+", name):
            raise GuardError("invalid_matching_identifier")
        if name in names and names[name] != code:
            raise GuardError(f"name_maps_to_multiple_codes:{name}")
        names[name] = code
        codes[code] = name
    return names


def parse_fields(raw: dict) -> tuple[dict[str, dict[str, int]], dict[str, dict[int, list[str]]]]:
    fields: dict[str, dict[str, int]] = {}
    collisions: dict[str, dict[int, list[str]]] = {}
    for msg, by_name in raw.items():
        if not isinstance(msg, str) or not isinstance(by_name, dict):
            raise GuardError("invalid_fields_message")
        fields[msg] = {}
        by_num: dict[int, list[str]] = {}
        for name, num in by_name.items():
            # bool is an int in Python, but is not a protobuf tag.
            if (not isinstance(name, str) or not isinstance(num, int) or isinstance(num, bool)
                    or num < 1 or num > 536870911 or 19000 <= num <= 19999):
                raise GuardError(f"invalid_field_tag:{msg}.{name}")
            fields[msg][name] = num
            by_num.setdefault(num, []).append(name)
        duplicates = {num: sorted(names) for num, names in by_num.items() if len(names) > 1}
        if duplicates:
            collisions[msg] = duplicates
    return fields, collisions


def extract_requirements(source_root: Path) -> tuple[set[tuple[str, str]], dict[str, str]]:
    files = ("RequiredMapping.cs", "RequiredMapping.Extra.cs")
    result: set[tuple[str, str]] = set()
    provenance: dict[str, str] = {}
    for filename, array_name in zip(files, ("Scanned", "Extra")):
        path = source_root / "GivreClone.Protocol" / filename
        data = safe_read(path)
        provenance[filename] = sha256(data)
        content = data.decode("utf-8-sig")
        match = re.search(r"\b" + array_name + r"\s*=\s*\{(?P<body>.*?)\};", content, re.S)
        if match is None:
            raise GuardError(f"manifest_array_missing:{filename}:{array_name}")
        body = re.sub(r"//[^\n]*", "", match.group("body"))
        pairs = PAIR.findall(body)
        residue = PAIR.sub("", body)
        if not re.fullmatch(r"[\s,]*", residue) or not pairs:
            raise GuardError(f"manifest_has_unparsed_entries:{filename}")
        result.update(pairs)
    if len(result) < 300:
        raise GuardError("manifest_too_small_or_incomplete")
    return result, provenance


def assess(target_dir: Path, source_root: Path, source_bundle: Path | None = None,
           source_manifest: Path | None = None, target_version: str = TARGET_VERSION) -> dict:
    if target_version != TARGET_VERSION:
        raise GuardError("unsupported_target_version")
    provenance = safe_read(target_dir / "PROVENANCE.md").decode("utf-8")
    for filename, (_, digest) in PINS.items():
        if filename not in provenance or digest not in provenance:
            raise GuardError(f"target_provenance_missing_pin:{filename}")
    target_raw = {name: load_json(verified_file(target_dir / name, pin), name)
                  for name, pin in PINS.items()}
    aliases = parse_matching(target_raw["matching_current.json"])
    fields, collisions = parse_fields(target_raw["fields.json"])
    required, source_hashes = extract_requirements(source_root)
    missing = sorted(f"{msg}.{field}" for msg, field in required
                     if field not in fields.get(msg, {}))
    ambiguous = sorted(f"{msg}.{name}" for msg, name in required
                       if len(collisions.get(msg, {}).get(fields.get(msg, {}).get(name, -1), [])) > 1)
    report = {
        "verdict": "SOURCE_ONLY", "requested_target_version": TARGET_VERSION,
        "client_build_attested": False,
        "target_catalogue": "CANDIDATE_ONLY", "exe_parity": "NOT_VERIFIED",
        "runtime_transport": "NOT_VERIFIED", "server_ack": "NOT_VERIFIED",
        "target_sha256": {name: digest for name, (_, digest) in PINS.items()},
        "source_declaration_sha256": source_hashes,
        "target_aliases": len(aliases), "target_messages_with_fields": len(fields),
        "required_pairs": len(required), "missing_required": missing,
        "ambiguous_required": ambiguous,
        "collision_messages": len(collisions),
        "collision_examples": [f"{msg}#{num}:{','.join(names)}"
                               for msg, nums in sorted(collisions.items())
                               for num, names in sorted(nums.items())][:16],
        "differences": [],
    }
    if (source_bundle is None) != (source_manifest is None):
        raise GuardError("source_bundle_and_manifest_required_together")
    if source_bundle is None:
        report["reason"] = "NO_ATTESTED_GIVRE_3_7_SOURCE_BUNDLE"
        return report
    manifest = load_json(safe_read(source_manifest), "source_manifest")
    if manifest.get("client_version") != TARGET_VERSION:
        raise GuardError("source_version_mismatch")
    if manifest.get("origin") not in ("authorized_offline_extract", "user_supplied_corpus"):
        raise GuardError("source_origin_unattested")
    remote_raw: dict[str, dict] = {}
    source_digests: dict[str, str] = {}
    for filename in PINS:
        entry = manifest.get("files", {}).get(filename) if isinstance(manifest.get("files"), dict) else None
        if (not isinstance(entry, dict) or not isinstance(entry.get("sha256"), str)
                or not isinstance(entry.get("size"), int) or isinstance(entry.get("size"), bool)):
            raise GuardError(f"source_manifest_pin_missing:{filename}")
        content = safe_read(source_bundle / filename)
        if len(content) != entry["size"] or sha256(content) != entry["sha256"]:
            raise GuardError(f"source_bundle_pin_mismatch:{filename}")
        source_digests[filename] = sha256(content)
        remote_raw[filename] = load_json(content, filename)
    remote_aliases = parse_matching(remote_raw["matching_current.json"])
    remote_fields, remote_collisions = parse_fields(remote_raw["fields.json"])
    differences = ([f"alias:{name}" for name in sorted(set(aliases) | set(remote_aliases))
                    if aliases.get(name) != remote_aliases.get(name)] +
                   [f"tag:{msg}.{field}" for msg in sorted(set(fields) | set(remote_fields))
                    for field in sorted(set(fields.get(msg, {})) | set(remote_fields.get(msg, {})))
                    if fields.get(msg, {}).get(field) != remote_fields.get(msg, {}).get(field)])
    report["source_bundle_sha256"] = source_digests
    report["differences"] = differences[:100]
    report["difference_count"] = len(differences)
    report["source_collision_messages"] = len(remote_collisions)
    report["source_missing_required"] = sorted(f"{msg}.{field}" for msg, field in required
                                                if field not in remote_fields.get(msg, {}))
    if differences or missing or ambiguous or report["source_missing_required"] or remote_collisions:
        report["verdict"] = "REVIEW_REQUIRED"
        report["reason"] = "CATALOGUE_DRIFT_OR_INCOMPLETE_OR_AMBIGUOUS"
    else:
        report["verdict"] = "CATALOGUE_CONCORDANT_CANDIDATE"
        report["reason"] = "OFFLINE_CATALOGUE_COMPARISON_ONLY"
    return report


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--target-dir", type=Path, required=True)
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--source-bundle", type=Path)
    parser.add_argument("--source-manifest", type=Path)
    parser.add_argument("--target-version", default=TARGET_VERSION)
    args = parser.parse_args(argv)
    try:
        result = assess(args.target_dir, args.source_root, args.source_bundle,
                        args.source_manifest, args.target_version)
    except (GuardError, OSError, UnicodeDecodeError) as exc:
        print(json.dumps({"verdict": "BLOCKED", "reason": str(exc)}, sort_keys=True))
        return 2
    print(json.dumps(result, sort_keys=True, ensure_ascii=True))
    return 0 if result["verdict"] == "CATALOGUE_CONCORDANT_CANDIDATE" else 2


if __name__ == "__main__":
    sys.exit(main())
