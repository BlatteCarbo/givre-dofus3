#!/usr/bin/env python3
"""Read-only bounded triage of the explicitly detached GivreClone 1.3.1 setup.

No execution, loading native code, touching installed/running GivreClone or
reading an account/configuration. URLs are reported only as origins/paths,
without query strings or fragments (which could contain secrets).
"""
import hashlib
import json
import re
from pathlib import Path
from urllib.parse import urlsplit

import pefile

HERE = Path(__file__).resolve().parents[1]
OFFLINE = HERE / "offline-evidence" / "setup-1.3.1-4D4D6458"
SOURCE = OFFLINE / "GivreClone-Setup-1.3.1.exe"
EXPECTED = "4D4D6458A0B808173749EB98D9FC3B199748B707CEFD8C7319809769C1DF6E83"


def inspect():
    if not SOURCE.is_file() or SOURCE.is_symlink() or not SOURCE.resolve().is_relative_to(OFFLINE.resolve()):
        raise ValueError("Expected offline setup copy is absent or unsafe")
    content = SOURCE.read_bytes()
    if len(content) < 256 or len(content) > 32 * 1024 * 1024:
        raise ValueError("Installer triage size limit")
    digest = hashlib.sha256(content).hexdigest().upper()
    if digest != EXPECTED:
        raise ValueError("Installer SHA mismatch; refusing unexpected bytes")
    pe = pefile.PE(data=content, fast_load=False)
    try:
        is_dotnet = hasattr(pe, "DIRECTORY_ENTRY_COM_DESCRIPTOR")
        imports = {}
        for group in getattr(pe, "DIRECTORY_ENTRY_IMPORT", []):
            library = group.dll.decode("ascii", "replace")
            imports[library] = [
                entry.name.decode("ascii", "replace") if entry.name else "#" + str(entry.ordinal)
                for entry in group.imports
            ]
        # Restrict output to useful high-level installer/build hints.
        regular = re.findall(rb"[\x20-\x7e]{7,300}", content)
        wide = re.findall(rb"(?:[\x20-\x7e]\x00){7,300}", content)
        strings = [s.decode("ascii", "replace") for s in regular]
        strings.extend(s[::2].decode("ascii", "replace") for s in wide)
        url_regex = re.compile(r"https?://[^\s\"'<>\\]{5,230}", re.I)
        endpoints = set()
        for s in strings:
            for match in url_regex.finditer(s):
                item = urlsplit(match.group())
                if item.hostname and item.scheme in ("https", "http"):
                    endpoints.add(item.scheme + "://" + item.netloc + item.path.rstrip(").,;"))
        needles = ("install", "update", "squirrel", "tauri", "electron", "wix",
                   "nsis", "inno", "bundle", "download", "github", "msi",
                   "release", "givre", "webview", "rust", "boost", "cytrus",
                   "version", "setup", "fetch")
        hints = sorted({s[:160] for s in strings if len(s) <= 160
                        and any(n in s.lower() for n in needles)
                        and not url_regex.search(s) and "=" not in s})
        size = len(content)
        overlay_at = pe.get_overlay_data_start_offset()
        report = {
            "status": "STATIC_SETUP_PE_ONLY",
            "version_claim_in_filename": "1.3.1 (NOT binary identity)",
            "size_bytes": size,
            "sha256": digest,
            "machine_hex": f"0x{pe.FILE_HEADER.Machine:04X}",
            "sections": [s.Name.rstrip(b"\x00").decode("ascii", "replace")
                         for s in pe.sections],
            "clr_directory_present": is_dotnet,
            "overlay_bytes": size - overlay_at if overlay_at is not None else 0,
            "imported_modules_and_functions": imports,
            "url_origins_and_paths_without_queries": sorted(endpoints)[:45],
            "keyword_hints_bounded": hints[:80],
            "original_GivreClone_running_installation_touched": False,
            "installer_executed": False,
            "app_payload_extracted": False,
        }
        return report
    finally:
        pe.close()


if __name__ == "__main__":
    print(json.dumps(inspect(), indent=2, ensure_ascii=False, sort_keys=True))
