"""Deterministic OFFLINE source+historical Windows App bundles, never gameplay certified."""
from pathlib import Path
import hashlib
import json
import stat
import sys
import zipfile

TOOLS = Path(__file__).resolve().parents[1]
ROOT = TOOLS.parent
sys.path.insert(0, str(TOOLS))
from MZP8Release import package as binary
from MZP11Release.package import SOURCE_FILES as PRIOR_SOURCE

binary.ARCHIVE = binary.ARTIFACTS / "MZP12-Historical-GivreCloneApp-WinX64-NOT-GAME-CERTIFIED.zip"
binary.MANIFEST = binary.ARTIFACTS / "MZP12-Historical-App-Manifest.json"
binary.README = (
    "MZP12 historical GivreClone.App Windows x64 Release: OFFLINE ONLY.\n"
    "NOT an authenticated or playable GivreClone v1.3.1 clone.\n"
    "Mapping build / installed Cytrus version / manifest SHA must agree.\n"
    "Server required_version drift refuses old mapping reuse.\n"
    "No original GivreClone accounts, sessions, game process or token included.\n"
    "No live game character, map, ACK or gameplay verified.\n"
)
SOURCE_FILES = tuple(dict.fromkeys(PRIOR_SOURCE + (
    "GivreClone.App/Services/MZP12SocketVersionBinding.cs",
    "GivreClone.App/Services/Transport/Socket/SocketProbe.cs",
    "tools/MZP12SocketBindingHarness/MZP12SocketBindingHarness.csproj",
    "tools/MZP12SocketBindingHarness/Program.cs",
    "tools/MZP8Release/package.py",
    "tools/MZP11Release/package.py",
    "tools/MZP12Release/package.py",
    "docs/MZP12_PRIME_HANDOFF_20261016.md",
)))

def source_package():
    path = binary.ARTIFACTS / "MZP12-Source-Delta-OFFLINE-ONLY.zip"
    entries = []
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for name in SOURCE_FILES:
            file = ROOT / name
            if not file.is_file() or file.is_symlink() or file.stat().st_size > 2_000_000:
                raise ValueError("Unsafe or absent source: " + name)
            if not file.resolve().is_relative_to(ROOT.resolve()):
                raise ValueError("File outside source root")
            contents = file.read_bytes()
            entries.append({"path": name, "bytes": len(contents),
                            "sha256": hashlib.sha256(contents).hexdigest()})
            info = zipfile.ZipInfo(name, binary.STAMP)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = (stat.S_IFREG | 0o644) << 16
            z.writestr(info, contents, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
        meta = (json.dumps({"gameplay_verified": False, "files": entries},
                           sort_keys=True, indent=2) + "\n").encode("utf8")
        info = zipfile.ZipInfo("SOURCE_SHA256_MANIFEST.json", binary.STAMP)
        info.compress_type = zipfile.ZIP_DEFLATED
        z.writestr(info, meta, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
    with zipfile.ZipFile(path) as z:
        if z.testzip() is not None:
            raise ValueError("Corrupt source archive")
    print("SOURCE_ZIP=" + str(path))
    print("SOURCE_SHA256=" + hashlib.sha256(path.read_bytes()).hexdigest())
    print("SOURCE_BYTES=" + str(path.stat().st_size))

if __name__ == "__main__":
    binary.main()
    source_package()
