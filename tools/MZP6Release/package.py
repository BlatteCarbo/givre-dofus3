"""Package only allowlisted source and the independent Windows WinExe.

Runs wholly within GivreWork; no installed GivreClone, GivreLauncher,
credentials, dumps, captures, Pro environment secrets, or network access.
Release should already have been built with EnableAvaloniaPro=true.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
from zipfile import ZipFile, ZipInfo, ZIP_DEFLATED

ROOT = Path(__file__).resolve().parents[2]
ARTIFACTS = ROOT / "artifacts"
PUBLISH = (ROOT / "tools/MZP3ReplicaWorkbench/bin/Release/"
           "net10.0-windows/win-x64/publish")

SOURCE_FILES = (
    "tools/MZP3ReplicaWorkbench/MZP3ReplicaWorkbench.csproj",
    "tools/MZP3ReplicaWorkbench/ReplicaApplication.cs",
    "tools/MZP3ReplicaWorkbench/ReplicaWorkbenchWindow.cs",
    "tools/MZP3ReplicaWorkbench/Program.cs",
    "tools/MZP1OperatorShell/PanelXamlLoader.cs",
    "GivreClone.App/ViewModels/ViewModelBase.cs",
    "GivreClone.App/ViewModels/MZP0OperatorContracts.cs",
    "GivreClone.App/ViewModels/MZP0OperatorViewModel.cs",
    "GivreClone.App/Controls/MZP0OperatorPanel.axaml.cs",
    "GivreClone.App/Controls/MZP0OperatorPanel.axaml",
    "GivreClone.App/Views/MZP4TestAccountsPanel.cs",
    "GivreClone.App/Views/MZP6ProtocolInspectorPanel.cs",
    "GivreClone.App/Services/MZP3TestAccountService.cs",
    "GivreClone.App/Services/MZP4LauncherPreflight.cs",
    "GivreClone.App/Services/MZP5TestRecoveryPreparation.cs",
    "GivreClone.App/Services/MZP5OfflineJournalHealth.cs",
    "GivreClone.Engine/Recovery/MZP0RecoveryJournal.cs",
    "GivreClone.Engine/Recovery/MZP0RecoveryModels.cs",
    "GivreClone.Engine/Recovery/MZP5RecoveryHealthProjection.cs",
    "GivreClone.Protocol/MZP0ProtocolAdmission.cs",
    "GivreClone.Protocol/MZP3RawProtobuf.cs",
    "GivreClone.Protocol/MZP3FrameAndEnvelope.cs",
    "GivreClone.Protocol/MZP4ProtocolAdapter.cs",
    "GivreClone.Protocol/MZP5InboundProtocol.cs",
    "tools/MZP6WorkbenchUiHarness/MZP6WorkbenchUiHarness.csproj",
    "tools/MZP6WorkbenchUiHarness/Program.cs",
    "tools/MZP6ProtocolUiHarness/MZP6ProtocolUiHarness.csproj",
    "tools/MZP6ProtocolUiHarness/Program.cs",
    "tools/MZP5RecoveryHealthHarness/MZP5RecoveryHealthHarness.csproj",
    "tools/MZP5RecoveryHealthHarness/Program.cs",
    "tools/MZP5RecoveryPreparationHarness/MZP5RecoveryPreparationHarness.csproj",
    "tools/MZP5RecoveryPreparationHarness/Program.cs",
    "GivreClone.App/Views/BotsView.axaml",
    "docs/MZP6_PRIME_HANDOFF_20261016.md",
    "docs/MZP6_PROTOCOL_UI.md",
    "docs/MZP5_RECOVERY_PRODUCT.md",
    "docs/MZP5_PROTOCOL.md",
    "tools/MZP6Release/README.txt",
    "tools/MZP6Release/package.py",
)


def add_checked(z: ZipFile, file: Path, name: str, manifest: dict) -> None:
    path = file.resolve(strict=True)
    if file.is_symlink() or not path.is_relative_to(ROOT):
        raise RuntimeError("Unsafe package path")
    raw = path.read_bytes()
    manifest[name] = hashlib.sha256(raw).hexdigest()
    # Fixed timestamps/permissions: identical inputs produce identical hashes,
    # regardless of the local Windows file modification times or build clock.
    info = ZipInfo(name, date_time=(2026, 10, 16, 0, 0, 0))
    info.compress_type = ZIP_DEFLATED
    info.external_attr = 0o644 << 16
    z.writestr(info, raw, compress_type=ZIP_DEFLATED)


def main() -> None:
    if not (PUBLISH / "MZP3ReplicaWorkbench.exe").is_file():
        raise RuntimeError("Release WinExe must be published first")
    ARTIFACTS.mkdir(exist_ok=True)
    archives = (
        ARTIFACTS / "MZP6-ReplicaWorkbench-win-x64-AvaloniaPro.zip",
        ARTIFACTS / "MZP6-Source-Modules.zip",
    )
    # Do not accidentally include source maps, PDBs, user JSON, a runtime log,
    # a bundled certificate or private account state from a publish directory.
    permitted_extensions = {".exe", ".dll", ".json"}
    binary_files = sorted(p for p in PUBLISH.iterdir()
                          if p.is_file() and not p.is_symlink()
                          and p.suffix.lower() in permitted_extensions)
    if len(binary_files) < 10:
        raise RuntimeError("WinExe dependencies missing")
    if any(p.suffix == ".json" and p.name not in {
            "MZP3ReplicaWorkbench.deps.json",
            "MZP3ReplicaWorkbench.runtimeconfig.json"} for p in binary_files):
        raise RuntimeError("Unexpected JSON in release package")

    results = {}
    for package, files in ((archives[0], binary_files),
                           (archives[1], [ROOT / s for s in SOURCE_FILES])):
        entries: dict[str, str] = {}
        with ZipFile(package, "w", compression=ZIP_DEFLATED, compresslevel=6) as zipout:
            for file in files:
                if not file.is_file():
                    raise RuntimeError("Missing source input: " + file.name)
                entry = (file.name if package == archives[0]
                         else file.relative_to(ROOT).as_posix())
                add_checked(zipout, file, entry, entries)
            if package == archives[0]:
                readme = ROOT / "tools/MZP6Release/README.txt"
                add_checked(zipout, readme, "README.txt", entries)
        with ZipFile(package) as verified:
            if verified.testzip() is not None or len(verified.namelist()) != len(entries):
                raise RuntimeError("Archive checksum/entries invalid")
        results[package.name] = {
            "sha256": hashlib.sha256(package.read_bytes()).hexdigest(),
            "bytes": package.stat().st_size,
            "entries": len(entries),
            "included_sha256": entries,
        }
    manifest_path = ARTIFACTS / "MZP6-release-manifest.json"
    manifest_path.write_text(json.dumps({
        "scope": "independent_offline_TEST_no_live_givre_parity",
        "target_client_candidate": "3.7.10.10",
        "gameplay_certified": False,
        "server_ack_certified": False,
        "process_or_network_accessed_by_package_script": False,
        "packages": results,
    }, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print("MZP6 private product package manifest verified")
    for name, result in results.items():
        print(f"{name}: {result['entries']} files, {result['bytes']} bytes, "
              f"SHA256={result['sha256']}")


if __name__ == "__main__":
    main()
