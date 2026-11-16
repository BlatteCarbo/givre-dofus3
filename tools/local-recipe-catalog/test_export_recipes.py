"""Synthetic-only read-only importer tests. No public API or gameplay."""
import hashlib
import importlib.util
import json
from pathlib import Path
import sqlite3
import sys
import tempfile
import unittest


SPEC = importlib.util.spec_from_file_location(
    "local_recipe_exporter", Path(__file__).with_name("export_recipes.py"))
module = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = module
SPEC.loader.exec_module(module)


def fixture(root, *, mismatch=False, orphan=False, missing_item=False,
            invalid_qty=False, duplicate_qty=False, rowid_reversed=False):
    source = Path(root) / "dofus.sqlite"
    db = sqlite3.connect(source)
    db.executescript("""
        CREATE TABLE ItemData(id INTEGER PRIMARY KEY,nameId INTEGER,level INTEGER);
        CREATE TABLE JobData(id INTEGER PRIMARY KEY,nameId INTEGER);
        CREATE TABLE translations(id TEXT,lang TEXT,value TEXT,PRIMARY KEY(id,lang));
        CREATE TABLE RecipeData(id TEXT PRIMARY KEY,jobId INTEGER,quantities TEXT,
            resultId INTEGER,resultLevel INTEGER);
        CREATE TABLE RecipeData_ingredientIds_junction(RecipeData_id INTEGER,target_id INTEGER,
            PRIMARY KEY (RecipeData_id,target_id));
        INSERT INTO ItemData VALUES(101,1,0),(102,2,5),(103,3,25);
        INSERT INTO JobData VALUES(13,4);
        INSERT INTO translations VALUES('1','fr','Plume fictive'),
            ('2','fr','Huile fictive'),('3','fr','Bague fictive'),
            ('4','fr','Métier fictif');
        INSERT INTO RecipeData VALUES('fake-uuid-1',13,'[20,3]',103,25);
    """)
    if missing_item:
        db.execute("DELETE FROM ItemData WHERE id=102")
    if invalid_qty:
        db.execute("UPDATE RecipeData SET quantities='[0,3]'")
    if duplicate_qty:
        db.execute("UPDATE RecipeData SET quantities='[20,20]'")
    relations = [102, 101] if rowid_reversed else [101, 102]
    for ingredient in relations[:1 if mismatch else 2]:
        db.execute("INSERT INTO RecipeData_ingredientIds_junction VALUES(?,?)",
                   ("fake-uuid-1", ingredient))
    if orphan:
        db.execute("INSERT INTO RecipeData_ingredientIds_junction VALUES(?,?)",
                   ("unused-uuid-2", 101))
    db.commit()
    db.close()
    return source, module.hash_file(source)


def export_fixture(source, digest, out, **kwargs):
    arguments = {
        "source_attested": True, "rowid_attested": True,
        "version_acknowledged": True,
    }
    arguments.update(kwargs)
    return module.export(source, digest, out, "3.7.12.12", **arguments)


class ExportContractTests(unittest.TestCase):
    def test_preview_real_payload_requires_no_fake_owner_manifest(self):
        with tempfile.TemporaryDirectory() as root:
            source, digest = fixture(root)
            output = Path(root) / "review"
            result = module.preview(source, digest, output)
            self.assertEqual(result["status"], "REVIEW_ONLY_NO_ADMISSIBLE_MANIFEST")
            self.assertEqual(result["gameVersion"], "UNKNOWN")
            self.assertFalse((output / "manifest.json").exists())
            self.assertTrue((output / "recipes.json").exists())
            self.assertTrue((output / "manifest.review.json").exists())
            self.assertFalse(json.loads(
                (output / "manifest.review.json").read_text())["allowedForAiOrMcp"])

    def test_realistic_json_manifest_matches_reader_contract_without_inventing_xp(self):
        with tempfile.TemporaryDirectory() as root:
            src, digest = fixture(root)
            before = src.read_bytes()
            output = Path(root) / "export"
            result = export_fixture(src, digest, output)
            self.assertEqual(result["status"], "LOCAL_REVIEW_ONLY")
            self.assertFalse(result["allowedForAiOrMcp"])
            meta = json.loads((output / "manifest.json").read_text("utf-8"))
            self.assertEqual(set(meta), {"schemaVersion", "gameVersion", "source", "permission", "format", "asset"})
            self.assertEqual(meta["source"], "local-extraction")
            self.assertEqual(meta["permission"], "owner-authorized")
            self.assertEqual(meta["format"], "json")
            self.assertEqual(meta["asset"]["file"], "recipes.json")
            raw = (output / "recipes.json").read_bytes()
            self.assertEqual(meta["asset"]["bytes"], len(raw))
            self.assertEqual(meta["asset"]["sha256"], hashlib.sha256(raw).hexdigest())
            rec = json.loads(raw)
            self.assertEqual(set(rec), {"schemaVersion", "items", "jobs", "recipes", "levelThresholds"})
            self.assertEqual(rec["levelThresholds"], [])
            self.assertEqual(rec["items"][0]["level"], None)  # source item level 0 is unknown
            self.assertEqual(rec["jobs"][0]["name"], "Métier fictif")
            recipe = rec["recipes"][0]
            self.assertEqual(recipe["id"], "fake-uuid-1")
            self.assertEqual(recipe["requiredLevel"], 25)
            self.assertEqual(recipe["ingredients"], [
                {"itemId": 101, "quantity": 20}, {"itemId": 102, "quantity": 3}])
            self.assertIsNone(recipe["resultQuantity"])
            self.assertIsNone(recipe["experiencePerCraft"])
            self.assertFalse(json.loads((output / "provenance.review.json").read_text())["allowedForAiOrMcp"])
            self.assertEqual(src.read_bytes(), before)
            self.assertFalse(Path(str(src) + "-wal").exists())
            self.assertFalse(Path(str(src) + "-shm").exists())
            with self.assertRaises(module.ExportError):
                export_fixture(src, digest, output)

    def test_missing_metadata_remains_null_and_reference_preserved(self):
        with tempfile.TemporaryDirectory() as root:
            src, digest = fixture(root, missing_item=True)
            result = export_fixture(src, digest, Path(root) / "export")
            catalog = json.loads((Path(root) / "export" / "recipes.json").read_text("utf-8"))
            self.assertEqual(result["counts"]["itemsMissingSourceMetadata"], 1)
            unknown = next(x for x in catalog["items"] if x["id"] == 102)
            self.assertEqual(unknown, {"id": 102, "name": None, "level": None})
            self.assertEqual(catalog["recipes"][0]["ingredients"][1]["itemId"], 102)

    def test_missing_output_item_gets_only_explicit_recipe_result_translation(self):
        with tempfile.TemporaryDirectory() as root:
            source, _ = fixture(root)
            db = sqlite3.connect(source)
            db.execute("ALTER TABLE RecipeData ADD COLUMN resultNameId TEXT")
            db.execute("UPDATE RecipeData SET resultId=500,resultNameId='99'")
            db.execute("INSERT INTO translations VALUES('99','fr','Produit attesté synthétique')")
            db.commit()
            db.close()
            digest = module.hash_file(source)
            out = Path(root) / "export"
            stats = export_fixture(source, digest, out)
            result = json.loads((out / "recipes.json").read_text("utf-8"))
            produced = next(i for i in result["items"] if i["id"] == 500)
            self.assertEqual(produced, {
                "id": 500, "name": "Produit attesté synthétique", "level": None})
            self.assertEqual(stats["counts"]["resultNamesFromRecipeDataTranslation"], 1)

    def test_without_explicit_attestations_fail_before_writing_manifest(self):
        with tempfile.TemporaryDirectory() as root:
            src, digest = fixture(root)
            output = Path(root) / "export"
            scenarios = (
                {"source_attested": False}, {"rowid_attested": False},
                {"version_acknowledged": False})
            for flags in scenarios:
                with self.subTest(flags=flags), self.assertRaises(module.ExportError):
                    export_fixture(src, digest, output, **flags)
                self.assertFalse(output.exists())

    def test_wrong_sha_and_source_wrong_name_and_false_version(self):
        with tempfile.TemporaryDirectory() as root:
            src, digest = fixture(root)
            with self.assertRaises(module.ExportError):
                module.inspect_source(src, "a" * 64)
            with self.assertRaises(module.ExportError):
                module.validate_version("3.7.12")
            with self.assertRaises(module.ExportError):
                module.validate_version("3.7.70000.12")
            other = Path(root) / "api-data.sqlite"
            other.write_bytes(src.read_bytes())
            with self.assertRaises(module.ExportError):
                module.inspect_source(other, digest)

    def test_qty_mismatch_orphan_or_zero_rejects_all(self):
        for variant in ("mismatch", "orphan", "invalid_qty"):
            with self.subTest(variant=variant), tempfile.TemporaryDirectory() as root:
                src, digest = fixture(root, **{variant: True})
                output = Path(root) / "export"
                with self.assertRaises(module.ExportError):
                    export_fixture(src, digest, output)
                self.assertFalse((output / "manifest.json").exists())

    def test_rowid_alignment_is_only_an_attested_assumption(self):
        with tempfile.TemporaryDirectory() as root:
            src, digest = fixture(root, rowid_reversed=True)
            out = Path(root) / "export"
            with self.assertRaises(module.ExportError):
                export_fixture(src, digest, out, rowid_attested=False)
            # When the operator attests the rowid pairing, the output contains
            # precisely that pairing; no inference from names or game facts.
            export_fixture(src, digest, out)
            doc = json.loads((out / "recipes.json").read_text())
            self.assertEqual(doc["recipes"][0]["ingredients"][0],
                             {"itemId": 102, "quantity": 20})
            self.assertEqual(
                json.loads((out / "provenance.review.json").read_text())["quantityMapping"],
                "JUNCTION_ROWID_ORDER_EXPLICITLY_ATTESTED_NO_ORDINAL_IN_SCHEMA")

    def test_refuses_nonempty_wal(self):
        with tempfile.TemporaryDirectory() as root:
            src, digest = fixture(root)
            wal = Path(str(src) + "-wal")
            wal.write_bytes(b"pending changes")
            with self.assertRaises(module.ExportError):
                module.inspect_source(src, digest)

    def test_empty_wal_does_not_modify_source(self):
        with tempfile.TemporaryDirectory() as root:
            src, digest = fixture(root)
            wal = Path(str(src) + "-wal")
            shm = Path(str(src) + "-shm")
            wal.write_bytes(b"")
            shm.write_bytes(b"fake leftover read-only shared memory")
            before = src.read_bytes()
            catalog, _counts, _stats, _size, _hash = module.inspect_source(src, digest)
            self.assertEqual(len(catalog["recipes"]), 1)
            self.assertEqual(src.read_bytes(), before)
            self.assertEqual(wal.read_bytes(), b"")
            self.assertEqual(shm.read_bytes(), b"fake leftover read-only shared memory")


if __name__ == "__main__":
    unittest.main()
