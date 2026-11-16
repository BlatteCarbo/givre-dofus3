"""Safely extract only executable code from the locally found Givre distribution.

Source is the SHA-pinned, already-isolated copy of Givre/update/pkg.zip.
No installers executed, no profile/account/config/log/secret files read, no
network or running-process interaction. Never writes into LocalAppData/Givre.
"""
from pathlib import Path
import hashlib
import json
import shutil
import stat
import zipfile

PROJECT = Path(__file__).resolve().parents[1]
ROOT = PROJECT / "offline-evidence" / "v131-app"
PACKAGE = ROOT / "GivreDistribution-candidate-D66690B6.zip"
TARGET = ROOT / "verified-binaries"
PACKAGE_SHA = "D66690B642204FC35B3EF2E4A3B9EF53483B41A447B0BFE3C04DFA2D38DB06FD"
ALLOWED = {
    "GivreClone.exe", "GivreKama.dll", "GivreMods.dll", "GivreMcp.exe",
    "WebAuth/GivreLauncher.WebAuth.dll", "WebAuth/GivreLauncher.WebAuth.exe",
    "WebAuth/Microsoft.Web.WebView2.Core.dll",
    "WebAuth/Microsoft.Web.WebView2.WinForms.dll",
    "WebAuth/Microsoft.Web.WebView2.Wpf.dll",
}
EXPECTED_INSTALLED_EXE_SHA = "21C9DE5BA4D15763E9DAF029FD7432E3574EA113C9D1F44175FA9E0590290758"


def check_path(root: Path, path: Path):
    if not path.resolve().is_relative_to(root.resolve()):
        raise ValueError("Path traversal/refused path")


def sha_stream(reader):
    h = hashlib.sha256()
    total = 0
    while True:
        block = reader.read(1024 * 1024)
        if not block:
            break
        total += len(block)
        if total > 100_000_000:
            raise ValueError("Stream exceeds executable size bound")
        h.update(block)
    return h.hexdigest().upper(), total


def main():
    if PACKAGE.is_symlink() or not PACKAGE.is_file():
        raise ValueError("Isolated distribution ZIP missing")
    with PACKAGE.open("rb") as reader:
        sha, size = sha_stream(reader)
    if sha != PACKAGE_SHA or size != 93_234_109:
        raise ValueError("Isolated ZIP not the previously witnessed distribution")

    if TARGET.exists() and TARGET.is_symlink():
        raise ValueError("Refuse linked output tree")
    TARGET.mkdir(parents=True, exist_ok=True)
    report = {"distribution_sha256": sha, "source_bytes": size,
              "exact_v131_version_proven": False,
              "original_installation_modified": False,
              "processes_interacted": False, "binaries": []}
    with zipfile.ZipFile(PACKAGE, "r") as package:
        infos = {info.filename: info for info in package.infolist()}
        if len(infos) != 22 or not ALLOWED.issubset(infos):
            raise ValueError("Unexpected package manifest")
        for name in sorted(ALLOWED):
            info = infos[name]
            if info.flag_bits & 1 or info.file_size > 100_000_000 or info.is_dir():
                raise ValueError("Unsafe binary member: " + name)
            # Block symlink members encoded with Unix external attributes.
            if stat.S_IFMT((info.external_attr >> 16) & 0xffff) == stat.S_IFLNK:
                raise ValueError("Symlink in package: " + name)
            destination = TARGET / name
            check_path(TARGET, destination)
            destination.parent.mkdir(parents=True, exist_ok=True)
            for component in (destination.parent, TARGET):
                if component.is_symlink():
                    raise ValueError("Linked output parent")
            if destination.exists():
                with destination.open("rb") as f:
                    existing_sha, existing_size = sha_stream(f)
                with package.open(info, "r") as f:
                    expected_sha, expected_size = sha_stream(f)
                if existing_sha != expected_sha or existing_size != expected_size:
                    raise ValueError("Existing extracted file mismatch: " + name)
            else:
                with package.open(info, "r") as src:
                    with destination.open("xb") as dst:
                        shutil.copyfileobj(src, dst, length=1024 * 1024)
                with destination.open("rb") as f:
                    existing_sha, existing_size = sha_stream(f)
                with package.open(info, "r") as f:
                    expected_sha, expected_size = sha_stream(f)
                if existing_sha != expected_sha or existing_size != expected_size:
                    raise ValueError("Extracted file checksum mismatch")
            if name == "GivreClone.exe" and existing_sha != EXPECTED_INSTALLED_EXE_SHA:
                raise ValueError("Distribution differs from the installed GivreClone.exe SHA")
            report["binaries"].append({
                "path": name, "bytes": existing_size, "sha256": existing_sha
            })

    manifest = ROOT / "MZP17-code-only-sha256.json"
    manifest.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n",
                        encoding="utf-8")
    print("MZP17 verified code-only distribution: PASS")
    print("PACKAGE_SHA256=" + sha)
    print("BINARIES=" + str(len(report["binaries"])))
    for item in report["binaries"]:
        print(f"{item['path']}|{item['bytes']}|{item['sha256']}")
    print("NO_CREDENTIALS_OR_PROCESSES_ACCESSED=True")


if __name__ == "__main__":
    main()
