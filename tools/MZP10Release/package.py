"""Reproducible Windows x64 compiled historical-App bundle, explicitly NOT playable.

Reuses MZP8's audited allowlist: no account/keydata, dumps, captures, loose
protocol mappings, original installed executable, native installer or secrets.
"""
from pathlib import Path
import sys

tools = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(tools))
from MZP8Release import package as release  # noqa: E402

release.ARCHIVE = release.ARTIFACTS / "MZP10-Historical-GivreCloneApp-WinX64-OFFLINE-ONLY.zip"
release.MANIFEST = release.ARTIFACTS / "MZP10-Historical-App-Manifest.json"
release.README = (
    "MZP10 (2026-10-16): historical GivreClone.App compiled Win-x64 Release.\n"
    "NOT A PLAYABLE OR AUTHENTICATED GAME CLONE. LIVE ACK/GAMEPLAY UNKNOWN.\n"
    "The build requests a specific 3.7.10.10 candidate mapping, never 'latest'.\n"
    "Version guard checks exact bundle metadata and mapping completeness;\n"
    "matching 3.7.x.x prefix or identical hashes are NOT runtime proof.\n"
    "A user's updated Dofus.exe does not expose its game protocol version via\n"
    "the standard Unity PE file-version resource. No guess is certified.\n"
    "Ankama Launcher 2 v0.14.3 installed: inspected read-only metadata;\n"
    "generated ZaapService.js Thrift methods observed, never executed.\n"
    "Accounts page shows mapping gate and a real no-network refresh action.\n"
    "Only owned TEST loopback and disposable TEST helper process were tested.\n"
    "Original GivreClone with 3 bots must stay entirely untouched.\n"
    "No original installer, account, config, password or auth token included.\n"
)

if __name__ == "__main__":
    release.main()
