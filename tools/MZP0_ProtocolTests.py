#!/usr/bin/env python3
"""Isolated negative-contract tests for the MZP0 offline protocol guard."""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import MZP0_ProtocolGuard as guard


class ProtocolGuardTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory(prefix="mzp0_protocol_")
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        self.target = root / "target"
        self.source = root / "source"
        self.bundle = root / "bundle"
        for folder in (self.target, self.source / "GivreClone.Protocol", self.bundle):
            folder.mkdir(parents=True)
        self.pairs = [(f"Msg{i}", "id") for i in range(302)]
        self.aliases = {f"type.ankama.com/x{i}": f"MessageType.Msg{i}" for i in range(302)}
        self.fields = {f"Msg{i}": {"id": 1} for i in range(302)}
        self.scanned = self.pairs[:301]
        self.extra = self.pairs[301:]
        self.write_declarations()
        self.write_catalogues()
        self.pins_patch = patch.object(guard, "PINS", self.pins)
        self.pins_patch.start()
        self.addCleanup(self.pins_patch.stop)

    def write_declarations(self) -> None:
        for filename, name, rows in (("RequiredMapping.cs", "Scanned", self.scanned),
                                     ("RequiredMapping.Extra.cs", "Extra", self.extra)):
            values = ",\n".join(f'("{msg}", "{field}")' for msg, field in rows)
            (self.source / "GivreClone.Protocol" / filename).write_text(
                f"public static readonly (string Message, string Field)[] {name} = {{\n{values}\n}};\n",
                encoding="utf-8")

    def write_catalogues(self, *, pinned: bool = True) -> None:
        entries = {"matching_current.json": self.aliases, "fields.json": self.fields}
        self.pins = {}
        files = {}
        for filename, content in entries.items():
            data = json.dumps(content, sort_keys=True, separators=(",", ":")).encode()
            (self.target / filename).write_bytes(data)
            (self.bundle / filename).write_bytes(data)
            self.pins[filename] = (len(data), guard.sha256(data))
            files[filename] = {"size": len(data), "sha256": guard.sha256(data)}
        (self.target / "PROVENANCE.md").write_text("\n".join(
            f"{name} {digest}" for name, (_, digest) in self.pins.items()), encoding="utf-8")
        self.manifest = self.source / "manifest.json"
        self.manifest.write_text(json.dumps({"client_version": guard.TARGET_VERSION,
                                             "origin": "authorized_offline_extract", "files": files}),
                                 encoding="utf-8")
        if pinned and hasattr(self, "pins_patch"):
            self.pins_patch.stop()
            self.pins_patch = patch.object(guard, "PINS", self.pins)
            self.pins_patch.start()

    def assess(self, include_source: bool = True) -> dict:
        if include_source:
            return guard.assess(self.target, self.source, self.bundle, self.manifest)
        return guard.assess(self.target, self.source)

    def test_matching_synthetic_catalogue_is_only_candidate(self) -> None:
        result = self.assess()
        self.assertEqual(result["verdict"], "CATALOGUE_CONCORDANT_CANDIDATE")
        self.assertEqual(result["exe_parity"], "NOT_VERIFIED")
        self.assertEqual(result["server_ack"], "NOT_VERIFIED")
        self.assertEqual(result["required_pairs"], 302)

    def test_missing_source_reports_source_only(self) -> None:
        result = self.assess(False)
        self.assertEqual(result["verdict"], "SOURCE_ONLY")
        self.assertEqual(result["reason"], "NO_ATTESTED_GIVRE_3_7_SOURCE_BUNDLE")

    def test_manifest_and_bundle_must_be_supplied_together(self) -> None:
        with self.assertRaisesRegex(guard.GuardError, "source_bundle_and_manifest_required_together"):
            guard.assess(self.target, self.source, self.bundle)

    def test_wrong_client_version_blocks(self) -> None:
        with self.assertRaisesRegex(guard.GuardError, "unsupported_target_version"):
            guard.assess(self.target, self.source, target_version="3.6.12.16")
        manifest = json.loads(self.manifest.read_text())
        manifest["client_version"] = "3.6.12.16"
        self.manifest.write_text(json.dumps(manifest))
        with self.assertRaisesRegex(guard.GuardError, "source_version_mismatch"):
            self.assess()

    def test_source_origin_is_not_accepted_without_explicit_classification(self) -> None:
        manifest = json.loads(self.manifest.read_text())
        manifest["origin"] = "live_process"
        self.manifest.write_text(json.dumps(manifest))
        with self.assertRaisesRegex(guard.GuardError, "source_origin_unattested"):
            self.assess()

    def test_target_modified_after_pinning_blocks(self) -> None:
        with (self.target / "fields.json").open("ab") as stream:
            stream.write(b" ")
        with self.assertRaisesRegex(guard.GuardError, "sha_or_size_drift"):
            self.assess()

    def test_target_provenance_is_required(self) -> None:
        (self.target / "PROVENANCE.md").write_text("no verified source")
        with self.assertRaisesRegex(guard.GuardError, "target_provenance_missing_pin"):
            self.assess()

    def test_source_manifest_hash_drift_blocks(self) -> None:
        with (self.bundle / "matching_current.json").open("ab") as stream:
            stream.write(b" ")
        with self.assertRaisesRegex(guard.GuardError, "source_bundle_pin_mismatch"):
            self.assess()

    def test_missing_required_field_is_review_required(self) -> None:
        self.fields["Msg0"].pop("id")
        self.write_catalogues()
        result = self.assess()
        self.assertIn("Msg0.id", result["missing_required"])
        self.assertEqual(result["verdict"], "REVIEW_REQUIRED")

    def test_remapped_required_field_is_review_required(self) -> None:
        self.fields["Msg0"]["id"] = 3
        self.write_catalogues()
        remote = json.loads((self.bundle / "fields.json").read_text())
        remote["Msg0"]["id"] = 4
        self.repin_bundle_only("fields.json", remote)
        report = self.assess()
        self.assertEqual(report["verdict"], "REVIEW_REQUIRED")
        self.assertIn("tag:Msg0.id", report["differences"])

    def test_remapped_alias_is_review_required(self) -> None:
        remote = dict(self.aliases)
        remote.pop("type.ankama.com/x0")
        remote["type.ankama.com/other"] = "MessageType.Msg0"
        self.repin_bundle_only("matching_current.json", remote)
        report = self.assess()
        self.assertEqual(report["verdict"], "REVIEW_REQUIRED")
        self.assertIn("alias:Msg0", report["differences"])

    def test_same_numeric_tag_with_two_required_names_blocks(self) -> None:
        self.fields["Msg0"]["alternate"] = 1
        self.write_catalogues()
        report = self.assess()
        self.assertEqual(report["verdict"], "REVIEW_REQUIRED")
        self.assertIn("Msg0.id", report["ambiguous_required"])

    def test_duplicate_json_object_key_blocks(self) -> None:
        with self.assertRaisesRegex(guard.GuardError, "duplicate_json_key"):
            guard.load_json(b'{"type.ankama.com/a":"MessageType.A","type.ankama.com/a":"MessageType.B"}', "matching")

    def test_duplicate_name_to_codes_blocks(self) -> None:
        with self.assertRaisesRegex(guard.GuardError, "name_maps_to_multiple_codes"):
            guard.parse_matching({"type.ankama.com/a": "MessageType.Foo",
                                  "type.ankama.com/b": "MessageType.Foo"})

    def test_invalid_zero_negative_bool_and_reserved_tags_block(self) -> None:
        for num in (0, -1, True, 19000, 536870912):
            with self.subTest(num=num), self.assertRaisesRegex(guard.GuardError, "invalid_field_tag"):
                guard.parse_fields({"Foo": {"id": num}})

    def test_source_required_manifest_incomplete_blocks(self) -> None:
        self.scanned = [("Msg1", "id")]
        self.write_declarations()
        with self.assertRaisesRegex(guard.GuardError, "manifest_too_small"):
            self.assess()

    def test_source_missing_file_blocks(self) -> None:
        (self.source / "GivreClone.Protocol" / "RequiredMapping.Extra.cs").unlink()
        with self.assertRaisesRegex(guard.GuardError, "missing_or_unreadable"):
            self.assess()

    def test_oversized_input_blocks_before_json_processing(self) -> None:
        with patch.object(guard, "MAX_INPUT_BYTES", 32):
            with self.assertRaisesRegex(guard.GuardError, "input_too_large"):
                self.assess()

    def repin_bundle_only(self, filename: str, data: dict) -> None:
        blob = json.dumps(data, sort_keys=True, separators=(",", ":")).encode()
        (self.bundle / filename).write_bytes(blob)
        manifest = json.loads(self.manifest.read_text())
        manifest["files"][filename] = {"size": len(blob), "sha256": guard.sha256(blob)}
        self.manifest.write_text(json.dumps(manifest))


if __name__ == "__main__":
    unittest.main(verbosity=2)
