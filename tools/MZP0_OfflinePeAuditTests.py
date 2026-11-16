#!/usr/bin/env python3
"""Self-contained PE fixtures; never read a real executable or process."""

from __future__ import annotations

import hashlib
import struct
import tempfile
import unittest
from pathlib import Path

import MZP0_OfflinePeAudit as audit


def fixture(*, clr: bool = False, metadata: bool = True, il_only: bool = True) -> bytes:
    image = bytearray(0x800)
    image[:2] = b"MZ"
    struct.pack_into("<I", image, 0x3C, 0x80)
    image[0x80:0x84] = b"PE\x00\x00"
    struct.pack_into("<HHIIIHH", image, 0x84, 0x8664, 1, 0, 0, 0, 0xF0, 0)
    optional = 0x98
    struct.pack_into("<H", image, optional, 0x20B)
    struct.pack_into("<I", image, optional + 108, 16)
    section = optional + 0xF0
    image[section:section + 8] = b".text\x00\x00\x00"
    struct.pack_into("<IIII", image, section + 8, 0x400, 0x2000, 0x400, 0x400)
    if clr:
        struct.pack_into("<II", image, optional + 112 + 14 * 8, 0x2100, 0x48)
        struct.pack_into("<I", image, 0x500, 0x48)
        struct.pack_into("<II", image, 0x508, 0x2200, 0x40)
        struct.pack_into("<I", image, 0x510, 1 if il_only else 0)
        if metadata:
            image[0x600:0x604] = b"BSJB"
    return bytes(image)


class OfflinePeAuditTests(unittest.TestCase):
    def examine(self, bytes_value: bytes) -> dict:
        with tempfile.TemporaryDirectory() as scratch:
            root = Path(scratch)
            artifact = root / "offline" / "GivreClone.exe"
            artifact.parent.mkdir()
            artifact.write_bytes(bytes_value)
            return audit.inspect_pe(artifact, root)

    def test_native_pe_candidate_is_not_assumed_to_be_nativeaot(self) -> None:
        payload = fixture()
        report = self.examine(payload)
        self.assertEqual(report["sha256"], hashlib.sha256(payload).hexdigest())
        self.assertEqual(report["analysis_path"], "native_or_aot_requires_static_recovery")
        self.assertEqual(report["architecture"], "x64")
        self.assertEqual(report["pe_kind"], "PE32+")
        self.assertEqual(report["sections"][0]["name"], ".text")
        self.assertFalse(report["binary_executed"])
        self.assertEqual(report["exe_feature_proof"], "none")

    def test_managed_il_requires_real_clr_metadata_signature(self) -> None:
        report = self.examine(fixture(clr=True))
        self.assertTrue(report["clr_header_present"])
        self.assertTrue(report["clr_metadata_signature_valid"])
        self.assertTrue(report["clr_il_only"])
        self.assertEqual(report["analysis_path"], "managed_il_eligible")

    def test_missing_clr_metadata_cannot_be_called_managed_il(self) -> None:
        report = self.examine(fixture(clr=True, metadata=False))
        self.assertTrue(report["clr_header_present"])
        self.assertFalse(report["clr_metadata_signature_valid"])
        self.assertEqual(report["analysis_path"], "native_or_aot_requires_static_recovery")

    def test_mixed_mode_requires_static_review(self) -> None:
        report = self.examine(fixture(clr=True, il_only=False))
        self.assertFalse(report["clr_il_only"])
        self.assertEqual(report["analysis_path"], "native_or_aot_requires_static_recovery")

    def test_outside_authority_is_denied(self) -> None:
        with tempfile.TemporaryDirectory() as scratch:
            outer = Path(scratch)
            allowed = outer / "approved"
            allowed.mkdir()
            candidate = outer / "GivreClone.exe"
            candidate.write_bytes(fixture())
            with self.assertRaises(audit.InvalidOfflineArtifact):
                audit.inspect_pe(candidate, allowed)

    def test_not_the_assembly_filename_is_denied(self) -> None:
        with tempfile.TemporaryDirectory() as scratch:
            root = Path(scratch)
            candidate = root / "GivreClone.App.exe"
            candidate.write_bytes(fixture())
            with self.assertRaises(audit.InvalidOfflineArtifact):
                audit.inspect_pe(candidate, root)

    def test_invalid_signatures_and_truncation_fail_closed(self) -> None:
        for broken in (b"MZ" + b"\x00" * 40, b"ZZ" + fixture()[2:],
                       fixture()[:0x150]):
            with self.subTest(length=len(broken)):
                with self.assertRaises(audit.InvalidOfflineArtifact):
                    self.examine(broken)

    def test_corrupted_section_pointer_fails_closed(self) -> None:
        broken = bytearray(fixture())
        section = 0x98 + 0xF0
        struct.pack_into("<I", broken, section + 20, 0xF000)
        with self.assertRaises(audit.InvalidOfflineArtifact):
            self.examine(bytes(broken))

    def test_unmapped_clr_rva_fails_closed(self) -> None:
        broken = bytearray(fixture(clr=True))
        struct.pack_into("<I", broken, 0x98 + 112 + 14 * 8, 0xF0000)
        with self.assertRaises(audit.InvalidOfflineArtifact):
            self.examine(bytes(broken))

    def test_incomplete_clr_directory_fails_closed(self) -> None:
        broken = bytearray(fixture(clr=True))
        struct.pack_into("<I", broken, 0x98 + 112 + 14 * 8 + 4, 0x10)
        with self.assertRaises(audit.InvalidOfflineArtifact):
            self.examine(bytes(broken))

    def test_symlink_denied_when_supported(self) -> None:
        with tempfile.TemporaryDirectory() as scratch:
            root = Path(scratch)
            outside = root / "source" / "GivreClone.exe"
            outside.parent.mkdir()
            outside.write_bytes(fixture())
            link = root / "GivreClone.exe"
            try:
                link.symlink_to(outside)
            except (OSError, NotImplementedError):
                self.skipTest("symlinks require additional Windows privileges")
            with self.assertRaises(audit.InvalidOfflineArtifact):
                audit.inspect_pe(link, root)


if __name__ == "__main__":
    unittest.main(verbosity=2)
