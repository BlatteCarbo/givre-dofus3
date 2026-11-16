#!/usr/bin/env python3
"""Partner-scoped public-page collector and offline export validator.

No anti-bot evasion, cookies, tokens, CAPTCHA solving, proxy rotation or API use.
The public DofusDB API licence restricts AI ingestion: every capture is review
only and is NEVER passed to Givre's AI/MCP by this program.
"""
from __future__ import annotations

import argparse
import hashlib
from html.parser import HTMLParser
import json
from pathlib import Path
import re
import time
from urllib import error, parse, request, robotparser

HOSTS = {"dofusdb.fr", "beta.dofusdb.fr", "www.dofusdb.fr"}
PREFIXES = ("/database/", "/fr/database/", "/tools/", "/fr/tools/",
            "/almanax/", "/fr/almanax/")
SEEDS = ("https://dofusdb.fr/tools/database/objects",
         "https://beta.dofusdb.fr/fr/database/items",
         "https://dofusdb.fr/tools/map")
USER_AGENT = "ChatGPT-User"  # Honest identification; stop if robots disallows.
MAX_RESPONSE = 2 * 1024 * 1024
MAX_PAGES = 10000
MIN_INTERVAL = 1.0


class Refused(RuntimeError):
    pass


def url_allowed(url: str) -> bool:
    try:
        p = parse.urlsplit(url)
        return (p.scheme == "https" and p.hostname in HOSTS
                and p.port is None and not p.username and not p.password
                and any(p.path.startswith(prefix) for prefix in PREFIXES)
                and not p.fragment and len(url) < 2048
                and len(p.query) <= 160)
    except ValueError:
        return False


def digest(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


class Links(HTMLParser):
    def __init__(self):
        super().__init__()
        self.links = []

    def handle_starttag(self, tag, attrs):
        if tag == "a":
            url = dict(attrs).get("href", "")
            if isinstance(url, str) and len(url) < 1024:
                self.links.append(url)


class NoRedirect(request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise Refused("REDIRECT_STOP")


def opener():
    return request.build_opener(NoRedirect())


def read_page(open_with, url: str, max_bytes=MAX_RESPONSE) -> tuple[bytes, str]:
    req = request.Request(url, headers={
        "User-Agent": USER_AGENT, "Accept": "text/html",
        "Accept-Encoding": "identity"})
    try:
        with open_with.open(req, timeout=15) as response:
            if response.status != 200:
                raise Refused(f"HTTP_{response.status}_STOP")
            ct = response.headers.get("Content-Type", "").lower()
            if "text/html" not in ct:
                raise Refused("NON_HTML_STOP")
            raw = response.read(max_bytes + 1)
            if len(raw) > max_bytes:
                raise Refused("PAGE_TOO_LARGE_STOP")
            lower = raw[:20000].lower()
            if b"captcha" in lower or b"cf-chl" in lower or b"turnstile" in lower:
                raise Refused("CHALLENGE_PAGE_STOP")
            return raw, ct
    except error.HTTPError as exc:
        if exc.code in (401, 403, 429):
            raise Refused(f"HTTP_{exc.code}_STOP") from exc
        raise Refused(f"HTTP_{exc.code}_STOP") from exc


def _json_atomic(path: Path, obj: dict):
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text(json.dumps(obj, ensure_ascii=False, indent=2, sort_keys=True), encoding="utf-8")
    tmp.replace(path)


def site_capture(destination: Path, seeds: list[str], max_pages: int,
                 open_with=None, sleep=time.sleep):
    """Crawl only explicitly allowed, public pages; no bypass of robots restrictions.

    A SPA page will not automatically expose all its underlying data: this
    program never interprets rendered HTML as complete object/recipe tables.
    """
    if not 1 <= max_pages <= MAX_PAGES or any(not url_allowed(x) for x in seeds):
        raise ValueError("Invalid pages/seed URL")
    destination.mkdir(parents=True, exist_ok=True)
    client = open_with or opener()
    checkpoint = destination / "checkpoint.json"
    if checkpoint.exists():
        data = json.loads(checkpoint.read_text(encoding="utf-8"))
        if data.get("schema") != 1 or data.get("purpose") != "human-review-only":
            raise Refused("INVALID_CHECKPOINT")
        queue = data["pending"]
        visited = data["captures"]
        for rec in visited:
            f = destination / rec["file"]
            if not f.is_file() or digest(f.read_bytes()) != rec["sha256"]:
                raise Refused("CHECKPOINT_HASH_MISMATCH")
    else:
        queue = list(seeds)
        visited = []
    already = {entry["url"] for entry in visited}
    robots_cache = {}
    last_request = 0.0
    status = "REVIEW_ONLY_INCOMPLETE"
    while queue and len(visited) < max_pages:
        url = queue.pop(0)
        if url in already:
            continue
        if not url_allowed(url):
            continue
        domain = parse.urlsplit(url).hostname
        if domain not in robots_cache:
            # Robots is read even when a partner claims scraping permission.
            # A separately signed waiver can be handled by an operator outside
            # this tool; this collector never evades technical restrictions.
            robots_url = f"https://{domain}/robots.txt"
            try:
                req = request.Request(robots_url, headers={"User-Agent": USER_AGENT})
                with client.open(req, timeout=15) as resp:
                    if resp.status != 200:
                        raise Refused("ROBOTS_UNAVAILABLE")
                    body = resp.read(256 * 1024 + 1)
                    if len(body) > 256 * 1024:
                        raise Refused("ROBOTS_OVERSIZED")
                robots = robotparser.RobotFileParser()
                robots.parse(body.decode("utf-8", errors="replace").splitlines())
            except Exception as exc:
                raise Refused("ROBOTS_UNAVAILABLE") from exc
            robots_cache[domain] = robots
        if not robots_cache[domain].can_fetch(USER_AGENT, url):
            status = "BLOCKED_ROBOTS"
            queue.insert(0, url)
            break
        delay = MIN_INTERVAL - (time.monotonic() - last_request)
        if delay > 0:
            sleep(delay)
        last_request = time.monotonic()
        try:
            raw, mime = read_page(client, url)
        except Refused as exc:
            status = str(exc)
            queue.insert(0, url)
            break
        name = digest(url.encode("utf-8"))[:28] + ".html"
        (destination / name).write_bytes(raw)
        visited.append(dict(url=url, file=name, sha256=digest(raw),
                            bytes=len(raw), mime=mime, verifiedRuntime=False,
                            aiMcpAllowed=False, verifiedPublisher=False))
        already.add(url)
        parser = Links()
        parser.feed(raw.decode("utf-8", errors="replace"))
        for href in parser.links[:500]:
            new = parse.urljoin(url, href)
            new = parse.urldefrag(new).url
            if url_allowed(new) and new not in already and new not in queue:
                queue.append(new)
    _json_atomic(checkpoint, dict(schema=1, purpose="human-review-only",
        source="DofusDB public site; partnership claimed by user",
        aiMcpAllowed=False, sourceLicenseExceptionVerified=False,
        status=status if queue else "HTML_PAGES_CAPTURED_ONLY_NOT_FULL_SITE",
        visitedPages=len(visited), pending=queue[:MAX_PAGES], captures=visited))
    return status, len(visited)


def normalize_partner_export(source: Path, destination: Path):
    """Operator-approved export is source data, never imported into the AI agent.

    Strictly preserve type/identity. An observed association is not an
    independently certified drop rate, recipe or traversable mine edge.
    """
    if not source.is_file() or source.stat().st_size > 32 * 1024 * 1024:
        raise Refused("MISSING_OR_OVERSIZED_PARTNER_EXPORT")
    raw = source.read_bytes()
    records = json.loads(raw, object_pairs_hook=lambda pairs: _no_duplicates(pairs))
    required = {"schemaVersion", "gameVersion", "items", "sets", "recipes",
                "drops", "resources", "maps", "transitions"}
    if not isinstance(records, dict) or set(records) != required or records["schemaVersion"] != 1:
        raise Refused("UNSUPPORTED_PARTNER_EXPORT_SCHEMA")
    for key, maximum in (("items", 100000), ("sets", 10000),
                         ("recipes", 50000), ("drops", 100000),
                         ("resources", 100000), ("maps", 50000),
                         ("transitions", 200000)):
        val = records[key]
        if not isinstance(val, list) or len(val) > maximum:
            raise Refused("INVALID_ARRAY_" + key)
    if not isinstance(records["gameVersion"], str) or not re.fullmatch(
            r"\d{1,5}(?:\.\d{1,5}){3}", records["gameVersion"]):
        raise Refused("INVALID_GAME_VERSION")
    destination.mkdir(parents=True, exist_ok=True)
    meta = dict(schema=1, exportSha256=digest(raw), exportBytes=len(raw),
        gameVersion=records["gameVersion"], counts={
            k: len(records[k]) for k in required if isinstance(records[k], list)},
        aiMcpAllowed=False, sourceLicenseExceptionVerified=False,
        publisherVerified=False, gameplayVerified=False,
        note="Review of supplied partner export; never infer recipes, rates or map edges")
    _json_atomic(destination / "review-manifest.json", meta)
    return meta


def _no_duplicates(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise Refused("DUPLICATE_JSON_FIELD")
        result[key] = value
    return result


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    crawl = sub.add_parser("crawl-site")
    crawl.add_argument("--output", type=Path, required=True)
    crawl.add_argument("--pages", type=int, default=10)
    crawl.add_argument("--seed", action="append", default=[])
    review = sub.add_parser("review-partner-export")
    review.add_argument("--input", type=Path, required=True)
    review.add_argument("--output", type=Path, required=True)
    args = ap.parse_args()
    try:
        if args.cmd == "crawl-site":
            status, count = site_capture(args.output, args.seed or list(SEEDS), args.pages)
            print(json.dumps(dict(status=status, pages=count, aiMcpAllowed=False)))
        else:
            meta = normalize_partner_export(args.input, args.output)
            print(json.dumps(meta, ensure_ascii=False))
    except (Refused, ValueError, OSError) as ex:
        print(json.dumps(dict(status="BLOCKED", reason=str(ex), aiMcpAllowed=False)))
        raise SystemExit(2)


if __name__ == "__main__":
    main()
