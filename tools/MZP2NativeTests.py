#!/usr/bin/env python3
"""Pure offline synthetic PE, safety and provenance tests (no real EXEs)."""

from __future__ import annotations

import hashlib
import struct
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import MZP2NativeProvenance as native


def native_fixture(*, resource: bool = True, imported: bool = True,
                   section_name: bytes = b".text") -> bytes:
    blob = bytearray(0xC00)
    blob[0:2] = b"MZ"
    struct.pack_into("<I", blob, 0x3C, 0x80)
    blob[0x80:0x84] = b"PE\0\0"
    struct.pack_into("<HHIIIHH", blob, 0x84, 0x8664, 2, 0, 0, 0, 0xF0, 0)
    op = 0x98
    struct.pack_into("<H", blob, op, 0x20B)
    struct.pack_into("<I", blob, op + 16, 0x2000)
    struct.pack_into("<I", blob, op + 108, 16)
    sec = op + 0xF0
    blob[sec:sec + len(section_name)] = section_name
    struct.pack_into("<IIII", blob, sec + 8, 0x400, 0x2000, 0x400, 0x400)
    struct.pack_into("<I", blob, sec + 36, 0x60000020)
    sec2 = sec + 40
    blob[sec2:sec2 + 5] = b".rsrc"
    struct.pack_into("<IIII", blob, sec2 + 8, 0x400, 0x3000, 0x400, 0x800)
    struct.pack_into("<I", blob, sec2 + 36, 0x40000040)
    if imported:
        struct.pack_into("<II", blob, op + 112 + 8, 0x2100, 0x28)
        struct.pack_into("<IIIII", blob, 0x500, 0, 0, 0, 0x2200, 0)
        blob[0x600:0x60d] = b"KERNEL32.dll\0"
    if resource:
        struct.pack_into("<II", blob, op + 112 + 2 * 8, 0x3000, 0x80)
        struct.pack_into("<H", blob, 0x800 + 14, 1)
        struct.pack_into("<II", blob, 0x800 + 16, 16, 0x80000020)
    return bytes(blob)


class NativeOfflineFixtureTests(unittest.TestCase):
    def setUp(self) -> None:
        self.scratch = tempfile.TemporaryDirectory(prefix="mzp2native-fixture-")
        self.addCleanup(self.scratch.cleanup)
        self.root = Path(self.scratch.name)
        (self.root / "GivreRework").mkdir()
        self.payload = native_fixture()

    def make_exe(self, blob: bytes | None = None, name: str = "GivreClone.exe") -> Path:
        candidate = self.root / "GivreRework" / name
        candidate.write_bytes(blob if blob is not None else self.payload)
        return candidate

    def test_fingerprint_imports_resource_and_sections_static_only(self) -> None:
        candidate = self.make_exe()
        result = native.static_fingerprint(candidate, self.root, expected_sha=native.sha256(candidate))
        self.assertEqual(result["coff_machine"], "0x8664")
        self.assertEqual(result["pe_format"], "PE32+")
        self.assertEqual(result["section_count"], 2)
        self.assertEqual(result["import_dlls"], ["kernel32.dll"])
        self.assertEqual(result["resource_types"],
                         [{"type": "standard_or_numeric", "id": 16, "value": "VERSION"}])
        self.assertEqual(result["sections"][0]["name"], ".text")
        self.assertIsNotNone(result["sections"][0]["start_sample_entropy_bits_per_byte"])
        self.assertFalse(result["installed_executable_accessed"])
        self.assertFalse(result["protection_unpacked_or_bypassed"])
        self.assertFalse(result["nativeaot_attested"])
        self.assertFalse(result["packer_attested"])

    def test_fingerprint_empty_import_resource_tables_is_valid(self) -> None:
        candidate = self.make_exe(native_fixture(resource=False, imported=False))
        result = native.static_fingerprint(candidate, self.root, expected_sha=native.sha256(candidate))
        self.assertEqual(result["import_dlls"], [])
        self.assertEqual(result["resource_types"], [])

    def test_fingerprint_rejects_sha_mismatch_before_metadata(self) -> None:
        candidate = self.make_exe()
        with self.assertRaisesRegex(native.RejectedOfflineArtifact, "SHA-256 mismatch"):
            native.static_fingerprint(candidate, self.root, expected_sha="0" * 64)

    def test_fingerprint_rejects_corrupt_PE(self) -> None:
        candidate = self.make_exe(b"BINARY" + b"\0" * 0xC00)
        with self.assertRaisesRegex(native.RejectedOfflineArtifact, "not DOS MZ"):
            native.static_fingerprint(candidate, self.root, expected_sha=native.sha256(candidate))

    def test_fingerprint_rejects_invalid_import_directory(self) -> None:
        corrupted = bytearray(native_fixture())
        struct.pack_into("<I", corrupted, 0x98 + 112 + 8, 0xFFF000)
        candidate = self.make_exe(bytes(corrupted))
        with self.assertRaisesRegex(native.RejectedOfflineArtifact, "RVA outside mapped"):
            native.static_fingerprint(candidate, self.root, expected_sha=native.sha256(candidate))

    def test_fingerprint_rejects_corrupt_resource_bounds(self) -> None:
        corrupted = bytearray(native_fixture())
        struct.pack_into("<H", corrupted, 0x800 + 14, 250)
        candidate = self.make_exe(bytes(corrupted))
        with self.assertRaisesRegex(native.RejectedOfflineArtifact, "resource type table"):
            native.static_fingerprint(candidate, self.root, expected_sha=native.sha256(candidate))

    def test_search_sha_exact_admits_renamed_detached_file(self) -> None:
        self.make_exe(name="detached-backup.exe")
        with patch.object(native, "EXPECTED_BYTES", len(self.payload)), \
             patch.object(native, "EXPECTED_SHA256", hashlib.sha256(self.payload).hexdigest()):
            report = native.search_detached(self.root, roots=("GivreRework",))
        self.assertTrue(report["all_subtrees_complete"])
        self.assertEqual(len(report["exact_sha_matches"]), 1)
        self.assertEqual(report["exact_sha_matches"][0]["path"], "GivreRework/detached-backup.exe")

    def test_search_mismatch_does_not_fingerprint(self) -> None:
        self.make_exe()
        with patch.object(native, "EXPECTED_BYTES", len(self.payload)):
            report = native.search_detached(self.root, roots=("GivreRework",))
        self.assertEqual(report["exact_sha_matches"], [])
        self.assertEqual(report["subtrees"][0]["skipped"]["hash_mismatch"], 1)

    def test_end_to_end_fixture_is_fingerprinted_only_on_exact_sha(self) -> None:
        self.make_exe()
        (self.root / "GivreWork").mkdir()
        with patch.object(native, "EXPECTED_BYTES", len(self.payload)), \
             patch.object(native, "EXPECTED_SHA256", hashlib.sha256(self.payload).hexdigest()):
            result = native.run(self.root, roots=("GivreRework",))
        self.assertEqual(result["status"], "matching_detached_fingerprint_verified")
        self.assertEqual(result["verified_detached_fingerprints"][0]["fingerprint"]["import_dlls"],
                         ["kernel32.dll"])
        self.assertTrue(result["version_drift"]["detached_binary_verified"])
        self.assertFalse(result["version_drift"]["product_functionality_proven_from_binary"])

    def test_search_skips_capture_secret_and_config_subtrees(self) -> None:
        for label in ("capture-data", "secrets", "configs", "givre-live-run", "obj"):
            folder = self.root / "GivreRework" / label
            folder.mkdir()
            (folder / "GivreClone.exe").write_bytes(self.payload)
        with patch.object(native, "EXPECTED_BYTES", len(self.payload)), \
             patch.object(native, "EXPECTED_SHA256", hashlib.sha256(self.payload).hexdigest()):
            report = native.search_detached(self.root, roots=("GivreRework",))
        self.assertEqual(report["exact_sha_matches"], [])
        self.assertGreaterEqual(report["subtrees"][0]["skipped"]["excluded"], 5)

    def test_search_budget_shortage_never_claims_full_coverage(self) -> None:
        for n in range(10):
            (self.root / "GivreRework" / f"x_{n:02}.txt").write_text("text", encoding="ascii")
        report = native.search_detached(self.root, roots=("GivreRework",), max_entries=2)
        self.assertFalse(report["all_subtrees_complete"])
        self.assertEqual(report["subtrees"][0]["status"], "partial")

    def test_search_rejects_subtree_outside_allowlist(self) -> None:
        with self.assertRaisesRegex(native.RejectedOfflineArtifact, "allowlist"):
            native.search_detached(self.root, roots=("../../AppData",))

    def test_source_version_matrix_does_not_assert_binary_parity(self) -> None:
        rels = {
            "GivreClone.App/GivreClone.App.csproj": "<AssemblyName>GivreClone</AssemblyName>\n<PublishAot>true</PublishAot>\n",
            "GivreClone.App/Services/LicenseService.cs": 'public const string AppVersion = "1.2.0";\n',
            "GivreClone.Engine/BotSession.cs": "class BotSession {}",
            "GivreClone.Engine/ActionSender.cs": "class ActionSender {}",
            "GivreClone.Engine/DofusCaptureHost.cs": "class DofusCaptureHost {}",
        }
        for rel, content in rels.items():
            path = self.root / "GivreWork" / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")
        report = native.source_version_matrix(self.root / "GivreWork", True)
        self.assertEqual([item["status"] for item in report["source_evidence"]], ["source_present"] * 6)
        self.assertTrue(all(item["exe_confirmed"] is False for item in report["source_evidence"]))
        self.assertEqual(report["nativeaot_assessment"], "unknown")
        self.assertEqual(report["packer_assessment"], "unknown")
        self.assertEqual(report["source_evidence"][1]["file_sha256"],
                         hashlib.sha256((self.root / "GivreWork" /
                                        "GivreClone.App/Services/LicenseService.cs").read_bytes()).hexdigest())

    def test_source_version_matrix_missing_files_are_explicit(self) -> None:
        folder = self.root / "GivreWork"
        folder.mkdir()
        report = native.source_version_matrix(folder, False)
        self.assertEqual({v["status"] for v in report["source_evidence"]}, {"missing_source"})
        self.assertFalse(report["detached_binary_verified"])

    def test_reject_symlink_into_outside_tree(self) -> None:
        target = self.root / "other.exe"
        target.write_bytes(self.payload)
        link = self.root / "GivreRework" / "GivreClone.exe"
        try:
            link.symlink_to(target)
        except (OSError, NotImplementedError):
            self.skipTest("symlink creation unsupported")
        with self.assertRaisesRegex(native.RejectedOfflineArtifact, "reparse point"):
            native.static_fingerprint(link, self.root, expected_sha=native.sha256(target))
        with patch.object(native, "EXPECTED_BYTES", len(self.payload)), \
             patch.object(native, "EXPECTED_SHA256", hashlib.sha256(self.payload).hexdigest()):
            search = native.search_detached(self.root, roots=("GivreRework",))
        self.assertEqual(search["exact_sha_matches"], [])


if __name__ == "__main__":
    unittest.main(verbosity=2)
