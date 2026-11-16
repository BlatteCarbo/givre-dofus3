"""Stage only the genuine WebAuth *application code* from the verified local ZIP.

No installed account/profile/browser-data trees, no process interaction, and
no authentication or network traffic. Intended as a read-only provenance
source for GivreClone.App's missing official WebAuth companion.
"""
from __future__ import annotations
from pathlib import Path
from hashlib import sha256
import json
import shutil
import stat
import zipfile

ROOT = Path(__file__).resolve().parents[1]
PKG = ROOT / "offline-evidence/v131-app/GivreDistribution-candidate-D66690B6.zip"
DEST = ROOT / "vendor/GivreWebAuth131"
PIN = "D66690B642204FC35B3EF2E4A3B9EF53483B41A447B0BFE3C04DFA2D38DB06FD"
FILES = (
    "GivreLauncher.WebAuth.exe",
    "GivreLauncher.WebAuth.dll",
    "GivreLauncher.WebAuth.deps.json",
    "GivreLauncher.WebAuth.runtimeconfig.json",
    "Microsoft.Web.WebView2.Core.dll",
    "Microsoft.Web.WebView2.WinForms.dll",
    "Microsoft.Web.WebView2.Wpf.dll",
    "runtimes/win-arm64/native/WebView2Loader.dll",
    "runtimes/win-x64/native/WebView2Loader.dll",
    "runtimes/win-x86/native/WebView2Loader.dll",
)


def digest(path: Path) -> str:
    h = sha256()
    with path.open("rb") as reader:
        for chunk in iter(lambda: reader.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest().upper()


def stage():
    if not PKG.is_file() or PKG.is_symlink() or digest(PKG) != PIN:
        raise ValueError("Distribution is missing or failed SHA pin")
    if DEST.exists() and DEST.is_symlink():
        raise ValueError("WebAuth vendor root symlink")
    DEST.mkdir(parents=True, exist_ok=True)
    receipts = []
    with zipfile.ZipFile(PKG) as archive:
        index = {info.filename: info for info in archive.infolist()}
        for name in FILES:
            member = "WebAuth/" + name
            info = index.get(member)
            if (info is None or info.is_dir() or info.flag_bits & 1
                    or not (1 <= info.file_size <= 15_000_000)):
                raise ValueError("Unexpected WebAuth code member: " + name)
            if stat.S_IFMT((info.external_attr >> 16) & 0xFFFF) == stat.S_IFLNK:
                raise ValueError("WebAuth code symlink")
            target = DEST / name
            if not target.resolve().is_relative_to(DEST.resolve()):
                raise ValueError("WebAuth traversal")
            target.parent.mkdir(parents=True, exist_ok=True)
            if target.parent.is_symlink() or target.is_symlink():
                raise ValueError("Reparse target")
            with archive.open(info) as stream:
                content = stream.read(info.file_size + 1)
            if len(content) != info.file_size:
                raise ValueError("WebAuth unexpected length")
            expected = sha256(content).hexdigest().upper()
            if target.exists():
                if digest(target) != expected:
                    raise ValueError("Existing staged WebAuth binary differs: " + name)
            else:
                with target.open("xb") as f:
                    f.write(content)
            receipts.append({"file": name, "bytes": len(content), "sha256": expected})
    manifest = ROOT / "offline-evidence/v131-app/MZP17-WebAuth-code-manifest.json"
    manifest.write_text(json.dumps({
        "source_zip_sha256": PIN,
        "original_binary_decompiled": False,
        "web_auth_managed_dll_decompiled": True,
        "auth_session_performed": False,
        "source_account_files_read": False,
        "files": receipts,
    }, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    print("MZP17 GENUINE WEBAUTH CODE STAGED: PASS")
    print("FILES=" + str(len(receipts)))
    print("BYTES=" + str(sum(x["bytes"] for x in receipts)))
    print("INDEPENDENT_PACKAGE_SHA256=" + PIN)
    print("PROVENANCE_MANIFEST=" + str(manifest))


if __name__ == "__main__":
    stage()
