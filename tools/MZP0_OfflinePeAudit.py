#!/usr/bin/env python3
"""Read-only static PE triage for an explicitly supplied OFFLINE GivreClone.exe.

The CLI accepts files only beneath the approved /givre tree, rejects links,
never executes the file and never reads a running installation or memory dump.
No PE sections, IL, strings or disassembly are exported: only metadata/hash.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import struct
from pathlib import Path


MAX_PE_SIZE = 512 * 1024 * 1024
ARCH = {0x014C: "x86", 0x8664: "x64", 0xAA64: "arm64"}


class InvalidOfflineArtifact(ValueError):
    pass


def allowed_file(path: Path, allowed_root: Path) -> Path:
    """Resolve under a fixed authority root and reject renamed/dump/live assets."""
    root = allowed_root.resolve(strict=True)
    candidate = path.resolve(strict=True)
    if not candidate.is_relative_to(root) or path.name != "GivreClone.exe":
        raise InvalidOfflineArtifact("file must be a copy of GivreClone.exe within approved root")
    current = path.absolute()
    while current != root:
        if current.is_symlink():
            raise InvalidOfflineArtifact("symlinks are not permitted")
        if current.parent == current:
            raise InvalidOfflineArtifact("path is outside approved root")
        current = current.parent
    if not candidate.is_file() or candidate.stat().st_size > MAX_PE_SIZE:
        raise InvalidOfflineArtifact("file absent or exceeds bounded offline size")
    return candidate


def _u16(blob: bytes, offset: int) -> int:
    if offset < 0 or offset + 2 > len(blob):
        raise InvalidOfflineArtifact("truncated PE metadata")
    return struct.unpack_from("<H", blob, offset)[0]


def _u32(blob: bytes, offset: int) -> int:
    if offset < 0 or offset + 4 > len(blob):
        raise InvalidOfflineArtifact("truncated PE metadata")
    return struct.unpack_from("<I", blob, offset)[0]


def _read_at(stream, offset: int, count: int, length: int) -> bytes:
    if offset < 0 or count < 0 or offset + count > length:
        raise InvalidOfflineArtifact("PE offset outside file")
    stream.seek(offset)
    data = stream.read(count)
    if len(data) != count:
        raise InvalidOfflineArtifact("truncated PE file")
    return data


def inspect_pe(path: Path, allowed_root: Path) -> dict:
    file_path = allowed_file(path, allowed_root)
    length = file_path.stat().st_size
    if length < 256:
        raise InvalidOfflineArtifact("truncated DOS/COFF header")
    with file_path.open("rb") as handle:
        dos = _read_at(handle, 0, 64, length)
        if dos[:2] != b"MZ":
            raise InvalidOfflineArtifact("not an MZ executable")
        pe_offset = _u32(dos, 0x3C)
        if not 0x40 <= pe_offset <= min(length - 24, 8 * 1024 * 1024):
            raise InvalidOfflineArtifact("invalid PE signature position")
        coff = _read_at(handle, pe_offset, 24, length)
        if coff[:4] != b"PE\x00\x00":
            raise InvalidOfflineArtifact("invalid PE signature")
        machine = _u16(coff, 4)
        sections_count = _u16(coff, 6)
        optional_size = _u16(coff, 20)
        if sections_count not in range(1, 97) or optional_size not in range(96, 4097):
            raise InvalidOfflineArtifact("invalid PE section/optional-header size")
        optional_offset = pe_offset + 24
        optional_header = _read_at(handle, optional_offset, optional_size, length)
        magic = _u16(optional_header, 0)
        if magic not in (0x10B, 0x20B):
            raise InvalidOfflineArtifact("unsupported optional header")
        directory_offset = 96 if magic == 0x10B else 112
        directory_count_offset = 92 if magic == 0x10B else 108
        directory_count = _u32(optional_header, directory_count_offset)
        if directory_count > 128 or directory_offset + min(directory_count, 16) * 8 > optional_size:
            raise InvalidOfflineArtifact("invalid PE data directory size")
        section_offset = optional_offset + optional_size
        section_bytes = _read_at(handle, section_offset, sections_count * 40, length)
        sections = []
        raw_intervals = []
        for index in range(sections_count):
            start = 40 * index
            header = section_bytes[start:start + 40]
            label_raw = header[:8].rstrip(b"\x00")
            label = label_raw.decode("ascii", "replace")
            if not label or not all(c.isascii() and (c.isalnum() or c in "._$-") for c in label):
                label = "<nonstandard>"
            virtual_size, virtual_address, raw_size, raw_ptr = struct.unpack_from("<IIII", header, 8)
            if raw_size:
                if raw_ptr < section_offset + sections_count * 40 or raw_ptr + raw_size > length:
                    raise InvalidOfflineArtifact("PE section points outside file or headers")
                raw_intervals.append((raw_ptr, raw_ptr + raw_size))
            sections.append({"name": label, "rva": virtual_address,
                             "virtual_size": virtual_size, "raw_size": raw_size,
                             "raw_offset": raw_ptr})
        # Reject raw byte overlaps; section table entries alone are not proof of functionality.
        raw_intervals.sort()
        if any(right[0] < left[1] for left, right in zip(raw_intervals, raw_intervals[1:])):
            raise InvalidOfflineArtifact("overlapping PE raw sections")

        def rva_to_file(rva: int, count: int) -> int:
            for sec in sections:
                delta = rva - sec["rva"]
                if 0 <= delta and delta + count <= sec["raw_size"]:
                    return sec["raw_offset"] + delta
            raise InvalidOfflineArtifact("CLR data RVA not backed by a file section")

        clr_rva = clr_size = 0
        if directory_count >= 15:
            clr_rva = _u32(optional_header, directory_offset + 14 * 8)
            clr_size = _u32(optional_header, directory_offset + 14 * 8 + 4)
            if bool(clr_rva) != bool(clr_size) or (clr_rva and clr_size < 0x48):
                raise InvalidOfflineArtifact("incomplete CLR data directory")
        clr_present = clr_rva > 0 and clr_size >= 0x48
        clr_metadata_valid = False
        il_only = False
        if clr_present:
            clr = _read_at(handle, rva_to_file(clr_rva, 0x48), 0x48, length)
            if _u32(clr, 0) < 0x48:
                raise InvalidOfflineArtifact("truncated CLR header")
            meta_rva = _u32(clr, 8)
            meta_size = _u32(clr, 12)
            if meta_size >= 16:
                signature = _read_at(handle, rva_to_file(meta_rva, 4), 4, length)
                clr_metadata_valid = signature == b"BSJB"
            il_only = bool(_u32(clr, 16) & 1)
        qualification = ("managed_il_eligible" if clr_present and clr_metadata_valid and il_only
                         else "native_or_aot_requires_static_recovery")
        handle.seek(0)
        digest = hashlib.sha256()
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return {
        "classification": "offline_static_pe_header_only",
        "file": "GivreClone.exe", "size_bytes": length, "sha256": digest.hexdigest(),
        "architecture": ARCH.get(machine, f"other_0x{machine:04x}"),
        "pe_kind": "PE32+" if magic == 0x20B else "PE32",
        "sections": sections,
        "clr_header_present": clr_present,
        "clr_metadata_signature_valid": clr_metadata_valid,
        "clr_il_only": il_only,
        "analysis_path": qualification,
        "ilspycmd_available": shutil.which("ilspycmd") is not None,
        "binary_executed": False,
        "process_or_installed_exe_touched": False,
        "exe_feature_proof": "none",
        "note": "PE identity alone cannot identify a specific running window or certify features.",
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, type=Path,
                        help="explicit detached/offline GivreClone.exe inside approved /givre root")
    args = parser.parse_args()
    authority = Path(__file__).resolve().parents[2]
    try:
        report = inspect_pe(args.input, authority)
    except (InvalidOfflineArtifact, OSError) as exception:
        parser.error(f"offline artifact rejected: {exception}")
    print(json.dumps(report, indent=2, sort_keys=True, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
