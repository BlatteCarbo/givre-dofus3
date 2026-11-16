import tempfile
import unittest
import json
from pathlib import Path
from collector import url_allowed, normalize_partner_export, Refused


class TestCollector(unittest.TestCase):
    def test_public_database_allowed(self):
        self.assertTrue(url_allowed("https://beta.dofusdb.fr/fr/database/items"))
        self.assertTrue(url_allowed("https://dofusdb.fr/tools/map"))

    def test_cross_origin_denied(self):
        self.assertFalse(url_allowed("https://evil.example/fr/database/items"))
        self.assertFalse(url_allowed("http://dofusdb.fr/tools/map"))
        self.assertFalse(url_allowed("https://api.dofusdb.fr/items"))

    def test_private_routes_denied(self):
        self.assertFalse(url_allowed("https://dofusdb.fr/user/settings"))
        self.assertFalse(url_allowed("https://dofusdb.fr/fr/login"))

    def test_export_preserves_counts_but_ai_denied(self):
        with tempfile.TemporaryDirectory() as tmp:
            raw = Path(tmp) / "partner.json"
            data = dict(schemaVersion=1,gameVersion="3.7.12.12",
                items=[{"id":101}],sets=[{"id":7}],recipes=[],drops=[],
                resources=[],maps=[],transitions=[])
            raw.write_text(json.dumps(data),encoding="utf-8")
            meta = normalize_partner_export(raw, Path(tmp)/"review")
            self.assertEqual(meta["counts"]["items"], 1)
            self.assertFalse(meta["aiMcpAllowed"])

    def test_unknown_schema_denied(self):
        with tempfile.TemporaryDirectory() as tmp:
            p=Path(tmp)/"bad.json"
            p.write_text('{"schemaVersion":999}',encoding="utf-8")
            with self.assertRaises(Refused): normalize_partner_export(p,Path(tmp)/"out")

    def test_duplicate_json_field_denied(self):
        with tempfile.TemporaryDirectory() as tmp:
            p=Path(tmp)/"bad.json"
            p.write_text('{"schemaVersion":1,"schemaVersion":1}',encoding="utf-8")
            with self.assertRaises(Refused): normalize_partner_export(p,Path(tmp)/"out")


if __name__ == "__main__":
    unittest.main()
