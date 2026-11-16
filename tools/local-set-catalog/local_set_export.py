#!/usr/bin/env python3
"""Read-only local Dofus SQLite audit and strictly gated EquipmentSetData export.

Only Python standard library. This tool never contacts the network and never
writes to the SQLite source. Manifest permissions are declared, not verified
by cryptographic signatures. No AI/MCP redistribution rights are granted.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import sqlite3
import sys
import tempfile
from dataclasses import dataclass

MAX_SETS = 10_000
MAX_ITEMS = 100_000
MAX_MEMBERS_PER_SET = 64
MAX_TOTAL_MEMBERS = 100_000
MAX_DATA_BYTES = 24 * 1024 * 1024
MAX_MANIFEST_BYTES = 65_536
MAX_DB_BYTES = 1_073_741_824
MAX_NAME_LENGTH = 256
ALLOWED_LANGUAGES = ("fr", "en")
GAME_VERSION = re.compile(r"^\d{1,5}(?:\.\d{1,5}){3}$", re.ASCII)
SHA256_HEX = re.compile(r"^[a-fA-F0-9]{64}$", re.ASCII)


class ExportError(ValueError):
    """A source, rights assertion, or dataset property failed validation."""


def valid_version(version: str) -> bool:
    return bool(GAME_VERSION.fullmatch(version)) and all(
        0 <= int(part) <= 65535 for part in version.split(".")
    )


def validate_evidence(value: str | None, field: str) -> str:
    if not isinstance(value, str) or not 3 <= len(value) <= 128:
        raise ExportError(f"{field} must be a specific 3-128 character evidence reference")
    if any(ord(char) < 32 or ord(char) == 127 for char in value):
        raise ExportError(f"{field} contains control characters")
    if value.strip().lower() in ("unknown", "none", "unverified", "n/a", "test"):
        raise ExportError(f"{field} cannot be a placeholder")
    return value


def _reject_path_links(path: Path, *, require_file: bool = False) -> Path:
    # Check the literal path *before* resolving to avoid masking reparse points.
    absolute = Path(os.path.abspath(os.fspath(path)))
    if any(part in (".", "..") for part in path.parts):
        raise ExportError("relative traversal segments are not accepted")
    for ancestor in (absolute, *absolute.parents):
        if ancestor.is_symlink() or (hasattr(ancestor, "is_junction") and ancestor.is_junction()):
            raise ExportError(f"linked/reparse path is not accepted: {ancestor}")
    if require_file and (not absolute.is_file() or absolute.stat().st_size < 4096):
        raise ExportError("missing or undersized SQLite source")
    return absolute


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _bounded_sqlite(source: Path, *, allow_sidecars: bool = False) -> tuple[sqlite3.Connection, tuple[int, int, int]]:
    path = _reject_path_links(source, require_file=True)
    stat = path.stat()
    if stat.st_size > MAX_DB_BYTES:
        raise ExportError("SQLite exceeds the 1 GiB offline bound")
    if not allow_sidecars and any(Path(str(path) + suffix).exists() for suffix in ("-wal", "-shm", "-journal")):
        raise ExportError("SQLite has mutable WAL/journal sidecar; prepare a sealed snapshot")
    # mode=ro + immutable=1: no source or sidecar creation.
    database = sqlite3.connect(path.as_uri() + "?mode=ro&immutable=1", uri=True)
    database.execute("PRAGMA query_only=ON")
    database.execute("PRAGMA trusted_schema=OFF")
    database.execute("PRAGMA busy_timeout=1000")
    return database, (stat.st_size, stat.st_mtime_ns, getattr(stat, "st_ino", 0))


def _sqlite_columns(db: sqlite3.Connection, table: str) -> set[str]:
    if table not in ("ItemSetData", "ItemData", "translations"):
        raise ExportError("unrecognized SQLite table")
    return {row[1] for row in db.execute('PRAGMA table_info("' + table + '")')}


def _limited_records(db: sqlite3.Connection, sql: str, bound: int) -> list[tuple]:
    rows = db.execute(sql + f" LIMIT {bound + 1}").fetchall()
    if len(rows) > bound:
        raise ExportError(f"SQLite row cap exceeded: {bound}")
    return rows


def _positive_id(value: object, label: str) -> int:
    if type(value) is not int or value <= 0 or value > 2**63 - 1:
        raise ExportError(f"invalid {label} ID")
    return value


def _source_name_id(value: object) -> int | None:
    # Zero is an unknown name reference; not a fabricated translation.
    if value is None or value == 0:
        return None
    return _positive_id(value, "name")


def _source_members(value: object, set_id: int) -> list[int]:
    if not isinstance(value, str) or len(value) > 8192:
        raise ExportError(f"set {set_id}: invalid JSON list bytes")
    try:
        members = json.loads(value)
    except (json.JSONDecodeError, ValueError) as exc:
        raise ExportError(f"set {set_id}: invalid member JSON") from exc
    if not isinstance(members, list) or not 1 <= len(members) <= MAX_MEMBERS_PER_SET:
        raise ExportError(f"set {set_id}: missing or oversized members")
    parsed = [_positive_id(member, "member") for member in members]
    if len(set(parsed)) != len(parsed):
        raise ExportError(f"set {set_id}: duplicated member IDs")
    return parsed


def _translations(db: sqlite3.Connection, name_ids: set[int], language: str) -> dict[int, str | None]:
    if language not in ALLOWED_LANGUAGES:
        raise ExportError("unsupported translation language")
    if not name_ids:
        return {}
    if not {"id", "lang", "value"} <= _sqlite_columns(db, "translations"):
        raise ExportError("missing translations schema")
    names: dict[int, str | None] = {}
    counts: dict[int, int] = {}
    sorted_names = sorted(name_ids)
    # SQLite has a variable parameter count limit. Only numeric IDs from the
    # source are queried; translations are never fetched as a giant corpus.
    for start in range(0, len(sorted_names), 400):
        group = sorted_names[start:start + 400]
        query = "SELECT id,value FROM translations WHERE lang=? AND id IN (" + (
            ",".join("?" for _ in group)
        ) + ")"
        for raw_id, value in db.execute(query, (language, *map(str, group))):
            try:
                ident = int(raw_id)
            except (ValueError, TypeError):
                continue
            if str(ident) != str(raw_id) or ident not in name_ids:
                continue
            counts[ident] = counts.get(ident, 0) + 1
            # Duplicate translations cannot be silently chosen by query order.
            if counts[ident] > 1:
                names[ident] = None
                continue
            if not isinstance(value, str) or not value.strip():
                names[ident] = None
            elif len(value) > MAX_NAME_LENGTH or any(ord(ch) < 32 for ch in value):
                # Contradictory/out-of-bounds translation stays explicitly unknown.
                names[ident] = None
            else:
                names[ident] = value
    return names


@dataclass(frozen=True)
class Audit:
    source: Path
    stat: tuple[int, int, int]
    sets: tuple[dict, ...]
    items: tuple[dict, ...]
    rejected_sets: tuple[dict, ...]
    invalid_source_sets: tuple[dict, ...]
    source_set_count: int
    source_item_count: int
    source_membership_count: int
    known_members_count: int
    missing_member_references: int
    mismatched_member_tags: int
    foreign_reverse_tags: int
    sqlite_user_version: int
    language: str
    sidecars: tuple[str, ...]
    unassigned_item_sentinels: dict[str, int]

    def summary(self) -> dict:
        return {
            "readOnly": True,
            "sourceFile": str(self.source),
            "sourceBytes": self.stat[0],
            "sqliteUserVersion": self.sqlite_user_version,
            "gameVersion": None,
            "gameVersionVerified": False,
            "sourceHasSQLiteSidecars": bool(self.sidecars),
            "sqliteSidecars": list(self.sidecars),
            "sourceSetCount": self.source_set_count,
            "sourceItemCount": self.source_item_count,
            "unassignedItemSentinels": self.unassigned_item_sentinels,
            "sourceMembershipCount": self.source_membership_count,
            "sourceMembersWithItemData": self.known_members_count,
            "missingMemberReferences": self.missing_member_references,
            "mismatchedMemberTags": self.mismatched_member_tags,
            "foreignReverseTags": self.foreign_reverse_tags,
            "exportableSetCount": len(self.sets),
            "exportableItemCount": len(self.items),
            "rejectedSetCount": len(self.rejected_sets),
            "structurallyInvalidSetCount": len(self.invalid_source_sets),
            "rejectedSetIds": [row["id"] for row in self.rejected_sets],
            "rejectedSetReasons": list(self.rejected_sets),
            "invalidSetReasons": list(self.invalid_source_sets),
            "translationLanguage": self.language,
            "publisherVerified": False,
            "aiDisclosureAuthorized": False
        }


def audit_sqlite(source: Path | str, *, language: str = "fr") -> Audit:
    """Inspect one immutable, read-only SQLite. No export/permission claims."""
    if language not in ALLOWED_LANGUAGES:
        raise ExportError("unsupported language")
    file = _reject_path_links(Path(source), require_file=True)
    sidecars = tuple(suffix for suffix in ("-wal", "-shm", "-journal")
                     if Path(str(file) + suffix).exists())
    db, fingerprint = _bounded_sqlite(file, allow_sidecars=True)
    try:
        if not {"id", "items", "nameId"} <= _sqlite_columns(db, "ItemSetData"):
            raise ExportError("missing ItemSetData columns")
        if not {"id", "itemSetId", "nameId"} <= _sqlite_columns(db, "ItemData"):
            raise ExportError("missing ItemData columns")
        rows_sets = _limited_records(
            db, "SELECT id,items,nameId FROM ItemSetData ORDER BY id", MAX_SETS
        )
        rows_items = _limited_records(
            db, "SELECT id,itemSetId,nameId FROM ItemData ORDER BY id", MAX_ITEMS
        )
        id_to_item: dict[int, tuple[int | None, int | None]] = {}
        unassigned_sentinels: dict[str, int] = {}
        for item_id, item_set_id, name_id in rows_items:
            item_id = _positive_id(item_id, "item")
            if item_id in id_to_item:
                raise ExportError("duplicate ItemData.id")
            if type(item_set_id) is not int or item_set_id < -1:
                raise ExportError("unrecognized/noninteger ItemData.itemSetId sentinel")
            # Actual local SQLite uses -1 (rather than 0) for no set.
            # Keep these rows OUT of the export entirely. Do not claim a
            # fabricated SetId=null item or manufacture any set membership.
            if item_set_id in (-1, 0):
                key = str(item_set_id)
                unassigned_sentinels[key] = unassigned_sentinels.get(key, 0) + 1
            id_to_item[item_id] = (item_set_id if item_set_id > 0 else None,
                                   _source_name_id(name_id))
        set_records: dict[int, tuple[list[int], int | None]] = {}
        invalid_source_sets: list[dict] = []
        source_link_count = 0
        for sid_raw, member_json, name_id in rows_sets:
            sid = _positive_id(sid_raw, "set")
            if sid in set_records:
                raise ExportError("duplicate ItemSetData.id")
            try:
                members = _source_members(member_json, sid)
            except ExportError as exc:
                invalid_source_sets.append({"id": sid, "reason": str(exc)})
                continue
            source_link_count += len(members)
            if source_link_count > MAX_TOTAL_MEMBERS:
                raise ExportError("more than 100000 membership links")
            set_records[sid] = (members, _source_name_id(name_id))
        if len(rows_sets) == 0:
            raise ExportError("no ItemSetData rows; source cannot represent a set catalog")

        assigned_by_set: dict[int, set[int]] = {}
        for item_id, (set_id, _) in id_to_item.items():
            if set_id:
                assigned_by_set.setdefault(set_id, set()).add(item_id)
        rejected: list[dict] = []
        included: list[tuple[int, list[int], int | None]] = []
        missing_count = mismatch_count = reverse_count = known_count = 0
        for sid, (members, name_id) in set_records.items():
            missing = [item for item in members if item not in id_to_item]
            mismatched = [item for item in members if item in id_to_item
                          and id_to_item[item][0] != sid]
            reverse = assigned_by_set.get(sid, set()).difference(members)
            missing_count += len(missing)
            mismatch_count += len(mismatched)
            reverse_count += len(reverse)
            known_count += len(members) - len(missing)
            if missing or mismatched or reverse:
                rejected.append({
                    "id": sid,
                    "reason": "membership inconsistency",
                    "missingItemIds": missing[:64],
                    "contradictingItemIds": mismatched[:64],
                    "reverseMissingItemIds": sorted(reverse)[:64],
                    "observedMemberItemIds": list(members),
                    "observedNameId": name_id,
                    "viewStatus": "UNKNOWN_METADATA" if missing and not mismatched and not reverse
                                  else "INCONSISTENT_MEMBERSHIP",
                    "catalogAdmitted": False
                })
            else:
                included.append((sid, members, name_id))
        for sid in set(assigned_by_set).difference(set_records):
            # ItemData refers to a set not present in ItemSetData. No inferred set.
            reverse_count += len(assigned_by_set[sid])

        exported_ids = {iid for _, members, _ in included for iid in members}
        name_ids = {name for _, _, name in included if name is not None}
        name_ids.update(row["observedNameId"] for row in rejected
                        if row["observedNameId"] is not None)
        name_ids.update(name for iid in exported_ids if
                        (name := id_to_item[iid][1]) is not None)
        names = _translations(db, name_ids, language)
        for row in rejected:
            row["observedName"] = names.get(row.pop("observedNameId"))
        actual_sets = tuple(
            {"id": sid, "name": names.get(name_id), "memberItemIds": members}
            for sid, members, name_id in included
        )
        actual_items = tuple(
            {"id": iid, "name": names.get(id_to_item[iid][1]),
             "setId": id_to_item[iid][0]}
            for iid in sorted(exported_ids)
        )
        source_user_version = db.execute("PRAGMA user_version").fetchone()[0]
        if file.stat().st_size != fingerprint[0] or file.stat().st_mtime_ns != fingerprint[1]:
            raise ExportError("SQLite changed during audit")
        return Audit(
            file, fingerprint, actual_sets, actual_items, tuple(rejected),
            tuple(invalid_source_sets), len(rows_sets), len(rows_items),
            source_link_count, known_count, missing_count, mismatch_count,
            reverse_count, source_user_version, language, sidecars,
            unassigned_sentinels
        )
    finally:
        db.close()


def _json_bytes(value: dict) -> bytes:
    # Exactly the schema accepted by EquipmentSetCatalogReader.
    return (json.dumps(value, ensure_ascii=False, separators=(",", ":"),
                       allow_nan=False) + "\n").encode("utf-8")


def _new_output_directory(destination: Path) -> Path:
    path = _reject_path_links(destination)
    if path.exists():
        raise ExportError("output directory already exists; overwrite is forbidden")
    if not path.parent.is_dir():
        raise ExportError("output parent must already exist")
    return path


def export_catalog(
    audit: Audit, destination: Path | str, *,
    source_owned: bool = False,
    rights_evidence: str | None = None,
    game_version: str | None = None,
    version_evidence: str | None = None,
    expected_source_sha256: str | None = None,
    allow_partial: bool = False,
) -> dict:
    """Export only with explicit caller ownership and SHA/version evidence claims.

    Produces exactly the reader's manifest/equipment-sets fields and a separate
    audit/provenance report. The report NEVER grants permission to feed AI or MCP.
    """
    if not source_owned:
        raise ExportError("export requires --source-owned assertion")
    rights_evidence = validate_evidence(rights_evidence, "--rights-evidence")
    version_evidence = validate_evidence(version_evidence, "--version-evidence")
    if not isinstance(game_version, str) or not valid_version(game_version):
        raise ExportError("valid --game-version with four numeric components is required")
    if not isinstance(expected_source_sha256, str) or not SHA256_HEX.fullmatch(expected_source_sha256):
        raise ExportError("--expected-source-sha256 pin is required")
    if audit.rejected_sets or audit.invalid_source_sets:
        if not allow_partial:
            raise ExportError(
                f"source has {len(audit.rejected_sets) + len(audit.invalid_source_sets)} "
                "inconsistent sets: choose --allow-partial explicitly or fix the source"
            )
    if not audit.sets:
        raise ExportError("no internally consistent sets to export")
    if audit.sidecars:
        raise ExportError("SQLite sidecars detected: export requires a sealed immutable snapshot")
    root = _new_output_directory(Path(destination))
    original = audit.source
    _reject_path_links(original, require_file=True)
    if original.stat().st_size != audit.stat[0] or original.stat().st_mtime_ns != audit.stat[1]:
        raise ExportError("source SQLite changed since audit")
    calculated_source_sha = sha256_file(original)
    if calculated_source_sha.lower() != expected_source_sha256.lower():
        raise ExportError("source SQLite SHA-256 does not match expected pin")

    payload = {"schemaVersion": 1, "sets": audit.sets, "items": audit.items}
    json_data = _json_bytes(payload)
    if not 2 <= len(json_data) <= MAX_DATA_BYTES:
        raise ExportError("JSON output exceeds EquipmentSetCatalogReader size bound")
    output_sha = hashlib.sha256(json_data).hexdigest()
    manifest = {
        "schemaVersion": 1,
        "gameVersion": game_version,
        "source": "local-extraction",
        "permission": "owner-authorized",
        "format": "json",
        "asset": {"file": "equipment-sets.json", "bytes": len(json_data),
                  "sha256": output_sha}
    }
    manifest_data = _json_bytes(manifest)
    if len(manifest_data) > MAX_MANIFEST_BYTES:
        raise ExportError("manifest exceeds reader bound")
    report = {
        "schemaVersion": 1,
        "sourceKind": "user-asserted-owned-local-extraction",
        "sourceSha256": calculated_source_sha,
        "sourceBytes": audit.stat[0],
        "rightsEvidenceId": rights_evidence,
        "rightsClaim": "owner-authorized (caller assertion)",
        "rightsIndependentlyVerified": False,
        "aiDisclosureAuthorized": False,
        "publisherVerified": False,
        "gameVersion": game_version,
        "gameVersionEvidenceId": version_evidence,
        "gameVersionIndependentlyVerified": False,
        "exportedJsonSha256": output_sha,
        "partialExport": bool(audit.rejected_sets or audit.invalid_source_sets),
        # Consultative observations only. Reader ignores this separate file.
        # No EquipmentSetItem placeholder or fabricated association can enter
        # equipment-sets.json: rejected sets remain inadmissible.
        "excludedSetObservations": list(audit.rejected_sets),
        "audit": audit.summary()
    }
    report_data = _json_bytes(report)
    if len(report_data) > 1024 * 1024:
        raise ExportError("provenance audit exceeds bound")

    # Prepare under the same parent and publish as ONE directory rename.
    # No output is created on failed validation. Existing outputs are never touched.
    temporary: Path | None = None
    try:
        temporary = Path(tempfile.mkdtemp(prefix=".set-catalog-stage-", dir=root.parent))
        for filename, data in (
            ("equipment-sets.json", json_data),
            ("manifest.json", manifest_data),
            ("provenance.json", report_data),
        ):
            new_path = temporary / filename
            with new_path.open("xb") as sink:
                sink.write(data)
                sink.flush()
                os.fsync(sink.fileno())
        if root.exists():
            raise ExportError("output was created concurrently; refusing overwrite")
        temporary.rename(root)
        temporary = None
    finally:
        if temporary and temporary.exists():
            # Only files created by this function in its uniquely named staging dir.
            for filename in ("equipment-sets.json", "manifest.json", "provenance.json"):
                (temporary / filename).unlink(missing_ok=True)
            temporary.rmdir()
    return {
        "outputDirectory": str(root),
        "gameVersion": game_version,
        "sourceSha256": calculated_source_sha,
        "outputSha256": output_sha,
        "setCount": len(audit.sets),
        "memberItemCount": len(audit.items),
        "excludedSetCount": len(audit.rejected_sets) + len(audit.invalid_source_sets),
        "publisherVerified": False,
        "aiDisclosureAuthorized": False
    }


def _main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="operation", required=True)
    audit_cmd = commands.add_parser("audit", help="Read-only structural audit; no ownership assertion")
    export_cmd = commands.add_parser("export", help="Build a new pinned local catalog, explicitly gated")
    for cmd in (audit_cmd, export_cmd):
        cmd.add_argument("--db", required=True, type=Path)
        cmd.add_argument("--language", default="fr", choices=ALLOWED_LANGUAGES)
    audit_cmd.add_argument("--source-sha256", action="store_true",
                           help="Print the SHA-256 for a future explicit export pin")
    export_cmd.add_argument("--output", required=True, type=Path)
    export_cmd.add_argument("--source-owned", action="store_true", required=False,
                            help="Assert this is your own game-data extraction and you have rights to export")
    export_cmd.add_argument("--rights-evidence", required=True,
                            help="Explicit provenance/permission evidence reference, stored as a claim")
    export_cmd.add_argument("--game-version", required=True,
                            help="Four-component version declared from independently held evidence")
    export_cmd.add_argument("--version-evidence", required=True,
                            help="Evidence reference for the version you declared")
    export_cmd.add_argument("--expected-source-sha256", required=True,
                            help="Previously audited complete source SQLite SHA-256")
    export_cmd.add_argument("--allow-partial", action="store_true",
                            help="Drop, and report, inconsistent sets; never fabricate missing objects")
    args = parser.parse_args(argv)
    try:
        if args.operation == "export":
            # Check authorization before even opening and decoding the database.
            if not args.source_owned:
                raise ExportError("--source-owned is required for export")
            validate_evidence(args.rights_evidence, "--rights-evidence")
            validate_evidence(args.version_evidence, "--version-evidence")
            if not valid_version(args.game_version):
                raise ExportError("invalid declared game version")
            if not SHA256_HEX.fullmatch(args.expected_source_sha256):
                raise ExportError("missing or malformed source SHA-256 pin")
        report = audit_sqlite(args.db, language=args.language)
        if args.operation == "audit":
            value = report.summary()
            if args.source_sha256:
                value["sourceSha256"] = sha256_file(report.source)
        else:
            value = export_catalog(
                report, args.output, source_owned=args.source_owned,
                rights_evidence=args.rights_evidence, game_version=args.game_version,
                version_evidence=args.version_evidence,
                expected_source_sha256=args.expected_source_sha256,
                allow_partial=args.allow_partial
            )
        print(json.dumps(value, ensure_ascii=False, sort_keys=True, indent=2))
        return 0
    except (ExportError, OSError, sqlite3.Error) as exc:
        print(f"REJECTED: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(_main())
