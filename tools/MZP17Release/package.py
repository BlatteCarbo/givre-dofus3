"""Deterministic historical App + genuine provenance-pinned WebAuth companion.

Includes *only* reviewed build code files. Never includes the user's installed
GivreClone, private browser profile, account database, dumps, game client files,
credentials, session logs or downloaded ZIP itself. NOT a gameplay-certified
clone of the 1.3.1 release.
"""
from __future__ import annotations
from pathlib import Path
import hashlib
import json
import stat
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools"))
from MZP8Release import package as base
BUILD = ROOT / "GivreClone.App/bin/Release/net10.0/win-x64"
DEST = ROOT / "artifacts/MZP17-Historical-GivreCloneApp-WinX64-WEBAUTH-OFFLINE-ONLY.zip"
MANIFEST = ROOT / "artifacts/MZP17-Historical-App-Manifest.json"
WEB_AUTH_SOURCE = ROOT / "offline-evidence/v131-app/MZP17-WebAuth-code-manifest.json"
STAMP = base.STAMP


def package():
    if not WEB_AUTH_SOURCE.is_file() or WEB_AUTH_SOURCE.is_symlink():
        raise ValueError("Missing code-only WebAuth provenance")
    evidence = json.loads(WEB_AUTH_SOURCE.read_text(encoding="utf-8"))
    if evidence.get("source_zip_sha256") != (
        "D66690B642204FC35B3EF2E4A3B9EF53483B41A447B0BFE3C04DFA2D38DB06FD"
    ) or evidence.get("source_account_files_read") or evidence.get("auth_session_performed"):
        raise ValueError("Untrusted WebAuth provenance source")
    witness = {
        "WebAuth/" + member["file"]: (member["sha256"], member["bytes"])
        for member in evidence["files"]
    }
    if len(witness) != 10:
        raise ValueError("Expected only ten original distribution WebAuth files")

    output_files = []
    root = BUILD.resolve(strict=True)
    for file in sorted(root.rglob("*")):
        if file.is_symlink():
            raise ValueError("Symlink in built release tree")
        if not file.is_file():
            continue
        name = file.relative_to(root).as_posix()
        depth = len(file.relative_to(root).parts)
        if depth == 1:
            allowed = (file.suffix.lower() == ".dll" or name in
                       {"GivreClone.exe", "GivreClone.deps.json",
                        "GivreClone.runtimeconfig.json"})
        elif name in witness:
            allowed = True
        elif depth == 2:
            allowed = (file.parent.name == "NativeTurboRuntime"
                       and file.name in base.NATIVE_ALLOWED)
        else:
            allowed = False
        if not allowed:
            if file.suffix.lower() == ".pdb":
                continue
            raise ValueError("Unreviewed file in build: " + name)
        if file.stat().st_size > 100_000_000:
            raise ValueError("Oversized output: " + name)
        data = file.read_bytes()
        file_sha = hashlib.sha256(data).hexdigest().upper()
        if name in witness:
            exp_sha, exp_bytes = witness[name]
            if file_sha != exp_sha or len(data) != exp_bytes:
                raise ValueError("WebAuth sidecar mismatches authenticated distribution: " + name)
        output_files.append((name, data, file_sha))

    found = {name for name, _, _ in output_files}
    if not base.REQUIRED.issubset(found) or not witness.keys() <= found:
        raise ValueError("Partial App / genuine companion missing")
    manifest = {
        "product": "Historical GivreClone.App with local authentic WebAuth code",
        "client_installed_version_manifest": "3.7.11.11 (local Cytrus evidence)",
        "source_distribution_zip_sha256": evidence["source_zip_sha256"],
        "contains_original_main_givrebot_exe": False,
        "contains_genuine_web_auth_companion": True,
        "original_v131_source_identity_certified": False,
        "authenticated_game_session_validated": False,
        "server_ack_validated": False,
        "gameplay_validated": False,
        "members": [
            {"path": n, "sha256": sha, "bytes": len(body)}
            for n, body, sha in output_files
        ],
    }
    blob = (json.dumps(manifest, indent=2, sort_keys=True) + "\n").encode()
    readme = (
        "MZP17: historical rebuilt Windows x64 GivreClone.App with genuine "
        "WebAuth helper from user-owned distribution.\n"
        "WebAuth EXE/DLL and dependencies are byte-identical to the SHA-pinned "
        "isolated original pkg.zip.\n"
        "This does NOT bypass login, official licensing, 2FA or ownership.\n"
        "No signed-in session, server ACK, map or gameplay was witnessed.\n"
        "This is NOT the exact decompiled GivreClone v1.3.1 source or a 100% clone.\n"
        "Do not run over or modify original GivreClone (three active bots).\n"
    ).encode()
    DEST.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(DEST, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for name, data in [*[(n, b) for n, b, _ in output_files],
                           ("README_MZP17.txt", readme),
                           ("MANIFEST_MZP17.json", blob)]:
            info = zipfile.ZipInfo(name, STAMP)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = (stat.S_IFREG | 0o644) << 16
            z.writestr(info, data, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
    with zipfile.ZipFile(DEST) as z:
        if z.testzip() is not None:
            raise ValueError("ZIP CRC error")
    MANIFEST.write_bytes(blob)
    print("MZP17 PACKAGE GENERATED")
    print("ARCHIVE=" + str(DEST))
    print("ARCHIVE_BYTES=" + str(DEST.stat().st_size))
    print("ARCHIVE_SHA256=" + hashlib.sha256(DEST.read_bytes()).hexdigest().upper())
    print("MEMBERS=" + str(len(output_files) + 2))
    print("WEBAUTH_VERIFIED_MEMBERS=10")
    print("GAMEPLAY_CERTIFIED=False")


if __name__ == "__main__":
    package()
