# /// script
# requires-python = ">=3.12"
# dependencies = []
# ///
# Run: uv run tools/Generate-ItemOverlay.py DATABASE I18N_JSON VERSION OUTPUT_DIRECTORY [RELEASE_JSON]
"""Export read-only SQLite data; the fifth TSV column counts distinct recipes per ingredient."""
from __future__ import annotations

import hashlib
import json
import sqlite3
import sys
from contextlib import closing
from pathlib import Path
from typing import Final

TARGET_VERSION: Final = "3.7.11.11"


def clean(text: str) -> str:
    return " ".join(text.split()).replace("\t", " ")


def digest(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def verify_release(database: Path, translations: Path, release_file: Path | None) -> bool:
    if release_file is None:
        return False
    release = json.loads(release_file.read_text(encoding="utf-8-sig"))
    url = "https://github.com/ledouxm/dofus-sqlite/releases/tag/v6.0_3.7.11.11"
    if release.get("html_url") != url or release.get("tag_name") != "v6.0_3.7.11.11" or release.get("draft") or release.get("prerelease"):
        raise ValueError("Unexpected release identity or unpublished release")
    for path in (database, translations):
        candidates = [asset for asset in release["assets"] if asset["name"] == path.name]
        if len(candidates) != 1:
            raise ValueError(f"Asset not uniquely listed in release: {path.name}")
        asset = candidates[0]
        if asset["size"] != path.stat().st_size or asset["digest"] != "sha256:" + digest(path):
            raise ValueError(f"Release asset size/digest mismatch: {path.name}")
    return True


def generate(database: Path, translations: Path, version: str, output: Path, release_file: Path | None = None) -> int:
    if version != TARGET_VERSION:
        raise ValueError(f"Expected {TARGET_VERSION}; refusing a differently labelled dataset")
    database = database.resolve(strict=True)
    translations = translations.resolve(strict=True)
    verified = verify_release(database, translations, release_file)
    localized = json.loads(translations.read_text(encoding="utf-8-sig"))
    texts = localized.get("texts", localized)
    if not isinstance(texts, dict) or not all(isinstance(k, str) and isinstance(v, str) for k, v in texts.items()):
        raise ValueError("Expected a French i18n JSON object mapping text IDs to strings")
    with closing(sqlite3.connect(database.as_uri() + "?mode=ro&immutable=1", uri=True)) as connection:
        connection.execute("PRAGMA query_only=ON")
        counts = dict(connection.execute('''
            SELECT CAST(j.target_id AS INTEGER), COUNT(DISTINCT r.id)
            FROM RecipeData_ingredientIds_junction j
            JOIN RecipeData r ON r.id = j.RecipeData_id
            GROUP BY CAST(j.target_id AS INTEGER)
        '''))
        rows = connection.execute('''
            SELECT i.id, i.level, i.nameId, t.nameId
            FROM ItemData i LEFT JOIN ItemTypeData t ON t.id = i.typeId
            ORDER BY i.id
        ''').fetchall()
    lines: list[str] = []
    excluded: list[int] = []
    for gid, level, name_id, type_name_id in rows:
        if type_name_id is None:
            excluded.append(gid)
            continue
        name = clean(texts.get(str(name_id), ""))
        kind = clean(texts.get(str(type_name_id), ""))
        if not name or not kind:
            raise ValueError(f"Missing French translation for item {gid}")
        if gid <= 0 or level < 0:
            raise ValueError(f"Invalid item ID/level: {gid}")
        lines.append(f"{name}\t{level}\t{kind}\t{gid}\t{counts.get(gid, 0)}\n")
    if not lines:
        raise ValueError("Empty item dataset")
    output.mkdir(parents=True, exist_ok=True)
    table = "".join(lines).encode("utf-8")
    manifest = {
        "declaredVersion": version,
        "versionVerified": verified,
        "versionEvidence": "GitHub release identity and asset SHA256/size verified" if verified else "Unverified operator version declaration",
        "releaseUrl": "https://github.com/ledouxm/dofus-sqlite/releases/tag/v6.0_3.7.11.11" if verified else None,
        "releaseMetadataSha256": digest(release_file) if release_file else None,
        "excludedMissingItemTypeIds": excluded,
        "sourceItemCount": len(rows),
        "sqliteSha256": digest(database),
        "i18nSha256": digest(translations),
        "itemsSha256": hashlib.sha256(table).hexdigest(),
        "itemCount": len(lines),
        "columns": ["name", "level", "type", "gid", "recipeCount"],
        "recipeCount": "COUNT(DISTINCT RecipeData.id) per ingredient; zero is known zero",
        "pricesIncluded": False,
    }
    (output / "items.tsv").write_bytes(table)
    (output / "items.provenance.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return len(lines)


if __name__ == "__main__":
    if len(sys.argv) not in (5, 6):
        raise SystemExit("Usage: Generate-ItemOverlay.py DATABASE I18N_JSON 3.7.11.11 OUTPUT_DIRECTORY [RELEASE_JSON]")
    print(f"Exported {generate(Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3], Path(sys.argv[4]), Path(sys.argv[5]) if len(sys.argv) == 6 else None)} items")
