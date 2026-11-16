#!/usr/bin/env python3
"""Reproducible, source-only Givre dependency callsite inventory (never executes code).

The parser is deliberately lexical rather than a C# compiler: it proves source
spelling, imports and locations, NOT resolved type identity, APIs or runtime parity.
Only source under App/Engine/Protocol/RelayAgent is examined. No DLLs, dumps,
secrets, configs, ZIPs, live processes, or generated MZP0 harness stubs are read.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

SRC = ("GivreClone.App", "GivreClone.Engine", "GivreClone.Protocol", "GivreRelayAgent")
PREFIXES = ("GivreCore", "GivreLauncher.Core")
EXCLUDED = {"obj", "bin", "artifacts", ".git", ".vs", "packages", "cache"}
USING = re.compile(r"^\s*(?:global\s+)?using\s+(?:(\w+)\s*=\s*)?(GivreCore|GivreLauncher\.Core)(?:\.([\w.]+))?\s*;")
QUALIFIED = re.compile(r"\b(GivreCore|GivreLauncher\.Core)(?:\.[A-Za-z_][A-Za-z_0-9]*)+")
NAMESPACE = re.compile(r"^\s*namespace\s+(GivreCore|GivreLauncher\.Core)(?:\.|\s*[;{])")

# Each name is anchored in its actual namespace imported by consumer files.
# 'interface' here means a consumed API surface, not a fabricated C# interface.
TOP = (
    ("P1_PROTOBUF", "GivreCore", "Protobuf", ("ProtoField", "RawProtobuf", "WireType"),
     "Parser/encoder/typed protobuf tree and frame length encoding"),
    ("P2_MITM_CAPTURE", "GivreCore", "Mitm", ("TcpProxyServer", "CapturedMessage", "Direction"),
     "TcpProxyServer capture event, replay, connection identity and captured frames"),
    ("P3_HANDSHAKE_RELAY", "GivreLauncher.Core", "Transport", ("HandshakeRelay",),
     "PID-scoped network relay, game proxy and data/closed events"),
    ("P4_GAME_LAUNCH", "GivreLauncher.Core", "Launch", ("GameLauncher",),
     "Game path resolution and owned-process launcher"),
    ("P5_PROXY_ROUTING", "GivreLauncher.Core", "Proxy", ("ProxySettings", "ProxyKind"),
     "Proxy policies, server/game routing and SOCKS/HTTP CONNECT"),
)


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def safe_files(root: Path):
    for directory in SRC:
        parent = root / directory
        if not parent.is_dir() or parent.is_symlink():
            continue
        # Work is limited to known source trees and excludes transitive links.
        for f in sorted(parent.rglob("*.cs")):
            rel = f.relative_to(root)
            if f.is_symlink() or any(p in EXCLUDED for p in rel.parts):
                continue
            if any(p.is_symlink() for p in list(f.parents)[:-1] if root in p.parents):
                continue
            yield f, rel.as_posix()


def collect(root: Path) -> dict:
    if not root.is_dir():
        raise ValueError("Source repository not found")
    # Source provenance: assembly project is an EXE named GivreRelay, not GivreCore.
    projects = {}
    project_links = []
    for name in ("GivreClone.App/GivreClone.App.csproj", "GivreClone.Engine/GivreClone.Engine.csproj",
                 "GivreClone.Protocol/GivreClone.Protocol.csproj", "GivreRelayAgent/GivreRelayAgent.csproj"):
        p = root / name
        if p.is_file() and not p.is_symlink():
            projects[name] = {"sha256": sha(p.read_bytes())}
            tree = ET.fromstring(p.read_bytes())
            for node in tree.iter():
                if node.tag.split("}")[-1] == "ProjectReference" and "Include" in node.attrib:
                    project_links.append({"project": name, "include": node.attrib["Include"]})
    callsites = []
    candidate_definitions = []
    priority = {x[0]: {"requiredNamespace": x[1] + "." + x[2],
                           "symbols": list(x[3]), "impact": x[4], "evidence": []} for x in TOP}
    per_file = {}
    relay_files = []
    for path, rel in safe_files(root):
        data = path.read_bytes()
        # Strict UTF-8 source only; reject rather than hide undecodable code.
        body = data.decode("utf-8-sig")
        lines = body.splitlines()
        imports = []
        matches = []
        for n, line in enumerate(lines, 1):
            # Lexical approximation: remove obvious line comments, not C# semantics.
            code = line.split("//", 1)[0]
            if not code.strip():
                continue
            m = USING.search(code)
            if m:
                name = m.group(2) + (("." + m.group(3)) if m.group(3) else "")
                imports.append({"line": n, "namespace": name, "alias": m.group(1)})
                matches.append({"line": n, "kind": "using", "symbol": name})
            if NAMESPACE.search(code):
                candidate_definitions.append({"path": rel, "line": n})
            # Directly qualified member/types with exact namespace; no source text recorded.
            for q in QUALIFIED.finditer(code):
                matches.append({"line": n, "kind": "qualified", "symbol": q.group(0)})
        imported = {p["namespace"] for p in imports}
        # Must have the corresponding exact namespace import or directly qualify
        # the symbol; finding the same word in unrelated C# proves nothing.
        for key, nsroot, branch, symbols, _ in TOP:
            full_ns = nsroot + "." + branch
            for n, line in enumerate(lines, 1):
                code = line.split("//", 1)[0]
                if not code.strip() or re.match(r"\s*(?:global\s+)?using\b", code):
                    continue
                for name in symbols:
                    direct = full_ns + "." + name
                    if (full_ns in imported and re.search(r"\b" + re.escape(name) + r"\b", code)) or \
                       re.search(r"\b" + re.escape(direct) + r"\b", code):
                        priority[key]["evidence"].append({"path": rel, "line": n, "symbol": name,
                                                           "lineSha256": sha(line.encode("utf-8"))})
        if matches:
            per_file[rel] = {"sha256": sha(data), "references": matches}
            callsites.extend({"path": rel, **m} for m in matches)
        if rel.startswith("GivreRelayAgent/") or rel.startswith("GivreClone.App/Services/Relay/"):
            relay_files.append({"path": rel, "sha256": sha(data)})
    for item in priority.values():
        hits = item["evidence"]
        item["count"] = len(hits)
        item["files"] = sorted({x["path"] for x in hits})
        item["examples"] = hits[:12]  # bounded proof set per interface
        del item["evidence"]

    return {
        "schema": "MZP2_DEPENDENCY_CALLSITE_V1",
        "proofLevel": "SOURCE_LEXICAL_ONLY_NOT_COMPILED_NOT_RUNTIME",
        "root": "GivreWork",
        "searchScope": list(SRC),
        "excludedGeneratedStubs": "tools/MZP0RecoveryHarness/MZP0GivreCoreStubs.cs",
        "projects": projects,
        "projectLinks": sorted(project_links, key=lambda x: (x["project"], x["include"])),
        "references": sorted(callsites, key=lambda x: (x["path"], x["line"], x["kind"], x["symbol"])),
        "referencingFiles": dict(sorted(per_file.items())),
        "topFive": priority,
        "candidateProviderDeclarations": candidate_definitions,
        "relaySource": {"project": "GivreRelayAgent/GivreRelayAgent.csproj",
                        "assemblyName": "GivreRelay", "isSubstituteForCoreOrLauncher": False,
                        "files": relay_files},
        "dependencyProjectAvailability": {
            "GivreCore": (root.parent / "GivreProxy/GivreCore/GivreCore.csproj").is_file(),
            "GivreLauncher.Core": (root.parent / "GivreLauncher/GivreLauncher.Core/GivreLauncher.Core.csproj").is_file(),
        },
        "sourceProviderVerified": False,
        "prodCompilationCertified": False,
        "runtimeSessionOrAckCertified": False,
    }


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    ap.add_argument("--write", type=Path, metavar="JSON_FILE")
    ap.add_argument("--verify", type=Path, metavar="JSON_FILE")
    args = ap.parse_args()
    root = args.root.resolve()
    result = collect(root)
    blob = json.dumps(result, indent=2, ensure_ascii=False, sort_keys=True) + "\n"
    if args.write:
        # Never overwrite consumer source or any arbitrary file.
        allowed = (Path(__file__).resolve().parent / "MZP2DependencyInventory.json").resolve()
        if args.write.resolve() != allowed:
            ap.error("--write is only permitted for tools/MZP2DependencyInventory.json")
        allowed.write_text(blob, encoding="utf-8")
    if args.verify:
        if not args.verify.is_file() or args.verify.read_text(encoding="utf-8") != blob:
            print("MZP2 STALE_SOURCE_OR_INVENTORY", file=sys.stderr)
            return 2
        print("MZP2 VERIFIED_SOURCE_SNAPSHOT")
    else:
        print("MZP2 SOURCE_ONLY refs={0} files={1} relay_files={2} provider={3}".format(
            len(result["references"]), len(result["referencingFiles"]),
            len(result["relaySource"]["files"]), result["sourceProviderVerified"]))
        for key, group in result["topFive"].items():
            print(f"{key}: {group['count']} lexical hits in {len(group['files'])} files")
    return 0


if __name__ == "__main__":
    sys.exit(main())
