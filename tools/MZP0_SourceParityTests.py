#!/usr/bin/env python3
"""Offline behavioral checks for MZP0_SourceParity (stdlib unittest only)."""

from __future__ import annotations

import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

import MZP0_SourceParity as parity


class SourceParityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.givre = Path(__file__).resolve().parents[1]
        cls.rework = cls.givre.parent / "GivreRework"

    def test_known_source_symbols_are_located(self) -> None:
        report = parity.build_inventory(self.givre, self.rework)
        self.assertEqual(report["feature_count"], 21)
        features = {f["id"]: f for f in report["features"]}
        for key in ("protocol_catalog", "outbound_envelope", "inbound_capture",
                    "request_correlation", "packet_bus", "action_sender", "combat",
                    "navigation", "bank", "market", "craft", "lua_runtime",
                    "route_recording", "multi_account", "orchestration", "native_turbo",
                    "avalonia_ui"):
            self.assertEqual(features[key]["givre"]["status"], "source_only", key)
        for key in ("protocol_catalog", "stream_framing", "private_differential",
                    "operator_panel", "game_state", "avalonia_ui"):
            self.assertEqual(features[key]["givre_rework"]["status"], "source_only", key)
        self.assertEqual(features["action_sender"]["givre_rework"]["status"], "missing_source")
        self.assertEqual(features["native_turbo"]["givre_rework"]["status"], "missing_source")

    def test_missing_external_core_and_mapping_is_reported(self) -> None:
        report = parity.build_inventory(self.givre, self.rework)
        self.assertFalse(report["dependencies_present"]["givre_core_neighbor_project"])
        self.assertFalse(report["dependencies_present"]["givre_data_mapping_directory"])
        self.assertFalse(report["global_proof"]["runtime_gameplay_validated"])
        self.assertFalse(report["global_proof"]["server_ack_validated"])
        self.assertEqual(report["global_proof"]["exe_features_confirmed"], 0)
        self.assertTrue(all(f["exe_confirmed"] is False for f in report["features"]))

    def test_no_private_dump_read_or_executable_assumed(self) -> None:
        report = parity.build_inventory(self.givre, self.rework)
        self.assertEqual(report["binary_provenance"]["state"], "no_offline_exe")
        self.assertEqual(report["binary_provenance"]["artifacts"], [])
        self.assertNotIn("dmp", json.dumps(report).lower())

    def test_app_modules_have_source_provenance_but_no_exe_claim(self) -> None:
        report = parity.build_inventory(self.givre, self.rework)
        modules = report["givre_app_module_provenance"]
        self.assertGreaterEqual(len(modules), 30)
        self.assertTrue(all(row["source_only"] and not row["exe_confirmed"] for row in modules))
        self.assertTrue(any(row["category"] == "lua_binding" for row in modules))
        self.assertTrue(any(row["category"] == "gameplay_service" for row in modules))
        self.assertTrue(any(row["category"] == "view_definition" for row in modules))
        self.assertFalse(any("bin/" in row["file"] or "dmp" in row["file"] for row in modules))

    def test_unverified_running_window_is_distinct_from_binary(self) -> None:
        report = parity.build_inventory(self.givre, self.rework, "1.3.1")
        observed = report["binary_provenance"]["running_window_observation"]
        self.assertEqual(observed["status"], "running_window_only")
        self.assertEqual(observed["version_label"], "1.3.1")
        self.assertIsNone(observed["binary_sha256"])
        self.assertFalse(observed["executable_read_or_copied"])
        self.assertEqual(report["binary_provenance"]["source_declared_version"]["version"], "1.2.0")
        self.assertEqual(report["binary_provenance"]["source_vs_window_version"],
                         "different_unlinked_labels")

    def test_inventory_is_deterministic_with_stable_sources(self) -> None:
        first = json.dumps(parity.build_inventory(self.givre, self.rework),
                           sort_keys=True, ensure_ascii=False)
        second = json.dumps(parity.build_inventory(self.givre, self.rework),
                            sort_keys=True, ensure_ascii=False)
        self.assertEqual(first, second)
        self.assertNotIn(str(self.givre), first)
        self.assertNotIn(str(self.rework), first)

    def test_anchor_matches_real_declaration_not_comment(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            src = root / "Example.cs"
            anchor = parity.Anchor("Example.cs", r"^\s*public sealed class Sample\b", "Sample")
            src.write_text("// public sealed class Sample\nstring s = 'Sample';\n", encoding="utf-8")
            self.assertEqual(parity.evidence(root, anchor)["status"], "missing_source")
            src.write_text("public sealed class Sample { }\n", encoding="utf-8")
            check = parity.evidence(root, anchor)
            self.assertEqual(check["status"], "source_only")
            self.assertEqual(check["evidence"][0]["line"], 1)
            self.assertEqual(check["evidence"][0]["sha256"], hashlib.sha256(src.read_bytes()).hexdigest())
            src.write_text("public sealed class Sample { public int V; }\n", encoding="utf-8")
            self.assertNotEqual(parity.evidence(root, anchor)["evidence"][0]["sha256"],
                                check["evidence"][0]["sha256"])

    def test_absent_or_unreadable_source_is_not_pass(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            anchor = parity.Anchor("Absent.cs", r"^public class Absent\b", "Absent")
            self.assertEqual(parity.evidence(root, anchor)["status"], "missing_source")
            file = root / "Absent.cs"
            file.write_bytes(b"\xff\xf9\x00")
            self.assertEqual(parity.evidence(root, anchor)["status"], "unverified")

    def test_escape_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            for escape in ("../outside.cs", "../../outside.cs"):
                with self.assertRaises(ValueError):
                    parity.confined_path(root, escape)

    def test_invalid_pe_does_not_count_as_offline_executable(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            bin_dir = root / "GivreClone.App" / "bin" / "Debug" / "net10.0"
            bin_dir.mkdir(parents=True)
            executable = bin_dir / "GivreClone.exe"
            executable.write_bytes(b"MZ" + b"\x00" * 200)
            self.assertEqual(parity.binary_provenance(root)["state"], "no_offline_exe")

    def test_pe_header_only_means_identity_not_functionality(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            bin_dir = root / "GivreClone.App" / "bin" / "Release" / "net10.0"
            bin_dir.mkdir(parents=True)
            executable = bin_dir / "GivreClone.exe"
            image = bytearray(256)
            image[0:2] = b"MZ"
            image[60:64] = (128).to_bytes(4, "little")
            image[128:132] = b"PE\x00\x00"
            executable.write_bytes(image)
            proof = parity.binary_provenance(root)
            self.assertEqual(proof["state"], "offline_executable_present")
            self.assertEqual(proof["artifacts"][0]["feature_proof"], "none")
            self.assertEqual(proof["exe_feature_verification"], "not_performed")

    def test_nested_publish_exe_is_recognized_as_file_only(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            bin_dir = root / "GivreClone.App" / "bin" / "Release" / "net10.0" / "win-x64" / "publish"
            bin_dir.mkdir(parents=True)
            image = bytearray(256)
            image[0:2] = b"MZ"
            image[60:64] = (128).to_bytes(4, "little")
            image[128:132] = b"PE\x00\x00"
            (bin_dir / "GivreClone.exe").write_bytes(image)
            proof = parity.binary_provenance(root)
            self.assertEqual(proof["state"], "offline_executable_present")
            self.assertTrue(proof["artifacts"][0]["file"].endswith("publish/GivreClone.exe"))

    def test_check_detects_stale_snapshot(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            temp = Path(tmp)
            givre, rework = temp / "GivreWork", temp / "GivreRework"
            givre.mkdir()
            rework.mkdir()
            output = temp / "manifest.json"
            cli = [sys.executable, "-B", str(Path(parity.__file__)), "--givre-root",
                   str(givre), "--rework-root", str(rework), "--output", str(output)]
            written = subprocess.run(cli, capture_output=True, text=True, check=False)
            self.assertEqual(written.returncode, 0, written.stderr)
            self.assertEqual(subprocess.run(cli + ["--check"], capture_output=True).returncode, 0)
            source = givre / "GivreClone.Protocol" / "ProtocolCatalog.cs"
            source.parent.mkdir(parents=True)
            source.write_text("public sealed class ProtocolCatalog {}", encoding="utf-8")
            self.assertNotEqual(subprocess.run(cli + ["--check"], capture_output=True).returncode, 0)

    def test_non_assembly_exe_name_is_not_assumed(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            bin_dir = root / "GivreClone.App" / "bin" / "Debug" / "net10.0"
            bin_dir.mkdir(parents=True)
            image = bytearray(256)
            image[0:2] = b"MZ"
            image[60:64] = (128).to_bytes(4, "little")
            image[128:132] = b"PE\x00\x00"
            (bin_dir / "GivreClone.App.exe").write_bytes(image)
            self.assertEqual(parity.binary_provenance(root)["artifacts"], [])


if __name__ == "__main__":
    unittest.main(verbosity=2)
