"""Deterministic source delta for MZP17, excluding original binary + secrets.

The original managed WebAuth IL remains in offline-evidence but is not packed:
it contains private auth handshake implementation and diagnostic URLs.
"""
from pathlib import Path
import hashlib
import json
import stat
import zipfile

ROOT = Path(__file__).resolve().parents[2]
TARGET = ROOT / "artifacts/MZP17-Source-Delta-OFFLINE-ONLY.zip"
STAMP = (2026, 10, 16, 0, 0, 0)
FILES = (
    "GivreClone.App/GivreClone.App.csproj",
    "GivreClone.App/Services/MZP17WebAuthIntegrity.cs",
    "GivreClone.App/Services/WebAuthRunner.cs",
    "tools/MZP17ExtractOwnedDistribution.py",
    "tools/MZP17StageWebAuth.py",
    "tools/MZP17NativeBinaryTriage.py",
    "tools/MZP17WebAuthContract.py",
    "tools/MZP17WebAuthHarness/MZP17WebAuthHarness.csproj",
    "tools/MZP17WebAuthHarness/Program.cs",
    "tools/MZP17RecoveredWebAuth/MZP17RecoveredWebAuth.csproj",
    "tools/MZP17Release/package.py",
    "tools/MZP17Release/package_source.py",
    "docs/MZP17_REAL_131_PACKAGE_AND_WEBAUTH_20261016.md",
)

def package():
    entries = []
    TARGET.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(TARGET, "w", compression=zipfile.ZIP_DEFLATED,
                         compresslevel=9) as z:
        for rel in FILES:
            file = ROOT / rel
            if not file.is_file() or file.is_symlink() or not file.resolve().is_relative_to(ROOT.resolve()):
                raise ValueError("Untrusted source path: " + rel)
            if file.stat().st_size > 1_000_000:
                raise ValueError("Oversized source: " + rel)
            raw = file.read_bytes()
            entries.append({"path": rel, "bytes": len(raw),
                            "sha256": hashlib.sha256(raw).hexdigest()})
            info = zipfile.ZipInfo(rel, STAMP)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = (stat.S_IFREG | 0o644) << 16
            z.writestr(info, raw, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
        manifest = (json.dumps({
            "source_level": "reviewable MZP17 delta; native v1.3.1 C# NOT recovered",
            "authenticated_gameplay": False,
            "decompiled_original_web_auth_il_included": False,
            "files": entries,
        }, indent=2, sort_keys=True) + "\n").encode()
        info = zipfile.ZipInfo("SOURCE_MANIFEST.json", STAMP)
        info.compress_type = zipfile.ZIP_DEFLATED
        z.writestr(info, manifest, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
    with zipfile.ZipFile(TARGET) as z:
        if z.testzip() is not None:
            raise ValueError("Source bundle corrupted")
    print("SOURCE_BUNDLE=" + str(TARGET))
    print("SOURCE_BYTES=" + str(TARGET.stat().st_size))
    print("SOURCE_SHA256=" + hashlib.sha256(TARGET.read_bytes()).hexdigest().upper())
    print("SOURCE_MEMBERS=" + str(len(FILES) + 1))

if __name__ == "__main__":
    package()
