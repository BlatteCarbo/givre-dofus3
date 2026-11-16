import copy
import json
from pathlib import Path
import tempfile
import unittest

import importer


FIXTURE = Path(__file__).with_name("fixtures") / "synthetic_partner_export.json"
PAGE = "https://dofusdb.fr/tools/map"
ROBOTS = "https://dofusdb.fr/robots.txt"


class FakeClock:
    def __init__(self):
        self.t = 10
        self.sleeps = []

    def now(self):
        return self.t

    def sleep(self, seconds):
        self.sleeps.append(seconds)
        self.t += seconds


class FakeTransport:
    def __init__(self, responses):
        self.responses = {key: list(value) for key, value in responses.items()}
        self.calls = []

    def __call__(self, url):
        self.calls.append(url)
        if url not in self.responses or not self.responses[url]:
            raise AssertionError(f"Unexpected network request {url}")
        return self.responses[url].pop(0)


def response(status, body=b"", content_type="text/plain", **headers):
    return status, {"content-type": content_type, **headers}, body


def export_dict():
    return json.loads(FIXTURE.read_text(encoding="utf-8"))


class InputNormalizationTests(unittest.TestCase):
    def test_synthetic_explicit_edges_and_unknown_are_separate(self):
        raw = FIXTURE.read_bytes()
        result, prov = importer.normalize(raw, "3.7.12.12", PAGE)
        self.assertEqual(len(result["maps"]), 2)
        self.assertEqual(result["maps"][0]["position"]["y"], -7)
        self.assertEqual(result["maps"][1]["floor"], -1)
        self.assertEqual(result["maps"][0]["worldId"], 1)
        self.assertEqual(result["maps"][0]["worldMapId"], 1)
        edge = result["maps"][0]["transitions"][0]
        self.assertEqual(edge["toMapId"], 9002)
        self.assertTrue(edge["targetInExport"])
        self.assertEqual(edge["verification"], "EXPLICIT_IN_UNVERIFIED_EXPORT")
        self.assertEqual(result["maps"][0]["transitions"][1]["verification"], "UNKNOWN_TARGET")
        self.assertNotIn("targetInExport", result["maps"][0]["transitions"][1])
        self.assertEqual(result["mcpAiAccess"], "BLOCKED_PENDING_CONTRACT_EXCEPTION")
        self.assertFalse(result["runtimeVerified"])
        self.assertEqual(prov["sourceCaptureSha256"], importer.sha256(raw))

    def test_manifest_sha_and_no_runtime_authority(self):
        with tempfile.TemporaryDirectory() as tmp:
            destination = Path(tmp) / "output"
            manifest = importer.offline_import(FIXTURE, destination, "3.7.12.12", PAGE)
            payload = (destination / "maps.normalized.json").read_bytes()
            self.assertEqual(manifest["normalized"]["sha256"], importer.sha256(payload))
            self.assertEqual((destination / "partner-source-capture.json").read_bytes(),
                             FIXTURE.read_bytes())
            self.assertFalse(manifest["partnerExceptionVerified"])
            self.assertFalse(manifest["allowedForAiMcpOrGameplay"])
            self.assertEqual(manifest["status"], "UNVERIFIED")
            self.assertEqual(json.loads((destination / "manifest.json").read_bytes()), manifest)
            with self.assertRaises(importer.ImportFailure):
                importer.offline_import(FIXTURE, destination, "3.7.12.12", PAGE)

    def test_invalid_duplicate_and_unbounded_schema(self):
        base = export_dict()
        cases = [
            {"maps": [base["maps"][0], base["maps"][0]]},
            {"maps": [dict(base["maps"][0], transitions=[{"toMapId": 5, "kind": "teleport-guess"}])]},
            {"maps": [dict(base["maps"][0], worldMapId=True)]},
            {"maps": [dict(base["maps"][0], position={"x": 2})]},
            {"maps": [dict(base["maps"][0], resources=[{"resourceId": 1, "cellId": 3, "secret": "x"}])]},
            {"maps": [dict(base["maps"][0], resources=[{"resourceId": 1}] * 513)]},
            {"schemaVersion": 2},
            {"exportKind": "dofusdb-api-dump"},
            {"gameVersion": "3.7.10.10"},
        ]
        for change in cases:
            with self.subTest(change=list(change)):
                candidate = dict(base, **change)
                with self.assertRaises(importer.ImportFailure):
                    importer.normalize(json.dumps(candidate).encode(), "3.7.12.12", PAGE)
        with self.assertRaises(importer.ImportFailure):
            importer.normalize(b'{"a": 1, "a": 2}', "3.7.12.12", PAGE)

    def test_target_outside_captured_set_cannot_be_promoted(self):
        candidate = export_dict()
        candidate["maps"][0]["transitions"][0]["toMapId"] = 999999
        payload, _ = importer.normalize(json.dumps(candidate).encode(), "3.7.12.12", PAGE)
        self.assertFalse(payload["maps"][0]["transitions"][0]["targetInExport"])
        self.assertEqual(payload["maps"][0]["transitions"][0]["verification"],
                         "EXPLICIT_TARGET_NOT_IN_EXPORT")

    def test_observed_feathers_page_schema_without_inferred_routes(self):
        source = {
            "total": 7, "limit": 1, "skip": 0,
            "data": [{"id": 1234567, "posX": -4, "posY": 9, "subAreaId": 55,
                      "worldMap": 1, "isTransition": True,
                      "unknownProviderColumn": {"irrelevant": "not a route"}}],
        }
        result, meta = importer.normalize_map_positions_sample(
            json.dumps(source).encode(), "3.7.12.12")
        entry = result["maps"][0]
        self.assertEqual(entry["mapId"], 1234567)
        self.assertEqual(entry["position"], {"x": -4, "y": 9})
        self.assertEqual(entry["worldMapId"], 1)
        self.assertTrue(entry["siteTransitionFlag"])
        self.assertIsNone(entry["worldId"])
        self.assertIsNone(entry["layer"])
        self.assertIsNone(entry["floor"])
        self.assertEqual(entry["transitions"], [])
        self.assertEqual(entry["zaaps"], [])
        self.assertEqual(entry["resources"], [])
        self.assertEqual(result["routeEligibility"], "UNKNOWN_NO_EDGES_IN_PAGE")
        self.assertFalse(result["publisherVerified"])
        self.assertTrue(meta["declaredGameVersionNotFromSource"])

    def test_observed_feathers_schema_negative_cases(self):
        data = {"total": 1, "limit": 1, "skip": 0, "data": [
            {"id": 123, "posX": 2, "posY": -1, "subAreaId": 2, "worldMap": 1}]}
        cases = [
            {"limit": 101},
            {"data": [data["data"][0], data["data"][0]]},
            {"data": [dict(data["data"][0], id=True)]},
            {"data": [dict(data["data"][0], worldMap="unknown")]},
            {"data": [dict(data["data"][0], posX=None)]},
            {"data": [dict(data["data"][0], isTransition=1)]},
            {"data": [dict(data["data"][0], extra1=1, **{f"extra{i}": i for i in range(2, 100)})]},
        ]
        for change in cases:
            with self.subTest(change=list(change)):
                with self.assertRaises(importer.ImportFailure):
                    importer.normalize_map_positions_sample(
                        json.dumps(dict(data, **change)).encode(), "3.7.12.12")

    def test_real_schema_edge_values_are_preserved_without_promotion(self):
        obj = {"total": 1, "limit": 1, "skip": 0,
               "data": [{"id": 0, "posX": 0, "posY": 0, "subAreaId": 1,
                         "worldMap": -1, "isTransition": False}]}
        data, _ = importer.normalize_map_positions_sample(
            json.dumps(obj).encode(), "3.7.12.12")
        record = data["maps"][0]
        self.assertEqual(record["mapId"], 0)
        self.assertEqual(record["worldMapId"], -1)
        self.assertEqual(record["verification"], "UNVERIFIED_ZERO_ID")
        self.assertEqual(record["transitions"], [])

    def test_allowlisted_urls_only(self):
        valid = "https://beta.dofusdb.fr/almanax/tools/map?page=2"
        self.assertEqual(importer.canonical_url(valid), valid)
        for url in [
            "http://dofusdb.fr/tools/map", "https://dofusdb.fr/other",
            "https://api.dofusdb.fr/maps", "https://evil.example/tools/map",
            "https://dofusdb.fr@evil.example/tools/map",
            "https://dofusdb.fr:8443/tools/map",
            "https://dofusdb.fr/tools/map#frag", "https://dofusdb.fr/tools/map?token=abc%0Asecret",
        ]:
            with self.subTest(url=url), self.assertRaises(importer.ImportFailure):
                importer.canonical_url(url)


class SiteCollectorTests(unittest.TestCase):
    def test_robots_disallow_refuses_all_page_requests(self):
        transport = FakeTransport({
            ROBOTS: [response(200, b"User-agent: ChatGPT-User\nDisallow: /\n")],
        })
        http = importer.HttpClient(clock=FakeClock(), transport=transport)
        with tempfile.TemporaryDirectory() as tmp:
            status = importer.probe_site(PAGE, Path(tmp), http=http)
            self.assertEqual(status["status"], "BLOCKED_ROBOTS")
            self.assertEqual(transport.calls, [ROBOTS])
            self.assertEqual(status["pages"], [])
            self.assertTrue((Path(tmp) / "checkpoint.json").exists())

    def test_unknown_robots_fails_closed(self):
        transport = FakeTransport({ROBOTS: [response(404)]})
        with tempfile.TemporaryDirectory() as tmp:
            status = importer.probe_site(PAGE, Path(tmp),
                                         http=importer.HttpClient(clock=FakeClock(), transport=transport))
        self.assertEqual(status["status"], "BLOCKED_HTTP_STATUS_404")
        self.assertEqual(transport.calls, [ROBOTS])

    def test_single_page_html_and_bounded_next(self):
        url2 = PAGE + "?page=2"
        transport = FakeTransport({
            ROBOTS: [response(200, b"User-agent: ChatGPT-User\nAllow: /tools/map\n")],
            PAGE: [response(200, b'<a rel="next" href="?page=2">next</a>', "text/html")],
            url2: [response(200, b"html snapshot", "text/html")],
        })
        clock = FakeClock()
        with tempfile.TemporaryDirectory() as tmp:
            status = importer.probe_site(PAGE, Path(tmp), max_pages=2,
                                         http=importer.HttpClient(clock=clock, transport=transport))
            self.assertEqual(status["status"], "UNVERIFIED_STATIC_HTML")
            self.assertEqual(len(status["pages"]), 2)
            self.assertEqual(transport.calls, [ROBOTS, PAGE, url2])
            self.assertGreaterEqual(sum(clock.sleeps), 2)

    def test_retries_only_short_429_and_never_403(self):
        clock = FakeClock()
        transport = FakeTransport({
            PAGE: [response(429, b"", **{"Retry-After": "2"}),
                   response(200, b"ok", "text/html")],
        })
        status, result = importer.bounded_get(importer.HttpClient(clock, transport), PAGE)
        self.assertEqual(status, "OK")
        self.assertEqual(result[1], b"ok")
        self.assertGreaterEqual(sum(clock.sleeps), 2)
        forbidden = FakeTransport({PAGE: [response(403)]})
        self.assertEqual(importer.bounded_get(importer.HttpClient(FakeClock(), forbidden), PAGE)[0],
                         "BLOCKED_HTTP_403")
        self.assertEqual(len(forbidden.calls), 1)
        long = FakeTransport({PAGE: [response(429, b"", **{"Retry-After": "500"})]})
        self.assertEqual(importer.bounded_get(importer.HttpClient(FakeClock(), long), PAGE)[0],
                         "RETRY_LATER_429")
        self.assertEqual(len(long.calls), 1)

    def test_captcha_and_redirect_stop(self):
        for code, content, expected in [
            (200, b"<div>CAPTCHA challenge</div>", "BLOCKED_CAPTCHA"),
            (302, b"", "BLOCKED_HTTP_STATUS_302"),
        ]:
            transport = FakeTransport({PAGE: [response(code, content)]})
            status, _ = importer.bounded_get(importer.HttpClient(FakeClock(), transport), PAGE)
            self.assertEqual(status, expected)
            self.assertEqual(len(transport.calls), 1)

    def test_checkpoint_resume_without_refetch_and_tamper_rejected(self):
        second = PAGE + "?page=2"
        robots_allowed = response(200, b"User-agent: ChatGPT-User\nAllow: /tools/map\n")
        first_transport = FakeTransport({
            ROBOTS: [robots_allowed],
            PAGE: [response(200, b'<a href="?page=2" rel="next">more</a>', "text/html")],
        })
        with tempfile.TemporaryDirectory() as tmp:
            dest = Path(tmp) / "probe"
            a = importer.probe_site(PAGE, dest, max_pages=1, http=importer.HttpClient(FakeClock(), first_transport))
            self.assertEqual(a["status"], "UNVERIFIED_PARTIAL")
            self.assertEqual(a["pendingUrls"], [second])
            self.assertEqual(len(a["pages"]), 1)
            resume_transport = FakeTransport({
                ROBOTS: [robots_allowed],
                second: [response(200, b"<p>second page</p>", "text/html")],
            })
            b = importer.probe_site(PAGE, dest, max_pages=2, resume=True,
                                    http=importer.HttpClient(FakeClock(), resume_transport))
            self.assertEqual(b["status"], "UNVERIFIED_STATIC_HTML")
            self.assertEqual(resume_transport.calls, [ROBOTS, second])
            self.assertEqual(len(b["pages"]), 2)
            with self.assertRaises(importer.ImportFailure):
                importer.probe_site(PAGE, dest, max_pages=3, resume=True)

    def test_checkpoint_hash_tamper_fails_before_network(self):
        robots_allowed = response(200, b"User-agent: ChatGPT-User\nAllow: /tools/map\n")
        transport = FakeTransport({
            ROBOTS: [robots_allowed],
            PAGE: [response(200, b'<a href="?page=2" rel="next">more</a>', "text/html")],
        })
        with tempfile.TemporaryDirectory() as tmp:
            dest = Path(tmp) / "probe"
            importer.probe_site(PAGE, dest, max_pages=1,
                                http=importer.HttpClient(FakeClock(), transport))
            (dest / "captures" / "page-000.html").write_bytes(b"tampered")
            no_network = FakeTransport({})
            with self.assertRaises(importer.ImportFailure):
                importer.probe_site(PAGE, dest, max_pages=2, resume=True,
                                    http=importer.HttpClient(FakeClock(), no_network))
            self.assertEqual(no_network.calls, [])


if __name__ == "__main__":
    unittest.main()
