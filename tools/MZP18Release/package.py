"""Reproducible code-only rebuilt GivreClone App + 2 isolated WebAuth helpers.

Never package accounts, active original installation, profiles, caches, tokens,
client binaries, logs, captures or the original main GivreClone.exe.
Offline build only; NOT a validated gameplay clone of Givre v1.3.1.
"""
from __future__ import annotations

import hashlib
import json
import stat
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools"))
from MZP8Release import package as base

BUILD = ROOT / "GivreClone.App/bin/Release/net10.0/win-x64"
SOURCE = ROOT / "offline-evidence/v131-app/MZP17-WebAuth-code-manifest.json"
DEST = ROOT / "artifacts/MZP18-Historical-GivreCloneApp-WinX64-ISOLATED-AUTH-OFFLINE-ONLY.zip"
MANIFEST = ROOT / "artifacts/MZP18-Historical-App-Manifest.json"

# Same byte hashes as the MZP18IsolatedWebAuthRunner production verifier.
ISOLATED = {
    "MZP18IsolatedWebAuth.exe": ("CACE60C4E4525BAEBE2C5D6E466C50B1C0215A4A6E5D806BEA69731F4FA66A44", 162304),
    "MZP18IsolatedWebAuth.dll": ("384431D2C004297487343B7B940A3BEB42155BCA182998A65EA3364FB4510DE5", 56320),
    "MZP18IsolatedWebAuth.deps.json": ("5A6621159F971854A0E344FE6C99291476640003E0D05B0BB3351E5EB72A0081", 1869),
    "MZP18IsolatedWebAuth.runtimeconfig.json": ("96BC2946DFF8790400D652EC7F8DE7BB043E954980D59470C112788B68A1FBD3", 519),
    "Microsoft.Web.WebView2.Core.dll": ("616157863B1CD1F1330B4914DD013ADEF10A4CE8CD819598FD6BD4037C2C1646", 567896),
    "Microsoft.Web.WebView2.WinForms.dll": ("A2DDFDE024F6A248576A69CE9A54B5407AD915EE8FEC7DFF3A089A60948AC0AE", 38360),
    "Microsoft.Web.WebView2.Wpf.dll": ("2FE5FBCE16F19324A41AE40EB81FEF4E48A853969185F24AB7902350FEABF536", 51160),
    "WebView2Loader.dll": ("1CBCA1698175936A93D0C4D4148ED355E1B91415C6A4DB6D762F017DE3B6EB9A", 165336),
}


def package() -> None:
    original = json.loads(SOURCE.read_text(encoding="utf-8"))
    if original["source_zip_sha256"] != "D66690B642204FC35B3EF2E4A3B9EF53483B41A447B0BFE3C04DFA2D38DB06FD":
        raise ValueError("MZP17 distribution evidence mismatch")
    if original.get("source_account_files_read") or original.get("auth_session_performed"):
        raise ValueError("Unexpected original source evidence")
    authentic = {"WebAuth/" + item["file"]: (item["sha256"], item["bytes"])
                 for item in original["files"]}
    if len(authentic) != 10:
        raise ValueError("Original WebAuth component list invalid")
    isolated = {"WebAuthTest/" + name: details for name, details in ISOLATED.items()}
    witnessed = authentic | isolated
    found: list[tuple[str, bytes, str]] = []
    root = BUILD.resolve(strict=True)
    for file in sorted(root.rglob("*")):
        if file.is_symlink():
            raise ValueError("Symlink in release output")
        if not file.is_file():
            continue
        name = file.relative_to(root).as_posix()
        depth = len(file.relative_to(root).parts)
        if depth == 1:
            allowed = file.suffix.lower() == ".dll" or name in {
                "GivreClone.exe", "GivreClone.deps.json", "GivreClone.runtimeconfig.json"}
        elif name in witnessed:
            allowed = True
        elif depth == 2:
            allowed = file.parent.name == "NativeTurboRuntime" and file.name in base.NATIVE_ALLOWED
        else:
            allowed = False
        if not allowed:
            if file.suffix.lower() == ".pdb":
                continue
            raise ValueError("Unreviewed release file: " + name)
        if file.stat().st_size > 100_000_000:
            raise ValueError("Oversized release member")
        data = file.read_bytes()
        sha = hashlib.sha256(data).hexdigest().upper()
        if name in witnessed and (sha, len(data)) != witnessed[name]:
            raise ValueError("Authenticity/integrity mismatch: " + name)
        found.append((name, data, sha))
    actual = {path for path, _, _ in found}
    if not base.REQUIRED.issubset(actual) or not witnessed.keys() <= actual:
        raise ValueError("Incomplete product or auth helpers")
    manifest = {
        "product": "Rebuilt historical GivreClone.App with isolated TEST browser login",
        "target_client_manifest": "3.7.11.11",
        "source_distribution_sha256": original["source_zip_sha256"],
        "contains_original_main_GivreClone_exe": False,
        "contains_original_unchanged_WebAuth_companion": True,
        "contains_rebuilt_isolated_TEST_WebAuth": True,
        "genuine_Ankama_test_auth_performed": False,
        "client_test_launch_verified": False,
        "server_ACK_verified": False,
        "gameplay_parity_verified": False,
        "members": [{"path": name, "sha256": sha, "bytes": len(payload)}
                    for name, payload, sha in found],
    }
    blob = (json.dumps(manifest, indent=2, sort_keys=True) + "\n").encode()
    note = (
        "MZP18 built clone. The manually invoked TEST WebAuth helper uses a separate\n"
        "GivreReplica/TestRuns browser profile. Its OAuth tokens are discarded;\n"
        "browser cookies may remain in that private TEST directory.\n"
        "No original Givre installation, accounts or sessions included.\n"
        "No client, game server, UID ACK or gameplay is certified.\n"
        "Never install over the original GivreClone (three active bots).\n"
    ).encode()
    DEST.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(DEST, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for name, payload in [*((n, b) for n, b, _ in found),
                              ("README_MZP18.txt", note), ("MANIFEST_MZP18.json", blob)]:
            info = zipfile.ZipInfo(name, base.STAMP)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = (stat.S_IFREG | 0o644) << 16
            z.writestr(info, payload, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
    with zipfile.ZipFile(DEST) as z:
        if z.testzip() is not None:
            raise ValueError("CRC error")
    MANIFEST.write_bytes(blob)
    print(f"ARCHIVE={DEST}")
    print(f"SHA256={hashlib.sha256(DEST.read_bytes()).hexdigest().upper()}")
    print(f"BYTES={DEST.stat().st_size}")
    print(f"MEMBERS={len(found) + 2}")
    print("GAMEPLAY_VERIFIED=False")


if __name__ == "__main__":
    package()
