"""Bounded deterministic package of the *historical* GivreClone.App win-x64 build.

This is a compile/inspect OFFLINE artifact, NOT a proven playable client.
Never package accounts, dumps, logs, installed apps, runtime secrets or PDBs.
"""
from __future__ import annotations

import hashlib
import json
import pathlib
import stat
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
BUILT = ROOT / "GivreClone.App" / "bin" / "Release" / "net10.0" / "win-x64"
ARTIFACTS = ROOT / "artifacts"
ARCHIVE = ARTIFACTS / "MZP8-Historical-GivreCloneApp-WinX64-OFFLINE-ONLY.zip"
MANIFEST = ARTIFACTS / "MZP8-Historical-App-Manifest.json"
STAMP = (2026, 10, 16, 0, 0, 0)

REQUIRED = {"GivreClone.exe", "GivreClone.dll", "GivreClone.deps.json",
            "GivreClone.runtimeconfig.json", "GivreCore.dll", "GivreLauncher.Core.dll",
            "GivreClone.Engine.dll", "GivreClone.Protocol.dll"}
NATIVE_ALLOWED = {
    "DofusCombatAnimationSkipper.dll", "DofusCombatSpeedHack.dll",
    "DofusNativeBootstrap.dll", "DofusNativeHost.dll",
    "PROVENANCE.md", "LICENSE.dofus3-native-host.txt", "LICENSE.MinHook.txt",
    "LICENSE.minhook-wrapper.txt", "LICENSE.UnityResolve.txt",
    "NOTICE.dofus3-native-host.txt",
}
README = ("MZP8 / 2026-10-16: historical GivreClone.App Windows x64 compile artifact.\n"
          "Status: RELEASE BUILD ONLY; NOT certified game-functional or GivreClone 1.3.1 parity.\n"
          "No server ACK, authenticated gameplay or Givre G0 certification.\n"
          "Default launch, Zaap 26666 and MITM 5658 explicitly REFUSE without independently\n"
          "authenticated TEST ownership and original handshake/Zaap implementation.\n"
          "Do NOT run against active original GivreClone and its 3 bots.\n"
          "Rebuild: dotnet restore GivreClone.App/GivreClone.App.csproj -r win-x64\n"
          "         dotnet build GivreClone.App/GivreClone.App.csproj -c Release -r win-x64 --no-restore -m:1\n")


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def read_allowed() -> list[tuple[str, bytes]]:
    root = BUILT.resolve(strict=True)
    found = []
    for path in sorted(root.rglob("*")):
        if path.is_symlink():
            raise ValueError("Symlink encountered in build output")
        if not path.is_file():
            continue
        relative = path.relative_to(root).as_posix()
        if len(path.parts) - len(root.parts) > 2:
            raise ValueError("Unexpected nested build path")
        if len(path.relative_to(root).parts) == 1:
            allowed = (path.suffix.lower() == ".dll" or relative in
                       {"GivreClone.exe", "GivreClone.deps.json", "GivreClone.runtimeconfig.json"})
        else:
            allowed = path.parent.name == "NativeTurboRuntime" and path.name in NATIVE_ALLOWED
        if not allowed:
            if path.suffix.lower() not in {".pdb"}:
                raise ValueError("Unexpected build file, review before packing: " + relative)
            continue
        size = path.stat().st_size
        if size > 100 * 1024 * 1024:
            raise ValueError("Unexpectedly large build file " + relative)
        found.append((relative, path.read_bytes()))
    if not REQUIRED.issubset({name for name, _ in found}):
        raise ValueError("Incomplete historical application output: " + str(REQUIRED - {n for n, _ in found}))
    return found


def write_archive(items: list[tuple[str, bytes]], manifest_bytes: bytes) -> None:
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(ARCHIVE, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zipped:
        for name, contents in [*items, ("README_MZP8.txt", README.encode("utf-8")),
                               ("MANIFEST_MZP8.json", manifest_bytes)]:
            info = zipfile.ZipInfo(name, STAMP)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = (stat.S_IFREG | 0o644) << 16
            zipped.writestr(info, contents, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
    with zipfile.ZipFile(ARCHIVE, "r") as zipped:
        damaged = zipped.testzip()
        if damaged:
            raise ValueError("Corrupt packaged member " + damaged)


def main() -> None:
    files = read_allowed()
    manifest = {
        "product": "historical GivreClone.App (partial reconstructed dependencies)",
        "build": "net10.0 / win-x64 / Release; compilation only",
        "live_authentication_verified": False,
        "gameplay_verified": False,
        "givre131_parity_certified": False,
        "givre_g0": "NOT_COMPARED",
        "members": [{"path": name, "sha256": sha(body), "bytes": len(body)} for name, body in files],
    }
    manifest_bytes = (json.dumps(manifest, ensure_ascii=False, sort_keys=True, indent=2) + "\n").encode("utf-8")
    write_archive(files, manifest_bytes)
    MANIFEST.write_bytes(manifest_bytes)
    print("ARCHIVE=" + str(ARCHIVE))
    print("ARCHIVE_SHA256=" + sha(ARCHIVE.read_bytes()))
    print("ARCHIVE_BYTES=" + str(ARCHIVE.stat().st_size))
    print("MEMBERS=" + str(len(files) + 2))


if __name__ == "__main__":
    main()
