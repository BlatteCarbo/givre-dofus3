#!/usr/bin/env python3
"""Offline operator-rights attestation for ONE SHA-pinned DofusDB export.

The user states their partnership permits AI/MCP. This records that statement;
it cannot verify a legal agreement, publisher identity or runtime permissions.
No source files, API keys, cookies or remote endpoints are read by the MCP here.
"""
from __future__ import annotations

import argparse
from datetime import date, datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import sys

FORMAT = "dofusdb-operator-ai-mcp-attestation-v1"
STATEMENT = "I attest I have DofusDB partner permission for the specified site/API AI and MCP use."
LABEL = re.compile(r"[A-Za-z0-9_.:-]{1,80}\Z")
VERSION = re.compile(r"(?:0|[1-9][0-9]{0,8})(?:\.(?:0|[1-9][0-9]{0,8})){3}\Z")
SHA256 = re.compile(r"[0-9a-fA-F]{64}\Z")
MAX_SOURCE_BYTES = 128 * 1024 * 1024
MAX_RECEIPT_BYTES = 8192


class ReviewRefused(ValueError):
    """Missing, ambiguous or stale local operator claims."""


def _json_no_duplicates(pairs: list[tuple[str, object]]) -> dict:
    result = {}
    for key, value in pairs:
        if key in result:
            raise ReviewRefused("duplicate-json-field")
        result[key] = value
    return result


def file_sha256(path: Path) -> str:
    if path.is_symlink() or not path.is_file():
        raise ReviewRefused("source-not-a-regular-file")
    path = path.resolve(strict=True)
    size = path.stat().st_size
    if not 0 < size <= MAX_SOURCE_BYTES:
        raise ReviewRefused("source-size-invalid")
    digest = hashlib.sha256()
    with path.open("rb") as reader:
        for chunk in iter(lambda: reader.read(65536), b""):
            digest.update(chunk)
    if path.stat().st_size != size:
        raise ReviewRefused("source-size-changed")
    return digest.hexdigest()


def create_attestation(
    *, source_path: Path, source_surface: str, game_version: str,
    operator_label: str, agreement_reference: str,
    permit_ai: bool, permit_mcp: bool, statement: str,
    scope_site: bool, scope_api: bool, valid_until: str,
) -> dict:
    """Create an untrusted operator claim, bound to one local source SHA."""
    if source_surface not in ("site", "api") or not (scope_site or scope_api):
        raise ReviewRefused("source-surface-not-attested")
    if (source_surface == "site" and not scope_site or
            source_surface == "api" and not scope_api):
        raise ReviewRefused("source-surface-not-in-scope")
    if not VERSION.fullmatch(game_version):
        raise ReviewRefused("exact-game-version-required")
    if not LABEL.fullmatch(operator_label) or not LABEL.fullmatch(agreement_reference):
        raise ReviewRefused("unsafe-or-missing-operator-reference")
    if not permit_ai or not permit_mcp or statement != STATEMENT:
        raise ReviewRefused("ai-mcp-scope-and-explicit-attestation-required")
    try:
        expiry = date.fromisoformat(valid_until)
    except ValueError as exc:
        raise ReviewRefused("invalid-expiry") from exc
    if expiry < datetime.now(timezone.utc).date():
        raise ReviewRefused("attestation-expired")
    digest = file_sha256(source_path)
    receipt = {
        "format": FORMAT,
        "source": "dofusdb",
        "sourceSurface": source_surface,
        "sourceGameVersion": game_version,
        "sourceSha256": digest,
        "scope": {
            "site": scope_site, "api": scope_api,
            "ai": permit_ai, "mcp": permit_mcp,
        },
        "operatorLabel": operator_label,
        "agreementReference": agreement_reference,
        "attestationStatement": STATEMENT,
        "validUntil": expiry.isoformat(),
        "issuedUtc": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "independentlyVerified": False,
        "runtimeAuthorized": False,
        "automaticPublicationAuthorized": False,
    }
    receipt["receiptId"] = _receipt_id(receipt)
    return receipt


def _receipt_id(receipt: dict) -> str:
    canonical = json.dumps({k: v for k, v in receipt.items() if k != "receiptId"},
                           sort_keys=True, separators=(",", ":"), ensure_ascii=True)
    return "operator-" + hashlib.sha256(canonical.encode("utf-8")).hexdigest()[:32]


def verify_attestation(
    receipt: dict, *, source_path: Path, required_surface: str,
    required_version: str, require_ai: bool = True, require_mcp: bool = True,
) -> dict:
    """Fail closed on mismatch; NEVER return independent legal verification."""
    denied = {"allowed": False, "status": "BLOCK", "independentlyVerified": False,
              "runtimeAuthorized": False, "message": "operator-claim-not-admitted"}
    if not isinstance(receipt, dict) or set(receipt) != {
        "format", "source", "sourceSurface", "sourceGameVersion", "sourceSha256",
        "scope", "operatorLabel", "agreementReference", "attestationStatement",
        "validUntil", "issuedUtc", "independentlyVerified", "runtimeAuthorized",
        "automaticPublicationAuthorized", "receiptId",
    }:
        return denied
    scope = receipt.get("scope")
    if not isinstance(scope, dict) or set(scope) != {"site", "api", "ai", "mcp"} or any(
            type(v) is not bool for v in scope.values()):
        return denied
    if (receipt["format"] != FORMAT or receipt["source"] != "dofusdb" or
            required_surface not in ("site", "api") or
            receipt["sourceSurface"] != required_surface or
            scope[required_surface] is not True or
            not VERSION.fullmatch(str(required_version)) or
            receipt["sourceGameVersion"] != required_version or
            (require_ai and scope["ai"] is not True) or
            (require_mcp and scope["mcp"] is not True) or
            receipt["attestationStatement"] != STATEMENT or
            receipt["independentlyVerified"] is not False or
            receipt["runtimeAuthorized"] is not False or
            receipt["automaticPublicationAuthorized"] is not False):
        return denied
    if not all(isinstance(receipt.get(k), str) and LABEL.fullmatch(receipt[k])
               for k in ("operatorLabel", "agreementReference")):
        return denied
    if not isinstance(receipt["sourceSha256"], str) or not SHA256.fullmatch(receipt["sourceSha256"]):
        return denied
    if not isinstance(receipt["issuedUtc"], str) or len(receipt["issuedUtc"]) > 40:
        return denied
    if not isinstance(receipt["validUntil"], str) or len(receipt["validUntil"]) != 10:
        return denied
    try:
        issued = datetime.fromisoformat(receipt["issuedUtc"])
        expiry = date.fromisoformat(receipt["validUntil"])
        now = datetime.now(timezone.utc)
        if (issued.tzinfo is None or issued > now or expiry < now.date()):
            return denied
        if receipt.get("receiptId") != _receipt_id(receipt):
            return denied
        if file_sha256(source_path).lower() != receipt["sourceSha256"].lower():
            return denied
    except (ValueError, TypeError, OSError, ReviewRefused):
        return denied
    return {
        "allowed": True, "status": "OPERATOR_ATTESTED_NOT_VERIFIED",
        "receiptId": receipt["receiptId"],
        "sourceSha256": receipt["sourceSha256"].lower(),
        "sourceGameVersion": required_version,
        "sourceSurface": required_surface,
        "rightsScope": {"ai": scope["ai"], "mcp": scope["mcp"]},
        "independentlyVerified": False, "runtimeAuthorized": False,
        "message": "scope-and-bytes-match-operator-claim-not-independently-verified",
    }


def read_receipt(path: Path) -> dict:
    if path.is_symlink() or not path.is_file() or not 0 < path.stat().st_size <= MAX_RECEIPT_BYTES:
        raise ReviewRefused("receipt-missing-or-over-limit")
    with path.open("r", encoding="utf-8") as reader:
        data = json.load(reader, object_pairs_hook=_json_no_duplicates)
    if not isinstance(data, dict):
        raise ReviewRefused("receipt-must-be-object")
    return data


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    create = sub.add_parser("attest", help="Store an explicit unverified operator claim")
    create.add_argument("--source-file", required=True, type=Path)
    create.add_argument("--surface", choices=("site", "api"), required=True)
    create.add_argument("--game-version", required=True)
    create.add_argument("--operator-label", required=True)
    create.add_argument("--agreement-reference", required=True)
    create.add_argument("--statement", required=True)
    create.add_argument("--ai", action="store_true")
    create.add_argument("--mcp", action="store_true")
    create.add_argument("--site-scope", action="store_true")
    create.add_argument("--api-scope", action="store_true")
    create.add_argument("--valid-until", required=True)
    create.add_argument("--output", required=True, type=Path)
    verify = sub.add_parser("verify", help="Check the claim against exact source bytes")
    verify.add_argument("--receipt", required=True, type=Path)
    verify.add_argument("--source-file", required=True, type=Path)
    verify.add_argument("--surface", choices=("site", "api"), required=True)
    verify.add_argument("--game-version", required=True)
    try:
        args = parser.parse_args(argv)
        if args.command == "attest":
            record = create_attestation(
                source_path=args.source_file, source_surface=args.surface,
                game_version=args.game_version, operator_label=args.operator_label,
                agreement_reference=args.agreement_reference,
                permit_ai=args.ai, permit_mcp=args.mcp,
                statement=args.statement, scope_site=args.site_scope,
                scope_api=args.api_scope, valid_until=args.valid_until)
            # Avoid overwrites, symlink destinations and implicit publication.
            if args.output.is_symlink():
                raise ReviewRefused("output-symlink-refused")
            args.output.parent.mkdir(parents=True, exist_ok=True)
            with args.output.open("x", encoding="utf-8") as writer:
                json.dump(record, writer, sort_keys=True, indent=2)
                writer.write("\n")
            output = {"status": "OPERATOR_ATTESTED_NOT_VERIFIED",
                      "receiptId": record["receiptId"], "independentlyVerified": False}
        else:
            output = verify_attestation(read_receipt(args.receipt),
                                        source_path=args.source_file,
                                        required_surface=args.surface,
                                        required_version=args.game_version)
        print(json.dumps(output, sort_keys=True))
        return 0 if args.command == "attest" or output.get("allowed") else 2
    except (OSError, ValueError, TypeError, UnicodeError, json.JSONDecodeError, ReviewRefused):
        print(json.dumps({"status": "BLOCK", "allowed": False,
                          "independentlyVerified": False, "message": "local-attestation-refused"}))
        return 2


if __name__ == "__main__":
    sys.exit(main())
