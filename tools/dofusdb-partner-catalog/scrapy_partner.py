#!/usr/bin/env python3
"""Bounded public DofusDB API capture for *local partner review only*.

No authentication, proxy rotation, fingerprint evasion, CAPTCHA solving,
AI/MCP/runtime disclosure or game actions. The operator explicitly asserts
permission for this partner review; site/API usage rights remain their duty.
"""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
from urllib.parse import parse_qs, urlencode, urlsplit


API_HOST = "api.dofusdb.fr"
USER_AGENT = "GivreWork-Partner-Review/1.0"
COLLECTIONS = (
    "items", "item-sets", "recipes", "monsters", "map-positions", "interactives",
    "jobs", "subareas", "areas", "worlds", "map-references", "transport-from-maps",
    "point-of-interest", "dungeons", "quests", "npcs", "achievements", "mounts", "spells",
)
MAX_BYTES = 8 * 1024 * 1024
MAX_TOTAL = 2_000_000
MAX_PAGES_PER_COLLECTION = 20_000
SCHEMA = 1


class CaptureError(ValueError):
    pass


def raw_sha(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def no_duplicate_json_keys(pairs):
    value = {}
    for name, item in pairs:
        if name in value:
            raise CaptureError("DUPLICATE_JSON_KEY")
        value[name] = item
    return value


def parse_envelope(raw: bytes, category: str, skip: int, page_size: int) -> dict:
    if category not in COLLECTIONS or not isinstance(skip, int) or skip < 0:
        raise CaptureError("INVALID_COLLECTION_OR_OFFSET")
    if not 1 <= page_size <= 50 or not 0 < len(raw) <= MAX_BYTES:
        raise CaptureError("INVALID_LIMIT_OR_BYTES")
    try:
        document = json.loads(raw.decode("utf-8"), object_pairs_hook=no_duplicate_json_keys,
                              parse_constant=lambda _: (_ for _ in ()).throw(CaptureError("NONFINITE")))
    except (UnicodeError, json.JSONDecodeError) as exc:
        raise CaptureError("INVALID_JSON") from exc
    if not isinstance(document, dict) or not {"data", "total", "skip", "limit"}.issubset(document):
        raise CaptureError("UNEXPECTED_ENVELOPE")
    count, returned_skip, returned_limit = (document[k] for k in ("total", "skip", "limit"))
    if any(type(x) is not int for x in (count, returned_skip, returned_limit)):
        raise CaptureError("NONINTEGER_PAGINATION")
    if not 0 <= count <= MAX_TOTAL or returned_skip != skip or returned_limit != page_size:
        raise CaptureError("PAGINATION_DRIFT")
    records = document["data"]
    if not isinstance(records, list) or len(records) != min(page_size, max(0, count - skip)):
        raise CaptureError("INCOMPLETE_PAGE")
    for row in records:
        if not isinstance(row, dict) or ("id" not in row and "_id" not in row):
            raise CaptureError("RECORD_IDENTITY_UNKNOWN")
    return {"total": count, "skip": skip, "limit": returned_limit, "records": len(records)}


def api_page_url(category: str, skip: int, page_size: int) -> str:
    if category not in COLLECTIONS or type(skip) is not int or skip < 0 or not 1 <= page_size <= 50:
        raise CaptureError("INVALID_REQUEST")
    return f"https://{API_HOST}/{category}?" + urlencode({"$limit": page_size, "$skip": skip})


def validate_response_url(url: str, category: str, skip: int, page_size: int) -> bool:
    parts = urlsplit(url)
    try:
        params = parse_qs(parts.query, strict_parsing=True)
        return (parts.scheme == "https" and parts.netloc == API_HOST and
                parts.path == "/" + category and not parts.fragment and
                params == {"$limit": [str(page_size)], "$skip": [str(skip)]})
    except ValueError:
        return False


def atomically_write(path: Path, data: bytes) -> None:
    if path.is_symlink():
        raise CaptureError("SYMLINK_OUTPUT_REFUSED")
    temp = path.with_name(path.name + ".new")
    with temp.open("xb") as stream:
        stream.write(data)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temp, path)


class CaptureStore:
    def __init__(self, root: Path, page_size: int):
        self.root = root.resolve()
        self.page_size = page_size
        self.root.mkdir(parents=True, exist_ok=True)

    def paths(self, category: str, skip: int):
        if category not in COLLECTIONS or type(skip) is not int or skip < 0:
            raise CaptureError("INVALID_PATH")
        folder = self.root / category
        folder.mkdir(parents=True, exist_ok=True)
        prefix = f"page-{skip:08d}"
        return folder / (prefix + ".json"), folder / (prefix + ".meta.json")

    def load(self, category: str, skip: int) -> dict | None:
        page_path, meta_path = self.paths(category, skip)
        if not page_path.exists() and not meta_path.exists():
            return None
        if not page_path.is_file() or not meta_path.is_file() or page_path.is_symlink() or meta_path.is_symlink():
            raise CaptureError("PARTIAL_OR_LINKED_CACHE")
        if page_path.stat().st_size > MAX_BYTES or meta_path.stat().st_size > 8192:
            raise CaptureError("CACHE_OVERSIZED")
        raw = page_path.read_bytes()
        try:
            meta = json.loads(meta_path.read_text(encoding="utf-8"), object_pairs_hook=no_duplicate_json_keys)
        except (UnicodeError, json.JSONDecodeError) as exc:
            raise CaptureError("INVALID_CACHE_METADATA") from exc
        check = parse_envelope(raw, category, skip, self.page_size)
        mandatory = {"schema", "category", "skip", "limit", "total", "records", "bytes", "sha256", "sourceUrl"}
        if not isinstance(meta, dict) or not mandatory.issubset(meta):
            raise CaptureError("INVALID_CACHE_METADATA")
        if (meta["schema"] != SCHEMA or meta["category"] != category or
            meta["skip"] != skip or meta["limit"] != self.page_size or
            meta["total"] != check["total"] or meta["records"] != check["records"] or
            meta["bytes"] != len(raw) or meta["sha256"] != raw_sha(raw) or
            not validate_response_url(meta["sourceUrl"], category, skip, self.page_size)):
            raise CaptureError("CACHE_HASH_OR_PAGINATION_MISMATCH")
        return check

    def save(self, category: str, skip: int, raw: bytes) -> dict:
        if self.load(category, skip) is not None:
            raise CaptureError("OVERWRITE_REFUSED")
        check = parse_envelope(raw, category, skip, self.page_size)
        page_path, meta_path = self.paths(category, skip)
        meta = dict(schema=SCHEMA, category=category, skip=skip, limit=self.page_size,
                    total=check["total"], records=check["records"], bytes=len(raw),
                    sha256=raw_sha(raw), sourceUrl=api_page_url(category, skip, self.page_size),
                    observedUtc=datetime.now(timezone.utc).isoformat(),
                    versionStatus="UNKNOWN", aiMcpAllowed=False, runtimeCertified=False)
        atomically_write(page_path, raw)
        atomically_write(meta_path, json.dumps(meta, sort_keys=True, indent=2).encode("utf-8"))
        return check

    def summarize(self, categories: tuple[str, ...], status: str = "REVIEW_ONLY") -> dict:
        results = {}
        for category in categories:
            offset = 0
            total = None
            pages = 0
            seen_ids = set()
            while True:
                page = self.load(category, offset)
                if page is None:
                    break
                if total is None:
                    total = page["total"]
                elif total != page["total"]:
                    raise CaptureError("CHANGED_COLLECTION_TOTAL")
                path, _ = self.paths(category, offset)
                for row in json.loads(path.read_bytes())["data"]:
                    # `id` is a *business identifier*, not necessarily a unique
                    # row (e.g. transport-from-maps has several records per map).
                    # Mongo `_id` is the unique document identity when present.
                    key = row.get("_id", row.get("id"))
                    if type(key) not in (str, int) or len(str(key)) > 128:
                        raise CaptureError("INVALID_RECORD_IDENTITY")
                    if key in seen_ids:
                        raise CaptureError("DUPLICATE_RECORD_ACROSS_PAGES")
                    seen_ids.add(key)
                pages += 1
                offset += page["records"]
                if offset >= total:
                    break
            results[category] = dict(expected=total, captured=offset, pages=pages,
                                     complete=total is not None and offset == total)
        return dict(schema=1, status=status,
                    complete=all(row["complete"] for row in results.values()),
                    source="DofusDB public API - user-asserted partner review",
                    sourceRightsVerifiedIndependently=False,
                    sourceGameVersion="UNKNOWN", aiMcpAllowed=False,
                    authorizedRuntimeUse=False, gameplayCertified=False,
                    collections=results)


def crawl(root: Path, categories: tuple[str, ...], page_size: int,
          max_pages: int, delay: float, partner_review: bool):
    if not partner_review:
        raise CaptureError("EXPLICIT_PARTNER_REVIEW_ACK_REQUIRED")
    if not categories or len(set(categories)) != len(categories) or any(c not in COLLECTIONS for c in categories):
        raise CaptureError("INVALID_COLLECTIONS")
    if not 1 <= page_size <= 50 or not 1 <= max_pages <= MAX_PAGES_PER_COLLECTION:
        raise CaptureError("UNSAFE_PAGINATION")
    if not 1 <= delay <= 60:
        raise CaptureError("DELAY_MUST_BE_AT_LEAST_ONE_SECOND")
    try:
        import scrapy
        from scrapy.crawler import CrawlerProcess
    except ImportError as exc:
        raise CaptureError("SCRAPY_NOT_INSTALLED_IN_THIS_PYTHON") from exc

    store = CaptureStore(root, page_size)

    class ReviewSpider(scrapy.Spider):
        name = "dofusdb_partner_review"
        custom_settings = {
            "USER_AGENT": USER_AGENT,
            "ROBOTSTXT_OBEY": True,
            "DOWNLOAD_DELAY": delay,
            "CONCURRENT_REQUESTS": 1,
            "CONCURRENT_REQUESTS_PER_DOMAIN": 1,
            "AUTOTHROTTLE_ENABLED": True,
            "AUTOTHROTTLE_TARGET_CONCURRENCY": 1.0,
            "DOWNLOAD_TIMEOUT": 20,
            "DOWNLOAD_MAXSIZE": MAX_BYTES,
            "REDIRECT_ENABLED": False,
            "RETRY_ENABLED": False,
            "COOKIES_ENABLED": False,
            "LOG_LEVEL": "ERROR",
            "TELNETCONSOLE_ENABLED": False,
            "DOWNLOAD_WARNSIZE": MAX_BYTES,
        }
        handle_httpstatus_list = [301, 302, 307, 308, 401, 403, 404, 429, 500, 503]

        async def start(self):
            for category in categories:
                try:
                    offset = 0
                    expected = None
                    n = 0
                    while n < max_pages:
                        saved = store.load(category, offset)
                        if saved is None:
                            break
                        if expected is not None and saved["total"] != expected:
                            raise CaptureError("CHANGED_COLLECTION_TOTAL")
                        expected = saved["total"]
                        offset += saved["records"]
                        n += 1
                        if offset >= expected:
                            break
                    if n >= max_pages or expected is not None and offset >= expected:
                        continue
                    url = api_page_url(category, offset, page_size)
                    yield scrapy.Request(url, callback=self.parse_page, errback=self.failed,
                                         meta=dict(collection=category, offset=offset,
                                                   n=n, expected=expected), dont_filter=True)
                except CaptureError as exc:
                    self.fail("CACHED_" + str(exc))
                    return

        def fail(self, reason):
            self.crawler.stats.set_value("capture_error", reason)
            self.crawler.engine.close_spider(self, reason)

        def failed(self, failure):
            self.fail("REQUEST_FAILED_" + type(failure.value).__name__)

        def parse_page(self, response):
            category, offset = response.meta["collection"], response.meta["offset"]
            if response.status != 200:
                self.fail("HTTP_" + str(response.status)); return
            if not validate_response_url(response.url, category, offset, page_size):
                self.fail("REDIRECT_OR_WRONG_URL"); return
            if "application/json" not in response.headers.get("Content-Type", b"").decode("ascii", "ignore"):
                self.fail("UNEXPECTED_MIME"); return
            try:
                result = store.save(category, offset, response.body)
            except CaptureError as exc:
                self.fail(str(exc)); return
            if response.meta["expected"] is not None and result["total"] != response.meta["expected"]:
                self.fail("CHANGED_COLLECTION_TOTAL"); return
            next_offset = offset + result["records"]
            if next_offset < result["total"] and response.meta["n"] + 1 < max_pages:
                yield scrapy.Request(api_page_url(category, next_offset, page_size),
                                     callback=self.parse_page, errback=self.failed,
                                     meta=dict(collection=category, offset=next_offset,
                                               expected=result["total"], n=response.meta["n"]+1),
                                     dont_filter=True)

    runner = CrawlerProcess(settings={"LOG_ENABLED": True, "LOG_LEVEL": "ERROR"})
    active = runner.create_crawler(ReviewSpider)
    runner.crawl(active)
    runner.start()
    failure = active.stats.get_value("capture_error", "REVIEW_ONLY")
    report = store.summarize(categories, failure)
    if not any(row["pages"] for row in report["collections"].values()):
        report["status"] = "NO_PAGES_CAPTURED" if failure == "REVIEW_ONLY" else failure
    elif failure == "REVIEW_ONLY":
        report["status"] = "REVIEW_ONLY_COMPLETE" if report["complete"] else "REVIEW_ONLY_PARTIAL"
    atomically_write(store.root / "review-summary.json",
                     json.dumps(report, sort_keys=True, indent=2).encode("utf-8"))
    return report


def main():
    parser = argparse.ArgumentParser(description="DofusDB review-only partner capture")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--collections", default=",".join(COLLECTIONS))
    parser.add_argument("--page-size", type=int, default=20)
    parser.add_argument("--max-pages-per-collection", type=int, default=2)
    parser.add_argument("--delay", type=float, default=1.0)
    parser.add_argument("--partner-review", action="store_true",
                        help="Explicitly assert authorized partner collection for offline review; not AI disclosure")
    args = parser.parse_args()
    try:
        result = crawl(args.output, tuple(args.collections.split(",")), args.page_size,
                       args.max_pages_per_collection, args.delay, args.partner_review)
        print(json.dumps(result, ensure_ascii=False, sort_keys=True))
    except (CaptureError, OSError, RuntimeError) as exc:
        print(json.dumps({"status": "BLOCKED", "reason": str(exc), "aiMcpAllowed": False}))
        raise SystemExit(2) from exc


if __name__ == "__main__":
    main()
