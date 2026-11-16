#!/usr/bin/env python3
"""Recover a project from an explicitly detached, managed-IL GivreClone.exe.

Input and output must remain under the approved /givre root. Only the
already-installed ilspycmd may be invoked; the target EXE is never executed.
All generated sources stay in a new, isolated directory with SHA-256 evidence.
This is a static recovery utility, not a claim of functional EXE parity.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform
import re
import shutil
import stat
import struct
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Callable

from MZP0_OfflinePeAudit import InvalidOfflineArtifact, allowed_file, inspect_pe


MAX_FILES = 20_000
MAX_FILE_BYTES = 128 * 1024 * 1024
MAX_TOTAL_BYTES = 1024 * 1024 * 1024
MAX_TOOL_SECONDS = 900
MAX_VERSION_SECONDS = 15
OUTPUT_PREFIX = "MZP0_OfflineDecompile_"
Runner = Callable[..., subprocess.CompletedProcess[str]]


class OfflineDecompileError(ValueError):
    """Source or destination fails the offline security contract."""


def _is_reparse(path: Path) -> bool:
    if path.is_symlink() or getattr(path, "is_junction", lambda: False)():
        return True
    try:
        state = path.stat(follow_symlinks=False)
    except FileNotFoundError:
        return False
    return bool(getattr(state, "st_file_attributes", 0) & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0))


def check_output(root: Path, relative_output: str) -> Path:
    """Require an unoccupied destination with an existing trusted parent."""
    canonical_root = root.resolve(strict=True)
    relative = Path(relative_output)
    if (not relative_output or relative.is_absolute() or relative.drive or
        any(part in (".", "..") for part in relative_output.replace("\\", "/").split("/")) or
        relative.name in ("", ".", "..") or
        not relative.name.startswith(OUTPUT_PREFIX) or len(relative.parts) < 2):
        raise OfflineDecompileError("output must be a new MZP0_OfflineDecompile_* directory below /givre")
    target = canonical_root / relative
    parent = target.parent
    if not parent.is_dir():
        raise OfflineDecompileError("output parent must already exist")
    current = parent
    while current != canonical_root:
        if not current.exists() or _is_reparse(current):
            raise OfflineDecompileError("output path has a missing or reparse-point ancestor")
        if current.parent == current:
            raise OfflineDecompileError("output outside approved root")
        current = current.parent
    if parent.resolve(strict=True) != parent or not parent.resolve(strict=True).is_relative_to(canonical_root):
        raise OfflineDecompileError("output parent resolves outside approved root")
    if target.exists() or target.is_symlink() or _is_reparse(target):
        raise OfflineDecompileError("output already exists; overwriting is forbidden")
    if target.resolve(strict=False) != target:
        raise OfflineDecompileError("output destination is not canonical")
    return target


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as data:
        for chunk in iter(lambda: data.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _cli_header_version(path: Path, pe: dict) -> dict:
    """Read the two COR20 runtime schema fields, not the target .NET version."""
    if not pe["clr_header_present"]:
        return {"cli_header_version": None, "target_framework": "unknown"}
    with path.open("rb") as data:
        data.seek(0x3C)
        pe_offset = struct.unpack("<I", data.read(4))[0]
        data.seek(pe_offset + 20)
        optional_size = struct.unpack("<H", data.read(2))[0]
        optional_start = pe_offset + 24
        data.seek(optional_start)
        opt = data.read(optional_size)
        pe32plus = struct.unpack_from("<H", opt, 0)[0] == 0x20B
        directory_start = 112 if pe32plus else 96
        cli_rva = struct.unpack_from("<I", opt, directory_start + 14 * 8)[0]
        for section in pe["sections"]:
            offset = cli_rva - section["rva"]
            if 0 <= offset and offset + 8 <= section["raw_size"]:
                data.seek(section["raw_offset"] + offset + 4)
                major, minor = struct.unpack("<HH", data.read(4))
                return {"cli_header_version": f"{major}.{minor}",
                        "target_framework": "unknown",
                        "caution": "CLI header version is not .NET TargetFramework"}
    raise OfflineDecompileError("valid CLR header could not be mapped into a section")


def _tool_version(tool: str, runner: Runner) -> str:
    try:
        result = runner([tool, "--version"], capture_output=True, text=True,
                        timeout=MAX_VERSION_SECONDS, check=False)
    except (OSError, subprocess.TimeoutExpired) as exception:
        raise OfflineDecompileError("ilspycmd --version failed") from exception
    if result.returncode != 0:
        raise OfflineDecompileError("ilspycmd --version failed")
    summary = (result.stdout or "") + "\n" + (result.stderr or "")
    match = re.search(r"\b(\d+\.\d+(?:\.\d+){1,3})\b", summary[:4096])
    if not match:
        raise OfflineDecompileError("ilspycmd version not parseable; refusing untracked tool")
    return match.group(1)


def _project_files(folder: Path) -> list[dict]:
    if _is_reparse(folder):
        raise OfflineDecompileError("decompiler output directory is a link or junction")
    results: list[dict] = []
    total_bytes = 0
    files_seen = 0
    cs_found = False
    project_found = False
    for current, directories, filenames in os.walk(folder, topdown=True, followlinks=False):
        current_path = Path(current)
        for name in directories + filenames:
            entry = current_path / name
            if _is_reparse(entry):
                raise OfflineDecompileError("decompiler output includes a link or junction")
        directories.sort()
        for name in sorted(filenames):
            file_path = current_path / name
            if not file_path.is_file():
                raise OfflineDecompileError("decompiler output has an unsupported filesystem item")
            size = file_path.stat().st_size
            files_seen += 1
            total_bytes += size
            if files_seen > MAX_FILES or size > MAX_FILE_BYTES or total_bytes > MAX_TOTAL_BYTES:
                raise OfflineDecompileError("decompiled output exceeds bounded file or byte limits")
            relative = file_path.relative_to(folder).as_posix()
            cs_found |= file_path.suffix.lower() == ".cs"
            project_found |= file_path.suffix.lower() == ".csproj"
            results.append({"path": relative, "bytes": size, "sha256": _sha256(file_path)})
    if not cs_found or not project_found:
        raise OfflineDecompileError("ilspycmd did not create both C# sources and a .csproj")
    return sorted(results, key=lambda item: item["path"])


def _report_blocked(pe: dict, status: str, reason: str) -> dict:
    return {"schema_version": 1, "status": status, "reason": reason,
            "input_sha256": pe["sha256"], "input_bytes": pe["size_bytes"],
            "pe_analysis_path": pe["analysis_path"],
            "decompiler_invoked": False, "exe_executed": False,
            "source_files_created": 0, "output": None}


def reconstruct(input_path: Path, output_relative: str, approved_root: Path,
                *, ilspy_executable: str | None = None,
                find_tool: Callable[[str], str | None] = shutil.which,
                runner: Runner = subprocess.run,
                timeout_seconds: int = 600) -> dict:
    """Produce a decompiled project and manifest, or an honest blocked result.

    Dependency injection exists for offline tests. The command-line interface
    does not expose an executable override; it uses PATH's installed ilspycmd.
    """
    if not 1 <= timeout_seconds <= MAX_TOOL_SECONDS:
        raise OfflineDecompileError("timeout must be between 1 and 900 seconds")
    root = approved_root.resolve(strict=True)
    source = allowed_file(input_path, root)
    destination = check_output(root, output_relative)
    pe = inspect_pe(source, root)
    if pe["analysis_path"] != "managed_il_eligible":
        return _report_blocked(pe, "unsupported_native_or_aot",
                               "No verified IL-only CLR metadata. Native, apphost, AOT and protected "
                               "PE cannot be converted to C# by ilspycmd from this EXE; "
                               "a separate static native recovery workflow is required.")
    tool = ilspy_executable if ilspy_executable is not None else find_tool("ilspycmd")
    if not tool:
        return _report_blocked(pe, "blocked_ilspycmd_missing",
                               "ilspycmd was not found; install it explicitly as a dotnet tool "
                               "and rerun this offline command. No installation was attempted.")
    tool_version = _tool_version(tool, runner)
    parent = destination.parent
    stage = Path(tempfile.mkdtemp(prefix=".MZP0_OfflineDecompile_stage_", dir=parent))
    try:
        recovered = stage / "source"
        recovered.mkdir()
        args = [tool, "-p", "-o", str(recovered), str(source)]
        try:
            completed = runner(args, capture_output=True, text=True,
                               timeout=timeout_seconds, check=False)
        except (OSError, subprocess.TimeoutExpired) as exception:
            raise OfflineDecompileError("ilspycmd execution failed or timed out") from exception
        if completed.returncode != 0:
            raise OfflineDecompileError(f"ilspycmd rejected the offline binary (exit {completed.returncode})")
        files = _project_files(recovered)
        if any(child.name != "source" for child in stage.iterdir()):
            raise OfflineDecompileError("unexpected files outside decompiler output folder")
        post_audit = inspect_pe(source, root)
        if post_audit["sha256"] != pe["sha256"] or post_audit["size_bytes"] != pe["size_bytes"]:
            raise OfflineDecompileError("offline input changed while being decompiled")
        # The manifest is intentionally not hashed inside its own file set.
        report = {
            "schema_version": 1, "status": "managed_source_recovered",
            "scope": "static_detached_executable_only",
            "input": {"relative_path": source.relative_to(root).as_posix(),
                      "bytes": pe["size_bytes"], "sha256": pe["sha256"],
                      "architecture": pe["architecture"], "pe_kind": pe["pe_kind"],
                      "clr_header_present": pe["clr_header_present"],
                      "clr_metadata_signature_valid": pe["clr_metadata_signature_valid"],
                      "clr_il_only": pe["clr_il_only"]},
            "target_runtime": _cli_header_version(source, pe),
            "toolchain": {"ilspycmd_version": tool_version,
                          "python_version": platform.python_version(),
                          "host_platform": platform.system(),
                          "decompile_command": ["ilspycmd", "-p", "-o", "source", "<offline-GivreClone.exe>"]},
            "output": destination.relative_to(root).as_posix(),
            "files_root": "source", "file_count": len(files), "files": files,
            "manifest_excluded_from_file_set": True,
            "exe_executed": False, "process_or_installed_exe_touched": False,
            "feature_equivalence_certified": False,
            "note": "Recovered C# is decompiler output; inspect and compile independently. "
                    "This is not proven equivalent to a running GivreClone version."}
        (stage / "manifest.json").write_text(
            json.dumps(report, ensure_ascii=False, sort_keys=True, indent=2) + "\n", encoding="utf-8")
        # Preserve no-overwrite: recheck then rename the entirely private tree.
        check_output(root, output_relative)
        os.rename(stage, destination)
        return {"schema_version": 1, "status": "managed_source_recovered",
                "output": destination.relative_to(root).as_posix(),
                "input_sha256": pe["sha256"], "file_count": len(files),
                "manifest": f"{destination.relative_to(root).as_posix()}/manifest.json",
                "exe_executed": False, "feature_equivalence_certified": False}
    finally:
        if stage.is_dir() and not _is_reparse(stage):
            shutil.rmtree(stage)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True,
                        help="explicit detached/offline GivreClone.exe inside /givre")
    parser.add_argument("--output", required=True,
                        help="NEW directory relative to /givre, e.g. GivreWork/MZP0_OfflineDecompile_v131")
    parser.add_argument("--timeout-seconds", type=int, default=600)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    try:
        result = reconstruct(args.input, args.output, root,
                             timeout_seconds=args.timeout_seconds)
    except (InvalidOfflineArtifact, OfflineDecompileError, FileExistsError,
            FileNotFoundError, PermissionError, OSError) as exc:
        print(json.dumps({"schema_version": 1, "status": "rejected",
                          "reason": str(exc), "exe_executed": False},
                         sort_keys=True, ensure_ascii=False))
        return 3
    print(json.dumps(result, ensure_ascii=False, sort_keys=True, indent=2))
    return 0 if result["status"] == "managed_source_recovered" else 2


if __name__ == "__main__":
    sys.exit(main())
