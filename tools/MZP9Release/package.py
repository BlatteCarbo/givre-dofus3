"""Deterministic partial historical App package (MZP9), no game certification.

Reuses the audited MZP8 packager's allowlist to avoid accidental credentials,
account databases, logs, maps, dumps, or arbitrary helper/process binaries.
"""
from pathlib import Path
import sys

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
from MZP8Release import package as release  # noqa: E402

release.ARCHIVE = release.ARTIFACTS / "MZP9-Historical-GivreCloneApp-WinX64-UNVERIFIED-GAME.zip"
release.MANIFEST = release.ARTIFACTS / "MZP9-Historical-App-Manifest.json"
release.README = (
    "MZP9 historical GivreClone.App net10.0 win-x64 Release (2026-10-16).\n"
    "COMPILES but is NOT CERTIFIED as a playable GivreClone 1.3.1 clone.\n"
    "No authenticated session, gameplay, ACK or protocol 3.7.10.10 proof.\n"
    "No original GivreClone/Ankama process, account, config or token is included.\n"
    "P0 safety: MITM launch denied before HAAPI when TEST launcher/Zaap\n"
    "authority absent; a failed relay cannot continue; only the OS Process\n"
    "handle returned by OUR TEST launcher is eligible for termination.\n"
    "Thrift Zaap dialect is candidate-only and requires independent proof.\n"
    "Launcher/transport tests remain separate; this package is not live proof.\n"
    "Never run it over an existing active GivreClone session.\n"
)

if __name__ == "__main__":
    release.main()
