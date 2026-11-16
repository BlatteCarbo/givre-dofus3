"""Deterministic binary + MZP11 source delta package, no private data.

Never packages installed games, manifests, logs, accounts, secrets, captures,
credential files, live sessions or original GivreClone install. Binary output is
NOT a certified playable GivreClone 1.3.1 clone.
"""
from pathlib import Path
import hashlib
import json
import stat
import sys
import zipfile

tools = Path(__file__).resolve().parents[1]
root = tools.parent
sys.path.insert(0, str(tools))
from MZP8Release import package as release  # noqa: E402

release.ARCHIVE = release.ARTIFACTS / "MZP11-Historical-GivreCloneApp-WinX64-OFFLINE-ONLY.zip"
release.MANIFEST = release.ARTIFACTS / "MZP11-Historical-App-Manifest.json"
release.README = (
    "MZP11 (2026-10-16): historical GivreClone.App net10 Windows x64 compiled output.\n"
    "NOT A PLAYABLE / NETWORK AUTHENTICATED GIVREBOT 1.3.1 CLONE.\n"
    "Cytrus gzip/SQLite local game releaseVersion = 6.0_3.7.11.11.\n"
    "Installed game version metadata is not a certified server wire protocol.\n"
    "Mapping request now uses installed manifest build, not 3.7.10.10 or latest.\n"
    "Two reads of the manifest protect against update during mapping fetch.\n"
    "Windows SQLite system winsqlite3.dll, no bundled vulnerable e_sqlite3.\n"
    "Launcher 2 Zaap Thrift generated field signatures reviewed offline only.\n"
    "Original GivreClone and its active bots must remain untouched.\n"
    "No account, password, licence token, manifest or active process included.\n"
)

SOURCE_FILES = (
    "GivreClone.App/GivreClone.App.csproj",
    "GivreClone.App/Services/MZP11CytrusVersionProbe.cs",
    "GivreClone.App/Services/MZP10MappingVersionAdmission.cs",
    "GivreClone.App/Services/MappingCatalog.cs",
    "GivreClone.App/Services/MZP4LauncherPreflight.cs",
    "GivreClone.App/Services/Transport/Socket/DofusClientVersion.cs",
    "GivreClone.App/Views/MZP10MappingVerificationPanel.cs",
    "GivreClone.App/Views/AccountsView.axaml",
    "tools/MZP11CytrusProbeHarness/MZP11CytrusProbeHarness.csproj",
    "tools/MZP11CytrusProbeHarness/Program.cs",
    "tools/MZP11Launcher2ContractHarness/verify.py",
    "tools/MZP10MappingUiHarness/Program.cs",
    "tools/MZP4LauncherHarness/Program.cs",
    "docs/MZP11_PRIME_HANDOFF_20261016.md",
)


def package_source():
    dest = release.ARTIFACTS / "MZP11-Source-Delta-OFFLINE-ONLY.zip"
    manifest = []
    with zipfile.ZipFile(dest, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for relative in SOURCE_FILES:
            path = root / relative
            if not path.is_file() or path.is_symlink() or path.stat().st_size > 2_000_000:
                raise ValueError("Missing/unsafe source path: " + relative)
            if path.resolve().is_relative_to(root.resolve()) is False:
                raise ValueError("Source path escaped repository")
            data = path.read_bytes()
            manifest.append({"path": relative, "bytes": len(data),
                             "sha256": hashlib.sha256(data).hexdigest()})
            info = zipfile.ZipInfo(relative, release.STAMP)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = (stat.S_IFREG | 0o644) << 16
            archive.writestr(info, data, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
        content = (json.dumps({"source_scope": "MZP11 delta only, not full rebuild",
                               "gameplay_verified": False, "files": manifest},
                              indent=2, sort_keys=True) + "\n").encode("utf8")
        info = zipfile.ZipInfo("SOURCE_SHA256_MANIFEST.json", release.STAMP)
        info.compress_type = zipfile.ZIP_DEFLATED
        archive.writestr(info, content, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
    with zipfile.ZipFile(dest) as archive:
        if archive.testzip():
            raise ValueError("Corrupt source archive")
    print("SOURCE_ARCHIVE=" + str(dest))
    print("SOURCE_SHA256=" + hashlib.sha256(dest.read_bytes()).hexdigest())
    print("SOURCE_BYTES=" + str(dest.stat().st_size))


if __name__ == "__main__":
    release.main()
    package_source()
