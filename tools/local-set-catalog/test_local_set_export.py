"""Synthetic offline unit tests for the independently gated SQLite exporter."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import shutil
import sqlite3
import subprocess
import sys
import tempfile
import unittest

from local_set_export import (
    ExportError, audit_sqlite, export_catalog, sha256_file, valid_version,
)


class LocalSetExportTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory(prefix="givre-local-set-test-")
        self.addCleanup(self.temporary.cleanup)
        self.folder = Path(self.temporary.name)
        self.db_path = self.folder / "fictional-game.sqlite"
        database = sqlite3.connect(self.db_path)
        try:
            database.executescript("""
                CREATE TABLE ItemSetData(id INTEGER PRIMARY KEY,items TEXT,nameId INTEGER);
                CREATE TABLE ItemData(id INTEGER PRIMARY KEY,itemSetId INTEGER,nameId INTEGER);
                CREATE TABLE translations(id TEXT, lang TEXT,value TEXT);
                INSERT INTO ItemSetData VALUES
                    (7,'[11,12]',71),(8,'[20]',81);
                INSERT INTO ItemData VALUES
                    (11,7,111),(12,7,112),(20,8,120),(99,0,199);
                INSERT INTO translations VALUES
                    ('71','fr','Panoplie FICTIVE A'),
                    ('81','fr','Panoplie FICTIVE B'),
                    ('111','fr','Coiffe FICTIVE'),
                    ('112','fr','Cape FICTIVE'),
                    ('120','fr','Bague FICTIVE'),
                    ('199','fr','Ressource FICTIVE');
            """)
            database.commit()
        finally:
            database.close()
        self.output = self.folder / "new-catalog"

    def update(self, sql: str) -> None:
        database = sqlite3.connect(self.db_path)
        try:
            database.executescript(sql)
            database.commit()
        finally:
            database.close()

    def export(self, audit=None, *, allow_partial=False, **kwargs) -> dict:
        record = audit if audit is not None else audit_sqlite(self.db_path)
        return export_catalog(
            record, self.output, source_owned=True,
            rights_evidence="unit-test-source-owner-assertion",
            game_version="3.7.12.12",
            version_evidence="unit-test-version-declaration",
            expected_source_sha256=sha256_file(self.db_path),
            allow_partial=allow_partial,
            **kwargs
        )

    def test_strict_round_trip_matches_readers_schema_and_preserves_ids(self):
        source_sha = sha256_file(self.db_path)
        audit = audit_sqlite(self.db_path)
        self.assertEqual(2, len(audit.sets))
        self.assertEqual(3, len(audit.items))
        self.assertEqual(4, audit.source_item_count)
        result = self.export(audit)
        self.assertEqual(2, result["setCount"])
        self.assertFalse(result["aiDisclosureAuthorized"])
        sets = json.loads((self.output / "equipment-sets.json").read_text("utf-8"))
        manifest = json.loads((self.output / "manifest.json").read_text("utf-8"))
        prov = json.loads((self.output / "provenance.json").read_text("utf-8"))
        self.assertEqual({"schemaVersion", "sets", "items"}, set(sets))
        self.assertEqual({"schemaVersion", "gameVersion", "source", "permission",
                          "format", "asset"}, set(manifest))
        self.assertEqual([11, 12], sets["sets"][0]["memberItemIds"])
        self.assertEqual("Panoplie FICTIVE A", sets["sets"][0]["name"])
        self.assertEqual([11, 12, 20], [x["id"] for x in sets["items"]])
        self.assertEqual([7, 7, 8], [x["setId"] for x in sets["items"]])
        self.assertEqual(source_sha, prov["sourceSha256"])
        self.assertFalse(prov["rightsIndependentlyVerified"])
        self.assertFalse(prov["gameVersionIndependentlyVerified"])
        self.assertFalse(prov["publisherVerified"])
        self.assertFalse(prov["aiDisclosureAuthorized"])
        payload = (self.output / "equipment-sets.json").read_bytes()
        self.assertEqual(len(payload), manifest["asset"]["bytes"])
        self.assertEqual(hashlib.sha256(payload).hexdigest(), manifest["asset"]["sha256"])
        self.assertEqual(source_sha, sha256_file(self.db_path))
        self.assertFalse(Path(str(self.db_path) + "-wal").exists())
        self.assertFalse(Path(str(self.db_path) + "-shm").exists())

    @unittest.skipUnless(shutil.which("dotnet"), "dotnet SDK not installed")
    def test_export_is_accepted_by_actual_csharp_equipment_set_catalog_reader(self):
        self.export()
        project = Path(__file__).with_name("ReaderSmoke.csproj")
        result = subprocess.run(
            ["dotnet", "run", "--project", str(project), "--configuration", "Release",
             "--", str(self.output), "3.7.12.12"],
            capture_output=True, text=True, check=False, encoding="utf-8",
            timeout=120
        )
        self.assertEqual(0, result.returncode, result.stderr + result.stdout)
        report = json.loads(result.stdout.strip().splitlines()[-1])
        self.assertTrue(report["admitted"])
        self.assertEqual(2, report["sets"])
        self.assertEqual(3, report["items"])
        self.assertEqual([11, 12, 20], report["memberIds"])
        self.assertFalse(report["publisherVerified"])
        self.assertFalse(report["aiDisclosureAuthorized"])

    def test_export_requires_explicit_source_owned_assertion(self):
        audit = audit_sqlite(self.db_path)
        with self.assertRaisesRegex(ExportError, "source-owned"):
            export_catalog(
                audit, self.output, source_owned=False,
                rights_evidence="synthetic-test-owner",
                game_version="3.7.12.12", version_evidence="synthetic-test-version",
                expected_source_sha256=sha256_file(self.db_path)
            )
        self.assertFalse(self.output.exists())

    def test_rejects_missing_or_placeholder_rights_evidence(self):
        for value in (None, "", "unknown", "n/a", "test", "x\x00secret"):
            with self.subTest(value=value), self.assertRaises(ExportError):
                export_catalog(
                    audit_sqlite(self.db_path), self.output, source_owned=True,
                    rights_evidence=value, game_version="3.7.12.12",
                    version_evidence="synthetic-test-version",
                    expected_source_sha256=sha256_file(self.db_path)
                )
        self.assertFalse(self.output.exists())

    def test_version_must_be_explicit_well_formed_and_supported(self):
        self.assertTrue(valid_version("3.7.12.12"))
        for version in ("3.7", "3.7.12", "3.7.12.65536", "v3.7.12.12",
                        "3.7.12.12-extra", "3.7.12.-1", ""):
            self.assertFalse(valid_version(version))
            with self.subTest(version=version), self.assertRaises(ExportError):
                export_catalog(
                    audit_sqlite(self.db_path), self.output, source_owned=True,
                    rights_evidence="synthetic-test-owner",
                    game_version=version, version_evidence="synthetic-test-version",
                    expected_source_sha256=sha256_file(self.db_path)
                )
        self.assertFalse(self.output.exists())

    def test_rejects_wrong_source_sha256_pin_without_output(self):
        with self.assertRaisesRegex(ExportError, "SHA-256"):
            export_catalog(
                audit_sqlite(self.db_path), self.output, source_owned=True,
                rights_evidence="synthetic-test-owner", game_version="3.7.12.12",
                version_evidence="synthetic-test-version",
                expected_source_sha256="0" * 64
            )
        self.assertFalse(self.output.exists())

    def test_mutating_database_after_audit_rejects_export(self):
        audit = audit_sqlite(self.db_path)
        self.update("UPDATE ItemData SET nameId=444 WHERE id=11;")
        with self.assertRaisesRegex(ExportError, "changed"):
            export_catalog(
                audit, self.output, source_owned=True,
                rights_evidence="synthetic-test-owner", game_version="3.7.12.12",
                version_evidence="synthetic-test-version",
                expected_source_sha256=sha256_file(self.db_path)
            )
        self.assertFalse(self.output.exists())

    def test_missing_source_item_disqualifies_its_set_in_strict_mode(self):
        self.update("DELETE FROM ItemData WHERE id=12;")
        audit = audit_sqlite(self.db_path)
        self.assertEqual(1, audit.missing_member_references)
        self.assertEqual([7], [item["id"] for item in audit.rejected_sets])
        self.assertEqual("UNKNOWN_METADATA", audit.rejected_sets[0]["viewStatus"])
        self.assertFalse(audit.rejected_sets[0]["catalogAdmitted"])
        self.assertEqual([11, 12], audit.rejected_sets[0]["observedMemberItemIds"])
        with self.assertRaisesRegex(ExportError, "inconsistent sets"):
            self.export(audit)
        self.assertFalse(self.output.exists())
        result = self.export(audit, allow_partial=True)
        self.assertEqual(1, result["setCount"])
        self.assertEqual(1, result["excludedSetCount"])
        self.assertEqual([8],
                         [s["id"] for s in json.loads(
                             (self.output / "equipment-sets.json").read_text("utf-8"))["sets"]])
        report = json.loads((self.output / "provenance.json").read_text("utf-8"))
        advisory = report["excludedSetObservations"][0]
        self.assertEqual("UNKNOWN_METADATA", advisory["viewStatus"])
        self.assertEqual([11, 12], advisory["observedMemberItemIds"])
        self.assertEqual("Panoplie FICTIVE A", advisory["observedName"])
        self.assertFalse(advisory["catalogAdmitted"])

    def test_foreign_tag_and_reverse_missing_member_refuse_set(self):
        self.update("UPDATE ItemData SET itemSetId=8 WHERE id=12;")
        audit = audit_sqlite(self.db_path)
        self.assertEqual(1, audit.mismatched_member_tags)
        self.assertEqual(1, audit.foreign_reverse_tags)
        self.assertEqual(0, len(audit.sets))
        with self.assertRaises(ExportError):
            self.export(audit, allow_partial=True)
        self.assertFalse(self.output.exists())

    def test_missing_member_json_is_reported_as_invalid_source_set(self):
        self.update("UPDATE ItemSetData SET items='not json' WHERE id=7;")
        audit = audit_sqlite(self.db_path)
        self.assertEqual(1, len(audit.invalid_source_sets))
        with self.assertRaisesRegex(ExportError, "inconsistent sets"):
            self.export(audit)
        self.assertFalse(self.output.exists())

    def test_duplicate_member_disqualifies_set(self):
        self.update("UPDATE ItemSetData SET items='[11,11]' WHERE id=7;")
        audit = audit_sqlite(self.db_path)
        self.assertEqual(1, len(audit.invalid_source_sets))
        self.assertEqual(1, len(audit.sets))

    def test_missing_schema_rejected_without_side_effects(self):
        self.update("DROP TABLE ItemSetData;")
        with self.assertRaisesRegex(ExportError, "missing ItemSetData"):
            audit_sqlite(self.db_path)
        self.assertFalse(self.output.exists())

    def test_ambiguous_duplicate_translation_is_unknown_not_guessed(self):
        self.update("INSERT INTO translations VALUES('71','fr','Autre nom FICTIF');")
        audit = audit_sqlite(self.db_path)
        self.assertIsNone(next(x for x in audit.sets if x["id"] == 7)["name"])
        self.assertEqual(2, len(audit.sets))

    def test_missing_translation_is_null_without_inventing_names(self):
        self.update("DELETE FROM translations WHERE id='111';")
        audit = audit_sqlite(self.db_path)
        self.assertIsNone(next(x for x in audit.items if x["id"] == 11)["name"])

    def test_minus_one_is_known_unassigned_sentinel_not_a_new_set(self):
        self.update("UPDATE ItemData SET itemSetId=-1 WHERE id=99;")
        audit = audit_sqlite(self.db_path)
        self.assertEqual({"-1": 1}, audit.unassigned_item_sentinels)
        self.assertEqual([11, 12, 20], [row["id"] for row in audit.items])

    def test_unrecognized_negative_itemset_sentinel_is_rejected(self):
        self.update("UPDATE ItemData SET itemSetId=-9 WHERE id=99;")
        with self.assertRaisesRegex(ExportError, "unrecognized"):
            audit_sqlite(self.db_path)

    def test_sqlite_sidecar_keeps_audit_available_but_blocks_export(self):
        sidecar = Path(str(self.db_path) + "-shm")
        sidecar.write_bytes(b"synthetic unsealed sidecar")
        report = audit_sqlite(self.db_path)
        self.assertTrue(report.summary()["sourceHasSQLiteSidecars"])
        self.assertIn("-shm", report.sidecars)
        with self.assertRaisesRegex(ExportError, "sealed immutable snapshot"):
            self.export(report)
        self.assertFalse(self.output.exists())

    def test_cannot_overwrite_existing_output(self):
        self.output.mkdir()
        marker = self.output / "important.txt"
        marker.write_text("do not overwrite", encoding="utf-8")
        with self.assertRaisesRegex(ExportError, "already exists"):
            self.export()
        self.assertEqual("do not overwrite", marker.read_text("utf-8"))

    def test_symlink_destination_rejected_when_symlinks_are_supported(self):
        link = self.folder / "linked"
        try:
            link.symlink_to(self.folder, target_is_directory=True)
        except (OSError, NotImplementedError):
            self.skipTest("creating symlinks requires host privileges")
        with self.assertRaisesRegex(ExportError, "linked/reparse"):
            export_catalog(
                audit_sqlite(self.db_path), link / "output", source_owned=True,
                rights_evidence="synthetic-test-owner", game_version="3.7.12.12",
                version_evidence="synthetic-test-version",
                expected_source_sha256=sha256_file(self.db_path)
            )

    def test_cli_audit_requires_no_rights_and_cli_export_gates_rights(self):
        entry = Path(__file__).with_name("local_set_export.py")
        result = subprocess.run(
            [sys.executable, str(entry), "audit", "--db", str(self.db_path)],
            capture_output=True, text=True, check=False, encoding="utf-8"
        )
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn('"sourceSetCount": 2', result.stdout)
        result = subprocess.run(
            [sys.executable, str(entry), "export", "--db", str(self.db_path),
             "--output", str(self.output), "--rights-evidence", "synthetic-test-owner",
             "--game-version", "3.7.12.12", "--version-evidence", "synthetic-test-version",
             "--expected-source-sha256", sha256_file(self.db_path)],
            capture_output=True, text=True, check=False, encoding="utf-8"
        )
        self.assertEqual(2, result.returncode)
        self.assertIn("--source-owned", result.stderr)
        self.assertFalse(self.output.exists())


if __name__ == "__main__":
    unittest.main()
