#!/usr/bin/env python3
"""Deterministic 100%-offline tests: synthetic PE and fake ilspycmd runner."""

from __future__ import annotations

import hashlib
import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

from MZP0_OfflinePeAuditTests import fixture
from MZP0_OfflineDecompile import (
    OfflineDecompileError, check_output, reconstruct,
)


class FakeIlspy:
    def __init__(self, *, exit_code: int = 0, mode: str = "valid") -> None:
        self.calls: list[list[str]] = []
        self.exit_code = exit_code
        self.mode = mode

    def __call__(self, args: list[str], **options) -> subprocess.CompletedProcess[str]:
        self.calls.append(args)
        assert options["capture_output"] is True
        assert options["text"] is True
        assert options["check"] is False
        assert options["timeout"] > 0
        if args[1:] == ["--version"]:
            if self.mode == "bad_version":
                return subprocess.CompletedProcess(args, 0, "unparseable-version", "")
            return subprocess.CompletedProcess(args, 0, "ilspycmd: 9.1.0.7988\n", "")
        assert args[1] == "-p"
        assert args[2] == "-o"
        destination = Path(args[3])
        assert args[4].endswith("GivreClone.exe")
        if self.exit_code:
            return subprocess.CompletedProcess(args, self.exit_code, "", "internal fake error")
        if self.mode == "tool_timeout":
            raise subprocess.TimeoutExpired(args, options["timeout"])
        (destination / "Main.cs").write_text("namespace Recovered; class MainThing {}\n", encoding="utf-8")
        if self.mode != "missing_project":
            (destination / "GivreClone.csproj").write_text(
                '<Project Sdk="Microsoft.NET.Sdk"></Project>\n', encoding="utf-8")
        if self.mode == "nested":
            (destination / "Sub").mkdir()
            (destination / "Sub" / "Feature.cs").write_text("class Feature {}\n", encoding="utf-8")
        if self.mode == "input_changed":
            exe = Path(args[4])
            original = bytearray(exe.read_bytes())
            original[-1] ^= 0x01
            exe.write_bytes(original)
        if self.mode == "symlink_output":
            (destination / "Link").symlink_to(destination / "Main.cs")
        if self.mode == "dest_race":
            published = destination.parent.parent / "MZP0_OfflineDecompile_fixture"
            published.mkdir()
            (published / "sentinel.txt").write_text("do not overwrite", encoding="utf-8")
        return subprocess.CompletedProcess(args, 0, "fake private output", "")


class OfflineDecompileTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory(prefix="mzp0-offline-")
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.input_parent = self.root / "offline"
        self.input_parent.mkdir()
        self.exe = self.input_parent / "GivreClone.exe"
        self.exe.write_bytes(fixture(clr=True))
        self.output_rel = "offline/MZP0_OfflineDecompile_fixture"
        self.outdir = self.root / self.output_rel

    def recover(self, tool: FakeIlspy | None = None) -> dict:
        return reconstruct(self.exe, self.output_rel, self.root, ilspy_executable="ilspycmd",
                           runner=tool or FakeIlspy())

    def test_managed_project_is_recovered_with_manifest_and_file_sha(self) -> None:
        fake = FakeIlspy(mode="nested")
        output = self.recover(fake)
        self.assertEqual(output["status"], "managed_source_recovered")
        self.assertFalse(output["exe_executed"])
        self.assertFalse(output["feature_equivalence_certified"])
        self.assertEqual(len(fake.calls), 2)
        self.assertEqual(fake.calls[0], ["ilspycmd", "--version"])
        self.assertEqual(fake.calls[1][1:3], ["-p", "-o"])
        self.assertTrue(self.outdir.is_dir())
        report = json.loads((self.outdir / "manifest.json").read_text(encoding="utf-8"))
        self.assertEqual(report["schema_version"], 1)
        self.assertEqual(report["input"]["sha256"], hashlib.sha256(self.exe.read_bytes()).hexdigest())
        self.assertTrue(report["input"]["clr_il_only"])
        self.assertEqual(report["toolchain"]["ilspycmd_version"], "9.1.0.7988")
        self.assertEqual(report["target_runtime"]["target_framework"], "unknown")
        self.assertEqual(report["file_count"], 3)
        self.assertEqual([row["path"] for row in report["files"]],
                         ["Main.cs", "GivreClone.csproj", "Sub/Feature.cs"])
        self.assertTrue(report["manifest_excluded_from_file_set"])
        for row in report["files"]:
            artifact = self.outdir / "source" / row["path"]
            self.assertEqual(row["bytes"], artifact.stat().st_size)
            self.assertEqual(row["sha256"], hashlib.sha256(artifact.read_bytes()).hexdigest())

    def test_second_invocation_refuses_overwrite_without_invoking_tool(self) -> None:
        self.recover()
        first = (self.outdir / "manifest.json").read_bytes()
        fake = FakeIlspy()
        with self.assertRaises(OfflineDecompileError):
            self.recover(fake)
        self.assertEqual(fake.calls, [])
        self.assertEqual(first, (self.outdir / "manifest.json").read_bytes())

    def test_native_and_aot_candidate_returns_report_without_outputs(self) -> None:
        self.exe.write_bytes(fixture(clr=False))
        tool = FakeIlspy()
        result = self.recover(tool)
        self.assertEqual(result["status"], "unsupported_native_or_aot")
        self.assertIn("apphost", result["reason"])
        self.assertEqual(result["source_files_created"], 0)
        self.assertEqual(tool.calls, [])
        self.assertFalse(self.outdir.exists())

    def test_mixed_mode_not_sent_to_ilspycmd(self) -> None:
        self.exe.write_bytes(fixture(clr=True, il_only=False))
        fake = FakeIlspy()
        result = self.recover(fake)
        self.assertEqual(result["status"], "unsupported_native_or_aot")
        self.assertEqual(fake.calls, [])

    def test_missing_ilspy_reports_blocked_without_writes(self) -> None:
        result = reconstruct(self.exe, self.output_rel, self.root,
                             find_tool=lambda _name: None)
        self.assertEqual(result["status"], "blocked_ilspycmd_missing")
        self.assertFalse(result["decompiler_invoked"])
        self.assertFalse(self.outdir.exists())

    def test_output_path_traversal_and_absolute_are_denied(self) -> None:
        for relative in ("../MZP0_OfflineDecompile_wrong", "/tmp/MZP0_OfflineDecompile_x",
                         "offline/../MZP0_OfflineDecompile_wrong", "offline/other",
                         "offline/MZP0_OfflineDecompile_/../MZP0_OfflineDecompile_x",
                         "MZP0_OfflineDecompile_in_root"):
            with self.subTest(path=relative):
                with self.assertRaises(OfflineDecompileError):
                    check_output(self.root, relative)

    def test_symlink_output_target_is_denied(self) -> None:
        elsewhere = self.root / "elsewhere"
        elsewhere.mkdir()
        try:
            self.outdir.symlink_to(elsewhere, target_is_directory=True)
        except (OSError, NotImplementedError):
            self.skipTest("creating junction/symlink unavailable")
        with self.assertRaises(OfflineDecompileError):
            check_output(self.root, self.output_rel)

    def test_symlink_output_parent_is_denied(self) -> None:
        elsewhere = self.root / "elsewhere"
        elsewhere.mkdir()
        link = self.root / "another"
        try:
            link.symlink_to(elsewhere, target_is_directory=True)
        except (OSError, NotImplementedError):
            self.skipTest("creating junction/symlink unavailable")
        with self.assertRaises(OfflineDecompileError):
            check_output(self.root, "another/MZP0_OfflineDecompile_fixture")

    def test_source_symlink_to_external_folder_is_denied(self) -> None:
        elsewhere = self.root.parent / "no-real-exe-here"
        link = self.root / "other"
        try:
            link.symlink_to(elsewhere, target_is_directory=True)
        except (OSError, NotImplementedError):
            self.skipTest("creating symlinks unavailable")
        with self.assertRaises((ValueError, OSError)):
            reconstruct(link / "GivreClone.exe", self.output_rel, self.root,
                        ilspy_executable="ilspycmd", runner=FakeIlspy())

    def test_tool_failure_cleans_staging_without_partial_published_output(self) -> None:
        runner = FakeIlspy(exit_code=4)
        with self.assertRaisesRegex(OfflineDecompileError, "exit 4"):
            self.recover(runner)
        self.assertFalse(self.outdir.exists())
        self.assertFalse(list(self.input_parent.glob(".MZP0_OfflineDecompile_stage_*")))

    def test_ilspy_timeout_cleans_private_staging(self) -> None:
        with self.assertRaisesRegex(OfflineDecompileError, "timed out"):
            self.recover(FakeIlspy(mode="tool_timeout"))
        self.assertFalse(self.outdir.exists())
        self.assertFalse(list(self.input_parent.glob(".MZP0_OfflineDecompile_stage_*")))

    def test_ilspy_version_required_for_provenance(self) -> None:
        with self.assertRaisesRegex(OfflineDecompileError, "version not parseable"):
            self.recover(FakeIlspy(mode="bad_version"))
        self.assertFalse(self.outdir.exists())

    def test_destination_appearing_during_run_is_never_overwritten(self) -> None:
        with self.assertRaisesRegex(OfflineDecompileError, "already exists"):
            self.recover(FakeIlspy(mode="dest_race"))
        self.assertEqual((self.outdir / "sentinel.txt").read_text(encoding="utf-8"),
                         "do not overwrite")
        self.assertFalse((self.outdir / "manifest.json").exists())
        self.assertFalse(list(self.input_parent.glob(".MZP0_OfflineDecompile_stage_*")))

    def test_source_modified_during_run_is_rejected(self) -> None:
        with self.assertRaisesRegex(OfflineDecompileError, "input changed"):
            self.recover(FakeIlspy(mode="input_changed"))
        self.assertFalse(self.outdir.exists())

    def test_project_incomplete_fails_closed(self) -> None:
        with self.assertRaisesRegex(OfflineDecompileError, "both C# sources and a .csproj"):
            self.recover(FakeIlspy(mode="missing_project"))
        self.assertFalse(self.outdir.exists())

    def test_corrupted_pe_rejected_without_tool(self) -> None:
        self.exe.write_bytes(b"MZ" + b"\x00" * 260)
        fake = FakeIlspy()
        with self.assertRaises(ValueError):
            self.recover(fake)
        self.assertEqual(fake.calls, [])
        self.assertFalse(self.outdir.exists())

    def test_decompiler_generated_symlink_rejected(self) -> None:
        with self.assertRaises((OfflineDecompileError, OSError)):
            self.recover(FakeIlspy(mode="symlink_output"))
        self.assertFalse(self.outdir.exists())

    def test_timeout_is_bounded(self) -> None:
        with self.assertRaisesRegex(OfflineDecompileError, "timeout"):
            reconstruct(self.exe, self.output_rel, self.root,
                        ilspy_executable="ilspycmd", runner=FakeIlspy(),
                        timeout_seconds=MAX_NEGATIVE_TIMEOUT)


MAX_NEGATIVE_TIMEOUT = 901


if __name__ == "__main__":
    unittest.main(verbosity=2)
