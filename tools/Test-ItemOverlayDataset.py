# /// script
# requires-python = ">=3.12"
# dependencies = []
# ///
# Run: uv run tools/Test-ItemOverlayDataset.py
from __future__ import annotations

import importlib.util
import json
import sqlite3
import sys
from contextlib import closing
from pathlib import Path
from tempfile import TemporaryDirectory

sys.dont_write_bytecode = True

spec = importlib.util.spec_from_file_location("overlay_generator", Path(__file__).with_name("Generate-ItemOverlay.py"))
assert spec is not None and spec.loader is not None
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

with TemporaryDirectory(prefix="item-overlay-") as root:
    directory = Path(root)
    db = directory / "dofus.sqlite"
    with closing(sqlite3.connect(db)) as connection:
        connection.executescript('''
            CREATE TABLE ItemData(id INTEGER, level INTEGER, nameId INTEGER, typeId INTEGER);
            CREATE TABLE ItemTypeData(id INTEGER, nameId INTEGER);
            CREATE TABLE RecipeData(id TEXT);
            CREATE TABLE RecipeData_ingredientIds_junction(RecipeData_id TEXT, target_id TEXT);
            INSERT INTO ItemData VALUES(1, 10, 100, 7), (2, 20, 101, 7), (3, 1, 100, 999);
            INSERT INTO ItemTypeData VALUES(7, 200);
            INSERT INTO RecipeData VALUES('a'), ('b');
            INSERT INTO RecipeData_ingredientIds_junction VALUES('a','1'),('a','1'),('b','1'),('orphan','1');
        ''')
        connection.commit()
    translations = directory / "fr.json"
    translations.write_text(json.dumps({"texts": {"100": "Bois\nclair", "101": "Pierre", "200": "Ressource"}}), encoding="utf-8")
    output = directory / "out"
    assert module.generate(db, translations, "3.7.11.11", output) == 2
    expected = "Bois clair\t10\tRessource\t1\t2\nPierre\t20\tRessource\t2\t0\n"
    assert (output / "items.tsv").read_text(encoding="utf-8") == expected
    first = (output / "items.tsv").read_bytes()
    module.generate(db, translations, "3.7.11.11", output)
    assert first == (output / "items.tsv").read_bytes()
    manifest = json.loads((output / "items.provenance.json").read_text())
    assert manifest["versionVerified"] is False and manifest["pricesIncluded"] is False
    assert manifest["excludedMissingItemTypeIds"] == [3]
    release_file = directory / "release.json"
    release = {
        "html_url": "https://github.com/ledouxm/dofus-sqlite/releases/tag/v6.0_3.7.11.11",
        "tag_name": "v6.0_3.7.11.11", "draft": False, "prerelease": False,
        "assets": [{"name": p.name, "size": p.stat().st_size, "digest": "sha256:" + module.digest(p)} for p in (db, translations)],
    }
    release_file.write_text(json.dumps(release), encoding="utf-8")
    assert module.generate(db, translations, "3.7.11.11", output, release_file) == 2
    assert json.loads((output / "items.provenance.json").read_text())["versionVerified"] is True
    release["assets"][0]["digest"] = "sha256:" + "0" * 64
    release_file.write_text(json.dumps(release), encoding="utf-8")
    try:
        module.generate(db, translations, "3.7.11.11", output, release_file)
    except ValueError:
        pass
    else:
        raise AssertionError("Tampered release digest was accepted")
    try:
        module.generate(db, translations, "3.7.7.6", output)
    except ValueError:
        pass
    else:
        raise AssertionError("Old version was accepted")
    translations.write_text('{}')
    try:
        module.generate(db, translations, "3.7.11.11", output)
    except ValueError:
        pass
    else:
        raise AssertionError("Missing translation was accepted")
    assert first == (output / "items.tsv").read_bytes()
print("PASS: distinct recipes, orphan exclusion, known zero, Unicode-safe TSV, deterministic export, provenance, version rejection, validation before writes, missing-type exclusion, release digest verification/tamper rejection")
