#!/usr/bin/env python3
"""Read-only, SHA-pinned local Dofus SQLite -> ProfessionCatalogReader JSON v1.

No network, no client interaction and no automated promotion to MCP/AI.
Claims of source ownership and SQLite junction ordering are explicit CLI
attestations, not claims made by the program.
"""
from __future__ import annotations

import argparse
from collections import defaultdict
from contextlib import closing
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import sqlite3
import sys
from urllib.parse import quote

VERSION = re.compile(r"^(?:\d{1,5}\.){3}\d{1,5}$")
SHA256 = re.compile(r"^[a-fA-F0-9]{64}$")
MAX_BYTES = 1_073_741_824
MAX_JSON = 24 * 1024 * 1024
MAX_ITEMS = 100_000
MAX_JOBS = 1_000
MAX_RECIPES = 50_000
MAX_LINKS = 500_000
MAX_ING = 32
MAX_NAME = 256
MAX_QUANTITY = 1_000_000
MAX_LEVEL = 200
TABLES = {
    "ItemData": {"id", "nameId", "level"},
    "JobData": {"id", "nameId"},
    "translations": {"id", "lang", "value"},
    "RecipeData": {"id", "jobId", "resultId", "resultLevel", "quantities"},
    "RecipeData_ingredientIds_junction": {"RecipeData_id", "target_id"},
}
SOURCE_LABEL = "local-extraction"


class ExportError(Exception):
    pass


def require_int(value, field, lo=1, hi=2**63 - 1):
    if type(value) is not int or value < lo or value > hi:
        raise ExportError(f"{field}: invalid integer")
    return value


def hash_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as reader:
        for chunk in iter(lambda: reader.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def validate_version(version: str):
    if not VERSION.fullmatch(version) or any(int(x) > 65535 for x in version.split(".")):
        raise ExportError("Game version must be an explicit four-part numerical declaration")


def validate_source(path: Path, expected_sha256: str) -> tuple[int, str]:
    if path.name != "dofus.sqlite" or not path.is_file() or path.is_symlink():
        raise ExportError("Source must be an existing nonlinked dofus.sqlite file")
    if not SHA256.fullmatch(expected_sha256):
        raise ExportError("Pinned SHA256 is required; no unpinned source allowed")
    # SQLite on Windows may retain empty WAL + SHM files after a read-only
    # connection. Never read if the WAL contains transactions or the rollback
    # journal is present. A zero-length WAL is inspected again after reading.
    wal = Path(str(path) + "-wal")
    journal = Path(str(path) + "-journal")
    if journal.exists() or (wal.exists() and (wal.is_symlink() or wal.stat().st_size != 0)):
        raise ExportError("Nonempty WAL or rollback journal: cannot inspect live SQLite")
    size = path.stat().st_size
    if not 4096 <= size <= MAX_BYTES:
        raise ExportError("SQLite is outside supported size bounds")
    with path.open("rb") as f:
        if f.read(16) != b"SQLite format 3\0":
            raise ExportError("Source is not SQLite3")
    actual = hash_file(path)
    if actual.lower() != expected_sha256.lower():
        raise ExportError("Source SHA256 does not match explicitly pinned hash")
    return size, actual


def sidecar_fingerprints(path: Path):
    observed = {}
    for suffix in ("-wal", "-shm", "-journal"):
        filename = Path(str(path) + suffix)
        if filename.is_symlink():
            raise ExportError("SQLite sidecar is a link")
        if filename.exists():
            if not filename.is_file() or filename.stat().st_size > 4 * 1024 * 1024:
                raise ExportError("SQLite sidecar is irregular or excessive")
            observed[suffix] = (filename.stat().st_size, hash_file(filename))
    return observed


def open_read_only(path: Path):
    # immutable=1 prevents SQLite trying to create journals/shm sidecars.
    # It is safe ONLY after rejecting sidecars and pinning a sealed file.
    uri = "file:" + quote(path.resolve().as_posix(), safe="/:") + "?mode=ro&immutable=1"
    db = sqlite3.connect(uri, uri=True, timeout=2)
    db.execute("PRAGMA query_only=ON")
    db.execute("PRAGMA trusted_schema=OFF")
    db.execute("PRAGMA foreign_keys=OFF")  # Some extracted associations have no valid FKs.
    return db


def validate_schema(db):
    present = {name: sql for name, sql in db.execute(
        "SELECT name, sql FROM sqlite_master WHERE type='table'")}
    if len(present) > 5000:
        raise ExportError("Too many source tables")
    for table, required in TABLES.items():
        if table not in present:
            raise ExportError(f"Missing required table: {table}")
        columns = {r[1] for r in db.execute(f'PRAGMA table_info("{table}")').fetchmany(97)}
        if not required.issubset(columns):
            raise ExportError(f"Missing required columns in {table}")
        if len(columns) >= 97:
            raise ExportError(f"Excessive columns in {table}")
    definition = present["RecipeData_ingredientIds_junction"] or ""
    if "WITHOUT ROWID" in definition.upper():
        raise ExportError("Ingredient junction has no rowid ordering")
    # The rowid ordering is nevertheless an assumption, not an ordinal stored
    # in the schema. Manifest attestation is required before export.
    try:
        db.execute('SELECT rowid FROM "RecipeData_ingredientIds_junction" LIMIT 1').fetchone()
    except sqlite3.Error as ex:
        raise ExportError("Ingredient junction rowid unavailable") from ex


def optional_name(value, field):
    if value is None or value == "":
        return None
    if not isinstance(value, str) or not value.strip() or len(value) > MAX_NAME:
        raise ExportError(f"{field}: invalid translation")
    return value


def counts(db):
    result = {}
    for table in TABLES:
        result[table] = require_int(
            db.execute(f'SELECT COUNT(*) FROM "{table}"').fetchone()[0],
            table + ".count", lo=0, hi=2_000_000)
    if (result["ItemData"] > MAX_ITEMS or result["JobData"] > MAX_JOBS
            or result["RecipeData"] > MAX_RECIPES
            or result["RecipeData_ingredientIds_junction"] > MAX_LINKS):
        raise ExportError("Source exceeds catalog reader limits")
    return result


def build_catalog(db):
    """Build exact source claims; unknown yield, XP, levels and names remain null."""
    items: dict[int, dict] = {}
    for ident, level, name in db.execute(
        "SELECT i.id,i.level,t.value FROM ItemData AS i "
        "LEFT JOIN translations AS t ON t.id=CAST(i.nameId AS TEXT) AND t.lang='fr' "
        "ORDER BY i.id LIMIT ?", (MAX_ITEMS + 1,)):
        iid = require_int(ident, "ItemData.id")
        if iid in items:
            raise ExportError("Duplicate ItemData ID")
        lvl = require_int(level, "ItemData.level", lo=0, hi=MAX_LEVEL)
        items[iid] = {"id": iid, "name": optional_name(name, "ItemData.name"),
                      "level": lvl if lvl != 0 else None}
    jobs: dict[int, dict] = {}
    for ident, name in db.execute(
        "SELECT j.id,t.value FROM JobData AS j "
        "LEFT JOIN translations AS t ON t.id=CAST(j.nameId AS TEXT) AND t.lang='fr' "
        "ORDER BY j.id LIMIT ?", (MAX_JOBS + 1,)):
        jid = require_int(ident, "JobData.id", hi=2**31 - 1)
        if jid in jobs:
            raise ExportError("Duplicate JobData ID")
        jobs[jid] = {"id": jid, "name": optional_name(name, "JobData.name")}
    links: dict[str, list[int]] = defaultdict(list)
    rows_seen = 0
    for rid, ing in db.execute(
        "SELECT RecipeData_id,target_id FROM RecipeData_ingredientIds_junction "
        "ORDER BY rowid LIMIT ?", (MAX_LINKS + 1,)):
        rows_seen += 1
        if rows_seen > MAX_LINKS:
            raise ExportError("Too many ingredient links")
        if not isinstance(rid, str) or len(rid) > 128 or not rid.strip():
            raise ExportError("Ingredient relation ID must be a nonempty string/UUID")
        if len(links[rid]) >= MAX_ING:
            raise ExportError("Recipe has too many ingredients")
        links[rid].append(require_int(ing, "junction.target_id"))
    recipes = []
    seen_recipes: set[str] = set()
    extra_item_ids: set[int] = set()
    # RecipeData.resultNameId is a separate, directly sourced translation key.
    # If ItemData lacks the crafted result ID, we may retain its supplied name;
    # resultLevel MUST NOT be reused as an item level or craft XP.
    has_result_name = "resultNameId" in {
        x[1] for x in db.execute('PRAGMA table_info("RecipeData")')
    }
    name_join = (" LEFT JOIN translations AS t ON "
                 "t.id=CAST(r.resultNameId AS TEXT) AND t.lang='fr'"
                 if has_result_name else "")
    result_name_select = "t.value" if has_result_name else "NULL"
    output_names: dict[int, str] = {}
    for rid, job, result, level, quantities_json, result_name in db.execute(
        "SELECT r.id,r.jobId,r.resultId,r.resultLevel,r.quantities,"
        + result_name_select + " FROM RecipeData AS r" + name_join +
        " ORDER BY r.resultId,r.id LIMIT ?", (MAX_RECIPES + 1,)):
        if len(recipes) == MAX_RECIPES:
            raise ExportError("Too many recipes")
        if not isinstance(rid, str) or not 0 < len(rid) <= 128 or not rid.strip():
            raise ExportError("RecipeData id must be text/UUID")
        if rid in seen_recipes:
            raise ExportError("Duplicate recipe ID")
        seen_recipes.add(rid)
        job = require_int(job, "RecipeData.jobId", hi=2**31 - 1)
        result = require_int(result, "RecipeData.resultId")
        level = require_int(level, "RecipeData.resultLevel", hi=MAX_LEVEL)
        if job not in jobs:
            raise ExportError("Recipe references an absent JobData id")
        parts = links.pop(rid, None)
        if parts is None or len(parts) == 0 or len(parts) > MAX_ING:
            raise ExportError("Recipe ingredient IDs missing or unbounded")
        if len(set(parts)) != len(parts):
            raise ExportError("Duplicate ingredient ID within recipe")
        if not isinstance(quantities_json, str) or len(quantities_json) > 4096:
            raise ExportError("Recipe quantities missing/oversized")
        try:
            quantities = json.loads(quantities_json,
                                    parse_constant=lambda _: (_ for _ in ()).throw(ValueError()))
        except (ValueError, TypeError) as ex:
            raise ExportError("Invalid RecipeData quantities JSON") from ex
        if not isinstance(quantities, list) or len(parts) != len(quantities):
            raise ExportError("Ingredient/quantity lengths differ")
        ingredients = []
        for iid, qty in zip(parts, quantities):
            ingredients.append({"itemId": iid, "quantity": require_int(
                qty, "RecipeData.quantity", hi=MAX_QUANTITY)})
            extra_item_ids.add(iid)
        extra_item_ids.add(result)
        if result not in items:
            translated = optional_name(result_name, "RecipeData.resultNameId translation")
            if translated is not None:
                if result in output_names and output_names[result] != translated:
                    raise ExportError("Conflicting direct output names for resultId")
                output_names[result] = translated
        recipes.append({
            "id": rid, "jobId": job, "resultItemId": result,
            "requiredLevel": level,
            "resultQuantity": None,
            "experiencePerCraft": None,
            "ingredients": ingredients,
        })
    if links:
        raise ExportError("Orphaned junction rows not corresponding to recipes")
    missing_item_ids = extra_item_ids - items.keys()
    for iid in sorted(missing_item_ids):
        if len(items) == MAX_ITEMS:
            raise ExportError("Placeholder item limit exceeded")
        items[iid] = {"id": iid, "name": output_names.get(iid), "level": None}
    return {
        "schemaVersion": 1,
        "items": sorted(items.values(), key=lambda x: x["id"]),
        "jobs": sorted(jobs.values(), key=lambda x: x["id"]),
        "recipes": recipes,
        "levelThresholds": [],  # No verified profession XP table/recipe XP field.
    }, {
        "recipes": len(recipes), "ingredientLinks": rows_seen,
        "itemsIncludingUnknownMetadata": len(items), "jobs": len(jobs),
        "itemsMissingSourceMetadata": len(missing_item_ids),
        "resultNamesFromRecipeDataTranslation": len(missing_item_ids & output_names.keys()),
        "noSourceOutputQuantity": len(recipes),
        "noSourceCraftExperience": len(recipes),
        "noVerifiedProfessionXpThresholds": True,
        "junctionQuantityAssociation": "ROWID_ORDER_ASSUMPTION_REQUIRES_OPERATOR_ATTESTATION",
    }


def verify_unchanged(path, original_bytes, original_hash):
    wal = Path(str(path) + "-wal")
    journal = Path(str(path) + "-journal")
    if journal.exists() or (wal.exists() and wal.stat().st_size != 0):
        raise ExportError("SQLite sidecars gained uncommitted data during read")
    if path.stat().st_size != original_bytes or hash_file(path) != original_hash:
        raise ExportError("Source mutated during read; discard results")


def inspect_source(path: Path, expected_sha: str):
    size, sha = validate_source(path, expected_sha)
    sidecars = sidecar_fingerprints(path)
    # sqlite3.Connection's context manager does not close its Windows file
    # handle. closing() is required for timely release before hash verification.
    with closing(open_read_only(path)) as db:
        validate_schema(db)
        source_counts = counts(db)
        catalog, analysis = build_catalog(db)
    verify_unchanged(path, size, sha)
    if sidecar_fingerprints(path) != sidecars:
        raise ExportError("SQLite sidecars changed during read; discard results")
    return catalog, source_counts, analysis, size, sha


def json_bytes(obj):
    data = (json.dumps(obj, ensure_ascii=False, sort_keys=True, separators=(",", ":"),
                       allow_nan=False) + "\n").encode("utf-8")
    if not 2 <= len(data) <= MAX_JSON:
        raise ExportError("recipes.json exceeds catalog reader bounds")
    return data


def write_new(path: Path, data: bytes):
    with path.open("xb") as stream:
        stream.write(data)
        stream.flush()
        os.fsync(stream.fileno())


def preview(path: Path, expected_sha: str, output_dir: Path):
    """Emit actual local recipe rows without manufacturing ownership/version."""
    if output_dir.is_symlink() or (output_dir.exists() and
                                   (not output_dir.is_dir() or any(output_dir.iterdir()))):
        raise ExportError("Preview directory must be empty")
    catalog, counts_from_source, stats, size, source_sha = inspect_source(path, expected_sha)
    data = json_bytes(catalog)
    review = {
        "schemaVersion": 1, "status": "REVIEW_ONLY_NO_ADMISSIBLE_MANIFEST",
        "source": "local-extraction", "sourceSha256": source_sha,
        "sourceBytes": size, "sourceCounts": counts_from_source,
        "exportCounts": stats, "gameVersion": "UNKNOWN",
        "permission": "UNVERIFIED", "junctionOrder": "ASSUMED_ROWID_NOT_ATTESTED",
        "recipesFile": "recipes.json", "recipesSha256": hashlib.sha256(data).hexdigest(),
        "recipesBytes": len(data), "allowedForAiOrMcp": False,
        "verifiedCraftXp": False, "verifiedCraftYield": False,
        "publishedForRuntime": False,
    }
    output_dir.mkdir(parents=True, exist_ok=True)
    write_new(output_dir / "recipes.json", data)
    write_new(output_dir / "manifest.review.json", json_bytes(review))
    return {"status": review["status"], "gameVersion": "UNKNOWN",
            "outputDir": str(output_dir), "counts": stats,
            "recipesSha256": review["recipesSha256"], "allowedForAiOrMcp": False}


def export(path: Path, expected_sha: str, output_dir: Path, game_version: str,
           *, source_attested: bool, rowid_attested: bool, version_acknowledged: bool):
    validate_version(game_version)
    if not source_attested:
        raise ExportError("Explicit --attest-source-owned permission required for owner-authorized manifest")
    if not rowid_attested:
        raise ExportError("Explicit --attest-junction-rowid-order required for mapping quantities to IDs")
    if not version_acknowledged:
        raise ExportError("--acknowledge-version-unverified required; SQLite game version is not proven")
    # Check before reading the 380MB source: no silent overwriting or filename ambiguity.
    if output_dir.is_symlink() or (output_dir.exists() and
                                   (not output_dir.is_dir() or any(output_dir.iterdir()))):
        raise ExportError("Output directory is not empty: existing manifests cannot be overwritten")
    catalog, source_counts, stats, source_size, source_hash = inspect_source(path, expected_sha)
    if any(r["resultQuantity"] is not None or r["experiencePerCraft"] is not None
           for r in catalog["recipes"]):
        raise ExportError("XP/yield must remain UNKNOWN from this source")
    recipe_bytes = json_bytes(catalog)
    manifest = {
        "schemaVersion": 1, "gameVersion": game_version,
        "source": SOURCE_LABEL, "permission": "owner-authorized",
        "format": "json", "asset": {
            "file": "recipes.json", "bytes": len(recipe_bytes),
            "sha256": hashlib.sha256(recipe_bytes).hexdigest(),
        },
    }
    provenance = {
        "schemaVersion": 1,
        "source": SOURCE_LABEL,
        "sourceName": "dofus.sqlite",
        "sourceSha256": source_hash, "sourceBytes": source_size,
        "sourceCounts": source_counts,
        "exportCounts": stats,
        "gameVersion": game_version, "gameVersionVerifiedAgainstClient": False,
        "sourceRights": "EXPLICIT_OPERATOR_ATTESTATION_NOT_INDEPENDENTLY_VERIFIED",
        "quantityMapping": "JUNCTION_ROWID_ORDER_EXPLICITLY_ATTESTED_NO_ORDINAL_IN_SCHEMA",
        "publisherVerified": False, "allowedForAiOrMcp": False,
        "runtimeVerified": False, "verifiedCraftXp": False, "verifiedCraftYield": False,
        "exportedAtUtc": datetime.now(timezone.utc).isoformat(),
    }
    output_dir.mkdir(parents=True, exist_ok=True)
    write_new(output_dir / "recipes.json", recipe_bytes)
    write_new(output_dir / "provenance.review.json", json_bytes(provenance))
    # Emit manifest last: a partial export is not an admissible catalog.
    write_new(output_dir / "manifest.json", json_bytes(manifest))
    return {"status": "LOCAL_REVIEW_ONLY", "outputDir": str(output_dir),
            "counts": stats, "fileSha256": manifest["asset"]["sha256"],
            "allowedForAiOrMcp": False}


def main(argv=None):
    p = argparse.ArgumentParser(description="Offline source-owned recipe export (no API/network)")
    sub = p.add_subparsers(dest="command", required=True)
    for name in ("inspect", "preview", "export"):
        sp = sub.add_parser(name)
        sp.add_argument("--sqlite", type=Path, required=True)
        sp.add_argument("--expected-sha256", required=True)
        if name in ("preview", "export"):
            sp.add_argument("--output-dir", type=Path, required=True)
        if name == "export":
            sp.add_argument("--game-version", required=True,
                            help="Operator-declared; not certified by the SQLite file")
            sp.add_argument("--attest-source-owned", action="store_true")
            sp.add_argument("--attest-junction-rowid-order", action="store_true")
            sp.add_argument("--acknowledge-version-unverified", action="store_true")
    args = p.parse_args(argv)
    try:
        if args.command == "inspect":
            _catalog, source_counts, stats, size, sha = inspect_source(
                args.sqlite, args.expected_sha256)
            output = {"status": "SOURCE_INSPECTED_NO_MANIFEST", "sourceSha256": sha,
                      "sourceBytes": size, "sourceCounts": source_counts,
                      "counts": stats, "eligibleForAiOrMcp": False,
                      "gameVersion": "UNKNOWN"}
        elif args.command == "preview":
            output = preview(args.sqlite, args.expected_sha256, args.output_dir)
        else:
            output = export(args.sqlite, args.expected_sha256, args.output_dir,
                            args.game_version, source_attested=args.attest_source_owned,
                            rowid_attested=args.attest_junction_rowid_order,
                            version_acknowledged=args.acknowledge_version_unverified)
        print(json.dumps(output, ensure_ascii=False, sort_keys=True))
        return 0
    except (ExportError, OSError, sqlite3.Error, UnicodeError, OverflowError) as ex:
        print(f"BLOCKED: {ex}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
