"""Reproducible MZP7 partial-provider C# source + prebuilt library bundles.

Explicit allowlist only; no installed GivreClone, network, account files, game
captures, dumps, credentials, logs, license secrets or arbitrary repo scanning.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
from zipfile import ZipFile, ZipInfo, ZIP_DEFLATED

ROOT = Path(__file__).resolve().parents[3]
DEST = ROOT / "GivreWork/artifacts"

SOURCE = (
    "GivreProxy/GivreCore/GivreCore.csproj",
    "GivreProxy/GivreCore/Protobuf/ProtoField.cs",
    "GivreProxy/GivreCore/Protobuf/RawProtobuf.cs",
    "GivreProxy/GivreCore/Mitm/TcpProxyServer.cs",
    "GivreProxy/GivreCore/Injection/DllInjector.cs",
    "GivreProxy/GivreCore/tests/GivreCoreProtobufContractHarness.csproj",
    "GivreProxy/GivreCore/tests/Program.cs",
    "GivreProxy/GivreCore/PROVENANCE.md",
    "GivreLauncher/GivreLauncher.Core/GivreLauncher.Core.csproj",
    "GivreLauncher/GivreLauncher.Core/Proxy/ProxySettings.cs",
    "GivreLauncher/GivreLauncher.Core/Transport/Socks5Forwarder.cs",
    "GivreLauncher/GivreLauncher.Core/Transport/HttpConnectForwarder.cs",
    "GivreLauncher/GivreLauncher.Core/Transport/TransportLog.cs",
    "GivreLauncher/GivreLauncher.Core/tests/GivreLauncherTransportHarness.csproj",
    "GivreLauncher/GivreLauncher.Core/tests/Program.cs",
    "GivreLauncher/GivreLauncher.Core/PROVENANCE.md",
    "GivreWork/GivreClone.Engine/GameState.cs",
    "GivreWork/GivreClone.Protocol/MZP7TypedProtobufContract.cs",
    "GivreWork/tools/MZP7CoreContractHarness/MZP7CoreContractHarness.csproj",
    "GivreWork/tools/MZP7CoreContractHarness/Program.cs",
    "GivreWork/tools/MZP7ProviderEvidence/scanner.py",
    "GivreWork/tools/MZP7ProviderEvidence/test_scanner.py",
    "GivreWork/tools/MZP7ProviderEvidence/MZP7EvidenceAfterReconstruction.json",
    "GivreWork/docs/MZP7_CORE_RECONSTRUCTION.md",
    "GivreWork/docs/MZP7_PROVIDER_EVIDENCE.md",
    "GivreWork/docs/MZP7_PRIME_HANDOFF_20261016.md",
    "GivreRework/src/GivreRework.Infrastructure/Protocol/Differential/MZP7ReplayLeaseAdmission.cs",
    "GivreRework/tests/MZP7P0Harness/MZP7P0Harness.csproj",
    "GivreRework/tests/MZP7P0Harness/Program.cs",
    "GivreRework/docs/MZP7_P0.md",
    "GivreWork/tools/MZP7Release/package.py",
    "GivreWork/tools/MZP7Release/README.txt",
)

LIBRARIES = (
    "GivreProxy/GivreCore/bin/Release/net10.0/GivreCore.dll",
    "GivreLauncher/GivreLauncher.Core/bin/Release/net10.0/GivreLauncher.Core.dll",
    "GivreWork/GivreClone.Protocol/bin/Release/net10.0/GivreClone.Protocol.dll",
    "GivreWork/GivreClone.Engine/bin/Release/net10.0/GivreClone.Engine.dll",
    "GivreWork/tools/MZP7Release/README.txt",
)


def build_zip(path: Path, filenames: tuple[str, ...]) -> dict:
    checksum: dict[str, str] = {}
    with ZipFile(path, "w", compression=ZIP_DEFLATED, compresslevel=6) as archive:
        for name in sorted(filenames):
            candidate = ROOT / name
            real = candidate.resolve(strict=True)
            if not real.is_relative_to(ROOT) or candidate.is_symlink() or not candidate.is_file():
                raise RuntimeError("Invalid source path: " + name)
            if any(p.is_symlink() for p in candidate.parents if p.is_relative_to(ROOT)):
                raise RuntimeError("Reparse point in source path: " + name)
            data = real.read_bytes()
            if len(data) > 25 * 1024 * 1024:
                raise RuntimeError("Unexpected file size: " + name)
            checksum[name] = hashlib.sha256(data).hexdigest()
            item = ZipInfo(name, date_time=(2026, 10, 16, 0, 0, 0))
            item.compress_type = ZIP_DEFLATED
            item.external_attr = 0o644 << 16
            archive.writestr(item, data)
    with ZipFile(path, "r") as reread:
        if reread.testzip() is not None or len(reread.namelist()) != len(filenames):
            raise RuntimeError("Archive integrity failed")
    return {
        "bytes": path.stat().st_size,
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        "files": checksum,
    }


def main() -> None:
    DEST.mkdir(parents=True, exist_ok=True)
    binaries = build_zip(DEST / "MZP7-Partial-Provider-Libraries-net10.zip", LIBRARIES)
    sources = build_zip(DEST / "MZP7-Reconstructed-Provider-Source.zip", SOURCE)
    manifest = {
        "product": "MZP7 partial reconstruction, NOT full GivreClone v1.3.1",
        "original_exe_decompiled": False,
        "full_original_app_build": False,
        "authenticated_gameplay_or_ack_proven": False,
        "live_process_or_socket_accessed_by_package_script": False,
        "library_bundle": binaries,
        "source_bundle": sources,
    }
    (DEST / "MZP7-reconstruction-manifest.json").write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print("MZP7 deterministic bundles verified")
    for kind, item in (("DLL", binaries), ("C# SOURCE", sources)):
        print(f"{kind}: {len(item['files'])} files, {item['bytes']} bytes, SHA256={item['sha256']}")


if __name__ == "__main__":
    main()
