#!/usr/bin/env python3
"""Verify an explicitly detached GivreClone.exe and recover IL with MZP0 tools.

Does not search an installation or processes. No source recovery is attempted
for native/AOT/apphost PE, even if its name/version resembles GivreClone.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from MZP0_OfflinePeAudit import allowed_file, inspect_pe
from MZP0_OfflineDecompile import reconstruct


def verify(root: Path, input_path: Path | None) -> dict:
    if input_path is None:
        return {"status": "blocked_no_detached_exe", "reason": "No detached GivreClone.exe path was provided",
                "source_recovered": False, "running_installation_touched": False}
    root = root.resolve(strict=True)
    path = input_path if input_path.is_absolute() else root / input_path
    source = allowed_file(path, root)
    pe = inspect_pe(source, root)
    evidence = {"input": source.relative_to(root).as_posix(), "sha256": pe["sha256"],
                "bytes": pe["size_bytes"], "architecture": pe["architecture"],
                "pe_kind": pe["pe_kind"], "clr_header_present": pe["clr_header_present"],
                "clr_metadata_signature_valid": pe["clr_metadata_signature_valid"],
                "clr_il_only": pe["clr_il_only"], "analysis_path": pe["analysis_path"]}
    if pe["analysis_path"] != "managed_il_eligible":
        return {"status": "unsupported_native_or_aot",
                "reason": "No verified managed IL in this EXE; .NET apphost/NativeAOT/native require a separate static approach",
                "binary": evidence, "source_recovered": False,
                "running_installation_touched": False}
    relative = "GivreWork/tools/MZP0_OfflineDecompile_MZP1_" + pe["sha256"][:16]
    result = reconstruct(source, relative, root)
    return {"status": result["status"], "binary": evidence,
            "recovery": result, "source_recovered": result["status"] == "managed_source_recovered",
            "running_installation_touched": False}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, default=None,
                        help="relative or absolute detached GivreClone.exe under /givre")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    try:
        result = verify(root, args.input)
    except (ValueError, OSError) as exc:
        result = {"status": "rejected", "reason": str(exc), "source_recovered": False}
    print(json.dumps(result, indent=2, sort_keys=True, ensure_ascii=False))
    return 0 if result["status"] == "managed_source_recovered" else 2


if __name__ == "__main__":
    raise SystemExit(main())
