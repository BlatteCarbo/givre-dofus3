#!/usr/bin/env python3
"""Deterministic, source-only Givre/Givre parity and offline EXE provenance inventory.

Reads allowlisted source paths and at most a few GivreClone.App/bin EXEs; never
opens a process, connects to the game, imports application code or reads dumps.
Evidence is limited to relative source locations, declaration line and SHA-256.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class Anchor:
    path: str
    pattern: str
    symbol: str


@dataclass(frozen=True)
class Feature:
    key: str
    label: str
    givre: Anchor | None
    rework: Anchor | None
    note: str


def a(path: str, symbol: str, declaration: str) -> Anchor:
    return Anchor(path, declaration, symbol)


# These are tightly scoped declarations, not text matches in documentation,
# binaries, private assets or tests. Presence establishes only source evidence.
FEATURES: tuple[Feature, ...] = (
    Feature("protocol_catalog", "Catalogue messages", a("GivreClone.Protocol/ProtocolCatalog.cs", "ProtocolCatalog", r"^\s*public sealed class ProtocolCatalog\b"), a("src/GivreRework.Infrastructure/Protocol/Versioned/P0PinnedBranchCatalog.cs", "P0PinnedBranchCatalog", r"^\s*public sealed class P0PinnedBranchCatalog\b"), "Rework catalogues 3.7 candidats, verification wire encore ouverte."),
    Feature("outbound_envelope", "Forge des requetes", a("GivreClone.Protocol/EnvelopeBuilder.cs", "EnvelopeBuilder", r"^\s*public static class EnvelopeBuilder\b"), a("src/GivreRework.Infrastructure/Scripting/IntentBridge/TypedLuaIntentBridge.cs", "TypedLuaIntentBridge", r"^\s*public (?:sealed |static )?class TypedLuaIntentBridge\b"), "L'intention offline Rework ne prouve pas une emission reseau."),
    Feature("inbound_capture", "Reception de messages", a("GivreClone.Engine/DofusCaptureHost.cs", "DofusCaptureHost", r"^\s*public sealed class DofusCaptureHost\b"), a("src/GivreRework.Infrastructure/Protocol/Differential/P0PrivateCapturePreview.cs", "P0PrivateCapturePreview", r"^\s*public static class P0PrivateCapturePreview\b"), "Givre appelle des types GivreCore externes; Rework ne lit qu'un corpus prive offline."),
    Feature("stream_framing", "Framing incremental", None, a("src/GivreRework.Infrastructure/Protocol/Stream/P0LegacyCompatibleStreamFramer.cs", "P0LegacyCompatibleStreamFramer", r"^\s*public sealed class P0LegacyCompatibleStreamFramer\b"), "Decodeur de framing Givre en dependance GivreCore absente de la copie."),
    Feature("request_correlation", "Correlation UID", a("GivreClone.Protocol/RequestCorrelator.cs", "RequestCorrelator", r"^\s*public sealed class RequestCorrelator\b"), a("src/GivreRework.Infrastructure/Protocol/Differential/P0CandidateCorrelationTracker.cs", "P0CandidateCorrelationTracker", r"^\s*public (?:sealed |static )?class P0CandidateCorrelationTracker\b"), "UID candidat Rework n'est pas une confirmation serveur."),
    Feature("packet_bus", "Bus par session", a("GivreClone.Engine/PacketBus.cs", "PacketBus", r"^\s*public sealed class PacketBus\b"), None, "Pas de bus synchrone equivalent certifie par cette selection source Rework."),
    Feature("game_state", "Etat de jeu", a("GivreClone.Engine/GameState.cs", "GameState", r"^\s*public sealed class GameState\b"), a("src/GivreRework.Domain/Combat/CombatObservationState.cs", "CombatObservationReducer", r"^\s*public static class CombatObservationReducer\b"), "Le reducteur Rework ne prouve pas la mise a jour depuis un flux reel."),
    Feature("action_sender", "Emetteur gameplay", a("GivreClone.Engine/ActionSender.cs", "ActionSender", r"^\s*public sealed class ActionSender\b"), None, "Aucun emetteur gameplay certifie de production Rework dans le perimetre P0."),
    Feature("combat", "Combat automatique", a("GivreClone.App/Services/LaunchService.Combat.cs", "LaunchService", r"^\s*public sealed partial class LaunchService\b"), a("src/GivreRework.Domain/Combat/Simulation/G7OfflineTacticalSimulator.cs", "G7OfflineTacticalSimulator", r"^\s*public (?:sealed |static )?class G7OfflineTacticalSimulator\b"), "Givre logique applicative source; Rework simulateur offline."),
    Feature("navigation", "Navigation et pathfinding", a("GivreClone.Protocol/MapPathfinder.cs", "MapPathfinder", r"^\s*public (?:sealed |static )?class MapPathfinder\b"), a("src/GivreRework.Domain/World/Pathfinding/ObservedWalkabilityPathfinder.cs", "ObservedWalkabilityPathfinder", r"^\s*public (?:sealed |static )?class ObservedWalkabilityPathfinder\b"), "Comparer topologie/cellules, sans inferrer de mouvement execute."),
    Feature("bank", "Banque/echange", a("GivreClone.App/Services/LaunchService.Bank.cs", "LaunchService", r"^\s*public sealed partial class LaunchService\b"), a("src/GivreRework.Application/Bank/Exchange/OfflineExchangeFlow.cs", "OfflineExchangeFlow", r"^\s*public static class OfflineExchangeFlow\b"), "Flux Rework est une preparation offline, pas un parcours banque reel."),
    Feature("market", "HDV et economie", a("GivreClone.App/Services/LaunchService.Hdv.cs", "LaunchService", r"^\s*public sealed partial class LaunchService\b"), a("src/GivreRework.Application/Economy/Market/OfflineMarketQuoteEngine.cs", "OfflineMarketQuoteEngine", r"^\s*public (?:sealed |static )?class OfflineMarketQuoteEngine\b"), "Les cotations Rework ne correspondent pas a un achat/une vente en jeu."),
    Feature("craft", "Fabrication", a("GivreClone.App/Services/LaunchService.Craft.cs", "LaunchService", r"^\s*public sealed partial class LaunchService\b"), a("src/GivreRework.Application/Economy/Crafting/OfflineCraftingPlanner.cs", "OfflineCraftingPlanner", r"^\s*public (?:sealed |static )?class OfflineCraftingPlanner\b"), "Rework planification hors ligne."),
    Feature("lua_runtime", "Execution de scripts Lua", a("GivreClone.App/Services/GivreLuaHost.cs", "GivreLuaHost", r"^\s*public sealed partial class GivreLuaHost\b"), a("src/GivreRework.Infrastructure/Scripting/Engine/BoundedOfflineLuaEngine.cs", "BoundedOfflineLuaEngine", r"^\s*public sealed class BoundedOfflineLuaEngine\b"), "Le runtime Rework est borne aux contrats offline tant que le flux manque."),
    Feature("route_recording", "Enregistrement d'itineraire", a("GivreClone.App/Services/RoadRecorder.cs", "RoadRecorder", r"^\s*public sealed class RoadRecorder\b"), a("src/GivreRework.Application/World/Recording/ObservedRouteRecorder.cs", "ObservedRouteRecorder", r"^\s*public (?:sealed |static )?class ObservedRouteRecorder\b"), "Rework enregistre des observations fournies; leur origine reste a verifier."),
    Feature("multi_account", "Gestion multi-comptes", a("GivreClone.Engine/Accounts/AccountManager.cs", "AccountManager", r"^\s*public sealed class AccountManager\b"), None, "Pas de gestionnaire de comptes live equivalent certifie dans Rework P0."),
    Feature("orchestration", "Orchestration de bots", a("GivreClone.Engine/BotOrchestrator.cs", "BotOrchestrator", r"^\s*public sealed class BotOrchestrator\b"), a("src/GivreRework.Application/Supervision/Offline/OfflineSupervisor.cs", "OfflineSupervisor", r"^\s*public (?:sealed |static )?class OfflineSupervisor\b"), "Le superviseur Rework est explicitement offline."),
    Feature("native_turbo", "Turbo/animations natif", a("GivreClone.App/Services/NativeTurboRuntime.cs", "NativeTurboRuntime", r"^\s*internal static class NativeTurboRuntime\b"), None, "Cette copie n'inclut aucune preuve d'integration native Rework."),
    Feature("avalonia_ui", "Interface Avalonia", a("GivreClone.App/Views/BotsView.axaml.cs", "BotsView", r"^\s*public partial class BotsView\b"), a("src/GivreRework.App/MainWindow.cs", "MainWindow", r"^\s*internal sealed class MainWindow\b"), "Comparer ecrans et commandes sur le code reel, pas sur le nom du framework."),
    Feature("private_differential", "Comparaison privee des decodeurs", None, a("src/GivreRework.Infrastructure/Protocol/Differential/P0SameBytesDifferential.cs", "P0SameBytesDifferential", r"^\s*public static class P0SameBytesDifferential\b"), "Les deux vrais decodeurs et le corpus authentifie manquent au passage end-to-end."),
    Feature("operator_panel", "Console diagnostics P0", None, a("src/GivreRework.App/Services/P0OperatorWindow.cs", "P0OperatorWindow", r"^\s*internal sealed class P0OperatorWindow\b"), "Fenetre offline verifiee, aucune session Dofus live deduite."),
)


def confined_path(root: Path, relative: str) -> Path:
    if Path(relative).is_absolute() or ".." in Path(relative).parts:
        raise ValueError("untrusted relative path")
    candidate = root / relative
    if candidate.is_symlink() or not candidate.resolve().is_relative_to(root.resolve()):
        raise ValueError("source outside root or symlink")
    return candidate


def evidence(root: Path, anchor: Anchor | None) -> dict:
    if anchor is None:
        return {"status": "missing_source", "reason": "no_corresponding_anchor_selected", "evidence": []}
    path = confined_path(root, anchor.path)
    if not path.is_file():
        return {"status": "missing_source", "reason": "source_file_absent", "evidence": []}
    raw = path.read_bytes()
    if len(raw) > 2 * 1024 * 1024:
        return {"status": "unverified", "reason": "source_too_large", "evidence": []}
    try:
        source = raw.decode("utf-8-sig")
    except UnicodeError:
        return {"status": "unverified", "reason": "not_utf8_source", "evidence": []}
    compiled = re.compile(anchor.pattern)
    matches = [line_number for line_number, line in enumerate(source.splitlines(), 1)
               if compiled.search(line)]
    if not matches:
        return {"status": "missing_source", "reason": "declaration_not_found", "evidence": []}
    return {"status": "source_only", "reason": "declaration_present_not_runtime_verified", "evidence": [
        {"file": anchor.path, "line": matches[0], "symbol": anchor.symbol,
         "sha256": hashlib.sha256(raw).hexdigest()}]}


def binary_provenance(root: Path) -> dict:
    """Only regular offline build outputs in this tree, not installed/live EXEs.

    GivreClone.App.csproj declares AssemblyName=GivreClone (not GivreClone.App).
    """
    bin_root = root / "GivreClone.App" / "bin"
    candidates: list[Path] = []
    if bin_root.is_dir() and not bin_root.is_symlink():
        # Also covers RID/publish subfolders, while remaining inside App/bin.
        for entry in bin_root.rglob("GivreClone.exe"):
            if entry.is_file() and not entry.is_symlink() and entry.resolve().is_relative_to(root.resolve()):
                candidates.append(entry)
    artifacts = []
    for path in sorted(candidates, key=lambda p: p.as_posix().casefold())[:8]:
        size = path.stat().st_size
        if size < 64 or size > 250 * 1024 * 1024:
            continue
        with path.open("rb") as stream:
            head = stream.read(64)
            if head[:2] != b"MZ":
                continue
            pe_offset = int.from_bytes(head[60:64], "little")
            if pe_offset > size - 4 or pe_offset > 8 * 1024 * 1024:
                continue
            stream.seek(pe_offset)
            if stream.read(4) != b"PE\x00\x00":
                continue
            stream.seek(0)
            digest = hashlib.sha256()
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)
        artifacts.append({"file": path.relative_to(root).as_posix(), "size_bytes": size,
                          "sha256": digest.hexdigest(), "feature_proof": "none"})
    return {"state": "offline_executable_present" if artifacts else "no_offline_exe",
            "artifacts": artifacts,
            "exe_feature_verification": "not_performed",
            "warning": "A valid PE and SHA do not establish application feature parity."}


def source_module_inventory(root: Path) -> list[dict]:
    """Inventory bounded app feature slices; no code text or assets are exported."""
    slices = (
        ("gameplay_service", "GivreClone.App/Services", "LaunchService*.cs"),
        ("lua_binding", "GivreClone.App/Services", "GivreLuaHost*.cs"),
        ("road_recorder", "GivreClone.App/Services", "RoadRecorder*.cs"),
        ("view_definition", "GivreClone.App/Views", "*.axaml"),
        ("view_model", "GivreClone.App/ViewModels", "*.cs"),
    )
    rows: list[dict] = []
    for group, folder, glob in slices:
        source_dir = root / folder
        if not source_dir.is_dir() or source_dir.is_symlink():
            continue
        for path in sorted(source_dir.glob(glob), key=lambda p: p.name.casefold())[:250]:
            if not path.is_file() or path.is_symlink() or not path.resolve().is_relative_to(root.resolve()):
                continue
            if path.stat().st_size > 2 * 1024 * 1024:
                continue
            raw = path.read_bytes()
            rows.append({"category": group, "file": path.relative_to(root).as_posix(),
                         "sha256": hashlib.sha256(raw).hexdigest(),
                         "source_only": True, "exe_confirmed": False})
    return sorted(rows, key=lambda row: (row["category"], row["file"]))


def source_declared_version(root: Path) -> dict:
    """Source license-client.version constant, never binary product version."""
    relative = "GivreClone.App/Services/LicenseService.cs"
    path = confined_path(root, relative)
    if not path.is_file() or path.stat().st_size > 2 * 1024 * 1024:
        return {"state": "not_found", "version": None, "evidence": []}
    raw = path.read_bytes()
    try:
        lines = raw.decode("utf-8-sig").splitlines()
    except UnicodeError:
        return {"state": "not_found", "version": None, "evidence": []}
    expression = re.compile(r'^\s*public const string AppVersion\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+)"\s*;')
    for line_no, line in enumerate(lines, 1):
        match = expression.search(line)
        if match:
            return {"state": "source_only", "version": match.group(1), "role": "license_client.version",
                    "evidence": [{"file": relative, "line": line_no,
                                  "sha256": hashlib.sha256(raw).hexdigest()}]}
    return {"state": "not_found", "version": None, "evidence": []}


def build_inventory(givre: Path, rework: Path, reported_window_version: str | None = None) -> dict:
    if not givre.is_dir() or not rework.is_dir():
        raise ValueError("both source repositories must exist")
    givre, rework = givre.resolve(), rework.resolve()
    records = []
    for spec in FEATURES:
        left, right = evidence(givre, spec.givre), evidence(rework, spec.rework)
        records.append({"id": spec.key, "label": spec.label,
                        "givre": left, "givre_rework": right,
                        "comparison": ("both_source" if left["status"] == right["status"] == "source_only"
                                       else "givre_source_only" if left["status"] == "source_only"
                                       else "rework_source_only" if right["status"] == "source_only"
                                       else "neither_source_verified"),
                        "functional_note": spec.note, "exe_confirmed": False})
    core = givre.parent / "GivreProxy" / "GivreCore" / "GivreCore.csproj"
    requirements = {
        "givre_core_neighbor_project": core.is_file(),
        "givre_data_mapping_directory": (givre / "data").is_dir(),
        "givre_server_mapping_directory": (givre / "server-mapping").is_dir(),
        "rework_pinned_matching": (rework / "data/3.7/matching_current.json").is_file(),
        "rework_pinned_fields": (rework / "data/3.7/fields.json").is_file(),
    }
    binary = binary_provenance(givre)
    binary["source_declared_version"] = source_declared_version(givre)
    binary["running_window_observation"] = {
        "status": "running_window_only" if reported_window_version else "not_reported",
        "version_label": reported_window_version,
        "version_provenance": "reported_by_prime_passive_ui_only" if reported_window_version else None,
        "binary_sha256": None,
        "executable_read_or_copied": False,
    }
    binary["source_vs_window_version"] = (
        "different_unlinked_labels" if reported_window_version and
        binary["source_declared_version"]["version"] and
        reported_window_version != binary["source_declared_version"]["version"]
        else "not_comparable_or_same_label")
    return {
        "schema_version": 1,
        "classification": "source_only_offline_parity_inventory",
        "repositories": {"givre": "GivreWork", "givre_rework": "GivreRework"},
        "binary_provenance": binary,
        "dependencies_present": requirements,
        "feature_count": len(records),
        "features": records,
        "givre_app_module_provenance": source_module_inventory(givre),
        "global_proof": {"source_scanned": True, "exe_features_confirmed": 0,
                         "live_process_examined": False, "runtime_gameplay_validated": False,
                         "server_ack_validated": False},
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    repo = Path(__file__).resolve().parents[1]
    parser.add_argument("--givre-root", type=Path, default=repo)
    parser.add_argument("--rework-root", type=Path, default=repo.parent / "GivreRework")
    parser.add_argument("--output", type=Path, default=repo / "tools/MZP0_SourceParityInventory.json")
    # A point-in-time, externally reported UI label, NOT parsed from any binary.
    # Default keeps plain --check reproducible for this snapshot (2026-10-16).
    parser.add_argument("--reported-window-version", default="1.3.1",
                        help="unverified version label reported from passive UI; not EXE proof")
    parser.add_argument("--check", action="store_true", help="compare exact JSON bytes; do not write")
    args = parser.parse_args()
    report = build_inventory(args.givre_root, args.rework_root, args.reported_window_version)
    encoded = (json.dumps(report, indent=2, ensure_ascii=False, sort_keys=True) + "\n").encode("utf-8")
    if args.check:
        if not args.output.is_file() or args.output.read_bytes() != encoded:
            parser.error("inventory stale or absent: regenerate source-only snapshot")
    else:
        args.output.write_bytes(encoded)
    statuses = {f["comparison"] for f in report["features"]}
    print(f"MZP0 offline source inventory: {len(report['features'])} features; "
          f"binary={report['binary_provenance']['state']}; comparisons={','.join(sorted(statuses))}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
