"""Offline synthetic fixtures: NO real DofusDB data and NO network."""

import json
from pathlib import Path
import tempfile
import unittest

from scrapy_partner import (CaptureError, CaptureStore, api_page_url,
                            parse_envelope, validate_response_url)


def fixture(total, skip, limit=2):
    rows = [{"id": i, "name": "SYNTHETIC-NOT-DOFUS"}
            for i in range(skip, min(total, skip+limit))]
    return json.dumps({"total": total, "skip": skip, "limit": limit,
                       "data": rows}).encode("utf-8")


class PartnerCaptureTests(unittest.TestCase):
    def test_all_categories_are_explicitly_allowlisted(self):
        for c in ("items", "item-sets", "recipes", "monsters", "map-positions", "interactives",
                  "jobs", "subareas", "areas", "worlds", "map-references", "transport-from-maps",
                  "point-of-interest", "dungeons", "quests", "npcs", "achievements", "mounts", "spells"):
            url = api_page_url(c, 0, 2)
            self.assertTrue(validate_response_url(url, c, 0, 2))

    def test_cross_domain_redirect_query_and_private_routes_refused(self):
        self.assertFalse(validate_response_url("https://malicious.invalid/items?$limit=2&$skip=0", "items", 0, 2))
        self.assertFalse(validate_response_url("http://api.dofusdb.fr/items?$limit=2&$skip=0", "items", 0, 2))
        self.assertFalse(validate_response_url("https://api.dofusdb.fr/private?$limit=2&$skip=0", "items", 0, 2))
        self.assertFalse(validate_response_url("https://api.dofusdb.fr/items?$limit=20&$skip=0", "items", 0, 2))
        with self.assertRaises(CaptureError): api_page_url("users", 0, 2)

    def test_valid_json_and_full_page(self):
        self.assertEqual(parse_envelope(fixture(5, 0), "items", 0, 2)["records"], 2)
        self.assertEqual(parse_envelope(fixture(5, 4), "items", 4, 2)["records"], 1)

    def test_rejects_incomplete_or_falsified_pagination(self):
        with self.assertRaisesRegex(CaptureError, "INCOMPLETE_PAGE"):
            parse_envelope(json.dumps({"total": 5, "skip": 2, "limit": 2,
                                        "data": [{"id": 2}]}).encode(), "items", 2, 2)
        with self.assertRaisesRegex(CaptureError, "PAGINATION_DRIFT"):
            parse_envelope(fixture(5, 2), "items", 4, 2)
        with self.assertRaises(CaptureError):
            parse_envelope(fixture(5, 0), "items", 0, 51)

    def test_json_duplicate_keys_denied(self):
        raw = b'{"total":2,"total":3,"skip":0,"limit":2,"data":[{"id":1},{"id":2}]}'
        with self.assertRaisesRegex(CaptureError, "DUPLICATE_JSON_KEY"):
            parse_envelope(raw, "items", 0, 2)

    def test_missing_identity_and_nonfinite_refused(self):
        for raw in (b'{"total":1,"skip":0,"limit":2,"data":[{"name":"fake"}]}',
                    b'{"total":1,"skip":0,"limit":2,"data":[{"id":NaN}]}'):
            with self.assertRaises(CaptureError): parse_envelope(raw, "items", 0, 2)

    def test_cache_preserves_sha_and_does_not_overwrite(self):
        with tempfile.TemporaryDirectory() as tmp:
            cache = CaptureStore(Path(tmp), 2)
            cache.save("items", 0, fixture(4, 0))
            self.assertEqual(cache.load("items", 0)["total"], 4)
            with self.assertRaisesRegex(CaptureError, "OVERWRITE_REFUSED"):
                cache.save("items", 0, fixture(4, 0))
            page, _ = cache.paths("items", 0)
            page.write_bytes(page.read_bytes() + b' ')
            with self.assertRaisesRegex(CaptureError, "CACHE_HASH_OR_PAGINATION_MISMATCH"):
                cache.load("items", 0)

    def test_resume_checks_every_prior_page_and_reports_coverage(self):
        with tempfile.TemporaryDirectory() as tmp:
            cache = CaptureStore(Path(tmp), 2)
            cache.save("recipes", 0, fixture(5, 0))
            partial = cache.summarize(("recipes",))
            self.assertFalse(partial["complete"])
            self.assertEqual(partial["collections"]["recipes"]["captured"], 2)
            cache.save("recipes", 2, fixture(5, 2))
            cache.save("recipes", 4, fixture(5, 4))
            complete = cache.summarize(("recipes",))
            self.assertTrue(complete["complete"])
            self.assertEqual(complete["collections"]["recipes"]["captured"], 5)
            self.assertFalse(complete["aiMcpAllowed"])

    def test_source_total_drift_is_not_reported_complete(self):
        with tempfile.TemporaryDirectory() as tmp:
            cache = CaptureStore(Path(tmp), 2)
            cache.save("items", 0, fixture(4, 0))
            cache.save("items", 2, fixture(5, 2))
            with self.assertRaisesRegex(CaptureError, "CHANGED_COLLECTION_TOTAL"):
                cache.summarize(("items",))

    def test_duplicate_record_across_pages_is_not_complete(self):
        with tempfile.TemporaryDirectory() as tmp:
            cache = CaptureStore(Path(tmp), 2)
            cache.save("items", 0, fixture(4, 0))
            bad = json.dumps({"total": 4, "skip": 2, "limit": 2,
                              "data": [{"id": 1}, {"id": 3}]}).encode()
            cache.save("items", 2, bad)
            with self.assertRaisesRegex(CaptureError, "DUPLICATE_RECORD_ACROSS_PAGES"):
                cache.summarize(("items",))

    def test_duplicate_business_id_is_legal_when_distinct_document_ids_exist(self):
        with tempfile.TemporaryDirectory() as tmp:
            cache = CaptureStore(Path(tmp), 2)
            a = json.dumps({"total": 4, "skip": 0, "limit": 2,
                            "data": [{"id": 42, "_id": "doc-a"}, {"id": 42, "_id": "doc-b"}]}).encode()
            b = json.dumps({"total": 4, "skip": 2, "limit": 2,
                            "data": [{"id": 42, "_id": "doc-c"}, {"id": 43, "_id": "doc-d"}]}).encode()
            cache.save("transport-from-maps", 0, a)
            cache.save("transport-from-maps", 2, b)
            self.assertTrue(cache.summarize(("transport-from-maps",))["complete"])

    def test_duplicate_document_id_refused_even_if_business_ids_differ(self):
        with tempfile.TemporaryDirectory() as tmp:
            cache = CaptureStore(Path(tmp), 2)
            a = json.dumps({"total": 4, "skip": 0, "limit": 2,
                            "data": [{"id": 4, "_id": "same"}, {"id": 5, "_id": "other"}]}).encode()
            b = json.dumps({"total": 4, "skip": 2, "limit": 2,
                            "data": [{"id": 6, "_id": "same"}, {"id": 7, "_id": "last"}]}).encode()
            cache.save("transport-from-maps", 0, a)
            cache.save("transport-from-maps", 2, b)
            with self.assertRaisesRegex(CaptureError, "DUPLICATE_RECORD_ACROSS_PAGES"):
                cache.summarize(("transport-from-maps",))

    def test_missing_meta_file_causes_hard_failure(self):
        with tempfile.TemporaryDirectory() as tmp:
            cache = CaptureStore(Path(tmp), 2)
            cache.save("items", 0, fixture(2, 0))
            _, meta = cache.paths("items", 0)
            meta.unlink()
            with self.assertRaisesRegex(CaptureError, "PARTIAL_OR_LINKED_CACHE"):
                cache.load("items", 0)


if __name__ == "__main__":
    unittest.main()
