#!/usr/bin/env python3
"""Offline tests of the bounded inventory and detached-only recovery choice."""

from __future__ import annotations

import hashlib
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from MZP0_OfflinePeAuditTests import fixture
from MZP1_BinaryIndex import inventory, pe_header
from MZP1_BinaryVerify import verify


class BinaryInventoryTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory(prefix="mzp1-binary-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.good = fixture(clr=True)

    def test_managed_detached_exe_is_hashed_and_listed(self) -> None:
        folder = self.root / "GivreWork" / "offline-release"
        folder.mkdir(parents=True)
        target = folder / "GivreClone.exe"
        target.write_bytes(self.good)
        listing = inventory(self.root)
        self.assertTrue(listing["coverage_complete_within_limits"])
        self.assertTrue(listing["givrebot_exe_found"])
        row = listing["givrebot_candidates"][0]
        self.assertEqual(row["sha256"], hashlib.sha256(self.good).hexdigest())
        self.assertEqual(row["relative_path"], "GivreWork/offline-release/GivreClone.exe")
        self.assertEqual(row["pe"]["status"], "pe")
        self.assertTrue(row["pe"]["clr_directory_present"])
        self.assertTrue(row["pe"]["clr_metadata_signature_valid"])
        self.assertTrue(row["pe"]["clr_il_only"])

    def test_dll_pe_header_is_qualified_independently_of_exe(self) -> None:
        dll = self.root / "GivreRelay.dll"
        dll.write_bytes(self.good)
        report = inventory(self.root)
        self.assertFalse(report["givrebot_exe_found"])
        self.assertEqual(report["named_candidates"][0]["pe"]["architecture"], "x64")
        self.assertEqual(pe_header(dll)["status"], "pe")
        self.assertEqual(report["named_candidates"][0]["sha256"], hashlib.sha256(self.good).hexdigest())

    def test_excludes_captures_secrets_and_dumps(self) -> None:
        for name in ("captures", "secrets", "config", ".git", "assets", "obj", ".venv"):
            path = self.root / name
            path.mkdir()
            (path / "GivreClone.exe").write_bytes(self.good)
        (self.root / "givre-live-mini.dmp").write_bytes(self.good)
        report = inventory(self.root)
        self.assertFalse(report["givrebot_exe_found"])
        self.assertEqual(report["binary_files_indexed"], 0)
        self.assertGreaterEqual(report["skipped"]["prohibited"], 8)

    def test_bounded_scan_does_not_claim_completeness(self) -> None:
        for n in range(15):
            (self.root / f"file_{n:03}.txt").write_text("not a binary", encoding="ascii")
        report = inventory(self.root, max_entries=5)
        self.assertFalse(report["coverage_complete_within_limits"])
        self.assertLessEqual(report["entries_inspected"], 6)

    def test_binary_record_limit_is_explicit(self) -> None:
        for n in range(4):
            (self.root / f"Other_{n}.dll").write_bytes(self.good)
        report = inventory(self.root, max_records=1)
        self.assertEqual(report["binary_files_indexed"], 1)
        self.assertFalse(report["coverage_complete_within_limits"])

    def test_source_symlink_is_skipped_when_available(self) -> None:
        destination = self.root / "real"
        destination.mkdir()
        (destination / "GivreClone.exe").write_bytes(self.good)
        link = self.root / "linked"
        try:
            link.symlink_to(destination, target_is_directory=True)
        except (OSError, NotImplementedError):
            self.skipTest("creating symlinks unavailable")
        report = inventory(self.root)
        self.assertEqual(report["givrebot_candidates"][0]["relative_path"], "real/GivreClone.exe")
        self.assertEqual(len(report["givrebot_candidates"]), 1)
        self.assertGreaterEqual(report["skipped"]["reparse"], 1)

    def test_no_candidate_verifier_stays_blocked(self) -> None:
        result = verify(self.root, None)
        self.assertEqual(result["status"], "blocked_no_detached_exe")
        self.assertFalse(result["source_recovered"])

    def test_native_real_pe_not_promoted_to_managed(self) -> None:
        path = self.root / "GivreClone.exe"
        path.write_bytes(fixture(clr=False))
        with patch("MZP1_BinaryVerify.reconstruct") as decompile:
            result = verify(self.root, path)
        self.assertEqual(result["status"], "unsupported_native_or_aot")
        self.assertFalse(result["source_recovered"])
        decompile.assert_not_called()

    def test_managed_target_reuses_real_pe_audit_and_explicit_port(self) -> None:
        path = self.root / "GivreClone.exe"
        path.write_bytes(self.good)
        fake_result = {"status": "managed_source_recovered", "file_count": 3}
        with patch("MZP1_BinaryVerify.reconstruct", return_value=fake_result) as decompile:
            result = verify(self.root, path)
        self.assertEqual(result["status"], "managed_source_recovered")
        self.assertTrue(result["source_recovered"])
        self.assertEqual(result["binary"]["sha256"], hashlib.sha256(self.good).hexdigest())
        arguments = decompile.call_args.args
        self.assertEqual(arguments[0], path)
        self.assertIn("MZP0_OfflineDecompile_MZP1_", arguments[1])
        self.assertEqual(arguments[2], self.root)

    def test_exe_named_differently_is_not_an_authorized_target(self) -> None:
        path = self.root / "GivreClone_v131.exe"
        path.write_bytes(self.good)
        with self.assertRaises(ValueError):
            verify(self.root, path)

    def test_non_pe_file_is_rejected(self) -> None:
        path = self.root / "GivreClone.exe"
        path.write_bytes(b"ZZ" + b"\0" * 256)
        with self.assertRaises(ValueError):
            verify(self.root, path)


if __name__ == "__main__":
    unittest.main(verbosity=2)
