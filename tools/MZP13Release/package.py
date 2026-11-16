"""Deterministic, limited provenance package of MZP13: not a playable clone."""
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
from MZP12Release.package import SOURCE_FILES as MZP12_FILES

binary.ARCHIVE = binary.ARTIFACTS / "MZP13-Historical-GivreCloneApp-WinX64-OFFLINE-ONLY.zip"
binary.MANIFEST = binary.ARTIFACTS / "MZP13-Historical-App-Manifest.json"
binary.README = (
    "MZP13 historical GivreClone.App Windows x64 Release (2026-10-16).\n"
    "This compiles original project source with RECONSTRUCTED Core/Launcher.\n"
    "It is NOT the exact decompiled v1.3.1 code and is not gameplay certified.\n"
    "MZP13: protected state reader folds map from wire-confirmed fields.json.\n"
    "Revoked sessions erase map/cell/character/fight state.\n"
    "No detached genuine v1.3.1 executable has been authenticated for audit.\n"
    "No Dofus authenticated login, server ACK, character/map/gameplay observed.\n"
    "Original GivreClone and its three bots must not be touched.\n"
    "No game manifest, private capture, account, token or original install copied.\n"
)

SOURCE_FILES = tuple(dict.fromkeys(MZP12_FILES + (
    "GivreClone.Engine/GameState.cs",
    "GivreClone.Engine/MZP13VerifiedMapReader.cs",
    "GivreClone.Engine/MZP13TestVisibility.cs",
    "GivreClone.Engine/Recovery/MZP1GuardedEngineWiring.cs",
    "tools/MZP13StateHarness/MZP13StateHarness.csproj",
    "tools/MZP13StateHarness/Program.cs",
    "tools/MZP1GuardedWiringHarness/MZP1GuardedWiringHarness.csproj",
    "tools/MZP1GuardedWiringHarness/MZP13GuardedStateTests.cs",
    "tools/MZP2HostAdmissionHarness/MZP2HostAdmissionHarness.csproj",
    "tools/MZP13Release/package.py",
    "docs/MZP13_PRIME_HANDOFF_20261016.md",
)))

def source_package():
    dest = binary.ARTIFACTS / "MZP13-Source-Delta-OFFLINE-ONLY.zip"
    manifest = []
    with zipfile.ZipFile(dest, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name in SOURCE_FILES:
            path = ROOT / name
            if not path.is_file() or path.is_symlink() or path.stat().st_size > 2_000_000:
                raise ValueError("Unsafe or missing source " + name)
            if not path.resolve().is_relative_to(ROOT.resolve()):
                raise ValueError("Source escapes workspace")
            content = path.read_bytes()
            manifest.append({"path": name, "bytes": len(content),
                             "sha256": hashlib.sha256(content).hexdigest()})
            info = zipfile.ZipInfo(name, binary.STAMP)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = (stat.S_IFREG | 0o644) << 16
            archive.writestr(info, content, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
        metadata = (json.dumps({"original_v131_decompiled": False,
                                "authenticated_gameplay_verified": False,
                                "files": manifest}, sort_keys=True, indent=2) + "\n").encode()
        info = zipfile.ZipInfo("SOURCE_SHA256_MANIFEST.json", binary.STAMP)
        info.compress_type = zipfile.ZIP_DEFLATED
        archive.writestr(info, metadata, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
    with zipfile.ZipFile(dest) as archive:
        if archive.testzip():
            raise ValueError("Source archive checksum failure")
    print("SOURCE_ARCHIVE=" + str(dest))
    print("SOURCE_SHA256=" + hashlib.sha256(dest.read_bytes()).hexdigest())
    print("SOURCE_BYTES=" + str(dest.stat().st_size))

if __name__ == "__main__":
    binary.main()
    source_package()
