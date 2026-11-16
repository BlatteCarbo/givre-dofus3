#!/usr/bin/env python3
"""Conservative DofusDB partner map collector/normalizer (stdlib only).

The public site is currently robots-disallowed for AI user agents. In this
environment probe-site must stop at robots. normalize consumes explicit
operator-supplied partner exports, never claiming to know the site's JSON API.
No resulting data is authorized for MCP, runtime, or AI ingestion by this tool.
"""

from __future__ import annotations

import argparse
import hashlib
from html.parser import HTMLParser
import json
import os
from pathlib import Path
import re
import sys
import time
from datetime import datetime, timezone
from email.utils import parsedate_to_datetime
from urllib import error, parse, request, robotparser

HOSTS = frozenset({"dofusdb.fr", "beta.dofusdb.fr"})
PAGE_PATHS = frozenset({"/tools/map", "/almanax/tools/map"})
USER_AGENT = "ChatGPT-User"  # Honest identification; site's robots.txt blocks it.
MAX_PAGES = 8
MAX_BYTES = 2 * 1024 * 1024
MAX_MAPS = 30000
MAX_PER_MAP = 512
GAME_VERSION = re.compile(r"^[0-9]{1,3}(?:\.[0-9]{1,4}){3}$")
KINDS = frozenset({"ordinary", "door", "stairs", "cave-entry", "cave-exit", "zaap", "portal"})
DIRECTIONS = frozenset({"north", "south", "east", "west", "up", "down"})
HEX = re.compile(r"^[a-f0-9]{64}$")


class ImportFailure(ValueError):
    pass


def canonical_url(raw: str, *, robots: bool = False) -> str:
    try:
        parsed = parse.urlsplit(raw)
        hostname, port = parsed.hostname, parsed.port
    except ValueError as ex:
        raise ImportFailure("Malformed site URL") from ex
    if (parsed.scheme != "https" or hostname not in HOSTS
            or port is not None or parsed.username or parsed.password
            or parsed.fragment):
        raise ImportFailure("URL outside the HTTPS DofusDB site allowlist")
    if parsed.path not in ({"/robots.txt"} if robots else PAGE_PATHS):
        raise ImportFailure("URL path outside the map-page allowlist")
    if len(raw) > 1024:
        raise ImportFailure("Unsafe or unbounded URL")
    if parsed.query:
        try:
            params = parse.parse_qsl(parsed.query, keep_blank_values=True, strict_parsing=True)
        except ValueError as ex:
            raise ImportFailure("Malformed query") from ex
        if (len(params) != 1 or params[0][0] != "page"
                or not params[0][1].isdigit()
                or not 1 <= int(params[0][1]) <= MAX_PAGES
                or parsed.query != f"page={params[0][1]}"):
            raise ImportFailure("Only bounded page=1..8 query is supported")
    return parse.urlunsplit((parsed.scheme, parsed.netloc, parsed.path, parsed.query, ""))


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def strict_json(data: bytes):
    def no_duplicates(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ImportFailure(f"Duplicate JSON key: {key}")
            result[key] = value
        return result

    try:
        return json.loads(data, object_pairs_hook=no_duplicates,
                          parse_constant=lambda _: (_ for _ in ()).throw(ImportFailure("Nonfinite number")))
    except (UnicodeError, json.JSONDecodeError) as ex:
        raise ImportFailure(f"Invalid JSON: {ex}") from ex


def require_object(value, keys, where):
    if not isinstance(value, dict) or set(value) - set(keys):
        raise ImportFailure(f"{where}: invalid object or unexpected keys")
    return value


def integer(value, where, *, minimum=0, maximum=2**53 - 1, optional=False):
    if optional and value is None:
        return None
    if type(value) is not int or value < minimum or value > maximum:
        raise ImportFailure(f"{where}: invalid integer")
    return value


def optional_label(value, where):
    if value is None:
        return None
    if not isinstance(value, str) or not value or len(value) > 100 or any(ord(c) < 32 for c in value):
        raise ImportFailure(f"{where}: invalid label")
    return value


def optional_position(value, where):
    if value is None:
        return None
    require_object(value, {"x", "y"}, where)
    if set(value) != {"x", "y"}:
        raise ImportFailure(f"{where}: both x and y required")
    return {
        "x": integer(value["x"], where + ".x", minimum=-10000, maximum=10000),
        "y": integer(value["y"], where + ".y", minimum=-10000, maximum=10000),
    }


def _optional_cell(record, key, where):
    return integer(record.get(key), where + "." + key, minimum=0, maximum=10000, optional=True)


def normalize(raw: bytes, game_version: str, source_url: str) -> tuple[dict, dict]:
    """Normalize an explicit v1 partner export. Never infer adjoining maps.

    'Proven' means only that the source export explicitly supplies toMapId.
    There is no verification of game/runtime connectivity or source ownership.
    """
    source_url = canonical_url(source_url)
    if not GAME_VERSION.fullmatch(game_version):
        raise ImportFailure("Invalid game version")
    root = strict_json(raw)
    require_object(root, {"schemaVersion", "exportKind", "gameVersion", "maps"}, "root")
    if root.get("schemaVersion") != 1 or root.get("exportKind") != "partner-map-records":
        raise ImportFailure("Unknown export schema; no speculative DofusDB adapter")
    if root.get("gameVersion") != game_version:
        raise ImportFailure("Export game version differs from requested version")
    records = root.get("maps")
    if not isinstance(records, list) or len(records) > MAX_MAPS:
        raise ImportFailure("Unbounded or invalid maps array")
    maps = []
    links = []
    seen = set()
    for i, rec in enumerate(records):
        prefix = f"maps[{i}]"
        require_object(rec, {"mapId", "worldId", "worldMapId", "subAreaId", "layer",
                             "floor", "position", "transitions", "resources", "zaaps"}, prefix)
        mid = integer(rec.get("mapId"), prefix + ".mapId", minimum=1)
        if mid in seen:
            raise ImportFailure("Duplicate mapId (ambiguous layer/identity)")
        seen.add(mid)
        layer = optional_label(rec.get("layer"), prefix + ".layer")
        floor = integer(rec.get("floor"), prefix + ".floor",
                        minimum=-1000, maximum=1000, optional=True)
        trans = rec.get("transitions", [])
        resources = rec.get("resources", [])
        zaaps = rec.get("zaaps", [])
        if any(not isinstance(v, list) or len(v) > MAX_PER_MAP
               for v in (trans, resources, zaaps)):
            raise ImportFailure(f"{prefix}: invalid/unbounded entries")
        clean = {
            "mapId": mid,
            "worldId": integer(rec.get("worldId"), prefix + ".worldId", optional=True),
            "worldMapId": integer(rec.get("worldMapId"), prefix + ".worldMapId",
                                  minimum=-1000000, optional=True),
            "subAreaId": integer(rec.get("subAreaId"), prefix + ".subAreaId", optional=True),
            "layer": layer,
            "floor": floor,
            "position": optional_position(rec.get("position"), prefix + ".position"),
            "resources": [], "zaaps": [], "transitions": [],
            "verification": "UNVERIFIED",
            "evidencePointer": f"/maps/{i}",
        }
        for j, res in enumerate(resources):
            where = f"{prefix}.resources[{j}]"
            require_object(res, {"resourceId", "cellId"}, where)
            clean["resources"].append({
                "resourceId": integer(res.get("resourceId"), where + ".resourceId", minimum=1),
                "cellId": _optional_cell(res, "cellId", where),
                "evidencePointer": f"/maps/{i}/resources/{j}",
                "verification": "UNVERIFIED",
            })
        for j, zaap in enumerate(zaaps):
            where = f"{prefix}.zaaps[{j}]"
            require_object(zaap, {"cellId"}, where)
            clean["zaaps"].append({
                "cellId": _optional_cell(zaap, "cellId", where),
                "evidencePointer": f"/maps/{i}/zaaps/{j}",
                "verification": "UNVERIFIED",
            })
        for j, edge in enumerate(trans):
            where = f"{prefix}.transitions[{j}]"
            require_object(edge, {"toMapId", "kind", "direction", "fromCellId", "toCellId"}, where)
            target = integer(edge.get("toMapId"), where + ".toMapId",
                             minimum=1, optional=True)
            kind = edge.get("kind")
            direction = edge.get("direction")
            if kind not in KINDS or (direction is not None and direction not in DIRECTIONS):
                raise ImportFailure(f"{where}: unknown transition kind/direction")
            actual = {
                "fromMapId": mid, "toMapId": target,
                "kind": kind, "direction": direction,
                "fromCellId": _optional_cell(edge, "fromCellId", where),
                "toCellId": _optional_cell(edge, "toCellId", where),
                "evidencePointer": f"/maps/{i}/transitions/{j}",
                "verification": "EXPLICIT_IN_UNVERIFIED_EXPORT" if target is not None else "UNKNOWN_TARGET",
            }
            clean["transitions"].append(actual)
            if target is not None:
                links.append(actual)
        maps.append(clean)
    # Referential consistency is distinct from in-game proof.
    for link in links:
        link["targetInExport"] = link["toMapId"] in seen
        if not link["targetInExport"]:
            link["verification"] = "EXPLICIT_TARGET_NOT_IN_EXPORT"
    normalized = {
        "schemaVersion": 1, "source": "dofusdb-partner-site-export",
        "gameVersion": game_version, "sitePage": source_url,
        "publisherVerified": False,
        "runtimeVerified": False, "mcpAiAccess": "BLOCKED_PENDING_CONTRACT_EXCEPTION",
        "routeEligibility": "UNVERIFIED",
        "maps": maps,
    }
    provenance = {
        "sourceCaptureSha256": sha256(raw), "sourceCaptureBytes": len(raw),
        "sourceKind": "OPERATOR_SUPPLIED_PARTNER_EXPORT",
        "sitePage": source_url, "gameVersion": game_version,
        "observedSiteSchema": False, "sourcePublisherVerified": False,
        "explicitTransitions": len(links), "mapCount": len(maps),
    }
    return normalized, provenance


def normalize_map_positions_sample(raw: bytes, game_version: str) -> tuple[dict, dict]:
    """Bounded offline adapter for observed /map-positions Feathers page shape.

    The only observed real record proved field types, not map adjacency,
    floors, item nodes, zaaps, or an authenticated game version. This function
    never fetches from the public API.
    """
    if not GAME_VERSION.fullmatch(game_version):
        raise ImportFailure("Invalid declared game version")
    root = strict_json(raw)
    require_object(root, {"total", "limit", "skip", "data"}, "map-positions page")
    total = integer(root.get("total"), "total", maximum=100000000)
    limit = integer(root.get("limit"), "limit", maximum=100)
    skip = integer(root.get("skip"), "skip", maximum=100000000)
    rows = root.get("data")
    if not isinstance(rows, list) or len(rows) > 100 or len(rows) > limit:
        raise ImportFailure("Unbounded map-positions rows")
    if len(rows) and (limit == 0 or skip >= total):
        raise ImportFailure("Page bounds inconsistent")
    maps = []
    ids = set()
    for i, rec in enumerate(rows):
        where = f"data[{i}]"
        if not isinstance(rec, dict) or len(rec) > 96:
            raise ImportFailure(where + ": bad or unbounded row")
        # First actual bounded page includes id == 0; preserve the observed
        # sentinel-like record instead of inventing a positive mapId.
        mid = integer(rec.get("id"), where + ".id", minimum=0)
        if mid in ids:
            raise ImportFailure("Duplicate map ID in source page")
        ids.add(mid)
        pos = optional_position({"x": rec.get("posX"), "y": rec.get("posY")},
                                where + ".position")
        transition_flag = rec.get("isTransition")
        if transition_flag is not None and type(transition_flag) is not bool:
            raise ImportFailure(where + ": nonboolean isTransition")
        maps.append({
            "mapId": mid,
            "worldId": None,
            "worldMapId": integer(rec.get("worldMap"), where + ".worldMap",
                                  minimum=-1000000, optional=True),
            "subAreaId": integer(rec.get("subAreaId"), where + ".subAreaId", optional=True),
            "layer": None, "floor": None, "position": pos,
            "siteTransitionFlag": transition_flag,
            "resources": [], "zaaps": [], "transitions": [],
            "verification": "UNVERIFIED_ZERO_ID" if mid == 0 else "UNVERIFIED",
            "evidencePointer": f"/data/{i}",
        })
    payload = {
        "schemaVersion": 1, "source": "dofusdb-public-api-map-positions-page",
        "gameVersion": game_version, "declaredGameVersionUnverified": True,
        "sourceEndpoint": "https://api.dofusdb.fr/map-positions",
        "publisherVerified": False, "runtimeVerified": False,
        "mcpAiAccess": "BLOCKED_PENDING_CONTRACT_EXCEPTION",
        "routeEligibility": "UNKNOWN_NO_EDGES_IN_PAGE",
        "maps": maps,
    }
    provenance = {
        "sourceCaptureSha256": sha256(raw), "sourceCaptureBytes": len(raw),
        "sourceKind": "OPERATOR_SUPPLIED_PUBLIC_API_PAGE_FOR_ISOLATED_REVIEW",
        "observedSchema": "Feathers collection: total/limit/skip/data",
        "declaredGameVersionNotFromSource": True,
        "pageTotal": total, "pageSkip": skip, "pageLimit": limit,
        "mapCount": len(maps), "explicitTransitions": 0,
    }
    return payload, provenance


def encode_json(value) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True,
                       allow_nan=False) + "\n").encode("utf-8")


def atomic_write(path: Path, data: bytes, *, checkpoint=False):
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + ".tmp")
    if tmp.exists():
        raise ImportFailure("Temporary output already exists; manual inspection required")
    try:
        with tmp.open("xb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        if path.exists() and not checkpoint:
            raise ImportFailure("Refuse to overwrite existing output")
        tmp.replace(path)
    finally:
        if tmp.exists():
            tmp.unlink()


def offline_import(input_file: Path, output_dir: Path, game_version: str,
                   site_page: str, *, api_snapshot=False):
    if not input_file.is_file() or input_file.stat().st_size > MAX_BYTES:
        raise ImportFailure("Partner export absent or exceeds 2 MiB")
    raw = input_file.read_bytes()
    if api_snapshot:
        payload, prov = normalize_map_positions_sample(raw, game_version)
    else:
        payload, prov = normalize(raw, game_version, site_page)
    data = encode_json(payload)
    source_hash = sha256(raw)
    data_hash = sha256(data)
    if output_dir.exists() and any(output_dir.iterdir()):
        raise ImportFailure("Refuse to overwrite an existing output directory")
    manifest = {
        "schemaVersion": 1, "status": "UNVERIFIED",
        "source": payload["source"],
        "gameVersion": game_version,
        "capturedAtUtc": datetime.now(timezone.utc).isoformat(),
        "sourceCapture": {"file": "partner-source-capture.json",
                          "sha256": source_hash, "bytes": len(raw)},
        "normalized": {"file": "maps.normalized.json", "sha256": data_hash, "bytes": len(data)},
        "provenance": prov, "licensePublic": "LPNC-IA 1.0 (2026)",
        "partnerExceptionVerified": False,
        "allowedForAiMcpOrGameplay": False,
    }
    atomic_write(output_dir / "partner-source-capture.json", raw)
    atomic_write(output_dir / "maps.normalized.json", data)
    atomic_write(output_dir / "manifest.json", encode_json(manifest))
    return manifest


class _NoRedirect(request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class Clock:
    def now(self):
        return time.monotonic()

    def sleep(self, seconds):
        time.sleep(seconds)


class HttpClient:
    def __init__(self, clock=None, transport=None):
        self.clock = clock or Clock()
        self.transport = transport or self._real_get
        self.last_request = None

    @staticmethod
    def _real_get(url):
        opener = request.build_opener(_NoRedirect)
        req = request.Request(url, headers={
            "User-Agent": USER_AGENT,
            "Accept": "text/html, text/plain, application/json",
        })
        try:
            with opener.open(req, timeout=12) as response:
                status, headers = response.status, dict(response.headers.items())
                body = response.read(MAX_BYTES + 1)
        except error.HTTPError as ex:
            status, headers = ex.code, dict(ex.headers.items())
            body = ex.read(min(MAX_BYTES, 8192) + 1)
        return status, headers, body

    def get(self, url):
        if self.last_request is not None:
            delta = self.clock.now() - self.last_request
            if delta < 1.0:
                self.clock.sleep(1.0 - delta)
        self.last_request = self.clock.now()
        status, headers, body = self.transport(url)
        if len(body) > MAX_BYTES:
            raise ImportFailure("Remote response larger than 2 MiB")
        return status, {str(k).lower(): str(v) for k, v in headers.items()}, body


def retry_seconds(header):
    if not header:
        return 2
    if header.isdigit():
        return int(header[:9])
    try:
        when = parsedate_to_datetime(header)
        return max(0, int((when - datetime.now(timezone.utc)).total_seconds()))
    except (ValueError, TypeError, OverflowError):
        return 2


def bounded_get(http, url):
    """GET only: max three attempts, 429 retry up to 10s; 403 stops immediately."""
    for attempt in range(3):
        status, headers, body = http.get(url)
        if status == 403:
            return "BLOCKED_HTTP_403", None
        if status == 429:
            wait = retry_seconds(headers.get("retry-after"))
            if wait > 10 or attempt == 2:
                return "RETRY_LATER_429", None
            http.clock.sleep(max(1, 2 ** attempt, wait))
            continue
        if status != 200:
            return "BLOCKED_HTTP_STATUS_" + str(status), None
        # Loading a reCAPTCHA JavaScript library in an ordinary page is not
        # proof of an active challenge. Stop only on recognizable challenges.
        preview = body[:32768].lower()
        if any(signal in preview for signal in (
                b"cf-chl-", b"captcha challenge", b"verify you are human",
                b"g-recaptcha-response", b"captcha verification required")):
            return "BLOCKED_CAPTCHA", None
        return "OK", (headers, body)
    return "RETRY_LATER_429", None


class _NextPage(HTMLParser):
    def __init__(self):
        super().__init__()
        self.href = None

    def handle_starttag(self, tag, attrs):
        attr = dict(attrs)
        if tag == "a" and "next" in attr.get("rel", "").lower().split() and self.href is None:
            self.href = attr.get("href")


def probe_site(start_url: str, output_dir: Path, *, max_pages=2, http=None, resume=False):
    start_url = canonical_url(start_url)
    if not 1 <= max_pages <= MAX_PAGES:
        raise ImportFailure("max-pages must be 1..8")
    if not resume and output_dir.exists() and any(output_dir.iterdir()):
        raise ImportFailure("Probe output already exists; preserve prior checkpoint/captures")
    if resume:
        checkpoint_file = output_dir / "checkpoint.json"
        if not checkpoint_file.is_file() or checkpoint_file.stat().st_size > 128 * 1024:
            raise ImportFailure("Checkpoint missing/oversized")
        checkpoint = strict_json(checkpoint_file.read_bytes())
        require_object(checkpoint, {"schemaVersion", "startUrl", "status", "siteAccess",
                                   "aiMcpAccess", "pages", "pendingUrls"}, "checkpoint")
        if (checkpoint["schemaVersion"] != 1 or checkpoint["startUrl"] != start_url
                or checkpoint["status"] != "UNVERIFIED_PARTIAL"
                or not isinstance(checkpoint["pages"], list)
                or not 0 < len(checkpoint["pages"]) <= MAX_PAGES
                or not isinstance(checkpoint["pendingUrls"], list)
                or not 0 < len(checkpoint["pendingUrls"]) <= MAX_PAGES):
            raise ImportFailure("Only partial matching checkpoints can be resumed")
        for i, page in enumerate(checkpoint["pages"]):
            require_object(page, {"url", "file", "bytes", "sha256", "status"}, "page")
            if (page["file"] != f"captures/page-{i:03d}.html"
                    or canonical_url(page["url"]) != page["url"]
                    or not isinstance(page["sha256"], str)
                    or not HEX.fullmatch(page["sha256"])):
                raise ImportFailure("Checkpoint page metadata tampered")
            capture = output_dir / page["file"]
            if (not capture.is_file() or capture.stat().st_size != page["bytes"]
                    or sha256(capture.read_bytes()) != page["sha256"]):
                raise ImportFailure("Checkpoint capture hash/size mismatch")
        for url in checkpoint["pendingUrls"]:
            canonical_url(url)
        if len(checkpoint["pages"]) >= max_pages:
            raise ImportFailure("Increase --max-pages within 8 before resuming")
    http = http or HttpClient()
    robots_url = parse.urlunsplit(("https", parse.urlsplit(start_url).netloc,
                                   "/robots.txt", "", ""))
    # Refuse to crawl if robots cannot be read, is too large or malformed.
    status, result = bounded_get(http, canonical_url(robots_url, robots=True))
    if not resume:
        checkpoint = {
            "schemaVersion": 1, "startUrl": start_url, "status": "BLOCKED_ROBOTS_UNKNOWN",
            "siteAccess": "REVIEW_ONLY", "aiMcpAccess": "BLOCKED_PENDING_CONTRACT_EXCEPTION",
            "pages": [], "pendingUrls": [start_url],
        }
    # Every actual page is checkpointed immediately; an interrupted probe may
    # be reviewed manually without risking a restart and duplicate requests.
    def save_checkpoint():
        atomic_write(output_dir / "checkpoint.json", encode_json(checkpoint), checkpoint=True)

    save_checkpoint()
    if status == "OK":
        headers, body = result
        try:
            if "text/plain" not in headers.get("content-type", "") or len(body) > 128 * 1024:
                raise ImportFailure("Robots content type or size untrusted")
            rules = robotparser.RobotFileParser()
            rules.parse(body.decode("utf-8-sig").splitlines())
            if not rules.can_fetch(USER_AGENT, start_url):
                checkpoint["status"] = "BLOCKED_ROBOTS"
            else:
                checkpoint["status"] = "READY"
                visited = {page["url"] for page in checkpoint["pages"]}
                while checkpoint["pendingUrls"] and len(checkpoint["pages"]) < max_pages:
                    url = canonical_url(checkpoint["pendingUrls"].pop(0))
                    if url in visited:
                        continue
                    visited.add(url)
                    # Next page may change host; read that host's robots separately.
                    if parse.urlsplit(url).netloc != parse.urlsplit(start_url).netloc:
                        checkpoint["status"] = "BLOCKED_CROSS_HOST_NEXT"
                        break
                    if not rules.can_fetch(USER_AGENT, url):
                        checkpoint["status"] = "BLOCKED_ROBOTS"
                        break
                    page_status, response = bounded_get(http, url)
                    if page_status != "OK":
                        checkpoint["status"] = page_status
                        break
                    headers, page = response
                    if "text/html" not in headers.get("content-type", ""):
                        checkpoint["status"] = "BLOCKED_UNEXPECTED_CONTENT_TYPE"
                        break
                    index = len(checkpoint["pages"])
                    name = f"page-{index:03d}.html"
                    atomic_write(output_dir / "captures" / name, page)
                    checkpoint["pages"].append({
                        "url": url, "file": "captures/" + name,
                        "bytes": len(page), "sha256": sha256(page),
                        "status": "UNVERIFIED_STATIC_HTML",
                    })
                    save_checkpoint()
                    parser = _NextPage()
                    parser.feed(page.decode("utf-8", "replace"))
                    if parser.href:
                        next_url = canonical_url(parse.urljoin(url, parser.href))
                        if next_url not in visited:
                            checkpoint["pendingUrls"].append(next_url)
                    save_checkpoint()
                if checkpoint["status"] == "READY":
                    checkpoint["status"] = ("UNVERIFIED_PARTIAL" if checkpoint["pendingUrls"]
                                             else "UNVERIFIED_STATIC_HTML")
        except (UnicodeError, ValueError, ImportFailure):
            checkpoint["status"] = "BLOCKED_INVALID_ROBOTS_OR_LINK"
    else:
        checkpoint["status"] = status
    save_checkpoint()
    return checkpoint


def main(argv=None):
    parser = argparse.ArgumentParser(description="DofusDB map importer - review only")
    sub = parser.add_subparsers(dest="command", required=True)
    probe = sub.add_parser("probe-site", help="Robots-aware HTML inspection, never the API")
    probe.add_argument("--url", default="https://dofusdb.fr/tools/map")
    probe.add_argument("--output-dir", type=Path, required=True)
    probe.add_argument("--max-pages", type=int, default=2)
    probe.add_argument("--resume", action="store_true",
                       help="Continue only a verified, partial checkpoint; recheck robots")
    imp = sub.add_parser("normalize", help="Normalize explicit partner-export JSON (offline)")
    imp.add_argument("--input", type=Path, required=True)
    imp.add_argument("--output-dir", type=Path, required=True)
    imp.add_argument("--game-version", required=True)
    imp.add_argument("--site-page", default="https://dofusdb.fr/tools/map")
    imp.add_argument("--api-snapshot-review", action="store_true",
                     help="Only locally supplied Feathers map-positions page; no API calls")
    args = parser.parse_args(argv)
    try:
        if args.command == "probe-site":
            result = probe_site(args.url, args.output_dir,
                                max_pages=args.max_pages, resume=args.resume)
        else:
            result = offline_import(args.input, args.output_dir, args.game_version,
                                    args.site_page, api_snapshot=args.api_snapshot_review)
        print(json.dumps({"status": result.get("status"), "outputDir": str(args.output_dir),
                          "aiMcpAccess": "BLOCKED_PENDING_CONTRACT_EXCEPTION"},
                         ensure_ascii=False))
        return 0 if result.get("status") == "UNVERIFIED" else 2
    except (ImportFailure, OSError) as ex:
        print(f"ERROR: {ex}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
