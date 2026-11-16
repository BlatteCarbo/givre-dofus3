#!/usr/bin/env python3
"""Prepare an operator-owned, hash-bound cross-patch Socket login review.

The raw previous mapping remains labelled with its original build. Matching
Protodec .proto files prove identical *extracted schema only*; this receipt
never certifies wire aliases, real login, or server acknowledgments.
"""

import argparse
from hashlib import sha256
import json
import os
from pathlib import Path
import re
import sys


def fail(message):
    raise ValueError(message)


def digest_file(path):
    h = sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest().upper()


def fingerprint_client(root):
    files = [
        "Dofus_Data/StreamingAssets/version", "Dofus.exe", "GameAssembly.dll",
        "Dofus_Data/il2cpp_data/Metadata/global-metadata.dat",
    ]
    result = sha256()
    for name in files:
        path = root.joinpath(*name.split("/"))
        if not path.is_file() or path.is_symlink() or path.stat().st_size > 512 * 1024 * 1024:
            fail("Missing, redirected or oversized installed binary: " + name)
        result.update((name + ":" + str(path.stat().st_size) + ":").encode("utf-8"))
        with path.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                result.update(block)
    return result.hexdigest().upper()


def validate_version(marker_path, expected):
    if marker_path.stat().st_size > 2048 or marker_path.is_symlink():
        fail("Unsafe installed marker")
    text = marker_path.read_text(encoding="utf-8-sig")
    versions = re.findall(r"(?m)^Version=([^\r\n]+)\r?$", text)
    if versions != [expected]:
        fail("Installed client version does not equal requested target")


def prepare(*, game_root, data_root, source_proto, target_proto,
            source_build, target_build, operator_confirmed, write):
    if not operator_confirmed:
        fail("Explicit operator confirmation required")
    for v in (source_build, target_build):
        if not re.fullmatch(r"[0-9]+(?:\.[0-9]+){3}", v):
            fail("Invalid four-part game version")
    if source_build == target_build or source_build.split(".")[:2] != target_build.split(".")[:2]:
        fail("Not a same-family cross-patch review")
    game_root, data_root = Path(game_root).resolve(), Path(data_root).resolve()
    # Never write into a game install or another account's folders.
    if data_root.name.lower() != "data" or "givreclone.app" not in [p.lower() for p in data_root.parts]:
        fail("Output must be the local GivreClone.App data directory")
    if game_root in data_root.parents or data_root in game_root.parents:
        fail("Review destination must not be in the installed game")
    marker = game_root / "Dofus_Data/StreamingAssets/version"
    validate_version(marker, target_build)
    source_proto, target_proto = Path(source_proto).resolve(), Path(target_proto).resolve()
    for proto in (source_proto, target_proto):
        if not proto.is_file() or proto.is_symlink() or not 100_000 <= proto.stat().st_size <= 20_000_000:
            fail("Missing or invalid extracted protocol candidate")
    old_sha, new_sha = digest_file(source_proto), digest_file(target_proto)
    if old_sha != new_sha:
        fail("Extracted schemas are DIFFERENT: no cross-patch review allowed")
    bundle = data_root / "bundle-build.json"
    if not bundle.is_file() or bundle.is_symlink():
        fail("Missing original mapping build declaration")
    if json.loads(bundle.read_text(encoding="utf-8-sig")).get("build") != source_build:
        fail("Mapping source build mismatch")
    pack = {}
    for name in ("matching_current.json", "fields.json"):
        path = data_root / name
        if not path.is_file() or path.is_symlink() or path.stat().st_size > 20_000_000:
            fail("Missing or redirected mapping: " + name)
        pack[name.replace(".", "_") + "Sha256"] = digest_file(path)
    receipt = dict(
        operatorConfirmedSameMappings=True,
        reviewOnly=True,
        sourceMappingBuild=source_build,
        installedGameBuild=target_build,
        sourceProtoSha256=old_sha,
        targetProtoSha256=new_sha,
        installedFingerprintSha256=fingerprint_client(game_root),
        **pack,
    )
    output = data_root / "compatibility-review.json"
    if output.is_symlink():
        fail("Redirected compatibility receipt refused")
    if write:
        content = (json.dumps(receipt, sort_keys=True, indent=2) + "\n").encode("utf-8")
        tmp = output.with_suffix(".json.new")
        if tmp.exists() or tmp.is_symlink():
            fail("Temp receipt already exists")
        with tmp.open("xb") as handle:
            handle.write(content)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(tmp, output)
    return dict(mode="written" if write else "dry_run", receiptPath=str(output),
                sourceBuild=source_build, installedBuild=target_build,
                protoSha256=old_sha, installedFingerprint=receipt["installedFingerprintSha256"],
                certified=False, serverAck=False, gameplayInjectionAuthorized=False)


def main():
    p = argparse.ArgumentParser(description="Build a local, non-certified cross-patch Socket review receipt")
    for option in ("game-root", "data-root", "source-proto", "target-proto",
                   "source-build", "target-build"):
        p.add_argument("--" + option, required=True)
    p.add_argument("--operator-confirms-same-mapping", action="store_true")
    p.add_argument("--write", action="store_true")
    args = p.parse_args()
    try:
        report = prepare(game_root=args.game_root, data_root=args.data_root,
                         source_proto=args.source_proto, target_proto=args.target_proto,
                         source_build=args.source_build, target_build=args.target_build,
                         operator_confirmed=args.operator_confirms_same_mapping,
                         write=args.write)
        print(json.dumps(report, sort_keys=True))
    except (ValueError, OSError, UnicodeError, json.JSONDecodeError) as exc:
        print(json.dumps({"mode": "BLOCKED", "reason": str(exc)}))
        raise SystemExit(2) from exc


if __name__ == "__main__":
    main()
