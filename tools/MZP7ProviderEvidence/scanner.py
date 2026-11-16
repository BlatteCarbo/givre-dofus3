"""MZP7: bounded, offline provider evidence. No live process, extraction or build.

An observed source declaration proves only a local declaration, NOT external
provenance, correct behavior, ABI compatibility or an authenticated session.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import sys
import xml.etree.ElementTree as ET
import zipfile

MAX_FILE = 24 * 1024 * 1024
MAX_SOURCE = 1024 * 1024
MAX_ARCHIVE_ENTRIES = 4096
MAX_ARCHIVE_BYTES = 64 * 1024 * 1024
MAX_MEMBER_BYTES = 1024 * 1024
MAX_SRC_FILES = 350
MAX_DIR_ENTRIES = 150
EXPECTED = {
    "GivreCore": {
        "project": "GivreCore.csproj",
        "namespace": "GivreCore",
        "required": ("ProtoField", "WireType", "RawProtobuf", "TcpProxyServer", "CapturedMessage", "Direction", "DllInjector"),
    },
    "GivreLauncher.Core": {
        "project": "GivreLauncher.Core.csproj",
        "namespace": "GivreLauncher.Core",
        "required": ("HandshakeRelay", "GameLauncher", "ProxySettings", "ZaapEmulator"),
    },
}
CONSUMERS = (
    "GivreClone.Protocol/GivreClone.Protocol.csproj",
    "GivreClone.Engine/GivreClone.Engine.csproj",
    "GivreClone.App/GivreClone.App.csproj",
)
EXAMPLES = (
    "GivreClone.Protocol/CharacterInfoReader.cs",
    "GivreClone.Protocol/EnvelopeBuilder.cs",
    "GivreClone.Protocol/FightPrepReader.cs",
    "GivreClone.Engine/BotHost.cs",
    "GivreClone.Engine/DofusCaptureHost.cs",
    "GivreClone.App/Services/LaunchService.cs",
    "GivreClone.App/Services/Transport/MitmTransport.cs",
)
DEFAULT_CANDIDATES = (
    "../GivreProxy/GivreCore/GivreCore.csproj",
    "../GivreLauncher/GivreLauncher.Core/GivreLauncher.Core.csproj",
    "../GivreAI",
    "../GivreKnowledgeCrawler",
    "../GivreSacrieurAutopilot",
    "artifacts/MZP6-Source-Modules.zip",
)
SKIP_PARTS = {".git", "bin", "obj", ".vs", "dist", "node_modules", "site-packages", "installed", "installation", "secrets", "accounts", "captures", "dumps", "cache"}
DECL_RE = re.compile(r"\b(?:class|struct|interface|enum|record\s+(?:class\s+|struct\s+)?)\s+([A-Za-z_][A-Za-z_0-9]*)")
NS_RE = re.compile(r"\bnamespace\s+(GivreCore(?:\.[\w.]+)?|GivreLauncher\.Core(?:\.[\w.]+)?)\s*[;{]")


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def root_guard(path: Path, allowed: Path) -> Path:
    """Reject symlink components, including broken links and redirects."""
    target = Path(os.path.abspath(path))
    parent = Path(os.path.abspath(allowed))
    try:
        parts = target.relative_to(parent).parts
    except ValueError as exc:
        raise ValueError("OUTSIDE_APPROVED_ROOT") from exc
    cur = parent
    is_reparse = lambda p: p.is_symlink() or (hasattr(p, "is_junction") and p.is_junction())
    if is_reparse(parent):
        raise ValueError("REPARSE_POINT")
    for part in parts:
        cur = cur / part
        if is_reparse(cur):
            raise ValueError("REPARSE_POINT")
    return target


def safe_read(path: Path, limit: int = MAX_FILE) -> bytes:
    before = path.stat()
    if before.st_size > limit:
        raise ValueError("FILE_LIMIT")
    with path.open("rb") as stream:
        data = stream.read(limit + 1)
    if len(data) > limit:
        raise ValueError("FILE_LIMIT")
    after = path.stat()
    if (before.st_size, before.st_mtime_ns, before.st_ino) != (after.st_size, after.st_mtime_ns, after.st_ino):
        raise ValueError("SOURCE_CHANGED_DURING_READ")
    return data


def strip_csharp(text: str) -> str:
    """Remove comments and strings before lexical namespace/type detection.

    This is deliberately a lexical inventory, not a Roslyn compilation.
    """
    pattern = re.compile(r'//[^\n]*|/\*[\s\S]*?\*/|@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"|\$?"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'')
    return pattern.sub(lambda m: "\n" * m.group().count("\n") + " " , text)


def source_surface(files: list[tuple[str, bytes]]) -> dict:
    declarations = set()
    namespaces = set()
    scoped = {key: set() for key in EXPECTED}
    source_count = 0
    samples = []
    for name, content in files:
        if not name.lower().endswith(".cs"):
            continue
        source_count += 1
        decoded = strip_csharp(content.decode("utf-8-sig", errors="replace"))
        ns = NS_RE.findall(decoded)
        types = DECL_RE.findall(decoded)
        namespaces.update(ns)
        declarations.update(types)
        # Avoid combining a provider namespace from file A with types from
        # unrelated file B. Ambiguous mixed-namespace files are inconclusive.
        if len(set(ns)) == 1:
            for provider, expected in EXPECTED.items():
                if ns[0] == expected["namespace"] or ns[0].startswith(expected["namespace"] + "."):
                    scoped[provider].update(types)
        if ns or types:
            samples.append({"file": name, "sha256": digest(content), "namespaces": sorted(set(ns)), "types": sorted(set(types))[:80]})
    return {"csFiles": source_count, "namespaces": sorted(namespaces), "types": sorted(declarations),
            "providerScopedTypes": {provider: sorted(types) for provider, types in scoped.items()},
            "samples": samples[:25]}


def project_surface(content: bytes) -> dict:
    doc = ET.fromstring(content)
    assembly_names = [e.text for e in doc.iter() if e.tag.split("}")[-1] == "AssemblyName" and e.text]
    references = [e.attrib.get("Include") for e in doc.iter() if e.tag.split("}")[-1] == "ProjectReference"]
    return {"declaredAssemblyNames": sorted(set(assembly_names)), "projectReferences": sorted(set(r for r in references if r))}


def classification(projects: list[str], namespace_types: dict) -> dict:
    result = {}
    for key, expected in EXPECTED.items():
        expected_ns = expected["namespace"]
        matching = sorted(t for t in namespace_types.get("providerScopedTypes", {}).get(key, []) if t in expected["required"])
        ns_match = any(ns == expected_ns or ns.startswith(expected_ns + ".") for ns in namespace_types.get("namespaces", []))
        project_match = expected["project"] in projects
        complete = project_match and ns_match and len(matching) == len(expected["required"])
        evidence = "SOURCE_NAME_SURFACE_CANDIDATE" if complete else (
            "SOURCE_PARTIAL_PROVIDER_SURFACE" if (project_match or (ns_match and matching)) else "NO_PROVIDER_DECLARATION")
        result[key] = {"level": evidence, "projectNameFound": project_match,
                       "providerNamespaceFound": ns_match, "requiredTypeNamesObserved": matching,
                       "requiredTypeNamesNotObserved": sorted(set(expected["required"]) - set(matching)),
                       "runtimeOrAbiCertified": False, "externalSourceProvenanceCertified": False}
    return result


def inspect_project(path: Path, allowed: Path) -> dict:
    projects = [path.name]
    project_bytes = safe_read(path, MAX_SOURCE)
    parsed = project_surface(project_bytes)
    files = []
    seen = 0
    directory = path.parent
    for base, dirs, names in os.walk(directory, topdown=True, followlinks=False):
        base_path = root_guard(Path(base), allowed)
        depth = len(base_path.relative_to(directory).parts)
        if depth > 4:
            raise ValueError("SOURCE_DEPTH_LIMIT")
        if len(dirs) + len(names) > MAX_DIR_ENTRIES:
            raise ValueError("SOURCE_DIRECTORY_LIMIT")
        dirs[:] = sorted(d for d in dirs if d.lower() not in SKIP_PARTS and not
                         (base_path / d).is_symlink() and not (hasattr((base_path / d), "is_junction") and (base_path / d).is_junction()))
        for name in sorted(names):
            if not name.endswith(".cs"):
                continue
            source_path = root_guard(base_path / name, allowed)
            seen += 1
            if seen > MAX_SRC_FILES:
                raise ValueError("SOURCE_FILE_COUNT_LIMIT")
            files.append((source_path.relative_to(directory).as_posix(), safe_read(source_path, MAX_SOURCE)))
    surface = source_surface(files)
    declared = parsed["declaredAssemblyNames"]
    # Project basename alone does not establish the advertised assembly identity.
    eligible = projects if not declared or path.stem in declared else []
    return {"kind": "project", "sha256": digest(project_bytes), "project": parsed,
            "assemblyNameContradictsProject": bool(declared and path.stem not in declared),
            "source": surface, "providers": classification(eligible, surface)}


def inspect_binary(path: Path) -> dict:
    data = safe_read(path)
    result = {"kind": "binary", "sha256": digest(data), "bytes": len(data),
              "level": "BINARY_BYTES_ONLY", "managedAssemblyName": None,
              "definedTypes": [], "referencedTypes": [], "providerApiCertified": False}
    try:
        import dnfile
        pe = dnfile.dnPE(data=data, fast_load=False)
        if not pe.net or not pe.net.mdtables:
            result["reason"] = "NOT_MANAGED_DOTNET_METADATA"
            return result
        tables = pe.net.mdtables
        result["managedAssemblyName"] = str(tables.Assembly.rows[0].Name) if tables.Assembly and tables.Assembly.rows else None
        types = []
        for row in tables.TypeDef.rows if tables.TypeDef else []:
            types.append(str(row.TypeNamespace) + "." + str(row.TypeName))
        refs = []
        for row in tables.TypeRef.rows if tables.TypeRef else []:
            refs.append(str(row.TypeNamespace) + "." + str(row.TypeName))
        result["definedTypes"] = sorted(t for t in types if t.startswith(("GivreCore.", "GivreLauncher.Core.")))[:150]
        result["referencedTypes"] = sorted(t for t in refs if t.startswith(("GivreCore.", "GivreLauncher.Core.")))[:150]
        result["level"] = "BINARY_MANAGED_METADATA"
    except Exception as exc:  # untrusted malformed PE/metadata raises library-specific parser exceptions
        result["reason"] = "METADATA_UNAVAILABLE_OR_INVALID:" + type(exc).__name__
    return result


def safe_zip_name(name: str) -> bool:
    parsed = PurePosixPath(name)
    return not (name.startswith(("/", "\\")) or "\\" in name or ".." in parsed.parts or ":" in name)


def inspect_archive(path: Path) -> dict:
    if path.stat().st_size > MAX_ARCHIVE_BYTES:
        raise ValueError("ARCHIVE_SIZE_LIMIT")
    result = {"kind": "archive", "sha256": digest(safe_read(path, MAX_ARCHIVE_BYTES)),
              "level": "ARCHIVE_MEMBER_NAMES", "membersChecked": 0, "providerProjects": [], "providers": {}}
    with zipfile.ZipFile(path) as archive:
        members = archive.infolist()
        if len(members) > MAX_ARCHIVE_ENTRIES:
            raise ValueError("ARCHIVE_ENTRY_COUNT_LIMIT")
        for m in members:
            if not safe_zip_name(m.filename) or ((m.external_attr >> 16) & 0o170000) == 0o120000:
                raise ValueError("ARCHIVE_PATH_OR_LINK_REJECTED")
        result["membersChecked"] = len(members)
        projects = [m for m in members if m.filename.endswith(tuple(e["project"] for e in EXPECTED.values()))]
        result["providerProjects"] = [{"path": m.filename, "size": m.file_size} for m in projects]
        files = []
        selected_projects = []
        for p in projects:
            if p.file_size > MAX_MEMBER_BYTES or p.compress_size == 0 and p.file_size > 0 or (p.compress_size and p.file_size > p.compress_size * 100):
                raise ValueError("ARCHIVE_COMPRESSION_LIMIT")
            raw = archive.read(p)
            declared = project_surface(raw)["declaredAssemblyNames"]
            basename = PurePosixPath(p.filename).stem
            if not declared or basename in declared:
                selected_projects.append(p.filename)
            prefix = p.filename.rsplit("/", 1)[0] + "/" if "/" in p.filename else ""
            relevant = [m for m in members if m.filename.startswith(prefix) and m.filename.endswith(".cs")]
            if len(relevant) > MAX_SRC_FILES:
                raise ValueError("ARCHIVE_SOURCE_LIMIT")
            for m in relevant:
                if m.file_size > MAX_MEMBER_BYTES or (m.compress_size == 0 and m.file_size > 0) or (m.compress_size and m.file_size > m.compress_size * 100):
                    raise ValueError("ARCHIVE_COMPRESSION_LIMIT")
                files.append((m.filename, archive.read(m)))
        surface = source_surface(files)
        result["source"] = surface
        result["providers"] = classification([PurePosixPath(p).name for p in selected_projects], surface)
        if projects:
            result["level"] = "ARCHIVED_SOURCE_DECLARATIONS"
    return result


def scan_candidate(path: Path, allowed: Path) -> list[dict]:
    path = root_guard(path, allowed)
    if not path.exists():
        return [{"path": str(path), "level": "MISSING", "reason": "PATH_NOT_FOUND"}]
    if path.is_dir():
        members = sorted(path.iterdir(), key=lambda p: p.name.lower())
        if len(members) > MAX_DIR_ENTRIES:
            return [{"path": str(path), "level": "INDETERMINATE", "reason": "DIRECTORY_LIMIT"}]
        # One directory level only; no bulk recursion, no installed binaries.
        safe = [p for p in members if p.is_file() and p.suffix.lower() in (".csproj", ".dll", ".zip")]
        if not safe:
            return [{"path": str(path), "level": "NO_CANDIDATE_IN_TOP_LEVEL", "entries": len(members)}]
        return [item for child in safe for item in scan_candidate(child, allowed)]
    result = {"path": str(path)}
    try:
        suffix = path.suffix.lower()
        if suffix == ".csproj":
            result.update(inspect_project(path, allowed))
        elif suffix == ".dll":
            result.update(inspect_binary(path))
        elif suffix == ".zip":
            result.update(inspect_archive(path))
        else:
            result.update({"level": "REJECTED", "reason": "UNSUPPORTED_EXTENSION"})
    except (ValueError, OSError, ET.ParseError, zipfile.BadZipFile, RuntimeError) as exc:
        result.update({"level": "INDETERMINATE", "reason": str(exc)[:120]})
    return [result]


def consumers(repo: Path) -> dict:
    info = []
    for relative in CONSUMERS:
        path = repo / relative
        content = safe_read(path, MAX_SOURCE)
        references = project_surface(content)["projectReferences"]
        info.append({"path": relative, "sha256": digest(content), "projectReferences": references,
                     "missingProjectReferences": [ref for ref in references if not (path.parent / ref.replace("\\", "/")).exists()]})
    callsites = []
    for relative in EXAMPLES:
        path = repo / relative
        if not path.is_file():
            continue
        content = safe_read(path, MAX_SOURCE)
        lines = content.decode("utf-8-sig", errors="replace").splitlines()
        hits = []
        for idx, line in enumerate(lines, 1):
            if any(word in line for word in ("GivreCore", "GivreLauncher.Core", "ProtoField", "RawProtobuf", "TcpProxyServer", "HandshakeRelay", "GameLauncher")):
                hits.append({"line": idx, "sha256": digest(line.encode("utf-8"))})
        callsites.append({"path": relative, "sha256": digest(content), "lexicalCallsiteLines": hits[:30]})
    return {"projects": info, "consumerExamples": callsites}


def run(repo: Path, allowed: Path, candidates: list[str]) -> dict:
    repo, allowed = Path(os.path.abspath(repo)), Path(os.path.abspath(allowed))
    root_guard(repo, allowed)
    observed = []
    for candidate in candidates:
        path = repo / candidate
        try:
            observed.extend(scan_candidate(path, allowed))
        except ValueError as exc:
            observed.append({"path": str(path), "level": "REJECTED", "reason": str(exc)})
    return {"schema": "MZP7ProviderEvidence.v1", "scope": "OFFLINE_BOUNDED_CANDIDATES_ONLY",
            "repo": str(repo), "consumers": consumers(repo), "candidates": observed,
            "verdict": {name: ("SOURCE_NAME_SURFACE_REQUIRES_ABI_REVIEW" if any(c.get("providers", {}).get(name, {}).get("level") == "SOURCE_NAME_SURFACE_CANDIDATE" for c in observed)
                              else "PARTIAL_SOURCE_MISSING_API" if any(c.get("providers", {}).get(name, {}).get("level") == "SOURCE_PARTIAL_PROVIDER_SURFACE" for c in observed)
                              else "UNRESOLVED_PROVIDER") for name in EXPECTED},
            "limits": {"candidateCount": len(candidates), "sourceFilesPerProject": MAX_SRC_FILES,
                       "archiveMemberCount": MAX_ARCHIVE_ENTRIES},
            "certified": {"cs0246Resolved": False, "actualRuntimeParity": False, "authenticatedSender": False}}


def main() -> int:
    parser = argparse.ArgumentParser(description="MZP7 targeted offline source/binary/archive evidence")
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--candidate", action="append", default=[], help="relative to repo; repeat; no recursive workspace scan")
    parser.add_argument("--output", type=Path, help="optional new report JSON path")
    args = parser.parse_args()
    # CLI is always constrained to the approved /givre workspace in which
    # this tool itself resides; the caller cannot redirect it to installations.
    approved = Path(__file__).resolve().parents[3]
    report = run(args.repo, approved, args.candidate if args.candidate else list(DEFAULT_CANDIDATES))
    encoded = json.dumps(report, ensure_ascii=False, indent=2, sort_keys=True) + "\n"
    if args.output:
        output = root_guard(args.output, approved)
        if output.exists():
            parser.error("refusing overwrite of existing report")
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(encoded, encoding="utf-8")
    else:
        print(encoded, end="")
    return 0


if __name__ == "__main__":
    sys.exit(main())
