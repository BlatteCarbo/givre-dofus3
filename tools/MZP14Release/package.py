"""Deterministic MZP14 built App + source bundle, no secrets or original client."""
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
from MZP13Release.package import SOURCE_FILES as PRIOR

binary.ARCHIVE = binary.ARTIFACTS / "MZP14-Historical-GivreCloneApp-WinX64-OFFLINE-ONLY.zip"
binary.MANIFEST = binary.ARTIFACTS / "MZP14-Historical-App-Manifest.json"
binary.README = (
    "MZP14 historical GivreClone.App Windows x64 Release, not a certified clone.\n"
    "Verified structural readers: admitted map and character selection.\n"
    "No authenticated Dofus 3.7.11.11 session, ACK, combat or gameplay.\n"
    "Original GivreClone v1.3.1 detached EXE absent; source parity NOT proven.\n"
    "Original three active bots/install/accounts remained untouched.\n"
)
SOURCE_FILES = tuple(dict.fromkeys(PRIOR + (
    "GivreClone.Engine/MZP14VerifiedCharacterReader.cs",
    "tools/MZP14CharacterHarness/MZP14CharacterHarness.csproj",
    "tools/MZP14CharacterHarness/Program.cs",
    "tools/MZP14Release/package.py",
    "docs/MZP14_PRIME_HANDOFF_20261016.md",
)))

def package_source():
    dest = binary.ARTIFACTS / "MZP14-Source-Delta-OFFLINE-ONLY.zip"
    files = []
    with zipfile.ZipFile(dest, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for relative in SOURCE_FILES:
            path = ROOT / relative
            if not path.is_file() or path.is_symlink() or path.stat().st_size > 2_000_000:
                raise ValueError("Unexpected source: " + relative)
            if not path.resolve().is_relative_to(ROOT.resolve()):
                raise ValueError("Source escapes workspace")
            raw = path.read_bytes()
            files.append({"path": relative, "bytes": len(raw),
                          "sha256": hashlib.sha256(raw).hexdigest()})
            info = zipfile.ZipInfo(relative, binary.STAMP)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = (stat.S_IFREG | 0o644) << 16
            z.writestr(info, raw, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
        meta = (json.dumps({"playable_certified": False, "original_source_decompiled": False,
                            "files": files}, indent=2, sort_keys=True) + "\n").encode()
        info = zipfile.ZipInfo("SOURCE_SHA256_MANIFEST.json", binary.STAMP)
        info.compress_type = zipfile.ZIP_DEFLATED
        z.writestr(info, meta, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
    with zipfile.ZipFile(dest) as z:
        if z.testzip() is not None: raise ValueError("Bad archive")
    print("SOURCE_ZIP=" + str(dest))
    print("SOURCE_SHA256=" + hashlib.sha256(dest.read_bytes()).hexdigest())
    print("SOURCE_BYTES=" + str(dest.stat().st_size))

if __name__ == "__main__":
    binary.main()
    package_source()
