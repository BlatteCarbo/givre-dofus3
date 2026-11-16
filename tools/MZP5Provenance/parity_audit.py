"""Bounded, source-traceable GivreClone product-parity inventory.

No binary/process/network access. Explicit allowlist only, with source SHA and
line+symbol witnesses. This is NOT decompilation or evidence of live behavior.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = pathlib.Path(__file__).with_name("parity_snapshot.json")

# Functional evidence, deliberately narrower than a speculative folder scan.
# These lexical symbols are checked against actual source bytes, not comments
# about an EXE or a synthetic GivreCore implementation.
CAPABILITIES = (
    ("accounts_offline", "isolated_test_io", ("GivreClone.App/Services/MZP3TestAccountService.cs", "CreateProfile("),
     ("GivreClone.App/Views/MZP4TestAccountsPanel.cs", "PrepareRecovery(")),
    ("accounts_original", "blocked_provider", ("GivreClone.App/ViewModels/AccountsViewModel.cs", "class AccountsViewModel"),
     ("GivreClone.App/Views/AccountsView.axaml", "<UserControl")),
    ("server_character", "blocked_provider", ("GivreClone.App/ViewModels/AccountsViewModel.ServerPick.cs", "Server"),
     ("GivreClone.App/Services/LaunchService.Character.cs", "Character")),
    ("launcher_auth", "blocked_provider", ("GivreClone.App/Services/LaunchService.cs", "GameLauncher"),
     ("GivreClone.App/Services/MZP4LauncherPreflight.cs", "IsConfigurationCandidate")),
    ("mitm_transport", "blocked_provider", ("GivreClone.App/Services/Transport/MitmTransport.cs", "OnData"),
     ("GivreClone.Engine/BotHost.cs", "TcpProxyServer")),
    ("protobuf_offline", "offline_codec", ("GivreClone.Protocol/MZP3RawProtobuf.cs", "TryParse("),
     ("GivreClone.Protocol/MZP4ProtocolAdapter.cs", "TryReadClientRequest(")),
    ("combat", "blocked_provider", ("GivreClone.App/Services/LaunchService.Combat.cs", "combat"),
     ("GivreClone.App/ViewModels/BotsViewModel.Combat.cs", "combat")),
    ("maps_movement", "blocked_provider", ("GivreClone.Protocol/MapPathfinder.cs", "class MapPathfinder"),
     ("GivreClone.App/Services/LaunchService.GameLink.cs", "map")),
    ("lua", "blocked_provider", ("GivreClone.App/ViewModels/BotsViewModel.Lua.cs", "Lua"),
     ("GivreClone.App/Services/GivreLuaHost.Bindings.cs", "Lua")),
    ("inventory", "blocked_provider", ("GivreClone.App/Services/InventoryStore.cs", "Inventory"),
     ("GivreClone.Protocol/InventoryReader.cs", "Inventory")),
    ("bank", "blocked_provider", ("GivreClone.App/Services/LaunchService.Bank.cs", "Bank"),
     ("GivreClone.App/ViewModels/BotsViewModel.cs", "Bank")),
    ("hdv_commerce", "blocked_provider", ("GivreClone.App/Services/LaunchService.Hdv.cs", "Hdv"),
     ("GivreClone.App/ViewModels/HdvViewModel.cs", "Hdv")),
    ("planning", "blocked_provider", ("GivreClone.App/Views/PlanningEditor.axaml", "<UserControl"),
     ("GivreClone.App/ViewModels/BotsViewModel.Planning.cs", "Planning")),
    ("recovery", "offline_journal", ("GivreClone.Engine/Recovery/MZP0RecoveryJournal.cs", "OpenDispatchWindow("),
     ("GivreClone.Engine/Recovery/MZP5RecoveryHealthProjection.cs", "Read(")),
)


def snapshot() -> dict:
    providers = {
        "GivreCore": "../GivreProxy/GivreCore/GivreCore.csproj",
        "GivreLauncher.Core": "../GivreLauncher/GivreLauncher.Core/GivreLauncher.Core.csproj",
    }
    results = []
    for (name, classification, *sources) in CAPABILITIES:
        evidence = []
        for relative, symbol in sources:
            path = ROOT / relative
            if not path.is_file() or path.is_symlink():
                evidence.append({"file": relative, "symbol": symbol, "found": False})
                continue
            data = path.read_bytes()
            lines = data.decode("utf-8-sig", errors="replace").splitlines()
            # This is explicitly a lexical witness, not proof of behavior.
            found = next((i for i, line in enumerate(lines, 1) if symbol in line), None)
            evidence.append({"file": relative, "symbol": symbol, "line": found,
                             "found": found is not None,
                             "sha256": hashlib.sha256(data).hexdigest()})
        results.append({"capability": name, "source_classification": classification,
                        "all_symbols_present": all(e["found"] for e in evidence),
                        "evidence": evidence,
                        "runtime_gameplay_certified": False,
                        "server_ack_certified": False})
    return {
        "schema": 1,
        "method": "targeted_source_lexical_sha256_only_not_decompiled",
        "target_version": "3.7.10.10",
        "reference_exe_v131_sha256_not_reverified":
            "21C9DE5BA4D15763E9DAF029FD7432E3574EA113C9D1F44175FA9E0590290758",
        "provider_projects_present": {name: (ROOT / path).is_file()
                                       for name, path in providers.items()},
        "capabilities": results,
        "decompiled_original_exe": False,
        "original_process_accessed": False,
        "live_network_or_gameplay_tested": False,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    action = parser.add_mutually_exclusive_group(required=True)
    action.add_argument("--write", action="store_true")
    action.add_argument("--verify", action="store_true")
    action.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    fresh = snapshot()
    if args.self_test:
        assert len(fresh["capabilities"]) >= 12
        assert fresh["method"].endswith("not_decompiled")
        assert fresh["decompiled_original_exe"] is False
        assert not fresh["live_network_or_gameplay_tested"]
        assert all(len(row["evidence"]) == 2 for row in fresh["capabilities"])
        assert all(e["file"].startswith("GivreClone.") for row in fresh["capabilities"] for e in row["evidence"])
        print(f"MZP5 forensic source self-test: PASS ({len(fresh['capabilities'])} scoped workflows)")
        return 0
    if args.write:
        OUT.write_text(json.dumps(fresh, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print(f"MZP5 source snapshot written: {len(fresh['capabilities'])} workflows")
        return 0
    try:
        previous = json.loads(OUT.read_text(encoding="utf-8"))
    except (FileNotFoundError, ValueError):
        print("MZP5 SNAPSHOT_ABSENT_OR_CORRUPT", file=sys.stderr)
        return 2
    if previous != fresh:
        print("MZP5 SOURCE_DRIFT_DETECTED", file=sys.stderr)
        return 3
    ok = sum(row["all_symbols_present"] for row in fresh["capabilities"])
    print(f"MZP5 VERIFIED_SOURCE_ONLY {ok}/{len(fresh['capabilities'])} scoped source witnesses; runtime NOT certified")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
